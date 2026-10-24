namespace Swrp;

/// <summary>
/// Every <see cref="SwrpPlayer"/> in the active scene, gathered once a frame. A scene-wide component search costs the
/// whole scene, and the HUD, comms, doors, NPCs and the holo-map each used to run their own every frame; with a full
/// server that adds up. Use <see cref="All"/> (check IsValid, an entry can be destroyed later in the frame) and
/// <see cref="Local"/> instead of <c>Scene.GetAllComponents&lt;SwrpPlayer&gt;()</c> on per-frame paths.
/// </summary>
public static class SwrpPlayers
{
	static float _at = -1f;
	static Scene _scene;
	static readonly List<SwrpPlayer> _all = new();
	static SwrpPlayer _local;

	static void Refresh()
	{
		var scene = Game.ActiveScene;
		if ( _at == Time.Now && ReferenceEquals( scene, _scene ) ) return;
		_at = Time.Now;
		_scene = scene;
		_all.Clear();
		_local = null;
		if ( scene is null ) return;
		_all.AddRange( scene.GetAllComponents<SwrpPlayer>() );
		foreach ( var p in _all )
		{
			if ( p.IsProxy || p.CharacterId is null ) continue;
			_local = p;
			break;
		}
	}

	/// <summary> Everyone, this frame. </summary>
	public static IReadOnlyList<SwrpPlayer> All
	{
		get
		{
			Refresh();
			return _all;
		}
	}

	/// <summary> Everyone still valid, this frame (the drop-in for <c>Scene.GetAllComponents&lt;SwrpPlayer&gt;()</c>). </summary>
	public static IEnumerable<SwrpPlayer> Valid => All.Where( p => p.IsValid() );

	/// <summary> Deployed troopers only, this frame. </summary>
	public static IEnumerable<SwrpPlayer> Deployed => All.Where( p => p.IsValid() && p.CharacterId is not null );

	/// <summary> This machine's deployed trooper (same rule as <see cref="SwrpPlayer.Local"/>), or null. </summary>
	public static SwrpPlayer Local
	{
		get
		{
			Refresh();
			return _local.IsValid() ? _local : null;
		}
	}

	public static SwrpPlayer BySteam( ulong steamId ) => Deployed.FirstOrDefault( p => p.OwnerSteamId == steamId );
}
