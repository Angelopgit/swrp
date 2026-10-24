using System;

namespace Swrp;

/// <summary>
/// A trooper's career beyond rank: qualifications (which gate the Heavy, Specialist, Medic and Officer classes, like the job
/// whitelists of Garry's Mod SWRP servers), cadet training run by Training Officers, medals and commendations, leave of
/// absence, the Trooper of the Week and battalion statistics. Host side, saved in swrp/career.json; chat commands land
/// here from <see cref="SwrpChat"/> through <see cref="TryChat"/>.
///
/// /qual [list|grant|revoke] · /class [trooper] &lt;id&gt; · /train · /commend · /medals · /loa · /stats · /totw
/// </summary>
public static class SwrpCareer
{
	const string FilePath = "swrp/career.json";

	[ConVar( "swrp_class_quals", Help = "Host: Heavy, Specialist, Medic and Officer need a qualification from a battalion officer (1, default) or are open to all (0)", Saved = true )]
	public static bool ClassQuals { get; set; } = true;

	/// <summary> Qualifications officers can grant. Those named by a class's <see cref="ClassDefinition.Qual"/> unlock it; the rest are titles that show on the roster and /examine. </summary>
	public static readonly (string Id, string Name)[] Quals =
	{
		( "heavy", "Heavy Weapons" ),
		( "sniper", "Specialist Marksman" ),
		( "medic", "Combat Medic" ),
		( "officer", "Officer Candidate" ),
		( "trainer", "Training Officer" ),
		( "arc", "ARC Trooper" ),
		( "pilot", "Pilot" ),
		( "engineer", "Engineer" ),
	};

	/// <summary> Class id to the qualification it needs (null: open). The one list is the class table (Data/Classes.cs). </summary>
	public static string QualForClass( string classId ) => ClassRegistry.Get( classId )?.Qual;

	/// <summary> The class a qualification unlocks (not Officer: that comes with rank), or null. </summary>
	static ClassDefinition ClassForQual( string qual ) => ClassRegistry.All.FirstOrDefault( c => c.Qual == qual && c.Id != ClassRegistry.Officer );

	public static readonly (string Id, string Name)[] Medals =
	{
		( "clone_cross", "Clone Cross" ),
		( "valor", "Medal of Valor" ),
		( "combat_medic", "Combat Medic Badge" ),
		( "marksman", "Marksman Ribbon" ),
		( "pilot_wings", "Pilot Wings" ),
		( "service_star", "Service Star" ),
		( "wound_stripe", "Wound Stripe" ),
		( "leadership", "Leadership Citation" ),
	};

	/// <summary> The cadet training checklist. Items 5 and 6 tick themselves (a simulation course run, the ship tour route). </summary>
	public static readonly string[] Checklist =
	{
		"Chain of command: ranks, battalions, who gives orders",
		"Drill: salutes, at ease, formations (emotes)",
		"Comms: /r, Y for radio voice, channels, radio etiquette",
		"Medical: call a medic (E downed), revive (hold E), bacta",
		"Simulation room: one course run",
		"Ship tour: armory, medbay, turbolift, simulation rooms (/guide tour)",
	};

	public const int SimItem = 4, TourItem = 5;

	// ----- saved data -----

	public sealed class MedalEntry
	{
		public ulong SteamId { get; set; }
		public string CharacterId { get; set; }
		public string Trooper { get; set; }
		public string Battalion { get; set; }
		public string Medal { get; set; }
		public string Citation { get; set; }
		public string By { get; set; }
		public DateTime When { get; set; }
	}

	public sealed class LeaveEntry
	{
		public DateTime Until { get; set; }
		public string Reason { get; set; }
		public DateTime Since { get; set; }
	}

	public sealed class UnitStats
	{
		public int Revives { get; set; }
		public int NpcKills { get; set; }
		public int CadetsPassed { get; set; }
		public int SimRuns { get; set; }
		public int BestSim { get; set; }
	}

	public sealed class Cadet
	{
		public List<int> Done { get; set; } = new();
		public List<string> Notes { get; set; } = new();
	}

	public sealed class Totw
	{
		public string Trooper { get; set; }
		public string Battalion { get; set; }
		public string Reason { get; set; }
		public string By { get; set; }
		public DateTime When { get; set; }
	}

	public sealed class CareerFile
	{
		/// <summary> "steamid:battalion" to the qualifications held in that battalion. </summary>
		public Dictionary<string, List<string>> Quals { get; set; } = new();
		public List<MedalEntry> Medals { get; set; } = new();
		public Dictionary<ulong, LeaveEntry> Leave { get; set; } = new();
		/// <summary> Per battalion id; per trooper under "steamid:characterid". </summary>
		public Dictionary<string, UnitStats> Stats { get; set; } = new();
		/// <summary> "steamid:characterid" of cadets in training. </summary>
		public Dictionary<string, Cadet> Cadets { get; set; } = new();
		public Totw TrooperOfTheWeek { get; set; }
	}

