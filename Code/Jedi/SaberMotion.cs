using System;

namespace Swrp.Jedi;

/// <summary> Where the saber is held: the grip (between the hands), the blade's direction, and which way the knuckles face. Body space. </summary>
public struct BladePose
{
	public Vector3 Hand;
	public Vector3 Dir;
	public Vector3 Knuckle;

	public BladePose( Vector3 hand, Vector3 dir, Vector3 knuckle )
	{
		Hand = hand;
		Dir = dir.Normal;
		Knuckle = knuckle.Normal;
	}

	public static BladePose Lerp( BladePose a, BladePose b, float t )
	{
		t = Math.Clamp( t, 0f, 1f );
		var dir = Vector3.Lerp( a.Dir, b.Dir, t );
		if ( dir.LengthSquared < 0.0001f ) dir = Vector3.Lerp( a.Dir, b.Dir + Vector3.Up * 0.1f, t );
		var k = Vector3.Lerp( a.Knuckle, b.Knuckle, t );
		if ( k.LengthSquared < 0.0001f ) k = b.Knuckle;
		return new BladePose( Vector3.Lerp( a.Hand, b.Hand, t ), dir, k );
	}

	/// <summary> Turned and moved round a pivot (the chest). </summary>
	public BladePose RotatedAbout( Vector3 pivot, Rotation r ) => new( pivot + r * ( Hand - pivot ), r * Dir, r * Knuckle );

	/// <summary> Mirrored left for right (the off hand). </summary>
	public BladePose Mirrored => new( Hand.WithY( -Hand.y ), Dir.WithY( -Dir.y ), Knuckle.WithY( -Knuckle.y ) );
}

/// <summary> One frame of the saber's motion: the blades, the body's lean, and what the swing is doing. </summary>
public struct MotionSample
{
	public BladePose Main;
	/// <summary> The off hand's shoto (dual) - unused otherwise. </summary>
	public BladePose Off;
	/// <summary> Torso twist (positive = turned left), bend (positive = forward) and roll, in degrees. </summary>
	public float Twist, Bend, Lean;
	/// <summary> How far the knees bend into the stance (0..1). </summary>
	public float Crouch;
	/// <summary> The form's footing: feet apart, front foot ahead, hips turned side-on, hips forward (see SaberForm). </summary>
	public float StanceWidth, StanceDepth, StanceTurn, StanceShift;
	/// <summary> The blade is cutting: hits count. </summary>
	public bool HitWindow;
	/// <summary> Somewhere in a swing (any phase). </summary>
	public bool Swinging;
	/// <summary> The right hand holds nothing (the saber is thrown). </summary>
	public bool HandEmpty;
	/// <summary> 0..1 how far the left hand has let go of a two-handed grip (casting, one-handed forms). </summary>
	public float OffHandFree;
	/// <summary> The left arm reaching out for a Force power (0..1) and where its palm points (body space). </summary>
	public float Cast;
	public Vector3 CastPalm;
	public int CastPower;
}

public enum MotionKind { Guard, Charge, Swing, Block, React, Lock, Throw }

/// <summary> How a recoil looks: bounced off a block, parried hard, guard broken, lost a blade lock, thrown by the Force. </summary>
public enum ReactKind { Blocked = 0, Parried = 1, GuardBreak = 2, LockLost = 3, Knocked = 4, BlockShake = 5 }

/// <summary>
/// The saber's procedural motion, the same maths on every machine: the host runs one per Jedi to know where the blade really
/// is (hits), and every machine runs one per Jedi it can see to draw and animate it. Everything is in body space (x forward,
/// y left, z up, from the feet of a 72-unit body; the caller scales and turns it into the world).
/// A swing is an arc in a plane through the chest: the blade turns round the plane's normal from one side to the other,
/// the hands lead it a little behind (the wrists roll through the cut). Each swing has anticipation (the blade drawn back
/// past the start), the strike (fast in the middle, where it lands), follow-through (carrying on and slowing) and recovery
/// back to the form's guard; a new swing blends from wherever the blade is, so chains flow from one cut into the next.
/// </summary>
public sealed class SaberMotion
{
	/// <summary> The chest: the pivot every swing turns round. </summary>
	public static readonly Vector3 Chest = new( 2f, 0f, 46f );

