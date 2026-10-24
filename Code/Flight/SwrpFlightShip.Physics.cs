using System;

namespace Swrp.Flight;

/// <summary>
/// The flight model, run by whoever owns the craft (its pilot's machine, or the host when nobody flies it). Arcade-leaning,
/// after WAC's ideas: engines spool rather than switch on, the airframe bleeds off sideslip so a fighter flies where its nose
/// points ("rail" drag), the gunship's hover assist holds it level and at height, and repulsors cancel gravity only as far as
/// the engines are spooled (a dead craft drops). Rotation goes through Rigidbody.SmoothRotate so collisions still push back.
/// </summary>
public sealed partial class SwrpFlightShip
{
	/// <summary> What the pilot is asking for this frame (set on the pilot's machine). </summary>
	public struct PilotInput
	{
		public float Forward;     // W/S: throttle (fighter) or forward/back (gunship)
		public float Strafe;      // D/A: right/left (gunship)
		public float Lift;        // Space/Ctrl
		public float Roll;        // D/A on a fighter
		public bool Boost;        // Shift
		public Vector3 Aim;       // where the nose should go
		public bool HasAim;
	}

	PilotInput _pilotIn;
	PilotInput _testIn;
	float _testUntil;

	/// <summary> Host, dev: fly an empty craft with fixed controls for a while (swrp_ship_fly). </summary>
	public void HostTestFly( PilotInput input, float seconds )
	{
		if ( IsProxy ) { Log.Info( "[SWRP] someone else flies that craft" ); return; }
		EngineOn = true;                // (it wakes once the ground under it has collision)
		_testIn = input;
		_testUntil = seconds;
	}
	bool _parked;
	[ConVar( "swrp_flight_debug", Help = "Dev: log the flight controls and altitude twice a second" )]
	public static bool FlightDebug { get; set; }
	RealTimeSince _sinceFlightLog, _sinceStepLog;
	float _vzOut, _vzCmd;
	bool _vzSet;
	TimeSince _sinceBoarded = 100f;
	TimeSince _sinceMoving;
	TimeSince _sinceAwake;
	Vector3 _rates;               // roll, pitch, yaw in degrees a second (local; wrecks tumbling)
	float _bank;                  // the fighter's lean into turns
	float _heading;
	bool _headingSet;

	protected override void OnFixedUpdate()
	{
		if ( Def is null || !_rb.IsValid() ) return;
		if ( IsProxy )
		{
			_rb.MotionEnabled = false;
			return;
		}
		var dt = Time.Delta;

		// parked: a still body until someone boards or something hits it (no one may be near enough to stream the floor's collision)
		if ( _parked )
		{
			_rb.MotionEnabled = false;
			// wake up once someone boards or it's hit (the deck under it is checked first, so it never drops through)
			// a trooper who just sat down is still a solid body inside the hull for a frame or two (until RidersTick switches their
			// collision off): waking then shoved the ARC-170 down through the deck and wrecked it (Angelo, 2026-10-12)
			var boarded = Count > 0 && _sinceBoarded > 0.5f;
			if ( ( boarded || EngineOn && _sinceBoarded > 0.5f || Wrecked ) && GroundReady() ) { _parked = false; _sinceAwake = 0; }
			else return;
		}
		_rb.MotionEnabled = true;

		Spool = Spool.Approach( EngineOn && !Wrecked ? 1f : 0f, dt / Def.SpoolSeconds );
		// nobody at the stick (the pilot got out, went down or disconnected): throttle off and a slow, level descent until it
		// settles (the landing assist slows the last of it); dev test flights keep their fixed controls
		if ( !Pilot.IsValid() )
		{
			if ( _testUntil > 0f ) _pilotIn = _testIn;
			else
			{
				_pilotIn = Landed ? default : new PilotInput { Lift = -0.35f };
				Throttle = 0f;
			}
		}
		if ( _testUntil > 0f && ( _testUntil -= dt ) <= 0f ) { _testIn = default; Log.Info( $"[SWRP] flight test end: {Def.Name} at {WorldPosition:0} speed {_rb.Velocity.Length:0} angles {WorldRotation.Angles()}" ); }

		if ( Wrecked )
		{
			WreckFlight( dt );
			return;
		}

		if ( Spool <= 0.01f )
		{
			// dead stick: just gravity; once it's sat still a few seconds with nobody aboard it parks
			if ( Count == 0 && _rb.Velocity.Length < 10f && _rb.AngularVelocity.Length < 0.1f )
			{
				if ( _sinceMoving > 4f && Landed ) _parked = true;
			}
			else _sinceMoving = 0;
			_headingSet = false;
			_vzSet = false;
			return;
		}
		_sinceMoving = 0;

		var power = Spool * Spool * ( 3f - 2f * Spool );
		var g = Scene.PhysicsWorld.Gravity;
		var vzIn = _rb.Velocity.z;
		if ( Def.Model == FlightModel.Fighter ) FlyFighter( dt, power, g );
		else FlyGunship( dt, power, g );
		var vzFly = _rb.Velocity.z;
		KeepInSpace( dt );
		_vzCmd += _rb.Velocity.z - vzFly;      // the edge of space pulls the gunship's own climb/sink too (it's set fresh every step)
		if ( FlightDebug && _sinceStepLog > 0.25f )
		{
			_sinceStepLog = 0;
			Log.Info( $"[SWRP] flight step {Def.Name}: vz after physics {vzIn:0} (we left {_vzOut:0}), after fly {vzFly:0}, after space {_rb.Velocity.z:0}, z {WorldPosition.z:0}, g {g.z:0}, dt {dt:0.0000}, lift {_pilotIn.Lift:0.00}" );
		}
		_vzOut = _rb.Velocity.z;
	}

