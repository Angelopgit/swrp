using System;

namespace Swrp;

/// <summary> How two allegiances treat each other. </summary>
public enum Stance
{
	Hostile = -1,
	Neutral = 0,
	Friendly = 1,
}

/// <summary>
/// A side of the war (the Grand Army of the Republic, the Separatists...). Playable units (<see cref="FactionDefinition"/>:
/// the 501st, the 212th, the Coruscant Guard...) belong to one; NPCs will too. Friendly fire and NPC targeting read
/// <see cref="AllegianceRegistry.StanceBetween"/>.
/// </summary>
public sealed class AllegianceDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string ShortName { get; init; }
	public string Description { get; init; }
	public string ColorHex { get; init; } = "#ffffff";
	public Color Color => Color.Parse( ColorHex ) ?? Color.White;
}

/// <summary>
/// Every allegiance and the relationship table between them. Edit the table to change who fights whom; anything not
/// listed is neutral, and an allegiance is always friendly to itself.
/// </summary>
public static class AllegianceRegistry
{
	public const string Gar = "gar";
	public const string Navy = "republic_navy";
	public const string Jedi = "jedi";
	public const string Cis = "cis";

	// Properties rather than cached fields, so a code hotload picks up edits to the tables.
	public static IReadOnlyList<AllegianceDefinition> All => new List<AllegianceDefinition>
	{
		new() { Id = Gar, Name = "Grand Army of the Republic", ShortName = "GAR", ColorHex = "#d8dde3",
			Description = "The clone army. Every battalion, legion and corps is a sub-division of the GAR." },
		new() { Id = Navy, Name = "Republic Navy", ShortName = "NAVY", ColorHex = "#5b7fb8",
			Description = "Fleet crews and naval officers of the Republic." },
		new() { Id = Jedi, Name = "Jedi Order", ShortName = "JEDI", ColorHex = "#7fc4ff",
			Description = "Jedi Generals and Commanders leading the clone army." },
		new() { Id = Cis, Name = "Confederacy of Independent Systems", ShortName = "CIS", ColorHex = "#c8742a",
			Description = "The Separatists and their droid armies." },
	};

	/// <summary> ( a, b, stance ): symmetric, so each pair is listed once. </summary>
	public static IReadOnlyList<(string A, string B, Stance Stance)> Relations => new List<(string, string, Stance)>
	{
		( Gar, Navy, Stance.Friendly ),
		( Gar, Jedi, Stance.Friendly ),
		( Navy, Jedi, Stance.Friendly ),
		( Gar, Cis, Stance.Hostile ),
		( Navy, Cis, Stance.Hostile ),
		( Jedi, Cis, Stance.Hostile ),
	};

	public static AllegianceDefinition Get( string id ) => All.FirstOrDefault( a => a.Id == id );

	public static Stance StanceBetween( string a, string b )
	{
		if ( string.IsNullOrEmpty( a ) || string.IsNullOrEmpty( b ) ) return Stance.Neutral;
		if ( a == b ) return Stance.Friendly;
		foreach ( var r in Relations )
			if ( ( r.A == a && r.B == b ) || ( r.A == b && r.B == a ) )
				return r.Stance;
		return Stance.Neutral;
	}

	public static bool AreFriendly( string a, string b ) => StanceBetween( a, b ) == Stance.Friendly;
	public static bool AreHostile( string a, string b ) => StanceBetween( a, b ) == Stance.Hostile;
}
