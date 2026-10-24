using System;

namespace Swrp;

/// <summary>
/// The camera's feel: head bob, strafe lean, look inertia, breathing and landing impact in first person; screen shake
/// (<see cref="SwrpShake"/>) in both views; a short fly-in down to your trooper when you deploy from the character screen;
/// and a slow drift up and away from your body when you die. Runs only on the local player's own camera after the
/// PlayerController has placed it, so it never touches physics or aim. <c>swrp_cinematic 0</c> turns all of it off.
/// </summary>
public sealed class CinematicCamera : Component
{
	[ConVar( "swrp_cinematic", Help = "Cinematic camera: head bob, sway, shake and ringing, the deploy fly-in and the death drift (1 = on, 0 = off)", Saved = true )]
	public static bool SwayEnabled { get; set; } = true;

	/// <summary> The character screen's Deploy button was pressed (the next spawn of your trooper flies in). </summary>
	static RealTimeSince _sinceDeployClicked = 100f;
	public static void MarkDeploy() { _sinceDeployClicked = 0f; Feel.FeelTransition.Deploy(); }     // fade through black (Code/Feel)

	const float FlyTime = 2.4f;
	float _flyT = -1f;
	Vector3 _flyFrom;
	Rotation _flyFromRot;

	float _deathT = -1f;
	Vector3 _deathEye, _deathFocus;
	float _deathYaw;
	SwrpVitals _vitals;

	PlayerController _controller;
	PlayerController Controller => _controller ??= GetComponent<PlayerController>();

	[Property, Range( 0, 3 )] public float BobAmount { get; set; } = 1f;
	[Property, Range( 0, 3 )] public float SwayAmount { get; set; } = 1f;

	float _bobPhase;
	float _bobBlend;
	float _lean;
	float _yawLag;
	float _pitchLag;
	float _landDip;
	float _landVelocity;
	Angles _lastEye;
	bool _haveLastEye;

	bool _reportedError;
	bool _wasOnGround = true;
	float _lastFallSpeed;

	// The PlayerController positions the camera during the frame's updates; OnPreRender runs after all of them,
	// so we nudge it last and the next frame starts from a clean camera again.
	protected override void OnStart()
	{
		// a fresh trooper straight off the character screen: fly in to them
		if ( !IsProxy && _sinceDeployClicked < 20f )
		{
			_sinceDeployClicked = 100f;
			_flyT = 0f;
		}
	}

	protected override void OnPreRender()
	{
		try
		{
			if ( IsProxy ) return;
			var cam = Scene.Camera;
			if ( !SwayEnabled || !cam.IsValid() || SwrpUiState.SpectateSteamId != 0 )
			{
				_flyT = -1f;
				return;
			}
			_vitals ??= GetComponent<SwrpVitals>();
			if ( _vitals is { IsDead: true } )
			{
				DeathDrift( cam );
				return;
			}
			_deathT = -1f;
			ApplySway( cam );
			FlyIn( cam );
			SwrpShake.Apply( cam );
		}
		catch ( Exception e )
		{
			if ( _reportedError ) return;
			_reportedError = true;
			Log.Warning( $"[SWRP] CinematicCamera: {e}" );
		}
	}

