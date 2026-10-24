using System;

namespace Swrp.Jedi;

/// <summary> Local UI switches for the Jedi panels (the Force wheel and the training panel). </summary>
public static class JediUi
{
	public static bool WheelOpen { get; set; }
	/// <summary> The wheel's highlighted power (JediForce index), or -1. </summary>
	public static int WheelHover { get; set; } = -1;
	public static bool SkillsOpen { get; set; }

	/// <summary> A word flashed on the HUD (PERFECT PARRY / PARRIED) and when. </summary>
	public static string FlashText { get; set; }
	public static bool FlashGood { get; set; }
	public static RealTimeSince SinceFlash { get; set; } = 99f;

	static readonly Dictionary<string, bool> _actions = new();
	/// <summary> Whether an input action exists (new ones only appear after the editor restarts with the new Input.config). </summary>
	/// <summary> An action pressed, or (until the editor restarts with the new Input.config) its default key read directly. </summary>
	public static bool Pressed( string action, string key ) => Has( action ) ? Input.Pressed( action ) : Input.Keyboard.Pressed( key );
	public static bool Down( string action, string key ) => Has( action ) ? Input.Down( action ) : Input.Keyboard.Down( key );

	public static bool Has( string action )
	{
		if ( _actions.TryGetValue( action, out var has ) ) return has;
		has = Input.ActionNames.Contains( action );
		_actions[action] = has;
		return has;
	}
}

public sealed partial class JediCombat
{
	// ----- owner input -----
	//   quick slot / H    ignite / retract: the saber is quick slot 1 and H puts it away (SwrpHands owns 1-5, the wheel, H, G)
	//   Mouse 1           attack: the way the mouse was moving picks the cut (left, right, up = overhead, down = thrust);
	//                     tap for a light swing, hold for a heavy one (it charges while held); in a blade lock, mash it
	//   Mouse 2           hold to guard; raising it just before a blow lands parries it; deflects blaster bolts
	//   R                 next lightsaber form you know
	//   F                 use the selected Force power (hold for Lightning)
	//   Z (hold)          Force wheel: move the mouse towards a power, let go to pick it
	//   Mouse 3           throw the saber; press again to call it back early
	//   P                 training: level, skills and forms

	bool _ownerBlock, _ownerCharge, _pressing, _chainAtPress;
	SwingDir _chargeDir, _pressDir;
	RealTimeSince _pressAt;
	Vector2 _mouseAcc;
	SwingDir _lastDir = SwingDir.Right;
	int _lastCombo;
	int _predictedCast = -1;
	Vector2 _wheel;
	bool _channeling;
	RealTimeSince _sinceMash;
	readonly Dictionary<int, RealTimeUntil> _cooldowns = new();

	const float HeavyHold = 0.24f;
	const float MaxCharge = 0.9f;

	/// <summary> Seconds left on a Force power's cooldown (owner). </summary>
	public float CooldownLeft( int power ) => _cooldowns.TryGetValue( power, out var u ) ? MathF.Max( 0f, (float)u.Relative ) : 0f;

	void OwnerUpdate()
	{
		var p = Player;
		if ( !Controller.IsValid() || p is null ) return;
		_mouseAcc = _mouseAcc * MathF.Exp( -Time.Delta * 12f ) + Input.MouseDelta;

		ForceCushion();
		JediHud.Ensure( Scene );

		if ( JediUi.Pressed( "JediMenu", "p" ) && !SwrpUiState.ChatOpen )
		{
			if ( IsJediOrder ) JediUi.SkillsOpen = !JediUi.SkillsOpen;
			else SwrpNet.ShowLocal( "Jedi training is for Jedi Order characters.", true );
		}
		if ( JediUi.SkillsOpen && Input.EscapePressed ) { Input.EscapePressed = false; JediUi.SkillsOpen = false; }

		var canAct = !SwrpUiState.AnyOpen && Alive && !p.Jailed && !p.Frozen && !p.InPod;
		if ( !canAct )
		{
			ReleaseHeld();
			return;
		}

		// drawing the blaster puts the saber away (choosing what's in hand, keys and wheel, is SwrpHands')
		var blaster = GetComponent<SwrpBlaster>();
		if ( blaster is { Drawn: true } && SaberOn ) RequestIgnite( false );

		if ( SaberOn && !Thrown && Input.Pressed( "Reload" ) ) NextForm();

		WheelInput();
		ForceInput();

		if ( JediUi.Pressed( "SaberThrow", "mouse3" ) )
		{
			if ( Thrown ) RequestRecall();
			else if ( SaberOn && Knows( "tech_throw" ) ) RequestThrow( AimPoint( 700f * Scale ) );
			else if ( SaberOn ) SwrpNet.ShowLocal( "Learn Saber Throw first (P).", true );
		}

		if ( !SaberOn || Thrown )
		{
			ReleaseHeld();
			return;
		}

		// a blade lock: mash the attack button to drive the other blade back
		if ( LockWith != Guid.Empty )
		{
			ReleaseHeld();
			if ( Input.Pressed( "Attack1" ) && _sinceMash > 0.05f ) { _sinceMash = 0; RequestMash(); }
			return;
		}

		var reacting = Motion.Kind == MotionKind.React;
		var block = Input.Down( "Attack2" ) && !reacting && !_pressing;
		if ( block != _ownerBlock )
		{
			_ownerBlock = block;
			RequestBlock( block );
			if ( block ) Motion.Block( true );
		}

		AttackInput( reacting );
	}

