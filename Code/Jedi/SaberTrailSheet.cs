using System;

namespace Swrp.Jedi;

/// <summary>
/// A blade's trail fill as one swept sheet: a strip of triangles between the curve the blade's inner end traced and the
/// curve its tip traced, fading with age. Drawn straight into the scene each frame (no model is built), so a fast cut
/// reads as one smooth smear instead of a fan of separate strands. Local only; owned by <see cref="SaberVisual"/>.
/// </summary>
sealed class SaberTrailSheet : IDisposable
{
	static Material _material;
	static RenderAttributes _attributes;
	readonly SceneCustomObject _obj;
	List<Vertex> _verts = new();
	int _count;

	public SaberTrailSheet( Scene scene )
	{
		// the shader the engine's own line renderer draws with: unlit, two-sided, and its blend combo is additive by
		// vertex alpha (source alpha, one), so the sheet glows over whatever is behind it like the blade does
		_material ??= Material.FromShader( "shaders/line.shader" );
		if ( _attributes is null )
		{
			_attributes = new RenderAttributes();
			_attributes.SetCombo( "D_BLEND", 1 );
			_attributes.SetCombo( "D_OPAQUE", 0 );
			_attributes.SetCombo( "D_ENABLE_LIGHTING", 0 );
		}
		_obj = new SceneCustomObject( scene.SceneWorld ) { RenderOverride = Draw };
		_obj.RenderingEnabled = false;
	}

	public void Hide()
	{
		_count = 0;
		if ( _obj.IsValid() ) _obj.RenderingEnabled = false;
	}

	/// <summary> Rebuild from the inner and tip curves (same length, oldest first); alpha peaks at the newest end. </summary>
	public void Set( List<Vector3> inner, List<Vector3> outer, Color color, float alpha )
	{
		var n = Math.Min( inner.Count, outer.Count );
		if ( n < 2 || alpha <= 0.002f || !_obj.IsValid() ) { Hide(); return; }

		var verts = new List<Vertex>( ( n - 1 ) * 6 );
		var box = BBox.FromPositionAndSize( outer[n - 1], 1f );
		Vertex V( Vector3 p, float a )
		{
			box = box.AddPoint( p );
			var v = new Vertex( p, Vector3.Up, Vector3.Forward, Vector4.Zero );
			v.Color = color.WithAlpha( Math.Clamp( a, 0f, 1f ) );
			return v;
		}
		for ( var k = 0; k < n - 1; k++ )
		{
			// fades in from nothing at the oldest end; the inner edge stays fainter than the tip's
			float a0 = MathF.Pow( k / (float)( n - 1 ), 1.6f ) * alpha, a1 = MathF.Pow( ( k + 1 ) / (float)( n - 1 ), 1.6f ) * alpha;
			Vertex i0 = V( inner[k], a0 * 0.25f ), o0 = V( outer[k], a0 ), i1 = V( inner[k + 1], a1 * 0.25f ), o1 = V( outer[k + 1], a1 );
			// one winding: the line shader draws both sides when unlit
			verts.Add( i0 ); verts.Add( o0 ); verts.Add( o1 );
			verts.Add( i0 ); verts.Add( o1 ); verts.Add( i1 );
		}
		_verts = verts;
		_count = verts.Count;
		_obj.Bounds = box;
		_obj.RenderingEnabled = true;
	}

	void Draw( SceneObject o )
	{
		var verts = _verts;
		if ( _count > 0 && _count <= verts.Count && _material is not null ) Graphics.Draw( verts, _count, _material, _attributes );
	}

	public void Dispose()
	{
		if ( _obj.IsValid() ) _obj.Delete();
	}
}
