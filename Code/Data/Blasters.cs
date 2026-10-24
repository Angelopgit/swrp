using System;

namespace Swrp;

/// <summary>
/// The weapon base: every blaster in the game, Republic and Separatist, is one entry in <see cref="BlasterRegistry"/>,
/// written in the stats players compare. Adding a gun is adding an entry (and its model, ModelSource/bf2_props.py for the BF2 ones):
/// it becomes an inventory item, a Q-menu weapon, and something an NPC can carry (NpcDefinition.WeaponItem), and every
/// shot from it, by a trooper or a droid, goes through the same rules.
///
/// The stats, in the units on the weapon card:
/// - Accuracy: the cone (degrees) bolts land in aimed down the sights. Lower is better.
/// - Hip fire: the cone without aiming. Lower is better.
/// - Fire rate: rounds a minute. Higher fires faster (a semi-automatic still needs a click per shot).
/// - Mobility: how fast you move carrying it, in percent of normal. Higher is faster.
/// - Damage: per bolt; a scatter blaster's damage is per pellet (Pellets bolts a shot).
/// - Stability: recoil, in percent of the standard kick. Lower kicks less.
/// - Drop-off: full damage up to DropOffStart, falling to DropOffMin (percent) at DropOffEnd and beyond. Long guns barely drop.
/// </summary>
public sealed class BlasterDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	/// <summary> republic or separatist. </summary>
	public string Side { get; init; } = "republic";

	public float Accuracy { get; init; } = 0.4f;
	public float HipFire { get; init; } = 1.5f;
	public float FireRate { get; init; } = 300f;
	public float Mobility { get; init; } = 100f;
	public float Damage { get; init; } = 20f;
	public int Pellets { get; init; } = 1;
	public float Stability { get; init; } = 50f;
	public float DropOffStart { get; init; } = 1500f;
	public float DropOffEnd { get; init; } = 4000f;
	public float DropOffMin { get; init; } = 50f;

	public bool Automatic { get; init; } = true;
	public int Magazine { get; init; } = 30;
	public float ReloadTime { get; init; } = 1.6f;
	public float Range { get; init; } = 4000f;
	/// <summary> Extra cone while moving. </summary>
	public float MoveSpread { get; init; } = 1.2f;
	public float AdsFov { get; init; } = 55f;
	public bool CanStun { get; init; } = true;
	/// <summary> 1 = pistol, 2 = rifle. </summary>
	public int HoldType { get; init; } = 2;
	/// <summary> 1 = primary (key 1), 2 = sidearm (key 2). </summary>
	public int Slot { get; init; } = 1;
	public string Bolt { get; init; } = "#1a3cff";     // saturated Republic blue (was #1e5bff, read too cyan; Angelo 2026-10-12)
	public string Sound { get; init; } = "sounds/ui/blaster_carbine.sound";

	/// <summary> The model, its placement in the grip frame, and the muzzle. </summary>
	public string Model { get; init; }
	public Vector3 ModelOffset { get; init; }
	public Angles ModelAngles { get; init; }
	public float MuzzleForward { get; init; } = 15f;
	public float MuzzleUp { get; init; } = 1.7f;

	/// <summary> The standard kick (degrees) a 100% stability gun has. </summary>
	public const float StandardRecoil = 2.4f;

	public WeaponStats ToStats() => new()
	{
		Damage = Damage, MagazineSize = Magazine, FireDelay = 60f / MathF.Max( 1f, FireRate ), Automatic = Automatic, ReloadTime = ReloadTime,
		Range = Range, HoldType = HoldType, BoltColorHex = Bolt, Slot = Slot, Spread = HipFire, AdsSpread = Accuracy, MoveSpread = MoveSpread,
		Recoil = StandardRecoil * Stability / 100f, AdsFov = AdsFov, CanStun = CanStun, ModelPath = Model, ModelOffset = ModelOffset,
		ModelAngles = ModelAngles, FireSound = SwrpAudio.Weapon( Id, Sound ), MuzzleForward = MuzzleForward, MuzzleUp = MuzzleUp,
		Mobility = Mobility / 100f, Pellets = Math.Max( 1, Pellets ), DropOffStart = DropOffStart, DropOffEnd = DropOffEnd, DropOffMin = DropOffMin / 100f,
	};
}

