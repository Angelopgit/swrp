using System;

namespace Swrp;

/// <summary>
/// Getting new troopers going and finding the way round the Venator. The host keeps the server rules (swrp/rules.json,
/// written with defaults the first time; bump Version after editing it so everyone accepts again) and who accepted
/// them; every client gets them on join and must accept them once (the Escape menu's RULES page opens by itself). The
/// GUIDE page (F2, or /guide) has the first steps, every key, and directions. Directions and the ship tour run on your
/// machine: a marker on the HUD walks you there, taking the turbolift or a teleport pad when the place is on another deck.
///
/// Chat: /rules, /guide, /keys, /directions &lt;place&gt; (or /dir, /where is), /guide tour, /where &lt;trooper&gt; (officers, staff).
/// </summary>
public static class SwrpGuide
{
	const string RulesFile = "swrp/rules.json";
	const string AcceptedFile = "swrp/rules_accepted.json";

	public sealed class RulesData
	{
		/// <summary> Raise after changing the rules: everyone has to accept the new version. </summary>
		public int Version { get; set; } = 1;
		public string Title { get; set; } = "SERVER RULES";
		public string Welcome { get; set; } = "Welcome aboard. This is a serious roleplay server: you are a clone trooper serving on a Venator-class Star Destroyer.";
		public List<string> Lines { get; set; } = new()
		{
			"Stay in character in local and radio chat. Out of character talk goes in /ooc or /looc.",
			"Follow the chain of command. Orders come from officers; disagree in character or take it to staff.",
			"No random killing or friendly fire. Combat needs a roleplay reason or a staff-run event.",
			"No metagaming: don't act on what your character couldn't know (OOC chat, Discord, other players' screens).",
			"No powergaming: give others a fair chance to react in /me and /do roleplay.",
			"Respect everyone. No harassment, slurs or hate speech, in character or out.",
			"Don't exploit bugs or abuse tools. Report bugs and problems with /report.",
			"Staff decisions stand. Appeal calmly through staff, not in public chat.",
		};
	}

	static RulesData _rules;
	static Dictionary<ulong, int> _accepted;

	static RulesData Rules
	{
		get
		{
			if ( _rules is not null ) return _rules;
			_rules = FileSystem.Data.ReadJson<RulesData>( RulesFile, null );
			if ( _rules is null )
			{
				_rules = new RulesData();
				try
				{
					FileSystem.Data.CreateDirectory( "swrp" );
					FileSystem.Data.WriteJson( RulesFile, _rules );     // so the host has a file to edit
				}
				catch ( Exception e )
				{
					Log.Warning( $"[SWRP] Could not write the default rules: {e.Message}" );
				}
			}
			return _rules;
		}
	}

	static Dictionary<ulong, int> Accepted => _accepted ??= FileSystem.Data.ReadJson<Dictionary<ulong, int>>( AcceptedFile, null ) ?? new();

	[ConCmd( "swrp_rules_reload", Help = "Host: reload swrp/rules.json and send it to everyone (raise Version in it to make everyone accept again)" )]
	public static void ReloadRules()
	{
		if ( !Networking.IsHost ) return;
		_rules = null;
		foreach ( var c in Connection.All ) SendRules( c );
		Log.Info( $"[SWRP] rules v{Rules.Version} sent to {Connection.All.Count} players" );
	}

	/// <summary> Host: someone joined. Rules, qualifications and the board go to them. </summary>
	public static void OnJoin( Connection c )
	{
		SendRules( c );
		SwrpCareer.SendQuals( c );
		SendBoard( c );
		SwrpEvents.SendState( c );
	}

	static void SendRules( Connection c )
	{
		if ( c is null ) return;
		var r = Rules;
		var needs = !c.IsHost && ( !Accepted.TryGetValue( c.SteamId.ValueUnsigned, out var v ) || v < r.Version );
		using ( Rpc.FilterInclude( c ) ) ReceiveRules( r.Version, r.Title ?? "SERVER RULES", r.Welcome ?? "", string.Join( "\n", r.Lines ?? new() ), needs );
	}

