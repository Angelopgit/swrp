namespace Swrp;

/// <summary> Dev check: what a weapon's live stats are in this session (after hotloads), e.g. <c>swrp_weapon_info z6</c>. </summary>
public static class WeaponInfoCommand
{
	[ConCmd( "swrp_weapon_info", Help = "Print a blaster's live stats: swrp_weapon_info <id>" )]
	public static void Info( string id )
	{
		var w = ItemRegistry.Get( id )?.Weapon;
		if ( w is null ) { Log.Info( $"[SWRP] no weapon '{id}'" ); return; }
		Log.Info( $"[SWRP] {id}: {60f / w.FireDelay:0} RPM (delay {w.FireDelay:0.###} s), damage {w.Damage}, mag {w.MagazineSize}, auto {w.Automatic}, spread {w.Spread}/{w.AdsSpread}, recoil {w.Recoil:0.###}, reload {w.ReloadTime}" );
	}
}
