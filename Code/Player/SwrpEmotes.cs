using System;

namespace Swrp;

/// <summary>
/// Emotes for an empty-handed trooper (no blaster drawn): a salute and at ease (parade rest), plus the raised wrist comm while
/// talking on the radio. The arms are posed bone by bone: the renderer's bone objects for the upper arm, forearm and hand are
/// taken over from the animation (ProceduralBone) and aimed so the shoulder, elbow and wrist land where a drilled trooper's
/// would: for the salute the upper arm out to the side at shoulder height and the forearm up to the brow, for at ease the
/// elbows back and both hands clasped at the small of the back. Each pose blends in and out from the animated arm, and the
/// bones go back to the animation when it ends. They're toggles: picking the same one again (or Stand Down) ends it, and so
/// does drawing a weapon or going down.
/// </summary>
public static partial class SwrpEmotes
{
	public const int None = 0, Salute = 1, AtEase = 2;
	/// <summary> Riding standing in a gunship bay: the left hand up on the overhead line (set by the craft, not the wheel). </summary>
	public const int Handhold = 3;

	public static string NameOf( int emote ) => emote switch { Salute => "Salute", AtEase => "At ease", _ => "None" };

	/// <summary>
	/// Where an arm should be: the wrist, which way the elbow points, and the hand's rotation. With <see cref="Tip"/> set the
	/// hand is instead held flat and straight in line with the forearm, its fingertips on <see cref="Tip"/> (the salute), with
	/// the back of the hand turned towards <see cref="Back"/>.
	/// </summary>
	readonly record struct ArmPose( Vector3 Wrist, Vector3 Pole, Rotation Hand, Vector3? Tip = null, Vector3 Back = default, bool FlatFingers = false, bool Grip = false );

	/// <summary>
	/// Every machine, every frame, for a body: pose the arms for the emote (<paramref name="weight"/> 0..1 blends in and out
	/// from the animated pose), and raise the left wrist to the helmet while the trooper talks on the radio
	/// (<paramref name="comm"/> 0..1, over anything else the left arm is doing). Both at 0 hand the arms back to the animation.
	/// </summary>
	public static void Apply( SkinnedModelRenderer r, int emote, float weight, float comm = 0f, float carry = 0f, float stride = 0f, float grip = 0f )
	{
		if ( !r.IsValid() ) return;
		HitReact.Tick( r );      // a hit's flinch turns the chest first; the arms below are solved on the flinched body (Code/Fx/HitReact)
		var posing = emote != None && weight > 0.001f;
		var talking = comm > 0.001f;
		// stock (swrp_stock_anims): the guns as s&box animates them, no carry or grip IK; emotes and the radio stay
		// a GMod set: its clips pose the body, the hands go onto the real gun by the same holds as the custom set (Angelo,
		// 2026-10-19: the GMod clips alone held every weapon wrong)
		// a GMod set: the clip holds the gun (hold_R is GMod's own hand attachment); only the left hand goes onto the real
		// gun's foregrip on the two-handed long-gun sets, as TFA's world models sit in GMod's hands
		var gmod = IsGmodSet( r );
		var carrying = carry > 0.001f && ClassDefinition.Custom;
		var gripping = grip > 0.001f && ( ClassDefinition.Custom || TwoHandGmodSet( r ) );
		r.ClearIk( "hand_right" );
		r.ClearIk( "hand_left" );
		LearnRifleGrip( r, carrying );
		// a GMod set's aim layer (SwrpEmotes.GmodAim.cs) first: emotes and the radio wrist go on top of it
		var aimed = ApplyGmodAim( r );
		if ( !aimed ) ReleaseGmodAim( r );
		aimed |= ApplyLoweredWalk( r );      // stock, gun lowered: the arms keep the standing lowered pose on the move
		if ( !posing && !talking && !carrying && !gripping )
		{
			CheekWeld( r, 0f );
			if ( !aimed ) Release( r );
			return;
		}
		if ( !r.CreateBoneObjects ) r.CreateBoneObjects = true;

		var body = StanceYawOf( r ) * r.WorldRotation;      // an NPC's skeleton may be turned side-on under its unturned body (Stance)
		var s = r.WorldScale.z;
		var fwd = body.Forward;
		var right = body.Right;
		var up = Vector3.Up;

		ArmPose? rightArm = null, leftArm = null;
		float rightW = weight, leftW = weight;

		if ( carrying && !posing && CarryPose( r, s, fwd, right, up, stride, out var carryR, out var carryL ) )
		{
			rightArm = carryR;
			leftArm = carryL;
			rightW = leftW = carry;
			CheekWeld( r, pin: true, w: 0f );
		}
		else if ( gripping && !posing && !gmod && PistolPose( r, s, fwd, out var piR, out var piL ) )
		{
			rightArm = piR;
			leftArm = piL;
			rightW = leftW = grip;
			CheekWeld( r, pin: true, w: 0f );
		}
		else if ( gripping && !posing && !gmod && !StockHold && ShoulderPose( r, s, fwd, right, out var shR, out var shL ) )
		{
			// a stocked rifle in the shoulder: the gun placed from the body and aim, both hands locked onto it
			rightArm = shR;
			leftArm = shL;
			rightW = leftW = grip;
			// shouldered at the eye: the head comes down onto the stock (cheek weld)
			CheekWeld( r, pin: true, w: _atEye.Contains( r ) && Aiming.Contains( r ) ? grip : 0f );      // only while aiming down the sights: otherwise the head stays upright (Angelo, 2026-10-16)
		}
		else if ( gripping && !posing && ForegripPose( r, s, right, up, out var support ) )
		{
			// the rifle as animated in the right hand; the left hand out on the real model's handguard
			leftArm = support;
			leftW = grip;
			if ( !gmod ) CheekWeld( r, pin: true, w: 0f );      // (on a GMod set the pin turned the head ~20 degrees off GMod's, anim_score.py)
		}
		if ( posing ) CheekWeld( r, 0f );

		if ( posing && emote == Salute && AnimBone( r, "head", out var head ) )
		{
			// a drill salute, built from the shoulder: the upper arm level and out to the side (a little forward), the forearm
			// rising at 45° in towards the brow, and the hand flat (fingers straight and together) in a straight line with the
			// forearm, palm turned down and in; the fingertips land on the right edge of the helmet's brow
			if ( AnimBone( r, "arm_upper_R", out var su ) && AnimBone( r, "arm_lower_R", out var se ) && AnimBone( r, "hand_R", out var sw ) )
			{
				var upperDir = ( right * SaluteSide + fwd * SaluteForward ).Normal;
				var foreDir = ( -right * 0.68f + up * 0.68f + fwd * SaluteForeForward ).Normal;
				var elbowAt = su.Position + upperDir * su.Position.Distance( se.Position );
				var wristAt = elbowAt + foreDir * se.Position.Distance( sw.Position );
				var back = up * 0.35f + right * 0.75f - fwd * 0.35f;
				back = ( back - foreDir * Vector3.Dot( back, foreDir ) ).Normal;
				rightArm = new ArmPose( wristAt, upperDir, Rotation.LookAt( foreDir, back ), null, default, true );
			}
		}
		else if ( posing && emote == AtEase && AnimBone( r, "pelvis", out var pelvis ) )
		{
			// parade rest: both hands at the small of the back, right hand resting in the left, palms facing out behind; elbows
			// bent and pointing out and back
			var small = pelvis.Position + up * ( AtEaseUp * s ) - fwd * ( AtEaseBack * s );
			rightArm = new ArmPose( small + right * ( 2.6f * s ) - fwd * ( 1f * s ), right * 0.55f - fwd * 0.85f,
				Rotation.LookAt( ( -right * 0.85f - up * 0.45f ).Normal, fwd ) );                  // right palm (-Z) faces back
			leftArm = new ArmPose( small - right * ( 2.6f * s ), -right * 0.55f - fwd * 0.85f,
				Rotation.LookAt( ( right * 0.85f - up * 0.45f ).Normal, -fwd ) );                  // left palm (+Z) faces back
		}

		else if ( posing && emote == Handhold && AnimBone( r, "head", out var hh ) )
		{
			// standing in the bay: the left arm straight up a little forward of the helmet, the fist closed round the overhead
			// grab line (palm forward, knuckles up), the elbow out to the side
			var wrist = hh.Position + up * ( 19f * s ) + fwd * ( 3f * s ) - right * ( 6.5f * s );
			leftArm = new ArmPose( wrist, -right * 0.85f + fwd * 0.1f, Rotation.LookAt( up, fwd ), Grip: true );
		}

		if ( talking && AnimBone( r, "head", out var h ) )
		{
			// the wrist comm: left forearm raised level across the chest, the top of the wrist (where the comm sits) just
			// under the helmet's mouth; elbow out to the left and down; fingers out to the right, palm down (left: +Z = palm)
			var wrist = h.Position - up * ( 4f * s ) + fwd * ( 10f * s ) + right * ( 0.5f * s );
			var hand = Rotation.LookAt( ( right * 0.9f + fwd * 0.25f ).Normal, -up );
			var comPose = new ArmPose( wrist, -right * 0.8f - up * 0.6f + fwd * 0.15f, hand );
			if ( leftArm is { } under && leftW > 0.001f )
			{
				// blend from the emote's left arm into the comm
				leftArm = new ArmPose( Vector3.Lerp( under.Wrist, comPose.Wrist, comm ), Vector3.Lerp( under.Pole, comPose.Pole, comm ), Rotation.Slerp( under.Hand, comPose.Hand, comm ) );
				leftW = MathF.Max( leftW, comm );
			}
			else
			{
				leftArm = comPose;
				leftW = comm;
			}
		}

		// a GMod set's aim layer (and the gun turned onto the crosshair) already posed an arm that has nothing else to do: keep
		// it. Freeing it here threw the aim layer's arm away, so the raised gun pointed where the bare clip did (Angelo, 2026-10-19)
		var keepAim = gmod && aimed && GmodAim.ContainsKey( r );
		if ( rightArm is not null || !keepAim ) PoseArm( r, "R", rightArm, rightW );
		if ( leftArm is not null || !keepAim ) PoseArm( r, "L", leftArm, leftW );
	}

	// ----- the support hand on the rifle -----

	/// <summary>
	/// Where the left hand holds each rifle model, in the gun's grip frame (x along the barrel from the pistol grip, y to the
	/// gun's left, z up; in the model's own units): the wrist, which way the fingers run and which way the palm faces. The
	/// clone rifle clips put the left hand where the original DC-15A had its foregrip, which on the cloud model is back by
	/// the trigger; this moves it out under the handguard. Tune live with swrp_grip.
	/// </summary>
	/// <summary> Bump when the tables below change: a code hotload keeps static fields, so they're rebuilt when this differs. </summary>
	const int GripTablesRevision = 11;
	static int _gripTablesBuilt;
	static Dictionary<string, (Vector3 Wrist, Vector3 Fingers, Vector3 Palm)> _supportGrips;
	static Dictionary<string, (float Stock, float StockZ)> _stocks;
	static Dictionary<string, Vector3> _hips;
	static void EnsureTables()
	{
		if ( _gripTablesBuilt == GripTablesRevision && _supportGrips is not null ) return;
		_gripTablesBuilt = GripTablesRevision;
		_supportGrips = DefaultSupportGrips();
		_stocks = DefaultStocks();
		_hips = DefaultHips();
		_sights = DefaultSights();
	}
	static Dictionary<string, Vector3> _sights;
	/// <summary> Per stocked rifle: the rear sight in the grip frame (model units), which the shouldered hold puts in front of the right eye. </summary>
	public static Dictionary<string, Vector3> Sights { get { EnsureTables(); return _sights; } }
	static Dictionary<string, Vector3> DefaultSights() => new()
	{
		["models/weapons/bf2_dc15le.vmdl"] = new( 0f, 0f, 3.6f ),
		["models/weapons/bf2_dc15a.vmdl"] = new( 0f, 0f, 2.7f ),
		["models/weapons/bf2_e5.vmdl"] = new( -2f, 0f, 4.4f ),
		// measured from each model's own geometry (2026-10-14: the Squad DC-15A and the TFA rifles had no entry, so the hold
		// never ran on them and the hands stayed open; scratchpad gunprof.py, calibrated on the three hand-tuned BF2 rifles)
		["models/weapons/bf2_e5c.vmdl"] = new( 0f, 0f, 5.2f ),
		["models/weapons/bf2_e5s.vmdl"] = new( 0f, 0f, 3.4f ),
		["models/weapons/bf2_valken38x.vmdl"] = new( 0f, 0f, 6.8f ),
		["models/weapons/gc_dc15a.vmdl"] = new( 0f, 0f, 3.2f ),
		["models/weapons/tfa_dc15se.vmdl"] = new( 0f, 0f, 3.7f ),
		["models/weapons/tfa_dp23.vmdl"] = new( 0f, 0f, 3.1f ),
		["models/weapons/tfa_dp24.vmdl"] = new( 0f, 0f, 2.0f ),
		["models/weapons/tfa_sg6.vmdl"] = new( 0f, 0f, 5.3f ),
		["models/weapons/tfa_westarm5.vmdl"] = new( 0f, 0f, 5.6f ),
	};
	public static Dictionary<string, (Vector3 Wrist, Vector3 Fingers, Vector3 Palm)> SupportGrips { get { EnsureTables(); return _supportGrips; } }
	public static Dictionary<string, (float Stock, float StockZ)> Stocks { get { EnsureTables(); return _stocks; } }
	public static Dictionary<string, Vector3> Hips { get { EnsureTables(); return _hips; } }

