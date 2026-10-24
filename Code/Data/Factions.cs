using System;

namespace Swrp;

/// <summary>
/// A rank inside a faction. Rank index 0 is the lowest and is what new characters start at.
/// </summary>
public sealed record RankDefinition( string Id, string Name, string Abbreviation );

/// <summary>
/// A playable unit: a battalion, legion or corps. Every clone unit is a sub-division of the Grand Army of the Republic
/// (its <see cref="Allegiance"/>); relationships between sides live in <see cref="AllegianceRegistry"/>. Whitelisted units
/// need an admin (or, later, their officers) to grant access per SteamID. The ids are what saves store, so never rename them.
/// </summary>
public sealed class FactionDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string ShortName { get; init; }
	public string Description { get; init; }

	/// <summary> Hex colour used for UI accents and the armour tint. </summary>
	public string ColorHex { get; init; } = "#ffffff";

	/// <summary> If true, a player must be on the whitelist for this faction to create a character in it. </summary>
	public bool Whitelisted { get; init; }

	/// <summary>
	/// The side this unit belongs to (an <see cref="AllegianceRegistry"/> id). All clone battalions are GAR. With
	/// swrp_friendlyfire off, blasters don't hurt anyone the relationship table marks friendly.
	/// </summary>
	public string Allegiance { get; init; } = AllegianceRegistry.Gar;

	public AllegianceDefinition AllegianceInfo => AllegianceRegistry.Get( Allegiance );

	/// <summary> "GAR · 501st Legion" style label. </summary>
	public string FullName => AllegianceInfo is { } a ? $"{a.ShortName} · {Name}" : Name;

	/// <summary> Number prefix used when generating designations, e.g. CT-5555. </summary>
	public string DesignationPrefix { get; init; } = "CT";

	/// <summary> Clones carry a CT number; the Jedi go by name ("Padawan Kel Varo"). </summary>
	public bool Numbered { get; init; } = true;

	/// <summary> "CT-5555", or empty for a faction without numbers. </summary>
	public string DesignationFor( string number ) => Numbered ? $"{DesignationPrefix}-{number}" : "";

	/// <summary> "SGT CT-5555 "Hardcase"", or "Padawan Kel Varo" for the Jedi. </summary>
	public string TitleFor( RankDefinition rank, string number, string name ) => Numbered ? $"{rank.Abbreviation} {DesignationPrefix}-{number} \"{name}\"" : $"{rank.Name} {name}";

	/// <summary> "SGT Hardcase", or "Padawan Kel Varo". </summary>
	public string ShortTitleFor( RankDefinition rank, string name ) => Numbered ? $"{rank.Abbreviation} {name}" : $"{rank.Name} {name}";

	IReadOnlyList<RankDefinition> _ranks;

	/// <summary> Defaults to the shared clone ladder. </summary>
	public IReadOnlyList<RankDefinition> Ranks { get => _ranks ?? FactionRegistry.CloneRanks; init => _ranks = value; }

	/// <summary> Item ids (and counts) every new character in this faction starts with. </summary>
	public IReadOnlyList<ItemStack> StartingLoadout { get; init; } = FactionRegistry.BasicLoadout;

	public Color Color => Color.Parse( ColorHex ) ?? Color.White;

	public RankDefinition GetRank( int index ) => Ranks[Math.Clamp( index, 0, Ranks.Count - 1 )];
}

/// <summary>
/// Every playable unit on the server (the GAR's battalions, legions and corps). Add new battalions here; give them
/// Allegiance = AllegianceRegistry.Gar (the default) so they're part of the Grand Army.
/// </summary>
public static class FactionRegistry
{
	public const string CloneTrooper = "clone_trooper";

	/// <summary> Lowest to highest. Index 0 is where every new character starts. </summary>
	// A property (not a cached field) so a code hotload always picks up ladder changes.
	/// <summary>
	/// The Jedi Order's ladder, lowest to highest: the same number of steps as the clone ladder, so a rank's index means the
	/// same standing on the ship (doors, officer tools) for a Jedi as for a clone: a Jedi General stands with a 2nd Lieutenant
	/// and up.
	/// </summary>
	public static IReadOnlyList<RankDefinition> JediRanks => new List<RankDefinition>
	{
		new( "pdn", "Padawan", "PDN" ),
		new( "spdn", "Senior Padawan", "SPDN" ),
		new( "pcmd", "Padawan Commander", "PCMD" ),
		new( "knt", "Jedi Knight", "KNT" ),
		new( "sknt", "Senior Jedi Knight", "SKNT" ),
		new( "jcmd", "Jedi Commander", "JCMD" ),
		new( "gen", "Jedi General", "GEN" ),
		new( "hgen", "High Jedi General", "HGEN" ),
		new( "mstr", "Jedi Master", "MSTR" ),
		new( "smstr", "Senior Jedi Master", "SMSTR" ),
		new( "cncl", "Master of the Council", "CNCL" ),
	};

	public static IReadOnlyList<RankDefinition> CloneRanks => new List<RankDefinition>
	{
		new( "cdt", "Cadet", "CDT" ),
		new( "pvt", "Private", "PVT" ),
		new( "pfc", "Private First Class", "PFC" ),
		new( "cpl", "Corporal", "CPL" ),
		new( "sgt", "Sergeant", "SGT" ),
		new( "ssgt", "Staff Sergeant", "SSG" ),
		new( "2lt", "2nd Lieutenant", "2LT" ),
		new( "1lt", "1st Lieutenant", "1LT" ),
		new( "cpt", "Captain", "CPT" ),
		new( "maj", "Major", "MAJ" ),
		new( "cmdr", "Commander", "CMDR" ),
	};