/// <summary>
/// TFA SWRP port (Angelo, 2026-10-12): the stats are the Garry's Mod TFA Star Wars weapons' own (Workshop 3751368978
/// "Aurora Content #2", lua/weapons/rw_sw_*.lua and at_sw_dc17m/at_sw_dc15sa): damage, RPM, magazine, pellets, auto/semi as
/// written; spread and iron accuracy converted from TFA's cone tangent to degrees (atan); recoil from KickUp (Stability =
/// KickUp / StandardRecoil); mobility from MoveSpeed; no damage drop-off (TFA's RangeFalloff is off); scoped guns (TFA RT
/// scopes) get a scope FOV from ScopeZoom, the rest TFA's IronFOV. Reload times are ours (TFA times them by animation).
/// Sounds: each gun's TFA fire sound (Assets/sounds/weapons_ext, local only). Models: BF2's own where it has the gun
/// (ModelSource/bf2_props.py), otherwise the TFA model (ModelSource/tfa_props.py, local only). DT-57 isn't in TFA: unchanged.
/// </summary>
public static class BlasterRegistry
{
	// Rebuilt when TableVersion changes: a code hotload keeps static fields, so a plain static list kept the old guns (and their
	// old fire rates) until the editor restarted. Bump TableVersion with any change below (and ItemRegistry's).
	const int TableVersion = 5;      // 4: DC-15A model offset onto the hand; 5: DC-17 handle into the hand
	// the BF2 DC-17's handle sat behind and above the pistol hold's fingers (swrp_gun_profile dc17, 2026-10-18)
	static List<BlasterDefinition> _all;
	static int _allVersion;
	public static IReadOnlyList<BlasterDefinition> All
	{
		get
		{
			if ( _all is null || _allVersion != TableVersion ) { _all = Build(); _allVersion = TableVersion; }
			return _all;
		}
	}

