using System;

namespace Swrp;

/// <summary>
/// Host-only persistence for characters. One json file per Steam account under the game's data folder,
/// so restarts keep everyone's characters and inventories.
/// </summary>
public static class CharacterStore
{
	public const int MaxCharactersPerPlayer = 3;
	const string Folder = "swrp/characters";

	static readonly Dictionary<ulong, List<CharacterRecord>> _cache = new();

	static string PathFor( ulong steamId ) => $"{Folder}/{steamId}.json";

	/// <summary> Forget cached files so edits made on disk between sessions are picked up. </summary>
	public static void ClearCache() => _cache.Clear();

	public static List<CharacterRecord> GetCharacters( ulong steamId )
	{
		if ( _cache.TryGetValue( steamId, out var list ) )
		{
			foreach ( var c in list ) c.ClassId = ClassRegistry.FixFor( c.FactionId, c.ClassId );
			return list;
		}

		list = FileSystem.Data.ReadJson<List<CharacterRecord>>( PathFor( steamId ), null ) ?? new List<CharacterRecord>();
		// Jedi made before the Jedi class (or moved between the Order and a battalion) get the class that fits them
		foreach ( var c in list ) c.ClassId = ClassRegistry.FixFor( c.FactionId, c.ClassId );
		_cache[steamId] = list;
		return list;
	}

	public static CharacterRecord Find( ulong steamId, string characterId )
		=> GetCharacters( steamId ).FirstOrDefault( x => x.Id == characterId );

	public static void Save( ulong steamId )
	{
		if ( !_cache.TryGetValue( steamId, out var list ) ) return;

		try
		{
			FileSystem.Data.CreateDirectory( Folder );

			// Keep the previous save next to the new one, so a bad write or an accidental delete can be undone by hand.
			var path = PathFor( steamId );
			if ( FileSystem.Data.FileExists( path ) )
				FileSystem.Data.WriteAllText( $"{Folder}/{steamId}.previous.json", FileSystem.Data.ReadAllText( path ) );

			FileSystem.Data.WriteJson( path, list );
		}
		catch ( Exception e )
		{
			Log.Error( $"[SWRP] Failed to save characters for {steamId}: {e.Message}" );
		}
	}

	/// <summary>
	/// Validates and creates a character. Returns null and sets <paramref name="error"/> on failure.
	/// </summary>
	public static CharacterRecord Create( ulong steamId, string name, string factionId, string classId, out string error )
	{
		error = null;
		name = (name ?? "").Trim();

		var characters = GetCharacters( steamId );
		if ( characters.Count >= MaxCharactersPerPlayer )
		{
			error = $"You can only have {MaxCharactersPerPlayer} characters.";
			return null;
		}

		if ( name.Length < 2 || name.Length > 24 )
		{
			error = "Name must be between 2 and 24 characters.";
			return null;
		}

		if ( !name.All( c => char.IsLetter( c ) || c == ' ' || c == '-' || c == '\'' ) )
		{
			error = "Name can only contain letters, spaces, hyphens and apostrophes.";
			return null;
		}

		var faction = FactionRegistry.Get( factionId );
		if ( faction is null )
		{
			error = "Unknown faction.";
			return null;
		}

		if ( !Whitelist.IsAllowed( steamId, faction ) )
		{
			error = $"You are not whitelisted for the {faction.Name}.";
			return null;
		}

		// everyone enlists as a Trooper (Jedi as the Jedi): classes are set in game by officers and staff (/class, SwrpCareer)
		var trooperClass = ClassRegistry.Get( ClassRegistry.FixFor( faction.Id, ClassRegistry.Trooper ) );

		var record = new CharacterRecord
		{
			ClassId = trooperClass.Id,
			OwnerSteamId = steamId,
			Name = name,
			FactionId = faction.Id,
			Designation = Game.Random.Next( 1000, 9999 ).ToString(),
		};

		foreach ( var stack in faction.StartingLoadout.Concat( trooperClass.ExtraKit ) )
			record.AddItem( stack.ItemId, stack.Count );

		characters.Add( record );
		Save( steamId );

		Log.Info( $"[SWRP] {steamId} created character {record.FullTitle} ({faction.Name}, {trooperClass.Name})" );
		return record;
	}

	public static bool Delete( ulong steamId, string characterId )
	{
		var characters = GetCharacters( steamId );
		var removed = characters.FirstOrDefault( x => x.Id == characterId );
		if ( removed is null )
			return false;

		characters.Remove( removed );
		Save( steamId );
		Log.Info( $"[SWRP] {steamId} deleted character {removed.FullTitle} ({removed.Id})" );
		return true;
	}
}
