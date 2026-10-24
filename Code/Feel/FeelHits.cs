using System;

namespace Swrp.Feel;

/// <summary>
/// Hit reactions on your own view, on top of what SwrpVitals and the camera already do (the random shake, ringing ears, the
/// HUD's hit marker and damage direction arrow):
/// <list type="bullet">
/// <item>a flinch: the view kicks up and rolls away from the side the hit came from, then springs back
/// (<c>swrp_feel_kick</c>);</item>
/// <item>a red flash round the screen edges, pale blue for a stun, and a short blur on a heavy hit (30+); a slow red pulse
/// in time with the heartbeat while you're at 30 health or less (<c>swrp_feel_hitfx</c>; drawn by
/// <see cref="FeelHitOverlay"/>);</item>
/// <item>bodies thrown by the shot that killed them: every machine remembers recent bolt hits on flesh, and a fresh corpse
/// next to one gets a push along the bolt (<c>swrp_feel_ragdoll</c>).</item>
/// </list>
/// </summary>
public static class FeelHits
{
	/// <summary> Damage at which a hit counts as heavy (blur, thump; SwrpVitals rings your ears from the same number). </summary>
	public const float Heavy = 30f;
	/// <summary> Health at and under which the low-health pulse runs (the heartbeat in SwrpVitals uses the same). </summary>
	public const float LowHealthAt = 30f;

	/// <summary> 0..1 the red (or stun blue) edge flash now. </summary>
	public static float Flash { get; private set; }
	/// <summary> True when the last flash was a stun. </summary>
	public static bool FlashStun { get; private set; }
	/// <summary> 0..1 the heavy hit blur now. </summary>
	public static float Blur { get; private set; }
	/// <summary> 0..1 the low-health pulse now (already shaped to the heartbeat). </summary>
	public static float LowHealth { get; private set; }
	/// <summary> Dev: seconds of fake low health left (swrp_feel_test lowhp). </summary>
	internal static float TestLowHealth { get; set; }

	// the flinch: pitch (up), roll and yaw offsets in degrees with their velocities
	static float _pitch, _pitchV, _roll, _rollV, _yaw, _yawV;
	static float _beat;

	/// <summary>
	/// Your own trooper was hit (hook: SwrpVitals.NotifyHurt, on the owner). <paramref name="from"/> is where the hit came
	/// from, <paramref name="amount"/> the damage.
	/// </summary>
	public static void Hurt( Vector3 from, float amount, bool stun )
	{
		if ( !Feel.On ) return;
		var heavy = !stun && amount >= Heavy;
		var k = stun ? 0.45f : Math.Clamp( amount / 40f, 0.25f, 1f );

		if ( Feel.HitFx )
		{
			Flash = MathF.Max( Flash, stun ? 0.7f : MathX.Lerp( 0.35f, 1f, k ) );
			FlashStun = stun;
			if ( heavy ) Blur = 1f;
		}
		if ( heavy && Feel.Sounds ) FeelSounds.PlayUi( FeelSounds.HitThump );

		var cam = Game.ActiveScene?.Camera;
		if ( !Feel.Kick || !cam.IsValid() ) return;
		// which side it came from, in the camera's frame: hits from the right roll the head left, and so on
		var dir = from - cam.WorldPosition;
		var right = dir.IsNearlyZero() ? 0f : Vector3.Dot( dir.Normal, cam.WorldRotation.Right );
		var front = dir.IsNearlyZero() ? 1f : Vector3.Dot( dir.Normal, cam.WorldRotation.Forward );
		_pitchV -= ( 90f + 60f * MathF.Max( 0f, front ) ) * k;             // head snaps up (negative pitch looks up)
		_rollV -= right * 160f * k;
		_yawV -= right * 70f * k;
	}

	// ----- ragdoll push -----

	struct Shot { public Vector3 Pos, Dir; public RealTimeSince Age; }
	// not readonly: a code hotload can leave a list of structs broken (it then throws on every use), and Tick swaps in new ones
	static List<Shot> _shots = new();
	static HashSet<GameObject> _seenCorpses = new();
	static List<(GameObject Go, Vector3 Push, Vector3 At, int Tries)> _pending = new();

	/// <summary> A bolt hit a body (from SwrpFx.Impact on every machine): remembered for a second in case it was the kill. </summary>
	internal static void FleshHit( Vector3 pos, Vector3 normal )
	{
		if ( !Feel.Ragdoll ) return;
		_shots.Add( new Shot { Pos = pos, Dir = -normal, Age = 0 } );
		if ( _shots.Count > 32 ) _shots.RemoveAt( 0 );
	}

