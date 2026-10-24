using System;

namespace Swrp;

/// <summary>
/// The local trooper's side of the build tools: physgun, tool gun and prop placement input, ported from Facepunch's Sandbox
/// (Physgun.OnControl, ToolMode dispatch, SpawnerWeapon; MIT, see LICENSE-facepunch-sandbox.txt). Added to the local player by
/// <see cref="BuildMenu"/>; does nothing unless a tool is picked in the Q menu. Physgun: hold left mouse to grab, wheel pulls or
/// pushes, E + mouse turns it (E + W/S distance, A/D spin; Shift snaps 45°, Alt 15°), right mouse freezes, R unfreezes; right mouse
/// on a loose prop pulls it in, then left mouse launches it. U undoes your last spawn or constraint.
/// </summary>
public sealed class SwrpBuildTool : Component, PlayerController.IEvents
{
	public static SwrpBuildTool Local { get; private set; }

	enum Hold { None, Beam, Gravity }

	Hold _hold;
	Guid _held;
	Rotation _grabOffset, _spin, _snap;
	float _distance;
	bool _spinning, _snapping, _preventReselect;
	TimeSince _sinceAim, _sincePull;

	// look and move taken from the controller while turning something (PreInput), used in OnUpdate
	Angles _look;
	Vector3 _move;
	float _capturedAt = -1f;
	Angles? _frozenEyes;

	// spawner
	Model _ghost;
	string _ghostFor;
	Rotation _placeOffset = Rotation.Identity, _placeSpin = Rotation.Identity;
	bool _rotating, _placeSnapping;
	bool _placeOk;

	BuildToolMode _mode;

	/// <summary> The physgun viewmodel's state (Sandbox's claws, button and brake). </summary>
	public static bool HoverGrabbable { get; private set; }
	public static bool Pulling { get; private set; }
	public static bool Spinning => Local is { } l && l._spinning;

	/// <summary> The tool gun's crosshair: false when nothing valid is under it. </summary>
	public static bool CrosshairValid { get; private set; } = true;

	/// <summary> The line the HUD shows while a tool is out. </summary>
	public static string Hint => SwrpBuild.Active switch
	{
		SwrpBuild.Tool.Physgun => SwrpBuild.Grabbing
			? "PHYSGUN · wheel pulls/pushes · E + mouse turns (Shift 45°, Alt 15°) · right mouse freezes · R unfreezes all"
			: "PHYSGUN · hold left mouse to grab · right mouse pulls a loose prop in · R unfreezes · U undo · Q menu",
		SwrpBuild.Tool.Toolgun => ToolHint( BuildToolMode.Get( SwrpBuild.ToolMode ) ),
		SwrpBuild.Tool.Spawner => SwrpBuild.SpawnModel is null ? "" :
			$"PLACING {( SwrpBuild.SpawnName ?? "PROP" ).ToUpperInvariant()} · left mouse places · E + mouse turns (Shift 45°) · right mouse puts it away · U undo",
		_ => "",
	};

