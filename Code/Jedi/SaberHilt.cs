using System;

namespace Swrp.Jedi;

/// <summary>
/// Lightsaber hilts: the Battlefront II hero hilts (ModelSource/bf2_props.py; local only, never publish), one per emitter
/// style in the designer, Maul's for a staff and Yoda's for the off-hand shoto, tinted by the design's finish.
/// Model space: the hilt runs along +X from the pommel's end (x = 0) to the emitter's mouth (x = the design's hilt length);
/// +Z is the front (the activation stud, under the thumb).
/// </summary>
public static class SaberHilt
{
	public const string DefaultModel = "models/jedi/hilt_obiwan.vmdl";
	public const string StaffModel = "models/jedi/hilt_maul.vmdl";
	public const string ShotoModel = "models/jedi/hilt_shoto.vmdl";

	// by SaberDesign.Emitters: Shrouded, Crowned, Flared, Ringed, Slotted, Vented, then (2026-10-13, more BF2 hero hilts:
	// the Jedi sabers Grievous carries, Luke's, Vader's, Rey's) Banded, Stepped, Caged, Scavenged
	static readonly string[] ByEmitter =
	{
		"models/jedi/hilt_obiwan.vmdl", "models/jedi/hilt_anakin.vmdl", "models/jedi/hilt_dooku.vmdl",
		"models/jedi/hilt_yoda.vmdl", "models/jedi/hilt_grievous2.vmdl", "models/jedi/hilt_grievous1.vmdl",
		"models/jedi/hilt_grievous3.vmdl", "models/jedi/hilt_luke.vmdl", "models/jedi/hilt_vader.vmdl", "models/jedi/hilt_rey.vmdl",
	};

	// by SaberDesign.Finishes: polished silver, black anodised, brass, bronze and chrome, two-tone, white ceramic
	static readonly Color[] FinishTints =
	{
		Color.White, new( 0.28f, 0.28f, 0.3f ), new( 1f, 0.82f, 0.5f ), new( 0.92f, 0.68f, 0.45f ), new( 0.8f, 0.8f, 0.85f ), new( 1.15f, 1.12f, 1.08f ),
	};

	/// <summary> The main hilt for a design (a staff's whole double hilt), or the off-hand shoto's. </summary>
	public static Model Get( SaberDesign d, bool shoto = false ) => Model.Load( PathFor( d, shoto ) );

	public static string PathFor( SaberDesign d, bool shoto = false )
	{
		if ( shoto ) return ShotoModel;
		if ( d.IsStaff ) return StaffModel;
		return ByEmitter[Math.Clamp( d.Emitter, 0, ByEmitter.Length - 1 )];
	}

	public static Color Tint( SaberDesign d ) => FinishTints[Math.Clamp( d.Finish, 0, FinishTints.Length - 1 )];
}