	void ReleaseHeld()
	{
		if ( _ownerBlock ) { _ownerBlock = false; RequestBlock( false ); }
		_pressing = false;
		_ownerCharge = false;
		if ( _channeling ) { _channeling = false; RequestForceStop(); }
		JediUi.WheelOpen = false;
	}

	void AttackInput( bool reacting )
	{
		if ( Input.Pressed( "Attack1" ) && !_ownerBlock && !reacting )
		{
			_pressing = true;
			_pressAt = 0;
			_pressDir = MouseDir();
			_chainAtPress = Motion.InChainWindow( Form.ChainWindow );
		}
		if ( !_pressing ) return;

		if ( !Input.Down( "Attack1" ) || _ownerCharge && _pressAt > MaxCharge )
		{
			_pressing = false;
			var heavy = _ownerCharge;
			_ownerCharge = false;
			TrySwing( _pressDir, heavy );
			return;
		}

		// held: the strike builds into a heavy one
		if ( !_ownerCharge && _pressAt > HeavyHold && Motion.CanCancel && Focus >= Form.FocusCost * 0.9f )
		{
			_ownerCharge = true;
			_chargeDir = StepDir( NextCombo(), _pressDir );
			Motion.Charge( _chargeDir );
		}
	}

	int NextCombo()
	{
		var count = Math.Max( 1, Form.Combo.Count );
		return _chainAtPress ? ( _lastCombo + 1 ) % count : 0;
	}

	SwingDir StepDir( int combo, SwingDir mouse )
	{
		var step = Form.Step( combo );
		return step.Direction == SwingDir.Any ? mouse : step.Direction;
	}

	/// <summary> The swing the mouse asked for: the way it was moving as the button went down. </summary>
	SwingDir MouseDir()
	{
		var m = _mouseAcc;
		if ( m.Length < 3f )
		{
			// no flick: alternate sides, as the blade comes back across
			return _lastDir == SwingDir.Left ? SwingDir.Right : SwingDir.Left;
		}
		if ( MathF.Abs( m.x ) > MathF.Abs( m.y ) * 0.8f ) return m.x < 0 ? SwingDir.Left : SwingDir.Right;
		return m.y < 0 ? SwingDir.Overhead : SwingDir.Thrust;
	}

	void TrySwing( SwingDir mouse, bool heavy )
	{
		var f = Form;
		var cost = f.FocusCost * ( heavy ? 1.8f : 1f );
		var startable = Motion.CanCancel || Motion.Kind == MotionKind.Charge;
		if ( !startable || Motion.Kind == MotionKind.React ) return;
		if ( Focus < cost * 0.5f )
		{
			SaberSounds.Play( SaberSounds.Deny, WorldPosition );
			if ( Motion.Kind == MotionKind.Charge ) Motion.Guard();
			return;
		}
		var combo = NextCombo();
		var dir = StepDir( combo, mouse );
		var (speed, arc) = SwingParams( combo, heavy );
		var serial = Math.Max( SwingSerial, _predictedSwing ) + 1;
		_predictedSwing = serial;
		_lastCombo = combo;
		if ( dir is SwingDir.Left or SwingDir.Right ) _lastDir = dir;
		Motion.Swing( dir, arc, speed, heavy, combo );
		OnSwingStarted( heavy, speed );
		// Ataru's spinning finisher leaves the ground: a short hop through the turn
		if ( dir == SwingDir.Spin && FormId == "ataru" && Controller.IsOnGround && GetComponent<Rigidbody>() is { } rb )
		{
			Controller.PreventGrounding( 0.2f );
			rb.Velocity = rb.Velocity.WithZ( 240f );
		}
		RequestSwing( serial, (int)mouse, heavy, combo );
	}

