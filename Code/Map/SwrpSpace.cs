using System;

namespace Swrp;

/// <summary>
/// Space outside the ship: the sky (an original starfield and nebula with a planet, from MapSource/space_sky.py), small ships
/// flying past the viewports now and then, and the hyperspace jump a Game Master orders with /hyperspace: alarm chime, the
/// ship shudders, the stars streak, the blue tunnel, then the drop out at a new planet. The planet is synced from the host;
/// the passing ships and the jump's effects run on every machine for its own view. Purely cosmetic.
/// </summary>
[Title( "SWRP Space" ), Category( "SWRP" ), Icon( "public" )]
public sealed class SwrpSpace : Component
{
	public record Destination( string Id, string Name, string Sky );

	public static readonly Destination[] Destinations =
	{
		new( "coruscant", "Coruscant", "materials/space/sky_coruscant.vmat" ),
		new( "kamino", "Kamino", "materials/space/sky_kamino.vmat" ),
		new( "geonosis", "Geonosis", "materials/space/sky_geonosis.vmat" ),
		new( "christophsis", "Christophsis", "materials/space/sky_christophsis.vmat" ),
		new( "tatooine", "Tatooine", "materials/space/sky_tatooine.vmat" ),
		new( "hoth", "Hoth", "materials/space/sky_hoth.vmat" ),
	};
	const string StreakSky = "materials/space/sky_streak.vmat";
	const string TunnelSky = "materials/space/sky_tunnel.vmat";

	// the jump's beats, in seconds from the order
	public const float Spool = 6f;         // alarm and announcement, the engines spool up, the ship starts to shudder
	public const float Streak = 1.2f;      // the stars stretch into lines
	public const float ExitTime = 1.4f;    // streaks again, then the new planet
	public const float DefaultTunnel = 12f;

	[ConVar( "swrp_space_ships", Help = "Small ships flying past the viewports now and then (1 = on, 0 = off)", Saved = true )]
	public static bool ShipsEnabled { get; set; } = true;

	[Sync( SyncFlags.FromHost )] public int Planet { get; set; }
	[Sync( SyncFlags.FromHost )] public bool InHyperspace { get; set; }

	static SwrpSpace _current;
	public static SwrpSpace Current => _current.IsValid() ? _current : _current = Game.ActiveScene?.GetAllComponents<SwrpSpace>().FirstOrDefault();

	public static Destination Here => Destinations[Math.Clamp( Current?.Planet ?? 0, 0, Destinations.Length - 1 )];

	/// <summary> The jump's white flash on screen (0..1), drawn by <see cref="GradeOverlay"/>. </summary>
	public static float Flash { get; private set; }

	/// <summary> 0..1 while the ship is in the jump (the HUD and ambience can read it). </summary>
	public static bool Jumping => Current is { } s && s._jumpT >= 0;

	/// <summary> Seconds since the jump was ordered on this machine, or -1 when there's no jump. </summary>
	public static float JumpTime => Current is { } s ? s._jumpT : -1f;

	/// <summary> Where the jump in progress is going (null when there's no jump): planets get ready during the jump. </summary>
	public static Destination JumpDestination => Current is { } s && s._jumpT >= 0f ? Destinations[Math.Clamp( s._jumpDest, 0, Destinations.Length - 1 )] : null;

	SkyBox2D _sky;
	string _shownSky;
	static string _skyOverride;

	/// <summary> A sky for wherever the local camera is (the Mos Eisley trip's TatooineWorld); null to use the space sky. </summary>
	public static Func<string> LocalSkyOverride { get; set; }

	float _jumpT = -1f;
	int _jumpDest;
	float _jumpTunnel = DefaultTunnel;
	int _beat;
	SoundHandle _tunnelLoop;
	RealTimeSince _tunnelLoopAge;

	protected override void OnStart()
	{
		_sky = GetComponent<SkyBox2D>();
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		Flash = MathF.Max( 0f, Flash - dt * 2.2f );

		if ( _jumpT >= 0f )
		{
			_jumpT += dt;
			RunJump();
		}
		else SwrpShake.Rumble = 0f;

		Flight.SwrpFlightDirector.Tick( Scene );
		SetSky( _skyOverride ?? LocalSkyOverride?.Invoke() ?? SkyNow() );
		// the tunnel turns slowly round the ship
		var inTunnel = _jumpT > Spool + Streak && _jumpT < Spool + Streak + _jumpTunnel;
		WorldRotation = inTunnel ? Rotation.FromAxis( new Vector3( 0f, -1f, 0f ), ( _jumpT - Spool ) * 14f ) : Rotation.Identity;
		UpdateStreaks( dt );
		UpdateShips( dt );
	}

