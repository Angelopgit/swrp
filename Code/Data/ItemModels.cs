namespace Swrp;

/// <summary>
/// The model for an inventory item when it lies in the world (SwrpWorldItem) or is held for roleplay (SwrpHands). Blasters
/// use their own WeaponStats model instead. The thermal detonator, bacta canister and stun baton are BF2 models
/// (ModelSource/bf2_props.py, local only); the rest are original PBR props (ModelSource/hd_props.py). All are built in
/// the grip frame: x forward from the hand, z up, in map units.
/// </summary>
public static class ItemModels
{
	public const string Crate = "models/props/supply_crate.vmdl";

	static readonly Dictionary<string, string> Paths = new()
	{
		["blaster_cell"] = "models/props/blaster_cell.vmdl",
		["thermal_detonator"] = "models/props/thermal_detonator.vmdl",
		["bacta_patch"] = "models/props/bacta_patch.vmdl",
		["bacta_injector"] = "models/props/bacta_injector.vmdl",
		["field_medkit"] = "models/props/field_medkit.vmdl",
		["medkit"] = "models/props/field_medkit.vmdl",
		["defibrillator"] = "models/props/defibrillator.vmdl",
		["ration_pack"] = "models/props/ration_pack.vmdl",
		["datapad"] = "models/props/datapad.vmdl",
		["binders"] = "models/props/binders.vmdl",
		["stun_baton"] = "models/props/stun_baton.vmdl",
	};

	public static string PathFor( string itemId )
	{
		if ( itemId is null ) return null;
		if ( itemId == Swrp.Jedi.JediCombat.SaberItem ) return Swrp.Jedi.SaberHilt.DefaultModel;
		if ( ItemRegistry.Get( itemId )?.Weapon?.ModelPath is { } gun ) return gun;
		return Paths.TryGetValue( itemId, out var p ) ? p : Crate;
	}

	public static Model For( string itemId ) => PathFor( itemId ) is { } p ? Model.Load( p ) : null;
}