	static List<BlasterDefinition> Build() => new List<BlasterDefinition>
	{
		// ----- Republic -----
		new()
		{
			Id = "dc15a", Name = "DC-15A Rifle", Description = "The clone trooper's standard blaster rifle. Fast, steady and accurate down the sights.",
			Accuracy = 0.286f, HipFire = 0.86f, FireRate = 650f, Mobility = 100f, Damage = 20f, Stability = 5.0f,
			Automatic = true, Magazine = 64, ReloadTime = 2.0f, Range = 6000f, AdsFov = 75f, HoldType = 2, Slot = 1, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/gc_dc15a.vmdl", ModelOffset = new Vector3( -1.8f, 0f, -0.9f ), MuzzleForward = 35.2f, MuzzleUp = 2.4f,      // offset: the GC grip sits 1.8 forward and 0.9 higher than the BF2 guns GripShift was set for (2026-10-14)
			     // Galactic Contention model (ModelSource/gc_weapons.py; local only): the BF2 one rendered near-black with white seams (2026-10-14)
		},
		new()
		{
			Id = "dc15le", Name = "DC-15LE Rifle", Description = "Long-barrelled DC-15 with a magnified sight. Harder hitting, slower firing.",
			Accuracy = 0.057f, HipFire = 0.69f, FireRate = 450f, Mobility = 100f, Damage = 30f, Stability = 2.3f,
			Automatic = true, Magazine = 45, ReloadTime = 2.2f, Range = 14000f, AdsFov = 26f, HoldType = 2, Slot = 1, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 14000f, DropOffEnd = 28000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_dc15le.vmdl", MuzzleForward = 30.4f, MuzzleUp = 1.8f,
		},
		new()
		{
			Id = "dc15s", Name = "DC-15S Carbine", Description = "Compact clone carbine. Made for close quarters and boarding actions.",
			Accuracy = 0.286f, HipFire = 1.15f, FireRate = 375f, Mobility = 100f, Damage = 30f, Stability = 4.2f,
			Automatic = true, Magazine = 32, ReloadTime = 1.8f, Range = 6000f, AdsFov = 75f, HoldType = 2, Slot = 1, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_dc15a.vmdl", MuzzleForward = 15.8f, MuzzleUp = 1.2f,
		},
		new()
		{
			Id = "dc15se", Name = "DC-15SE Carbine", Description = "Scoped DC-15 carbine variant. Heavier bolts at a slower rate.",
			Accuracy = 0.229f, HipFire = 1.15f, FireRate = 275f, Mobility = 100f, Damage = 35f, Stability = 4.2f,
			Automatic = true, Magazine = 25, ReloadTime = 2.0f, Range = 14000f, AdsFov = 30f, HoldType = 2, Slot = 1, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 14000f, DropOffEnd = 28000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/tfa_dc15se.vmdl", MuzzleForward = 16.7f, MuzzleUp = -0.9f,
		},
		new()
		{
			Id = "dc15x", Name = "DC-15X Sniper Rifle", Description = "Marksman's rifle. One devastating bolt at a time, down a long scope.",
			Accuracy = 0.006f, HipFire = 1.43f, FireRate = 75f, Mobility = 100f, Damage = 145f, Stability = 5.0f,
			Automatic = false, Magazine = 6, ReloadTime = 2.6f, Range = 14000f, AdsFov = 18f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#1a3cff",
			DropOffStart = 14000f, DropOffEnd = 28000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_dc15le.vmdl", MuzzleForward = 30.4f, MuzzleUp = 1.8f,
		},
		new()
		{
			Id = "dc17", Name = "DC-17 Pistol", Description = "Officer and specialist sidearm. One shot per trigger pull.",
			Accuracy = 0.286f, HipFire = 1.43f, FireRate = 325f, Mobility = 100f, Damage = 30f, Stability = 14.6f,
			Automatic = false, Magazine = 20, ReloadTime = 1.3f, Range = 3000f, AdsFov = 75f, HoldType = 1, Slot = 2, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 3000f, DropOffEnd = 6000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_pistol.sound", Model = "models/weapons/bf2_dc17.vmdl", ModelOffset = new Vector3( 2f, 0f, -1f ), MuzzleForward = 7.0f, MuzzleUp = 0.5f,
		},
		new()
		{
			Id = "dc17ext", Name = "DC-17 Extended", Description = "DC-17 with an extended barrel and a bigger cell.",
			Accuracy = 0.286f, HipFire = 1.15f, FireRate = 355f, Mobility = 100f, Damage = 30f, Stability = 4.2f,
			Automatic = false, Magazine = 26, ReloadTime = 1.4f, Range = 3000f, AdsFov = 75f, HoldType = 1, Slot = 2, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 3000f, DropOffEnd = 6000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_pistol.sound", Model = "models/weapons/bf2_dc17_ext.vmdl", ModelOffset = new Vector3( 2f, 0f, -1f ), MuzzleForward = 8.42f, MuzzleUp = 1.7f,
		},
		new()
		{
			Id = "dc17s", Name = "DC-17S Auto Pistol", Description = "Fully automatic DC-17 variant. Empties its cell fast.",
			Accuracy = 0.286f, HipFire = 1.43f, FireRate = 385f, Mobility = 100f, Damage = 35f, Stability = 8.3f,
			Automatic = true, Magazine = 36, ReloadTime = 1.4f, Range = 3000f, AdsFov = 75f, HoldType = 1, Slot = 2, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 3000f, DropOffEnd = 6000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_pistol.sound", Model = "models/weapons/tfa_dc17s.vmdl", MuzzleForward = 15.3f, MuzzleUp = 0.1f,
		},
		new()
		{
			Id = "dc19", Name = "DC-19 Stealth Carbine", Description = "Shadow trooper carbine. Dark, suppressed bolts that hit very hard.",
			Accuracy = 0.229f, HipFire = 1.15f, FireRate = 275f, Mobility = 100f, Damage = 90f, Stability = 4.2f,
			Automatic = true, Magazine = 25, ReloadTime = 1.9f, Range = 6000f, AdsFov = 75f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#4b2bbf",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_dc15a.vmdl", MuzzleForward = 15.8f, MuzzleUp = 1.2f,
		},
		new()
		{
			Id = "dc19le", Name = "DC-19LE Marksman Carbine", Description = "Scoped DC-19. Slow, crushing single shots.",
			Accuracy = 0.006f, HipFire = 1.15f, FireRate = 155f, Mobility = 100f, Damage = 165f, Stability = 4.2f,
			Automatic = false, Magazine = 15, ReloadTime = 2.2f, Range = 14000f, AdsFov = 24f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#4b2bbf",
			DropOffStart = 14000f, DropOffEnd = 28000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_dc15a.vmdl", MuzzleForward = 15.8f, MuzzleUp = 1.2f,
		},
		new()
		{
			Id = "dc17m", Name = "DC-17M Commando Rifle", Description = "Republic Commando's modular rifle in its blaster configuration.",
			Accuracy = 0.172f, HipFire = 1.15f, FireRate = 530f, Mobility = 100f, Damage = 40f, Stability = 4.2f,
			Automatic = true, Magazine = 50, ReloadTime = 2.0f, Range = 6000f, AdsFov = 75f, HoldType = 2, Slot = 1, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_dc17m.vmdl", MuzzleForward = 16.39f, MuzzleUp = 3.39f,
		},
		new()
		{
			Id = "dc15sa", Name = "DC-15SA Sidearm", Description = "Compact DC-15 side arm. Slow but hard hitting for a pistol.",
			Accuracy = 0.229f, HipFire = 1.26f, FireRate = 255f, Mobility = 100f, Damage = 40f, Stability = 4.2f,
			Automatic = false, Magazine = 20, ReloadTime = 1.4f, Range = 3000f, AdsFov = 75f, HoldType = 1, Slot = 2, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 3000f, DropOffEnd = 6000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_pistol.sound", Model = "models/weapons/tfa_dc15sa.vmdl", MuzzleForward = 7.1f, MuzzleUp = 0.9f,
		},
		new()
		{
			Id = "dp23", Name = "DP-23 Scatter Blaster", Description = "Close-range scatter blaster: eight bolts a shot. Brutal up close, useless at range.",
			Accuracy = 4.004f, HipFire = 4.57f, FireRate = 135f, Mobility = 100f, Damage = 20f, Stability = 87.5f, Pellets = 8,
			Automatic = false, Magazine = 8, ReloadTime = 2.4f, Range = 6000f, AdsFov = 75f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#1a3cff",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/tfa_dp23.vmdl", MuzzleForward = 20.3f, MuzzleUp = 0.8f,
		},
		new()
		{
			Id = "dp24", Name = "DP-24 Rifle", Description = "Long-barrelled clone rifle. Steady automatic fire.",
			Accuracy = 0.229f, HipFire = 1.72f, FireRate = 425f, Mobility = 100f, Damage = 30f, Stability = 6.2f,
			Automatic = true, Magazine = 35, ReloadTime = 2.0f, Range = 6000f, AdsFov = 75f, HoldType = 2, Slot = 1, CanStun = true, Bolt = "#1a3cff",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/tfa_dp24.vmdl", MuzzleForward = 21.9f, MuzzleUp = -0.2f,
		},
		new()
		{
			Id = "westarm5", Name = "Westar-M5 Rifle", Description = "ARC trooper's scoped blaster rifle. Heavy, accurate automatic fire.",
			Accuracy = 0.029f, HipFire = 0.69f, FireRate = 435f, Mobility = 100f, Damage = 50f, Stability = 0.6f,
			Automatic = true, Magazine = 54, ReloadTime = 2.2f, Range = 6000f, AdsFov = 30f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#1a3cff",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/tfa_westarm5.vmdl", MuzzleForward = 9.1f, MuzzleUp = 3.9f,
		},
		new()
		{
			Id = "valken38x", Name = "Valken-38X Sniper", Description = "Long-range scoped rifle with a high-magnification sight.",
			Accuracy = 0.057f, HipFire = 0.86f, FireRate = 165f, Mobility = 100f, Damage = 95f, Stability = 12.5f,
			Automatic = true, Magazine = 10, ReloadTime = 2.4f, Range = 14000f, AdsFov = 15f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#1a3cff",
			DropOffStart = 14000f, DropOffEnd = 28000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_valken38x.vmdl", MuzzleForward = 27.3f, MuzzleUp = 3.2f,
		},
		new()
		{
			Id = "z6", Name = "Z-6 Rotary Blaster", Description = "Heavy repeating blaster. A huge cell and a torrent of bolts, far faster than a DC-15A.",     // 1100 RPM (TFA had 300; raised for Angelo 2026-10-12)
			Accuracy = 1.146f, HipFire = 1.15f, FireRate = 1100f, Mobility = 100f, Damage = 20f, Stability = 2.1f,
			Automatic = true, Magazine = 250, ReloadTime = 3.2f, Range = 6000f, AdsFov = 75f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#2a50ff",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_z6.vmdl", MuzzleForward = 36.7f, MuzzleUp = 3.4f,
		},

		// ----- Separatist (droids carry these; staff can spawn them) -----
		new()
		{
			Id = "e5", Name = "E-5 Blaster Rifle", Side = "separatist", Description = "The battle droid's standard rifle. Cheap, but it hits hard.",
			Accuracy = 0.286f, HipFire = 0.86f, FireRate = 300f, Mobility = 90f, Damage = 35f, Stability = 12.5f,
			Automatic = true, Magazine = 25, ReloadTime = 2.0f, Range = 6000f, AdsFov = 78f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_e5.vmdl", MuzzleForward = 13.5f, MuzzleUp = 2.3f,
		},
		new()
		{
			Id = "e5bx", Name = "E-5BX Blaster Rifle", Side = "separatist", Description = "Commando droid E-5 variant. A little harder hitting.",
			Accuracy = 0.286f, HipFire = 0.86f, FireRate = 320f, Mobility = 90f, Damage = 38f, Stability = 12.5f,
			Automatic = true, Magazine = 30, ReloadTime = 2.0f, Range = 6000f, AdsFov = 78f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_e5.vmdl", MuzzleForward = 13.5f, MuzzleUp = 2.3f,
		},
		new()
		{
			Id = "e5c", Name = "E-5C Heavy Repeater", Side = "separatist", Description = "Heavy droid repeater. Fast automatic fire from a big cell.",
			Accuracy = 0.286f, HipFire = 1.06f, FireRate = 650f, Mobility = 85f, Damage = 20f, Stability = 6.2f,
			Automatic = true, Magazine = 50, ReloadTime = 2.6f, Range = 6000f, AdsFov = 78f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_e5c.vmdl", MuzzleForward = 21.7f, MuzzleUp = 1.8f,
		},
		new()
		{
			Id = "e5s", Name = "E-5S Sniper Rifle", Side = "separatist", Description = "Droid marksman rifle. Slow, heavy bolts from far away.",
			Accuracy = 0.006f, HipFire = 2.00f, FireRate = 110f, Mobility = 85f, Damage = 135f, Stability = 8.3f,
			Automatic = false, Magazine = 5, ReloadTime = 2.6f, Range = 14000f, AdsFov = 20f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 14000f, DropOffEnd = 28000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/bf2_e5s.vmdl", MuzzleForward = 38.8f, MuzzleUp = 2.0f,
		},
		new()
		{
			Id = "rg4d", Name = "RG-4D Pistol", Side = "separatist", Description = "Scoped Separatist blaster pistol.",
			Accuracy = 0.143f, HipFire = 1.43f, FireRate = 325f, Mobility = 100f, Damage = 35f, Stability = 14.6f,
			Automatic = false, Magazine = 20, ReloadTime = 1.4f, Range = 14000f, AdsFov = 28f, HoldType = 1, Slot = 2, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 14000f, DropOffEnd = 28000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_pistol.sound", Model = "models/weapons/bf2_rg4d.vmdl", MuzzleForward = 13.3f, MuzzleUp = 2.2f,
		},
		new()
		{
			Id = "se14", Name = "SE-14 Pistol", Side = "separatist", Description = "Rapid-fire Separatist blaster pistol.",
			Accuracy = 0.286f, HipFire = 0.86f, FireRate = 240f, Mobility = 90f, Damage = 30f, Stability = 12.5f,
			Automatic = true, Magazine = 30, ReloadTime = 1.4f, Range = 3000f, AdsFov = 78f, HoldType = 1, Slot = 2, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 3000f, DropOffEnd = 6000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_pistol.sound", Model = "models/weapons/tfa_se14.vmdl", MuzzleForward = 7.6f, MuzzleUp = 0.8f,
		},
		new()
		{
			Id = "se14c", Name = "SE-14C Pistol", Side = "separatist", Description = "Scoped SE-14 with a burst of speed.",
			Accuracy = 0.286f, HipFire = 0.86f, FireRate = 240f, Mobility = 90f, Damage = 30f, Stability = 8.3f,
			Automatic = true, Magazine = 30, ReloadTime = 1.4f, Range = 3000f, AdsFov = 30f, HoldType = 1, Slot = 2, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 3000f, DropOffEnd = 6000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_pistol.sound", Model = "models/weapons/bf2_se14c.vmdl", MuzzleForward = 7.7f, MuzzleUp = 1.5f,
		},
		new()
		{
			Id = "sg6", Name = "SG-6 Scatter Blaster", Side = "separatist", Description = "Separatist shotgun blaster: six bolts a shot.",
			Accuracy = 2.862f, HipFire = 4.00f, FireRate = 120f, Mobility = 85f, Damage = 25f, Stability = 50.0f, Pellets = 6,
			Automatic = false, Magazine = 6, ReloadTime = 2.4f, Range = 6000f, AdsFov = 78f, HoldType = 2, Slot = 1, CanStun = false, Bolt = "#ff3b2f",
			DropOffStart = 6000f, DropOffEnd = 12000f, DropOffMin = 100f, Sound = "sounds/ui/blaster_carbine.sound", Model = "models/weapons/tfa_sg6.vmdl", MuzzleForward = 21.0f, MuzzleUp = 1.7f,
		},
		new()
		{
			Id = "dt57", Name = "DT-57 Blaster Pistol", Side = "separatist", Description = "A Separatist officer's heavy pistol.",
			Accuracy = 0.5f, HipFire = 1.6f, FireRate = 300f, Mobility = 105f, Damage = 22f, Stability = 75f, DropOffStart = 900f, DropOffEnd = 2500f, DropOffMin = 45f,
			Automatic = false, Magazine = 15, ReloadTime = 1.3f, Range = 3000f, AdsFov = 60f, HoldType = 1, Slot = 2, Bolt = "#ff3b2f",
			Sound = "sounds/ui/blaster_pistol.sound", MuzzleForward = 9f, MuzzleUp = 1.5f,
		},
	};

	public static BlasterDefinition Get( string id ) => All.FirstOrDefault( b => b.Id == id );
}
