using System;

namespace Swrp.Flight;

/// <summary>
/// Host: keeps the flyable craft on their hangar pads (a wrecked or lost one comes back after <see cref="RespawnSeconds"/>),
/// and the dev/staff commands to spawn, list, repair and clear craft. Ticked from SwrpSpace every frame.
/// </summary>
public static class SwrpFlightDirector
{
	[ConVar( "swrp_flight", Help = "Flyable ARC-170s and LAAT/i gunships on the hangar pads (1, default) or the old parked static ARC-170 (0); host", Saved = true )]
	public static bool Enabled { get; set; } = true;

	[ConVar( "swrp_flight_respawn", Help = "Seconds before a wrecked craft is replaced on its hangar pad (default 90)", Saved = true )]
	public static float RespawnSeconds { get; set; } = 90f;

	/// <summary> True: the map's static parked ARC-170 is hidden (a flyable one takes its spot). </summary>
	public static bool ReplacesParked => Enabled;

	/// <summary>
	/// The main hangar's pads, in the open lane under the dorsal trench (x -544..544) so craft can lift straight out through
	/// the topside doors: the LAAT/i on the forward pad ring, the ARC-170 aft of the parked ride gunship. (A property, not a
	/// field: a code hotload keeps static fields.) FindPad nudges each to the nearest clear patch with open sky above.
	/// </summary>
	static (string Craft, Vector3 At, float Yaw)[] Pads => new[]
	{
		( "arc170", new Vector3( 0f, -8300f, -1224f ), 90f ),
		( "laat", new Vector3( 0f, -3900f, -1222f ), 90f ),
	};

	static readonly SwrpFlightShip[] _onPad = new SwrpFlightShip[2];
	static readonly RealTimeSince[] _padEmpty = new RealTimeSince[2];
	static readonly bool[] _padBad = new bool[2];
	static Scene _scene;

	public static void Tick( Scene scene )
	{
		if ( !Networking.IsHost || !Enabled || scene is null ) return;
		if ( _scene != scene )
		{
			_scene = scene;
			_hidOld = false;
			Array.Clear( _onPad );
			Array.Clear( _padBad );
			for ( var i = 0; i < Pads.Length; i++ ) _padEmpty[i] = 1000f;
		}
		// the static parked ARC-170 from before flight existed (a hotload keeps it): the flyable one takes its place
		if ( !_hidOld )
		{
			_hidOld = true;
			foreach ( var old in scene.GetAllObjects( true ).Where( g => g.Name == "Parked ARC-170" ).ToList() ) old.Enabled = false;
		}
		for ( var i = 0; i < Pads.Length; i++ )
		{
			if ( _onPad[i].IsValid() || _padBad[i] ) continue;
			if ( _padEmpty[i] < RespawnSeconds ) continue;
			var (craft, at, yaw) = Pads[i];
			var def = CraftRegistry.Find( craft );
			// a clear patch of deck at or near the pad, open to the topside doors if it can be
			var spot = FindPad( scene, def, at, yaw, true ) ?? FindPad( scene, def, at, yaw );
			if ( spot is null )
			{
				_padBad[i] = true;
				Log.Warning( $"[SWRP] flight: no clear deck for the {def.Name} near {at}" );
				continue;
			}
			// wait while something's parked on it
			if ( SwrpFlightShip.All.Any( s => s.WorldPosition.Distance( spot.Value ) < 500f ) ) { _padEmpty[i] = RespawnSeconds - 10f; continue; }
			_onPad[i] = Spawn( def, spot.Value, yaw );
		}
	}

	static bool _hidOld;

	static SceneTrace Solid( Scene scene, Vector3 a, Vector3 b ) =>
		scene.Trace.Ray( a, b ).WithoutTags( "player", "npc", "trigger", "debris", "ragdoll", "worlditem", "flightship" );

	/// <summary> The deck height under a point (the first solid surface below), or null if there's nothing within reach. </summary>
	public static float? DeckAt( Scene scene, Vector3 at, float above = 120f, float below = 400f )
	{
		var tr = Solid( scene, at + Vector3.Up * above, at + Vector3.Down * below ).Run();
		return tr.Hit && !tr.StartedSolid && tr.Normal.z > 0.7f ? tr.HitPosition.z : null;
	}

