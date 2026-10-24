using System;

namespace Swrp.Jedi;

/// <summary>
/// Draws a Jedi's lightsaber and animates the body round it, on every machine (nothing here is networked). Lives on the body
/// (the SkinnedModelRenderer's object) and is driven each frame by <see cref="JediCombat"/> (or, for the character screen's
/// mannequin, by <see cref="JediMannequins"/>).
///
/// The saber: the hilt model (hanging at the left hip when put away), and each blade as light rather than a solid
/// shape: a white-hot core, a coloured glow round it and a soft haze, a light that moves with the blade and lights the
/// room in its colour, and a ribbon trail behind the edge sampled every frame that fades with the blade's speed. The hum
/// rises in pitch and volume as the blade moves faster. Where a lit blade touches a wall or floor it leaves a glowing cut
/// that cools to a scorch (never carving the ship).
///
/// The body: the procedural layer on top of whatever the animgraph plays. Both arms are solved (two-bone IK) onto the hilt:
/// the right hand at the grip, the left below it on two-handed forms and the staff, free on one-handed forms and dual
/// sabers, out towards the target for a Force power. The spine twists and bends through the cut, the head turns to the
/// nearest foe, and blows taken add a short flinch.
/// </summary>
public sealed class SaberRig : Component
{
	public struct Input
	{
		public MotionSample Sample;
		public SaberDesign Design;
		public float Ignite;
		public bool Holstered;
		public bool Posing;
		public float Scale;
		public Vector3 Origin;
		public float Yaw;
		public bool FirstPerson;
		public bool Thrown;
		public float ThrowTime;
		public Func<float, Vector3, (Vector3 Pos, bool Returning, bool Done)> Throw;
		public Vector3? LockPoint;
		public Vector3? LookTarget;
		/// <summary> First person: the (lagged) view to lay the saber out round, like a view model. </summary>
		public Transform? View;
		/// <summary> How fast the body moves over the ground: the stance gives way to the walk cycle as it speeds up. </summary>
		public float MoveSpeed;
		public JediCombat Owner;
	}

	SkinnedModelRenderer _body;
	SaberVisual _main, _off;
	readonly SaberHum _hum = new();
	readonly HashSet<GameObject> _taken = new();
	Vector3 _flinch, _flinchVel;
	float _headYaw, _headPitch;
	RealTimeSince _sinceSpark;
	PointLight _lockLight;

	/// <summary> A blow landed on this body: a quick additive flinch away from it. </summary>
	/// <summary> The blade bit into something: flare it. </summary>
	public void Flare()
	{
		if ( _main is not null ) _main.Flare = 1f;
		if ( _off is not null ) _off.Flare = 1f;
	}

	/// <summary> Posing the body this frame (saber up, or a power): flinches go through the rig. </summary>
	public bool Posing => _taken.Count > 0;

	public void Flinch( Vector3 fromDir )
	{
		var local = _body.IsValid() ? _body.WorldRotation.Inverse * fromDir : fromDir;
		_flinchVel += new Vector3( -local.x, -local.y, 0f ) * 120f;
	}

