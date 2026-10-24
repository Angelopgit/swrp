using System;

namespace Swrp.Jedi;

/// <summary>
/// Jedi on the local mannequins (the character screen's hangar stand and the swrp_anim_test stand): the saber the Jedi built,
/// lit in the hands with the odd flourish on the character screen, or put through every move on the test stand
/// (swrp_saber_test). Takes over from <see cref="SwrpAnimMannequin"/> while the mannequin is a Jedi (it hands the body back
/// when it isn't), so nothing else poses the arms at the same time. Local only.
/// </summary>
public sealed class JediStand : Component
{
	public SwrpAnimMannequin Mannequin { get; set; }
	/// <summary> Run the full demo (every swing, guard, parry, lock, powers) instead of the character screen's idle. </summary>
	public bool Demo { get; set; }

	/// <summary> Just standing, saber off on the belt (the character screen, Angelo 2026-10-18). </summary>
	public bool Still { get; set; }
	public string FormId { get; set; } = "shiicho";

	/// <summary>
	/// Hold one pose for inspection instead of moving (the pose stage): "guard", "block", "lock", "react", "throw",
	/// "cast", or a swing frame "left@0.4" (direction @ how far through the cut, 0..1; below 0 is the wind-up, above 1 the
	/// follow-through).
	/// </summary>
	public string Hold { get; set; }
	string _held;

	readonly SaberMotion _motion = new();
	SaberRig _rig;
	SkinnedModelRenderer _body;
	float _ignite;
	bool _lit = true;
	int _step;
	TimeUntil _next = 0.6f;

	/// <summary> Every frame (the system below): hand Jedi mannequins to a stand, and give the others back. </summary>
	internal static void Tick( Scene scene )
	{
		foreach ( var m in scene.GetAllComponents<SwrpAnimMannequin>() )
		{
			if ( !IsJedi( m.Faction ) || m.SequenceMode ) continue;
			var stand = m.Components.Get<JediStand>( FindMode.EverythingInSelf ) ?? m.Components.Create<JediStand>();
			stand.Mannequin = m;
			m.Enabled = false;
		}
	}

	static bool IsJedi( string faction ) => string.Equals( faction, "jedi", StringComparison.OrdinalIgnoreCase );

	protected override void OnUpdate()
	{
		_body ??= GetComponent<SkinnedModelRenderer>();
		var m = Mannequin ?? GetComponent<SwrpAnimMannequin>( true );
		if ( m is null || !IsJedi( m.Faction ) || !_body.IsValid() )
		{
			if ( m is not null ) m.Enabled = true;
			foreach ( var c in GameObject.Children )
				if ( c.Name == "Gun" ) c.Enabled = true;
			Destroy();
			return;
		}

		// what the mannequin did for the body: locomotion parameters and the outfit (here, the Jedi)
		var fwd = WorldRotation.Forward;
		_body.Set( "holdtype", 0 );
		_body.Set( "b_grounded", true );
		_body.Set( "move_speed", 0f );
		_body.Set( "move_groundspeed", 0f );
		_body.Set( "wish_speed", 0f );
		_body.SetLookDirection( "aim_eyes", fwd );
		_body.SetLookDirection( "aim_head", fwd );
		_body.SetLookDirection( "aim_body", fwd );
		SwrpOutfit.Apply( _body, m.Faction, false, m.Appearance );
		foreach ( var c in GameObject.Children )
			if ( c.Name == "Gun" && c.Enabled ) c.Enabled = false;

		var design = SaberDesign.From( m.Appearance );
		_motion.Design = design;
		_motion.Form = SaberForm.Get( FormId );
		if ( Hold is not null ) HoldStep(); else if ( Opponent is not null ) { DuelStep(); DefendStep(); } else if ( Demo ) DemoStep( design ); else IdleStep();
		_motion.Advance( Time.Delta );
		_ignite = _ignite.Approach( _lit ? 1f : 0f, Time.Delta / ( _lit ? 0.22f : 0.28f ) );

		_rig ??= Components.Get<SaberRig>( FindMode.EverythingInSelf ) ?? Components.Create<SaberRig>();
		var sample = Hold is not null && _holdT is { } ht ? _motion.Probe( ht, 0f, Time.Now ) : _motion.Sample( 0f, Time.Now );
		if ( Demo ) { PinTargets(); HitTest( sample, design ); StrikeSlowMo( sample ); }
		_lastBlade = Blade;
		Blade = SaberMotion.Blade( SaberMotion.ToWorld( sample.Main, WorldPosition, WorldRotation.Yaw(), WorldScale.z ), design.HiltLength, design.BladeLength, WorldScale.z );
		if ( Opponent is not null && Attacker && sample.HitWindow ) DuelContact( design );
		_rig.Drive( new SaberRig.Input
		{
			Sample = sample,
			Design = design,
			Ignite = _ignite,
			Holstered = !_lit && _ignite <= 0f,
			Posing = _lit || _ignite > 0f || sample.Cast > 0f,
			Scale = WorldScale.z,
			Origin = WorldPosition,
			Yaw = WorldRotation.Yaw(),
		} );
	}

