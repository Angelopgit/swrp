using System;

namespace Swrp;

/// <summary>
/// How a timed light's brightness runs over its life (0..1 of the duration).
/// </summary>
public enum LightCurve
{
	/// <summary> Full at once, then a fast exponential drop: muzzle flashes, sparks. </summary>
	Flash,
	/// <summary> Straight fade to nothing. </summary>
	Linear,
	/// <summary> Eases out: holds a little, then rolls off. </summary>
	Smooth,
	/// <summary> A quick swell, then a long warm tail that lingers: impacts cooling, hot metal. </summary>
	Ember,
	/// <summary> Steady for three quarters of the life, then fades: breaches, timed lamps. </summary>
	Hold,
	/// <summary> Swells up and back down once. </summary>
	Swell,
}

/// <summary>
/// What a light looks like: colour and brightness, reach, flicker, whether it may ask for a shadow, how much fake bounce it
/// throws off the surface it's on, and how strongly it lights the haze. Immutable; make variants with <c>with</c>.
/// </summary>
public sealed record LightProfile
{
	public Color Color { get; init; } = Color.White;
	/// <summary> Multiplies the colour (HDR: 1 is a dim lamp, 6+ is a flash). </summary>
	public float Intensity { get; init; } = 1f;
	public float Radius { get; init; } = 200f;
	/// <summary> 0..1: how deep the flicker dips. </summary>
	public float Flicker { get; init; }
	/// <summary> Flicker rate (roughly dips per second). </summary>
	public float FlickerSpeed { get; init; } = 14f;
	/// <summary> 0..1: strength of the fake bounce light (a dim second light off the surface, tinted by its colour). </summary>
	public float Bounce { get; init; }
	/// <summary> Wants a shadow when the shadow budget allows (only the best few get one). </summary>
	public bool Shadows { get; init; }
	/// <summary> Weight in the budget: 2 beats two ordinary lights of the same brightness. </summary>
	public float Priority { get; init; } = 1f;
	/// <summary> How much it lights volumetric fog. </summary>
	public float Fog { get; init; } = 0.6f;
	public float Attenuation { get; init; } = 1f;

	// ----- presets -----

	// blaster light: bright and wide, so a shot fills the room in its colour (Angelo, 2026-10-08: "brighter and take up more in the room")
	public static LightProfile Muzzle( Color c ) => new() { Color = Color.Lerp( Vivid( c ), Color.White, 0.1f ), Intensity = 12f, Radius = 420f, Bounce = 0.5f, Priority = 1.4f, Fog = 1.6f };
	public static LightProfile Bolt( Color c ) => new() { Color = Vivid( c ), Intensity = 7f, Radius = 340f, Flicker = 0.15f, FlickerSpeed = 22f, Priority = 1.1f, Fog = 1.6f };
	public static LightProfile Impact( Color c ) => new() { Color = Vivid( c ), Intensity = 4.5f, Radius = 360f, Bounce = 0.8f, Priority = 1.2f, Fog = 1.2f };

	/// <summary> Blue light pushed deeper and more saturated (blue bolts read as blue, not cyan-white); other colours unchanged. </summary>
	public static Color Vivid( Color c )
	{
		if ( c.b <= c.r || c.b <= c.g ) return c;
		var grey = ( c.r + c.g + c.b ) / 3f;
		const float k = 1.7f;     // 1.4 still read cyan on blue bolts (2026-10-12)
		return new Color( MathF.Max( 0f, grey + ( c.r - grey ) * k ), MathF.Max( 0f, grey + ( c.g - grey ) * k ), grey + ( c.b - grey ) * k, c.a );
	}
	/// <summary> The orange glow a hit leaves behind as the spot cools. </summary>
	public static LightProfile Ember( Color c ) => new() { Color = Color.Lerp( new Color( 1f, 0.42f, 0.12f ), c, 0.25f ), Intensity = 1.6f, Radius = 70f, Flicker = 0.35f, FlickerSpeed = 9f, Priority = 0.5f, Fog = 0.3f };
	public static LightProfile Saber( Color c ) => new() { Color = c, Intensity = 4f, Radius = 280f, Flicker = 0.08f, FlickerSpeed = 18f, Bounce = 0.45f, Shadows = true, Priority = 3f, Fog = 1.4f };
	public static LightProfile Blast => new() { Color = new Color( 1f, 0.62f, 0.32f ), Intensity = 14f, Radius = 520f, Bounce = 0.6f, Shadows = true, Priority = 2.5f, Fog = 1.5f };
	public static LightProfile Sparks => new() { Color = new Color( 1f, 0.66f, 0.3f ), Intensity = 5f, Radius = 150f, Flicker = 0.6f, FlickerSpeed = 30f, Bounce = 0.4f, Priority = 1f, Fog = 0.8f };
	/// <summary> Cold light from space pouring through a hole in the hull. </summary>
	public static LightProfile Breach => new() { Color = new Color( 0.55f, 0.72f, 1f ), Intensity = 3.2f, Radius = 520f, Bounce = 0.35f, Shadows = true, Priority = 2f, Fog = 2f };
}