	/// <summary> From high behind the trooper down into their eyes (or their third person view), easing in. </summary>
	void FlyIn( CameraComponent cam )
	{
		if ( _flyT < 0f ) return;
		var target = cam.WorldPosition;
		var targetRot = cam.WorldRotation;
		if ( _flyT == 0f )
		{
			// start up and back over the shoulder, pulled in short of any wall
			var back = Rotation.FromYaw( targetRot.Yaw() );
			var want = target + back.Backward * 420f + back.Right * 140f + Vector3.Up * 240f;
			var tr = Scene.Trace.Ray( target, want ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player" ).Radius( 8f ).Run();
			_flyFrom = tr.Hit ? tr.HitPosition + tr.Normal * 12f : want;
			_flyFromRot = Rotation.LookAt( target - _flyFrom, Vector3.Up );
		}
		_flyT += Time.Delta;
		var k = Math.Clamp( _flyT / FlyTime, 0f, 1f );
		var e = k * k * k * ( k * ( k * 6f - 15f ) + 10f );          // smoother step
		cam.WorldPosition = Vector3.Lerp( _flyFrom, target, e );
		cam.WorldRotation = Rotation.Slerp( _flyFromRot, targetRot, MathF.Min( 1f, e * 1.15f ) );
		if ( k >= 1f ) _flyT = -1f;
	}

	/// <summary> Dead: the view lifts slowly off the body and drifts round it, looking down. </summary>
	void DeathDrift( CameraComponent cam )
	{
		if ( _deathT < 0f )
		{
			_deathT = 0f;
			_deathEye = cam.WorldPosition;
			_deathYaw = cam.WorldRotation.Yaw();
			_deathFocus = _vitals.WorldPosition + Vector3.Up * 16f;
		}
		_deathT += Time.Delta;
		var k = Math.Clamp( _deathT / 7f, 0f, 1f );
		k = k * k * ( 3f - 2f * k );
		var yaw = _deathYaw + 180f + _deathT * 4f;
		var orbit = _deathFocus + Rotation.FromYaw( yaw ).Forward * ( 70f + 110f * k ) + Vector3.Up * ( 60f + 150f * k );
		var want = Vector3.Lerp( _deathEye, orbit, k );
		var tr = Scene.Trace.Ray( _deathFocus + Vector3.Up * 20f, want ).WithoutTags( "trigger", "player" ).Radius( 6f ).Run();
		var pos = tr.Hit ? tr.HitPosition + tr.Normal * 8f : want;
		cam.WorldPosition = pos;
		cam.WorldRotation = Rotation.Slerp( cam.WorldRotation, Rotation.LookAt( _deathFocus - pos, Vector3.Up ), MathF.Min( 1f, 0.25f + k ) );
	}

	void ApplySway( CameraComponent cam )
	{
		if ( IsProxy || !SwayEnabled || !cam.IsValid() || SwrpUiState.SpectateSteamId != 0 ) return;
		if ( !Controller.IsValid() || Controller.ThirdPerson ) return;

		var dt = Math.Min( Time.Delta, 0.05f );

		var velocity = Controller.Velocity;
		var flat = velocity.WithZ( 0 );

		// Landing: remember how fast we were falling, and kick the camera down the frame we touch ground.
		if ( !Controller.IsOnGround ) _lastFallSpeed = velocity.z;
		else if ( !_wasOnGround && _lastFallSpeed < -150f )
			_landVelocity -= Math.Clamp( -_lastFallSpeed * 0.012f, 1.5f, 14f ) * 28f;
		_wasOnGround = Controller.IsOnGround;
		var speed = flat.Length;
		var eye = Controller.EyeAngles;

		// ----- Head bob: fades in with speed, only while on the ground -----
		var target = Controller.IsOnGround ? Math.Clamp( speed / 280f, 0f, 1f ) : 0f;
		_bobBlend = _bobBlend.LerpTo( target, dt * 8f );
		_bobPhase += dt * ( 6.5f + speed * 0.014f );

		var bobUp = MathF.Sin( _bobPhase * 2f ) * 0.9f * _bobBlend * BobAmount;
		var bobSide = MathF.Cos( _bobPhase ) * 0.7f * _bobBlend * BobAmount;
		var bobRoll = MathF.Cos( _bobPhase ) * 0.35f * _bobBlend * BobAmount;
		var bobPitch = MathF.Sin( _bobPhase * 2f ) * 0.18f * _bobBlend * BobAmount;

		// ----- Breathing when standing still -----
		var idle = 1f - _bobBlend;
		var breathe = MathF.Sin( Time.Now * 1.4f ) * idle;
		var breatheUp = breathe * 0.25f;
		var breathePitch = breathe * 0.06f;

		// ----- Strafe lean: roll into the direction we're moving sideways -----
		var rightSpeed = Vector3.Dot( flat, Rotation.FromYaw( eye.yaw ).Right );
		_lean = _lean.LerpTo( Math.Clamp( rightSpeed / 320f, -1f, 1f ) * 1.6f * SwayAmount, dt * 6f );

		// ----- Look inertia: the view lags a little behind quick turns, then settles -----
		if ( !_haveLastEye ) { _lastEye = eye; _haveLastEye = true; }
		var yawDelta = MathX.DeltaDegrees( _lastEye.yaw, eye.yaw );
		var pitchDelta = MathX.DeltaDegrees( _lastEye.pitch, eye.pitch );
		_lastEye = eye;

		_yawLag = ( _yawLag + Math.Clamp( yawDelta, -25f, 25f ) * 0.25f * SwayAmount ).LerpTo( 0f, dt * 7f );
		_pitchLag = ( _pitchLag + Math.Clamp( pitchDelta, -25f, 25f ) * 0.25f * SwayAmount ).LerpTo( 0f, dt * 7f );

		// ----- Landing impact: spring back to zero -----
		_landVelocity += -_landDip * 220f * dt;
		_landVelocity *= MathF.Max( 0f, 1f - 9f * dt );
		_landDip += _landVelocity * dt;

		var offset = new Vector3( 0f, -bobSide, bobUp + breatheUp + _landDip );
		var tilt = new Angles( bobPitch + breathePitch - _landDip * 0.25f + _pitchLag * 0.5f, -_yawLag * 0.4f, bobRoll - _lean - _yawLag * 0.35f );

		cam.WorldPosition += cam.WorldRotation * offset;
		cam.WorldRotation = cam.WorldRotation * tilt.ToRotation();
	}
}

/// <summary>
/// Camera shake for the local view: short kicks (<see cref="Add"/>, <see cref="AddAt"/> for explosions and heavy hits) that
/// die away, and a sustained <see cref="Rumble"/> (the hyperspace jump). Applied by <see cref="CinematicCamera"/> in first
/// and third person; swrp_cinematic 0 turns it off.
/// </summary>
public static class SwrpShake
{
	static float _trauma;
	static float _seed = 13.7f;

