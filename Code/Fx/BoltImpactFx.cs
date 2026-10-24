using System;
using System.Collections.Generic;

namespace Swrp;

/// <summary>
/// The energy of a bolt dispersing where it hits (Angelo, 2026-10-12: "blue energy disperse when the blaster shot hits"): a ring
/// of glow motes racing out across the surface, a few jagged crackles of arc that flicker and die, all in the bolt's own deep
/// colour and gone in a quarter of a second. Local only; skipped far from your camera and capped so automatic fire stays cheap.
/// Called from <see cref="SwrpFx.Impact"/>.
/// </summary>
public sealed class BoltImpactFx : Component
{
	const int Motes = 10;
	const int Arcs = 3;
	const float Life = 0.26f;
	const float Reach = 16f;
	/// <summary> At most this many bursts alive at once (each is ~13 small renderers). </summary>
	const int MaxLive = 12;

	public static int Live { get; private set; }

	Vector3 _normal;
	Color _color;
	float _scale;
	TimeSince _age;
	readonly List<(SpriteRenderer R, Vector3 Dir, float Speed)> _motes = new();
	readonly List<LineRenderer> _arcs = new();
	SpriteRenderer _core;

	public static void Spawn( Scene scene, Vector3 pos, Vector3 normal, Color color, float scale = 1f )
	{
		if ( Live >= MaxLive || !SwrpFx.Near( scene, pos, SwrpFx.LightRange ) || SwrpFx.GlowSprite is null ) return;
		var go = scene.CreateObject();
		go.Name = "bolt_disperse";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos + normal * 1.2f;
		var fx = go.Components.Create<BoltImpactFx>();
		fx._normal = normal;
		fx._color = LightProfile.Vivid( color );
		fx._scale = scale;
		fx.Build();
	}

	void Build()
	{
		Live++;
		_age = 0;
		var sprite = SwrpFx.GlowSprite;

		// two directions across the surface
		var t1 = Vector3.Cross( _normal, MathF.Abs( _normal.z ) > 0.9f ? Vector3.Forward : Vector3.Up ).Normal;
		var t2 = Vector3.Cross( _normal, t1 ).Normal;

		// a tight saturated core where the bolt landed, no white
		_core = Sprite( sprite, ( _color * 1.4f ).WithAlpha( 0.9f ), 7f * _scale );

		for ( var i = 0; i < Motes; i++ )
		{
			var a = ( i + Game.Random.Float( -0.3f, 0.3f ) ) / Motes * MathF.PI * 2f;
			var dir = ( t1 * MathF.Cos( a ) + t2 * MathF.Sin( a ) + _normal * Game.Random.Float( 0.05f, 0.35f ) ).Normal;
			var r = Sprite( sprite, ( _color * 1.2f ).WithAlpha( 0.8f ), Game.Random.Float( 1.6f, 2.6f ) * _scale );
			_motes.Add( (r, dir, Game.Random.Float( 0.7f, 1.15f )) );
		}

		for ( var i = 0; i < Arcs; i++ )
		{
			var a = Game.Random.Float( 0f, MathF.PI * 2f );
			var dir = t1 * MathF.Cos( a ) + t2 * MathF.Sin( a );
			var side = Vector3.Cross( _normal, dir );
			var len = Game.Random.Float( 6f, 12f ) * _scale;
			var pts = new List<Vector3>();
			for ( var k = 0; k <= 5; k++ )
			{
				var f = k / 5f;
				var jag = k == 0 ? 0f : Game.Random.Float( -1.6f, 1.6f ) * _scale;
				pts.Add( WorldPosition + dir * len * f + side * jag + _normal * Game.Random.Float( 0f, 1f ) );
			}
			var line = new GameObject( GameObject, true, "arc" ).Components.Create<LineRenderer>();
			line.GameObject.Flags |= GameObjectFlags.NotSaved;
			line.UseVectorPoints = true;
			line.VectorPoints = pts;
			line.Color = new Gradient( new Gradient.ColorFrame( 0f, ( _color * 1.5f ).WithAlpha( 1f ) ), new Gradient.ColorFrame( 1f, ( _color * 1.1f ).WithAlpha( 0f ) ) );
			line.Width = new Curve( new Curve.Frame( 0f, 0.35f * _scale ), new Curve.Frame( 1f, 0.12f * _scale ) );
			line.Additive = true;
			line.Lighting = false;
			line.CastShadows = false;
			_arcs.Add( line );
		}
	}

	SpriteRenderer Sprite( Sprite sprite, Color color, float size )
	{
		var go = new GameObject( GameObject, true, "mote" );
		go.Flags |= GameObjectFlags.NotSaved;
		var r = go.Components.Create<SpriteRenderer>();
		r.Sprite = sprite;
		r.Additive = true;
		r.Lighting = false;
		r.Shadows = false;
		r.Color = color;
		r.Size = new Vector2( size, size );
		return r;
	}

	protected override void OnUpdate()
	{
		var t = _age / Life;
		if ( t >= 1f ) { GameObject.Destroy(); return; }
		var fade = 1f - t;
		var ease = 1f - fade * fade;     // fast out, slowing as it spreads

		if ( _core.IsValid() )
		{
			_core.Color = ( _color * 1.4f ).WithAlpha( 0.9f * fade * fade );
			_core.Size = new Vector2( 7f + 5f * ease, 7f + 5f * ease ) * _scale;
		}
		foreach ( var (r, dir, speed) in _motes )
		{
			if ( !r.IsValid() ) continue;
			r.WorldPosition = WorldPosition + dir * Reach * speed * ease * _scale;
			r.Color = ( _color * 1.2f ).WithAlpha( 0.8f * fade );
		}
		// the arcs crackle: flicker on and off, gone by half-life
		for ( var i = 0; i < _arcs.Count; i++ )
			if ( _arcs[i].IsValid() ) _arcs[i].Enabled = t < 0.5f && Game.Random.Float() > 0.35f;
	}

	protected override void OnDestroy() => Live = Math.Max( 0, Live - 1 );
}
