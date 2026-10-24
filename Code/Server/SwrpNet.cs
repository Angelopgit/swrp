using System;

namespace Swrp;

/// <summary>
/// Client to host requests for the character system, and host to client replies.
/// Every request is validated on the host against <see cref="Rpc.Caller"/>'s SteamID.
/// </summary>
public static class SwrpNet
{
	// ----- Client side state, filled by the host's replies -----

	public static CharacterMenuState MenuState { get; private set; }
	public static string LastMessage { get; private set; }
	public static bool LastMessageIsError { get; private set; }
	public static RealTimeSince LastMessageTime { get; private set; }

	/// <summary> Bumped whenever client state changes, so panels can include it in BuildHash. </summary>
	public static int Version { get; private set; }

	public static void ResetClientState()
	{
		MenuState = null;
		LastMessage = null;
		Version++;
	}

	// ----- Requests (client -> host) -----

	[Rpc.Host]
	public static void RequestMenuState()
	{
		SendMenuState( Rpc.Caller );
	}

	[Rpc.Host]
	public static void CreateCharacter( string name, string factionId, string classId, string appearance )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var record = CharacterStore.Create( caller.SteamId.ValueUnsigned, name, factionId, classId, out var error );
		if ( record is null )
		{
			NotifyConnection( caller, error, true );
			return;
		}
		if ( record.Faction.Id == "jedi" )
		{
			record.Appearance = Appearance.Sanitize( appearance );
			CharacterStore.Save( caller.SteamId.ValueUnsigned );
		}