	[ConVar( "swrp_space_radius", Help = "How far from the Venator craft may fly before space gently turns them back (units)", Saved = true )]
	public static float SpaceRadius { get; set; } = 30000f;

	/// <summary> Centre of the flyable space: the middle of the Venator. Above SpaceTop sit the map's floating Jedi temple and sim rooms. </summary>
	static readonly Vector3 SpaceCentre = new( 0f, -2500f, -1000f );
	const float SpaceTop = 11000f, SpaceBottom = -16000f;

	/// <summary>
	/// The edge of the flyable space. The old map had an invisible box ~13000 units out that craft slammed into; now there's no
	/// wall, just a soft pull back towards the ship once you're past the radius (Angelo, 2026-10-12: fly in and out of the Venator).
	/// </summary>
	/// <summary>
	/// Places where the space edge doesn't apply (the Mos Eisley trip, TatooineWorld): return true for a position that is free
	/// flight, and KeepInSpace leaves the craft alone there and OutOfSpace stays false.
	/// </summary>
	public static Func<Vector3, bool> FreeFlight { get; set; }

	void KeepInSpace( float dt )
	{
		var p = WorldPosition;
		if ( FreeFlight?.Invoke( p ) == true ) return;
		var push = Vector3.Zero;
		var flat = ( p - SpaceCentre ).WithZ( 0f );
		var over = flat.Length - SpaceRadius;
		if ( over > 0f ) push -= flat.Normal * MathF.Min( over, 3000f ) * 0.6f;
		if ( p.z > SpaceTop ) push += Vector3.Down * MathF.Min( p.z - SpaceTop, 3000f ) * 0.6f;
		if ( p.z < SpaceBottom ) push += Vector3.Up * MathF.Min( SpaceBottom - p.z, 3000f ) * 0.6f;
		if ( push == Vector3.Zero ) return;
		// cancel the outward part of the velocity, then pull back in
		var v = _rb.Velocity;
		var n = push.Normal;
		var outward = Vector3.Dot( v, -n );
		if ( outward > 0f ) v += n * outward * MathF.Min( 1f, dt * 1.5f );
		_rb.Velocity = v + push * dt;
	}

	/// <summary> True once a craft is past the edge of the flyable space (FlightHud warns the pilot). </summary>
	public bool OutOfSpace
	{
		get
		{
			var p = WorldPosition;
			if ( FreeFlight?.Invoke( p ) == true ) return false;
			return ( p - SpaceCentre ).WithZ( 0f ).Length > SpaceRadius || p.z > SpaceTop || p.z < SpaceBottom;
		}
	}

