using System;

namespace Swrp;

/// <summary>
/// Characters take on the room they stand in (Angelo, 2026-10-12): the ship's lamps already light troopers directly, but the
/// ambient fill and reflections were one flat colour everywhere, so white armour read the same in a white bay, a red brig or
/// a blue corridor. Two parts, both following the camera (that's what you see yourself and the troopers around you in):
///   - the fill: the ambient colour leans towards the colour of the lamps near the camera (each weighted by brightness and
///     closeness), keeping its brightness, blended over about a second as you walk between rooms;
///   - reflections: one realtime cubemap probe moves with the camera, so glossy armour and visors mirror the room's own
///     lamps, strips and walls.
/// Runs from <see cref="LightingSystem"/>'s tick on the game's own scene; swrp_room_light 0 turns it off.
/// </summary>
public static class RoomLight
{
	[ConVar( "swrp_room_light", Help = "Characters pick up the colour of the room they're in (fill tint and live reflections; 0 = off)", Saved = true )]
	public static bool Enabled { get; set; } = true;

	/// <summary> On unless the simple ship-wide lighting is in use. </summary>
	public static bool On => Enabled && !LightZones.Simple;

	[ConVar( "swrp_room_tint", Help = "How far the ambient fill leans towards the nearby lamps' colour (0..1)", Saved = true )]
	public static float Strength { get; set; } = 0.85f;

	[ConVar( "swrp_room_light_debug", Help = "Dev: log the sampled room colour" )]
	public static bool Debug { get; set; }

	[ConVar( "swrp_room_reflections", Help = "A live reflection probe follows the camera (1 = on; off saves a little GPU)", Saved = true )]
	public static bool Reflections { get; set; } = true;

	const float Reach = 900f;

	/// <summary> Dev: sample the room here instead of at the camera (screenshots of a stand away from the player). </summary>
	public static Vector3? TestAt { get; set; }

	[ConCmd( "swrp_room_light_at", Help = "swrp_room_light_at <x> <y> <z> - dev: take the room colour and reflections from this point instead of the camera; no arguments goes back to the camera" )]
	public static void TestAtCommand( string x = "", string y = "", string z = "" )
	{
		TestAt = float.TryParse( x, out var fx ) && float.TryParse( y, out var fy ) && float.TryParse( z, out var fz ) ? new Vector3( fx, fy, fz ) : null;
		_sinceSample = 10f;
		Log.Info( $"[SWRP] room light sampled at {( TestAt?.ToString() ?? "the camera" )}" );
	}

	static Scene _scene;
	static readonly List<PointLight> _lamps = new();
	static RealTimeSince _sinceSample = 10f;
	static RealTimeSince _sinceRescan = 10f;
	static Color _want = Color.White, _now = Color.White;
	static GameObject _probeGo;

	[ConVar( "swrp_room_tint_body", Help = "How strongly each trooper's armour takes on the colour of the lamps around them (0 = off)", Saved = true )]
	public static float BodyStrength { get; set; } = 0.55f;

	static readonly Dictionary<Component, (Color Now, Color Want, RealTimeSince Since)> _bodies = new();

	/// <summary>
	/// Every machine: the tint for a character standing at <paramref name="at"/>, so each trooper is coloured by their own room
	/// (white bay white, red brig red, blue corridors blue), not just by the camera's. Sampled a few times a second per
	/// character and eased in.
	/// </summary>
	public static Color BodyTint( Component who, Vector3 at )
	{
		if ( !On || BodyStrength <= 0f || who is null ) return Color.White;
		if ( _bodies.Count > 256 )
			foreach ( var k in _bodies.Keys.Where( k => !k.IsValid() ).ToList() ) _bodies.Remove( k );
		_bodies.TryGetValue( who, out var e );
		if ( e.Now == default ) e = (Color.White, Color.White, 10f);
		if ( e.Since > 0.3f )
		{
			e.Since = 0;
			var hue = Sample( at + Vector3.Up * 50f, out _ );
			// push the colour further from white than the fill does (the armour is white: a light lean barely shows)
			var s = Math.Clamp( BodyStrength, 0f, 1f ) * 1.6f;
			e.Want = new Color( Math.Clamp( 1f + ( hue.r - 1f ) * s, 0.35f, 1f ), Math.Clamp( 1f + ( hue.g - 1f ) * s, 0.35f, 1f ), Math.Clamp( 1f + ( hue.b - 1f ) * s, 0.35f, 1f ) );
		}
		var t = Math.Min( 1f, Time.Delta * 2f );
		e.Now = new Color( MathX.Lerp( e.Now.r, e.Want.r, t ), MathX.Lerp( e.Now.g, e.Want.g, t ), MathX.Lerp( e.Now.b, e.Want.b, t ) );
		_bodies[who] = e;
		return e.Now;
	}

