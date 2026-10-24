namespace Swrp;

/// <summary>
/// Ship lamps the map's own light pass left out: plain white lamps like the rest (the map's "Lights"), hung near the
/// ceiling where Angelo found dark corners (his loc tags). Added under the map's Lights group when the ship starts, so the
/// alert colours and lamp shadows treat them as ship lamps. Add a spot by adding its position.
/// </summary>
public static class SwrpExtraLamps
{
	static readonly Vector3[] Spots =
	{
		// loc #8 and #9, 2026-10-19: the side hangar bays by the bay doors (floor -1278, ceiling about -620); along each bay
		new( 2000f, 1350f, -760f ), new( 2000f, 1900f, -760f ), new( 2000f, 2450f, -760f ),
		new( -2000f, 1350f, -760f ), new( -2000f, 1900f, -760f ), new( -2000f, 2450f, -760f ),
	};

	/// <summary> The map's standard white lamp (MapSource/notorious_scene.py), a little wider to reach the floor of the tall bays. </summary>
	static readonly Color White = new( 3f, 2.91f, 2.76f );
	const float Radius = 900f;

	/// <summary> Dev (code_run_static_method): add them to the running scene now. </summary>
	public static void PlaceNow() => Place( Game.ActiveScene );

	public static void Place( Scene scene )
	{
		var parent = scene.GetAllObjects( false ).FirstOrDefault( g => g.Name == "Lights" && g.Parent == scene );
		if ( parent is null ) return;
		foreach ( var at in Spots )
		{
			var go = new GameObject( parent, true, "Light (added)" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = at;
			var l = go.Components.Create<PointLight>();
			l.LightColor = White;
			l.Radius = Radius;
			l.Shadows = false;
		}
	}
}
