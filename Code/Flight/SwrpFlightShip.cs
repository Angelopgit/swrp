using System;

namespace Swrp.Flight;

/// <summary>
/// A flyable craft (ARC-170, LAAT/i). Design after Garry's Mod's WAC aircraft, written fresh for s&amp;box:
/// - seats with roles (pilot, co-pilot, gunners, passengers); E near the craft takes the nearest free seat, the number keys
///   move you between seats, E gets you out (in the air too: that's a jump);
/// - the pilot owns the craft on the network and flies it locally (no lag at the stick); everything else is the host's call:
///   who sits where, health, ammo, every shot and what it hits;
/// - engines spool up and down (R), a hover assist levels the gunship and holds its height, the fighter banks into turns;
/// - damage tears plating loose (sparks, smoke), a dead craft loses power, smokes, falls and blows up.
/// Partial: Physics (the flight model), Pilot (local input, camera, seat holding), Combat (weapons, damage, wrecks).
/// </summary>
[Title( "SWRP Flight Ship" ), Category( "SWRP" ), Icon( "flight_takeoff" )]
public sealed partial class SwrpFlightShip : Component, ISwrpUsable, Component.ICollisionListener
{
	/// <summary> Which craft (CraftRegistry id). Set by the host before spawning. </summary>
	[Sync( SyncFlags.FromHost )] public string CraftId { get; set; }

	/// <summary> Who sits in each seat: player GameObject ids, comma separated, empty for a free seat. </summary>
	[Sync( SyncFlags.FromHost )] public string SeatList { get; set; } = "";