	static string ToolHint( BuildToolMode m )
	{
		var parts = new List<string> { $"TOOL GUN: {m.Name.ToUpperInvariant()}", m.Description };
		if ( m.Primary is { } p ) parts.Add( $"left mouse {p}" );
		if ( m.Secondary is { } s ) parts.Add( $"right mouse {s}" );
		if ( m.Reload is { } r ) parts.Add( $"R {r}" );
		parts.Add( "U undo" );
		return string.Join( " · ", parts );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		Local = this;

		var me = GetComponent<SwrpPlayer>();

		// F7: look through the next camera (Camera tool), or back to your own eyes
		if ( me.IsValid() && !SwrpUiState.ChatOpen && Input.Pressed( "BuildMenu" ) )
		{
			if ( SwrpBuild.Allowed ) SwrpCameraView.Next();
			else SwrpBuild.RequestAccess();
		}
		if ( SwrpCameraView.Active && ( !SwrpBuild.Allowed || !me.IsValid() || me.CharacterId is null ) ) SwrpCameraView.Viewing = null;
		ViewThirdPerson( me );
		var usable = me.IsValid() && me.Controller.IsValid() && SwrpBuild.Allowed && !SwrpUiState.AnyOpen && !me.Frozen && !me.InVehicle;

		if ( usable && !SwrpUiState.ChatOpen && Input.Pressed( "BuildUndo" ) ) SwrpBuildUndo.Undo();      // U: Z is prone (SwrpProne)

		var tool = usable ? SwrpBuild.Active : SwrpBuild.Tool.None;
		var shown = me.IsValid() && SwrpBuild.Allowed && !me.InVehicle ? SwrpBuild.Active : SwrpBuild.Tool.None;      // stays in hand while a menu is up
		SwrpHeldTools.Tick( shown == SwrpBuild.Tool.Physgun ? 1 : shown is SwrpBuild.Tool.Toolgun or SwrpBuild.Tool.Spawner ? 2 : 0 );
		if ( tool != SwrpBuild.Tool.Physgun && _hold != Hold.None ) EndHold( 0, default );
		if ( tool != SwrpBuild.Tool.Toolgun ) _mode?.Reset();
		if ( tool != SwrpBuild.Tool.Spawner ) _rotating = false;
		CrosshairValid = true;

		// look/move this frame: what PreInput took from the controller, or the raw input if it didn't run (noclip)
		var captured = _capturedAt == Time.Now;
		var look = captured ? _look : Input.AnalogLook;
		var move = captured ? _move : Input.AnalogMove;
		_look = default;
		_move = default;

		if ( tool == SwrpBuild.Tool.None ) { _frozenEyes = null; return; }

		var eye = me.Controller.EyePosition;
		var rot = me.Controller.EyeAngles.ToRotation();

		switch ( tool )
		{
			case SwrpBuild.Tool.Physgun: Physgun( me, eye, rot, look, move ); break;
			case SwrpBuild.Tool.Toolgun: Toolgun( eye, rot ); break;
			case SwrpBuild.Tool.Spawner: Spawner( eye, rot, look ); break;
		}

		// turning something holds the view still
		var turning = _spinning || _rotating;
		if ( turning ) me.Controller.EyeAngles = _frozenEyes ??= me.Controller.EyeAngles;
		else _frozenEyes = null;
	}

	void PlayerController.IEvents.PreInput()
	{
		if ( IsProxy || !( _spinning || _rotating ) ) return;
		_look = Input.AnalogLook;
		_move = Input.AnalogMove;
		_capturedAt = Time.Now;
		Input.AnalogLook = Angles.Zero;
		if ( _spinning ) Input.AnalogMove = Vector3.Zero;
	}

	bool? _thirdBefore;

	/// <summary> Looking through a camera you see yourself, so your body draws as in third person meanwhile. </summary>
	void ViewThirdPerson( SwrpPlayer me )
	{
		if ( !me.IsValid() || !me.Controller.IsValid() ) return;
		if ( SwrpCameraView.Active && _thirdBefore is null ) { _thirdBefore = me.Controller.ThirdPerson; me.Controller.ThirdPerson = true; }
		else if ( !SwrpCameraView.Active && _thirdBefore is { } before ) { me.Controller.ThirdPerson = before; _thirdBefore = null; }
	}

	protected override void OnPreRender()
	{
		if ( IsProxy ) return;
		if ( GetComponent<SwrpPlayer>() is { } me ) SwrpCameraView.Apply( me );
	}

	protected override void OnDisabled()
	{
		if ( !IsProxy && _hold != Hold.None ) EndHold( 0, default );
	}

