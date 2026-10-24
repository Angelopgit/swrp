using System;

namespace Swrp;

/// <summary>
/// Squad tactics for riflemen (clones and B1s), host side (Angelo, 2026-10-21: "real military tactics"). NPCs spawned together
/// share a <see cref="SquadId"/>. In a fight each one holds a cover spot picked from the navmesh (hidden kneeling from every
/// enemy it knows of, able to see the target standing, not taken by a squadmate and not in a squadmate's line of fire), ducks
/// and peeks from it, and kneels where it stands when there is none. Squads closing on an enemy bound: half move to the next
/// cover while the other half hold and shoot, then swap. Nobody fires with a friend in the way: they step aside instead. When
/// the fight is over the squad forms on its leader: a wedge on the move, a 360-degree ring with outward sectors when halted,
/// or a line (swrp_npc_formation). Callouts float over their heads; swrp_npc_squad_debug draws the reasoning.
/// </summary>
public sealed partial class SwrpNpc
{
	/// <summary> Beyond this the squad bounds forward (half moving, half covering); inside it they hold and fight. </summary>
	public const float BoundRange = 1100f;

	/// <summary> Host: NPCs with the same non-zero id fight and move as one squad. </summary>
	public int SquadId { get; set; }

	[ConVar( "swrp_npc_squad_debug", Help = "Host: draw NPC squad reasoning (cover picks, fire lanes, formation slots)" )]
	public static bool SquadDebug { get; set; }

	[ConVar( "swrp_npc_formation", Help = "NPC squads when clear: auto (wedge moving, ring halted), wedge, line, ring", Saved = true )]
	public static string FormationMode { get; set; } = "auto";

	[ConVar( "swrp_npc_callouts", Help = "NPC squad callouts over their heads", Saved = true )]
	public static bool Callouts { get; set; } = true;

	/// <summary> Dev: totals since the last <see cref="ResetSquadStats"/> (the scripted fights). </summary>
	public static int FriendlyHits, BlockedBursts, Bursts;
	public static void ResetSquadStats() { FriendlyHits = 0; BlockedBursts = 0; Bursts = 0; }
	public float CombatSeconds { get; private set; }
	public float CoverSeconds { get; private set; }

	Vector3? _cover;           // the spot it holds in a fight
	TimeUntil _coverHold;      // how long before picking a better one
	TimeUntil _peekSwitch;
	bool _peeking = true;
	bool _bounding;            // moving to the next spot while the others cover
	Vector3? _slot;            // formation slot when clear
	Vector3 _watch;            // the sector to face in a halted ring
	TimeSince _sinceCallout = 100;
	TimeSince _sinceStep = 100;

	// ----- the squad -----

	sealed class Squad { public int Id; public int MovingGroup; public TimeSince SinceSwap; public TimeSince SinceClear = 100; public Vector3 LeaderWas; public TimeSince LeaderStill; public bool CalledClear; }
	static Dictionary<int, Squad> _squads;
	static int _nextSquad = 1;
	public static int NewSquadId() => _nextSquad++;

	TimeSince _sinceSquadLook = 10;

	/// <summary>
	/// Host: riflemen spawned one at a time (a Game Master placing them) team up with friendly riflemen near them: they join a
	/// team within 500 units that has room, or start one with a loner.
	/// </summary>
	void FindSquad()
	{
		if ( SquadId != 0 || Def.Style != NpcStyle.Rifleman || _sinceSquadLook < 2f ) return;
		_sinceSquadLook = Game.Random.Float( 0f, 0.5f );
		var near = Scene.GetAllComponents<SwrpNpc>().Where( n => n != this && !n.IsDead && n.Def.Style == NpcStyle.Rifleman && n.Allegiance == Allegiance
			&& n.WorldPosition.Distance( WorldPosition ) < 500f ).OrderBy( n => n.WorldPosition.Distance( WorldPosition ) ).ToList();
		if ( near.FirstOrDefault( n => n.SquadId != 0 && n.Mates().Count() < 5 ) is { } team ) SquadId = team.SquadId;
		else if ( near.FirstOrDefault( n => n.SquadId == 0 ) is { } loner ) SquadId = loner.SquadId = NewSquadId();
	}

	Squad MySquad => SquadId == 0 ? null : ( ( _squads ??= new() ).TryGetValue( SquadId, out var s ) ? s : _squads[SquadId] = new Squad { Id = SquadId } );

	IEnumerable<SwrpNpc> Mates() => SquadId == 0 ? Enumerable.Empty<SwrpNpc>()
		: Scene.GetAllComponents<SwrpNpc>().Where( n => n.SquadId == SquadId && !n.IsDead );

