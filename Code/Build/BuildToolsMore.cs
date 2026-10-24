using System;

namespace Swrp;

// The rest of Sandbox's tool gun modes (MIT, see LICENSE-facepunch-sandbox.txt): slider, motor, keep upright, gravity, trail,
// stacker, and the gadget placers (thruster, wheel, hoverball, balloon). Gadgets are driven from a seat (BuildGadgets.cs).
// Models are stock s&box ones, since Sandbox's own gadget models come from packages we don't ship.

sealed class SliderMode : ConstraintMode
{
	public override string Id => "slider";
	public override string Name => "Slider";
	public override string Description => Stage == 1 ? "Click the other end: it slides along the line between them" : "Lets two objects slide along a line";
	protected override float Value1 => 0;
	protected override float Value2 => 0;
	protected override bool Flag => false;
	protected override void Submit( BuildAim aim ) => SwrpToolgunMore.Joint2( "slider", FirstId, FirstLocal, FirstNormal, aim.Id, aim.Local, aim.Point );
}

sealed class MotorMode : ConstraintMode
{
	public static bool Flip;
	public static bool Reversed;
	public static bool AlwaysOn = true;
	public override string Id => "motor";
	public override string Name => "Motor";
	public override string Description => Stage == 1 ? "Click what it spins against (or the ship)" : "Click the face to spin around, then its base";
	protected override float Value1 => 0;
	protected override float Value2 => 0;
	protected override bool Flag => false;
	protected override void Submit( BuildAim aim ) => SwrpToolgunMore.Joint2( "motor", FirstId, FirstLocal, Flip ? -FirstNormal : FirstNormal, aim.Id, aim.Local, aim.Point, Reversed, AlwaysOn );
}

sealed class KeepUprightMode : BuildToolMode
{
	int _stage;
	Guid _first;
	Vector3 _firstLocal;
	public override string Id => "upright";
	public override string Name => "Keep upright";
	public override string Description => _stage == 1 ? "Right-click the object to keep it upright against" : "Stops an object tipping over";
	public override string Primary => "keep upright";
	public override string Secondary => _stage == 1 ? "link here" : "link to another object";
	public override string Reload => _stage == 1 ? "cancel" : "remove";
	public override void Reset() => _stage = 0;

	public override void Control( BuildAim aim )
	{
		Valid = aim.Target.IsValid();
		if ( Input.Pressed( "Reload" ) )
		{
			if ( _stage == 1 ) _stage = 0;
			else if ( Valid ) SwrpToolgun.RemoveConstraints( aim.Id, "upright", aim.Point );
			return;
		}
		if ( !Valid ) return;
		if ( Input.Pressed( "Attack1" ) ) { SwrpToolgunMore.Upright( aim.Id, aim.Local, Guid.Empty, default, aim.Point ); _stage = 0; }
		else if ( Input.Pressed( "Attack2" ) )
		{
			if ( _stage == 0 ) { _first = aim.Id; _firstLocal = aim.Local; _stage = 1; SwrpBuildTool.Zap( aim.Point ); }
			else { if ( aim.Id != _first ) SwrpToolgunMore.Upright( _first, _firstLocal, aim.Id, aim.Local, aim.Point ); _stage = 0; }
		}
	}
}

sealed class GravityMode : BuildToolMode
{
	public static float Value = 0f;
	public override string Id => "gravity";
	public override string Name => "Gravity";
	public override string Description => $"Sets how much gravity pulls a prop (×{Value:0.##})";
	public override string Primary => "set gravity";
	public override string Secondary => "copy gravity";
	public override string Reload => "normal gravity";

	public override void Control( BuildAim aim )
	{
		var rb = aim.Target?.Components.Get<Rigidbody>();
		Valid = rb.IsValid();
		if ( !Valid ) return;
		if ( Input.Pressed( "Attack1" ) ) SwrpToolgunMore.Gravity( aim.Id, Value, aim.Point );
		else if ( Input.Pressed( "Attack2" ) ) { Value = rb.GravityScale; SwrpBuildTool.Zap( aim.Point ); }
		else if ( Input.Pressed( "Reload" ) ) SwrpToolgunMore.Gravity( aim.Id, 1f, aim.Point );
	}
}

