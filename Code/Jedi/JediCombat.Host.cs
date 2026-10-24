using System;

namespace Swrp.Jedi;

/// <summary> What a saber blow met, for the effects every machine draws. </summary>
public enum SaberHit { Flesh = 0, Droid = 1, Block = 2, Parry = 3, Clash = 4, GuardBreak = 5, LockStart = 6, LockWon = 7, Catch = 8, Object = 9, DroidDown = 10 }

public sealed partial class JediCombat
{
	// ----- host state -----

	readonly SaberMotion _host = new();
	readonly HashSet<GameObject> _hits = new();
	/// <summary> Host: the blades as they are this frame (other Jedi read them for clashes). </summary>
	public List<(Vector3 Base, Vector3 Tip)> HostBlades { get; private set; }
	RealTimeSince _blockStart, _sinceAction, _sinceForce, _lockStart, _sinceSave;
	RealTimeUntil _riposte;
	bool _swingRiposte;
	float _swingDamage = 1f;
	float _throwT;
	Vector3 _prevThrowPos;
	readonly HashSet<GameObject> _throwHits = new();
	JediRecord _record;
	bool _dirty;
	string _loadedFor;

	bool IsCallerOwner() => Rpc.Caller is { } caller && caller.Id == Network.OwnerId;

	void HostStart() => EnsureLoaded();

	/// <summary> Host: load this character's training (once per character; a staff faction change makes a Jedi later). </summary>
	void EnsureLoaded()
	{
		var p = Player;
		if ( p?.CharacterId is null || !IsJediOrder || _loadedFor == p.CharacterId ) return;
		HostSaveNow();
		_loadedFor = p.CharacterId;
		_record = JediProgress.Load( p.OwnerSteamId, p.CharacterId );
		FormId = SaberForm.All.Any( f => f.Id == _record.Form ) ? _record.Form : "shiicho";
		SelectedPower = Math.Max( 0, Array.IndexOf( JediForce.Ids, _record.Selected ) );
		PublishRecord();
		Focus = MaxFocus;
		ForcePoints = MaxForce;
	}

	void PublishRecord()
	{
		if ( _record is null ) return;
		Level = _record.Level;
		Xp = _record.Xp;
		SkillPoints = _record.Points;
		SkillList = string.Join( ",", _record.Skills );
		MaxFocus = 100f + ( _record.Skills.Contains( "tech_focus" ) ? 25f : 0f );
		MaxForce = 100f + ( _record.Skills.Contains( "force_pool" ) ? 30f : 0f );
	}

	void HostSaveNow()
	{
		if ( _record is null || Player?.CharacterId is null ) return;
		_dirty = false;
		_sinceSave = 0;
		JediProgress.Save( Player.OwnerSteamId, _loadedFor ?? Player.CharacterId );
	}

	/// <summary> Host: experience for something done in a fight. </summary>
	public void Award( int xp )
	{
		if ( _record is null || xp <= 0 ) return;
		var levels = JediProgress.AddXp( _record, xp );
		_dirty = true;
		PublishRecord();
		if ( levels > 0 )
		{
			HostSaveNow();
			ReceiveNote( $"Your training deepens: level {_record.Level}. Skill points to spend: {_record.Points} (P).", true );
		}
	}

	void HostUpdate()
	{
		if ( !IsJedi )
		{
			if ( SaberOn ) SaberOn = false;
			if ( Blocking ) Blocking = false;
			// (the other side can't resolve a lock with someone who no longer holds a saber: free them both)
			if ( LockWith != Guid.Empty ) EndLock();
			return;
		}
		EnsureLoaded();
		var dt = Time.Delta;
		_host.Form = Form;
		_host.Design = Design;
		_host.Advance( dt );
		if ( _host.Kind == MotionKind.Block && !Blocking ) _host.Block( false );
		HostTryBlock();

		if ( SaberOn && !Thrown && !HasSaber ) SaberOn = false;
		if ( !Alive )
		{
			if ( SaberOn ) SaberOn = false;
			Blocking = false;
			if ( LockWith != Guid.Empty ) EndLock();
		}

		// focus comes back when you stop fighting (slowly while guarding); the Force after a pause
		var stats = Design.Stats;
		if ( Blocking ) Focus = MathF.Min( MaxFocus, Focus + 3f * stats.FocusRegen * dt );
		else if ( _sinceAction > 1.1f ) Focus = MathF.Min( MaxFocus, Focus + 15f * stats.FocusRegen * dt );
		if ( _sinceForce > 1.6f ) ForcePoints = MathF.Min( MaxForce, ForcePoints + ( Knows( "force_pool" ) ? 8f : 6f ) * dt );

		if ( SaberOn && !Thrown ) HostSweep();
		else HostBlades = null;
		HostLockTick();
		HostThrowTick( dt );
		HostForceTick( dt );

		if ( _dirty && _sinceSave > 20f ) HostSaveNow();
	}