	public SaberForm Form { get; set; } = SaberForm.Default;
	public SaberDesign Design { get; set; } = SaberDesign.Default;

	public MotionKind Kind { get; private set; } = MotionKind.Guard;
	float _t;                 // time in the current action (stops during hit-stop)
	float _hitStop;
	BladePose _from;          // where the blade was when this action began
	BladePose _last;          // the last sampled pose
	float _blendIn = 0.14f;

	// the swing
	public SwingDir Dir { get; private set; }
	float _arc, _speed;
	bool _heavy;
	public int Combo { get; private set; }

	// recoils, blocks, locks
	ReactKind _react;
	float _reactLen;
	float _blockSince;
	/// <summary> The cut a raised guard is meeting (Any = the form's own block), so the blade goes where the blow lands. </summary>
	public SwingDir Incoming { get; private set; }
	public float LockMeter { get; set; }
	bool _parryFlick;

	// the Force (left arm), on top of anything else
	float _castT = 99f, _castLen;
	int _castPower;

	public bool Heavy => _heavy;
	public float Time => _t;
	/// <summary> Counts up with every new action (a new swing is a new id, even in the same direction). </summary>
	public int ActionId { get; private set; }

	/// <summary> The current swing's cutting window, in its own time: the strike and the first third of the follow-through. </summary>
	public (float From, float To) HitWindowTimes
	{
		get
		{
			var tm = SwingTiming;
			return ( tm.Windup, tm.Windup + tm.Strike + tm.Follow * 0.35f );
		}
	}

	/// <summary> The pose at another moment of the current action, without changing anything (sub-frame hit sweeps). </summary>
	public MotionSample Probe( float t, float aimPitch, float time )
	{
		var (keepT, keepLast, keepBlend) = ( _t, _last, _blendIn );
		_t = t;
		var s = Sample( aimPitch, time );
		( _t, _last, _blendIn ) = ( keepT, keepLast, keepBlend );
		return s;
	}

	// ----- swing timing (seconds at speed 1) -----
	public readonly record struct Timing( float Windup, float Strike, float Follow, float Recover )
	{
		public float Total => Windup + Strike + Follow + Recover;
	}

	public Timing SwingTiming => TimingFor( Dir, _heavy, _speed );

	public static Timing TimingFor( SwingDir dir, bool heavy, float speed )
	{
		var t = heavy ? new Timing( 0.2f, 0.27f, 0.16f, 0.34f ) : new Timing( 0.12f, 0.2f, 0.12f, 0.24f );
		if ( dir == SwingDir.Spin ) t = t with { Strike = t.Strike * 2.1f, Follow = t.Follow * 1.2f };
		if ( dir == SwingDir.Thrust ) t = t with { Strike = t.Strike * 0.75f, Follow = t.Follow * 0.8f };
		var s = MathF.Max( 0.3f, speed );
		return new Timing( t.Windup / s, t.Strike / s, t.Follow / s, t.Recover / s );
	}

	/// <summary> In the late part of a swing: the next one starts from here as the next step of the chain. </summary>
	public bool InChainWindow( float chainWindow )
	{
		if ( Kind != MotionKind.Swing ) return false;
		var tm = SwingTiming;
		return _t >= tm.Windup + tm.Strike && _t <= tm.Total + chainWindow;
	}

	/// <summary> Far enough through a swing to start another (the strike has landed or missed). </summary>
	public bool CanCancel => Kind != MotionKind.Swing || _t >= SwingTiming.Windup + SwingTiming.Strike * 0.95f;

	public bool SwingDone => Kind == MotionKind.Swing && _t >= SwingTiming.Total;

	// ----- actions -----

	public void Guard()
	{
		if ( Kind == MotionKind.Guard ) return;
		Begin( MotionKind.Guard, 0.22f );
	}

