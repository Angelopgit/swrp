using System;

namespace Swrp;

/// <summary>
/// The wisp of smoke a blaster bolt leaves where it hits (Angelo, 2026-10-19: "a smoke effect on blaster shots when they
/// hit"): a few small grey puffs off the BF2 billow flipbook that curl up off the surface and thin out. Local, capped.
/// </summary>
public sealed class BoltSmoke : Component
{
	const int MaxLive = 80;          // puffs across every impact; automatic fire stops adding past this
	static int _live;
	static Sprite _fallback;

	readonly List<(SpriteRenderer R, Vector3 Vel, float Size, float Life, Sprite[] Frames)> _puffs = new();
	TimeSince _age;
	int _mine;      // puffs of this impact still counted in _live

	public static void Spawn( Scene scene, Vector3 pos, Vector3 normal, float scale )
	{
		var n = Math.Min( _live >= MaxLive ? 0 : MaxLive - _live, scale > 1.5f ? 5 : 3 );
		if ( n <= 0 ) return;
		var sheets = Billow.Sheets;
		_fallback ??= Sprite.FromTexture( Texture.Load( "materials/fx/steam.png" ) );
		var go = scene.CreateObject();
		go.Name = "bolt_smoke";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos + normal * 3f;
		var s = go.Components.Create<BoltSmoke>();
		s._age = 0;
		for ( var i = 0; i < n; i++ )
		{
			var p = new GameObject( go, true, "puff" );
			p.NetworkMode = NetworkMode.Never;
			p.Flags |= GameObjectFlags.NotSaved;
			p.LocalPosition = Vector3.Random * 3f * scale;
			var r = p.Components.Create<SpriteRenderer>();
			var frames = sheets?[Game.Random.Int( 0, sheets.Length - 1 )];
			r.Sprite = frames?[Game.Random.Int( 0, 8 )] ?? _fallback;
			r.Lighting = true;
			r.Shadows = false;
			r.Color = Color.Transparent;
			// off the surface first, then drifting up whichever way the surface faces
			var vel = ( normal * Game.Random.Float( 20f, 45f ) + Vector3.Random * 10f + Vector3.Up * Game.Random.Float( 12f, 28f ) ) * MathF.Sqrt( scale );
			s._puffs.Add( (r, vel, Game.Random.Float( 22f, 40f ) * scale, Game.Random.Float( 1.3f, 2.2f ), frames) );
			_live++;
			s._mine++;
		}
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		for ( var i = _puffs.Count - 1; i >= 0; i-- )
		{
			var (r, vel, size, life, frames) = _puffs[i];
			var k = _age / life;
			if ( !r.IsValid() || k >= 1f ) { r?.GameObject?.Destroy(); _puffs.RemoveAt( i ); _live--; _mine--; continue; }
			if ( frames is not null ) r.Sprite = frames[Math.Min( 63, 8 + (int)( k * 55f ) )];
			r.WorldPosition += ( vel * ( 1f - k ) + Vector3.Up * 14f * k ) * dt;
			var s = size * ( 0.3f + 0.7f * MathF.Sqrt( k ) );
			r.Size = new Vector2( s, s );
			var shade = 0.32f - 0.08f * k;
			r.Color = new Color( shade, shade, shade * 1.04f, 0.42f * MathF.Min( 1f, k * 7f ) * ( 1f - k ) );
		}
		if ( _puffs.Count == 0 ) GameObject.Destroy();
	}

	protected override void OnDestroy()
	{
		_live = Math.Max( 0, _live - _mine );
		_mine = 0;
	}
}
