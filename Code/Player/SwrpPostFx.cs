using System;

namespace Swrp;

/// <summary>
/// The camera's look: subtle filmic grade per area (cool bridge, warm hangar, red brig, green medbay...), gentle bloom on
/// lights and screens, light film grain, a soft vignette, a little depth of field while aiming down the sights, and the
/// ship's alert level pulling the grade towards red. Runs on each machine's own camera. swrp_postfx 0 turns the whole
/// stack off for low-end PCs. The tint itself is drawn by <see cref="GradeOverlay"/>.
/// </summary>
[Title( "SWRP Post FX" ), Category( "SWRP" ), Icon( "movie_filter" )]
public sealed class SwrpPostFx : Component
{
	[ConVar( "swrp_postfx", Help = "Cinematic post-processing: area colour grade, bloom, grain, vignette, aim focus (1 = on, 0 = off for low-end PCs)", Saved = true )]
	public static bool On { get; set; } = true;

	/// <summary> Tint = the wash colour (alpha from Strength), TempK = white balance (lower is warmer), and the glow colour of bloom. </summary>
	public record Grade( Color Tint, float Strength, float TempK, float Saturation, float Contrast, Color Glow );

	/// <summary> The single grade the simple ship-wide lighting uses: no colour wash, daylight white balance, white glow. </summary>
	public static readonly Grade Neutral = new( Color.White, 0f, 6500f, 1f, 1.04f, Color.White );

	public static readonly Dictionary<string, Grade> Grades = new()
	{
		["bridge"] = new( new Color( 0.3f, 0.55f, 1f ), 0.06f, 8200f, 0.95f, 1.06f, new Color( 0.75f, 0.9f, 1f ) ),
		["hangar"] = new( new Color( 1f, 0.7f, 0.4f ), 0.045f, 5500f, 1.03f, 1.05f, new Color( 1f, 0.9f, 0.75f ) ),
		["brig"] = new( new Color( 1f, 0.12f, 0.08f ), 0.08f, 6300f, 0.9f, 1.08f, new Color( 1f, 0.7f, 0.65f ) ),
		["medbay"] = new( new Color( 0.3f, 1f, 0.85f ), 0.045f, 7400f, 0.97f, 1.05f, new Color( 0.85f, 1f, 0.95f ) ),     // teal, lighter: it was a green fog
		["engineering"] = new( new Color( 1f, 0.55f, 0.25f ), 0.045f, 5800f, 1f, 1.08f, new Color( 1f, 0.82f, 0.65f ) ),
		["temple"] = new( new Color( 1f, 0.82f, 0.5f ), 0.03f, 6100f, 1f, 1.03f, new Color( 1f, 0.95f, 0.85f ) ),
		["corridor"] = new( new Color( 0.7f, 0.82f, 1f ), 0.02f, 6900f, 0.98f, 1.04f, Color.White ),
		// planets (STYLE_GUIDE.md): picked by the planet you're on, over the ship's area profile (Code/Feel thread)
		["tatooine"] = new( new Color( 1f, 0.78f, 0.5f ), 0.035f, 5600f, 1.04f, 1.06f, new Color( 1f, 0.92f, 0.78f ) ),
		["kamino"] = new( new Color( 0.55f, 0.7f, 0.9f ), 0.04f, 7600f, 0.88f, 1.04f, new Color( 0.85f, 0.92f, 1f ) ),
		["hoth"] = new( new Color( 0.75f, 0.88f, 1f ), 0.03f, 8000f, 0.95f, 1.05f, new Color( 0.92f, 0.96f, 1f ) ),
	};

	/// <summary> The tint the overlay draws this frame (alpha = strength). </summary>
	public static Color OverlayTint { get; private set; } = Color.Transparent;

	Color _tint = new( 0.82f, 0.9f, 1f );
	float _strength = 0.03f;
	float _sat = 1f, _contrast = 1f, _temp = 6500f;
	Color _glow = Color.White;
	float _focus;
	float _drain;
	bool _shownEnabled = true;

	[ConCmd( "swrp_postfx_info", Help = "Print the camera's post-processing state: area, grade, alert tint" )]
	public static void Info()
	{
		Log.Info( $"[SWRP] postfx {(On ? "on" : "off")}, area {SwrpAmbience.CurrentProfile}, tint {OverlayTint}; grading modes: {string.Join( ", ", Enum.GetNames( typeof( ColorGrading.GradingType ) ) )}" );
	}

	T Ensure<T>() where T : Component, new()
	{
		return Components.GetOrCreate<T>( FindMode.EverythingInSelf );
	}