	string SkyNow()
	{
		if ( _jumpT < 0f ) return InHyperspace ? TunnelSky : Destinations[Math.Clamp( Planet, 0, Destinations.Length - 1 )].Sky;     // (joined mid-jump)
		var t = _jumpT;
		if ( t < Spool ) return Destinations[Math.Clamp( Planet, 0, Destinations.Length - 1 )].Sky;
		t -= Spool;
		if ( t < Streak ) return StreakSky;
		t -= Streak;
		if ( t < _jumpTunnel ) return TunnelSky;
		t -= _jumpTunnel;
		return t < ExitTime * 0.45f ? StreakSky : Destinations[_jumpDest].Sky;
	}

	void SetSky( string path )
	{
		if ( path == _shownSky || !_sky.IsValid() ) return;
		var m = Material.Load( path );
		if ( m is null ) return;
		_shownSky = path;
		_sky.SkyMaterial = m;
	}

	// ----- the jump ------------------------------------------------------------------------------------------------------

	/// <summary> Every machine runs the same jump from the host's order. </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void BeginJump( int dest, float tunnel )
	{
		_jumpDest = Math.Clamp( dest, 0, Destinations.Length - 1 );
		_jumpTunnel = Math.Clamp( tunnel, 4f, 60f );
		_jumpT = 0f;
		_beat = 0;
	}

	void RunJump()
	{
		var t = _jumpT;
		var jumpAt = Spool;
		var tunnelAt = Spool + Streak;
		var exitAt = tunnelAt + _jumpTunnel;
		var endAt = exitAt + ExitTime;

		if ( _beat == 0 )
		{
			_beat = 1;
			Sound.Play( "sounds/space/hyper_alarm.sound" );
			Sound.Play( "sounds/space/hyper_spool.sound" );
		}
		if ( _beat == 1 && t >= jumpAt )
		{
			_beat = 2;
			Sound.Play( "sounds/space/hyper_jump.sound" );
			Flash = 1f;
			SwrpShake.Add( 0.9f );
		}
		if ( _beat == 2 && t >= tunnelAt )
		{
			_beat = 3;
			_tunnelLoopAge = 100;
		}
		if ( _beat == 3 && t >= exitAt )
		{
			_beat = 4;
			_tunnelLoop?.Stop( 0.6f );
			_tunnelLoop = null;
			Sound.Play( "sounds/space/hyper_exit.sound" );
			Flash = 0.85f;
			SwrpShake.Add( 0.7f );
		}

		// the tunnel's rumble, a seamless 8 s loop restarted a moment early
		if ( _beat == 3 && _tunnelLoopAge > 7.92f )
		{
			_tunnelLoopAge = 0;
			_tunnelLoop = Sound.Play( "sounds/space/hyper_tunnel.sound" );
		}

		// how hard the ship shudders through it all
		SwrpShake.Rumble = t < jumpAt ? 0.4f * MathF.Pow( t / jumpAt, 2f )
			: t < tunnelAt ? 0.7f
			: t < exitAt ? 0.16f + 0.04f * MathF.Sin( t * 3f )
			: MathF.Max( 0f, 0.5f * ( 1f - ( t - exitAt ) / ExitTime ) );

		if ( t >= endAt )
		{
			_jumpT = -1f;
			SwrpShake.Rumble = 0f;
			if ( Networking.IsHost )
			{
				Planet = _jumpDest;
				InHyperspace = false;
				SwrpShip.Current?.PublicAddress( $"Dropping out of hyperspace. We have arrived at {Destinations[_jumpDest].Name}.", "Bridge" );
			}
		}
	}

	/// <summary> Host: jump to a destination (index), spending this many seconds in the tunnel. </summary>
	public bool Jump( Connection caller, int dest, float tunnel, out string error )
	{
		error = null;
		if ( _jumpT >= 0f || InHyperspace ) { error = "The ship is already in a jump."; return false; }
		if ( dest < 0 || dest >= Destinations.Length ) { error = "Unknown destination."; return false; }
		if ( dest == Planet ) { error = $"We are already at {Destinations[dest].Name}."; return false; }
		InHyperspace = true;
		SwrpShip.Current?.PublicAddress( $"All hands, prepare for the jump to lightspeed. Destination: {Destinations[dest].Name}.", "Bridge" );
		BeginJump( dest, tunnel );
		return true;
	}