	[Rpc.Host]
	public static void AcceptRules( int version )
	{
		var c = Rpc.Caller;
		if ( c is null || version < Rules.Version ) return;
		Accepted[c.SteamId.ValueUnsigned] = Rules.Version;
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( AcceptedFile, Accepted );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not save rule acceptances: {e.Message}" );
		}
	}

	// ----- client: the rules -----

	public static int RulesVersion { get; private set; }
	public static string RulesTitle { get; private set; } = "SERVER RULES";
	public static string RulesWelcome { get; private set; } = "";
	public static List<string> RulesLines { get; private set; } = new();
	/// <summary> Client: the rules still need accepting on this machine. </summary>
	public static bool MustAccept { get; private set; }

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveRules( int version, string title, string welcome, string lines, bool needsAccept )
	{
		RulesVersion = version;
		RulesTitle = title;
		RulesWelcome = welcome;
		RulesLines = ( lines ?? "" ).Split( '\n', StringSplitOptions.RemoveEmptyEntries ).ToList();
		MustAccept = needsAccept;
	}

	public static void AcceptLocal()
	{
		MustAccept = false;
		AcceptRules( RulesVersion );
		SwrpChat.AddLocal( ChatKind.System, "Thanks. F2 (or /guide) opens the guide: first steps, every key and directions round the ship." );
	}

	// ----- the board: Trooper of the Week (shown on the Tab board and the guide) -----

	public static string BoardTotw { get; private set; }

	public static void BroadcastBoard()
	{
		foreach ( var c in Connection.All ) SendBoard( c );
	}

	static void SendBoard( Connection c )
	{
		var t = SwrpCareer.TrooperOfTheWeek;
		var text = t is null ? "" : $"{t.Trooper} ({t.Battalion}){( string.IsNullOrWhiteSpace( t.Reason ) ? "" : $": {t.Reason}" )}";
		using ( Rpc.FilterInclude( c ) ) ReceiveBoard( text );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveBoard( string totw ) => BoardTotw = string.IsNullOrWhiteSpace( totw ) ? null : totw;

	// ----- keys -----

	public static readonly (string Group, (string Key, string What)[] Keys)[] KeyList =
	{
		( "MOVING AND LOOKING", new[]
		{
			( "W A S D / Shift / Space / Ctrl", "Move, sprint, jump, crouch" ),
			( "C", "Third person; hold Alt to look around, Alt + wheel zooms" ),
			( "E", "Use doors, consoles, lifts, items; hold on a downed trooper to revive" ),
			( "F8", "Hide the HUD for screenshots" ),
			( "F2", "This guide" ),
			( "Tab (hold)", "Scoreboard and mission board" ),
			( "Esc", "Menu: guide, rules, scoreboard, characters, settings" ),
		} ),
		( "KIT AND COMBAT", new[]
		{
			( "1 - 5 / wheel", "Quick slots: take out what's on that slot" ),
			( "H", "Put away or take out what's in your hands" ),
			( "Mouse 1 / Mouse 2 / R", "Fire, aim down the sights, reload" ),
			( "X", "Stun setting on and off" ),
			( "T", "Throw a thermal detonator" ),
			( "G", "Drop what's in your hands" ),
			( "I", "Inventory" ),
			( "Right mouse, empty hands (hold)", "Emotes: salute, at ease and more" ),
		} ),
		( "COMMS", new[]
		{
			( "Enter", "Chat (/help for every command)" ),
			( "V (hold)", "Talk" ),
			( "B", "Voice range: whisper, normal, yell" ),
			( "Y", "Your voice on the radio net instead of just your helmet" ),
			( "N", "Radio receiver on or off" ),
			( "/channel", "Radio net: squad, legion, command, open, pa (Captain+)" ),
		} ),
		( "WHEN YOU'RE HIT", new[]
		{
			( "E (downed)", "Call a medic" ),
			( "K (hold, downed)", "Give up and respawn at the medbay" ),
		} ),
		( "UNITS AND MENUS", new[]
		{
			( "F4", "Character screen" ),
			( "L", "Battalion roster" ),
			( "P", "Jedi training (Jedi only)" ),
			( "F / Z / Mouse 3", "Jedi: Force power, Force wheel, saber throw" ),
			( "O / J / Q (hold)", "Staff panel, Game Master menu, spawn menu (staff)" ),
		} ),
	};

	// ----- places, directions and the tour (client) -----

	public static readonly (string Id, string Name, string[] Words)[] Places =
	{
		( "armory", "Armory", new[] { "armory", "armoury", "weapons", "guns" } ),
		( "medbay", "Medbay", new[] { "medbay", "medical", "bacta", "hospital" } ),
		( "lift", "Turbolift", new[] { "lift", "turbolift", "elevator" } ),
		( "sim", "Simulation rooms", new[] { "sim", "sims", "simulation", "training", "course" } ),
		( "bridge", "Bridge", new[] { "bridge", "command" } ),
		( "holomap", "Holo-map", new[] { "holomap", "holo", "map" } ),
		( "hangar", "Hangar", new[] { "hangar", "ships", "laat" } ),
		( "brig", "Brig", new[] { "brig", "jail", "cells" } ),
		( "briefing", "Briefing table", new[] { "briefing", "briefings", "warroom", "war" } ),
		( "barracks", "Barracks", new[] { "barracks", "spawn", "bunks" } ),
	};

	static readonly string[] TourSteps = { "armory", "medbay", "lift", "sim" };

	public static string WaypointLabel { get; private set; }
	public static Vector3? WaypointTarget { get; private set; }
	/// <summary> Where the marker points right now (the place, or the lift or pad on the way). </summary>
	public static Vector3? MarkerAt { get; private set; }
	public static string MarkerLabel { get; private set; }
	public static int TourStep { get; private set; } = -1;
	static string _placeId;

	public static string PlaceName( string id ) => Places.FirstOrDefault( p => p.Id == id ).Name ?? id;

	/// <summary> The place a word asks for, or null. Door names are searched last, so "/dir cantina" works too. </summary>
	public static string Resolve( string text )
	{
		text = ( text ?? "" ).Trim().ToLowerInvariant().Replace( "the ", "" );
		if ( text.Length == 0 ) return null;
		foreach ( var p in Places )
			if ( p.Id == text || p.Words.Any( w => w == text || text.StartsWith( w ) ) ) return p.Id;
		var scene = Game.ActiveScene;
		var door = scene?.GetAllComponents<SwrpDoor>().FirstOrDefault( d => d.DisplayName?.Contains( text, StringComparison.OrdinalIgnoreCase ) == true );
		return door is null ? null : $"door:{door.DisplayName}";
	}

	/// <summary> Client: the nearest instance of a place (same deck first). </summary>
	public static Vector3? Locate( string id, Vector3 from, out string name )
	{
		name = id.StartsWith( "door:" ) ? id[5..] : PlaceName( id );
		var scene = Game.ActiveScene;
		if ( scene is null ) return null;
		IEnumerable<Vector3> points = id switch
		{
			"armory" => scene.GetAllComponents<SwrpArmory>().Select( c => c.WorldPosition ),
			"medbay" => scene.GetAllComponents<SwrpBactaPod>().Select( c => c.WorldPosition ).Concat( scene.GetAllComponents<SwrpBactaStation>().Select( c => c.WorldPosition ) ),
			"lift" => LiftDoors( scene ),
			"sim" => scene.GetAllComponents<SwrpTrainingCourse>().Select( c => c.WorldPosition ),
			"bridge" => scene.GetAllComponents<SwrpShipConsole>().Select( c => c.WorldPosition ),
			"holomap" => scene.GetAllComponents<SwrpHoloMap>().Select( c => c.WorldPosition ),
			"hangar" => scene.GetAllComponents<SwrpHangarConsole>().Select( c => c.WorldPosition ),
			"brig" => scene.GetAllComponents<SwrpBrigConsole>().Select( c => c.WorldPosition ),
			"briefing" => scene.GetAllComponents<SwrpBriefingTable>().Select( c => c.WorldPosition ),
			"barracks" => scene.GetAllComponents<SwrpSpawnPoint>().Select( c => c.WorldPosition ),
			_ when id.StartsWith( "door:" ) => scene.GetAllComponents<SwrpDoor>().Where( d => d.DisplayName == id[5..] ).Select( d => d.WorldPosition ),
			_ => Enumerable.Empty<Vector3>(),
		};
		var list = points.ToList();
		if ( list.Count == 0 ) return null;
		var deck = SwrpShip.DeckName( from );
		return list.OrderBy( p => SwrpShip.DeckName( p ) == deck ? 0 : 1 ).ThenBy( p => p.Distance( from ) ).First();
	}

	static IEnumerable<Vector3> LiftDoors( Scene scene )
	{
		foreach ( var lift in scene.GetAllComponents<SwrpTurbolift>() )
			for ( var i = 0; i < lift.Decks.Count; i++ )
				if ( lift.DoorOf( i ) is { } d && d.IsValid() ) yield return d.WorldPosition;
	}

	/// <summary> Client: point the HUD marker at a place. </summary>
	public static bool SetDestination( string id )
	{
		var me = SwrpPlayers.Local;
		if ( me is null ) return false;
		var at = Locate( id, me.WorldPosition, out var name );
		if ( at is null )
		{
			SwrpChat.AddLocal( ChatKind.System, $"There's no {name} on this ship." );
			return false;
		}
		_placeId = id;
		WaypointTarget = at;
		WaypointLabel = name;
		_sinceRoute = 10f;
		SwrpChat.AddLocal( ChatKind.System, $"Directions to the {name}: follow the marker{( SwrpShip.DeckName( at.Value ) != SwrpShip.DeckName( me.WorldPosition ) ? $" ({SwrpShip.DeckName( at.Value )})" : "" )}. /dir off clears it." );
		return true;
	}

	public static void ClearDestination()
	{
		_placeId = null;
		WaypointTarget = null;
		WaypointLabel = null;
		MarkerAt = null;
		MarkerLabel = null;
		TourStep = -1;
	}

	public static void StartTour()
	{
		TourStep = 0;
		SwrpChat.AddLocal( ChatKind.System, "Ship tour: the marker takes you to the armory, the medbay, a turbolift and the simulation rooms. /dir off stops it." );
		var step = TourStep;
		if ( !SetDestination( TourSteps[0] ) ) TourStep = -1;
		else TourStep = step;
	}

	static RealTimeSince _sinceRoute;

	/// <summary> Client, every frame: steer the marker, via a lift or pad when the place is on another deck. </summary>
	public static void Tick()
	{
		var me = SwrpPlayers.Local;
		if ( me is null || WaypointTarget is not { } target ) { MarkerAt = null; return; }
		if ( _sinceRoute < 0.4f ) return;
		_sinceRoute = 0;

		var here = me.WorldPosition;
		if ( here.Distance( target ) < 170f )
		{
			if ( TourStep >= 0 && TourStep < TourSteps.Length - 1 )
			{
				SwrpChat.AddLocal( ChatKind.System, $"Tour: this is the {WaypointLabel}. {TourBlurb( TourSteps[TourStep] )}" );
				var next = TourStep + 1;
				SetDestination( TourSteps[next] );
				TourStep = next;
				return;
			}
			SwrpChat.AddLocal( ChatKind.System, TourStep >= 0 ? $"Tour: this is the {WaypointLabel}. {TourBlurb( TourSteps[TourStep] )} That's the tour done." : $"You've reached the {WaypointLabel}." );
			if ( TourStep >= 0 ) SwrpCareer.TourDone();
			ClearDestination();
			return;
		}

		var myDeck = SwrpShip.DeckName( here );
		var theirDeck = SwrpShip.DeckName( target );
		if ( myDeck == theirDeck )
		{
			MarkerAt = target;
			MarkerLabel = WaypointLabel;
			return;
		}

		var scene = Game.ActiveScene;
		// a pad from here straight there (bridge lifts, simulation rooms, temple)
		var pad = scene.GetAllComponents<SwrpTeleporter>()
			.Where( t => SwrpShip.DeckName( t.WorldPosition ) == myDeck && t.Targets.Any( d => SwrpShip.DeckName( d.Pos ) == theirDeck ) )
			.OrderBy( t => t.WorldPosition.Distance( here ) ).FirstOrDefault();
		if ( pad is not null )
		{
			MarkerAt = pad.WorldPosition;
			MarkerLabel = $"Pad to the {theirDeck}";
			return;
		}

		// otherwise the turbolift, to the deck a pad leaves from if the place is only reachable by pad
		var via = scene.GetAllComponents<SwrpTeleporter>().FirstOrDefault( t => t.Targets.Any( d => SwrpShip.DeckName( d.Pos ) == theirDeck ) ) is { } far
			? SwrpShip.DeckName( far.WorldPosition ) : theirDeck;
		var lift = LiftDoors( scene ).Where( p => SwrpShip.DeckName( p ) == myDeck ).OrderBy( p => p.Distance( here ) ).Cast<Vector3?>().FirstOrDefault();
		MarkerAt = lift ?? target;
		MarkerLabel = lift is null ? WaypointLabel : $"Turbolift to the {via}";
	}

	static string TourBlurb( string id ) => id switch
	{
		"armory" => "E on a rack takes a weapon (Corporal and up open the door); blaster cells refill your magazines.",
		"medbay" => "Bacta pods and stations heal and stop bleeding. You respawn here after bleeding out.",
		"lift" => "E calls the car, E on the panel inside picks a deck.",
		"sim" => "Course consoles start a 40-second target run; your best goes on the board (/training).",
		_ => "",
	};

	// ----- chat -----

	/// <summary> Client: commands handled on this machine. </summary>
	public static bool TryLocal( string cmd, string rest )
	{
		if ( SquadMenu.TryLocal( cmd, rest ) ) return true;
		if ( SwrpLocTag.TryLocal( cmd, rest ) ) return true;
		if ( SwrpArmorRegistry.TryLocal( cmd, rest ) ) return true;      // /helmet, /armor      // "/loc [note]": a location tag      // "/squad" on its own opens the squad menu
		switch ( cmd )
		{
			case "/guide":
				if ( rest.Trim().Equals( "tour", StringComparison.OrdinalIgnoreCase ) ) { StartTour(); return true; }
				EscapeMenu.Show( "guide" );
				return true;
			case "/keys":
				EscapeMenu.Show( "keys" );
				return true;
			case "/rules":
			case "/motd":
				EscapeMenu.Show( "rules" );
				return true;
			case "/directions":
			case "/dir":
			case "/goto-place":
			{
				var r = rest.Trim();
				if ( r is "off" or "stop" or "clear" or "cancel" ) { ClearDestination(); SwrpChat.AddLocal( ChatKind.System, "Directions cleared." ); return true; }
				var id = Resolve( r );
				if ( id is null )
				{
					SwrpChat.AddLocal( ChatKind.System, $"Directions to: {string.Join( ", ", Places.Select( p => p.Id ) )}, or part of a door's name. /dir off clears." );
					return true;
				}
				SetDestination( id );
				return true;
			}
		}
		return false;
	}

	/// <summary> Host: /where &lt;trooper&gt; for officers and staff. </summary>
	public static bool TryChat( Connection caller, SwrpPlayer me, string cmd, string rest )
	{
		if ( cmd != "/where" ) return false;
		var officer = Whitelist.IsStaff( caller ) || me.IsValid() && me.CharacterId is not null && me.RankIndex >= SwrpRoleplay.LieutenantRank;
		if ( rest.Trim().Length == 0 )
		{
			if ( me.IsValid() ) SwrpChat.SystemTo( caller, $"You are on the {SwrpShip.DeckName( me.WorldPosition )}." );
			return true;
		}
		if ( !officer ) { SwrpChat.SystemTo( caller, "Locating troopers is for officers (2nd Lieutenant+) and staff. /dir <place> gives directions." ); return true; }
		var conn = SwrpNet.FindConnection( rest, out var problem );
		var p = conn is null ? null : SwrpGameManager.Current?.FindPlayer( conn );
		if ( !p.IsValid() || p.CharacterId is null || p.Invisible && !Whitelist.IsStaff( caller ) ) { SwrpChat.SystemTo( caller, problem ?? $"No deployed trooper matches '{rest}'." ); return true; }
		SwrpChat.SystemTo( caller, $"{p.FullTitle}: {SwrpShip.DeckName( p.WorldPosition )}{( SwrpRoleplay.Current?.SquadOf( p.OwnerSteamId ) is { } sq ? $", squad {sq.Name}" : "" )}." );
		return true;
	}
}