	/// <summary> Holding the attack button: the blade drawn back towards where the swing will start, building up. </summary>
	public void Charge( SwingDir dir )
	{
		Dir = dir;
		_arc = 150f;
		_heavy = true;
		Begin( MotionKind.Charge, 0.2f );
	}

	public void Swing( SwingDir dir, float arc, float speed, bool heavy, int combo )
	{
		Dir = dir;
		_arc = arc;
		_speed = speed;
		_heavy = heavy;
		Combo = combo;
		// from a charge the blade is already wound back: skip most of the anticipation
		var fromCharge = Kind == MotionKind.Charge;
		Begin( MotionKind.Swing, 0f );
		if ( fromCharge ) _t = SwingTiming.Windup * 0.7f;
	}

	public void Block( bool on, SwingDir from = SwingDir.Any )
	{
		if ( on && Kind != MotionKind.Block )
		{
			Incoming = from;
			Begin( MotionKind.Block, 0.09f );
			_blockSince = 0f;
			_parryFlick = false;
		}
		else if ( on && from != Incoming && !_parryFlick )
		{
			// already guarding: swing the guard over to meet the new cut
			Incoming = from;
			var since = _blockSince;
			Begin( MotionKind.Block, 0.1f );
			_blockSince = since;
		}
		else if ( !on && Kind == MotionKind.Block ) Begin( MotionKind.Guard, 0.18f );
	}

	/// <summary> A perfect parry: a sharp flick of the blade from the guard. </summary>
	public void Parry()
	{
		if ( Kind != MotionKind.Block ) Begin( MotionKind.Block, 0.06f );
		_parryFlick = true;
		_t = 0f;
	}

	public void React( ReactKind kind, float seconds )
	{
		_react = kind;
		_reactLen = MathF.Max( 0.1f, seconds );
		Begin( MotionKind.React, 0.06f );
	}

	public void Lock( bool on )
	{
		if ( on && Kind != MotionKind.Lock ) Begin( MotionKind.Lock, 0.1f );
		else if ( !on && Kind == MotionKind.Lock ) Begin( MotionKind.Guard, 0.2f );
	}

	public void Throw( bool on )
	{
		if ( on && Kind != MotionKind.Throw ) Begin( MotionKind.Throw, 0.05f );
		else if ( !on && Kind == MotionKind.Throw ) Begin( MotionKind.Guard, 0.15f );
	}

	/// <summary> The left arm reaches out for a Force power (the right keeps the saber). </summary>
	public void Cast( int power, float seconds )
	{
		_castPower = power;
		_castLen = seconds;
		_castT = 0f;
	}

	/// <summary> Freeze the motion for a moment as a blow lands (30-60 ms): the hit reads as solid. </summary>
	public void HitStop( float seconds ) => _hitStop = MathF.Max( _hitStop, seconds );

	void Begin( MotionKind kind, float blend )
	{
		ActionId++;
		_from = _last;
		Kind = kind;
		_t = 0f;
		_blendIn = blend;
	}

	public void Advance( float dt )
	{
		_castT += dt;
		if ( _hitStop > 0f ) { _hitStop -= dt; return; }
		_t += dt;
		_blockSince += dt;
		if ( Kind == MotionKind.Swing && _t > SwingTiming.Total + 0.5f ) Begin( MotionKind.Guard, 0.01f );
		if ( Kind == MotionKind.React && _t > _reactLen ) Begin( MotionKind.Guard, 0.2f );
	}

	// ----- sampling -----

	static float Smooth( float x ) { x = Math.Clamp( x, 0f, 1f ); return x * x * ( 3f - 2f * x ); }
	static float EaseOut( float x ) { x = Math.Clamp( x, 0f, 1f ); return 1f - ( 1f - x ) * ( 1f - x ); }
	/// <summary> A cut: slow off the mark, fastest through the middle, easing into the end. </summary>
	static float Cut( float x ) { x = Math.Clamp( x, 0f, 1f ); return x < 0.5f ? 4f * x * x * x : 1f - MathF.Pow( -2f * x + 2f, 3f ) / 2f; }

