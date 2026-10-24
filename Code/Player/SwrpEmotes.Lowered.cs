using System;

namespace Swrp;

/// <summary>
/// Stock s&amp;box animations, gun lowered (Shift+E+R): the human graph lowers the rifle only standing still, and walking
/// brought it back up (Angelo, 2026-10-19: "should be able to walk with the rifle lowered like this"). While standing
/// lowered the arms' pose against the chest is kept per model; while moving lowered the arms are held in it, so the walk
/// keeps the gun down across the body exactly as it stands.
/// </summary>
public static partial class SwrpEmotes
{
	/// <summary> Bodies with the gun lowered on the stock set, and how fast they move over the ground (set by the driver). </summary>
	public static readonly Dictionary<SkinnedModelRenderer, float> LoweredWalk = new();

	static readonly string[] LowerBones = { "clavicle_L", "arm_upper_L", "arm_lower_L", "hand_L", "clavicle_R", "arm_upper_R", "arm_lower_R", "hand_R" };
	static readonly Dictionary<Model, Dictionary<string, Rotation>> _lowerPose = new();

	/// <summary> True while it holds the arms (moving lowered, with a pose learned standing). </summary>
	static bool ApplyLoweredWalk( SkinnedModelRenderer r )
	{
		if ( !LoweredWalk.TryGetValue( r, out var speed ) || r.Model is not { } model || !RawBone( r, "spine_2", out var chest ) ) return false;
		if ( speed < 5f )
		{
			// standing lowered: the graph's own lowered pose, learned against the chest (straight from the animation)
			var pose = new Dictionary<string, Rotation>();
			foreach ( var name in LowerBones )
				if ( RawBone( r, name, out var b ) ) pose[name] = chest.Rotation.Inverse * b.Rotation;
			if ( pose.Count == LowerBones.Length ) _lowerPose[model] = pose;
			return false;
		}
		if ( !_lowerPose.TryGetValue( model, out var held ) ) return false;
		if ( !r.CreateBoneObjects ) r.CreateBoneObjects = true;
		// moving: the arms held in that pose against the chest as it moves (parent first, so the hands land where they stood)
		var w = Math.Clamp( ( speed - 5f ) / 25f, 0f, 1f );
		foreach ( var name in LowerBones )
		{
			var go = r.GetBoneObject( name );
			if ( !go.IsValid() || !held.TryGetValue( name, out var rel ) || !RawBone( r, name, out var anim ) ) continue;
			Take( go ).WorldRotation = Rotation.Slerp( anim.Rotation, chest.Rotation * rel, w );
		}
		return true;
	}
}
