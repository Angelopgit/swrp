using System;

namespace Swrp;

/// <summary>
/// The tool gun, adapted from Facepunch's Sandbox tool gun and its tool modes (MIT, see LICENSE-facepunch-sandbox.txt). Each mode is
/// a plain class run by the local <see cref="SwrpBuildTool"/>: it traces, keeps its stage, and asks the host through
/// <see cref="SwrpToolgun"/>, which checks staff rights and that both ends are things the build tools may touch
/// (<see cref="SwrpBuild.Grabbable"/>; constraints may also anchor to the map). Pick the mode in the Q menu.
/// Ported: remover, weld, no-collide, ball socket, rope, elastic, colour, resizer, mass. Left out for now: the ones that spawn
/// Sandbox's own entities (balloon, thruster, wheel, hoverball, button, emitter, hydraulic, winch, motor), the duplicator and stacker.
/// </summary>
public abstract class BuildToolMode
{
	public abstract string Id { get; }
	public abstract string Name { get; }
	public abstract string Description { get; }
	public virtual string Primary => null;
	public virtual string Secondary => null;
	public virtual string Reload => null;

	/// <summary> Set each frame: false paints the crosshair red (nothing valid under it). </summary>
	public bool Valid { get; protected set; } = true;

	public virtual void Reset() { }
	public abstract void Control( BuildAim aim );

	/// <summary> Every mode. (A new field name on purpose: hot-reload keeps a static field's old value, so growing the old
	/// array left new modes falling back to the remover.) </summary>
	public static BuildToolMode[] All => _modesV3;

	static readonly BuildToolMode[] _modesV3 =
	{
		new RemoverMode(), new WeldMode(), new NoCollideMode(), new BallSocketMode(), new RopeMode(), new ElasticMode(),
		new SliderMode(), new MotorMode(), new KeepUprightMode(), new ColourMode(), new ResizerMode(), new MassMode(), new GravityMode(),
		new TrailMode(), new StackerMode(), new ThrusterMode(), new WheelMode(), new HoverballMode(), new BalloonMode(), new CameraMode(),
	};

	public static BuildToolMode Get( string id ) => All.FirstOrDefault( m => m.Id == id ) ?? All[0];
}

/// <summary> Where the local trooper is aiming, and what that hits (a buildable root object, the map, or nothing). </summary>
public readonly struct BuildAim
{
	public readonly Vector3 Eye;
	public readonly Vector3 Forward;
	public readonly SceneTraceResult Trace;
	public readonly GameObject Target;      // a staff prop, NPC or dropped item (root), or null
	public readonly bool IsWorld;           // hit the ship itself (constraints can anchor to it)

	public BuildAim( Vector3 eye, Vector3 forward, SceneTraceResult tr, GameObject target, bool world )
	{
		Eye = eye;
		Forward = forward;
		Trace = tr;
		Target = target;
		IsWorld = world;
	}

	public bool Hit => Target.IsValid() || IsWorld;
	public Vector3 Point => Trace.EndPosition;

	/// <summary> The hit point local to the target, or the world point when it's the map. </summary>
	public Vector3 Local => Target.IsValid() ? Target.WorldTransform.PointToLocal( Trace.EndPosition ) : Trace.EndPosition;
	public Guid Id => Target.IsValid() ? Target.Id : Guid.Empty;
}

// ----- the modes (client) -----

sealed class RemoverMode : BuildToolMode
{
	public override string Id => "remover";
	public override string Name => "Remover";
	public override string Description => "Deletes a prop, NPC or dropped item";
	public override string Primary => "remove";

	public override void Control( BuildAim aim )
	{
		Valid = aim.Target.IsValid();
		if ( Valid && Input.Pressed( "Attack1" ) ) SwrpToolgun.Remove( aim.Id, aim.Point );
	}
}

/// <summary> Two clicks join two things (Sandbox's BaseConstraintToolMode); right mouse cancels, R removes this kind from what you aim at. </summary>
abstract class ConstraintMode : BuildToolMode
{
	protected int Stage;
	protected Guid FirstId;
	protected Vector3 FirstLocal;
	protected Vector3 FirstNormal;

	public override string Primary => Stage == 1 ? "attach here" : "pick the first object";
	public override string Secondary => Stage == 1 ? "cancel" : null;
	public override string Reload => "remove from object";
	public override void Reset() => Stage = 0;

	/// <summary> The second click: ask the host for the constraint. </summary>
	protected virtual void Submit( BuildAim aim ) => SwrpToolgun.Constrain( Id, FirstId, FirstLocal, aim.Id, aim.Local, Value1, Value2, Flag, aim.Point );