	/// <summary> The pose now, with the view's pitch (degrees, positive = looking down) tilting the swing up or down. </summary>
	public MotionSample Sample( float aimPitch, float time )
	{
		var s = new MotionSample { Crouch = Form.Crouch, StanceWidth = Form.StanceWidth, StanceDepth = Form.StanceDepth, StanceTurn = Form.StanceTurn, StanceShift = Form.StanceShift };
		var guard = GuardPose( time );
		BladePose pose;
		switch ( Kind )
		{
			case MotionKind.Charge:
				{
					var start = ArcPose( Dir, _arc, -0.12f, out _ );
					// a slight tremble as the strike builds
					var shake = new Vector3( 0, MathF.Sin( time * 47f ), MathF.Sin( time * 39f ) ) * 0.25f * Smooth( _t / 0.4f );
					pose = BladePose.Lerp( guard, start, Smooth( _t / 0.22f ) );
					pose.Hand += shake;
					s.Twist = TwistFor( Dir, -0.1f ) * Smooth( _t / 0.22f );
					s.Crouch += 0.1f;
					break;
				}
			case MotionKind.Swing:
				pose = SwingPose( guard, ref s );
				break;
			case MotionKind.Block:
				{
					var block = BlockPose( aimPitch );
					if ( _parryFlick )
					{
						// snap out and back over a tenth of a second
						var f = MathF.Sin( Math.Clamp( _t / 0.16f, 0f, 1f ) * MathF.PI );
						var r = Rotation.FromAxis( Vector3.Forward, -32f * f );
						block = new BladePose( block.Hand + new Vector3( 3f * f, 0, 1.5f * f ), r * block.Dir, r * block.Knuckle );
						s.Twist = 8f * f;
					}
					pose = block;
					s.Crouch += 0.08f;
					break;
				}
			case MotionKind.React:
				pose = ReactPose( guard, ref s );
				break;
			case MotionKind.Lock:
				{
					var shove = LockMeter;
					var jitter = new Vector3( 0, MathF.Sin( time * 31f ) * 0.5f, MathF.Sin( time * 43f ) * 0.6f );
					pose = new BladePose( Chest + new Vector3( 15f + shove * 3f, -1f, 7f ) + jitter, new Vector3( 0.55f, 0.18f, 0.82f ), Vector3.Forward );
					s.Bend = 6f + shove * 9f;
					s.Crouch += 0.2f;
					break;
				}
			case MotionKind.Throw:
				{
					// the arm sweeps through the throw, then stays out, reaching to call the saber back
					var u = Smooth( _t / 0.22f );
					var back = Chest + new Vector3( -4f, -12f, 10f );
					var out_ = Chest + new Vector3( 24f, -5f, 6f );
					pose = new BladePose( Vector3.Lerp( back, out_, u ), Vector3.Forward, Vector3.Up );
					s.Twist = MathX.Lerp( -25f, 10f, u );
					s.HandEmpty = true;
					break;
				}
			default:
				pose = guard;
				break;
		}

		// the form's lean into its stance, under whatever the action adds
		s.Bend += Form.StanceBend;

		// blend in from where the blade was
		if ( _blendIn > 0.001f && _t < _blendIn ) pose = BladePose.Lerp( _from, pose, Smooth( _t / _blendIn ) );

		// the view's pitch tilts the whole stance round the chest (half of it: the arms don't follow the eyes all the way)
		var tilt = Rotation.FromPitch( Math.Clamp( aimPitch, -50f, 50f ) * 0.5f );
		pose = pose.RotatedAbout( Chest, tilt );
		_last = pose;
		s.Main = pose;

		// dual: the shoto mirrors the main blade a beat behind; at guard it is held low and reversed
		if ( Design.IsDual ) s.Off = OffPose( pose, guard, aimPitch, time );

		s.OffHandFree = Form.OneHanded || Design.IsDual || Kind == MotionKind.Throw ? 1f : 0f;
		if ( Design.IsStaff ) s.OffHandFree = 0f;

		// the Force: left arm out, palm to the target
		if ( _castT < _castLen + 0.25f )
		{
			var u = _castT < 0.12f ? Smooth( _castT / 0.12f ) : _castT > _castLen ? 1f - Smooth( ( _castT - _castLen ) / 0.25f ) : 1f;
			s.Cast = u;
			s.CastPower = _castPower;
			s.CastPalm = _castPower switch
			{
				JediForce.Pull => Vector3.Lerp( new Vector3( 1f, 0.1f, 0.05f ), new Vector3( 0.3f, 0.35f, 0.1f ), Smooth( _castT / MathF.Max( 0.1f, _castLen ) ) ).Normal,
				JediForce.Heal => new Vector3( -0.6f, -0.3f, 0.2f ).Normal,
				JediForce.Leap or JediForce.Speed => new Vector3( 0.2f, 0.5f, -0.8f ).Normal,
				_ => new Vector3( 1f, 0.05f, 0.08f ).Normal,
			};
			s.CastPalm = tilt * s.CastPalm;
			s.OffHandFree = MathF.Max( s.OffHandFree, u );
		}
		return s;
	}

