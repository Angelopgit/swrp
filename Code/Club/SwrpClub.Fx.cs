using System;

namespace Swrp;

/// <summary> Colour sets for the club lights (the DJ picks one). </summary>
public static class ClubPalettes
{
	public static readonly (string Name, Color[] Colors)[] All =
	{
		( "Coruscant", new[] { new Color( 0.2f, 0.45f, 1f ), new Color( 0.85f, 0.2f, 1f ), new Color( 0.1f, 0.95f, 1f ), new Color( 1f, 0.25f, 0.6f ) } ),
		( "Sith", new[] { new Color( 1f, 0.08f, 0.05f ), new Color( 1f, 0.35f, 0.05f ), new Color( 0.7f, 0f, 0.25f ), new Color( 1f, 0.75f, 0.6f ) } ),
		( "Jedi", new[] { new Color( 0.15f, 1f, 0.3f ), new Color( 0.2f, 0.5f, 1f ), new Color( 0.95f, 0.95f, 1f ), new Color( 0.55f, 0.2f, 1f ) } ),
		( "Mustafar", new[] { new Color( 1f, 0.5f, 0.05f ), new Color( 1f, 0.85f, 0.2f ), new Color( 1f, 0.15f, 0.1f ), new Color( 0.3f, 0.1f, 1f ) } ),
	};

	public static int Count => All.Length;

	public static Color Pick( int palette, int i )
	{
		var set = All[Math.Clamp( palette, 0, All.Length - 1 )].Colors;
		return set[( ( i % set.Length ) + set.Length ) % set.Length];
	}
}

/// <summary>
/// The club's lights, on every machine whose camera is in (or at the door of) the cantina: four moving heads on the ceiling with
/// visible beams sweeping the dance floor, floor washes, a fan of lasers from the booth at head height, a strobe on the beat, drifting
/// coloured haze, and a dim/haze light zone for the room. Movement runs on the host's clock so everyone sees the same sweep; the pulse
/// follows the music this machine hears, which is in step. Nothing here is networked. swrp_club_strobe 0 turns the strobe off for you.
/// The FX root carries <see cref="SwrpLightZoneTag"/> so the ship's lamp dimming leaves the club lights alone.
/// </summary>
public sealed partial class SwrpClub
{
	[ConVar( "swrp_club_strobe", Help = "Show the club strobe flashes (0 = off, for you only)", Saved = true )]
	public static bool StrobeAllowed { get; set; } = true;

	[ConVar( "swrp_club_fx_always", Help = "Debug: build the club FX even when your camera is outside the cantina" )]
	public static bool FxAlways { get; set; }

	// ceiling over the booth and dance floor is at z -617 (VMF brush x -1730..-1119, y 3868..4253)
	static readonly Vector3 LaserOrigin = BoothPos + new Vector3( 0f, 8f, 58f );        // just above the desk front, head height
	static readonly Vector3[] SpotMounts =
	{
		new( -1680f, 4000f, -628f ), new( -1325f, 4000f, -628f ),
		new( -1680f, 4240f, -628f ), new( -1325f, 4240f, -628f ),
	};
	const int LaserCount = 10;
	const int HazeCount = 16;

	/// <summary> The booth screen's centre: hung above the DJ, in front of the back-wall dressing (wall face y 3868). </summary>
	static readonly Vector3 ScreenPos = new( -1503f, 3925f, -748f );

	sealed class Puff
	{
		public SpriteRenderer R;
		public Vector3 Home;
		public float Seed;
	}

	GameObject _fx;
	SpotLight[] _spots;
	LineRenderer[] _beams;
	SpriteRenderer[] _lenses;
	PointLight[] _washes;
	PointLight _strobe;
	LineRenderer[] _lasers, _laserGlow;
	SpriteRenderer[] _laserDots;
	Puff[] _haze;
	GameObject _screen;
	LightZone _zone;
	(bool Lights, bool Haze) _zoneKey;

	float _beatAvg;
	RealTimeSince _sinceBeat = 10f;
	float _pulse;           // 1 on a beat, falls away
	float _strobeT;

	static Sprite _puffSprite, _glowSprite;

