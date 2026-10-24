using System;

namespace Swrp.Jedi;

/// <summary>
/// One lightsaber as it's drawn (local only): the hilt, and per blade a white-hot core, a coloured glow, a soft haze,
/// a light, and a ribbon trail. Owned by a <see cref="SaberRig"/>.
/// </summary>
sealed class SaberVisual
{
	readonly GameObject _root;      // the hilt (scaled with the body)
	readonly GameObject _fx;        // the blades' lines, sprites and light (world space, unscaled)
	readonly ModelRenderer _hilt;
	readonly List<Blade> _blades = new();
	PointLight _light;
	LightEmitter _emitter;      // the lighting framework's light for this saber (Code/Lighting); _light is the fallback
	string _builtKey;
	BladePose _pose;
	bool _placed;
	Vector3 _lastBurn;
	RealTimeSince _sinceBurnFx;

	public Vector3 Center { get; private set; }
	public float TipSpeed { get; private set; }

	/// <summary> 0..1: the blade flares white-hot for a moment where it bites (a hit, a block, a clash), then settles. </summary>
	public float Flare { get; set; }

	sealed class Blade
	{
		public LineRenderer Core, Glow;
		public SpriteRenderer[] Haze;
		public LineRenderer[] Trails;
		public SaberTrailSheet Sheet;
		public readonly List<(float T, Vector3 Base, Vector3 Tip)> History = new();
		public Vector3 Base, Tip, LastTip;
		public bool HasLast;
		public bool Reverse;
	}

	// the trail: a bright strand along the tip's path; the smear across the cut is one swept sheet (SaberTrailSheet) from
	// SheetFrom of the way up the blade to the tip
	static readonly float[] TrailAt = { 1f };
	const float SheetFrom = 0.28f;

	public SaberVisual( Scene scene, string name )
	{
		_root = new GameObject( true, $"jedi_{name}" );
		_root.NetworkMode = NetworkMode.Never;
		_root.Flags |= GameObjectFlags.NotSaved;
		_hilt = _root.Components.Create<ModelRenderer>();
		_hilt.RenderType = ModelRenderer.ShadowRenderType.On;
		_fx = new GameObject( true, $"jedi_{name}_blade" );
		_fx.NetworkMode = NetworkMode.Never;
		_fx.Flags |= GameObjectFlags.NotSaved;
	}

	public void Build( SaberDesign d, bool shoto )
	{
		var key = d.HiltKey + ( shoto ? "s" : "" );
		if ( _builtKey == key ) return;
		_builtKey = key;
		_hilt.Model = SaberHilt.Get( d, shoto );
		_hilt.Tint = SaberHilt.Tint( d );
		var count = d.IsStaff && !shoto ? 2 : 1;
		foreach ( var b in _blades ) DestroyBlade( b );
		_blades.Clear();
		for ( var i = 0; i < count; i++ ) _blades.Add( MakeBlade( d.BladeColor, i == 1 ) );
		if ( !_light.IsValid() )
		{
			var lg = new GameObject( _fx, true, "light" );
			lg.NetworkMode = NetworkMode.Never;
			_light = lg.Components.Create<PointLight>();
			_light.Shadows = false;
		}
	}

	Blade MakeBlade( Color color, bool reverse )
	{
		var b = new Blade { Reverse = reverse };
		var hot = Color.Lerp( color, Color.White, 0.82f );
		b.Core = Line( "core", new Gradient( new Gradient.ColorFrame( 0f, hot * 2.6f ), new Gradient.ColorFrame( 1f, hot * 2.2f ) ) );
		b.Glow = Line( "glow", new Gradient( new Gradient.ColorFrame( 0f, ( color * 1.9f ).WithAlpha( 0.85f ) ), new Gradient.ColorFrame( 1f, ( color * 1.6f ).WithAlpha( 0.7f ) ) ) );
		var sprite = SwrpFx.GlowSprite;
		b.Haze = new SpriteRenderer[sprite is null ? 0 : 4];
		for ( var i = 0; i < b.Haze.Length; i++ )
		{
			var go = new GameObject( _fx, true, "haze" );
			go.NetworkMode = NetworkMode.Never;
			var r = go.Components.Create<SpriteRenderer>();
			r.Sprite = sprite;
			r.Additive = true;
			r.Lighting = false;
			r.Shadows = false;
			r.Color = ( color * 1.4f ).WithAlpha( 0.22f );
			b.Haze[i] = r;
		}
		b.Trails = new LineRenderer[TrailAt.Length];
		for ( var i = 0; i < TrailAt.Length; i++ )
		{
			var t = Line( "trail", new Gradient( new Gradient.ColorFrame( 0f, color.WithAlpha( 0f ) ), new Gradient.ColorFrame( 1f, ( color * 1.5f ).WithAlpha( 0.5f ) ) ) );
			b.Trails[i] = t;
		}
		b.Sheet = new SaberTrailSheet( _fx.Scene );
		return b;
	}