	public static int Find( string id )
	{
		if ( string.IsNullOrWhiteSpace( id ) ) return -1;
		id = id.Trim().ToLowerInvariant();
		if ( int.TryParse( id, out var n ) && n >= 1 && n <= Destinations.Length ) return n - 1;
		for ( var i = 0; i < Destinations.Length; i++ )
			if ( Destinations[i].Id.StartsWith( id ) || Destinations[i].Name.ToLowerInvariant().StartsWith( id ) ) return i;
		return -1;
	}

	public static string List => string.Join( ", ", Destinations.Select( ( d, i ) => $"{i + 1} {d.Id}" ) );

	/// <summary> /hyperspace [destination] [seconds]: Game Masters order the jump; anyone else hears where the ship is. </summary>
	public static void Command( Connection caller, string rest )
	{
		var space = Current;
		if ( space is null ) { SwrpChat.SystemTo( caller, "This map has no space outside." ); return; }
		var args = ( rest ?? "" ).Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		if ( args.Length == 0 || !SwrpGameMaster.IsGameMaster( caller ) )
		{
			SwrpChat.SystemTo( caller, space.InHyperspace ? "The ship is in hyperspace." : $"The ship is holding position at {Here.Name}." );
			if ( SwrpGameMaster.IsGameMaster( caller ) ) SwrpChat.SystemTo( caller, $"Usage: /hyperspace <destination> [seconds in the tunnel, 4-60]. Destinations: {List}." );
			return;
		}
		var dest = Find( args[0] );
		if ( dest < 0 ) { SwrpChat.SystemTo( caller, $"Unknown destination. Destinations: {List}." ); return; }
		var secs = args.Length > 1 && float.TryParse( args[1], out var s ) ? s : DefaultTunnel;
		if ( !space.Jump( caller, dest, secs, out var error ) ) { SwrpChat.SystemTo( caller, error ); return; }
		SwrpChat.StaffLog( $"[GM] {caller.DisplayName} ordered the jump to {Destinations[dest].Name}" );
		SwrpStaffTools.RecordText( caller, "hyperspace", Destinations[dest].Name );
		SwrpChat.SystemTo( caller, $"GM: hyperspace jump to {Destinations[dest].Name} ({Math.Clamp( secs, 4f, 60f ):0} s in the tunnel)." );
	}

	[ConCmd( "swrp_hyperspace", Help = "swrp_hyperspace <destination> [seconds] - host only: jump the ship (same as /hyperspace). Destinations: coruscant, kamino, geonosis, christophsis, tatooine, hoth" )]
	public static void HyperspaceCommand( string destination, float seconds = DefaultTunnel )
	{
		if ( !Networking.IsHost || Current is not { } space ) return;
		var dest = Find( destination );
		if ( dest < 0 ) { Log.Info( $"[SWRP] Unknown destination. {List}" ); return; }
		if ( !space.Jump( Connection.Local, dest, seconds, out var error ) ) Log.Info( $"[SWRP] {error}" );
	}

	[ConCmd( "swrp_space_sky", Help = "swrp_space_sky <material> - your view only: show another sky material (no argument goes back)" )]
	public static void SkyCommand( string material = "" ) => _skyOverride = string.IsNullOrWhiteSpace( material ) ? null : material;

	// ----- star streaks past the viewports during the jump ---------------------------------------------------------------

	const int StreakCount = 90;
	readonly List<(LineRenderer Line, Vector3 Offset, float Length)> _streaks = new();
	GameObject _streakRoot;
	bool _streaksTunnel, _streaksColored;

