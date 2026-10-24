using System;

namespace Swrp;

/// <summary>
/// A turbolift shaft. Every deck the shaft serves has its own copy of the car behind a lift door (the map build places one
/// per deck), so a ride is: both doors close and hold, the car hums for a moment, everyone aboard is moved to the same spot
/// in the target deck's car, and that deck's door opens. The doors double as call buttons (E opens them like any door);
/// the panel inside the car picks the deck. The host runs the ride; data comes from notorious_transit.py via notorious_scene.py.
/// </summary>
[Title( "SWRP Turbolift" ), Category( "SWRP" ), Icon( "elevator" )]
public sealed class SwrpTurbolift : Component
{
	/// <summary> Objects with this tag ride lifts and use teleporters along with players (test stands, later NPCs). </summary>
	public const string RiderTag = "transit";

	[Property] public string LiftId { get; set; }
	[Property] public string LiftName { get; set; } = "Turbolift";

	/// <summary> "offset|deck name|door id;..." bottom to top. Offset is the deck's height above the car's own deck. </summary>
	[Property] public string Floors { get; set; }

	/// <summary> The car's inside, in world space, on the deck the car was built for (offset 0). </summary>
	[Property] public Vector3 CarMin { get; set; }
	[Property] public Vector3 CarMax { get; set; }
	[Property] public float RideSeconds { get; set; } = 2.6f;

	[Sync( SyncFlags.FromHost )] public bool Moving { get; set; }
	[Sync( SyncFlags.FromHost )] public int MovingTo { get; set; } = -1;

	public record Deck( float Offset, string Name, string DoorId );

	string _parsedFrom;
	List<Deck> _decks = new();

	public IReadOnlyList<Deck> Decks
	{
		get
		{
			if ( _parsedFrom != Floors )
			{
				_parsedFrom = Floors;
				_decks = ( Floors ?? "" ).Split( ';', StringSplitOptions.RemoveEmptyEntries ).Select( s => s.Split( '|' ) )
					.Where( f => f.Length >= 3 ).Select( f => new Deck( f[0].ToFloat(), f[1], f[2] ) ).ToList();
			}
			return _decks;
		}
	}

	public BBox CarBox( int deck ) => new( CarMin + Vector3.Up * Decks[deck].Offset, CarMax + Vector3.Up * Decks[deck].Offset );

	public Vector3 CarCenter( int deck ) => CarBox( deck ).Center;

	/// <summary> The deck whose car this point is in, or -1. </summary>
	public int DeckAt( Vector3 pos )
	{
		for ( var i = 0; i < Decks.Count; i++ )
			if ( CarBox( i ).Contains( pos ) ) return i;
		return -1;
	}

	public SwrpDoor DoorOf( int deck ) => Scene.GetAllComponents<SwrpDoor>().FirstOrDefault( d => d.DoorId == Decks[deck].DoorId );

	/// <summary> The lift whose car this point is in (for the deck menu and the prompt). </summary>
	public static SwrpTurbolift At( Scene scene, Vector3 pos, out int deck )
	{
		foreach ( var lift in scene.GetAllComponents<SwrpTurbolift>() )
		{
			deck = lift.DeckAt( pos );
			if ( deck >= 0 ) return lift;
		}
		deck = -1;
		return null;
	}

	/// <summary> Client: ride from the car you are standing in to another deck. </summary>
	[Rpc.Host]
	public void RequestRide( int target )
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		var staff = caller is not null && Whitelist.IsStaff( caller );
		if ( !p.IsValid() ) return;
		if ( !staff && ( p.CharacterId is null || p.Jailed ) )
		{
			SwrpNet.NotifyConnection( caller, "Access denied.", true );
			return;
		}

		var from = DeckAt( p.WorldPosition );
		if ( from < 0 )
		{
			SwrpNet.NotifyConnection( caller, "Step inside the turbolift first.", true );
			return;
		}
		if ( target < 0 || target >= Decks.Count ) return;
		if ( target == from )
		{
			SwrpNet.NotifyConnection( caller, $"You are already on the {Decks[target].Name}.", true );
			return;
		}
		if ( Moving )
		{
			SwrpNet.NotifyConnection( caller, "The turbolift is already in motion.", true );
			return;
		}

