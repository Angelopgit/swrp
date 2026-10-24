using System;

namespace Swrp.Feel;

/// <summary>
/// The game feel module: one place for how the game moves, sounds and reacts, so every thread's features feel the same.
/// <list type="bullet">
/// <item><see cref="FeelMotion"/>: easing curves and the shared UI timings (the same numbers as <c>Code/Feel/_feel.scss</c>).</item>
/// <item><see cref="FeelTransition"/>: full-screen fades (deploy, character screen; presets for hyperspace and landing).</item>
/// <item><see cref="FeelHits"/>: the directional flinch when you're hit, the red edge flash and blur of a heavy hit, the
/// low-health pulse, and the push a body gets from the shot that killed it.</item>
/// <item><see cref="FeelSounds"/>: the synthesized feel sounds (<c>SoundSource/feel_sounds.py</c>): footsteps on planet
/// ground, impacts per surface, debris, bolts whizzing past, door and lift whooshes.</item>
/// </list>
/// Everything here is local and cosmetic (each machine feels its own view). Every feature has a <c>swrp_feel_*</c> switch;
/// <c>swrp_feel 0</c> turns them all off. <c>STYLE_GUIDE.md</c> in the project root holds the numbers.
/// </summary>
public static class Feel
{
	[ConVar( "swrp_feel", Help = "Game feel module: hit reactions, screen transitions, feel sounds (1 = on, 0 = all off)", Saved = true )]
	public static bool On { get; set; } = true;

	[ConVar( "swrp_feel_kick", Help = "Hits flinch your view away from where they came from (1 = on)", Saved = true )]
	public static bool KickOn { get; set; } = true;

	[ConVar( "swrp_feel_hitfx", Help = "Red edge flash on hits, a short blur on heavy hits, a pulse at low health (1 = on)", Saved = true )]
	public static bool HitFxOn { get; set; } = true;

	[ConVar( "swrp_feel_transitions", Help = "Fade through black when you deploy and when the character screen opens or closes (1 = on)", Saved = true )]
	public static bool TransitionsOn { get; set; } = true;

	[ConVar( "swrp_feel_ragdoll", Help = "Bodies are thrown by the shot that killed them (1 = on)", Saved = true )]
	public static bool RagdollOn { get; set; } = true;

	[ConVar( "swrp_feel_sounds", Help = "Feel sounds: footsteps on planet ground, impacts per surface, debris, door and lift whooshes (1 = on)", Saved = true )]
	public static bool SoundsOn { get; set; } = true;

	[ConVar( "swrp_feel_whizz", Help = "Bolts passing close to your head whizz by (1 = on)", Saved = true )]
	public static bool WhizzOn { get; set; } = true;

	[ConVar( "swrp_feel_volume", Help = "Volume of the feel sounds (0 to 1.5, default 1)", Saved = true )]
	public static float Volume { get; set; } = 1f;

	internal static bool Kick => On && KickOn;
	internal static bool HitFx => On && HitFxOn;
	internal static bool Transitions => On && TransitionsOn;
	internal static bool Ragdoll => On && RagdollOn;
	internal static bool Sounds => On && SoundsOn && Volume > 0.001f;
	internal static bool Whizz => On && WhizzOn && Volume > 0.001f;

	/// <summary> The local player's trooper this frame (cached once a frame: <see cref="SwrpPlayer.Local"/> searches the scene). </summary>
	internal static SwrpPlayer LocalPlayer { get; set; }

