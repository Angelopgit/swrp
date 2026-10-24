using System;

namespace Swrp;

/// <summary>
/// GMod's camera tool: the tool gun's Camera mode places a stationary camera where you stand looking; F7 (any time, staff) or
/// right mouse with the mode out switches your view through each camera and back to your own eyes. With F8 (everything hidden)
/// it's for screenshots. Cameras are frozen staff props: the physgun moves them, the remover or U takes them back.
/// </summary>
public sealed class SwrpBuildCamera : Component { }

sealed class CameraMode : BuildToolMode
{
	public override string Id => "camera";
	public override string Name => "Camera";
	public override string Description => "Places a camera where you stand looking; F7 looks through your cameras and back";
	public override string Primary => "place camera here";
	public override string Secondary => "next camera";
	public override string Reload => "back to your eyes";

	public override void Control( BuildAim aim )
	{
		Valid = true;
		if ( Input.Pressed( "Attack1" ) ) SwrpCameraView.Place( aim.Eye, Rotation.LookAt( aim.Forward ) );
		else if ( Input.Pressed( "Attack2" ) ) SwrpCameraView.Next();
		else if ( Input.Pressed( "Reload" ) ) SwrpCameraView.Viewing = null;
	}
}

public static class SwrpCameraView
{
	const string Model = "models/dev/box.vmdl";

	/// <summary> Local: the camera you're looking through, or null for your own eyes. </summary>
	public static GameObject Viewing { get; set; }

	public static bool Active => Viewing.IsValid();

	/// <summary> Your own eyes, then each camera on the ship in turn. </summary>
	public static void Next()
	{
		var cams = Game.ActiveScene.GetAllComponents<SwrpBuildCamera>().Select( c => c.GameObject ).OrderBy( g => g.Id ).ToList();
		if ( cams.Count == 0 ) { Viewing = null; return; }
		var i = Viewing.IsValid() ? cams.IndexOf( Viewing ) : -1;
		Viewing = i + 1 < cams.Count ? cams[i + 1] : null;
	}

	/// <summary> Local, after the player's camera has moved: put it in the camera you're viewing through. </summary>
	internal static void Apply( SwrpPlayer me )
	{
		var cam = me.Scene.Camera;
		if ( !Active || !cam.IsValid() ) return;
		var t = Viewing.WorldTransform;
		cam.WorldPosition = t.Position + t.Rotation.Forward * 6f;      // just in front of its own box
		cam.WorldRotation = t.Rotation;
	}

	[Rpc.Host]
	public static void Place( Vector3 at, Rotation rotation )
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) ) return;
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !me.IsValid() || me.WorldPosition.Distance( at ) > 400f ) return;
		var mdl = Sandbox.Model.Load( Model );
		var go = SwrpBuild.CreateProp( mdl, "Camera", at, rotation, true, g =>
		{
			g.WorldScale = new Vector3( 0.2f, 0.12f, 0.12f );
			g.Components.Get<ModelRenderer>().Tint = new Color( 0.15f, 0.16f, 0.18f );
			g.AddComponent<SwrpBuildCamera>();
		} );
		if ( go is null ) return;
		SwrpBuildUndo.Add( caller, "camera", go );
		SwrpBuild.Log( caller, $"placed a camera at {SwrpShip.DeckName( at )}" );
		SwrpBuild.Notify( caller, "Camera placed · F7 to look through it" );
	}
}
