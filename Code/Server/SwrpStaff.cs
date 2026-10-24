using System;

namespace Swrp;

/// <summary>
/// Staff tools, typed in chat by Moderators and Admins. Everything runs on the host and is checked against the
/// caller's role; staff can only act on people below their own role (the host can act on anyone).
///
/// Moderator and up: /kick /ban (up to 1 day) /mute /unmute /gag /ungag /jail /unjail /freeze /unfreeze /slay
///                   /respawn /bring /goto /return /spectate
/// Admin:            permanent bans, /unban, /setname /setdesc /setfaction /setrank /promote /demote /setrole
///
/// Durations: 30s, 10m, 2h, 3d, or "perm". Bans, mutes and gags are saved in swrp/punishments.json.
/// </summary>
public static class SwrpStaff
{
	const string FilePath = "swrp/punishments.json";
	static readonly TimeSpan ModeratorMaxBan = TimeSpan.FromDays( 1 );

	public sealed class BanEntry
	{
		public ulong SteamId { get; set; }
		public string Name { get; set; }
		public string Reason { get; set; }
		public string By { get; set; }

		/// <summary> UTC ticks when the ban ends; 0 means permanent. </summary>
		public long UntilTicks { get; set; }
	}

	public sealed class PunishFile
	{
		public List<BanEntry> Bans { get; set; } = new();

		/// <summary> SteamID to UTC ticks the punishment ends (0 = until removed). </summary>
		public Dictionary<ulong, long> Muted { get; set; } = new();
		public Dictionary<ulong, long> Gagged { get; set; } = new();

		/// <summary> Brig sentences, so dying, switching character or reconnecting doesn't end one. </summary>
		public Dictionary<ulong, long> Jailed { get; set; } = new();
	}

	static PunishFile _data;

	static PunishFile Data
	{
		get
		{
			if ( _data is not null ) return _data;
			_data = FileSystem.Data.ReadJson<PunishFile>( FilePath, null ) ?? new PunishFile();
			return _data;
		}
	}