	/// <summary> A swing's speed and arc from the form's combo step, the saber and the skills (owner and host agree on it). </summary>
	(float Speed, float Arc) SwingParams( int combo, bool heavy )
	{
		var f = Form;
		var step = f.Step( combo );
		var speed = f.Tempo * step.Speed * Design.Stats.Speed * ( heavy ? 0.85f : 1f );
		if ( Knows( "tech_flow" ) ) speed *= 1f + 0.06f * combo;
		return ( speed, step.Arc );
	}

	/// <summary> Every machine as a swing starts: the whoosh. </summary>
	void OnSwingStarted( bool heavy, float speed )
	{
		SaberSounds.Swing( HiltPosition, heavy, speed, Design.CrystalDef.HumPitch );
	}

	void NextForm()
	{
		var forms = SaberForm.All.Where( f => Knows( "form_" + f.Id ) ).ToList();
		if ( forms.Count < 2 ) { SwrpNet.ShowLocal( "You know only one form. Learn more in training (P).", false ); return; }
		var i = forms.FindIndex( f => f.Id == FormId );
		var next = forms[( i + 1 ) % forms.Count];
		RequestForm( next.Id );
		SwrpNet.ShowLocal( $"Form {next.Numeral}: {next.Title}", false );
		SaberSounds.Play( SaberSounds.FormChange, WorldPosition + Vector3.Up * 50f );
	}

