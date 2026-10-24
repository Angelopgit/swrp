using System;

namespace Swrp;

/// <summary>
/// An area with its own light level: <see cref="Ambient"/> scales the ambient fill (0.1 = a dark room where only flashes,
/// sabers and lamps show), <see cref="Exposure"/> caps the camera's auto-exposure so it can't brighten the dark straight back
/// up, <see cref="Fog"/> adds volumetric haze (tinted <see cref="FogColor"/>) that the dynamic lights glow in, and
/// <see cref="Lamps"/> dims the room's own lights inside the box (0.05 = emergency-lit, so a blaster shot owns the room).
/// </summary>
public sealed record LightZone( string Name, BBox Box, float Ambient = 1f, float Exposure = 1f, float Fog = 0f, Color? FogColor = null, float Lamps = 1f );

/// <summary>
/// Put on an object to make a light zone in a scene (its Box is in the object's space). Code can also add zones through
/// <see cref="Lighting.AddZone"/>; the Venator's dark rooms are listed in <see cref="LightZones.Defaults"/>.
/// </summary>
[Title( "SWRP Light Zone" ), Category( "SWRP" ), Icon( "contrast" )]
public sealed class SwrpLightZone : Component
{
	[Property] public BBox Box { get; set; } = new( new Vector3( -256, -256, -64 ), new Vector3( 256, 256, 256 ) );
	[Property, Range( 0f, 2f )] public float Ambient { get; set; } = 0.25f;
	[Property, Range( 0.1f, 2f )] public float Exposure { get; set; } = 0.6f;
	[Property, Range( 0f, 4f )] public float Fog { get; set; } = 0.4f;
	[Property] public Color FogColor { get; set; } = new( 0.55f, 0.6f, 0.7f );
	[Property, Range( 0f, 1f )] public float Lamps { get; set; } = 0.15f;

	LightZone _zone;

	protected override void OnEnabled()
	{
		_zone = new LightZone( GameObject.Name, Box.Transform( WorldTransform ), Ambient, Exposure, Fog, FogColor, Lamps );
		LightZones.Add( _zone );
	}

	protected override void OnDisabled() => LightZones.Remove( _zone );

	protected override void DrawGizmos()
	{
		Gizmo.Draw.Color = Color.Yellow.WithAlpha( 0.4f );
		Gizmo.Draw.LineBBox( Box );
	}
}

/// <summary>
/// The camera's light zone, applied on top of everyone else's ambient and exposure: at the start of each frame the fill and
/// exposure go back to what the other systems (DEFCON tint, the hull's space mood) left them, at the end this scales them by
/// the zone's values. So nobody has to know about anybody else, and nothing compounds. Blends over about a second as you
/// walk between zones.
/// </summary>
public static class LightZones
{
	[ConVar( "swrp_light_zones", Help = "Light zones: dark rooms (lower ambient, capped exposure) and haze (1 = on)", Saved = true )]
	public static bool On { get; set; } = true;

	/// <summary>
	/// One plain lighting for the whole ship (Angelo, 2026-10-12: "one general lighting... minimalistic and simple, with an HD
	/// feel"): no per-room zones, haze or lamp dimming, no room tint or moving reflection probe (RoomLight), no shadow
	/// re-picking (LampShadows), no failing-tube flicker (SwrpLightFx), and a fixed camera exposure instead of auto-exposure.
	/// The map's lamps, one ambient fill, bloom and the combat lights (muzzle, bolts, impacts) do the lighting. 0 brings the
	/// layered systems back as they were.
	/// </summary>
	[ConVar( "swrp_lighting_simple", Help = "One plain lighting for the whole ship (1); 0 = the layered room zones, tints, shadows and flicker", Saved = true )]
	public static bool Simple { get; set; } = true;

	[ConVar( "swrp_light_exposure", Help = "Simple lighting: the camera's fixed exposure (swrp_brightness scales it)", Saved = true )]
	public static float FixedExposure { get; set; } = 1.4f;

