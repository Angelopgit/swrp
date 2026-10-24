using System;

namespace Swrp;

/// <summary>
/// Sandbox's contraption gadgets and how they're driven, adapted from Facepunch's Sandbox (ControlSystem, ThrusterEntity,
/// WheelEntity, HoverballEntity, MotorEntity, BaseChair; MIT, see LICENSE-facepunch-sandbox.txt). A trooper sits in a
/// <see cref="SwrpSeat"/>; every tick the host finds everything joined to that seat (<see cref="SwrpContraption.Linked"/>) and lets
/// each gadget read the sitter's keys (the same Input.config actions as on foot, through <c>Connection.Down</c>). Gadgets are
/// staff props with one extra component, so the physgun, remover and undo treat them like any other prop.
/// Sounds are SWRP's own (Sandbox's are Envato-licensed).
/// </summary>
public interface ISwrpControllable
{
	/// <summary> Host, every physics tick while someone sits in a seat joined to this gadget. </summary>
	void OnControl( Connection driver );
}

public static class SwrpContraption
{
	/// <summary> The keys a gadget can listen to (Input.config action names) and how the menu shows them. </summary>
	public static readonly (string Action, string Label)[] Keys =
	{
		( "", "NONE" ), ( "Forward", "W" ), ( "Backward", "S" ), ( "Left", "A" ), ( "Right", "D" ), ( "Jump", "SPACE" ),
		( "Duck", "CTRL" ), ( "Run", "SHIFT" ), ( "Attack1", "LMB" ), ( "Attack2", "RMB" ),
	};

	public static string KeyLabel( string action ) => Keys.FirstOrDefault( k => k.Action == ( action ?? "" ) ).Label ?? action;

	public static float Analog( Connection c, string action ) => c is not null && !string.IsNullOrEmpty( action ) && c.Down( action ) ? 1f : 0f;

	/// <summary> Every root object joined to <paramref name="start"/> by joints or tool gun constraints (Sandbox's LinkedGameObjectBuilder). </summary>
	public static HashSet<GameObject> Linked( GameObject start )
	{
		var result = new HashSet<GameObject>();
		if ( start?.Root is not { } first ) return result;
		var links = start.Scene.GetAllComponents<SwrpConstraintLink>().ToArray();
		var queue = new Queue<GameObject>();
		queue.Enqueue( first );
		while ( queue.Count > 0 )
		{
			var root = queue.Dequeue();
			if ( root is null || !result.Add( root ) || result.Count > 256 ) continue;

			foreach ( var joint in root.Components.GetAll<Joint>( FindMode.EverythingInSelfAndDescendants ) )
			{
				if ( joint.Body?.Root is { } other && other != root && other is not Sandbox.Scene ) queue.Enqueue( other );
			}
			foreach ( var rb in root.Components.GetAll<Rigidbody>( FindMode.EverythingInSelfAndDescendants ) )
			{
				foreach ( var joint in rb.Joints )
				{
					if ( joint.Object1?.Root is { } a && a is not Sandbox.Scene ) queue.Enqueue( a );
					if ( joint.Object2?.Root is { } b && b is not Sandbox.Scene ) queue.Enqueue( b );
				}
			}
			foreach ( var link in links )
			{
				if ( !link.IsValid() || !link.Attachment.IsValid() ) continue;
				if ( link.GameObject.Root == root ) queue.Enqueue( link.Attachment.Root );
				else if ( link.Attachment.Root == root ) queue.Enqueue( link.GameObject.Root );
			}
		}
		result.RemoveWhere( g => g is null || g is Sandbox.Scene || g.Components.Get<SwrpPlayer>() is not null );
		return result;
	}
}

/// <summary> Host: runs every occupied seat's contraption each physics tick (Sandbox's ControlSystem). </summary>
public sealed class SwrpControlSystem : GameObjectSystem<SwrpControlSystem>
{
	public SwrpControlSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 11, Tick, "SwrpControl" );
	}

	void Tick()
	{
		if ( Scene != Game.ActiveScene || !Networking.IsHost ) return;
		var driven = new HashSet<GameObject>();
		foreach ( var seat in Scene.GetAllComponents<SwrpSeat>() )
		{
			if ( !seat.IsValid() || seat.Sitter is not { } p || p.Network.Owner is not { } driver ) continue;
			if ( driven.Contains( seat.GameObject.Root ) ) continue;      // the first to sit drives; a second seat on it does nothing
			var linked = SwrpContraption.Linked( seat.GameObject );
			if ( linked.Overlaps( driven ) ) continue;
			driven.UnionWith( linked );
			foreach ( var go in linked )
				foreach ( var c in go.Components.GetAll<ISwrpControllable>( FindMode.EnabledInSelfAndDescendants ) )
					c.OnControl( driver );
		}
	}
}

