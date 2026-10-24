using System;

namespace Swrp;

/// <summary>
/// What an ambient naval officer (<see cref="SwrpCrew"/>) is busy with, and the procedural pose for it: the datapad in the
/// left hand, and every so often a task that takes over the arms and head from the animation. Reading the datapad (held up
/// at chest height, screen tilted to the face, head bowed, the right hand tapping at it now and then), working a console
/// (right hand tapping out entries at waist height, head down), thinking it over (pad lowered, right hand at the chin), or
/// surveying the room (arms to the animation, head turning). Officers on a beat read while they walk now and then and do a
/// task at each stop; officers at a post cycle through them. Poses blend in and out; arms are two-bone IK from the animated
/// shoulder (the same approach as <see cref="SwrpEmotes"/>). Cosmetic and local, like the crew.
/// </summary>
public sealed class SwrpOfficerTasks
{
	public enum Task { None, Read, Console, Think, Survey }

	readonly bool _posted;
	Task _task = Task.None, _want = Task.None;
	float _w;
	RealTimeUntil _until;
	bool _wasMoving;

	// reading: the right hand comes in to tap at the screen for a while, then rests
	bool _tapping;
	float _tapW;
	RealTimeUntil _tapSwitch;
	RealTimeUntil _nextTap;
	float _tapDip;
	Vector2 _tapAt, _tapGoal;

	// the head
	float _pitch, _yaw, _yawGoal;
	RealTimeUntil _nextGlance;

	GameObject _pad;

	/// <summary>
	/// Datapad placement in the left hand's frame (x along the fingers; on the crew rig the palm faces -z, seen in play), its
	/// screen turned to face out of the palm; swrp_datapad_fix.
	/// </summary>
	public static Vector3 PadOffset { get; set; } = new( 3.2f, 0.6f, -1.25f );
	public static Angles PadAngles { get; set; } = new( 0f, 0f, 180f );

	readonly bool _readOnly;

	/// <param name="readOnly"> Only ever reading the datapad (a player holding one, SwrpHands). </param>
	public SwrpOfficerTasks( bool posted, bool readOnly = false )
	{
		_posted = posted;
		_readOnly = readOnly;
		_until = Game.Random.Float( 0.5f, 3f );
	}

	public Task Current => _task;

	/// <summary> Every frame while the officer runs: pick tasks, blend them, pose the bones. </summary>
	public void Tick( SkinnedModelRenderer r, float dt, bool moving )
	{
		if ( !r.IsValid() || r.Model is null ) return;
		if ( !r.CreateBoneObjects ) r.CreateBoneObjects = true;
		HoldPad( r );

		if ( moving != _wasMoving )
		{
			_wasMoving = moving;
			_until = 0;
		}
		if ( _until <= 0f )
		{
			_want = Pick( moving );
			_until = moving ? Game.Random.Float( 5f, 12f ) : _posted ? Game.Random.Float( 7f, 16f ) : Game.Random.Float( 4f, 9f );
		}
		// blend the old task out before the new one comes in
		if ( _want != _task )
		{
			_w = MathF.Max( 0f, _w - dt * 2.4f );
			if ( _w <= 0f ) { _task = _want; _tapping = false; _tapW = 0f; }
		}
		else _w = MathF.Min( 1f, _w + dt * 1.8f );
		var w = Smooth( _w );

		Head( r, dt, w );
		Arms( r, dt, w );
	}

	Task Pick( bool moving )
	{
		if ( _readOnly ) return Task.Read;
		var x = Game.Random.Float();
		if ( moving ) return x < 0.4f ? Task.Read : Task.None;
		if ( _posted ) return x < 0.42f ? Task.Console : x < 0.72f ? Task.Read : x < 0.86f ? Task.Think : Task.Survey;
		return x < 0.55f ? Task.Read : x < 0.75f ? Task.Think : Task.Survey;
	}

	static float Smooth( float t ) => t * t * ( 3f - 2f * t );

	/// <summary> Put the datapad away and give the head and arms back to the animation. </summary>
	public void Stop( SkinnedModelRenderer r )
	{
		_pad?.Destroy();
		_pad = null;
		if ( !r.IsValid() || !r.CreateBoneObjects ) return;
		Free( r.GetBoneObject( "head" ) );
		foreach ( var s in new[] { "L", "R" } )
			foreach ( var b in new[] { "arm_upper_", "arm_lower_", "hand_" } )
				Free( r.GetBoneObject( b + s ) );
		foreach ( var bone in r.Model.Bones.AllBones )
			if ( bone.Name.StartsWith( "finger_" ) ) Free( r.GetBoneObject( bone ) );
	}

