using System;
using System.Text.Json.Serialization;

namespace Swrp;

// ---------------------------------------------------------------------------------------------------------------------
// Shared roleplay state: squads, platoons, the active order and the recent order log. The host owns it and publishes it
// as one JSON string that only changes when something happens; clients parse it once per change.
// ---------------------------------------------------------------------------------------------------------------------

public sealed class SquadInfo
{
	public int Id { get; set; }
	public string Name { get; set; }
	public string ColorHex { get; set; } = "#5fb4ff";
	public ulong Leader { get; set; }
	public List<ulong> Members { get; set; } = new();
	public int PlatoonId { get; set; }

	/// <summary> SteamID (as text) to the member's last known "RANK Name", so the squad menu can name members who are offline. </summary>
	public Dictionary<string, string> Names { get; set; } = new();

	/// <summary> The squad's rally point, set by its leader from the squad menu (a marker on every member's HUD). </summary>
	public bool HasRally { get; set; }
	public float RallyX { get; set; }
	public float RallyY { get; set; }
	public float RallyZ { get; set; }

	[JsonIgnore] public Vector3 Rally => new( RallyX, RallyY, RallyZ );

	public string NameOf( ulong steamId ) => Names.TryGetValue( steamId.ToString(), out var n ) ? n : "Trooper";
}

/// <summary> A squad invite waiting for an answer (published so the squad menu can show accept and decline). </summary>
public sealed class InviteInfo
{
	public ulong SteamId { get; set; }
	public int SquadId { get; set; }
	public string From { get; set; }
}

public sealed class PlatoonInfo
{
	public int Id { get; set; }
	public string Name { get; set; }
	public ulong Commander { get; set; }
	public string CommanderName { get; set; }
}

public sealed class OrderInfo
{
	public int Id { get; set; }
	public string Title { get; set; }
	public string Objective { get; set; }
	public string Rally { get; set; }
	public bool HasRallyPoint { get; set; }
	public float RallyX { get; set; }
	public float RallyY { get; set; }
	public float RallyZ { get; set; }
	public string Priority { get; set; } = "normal";
	public string Author { get; set; }
	public DateTime Posted { get; set; }
	/// <summary> active, complete, failed or cancelled. </summary>
	public string Status { get; set; } = "active";
	public string Note { get; set; }
	public DateTime? Closed { get; set; }

	[JsonIgnore] public Vector3 RallyPoint => new( RallyX, RallyY, RallyZ );
}

public sealed class RoleplayState
{
	public List<SquadInfo> Squads { get; set; } = new();
	public List<PlatoonInfo> Platoons { get; set; } = new();
	public OrderInfo Order { get; set; }
	/// <summary> The last few finished orders, newest first, for the mission board. </summary>
	public List<OrderInfo> RecentOrders { get; set; } = new();
	public List<InviteInfo> Invites { get; set; } = new();
}

/// <summary>
/// Squads, platoons, orders, radio channels and briefings. Lives on one networked object in the scene (added by the scene
/// builder, or created by the host if missing). Chat commands land here through <see cref="SwrpChat"/>.
/// </summary>
[Title( "SWRP Roleplay" ), Category( "SWRP" ), Icon( "groups" )]
public sealed class SwrpRoleplay : Component
{
	public const int MaxSquadSize = 8;
	public const int LieutenantRank = 6;   // 2nd Lieutenant: platoons, command channel, dispatch, briefings, rank log
	public const int CaptainRank = 8;      // Captain: orders, officer promotions
	public const string OrdersFile = "swrp/orders.json";

	public static readonly string[] Channels = { "squad", "legion", "command", "open", "pa" };
	static readonly string[] SquadColors = { "#5fb4ff", "#ffb547", "#7dffa1", "#ff6b6b", "#c9a4ff", "#ffe66b", "#6bfff0", "#ff9ad5" };

	/// <summary> Published by the host whenever squads, platoons or orders change. </summary>
	[Sync( SyncFlags.FromHost )] public string StateJson { get; set; }

