using System;

namespace Swrp;

/// <summary>
/// The ship's alert level (DEFCON 5, normal operations, down to DEFCON 1, maximum alert) and its public address. The host owns
/// both; officers (2nd Lieutenant and up) and staff set them with /alert and /pa or from a bridge console. Every machine tints
/// the ship's lights from the synced level, sounds the alarm when it changes and shows it on the HUD. At DEFCON 1 the ship
/// can go into lockdown: every door then needs a 2nd Lieutenant or higher (swrp_defcon_lockdown).
/// One per scene ("SWRP Ship", placed by notorious_scene.py).
/// </summary>
[Title( "SWRP Ship Systems" ), Category( "SWRP" ), Icon( "warning" )]
public sealed class SwrpShip : Component
{
	public const int OfficerRank = SwrpRoleplay.LieutenantRank;
	public const float AnnounceCooldown = 8f;
	public const float AlertCooldown = 3f;

	[ConVar( "swrp_defcon_lockdown", Help = "1 = DEFCON 1 locks every door to 2nd Lieutenant and up (host, default 1)", Saved = true )]
	public static bool LockdownAtDefconOne { get; set; } = true;

	public record AlertInfo( int Level, string Name, string Hex, Color Tint, float Amount );

	/// <summary> Index = level. Tint and how far the ship's lights move towards it. </summary>
	public static readonly AlertInfo[] Levels =
	{
		null,
		new( 1, "Maximum Alert", "#ff3b30", new Color( 1f, 0.16f, 0.1f ), 0.85f ),
		new( 2, "Battle Stations", "#ff7a2f", new Color( 1f, 0.42f, 0.2f ), 0.7f ),
		new( 3, "Elevated", "#ffb547", new Color( 1f, 0.72f, 0.38f ), 0.45f ),
		new( 4, "Increased Readiness", "#ffe08a", new Color( 1f, 0.94f, 0.78f ), 0.15f ),
		new( 5, "Normal Operations", "#7dffa1", Color.White, 0f ),
	};

	[Sync( SyncFlags.FromHost )] public int AlertLevel { get; set; } = 5;
	[Sync( SyncFlags.FromHost )] public string AlertReason { get; set; }
	[Sync( SyncFlags.FromHost )] public string AlertSetBy { get; set; }
	[Sync( SyncFlags.FromHost )] public bool LockdownActive { get; set; }

	/// <summary> The main hangar's topside doors (Map/SwrpRoofDoors.cs slides them; the hangar control room's button opens them). </summary>
	[Sync( SyncFlags.FromHost )] public bool RoofOpen { get; set; }
	/// <summary> The enemy warship is in (a GM's boarding attack, <see cref="SwrpBoardingShip"/>). </summary>
	[Sync( SyncFlags.FromHost )] public bool BoardingShip { get; set; }

	/// <summary> The host's swrp_defcon_lockdown, for the console menu. </summary>
	[Sync( SyncFlags.FromHost )] public bool LockdownEnabled { get; set; } = true;

	public AlertInfo Alert => Levels[Math.Clamp( AlertLevel, 1, 5 )];

	// Where things are in the Notorious map (MapSource/regions.py has the same boxes).
	static readonly BBox ShipBox = new( new Vector3( -6100, -12020, -2900 ), new Vector3( 6100, 12030, 3430 ) );
	static readonly BBox TempleBox = new( new Vector3( 6250, -14300, 13350 ), new Vector3( 14320, -3350, 15050 ) );
	static readonly BBox SimBox = new( new Vector3( -13100, 2960, 13400 ), new Vector3( -6450, 11500, 15260 ) );

	public static bool InShip( Vector3 p ) => ShipBox.Contains( p );