	SceneTraceResult Trace( Vector3 eye, Vector3 forward ) =>
		Scene.Trace.Ray( eye, eye + forward * SwrpPhysgun.Range ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();

	// ----- physgun (Sandbox Physgun.OnControl) -----

	void Physgun( SwrpPlayer me, Vector3 eye, Rotation rot, Angles lookInput, Vector3 move )
	{
		var yaw = rot.Yaw();
		_spinning = Input.Down( "Use" ) && _hold != Hold.None;

		var snapping = Input.Down( "Run" ) || Input.Down( "Walk" );
		var snapAngle = Input.Down( "Walk" ) ? 15f : 45f;
		if ( !snapping && _snapping ) _spin = _snap;
		_snapping = snapping;

		if ( _hold != Hold.None )
		{
			if ( Input.Pressed( "Reload" ) ) SwrpPhysgun.UnfreezeAll( _held );

			if ( _hold == Hold.Gravity )
			{
				if ( Input.Pressed( "Attack1" ) ) { EndHold( 2, rot.Forward ); return; }
				if ( Input.Pressed( "Attack2" ) ) { EndHold( 0, default ); return; }
			}
			else
			{
				if ( !Input.Down( "Attack1" ) ) { EndHold( 0, default ); return; }
				if ( Input.Down( "Attack2" ) ) { EndHold( 1, default ); return; }
				if ( Input.MouseWheel.y != 0 )
				{
					_distance = MathF.Max( 0f, _distance + Input.MouseWheel.y * 20f );
					Input.MouseWheel = default;
				}
			}

			if ( _spinning )
			{
				const float keyboardSpeed = 240f;
				var look = lookInput * -1;
				if ( _hold == Hold.Beam ) _distance = MathF.Max( 0f, _distance + move.x * keyboardSpeed * Time.Delta );
				if ( _snapping )
				{
					if ( MathF.Abs( look.yaw ) > MathF.Abs( look.pitch ) ) look.pitch = 0;
					else look.yaw = 0;
				}

				_spin = Rotation.From( look ) * _spin;
				var spin = _spin;

				// gravity holds pitch with W/S around your right; beam holds use only your yaw
				if ( _hold == Hold.Gravity && MathF.Abs( move.x ) > 0.001f )
					spin = Rotation.From( new Angles( move.x * keyboardSpeed * Time.Delta, 0f, 0f ) ) * spin;
				var eyeRotation = _hold == Hold.Gravity ? rot : Rotation.FromYaw( yaw );

				// A/D turns it around world up
				if ( MathF.Abs( move.y ) > 0.001f )
				{
					var world = Rotation.FromYaw( move.y * keyboardSpeed * Time.Delta ) * ( eyeRotation * spin );
					spin = eyeRotation.Inverse * world;
				}
				_spin = spin;

				if ( _snapping ) spin = eyeRotation.Inverse * Snap( ( eyeRotation * spin ).Angles(), snapAngle ).ToRotation();
				_snap = spin;
				_grabOffset = spin;
			}

			if ( _sinceAim > 1f / 30f )
			{
				_sinceAim = 0;
				SwrpPhysgun.Aim( eye, rot, _grabOffset, _distance );
			}
			return;
		}

		if ( _preventReselect )
		{
			if ( !Input.Down( "Attack1" ) && !Input.Down( "Attack2" ) ) _preventReselect = false;
			return;
		}

		var tr = Trace( eye, rot.Forward );
		var target = tr.Hit ? SwrpBuild.Grabbable( tr.GameObject ) : null;
		HoverGrabbable = target is not null;
		Pulling = target is not null && Input.Down( "Attack2" );
		if ( target is null )
		{
			if ( Input.Pressed( "Attack2" ) ) SwrpAudio.Ui( "sounds/ui/ui_deny.sound" );
			return;
		}

		if ( Input.Down( "Attack2" ) )
		{
			if ( _sincePull > 0.05f && target.Components.Get<Rigidbody>() is { MotionEnabled: true } )
			{
				_sincePull = 0;
				SwrpPhysgun.PullTick( target.Id, eye, rot );
			}
			return;
		}

		if ( Input.Down( "Attack1" ) )
		{
			// a ragdoll's limb has its own body: grab that one (the host finds it by its bone name)
			var root = target;
			string limb = null;
			if ( tr.GameObject != target && tr.GameObject.Components.Get<Rigidbody>() is not null ) { target = tr.GameObject; limb = target.Name; }
			_held = root.Id;
			_distance = eye.Distance( tr.HitPosition );
			_grabOffset = Rotation.FromYaw( yaw ).Inverse * target.WorldRotation;
			_spin = _snap = _grabOffset;
			_hold = Hold.Beam;
			SwrpBuild.Grabbing = true;
			_sinceAim = 0;
			SwrpPhysgun.Begin( root.Id, limb, target.WorldTransform.PointToLocal( tr.HitPosition ), _grabOffset, _distance, eye, rot );
		}
		else if ( Input.Pressed( "Reload" ) ) SwrpPhysgun.UnfreezeAll( target.Id );
	}

	/// <summary> The host caught what your right mouse was pulling: it's a gravity hold now. </summary>
	internal void OnPulledIn( Guid target, Rotation grabOffset )
	{
		if ( SwrpBuild.Active != SwrpBuild.Tool.Physgun || _hold != Hold.None ) { SwrpPhysgun.End( 0, default ); return; }
		_held = target;
		_hold = Hold.Gravity;
		_distance = 0;
		_grabOffset = _spin = _snap = grabOffset;
		SwrpBuild.Grabbing = true;
	}

	void EndHold( int mode, Vector3 dir )
	{
		_hold = Hold.None;
		_spinning = false;
		_preventReselect = true;
		SwrpBuild.Grabbing = false;
		SwrpPhysgun.End( mode, dir );
	}

	static Angles Snap( Angles a, float step ) => new( MathF.Round( a.pitch / step ) * step, MathF.Round( a.yaw / step ) * step, MathF.Round( a.roll / step ) * step );

	// ----- tool gun -----

	void Toolgun( Vector3 eye, Rotation rot )
	{
		var mode = BuildToolMode.Get( SwrpBuild.ToolMode );
		if ( mode != _mode ) { _mode?.Reset(); _mode = mode; }

		var tr = Trace( eye, rot.Forward );
		var target = tr.Hit ? SwrpBuild.Grabbable( tr.GameObject ) : null;
		var root = tr.GameObject?.Root;
		var world = tr.Hit && target is null && root is not null && !root.Tags.Has( "player" )
			&& root.Components.Get<SwrpPlayer>() is null && root.Components.Get<SwrpDoor>() is null;

		mode.Control( new BuildAim( eye, rot.Forward, tr, target, world ) );
		CrosshairValid = mode.Valid;
	}

	// ----- spawner (Sandbox SpawnerWeapon) -----

	async void LoadGhost( string model )
	{
		_ghostFor = model;
		_ghost = null;
		var mdl = SwrpBuild.IsCloud( model ) ? await Cloud.Load<Model>( model ) : Model.Load( SwrpBuild.GhostModel( model ) );
		if ( _ghostFor == model ) _ghost = mdl is { IsError: false } ? mdl : null;
	}

	void Spawner( Vector3 eye, Rotation rot, Angles look )
	{
		var model = SwrpBuild.SpawnModel;
		if ( model is null ) { _rotating = false; return; }
		if ( model != _ghostFor ) { LoadGhost( model ); _placeOffset = _placeSpin = Rotation.Identity; }

		_rotating = Input.Down( "Use" );
		var snapping = Input.Down( "Run" );
		if ( !snapping && _placeSnapping ) _placeSpin = _placeOffset;
		_placeSnapping = snapping;
		if ( _rotating )
		{
			_placeSpin = Rotation.From( look with { pitch = 0 } ) * _placeSpin;
			_placeOffset = _placeSnapping ? Snap( _placeSpin.Angles(), 45f ).ToRotation() : _placeSpin;
		}

		if ( Input.Pressed( "Attack2" ) )
		{
			SwrpBuild.SpawnModel = null;
			SwrpBuild.Active = SwrpBuild.Tool.None;
			return;
		}

		var tr = Scene.Trace.Ray( eye, eye + rot.Forward * SwrpPhysgun.Range ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player" ).Run();
		_placeOk = tr.Hit && _ghost is not null;
		CrosshairValid = _placeOk;
		if ( !tr.Hit ) return;

		// stand it on the surface, facing you
		var up = tr.Normal;
		var right = Vector3.Cross( up, -rot.Forward ).Normal;
		var forward = Vector3.Cross( right, up ).Normal;
		var facing = Rotation.LookAt( forward, up );
		var scale = SwrpBuild.GhostScale( model );
		var position = tr.EndPosition + up * -( ( _ghost?.Bounds.Mins.z ?? 0f ) * scale );
		var placement = new Transform( position, facing * _placeOffset, scale );

		if ( _ghost is not null && !VisorOverlay.HudHidden && !SwrpCameraView.Active ) DebugOverlay.Model( _ghost, color: new Color( 0.55f, 0.85f, 1f, 0.55f ), transform: placement, castShadows: false );

		if ( _placeOk && Input.Pressed( "Attack1" ) )
		{
			SwrpBuild.SpawnPropAt( model, placement.Position, placement.Rotation, ColourMode.Colour );
			Zap( tr.EndPosition );
			_placeOffset = _placeSpin = Rotation.Identity;
		}
	}

	// ----- tracer -----

	/// <summary> The tool gun's tracer from your own hand (instant feedback before the host answers). </summary>
	public static void Zap( Vector3 at ) => ZapFrom( SwrpPlayer.Local, at );

	public static void ZapFrom( SwrpPlayer holder, Vector3 at )
	{
		if ( holder is null || !holder.Controller.IsValid() ) return;
		var aim = holder.Controller.EyeAngles.ToRotation();
		var from = ( holder.Network.Owner is { } o ? SwrpHeldToolSystem.Muzzle( o.Id ) : null ) ?? SwrpPhysgunSystem.Hand( holder ) ?? ( holder.Controller.EyePosition + aim.Down * 14f + aim.Right * 8f + aim.Forward * 16f );
		SwrpPhysgunSystem.AddZap( from, at );
		if ( holder == SwrpPlayer.Local ) SwrpHeldToolSystem.Fire();
		SwrpAudio.At( "sounds/ui/ui_click.sound", from );
	}
}