	static Dictionary<string, (Vector3 Wrist, Vector3 Fingers, Vector3 Palm)> DefaultSupportGrips() => new()
	{
		["models/dc_15a.vmdl"] = ( new Vector3( 6f, 2.8f, -3.6f ), new Vector3( 0.15f, -0.75f, 0.65f ), new Vector3( 0.1f, -0.6f, 0.8f ) ),
		["models/weapons/bf2_dc17m.vmdl"] = ( new Vector3( 8f, 2.4f, -2.4f ), new Vector3( 0.15f, -0.75f, 0.65f ), new Vector3( 0.1f, -0.6f, 0.8f ) ),
		["models/weapons/bf2_z6.vmdl"] = ( new Vector3( 10f, 3.2f, -3f ), new Vector3( 0.15f, -0.75f, 0.65f ), new Vector3( 0.1f, -0.6f, 0.8f ) ),
		// the shouldered rifles: the vertical-grip fist (FistFingers / FistPalm) round each gun's lower edge, fitted to its own
		// geometry with swrp_grip_fit (lift 1.1, gap 0.3) at the old spots along the barrel, checked on the stand 2026-10-16. The
		// palm-up cup before it (and Angelo's 2026-10-13 tweaks to it) put the hand under the guns with the fingers curled round
		// thin air: from the side a raised open hand. The DC-15A's height picked by eye; the DP-24's barrel has no vertices mid-way.
		["models/weapons/gc_dc15a.vmdl"] = ( new Vector3( 9.0f, 2.2f, 1.2f ), FistFingers, FistPalm ),      // on the handguard's left side at mid height (10/1.5/0 sat under it; filmed 2026-10-18)
		["models/weapons/bf2_dc15a.vmdl"] = ( new Vector3( 12.0f, 1.2f, 1.7f ), FistFingers, FistPalm ),
		["models/weapons/bf2_dc15le.vmdl"] = ( new Vector3( 9.2f, 1.3f, -0.2f ), FistFingers, FistPalm ),
		["models/weapons/bf2_e5.vmdl"] = ( new Vector3( 9.7f, 1.3f, 1.3f ), FistFingers, FistPalm ),
		["models/weapons/bf2_e5c.vmdl"] = ( new Vector3( 11.0f, 1.9f, 0.4f ), FistFingers, FistPalm ),
		["models/weapons/bf2_e5s.vmdl"] = ( new Vector3( 11.0f, 1.6f, 3.6f ), FistFingers, FistPalm ),
		["models/weapons/bf2_valken38x.vmdl"] = ( new Vector3( 11.0f, 1.3f, 2.1f ), FistFingers, FistPalm ),
		["models/weapons/tfa_dc15se.vmdl"] = ( new Vector3( 9.2f, 1.9f, -0.8f ), FistFingers, FistPalm ),
		["models/weapons/tfa_dp23.vmdl"] = ( new Vector3( 11.0f, 1.5f, 1.0f ), FistFingers, FistPalm ),
		["models/weapons/tfa_dp24.vmdl"] = ( new Vector3( 10.0f, 2.1f, -0.1f ), FistFingers, FistPalm ),
		["models/weapons/tfa_sg6.vmdl"] = ( new Vector3( 11.0f, 1.7f, 0.4f ), FistFingers, FistPalm ),
		["models/weapons/tfa_westarm5.vmdl"] = ( new Vector3( 8.5f, 1.8f, 3.2f ), FistFingers, FistPalm ),
	};

	/// <summary> The rifle model a body is holding right now (set by whoever draws its gun), for its foregrip. </summary>
	public static readonly Dictionary<SkinnedModelRenderer, string> HeldModel = new();

	/// <summary> The left hand on the held rifle's handguard, in world space, if the model has a foregrip entry. </summary>
	static bool Foregrip( SkinnedModelRenderer r, float s, out Transform hold, out (Vector3 Wrist, Vector3 Fingers, Vector3 Palm) grip )
	{
		hold = default;
		grip = default;
		if ( HeldModel.Count > 128 )
			foreach ( var gone in HeldModel.Keys.Where( k => !k.IsValid() ).ToList() ) HeldModel.Remove( gone );
		if ( !HeldModel.TryGetValue( r, out var path ) || path is null || !SupportGrips.TryGetValue( path, out grip ) ) return false;
		if ( r.GetAttachment( "hold_R" ) is not { } h ) return false;
		hold = new Transform( h.Position + h.Rotation * ( WeaponStats.GripShift * s ), h.Rotation );      // the model's own frame
		return true;
	}

	static bool ForegripPose( SkinnedModelRenderer r, float s, Vector3 right, Vector3 up, out ArmPose arm )
	{
		arm = default;
		if ( !Foregrip( r, s, out var hold, out var grip ) ) return false;
		arm = SupportArm( hold, grip, s, right, up );
		// a GMod set: the elbow where GMod's clip has it (the fixed pole put it 15-35 degrees off, scratchpad anim_score.py)
		if ( IsGmodSet( r ) && AnimBone( r, "arm_upper_L", out var sh ) && AnimBone( r, "arm_lower_L", out var el ) && AnimBone( r, "hand_L", out var wr ) )
		{
			var pole = el.Position - ( sh.Position + wr.Position ) * 0.5f;
			if ( pole.Length > 0.5f ) arm = arm with { Pole = pole.Normal };
		}
		return true;
	}

	static ArmPose SupportArm( Transform hold, (Vector3 Wrist, Vector3 Fingers, Vector3 Palm) grip, float s, Vector3 right, Vector3 up )
	{
		var wrist = hold.Position + hold.Rotation * ( grip.Wrist * s );
		var rot = Rotation.LookAt( hold.Rotation * grip.Fingers.Normal, hold.Rotation * grip.Palm.Normal );     // left hand: +Z = palm
		return new ArmPose( wrist, -right * 0.55f - up * 0.8f, rot );
	}

	/// <summary> The vertical-grip support hold (Angelo's BF2 photos, 2026-10-16): fingers forward along the gun and curling round
	/// under it, palm in against its left side, so the hand reads as a fist round the gun's lower edge. (The old palm-up cup put
	/// the fingers round thin air below the guns: a raised open hand from the side.) </summary>
	public static readonly Vector3 FistFingers = new( 0.97f, 0f, -0.25f ), FistPalm = new( 0f, -0.95f, 0.3f );

	/// <summary>
	/// Dev: fit the vertical-grip support hold to a rifle's own geometry (keeping its table's spot along the barrel): the wrist just
	/// off the gun's left side, at the height of its lower edge there. Logs the values to paste into DefaultSupportGrips.
	/// </summary>
	[ConCmd( "swrp_grip_fit", Help = "swrp_grip_fit [model path or 'all'] [height above the lower edge] [gap off the side] - dev: fit the fist support hold to a rifle's geometry" )]
	public static void GripFitCommand( string model = "all", float lift = 1.1f, float gap = 0.3f )
	{
		foreach ( var path in SupportGrips.Keys.ToList() )
		{
			if ( model != "all" && model != path || !Stocks.ContainsKey( path ) ) continue;
			var w = ItemRegistry.All.Select( i => i.Weapon ).FirstOrDefault( x => x?.ModelPath == path );
			var m = Model.Load( path );
			var verts = m?.GetVertices();
			if ( w is null || verts is null || verts.Length == 0 ) { Log.Info( $"[SWRP] grip fit: no geometry for {path}" ); continue; }
			var place = new Transform( w.ModelOffset, w.ModelAngles.ToRotation(), w.ModelScale );
			var x0 = SupportGrips[path].Wrist.x;
			// the gun body over the hand's span (wrist to fingertips), leaving out anything sticking far out to a side
			var pts = verts.Select( v => place.PointToWorld( v.Position ) ).Where( p => p.x > x0 && p.x < x0 + 4f && MathF.Abs( p.y ) < 2.2f ).ToList();
			if ( pts.Count == 0 ) { Log.Info( $"[SWRP] grip fit: {path} has nothing at x {x0}" ); continue; }
			var zs = pts.Select( p => p.z ).OrderBy( z => z ).ToList();
			var bottom = zs[Math.Min( zs.Count - 1, zs.Count / 20 )];      // (5th percentile: one stray vertex doesn't pull the hand down)
			var left = pts.Max( p => p.y );
			var wrist = new Vector3( x0, left + gap, bottom + lift );
			SupportGrips[path] = ( wrist, FistFingers, FistPalm );
			Log.Info( $"[SWRP] grip fit {path}: wrist {wrist.x:0.0} {wrist.y:0.0} {wrist.z:0.0} (lower edge {bottom:0.0}, left side {left:0.0})" );
		}
	}

	[ConCmd( "swrp_grip", Help = "swrp_grip <x y z> [fx fy fz] [px py pz] [model] - dev: set where the left hand holds a rifle (grip frame; see SwrpEmotes.SupportGrips)" )]
	public static void GripCommand( float x, float y, float z, float fx = 0.2f, float fy = -0.8f, float fz = 0.55f, float px = 0f, float py = -0.5f, float pz = 0.85f, string model = "models/weapons/bf2_dc15a.vmdl" )
	{
		SupportGrips[model] = ( new Vector3( x, y, z ), new Vector3( fx, fy, fz ), new Vector3( px, py, pz ) );
		Log.Info( $"[SWRP] {model} left hand: wrist {x} {y} {z}, fingers {fx} {fy} {fz}, palm {px} {py} {pz}" );
	}

	// ----- the shouldered rifle -----

	/// <summary>
	/// Per stocked rifle (grip frame, model units): how far behind the pistol grip the buttplate is and the height of its
	/// middle. Rifles listed here are held with the stock in the right shoulder pocket; anything else (the Z-6, pistols) keeps
	/// the animation's hold. Measured with swrp_gun_profile.
	/// </summary>
	static Dictionary<string, (float Stock, float StockZ)> DefaultStocks() => new()
	{
		["models/weapons/bf2_dc15le.vmdl"] = ( 11.5f, 1.5f ),     // DC-15A (and the DC-15X)
		["models/weapons/bf2_dc15a.vmdl"] = ( 5.5f, 1.3f ),       // DC-15S, folded stock
		["models/weapons/bf2_e5.vmdl"] = ( 7.5f, 2.7f ),
		// measured from each model's own geometry (2026-10-14: the Squad DC-15A and the TFA rifles had no entry, so the hold
		// never ran on them and the hands stayed open; scratchpad gunprof.py, calibrated on the three hand-tuned BF2 rifles)
		["models/weapons/bf2_e5c.vmdl"] = ( 13.7f, 0.5f ),
		["models/weapons/bf2_e5s.vmdl"] = ( 10.5f, 0.8f ),
		["models/weapons/bf2_valken38x.vmdl"] = ( 12.6f, 2.4f ),
		["models/weapons/gc_dc15a.vmdl"] = ( 13.2f, 1.3f ),
		["models/weapons/tfa_dc15se.vmdl"] = ( 5.0f, 0.5f ),
		["models/weapons/tfa_dp23.vmdl"] = ( 11.7f, 1.5f ),
		["models/weapons/tfa_dp24.vmdl"] = ( 11.3f, 1.3f ),
		["models/weapons/tfa_sg6.vmdl"] = ( 8.9f, 0.5f ),
		["models/weapons/tfa_westarm5.vmdl"] = ( 5.8f, 0.7f ),
	};

	/// <summary>
	/// s&box's own rifle hold (the human graph's holdtype animations, aim and fire) instead of our shouldered pose; only the
	/// left hand is still put on the real handguard. For comparing the two (Angelo, 2026-10-18).
	/// </summary>
	/// <summary> How far the rifle line keeps from the centre of the head (model units). </summary>
	[ConVar( "swrp_helmet_clear", Help = "Model units the rifle keeps from the centre of the head", Saved = true )]
	public static float HelmetClearance { get; set; } = 5.5f;

	[ConVar( "swrp_stock_hold", Help = "1 = guns use s&box's own hold animations (left hand still on the handguard); 0 = our shouldered pose", Saved = true )]
	public static bool StockHold { get; set; }

	/// <summary> How far down the rifle points at the low ready (safety, sprinting), degrees. </summary>
	[ConVar( "swrp_low_ready", Help = "Degrees the rifle points down at the low ready (safety, sprinting)", Saved = true )]
	public static float LowReadyPitch { get; set; } = 30f;

	[ConVar( "swrp_grip_debug", Help = "Dev: log why the shouldered rifle pose does or doesn't apply" )]
	public static bool GripDebug { get; set; }
	static RealTimeSince _sinceGripDebug;