	/// <summary> Finds a rank by number (1 = Cadet), id, abbreviation or name. Returns -1 if nothing matches. </summary>
	public static int FindRank( IReadOnlyList<RankDefinition> ranks, string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return -1;
		text = text.Trim();

		if ( int.TryParse( text, out var n ) )
			return n >= 1 && n <= ranks.Count ? n - 1 : -1;

		for ( var i = 0; i < ranks.Count; i++ )
		{
			var r = ranks[i];
			if ( string.Equals( r.Id, text, StringComparison.OrdinalIgnoreCase ) || string.Equals( r.Abbreviation, text, StringComparison.OrdinalIgnoreCase ) || string.Equals( r.Name, text, StringComparison.OrdinalIgnoreCase ) )
				return i;
		}

		return -1;
	}

	public static readonly IReadOnlyList<ItemStack> BasicLoadout = new List<ItemStack>
	{
		new( "blaster_cell", 4 ),
		new( "bacta_injector", 2 ),
		new( "bacta_patch", 2 ),
		new( "ration_pack", 3 ),
		new( "datapad", 1 ),
	};

	// renamed when the list changes: a code hotload keeps a static list's old contents
	static readonly List<FactionDefinition> _allV2 = new()
	{
		new FactionDefinition
		{
			Id = CloneTrooper,
			Name = "Clone Trooper",
			ShortName = "CT",
			Description = "A shiny fresh out of Kamino. Every recruit starts here before being picked up by a legion.",
			ColorHex = "#d8dde3",
			Whitelisted = false,
		},
		new FactionDefinition
		{
			Id = "501st",
			Name = "501st Legion",
			ShortName = "501st",
			Description = "Vader's Fist's predecessors. Elite assault troopers under Captain Rex and General Skywalker.",
			ColorHex = "#3a66c4",      // nearer the armour's Mark501 #345ca8, a touch brighter for dark UI (STYLE_GUIDE.md)
			Whitelisted = true,
			StartingLoadout = new List<ItemStack> { new( "dc15a", 1 ), new( "blaster_cell", 6 ), new( "bacta_injector", 2 ), new( "thermal_detonator", 1 ), new( "ration_pack", 3 ), new( "datapad", 1 ) },
		},
		new FactionDefinition
		{
			Id = "212th",
			Name = "212th Attack Battalion",
			ShortName = "212th",
			Description = "Commander Cody's ghosts. Combined-arms specialists serving General Kenobi.",
			ColorHex = "#e08a1e",
			Whitelisted = true,
			StartingLoadout = new List<ItemStack> { new( "dc15a", 1 ), new( "blaster_cell", 6 ), new( "bacta_injector", 2 ), new( "thermal_detonator", 1 ), new( "ration_pack", 3 ), new( "datapad", 1 ) },
		},
		new FactionDefinition
		{
			Id = "104th",
			Name = "104th Battalion",
			ShortName = "104th",
			Description = "The Wolfpack. Commander Wolffe's hunters under General Plo Koon.",
			ColorHex = "#8a939c",
			Whitelisted = true,
		},
		new FactionDefinition
		{
			Id = "327th",
			Name = "327th Star Corps",
			ShortName = "327th",
			Description = "Commander Bly's corps, attached to General Aayla Secura.",
			ColorHex = "#e3c43a",
			Whitelisted = true,
		},
		new FactionDefinition
		{
			Id = "41st",
			Name = "41st Elite Corps",
			ShortName = "41st",
			Description = "Commander Gree's jungle and recon specialists serving Master Yoda.",
			ColorHex = "#4f8a3c",
			Whitelisted = true,
		},
		new FactionDefinition
		{
			Id = "coruscant_guard",
			Name = "Coruscant Guard",
			ShortName = "CG",
			Description = "Commander Fox's military police. Keep order on the ship and in the brig.",
			ColorHex = "#b3262b",
			Whitelisted = true,
			StartingLoadout = new List<ItemStack> { new( "dc17", 1 ), new( "stun_baton", 1 ), new( "binders", 2 ), new( "blaster_cell", 4 ), new( "ration_pack", 3 ), new( "datapad", 1 ) },
		},
		new FactionDefinition
		{
			Id = "jedi",
			Name = "Jedi Order",
			ShortName = "JEDI",
			Description = "Jedi Generals, Commanders and Padawans leading the clone army. On the Republic's side with every battalion, with their own comms (JEDI NET).",
			ColorHex = "#7fc4ff",
			Whitelisted = true,
			Allegiance = AllegianceRegistry.Jedi,
			Numbered = false,
			Ranks = JediRanks,
			StartingLoadout = new List<ItemStack> { new( "lightsaber", 1 ), new( "dc17", 1 ), new( "bacta_injector", 2 ), new( "ration_pack", 3 ), new( "datapad", 1 ) },
		},
	};

	public static IReadOnlyList<FactionDefinition> All => _allV2;

	public static FactionDefinition Get( string id ) => _allV2.FirstOrDefault( x => x.Id == id );

	public static FactionDefinition Default => Get( CloneTrooper );
}
