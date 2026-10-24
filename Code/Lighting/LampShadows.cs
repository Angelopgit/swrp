using System;

namespace Swrp;

/// <summary>
/// Real shadows from the ship's own lamps (the map's "Lights"), on a budget: the few lamps nearest the camera (and in front
/// of it) cast shadows, everything else stays shadowless, re-picked a few times a second as you move. The original Notorious
/// map got its deep shadows from baked lightmaps; this is the live stand-in (Angelo, 2026-10-12: "darker feel, shadows").
/// Runs from <see cref="LightingSystem"/>'s tick, on the game's own scene only.
/// </summary>
public static class LampShadows
{
	[ConVar( "swrp_lamp_shadows", Help = "How many of the nearest ship lamps cast real shadows (0 = none)", Saved = true )]
	public static int Budget { get; set; } = 6;

	[ConVar( "swrp_lamp_shadow_range", Help = "Lamps further than this from the camera never cast shadows (units)", Saved = true )]
	public static float Range { get; set; } = 1800f;

	static Scene _scene;
	static readonly List<PointLight> _lamps = new();
	static readonly HashSet<PointLight> _on = new();
	static RealTimeSince _sincePick = 10f;

	public static void Tick( Scene scene, CameraComponent cam )
	{
		if ( scene != _scene )
		{
			_scene = scene;
			_lamps.Clear();
			_on.Clear();
			foreach ( var l in scene.GetAllComponents<PointLight>() )
				if ( l.GameObject.Parent?.Name is "Lights" ) _lamps.Add( l );
		}
		if ( LightZones.Simple )
		{
			// simple lighting: no shadow juggling at all
			foreach ( var l in _on ) if ( l.IsValid() ) l.Shadows = false;
			_on.Clear();
			return;
		}
		if ( !cam.IsValid() || _sincePick < 0.6f ) return;
		_sincePick = 0;

		var eye = cam.WorldPosition;
		var fwd = cam.WorldRotation.Forward;
		var want = new HashSet<PointLight>();
		if ( Budget > 0 )
		{
			foreach ( var l in _lamps
				.Where( l => l.IsValid() && l.Enabled )
				.Select( l => (L: l, D: l.WorldPosition.Distance( eye )) )
				.Where( x => x.D < Range )
				// behind you counts for less, but a lamp right over you still throws your shadow
				.OrderBy( x => x.D * ( Vector3.Dot( fwd, ( x.L.WorldPosition - eye ).Normal ) < -0.2f && x.D > 300f ? 2.5f : 1f ) )
				.Take( Budget ) )
				want.Add( l.L );
		}
		// keep the lamps already casting shadows unless they're out of range: swapping a lamp's shadow on and off as you run made
		// the lighting pop (Angelo, 2026-10-12). Only one swap per pick, and only to a lamp clearly closer than the one it replaces.
		foreach ( var l in _on.ToList() )
			if ( !l.IsValid() || !l.Enabled || l.WorldPosition.Distance( eye ) > Range * 1.15f ) { if ( l.IsValid() ) l.Shadows = false; _on.Remove( l ); }
		foreach ( var l in want )
		{
			if ( _on.Contains( l ) ) continue;
			if ( _on.Count < Budget ) { _on.Add( l ); l.Shadows = true; continue; }
			var far = _on.OrderByDescending( x => x.WorldPosition.Distance( eye ) ).First();
			if ( l.WorldPosition.Distance( eye ) < far.WorldPosition.Distance( eye ) * 0.6f ) { far.Shadows = false; _on.Remove( far ); _on.Add( l ); l.Shadows = true; }
			break;
		}
		while ( _on.Count > Math.Max( 0, Budget ) ) { var x = _on.First(); x.Shadows = false; _on.Remove( x ); }
	}
}
