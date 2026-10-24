using System;

namespace Swrp;

/// <summary>
/// A visible flinch when a body takes a hit (Angelo, 2026-10-13: "you can visually see some sort of hit repulsion of their body
/// slightly"). The upper body (spine_0 and everything above it) snaps away from the shot about the base of the spine, then
/// springs back over about a third of a second; a hit from the side also twists the shoulders a little. Players, NPC clones and
/// any other body on the Citizen rig; a skeleton without spine_0 (the GC droids) is left alone.
///
/// It's procedural, so it sits on top of whatever the animation, the rifle hold and the hold life are doing: SwrpEmotes reads
/// the flinched upper body through <see cref="Adjust"/> when it solves the arms, so the hands stay on the gun and the gun
/// rides with the chest. The host decides a hit happened and broadcasts it (SwrpVitals / SwrpNpc), so everyone sees it.
/// First-person flinch of the camera is separate (Code/Feel/FeelHits).
/// </summary>
public static class HitReact
{
	[ConVar( "swrp_hit_react", Help = "Bodies flinch away from the shot when hit (0 = off)", Saved = true )]
	public static bool On { get; set; } = true;

	[ConVar( "swrp_hit_react_scale", Help = "How far bodies flinch when hit (1 = default)", Saved = true )]
	public static float Scale { get; set; } = 1f;

	const float Rise = 0.06f;        // seconds to the furthest lean
	const float Settle = 0.34f;      // seconds back to still (an underdamped spring: a small overshoot on the way back)

	sealed class Flinch
	{
		public Vector3 Axis;           // world axis the upper body turns about
		public float Degrees;
		public RealTimeSince Since;
		public float Frame = -1f;
		public Rotation Turn = Rotation.Identity;
		public Vector3 Pivot;
		public HashSet<string> Upper;
	}

	static readonly Dictionary<SkinnedModelRenderer, Flinch> _active = new();
	static readonly Dictionary<Model, HashSet<string>> _upper = new();

	/// <summary>
	/// Every machine: start a flinch on a body. <paramref name="dir"/> is the way the shot travelled (from the shooter to the
	/// body); <paramref name="strength"/> 0..1 (a blaster hit about 0.45, a heavy hit or stun 1).
	/// </summary>
	public static void Play( SkinnedModelRenderer r, Vector3 dir, float strength, bool head = false )
	{
		if ( !On || !r.IsValid() || r.Model is null || r.Model.IsError ) return;
		var upper = UpperBones( r.Model );
		if ( upper is null ) return;
		dir = dir.WithZ( dir.z * 0.3f );
		if ( dir.LengthSquared < 0.0001f ) dir = -r.WorldRotation.Forward;
		dir = dir.Normal;
		// lean away: the chest moves along the shot, so the body turns about (up x dir); a side hit also twists the shoulders
		var lean = Vector3.Cross( Vector3.Up, dir ).Normal;
		var side = Vector3.Dot( r.WorldRotation.Right, dir );
		var axis = ( lean + Vector3.Up * ( -side * 0.45f ) ).Normal;
		var deg = Math.Clamp( strength, 0f, 1.2f ) * ( head ? 18f : 14f ) * Scale;
		if ( deg < 0.5f ) return;
		if ( !r.CreateBoneObjects ) r.CreateBoneObjects = true;
		_active[r] = new Flinch { Axis = axis, Degrees = deg, Since = 0, Upper = upper };
	}

	/// <summary> 0..1..0 over the flinch: a quick snap out, then a damped spring back (dips a little past zero). </summary>
	public static float Curve( float t )
	{
		if ( t <= 0f ) return 0f;
		if ( t < Rise ) { var x = t / Rise; return x * x * ( 3f - 2f * x ); }
		var u = ( t - Rise ) / Settle;
		if ( u >= 1f ) return 0f;
		return MathF.Exp( -4.2f * u ) * MathF.Cos( u * MathF.PI * 1.35f ) * ( 1f - u );
	}

	/// <summary>
	/// Pose the flinch on this body for this frame (idempotent: SwrpEmotes calls it before solving the arms, the system calls
	/// it for bodies nobody else poses). Returns false once the body is still again.
	/// </summary>
	public static bool Tick( SkinnedModelRenderer r )
	{
		if ( !_active.TryGetValue( r, out var f ) ) return false;
		if ( f.Frame == Time.Now ) return true;
		f.Frame = Time.Now;
		var spine = r.IsValid() ? r.Model?.Bones.GetBone( "spine_0" ) : null;
		var go = spine is not null ? r.GetBoneObject( spine ) : null;
		var k = Curve( f.Since );
		if ( spine is null || !go.IsValid() || f.Since > Rise + Settle || !On )
		{
			if ( go.IsValid() && go.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) go.Flags &= ~GameObjectFlags.ProceduralBone;
			_active.Remove( r );
			return false;
		}
		if ( !r.TryGetBoneTransformAnimation( in spine, out var anim ) || spine.Parent is not { } parent || !r.TryGetBoneTransformAnimation( in parent, out var pa ) ) return true;
		// on top of a rifle stance's hips (SwrpEmotes.Stance brings the pelvis down and may turn it)
		anim = SwrpEmotes.StanceAdjust( r, spine.Name, anim );
		pa = SwrpEmotes.StanceAdjust( r, parent.Name, pa );
		f.Pivot = anim.Position;
		f.Turn = Rotation.FromAxis( f.Axis, f.Degrees * k );
		if ( !go.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) go.Flags |= GameObjectFlags.ProceduralBone;
		go.LocalRotation = pa.Rotation.Inverse * ( f.Turn * anim.Rotation );
		return true;
	}

	/// <summary>
	/// An animated bone's world transform with this frame's flinch on it (bones above the base of the spine turn with it), for
	/// code that reads the animation and poses on top of it (the arm IK).
	/// </summary>
	public static Transform Adjust( SkinnedModelRenderer r, string bone, Transform tx )
	{
		if ( !_active.TryGetValue( r, out var f ) || !f.Upper.Contains( bone ) ) return tx;
		return new Transform( f.Pivot + f.Turn * ( tx.Position - f.Pivot ), f.Turn * tx.Rotation, tx.Scale );
	}

	/// <summary> The bones that turn with the chest (spine_0 and its children), or null when the skeleton has no spine_0. </summary>
	static HashSet<string> UpperBones( Model model )
	{
		if ( _upper.TryGetValue( model, out var set ) ) return set;
		var bones = model.Bones.AllBones.ToList();
		if ( !bones.Any( b => b.Name == "spine_0" ) ) return _upper[model] = null;
		set = new HashSet<string>();
		foreach ( var b in bones )
			for ( var p = b; p is not null; p = p.Parent )
				if ( p.Name == "spine_0" ) { set.Add( b.Name ); break; }
		return _upper[model] = set;
	}

	/// <summary> Runs the flinches of bodies that nothing else poses this frame (no gun held, droids). </summary>
	internal static void TickAll()
	{
		if ( _active.Count == 0 ) return;
		foreach ( var r in _active.Keys.ToList() )
			if ( !r.IsValid() ) _active.Remove( r );
			else Tick( r );
	}
}

/// <summary> Ticks <see cref="HitReact"/> at the start of every frame, so the chest is already turned when SwrpEmotes solves the arms. </summary>
public sealed class HitReactSystem : GameObjectSystem<HitReactSystem>
{
	public HitReactSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 5, HitReact.TickAll, "SwrpHitReact" );
	}
}
