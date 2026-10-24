using System;

namespace Swrp;

/// <summary>
/// How a downed trooper lies: instead of the body tipping over stiff as a plank, it drops (a short fall blended over
/// <see cref="FallTime"/>) onto its back, then lies wounded: the torso propped up on the left elbow, the right hand pressed
/// to the wound (to the helmet if only stunned), one knee drawn up, the chest breathing hard, the head turning to look
/// about, and now and then a wince that shifts the body. Crawling drags it along with the arms working. Revived, it blends
/// back up to standing over <see cref="RiseTime"/>. Every machine runs it from the synced <see cref="SwrpVitals.IsDowned"/>,
/// so everyone sees the same thing; the pose is varied a little per trooper.
/// The bones are posed as the emotes are (<see cref="SwrpEmotes"/>): the renderer's bone objects are taken over from the
/// animation (ProceduralBone) after everything else has run this frame (OnPreRender); the torso and head from the rest
/// pose (so mouse look can't twist them), the limbs from the animated pose, with world-space turns, so no bone axis conventions are assumed; limbs are two-bone IK from their (turned) roots.
/// </summary>
[Title( "SWRP Downed Pose" ), Category( "SWRP" ), Icon( "airline_seat_flat" )]
public sealed class SwrpDownedPose : Component
{
	/// <summary> Set every frame by whoever owns the state (SwrpVitals, or the test stand). </summary>
	public bool Down { get; set; }

	/// <summary> Stunned rather than wounded: a hand to the helmet instead of the wound. </summary>
	public bool Stunned { get; set; }

	public const float FallTime = 0.75f, RiseTime = 1.1f;

	SkinnedModelRenderer _r;
	float _w;                          // 0 standing .. 1 lying
	float _yaw;                        // the way the body lies (feet towards)
	bool _posing;
	float _seed;
	float _wince;
	RealTimeUntil _nextWince;
	float _look, _lookGoal;
	RealTimeUntil _nextLook;
	Vector3 _lastPos;
	float _crawl;
	float _standYaw;
	Rotation _set;
	Vector3 _base;                      // where the body sits when standing (local)

	static readonly string[] Spine = { "spine_0", "spine_1", "spine_2", "neck_0", "head" };

	/// <summary> Every machine, every frame: keep this body's downed pose in step with the state. </summary>
	public static void Apply( SkinnedModelRenderer body, bool down, bool stunned )
	{
		if ( !body.IsValid() ) return;
		var pose = body.Components.Get<SwrpDownedPose>( FindMode.EverythingInSelf );
		if ( pose is null )
		{
			if ( !down ) return;
			pose = body.Components.Create<SwrpDownedPose>();
		}
		pose.Down = down;
		pose.Stunned = stunned;
	}

	protected override void OnStart()
	{
		_r = GetComponent<SkinnedModelRenderer>();
		_seed = ( GameObject.Id.GetHashCode() & 0xffff ) / 65535f;
		_yaw = WorldRotation.Yaw();
		_lastPos = WorldPosition;
	}

	protected override void OnPreRender()
	{
		if ( !_r.IsValid() || _r.Model is null ) return;
		var dt = Time.Delta;
		if ( _w <= 0f && !Down ) { if ( _posing ) Release(); return; }

		// the fall and the rise; the body keeps the way it was facing when it went down (and turns slowly while crawling)
		// what the controller wants this frame: if nothing has turned the body since we laid it down, it's still our own
		// lying rotation, whose yaw is meaningless (pitched 90°), so keep the standing yaw we last saw
		if ( _w <= 0f || WorldRotation.Forward.Distance( _set.Forward ) + WorldRotation.Up.Distance( _set.Up ) > 0.01f ) _standYaw = WorldRotation.Yaw();
		if ( _w <= 0f ) { _yaw = _standYaw; _base = LocalPosition; }
		_w = Math.Clamp( _w + ( Down ? dt / FallTime : -dt / RiseTime ), 0f, 1f );
		var moved = ( WorldPosition - _lastPos ).WithZ( 0 ).Length / MathF.Max( dt, 0.001f );
		_lastPos = WorldPosition;
		_crawl = _crawl.LerpTo( Down && moved > 5f ? 1f : 0f, dt * 4f );
		if ( _crawl > 0.3f ) _yaw += MathX.DeltaDegrees( _yaw, _standYaw ) * MathF.Min( 1f, dt * 1.5f );
		var e = _w * _w * ( 3f - 2f * _w );

		// on its back, feet along the way it faced; the body origin (the feet) dropped so the back rests on the floor
		var lying = Rotation.FromYaw( _yaw ) * Rotation.FromPitch( -90f );
		WorldRotation = Rotation.Slerp( Rotation.FromYaw( _standYaw ), lying, e );
		_set = WorldRotation;
		var s = _r.WorldScale.z;
		LocalPosition = _base + new Vector3( 0, 0, 6f * s * e );

		if ( !_r.CreateBoneObjects ) _r.CreateBoneObjects = true;
		Pose( dt, e, s );
	}

