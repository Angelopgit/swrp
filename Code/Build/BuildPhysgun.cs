using System;
using Sandbox.Physics;

namespace Swrp;

/// <summary>
/// The physgun's host side, adapted from Facepunch's Sandbox physgun (MIT, see LICENSE-facepunch-sandbox.txt). Sandbox runs the
/// grab on the holder's machine; SWRP is host-authoritative, so the holder's machine sends its aim and the host drives the held
/// body with the same control joint (a keyframed anchor at the aim point, springs 32/4 linear and 64/4 angular). Beam grabs hold
/// the point you clicked; gravity holds (right mouse pulls a loose prop in) hold its centre of mass in front of you.
/// What can be grabbed is <see cref="SwrpBuild.Grabbable"/>: staff props, NPCs and dropped items, never the map, doors or players.
/// </summary>
public static class SwrpPhysgun
{
	public const float Range = 4096f;
	const float PullForce = 1000f;
	const float LaunchForce = 2000f;
	public const float PullDistance = 200f;

	internal sealed class Hold
	{
		public Connection Owner;
		public GameObject Target;
		public Vector3 LocalOffset;
		public Rotation GrabOffset;
		public float Distance;
		public bool Pulling;
		public Vector3 EyePos;
		public Rotation EyeRot;
		public PhysicsBody Anchor;
		public Sandbox.Physics.ControlJoint Joint;

		public Vector3 EndPoint => Target.WorldTransform.PointToWorld( LocalOffset );

		public void RemoveJoint()
		{
			Joint?.Remove();
			Joint = null;
			Anchor?.Remove();
			Anchor = null;
		}
	}

	sealed class Pull
	{
		public Rigidbody Body;
		public Vector3 EyePos;
		public Rotation EyeRot;
		public TimeSince Since;
	}

	internal static readonly Dictionary<Guid, Hold> Holds = new();
	static readonly Dictionary<Guid, Pull> _pulls = new();

	static GameObject Find( Guid id ) => Game.ActiveScene?.Directory.FindByGuid( id );

	static Rigidbody BodyOf( GameObject root ) => root?.Components.Get<Rigidbody>( FindMode.EverythingInSelf );

	// ----- client -> host -----

