using System;

namespace Swrp;

/// <summary>
/// How settled a rifleman is (Angelo, 2026-10-21: clones "move to a relaxed stance when danger is not present after about 60
/// seconds", with ambient idles). A ladder, host-decided and synced: <b>combat</b> while an enemy has been seen, shot at it or
/// hurt it in the last few seconds; <b>alert</b> after that, rifle still up and turned on the last contact, scanning;
/// <b>relaxed</b> after <see cref="RelaxSeconds"/> with nothing: GMod's lowered rifle (the passive set) and, standing still,
/// now and then one of Squad's emotes on the upper body (a hand across the visor, a thumbs-up), never two of a
/// squad within a few seconds of each other. Any threat (an enemy seen, a hit, a hostile shot heard, a bolt passing close, a
/// squadmate down) puts it straight back in combat. Clone NPCs only: droids keep their own idles, players are untouched.
/// </summary>
public sealed partial class SwrpNpc
{
	public const int CalmCombat = 0, CalmAlert = 1, CalmRelaxed = 2;

	[ConVar( "swrp_npc_relax_seconds", Help = "Seconds without a threat before NPC clones relax (lower their rifles, idle)", Saved = true )]
	public static float RelaxSeconds { get; set; } = 60f;

	[ConVar( "swrp_npc_ambient", Help = "Relaxed NPC clones play ambient idles (visor wipe, helmet tap, ...)", Saved = true )]
	public static bool Ambient { get; set; } = true;

	/// <summary> Combat drops to alert this long after the last threat. </summary>
	public const float AlertAfter = 6f;

	/// <summary> 0 combat, 1 alert, 2 relaxed (synced: every machine poses it). </summary>
	[Sync( SyncFlags.FromHost )] public int Calm { get; set; } = CalmAlert;

	TimeSince _sinceThreat;
	TimeUntil _nextAmbient = 10f;
	Model _gmodRelaxed;

	static (string Name, float Weight)[] AmbientPool => new[] { ("amb_visorwipe", 3f), ("amb_thumbsup", 1.5f) };      // (not a readonly static: a hotload kept the old list)

	/// <summary> Host: when each squad last started an ambient idle (no two within <see cref="SquadGap"/> s). </summary>
	static Dictionary<int, float> _squadAmbient;
	const float SquadGap = 4f;

	/// <summary> Dev: every ambient idle started (time, squad, NPC, idle) and every threat reaction (cue time, NPC), for the tests. </summary>
	public static List<(float T, int Squad, string Npc, string Name)> AmbientLog { get; } = new();
	public static List<(float Cue, float Snapped, string Npc)> ReactionLog { get; } = new();

	bool Calms => Def.IsClone && Def.Style == NpcStyle.Rifleman;

	/// <summary> Host: something dangerous happened to or near this NPC: straight back to combat. </summary>
	public void Threat()
	{
		_sinceThreat = 0;
		if ( Calm == CalmCombat ) return;
		ReactionLog.Add( (Time.Now, Time.Now, DisplayName) );
		Calm = CalmCombat;
		_nextAmbient = Game.Random.Float( 12f, 30f );
	}

	/// <summary>
	/// Host, every shot fired (NPCs' and players'): NPCs it passed close to, hostile shooters they could hear (1500 units), react.
	/// A friendly trooper practising at the range doesn't keep them on edge.
	/// </summary>
	public static void HeardShot( Scene scene, Vector3 from, Vector3 to, string shooterSide )
	{
		if ( !Networking.IsHost || scene is null ) return;
		foreach ( var n in scene.GetAllComponents<SwrpNpc>() )
		{
			if ( n.IsDead || n.Calm == CalmCombat && n._sinceThreat < 1f ) continue;
			var chest = n.WorldPosition + Vector3.Up * 44f;
			var hostile = AllegianceRegistry.AreHostile( n.Allegiance, shooterSide );
			if ( hostile && from.Distance( chest ) < 1500f || InLane( chest, from, to, 150f ) && !( n.WorldPosition.Distance( from ) < 60f ) )
				n.Threat();
		}
	}