	float? _holdT;

	/// <summary> The pose stage: put the motion in the held pose (once), and for a swing, the frame to show. </summary>
	void HoldStep()
	{
		_lit = true;
		_holdT = null;
		var parts = Hold.Split( '@' );
		var what = parts[0].ToLowerInvariant();
		if ( Enum.TryParse<SwingDir>( what, true, out var dir ) && dir != SwingDir.Any )
		{
			var phase = parts.Length > 1 && float.TryParse( parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p ) ? p : 0.5f;
			if ( _held != Hold || _motion.Kind != MotionKind.Swing )
			{
				_motion.Guard();
				_motion.Advance( 1f );
				_motion.Sample( 0f, Time.Now );
				_motion.Swing( dir, _motion.Form.Combo.FirstOrDefault( s => s.Direction == dir )?.Arc ?? 150f, 1f, false, 0 );
			}
			var tm = _motion.SwingTiming;
			_holdT = phase < 0f ? tm.Windup * ( 1f + phase ) : phase > 1f ? tm.Windup + tm.Strike + tm.Follow * MathF.Min( 1f, phase - 1f ) : tm.Windup + tm.Strike * phase;
		}
		else if ( _held != Hold )
		{
			_motion.Guard();
			_motion.Advance( 1f );
			switch ( what )
			{
				case "block": _motion.Block( true ); break;
				case "lock": _motion.Lock( true ); break;
				case "react": _motion.React( ReactKind.Parried, 99f ); break;
				case "throw": _motion.Throw( true ); break;
				case "cast": _motion.Cast( JediForce.Push, 99f ); break;
				case "push": case "pull": case "lightning": case "heal": case "leap": _motion.Cast( what switch { "pull" => JediForce.Pull, "lightning" => JediForce.Lightning, "heal" => JediForce.Heal, "leap" => JediForce.Leap, _ => JediForce.Push }, 99f ); break;
			}
		}
		_held = Hold;

		// a held Force power fires its effect over and over (so a screenshot catches it)
		var power = what switch { "push" => JediForce.Push, "pull" => JediForce.Pull, "lightning" => JediForce.Lightning, "heal" => JediForce.Heal, "leap" => JediForce.Leap, _ => -1 };
		if ( power >= 0 && _sinceFx > ( power == JediForce.Lightning ? 0.15f : 0.6f ) )
		{
			_sinceFx = 0;
			var fwd = WorldRotation.Forward;
			var hand = WorldPosition + WorldRotation * ( SaberMotion.Chest + new Vector3( 22f, 6f, 6f ) ) * WorldScale.z;
			var at = power switch { JediForce.Pull => hand + fwd * 300f, JediForce.Lightning => hand, JediForce.Heal or JediForce.Leap => WorldPosition, _ => hand + fwd * 20f };
			var aim = power switch { JediForce.Pull => -fwd * 300f, JediForce.Lightning => fwd * 260f, JediForce.Heal => Vector3.Up, _ => fwd };
			SaberFx.Force( Scene, null, power, at, aim, power == JediForce.Heal ? 3f : 400f, power == JediForce.Heal ? GameObject : null );
		}
	}
	RealTimeSince _sinceFx;