	protected abstract float Value1 { get; }
	protected abstract float Value2 { get; }
	protected abstract bool Flag { get; }

	public override void Control( BuildAim aim )
	{
		if ( Input.Pressed( "Attack2" ) ) { Stage = 0; return; }

		if ( Input.Pressed( "Reload" ) && aim.Target.IsValid() )
		{
			SwrpToolgun.RemoveConstraints( aim.Id, Id, aim.Point );
			return;
		}

		// the first end must be something that moves; the second may be the ship
		Valid = Stage == 0 ? aim.Target.IsValid() : aim.Hit && aim.Id != FirstId;
		if ( !Valid || !Input.Pressed( "Attack1" ) ) return;

		if ( Stage == 0 )
		{
			FirstId = aim.Id;
			FirstLocal = aim.Local;
			FirstNormal = aim.Trace.Normal;
			Stage = 1;
			SwrpBuildTool.Zap( aim.Point );
			return;
		}

		Submit( aim );
		Stage = 0;
	}
}

sealed class WeldMode : ConstraintMode
{
	public static bool Rigid = true;
	public override string Id => "weld";
	public override string Name => "Weld";
	public override string Description => Stage == 1 ? "Click what to weld it to (or the ship)" : "Welds two objects together";
	protected override float Value1 => 0;
	protected override float Value2 => 0;
	protected override bool Flag => Rigid;
}

sealed class NoCollideMode : ConstraintMode
{
	public override string Id => "nocollide";
	public override string Name => "No collide";
	public override string Description => Stage == 1 ? "Click the second object" : "Two objects stop colliding with each other";
	protected override float Value1 => 0;
	protected override float Value2 => 0;
	protected override bool Flag => false;
}

sealed class BallSocketMode : ConstraintMode
{
	public static bool Collide;
	public override string Id => "ballsocket";
	public override string Name => "Ball socket";
	public override string Description => Stage == 1 ? "Click where it pivots (or the ship)" : "Joins two objects at a pivot";
	protected override float Value1 => 0;
	protected override float Value2 => 0;
	protected override bool Flag => Collide;
}

sealed class RopeMode : ConstraintMode
{
	public static float Slack;
	public static bool Rigid;
	public override string Id => "rope";
	public override string Name => "Rope";
	public override string Description => Stage == 1 ? "Click the other end (or the ship)" : "Ties a rope between two points";
	protected override float Value1 => Slack;
	protected override float Value2 => 0;
	protected override bool Flag => Rigid;
}

sealed class ElasticMode : ConstraintMode
{
	public static float Frequency = 2f;
	public static float Damping = 0.1f;
	public override string Id => "elastic";
	public override string Name => "Elastic";
	public override string Description => Stage == 1 ? "Click the other end (or the ship)" : "A springy band between two points";
	protected override float Value1 => Frequency;
	protected override float Value2 => Damping;
	protected override bool Flag => false;
}

sealed class ColourMode : BuildToolMode
{
	public static Color Colour = new( 0.85f, 0.25f, 0.2f );
	public override string Id => "colour";
	public override string Name => "Colour";
	public override string Description => "Tints a prop";
	public override string Primary => "paint";
	public override string Secondary => "reset";

	public override void Control( BuildAim aim )
	{
		Valid = aim.Target.IsValid();
		if ( !Valid ) return;
		if ( Input.Pressed( "Attack1" ) ) SwrpToolgun.Paint( aim.Id, Colour, aim.Point );
		else if ( Input.Pressed( "Attack2" ) ) SwrpToolgun.Paint( aim.Id, Color.White, aim.Point );
	}
}

sealed class ResizerMode : BuildToolMode
{
	TimeSince _since;
	public override string Id => "resizer";
	public override string Name => "Resizer";
	public override string Description => "Grows or shrinks a prop";
	public override string Primary => "grow (hold)";
	public override string Secondary => "shrink (hold)";
	public override string Reload => "reset size";

	public override void Control( BuildAim aim )
	{
		Valid = aim.Target.IsValid();
		if ( !Valid ) return;
		if ( Input.Pressed( "Reload" ) ) { SwrpToolgun.Resize( aim.Id, 0f, aim.Point ); return; }
		if ( _since < 0.03f ) return;
		var step = Input.Down( "Attack1" ) ? 0.033f : Input.Down( "Attack2" ) ? -0.033f : 0f;
		if ( step == 0f ) return;
		_since = 0;
		SwrpToolgun.Resize( aim.Id, step, aim.Point );
	}
}

sealed class MassMode : BuildToolMode
{
	public static float Value = 100f;
	public override string Id => "mass";
	public override string Name => "Mass";
	public override string Description => $"Sets a prop's mass ({Value:0} kg)";
	public override string Primary => "set mass";
	public override string Secondary => "copy mass";
	public override string Reload => "reset mass";

