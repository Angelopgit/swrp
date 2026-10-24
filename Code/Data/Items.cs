using System;
using System.Text.Json.Serialization;

namespace Swrp;

public enum ItemCategory
{
	Weapon,
	Ammo,
	Medical,
	Explosive,
	Food,
	Utility,
}

/// <summary>
/// A stack of one item type inside a character's inventory.
/// </summary>
public sealed class ItemStack
{
	public string ItemId { get; set; }
	public int Count { get; set; }

	public ItemStack() { }

	public ItemStack( string itemId, int count )
	{
		ItemId = itemId;
		Count = count;
	}

	[JsonIgnore] public ItemDefinition Definition => ItemRegistry.Get( ItemId );
}

/// <summary>
/// Static description of an item type.
/// </summary>
public sealed class ItemDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }

	/// <summary> Material icon name shown in the inventory grid. </summary>
	public string Icon { get; init; } = "inventory_2";
	public ItemCategory Category { get; init; }
	public int MaxStack { get; init; } = 1;

	/// <summary> Consumables are removed when used and run <see cref="UseMessage"/>. </summary>
	public bool Consumable { get; init; }
	public string UseMessage { get; init; }

	/// <summary> Health restored when a consumable is used. </summary>
	public float Heals { get; init; }

	/// <summary> Seconds to apply (moving away cancels). 0 = instant. </summary>
	public float UseTime { get; init; }

	/// <summary> Extra health restored gradually over <see cref="HealSeconds"/>. </summary>
	public float HealOverTime { get; init; }
	public float HealSeconds { get; init; } = 6f;

	/// <summary> Closes a bleeding wound. </summary>
	public bool StopsBleed { get; init; }

	/// <summary> Set on blasters. Null for everything that doesn't shoot. </summary>
	public WeaponStats Weapon { get; init; }

	/// <summary> Gear you can take out and hold for roleplay (a datapad, binders): it gets a quick slot like a blaster (SwrpHands). </summary>
	public bool Holdable { get; init; }
	/// <summary> Animgraph holdtype while it's held (4 = an item in one hand). </summary>
	public int HoldType { get; init; } = 4;
	/// <summary> What the trooper does with it on left click while holding it (sent as /me), e.g. "checks their datapad." </summary>
	public string HoldAction { get; init; }
}

/// <summary>
/// How a blaster shoots (built from its BlasterDefinition, the weapon base). Reloading uses one blaster cell from the
/// inventory and refills the magazine.
/// </summary>
public sealed class WeaponStats
{
	/// <summary> Move speed carrying it (1 = normal). </summary>
	public float Mobility { get; init; } = 1f;
	/// <summary> Bolts per shot (scatter blasters); Damage is per bolt. </summary>
	public int Pellets { get; init; } = 1;
	/// <summary> Full damage up to DropOffStart, easing to DropOffMin (a fraction) at DropOffEnd. </summary>
	public float DropOffStart { get; init; } = 100000f;
	public float DropOffEnd { get; init; } = 100001f;
	public float DropOffMin { get; init; } = 1f;

	/// <summary> A bolt's damage after flying this far. </summary>
	public float DamageAt( float distance )
	{
		if ( distance <= DropOffStart ) return Damage;
		var t = Math.Clamp( ( distance - DropOffStart ) / MathF.Max( 1f, DropOffEnd - DropOffStart ), 0f, 1f );
		return Damage * ( 1f + ( DropOffMin - 1f ) * t );
	}

	public float Damage { get; init; } = 20f;
	public int MagazineSize { get; init; } = 30;
	/// <summary> Seconds between shots. </summary>
	public float FireDelay { get; init; } = 0.18f;
	/// <summary> Hold the trigger to keep firing; false means one shot per click. </summary>
	public bool Automatic { get; init; } = true;
	public float ReloadTime { get; init; } = 1.6f;
	public float Range { get; init; } = 4000f;
	/// <summary> Citizen animgraph holdtype: 1 = pistol, 2 = rifle. </summary>
	public int HoldType { get; init; } = 2;
	public string BoltColorHex { get; init; } = "#1a3cff";     // saturated Republic blue (was #1e5bff)

