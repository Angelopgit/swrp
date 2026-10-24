using System;

namespace Swrp.Flight;

/// <summary>
/// The local side of sitting in a craft: holding riders in their seats on every screen, the seated trooper's controls and
/// camera. Keys (no new bindings needed):
///   pilot   mouse aim (the craft turns to it), W/S throttle (fighter) or forward/back (gunship), A/D roll (fighter) or
///           strafe (gunship), Space/Ctrl up/down, Shift boost, R engines on/off, Alt hold to look round without steering
///   all     Mouse1/Mouse2 the seat's weapons, C cockpit or chase view, 1-9 change seat, E get out
/// </summary>
public sealed partial class SwrpFlightShip
{
	static SwrpFlightShip _localShip;
	static int _localSeat = -1;
	static Angles _aim, _steer;
	static bool _altView;
	static RealTimeSince _sinceExitAsked = 100f;
	static RealTimeSince _sinceInputLog;
	static RealTimeSince _sinceSeated = 100f;

	/// <summary> The craft the local trooper sits in (null on foot), and their seat. </summary>
	public static SwrpFlightShip LocalShip => _localShip.IsValid() ? _localShip : null;
	public static int LocalSeat => _localSeat;
	public static Angles LocalAim => _aim;

	/// <summary> Every frame on the local machine: if the local trooper sits here, read their controls. </summary>
	void LocalPilot()
	{
		var me = SwrpPlayer.Local;
		var seat = me.IsValid() && _sinceExitAsked > 1f ? SeatOf( me ) : -1;
		if ( seat < 0 )
		{
			if ( _localShip == this ) { _localShip = null; _localSeat = -1; StopFeel(); }
			return;
		}
		if ( _localShip != this || _localSeat != seat )
		{
			// just sat down: look the way the seat faces, start in the seat's natural view
			_localShip = this;
			_sinceSeated = 0;
			_aim = ( WorldRotation * Rotation.FromYaw( Def.Seats[seat].Yaw ) ).Angles().WithRoll( 0 );
			_steer = _aim;
			_altView = Def.Seats[seat].Role != SeatRole.Pilot;
			EnsureHud();
		}
		_localSeat = seat;
		var role = Def.Seats[seat].Role;

		if ( SwrpUiState.AnyOpen || SwrpUiState.ChatOpen )
		{
			if ( role == SeatRole.Pilot ) _pilotIn = new PilotInput { Aim = _steer.Forward, HasAim = true };
			return;
		}

		_aim += Input.AnalogLook;
		_aim = _aim.WithPitch( Math.Clamp( _aim.pitch, -89f, 89f ) ).WithRoll( 0 );

		// not the same E press that boarded: on the host the seat is taken within that frame, and reading the press here as well
		// put the trooper straight back out (Angelo, 2026-10-12: "it won't place me in the vehicle")
		if ( Input.Pressed( "Use" ) && _sinceSeated > 0.4f )
		{
			_sinceExitAsked = 0;
			_localShip = null;
			_localSeat = -1;
			LeaveLocally( me );
			RequestExit();
			return;
		}
		for ( var i = 1; i <= 9 && i <= Def.Seats.Length; i++ )
			if ( Input.Pressed( $"Slot{i}" ) && i - 1 != seat ) RequestSeat( i - 1 );
		if ( Input.Pressed( "View" ) ) _altView = !_altView;
		LocalTurretAim( seat, _aim.Forward );

		if ( role == SeatRole.Pilot )
		{
			// a request made before the craft became ours on the network: the host may have been too late to act on it
			if ( !IsProxy && _wantEngine is { } e ) { EngineOn = e && !Wrecked; _wantEngine = null; }
			if ( !IsProxy && _wantDoors is { } d ) { DoorsOpen = d; _wantDoors = null; }
			if ( Input.Pressed( "Reload" ) && !Wrecked )
			{
				if ( IsProxy ) { _wantEngine = !EngineOn; RequestEngine( !EngineOn ); }      // ownership is still on its way: ask the host, and apply it ourselves once it lands
				else EngineOn = !EngineOn;
			}
			if ( Input.Pressed( "Slot0" ) && Def.HasDoors )
			{
				if ( IsProxy ) { _wantDoors = !DoorsOpen; RequestDoors( !DoorsOpen ); }
				else DoorsOpen = !DoorsOpen;
			}
			// Alt: look round freely; the craft keeps the heading it had
			if ( !Input.Down( "Walk" ) ) _steer = SmoothSteer( _steer, _aim );      // (eased, SwrpFlightShip.Feel.cs)
			var move = Input.AnalogMove;
			var fighter = Def.Model == FlightModel.Fighter;
			_pilotIn = new PilotInput
			{
				Forward = move.x,
				Strafe = fighter ? 0f : -move.y,
				Roll = fighter ? -move.y : 0f,
				Lift = ( Input.Down( "Jump" ) ? 1f : 0f ) - ( Descend ? 1f : 0f ),
				Boost = Input.Down( "Run" ),
				Aim = _steer.Forward,
				HasAim = true,
			};
			UpdateLock();      // (SwrpFlightShip.Instruments.cs)
		}

		if ( FlightDebug && _sinceInputLog > 0.5f )
		{
			_sinceInputLog = 0;
			bool duck = Input.Down( "Duck" ), ctrl = Input.Keyboard.Down( "ctrl" ), lctrl = Input.Keyboard.Down( "lctrl" ), ckey = Input.Keyboard.Down( "c" ), jump = Input.Down( "Jump" );
			var names = new[] { "control", "lcontrol", "rctrl", "rcontrol", "CTRL", "LCTRL", "LCONTROL", "left ctrl" };
			var extra = string.Join( " ", names.Select( n => { try { return $"{n}={Input.Keyboard.Down( n )}"; } catch { return $"{n}=x"; } } ) );
			var actions = string.Join( " ", Input.GetActions().Where( x => x.Name is "Duck" ).Select( x => $"Duck->{x.KeyboardCode}" ) );
			Log.Info( $"[SWRP] flight input: duck {duck} ctrl {ctrl} lctrl {lctrl} c {ckey} jump {jump} ui {SwrpUiState.AnyOpen} lift {_pilotIn.Lift:0.00} | {extra} | {actions}" );
		}
		LocalWeapons( seat );
	}

