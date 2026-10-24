using System;

namespace Swrp;

/// <summary>
/// Lag compensation for blaster shots. A client aims at where it sees a target, which is a round trip (plus interpolation)
/// behind where the host has it now, so a host-only trace misses anything that moves. The host keeps a short history of
/// every trooper's and NPC's position. A shot carries the shooter's claim ("I hit this one, and it stood here"); when the
/// claim matches that history, the host traces the same ray shifted by how far the target has moved since, against that
/// target alone, so the hit lands on the real hitboxes (head shots stay head shots). Walls are checked on the unshifted ray,
/// so nobody is hit through cover. Favour the shooter, within limits: a claim older than <see cref="MaxRewind"/> or further
/// than <see cref="Slack"/> from anywhere the target really was is ignored.
/// </summary>
public static class SwrpLagComp
{
	/// <summary> How far back a claim may reach (seconds). Covers about 400 ms of ping plus interpolation. </summary>
	public const float MaxRewind = 0.6f;

	/// <summary> How far a claimed position may be from the target's recorded path (units). </summary>
	public const float Slack = 28f;

	[ConVar( "swrp_lagcomp", Help = "Host: lag-compensate blaster hits from clients (1, default) or trace only where targets are now (0)" )]
	public static bool Enabled { get; set; } = true;

	[ConVar( "swrp_lagcomp_debug", Help = "Host: log every compensated hit and every rejected claim" )]
	public static bool Debug { get; set; }

	struct Sample { public float Time; public Vector3 Pos; }

	static readonly Dictionary<Guid, List<Sample>> _history = new();
	static float _recordedAt = -1f;

	/// <summary> Host, once a frame (SwrpRefineSystem): record where every trooper and NPC is. </summary>
	public static void Record( Scene scene )
	{
		if ( !Networking.IsHost || scene is null || _recordedAt == Time.Now ) return;
		_recordedAt = Time.Now;
		var now = Time.Now;

		foreach ( var p in SwrpPlayers.Valid ) Add( p.GameObject, now );
		foreach ( var n in scene.GetAllComponents<SwrpNpc>() )
			if ( !n.IsDead ) Add( n.GameObject, now );

		// forget anyone gone, now and then
		if ( _history.Count > 0 && (int)( now * 2f ) % 20 == 0 )
			foreach ( var id in _history.Keys.ToList() )
				if ( _history[id].Count == 0 || now - _history[id][^1].Time > 5f ) _history.Remove( id );
	}

	static void Add( GameObject go, float now )
	{
		if ( !_history.TryGetValue( go.Id, out var list ) ) _history[go.Id] = list = new List<Sample>( 64 );
		list.Add( new Sample { Time = now, Pos = go.WorldPosition } );
		var cut = 0;
		while ( cut < list.Count && now - list[cut].Time > MaxRewind + 0.2f ) cut++;
		if ( cut > 0 ) list.RemoveRange( 0, cut );
	}

	/// <summary> Was the target really near the claimed spot within the rewind window? </summary>
	public static bool Plausible( GameObject target, Vector3 claimed )
	{
		if ( !target.IsValid() || !_history.TryGetValue( target.Id, out var list ) || list.Count == 0 ) return false;
		var now = Time.Now;
		var best = float.MaxValue;
		for ( var i = 0; i < list.Count; i++ )
		{
			if ( now - list[i].Time > MaxRewind ) continue;
			var a = list[i].Pos;
			var b = i + 1 < list.Count ? list[i + 1].Pos : target.WorldPosition;
			best = MathF.Min( best, DistanceToSegment( claimed, a, b ) );
		}
		best = MathF.Min( best, claimed.Distance( target.WorldPosition ) );
		return best <= Slack;
	}

	static float DistanceToSegment( Vector3 p, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var len = ab.LengthSquared;
		if ( len < 0.001f ) return p.Distance( a );
		var t = Math.Clamp( Vector3.Dot( p - a, ab ) / len, 0f, 1f );
		return p.Distance( a + ab * t );
	}

	/// <summary>
	/// Host: the compensated hit on <paramref name="target"/> for a ray, or null. The ray is shifted by how far the target moved
	/// since the claim and traced against that target only; it has to be closer than <paramref name="blockedAt"/> (the first thing
	/// the real, unshifted ray hit), so cover still stops it.
	/// </summary>
	public static SceneTraceResult? Trace( Scene scene, GameObject shooter, GameObject target, Vector3 claimedRoot, Vector3 origin, Vector3 direction, float range, float blockedAt )
	{
		if ( !Enabled || !target.IsValid() ) return null;
		var delta = target.WorldPosition - claimedRoot;
		if ( delta.Length < 0.5f ) return null;      // it hasn't moved: the plain trace already said what happened

		var from = origin + delta;
		SceneTraceResult? best = null;
		foreach ( var tr in scene.Trace.Ray( from, from + direction * range ).IgnoreGameObjectHierarchy( shooter ).WithoutTags( "trigger" ).UseHitboxes().RunAll() )
		{
			if ( !tr.Hit || tr.GameObject is null || !IsPartOf( tr.GameObject, target ) ) continue;
			if ( best is null || tr.Distance < best.Value.Distance ) best = tr;
		}
		if ( best is not { } hit || hit.Distance > blockedAt ) return null;
		if ( Debug ) Log.Info( $"[SWRP] lagcomp: hit {target.Name} shifted {delta.Length:0} units at {hit.Distance:0}" );
		return hit;
	}

	public static bool IsPartOf( GameObject go, GameObject root )
	{
		for ( var g = go; g is not null && g is not Sandbox.Scene; g = g.Parent )
			if ( g == root ) return true;
		return false;
	}

	/// <summary> The trooper or NPC a trace hit (the object whose position the history keeps), or null. </summary>
	public static GameObject TargetOf( SceneTraceResult tr )
	{
		if ( !tr.Hit || tr.GameObject is null ) return null;
		if ( tr.GameObject.Components.GetInAncestorsOrSelf<SwrpVitals>() is { } v ) return v.GameObject;
		if ( tr.GameObject.Components.GetInAncestorsOrSelf<SwrpNpc>() is { } n ) return n.GameObject;
		return null;
	}
}
