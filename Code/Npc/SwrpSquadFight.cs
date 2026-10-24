using System;
using System.Text.Json;

namespace Swrp;

/// <summary>
/// Dev: a scripted squad fight, clones against B1s, <c>n</c> a side, <c>gap</c> units apart along the camera's view, and its
/// numbers in data/anim_rec/squad_fight_&lt;n&gt;.json: friendly-fire hits, shots held for a friend in the way, the share of
/// fighting time spent at a cover spot, and how long the winners took to form up once the last enemy fell.
/// </summary>
public sealed class SwrpSquadFight : Component
{
	List<SwrpNpc> _a, _b;
	int _n;
	TimeSince _age;
	TimeSince? _won;
	float? _formed;

	/// <summary> Dev (code_run_static_method), host: start a fight at <paramref name="x"/>,<paramref name="y"/>,<paramref name="z"/>. </summary>
	public static void Run( int n, float x, float y, float z, float gap = 1400f, float yaw = 0f )
	{
		var scene = Game.ActiveScene;
		if ( !Networking.IsHost || scene is null ) return;
		var at = new Vector3( x, y, z );
		var fwd = Rotation.FromYaw( yaw ).Forward;
		SwrpNpc.ResetSquadStats();
		var go = scene.CreateObject();
		go.Name = "Squad Fight";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var f = go.AddComponent<SwrpSquadFight>();
		f._n = n;
		f._a = SwrpNpcDirector.Spawn( scene, NpcRegistry.Get( NpcRegistry.Clone ), at - fwd * gap * 0.5f, yaw, n );
		f._b = SwrpNpcDirector.Spawn( scene, NpcRegistry.Get( NpcRegistry.B1 ), at + fwd * gap * 0.5f, yaw + 180f, n );
		foreach ( var npc in f._a.Concat( f._b ) ) npc.Hunt = true;
		f._age = 0;
	}

	protected override void OnFixedUpdate()
	{
		var aliveA = _a.Where( v => v.IsValid() && !v.IsDead ).ToList();
		var aliveB = _b.Where( v => v.IsValid() && !v.IsDead ).ToList();
		var winners = aliveA.Count == 0 ? aliveB : aliveB.Count == 0 ? aliveA : null;
		if ( winners is not null && _won is null )
		{
			_won = 0;
			foreach ( var w in winners ) w.Hunt = false;      // so they form up instead of going looking
		}
		if ( _won is { } since && _formed is null && winners.Count > 0 && since > 1f && winners.All( w => w.InSlot ) ) _formed = since;
		if ( _age < 240f && ( _won is null || _formed is null && _won.Value < 40f ) ) return;

		var all = _a.Concat( _b ).Where( v => v.IsValid() ).ToList();
		var combat = all.Sum( v => v.CombatSeconds );
		var report = new
		{
			n = _n,
			seconds = (float)_age,
			winner = aliveA.Count > 0 && aliveB.Count == 0 ? "clones" : aliveB.Count > 0 && aliveA.Count == 0 ? "droids" : "none",
			left = winners?.Count ?? 0,
			friendly_hits = SwrpNpc.FriendlyHits,
			held_for_friend = SwrpNpc.BlockedBursts,
			shots = SwrpNpc.Bursts,
			cover_share = combat > 0 ? all.Sum( v => v.CoverSeconds ) / combat : 0f,
			form_up_seconds = _formed,
		};
		FileSystem.Data.CreateDirectory( "anim_rec" );
		FileSystem.Data.WriteAllText( $"anim_rec/squad_fight_{_n}.json", JsonSerializer.Serialize( report ) );
		Log.Info( $"[SWRP] squad fight {_n}v{_n}: {JsonSerializer.Serialize( report )}" );
		GameObject.Destroy();
	}
}
