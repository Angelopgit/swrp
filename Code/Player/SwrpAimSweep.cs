using System;
using System.Text.Json;

namespace Swrp;

/// <summary>
/// Dev: how far each raised blaster points off the crosshair. Takes the first player (the test trooper), and for every gun ×
/// stand / crouch / walk / down the sights × look pitch × look yaw, turns its view, lets the pose settle and measures the
/// angle between the barrel (the gun model's forward, from its muzzle) and the line from the muzzle to the crosshair point.
/// Writes data/anim_rec/aim_sweep.json (scratchpad aim_report.py turns it into the audit page's table).
/// </summary>
public sealed class SwrpAimSweep : Component
{
	static readonly float[] Pitches = { -60f, -30f, 0f, 30f, 60f };      // EyeAngles pitch: negative looks up
	static readonly float[] Yaws = { -45f, 0f, 45f };
	static readonly string[] States = { "stand", "crouch", "walk", "sights" };
	const float Settle = 0.5f, Sample = 0.25f;

	/// <summary> A sweep is driving the player: input off, the view and stance set here. </summary>
	public static bool Running { get; private set; }
	/// <summary> Down the sights this step (SwrpBlaster reads it as right mouse). </summary>
	public static bool Sights { get; private set; }

	SwrpPlayer _p;
	string[] _guns;
	int _g, _s, _pi, _yi;
	Angles _base;
	Vector3 _start;
	bool _wasThird;
	RealTimeSince _since;
	float _sum, _max;
	int _n;
	readonly List<object> _rows = new();

	/// <summary> Dev (code_run_static_method): sweep these guns (comma separated weapon ids). </summary>
	public static void Run( string guns = "dc15a" )
	{
		var p = Game.ActiveScene?.GetAllComponents<SwrpPlayer>().FirstOrDefault();
		if ( p?.Controller is null ) { Log.Info( "[SWRP] aim sweep: no player" ); return; }
		var c = p.GameObject.Components.Create<SwrpAimSweep>();
		c._p = p;
		c._guns = guns.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		c._base = p.Controller.EyeAngles.WithPitch( 0 );
		c._start = p.WorldPosition;
		c._wasThird = p.Controller.ThirdPerson;
		p.Controller.ThirdPerson = true;
		Running = true;
		c.Step( true );
	}

	void Step( bool gunChanged )
	{
		var blaster = _p.GetComponent<SwrpBlaster>();
		if ( gunChanged && blaster.IsValid() ) { blaster.WeaponId = _guns[_g]; blaster.SetDrawn( true ); }
		Sights = States[_s] == "sights";
		// back on the start spot every step: the walk drifts, and a wall or the floor near the muzzle spoils the reading
		_p.TeleportTo( _start, _base.yaw );
		_since = 0;
		_sum = _max = 0;
		_n = 0;
	}

	protected override void OnUpdate()
	{
		if ( !_p.IsValid() || !Running ) { Finish(); return; }
		var ctl = _p.Controller;
		var state = States[_s];
		ctl.EyeAngles = new Angles( Pitches[_pi], _base.yaw + Yaws[_yi], 0 );
		ctl.UpdateDucking( state == "crouch" );
		// walking: back and forth along the base heading, a new way each step so the trooper stays put
		var dir = Rotation.FromYaw( _base.yaw ).Forward * ( ( _pi + _yi ) % 2 == 0 ? 1f : -1f );
		ctl.WishVelocity = state == "walk" ? dir * 100f : Vector3.Zero;

		if ( _since < Settle ) return;
		var blaster = _p.GetComponent<SwrpBlaster>();
		if ( blaster.IsValid() && _p.Body.IsValid() && SwrpEmotes.AimPoint.TryGetValue( _p.Body, out var aim ) )
		{
			var (muzzle, barrel) = blaster.MuzzleRay;
			var err = MathF.Acos( Math.Clamp( Vector3.Dot( barrel.Normal, ( aim - muzzle ).Normal ), -1f, 1f ) ) * 180f / MathF.PI;
			_sum += err;
			_max = MathF.Max( _max, err );
			_n++;
		}
		if ( _since < Settle + Sample ) return;

		_rows.Add( new { gun = _guns[_g], state, pitch = Pitches[_pi], yaw = Yaws[_yi], err = _n > 0 ? _sum / _n : -1f, max = _max,
			set = _p.GmodSet, held = blaster?.WeaponId, model = _p.Body?.Model?.ResourcePath, turn = SwrpEmotes.LastAimTurn,
			dist = SwrpEmotes.AimPoint.TryGetValue( _p.Body, out var ap ) && blaster.IsValid() ? ap.Distance( blaster.MuzzleRay.Muzzle ) : -1f } );
		// next: yaw, then pitch, then state, then gun
		if ( ++_yi < Yaws.Length ) { Step( false ); return; }
		_yi = 0;
		if ( ++_pi < Pitches.Length ) { Step( false ); return; }
		_pi = 0;
		if ( ++_s < States.Length ) { Step( false ); return; }
		_s = 0;
		if ( ++_g < _guns.Length ) { Step( true ); return; }
		Finish();
	}

	void Finish()
	{
		Running = false;
		Sights = false;
		if ( _p.IsValid() && _p.Controller.IsValid() )
		{
			_p.Controller.ThirdPerson = _wasThird;
			_p.Controller.WishVelocity = Vector3.Zero;
			_p.Controller.UpdateDucking( false );
		}
		FileSystem.Data.CreateDirectory( "anim_rec" );
		FileSystem.Data.WriteAllText( "anim_rec/aim_sweep.json", JsonSerializer.Serialize( _rows ) );
		Log.Info( $"[SWRP] aim sweep: {_rows.Count} steps to anim_rec/aim_sweep.json" );
		Destroy();
	}

	protected override void OnDestroy()
	{
		if ( Running ) { Running = false; Sights = false; }
	}
}
