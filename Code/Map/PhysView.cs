using System;
using System.Text;

namespace Swrp;

/// <summary>
/// Dev: a "collision camera". Traces one ray per pixel against the physics world (what ships and players actually bump into)
/// and writes the depth and which object was hit to data/physview.txt, so the Venator's invisible walls show up next to a
/// normal screenshot from the same spot (Angelo, 2026-10-12: players will fly in and out of the Venator).
/// </summary>
public static class PhysView
{
	[ConCmd( "swrp_phys_view", Help = "swrp_phys_view <x> <y> <z> <pitch> <yaw> [fov] [w] [h] - dev: trace a collision depth image to data/physview.txt" )]
	public static void Run( float x, float y, float z, float pitch, float yaw, float fov = 60f, int w = 160, int h = 120 )
	{
		if ( Game.ActiveScene is not { } sc ) return;
		var eye = new Vector3( x, y, z );
		var rot = Rotation.From( pitch, yaw, 0 );
		var t = MathF.Tan( fov.DegreeToRadian() * 0.5f );
		var names = new List<string>();
		var sb = new StringBuilder();
		sb.AppendLine( $"{w} {h}" );
		for ( int j = 0; j < h; j++ )
		{
			for ( int i = 0; i < w; i++ )
			{
				var u = ( ( i + 0.5f ) / w * 2f - 1f ) * t;
				var v = ( 1f - ( j + 0.5f ) / h * 2f ) * t * h / w;
				var dir = ( rot.Forward + rot.Right * u + rot.Up * v ).Normal;
				var tr = sc.Trace.Ray( eye, eye + dir * 30000f ).WithoutTags( "player", "npc", "trigger" ).Run();
				if ( !tr.Hit ) { sb.Append( "-1:-1 " ); continue; }
				var n = tr.GameObject?.Name ?? "?";
				var k = names.IndexOf( n );
				if ( k < 0 ) { names.Add( n ); k = names.Count - 1; }
				sb.Append( $"{tr.Distance:0}:{k} " );
			}
			sb.AppendLine();
		}
		sb.AppendLine( string.Join( "|", names ) );
		FileSystem.Data.WriteAllText( "physview.txt", sb.ToString() );
		Log.Info( $"[SWRP] phys view written ({names.Count} objects)" );
	}
}