	/// <summary> 1 = primary (key 1), 2 = sidearm (key 2). </summary>
	public int Slot { get; init; } = 1;

	/// <summary> Cone half-angle in degrees from the hip, aimed down the sights (right mouse), and extra while moving. </summary>
	public float Spread { get; init; } = 1.5f;
	public float AdsSpread { get; init; } = 0.4f;
	public float MoveSpread { get; init; } = 1.2f;

	/// <summary> Degrees the view kicks up per shot (and a random sideways share). </summary>
	public float Recoil { get; init; } = 1f;

	/// <summary> Field of view while aiming down the sights. </summary>
	public float AdsFov { get; init; } = 55f;

	/// <summary> Can switch to stun rounds (X). </summary>
	public bool CanStun { get; init; } = true;
	public float StunSeconds { get; init; } = 8f;

	public string ModelPath { get; init; }

	/// <summary>
	/// Where the model sits relative to the hand (the grip frame: forward along the barrel, up on top). Our own blaster models
	/// are built with the grip at the origin; models from elsewhere (cloud packages) need moving, turning and sizing to match.
	/// </summary>
	public Vector3 ModelOffset { get; init; }
	public Angles ModelAngles { get; init; }
	public float ModelScale { get; init; } = 1f;

	/// <summary> Put a weapon mesh object (a child of the grip frame) where this weapon's model belongs. </summary>
	/// <summary>
	/// Where every gun model sits against the hand's hold_R point (grip frame): the Battlefront II guns are built with the
	/// pistol grip's top at the origin, which put the trooper's knuckles above and behind the grip. Measured 2026-10-12 with
	/// swrp_gun_profile on the pose stage; tune live with swrp_grip_shift.
	/// </summary>
	public static Vector3 GripShift { get; set; } = new( -2.5f, 0f, 1.6f );

	[ConCmd( "swrp_grip_shift", Help = "swrp_grip_shift <x> <y> <z> - dev: move every gun against the hand (grip frame; redraw to see it)" )]
	public static void GripShiftCommand( float x, float y, float z ) => GripShift = new Vector3( x, y, z );

	/// <summary> The muzzle in the hold frame (the model's muzzle moved with <see cref="GripShift"/>). </summary>
	public Vector3 MuzzleLocal => new Vector3( MuzzleForward, 0f, MuzzleUp ) + GripShift;

	public void PlaceMesh( GameObject mesh )
	{
		mesh.LocalPosition = ModelOffset + GripShift;
		mesh.LocalRotation = ModelAngles.ToRotation();
		mesh.LocalScale = ModelScale;
	}
	public string FireSound { get; init; } = "sounds/ui/blaster_carbine.sound";

	/// <summary> Muzzle position on the model: distance forward from the grip and height above it. </summary>
	public float MuzzleForward { get; init; } = 15f;
	public float MuzzleUp { get; init; } = 1.7f;

	public Color BoltColor => Color.Parse( BoltColorHex ) ?? Color.Cyan;
}

public static class ItemRegistry
{
	/// <summary> Slots per character inventory. </summary>
	public const int InventorySlots = 24;