	/// <summary> The form's guard, breathing a little. </summary>
	BladePose GuardPose( float time )
	{
		var f = Form;
		var breathe = MathF.Sin( time * 1.6f );
		var hand = Chest + f.GuardHand + new Vector3( 0, 0, breathe * 0.35f );
		var dir = Rotation.From( -f.GuardPitch + breathe * 1.2f, f.GuardYaw, 0 ).Forward;
		if ( Design.IsStaff )
		{
			// a staff is held across the body at the middle; each form still shows through: how high it's held, how steeply
			// it's tilted (a power form's staff stands more upright) and how far it's turned off square
			hand = Chest + new Vector3( 11f + f.GuardHand.x * 0.15f, -2f + f.GuardHand.y * 0.3f, -4f + f.GuardHand.z * 0.5f + breathe * 0.35f );
			dir = Rotation.From( -10f - f.GuardPitch * 0.35f, 75f + f.GuardYaw * 0.4f, 0 ).Forward;
		}
		var k = ( Vector3.Forward - dir * Vector3.Dot( Vector3.Forward, dir ) );
		if ( k.LengthSquared < 0.01f ) k = Vector3.Up;
		return new BladePose( hand, dir, k );
	}

	BladePose BlockPose( float aimPitch )
	{
		if ( Design.IsStaff ) return new BladePose( Chest + new Vector3( 13f, 0f, 2f ), new Vector3( 0.05f, 1f, 0.35f ), Vector3.Forward );
		// high guard when looking up, otherwise the blade slanted across the body
		if ( aimPitch < -18f ) return new BladePose( Chest + new Vector3( 10f, -3f, 14f ), new Vector3( 0.15f, 1f, 0.1f ), Vector3.Up );
		var f = Form;
		// meeting a known cut: upright on the side a sweep comes from (hilt at the waist, so the blade covers hip to head),
		// a low bar across the hips for a rising cut, and the forte across the line of a thrust
		var side = Incoming switch { SwingDir.Left => 1f, SwingDir.Right => -1f, _ => 0f };
		var out1 = f.BlockOut * Form.Reach;
		if ( side != 0f ) return new BladePose( Chest + new Vector3( out1, side * 9f, -9f ), new Vector3( 0.25f, side * 0.18f, 1f ).Normal, new Vector3( 0f, side, 0f ) );
		if ( Incoming == SwingDir.Rising ) return new BladePose( Chest + new Vector3( out1, -7f, -10f ), new Vector3( 0.3f, 1f, -0.5f ).Normal, Vector3.Forward );
		if ( Incoming == SwingDir.Thrust ) return new BladePose( Chest + new Vector3( out1 + 2f, -3f, -6f ), new Vector3( 0.2f, 0.45f, 0.9f ).Normal, Vector3.Forward );
		// the form's own block: a duellist's parry held out ahead, Soresu's tight upright wall, Djem So's braced high bar
		var lean = Rotation.FromAxis( Vector3.Forward, f.BlockLean );
		var dir = lean * new Vector3( 0.2f, 0.95f, 0.85f ).Normal;
		if ( f.OneHanded ) dir = lean * new Vector3( 0.55f, 0.65f, 0.5f ).Normal;
		return new BladePose( Chest + new Vector3( out1, f.OneHanded ? -5f : -3f, f.BlockHeight ), dir, Vector3.Forward );
	}

