using System;
using System.Threading.Tasks;

namespace Swrp.Flight;

/// <summary>
/// Weapons and damage. A seated trooper's machine asks the host to fire (with where it sees the craft and what the crosshair
/// is on); the host checks the seat, rate and ammo, traces each bolt and applies the damage, and every screen draws the bolts.
/// Damage tears plating loose (each fifth of the health lost: sparks and smoke where the hit landed), a wreck loses power,
/// burns, falls and blows up, killing whoever is still aboard.
/// </summary>
public sealed partial class SwrpFlightShip
{
	// ----- firing (local) -----------------------------------------------------------------------------------------------

	readonly Dictionary<string, RealTimeSince> _localShot = new();

	void LocalWeapons( int seat )
	{
		if ( Wrecked ) return;
		var ids = Def.Seats[seat].Weapons;
		for ( var slot = 0; slot < ids.Length && slot < 2; slot++ )
		{
			if ( !Input.Down( slot == 0 ? "Attack1" : "Attack2" ) ) continue;
			var w = Def.Weapon( ids[slot] );
			if ( w is null ) continue;
			if ( _localShot.TryGetValue( w.Id, out var since ) && since < w.FireDelay ) continue;
			if ( w.Ammo > 0 && AmmoOf( w ) <= 0 ) continue;
			_localShot[w.Id] = 0;
			RequestFire( seat, w.Id, WorldPosition, WorldRotation, AimPoint( w.Range ), w.Kind == WeaponKind.Missile ? LockedId : Guid.Empty );
		}
	}

	/// <summary> Local: what the crosshair (the middle of the screen) is on. </summary>
	Vector3 AimPoint( float range )
	{
		var cam = Scene.Camera;
		var from = cam.WorldPosition;
		var dir = cam.WorldRotation.Forward;
		var tr = Scene.Trace.Ray( from + dir * 40f, from + dir * range ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "debris" ).Run();
		return tr.Hit ? tr.HitPosition : tr.EndPosition;
	}

	[ConVar( "swrp_flight_debug", Help = "1 = log where each craft weapon fires from and along (ship space)" )]
	public static bool Debug { get; set; }

	// ----- firing (host) ------------------------------------------------------------------------------------------------

	readonly Dictionary<string, RealTimeSince> _hostShot = new();
	readonly Dictionary<string, int> _turn = new();

	[Rpc.Host]
	void RequestFire( int seat, string weaponId, Vector3 shipPos, Rotation shipRot, Vector3 aimPoint, Guid target )
	{
		if ( Def is null || Wrecked ) return;
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		if ( !p.IsValid() || SeatOf( p ) != seat ) return;
		if ( !Def.Seats[seat].Weapons.Contains( weaponId ) || Def.Weapon( weaponId ) is not { } w ) return;
		if ( _hostShot.TryGetValue( w.Id, out var since ) && since < w.FireDelay * 0.7f ) return;
		if ( w.Ammo > 0 )
		{
			var left = AmmoOf( w );
			if ( left <= 0 ) return;
			SetAmmo( w, left - 1 );
		}
		_hostShot[w.Id] = 0;
		HostFire( p, w, shipPos, shipRot, aimPoint, target );
	}

	/// <summary> Host, dev: fire one of this craft's weapons straight ahead with nobody in the seat (swrp_ship_fire). </summary>
	public void HostTestFire( WeaponDef w ) => HostFire( null, w, WorldPosition, WorldRotation, WorldPosition + WorldRotation.Forward * w.Range * 0.5f + WorldRotation.Up * 110f );

