using System;

namespace Swrp.Jedi;

/// <summary>
/// The traces a blade's cut makes between two frames, shared by the host's hit detection and the test stand's hit test.
/// The blade is stepped from last frame's position to this one's by turning it (its emitter moving in a line, its direction
/// turning by slerp), not by sliding its two ends straight, so a fast wide cut still reaches full length mid-step instead of
/// cutting the corner. Points along the blade are traced from step to step; and the blade itself is traced from emitter to
/// tip each frame, which finds anything it is already inside (a thrust whose tip starts in its target, a blade laid on a
/// body), since a trace that starts inside a collider doesn't hit it.
/// </summary>
public static class SaberSweep
{
	/// <summary> Where a sweeper has got to in the current swing. </summary>
	public sealed class State
	{
		public int Id = -1;
		public float T;
	}

	/// <summary>
	/// Walk the part of the current swing's cutting window that passed since last frame in slices of at most 1/90 s of swing
	/// time (whatever the frame rate: a slow frame or a hitch can't skip a strike), calling <paramref name="pair"/> for each
	/// blade from one slice to the next (index, emitter and tip before, emitter and tip after, seconds). Stops when it returns
	/// true.
	/// </summary>
	public static void Step( SaberMotion m, State st, float aimPitch, Func<MotionSample, List<(Vector3 Base, Vector3 Tip)>> blades,
		Func<int, Vector3, Vector3, Vector3, Vector3, float, bool> pair )
	{
		if ( m.Kind != MotionKind.Swing ) { st.Id = -1; return; }
		if ( st.Id != m.ActionId ) { st.Id = m.ActionId; st.T = 0f; }
		var (ws, we) = m.HitWindowTimes;
		var from = MathF.Max( st.T, ws );
		var to = MathF.Min( m.Time, we );
		st.T = m.Time;
		if ( to <= from ) return;
		var n = Math.Clamp( (int)MathF.Ceiling( ( to - from ) * 90f ), 1, 24 );
		var dt = ( to - from ) / n;
		var now = Time.Now;
		var prev = blades( m.Probe( from, aimPitch, now ) );
		for ( var k = 1; k <= n; k++ )
		{
			var cur = blades( m.Probe( from + dt * k, aimPitch, now ) );
			for ( var i = 0; i < Math.Min( prev.Count, cur.Count ); i++ )
				if ( pair( i, prev[i].Base, prev[i].Tip, cur[i].Base, cur[i].Tip, dt ) ) return;
			prev = cur;
		}
	}

	static readonly float[] Points = { 0.08f, 0.3f, 0.52f, 0.74f, 0.92f, 1f };

	public static List<(Vector3 From, Vector3 To)> Segments( Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float scale )
	{
		var list = new List<(Vector3, Vector3)>();
		var len0 = a0.Distance( b0 );
		var len1 = a1.Distance( b1 );
		var d0 = ( b0 - a0 ).Normal;
		var d1 = ( b1 - a1 ).Normal;
		var turn = Vector3.GetAngle( d0, d1 );
		var steps = Math.Clamp( (int)MathF.Ceiling( MathF.Max( turn / 6f, b0.Distance( b1 ) / ( 5f * scale ) ) ), 1, 18 );
		var r0 = Rotation.LookAt( d0 );
		var r1 = Rotation.LookAt( d1 );

		( Vector3 Base, Vector3 Dir, float Len ) At( float u )
		{
			var dir = turn < 0.5f ? Vector3.Lerp( d0, d1, u ).Normal : Rotation.Slerp( r0, r1, u ).Forward;
			return ( Vector3.Lerp( a0, a1, u ), dir, MathX.Lerp( len0, len1, u ) );
		}

		var prev = At( 0f );
		for ( var k = 1; k <= steps; k++ )
		{
			var cur = At( k / (float)steps );
			foreach ( var f in Points )
			{
				var from = prev.Base + prev.Dir * prev.Len * f;
				var to = cur.Base + cur.Dir * cur.Len * f;
				if ( from.DistanceSquared( to ) < 0.01f ) continue;
				list.Add( ( from, to ) );
			}
			prev = cur;
		}
		// the blade as it lies now
		list.Add( ( a1, b1 ) );
		return list;
	}
}