	/// <summary> A plain name for where a point is aboard: the deck, the bridge, the Jedi temple or the simulation rooms. </summary>
	public static string DeckName( Vector3 p )
	{
		if ( TempleBox.Contains( p ) ) return "Jedi Temple";
		if ( SimBox.Contains( p ) ) return "Simulation Rooms";
		if ( !ShipBox.Contains( p ) ) return "off the ship";
		return p.z switch
		{
			> 2800 => "Bridge",
			> 0 => "Upper Decks",
			> -500 => "Command Deck",
			> -1000 => "Operations Deck",
			> -1500 => "Main Deck",
			_ => "Engineering Deck",
		};
	}

	static SwrpShip _current;
	public static SwrpShip Current => _current.IsValid() ? _current : _current = Game.ActiveScene?.GetAllComponents<SwrpShip>().FirstOrDefault();

	/// <summary> True while the ship is locked down (DEFCON 1 with lockdown on). Doors read this. </summary>
	public static bool Lockdown => Current is { LockdownActive: true };

	// ----- client: lights, alarm, announcement banner -----

	/// <summary> The last ship announcement, for the HUD banner. </summary>
	public static string Announcement { get; private set; }
	public static string AnnouncementBy { get; private set; }
	public static RealTimeSince SinceAnnouncement { get; private set; } = 100;

	readonly List<(PointLight Light, Color Base)> _lights = new();
	readonly List<(AmbientLight Light, Color Base)> _ambient = new();
	int _shownLevel = -1;
	int _klaxonsLeft;
	TimeSince _sinceKlaxon;
	TimeSince _hostAction = 100;
	TimeSince _hostAnnounce = 100;

	protected override void OnStart()
	{
		SwrpExtraLamps.Place( Scene );      // lamps for the dark corners Angelo tagged, before the ship's lights are gathered
		// the ship's own lights (the Jedi temple and simulation rooms keep theirs)
		foreach ( var l in Scene.GetAllComponents<PointLight>() )
		{
			var p = l.WorldPosition;
			if ( MathF.Abs( p.x ) < 6100 && MathF.Abs( p.y ) < 12100 && p.z > -2900 && p.z < 3500 && l.GameObject.Parent?.Name == "Lights" )
				_lights.Add( (l, l.LightColor) );
		}
		// the ambient fill does most of the lighting, so it takes the alert colour too
		foreach ( var a in Scene.GetAllComponents<AmbientLight>() )
			_ambient.Add( (a, a.Color) );
	}

	protected override void OnUpdate()
	{
		SwrpRoofDoors.Tick( Scene );
		SwrpBoardingShip.Tick( Scene, BoardingShip );
		if ( Networking.IsHost )
		{
			LockdownEnabled = LockdownAtDefconOne;
			LockdownActive = AlertLevel == 1 && LockdownAtDefconOne;
		}

		if ( _shownLevel != AlertLevel )
		{
			var first = _shownLevel < 0;
			_shownLevel = AlertLevel;
			ApplyLights( 1f );
			if ( !first ) Alarm();
		}

		if ( _klaxonsLeft > 0 && _sinceKlaxon > 2.3f )
		{
			_klaxonsLeft--;
			_sinceKlaxon = 0;
			Sound.Play( SwrpAudio.AlertKlaxon );
		}
	}

	/// <summary>
	/// The alert level no longer recolours the ship (Angelo, 2026-10-12: only the HUD banner at the top shows DEFCON 4-1);
	/// this puts the lights back to their own colour, in case a session tinted them before the change.
	/// </summary>
	void ApplyLights( float k )
	{
		foreach ( var (light, b) in _lights )
			if ( light.IsValid() ) light.LightColor = b * k;
		foreach ( var (amb, b) in _ambient )
			if ( amb.IsValid() ) amb.Color = b;
	}