	public static SwrpRoleplay Current => _current.IsValid() ? _current : _current = Game.ActiveScene?.GetAllComponents<SwrpRoleplay>().FirstOrDefault();
	static SwrpRoleplay _current;

	// ----- Client view -----

	string _parsedFrom;
	RoleplayState _parsed = new();
	public int Version { get; private set; }

	/// <summary> The current state, parsed once per change. </summary>
	public RoleplayState State
	{
		get
		{
			if ( !ReferenceEquals( _parsedFrom, StateJson ) )
			{
				_parsedFrom = StateJson;
				_parsed = string.IsNullOrEmpty( StateJson ) ? new RoleplayState() : Json.Deserialize<RoleplayState>( StateJson ) ?? new RoleplayState();
				Version++;
			}

			return _parsed;
		}
	}

	public SquadInfo SquadOf( ulong steamId )
	{
		var squads = State.Squads;
		for ( var i = 0; i < squads.Count; i++ )
			if ( squads[i].Members.Contains( steamId ) ) return squads[i];
		return null;
	}

	public PlatoonInfo PlatoonFor( SquadInfo squad )
	{
		if ( squad is null || squad.PlatoonId == 0 ) return null;
		var list = State.Platoons;
		for ( var i = 0; i < list.Count; i++ )
			if ( list[i].Id == squad.PlatoonId ) return list[i];
		return null;
	}

	// ----- Host state -----

	RoleplayState _host = new();
	int _nextId = 1;
	readonly Dictionary<ulong, int> _invites = new();
	readonly Dictionary<ulong, string> _inviteFrom = new();
	bool _loadedLog;

	protected override void OnStart()
	{
		_current = this;
		if ( Networking.IsHost ) LoadLog();
	}

	void LoadLog()
	{
		if ( _loadedLog ) return;
		_loadedLog = true;
		var log = FileSystem.Data.ReadJson<List<OrderInfo>>( OrdersFile, null ) ?? new();
		_host.RecentOrders = log.OrderByDescending( o => o.Posted ).Take( 8 ).ToList();
		_nextId = log.Count == 0 ? 1 : log.Max( o => o.Id ) + 1;

		// standing squads and platoons from the last session (swrp_squads_persist)
		if ( PersistSquads && FileSystem.Data.ReadJson<RoleplayState>( SquadsFile, null ) is { } saved )
		{
			_host.Squads = saved.Squads ?? new();
			_host.Platoons = saved.Platoons ?? new();
			var ids = _host.Squads.Select( s => s.Id ).Concat( _host.Platoons.Select( p => p.Id ) ).DefaultIfEmpty( 0 ).Max();
			_nextId = Math.Max( _nextId, ids + 1 );
		}
		Publish();
	}

	public const string SquadsFile = "swrp/squads.json";

	[ConVar( "swrp_squads_persist", Help = "Host: squads and platoons are saved and stay when their members log off (1, default), or last for the session (0)", Saved = true )]
	public static bool PersistSquads { get; set; } = true;

	string _savedSquads;