sealed class TrailMode : BuildToolMode
{
	public static float Width = 4f;
	public static float Life = 1f;
	public override string Id => "trail";
	public override string Name => "Trail";
	public override string Description => "Leaves a trail behind a moving prop (colour from the colour tool)";
	public override string Primary => "add trail";
	public override string Secondary => "remove trail";

	public override void Control( BuildAim aim )
	{
		Valid = aim.Target.IsValid();
		if ( !Valid ) return;
		if ( Input.Pressed( "Attack1" ) ) SwrpToolgunMore.Trail( aim.Id, ColourMode.Colour, Width, Life, aim.Point );
		else if ( Input.Pressed( "Attack2" ) ) SwrpToolgunMore.Trail( aim.Id, Color.White, 0f, 0f, aim.Point );
	}
}

sealed class StackerMode : BuildToolMode
{
	public static int Count = 1;
	public static int Direction;      // up, right, down, left, forward, back
	public static bool WorldAligned;
	static readonly string[] Names = { "up", "right", "down", "left", "forward", "back" };
	public override string Id => "stacker";
	public override string Name => "Stacker";
	public override string Description => $"Copies a prop {Count}× {Names[Direction]} ({( WorldAligned ? "world" : "object" )} axes)";
	public override string Primary => "stack";
	public override string Secondary => "world/object axes";
	public override string Reload => "next direction";

	public static Vector3 Axis( int dir ) => dir switch { 1 => Vector3.Right, 2 => Vector3.Down, 3 => Vector3.Left, 4 => Vector3.Forward, 5 => Vector3.Backward, _ => Vector3.Up };

	public override void Control( BuildAim aim )
	{
		if ( Input.Pressed( "Reload" ) ) Direction = ( Direction + 1 ) % 6;
		if ( Input.Pressed( "Attack2" ) ) WorldAligned = !WorldAligned;
		Valid = aim.Target.IsValid() && aim.Target.Tags.Has( SwrpBuild.PropTag ) && aim.Target.Components.Get<ModelRenderer>() is not null;
		if ( !Valid ) return;

		var target = aim.Target;
		var model = target.Components.Get<ModelRenderer>().Model;
		foreach ( var t in SwrpToolgunMore.StackTransforms( target, model, Count, Direction, WorldAligned ) )
			Game.ActiveScene.DebugOverlay.Model( model, color: new Color( 0.55f, 0.85f, 1f, 0.5f ), transform: t, castShadows: false );
		if ( Input.Pressed( "Attack1" ) ) SwrpToolgunMore.Stack( aim.Id, Count, Direction, WorldAligned, aim.Point );
	}
}

/// <summary> The gadget placers share a ghost preview at the placement transform. </summary>
abstract class GadgetMode : BuildToolMode
{
	protected abstract string ModelPath { get; }
	protected abstract Transform Placement( BuildAim aim, Model model );
	protected virtual bool NeedsTarget => false;

	protected void Ghost( BuildAim aim )
	{
		Valid = aim.Hit && ( !NeedsTarget || aim.Target.IsValid() );
		if ( !Valid ) return;
		var model = Model.Load( ModelPath );
		if ( model is null || model.IsError ) return;
		Game.ActiveScene.DebugOverlay.Model( model, color: new Color( 0.55f, 0.85f, 1f, 0.6f ), transform: Placement( aim, model ), castShadows: false );
	}
}

sealed class ThrusterMode : GadgetMode
{
	public static float Power = 0.5f;
	public static string Key = "Jump";
	public static string ReverseKey = "";
	public static bool AxisUp = true;
	public const string Model = "models/citizen_props/sodacan01.vmdl";
	public override string Id => "thruster";
	public override string Name => "Thruster";
	public override string Description => $"Pushes what it's on while you sit in a joined seat and hold {SwrpContraption.KeyLabel( Key )}";
	public override string Primary => "place (welded)";
	public override string Secondary => "place loose";
	public override string Reload => "turn it sideways";
	protected override string ModelPath => Model;