	public override void Control( BuildAim aim )
	{
		var rb = aim.Target?.Components.Get<Rigidbody>();
		Valid = rb.IsValid();
		if ( !Valid ) return;
		if ( Input.Pressed( "Attack1" ) ) SwrpToolgun.SetMass( aim.Id, Value, aim.Point );
		else if ( Input.Pressed( "Attack2" ) ) { Value = Math.Clamp( MathF.Round( rb.Mass ), 1f, 250f ); SwrpBuildTool.Zap( aim.Point ); }
		else if ( Input.Pressed( "Reload" ) ) SwrpToolgun.SetMass( aim.Id, 0f, aim.Point );
	}
}

// ----- host -----

/// <summary> The tool gun's host side: every action checks staff rights and its targets again, then logs. </summary>
public static class SwrpToolgun
{
	static GameObject Target( Guid id ) => id == Guid.Empty ? null : SwrpBuild.Grabbable( Game.ActiveScene?.Directory.FindByGuid( id ) );

	static bool Allowed( out Connection caller )
	{
		caller = Rpc.Caller;
		return SwrpBuild.May( caller );
	}

	[Rpc.Host]
	public static void Remove( Guid id, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id ) is not { } target ) return;
		SwrpBuild.Log( caller, $"removed a {target.Name}" );
		target.Destroy();
		Zap( caller.Id, at );
	}

	[Rpc.Host]
	public static void Paint( Guid id, Color colour, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id ) is not { } target ) return;
		foreach ( var r in target.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) ) r.Tint = colour;
		target.Network.Refresh();
		Zap( caller.Id, at );
	}

	/// <summary> <paramref name="step"/> 0 resets to normal size. </summary>
	[Rpc.Host]
	public static void Resize( Guid id, float step, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id ) is not { } target ) return;
		if ( step == 0f ) { target.WorldScale = Vector3.One; Zap( caller.Id, at ); return; }
		var scale = target.WorldScale + step;
		if ( scale.Length < 0.1f || scale.Length > 20f ) return;
		target.WorldScale = Vector3.Max( scale, 0.01f );
	}

	/// <summary> <paramref name="mass"/> 0 goes back to the model's own mass. </summary>
	[Rpc.Host]
	public static void SetMass( Guid id, float mass, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id )?.Components.Get<Rigidbody>() is not { } rb ) return;
		rb.MassOverride = Math.Clamp( mass, 0f, 50000f );
		Zap( caller.Id, at );
	}

	/// <summary>
	/// Join two things. <paramref name="b"/> empty means the ship: then <paramref name="localB"/> is a world point. The joint lives on
	/// two small child objects (one on each end), with a <see cref="SwrpConstraintLink"/> so removing either end removes the pair.
	/// </summary>
	[Rpc.Host]
	public static void Constrain( string kind, Guid a, Vector3 localA, Guid b, Vector3 localB, float value1, float value2, bool flag, Vector3 at )
	{
		if ( !Allowed( out var caller ) ) return;
		var first = Target( a );
		var second = Target( b );
		if ( first is null || a == b || ( b != Guid.Empty && second is null ) ) return;
		if ( first.Components.Get<Rigidbody>() is null && kind != "nocollide" ) return;

		var go1 = new GameObject( false, kind );
		go1.Parent = first;
		go1.LocalPosition = localA;
		go1.WorldRotation = Rotation.Identity;

		var go2 = new GameObject( false, kind );
		if ( second is not null )
		{
			go2.Parent = second;
			go2.LocalPosition = localB;
		}
		else go2.WorldPosition = localB;      // anchored to the ship: a loose marker with no body
		go2.WorldRotation = Rotation.Identity;

		var link = go1.AddComponent<SwrpConstraintLink>();
		link.Attachment = go2;
		link.Kind = kind;

		var length = go1.WorldPosition.Distance( go2.WorldPosition );
		switch ( kind )
		{
			case "weld":
			{
				var joint = go1.AddComponent<FixedJoint>();
				joint.Attachment = Joint.AttachmentMode.Auto;
				joint.Body = go2;
				joint.EnableCollision = true;
				joint.AngularFrequency = flag ? 0 : 10;
				joint.LinearFrequency = flag ? 0 : 10;
				break;
			}
			case "nocollide":
			{
				if ( second is null ) { go1.Destroy(); go2.Destroy(); return; }
				var filter = go1.AddComponent<PhysicsFilter>();
				filter.Body = second;
				break;
			}
			case "ballsocket":
			{
				// the pivot is the second click: move the first end there
				go1.WorldPosition = go2.WorldPosition;
				var joint = go1.AddComponent<BallJoint>();
				joint.Body = go2;
				joint.Friction = 0f;
				joint.EnableCollision = flag;
				break;
			}
			case "rope":
			{
				var len = MathF.Max( 1f, length + Math.Clamp( value1, -500f, 500f ) );
				var joint = go1.AddComponent<SpringJoint>();
				joint.Body = go2;
				joint.MinLength = flag ? len : 0;
				joint.MaxLength = len;
				joint.RestLength = len;
				joint.Frequency = 0;
				joint.Damping = 0;
				joint.EnableCollision = true;
				Line( go1, go2, len, !flag, 1f, Color.White, "materials/default/rope01.vmat" );
				break;
			}
			case "elastic":
			{
				var joint = go1.AddComponent<SpringJoint>();
				joint.Body = go2;
				joint.MinLength = 0;
				joint.MaxLength = float.MaxValue;
				joint.RestLength = length;
				joint.Frequency = Math.Clamp( value1, 0f, 15f );
				joint.Damping = Math.Clamp( value2, 0f, 4f );
				joint.EnableCollision = true;
				Line( go1, go2, length, true, 0.5f, new Color( 1f, 0.6f, 0.15f ), null );
				break;
			}
			default:
				go1.Destroy();
				go2.Destroy();
				return;
		}

		go2.NetworkSpawn();
		go1.NetworkSpawn();
		SwrpBuildUndo.Add( caller, kind, go1, go2 );
		SwrpBuild.Log( caller, $"{kind}: {first.Name} to {second?.Name ?? "the ship"}" );
		Zap( caller.Id, at );
	}

	static void Line( GameObject go1, GameObject go2, float length, bool sag, float radius, Color colour, string material )
	{
		var segments = Math.Clamp( MathX.CeilToInt( length / 16f ), 2, 48 );
		if ( sag )
		{
			var rope = go1.AddComponent<VerletRope>();
			rope.Attachment = go2;
			rope.SegmentCount = segments;
			rope.Radius = radius;
		}
		var line = go1.AddComponent<LineRenderer>();
		line.Points = new List<GameObject> { go1, go2 };
		line.Width = radius;
		line.Color = colour;
		line.Lighting = true;
		line.CastShadows = true;
		line.SplineInterpolation = sag ? 4 : 0;
		if ( material is not null )
			line.Texturing = line.Texturing with { Material = Material.Load( material ), WorldSpace = true, UnitsPerTexture = 32 };
		line.Face = SceneLineObject.FaceMode.Cylinder;
	}

	/// <summary> R with a constraint tool: remove that kind of constraint from the object you aim at (both ends go). </summary>
	[Rpc.Host]
	public static void RemoveConstraints( Guid id, string kind, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id ) is not { } target ) return;
		var gone = 0;
		foreach ( var link in Game.ActiveScene.GetAllComponents<SwrpConstraintLink>().ToArray() )
		{
			if ( link.Kind != kind ) continue;
			var mine = link.GameObject.Root == target || link.Attachment.IsValid() && link.Attachment.Root == target;
			if ( !mine ) continue;
			link.GameObject.Destroy();
			gone++;
		}
		if ( gone > 0 ) Zap( caller.Id, at );
		SwrpBuild.Notify( caller, gone == 0 ? $"No {kind} on that" : $"Removed {gone} {kind}{( gone == 1 ? "" : "s" )}" );
	}

	/// <summary> Host: show <paramref name="owner"/>'s tracer to everyone. </summary>
	public static void ZapAll( Guid owner, Vector3 at ) => Zap( owner, at );

	/// <summary> Everyone: the tool gun's tracer from the user's hand to the point it hit. </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Zap( Guid owner, Vector3 at )
	{
		var holder = SwrpPlayers.Valid.FirstOrDefault( p => p.Network.Owner?.Id == owner );
		SwrpBuildTool.ZapFrom( holder, at );
	}
}

/// <summary>
/// One end of a tool gun constraint (Sandbox's ConstraintCleanup): destroying it destroys the other end, and when the other end
/// goes (its object removed) this end takes its joint, rope and line with it.
/// </summary>
public sealed class SwrpConstraintLink : Component
{
	[Property, Sync] public GameObject Attachment { get; set; }
	[Property, Sync] public string Kind { get; set; }

	/// <summary> A one-ended constraint (keep upright against the world): nothing to wait for at the other end. </summary>
	[Property, Sync] public bool Standalone { get; set; }

	protected override void OnDestroy()
	{
		if ( !IsProxy && Attachment.IsValid() ) Attachment.Destroy();
		base.OnDestroy();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || Standalone || Attachment.IsValid() ) return;
		DestroyGameObject();
	}
}
