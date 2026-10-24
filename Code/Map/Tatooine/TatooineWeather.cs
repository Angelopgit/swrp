using System;

namespace Swrp.Tatooine;

/// <summary>
/// Tatooine's wind, for this machine's camera only while it's on the planet. One wind (<see cref="Wind"/>: a direction that
/// veers a little with each gust, and a strength) drives everything: the wind bed and whistle loops, gusts every several
/// seconds (gust sound, haze thickening, a slight buffet), the blown sand and the footstep dust drift. The blown sand is
/// what slow-motion desert footage and the saltation literature (Bagnold) show: faint, thin streamers snaking along the
/// ground in the wind's direction, almost all below knee height (the saltation layer is mostly under 0.3 m), a few metres
/// long, moving at a few metres a second; and on dune crests, sheets of sand lifting off and pouring over the lee side.
/// Never at the lens: they fade in from ~6 m out and out again by ~50 m. Sounds: the recorded set from
/// SoundSource/tatooine_ext_sounds.py (local only) when it's there, else SoundSource/tatooine_sounds.py's synthesized one.
/// Cosmetic and local: no networking; up to 80 line ribbons and 90 dust sprites.
/// </summary>
public static class TatooineWeather
{
	[ConVar( "swrp_tatooine_wind", Help = "Tatooine's wind: gusts, blowing sand and wind sounds (1 = on, default)", Saved = true )]
	public static bool Enabled { get; set; } = true;

	/// <summary> The prevailing wind's direction (the dunes are shaped by it: crescents' horns and slip faces point this way). </summary>
	public static readonly Vector3 Direction = new Vector3( 1f, 0.22f, 0f ).Normal;

	/// <summary> 0..1: how hard the current gust is blowing (0 between gusts). </summary>
	public static float Gust { get; private set; }

	/// <summary> The wind right now: where it blows (the prevailing direction, veering a few degrees during a gust) scaled by
	/// its strength (0.35 calm, 1 at a gust's peak). Streaks, crest sand and dust drift all read this. </summary>
	public static Vector3 Wind { get; private set; } = Direction * 0.35f;

	static SoundHandle _bed, _hiss;
	static bool? _recorded;

	/// <summary> The recorded wind (sounds/tatooine/ext) is installed on this machine. </summary>
	static bool Recorded => _recorded ??= FileSystem.Mounted.FileExists( "sounds/tatooine/ext/wind_bed.sound" );

	[ConVar( "swrp_tatooine_wind_volume", Help = "Tatooine's wind volume (0-1.5, default 0.4)", Saved = true )]
	public static float Volume { get; set; } = 0.4f;
	static float _nextGust = 6f, _gustT = -1f, _gustLength = 4f, _nextMoan = 15f, _veer, _veerTarget;
	static GameObject _sandRoot;
	static Sprite _sprite;

	internal static void Tick( Scene scene, bool onPlanet, float daylight )
	{
		var on = onPlanet && Enabled;
		Sounds( on );
		if ( !on )
		{
			Gust = 0f;
			if ( _sandRoot.IsValid() ) _sandRoot.Enabled = false;
			return;
		}

		var dt = Time.Delta;
		// gusts: rise fast, fall slowly
		_nextGust -= dt;
		if ( _gustT < 0f && _nextGust <= 0f )
		{
			_gustT = 0f;
			_gustLength = Game.Random.Float( 3.5f, 5.5f );
			_nextGust = Game.Random.Float( 7f, 16f );
			_veerTarget = Game.Random.Float( -12f, 12f );
			Play( Recorded ? "sounds/tatooine/ext/gust.sound" : "sounds/tatooine/wind_gust.sound", Game.Random.Float( 0.7f, 1f ) );
		}
		if ( _gustT >= 0f )
		{
			_gustT += dt;
			var k = _gustT / _gustLength;
			var peak = 0.42f;
			Gust = k < peak ? MathF.Pow( k / peak, 1.6f ) : MathF.Pow( MathF.Max( 0f, ( 1f - k ) / ( 1f - peak ) ), 2.2f );
			if ( k >= peak && k - dt / _gustLength < peak ) SwrpShake.Add( 0.10f );
			if ( k >= 1f ) { _gustT = -1f; Gust = 0f; }
		}
		_veer = _veer.LerpTo( _gustT >= 0f ? _veerTarget : 0f, dt * 0.6f );
		Wind = Rotation.FromYaw( _veer ) * Direction * ( 0.35f + 0.65f * Gust );

		_nextMoan -= dt;
		if ( _nextMoan <= 0f && !Recorded )
		{
			_nextMoan = Game.Random.Float( 22f, 50f );
			Play( "sounds/tatooine/distant_moan.sound", Game.Random.Float( 0.6f, 1f ) );
		}

		var v = Math.Clamp( Volume, 0f, 1.5f );
		if ( _bed is not null ) _bed.Volume = v * ( 0.75f + Gust * 0.35f );
		if ( _hiss is not null ) _hiss.Volume = v * ( Recorded ? 0.6f + Gust * 0.6f : 0.35f + Gust * 1.1f );
		EnsureRoot( scene );
		Streaks( scene, dt, daylight );
		Kicks( scene, dt );
	}

