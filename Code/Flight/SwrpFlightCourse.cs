using System;
using System.Text.Json;

namespace Swrp.Flight;

/// <summary>
/// Dev: a scripted course for an empty craft, logged every physics step to data/anim_rec/course_&lt;craft&gt;.json, to check the
/// handling against its sheet (MapSource/FLIGHT_HANDLING.md): a straight run up to speed, hard turns either way, a
/// slalom, a climb and a dive, then a stop. The log keeps speed, heading, pitch, bank, sideslip and the turn's rate and its
/// change step to step (a jerk in the steering shows as a spike there).
/// </summary>
public sealed class SwrpFlightCourse : Component
{
	SwrpFlightShip _ship;
	TimeSince _t;
	float _yaw0;
	readonly List<float[]> _rows = new();
	float _lastYaw, _lastRate;
	bool _first = true;

	public const float Seconds = 24f;

	/// <summary> Dev (code_run_static_method), host: fly the empty craft of this id through the course from where it is. </summary>
	public static void Run( string craftId )
	{
		var s = SwrpFlightShip.All.FirstOrDefault( x => x.Def?.Id == craftId && !x.Pilot.IsValid() );
		if ( s is null ) { Log.Info( $"[SWRP] course: no empty {craftId}" ); return; }
		var c = s.Components.Create<SwrpFlightCourse>();
		c._ship = s;
		c._yaw0 = s.WorldRotation.Yaw();
		c._t = 0;
		s.EngineOn = true;
		s.Spool = 1f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !_ship.IsValid() ) { Destroy(); return; }
		var t = (float)_t;
		// the course: (aim yaw offset, aim pitch, forward input, lift)
		float yaw = 0f, pitch = 0f, fwd = 1f, lift = 0f;
		if ( t < 4f ) { }
		else if ( t < 8f ) yaw = 90f;
		else if ( t < 12f ) yaw = -90f;
		else if ( t < 17f ) yaw = MathF.Sin( ( t - 12f ) * 1.6f ) * 45f;
		else if ( t < 19f ) pitch = -30f;
		else if ( t < 21f ) pitch = 35f;
		else { fwd = -1f; }
		var aim = Rotation.From( pitch, _yaw0 + yaw, 0f ).Forward;
		_ship.HostTestFly( new SwrpFlightShip.PilotInput { Forward = fwd, Lift = lift, Strafe = 0f, Aim = aim, HasAim = true }, 0.2f );

		var a = _ship.WorldRotation.Angles();
		var v = _ship.Velocity;
		var local = _ship.WorldRotation.Inverse * v;
		var slip = v.Length > 50f ? MathF.Atan2( local.y, MathF.Max( 1f, local.x ) ) * 180f / MathF.PI : 0f;
		var rate = _first ? 0f : AngleDelta( _lastYaw, a.yaw ) / MathF.Max( Time.Delta, 0.001f );
		var accel = _first ? 0f : ( rate - _lastRate ) / MathF.Max( Time.Delta, 0.001f );
		_first = false;
		_lastYaw = a.yaw; _lastRate = rate;
		_rows.Add( new[] { t, v.Length, a.yaw, a.pitch, a.roll, slip, rate, accel, _ship.WorldPosition.z } );
		if ( t < Seconds ) return;
		FileSystem.Data.CreateDirectory( "anim_rec" );
		FileSystem.Data.WriteAllText( $"anim_rec/course_{_ship.Def.Id}.json", JsonSerializer.Serialize( new { craft = _ship.Def.Id, cols = new[] { "t", "speed", "yaw", "pitch", "roll", "slip", "yaw_rate", "yaw_accel", "z" }, rows = _rows } ) );
		Log.Info( $"[SWRP] course {_ship.Def.Id}: {_rows.Count} steps written" );
		Destroy();
	}

	static float AngleDelta( float from, float to )
	{
		var d = ( to - from ) % 360f;
		if ( d > 180f ) d -= 360f;
		if ( d < -180f ) d += 360f;
		return d;
	}
}
