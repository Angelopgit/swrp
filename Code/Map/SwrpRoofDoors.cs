using System;

namespace Swrp;

/// <summary>
/// The main hangar's topside doors: the map's own dorsal roof doors (VMF "roofdoor*", working SwrpDoors from
/// MapSource/notorious_doors.py, console-only) slide open so craft fly straight up and out, driven from the button in the
/// main hangar control room. Open or shut is synced from the host (SwrpShip.RoofOpen); the host keeps every roof door in step
/// with it. Once open, a translucent blue atmosphere shield fills the opening, like the films' hangar mouths: craft fly
/// straight through it (no collision).
/// </summary>
public static class SwrpRoofDoors
{
	/// <summary> The map's control button for them, in the main hangar control room. </summary>
	public static readonly Vector3 ButtonAt = new( 146f, 991f, -784f );

	public static bool IsRoofDoor( SwrpDoor d ) => d.IsValid() && d.DoorId is { } id && id.StartsWith( "roofdoor", StringComparison.OrdinalIgnoreCase );

	/// <summary> Part of a topside door (they open, so they don't count as a ceiling over a hangar pad). </summary>
	public static bool IsDoor( GameObject go ) => go.IsValid() && IsRoofDoor( go.Components.GetInAncestorsOrSelf<SwrpDoor>() );

	static Scene _scene;
	static GameObject _control, _shield;
	static bool _shieldShown;

	/// <summary> Every frame (from SwrpShip): host keeps the doors in step; every machine builds the control and the shield. </summary>
	public static void Tick( Scene scene )
	{
		if ( scene is null ) return;
		var ship = SwrpShip.Current;
		var open = ship?.RoofOpen == true;
		var doors = scene.GetAllComponents<SwrpDoor>().Where( IsRoofDoor ).ToList();
		if ( doors.Count == 0 ) return;
		if ( _scene != scene || !_control.IsValid() )
		{
			_scene = scene;
			Build( scene, doors );
		}
		if ( Networking.IsHost )
			foreach ( var d in doors )
				if ( d.IsOpen != open ) d.SetOpen( open );
		var show = open && doors.All( d => d.IsOpen );
		if ( _shield.IsValid() && show != _shieldShown )
		{
			_shieldShown = show;
			_shield.Enabled = show;
		}
	}

	static void Build( Scene scene, List<SwrpDoor> doors )
	{
		_control?.Destroy();
		_shield?.Destroy();
		_control = scene.CreateObject();
		_control.Name = "Topside Hangar Door Control";
		_control.NetworkMode = NetworkMode.Never;
		_control.Flags |= GameObjectFlags.NotSaved;
		_control.WorldPosition = ButtonAt;
		_control.Components.Create<BoxCollider>().Scale = new Vector3( 20f );
		_control.Components.Create<SwrpRoofControl>();

		// the shield: a thin glowing pane across the doors' closed footprint, a little below them
		var bounds = doors.Select( d => d.GameObject.GetBounds() ).Aggregate( ( a, b ) => a.AddBBox( b ) );
		_shield = scene.CreateObject();
		_shield.Name = "Topside Hangar Shield";
		_shield.NetworkMode = NetworkMode.Never;
		_shield.Flags |= GameObjectFlags.NotSaved;
		_shield.WorldPosition = bounds.Center.WithZ( bounds.Mins.z - 4f );
		_shield.WorldScale = new Vector3( bounds.Size.x / 50f, bounds.Size.y / 50f, 0.02f );
		var r = _shield.Components.Create<ModelRenderer>();
		r.Model = Model.Load( "models/dev/box.vmdl" );
		r.MaterialOverride = Material.Load( "materials/fx/shield.vmat" );
		r.Tint = new Color( 0.35f, 0.65f, 1f, 0.5f );
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		_shield.Enabled = false;
		_shieldShown = false;
	}

	/// <summary> Client: the control room button. </summary>
	[Rpc.Host]
	public static void RequestToggle()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		var staff = caller is not null && Whitelist.IsStaff( caller );
		if ( !staff && ( !p.IsValid() || p.CharacterId is null || p.Jailed ) ) { SwrpNet.NotifyConnection( caller, "Access denied.", true ); return; }
		if ( p.IsValid() && p.WorldPosition.Distance( ButtonAt ) > 260f ) { SwrpNet.NotifyConnection( caller, "You are too far away.", true ); return; }
		if ( SwrpShip.Current is not { } ship ) return;
		ship.RoofOpen = !ship.RoofOpen;
		var who = p.IsValid() ? $"{p.Rank.Abbreviation} {p.CharacterName}" : caller?.DisplayName;
		SwrpChat.SendTo( Connection.All, ChatKind.System, $"Attention: topside hangar doors {( ship.RoofOpen ? "opening" : "closing" )}. ({who})" );
	}

	[ConCmd( "swrp_roofdoors", Help = "swrp_roofdoors [0/1] - host: open or shut the main hangar's topside doors (no argument toggles)" )]
	public static void Command( int open = -1 )
	{
		if ( !Networking.IsHost || SwrpShip.Current is not { } ship ) return;
		ship.RoofOpen = open < 0 ? !ship.RoofOpen : open != 0;
		Log.Info( $"[SWRP] topside hangar doors {( ship.RoofOpen ? "open" : "shut" )}" );
	}
}

public sealed class SwrpRoofControl : Component, ISwrpUsable
{
	public (string Title, string Sub, string Tone) Prompt( SwrpPlayer me ) =>
		( "Topside hangar doors", SwrpShip.Current?.RoofOpen == true ? "Close the topside hangar doors" : "Open the topside hangar doors", "info" );

	public void Use( SwrpPlayer me ) => SwrpRoofDoors.RequestToggle();
}
