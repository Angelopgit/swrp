using System;

namespace Swrp;

/// <summary>
/// Gives a light some life: pulsing warning beacons, flickering fixtures, or a spinning beacon head.
/// Put it next to a light component (PointLight / SpotLight).
/// </summary>
[Title( "Light FX" ), Category( "SWRP" ), Icon( "lightbulb" )]
public sealed class LightFx : Component
{
	public enum FxMode
	{
		/// <summary> Smooth pulse, like a warning beacon. </summary>
		Pulse,
		/// <summary> Mostly steady with occasional stutters, like a failing fluorescent. </summary>
		Flicker,
		/// <summary> Rotates the object around its up axis. Use on a SpotLight for a sweeping beacon. </summary>
		Spin,
		/// <summary> Very slow gentle breathing of brightness. </summary>
		Breathe,
	}

	[Property] public FxMode Mode { get; set; } = FxMode.Pulse;

	/// <summary> Pulses per second (Pulse/Breathe) or revolutions per second (Spin). </summary>
	[Property, Range( 0.05f, 5f )] public float Speed { get; set; } = 0.8f;

	/// <summary> Brightness at the dim end, as a fraction of the original (Pulse/Flicker/Breathe). </summary>
	[Property, Range( 0f, 1f )] public float MinBrightness { get; set; } = 0.08f;

	/// <summary> Offset so lights placed together don't pulse in lockstep. </summary>
	[Property, Range( 0f, 1f )] public float Phase { get; set; }

	Light _light;
	Color _baseColor;
	float _flickerUntil;
	float _flickerLevel = 1f;

	protected override void OnStart()
	{
		_light = GetComponent<Light>();
		if ( _light.IsValid() )
			_baseColor = _light.LightColor;
	}

	protected override void OnUpdate()
	{
		if ( Mode == FxMode.Spin )
		{
			LocalRotation = Rotation.FromYaw( Speed * 360f * Time.Delta ) * LocalRotation;
			return;
		}

		if ( !_light.IsValid() ) return;

		float k;
		var t = Time.Now * Speed + Phase;

		switch ( Mode )
		{
			case FxMode.Pulse:
				// A quick bright flash with a slow tail reads as a beacon rather than a sine wave.
				var s = 0.5f + 0.5f * MathF.Sin( t * MathF.PI * 2f );
				k = MathF.Pow( s, 3f );
				break;

			case FxMode.Breathe:
				k = 0.5f + 0.5f * MathF.Sin( t * MathF.PI * 2f );
				break;

			default: // Flicker
				if ( Time.Now > _flickerUntil )
				{
					// Mostly steady; every so often a short stutter at a random level.
					if ( Game.Random.Float() < 0.012f )
					{
						_flickerUntil = Time.Now + Game.Random.Float( 0.04f, 0.22f );
						_flickerLevel = Game.Random.Float( 0f, 0.7f );
					}
					else
					{
						_flickerLevel = 1f;
					}
				}

				k = _flickerLevel;
				break;
		}

		var brightness = MinBrightness + ( 1f - MinBrightness ) * k;
		_light.LightColor = new Color( _baseColor.r * brightness, _baseColor.g * brightness, _baseColor.b * brightness, _baseColor.a );
	}
}