	static CareerFile _data;
	static bool _dirty;
	static RealTimeSince _sinceSave;

	static CareerFile Data => _data ??= FileSystem.Data.ReadJson<CareerFile>( FilePath, null ) ?? new CareerFile();

	public static void Reload() => _data = null;

	static void Save( bool now = true )
	{
		_dirty = true;
		if ( !now && _sinceSave < 20f ) return;     // statistics tick often: those are written at most every 20 s
		_dirty = false;
		_sinceSave = 0;
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( FilePath, Data );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not save the career file: {e.Message}" );
		}
	}

	/// <summary> Host, now and then: write pending statistics. </summary>
	public static void Flush()
	{
		if ( _dirty && _sinceSave > 20f ) Save();
	}

	static string Key( ulong steamId, string battalion ) => $"{steamId}:{battalion}";
	static string Key( SwrpPlayer p ) => $"{p.OwnerSteamId}:{p.CharacterId}";

	// ----- queries -----

	public static bool HasQual( ulong steamId, string battalion, string qual )
		=> Data.Quals.TryGetValue( Key( steamId, battalion ), out var list ) && list.Contains( qual );

	public static List<string> QualsOf( ulong steamId, string battalion )
		=> Data.Quals.TryGetValue( Key( steamId, battalion ), out var list ) ? list : new List<string>();

	public static string QualName( string id ) => Quals.FirstOrDefault( q => q.Id == id ).Name ?? id;

	/// <summary> Host: may this player take that class in that battalion? Admins always may. </summary>
	public static bool MayTakeClass( ulong steamId, string battalion, string classId, out string error )
	{
		error = null;
		var qual = QualForClass( classId );
		if ( !ClassQuals || qual is null || battalion == FactionRegistry.CloneTrooper && qual != "officer" ) return true;
		if ( Whitelist.GetRole( steamId ) >= StaffRole.Admin || HasQual( steamId, battalion, qual ) ) return true;
		error = $"The {ClassRegistry.Get( classId )?.Name ?? classId} class needs the {QualName( qual )} qualification in this battalion. Ask one of its officers (/qual grant).";
		return false;
	}

	public static LeaveEntry LeaveOf( ulong steamId )
	{
		if ( !Data.Leave.TryGetValue( steamId, out var l ) ) return null;
		if ( l.Until > DateTime.UtcNow ) return l;
		Data.Leave.Remove( steamId );
		Save();
		return null;
	}

	public static IEnumerable<MedalEntry> MedalsOf( ulong steamId, string characterId )
		=> Data.Medals.Where( m => m.SteamId == steamId && m.CharacterId == characterId );

	public static UnitStats StatsOf( string key ) => Data.Stats.TryGetValue( key, out var s ) ? s : Data.Stats[key] = new UnitStats();

	public static Totw TrooperOfTheWeek => Data.TrooperOfTheWeek;

	/// <summary> One line for /examine: qualifications and medals. Null when there's nothing to say. </summary>
	public static string ExamineLine( SwrpPlayer p )
	{
		var parts = new List<string>();
		var quals = QualsOf( p.OwnerSteamId, p.FactionId );
		if ( quals.Count > 0 ) parts.Add( "Qualified: " + string.Join( ", ", quals.Select( QualName ) ) );
		var medals = MedalsOf( p.OwnerSteamId, p.CharacterId ).GroupBy( m => m.Medal ).Select( g => g.Count() > 1 ? $"{MedalName( g.Key )} x{g.Count()}" : MedalName( g.Key ) ).ToList();
		if ( medals.Count > 0 ) parts.Add( "Decorations: " + string.Join( ", ", medals ) );
		if ( LeaveOf( p.OwnerSteamId ) is { } l ) parts.Add( $"On leave until {l.Until:d MMM}" );
		return parts.Count == 0 ? null : string.Join( ". ", parts ) + ".";
	}

	static string MedalName( string id ) => Medals.FirstOrDefault( m => m.Id == id ).Name ?? id;

	// ----- hooks from the rest of the game (host) -----

	public static void OnRevive( SwrpPlayer medic )
	{
		if ( !medic.IsValid() || medic.CharacterId is null ) return;
		StatsOf( medic.FactionId ).Revives++;
		StatsOf( Key( medic ) ).Revives++;
		Save( false );
	}

	public static void OnNpcKill( SwrpPlayer killer )
	{
		if ( !killer.IsValid() || killer.CharacterId is null ) return;
		StatsOf( killer.FactionId ).NpcKills++;
		StatsOf( Key( killer ) ).NpcKills++;
		Save( false );
	}

	public static void OnSimRun( SwrpPlayer runner, int score )
	{
		if ( !runner.IsValid() || runner.CharacterId is null ) return;
		foreach ( var s in new[] { StatsOf( runner.FactionId ), StatsOf( Key( runner ) ) } )
		{
			s.SimRuns++;
			s.BestSim = Math.Max( s.BestSim, score );
		}
		if ( Data.Cadets.TryGetValue( Key( runner ), out var c ) && !c.Done.Contains( SimItem ) )
		{
			c.Done.Add( SimItem );
			Tell( runner, "Training: simulation course run ticked off. /train shows your checklist." );
		}
		Save();
	}

	/// <summary> Host: the client finished the guided ship tour. </summary>
	[Rpc.Host]
	public static void TourDone()
	{
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		if ( !p.IsValid() || p.CharacterId is null ) return;
		if ( Data.Cadets.TryGetValue( Key( p ), out var c ) && !c.Done.Contains( TourItem ) )
		{
			c.Done.Add( TourItem );
			Save();
			Tell( p, "Training: ship tour ticked off. /train shows your checklist." );
		}
	}

	/// <summary> Host: a character just deployed; cadets get a training card. </summary>
	public static void OnDeployed( SwrpPlayer p )
	{
		if ( !p.IsValid() || p.CharacterId is null || p.RankIndex != 0 || p.FactionId == "jedi" ) return;
		if ( !Data.Cadets.ContainsKey( Key( p ) ) )
		{
			Data.Cadets[Key( p )] = new Cadet();
			Save();
		}
		Tell( p, "You're a Cadet. Pass cadet training to become a Private: /train shows the checklist, /guide tour walks you round the ship, and any Training Officer can run you through the rest." );
	}

	static void Tell( SwrpPlayer p, string text )
	{
		if ( p.Network.Owner is { } c ) SwrpChat.SystemTo( c, text );
	}

	// ----- qualifications to the client (the character screen shows which classes are locked) -----

	/// <summary> Client: "battalion:qual" for everything this player holds. </summary>
	public static HashSet<string> LocalQuals { get; private set; } = new();
	public static bool LocalClassQuals { get; private set; } = true;
	public static bool LocalIsAdmin { get; private set; }

	/// <summary> Client: is that class locked for this player in that battalion? </summary>
	public static bool LockedLocal( string battalion, string classId )
	{
		var qual = QualForClass( classId );
		if ( !LocalClassQuals || LocalIsAdmin || qual is null || battalion == FactionRegistry.CloneTrooper && qual != "officer" ) return false;
		return !LocalQuals.Contains( $"{battalion}:{qual}" );
	}

	public static void SendQuals( Connection c )
	{
		if ( c is null ) return;
		var id = c.SteamId.ValueUnsigned;
		var list = Data.Quals.Where( kv => kv.Key.StartsWith( $"{id}:" ) )
			.SelectMany( kv => kv.Value.Select( q => $"{kv.Key[( kv.Key.IndexOf( ':' ) + 1 )..]}:{q}" ) );
		using ( Rpc.FilterInclude( c ) ) ReceiveQuals( string.Join( ",", list ), ClassQuals, Whitelist.GetRole( c ) >= StaffRole.Admin );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveQuals( string csv, bool classQuals, bool admin )
	{
		LocalQuals = ( csv ?? "" ).Split( ',', StringSplitOptions.RemoveEmptyEntries ).ToHashSet();
		LocalClassQuals = classQuals;
		LocalIsAdmin = admin;
	}

	// ----- chat commands -----

	public static bool TryChat( Connection caller, SwrpPlayer me, string cmd, string rest )
	{
		switch ( cmd )
		{
			case "/qual":
			case "/quals":
				QualCommand( caller, me, rest );
				return true;
			case "/class":
				ClassCommand( caller, me, rest );
				return true;
			case "/train":
			case "/training-officer":
				TrainCommand( caller, me, rest );
				return true;
			case "/commend":
			case "/medal":
				Commend( caller, me, rest );
				return true;
			case "/medals":
				ShowMedals( caller, me, rest );
				return true;
			case "/loa":
				LoaCommand( caller, me, rest );
				return true;
			case "/stats":
				StatsCommand( caller, me, rest );
				return true;
			case "/totw":
				TotwCommand( caller, me, rest );
				return true;
		}
		return false;
	}

	static void Say( Connection c, string text ) => SwrpChat.SystemTo( c, text );

	static (string First, string Tail) Split( string args ) => SwrpStaff.Split( args ?? "" );

	/// <summary> An online, deployed trooper by name, or null after telling the caller why not. </summary>
	static SwrpPlayer FindTrooper( Connection caller, string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) { Say( caller, "Name a trooper (character or Steam name, \"quoted\" if it has spaces)." ); return null; }
		var conn = SwrpNet.FindConnection( name, out var problem );
		var p = conn is null ? null : SwrpGameManager.Current?.FindPlayer( conn );
		if ( !p.IsValid() || p.CharacterId is null )
		{
			Say( caller, problem ?? $"No deployed trooper matches '{name}'." );
			return null;
		}
		return p;
	}

	static bool IsStaff( Connection c ) => c is not null && Whitelist.IsStaff( c );

	/// <summary> Staff powers in /qual and /class: Moderator+ by default, set on the staff panel's PERMISSIONS grid (SwrpPermissions). </summary>
	static bool StaffFor( Connection c, string cmd ) => c is not null && Whitelist.GetRole( c ) >= SwrpPermissions.Need( cmd, StaffRole.Moderator );

	/// <summary> Officer authority in the target's battalion (rank or post), or staff. </summary>
	static bool OfficerOver( Connection caller, SwrpPlayer me, SwrpPlayer target, int minAuthority = 0 )
		=> IsStaff( caller ) || SwrpBattalions.Authority( caller, me, target.FactionId ) >= Math.Max( minAuthority, SwrpBattalions.OfficerRank ) && target != me;

	static bool OfficerOver( Connection caller, SwrpPlayer me, SwrpPlayer target, string cmd )
		=> StaffFor( caller, cmd ) || SwrpBattalions.Authority( caller, me, target.FactionId ) >= SwrpBattalions.OfficerRank && target != me;

	// /qual

	static void QualCommand( Connection caller, SwrpPlayer me, string args )
	{
		var (sub, rest) = Split( args );
		sub = sub.ToLowerInvariant();
		if ( sub is "" or "list" )
		{
			var who = sub == "list" && rest.Length > 0 ? FindTrooper( caller, rest ) : me;
			if ( !who.IsValid() || who.CharacterId is null ) { if ( rest.Length == 0 ) Say( caller, "Deploy first, or /qual list <trooper>." ); return; }
			var held = QualsOf( who.OwnerSteamId, who.FactionId );
			Say( caller, $"{who.FullTitle} ({who.Faction.ShortName}): {( held.Count == 0 ? "no qualifications" : string.Join( ", ", held.Select( QualName ) ) )}." );
			var unlocks = ClassRegistry.All.Where( c => c.Qual is not null ).Select( c => $"{c.Qual} = {c.Name}" );
			Say( caller, $"Qualifications: {string.Join( ", ", Quals.Select( q => $"{q.Id} ({q.Name})" ) )}. Classes they unlock: {string.Join( ", ", unlocks )} (Officer also comes with Sergeant){( ClassQuals ? "" : "; off on this server" )}. Officers: /qual grant|revoke <trooper> <qual>, /class <trooper> <class>." );
			return;
		}
		if ( sub is not ( "grant" or "revoke" ) ) { Say( caller, "Usage: /qual, /qual list [trooper], /qual grant <trooper> <qual>, /qual revoke <trooper> <qual>" ); return; }

		var (name, qualText) = Split( rest );
		var target = FindTrooper( caller, name );
		if ( target is null ) return;
		var qual = Quals.FirstOrDefault( q => q.Id.Equals( qualText.Trim(), StringComparison.OrdinalIgnoreCase ) || q.Name.Equals( qualText.Trim(), StringComparison.OrdinalIgnoreCase ) ).Id;
		if ( qual is null ) { Say( caller, $"Unknown qualification '{qualText}'. One of: {string.Join( ", ", Quals.Select( q => q.Id ) )}." ); return; }
		if ( !OfficerOver( caller, me, target, "/qual" ) ) { Say( caller, $"Only officers of the {target.Faction.Name} (2nd Lieutenant+, or its Commander, XO or Jedi General) and staff can do that." ); return; }

		var key = Key( target.OwnerSteamId, target.FactionId );
		if ( !Data.Quals.TryGetValue( key, out var list ) ) Data.Quals[key] = list = new List<string>();
		var grant = sub == "grant";
		if ( grant == list.Contains( qual ) ) { Say( caller, $"{target.FullTitle} {( grant ? "already holds" : "doesn't hold" )} {QualName( qual )}." ); return; }
		if ( grant ) list.Add( qual ); else list.Remove( qual );
		Save();

		var by = me.IsValid() && me.CharacterId is not null ? $"{me.Rank.Abbreviation} {me.CharacterName}" : caller.DisplayName;
		SwrpRanks.RecordNote( by, caller.SteamId.ValueUnsigned, target.FullTitle, target.OwnerSteamId, $"{( grant ? "qualified" : "removed the qualification" )} {QualName( qual )}" );
		SwrpStaffTools.RecordText( caller, grant ? "qual grant" : "qual revoke", $"{target.FullTitle}: {QualName( qual )}" );
		Say( caller, $"{target.FullTitle} {( grant ? "is now qualified" : "no longer holds" )} {QualName( qual )}." );
		var unlocked = ClassForQual( qual );
		Tell( target, grant ? $"You are now qualified: {QualName( qual )} ({by}).{( unlocked is null ? "" : $" /class {unlocked.Id} makes you a {unlocked.Name}." )}" : $"Your {QualName( qual )} qualification was removed ({by})." );
		SendQuals( target.Network.Owner );

		// revoked while serving in that class: back to Trooper
		if ( !grant && unlocked is not null && target.Class.Id == unlocked.Id ) SetClass( target, ClassRegistry.Trooper );
	}

	// /class

	static readonly Dictionary<ulong, RealTimeSince> _sinceClass = new();

	static ClassDefinition FindClass( SwrpPlayer who, string text )
		=> ClassRegistry.For( who.FactionId ).FirstOrDefault( c => c.Id.Equals( text.Trim(), StringComparison.OrdinalIgnoreCase ) || c.Name.Equals( text.Trim(), StringComparison.OrdinalIgnoreCase ) );

	static void ClassCommand( Connection caller, SwrpPlayer me, string args )
	{
		var (first, tail) = Split( args );
		// /class <trooper> <class>: the trooper's officers and staff set it; the class's qualification comes with it, so /qual agrees
		if ( tail.Length > 0 )
		{
			var target = FindTrooper( caller, first );
			if ( target is null ) return;
			if ( !OfficerOver( caller, me, target, "/class" ) ) { Say( caller, $"Only officers of the {target.Faction.Name} (2nd Lieutenant+, or its Commander, XO or Jedi General) and staff can set a trooper's class." ); return; }
			var pick = FindClass( target, tail );
			if ( pick is null ) { Say( caller, $"Unknown class '{tail}'. One of: {string.Join( ", ", ClassRegistry.For( target.FactionId ).Select( c => c.Id ) )}." ); return; }
			if ( ClassRegistry.ForRank( target.FactionId, pick.Id, target.RankIndex ) == target.Class.Id ) { Say( caller, $"{target.FullTitle} is already a {target.Class.Name}." ); return; }
			var by = me.IsValid() && me.CharacterId is not null ? $"{me.Rank.Abbreviation} {me.CharacterName}" : caller.DisplayName;
			if ( pick.Qual is { } q && !HasQual( target.OwnerSteamId, target.FactionId, q ) )
			{
				var key = Key( target.OwnerSteamId, target.FactionId );
				if ( !Data.Quals.TryGetValue( key, out var list ) ) Data.Quals[key] = list = new List<string>();
				list.Add( q );
				Save();
				SendQuals( target.Network.Owner );
			}
			SwrpRanks.RecordNote( by, caller.SteamId.ValueUnsigned, target.FullTitle, target.OwnerSteamId, $"set the class to {pick.Name}" );
			SwrpStaffTools.RecordText( caller, "class", $"{target.FullTitle}: {pick.Name}" );
			var now = SetClass( target, pick.Id );
			Say( caller, $"{target.FullTitle} is now a {pick.Name}{( now ? "" : " from their next deploy" )}." );
			Tell( target, $"{by} made you a {pick.Name}." );
			return;
		}

		if ( !me.IsValid() || me.CharacterId is null ) { Say( caller, "Deploy first." ); return; }
		var options = ClassRegistry.For( me.FactionId ).ToList();
		var want = FindClass( me, args ?? "" );
		if ( want is null )
		{
			Say( caller, $"You are a {me.Class.Name}. /class <{string.Join( "|", options.Select( c => c.Id ) )}> switches once you hold its qualification (you redeploy at a spawn point). Officers: /class <trooper> <class>." );
			return;
		}
		if ( ClassRegistry.ForRank( me.FactionId, want.Id, me.RankIndex ) == me.Class.Id ) { Say( caller, $"You're already a {me.Class.Name}{( me.Class.Id == ClassRegistry.Officer ? " (Sergeant and above serve as Officers)" : "" )}." ); return; }
		if ( _sinceClass.TryGetValue( caller.SteamId.ValueUnsigned, out var since ) && since < 30f && !IsStaff( caller ) ) { Say( caller, "You just changed class. Give it a moment." ); return; }
		if ( !MayTakeClass( caller.SteamId.ValueUnsigned, me.FactionId, want.Id, out var error ) ) { Say( caller, error ); return; }
		if ( !CanRedeploy( me ) ) { Say( caller, "Not now." ); return; }

		_sinceClass[caller.SteamId.ValueUnsigned] = 0;
		if ( SetClass( me, want.Id ) ) Say( caller, $"You redeploy as a {want.Name}." );
	}

	static bool CanRedeploy( SwrpPlayer p ) => !( p.Jailed || p.Frozen || p.InVehicle || p.InPod || p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } );

	/// <summary> Host: save the trooper's class and redeploy them in it; false when they can't redeploy now (it applies on their next deploy). </summary>
	static bool SetClass( SwrpPlayer target, string classId )
	{
		var record = CharacterStore.Find( target.OwnerSteamId, target.CharacterId );
		if ( record is null ) return false;
		record.ClassId = classId;
		CharacterStore.Save( target.OwnerSteamId );
		if ( !CanRedeploy( target ) || target.Network.Owner is not { } owner ) return false;
		SwrpGameManager.Current?.SpawnCharacter( owner, record, force: true );
		return true;
	}

	// /train

	static bool IsTrainer( Connection caller, SwrpPlayer me )
		=> IsStaff( caller ) || me.IsValid() && me.CharacterId is not null && ( me.RankIndex >= SwrpRoleplay.LieutenantRank || HasQual( me.OwnerSteamId, me.FactionId, "trainer" ) );

	static void TrainCommand( Connection caller, SwrpPlayer me, string args )
	{
		var (sub, rest) = Split( args );
		sub = sub.ToLowerInvariant();
		var trainer = IsTrainer( caller, me );

		if ( sub is "" or "status" )
		{
			var who = rest.Length > 0 ? FindTrooper( caller, rest ) : me;
			if ( !who.IsValid() ) { Say( caller, "Deploy first." ); return; }
			ShowChecklist( caller, who );
			if ( sub == "" && trainer ) Say( caller, "Trainer: /train list · /train check <cadet> <1-6|all> · /train uncheck <cadet> <#> · /train note <cadet> <text> · /train pass <cadet> · /train fail <cadet> [reason]" );
			return;
		}

		if ( !trainer ) { Say( caller, "Cadet training is run by Training Officers (the trainer qualification), Lieutenants and up, and staff." ); return; }

		if ( sub == "list" )
		{
			var cadets = SwrpPlayers.Deployed.Where( p => p.RankIndex == 0 && p.FactionId != "jedi" ).ToList();
			if ( cadets.Count == 0 ) { Say( caller, "No cadets aboard." ); return; }
			foreach ( var c in cadets )
			{
				var done = Data.Cadets.TryGetValue( Key( c ), out var cd ) ? cd.Done.Count : 0;
				Say( caller, $"{c.FullTitle} ({c.Faction.ShortName}, {SwrpShip.DeckName( c.WorldPosition )}): {done}/{Checklist.Length}" );
			}
			return;
		}

		var (name, tail) = Split( rest );
		var target = FindTrooper( caller, name );
		if ( target is null ) return;
		if ( target == me && !IsStaff( caller ) ) { Say( caller, "You can't train yourself." ); return; }
		if ( !Data.Cadets.TryGetValue( Key( target ), out var cadet ) ) Data.Cadets[Key( target )] = cadet = new Cadet();
		var by = me.IsValid() && me.CharacterId is not null ? $"{me.Rank.Abbreviation} {me.CharacterName}" : caller.DisplayName;

		switch ( sub )
		{
			case "check":
			case "uncheck":
			{
				var items = tail.Trim() == "all" ? Enumerable.Range( 0, Checklist.Length ).ToList()
					: tail.Split( new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries ).Select( t => int.TryParse( t, out var n ) ? n - 1 : -1 ).Where( n => n >= 0 && n < Checklist.Length ).ToList();
				if ( items.Count == 0 ) { Say( caller, $"Usage: /train {sub} <cadet> <1-{Checklist.Length}|all>" ); return; }
				foreach ( var i in items )
				{
					if ( sub == "check" && !cadet.Done.Contains( i ) ) cadet.Done.Add( i );
					if ( sub == "uncheck" ) cadet.Done.Remove( i );
				}
				Save();
				ShowChecklist( caller, target );
				if ( sub == "check" ) Tell( target, $"{by} signed off: {string.Join( "; ", items.Select( i => Checklist[i] ) )}." );
				return;
			}
			case "note":
				if ( tail.Length == 0 ) { Say( caller, "Usage: /train note <cadet> <text>" ); return; }
				cadet.Notes.Add( $"{DateTime.UtcNow:d MMM} {by}: {SwrpChat.Clean( tail, 160 )}" );
				if ( cadet.Notes.Count > 12 ) cadet.Notes.RemoveAt( 0 );
				Save();
				Say( caller, "Noted." );
				return;
			case "fail":
				Tell( target, $"{by}: not passed this time{( tail.Length > 0 ? $" ({SwrpChat.Clean( tail, 120 )})" : "" )}. Your checklist is kept; try again with any Training Officer." );
				cadet.Notes.Add( $"{DateTime.UtcNow:d MMM} {by}: not passed {SwrpChat.Clean( tail, 120 )}" );
				Save();
				Say( caller, $"{target.FullTitle} told." );
				return;
			case "pass":
			{
				if ( target.RankIndex != 0 ) { Say( caller, $"{target.FullTitle} isn't a cadet." ); return; }
				var missing = Enumerable.Range( 0, Checklist.Length ).Where( i => !cadet.Done.Contains( i ) ).ToList();
				if ( missing.Count > 0 && tail.Trim() != "force" )
				{
					Say( caller, $"Not done yet: {string.Join( ", ", missing.Select( i => i + 1 ) )}. Tick them with /train check, or /train pass <cadet> force." );
					return;
				}
				var record = CharacterStore.Find( target.OwnerSteamId, target.CharacterId );
				if ( record is null ) return;
				SwrpRanks.Record( by, caller.SteamId.ValueUnsigned, record, 0, 1, "passed cadet training of" );
				record.RankIndex = 1;
				CharacterStore.Save( target.OwnerSteamId );
				target.RefreshFromStore();
				Data.Cadets.Remove( Key( target ) );
				StatsOf( target.FactionId ).CadetsPassed++;
				Save();
				SwrpStaffTools.RecordText( caller, "train pass", target.FullTitle );
				var faction = SwrpPlayers.Deployed.Where( p => p.FactionId == target.FactionId ).Select( p => p.Network.Owner );
				SwrpChat.SendTo( faction, ChatKind.Radio, $"[{target.Faction.ShortName}] {target.CharacterName} passed cadet training and is now {record.Rank.Name}. Welcome to the unit." );
				Tell( target, $"Training passed. You are now {record.Rank.Name}." );
				return;
			}
		}
		Say( caller, "Usage: /train [status|list|check|uncheck|note|pass|fail]" );
	}

	static void ShowChecklist( Connection caller, SwrpPlayer who )
	{
		if ( who.RankIndex != 0 && !Data.Cadets.ContainsKey( Key( who ) ) ) { Say( caller, $"{who.FullTitle} has passed cadet training." ); return; }
		var done = Data.Cadets.TryGetValue( Key( who ), out var c ) ? c.Done : new List<int>();
		Say( caller, $"Cadet training, {who.FullTitle}: {done.Count}/{Checklist.Length}" );
		for ( var i = 0; i < Checklist.Length; i++ )
			Say( caller, $"{( done.Contains( i ) ? "[x]" : "[ ]" )} {i + 1}. {Checklist[i]}" );
	}

	// medals

	static readonly List<DateTime> _recentMedals = new();

	static void Commend( Connection caller, SwrpPlayer me, string args )
	{
		var (name, rest) = Split( args );
		var (medalText, citation) = Split( rest );
		var medal = Medals.FirstOrDefault( m => m.Id.Equals( medalText, StringComparison.OrdinalIgnoreCase ) ).Id;
		if ( string.IsNullOrEmpty( name ) || medal is null )
		{
			Say( caller, $"Usage: /commend <trooper> <medal> [citation]. Medals: {string.Join( ", ", Medals.Select( m => $"{m.Id} ({m.Name})" ) )}." );
			return;
		}
		var target = FindTrooper( caller, name );
		if ( target is null ) return;
		if ( !OfficerOver( caller, me, target, SwrpRoleplay.CaptainRank ) ) { Say( caller, $"Medals are awarded by Captains and up of the {target.Faction.Name}, its Commander, XO or Jedi General, and staff." ); return; }
		if ( !IsStaff( caller ) )
		{
			_recentMedals.RemoveAll( t => DateTime.UtcNow - t > TimeSpan.FromMinutes( 10 ) );
			if ( _recentMedals.Count >= 10 ) { Say( caller, "That's a lot of medals at once. Give it a few minutes." ); return; }
			_recentMedals.Add( DateTime.UtcNow );
		}

		var by = me.IsValid() && me.CharacterId is not null ? $"{me.Rank.Abbreviation} {me.CharacterName}" : caller.DisplayName;
		Data.Medals.Add( new MedalEntry
		{
			SteamId = target.OwnerSteamId,
			CharacterId = target.CharacterId,
			Trooper = target.FullTitle,
			Battalion = target.FactionId,
			Medal = medal,
			Citation = SwrpChat.Clean( citation, 200 ),
			By = by,
			When = DateTime.UtcNow,
		} );
		Save();
		SwrpStaffTools.RecordText( caller, "commend", $"{target.FullTitle}: {MedalName( medal )}" );
		var cite = string.IsNullOrWhiteSpace( citation ) ? "" : $" \"{SwrpChat.Clean( citation, 200 )}\"";
		SwrpChat.SendTo( Connection.All, ChatKind.Broadcast, $"[COMMENDATION] {by} awarded {target.FullTitle} the {MedalName( medal )}.{cite}" );
	}

	static void ShowMedals( Connection caller, SwrpPlayer me, string args )
	{
		var who = args.Trim().Length > 0 ? FindTrooper( caller, args ) : me;
		if ( !who.IsValid() || who.CharacterId is null ) { if ( args.Trim().Length == 0 ) Say( caller, "Deploy first, or /medals <trooper>." ); return; }
		var list = MedalsOf( who.OwnerSteamId, who.CharacterId ).ToList();
		if ( list.Count == 0 ) { Say( caller, $"{who.FullTitle} has no decorations yet." ); return; }
		Say( caller, $"{who.FullTitle}: {list.Count} decoration{( list.Count == 1 ? "" : "s" )}" );
		foreach ( var m in list.TakeLast( 10 ) )
			Say( caller, $"{MedalName( m.Medal )}, {m.When:d MMM yyyy}, by {m.By}{( string.IsNullOrWhiteSpace( m.Citation ) ? "" : $": {m.Citation}" )}" );
	}

	// leave

	static void LoaCommand( Connection caller, SwrpPlayer me, string args )
	{
		var (sub, rest) = Split( args );
		var id = caller.SteamId.ValueUnsigned;
		if ( sub is "end" or "off" or "back" )
		{
			if ( Data.Leave.Remove( id ) ) { Save(); Say( caller, "Welcome back: your leave is over." ); }
			else Say( caller, "You're not on leave." );
			return;
		}
		if ( sub == "list" )
		{
			var battalion = rest.Length > 0 ? rest.Trim() : me?.FactionId;
			var names = Data.Leave.Where( kv => kv.Value.Until > DateTime.UtcNow )
				.Select( kv => (kv.Key, kv.Value, Bat: CharacterStore.GetCharacters( kv.Key ).Select( c => c.FactionId ).ToList()) )
				.Where( x => battalion is null || x.Bat.Contains( battalion ) )
				.Select( x => $"{SwrpBattalions.NameOf( x.Key )} until {x.Value.Until:d MMM}{( string.IsNullOrWhiteSpace( x.Value.Reason ) ? "" : $" ({x.Value.Reason})" )}" ).ToList();
			Say( caller, names.Count == 0 ? "Nobody is on leave." : "On leave: " + string.Join( "; ", names ) );
			return;
		}
		if ( !int.TryParse( sub, out var days ) || days < 1 || days > 90 )
		{
			var l = LeaveOf( id );
			Say( caller, l is null ? "Usage: /loa <days 1-90> [reason] · /loa end · /loa list [battalion]" : $"You're on leave until {l.Until:d MMM}. /loa end when you're back." );
			return;
		}
		Data.Leave[id] = new LeaveEntry { Since = DateTime.UtcNow, Until = DateTime.UtcNow.AddDays( days ), Reason = SwrpChat.Clean( rest, 80 ) };
		Save();
		Say( caller, $"Leave of absence recorded until {DateTime.UtcNow.AddDays( days ):d MMM}. It shows on your battalion's roster; /loa end when you're back." );
	}

	// stats

	static void StatsCommand( Connection caller, SwrpPlayer me, string args )
	{
		var a = args.Trim();
		if ( a is "" or "me" )
		{
			if ( !me.IsValid() || me.CharacterId is null ) { Say( caller, "Deploy first, or /stats <battalion|all>." ); return; }
			var s = StatsOf( Key( me ) );
			Say( caller, $"{me.FullTitle}: {s.Revives} revives, {s.NpcKills} enemy kills, {s.SimRuns} simulation runs (best {s.BestSim}), {MedalsOf( me.OwnerSteamId, me.CharacterId ).Count()} decorations." );
			return;
		}
		var units = a == "all" ? FactionRegistry.All.ToList() : FactionRegistry.All.Where( f => f.Id.Equals( a, StringComparison.OrdinalIgnoreCase ) || f.ShortName.Equals( a, StringComparison.OrdinalIgnoreCase ) ).ToList();
		if ( units.Count == 0 ) { Say( caller, $"Unknown battalion '{a}'. /stats [me|all|<battalion>]" ); return; }
		foreach ( var f in units )
		{
			var s = StatsOf( f.Id );
			var medals = Data.Medals.Count( m => m.Battalion == f.Id );
			Say( caller, $"{f.Name}: {s.Revives} revives, {s.NpcKills} enemy kills, {s.CadetsPassed} cadets passed, {s.SimRuns} sim runs (best {s.BestSim}), {medals} decorations." );
		}
	}

	// trooper of the week

	static void TotwCommand( Connection caller, SwrpPlayer me, string args )
	{
		var (name, reason) = Split( args );
		if ( name.Length == 0 )
		{
			var t = Data.TrooperOfTheWeek;
			Say( caller, t is null ? "No Trooper of the Week yet." : $"Trooper of the Week: {t.Trooper} ({t.Battalion}), named by {t.By} on {t.When:d MMM}{( string.IsNullOrWhiteSpace( t.Reason ) ? "" : $": {t.Reason}" )}" );
			return;
		}
		var post = me.IsValid() ? SwrpBattalions.PostOf( me.FactionId, caller.SteamId.ValueUnsigned ) : SwrpBattalions.Post.None;
		if ( !IsStaff( caller ) && post is not ( SwrpBattalions.Post.Commander or SwrpBattalions.Post.Executive ) && ( !me.IsValid() || me.RankIndex < FactionRegistry.CloneRanks.Count - 2 ) )
		{
			Say( caller, "Battalion Commanders and XOs, Majors and up, and staff name the Trooper of the Week." );
			return;
		}
		var target = FindTrooper( caller, name );
		if ( target is null ) return;
		var by = me.IsValid() && me.CharacterId is not null ? $"{me.Rank.Abbreviation} {me.CharacterName}" : caller.DisplayName;
		Data.TrooperOfTheWeek = new Totw { Trooper = target.FullTitle, Battalion = target.Faction.ShortName, Reason = SwrpChat.Clean( reason, 160 ), By = by, When = DateTime.UtcNow };
		Save();
		SwrpGuide.BroadcastBoard();
		SwrpChat.SendTo( Connection.All, ChatKind.Broadcast, $"[TROOPER OF THE WEEK] {target.FullTitle} of the {target.Faction.Name}, named by {by}.{( string.IsNullOrWhiteSpace( reason ) ? "" : $" {SwrpChat.Clean( reason, 160 )}" )}" );
	}
}
