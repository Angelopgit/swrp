using System;

namespace Swrp;

/// <summary>
/// The staff toolkit around <see cref="SwrpStaff"/>: the staff action log, rate limits, player reports, the duty toggle and
/// staff powers (noclip, god mode, invisibility), /tp, /announce, chat whitelisting, and the data the staff panel shows.
/// All host side; chat commands land here from <see cref="SwrpChat"/>, and the staff panel (StaffMenu key or /staff) sends the same chat commands.
/// </summary>
public static class SwrpStaffTools
{
	// ----- Staff action log (swrp/stafflog.json) -----

	public const string LogFile = "swrp/stafflog.json";

	public sealed class LogEntry
	{
		public DateTime When { get; set; }
		public string By { get; set; }
		public ulong BySteamId { get; set; }
		public string Action { get; set; }
		public string Target { get; set; }
		public ulong TargetSteamId { get; set; }
		public string Details { get; set; }
	}

	static List<LogEntry> _log;
	static List<LogEntry> Entries => _log ??= FileSystem.Data.ReadJson<List<LogEntry>>( LogFile, null ) ?? new();

	public static void Reset()
	{
		_log = null;
		_recent.Clear();
		_reports.Clear();
		_offDuty.Clear();
		_noclip.Clear();
		_god.Clear();
		_invisible.Clear();
		_lastReport.Clear();
	}

