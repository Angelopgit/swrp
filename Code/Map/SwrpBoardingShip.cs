using System;

namespace Swrp;

/// <summary>
/// The enemy warship off the Venator's starboard side (the map's props_11_7_2) is hidden until a Game Master starts a
/// boarding attack from the GM menu (/gm board): then it drops out of hyperspace into its place, nose first along -y,
/// with a flash. /gm board off sends it away again. Shown or hidden is synced from the host (SwrpShip.BoardingShip); every
/// machine plays the arrival itself, and anyone joining later just sees it there.
/// </summary>
public static class SwrpBoardingShip
{
	public const string PropName = "props_11_7_2";
	// the map build (MapSource/notorious_build.py, FRIGATE_BOX) puts every brush and prop of the frigate into props_11_7_2,
	// side doors and all. The strips down its flanks and its middle used to be cells of their own: now inside it, their old models stay off.
	static readonly string[] Parts = { PropName };
	static readonly string[] Retired = { "props_10_9_3", "props_12_9_3", "props_11_7_3" };
	const float WarpTime = 1.1f;            // seconds from the jump point to its place
	const float WarpDistance = 90000f;      // how far behind its place it drops out (it points along -y)

	static Scene _scene;
	static GameObject _ship;
	static List<(GameObject Go, Vector3 Home)> _parts = new();
	static List<GameObject> _retired = new();
	static bool? _shown;
	static RealTimeSince _sinceChange;
	static GameObject _flash;
	static Vector3 _nose;

	/// <summary> Every frame (from SwrpShip). </summary>
	public static void Tick( Scene scene, bool want )
	{
		if ( scene is null ) return;
		if ( _scene != scene )
		{
			_scene = scene;
			_ship = scene.GetAllObjects( false ).FirstOrDefault( g => g.Name == PropName );
			_parts = scene.GetAllObjects( false ).Where( g => Parts.Contains( g.Name ) ).Select( g => (g, g.WorldPosition) ).ToList();
			_retired = scene.GetAllObjects( false ).Where( g => Retired.Contains( g.Name ) ).ToList();
			_shown = null;
			if ( _ship.IsValid() )
			{
				var b = _ship.GetBounds();
				_nose = new Vector3( b.Center.x, b.Mins.y, b.Center.z );
			}
		}
		// every frame: once off at load didn't hold on a client (2026-10-19, Angelo's screenshot showed them hanging in space)
		foreach ( var old in _retired )
			if ( old.IsValid() && old.Enabled ) old.Enabled = false;
		if ( !_ship.IsValid() ) return;

		if ( _shown != want )
		{
			var first = _shown is null;      // the map loading, or joining mid-event: no warp, just as it stands
			_shown = want;
			_sinceChange = first ? WarpTime : 0;
			if ( want && !first ) Arrive();
		}

		// arriving: rushes in from far behind and brakes hard into place; leaving: the same in reverse, then hidden
		var t = Math.Clamp( _sinceChange / WarpTime, 0f, 1f );
		var away = want ? MathF.Pow( 1f - t, 4f ) : MathF.Pow( t, 4f );
		var visible = want || t < 1f;
		foreach ( var (go, rest) in _parts )
		{
			if ( !go.IsValid() ) continue;
			if ( go.Enabled != visible ) go.Enabled = visible;
			go.WorldPosition = rest + new Vector3( 0f, WarpDistance * away, 0f );
			// solid only once it has stopped (a moving 28,000 unit hull would sweep through everything)
			if ( go.Components.Get<ModelCollider>( FindMode.EverythingInSelf ) is { } col && col.Enabled != ( want && t >= 1f ) )
				col.Enabled = want && t >= 1f;
		}

		if ( _flash.IsValid() && _flash.Components.Get<PointLight>() is { } light )
		{
			var f = 1f - Math.Clamp( _sinceChange / 1.6f, 0f, 1f );
			light.LightColor = new Color( 0.6f, 0.8f, 1f ) * ( 40f * f * f );
			if ( f <= 0f ) _flash.Destroy();
		}
	}

	static void Arrive()
	{
		_flash?.Destroy();
		_flash = _scene.CreateObject();
		_flash.Name = "Boarding Ship Arrival Flash";
		_flash.NetworkMode = NetworkMode.Never;
		_flash.Flags |= GameObjectFlags.NotSaved;
		_flash.WorldPosition = _nose;      // at its nose, where it drops out
		var light = _flash.Components.Create<PointLight>();
		light.Radius = 30000f;
		light.Shadows = false;
		Sound.Play( SwrpAudio.Teleport );
		Sound.Play( SwrpAudio.AlertChange );
	}
}
