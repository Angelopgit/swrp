using System;

namespace Swrp.Flight;

/// <summary>
/// A starship laser bolt for the eye: a bolt of glowing energy (much bigger than a rifle bolt, so it reads at fighter ranges)
/// that races from the muzzle to where the host's trace landed. Built like the rifle bolt (BlasterBolt) so it reads as light
/// rather than a flat strip (Angelo, 2026-10-19: "the energy rounds look just like images"): a thin hot filament, a haze
/// of soft glow along it growing to the head, a bright halo at the head and a light riding it. Where it hits, the energy
/// bursts when the bolt gets there (not when it's fired), scaled to the cannon.
/// </summary>
public sealed class FlightBolt : Component
{
	const float Speed = 14000f;
	const int Haze = 8;

	Vector3 _start, _end, _dir;
	float _length, _total, _width;
	RealTimeSince _born;
	LineRenderer _core;
	readonly List<SpriteRenderer> _haze = new();
	SpriteRenderer _halo;
	PointLight _light;
	Color _color;
	(Vector3 Normal, bool Scorch, float Scale)? _hit;

	// every live bolt, so stale ones can be swept even if their own update stops (a code hotload mid-flight left LAAT bolts
	// frozen on the hangar deck, Angelo 2026-10-12)
	static readonly List<FlightBolt> _live = new();
	static RealTimeSince _sinceSceneSweep = 100f;

	/// <param name="hitNormal"> Set when the shot hit something: the burst plays there as the bolt arrives. </param>
	public static void Spawn( Scene scene, Vector3 start, Vector3 end, Color color, float length = 280f, float width = 5f,
		Vector3? hitNormal = null, bool scorch = true )
	{
		Sweep( scene );
		var go = scene.CreateObject();
		go.Name = "flight_bolt";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = start;
		var b = go.Components.Create<FlightBolt>();
		b._start = start;
		b._end = end;
		b._total = start.Distance( end );
		b._dir = b._total > 0.01f ? ( end - start ) / b._total : Vector3.Forward;
		b._length = length;
		b._width = width;
		b._born = 0;
		b._color = LightProfile.Vivid( color );
		if ( hitNormal is { } n ) b._hit = (n, scorch, width / 1.6f);
		_live.Add( b );

		// the filament: thin, a little hotter than the bolt's colour, fading in from the tail (brightness from alpha and size,
		// not overdriven colour, which clips blue to white: LightProfile.Vivid keeps it saturated)
		var deep = b._color;
		var hot = Color.Lerp( deep, Color.White, 0.08f );
		b._core = go.Components.Create<LineRenderer>();
		b._core.UseVectorPoints = true;
		b._core.VectorPoints = new List<Vector3> { start, start };
		b._core.Color = new Gradient( new Gradient.ColorFrame( 0f, hot.WithAlpha( 0f ) ), new Gradient.ColorFrame( 0.4f, ( hot * 1.15f ).WithAlpha( 0.85f ) ), new Gradient.ColorFrame( 1f, ( hot * 1.3f ).WithAlpha( 1f ) ) );
		b._core.Width = new Curve( new Curve.Frame( 0f, width * 0.35f ), new Curve.Frame( 1f, width * 0.45f ) );
		b._core.Additive = true;
		b._core.Lighting = false;
		b._core.CastShadows = false;

		// the haze: round glow along the bolt, faint at the tail, bigger and brighter at the head; it has body from every angle
		var sprite = SwrpFx.GlowSprite;
		if ( sprite is not null )
		{
			for ( var i = 0; i < Haze; i++ )
			{
				var t = i / (float)( Haze - 1 );
				b._haze.Add( Glow( go, sprite, ( deep * ( 1f + 0.35f * t ) ).WithAlpha( 0.2f + 0.6f * t * t ), width * ( 3f + 4f * t ) ) );
			}
			b._halo = Glow( go, sprite, ( deep * 1.2f ).WithAlpha( 0.55f ), width * 12f );
		}
		if ( SwrpFx.TakeLight( scene, start ) ) b._light = SwrpFx.AddLight( go, deep * 4f, 60f * width );
	}

	static SpriteRenderer Glow( GameObject parent, Sprite sprite, Color color, float size )
	{
		var go = new GameObject( parent, true, "haze" );
		go.NetworkMode = NetworkMode.Never;
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

	/// <summary> Removes bolts that should have finished (well past their flight time), and any left over from an old scene. </summary>
	public static void Sweep( Scene scene )
	{
		for ( var i = _live.Count - 1; i >= 0; i-- )
		{
			var b = _live[i];
			if ( !b.IsValid() ) { _live.RemoveAt( i ); continue; }
			if ( b._born > ( b._total + b._length ) / Speed + 1f ) { _live.RemoveAt( i ); b.GameObject.Destroy(); }
		}
		// stale ones from before a hotload aren't in the list: anything named like a bolt with no flight left
		if ( scene is null || _sinceSceneSweep < 1f ) return;
		_sinceSceneSweep = 0;
		foreach ( var c in scene.GetAllComponents<FlightBolt>().ToList() )
			if ( !_live.Contains( c ) && c._born > 3f ) c.GameObject.Destroy();
	}

	protected override void OnDestroy() => _live.Remove( this );

	protected override void OnUpdate()
	{
		// from the time since it was fired, not a running sum, so a missed frame never strands it
		var travelled = _born * Speed;
		var head = MathF.Min( travelled, _total );
		var tail = MathF.Max( 0f, head - _length );
		var a = _start + _dir * tail;
		var b = _start + _dir * head;
		_core.VectorPoints = new List<Vector3> { a, b };
		for ( var i = 0; i < _haze.Count; i++ )
			if ( _haze[i].IsValid() ) _haze[i].WorldPosition = Vector3.Lerp( a, b, i / (float)( _haze.Count - 1 ) );
		if ( _halo.IsValid() ) _halo.WorldPosition = b;
		if ( _light.IsValid() ) _light.WorldPosition = b;

		// the bolt reaches what it hit: the energy bursts there now
		if ( _hit is { } hit && travelled >= _total )
		{
			_hit = null;
			SwrpFx.Impact( Scene, _end, hit.Normal, _color, hit.Scorch, hit.Scale );
			if ( _halo.IsValid() ) _halo.Enabled = false;
		}
		if ( tail >= _total ) GameObject.Destroy();
	}
}