	/// <summary>
	/// Down: Left Ctrl (the Duck action), or the C key straight from the keyboard. The Duck action alone didn't bring the LAAT
	/// down in play (Angelo, 2026-10-12), so the raw keys are read too.
	/// </summary>
	static bool Descend => Input.Down( "Duck" ) || Input.Keyboard.Down( "ctrl" ) || Input.Keyboard.Down( "lctrl" ) || Input.Keyboard.Down( "c" );

	bool? _wantEngine, _wantDoors;
	static GameObject _hud;

	/// <summary> The flight HUD: its own screen panel, made the first time you sit in a craft. </summary>
	static void EnsureHud()
	{
		if ( _hud.IsValid() ) return;
		_hud = new GameObject( true, "Flight HUD" );
		_hud.NetworkMode = NetworkMode.Never;
		_hud.Flags |= GameObjectFlags.NotSaved;
		_hud.Components.Create<ScreenPanel>().ZIndex = 60;
		_hud.Components.Create<FlightHud>();
	}

	/// <summary> Pilot to host, before the craft is theirs on the network: start or stop the engines. </summary>
	[Rpc.Host]
	void RequestEngine( bool on )
	{
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		if ( p.IsValid() && SeatOf( p ) == Def.PilotSeat && !IsProxy ) EngineOn = on && !Wrecked;
	}

	/// <summary> Local: step out at once on this screen (the host moves us to the door). Keep the craft's speed if it's flying. </summary>
	void LeaveLocally( SwrpPlayer me )
	{
		RestoreRider( me, Velocity );
	}