	/// <summary> The swing's plane: the normal the blade turns round, and the start and end angles. </summary>
	static (Vector3 Normal, float From, float To) Plane( SwingDir dir, float arc, float tilt )
	{
		var h = arc * 0.5f;
		return dir switch
		{
			SwingDir.Right => ( new Vector3( 0f, -tilt, 1f ).Normal, h, -h ),
			SwingDir.Overhead => ( new Vector3( 0f, 1f, 0.14f ).Normal, -h - 12f, h - 12f ),
			SwingDir.Rising => ( new Vector3( 0f, -( tilt + 0.33f ), 1f ).Normal, -h, h ),
			SwingDir.Spin => ( new Vector3( 0f, 0.1f, 1f ).Normal, -200f, 160f ),
			_ => ( new Vector3( 0f, tilt, 1f ).Normal, -h, h ),            // Left
		};
	}

	/// <summary> The blade on a swing's arc at u (0 = start of the cut, 1 = its end; below 0 is the wind-up, above 1 the follow-through). </summary>
	BladePose ArcPose( SwingDir dir, float arc, float u, out float angle )
	{
		if ( dir == SwingDir.Thrust )
		{
			angle = 0f;
			var reach = Math.Clamp( u, -0.3f, 1.15f );
			var hand = Vector3.Lerp( Chest + new Vector3( 2f, -6f, -3f ), Chest + new Vector3( 24f * Form.Reach, -2f, 1f ), MathF.Max( 0f, reach ) )
				+ ( reach < 0f ? new Vector3( reach * 10f, 0, 0 ) : Vector3.Zero );
			return new BladePose( hand, new Vector3( 1f, 0.02f, 0.05f ), Vector3.Up );
		}
		var (n, from, to) = Plane( dir, arc, Form.CutTilt );
		angle = MathX.Lerp( from, to, u );
		var staff = Design.IsStaff;
		var bladeRot = Rotation.FromAxis( n, angle * ( staff ? 1.5f : 1f ) );
		// the hands lag the blade through the cut (the wrists roll it over) and stay in front of the body
		var handRot = Rotation.FromAxis( n, angle * ( staff ? 0.45f : Form.WristLead ) );
		var d = bladeRot * Vector3.Forward;
		var hd = handRot * Vector3.Forward;
		var hand2 = Chest + hd * ( staff ? 13f : 11f ) * Form.Reach + new Vector3( 0f, -2f, -3f );
		if ( staff )
		{
			// a staff spins like a wheel in front of the body, its plane tipped by the cut (not round the body, which swept
			// the back blade through the wielder's own side): both ends cut ahead and to the sides, the hands held out front
			var axis = ( n * 0.55f + Vector3.Forward * 0.85f ).Normal;
			var start = Vector3.Cross( axis, Vector3.Up );
			if ( start.LengthSquared < 0.01f ) start = Vector3.Left;
			d = Rotation.FromAxis( axis, angle * 1.5f ) * start.Normal;
			hand2 = Chest + new Vector3( 13f * Form.Reach, -2f, -2f ) + hd * 8f;      // (the wheel rides out towards the side being cut)
		}
		// an overhead goes up above the head (not past the face): the hands ride higher and further out as they rise
		if ( dir == SwingDir.Overhead && !staff ) hand2 = Chest + hd * 15f * Form.Reach + new Vector3( -1f, -2f, 1f + MathF.Max( 0f, hd.z ) * 5f );
		// keep the hands from going behind the shoulders
		if ( hand2.x < Chest.x + 3f ) hand2 = hand2.WithX( Chest.x + 3f );
		// the knuckles lead the cut
		var ahead = Rotation.FromAxis( n, ( to > from ? 1f : -1f ) * 8f ) * d;
		return new BladePose( hand2, d, ahead - d );
	}