	void HoldPad( SkinnedModelRenderer r )
	{
		if ( _pad.IsValid() )
		{
			_pad.LocalPosition = PadOffset;
			_pad.LocalRotation = PadAngles.ToRotation();
			return;
		}
		var hand = r.GetBoneObject( "hand_L" );
		var model = ItemModels.For( "datapad" );
		if ( !hand.IsValid() || model is null ) return;
		_pad = new GameObject( hand, true, "Datapad" );
		_pad.NetworkMode = NetworkMode.Never;
		_pad.Flags |= GameObjectFlags.NotSaved;
		_pad.LocalPosition = PadOffset;
		_pad.LocalRotation = PadAngles.ToRotation();
		_pad.LocalScale = 1f;
		var mr = _pad.Components.Create<ModelRenderer>();
		mr.Model = model;
		mr.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	// ----- the head -----

	void Head( SkinnedModelRenderer r, float dt, float w )
	{
		var pitchGoal = _task switch { Task.Read => 26f, Task.Console => 20f, Task.Think => 6f, _ => 0f } * w;
		if ( _nextGlance <= 0f )
		{
			var wide = _task is Task.Survey or Task.Think or Task.None;
			_nextGlance = wide ? Game.Random.Float( 1.5f, 4.5f ) : Game.Random.Float( 3f, 7f );
			_yawGoal = wide ? Game.Random.Float( -38f, 38f ) * ( _task == Task.None ? 0.5f : 1f ) : Game.Random.Float( -8f, 8f );
		}
		_pitch = _pitch.LerpTo( pitchGoal, dt * 2.5f );
		_yaw = _yaw.LerpTo( _yawGoal, dt * 2f );

		var head = r.GetBoneObject( "head" );
		if ( !head.IsValid() ) return;
		if ( MathF.Abs( _pitch ) < 0.2f && MathF.Abs( _yaw ) < 0.2f )
		{
			Free( head );
			return;
		}
		if ( !AnimBone( r, "head", out var anim ) ) return;
		var up = Vector3.Up;
		var fwd = r.WorldRotation.Forward;
		var look = Rotation.FromAxis( up, _yaw ) * fwd;
		look = ( look * MathF.Cos( _pitch.DegreeToRadian() ) - up * MathF.Sin( _pitch.DegreeToRadian() ) ).Normal;
		Take( head ).WorldRotation = Rotation.FromToRotation( fwd, look ) * anim.Rotation;
	}

	// ----- the arms -----

	readonly record struct ArmPose( Vector3 Wrist, Vector3 Pole, Rotation Hand );

	void Arms( SkinnedModelRenderer r, float dt, float w )
	{
		ArmPose? left = null, right = null;
		var rightW = w;
		var s = r.WorldScale.z;
		var body = r.WorldRotation;
		var fwd = body.Forward;
		var side = body.Right;
		var up = Vector3.Up;

		if ( _task != Task.None && AnimBone( r, "spine_2", out var chest ) && AnimBone( r, "pelvis", out var pelvis ) )
		{
			switch ( _task )
			{
				case Task.Read:
					{
						var c = chest.Position + fwd * ( 11f * s ) + up * ( 2f * s ) - side * ( 0.5f * s );
						left = PadIn( c, fwd, side, up, 0.72f, s, out var screenN, out var along, out var across );
						// the right hand: in to tap for a while, then out to rest
						if ( _tapSwitch <= 0f )
						{
							_tapping = !_tapping && Game.Random.Float() < 0.75f;
							_tapSwitch = _tapping ? Game.Random.Float( 2.5f, 6f ) : Game.Random.Float( 1.5f, 4f );
						}
						_tapW = _tapW.Approach( _tapping ? 1f : 0f, dt * 2.2f );
						if ( _tapW > 0.001f )
						{
							Tapping( dt, 0.55f );
							var tip = c + along * ( _tapAt.x * s ) + across * ( _tapAt.y * s ) + screenN * ( ( 0.5f + 1.4f * _tapDip ) * s );
							var fingers = ( -screenN * 0.55f + fwd * 0.55f - side * 0.45f ).Normal;
							right = Reach( tip, fingers, screenN, side * 0.8f - up * 0.5f - fwd * 0.1f, s );
							rightW = w * Smooth( _tapW );
						}
						break;
					}
				case Task.Console:
					{
						// the right hand on a console just below the hips in front (console tops stand about at the wrist), tapping out entries across it
						Tapping( dt, 1.6f );
						var key = pelvis.Position - up * ( 3.5f * s ) + fwd * ( 15f * s ) + side * ( ( 3.2f + _tapAt.x ) * s ) + fwd * ( _tapAt.y * s );
						var tip = key + up * ( ( 0.3f + 1.5f * _tapDip ) * s );
						right = Reach( tip, ( fwd * 0.85f - up * 0.5f ).Normal, up, side * 0.8f - up * 0.4f - fwd * 0.2f, s );
						// the pad held flat at the left hip
						left = PadIn( pelvis.Position + up * ( 1.5f * s ) + fwd * ( 8f * s ) - side * ( 4f * s ), fwd, side, up, 0.8f, s, out _, out _, out _ );
						break;
					}
				case Task.Think:
					{
						// pad lowered across the body, right hand up at the chin
						var c = chest.Position + fwd * ( 10f * s ) - up * ( 9f * s ) - side * ( 1f * s );
						left = PadIn( c, fwd, side, up, 0.65f, s, out _, out _, out _ );
						if ( AnimBone( r, "head", out var head ) )
						{
							var chin = head.Position + fwd * ( 5.2f * s ) - up * ( 1.2f * s );
							right = Reach( chin, ( up * 0.85f - side * 0.25f + fwd * 0.1f ).Normal, fwd, side * 0.25f - up * 1.0f + fwd * 0.35f, s, 5.5f );
						}
						break;
					}
			}
		}

		PoseArm( r, "L", left, w );
		PoseArm( r, "R", right, rightW );
	}

	/// <summary>
	/// The left hand holding the pad with its centre at <paramref name="c"/>, its screen tilted towards the face by
	/// <paramref name="tilt"/> (0 = facing forward, 1 = facing straight up); the screen's normal and axes come back out.
	/// </summary>
	static ArmPose PadIn( Vector3 c, Vector3 fwd, Vector3 side, Vector3 up, float tilt, float s, out Vector3 screenN, out Vector3 along, out Vector3 across )
	{
		screenN = ( up * tilt - fwd * ( 1f - tilt ) ).Normal;
		var fingers = ( side * 0.9f + fwd * 0.25f );
		fingers = ( fingers - screenN * Vector3.Dot( fingers, screenN ) ).Normal;
		var hand = Rotation.LookAt( fingers, -screenN );     // palm (-z) under the pad, its screen towards screenN
		along = hand.Forward;
		across = hand.Left;
		var wrist = c - hand * ( PadOffset * s );
		return new ArmPose( wrist, -side * 0.7f - up * 0.7f, hand );
	}

	/// <summary> The right hand reaching so its fingertips land on <paramref name="tip"/>, the back of the hand towards <paramref name="back"/> (the palm away from it). </summary>
	static ArmPose Reach( Vector3 tip, Vector3 fingers, Vector3 back, Vector3 pole, float s, float reach = 6.5f )
	{
		var z = ( back - fingers * Vector3.Dot( back, fingers ) ).Normal;
		return new ArmPose( tip - fingers * ( reach * s ), pole, Rotation.LookAt( fingers, -z ) );     // the right hand's palm is +z on the crew rig (mirror of the left)
	}

	/// <summary> A finger tapping: a quick dip at each touch, moving to a new spot between taps (spread across the area). </summary>
	void Tapping( float dt, float spread )
	{
		if ( _nextTap <= 0f )
		{
			_nextTap = Game.Random.Float( 0.25f, 0.9f );
			_tapGoal = new Vector2( Game.Random.Float( -2.2f, 2.2f ), Game.Random.Float( -1.3f, 1.3f ) ) * spread;
		}
		_tapAt = Vector2.Lerp( _tapAt, _tapGoal, Math.Clamp( dt * 9f, 0f, 1f ) );
		// down for the first part of each interval, up for the rest
		var t = (float)_nextTap.Relative;
		_tapDip = t < 0.12f ? t / 0.12f : MathF.Min( 1f, _tapDip + dt * 6f );
	}

	static void PoseArm( SkinnedModelRenderer r, string side, ArmPose? pose, float w )
	{
		var upper = r.GetBoneObject( $"arm_upper_{side}" );
		var lower = r.GetBoneObject( $"arm_lower_{side}" );
		var hand = r.GetBoneObject( $"hand_{side}" );
		if ( pose is not { } p || w <= 0.001f || !upper.IsValid() || !lower.IsValid() || !hand.IsValid() )
		{
			Free( upper );
			Free( lower );
			Free( hand );
			return;
		}
		if ( !AnimBone( r, $"arm_upper_{side}", out var au ) || !AnimBone( r, $"arm_lower_{side}", out var al ) || !AnimBone( r, $"hand_{side}", out var ah ) ) return;
		var shoulder = upper.WorldPosition;
		var a = au.Position.Distance( al.Position );
		var b = al.Position.Distance( ah.Position );
		var (elbow, wrist) = Solve( shoulder, p.Wrist, p.Pole, a, b );
		var upperRot = Rotation.FromToRotation( al.Position - au.Position, elbow - shoulder ) * au.Rotation;
		var lowerRot = Rotation.FromToRotation( ah.Position - al.Position, wrist - elbow ) * al.Rotation;
		var handRot = p.Hand * SwrpEmotes.HandFix( side == "R" );
		Take( upper ).WorldRotation = Rotation.Slerp( au.Rotation, upperRot, w );
		Take( lower ).WorldRotation = Rotation.Slerp( al.Rotation, lowerRot, w );
		Take( hand ).WorldRotation = Rotation.Slerp( ah.Rotation, handRot, w );
	}

	static (Vector3 Elbow, Vector3 Wrist) Solve( Vector3 shoulder, Vector3 target, Vector3 poleDir, float a, float b )
	{
		var toWrist = target - shoulder;
		var d = Math.Clamp( toWrist.Length, MathF.Abs( a - b ) + 0.01f, a + b - 0.01f );
		var dir = toWrist.Normal;
		var pole = ( poleDir - dir * Vector3.Dot( poleDir, dir ) ).Normal;
		var cosA = Math.Clamp( ( a * a + d * d - b * b ) / ( 2f * a * d ), -1f, 1f );
		return ( shoulder + dir * ( a * cosA ) + pole * ( a * MathF.Sqrt( 1f - cosA * cosA ) ), shoulder + dir * d );
	}

	static GameObject Take( GameObject bone )
	{
		if ( !bone.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) bone.Flags |= GameObjectFlags.ProceduralBone;
		return bone;
	}

	static void Free( GameObject bone )
	{
		if ( bone.IsValid() && bone.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) bone.Flags &= ~GameObjectFlags.ProceduralBone;
	}

	static bool AnimBone( SkinnedModelRenderer r, string name, out Transform tx )
	{
		tx = default;
		var bone = r.Model?.Bones.GetBone( name );
		return bone is not null && r.TryGetBoneTransformAnimation( in bone, out tx );
	}

	[ConCmd( "swrp_officer_bones", Help = "dev: print the officers' animated bone positions next to their body positions (world vs model space check)" )]
	public static void BonesCommand()
	{
		foreach ( var r in Game.ActiveScene?.GetAllComponents<SkinnedModelRenderer>().Where( x => x.GameObject.Name == "officer" ) ?? Enumerable.Empty<SkinnedModelRenderer>() )
		{
			var line = $"[SWRP] officer body {r.WorldPosition:0} scale {r.WorldScale.z:0.00} yaw {r.WorldRotation.Yaw():0}";
			foreach ( var n in new[] { "pelvis", "spine_2", "head", "arm_upper_R", "hand_R", "hand_L" } )
				if ( AnimBone( r, n, out var t ) ) line += $" | {n} anim {t.Position:0}" + ( r.GetBoneObject( n ) is { } go && go.IsValid() ? $" obj {go.WorldPosition:0}" : "" );
			Log.Info( line );
		}
	}

	[ConCmd( "swrp_datapad_fix", Help = "dev: swrp_datapad_fix <x> <y> <z> [pitch yaw roll] - where the officers' datapad sits in the left hand (x along the fingers, z out of the palm)" )]
	public static void PadFixCommand( float x, float y, float z, float pitch = 0f, float yaw = 0f, float roll = 0f )
	{
		PadOffset = new Vector3( x, y, z );
		PadAngles = new Angles( pitch, yaw, roll );
	}
}