	public void Drive( Input i )
	{
		_body ??= GetComponent<SkinnedModelRenderer>();
		if ( !_body.IsValid() ) return;
		var d = i.Design;
		_main ??= new SaberVisual( Scene, "saber" );
		_main.Build( d, false );
		if ( d.IsDual ) { _off ??= new SaberVisual( Scene, "shoto" ); _off.Build( d, true ); }
		else if ( _off is not null ) { _off.Destroy(); _off = null; }

		var visible = _body.Enabled && _body.GameObject.Active;
		var scale = i.Scale;
		var s = i.Sample;

		// ----- where the saber is -----
		if ( !visible )
		{
			_main.Hide();
			_off?.Hide();
			_hum.Stop();
			Release();
			return;
		}

		var mainWorld = i.View is { } v0 ? ViewPose( s.Main, v0, scale ) : SaberMotion.ToWorld( s.Main, i.Origin, i.Yaw, scale );
		BladePose? offWorld = null;
		if ( d.IsDual )
		{
			var ow = i.View is { } v1 ? ViewPose( s.Off, v1, scale ) : SaberMotion.ToWorld( s.Off, i.Origin, i.Yaw, scale );
			offWorld = ow;
		}

		if ( i.Holstered )
		{
			PlaceAtHip( _main, d, scale, false );
			if ( _off is not null ) PlaceAtHip( _off, d, scale, true );
		}
		else if ( i.Thrown && i.Throw is not null && i.Owner is not null )
		{
			var (pos, _, _) = i.Throw( i.ThrowTime, i.Owner.CatchPoint );
			// spinning flat as it flies, about ten turns a second
			var spin = Rotation.FromAxis( Vector3.Up, i.ThrowTime * 1300f ) * Rotation.FromPitch( -8f );
			var dir = spin.Forward;
			var hand = pos - dir * d.HiltLength * 0.1f * scale;
			_main.Place( new BladePose( hand, dir, Vector3.Up ), d, scale, false );
			if ( _off is not null && offWorld is { } o2 ) _off.Place( o2, d, scale, true );
		}
		else
		{
			_main.Place( mainWorld, d, scale, false );
			if ( _off is not null && offWorld is { } ow ) _off.Place( ow, d, scale, true );
		}

		// ----- the blades -----
		var lit = i.Ignite;
		_main.Blades( d, lit, scale, false, i.FirstPerson );
		_off?.Blades( d, lit, scale, true, i.FirstPerson );
		var speed = MathF.Max( _main.TipSpeed, _off?.TipSpeed ?? 0f );
		if ( lit > 0.01f ) _hum.Update( _main.Center, speed / MathF.Max( 0.5f, scale ), lit, d.CrystalDef.HumPitch * ( d.IsStaff ? 0.94f : 1f ), i.Thrown );
		else _hum.Stop();

		// cuts in the walls and floor where the blade touches
		if ( lit > 0.9f )
		{
			_main.BurnWorld( Scene, d, scale, i.Owner?.GameObject ?? GameObject );
			_off?.BurnWorld( Scene, d, scale, i.Owner?.GameObject ?? GameObject );
		}

		// a blade lock: sparks pour from where the blades cross (one side makes them)
		if ( i.LockPoint is { } lp && i.Owner is { } own && own.GameObject.Id.CompareTo( own.LockWith ) < 0 )
		{
			if ( _sinceSpark > 0.045f )
			{
				_sinceSpark = 0;
				SaberFx.LockSparks( Scene, lp, d.BladeColor );
			}
			_hum.Grind( lp, true );
			// where the blades grind the light flickers hot white over both of them (and the faces behind)
			if ( !_lockLight.IsValid() && SwrpFx.LightsOn )
			{
				var lg = new GameObject( true, "saber_lock_light" );
				lg.NetworkMode = NetworkMode.Never;
				lg.Flags |= GameObjectFlags.NotSaved;
				_lockLight = lg.Components.Create<PointLight>();
				_lockLight.Shadows = false;
			}
			if ( _lockLight.IsValid() )
			{
				_lockLight.WorldPosition = lp;
				_lockLight.Radius = 150f * i.Scale;
				_lockLight.LightColor = Color.Lerp( d.BladeColor, Color.White, 0.6f ) * Game.Random.Float( 1.2f, 4.2f );
			}
		}
		else
		{
			_hum.Grind( Vector3.Zero, false );
			if ( _lockLight.IsValid() ) { _lockLight.GameObject.Destroy(); _lockLight = null; }
		}

		// ----- the body -----
		// (in first person the body only casts a shadow, and its arms can't reach a saber laid out round the view: leave them be)
		if ( i.Posing && i.View is null ) Pose( i, mainWorld, offWorld );
		else Release();
	}