	/// <summary>
	/// Heavy guns with no stock, carried at the hip instead (the Z-6): where the pistol grip sits from the pelvis (forward, out to
	/// the right, up) in model units.
	/// </summary>
	static Dictionary<string, Vector3> DefaultHips() => new()
	{
		["models/weapons/bf2_z6.vmdl"] = new( 9f, 5.5f, 9f ),
	};

	/// <summary> Bodies whose rifle is lowered (weapon safety): the low ready from the shoulder pocket instead of up at the eye. </summary>
	public static readonly HashSet<SkinnedModelRenderer> Lowered = new();

	/// <summary> Bodies aiming down the sights: the rifle comes up level into the top of the shoulder. Otherwise it's held ready
	/// across the chest, muzzle a little down and in (Angelo's reference, 2026-10-12). </summary>
	public static readonly HashSet<SkinnedModelRenderer> Aiming = new();

	// the cheek weld (tunable live with swrp_cheek): the right eye from the head bone, and where the rear sight sits from it
	public static float EyeUp { get; set; } = 3.2f;
	public static float EyeForward { get; set; } = 3.6f;
	public static float EyeRight { get; set; } = 1.3f;
	public static float EyeRelief { get; set; } = 4.5f;
	public static float SightBelowEye { get; set; } = 0.9f;
	// the shouldered (up and ready) pocket: from the shoulder joint in towards the chest, forward and up onto the top of the pad
	public static float HighPocketIn { get; set; } = 1.0f;      // further in or back and the stock sinks into the chest plate (checked top-down on the stand)
	public static float HighPocketForward { get; set; } = 4.0f;      // 1.0 in / 4.0 fwd since the new human graph: 2.5 / 1.5 put the stock inside the shoulder plate and the gun against the helmet (Angelo, 2026-10-18)
	public static float HighPocketUp { get; set; } = 0.5f;      // pocket 2.5 in, 1.5 fwd, 0.5 up + AimInward 20: tuned on the stand against Angelo's references 2026-10-16 (the old 1.2/4.5/2.5 left the support arm locked straight, short of the foregrip)
	/// <summary> Shouldered at the eye: degrees the rifle turns in across the chest (swrp_aim_inward). 0 with the body side-on:
	/// the barrel straight on the crosshair, and the support hand still reaches the foregrip with a bent elbow (2026-10-16). </summary>
	public static float AimInward { get; set; } = 0f;
	/// <summary> Aiming down the sights: how much higher the stock rides (inches) and how much of the inward angle stays (swrp_ads). </summary>
	public static float AdsRaise { get; set; } = 2.5f;
	/// <summary> Shouldered (off safety): the body turns this far right of the aim, left shoulder to the target, as in Angelo's
	/// references (2026-10-16); standing still the shooting stance comes with it (<see cref="Stance"/>). </summary>
	[ConVar( "swrp_blade", Help = "Shouldered rifle: degrees the body turns side-on (left shoulder to the target)", Saved = true )]
	public static float BladeYaw { get; set; } = 30f;
	public static float AdsInwardShare { get; set; } = 0.35f;

	[ConCmd( "swrp_ads", Help = "swrp_ads <raise inches> <inward share 0-1> - dev: how the shouldered rifle comes up when aiming down the sights" )]
	public static void AdsCommand( float raise, float share ) { AdsRaise = raise; AdsInwardShare = share; }
	/// <summary> Shouldered at the eye: how far the head drops and rolls onto the stock (swrp_cheek_weld pitch roll). </summary>
	public static float CheekPitch { get; set; } = 12f;
	public static float CheekRoll { get; set; } = 8f;
	static readonly HashSet<SkinnedModelRenderer> _atEye = new();
	/// <summary> Shouldered: how far out to the side the right elbow points (0 straight down; it always points 0.8 down). </summary>
	public static float RightElbowOut { get; set; } = 0.6f;

	[ConCmd( "swrp_elbow", Help = "swrp_elbow <out> - dev: how far out the right elbow points when the rifle is shouldered (0 = straight down)" )]
	public static void ElbowCommand( float outward ) => RightElbowOut = outward;

	[ConCmd( "swrp_aim_inward",Help = "swrp_aim_inward <degrees> - dev: how far the shouldered rifle turns in across the chest" )]
	public static void AimInwardCommand( float deg ) => AimInward = deg;

	[ConCmd( "swrp_cheek_weld", Help = "swrp_cheek_weld <pitch> <roll> - dev: how far the head drops and leans onto the stock when shouldered" )]
	public static void CheekCommand2( float pitch, float roll ) { CheekPitch = pitch; CheekRoll = roll; }

	/// <summary> The head down and leaned in onto the stock, on top of the animated head (w 0 hands it back). With the shooting
	/// stance leaning the chest, the neck and head take most of that lean back out, so the head stays nearly upright. </summary>
	static void CheekWeld( SkinnedModelRenderer r, float w, bool pin = false )
	{
		var neck = r.CreateBoneObjects ? r.GetBoneObject( "neck_0" ) : null;
		var head = r.CreateBoneObjects ? r.GetBoneObject( "head" ) : null;
		var leaned = _stance.TryGetValue( r, out var st ) && ( st.LeanW > 0.001f || st.Drop > 0.001f || MathF.Abs( st.TwistDeg ) > 0.01f );
		if ( w <= 0.001f && !leaned && pin && head.IsValid() && RawBone( r, "head", out var hr ) && RawBone( r, "neck_0", out var nr ) )
		{
			// just the neck length (below): the head turned as animated
			Free( neck );
			Take( head ).LocalRotation = nr.Rotation.Inverse * hr.Rotation;
			PinHeadLength( r, head );
			return;
		}
		if ( w <= 0.001f && !leaned || !neck.IsValid() || !head.IsValid() ) { Free( neck ); Free( head ); return; }
		if ( !AnimBone( r, "neck_0", out var n ) || !AnimBone( r, "head", out var h ) ) return;
		// (and all of the chest's own turn, so the head still looks where the animation aimed it)
		var counter = leaned ? Rotation.Slerp( Rotation.Identity, st.Pitch.Inverse, StanceHead ) * st.Twist.Inverse : Rotation.Identity;
		// pitch about the aim's right axis (down), roll about the aim (towards the right shoulder); the body may be turned side-on
		var look = AimDir.TryGetValue( r, out var ad ) && ad.WithZ( 0 ).LengthSquared > 0.01f ? Rotation.LookAt( ad.WithZ( 0 ) ) : StanceYawOf( r ) * r.WorldRotation;
		var tilt = Rotation.FromAxis( look.Right, -CheekPitch * w * 0.5f ) * Rotation.FromAxis( look.Forward, CheekRoll * w * 0.5f );
		Take( neck ).WorldRotation = tilt * Rotation.Slerp( Rotation.Identity, counter, 0.5f ) * n.Rotation;
		Take( head ).WorldRotation = tilt * tilt * counter * h.Rotation;
		if ( pin ) PinHeadLength( r, head );
	}

	/// <summary>
	/// The rifle animations move the head bone 1.7 in up off the neck (neck_0 to head 6.8 against 5.2 at rest, measured with
	/// swrp_neck_debug): the trooper stretched his neck with a gun up (Angelo, 2026-10-18). Back to the skeleton's own length.
	/// </summary>
	static void PinHeadLength( SkinnedModelRenderer r, GameObject head )
	{
		if ( r.Model?.Bones.GetBone( "head" ) is not { } bone || bone.Parent is not { } parent || r.GetBoneObject( parent ) is not { } neck ) return;
		if ( !RawBone( r, "head", out var h ) || !RawBone( r, parent.Name, out var n ) ) return;
		var rest = r.Model.GetBoneTransform( bone.Index ).Position.Distance( r.Model.GetBoneTransform( parent.Index ).Position ) * r.WorldScale.z;
		var dir = ( h.Position - n.Position ).Normal;
		Take( head ).WorldPosition = neck.WorldPosition + neck.WorldRotation * ( n.Rotation.Inverse * dir ) * rest;
	}

	[ConCmd( "swrp_pocket", Help = "swrp_pocket <in> <forward> <up> - dev: where the stock sits in the shoulder when the rifle is up" )]
	public static void PocketCommand( float pocketIn, float forward, float up ) { HighPocketIn = pocketIn; HighPocketForward = forward; HighPocketUp = up; }

	[ConCmd( "swrp_cheek", Help = "swrp_cheek <eye up> <eye forward> <eye right> <relief> <sight below eye> - dev: tune the shouldered rifle at the eye" )]
	public static void CheekCommand( float up, float forward, float rightOff, float relief, float below )
	{
		EyeUp = up; EyeForward = forward; EyeRight = rightOff; EyeRelief = relief; SightBelowEye = below;
	}

	/// <summary> Where each body is aiming (set by whoever drives it: the player's eyes, an NPC's target); body forward if unset. </summary>
	public static readonly Dictionary<SkinnedModelRenderer, Vector3> AimDir = new();

	/// <summary> Where each body's crosshair lands (players): the shouldered rifle points from the shoulder at it. </summary>
	public static readonly Dictionary<SkinnedModelRenderer, Vector3> AimPoint = new();

	// the shoulder pocket and how the rifle sits in it (tunable live with swrp_shoulder)
	public static float PocketIn { get; set; } = 1f;          // from the shoulder joint in towards the chest
	public static float PocketForward { get; set; } = 5f;      // in front of the pad: further back the stock sinks into the chest   // and forward onto the front of the shoulder
	public static float PocketDown { get; set; } = -2f;
	public static float ShoulderDrop { get; set; } = 20f;      // the barrel dipped a little below the line of sight at rest (degrees)
	public static float RifleInward { get; set; } = 38f;      // the ready hold: diagonally across the chest (Angelo's BF2 reference)       // degrees the barrel turns in across the body

	[ConCmd( "swrp_shoulder", Help = "swrp_shoulder <in> <forward> <down> [drop degrees] [inward degrees] - dev: tune where a rifle's stock sits in the shoulder" )]
	public static void ShoulderCommand( float pocketIn, float forward, float down, float drop = 6f, float inward = 9f )
	{
		PocketIn = pocketIn; PocketForward = forward; PocketDown = down; ShoulderDrop = drop; RifleInward = inward;
	}

	/// <summary>
	/// Pistols held out in both hands along the aim (Angelo, 2026-10-18: the stock pistol pose held the DC-17 muzzle up by the
	/// head): the grip from the right shoulder (forward, in across the body, down), model units.
	/// </summary>
	public static Vector3 PistolReach { get; set; } = new( 15f, 4.5f, 3f );
	/// <summary> The right hand moved along the pistol from where hold_R puts it (x forward, z up the gun). </summary>
	public static Vector3 PistolHand { get; set; }

	[ConCmd( "swrp_pistol_hand", Help = "swrp_pistol_hand <forward> <left> <up> - dev: slide the right hand along the pistol" )]
	public static void PistolHandCommand( float x, float y, float z ) => PistolHand = new Vector3( x, y, z );
	public static readonly HashSet<string> Pistols = new()
	{
		"models/weapons/bf2_dc17.vmdl", "models/weapons/bf2_dc17_ext.vmdl", "models/weapons/bf2_rg4d.vmdl", "models/weapons/bf2_se14c.vmdl",
		"models/weapons/tfa_dc15sa.vmdl", "models/weapons/tfa_dc17s.vmdl", "models/weapons/tfa_se14.vmdl",
	};
	/// <summary> The left hand cupped under and round the right on a pistol (wrist in the grip frame, fingers, palm). </summary>
	public static (Vector3 Wrist, Vector3 Fingers, Vector3 Palm) PistolSupport { get; set; } = ( new Vector3( -1.5f, 2.2f, -1.5f ), FistFingers, FistPalm );      // a fist round the right hand's fingers from the left (filmed 2026-10-18)

	[ConCmd( "swrp_pistol", Help = "swrp_pistol <forward> <in> <down> [left x y z] - dev: where a pistol is held out in both hands" )]
	public static void PistolCommand( float forward, float inward, float down, float lx = -1.5f, float ly = 2.2f, float lz = -1.5f, float fx = 0.97f, float fy = 0f, float fz = -0.25f, float px = 0f, float py = -0.95f, float pz = 0.3f )
	{
		PistolReach = new Vector3( forward, inward, down );
		PistolSupport = ( new Vector3( lx, ly, lz ), new Vector3( fx, fy, fz ).Normal, new Vector3( px, py, pz ).Normal );
	}

