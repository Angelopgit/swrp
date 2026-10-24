using System;

namespace Swrp.Jedi;

/// <summary>
/// The Jedi lightsaber combat system on one player. Every player gets one at spawn (SwrpGameManager), but it only wakes for
/// a Jedi (the Jedi Order faction, not playing an event character); on anyone else it just takes Force pushes and pulls.
///
/// The host is authoritative: the owner reads input and asks (Rpc.Host), the host checks focus, cooldowns and timing, runs
/// its own copy of the saber's motion to find what the blade really sweeps through, and publishes the result as synced
/// state (serial numbers for one-off actions, flags for held ones). Every machine plays the motion from that state with its
/// own <see cref="SaberMotion"/> and draws it with a <see cref="SaberRig"/> on the body; the owner starts its own swings
/// and guard at once without waiting for the round trip.
///
/// Split over: JediCombat.cs (state, the shared timeline, the rig), JediCombat.Owner.cs (input), JediCombat.Host.cs (hits,
/// blocks, parries, locks, the throw, deflecting bolts, experience) and JediForce.cs (the Force powers).
/// </summary>
[Title( "Jedi Combat" ), Category( "SWRP" ), Icon( "flare" )]
public sealed partial class JediCombat : Component, PlayerController.IEvents, ICameraModifier
{
	[ConVar( "swrp_jedi_sparring", Help = "1 = Jedi blades hurt other Jedi even with friendly fire off, never taking them below 15 health (host, default 1)", Saved = true )]
	public static bool Sparring { get; set; } = true;

	[ConVar( "swrp_jedi_lightning", Help = "1 = Jedi may learn and use Force Lightning (host, default 0)", Saved = true )]
	public static bool LightningAllowed { get; set; }

	[ConVar( "swrp_jedi_hitstop", Help = "Hit-stop on a landed saber blow in milliseconds (your view, 0 = off, default 45)", Saved = true )]
	public static float HitStopMs { get; set; } = 45f;

	// ----- synced from the host -----

	[Sync( SyncFlags.FromHost )] public bool SaberOn { get; set; }
	[Sync( SyncFlags.FromHost )] public string FormId { get; set; } = "shiicho";
	[Sync( SyncFlags.FromHost )] public float Focus { get; set; } = 100f;
	[Sync( SyncFlags.FromHost )] public float MaxFocus { get; set; } = 100f;
	[Sync( SyncFlags.FromHost )] public float ForcePoints { get; set; } = 100f;
	[Sync( SyncFlags.FromHost )] public float MaxForce { get; set; } = 100f;
	[Sync( SyncFlags.FromHost )] public bool Blocking { get; set; }

	[Sync( SyncFlags.FromHost )] public int SwingSerial { get; set; }
	[Sync( SyncFlags.FromHost )] public int SwingDirection { get; set; }
	[Sync( SyncFlags.FromHost )] public int SwingCombo { get; set; }
	[Sync( SyncFlags.FromHost )] public bool SwingHeavy { get; set; }
	[Sync( SyncFlags.FromHost )] public float SwingSpeed { get; set; } = 1f;
	[Sync( SyncFlags.FromHost )] public float SwingArc { get; set; } = 150f;

	[Sync( SyncFlags.FromHost )] public int ReactSerial { get; set; }
	[Sync( SyncFlags.FromHost )] public int ReactType { get; set; }
	[Sync( SyncFlags.FromHost )] public float ReactSeconds { get; set; }
	[Sync( SyncFlags.FromHost )] public int ParrySerial { get; set; }

	/// <summary> The other Jedi's object in a blade lock (empty when not locked). </summary>
	[Sync( SyncFlags.FromHost )] public Guid LockWith { get; set; }
	/// <summary> Who's winning the lock, -1..1 (positive = this Jedi). </summary>
	[Sync( SyncFlags.FromHost )] public float LockMeter { get; set; }