	static void TickRagdolls( Scene scene )
	{
		_shots.RemoveAll( s => s.Age > 1.2f );
		if ( !Feel.Ragdoll ) { _pending.Clear(); return; }

		foreach ( var corpse in scene.GetAllComponents<CorpseFade>() )
		{
			var go = corpse.GameObject;
			if ( !_seenCorpses.Add( go ) ) continue;
			// the most recent hit close to the body
			var center = go.WorldPosition + Vector3.Up * 36f;
			for ( var i = _shots.Count - 1; i >= 0; i-- )
			{
				if ( _shots[i].Pos.DistanceSquared( center ) > 90f * 90f ) continue;
				var push = ( _shots[i].Dir.WithZ( 0f ).Normal * 230f + Vector3.Up * 70f );
				_pending.Add( ( go, push, _shots[i].Pos, 0 ) );
				_shots.RemoveAt( i );
				break;
			}
		}
		if ( _seenCorpses.Count > 64 ) _seenCorpses.RemoveWhere( g => !g.IsValid() );

		// the ragdoll's bodies appear a frame or two after the corpse does: try for a few frames
		for ( var i = _pending.Count - 1; i >= 0; i-- )
		{
			var (go, push, hitAt, tries) = _pending[i];
			if ( !go.IsValid() || tries > 10 ) { _pending.RemoveAt( i ); continue; }
			var bodies = go.Components.GetAll<Rigidbody>( FindMode.EverythingInSelfAndDescendants ).ToList();
			var pushed = 0;
			foreach ( var rb in bodies )
			{
				var pb = rb.PhysicsBody;
				if ( pb is null ) continue;
				// the part that was hit flies hardest, the rest follows
				var near = 1f - Math.Clamp( rb.WorldPosition.Distance( hitAt ) / 60f, 0f, 0.6f );
				pb.Velocity += push * near;
				pushed++;
			}
			if ( pushed == 0 ) { _pending[i] = ( go, push, hitAt, tries + 1 ); continue; }
			_pending.RemoveAt( i );
		}
	}

	// ----- every frame -----

	internal static void Tick( Scene scene, float dt )
	{
		// The ragdoll push must never stop the rest: when it threw on every frame (its list broken by a hotload) the red
		// edge flash and the low-health pulse never faded, and a fresh trooper spawned in a red screen.
		try
		{
			TickRagdolls( scene );
		}
		catch ( Exception )
		{
			_shots = new();
			_seenCorpses = new();
			_pending = new();
		}

		Flash = MathF.Max( 0f, Flash - dt / 0.45f );
		Blur = MathF.Max( 0f, Blur - dt / 0.4f );
		if ( !Feel.HitFx ) { Flash = 0f; Blur = 0f; }

		// low health: a double pulse in time with the heartbeat (0.9 s, 0.65 s when very low or downed)
		var vitals = Feel.LocalPlayer?.GetComponent<SwrpVitals>();
		TestLowHealth = MathF.Max( 0f, TestLowHealth - dt );
		var low = TestLowHealth > 0f || vitals is { IsDead: false } v && ( v.IsWounded || v.Health <= LowHealthAt );
		if ( low && Feel.HitFx )
		{
			var fast = TestLowHealth > 0f || vitals is { } w && ( w.Health <= 15f || w.IsWounded );
			_beat += dt / ( fast ? 0.65f : 0.9f );
			var p = _beat % 1f;
			var lub = MathF.Exp( -MathF.Pow( ( p - 0.05f ) / 0.06f, 2f ) );
			var dub = 0.6f * MathF.Exp( -MathF.Pow( ( p - 0.25f ) / 0.07f, 2f ) );
			LowHealth = LowHealth.LerpTo( MathF.Max( lub, dub ), dt * 20f );
		}
		else
		{
			_beat = 0f;
			LowHealth = LowHealth.LerpTo( 0f, dt * 4f );
		}

		Kick( scene, dt );
	}

	static void Kick( Scene scene, float dt )
	{
		FeelMotion.Spring( ref _pitch, ref _pitchV, 0f, 3.2f, 0.55f, dt );
		FeelMotion.Spring( ref _roll, ref _rollV, 0f, 2.6f, 0.6f, dt );
		FeelMotion.Spring( ref _yaw, ref _yawV, 0f, 2.8f, 0.65f, dt );
		_pitch = Math.Clamp( _pitch, -8f, 8f );
		_roll = Math.Clamp( _roll, -9f, 9f );
		_yaw = Math.Clamp( _yaw, -5f, 5f );
		if ( !Feel.Kick ) { _pitch = _roll = _yaw = _pitchV = _rollV = _yawV = 0f; return; }
		if ( MathF.Abs( _pitch ) + MathF.Abs( _roll ) + MathF.Abs( _yaw ) < 0.01f ) return;

		// only your own first or third person view; never a spectated player's, the character screen or a dead body's drift
		var cam = scene.Camera;
		if ( !cam.IsValid() || Feel.LocalPlayer is null || SwrpUiState.SpectateSteamId != 0 ) return;
		if ( Feel.LocalPlayer.GetComponent<SwrpVitals>() is { IsDead: true } ) return;
		_kickBase = cam.WorldRotation;
		_kickWritten = _kickBase * new Angles( _pitch, _yaw, _roll ).ToRotation();
		cam.WorldRotation = _kickWritten;
		_kickApplied = true;
	}

	static Rotation _kickBase, _kickWritten;
	static bool _kickApplied;

	/// <summary> Start of the frame: take last frame's kick back off if nothing has placed the camera since (so it never adds up). </summary>
	internal static void Restore( Scene scene )
	{
		if ( !_kickApplied ) return;
		_kickApplied = false;
		var cam = scene.Camera;
		if ( cam.IsValid() && cam.WorldRotation == _kickWritten ) cam.WorldRotation = _kickBase;
	}
}