	/// <summary> Host, every think: step down the ladder as time passes without a threat; idle when relaxed. </summary>
	void ThinkCalm()
	{
		if ( _target.IsValid() && _sinceSeen < 1f ) Threat();
		if ( !Calms ) return;
		var want = _sinceThreat < AlertAfter ? CalmCombat : _sinceThreat < RelaxSeconds ? CalmAlert : CalmRelaxed;
		if ( want > Calm ) Calm = want;      // (only ever down here: up is Threat's, at once)
		if ( Calm == CalmRelaxed ) TickAmbient();
	}

	void TickAmbient()
	{
		if ( !Ambient || _nextAmbient.Relative > 0 || _velocity.Length > 20f ) return;
		_squadAmbient ??= new();
		if ( SquadId != 0 && _squadAmbient.TryGetValue( SquadId, out var last ) && Time.Now - last < SquadGap )
		{
			_nextAmbient = Game.Random.Float( 2f, 5f );
			return;
		}
		var total = AmbientPool.Sum( a => a.Weight );
		var pick = Game.Random.Float( 0f, total );
		var name = AmbientPool[^1].Name;
		foreach ( var a in AmbientPool )
		{
			if ( ( pick -= a.Weight ) <= 0f ) { name = a.Name; break; }
		}
		if ( SquadId != 0 ) _squadAmbient[SquadId] = Time.Now;
		AmbientLog.Add( (Time.Now, SquadId, DisplayName, name) );
		_nextAmbient = Game.Random.Float( 18f, 45f );
		PlayAmbient( name );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayAmbient( string name )
	{
		if ( _body.IsValid() && Calm == CalmRelaxed ) SwrpEmotes.GmodGesture( _body, name );
	}

	/// <summary>
	/// Every machine, before the body animates: the relaxed clone on GMod's lowered-rifle set, the others on the rifle set; any
	/// idle stops the moment it leaves relaxed. Returns true while relaxed.
	/// </summary>
	bool PoseCalm()
	{
		if ( _gmod is null || !Calms ) return false;
		var relaxed = Calm == CalmRelaxed;
		_gmodRelaxed ??= Def.Class.LoadGmod( "passive" );
		var want = relaxed && _gmodRelaxed is not null ? _gmodRelaxed : _gmod;
		if ( _body.Model != want )
		{
			SwrpEmotes.FadeFromHere( _body );      // (blend the arms over, as players' set changes do)
			if ( !relaxed ) SwrpEmotes.StopGmodGesture( _body );
			_body.Model = want;
			SwrpOutfit.Apply( _body, Def.BattalionId );      // (the markings again on the new model)
		}
		return relaxed;
	}

	/// <summary> Every machine: a relaxed clone now and then glances at the squadmate nearest it for a couple of seconds. </summary>
	Vector3? Glance()
	{
		var beat = ( Time.Now + ( GameObject.Id.GetHashCode() & 255 ) * 0.05f ) % 11f;
		if ( beat > 2.5f ) return null;
		var mate = Scene.GetAllComponents<SwrpNpc>().Where( n => n != this && !n.IsDead && n.WorldPosition.Distance( WorldPosition ) < 350f )
			.OrderBy( n => n.WorldPosition.DistanceSquared( WorldPosition ) ).FirstOrDefault();
		return mate is null ? null : ( mate.WorldPosition + Vector3.Up * 60f - Eye ).Normal;
	}

	/// <summary> Host: alert, with no one in sight, it keeps its rifle on the last contact, sweeping a little either side. </summary>
	Vector3 AlertAim()
	{
		if ( Calm != CalmAlert || _lastSeen == Vector3.Zero || !Calms ) return Vector3.Zero;
		var to = ( _lastSeen - WorldPosition ).WithZ( 0 );
		if ( to.Length < 100f ) return Vector3.Zero;
		var sweep = Rotation.FromYaw( MathF.Sin( Time.Now * 0.6f + ( GameObject.Id.GetHashCode() & 63 ) ) * 25f );
		return WorldPosition + sweep * to + Vector3.Up * 50f;
	}
}