		NotifyConnection( caller, $"Welcome to the Republic, {record.FullTitle}.", false );
		SendMenuState( caller );
		Deploy( caller, record );      // enlisting puts you straight into the new character
	}

	[Rpc.Host]
	public static void DeleteCharacter( string characterId )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var steamId = caller.SteamId.ValueUnsigned;
		var active = SwrpGameManager.Current?.FindPlayer( caller );
		if ( active.IsValid() && active.CharacterId == characterId )
		{
			NotifyConnection( caller, "You can't delete the character you're playing.", true );
			return;
		}

		if ( CharacterStore.Delete( steamId, characterId ) )
			NotifyConnection( caller, "Character deleted.", false );

		SendMenuState( caller );
	}

	[Rpc.Host]
	public static void SelectCharacter( string characterId )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		var record = CharacterStore.Find( caller.SteamId.ValueUnsigned, characterId );
		if ( record is null )
		{
			NotifyConnection( caller, "That character doesn't exist.", true );
			return;
		}

		Deploy( caller, record );
	}

	/// <summary>
	/// Dev, host's own editor session: deploy the host as one of their characters by name (part of it), without the menu, so
	/// a remote tool (the editor MCP) can get into the game to test. Returns what happened.
	/// </summary>
	public static string DevDeployLocal( string name )
	{
		if ( !Networking.IsHost || Connection.Local is not { } me ) return "not host";
		var record = CharacterStore.GetCharacters( me.SteamId.ValueUnsigned ).FirstOrDefault( c => c.Name.Contains( name ?? "", StringComparison.OrdinalIgnoreCase ) );
		if ( record is null ) return "no such character";
		Deploy( me, record );
		return $"deployed {record.Name}";
	}

	/// <summary>
	/// Dev, host's own editor session: deploy the host as a throwaway test character ("Test Dummy", 501st, any class), made
	/// on first use outside the character slots, so tools never have to deploy someone's real character (never Cub).
	/// </summary>
	public static string DevDeployTest( string classId )
	{
		if ( !Networking.IsHost || Connection.Local is not { } me ) return "not host";
		var list = CharacterStore.GetCharacters( me.SteamId.ValueUnsigned );
		var record = list.FirstOrDefault( c => c.Name == "Test Dummy" );
		if ( record is null )
		{
			record = new CharacterRecord { Name = "Test Dummy", OwnerSteamId = me.SteamId.ValueUnsigned, FactionId = "501st", Designation = "0000" };
			list.Add( record );
		}
		record.ClassId = ClassRegistry.FixFor( record.FactionId, string.IsNullOrEmpty( classId ) ? ClassRegistry.Trooper : classId );
		CharacterStore.Save( me.SteamId.ValueUnsigned );
		Deploy( me, record );
		return $"deployed Test Dummy as {record.ClassId}";
	}

	/// <summary> Host: spawn a connection as one of their characters and close the menu. </summary>
	static void Deploy( Connection caller, CharacterRecord record )
	{
		// Losing a whitelist after creating the character locks it until re-whitelisted.
		if ( !Whitelist.IsAllowed( caller.SteamId.ValueUnsigned, record.Faction ) )
		{
			NotifyConnection( caller, $"You are no longer whitelisted for the {record.Faction.Name}.", true );
			return;
		}

		record.LastPlayed = DateTime.UtcNow;
		CharacterStore.Save( caller.SteamId.ValueUnsigned );

		SwrpGameManager.Current?.SpawnCharacter( caller, record );
		CloseMenu( caller );
	}

	/// <summary> Admin command: grant or revoke a faction whitelist. Target is a SteamID64 or part of a connected player's name. </summary>
	[Rpc.Host]
	public static void AdminWhitelist( string target, string factionId, bool grant )
	{
		var caller = Rpc.Caller;
		if ( !RequireAdmin( caller ) ) return;

		if ( !TryResolveSteamId( target, out var steamId, out var label ) )
		{
			NotifyConnection( caller, $"No player or SteamID matches '{target}'.", true );
			return;
		}

		var faction = FactionRegistry.Get( factionId );
		if ( faction is null )
		{
			NotifyConnection( caller, $"Unknown faction '{factionId}'. Valid: {string.Join( ", ", FactionRegistry.All.Select( x => x.Id ) )}", true );
			return;
		}

		var changed = grant ? Whitelist.Grant( steamId, faction.Id, caller?.DisplayName ?? "console", label ) : Whitelist.Revoke( steamId, faction.Id );
		var verb = grant ? "whitelisted for" : "removed from";
		NotifyConnection( caller, changed ? $"{label} {verb} the {faction.Name}." : "Nothing changed.", !changed );

		// Let the target see their new options straight away.
		if ( Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == steamId ) is { } targetConnection )
		{
			if ( changed ) NotifyConnection( targetConnection, $"You have been {verb} the {faction.Name}.", false );
			SendMenuState( targetConnection );
		}
	}

	/// <summary> Admin command: set the rank of a connected player's current character. </summary>
	[Rpc.Host]
	public static void AdminSetRank( string target, int rankIndex )
	{
		var caller = Rpc.Caller;
		if ( !RequireAdmin( caller ) ) return;

		var targetConnection = FindConnection( target );
		var player = targetConnection is null ? null : SwrpGameManager.Current?.FindPlayer( targetConnection );
		if ( !player.IsValid() )
		{
			NotifyConnection( caller, $"No spawned player matches '{target}'.", true );
			return;
		}

		var record = CharacterStore.Find( player.OwnerSteamId, player.CharacterId );
		if ( record is null ) return;

		var newRank = Math.Clamp( rankIndex, 0, record.Faction.Ranks.Count - 1 );
		SwrpRanks.Record( $"[console] {caller?.DisplayName ?? "host"}", caller?.SteamId.ValueUnsigned ?? 0, record, record.RankIndex, newRank, "set rank of" );
		record.RankIndex = newRank;
		CharacterStore.Save( player.OwnerSteamId );
		player.RefreshFromStore();

		NotifyConnection( caller, $"{record.FullTitle} is now {record.Rank.Name}.", false );
		NotifyConnection( targetConnection, $"You are now {record.Rank.Name}.", false );
	}

	[Rpc.Host]
	public static void AdminAddAdmin( string target )
	{
		var caller = Rpc.Caller;
		if ( !RequireAdmin( caller ) ) return;

		if ( !TryResolveSteamId( target, out var steamId, out var label ) )
		{
			NotifyConnection( caller, $"No player or SteamID matches '{target}'.", true );
			return;
		}

		Whitelist.AddAdmin( steamId );
		NotifyConnection( caller, $"{label} is now an SWRP admin.", false );
	}

	[Rpc.Host]
	public static void AdminListWhitelist()
	{
		var caller = Rpc.Caller;
		if ( !RequireAdmin( caller ) ) return;

		var text = Whitelist.Describe();
		Log.Info( $"[SWRP] Whitelist:\n{text}" );
		NotifyConnection( caller, "Whitelist printed to the console.", false );
		PrintToConnection( caller, text );
	}

	// ----- Replies (host -> one client) -----

	public static void SendMenuState( Connection connection )
	{
		if ( connection is null ) return;

		var steamId = connection.SteamId.ValueUnsigned;
		var state = new CharacterMenuState
		{
			Characters = CharacterStore.GetCharacters( steamId ).Select( CharacterSummary.From ).ToList(),
			AllowedFactions = Whitelist.AllowedFactions( steamId ),
			MaxCharacters = CharacterStore.MaxCharactersPerPlayer,
			IsAdmin = Whitelist.IsAdmin( connection ),
		};

		using ( Rpc.FilterInclude( connection ) )
		{
			ReceiveMenuState( Json.Serialize( state ) );
		}
	}

	public static void NotifyConnection( Connection connection, string message, bool isError )
	{
		if ( connection is null ) return;

		using ( Rpc.FilterInclude( connection ) )
		{
			ReceiveNotification( message, isError );
		}
	}

	static void PrintToConnection( Connection connection, string text )
	{
		using ( Rpc.FilterInclude( connection ) )
		{
			ReceiveConsolePrint( text );
		}
	}

	static void CloseMenu( Connection connection )
	{
		using ( Rpc.FilterInclude( connection ) )
		{
			ReceiveCloseMenu();
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveMenuState( string json )
	{
		MenuState = Json.Deserialize<CharacterMenuState>( json );
		Version++;
	}

	/// <summary> Show a toast on this machine only (no network). </summary>
	public static void ShowLocal( string message, bool isError )
	{
		LastMessage = message;
		LastMessageIsError = isError;
		LastMessageTime = 0;
		Version++;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveNotification( string message, bool isError )
	{
		LastMessage = message;
		LastMessageIsError = isError;
		LastMessageTime = 0;
		Version++;

		if ( isError ) Log.Warning( $"[SWRP] {message}" );
		else Log.Info( $"[SWRP] {message}" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveConsolePrint( string text )
	{
		Log.Info( text );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveCloseMenu()
	{
		SwrpUiState.CharacterMenuOpen = false;
		Version++;
	}

	// ----- Helpers -----

	static bool RequireAdmin( Connection caller )
	{
		if ( Whitelist.IsAdmin( caller ) ) return true;
		NotifyConnection( caller, "You need to be an admin to do that.", true );
		return false;
	}

	public static Connection FindConnection( string target ) => FindConnection( target, out _ );

	/// <summary>
	/// An online player by SteamID, Steam name or character name. Exact names win over names that start with the text,
	/// and those over names that contain it; when more than one player matches at the best level nobody is returned and
	/// <paramref name="problem"/> lists them, so "/slay CT" can never pick whoever happens to be first.
	/// </summary>
	public static Connection FindConnection( string target, out string problem )
	{
		problem = null;
		target = target?.Trim().Trim( '"' );
		if ( string.IsNullOrWhiteSpace( target ) ) return null;

		if ( ulong.TryParse( target, out var id ) )
			return Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == id );

		var players = SwrpGameManager.Current?.Scene.GetAllComponents<SwrpPlayer>().ToList() ?? new List<SwrpPlayer>();
		string CharName( Connection c ) => players.FirstOrDefault( p => p.Network.OwnerId == c.Id )?.CharacterName ?? "";
		bool Test( string name, int level ) => level switch
		{
			0 => name.Equals( target, StringComparison.OrdinalIgnoreCase ),
			1 => name.StartsWith( target, StringComparison.OrdinalIgnoreCase ),
			_ => name.Contains( target, StringComparison.OrdinalIgnoreCase ),
		};

		for ( var level = 0; level < 3; level++ )
		{
			var hits = Connection.All.Where( c => Test( c.DisplayName ?? "", level ) || Test( CharName( c ), level ) ).Distinct().ToList();
			if ( hits.Count == 1 ) return hits[0];
			if ( hits.Count > 1 )
			{
				problem = $"'{target}' matches {hits.Count} players: {string.Join( ", ", hits.Take( 6 ).Select( c => $"{c.DisplayName} ({CharName( c )})" ) )}. Use more of the name, \"quotes\" for names with spaces, or the SteamID.";
				return null;
			}
		}
		return null;
	}

	public static bool TryResolveSteamId( string target, out ulong steamId, out string label )
	{
		if ( FindConnection( target ) is { } connection )
		{
			steamId = connection.SteamId.ValueUnsigned;
			label = connection.DisplayName;
			return true;
		}

		if ( ulong.TryParse( target, out steamId ) && steamId > 0 )
		{
			label = steamId.ToString();
			return true;
		}

		label = null;
		return false;
	}
}