	/// <summary> The dunes' colour as they are lit right now, pushed orange like the dust (warm by day, dim at night). </summary>
	static Color SandColour() { var l = TatooinePlanet.GroundLight; return new Color( 0.90f * l.r, 0.63f * l.g, 0.34f * l.b ); }

	static void EnsureRoot( Scene scene )
	{
		if ( _sandRoot.IsValid() && _sandRoot.Scene == scene ) { _sandRoot.Enabled = true; return; }
		_streaks.Clear();
		_puffs.Clear();
		_sprite ??= Sprite.FromTexture( Texture.Load( "materials/tatooine/dust.png" ) );
		_sandRoot = scene.CreateObject();
		_sandRoot.Name = "Tatooine blowing sand";
		_sandRoot.NetworkMode = NetworkMode.Never;
		_sandRoot.Flags |= GameObjectFlags.NotSaved;
	}

	// ----- blown sand: streamers along the ground and sheets off the dune crests ------------------------------------------

	const int MaxStreaks = 160;
	const int Segments = 8;
	const float Near = 240f, Far = 2000f;      // never at the lens; gone by ~50 m, where the haze takes over

	sealed class Streak
	{
		public LineRenderer Line;
		public Vector3 Head;
		public float Len, Width, Height, Age, Life, Speed, Phase, Strength;
		public bool Sheet, Alive;
	}

	static readonly List<Streak> _streaks = new();

	/// <summary> How many streaks should be out: a few in calm air, the full set at a gust's peak. </summary>
	static int Wanted => (int)( 30 + ( MaxStreaks - 30 ) * Gust );