	static bool PistolPose( SkinnedModelRenderer r, float s, Vector3 fwd, out ArmPose rightArm, out ArmPose leftArm )
	{
		rightArm = leftArm = default;
		if ( GripDebug && _sinceGripDebug > 1f )
		{
			_sinceGripDebug = 0;
			Log.Info( $"[SWRP] pistol {r.GameObject.Name}: learned {r.Model is { } m0 && _gripsV3.ContainsKey( m0 )} held {( HeldModel.TryGetValue( r, out var h0 ) ? h0 : "-" )} holdtype {r.Parameters.GetInt( "holdtype" )}" );
		}
		if ( !GripOf( r, out var grip ) ) return false;
		if ( !HeldModel.TryGetValue( r, out var path ) || path is null || !Pistols.Contains( path ) ) return false;
		if ( !AnimBone( r, "arm_upper_R", out var shoulder ) ) return false;
		var aim = AimDir.TryGetValue( r, out var a ) && a.LengthSquared > 0.01f ? a.Normal : fwd;
		if ( AimPoint.TryGetValue( r, out var point ) && ( point - shoulder.Position ).LengthSquared > 400f ) aim = ( point - shoulder.Position ).Normal;
		var ang = Rotation.LookAt( aim ).Angles();
		ang.pitch = Lowered.Contains( r ) ? LowReadyPitch : Math.Clamp( ang.pitch, -60f, 60f );
		var gunRot = Rotation.From( ang.WithRoll( 0 ) );
		var flatRight = Rotation.FromYaw( ang.yaw ).Right;
		var gripPos = shoulder.Position + gunRot.Forward * ( PistolReach.x * s ) - flatRight * ( PistolReach.y * s ) - Vector3.Up * ( PistolReach.z * s );
		var life = HoldLife( r, s, gunRot );
		gripPos += life.Offset;
		gunRot = life.Turn * gunRot;
		// hold_R is a bone the graph animates off the hand, differently per hold type: take this frame's hand -> hold_R
		var handToHold = RawBone( r, "hand_R", out var hb ) && RawBone( r, "hold_R", out var hh ) ? hb.ToLocal( hh ) : grip.Hold;
		var handRot = gunRot * handToHold.Rotation.Inverse;
		// the model is drawn GripShift off hold_R (set for the rifles): the hand goes where that puts the pistol's grip at gripPos
		var handPos = gripPos - gunRot * ( ( WeaponStats.GripShift - PistolHand ) * s ) - handRot * handToHold.Position;
		rightArm = new ArmPose( handPos, flatRight * 0.6f - Vector3.Up * 0.8f, handRot, Grip: true );
		if ( GripDebug && _sinceGripDebug < 0.02f && r.GetAttachment( "hold_R" ) is { } nowP )
			Log.Info( $"[SWRP] pistol want fwd {gunRot.Forward}  now hold_R fwd {nowP.Rotation.Forward} at {nowP.Position.Distance( gripPos ) / s:0.0} off; hand->hold {handToHold.Position} {handToHold.Rotation.Angles()} learned {grip.Hold.Position} {grip.Hold.Rotation.Angles()}; R at {gunRot.Inverse * ( ( r.GetBoneObject( "hand_R" )?.WorldPosition ?? 0 ) - gripPos ) / s} L at {gunRot.Inverse * ( ( r.GetBoneObject( "hand_L" )?.WorldPosition ?? 0 ) - gripPos ) / s} s {s}" );
		var hold = new Transform( gripPos, gunRot );
		leftArm = SupportArm( hold, PistolSupport, s, flatRight, Vector3.Up ) with { Pole = -flatRight * 0.6f - Vector3.Up * 0.8f, Grip = true };
		return true;
	}

	static bool ShoulderPose( SkinnedModelRenderer r, float s, Vector3 fwd, Vector3 right, out ArmPose rightArm, out ArmPose leftArm )
	{
		rightArm = leftArm = default;
		if ( GripDebug && _sinceGripDebug > 1f )
		{
			_sinceGripDebug = 0;
			Log.Info( $"[SWRP] shoulder {r.GameObject.Name}: model {r.Model?.ResourcePath} learned {( r.Model is { } m0 && _gripsV3.TryGetValue( m0, out var g0 ) ? $"hold {g0.Hold.Position} {g0.Hold.Rotation.Angles()} left {g0.LeftOnRifle.Position}" : "no" )} held {( HeldModel.TryGetValue( r, out var h0 ) ? h0 : "-" )} holdtype {r.Parameters.GetInt( "holdtype" )}" );
		}
		if ( !GripOf( r, out var grip ) ) return false;
		if ( AimDir.Count > 128 )
			foreach ( var gone in AimDir.Keys.Where( k => !k.IsValid() ).ToList() ) { AimDir.Remove( gone ); AimPoint.Remove( gone ); }
		if ( !HeldModel.TryGetValue( r, out var path ) || path is null ) return false;
		var hip = Hips.TryGetValue( path, out var hipAt );
		if ( !Stocks.TryGetValue( path, out var stock ) && !hip ) return false;
		if ( !AnimBone( r, "arm_upper_R", out var shoulder ) || !AnimBone( r, "pelvis", out var pelvis ) ) return false;

		// aim: where the trooper looks, a touch low so the sights sit under the eye line; never more than 60 degrees up or down
		var aim = AimDir.TryGetValue( r, out var a ) && a.LengthSquared > 0.01f ? a.Normal : fwd;
		// with a target point (the player's crosshair): from the shoulder straight at it, so the barrel lines up on what they shoot
		if ( AimPoint.TryGetValue( r, out var point ) && ( point - shoulder.Position ).LengthSquared > 400f ) aim = ( point - shoulder.Position ).Normal;
		var ang = Rotation.LookAt( aim ).Angles();
		var lowered = Lowered.Contains( r );
		var sight = Vector3.Zero;
		Transform head = default;
		// off safety the rifle is always up in the shoulder at the eye, ready and firing (Angelo, 2026-10-13); on safety it's the
		// ready hold across the chest (what off safety used to be)
		var atEye = !hip && !lowered && Sights.TryGetValue( path, out sight ) && AnimBone( r, "head", out head );
		ang.pitch = hip ? Math.Clamp( ang.pitch * 0.5f, -25f, 25f ) : atEye ? Math.Clamp( ang.pitch, -60f, 60f ) : Math.Clamp( ang.pitch + ShoulderDrop, -60f, 60f );      // a hip gun only half follows the aim
		// the low ready (safety, sprinting): stock still in the shoulder, muzzle a fixed way down and across the body; following
		// the aim plus the drop pointed the rifle almost straight at the floor along the leg (Angelo, 2026-10-14)
		if ( lowered && !hip ) ang.pitch = LowReadyPitch;
		// a shouldered rifle points in across the body towards the eye line (the stock is in the right shoulder, the sights in
		// front of the right eye), which also brings the handguard within the support arm's reach
		if ( !hip && !atEye ) ang.yaw += RifleInward;
		// shouldered at the eye the rifle still angles in across the chest a little, so the support hand reaches the foregrip
		// with a bent elbow (Angelo's references, 2026-10-16)
		// aiming down the sights (right mouse) the rifle comes straighter onto the aim line and up to the eye, the head down onto it
		var ads = atEye && Aiming.Contains( r );
		if ( atEye ) { ang.yaw += ads ? AimInward * AdsInwardShare : AimInward; _atEye.Add( r ); } else _atEye.Remove( r );
		var gunRot = Rotation.From( ang.WithRoll( 0 ) );
		var gunFwd = gunRot.Forward;
		var gunUp = gunRot.Up;
		var flatRight = gunRot.Right;

		// the buttplate in the pocket: in from the shoulder joint, forward onto the chest plate, a little down
		var pocket = shoulder.Position - flatRight * ( PocketIn * s ) + fwd * ( PocketForward * s ) - Vector3.Up * ( PocketDown * s );
		var shift = WeaponStats.GripShift;
		var gripPos = hip
			? pelvis.Position + fwd * ( hipAt.x * s ) + right * ( hipAt.y * s ) + Vector3.Up * ( hipAt.z * s ) - gunRot * ( shift * s )
			: pocket + gunFwd * ( ( stock.Stock - shift.x ) * s ) - gunUp * ( ( stock.StockZ + shift.z ) * s );
		if ( atEye )
		{
			// shouldered and level (Angelo's references, 2026-10-12): the buttplate in the top of the shoulder pocket and the rifle
			// level along the aim, so the sights ride just under the cheek. Placing the sights at the eye instead floated the stock
			// 9 units over the shoulder, beside the helmet (measured with swrp_grip_debug).
			var highPocket = shoulder.Position - flatRight * ( HighPocketIn * s ) + fwd * ( HighPocketForward * s ) + Vector3.Up * ( ( HighPocketUp + ( ads ? AdsRaise : 0f ) ) * s );
			gripPos = highPocket + gunFwd * ( ( stock.Stock - shift.x ) * s ) - gunUp * ( ( stock.StockZ + shift.z ) * s );
		}
		if ( GripDebug && _sinceGripDebug < 0.02f && !hip )
		{
			var butt = gripPos + gunRot * ( ( new Vector3( -stock.Stock, 0f, stock.StockZ ) + shift ) * s );
			var body = r.WorldRotation;
			var rel = body.Inverse * ( butt - shoulder.Position ) / s;
			var headRel = AnimBone( r, "head", out var hd ) ? body.Inverse * ( hd.Position - shoulder.Position ) / s : Vector3.Zero;
			Log.Info( $"[SWRP] butt from shoulder joint (fwd, left, up): {rel.x:0.0} {rel.y:0.0} {rel.z:0.0}  head from shoulder: {headRel.x:0.0} {headRel.y:0.0} {headRel.z:0.0}  atEye {atEye}" );
		}

		// keep the gun out of the helmet: the Squad aim stance hunches the head forward over the right shoulder, and the rifle
		// line went straight through it (Angelo, 2026-10-18); push the gun sideways off the head until the barrel clears it
		if ( !hip && AnimBone( r, "head", out var hb ) )
		{
			var toHead = hb.Position - gripPos;
			var side = toHead - gunFwd * Vector3.Dot( toHead, gunFwd );
			var clear = HelmetClearance * s;
			if ( Vector3.Dot( toHead, gunFwd ) > 0f && side.Length < clear )
				gripPos -= ( side.Length > 0.01f ? side.Normal : flatRight * -1f ) * ( clear - side.Length );
		}

		// life in the hold: a slow breathing sway, a bob with each step while walking, and a kick back and up when the gun fires
		var life = HoldLife( r, s, gunRot );
		gripPos += life.Offset;
		gunRot = life.Turn * gunRot;

		// the right hand that puts the grip there (hand -> hold_R as the animation holds it), the left on the handguard
		var handRot = gunRot * grip.Hold.Rotation.Inverse;
		var handPos = gripPos - handRot * grip.Hold.Position;      // (as CarryPose)
		// the right elbow down and a little out under the gun (Angelo's references, 2026-10-16: out level it read as a chicken wing)
		rightArm = new ArmPose( handPos, flatRight * RightElbowOut - Vector3.Up * 0.8f, handRot, Grip: true );
		var hold = new Transform( gripPos + gunRot * ( shift * s ), gunRot );      // the model's own frame, for the foregrip
		if ( GripDebug && _sinceGripDebug < 0.02f && r.GetAttachment( "hold_R" ) is { } now )
			Log.Info( $"[SWRP] shoulder want grip {gripPos} fwd {gunFwd}  now hold_R {now.Position} fwd {now.Rotation.Forward}  shoulder {shoulder.Position} hand target {handPos}" );
		leftArm = SupportGrips.TryGetValue( path, out var fg )
			? SupportArm( hold, fg, s, right, Vector3.Up ) with { Pole = atEye ? -flatRight * 0.12f - Vector3.Up * 1f : -flatRight * 0.35f - Vector3.Up * 0.9f, Grip = true }
			: new ArmPose( new Transform( handPos, handRot ).ToWorld( grip.LeftOnRifle ).Position, -flatRight * 0.35f - Vector3.Up * 0.9f, new Transform( handPos, handRot ).ToWorld( grip.LeftOnRifle ).Rotation );
		return true;
	}

	// ----- the shooting stance -----

	/// <summary>
	/// The rifle up in the shoulder (Angelo's BF2 references, 2026-10-16): standing still the hips come down, the feet split along
	/// the aim (left foot forward towards the target, right foot back and turned out) and the knees bend; the chest leans into the
	/// gun with the head nearly upright. Moving, the walk cycle keeps the legs and only the lean stays. Posed on the bone objects
	/// like the arms: the pelvis brought down (and, for a body that can't be turned itself, turned side-on), spine_1 leaned, each
	/// leg solved onto its foot; everything that reads the animated upper body (arm solve, cheek weld, flinch) reads it through
	/// <see cref="StanceAdjust"/>. Skeletons without the Citizen spine and legs are left alone.
	/// </summary>
	[ConVar( "swrp_stance", Help = "Shouldered rifle stance: hips down, feet split along the aim, chest leaned into the gun (0 = off)", Saved = true )]
	public static bool StanceOn { get; set; } = true;
	// tuned on the test stand against Angelo's five BF2 photos, 2026-10-16 (side, behind-left, close right, front; swrp_stance_debug:
	// right elbow 86°, left 111°, shoulders 44° off the aim)
	// 2.5 / 0 / 4 since 2026-10-18: 8 in down, 12° hip tip and 14° chest lean read as troopers tilted and leaning (Angelo)
	public static float StanceDrop { get; set; } = 2.5f;          // inches the hips come down (standing still)
	public static float StanceHipTilt { get; set; } = 0f;      // degrees the hips tip forward with them (standing still: the whole back leans)
	public static float StanceLean { get; set; } = 4f;         // degrees the chest leans into the gun (moving too)
	public static float StanceFront { get; set; } = 8f;        // the left foot ahead of the hips along the aim (inches)
	public static float StanceBack { get; set; } = 8f;         // the right foot behind them
	public static float StanceFrontSide { get; set; } = -4f;    // each foot across the aim line (+ to the right)
	public static float StanceBackSide { get; set; } = 6f;
	public static float StanceFrontYaw { get; set; } = -5f;    // where each foot points, from the aim (+ to the left)
	public static float StanceBackYaw { get; set; } = -25f;
	public static float StanceHead { get; set; } = 0.75f;       // how much of the lean the neck and head take back out (1 = upright)