	// ----- requests -----

	/// <summary> Owner: light or put away the saber (SwrpHands: quick slots, the wheel, H). </summary>
	public void OwnerSetSaber( bool on )
	{
		if ( !IsProxy && SaberOn != on && !( on && Thrown ) ) RequestIgnite( on );
	}

	[Rpc.Host]
	void RequestIgnite( bool on )
	{
		if ( !IsCallerOwner() || !IsJedi ) return;
		if ( on && ( !Alive || Thrown ) ) return;
		if ( on && !HasSaber ) { ReceiveNote( "You don't have your lightsaber.", false ); return; }
		if ( SaberOn == on ) return;
		SaberOn = on;
		if ( !on )
		{
			Blocking = false;
			if ( LockWith != Guid.Empty ) EndLock();
		}
		_host.Guard();
	}

	/// <summary> Host: the lightsaber item was used from the inventory: light it or put it away. </summary>
	public void HostToggleSaber()
	{
		if ( !IsJedi || Thrown ) return;
		if ( !SaberOn && ( !Alive || !HasSaber ) ) return;
		SaberOn = !SaberOn;
		if ( !SaberOn ) { Blocking = false; if ( LockWith != Guid.Empty ) EndLock(); }
		_host.Guard();
	}

	[Rpc.Host]
	void RequestForm( string id )
	{
		if ( !IsCallerOwner() || !IsJedi || id is null || !Knows( "form_" + id ) || !SaberForm.All.Any( f => f.Id == id ) ) return;
		FormId = id;
		if ( _record is not null ) { _record.Form = id; _dirty = true; }
	}

	bool _wantBlock;

	[Rpc.Host]
	void RequestBlock( bool on )
	{
		if ( !IsCallerOwner() || !IsJedi ) return;
		_wantBlock = on;
		if ( !on )
		{
			if ( Blocking ) _sinceGuardDown = 0;
			Blocking = false;
			_host.Block( false );
		}
		else HostTryBlock();
	}

	/// <summary> Host: raise the guard if the saber is free to (not mid-cut, staggered, thrown or locked). </summary>
	RealTimeSince _sinceGuardDown = 10f;

	void HostTryBlock()
	{
		if ( !_wantBlock || Blocking ) return;
		if ( !SaberOn || Thrown || LockWith != Guid.Empty || _host.Kind == MotionKind.React || !Alive || !_host.CanCancel ) return;
		// a guard snapped back up straight after dropping it gets no parry window: tapping the button can't make every block perfect
		_blockStart = _sinceGuardDown < 0.45f ? 10f : 0f;
		Blocking = true;
		_host.Block( true );
	}

	[Rpc.Host]
	void RequestSwing( int serial, int mouseDir, bool heavy, int combo )
	{
		if ( !IsCallerOwner() || !IsJedi || !SaberOn || Thrown || !Alive || LockWith != Guid.Empty ) return;
		if ( _host.Kind == MotionKind.React || !_host.CanCancel ) return;
		var f = Form;
		var cost = f.FocusCost * ( heavy ? 1.8f : 1f );
		if ( Focus < cost * 0.5f ) return;

		// the chain: the next step only when this swing follows the last one in time. Judged by time since the last swing
		// started (its length, the chain window, a held heavy and some lag), because the owner decides at the press and a
		// heavy is only sent on release; an unbroken chain that the host doesn't accept is replayed from the host's version
		var count = Math.Max( 1, f.Combo.Count );
		var asked = combo;
		var inChain = !_chainBroken && _sinceSwingStart <= _lastSwingLength + f.ChainWindow + MaxCharge + 0.25f;
		combo = inChain && combo == ( _host.Combo + 1 ) % count ? combo : 0;
		var mouse = (SwingDir)Math.Clamp( mouseDir, 1, 6 );
		var step = f.Step( combo );
		var dir = step.Direction == SwingDir.Any ? mouse : step.Direction;
		var (speed, arc) = SwingParams( combo, heavy );

		Focus = MathF.Max( 0f, Focus - cost );
		_sinceAction = 0;
		Blocking = false;
		_host.Swing( dir, arc, speed, heavy, combo );
		_sinceSwingStart = 0;
		_lastSwingLength = _host.SwingTiming.Total;
		_chainBroken = false;
		_hits.Clear();
		_swingDamage = step.Damage;
		_swingRiposte = _riposte > 0f;
		_riposte = 0;

		SwingDirection = (int)dir;
		SwingCombo = combo;
		SwingHeavy = heavy;
		SwingSpeed = speed;
		SwingArc = arc;
		// a different step from the one the owner predicted: a serial it didn't make, so it plays the host's swing
		SwingSerial = Math.Max( serial, SwingSerial + 1 ) + ( combo == asked ? 0 : 1 );
	}