	static void Streaks( Scene scene, float dt, float daylight )
	{
		var cam = scene.Camera;
		if ( !cam.IsValid() ) return;
		var eye = cam.WorldPosition;
		var wind = Wind.WithZ( 0f );
		var dir = wind.Normal;
		var side = new Vector3( -dir.y, dir.x, 0f );
		// lighter than the ground: sand-on-sand at the same colour had no contrast and vanished (Angelo, 2026-10-22);
		// airborne grains catch the sun and read pale and bright against the dunes
		var colour = Color.Lerp( SandColour() * 1.3f, TatooinePlanet.GroundLight * 0.95f, 0.3f );
		var night = MathX.Lerp( 0.5f, 1f, daylight );      // still readable at dawn and dusk (0.15 hid them at 06:00)

		var alive = _streaks.Count( x => x.Alive );
		for ( var tries = 0; alive < Wanted && tries < 4; tries++ )
		{
			var s = _streaks.FirstOrDefault( x => !x.Alive ) ?? Make();
			if ( s is null ) break;
			Spawn( s, eye, dir );
			alive++;
		}

		foreach ( var s in _streaks )
		{
			if ( !s.Alive ) continue;
			s.Age += dt;
			if ( s.Age >= s.Life ) { s.Alive = false; s.Line.Enabled = false; continue; }
			// grains travel at a few metres a second, faster in the gust; the streamer itself snakes as it goes
			s.Head += dir * s.Speed * ( 0.6f + wind.Length ) * dt;
			var k = s.Age / s.Life;
			var life = MathF.Min( 1f, k * 5f ) * MathF.Min( 1f, ( 1f - k ) * 3f );
			var d = s.Head.WithZ( 0 ).Distance( eye.WithZ( 0 ) );
			var dist = Math.Clamp( ( d - Near ) / 260f, 0f, 1f ) * Math.Clamp( ( Far - d ) / 900f, 0f, 1f );
			var a = s.Strength * life * dist * night * ( s.Sheet ? 0.45f : 0.8f ) * ( 0.3f + 0.7f * Gust );     // (0.16, then 0.42, were still too faint to notice in play)
			if ( a < 0.002f ) { s.Line.Enabled = false; continue; }
			s.Line.Enabled = true;

			var pts = new List<Vector3>( Segments + 1 );
			for ( var i = 0; i <= Segments; i++ )
			{
				var u = i / (float)Segments;                     // 0 = the tail, upwind; 1 = the head
				var along = s.Head - dir * s.Len * ( 1f - u );
				var wiggle = MathF.Sin( s.Phase + u * 3.1f + s.Age * 2.3f ) * s.Len * ( s.Sheet ? 0.05f : 0.08f );
				var p = along + side * wiggle;
				// hug the ground; sheets lift off the crest and pour down the lee side with it
				p.z = TatooinePlanet.TerrainHeight( p ) + s.Height * ( s.Sheet ? 0.4f + 0.6f * MathF.Sin( u * MathF.PI ) : 0.6f + 0.4f * MathF.Sin( u * MathF.PI ) );
				pts.Add( p );
			}
			s.Line.VectorPoints = pts;
			s.Line.Color = new Gradient( new Gradient.ColorFrame( 0f, colour.WithAlpha( 0f ) ), new Gradient.ColorFrame( 0.35f, colour.WithAlpha( a ) ),
				new Gradient.ColorFrame( 0.8f, colour.WithAlpha( a * 0.7f ) ), new Gradient.ColorFrame( 1f, colour.WithAlpha( 0f ) ) );
		}
	}

	static Streak Make()
	{
		if ( _streaks.Count >= MaxStreaks || !_sandRoot.IsValid() ) return null;
		var go = new GameObject( _sandRoot, true, "streak" );
		go.NetworkMode = NetworkMode.Never;
		var line = go.Components.Create<LineRenderer>();
		line.UseVectorPoints = true;
		line.Lighting = false;
		line.CastShadows = false;
		line.Opaque = false;
		line.DepthFeather = 6f;          // soft where it meets the sand
		line.FogStrength = 0.5f;
		// a ribbon of thin grain filaments (MapSource/tatooine_textures.py sand_streak), scrolling downwind: a flat colour
		// band read as paint on the ground, not sand in the air (Angelo, 2026-10-22)
		line.Texturing = StreakTexture();         // half the haze: full haze turned far streaks into pale grey patches
		var s = new Streak { Line = line };
		_streaks.Add( s );
		return s;
	}

	static TrailTextureConfig StreakTexture()
	{
		var tex = TrailTextureConfig.Default;
		tex.Texture = Texture.Load( "materials/tatooine/sand_streak.png" );
		tex.WorldSpace = true;
		tex.UnitsPerTexture = 260f;
		tex.Scroll = -1.2f;
		return tex;
	}