	void Pose( float dt, float e, float s )
	{
		if ( !RestBone( "spine_0", out var spine0 ) || !RestBone( "neck_0", out var neck ) ) return;
		_posing = true;
		var fwd = Rotation.FromYaw( _yaw ).Forward;      // towards the feet when lying
		var left = Rotation.FromYaw( _yaw ).Left;
		var up = Vector3.Up;
		var t = Time.Now + _seed * 30f;

		// winces: now and then the whole body tenses, curls a little and the knee comes up
		if ( _nextWince <= 0f ) { _nextWince = 3f + _seed * 4f + Game.Random.Float( 0f, 5f ); _wince = 1f; }
		_wince = MathF.Max( 0f, _wince - dt * 1.6f );
		var tense = MathF.Sin( MathF.Min( 1f, _wince ) * MathF.PI );

		// ----- torso: raised off the floor (propped on the elbow), breathing hard -----
		var breath = MathF.Sin( t * ( Stunned ? 2.2f : 3.4f ) );
		var raise = ( 28f + 6f * _seed + 3f * breath * 0.5f + 8f * tense - 10f * _crawl ) * e;
		var cur = ( neck.Position - spine0.Position ).Normal;
		var want = ( -fwd * MathF.Cos( raise.DegreeToRadian() ) + up * MathF.Sin( raise.DegreeToRadian() ) ).Normal;
		// a slight roll onto the propping (left) side
		var roll = Rotation.FromAxis( -fwd, -( 14f + 6f * tense ) * e );
		var turn = Rotation.Slerp( Rotation.Identity, roll * Rotation.FromToRotation( cur, want ), e );
		var pivot = spine0.Position;

		// the head: looks about, sometimes down at the wound
		if ( _nextLook <= 0f )
		{
			_nextLook = Game.Random.Float( 1.2f, 3.5f );
			_lookGoal = Game.Random.Float() < 0.3f ? 0f : Game.Random.Float( -40f, 40f );
		}
		_look = _look.LerpTo( _lookGoal, dt * 2.5f );
		var spineDir = turn * cur;
		var nod = ( _lookGoal == 0f ? 25f : 5f ) + 10f * tense;

		foreach ( var name in Spine )
		{
			var bone = _r.GetBoneObject( name );
			if ( !bone.IsValid() || !RestBone( name, out var a ) ) continue;
			var rot = turn * a.Rotation;
			// breathing: the chest heaves
			if ( name == "spine_2" ) rot = Rotation.FromAxis( left, breath * 2.5f * e ) * rot;
			if ( name == "head" ) rot = Rotation.FromAxis( spineDir, _look * e ) * Rotation.FromAxis( left, -nod * e ) * rot;
			Take( bone ).WorldRotation = rot;
		}
		Vector3 Turned( Vector3 p ) => pivot + turn * ( p - pivot );

		// ----- arms -----
		var floorZ = WorldPosition.z - 6f * s * e;
		// left: propped on the forearm, elbow under the shoulder, hand flat towards the feet (crawling: reaching and pulling)
		if ( RestBone( "arm_upper_L", out var shL ) )
		{
			var shoulder = Turned( shL.Position );
			var reach = MathF.Sin( t * 3f ) * 0.5f + 0.5f;
			var wrist = shoulder.WithZ( floorZ + 2f * s ) + left * ( 4f * s ) + fwd * ( ( 8f + _crawl * ( 6f * reach - 10f ) ) * s );
			Arm( "L", wrist, -up * 0.6f - fwd * 0.7f + left * 0.4f, e );
		}
		// right: pressed to the wound (belly), or to the helmet when stunned; crawling, it pushes on the floor
		if ( RestBone( "arm_upper_R", out var shR ) && RestBone( "spine_1", out var belly ) && RestBone( "head", out var headA ) )
		{
			var clutch = Stunned ? Turned( headA.Position ) + up * ( 2f * s ) - left * ( 2f * s ) : Turned( belly.Position ) + up * ( 3.5f * s ) - left * ( 1f * s );
			clutch += up * ( breath * 0.4f * s );
			var push = Turned( shR.Position ).WithZ( floorZ + 2f * s ) - left * ( 6f * s ) + fwd * ( ( 2f + 8f * ( MathF.Sin( t * 3f + 1.6f ) * 0.5f + 0.5f ) ) * s );
			Arm( "R", Vector3.Lerp( clutch, push, _crawl ), -left * 0.8f + up * 0.3f - fwd * 0.2f, e );
		}

		// ----- legs: one knee drawn up (more when wincing), the other out straight; crawling, both push in turn -----
		var bendL = 0.35f + 0.25f * _seed + 0.25f * tense;
		var bendR = 0.05f + 0.05f * MathF.Max( 0f, breath );
		if ( _crawl > 0.01f )
		{
			bendL = MathX.Lerp( bendL, 0.5f + 0.4f * MathF.Sin( t * 3f ), _crawl );
			bendR = MathX.Lerp( bendR, 0.5f - 0.4f * MathF.Sin( t * 3f ), _crawl );
		}
		Leg( "L", bendL, fwd, up, e );
		Leg( "R", bendR, fwd, up, e );
	}