	RealTimeSince _sinceSwingStart = 99f;
	float _lastSwingLength;
	bool _chainBroken;

	[Rpc.Host]
	void RequestSelect( int power )
	{
		if ( !IsCallerOwner() || JediForce.Get( power ) is not { } def || !Knows( def.Skill ) ) return;
		SelectedPower = power;
		if ( _record is not null ) { _record.Selected = JediForce.Ids[power]; _dirty = true; }
	}

	[Rpc.Host]
	void RequestUnlock( string id )
	{
		if ( !IsCallerOwner() || !IsJediOrder || _record is null || JediProgress.Skill( id ) is not { } skill ) return;
		if ( id == "force_lightning" && !LightningAllowed ) { ReceiveNote( "The Council forbids that path here.", false ); return; }
		if ( !JediProgress.CanLearn( _record, skill, out var why ) ) { ReceiveNote( why, false ); return; }
		_record.Points -= skill.Cost;
		_record.Skills.Add( id );
		PublishRecord();
		HostSaveNow();
		ReceiveNote( $"Learned {skill.Title}.", true );
	}

	/// <summary>
	/// Host, from a Game Master's /gm jedi max (the GM menu's button): their own deployed Jedi gets the whole training at once:
	/// the top level, every form, technique and power (Lightning still only where swrp_jedi_lightning allows it), and full
	/// focus and Force, so every ability can be tested. Saved like any training. Returns true when it was done.
	/// </summary>
	public static bool GmMaxTraining( Connection caller, SwrpPlayer me )
	{
		var j = me?.GetComponent<JediCombat>();
		if ( j is null || !j.IsJediOrder )
		{
			SwrpChat.SystemTo( caller, "Deploy a Jedi Order character first: training belongs to Jedi." );
			return false;
		}
		j.EnsureLoaded();
		if ( j._record is null ) return false;
		j._record.Level = JediProgress.MaxLevel;
		j._record.Xp = 0;
		j._record.Points = 0;
		j._record.Skills = JediProgress.Skills.Select( s => s.Id ).ToList();
		j.PublishRecord();
		j.Focus = j.MaxFocus;
		j.ForcePoints = j.MaxForce;
		j._hostCooldowns.Clear();
		j.HostSaveNow();
		j.ReceiveNote( $"Training maxed for testing: level {JediProgress.MaxLevel}, every form and power ({( LightningAllowed ? "Lightning allowed" : "Lightning learned but off: swrp_jedi_lightning 1 to allow" )}). R cycles forms, Z picks powers.", true );
		return true;
	}

	/// <summary> Owner: spend skill points (the training panel). </summary>
	public void Learn( string id ) => RequestUnlock( id );

	/// <summary> Host: a recoil, played everywhere. </summary>
	void React( ReactKind kind, float seconds )
	{
		ReactType = (int)kind;
		ReactSeconds = seconds;
		ReactSerial++;
		_host.React( kind, seconds );
		_hits.Clear();
		_chainBroken = true;
		if ( kind is ReactKind.GuardBreak or ReactKind.LockLost or ReactKind.Knocked ) Blocking = false;
	}

	/// <summary> Facing someone at a point: within 80 degrees of the view. </summary>
	bool Faces( Vector3 point )
	{
		var to = ( point - WorldPosition ).WithZ( 0 );
		if ( to.LengthSquared < 1f ) return true;
		return Vector3.Dot( Rotation.FromYaw( FacingYaw ).Forward, to.Normal ) > 0.17f;
	}

	// ----- the blade's sweep -----

	readonly SaberSweep.State _sweep = new();