/// <summary>
/// A seat on a chair prop (Sandbox's BaseChair, simplified): E sits, E again stands up beside it. While seated your keys drive
/// whatever is joined to the chair, and the mouse still looks around.
/// </summary>
public sealed class SwrpSeat : Component, ISwrpUsable
{
	[Sync( SyncFlags.FromHost )] public Guid Occupant { get; set; }
	[Property] public Vector3 SeatOffset { get; set; } = new( 0, 0, 14 );

	public SwrpPlayer Sitter => Occupant == Guid.Empty ? null : SwrpPlayers.Valid.FirstOrDefault( p => p.GameObject.Id == Occupant );

	Transform SeatTransform => WorldTransform.WithScale( 1f ).ToWorld( new Transform( SeatOffset ) );

	public (string Title, string Sub, string Tone) Prompt( SwrpPlayer me ) =>
		Occupant == Guid.Empty ? ("Seat", "E to sit · your keys drive what it's joined to", "info") : ("Seat", "Taken", "denied");

	public void Use( SwrpPlayer me )
	{
		if ( Occupant == Guid.Empty ) RequestSit();
	}

	[Rpc.Host]
	void RequestSit()
	{
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		if ( !p.IsValid() || Occupant != Guid.Empty || p.InVehicle || p.InPod || p.Jailed || p.Frozen ) return;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } ) return;
		if ( p.WorldPosition.Distance( WorldPosition ) > 200f ) return;
		if ( Scene.GetAllComponents<SwrpSeat>().Any( s => s.Occupant == p.GameObject.Id ) ) return;
		Occupant = p.GameObject.Id;
		p.InVehicle = true;
		Sound.Play( "sounds/flight/seat.sound", WorldPosition );
	}

	[Rpc.Host]
	void RequestStand()
	{
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		if ( !p.IsValid() || p.GameObject.Id != Occupant ) return;
		HostRelease( true );
	}

	void HostRelease( bool place )
	{
		var p = Sitter;
		Occupant = Guid.Empty;
		if ( !p.IsValid() ) return;
		p.InVehicle = false;
		if ( !place ) return;
		var at = WorldPosition + WorldRotation.Right * 40f;
		var tr = Scene.Trace.Ray( at + Vector3.Up * 60f, at + Vector3.Down * 120f ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "trigger" ).Run();
		if ( tr.Hit ) at = tr.HitPosition + Vector3.Up * 4f;
		p.TeleportTo( at, WorldRotation.Yaw() );
	}

	readonly List<Collider> _off = new();
	SwrpPlayer _held;
	TimeSince _sinceSat;
	Angles _look;

	protected override void OnUpdate()
	{
		var p = Sitter;

		if ( !IsProxy && Occupant != Guid.Empty && ( !p.IsValid() || p.GetComponent<SwrpVitals>() is { IsDead: true } ) ) HostRelease( false );

		if ( _held.IsValid() && _held != p ) Restore( _held );
		if ( !p.IsValid() ) { _held = null; return; }
		if ( _held != p ) { _held = p; _sinceSat = 0; if ( p.Controller.IsValid() ) _look = p.Controller.EyeAngles; }

		var t = SeatTransform;
		if ( !p.IsProxy || Networking.IsHost ) p.WorldPosition = t.Position;
		else if ( p.Body.IsValid() ) p.Body.WorldPosition = t.Position;
		if ( p.Body.IsValid() ) { p.Body.Set( "sit", 1 ); p.Body.WorldRotation = Rotation.FromYaw( t.Rotation.Yaw() ); }

		if ( p.IsProxy ) return;
		foreach ( var c in p.Components.GetAll<Collider>( FindMode.EnabledInSelfAndDescendants ) )
		{
			c.Enabled = false;
			if ( !_off.Contains( c ) ) _off.Add( c );
		}
		if ( p.GetComponent<Rigidbody>() is { } rb ) { rb.Velocity = Vector3.Zero; rb.MotionEnabled = false; }

		// the controller ignores look while in a vehicle: look around by hand
		if ( p.Controller.IsValid() && !SwrpUiState.AnyOpen )
		{
			_look += Input.AnalogLook;
			_look = _look.WithPitch( Math.Clamp( _look.pitch, -80f, 80f ) );
			p.Controller.EyeAngles = _look;
		}
		if ( _sinceSat > 0.5f && Input.Pressed( "Use" ) && !SwrpUiState.AnyOpen ) RequestStand();
	}

	void Restore( SwrpPlayer p )
	{
		if ( p.Body.IsValid() ) p.Body.Set( "sit", 0 );
		if ( p.IsProxy ) return;
		foreach ( var c in _off ) if ( c.IsValid() ) c.Enabled = true;
		_off.Clear();
		if ( !p.Noclip && p.GetComponent<Rigidbody>() is { } rb ) rb.MotionEnabled = true;
	}

	protected override void OnDestroy()
	{
		if ( _held.IsValid() ) Restore( _held );
		if ( !IsProxy && Sitter is { } p && p.IsValid() ) p.InVehicle = false;
	}
}