	/// <summary> A body-space pose laid out round the eyes: what sits at eye height ahead of the chest stays there on screen wherever you look, nudged up and out so the hilt is in view. </summary>
	static BladePose ViewPose( BladePose p, Transform view, float scale )
	{
		var rel = p.Hand - new Vector3( 0f, 0f, 64f ) + new Vector3( 4f, 0f, 9f );
		var r = view.Rotation;
		return new BladePose( view.Position + r * ( rel * scale ), r * p.Dir, r * p.Knuckle );
	}

	void PlaceAtHip( SaberVisual v, SaberDesign d, float scale, bool shoto )
	{
		// the left hip (the right for a shoto), hanging emitter-down, riding the pelvis
		var model = _body.Model;
		var pelvisBone = model?.Bones.GetBone( "pelvis" );
		var hipModel = shoto ? new Vector3( 0.6f, -6.4f, 34f ) : new Vector3( 1.8f, 6.6f, 34.5f );
		var dirModel = new Vector3( 0.22f, shoto ? -0.1f : 0.1f, -1f ).Normal;
		Vector3 hip, dir;
		if ( pelvisBone is not null && _body.TryGetBoneTransform( "pelvis", out var pelvis ) )
		{
			var bind = model.GetBoneTransform( pelvisBone.Index );
			hip = pelvis.PointToWorld( bind.PointToLocal( hipModel ) );
			dir = pelvis.Rotation * ( bind.Rotation.Inverse * dirModel );
		}
		else
		{
			hip = _body.WorldTransform.PointToWorld( hipModel );
			dir = _body.WorldRotation * dirModel;
		}
		var hiltLen = shoto ? SaberDesign.ShotoHilt : d.HiltLength;
		// the grip point is 0.58 of the way up the hilt; hang it with the hilt's middle at the hip
		var hand = hip + dir * hiltLen * ( d.IsStaff && !shoto ? 0f : 0.08f ) * scale;
		v.Place( new BladePose( hand, dir, _body.WorldRotation.Left * ( shoto ? -1f : 1f ) ), d, scale, shoto );
	}

	// ----- the body's procedural layer -----

