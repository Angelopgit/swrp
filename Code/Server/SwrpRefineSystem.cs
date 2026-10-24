using System;

namespace Swrp;

/// <summary>
/// Runs the quality-of-life layer on every machine: puts up the wayfinding HUD (deck chip, directions marker, event
/// objective and timer), steers the directions marker, keeps event music going, and on the host writes pending career
/// statistics. Made at runtime like <see cref="Feel.FeelSystem"/>, so the generated scene doesn't need an entry.
/// </summary>
public sealed class SwrpRefineSystem : GameObjectSystem<SwrpRefineSystem>
{
	GameObject _hud, _squad, _armor;
	bool _reportedError;

	public SwrpRefineSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 30, Tick, "SwrpRefine" );
	}

	void Tick()
	{
		if ( Scene is null || !Game.IsPlaying || Scene != Game.ActiveScene ) return;
		try
		{
			if ( !_hud.IsValid() )
			{
				_hud = Scene.CreateObject();
				_hud.Name = "Wayfinding HUD";
				_hud.NetworkMode = NetworkMode.Never;
				_hud.Flags |= GameObjectFlags.NotSaved;
				var panel = _hud.Components.Create<ScreenPanel>();
				panel.ZIndex = 120;
				_hud.Components.Create<WayfindingHud>();
			}
			if ( !_squad.IsValid() )
			{
				// the squad menu: its own layer above the HUD so it takes the mouse
				_squad = Scene.CreateObject();
				_squad.Name = "Squad Menu";
				_squad.NetworkMode = NetworkMode.Never;
				_squad.Flags |= GameObjectFlags.NotSaved;
				var panel = _squad.Components.Create<ScreenPanel>();
				panel.ZIndex = 700;
				_squad.Components.Create<SquadMenu>();
			}
			if ( !_armor.IsValid() )
			{
				// the armour customiser: over the character screen, beside the hangar's trooper
				_armor = Scene.CreateObject();
				_armor.Name = "Armour Customiser";
				_armor.NetworkMode = NetworkMode.Never;
				_armor.Flags |= GameObjectFlags.NotSaved;
				var panel = _armor.Components.Create<ScreenPanel>();
				panel.ZIndex = 850;
				_armor.Components.Create<ArmorCustomizer>();
			}
			SwrpGuide.Tick();
			SwrpLocTag.Tick();
			SwrpLagComp.Record( Scene );      // host: where everyone stood, for lag-compensated hits
			SwrpEvents.TickMusic();
			if ( Networking.IsHost ) SwrpCareer.Flush();
		}
		catch ( Exception e )
		{
			if ( _reportedError ) return;
			_reportedError = true;
			Log.Warning( $"[SWRP] refine: {e}" );
		}
	}

	// ----- load test -----

	static readonly List<float> _frames = new();
	static RealTimeSince _sinceTest;
	static float _testLength;

	/// <summary>
	/// Host: a rough load test without 64 people. Spawns friendly clone NPCs spread over the spawn points (they walk, animate,
	/// shoot droids if any are about and ride the navmesh like players' bodies would) and logs the frame times for a while.
	/// It doesn't stand in for real clients' voice and network traffic; a second client is still the real test.
	/// </summary>
	[ConCmd( "swrp_loadtest", Help = "swrp_loadtest <clones 1-60> [seconds] - host: spawn friendly clone NPCs over the spawn points and log frame times (swrp_loadtest 0 cleans up)" )]
	public static void LoadTest( int count = 30, float seconds = 30f )
	{
		var scene = Game.ActiveScene;
		if ( !Networking.IsHost || scene is null ) return;
		if ( count <= 0 )
		{
			Log.Info( $"[SWRP] loadtest: removed {SwrpNpcDirector.CleanUp( scene )} NPCs" );
			return;
		}
		var def = NpcRegistry.Get( "clone" );
		var points = scene.GetAllComponents<SwrpSpawnPoint>().Select( p => p.WorldTransform ).ToList();
		if ( def is null || points.Count == 0 ) { Log.Warning( "[SWRP] loadtest: needs the clone NPC type and spawn points" ); return; }
		var made = 0;
		for ( var i = 0; i < Math.Clamp( count, 1, 60 ); i++ )
		{
			var t = points[i % points.Count];
			made += SwrpNpcDirector.Spawn( scene, def, t.Position + new Vector3( Game.Random.Float( -80f, 80f ), Game.Random.Float( -80f, 80f ), 0f ), t.Rotation.Yaw(), 1 ).Count;
		}
		_frames.Clear();
		_sinceTest = 0;
		_testLength = Math.Clamp( seconds, 5f, 300f );
		Log.Info( $"[SWRP] loadtest: {made} clones spawned (cap swrp_npc_max {SwrpNpcDirector.MaxNpcs}), timing {_testLength:0} s..." );
		LoadTestSampler.Start( scene );
	}

	/// <summary> Collects frame times while a load test runs, then prints the summary. </summary>
	sealed class LoadTestSampler : Component
	{
		public static void Start( Scene scene )
		{
			var go = scene.CreateObject();
			go.Name = "Load test sampler";
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.Components.Create<LoadTestSampler>();
		}

		protected override void OnUpdate()
		{
			_frames.Add( Time.Delta * 1000f );
			if ( _sinceTest < _testLength ) return;
			if ( _frames.Count > 0 )
			{
				var sorted = _frames.OrderBy( f => f ).ToList();
				var avg = _frames.Average();
				var p99 = sorted[Math.Clamp( (int)( sorted.Count * 0.99f ), 0, sorted.Count - 1 )];
				Log.Info( $"[SWRP] loadtest: {_frames.Count} frames, average {avg:0.0} ms ({1000f / Math.Max( avg, 0.01f ):0} fps), 99th percentile {p99:0.0} ms, worst {sorted[^1]:0.0} ms; NPCs alive {SwrpNpcDirector.All( Scene ).Count( n => !n.IsDead )}. swrp_loadtest 0 cleans up." );
			}
			GameObject.Destroy();
		}
	}
}
