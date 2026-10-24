using System;

namespace Swrp;

/// <summary> A battalion's leadership posts and the battalion action history (swrp/battalions.json on the host). </summary>
public sealed class BattalionFile
{
	public Dictionary<string, BattalionPosts> Posts { get; set; } = new();
	public List<BattalionAction> Actions { get; set; } = new();
	public List<BattalionTransfer> Transfers { get; set; } = new();

	/// <summary> SteamID64 to the name they last played under and when they were last online (UTC). </summary>
	public Dictionary<ulong, SeenPlayer> Seen { get; set; } = new();
	public int NextId { get; set; } = 1;
}

public sealed class BattalionPosts
{
	/// <summary> The battalion leader. </summary>
	public ulong Commander { get; set; }

	/// <summary> The second in command (the "co-leader"). </summary>
	public ulong Executive { get; set; }

	/// <summary> The Jedi attached to the battalion; any unit. </summary>
	public ulong JediGeneral { get; set; }
}

public sealed class SeenPlayer
{
	public string Name { get; set; }
	public DateTime LastSeen { get; set; }
}

public sealed class BattalionAction
{
	public int Id { get; set; }
	public DateTime When { get; set; }
	public string By { get; set; }
	public ulong BySteamId { get; set; }
	public bool ByStaff { get; set; }
	public string Battalion { get; set; }

	/// <summary> whitelist, remove, promote, demote, role, transfer, post, reverse. </summary>
	public string Kind { get; set; }
	public ulong TargetSteamId { get; set; }
	public string TargetCharacterId { get; set; }
	public string TargetName { get; set; }

	/// <summary> What it was and what it became (rank index, role, battalion id, post holder), for the log and for reversing. </summary>
	public string From { get; set; }
	public string To { get; set; }
	public bool Reversed { get; set; }

	public string Describe() => Kind switch
	{
		"whitelist" => $"whitelisted {TargetName}",
		"remove" => $"removed {TargetName} (to {FactionRegistry.Get( To )?.Name ?? To})",
		"promote" or "demote" => $"{Kind}d {TargetName} {RankName( From )} → {RankName( To )}",
		"role" => $"set {TargetName}'s role to {( string.IsNullOrEmpty( To ) ? "none" : To )}",
		"transfer" => $"transferred {TargetName} {FactionRegistry.Get( From )?.Name ?? From} → {FactionRegistry.Get( To )?.Name ?? To}",
		"post" => $"made {TargetName} {From}",
		"reverse" => $"reversed action #{To}",
		_ => $"{Kind} {TargetName}",
	};

	static string RankName( string index ) => int.TryParse( index, out var i ) ? FactionRegistry.CloneRanks[Math.Clamp( i, 0, 10 )].Name : index;
}

public sealed class BattalionTransfer
{
	public int Id { get; set; }
	public DateTime When { get; set; }
	public ulong SteamId { get; set; }
	public string CharacterId { get; set; }
	public string Name { get; set; }
	public string From { get; set; }
	public string To { get; set; }
	public string RequestedBy { get; set; }
	public ulong RequestedBySteamId { get; set; }

	/// <summary> Which side has agreed: the battalion the request came from counts as approved. </summary>
	public bool FromApproved { get; set; }
	public bool ToApproved { get; set; }
}

/// <summary>
/// Battalion officer tools. Officers (2nd Lieutenant and up by default, swrp_battalion_officer_rank) and the leadership posts
/// (Commander, Executive Officer, Jedi General; set by staff) run their own battalion: whitelist players in, remove them,
/// promote up to one rank below their own, demote, set roles, and arrange transfers, which need both battalions' command
/// (Commander or XO) or staff. Every action is rate limited, written to the rank log, the staff log and the battalion history,
/// and staff can override anything and reverse any action. Host side; the menu is <see cref="BattalionMenu"/>.
/// </summary>
public static class SwrpBattalions
{
	public const string FilePath = "swrp/battalions.json";

	[ConVar( "swrp_battalion_officer_rank", Help = "Lowest rank index with battalion officer powers (host, default 6 = 2nd Lieutenant)", Saved = true )]
	public static int OfficerRank { get; set; } = SwrpRoleplay.LieutenantRank;

	public enum Post { None, Jedi, Executive, Commander }

	static BattalionFile _data;
	static BattalionFile Data => _data ??= FileSystem.Data.ReadJson<BattalionFile>( FilePath, null ) ?? new();

	public static void Reload() => _data = null;