	[ConVar( "swrp_light_fog", Help = "Volumetric haze in light zones that have it (1 = on, 0 = off for slower PCs)", Saved = true )]
	public static bool FogOn { get; set; } = true;

	[ConVar( "swrp_light_min_ambient", Help = "Darkest a light zone may make the ambient fill (share of normal; 0.35 = dark rooms stay readable)", Saved = true )]
	public static float MinAmbient { get; set; } = 0.3f;

	[ConVar( "swrp_light_min_exposure", Help = "Lowest a light zone may cap the camera's exposure (share of normal)", Saved = true )]
	public static float MinExposure { get; set; } = 0.7f;

	[ConVar( "swrp_light_min_lamps", Help = "Dimmest a light zone may make a room's own lamps (share of normal)", Saved = true )]
	public static float MinLamps { get; set; } = 0.3f;

	[ConVar( "swrp_light_exposure_floor", Help = "Lowest auto-exposure the camera may drop to (0 = the scene's own); higher keeps rooms readable when bright screens or holograms are in view", Saved = true )]
	public static float ExposureFloor { get; set; } = 1f;      // was 2.4, which held every room bright and flat (Angelo, 2026-10-12: match the original's darker feel)

	/// <summary> The one knob for how bright the whole ship looks (camera exposure, every room alike). </summary>
	[ConVar( "swrp_brightness", Help = "How bright the whole ship looks (1 = as tuned; 1.2 brighter, 0.8 darker)", Saved = true )]
	public static float Brightness { get; set; } = 1f;

	[ConVar( "swrp_light_exposure_span", Help = "How far below its maximum the auto-exposure may drop (share; 1 = fixed exposure, lower = more adaptation)", Saved = true )]
	public static float ExposureSpan { get; set; } = 0.55f;

	[ConVar( "swrp_light_exposure_rate", Help = "How fast the eye adapts (the engine's auto-exposure rate; lower is slower)", Saved = true )]
	public static float ExposureRate { get; set; } = 0.35f;

	[ConVar( "swrp_light_ambient_scale", Help = "Scales the ambient fill everywhere on top of the zones (1 = as authored)", Saved = true )]
	public static float AmbientScale { get; set; } = 1f;      // 0.75 was too dark in play (Angelo, 2026-10-12): moody but readable

	[ConVar( "swrp_light_ambient", Help = "Dev: force the ambient multiplier everywhere (-1 = use the zones; 0.1 = very dark)" )]
	public static float AmbientOverride { get; set; } = -1f;

	/// <summary> Set each frame by whatever owns the camera's outdoor light (Tatooine's night dims the ship's ambient fill); 1 aboard. </summary>
	public static float OutsideScale { get; set; } = 1f;

	/// <summary> A staged shot's own fill, also in simple lighting (SwrpMenuStage: the character screen's hangar, so its key shapes the armour). </summary>
	public static float StageAmbient { get; set; } = 1f;