	void FxTick()
	{
		var cam = Scene.Camera;
		var near = FxAlways || cam.IsValid() && Room.Grow( 120f ).Contains( cam.WorldPosition );
		if ( !near )
		{
			ClearFx();
			return;
		}

		if ( !_fx.IsValid() ) BuildFx();

		var t = (float)( Now % 3600.0 );
		Beat( t );

		// ----- the dim / haze zone -----
		var key = ( Lights || Lasers || Strobe, Haze );
		if ( _zone is null || key != _zoneKey )
		{
			if ( _zone is not null ) LightZones.Remove( _zone );
			_zone = null;
			_zoneKey = key;
			if ( key.Item1 || key.Item2 )
			{
				_zone = new LightZone( "Cantina Club", Room,
					Ambient: key.Item1 ? 0.6f : 0.95f,
					Exposure: key.Item1 ? 0.85f : 1f,
					Fog: key.Item2 ? 1.2f : 0.25f,
					FogColor: Color.Lerp( ClubPalettes.Pick( Palette, 0 ), new Color( 0.6f, 0.6f, 0.65f ), 0.5f ),
					Lamps: key.Item1 ? 0.45f : 1f );
				Lighting.AddZone( _zone );
			}
		}

		var colorStep = (int)( t / 4f );       // a new colour every four seconds, the same everywhere

		// ----- moving heads: the light, its visible beam and the lens glow -----
		for ( var i = 0; i < _spots.Length; i++ )
		{
			var s = _spots[i];
			s.Enabled = Lights;
			_beams[i].Enabled = Lights;
			_lenses[i].Enabled = Lights;
			if ( !Lights ) continue;
			var ph = t * ( 0.5f + 0.07f * i ) + i * 1.7f;
			var target = FloorPos + new Vector3( MathF.Sin( ph ) * 300f, MathF.Cos( ph * 0.8f ) * 260f, 2f );
			var from = s.WorldPosition;
			s.WorldRotation = Rotation.LookAt( target - from );
			var c = ClubPalettes.Pick( Palette, colorStep + i );
			s.LightColor = c * ( 6f + 10f * _pulse );
			_beams[i].VectorPoints = new List<Vector3> { from, target };
			// additive lines ignore alpha, so the beam's softness is in how dark its colour is
			_beams[i].Color = new Gradient( new Gradient.ColorFrame( 0f, ( c * ( 0.9f + 0.6f * _pulse ) ).WithAlpha( 1f ) ), new Gradient.ColorFrame( 1f, ( c * 0.1f ).WithAlpha( 1f ) ) );
			_lenses[i].Color = ( c * ( 3f + 3f * _pulse ) ).WithAlpha( 1f );
		}
		for ( var i = 0; i < _washes.Length; i++ )
		{
			var w = _washes[i];
			w.Enabled = Lights;
			if ( !Lights ) continue;
			w.LightColor = ClubPalettes.Pick( Palette, colorStep + i + 2 ) * ( 1.5f + 4f * _pulse );
		}

		// ----- strobe -----
		_strobeT = MathF.Max( 0f, _strobeT - Time.Delta );
		_strobe.Enabled = Strobe && StrobeAllowed && _strobeT > 0f;

		// ----- lasers: a fan at head height across the dance floor, with a dot where each one lands -----
		for ( var i = 0; i < _lasers.Length; i++ )
		{
			var on = Lasers;
			_lasers[i].Enabled = on;
			_laserGlow[i].Enabled = on;
			_laserDots[i].Enabled = on;
			if ( !on ) continue;
			var spread = ( i / (float)( LaserCount - 1 ) - 0.5f ) * 100f;
			var yaw = 90f + spread + MathF.Sin( t * 0.8f + i * 0.35f ) * 14f;
			var pitch = 1f + 7f * ( 0.5f + 0.5f * MathF.Sin( t * 0.55f + i * 0.6f ) );       // positive is down
			var dir = new Angles( pitch, yaw, 0f ).Forward;
			var tr = Scene.Trace.Ray( LaserOrigin, LaserOrigin + dir * 1600f ).IgnoreGameObjectHierarchy( GameObject ).Run();
			var end = tr.Hit ? tr.EndPosition : LaserOrigin + dir * 1600f;
			var pts = new List<Vector3> { LaserOrigin, end };
			_lasers[i].VectorPoints = pts;
			_laserGlow[i].VectorPoints = pts;
			var c = ClubPalettes.Pick( Palette, colorStep + ( i % 2 ) );
			_lasers[i].Color = new Gradient( new Gradient.ColorFrame( 0f, ( c * 3f ).WithAlpha( 1f ) ), new Gradient.ColorFrame( 1f, ( c * 1.6f ).WithAlpha( 1f ) ) );
			_laserGlow[i].Color = new Gradient( new Gradient.ColorFrame( 0f, ( c * ( 0.7f + 0.4f * _pulse ) ).WithAlpha( 1f ) ), new Gradient.ColorFrame( 1f, ( c * 0.25f ).WithAlpha( 1f ) ) );
			_laserDots[i].WorldPosition = end - dir * 2f;
			_laserDots[i].Color = ( c * 6f ).WithAlpha( 1f );
		}

		// ----- haze: big soft puffs drifting through the room, tinted by the lights -----
		for ( var i = 0; i < _haze.Length; i++ )
		{
			var p = _haze[i];
			p.R.Enabled = Haze;
			if ( !Haze ) continue;
			var drift = new Vector3( MathF.Sin( t * 0.07f + p.Seed ) * 90f, MathF.Cos( t * 0.05f + p.Seed * 1.3f ) * 90f, MathF.Sin( t * 0.11f + p.Seed * 0.7f ) * 18f );
			p.R.WorldPosition = p.Home + drift;
			var c = Lights || Lasers ? ClubPalettes.Pick( Palette, colorStep + i ) : new Color( 0.75f, 0.75f, 0.8f );
			p.R.Color = ( c * ( 0.08f + 0.06f * _pulse ) ).WithAlpha( 1f );
		}

		// ----- the booth screen -----
		if ( _screen.IsValid() ) _screen.Enabled = ShowScreen;
	}