	void Pose( Input i, BladePose main, BladePose? off )
	{
		var r = _body;
		if ( !r.CreateBoneObjects ) r.CreateBoneObjects = true;
		var s = i.Sample;
		var scale = i.Scale;
		var body = Rotation.FromYaw( i.Yaw );
		var d = i.Design;

		// the legs first: the form's footing, which moves the hips everything else hangs from
		var stanceTurn = Stance( i, r, body, scale );

		// the spine: the cut's twist and bend, plus any flinch, shared over three bones
		_flinchVel += -_flinch * 160f * Time.Delta;
		_flinchVel *= MathF.Max( 0f, 1f - 11f * Time.Delta );
		_flinch += _flinchVel * Time.Delta;
		// (the chest follows a side-on stance a third of the way; the head looks back to the front)
		var twist = s.Twist + _flinch.y * 0.6f + stanceTurn * 0.35f;
		var bend = s.Bend + _flinch.x;
		var lean = s.Lean;
		var right = body.Right;
		var fwd = body.Forward;
		foreach ( var (bone, share) in Spine )
		{
			var go = r.GetBoneObject( bone );
			if ( !go.IsValid() || !Anim( r, bone, out var anim ) ) continue;
			var add = Rotation.FromAxis( Vector3.Up, twist * share ) * Rotation.FromAxis( right, -bend * share ) * Rotation.FromAxis( fwd, lean * share );
			Take( go ).WorldRotation = add * anim.Rotation;
		}

		// the head turns to whoever you're fighting
		var head = r.GetBoneObject( "head" );
		if ( head.IsValid() && Anim( r, "head", out var ha ) )
		{
			var wantYaw = -twist * 0.75f;
			var wantPitch = 0f;
			if ( i.LookTarget is { } at )
			{
				var to = ( at - head.WorldPosition );
				var local = body.Inverse * to;
				wantYaw = Math.Clamp( MathX.RadianToDegree( MathF.Atan2( local.y, local.x ) ) - twist, -45f, 45f );
				wantPitch = Math.Clamp( -MathX.RadianToDegree( MathF.Atan2( local.z, local.WithZ( 0 ).Length ) ), -25f, 25f );
			}
			_headYaw = _headYaw.LerpTo( wantYaw, Time.Delta * 8f );
			_headPitch = _headPitch.LerpTo( wantPitch, Time.Delta * 8f );
			// on top of the spine's twist: the eyes end up on the target whatever the body is doing
			Take( head ).WorldRotation = Rotation.FromAxis( Vector3.Up, twist + _headYaw ) * Rotation.FromAxis( right, -bend * 0.5f + _headPitch ) * ha.Rotation;
		}

		// the arms onto the hilt
		var hiltLen = d.HiltLength * scale;
		Vector3 rightGrip, leftGrip;
		if ( d.IsStaff )
		{
			rightGrip = main.Hand + main.Dir * 3.6f * scale;
			leftGrip = main.Hand - main.Dir * 3.6f * scale;
		}
		else
		{
			rightGrip = main.Hand + main.Dir * 0.02f * hiltLen;
			leftGrip = main.Hand - main.Dir * 0.27f * hiltLen;
		}

		if ( !s.HandEmpty ) Arm( r, "R", rightGrip, main.Dir, main.Knuckle, body * new Vector3( -0.3f, -0.75f, -0.6f ), 1f );
		else
		{
			// the saber's away: the empty hand reaches after it, fingers open
			var reach = i.Origin + body * ( s.Main.Hand * scale );
			Arm( r, "R", reach, body.Up, body.Forward, body * new Vector3( -0.3f, -0.75f, -0.6f ), 1f, open: true );
		}

		var chest = i.Origin + body * ( SaberMotion.Chest * scale );
		if ( s.Cast > 0.01f )
		{
			// a Force power: the left arm out, palm to the target
			var palm = body * s.CastPalm;
			var wrist = chest + body * new Vector3( 4f, 8f, 8f ) * scale + palm * 18f * scale;
			if ( s.CastPower == JediForce.Heal ) wrist = chest + body * new Vector3( 7f, 1f, 2f ) * scale;
			var blendFrom = d.IsDual && off is { } o0 ? o0.Hand : leftGrip;
			var w = s.Cast;
			Arm( r, "L", Vector3.Lerp( blendFrom, wrist, w ), body.Up, palm, body * new Vector3( -0.2f, 0.8f, -0.5f ), 1f, castPalm: palm, castW: w );
		}
		else if ( d.IsDual && off is { } o )
		{
			Arm( r, "L", o.Hand, o.Dir, o.Knuckle, body * new Vector3( -0.3f, 0.75f, -0.6f ), 1f, left: true );
		}
		else if ( s.OffHandFree > 0.5f )
		{
			// one-handed: the off hand held back for balance, open
			var balance = chest + body * new Vector3( -9f, 12f, 7f ) * scale;      // raised behind, like a fencer's
			Arm( r, "L", balance, body.Up, body.Left, body * new Vector3( -0.6f, 0.6f, -0.4f ), 1f, open: true );
		}
		else
		{
			Arm( r, "L", leftGrip, main.Dir, main.Knuckle, body * new Vector3( -0.2f, 0.8f, -0.6f ), 1f, left: true );
		}
	}

	float _stanceW;