	void Arm( string side, Vector3 wristTarget, Vector3 pole, float w )
	{
		var upper = _r.GetBoneObject( $"arm_upper_{side}" );
		var lower = _r.GetBoneObject( $"arm_lower_{side}" );
		var hand = _r.GetBoneObject( $"hand_{side}" );
		if ( !upper.IsValid() || !lower.IsValid() || !hand.IsValid() ) return;
		if ( !AnimBone( $"arm_upper_{side}", out var au ) || !AnimBone( $"arm_lower_{side}", out var al ) || !AnimBone( $"hand_{side}", out var ah ) ) return;
		Limb( upper, lower, hand, au, al, ah, wristTarget, pole, w );
	}

	void Leg( string side, float bend, Vector3 fwd, Vector3 up, float w )
	{
		var upper = _r.GetBoneObject( $"leg_upper_{side}" );
		var lower = _r.GetBoneObject( $"leg_lower_{side}" );
		var foot = _r.GetBoneObject( $"ankle_{side}" );
		if ( !upper.IsValid() || !lower.IsValid() || !foot.IsValid() ) return;
		if ( !AnimBone( $"leg_upper_{side}", out var au ) || !AnimBone( $"leg_lower_{side}", out var al ) || !AnimBone( $"ankle_{side}", out var ah ) ) return;
		var a = au.Position.Distance( al.Position );
		var b = al.Position.Distance( ah.Position );
		// the foot slides back along the floor towards the hip as the knee comes up
		var reach = ( a + b ) * ( 1f - bend * 0.55f );
		var target = upper.WorldPosition + fwd * reach;
		target.z = MathF.Min( target.z, ah.Position.z );
		Limb( upper, lower, foot, au, al, ah, target, up + fwd * 0.2f, w );
	}

