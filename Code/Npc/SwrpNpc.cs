using System;

namespace Swrp;

/// <summary>
/// A battle droid, super battle droid, droideka or clone trooper NPC. The host thinks for it (find a hostile target, move on the
/// navmesh, take cover, fire) and owns its health; every machine builds and animates the body from the synced state. Shots go
/// through the same rules as players' (<see cref="SwrpCombat"/>): stun rounds, hit zones, the kill feed, and the allegiance
/// table for who is an enemy. Spawned by <see cref="SwrpNpcDirector"/>; the kind comes from <see cref="NpcRegistry"/>.
/// </summary>
[Title( "SWRP NPC" ), Category( "SWRP" ), Icon( "smart_toy" )]
public sealed partial class SwrpNpc : Component
{
	public const string Tag = "npc";
	public const float DeadLinger = 20f;

	[Sync( SyncFlags.FromHost )] public string NpcId { get; set; } = NpcRegistry.B1;
	[Sync( SyncFlags.FromHost )] public string CallSign { get; set; }
	[Sync( SyncFlags.FromHost )] public float Health { get; set; } = 100f;
	[Sync( SyncFlags.FromHost )] public float Armor { get; set; }
	[Sync( SyncFlags.FromHost )] public float Shield { get; set; }
	[Sync( SyncFlags.FromHost )] public bool IsDead { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Stunned { get; set; }

	/// <summary> A droideka unfolded with its shield up (it can't move like that). </summary>
	[Sync( SyncFlags.FromHost )] public bool Deployed { get; set; }

	/// <summary> Where it is aiming, for the body's pose (zero = straight ahead). </summary>
	[Sync( SyncFlags.FromHost )] public Vector3 AimAt { get; set; }

	/// <summary> Kneeling to shoot: riflemen holding a position spend much of a firefight down on one knee. </summary>
	[Sync( SyncFlags.FromHost )] public bool Crouched { get; set; }

	public NpcDefinition Def => NpcRegistry.Get( NpcId ) ?? NpcRegistry.All[0];
	public string Allegiance => Def.AllegianceId;
	public string DisplayName => string.IsNullOrEmpty( CallSign ) ? Def.Name : CallSign;
	public bool Alive => !IsDead;

	/// <summary> Host: where to go when nothing is in sight (a Game Master's order or a wave's objective). </summary>
	public Vector3? Objective { get; set; }

	/// <summary> Host: seek out the nearest enemy when nothing is in sight (waves do). </summary>
	public bool Hunt { get; set; }

	public Vector3 Eye => WorldPosition + Vector3.Up * ( Def.Style == NpcStyle.Roller ? 34f : ( Crouched ? 40f : 60f ) * WorldScale.z );

	// ----- every machine: body -----

	SkinnedModelRenderer _body;
	GameObject _gun;
	ModelRenderer _gunMesh;
	CapsuleCollider _capsule;
	float _duck;
	DroidekaRig _rig;
	DroidekaShield _shield;
	Vector3 _lastPos;
	Vector3 _velocity;
	bool _shownDead;
	bool _shownStun;

	protected override void OnStart()
	{
		_lastPos = WorldPosition;
		Tags.Add( Tag );
		BuildBody();
	}

	void BuildBody()
	{
		var def = Def;
		if ( def.Style == NpcStyle.Roller )
		{
			BuildDroideka( def );
			return;
		}

		// armed: the Squad movement set; stock (swrp_stock_anims, the default) GMod's rifle set, as players
		_gmod = def.IsClone && !ClassDefinition.Custom ? def.Class.LoadGmod( "ar2" ) : null;
		var model = _gmod ?? ( def.IsClone ? def.Class.LoadModel( squad: SquadWalk ) : Model.Load( def.ModelPath ) );
		_body = AddComponent<SkinnedModelRenderer>();
		_body.Model = model ?? Model.Load( "models/citizen/citizen.vmdl" );
		if ( def.IsClone ) SwrpOutfit.Apply( _body, def.BattalionId );      // the battalion's markings
		_body.Tint = def.Tint;
		_body.UseAnimGraph = true;
		WorldScale = def.Scale * SwrpPlayer.PlayerScaleFor( Scene );

		// the blaster in its right hand: its weapon item's model (the clones' DC-15A), the droids' E-5 (B2s have theirs built
		// into the forearms)
		var gunModel = def.Gun?.ModelPath ?? ( def.HoldType == 2 ? "models/weapons/bf2_e5.vmdl" : null );
		if ( gunModel is not null )
		{
			var gun = new GameObject( GameObject, true, "Weapon" );
			gun.NetworkMode = NetworkMode.Never;
			gun.Flags |= GameObjectFlags.NotSaved;
			var gunMesh = new GameObject( gun, true, "Mesh" );
			_gunMesh = gunMesh.AddComponent<ModelRenderer>();
			_gunMesh.Model = Model.Load( gunModel );
			def.Gun?.PlaceMesh( gunMesh );
			gun.AddComponent<SwrpMannequinGun>().Body = _body;
			_gun = gun;
		}

		var col = AddComponent<CapsuleCollider>();
		col.Start = Vector3.Up * 12f;
		col.End = Vector3.Up * 62f;
		col.Radius = 13f;
		_capsule = col;
	}

	static ModelRenderer Box( GameObject parent, string name, Vector3 size, Vector3 pos, Color tint, string model = "models/dev/box.vmdl" )
	{
		var m = Model.Load( model );
		var unit = m.Bounds.Size;
		var go = new GameObject( parent, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.LocalPosition = pos;
		go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = m;
		r.Tint = tint;
		return r;
	}

	void BuildDroideka( NpcDefinition def )
	{
		// Battlefront II's droideka, posed by code (Code/Npc/DroidekaRig.cs), in a refracting deflector bubble (DroidekaShield)
		_rig = AddComponent<DroidekaRig>();
		_rig.WantDeployed = Deployed;
		_shield = AddComponent<DroidekaShield>();

		var col = AddComponent<SphereCollider>();
		col.Center = Vector3.Up * 30f;
		col.Radius = 26f;
	}

	protected override void OnUpdate()
	{
		// velocity from movement (networked transforms are interpolated, so this is smooth on every machine)
		var dt = MathF.Max( Time.Delta, 0.001f );
		var v = ( WorldPosition - _lastPos ) / dt;
		_lastPos = WorldPosition;
		_velocity = Vector3.Lerp( _velocity, v.WithZ( 0 ), MathF.Min( 1f, dt * 8f ) );

		if ( IsDead && !_shownDead )
		{
			_shownDead = true;
			OnDiedLocal();
		}
		if ( IsDead ) return;

		if ( Stunned != _shownStun )
		{
			_shownStun = Stunned;
			if ( Stunned ) Sparks( 6 );
		}
		if ( Stunned && Game.Random.Float() < dt * 3f ) Sparks( 1 );

		if ( Def.Style == NpcStyle.Roller ) AnimateRoller( dt );
		else AnimateBody();
	}

	float _bladeW, _stanceLegs, _walkW;

	void AnimateBody()
	{
		if ( !_body.IsValid() ) return;
		var speed = _velocity.Length;
		// a clone with his rifle up stands side-on to the target like a player, left shoulder forward, and standing still takes the
		// shooting stance (Angelo's references, 2026-10-16). The body is this NPC's networked root, so the skeleton turns under
		// it instead (SwrpEmotes.Stance): walking and the look-at are worked out against the turned skeleton
		var clone = Def.IsClone && Def.Gun is { ModelPath: not null } && Def.HoldType == 2;        // the clone rig only
		var shouldered = clone && _gmod is null && !Stunned && SwrpEmotes.ShoulderedAtEye( Def.Gun.ModelPath );      // (a GMod set stands as GMod's clips do)
		_bladeW = _bladeW.Approach( shouldered ? 1f : 0f, Time.Delta * 5f );
		_stanceLegs = _stanceLegs.Approach( shouldered && speed < 30f ? 1f : 0f, Time.Delta * ( speed < 30f ? 4f : 8f ) );
		_walkW = _walkW.Approach( speed >= 30f ? 1f : 0f, Time.Delta * 6f );
		var blade = _bladeW * _bladeW * ( 3f - 2f * _bladeW );
		// walking, the hips turn back towards the aim and the chest alone stays side-on (SwrpEmotes.WalkChestShare)
		var skeletonYaw = -SwrpEmotes.BladeYaw * blade * ( 1f - SwrpEmotes.WalkChestShare * _walkW );
		var chestYaw = -SwrpEmotes.BladeYaw * blade * SwrpEmotes.WalkChestShare * _walkW;
		var local = ( Rotation.FromYaw( skeletonYaw ) * WorldRotation ).Inverse * _velocity;
		var relaxed = PoseCalm();
		_duck = _duck.LerpTo( Crouched || Stunned ? 1f : 0f, Time.Delta * 7f );
		if ( _capsule.IsValid() ) _capsule.End = Vector3.Up * MathX.Lerp( 62f, 40f, _duck );
		_body.Set( "b_grounded", true );
		_body.Set( "holdtype", _gmod is not null ? 0 : Def.HoldType );      // a GMod set's clips hold the gun themselves
		_body.Set( "duck", ClassDefinition.Custom || _gmod is not null ? _duck : _duck * SwrpPlayer.CrouchDepth );      // stock: part way down, as players; a GMod set: GMod's own crouch
		_body.Set( "move_groundspeed", speed );
		// the walk and run clips cover only ~70% of the ground the NPC moves over, so the planted foot slid ~30% of its speed
		// (measured with swrp_npc_foot_debug, Angelo 2026-10-18): play the locomotion faster while moving to plant the feet
		// the stride speed-up was for the Squad clips; the stock clips plant their feet at 1 (measured with swrp_npc_foot_debug,
		// 2026-10-19: 1.4 slid the planted foot 20-57 u/s at a run, 1 kept it under 12)
		// running, the stock run covers a little less ground than an NPC moves: quicken it with speed (229 u/s slid 40)
		_body.PlaybackRate = _gmod is not null || !ClassDefinition.Custom ? 1f : MathX.Lerp( 1f, StrideRate, _walkW );
		// a B1's own run covers about DroidRunStride u/s: dashing faster, it plays quicker so the planted foot holds (2026-10-21:
		// at the 5 m/s cover dash the foot slid 18-50 u/s)
		if ( _gmod is null && Def.Style == NpcStyle.Rifleman && speed > DroidRunStride ) _body.PlaybackRate *= speed / DroidRunStride;
		_body.Set( "move_speed", speed );
		_body.Set( "move_x", local.x );
		_body.Set( "move_y", local.y );
		_body.Set( "wish_groundspeed", speed );
		_body.Set( "wish_speed", speed );
		_body.Set( "wish_x", local.x );
		_body.Set( "wish_y", local.y );
		SwrpEmotes.GmodPace( _body );
		var aim = AimAt == Vector3.Zero ? WorldRotation.Forward : ( AimAt - Eye ).Normal;
		if ( clone )
		{
			SwrpEmotes.HeldModel[_body] = Def.Gun.ModelPath;
			SwrpEmotes.AimDir[_body] = aim;
			if ( AimAt != Vector3.Zero ) SwrpEmotes.AimPoint[_body] = AimAt; else SwrpEmotes.AimPoint.Remove( _body );
			SwrpEmotes.MuzzleOf[_body] = Def.Gun.MuzzleLocal;
			if ( _gmod is not null && !relaxed ) SwrpEmotes.GmodAim[_body] = "ar2";
			else SwrpEmotes.GmodAim.Remove( _body );
			SwrpEmotes.Stance( _body, blade * _stanceLegs * ( 1f - _duck ), blade, WorldRotation.Yaw(), skeletonYaw, chestYaw );
			SwrpEmotes.Apply( _body, SwrpEmotes.None, 0f, 0f, 0f, 0f, 1f );
		}
		if ( FootDebug ) FootSlideProbe( speed );
		// the graph reads the look-at against the unturned body: turned back, head and eyes still find the target
		var look = Rotation.FromYaw( -skeletonYaw ) * ( relaxed && Glance() is { } glance ? glance : aim );
		_body.SetLookDirection( "aim_eyes", look );
		var room = RoomLight.BodyTint( this, WorldPosition );      // takes on the colour of the lamps around it
		_body.Tint = new Color( Def.Tint.r * room.r, Def.Tint.g * room.g, Def.Tint.b * room.b, Def.Tint.a );
		_body.SetLookDirection( "aim_head", look );
		_body.SetLookDirection( "aim_body", SwrpEmotes.BladedBodyLook( look.WithZ( look.z * 0.5f ).Normal, WorldRotation, blade ) );
	}


	void AnimateRoller( float dt )
	{
		if ( _rig.IsValid() )
		{
			_rig.WantDeployed = Deployed;
			_rig.Speed = _velocity.Length;
			_rig.AimAt = AimAt;
		}
		if ( _shield.IsValid() )
		{
			_shield.Up = Deployed && Shield > 0 && ( _rig?.Unfold ?? 1f ) > 0.97f;
			_shield.Strength = Shield / MathF.Max( 1f, Def.Shield );
			_shield.Centre = WorldPosition + Vector3.Up * 40f;
		}
		if ( AimAt != Vector3.Zero && Deployed )
			WorldRotation = Rotation.Lerp( WorldRotation, Rotation.FromYaw( ( AimAt - WorldPosition ).WithZ( 0 ).EulerAngles.yaw ), dt * 6f );
	}

	void OnDiedLocal()
	{
		Sparks( 10 );
		if ( Def.Style == NpcStyle.Roller )
		{
			// the droideka slumps where it stood, its bubble gone
			if ( _rig.IsValid() ) _rig.Dead = true;
			if ( _shield.IsValid() ) _shield.Up = false;
			return;
		}

		if ( _body.IsValid() )
		{
			SwrpCorpses.SpawnRagdoll( Scene, _body )?.Tags.Add( "npcwreck" );
			SwrpEmotes.EndStance( _body );
			_body.Enabled = false;
			foreach ( var r in Components.GetAll<ModelRenderer>( FindMode.EverythingInDescendants ) ) r.Enabled = false;
		}
	}

	void Sparks( int n )
	{
		var at = WorldPosition + Vector3.Up * ( Def.Style == NpcStyle.Roller ? 30f : 50f );
		for ( var i = 0; i < n; i++ )
			SparkFx.Spawn( Scene, at, Vector3.Random * 220f + Vector3.Up * 120f, new Color( 1f, 0.75f, 0.3f ) );
	}

	// ----- host: brain -----

	NavMeshAgent _agent;
	GameObject _target;
	Vector3 _lastSeen;
	TimeSince _sinceSeen = 100;
	TimeSince _sinceThink;
	TimeSince _sinceScan = 10;
	TimeSince _sinceShot;
	TimeSince _sinceBurst;
	int _burstLeft;
	Vector2 _sprayDrift;      // degrees: x yaw, y pitch; walks through a burst (FireAt)
	TimeSince _sinceHit = 100;
	TimeSince _sinceCover = 100;
	TimeSince _sinceDoors;
	TimeSince _sinceWander = 100;
	TimeUntil _stunUntil;
	TimeUntil _coverUntil;
	TimeSince _sinceDeath;
	TimeSince _sinceDeployChange = 10;
	bool _inCover;
	Vector3 _home;

	/// <summary> Host, right after spawning. </summary>
	public void HostInit()
	{
		var def = Def;
		Health = def.Health;
		_sinceThreat = 0;
		Armor = def.Armor;
		Shield = def.Shield;
		_home = WorldPosition;
		_burstLeft = def.Burst;
		_grenades = def.Grenades;
		_grenadeReady = Game.Random.Float( 6f, 14f );     // nobody opens a fight with one
		_sinceGrenadeThink = Game.Random.Float( 0f, 1.5f );

		_agent = AddComponent<NavMeshAgent>();
		_agent.MaxSpeed = def.Style == NpcStyle.Rifleman ? SwrpSpeeds.U( SwrpSpeeds.NpcPatrol ) * def.Scale : def.WalkSpeed;
		_agent.Acceleration = def.Style == NpcStyle.Roller ? 900f : 600f;
		_agent.Radius = def.Style == NpcStyle.Roller ? 24f : 14f;
		_agent.Height = 64f * def.Scale * SwrpPlayer.PlayerScaleFor( Scene );
		_agent.UpdateRotation = false;
		_agent.Separation = 0.4f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		if ( IsDead )
		{
			if ( _sinceDeath > DeadLinger ) GameObject.Destroy();
			return;
		}

		if ( Stunned )
		{
			if ( _stunUntil.Relative <= 0 ) Stunned = false;
			else { StopMoving(); return; }
		}

		// a droideka's shield recharges when nothing has hit it for a while
		if ( Def.Shield > 0 && _sinceHit > 4f && Shield < Def.Shield ) Shield = MathF.Min( Def.Shield, Shield + Def.Shield * 0.15f * Time.Delta );

		if ( _sinceDoors > 0.5f )
		{
			_sinceDoors = 0;
			OpenDoorsAhead();
		}

		Face();
		Shoot();

		if ( _sinceThink < 0.2f ) return;
		_sinceThink = 0;
		Think();
	}

	void Think()
	{
		if ( Tiered ) { var v = _velocity.Length; GaitSeconds[v < 20f ? 3 : v < ( Patrol + Advance ) * 0.5f ? 0 : v < ( Advance + Dash ) * 0.5f ? 1 : 2] += 0.2f; }
		ThinkCalm();      // (Code/Npc/SwrpNpc.Calm.cs)
		var def = Def;
		if ( _sinceScan > 0.5f )
		{
			_sinceScan = 0;
			Scan();
		}

		var visible = _target.IsValid() && _sinceSeen < 0.6f;
		var dist = visible ? _target.WorldPosition.Distance( WorldPosition ) : 0f;

		switch ( def.Style )
		{
			case NpcStyle.Roller:
				if ( visible && dist < def.Range * 0.6f )
				{
					if ( !Deployed ) Deploy( true );
					StopMoving();
					return;
				}
				if ( Deployed && _sinceSeen > 3f ) Deploy( false );
				if ( !Deployed ) Travel( def.RunSpeed );
				return;

			case NpcStyle.Advancer:
				if ( visible )
				{
					if ( dist > 450f ) MoveTo( _target.WorldPosition, def.WalkSpeed );
					else StopMoving();
					return;
				}
				Travel( def.WalkSpeed );
				return;

			default:
				if ( DodgeGrenades() ) return;
				if ( _inCover )
				{
					if ( _coverUntil.Relative > 0 ) return;
					_inCover = false;
				}
				if ( ThinkGrenade() ) return;
				if ( SquadFight( visible, dist ) ) return;      // (Code/Npc/SwrpNpc.Squad.cs)
				if ( visible )
				{
					// hurt or under fire: duck behind something for a moment
					var hurt = Health < def.Health * 0.6f || _sinceHit < 1.5f;
					if ( hurt && _sinceCover > 6f && TakeCover() ) return;
					if ( dist > def.Range * 0.75f ) MoveTo( _target.WorldPosition, Advance );
					else
					{
						StopMoving();
						UpdateCrouch( dist );
					}
					return;
				}
				if ( Crouched && _sinceSeen > 2.5f ) Crouched = false;
				if ( SquadFormUp() ) return;
				Travel( Advance );
				return;
		}
	}

	/// <summary> Nothing in sight: chase the last place an enemy was seen, then the objective, then hunt, then wander home. </summary>
	void Travel( float speed )
	{
		if ( _sinceSeen < 10f && _lastSeen != Vector3.Zero && _lastSeen.Distance( WorldPosition ) > 80f )
		{
			MoveTo( _lastSeen, speed );
			return;
		}
		if ( Objective is { } obj )
		{
			// everyone sent to one point gets their own spot around it, or they all circled it trying to stand on it (Angelo, 2026-10-21)
			var spot = ObjectiveSpot( obj );
			if ( spot.Distance( WorldPosition ) > 40f ) MoveTo( spot, spot.Distance( WorldPosition ) > 250f ? speed : Patrol );
			else StopMoving();
			return;
		}
		if ( Hunt && SwrpNpcDirector.NearestHostile( this, 8000f ) is { } prey )
		{
			MoveTo( prey.WorldPosition, speed );
			return;
		}
		if ( _sinceWander > ( Calm == CalmRelaxed ? 30f : 9f ) )
		{
			_sinceWander = 0;
			var home = Objective ?? _home;
			if ( Scene.NavMesh.GetRandomPoint( home, 350f ) is { } p ) MoveTo( p, Tiered ? Patrol : Def.WalkSpeed * 0.6f );
		}
	}

	/// <summary>
	/// This NPC's own place around an objective shared with others: a spiral out from it by spawn order, one spot per unit (a
	/// squad's leader, its team forming round it, or a loner), squads 220 units apart so their formations don't run together
	/// (Angelo, 2026-10-21: sixty clones sent somewhere stood in one crowd).
	/// </summary>
	Vector3 ObjectiveSpot( Vector3 obj )
	{
		var all = Scene.GetAllComponents<SwrpNpc>().Where( n => !n.IsDead && n.Objective is { } o && o.Distance( obj ) < 1f ).ToList();
		var units = all.Where( n => n.SquadId == 0 ).Concat( all.Where( n => n.SquadId != 0 ).GroupBy( n => n.SquadId ).Select( g => g.OrderBy( n => n.GameObject.Id ).First() ) )
			.OrderBy( n => n.GameObject.Id ).ToList();
		var i = units.IndexOf( this );
		if ( i < 0 ) i = units.Count;
		if ( i == 0 ) return obj;
		var a = i * 2.39996f;      // the golden angle: evenly spread however many there are
		var p = obj + new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0 ) * ( units.Any( n => n.SquadId != 0 ) ? 220f : 60f ) * MathF.Sqrt( i );
		return Scene.NavMesh.GetClosestPoint( p ) ?? p;
	}

	void Scan()
	{
		var found = SwrpNpcDirector.FindTarget( this, Def.Range + 400f, 6 );
		if ( found.IsValid() )
		{
			_target = found;
			_sinceSeen = 0;
			_lastSeen = found.WorldPosition;
		}
		else if ( _target.IsValid() && !SwrpNpcDirector.IsValidTarget( this, _target ) )
			_target = null;
	}

	void Face()
	{
		var aim = _target.IsValid() && _sinceSeen < 1f ? SwrpNpcDirector.AimPoint( _target ) : AlertAim();
		AimAt = aim;
		if ( Def.Style == NpcStyle.Roller && Deployed ) return;     // a deployed droideka turns towards AimAt as it animates

		var dir = aim != Vector3.Zero && Def.Style != NpcStyle.Roller ? aim - WorldPosition : _velocity.Length < 20f && Watch != Vector3.Zero ? Watch : _velocity;
		if ( dir.WithZ( 0 ).Length < 1f ) return;
		var want = Rotation.FromYaw( dir.WithZ( 0 ).EulerAngles.yaw );
		WorldRotation = Rotation.Lerp( WorldRotation, want, Time.Delta * 8f );
	}

	void Shoot()
	{
		var def = Def;
		if ( !_target.IsValid() || _sinceSeen > 0.6f ) return;
		if ( def.Style == NpcStyle.Roller && ( !Deployed || _sinceDeployChange < 1f ) ) return;
		if ( _inCover && _coverUntil.Relative > 0.6f ) return;
		if ( _cover is { } held && held.Distance( WorldPosition ) < 40f && !_peeking || _bounding && _velocity.Length > 60f ) return;
		var aim = SwrpNpcDirector.AimPoint( _target );
		if ( aim.Distance( Eye ) > def.Range ) return;

		if ( _burstLeft <= 0 )
		{
			if ( _sinceBurst < def.BurstPause ) return;
			_burstLeft = def.Burst;
		}
		if ( _sinceShot < def.FireDelay ) return;

		// only fire roughly where it's facing
		if ( def.Style != NpcStyle.Roller && Vector3.Dot( WorldRotation.Forward, ( aim - WorldPosition ).WithZ( 0 ).Normal ) < 0.8f ) return;

		if ( FriendInLane( Muzzle, aim ) ) { _sinceShot = 0; return; }
		_sinceShot = 0;
		Bursts++;
		_burstLeft--;
		if ( _burstLeft <= 0 ) _sinceBurst = 0;
		FireAt( aim );
	}

	Vector3 Muzzle
	{
		get
		{
			if ( Def.Style == NpcStyle.Roller ) return _rig.IsValid() ? _rig.MuzzleWorld() : WorldPosition + WorldRotation.Forward * 26f + Vector3.Up * 40f;
			// a carried weapon item fires from its own muzzle, wherever the hand holds it (while the gun follows the hand)
			if ( Def.Gun is { } g && _gun.IsValid() && _gun.WorldPosition.Distance( WorldPosition ) < 120f * WorldScale.z )
				return _gun.WorldTransform.PointToWorld( g.MuzzleLocal );
			return WorldPosition + WorldRotation.Forward * 22f * WorldScale.z + WorldRotation.Right * 6f + Vector3.Up * ( Crouched ? 32f : 50f ) * WorldScale.z;
		}
	}

	void FireAt( Vector3 aim )
	{
		var def = Def;
		var start = Muzzle;
		var dir = ( aim - start ).Normal;
		var rot = Rotation.LookAt( dir );
		// the spray: the cone widens and the muzzle climbs and wanders through a burst, settling between bursts, so a burst walks
		// across the target instead of every bolt on one line (Angelo, 2026-10-19: "some variations in their spray")
		var shot = Math.Max( 0, def.Burst - _burstLeft - 1 );       // 0 for the first bolt of a burst
		if ( shot == 0 ) _sprayDrift = new Vector2( Game.Random.Float( -0.6f, 0.6f ), Game.Random.Float( -0.4f, 0.4f ) ) * def.Spread;
		_sprayDrift += new Vector2( Game.Random.Float( -0.45f, 0.45f ), -0.35f ) * def.Spread;      // up (negative pitch) and sideways
		if ( _sprayDrift.Length > def.Spread * 2f ) _sprayDrift = _sprayDrift.Normal * def.Spread * 2f;      // long bursts don't climb off the target
		var r = MathF.Sqrt( Game.Random.Float( 0f, 1f ) ) * def.Spread * ( _sinceHit < 1f ? 1.4f : 1f ) * ( 1f + 0.25f * shot );
		var a = Game.Random.Float( 0f, MathF.PI * 2f );
		dir = ( rot * Rotation.From( MathF.Sin( a ) * r + _sprayDrift.y, MathF.Cos( a ) * r + _sprayDrift.x, 0 ) ).Forward;

		var tr = Scene.Trace.Ray( start, start + dir * def.Range )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "ragdoll" )
			.UseHitboxes()
			.Run();
		HeardShot( Scene, start, tr.EndPosition, Allegiance );

		// a Jedi guarding with a lit saber turns the bolt on the blade (Code/Jedi)
		if ( Swrp.Jedi.JediCombat.TryDeflect( tr, GameObject, start, def.DamageAt( tr.Distance ), def.WeaponName, def.BoltColor ) )
		{
			ShowShot( start, tr.EndPosition, tr.Normal, true, false );
			return;
		}

		var hitSomeone = false;
		if ( tr.Hit && tr.GameObject is { } hit )
		{
			if ( SwrpNpcDirector.AllegianceOf( hit ) is { } side && AllegianceRegistry.AreFriendly( Allegiance, side ) ) { FriendlyHits++; if ( SquadDebug ) Log.Info( $"[SWRP] friendly hit: {DisplayName} hit {hit.Root.Name} ({hit.Name}) at {tr.Distance:0}" ); }
			if ( hit.Components.GetInAncestorsOrSelf<SwrpVitals>() is { IsDead: false } vitals && SwrpNpcDirector.IsHostile( this, vitals.GameObject ) )
			{
				var victim = vitals.GetComponent<SwrpPlayer>();
				var (mult, head) = SwrpCombat.Zone( victim, tr );
				if ( vitals.TakeDamage( ( def.DamageAt( tr.Distance ) ) * mult, null, start, def.WeaponName, head, DisplayName ) ) Voice( "enemydown" );
				hitSomeone = true;
			}
			else if ( hit.Components.GetInAncestorsOrSelf<SwrpNpc>() is { IsDead: false } npc && SwrpNpcDirector.IsHostile( this, npc.GameObject ) )
			{
				if ( npc.TakeHit( def.DamageAt( tr.Distance ), null, DisplayName, start, def.WeaponName, false, false, 0f ) ) Voice( "enemydown" );
				hitSomeone = true;
			}
		}

		ShowShot( start, tr.EndPosition, tr.Hit ? tr.Normal : -dir, tr.Hit, !hitSomeone );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowShot( Vector3 start, Vector3 end, Vector3 normal, bool hit, bool hitWorld )
	{
		var def = Def;
		var color = def.BoltColor;
		if ( _body.IsValid() ) { _body.Set( "b_attack", true ); SwrpEmotes.Kick( _body ); }
		if ( _body.IsValid() && _gmod is not null ) SwrpEmotes.GmodGesture( _body, "range_ar2" );
		if ( _rig.IsValid() ) _rig.Fire();
		Sound.Play( def.FireSound, start );
		SwrpFx.MuzzleFlash( Scene, start, color );
		BlasterBolt.Spawn( Scene, start, end, color );
		if ( hit )
		{
			SwrpFx.Impact( Scene, end, normal, color, hitWorld );
			Sound.Play( SwrpAudio.BlasterImpact, end );
		}
	}

	// ----- host: kneeling -----

	TimeUntil _crouchSwitch;

	/// <summary>
	/// Holding a position and shooting: riflemen spend most of it down on one knee (more when hurt), now and then rising for
	/// a better look; never when the enemy is right on top of them. Each one keeps its own rhythm.
	/// </summary>
	void UpdateCrouch( float dist )
	{
		if ( Def.Style != NpcStyle.Rifleman || _crouchSwitch.Relative > 0 ) return;
		var hurt = Health < Def.Health * 0.6f;
		var kneel = dist > 260f && Game.Random.Float() < ( Crouched ? 0.6f : 0.75f ) + ( hurt ? 0.15f : 0f );
		Crouched = kneel;
		_crouchSwitch = kneel ? Game.Random.Float( 3f, 7f ) : Game.Random.Float( 1.2f, 3f );
	}

	// ----- host: thermal detonators -----

	/// <summary> The base chance an NPC that could throw one does, each time it considers it (about every 1.5 s). </summary>
	public const float GrenadeChance = 0.4f;

	/// <summary> Seconds between two throws from the same side, and how many of a side's may be in the air at once. </summary>
	public const float SideGap = 8f;
	public const int SideInFlight = 2;

	static Dictionary<string, float> _sideThrew = new();

	int _grenades;
	TimeUntil _grenadeReady;
	TimeSince _sinceGrenadeThink;

	/// <summary>
	/// Maybe throw a thermal detonator: only riflemen with one left, off their own cooldown and their side's (one throw per
	/// side every <see cref="SideGap"/> seconds, at most <see cref="SideInFlight"/> in the air), at an enemy 320 to 1300 units
	/// away on about the same deck. The chance starts at <see cref="GrenadeChance"/>, rises when the enemy has ducked out of
	/// sight (flush them out) or several are bunched together, and drops at close range. Never where it would land near a
	/// friend, and only with a clear arc.
	/// </summary>
	bool ThinkGrenade()
	{
		if ( _grenades <= 0 || _grenadeReady.Relative > 0 || _sinceGrenadeThink < 1.5f ) return false;
		_sinceGrenadeThink = Game.Random.Float( -0.5f, 0.3f );
		if ( !_target.IsValid() || _sinceSeen > 4f || _lastSeen == Vector3.Zero ) return false;

		var hiding = _sinceSeen > 1f;
		var landing = hiding ? _lastSeen : _target.WorldPosition;
		var dist = landing.WithZ( 0 ).Distance( WorldPosition.WithZ( 0 ) );
		if ( dist < 320f || dist > 1300f || MathF.Abs( landing.z - WorldPosition.z ) > 160f ) return false;

		var side = Allegiance ?? "";
		_sideThrew ??= new();
		if ( _sideThrew.TryGetValue( side, out var at ) && Time.Now - at < SideGap ) return false;
		if ( Scene.GetAllComponents<SwrpGrenade>().Count( g => g.Allegiance == side ) >= SideInFlight ) return false;

		var chance = GrenadeChance;
		if ( hiding ) chance += 0.25f;
		if ( HostilesNear( landing, 260f ) >= 2 ) chance += 0.2f;
		if ( !hiding && dist < 520f ) chance -= 0.15f;
		if ( Game.Random.Float() > chance )
		{
			_grenadeReady = Game.Random.Float( 3f, 6f );
			return false;
		}

		// a little off target, and never near a friend (or itself)
		landing += Vector3.Random.WithZ( 0 ).Normal * Game.Random.Float( 20f, 80f );
		if ( FriendNear( landing, 340f ) ) { _grenadeReady = 2f; return false; }

		var from = WorldPosition + Vector3.Up * 62f * WorldScale.z + WorldRotation.Forward * 18f + WorldRotation.Right * 8f;
		if ( !Arc( from, landing + Vector3.Up * 6f, out var velocity ) ) { _grenadeReady = 2.5f; return false; }

		_grenades--;
		_grenadeReady = Game.Random.Float( 16f, 26f );
		_sideThrew[side] = Time.Now;
		StopMoving();
		Crouched = false;
		AimAt = landing;
		ThrowMotion();
		Voice( "grenade" );
		SwrpGrenade.Throw( Scene, from, velocity, side, DisplayName, null, this );
		return true;
	}

	/// <summary> A throw that lands at <paramref name="to"/> with nothing in the way: a flat arc first, then a lob. </summary>
	bool Arc( Vector3 from, Vector3 to, out Vector3 velocity )
	{
		var g = Scene.PhysicsWorld.Gravity;
		var flat = from.WithZ( 0 ).Distance( to.WithZ( 0 ) );
		foreach ( var t in new[] { Math.Clamp( flat / 900f, 0.5f, 1.2f ), Math.Clamp( flat / 620f, 0.8f, 1.7f ) } )
		{
			velocity = ( to - from ) / t - g * ( 0.5f * t );
			var up = g.z < 0f ? Math.Clamp( velocity.z / -g.z, 0f, t ) : 0f;
			var apex = from + velocity * up + g * ( 0.5f * up * up );
			if ( !Blocked( from, apex ) && !Blocked( apex, to + Vector3.Up * 14f ) ) return true;
		}
		velocity = default;
		return false;
	}

	bool Blocked( Vector3 a, Vector3 b ) => Scene.Trace.Ray( a, b ).IgnoreGameObjectHierarchy( GameObject )
		.WithoutTags( "player", "npc", "ragdoll", "trigger", "grenade", "worlditem" ).Run().Hit;

	int HostilesNear( Vector3 point, float radius )
	{
		var n = 0;
		foreach ( var p in SwrpPlayers.Valid )
			if ( p.CharacterId is not null && !p.Noclip && p.WorldPosition.Distance( point ) < radius && SwrpNpcDirector.IsHostile( this, p.GameObject ) ) n++;
		foreach ( var o in Scene.GetAllComponents<SwrpNpc>() )
			if ( !o.IsDead && o.WorldPosition.Distance( point ) < radius && SwrpNpcDirector.IsHostile( this, o.GameObject ) ) n++;
		return n;
	}

	bool FriendNear( Vector3 point, float radius )
	{
		if ( WorldPosition.Distance( point ) < radius ) return true;
		foreach ( var p in SwrpPlayers.Valid )
			if ( p.CharacterId is not null && p.WorldPosition.Distance( point ) < radius && AllegianceRegistry.AreFriendly( Allegiance, p.Allegiance ) ) return true;
		foreach ( var o in Scene.GetAllComponents<SwrpNpc>() )
			if ( o != this && !o.IsDead && o.WorldPosition.Distance( point ) < radius && AllegianceRegistry.AreFriendly( Allegiance, o.Allegiance ) ) return true;
		return false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ThrowMotion()
	{
		if ( _body.IsValid() ) _body.Set( "b_attack", true );
	}

	/// <summary>
	/// A live detonator close by: most NPCs (three in four, decided once per detonator) notice it after a moment and run
	/// clear of it; the rest are caught out.
	/// </summary>
	bool DodgeGrenades()
	{
		foreach ( var g in Scene.GetAllComponents<SwrpGrenade>() )
		{
			if ( g.Age < 0.4f || g.Age > SwrpGrenade.Fuse - 0.15f ) continue;
			var d = g.WorldPosition.Distance( WorldPosition );
			if ( d > SwrpGrenade.Radius * 0.8f ) continue;
			if ( ( ( g.GameObject.Id.GetHashCode() ^ GameObject.Id.GetHashCode() ) & 3 ) == 0 ) continue;

			var away = ( WorldPosition - g.WorldPosition ).WithZ( 0 );
			away = away.Length < 1f ? Vector3.Random.WithZ( 0 ).Normal : away.Normal;
			var spot = WorldPosition + away * 380f;
			if ( Scene.NavMesh.GetRandomPoint( spot, 120f ) is { } p ) spot = p;
			MoveTo( spot, Dash );
			_inCover = true;
			_coverUntil = 1.8f;
			return true;
		}
		return false;
	}

	bool TakeCover()
	{
		if ( !_target.IsValid() ) return false;
		_sinceCover = 0;
		var enemyEye = SwrpNpcDirector.AimPoint( _target ) + Vector3.Up * 10f;
		Vector3? best = null;
		var bestDist = float.MaxValue;
		for ( var i = 0; i < 10; i++ )
		{
			if ( Scene.NavMesh.GetRandomPoint( WorldPosition, 420f ) is not { } p ) continue;
			// cover = the enemy can't see a crouched droid there
			var tr = Scene.Trace.Ray( enemyEye, p + Vector3.Up * 40f ).IgnoreGameObjectHierarchy( _target ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "ragdoll" ).Run();
			if ( !tr.Hit || tr.EndPosition.Distance( p + Vector3.Up * 40f ) < 24f ) continue;
			var d = p.Distance( WorldPosition );
			if ( d < bestDist ) { bestDist = d; best = p; }
		}
		if ( best is not { } spot ) return false;
		MoveTo( spot, Dash );
		_inCover = true;
		_coverUntil = 2.5f + bestDist / Dash + Game.Random.Float( 0f, 1.5f );
		return true;
	}

	void Deploy( bool on )
	{
		if ( Deployed == on ) return;
		Deployed = on;
		_sinceDeployChange = 0;
		if ( on ) StopMoving();
		PlayAt( on ? SwrpAudio.DoorLock : SwrpAudio.DoorUnlock );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayAt( string sound ) => Sound.Play( sound, WorldPosition + Vector3.Up * 30f );

	[ConVar( "swrp_npc_stride", Help = "NPC walk/run animation speed while moving (1 = as authored)", Saved = true )]
	public static float StrideRate { get; set; } = 1.4f;

	[ConVar( "swrp_npc_droid_stride", Help = "Dev: the B1 run clip's ground speed (u/s); faster than this it plays quicker" )]
	public static float DroidRunStride { get; set; } = 160f;

	[ConVar( "swrp_npc_foot_debug", Help = "Dev: log how fast the planted foot slides compared with the NPC's speed" )]
	public static bool FootDebug { get; set; }
	Vector3 _lastFootL, _lastFootR;
	float _minFoot = 9999f, _sumSpeed; int _footN; RealTimeSince _sinceFootLog;

	void FootSlideProbe( float speed )
	{
		if ( _body.GetBoneObject( "ankle_L" ) is not { } l || _body.GetBoneObject( "ankle_R" ) is not { } r ) { _body.CreateBoneObjects = true; return; }
		var dt = MathF.Max( Time.Delta, 0.001f );
		var vl = ( l.WorldPosition - _lastFootL ).WithZ( 0 ).Length / dt;
		var vr = ( r.WorldPosition - _lastFootR ).WithZ( 0 ).Length / dt;
		_lastFootL = l.WorldPosition; _lastFootR = r.WorldPosition;
		_minFoot = MathF.Min( _minFoot, MathF.Min( vl, vr ) ); _sumSpeed += speed; _footN++;
		if ( _sinceFootLog < 1f ) return;
		Log.Info( $"[SWRP] npc feet: body {_sumSpeed / _footN:0} u/s, slowest foot {_minFoot:0} u/s, playback {_body.PlaybackRate:0.00}" );
		_sinceFootLog = 0; _minFoot = 9999f; _sumSpeed = 0; _footN = 0;
	}

	/// <summary> NPC clones walk with the Squad movement set (1) or the clone drill set (0). </summary>
	[ConVar( "swrp_npc_squad_walk", Help = "NPC clones: 1 = Squad movement set, 0 = the clone drill walk (applies to newly spawned NPCs)", Saved = true )]
	public static bool SquadWalk { get; set; } = true;

	Model _gmod;      // a clone on GMod's rifle set (stock mode)

	/// <summary> Dev, host: send every NPC walking to a point at a speed (for filming locomotion). </summary>
	[ConCmd( "swrp_npc_walkto", Help = "swrp_npc_walkto <x> <y> <z> [speed] - dev, host: every NPC walks to that point" )]
	public static void DevWalkTo( float x, float y, float z, float speed = 84f )
	{
		if ( !Networking.IsHost ) return;
		foreach ( var n in Game.ActiveScene?.GetAllComponents<SwrpNpc>() ?? Enumerable.Empty<SwrpNpc>() ) n.MoveTo( new Vector3( x, y, z ), speed );
	}

	// the shared speed table's tiers for riflemen (clones, B1s); other kinds keep their own walk and run
	bool Tiered => Def.Style == NpcStyle.Rifleman;
	float Patrol => Tiered ? SwrpSpeeds.U( SwrpSpeeds.NpcPatrol ) * Def.Scale : Def.WalkSpeed;
	float Advance => Tiered ? SwrpSpeeds.U( SwrpSpeeds.NpcAdvance ) * Def.Scale : Def.RunSpeed;
	float Dash => Tiered ? SwrpSpeeds.U( SwrpSpeeds.NpcDash ) * Def.Scale : Def.RunSpeed;

	/// <summary> Dev: seconds spent moving at each tier (patrol, advance, dash) and still, for the gait-in-a-fight numbers. </summary>
	public static float[] GaitSeconds { get; } = new float[4];

	void MoveTo( Vector3 point, float speed )
	{
		if ( !_agent.IsValid() ) return;
		if ( Crouched ) Crouched = false;
		_agent.MaxSpeed = speed;
		// a dash gets going and stops hard; a patrol eases in (per tier, 2026-10-21)
		if ( Tiered ) _agent.Acceleration = speed >= Dash * 0.95f ? 1100f : speed <= Patrol * 1.05f ? 380f : 700f;
		_agent.MoveTo( point );
	}

	void StopMoving()
	{
		if ( _agent.IsValid() ) _agent.Stop();
	}

	void OpenDoorsAhead()
	{
		if ( _velocity.Length < 20f ) return;
		foreach ( var d in SwrpNpcDirector.Doors( Scene ) )
		{
			if ( d.IsOpen || d.Locked || d.ConsoleOnly || d.HeldShut ) continue;
			var to = d.WorldPosition - WorldPosition;
			if ( MathF.Abs( to.z ) > 110f || to.WithZ( 0 ).Length > 110f ) continue;
			d.SetOpen( true );
		}
	}

	// ----- host: damage -----

	/// <summary> Host: a bolt hit this NPC. Returns true if it destroyed it. </summary>
	public bool TakeHit( float amount, SwrpPlayer shooter, string shooterName, Vector3 from, string weapon, bool head, bool stun, float stunSeconds )
	{
		if ( !Networking.IsHost || IsDead ) return false;
		var def = Def;
		_sinceHit = 0;
		Threat();

		// shot from out of sight: turn to face whoever did it
		if ( !_target.IsValid() || _sinceSeen > 1f )
		{
			_lastSeen = from;
			_sinceSeen = 2f;
		}

		if ( Def.Shield > 0 && Deployed && Shield > 0 )
		{
			Shield = MathF.Max( 0, Shield - ( stun ? 40f : amount ) );
			ShieldHit( from );
			return false;
		}

		Flinch( WorldPosition - from, stun ? 1f : Math.Clamp( amount / 45f, 0.3f, 1f ), head );
		if ( Game.Random.Float() < 0.35f ) Voice( Vector3.Dot( WorldRotation.Forward, ( from - WorldPosition ).WithZ( 0 ).Normal ) < -0.3f ? "behind" : "hit" );

		if ( stun )
		{
			if ( def.StunScale <= 0f ) return false;
			Stunned = true;
			_stunUntil = stunSeconds * def.StunScale;
			StopMoving();
			return false;
		}

		var soaked = MathF.Min( Armor, amount * 0.6f );
		Armor -= soaked;
		Health = MathF.Max( 0, Health - ( amount - soaked ) );
		if ( Health > 0 ) return false;

		IsDead = true;
		Voice( "death", urgent: true );
		MateDown();
		Stunned = false;
		Deployed = false;
		_sinceDeath = 0;
		StopMoving();
		if ( _agent.IsValid() ) _agent.Enabled = false;
		foreach ( var c in Components.GetAll<Collider>() ) c.Enabled = false;

		SwrpCombat.AnnounceKill( shooter, null, weapon, head, DisplayName, shooterName );
		return true;
	}

	/// <summary> Everyone sees the body flinch away from the shot (Code/Fx/HitReact). </summary>
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	void Flinch( Vector3 dir, float strength, bool head ) => HitReact.Play( _body, dir, strength, head );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShieldHit( Vector3 from )
	{
		_shield?.Hit( from );
		var at = WorldPosition + Vector3.Up * 36f + ( from - WorldPosition ).WithZ( 0 ).Normal * 46f;
		SparkFx.Spawn( Scene, at, Vector3.Random * 120f, new Color( 0.4f, 0.7f, 1f ) );
		Sound.Play( SwrpAudio.BlasterImpact, at );
	}

	/// <summary> Host: removed by a Game Master clean-up (no kill feed). </summary>
	public void Despawn() => GameObject.Destroy();
}