	/// <summary>
	/// A spot for a craft at or near <paramref name="want"/>: flat deck under its whole footprint and nothing solid in the
	/// space it takes up (and, with <paramref name="needSky"/>, open overhead up through the topside doors' shaft). Searches
	/// outward in rings; null if there's none within 1500 units.
	/// </summary>
	public static Vector3? FindPad( Scene scene, CraftDef def, Vector3 want, float yaw, bool needSky = false )
	{
		var lo = new Vector3( float.MaxValue );
		var hi = new Vector3( float.MinValue );
		foreach ( var (c, size) in def.Boxes ) { lo = lo.ComponentMin( c - size * 0.5f ); hi = hi.ComponentMax( c + size * 0.5f ); }
		var rot = Rotation.FromYaw( yaw );

		bool OpenAbove( Vector3 at )
		{
			foreach ( var off in new[] { Vector3.Zero, rot * new Vector3( 0, 300, 0 ), rot * new Vector3( 0, -300, 0 ) } )
			{
				// the topside doors themselves don't count: they open
				var tr = Solid( scene, at + off + Vector3.Up * ( hi.z + 20f ), at + off + Vector3.Up * 2500f ).Run();
				if ( tr.Hit && !SwrpRoofDoors.IsDoor( tr.GameObject ) ) return false;
			}
			return true;
		}

		bool Clear( Vector3 center, float deck )
		{
			for ( var x = lo.x; x <= hi.x; x += 60f )
				for ( var y = lo.y; y <= hi.y; y += 60f )
				{
					var p = center + rot * new Vector3( x, y, 0f );
					var d = DeckAt( scene, p.WithZ( deck ), 40f, 80f );
					if ( d is null || MathF.Abs( d.Value - deck ) > 30f ) return false;          // a hole or a step
					if ( Solid( scene, p.WithZ( deck + 30f ), p.WithZ( deck + hi.z ) ).Run().Hit ) return false;
				}
			return true;
		}

		for ( var ring = 0; ring <= 15; ring++ )
		{
			var r = ring * 100f;
			var count = ring == 0 ? 1 : ring * 8;
			for ( var k = 0; k < count; k++ )
			{
				var a = k * MathF.PI * 2f / count;
				var p = want + new Vector3( MathF.Cos( a ) * r, MathF.Sin( a ) * r, 0f );
				if ( SwrpFlightShip.All.Any( s => s.WorldPosition.WithZ( 0 ).Distance( p.WithZ( 0 ) ) < 700f ) ) continue;
				if ( DeckAt( scene, p ) is not { } deck ) continue;
				if ( Clear( p.WithZ( deck ), deck ) && ( !needSky || OpenAbove( p.WithZ( deck ) ) ) ) return p.WithZ( deck + 2f );
			}
		}
		return null;
	}

	/// <summary> Host: a craft was wrecked; its pad starts counting down. </summary>
	public static void Lost( SwrpFlightShip ship )
	{
		for ( var i = 0; i < Pads.Length; i++ )
			if ( _onPad[i] == ship ) { _onPad[i] = null; _padEmpty[i] = 0; }
	}

	/// <summary> Host: put a craft in the world, parked, engines off. </summary>
	public static SwrpFlightShip Spawn( CraftDef def, Vector3 at, float yaw )
	{
		var go = new GameObject( true, def.Name );
		go.WorldPosition = at;
		go.WorldRotation = Rotation.FromYaw( yaw );
		var s = go.Components.Create<SwrpFlightShip>();
		s.CraftId = def.Id;
		s.Health = def.Health;
		s.HomePosition = at;
		s.HomeYaw = yaw;
		go.NetworkSpawn();
		go.Network.SetOrphanedMode( NetworkOrphaned.Host );
		Log.Info( $"[SWRP] flight: {def.Name} at {at}" );
		return s;
	}

	// ----- staff spawn menu (Q) ---------------------------------------------------------------------------------------

	/// <summary> Client (staff or Game Master): put a craft on the deck where you aim, facing the way you look. </summary>
	[Rpc.Host]
	public static void RequestSpawn( string craftId )
	{
		var caller = Rpc.Caller;
		var def = CraftRegistry.Find( craftId );
		if ( !SwrpBuild.May( caller ) || def is null ) return;
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !me.IsValid() || !me.Controller.IsValid() ) { SwrpChat.SystemTo( caller, "Deploy (noclip is fine) to spawn craft where you aim." ); return; }
		if ( SwrpFlightShip.All.Count( s => s.StaffSpawned ) >= MaxSpawned ) { SwrpChat.SystemTo( caller, $"There are already {MaxSpawned} spawned craft; remove some first." ); return; }