	/// <summary>
	/// The Venator's own zones. Empty by default: the map thread owns where rooms are dark (it can add them here or with
	/// <see cref="SwrpLightZone"/>). swrp_light_zone_here makes one round the camera for testing.
	/// </summary>
	static LightZone[] Defaults() => new[]
	{
		// one scale ship-wide (map thread, Angelo's unified-style ask; the smallest box wins): the whole ship a little moodier
		// than the old flat fill, working spaces darker and hazier, the hangar and bridges bright, the medbay clean
		// re-tuned 2026-10-11 for the PBR ship (HD pivot): measured mean luma against STYLE_GUIDE.md section 3
		new LightZone( "Ship", new BBox( new Vector3( -6100f, -12020f, -2900f ), new Vector3( 6100f, 12030f, 3430f ) ), Ambient: 0.9f ),
		new LightZone( "Main hangar", new BBox( new Vector3( -2600f, -11000f, -1300f ), new Vector3( 2600f, 2800f, 450f ) ), Ambient: 1f, Exposure: 0.85f ),     // was 1.55 / 1.2: with the narrower exposure window it washed the hangar out (Angelo, 2026-10-12)
		new LightZone( "Port bridge", new BBox( new Vector3( -1700f, 3000f, 2900f ), new Vector3( -200f, 6200f, 3800f ) ), Ambient: 0.9f, Fog: 0.1f, FogColor: new Color( 0.5f, 0.58f, 0.7f ) ),
		new LightZone( "Starboard bridge", new BBox( new Vector3( 200f, 3000f, 2900f ), new Vector3( 1700f, 6200f, 3800f ) ), Ambient: 0.9f, Fog: 0.1f, FogColor: new Color( 0.5f, 0.58f, 0.7f ) ),
		new LightZone( "Lower deck corridors", new BBox( new Vector3( -1150f, 3500f, -1960f ), new Vector3( 1150f, 4880f, -1500f ) ), Ambient: 1.8f, Exposure: 1.3f ),     // engineering corridors: few lamps of their own
		new LightZone( "Medbay", new BBox( new Vector3( -2500f, 4800f, -1260f ), new Vector3( -1300f, 5600f, -1000f ) ), Ambient: 0.75f, Exposure: 0.85f ),
		new LightZone( "Engine room", new BBox( new Vector3( -420f, 4880f, -1960f ), new Vector3( 430f, 6000f, -1450f ) ), Ambient: 1f, Exposure: 1.25f, Fog: 0.3f, FogColor: new Color( 0.62f, 0.5f, 0.38f ), Lamps: 0.9f ),
		new LightZone( "Centrifuge room", new BBox( new Vector3( -2100f, 3550f, -1900f ), new Vector3( -1150f, 4650f, -1500f ) ), Ambient: 1f, Exposure: 1.25f, Fog: 0.25f, FogColor: new Color( 0.5f, 0.55f, 0.62f ), Lamps: 0.9f ),
		// the brig's detention hall and gallery: readable, a light haze with only a hint of red (the cells' red lamps are the
		// accent, notorious_scene.py); it was dim, capped and red-fogged, which Angelo found too dark to see (2026-10-12)
		new LightZone( "Brig", new BBox( new Vector3( 1150f, 4200f, -1260f ), new Vector3( 3800f, 6200f, -640f ) ), Ambient: 0.95f, Exposure: 1f, Fog: 0.12f, FogColor: new Color( 0.58f, 0.52f, 0.54f ), Lamps: 1f ),
	};

	static readonly List<LightZone> _zones = new();
	static bool _defaultsAdded;
	static Scene _scene;

	public static string CurrentName { get; private set; }
	public static float AmbientNow { get; private set; } = 1f;
	public static float ExposureNow { get; private set; } = 1f;
	public static float FogNow { get; private set; }
	public static float LampsNow { get; private set; } = 1f;

	// what we wrote, and what it was before we scaled it
	static AmbientLight _ambient;
	static Color _ambientBase, _ambientWritten;
	static bool _ambientApplied;
	static Tonemapping _tone;
	static Vector2 _toneBase, _toneWritten;
	static bool _toneApplied;

	// the room's own lamps in the camera's zone: base colour and what we wrote
	static readonly List<(Light L, Color Base, Color Written)> _lamps = new();
	static LightZone _lampZone;
	static RealTimeSince _sinceLampScan;

	// haze
	static GameObject _fogRoot;
	static readonly Dictionary<LightZone, VolumetricFogVolume> _fog = new();

	public static void Add( LightZone z ) { if ( z is not null && !_zones.Contains( z ) ) _zones.Add( z ); }
	public static void Remove( LightZone z ) { if ( z is null ) return; _zones.Remove( z ); if ( _fog.Remove( z, out var f ) && f.IsValid() ) f.GameObject.Destroy(); }
	public static IReadOnlyList<LightZone> All => _zones;