/// <summary> Pushes its prop along its own down axis while its key is held (Sandbox's ThrusterEntity). </summary>
public sealed class SwrpThruster : Component, ISwrpControllable
{
	[Property, Sync] public float Power { get; set; } = 0.5f;
	[Property, Sync] public bool Invert { get; set; }
	[Property, Sync] public string Key { get; set; } = "Jump";
	[Property, Sync] public string ReverseKey { get; set; } = "";
	[Sync( SyncFlags.FromHost )] public bool Firing { get; set; }

	float _thrust;
	TimeSince _sinceControl = 10;
	PointLight _light;

	public void OnControl( Connection driver )
	{
		_thrust = Math.Clamp( SwrpContraption.Analog( driver, Key ) - SwrpContraption.Analog( driver, ReverseKey ), -1f, 1f );
		_sinceControl = 0;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( _sinceControl > 0.1f ) _thrust = 0;
		var firing = MathF.Abs( _thrust ) > 0.1f;
		if ( firing != Firing )
		{
			Firing = firing;
			if ( firing ) Sound.Play( "sounds/flight/engine_start.sound", WorldPosition );
		}
		if ( !firing || GetComponent<Rigidbody>() is not { } body ) return;
		body.ApplyImpulse( WorldRotation.Up * -10000f * _thrust * Power * ( Invert ? -1f : 1f ) );
	}

	protected override void OnUpdate()
	{
		if ( Firing && !_light.IsValid() )
		{
			var go = new GameObject( GameObject, true, "flame" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = Vector3.Down * 12f;
			_light = go.Components.Create<PointLight>();
			_light.LightColor = new Color( 0.45f, 0.75f, 1f ) * 4f;
			_light.Radius = 160f;
			_light.Shadows = false;
		}
		else if ( !Firing && _light.IsValid() ) { _light.GameObject.Destroy(); _light = null; }
		if ( _light.IsValid() ) _light.LightColor = new Color( 0.45f, 0.75f, 1f ) * ( 3.2f + MathF.Sin( Time.Now * 40f ) * 0.8f );
	}
}

/// <summary> Drives the wheel joint's motor and steering from the seated trooper's keys (Sandbox's WheelEntity). </summary>
public sealed class SwrpWheelDrive : Component, ISwrpControllable
{
	[Property, Sync] public bool Reversed { get; set; }
	[Property, Sync] public float Speed { get; set; } = 0.5f;
	[Property, Sync] public float Power { get; set; } = 0.5f;
	[Property, Sync] public string ForwardKey { get; set; } = "Forward";
	[Property, Sync] public string ReverseKey { get; set; } = "Backward";
	[Property, Sync] public string BrakeKey { get; set; } = "Jump";
	[Property, Sync] public string LeftKey { get; set; } = "";
	[Property, Sync] public string RightKey { get; set; } = "";

	TimeSince _sinceControl = 10;

	public void OnControl( Connection d )
	{
		_sinceControl = 0;
		Apply( SwrpContraption.Analog( d, ForwardKey ), SwrpContraption.Analog( d, ReverseKey ), SwrpContraption.Analog( d, BrakeKey ),
			SwrpContraption.Analog( d, LeftKey ), SwrpContraption.Analog( d, RightKey ) );
	}

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost && _sinceControl > 0.1f && _sinceControl < 0.5f ) Apply( 0, 0, 0, 0, 0 );
	}

	void Apply( float forward, float reverse, float brake, float left, float right )
	{
		var joint = Components.Get<WheelJoint>( FindMode.EverythingInSelfAndDescendants );
		if ( !joint.IsValid() ) return;
		var speed = Math.Clamp( forward - reverse, -1f, 1f );
		if ( brake > 0.1f )
		{
			joint.EnableSpinMotor = true;
			joint.SpinMotorSpeed = 0;
			joint.MaxSpinTorque = 500000 * Power;
		}
		else if ( speed.AlmostEqual( 0f ) ) joint.EnableSpinMotor = false;
		else
		{
			if ( Reversed ) speed = -speed;
			joint.EnableSpinMotor = true;
			joint.SpinMotorSpeed = -2000 * speed * Speed;
			joint.MaxSpinTorque = 200000 * Power;
		}

		var steer = Math.Clamp( right - left, -1f, 1f );
		if ( string.IsNullOrEmpty( LeftKey ) && string.IsNullOrEmpty( RightKey ) ) return;
		joint.EnableSteering = true;
		joint.SteeringDampingRatio = 1f;
		joint.MaxSteeringTorque = 500000;
		joint.SteeringLimits = new Vector2( -45, 45 );
		joint.TargetSteeringAngle = 30 * steer;
	}
}