	static void Spawn( Streak s, Vector3 eye, Vector3 dir )
	{
		if ( s.Line.Texturing.Texture is null ) s.Line.Texturing = StreakTexture();      // (ribbons made before a hotload)
		// anywhere in a ring round the camera, a little upwind so it streams past rather than away
		var a = Game.Random.Float( 0f, MathF.PI * 2f );
		var r = Game.Random.Float( Near + 100f, Far );
		var at = eye.WithZ( 0 ) + new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * r - dir * 300f;
		// a crest nearby? (higher than the ground just upwind and dropping away downwind): sand sheets off it
		s.Sheet = false;
		for ( var i = 0; i < 5 && Gust > 0.25f; i++ )
		{
			var c = at + new Vector3( Game.Random.Float( -600f, 600f ), Game.Random.Float( -600f, 600f ), 0f );
			var h = TatooinePlanet.TerrainHeight( c );
			if ( h - TatooinePlanet.TerrainHeight( c - dir * 250f ) > 25f && h - TatooinePlanet.TerrainHeight( c + dir * 200f ) > 30f ) { at = c; s.Sheet = true; break; }
		}
		// in a gust, wide low drifts of sand sweep across the open ground too, not just off the crests
		var drift = !s.Sheet && Gust > 0.3f && Game.Random.Float() < 0.35f;
		if ( drift ) s.Sheet = true;
		s.Head = at;
		s.Len = drift ? Game.Random.Float( 400f, 800f ) : s.Sheet ? Game.Random.Float( 200f, 420f ) : Game.Random.Float( 120f, 480f );       // 3-12 m streamers
		s.Width = drift ? Game.Random.Float( 18f, 34f ) : s.Sheet ? Game.Random.Float( 24f, 46f ) : Game.Random.Float( 10f, 26f );     // the ribbons face the camera, so width is their height: keep it under the knee
		s.Height = drift ? Game.Random.Float( 6f, 20f ) : s.Sheet ? Game.Random.Float( 14f, 40f ) : Game.Random.Float( 4f, 18f );         // mostly below the knee
		s.Speed = Game.Random.Float( 110f, 230f );
		s.Life = Game.Random.Float( 1.4f, 3.2f );
		s.Phase = Game.Random.Float( 0f, 10f );
		s.Strength = Game.Random.Float( 0.5f, 1f );
		s.Age = 0f;
		s.Alive = true;
		s.Line.Width = new Curve( new Curve.Frame( 0f, s.Width * 0.3f ), new Curve.Frame( 0.6f, s.Width ), new Curve.Frame( 1f, s.Width * 0.5f ) );
		s.Line.Enabled = false;
	}

	// ----- sand kicked up by feet ----------------------------------------------------------------------------------------

	const int MaxPuffs = 90;
	const float Stride = 60f;            // a puff about every step
	static readonly Dictionary<GameObject, (Vector3 Last, float Travelled)> _walkers = new();
	static readonly List<(SpriteRenderer R, Vector3 Vel, float Age, float Life, float Size, Color Tint)> _puffs = new();

	/// <summary> Troopers and NPCs walking on the ground near the camera kick up dust at each step: the colour of the ground
	/// under the foot, bigger and longer-lived the faster they move, drifting off on the wind. </summary>
	static void Kicks( Scene scene, float dt )
	{
		if ( !_sandRoot.IsValid() || _sprite is null ) return;
		var cam = scene.Camera.WorldPosition;
		var seen = new HashSet<GameObject>();
		void Walk( GameObject go, bool grounded )
		{
			var pos = go.WorldPosition;
			if ( !TatooinePlanet.IsOnPlanet( pos ) || pos.Distance( cam ) > 3000f ) return;
			seen.Add( go );
			var (last, travelled) = _walkers.TryGetValue( go, out var w ) ? w : (pos, 0f);
			var moved = ( pos - last ).WithZ( 0 ).Length;
			travelled = grounded && moved < 60f ? travelled + moved : 0f;      // teleports and jumps don't count
			if ( travelled >= Stride ) { travelled = 0f; Puff( pos, Math.Clamp( moved / MathF.Max( dt, 0.001f ) / 220f, 0.45f, 1.7f ) ); }
			_walkers[go] = (pos, travelled);
		}
		foreach ( var p in SwrpPlayers.Valid )
			if ( p.Controller.IsValid() && !p.Noclip ) Walk( p.GameObject, p.Controller.IsOnGround );
		foreach ( var n in scene.GetAllComponents<SwrpNpc>() )
			Walk( n.GameObject, true );
		foreach ( var gone in _walkers.Keys.Where( k => !seen.Contains( k ) ).ToList() ) _walkers.Remove( gone );

		for ( var i = _puffs.Count - 1; i >= 0; i-- )
		{
			var f = _puffs[i];
			f.Age += dt;
			if ( f.Age >= f.Life || !f.R.IsValid() ) { if ( f.R.IsValid() ) f.R.GameObject.Destroy(); _puffs.RemoveAt( i ); continue; }      // (a puff left from the last play session has no object)
			var k = f.Age / f.Life;
			// it bursts out, slows, settles a little and drifts off on the wind (harder in a gust)
			f.Vel = f.Vel * MathF.Max( 0f, 1f - dt * 2.2f ) + ( Wind * 140f + Vector3.Down * 8f ) * dt;
			f.R.WorldPosition += f.Vel * dt;
			var size = f.Size * ( 0.35f + MathF.Sqrt( k ) * 1.25f );
			f.R.Size = new Vector2( size * 1.3f, size * 0.75f );
			f.R.Color = f.Tint.WithAlpha( 0.55f * MathF.Min( 1f, k * 10f ) * MathF.Pow( 1f - k, 1.6f ) );
			_puffs[i] = f;
		}
	}

