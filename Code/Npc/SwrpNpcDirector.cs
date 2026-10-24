using System;

namespace Swrp;

/// <summary>
/// Host side bookkeeping for NPCs: spawning (capped by swrp_npc_max), the navmesh tiles they need, who counts as an enemy of
/// whom (the allegiance table), target finding, waves and the Game Master clean-up.
///
/// The navmesh is generated on demand: the scene has it enabled with deferred generation, and the first NPC spawned in an area
/// builds the tiles around it, so the rest of the ship costs nothing until a fight happens there.
/// </summary>
public static class SwrpNpcDirector
{
	[ConVar( "swrp_npc_max", Help = "Most NPCs alive at once (host, default 30)", Saved = true )]
	public static int MaxNpcs { get; set; } = 30;

	/// <summary> Half-size of the navmesh area built around a spawn point. </summary>
	public const float NavRadius = 2600f;

	public static IEnumerable<SwrpNpc> All( Scene scene ) => scene.GetAllComponents<SwrpNpc>();
	public static int AliveCount( Scene scene ) => All( scene ).Count( n => !n.IsDead );

	static int _callSign = 100;

	/// <summary> Host: spawn up to <paramref name="count"/> NPCs in a loose group around a point. Returns how many. </summary>
	public static List<SwrpNpc> Spawn( Scene scene, NpcDefinition def, Vector3 at, float yaw, int count )
	{
		var made = new List<SwrpNpc>();
		if ( !Networking.IsHost || def is null || def.EventOnly ) return made;
		EnsureNavMesh( scene, at );

		var room = Math.Max( 0, MaxNpcs - AliveCount( scene ) );
		count = Math.Min( count, room );
		var spacing = def.Style == NpcStyle.Roller ? 90f : 60f;
		var perRow = Math.Max( 1, (int)MathF.Ceiling( MathF.Sqrt( count ) ) );
		var rot = Rotation.FromYaw( yaw );
		var squad = 0;

		for ( var i = 0; i < count; i++ )
		{
			var row = i / perRow;
			var col = i % perRow;
			var offset = rot * new Vector3( -row * spacing, ( col - ( perRow - 1 ) * 0.5f ) * spacing, 0 );
			if ( def.Style == NpcStyle.Rifleman && count > 5 )
			{
				// teams of five, three abreast and two behind, 70 apart; the teams 320 apart in a grid
				var teams = (int)MathF.Ceiling( count / 5f );
				var across = Math.Max( 1, (int)MathF.Ceiling( MathF.Sqrt( teams ) ) );
				int team = i / 5, m = i % 5;
				offset = rot * ( new Vector3( -( team / across ) * 320f, ( team % across - ( across - 1 ) * 0.5f ) * 320f, 0 )
					+ new Vector3( -( m / 3 ) * 70f, ( m % 3 - 1 ) * 70f + ( m >= 3 ? 35f : 0f ), 0 ) );
			}
			var p = Ground( scene, at + offset ) ?? at;

			var go = new GameObject( true, def.Name );
			go.WorldPosition = p;
			go.WorldRotation = rot;
			var npc = go.AddComponent<SwrpNpc>();
			npc.NpcId = def.Id;
			if ( def.Style == NpcStyle.Rifleman && count > 1 && i % 5 == 0 ) squad = SwrpNpc.NewSquadId();      // fire teams of up to five
			npc.SquadId = squad;
			npc.CallSign = def.IsClone ? $"CT-{Game.Random.Next( 1000, 9999 )}" : $"{def.Short}-{_callSign++ % 9000 + 100:0000}";
			go.NetworkSpawn();
			npc.HostInit();
			made.Add( npc );
		}
		return made;
	}

	/// <summary> The floor under a point (a short trace down), or null. </summary>
	static Vector3? Ground( Scene scene, Vector3 p )
	{
		var tr = scene.Trace.Ray( p + Vector3.Up * 60f, p + Vector3.Down * 300f ).WithoutTags( "npc", "trigger", "ragdoll", "player" ).Run();
		return tr.Hit ? tr.EndPosition : null;
	}

	// ----- navmesh -----

	// areas already built, as an array replaced on every change (a hotload can leave a static List in a bad state)
	static Scene _navScene;
	static BBox[] _navAreas = Array.Empty<BBox>();