	static void Save()
	{
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( FilePath, Data );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not save {FilePath}: {e.Message}" );
		}
	}

	public static BattalionPosts PostsOf( string battalion ) => Data.Posts.TryGetValue( battalion, out var p ) ? p : new BattalionPosts();

	public static Post PostOf( string battalion, ulong steamId )
	{
		if ( steamId == 0 ) return Post.None;
		var p = PostsOf( battalion );
		return p.Commander == steamId ? Post.Commander : p.Executive == steamId ? Post.Executive : p.JediGeneral == steamId ? Post.Jedi : Post.None;
	}

	public static string NameOf( ulong steamId )
	{
		if ( steamId == 0 ) return "none";
		if ( Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == steamId ) is { } c ) return c.DisplayName;
		return Data.Seen.TryGetValue( steamId, out var s ) ? s.Name : steamId.ToString();
	}

	// ----- last seen -----

	static RealTimeSince _sinceSeen = 100;

	/// <summary> Host, every frame from the game manager: record who is online, once a minute. </summary>
	public static void TickSeen()
	{
		if ( _sinceSeen < 60f ) return;
		_sinceSeen = 0;
		foreach ( var c in Connection.All ) MarkSeen( c );
		Save();
	}

	public static void MarkSeen( Connection c )
	{
		if ( c is null ) return;
		Data.Seen[c.SteamId.ValueUnsigned] = new SeenPlayer { Name = c.DisplayName, LastSeen = DateTime.UtcNow };
	}

	public static void OnLeft( Connection c )
	{
		MarkSeen( c );
		Save();
	}

	// ----- who may do what -----

	/// <summary> The authority someone has in a battalion, as a rank index: their rank, raised by a post. -1 = none. </summary>
	public static int Authority( Connection caller, SwrpPlayer me, string battalion )
	{
		var post = PostOf( battalion, caller?.SteamId.ValueUnsigned ?? 0 );
		var rank = me.IsValid() && me.CharacterId is not null && me.FactionId == battalion ? me.RankIndex : -1;
		var ranks = FactionRegistry.CloneRanks.Count;
		var floor = post switch
		{
			Post.Commander or Post.Jedi => ranks - 1,          // as a Commander: promote up to Major
			Post.Executive => ranks - 2,                        // as a Major: up to Captain
			_ => -1,
		};
		var auth = Math.Max( floor, rank );
		return post != Post.None || rank >= OfficerRank ? auth : -1;
	}

	static bool IsStaff( Connection c ) => c is not null && Whitelist.IsStaff( c );

	/// <summary> Can this person act on that member at all (staff, or authority above the member's rank and post)? </summary>
	static bool Outranks( Connection caller, int auth, string battalion, ulong targetSteam, int targetRank )
	{
		if ( IsStaff( caller ) ) return true;
		if ( auth < 0 || targetSteam == caller.SteamId.ValueUnsigned ) return false;
		var mine = PostOf( battalion, caller.SteamId.ValueUnsigned );
		var theirs = PostOf( battalion, targetSteam );
		if ( theirs >= mine && theirs != Post.None ) return false;     // nobody below the Commander touches the Commander
		return targetRank < auth || mine > theirs && mine != Post.None;
	}

	// ----- rate limits (staff are never limited) -----

	static readonly Dictionary<string, (int Count, TimeSpan Window)> Limits = new()
	{
		["whitelist"] = (8, TimeSpan.FromMinutes( 10 )),
		["remove"] = (5, TimeSpan.FromMinutes( 10 )),
		["rank"] = (12, TimeSpan.FromMinutes( 10 )),
		["role"] = (20, TimeSpan.FromMinutes( 10 )),
		["transfer"] = (4, TimeSpan.FromMinutes( 30 )),
	};
	static readonly Dictionary<(ulong, string), List<DateTime>> _recent = new();

	static bool CheckRate( Connection caller, string kind )
	{
		if ( IsStaff( caller ) || caller.IsHost || !Limits.TryGetValue( kind, out var limit ) ) return true;
		var key = (caller.SteamId.ValueUnsigned, kind);
		if ( !_recent.TryGetValue( key, out var times ) ) _recent[key] = times = new();
		var now = DateTime.UtcNow;
		times.RemoveAll( t => now - t > limit.Window );
		if ( times.Count >= limit.Count )
		{
			SwrpChat.SystemTo( caller, $"Slow down: at most {limit.Count} {kind} actions per {limit.Window.TotalMinutes:0} minutes." );
			SwrpChat.StaffLog( $"{caller.DisplayName} hit the battalion {kind} rate limit" );
			return false;
		}
		times.Add( now );
		return true;
	}

	// ----- logging -----

	static string Actor( Connection caller, SwrpPlayer me ) => me.IsValid() && me.CharacterId is not null ? $"{me.ShortName} ({caller?.DisplayName})" : caller?.DisplayName ?? "console";

	static BattalionAction Record( Connection caller, SwrpPlayer me, string battalion, string kind, ulong targetSteam, string targetCharacter, string targetName, string from, string to )
	{
		var a = new BattalionAction
		{
			Id = Data.NextId++,
			When = DateTime.UtcNow,
			By = Actor( caller, me ),
			BySteamId = caller?.SteamId.ValueUnsigned ?? 0,
			ByStaff = IsStaff( caller ),
			Battalion = battalion,
			Kind = kind,
			TargetSteamId = targetSteam,
			TargetCharacterId = targetCharacter,
			TargetName = targetName,
			From = from,
			To = to,
		};
		Data.Actions.Add( a );
		if ( Data.Actions.Count > 2000 ) Data.Actions.RemoveRange( 0, Data.Actions.Count - 2000 );
		Save();

		var bat = FactionRegistry.Get( battalion )?.Name ?? battalion;
		var line = $"[{bat}] #{a.Id} {a.By} {a.Describe()}";
		SwrpRanks.RecordNote( a.By, a.BySteamId, targetName, targetSteam, $"#{a.Id} {a.Describe()}" );
		SwrpStaffTools.Record( caller, $"battalion {kind}", $"{targetSteam} [{bat}] #{a.Id} {a.Describe()}" );
		SwrpChat.StaffLog( line );
		return a;
	}

	// ----- roster -----

	public sealed class Member
	{
		public ulong SteamId { get; set; }
		public string CharacterId { get; set; }
		public string Player { get; set; }
		public string Name { get; set; }
		public string Designation { get; set; }
		public string Rank { get; set; }
		public int RankIndex { get; set; }
		public string Role { get; set; }
		public string Post { get; set; }
		public bool Online { get; set; }
		public string LastSeen { get; set; }
		public bool CanRank { get; set; }
		public bool CanRole { get; set; }
		public bool CanRemove { get; set; }
		public bool CanTransfer { get; set; }
	}

	/// <summary> Every steam id that has a character file. </summary>
	static IEnumerable<ulong> AllPlayers()
	{
		var ids = new HashSet<ulong>();
		try
		{
			foreach ( var f in FileSystem.Data.FindFile( "swrp/characters", "*.json" ) )
			{
				var name = System.IO.Path.GetFileNameWithoutExtension( f );
				if ( ulong.TryParse( name, out var id ) ) ids.Add( id );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not list characters: {e.Message}" );
		}
		foreach ( var (id, list) in Whitelist.AllFactions() ) ids.Add( id );
		return ids;
	}

	static string Ago( DateTime when )
	{
		var d = DateTime.UtcNow - when;
		if ( when == default ) return "never";
		if ( d.TotalMinutes < 2 ) return "just now";
		if ( d.TotalHours < 1 ) return $"{(int)d.TotalMinutes} min ago";
		if ( d.TotalDays < 1 ) return $"{(int)d.TotalHours} h ago";
		return $"{(int)d.TotalDays} d ago";
	}

	public static List<Member> Roster( string battalion, Connection viewer = null, int auth = -1 )
	{
		var faction = FactionRegistry.Get( battalion );
		var online = Connection.All.Select( c => c.SteamId.ValueUnsigned ).ToHashSet();
		var command = viewer is not null && ( IsStaff( viewer ) || PostOf( battalion, viewer.SteamId.ValueUnsigned ) is Post.Commander or Post.Executive );
		var list = new List<Member>();
		foreach ( var id in AllPlayers() )
		{
			var chars = CharacterStore.GetCharacters( id ).Where( c => c.FactionId == battalion ).ToList();
			Data.Seen.TryGetValue( id, out var seen );
			var player = NameOf( id );
			var post = PostOf( battalion, id );
			foreach ( var c in chars )
			{
				var lastSeen = online.Contains( id ) ? "online"
					: SwrpCareer.LeaveOf( id ) is { } leave ? $"LOA to {leave.Until:d MMM}"      // /loa: on leave, not missing
					: Ago( new[] { c.LastPlayed, seen?.LastSeen ?? default }.Max() );
				var acts = viewer is not null && Outranks( viewer, auth, battalion, id, c.RankIndex );
				list.Add( new Member
				{
					SteamId = id, CharacterId = c.Id, Player = player, Name = c.Name, Designation = c.Faction.DesignationFor( c.Designation ),
					Rank = c.Rank.Name, RankIndex = c.RankIndex, Role = c.BattalionRole ?? "", Post = post == Post.None ? "" : PostLabel( post ),
					Online = online.Contains( id ), LastSeen = lastSeen,
					CanRank = acts, CanRole = acts, CanRemove = acts, CanTransfer = command && acts || viewer is not null && IsStaff( viewer ),
				} );
			}
			// whitelisted for this battalion but no character in it yet
			if ( chars.Count == 0 && Whitelist.IsWhitelistedFor( id, battalion ) )
			{
				list.Add( new Member
				{
					SteamId = id, Player = player, Name = "(no character yet)", Designation = "", Rank = "-", RankIndex = -1, Post = post == Post.None ? "" : PostLabel( post ),
					Online = online.Contains( id ), LastSeen = online.Contains( id ) ? "online" : Ago( seen?.LastSeen ?? default ),
					CanRemove = viewer is not null && ( IsStaff( viewer ) || auth >= 0 && PostOf( battalion, id ) == Post.None ),
				} );
			}
		}
		return list.OrderByDescending( m => m.Post == "Commander" ).ThenByDescending( m => m.Post == "Executive Officer" ).ThenByDescending( m => m.RankIndex ).ThenBy( m => m.Name ).ToList();
	}

	public static string PostLabel( Post p ) => p switch { Post.Commander => "Commander", Post.Executive => "Executive Officer", Post.Jedi => "Jedi General", _ => "" };

	// ----- commands (host) -----

	public const string Usage = "/battalion or L opens the battalion menu · /battalion add <player> · remove <member> · promote <member> · demote <member> · role <member> <role|none> · transfer <member> <battalion> · approve|deny <request #> · log [n]; staff: post <battalion> <commander|xo|jedi> <player|none>, reverse <action #>, view <battalion>";

	/// <summary> Host: everything after /battalion. </summary>
	public static void Command( Connection caller, SwrpPlayer me, string args )
	{
		var parts = ( args ?? "" ).Trim().Split( ' ', 2, StringSplitOptions.RemoveEmptyEntries );
		var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
		var rest = parts.Length > 1 ? parts[1].Trim() : "";

		// which battalion: your character's, or (staff) the one you are viewing
		var battalion = ViewedBattalion( caller, me );

		switch ( sub )
		{
			case "":
			case "menu":
				if ( battalion is null ) { SwrpChat.SystemTo( caller, "Deploy a character in a battalion first (staff: /battalion view <battalion>)." ); return; }
				using ( Rpc.FilterInclude( caller ) ) OpenMenu();
				SendRoster( caller, me );
				return;
			case "view":
				if ( !IsStaff( caller ) ) { SwrpChat.SystemTo( caller, "Only staff can view other battalions." ); return; }
				if ( FactionRegistry.Get( rest.ToLowerInvariant() ) is not { } f ) { SwrpChat.SystemTo( caller, $"Battalions: {string.Join( ", ", FactionRegistry.All.Select( x => x.Id ) )}" ); return; }
				_staffView[caller.SteamId.ValueUnsigned] = f.Id;
				using ( Rpc.FilterInclude( caller ) ) OpenMenu();
				SendRoster( caller, me );
				return;
			case "post":
				PostCommand( caller, me, rest );
				return;
			case "reverse":
			case "undo":
				Reverse( caller, me, rest );
				return;
			case "log":
				ShowLog( caller, battalion, rest );
				return;
			case "approve":
			case "deny":
				Decide( caller, me, rest, sub == "approve" );
				return;
		}

		if ( battalion is null ) { SwrpChat.SystemTo( caller, "Deploy a character in a battalion first." ); return; }
		var auth = Authority( caller, me, battalion );
		if ( auth < 0 && !IsStaff( caller ) )
		{
			SwrpChat.SystemTo( caller, $"Battalion officer tools are for {FactionRegistry.CloneRanks[Math.Clamp( OfficerRank, 0, 10 )].Name}s and up, and the battalion's Commander, Executive Officer and Jedi General." );
			return;
		}

		switch ( sub )
		{
			case "add":
			case "whitelist":
				Add( caller, me, battalion, rest );
				break;
			case "remove":
			case "kick":
				Remove( caller, me, battalion, auth, rest );
				break;
			case "promote":
			case "demote":
				Step( caller, me, battalion, auth, rest, sub == "promote" ? 1 : -1 );
				break;
			case "role":
				Role( caller, me, battalion, auth, rest );
				break;
			case "transfer":
				Transfer( caller, me, battalion, auth, rest );
				break;
			default:
				SwrpChat.SystemTo( caller, Usage );
				return;
		}
		SendRoster( caller, me );
	}

	static readonly Dictionary<ulong, string> _staffView = new();

	static string ViewedBattalion( Connection caller, SwrpPlayer me )
	{
		if ( IsStaff( caller ) && _staffView.TryGetValue( caller.SteamId.ValueUnsigned, out var v ) ) return v;
		return me.IsValid() && me.CharacterId is not null ? me.FactionId : null;
	}

	/// <summary> A roster member by character id, or a character name / designation / player name within the battalion. </summary>
	static (ulong SteamId, CharacterRecord Character) Find( string battalion, string who )
	{
		who = ( who ?? "" ).Trim();
		if ( who.Length == 0 ) return (0, null);
		foreach ( var id in AllPlayers() )
			foreach ( var c in CharacterStore.GetCharacters( id ) )
				if ( c.FactionId == battalion && c.Id == who ) return (id, c);
		foreach ( var id in AllPlayers() )
			foreach ( var c in CharacterStore.GetCharacters( id ) )
				if ( c.FactionId == battalion && ( c.Name.Contains( who, StringComparison.OrdinalIgnoreCase ) || c.Designation == who.Split( '-' ).Last() || NameOf( id ).Contains( who, StringComparison.OrdinalIgnoreCase ) ) )
					return (id, c);
		return (0, null);
	}

	static void Refresh( ulong steamId, string characterId, string notice, bool bad )
	{
		var p = Game.ActiveScene?.GetAllComponents<SwrpPlayer>().FirstOrDefault( x => x.OwnerSteamId == steamId );
		if ( p.IsValid() && p.CharacterId == characterId ) p.RefreshFromStore();
		if ( Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == steamId ) is { } conn )
		{
			if ( notice is not null ) SwrpNet.NotifyConnection( conn, notice, bad );
			SwrpNet.SendMenuState( conn );
		}
	}

	static void Add( Connection caller, SwrpPlayer me, string battalion, string who )
	{
		var f = FactionRegistry.Get( battalion );
		if ( !SwrpNet.TryResolveSteamId( who, out var steamId, out var label ) ) { SwrpChat.SystemTo( caller, $"No online player or SteamID matches '{who}'." ); return; }
		if ( Whitelist.IsWhitelistedFor( steamId, battalion ) ) { SwrpChat.SystemTo( caller, $"{label} is already whitelisted for the {f.Name}." ); return; }
		if ( !CheckRate( caller, "whitelist" ) ) return;
		Whitelist.Grant( steamId, battalion, Actor( caller, me ), label );
		Record( caller, me, battalion, "whitelist", steamId, null, label, null, battalion );
		SwrpChat.SystemTo( caller, $"{label} can now enlist in the {f.Name}." );
		Refresh( steamId, null, $"You've been whitelisted for the {f.Name} by {Actor( caller, me )}.", false );
	}

	static void Remove( Connection caller, SwrpPlayer me, string battalion, int auth, string who )
	{
		var f = FactionRegistry.Get( battalion );
		var (steamId, c) = Find( battalion, who );
		if ( c is null )
		{
			// a whitelisted player with no character here yet
			if ( SwrpNet.TryResolveSteamId( who, out var sid, out var label ) && Whitelist.IsWhitelistedFor( sid, battalion ) )
			{
				if ( !IsStaff( caller ) && PostOf( battalion, sid ) != Post.None ) { SwrpChat.SystemTo( caller, "Only staff can remove a battalion's leadership." ); return; }
				if ( !CheckRate( caller, "remove" ) ) return;
				Whitelist.Revoke( sid, battalion );
				Record( caller, me, battalion, "remove", sid, null, label, battalion, "" );
				SwrpChat.SystemTo( caller, $"{label} is no longer whitelisted for the {f.Name}." );
				return;
			}
			SwrpChat.SystemTo( caller, $"No member of the {f.Name} matches '{who}'." );
			return;
		}
		if ( !Outranks( caller, auth, battalion, steamId, c.RankIndex ) ) { SwrpChat.SystemTo( caller, $"You can't remove {c.Name}: they outrank you or hold a post." ); return; }
		if ( !CheckRate( caller, "remove" ) ) return;

		// back to the unassigned shinies; the record keeps everything else, so a reversal restores it exactly
		var to = FactionRegistry.CloneTrooper;
		var from = c.FactionId;
		var role = c.BattalionRole;
		c.FactionId = to;
		c.RankIndex = Math.Clamp( c.RankIndex, 0, c.Faction.Ranks.Count - 1 );
		c.BattalionRole = null;
		CharacterStore.Save( steamId );
		if ( !CharacterStore.GetCharacters( steamId ).Any( x => x.FactionId == battalion ) ) Whitelist.Revoke( steamId, battalion );
		var a = Record( caller, me, battalion, "remove", steamId, c.Id, c.FullTitle, from, to );
		a.From = $"{from}|{role}";
		Save();
		SwrpChat.SystemTo( caller, $"{c.Name} has been removed from the {f.Name}." );
		Refresh( steamId, c.Id, $"You've been removed from the {f.Name} by {Actor( caller, me )}.", true );
	}

	static void Step( Connection caller, SwrpPlayer me, string battalion, int auth, string who, int step )
	{
		var (steamId, c) = Find( battalion, who );
		if ( c is null ) { SwrpChat.SystemTo( caller, $"No member matches '{who}'." ); return; }
		if ( !Outranks( caller, auth, battalion, steamId, c.RankIndex ) ) { SwrpChat.SystemTo( caller, $"You can't change {c.Name}'s rank: they outrank you or hold a post." ); return; }
		var ranks = c.Faction.Ranks;
		var from = c.RankIndex;
		var to = Math.Clamp( from + step, 0, ranks.Count - 1 );
		if ( to == from ) { SwrpChat.SystemTo( caller, $"{c.Name} is already {ranks[from].Name}." ); return; }
		if ( !IsStaff( caller ) && to >= auth ) { SwrpChat.SystemTo( caller, $"You can promote up to {ranks[Math.Max( 0, auth - 1 )].Name} (one rank below your own authority)." ); return; }
		if ( !CheckRate( caller, "rank" ) ) return;

		c.RankIndex = to;
		CharacterStore.Save( steamId );
		SwrpRanks.Record( Actor( caller, me ), caller?.SteamId.ValueUnsigned ?? 0, c, from, to, step > 0 ? "promoted" : "demoted" );
		Record( caller, me, battalion, step > 0 ? "promote" : "demote", steamId, c.Id, c.FullTitle, from.ToString(), to.ToString() );
		SwrpChat.SystemTo( caller, $"{c.Name} is now {ranks[to].Name}." );
		Refresh( steamId, c.Id, $"{Actor( caller, me )} {( step > 0 ? "promoted" : "demoted" )} you to {ranks[to].Name}.", step < 0 );
	}

	static void Role( Connection caller, SwrpPlayer me, string battalion, int auth, string args )
	{
		// the member is everything but the last word; the role is the last word(s) after the member's name... keep it simple:
		// "role <member> <role>" where member is one word (name, designation or character id)
		var p = args.Split( ' ', 2, StringSplitOptions.RemoveEmptyEntries );
		if ( p.Length < 2 ) { SwrpChat.SystemTo( caller, "Usage: /battalion role <member> <role, or none>" ); return; }
		var (steamId, c) = Find( battalion, p[0] );
		if ( c is null ) { SwrpChat.SystemTo( caller, $"No member matches '{p[0]}'." ); return; }
		if ( !Outranks( caller, auth, battalion, steamId, c.RankIndex ) && !( steamId == caller.SteamId.ValueUnsigned && auth >= 0 ) )
		{
			SwrpChat.SystemTo( caller, $"You can't set {c.Name}'s role: they outrank you or hold a post." );
			return;
		}
		var role = SwrpChat.Clean( p[1], 24 );
		if ( role.Equals( "none", StringComparison.OrdinalIgnoreCase ) || role == "-" ) role = "";
		if ( !CheckRate( caller, "role" ) ) return;
		var from = c.BattalionRole ?? "";
		c.BattalionRole = role.Length == 0 ? null : role;
		CharacterStore.Save( steamId );
		Record( caller, me, battalion, "role", steamId, c.Id, c.FullTitle, from, role );
		SwrpChat.SystemTo( caller, role.Length == 0 ? $"{c.Name} has no role now." : $"{c.Name} is now {role}." );
		Refresh( steamId, c.Id, role.Length == 0 ? null : $"Your role in the battalion is now {role}.", false );
	}

	// ----- transfers -----

	static bool IsCommand( Connection c, string battalion ) => PostOf( battalion, c.SteamId.ValueUnsigned ) is Post.Commander or Post.Executive;

	static void Transfer( Connection caller, SwrpPlayer me, string battalion, int auth, string args )
	{
		var p = args.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		if ( p.Length < 2 || FactionRegistry.Get( p[^1].ToLowerInvariant() ) is not { } target ) { SwrpChat.SystemTo( caller, $"Usage: /battalion transfer <member> <{string.Join( "|", FactionRegistry.All.Select( x => x.Id ) )}>" ); return; }
		var (steamId, c) = Find( battalion, string.Join( ' ', p[..^1] ) );
		if ( c is null ) { SwrpChat.SystemTo( caller, "No member of your battalion matches that." ); return; }
		if ( target.Id == battalion ) { SwrpChat.SystemTo( caller, "They are already in that battalion." ); return; }
		var staff = IsStaff( caller );
		if ( !staff && !IsCommand( caller, battalion ) ) { SwrpChat.SystemTo( caller, "Transfers are arranged by a battalion's Commander or Executive Officer (or staff)." ); return; }
		if ( !staff && !Outranks( caller, auth, battalion, steamId, c.RankIndex ) ) { SwrpChat.SystemTo( caller, $"You can't transfer {c.Name}." ); return; }
		if ( !CheckRate( caller, "transfer" ) ) return;

		if ( staff )
		{
			DoTransfer( caller, me, steamId, c, target.Id );
			return;
		}

		var req = new BattalionTransfer
		{
			Id = Data.NextId++, When = DateTime.UtcNow, SteamId = steamId, CharacterId = c.Id, Name = c.FullTitle, From = battalion, To = target.Id,
			RequestedBy = Actor( caller, me ), RequestedBySteamId = caller.SteamId.ValueUnsigned, FromApproved = true,
		};
		Data.Transfers.Add( req );
		Save();
		SwrpChat.StaffLog( $"[{FactionRegistry.Get( battalion )?.Name}] transfer request #{req.Id}: {req.RequestedBy} asks to move {c.Name} to the {target.Name}" );
		SwrpChat.SystemTo( caller, $"Transfer request #{req.Id} sent: the {target.Name}'s Commander or XO (or staff) must approve it with /battalion approve {req.Id}." );
		foreach ( var conn in Connection.All.Where( x => IsCommand( x, target.Id ) ) )
			SwrpChat.SystemTo( conn, $"[{target.Name}] {req.RequestedBy} asks to transfer {c.Name} into your battalion. /battalion approve {req.Id} or /battalion deny {req.Id}." );
	}

	static void Decide( Connection caller, SwrpPlayer me, string args, bool approve )
	{
		var req = int.TryParse( args.Trim().TrimStart( '#' ), out var id ) ? Data.Transfers.FirstOrDefault( t => t.Id == id ) : null;
		if ( req is null ) { SwrpChat.SystemTo( caller, "No pending transfer with that number. The battalion menu lists them." ); return; }
		var staff = IsStaff( caller );
		var fromSide = IsCommand( caller, req.From );
		var toSide = IsCommand( caller, req.To );
		if ( !staff && !fromSide && !toSide ) { SwrpChat.SystemTo( caller, "Only either battalion's Commander or XO (or staff) can decide this transfer." ); return; }

		if ( !approve )
		{
			Data.Transfers.Remove( req );
			Save();
			SwrpChat.StaffLog( $"transfer request #{req.Id} ({req.Name} → {req.To}) denied by {Actor( caller, me )}" );
			SwrpChat.SystemTo( caller, $"Transfer #{req.Id} denied." );
			return;
		}
		if ( staff ) { req.FromApproved = true; req.ToApproved = true; }
		if ( fromSide ) req.FromApproved = true;
		if ( toSide ) req.ToApproved = true;
		if ( !req.FromApproved || !req.ToApproved )
		{
			Save();
			SwrpChat.SystemTo( caller, $"Approved on your side; transfer #{req.Id} still needs the other battalion's command." );
			return;
		}

		Data.Transfers.Remove( req );
		var c = CharacterStore.Find( req.SteamId, req.CharacterId );
		if ( c is null || c.FactionId != req.From ) { Save(); SwrpChat.SystemTo( caller, "That character is no longer in the battalion; the request was dropped." ); return; }
		DoTransfer( caller, me, req.SteamId, c, req.To );
	}

	static void DoTransfer( Connection caller, SwrpPlayer me, ulong steamId, CharacterRecord c, string to )
	{
		var from = c.FactionId;
		var target = FactionRegistry.Get( to );
		var role = c.BattalionRole;
		c.FactionId = to;
		c.RankIndex = Math.Clamp( c.RankIndex, 0, target.Ranks.Count - 1 );
		c.BattalionRole = null;
		CharacterStore.Save( steamId );
		if ( target.Whitelisted && !Whitelist.IsWhitelistedFor( steamId, to ) ) Whitelist.Grant( steamId, to, Actor( caller, me ), NameOf( steamId ) );
		var a = Record( caller, me, from, "transfer", steamId, c.Id, c.FullTitle, from, to );
		a.From = $"{from}|{role}";
		Save();
		SwrpChat.SystemTo( caller, $"{c.Name} now serves in the {target.Name}." );
		Refresh( steamId, c.Id, $"You've been transferred to the {target.Name}.", false );
	}

	// ----- staff: posts and reversal -----

	static void PostCommand( Connection caller, SwrpPlayer me, string args )
	{
		if ( !IsStaff( caller ) ) { SwrpChat.SystemTo( caller, "Battalion posts are set by staff." ); return; }
		var p = args.Split( ' ', 3, StringSplitOptions.RemoveEmptyEntries );
		if ( p.Length < 3 || FactionRegistry.Get( p[0].ToLowerInvariant() ) is not { } f )
		{
			SwrpChat.SystemTo( caller, "Usage: /battalion post <battalion> <commander|xo|jedi> <player or SteamID, or none>" );
			return;
		}
		var which = p[1].ToLowerInvariant() switch
		{
			"commander" or "leader" or "co" => Post.Commander,
			"xo" or "executive" or "colead" or "coleader" or "co-leader" => Post.Executive,
			"jedi" or "general" or "jedigeneral" => Post.Jedi,
			_ => Post.None,
		};
		if ( which == Post.None ) { SwrpChat.SystemTo( caller, "Post must be commander, xo or jedi." ); return; }
		ulong sid = 0;
		var label = "none";
		if ( !p[2].Equals( "none", StringComparison.OrdinalIgnoreCase ) && !SwrpNet.TryResolveSteamId( p[2], out sid, out label ) )
		{
			SwrpChat.SystemTo( caller, $"No online player or SteamID matches '{p[2]}'." );
			return;
		}
		SetPost( caller, me, f.Id, which, sid, label );
	}

	static void SetPost( Connection caller, SwrpPlayer me, string battalion, Post which, ulong sid, string label )
	{
		if ( !Data.Posts.TryGetValue( battalion, out var posts ) ) Data.Posts[battalion] = posts = new BattalionPosts();
		var old = which switch { Post.Commander => posts.Commander, Post.Executive => posts.Executive, _ => posts.JediGeneral };
		switch ( which )
		{
			case Post.Commander: posts.Commander = sid; break;
			case Post.Executive: posts.Executive = sid; break;
			default: posts.JediGeneral = sid; break;
		}
		// one post per person per battalion
		if ( sid != 0 )
		{
			if ( which != Post.Commander && posts.Commander == sid ) posts.Commander = 0;
			if ( which != Post.Executive && posts.Executive == sid ) posts.Executive = 0;
			if ( which != Post.Jedi && posts.JediGeneral == sid ) posts.JediGeneral = 0;
		}
		Save();
		var a = Record( caller, me, battalion, "post", sid, null, sid == 0 ? $"nobody ({NameOf( old )} stood down)" : label, PostLabel( which ), $"{(int)which}|{old}" );
		SwrpChat.SystemTo( caller, $"{FactionRegistry.Get( battalion )?.Name} {PostLabel( which )}: {( sid == 0 ? "vacant" : label )}." );
		if ( sid != 0 ) Refresh( sid, null, $"You are now the {PostLabel( which )} of the {FactionRegistry.Get( battalion )?.Name}.", false );
	}

	static void Reverse( Connection caller, SwrpPlayer me, string args )
	{
		if ( !IsStaff( caller ) ) { SwrpChat.SystemTo( caller, "Only staff can reverse battalion actions." ); return; }
		var a = int.TryParse( args.Trim().TrimStart( '#' ), out var id ) ? Data.Actions.FirstOrDefault( x => x.Id == id ) : null;
		if ( a is null || a.Kind == "reverse" ) { SwrpChat.SystemTo( caller, "No reversible battalion action with that number (/battalion log lists them)." ); return; }
		if ( a.Reversed ) { SwrpChat.SystemTo( caller, $"Action #{a.Id} was already reversed." ); return; }

		var c = a.TargetCharacterId is null ? null : CharacterStore.Find( a.TargetSteamId, a.TargetCharacterId );
		var fromParts = ( a.From ?? "" ).Split( '|' );
		switch ( a.Kind )
		{
			case "whitelist":
				Whitelist.Revoke( a.TargetSteamId, a.Battalion );
				break;
			case "remove" when c is null:
				Whitelist.Grant( a.TargetSteamId, a.Battalion, caller.DisplayName, a.TargetName );
				break;
			case "remove":
			case "transfer":
				if ( c is null ) { SwrpChat.SystemTo( caller, "That character no longer exists." ); return; }
				c.FactionId = fromParts[0];
				c.BattalionRole = fromParts.Length > 1 && fromParts[1].Length > 0 ? fromParts[1] : null;
				CharacterStore.Save( a.TargetSteamId );
				if ( FactionRegistry.Get( fromParts[0] ) is { Whitelisted: true } ) Whitelist.Grant( a.TargetSteamId, fromParts[0], caller.DisplayName, a.TargetName );
				break;
			case "promote":
			case "demote":
				if ( c is null || !int.TryParse( a.From, out var rank ) ) { SwrpChat.SystemTo( caller, "That character no longer exists." ); return; }
				var now = c.RankIndex;
				c.RankIndex = rank;
				CharacterStore.Save( a.TargetSteamId );
				SwrpRanks.Record( caller.DisplayName, caller.SteamId.ValueUnsigned, c, now, rank, $"reversed #{a.Id}" );
				break;
			case "role":
				if ( c is null ) { SwrpChat.SystemTo( caller, "That character no longer exists." ); return; }
				c.BattalionRole = string.IsNullOrEmpty( a.From ) ? null : a.From;
				CharacterStore.Save( a.TargetSteamId );
				break;
			case "post":
				var t = ( a.To ?? "" ).Split( '|' );
				if ( t.Length == 2 && int.TryParse( t[0], out var w ) && ulong.TryParse( t[1], out var prev ) )
				{
					if ( !Data.Posts.TryGetValue( a.Battalion, out var posts ) ) Data.Posts[a.Battalion] = posts = new BattalionPosts();
					switch ( (Post)w )
					{
						case Post.Commander: posts.Commander = prev; break;
						case Post.Executive: posts.Executive = prev; break;
						default: posts.JediGeneral = prev; break;
					}
				}
				break;
		}
		a.Reversed = true;
		Record( caller, me, a.Battalion, "reverse", a.TargetSteamId, a.TargetCharacterId, a.TargetName, a.Kind, a.Id.ToString() );
		if ( c is not null ) Refresh( a.TargetSteamId, c.Id, "Staff reversed a battalion change to your character.", false );
		SwrpChat.SystemTo( caller, $"Reversed #{a.Id}: {a.Describe()}." );
	}

	static void ShowLog( Connection caller, string battalion, string args )
	{
		var staff = IsStaff( caller );
		var n = int.TryParse( args, out var k ) ? Math.Clamp( k, 1, 30 ) : 10;
		var rows = Data.Actions.Where( a => staff || a.Battalion == battalion ).TakeLast( n ).ToList();
		if ( rows.Count == 0 ) { SwrpChat.SystemTo( caller, "No battalion actions logged." ); return; }
		foreach ( var a in rows )
			SwrpChat.SystemTo( caller, $"#{a.Id} {a.When:MM-dd HH:mm} [{FactionRegistry.Get( a.Battalion )?.Name ?? a.Battalion}] {a.By} {a.Describe()}{( a.Reversed ? " (reversed)" : "" )}" );
	}

	// ----- menu data -----

	public sealed class RosterData
	{
		public string Battalion { get; set; }
		public string Name { get; set; }
		public string ColorHex { get; set; }
		public string Commander { get; set; }
		public string Executive { get; set; }
		public string JediGeneral { get; set; }
		public bool IsStaff { get; set; }
		public bool IsOfficer { get; set; }
		public bool IsCommand { get; set; }
		public string MyPost { get; set; }
		public string PromoteLimit { get; set; }
		public List<Member> Members { get; set; } = new();
		public List<string> Pending { get; set; } = new();
		public List<int> PendingIds { get; set; } = new();
		public List<string> Log { get; set; } = new();
		public List<int> LogIds { get; set; } = new();
		public List<string> Battalions { get; set; } = new();
	}

	/// <summary> Client: the last roster the host sent. </summary>
	public static RosterData Menu { get; private set; }
	public static int MenuVersion { get; private set; }

	[Rpc.Host]
	public static void RequestRoster()
	{
		var caller = Rpc.Caller;
		SendRoster( caller, SwrpGameManager.Current?.FindPlayer( caller ) );
	}

	static void SendRoster( Connection caller, SwrpPlayer me )
	{
		var battalion = ViewedBattalion( caller, me );
		if ( battalion is null || FactionRegistry.Get( battalion ) is not { } f ) return;
		var auth = Authority( caller, me, battalion );
		var posts = PostsOf( battalion );
		var staff = IsStaff( caller );
		var ranks = f.Ranks;
		var data = new RosterData
		{
			Battalion = f.Id, Name = f.Name, ColorHex = f.ColorHex,
			Commander = NameOf( posts.Commander ), Executive = NameOf( posts.Executive ), JediGeneral = NameOf( posts.JediGeneral ),
			IsStaff = staff, IsOfficer = auth >= 0 || staff, IsCommand = staff || IsCommand( caller, battalion ),
			MyPost = PostLabel( PostOf( battalion, caller.SteamId.ValueUnsigned ) ),
			PromoteLimit = staff ? "any rank" : auth > 0 ? ranks[Math.Clamp( auth - 1, 0, ranks.Count - 1 )].Name : "",
			Members = Roster( battalion, caller, auth ),
			Battalions = FactionRegistry.All.Select( x => x.Id ).ToList(),
		};
		foreach ( var t in Data.Transfers.Where( t => staff || t.From == battalion || t.To == battalion ) )
		{
			data.Pending.Add( $"#{t.Id} {t.Name}: {FactionRegistry.Get( t.From )?.Name} → {FactionRegistry.Get( t.To )?.Name} (by {t.RequestedBy}; {( t.FromApproved ? "sent" : "waiting" )}, {( t.ToApproved ? "accepted" : "awaiting the receiving battalion" )})" );
			data.PendingIds.Add( t.Id );
		}
		foreach ( var a in Data.Actions.Where( a => staff || a.Battalion == battalion ).TakeLast( 12 ).Reverse() )
		{
			data.Log.Add( $"#{a.Id} {a.When:MM-dd HH:mm} {a.By} {a.Describe()}{( a.Reversed ? " (reversed)" : "" )}" );
			data.LogIds.Add( a.Kind == "reverse" || a.Reversed ? 0 : a.Id );
		}
		using ( Rpc.FilterInclude( caller ) ) ReceiveRoster( Json.Serialize( data ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveRoster( string json )
	{
		Menu = Json.Deserialize<RosterData>( json );
		MenuVersion++;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void OpenMenu() => SwrpUiState.BattalionOpen = true;

	[ConCmd( "swrp_battalion_dump", Help = "swrp_battalion_dump - print the battalion roster this client last received (open it with /battalion first)" )]
	public static void Dump()
	{
		if ( Menu is not { } m ) { Log.Info( "[SWRP] No roster received yet." ); return; }
		Log.Info( $"[SWRP] {m.Name}: commander {m.Commander}, xo {m.Executive}, jedi {m.JediGeneral}; officer {m.IsOfficer}, command {m.IsCommand}, staff {m.IsStaff}, promote up to {m.PromoteLimit}" );
		foreach ( var x in m.Members )
			Log.Info( $"[SWRP]   {x.Rank} {x.Designation} {x.Name} ({x.Player}) role '{x.Role}' post '{x.Post}' {x.LastSeen} rank:{x.CanRank} role:{x.CanRole} remove:{x.CanRemove} transfer:{x.CanTransfer}" );
		foreach ( var l in m.Log ) Log.Info( $"[SWRP]   log {l}" );
	}

	/// <summary> Staff panel: every battalion's posts, for the Battalions tab. </summary>
	public static List<string> PostSummary() => FactionRegistry.All.Select( f =>
	{
		var p = PostsOf( f.Id );
		return $"{f.Id}|{f.Name}|{NameOf( p.Commander )}|{NameOf( p.Executive )}|{NameOf( p.JediGeneral )}";
	} ).ToList();
}
