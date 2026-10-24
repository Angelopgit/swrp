using System;

namespace Swrp;

/// <summary>
/// The brig's blue shields (cell gates, entrance rays, fight-cage walls) and who may switch them: Captains and up, the
/// Coruscant Guard (the ship's shock troopers and military police) and staff. Every switch is logged for staff.
/// </summary>
public static class SwrpBrig
{
	public const int Rank = SwrpDoor.OfficerRank;      // Captain
	public const string Guard = "coruscant_guard";

	public static bool MayControlLocal( SwrpPlayer p ) => p.IsValid() && p.CharacterId is not null && !p.Jailed && ( p.RankIndex >= Rank || p.FactionId == Guard );

	/// <summary> Host. </summary>
	public static bool MayControl( Connection c, SwrpPlayer p ) => c is not null && Whitelist.IsStaff( c ) || MayControlLocal( p );

	public static IEnumerable<SwrpDoor> Shields( Scene scene ) => scene.GetAllComponents<SwrpDoor>().Where( d => d.IsBrigShield ).OrderBy( d => Order( d.DoorId ) );

	static int Order( string id ) => id.StartsWith( "cell" ) && int.TryParse( id[4..^5], out var n ) ? n : id.StartsWith( "brig_ray" ) ? 100 + id.Last() : 200 + id.Last();

	/// <summary> A shield is "up" while its door is closed. </summary>
	public static bool IsUp( SwrpDoor d ) => !d.IsOpen;

	/// <summary> Host: raise or lower shields, with checks and a log line. </summary>
	public static void Switch( Connection caller, SwrpPlayer p, IEnumerable<SwrpDoor> doors, bool up, string where )
	{
		if ( !MayControl( caller, p ) )
		{
			SwrpNet.NotifyConnection( caller, $"Brig shields are for {FactionRegistry.CloneRanks[Rank].Name}s and up or the {FactionRegistry.Get( Guard )?.Name}.", true );
			return;
		}
		var list = doors.Where( d => d.IsValid() && IsUp( d ) != up ).ToList();
		if ( list.Count == 0 ) return;
		foreach ( var d in list ) d.SetOpen( !up );
		var who = p.IsValid() && p.CharacterId is not null ? $"{p.ShortName} ({caller?.DisplayName})" : caller?.DisplayName;
		var what = list.Count == 1 ? list[0].DisplayName : $"{list.Count} brig shields";
		SwrpChat.StaffLog( $"{who} {( up ? "raised" : "lowered" )} {what} from {where}" );
		SwrpStaffTools.RecordText( caller, "brig shield", $"{( up ? "raised" : "lowered" )} {what} from {where}" );
	}
}

/// <summary> A button in the brig tower (or by the entrance) that raises or lowers one or more brig shields. </summary>
[Title( "SWRP Brig Shield Button" ), Category( "SWRP" ), Icon( "shield" )]
public sealed class SwrpShieldButton : Component
{
	/// <summary> Comma separated door ids. </summary>
	[Property] public string Doors { get; set; }

	public IEnumerable<SwrpDoor> Targets
	{
		get
		{
			var ids = ( Doors ?? "" ).Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
			return Scene.GetAllComponents<SwrpDoor>().Where( d => ids.Contains( d.DoorId ) );
		}
	}

	public bool AnyShieldUp => Targets.Any( SwrpBrig.IsUp );

	public string Label => Targets.ToList() is { Count: 1 } one ? $"{one[0].DisplayName} shield" : "brig shields";

	[Rpc.Host]
	public void RequestToggle()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( p.IsValid() && p.WorldPosition.Distance( WorldPosition ) > 220f ) return;
		SwrpBrig.Switch( caller, p, Targets, !AnyShieldUp, "the brig tower" );
	}
}

/// <summary>
/// The shield control console ring in the brig's first room: E opens <see cref="BrigMenu"/>, a list of every brig shield
/// with a switch each, and all-cells buttons. Placed on the ring by notorious_scene.py.
/// </summary>
[Title( "SWRP Brig Shield Control" ), Category( "SWRP" ), Icon( "admin_panel_settings" )]
public sealed class SwrpBrigConsole : Component
{
	public const float Reach = 170f;

	/// <summary> Client: the console the local trooper is next to and facing, if any. </summary>
	public static SwrpBrigConsole Facing( Scene scene, SwrpPlayer me, CameraComponent cam )
	{
		foreach ( var c in scene.GetAllComponents<SwrpBrigConsole>() )
		{
			var to = c.WorldPosition - me.WorldPosition;
			if ( to.WithZ( 0 ).Length > Reach || MathF.Abs( to.z ) > 120f ) continue;
			if ( to.WithZ( 0 ).Length > 40f && Vector3.Dot( to.WithZ( 0 ).Normal, cam.WorldRotation.Forward.WithZ( 0 ).Normal ) < 0.4f ) continue;
			return c;
		}
		return null;
	}

	/// <summary> Client: E at the ring. Anyone can look; the host refuses switches from those without access (staff always may). </summary>
	public void RequestOpen() => SwrpUiState.BrigMenu = this;

	[Rpc.Host]
	public void RequestSwitch( string doorId, bool up )
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( p.IsValid() && p.WorldPosition.Distance( WorldPosition ) > Reach + 80f ) return;
		var doors = doorId == "*cells" ? SwrpBrig.Shields( Scene ).Where( d => d.DoorId.StartsWith( "cell" ) )
			: doorId == "*all" ? SwrpBrig.Shields( Scene )
			: SwrpBrig.Shields( Scene ).Where( d => d.DoorId == doorId );
		SwrpBrig.Switch( caller, p, doors, up, "the brig shield control" );
	}
}