	static void Puff( Vector3 foot, float strength )
	{
		// the ground's own colour lit like the ground (the sprites are unlit, so an albedo alone came out grey), pushed
		// towards orange: fine dust in the air glows warmer than the sand it came off (Angelo, 2026-10-21)
		var g = TatooinePlanet.GroundColour( foot );
		var l = TatooinePlanet.GroundLight;
		var tint = new Color( g.r * l.r * 1.12f, g.g * l.g * 0.86f, g.b * l.b * 0.58f );
		for ( var n = 0; n < ( strength > 1f ? 3 : 2 ) && _puffs.Count < MaxPuffs; n++ )
		{
			var go = new GameObject( _sandRoot, true, "kick" );
			go.NetworkMode = NetworkMode.Never;
			go.WorldPosition = foot + Vector3.Up * ( 4f + n * 6f );
			var r = go.Components.Create<SpriteRenderer>();
			r.Sprite = _sprite;
			r.Lighting = false;
			r.Shadows = false;
			r.Color = Color.Transparent;
			var vel = ( Vector3.Random.WithZ( 0 ) * 55f + Vector3.Up * Game.Random.Float( 20f, 55f ) ) * strength;
			_puffs.Add( (r, vel, 0f, Game.Random.Float( 1.6f, 2.6f ) * MathF.Sqrt( strength ), Game.Random.Float( 70f, 110f ) * strength, tint) );
		}
	}

	static void Play( string sound, float volume )
	{
		var h = Sound.Play( sound );
		if ( h is not null ) h.Volume *= volume * Math.Clamp( Volume, 0f, 1.5f );
	}

	static void Sounds( bool on )
	{
		// the loops restart when they end (24 s and 14 s, so the seam hides under the gusts)
		if ( on )
		{
			// recorded: the calm wind bed with a thin whistle over it; synthesized: the wind bed and the sand hiss
			if ( _bed is null || !_bed.IsPlaying ) _bed = Sound.Play( Recorded ? "sounds/tatooine/ext/wind_bed.sound" : "sounds/tatooine/desert_wind.sound" );
			if ( _hiss is null || !_hiss.IsPlaying ) _hiss = Sound.Play( Recorded ? "sounds/tatooine/ext/wind_whistle.sound" : "sounds/tatooine/sand_hiss.sound" );
		}
		else
		{
			_bed?.Stop( 1.5f );
			_hiss?.Stop( 1.5f );
			_bed = _hiss = null;
		}
	}

	internal static void Clear()
	{
		Sounds( false );
		_puffs.Clear();
		_walkers.Clear();
		if ( _sandRoot.IsValid() ) _sandRoot.Destroy();
		_sandRoot = null;
		_streaks.Clear();
		Gust = 0f;
	}
}
