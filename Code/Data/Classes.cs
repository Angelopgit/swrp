using System;

namespace Swrp;

/// <summary>
/// A trooper class: which body the character wears and how it plays. Picked once when the character is created.
/// The model paths live here and nowhere else, so swapping or removing a model is a one-line change. Every class is a
/// Battlefront II model on the same Citizen rig (ModelSource/bf2_clones.py), so they all animate alike; battalions are
/// material groups on them (Characters/SwrpOutfit.cs).
/// </summary>
public sealed class ClassDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public string ModelPath { get; init; }

	/// <summary> Armour the class spawns with. Absorbs most incoming damage until it's gone. </summary>
	public float MaxArmor { get; init; } = 50f;

	/// <summary> Multiplies the controller's walk and run speed. </summary>
	public float SpeedScale { get; init; } = 1f;

	/// <summary> Field medic: revives in a few seconds and restores more health; the defibrillator and medkit work in their hands. </summary>
	public bool IsMedic { get; init; }

	/// <summary> The SwrpCareer qualification (/qual id) that unlocks this class in a battalion; null = open to everyone. </summary>
	public string Qual { get; init; }

	/// <summary> Weapon item ids this class may use, primary first. Missing ones are issued when the trooper deploys. </summary>
	public IReadOnlyList<string> Weapons { get; init; } = new List<string>();

	/// <summary> Extra items on top of the faction's starting kit. </summary>
	public IReadOnlyList<ItemStack> ExtraKit { get; init; } = new List<ItemStack>();

	/// <summary> Short perk lines shown on the creation screen. </summary>
	public IReadOnlyList<string> Perks { get; init; } = new List<string>();

	/// <summary> Only for this faction (the Jedi class); clone units don't see it among the class tabs. </summary>
	public string OnlyFaction { get; init; }

	/// <summary>
	/// Clone weapon handling: 1 = the clone set (rifle carry, shouldered aim, crouched aim, recoil and breathing made for
	/// the troopers, see "<c>_drill</c>" models), 0 = the stock s&box human set. Each player's own view; changes apply at once.
	/// </summary>
	[ConVar( "swrp_clone_anims", Help = "Clone weapon animations (1 = clone set, 0 = stock s&box set)", Saved = true )]
	public static bool CloneAnims { get; set; } = true;

	/// <summary>
	/// Everything stock, to compare (Angelo, 2026-10-19: "pipe them to Sandbox's regular humanoid model and see how it looks"):
	/// the class's base model on the s&box human animations alone, and none of our procedural holds, stances or carries.
	/// Each player's own view. 0 keeps the custom set (<see cref="CloneAnims"/>).
	/// </summary>
	[ConVar( "swrp_stock_anims", Help = "1 = clones on the stock s&box human animations only (no custom sets or procedural holds), to compare", Saved = true )]
	public static bool StockAnims { get; set; } = true;      // the default since Angelo's comparison (2026-10-19: "Stock looks better")

	/// <summary> The custom animation sets are on (the clone/Squad/patrol variants). </summary>
	public static bool Custom => CloneAnims && !StockAnims;

	/// <summary> The model path to use right now: the "_drill" variant (clone animation set) when it's switched on. </summary>
	public string ActiveModelPath => Custom && ModelPath?.EndsWith( ".vmdl" ) == true ? ModelPath[..^5] + "_drill.vmdl" : ModelPath;

	/// <summary>
	/// Null if the model file isn't in the project, so callers can fall back. <paramref name="squad"/>: the "_squad" variant
	/// (Squad's soldier movement and the prone sequences, ModelSource/gc_droid_anims.py) for a trooper with a weapon out or
	/// lying prone; unarmed troopers keep the original walk and run (Angelo, 2026-10-13). Classes without one use the usual.
	/// </summary>
	/// <summary>
	/// Stock mode: Garry's Mod's own player animations for a GMod hold type ("normal", "ar2", "pistol", "passive"), the
	/// "_gmod_&lt;hold&gt;" variant (ModelSource/gc_droid_anims.py, Angelo 2026-10-19). Null when the class has none.
	/// </summary>
	/// <summary> The GMod animation sets (back on, redone: Angelo, 2026-10-19 "re-implement the animations from Gary's mod"). </summary>
	[ConVar( "swrp_gmod_anims", Help = "1 = clones on Garry's Mod's own animation sets (stock mode only); 0 = the s&box human animations", Saved = true )]
	public static bool GmodAnims { get; set; } = true;

	public Model LoadGmod( string hold )
	{
		if ( !GmodAnims ) return null;
		var path = ModelPath?.EndsWith( ".vmdl" ) == true ? ModelPath[..^5] + $"_gmod_{hold}.vmdl" : null;
		return path is not null && FileSystem.Mounted.FileExists( path + "_c" ) && Model.Load( path ) is { IsError: false } m ? m : null;
	}

	public Model LoadModel( bool squad = false, bool patrol = false )
	{
		// relaxed: the "_patrol" variant, the Squad set with Squad's patrol walk (gc_droid_anims.py, Angelo 2026-10-19)
		var patrolPath = ModelPath?.EndsWith( ".vmdl" ) == true ? ModelPath[..^5] + "_patrol.vmdl" : null;
		if ( patrol && Custom && patrolPath is not null && FileSystem.Mounted.FileExists( patrolPath + "_c" ) && Model.Load( patrolPath ) is { IsError: false } pm )
			return pm;
		var squadPath = ModelPath?.EndsWith( ".vmdl" ) == true ? ModelPath[..^5] + "_squad.vmdl" : null;
		if ( squad && Custom && squadPath is not null && FileSystem.Mounted.FileExists( squadPath + "_c" ) && Model.Load( squadPath ) is { IsError: false } sq )
			return sq;
		var model = Model.Load( ActiveModelPath );
		if ( ( model is null || model.IsError ) && ActiveModelPath != ModelPath )
			model = Model.Load( ModelPath );
		return model is null || model.IsError ? null : model;
	}
}