	static void Reset( Scene scene )
	{
		_scene = scene;
		_ambient = null;
		_tone = null;
		_ambientApplied = _toneApplied = false;
		_fogRoot = null;
		_fog.Clear();
		_lamps.Clear();
		_lampZone = null;
		AmbientNow = ExposureNow = LampsNow = 1f;
		FogNow = 0f;
		// the ship's rooms afresh each play session, so retuned defaults apply after a hotload (static lists survive it)
		var fresh = Defaults();
		_zones.RemoveAll( z => fresh.Any( f => f.Name == z.Name ) );
		foreach ( var z in fresh ) Add( z );
		_defaultsAdded = true;
	}

	/// <summary> Start of frame: undo last frame's scaling, if nobody has changed the values since. </summary>
	public static void Restore( Scene scene )
	{
		if ( scene != _scene ) return;
		RestoreAll();
	}

	/// <summary> The game's own scene while playing: never write into the editor's copy (it gets saved). </summary>
	public static bool Live( Scene scene ) => scene is not null && Game.IsPlaying && scene == Game.ActiveScene;

	/// <summary> Put back everything we scaled. The 'written' values are kept, so if this is ever missed (a hotload between
	/// frames) the next Apply still sees its own value and reuses the stored base instead of scaling it again. </summary>
	static void RestoreAll()
	{
		if ( _ambientApplied && _ambient.IsValid() && _ambient.Color == _ambientWritten ) _ambient.Color = _ambientBase;
		_ambientApplied = false;
		if ( _toneApplied && _tone.IsValid() && new Vector2( _tone.MinimumExposure, _tone.MaximumExposure ) == _toneWritten )
		{
			_tone.MinimumExposure = _toneBase.x;
			_tone.MaximumExposure = _toneBase.y;
		}
		_toneApplied = false;
		for ( var i = 0; i < _lamps.Count; i++ )
		{
			var (l, b, w) = _lamps[i];
			if ( l.IsValid() && l.LightColor == w ) l.LightColor = b;
		}
	}