	void UpdateStreaks( float dt )
	{
		var active = _jumpT >= Spool - 0.3f && _jumpT < Spool + Streak + _jumpTunnel + ExitTime;
		if ( !active )
		{
			if ( _streakRoot.IsValid() && _streakRoot.Enabled ) _streakRoot.Enabled = false;
			return;
		}
		var cam = Scene.Camera;
		if ( !cam.IsValid() ) return;
		if ( !_streakRoot.IsValid() ) BuildStreaks();
		_streakRoot.Enabled = true;

		// fast during the jump and the drop out, a steady rush in the tunnel; the ship flies towards -Y
		var t = _jumpT - Spool;
		var tunnel = t > Streak && t < Streak + _jumpTunnel;
		var speed = tunnel ? 26000f : 60000f;
		var stretch = t < 0f ? 0.2f : tunnel ? 1f : 1.6f;
		var color = tunnel ? new Color( 0.45f, 0.75f, 1f ) : new Color( 0.85f, 0.92f, 1f );
		// dropping out: the streaks slow, shorten and fade as the new planet comes up
		var exit = t - Streak - _jumpTunnel;
		var fade = exit > 0f ? Math.Clamp( 1f - exit / ExitTime, 0f, 1f ) : 1f;
		stretch *= fade;
		speed *= 0.3f + 0.7f * fade;
		color = color.WithAlpha( fade );
		var recolor = tunnel != _streaksTunnel || !_streaksColored || fade < 1f;
		_streaksTunnel = tunnel;
		_streaksColored = true;
		var gradient = new Gradient( new Gradient.ColorFrame( 0f, color ), new Gradient.ColorFrame( 1f, color.WithAlpha( 0f ) ) );
		var origin = cam.WorldPosition;
		for ( var i = 0; i < _streaks.Count; i++ )
		{
			var (line, off, len) = _streaks[i];
			off.y += speed * dt;
			if ( off.y > 16000f ) off.y -= 32000f;
			_streaks[i] = (line, off, len);
			var head = origin + off;
			line.VectorPoints = new List<Vector3> { head, head - new Vector3( 0f, len * stretch, 0f ) };     // the trail behind each star
			if ( recolor ) line.Color = gradient;
		}
	}

	void BuildStreaks()
	{
		_streakRoot = new GameObject( false, "Hyperspace Streaks" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
		var rnd = new Random( 77 );
		for ( var i = 0; i < StreakCount; i++ )
		{
			var go = new GameObject( _streakRoot, false, "streak" );
			var line = go.Components.Create<LineRenderer>();
			line.UseVectorPoints = true;
			line.Additive = true;
			line.Lighting = false;
			line.CastShadows = false;
			var w = 6f + (float)rnd.NextDouble() * 14f;
			line.Width = new Curve( new Curve.Frame( 0f, w ), new Curve.Frame( 1f, w * 0.6f ) );
			go.Enabled = true;
			// a hollow cylinder round the ship's long axis, well outside the hull
			var a = (float)rnd.NextDouble() * MathF.PI * 2f;
			var r = 5000f + (float)rnd.NextDouble() * 7000f;
			var off = new Vector3( MathF.Cos( a ) * r, -16000f + (float)rnd.NextDouble() * 32000f, MathF.Sin( a ) * r );
			_streaks.Add( (line, off, 1200f + (float)rnd.NextDouble() * 3500f) );
		}
	}

	// ----- small ships flying past ---------------------------------------------------------------------------------------

	static readonly (string Model, float Speed, int Group, float Weight)[] ShipTypes =
	{
		("models/space/ship_fighter.vmdl", 3200f, 3, 0.5f),
		("models/space/ship_shuttle.vmdl", 1700f, 1, 0.3f),
		("models/space/ship_frigate.vmdl", 900f, 1, 0.2f),
	};
	const int MaxShips = 6;

	readonly List<(GameObject Go, Vector3 Velocity, float Life)> _ships = new();
	RealTimeUntil _nextFlight = 12f;

	void UpdateShips( float dt )
	{
		for ( var i = _ships.Count - 1; i >= 0; i-- )
		{
			var (go, vel, life) = _ships[i];
			life -= dt;
			if ( !go.IsValid() || life <= 0f || _jumpT >= 0f || !ShipsEnabled )
			{
				go?.Destroy();
				_ships.RemoveAt( i );
				continue;
			}
			go.WorldPosition += vel * dt;
			_ships[i] = (go, vel, life);
		}

		if ( !ShipsEnabled || _jumpT >= 0f || SwrpPlayer.Local is null && !SwrpAnimMannequin.CameraPlaced || _nextFlight > 0f ) return;
		_nextFlight = Game.Random.Float( 25f, 70f );
		Launch( -1, -1 );
	}

	/// <summary> One flight past the ship: a pair or trio of fighters, a shuttle or a frigate. Type -1 picks at random. </summary>
	void Launch( int type, int lane )
	{
		if ( type < 0 )
		{
			var pick = Game.Random.Float();
			type = 0;
			for ( var i = 0; i < ShipTypes.Length; i++ ) { if ( pick < ShipTypes[i].Weight ) { type = i; break; } pick -= ShipTypes[i].Weight; }
		}
		var (model, speed, group, _) = ShipTypes[type];
		var count = group > 1 ? Game.Random.Int( 2, group ) : 1;
		if ( _ships.Count + count > MaxShips ) return;

		// three lanes: down either side (seen from the hangar bays) or across the bow (seen from the bridge)
		Vector3 start, dir;
		if ( lane < 0 || lane > 2 ) lane = Game.Random.Int( 0, 2 );
		var flip = Game.Random.Int( 0, 1 ) == 0 ? 1f : -1f;
		if ( lane < 2 )
		{
			var side = lane == 0 ? 1f : -1f;
			start = new Vector3( side * Game.Random.Float( 6500f, 10000f ), flip * 17000f, Game.Random.Float( -2600f, 2400f ) );
			dir = new Vector3( Game.Random.Float( -0.08f, 0.08f ), -flip, Game.Random.Float( -0.04f, 0.04f ) ).Normal;
		}
		else
		{
			start = new Vector3( flip * 17000f, Game.Random.Float( -15000f, -10000f ), Game.Random.Float( 1200f, 4200f ) );
			dir = new Vector3( -flip, Game.Random.Float( -0.15f, 0.15f ), Game.Random.Float( -0.04f, 0.04f ) ).Normal;
		}
		var rot = Rotation.LookAt( dir, Vector3.Up );
		var life = 34000f / speed;
		for ( var i = 0; i < count; i++ )
		{
			var go = new GameObject( true, "Passing Ship" ) { Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked };
			var r = go.Components.Create<ModelRenderer>();
			r.Model = Model.Load( model );
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			// fighters fly in a loose echelon
			go.WorldPosition = start + rot.Backward * ( i * 520f ) + rot.Right * ( i * 380f * ( i % 2 == 0 ? 1f : -1f ) ) + Vector3.Up * ( i * 90f );
			go.WorldRotation = rot;
			_ships.Add( (go, dir * speed * Game.Random.Float( 0.92f, 1.08f ), life) );
		}
	}

	[ConCmd( "swrp_look", Help = "swrp_look - print what the camera is looking at (object, distance, model and material)" )]
	public static void LookCommand()
	{
		var scene = Game.ActiveScene;
		var cam = scene?.Camera;
		if ( cam is null ) { Log.Info( "[SWRP] no camera" ); return; }
		var tr = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 40000f ).Run();
		var r = tr.GameObject?.GetComponent<ModelRenderer>();
		Log.Info( tr.Hit
			? $"[SWRP] looking at {tr.GameObject?.Name} at {tr.Distance:0} ({tr.HitPosition.x:0} {tr.HitPosition.y:0} {tr.HitPosition.z:0}) normal ({tr.Normal.x:0.00} {tr.Normal.y:0.00} {tr.Normal.z:0.00}), model {r?.Model?.ResourcePath}, surface {tr.Surface?.ResourceName}"
			: "[SWRP] looking at open space" );
	}