		Ride( from, target );
	}

	/// <summary> Host: run a ride. Public for the swrp_lift_ride test command. </summary>
	public async void Ride( int from, int to )
	{
		if ( !Networking.IsHost || Moving ) return;
		var a = DoorOf( from );
		var b = DoorOf( to );
		Moving = true;
		MovingTo = to;
		try
		{
			foreach ( var d in new[] { a, b } )
			{
				if ( !d.IsValid() ) continue;
				d.HeldShut = true;
				d.SetOpen( false );
			}
			PlayAt( SwrpAudio.LiftRide, CarCenter( from ) );

			await Task.DelaySeconds( RideSeconds );
			if ( !this.IsValid() ) return;

			var lift = Vector3.Up * ( Decks[to].Offset - Decks[from].Offset );
			var box = CarBox( from );
			foreach ( var p in Scene.GetAllComponents<SwrpPlayer>().ToList() )
			{
				if ( !box.Contains( p.WorldPosition ) || p.GetComponent<SwrpVitals>() is { IsDead: true } ) continue;
				var yaw = p.Controller.IsValid() ? p.Controller.EyeAngles.yaw : p.WorldRotation.Yaw();
				p.TeleportTo( p.WorldPosition + lift + Vector3.Up * 2f, yaw );
			}
			foreach ( var go in Scene.FindAllWithTag( RiderTag ).ToList() )
				if ( box.Contains( go.WorldPosition ) ) go.WorldPosition += lift;

			if ( a.IsValid() ) a.HeldShut = false;
			if ( b.IsValid() )
			{
				b.HeldShut = false;
				b.SetOpen( true );
			}
			PlayAt( SwrpAudio.LiftArrive, CarCenter( to ) );
		}
		finally
		{
			if ( this.IsValid() )
			{
				Moving = false;
				MovingTo = -1;
				if ( a.IsValid() ) a.HeldShut = false;
				if ( b.IsValid() ) b.HeldShut = false;
			}
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayAt( string sound, Vector3 position ) => Sound.Play( sound, position );

	public string Describe()
	{
		var decks = string.Join( ", ", Decks.Select( ( d, i ) => $"{i + 1}:{d.Name} z{CarBox( i ).Mins.z + 6:0} door {d.DoorId}{( DoorOf( i ).IsValid() ? "" : " MISSING" )}" ) );
		return $"{LiftName} ({LiftId}){( Moving ? $" moving to {Decks[MovingTo].Name}" : "" )}: {decks}";
	}

	// ----- test helpers (host) -----

	[ConCmd( "swrp_lift_ride", Help = "swrp_lift_ride <lift id> <from deck> <to deck> - host only: run a turbolift ride (deck numbers start at 1)" )]
	public static void TestRide( string liftId, int from, int to )
	{
		if ( !Networking.IsHost ) return;
		var lift = Game.ActiveScene?.GetAllComponents<SwrpTurbolift>().FirstOrDefault( l => l.LiftId == liftId );
		if ( lift is null || from < 1 || to < 1 || from > lift.Decks.Count || to > lift.Decks.Count )
		{
			Log.Info( "[SWRP] usage: swrp_lift_ride <port|starboard> <from> <to>" );
			return;
		}
		Log.Info( $"[SWRP] {lift.LiftName}: {lift.Decks[from - 1].Name} to {lift.Decks[to - 1].Name}" );
		lift.Ride( from - 1, to - 1 );
	}

	[ConCmd( "swrp_transit_info", Help = "swrp_transit_info - print every turbolift and teleporter" )]
	public static void Info()
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		foreach ( var l in scene.GetAllComponents<SwrpTurbolift>() ) Log.Info( $"[SWRP] {l.Describe()}" );
		foreach ( var t in scene.GetAllComponents<SwrpTeleporter>() ) Log.Info( $"[SWRP] {t.Describe()}" );
	}

	[ConCmd( "swrp_transit_check", Help = "swrp_transit_check - host only: check every lift car and teleporter destination has floor under it and room to stand" )]
	public static void Check()
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		var problems = 0;

		foreach ( var l in scene.GetAllComponents<SwrpTurbolift>() )
		{
			for ( var i = 0; i < l.Decks.Count; i++ )
			{
				var c = l.CarCenter( i );
				var floor = l.CarBox( i ).Mins.z + 6f;
				var note = StandCheck( scene, c.WithZ( floor + 40f ), floor );
				if ( !l.DoorOf( i ).IsValid() ) note = ( note ?? "" ) + " door missing";
				if ( note is not null ) problems++;
				Log.Info( $"[SWRP] {l.LiftName} · {l.Decks[i].Name}: {note ?? "ok"}" );
			}
		}

		foreach ( var t in scene.GetAllComponents<SwrpTeleporter>() )
		{
			foreach ( var (pos, _) in t.Targets )
			{
				var note = StandCheck( scene, pos + Vector3.Up * 40f, pos.z );
				if ( note is null ) continue;
				problems++;
				Log.Info( $"[SWRP] {t.TeleporterName} destination {pos.x:0},{pos.y:0},{pos.z:0}: {note}" );
			}
		}
		Log.Info( $"[SWRP] transit check: {problems} problem(s)" );
	}

	/// <summary> Null when a trooper fits here: floor within reach below, nothing solid in the body's space. </summary>
	static string StandCheck( Scene scene, Vector3 waist, float expectFloor )
	{
		var down = scene.Trace.Ray( waist, waist + Vector3.Down * 120f ).Run();
		if ( !down.Hit ) return "no floor";
		if ( down.EndPosition.z > expectFloor + 24f || down.EndPosition.z < expectFloor - 40f ) return $"floor at {down.EndPosition.z:0}, expected {expectFloor:0}";   // a pad may drop you a little
		var body = scene.Trace.Box( new BBox( new Vector3( -14, -14, 0 ), new Vector3( 14, 14, 60 ) ), down.EndPosition + Vector3.Up * 4f, down.EndPosition + Vector3.Up * 5f ).Run();
		return body.Hit || body.StartedSolid ? $"blocked by {body.GameObject?.Name}" : null;
	}
}