public static class ClassRegistry
{
	public const string Trooper = "trooper";
	public const string Jedi = "jedi";

	/// <summary> The class a character of this faction must be: Jedi are always the Jedi class, clones never are. </summary>
	public static string FixFor( string factionId, string classId )
	{
		if ( factionId == Jedi ) return Jedi;
		return classId == Jedi || Get( classId ) is null ? Trooper : classId;
	}

	public const string Officer = "officer";
	public const string Medic = "medic";

	/// <summary> Sergeant: from this rank up a trooper is an Officer (Angelo, 2026-10-13: not picked in the menu, granted by rank). </summary>
	public const int OfficerRank = 4;

	/// <summary>
	/// The class a character actually plays: <see cref="FixFor"/>, then the Officer rule. A Trooper at Sergeant or above is an
	/// Officer (model, kama and kit); an Officer below Sergeant (demoted, or an old save) goes back to Trooper. Heavy and
	/// Specialist keep their own class whatever the rank.
	/// </summary>
	public static string ForRank( string factionId, string classId, int rankIndex )
	{
		var c = FixFor( factionId, classId );
		if ( factionId == Jedi ) return c;
		if ( c == Trooper && rankIndex >= OfficerRank ) return Officer;
		if ( c == Officer && rankIndex < OfficerRank ) return Trooper;
		return c;
	}

	/// <summary> The classes a faction's creation screen (and /class) offers; Officer comes with rank instead (<see cref="ForRank"/>). </summary>
	public static IEnumerable<ClassDefinition> For( string factionId ) => _classesV5.Where( c => c.Id != Officer && ( c.OnlyFaction is null ? factionId != Jedi : c.OnlyFaction == factionId ) );