	[ConCmd( "swrp_stance_tune", Help = "swrp_stance_tune <drop> <hip tilt> <lean> <front> <back> <front side> <back side> [front yaw] [back yaw] [head 0-1] - dev: the shouldered shooting stance (inches, degrees)" )]
	public static void StanceCommand( float drop, float hipTilt, float lean, float front, float back, float frontSide, float backSide, float frontYaw = -5f, float backYaw = -25f, float head = 0.75f )
	{
		StanceDrop = drop; StanceHipTilt = hipTilt; StanceLean = lean; StanceFront = front; StanceBack = back; StanceFrontSide = frontSide; StanceBackSide = backSide;
		StanceFrontYaw = frontYaw; StanceBackYaw = backYaw; StanceHead = head;
	}

	/// <summary> A gun held stocked in the shoulder up at the eye (off safety): the ones the side-on stance is for. Not hip guns. </summary>
	public static bool ShoulderedAtEye( string modelPath ) => modelPath is not null && Stocks.ContainsKey( modelPath ) && Sights.ContainsKey( modelPath ) && !Hips.ContainsKey( modelPath );

	sealed class StanceNow
	{
		public HashSet<string> Upper;
		public float? FeetYaw;
		public bool Shuffling;
		public float ShuffleTotal;
		public Vector3 Origin;
		public float Drop;
		public Rotation Yaw = Rotation.Identity;
		public Vector3 Hips;
		public Rotation Tilt = Rotation.Identity;
		/// <summary> Everything spine_1 and up turns by: the twist after the lean. </summary>
		public Rotation Lean = Rotation.Identity;
		public Rotation LeanPitch = Rotation.Identity, Twist = Rotation.Identity;
		public float LeanW, LeanDeg, TiltDeg, TwistDeg;
		public Vector3 Pivot;
		/// <summary> All the forward lean the head sits on (hips and chest), for the head to take back out. </summary>
		public Rotation Pitch => LeanPitch * Tilt;
	}

	/// <summary> Moving with the rifle up: how much of the side-on turn the chest keeps on its own while the hips turn back towards
	/// the aim, so the legs walk forward instead of crabwise (swrp_stance_walk; 0 = the whole body stays side-on). </summary>
	[ConVar( "swrp_stance_walk", Help = "Rifle up and moving: share of the side-on turn carried by the chest alone, the hips facing the aim (0-1)", Saved = true )]
	public static float WalkChestShare { get; set; } = 0.7f;

	static readonly Dictionary<SkinnedModelRenderer, StanceNow> _stance = new();
	static readonly Dictionary<Model, HashSet<string>> _stanceRigs = new();
	static readonly string[] StanceBones = { "pelvis", "spine_1", "leg_upper_L", "leg_lower_L", "ankle_L", "leg_upper_R", "leg_lower_R", "ankle_R" };
	static readonly string[] Sides = { "L", "R" };

	/// <summary>
	/// Every machine, every frame, before <see cref="Apply"/>: pose the shooting stance on a body. <paramref name="legs"/> 0..1
	/// is the split stance (standing still), <paramref name="lean"/> 0..1 the chest into the gun, <paramref name="aimYaw"/> the
	/// way the gun points; <paramref name="skeletonYaw"/> turns the whole skeleton under the body (degrees, + left) for bodies
	/// that can't be turned themselves (an NPC's body is its networked root); <paramref name="chestYaw"/> turns the chest and all on
	/// it (degrees, + left) while the hips stay. All zero hands everything back to the animation.
	/// </summary>
	public static void Stance( SkinnedModelRenderer r, float legs, float lean, float aimYaw, float skeletonYaw = 0f, float chestYaw = 0f )
	{
		if ( !r.IsValid() ) return;
		if ( !ClassDefinition.Custom ) legs = lean = 0f;      // stock (swrp_stock_anims): no shooting stance
		if ( _stance.Count > 64 )
			foreach ( var gone in _stance.Keys.Where( k => !k.IsValid() ).ToList() ) _stance.Remove( gone );
		legs = StanceOn ? Math.Clamp( legs, 0f, 1f ) : 0f;
		lean = StanceOn ? Math.Clamp( lean, 0f, 1f ) : 0f;
		var model = r.Model;
		var upper = model is not null && !model.IsError ? StanceRig( model ) : null;
		if ( upper is null || legs <= 0.001f && lean <= 0.001f && MathF.Abs( skeletonYaw ) < 0.01f && MathF.Abs( chestYaw ) < 0.01f )
		{
			EndStance( r );
			return;
		}
		if ( !r.CreateBoneObjects ) r.CreateBoneObjects = true;
		var bones = model.Bones;
		var pelvisBone = bones.GetBone( "pelvis" );
		var pelvisGo = r.GetBoneObject( pelvisBone );
		if ( !pelvisGo.IsValid() || !r.TryGetBoneTransformAnimation( in pelvisBone, out var pelvis ) ) return;
		if ( !_stance.TryGetValue( r, out var st ) ) _stance[r] = st = new StanceNow { Upper = upper };
		var s = r.WorldScale.z;
		var aim = Rotation.FromYaw( aimYaw );
		st.Origin = r.WorldPosition;
		st.Drop = StanceDrop * s * legs;
		st.Yaw = Rotation.FromYaw( skeletonYaw );
		st.Tilt = Rotation.Identity;
		st.Hips = Turn( st, pelvis ).Position;
		// aiming up the back comes upright (the chest a little past it), aiming down the chest leans further over the gun
		var aimUp = AimDir.TryGetValue( r, out var ad ) && ad.LengthSquared > 0.01f ? MathF.Asin( Math.Clamp( ad.Normal.z, -1f, 1f ) ) * 180f / MathF.PI : 0f;
		var pitchLean = aimUp > 0f ? -MathF.Min( aimUp, 60f ) * 0.5f : MathF.Min( -aimUp, 60f ) * 0.3f;
		st.TiltDeg = StanceHipTilt * legs * ( 1f - Math.Clamp( aimUp / 45f, 0f, 1f ) );
		st.LeanDeg = ( StanceLean + pitchLean ) * lean;
		st.Tilt = Rotation.FromAxis( aim.Right, -st.TiltDeg );

		// the hips down, tipped forward (and turned): everything else hangs off the pelvis
		var p = Turn( st, pelvis );
		Take( pelvisGo ).WorldPosition = p.Position;
		pelvisGo.WorldRotation = p.Rotation;

		// the chest leans into the gun about the middle of the back, towards where the gun points
		st.LeanW = lean;
		st.LeanPitch = Rotation.FromAxis( aim.Right, -st.LeanDeg );
		st.TwistDeg = chestYaw;
		st.Twist = Rotation.FromAxis( Vector3.Up, chestYaw );
		st.Lean = st.Twist * st.LeanPitch;
		var spine1 = bones.GetBone( "spine_1" );
		var s1Go = r.GetBoneObject( spine1 );
		if ( spine1.Parent is { } s0 && s1Go.IsValid() && r.TryGetBoneTransformAnimation( in spine1, out var a1 ) && r.TryGetBoneTransformAnimation( in s0, out var a0 ) )
		{
			a1 = Turn( st, a1 );
			a0 = Turn( st, a0 );
			st.Pivot = a1.Position;
			Take( s1Go ).LocalRotation = a0.Rotation.Inverse * ( st.Lean * a1.Rotation );
		}

		// the feet stay planted through small turns of the aim, then step round to catch up (or follow it while settling in): the
		// back foot lifts and swings round, the front one pivots and lifts a little at the end
		st.FeetYaw ??= aimYaw;
		var behind = MathX.DeltaDegrees( st.FeetYaw.Value, aimYaw );
		var liftBack = 0f;
		var liftFront = 0f;
		if ( legs < 0.5f ) { st.FeetYaw = aimYaw; st.Shuffling = false; }
		else
		{
			if ( !st.Shuffling && MathF.Abs( behind ) > 25f ) { st.Shuffling = true; st.ShuffleTotal = MathF.Abs( behind ); }
			if ( st.Shuffling )
			{
				var step = 220f * Time.Delta;
				st.FeetYaw = st.FeetYaw.Value + Math.Clamp( behind, -step, step );
				var left = MathF.Abs( MathX.DeltaDegrees( st.FeetYaw.Value, aimYaw ) );
				st.ShuffleTotal = MathF.Max( st.ShuffleTotal, left );
				var done = Math.Clamp( 1f - left / MathF.Max( st.ShuffleTotal, 1f ), 0f, 1f );
				liftBack = MathF.Sin( done * MathF.PI ) * 2.5f * s;
				liftFront = MathF.Sin( Math.Clamp( done * 2f - 1f, 0f, 1f ) * MathF.PI ) * 1.2f * s;
				if ( left < 2f ) st.Shuffling = false;
			}
		}
		var feet = Rotation.FromYaw( st.FeetYaw.Value );
		StanceLeg( r, st, "L", legs, feet, s, liftFront );
		StanceLeg( r, st, "R", legs, feet, s, liftBack );
	}

	/// <summary> One leg onto its place in the stance: the foot set along the aim (blended from where the animation has it), the
	/// knee bent out over the toes, and the foot flat and turned the way the stance points it. </summary>
	static void StanceLeg( SkinnedModelRenderer r, StanceNow st, string side, float legs, Rotation aim, float s, float lift = 0f )
	{
		var bones = r.Model.Bones;
		var ub = bones.GetBone( $"leg_upper_{side}" );
		var lb = bones.GetBone( $"leg_lower_{side}" );
		var fb = bones.GetBone( $"ankle_{side}" );
		var upperGo = r.GetBoneObject( ub );
		var lowerGo = r.GetBoneObject( lb );
		var footGo = r.GetBoneObject( fb );
		if ( !upperGo.IsValid() || !lowerGo.IsValid() || !footGo.IsValid() ) return;
		if ( legs <= 0.001f ) { Free( upperGo ); Free( lowerGo ); Free( footGo ); return; }      // the animated leg, carried by the pelvis
		if ( !r.TryGetBoneTransformAnimation( in ub, out var au ) || !r.TryGetBoneTransformAnimation( in lb, out var al ) || !r.TryGetBoneTransformAnimation( in fb, out var foot ) ) return;
		au = Turn( st, au );
		al = Turn( st, al );
		var af = Turn( st, foot );
		var a = au.Position.Distance( al.Position );
		var b = al.Position.Distance( af.Position );
		if ( a < 1f || b < 1f ) return;

		var front = side == "L";
		// the animated foot stays on the floor while the hips come down and tip: turned with the body, nothing else
		var floorFoot = st.Origin + st.Yaw * ( foot.Position - st.Origin );
		var place = st.Origin + aim.Forward * ( ( front ? StanceFront : -StanceBack ) * s ) + aim.Right * ( ( front ? StanceFrontSide : StanceBackSide ) * s );
		place.z = floorFoot.z + lift;      // (a foot stepping round as the stance turns)
		var target = Vector3.Lerp( floorFoot, place, legs );
		var footYaw = aim.Yaw() + ( front ? StanceFrontYaw : StanceBackYaw );
		var toes = Rotation.FromYaw( footYaw ).Forward;
		var bodyFwd = ( st.Yaw * r.WorldRotation ).Forward;
		var animKnee = al.Position - ( au.Position + af.Position ) * 0.5f;
		var pole = Vector3.Lerp( animKnee.Length > 0.5f * s ? animKnee.Normal : bodyFwd, toes + Vector3.Up * 0.15f, legs );
		if ( pole.LengthSquared < 0.01f ) pole = bodyFwd;

		var (knee, ankle) = Solve( au.Position, target, pole.Normal, a, b );
		Take( upperGo ).WorldRotation = Rotation.FromToRotation( al.Position - au.Position, knee - au.Position ) * au.Rotation;
		Take( lowerGo ).WorldRotation = Rotation.FromToRotation( af.Position - al.Position, ankle - knee ) * al.Rotation;
		var turn = Rotation.FromAxis( Vector3.Up, MathX.DeltaDegrees( ( st.Yaw * r.WorldRotation ).Yaw(), footYaw ) * legs );
		Take( footGo ).WorldRotation = turn * st.Yaw * foot.Rotation;      // flat as animated, not tipped with the hips
	}