/// <summary>
/// The deck panel inside a turbolift car: E opens the deck menu (<see cref="LiftMenu"/>). A child of the turbolift object,
/// one per deck's car.
/// </summary>
[Title( "SWRP Lift Panel" ), Category( "SWRP" ), Icon( "dialpad" )]
public sealed class SwrpLiftPanel : Component
{
	public SwrpTurbolift Lift => GameObject.Parent?.GetComponent<SwrpTurbolift>();

	static readonly Vector3 PanelSize = new( 2f, 14f, 20f );
	static readonly Color Idle = new( 0.25f, 0.55f, 0.9f );
	static readonly Color Lit = new( 1f, 0.68f, 0.15f );
	ModelRenderer _screen;
	readonly List<ModelRenderer> _deckLights = new();
	int _deck = -1;

	protected override void OnStart()
	{
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box.Bounds.Size;
		AddBox( "Housing", PanelSize, Vector3.Zero, new Color( 0.09f, 0.1f, 0.12f ) );
		_screen = AddBox( "Screen", new Vector3( 0.6f, 10.5f, 15f ), new Vector3( 1.1f, 0, 0.8f ), new Color( 0.05f, 0.12f, 0.2f ) );

		// one lit bar per deck, bottom deck lowest; this car's deck glows amber
		var decks = Lift?.Decks.Count ?? 0;
		for ( var i = 0; i < decks; i++ )
			_deckLights.Add( AddBox( $"Deck {i + 1}", new Vector3( 0.4f, 7f, 1.6f ), new Vector3( 1.5f, 0, -4.6f + i * 11f / Math.Max( 1, decks - 1 ) ), Idle ) );

		var col = AddComponent<BoxCollider>();
		col.Scale = PanelSize + 1f;

		ModelRenderer AddBox( string name, Vector3 size, Vector3 offset, Color tint )
		{
			var go = new GameObject( GameObject, true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = offset;
			go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = box;
			r.Tint = tint;
			return r;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_screen.IsValid() || Lift is not { } lift ) return;
		if ( _deck < 0 ) _deck = lift.DeckAt( WorldPosition + WorldRotation.Forward * 4f );
		var pulse = 0.6f + 0.4f * MathF.Sin( Time.Now * 8f );
		for ( var i = 0; i < _deckLights.Count; i++ )
		{
			if ( !_deckLights[i].IsValid() ) continue;
			_deckLights[i].Tint = lift.Moving && lift.MovingTo == i ? Lit * pulse : i == _deck ? Lit : Idle * 0.6f;
		}
	}
}

/// <summary>
/// A teleport pad from the original map (the bridge lifts, the simulation rooms, the Jedi temple). Walking into the box sends a
/// trooper to one of its destinations, round-robin so a squad doesn't stack. The host watches the box; arrivals get a short
/// grace period so nobody bounces straight back.
/// </summary>
[Title( "SWRP Teleporter" ), Category( "SWRP" ), Icon( "move_down" )]
public sealed class SwrpTeleporter : Component
{
	[Property] public string TeleporterName { get; set; } = "Lift";
	[Property] public Vector3 Size { get; set; } = new( 100, 100, 100 );

