using System;
using System.Text.Json;

namespace Swrp;

/// <summary>
/// Dev: records the pose stage's skeleton (key joints in the stage's own frame, every frame) for a few seconds and writes it to
/// data/anim_rec/&lt;name&gt;.json, to score our clone against Garry's Mod's own keyframes offline (scratchpad anim_score.py).
/// </summary>
public sealed class SwrpPoseRecorder : Component
{
	static readonly string[] Joints = { "pelvis", "spine_2", "neck_0", "head", "arm_upper_L", "arm_lower_L", "hand_L", "arm_upper_R", "arm_lower_R",
		"hand_R", "hold_R", "leg_upper_L", "leg_lower_L", "ankle_L", "ball_L", "leg_upper_R", "leg_lower_R", "ankle_R", "ball_R" };

	public string Name { get; set; }
	public float Seconds { get; set; } = 3f;
	readonly List<object> _frames = new();
	TimeSince _age;
	SwrpPlayer _pl;

	[ConCmd( "swrp_pose_record", Help = "swrp_pose_record <name> [seconds] - dev: record the pose stage's joints to data/anim_rec/<name>.json" )]
	public static void Record( string name, float seconds = 3f )
	{
		var stage = Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>().FirstOrDefault( m => m.GameObject.Name == "Pose Stage" );
		if ( stage is null ) { Log.Info( "[SWRP] no pose stage" ); return; }
		var rec = stage.GameObject.Components.Create<SwrpPoseRecorder>();
		rec.Name = name;
		rec.Seconds = seconds;
		rec._age = 0;
	}

	/// <summary> Dev: the first player's body instead of the stage, while it walks (DevWalk), with its model and move parameters. </summary>
	public static void RecordPlayer( string name, float seconds = 3f )
	{
		var p = Game.ActiveScene?.GetAllComponents<SwrpPlayer>().FirstOrDefault();
		if ( p?.Body is null ) { Log.Info( "[SWRP] no player" ); return; }
		var rec = p.Body.GameObject.Components.Create<SwrpPoseRecorder>();
		rec.Name = name;
		rec.Seconds = seconds;
		rec._age = 0;
	}

	/// <summary> Dev: walk the first player straight ahead for a while (input off), sprinting or not, then hand input back. </summary>
	public static void DevWalk( float speed, float seconds, bool sprint = false )
	{
		var p = Game.ActiveScene?.GetAllComponents<SwrpPlayer>().FirstOrDefault();
		if ( p?.Controller is null ) return;
		_walk = (p, speed, sprint);
		_walkUntil = seconds;
		p.Controller.UseInputControls = false;
	}
	static (SwrpPlayer P, float Speed, bool Sprint)? _walk;
	// (expires on its own: a recorder that finished first never cleared it, and every player's input stayed off, 2026-10-19)
	public static bool Walking => _walk is not null && !_walkUntil;
	static RealTimeUntil _walkUntil;

	/// <summary>
	/// Dev: a scripted 14 s run (2 s each): straight at 200, turning at 120 deg/s, strafing right, sprinting at 400, sprint
	/// toggled every half second, starting and stopping every half second, running backwards. Record with RecordPlayer alongside.
	/// </summary>
	public static void DevRunScript()
	{
		DevWalk( 155f, 20f );
		_script = 0;
	}
	static RealTimeSince? _script;
	static RealTimeSince? _sprintTest;

	/// <summary>
	/// Dev: Shift-run test. Sprints the whole time while the view swings like a player looking around (a slow weave, hard
	/// 90-degree turns, a 180 and a full circle), with diagonal and sideways stretches and short lets-go of the stick.
	/// </summary>
	public static void DevSprintTest( float seconds = 30f )
	{
		DevWalk( 210f, seconds );
		_sprintTest = 0;
	}

	/// <summary> Dev: the scripted run wants a sprint this frame (SwrpPlayer reads it). </summary>
	public static bool DevSprint { get; private set; }