	protected override Transform Placement( BuildAim aim, Sandbox.Model model ) => SwrpToolgunMore.ThrusterPlacement( aim.Point, aim.Trace.Normal, AxisUp );

	public override void Control( BuildAim aim )
	{
		if ( Input.Pressed( "Reload" ) ) AxisUp = !AxisUp;
		Ghost( aim );
		if ( !Valid ) return;
		var weld = Input.Pressed( "Attack1" );
		if ( weld || Input.Pressed( "Attack2" ) )
			SwrpToolgunMore.Gadget( "thruster", aim.Id, aim.Point, aim.Trace.Normal, AxisUp, weld, Power, 0, Key, ReverseKey, "", "", false );
	}
}

sealed class WheelMode : GadgetMode
{
	public static float Speed = 0.5f;
	public static float Power = 0.5f;
	public static bool Steer;
	public static bool Reversed;
	public static bool AxisRight = true;
	public static string Path = "models/citizen_props/wheel01.vmdl";
	public override string Id => "wheel";
	public override string Name => "Wheel";
	public override string Description => $"A motor wheel: W/S drive it from a joined seat, Space brakes{( Steer ? ", A/D steer" : "" )}";
	public override string Primary => "place";
	public override string Secondary => "turn the axle";
	public override string Reload => Reversed ? "spin forwards" : "spin backwards";
	protected override string ModelPath => Path;
	protected override bool NeedsTarget => true;

	protected override Transform Placement( BuildAim aim, Model model ) => SwrpToolgunMore.WheelPlacement( aim.Point, aim.Trace.Normal, model, AxisRight );

	public override void Control( BuildAim aim )
	{
		if ( Input.Pressed( "Attack2" ) ) AxisRight = !AxisRight;
		if ( Input.Pressed( "Reload" ) ) Reversed = !Reversed;
		Ghost( aim );
		if ( Valid && Input.Pressed( "Attack1" ) )
			SwrpToolgunMore.Gadget( "wheel:" + Path, aim.Id, aim.Point, aim.Trace.Normal, AxisRight, true, Power, Speed, "Forward", "Backward",
				Steer ? "Left" : "", Steer ? "Right" : "", Reversed );
	}
}

sealed class HoverballMode : GadgetMode
{
	public static float Speed = 1f;
	public static float Resistance;
	public const string Model = "models/dev/sphere.vmdl";
	public override string Id => "hoverball";
	public override string Name => "Hoverball";
	public override string Description => "Holds its height; Space/Ctrl raise and lower it from a joined seat";
	public override string Primary => "place (welded)";
	public override string Secondary => "place loose";
	protected override string ModelPath => Model;

	protected override Transform Placement( BuildAim aim, Sandbox.Model model ) =>
		new( aim.Point + aim.Trace.Normal * 8f, Rotation.Identity, 0.16f );

	public override void Control( BuildAim aim )
	{
		Ghost( aim );
		if ( !Valid ) return;
		var weld = Input.Pressed( "Attack1" );
		if ( weld || Input.Pressed( "Attack2" ) )
			SwrpToolgunMore.Gadget( "hoverball", aim.Id, aim.Point, aim.Trace.Normal, true, weld, Resistance, Speed, "Jump", "Duck", "", "", false );
	}
}

sealed class BalloonMode : GadgetMode
{
	public static float Force = 1f;
	public static float Length = 50f;
	public static string Path = "models/citizen_props/balloonregular01.vmdl";
	public override string Id => "balloon";
	public override string Name => "Balloon";
	public override string Description => "Floats up, tied on a rope (colour from the colour tool)";
	public override string Primary => "place on a rope";
	public override string Secondary => "place loose";
	protected override string ModelPath => Path;