	/// <summary> Beam grab: hold the clicked point (local to the object) at this distance, keeping this rotation relative to your yaw. </summary>
	[Rpc.Host]
	public static void Begin( Guid id, string limb, Vector3 localOffset, Rotation grabOffset, float distance, Vector3 eyePos, Rotation eyeRot )
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) ) return;
		if ( SwrpBuild.Grabbable( Find( id ) ) is not { } root ) return;
		// a ragdoll: hold the limb you clicked (its own body, found by its bone name: the limb objects are made on each machine),
		// like Sandbox; anything else: the whole object
		var target = string.IsNullOrEmpty( limb ) ? root
			: root.Components.GetAll<Rigidbody>( FindMode.EverythingInDescendants ).FirstOrDefault( r => r.GameObject.Name == limb )?.GameObject ?? root;
		if ( Holds.Values.Any( h => h.Target == target && h.Owner != caller ) ) return;

		Release( caller.Id, 0, default );
		_pulls.Remove( caller.Id );
		if ( BodyOf( target ) is { } rb ) rb.MotionEnabled = true;     // Sandbox unfreezes what you grab

		Holds[caller.Id] = new Hold
		{
			Owner = caller, Target = target, LocalOffset = localOffset, GrabOffset = grabOffset, Distance = distance,
			EyePos = eyePos, EyeRot = eyeRot,
		};
		BeamOn( caller.Id, root.Id, localOffset, false, target == root ? "" : target.Name );
	}

	/// <summary> Every tick while holding: the holder's aim, rotation offset (E + mouse) and distance (wheel, E + W/S). </summary>
	[Rpc.Host( NetFlags.Unreliable )]
	public static void Aim( Vector3 eyePos, Rotation eyeRot, Rotation grabOffset, float distance )
	{
		if ( !Holds.TryGetValue( Rpc.Caller.Id, out var h ) ) return;
		h.EyePos = eyePos;
		h.EyeRot = eyeRot;
		h.GrabOffset = grabOffset;
		h.Distance = MathF.Max( 0f, distance );
	}

	/// <summary> Let go: 0 drops, 1 freezes it where it is, 2 launches it along <paramref name="dir"/>. </summary>
	[Rpc.Host]
	public static void End( int mode, Vector3 dir )
	{
		Release( Rpc.Caller.Id, mode, dir );
	}

	/// <summary> Right mouse on a loose prop: pull it toward you (Sandbox's gravity grab). Sent every few ticks while held down. </summary>
	[Rpc.Host( NetFlags.Unreliable )]
	public static void PullTick( Guid id, Vector3 eyePos, Rotation eyeRot )
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) || Holds.ContainsKey( caller.Id ) ) return;
		if ( BodyOf( SwrpBuild.Grabbable( Find( id ) ) ) is not { MotionEnabled: true } rb ) { _pulls.Remove( caller.Id ); return; }
		if ( Holds.Values.Any( h => h.Target == rb.GameObject ) ) return;
		_pulls[caller.Id] = new Pull { Body = rb, EyePos = eyePos, EyeRot = eyeRot, Since = 0 };
	}

	/// <summary> R on a prop: unfreeze it and everything constrained to it. </summary>
	[Rpc.Host]
	public static void UnfreezeAll( Guid id )
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) || SwrpBuild.Grabbable( Find( id ) ) is not { } target ) return;
		var bodies = new HashSet<Rigidbody>();
		Connected( target, bodies );
		foreach ( var rb in bodies ) rb.MotionEnabled = true;
		var count = bodies.Select( b => b.GameObject.Root ).Distinct().Count();
		Fx( target.WorldPosition, false );
		SwrpBuild.Notify( caller, count == 1 ? "Unfroze 1 object" : $"Unfroze {count} objects" );
	}

	static void Connected( GameObject source, HashSet<Rigidbody> result )
	{
		foreach ( var rb in source.Root.Components.GetAll<Rigidbody>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( !result.Add( rb ) ) continue;
			foreach ( var joint in rb.Joints )
			{
				if ( joint.Object1.IsValid() ) Connected( joint.Object1, result );
				if ( joint.Object2.IsValid() ) Connected( joint.Object2, result );
			}
		}
	}

	// ----- host -----

	static void Release( Guid owner, int mode, Vector3 dir )
	{
		if ( !Holds.Remove( owner, out var h ) ) return;
		h.RemoveJoint();
		BeamOff( owner );
		if ( !h.Target.IsValid() ) return;
		if ( BodyOf( h.Target ) is not { } rb ) return;

		if ( mode == 1 )
		{
			rb.Velocity = Vector3.Zero;
			rb.AngularVelocity = Vector3.Zero;
			rb.MotionEnabled = false;
			Fx( h.Target.WorldPosition, true );
		}
		else if ( mode == 2 && rb.MotionEnabled )
		{
			var mass = rb.Mass;
			rb.ApplyImpulse( dir.Normal * ( mass * LaunchForce ) );
			rb.PhysicsBody?.ApplyAngularImpulse( Vector3.Random * ( mass * LaunchForce ) );
		}
	}

	/// <summary> Host, every physics tick (<see cref="SwrpPhysgunSystem"/>). </summary>
	internal static void Step( Scene scene )
	{
		if ( !Networking.IsHost ) return;

		foreach ( var (owner, h) in Holds.ToArray() )
		{
			if ( !h.Target.IsValid() || h.Owner is null || !Connection.All.Contains( h.Owner ) || !SwrpBuild.May( h.Owner ) )
			{
				Release( owner, 0, default );
				continue;
			}

			var forward = h.EyeRot.Forward;
			var rb = BodyOf( h.Target );
			if ( rb is null )
			{
				// NPCs and anything without a body: move it there directly (the old SWRP physgun)
				var at = h.EyePos + forward * h.Distance;
				var root = at - ( h.EndPoint - h.Target.WorldPosition );
				h.Target.WorldPosition = Vector3.Lerp( h.Target.WorldPosition, root, 0.5f );
				if ( h.Target.Components.Get<NavMeshAgent>() is { } agent ) agent.SetAgentPosition( h.Target.WorldPosition );
				continue;
			}

			if ( !rb.MotionEnabled || !rb.PhysicsBody.IsValid() )
			{
				h.RemoveJoint();
				continue;
			}

			var distance = ClampDistance( rb, h.EndPoint, h.EyePos, forward, h.Distance );
			var targetPosition = h.EyePos + forward * distance;
			var targetRotation = h.Pulling ? h.EyeRot * h.GrabOffset : Rotation.FromYaw( h.EyeRot.Yaw() ) * h.GrabOffset;

			h.Anchor ??= new PhysicsBody( scene.PhysicsWorld ) { BodyType = PhysicsBodyType.Keyframed, AutoSleep = false };
			h.Anchor.Transform = new Transform( targetPosition, targetRotation );

			if ( h.Joint is null )
			{
				// scale is built into physics, take it out
				var bodyTransform = rb.WorldTransform.WithScale( 1.0f );
				var body = rb.PhysicsBody;
				var point1 = new PhysicsPoint( h.Anchor );
				var point2 = new PhysicsPoint( body, bodyTransform.PointToLocal( h.EndPoint ) );
				var maxForce = body.Mass * body.World.Gravity.LengthSquared;

				h.Joint = PhysicsJoint.CreateControl( point1, point2 );
				h.Joint.LinearSpring = new PhysicsSpring( 32, 4, maxForce );
				h.Joint.AngularSpring = new PhysicsSpring( 64, 4, maxForce * 3 );
			}
		}

		foreach ( var (owner, p) in _pulls.ToArray() )
		{
			var conn = Connection.All.FirstOrDefault( c => c.Id == owner );
			if ( p.Since > 0.25f || !p.Body.IsValid() || !p.Body.MotionEnabled || conn is null )
			{
				_pulls.Remove( owner );
				continue;
			}

			var closest = p.Body.FindClosestPoint( p.EyePos );
			if ( closest.Distance( p.EyePos ) <= PullDistance )
			{
				// close enough: it's now a gravity hold at its centre of mass
				_pulls.Remove( owner );
				var go = p.Body.GameObject;
				var bodyTransform = p.Body.WorldTransform;
				var scaleOnly = new Transform( Vector3.Zero, Rotation.Identity, bodyTransform.Scale );
				Holds[owner] = new Hold
				{
					Owner = conn, Target = go, Pulling = true, Distance = 0,
					LocalOffset = scaleOnly.PointToLocal( p.Body.PhysicsBody?.LocalMassCenter ?? Vector3.Zero ),
					GrabOffset = p.EyeRot.Inverse * bodyTransform.Rotation,
					EyePos = p.EyePos, EyeRot = p.EyeRot,
				};
				using ( Rpc.FilterInclude( conn ) ) PulledIn( go.Id, Holds[owner].GrabOffset );
				BeamOn( owner, go.Id, Vector3.Zero, true );
				continue;
			}

			p.Body.ApplyForce( p.EyeRot.Backward * p.Body.Mass * PullForce );
		}
	}

	/// <summary> Keep the held thing at least a little way in front of the eyes. </summary>
	static float ClampDistance( Rigidbody body, Vector3 point, Vector3 eye, Vector3 forward, float distance, float min = 50f )
	{
		distance = MathF.Max( 0f, distance );
		var closest = body.FindClosestPoint( eye );
		var along = distance + Vector3.Dot( closest - point, forward );
		return along < min ? distance + ( min - along ) : distance;
	}

	// ----- host -> clients -----

	/// <summary> To the holder: your right-mouse pull caught something (left mouse launches it, right mouse drops it). </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PulledIn( Guid target, Rotation grabOffset )
	{
		SwrpBuildTool.Local?.OnPulledIn( target, grabOffset );
	}

	public struct Beam
	{
		public Guid Target;
		public string Limb;
		public Vector3 LocalOffset;
		public bool Pulling;
	}

	/// <summary> Every machine: who is holding what, for the beams (<see cref="SwrpPhysgunSystem"/> draws them). </summary>
	public static readonly Dictionary<Guid, Beam> Beams = new();

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void BeamOn( Guid owner, Guid target, Vector3 localOffset, bool pulling = false, string limb = "" )
	{
		Beams[owner] = new Beam { Target = target, LocalOffset = localOffset, Pulling = pulling, Limb = limb };
		if ( Find( target ) is { } go ) SwrpAudio.At( "sounds/ui/reload_charge.sound", go.WorldPosition );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void BeamOff( Guid owner )
	{
		Beams.Remove( owner );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Fx( Vector3 at, bool frozen )
	{
		SwrpAudio.At( frozen ? "sounds/ui/shield_on.sound" : "sounds/ui/shield_off.sound", at );
		SwrpPhysgunSystem.Flash( at, frozen );
	}
}

/// <summary>
/// Runs the physgun: the host steps every hold each physics tick; every machine draws the beams (a bent line from the holder's
/// right hand to the held point, Sandbox-style) and the freeze flashes.
/// </summary>
public sealed class SwrpPhysgunSystem : GameObjectSystem<SwrpPhysgunSystem>
{
	readonly Dictionary<Guid, LineRenderer> _lines = new();
	readonly List<(GameObject Go, TimeSince Since)> _flashes = new();
	static readonly Color BeamColor = new( 0.35f, 0.8f, 1f, 0.9f );

	public SwrpPhysgunSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 10, () => SwrpPhysgun.Step( Scene ), "SwrpPhysgun" );
		Listen( Stage.FinishUpdate, 10, DrawBeams, "SwrpPhysgunBeams" );
	}

	readonly List<(LineRenderer Line, TimeSince Since)> _zaps = new();

	/// <summary> A tool gun tracer: a thin bright line that fades out quickly. </summary>
	public static void AddZap( Vector3 from, Vector3 to )
	{
		if ( Current is not { } sys || sys.Scene is null ) return;
		var go = sys.Scene.CreateObject();
		go.Name = "Tool gun tracer";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var line = go.Components.Create<LineRenderer>();
		line.UseVectorPoints = true;
		line.VectorPoints = new List<Vector3> { from, to };
		line.Color = BeamColor;
		line.Width = 0.5f;
		line.Lighting = false;
		line.CastShadows = false;
		sys._zaps.Add( (line, 0) );
	}

	public static void Flash( Vector3 at, bool frozen )
	{
		if ( Current is not { } sys || sys.Scene is null ) return;
		var go = sys.Scene.CreateObject();
		go.Name = "Physgun flash";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = at;
		var light = go.Components.Create<PointLight>();
		light.LightColor = frozen ? new Color( 0.4f, 0.85f, 1f ) * 3f : new Color( 1f, 0.75f, 0.3f ) * 3f;
		light.Radius = 120f;
		light.Shadows = false;
		sys._flashes.Add( (go, 0) );
	}

	void DrawBeams()
	{
		if ( Scene is null || Scene != Game.ActiveScene ) return;
		SwrpHeldToolSystem.Draw( Scene );

		for ( var i = _flashes.Count - 1; i >= 0; i-- )
		{
			var (go, since) = _flashes[i];
			if ( !go.IsValid() || since > 0.35f ) { go?.Destroy(); _flashes.RemoveAt( i ); continue; }
			if ( go.Components.Get<PointLight>() is { } l ) l.LightColor = l.LightColor.WithAlpha( 1f - since / 0.35f );
		}

		for ( var i = _zaps.Count - 1; i >= 0; i-- )
		{
			var (zap, since) = _zaps[i];
			if ( !zap.IsValid() || since > 0.15f ) { zap?.GameObject?.Destroy(); _zaps.RemoveAt( i ); continue; }
			zap.Color = BeamColor.WithAlpha( BeamColor.a * ( 1f - since / 0.15f ) );
		}

		foreach ( var owner in _lines.Keys.ToArray() )
		{
			if ( SwrpPhysgun.Beams.ContainsKey( owner ) && _lines[owner].IsValid() ) continue;
			_lines[owner]?.GameObject?.Destroy();
			_lines.Remove( owner );
		}

		foreach ( var (owner, beam) in SwrpPhysgun.Beams )
		{
			var target = Scene.Directory.FindByGuid( beam.Target );
			if ( target.IsValid() && !string.IsNullOrEmpty( beam.Limb ) )
				target = target.Components.GetAll<Rigidbody>( FindMode.EverythingInDescendants ).FirstOrDefault( r => r.GameObject.Name == beam.Limb )?.GameObject ?? target;
			var holder = SwrpPlayers.Valid.FirstOrDefault( p => p.Network.Owner?.Id == owner );
			if ( !target.IsValid() || holder is null || !holder.Controller.IsValid() )
			{
				if ( _lines.Remove( owner, out var stale ) ) stale?.GameObject?.Destroy();
				continue;
			}

			if ( !_lines.TryGetValue( owner, out var line ) || !line.IsValid() )
			{
				var go = Scene.CreateObject();
				go.Name = "Physgun beam";
				go.NetworkMode = NetworkMode.Never;
				go.Flags |= GameObjectFlags.NotSaved;
				line = go.Components.Create<LineRenderer>();
				line.UseVectorPoints = true;
				line.Color = BeamColor;
				line.Width = 0.8f;
				line.Lighting = false;
				line.CastShadows = false;
				_lines[owner] = line;
			}

			// the gravity hold has no beam to speak of: a short stub to the prop's centre
			var eye = holder.Controller.EyePosition;
			var aim = holder.Controller.EyeAngles.ToRotation();
			var start = SwrpHeldToolSystem.Muzzle( owner ) ?? Hand( holder ) ?? ( eye + aim.Down * 14f + aim.Right * 8f + aim.Forward * 16f );
			var end = beam.Pulling ? target.WorldPosition : target.WorldTransform.PointToWorld( beam.LocalOffset );
			var bend = start + aim.Forward * ( start.Distance( end ) * 0.5f );

			var points = new List<Vector3>( 16 );
			for ( var i = 0; i <= 15; i++ )
			{
				var t = i / 15f;
				var a = Vector3.Lerp( start, bend, t );
				var b = Vector3.Lerp( bend, end, t );
				points.Add( Vector3.Lerp( a, b, t ) );
			}
			line.VectorPoints = points;
		}
	}

	internal static Vector3? Hand( SwrpPlayer p )
	{
		var body = p.Body;
		if ( !body.IsValid() || body.SceneModel is null ) return null;
		var bone = body.GetBoneObject( "hand_R" );
		return bone.IsValid() ? bone.WorldPosition : null;
	}
}