	/// <summary> An animated transform turned, brought down and tipped forward with the stance's pelvis. </summary>
	static Transform Turn( StanceNow st, Transform tx )
	{
		var at = st.Origin + st.Yaw * ( tx.Position - st.Origin ) - Vector3.Up * st.Drop;
		return new( st.Hips + st.Tilt * ( at - st.Hips ), st.Tilt * st.Yaw * tx.Rotation, tx.Scale );
	}

	/// <summary> Give the hips, chest and legs back to the animation. </summary>
	public static void EndStance( SkinnedModelRenderer r )
	{
		if ( !_stance.Remove( r ) || !r.IsValid() || !r.CreateBoneObjects ) return;
		foreach ( var name in StanceBones ) Free( r.GetBoneObject( name ) );
	}

	/// <summary>
	/// An animated bone's world transform with the shooting stance on it: everything turned and brought down with the hips, the
	/// chest and all on it leaned. For code that reads the animation and poses on top of it (the arm solve, the flinch).
	/// </summary>
	public static Transform StanceAdjust( SkinnedModelRenderer r, string bone, Transform tx )
	{
		if ( !_stance.TryGetValue( r, out var st ) ) return tx;
		tx = Turn( st, tx );
		if ( !st.Upper.Contains( bone ) ) return tx;
		return new Transform( st.Pivot + st.Lean * ( tx.Position - st.Pivot ), st.Lean * tx.Rotation, tx.Scale );
	}

	/// <summary>
	/// The animgraph's aim_body for a body turned side-on: straight on along the body (pitched with the aim), so the graph doesn't
	/// twist the chest back square to the target and undo the blade; <paramref name="blade"/> 0 gives the plain look direction.
	/// </summary>
	public static Vector3 BladedBodyLook( Vector3 look, Rotation body, float blade )
	{
		var along = body.Forward.WithZ( 0 ).Normal * look.WithZ( 0 ).Length + Vector3.Up * look.z;
		return Vector3.Lerp( look, along, Math.Clamp( blade, 0f, 1f ) ).Normal;
	}

	/// <summary> How the stance turns a body's skeleton under it (identity unless it's an NPC turned side-on). </summary>
	public static Rotation StanceYawOf( SkinnedModelRenderer r ) => _stance.TryGetValue( r, out var st ) ? st.Yaw : Rotation.Identity;

	[ConCmd( "swrp_hand_debug", Help = "dev: the test stand's hands on its rifle, in the gun's frame (x along the barrel, y to its left, z up): bone axes, fingers, thumb" )]
	public static void HandDebugCommand()
	{
		var r = SwrpAnimMannequin.TestStand( Game.ActiveScene )?.GetComponent<SkinnedModelRenderer>();
		if ( !r.IsValid() || !r.CreateBoneObjects || r.GetAttachment( "hold_R" ) is not { } hold ) { Log.Info( "[SWRP] no posed stand" ); return; }
		var gun = hold.Rotation;
		string V( Vector3 w ) { var v = gun.Inverse * w; return $"({v.x:0.00} {v.y:0.00} {v.z:0.00})"; }
		Vector3 At( string n ) => r.GetBoneObject( n ) is { } g && g.IsValid() ? g.WorldPosition : Vector3.Zero;
		foreach ( var side in Sides )
		{
			var hand = r.GetBoneObject( $"hand_{side}" );
			if ( !hand.IsValid() ) continue;
			var q = hand.WorldRotation;
			var s = r.WorldScale.z;
			var wrist = gun.Inverse * ( hand.WorldPosition - hold.Position ) / s;
			var stand = SwrpAnimMannequin.TestStand( Game.ActiveScene );
			var aimFrame = Rotation.FromYaw( stand?.AimYaw ?? 0f );
			var rel = aimFrame.Inverse * ( hand.WorldPosition - r.WorldPosition );
			Log.Info( $"[SWRP] hand_{side}: at (fwd right up from the feet) {rel.x:0.0} {-rel.y:0.0} {rel.z:0.0}; wrist {wrist.x:0.0} {wrist.y:0.0} {wrist.z:0.0}; X {V( q.Forward )} Y {V( q.Left )} Z {V( q.Up )}; to middle knuckle {V( ( At( $"finger_middle_0_{side}" ) - hand.WorldPosition ).Normal )} to middle tip {V( ( At( $"finger_middle_2_{side}" ) - hand.WorldPosition ).Normal )} to thumb tip {V( ( At( $"finger_thumb_2_{side}" ) - hand.WorldPosition ).Normal )}" );
		}
		if ( HeldModel.TryGetValue( r, out var path ) && path is not null && SupportGrips.TryGetValue( path, out var g ) )
			Log.Info( $"[SWRP] support grip for {path}: wrist {g.Wrist} fingers {g.Fingers} palm {g.Palm}" );
	}

	[ConCmd( "swrp_stance_debug", Help = "dev: print the test stand's (or your) stance: elbow angles, how side-on the shoulders are, hips, feet, lean and turn" )]
	public static void StanceDebugCommand()
	{
		var r = SwrpAnimMannequin.TestStand( Game.ActiveScene )?.GetComponent<SkinnedModelRenderer>() ?? SwrpPlayer.Local?.Body;
		if ( !r.IsValid() || !r.CreateBoneObjects ) { Log.Info( "[SWRP] no posed body" ); return; }
		Vector3 At( string n ) => r.GetBoneObject( n ) is { } g && g.IsValid() ? g.WorldPosition : Vector3.Zero;
		float Elbow( string side )
		{
			var u = At( $"arm_upper_{side}" ); var e = At( $"arm_lower_{side}" ); var w = At( $"hand_{side}" );
			return AngleOf( u - e, w - e );
		}
		static float AngleOf( Vector3 a, Vector3 b ) => MathF.Acos( Math.Clamp( Vector3.Dot( a.Normal, b.Normal ), -1f, 1f ) ) * 180f / MathF.PI;
		var aim = AimDir.TryGetValue( r, out var ad ) ? ad.WithZ( 0 ).Normal : r.WorldRotation.Forward;
		var shoulders =( At( "arm_upper_L" ) - At( "arm_upper_R" ) ).WithZ( 0 );
		var side = AngleOf( shoulders, aim );      // 90 = square to the target, 0 = fully side-on
		var hips = ( At( "leg_upper_L" ) - At( "leg_upper_R" ) ).WithZ( 0 );
		var s = r.WorldScale.z;
		var feet = ( At( "ankle_L" ) - At( "ankle_R" ) ) / s;
		var pelvisUp = ( At( "pelvis" ).z - r.WorldPosition.z ) / s;
		var headUp = ( At( "head" ).z - r.WorldPosition.z ) / s;
		var lElbowDrop = ( At( "hand_L" ).z - At( "arm_lower_L" ).z ) / s;
		var rElbowDrop = ( At( "hand_R" ).z - At( "arm_lower_R" ).z ) / s;
		var leanNow = _stance.TryGetValue( r, out var st ) ? $"hips tipped {st.TiltDeg:0}°, chest leaned {st.LeanDeg:0}°, chest turned {st.TwistDeg:0}°, hips down {st.Drop / s:0.0} in" : "no stance";
		Log.Info( $"[SWRP] stance: elbows R {Elbow( "R" ):0}° L {Elbow( "L" ):0}° (hand above elbow R {rElbowDrop:0.0} L {lElbowDrop:0.0} in); shoulders {side:0}° to the aim, hips {AngleOf( hips, aim ):0}°; feet apart {feet.WithZ( 0 ).Length:0.0} in; pelvis {pelvisUp:0.0} in, head {headUp:0.0} in up; {leanNow}" );
	}

	/// <summary> spine_1 and every bone on it, for a skeleton with the Citizen spine and legs (null otherwise). </summary>
	static HashSet<string> StanceRig( Model model )
	{
		if ( _stanceRigs.TryGetValue( model, out var set ) ) return set;
		var all = model.Bones.AllBones.ToList();
		if ( StanceBones.Any( n => !all.Any( b => b.Name == n ) ) ) return _stanceRigs[model] = null;
		set = new HashSet<string>();
		foreach ( var b in all )
			for ( var p = b; p is not null; p = p.Parent )
				if ( p.Name == "spine_1" ) { set.Add( b.Name ); break; }
		return _stanceRigs[model] = set;
	}

	// ----- the hold coming alive -----

	/// <summary> When each body last fired (SwrpBlaster / SwrpNpc call <see cref="Kick"/>), for the recoil in the hold. </summary>
	static readonly Dictionary<SkinnedModelRenderer, RealTimeSince> _kicks = new();

	[ConVar( "swrp_hold_life", Help = "Rifle hold movement: breathing sway, walking bob and recoil kick (1 = on)", Saved = true )]
	public static bool HoldLifeOn { get; set; } = true;

	/// <summary> Every machine: this body just fired (the shot's broadcast). </summary>
	public static void Kick( SkinnedModelRenderer r )
	{
		if ( !r.IsValid() ) return;
		if ( _kicks.Count > 128 )
			foreach ( var gone in _kicks.Keys.Where( k => !k.IsValid() ).ToList() ) _kicks.Remove( gone );
		_kicks[r] = 0;
	}

	/// <summary>
	/// Small movements on top of the posed rifle so it never sits dead still: a slow breathing sway at rest, a bob and a little
	/// yaw swing in time with the steps while moving, and a sharp kick back into the shoulder and up on each shot that settles
	/// in about a tenth of a second (Angelo, 2026-10-12: "animate the weapon holding better").
	/// </summary>
	static (Vector3 Offset, Rotation Turn) HoldLife( SkinnedModelRenderer r, float s, Rotation gunRot )
	{
		if ( !HoldLifeOn ) return (Vector3.Zero, Rotation.Identity);
		var phase = ( r.GameObject.Id.GetHashCode() & 1023 ) / 163f;      // each trooper breathes on their own beat
		var t = Time.Now + phase;
		var speed = r.GetFloat( "move_groundspeed" );
		var moving = Math.Clamp( speed / 180f, 0f, 1.3f );
		var still = 1f - Math.Min( 1f, moving );

		// breathing: about 16 breaths a minute, a fraction of a degree and a fraction of a unit
		var breath = MathF.Sin( t * 1.7f );
		var pitch = breath * 0.5f * still;
		var yaw = MathF.Sin( t * 0.83f ) * 0.4f * still;
		var up = breath * 0.18f * still;

		// walking: two bobs and one side swing per stride, scaled by speed
		var step = t * ( 4.2f + 2.2f * moving );
		up += MathF.Abs( MathF.Sin( step ) ) * 0.55f * moving - 0.3f * moving;
		yaw += MathF.Sin( step * 0.5f ) * 1.6f * moving;
		pitch += MathF.Sin( step ) * 0.8f * moving;

		// recoil: back along the gun and the muzzle up, fast in and settling out
		var back = 0f;
		if ( _kicks.TryGetValue( r, out var since ) && since < 0.25f )
		{
			var k = since < 0.03f ? since / 0.03f : MathF.Exp( -( since - 0.03f ) / 0.06f );
			back = 1.4f * k;
			pitch -= 4.5f * k;
			yaw += 0.8f * k * MathF.Sin( phase * 7f );
		}

		var offset = Vector3.Up * ( up * s ) - gunRot.Forward * ( back * s );
		var turn = Rotation.FromAxis( gunRot.Right, -pitch ) * Rotation.FromAxis( Vector3.Up, yaw );
		return (offset, turn);
	}

	// ----- the sprint carry -----

	// where the low carry holds the rifle (tunable live with swrp_carry)
	public static float CarryRight { get; set; } = 6.5f;
	public static float CarryForward { get; set; } = 10f;
	public static float CarryUp { get; set; } = 11f;
	public static float CarryAim { get; set; } = 0.5f;
	public static float CarryDown { get; set; } = 0.16f;

	/// <summary> Bodies in the relaxed carry (weapon relaxed): the carry pose with the muzzle down (set by whoever drives the body). </summary>
	public static readonly HashSet<SkinnedModelRenderer> Relaxed = new();
	public static float RelaxRight { get; set; } = 3f;
	public static float RelaxForward { get; set; } = 10f;
	public static float RelaxUp { get; set; } = 11f;
	public static float RelaxAim { get; set; } = 0.45f;
	public static float RelaxAcross { get; set; } = 0.65f;
	public static float RelaxDown { get; set; } = 0.6f;

	[ConCmd( "swrp_relax", Help = "swrp_relax <right> <forward> <up> <aim forward> <across> <down> - dev: tune the relaxed muzzle-down carry" )]
	public static void RelaxCommand( float right, float forward, float up, float aim, float across, float down )
	{
		RelaxRight = right; RelaxForward = forward; RelaxUp = up; RelaxAim = aim; RelaxAcross = across; RelaxDown = down;
	}

	[ConCmd( "swrp_carry", Help = "swrp_carry <right> <forward> <up> <aim forward> <aim down> - dev: tune the low carry / low ready rifle hold" )]
	public static void CarryCommand( float right, float forward, float up, float aim, float down )
	{
		CarryRight = right; CarryForward = forward; CarryUp = up; CarryAim = aim; CarryDown = down;
	}