	protected override Transform Placement( BuildAim aim, Model model ) => new( aim.Point + Vector3.Up * Length, Rotation.Identity );

	public override void Control( BuildAim aim )
	{
		Ghost( aim );
		if ( !Valid ) return;
		var rope = Input.Pressed( "Attack1" );
		if ( rope || Input.Pressed( "Attack2" ) )
			SwrpToolgunMore.Balloon( Path, aim.Id, aim.Local, aim.Point, rope, Force, Length, ColourMode.Colour );
	}
}

// ----- host -----

public static class SwrpToolgunMore
{
	static GameObject Target( Guid id ) => id == Guid.Empty ? null : SwrpBuild.Grabbable( Game.ActiveScene?.Directory.FindByGuid( id ) );

	static bool Allowed( out Connection caller )
	{
		caller = Rpc.Caller;
		return SwrpBuild.May( caller );
	}

	static void Done( Connection caller, string what, Vector3 at, params GameObject[] made )
	{
		if ( made.Length > 0 ) SwrpBuildUndo.Add( caller, what, made );
		SwrpBuild.Log( caller, what );
		SwrpToolgun.ZapAll( caller.Id, at );
	}

	static (GameObject a, GameObject b) Pair( string kind, GameObject first, Vector3 localA, GameObject second, Vector3 localB )
	{
		var go1 = new GameObject( false, kind );
		go1.Parent = first;
		go1.LocalPosition = localA;
		var go2 = new GameObject( false, kind );
		if ( second is not null ) { go2.Parent = second; go2.LocalPosition = localB; }
		else go2.WorldPosition = localB;
		var link = go1.AddComponent<SwrpConstraintLink>();
		link.Attachment = go2;
		link.Kind = kind;
		return (go1, go2);
	}

	/// <summary> Slider or motor between two things (second may be the ship: <paramref name="b"/> empty, <paramref name="localB"/> world). </summary>
	[Rpc.Host]
	public static void Joint2( string kind, Guid a, Vector3 localA, Vector3 normalA, Guid b, Vector3 localB, Vector3 at, bool reversed = false, bool alwaysOn = true )
	{
		if ( !Allowed( out var caller ) ) return;
		var first = Target( a );
		var second = Target( b );
		if ( first?.Components.Get<Rigidbody>() is null || a == b || ( b != Guid.Empty && second is null ) ) return;

		var (go1, go2) = Pair( kind, first, localA, second, localB );
		if ( kind == "slider" )
		{
			var axis = Rotation.LookAt( Vector3.Direction( go1.WorldPosition, go2.WorldPosition ) );
			go1.WorldRotation = axis;
			go2.WorldRotation = axis;
			var joint = go1.AddComponent<SliderJoint>();
			joint.Body = go2;
			joint.MinLength = 0;
			joint.MaxLength = go1.WorldPosition.Distance( go2.WorldPosition );
			joint.EnableCollision = true;
			var line = go1.AddComponent<LineRenderer>();
			line.Points = new List<GameObject> { go1, go2 };
			line.Width = 0.5f;
			line.Color = Color.Black;
		}
		else if ( kind == "motor" )
		{
			// the hinge spins around its anchor's up: the face you clicked first
			var normal = normalA.Normal;
			var reference = MathF.Abs( normal.Dot( Vector3.Up ) ) > 0.99f ? Vector3.Forward : Vector3.Up;
			var axisRotation = Rotation.LookAt( Vector3.Cross( reference, normal ).Normal, normal );
			go1.WorldRotation = axisRotation;
			go2.WorldTransform = go1.WorldTransform;
			var joint = go1.AddComponent<HingeJoint>();
			joint.Attachment = Sandbox.Joint.AttachmentMode.Auto;
			joint.Body = go2;
			joint.EnableCollision = false;
			var motor = go1.AddComponent<SwrpMotor>();
			motor.Reversed = reversed;
			motor.AlwaysOn = alwaysOn;
		}
		else { go1.Destroy(); go2.Destroy(); return; }

		go2.NetworkSpawn();
		go1.NetworkSpawn();
		Done( caller, $"{kind}: {first.Name} to {second?.Name ?? "the ship"}", at, go1, go2 );
	}