	/// <summary>
	/// Dev: give the running ship the same lamp colours as the rebuilt scene: every white, warm or yellow lamp the same white
	/// (one colour temperature, front to back), blue and red accents kept. The saved scene already has this; this is for a
	/// session that was loaded before.
	/// </summary>
	[ConCmd( "swrp_lamps_white", Help = "swrp_lamps_white - dev: every warm or yellow ship lamp to the same white in this session" )]
	public static void LampsWhite()
	{
		var ship = Current;
		if ( ship is null ) return;
		var n = 0;
		for ( var i = 0; i < ship._lights.Count; i++ )
		{
			var (l, c) = ship._lights[i];
			if ( !l.IsValid() ) continue;
			var mx = MathF.Max( c.r, MathF.Max( c.g, c.b ) );
			var warm = mx > 0f && c.r >= mx && c.g >= 0.8f * c.r && c.b >= 0.4f * c.r;
			var amber = mx > 0f && ( MathF.Min( c.r, c.g ) >= 0.7f * mx && c.b < 0.85f * mx || c.r >= mx && c.g >= 0.3f * c.r && c.b < 0.6f * c.g );
			var p = l.WorldPosition;
			var exempt = p.x is >= 1150 and <= 3800 && p.y is >= 4200 and <= 6200 && p.z is >= -1260 and <= -640      // the brig
				|| p.x is >= -2200 and <= -700 && p.y is >= 3300 and <= 4700 && p.z is >= -900 and <= -500;      // the club
			if ( !warm && !( amber && !exempt ) ) continue;
			var w = new Color( 3f, 2.91f, 2.76f, 1f );      // one colour and one strength for every regular lamp
			ship._lights[i] = (l, w);
			l.LightColor = w;
			n++;
		}
		Log.Info( $"[SWRP] {n} ship lamps set to white" );
	}