	// ----- camera -------------------------------------------------------------------------------------------------------

	protected override void OnPreRender()
	{
		if ( Def is null ) return;
		HoldRiders();
		if ( _localShip != this || _localSeat < 0 ) return;
		var cam = Scene.Camera;
		if ( !cam.IsValid() ) return;

		var seat = Def.Seats[_localSeat];
		var rot = _aim.ToRotation();
		var chase = seat.Role == SeatRole.Pilot ? !_altView : _altView;
		if ( chase )
		{
			// behind and above the craft, looking where you aim; pulled in short of walls
			var pivot = WorldPosition + WorldRotation.Up * Def.CameraHeight * 0.45f + Vector3.Up * Def.CameraHeight * 0.3f;
			var want = pivot - rot.Forward * Def.CameraDistance + Vector3.Up * Def.CameraHeight * 0.25f;
			var tr = Scene.Trace.Ray( pivot, want ).Radius( 10f ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "trigger", "player", "debris", "flightship" ).Run();
			var (chasePos, chaseRot) = ChaseCamera( tr.Hit ? tr.EndPosition + tr.Normal * 6f : want, rot );
			cam.WorldPosition = chasePos;
			cam.WorldRotation = chaseRot;
		}
		else
		{
			cam.WorldPosition = WorldTransform.PointToWorld( seat.Eye );
			cam.WorldRotation = rot;
		}
		FlightFeel( cam, chase );
	}

	// ----- riders -------------------------------------------------------------------------------------------------------