/// <summary> Holds its height against gravity; the seated trooper's keys raise or lower it (Sandbox's HoverballEntity). </summary>
public sealed class SwrpHoverball : Component, ISwrpControllable
{
	[Property, Sync] public bool IsOn { get; set; } = true;
	[Property, Sync] public float TargetZ { get; set; }
	[Property, Sync] public float Speed { get; set; } = 1f;
	[Property, Sync] public float AirResistance { get; set; }
	[Property, Sync] public string UpKey { get; set; } = "Jump";
	[Property, Sync] public string DownKey { get; set; } = "Duck";

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		TargetZ = WorldPosition.z;
		if ( GetComponent<Rigidbody>() is { } rb ) rb.Gravity = !IsOn;
	}

	public void OnControl( Connection d )
	{
		if ( !IsOn ) return;
		var amount = Math.Clamp( SwrpContraption.Analog( d, UpKey ) - SwrpContraption.Analog( d, DownKey ), -1f, 1f );
		TargetZ += amount * Time.Delta * Time.Delta * 5000f * Speed;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !IsOn || GetComponent<Rigidbody>() is not { } rb ) return;
		if ( !rb.MotionEnabled ) { TargetZ = WorldPosition.z; return; }
		rb.Gravity = false;
		var vel = rb.Velocity;
		var targetVelZ = Math.Clamp( ( TargetZ - WorldPosition.z ) * 20f, -400f, 400f );
		var newVel = vel.WithZ( vel.z + ( targetVelZ - vel.z ) * Math.Min( Time.Delta * 15f * ( AirResistance + 1f ), 1f ) );
		if ( AirResistance > 0f )
		{
			var drag = Math.Min( AirResistance * Time.Delta * 5f, 1f );
			newVel = newVel.WithX( vel.x * ( 1f - drag ) ).WithY( vel.y * ( 1f - drag ) );
		}
		rb.Velocity = newVel;
	}
}

/// <summary> Spins a hinge: always on, or while the seated trooper holds its keys (Sandbox's MotorEntity). </summary>
public sealed class SwrpMotor : Component, ISwrpControllable
{
	[Property, Sync] public float Speed { get; set; } = 0.5f;
	[Property, Sync] public float Torque { get; set; } = 0.5f;
	[Property, Sync] public bool Reversed { get; set; }
	[Property, Sync] public bool AlwaysOn { get; set; } = true;
	[Property, Sync] public string ForwardKey { get; set; } = "Attack1";
	[Property, Sync] public string ReverseKey { get; set; } = "Attack2";

	const float MaxDegreesPerSecond = 720f;
	const float MaxTorqueScale = 200000f;
	TimeSince _sinceControl = 10;

	public void OnControl( Connection d )
	{
		_sinceControl = 0;
		Drive( SwrpContraption.Analog( d, ForwardKey ), SwrpContraption.Analog( d, ReverseKey ) );
	}

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost && _sinceControl > 0.1f ) Drive( 0, 0 );
	}

	void Drive( float forward, float reverse )
	{
		if ( !Networking.IsHost || GetComponent<HingeJoint>() is not { } joint ) return;
		var held = forward > 0.5f || reverse > 0.5f;
		if ( !held && !AlwaysOn ) { joint.Motor = HingeJoint.MotorMode.Disabled; return; }
		var direction = held ? ( forward > reverse ? 1f : -1f ) : ( Reversed ? -1f : 1f );
		joint.Motor = HingeJoint.MotorMode.TargetVelocity;
		joint.TargetVelocity = direction * Speed * MaxDegreesPerSecond;
		joint.MaxTorque = Torque * MaxTorqueScale;
	}
}
