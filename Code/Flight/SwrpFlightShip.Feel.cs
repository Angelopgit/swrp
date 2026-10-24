using System;

namespace Swrp.Flight;

/// <summary>
/// What flying feels like from the seat (Angelo, 2026-10-21: "immersive when flying it, fluidity"): the mouse aim reaches the
/// craft through a short smoothing (no snapping or jitter from raw mouse steps), the chase camera trails the craft on a
/// spring and leans a little with its bank, and speed is felt through the field of view, the engine note (Effects), a wind
/// rush on a planet, a light shake on the afterburner and dust streaking past the camera. Local only: the seat's own screen.
/// </summary>
public sealed partial class SwrpFlightShip
{
	[ConVar( "swrp_flight_steer_smooth", Help = "Seconds the mouse aim is smoothed by before the craft steers to it (0 = raw)", Saved = true )]
	public static float SteerSmoothing { get; set; } = 0.06f;

	[ConVar( "swrp_flight_camera_lag", Help = "Chase camera: how quickly it catches the craft up (higher = tighter)", Saved = true )]
	public static float CameraCatchUp { get; set; } = 7f;

	[ConVar( "swrp_flight_dust", Help = "Dust streaking past the camera in flight", Saved = true )]
	public static bool DustOn { get; set; } = true;

	static Vector3 _chasePos;
	static bool _chaseSet;
	static float _speedFov;
	static SoundHandle _wind;
	static readonly List<(GameObject Go, LineRenderer Line, Vector3 Pos)> _dust = new();

	/// <summary> Local: ease the steering toward the mouse aim. </summary>
	static Angles SmoothSteer( Angles steer, Angles aim )
	{
		if ( SteerSmoothing <= 0.001f ) return aim;
		var k = 1f - MathF.Exp( -Time.Delta / SteerSmoothing );
		return Rotation.Slerp( steer.ToRotation(), aim.ToRotation(), k ).Angles().WithRoll( 0 );
	}

	/// <summary> Local chase camera: trail the wanted spot on a spring, and lean with a third of the craft's bank. </summary>
	(Vector3 Pos, Rotation Rot) ChaseCamera( Vector3 want, Rotation look )
	{
		if ( !_chaseSet || _chasePos.Distance( want ) > 2500f ) { _chasePos = want; _chaseSet = true; }
		_chasePos = Vector3.Lerp( _chasePos, want, 1f - MathF.Exp( -CameraCatchUp * Time.Delta ) );
		var lean = Rotation.FromRoll( WorldRotation.Angles().roll * 0.33f );
		return (_chasePos, look * lean);
	}

	/// <summary> Local, after the camera is placed: field of view, shake, wind and dust by speed and afterburner. </summary>
	void FlightFeel( CameraComponent cam, bool chase )
	{
		var speed = Velocity.Length;
		var k = Math.Clamp( speed / MathF.Max( 1f, Def.MaxSpeed ), 0f, 1.4f );
		_speedFov = _speedFov.LerpTo( 7f * k, Time.Delta * 3f );
		BoostFov( cam );
		cam.FieldOfView += _speedFov;

		// a fine shake on the afterburner, stronger in the cockpit
		if ( Boosting ) cam.WorldPosition += Vector3.Random * ( chase ? 1.2f : 0.6f ) * ( 0.5f + 0.5f * k );

		// wind rushing past on a planet (space is silent), by speed
		var onPlanet = FreeFlight?.Invoke( WorldPosition ) == true;
		var windVol = onPlanet && Spool > 0.05f ? Math.Clamp( ( speed - 300f ) / 2500f, 0f, 1f ) * 0.9f : 0f;
		if ( windVol > 0.01f )
		{
			if ( _wind is null || !_wind.IsPlaying ) _wind = Sound.Play( "sounds/tatooine/desert_wind.sound", cam.WorldPosition );
			if ( _wind is not null ) { _wind.Position = cam.WorldPosition; _wind.Volume = windVol; _wind.Pitch = 0.8f + 0.5f * k; }
		}
		else if ( _wind is not null ) { _wind.Stop( 0.5f ); _wind = null; }

		Dust( cam, speed, onPlanet );
	}

	/// <summary> Short streaks fixed in the world round the camera: still, they're specks; at speed they rush past as lines. </summary>
	void Dust( CameraComponent cam, float speed, bool onPlanet )
	{
		const int Count = 48;
		const float Box = 1400f;
		var on = DustOn && speed > 250f;
		if ( _dust.Count == 0 && on )
			for ( var i = 0; i < Count; i++ )
			{
				var go = new GameObject( true, "Flight dust" );
				go.NetworkMode = NetworkMode.Never;
				go.Flags |= GameObjectFlags.NotSaved;
				var line = go.Components.Create<LineRenderer>();
				line.UseVectorPoints = true;
				line.Additive = true;
				line.Lighting = false;
				line.CastShadows = false;
				line.Width = new Curve( new Curve.Frame( 0f, 0.6f ), new Curve.Frame( 1f, 1.6f ) );
				_dust.Add( (go, line, cam.WorldPosition + Vector3.Random * Box) );
			}
		var vel = Velocity;
		var tint = onPlanet ? new Color( 1f, 0.86f, 0.62f ) : new Color( 0.8f, 0.88f, 1f );
		var alpha = Math.Clamp( ( speed - 250f ) / 1500f, 0f, 1f ) * 0.55f;
		for ( var i = 0; i < _dust.Count; i++ )
		{
			var (go, line, pos) = _dust[i];
			if ( !go.IsValid() ) continue;
			go.Enabled = on;
			if ( !on ) continue;
			// out of the box: back in ahead of the camera, along where it's heading
			var rel = pos - cam.WorldPosition;
			if ( MathF.Abs( rel.x ) > Box || MathF.Abs( rel.y ) > Box || MathF.Abs( rel.z ) > Box )
				pos = cam.WorldPosition + vel.Normal * Box * 0.9f + new Vector3( Game.Random.Float( -1f, 1f ), Game.Random.Float( -1f, 1f ), Game.Random.Float( -1f, 1f ) ) * Box * 0.8f;
			// right by the lens a speck fills the screen: hide it as it goes past
			go.Enabled = ( pos - cam.WorldPosition ).Length > 160f && ( pos - vel * 0.035f - cam.WorldPosition ).Length > 160f;
			line.VectorPoints = new List<Vector3> { pos, pos - vel * 0.035f };
			line.Color = new Gradient( new Gradient.ColorFrame( 0f, tint.WithAlpha( alpha ) ), new Gradient.ColorFrame( 1f, tint.WithAlpha( 0f ) ) );
			_dust[i] = (go, line, pos);
		}
	}

	/// <summary> Local: out of the seat, the wind and dust go. </summary>
	static void StopFeel()
	{
		if ( _wind is not null ) { _wind.Stop( 0.3f ); _wind = null; }
		foreach ( var d in _dust ) if ( d.Go.IsValid() ) d.Go.Destroy();
		_dust.Clear();
		_chaseSet = false;
	}
}