	LineRenderer Line( string name, Gradient color )
	{
		var go = new GameObject( _fx, true, name );
		go.NetworkMode = NetworkMode.Never;
		var l = go.Components.Create<LineRenderer>();
		l.UseVectorPoints = true;
		l.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero };
		l.Color = color;
		l.Additive = true;
		l.Lighting = false;
		l.CastShadows = false;
		l.Enabled = false;
		return l;
	}

	static void DestroyBlade( Blade b )
	{
		b.Core?.GameObject.Destroy();
		b.Glow?.GameObject.Destroy();
		foreach ( var h in b.Haze ) h?.GameObject.Destroy();
		foreach ( var t in b.Trails ) t?.GameObject.Destroy();
		b.Sheet?.Dispose();
	}

	/// <summary> The hilt at a pose (world): the grip point at <see cref="BladePose.Hand"/>. </summary>
	public void Place( BladePose world, SaberDesign d, float scale, bool shoto )
	{
		_pose = world;
		_placed = true;
		var len = shoto ? SaberDesign.ShotoHilt : d.HiltLength;
		var grip = d.IsStaff && !shoto ? 0.5f : 0.58f;
		if ( !_root.Enabled ) _root.Enabled = true;
		_root.WorldScale = scale;
		_root.WorldRotation = Rotation.LookAt( world.Dir, world.Knuckle );
		_root.WorldPosition = world.Hand - world.Dir * len * grip * scale;
	}

	/// <summary> The blades: grown out to <paramref name="ignite"/> of their length, with glow, light and trails. </summary>
	public void Blades( SaberDesign d, float ignite, float scale, bool shoto, bool firstPerson )
	{
		if ( !_placed ) return;
		var hiltLen = ( shoto ? SaberDesign.ShotoHilt : d.HiltLength ) * scale;
		var bladeLen = ( shoto ? SaberDesign.ShotoBlade : d.BladeLength ) * scale;
		var color = d.BladeColor;
		var lit = ignite > 0.001f;
		var now = Time.Now;
		var dt = MathF.Max( Time.Delta, 0.001f );
		var maxSpeed = 0f;
		Flare = MathF.Max( 0f, Flare - dt / 0.14f );
		var flare = Flare * Flare;
		var flicker = ( 1f + MathF.Sin( now * 61f ) * 0.04f + MathF.Sin( now * 23f ) * 0.03f ) * ( 1f + 0.55f * flare );

		foreach ( var b in _blades )
		{
			var dir = b.Reverse ? -_pose.Dir : _pose.Dir;
			var emitterOffset = d.IsStaff && !shoto ? 0.5f : 0.42f;
			b.Base = _pose.Hand + dir * hiltLen * emitterOffset;
			// grows out of the emitter fast, slowing as it reaches full length
			var ext = 1f - MathF.Pow( 1f - Math.Clamp( ignite, 0f, 1f ), 2.2f );
			b.Tip = b.Base + dir * bladeLen * ext;

			b.Core.Enabled = lit;
			b.Glow.Enabled = lit;
			foreach ( var h in b.Haze ) h.Enabled = lit;
			if ( !lit )
			{
				foreach ( var t in b.Trails ) t.Enabled = false;
				b.Sheet?.Hide();
				b.History.Clear();
				b.HasLast = false;
				continue;
			}

			// the core tapers to a rounded tip; the glow is wider and pulses faintly
			var w = scale * flicker;
			b.Core.VectorPoints = new List<Vector3> { b.Base, b.Tip };
			b.Core.Width = new Curve( new Curve.Frame( 0f, 0.62f * w ), new Curve.Frame( 0.92f, 0.56f * w ), new Curve.Frame( 1f, 0.2f * w ) );
			b.Glow.VectorPoints = new List<Vector3> { b.Base - dir * 0.4f * scale, b.Tip + dir * 0.8f * scale };
			b.Glow.Width = new Curve( new Curve.Frame( 0f, 2.3f * w ), new Curve.Frame( 0.9f, 2.1f * w ), new Curve.Frame( 1f, 0.9f * w ) );
			for ( var i = 0; i < b.Haze.Length; i++ )
			{
				var f = ( i + 0.5f ) / b.Haze.Length;
				b.Haze[i].WorldPosition = Vector3.Lerp( b.Base, b.Tip, f );
				var size = ( firstPerson ? 5f : 7.5f ) * scale * ( 0.8f + 0.2f * ext );
				b.Haze[i].Size = new Vector2( size, size );
			}
			// as it ignites (and retracts) the tip burns brightest, a hot point leading the blade out of the emitter
			if ( ext < 0.999f && b.Haze.Length > 0 )
			{
				var tip = b.Haze[^1];
				tip.WorldPosition = b.Tip;
				var ts = 11f * scale * ( 1f - ext * 0.5f );
				tip.Size = new Vector2( ts, ts );
			}

			// how fast the tip is moving, for the hum and the trail
			var speed = b.HasLast ? b.Tip.Distance( b.LastTip ) / dt : 0f;
			b.LastTip = b.Tip;
			b.HasLast = true;
			maxSpeed = MathF.Max( maxSpeed, speed );

			// the trail: recent blade positions, fading with age and with how fast the blade moves (a jump no swing could
			// make - a teleport, a respawn, a pose snapped on the stage - starts it afresh instead of smearing across)
			if ( speed > 9000f * scale ) b.History.Clear();
			b.History.Add( ( now, b.Base, b.Tip ) );
			// about a tenth of a second of blade, but never fewer than three samples (at a low frame rate a fixed age left one)
			var keep = MathF.Max( 0.085f, dt * 2.5f );
			while ( b.History.Count > 3 && now - b.History[0].T > keep ) b.History.RemoveAt( 0 );
			while ( b.History.Count > 12 ) b.History.RemoveAt( 0 );
			var strength = Math.Clamp( ( speed / scale - 260f ) / 900f, 0f, 1f ) * ext;
			for ( var i = 0; i < b.Trails.Length; i++ )
			{
				var trail = b.Trails[i];
				var on = strength > 0.03f && b.History.Count >= 3;
				trail.Enabled = on;
				if ( !on ) continue;
				var f = TrailAt[i];
				var pts = new List<Vector3>( b.History.Count );
				Smooth( b.History, f, pts );
				trail.VectorPoints = pts;
				// the tip strand is the bright edge; the inner ones a fainter fill
				var edge = f >= 0.99f;
				var a = ( edge ? 0.5f : 0.12f ) * strength;
				trail.Color = new Gradient( new Gradient.ColorFrame( 0f, color.WithAlpha( 0f ) ), new Gradient.ColorFrame( 0.6f, ( color * 1.4f ).WithAlpha( a * 0.5f ) ), new Gradient.ColorFrame( 1f, ( Color.Lerp( color, Color.White, edge ? 0.45f : 0.2f ) * 1.6f ).WithAlpha( a ) ) );
				var tw = ( edge ? 2.2f : bladeLen / scale * 0.12f * 1.15f ) * scale;
				// thin at both ends: the newest end sits under the blade, where square ends stacked into a saw-tooth
				trail.Width = new Curve( new Curve.Frame( 0f, tw * 0.3f ), new Curve.Frame( 0.8f, tw ), new Curve.Frame( 1f, tw * 0.45f ) );
			}
			b.Sheet ??= new SaberTrailSheet( _fx.Scene );
			if ( strength > 0.03f && b.History.Count >= 3 )
			{
				var inner = new List<Vector3>( b.History.Count * 5 );
				var outer = new List<Vector3>( b.History.Count * 5 );
				Smooth( b.History, SheetFrom, inner );
				Smooth( b.History, 1f, outer );
				b.Sheet.Set( inner, outer, Color.Lerp( color, Color.White, 0.25f ), 0.42f * strength );
			}
			else b.Sheet.Hide();
		}

		TipSpeed = maxSpeed;
		if ( _blades.Count > 0 ) Center = Vector3.Lerp( _blades[0].Base, _blades[0].Tip, 0.4f );

		// the light moves with the blade and lights the room in its colour
		if ( _light.IsValid() && LightingSystem.On )
		{
			if ( _light.Enabled ) _light.Enabled = false;
			if ( _emitter is null || !_emitter.Alive ) _emitter = Lighting.Attach( _light.GameObject, LightProfile.Saber( color ) );
			_light.WorldPosition = _blades.Count > 1 ? _pose.Hand : Center;
			if ( _emitter is not null )
			{
				_emitter.Active = lit;
				_emitter.ColorOverride = Color.Lerp( color, Color.White, 0.35f * flare );
				_emitter.IntensityScale = ( 1f + 0.16f * ( flicker - 1f ) + 1.55f * flare ) * ignite;
				_emitter.RadiusScale = ( _blades.Count > 1 ? 1.2f : 1f ) * scale;
			}
		}
		else if ( _light.IsValid() )
		{
			if ( _emitter is not null ) _emitter.Active = false;
			var near = SwrpFx.LightsOn && SwrpFx.Near( _fx.Scene, Center, SwrpFx.LightRange );
			_light.Enabled = lit && near;
			if ( _light.Enabled )
			{
				_light.WorldPosition = _blades.Count > 1 ? _pose.Hand : Center;
				_light.LightColor = Color.Lerp( color, Color.White, 0.35f * flare ) * ( 1.6f + 0.25f * flicker + 2.5f * flare ) * ignite;
				_light.Radius = ( _blades.Count > 1 ? 210f : 170f ) * scale;
			}
		}
	}

	/// <summary> Where a lit blade passes into a wall or floor: a glowing cut that cools to a scorch, sparks and a hiss. </summary>
	public void BurnWorld( Scene scene, SaberDesign d, float scale, GameObject ignore )
	{
		if ( !_placed || !SwrpFx.Near( scene, _pose.Hand, 2500f ) ) return;
		foreach ( var b in _blades )
		{
			var tr = scene.Trace.Ray( b.Base, b.Tip ).IgnoreGameObjectHierarchy( ignore ).WithoutTags( "trigger", "player", "npc", "ragdoll" ).Run();
			if ( !tr.Hit || tr.GameObject is null ) continue;
			if ( tr.GameObject.Components.GetInAncestorsOrSelf<SwrpPlayer>() is not null ) continue;
			var at = tr.HitPosition;
			if ( at.DistanceSquared( _lastBurn ) < 2.2f * 2.2f * scale * scale ) continue;
			var along = ( at - _lastBurn );
			if ( along.Length > 14f * scale ) along = Vector3.Cross( tr.Normal, ( b.Tip - b.Base ).Normal );
			_lastBurn = at;
			SaberFx.Burn( scene, at, tr.Normal, along.Normal, d.BladeColor, scale );
			if ( _sinceBurnFx > 0.12f )
			{
				_sinceBurnFx = 0;
				SaberFx.BurnSparks( scene, at, tr.Normal, d.BladeColor );
			}
		}
	}

	/// <summary> The trail's points at fraction f along the blade, rounded through the samples (Catmull-Rom, five between each pair, short enough that the ribbon's joints don't notch) so it sweeps as a curve, not a fan of straight cuts. </summary>
	static void Smooth( List<(float T, Vector3 Base, Vector3 Tip)> h, float f, List<Vector3> pts )
	{
		Vector3 P( int i ) => Vector3.Lerp( h[Math.Clamp( i, 0, h.Count - 1 )].Base, h[Math.Clamp( i, 0, h.Count - 1 )].Tip, f );
		for ( var i = 0; i < h.Count - 1; i++ )
		{
			Vector3 p0 = P( i - 1 ), p1 = P( i ), p2 = P( i + 1 ), p3 = P( i + 2 );
			for ( var s = 0; s < 5; s++ )
			{
				var t = s / 5f;
				var t2 = t * t;
				var t3 = t2 * t;
				pts.Add( 0.5f * ( 2f * p1 + ( p2 - p0 ) * t + ( 2f * p0 - 5f * p1 + 4f * p2 - p3 ) * t2 + ( 3f * p1 - p0 - 3f * p2 + p3 ) * t3 ) );
			}
		}
		pts.Add( P( h.Count - 1 ) );
	}

	public void Hide()
	{
		_placed = false;
		if ( _root.Enabled ) _root.Enabled = false;
		foreach ( var b in _blades )
		{
			b.Core.Enabled = false;
			b.Glow.Enabled = false;
			foreach ( var h in b.Haze ) h.Enabled = false;
			foreach ( var t in b.Trails ) t.Enabled = false;
			b.Sheet?.Hide();
			b.History.Clear();
			b.HasLast = false;
		}
		if ( _light.IsValid() ) _light.Enabled = false;
		if ( _emitter is not null ) _emitter.Active = false;
	}

	public void Destroy()
	{
		_root?.Destroy();
		_fx?.Destroy();
	}
}

