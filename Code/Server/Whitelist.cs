using System;

namespace Swrp;

/// <summary> Saved by name (whitelist.json Roles), so the numbers can move. </summary>
public enum StaffRole
{
	User = 0,
	TrialModerator = 1,
	Moderator = 2,
	Admin = 3,
	SeniorAdmin = 4,
}

/// <summary>
/// The on-disk whitelist. Lives at <c>swrp/whitelist.json</c> in the host's data folder and can be edited by hand
/// while the server is off, or live with the <c>swrp_whitelist</c> console commands.
/// </summary>
public sealed class WhitelistFile
{
	/// <summary> SteamID64s that can manage the whitelist and use admin commands. The host is always an admin. </summary>
	public List<ulong> Admins { get; set; } = new();

	/// <summary> SteamID64 to "Moderator" or "Admin". Anyone not listed here (and not in Admins) is a plain User. </summary>
	public Dictionary<ulong, string> Roles { get; set; } = new();

	/// <summary> SteamID64 to the faction ids that player may create characters in. </summary>
	public Dictionary<ulong, List<string>> Factions { get; set; } = new();

	/// <summary> "steamid:faction" to who granted it, the player's name then, and when (UTC). </summary>
	public Dictionary<string, WhitelistGrant> Granted { get; set; } = new();

	/// <summary> Staff (Moderators) who may also run events with the Game Master tools. Admins always can. </summary>
	public List<ulong> GameMasters { get; set; } = new();
}

public sealed class WhitelistGrant
{
	public string By { get; set; }
	public string Player { get; set; }
	public DateTime When { get; set; }
}

public static class Whitelist
{
	const string FilePath = "swrp/whitelist.json";

	static WhitelistFile _data;

	static WhitelistFile Data
	{
		get
		{
			if ( _data is not null ) return _data;

			_data = FileSystem.Data.ReadJson<WhitelistFile>( FilePath, null );
			if ( _data is null )
			{
				_data = new WhitelistFile();
				Save();
			}

			return _data;
		}
	}

	public static void Reload() => _data = null;

