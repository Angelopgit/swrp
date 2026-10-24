using System;

namespace Swrp.Feel;

/// <summary>
/// Easing curves and the shared motion timings. The SCSS side (<c>Code/Feel/_feel.scss</c>: <c>$feel-fast</c>,
/// <c>$feel-med</c>, <c>$feel-slow</c> and the <c>feel-*</c> mixins) uses the same numbers, so a panel animated from code and
/// one animated by a stylesheet move alike. Use these instead of inventing a new duration.
/// </summary>
public static class FeelMotion
{
	/// <summary> Buttons, hover, small pop-ups, tooltips (s). </summary>
	public const float Fast = 0.12f;
	/// <summary> Backdrops, list items, side panels sliding in (s). </summary>
	public const float Med = 0.25f;
	/// <summary> Big holo panels and full-screen menus (s). </summary>
	public const float Slow = 0.35f;
	/// <summary> Screen fades back in after a cut (deploy, landing) (s). </summary>
	public const float Reveal = 0.8f;

	/// <summary> Distance a panel slides in from (px). </summary>
	public const float SlideDistance = 12f;
	/// <summary> Scale a panel pops in from. </summary>
	public const float PopScale = 0.97f;

	public static float Linear( float t ) => Math.Clamp( t, 0f, 1f );

	/// <summary> Fast start, gentle stop: everything coming on screen. CSS <c>ease-out</c> is close to this. </summary>
	public static float OutCubic( float t ) { t = Math.Clamp( t, 0f, 1f ) - 1f; return t * t * t + 1f; }

	/// <summary> Gentle start, fast end: things leaving the screen. </summary>
	public static float InCubic( float t ) { t = Math.Clamp( t, 0f, 1f ); return t * t * t; }

	/// <summary> Smooth both ends: fades, camera moves. </summary>
	public static float InOutCubic( float t )
	{
		t = Math.Clamp( t, 0f, 1f );
		return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow( -2f * t + 2f, 3f ) / 2f;
	}

	/// <summary> Ken Perlin's smootherstep (the deploy fly-in uses it). </summary>
	public static float Smoother( float t ) { t = Math.Clamp( t, 0f, 1f ); return t * t * t * ( t * ( t * 6f - 15f ) + 10f ); }

	/// <summary> A slight overshoot then settle: pop-ups and hit markers. </summary>
	public static float OutBack( float t, float overshoot = 1.4f )
	{
		t = Math.Clamp( t, 0f, 1f ) - 1f;
		return t * t * ( ( overshoot + 1f ) * t + overshoot ) + 1f;
	}

	/// <summary> 0 → 1 → 0 over the time: flashes and pulses. </summary>
	public static float Pulse( float t ) { t = Math.Clamp( t, 0f, 1f ); return MathF.Sin( t * MathF.PI ); }

	/// <summary> How far an intro is after <paramref name="since"/> seconds of <paramref name="duration"/>, eased out. </summary>
	public static float In( float since, float duration = Med ) => OutCubic( since / MathF.Max( 0.0001f, duration ) );

	/// <summary> 1 → 0 over <paramref name="duration"/>, eased in (for outros). </summary>
	public static float Out( float since, float duration = Fast ) => 1f - InCubic( since / MathF.Max( 0.0001f, duration ) );

	/// <summary>
	/// A damped spring step (critically damped at <paramref name="damping"/> 1): moves <paramref name="value"/> towards
	/// <paramref name="target"/> carrying <paramref name="velocity"/>. Frequency in Hz. Camera kicks use it.
	/// </summary>
	public static void Spring( ref float value, ref float velocity, float target, float frequency, float damping, float dt )
	{
		var w = 2f * MathF.PI * frequency;
		var a = -2f * damping * w * velocity - w * w * ( value - target );
		velocity += a * dt;
		value += velocity * dt;
	}

	/// <summary> CSS for a panel animated from code: opacity and a slide in from below by <see cref="SlideDistance"/>. </summary>
	public static string SlideStyle( float k )
	{
		var inv = System.Globalization.CultureInfo.InvariantCulture;
		return $"opacity: {k.ToString( "0.000", inv )}; transform: translateY( {( ( 1f - k ) * SlideDistance ).ToString( "0.0", inv )}px );";
	}
}