	/// <summary> The character screen: lit at guard, now and then a cut or two through the air. </summary>
	void IdleStep()
	{
		_lit = !Still;      // the character screen: saber off and on the belt, hands down
		if ( Still || _next > 0f ) return;
		_next = Game.Random.Float( 3.5f, 6f );
		var dirs = new[] { SwingDir.Left, SwingDir.Right, SwingDir.Overhead, SwingDir.Rising };
		var d = dirs[Game.Random.Int( 0, dirs.Length - 1 )];
		_motion.Swing( d, 150f, 0.8f, false, 0 );
		SaberSounds.Swing( WorldPosition + Vector3.Up * 45f, false, 0.8f, _motion.Design.CrystalDef.HumPitch );
	}

	/// <summary> The test stand: every move in turn, then round again. </summary>
	void DemoStep( SaberDesign design )
	{
		if ( _next > 0f ) return;
		var at = WorldPosition + Vector3.Up * 45f;
		switch ( _step++ % 16 )
		{
			case 0: _lit = true; _next = 1.2f; break;
			case 1: Swing( SwingDir.Left, false ); break;
			case 2: Swing( SwingDir.Right, false ); break;
			case 3: Swing( SwingDir.Overhead, false ); break;
			case 4: Swing( SwingDir.Thrust, false ); break;
			case 5: Swing( SwingDir.Rising, false ); break;
			case 6: Swing( SwingDir.Left, true ); break;
			case 7: Swing( SwingDir.Spin, false ); _next = 1.4f; break;
			case 8: _motion.Block( true ); _next = 1.0f; break;
			case 9: _motion.Parry(); SaberFx.Hit( Scene, SaberHit.Parry, at + WorldRotation.Forward * 14f, Vector3.Up, design.BladeColor, null ); _next = 0.8f; break;
			case 10: _motion.Block( false ); _motion.React( ReactKind.Parried, 0.75f ); _next = 1.1f; break;
			case 11: _motion.Lock( true ); _next = 1.6f; break;
			case 12: _motion.Lock( false ); _motion.Cast( JediForce.Push, 0.45f ); SaberFx.Force( Scene, null, JediForce.Push, at + WorldRotation.Forward * 20f, WorldRotation.Forward, 400f, null ); _next = 1.2f; break;
			case 13: _motion.Cast( JediForce.Pull, 0.55f ); _next = 1.2f; break;
			case 14: _lit = false; _next = 1.6f; break;
			case 15: _next = 0.4f; break;
		}
	}

	// ----- the hit test: the same sweep the host runs, counted per swing (nobody is hurt) -----

	readonly Dictionary<GameObject, int> _hitCount = new();
	float _lastT = -1f;
	SwingDir _testDir;
	int _swings, _missed, _doubled;