	/// <summary> Its place in the squad: the leader is 0 (the lowest call order still standing). </summary>
	int Index => Mates().OrderBy( n => n.GameObject.Id ).TakeWhile( n => n != this ).Count();
	SwrpNpc Leader => Mates().OrderBy( n => n.GameObject.Id ).FirstOrDefault();

	/// <summary> Every enemy this squad knows of (each member's target), for scoring cover against all of them. </summary>
	List<Vector3> Threats()
	{
		var list = new List<Vector3>();
		foreach ( var m in SquadId == 0 ? new[] { this } : Mates() )
			if ( m._target.IsValid() && m._sinceSeen < 6f ) list.Add( SwrpNpcDirector.AimPoint( m._target ) + Vector3.Up * 10f );
		return list.Distinct().Take( 4 ).ToList();
	}

	// ----- in a fight -----

	/// <summary> Host, the rifleman's Think with an enemy seen in the last few seconds. False: nothing for the squad to do. </summary>
	bool SquadFight( bool visible, float dist )
	{
		if ( Def.Style != NpcStyle.Rifleman ) return false;
		FindSquad();
		// a squadmate's contact is everyone's: those who can't see it yet move up on where it was called
		if ( ( !_target.IsValid() || _sinceSeen > 6f ) && Mates().FirstOrDefault( m => m._target.IsValid() && m._sinceSeen < 3f ) is { } spotter )
		{
			_target = spotter._target;
			_lastSeen = spotter._lastSeen;
			_sinceSeen = 3f;
		}
		if ( !_target.IsValid() || _sinceSeen > 6f ) return false;
		CombatSeconds += 0.2f;
		var squad = MySquad;
		if ( squad is not null ) { squad.SinceClear = 0; squad.CalledClear = false; }
		_slot = null;
		if ( _sinceSeen < 0.3f && _sinceCallout > 8f && Index == 0 ) Callout( "Contact!" );

		// bounding: while the enemy is beyond good range, the two halves take turns to move up
		var far = dist > BoundRange || !visible;
		if ( squad is not null && Mates().Count() > 1 && far )
		{
			var group = Index % 2;
			var movers = Mates().Where( m => m.Index % 2 == squad.MovingGroup ).ToList();
			if ( squad.SinceSwap > 7f || squad.SinceSwap > 1.5f && movers.All( m => !m._bounding ) )
			{
				squad.MovingGroup ^= 1;
				squad.SinceSwap = 0;
				foreach ( var m in Mates() ) m._bounding = m.Index % 2 == squad.MovingGroup;
				Leader?.Callout( squad.MovingGroup == 0 ? "Moving!" : "Covering!" );
			}
			if ( group == squad.MovingGroup && _bounding )
			{
				if ( _cover is { } c && c.Distance( WorldPosition ) < 30f && c.Distance( _target.WorldPosition ) < dist + 40f && _coverHold.Relative < 6f )
					_bounding = false;      // arrived
				else if ( PickCover( toward: true ) is { } next ) { HoldCover( next, Dash ); return true; }
				else if ( _cover is not { } open || open.Distance( WorldPosition ) < 40f )
				{
					// nothing to hide behind out there: bound anyway, a rush of a few seconds, and go down at the end of it
					var to = ( _target.WorldPosition - WorldPosition ).WithZ( 0 ).Normal;
					var side = Vector3.Cross( to, Vector3.Up ) * ( group == 0 ? 1f : -1f ) * Game.Random.Float( 0f, 120f );
					if ( Scene.NavMesh.GetClosestPoint( WorldPosition + to * 320f + side ) is { } rush ) { HoldCover( rush, Dash ); _bounding = false; return true; }
					_bounding = false;
				}
			}
		}
		else _bounding = false;

		// hold a spot: find cover if this one is exposed or old; none, kneel where it stands
		var here = _cover is { } at && at.Distance( WorldPosition ) < 40f;
		if ( _cover is null || _coverHold.Relative <= 0 || here && !Hidden( _cover.Value ) )
		{
			if ( PickCover( toward: false ) is { } spot ) HoldCover( spot, Dash );
			else if ( Mates().FirstOrDefault( m => m != this && m.WorldPosition.Distance( WorldPosition ) < 60f ) is { } close
				&& Scene.NavMesh.GetClosestPoint( WorldPosition + ( WorldPosition - close.WorldPosition ).WithZ( 0 ).Normal * 90f ) is { } apart )
				HoldCover( apart, Patrol );      // no cover and on top of a squadmate: spread out first
			else { _cover = WorldPosition; _coverHold = 4f; StopMoving(); Crouched = true; }
		}
		if ( _cover is { } goal && goal.Distance( WorldPosition ) > 40f ) { MoveTo( goal, Dash ); return true; }

		// at the spot: duck and peek, each on its own rhythm (peeking stands up if kneeling can't see over)
		if ( Hidden( WorldPosition ) ) CoverSeconds += 0.2f;      // (kneeling in the open doesn't count)
		StopMoving();
		if ( _peekSwitch.Relative <= 0 )
		{
			_peeking = !_peeking;
			_peekSwitch = _peeking ? Game.Random.Float( 2f, 4f ) : Game.Random.Float( 1f, 2.2f );
		}
		Crouched = !_peeking || CanSee( WorldPosition + Vector3.Up * 40f * WorldScale.z );
		return true;
	}