	/// <summary> A steady shudder, 0..1, held for as long as it is set. </summary>
	public static float Rumble { get; set; }

	/// <summary> The current shake, 0..1 (the muffled ringing and the HUD read it). </summary>
	public static float Amount => Math.Clamp( _trauma * _trauma + Rumble, 0f, 1f );

	public static void Add( float amount ) => _trauma = Math.Clamp( _trauma + amount, 0f, 1f );

	/// <summary> Ears ringing after a blast or a heavy hit, 0..1: the world goes muffled and a high tone rings. </summary>
	public static float Ringing { get; private set; }
	static Sandbox.Audio.LowPassProcessor _muffle;

	public static void Ring( float amount )
	{
		if ( !CinematicCamera.SwayEnabled ) return;
		var was = Ringing;
		Ringing = Math.Clamp( Ringing + amount, 0f, 1f );
		if ( was < 0.25f )
		{
			var h = Sound.Play( SwrpAudio.EarRing );
			if ( h is not null ) h.Volume *= Math.Clamp( amount * 1.3f, 0.35f, 1f );
		}
	}

	/// <summary> A blast at <paramref name="pos"/>: shakes and, close enough, rings the local player's ears. </summary>
	public static void Blast( Vector3 pos, float power, float radius )
	{
		AddAt( pos, power, radius );
		var cam = Game.ActiveScene?.Camera;
		if ( cam is null ) return;
		var d = cam.WorldPosition.Distance( pos );
		if ( d < radius * 0.5f ) Ring( power * ( 1f - d / ( radius * 0.5f ) ) );
	}

	[ConCmd( "swrp_ring", Help = "swrp_ring [0-1] - your view: test the ringing ears and muffled sound of a heavy hit" )]
	public static void RingCommand( float amount = 0.8f )
	{
		Ring( amount );
		Add( amount * 0.6f );
		Log.Info( $"[SWRP] ringing {Ringing:0.00}" );
	}

	/// <summary> Every frame (from the camera's post-processing): fade the ringing and the muffling with it. </summary>
	internal static void UpdateEars( float dt )
	{
		Ringing = MathF.Max( 0f, Ringing - dt / 3.4f );
		var mixer = Sandbox.Audio.Mixer.Default;
		if ( mixer is null ) return;
		// ringing ears, or under the bacta in a pod
		var muffle = MathF.Max( CinematicCamera.SwayEnabled ? MathF.Min( 1f, Ringing * 1.4f ) : 0f, SwrpBactaPod.LocalSubmerged * 0.75f );
		if ( muffle > 0.01f )
		{
			if ( _muffle is null )
			{
				_muffle = new Sandbox.Audio.LowPassProcessor();
				mixer.AddProcessor( _muffle );
			}
			_muffle.Cutoff = MathX.Lerp( 0.6f, 0.04f, muffle );
		}
		else if ( _muffle is not null )
		{
			mixer.RemoveProcessor( _muffle );
			_muffle = null;
		}
	}

	/// <summary> A kick from something at <paramref name="pos"/>, fading out to nothing at <paramref name="radius"/> from the camera. </summary>
	public static void AddAt( Vector3 pos, float amount, float radius )
	{
		var cam = Game.ActiveScene?.Camera;
		if ( cam is null ) return;
		var d = cam.WorldPosition.Distance( pos );
		if ( d >= radius ) return;
		var k = 1f - d / radius;
		Add( amount * k * k );
	}

	internal static void Apply( CameraComponent cam )
	{
		if ( !cam.IsValid() ) return;
		var dt = Math.Min( Time.Delta, 0.05f );
		_trauma = MathF.Max( 0f, _trauma - dt * 1.1f );
		var s = Amount;
		if ( s <= 0.001f ) return;
		_seed += dt * ( 14f + 10f * s );
		float N( float o ) => MathF.Sin( _seed * 1.7f + o ) * 0.6f + MathF.Sin( _seed * 3.1f + o * 2.3f ) * 0.3f + MathF.Sin( _seed * 7.3f + o * 4.1f ) * 0.1f;
		var tilt = new Angles( N( 0f ) * 2.2f * s, N( 10f ) * 1.6f * s, N( 20f ) * 2.6f * s );
		cam.WorldPosition += cam.WorldRotation * new Vector3( 0f, N( 30f ) * 1.5f * s, N( 40f ) * 1.5f * s );
		cam.WorldRotation = cam.WorldRotation * tilt.ToRotation();
	}
}