	[ConCmd( "swrp_probe", Help = "swrp_probe <x> <y> <z> - print the floor below a point and the distance to the walls round it (for placing things)" )]
	public static void ProbeCommand( float x, float y, float z )
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		var p = new Vector3( x, y, z );
		var down = scene.Trace.Ray( p, p + Vector3.Down * 2000f ).Run();
		var up = scene.Trace.Ray( p, p + Vector3.Up * 2000f ).Run();
		string Wall( Vector3 d )
		{
			var tr = scene.Trace.Ray( p, p + d * 3000f ).Run();
			return tr.Hit ? $"{tr.Distance:0}" : "-";
		}
		Log.Info( $"[SWRP] probe {x:0} {y:0} {z:0}: floor {( down.Hit ? down.HitPosition.z.ToString( "0" ) : "none" )} ({down.GameObject?.Name}), ceiling {( up.Hit ? up.HitPosition.z.ToString( "0" ) : "none" )}, walls +x {Wall( Vector3.Forward )} -x {Wall( Vector3.Backward )} +y {Wall( Vector3.Left )} -y {Wall( Vector3.Right )}{( down.StartedSolid ? " (inside something)" : "" )}" );
	}

	[ConCmd( "swrp_space_flyby", Help = "swrp_space_flyby [0 fighters | 1 shuttle | 2 frigate] [0 +X side | 1 -X side | 2 across the bow] - your view only: send a flight past the ship now" )]
	public static void FlybyCommand( int type = -1, int lane = -1 ) => Current?.Launch( Math.Clamp( type, -1, ShipTypes.Length - 1 ), lane );

	protected override void OnDestroy()
	{
		foreach ( var (go, _, _) in _ships ) go?.Destroy();
		_ships.Clear();
		_streakRoot?.Destroy();
		_tunnelLoop?.Stop();
	}
}
