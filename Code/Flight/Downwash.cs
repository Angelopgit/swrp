using System;

namespace Swrp.Flight;

/// <summary>
/// A hovering gunship's repulsor wash on whatever is under it (Angelo, 2026-10-19: "the debris of air when floating around
/// the LAAT ... on top of a surface/object underneath it"; then "more volumetric air winds"): thick billowing dust off the
/// BF2 smoke flipbook rolling out across the surface in a churning ring, a swirl of it under the craft, and grit flicked out
/// low and fast. Standing in it, your own view takes the gust (looks only, nothing pushes you): the camera buffets, wind
/// streaks rush past and the wind roars. The closer to the surface and the more power the stronger. Local only.
/// </summary>
public sealed class Downwash : Component
{
	const float Reach = 900f;           // no wash higher than this over the surface
	const int MaxPuffs = 90;            // per craft
	const float GustRadius = 900f;      // how far out from under the craft your view feels it
	static Sprite _dust, _grit;

	readonly List<Puff> _puffs = new();
	readonly List<(LineRenderer L, Vector3 Pos, Vector3 Vel, RealTimeSince Age)> _streaks = new();
	RealTimeSince _sinceTrace = 1f, _sinceSpawn, _sinceStreak;
	SceneTraceResult _ground;
	SoundHandle _roar;

	class Puff
	{
		public SpriteRenderer R;
		public Vector3 Vel;
		public float Size, Life, Spin;
		public RealTimeSince Age;
		public bool Grit;
		public Sprite[] Frames;
	}

	/// <summary> The craft's power (0..1) this frame; set by the craft. </summary>
	public float Power { get; set; }

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		if ( _sinceTrace > 0.1f )
		{
			_sinceTrace = 0;
			var from = WorldPosition + Vector3.Up * 40f;
			_ground = Scene.Trace.Ray( from, from + Vector3.Down * ( Reach + 40f ) ).IgnoreGameObjectHierarchy( GameObject.Root )
				.WithoutTags( "trigger", "player", "npc", "debris" ).Run();
		}

		var strength = _ground.Hit && Power > 0.05f ? Power * Math.Clamp( 1f - _ground.Distance / Reach, 0f, 1f ) : 0f;
		if ( strength > 0.02f && SwrpFx.Near( Scene, _ground.HitPosition, 5000f ) )
		{
			_dust ??= Sprite.FromTexture( Texture.Load( "materials/fx/steam.png" ) );
			_grit ??= SwrpFx.GlowSprite;
			var every = MathX.Lerp( 0.09f, 0.018f, strength );
			while ( _sinceSpawn > every && _puffs.Count < MaxPuffs )
			{
				_sinceSpawn -= every;
				Spawn( strength, Game.Random.Float() < 0.3f );
			}
			if ( _sinceSpawn > every ) _sinceSpawn = 0;
		}
		else _sinceSpawn = 0;

		var centre = _ground.HitPosition;
		for ( var i = _puffs.Count - 1; i >= 0; i-- )
		{
			var p = _puffs[i];
			var k = p.Age / p.Life;
			if ( !p.R.IsValid() || k >= 1f ) { p.R?.GameObject?.Destroy(); _puffs.RemoveAt( i ); continue; }
			if ( p.Grit )
			{
				p.R.WorldPosition += p.Vel * ( 1f - k * 0.6f ) * dt + Vector3.Down * ( 220f * p.Age * dt );
				p.R.Color = new Color( 0.22f, 0.2f, 0.18f, 0.9f * ( 1f - k ) );
				continue;
			}
			// rolling out and slowing, curling round the craft (the swirl) and lifting as it spreads
			var out_ = ( p.R.WorldPosition - centre ).WithZ( 0 );
			var swirl = out_.Length > 1f ? Vector3.Cross( Vector3.Up, out_.Normal ) * p.Spin * ( 1f - k ) : Vector3.Zero;
			p.R.WorldPosition += ( p.Vel * MathF.Pow( 1f - k, 1.6f ) + swirl + Vector3.Up * ( 10f + 45f * k ) ) * dt;
			if ( p.Frames is not null ) p.R.Sprite = p.Frames[Math.Min( 63, (int)( MathF.Sqrt( k ) * 63f ) )];
			var s = p.Size * ( 0.35f + 0.9f * MathF.Sqrt( k ) );
			p.R.Size = new Vector2( s, s * 0.75f );
			p.R.Color = new Color( 0.5f, 0.47f, 0.43f, 0.5f * MathF.Min( 1f, k * 5f ) * ( 1f - k ) );
		}

