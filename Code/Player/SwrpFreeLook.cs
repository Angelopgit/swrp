using System;

namespace Swrp;

/// <summary>
/// Third person free look: hold Alt to orbit the camera around your trooper while they keep facing and walking the same
/// way. Let go and the camera eases back behind them. Owner only; first person is untouched.
/// </summary>
public sealed class SwrpFreeLook : Component
{
	/// <summary> Third person camera distance behind the trooper; the mouse wheel changes it in third person. </summary>
	[ConVar( "swrp_thirdperson_distance", Help = "Third person camera distance (mouse wheel in third person, 60-300)", Saved = true )]
	public static float CameraDistance { get; set; } = 160f;

	/// <summary> Over-the-shoulder offset: positive is right, negative left, 0 centred. </summary>
	[ConVar( "swrp_thirdperson_side", Help = "Third person camera shoulder offset (-40 left, 0 centre, 40 right)", Saved = true )]
	public static float CameraSide { get; set; } = 0f;

	[ConVar( "swrp_thirdperson_height", Help = "Third person camera height above the eyes (-20 to 40)", Saved = true )]
	public static float CameraHeight { get; set; } = 12f;

	PlayerController _controller;
	PlayerController Controller => _controller ??= GetComponent<PlayerController>();

	bool _active;
	Angles _held;      // where the trooper was looking when free look started
	Angles _look;      // where the camera looks while free looking
	float _blend;      // 0 = normal camera, 1 = fully free

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		var c = Controller;
		if ( !c.IsValid() ) return;

		// Third person framing: Walk + the mouse wheel zooms (the wheel alone switches weapons, SwrpHands), the rest comes from
		// the saved settings.
		if ( c.ThirdPerson )
		{
			if ( !SwrpUiState.AnyOpen && !SwrpBuild.Grabbing && Input.Down( "Walk" ) && Input.MouseWheel.y != 0 )
			{
				var d = Math.Clamp( CameraDistance - Input.MouseWheel.y * 15f, 60f, 300f );
				if ( d != CameraDistance ) ConsoleSystem.SetValue( "swrp_thirdperson_distance", d );
			}
			c.CameraOffset = new Vector3( Math.Clamp( CameraDistance, 60f, 300f ), Math.Clamp( CameraSide, -60f, 60f ), Math.Clamp( CameraHeight, -20f, 40f ) );
		}

		var want = c.ThirdPerson && Input.Down( "Walk" ) && !SwrpUiState.AnyOpen;

		if ( want && !_active )
		{
			_active = true;
			_held = c.EyeAngles;
			_look = c.EyeAngles;
		}
		else if ( !want && _active )
		{
			_active = false;
		}

		if ( _active )
		{
			// Whatever look input moved the eyes this frame turns the camera instead; the trooper keeps its heading.
			var delta = c.EyeAngles - _held;
			_look = new Angles( Math.Clamp( _look.pitch + delta.pitch, -80f, 80f ), _look.yaw + delta.yaw, 0 );
			c.EyeAngles = _held;
		}

		_blend = _blend.Approach( _active ? 1f : 0f, Time.Delta * 6f );
	}

	protected override void OnPreRender()
	{
		if ( IsProxy || _blend <= 0.001f ) return;
		var c = Controller;
		var cam = Scene.Camera;
		if ( !c.IsValid() || !cam.IsValid() || !c.ThirdPerson ) return;

		var pivot = c.EyePosition;
		var rot = Rotation.Slerp( cam.WorldRotation, _look.ToRotation(), _blend );
		var wanted = pivot - rot.Forward * Math.Clamp( CameraDistance, 60f, 300f ) + rot.Right * CameraSide + Vector3.Up * CameraHeight;

		// Keep the camera out of walls.
		var tr = Scene.Trace.Ray( pivot, wanted ).Radius( 6f ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player" ).Run();
		var pos = tr.Hit ? tr.EndPosition + tr.Normal * 4f : wanted;

		cam.WorldPosition = Vector3.Lerp( cam.WorldPosition, pos, _blend );
		cam.WorldRotation = rot;
	}
}