	[ConCmd( "swrp_feel_test", Help = "swrp_feel_test <hit|heavy|stun|lowhp|deploy|dip|flash|whizz|step|impact|debris|door|lift> - your view only: try a feel effect (no damage, nothing networked)" )]
	public static void Test( string what = "hit" )
	{
		var cam = Game.ActiveScene?.Camera;
		var ahead = cam.IsValid() ? cam.WorldPosition + cam.WorldRotation.Forward * 200f : Vector3.Zero;
		var side = cam.IsValid() ? cam.WorldPosition + cam.WorldRotation.Right * 300f : Vector3.Zero;
		switch ( what )
		{
			case "hit": FeelHits.Hurt( side, 18f, false ); break;
			case "heavy": FeelHits.Hurt( side, 45f, false ); SwrpShake.Add( 0.5f ); break;
			case "stun": FeelHits.Hurt( side, 0f, true ); break;
			case "lowhp": FeelHits.TestLowHealth = FeelHits.TestLowHealth > 0f ? 0f : 8f; break;
			case "deploy": FeelTransition.Deploy(); FeelTransition.ReleaseHold(); break;
			case "dip": FeelTransition.Dip( Color.Black, FeelMotion.Fast, 0.15f, FeelMotion.Slow ); break;
			case "flash": FeelTransition.Hyperspace(); break;
			case "whizz": FeelSounds.Play( FeelSounds.BoltWhizz, cam.IsValid() ? cam.WorldPosition + cam.WorldRotation.Right * 40f : Vector3.Zero ); break;
			case "step": FeelSounds.Play( FeelSounds.StepSand, ahead ); break;
			case "impact": FeelSounds.Play( FeelSounds.ImpactMetal, ahead ); break;
			case "debris": FeelSounds.Debris( ahead, 12 ); break;
			case "door": FeelSounds.Play( FeelSounds.DoorWhoosh, ahead ); break;
			case "lift": FeelSounds.PlayUi( FeelSounds.LiftWhoosh ); break;
			default: Log.Info( "[SWRP] swrp_feel_test hit|heavy|stun|lowhp|deploy|dip|flash|whizz|step|impact|debris|door|lift" ); return;
		}
		Log.Info( $"[SWRP] feel test: {what}" );
	}

	[ConCmd( "swrp_feel_info", Help = "Print the feel module's switches and state" )]
	public static void Info()
	{
		Log.Info( $"[SWRP] feel {( On ? "on" : "off" )}: kick {KickOn}, hitfx {HitFxOn}, transitions {TransitionsOn}, ragdoll {RagdollOn}, sounds {SoundsOn}, whizz {WhizzOn}, volume {Volume:0.00}" );
		Log.Info( $"[SWRP] fade {FeelTransition.Alpha:0.00}, hit flash {FeelHits.Flash:0.00}, blur {FeelHits.Blur:0.00}, low health {FeelHits.LowHealth:0.00}, surface here {FeelSounds.SurfaceAt( Game.ActiveScene?.Camera?.WorldPosition ?? Vector3.Zero )}" );
	}
}

/// <summary>
/// Runs the feel module once a frame on each machine (after every component's update, before rendering) and puts its two
/// screen layers up: the hit layer just over the colour grade and under the HUD, and the fade layer over everything.
/// </summary>
public sealed class FeelSystem : GameObjectSystem<FeelSystem>
{
	GameObject _hitLayer, _fadeLayer;
	bool _reportedError;

	public FeelSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 20, () => { if ( Live( Scene ) ) FeelHits.Restore( Scene ); }, "SwrpFeelRestore" );
		Listen( Stage.FinishUpdate, 20, Tick, "SwrpFeel" );
	}

	static bool Live( Scene scene ) => scene is not null && Game.IsPlaying && scene == Game.ActiveScene;

	void Tick()
	{
		if ( !Live( Scene ) ) return;
		try
		{
			Feel.LocalPlayer = SwrpPlayer.Local;
			EnsureLayers();
			var dt = Math.Min( Time.Delta, 0.05f );
			FeelTransition.Tick( dt );
			FeelHits.Tick( Scene, dt );
			FeelSounds.Tick( Scene, dt );
		}
		catch ( Exception e )
		{
			if ( _reportedError ) return;
			_reportedError = true;
			Log.Warning( $"[SWRP] feel: {e}" );
		}
	}

	void EnsureLayers()
	{
		if ( !_hitLayer.IsValid() ) _hitLayer = Layer<FeelHitOverlay>( "Feel Hit FX", 55 );
		if ( !_fadeLayer.IsValid() ) _fadeLayer = Layer<FeelFadeOverlay>( "Feel Fade", 900 );
	}

	GameObject Layer<T>( string name, int z ) where T : PanelComponent, new()
	{
		var go = Scene.CreateObject();
		go.Name = name;
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var panel = go.Components.Create<ScreenPanel>();
		panel.ZIndex = z;
		go.Components.Create<T>();
		return go;
	}
}
