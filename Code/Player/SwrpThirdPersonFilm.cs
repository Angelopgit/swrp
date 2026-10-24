using System;

namespace Swrp;

/// <summary>
/// Dev: films the first player from a third-person camera behind its shoulder (off-screen, not the main camera) every frame for a
/// while, to data/shots/tp_&lt;name&gt;_&lt;n&gt;.png, so the legs can be checked frame by frame as a player sees them.
/// </summary>
public sealed class SwrpThirdPersonFilm : Component
{
	string _name;
	float _seconds;
	int _n, _every;
	TimeSince _age;
	CameraComponent _cam;
	SwrpPlayer _p;

	/// <summary> Dev (code_run_static_method): film for <paramref name="seconds"/>, a frame every <paramref name="every"/> rendered frames. </summary>
	public static void Film( string name, float seconds = 2f, int every = 1 )
	{
		var p = Game.ActiveScene?.GetAllComponents<SwrpPlayer>().FirstOrDefault();
		if ( p is null ) return;
		var go = Game.ActiveScene.CreateObject();
		go.Name = "Third Person Film";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var f = go.AddComponent<SwrpThirdPersonFilm>();
		f._name = name; f._seconds = seconds; f._every = Math.Max( 1, every ); f._p = p; f._age = 0;
		f._cam = go.AddComponent<CameraComponent>();
		f._cam.IsMainCamera = false;
		f._cam.Priority = -100;
		f._cam.FieldOfView = 60f;
		f._cam.ZNear = 1f;
	}

	/// <summary> Dev: one third-person shot of the first player right now (RenderToBitmap repeated every frame returned one frozen image). </summary>
	public static void Shot( string name )
	{
		var p = Game.ActiveScene?.GetAllComponents<SwrpPlayer>().FirstOrDefault();
		if ( p is null ) return;
		var go = Game.ActiveScene.CreateObject();
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var cam = go.AddComponent<CameraComponent>();
		cam.IsMainCamera = false; cam.Priority = -100; cam.FieldOfView = 60f; cam.ZNear = 1f;
		var eye = p.Controller.EyeAngles.WithPitch( 10f ).ToRotation();
		var head = p.WorldPosition + Vector3.Up * 60f;
		go.WorldPosition = head - eye.Forward * 75f + eye.Right * 18f + Vector3.Up * 2f;
		go.WorldRotation = Rotation.LookAt( head + eye.Forward * 40f - Vector3.Up * 30f - go.WorldPosition );
		var bmp = new Bitmap( 360, 400 );
		cam.RenderToBitmap( bmp );
		FileSystem.Data.CreateDirectory( "shots" );
		FileSystem.Data.WriteAllBytes( $"shots/{name}.png", bmp.ToPng() );
		go.Destroy();
	}

	/// <summary> Dev: one shot of the <paramref name="index"/>th NPC from its left side (yaw 90) or any angle, to data/shots/&lt;name&gt;.png. </summary>
	public static string ShotNpc( string name, int index = 0, float side = 90f, float dist = 110f )
	{
		var npc = Game.ActiveScene?.GetAllComponents<SwrpNpc>().Where( n => !n.IsDead ).Skip( index ).FirstOrDefault();
		if ( npc is null ) return "no npc";
		var go = Game.ActiveScene.CreateObject();
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var cam = go.AddComponent<CameraComponent>();
		cam.IsMainCamera = false; cam.Priority = -100; cam.FieldOfView = 60f; cam.ZNear = 1f;
		var mid = npc.WorldPosition + Vector3.Up * 40f;
		var from = Rotation.FromYaw( npc.WorldRotation.Yaw() + side ).Forward;
		go.WorldPosition = mid + from * dist + Vector3.Up * 10f;
		go.WorldRotation = Rotation.LookAt( mid - go.WorldPosition );
		var bmp = new Bitmap( 400, 400 );
		cam.RenderToBitmap( bmp );
		FileSystem.Data.CreateDirectory( "shots" );
		FileSystem.Data.WriteAllBytes( $"shots/{name}.png", bmp.ToPng() );
		go.Destroy();
		return $"crouched {npc.Crouched} aimAt {npc.AimAt} pos {npc.WorldPosition} turn {SwrpEmotes.LastAimTurn:0}";
	}

	/// <summary> Dev: every clone NPC's right hand height over its head, crouched or not, and the aim pitch (a rifle lifted over the helmet shows up positive). </summary>
	public static string NpcPoses()
	{
		var s = "";
		foreach ( var n in Game.ActiveScene.GetAllComponents<SwrpNpc>().Where( n => !n.IsDead && n.NpcId == NpcRegistry.Clone ) )
		{
			var r = n.GetComponent<SkinnedModelRenderer>();
			if ( r?.GetBoneObject( "hand_R" ) is not { } h || r.GetBoneObject( "head" ) is not { } hd ) continue;
			var pitch = n.AimAt == Vector3.Zero ? 0f : Rotation.LookAt( n.AimAt - n.Eye ).Pitch();
			var dist = n.AimAt == Vector3.Zero ? 0f : n.AimAt.Distance( n.Eye );
			s += $"{(n.Crouched ? "C" : "S")} hand-head {h.WorldPosition.z - hd.WorldPosition.z:0} pitch {pitch:0} dist {dist:0} yawErr {( n.AimAt == Vector3.Zero ? 0 : Angles.NormalizeAngle( Rotation.LookAt( n.AimAt - n.Eye ).Yaw() - n.WorldRotation.Yaw() ) ):0}; ";
		}
		return s;
	}

	int _frame;
	protected override void OnPreRender()
	{
		if ( !_p.IsValid() || _age > _seconds ) { GameObject.Destroy(); return; }
		if ( _frame++ % _every != 0 ) return;
		var eye = _p.Controller.EyeAngles.WithPitch( 10f ).ToRotation();
		var head = _p.WorldPosition + Vector3.Up * 60f;
		WorldPosition = head - eye.Forward * 75f + eye.Right * 18f + Vector3.Up * 2f;
		WorldRotation = Rotation.LookAt( head + eye.Forward * 40f - Vector3.Up * 30f - WorldPosition );
		var bmp = new Bitmap( 360, 400 );
		_cam.RenderToBitmap( bmp );
		FileSystem.Data.CreateDirectory( "shots" );
		FileSystem.Data.WriteAllBytes( $"shots/tp_{_name}_{_n++:000}.png", bmp.ToPng() );
	}
}