	void FlyFighter( float dt, float power, Vector3 g )
	{
		var d = Def;
		var rot = WorldRotation;
		var inp = _pilotIn;

		// W/S move the throttle; nobody at the stick: it eases off to a hover
		Throttle = Pilot.IsValid() || _testUntil > 0f ? Math.Clamp( Throttle + inp.Forward * dt * 0.7f, 0f, 1f ) : Throttle.Approach( 0f, dt * 0.4f );

		// the afterburner (Shift): heats while it's lit and cools after; run it dry and it's locked out until it has cooled
		// most of the way (Angelo, 2026-10-21: "boost thrusters ... like afterburner")
		Boosting = inp.Boost && Throttle > 0.15f && !_overheated;
		Heat = Math.Clamp( Heat + ( Boosting ? dt / d.BoostSeconds : -dt / d.BoostCool ), 0f, 1f );
		if ( Heat >= 1f ) _overheated = true;
		else if ( _overheated && Heat < 0.35f ) _overheated = false;
		Overheated = _overheated;

		// thrust only ever pushes along the nose. The throttle sets the speed it pushes towards; the airframe's lift and
		// repulsors carry its weight, but the part of gravity along the nose is felt: diving gains speed, climbing costs it, and
		// drag pulls an overspeed back (Angelo, 2026-10-21: it couldn't thrust forward pointing down on the planet)
		var lv = rot.Inverse * _rb.Velocity;
		var top = Throttle * d.MaxSpeed * ( Boosting ? d.BoostMul : 1f );
		var accel = d.Accel * ( Boosting ? 1.8f : 1f ) * power;
		var along = Vector3.Dot( g, rot.Forward ) * 0.6f * power;          // (+ pointing down)
		if ( lv.x < top ) lv.x = MathF.Min( top, lv.x + accel * dt ) + along * dt;
		else lv.x = lv.x.Approach( top, ( accel * 0.5f + lv.x * lv.x / ( d.MaxSpeed * d.MaxSpeed ) * accel * 0.5f ) * dt ) + along * dt;

		// flight assist: the airframe and repulsors kill sideslip so it flies where it points; Space/Ctrl lift it straight up and down
		var grip = 1f - MathF.Exp( -( 1.2f + 2.8f * power ) * d.SlipGrip * dt );
		lv.y += ( 0f - lv.y ) * grip;
		lv.z += ( MathF.Max( inp.Lift * d.ClimbSpeed, -LandingSink( d ) ) * power - lv.z ) * grip;
		_rb.Velocity = rot * lv - g * power * dt;

		// on the deck with the gear down: taxi gently and only turn flat (no pitching or rolling into the floor) until lifting off
		var grounded = GearDown && AltitudeCached < 40f && inp.Lift <= 0f;
		if ( grounded )
		{
			var taxi = rot.Inverse * _rb.Velocity;
			taxi.x = Math.Clamp( taxi.x, -150f, 450f );
			_rb.Velocity = rot * taxi;
		}

		// steering: the nose swings toward the aim, but the turn has inertia: it winds up and runs out at the craft's angular
		// acceleration (an ARC-170 snaps round, heavier craft lean into it). Near straight down is allowed for diving.
		var aim = inp.HasAim ? inp.Aim : rot.Forward.WithZ( 0 ).Normal;
		var now = rot.Angles();
		var aimAngles = Rotation.LookAt( aim ).Angles();
		aimAngles.pitch = Math.Clamp( aimAngles.pitch, -88f, 88f );
		var speedK = 0.6f + 0.4f * Math.Clamp( lv.x / 1400f, 0f, 1f );
		// the turn is a velocity (yaw and pitch rates) that winds toward the one that would bring the nose onto the aim and
		// stop it there; reversing a turn runs the rate down through zero instead of flipping it in one step (the course log
		// showed 2,500 deg/s^2 spikes at the slalom's reversals with a single turn-speed number)
		var dYaw = AngleDelta( now.yaw, aimAngles.yaw );
		var dPitch = aimAngles.pitch - now.pitch;
		var gap = MathF.Sqrt( dYaw * dYaw + dPitch * dPitch );
		var rateWant = MathF.Min( gap * 3.5f, d.PitchRate * speedK * power );
		rateWant = MathF.Min( rateWant, MathF.Sqrt( 2f * d.TurnAccel * MathF.Max( 0f, gap ) ) );      // (v^2 = 2 a s: stops on the aim)
		var want = gap > 0.01f ? new Vector2( dYaw, dPitch ) / gap * rateWant : Vector2.Zero;
		var change = want - _turnVel;
		var maxChange = d.TurnAccel * dt;
		_turnVel += change.Length > maxChange ? change.Normal * maxChange : change;
		var na = new Angles( Math.Clamp( now.pitch + _turnVel.y * dt, -88f, 88f ), now.yaw + _turnVel.x * dt, 0f );
		if ( grounded ) { na.pitch = 0f; na.yaw = now.yaw + Math.Clamp( dYaw, -30f * dt, 30f * dt ); _turnVel = Vector2.Zero; }

		var yawRate = dt > 0f ? AngleDelta( now.yaw, na.yaw ) / dt : 0f;
		// banking: leans by how hard it's turning (hovering it barely leans; at speed it banks hard), A/D lean it further or
		// level it; the roll itself winds up at the roll rate
		var bankWant = grounded ? 0f : Math.Clamp( -yawRate * 1.1f * Math.Clamp( MathF.Abs( lv.x ) / 900f, 0.2f, 1f ) + inp.Roll * 45f, -d.MaxBank, d.MaxBank );
		_bankRate = _bankRate.Approach( Math.Clamp( ( bankWant - _bank ) * 4f, -d.RollRate, d.RollRate ), d.RollRate * 4f * dt );
		_bank += _bankRate * dt;
		_rb.SmoothRotate( Rotation.From( new Angles( na.pitch, na.yaw, _bank ) ), dt * 2f, dt );
	}