/// <summary>
/// One light the <see cref="LightingSystem"/> is asked to draw: a timed flash, or a persistent emitter that follows an object
/// (a saber, a bolt) until it's stopped or the object goes. Change the public fields any frame; the system reads them after
/// everything has updated. It gets a real light only while it scores in the budget.
/// </summary>
public sealed class LightEmitter
{
	public LightProfile Profile;
	public Vector3 Position;
	/// <summary> The surface it sits on (impacts); zero = find the floor below for the bounce. </summary>
	public Vector3 Normal;
	/// <summary> Follow this object (with <see cref="Offset"/> in its space); the emitter ends when it's destroyed. </summary>
	public GameObject Follow;
	public Vector3 Offset;
	/// <summary> Live overrides on top of the profile (a saber igniting, a blade flaring). </summary>
	public Color? ColorOverride;
	public float IntensityScale = 1f;
	public float RadiusScale = 1f;
	/// <summary> Off for now, kept alive (a sheathed saber). </summary>
	public bool Active = true;

	public float Duration;      // <= 0: until stopped
	public LightCurve Curve;
	public float Age;
	public bool Stopped;
	public string Tag;

	// the system's own state
	internal float Seed = Game.Random.Float( 0f, 100f );
	internal float Brightness;      // this frame's luminance after curve and flicker
	internal Color Final;           // this frame's colour * intensity
	internal float Score;
	internal float Vis;             // fade-in once it gets a slot
	internal int Slot = -1, BounceSlot = -1;
	internal bool Shadowed;
	internal Color SurfaceTint = new( 0.6f, 0.58f, 0.55f );
	internal Vector3 BouncePos;
	internal bool HaveBounce;
	internal float SinceBounceCheck = 99f;

	public bool Alive => !Stopped && ( Duration <= 0f || Age < Duration ) && ( Follow is null || Follow.IsValid() );

	public void Stop() => Stopped = true;
}

/// <summary>
/// The lighting framework's front door. Everything here is local and cosmetic (each machine lights its own view); call it from
/// the effects every machine already plays. All calls are cheap when the budget is full: a request that doesn't score just
/// isn't drawn.
/// </summary>
public static partial class Lighting
{
	/// <summary> A timed light at a point. </summary>
	public static LightEmitter Flash( Vector3 pos, Color color, float intensity, float radius, float duration, LightCurve curve = LightCurve.Flash, Vector3 normal = default )
		=> Flash( pos, new LightProfile { Color = color, Intensity = intensity, Radius = radius }, duration, curve, normal );

	public static LightEmitter Flash( Vector3 pos, LightProfile profile, float duration, LightCurve curve = LightCurve.Flash, Vector3 normal = default )
	{
		var sys = LightingSystem.Current;
		if ( sys is null || !LightingSystem.On ) return null;
		var e = new LightEmitter { Profile = profile, Position = pos, Normal = normal, Duration = MathF.Max( 0.01f, duration ), Curve = curve };
		sys.Add( e );
		return e;
	}

	/// <summary> A persistent light that rides an object until <see cref="LightEmitter.Stop"/> or the object is destroyed. </summary>
	public static LightEmitter Attach( GameObject go, LightProfile profile, Vector3 offset = default )
	{
		var sys = LightingSystem.Current;
		if ( sys is null || !go.IsValid() ) return null;
		var e = new LightEmitter { Profile = profile, Follow = go, Offset = offset, Position = go.WorldPosition };
		sys.Add( e );
		return e;
	}

	/// <summary> A blaster's muzzle flash: a hard, short pop with bounce off whatever is around the gun. </summary>
	public static void MuzzleFlash( Vector3 pos, Color color )
	{
		if ( !SwrpFx.LightsOn ) return;
		Flash( pos, LightProfile.Muzzle( color ), 0.075f, LightCurve.Flash );
	}

	/// <summary> A bolt hitting a surface: a flash off the wall, tinted bounce, and a small ember that lingers as it cools. </summary>
	public static void Impact( Vector3 pos, Vector3 normal, Color color, bool ember = true )
	{
		if ( !SwrpFx.LightsOn ) return;
		var at = pos + normal * 6f;
		Flash( at, LightProfile.Impact( color ), 0.2f, LightCurve.Flash, normal );
		if ( ember && LightingSystem.Embers ) Flash( pos + normal * 2.5f, LightProfile.Ember( color ), Game.Random.Float( 1.1f, 1.7f ), LightCurve.Ember, normal );
	}

	/// <summary> A detonator: a big shadowed fireball light, then a warm afterglow. </summary>
	public static void Explosion( Vector3 pos, float radius )
	{
		if ( !SwrpFx.LightsOn ) return;
		var p = LightProfile.Blast with { Radius = MathF.Max( 260f, radius * 2f ) };
		Flash( pos + Vector3.Up * 20f, p, 0.5f, LightCurve.Flash );
		Flash( pos + Vector3.Up * 12f, p with { Intensity = 2.2f, Shadows = false, Priority = 0.8f, Flicker = 0.3f, FlickerSpeed = 7f }, 2.4f, LightCurve.Ember );
	}

	/// <summary> A bolt in flight: a travelling light. Returns the emitter so the bolt could change it; it ends with the object. </summary>
	public static LightEmitter Bolt( GameObject bolt, Color color )
	{
		if ( !SwrpFx.LightsOn ) return null;
		return Attach( bolt, LightProfile.Bolt( color ) );
	}

	/// <summary> A dark (or bright) area: ambient multiplier, exposure cap, haze. See <see cref="LightZone"/>. </summary>
	public static void AddZone( LightZone zone ) => LightZones.Add( zone );
}