	void HostFire( SwrpPlayer p, WeaponDef w, Vector3 shipPos, Rotation shipRot, Vector3 aimPoint, Guid target = default )
	{
		// trust where the shooter sees the craft (a pilot's machine is ahead of ours), within reason
		if ( shipPos.Distance( WorldPosition ) > 1000f ) { shipPos = WorldPosition; shipRot = WorldRotation; }
		var xf = new Transform( shipPos, shipRot );

		IEnumerable<Vector3> muzzles;
		if ( w.Linked ) muzzles = w.Muzzles;
		else
		{
			_turn.TryGetValue( w.Id, out var t );
			_turn[w.Id] = t + 1;
			muzzles = new[] { w.Muzzles[t % w.Muzzles.Length] };
		}

		foreach ( var m in muzzles )
		{
			var from = xf.PointToWorld( m );
			var center = w.Arc > 0f ? shipRot * w.ArcCenter : shipRot.Forward;
			var dir = Clamp( ( aimPoint - from ).Normal, center, w.Arc > 0f ? w.Arc : w.Gimbal );
			dir = Spread( dir, w.Spread );
			if ( Debug ) Log.Info( $"[SWRP] flight fire {w.Id}: from {WorldTransform.PointToLocal( from ):0} (ship space) along {( WorldRotation.Inverse * dir ):0.00}" );
			if ( w.Kind == WeaponKind.Missile ) FlightMissile.HostLaunch( Scene, this, p, w, from, dir, Velocity, target );
			else HostLaser( p, w, from, dir );
		}
	}