	/// <summary> Two-bone solve onto a target, each bone turned from its animated rotation, blended by w. </summary>
	void Limb( GameObject upper, GameObject lower, GameObject end, Transform au, Transform al, Transform ah, Vector3 target, Vector3 pole, float w )
	{
		var root = upper.WorldPosition;
		var a = au.Position.Distance( al.Position );
		var b = al.Position.Distance( ah.Position );
		var toT = target - root;
		var d = Math.Clamp( toT.Length, MathF.Abs( a - b ) + 0.01f, a + b - 0.01f );
		var dir = toT.Normal;
		var pd = ( pole - dir * Vector3.Dot( pole, dir ) ).Normal;
		var cosA = Math.Clamp( ( a * a + d * d - b * b ) / ( 2f * a * d ), -1f, 1f );
		var mid = root + dir * ( a * cosA ) + pd * ( a * MathF.Sqrt( 1f - cosA * cosA ) );
		var tip = root + dir * d;
		var animUpperDir = al.Position - au.Position;
		var upperRot = Rotation.FromToRotation( animUpperDir, mid - root ) * au.Rotation;
		var lowerRot = Rotation.FromToRotation( ah.Position - al.Position, tip - mid ) * al.Rotation;
		Take( upper ).WorldRotation = Rotation.Slerp( au.Rotation, upperRot, w );
		Take( lower ).WorldRotation = Rotation.Slerp( al.Rotation, lowerRot, w );
		Take( end ).WorldRotation = Rotation.Slerp( ah.Rotation, lowerRot * ( al.Rotation.Inverse * ah.Rotation ), w );
	}

	void Release()
	{
		_posing = false;
		LocalPosition = _base;
		WorldRotation = Rotation.FromYaw( _standYaw );
		if ( !_r.IsValid() ) return;
		foreach ( var name in Spine.Concat( new[] { "arm_upper_L", "arm_lower_L", "hand_L", "arm_upper_R", "arm_lower_R", "hand_R",
			"leg_upper_L", "leg_lower_L", "ankle_L", "leg_upper_R", "leg_lower_R", "ankle_R" } ) )
		{
			var bone = _r.GetBoneObject( name );
			if ( bone.IsValid() && bone.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) bone.Flags &= ~GameObjectFlags.ProceduralBone;
		}
	}

	protected override void OnDestroy()
	{
		if ( _posing ) Release();
	}

	static GameObject Take( GameObject bone )
	{
		if ( !bone.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) bone.Flags |= GameObjectFlags.ProceduralBone;
		return bone;
	}

	/// <summary>
	/// A bone where the skeleton's rest pose puts it on this body (world space). The torso and head are built from this,
	/// not the animation, so the look-aim (the animation turning the spine and head to where the player looks) can't twist
	/// a lying trooper round.
	/// </summary>
	bool RestBone( string name, out Transform tx )
	{
		tx = default;
		var bone = _r.Model?.Bones.GetBone( name );
		if ( bone is null ) return false;
		tx = _r.WorldTransform.ToWorld( _r.Model.GetBoneTransform( bone.Index ) );
		return true;
	}

	bool AnimBone( string name, out Transform tx )
	{
		tx = default;
		var bone = _r.Model?.Bones.GetBone( name );
		return bone is not null && _r.TryGetBoneTransformAnimation( in bone, out tx );
	}

	// ----- testing on the animation stand -----

	[ConCmd( "swrp_downed_test", Help = "swrp_downed_test [1 down / 0 up / 2 stunned] - dev, this machine: put the swrp_anim_test stand down (or back up) to check the downed pose" )]
	public static void TestCommand( int down = 1 )
	{
		var m = Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>().FirstOrDefault();
		var r = m?.GetComponent<SkinnedModelRenderer>();
		if ( !r.IsValid() ) { Log.Warning( "[SWRP] Spawn the stand first: swrp_anim_test" ); return; }
		_testDown = down != 0;
		_testStunned = down == 2;
		_testBody = r;
		Apply( r, _testDown, _testStunned );
	}

	static SkinnedModelRenderer _testBody;
	static bool _testDown, _testStunned;

	/// <summary> The test stand has no vitals: keep its pose fed from the test command. </summary>
	protected override void OnUpdate()
	{
		if ( _r == _testBody && _testBody.IsValid() ) { Down = _testDown; Stunned = _testStunned; }
	}
}