	/// <summary>
	/// The stance (standing still, saber up): the hips drop into the form's crouch, shift and turn side-on, and both legs are
	/// solved (two-bone, like the arms) onto the form's footing: the sword-side foot forward, the back foot out and turned.
	/// Gives way to the animation's walk cycle as the body starts to move. Returns the hips' turn (degrees) for the spine.
	/// </summary>
	float Stance( Input i, SkinnedModelRenderer r, Rotation body, float scale )
	{
		var s = i.Sample;
		var want = 1f - Math.Clamp( i.MoveSpeed / 70f, 0f, 1f );
		_stanceW = _stanceW.Approach( want, Time.Delta * ( want > _stanceW ? 3f : 9f ) );
		var w = _stanceW * _stanceW * ( 3f - 2f * _stanceW );
		var pelvis = r.GetBoneObject( "pelvis" );
		if ( !pelvis.IsValid() || !Anim( r, "pelvis", out var pa ) ) return 0f;

		// the hips turn into a cut too (half as far as the chest), so the body drives the blade
		var turn = ( s.StanceTurn + s.Twist * 0.45f ) * w;
		var footTurn = s.StanceTurn * w;      // (the feet stay planted through the cut)
		var drop = ( 2f + s.Crouch * 12f ) * scale * w;
		var shift = ( s.StanceShift + s.Bend * 0.08f ) * scale * w;
		Take( pelvis ).WorldPosition = pa.Position + Vector3.Down * drop + body.Forward * shift;
		Take( pelvis ).WorldRotation = Rotation.FromAxis( Vector3.Up, turn ) * pa.Rotation;

		foreach ( var side in new[] { "R", "L" } )
		{
			var upper = r.GetBoneObject( $"leg_upper_{side}" );
			var lower = r.GetBoneObject( $"leg_lower_{side}" );
			var ankle = r.GetBoneObject( $"ankle_{side}" );
			if ( !upper.IsValid() || !lower.IsValid() || !ankle.IsValid() ) continue;
			if ( !Anim( r, $"leg_upper_{side}", out var au ) || !Anim( r, $"leg_lower_{side}", out var al ) || !Anim( r, $"ankle_{side}", out var aa ) ) continue;
			var front = side == "R";

			// the foot on the ground, the ankle at the animation's height above it
			var foot = new Vector3( ( front ? 0.5f : -0.5f ) * s.StanceDepth, ( front ? -0.5f : 0.5f ) * s.StanceWidth, 0f );
			foot = Rotation.FromYaw( footTurn * 0.8f ) * foot;
			var ground = i.Origin + body * ( foot * scale );
			var planted = ground + Vector3.Up * MathF.Max( 0f, aa.Position.z - i.Origin.z );
			var target = Vector3.Lerp( aa.Position, planted, w );
			// the front foot points at the foe, the back one turned out with its knee over the toes (not splayed)
			var footYaw = front ? footTurn * 0.2f : ( footTurn * 0.6f + 20f ) * w;
			var pole = Rotation.FromYaw( footYaw ) * body * new Vector3( 1f, front ? -0.08f : 0.12f, 0f );

			var hip = upper.WorldPosition;
			var a = au.Position.Distance( al.Position );
			var b = al.Position.Distance( aa.Position );
			var (knee, end) = Solve( hip, target, pole, a, b );
			Take( upper ).WorldRotation = Rotation.FromToRotation( al.Position - au.Position, knee - hip ) * au.Rotation;
			Take( lower ).WorldRotation = Rotation.FromToRotation( aa.Position - al.Position, end - knee ) * al.Rotation;
			Take( ankle ).WorldRotation = Rotation.FromAxis( Vector3.Up, footYaw ) * aa.Rotation;
		}
		return turn;
	}

	static readonly (string Bone, float Share)[] Spine = { ( "spine_0", 0.3f ), ( "spine_1", 0.35f ), ( "spine_2", 0.35f ) };

	// per model: the hold attachments (where a hilt sits in each fist) relative to the hand bones
	static readonly Dictionary<(Model, string), Transform> _holds = new();