	void HostLaser( SwrpPlayer shooter, WeaponDef w, Vector3 from, Vector3 dir )
	{
		var trace = Scene.Trace.Ray( from, from + dir * w.Range ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "debris" ).UseHitboxes();
		for ( var i = 0; i < ( Def?.Seats.Length ?? 0 ); i++ )
			if ( PlayerIn( i ) is { } rider && rider.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( rider.GameObject );
		var tr = trace.Run();
		var flesh = tr.Hit && HostHit( tr, w.Damage, w.ShipDamage, shooter, w.Name, 0f );
		ShowLaser( w.Id, from, tr.EndPosition, tr.Hit ? tr.Normal : -dir, tr.Hit, flesh );
	}

	/// <summary> Host: a bolt hit something. Troopers, NPCs and craft take damage (friendly fire rules apply). True if it hit a body. </summary>
	public static bool HostHit( SceneTraceResult tr, float damage, float shipDamage, SwrpPlayer shooter, string weapon, float carve )
	{
		var go = tr.GameObject;
		if ( go?.Components.GetInAncestorsOrSelf<SwrpVitals>() is { } vitals )
		{
			if ( vitals.IsDead ) return true;
			var victim = vitals.GetComponent<SwrpPlayer>();
			if ( victim.IsValid() && shooter.IsValid() && victim != shooter && !SwrpCombat.FriendlyFire && SwrpCombat.SameSide( shooter, victim ) ) return true;
			vitals.TakeDamage( damage, shooter, tr.StartPosition, weapon, false );
			return true;
		}
		if ( go?.Components.GetInAncestorsOrSelf<SwrpNpc>() is { } npc )
		{
			if ( npc.IsDead ) return true;
			if ( shooter.IsValid() && !SwrpCombat.FriendlyFire && AllegianceRegistry.AreFriendly( shooter.Allegiance, npc.Allegiance ) ) return true;
			npc.TakeHit( damage, shooter, null, tr.StartPosition, weapon, false, false, 0f );
			return true;
		}
		if ( go?.Components.GetInAncestorsOrSelf<SwrpFlightShip>() is { } ship )
		{
			ship.HostDamage( shipDamage, tr.HitPosition, shooter, weapon );
			return false;
		}
		return false;
	}

	/// <summary> Host: a blast (missile, torpedo, wreck) hurts everything in the open within its radius. </summary>
	public static void HostBlast( Scene scene, Vector3 pos, float radius, float damage, float shipDamage, float carve, SwrpPlayer shooter, string weapon, GameObject ignore = null )
	{
		float Falloff( Vector3 point, GameObject who )
		{
			var d = point.Distance( pos );
			if ( d >= radius ) return 0f;
			var tr = scene.Trace.Ray( pos, point ).IgnoreGameObjectHierarchy( who ).WithoutTags( "player", "npc", "ragdoll", "trigger", "debris", "flightship" );
			if ( ignore is not null ) tr = tr.IgnoreGameObjectHierarchy( ignore );
			if ( tr.Run().Hit ) return 0f;
			var k = 1f - d / radius;
			return MathF.Max( 0.15f, k * k );
		}

		foreach ( var v in scene.GetAllComponents<SwrpVitals>().ToList() )
		{
			if ( v.IsDead || damage <= 0f ) continue;
			var k = Falloff( v.WorldPosition + Vector3.Up * 32f, v.GameObject );
			if ( k <= 0f ) continue;
			var victim = v.GetComponent<SwrpPlayer>();
			if ( victim.IsValid() && victim.InVehicle ) continue;                                     // the craft takes it for them
			if ( victim.IsValid() && shooter.IsValid() && victim != shooter && !SwrpCombat.FriendlyFire && SwrpCombat.SameSide( shooter, victim ) ) continue;
			v.TakeDamage( damage * k, shooter, pos, weapon, false );
		}
		foreach ( var n in scene.GetAllComponents<SwrpNpc>().ToList() )
		{
			if ( n.IsDead || damage <= 0f ) continue;
			var k = Falloff( n.WorldPosition + Vector3.Up * 32f, n.GameObject );
			if ( k <= 0f ) continue;
			if ( shooter.IsValid() && !SwrpCombat.FriendlyFire && AllegianceRegistry.AreFriendly( shooter.Allegiance, n.Allegiance ) ) continue;
			n.TakeHit( damage * k, shooter, null, pos, weapon, false, false, 0f );
		}
		foreach ( var s in All.ToList() )
		{
			if ( s.GameObject == ignore || s.Wrecked ) continue;
			var d = s.WorldPosition.Distance( pos ) - 250f;              // from the hull, roughly
			if ( d >= radius ) continue;
			var k = 1f - Math.Clamp( d / radius, 0f, 1f );
			s.HostDamage( shipDamage * k, pos, shooter, weapon );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowLaser( string weaponId, Vector3 from, Vector3 to, Vector3 normal, bool hit, bool flesh )
	{
		var w = Def?.Weapon( weaponId );
		if ( w is null ) return;
		// the hit bursts when the bolt gets there, scaled to the cannon (FlightBolt)
		FlightBolt.Spawn( Scene, from, to, w.BoltColor, w.Damage >= 40f ? 300f : 200f, w.Damage >= 40f ? 6f : 4f, hit ? normal : null, !flesh );
		SwrpFx.MuzzleFlash( Scene, from, w.BoltColor );
		if ( w.Sound is not null ) Sound.Play( w.Sound, from );
	}

	static Vector3 Clamp( Vector3 dir, Vector3 center, float maxDegrees )
	{
		var cos = Vector3.Dot( dir, center );
		var limit = MathF.Cos( maxDegrees * MathF.PI / 180f );
		if ( cos >= limit ) return dir;
		var side = ( dir - center * cos ).Normal;
		if ( side.LengthSquared < 0.5f ) return center;
		var a = maxDegrees * MathF.PI / 180f;
		return ( center * MathF.Cos( a ) + side * MathF.Sin( a ) ).Normal;
	}

	static Vector3 Spread( Vector3 dir, float degrees )
	{
		if ( degrees <= 0f ) return dir;
		var r = Rotation.LookAt( dir );
		var a = Game.Random.Float( 0f, MathF.PI * 2f );
		var m = Game.Random.Float( 0f, degrees );
		return ( r * Rotation.From( new Angles( MathF.Sin( a ) * m, MathF.Cos( a ) * m, 0f ) ) ).Forward;
	}

	// ----- ammo ---------------------------------------------------------------------------------------------------------

	readonly Dictionary<string, RealTimeSince> _reload = new();

	public int AmmoOf( WeaponDef w )
	{
		if ( w.Ammo <= 0 ) return int.MaxValue;
		foreach ( var part in ( AmmoList ?? "" ).Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var kv = part.Split( ':' );
			if ( kv.Length == 2 && kv[0] == w.Id && int.TryParse( kv[1], out var n ) ) return n;
		}
		return w.Ammo;
	}

	void SetAmmo( WeaponDef w, int n )
	{
		var d = Def.Weapons.Where( x => x.Ammo > 0 ).ToDictionary( x => x.Id, AmmoOf );
		d[w.Id] = Math.Clamp( n, 0, w.Ammo );
		AmmoList = string.Join( ";", d.Select( kv => $"{kv.Key}:{kv.Value}" ) );
		if ( n < w.Ammo && !_reload.ContainsKey( w.Id ) ) _reload[w.Id] = 0;
	}

	/// <summary> Host: launchers load a round back every so often. </summary>
	void HostAmmo()
	{
		foreach ( var w in Def.Weapons )
		{
			if ( w.Ammo <= 0 || !_reload.TryGetValue( w.Id, out var since ) || since < w.ReloadSeconds ) continue;
			var n = AmmoOf( w );
			if ( n >= w.Ammo ) { _reload.Remove( w.Id ); continue; }
			_reload[w.Id] = 0;
			SetAmmo( w, n + 1 );
		}
	}

	// ----- damage -------------------------------------------------------------------------------------------------------

	SwrpPlayer _lastAttacker;
	string _lastWeapon;
	RealTimeSince _sinceHitFx = 10f;

	/// <summary> Host: the craft takes damage at a world point. </summary>
	public void HostDamage( float amount, Vector3 at, SwrpPlayer attacker, string weapon )
	{
		if ( !Networking.IsHost || Wrecked || Def is null || amount <= 0f ) return;
		var before = Health;
		Health = MathF.Max( 0f, Health - amount );
		if ( attacker.IsValid() ) { _lastAttacker = attacker; _lastWeapon = weapon; }
		if ( _sinceHitFx > 0.12f )
		{
			_sinceHitFx = 0;
			HitFx( at );
		}
		// each fifth of the hull lost tears a chunk off where the hit landed
		var a = Stage( before );
		var b = Stage( Health );
		for ( var s = a + 1; s <= b && s < 5; s++ ) ShedPart( WorldTransform.PointToLocal( at ), Game.Random.Int( 0, 99999 ) );
		if ( Health <= 0f ) HostWreck( attacker );
	}

	int Stage( float health ) => (int)( ( 1f - health / Def.Health ) * 5f );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void HitFx( Vector3 at )
	{
		Sound.Play( "sounds/flight/hull_hit.sound", at );
		if ( SwrpFx.Near( Scene, at, 5000f ) )
			for ( var i = 0; i < 4 && SparkFx.Live < SwrpFx.MaxSparks; i++ )
				SparkFx.Spawn( Scene, at, ( Vector3.Random + Vector3.Up * 0.4f ).Normal * Game.Random.Float( 120f, 320f ) + Velocity, new Color( 1f, 0.75f, 0.4f ) );
	}

	// ----- damage you can see ------------------------------------------------------------------------------------------

	/// <summary> Every screen: a heavy hit tears plating loose: a burst of sparks, a puff of smoke and a glow where it landed. </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShedPart( Vector3 local, int seed )
	{
		var at = WorldTransform.PointToWorld( local );
		if ( !SwrpFx.Near( Scene, at, 6000f ) ) return;
		var rng = new Random( seed );
		for ( var i = 0; i < 10 && SparkFx.Live < SwrpFx.MaxSparks; i++ )
			SparkFx.Spawn( Scene, at, Velocity + new Vector3( (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() ) * 520f, new Color( 1f, 0.75f, 0.4f ) );
		SwrpFx.Glow( Scene, at, new Color( 1f, 0.55f, 0.2f ) * 2.5f, 40f, 160f, 0.35f );
		var go = Scene.CreateObject();
		go.Name = "ship_smoke";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = at;
		go.Components.Create<BlastSmoke>();
	}

	// ----- wrecks -------------------------------------------------------------------------------------------------------

	RealTimeSince _sinceWreck;
	bool _crashed;
	bool _blown;

	void HostWreck( SwrpPlayer attacker )
	{
		if ( Wrecked ) return;
		Wrecked = true;
		_sinceWreck = 0;
		_parked = false;
		if ( !IsProxy ) EngineOn = false;
		WreckFx();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void WreckFx()
	{
		Sound.Play( "sounds/flight/alarm.sound", WorldPosition );
		Sound.Play( "sounds/flight/explode_small.sound", WorldPosition );
		SwrpFx.Glow( Scene, WorldPosition, new Color( 1f, 0.55f, 0.2f ) * 3f, 60f, 320f, 0.5f );
	}

	/// <summary> Host, every frame: a wreck blows apart when it hits something, lands, or has fallen long enough. </summary>
	void HostWreck()
	{
		if ( !Wrecked || _blown ) return;
		if ( !( _crashed || _sinceWreck > 8f || _sinceWreck > 1.2f && Landed ) ) return;
		_blown = true;

		var pos = WorldPosition + WorldRotation.Up * 100f;
		var killer = _lastAttacker;
		var weapon = _lastWeapon ?? "a crash";
		for ( var i = 0; i < Def.Seats.Length; i++ )
		{
			var p = PlayerIn( i );
			if ( Occupant( i ) == Guid.Empty ) continue;
			HostLeave( i, false );
			if ( p.IsValid() && p.GetComponent<SwrpVitals>() is { IsDead: false } v ) v.TakeDamage( 1000f, killer, pos, weapon, false );
		}
		HostBlast( Scene, pos, 520f, 160f, 300f, 160f, killer, weapon, GameObject );
		BlowApart( pos );
		SwrpFlightDirector.Lost( this );
		DestroySoon();
	}

	async void DestroySoon()
	{
		await Task.DelaySeconds( 0.25f );
		if ( GameObject.IsValid() ) GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void BlowApart( Vector3 pos )
	{
		var floor = Scene.Trace.Ray( pos, pos + Vector3.Down * 400f ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc", "trigger", "debris" ).Run();
		SwrpFx.Explosion( Scene, pos, floor.Hit ? floor.HitPosition : Vector3.Zero, floor.Hit ? floor.Normal : Vector3.Up, 520f );
		Sound.Play( "sounds/flight/explode_big.sound", pos );
		if ( _renderer.IsValid() ) _renderer.Enabled = false;

		// the burnt-out hull, flung up and tumbling, smouldering until it's cleared away
		if ( !_renderer.IsValid() || _renderer.Model is null ) return;
		var go = new GameObject( true, "Wreck" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( "debris" );
		go.WorldTransform = WorldTransform;
		var mr = go.Components.Create<ModelRenderer>();
		mr.Model = _renderer.Model;
		mr.Tint = new Color( 0.22f, 0.2f, 0.19f );
		foreach ( var (center, size) in Def.Boxes.Take( 3 ) )
		{
			var box = go.Components.Create<BoxCollider>();
			box.Center = center;
			box.Scale = size;
		}
		var rb = go.Components.Create<Rigidbody>();
		rb.MassOverride = Def.Mass;
		rb.Velocity = Velocity * 0.5f + Vector3.Up * 420f;
		rb.AngularVelocity = Vector3.Random * 1.5f;
		go.Components.Create<WreckPiece>();
	}

	// ----- burning, engines ---------------------------------------------------------------------------------------------

	RealTimeSince _sinceSmoke;
	SoundHandle _engine;
	RealTimeSince _engineAge;
	bool? _shownEngineOn;
	bool _washOnGround = true;
	GameObject _washGo;
	Downwash _wash;

	bool Gunship => Def.Model != FlightModel.Fighter;
	// the LAAT's own engine (Star Wars Battlefront II's, local only: Assets/sounds/flight/laat/LOCAL_ONLY.txt); fighters keep theirs
	string EngineLoop => Gunship ? "sounds/flight/laat/engine_loop.sound" : "sounds/flight/engine_fighter.sound";
	float EngineLoopLength => Gunship ? 9.31f : 3.98f;

	/// <summary> Every screen: engine sound, damage sparks and smoke, a wreck on fire, chunks coming off. </summary>
	void Effects()
	{
		if ( Def is null || _blown ) return;
		EngineFlares();
		Contrails();

		// starting and stopping, heard by everyone near (EngineOn is synced; the pilot's own screen used to be the only one)
		if ( _shownEngineOn != EngineOn )
		{
			if ( _shownEngineOn is not null && SwrpFx.Near( Scene, WorldPosition, 9000f ) )
				Sound.Play( EngineOn ? ( Gunship ? "sounds/flight/laat/engine_start.sound" : "sounds/flight/engine_start.sound" )
					: Gunship ? "sounds/flight/laat/engine_stop.sound" : "sounds/flight/engine_stop.sound", WorldPosition );
			_shownEngineOn = EngineOn;
		}

		// lifting off and setting down (gunships)
		if ( Gunship && Spool > 0.5f )
		{
			var alt = AltitudeCached;
			if ( _washOnGround && alt > 70f ) { _washOnGround = false; Sound.Play( "sounds/flight/laat/takeoff.sound", WorldPosition ); }
			else if ( !_washOnGround && alt < 35f ) { _washOnGround = true; Sound.Play( "sounds/flight/laat/landing.sound", WorldPosition ); }
		}

		// the repulsor wash on the surface underneath (the ARC-170 kicks it up too, Angelo 2026-10-21; the wash scales its own
		// puffs by height and power, so the fighter's is the same gust and dust)
		{
			if ( !_washGo.IsValid() )
			{
				_washGo = new GameObject( GameObject, true, "Downwash" );
				_washGo.NetworkMode = NetworkMode.Never;
				_washGo.Flags |= GameObjectFlags.NotSaved;
				_wash = _washGo.Components.Create<Downwash>();
			}
			_wash.Power = Wrecked ? 0f : Spool;
		}

		// the engine: a loop, each pass overlapping the last by half a second so there's no gap, louder and higher with power and speed
		var audible = Spool > 0.02f && SwrpFx.Near( Scene, WorldPosition, 9000f );
		if ( audible )
		{
			if ( _engine is null || !_engine.IsPlaying || _engineAge > EngineLoopLength - 0.5f )
			{
				_engineAge = 0;
				if ( _engine is not null && _engine.IsPlaying ) _engine.Stop( 0.5f );
				_engine = Sound.Play( EngineLoop, WorldPosition );
			}
			if ( _engine is not null )
			{
				var speed = Math.Clamp( Velocity.Length / Def.MaxSpeed, 0f, 1.5f );
				_engine.Position = WorldPosition;
				_engine.Volume = Spool * ( 0.45f + 0.4f * speed ) * ( Boosting ? 1.2f : 1f );
				_engine.Pitch = 0.75f + 0.25f * Spool + 0.3f * speed;
			}
		}
		else if ( _engine is not null )
		{
			if ( _engine.IsPlaying ) _engine.Stop( 0.4f );
			_engine = null;
		}

		// damaged: sparks; badly: smoke; wrecked: fire
		var hurt = 1f - Health / Def.Health;
		if ( !Wrecked && hurt < 0.4f ) return;
		var every = Wrecked ? 0.12f : hurt > 0.7f ? 0.35f : 0.8f;
		if ( _sinceSmoke < every || !SwrpFx.Near( Scene, WorldPosition, 7000f ) ) return;
		_sinceSmoke = 0;
		var spot = WorldTransform.PointToWorld( new Vector3( Game.Random.Float( -200f, 120f ), Game.Random.Float( -60f, 60f ), Game.Random.Float( 90f, 150f ) ) );
		if ( Wrecked || hurt > 0.7f )
			SwrpFx.Glow( Scene, spot, new Color( 1f, 0.5f, 0.15f ) * 2.5f, 30f, 110f, 0.4f );
		if ( Wrecked || hurt > 0.55f )
		{
			var go = Scene.CreateObject();
			go.Name = "ship_smoke";
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = spot;
			go.Components.Create<BlastSmoke>();
		}
		else if ( SparkFx.Live < SwrpFx.MaxSparks )
			SparkFx.Spawn( Scene, spot, Vector3.Random * 200f + Velocity, new Color( 1f, 0.8f, 0.45f ) );
	}

	protected override void OnDestroy()
	{
		if ( _engine is not null && _engine.IsPlaying ) _engine.Stop( 0.2f );
		// the local moving parts (not networked) go with the craft
		_gearGo?.Destroy();
		_canopyGo?.Destroy();
		_washGo?.Destroy();
		foreach ( var tr in _trails ) if ( tr.Line.IsValid() ) tr.Line.GameObject.Destroy();      // (the contrails aren't children)
		foreach ( var go in _extraGo ) go?.Destroy();
		foreach ( var p in _riding.Keys.ToList() )
			if ( p.IsValid() && SeatOf( p ) >= 0 ) RestoreRider( p, Vector3.Zero );
	}
}

/// <summary> A big chunk of a destroyed craft: tumbles, settles, smoulders, and is cleared away after a while. </summary>
public sealed class WreckPiece : Component
{
	public const float Life = 25f;
	TimeSince _age;
	RealTimeSince _sinceSmoke;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		if ( _age > Life ) { GameObject.Destroy(); return; }
		if ( _age < 8f && _sinceSmoke > 0.6f && SwrpFx.Near( Scene, WorldPosition, 6000f ) )
		{
			_sinceSmoke = 0;
			SwrpFx.Glow( Scene, WorldPosition + Vector3.Up * 20f, new Color( 1f, 0.5f, 0.15f ) * 2f, 20f, 70f, 0.5f );
		}
	}
}
