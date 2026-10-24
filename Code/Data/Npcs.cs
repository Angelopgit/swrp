using System;

namespace Swrp;

/// <summary> How an NPC type moves and fights. </summary>
public enum NpcStyle
{
	/// <summary> Holds ground, shoots in bursts, ducks into cover when hurt or under fire (B1, clones). </summary>
	Rifleman,

	/// <summary> Walks straight at the enemy firing its wrist blasters, never hides (B2). </summary>
	Advancer,

	/// <summary> Rolls fast to firing range, unfolds, raises its shield and fires twin blasters; rolls again when it loses sight (droideka). </summary>
	Roller,
}

/// <summary>
/// One kind of NPC (or event character a Game Master can play). Data only; <see cref="SwrpNpc"/> runs it. The droid models are
/// simple originals from ModelSource/droids.py, stand-ins until the server's own CIS droid models are added: set
/// <see cref="ModelPath"/> to the new model (any model on the clone/human skeleton animates) and it is used.
/// </summary>
public sealed class NpcDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }

	/// <summary> Short name for the kill feed and the GM menu. </summary>
	public string Short { get; init; }

	public string AllegianceId { get; init; } = AllegianceRegistry.Cis;
	public AllegianceDefinition Allegiance => AllegianceRegistry.Get( AllegianceId );

	/// <summary> A citizen-rigged model (animated with the same graph as troopers). Empty: built from primitives (the droideka). </summary>
	public string ModelPath { get; init; } = "models/citizen/citizen.vmdl";
	public string TintHex { get; init; } = "#ffffff";
	public float Scale { get; init; } = 1f;

	public float Health { get; init; } = 100f;
	public float Armor { get; init; }

	/// <summary> A droideka's deflector: soaks this much before the body takes damage, recharges when not hit. </summary>
	public float Shield { get; init; }

	public float WalkSpeed { get; init; } = 140f;
	public float RunSpeed { get; init; } = 220f;

	/// <summary> Stun rounds keep it down this many times as long as a trooper (0 = immune). </summary>
	public float StunScale { get; init; } = 1f;

	public NpcStyle Style { get; init; } = NpcStyle.Rifleman;

	// weapon
	public string WeaponName { get; init; } = "E-5 blaster";

	/// <summary>
	/// A player weapon item this NPC carries (ItemRegistry id). Its model goes in the hand, and its damage, rate of fire, range,
	/// sound and bolt replace the numbers below, so an NPC clone's DC-15A is the same gun a trooper carries.
	/// </summary>
	public string WeaponItem { get; init; }
	public WeaponStats Gun => ItemRegistry.Get( !string.IsNullOrEmpty( WeaponItem ) ? WeaponItem : Id == NpcRegistry.Clone ? "dc15a" : null )?.Weapon;

	float _damage = 8f, _fireDelay = 0.45f, _range = 2600f;
	public float Damage { get => NpcDamage ?? Gun?.Damage ?? _damage; init => _damage = value; }
	public float FireDelay { get => NpcFireDelay ?? Gun?.FireDelay ?? _fireDelay; init => _fireDelay = value; }
	public float Range { get => NpcRange ?? Gun?.Range ?? _range; init => _range = value; }
	/// <summary> NPC-only numbers over the carried gun's (the TFA port, 2026-10-12, made player guns much stronger: droids and
	/// NPC clones keep the damage, rate and range they were balanced with). </summary>
	public float? NpcDamage { get; init; }
	public float? NpcFireDelay { get; init; }
	public float? NpcRange { get; init; }
	/// <summary> Damage of one bolt at a distance: the NPC override flat, else the carried gun's drop-off. </summary>
	public float DamageAt( float distance ) => NpcDamage ?? Gun?.DamageAt( distance ) ?? Damage;
	public int Burst { get; init; } = 3;
	public float BurstPause { get; init; } = 1.4f;
	public float Spread { get; init; } = 3f;
	public string BoltHex { get; init; } = "#ff3b2f";
	public int HoldType { get; init; } = 2;

	public string FireSound => Gun?.FireSound ?? ( HoldType == 2 ? "sounds/ui/blaster_carbine.sound" : "sounds/ui/blaster_pistol.sound" );

	/// <summary> Thermal detonators it carries (riflemen one each unless set; see SwrpNpc for when they throw). </summary>
	public int Grenades { get => _grenades ?? ( Style == NpcStyle.Rifleman ? 1 : 0 ); init => _grenades = value; }
	int? _grenades;

	/// <summary> A clone NPC: the trooper class whose body it wears (Data/Classes.cs). Null for droids. </summary>
	public string ClassId { get; init; }

	/// <summary> A clone NPC's battalion: its markings, the class model's material group (Characters/SwrpOutfit.cs). </summary>
	public string BattalionId { get; init; }

	public bool IsClone => ClassId is not null;
	public ClassDefinition Class => ClassRegistry.Get( ClassId ) ?? ClassRegistry.Default;

	/// <summary> The model the spawn menu's icon shows. </summary>
	public string ThumbModel => IsClone ? Class.ModelPath : ModelPath;

	/// <summary> Not spawnable as an NPC, only played by a Game Master (the commander). </summary>
	public bool EventOnly { get; init; }

	public Color Tint => Color.Parse( TintHex ) ?? Color.White;
	public Color BoltColor => Gun?.BoltColor ?? Color.Parse( BoltHex ) ?? Color.Red;
}