	void HoldCover( Vector3 spot, float speed )
	{
		_cover = spot;
		_coverHold = Game.Random.Float( 6f, 10f );
		_peeking = false;
		_peekSwitch = Game.Random.Float( 0.8f, 1.6f );
		MoveTo( spot, speed );
		if ( SquadDebug ) DebugOverlay.Line( WorldPosition + Vector3.Up * 20f, spot + Vector3.Up * 20f, Color.Cyan, 2f );
	}

	/// <summary> True if no enemy the squad knows of can see a kneeling NPC at the point. </summary>
	bool Hidden( Vector3 p )
	{
		var head = p + Vector3.Up * 40f * WorldScale.z;
		foreach ( var t in Threats() )
			if ( !Blocked( t, head ) ) return false;
		return true;
	}

	bool CanSee( Vector3 from ) => _target.IsValid() && !Blocked( from, SwrpNpcDirector.AimPoint( _target ) );

	/// <summary>
	/// The best cover around it (or, bounding, around a point a bound towards the enemy): the far side of every solid thing
	/// close by (crates, rocks, walls, wrecks), as seen from the enemy, and a few random navmesh points. A spot must hide it
	/// kneeling from every known enemy; then it scores for seeing the target standing (to peek), being close, and (bounding)
	/// gaining ground. Spots a squadmate holds or that sit in a squadmate's line of fire are skipped.
	/// </summary>
	Vector3? PickCover( bool toward )
	{
		if ( !_target.IsValid() ) return null;
		var enemy = _target.WorldPosition;
		var toEnemy = ( enemy - WorldPosition ).WithZ( 0 ).Normal;
		var centre = toward ? WorldPosition + toEnemy * MathF.Min( 380f, WorldPosition.Distance( enemy ) * 0.4f ) : WorldPosition;
		var mates = Mates().Where( m => m != this ).ToList();
		Vector3? best = null;
		var bestScore = float.MinValue;
		foreach ( var p in CoverCandidates( centre, toward ? 320f : 450f, enemy ) )
		{
			if ( p.Distance( enemy ) < 200f || MathF.Abs( p.z - WorldPosition.z ) > 120f ) continue;
			if ( mates.Any( m => ( m._cover ?? m.WorldPosition ).Distance( p ) < 70f ) ) continue;
			if ( mates.Any( m => m._target.IsValid() && InLane( p, m.WorldPosition + Vector3.Up * 50f, SwrpNpcDirector.AimPoint( m._target ), 40f ) ) ) continue;
			if ( !Hidden( p ) ) continue;
			var score = -p.Distance( WorldPosition ) * 0.01f;
			if ( !Blocked( p + Vector3.Up * 62f * WorldScale.z, SwrpNpcDirector.AimPoint( _target ) ) ) score += 4f;      // can peek
			if ( toward ) score += ( WorldPosition.Distance( enemy ) - p.Distance( enemy ) ) * 0.01f;
			if ( score > bestScore ) { bestScore = score; best = p; }
			if ( SquadDebug ) DebugOverlay.Sphere( new Sphere( p, 8f ), Color.Green, 2f );
		}
		return best;
	}