		var scene = me.Scene;
		var eye = me.Controller.EyePosition;
		var fwd = me.Controller.EyeAngles.ToRotation().Forward;
		var yaw = me.Controller.EyeAngles.yaw;
		var tr = scene.Trace.Ray( eye, eye + fwd * 4000f ).IgnoreGameObjectHierarchy( me.GameObject ).WithoutTags( "trigger", "player", "debris" ).Run();
		var at = tr.Hit ? tr.HitPosition : eye + fwd * 1500f;

		// a clear patch of deck near there; failing that, the ground under the aim
		Vector3? spot = DeckAt( scene, at ) is not null ? FindPad( scene, def, at, yaw ) : null;
		if ( spot is null )
		{
			var down = scene.Trace.Ray( at + Vector3.Up * 100f, at + Vector3.Down * 3000f ).WithoutTags( "player", "trigger", "debris", "npc" ).Run();
			spot = ( down.Hit ? down.HitPosition : at ) + Vector3.Up * 4f;
		}
		var craft = Spawn( def, spot.Value, yaw );
		craft.StaffSpawned = true;
		SwrpBuild.Log( caller, $"spawned an {def.Name} at {SwrpShip.DeckName( spot.Value )}" );
	}

	public const int MaxSpawned = 8;

	/// <summary> Client (staff or Game Master): remove every craft spawned from the menu (anyone aboard is set down). </summary>
	[Rpc.Host]
	public static void RequestClearSpawned()
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) ) return;
		var n = 0;
		foreach ( var s in SwrpFlightShip.All.Where( s => s.StaffSpawned ).ToList() )
		{
			s.HostEmpty();
			s.GameObject.Destroy();
			n++;
		}
		SwrpBuild.Log( caller, $"removed {n} spawned craft" );
	}

	// ----- commands ------------------------------------------------------------------------------------------------------

	static bool Allowed()
	{
		if ( !Networking.IsHost ) { Log.Info( "[SWRP] host only" ); return false; }
		return true;
	}

	[ConCmd( "swrp_ship_spawn", Help = "swrp_ship_spawn <arc170|laat> - host: a flyable craft where the camera looks (on the deck in front of you)" )]
	public static void SpawnCommand( string craft = "arc170" )
	{
		if ( !Allowed() || Game.ActiveScene is not { } scene || !scene.Camera.IsValid() ) return;
		var def = CraftRegistry.Find( craft );
		if ( def is null ) { Log.Info( $"[SWRP] no craft '{craft}' (arc170, laat)" ); return; }
		var cam = scene.Camera;
		var tr = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 3000f ).WithoutTags( "player", "trigger", "debris" ).Run();
		var at = tr.Hit ? tr.HitPosition : cam.WorldPosition + cam.WorldRotation.Forward * 1500f;
		var down = scene.Trace.Ray( at + Vector3.Up * 50f, at + Vector3.Down * 2000f ).WithoutTags( "player", "trigger", "debris" ).Run();
		if ( down.Hit ) at = down.HitPosition;
		Spawn( def, at + Vector3.Up * 4f, cam.WorldRotation.Yaw() + 180f ).StaffSpawned = true;
	}

	[ConCmd( "swrp_ship_list", Help = "swrp_ship_list - the flyable craft, where they are, who's aboard" )]
	public static void ListCommand()
	{
		foreach ( var s in SwrpFlightShip.All )
		{
			var crew = string.Join( ", ", Enumerable.Range( 0, s.Def?.Seats.Length ?? 0 ).Select( i => s.PlayerIn( i ) is { } p && p.IsValid() ? $"{s.Def.Seats[i].Name}: {p.CharacterName}" : null ).Where( x => x is not null ) );
			Log.Info( $"[SWRP] {s.Def?.Name} {s.GameObject.Id} at {s.WorldPosition:0} hull {s.Health:0}/{s.Def?.Health:0}{( s.Wrecked ? " WRECKED" : "" )} engines {( s.EngineOn ? "on" : "off" )} speed {s.Velocity.Length:0} owner {s.GameObject.Network.Owner?.DisplayName ?? "host"} [{crew}]" );
		}
		if ( SwrpFlightShip.All.Count == 0 ) Log.Info( "[SWRP] no flyable craft" );
	}

	[ConCmd( "swrp_ship_repair", Help = "swrp_ship_repair - host: every craft back to full hull and ammo " )]
	public static void RepairCommand()
	{
		if ( !Allowed() ) return;
		foreach ( var s in SwrpFlightShip.All.Where( s => !s.Wrecked ) )
		{
			s.Health = s.Def.Health;
			s.AmmoList = "";
		}
	}

	[ConCmd( "swrp_ship_damage", Help = "swrp_ship_damage <amount> - host, dev: damage the craft nearest the camera (tests damage and wrecks)" )]
	public static void DamageCommand( float amount = 300f )
	{
		if ( !Allowed() || Game.ActiveScene is not { } scene || !scene.Camera.IsValid() ) return;
		var cam = scene.Camera.WorldPosition;
		var s = SwrpFlightShip.All.OrderBy( x => x.WorldPosition.Distance( cam ) ).FirstOrDefault();
		if ( s is null ) { Log.Info( "[SWRP] no craft" ); return; }
		var at = s.WorldTransform.PointToWorld( new Vector3( Game.Random.Float( -150f, 150f ), Game.Random.Float( -120f, 120f ), 120f ) );
		s.HostDamage( amount, at, null, "a dev command" );
		Log.Info( $"[SWRP] {s.Def.Name}: hull {s.Health:0}/{s.Def.Health:0}" );
	}

	[ConCmd( "swrp_ship_fly", Help = "swrp_ship_fly <forward> <lift> <turn degrees> <seconds> [strafe] - host, dev: fly the empty craft nearest the camera with fixed controls (engines on), logging where it ends up" )]
	public static void FlyCommand( float forward = 0.5f, float lift = 0.3f, float turn = 0f, float seconds = 4f, float strafe = 0f )
	{
		if ( !Allowed() || Game.ActiveScene is not { } scene || !scene.Camera.IsValid() ) return;
		var cam = scene.Camera.WorldPosition;
		var s = SwrpFlightShip.All.Where( x => x.Pilot is null ).OrderBy( x => x.WorldPosition.Distance( cam ) ).FirstOrDefault();
		if ( s is null ) { Log.Info( "[SWRP] no empty craft" ); return; }
		var aim = Rotation.FromYaw( s.WorldRotation.Yaw() + turn ).Forward;
		s.HostTestFly( new SwrpFlightShip.PilotInput { Forward = forward, Lift = lift, Strafe = strafe, Aim = aim, HasAim = true }, seconds );
		Log.Info( $"[SWRP] flight test: {s.Def.Name} from {s.WorldPosition:0}, {seconds} s" );
	}

	[ConCmd( "swrp_ship_fire", Help = "swrp_ship_fire <weapon id> [shots] - host, dev: fire a weapon of the craft nearest the camera straight ahead (ids: arc_cannons arc_torpedoes arc_tail laat_wing laat_missiles laat_chin laat_ball_l laat_ball_r)" )]
	public static void FireCommand( string weapon = "arc_cannons", int shots = 4 )
	{
		if ( !Allowed() || Game.ActiveScene is not { } scene || !scene.Camera.IsValid() ) return;
		var cam = scene.Camera.WorldPosition;
		var s = SwrpFlightShip.All.Where( x => x.Def?.Weapon( weapon ) is not null ).OrderBy( x => x.WorldPosition.Distance( cam ) ).FirstOrDefault();
		if ( s is null ) { Log.Info( $"[SWRP] no craft with '{weapon}'" ); return; }
		var w = s.Def.Weapon( weapon );
		FireBurst( s, w, Math.Clamp( shots, 1, 20 ) );
	}

	static async void FireBurst( SwrpFlightShip s, WeaponDef w, int shots )
	{
		for ( var i = 0; i < shots && s.IsValid(); i++ )
		{
			s.HostTestFire( w );
			await GameTask.DelaySeconds( w.FireDelay );
		}
	}

	[ConCmd( "swrp_ship_doors", Help = "swrp_ship_doors <0|1> - host, dev: shut or open the bay doors of every gunship nobody is flying" )]
	public static void DoorsCommand( int open = 1 )
	{
		if ( !Allowed() ) return;
		foreach ( var s in SwrpFlightShip.All.Where( x => !x.IsProxy && x.Def?.HasDoors == true ) ) s.DoorsOpen = open != 0;
	}

	[ConCmd( "swrp_ship_clear", Help = "swrp_ship_clear - host: remove every flyable craft (troopers aboard are set down where they are); the pads refill" )]
	public static void ClearCommand()
	{
		if ( !Allowed() ) return;
		foreach ( var s in SwrpFlightShip.All.ToList() )
		{
			s.HostEmpty();
			s.GameObject.Destroy();
		}
		for ( var i = 0; i < Pads.Length; i++ ) { _onPad[i] = null; _padEmpty[i] = 1000f; _padBad[i] = false; }
		_hidOld = false;
	}
}