	// Built on first use and rebuilt after a code hotload changes it (a hotload keeps static fields, so a plain static table
	// would keep the old weapons until the editor restarts).
	static Dictionary<string, ItemDefinition> Build() => BlasterRegistry.All.Select( b => new ItemDefinition
	{
		Id = b.Id, Name = b.Name, Description = b.Description, Icon = "gps_fixed", Category = ItemCategory.Weapon, Weapon = b.ToStats(),
	} ).Concat( new List<ItemDefinition>
	{
		new() { Id = Swrp.Jedi.JediCombat.SaberItem, Name = "Lightsaber", Description = "Your own lightsaber, built when you were made a Jedi. 1 or H (or Use here) lights it and puts it away; the hilt hangs at your hip.", Icon = "flare", Category = ItemCategory.Weapon },
		new() { Id = "stun_baton", Name = "Stun Baton", Description = "Non-lethal riot control.", Icon = "bolt", Category = ItemCategory.Weapon, Holdable = true, HoldAction = "taps the stun baton against their palm." },
		new() { Id = "blaster_cell", Name = "Blaster Cell", Description = "Power cell for blasters.", Icon = "battery_charging_full", Category = ItemCategory.Ammo, MaxStack = 20 },
		new() { Id = "thermal_detonator", Name = "Thermal Detonator", Description = "Class-A thermal detonator. Take it out from the quick bar and left click, or press T (or Throw here): it goes off three seconds later. Handle with care.", Icon = "brightness_high", Category = ItemCategory.Explosive, MaxStack = 3, Holdable = true },
		new() { Id = "bacta_patch", Name = "Bacta Patch", Description = "Adhesive bacta dressing. Stops bleeding and heals 25 over a few seconds. 1.5 s to apply.", Icon = "healing", Category = ItemCategory.Medical, MaxStack = 6, Consumable = true, UseTime = 1.5f, HealOverTime = 25, HealSeconds = 6f, StopsBleed = true, UseMessage = "You press the bacta patch over the wound." },
		new() { Id = "bacta_injector", Name = "Bacta Injector", Description = "Field injector. Heals 35 at once and stops bleeding. 0.8 s to apply.", Icon = "medical_services", Category = ItemCategory.Medical, MaxStack = 5, Consumable = true, UseTime = 0.8f, Heals = 35, StopsBleed = true, UseMessage = "You inject the bacta and feel the sting fade." },
		new() { Id = "field_medkit", Name = "Field Medkit", Description = "Full trauma kit. Stops bleeding, heals 20 at once and 50 more over 10 seconds. 4 s to apply.", Icon = "medical_information", Category = ItemCategory.Medical, MaxStack = 2, Consumable = true, UseTime = 4f, Heals = 20, HealOverTime = 50, HealSeconds = 10f, StopsBleed = true, UseMessage = "You work through the medkit. Much better." },
		new() { Id = Defibrillator, Name = "Defibrillator", Description = "Medic's defibrillator. Take it out and hold E on a downed trooper: they're back on their feet at full health, wounds closed. Medics only.", Icon = "monitor_heart", Category = ItemCategory.Medical, Holdable = true, HoldAction = "charges the defibrillator paddles." },
		new() { Id = Medkit, Name = "Medkit", Description = "Medic's trauma kit. Take it out and hold E on a wounded or bleeding trooper: full health, bleeding stopped. Never runs out. Medics only.", Icon = "medical_information", Category = ItemCategory.Medical, Holdable = true, HoldAction = "opens the medkit." },
		new() { Id = "ration_pack", Name = "Ration Pack", Description = "Tastes like the inside of a helmet.", Icon = "lunch_dining", Category = ItemCategory.Food, MaxStack = 10, Consumable = true, Heals = 5, UseMessage = "You eat a ration pack. Delicious, as always." },
		new() { Id = "datapad", Name = "Datapad", Description = "Personal datapad with orders and the ship schematics.", Icon = "tablet_android", Category = ItemCategory.Utility, Holdable = true, HoldType = 0, HoldAction = "checks their datapad." },      // arms free: SwrpHands holds it up to read
		new() { Id = "binders", Name = "Binders", Description = "Restraints for prisoners.", Icon = "link", Category = ItemCategory.Utility, MaxStack = 5, Holdable = true, HoldAction = "readies a pair of binders." },
	} ).ToDictionary( x => x.Id );

	static Dictionary<string, ItemDefinition> _itemDefsCache;
	static int _itemDefsHash;

	/// <summary> Bump when the item table changes, so a running editor picks it up on hotload. </summary>
	public const string Defibrillator = "defibrillator", Medkit = "medkit";

	const int TableVersion = 13;      // 13: medic defibrillator and medkit; 12: datapad held up to read; 11: DC-17 seat; 9: DC-15A on the GC model (BlasterRegistry rebuilds on its own TableVersion)
	static Dictionary<string, ItemDefinition> _itemDefs
	{
		get
		{
			var hash = TableVersion;
			if ( _itemDefsCache is null || _itemDefsHash != hash ) { _itemDefsCache = Build(); _itemDefsHash = hash; }
			return _itemDefsCache;
		}
	}

	public static IEnumerable<ItemDefinition> All => _itemDefs.Values;

	public static ItemDefinition Get( string id ) => id is not null && _itemDefs.TryGetValue( id, out var item ) ? item : null;
}