	[Sync( SyncFlags.FromHost )] public bool Thrown { get; set; }
	[Sync( SyncFlags.FromHost )] public int ThrowSerial { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector3 ThrowFrom { get; set; }
	[Sync( SyncFlags.FromHost )] public Vector3 ThrowTo { get; set; }
	[Sync( SyncFlags.FromHost )] public float ThrowSide { get; set; }
	/// <summary> Seconds out before it turns back (shortened if it hits something). </summary>
	[Sync( SyncFlags.FromHost )] public float ThrowOut { get; set; } = 0.6f;

	[Sync( SyncFlags.FromHost )] public bool SpeedActive { get; set; }
	[Sync( SyncFlags.FromHost )] public int CastSerial { get; set; }
	[Sync( SyncFlags.FromHost )] public int CastPower { get; set; }
	[Sync( SyncFlags.FromHost )] public float CastSeconds { get; set; }

	[Sync( SyncFlags.FromHost )] public int Level { get; set; } = 1;
	[Sync( SyncFlags.FromHost )] public int Xp { get; set; }
	[Sync( SyncFlags.FromHost )] public int SkillPoints { get; set; }
	/// <summary> Learned skills, comma separated (JediProgress ids). </summary>
	[Sync( SyncFlags.FromHost )] public string SkillList { get; set; } = "";
	/// <summary> The Force power on the F key (JediForce index). </summary>
	[Sync( SyncFlags.FromHost )] public int SelectedPower { get; set; }

	// ----- who and what -----

	SwrpPlayer _player;
	public SwrpPlayer Player => _player ??= GetComponent<SwrpPlayer>();
	public SwrpVitals Vitals => GetComponent<SwrpVitals>();
	public PlayerController Controller => Player?.Controller;

	/// <summary> Can fight with a lightsaber: a Jedi (the order's faction, as themselves), or anyone carrying one. </summary>
	public bool IsJedi => IsJediOrder || HasSaber;

	/// <summary> In the Jedi Order (forms, training, the Force, the 1/H keys); anyone else carrying a lightsaber (a staff spawn) can still fight with it. </summary>
	public bool IsJediOrder => Player is { } p && string.Equals( p.FactionId, "jedi", StringComparison.OrdinalIgnoreCase ) && string.IsNullOrEmpty( p.EventCharacter );

	public bool Alive => Vitals is not ( { IsDead: true } or { IsDowned: true } );

	string _designFor;
	SaberDesign _design = SaberDesign.Default;
	public SaberDesign Design
	{
		get
		{
			var look = Player?.Appearance;
			if ( look != _designFor ) { _designFor = look; _design = SaberDesign.From( look ); }
			return _design;
		}
	}

	public SaberForm Form => SaberForm.Get( FormId );
	public bool Knows( string skill ) => ( "," + SkillList + "," ).Contains( "," + skill + "," );
	public float Scale => Player?.BodyScale ?? 1f;

	public static JediCombat Local => SwrpPlayer.Local?.GetComponent<JediCombat>();

	/// <summary> Movement speed for Force Speed, guarding and blade locks (SwrpVitals multiplies the walk and run speeds by it). </summary>
	public static float MoveScale( SwrpPlayer p )
	{
		var j = p?.GetComponent<JediCombat>();
		if ( j is null || !j.IsJedi ) return 1f;
		if ( j.LockWith != Guid.Empty ) return 0.15f;
		if ( j.SpeedActive ) return 1.7f;
		if ( j.Blocking || j._ownerBlock ) return j.Form.BlockMove;
		if ( j.Motion.Kind == MotionKind.Swing && j.Motion.Heavy ) return 0.6f;
		return 1f;
	}

	/// <summary> The lightsaber's inventory item (every Jedi carries one; it's what 1, H and Use light). </summary>
	public const string SaberItem = "lightsaber";

	/// <summary> Host, before a character spawns: a Jedi without their lightsaber gets it back. True if the inventory changed. </summary>
	public static bool IssueSaber( CharacterRecord record )
	{
		if ( record is null || !string.Equals( record.FactionId, "jedi", StringComparison.OrdinalIgnoreCase ) ) return false;
		if ( record.Inventory.Any( i => i.ItemId == SaberItem && i.Count > 0 ) ) return false;
		return record.AddItem( SaberItem, 1 ) == 0;
	}

	/// <summary> Carrying the hilt (it can be dropped, and picked up again). </summary>
	public bool HasSaber => Player?.Inventory.Any( i => i.ItemId == SaberItem && i.Count > 0 ) ?? false;

	/// <summary> A Jedi player (their H key lights the saber rather than drawing the blaster). </summary>
	public static bool IsJediPlayer( SwrpPlayer p ) => p?.GetComponent<JediCombat>() is { IsJediOrder: true };

	/// <summary> Whether the saber has the trooper's hands (the blaster stays holstered and its keys go to the saber). </summary>
	public static bool HoldsSaber( SwrpPlayer p ) => p?.GetComponent<JediCombat>() is { IsJedi: true } j && ( j.SaberOn || j.Thrown );

	// ----- the motion every machine plays -----

	/// <summary> This machine's copy of the saber's motion (what gets drawn). </summary>
	public SaberMotion Motion { get; } = new();

	int _seenSwing, _seenReact, _seenParry, _seenCast, _seenThrow;
	int _predictedSwing = -1;
	bool _seenInit;
	float _ignite;            // 0..1 blade extension
	RealTimeSince _sinceThrowLocal;
	SaberRig _rig;
	Rotation _viewLag;
	bool _viewHasLag;

	protected override void OnStart()
	{
		if ( Networking.IsHost ) HostStart();
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost ) HostUpdate();
		if ( !IsJedi )
		{
			if ( _rig.IsValid() ) _rig.Destroy();
			return;
		}

		if ( !IsProxy ) OwnerUpdate();
		FollowState();
		Motion.Advance( Time.Delta );
		DriveRig();
	}