	/// <summary> The lamps' colour around a point, normalised to luminance 1 (white when there are none). </summary>
	static Color Sample( Vector3 eye, out float weight )
	{
		float r = 0, g = 0, b = 0, w = 0;
		foreach ( var l in _lamps )
		{
			if ( !l.IsValid() || !l.Enabled ) continue;
			var d = l.WorldPosition.Distance( eye );
			if ( d > Reach ) continue;
			var c = l.LightColor;
			var lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
			if ( lum <= 0.001f ) continue;
			var mx = MathF.Max( c.r, MathF.Max( c.g, c.b ) );
			var sat = ( mx - MathF.Min( c.r, MathF.Min( c.g, c.b ) ) ) / MathF.Max( 0.001f, mx );
			var k = lum * ( 1f + 3f * sat ) * ( 1f - d / Reach ) * ( 1f - d / Reach );
			r += c.r / lum * k; g += c.g / lum * k; b += c.b / lum * k; w += k;
		}
		weight = w;
		if ( w <= 0.001f ) return Color.White;
		var hue = new Color( r / w, g / w, b / w );
		var hl = hue.r * 0.3f + hue.g * 0.59f + hue.b * 0.11f;
		return new Color( hue.r / hl, hue.g / hl, hue.b / hl );
	}

	/// <summary> This frame's hue for the fill (white = no change); LightZones multiplies the ambient by it. </summary>
	public static Color Hue => On ? _now : Color.White;

	public static void Tick( Scene scene, CameraComponent cam, float dt )
	{
		if ( scene != _scene )
		{
			_scene = scene;
			_lamps.Clear();
			_probeGo = null;
			_sinceRescan = 10f;
		}
		if ( !cam.IsValid() ) return;
		var eye = TestAt ?? cam.WorldPosition;

		// every static light in the map and the rooms' own fittings (club lights, screens), not the lighting engine's flashes
		if ( _sinceRescan > 5f )
		{
			_sinceRescan = 0;
			_lamps.Clear();
			foreach ( var l in scene.GetAllComponents<PointLight>() )
				if ( !l.GameObject.Name.StartsWith( "dyn_light_" ) && l.GameObject.Name != "Blaster Model" && l.Components.GetInAncestorsOrSelf<SwrpPlayer>() is null ) _lamps.Add( l );
		}

		if ( On && _sinceSample > 0.2f )
		{
			_sinceSample = 0;
			// the lamps' own colours (their base colour, before a light zone dims them), weighted by brightness and closeness
			float r = 0, g = 0, b = 0, w = 0;
			foreach ( var l in _lamps )
			{
				if ( !l.IsValid() || !l.Enabled ) continue;
				var d = l.WorldPosition.Distance( eye );
				if ( d > Reach ) continue;
				var c = l.LightColor;
				var lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
				if ( lum <= 0.001f ) continue;
				// saturated lamps count for more: a red alarm strip colours a room far more than its share of the light
				var sat = ( MathF.Max( c.r, MathF.Max( c.g, c.b ) ) - MathF.Min( c.r, MathF.Min( c.g, c.b ) ) ) / MathF.Max( 0.001f, MathF.Max( c.r, MathF.Max( c.g, c.b ) ) );
				var k = lum * ( 1f + 3f * sat ) * ( 1f - d / Reach ) * ( 1f - d / Reach );
				r += c.r / lum * k; g += c.g / lum * k; b += c.b / lum * k; w += k;
			}
			if ( w > 0.001f )
			{
				var hue = new Color( r / w, g / w, b / w );
				// keep the fill's brightness: normalise the hue to luminance 1, then lean towards it
				var hl = hue.r * 0.3f + hue.g * 0.59f + hue.b * 0.11f;
				hue = new Color( hue.r / hl, hue.g / hl, hue.b / hl );
				var s = Math.Clamp( Strength, 0f, 1f );
				_want = new Color( MathX.Lerp( 1f, hue.r, s ), MathX.Lerp( 1f, hue.g, s ), MathX.Lerp( 1f, hue.b, s ) );
			}
			else _want = Color.White;
			if ( Debug ) Log.Info( $"[SWRP] room light at {eye:0}: {w:0.00} weight from {_lamps.Count} lamps, hue {_want}" );
		}
		if ( !On ) _want = Color.White;
		var t = Math.Min( 1f, dt * 2.5f );
		_now = new Color( MathX.Lerp( _now.r, _want.r, t ), MathX.Lerp( _now.g, _want.g, t ), MathX.Lerp( _now.b, _want.b, t ) );

		// the live reflection probe at the camera
		var wantProbe = On && Reflections;
		if ( wantProbe && !_probeGo.IsValid() )
		{
			_probeGo = scene.CreateObject();
			_probeGo.Name = "SWRP Room Reflections";
			_probeGo.NetworkMode = NetworkMode.Never;
			_probeGo.Flags |= GameObjectFlags.NotSaved;
			var p = _probeGo.Components.Create<EnvmapProbe>();
			p.Mode = EnvmapProbe.EnvmapProbeMode.Realtime;
			p.Bounds = new BBox( new Vector3( -1200f ), new Vector3( 1200f ) );
			p.Feathering = 0.2f;
			p.Resolution = EnvmapProbe.CubemapResolution.Small;
			p.UpdateStrategy = EnvmapProbe.CubemapDynamicUpdate.TimeInterval;
			p.DelayBetweenUpdates = 0.6f;
			p.ZNear = 4f;
			p.ZFar = 4000f;
		}
		if ( _probeGo.IsValid() )
		{
			_probeGo.Enabled = wantProbe;
			// moved in steps, not every frame: a probe that re-renders from a new spot each update made reflections flicker as you ran
			if ( wantProbe && _probeGo.WorldPosition.Distance( eye ) > 350f ) _probeGo.WorldPosition = eye;
		}
	}
}