	void FlyGunship( float dt, float power, Vector3 g )
	{
		var d = Def;
		var inp = _pilotIn;
		if ( FlightDebug && _sinceFlightLog > 0.5f )
		{
			_sinceFlightLog = 0;
			Log.Info( $"[SWRP] flight {Def.Name}: lift {inp.Lift:0.00} fwd {inp.Forward:0.00} alt {AltitudeCached:0} sink {LandingSink( d ):0} vz {_rb.Velocity.z:0} power {power:0.00} pilot {Pilot?.CharacterName} proxy {IsProxy}" );
		}
		if ( !_headingSet ) { _heading = WorldRotation.Yaw(); _headingSet = true; }
		Boosting = inp.Boost && inp.Forward > 0.1f && !_overheated;
		Heat = Math.Clamp( Heat + ( Boosting ? dt / d.BoostSeconds : -dt / d.BoostCool ), 0f, 1f );
		if ( Heat >= 1f ) _overheated = true;
		else if ( _overheated && Heat < 0.35f ) _overheated = false;
		Overheated = _overheated;

		// the nose swings round to the aim
		if ( inp.HasAim )
		{
			var want = Rotation.LookAt( inp.Aim.WithZ( 0 ) ).Yaw();
			// 14 tonnes: the turn winds up and runs out at the yaw acceleration instead of starting and stopping at once
			var rate = Math.Clamp( AngleDelta( _heading, want ) * 2.2f, -d.YawRate, d.YawRate );
			_yawVel = _yawVel.Approach( rate, d.YawAccel * dt );
			_heading += _yawVel * dt * power;
		}
		else _yawVel = _yawVel.Approach( 0f, d.YawAccel * dt );
		var flat = Rotation.FromYaw( _heading );

		// WAC-style hover assist: with no input it holds position and height; inputs set a velocity to fly at
		var speed = d.MaxSpeed * ( Boosting ? d.BoostMul : 1f );
		var target = ( flat.Forward * inp.Forward * speed + flat.Right * inp.Strafe * d.StrafeSpeed + Vector3.Up * MathF.Max( inp.Lift * d.ClimbSpeed, -LandingSink( d ) ) ) * power;
		var v = _rb.Velocity;
		var a = d.Accel * ( Boosting ? 1.35f : 1f ) * power;
		var flatV = v.WithZ( 0 );
		flatV += ( target.WithZ( 0 ) - flatV ).ClampLength( a * dt );
		// the climb or sink is our own number, eased towards the target, not re-read from the body each step: something in the
		// physics step kept knocking the measured speed back up (up to +500 in one step), so held Ctrl barely sank the LAAT
		// (Angelo, 2026-10-19, logged with swrp_flight_debug). Never re-read after the start: taking over a collision's knock
		// (+thousands) once launched the LAAT 120,000 units up, so a hull scrape now only shoves it for that one step.
		if ( !_vzSet ) { _vzCmd = v.z; _vzSet = true; }
		_vzCmd = _vzCmd.Approach( target.z, a * 1.4f * dt );
		var vz = _vzCmd;
		_rb.Velocity = flatV.WithZ( vz ) - g * power * dt;

		// attitude: the nose dips as it speeds up and lifts as it brakes, it banks into strafes and turns
		var lv = flat.Inverse * flatV;
		var push = flat.Inverse * ( target - v ).WithZ( 0 );
		var pitch = Math.Clamp( push.x / MathF.Max( 1f, speed ) * 20f + lv.x / MathF.Max( 1f, speed ) * 6f, -14f, 14f );
		var turn = inp.HasAim ? AngleDelta( _heading, Rotation.LookAt( inp.Aim.WithZ( 0 ) ).Yaw() ) : 0f;
		// lean into the turn and into a strafe (from the controls, not the sideslip, which points the wrong way mid-turn)
		var bank = Math.Clamp( inp.Strafe * 14f - turn * 0.3f * Math.Clamp( flatV.Length / 600f, 0.3f, 1f ), -24f, 24f );
		if ( GearDown && AltitudeCached < 40f && inp.Lift <= 0f ) { pitch = 0f; bank = 0f; }      // sitting on the gear: stay level
		var attitude = Rotation.From( new Angles( pitch, _heading, bank ) );
		_rb.SmoothRotate( attitude, 0.35f, dt );
	}