	void Beat( float t )
	{
		var hearing = Level > 0.005f;
		var beat = false;
		if ( hearing )
		{
			_beatAvg = _beatAvg.LerpTo( Level, Time.Delta * 1.5f );
			if ( Level > _beatAvg * 1.3f + 0.01f && _sinceBeat > 0.28f ) beat = true;
		}
		else if ( Loaded( LiveDeck ) && PausedAt( LiveDeck ) < 0f )
		{
			// nothing measurable (a YouTube video, or nothing loaded here yet): a steady 120 bpm on the host clock
			var phase = t * 2f % 1f;
			if ( phase < 0.1f && _sinceBeat > 0.3f ) beat = true;
		}
		if ( beat )
		{
			_sinceBeat = 0;
			_pulse = 1f;
			if ( Strobe ) _strobeT = 0.05f;      // at most about four flashes a second
		}
		_pulse = MathF.Max( 0f, _pulse - Time.Delta * 3.5f );
	}

	/// <summary> 0..1 beat pulse, for the screen. </summary>
	public float Pulse => _pulse;

	void BuildFx()
	{
		_fx = new GameObject( GameObject, true, "Club FX" );
		_fx.NetworkMode = NetworkMode.Never;
		_fx.Flags |= GameObjectFlags.NotSaved;
		_fx.WorldPosition = WorldPosition;
		_fx.Components.Create<SwrpLightZoneTag>();       // the room's lamp dimming must not grab these

		_puffSprite ??= Sprite.FromTexture( Texture.Load( "materials/fx/steam.png" ) );
		_glowSprite ??= Sprite.FromTexture( Texture.Load( "materials/fx/glow.png" ) );
		var box = Model.Load( "models/dev/box.vmdl" );
		var dark = Material.Load( "materials/crew/droid_dark.vmat" );

		_spots = new SpotLight[SpotMounts.Length];
		_beams = new LineRenderer[SpotMounts.Length];
		_lenses = new SpriteRenderer[SpotMounts.Length];
		for ( var i = 0; i < SpotMounts.Length; i++ )
		{
			var go = Child( $"Moving Head {i}", SpotMounts[i] );
			var s = go.Components.Create<SpotLight>();
			s.Radius = 1100f;
			s.ConeOuter = 24f;
			s.ConeInner = 14f;
			s.Shadows = false;
			s.LightColor = Color.Black;
			_spots[i] = s;

			// a small dark housing on the ceiling so the light comes from something
			var house = Child( $"Moving Head {i} Body", SpotMounts[i] + Vector3.Up * 4f );
			house.WorldScale = new Vector3( 14f, 14f, 10f ) / box.Bounds.Size;
			var hr = house.Components.Create<ModelRenderer>();
			hr.Model = box;
			if ( dark is not null ) hr.MaterialOverride = dark;
			hr.RenderType = ModelRenderer.ShadowRenderType.Off;

			_beams[i] = Line( Child( $"Moving Head {i} Beam", SpotMounts[i] ), 2f, 16f );
			_lenses[i] = Glow( Child( $"Moving Head {i} Lens", SpotMounts[i] + Vector3.Down * 3f ), 26f );
		}

		_washes = new PointLight[2];
		for ( var i = 0; i < 2; i++ )
		{
			var w = Child( $"Wash {i}", FloorPos + new Vector3( i == 0 ? -200f : 200f, 0f, 70f ) ).Components.Create<PointLight>();
			w.Radius = 520f;
			w.Shadows = false;
			w.LightColor = Color.Black;
			_washes[i] = w;
		}

		_strobe = Child( "Strobe", FloorPos + Vector3.Up * 170f ).Components.Create<PointLight>();
		_strobe.Radius = 1200f;
		_strobe.Shadows = false;
		_strobe.LightColor = new Color( 1f, 1f, 1.05f ) * 20f;
		_strobe.Enabled = false;

		_lasers = new LineRenderer[LaserCount];
		_laserGlow = new LineRenderer[LaserCount];
		_laserDots = new SpriteRenderer[LaserCount];
		for ( var i = 0; i < LaserCount; i++ )
		{
			var go = Child( $"Laser {i}", LaserOrigin );
			_lasers[i] = Line( go, 1.2f, 1.8f );
			_laserGlow[i] = Line( Child( $"Laser {i} Glow", LaserOrigin ), 4f, 7f );
			_laserDots[i] = Glow( Child( $"Laser {i} Dot", LaserOrigin ), 14f );
		}

		_haze = new Puff[HazeCount];
		var rng = new Random( 1313 );
		for ( var i = 0; i < HazeCount; i++ )
		{
			var home = new Vector3( MathX.Lerp( Room.Mins.x + 120f, Room.Maxs.x - 120f, (float)rng.NextDouble() ),
				MathX.Lerp( 3950f, 4500f, (float)rng.NextDouble() ),
				MathX.Lerp( -780f, -660f, (float)rng.NextDouble() ) );
			var r = Child( $"Haze {i}", home ).Components.Create<SpriteRenderer>();
			r.Sprite = _puffSprite;
			r.Lighting = false;
			r.Shadows = false;
			r.Additive = true;
			r.Size = new Vector2( 1f, 1f ) * MathX.Lerp( 220f, 360f, (float)rng.NextDouble() );
			r.Color = Color.White.WithAlpha( 0f );
			r.Enabled = false;
			_haze[i] = new Puff { R = r, Home = home, Seed = i * 2.3f };
		}

		// the screen above the DJ: a spectrum visualiser, or the official YouTube player for a video track
		_screen = Child( "Booth Screen", ScreenPos );
		_screen.WorldRotation = Rotation.LookAt( new Vector3( 0f, 1f, 0f ) );       // facing the dance floor (+Y)
		_screen.WorldScale = new Vector3( 3f );
		var wp = _screen.Components.Create<WorldPanel>();
		wp.PanelSize = new Vector2( 1280, 720 );
		wp.LookAtCamera = false;
		wp.RenderOptions.Game = true;
		_screen.Components.Create<ClubScreen>().Club = this;
	}