	/// <summary> Per model: the hold_R frame (where the gun sits) relative to the right hand, and the left hand on the rifle
	/// relative to the right, read off the animated two-handed pose while it isn't being overridden. </summary>
	static readonly Dictionary<Model, (Transform Hold, Transform LeftOnRifle)> _gripsV3 = new();     // renamed so a hotload drops grips learned from the old broken pose

	static readonly (Transform Hold, Transform LeftOnRifle) FallbackGrip = ( new Transform( new Vector3( 0.43f, 0.01f, -2.34f ), Rotation.From( 42.74f, -21.27f, -93.66f ) ), new Transform( new Vector3( 6.75f, 1.34f, -1.55f ) ) );

	/// <summary> A body on a Garry's Mod animation set (a "_gmod_&lt;hold&gt;" model). </summary>
	public static bool IsGmodSet( SkinnedModelRenderer r ) => r?.Model?.ResourcePath?.Contains( "_gmod_" ) == true;

	/// <summary>
	/// The grip for the holds: the learned one, or on a GMod set (holdtype 0, nothing to learn from) this frame's hand -> hold_R
	/// off the skeleton (hold_R is the hand's child) with the measured left hand.
	/// </summary>
	static bool GripOf( SkinnedModelRenderer r, out (Transform Hold, Transform LeftOnRifle) grip )
	{
		grip = default;
		if ( r.Model is not { } model ) return false;
		if ( !IsGmodSet( r ) ) return _gripsV3.TryGetValue( model, out grip );
		grip = RawBone( r, "hand_R", out var hb ) && RawBone( r, "hold_R", out var hh ) ? ( hb.ToLocal( hh ), FallbackGrip.LeftOnRifle ) : FallbackGrip;
		return true;
	}

	static void LearnRifleGrip( SkinnedModelRenderer r, bool carrying )
	{
		if ( r.Model is not { } model || _gripsV3.ContainsKey( model ) ) return;
		if ( r.Parameters.GetInt( "holdtype" ) is not ( 1 or 2 ) ) return;
		if ( !AnimBone( r, "hand_R", out var hr ) || !AnimBone( r, "hand_L", out var hl ) || r.GetAttachment( "hold_R" ) is not { } hold ) return;
		// only from the animation itself: not once code has taken the hands over
		var bone = r.CreateBoneObjects ? r.GetBoneObject( "hand_R" ) : null;
		if ( bone.IsValid() && bone.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) return;
		var hand = bone.IsValid() ? bone.WorldTransform : hr;
		var learned = ( hand.ToLocal( hold ), hr.ToLocal( hl ) );
		// the animation's own grip, measured before the 2026-10-13 graph update; since the new human graph's rifle hold the
		// learned grip came out ~12 units off the hand and the gun floated away from it (Angelo, 2026-10-18), so a reading
		// that far from the hand falls back to this one
		if ( learned.Item1.Position.Length > 4f ) learned = FallbackGrip;
		_gripsV3[model] = learned;
	}

	/// <summary>
	/// Sprinting with a rifle: carried low across the body like a trooper on the move, the grip by the right hip, the barrel
	/// angled down and across to the left, the left hand still on the foregrip, bouncing a little with each stride
	/// (<paramref name="stride"/>, 0..1 round a step cycle).
	/// </summary>
	static bool CarryPose( SkinnedModelRenderer r, float s, Vector3 fwd, Vector3 right, Vector3 up, float stride, out ArmPose rightArm, out ArmPose leftArm )
	{
		rightArm = leftArm = default;
		// a model only ever carried relaxed (the "_patrol" one: holdtype 0, nothing to learn from) takes the measured grip
		if ( r.Model is not { } model || !AnimBone( r, "pelvis", out var pelvis ) ) return false;
		var grip = GripOf( r, out var g ) ? g : FallbackGrip;

		var bob = MathF.Sin( stride * MathF.PI * 4f );        // two bounces per step cycle (each footfall)
		var sway = MathF.Sin( stride * MathF.PI * 2f );
		// relaxed (walking the ship): the grip by the right hip, the muzzle pointed down at the deck ahead and a little left,
		// only a light sway with the steps (Angelo, 2026-10-19: "barrel pointing downwards, natural")
		var relaxed = Relaxed.Contains( r );
		var gripPos = relaxed
			? pelvis.Position + right * ( RelaxRight * s ) + fwd * ( RelaxForward * s ) + up * ( ( RelaxUp + bob * 0.3f ) * s )
			: pelvis.Position + right * ( CarryRight * s ) + fwd * ( CarryForward * s ) + up * ( ( CarryUp + bob * 0.9f ) * s );
		var barrel = relaxed
			? ( fwd * RelaxAim - right * ( RelaxAcross + sway * 0.02f ) - up * RelaxDown ).Normal
			: ( fwd * CarryAim - right * ( 0.78f + sway * 0.04f ) - up * CarryDown ).Normal;
		var holdRot = Rotation.LookAt( barrel, up * 0.9f + right * 0.25f );

		// the right hand that puts the gun there, and the left hand where it sits on the rifle
		// this frame's hand -> hold_R: it differs per hold type, and relaxed walks on holdtype 0 (as PistolPose)
		var handToHold = relaxed && RawBone( r, "hand_R", out var hb ) && RawBone( r, "hold_R", out var hh ) ? hb.ToLocal( hh ) : grip.Hold;
		var handRot = holdRot * handToHold.Rotation.Inverse;
		var handPos = gripPos - handRot * handToHold.Position;
		var hand = new Transform( handPos, handRot );
		var left = hand.ToWorld( grip.LeftOnRifle );

		rightArm = new ArmPose( handPos, right * 0.7f - fwd * 0.45f - up * 0.55f, handRot );
		leftArm = new ArmPose( left.Position, -right * 0.45f - up * 0.85f + fwd * 0.2f, left.Rotation );
		if ( HeldModel.TryGetValue( r, out var path ) && path is not null && SupportGrips.TryGetValue( path, out var fg ) )
		{
			var support = SupportArm( new Transform( gripPos + holdRot * ( WeaponStats.GripShift * s ), holdRot ), fg, s, right, up );
			leftArm = support with { Pole = -right * 0.45f - up * 0.85f + fwd * 0.2f };
		}
		return true;
	}

	static readonly string[] ArmBones = { "arm_upper_", "arm_lower_", "hand_" };

	public static float ThumbIn { get; set; } = 1f;
	public static Rotation ThumbTwist { get; set; } = Rotation.From( 0f, -25f, 0f );

	[ConCmd( "swrp_thumb", Help = "swrp_thumb <in 0-1> [pitch yaw roll] - dev: how far the salute's thumb turns in from its rest spread, and a twist on its root" )]
	public static void ThumbCommand( float amount, float pitch = 0, float yaw = 0, float roll = 0 )
	{
		ThumbIn = amount;
		ThumbTwist = Rotation.From( pitch, yaw, roll );
	}

	// the salute's lines (tunable live with swrp_salute)
	public static float AtEaseUp { get; set; } = 8.5f;
	public static float AtEaseBack { get; set; } = 7.5f;
	public static float SaluteSide { get; set; } = 0.92f;
	public static float SaluteForward { get; set; } = 0.4f;
	public static float SaluteForeForward { get; set; } = 0.33f;

	[ConCmd( "swrp_atease", Help = "swrp_atease <up> <back> - dev: tune where the hands clasp behind the back (from the pelvis)" )]
	public static void AtEaseCommand( float up, float back )
	{
		AtEaseUp = up;
		AtEaseBack = back;
	}

	[ConCmd( "swrp_salute", Help = "swrp_salute <side> <forward> <forearm forward> - dev: tune the salute's upper arm and forearm lines" )]
	public static void SaluteCommand( float side, float forward, float foreForward )
	{
		SaluteSide = side;
		SaluteForward = forward;
		SaluteForeForward = foreForward;
	}

