using System;
using System.Diagnostics;

namespace Swrp;

/// <summary>
/// The dynamic lighting framework (Teardown-style): every flash, bolt, saber and breach asks for light through
/// <see cref="Lighting"/>, and this system decides each frame which of them get one of a small pool of real PointLights.
/// Requests are scored by brightness, reach towards the camera, whether they're in front of you and their priority; the best
/// <see cref="MaxLights"/> are drawn, the best few that want one get a shadow (<see cref="MaxShadows"/>), and the brightest get
/// a dim second light off their surface, tinted by its colour (fake bounce: blue saber light picks up the grey deck's warmth,
/// a red bolt splashes pink off white armour). Everything else is simply not drawn, so a firefight costs the same as one shot.
/// It also applies the camera's light zone (<see cref="LightZones"/>: dark rooms, haze) on top of whatever else sets the
/// ambient fill and exposure. Local and cosmetic: each machine lights its own view.
/// </summary>
public sealed class LightingSystem : GameObjectSystem<LightingSystem>
{
	[ConVar( "swrp_light", Help = "Dynamic lighting framework: flashes, bolts, sabers and breaches light the room (1 = on, 0 = off)", Saved = true )]
	public static bool On { get; set; } = true;

	[ConVar( "swrp_light_max", Help = "Most dynamic lights drawn at once (bounce lights included); the best-scoring requests get them", Saved = true )]
	public static int MaxLights { get; set; } = 20;

	[ConVar( "swrp_light_shadows", Help = "How many of the dynamic lights may cast shadows (sabers and blasts ask for one; 0 = none)", Saved = true )]
	public static int MaxShadows { get; set; } = 2;

	[ConVar( "swrp_light_bounce", Help = "Fake bounce light: the brightest lights throw a dim tinted second light off the surface they hit (1 = on)", Saved = true )]
	public static bool BounceOn { get; set; } = true;

	[ConVar( "swrp_light_bounce_max", Help = "Most bounce lights at once (they come out of swrp_light_max)", Saved = true )]
	public static int MaxBounce { get; set; } = 5;

	[ConVar( "swrp_light_embers", Help = "Impacts leave a small glowing ember light that lingers as it cools (1 = on)", Saved = true )]
	public static bool Embers { get; set; } = true;

	[ConVar( "swrp_light_range", Help = "Dynamic lights further than this from the camera are never drawn (world units)", Saved = true )]
	public static float Range { get; set; } = 3200f;

	[ConVar( "swrp_light_intensity", Help = "Brightness scale for every dynamic light", Saved = true )]
	public static float IntensityScale { get; set; } = 1f;

	[ConVar( "swrp_light_debug", Help = "Overlay: active (green), shadowed (yellow), bounce (cyan) and culled (red) lights, and the cost" )]
	public static bool Debug { get; set; }

	/// <summary> Hard ceiling so a typo in swrp_light_max can't make 500 lights. </summary>
	const int PoolCeiling = 48;

	/// <summary> Requests kept at once; past this the oldest timed ones are dropped. </summary>
	const int MaxRequests = 256;

	readonly List<LightEmitter> _emitters = new();
	readonly List<LightEmitter> _ranked = new();
	readonly List<PointLight> _pool = new();
	readonly bool[] _used = new bool[PoolCeiling];
	GameObject _root;
	readonly Stopwatch _watch = new();
	float _costMs, _costAvg;
	int _drawn, _shadowed, _bounced, _culled;