	static LineRenderer Line( GameObject go, float startWidth, float endWidth )
	{
		var l = go.Components.Create<LineRenderer>();
		l.UseVectorPoints = true;
		l.VectorPoints = new List<Vector3> { go.WorldPosition, go.WorldPosition };
		l.Width = new Curve( new Curve.Frame( 0f, startWidth ), new Curve.Frame( 1f, endWidth ) );
		l.Opaque = false;        // the default is opaque, which ignores Additive and draws solid slabs
		l.Additive = true;
		l.Lighting = false;
		l.CastShadows = false;
		l.Enabled = false;
		return l;
	}

	SpriteRenderer Glow( GameObject go, float size )
	{
		var r = go.Components.Create<SpriteRenderer>();
		r.Sprite = _glowSprite;
		r.Lighting = false;
		r.Shadows = false;
		r.Additive = true;
		r.Size = new Vector2( size, size );
		r.Color = Color.Black;
		r.Enabled = false;
		return r;
	}

	GameObject Child( string name, Vector3 at )
	{
		var go = new GameObject( _fx, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = at;
		return go;
	}

	void ClearFx()
	{
		if ( _zone is not null ) LightZones.Remove( _zone );
		_zone = null;
		if ( _fx.IsValid() ) _fx.Destroy();
		_fx = null;
		_screen = null;
		_spots = null;
		_beams = null;
		_lenses = null;
		_washes = null;
		_lasers = null;
		_laserGlow = null;
		_laserDots = null;
		_haze = null;
		_strobe = null;
	}
}