	IEnumerable<Vector3> CoverCandidates( Vector3 centre, float radius, Vector3 enemy )
	{
		var n = 0;
		foreach ( var go in Scene.FindInPhysics( new Sphere( centre, radius ) ) )
		{
			if ( n >= 14 || go.Root.Tags.Has( Tag ) || go.Root.Tags.Has( "player" ) || go.Root.Tags.Has( "npcwreck" ) || go.Tags.Has( "ragdoll" ) || go.Tags.Has( "trigger" ) ) continue;
			var b = go.GetBounds();
			if ( b.Size.z < 30f || b.Size.x > 1200f || b.Size.y > 1200f ) continue;      // too low to hide behind, or the ground / a whole building
			var away = ( b.Center - enemy ).WithZ( 0 ).Normal;
			var behind = b.Center.WithZ( centre.z ) + away * ( MathF.Max( b.Size.x, b.Size.y ) * 0.5f + 28f );
			if ( Scene.NavMesh.GetClosestPoint( behind ) is { } p && p.Distance( behind ) < 60f ) { n++; yield return p; }
		}
		for ( var i = 0; i < 8; i++ )
			if ( Scene.NavMesh.GetRandomPoint( centre, radius ) is { } p ) yield return p;
	}

	/// <summary> True if <paramref name="p"/> is within <paramref name="width"/> of the shot from a to b (and between them). </summary>
	static bool InLane( Vector3 p, Vector3 a, Vector3 b, float width )
	{
		if ( p.WithZ( 0 ).Distance( a.WithZ( 0 ) ) < 24f && MathF.Abs( p.z - a.z ) < 40f ) return true;      // right at the muzzle
		var ab = b - a;
		var t = Vector3.Dot( p - a, ab ) / MathF.Max( 1f, ab.LengthSquared );
		if ( t <= 0f || t > 0.98f ) return false;
		return ( a + ab * t ).Distance( p ) < width;
	}

	/// <summary>
	/// Host, before each burst: a friend (squadmate, allied NPC or trooper) in the line of fire. If so the NPC holds fire and
	/// side-steps off the line instead.
	/// </summary>
	bool FriendInLane( Vector3 from, Vector3 aim )
	{
		Vector3? friend = null;
		foreach ( var o in Scene.GetAllComponents<SwrpNpc>() )
			if ( o != this && !o.IsDead && AllegianceRegistry.AreFriendly( Allegiance, o.Allegiance ) && InLane( o.WorldPosition + Vector3.Up * ( o.Crouched ? 26f : 40f ), from, aim, 30f ) ) { friend = o.WorldPosition; break; }
		if ( friend is null )
			foreach ( var p in SwrpPlayers.Valid )
				if ( p.CharacterId is not null && AllegianceRegistry.AreFriendly( Allegiance, p.Allegiance ) && InLane( p.WorldPosition + Vector3.Up * 40f, from, aim, 30f ) ) { friend = p.WorldPosition; break; }
		if ( friend is null ) return false;
		BlockedBursts++;
		if ( SquadDebug ) DebugOverlay.Line( from, aim, Color.Red, 0.5f );
		if ( _sinceStep > 1.5f )
		{
			// a step to whichever side is further from the friend
			_sinceStep = 0;
			var side = Vector3.Cross( ( aim - from ).WithZ( 0 ).Normal, Vector3.Up );
			if ( Vector3.Dot( friend.Value - WorldPosition, side ) > 0 ) side = -side;
			if ( Scene.NavMesh.GetClosestPoint( WorldPosition + side * Game.Random.Float( 60f, 100f ) ) is { } step )
			{
				_cover = step;
				_coverHold = 3f;
				MoveTo( step, Advance );
			}
			if ( _sinceCallout > 4f ) Callout( "Check fire!" );
		}
		return true;
	}

	/// <summary> Host: a squadmate went down near it. </summary>
	void MateDown()
	{
		foreach ( var m in Mates() )
			if ( m != this && m.WorldPosition.Distance( WorldPosition ) < 900f ) { m.Callout( "Man down!" ); break; }
		foreach ( var m in Mates() ) m.Threat();
	}

	// ----- clear: formations -----