	public LightingSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 10, Restore, "SwrpLightingRestore" );
		Listen( Stage.FinishUpdate, 10, Tick, "SwrpLighting" );
	}

	public int Count => _emitters.Count;

	internal void Add( LightEmitter e )
	{
		if ( _emitters.Count >= MaxRequests )
		{
			var oldest = _emitters.FindIndex( x => x.Duration > 0f );
			if ( oldest < 0 ) return;
			Release( _emitters[oldest] );
			_emitters.RemoveAt( oldest );
		}
		_emitters.Add( e );
	}

	/// <summary> Start of the frame: hand the ambient fill and exposure back as the other systems left them. </summary>
	void Restore() => LightZones.Restore( Scene );

	void Tick()
	{
		if ( !LightZones.Live( Scene ) ) { LightZones.Apply( Scene, null, 0f ); HideUnused( null ); return; }     // never light the editor's copy of the scene
		_watch.Restart();
		var dt = Time.Delta;
		var cam = Scene.Camera;
		LightZones.Apply( Scene, cam, dt );
		RoomLight.Tick( Scene, cam, dt );
		LampShadows.Tick( Scene, cam );

		if ( !On || !cam.IsValid() )
		{
			foreach ( var e in _emitters ) Release( e );
			if ( !On ) _emitters.Clear();
			HideUnused( null );
			_drawn = _shadowed = _bounced = 0;
			Finish();
			return;
		}

		var camPos = cam.WorldPosition;
		var fwd = cam.WorldRotation.Forward;
		var now = Time.Now;
		var range = MathF.Max( 200f, Range );

		// age, follow, cull the dead, score the living
		_ranked.Clear();
		_culled = 0;
		for ( var i = _emitters.Count - 1; i >= 0; i-- )
		{
			var e = _emitters[i];
			e.Age += dt;
			if ( !e.Alive ) { Release( e ); _emitters.RemoveAt( i ); continue; }
			if ( e.Follow.IsValid() ) e.Position = e.Follow.WorldTransform.PointToWorld( e.Offset );
			if ( !e.Active ) { e.Score = 0f; Release( e ); continue; }

			var p = e.Profile;
			var k = e.Duration > 0f ? Eval( e.Curve, e.Age / e.Duration ) : 1f;
			if ( p.Flicker > 0f ) k *= 1f - p.Flicker * Noise( now * p.FlickerSpeed + e.Seed );
			var colour = ( e.ColorOverride ?? p.Color ) * ( p.Intensity * e.IntensityScale * k * IntensityScale );
			e.Final = colour;
			e.Brightness = MathF.Max( colour.r * 0.3f + colour.g * 0.59f + colour.b * 0.11f, MathF.Max( colour.r, MathF.Max( colour.g, colour.b ) ) * 0.5f );     // (luminance alone undersells deep blue)

			var radius = p.Radius * e.RadiusScale;
			var to = e.Position - camPos;
			var d = to.Length;
			if ( e.Brightness < 0.02f || d > range + radius ) { e.Score = 0f; Release( e ); _culled++; continue; }

			var reach = radius * radius / ( d * d + radius * radius );
			var view = 1f;
			if ( d > radius * 1.1f && Vector3.Dot( fwd, to / d ) < -0.1f ) view = 0.15f;     // behind you and not round you
			e.Score = p.Priority * e.Brightness * reach * view * ( e.Slot >= 0 ? 1.3f : 1f );    // a light already shown keeps its place
			_ranked.Add( e );
		}
		_ranked.Sort( ( a, b ) => b.Score.CompareTo( a.Score ) );

		// the budget: main lights first, then shadows for the best that want one, then bounce for the brightest
		var max = Math.Clamp( MaxLights, 0, PoolCeiling );
		var bounceMax = BounceOn ? Math.Clamp( MaxBounce, 0, max / 2 ) : 0;
		var mains = Math.Min( _ranked.Count, max );
		var bounceWanted = 0;
		for ( var i = 0; i < mains; i++ ) if ( _ranked[i].Profile.Bounce > 0f ) bounceWanted++;
		var bounces = Math.Min( bounceMax, bounceWanted );
		mains = Math.Min( mains, max - bounces );

		var shadowsLeft = Math.Max( 0, MaxShadows );
		var bouncesLeft = bounces;
		var used = _used;
		Array.Clear( used );

		// pass 1: who is in, and keep the slot they had
		for ( var i = 0; i < _ranked.Count; i++ )
		{
			var e = _ranked[i];
			if ( i >= mains ) { Release( e ); _culled++; continue; }
			e.Shadowed = e.Profile.Shadows && shadowsLeft > 0;
			if ( e.Shadowed ) shadowsLeft--;
			var wantBounce = bouncesLeft > 0 && e.Profile.Bounce > 0f;
			if ( wantBounce ) bouncesLeft--;
			if ( e.Slot >= 0 && e.Slot < PoolCeiling ) used[e.Slot] = true; else e.Slot = -1;
			if ( !wantBounce && e.BounceSlot >= 0 ) e.BounceSlot = -1;
			if ( e.BounceSlot >= 0 && e.BounceSlot < PoolCeiling ) used[e.BounceSlot] = true; else e.BounceSlot = wantBounce ? -2 : -1;
		}

		// pass 2: new slots, and draw
		_drawn = _shadowed = _bounced = 0;
		for ( var i = 0; i < mains; i++ )
		{
			var e = _ranked[i];
			if ( e.Slot < 0 ) { e.Slot = FreeSlot( used ); e.Vis = 0f; }
			if ( e.Slot < 0 ) continue;
			if ( e.BounceSlot == -2 ) e.BounceSlot = FreeSlot( used );
			e.Vis = MathF.Min( 1f, e.Vis + dt * 25f );

			var radius = e.Profile.Radius * e.RadiusScale;
			var l = Light( e.Slot );
			l.WorldPosition = e.Position;
			l.LightColor = e.Final * e.Vis;
			l.Radius = radius;
			l.Attenuation = e.Profile.Attenuation;
			l.FogStrength = e.Profile.Fog;
			if ( l.Shadows != e.Shadowed ) l.Shadows = e.Shadowed;
			if ( !l.Enabled ) l.Enabled = true;
			_drawn++;
			if ( e.Shadowed ) _shadowed++;

			if ( e.BounceSlot >= 0 )
			{
				UpdateBounce( e, dt );
				var b = Light( e.BounceSlot );
				if ( e.HaveBounce )
				{
					b.WorldPosition = e.BouncePos;
					b.LightColor = Mul( e.Final, e.SurfaceTint ) * ( e.Profile.Bounce * 0.32f * e.Vis );
					b.Radius = radius * 1.5f;
					b.Attenuation = 1.6f;
					b.FogStrength = 0f;
					if ( b.Shadows ) b.Shadows = false;
					if ( !b.Enabled ) b.Enabled = true;
					_drawn++;
					_bounced++;
				}
				else if ( b.Enabled ) b.Enabled = false;
			}
		}
		HideUnused( used );

		if ( Debug ) DrawDebug( cam );
		Finish();
	}

	void Finish()
	{
		_watch.Stop();
		_costMs = (float)_watch.Elapsed.TotalMilliseconds;
		_costAvg = MathX.Lerp( _costAvg, _costMs, 0.05f );
		if ( Debug && !On ) Scene.GetSystem<DebugOverlaySystem>().ScreenText( new Vector2( 20, 160 ), "swrp_light 0 (dynamic lighting off)", 14, TextFlag.LeftTop, Color.Orange );
	}

	static int FreeSlot( bool[] used )
	{
		for ( var i = 0; i < used.Length; i++ )
			if ( !used[i] ) { used[i] = true; return i; }
		return -1;
	}

	void Release( LightEmitter e )
	{
		e.Slot = -1;
		e.BounceSlot = -1;
		e.Vis = 0f;
	}

	void HideUnused( bool[] used )
	{
		for ( var i = 0; i < _pool.Count; i++ )
			if ( ( used is null || !used[i] ) && _pool[i].IsValid() && _pool[i].Enabled ) _pool[i].Enabled = false;
	}

	PointLight Light( int slot )
	{
		if ( !_root.IsValid() )
		{
			_root = Scene.CreateObject();
			_root.Name = "SWRP Lighting";
			_root.NetworkMode = NetworkMode.Never;
			_root.Flags |= GameObjectFlags.NotSaved;
			_pool.Clear();
		}
		while ( _pool.Count <= slot )
		{
			var go = new GameObject( _root, true, $"dyn_light_{_pool.Count}" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			var l = go.Components.Create<PointLight>();
			l.Shadows = false;
			l.LightColor = Color.Black;
			l.Enabled = false;
			_pool.Add( l );
		}
		if ( !_pool[slot].IsValid() )
		{
			var go = new GameObject( _root, true, $"dyn_light_{slot}" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			_pool[slot] = go.Components.Create<PointLight>();
		}
		return _pool[slot];
	}

	/// <summary>
	/// Where the bounce light sits and what colour the surface gives it. Impacts know their surface; anything else (a saber,
	/// a bolt) looks for the floor below a few times a second.
	/// </summary>
	void UpdateBounce( LightEmitter e, float dt )
	{
		var radius = e.Profile.Radius * e.RadiusScale;
		if ( e.Normal != Vector3.Zero )
		{
			if ( !e.HaveBounce )
			{
				e.SurfaceTint = SurfaceTint( Scene, e.Position - e.Normal * 8f, e.Normal );
				e.HaveBounce = true;
			}
			e.BouncePos = e.Position + e.Normal * radius * 0.35f;
			return;
		}
		e.SinceBounceCheck += dt;
		if ( e.SinceBounceCheck < 0.15f && e.HaveBounce ) { e.BouncePos = e.BouncePos.WithX( e.Position.x ).WithY( e.Position.y ); return; }
		e.SinceBounceCheck = 0f;
		var tr = Scene.Trace.Ray( e.Position, e.Position + Vector3.Down * radius * 1.2f ).WithoutTags( "player", "npc", "ragdoll", "trigger", "debris" ).Run();
		if ( !tr.Hit ) { e.HaveBounce = false; return; }
		e.SurfaceTint = SurfaceTint( Scene, tr.HitPosition - tr.Normal * 2f, tr.Normal, tr.GameObject );
		e.BouncePos = tr.HitPosition + tr.Normal * radius * 0.3f;
		e.HaveBounce = true;
	}

	/// <summary>
	/// The colour a surface bounces. The ship is PBR steel plating, so a cool grey; a tinted model (a painted craft, a droid)
	/// bounces its tint. Pushed towards saturation and normalised, so dark plating still bounces its hue rather than nothing.
	/// </summary>
	public static Color SurfaceTint( Scene scene, Vector3 inside, Vector3 normal, GameObject hit = null )
	{
		var albedo = new Color( 0.56f, 0.58f, 0.62f );
		if ( hit.IsValid() && hit.Components.Get<ModelRenderer>( FindMode.EverythingInSelf ) is { } mr && mr.Tint != Color.White )
			albedo = Mul( albedo, mr.Tint );

		// keep the hue, lift the level, add a touch of saturation
		var m = MathF.Max( 0.2f, MathF.Max( albedo.r, MathF.Max( albedo.g, albedo.b ) ) );
		var hue = new Color( albedo.r / m, albedo.g / m, albedo.b / m );
		var grey = ( hue.r + hue.g + hue.b ) / 3f;
		hue = new Color( grey + ( hue.r - grey ) * 1.35f, grey + ( hue.g - grey ) * 1.35f, grey + ( hue.b - grey ) * 1.35f );
		var level = 0.45f + 0.55f * MathF.Sqrt( m );
		return new Color( MathF.Max( 0f, hue.r ) * level, MathF.Max( 0f, hue.g ) * level, MathF.Max( 0f, hue.b ) * level );
	}

	static Color Mul( Color a, Color b ) => new( a.r * b.r, a.g * b.g, a.b * b.b, 1f );

	/// <summary> Brightness over a timed light's life, t = 0..1. </summary>
	public static float Eval( LightCurve curve, float t )
	{
		t = Math.Clamp( t, 0f, 1f );
		switch ( curve )
		{
			case LightCurve.Flash: return t < 0.05f ? 1f : MathF.Exp( -4.5f * ( t - 0.05f ) ) * ( 1f - t );
			case LightCurve.Linear: return 1f - t;
			case LightCurve.Smooth: return 1f - t * t * ( 3f - 2f * t );
			case LightCurve.Ember: return ( t < 0.04f ? t / 0.04f : 1f ) * MathF.Pow( 1f - t, 1.6f );
			case LightCurve.Hold: return t < 0.75f ? 1f : 1f - ( t - 0.75f ) / 0.25f;
			case LightCurve.Swell: return 0.5f - 0.5f * MathF.Cos( t * MathF.PI * 2f );
			default: return 1f - t;
		}
	}

	/// <summary> Smooth 0..1 noise for flicker (two detuned waves and a hash step, so it doesn't read as a sine). </summary>
	static float Noise( float x )
	{
		var i = MathF.Floor( x );
		var f = x - i;
		var a = Hash( i );
		var b = Hash( i + 1f );
		var s = f * f * ( 3f - 2f * f );
		return Math.Clamp( MathX.Lerp( a, b, s ) * 0.75f + 0.25f * ( 0.5f + 0.5f * MathF.Sin( x * 2.7f ) ), 0f, 1f );
	}

	static float Hash( float n )
	{
		var s = MathF.Sin( n * 127.1f ) * 43758.547f;
		return s - MathF.Floor( s );
	}

	void DrawDebug( CameraComponent cam )
	{
		var o = Scene.GetSystem<DebugOverlaySystem>();
		foreach ( var e in _emitters )
		{
			var radius = e.Profile.Radius * e.RadiusScale;
			if ( e.Slot >= 0 )
			{
				o.Sphere( new Sphere( e.Position, 4f ), e.Shadowed ? Color.Yellow : Color.Green, 0f, default, true );
				o.Sphere( new Sphere( e.Position, radius ), ( e.Shadowed ? Color.Yellow : Color.Green ).WithAlpha( 0.15f ), 0f, default, false );
				if ( e.BounceSlot >= 0 && e.HaveBounce ) o.Line( e.Position, e.BouncePos, Color.Cyan, 0f, default, true );
			}
			else if ( e.Active && e.Position.DistanceSquared( cam.WorldPosition ) < Range * Range )
				o.Sphere( new Sphere( e.Position, 3f ), Color.Red.WithAlpha( 0.6f ), 0f, default, true );
		}
		var zone = LightZones.CurrentName ?? "none";
		o.ScreenText( new Vector2( 20, 160 ),
			$"SWRP lighting: {_drawn}/{Math.Clamp( MaxLights, 0, PoolCeiling )} lights ({_shadowed} shadowed, {_bounced} bounce), {_emitters.Count} requests, {_culled} culled\n" +
			$"cost {_costAvg:0.00} ms/frame  pool {_pool.Count}  zone {zone} (ambient x{LightZones.AmbientNow:0.00}, exposure x{LightZones.ExposureNow:0.00}, haze {LightZones.FogNow:0.00})",
			14, TextFlag.LeftTop, Color.White );
	}

	[ConCmd( "swrp_light_info", Help = "Print the dynamic lighting state: requests, lights drawn, shadows, bounce, cost, zone" )]
	public static void Info()
	{
		var s = Current;
		if ( s is null ) { Log.Info( "[SWRP] lighting: no system in this scene" ); return; }
		Log.Info( $"[SWRP] lighting {(On ? "on" : "off")}: {s._drawn} lights drawn ({s._shadowed} shadowed, {s._bounced} bounce) of max {MaxLights}, {s._emitters.Count} requests, {s._culled} culled, pool {s._pool.Count}, cost {s._costAvg:0.00} ms; zone {LightZones.CurrentName ?? "none"} ambient x{LightZones.AmbientNow:0.00} exposure x{LightZones.ExposureNow:0.00}" );
	}

	/// <summary> Dev: a test light where you look (swrp_light_test [r g b] [intensity] [radius] [seconds]). </summary>
	[ConCmd( "swrp_light_test", Help = "swrp_light_test [r g b] [intensity] [radius] [seconds] - a test flash where the camera looks (local)" )]
	public static void Test( float r = 1f, float g = 0.3f, float b = 0.2f, float intensity = 6f, float radius = 300f, float seconds = 2f )
	{
		if ( Game.ActiveScene is not { } scene || !scene.Camera.IsValid() ) return;
		var cam = scene.Camera;
		var tr = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 3000f ).WithoutTags( "trigger" ).Run();
		var at = tr.Hit ? tr.HitPosition + tr.Normal * 10f : cam.WorldPosition + cam.WorldRotation.Forward * 200f;
		Lighting.Flash( at, new LightProfile { Color = new Color( r, g, b ), Intensity = intensity, Radius = radius, Bounce = 0.6f, Shadows = true, Priority = 2f }, seconds, LightCurve.Hold, tr.Hit ? tr.Normal : Vector3.Zero );
	}

	/// <summary> Dev: sustained fake fire round where you look, for screenshots and to load the budget (swrp_light_barrage [seconds] [shots a second]). </summary>
	[ConCmd( "swrp_light_barrage", Help = "swrp_light_barrage [seconds] [shots per second] [bolt hex, e.g. #2e6bff; empty = red and blue] - dev: sustained fake blaster fire where the camera looks (local, no damage)" )]
	public static async void Barrage( float seconds = 4f, float rate = 12f, string hex = "" )
	{
		var end = Time.Now + Math.Clamp( seconds, 0.1f, 60f );
		var gap = 1f / Math.Clamp( rate, 1f, 100f );
		var i = 0;
		while ( Time.Now < end && Game.ActiveScene is { } scene && scene.Camera.IsValid() )
		{
			var cam = scene.Camera;
			var dir = ( cam.WorldRotation.Forward + Vector3.Random * 0.3f ).Normal;
			var tr = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + dir * 3000f ).WithoutTags( "trigger", "player", "npc" ).Run();
			var c = Color.Parse( hex ) ?? ( i % 3 == 0 ? new Color( 0.2f, 0.45f, 1f ) : new Color( 1f, 0.15f, 0.1f ) );
			i++;
			var muzzle = cam.WorldPosition + cam.WorldRotation.Forward * 60f + cam.WorldRotation.Right * 25f + cam.WorldRotation.Down * 15f;
			Lighting.MuzzleFlash( muzzle, c );
			if ( tr.Hit )
			{
				BlasterBolt.Spawn( scene, muzzle, tr.HitPosition, c );
				Lighting.Impact( tr.HitPosition, tr.Normal, c );
			}
			await GameTask.DelaySeconds( gap );
		}
	}

	/// <summary> Dev: a burst of blaster fire where you look, to see the budget work (swrp_light_burst [shots]). </summary>
	[ConCmd( "swrp_light_burst", Help = "swrp_light_burst [shots] - dev: fake blaster impacts and muzzle flashes round where the camera looks (local, no damage)" )]
	public static void Burst( int shots = 12 )
	{
		if ( Game.ActiveScene is not { } scene || !scene.Camera.IsValid() ) return;
		var cam = scene.Camera;
		for ( var i = 0; i < Math.Clamp( shots, 1, 200 ); i++ )
		{
			var dir = ( cam.WorldRotation.Forward + Vector3.Random * 0.35f ).Normal;
			var tr = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + dir * 3000f ).WithoutTags( "trigger", "player" ).Run();
			if ( !tr.Hit ) continue;
			var c = i % 2 == 0 ? new Color( 1f, 0.15f, 0.1f ) : new Color( 0.2f, 0.45f, 1f );
			Lighting.MuzzleFlash( cam.WorldPosition + cam.WorldRotation.Forward * 40f + Vector3.Random * 20f, c );
			Lighting.Impact( tr.HitPosition, tr.Normal, c );
		}
	}
}