	/// <summary> What the view is pointing at, up to a distance. </summary>
	Vector3 AimPoint( float range )
	{
		var eye = Controller.EyeTransform;
		var tr = Scene.Trace.Ray( eye.Position, eye.Position + eye.Rotation.Forward * range ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		return tr.Hit ? tr.HitPosition : tr.EndPosition;
	}

	// ----- the Force wheel and the F key -----

	void WheelInput()
	{
		var down = JediUi.Down( "ForceWheel", "z" );
		if ( down && !JediUi.WheelOpen ) { JediUi.WheelOpen = true; _wheel = Vector2.Zero; }
		if ( !JediUi.WheelOpen ) return;
		if ( down )
		{
			_wheel += Input.MouseDelta;
			if ( _wheel.Length > 120f ) _wheel = _wheel.Normal * 120f;
			JediUi.WheelHover = _wheel.Length > 18f ? JediForce.AtAngle( this, MathF.Atan2( _wheel.x, -_wheel.y ) ) : -1;
			return;
		}
		JediUi.WheelOpen = false;
		if ( JediUi.WheelHover >= 0 && JediUi.WheelHover != SelectedPower )
		{
			RequestSelect( JediUi.WheelHover );
			SaberSounds.Play( SaberSounds.FormChange, WorldPosition + Vector3.Up * 50f );
		}
		JediUi.WheelHover = -1;
	}

	/// <summary> The wheel holds the view still while it's open. </summary>
	void PlayerController.IEvents.PreInput()
	{
		if ( IsProxy || !JediUi.WheelOpen ) return;
		Input.AnalogLook = Angles.Zero;
	}

	string ForceKey => JediUi.Has( "ForceUse" ) ? "ForceUse" : "Flashlight";

	void ForceInput()
	{
		var key = ForceKey;
		if ( _channeling && !Input.Down( key ) )
		{
			_channeling = false;
			RequestForceStop();
		}
		if ( !Input.Pressed( key ) || JediUi.WheelOpen ) return;

		var power = SelectedPower;
		var def = JediForce.Get( power );
		if ( def is null ) return;
		if ( !Knows( def.Skill ) ) { SwrpNet.ShowLocal( $"You haven't learned {def.Title}.", true ); return; }
		if ( power == JediForce.Lightning && !LightningAllowed ) { SwrpNet.ShowLocal( "Force Lightning isn't allowed here.", true ); return; }
		if ( CooldownLeft( power ) > 0f || ForcePoints < def.Cost * Form.ForceCost )
		{
			SaberSounds.Play( SaberSounds.Deny, WorldPosition );
			return;
		}

		var eye = Controller.EyeTransform;
		// a leap goes at once on this machine (the owner moves the body); the host takes the Force points
		if ( power == JediForce.Leap ) JediForce.OwnerLeap( this );
		if ( power == JediForce.Lightning ) _channeling = true;

		_predictedCast = CastSerial + 1;
		Motion.Cast( power, def.CastSeconds );
		RequestForce( power, eye.Position, eye.Rotation.Forward, _predictedCast );
	}

	/// <summary> A Jedi doesn't break their legs: just above the ground the Force takes the worst of a fall. </summary>
	/// <summary>
	/// A Jedi lands softly. After a Force Leap the whole fall is held under the speed that hurts (SwrpVitals measures the
	/// fastest fall and hurts above 650), and the landing puffs dust; any other fall is braked just above the ground.
	/// </summary>
	void ForceCushion()
	{
		if ( !Controller.IsValid() ) return;
		var rb = GetComponent<Rigidbody>();
		if ( rb is null ) return;
		if ( Controller.IsOnGround )
		{
			if ( _leaping && _sinceLeap > 0.15f )
			{
				_leaping = false;
				SaberFx.ForceDust( Scene, WorldPosition, 0.8f );
				var h = SaberSounds.Play( SaberSounds.Leap, WorldPosition );
				if ( h is not null ) { h.Pitch = 0.7f; h.Volume *= 0.6f; }
			}
			return;
		}
		const float Safe = -540f;
		if ( rb.Velocity.z >= Safe ) return;
		if ( !_leaping )
		{
			if ( !IsJediOrder ) return;
			var down = Scene.Trace.Ray( WorldPosition + Vector3.Up * 4f, WorldPosition + Vector3.Down * 320f * Scale ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( !down.Hit ) return;
			if ( _sinceCushionFx > 0.5f ) { _sinceCushionFx = 0; SaberFx.ForceDust( Scene, down.HitPosition, 0.6f ); }
		}
		rb.Velocity = rb.Velocity.WithZ( Safe );
	}

	bool _leaping;
	RealTimeSince _sinceLeap;

	/// <summary> Owner: a Force Leap just went off (soft landing until the feet touch down). </summary>
	internal void StartLeap()
	{
		_leaping = true;
		_sinceLeap = 0;
	}
	RealTimeSince _sinceCushionFx;

	// ----- host to owner -----

	/// <summary> Host to owner: thrown by the Force or a lost blade lock (the owner moves the body). </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void ReceiveKnock( Vector3 velocity, float noGround )
	{
		var rb = GetComponent<Rigidbody>();
		if ( rb is null || !Controller.IsValid() ) return;
		Controller.PreventGrounding( noGround );
		rb.Velocity = rb.Velocity * 0.3f + velocity;
		// thrown by the Force: the view jolts and the edges of the frame widen as you go
		JediCamera.Punch( Math.Clamp( velocity.Length / 900f, 0.2f, 0.7f ), 5f );
	}

	/// <summary> Host to owner: Force Pull tore the blaster from your hands. </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void ReceiveDisarm()
	{
		if ( GetComponent<SwrpBlaster>() is { Drawn: true } b )
		{
			b.Drawn = false;
			SwrpNet.ShowLocal( "The blaster is wrenched from your hands!", true );
		}
	}

	/// <summary> Host to owner: a word on the HUD for a moment (a perfect parry, being parried). </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void ReceiveFlash( string text, bool good )
	{
		JediUi.FlashText = text;
		JediUi.FlashGood = good;
		JediUi.SinceFlash = 0;
	}

	/// <summary> Host to owner: a power's cooldown started. </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void ReceiveCooldown( int power, float seconds ) => _cooldowns[power] = seconds;

	/// <summary> Host to owner: something to tell the Jedi (level up, a skill learned). </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void ReceiveNote( string text, bool good )
	{
		SwrpNet.ShowLocal( text, !good );
		if ( good ) SaberSounds.Play( SaberSounds.LevelUp, WorldPosition + Vector3.Up * 50f );
	}

	[ConCmd( "swrp_jedi_menu", Help = "Open or close the Jedi training panel (also P)" )]
	public static void MenuCommand() => JediUi.SkillsOpen = !JediUi.SkillsOpen;
}