	// ----- the local view: a punch of field of view on hits, wider while Force Speed runs -----

	int ICameraModifier.CameraOrder => 250;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || !IsJedi ) return;
		view.FieldOfView += JediCamera.Fov( SpeedActive ? 9f : 0f );
	}

	protected override void OnDestroy()
	{
		if ( _rig.IsValid() ) _rig.Destroy();
		if ( Networking.IsHost ) HostSaveNow();
	}

	/// <summary> Every machine: play what the host says is happening. </summary>
	/// <summary> The cut a Jedi in reach is swinging at us right now, as this client sees it (Any when none). </summary>
	SwingDir IncomingCut()
	{
		var reach = 110f * Scale;
		var fwd = Rotation.FromYaw( FacingYaw ).Forward;
		JediCombat best = null;
		var bestD = reach * reach;
		foreach ( var other in Scene.GetAllComponents<JediCombat>() )
		{
			if ( other == this || other.Motion.Kind != MotionKind.Swing ) continue;
			var to = ( other.WorldPosition - WorldPosition ).WithZ( 0 );
			var d = to.LengthSquared;
			if ( d >= bestD || Vector3.Dot( to.Normal, fwd ) < 0.3f ) continue;
			best = other; bestD = d;
		}
		if ( best is null ) return SwingDir.Any;
		var dir = (SwingDir)best.SwingDirection;
		return dir is SwingDir.Spin or SwingDir.Overhead ? SwingDir.Any : dir;
	}

	void FollowState()
	{
		Motion.Form = Form;
		Motion.Design = Design;
		if ( !_seenInit )
		{
			// joining late: don't replay whatever happened last
			_seenInit = true;
			_seenSwing = SwingSerial; _seenReact = ReactSerial; _seenParry = ParrySerial; _seenCast = CastSerial; _seenThrow = ThrowSerial;
		}

		if ( SwingSerial != _seenSwing )
		{
			_seenSwing = SwingSerial;
			if ( IsProxy || SwingSerial != _predictedSwing )
			{
				Motion.Swing( (SwingDir)SwingDirection, SwingArc, SwingSpeed, SwingHeavy, SwingCombo );
				OnSwingStarted( SwingHeavy, SwingSpeed );
			}
		}
		if ( ReactSerial != _seenReact )
		{
			_seenReact = ReactSerial;
			Motion.React( (ReactKind)ReactType, ReactSeconds );
			_ownerCharge = false;
		}
		if ( ParrySerial != _seenParry )
		{
			_seenParry = ParrySerial;
			Motion.Parry();
		}
		if ( CastSerial != _seenCast )
		{
			_seenCast = CastSerial;
			if ( IsProxy || CastSerial != _predictedCast ) Motion.Cast( CastPower, CastSeconds );
		}
		if ( ThrowSerial != _seenThrow )
		{
			_seenThrow = ThrowSerial;
			_sinceThrowLocal = 0;
			SaberSounds.Play( SaberSounds.Throw, WorldPosition + Vector3.Up * 50f * Scale );
		}

		var locked = LockWith != Guid.Empty;
		Motion.Lock( locked );
		Motion.LockMeter = LockMeter;
		Motion.Throw( Thrown );
		var guard = ( Blocking || _ownerBlock ) && SaberOn;
		// the guard comes up from rest, or cuts a swing short once its strike is done
		if ( !locked && !Thrown && ( Motion.Kind is MotionKind.Guard or MotionKind.Block || guard && Motion.Kind == MotionKind.Swing && Motion.CanCancel ) )
		{
			// the guard swings round to meet a cut coming in, and holds there until the next one
			var cut = guard ? IncomingCut() : SwingDir.Any;
			Motion.Block( guard, cut != SwingDir.Any || Motion.Kind != MotionKind.Block ? cut : Motion.Incoming );
		}
		if ( _ownerCharge && Motion.Kind == MotionKind.Guard ) Motion.Charge( _chargeDir );

		// the blade grows out of the emitter and back in
		var want = ( SaberOn || Thrown ) && Alive ? 1f : 0f;
		var was = _ignite;
		_ignite = _ignite.Approach( want, Time.Delta / ( want > _ignite ? 0.22f : 0.28f ) );
		if ( was <= 0f && _ignite > 0f ) SaberSounds.Play( SaberSounds.Ignite, HiltPosition );
		if ( was >= 1f && _ignite < 1f && !Thrown ) SaberSounds.Play( SaberSounds.Retract, HiltPosition );
	}

	/// <summary> Roughly where the hilt is (sounds). </summary>
	Vector3 HiltPosition => WorldPosition + Rotation.FromYaw( FacingYaw ) * ( SaberMotion.Chest + new Vector3( 12f, -4f, -6f ) ) * Scale;

	/// <summary> The yaw the saber's motion is laid out on: the view's (it's where the owner swings). </summary>
	public float FacingYaw => Controller.IsValid() ? Controller.EyeAngles.yaw : WorldRotation.Yaw();
	public float AimPitch => Controller.IsValid() ? Controller.EyeAngles.pitch : 0f;

	/// <summary> The current sample, as drawn (in body space). </summary>
	public MotionSample Sample { get; private set; }

	void DriveRig()
	{
		var body = Player?.Body;
		if ( !body.IsValid() ) return;
		if ( !_rig.IsValid() || _rig.GameObject != body.GameObject )
		{
			_rig = body.Components.Get<SaberRig>( FindMode.EverythingInSelf ) ?? body.Components.Create<SaberRig>();
		}
		var firstPerson = !IsProxy && Controller.IsValid() && !Controller.ThirdPerson && SwrpUiState.SpectateSteamId == 0;
		// first person: the saber is laid out round the view itself (the whole view's pitch, not half of it round the chest),
		// trailing a turn of the head a little so it has weight
		var sample = Motion.Sample( firstPerson ? 0f : AimPitch, Time.Now );
		Transform? view = null;
		if ( firstPerson )
		{
			var eye = Controller.EyeTransform;
			_viewLag = _viewHasLag ? Rotation.Slerp( _viewLag, eye.Rotation, MathF.Min( 1f, Time.Delta * 16f ) ) : eye.Rotation;
			_viewHasLag = true;
			view = new Transform( eye.Position, _viewLag );
		}
		else _viewHasLag = false;
		Sample = sample;

		// the animgraph parameters an authored saber graph reads (Code/Jedi/ANIMGRAPH.md); the current graph ignores them and
		// the procedural layer in SaberRig does the work
		body.Set( "saber_active", SaberOn || Thrown );
		body.Set( "is_blocking", Blocking || _ownerBlock );
		body.Set( "attack_index", Motion.Kind == MotionKind.Swing ? Motion.Combo : -1 );
		body.Set( "swing_dir", Motion.Kind == MotionKind.Swing ? (int)Motion.Dir : 0 );
		body.Set( "form_id", Form.FormId );
		body.Set( "saber_mode", Design.Mode );
		body.Set( "saber_react", Motion.Kind == MotionKind.React ? ReactType + 1 : 0 );
		body.Set( "saber_lock", LockWith != Guid.Empty );
		body.Set( "force_cast", sample.Cast > 0.01f ? sample.CastPower + 1 : 0 );
		var dead = !Alive;
		_rig.Drive( new SaberRig.Input
		{
			Sample = sample,
			Design = Design,
			Ignite = _ignite,
			Holstered = !SaberOn && !Thrown && _ignite <= 0f,
			Posing = ( SaberOn || _ignite > 0f || Thrown || sample.Cast > 0f ) && !dead,
			Scale = Scale,
			Origin = WorldPosition,
			Yaw = firstPerson ? FacingYaw : BodyYaw(),
			FirstPerson = firstPerson,
			View = view,
			MoveSpeed = Controller.IsValid() ? Controller.Velocity.WithZ( 0 ).Length : 0f,
			Thrown = Thrown,
			ThrowTime = _sinceThrowLocal,
			Throw = ThrowPath,
			LockPoint = LockContact(),
			LookTarget = LookTarget(),
			Owner = this,
		} );
	}

	/// <summary> Third person: the yaw the body is turned to (it lags the view a little); the saber follows the view. </summary>
	float BodyYaw()
	{
		var body = Player?.Body;
		var eye = FacingYaw;
		if ( !body.IsValid() ) return eye;
		// the body's own facing, pulled most of the way to the view so the arms can reach where the blade is
		var by = body.WorldRotation.Yaw();
		return by + MathX.DeltaDegrees( by, eye ) * 0.85f;
	}

	// ----- the thrown saber's flight, the same curve on every machine -----

	/// <summary> Where the thrown saber is t seconds after leaving the hand, and whether it's on the way back. </summary>
	public (Vector3 Pos, bool Returning, bool Done) ThrowPath( float t, Vector3 hand )
	{
		var outT = MathF.Max( 0.15f, ThrowOut );
		var from = ThrowFrom;
		var to = ThrowTo;
		var dist = from.Distance( to );
		var side = Rotation.LookAt( ( to - from ).WithZ( 0 ).Normal ).Right * ThrowSide * dist * 0.28f;
		if ( t < outT )
		{
			var u = t / outT;
			var u2 = 1f - ( 1f - u ) * ( 1f - u );       // fast out of the hand, slowing as it reaches the far end
			var ctrl = ( from + to ) * 0.5f + side;
			return ( Bezier( from, ctrl, to, u2 ), false, false );
		}
		var back = Math.Clamp( ( t - outT ) / ( outT * 1.05f ), 0f, 1f );
		var b2 = back * back;
		var ctrlBack = ( to + hand ) * 0.5f - side;
		return ( Bezier( to, ctrlBack, hand, b2 ), true, back >= 1f );
	}

	static Vector3 Bezier( Vector3 a, Vector3 c, Vector3 b, float u ) => Vector3.Lerp( Vector3.Lerp( a, c, u ), Vector3.Lerp( c, b, u ), u );

	/// <summary> Where the hand would catch the saber (the right hand at guard), in the world. </summary>
	public Vector3 CatchPoint => WorldPosition + Rotation.FromYaw( FacingYaw ) * ( SaberMotion.Chest + new Vector3( 16f, -6f, 2f ) ) * Scale;

	// ----- blades in the world (any machine, from its own motion) -----

	/// <summary> The lit blades as world segments (emitter, tip), from a sample. </summary>
	public List<(Vector3 Base, Vector3 Tip)> BladesFor( MotionSample s, float extension = 1f )
	{
		var list = new List<(Vector3, Vector3)>();
		var d = Design;
		var scale = Scale;
		var yaw = FacingYaw;
		var main = SaberMotion.ToWorld( s.Main, WorldPosition, yaw, scale );
		if ( d.IsStaff )
		{
			var half = d.HiltLength * 0.5f * scale;
			var len = d.BladeLength * scale * extension;
			list.Add( ( main.Hand + main.Dir * half, main.Hand + main.Dir * ( half + len ) ) );
			list.Add( ( main.Hand - main.Dir * half, main.Hand - main.Dir * ( half + len ) ) );
		}
		else
		{
			var (b, t) = SaberMotion.Blade( main, d.HiltLength, d.BladeLength * extension, scale );
			list.Add( ( b, t ) );
			if ( d.IsDual )
			{
				var off = SaberMotion.ToWorld( s.Off, WorldPosition, yaw, scale );
				var (ob, ot) = SaberMotion.Blade( off, SaberDesign.ShotoHilt, SaberDesign.ShotoBlade * extension, scale );
				list.Add( ( ob, ot ) );
			}
		}
		return list;
	}

	/// <summary> In a blade lock: where the two blades cross (for the sparks), from this machine's view of both. </summary>
	Vector3? LockContact()
	{
		if ( LockWith == Guid.Empty ) return null;
		var other = Scene.Directory.FindByGuid( LockWith )?.Components.Get<JediCombat>();
		if ( other is null ) return null;
		var mine = BladesFor( Sample );
		var theirs = other.BladesFor( other.Sample );
		if ( mine.Count == 0 || theirs.Count == 0 ) return null;
		var (p, q, _) = SegmentClosest( mine[0].Base, mine[0].Tip, theirs[0].Base, theirs[0].Tip );
		return ( p + q ) * 0.5f;
	}

	/// <summary> Somebody worth looking at: the nearest enemy (or Jedi in a lock) in front, within reach. </summary>
	Vector3? LookTarget()
	{
		if ( !SaberOn ) return null;
		if ( LockWith != Guid.Empty && Scene.Directory.FindByGuid( LockWith ) is { } partner ) return partner.WorldPosition + Vector3.Up * 60f * Scale;
		if ( _lookScan < 0.3f ) return _lookAt;
		_lookScan = 0;
		_lookAt = null;
		var eye = WorldPosition + Vector3.Up * 64f * Scale;
		var fwd = Rotation.FromYaw( FacingYaw ).Forward;
		var best = 500f * Scale;
		foreach ( var npc in Scene.GetAllComponents<SwrpNpc>() )
		{
			if ( npc.IsDead || AllegianceRegistry.AreFriendly( npc.Allegiance, Player.Allegiance ) ) continue;
			var to = npc.Eye - eye;
			var d = to.Length;
			if ( d < best && Vector3.Dot( to.Normal, fwd ) > 0.3f ) { best = d; _lookAt = npc.Eye; }
		}
		foreach ( var other in Scene.GetAllComponents<JediCombat>() )
		{
			if ( other == this || !other.IsJedi || !other.SaberOn ) continue;
			var at = other.WorldPosition + Vector3.Up * 62f * other.Scale;
			var to = at - eye;
			var d = to.Length;
			if ( d < best && Vector3.Dot( to.Normal, fwd ) > 0.3f ) { best = d; _lookAt = at; }
		}
		return _lookAt;
	}
	RealTimeSince _lookScan;
	Vector3? _lookAt;

	/// <summary> The closest points between two segments and the distance between them. </summary>
	public static (Vector3 P, Vector3 Q, float Distance) SegmentClosest( Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2 )
	{
		var d1 = q1 - p1;
		var d2 = q2 - p2;
		var r = p1 - p2;
		var a = Vector3.Dot( d1, d1 );
		var e = Vector3.Dot( d2, d2 );
		var f = Vector3.Dot( d2, r );
		float s, t;
		if ( a <= 1e-6f && e <= 1e-6f ) return ( p1, p2, p1.Distance( p2 ) );
		if ( a <= 1e-6f ) { s = 0; t = Math.Clamp( f / e, 0f, 1f ); }
		else
		{
			var c = Vector3.Dot( d1, r );
			if ( e <= 1e-6f ) { t = 0; s = Math.Clamp( -c / a, 0f, 1f ); }
			else
			{
				var b = Vector3.Dot( d1, d2 );
				var denom = a * e - b * b;
				s = denom != 0 ? Math.Clamp( ( b * f - c * e ) / denom, 0f, 1f ) : 0f;
				t = ( b * s + f ) / e;
				if ( t < 0 ) { t = 0; s = Math.Clamp( -c / a, 0f, 1f ); }
				else if ( t > 1 ) { t = 1; s = Math.Clamp( ( b - c ) / a, 0f, 1f ); }
			}
		}
		var cp = p1 + d1 * s;
		var cq = p2 + d2 * t;
		return ( cp, cq, cp.Distance( cq ) );
	}
}
