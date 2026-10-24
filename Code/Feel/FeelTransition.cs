using System;

namespace Swrp.Feel;

/// <summary>
/// Full-screen fades, drawn over everything (HUD and menus too) by <see cref="FeelFadeOverlay"/>. One fade at a time; a new
/// one starts from wherever the screen is now, so they never pop. Presets:
/// <list type="bullet">
/// <item><see cref="Deploy"/>: the Deploy button fades to black and holds until your trooper is in the world, then the
/// view fades back in over the camera's fly-in.</item>
/// <item><see cref="CharacterScreen"/>: the character screen opening or closing in game cuts through black.</item>
/// <item><see cref="Hyperspace"/> / <see cref="Landing"/>: for the hyperspace jump and the gunship ride; both already have
/// their own (SwrpSpace's white flash), so these are offered, not wired.</item>
/// </list>
/// </summary>
public static class FeelTransition
{
	enum Phase { Idle, In, Hold, Out }

	static Phase _phase;
	static float _t, _inTime, _holdTime, _outTime, _from;
	static Func<bool> _holdUntil;
	static float _holdMax;

	/// <summary> 0..1 how much of the screen the fade covers right now. </summary>
	public static float Alpha { get; private set; }

	/// <summary> The fade's colour (black, or the hyperspace white). </summary>
	public static Color Tint { get; private set; } = Color.Black;

	/// <summary> True while a fade is running (menus may wait for it). </summary>
	public static bool Busy => _phase != Phase.Idle;

	/// <summary> Fade to <paramref name="color"/> over <paramref name="inTime"/>, hold, then fade back out over <paramref name="outTime"/>. </summary>
	public static void Dip( Color color, float inTime, float hold, float outTime )
	{
		if ( !Feel.Transitions ) return;
		Tint = color;
		_from = Alpha;
		_inTime = MathF.Max( 0.0001f, inTime );
		_holdTime = hold;
		_outTime = MathF.Max( 0.0001f, outTime );
		_holdUntil = null;
		_t = 0f;
		_phase = Phase.In;
	}

	/// <summary> Start fully covered and fade out over <paramref name="outTime"/> (for cuts that already happened). </summary>
	public static void Reveal( Color color, float outTime )
	{
		if ( !Feel.Transitions ) return;
		Tint = color;
		Alpha = 1f;
		_outTime = MathF.Max( 0.0001f, outTime );
		_holdUntil = null;
		_t = 0f;
		_phase = Phase.Out;
	}

	/// <summary> Like <see cref="Dip"/> but the hold lasts until <paramref name="until"/> is true (at most <paramref name="maxHold"/> s). </summary>
	public static void DipUntil( Color color, float inTime, Func<bool> until, float maxHold, float outTime )
	{
		Dip( color, inTime, 0f, outTime );
		if ( !Busy ) return;
		_holdUntil = until;
		_holdMax = maxHold;
	}

	/// <summary> End a held fade now (it fades back out). </summary>
	public static void ReleaseHold()
	{
		_holdUntil = null;
		if ( _phase == Phase.Hold ) { _phase = Phase.Out; _t = 0f; }
	}

	/// <summary> Clear the screen at once (a fade must never be left stuck over the game). </summary>
	public static void Clear()
	{
		_phase = Phase.Idle;
		_holdUntil = null;
		Alpha = 0f;
	}

	// ----- presets -----

	/// <summary> The character screen's Deploy: black until your trooper exists (at most 6 s), then a slow reveal. </summary>
	public static void Deploy()
	{
		Sound( FeelSounds.TransitionWhoosh );
		DipUntil( Color.Black, FeelMotion.Slow, () => Feel.LocalPlayer.IsValid() && Feel.LocalPlayer.Controller.IsValid(), 6f, FeelMotion.Reveal );
	}

	/// <summary> The character screen opening or closing in game: the cut has happened, fade back in from black. </summary>
	public static void CharacterScreen() => Reveal( Color.Black, FeelMotion.Slow );

	/// <summary> Into (or out of) hyperspace: a white-blue flash that clears slowly. </summary>
	public static void Hyperspace() => Reveal( new Color( 0.85f, 0.93f, 1f ), FeelMotion.Reveal );

	/// <summary> A gunship ride down or up: black in, held for the ride, slow reveal. </summary>
	public static void Landing( float rideSeconds ) => Dip( Color.Black, FeelMotion.Reveal, MathF.Max( 0f, rideSeconds - 2f * FeelMotion.Reveal ), FeelMotion.Reveal );

	static void Sound( string sound )
	{
		if ( Feel.Transitions && Feel.Sounds ) FeelSounds.PlayUi( sound );
	}

	// ----- every frame -----

	static bool _wasDeployed;
	static bool _wasCharMenu;

	internal static void Tick( float dt )
	{
		if ( !Feel.Transitions ) { Clear(); return; }

		// the character screen opened or closed while deployed (F4, Esc → Characters): the cut has just happened
		var deployed = Feel.LocalPlayer.IsValid();
		var charMenu = SwrpUiState.CharacterMenuOpen;
		if ( deployed && _wasDeployed && charMenu != _wasCharMenu && _phase == Phase.Idle ) CharacterScreen();
		_wasDeployed = deployed;
		_wasCharMenu = charMenu;

		_t += dt;
		switch ( _phase )
		{
			case Phase.In:
				Alpha = MathX.Lerp( _from, 1f, FeelMotion.InOutCubic( _t / _inTime ) );
				if ( _t >= _inTime ) { _phase = Phase.Hold; _t = 0f; Alpha = 1f; }
				break;
			case Phase.Hold:
				Alpha = 1f;
				var done = _holdUntil is not null ? ( _holdUntil() || _t >= _holdMax ) : _t >= _holdTime;
				if ( done ) { _phase = Phase.Out; _t = 0f; _holdUntil = null; }
				break;
			case Phase.Out:
				Alpha = 1f - FeelMotion.InOutCubic( _t / _outTime );
				if ( _t >= _outTime ) Clear();
				break;
			default:
				Alpha = 0f;
				break;
		}
	}
}