	/// <summary> How the torso turns through a cut (the form's BodyDrive: a duellist's wrist cut barely turns it, a power cut throws the whole body in). </summary>
	float TwistFor( SwingDir dir, float u ) => Form.BodyDrive * dir switch
	{
		SwingDir.Left => MathX.Lerp( -38f, 40f, u ),
		SwingDir.Right => MathX.Lerp( 36f, -40f, u ),
		SwingDir.Rising => MathX.Lerp( -32f, 34f, u ),
		SwingDir.Spin => MathX.Lerp( -60f, 70f, u ),
		SwingDir.Thrust => MathX.Lerp( 14f, -20f, u ),
		_ => 0f,
	};

	BladePose SwingPose( BladePose guard, ref MotionSample s )
	{
		var tm = SwingTiming;
		var t = _t;
		s.Swinging = true;
		BladePose pose;
		if ( t < tm.Windup )
		{
			// anticipation: drawn back past the start of the cut
			var k = Smooth( t / tm.Windup );
			var start = ArcPose( Dir, _arc, -0.1f * k, out _ );
			pose = BladePose.Lerp( _from, start, k );
			s.Twist = TwistFor( Dir, -0.1f ) * k;
			s.Bend = Dir == SwingDir.Overhead ? -6f * k : 0f;
			// blended already: no extra blend-in on top
			_blendIn = 0f;
		}
		else if ( t < tm.Windup + tm.Strike )
		{
			var u = Cut( ( t - tm.Windup ) / tm.Strike ) * 1.1f - 0.1f;
			pose = ArcPose( Dir, _arc, u, out _ );
			// the body leads the blade: the hips and chest are already turning through by the time it lands
			s.Twist = TwistFor( Dir, MathF.Min( 1.05f, u + 0.18f ) );
			s.Bend = Dir == SwingDir.Overhead ? MathX.Lerp( -6f, 14f, u ) : Dir == SwingDir.Thrust ? 8f * u : 3f;
			s.Lean = Dir == SwingDir.Left ? 5f * u : Dir == SwingDir.Right ? -5f * u : 0f;
			s.HitWindow = true;
			s.Crouch += 0.12f;
		}
		else if ( t < tm.Windup + tm.Strike + tm.Follow )
		{
			// follow-through: on past the end, slowing
			var f = EaseOut( ( t - tm.Windup - tm.Strike ) / tm.Follow );
			pose = ArcPose( Dir, _arc, 1f + 0.12f * f, out _ );
			s.Twist = TwistFor( Dir, 1f + 0.08f * f );
			s.Bend = Dir == SwingDir.Overhead ? 14f : Dir == SwingDir.Thrust ? 8f : 3f;
			s.HitWindow = f < 0.35f;
			s.Crouch += 0.12f * ( 1f - f );
		}
		else
		{
			// recovery back to guard
			var r = Smooth( ( t - tm.Windup - tm.Strike - tm.Follow ) / tm.Recover );
			var end = ArcPose( Dir, _arc, 1.12f, out _ );
			pose = BladePose.Lerp( end, guard, r );
			s.Twist = TwistFor( Dir, 1.08f ) * ( 1f - r );
			s.Bend = ( Dir == SwingDir.Overhead ? 14f : 3f ) * ( 1f - r );
		}
		if ( _heavy ) { s.Bend *= 1.3f; s.Twist *= 1.15f; }
		return pose;
	}