	[Sync( SyncFlags.FromHost )] public float Health { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Wrecked { get; set; }

	/// <summary> Rounds left per launcher, "id:n" pairs (only weapons with limited ammo). </summary>
	[Sync( SyncFlags.FromHost )] public string AmmoList { get; set; } = "";

	// flown by whoever owns the craft (its pilot, else the host)
	[Sync] public bool EngineOn { get; set; }
	[Sync] public float Spool { get; set; }
	[Sync] public float Throttle { get; set; }
	[Sync] public bool Boosting { get; set; }

	/// <summary> Host: where the director parked it (it comes back here after a wreck). </summary>
	public Vector3 HomePosition { get; set; }
	public float HomeYaw { get; set; }

	/// <summary> Host: spawned from the staff menu or a command (not a hangar pad's); the menu's remove button takes these. </summary>
	public bool StaffSpawned { get; set; }

	public static readonly List<SwrpFlightShip> All = new();

	/// <summary>
	/// In use (engines on, flown, moving or falling). Parked craft sit still as static bodies.
	/// </summary>
	public bool NeedsGround => !_parked && ( EngineOn || Spool > 0.01f || Wrecked || Pilot.IsValid() || Velocity.Length > 30f ) || _waitingGround;

	CraftDef _def;
	public CraftDef Def => _def ??= CraftRegistry.Find( CraftId );

	Rigidbody _rb;
	ModelRenderer _renderer;
	public Rigidbody Body => _rb;
	public Vector3 Velocity => _rb.IsValid() ? ( IsProxy ? _netVelocity : _rb.Velocity ) : Vector3.Zero;

	protected override void OnEnabled() => All.Add( this );
	protected override void OnDisabled() => All.Remove( this );

	protected override void OnStart()
	{
		if ( Def is null )
		{
			Log.Warning( $"[SWRP] flight ship: unknown craft '{CraftId}'" );
			return;
		}
		BuildLocal();
		if ( Networking.IsHost && Health <= 0f && !Wrecked ) Health = Def.Health;
	}

	/// <summary> Every machine: the model, collision boxes and body. </summary>
	void BuildLocal()
	{
		Tags.Add( "flightship" );
		_renderer = Components.Create<ModelRenderer>();
		_renderer.Model = Model.Load( Def.ModelPath );
		foreach ( var (center, size) in Def.Boxes )
		{
			var b = Components.Create<BoxCollider>();
			b.Center = center;
			b.Scale = size;
			_boxes.Add( b );
		}
		_rb = Components.Create<Rigidbody>();
		_rb.MassOverride = Def.Mass;
		_rb.Gravity = true;
		_rb.MotionEnabled = !IsProxy;
		_parked = true;
	}

	// ----- seats ------------------------------------------------------------------------------------------------------

	Guid[] _seats = Array.Empty<Guid>();
	string _parsed;

	void Parse()
	{
		if ( _parsed == SeatList && _seats.Length == ( Def?.Seats.Length ?? 0 ) ) return;
		_parsed = SeatList;
		var n = Def?.Seats.Length ?? 0;
		_seats = new Guid[n];
		var parts = ( SeatList ?? "" ).Split( ',' );
		for ( var i = 0; i < n && i < parts.Length; i++ ) Guid.TryParse( parts[i], out _seats[i] );
	}

	public Guid Occupant( int seat )
	{
		Parse();
		return seat >= 0 && seat < _seats.Length ? _seats[seat] : Guid.Empty;
	}

	public SwrpPlayer PlayerIn( int seat )
	{
		var id = Occupant( seat );
		if ( id == Guid.Empty ) return null;
		return Scene.Directory.FindByGuid( id )?.Components.Get<SwrpPlayer>();
	}

	public int SeatOf( SwrpPlayer p )
	{
		if ( !p.IsValid() ) return -1;
		Parse();
		return Array.IndexOf( _seats, p.GameObject.Id );
	}

	public int Count
	{
		get
		{
			Parse();
			return _seats.Count( g => g != Guid.Empty );
		}
	}

	public SwrpPlayer Pilot => Def is null ? null : PlayerIn( Def.PilotSeat );

	/// <summary> The craft (and seat) a player sits in, or null. </summary>
	public static SwrpFlightShip ShipOf( SwrpPlayer p, out int seat )
	{
		seat = -1;
		if ( !p.IsValid() ) return null;
		foreach ( var s in All )
		{
			seat = s.SeatOf( p );
			if ( seat >= 0 ) return s;
		}
		return null;
	}

	public Transform SeatTransform( int seat )
	{
		var d = Def.Seats[seat];
		return new Transform( WorldTransform.PointToWorld( d.Position ), WorldRotation * Rotation.FromYaw( d.Yaw ) );
	}

	void HostSetSeat( int seat, Guid who )
	{
		Parse();
		var arr = _seats.ToArray();
		arr[seat] = who;
		SeatList = string.Join( ",", arr.Select( g => g == Guid.Empty ? "" : g.ToString() ) );
	}

	// ----- E prompt -----------------------------------------------------------------------------------------------------

	public (string Title, string Sub, string Tone) Prompt( SwrpPlayer me )
	{
		if ( Def is null ) return ( "Craft", "", "denied" );
		if ( Wrecked ) return ( Def.Name, "Wrecked", "denied" );
		var seat = BestSeat( me.WorldPosition );
		if ( seat < 0 ) return ( Def.Name, "Every seat is taken", "denied" );
		return ( Def.Name, $"Board: {Def.Seats[seat].Name}", "info" );
	}

	public void Use( SwrpPlayer me ) => RequestSeat( -1 );

	/// <summary> The free seat nearest a point (walk up to the nose for the cockpit, to the bay for the bay). </summary>
	public int BestSeat( Vector3 near )
	{
		if ( Def is null ) return -1;
		var best = -1;
		var bestD = float.MaxValue;
		for ( var i = 0; i < Def.Seats.Length; i++ )
		{
			if ( Occupant( i ) != Guid.Empty ) continue;
			var d = WorldTransform.PointToWorld( Def.Seats[i].Position ).WithZ( 0 ).Distance( near.WithZ( 0 ) );
			if ( d < bestD ) { bestD = d; best = i; }
		}
		return best;
	}

	/// <summary> Client: take a seat (-1 = the nearest free one), or move to another seat when already aboard. </summary>
	[Rpc.Host]
	public void RequestSeat( int seat )
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( Def is null || Wrecked || !p.IsValid() || p.CharacterId is null || p.Jailed || p.Frozen || p.InPod ) return;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } ) return;

		var current = SeatOf( p );
		if ( current < 0 )
		{
			if ( ShipOf( p, out _ ) is not null ) return;                                  // aboard another craft
			if ( p.WorldPosition.Distance( WorldPosition ) > ReachToBoard ) return;
		}
		if ( seat < 0 ) seat = BestSeat( p.WorldPosition );
		if ( seat < 0 || seat >= Def.Seats.Length ) { SwrpNet.NotifyConnection( caller, $"Every seat on the {Def.Name} is taken.", true ); return; }
		if ( seat == current ) return;
		if ( Occupant( seat ) != Guid.Empty ) { SwrpNet.NotifyConnection( caller, $"{Def.Seats[seat].Name} is taken.", true ); return; }

		if ( current >= 0 ) HostLeave( current, false );
		HostSetSeat( seat, p.GameObject.Id );
		p.InVehicle = true;
		_sinceBoarded = 0;
		if ( Def.Seats[seat].Role == SeatRole.Pilot ) GameObject.Network.AssignOwnership( caller );
		SeatFx( WorldTransform.PointToWorld( Def.Seats[seat].Position ) );
	}

	float ReachToBoard => Def.Model == FlightModel.Gunship ? 720f : 640f;

	/// <summary> Client: get out. </summary>
	[Rpc.Host]
	public void RequestExit()
	{
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		var seat = SeatOf( p );
		if ( seat < 0 ) return;
		HostLeave( seat, true );
	}

	/// <summary> Host: empty a seat; <paramref name="place"/> puts the trooper outside by their door. </summary>
	void HostLeave( int seat, bool place )
	{
		var p = PlayerIn( seat );
		HostSetSeat( seat, Guid.Empty );
		if ( Def.Seats[seat].Role == SeatRole.Pilot ) HostReleasePilot();
		if ( !p.IsValid() ) return;
		p.InVehicle = false;
		if ( !place ) return;

		var d = Def.Seats[seat];
		var at = WorldTransform.PointToWorld( d.ExitOffset );
		// on the ground: stand on whatever is under the door; in the air: you jump
		var tr = Scene.Trace.Ray( at + Vector3.Up * 120f, at + Vector3.Down * 200f ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "player", "trigger", "npc", "debris" ).Run();
		if ( tr.Hit ) at = tr.HitPosition + Vector3.Up * 4f;
		p.TeleportTo( at, ( WorldRotation * Rotation.FromYaw( d.Yaw ) ).Yaw() );
		SeatFx( at );
	}

	/// <summary> Host: everyone off where they are (the craft is being removed). </summary>
	public void HostEmpty()
	{
		if ( Def is null ) return;
		for ( var i = 0; i < Def.Seats.Length; i++ )
			if ( Occupant( i ) != Guid.Empty ) HostLeave( i, false );
	}

	/// <summary> Host: nobody flies it now. On the ground the engines stop; in the air the hover assist holds it. </summary>
	void HostReleasePilot()
	{
		if ( Connection.Host is { } host ) GameObject.Network.AssignOwnership( host );
		else GameObject.Network.DropOwnership();
		_pilotIn = default;
		if ( Landed ) EngineOn = false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SeatFx( Vector3 at ) => Sound.Play( "sounds/flight/seat.sound", at );

	/// <summary> Host, every frame: seats of troopers who died, went down or left are emptied. </summary>
	void HostSeats()
	{
		if ( Def is null ) return;
		for ( var i = 0; i < Def.Seats.Length; i++ )
		{
			if ( Occupant( i ) == Guid.Empty ) continue;
			var p = PlayerIn( i );
			if ( !p.IsValid() || p.CharacterId is null || p.Jailed || p.InPod || p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } )
				HostLeave( i, false );
		}
		if ( Wrecked ) return;
		// the pilot's machine flies it: make sure the right machine owns it
		var pilot = Pilot;
		// unpiloted and back on the deck after the slow descent: engines off (Physics brings it down)
		if ( !pilot.IsValid() && EngineOn && Landed && _testUntil <= 0f ) EngineOn = false;
		if ( pilot.IsValid() && pilot.Network.Owner is { } c && GameObject.Network.Owner != c ) GameObject.Network.AssignOwnership( c );
	}

	// ----- per frame ----------------------------------------------------------------------------------------------------

	Vector3 _netVelocity, _lastPos;

	protected override void OnUpdate()
	{
		FlightBolt.Sweep( Scene );
		if ( Def is null || !_rb.IsValid() ) return;

		// how fast it's going on screens that don't simulate it (HUD, sounds)
		if ( IsProxy && Time.Delta > 0f )
		{
			_netVelocity = Vector3.Lerp( _netVelocity, ( WorldPosition - _lastPos ) / Time.Delta, MathF.Min( 1f, Time.Delta * 8f ) );
		}
		_lastPos = WorldPosition;

		if ( Networking.IsHost )
		{
			HostSeats();
			HostAmmo();
			HostWreck();
		}
		RidersTick( Scene );
		PartsTick();
		AnimateParts();
		LocalPilot();
		Effects();
	}

	/// <summary> Sitting on the ground (or as good as): slow and with something just under the gear. </summary>
	public bool Landed
	{
		get
		{
			if ( Velocity.Length > 120f ) return false;
			var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * 40f, WorldPosition + Vector3.Down * 60f ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "player", "trigger", "npc", "debris" ).Run();
			return tr.Hit;
		}
	}

	/// <summary> Above the ground, in units (a long way if nothing's below). </summary>
	public float Altitude
	{
		get
		{
			var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * 20f, WorldPosition + Vector3.Down * 50000f ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "player", "trigger", "npc", "debris" ).Run();
			return tr.Hit ? MathF.Max( 0f, tr.Distance - 20f ) : 50000f;
		}
	}
}