public static class NpcRegistry
{
	public const string B1 = "b1";
	public const string B2 = "b2";
	public const string Droideka = "droideka";
	public const string Clone = "clone";
	public const string Commander = "commander";

	/// <summary> Bump when the table changes: a code hotload keeps static fields, so the list is rebuilt when this no longer matches. </summary>
	const int Revision = 5;      // 2: the BF2 B1 and B2 models; 3: the Galactic Contention B1 and B2 with their own animations; 4: B2 holdtype 0; 5: clone battalions and classes
	static List<NpcDefinition> _all;
	static int _allRevision;

	public static IReadOnlyList<NpcDefinition> All
	{
		get
		{
			if ( _all is null || _allRevision != Revision ) { _all = Build(); _allRevision = Revision; }
			return _all;
		}
	}

	static List<NpcDefinition> Build() => new List<NpcDefinition>
	{
		new()
		{
			Id = B1, Name = "B1 Battle Droid", Short = "B1",
			ModelPath = "models/troopers/gc_droid_b1.vmdl", Scale = 1f, Health = 55f,      // Galactic Contention model and locomotion (ModelSource/gc_droids.py, gc_droid_anims.py; local only, ask the GC team before it ships); the BF2 one was clone_droid_b1_drill.vmdl
			WalkSpeed = 120f, RunSpeed = 190f, Style = NpcStyle.Rifleman,
			// the E-5 off the weapon base: its damage, rate of fire, range, sound and bolt
			WeaponName = "E-5", WeaponItem = "e5", Burst = 3, BurstPause = 1.6f, Spread = 3.5f, NpcDamage = 8f, NpcFireDelay = 0.2f, NpcRange = 3000f,
		},
		new()
		{
			Id = B2, Name = "B2 Super Battle Droid", Short = "B2",
			ModelPath = "models/troopers/gc_droid_b2.vmdl", Scale = 1f,      // GC model; its own idle and walk hold the wrist guns up, so no holdtype pose (the pistol one raised an arm)
			 Health = 160f, Armor = 120f, StunScale = 0.4f,
			WalkSpeed = 105f, RunSpeed = 105f, Style = NpcStyle.Advancer,
			WeaponName = "wrist blasters", Damage = 9f, FireDelay = 0.2f, Burst = 6, BurstPause = 1.1f, Spread = 4.5f, Range = 2000f, HoldType = 0,
		},
		new()
		{
			Id = Droideka, Name = "Droideka", Short = "Droideka",
			ModelPath = "", TintHex = "#8a6a45", Health = 180f, Shield = 260f, StunScale = 0f,
			WalkSpeed = 420f, RunSpeed = 420f, Style = NpcStyle.Roller,
			WeaponName = "twin blasters", Damage = 9f, FireDelay = 0.14f, Burst = 8, BurstPause = 0.9f, Spread = 2.5f, Range = 2200f,
		},
		CloneOf( Clone, "Clone Trooper", "Clone", ClassRegistry.Trooper, null ),
		new()
		{
			Id = Commander, Name = "Separatist Commander", Short = "CIS Commander", EventOnly = true,
			ModelPath = "models/droids/droid_commander.vmdl", Scale = 1.08f, Health = 150f, Armor = 50f,
			WeaponName = "DT-57 blaster pistol", Damage = 22f, HoldType = 1,
		},
	}.Concat( CloneVariants() ).ToList();