	public static void EnsureNavMesh( Scene scene, Vector3 around )
	{
		if ( scene?.NavMesh is not { } nav ) return;
		if ( _navScene != scene )
		{
			_navScene = scene;
			_navAreas = Array.Empty<BBox>();
		}
		if ( !nav.IsEnabled ) nav.IsEnabled = true;
		// the scene's navmesh bounds come from the map, so Tatooine (built at runtime, far below) had none and its NPCs never moved:
		// grow them to take in a fight anywhere, and build every area again (2026-10-21)
		if ( !nav.Bounds.Contains( around ) )
		{
			nav.CustomBounds = true;
			nav.Bounds = nav.Bounds.AddBBox( new BBox( around - new Vector3( NavRadius, NavRadius, 900f ), around + new Vector3( NavRadius, NavRadius, 900f ) ) );
			_navAreas = Array.Empty<BBox>();
		}
		foreach ( var b in _navAreas )
			if ( b.Contains( around ) ) return;

		var box = new BBox( around - new Vector3( NavRadius, NavRadius, 900f ), around + new Vector3( NavRadius, NavRadius, 900f ) );
		var t = RealTime.Now;
		nav.GenerateTiles( scene.PhysicsWorld, box );
		_navAreas = _navAreas.Append( new BBox( box.Mins + new Vector3( 600, 600, 300 ), box.Maxs - new Vector3( 600, 600, 300 ) ) ).ToArray();
		Log.Info( $"[SWRP] navmesh built around {around.x:0},{around.y:0},{around.z:0} in {( RealTime.Now - t ) * 1000:0} ms" );
	}

	// ----- sides and targets -----

	/// <summary> The allegiance of anything that can fight: an NPC, a trooper (or a GM's event character), a training dummy (GAR). </summary>
	public static string AllegianceOf( GameObject go )
	{
		if ( go.Components.GetInAncestorsOrSelf<SwrpNpc>() is { } npc ) return npc.Allegiance;
		if ( go.Components.GetInAncestorsOrSelf<SwrpPlayer>() is { } p ) return p.Allegiance;
		if ( go.Components.GetInAncestorsOrSelf<SwrpVitals>() is not null ) return AllegianceRegistry.Gar;
		return null;
	}

	public static bool IsHostile( SwrpNpc npc, GameObject other ) => AllegianceRegistry.AreHostile( npc.Allegiance, AllegianceOf( other ) );

	/// <summary> Something an NPC may shoot at right now (alive, standing, not a staff member flying about unseen). </summary>
	public static bool IsValidTarget( SwrpNpc npc, GameObject go )
	{
		if ( !go.IsValid() || !IsHostile( npc, go ) ) return false;
		if ( go.Components.Get<SwrpNpc>() is { } other ) return !other.IsDead;
		if ( go.Components.Get<SwrpPlayer>() is { } p && ( p.CharacterId is null || p.Noclip || p.Invisible ) ) return false;
		return go.Components.Get<SwrpVitals>() is { IsDead: false, IsDowned: false };
	}

	/// <summary> Centre of mass, where NPCs aim. </summary>
	public static Vector3 AimPoint( GameObject go )
	{
		if ( go.Components.Get<SwrpNpc>() is { } npc ) return npc.WorldPosition + Vector3.Up * ( npc.Def.Style == NpcStyle.Roller ? 32f : ( npc.Crouched ? 28f : 44f ) * npc.WorldScale.z );
		if ( go.Components.Get<SwrpPlayer>() is { Controller: { } c } ) return c.EyePosition + Vector3.Down * 16f;
		return go.WorldPosition + Vector3.Up * 46f;
	}

	static IEnumerable<GameObject> Candidates( Scene scene )
	{
		foreach ( var n in scene.GetAllComponents<SwrpNpc>() ) if ( !n.IsDead ) yield return n.GameObject;
		foreach ( var v in scene.GetAllComponents<SwrpVitals>() ) if ( !v.IsDead ) yield return v.GameObject;
	}