	void Publish()
	{
		// the squad menu names offline members and shows waiting invites
		foreach ( var squad in _host.Squads )
			foreach ( var id in squad.Members )
				if ( PlayerBySteam( Scene, id ) is { } p ) squad.Names[id.ToString()] = Short( p );
		foreach ( var squad in _host.Squads )
			foreach ( var gone in squad.Names.Keys.Where( k => !squad.Members.Any( m => m.ToString() == k ) ).ToList() )
				squad.Names.Remove( gone );
		_host.Invites = _invites.Where( kv => _host.Squads.Any( s => s.Id == kv.Value ) )
			.Select( kv => new InviteInfo { SteamId = kv.Key, SquadId = kv.Value, From = _inviteFrom.TryGetValue( kv.Key, out var f ) ? f : "" } ).ToList();

		StateJson = Json.Serialize( _host );
		if ( !PersistSquads || !Networking.IsHost ) return;
		// only the squads and platoons are saved, and only when they changed
		var squads = Json.Serialize( new RoleplayState { Squads = _host.Squads, Platoons = _host.Platoons } );
		if ( squads == _savedSquads ) return;
		_savedSquads = squads;
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( SquadsFile, new RoleplayState { Squads = _host.Squads, Platoons = _host.Platoons } );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not save squads: {e.Message}" );
		}
	}

	void AppendLog( OrderInfo order )
	{
		var log = FileSystem.Data.ReadJson<List<OrderInfo>>( OrdersFile, null ) ?? new();
		log.RemoveAll( o => o.Id == order.Id );
		log.Add( order );
		if ( log.Count > 300 ) log.RemoveRange( 0, log.Count - 300 );
		FileSystem.Data.CreateDirectory( "swrp" );
		FileSystem.Data.WriteJson( OrdersFile, log );
	}

	/// <summary> Host: someone left, so tidy their squad (standing squads keep their members while they're away). </summary>
	public void OnPlayerLeft( ulong steamId )
	{
		_invites.Remove( steamId );
		if ( PersistSquads ) return;
		var squad = _host.Squads.FirstOrDefault( s => s.Members.Contains( steamId ) );
		if ( squad is null ) return;
		RemoveMember( squad, steamId, null );
	}

	// ----- Helpers -----

	public static SwrpPlayer PlayerBySteam( Scene scene, ulong steamId )
		=> scene.GetAllComponents<SwrpPlayer>().FirstOrDefault( p => p.OwnerSteamId == steamId && p.CharacterId is not null );

	static string Short( SwrpPlayer p ) => p.IsValid() ? $"{p.Rank.Abbreviation} {p.CharacterName}" : "Unknown";

	SwrpPlayer FindTarget( Connection caller, string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) { SwrpChat.SystemTo( caller, "Name a player (part of their character or Steam name)." ); return null; }
		var conn = SwrpNet.FindConnection( name );
		var p = conn is not null ? SwrpGameManager.Current?.FindPlayer( conn )
			: Scene.GetAllComponents<SwrpPlayer>().FirstOrDefault( x => x.CharacterId is not null && x.CharacterName?.Contains( name.Trim(), StringComparison.OrdinalIgnoreCase ) == true );
		if ( !p.IsValid() ) SwrpChat.SystemTo( caller, $"No deployed trooper matches '{name}'." );
		return p;
	}

	IEnumerable<Connection> SquadConnections( SquadInfo squad )
	{
		foreach ( var id in squad.Members )
			if ( PlayerBySteam( Scene, id )?.Network.Owner is { } c ) yield return c;
	}

	void SquadNotice( SquadInfo squad, string text )
	{
		SwrpChat.SendTo( SquadConnections( squad ), ChatKind.Radio, $"[SQUAD | {squad.Name}] {text}" );
		using ( Rpc.FilterInclude( SquadConnections( squad ).ToList() ) ) PlayUi( SwrpAudio.SquadJoin );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayUi( string sound ) => SwrpAudio.Ui( sound );

	static (string First, string Tail) Split( string args )
	{
		args = ( args ?? "" ).Trim();
		var i = args.IndexOf( ' ' );
		return i < 0 ? (args, "") : (args[..i], args[(i + 1)..].Trim());
	}

	// ----- /squad -----

	public void SquadCommand( Connection caller, SwrpPlayer sender, string args )
	{
		var (sub, rest) = Split( args );
		var me = sender.OwnerSteamId;
		var mine = _host.Squads.FirstOrDefault( s => s.Members.Contains( me ) );

		switch ( sub.ToLowerInvariant() )
		{
			case "create":
			{
				if ( mine is not null ) { SwrpChat.SystemTo( caller, $"You are already in {mine.Name}. /squad leave first." ); return; }
				var name = rest.Length is >= 2 and <= 24 ? rest : $"Squad {_host.Squads.Count + 1}";
				if ( _host.Squads.Any( s => s.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) ) { SwrpChat.SystemTo( caller, "A squad with that name already exists." ); return; }
				var squad = new SquadInfo { Id = _nextId++, Name = name, Leader = me, ColorHex = SquadColors[_host.Squads.Count % SquadColors.Length] };
				squad.Members.Add( me );
				_host.Squads.Add( squad );
				_invites.Remove( me );
				TuneToSquad( sender, true );
				Publish();
				SquadNotice( squad, $"{Short( sender )} formed the squad. Your radio is on the squad net. Invite from the squad menu (/squad) or /squad invite <name>." );
				return;
			}

			case "invite":
			{
				if ( mine is null || mine.Leader != me ) { SwrpChat.SystemTo( caller, "Only a squad leader can invite." ); return; }
				if ( mine.Members.Count >= MaxSquadSize ) { SwrpChat.SystemTo( caller, $"Squads hold {MaxSquadSize} troopers." ); return; }
				var t = FindTarget( caller, rest );
				if ( t is null ) return;
				if ( _host.Squads.Any( s => s.Members.Contains( t.OwnerSteamId ) ) ) { SwrpChat.SystemTo( caller, $"{Short( t )} is already in a squad." ); return; }
				_invites[t.OwnerSteamId] = mine.Id;
				_inviteFrom[t.OwnerSteamId] = Short( sender );
				Publish();
				SwrpChat.SystemTo( caller, $"Invited {Short( t )} to {mine.Name}." );
				if ( t.Network.Owner is { } tc )
				{
					SwrpChat.SystemTo( tc, $"{Short( sender )} invited you to squad {mine.Name}. Accept in the squad menu (/squad or F3), or /squad accept." );
					using ( Rpc.FilterInclude( tc ) ) PlayUi( SwrpAudio.SquadJoin );
				}
				return;
			}

			case "accept":
			case "join":
			{
				if ( mine is not null ) { SwrpChat.SystemTo( caller, $"You are already in {mine.Name}." ); return; }
				if ( !_invites.Remove( me, out var squadId ) || _host.Squads.FirstOrDefault( s => s.Id == squadId ) is not { } squad )
				{ SwrpChat.SystemTo( caller, "You have no squad invite waiting." ); return; }
				_inviteFrom.Remove( me );
				if ( squad.Members.Count >= MaxSquadSize ) { SwrpChat.SystemTo( caller, $"{squad.Name} is full." ); Publish(); return; }
				squad.Members.Add( me );
				TuneToSquad( sender, true );
				Publish();
				SquadNotice( squad, $"{Short( sender )} joined the squad." );
				return;
			}

			case "decline":
				if ( !_invites.Remove( me ) ) { SwrpChat.SystemTo( caller, "You have no squad invite waiting." ); return; }
				_inviteFrom.Remove( me );
				Publish();
				SwrpChat.SystemTo( caller, "Invite declined." );
				return;

			case "rally":
			{
				if ( mine is null || mine.Leader != me ) { SwrpChat.SystemTo( caller, "Only the squad leader sets the rally point." ); return; }
				if ( rest.Trim() is "clear" or "off" )
				{
					mine.HasRally = false;
					Publish();
					SquadNotice( mine, $"{Short( sender )} cleared the rally point." );
					return;
				}
				if ( !SwrpGameMaster.Crosshair( caller, sender, out var at, out _ ) ) return;
				mine.HasRally = true;
				mine.RallyX = at.x;
				mine.RallyY = at.y;
				mine.RallyZ = at.z;
				Publish();
				SquadNotice( mine, $"{Short( sender )} set a rally point on the {SwrpShip.DeckName( at )}. Follow the marker." );
				return;
			}

			case "leave":
				if ( mine is null ) { SwrpChat.SystemTo( caller, "You are not in a squad." ); return; }
				RemoveMember( mine, me, $"{Short( sender )} left the squad." );
				TuneToSquad( sender, false );
				SwrpChat.SystemTo( caller, $"You left {mine.Name}." );
				return;

			case "kick":
			{
				if ( mine is null || mine.Leader != me ) { SwrpChat.SystemTo( caller, "Only the squad leader can kick." ); return; }
				var t = FindTarget( caller, rest );
				if ( t is null || !mine.Members.Contains( t.OwnerSteamId ) || t.OwnerSteamId == me ) { SwrpChat.SystemTo( caller, "They are not in your squad." ); return; }
				RemoveMember( mine, t.OwnerSteamId, $"{Short( t )} was removed from the squad." );
				TuneToSquad( t, false );
				if ( t.Network.Owner is { } tc ) SwrpChat.SystemTo( tc, $"You were removed from {mine.Name}." );
				return;
			}

			case "promote":
			case "leader":
			{
				if ( mine is null || mine.Leader != me ) { SwrpChat.SystemTo( caller, "Only the squad leader can hand over command." ); return; }
				var t = FindTarget( caller, rest );
				if ( t is null || !mine.Members.Contains( t.OwnerSteamId ) ) { SwrpChat.SystemTo( caller, "They are not in your squad." ); return; }
				mine.Leader = t.OwnerSteamId;
				Publish();
				SquadNotice( mine, $"{Short( t )} now leads the squad." );
				return;
			}

			case "disband":
				if ( mine is null || ( mine.Leader != me && !Whitelist.IsStaff( caller ) ) ) { SwrpChat.SystemTo( caller, "Only the squad leader can disband." ); return; }
				SquadNotice( mine, $"{Short( sender )} disbanded the squad." );
				foreach ( var id in mine.Members ) TuneToSquad( PlayerBySteam( Scene, id ), false );
				_host.Squads.Remove( mine );
				Publish();
				return;

			case "color":
			case "colour":
			{
				if ( mine is null || mine.Leader != me ) { SwrpChat.SystemTo( caller, "Only the squad leader can change the colour." ); return; }
				var hex = rest.Trim();
				if ( !hex.StartsWith( '#' ) ) hex = "#" + hex;
				if ( Color.Parse( hex ) is null || hex.Length != 7 ) { SwrpChat.SystemTo( caller, "Use a hex colour like #ff8800." ); return; }
				mine.ColorHex = hex;
				Publish();
				SwrpChat.SystemTo( caller, $"Squad colour set to {hex}." );
				return;
			}

			case "rename":
				if ( mine is null || mine.Leader != me ) { SwrpChat.SystemTo( caller, "Only the squad leader can rename it." ); return; }
				if ( rest.Length is < 2 or > 24 ) { SwrpChat.SystemTo( caller, "Squad names are 2 to 24 characters." ); return; }
				mine.Name = rest;
				Publish();
				SquadNotice( mine, $"Squad renamed to {rest}." );
				return;

			case "list":
				if ( _host.Squads.Count == 0 ) { SwrpChat.SystemTo( caller, "No squads formed." ); return; }
				foreach ( var s in _host.Squads )
					SwrpChat.SystemTo( caller, $"{s.Name} ({s.Members.Count}/{MaxSquadSize}) led by {Short( PlayerBySteam( Scene, s.Leader ) )}" );
				return;

			default:
				SwrpChat.SystemTo( caller, "Squad: /squad (or F3) opens the squad menu. Commands: create [name], invite <player>, accept, decline, leave, kick <player>, promote <player>, rename <name>, color <#hex>, rally [clear], disband, list." );
				return;
		}
	}

	/// <summary> Host: joining a squad puts your radio on its own net; leaving takes you back to your legion's. </summary>
	static void TuneToSquad( SwrpPlayer p, bool on )
	{
		if ( !p.IsValid() ) return;
		if ( on ) p.RadioChannel = "squad";
		else if ( p.RadioChannel == "squad" ) p.RadioChannel = "legion";
	}

	void RemoveMember( SquadInfo squad, ulong steamId, string notice )
	{
		squad.Members.Remove( steamId );
		if ( squad.Members.Count == 0 )
		{
			_host.Squads.Remove( squad );
			Publish();
			return;
		}

		if ( squad.Leader == steamId ) squad.Leader = squad.Members[0];
		Publish();
		if ( notice is not null ) SquadNotice( squad, notice );
	}

	// ----- /platoon -----

	public void PlatoonCommand( Connection caller, SwrpPlayer sender, string args )
	{
		var (sub, rest) = Split( args );
		var me = sender.OwnerSteamId;
		var officer = sender.RankIndex >= LieutenantRank || Whitelist.IsStaff( caller );
		var mine = _host.Platoons.FirstOrDefault( p => p.Commander == me );

		switch ( sub.ToLowerInvariant() )
		{
			case "create":
			{
				if ( !officer ) { SwrpChat.SystemTo( caller, "Platoons are led by Lieutenants and above." ); return; }
				if ( mine is not null ) { SwrpChat.SystemTo( caller, $"You already command {mine.Name}." ); return; }
				var name = rest.Length is >= 2 and <= 24 ? rest : $"Platoon {_host.Platoons.Count + 1}";
				mine = new PlatoonInfo { Id = _nextId++, Name = name, Commander = me, CommanderName = Short( sender ) };
				_host.Platoons.Add( mine );
				Publish();
				SwrpChat.SystemTo( caller, $"{name} formed. Attach squads with /platoon add <squad name>." );
				return;
			}

			case "add":
			case "remove":
			{
				if ( mine is null ) { SwrpChat.SystemTo( caller, "You don't command a platoon. /platoon create first." ); return; }
				var squad = _host.Squads.FirstOrDefault( s => s.Name.Equals( rest.Trim(), StringComparison.OrdinalIgnoreCase ) );
				if ( squad is null ) { SwrpChat.SystemTo( caller, $"No squad named '{rest}'. /squad list shows them." ); return; }
				var adding = sub.Equals( "add", StringComparison.OrdinalIgnoreCase );
				squad.PlatoonId = adding ? mine.Id : 0;
				Publish();
				SquadNotice( squad, adding ? $"Attached to {mine.Name} under {mine.CommanderName}." : $"Detached from {mine.Name}." );
				SwrpChat.SystemTo( caller, $"{squad.Name} {( adding ? "attached to" : "detached from" )} {mine.Name}." );
				return;
			}

			case "disband":
				if ( mine is null ) { SwrpChat.SystemTo( caller, "You don't command a platoon." ); return; }
				foreach ( var s in _host.Squads.Where( s => s.PlatoonId == mine.Id ) ) s.PlatoonId = 0;
				_host.Platoons.Remove( mine );
				Publish();
				SwrpChat.SystemTo( caller, $"{mine.Name} disbanded." );
				return;

			case "list":
				if ( _host.Platoons.Count == 0 ) { SwrpChat.SystemTo( caller, "No platoons formed." ); return; }
				foreach ( var p in _host.Platoons )
					SwrpChat.SystemTo( caller, $"{p.Name} under {p.CommanderName}: {string.Join( ", ", _host.Squads.Where( s => s.PlatoonId == p.Id ).Select( s => s.Name ) )}" );
				return;

			default:
				SwrpChat.SystemTo( caller, "Platoon (Lieutenant+): /platoon create [name], add <squad>, remove <squad>, disband, list." );
				return;
		}
	}

	// ----- /order -----

	static readonly string[] Priorities = { "low", "normal", "high", "critical" };

	public void OrderCommand( Connection caller, SwrpPlayer sender, string args )
	{
		var (sub, rest) = Split( args );
		var officer = sender.RankIndex >= CaptainRank || Whitelist.IsStaff( caller );
		var lower = sub.ToLowerInvariant();

		if ( lower is "" or "help" )
		{
			SwrpChat.SystemTo( caller, "Orders (Captain+): /order <title> | <objective> | <rally point or 'here'> | <low/normal/high/critical>, /order complete [note], /order fail [note], /order cancel. Hold Tab for the mission board." );
			return;
		}

		if ( lower is "complete" or "done" or "fail" or "failed" or "cancel" )
		{
			if ( !officer ) { SwrpChat.SystemTo( caller, "Only Captains and above can close orders." ); return; }
			var order = _host.Order;
			if ( order is null ) { SwrpChat.SystemTo( caller, "There is no active order." ); return; }
			order.Status = lower switch { "complete" or "done" => "complete", "cancel" => "cancelled", _ => "failed" };
			order.Note = rest;
			order.Closed = DateTime.UtcNow;
			_host.Order = null;
			_host.RecentOrders.Insert( 0, order );
			if ( _host.RecentOrders.Count > 8 ) _host.RecentOrders.RemoveRange( 8, _host.RecentOrders.Count - 8 );
			AppendLog( order );
			Publish();
			SwrpChat.SendTo( Connection.All, ChatKind.Dispatch, $"[ORDERS] {order.Title}: {order.Status.ToUpperInvariant()}{( string.IsNullOrWhiteSpace( rest ) ? "" : $" ({rest})" )}. ({Short( sender )})" );
			PlayUi( SwrpAudio.OrderPosted );
			return;
		}

		if ( !officer ) { SwrpChat.SystemTo( caller, "Only Captains and above can post orders." ); return; }

		var parts = args.Split( '|', StringSplitOptions.TrimEntries );
		var title = parts[0];
		if ( title.Length is < 2 or > 60 ) { SwrpChat.SystemTo( caller, "Usage: /order <title> | <objective> | <rally point or 'here'> | <priority>" ); return; }
		var objective = parts.Length > 1 ? parts[1] : "";
		var rally = parts.Length > 2 ? parts[2] : "";
		var priority = parts.Length > 3 && Priorities.Contains( parts[3].ToLowerInvariant() ) ? parts[3].ToLowerInvariant() : "normal";

		// An order that replaces an active one closes it as superseded.
		if ( _host.Order is { } previous )
		{
			previous.Status = "cancelled";
			previous.Note = "superseded";
			previous.Closed = DateTime.UtcNow;
			_host.RecentOrders.Insert( 0, previous );
			AppendLog( previous );
		}

		var o = new OrderInfo
		{
			Id = _nextId++,
			Title = title,
			Objective = objective,
			Rally = rally.Equals( "here", StringComparison.OrdinalIgnoreCase ) ? $"{Short( sender )}'s position" : rally,
			Priority = priority,
			Author = Short( sender ),
			Posted = DateTime.UtcNow,
		};

		if ( rally.Equals( "here", StringComparison.OrdinalIgnoreCase ) )
		{
			o.HasRallyPoint = true;
			o.RallyX = sender.WorldPosition.x;
			o.RallyY = sender.WorldPosition.y;
			o.RallyZ = sender.WorldPosition.z;
		}

		_host.Order = o;
		if ( _host.RecentOrders.Count > 8 ) _host.RecentOrders.RemoveRange( 8, _host.RecentOrders.Count - 8 );
		AppendLog( o );
		Publish();

		SwrpChat.SendTo( Connection.All, ChatKind.Dispatch, $"[ORDERS | {priority.ToUpperInvariant()}] {title}{( objective.Length > 0 ? $": {objective}" : "" )}{( o.Rally.Length > 0 ? $" Rally: {o.Rally}." : "" )} ({o.Author})" );
		PlayUi( SwrpAudio.OrderPosted );
	}

	// ----- Radio -----

	/// <summary> Host: send a radio line. Channel null means the sender's current channel. </summary>
	public void Radio( Connection caller, SwrpPlayer sender, string channel, string text, bool dispatch )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return;
		if ( SwrpStaff.IsGagged( caller.SteamId.ValueUnsigned ) && !dispatch )
		{
			SwrpChat.SystemTo( caller, "You are gagged: your comms are cut." );
			return;
		}

		channel = ( channel ?? sender.RadioChannel ?? "legion" ).ToLowerInvariant();
		var staff = Whitelist.IsStaff( caller );
		IEnumerable<Connection> to;
		string tag;

		switch ( channel )
		{
			case "squad":
				var squad = _host.Squads.FirstOrDefault( s => s.Members.Contains( sender.OwnerSteamId ) );
				if ( squad is null ) { SwrpChat.SystemTo( caller, "You are not in a squad. /squad create or /channel legion." ); return; }
				to = SquadConnections( squad );
				tag = $"SQUAD | {squad.Name}";
				break;
			case "command":
				if ( sender.RankIndex < LieutenantRank && !staff ) { SwrpChat.SystemTo( caller, "The command channel is for Lieutenants and above." ); return; }
				to = Scene.GetAllComponents<SwrpPlayer>().Where( p => p.CharacterId is not null && ( p.RankIndex >= LieutenantRank || p.Network.Owner is { } o && Whitelist.IsStaff( o ) ) ).Select( p => p.Network.Owner );
				tag = "COMMAND";
				break;
			case "open":
				to = Connection.All;
				tag = "OPEN";
				break;
			case "pa":
				if ( sender.RankIndex < CaptainRank && !staff ) { SwrpChat.SystemTo( caller, "The ship's PA is for Captains and above." ); return; }
				to = Connection.All;
				tag = "SHIP PA";
				break;
			default:
				channel = "legion";
				to = Scene.GetAllComponents<SwrpPlayer>().Where( p => p.FactionId == sender.FactionId ).Select( p => p.Network.Owner );
				tag = sender.Faction.ShortName;
				break;
		}

		var name = $"{sender.Rank.Abbreviation} {sender.CharacterName}";
		if ( dispatch ) SwrpChat.SendTo( to, ChatKind.Dispatch, $"[DISPATCH | {tag}] {name}: {text}" );
		else SwrpChat.SendTo( to, ChatKind.Radio, $"[RADIO | {tag}] {name}: {text}" );
	}

	public void ChannelCommand( Connection caller, SwrpPlayer sender, string args )
	{
		var want = args.Trim().ToLowerInvariant();
		if ( !Channels.Contains( want ) )
		{
			SwrpChat.SystemTo( caller, $"Radio channel: {sender.RadioChannel}. Channels: squad, legion, command (Lieutenant+), open, pa (Captain+: Y puts your voice on the ship's speakers). /channel <name> to switch." );
			return;
		}

		if ( want == "command" && sender.RankIndex < LieutenantRank && !Whitelist.IsStaff( caller ) )
		{
			SwrpChat.SystemTo( caller, "The command channel is for Lieutenants and above." );
			return;
		}

		if ( want == "pa" && sender.RankIndex < CaptainRank && !Whitelist.IsStaff( caller ) )
		{
			SwrpChat.SystemTo( caller, "The ship's PA is for Captains and above." );
			return;
		}

		sender.RadioChannel = want;
		SwrpChat.SystemTo( caller, $"Radio set to {want.ToUpperInvariant()}. /r <message> talks on it." );
	}

	// ----- /briefing -----

	public void BriefingCommand( Connection caller, SwrpPlayer sender, string args )
	{
		if ( sender.RankIndex < LieutenantRank && !Whitelist.IsStaff( caller ) )
		{
			SwrpChat.SystemTo( caller, "Only Lieutenants and above (or staff) can run a briefing." );
			return;
		}

		var table = Scene.GetAllComponents<SwrpBriefingTable>()
			.OrderBy( t => t.WorldPosition.DistanceSquared( sender.WorldPosition ) )
			.FirstOrDefault();

		if ( !table.IsValid() || table.WorldPosition.Distance( sender.WorldPosition ) > 450f )
		{
			SwrpChat.SystemTo( caller, "Stand next to a holo-table (war room, bridge or briefing room) to run a briefing." );
			return;
		}

		if ( args.Trim().Equals( "clear", StringComparison.OrdinalIgnoreCase ) )
		{
			table.Clear();
			SwrpChat.SystemTo( caller, "Holo-table cleared." );
			return;
		}

		var parts = args.Split( '|', 2, StringSplitOptions.TrimEntries );
		if ( parts[0].Length == 0 )
		{
			SwrpChat.SystemTo( caller, "Usage: /briefing <title> | <text>   (or /briefing clear)" );
			return;
		}

		table.Show( parts[0], parts.Length > 1 ? parts[1] : "", $"{sender.Rank.Abbreviation} {sender.CharacterName}" );
		SwrpChat.SendTo( SwrpChat.Nearby( sender, 900f ), ChatKind.Radio, $"[BRIEFING] {sender.Rank.Abbreviation} {sender.CharacterName} put up \"{parts[0]}\" on the holo-table." );
	}
}