	/// <summary> Keep upright: against the world (<paramref name="b"/> empty) or linked to another object. </summary>
	[Rpc.Host]
	public static void Upright( Guid a, Vector3 localA, Guid b, Vector3 localB, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( a ) is not { } first || first.Components.Get<Rigidbody>() is not { } rb ) return;
		var second = Target( b );
		if ( b != Guid.Empty && ( second is null || second == first ) ) return;
		const float torqueScale = 10f, torqueMultiplier = 5000f;

		if ( second is null )
		{
			var go = new GameObject( first, false, "upright" );
			go.LocalPosition = localA;
			var link = go.AddComponent<SwrpConstraintLink>();
			link.Kind = "upright";
			link.Standalone = true;
			var joint = go.AddComponent<UprightJoint>();
			joint.Body = null;
			joint.Hertz = 2f;
			joint.DampingRatio = 0.7f;
			joint.MaxTorque = torqueMultiplier * rb.Mass * torqueScale;
			go.NetworkSpawn();
			Done( caller, $"keep upright: {first.Name}", at, go );
			return;
		}

		var (go1, go2) = Pair( "upright", first, localA, second, localB );
		go1.WorldTransform = go2.WorldTransform;
		var j = go1.AddComponent<UprightJoint>();
		j.Body = go2;
		j.Hertz = 2f;
		j.DampingRatio = 0.7f;
		j.MaxTorque = torqueMultiplier * rb.Mass * torqueScale;
		go2.NetworkSpawn();
		go1.NetworkSpawn();
		Done( caller, $"keep upright: {first.Name} to {second.Name}", at, go1, go2 );
	}

	[Rpc.Host]
	public static void Gravity( Guid id, float scale, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id )?.Components.Get<Rigidbody>() is not { } rb ) return;
		rb.GravityScale = Math.Clamp( scale, -4f, 4f );
		SwrpToolgun.ZapAll( caller.Id, at );
	}

	/// <summary> <paramref name="width"/> 0 removes the trail. </summary>
	[Rpc.Host]
	public static void Trail( Guid id, Color colour, float width, float life, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id ) is not { } target ) return;
		target.Components.Get<TrailRenderer>()?.Destroy();
		if ( width > 0f )
		{
			var trail = target.AddComponent<TrailRenderer>();
			trail.Color = new Gradient( new Gradient.ColorFrame( 0, colour ), new Gradient.ColorFrame( 1, colour.WithAlpha( 0 ) ) );
			trail.Width = new Curve( new Curve.Frame( 0, width ), new Curve.Frame( 1, 0 ) );
			trail.LifeTime = Math.Clamp( life, 0.1f, 10f );
			trail.Face = SceneLineObject.FaceMode.Camera;
		}
		target.Network.Refresh();
		SwrpToolgun.ZapAll( caller.Id, at );
	}

	public static IEnumerable<Transform> StackTransforms( GameObject target, Model model, int count, int direction, bool world )
	{
		var axis = StackerMode.Axis( direction );
		var bounds = model.Bounds;
		var size = bounds.Size * target.WorldScale;
		var step = MathF.Abs( axis.x ) * size.x + MathF.Abs( axis.y ) * size.y + MathF.Abs( axis.z ) * size.z;
		var dir = world ? axis : target.WorldRotation * axis;
		for ( var i = 1; i <= count; i++ )
			yield return new Transform( target.WorldPosition + dir * step * i, target.WorldRotation, target.WorldScale );
	}

	[Rpc.Host]
	public static void Stack( Guid id, int count, int direction, bool world, Vector3 at )
	{
		if ( !Allowed( out var caller ) || Target( id ) is not { } target || !target.Tags.Has( SwrpBuild.PropTag ) ) return;
		var renderer = target.Components.Get<ModelRenderer>();
		if ( renderer?.Model is not { } model ) return;
		var made = new List<GameObject>();
		foreach ( var t in StackTransforms( target, model, Math.Clamp( count, 1, 10 ), direction, world ) )
		{
			var go = SwrpBuild.CreateProp( model, target.Name, t.Position, t.Rotation, true, g =>
			{
				g.WorldScale = t.Scale;
				g.Components.Get<ModelRenderer>().Tint = renderer.Tint;
			} );
			if ( go is not null ) made.Add( go );
		}
		if ( made.Count > 0 ) Done( caller, $"stacked {made.Count}× {target.Name}", at, made.ToArray() );
	}

	public static Transform ThrusterPlacement( Vector3 point, Vector3 normal, bool axisUp ) =>
		new( point, Rotation.LookAt( normal ) * ( axisUp ? new Angles( 90, 0, 0 ) : new Angles( -90, 0, 0 ) ) );

	public static Transform WheelPlacement( Vector3 point, Vector3 normal, Model model, bool axisRight )
	{
		var surface = Rotation.LookAt( normal );
		var offset = model.Bounds.Size.y * 0.5f;
		return new Transform( point + normal * offset, Rotation.LookAt( normal, surface * ( axisRight ? Vector3.Right : Vector3.Up ) ) * new Angles( 0, 90, 0 ) );
	}

	/// <summary>
	/// Place a thruster, wheel or hoverball on what you aim at (<paramref name="kind"/> "wheel:&lt;model&gt;" for wheels). Welded ones
	/// hold to the target with a rigid joint (Sandbox's local-frame weld); on the ship they're placed frozen.
	/// </summary>
	[Rpc.Host]
	public static void Gadget( string kind, Guid on, Vector3 point, Vector3 normal, bool axis, bool weld, float power, float speed,
		string key1, string key2, string key3, string key4, bool reversed )
	{
		if ( !Allowed( out var caller ) ) return;
		var target = Target( on );
		if ( on != Guid.Empty && target is null ) return;
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !me.IsValid() || me.WorldPosition.Distance( point ) > SwrpPhysgun.Range + 200f ) return;
		bool KeyOk( string k ) => SwrpContraption.Keys.Any( x => x.Action == ( k ?? "" ) );
		if ( !KeyOk( key1 ) || !KeyOk( key2 ) || !KeyOk( key3 ) || !KeyOk( key4 ) ) return;

		string modelPath;
		Transform tx;
		string name;
		if ( kind == "thruster" ) { modelPath = ThrusterMode.Model; tx = ThrusterPlacement( point, normal, axis ); name = "Thruster"; }
		else if ( kind == "hoverball" ) { modelPath = HoverballMode.Model; tx = new Transform( point + normal * 8f, Rotation.Identity, 0.16f ); name = "Hoverball"; }
		else if ( kind.StartsWith( "wheel:" ) && target is not null )
		{
			modelPath = kind[6..];
			if ( !modelPath.StartsWith( "models/citizen_props/wheel" ) ) return;
			var m = Model.Load( modelPath );
			if ( m is null || m.IsError ) return;
			tx = WheelPlacement( point, normal, m, axis );
			name = "Wheel";
		}
		else return;

		var model = Model.Load( modelPath );
		if ( model is null || model.IsError ) return;
		var weldTo = weld ? target : null;

		var go = SwrpBuild.CreateProp( model, name, tx.Position, tx.Rotation, target is null, g =>
		{
			g.WorldScale = tx.Scale;
			if ( kind == "thruster" )
			{
				var t = g.AddComponent<SwrpThruster>();
				t.Power = Math.Clamp( power, 0f, 1f );
				t.Key = key1;
				t.ReverseKey = key2;
			}
			else if ( kind == "hoverball" )
			{
				var h = g.AddComponent<SwrpHoverball>();
				h.AirResistance = Math.Clamp( power, 0f, 10f );
				h.Speed = Math.Clamp( speed, 0f, 20f );
				h.UpKey = key1;
				h.DownKey = key2;
			}
			else
			{
				var w = g.AddComponent<SwrpWheelDrive>();
				w.Power = Math.Clamp( power, 0f, 1f );
				w.Speed = Math.Clamp( speed, 0f, 1f );
				w.Reversed = reversed;
				w.ForwardKey = key1;
				w.ReverseKey = key2;
				w.LeftKey = key3;
				w.RightKey = key4;
				var anchor = new GameObject( g, true, "axle" );
				anchor.LocalRotation = new Angles( 0, 90, 90 );
				var joint = anchor.AddComponent<WheelJoint>();
				joint.Attachment = Sandbox.Joint.AttachmentMode.Auto;
				joint.EnableSuspension = true;
				joint.EnableSuspensionLimit = true;
				joint.SuspensionLimits = new Vector2( -32, 32 );
				joint.EnableCollision = false;
				joint.Body = target;
				return;
			}

			if ( weldTo is null ) return;
			var fixedJoint = g.AddComponent<FixedJoint>();
			fixedJoint.Attachment = Sandbox.Joint.AttachmentMode.LocalFrames;
			fixedJoint.LocalFrame2 = weldTo.WorldTransform.WithScale( 1 ).ToLocal( new Transform( tx.Position, tx.Rotation ) );
			fixedJoint.LocalFrame1 = new Transform();
			fixedJoint.AngularFrequency = 0;
			fixedJoint.LinearFrequency = 0;
			fixedJoint.Body = weldTo;
			fixedJoint.EnableCollision = false;
		} );
		if ( go is null ) return;
		Done( caller, $"{name.ToLowerInvariant()} on {target?.Name ?? "the ship"}", point, go );
	}

	[Rpc.Host]
	public static void Balloon( string model, Guid on, Vector3 local, Vector3 point, bool rope, float force, float length, Color colour )
	{
		if ( !Allowed( out var caller ) || !model.StartsWith( "models/citizen_props/balloon" ) ) return;
		var target = Target( on );
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !me.IsValid() || me.WorldPosition.Distance( point ) > SwrpPhysgun.Range + 200f ) return;
		var mdl = Model.Load( model );
		if ( mdl is null || mdl.IsError ) return;
		length = Math.Clamp( length, 10f, 500f );

		GameObject anchor = null;
		var go = SwrpBuild.CreateProp( mdl, "Balloon", point + Vector3.Up * ( rope ? length : 20f ), Rotation.Identity, false, g =>
		{
			g.Components.Get<ModelRenderer>().Tint = colour;
			g.Components.Get<Rigidbody>().GravityScale = -Math.Clamp( force, 0.1f, 4f );
			if ( !rope ) return;
			anchor = new GameObject( false, "balloon rope" );
			if ( target is not null ) { anchor.Parent = target; anchor.LocalPosition = local; }
			else anchor.WorldPosition = point;
			var link = g.AddComponent<SwrpConstraintLink>();
			link.Attachment = anchor;
			link.Kind = "rope";
			var joint = g.AddComponent<SpringJoint>();
			joint.Body = anchor;
			joint.MinLength = 0;
			joint.MaxLength = length;
			joint.RestLength = length;
			joint.Frequency = 0;
			joint.Damping = 0;
			joint.EnableCollision = true;
			var verlet = g.AddComponent<VerletRope>();
			verlet.Attachment = anchor;
			verlet.SegmentCount = Math.Clamp( MathX.CeilToInt( length / 16f ), 2, 48 );
			verlet.Radius = 0.5f;
			var line = g.AddComponent<LineRenderer>();
			line.Points = new List<GameObject> { g, anchor };
			line.Width = 0.5f;
			line.Color = Color.White;
			line.SplineInterpolation = 4;
			line.Face = SceneLineObject.FaceMode.Cylinder;
			anchor.NetworkSpawn();
		} );
		if ( go is null ) return;
		Done( caller, $"balloon on {target?.Name ?? "the ship"}", point, anchor is null ? new[] { go } : new[] { go, anchor } );
	}
}