	/// <summary> End of frame: find the camera's zone, blend towards it, scale ambient and exposure, place the haze. </summary>
	public static void Apply( Scene scene, CameraComponent cam, float dt )
	{
		if ( !Live( scene ) ) { if ( scene == _scene ) RestoreAll(); return; }
		if ( scene != _scene ) Reset( scene );
		if ( !_defaultsAdded ) { _defaultsAdded = true; foreach ( var z in Defaults() ) Add( z ); }
		if ( !cam.IsValid() ) return;

		var pos = cam.WorldPosition;
		LightZone zone = null;
		var best = float.MaxValue;
		if ( On && !Simple )
			foreach ( var z in _zones )
			{
				if ( !z.Box.Contains( pos ) ) continue;
				var vol = z.Box.Size.x * z.Box.Size.y * z.Box.Size.z;      // the smallest box wins (a closet inside a hall)
				if ( vol < best ) { best = vol; zone = z; }
			}
		CurrentName = zone?.Name;

		// zones are an opt-in mood: the floors keep any room readable (the dev override can still go darker)
		var wantAmbient = ( AmbientOverride >= 0f ? AmbientOverride : MathF.Max( zone?.Ambient ?? 1f, MinAmbient ) ) * OutsideScale * StageAmbient;
		var wantExposure = AmbientOverride >= 0f ? MathX.Lerp( 0.45f, 1f, Math.Clamp( AmbientOverride, 0f, 1f ) ) : MathF.Max( zone?.Exposure ?? 1f, MinExposure );
		var k = Math.Min( 1f, dt * 2f );
		AmbientNow = AmbientNow.LerpTo( wantAmbient * ( AmbientOverride >= 0f ? 1f : MathF.Max( 0.05f, AmbientScale ) ), k );
		ExposureNow = ExposureNow.LerpTo( wantExposure, k );
		FogNow = FogNow.LerpTo( FogOn && On && !Simple ? zone?.Fog ?? 0f : 0f, k );
		LampsNow = LampsNow.LerpTo( MathF.Max( zone?.Lamps ?? 1f, MinLamps ), k );

		// ambient fill
		_ambient ??= scene.GetAllComponents<AmbientLight>().FirstOrDefault();
		var hue = RoomLight.Hue;     // the room's colour (RoomLight), white when off
		if ( _ambient.IsValid() && ( MathF.Abs( AmbientNow - 1f ) > 0.002f || hue != Color.White ) )
		{
			if ( _ambient.Color != _ambientWritten ) _ambientBase = _ambient.Color;     // still our value: the restore was missed, keep the base
			_ambientWritten = new Color( _ambientBase.r * AmbientNow * hue.r, _ambientBase.g * AmbientNow * hue.g, _ambientBase.b * AmbientNow * hue.b, _ambientBase.a );
			_ambient.Color = _ambientWritten;
			_ambientApplied = true;
		}

		// exposure: hold the auto-exposure down so a dark room stays dark (and a flash makes it adapt, briefly)
		if ( !_tone.IsValid() ) _tone = cam.GetComponent<Tonemapping>();
		if ( _tone.IsValid() && Simple )
		{
			// fixed: the same exposure everywhere, never adapting to what's in view
			var nowTone = new Vector2( _tone.MinimumExposure, _tone.MaximumExposure );
			if ( nowTone != _toneWritten ) _toneBase = nowTone;
			var e = MathF.Max( 0.1f, FixedExposure * Math.Clamp( Brightness, 0.3f, 3f ) );
			_toneWritten = new Vector2( e, e );
			_tone.MinimumExposure = e;
			_tone.MaximumExposure = e;
			_toneApplied = true;
		}
		else if ( _tone.IsValid() && ( MathF.Abs( ExposureNow - 1f ) > 0.002f || ExposureFloor > 0f ) )
		{
			var nowTone = new Vector2( _tone.MinimumExposure, _tone.MaximumExposure );
			if ( nowTone != _toneWritten ) _toneBase = nowTone;
			var max = _toneBase.y * ExposureNow * Math.Clamp( Brightness, 0.3f, 3f );
			var min = MathF.Max( MathF.Min( _toneBase.x * Math.Clamp( Brightness, 0.3f, 3f ), max ), ExposureFloor * MathF.Min( 1f, ExposureNow ) );     // the floor drops with a dark zone's cap
			// a narrow window, adapting slowly: with a wide one the engine's own auto-exposure swung the whole picture brighter and
			// darker every time a lit strip or a dark wall filled the view, which read as the lights flickering while running
			// (Angelo, 2026-10-12: "are two lighting engines fighting?")
			min = MathF.Max( min, max * ExposureSpan );
			if ( _tone.Rate > ExposureRate ) _tone.Rate = ExposureRate;
			_toneWritten = new Vector2( min, MathF.Max( min, max ) );
			_tone.MinimumExposure = _toneWritten.x;
			_tone.MaximumExposure = _toneWritten.y;
			_toneApplied = true;
		}

		UpdateLamps( scene, zone );
		UpdateFog( scene, pos );
	}

