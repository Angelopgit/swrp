using System;

namespace Swrp.Jedi;

/// <summary>
/// Dev: a row of Jedi, one per lightsaber form, frozen in the same pose side by side, far below the ship where nobody walks,
/// lit and filmed by a camera of its own that never draws to anyone's screen (render it with the editor's camera screenshot
/// by the id this prints). For comparing stances and swing frames, and for timing with several sabers lit, while someone
/// is playing. Local only.
/// </summary>
public static class JediPoseStage
{
	static readonly Vector3 Spot = new( 0f, 0f, -14000f );
	const string RootName = "Saber Pose Stage";

	[ConCmd( "swrp_saber_stage", Help = "swrp_saber_stage <pose|off> [forms: all or ids,comma,separated] [view yaw] [crystal 0-6] [blade 0-2] - dev: a hidden row of Jedi, one per form, holding a pose (guard, block, lock, react, throw, cast, or left@0.4 / overhead@-0.5 ...), or demo to set them all swinging; prints the camera id" )]
	public static void StageCommand( string pose = "guard", string forms = "all", float viewYaw = 0f, int crystal = 0, int blade = 0 )
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		var old = scene.GetAllObjects( true ).FirstOrDefault( g => g.Name == RootName );
		if ( pose == "off" ) { old?.Destroy(); return; }

		var ids = forms == "all" ? SaberForm.All.Select( f => f.Id ).ToList() : forms.Split( ',', StringSplitOptions.RemoveEmptyEntries ).ToList();
		var look = new Appearance { SaberCrystal = crystal, SaberMode = blade }.ToString();
		var key = $"{string.Join( ",", ids )}|{look}|{viewYaw}";
		if ( old is not null && old.Tags.Has( "jedi_stage" ) && old.Components.Get<StageKey>() is { } k && k.Key == key )
		{
			foreach ( var s in old.Components.GetAll<JediStand>( FindMode.EverythingInDescendants ) ) Pose( s, pose );
			Log.Info( $"[SWRP] saber stage: {pose}" );
			return;
		}
		old?.Destroy();

		var root = new GameObject( true, RootName );
		root.NetworkMode = NetworkMode.Never;
		root.Flags |= GameObjectFlags.NotSaved;
		root.Tags.Add( "jedi_stage" );
		root.WorldPosition = Spot;
		root.Components.Create<StageKey>().Key = key;

		var scale = SwrpPlayer.PlayerScaleFor( scene );
		var gap = 70f * scale;
		var width = gap * ( ids.Count - 1 );

		// a floor and a soft key light, so the figures read
		var floor = new GameObject( root, true, "Floor" );
		floor.LocalPosition = Vector3.Down * 2f;
		floor.LocalScale = new Vector3( 8f, ( width + 400f ) / 50f, 0.04f );
		var fr = floor.Components.Create<ModelRenderer>();
		fr.Model = Model.Load( "models/dev/box.vmdl" );
		fr.Tint = new Color( 0.16f, 0.17f, 0.19f );
		var key1 = new GameObject( root, true, "Key Light" );
		key1.LocalPosition = new Vector3( 260f, -120f, 260f );
		var kl = key1.Components.Create<PointLight>();
		kl.LightColor = new Color( 0.9f, 0.92f, 1f ) * 1.4f;
		kl.Radius = width + 900f;

		for ( var i = 0; i < ids.Count; i++ )
		{
			var go = new GameObject( root, true, $"Stand {ids[i]}" );
			go.NetworkMode = NetworkMode.Never;
			go.LocalPosition = new Vector3( 0f, width * 0.5f - gap * i, 0f );
			go.WorldRotation = Rotation.FromYaw( 0f );
			go.WorldScale = scale;
			var r = go.Components.Create<SkinnedModelRenderer>();
			r.Model = ClassRegistry.Default.LoadModel() ?? Model.Load( "models/citizen/citizen.vmdl" );
			var m = go.Components.Create<SwrpAnimMannequin>();
			m.HoldType = 0;
			m.Faction = "jedi";
			m.Appearance = look;
			m.Enabled = false;
			var stand = go.Components.Create<JediStand>();
			stand.Mannequin = m;
			stand.FormId = ids[i];
			Pose( stand, pose );
		}

		// the camera: in front, a little high, the whole row in frame
		var camGo = new GameObject( root, true, "Stage Camera" );
		var dir = Rotation.FromYaw( viewYaw );
		var target = Spot + Vector3.Up * 45f * scale;
		var dist = MathF.Max( 170f, width * 0.95f + 110f ) * scale;
		camGo.WorldPosition = target + dir.Forward * dist + Vector3.Up * 22f;
		camGo.WorldRotation = Rotation.LookAt( target - camGo.WorldPosition );
		var cam = camGo.Components.Create<CameraComponent>();
		cam.FieldOfView = 45f;
		cam.IsMainCamera = false;
		cam.Priority = -100;
		cam.Enabled = false;
		Log.Info( $"[SWRP] saber stage: {ids.Count} forms ({string.Join( ", ", ids )}), {pose}: camera {cam.Id}" );
	}

	/// <summary> "demo" sets the whole row swinging through the test stand's routine (for timing with every saber moving). </summary>
	static void Pose( JediStand s, string pose )
	{
		s.Demo = pose == "demo";
		s.Hold = s.Demo ? null : pose;
	}

	[ConCmd( "swrp_saber_forms", Help = "dev: print every lightsaber form's numbers as loaded (assets or built-in)" )]
	public static void FormsCommand()
	{
		foreach ( var f in SaberForm.All )
			Log.Info( $"[SWRP] form {f.Id} ({( string.IsNullOrEmpty( f.ResourcePath ) ? "built-in" : f.ResourcePath )}): tempo {f.Tempo} dmg {f.Damage} cost {f.FocusCost} block {f.BlockCost} parry {f.ParryWindow} reach {f.Reach} tilt {f.CutTilt} wrist {f.WristLead} drive {f.BodyDrive} stance {f.StanceWidth}/{f.StanceDepth}/{f.StanceTurn}" );
	}

	/// <summary> Remembers what a stage was built for, so a new pose only re-poses it. </summary>
	public sealed class StageKey : Component
	{
		public string Key { get; set; }
	}
}