	/// <summary> Every screen, every frame: troopers aboard sit (or stand) in their seats and turn to look where they look. </summary>
	/// <summary>
	/// Host: teleport the craft (and everyone seated in it) to a new place, e.g. the jump down to Mos Eisley and back. The
	/// riders are moved onto their seats at once and nobody sees the craft slide across the gap.
	/// </summary>
	public void Relocate( Vector3 pos, Rotation rot, Vector3 velocity )
	{
		if ( !Networking.IsHost ) return;
		// a piloted craft belongs to its pilot: their synced transform would overwrite a host-side move, so the owner does it
		if ( IsProxy ) { RelocateOwner( pos, rot, velocity ); return; }
		MoveTo( pos, rot, velocity );
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void RelocateOwner( Vector3 pos, Rotation rot, Vector3 velocity ) => MoveTo( pos, rot, velocity );

	void MoveTo( Vector3 pos, Rotation rot, Vector3 velocity )
	{
		WorldPosition = pos;
		WorldRotation = rot;
		if ( _rb.IsValid() ) { _rb.Velocity = velocity; _rb.AngularVelocity = Vector3.Zero; }
		Network.ClearInterpolation();
		HoldRiders();
		for ( var i = 0; i < _seats.Length; i++ )
			if ( _seats[i] != Guid.Empty && PlayerIn( i ) is { } p && p.IsValid() ) p.Network.ClearInterpolation();
	}

	void HoldRiders()
	{
		Parse();
		for ( var i = 0; i < _seats.Length; i++ )
		{
			if ( _seats[i] == Guid.Empty ) continue;
			var p = PlayerIn( i );
			if ( !p.IsValid() ) continue;
			if ( !p.IsProxy && _sinceExitAsked < 1.2f ) continue;            // stepping out
			var t = SeatTransform( i );
			var seat = Def.Seats[i];
			// the rider's own machine and the host move the (synced) trooper; everyone else only draws the body in the seat, so
			// their copy doesn't fight the synced position and jitter (refinement thread, 2026-10-12)
			if ( !p.IsProxy || Networking.IsHost ) p.WorldPosition = t.Position;
			else if ( p.Body.IsValid() ) p.Body.WorldPosition = t.Position;
			// seen by everyone in an open cockpit or bay; hidden in turrets, and from yourself when you look out of your own eyes
			var firstPerson = !p.IsProxy && _localShip == this && !( seat.Role == SeatRole.Pilot ? !_altView : _altView );
			p.HideInVehicle = seat.HideBody || firstPerson;
			if ( p.Body.IsValid() ) p.Body.Set( "sit", seat.Sit ? 1 : 0 );
			// standing in the bay: still and facing forward, a hand on the overhead line (SwrpPlayer.RideHold)
			p.RideHold = seat.Role == SeatRole.Passenger && !seat.Sit;
			p.RideRotation = t.Rotation;
			if ( !p.IsProxy && p.Controller.IsValid() )
			{
				p.Controller.EyeAngles = _aim.WithPitch( Math.Clamp( _aim.pitch, -60f, 60f ) );
				if ( p.GetComponent<Rigidbody>() is { } rb ) rb.Velocity = Vector3.Zero;
			}
		}
	}

	static float _ridersAt = -1f;
	static readonly Dictionary<SwrpPlayer, List<Collider>> _riding = new();

	/// <summary>
	/// Once a frame on every machine: troopers aboard lose their own collision (it would shove the craft about) and stop
	/// simulating their body; they get both back when they leave.
	/// </summary>
	static void RidersTick( Scene scene )
	{
		if ( _ridersAt == Time.Now ) return;
		_ridersAt = Time.Now;

		var aboard = new HashSet<SwrpPlayer>();
		foreach ( var s in All )
		{
			if ( s.Def is null ) continue;
			for ( var i = 0; i < s.Def.Seats.Length; i++ )
				if ( s.PlayerIn( i ) is { } p && p.IsValid() ) aboard.Add( p );
		}

		foreach ( var p in aboard )
		{
			if ( !p.IsProxy && _sinceExitAsked < 1.2f ) continue;
			if ( !_riding.TryGetValue( p, out var off ) ) _riding[p] = off = new List<Collider>();
			// every frame: anything that switched a collider back on (the player controller does) is switched off again,
			// or the trooper's body inside the hull shoves the craft about and it "crashes" on the deck
			foreach ( var c in p.Components.GetAll<Collider>( FindMode.EnabledInSelfAndDescendants ) )
			{
				c.Enabled = false;
				if ( !off.Contains( c ) ) off.Add( c );
			}
			if ( !p.IsProxy && p.GetComponent<Rigidbody>() is { } rb && rb.MotionEnabled )
			{
				rb.Velocity = Vector3.Zero;
				rb.MotionEnabled = false;
			}
			// and the trooper's movement controller is switched off while aboard: in its fixed step it put the body's colliders
			// back on before the physics ran, so a seated trooper (held still, a solid lump inside the hull) shoved the craft
			// down through the hangar deck at ~130 u/s as it spooled (Angelo, 2026-10-21; switching the controller off stopped it)
			if ( !p.IsProxy && p.Controller.IsValid() && p.Controller.Enabled ) p.Controller.Enabled = false;
		}

		foreach ( var p in _riding.Keys.ToList() )
			if ( !p.IsValid() ) _riding.Remove( p );
			else if ( !aboard.Contains( p ) ) RestoreRider( p, Vector3.Zero );
	}

	static void RestoreRider( SwrpPlayer p, Vector3 velocity )
	{
		if ( !p.IsValid() ) return;
		p.HideInVehicle = false;
		p.RideHold = false;
		if ( p.Body.IsValid() ) { p.Body.Set( "sit", 0 ); p.Body.LocalPosition = Vector3.Zero; }
		if ( !p.IsProxy && p.Controller.IsValid() && !p.Controller.Enabled ) p.Controller.Enabled = true;
		if ( !_riding.Remove( p, out var off ) ) return;
		foreach ( var c in off ) if ( c.IsValid() ) c.Enabled = true;
		if ( !p.IsProxy && !p.Noclip && p.GetComponent<Rigidbody>() is { } rb )
		{
			rb.MotionEnabled = true;
			rb.Velocity = velocity;
		}
	}
}