	// renamed when the list changes: a code hotload keeps a static list's old contents
	static readonly List<ClassDefinition> _classesV5 = new()
	{
		new ClassDefinition
		{
			Id = Trooper,
			Name = "Trooper",
			Description = "Standard Phase II infantry. Balanced, and what every legion is built on.",
			ModelPath = "models/troopers/clone_trooper.vmdl",
			MaxArmor = 50f,
			Weapons = new List<string> { "dc15a", "dc17" },
			Perks = new List<string> { "Balanced speed", "50 armour", "DC-15A rifle and DC-17 pistol" },
		},
		new ClassDefinition
		{
			Id = "heavy",
			Name = "Heavy",
			Description = "Heavy weapons specialist in reinforced armour. Slower, but carries extra power and ordnance.",
			ModelPath = "models/troopers/clone_heavy.vmdl",
			Qual = "heavy",
			SpeedScale = 0.9f,
			MaxArmor = 100f,
			Weapons = new List<string> { "z6" },
			ExtraKit = new List<ItemStack> { new( "blaster_cell", 8 ), new( "thermal_detonator", 2 ) },
			Perks = new List<string> { "10% slower", "100 armour", "Z-6 rotary blaster", "+8 blaster cells", "+2 thermal detonators" },
		},
		new ClassDefinition
		{
			Id = "specialist",
			Name = "Specialist",
			Description = "Light, fast marksman and recon trooper with the Valken-38X sniper rifle, as Battlefront II fields them.",
			Qual = "sniper",
			ModelPath = "models/troopers/clone_specialist.vmdl",
			SpeedScale = 1.08f,
			MaxArmor = 25f,
			Weapons = new List<string> { "valken38x", "dc17" },
			Perks = new List<string> { "8% faster", "25 armour", "Valken-38X sniper rifle and DC-17 pistol" },
		},
		new ClassDefinition
		{
			Id = Medic,
			Name = "Medic",
			Description = "Combat medic. Defibrillator for the downed, medkit for the wounded, and fast revives.",
			Qual = "medic",
			IsMedic = true,
			// no medic body on the BF2 rig: the trooper model; the medic qual unlocks the specialist visor (SwrpArmor)
			ModelPath = "models/troopers/clone_trooper.vmdl",
			MaxArmor = 50f,
			// the tools ride in Weapons so they're issued on a class change like the guns (ExtraKit only comes at enlisting)
			Weapons = new List<string> { "dc15s", "dc17", ItemRegistry.Defibrillator, ItemRegistry.Medkit },
			Perks = new List<string> { "Balanced speed", "50 armour", "DC-15S carbine and DC-17 pistol", "Defibrillator: hold E on the downed, back at full health", "Medkit: hold E on the wounded, full health, bleeding stopped" },
		},
		new ClassDefinition
		{
			Id = "officer",
			Name = "Officer",
			Qual = "officer",
			Description = "Phase II officer with the command kama and pauldron, as Battlefront II fields them. Leads the squad from the front.",
			ModelPath = "models/troopers/clone_officer.vmdl",
			MaxArmor = 50f,
			Weapons = new List<string> { "dc17", "dc15s" },
			ExtraKit = new List<ItemStack> { new( "datapad", 1 ), new( "binders", 2 ) },
			Perks = new List<string> { "Balanced speed", "50 armour", "DC-17 pistol and DC-15S carbine", "+1 datapad, +2 binders" },
		},
		new ClassDefinition
		{
			Id = Jedi,
			Name = "Jedi",
			Description = "A Jedi of the Order serving with the fleet: robes instead of armour, a lightsaber and the Force.",
			OnlyFaction = Jedi,
			// robes on the same universal rig as the troopers (Characters/SwrpOutfit.cs picks the Jedi's look)
			ModelPath = SwrpOutfit.JediModel,
			SpeedScale = 1.08f,
			MaxArmor = 25f,
			Perks = new List<string> { "Lightsaber: 1 or H lights it", "Force powers (F, Z for the wheel, P for skills)", "8% faster", "25 armour (robes)", "DC-17 pistol in the kit" },
		},
	};

	public static IReadOnlyList<ClassDefinition> All => _classesV5;

	public static ClassDefinition Get( string id ) => _classesV5.FirstOrDefault( x => x.Id == id );

	public static ClassDefinition Default => Get( Trooper );
}