	void Alarm()
	{
		Sound.Play( SwrpAudio.AlertChange );
		_klaxonsLeft = AlertLevel switch { 1 => 3, 2 => 1, _ => 0 };
		_sinceKlaxon = 1.5f;    // the first klaxon follows the change tone
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowAnnouncement( string text, string by )
	{
		Announcement = text;
		AnnouncementBy = by;
		SinceAnnouncement = 0;
		Sound.Play( SwrpAudio.ShipChime );
	}

	// ----- host -----

	/// <summary> Officers (2nd Lieutenant+) on duty and staff run the ship's alert level and public address. </summary>
	public static bool MayCommand( Connection caller, SwrpPlayer p )
	{
		if ( caller is not null && Whitelist.IsStaff( caller ) ) return true;
		return p.IsValid() && p.CharacterId is not null && !p.Jailed && p.RankIndex >= OfficerRank;
	}

	static string Who( Connection caller, SwrpPlayer p ) => p.IsValid() && p.CharacterId is not null ? $"{p.Rank.Abbreviation} {p.CharacterName}" : caller?.DisplayName ?? "Bridge";

	/// <summary> Host: /alert, /defcon and /pa. </summary>
	public void Command( Connection caller, SwrpPlayer sender, string cmd, string args )
	{
		args = ( args ?? "" ).Trim();
		if ( cmd == "/pa" )
		{
			Announce( caller, sender, args );
			return;
		}

		if ( args.Length == 0 )
		{
			var by = string.IsNullOrEmpty( AlertSetBy ) ? "" : $", set by {AlertSetBy}";
			var why = string.IsNullOrEmpty( AlertReason ) ? "" : $": {AlertReason}";
			SwrpChat.SystemTo( caller, $"Alert level: DEFCON {AlertLevel} ({Alert.Name}){why}{by}.{( LockdownActive ? " The ship is in lockdown." : "" )} Officers: /alert <1-5> [reason]." );
			return;
		}

		var parts = args.Split( ' ', 2 );
		if ( !int.TryParse( parts[0], out var level ) || level < 1 || level > 5 )
		{
			SwrpChat.SystemTo( caller, "Usage: /alert <1-5> [reason]  (5 = normal operations, 1 = maximum alert)" );
			return;
		}
		SetAlert( caller, sender, level, parts.Length > 1 ? parts[1] : "" );
	}

	public void SetAlert( Connection caller, SwrpPlayer sender, int level, string reason )
	{
		if ( !MayCommand( caller, sender ) )
		{
			SwrpChat.SystemTo( caller, "Only officers (2nd Lieutenant and up) or staff can change the alert level." );
			return;
		}
		if ( _hostAction < AlertCooldown )
		{
			SwrpChat.SystemTo( caller, "The alert level was just changed; give it a moment." );
			return;
		}

		level = Math.Clamp( level, 1, 5 );
		reason = SwrpChat.Clean( reason, 80 );
		if ( level == AlertLevel && reason == ( AlertReason ?? "" ) )
		{
			SwrpChat.SystemTo( caller, $"The ship is already at DEFCON {level}." );
			return;
		}

		_hostAction = 0;
		var who = Who( caller, sender );
		AlertLevel = level;
		AlertReason = reason;
		AlertSetBy = who;
		LockdownActive = level == 1 && LockdownAtDefconOne;

		var info = Alert;
		var line = $"[SHIP] Alert level DEFCON {level}: {info.Name.ToUpperInvariant()}{( reason.Length > 0 ? $" - {reason}" : "" )} (by {who})";
		if ( LockdownActive ) line += ". Lockdown: doors open for 2nd Lieutenants and up only.";
		SwrpChat.SendTo( Connection.All, ChatKind.Broadcast, line );
		if ( Whitelist.IsStaff( caller ) )
		{
			SwrpChat.StaffLog( $"{caller.DisplayName} set the alert level to DEFCON {level}{( reason.Length > 0 ? $" ({reason})" : "" )}" );
			SwrpStaffTools.RecordText( caller, "alert", $"DEFCON {level} {reason}".Trim() );
		}
	}

	public void Announce( Connection caller, SwrpPlayer sender, string text )
	{
		if ( !MayCommand( caller, sender ) )
		{
			SwrpChat.SystemTo( caller, "Only officers (2nd Lieutenant and up) or staff can use the ship's public address." );
			return;
		}
		text = SwrpChat.Clean( text, 160 );
		if ( text.Length == 0 )
		{
			SwrpChat.SystemTo( caller, "Usage: /pa <announcement>" );
			return;
		}
		if ( _hostAnnounce < AnnounceCooldown )
		{
			SwrpChat.SystemTo( caller, $"Wait {AnnounceCooldown - _hostAnnounce:0} s before the next announcement." );
			return;
		}

		_hostAnnounce = 0;
		var who = Who( caller, sender );
		SwrpChat.SendTo( Connection.All, ChatKind.Broadcast, $"[SHIP ANNOUNCEMENT] {who}: {text}" );
		ShowAnnouncement( text, who );
		if ( Whitelist.IsStaff( caller ) )
		{
			SwrpChat.StaffLog( $"{caller.DisplayName} made a ship announcement: {text}" );
			SwrpStaffTools.RecordText( caller, "ship announcement", text );
		}
	}

	/// <summary> Host: a ship announcement from the ship itself (the hyperspace jump), no rank check or cooldown. </summary>
	public void PublicAddress( string text, string who )
	{
		if ( !Networking.IsHost ) return;
		SwrpChat.SendTo( Connection.All, ChatKind.Broadcast, $"[SHIP ANNOUNCEMENT] {who}: {text}" );
		ShowAnnouncement( text, who );
	}

	// ----- requests from the bridge console menu -----

	[Rpc.Host]
	public void RequestAlert( int level, string reason )
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !NearConsole( p ) && !( caller is not null && Whitelist.IsStaff( caller ) ) )
		{
			SwrpNet.NotifyConnection( caller, "You are too far from a bridge console.", true );
			return;
		}
		SetAlert( caller, p, level, reason );
	}

	[Rpc.Host]
	public void RequestAnnounce( string text )
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !NearConsole( p ) && !( caller is not null && Whitelist.IsStaff( caller ) ) )
		{
			SwrpNet.NotifyConnection( caller, "You are too far from a bridge console.", true );
			return;
		}
		Announce( caller, p, text );
	}

	bool NearConsole( SwrpPlayer p ) => p.IsValid() && Scene.GetAllComponents<SwrpShipConsole>().Any( c => c.WorldPosition.Distance( p.WorldPosition ) < 260f );

	// ----- test helpers (host) -----

	[ConCmd( "swrp_alert", Help = "swrp_alert <1-5> [reason] - host only: set the alert level as the bridge (same as /alert)" )]
	public static void AlertCommand( int level, string reason = "" )
	{
		if ( !Networking.IsHost || Current is not { } ship ) return;
		ship._hostAction = 100;
		ship.SetAlert( Connection.Local, null, level, reason );
	}

	[ConCmd( "swrp_pa", Help = "swrp_pa \"<text>\" - host only: make a ship announcement (same as /pa)" )]
	public static void PaCommand( string text )
	{
		if ( !Networking.IsHost || Current is not { } ship ) return;
		ship._hostAnnounce = 100;
		ship.Announce( Connection.Local, SwrpPlayer.Local, text );
	}
}