	/// <summary>
	/// Two-bone solve for an arm: the wrist placed so the fist closes round <paramref name="grip"/> with the hilt running
	/// along <paramref name="along"/>, the elbow bent towards <paramref name="pole"/>, each bone aimed from its animated
	/// rotation (keeping the animation's twist).
	/// </summary>
	void Arm( SkinnedModelRenderer r, string side, Vector3 grip, Vector3 along, Vector3 knuckle, Vector3 pole, float w, bool open = false, bool left = false, Vector3? castPalm = null, float castW = 0f )
	{
		var upper = r.GetBoneObject( $"arm_upper_{side}" );
		var lower = r.GetBoneObject( $"arm_lower_{side}" );
		var hand = r.GetBoneObject( $"hand_{side}" );
		if ( !upper.IsValid() || !lower.IsValid() || !hand.IsValid() ) return;
		if ( !Anim( r, $"arm_upper_{side}", out var au ) || !Anim( r, $"arm_lower_{side}", out var al ) || !Anim( r, $"hand_{side}", out var ah ) ) return;

		// the hand's rotation: the hilt through the fist (the hold attachment's up), knuckles forward
		Rotation handRot;
		Vector3 wristTarget;
		var hold = HoldFor( r, side, ah );
		if ( castPalm is { } palm )
		{
			var fingers = ( Vector3.Up - palm * Vector3.Dot( Vector3.Up, palm ) ).Normal;
			handRot = Rotation.LookAt( fingers, palm ) * SwrpEmotes.HandFix( side == "R" );
			handRot = Rotation.Slerp( ah.Rotation, handRot, castW );
			wristTarget = grip;
		}
		else if ( open )
		{
			handRot = ah.Rotation;
			wristTarget = grip;
		}
		else
		{
			var fwd = ( knuckle - along * Vector3.Dot( knuckle, along ) );
			if ( fwd.LengthSquared < 0.001f ) fwd = Vector3.Cross( along, Vector3.Up );
			var holdRot = Rotation.LookAt( fwd.Normal, along );
			handRot = holdRot * hold.Rotation.Inverse;
			wristTarget = grip - handRot * hold.Position;
		}

		var shoulder = upper.WorldPosition;
		var a = au.Position.Distance( al.Position );
		var b = al.Position.Distance( ah.Position );
		var (elbow, wrist) = Solve( shoulder, wristTarget, pole, a, b );
		var upperRot = Rotation.FromToRotation( al.Position - au.Position, elbow - shoulder ) * au.Rotation;
		var lowerRot = Rotation.FromToRotation( ah.Position - al.Position, wrist - elbow ) * al.Rotation;
		Take( upper ).WorldRotation = Rotation.Slerp( au.Rotation, upperRot, w );
		Take( lower ).WorldRotation = Rotation.Slerp( al.Rotation, lowerRot, w );
		Take( hand ).WorldRotation = Rotation.Slerp( ah.Rotation, handRot, w );
		// a closed fist round the hilt (an open hand for a Force power or reaching for a thrown saber)
		if ( !open && castPalm is null ) CurlFingers( r, side, w );
		else ReleaseFingers( r, side );
	}

	// the fingers' bend (local axis and degrees per joint): tunable live with swrp_saber_curl
	public static Vector3 CurlAxis { get; set; } = new( 0f, 1f, 0f );
	public static float CurlDegrees { get; set; } = 75f;
	public static float ThumbDegrees { get; set; } = 32f;

	[ConCmd( "swrp_saber_curl", Help = "swrp_saber_curl <axis x> <axis y> <axis z> <degrees per joint> [thumb degrees] - dev: how the fingers close round a lightsaber hilt" )]
	public static void CurlCommand( float x, float y, float z, float degrees, float thumb = 28f )
	{
		CurlAxis = new Vector3( x, y, z ).Normal;
		CurlDegrees = degrees;
		ThumbDegrees = thumb;
	}