		Gust( strength );
	}

	void Spawn( float strength, bool grit )
	{
		var n = _ground.Normal;
		var t1 = Vector3.Cross( n, MathF.Abs( n.z ) > 0.9f ? Vector3.Forward : Vector3.Up ).Normal;
		var t2 = Vector3.Cross( n, t1 ).Normal;
		var a = Game.Random.Float( 0f, MathF.PI * 2f );
		var outward = t1 * MathF.Cos( a ) + t2 * MathF.Sin( a );
		var start = _ground.HitPosition + n * Game.Random.Float( 6f, 24f ) + outward * Game.Random.Float( 20f, 180f );
		var go = new GameObject( false, grit ? "wash grit" : "wash dust" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = start;
		go.Enabled = true;
		var r = go.Components.Create<SpriteRenderer>();
		var sheets = grit ? null : Billow.Sheets;
		var frames = sheets?[Game.Random.Int( 0, sheets.Length - 1 )];
		r.Sprite = grit ? _grit : frames?[0] ?? _dust;
		r.Lighting = !grit;
		r.Shadows = false;
		r.Color = Color.Transparent;
		var speed = ( grit ? Game.Random.Float( 500f, 900f ) : Game.Random.Float( 320f, 620f ) ) * ( 0.5f + 0.5f * strength );
		var vel = outward * speed + n * ( grit ? Game.Random.Float( 40f, 140f ) : Game.Random.Float( 0f, 30f ) );
		if ( grit ) r.Size = new Vector2( Game.Random.Float( 1.5f, 3.5f ) );
		_puffs.Add( new Puff
		{
			R = r, Vel = vel, Grit = grit, Frames = frames, Age = 0,
			Size = Game.Random.Float( 140f, 280f ) * ( 0.6f + 0.4f * strength ),
			Life = grit ? Game.Random.Float( 0.35f, 0.6f ) : Game.Random.Float( 1.4f, 2.4f ),
			Spin = Game.Random.Float( 60f, 160f ) * ( Game.Random.Float() < 0.5f ? 1f : -1f ) * strength,
		} );
	}

	/// <summary> Your own view standing in the wash: buffeting, wind streaks past the camera, the roar. Looks only. </summary>
	void Gust( float strength )
	{
		var cam = Scene.Camera;
		var g = 0f;
		if ( cam.IsValid() && strength > 0.02f )
		{
			var flat = ( cam.WorldPosition - _ground.HitPosition ).WithZ( 0 ).Length;
			var high = cam.WorldPosition.z - _ground.HitPosition.z;
			if ( high < 260f ) g = strength * Math.Clamp( 1f - flat / GustRadius, 0f, 1f );
		}

		if ( g > 0.02f )
		{
			SwrpShake.Add( 0.9f * g * g * Time.Delta );
			var every = MathX.Lerp( 0.08f, 0.012f, g );
			while ( _sinceStreak > every && _streaks.Count < 40 )
			{
				_sinceStreak -= every;
				Streak( cam, g );
			}
			if ( _sinceStreak > every ) _sinceStreak = 0;
		}
		else _sinceStreak = 0;

		for ( var i = _streaks.Count - 1; i >= 0; i-- )
		{
			var (l, pos, vel, age) = _streaks[i];
			var k = age / 0.35f;
			if ( !l.IsValid() || k >= 1f ) { l?.GameObject?.Destroy(); _streaks.RemoveAt( i ); continue; }
			var head = pos + vel * age;
			l.VectorPoints = new List<Vector3> { head - vel.Normal * 70f, head };
			l.Color = new Gradient( new Gradient.ColorFrame( 0f, new Color( 0.75f, 0.72f, 0.66f, 0f ) ), new Gradient.ColorFrame( 1f, new Color( 0.75f, 0.72f, 0.66f, 0.22f * MathF.Sin( k * MathF.PI ) ) ) );
		}

		// the roar, louder the stronger the gust where you stand
		if ( g > 0.02f )
		{
			if ( _roar is null || !_roar.IsPlaying ) _roar = Sound.Play( "sounds/ui/wind_loop.sound" );      // restarted if the wav doesn't loop
			if ( _roar is not null ) _roar.Volume = MathX.Lerp( _roar.Volume, Math.Clamp( g * 1.3f, 0f, 1f ), Time.Delta * 3f );
		}
		else if ( _roar is not null && _roar.IsPlaying )
		{
			_roar.Volume = MathX.Lerp( _roar.Volume, 0f, Time.Delta * 3f );
			if ( _roar.Volume < 0.02f ) { _roar.Stop(); _roar = null; }
		}
	}

	void Streak( CameraComponent cam, float g )
	{
		// the wind blows out from under the craft, past you
		var outward = ( cam.WorldPosition - _ground.HitPosition ).WithZ( 0 ).Normal;
		if ( outward.IsNearZeroLength ) outward = Vector3.Random.WithZ( 0 ).Normal;
		var side = Vector3.Cross( Vector3.Up, outward );
		var pos = cam.WorldPosition + cam.WorldRotation.Forward * Game.Random.Float( 40f, 160f ) + side * Game.Random.Float( -90f, 90f )
			+ Vector3.Up * Game.Random.Float( -70f, 30f ) - outward * 90f;
		var go = new GameObject( false, "wash streak" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.Enabled = true;
		var l = go.Components.Create<LineRenderer>();
		l.UseVectorPoints = true;
		l.VectorPoints = new List<Vector3> { pos, pos };
		l.Width = new Curve( new Curve.Frame( 0f, 0.1f ), new Curve.Frame( 1f, 0.5f ) );
		l.Lighting = false;
		l.CastShadows = false;
		_streaks.Add( (l, pos, ( outward + Vector3.Up * 0.08f ) * Game.Random.Float( 600f, 1000f ) * ( 0.6f + 0.4f * g ), 0) );
	}

	protected override void OnDestroy()
	{
		foreach ( var p in _puffs ) p.R?.GameObject?.Destroy();
		_puffs.Clear();
		foreach ( var s in _streaks ) s.L?.GameObject?.Destroy();
		_streaks.Clear();
		_roar?.Stop();
		_roar = null;
	}
}