	/// <summary>
	/// Two-bone solve for one arm onto <paramref name="pose"/>: the elbow placed by the pole, each bone aimed from its
	/// animated rotation (keeping the animation's twist), the hand turned to the pose, all blended by <paramref name="w"/>.
	/// No pose (or weight 0) gives the arm back to the animation.
	/// </summary>
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
			FreeFingers( r, side );
			return;
		}
		if ( !AnimBone( r, $"arm_upper_{side}", out var au ) || !AnimBone( r, $"arm_lower_{side}", out var al ) || !AnimBone( r, $"hand_{side}", out var ah ) ) return;

		// the shoulder stays where the (animated) clavicle puts it
		var shoulder = upper.WorldPosition;
		var a = au.Position.Distance( al.Position );
		var b = al.Position.Distance( ah.Position );
		// a collapsed arm (bones not posed yet, e.g. the frame you sit down in a craft) has nothing to solve
		if ( a < 1f || b < 1f ) return;
		var target = p.Wrist;
		var handRot = p.Hand * HandFix( side == "R" );
		var (elbow, wrist) = Solve( shoulder, target, p.Pole, a, b );
		if ( GripDebug && _sinceGripDebug < 0.02f ) Log.Info( $"[SWRP] arm {side}: reach {shoulder.Distance( target ) / r.WorldScale.z:0.0} of {( a + b ) / r.WorldScale.z:0.0}, wrist off target {wrist.Distance( target ) / r.WorldScale.z:0.0}" );
		if ( p.Tip is { } tip )
		{
			// flat hand straight on from the forearm: walk the wrist back from the fingertips along the forearm (twice, since
			// the forearm's line depends on where the wrist ends up)
			var reach = HandLength( r, side, ah );
			for ( var i = 0; i < 2; i++ )
			{
				var line = ( wrist - elbow ).Normal;
				( elbow, wrist ) = Solve( shoulder, tip - line * reach, p.Pole, a, b );
			}
			var along = ( wrist - elbow ).Normal;
			var back = ( p.Back - along * Vector3.Dot( p.Back, along ) ).Normal;
			handRot = Rotation.LookAt( along, back ) * HandFix( side == "R" );
		}

		var upperRot = Rotation.FromToRotation( al.Position - au.Position, elbow - shoulder ) * au.Rotation;
		var lowerRot = Rotation.FromToRotation( ah.Position - al.Position, wrist - elbow ) * al.Rotation;

		Take( upper ).WorldRotation = Rotation.Slerp( au.Rotation, upperRot, w );
		Take( lower ).WorldRotation = Rotation.Slerp( al.Rotation, lowerRot, w );
		Take( hand ).WorldRotation = Rotation.Slerp( ah.Rotation, handRot, w );
		if ( p.FlatFingers ) Fingers( r, side, w );
		else if ( p.Grip && GripFingersOn ) GripFingers( r, side, w );
		else FreeFingers( r, side );
	}

	/// <summary> Shoulder to elbow and wrist for a target, the elbow bent towards the pole. </summary>
	static (Vector3 Elbow, Vector3 Wrist) Solve( Vector3 shoulder, Vector3 target, Vector3 poleDir, float a, float b )
	{
		var toWrist = target - shoulder;
		var lo = MathF.Abs( a - b ) + 0.01f;
		var d = Math.Clamp( toWrist.Length, lo, MathF.Max( lo, a + b - 0.01f ) );
		var dir = toWrist.Normal;
		var pole = ( poleDir - dir * Vector3.Dot( poleDir, dir ) ).Normal;
		var cosA = Math.Clamp( ( a * a + d * d - b * b ) / ( 2f * a * d ), -1f, 1f );
		return ( shoulder + dir * ( a * cosA ) + pole * ( a * MathF.Sqrt( 1f - cosA * cosA ) ), shoulder + dir * d );
	}

	/// <summary> Wrist to the tip of the middle finger, from the skeleton (a little past the last knuckle). </summary>
	static float HandLength( SkinnedModelRenderer r, string side, Transform hand )
	{
		if ( AnimBone( r, $"finger_middle_2_{side}", out var tip ) ) return hand.Position.Distance( tip.Position ) + 2f * r.WorldScale.z;
		return 8f * r.WorldScale.z;
	}

	/// <summary> Straighten every finger of a hand to the skeleton's rest pose (flat and together), blended by w. </summary>
	static void Fingers( SkinnedModelRenderer r, string side, float w )
	{
		foreach ( var bone in r.Model.Bones.AllBones )
		{
			if ( !bone.Name.StartsWith( "finger_" ) || !bone.Name.EndsWith( "_" + side ) ) continue;
			var go = r.GetBoneObject( bone );
			if ( !go.IsValid() || bone.Parent is null ) continue;
			if ( !r.TryGetBoneTransformAnimation( in bone, out var now ) ) continue;
			var parent = bone.Parent;
			if ( !r.TryGetBoneTransformAnimation( in parent, out var pt ) ) continue;
			var animLocal = pt.Rotation.Inverse * now.Rotation;
			// flat: each joint straight on from the one before; the thumb's root turned most of the way in from its rest
			// spread so it lies straight along the side of the hand (ThumbIn, tunable with swrp_thumb)
			var flat = !bone.Name.Contains( "thumb" ) ? Rotation.Identity
				: bone.Name.StartsWith( "finger_thumb_0" ) ? Rotation.Slerp( bone.LocalTransform.Rotation, Rotation.Identity, ThumbIn ) * ThumbTwist
				: Rotation.Identity;
			Take( go ).LocalRotation = Rotation.Slerp( animLocal, flat, w );
		}
	}

	[ConVar( "swrp_grip_fingers", Help = "Dev: close the fingers round held guns (1) or leave them to the animation (0)" )]
	public static bool GripFingersOn { get; set; } = true;

	// fingers closed round a grip or handguard (tunable live with swrp_curl): the local axis each joint bends about, and how far
	public static Vector3 CurlAxis { get; set; } = new( 0f, 0f, -1f );      // the same bend axis on both hands (checked on the pose stage)
	public static float CurlRoot { get; set; } = 50f;
	public static float CurlTip { get; set; } = 65f;
	public static float CurlThumb { get; set; } = 30f;
	public static float CurlSign { get; set; } = -1f;      // (checked with swrp_gun_profile: -1 wraps the grip)
	public static float SupportCurl { get; set; } = 1.2f;   // the support hand closes round the gun (0.45 left it open, Angelo 2026-10-13; 1.2 a full fist with the vertical-grip hold, 2026-10-16)

	[ConCmd( "swrp_support_curl", Help = "swrp_support_curl <multiplier> - dev: how far the left hand's fingers close round the handguard (1 = as the right hand)" )]
	public static void SupportCurlCommand( float mult ) => SupportCurl = mult;

	[ConVar( "swrp_trigger_curl", Help = "How far the right index finger bends onto the trigger (share of a full grip curl)", Saved = true )]
	public static float TriggerCurl { get; set; } = 0.35f;

	[ConCmd( "swrp_curl_sign", Help = "swrp_curl_sign <1|-1> - dev: flip which way the fingers close" )]
	public static void CurlSignCommand( float s ) => CurlSign = s < 0 ? -1f : 1f;

	[ConCmd( "swrp_curl", Help = "swrp_curl <axis x y z> <root deg> <joint deg> [thumb deg] - dev: how the fingers close round a gun" )]
	public static void CurlCommand( float x, float y, float z, float root, float joint, float thumb = 30f )
	{
		CurlAxis = new Vector3( x, y, z ); CurlRoot = root; CurlTip = joint; CurlThumb = thumb;
	}

	/// <summary> Wrap the fingers round what the hand holds: each joint bent from its rest by the curl. </summary>
	/// <summary>
	/// Wrap the fingers round what the hand holds. Each joint bends about the knuckle line (index to ring), measured on the
	/// animated hand every frame, so it
	/// doesn't depend on how the bones' local axes happen to lie; the knuckles bend less than the middle joints, the thumb
	/// closes in less again. Bent on from the animation's own pose (bending from the bind pose twisted the gloves).
	/// </summary>
	static void GripFingers( SkinnedModelRenderer r, string side, float w )
	{
		// (raw, like the joints below: the knuckle line has to be in the same frame as the animated finger it bends)
		if ( !RawBone( r, $"finger_index_0_{side}", out var index0 ) || !RawBone( r, $"finger_ring_0_{side}", out var ring0 ) ) { if ( GripDebug && _sinceGripDebug < 0.02f ) Log.Info( $"[SWRP] fingers {side}: no knuckle bones" ); return; }
		var curledCount = 0;
		// across the knuckles, mirrored on the left hand so both bend the same way (CurlSign flips both, for checking)
		var across = ( ring0.Position - index0.Position ).Normal * ( side == "L" ? -1f : 1f ) * CurlSign;
		var bones = r.Model.Bones.AllBones.ToList();
		foreach ( var bone in bones )
		{
			if ( !bone.Name.StartsWith( "finger_" ) || !bone.Name.EndsWith( "_" + side ) || bone.Name.Contains( "_meta_" ) ) continue;
			var go = r.GetBoneObject( bone );
			if ( !go.IsValid() || bone.Parent is null ) continue;
			if ( !r.TryGetBoneTransformAnimation( in bone, out var now ) ) continue;
			var parent = bone.Parent;
			if ( !r.TryGetBoneTransformAnimation( in parent, out var pt ) ) continue;
			var child = bones.FirstOrDefault( b => b.Parent == bone );
			if ( child is null || !r.TryGetBoneTransformAnimation( in child, out var ct ) ) continue;
			var along = ( ct.Position - now.Position ).Normal;
			var thumb = bone.Name.Contains( "thumb" );
			// a finger bends about the knuckle line (measured on the stand: about any other axis it swings up or down instead of
			// closing round the grip); taken square to the finger so it never twists it
			var axisWorld = ( across - along * Vector3.Dot( across, along ) ).Normal;
			if ( axisWorld.LengthSquared < 0.5f ) continue;
			var deg = ( thumb ? CurlThumb : bone.Name.Contains( "_0_" ) ? CurlRoot : CurlTip ) * ( side == "L" ? SupportCurl : 1f );
			// the trigger finger: the right index lies along the trigger guard, crooked onto the trigger, not wrapped round the grip
			if ( side == "R" && bone.Name.Contains( "index" ) ) deg *= TriggerCurl;
			var animLocal = pt.Rotation.Inverse * now.Rotation;
			var localAxis = now.Rotation.Inverse * axisWorld;
			var curled = animLocal * Rotation.FromAxis( localAxis, deg );
			Take( go ).LocalRotation = Rotation.Slerp( animLocal, curled, w );
			curledCount++;
		}
		if ( GripDebug && _sinceGripDebug < 0.02f ) Log.Info( $"[SWRP] fingers {side}: curled {curledCount} joints, weight {w:0.00}" );
	}

	static void FreeFingers( SkinnedModelRenderer r, string side )
	{
		if ( !r.CreateBoneObjects ) return;
		foreach ( var bone in r.Model.Bones.AllBones )
			if ( bone.Name.StartsWith( "finger_" ) && bone.Name.EndsWith( "_" + side ) ) Free( r.GetBoneObject( bone ) );
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

	/// <summary> Give both arms back to the animation. </summary>
	static void Release( SkinnedModelRenderer r )
	{
		if ( !r.CreateBoneObjects ) return;
		foreach ( var side in new[] { "R", "L" } )
			foreach ( var name in ArmBones )
				Free( r.GetBoneObject( name + side ) );
	}

	/// <summary> Turns the 'fingers along X, palm along Z' frame above into the hand bone's own frame. </summary>
	public static Rotation HandFix( bool rightHand ) => rightHand ? FixRight : FixLeft;
	public static Rotation FixRight { get; set; } = Rotation.Identity;
	public static Rotation FixLeft { get; set; } = Rotation.Identity;

	/// <summary> A bone's world transform from the animation (before any IK; plus the shooting stance and a hit's flinch). </summary>
	static bool AnimBone( SkinnedModelRenderer r, string name, out Transform tx )
	{
		tx = default;
		var bone = r.Model?.Bones.GetBone( name );
		if ( bone is null || !r.TryGetBoneTransformAnimation( in bone, out tx ) ) return false;
		// with the stance's hips and lean, and a hit's flinch on the upper body, so the hands stay on the gun
		tx = HitReact.Adjust( r, name, StanceAdjust( r, name, tx ) );
		return true;
	}

	/// <summary> A bone's world transform straight from the animation, nothing on top (for local frames, like the knuckle line). </summary>
	static bool RawBone( SkinnedModelRenderer r, string name, out Transform tx )
	{
		tx = default;
		var bone = r.Model?.Bones.GetBone( name );
		return bone is not null && r.TryGetBoneTransformAnimation( in bone, out tx );
	}

	[ConCmd( "swrp_neck_debug", Help = "dev: the pose stage's neck and head bone lengths, animated against the model's rest pose" )]
	public static void NeckDebugCommand()
	{
		var r = Game.ActiveScene?.GetAllComponents<SkinnedModelRenderer>().FirstOrDefault( x => x.GameObject.Name == "Pose Stage" );
		if ( !r.IsValid() ) { Log.Info( "[SWRP] no pose stage" ); return; }
		foreach ( var (a, b) in new[] { ("spine_2", "neck_0"), ("neck_0", "head"), ("spine_2", "clavicle_R"), ("pelvis", "spine_0") } )
		{
			var ba = r.Model.Bones.GetBone( a ); var bb = r.Model.Bones.GetBone( b );
			if ( ba is null || bb is null ) { Log.Info( $"[SWRP] no bone {a}/{b}" ); continue; }
			r.TryGetBoneTransformAnimation( in ba, out var ta ); r.TryGetBoneTransformAnimation( in bb, out var tb );
			var rest = ba.LocalTransform.Position.Distance( bb.LocalTransform.Position );
			var restModel = r.Model.GetBoneTransform( bb.Index ).Position.Distance( r.Model.GetBoneTransform( ba.Index ).Position );
			Log.Info( $"[SWRP] {a}->{b}: animated {ta.Position.Distance( tb.Position ) / r.WorldScale.z:0.00} rest {restModel:0.00}" );
		}
		// every bone whose length to its parent differs from the rest pose (animation translating it)
		foreach ( var bone in r.Model.Bones.AllBones )
		{
			if ( bone.Parent is not { } par || !r.TryGetBoneTransformAnimation( in bone, out var tbone ) || !r.TryGetBoneTransformAnimation( in par, out var tpar ) ) continue;
			var len = tbone.Position.Distance( tpar.Position ) / r.WorldScale.z;
			var restLen = r.Model.GetBoneTransform( bone.Index ).Position.Distance( r.Model.GetBoneTransform( par.Index ).Position );
			if ( MathF.Abs( len - restLen ) > 0.3f ) Log.Info( $"[SWRP] stretched {par.Name}->{bone.Name}: {len:0.00} vs rest {restLen:0.00}" );
		}
	}

	[ConCmd( "swrp_bone_axes", Help = "dev: print how the hand bones are oriented on your body (which local axis runs along the fingers)" )]
	public static void BoneAxesCommand()
	{
		var r = SwrpPlayer.Local?.Body ?? Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>().FirstOrDefault()?.GetComponent<SkinnedModelRenderer>();
		if ( !r.IsValid() ) { Log.Info( "[SWRP] no body" ); return; }
		var names = r.Model.Bones.AllBones.Select( b => b.Name ).ToList();
		Log.Info( $"[SWRP] bones: {string.Join( " ", names.Where( n => n.Contains( "hand" ) || n.Contains( "finger_middle" ) || n.Contains( "finger_thumb_0" ) || n.Contains( "arm_lower" ) || n == "head" || n == "pelvis" ) )}" );
		var body = r.WorldRotation;
		foreach ( var side in new[] { "R", "L" } )
		{
			if ( !AnimBone( r, $"hand_{side}", out var hand ) ) continue;
			string Local( Vector3 world ) { var v = hand.Rotation.Inverse * world; return $"({v.x:0.00} {v.y:0.00} {v.z:0.00})"; }
			string Body( Vector3 world ) { var v = body.Inverse * world; return $"({v.x:0.00} {v.y:0.00} {v.z:0.00})"; }
			var finger = names.FirstOrDefault( n => n.StartsWith( "finger_middle_0" ) && n.EndsWith( side ) );
			var thumb = names.FirstOrDefault( n => n.StartsWith( "finger_thumb_0" ) && n.EndsWith( side ) );
			var lower = $"arm_lower_{side}";
			var line = $"[SWRP] hand_{side}: x→body {Body( hand.Rotation.Forward )} y→body {Body( hand.Rotation.Left )} z→body {Body( hand.Rotation.Up )}";
			if ( finger is not null && AnimBone( r, finger, out var f ) ) line += $"; to finger (hand local) {Local( ( f.Position - hand.Position ).Normal )}";
			if ( thumb is not null && AnimBone( r, thumb, out var t ) ) line += $"; to thumb {Local( ( t.Position - hand.Position ).Normal )}";
			if ( AnimBone( r, lower, out var lo ) ) line += $"; from forearm {Local( ( hand.Position - lo.Position ).Normal )}";
			if ( AnimBone( r, "head", out var hd ) ) line += $"; head rel body {Body( hd.Position - r.WorldPosition )}";
			if ( AnimBone( r, $"hand_{side}_IK_attach", out var ia ) )
			{
				var rel = hand.Rotation.Inverse * ia.Rotation;
				line += $"; IK attach offset (hand local) {Local( ia.Position - hand.Position )} rot {rel.Angles()}";
			}
			Log.Info( line );
		}
	}

	[ConCmd( "swrp_emote_fix", Help = "dev: swrp_emote_fix <pitch> <yaw> <roll> [left 0/1] - correction from the emote hand frame to the hand bone" )]
	public static void FixCommand( float pitch, float yaw, float roll, int left = 0 )
	{
		if ( left == 1 ) FixLeft = Rotation.From( pitch, yaw, roll );
		else FixRight = Rotation.From( pitch, yaw, roll );
	}
}