	void HitTest( MotionSample s, SaberDesign d )
	{
		// a new swing: report the last one
		if ( _motion.Kind == MotionKind.Swing && _motion.Time < _lastT ) Report();
		if ( _motion.Kind != MotionKind.Swing && _lastT >= 0f ) Report();
		_lastT = _motion.Kind == MotionKind.Swing ? _motion.Time : -1f;
		if ( _motion.Kind == MotionKind.Swing ) _testDir = _motion.Dir;

		var scale = WorldScale.z;
		List<(Vector3 Base, Vector3 Tip)> Blades( MotionSample x )
		{
			var w = SaberMotion.ToWorld( x.Main, WorldPosition, WorldRotation.Yaw(), scale );
			return new() { SaberMotion.Blade( w, d.HiltLength, d.BladeLength, scale ) };
		}
		SaberSweep.Step( _motion, _sweep, 0f, Blades, ( i, a0, b0, a1, b1, dt ) =>
		{
			var seen = new HashSet<GameObject>();
			foreach ( var (from, to) in SaberSweep.Segments( a0, b0, a1, b1, scale ) )
			{
				foreach ( var tr in Scene.Trace.Ray( from, to ).Radius( 1.6f * scale ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll" ).UseHitboxes().RunAll() )
				{
					var npc = tr.GameObject?.Components.GetInAncestorsOrSelf<SwrpNpc>();
					var post = tr.GameObject is { } g && g.Name.StartsWith( "Saber Target" ) ? g : null;
					var target = npc?.GameObject ?? post;
					if ( target is null || !seen.Add( target ) ) continue;
					_hitCount[target] = _hitCount.GetValueOrDefault( target ) + 1;
					// the host only counts the first: show it so the feel can be judged
					if ( _hitCount[target] == 1 )
					{
						_motion.HitStop( JediCombat.HitStopMs / 1000f );
						_rig?.Flare();
						// the reaction the host gives a real blow (no damage): let go of the pin a moment so the shove shows
						if ( npc is not null )
						{
							// (it goes back to its spot afterwards, so a run of cuts doesn't walk it out of reach)
							_unpinned[npc] = 0;
							JediCombat.NpcStagger( npc, b1 - b0, _motion.Heavy, scale, null, WorldPosition );
							FxFlinch.Hit( npc.GameObject, tr.HitPosition, b1 - b0 );
						}
						var droid = npc is null || npc.Allegiance == AllegianceRegistry.Cis;
						SaberFx.Hit( Scene, droid ? SaberHit.Droid : SaberHit.Flesh, tr.HitPosition, ( b1 - b0 ).Normal, d.BladeColor, npc?.GameObject );
					}
				}
			}
			return false;
		} );
	}

	readonly SaberSweep.State _sweep = new();

	readonly Dictionary<SwrpNpc, Vector3> _pins = new();
	readonly Dictionary<SwrpNpc, RealTimeSince> _unpinned = new();

	/// <summary> Test only: NPCs placed in reach of the test stand are held where they were put, so every swing can be judged. </summary>
	void PinTargets()
	{
		foreach ( var n in Scene.GetAllComponents<SwrpNpc>() )
		{
			if ( _unpinned.TryGetValue( n, out var since ) && since < 0.8f ) continue;
			if ( !_pins.TryGetValue( n, out var at ) )
			{
				if ( n.WorldPosition.Distance( WorldPosition ) > 70f * WorldScale.z ) continue;
				_pins[n] = at = n.WorldPosition;
			}
			n.WorldPosition = at;
		}
	}

	void Report()
	{
		if ( _lastT < 0f ) return;
		_swings++;
		// what a cut ahead should reach: NPCs and target posts in front, within the blade's reach
		var fwd = WorldRotation.Forward;
		bool Ahead( GameObject g ) { var to = ( g.WorldPosition - WorldPosition ).WithZ( 0 ); return to.Length < 46f * WorldScale.z && Vector3.Dot( to.Normal, fwd ) > 0.5f; }
		var inReach = Scene.GetAllComponents<SwrpNpc>().Select( n => n.GameObject ).Concat( Scene.GetAllObjects( true ).Where( g => g.Name.StartsWith( "Saber Target" ) ) ).Where( Ahead ).ToList();
		var missed = inReach.Where( n => !_hitCount.ContainsKey( n ) ).ToList();
		var doubled = _hitCount.Where( kv => kv.Value > 1 ).ToList();
		_missed += missed.Count;
		_doubled += doubled.Count;
		Log.Info( $"[JEDI TEST] {_testDir}: missed {string.Join( ",", missed.Select( m => m.Name.Replace( "Saber Target ", "#" ) ) )}; hit {_hitCount.Count} ({string.Join( ", ", _hitCount.Select( kv => $"{kv.Key.Name} x{kv.Value} frames" ) )}), in reach but missed {missed.Count}; totals {_swings} swings, {_missed} misses" );
		_hitCount.Clear();
		_lastT = -1f;
	}

	void Swing( SwingDir dir, bool heavy )
	{
		_motion.Swing( dir, 150f, heavy ? 0.85f : 1f, heavy, 0 );
		SaberSounds.Swing( WorldPosition + Vector3.Up * 45f, heavy, 1f, _motion.Design.CrystalDef.HumPitch );
		_next = heavy ? 1.3f : 0.95f;
	}

	protected override void OnDestroy()
	{
		if ( _rig.IsValid() ) _rig.Destroy();
	}

	/// <summary> Target posts round the test stand (a fan of body-sized boxes, nothing else): what each swing should cut. </summary>
	[ConCmd( "swrp_saber_targets", Help = "swrp_saber_targets [radius] - posts in a fan in front of the saber test stand for its hit test; swrp_saber_targets 0 removes them" )]
	public static void TargetsCommand( float radius = 34f )
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		foreach ( var old in scene.GetAllObjects( true ).Where( g => g.Name.StartsWith( "Saber Target" ) ).ToList() ) old.Destroy();
		var stand = scene.GetAllComponents<JediStand>().FirstOrDefault();
		if ( radius <= 0f || stand is null ) return;
		var box = Model.Load( "models/dev/box.vmdl" );
		var i = 0;
		foreach ( var a in new[] { -60f, -30f, 0f, 30f, 60f } )
		{
			var go = new GameObject( true, $"Saber Target {++i}" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = stand.WorldPosition + ( stand.WorldRotation * Rotation.FromYaw( a ) ).Forward * radius * stand.WorldScale.z;
			go.WorldRotation = stand.WorldRotation;
			var col = go.Components.Create<BoxCollider>();
			col.Scale = new Vector3( 12f, 12f, 66f );
			col.Center = Vector3.Up * 33f;
			var vis = new GameObject( go, true, "Post Mesh" );
			vis.LocalPosition = Vector3.Up * 33f;
			vis.LocalScale = new Vector3( 12f, 12f, 66f ) / 50f;
			var r = vis.Components.Create<ModelRenderer>();
			r.Model = box;
			r.Tint = new Color( 0.4f, 0.42f, 0.45f );
		}
	}

	// ----- the duel stand (swrp_saber_duel): one stand attacks, the other guards, so blocks, parries and clashes can be watched -----

	/// <summary> The blade as drawn this frame (emitter, tip). </summary>
	public (Vector3 Base, Vector3 Tip) Blade { get; private set; }
	public JediStand Opponent { get; set; }
	public bool Attacker { get; set; }
	int _duelStep;
	int _contactFor = -1;
	float _guardLead;
	(Vector3 Base, Vector3 Tip) _lastBlade;
	float _minGap = float.MaxValue;
	string _lastCut;
	bool _touched;
	float _expect;
	TimeSince _sinceCut;
	MotionKind _kindAtGap;

	/// <summary> The attacker cuts in turn from every side; the defender raises its guard either just in time (a parry) or early (a block). </summary>
	void DuelStep()
	{
		_lit = true;
		if ( !Attacker ) return;
		if ( _next > 0f ) return;
		var dirs = new[] { SwingDir.Left, SwingDir.Right, SwingDir.Overhead, SwingDir.Rising, SwingDir.Thrust };
		var dir = dirs[_duelStep % dirs.Length];
		var heavy = _duelStep % 3 == 2;
		if ( _lastCut is not null && !_touched )
			Log.Info( $"[JEDI DUEL] {_lastCut}: NO CONTACT (closest {_minGap / WorldScale.z:0.0} units, defender {_kindAtGap})" );
		_lastCut = $"{dir}{( heavy ? " heavy" : "" )}";
		_minGap = float.MaxValue;
		_touched = false;
		_sinceCut = 0;
		_motion.Swing( dir, _motion.Form.Step( 0 ).Arc, _motion.Form.Tempo, heavy, 0 );
		SaberSounds.Swing( WorldPosition + Vector3.Up * 45f, heavy, _motion.Form.Tempo, _motion.Design.CrystalDef.HumPitch );
		// the defender: alternate a late raise (inside the parry window: a parry) and an early one (a plain block)
		var parry = _duelStep % 2 == 0;
		var contact = SaberMotion.TimingFor( dir, heavy, _motion.Form.Tempo ).Windup + SaberMotion.TimingFor( dir, heavy, _motion.Form.Tempo ).Strike * 0.4f;
		Opponent._guardLead = parry ? Opponent._motion.Form.ParryWindow * 0.5f : 0.45f;
		Opponent._raiseIn = MathF.Max( 0f, contact - Opponent._guardLead );
		_expect = contact;
		Opponent._parryNext = parry;
		Opponent._incoming = dir;
		_duelStep++;
		_next = heavy ? 1.9f : 1.5f;
	}

	float _raiseIn = -1f;
	bool _parryNext;
	SwingDir _incoming;
	TimeSince _sinceRaise;

	/// <summary> The defender's guard goes up on cue and comes down after the blow (per frame, on the same clock as the cut, so slow motion keeps the timing). </summary>
	void DefendStep()
	{
		if ( Opponent is null || Attacker ) return;
		if ( _raiseIn >= 0f )
		{
			_raiseIn -= Time.Delta;
			if ( _raiseIn < 0f ) { _motion.Block( true, _incoming ); _sinceRaise = 0; }
		}
		else if ( _motion.Kind == MotionKind.Block && _sinceRaise > 0.9f ) _motion.Block( false );
	}

	/// <summary> The attacker's blade meets the defender's: a parry (the attacker thrown back) or a block (a bounce), as the host would judge it. </summary>
	void DuelContact( SaberDesign d )
	{
		if ( _motion.Kind != MotionKind.Swing || _contactFor == _motion.ActionId ) return;
		// swept: the blade can cross the other between frames, so walk from last frame's blade to this one
		var (oa, ob) = Opponent.Blade;
		var dist = float.MaxValue;
		var at = Vector3.Zero;
		for ( var k = 1; k <= 6; k++ )
		{
			var u = k / 6f;
			var (p, q, gap) = JediCombat.SegmentClosest( Vector3.Lerp( _lastBlade.Base, Blade.Base, u ), Vector3.Lerp( _lastBlade.Tip, Blade.Tip, u ), oa, ob );
			if ( gap < dist ) { dist = gap; at = ( p + q ) * 0.5f; }
		}
		if ( !_touched && dist < _minGap ) { _minGap = dist; _kindAtGap = Opponent._motion.Kind; }
		if ( dist > 3.2f * WorldScale.z || Opponent._motion.Kind != MotionKind.Block ) return;
		_contactFor = _motion.ActionId;
		_touched = true;
		var lead = Opponent._sinceRaise * 1000f;
		var perfect = Opponent._parryNext && Opponent._sinceRaise <= Opponent._motion.Form.ParryWindow;
		var stop = JediCombat.HitStopMs / 1000f;
		_motion.HitStop( stop );
		Opponent._motion.HitStop( stop * 0.8f );
		_rig?.Flare();
		Opponent._rig?.Flare();
		if ( perfect )
		{
			Opponent._motion.Parry();
			_motion.React( ReactKind.Parried, 0.75f );
			SaberFx.Hit( Scene, SaberHit.Parry, at, Vector3.Up, d.BladeColor, null );
		}
		else
		{
			_motion.React( _motion.Heavy ? ReactKind.Blocked : ReactKind.Blocked, _motion.Heavy ? 0.4f : 0.3f );
			Opponent._motion.React( ReactKind.BlockShake, 0.18f );
			Opponent._sinceRaise = 0;
			SaberFx.Hit( Scene, SaberHit.Block, at, Vector3.Up, d.BladeColor, null );
		}
		Log.Info( $"[JEDI DUEL] {_motion.Dir}{( _motion.Heavy ? " heavy" : "" )}: {( perfect ? "PARRIED" : "blocked" )} (guard up {lead:0} ms before contact, {_sinceCut * 1000f:0} ms into the cut, expected {_expect * 1000f:0})" );
	}

	static bool _slowOnStrike;
	static RealTimeSince _sinceStrikeSlow = 99f;

	/// <summary> Dev filming: the test stand drops the scene to a crawl for a few seconds each time a cut bites. </summary>
	void StrikeSlowMo( MotionSample s )
	{
		if ( !_slowOnStrike ) return;
		if ( Scene.TimeScale < 1f )
		{
			if ( _sinceStrikeSlow > 3f ) Scene.TimeScale = 1f;
			return;
		}
		if ( s.HitWindow && _sinceStrikeSlow > 4f ) { Scene.TimeScale = 0.03f; _sinceStrikeSlow = 0; }
	}

	[ConCmd( "swrp_saber_strikeslow", Help = "swrp_saber_strikeslow [0|1] - dev: the test stand slows the scene to a crawl for 3 s each time a cut reaches its hit window, to film trails" )]
	public static void StrikeSlowCommand( int on = 1 )
	{
		_slowOnStrike = on != 0;
		if ( !_slowOnStrike && Game.ActiveScene is { } scene ) scene.TimeScale = 1f;
	}

	[ConCmd( "swrp_saber_slowmo", Help = "swrp_saber_slowmo [scale] - dev: slow the local scene (0.1 = a tenth speed, 1 = normal) to film swings and trails" )]
	public static void SlowMoCommand( float scale = 1f )
	{
		if ( Game.ActiveScene is not { } scene ) return;
		scene.TimeScale = Math.Clamp( scale, 0.02f, 1f );
		Log.Info( $"[SWRP] time scale {scene.TimeScale}" );
	}

	[ConCmd( "swrp_saber_duel", Help = "swrp_saber_duel [attacker form] [defender form] [crystal A] [crystal B] - a local duel stand: one Jedi cuts from every side, the other blocks and parries in turn; swrp_saber_duel off removes it" )]
	public static void DuelCommand( string attacker = "djemso", string defender = "soresu", int crystalA = 0, int crystalB = 1 )
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		foreach ( var old in scene.GetAllObjects( true ).Where( g => g.Name.StartsWith( "Saber Duel" ) ).ToList() ) old.Destroy();
		if ( attacker == "off" ) return;
		var cam = scene.Camera;
		var fwd = cam?.WorldRotation.Forward.WithZ( 0 ).Normal ?? Vector3.Forward;
		var mid = cam is not null ? scene.Trace.Ray( cam.WorldPosition + fwd * 120f, cam.WorldPosition + fwd * 120f + Vector3.Down * 400f ).Run().EndPosition : Vector3.Zero;
		var side = Vector3.Cross( fwd, Vector3.Up ).Normal;
		var gap = 26f * SwrpPlayer.PlayerScaleFor( scene );
		JediStand Make( string name, Vector3 at, Vector3 facing, string form, int crystal )
		{
			var go = new GameObject( true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = at;
			go.WorldRotation = Rotation.LookAt( facing.WithZ( 0 ) );
			go.WorldScale = SwrpPlayer.PlayerScaleFor( scene );
			var r = go.Components.Create<SkinnedModelRenderer>();
			r.Model = ClassRegistry.Default.LoadModel() ?? Model.Load( "models/citizen/citizen.vmdl" );
			var m = go.Components.Create<SwrpAnimMannequin>();
			m.HoldType = 0;
			m.Faction = "jedi";
			m.Appearance = new Appearance { SaberCrystal = crystal, Hair = crystal % 4 }.ToString();
			m.Enabled = false;
			var s = go.Components.Create<JediStand>();
			s.Mannequin = m;
			s.FormId = form;
			return s;
		}
		var a = Make( "Saber Duel A", mid - side * gap, side, attacker, crystalA );
		var b = Make( "Saber Duel B", mid + side * gap, -side, defender, crystalB );
		a.Opponent = b; a.Attacker = true;
		b.Opponent = a;
		Log.Info( $"[SWRP] saber duel: {attacker} attacks, {defender} guards, at {mid.x:0} {mid.y:0} {mid.z:0}" );
	}

	[ConCmd( "swrp_saber_test", Help = "swrp_saber_test [crystal 0-6] [blade 0 single 1 dual 2 staff] [form id] [emitter] [finish] - local Jedi test stand putting a saber through every move; swrp_saber_test -1 removes it" )]
	public static void TestCommand( int crystal = 0, int mode = 0, string form = "shiicho", int emitter = 0, int finish = 0 )
	{
		// a Jedi stand switches its mannequin off (it poses the body itself), and Spawn only clears enabled mannequins,
		// so clear the old stands here or every call leaves one behind
		if ( Game.ActiveScene is { } scene )
			foreach ( var old in scene.GetAllComponents<JediStand>().Where( s => s.GameObject.Name == "Anim Test" ).ToList() )
				old.GameObject.Destroy();
		SwrpAnimMannequin.Spawn( crystal < 0 ? -1 : 0, 0, 0, 0, 90, "jedi" );
		if ( crystal < 0 ) return;
		var m = Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>().FirstOrDefault();
		if ( m is null ) return;
		// at a player's size (the map's players are scaled up, and so are the NPCs it is tested on), so reach is honest
		m.WorldScale = SwrpPlayer.PlayerScaleFor( m.Scene );
		var look = new Appearance { SaberCrystal = crystal, SaberMode = mode, SaberEmitter = emitter, SaberFinish = finish };
		m.Faction = "jedi";
		m.Appearance = look.ToString();
		var stand = m.Components.Create<JediStand>();
		stand.Mannequin = m;
		stand.Demo = true;
		stand.FormId = form;
		m.Enabled = false;
	}
}

/// <summary> Hands Jedi mannequins to <see cref="JediStand"/> every frame. </summary>
public sealed class JediStandSystem : GameObjectSystem
{
	public JediStandSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, () => JediStand.Tick( Scene ), "JediStands" );
	}
}