	/// <summary> Called from the player each frame while a DevWalk runs. </summary>
	public static void TickDevWalk()
	{
		if ( _walk is not { } w || !w.P.IsValid() ) return;
		if ( _walkUntil ) { w.P.Controller.UseInputControls = true; w.P.Controller.WishVelocity = Vector3.Zero; _walk = null; _script = null; _sprintTest = null; DevSprint = false; return; }
		var c = w.P.Controller;
		if ( _sprintTest is { } st )
		{
			var tt = (float)st;
			var phase = (int)( tt / 3f ) % 10;      // 3 s each
			var yawRate = phase switch { 0 => 0f, 1 => 60f * MathF.Sin( tt * 2f ), 2 => 90f, 3 => -90f, 4 => 180f, 5 => 0f, 6 => 120f * MathF.Sin( tt * 4f ), 7 => 0f, 8 => 360f / 3f, _ => -45f };
			c.EyeAngles = c.EyeAngles.WithYaw( c.EyeAngles.yaw + yawRate * Time.Delta );
			var f2 = c.EyeAngles.WithPitch( 0 ).Forward;
			var r2 = c.EyeAngles.WithPitch( 0 ).ToRotation().Right;
			var dir = phase switch { 5 => ( f2 + r2 ).Normal, 7 => ( f2 - r2 ).Normal, _ => f2 };
			var letGo = phase == 9 && (int)( tt / 0.4f ) % 3 == 0;      // brief stops
			DevSprint = !letGo;
			c.WishVelocity = letGo ? Vector3.Zero : dir * w.Speed;
			return;
		}
		if ( _script is not { } s ) { c.WishVelocity = c.EyeAngles.WithPitch( 0 ).Forward * w.Speed; return; }
		var t = (float)s;
		var seg = (int)( t / 2f );
		var half = (int)( t / 0.5f ) % 2 == 0;
		// 14-17 s holster and draw every 0.75 s while jogging, 17-20 s the same standing still
		if ( seg >= 7 && w.P.GetComponent<SwrpBlaster>() is { } bl )
		{
			var drawn = (int)( t / 0.75f ) % 2 == 0;
			if ( bl.Drawn != drawn ) bl.SetDrawn( drawn );
		}
		if ( seg == 1 ) c.EyeAngles = c.EyeAngles.WithYaw( c.EyeAngles.yaw + 120f * Time.Delta );
		var fwd = c.EyeAngles.WithPitch( 0 ).Forward;
		var right = c.EyeAngles.WithPitch( 0 ).ToRotation().Right;
		DevSprint = seg == 1 || seg == 3 || seg == 4 && half;      // (turning while sprinting too)
		c.WishVelocity = seg switch
		{
			2 => right * 155f,
			1 or 3 => fwd * 210f,
			4 => fwd * ( half ? 210f : 155f ),
			5 => half ? fwd * 155f : Vector3.Zero,
			6 => -fwd * 155f,
			7 => fwd * 155f,
			8 or 9 => Vector3.Zero,
			_ => fwd * 155f,
		};
	}

	protected override void OnUpdate()
	{
		TickDevWalk();
		var r = GetComponent<SkinnedModelRenderer>();
		if ( r?.Model is null ) { Destroy(); return; }
		_pl ??= Components.GetInAncestors<SwrpPlayer>();
		var root = r.WorldTransform;
		var f = new Dictionary<string, float[]> { ["t"] = new[] { (float)_age },
			["params"] = new[] { r.Parameters.GetFloat( "move_x" ), r.Parameters.GetFloat( "move_y" ), r.Parameters.GetFloat( "move_groundspeed" ), r.PlaybackRate, r.Parameters.GetFloat( "duck" ), r.Model.ResourcePath.GetHashCode() % 1000, r.WorldRotation.Yaw(), r.WorldPosition.x, r.WorldPosition.y,
				_pl?.Controller?.EyeAngles.yaw ?? 0f, _pl?.Controller?.Velocity.x ?? 0f, _pl?.Controller?.Velocity.y ?? 0f, _pl?.Sprinting == true ? 1f : 0f,
				( _pl?.GmodSet ?? "" ).GetHashCode() % 1000, r.Parameters.GetFloat( "wish_x" ), r.Parameters.GetFloat( "wish_y" ) } };
		foreach ( var j in Joints )
		{
			var bone = r.Model.Bones.GetBone( j );
			if ( bone is null || !r.TryGetBoneTransform( in bone, out var tx ) ) continue;
			var p = root.PointToLocal( tx.Position );      // (relative to the body: what the viewer sees ride along with it)
			var q = root.Rotation.Inverse * tx.Rotation;
			f[j] = new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
			// and the bone object (what procedural poses move), to tell the two apart
			if ( r.CreateBoneObjects && r.GetBoneObject( j ) is { } go && go.IsValid() ) { var gp = root.PointToLocal( go.WorldPosition ); f[j + "#"] = new[] { gp.x, gp.y, gp.z }; }
		}
		_frames.Add( f );
		if ( _age < Seconds ) return;
		FileSystem.Data.CreateDirectory( "anim_rec" );
		FileSystem.Data.WriteAllText( $"anim_rec/{Name}.json", JsonSerializer.Serialize( new { name = Name, model = r.Model.ResourcePath, scale = r.WorldScale.z, frames = _frames } ) );
		Log.Info( $"[SWRP] recorded {_frames.Count} frames to anim_rec/{Name}.json" );
		Destroy();
	}
}