	protected override void OnUpdate()
	{
		SwrpShake.UpdateEars( Time.Delta );
		var bloom = GetComponent<Bloom>();
		var grain = GetComponent<FilmGrain>();
		var vignette = GetComponent<Vignette>();
		var chroma = GetComponent<ChromaticAberration>();
		var adjust = Ensure<ColorAdjustments>();
		var dof = Ensure<DepthOfField>();
		var grade = Ensure<ColorGrading>();

		if ( On != _shownEnabled )
		{
			_shownEnabled = On;
			foreach ( var c in new Component[] { bloom, vignette, chroma, adjust, dof, grade } )
				if ( c.IsValid() ) c.Enabled = On;
		}
		if ( !On )
		{
			OverlayTint = Color.Transparent;
			return;
		}

		// the area's grade (the alert level no longer tints the screen: the HUD banner shows it, Angelo 2026-10-12)
		var preview = SwrpPlayer.Local is null && SwrpAnimMannequin.CameraPlaced;
		var profile = preview ? SwrpAmbientZone.ProfileAt( Scene, Scene.Camera.WorldPosition ) : SwrpAmbience.CurrentProfile;
		var g = Grades.TryGetValue( profile ?? "corridor", out var found ) ? found : Grades["corridor"];
		// simple lighting: one clean, neutral white look everywhere (Angelo, 2026-10-12: no amber anywhere; calm and safe like spawn)
		if ( LightZones.Simple ) g = Neutral;
		var tint = g.Tint;
		var strength = g.Strength;
		var temp = g.TempK;
		if ( SwrpPlayer.Local is null && !preview ) strength = 0f;     // the character screen has its own look

		// the character screen's hangar is graded like a film still: a little less colour, a little more contrast, darker edges
		var stage = SwrpPlayer.Local is null && SwrpMenuStage.Showing;
		var k = Math.Min( 1f, Time.Delta * 1.5f );
		_tint = Color.Lerp( _tint, tint, k );
		_strength = _strength.LerpTo( strength, k );
		_sat = _sat.LerpTo( stage ? 0.85f : g.Saturation, k );
		_contrast = _contrast.LerpTo( stage ? 1.1f : g.Contrast, k );
		_temp = Math.Clamp( _temp, 4000f, 12000f );     // (a fresh field after a hotload starts at 0)
		_temp = _temp.LerpTo( SwrpPlayer.Local is null && !preview ? 6500f : temp, k );
		_glow = Color.Lerp( _glow, g.Glow, k );
		OverlayTint = _tint.WithAlpha( _strength );

		grade.GradingMethod = ColorGrading.GradingType.TemperatureControl;
		grade.ColorTempK = _temp;
		grade.BlendFactor = 1f;
		// dead: the colour drains away; ears ringing: the edges close in a little
		var dead = SwrpPlayer.Local?.GetComponent<SwrpVitals>() is { IsDead: true };
		_drain = _drain.LerpTo( dead ? 1f : 0f, Time.Delta * ( dead ? 0.6f : 3f ) );
		adjust.Saturation = _sat * ( 1f - 0.75f * _drain );
		adjust.Contrast = _contrast;
		if ( bloom.IsValid() ) { bloom.Tint = _glow; bloom.Strength = 1f; bloom.Threshold = 0.75f; }     // the strips and screens glow against the darker ship (2026-10-12)
		if ( grain.IsValid() && grain.Enabled ) grain.Enabled = false;      // no film grain (Angelo, 2026-10-12)
		if ( vignette.IsValid() ) { vignette.Intensity = ( stage ? 0.35f : 0.2f ) + 0.25f * _drain + 0.2f * SwrpShake.Ringing; vignette.Smoothness = 0.85f; vignette.Color = Color.Lerp( Color.Black, _tint * 0.25f, 0.4f ).WithAlpha( 1f ); }

		// aiming down the sights: focus on what's under the crosshair, the background softens a little
		var blaster = SwrpPlayer.Local?.GetComponent<SwrpBlaster>();
		var aim = blaster?.AimBlend ?? 0f;
		var third = SwrpPlayer.Local?.Controller is { ThirdPerson: true };
		_focus = _focus.LerpTo( third ? 0f : aim, Time.Delta * 8f );
		if ( stage && SwrpMenuStage.FocusDistance > 0f )
		{
			// the hangar's long lens: sharp on the trooper, the bay behind soft (SwrpMenuStage racks it on a new pick)
			dof.Enabled = true;
			dof.FocalDistance = SwrpMenuStage.FocusDistance;
			dof.FocusRange = SwrpMenuStage.FocusRange;
			dof.BlurSize = SwrpMenuStage.FocusBlur;
			dof.FrontBlur = false;
			dof.BackBlur = true;
			return;
		}
		dof.Enabled = _focus > 0.02f;
		if ( dof.Enabled )
		{
			var cam = Scene.Camera;
			var tr = Scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 4000f ).IgnoreGameObjectHierarchy( SwrpPlayer.Local.GameObject ).WithoutTags( "trigger" ).Run();
			dof.FocalDistance = dof.FocalDistance.LerpTo( tr.Distance, Time.Delta * 10f );
			dof.FocusRange = 300f + tr.Distance * 0.5f;
			dof.BlurSize = 5f * _focus;
			dof.FrontBlur = false;
			dof.BackBlur = true;
		}
	}
}