	BladePose ReactPose( BladePose guard, ref MotionSample s )
	{
		var t = _t / _reactLen;
		var hit = t < 0.25f ? Smooth( t / 0.25f ) : 1f - Smooth( ( t - 0.25f ) / 0.75f );
		BladePose knocked;
		switch ( _react )
		{
			case ReactKind.Parried:
				knocked = new BladePose( Chest + new Vector3( 8f, -14f, 6f ), new Vector3( -0.2f, -0.8f, 0.6f ), Vector3.Up );
				s.Twist = -24f * hit; s.Bend = -10f * hit; s.Lean = -6f * hit;
				break;
			case ReactKind.GuardBreak:
			case ReactKind.LockLost:
			case ReactKind.Knocked:
				knocked = new BladePose( Chest + new Vector3( 6f, -12f, -16f ), new Vector3( 0.4f, -0.5f, -0.75f ), Vector3.Forward );
				s.Bend = -16f * hit; s.Twist = -12f * hit; s.Crouch += 0.35f * hit;
				break;
			case ReactKind.BlockShake:
				knocked = BladePose.Lerp( BlockPose( 0f ), guard, 0.2f );
				knocked.Hand += new Vector3( -3f, 0, 0 );
				s.Bend = -4f * hit;
				break;
			default:    // Blocked: bounced back off the other blade
				knocked = new BladePose( guard.Hand + new Vector3( -5f, -4f, 4f ), ( guard.Dir + new Vector3( -0.6f, -0.3f, 0.4f ) ).Normal, guard.Knuckle );
				s.Bend = -7f * hit; s.Twist = -10f * hit;
				break;
		}
		return BladePose.Lerp( guard, knocked, hit );
	}

	BladePose OffPose( BladePose main, BladePose guard, float aimPitch, float time )
	{
		if ( Kind == MotionKind.Swing )
		{
			// the off hand cuts a moment after the main one, on the mirrored arc
			var tm = SwingTiming;
			var lag = tm.Strike * 0.45f;
			var mirror = Dir switch { SwingDir.Left => SwingDir.Right, SwingDir.Right => SwingDir.Left, _ => Dir };
			var t = _t - lag;
			if ( t > tm.Windup && t < tm.Windup + tm.Strike + tm.Follow )
			{
				var u = Cut( ( t - tm.Windup ) / tm.Strike ) * 1.1f - 0.1f;
				var p = ArcPose( mirror, _arc * 0.8f, u, out _ );
				return p.RotatedAbout( Chest, Rotation.FromPitch( Math.Clamp( aimPitch, -50f, 50f ) * 0.5f ) ).Mirrored;
			}
		}
		// held low and back, point down (a reverse grip shoto)
		// (angled out and back, clear of the hip and thigh)
		var low = new BladePose( Chest + new Vector3( 8f, 11f, -7f ), new Vector3( -0.5f, 0.5f, -0.7f ), Vector3.Forward );
		return low.RotatedAbout( Chest, Rotation.FromPitch( Math.Clamp( aimPitch, -50f, 50f ) * 0.5f ) );
	}

	// ----- world space -----

	/// <summary> A body-space pose put into the world: feet at <paramref name="origin"/>, facing <paramref name="yaw"/>, body scale <paramref name="scale"/>. </summary>
	public static BladePose ToWorld( BladePose p, Vector3 origin, float yaw, float scale )
	{
		var r = Rotation.FromYaw( yaw );
		return new BladePose( origin + r * ( p.Hand * scale ), r * p.Dir, r * p.Knuckle );
	}

	/// <summary> The emitter end of a blade and its tip, for a pose already in the world. </summary>
	public static (Vector3 Base, Vector3 Tip) Blade( BladePose world, float hiltLength, float bladeLength, float scale, bool reverse = false )
	{
		var d = reverse ? -world.Dir : world.Dir;
		// the grip sits a little above the hilt's middle: the emitter is the rest of the hilt beyond it
		var emitter = world.Hand + d * ( hiltLength * ( reverse ? 0.5f : 0.42f ) * scale );
		return ( emitter, emitter + d * bladeLength * scale );
	}
}