	static void Save()
	{
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( FilePath, _data );
		}
		catch ( Exception e )
		{
			Log.Error( $"[SWRP] Failed to save punishments: {e.Message}" );
		}
	}

	// Session-only state (host)
	static readonly Dictionary<ulong, (Vector3 Position, float Yaw)> _returnTo = new();
	static Dictionary<ulong, long> _jailUntil => Data.Jailed;
	static readonly Dictionary<ulong, RealTimeSince> _sinceCellCheck = new();
	static readonly HashSet<ulong> _frozen = new();

	public static void Reset()
	{
		_data = null;
		_returnTo.Clear();
		_sinceCellCheck.Clear();
		_frozen.Clear();
	}

	// ----- Queries used by the rest of the game -----

	public static bool IsBanned( ulong steamId, out string message )
	{
		message = null;
		Expire();
		var ban = Data.Bans.FirstOrDefault( b => b.SteamId == steamId );
		if ( ban is null ) return false;

		message = ban.UntilTicks == 0
			? $"You are permanently banned: {ban.Reason}"
			: $"You are banned for another {Remaining( ban.UntilTicks )}: {ban.Reason}";
		return true;
	}

	public static bool IsMuted( ulong steamId, out string remaining ) => Active( Data.Muted, steamId, out remaining );
	public static bool IsGagged( ulong steamId ) => Active( Data.Gagged, steamId, out _ );

	static bool Active( Dictionary<ulong, long> table, ulong steamId, out string remaining )
	{
		remaining = null;
		if ( !table.TryGetValue( steamId, out var until ) ) return false;
		if ( until != 0 && until <= DateTime.UtcNow.Ticks )
		{
			table.Remove( steamId );
			Save();
			return false;
		}

		if ( until != 0 ) remaining = Remaining( until );
		return true;
	}

	/// <summary> Host, every frame for each player: keep its synced staff flags in line with the punishment state. </summary>
	public static void TickPlayer( SwrpPlayer p )
	{
		{
			var id = p.OwnerSteamId;
			if ( id == 0 ) return;

			var frozen = _frozen.Contains( id );
			if ( p.Frozen != frozen ) p.Frozen = frozen;

			var gagged = IsGagged( id );
			if ( p.Gagged != gagged ) p.Gagged = gagged;

			var jailed = _jailUntil.TryGetValue( id, out var until );
			if ( jailed && until != 0 && until <= DateTime.UtcNow.Ticks )
			{
				Release( p );
				jailed = false;
			}
			if ( p.Jailed != jailed ) p.Jailed = jailed;
			if ( jailed ) KeepInCell( p );
		}
	}

	/// <summary>
	/// Host: a prisoner who is nowhere near a cell (they respawned at the medbay, switched character or came back after
	/// leaving) goes back to one. Checked every few seconds; the owner's position lags a teleport, hence the gap.
	/// </summary>
	static void KeepInCell( SwrpPlayer p )
	{
		if ( p.CharacterId is null ) return;
		var id = p.OwnerSteamId;
		if ( _sinceCellCheck.TryGetValue( id, out var since ) && since < 3f ) return;
		_sinceCellCheck[id] = 0;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: true } ) return;
		var cells = p.Scene.GetAllComponents<SwrpJailPoint>().ToList();
		if ( cells.Count == 0 || cells.Any( c => c.WorldPosition.Distance( p.WorldPosition ) < 420f ) ) return;
		var cell = PickJailCell( p.Scene );
		p.GetComponent<SwrpBlaster>()?.ForceHolster();
		p.TeleportTo( cell.WorldPosition, cell.WorldRotation.Yaw() );
		if ( p.Network.Owner is { } owner )
			SwrpNet.NotifyConnection( owner, _jailUntil.TryGetValue( id, out var until ) && until != 0 ? $"You are still in the brig for {Remaining( until )}." : "You are still in the brig.", true );
	}

	static void Expire()
	{
		var now = DateTime.UtcNow.Ticks;
		if ( Data.Bans.RemoveAll( b => b.UntilTicks != 0 && b.UntilTicks <= now ) > 0 ) Save();
	}

	// ----- Command dispatch -----

	/// <summary> Host. Returns true if <paramref name="cmd"/> was a staff command (handled or refused). </summary>
	public static bool TryRun( Connection caller, string cmd, string args )
	{
		if ( !Commands.TryGetValue( cmd, out var command ) ) return false;

		var role = Whitelist.GetRole( caller );
		var need = SwrpPermissions.Need( cmd, command.Role );
		if ( role < need )
		{
			SwrpChat.SystemTo( caller, $"{cmd} needs {need} or higher." );
			return true;
		}

		if ( !SwrpStaffTools.CheckRate( caller, cmd ) ) return true;

		try
		{
			command.Run( caller, args ?? "" );
			if ( cmd != "/ranks" ) SwrpStaffTools.Record( caller, cmd, args ?? "" );
		}
		catch ( Exception e )
		{
			SwrpChat.SystemTo( caller, $"{cmd} failed: {e.Message}" );
			Log.Warning( e );
		}

		return true;
	}

	/// <summary> Each command's role in the code table (the default the permissions grid starts from). </summary>
	public static IEnumerable<KeyValuePair<string, StaffRole>> DefaultRoles() => Commands.Select( c => KeyValuePair.Create( c.Key, c.Value.Role ) );

	public static string HelpFor( StaffRole role )
	{
		var cmds = Commands.Where( c => SwrpPermissions.Need( c.Key, c.Value.Role ) <= role ).Select( c => $"{c.Key} {c.Value.Usage}" );
		return "Staff: " + string.Join( " | ", cmds ) + "\n" + SwrpStaffTools.HelpLine;
	}

	sealed record Command( StaffRole Role, string Usage, Action<Connection, string> Run );

	static readonly Dictionary<string, Command> Commands = new()
	{
		["/kick"] = new( StaffRole.TrialModerator, "<player> [reason]", Kick ),
		["/ban"] = new( StaffRole.Moderator, "<player|steamid> <30m|2h|3d|perm> [reason]", Ban ),
		["/unban"] = new( StaffRole.Admin, "<steamid|name>", Unban ),
		["/mute"] = new( StaffRole.TrialModerator, "<player> [duration]", ( c, a ) => SetFlag( c, a, Data.Muted, "muted", true ) ),
		["/unmute"] = new( StaffRole.TrialModerator, "<player>", ( c, a ) => SetFlag( c, a, Data.Muted, "unmuted", false ) ),
		["/gag"] = new( StaffRole.TrialModerator, "<player> [duration]", ( c, a ) => SetFlag( c, a, Data.Gagged, "gagged", true ) ),
		["/ungag"] = new( StaffRole.TrialModerator, "<player>", ( c, a ) => SetFlag( c, a, Data.Gagged, "ungagged", false ) ),
		["/jail"] = new( StaffRole.Moderator, "<player> [duration]", Jail ),
		["/unjail"] = new( StaffRole.Moderator, "<player>", Unjail ),
		["/freeze"] = new( StaffRole.TrialModerator, "<player>", ( c, a ) => Freeze( c, a, true ) ),
		["/unfreeze"] = new( StaffRole.TrialModerator, "<player>", ( c, a ) => Freeze( c, a, false ) ),
		["/slay"] = new( StaffRole.Moderator, "<player>", Slay ),
		["/respawn"] = new( StaffRole.Moderator, "<player>", Respawn ),
		["/bring"] = new( StaffRole.TrialModerator, "<player>", Bring ),
		["/goto"] = new( StaffRole.TrialModerator, "<player>", Goto ),
		["/return"] = new( StaffRole.TrialModerator, "[player]", Return ),
		["/spectate"] = new( StaffRole.TrialModerator, "[player] (no name stops)", Spectate ),
		["/setname"] = new( StaffRole.Admin, "<player> <rp name>", SetName ),
		["/setdesc"] = new( StaffRole.Admin, "<player> <description>", SetDescription ),
		["/setfaction"] = new( StaffRole.Admin, "<player> <faction id>", SetFaction ),
		["/setrank"] = new( StaffRole.Admin, "<player> <rank # or name>", SetRank ),
		["/promote"] = new( StaffRole.Admin, "<player>", ( c, a ) => StepRank( c, a, +1 ) ),
		["/demote"] = new( StaffRole.Admin, "<player>", ( c, a ) => StepRank( c, a, -1 ) ),
		["/give"] = new( StaffRole.Admin, "<player> <item id> [count]", Give ),
		["/setrole"] = new( StaffRole.Admin, "<player|steamid> <user|trial|moderator|admin|senior>", SetRole ),
		["/perm"] = new( StaffRole.SeniorAdmin, "<command> <trial|moderator|admin|senior|default>", Perm ),
		["/ranks"] = new( StaffRole.TrialModerator, "", ListRanks ),
	};

	// ----- Helpers -----

	internal static (string First, string Tail) Split( string args )
	{
		args = args.Trim();
		// "quoted names" may have spaces in them
		if ( args.StartsWith( '"' ) && args.IndexOf( '"', 1 ) is var q and > 0 )
			return (args[1..q], args[(q + 1)..].Trim());
		var i = args.IndexOf( ' ' );
		return i < 0 ? (args, "") : (args[..i], args[(i + 1)..].Trim());
	}

	/// <summary> Resolves an online player the caller is allowed to act on, or tells them why not. </summary>
	static Connection Target( Connection caller, string name, bool allowSelf = false )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
		{
			SwrpChat.SystemTo( caller, "Name a player (part of their Steam name or character name, or a SteamID)." );
			return null;
		}

		var target = SwrpNet.FindConnection( name, out var problem );
		if ( target is null )
		{
			SwrpChat.SystemTo( caller, problem ?? $"No online player matches '{name}'." );
			return null;
		}

		if ( target == caller ) return allowSelf ? target : Refuse( caller, "You can't do that to yourself." );
		if ( target.IsHost ) return Refuse( caller, $"{target.DisplayName} is the host; staff actions can't target the host." );
		if ( !caller.IsHost && Whitelist.GetRole( target ) >= Whitelist.GetRole( caller ) )
			return Refuse( caller, $"{target.DisplayName} is {Whitelist.GetRole( target )}; you can only act on people below your role." );

		return target;
	}

	static Connection Refuse( Connection caller, string why )
	{
		SwrpChat.SystemTo( caller, why );
		return null;
	}

	static SwrpPlayer PlayerOf( Connection c ) => c is null ? null : SwrpGameManager.Current?.FindPlayer( c );

	/// <summary> "30s", "10m", "2h", "3d", "perm"/"0" -> null for permanent. Throws on nonsense. </summary>
	static TimeSpan? ParseDuration( string text )
	{
		text = (text ?? "").Trim().ToLowerInvariant();
		if ( text is "" or "perm" or "permanent" or "0" ) return null;

		var unit = text[^1];
		if ( !double.TryParse( char.IsDigit( unit ) ? text : text[..^1], out var n ) || n <= 0 )
			throw new Exception( $"'{text}' isn't a duration. Use 30s, 10m, 2h, 3d or perm." );

		return unit switch
		{
			's' => TimeSpan.FromSeconds( n ),
			'h' => TimeSpan.FromHours( n ),
			'd' => TimeSpan.FromDays( n ),
			_ => TimeSpan.FromMinutes( n ),
		};
	}

	static string Remaining( long untilTicks )
	{
		var left = TimeSpan.FromTicks( Math.Max( 0, untilTicks - DateTime.UtcNow.Ticks ) );
		if ( left.TotalDays >= 1 ) return $"{(int)left.TotalDays}d {left.Hours}h";
		if ( left.TotalHours >= 1 ) return $"{(int)left.TotalHours}h {left.Minutes}m";
		if ( left.TotalMinutes >= 1 ) return $"{(int)left.TotalMinutes}m";
		return $"{Math.Max( 1, (int)left.TotalSeconds )}s";
	}

	static string Describe( TimeSpan? d ) => d is null ? "permanently" : $"for {Remaining( DateTime.UtcNow.Ticks + d.Value.Ticks )}";

	internal static void Remember( SwrpPlayer p )
	{
		if ( p.IsValid() && p.Controller.IsValid() )
			_returnTo[p.OwnerSteamId] = (p.WorldPosition, p.Controller.EyeAngles.yaw);
	}

	// ----- Commands -----

	static void Kick( Connection caller, string args )
	{
		var (name, reason) = Split( args );
		if ( Target( caller, name ) is not { } t ) return;

		reason = string.IsNullOrEmpty( reason ) ? "Kicked by staff" : reason;
		SwrpChat.StaffLog( $"{caller.DisplayName} kicked {t.DisplayName} ({reason})" );
		t.Kick( reason );
	}

	static void Ban( Connection caller, string args )
	{
		var (name, rest) = Split( args );
		var (durText, reason) = Split( rest );
		var duration = ParseDuration( durText );
		reason = string.IsNullOrEmpty( reason ) ? "Banned by staff" : reason;

		if ( Whitelist.GetRole( caller ) < StaffRole.Admin && (duration is null || duration > ModeratorMaxBan) )
		{
			SwrpChat.SystemTo( caller, "Moderators can ban for up to 1 day. Ask an admin for longer or permanent bans." );
			return;
		}

		ulong steamId;
		string label;
		Connection online = null;

		if ( ulong.TryParse( name, out var raw ) && SwrpNet.FindConnection( name ) is null )
		{
			steamId = raw;
			label = name;
			if ( Connection.All.FirstOrDefault( c => c.IsHost )?.SteamId.ValueUnsigned == steamId )
			{
				SwrpChat.SystemTo( caller, "Staff actions can't target the host." );
				return;
			}
			if ( !caller.IsHost && Whitelist.GetRole( steamId ) >= Whitelist.GetRole( caller ) )
			{
				SwrpChat.SystemTo( caller, "You can only ban people below your role." );
				return;
			}
		}
		else
		{
			if ( Target( caller, name ) is not { } t ) return;
			steamId = t.SteamId.ValueUnsigned;
			label = t.DisplayName;
			online = t;
		}

		Data.Bans.RemoveAll( b => b.SteamId == steamId );
		Data.Bans.Add( new BanEntry
		{
			SteamId = steamId,
			Name = label,
			Reason = reason,
			By = caller.DisplayName,
			UntilTicks = duration is null ? 0 : DateTime.UtcNow.Ticks + duration.Value.Ticks,
		} );
		Save();

		SwrpChat.StaffLog( $"{caller.DisplayName} banned {label} {Describe( duration )} ({reason})" );
		online?.Kick( duration is null ? $"Permanently banned: {reason}" : $"Banned {Describe( duration )}: {reason}" );
	}

	static void Unban( Connection caller, string args )
	{
		var key = args.Trim();
		var removed = Data.Bans.RemoveAll( b => b.SteamId.ToString() == key || (b.Name?.Contains( key, StringComparison.OrdinalIgnoreCase ) ?? false) );
		if ( removed == 0 )
		{
			SwrpChat.SystemTo( caller, $"No ban matches '{key}'." );
			return;
		}

		Save();
		SwrpChat.StaffLog( $"{caller.DisplayName} unbanned {key}" );
	}

	static void SetFlag( Connection caller, string args, Dictionary<ulong, long> table, string verb, bool on )
	{
		var (name, durText) = Split( args );
		if ( Target( caller, name ) is not { } t ) return;
		var id = t.SteamId.ValueUnsigned;

		if ( on )
		{
			var duration = ParseDuration( durText );
			table[id] = duration is null ? 0 : DateTime.UtcNow.Ticks + duration.Value.Ticks;
			SwrpChat.StaffLog( $"{caller.DisplayName} {verb} {t.DisplayName} {Describe( duration )}" );
		}
		else
		{
			table.Remove( id );
			SwrpChat.StaffLog( $"{caller.DisplayName} {verb} {t.DisplayName}" );
		}

		Save();
		SwrpNet.NotifyConnection( t, $"You have been {verb} by staff.", on );
	}

	static void Freeze( Connection caller, string args, bool on )
	{
		if ( Target( caller, Split( args ).First ) is not { } t ) return;
		var id = t.SteamId.ValueUnsigned;
		if ( on ) _frozen.Add( id ); else _frozen.Remove( id );

		SwrpChat.StaffLog( $"{caller.DisplayName} {(on ? "froze" : "unfroze")} {t.DisplayName}" );
		SwrpNet.NotifyConnection( t, on ? "You have been frozen by staff." : "You can move again.", on );
	}

	static void Jail( Connection caller, string args )
	{
		var (name, durText) = Split( args );
		if ( Target( caller, name ) is not { } t ) return;
		if ( PlayerOf( t ) is not { } p )
		{
			SwrpChat.SystemTo( caller, $"{t.DisplayName} hasn't deployed a character." );
			return;
		}

		var cell = PickJailCell( p.Scene );
		if ( cell is null )
		{
			SwrpChat.SystemTo( caller, "This map has no jail cells (SwrpJailPoint)." );
			return;
		}

		var duration = ParseDuration( durText );
		Remember( p );
		_jailUntil[p.OwnerSteamId] = duration is null ? 0 : DateTime.UtcNow.Ticks + duration.Value.Ticks;
		Save();
		p.Jailed = true;
		p.GetComponent<SwrpBlaster>()?.ForceHolster();
		p.TeleportTo( cell.WorldPosition, cell.WorldRotation.Yaw() );

		SwrpChat.StaffLog( $"{caller.DisplayName} jailed {t.DisplayName} {Describe( duration )}" );
		SwrpNet.NotifyConnection( t, $"You have been jailed {Describe( duration )}.", true );
	}

	static void Unjail( Connection caller, string args )
	{
		if ( Target( caller, Split( args ).First ) is not { } t ) return;
		if ( PlayerOf( t ) is not { } p || !_jailUntil.ContainsKey( p.OwnerSteamId ) )
		{
			SwrpChat.SystemTo( caller, $"{t.DisplayName} isn't jailed." );
			return;
		}

		Release( p );
		SwrpChat.StaffLog( $"{caller.DisplayName} released {t.DisplayName} from jail" );
	}

	static void Release( SwrpPlayer p )
	{
		if ( _jailUntil.Remove( p.OwnerSteamId ) ) Save();
		p.Jailed = false;

		if ( _returnTo.Remove( p.OwnerSteamId, out var back ) )
			p.TeleportTo( back.Position, back.Yaw );
		else if ( CharacterStore.Find( p.OwnerSteamId, p.CharacterId ) is { } record && p.Network.Owner is { } conn )
			SwrpGameManager.Current?.SpawnCharacter( conn, record, force: true );     // jailed in an earlier session: out of the cell to a spawn

		if ( p.Network.Owner is { } owner )
			SwrpNet.NotifyConnection( owner, "You have been released from jail.", false );
	}

	static SwrpJailPoint PickJailCell( Scene scene )
	{
		var cells = scene.GetAllComponents<SwrpJailPoint>().ToList();
		if ( cells.Count == 0 ) return null;

		// Prefer an empty cell.
		var players = scene.GetAllComponents<SwrpPlayer>().ToList();
		var free = cells.Where( c => !players.Any( p => p.WorldPosition.Distance( c.WorldPosition ) < 60f ) ).ToList();
		var pool = free.Count > 0 ? free : cells;
		return pool[Game.Random.Next( pool.Count )];
	}

	static void Slay( Connection caller, string args )
	{
		if ( Target( caller, Split( args ).First ) is not { } t ) return;
		if ( PlayerOf( t )?.GetComponent<SwrpVitals>() is not { } vitals ) { SwrpChat.SystemTo( caller, $"{t.DisplayName} isn't deployed." ); return; }

		vitals.TakeDamage( 100000f );
		SwrpChat.StaffLog( $"{caller.DisplayName} slayed {t.DisplayName}" );
	}

	static void Respawn( Connection caller, string args )
	{
		if ( Target( caller, Split( args ).First, allowSelf: true ) is not { } t ) return;
		var p = PlayerOf( t );
		var record = p is null ? null : CharacterStore.Find( p.OwnerSteamId, p.CharacterId );
		if ( record is null ) { SwrpChat.SystemTo( caller, $"{t.DisplayName} isn't deployed." ); return; }

		if ( _jailUntil.Remove( p.OwnerSteamId ) ) Save();
		SwrpGameManager.Current?.SpawnCharacter( t, record, force: true );
		SwrpChat.StaffLog( $"{caller.DisplayName} respawned {t.DisplayName}" );
	}

	static void Bring( Connection caller, string args )
	{
		if ( Target( caller, Split( args ).First ) is not { } t ) return;
		var me = PlayerOf( caller );
		var them = PlayerOf( t );
		if ( me is null || them is null ) { SwrpChat.SystemTo( caller, "You both need to be deployed." ); return; }

		Remember( them );
		var forward = me.Controller.EyeAngles.WithPitch( 0 ).ToRotation().Forward;
		them.TeleportTo( me.WorldPosition + forward * 64f + Vector3.Up * 4f, me.Controller.EyeAngles.yaw + 180f );
		SwrpChat.StaffLog( $"{caller.DisplayName} brought {t.DisplayName}" );
	}

	static void Goto( Connection caller, string args )
	{
		if ( Target( caller, Split( args ).First, allowSelf: false ) is not { } t ) return;
		var me = PlayerOf( caller );
		var them = PlayerOf( t );
		if ( me is null || them is null ) { SwrpChat.SystemTo( caller, "You both need to be deployed." ); return; }

		Remember( me );
		var behind = them.Controller.EyeAngles.WithPitch( 0 ).ToRotation().Backward;
		me.TeleportTo( them.WorldPosition + behind * 64f + Vector3.Up * 4f, them.Controller.EyeAngles.yaw );
		SwrpChat.StaffLog( $"{caller.DisplayName} went to {t.DisplayName}" );
	}

	static void Return( Connection caller, string args )
	{
		var name = Split( args ).First;
		var t = string.IsNullOrEmpty( name ) ? caller : Target( caller, name, allowSelf: true );
		if ( t is null || PlayerOf( t ) is not { } p ) return;

		if ( !_returnTo.Remove( p.OwnerSteamId, out var back ) )
		{
			SwrpChat.SystemTo( caller, $"No saved position for {t.DisplayName}." );
			return;
		}

		p.TeleportTo( back.Position, back.Yaw );
		SwrpChat.StaffLog( $"{caller.DisplayName} returned {t.DisplayName}" );
	}

	static void Spectate( Connection caller, string args )
	{
		var me = PlayerOf( caller );
		if ( me is null ) { SwrpChat.SystemTo( caller, "Deploy a character first." ); return; }

		var name = Split( args ).First;
		if ( string.IsNullOrEmpty( name ) || name is "off" or "stop" )
		{
			me.SetSpectate( 0 );
			return;
		}

		if ( Target( caller, name ) is not { } t || PlayerOf( t ) is not { } them ) return;
		me.SetSpectate( them.OwnerSteamId );
		SwrpChat.SystemTo( caller, $"Spectating {t.DisplayName}. Press Jump or Use, or type /spectate, to stop." );
	}

	// ----- Character administration -----

	/// <summary> Online target's active character record, or null with a message to the caller. </summary>
	static (SwrpPlayer Player, CharacterRecord Record, Connection Who) Character( Connection caller, string name )
	{
		if ( Target( caller, name, allowSelf: true ) is not { } t ) return default;
		var p = PlayerOf( t );
		var record = p is null ? null : CharacterStore.Find( p.OwnerSteamId, p.CharacterId );
		if ( record is null )
		{
			SwrpChat.SystemTo( caller, $"{t.DisplayName} doesn't have a character deployed." );
			return default;
		}

		return (p, record, t);
	}

	static void Commit( (SwrpPlayer Player, CharacterRecord Record, Connection Who) c, Connection caller, string what )
	{
		CharacterStore.Save( c.Record.OwnerSteamId );
		c.Player.RefreshFromStore();
		SwrpChat.StaffLog( $"{caller.DisplayName} {what}" );
		SwrpNet.NotifyConnection( c.Who, $"Staff updated your character: {what.Replace( c.Who.DisplayName + " ", "" )}", false );
		SwrpNet.SendMenuState( c.Who );
	}

	static void Give( Connection caller, string args )
	{
		var (name, rest) = Split( args );
		var (itemId, countText) = Split( rest );
		itemId = itemId.Trim().ToLowerInvariant();
		var def = ItemRegistry.Get( itemId );
		if ( def is null )
		{
			SwrpChat.SystemTo( caller, $"No item '{itemId}'. Items: {string.Join( ", ", ItemRegistry.All.Select( i => i.Id ) )}" );
			return;
		}

		var count = int.TryParse( countText.Trim(), out var n ) ? Math.Clamp( n, 1, 500 ) : 1;
		var c = Character( caller, name );
		if ( c.Record is null ) return;

		var left = c.Record.AddItem( itemId, count );
		if ( left == count )
		{
			SwrpChat.SystemTo( caller, $"{c.Record.Name}'s inventory is full." );
			return;
		}

		Commit( c, caller, $"gave {c.Who.DisplayName} {count - left}x {def.Name}" );
	}

	static void SetName( Connection caller, string args )
	{
		var (name, value) = Split( args );
		value = value.Trim();
		if ( value.Length < 2 || value.Length > 24 || !value.All( ch => char.IsLetter( ch ) || ch is ' ' or '-' or '\'' ) )
		{
			SwrpChat.SystemTo( caller, "Names are 2 to 24 letters (spaces, hyphens and apostrophes allowed)." );
			return;
		}

		var c = Character( caller, name );
		if ( c.Record is null ) return;
		var old = c.Record.Name;
		c.Record.Name = value;
		Commit( c, caller, $"renamed {old} to {value}" );
	}

	static void SetDescription( Connection caller, string args )
	{
		var (name, value) = Split( args );
		var c = Character( caller, name );
		if ( c.Record is null ) return;

		c.Record.Description = value.Length > 300 ? value[..300] : value;
		Commit( c, caller, $"changed {c.Record.Name}'s description" );
	}

	static void SetFaction( Connection caller, string args )
	{
		var (name, value) = Split( args );
		var faction = FactionRegistry.Get( value.Trim().ToLowerInvariant() )
			?? FactionRegistry.All.FirstOrDefault( f => f.ShortName.Equals( value.Trim(), StringComparison.OrdinalIgnoreCase ) || f.Name.Equals( value.Trim(), StringComparison.OrdinalIgnoreCase ) );
		if ( faction is null )
		{
			SwrpChat.SystemTo( caller, $"Unknown faction. Valid: {string.Join( ", ", FactionRegistry.All.Select( f => f.Id ) )}" );
			return;
		}

		var c = Character( caller, name );
		if ( c.Record is null ) return;
		c.Record.FactionId = faction.Id;
		c.Record.RankIndex = Math.Clamp( c.Record.RankIndex, 0, faction.Ranks.Count - 1 );
		Commit( c, caller, $"moved {c.Record.Name} to the {faction.Name}" );
	}

	static void SetRank( Connection caller, string args )
	{
		var (name, value) = Split( args );
		var c = Character( caller, name );
		if ( c.Record is null ) return;

		var ranks = c.Record.Faction.Ranks;
		var index = FactionRegistry.FindRank( ranks, value );
		if ( index < 0 )
		{
			SwrpChat.SystemTo( caller, "Unknown rank. Type /ranks for the ladder (number, name or abbreviation)." );
			return;
		}

		SwrpRanks.Record( $"[staff] {caller.DisplayName}", caller.SteamId.ValueUnsigned, c.Record, c.Record.RankIndex, index, "set rank of" );
		c.Record.RankIndex = index;
		Commit( c, caller, $"set {c.Record.Name}'s rank to {ranks[index].Name}" );
	}

	static void StepRank( Connection caller, string args, int step )
	{
		var c = Character( caller, Split( args ).First );
		if ( c.Record is null ) return;

		var ranks = c.Record.Faction.Ranks;
		var index = Math.Clamp( c.Record.RankIndex + step, 0, ranks.Count - 1 );
		if ( index == c.Record.RankIndex )
		{
			SwrpChat.SystemTo( caller, $"{c.Record.Name} is already {ranks[index].Name}." );
			return;
		}

		SwrpRanks.Record( $"[staff] {caller.DisplayName}", caller.SteamId.ValueUnsigned, c.Record, c.Record.RankIndex, index, step > 0 ? "promoted" : "demoted" );
		c.Record.RankIndex = index;
		Commit( c, caller, $"{(step > 0 ? "promoted" : "demoted")} {c.Record.Name} to {ranks[index].Name}" );
	}

	static void ListRanks( Connection caller, string args )
	{
		var ranks = FactionRegistry.CloneRanks;
		SwrpChat.SystemTo( caller, "Ranks: " + string.Join( "  ", ranks.Select( ( r, i ) => $"{i + 1}. {r.Name} ({r.Abbreviation})" ) ) );
		SwrpChat.SystemTo( caller, "Jedi Order: " + string.Join( "  ", FactionRegistry.JediRanks.Select( ( r, i ) => $"{i + 1}. {r.Name} ({r.Abbreviation})" ) ) );
	}

	static void SetRole( Connection caller, string args )
	{
		var (name, value) = Split( args );
		if ( !Whitelist.TryParseRole( value, out var role ) )
		{
			SwrpChat.SystemTo( caller, "Role must be user, trial, moderator, admin or senior." );
			return;
		}

		if ( !SwrpNet.TryResolveSteamId( name, out var steamId, out var label ) )
		{
			SwrpChat.SystemTo( caller, $"No player or SteamID matches '{name}'." );
			return;
		}

		if ( !caller.IsHost && steamId == caller.SteamId.ValueUnsigned )
		{
			SwrpChat.SystemTo( caller, "You can't change your own role." );
			return;
		}

		var target = Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == steamId );
		var mine = Whitelist.GetRole( caller );
		var theirs = target is not null ? Whitelist.GetRole( target ) : Whitelist.GetRole( steamId );
		if ( !caller.IsHost && ( role >= mine || theirs >= mine || target?.IsHost == true ) )
		{
			SwrpChat.SystemTo( caller, $"You can only give roles below yours ({mine}) to people below yours." );
			return;
		}

		Whitelist.SetRole( steamId, role );
		SwrpChat.StaffLog( $"{caller.DisplayName} made {label} a {role}" );
		if ( target is { } online )
			SwrpNet.NotifyConnection( online, $"Your staff role is now {role}.", false );
	}

	/// <summary> /perm &lt;command&gt; &lt;role|default&gt;: the minimum role for one staff command (SwrpPermissions). </summary>
	static void Perm( Connection caller, string args )
	{
		var (cmd, value) = Split( args );
		cmd = cmd.Trim().ToLowerInvariant();
		if ( cmd.Length > 0 && cmd[0] != '/' ) cmd = "/" + cmd;
		if ( !SwrpPermissions.Rows().Any( r => r.StartsWith( cmd + "|" ) ) )
		{
			SwrpChat.SystemTo( caller, $"'{cmd}' isn't a staff command that can be changed." );
			return;
		}

		StaffRole? role = null;
		if ( !value.Trim().Equals( "default", StringComparison.OrdinalIgnoreCase ) )
		{
			if ( !Whitelist.TryParseRole( value, out var r ) || r == StaffRole.User )
			{
				SwrpChat.SystemTo( caller, "Role must be trial, moderator, admin, senior or default." );
				return;
			}
			role = r;
		}

		SwrpPermissions.Set( cmd, role );
		SwrpChat.StaffLog( $"{caller.DisplayName} set {cmd} to {role?.ToString() ?? "its default"}" );
	}
}

/// <summary>
/// A cell the /jail command can put people in.
/// </summary>
[Title( "SWRP Jail Point" ), Category( "SWRP" ), Icon( "lock" )]
public sealed class SwrpJailPoint : Component
{
	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Red;
		Gizmo.Draw.LineBBox( new BBox( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 72 ) ) );
	}
}