	/// <summary> Host: the nearest enemy this NPC can see, checking at most <paramref name="maxChecks"/> lines of sight. </summary>
	public static GameObject FindTarget( SwrpNpc npc, float range, int maxChecks )
	{
		var eye = npc.Eye;
		var r2 = range * range;
		var list = Candidates( npc.Scene ).Where( g => g.WorldPosition.DistanceSquared( npc.WorldPosition ) < r2 && IsValidTarget( npc, g ) )
			.OrderBy( g => g.WorldPosition.DistanceSquared( npc.WorldPosition ) ).Take( maxChecks );
		foreach ( var g in list )
		{
			var aim = AimPoint( g );
			var tr = npc.Scene.Trace.Ray( eye, aim ).IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger", "ragdoll", SwrpNpc.Tag ).Run();
			if ( !tr.Hit || tr.GameObject is { } hit && hit.Root == g.Root || tr.EndPosition.Distance( aim ) < 20f )
				return g;
		}
		return null;
	}

	/// <summary> Host: the nearest enemy anywhere within range, seen or not (for hunting waves). </summary>
	public static GameObject NearestHostile( SwrpNpc npc, float range )
	{
		var r2 = range * range;
		return Candidates( npc.Scene ).Where( g => g.WorldPosition.DistanceSquared( npc.WorldPosition ) < r2 && IsValidTarget( npc, g ) )
			.OrderBy( g => g.WorldPosition.DistanceSquared( npc.WorldPosition ) ).FirstOrDefault();
	}

	// ----- doors, cached -----

	static SwrpDoor[] _doorCache = Array.Empty<SwrpDoor>();
	static RealTimeSince _doorsAge = 100;

	public static SwrpDoor[] Doors( Scene scene )
	{
		if ( _doorsAge > 10f )
		{
			_doorsAge = 0;
			_doorCache = scene.GetAllComponents<SwrpDoor>().ToArray();
		}
		return _doorCache;
	}

	// ----- clean-up -----

	/// <summary> Host: every NPC, wave and droid wreck gone. Returns how many NPCs were removed. </summary>
	public static int CleanUp( Scene scene )
	{
		var n = 0;
		foreach ( var npc in All( scene ).ToList() )
		{
			npc.Despawn();
			n++;
		}
		foreach ( var w in scene.GetAllComponents<SwrpNpcWave>().ToList() ) w.GameObject.Destroy();
		ClearWrecks();
		return n;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ClearWrecks()
	{
		foreach ( var go in Game.ActiveScene?.FindAllWithTag( "npcwreck" ).ToList() ?? new() )
			go.Destroy();
	}
}

/// <summary>
/// A Game Master's wave spawner (host only, not networked): every <see cref="Interval"/> seconds it sends <see cref="PerWave"/>
/// NPCs from its spot, cycling through <see cref="Types"/>, and they hunt the nearest enemies. Removed when its waves are done
/// or by /gm wave stop or /gm cleanup.
/// </summary>
public sealed class SwrpNpcWave : Component
{
	public List<string> Types { get; set; } = new();
	public int PerWave { get; set; } = 4;
	public int WavesLeft { get; set; } = 3;
	public float Interval { get; set; } = 45f;
	public string StartedBy { get; set; }

	TimeSince _sinceWave = 1000;
	int _typeIndex;

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( WavesLeft <= 0 )
		{
			GameObject.Destroy();
			return;
		}
		if ( _sinceWave < Interval ) return;
		_sinceWave = 0;
		WavesLeft--;

		var sent = 0;
		for ( var i = 0; i < PerWave; i++ )
		{
			var def = NpcRegistry.Get( Types[_typeIndex++ % Types.Count] );
			var made = SwrpNpcDirector.Spawn( Scene, def, WorldPosition, WorldRotation.Yaw(), 1 );
			foreach ( var n in made ) n.Hunt = true;
			sent += made.Count;
		}
		var left = WavesLeft > 0 ? $", {WavesLeft} more every {Interval:0} s" : ", last wave";
		SwrpChat.StaffLog( $"[GM] Wave from {StartedBy}: {sent} NPCs sent{left}{( sent < PerWave ? $" (NPC limit {SwrpNpcDirector.MaxNpcs} reached)" : "" )}" );
		SwrpStaffTools.RecordText( null, "gm wave", $"{StartedBy}'s wave: {sent} NPCs sent{left}" );
	}
}