	/// <summary> Host: record a staff action. The target is the first word of the arguments when it names an online player. </summary>
	public static void Record( Connection caller, string action, string args )
	{
		var (first, rest) = Split( args );
		var target = SwrpNet.FindConnection( first );
		var entry = new LogEntry
		{
			When = DateTime.UtcNow,
			By = caller?.DisplayName ?? "console",
			BySteamId = caller?.SteamId.ValueUnsigned ?? 0,
			Action = action,
			Target = target?.DisplayName ?? first,
			TargetSteamId = target?.SteamId.ValueUnsigned ?? ( ulong.TryParse( first, out var id ) ? id : 0 ),
			Details = target is null ? rest : rest,
		};
		Entries.Add( entry );
		if ( Entries.Count > 2000 ) Entries.RemoveRange( 0, Entries.Count - 2000 );

		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( LogFile, Entries );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not save the staff log: {e.Message}" );
		}
	}

	/// <summary> Host: record an action that has no single target player (Game Master events, ship alerts). </summary>
	public static void RecordText( Connection caller, string action, string details ) => Record( caller, action, $"- {details}" );

	public static IEnumerable<LogEntry> Query( string filter, int count )
	{
		filter = filter?.Trim();
		return Entries.Where( e => string.IsNullOrEmpty( filter )
				|| e.By.Contains( filter, StringComparison.OrdinalIgnoreCase )
				|| ( e.Target?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false )
				|| e.TargetSteamId.ToString() == filter || e.BySteamId.ToString() == filter )
			.TakeLast( Math.Clamp( count, 1, 100 ) );
	}

	// ----- Rate limits (stop a compromised or angry staff account mass-banning) -----

	static readonly Dictionary<(ulong, string), List<DateTime>> _recent = new();

	static readonly Dictionary<string, (int Count, TimeSpan Window)> Limits = new()
	{
		["/ban"] = (4, TimeSpan.FromMinutes( 10 )),
		["/kick"] = (8, TimeSpan.FromMinutes( 5 )),
		["/slay"] = (10, TimeSpan.FromMinutes( 2 )),
		["/jail"] = (10, TimeSpan.FromMinutes( 5 )),
		["/setrole"] = (5, TimeSpan.FromMinutes( 10 )),
		["/announce"] = (4, TimeSpan.FromMinutes( 2 )),
	};

	/// <summary> Host: false (and a message) if the caller is over the limit for this command. The host is never limited. </summary>
	public static bool CheckRate( Connection caller, string cmd )
	{
		if ( caller is null || caller.IsHost || !Limits.TryGetValue( cmd, out var limit ) ) return true;
		var key = (caller.SteamId.ValueUnsigned, cmd);
		if ( !_recent.TryGetValue( key, out var times ) ) _recent[key] = times = new();
		var now = DateTime.UtcNow;
		times.RemoveAll( t => now - t > limit.Window );
		if ( times.Count >= limit.Count )
		{
			SwrpChat.SystemTo( caller, $"Slow down: at most {limit.Count}x {cmd} per {limit.Window.TotalMinutes:0} minutes." );
			SwrpChat.StaffLog( $"{caller.DisplayName} hit the {cmd} rate limit" );
			return false;
		}

		times.Add( now );
		return true;
	}

	// ----- Reports -----

	public sealed class Report
	{
		public int Id { get; set; }
		public DateTime When { get; set; }
		public string Reporter { get; set; }
		public ulong ReporterSteamId { get; set; }
		public string Target { get; set; }
		public ulong TargetSteamId { get; set; }
		public string Reason { get; set; }
		public string ClaimedBy { get; set; }
		public bool Closed { get; set; }
		public string Note { get; set; }
	}

	static readonly List<Report> _reports = new();
	static readonly Dictionary<ulong, DateTime> _lastReport = new();
	static int _nextReport = 1;

	public static IReadOnlyList<Report> Reports => _reports;

	/// <summary> Host: /report &lt;player&gt; &lt;reason&gt; from anyone. </summary>
	public static void FileReport( Connection caller, string args )
	{
		var (name, reason) = Split( args );
		if ( string.IsNullOrWhiteSpace( name ) || reason.Length < 3 )
		{
			SwrpChat.SystemTo( caller, "Usage: /report <player> <what happened>" );
			return;
		}

		var id = caller.SteamId.ValueUnsigned;
		if ( _lastReport.TryGetValue( id, out var last ) && DateTime.UtcNow - last < TimeSpan.FromSeconds( 30 ) )
		{
			SwrpChat.SystemTo( caller, "You just sent a report. Give staff a moment." );
			return;
		}

		var target = SwrpNet.FindConnection( name );

		_lastReport[id] = DateTime.UtcNow;
		var r = new Report
		{
			Id = _nextReport++,
			When = DateTime.UtcNow,
			Reporter = caller.DisplayName,
			ReporterSteamId = id,
			Target = target?.DisplayName ?? name,
			TargetSteamId = target?.SteamId.ValueUnsigned ?? 0,
			Reason = reason.Length > 200 ? reason[..200] : reason,
		};
		_reports.Add( r );
		if ( _reports.Count > 100 ) _reports.RemoveAt( 0 );

		SwrpChat.SystemTo( caller, $"Report #{r.Id} sent to staff. Thanks." );
		var staff = Connection.All.Where( Whitelist.IsStaff ).ToList();
		SwrpChat.SendTo( staff, ChatKind.Staff, $"[REPORT #{r.Id}] {r.Reporter} reported {r.Target}: {r.Reason}  (/report claim {r.Id})" );
		using ( Rpc.FilterInclude( staff ) ) StaffPing();
		Record( caller, "/report", $"{r.TargetSteamId} {r.Reason}" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void StaffPing() => SwrpAudio.Ui( SwrpAudio.Dispatch );

	/// <summary> Host: /report claim|close &lt;id&gt; [note] and /reports, for staff. </summary>
	public static bool ReportStaffCommand( Connection caller, string sub, string args )
	{
		if ( !Whitelist.IsStaff( caller ) ) return false;
		var (idText, note) = Split( args );
		var r = int.TryParse( idText, out var rid ) ? _reports.FirstOrDefault( x => x.Id == rid ) : null;

		switch ( sub )
		{
			case "claim":
				if ( r is null ) { SwrpChat.SystemTo( caller, $"No report #{idText}." ); return true; }
				r.ClaimedBy = caller.DisplayName;
				SwrpChat.StaffLog( $"{caller.DisplayName} claimed report #{r.Id} ({r.Target})" );
				Record( caller, "/report claim", $"{r.TargetSteamId} #{r.Id}" );
				return true;
			case "close":
				if ( r is null ) { SwrpChat.SystemTo( caller, $"No report #{idText}." ); return true; }
				r.Closed = true;
				r.ClaimedBy ??= caller.DisplayName;
				r.Note = note;
				SwrpChat.StaffLog( $"{caller.DisplayName} closed report #{r.Id}{( string.IsNullOrEmpty( note ) ? "" : $": {note}" )}" );
				Record( caller, "/report close", $"{r.TargetSteamId} #{r.Id} {note}" );
				if ( Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == r.ReporterSteamId ) is { } reporter )
					SwrpChat.SystemTo( reporter, $"Your report #{r.Id} was handled by staff." );
				return true;
			case "list":
				var open = _reports.Where( x => !x.Closed ).ToList();
				if ( open.Count == 0 ) { SwrpChat.SystemTo( caller, "No open reports." ); return true; }
				foreach ( var x in open.TakeLast( 10 ) )
					SwrpChat.SystemTo( caller, $"#{x.Id} {x.Reporter} -> {x.Target}: {x.Reason}{( x.ClaimedBy is null ? "" : $" (claimed by {x.ClaimedBy})" )}" );
				return true;
		}

		return false;
	}

	// ----- Duty and powers -----

	static readonly HashSet<ulong> _offDuty = new();
	static readonly HashSet<ulong> _noclip = new();
	static readonly HashSet<ulong> _god = new();
	static readonly HashSet<ulong> _invisible = new();

	public static bool OnDuty( ulong steamId ) => !_offDuty.Contains( steamId );
	public static bool IsGod( ulong steamId ) => _god.Contains( steamId );

	/// <summary> Host, every frame per player: keep the synced power flags in line. </summary>
	public static void TickPlayer( SwrpPlayer p )
	{
		var id = p.OwnerSteamId;
		var noclip = _noclip.Contains( id );
		if ( p.Noclip != noclip ) p.Noclip = noclip;
		var invis = _invisible.Contains( id );
		if ( p.Invisible != invis ) p.Invisible = invis;
		var god = _god.Contains( id );
		if ( p.GodMode != god ) p.GodMode = god;
		var duty = OnDuty( id ) && Whitelist.IsStaff( p.Network.Owner );
		if ( p.StaffOnDuty != duty ) p.StaffOnDuty = duty;
	}

	/// <summary> Host: the toolkit's own commands. Returns true if <paramref name="cmd"/> was one of them. </summary>
	public static bool TryRun( Connection caller, string cmd, string args )
	{
		var role = Whitelist.GetRole( caller );
		if ( cmd == "/heal" ) return Heal( caller, role, args );      // (kept out of Commands: a hotload keeps that table's old contents)
		if ( !Commands.TryGetValue( cmd, out var need ) ) return false;
		need = SwrpPermissions.Need( cmd, need );
		if ( role < need ) { SwrpChat.SystemTo( caller, $"{cmd} needs {need} or higher." ); return true; }
		if ( !CheckRate( caller, cmd ) ) return true;

		var me = SwrpGameManager.Current?.FindPlayer( caller );
		var id = caller.SteamId.ValueUnsigned;
		args ??= "";

		switch ( cmd )
		{
			case "/duty":
				if ( _offDuty.Remove( id ) ) SwrpChat.SystemTo( caller, "On duty: staff powers available." );
				else
				{
					_offDuty.Add( id );
					_noclip.Remove( id );
					_god.Remove( id );
					_invisible.Remove( id );
					SwrpChat.SystemTo( caller, "Off duty: noclip, god and invisibility are off. Play normally; /duty to go back on." );
				}
				SwrpChat.StaffLog( $"{caller.DisplayName} is {( OnDuty( id ) ? "on" : "off" )} duty" );
				break;

			case "/noclip":
			case "/god":
			case "/invis":
			case "/invisible":
			{
				if ( !OnDuty( id ) ) { SwrpChat.SystemTo( caller, "You're off duty. /duty first." ); return true; }
				if ( me is null ) { SwrpChat.SystemTo( caller, "Deploy a character first." ); return true; }
				var set = cmd switch { "/noclip" => _noclip, "/god" => _god, _ => _invisible };
				if ( !set.Add( id ) ) set.Remove( id );
				var label = cmd == "/noclip" ? "Noclip" : cmd == "/god" ? "God mode" : "Invisibility";
				SwrpChat.SystemTo( caller, $"{label} {( set.Contains( id ) ? "ON" : "OFF" )}." );
				SwrpChat.StaffLog( $"{caller.DisplayName} turned {label.ToLowerInvariant()} {( set.Contains( id ) ? "on" : "off" )}" );
				break;
			}

			case "/tp":
			{
				if ( me is null ) { SwrpChat.SystemTo( caller, "Deploy a character first." ); return true; }
				var parts = args.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				if ( parts.Length < 3 || !float.TryParse( parts[0], out var x ) || !float.TryParse( parts[1], out var y ) || !float.TryParse( parts[2], out var z ) )
				{
					SwrpChat.SystemTo( caller, $"Usage: /tp <x> <y> <z>. You are at {me.WorldPosition.x:0} {me.WorldPosition.y:0} {me.WorldPosition.z:0}." );
					return true;
				}
				me.TeleportTo( new Vector3( x, y, z ), me.Controller.IsValid() ? me.Controller.EyeAngles.yaw : 0 );
				SwrpChat.StaffLog( $"{caller.DisplayName} teleported to {x:0} {y:0} {z:0}" );
				break;
			}

			case "/announce":
			{
				var text = args.Trim();
				if ( text.Length == 0 ) { SwrpChat.SystemTo( caller, "Usage: /announce <message>" ); return true; }
				if ( text.Length > 160 ) text = text[..160];
				Announce( text, caller.DisplayName );
				SwrpChat.SendTo( Connection.All, ChatKind.Broadcast, $"[ANNOUNCEMENT] {text}" );
				SwrpChat.StaffLog( $"{caller.DisplayName} announced: {text}" );
				break;
			}

			case "/stafflog":
			{
				var (first, rest) = Split( args );
				var count = int.TryParse( first, out var n ) ? n : 10;
				var filter = int.TryParse( first, out _ ) ? rest : args.Trim();
				var rows = Query( filter, count ).ToList();
				if ( rows.Count == 0 ) { SwrpChat.SystemTo( caller, "No staff actions logged." ); return true; }
				foreach ( var e in rows )
					SwrpChat.SystemTo( caller, $"{e.When:MM-dd HH:mm} {e.By} {e.Action} {e.Target} {e.Details}".Trim() );
				return true;      // reading the log isn't itself logged
			}

			case "/reports":
				ReportStaffCommand( caller, "list", "" );
				return true;

			case "/staff":
			case "/staffmenu":
				using ( Rpc.FilterInclude( caller ) ) OpenPanel();
				return true;

			case "/whitelist":
			case "/unwhitelist":
			{
				var (who, faction) = Split( args );
				var grant = cmd == "/whitelist";
				if ( !SwrpNet.TryResolveSteamId( who, out var steamId, out var label ) ) { SwrpChat.SystemTo( caller, $"No player or SteamID matches '{who}'." ); return true; }
				var f = FactionRegistry.Get( faction.Trim() );
				if ( f is null ) { SwrpChat.SystemTo( caller, $"Unknown faction. Valid: {string.Join( ", ", FactionRegistry.All.Select( x => x.Id ) )}" ); return true; }
				var changed = grant ? Whitelist.Grant( steamId, f.Id, caller.DisplayName, label ) : Whitelist.Revoke( steamId, f.Id );
				SwrpChat.SystemTo( caller, changed ? $"{label} {( grant ? "whitelisted for" : "removed from" )} the {f.Name}." : "Nothing changed." );
				if ( changed && Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == steamId ) is { } tc )
				{
					SwrpNet.NotifyConnection( tc, $"You have been {( grant ? "whitelisted for" : "removed from" )} the {f.Name}.", false );
					SwrpNet.SendMenuState( tc );
				}
				if ( changed ) SwrpChat.StaffLog( $"{caller.DisplayName} {( grant ? "whitelisted" : "unwhitelisted" )} {label} for {f.Name}" );
				break;
			}

			default:
				return false;
		}

		Record( caller, cmd, args );
		return true;
	}

	/// <summary>
	/// /heal [player]: staff (Moderator and up) restore health and armour, stop bleeding and get a downed trooper back up, on
	/// themselves or on another player by name, designation or SteamID (Angelo, 2026-10-14).
	/// </summary>
	static bool Heal( Connection caller, StaffRole role, string args )
	{
		var healNeed = SwrpPermissions.Need( "/heal", StaffRole.Moderator );
		if ( role < healNeed ) { SwrpChat.SystemTo( caller, $"/heal needs {healNeed} or higher." ); return true; }
		if ( !CheckRate( caller, "/heal" ) ) return true;
		var who = ( args ?? "" ).Trim();
		SwrpPlayer target;
		if ( who.Length == 0 ) target = SwrpGameManager.Current?.FindPlayer( caller );
		else
		{
			if ( !SwrpNet.TryResolveSteamId( who, out var steamId, out _ ) ) { SwrpChat.SystemTo( caller, $"No online player matches '{who}'." ); return true; }
			target = Game.ActiveScene?.GetAllComponents<SwrpPlayer>().FirstOrDefault( p => p.OwnerSteamId == steamId && p.CharacterId is not null );
		}
		if ( !target.IsValid() ) { SwrpChat.SystemTo( caller, who.Length == 0 ? "Deploy a character first." : $"{who} has no character deployed." ); return true; }
		var vitals = target.GetComponent<SwrpVitals>();
		if ( vitals is null || !vitals.StaffHeal() ) { SwrpChat.SystemTo( caller, $"{target.FullTitle} is dead: they redeploy instead." ); return true; }
		SwrpChat.SystemTo( caller, who.Length == 0 ? "Healed: full health and armour." : $"Healed {target.FullTitle}." );
		if ( who.Length > 0 && target.Network.Owner is { } o ) SwrpNet.NotifyConnection( o, $"{caller.DisplayName} healed you.", false );
		SwrpChat.StaffLog( $"{caller.DisplayName} healed {target.FullTitle}" );
		Record( caller, "/heal", who );
		return true;
	}

	/// <summary> This toolkit's commands and their default roles, for the permissions grid. </summary>
	// /qual and /class are officer commands (SwrpCareer); the grid sets which staff role may use them on any trooper
	public static IEnumerable<KeyValuePair<string, StaffRole>> DefaultRoles() => Commands.Append( KeyValuePair.Create( "/heal", StaffRole.Moderator ) )
		.Append( KeyValuePair.Create( "/qual", StaffRole.Moderator ) ).Append( KeyValuePair.Create( "/class", StaffRole.Moderator ) );

	static readonly Dictionary<string, StaffRole> Commands = new()
	{
		["/duty"] = StaffRole.Moderator,
		["/noclip"] = StaffRole.Moderator,
		["/god"] = StaffRole.Moderator,
		["/invis"] = StaffRole.Moderator,
		["/invisible"] = StaffRole.Moderator,
		["/tp"] = StaffRole.Moderator,
		["/announce"] = StaffRole.Moderator,
		["/stafflog"] = StaffRole.TrialModerator,
		["/reports"] = StaffRole.TrialModerator,
		["/staff"] = StaffRole.TrialModerator,
		["/staffmenu"] = StaffRole.TrialModerator,
		["/whitelist"] = StaffRole.Admin,
		["/unwhitelist"] = StaffRole.Admin,
	};

	public static string HelpLine => "Toolkit: /duty /noclip /god /invis /heal [player] /tp <x y z> /announce <text> /stafflog [n] [player] /reports, /report claim|close <id> [note]; Admin: /whitelist /unwhitelist <player|steamid> <faction>. O or /staff opens the staff panel; its HOTKEYS row puts noclip, god and invisibility on keys of your choice (saved as swrp_staffkey_noclip/_god/_invis; noclip starts on V: tap for noclip, hold to talk). Hold Q for the spawn menu: NPCs (friendly clones carry the DC-15A), weapons and ammo where you aim, props, physgun and remover. Console: swrp_doorway_check reports anything blocking a doorway; pose dev tools: swrp_pose_stage, swrp_gun_profile, swrp_grip, swrp_salute, swrp_atease, swrp_thumb, swrp_carry, swrp_menu_stage, swrp_menu_stage_yaw; swrp_roofdoors [0/1] opens or shuts the topside hangar doors.";

	// ----- Announcement banner -----

	public static string Banner { get; private set; }
	public static string BannerBy { get; private set; }
	public static RealTimeSince SinceBanner { get; private set; } = 100;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Announce( string text, string by )
	{
		Banner = text;
		BannerBy = by;
		SinceBanner = 0;
		SwrpAudio.Ui( SwrpAudio.OrderPosted );
	}

	// ----- Staff panel data -----

	public sealed class PanelCharacter
	{
		public string Name { get; set; }
		public string Designation { get; set; }
		public string Faction { get; set; }
		public string Rank { get; set; }
		public string Class { get; set; }
		public bool Deployed { get; set; }
	}

	public sealed class PanelPlayer
	{
		public ulong SteamId { get; set; }
		public string Name { get; set; }
		public string CharacterName { get; set; }
		public string Designation { get; set; }
		public string Faction { get; set; }
		public string Rank { get; set; }
		public string Role { get; set; }
		public int Ping { get; set; }
		public bool IsHost { get; set; }
		public bool Jailed { get; set; }
		public bool Frozen { get; set; }
		public bool Muted { get; set; }
		public bool Gagged { get; set; }
		public List<string> Recent { get; set; } = new();
		public List<PanelCharacter> Characters { get; set; } = new();
		public List<string> Whitelist { get; set; } = new();
	}

	public sealed class PanelGrant
	{
		public string Faction { get; set; }
		public string Player { get; set; }
		public string By { get; set; }
		public string When { get; set; }
	}

	public sealed class PanelData
	{
		public string MyRole { get; set; }
		public bool OnDuty { get; set; }
		public List<PanelPlayer> Players { get; set; } = new();
		public List<Report> Reports { get; set; } = new();
		public List<string> Log { get; set; } = new();
		public List<PanelGrant> Grants { get; set; } = new();
		public List<string> Factions { get; set; } = new();

		/// <summary> "id|name|commander|xo|jedi" per battalion. </summary>
		public List<string> Battalions { get; set; } = new();

		/// <summary> Senior Admins only: SwrpPermissions.Rows(). </summary>
		public List<string> Perms { get; set; } = new();
	}

	/// <summary> Client: the last panel data the host sent. </summary>
	public static PanelData Panel { get; private set; }
	public static int PanelVersion { get; private set; }

	/// <summary> Client: ask the host for fresh panel data (staff only). </summary>
	[Rpc.Host]
	public static void RequestPanel()
	{
		var caller = Rpc.Caller;
		if ( caller is null || !Whitelist.IsTrialStaff( caller ) ) return;

		var admin = Whitelist.IsAdmin( caller );
		var data = new PanelData
		{
			MyRole = Whitelist.GetRole( caller ).ToString(),
			OnDuty = OnDuty( caller.SteamId.ValueUnsigned ),
			Reports = _reports.Where( r => !r.Closed || DateTime.UtcNow - r.When < TimeSpan.FromHours( 1 ) ).TakeLast( 30 ).ToList(),
			Log = Entries.TakeLast( 40 ).Reverse().Select( e => $"{e.When:MM-dd HH:mm}  {e.By}  {e.Action}  {e.Target} {e.Details}".TrimEnd() ).ToList(),
			Factions = FactionRegistry.All.Select( f => f.Id ).ToList(),
			Battalions = SwrpBattalions.PostSummary(),
		};

		var players = SwrpGameManager.Current?.Scene.GetAllComponents<SwrpPlayer>().ToList() ?? new();
		foreach ( var c in Connection.All )
		{
			var sid = c.SteamId.ValueUnsigned;
			var p = players.FirstOrDefault( x => x.Network.OwnerId == c.Id );
			var row = new PanelPlayer
			{
				SteamId = sid,
				Name = c.DisplayName,
				CharacterName = p?.CharacterName,
				Designation = p?.Faction.DesignationFor( p.Designation ),
				Faction = p?.Faction.Name,
				Rank = p?.Rank.Name,
				Role = c.IsHost ? "Host" : Whitelist.GetRole( c ).ToString(),
				Ping = (int)c.Ping,
				IsHost = c.IsHost,
				Jailed = p?.Jailed ?? false,
				Frozen = p?.Frozen ?? false,
				Muted = SwrpStaff.IsMuted( sid, out _ ),
				Gagged = SwrpStaff.IsGagged( sid ),
				Recent = Entries.Where( e => e.TargetSteamId == sid || e.BySteamId == sid ).TakeLast( 6 ).Reverse()
					.Select( e => $"{e.When:MM-dd HH:mm} {e.By} {e.Action} {e.Details}".TrimEnd() ).ToList(),
			};

			if ( admin )
			{
				row.Characters = CharacterStore.GetCharacters( sid ).Select( r => new PanelCharacter
				{
					Name = r.Name,
					Designation = r.Faction.DesignationFor( r.Designation ),
					Faction = r.Faction.Name,
					Rank = r.Rank.Name,
					Class = ( ClassRegistry.Get( r.ClassId ) ?? ClassRegistry.Default ).Name,
					Deployed = p?.CharacterId == r.Id,
				} ).ToList();
				row.Whitelist = Whitelist.AllowedFactions( sid ).Where( f => FactionRegistry.Get( f )?.Whitelisted == true ).ToList();
			}

			data.Players.Add( row );
		}

		if ( Whitelist.GetRole( caller ) >= StaffRole.SeniorAdmin ) data.Perms = SwrpPermissions.Rows();
		if ( admin ) data.Grants = Whitelist.Grants().Select( g => new PanelGrant { Faction = g.Faction, Player = g.Player, By = g.By, When = g.When } ).ToList();

		using ( Rpc.FilterInclude( caller ) ) ReceivePanel( Json.Serialize( data ) );
	}

	/// <summary> Client: /staff opens the panel (the host already checked the caller is staff). </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void OpenPanel()
	{
		SwrpUiState.StaffOpen = true;
		RequestPanel();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceivePanel( string json )
	{
		Panel = Json.Deserialize<PanelData>( json );
		PanelVersion++;
	}

	static (string First, string Tail) Split( string args ) => SwrpStaff.Split( args ?? "" );
}