	/// <summary> Close a hand: each finger joint bent from the animation's pose by the curl (the thumb less), blended by w. </summary>
	void CurlFingers( SkinnedModelRenderer r, string side, float w )
	{
		foreach ( var bone in r.Model.Bones.AllBones )
		{
			if ( !bone.Name.StartsWith( "finger_" ) || !bone.Name.EndsWith( "_" + side ) || bone.Parent is null ) continue;
			var go = r.GetBoneObject( bone );
			if ( !go.IsValid() || !r.TryGetBoneTransformAnimation( in bone, out var now ) ) continue;
			var parent = bone.Parent;
			if ( !r.TryGetBoneTransformAnimation( in parent, out var pt ) ) continue;
			var animLocal = pt.Rotation.Inverse * now.Rotation;
			var thumb = bone.Name.Contains( "thumb" );
			var deg = ( thumb ? ThumbDegrees : CurlDegrees ) * ( side == "L" ? -1f : 1f );
			Take( go ).LocalRotation = Rotation.Slerp( animLocal, animLocal * Rotation.FromAxis( CurlAxis, deg ), w );
		}
	}

	void ReleaseFingers( SkinnedModelRenderer r, string side )
	{
		foreach ( var bone in r.Model.Bones.AllBones )
		{
			if ( !bone.Name.StartsWith( "finger_" ) || !bone.Name.EndsWith( "_" + side ) ) continue;
			var go = r.GetBoneObject( bone );
			if ( go.IsValid() && _taken.Contains( go ) && go.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) { go.Flags &= ~GameObjectFlags.ProceduralBone; _taken.Remove( go ); }
		}
	}

	/// <summary> The hold attachment (hold_R / hold_L) relative to the hand, learned from the animated body; a fist-sized guess until then. </summary>
	Transform HoldFor( SkinnedModelRenderer r, string side, Transform animHand )
	{
		var key = ( r.Model, side );
		if ( _holds.TryGetValue( key, out var t ) ) return t;
		var hand = r.GetBoneObject( $"hand_{side}" );
		var free = !hand.IsValid() || !hand.Flags.HasFlag( GameObjectFlags.ProceduralBone );
		if ( free && r.GetAttachment( $"hold_{side}" ) is { } att )
		{
			t = animHand.ToLocal( att );
			_holds[key] = t;
			return t;
		}
		return new Transform( new Vector3( 3.2f, side == "R" ? -0.6f : 0.6f, -0.8f ) * r.WorldScale.z, Rotation.Identity );
	}

	/// <summary> Learn the hold attachments while the arms are still the animation's (called before posing starts). </summary>
	public void Learn()
	{
		_body ??= GetComponent<SkinnedModelRenderer>();
		if ( !_body.IsValid() || _taken.Count > 0 ) return;
		foreach ( var side in new[] { "R", "L" } )
			if ( Anim( _body, $"hand_{side}", out var ah ) ) HoldFor( _body, side, ah );
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

	static bool Anim( SkinnedModelRenderer r, string name, out Transform tx )
	{
		tx = default;
		var bone = r.Model?.Bones.GetBone( name );
		return bone is not null && r.TryGetBoneTransformAnimation( in bone, out tx );
	}

	GameObject Take( GameObject bone )
	{
		if ( !bone.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) bone.Flags |= GameObjectFlags.ProceduralBone;
		_taken.Add( bone );
		return bone;
	}

	/// <summary> Hand every bone taken back to the animation. </summary>
	void Release()
	{
		if ( _taken.Count == 0 ) { Learn(); return; }
		foreach ( var bone in _taken )
			if ( bone.IsValid() && bone.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) bone.Flags &= ~GameObjectFlags.ProceduralBone;
		_taken.Clear();
	}

	protected override void OnDestroy()
	{
		Release();
		_main?.Destroy();
		_off?.Destroy();
		_hum.Stop();
		_hum.Grind( Vector3.Zero, false );
		if ( _lockLight.IsValid() ) _lockLight.GameObject.Destroy();
	}

	protected override void OnDisabled()
	{
		_main?.Hide();
		_off?.Hide();
		_hum.Stop();
	}
}