	/// <summary>
	/// Dim the scene's own lights inside the camera's zone (not the dynamic ones). Whatever else drives them (flicker, DEFCON)
	/// wrote this frame's colour; we scale it and put it back at the start of the next frame.
	/// </summary>
	static void UpdateLamps( Scene scene, LightZone zone )
	{
		// the set: the zone's lights, kept while you stay in it (rescanned now and then for lights that come and go)
		var target = zone ?? ( MathF.Abs( LampsNow - 1f ) > 0.01f ? _lampZone : null );
		if ( target != _lampZone || ( target is not null && _sinceLampScan > 3f ) )
		{
			foreach ( var (l, b, w) in _lamps ) if ( l.IsValid() && l.LightColor == w ) l.LightColor = b;
			_lamps.Clear();
			_lampZone = target;
			_sinceLampScan = 0;
			if ( target is not null )
				foreach ( var l in scene.GetAllComponents<Light>() )
				{
					if ( l is DirectionalLight || !target.Box.Contains( l.WorldPosition ) ) continue;
					if ( l.GameObject.Name.StartsWith( "dyn_light_" ) || l.Components.GetInAncestorsOrSelf<SwrpLightZoneTag>() is not null ) continue;
					_lamps.Add( (l, l.LightColor, l.LightColor) );
				}
		}
		if ( _lampZone is null ) return;
		for ( var i = 0; i < _lamps.Count; i++ )
		{
			var (l, b0, w0) = _lamps[i];
			if ( !l.IsValid() ) continue;
			var b = l.LightColor == w0 ? b0 : l.LightColor;     // our own value still there: keep the stored base
			var w = new Color( b.r * LampsNow, b.g * LampsNow, b.b * LampsNow, b.a );
			l.LightColor = w;
			_lamps[i] = (l, b, w);
		}
		if ( zone is null && MathF.Abs( LampsNow - 1f ) <= 0.01f ) { foreach ( var (l, b, w) in _lamps ) if ( l.IsValid() && l.LightColor == w ) l.LightColor = b; _lamps.Clear(); _lampZone = null; }
	}

	/// <summary> A fog volume per hazy zone near the camera; the camera's own zone at the blended strength. </summary>
	static void UpdateFog( Scene scene, Vector3 pos )
	{
		foreach ( var z in _zones )
		{
			var wants = FogOn && On && !Simple && z.Fog > 0f && z.Box.Grow( 1500f ).Contains( pos );
			_fog.TryGetValue( z, out var f );
			if ( !wants )
			{
				if ( f.IsValid() && f.Enabled ) f.Enabled = false;
				continue;
			}
			if ( !f.IsValid() )
			{
				if ( !_fogRoot.IsValid() )
				{
					_fogRoot = scene.CreateObject();
					_fogRoot.Name = "SWRP Light Zones";
					_fogRoot.NetworkMode = NetworkMode.Never;
					_fogRoot.Flags |= GameObjectFlags.NotSaved;
				}
				var go = new GameObject( _fogRoot, true, $"haze_{z.Name}" );
				go.NetworkMode = NetworkMode.Never;
				go.Flags |= GameObjectFlags.NotSaved;
				go.WorldPosition = z.Box.Center;
				f = go.Components.Create<VolumetricFogVolume>();
				f.Bounds = new BBox( z.Box.Mins - z.Box.Center, z.Box.Maxs - z.Box.Center );
				f.FalloffExponent = 1f;
				_fog[z] = f;
			}
			if ( !f.Enabled ) f.Enabled = true;
			f.Strength = z.Name == CurrentName ? FogNow : z.Fog * 0.5f;
			f.Color = z.FogColor ?? new Color( 0.55f, 0.6f, 0.7f );
		}
	}

	[ConCmd( "swrp_light_zone_here", Help = "swrp_light_zone_here [ambient 0..1] [haze] [size] [exposure] [lamps] - dev: a light zone box round the camera (local, until restart; swrp_light_zone_clear removes them)" )]
	public static void ZoneHere( float ambient = 0.12f, float fog = 0.6f, float size = 900f, float exposure = 0.55f, float lamps = 0.08f )
	{
		if ( Game.ActiveScene is not { } scene || !scene.Camera.IsValid() ) return;
		var p = scene.Camera.WorldPosition;
		var half = new Vector3( size * 0.5f, size * 0.5f, size * 0.3f );
		var z = new LightZone( $"dev{_zones.Count( x => x.Name.StartsWith( "dev" ) ) + 1}", new BBox( p - half, p + half ), ambient, exposure, fog, new Color( 0.5f, 0.56f, 0.68f ), lamps );
		Add( z );
		Log.Info( $"[SWRP] light zone {z.Name} round {p}: ambient x{ambient}, exposure x{exposure}, haze {fog}" );
	}

	[ConCmd( "swrp_light_zone_clear", Help = "Dev: remove the light zones made with swrp_light_zone_here" )]
	public static void ZoneClear()
	{
		foreach ( var z in _zones.Where( x => x.Name.StartsWith( "dev" ) ).ToList() ) Remove( z );
	}