	/// <summary> Host, the rifleman's Think with nothing in sight: keep its place in the squad's formation. False: no squad. </summary>
	bool SquadFormUp()
	{
		FindSquad();
		var squad = MySquad;
		if ( squad is null || Def.Style != NpcStyle.Rifleman ) return false;
		_cover = null;
		_bounding = false;
		var leader = Leader;
		if ( leader == this || !leader.IsValid() )
		{
			if ( squad.SinceClear > 1.5f && !squad.CalledClear && Mates().Count() > 1 ) { squad.CalledClear = true; Callout( "Clear! Form up!" ); }
			if ( leader.IsValid() && leader.WorldPosition.Distance( squad.LeaderWas ) > 20f ) { squad.LeaderWas = leader.WorldPosition; squad.LeaderStill = 0; }
			// after a fight the leader holds a moment while the squad consolidates on it, then travels as before; the rest follow
			if ( squad.SinceClear < 8f && Mates().Count() > 1 ) { StopMoving(); return true; }
			return false;
		}
		if ( leader._target.IsValid() && leader._sinceSeen < 6f ) return false;      // the leader is still fighting: keep at it

		var i = Index;
		var n = Mates().Count();
		var moving = squad.LeaderStill < 2f;
		var mode = FormationMode == "auto" ? ( moving ? "wedge" : "ring" ) : FormationMode;
		var heading = leader.WorldRotation;
		Vector3 offset;
		switch ( mode )
		{
			case "line":
				offset = new Vector3( 0, ( i % 2 == 1 ? 1 : -1 ) * ( ( i + 1 ) / 2 ) * 90f, 0 );
				_watch = heading.Forward;
				break;
			case "ring":
				// all round the leader, each facing out over its own sector (ATP 3-21.8's perimeter)
				var a = MathF.PI * 2f * ( i - 1 ) / Math.Max( 1, n - 1 );
				var outward = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 );
				offset = outward * 110f;
				_watch = heading * outward;
				break;
			default:      // wedge: alternate sides, each a step further back and out
				var k = ( i + 1 ) / 2;
				offset = new Vector3( -k * 80f, ( i % 2 == 1 ? 1 : -1 ) * k * 80f, 0 );
				_watch = heading * new Vector3( 1, ( i % 2 == 1 ? 1 : -1 ) * 0.6f, 0 ).Normal;
				break;
		}
		var want = leader.WorldPosition + heading * offset;
		foreach ( var o in Scene.GetAllComponents<SwrpNpc>() )
		{
			if ( o == this || o.IsDead || o.SquadId == SquadId ) continue;
			var d = ( want - o.WorldPosition ).WithZ( 0 );
			if ( d.Length < 70f ) want += ( d.Length < 1f ? heading.Right : d.Normal ) * ( 70f - d.Length );
		}
		if ( Scene.NavMesh.GetClosestPoint( want ) is { } p ) want = p;
		_slot = want;
		if ( SquadDebug ) DebugOverlay.Sphere( new Sphere( want, 10f ), Color.Yellow, 0.25f );
		if ( want.Distance( WorldPosition ) > 40f ) MoveTo( want, want.Distance( WorldPosition ) > 250f ? Advance : Patrol );
		else { StopMoving(); Crouched = mode == "ring" && Calm != CalmRelaxed; }      // (relaxed, they stand at ease)
		return true;
	}

	/// <summary> Dev: what it is doing, for swrp_npc_info-style checks. </summary>
	public string SquadState => $"sq {SquadId}#{Index} {( _bounding ? "bounding " : "" )}cover {( _cover is { } c ? c.Distance( WorldPosition ) : -1 ):0} hidden {( _cover is { } h && _target.IsValid() ? Hidden( h ) : false )} peek {_peeking} crouch {Crouched} slot {( _slot is { } s ? s.Distance( WorldPosition ) : -1 ):0} target {_target.IsValid()} seen {_sinceSeen:0.0} vel {_velocity.Length:0}";

	/// <summary> Dev: how its cover search went just now (objects near, candidates, hidden ones). </summary>
	public string DevCover()
	{
		if ( !_target.IsValid() ) return "no target";
		var objs = Scene.FindInPhysics( new Sphere( WorldPosition, 450f ) ).Select( g => $"{g.Name}:{g.GetBounds().Size.z:0}" ).Take( 12 );
		var c = CoverCandidates( WorldPosition, 450f, _target.WorldPosition ).ToList();
		return $"threats {Threats().Count} objs [{string.Join( ",", objs )}] candidates {c.Count} hidden {c.Count( Hidden )}";
	}

	/// <summary> Dev: in its formation slot (the leader always is). </summary>
	public bool InSlot => Leader == this || _slot is { } s && s.Distance( WorldPosition ) < 40f;

	/// <summary> The sector a halted squadmate watches (zero: none), for Face. </summary>
	Vector3 Watch => _slot is { } s && s.Distance( WorldPosition ) < 40f ? _watch : Vector3.Zero;

	// ----- callouts -----

	void Callout( string text )
	{
		Voice( VoiceFor( text ) );      // (droids say it: Code/Npc/SwrpNpc.Voice.cs)
		if ( !Callouts ) return;
		_sinceCallout = 0;
		ShowCallout( text );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	void ShowCallout( string text )
	{
		if ( VisorOverlay.HudHidden ) return;      // F8: nothing over the picture
		DebugOverlay.Text( WorldPosition + Vector3.Up * 84f * WorldScale.z, text, 14f, TextFlag.Center, Color.White, 1.8f );
	}
}