	static void Save()
	{
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( FilePath, _data );
		}
		catch ( Exception e )
		{
			Log.Error( $"[SWRP] Failed to save whitelist: {e.Message}" );
		}
	}

	public static StaffRole GetRole( ulong steamId )
	{
		if ( Data.Admins.Contains( steamId ) ) return StaffRole.Admin;
		if ( Data.Roles.TryGetValue( steamId, out var name ) && Enum.TryParse<StaffRole>( name, true, out var role ) )
			return role;
		return StaffRole.User;
	}

	/// <summary> The host and anyone with the s&box "admin" permission are always Admin. </summary>
	public static StaffRole GetRole( Connection connection )
	{
		if ( connection is null ) return StaffRole.User;
		if ( connection.IsHost ) return StaffRole.SeniorAdmin;
		if ( connection.HasPermission( "admin" ) ) return StaffRole.Admin;
		return GetRole( connection.SteamId.ValueUnsigned );
	}

	public static void SetRole( ulong steamId, StaffRole role )
	{
		Data.Admins.Remove( steamId );
		if ( role == StaffRole.User ) Data.Roles.Remove( steamId );
		else Data.Roles[steamId] = role.ToString();
		Save();
	}

	public static bool IsAdmin( Connection connection ) => GetRole( connection ) >= StaffRole.Admin;

	/// <summary> The Game Master flag (see SwrpGameMaster.IsGameMaster, which also lets every admin in). </summary>
	public static bool IsGameMaster( ulong steamId ) => Data.GameMasters.Contains( steamId );

	public static void SetGameMaster( ulong steamId, bool on )
	{
		Data.GameMasters.Remove( steamId );
		if ( on ) Data.GameMasters.Add( steamId );
		Save();
	}
	public static bool IsStaff( Connection connection ) => GetRole( connection ) >= StaffRole.Moderator;

	/// <summary> Trial Moderator or above: staff chat, the staff panel and the commands their role allows (IsStaff powers stay Moderator+). </summary>
	public static bool IsTrialStaff( Connection connection ) => GetRole( connection ) >= StaffRole.TrialModerator;

	public static bool TryParseRole( string text, out StaffRole role )
	{
		switch ( ( text ?? "" ).Trim().ToLowerInvariant() )
		{
			case "trial": case "trialmod": role = StaffRole.TrialModerator; return true;
			case "mod": role = StaffRole.Moderator; return true;
			case "senior": role = StaffRole.SeniorAdmin; return true;
			default: return Enum.TryParse( text?.Trim(), true, out role ) && Enum.IsDefined( role );
		}
	}

	public static bool IsAllowed( ulong steamId, FactionDefinition faction )
	{
		if ( faction is null ) return false;
		if ( !faction.Whitelisted ) return true;

		// Admins can create in any faction so they can run events.
		if ( Data.Admins.Contains( steamId ) ) return true;
		if ( Connection.All.FirstOrDefault( c => c.SteamId.ValueUnsigned == steamId ) is { } c && IsAdmin( c ) ) return true;

		return Data.Factions.TryGetValue( steamId, out var list ) && list.Contains( faction.Id );
	}

	/// <summary> Whether this player was whitelisted for this battalion (not just allowed because it's open or they're an admin). </summary>
	public static bool IsWhitelistedFor( ulong steamId, string factionId ) => Data.Factions.TryGetValue( steamId, out var list ) && list.Contains( factionId );

	/// <summary> Every whitelist entry: SteamID64 and the battalions they were whitelisted for. </summary>
	public static IEnumerable<(ulong SteamId, List<string> Factions)> AllFactions() => Data.Factions.Select( x => (x.Key, x.Value) ).ToList();

	public static List<string> AllowedFactions( ulong steamId )
		=> FactionRegistry.All.Where( f => IsAllowed( steamId, f ) ).Select( f => f.Id ).ToList();

	public static bool Grant( ulong steamId, string factionId ) => Grant( steamId, factionId, null, null );

	public static bool Grant( ulong steamId, string factionId, string by, string playerName )
	{
		if ( FactionRegistry.Get( factionId ) is null ) return false;
		Data.Granted[$"{steamId}:{factionId}"] = new WhitelistGrant { By = by ?? "console", Player = playerName ?? steamId.ToString(), When = DateTime.UtcNow };

		if ( !Data.Factions.TryGetValue( steamId, out var list ) )
			Data.Factions[steamId] = list = new List<string>();

		if ( !list.Contains( factionId ) )
			list.Add( factionId );

		Save();
		return true;
	}

	public static bool Revoke( ulong steamId, string factionId )
	{
		if ( !Data.Factions.TryGetValue( steamId, out var list ) || !list.Remove( factionId ) )
			return false;

		if ( list.Count == 0 )
			Data.Factions.Remove( steamId );
		Data.Granted.Remove( $"{steamId}:{factionId}" );

		Save();
		return true;
	}

	public static void AddAdmin( ulong steamId ) => SetRole( steamId, StaffRole.Admin );

	/// <summary> Every whitelist entry with who granted it (older entries show "unknown"). </summary>
	public static IEnumerable<(string Faction, string Player, string By, string When)> Grants()
	{
		foreach ( var (steamId, factions) in Data.Factions )
			foreach ( var f in factions )
			{
				Data.Granted.TryGetValue( $"{steamId}:{f}", out var g );
				yield return (f, g?.Player ?? steamId.ToString(), g?.By ?? "unknown", g is null ? "" : g.When.ToString( "yyyy-MM-dd" ));
			}
	}

	public static string Describe()
	{
		var lines = new List<string> { $"Admins: {string.Join( ", ", Data.Admins )}", $"Moderators: {string.Join( ", ", Data.Roles.Where( x => x.Value == "Moderator" ).Select( x => x.Key ) )}" };
		foreach ( var (steamId, factions) in Data.Factions )
			lines.Add( $"{steamId}: {string.Join( ", ", factions )}" );

		return string.Join( "\n", lines );
	}
}