/// <summary> The blade's hum on one saber: a seamless loop (two copies crossfading), its pitch and volume following the blade's speed. </summary>
sealed class SaberHum
{
	SoundHandle _a, _b, _grind;
	RealTimeSince _started;
	bool _thrownLoop;
	Vector3 _lastPos;
	bool _hasLast;

	public void Update( Vector3 pos, float speed, float ignite, float pitch, bool thrown )
	{
		// the spinning throw has its own (shorter) loop: switch at once when the saber leaves or comes back to the hand
		var loop = thrown ? SaberSounds.SpinLoopSeconds : SaberSounds.HumLoopSeconds;
		if ( _a is null || !_a.IsPlaying || _started > loop - 0.3f || thrown != _thrownLoop )
		{
			if ( _b is not null && _b.IsPlaying ) _b.Stop();
			_b = thrown != _thrownLoop ? null : _a;
			if ( thrown != _thrownLoop && _a is not null && _a.IsPlaying ) _a.Stop();
			_a = Sound.Play( SaberSounds.Real( thrown ? SaberSounds.ThrowSpin : SaberSounds.Hum ), pos );
			_thrownLoop = thrown;
			_started = 0;
		}
		var swing = Math.Clamp( speed / 1400f, 0f, 1f );
		var vol = ( 0.55f + 0.65f * swing ) * Math.Clamp( ignite, 0f, 1f );
		var p = pitch * ( 0.88f + 0.12f * ignite ) * ( 1f + 0.3f * swing );

		// Doppler: a blade coming at you rises in pitch, one going away drops
		var dt = MathF.Max( Time.Delta, 0.001f );
		if ( _hasLast && Game.ActiveScene?.Camera is { } cam )
		{
			var toCam = ( cam.WorldPosition - pos );
			if ( toCam.LengthSquared > 1f )
			{
				var closing = Vector3.Dot( ( pos - _lastPos ) / dt, toCam.Normal );
				p *= Math.Clamp( 1f + closing / 2400f, 0.85f, 1.2f );
			}
		}
		_lastPos = pos;
		_hasLast = true;

		var fadeIn = Math.Clamp( _started / 0.3f, 0f, 1f );
		if ( _a is not null ) { _a.Position = pos; _a.Volume = vol * ( _b is null ? 1f : fadeIn ); _a.Pitch = p; }
		if ( _b is not null && _b.IsPlaying ) { _b.Position = pos; _b.Volume = vol * ( 1f - fadeIn ); _b.Pitch = p; }
	}

	public void Grind( Vector3 pos, bool on )
	{
		if ( !on )
		{
			if ( _grind is not null && _grind.IsPlaying ) _grind.Stop();
			_grind = null;
			return;
		}
		if ( _grind is null || !_grind.IsPlaying ) _grind = Sound.Play( SaberSounds.LockGrind, pos );
		if ( _grind is not null ) _grind.Position = pos;
	}

	public void Stop()
	{
		if ( _a is not null && _a.IsPlaying ) _a.Stop();
		if ( _b is not null && _b.IsPlaying ) _b.Stop();
		_a = _b = null;
	}
}