/// <summary>
/// A bridge command console: E opens the ship systems menu (<see cref="ShipConsoleMenu"/>) to set the alert level and make
/// announcements. Officers and staff only; everyone else gets the current alert level. Built from stock boxes.
/// </summary>
[Title( "SWRP Ship Console" ), Category( "SWRP" ), Icon( "settings_input_antenna" )]
public sealed class SwrpShipConsole : Component
{
	[Property] public string ConsoleName { get; set; } = "Bridge Command Console";

	ModelRenderer _screen;
	PointLight _light;

	protected override void OnStart()
	{
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box.Bounds.Size;
		Part( "Pedestal", new Vector3( 18, 34, 36 ), new Vector3( -2, 0, 18 ), new Color( 0.1f, 0.11f, 0.13f ) );
		Part( "Desk", new Vector3( 26, 44, 4 ), new Vector3( 0, 0, 38 ), new Color( 0.22f, 0.24f, 0.28f ), new Angles( -18, 0, 0 ) );
		_screen = Part( "Screen", new Vector3( 20, 38, 1 ), new Vector3( 0.6f, 0, 40.6f ), new Color( 0.3f, 0.7f, 1f ), new Angles( -18, 0, 0 ) );

		var lightGo = new GameObject( GameObject, true, "Console Light" );
		lightGo.NetworkMode = NetworkMode.Never;
		lightGo.Flags |= GameObjectFlags.NotSaved;
		lightGo.LocalPosition = new Vector3( 10, 0, 52 );
		_light = lightGo.AddComponent<PointLight>();
		_light.Radius = 120f;
		_light.Shadows = false;

		var col = AddComponent<BoxCollider>();
		col.Center = new Vector3( 0, 0, 21 );
		col.Scale = new Vector3( 28, 46, 44 );

		ModelRenderer Part( string name, Vector3 size, Vector3 pos, Color tint, Angles? angles = null )
		{
			var go = new GameObject( GameObject, true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = pos;
			go.LocalRotation = ( angles ?? Angles.Zero ).ToRotation();
			go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = box;
			r.Tint = tint;
			return r;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_screen.IsValid() || SwrpShip.Current is not { } ship ) return;
		var c = Color.Parse( ship.Alert.Hex ) ?? Color.White;
		if ( ship.AlertLevel == 5 ) c = new Color( 0.3f, 0.7f, 1f );
		var pulse = ship.AlertLevel <= 2 ? 0.7f + 0.3f * MathF.Sin( Time.Now * 5f ) : 1f;
		_screen.Tint = c * pulse;
		if ( _light.IsValid() ) _light.LightColor = c * 0.8f * pulse;
	}
}