	/// <summary> "x,y,z,yaw;..." world positions on the floor. </summary>
	[Property] public string Destinations { get; set; }

	public const float Grace = 2f;
	static readonly Dictionary<Guid, RealTimeSince> LastTeleport = new();

	string _parsedFrom;
	List<(Vector3 Pos, float Yaw)> _targets = new();
	int _next;
	TimeSince _sinceScan;

	public IReadOnlyList<(Vector3 Pos, float Yaw)> Targets
	{
		get
		{
			if ( _parsedFrom != Destinations )
			{
				_parsedFrom = Destinations;
				_targets = ( Destinations ?? "" ).Split( ';', StringSplitOptions.RemoveEmptyEntries ).Select( s => s.Split( ',' ) )
					.Where( f => f.Length >= 4 ).Select( f => ( new Vector3( f[0].ToFloat(), f[1].ToFloat(), f[2].ToFloat() ), f[3].ToFloat() ) ).ToList();
			}
			return _targets;
		}
	}

	public BBox Box => BBox.FromPositionAndSize( WorldPosition, Size + 8f );

	/// <summary> Is this point standing in the pad (the trigger boxes start a little above the floor)? </summary>
	public bool Holds( Vector3 feet ) => Box.Contains( feet + Vector3.Up * 32f );

	/// <summary> The pad the local trooper is standing in, if any (for the E prompt). </summary>
	public static SwrpTeleporter At( Scene scene, Vector3 feet ) => scene.GetAllComponents<SwrpTeleporter>().FirstOrDefault( t => t.Holds( feet ) );

	/// <summary>
	/// Client: E inside a pad. Walking in does it on its own; this is for staff in noclip (who pass through pads freely) and
	/// anyone who stepped in during the grace period after arriving.
	/// </summary>
	[Rpc.Host]
	public void RequestUse()
	{
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		if ( !p.IsValid() || !Holds( p.WorldPosition ) || p.Frozen || p.Jailed || Targets.Count == 0 ) return;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } ) return;
		LastTeleport[p.GameObject.Id] = 0;
		var (pos, yaw) = NextTarget();
		Fx( p.WorldPosition, pos );
		p.TeleportTo( pos + Vector3.Up * 4f, yaw );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || _sinceScan < 0.1f || Targets.Count == 0 ) return;
		_sinceScan = 0;
		var box = Box;

		foreach ( var p in SwrpPlayers.Valid.ToList() )
		{
			// the trigger boxes start a few units above the floor, so test the body's middle, not the feet
			if ( !box.Contains( p.WorldPosition + Vector3.Up * 32f ) || p.Noclip || p.Frozen || p.Jailed ) continue;
			if ( p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } ) continue;
			if ( !Ready( p.GameObject ) ) continue;
			var (pos, yaw) = NextTarget();
			Fx( p.WorldPosition, pos );
			p.TeleportTo( pos + Vector3.Up * 4f, yaw );
		}

		foreach ( var go in Scene.FindAllWithTag( SwrpTurbolift.RiderTag ).ToList() )
		{
			if ( !box.Contains( go.WorldPosition + Vector3.Up * 32f ) || !Ready( go ) ) continue;
			var (pos, yaw) = NextTarget();
			Fx( go.WorldPosition, pos );
			go.WorldPosition = pos;
			go.WorldRotation = Rotation.FromYaw( yaw );
		}
	}

	static bool Ready( GameObject go )
	{
		if ( LastTeleport.TryGetValue( go.Id, out var t ) && t < Grace ) return false;
		LastTeleport[go.Id] = 0;
		return true;
	}

	(Vector3, float) NextTarget()
	{
		var t = Targets[_next % Targets.Count];
		_next++;
		return t;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Fx( Vector3 from, Vector3 to )
	{
		Sound.Play( SwrpAudio.Teleport, from );
		Sound.Play( SwrpAudio.Teleport, to );
	}

	public string Describe()
	{
		var c = WorldPosition;
		return $"{TeleporterName} at {c.x:0},{c.y:0},{c.z:0} size {Size.x:0}x{Size.y:0}x{Size.z:0} -> {Targets.Count} destination(s)"
			+ ( Targets.Count > 0 ? $" around {Targets[0].Pos.x:0},{Targets[0].Pos.y:0},{Targets[0].Pos.z:0}" : "" );
	}
}