	[ConCmd( "swrp_light_zones_reload", Help = "Dev: put the ship's light zones back to their coded values (after a code change, without restarting play)" )]
	public static void ZonesReload()
	{
		var fresh = Defaults();
		_zones.RemoveAll( z => fresh.Any( f => f.Name == z.Name ) );
		foreach ( var z in fresh ) Add( z );
		Log.Info( $"[SWRP] {fresh.Length} light zones reloaded" );
	}

	[ConCmd( "swrp_light_zones_list", Help = "List the light zones and which one the camera is in" )]
	public static void ZoneList()
	{
		foreach ( var z in _zones ) Log.Info( $"[SWRP] zone {z.Name}: {z.Box.Mins} - {z.Box.Maxs}, ambient x{z.Ambient}, exposure x{z.Exposure}, haze {z.Fog}{( z.Name == CurrentName ? "  <- camera" : "" )}" );
		if ( _zones.Count == 0 ) Log.Info( "[SWRP] no light zones" );
	}
}

/// <summary> Lights under an object with this are never dimmed by a light zone (a lamp that must stay lit). </summary>
[Title( "SWRP Zone-Proof Light" ), Category( "SWRP" ), Icon( "light" )]
public sealed class SwrpLightZoneTag : Component { }

/// <summary> Dev: what surface the camera sees at a point on screen (normal and material), for chasing lighting seams. </summary>
public static class SurfaceProbe
{
	[ConCmd( "swrp_ceiling_at", Help = "swrp_ceiling_at <x> <y> <z> - dev: log the floor and ceiling heights above and below a point" )]
	public static void CeilingAt( float x, float y, float z )
	{
		if ( Game.ActiveScene is not { } sc ) return;
		var p = new Vector3( x, y, z );
		var up = sc.Trace.Ray( p, p + Vector3.Up * 2000f ).WithoutTags( "player", "npc", "trigger" ).Run();
		var dn = sc.Trace.Ray( p, p + Vector3.Down * 2000f ).WithoutTags( "player", "npc", "trigger" ).Run();
		Log.Info( $"[SWRP] at {x:0} {y:0}: floor {( dn.Hit ? dn.HitPosition.z : float.NaN ):0} ceiling {( up.Hit ? up.HitPosition.z : float.NaN ):0} start solid {up.StartedSolid}" );
	}

	[ConCmd( "swrp_lights_near", Help = "swrp_lights_near <x> <y> <z> [range] - dev: log every light near a point with its colour" )]
	public static void LightsNear( float x, float y, float z, float range = 900f )
	{
		if ( Game.ActiveScene is not { } sc ) return;
		var p = new Vector3( x, y, z );
		foreach ( var l in sc.GetAllComponents<Light>().Where( l => l.WorldPosition.Distance( p ) < range ).OrderBy( l => l.WorldPosition.Distance( p ) ) )
			Log.Info( $"[SWRP] light {l.GetType().Name} '{l.GameObject.Name}' parent '{l.GameObject.Parent?.Name}' at {l.WorldPosition:0} colour {l.LightColor} on {l.Enabled}" );
	}

	[ConCmd( "swrp_surface_at", Help = "swrp_surface_at <screen x 0-1> <screen y 0-1> - dev: log the surface under that point of the screen" )]
	public static void At( float sx, float sy )
	{
		if ( Game.ActiveScene?.Camera is not { } cam ) return;
		var ray = cam.ScreenNormalToRay( new Vector3( sx, sy, 0f ) );
		var tr = Game.ActiveScene.Trace.Ray( ray, 5000f ).WithoutTags( "player", "npc", "trigger" ).Run();
		Log.Info( $"[SWRP] surface at {sx:0.00},{sy:0.00}: hit {tr.Hit} pos {tr.HitPosition:0} normal {tr.Normal:0.00} object {tr.GameObject?.Name} surface {tr.Surface?.ResourceName}" );
	}
}