	void HostSweep()
	{
		var s = _host.Sample( AimPitch, Time.Now );
		HostBlades = BladesFor( s );
		var scale = Scale;
		SaberSweep.Step( _host, _sweep, AimPitch, x => BladesFor( x ), ( i, a0, b0, a1, b1, dt ) =>
		{
			var tipSpeed = b0.Distance( b1 ) / MathF.Max( dt, 0.001f );

			// blade on blade first: another Jedi's saber in the way
			foreach ( var other in Scene.GetAllComponents<JediCombat>() )
			{
				if ( other == this || !other.IsJedi || !other.SaberOn || other.Thrown || other.HostBlades is null || _hits.Contains( other.GameObject ) ) continue;
				if ( other.WorldPosition.DistanceSquared( WorldPosition ) > 260f * 260f * scale * scale ) continue;
				foreach ( var (ob, ot) in other.HostBlades )
				{
					var (p, q, dist) = SegmentClosest( a1, b1, ob, ot );
					// the swept edge too, so fast blades don't pass through each other between slices
					if ( dist > 3.2f * scale ) ( p, q, dist ) = SegmentClosest( b0, b1, ob, ot );
					if ( dist > 3.2f * scale ) continue;
					_hits.Add( other.GameObject );
					BladeContact( other, ( p + q ) * 0.5f );
					return true;
				}
			}

			// everything else the edge passes through (SaberSweep)
			var motion = ( b1 - b0 ).LengthSquared > 0.01f ? ( b1 - b0 ).Normal : ( b1 - a1 ).Normal;
			foreach ( var (from, to) in SaberSweep.Segments( a0, b0, a1, b1, scale ) )
				foreach ( var hit in Scene.Trace.Ray( from, to ).Radius( 1.6f * scale ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll" ).UseHitboxes().RunAll() )
					if ( BodyHit( hit, tipSpeed, motion ) ) return true;
			return false;
		} );
	}

	/// <summary> Host: the edge passed through something. Returns true to stop sweeping this frame (a block or a lock). </summary>
	bool BodyHit( SceneTraceResult tr, float tipSpeed, Vector3 motion )
	{
		var go = tr.GameObject;
		if ( go is null ) return false;
		var vitals = go.Components.GetInAncestorsOrSelf<SwrpVitals>();
		var npc = vitals is null ? go.Components.GetInAncestorsOrSelf<SwrpNpc>() : null;
		var target = go.Components.GetInAncestorsOrSelf<SwrpTrainingTarget>();
		var root = vitals?.GameObject ?? npc?.GameObject ?? target?.GameObject;
		if ( root is null || root == GameObject || _hits.Contains( root ) ) return false;
		_hits.Add( root );

		// a Jedi facing the blow with their own blade lit can stop it
		if ( vitals?.GetComponent<JediCombat>() is { } other && other.IsJedi && other.SaberOn && !other.Thrown && other.Alive && other.Faces( WorldPosition ) && other.Blocking )
		{
			if ( ResolveBlock( other, tr.HitPosition ) ) return true;
		}

		if ( target is not null ) { target.HostHit( Player ); FxHit( tr.HitPosition, motion, (int)SaberHit.Object, Guid.Empty ); return false; }

		var heavy = _host.Heavy;
		var speedFactor = Math.Clamp( tipSpeed / ( 700f * Scale ), 0.65f, 1.3f );
		var dmg = ( heavy ? 58f : 34f ) * Form.Damage * _swingDamage * Design.Stats.Damage * speedFactor;
		if ( heavy && Knows( "tech_heavy" ) ) dmg *= 1.15f;
		if ( _swingRiposte ) dmg *= Form.Riposte * ( Knows( "tech_riposte" ) ? 1.25f : 1f );

		if ( vitals is not null )
		{
			if ( vitals.IsDead ) return false;
			var victim = vitals.GetComponent<SwrpPlayer>();
			if ( victim.IsValid() && !SwrpCombat.FriendlyFire && SwrpCombat.SameSide( Player, victim ) )
			{
				// sparring between Jedi: it stings, but nobody is cut down in training
				if ( !( Sparring && vitals.GetComponent<JediCombat>() is { IsJedi: true } ) ) return false;
				dmg = MathF.Min( dmg, MathF.Max( 0f, vitals.Health - 15f ) );
			}
			var head = victim.IsValid() && SwrpCombat.Zone( victim, tr ).Head;
			var killed = dmg > 0f && vitals.TakeDamage( dmg, Player, WorldPosition, "Lightsaber", head );
			Award( (int)( dmg * 0.5f ) + ( killed ? 100 : 0 ) );
			HitLanded( tr.HitPosition, motion, SaberHit.Flesh, root );
			return false;
		}
		if ( npc is not null )
		{
			if ( npc.IsDead ) return false;
			if ( !SwrpCombat.FriendlyFire && AllegianceRegistry.AreFriendly( Player.Allegiance, npc.Allegiance ) ) return false;
			var head = tr.HitPosition.z > npc.Eye.z - 8f;
			var killed = npc.TakeHit( dmg, Player, null, WorldPosition, "Lightsaber", head, false, 0f );
			Award( (int)( dmg * 0.4f ) + ( killed ? 40 : 0 ) );
			var droid = npc.Def.Style is NpcStyle.Roller or NpcStyle.Advancer || npc.Allegiance == AllegianceRegistry.Cis;
			HitLanded( tr.HitPosition, motion, killed && droid ? SaberHit.DroidDown : droid ? SaberHit.Droid : SaberHit.Flesh, root );
			// a blow that doesn't finish them still rocks them: knocked a step along the cut and staggered out of their aim
			// for a moment (longer for a heavy). Shielded droids are left alone: a stun would drain the shield instead
			if ( !killed && npc.Def.Shield <= 0 )
			{
				NpcStagger( npc, motion, heavy, Scale, Player, WorldPosition );
			}
		}
		return false;
	}

	/// <summary> Host: a saber blow that didn't finish an NPC rocks it: a step along the cut and a moment out of its aim. </summary>
	public static void NpcStagger( SwrpNpc npc, Vector3 motion, bool heavy, float scale, SwrpPlayer by, Vector3 from )
	{
		if ( npc is null || npc.IsDead || npc.Def.Shield > 0 ) return;
		// along the cut, but always away from the one who cut: a blade sweeping back past the body never drags it in
		var away = ( npc.WorldPosition - from ).WithZ( 0 ).Normal;
		var dir = ( motion.WithZ( 0 ).Normal * 0.6f + away * 0.8f ).Normal;
		if ( Vector3.Dot( dir, away ) < 0.3f ) dir = away;
		ShoveNpc( npc, dir * ( heavy ? 16f : 7f ) * scale );
		npc.TakeHit( 0f, by, null, from, "Lightsaber", false, true, heavy ? 0.7f : 0.3f );
	}

	void HitLanded( Vector3 at, Vector3 motion, SaberHit kind, GameObject victim )
	{
		_host.HitStop( HitStopMs / 1000f );
		_sinceAction = 0;
		FxHit( at, motion, (int)kind, victim?.Id ?? Guid.Empty );
	}

	/// <summary> Host: this blade met another Jedi's. Both mid-strike and face to face: a lock. Otherwise they block it. </summary>
	void BladeContact( JediCombat other, Vector3 at )
	{
		var otherCutting = other._host.Kind == MotionKind.Swing && other._host.Sample( other.AimPitch, Time.Now ).HitWindow;
		if ( otherCutting && Faces( other.WorldPosition ) && other.Faces( WorldPosition ) && other.LockWith == Guid.Empty )
		{
			StartLock( other, at );
			return;
		}
		if ( other.Blocking && other.Faces( WorldPosition ) && ResolveBlock( other, at ) ) return;

		// the blades just met: both bounce
		React( ReactKind.Blocked, 0.3f );
		other.React( ReactKind.BlockShake, 0.2f );
		other.Focus = MathF.Max( 0f, other.Focus - 6f );
		FxHit( at, Vector3.Up, (int)SaberHit.Clash, other.GameObject.Id );
	}

	/// <summary> Host: the other Jedi is guarding against this blow: a perfect parry, a block, or a broken guard. </summary>
	bool ResolveBlock( JediCombat other, Vector3 at )
	{
		var parryWindow = other.Form.ParryWindow * other.Design.Stats.Parry;
		if ( other._blockStart <= parryWindow )
		{
			React( ReactKind.Parried, 0.75f );
			other.ParrySerial++;
			other._host.Parry();
			other._riposte = 1.2f;
			other.Focus = MathF.Min( other.MaxFocus, other.Focus + 6f );
			other.Award( 10 );
			other.ReceiveFlash( "PERFECT PARRY", true );
			ReceiveFlash( "PARRIED", false );
			FxHit( at, Vector3.Up, (int)SaberHit.Parry, other.GameObject.Id );
			return true;
		}

		var heavy = _host.Heavy;
		var cost = 14f * ( heavy ? 1.9f : 1f ) * Form.Damage * _swingDamage * other.Form.BlockCost / MathF.Max( 0.3f, other.Design.Stats.Block );
		if ( heavy && Knows( "tech_heavy" ) ) cost *= 1.3f;
		other.Focus -= cost;
		other._sinceAction = 0;
		other.Award( 2 );
		React( ReactKind.Blocked, heavy ? 0.4f : 0.3f );
		if ( other.Focus <= 0f )
		{
			other.Focus = 0f;
			other.React( ReactKind.GuardBreak, 1.3f );
			FxHit( at, Vector3.Up, (int)SaberHit.GuardBreak, other.GameObject.Id );
		}
		else
		{
			other.React( ReactKind.BlockShake, 0.18f );
			FxHit( at, Vector3.Up, (int)SaberHit.Block, other.GameObject.Id );
		}
		return true;
	}

	// ----- blade locks -----

	void StartLock( JediCombat other, Vector3 at )
	{
		foreach ( var j in new[] { this, other } )
		{
			j.Blocking = false;
			j._lockStart = 0;
			j.LockMeter = 0f;
			j._host.Lock( true );
		}
		LockWith = other.GameObject.Id;
		other.LockWith = GameObject.Id;
		FxHit( at, Vector3.Up, (int)SaberHit.LockStart, other.GameObject.Id );
	}

	[Rpc.Host]
	void RequestMash()
	{
		if ( !IsCallerOwner() || LockWith == Guid.Empty || _sinceMashHost < 0.05f ) return;
		_sinceMashHost = 0;
		var other = Scene.Directory.FindByGuid( LockWith )?.Components.Get<JediCombat>();
		if ( other is null ) return;
		var push = 0.075f * Form.LockStrength * ( 0.6f + 0.4f * Focus / MathF.Max( 1f, MaxFocus ) );
		LockMeter = Math.Clamp( LockMeter + push, -1f, 1f );
		other.LockMeter = -LockMeter;
	}
	RealTimeSince _sinceMashHost;

	void HostLockTick()
	{
		if ( LockWith == Guid.Empty ) return;
		var other = Scene.Directory.FindByGuid( LockWith )?.Components.Get<JediCombat>();
		if ( other is null || other.LockWith != GameObject.Id || !other.Alive || !Alive || !SaberOn || !other.SaberOn )
		{
			EndLock();
			return;
		}
		// one side runs the lock (the lower id), so it isn't resolved twice
		// (the other side takes over if that one ever stops ticking, so nobody is left stuck in a lock)
		if ( GameObject.Id.CompareTo( other.GameObject.Id ) > 0 && _lockStart < 4.5f ) return;

		var dt = Time.Delta;
		Focus = MathF.Max( 0f, Focus - 4f * dt );
		other.Focus = MathF.Max( 0f, other.Focus - 4f * dt );
		// the steadier mind gains a little ground on its own
		LockMeter = Math.Clamp( LockMeter + ( Focus - other.Focus ) / 100f * 0.08f * dt, -1f, 1f );
		other.LockMeter = -LockMeter;
		if ( _lockStart < 3.2f && MathF.Abs( LockMeter ) < 1f ) return;

		var won = LockMeter > 0f || LockMeter == 0f && Focus >= other.Focus;
		var winner = won ? this : other;
		var loser = won ? other : this;
		EndLock();
		loser.React( ReactKind.LockLost, 1.1f );
		var away = ( loser.WorldPosition - winner.WorldPosition ).WithZ( 0 ).Normal;
		loser.ReceiveKnock( away * 420f + Vector3.Up * 140f, 0.25f );
		winner._riposte = 1.0f;
		winner.Award( 15 );
		FxHit( ( winner.WorldPosition + loser.WorldPosition ) * 0.5f + Vector3.Up * 50f * Scale, away, (int)SaberHit.LockWon, loser.GameObject.Id );
	}

	void EndLock()
	{
		var other = LockWith == Guid.Empty ? null : Scene.Directory.FindByGuid( LockWith )?.Components.Get<JediCombat>();
		LockWith = Guid.Empty;
		LockMeter = 0f;
		_host.Lock( false );
		if ( other is not null && other.LockWith == GameObject.Id )
		{
			other.LockWith = Guid.Empty;
			other.LockMeter = 0f;
			other._host.Lock( false );
		}
	}

	// ----- the saber throw -----

	[Rpc.Host]
	void RequestThrow( Vector3 target )
	{
		if ( !IsCallerOwner() || !IsJedi || !SaberOn || Thrown || !Alive || !Knows( "tech_throw" ) || LockWith != Guid.Empty || Focus < 10f ) return;
		var from = CatchPoint;
		var range = 650f * Scale;
		var dir = ( target - from );
		if ( dir.Length > range ) target = from + dir.Normal * range;
		var tr = Scene.Trace.Ray( from, target ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player", "npc", "ragdoll" ).Run();
		if ( tr.Hit ) target = tr.HitPosition - ( target - from ).Normal * 8f;
		var dist = from.Distance( target );
		ThrowFrom = from;
		ThrowTo = target;
		ThrowSide = Game.Random.Float() < 0.5f ? -0.6f : 0.6f;
		ThrowOut = Math.Clamp( dist / 1000f, 0.3f, 0.75f );
		Thrown = true;
		ThrowSerial++;
		Blocking = false;
		Focus -= 10f;
		_sinceAction = 0;
		_throwT = 0f;
		_prevThrowPos = from;
		_throwHits.Clear();
		_host.Throw( true );
	}

	[Rpc.Host]
	void RequestRecall()
	{
		if ( !IsCallerOwner() || !Thrown || _throwT >= ThrowOut ) return;
		var (pos, _, _) = ThrowPath( _throwT, CatchPoint );
		ThrowTo = pos;
		ThrowOut = MathF.Max( 0.1f, _throwT );
	}

	void HostThrowTick( float dt )
	{
		if ( !Thrown ) return;
		_throwT += dt;
		var (pos, returning, done) = ThrowPath( _throwT, CatchPoint );
		var radius = Design.BladeLength * 0.55f * Scale;
		foreach ( var tr in Scene.Trace.Ray( _prevThrowPos, pos ).Radius( radius ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll" ).UseHitboxes().RunAll() )
		{
			var go = tr.GameObject;
			var vitals = go?.Components.GetInAncestorsOrSelf<SwrpVitals>();
			var npc = vitals is null ? go?.Components.GetInAncestorsOrSelf<SwrpNpc>() : null;
			var root = vitals?.GameObject ?? npc?.GameObject;
			if ( root is null )
			{
				// a wall on the way out: it turns back from there
				if ( !returning && tr.Hit && tr.GameObject is not null && !tr.GameObject.Tags.Has( "player" ) )
				{
					ThrowTo = pos;
					ThrowOut = MathF.Max( 0.1f, _throwT );
				}
				continue;
			}
			if ( _throwHits.Contains( root ) ) continue;
			_throwHits.Add( root );
			// a guarding Jedi bats it away
			if ( vitals?.GetComponent<JediCombat>() is { IsJedi: true, SaberOn: true, Blocking: true } other && other.Faces( pos ) )
			{
				other.Focus -= 10f;
				FxHit( tr.HitPosition, Vector3.Up, (int)SaberHit.Block, other.GameObject.Id );
				if ( !returning ) { ThrowTo = pos; ThrowOut = MathF.Max( 0.1f, _throwT ); }
				continue;
			}
			var dmg = 38f * Form.Damage * Design.Stats.Damage;
			if ( vitals is not null && !vitals.IsDead )
			{
				var victim = vitals.GetComponent<SwrpPlayer>();
				if ( victim.IsValid() && !SwrpCombat.FriendlyFire && SwrpCombat.SameSide( Player, victim ) ) continue;
				var killed = vitals.TakeDamage( dmg, Player, ThrowFrom, "Thrown lightsaber", false );
				Award( 20 + ( killed ? 100 : 0 ) );
				FxHit( tr.HitPosition, ( pos - _prevThrowPos ).Normal, (int)SaberHit.Flesh, root.Id );
			}
			else if ( npc is not null && !npc.IsDead )
			{
				if ( !SwrpCombat.FriendlyFire && AllegianceRegistry.AreFriendly( Player.Allegiance, npc.Allegiance ) ) continue;
				var killed = npc.TakeHit( dmg, Player, null, ThrowFrom, "Thrown lightsaber", false, false, 0f );
				Award( 15 + ( killed ? 40 : 0 ) );
				FxHit( tr.HitPosition, ( pos - _prevThrowPos ).Normal, (int)SaberHit.Droid, root.Id );
			}
		}
		_prevThrowPos = pos;
		if ( !done ) return;
		Thrown = false;
		_host.Throw( false );
		if ( !Alive ) SaberOn = false;
		FxHit( CatchPoint, Vector3.Up, (int)SaberHit.Catch, GameObject.Id );
	}

	// ----- blaster bolts -----

	/// <summary>
	/// Host, from a blaster or NPC shot: if the trace hit a Jedi who is guarding (or has learned Deflection, and isn't
	/// attacking) and faces the shot, the bolt is turned on their blade and sent back - at the shooter for a good parry or
	/// a skilled form, otherwise somewhere ahead of where they look. Returns true when the bolt was deflected (the caller
	/// applies no damage).
	/// </summary>
	public static bool TryDeflect( SceneTraceResult tr, GameObject shooter, Vector3 shotStart, float damage, string weapon, Color color )
	{
		if ( !Networking.IsHost || !tr.Hit ) return false;
		var j = tr.GameObject?.Components.GetInAncestorsOrSelf<JediCombat>();
		if ( j is null || !j.IsJedi || !j.SaberOn || j.Thrown || !j.Alive || j.LockWith != Guid.Empty ) return false;
		return j.HostDeflect( shooter, shotStart, damage, weapon, color );
	}

	bool HostDeflect( GameObject shooter, Vector3 shotStart, float damage, string weapon, Color color )
	{
		if ( !Faces( shotStart ) ) return false;
		var guarding = Blocking;
		if ( !guarding )
		{
			var free = _host.Kind is MotionKind.Guard or MotionKind.Block;
			if ( !free || !Knows( "tech_deflect" ) || Game.Random.Float() > 0.4f ) return false;
		}
		// every bolt turned costs focus; with none left the bolt gets through (and a guard under fire breaks)
		var cost = guarding ? 2.5f * Form.BlockCost : 5f;
		if ( Focus < cost )
		{
			if ( guarding ) { Focus = 0f; React( ReactKind.GuardBreak, 1.0f ); }
			return false;
		}
		var stats = Design.Stats;
		var perfect = guarding && _blockStart <= Form.ParryWindow * 1.6f * stats.Parry;
		var accuracy = Math.Clamp( Form.DeflectSkill * stats.Deflect * ( Knows( "tech_redirect" ) ? 1.4f : 1f ) + ( perfect ? 0.6f : 0f ), 0f, 1f );
		var scale = Scale;
		var facing = Rotation.FromYaw( FacingYaw );
		var blade = WorldPosition + facing * ( SaberMotion.Chest + new Vector3( 16f, 0f, 4f ) ) * scale;

		Vector3 dir;
		if ( shooter.IsValid() && Game.Random.Float() < accuracy )
		{
			var at = shooter.Components.Get<SwrpNpc>() is { } n ? n.Eye + Vector3.Down * 14f : shooter.WorldPosition + Vector3.Up * 44f * scale;
			dir = ( at - blade ).Normal;
		}
		else
		{
			var aim = Controller.IsValid() ? Controller.EyeAngles.ToRotation() : facing;
			var spread = ( 1f - accuracy ) * 28f + 4f;
			dir = ( aim * Rotation.From( Game.Random.Float( -spread, spread ) * 0.6f, Game.Random.Float( -spread, spread ), 0 ) ).Forward;
		}

		var tr = Scene.Trace.Ray( blade, blade + dir * 3200f ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ragdoll" ).UseHitboxes().Run();
		var dmg = damage * ( perfect ? 1f : 0.8f );
		var hitSomeone = false;
		if ( tr.Hit && tr.GameObject is { } hit )
		{
			if ( hit.Components.GetInAncestorsOrSelf<SwrpVitals>() is { IsDead: false } v && v.GameObject != GameObject )
			{
				var victim = v.GetComponent<SwrpPlayer>();
				if ( !( victim.IsValid() && !SwrpCombat.FriendlyFire && SwrpCombat.SameSide( Player, victim ) ) )
				{
					var (mult, head) = SwrpCombat.Zone( victim, tr );
					if ( v.TakeDamage( dmg * mult, Player, blade, "Deflected bolt", head ) ) Award( 60 );
					hitSomeone = true;
				}
			}
			else if ( hit.Components.GetInAncestorsOrSelf<SwrpNpc>() is { IsDead: false } npc )
			{
				if ( SwrpCombat.FriendlyFire || !AllegianceRegistry.AreFriendly( Player.Allegiance, npc.Allegiance ) )
				{
					if ( npc.TakeHit( dmg, Player, null, blade, "Deflected bolt", false, false, 0f ) ) Award( 25 );
					hitSomeone = true;
				}
			}
		}

		Focus = MathF.Max( 0f, Focus - cost );
		_sinceAction = 0;
		Award( perfect ? 6 : 3 );
		if ( perfect ) { ParrySerial++; _host.Parry(); }
		FxDeflect( blade, tr.EndPosition, tr.Hit ? tr.Normal : -dir, tr.Hit, !hitSomeone, color );
		return true;
	}

	// ----- effects, on every machine -----

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void FxHit( Vector3 at, Vector3 motion, int kind, Guid victim )
	{
		var k = (SaberHit)kind;
		var color = Design.BladeColor;
		var victimGo = victim == Guid.Empty ? null : Scene.Directory.FindByGuid( victim );
		SaberFx.Hit( Scene, k, at, motion, color, victimGo );

		// hit-stop and a jolt for whoever is in it
		var stop = HitStopMs / 1000f;
		if ( k is SaberHit.Flesh or SaberHit.Droid or SaberHit.DroidDown or SaberHit.Block or SaberHit.Parry or SaberHit.Clash or SaberHit.Object or SaberHit.LockStart or SaberHit.GuardBreak )
		{
			_rig?.Flare();
			victimGo?.Components.Get<JediCombat>()?._rig?.Flare();
			Motion.HitStop( stop );
			victimGo?.Components.Get<JediCombat>()?.Motion.HitStop( stop * 0.8f );
		}
		var local = SwrpPlayer.Local?.GameObject;
		if ( local is not null && ( local == GameObject || local == victimGo ) )
		{
			var heavy = k is SaberHit.GuardBreak or SaberHit.LockWon or SaberHit.Parry;
			JediCamera.Punch( heavy ? 0.5f : k is SaberHit.Flesh or SaberHit.Droid or SaberHit.DroidDown ? 0.35f : 0.25f, heavy ? 3.5f : 2f );
		}
		// a body jolt on whoever was cut: a Jedi's own rig takes it, anyone else (troopers, droids) gets a quick recoil of the spine
		if ( k is SaberHit.Flesh or SaberHit.Droid ) FxFlinch.Hit( victimGo, at, motion );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void FxDeflect( Vector3 from, Vector3 to, Vector3 normal, bool hit, bool hitWorld, Color color )
	{
		SaberFx.Deflect( Scene, from, to, normal, hit, hitWorld, color, Design.BladeColor );
		// the blade snaps to meet it and flares
		Motion.Parry();
		_rig?.Flare();
		if ( SwrpPlayer.Local?.GameObject == GameObject ) JediCamera.Punch( 0.15f, 1f );
	}
}