	static readonly string[] Battalions = { "501st", "212th", "104th", "327th", "41st", "coruscant_guard" };
	static readonly (string Class, string Name)[] CloneClasses = { (ClassRegistry.Trooper, "Trooper"), ("heavy", "Heavy"), ("specialist", "Specialist"), (ClassRegistry.Medic, "Medic"), (ClassRegistry.Officer, "Officer") };

	/// <summary> Every class unmarked, then every battalion in every class (Angelo, 2026-10-21): ids clone_&lt;class&gt; and clone_&lt;battalion&gt;_&lt;class&gt;. </summary>
	static IEnumerable<NpcDefinition> CloneVariants()
	{
		foreach ( var (cls, name) in CloneClasses.Skip( 1 ) )
			yield return CloneOf( $"clone_{cls}", $"Clone {name}", $"Clone {name}", cls, null );
		foreach ( var b in Battalions )
		{
			var tag = FactionRegistry.Get( b )?.ShortName ?? b;
			foreach ( var (cls, name) in CloneClasses )
				yield return CloneOf( $"clone_{b}_{cls}", $"{tag} Clone {name}", $"{tag} {name}", cls, b );
		}
	}

	/// <summary> A clone NPC of a class: its body, armour and class weapon (Data/Classes.cs), with NPC numbers for the gun. </summary>
	static NpcDefinition CloneOf( string id, string name, string shortName, string cls, string battalion )
	{
		var heavy = cls == "heavy";
		var sniper = cls == "specialist";
		var carbine = cls is ClassRegistry.Medic or ClassRegistry.Officer;
		return new()
		{
			Id = id, Name = name, Short = shortName, AllegianceId = AllegianceRegistry.Gar, ClassId = cls, BattalionId = battalion,
			ModelPath = "", TintHex = "#ffffff", Health = 100f, Armor = heavy ? 60f : cls == "specialist" ? 10f : 25f,
			WalkSpeed = 110f, RunSpeed = 320f, Style = NpcStyle.Rifleman,      // the s&box clips' own walk and run speeds, as players
			// the class's own gun: same model, sound and bolt as a trooper's, with the NPC damage, rate and range
			WeaponName = heavy ? "Z-6" : sniper ? "Valken-38X" : carbine ? "DC-15S" : "DC-15A", WeaponItem = heavy ? "z6" : sniper ? "valken38x" : carbine ? "dc15s" : "dc15a",
			Burst = heavy ? 14 : sniper ? 1 : carbine ? 4 : 3, BurstPause = heavy ? 1.6f : sniper ? 2.4f : 1.2f, Spread = heavy ? 4f : sniper ? 0.8f : 2.5f, Grenades = heavy ? 3 : 2,
			NpcDamage = heavy ? 12f : sniper ? 60f : carbine ? 24f : 30f, NpcFireDelay = heavy ? 0.07f : sniper ? 1.2f : carbine ? 0.18f : 0.222f, NpcRange = sniper ? 9000f : heavy || carbine ? 4500f : 6000f,
		};
	}

	public static NpcDefinition Get( string id ) => All.FirstOrDefault( n => string.Equals( n.Id, id, StringComparison.OrdinalIgnoreCase ) );
}