	/// <summary> Landing assist: the closer the ground, the slower it lets you sink (a gentle touchdown, not a crash). </summary>
	float LandingSink( CraftDef d )
	{
		var alt = AltitudeCached;
		return MathX.Lerp( 120f, d.ClimbSpeed, Math.Clamp( ( alt - 30f ) / 450f, 0f, 1f ) );
	}

	/// <summary> Wrecked and falling: no power, a slow roll and the nose dropping. </summary>
	void WreckFlight( float dt )
	{
		Throttle = 0f;
		Boosting = false;
		if ( _rb.Velocity.Length < 60f ) return;
		_rates += ( new Vector3( 110f, 22f, 18f ) - _rates ) * ( 1f - MathF.Exp( -1.5f * dt ) );
		Turn( _rates, 0.15f );
	}

	/// <summary> Turn at local rates (roll, pitch, yaw in degrees a second) through the physics body. </summary>
	void Turn( Vector3 rates, float lead )
	{
		var target = WorldRotation * Rotation.From( new Angles( rates.y * lead, rates.z * lead, rates.x * lead ) );
		_rb.SmoothRotate( target, lead, Time.Delta );
	}

	bool _waitingGround;

	/// <summary> Something solid under the gear, or open air under it, or we've waited long enough. </summary>
	bool GroundReady()
	{
		var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * 40f, WorldPosition + Vector3.Down * 120f ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "player", "trigger", "npc", "debris" ).Run();
		if ( tr.Hit ) { _waitingGround = false; return true; }
		return true;
	}

	static float AngleDelta( float from, float to )
	{
		var d = ( to - from ) % 360f;
		if ( d > 180f ) d -= 360f;
		if ( d < -180f ) d += 360f;
		return d;
	}

	// ----- crashes ------------------------------------------------------------------------------------------------------

	TimeSince _sinceImpact;

	void ICollisionListener.OnCollisionStart( Collision c )
	{
		if ( IsProxy || Def is null || _sinceImpact < 0.4f ) return;
		var other = c.Other.GameObject;
		if ( other is null || other.Tags.Has( "player" ) || other.Tags.Has( "debris" ) || other.Tags.Has( "npc" ) || other.Root.Components.Get<SwrpPlayer>() is not null ) return;
		// loose things (a dropped rifle, a crate) get knocked aside, they don't wreck a gunship: only the world and other craft count
		if ( other.Root.Components.Get<SwrpFlightShip>() is null && other.Root.Components.Get<Rigidbody>() is { MotionEnabled: true } ) return;
		// only the push straight into what was hit counts (sliding and settling on the deck don't), and not while waking or spooling
		var speed = MathF.Abs( c.Contact.NormalSpeed );
		if ( !Wrecked && ( _sinceAwake < 4f || Spool < 0.95f && speed < 1100f ) ) return;
		if ( speed < ( Wrecked ? 200f : 650f ) ) return;
		_sinceImpact = 0;
		Log.Info( $"[SWRP] flight: {Def.Name} hit {other.Name} at {speed:0} u/s" );
		ReportImpact( speed, c.Contact.Point );
	}

	void ICollisionListener.OnCollisionUpdate( Collision c ) { }
	void ICollisionListener.OnCollisionStop( CollisionStop c ) { }

	/// <summary> Owner to host: the craft hit something hard. </summary>
	[Rpc.Host]
	void ReportImpact( float speed, Vector3 at )
	{
		if ( Rpc.Caller != GameObject.Network.Owner && !( Rpc.Caller?.IsHost ?? false ) ) return;
		if ( Wrecked ) { _crashed = true; return; }
		HostDamage( ( speed - 550f ) * 1.4f, at, null, "a crash" );
		ImpactFx( at, Math.Clamp( speed / 1500f, 0.3f, 1f ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ImpactFx( Vector3 at, float strength )
	{
		Sound.Play( "sounds/flight/impact.sound", at );
		SwrpShake.AddAt( at, 0.5f * strength, 1500f );
		for ( var i = 0; i < (int)( 14 * strength ) && SparkFx.Live < SwrpFx.MaxSparks; i++ )
			SparkFx.Spawn( Scene, at, ( Vector3.Random + Vector3.Up * 0.5f ).Normal * Game.Random.Float( 150f, 400f ), new Color( 1f, 0.75f, 0.4f ) );
	}
}
