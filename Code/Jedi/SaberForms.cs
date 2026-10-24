using System;

namespace Swrp.Jedi;

/// <summary> Which way a swing goes. Any = the way the mouse moved when you attacked. </summary>
public enum SwingDir
{
	Any = 0,
	/// <summary> From your right side across to your left. </summary>
	Left = 1,
	/// <summary> From your left side across to your right. </summary>
	Right = 2,
	/// <summary> Down from over your head. </summary>
	Overhead = 3,
	/// <summary> Straight ahead, point first. </summary>
	Thrust = 4,
	/// <summary> A full turn of the body with the blade out (a finisher). </summary>
	Spin = 5,
	/// <summary> Up from low on your right (a rising cut). </summary>
	Rising = 6,
}

/// <summary> One step of a form's combo chain. </summary>
public sealed class ComboStep
{
	[KeyProperty] public SwingDir Direction { get; set; } = SwingDir.Any;
	/// <summary> Speed of this swing against the form's tempo. </summary>
	public float Speed { get; set; } = 1f;
	/// <summary> Damage of this swing against the form's. </summary>
	public float Damage { get; set; } = 1f;
	/// <summary> How far the blade travels, in degrees (90-200; spins go round). </summary>
	public float Arc { get; set; } = 150f;

	public ComboStep() { }
	public ComboStep( SwingDir dir, float speed = 1f, float damage = 1f, float arc = 150f ) { Direction = dir; Speed = speed; Damage = damage; Arc = arc; }
}

/// <summary>
/// A lightsaber form (Shii-Cho, Makashi, Soresu, Ataru, Djem So, Juyo) as data: its combo chain, tempo, focus cost, how well
/// it blocks and parries, how it holds the blade at guard, and the authored animation set an animgraph would play for it
/// (form_id / the "anim set" names in Code/Jedi/ANIMGRAPH.md). Edit the .sform assets in Assets/data/jedi/forms; the
/// built-in copies below are used when an asset is missing.
/// </summary>
[AssetType( Name = "Lightsaber Form", Extension = "sform", Category = "SWRP" )]
public sealed class SaberForm : GameResource
{
	/// <summary>
	/// Which generation of form data this is. Bump <see cref="DataVersion"/> when fields are added and rerun
	/// ModelSource/jedi_forms.py: an asset older than the code is ignored (the built-in copy wins), since the editor can keep
	/// an asset it loaded before a field existed until it restarts.
	/// </summary>
	[Property] public int Version { get; set; }
	public const int DataVersion = 6;

	/// <summary> Saved on characters and sent over the network: never rename. </summary>
	[Property] public string Id { get; set; } = "shiicho";
	[Property] public string Title { get; set; } = "Shii-Cho";
	[Property] public string Numeral { get; set; } = "I";
	[Property] public string Description { get; set; } = "";
	/// <summary> The animgraph's form_id (0 Shii-Cho .. 6 Juyo, in canon numbering). </summary>
	[Property] public int FormId { get; set; }
	/// <summary> Animation set an authored graph plays for this form (see ANIMGRAPH.md). </summary>
	[Property] public string AnimationSet { get; set; } = "saber_shiicho";

	// ----- progression -----
	[Property] public int RequiredLevel { get; set; } = 1;
	/// <summary> Another form you must know first (empty for none). </summary>
	[Property] public string Requires { get; set; } = "";

	// ----- feel -----
	/// <summary> Swing speed (1 = a light swing's strike in about 0.22 s). </summary>
	[Property, Range( 0.5f, 1.6f )] public float Tempo { get; set; } = 1f;
	[Property, Range( 0.5f, 1.6f )] public float Damage { get; set; } = 1f;
	/// <summary> Focus each swing costs (heavies cost twice). </summary>
	[Property, Range( 0f, 30f )] public float FocusCost { get; set; } = 8f;
	/// <summary> Focus a blocked hit costs, against the default (lower blocks better). </summary>
	[Property, Range( 0.3f, 2f )] public float BlockCost { get; set; } = 1f;
	/// <summary> Perfect-parry window in seconds after raising your guard. </summary>
	[Property, Range( 0.05f, 0.4f )] public float ParryWindow { get; set; } = 0.16f;
	/// <summary> 0 = deflected bolts fly off anywhere ahead, 1 = straight back at the shooter. </summary>
	[Property, Range( 0f, 1f )] public float DeflectSkill { get; set; } = 0.5f;
	/// <summary> Bonus damage on the swing right after a perfect parry. </summary>
	[Property, Range( 1f, 2f )] public float Riposte { get; set; } = 1.25f;
	/// <summary> Walk speed while guarding. </summary>
	[Property, Range( 0.4f, 1f )] public float BlockMove { get; set; } = 0.75f;
	/// <summary> Pushing power in a blade lock. </summary>
	[Property, Range( 0.5f, 1.6f )] public float LockStrength { get; set; } = 1f;
	/// <summary> How far the arms carry the blade out from the body (a duellist's extension, a defender's tight circle). </summary>
	[Property, Range( 0.8f, 1.2f )] public float Reach { get; set; } = 1f;
	/// <summary> Force powers' cost in this form (Niman blends saber and Force). </summary>
	[Property, Range( 0.5f, 1.5f )] public float ForceCost { get; set; } = 1f;
	[Property] public List<ComboStep> Combo { get; set; } = new();
	/// <summary> After a light swing ends, how long the next one still counts as the next step of the chain. </summary>
	[Property, Range( 0.1f, 1f )] public float ChainWindow { get; set; } = 0.45f;

	// ----- guard pose (body space: x forward, y left, z up, on a 72-unit body) -----
	/// <summary> Where the hilt is held at guard, from the chest. </summary>
	[Property] public Vector3 GuardHand { get; set; } = new( 13f, -4f, -6f );
	/// <summary> Blade at guard: pitch up from forward (90 = straight up) and yaw (positive = to the left). </summary>
	[Property] public float GuardPitch { get; set; } = 55f;
	[Property] public float GuardYaw { get; set; } = 10f;
	/// <summary> One hand on the hilt (the other free, held out for balance). </summary>
	[Property] public bool OneHanded { get; set; }
	/// <summary> How far the body crouches into the stance (0..1). </summary>
	[Property, Range( 0f, 1f )] public float Crouch { get; set; } = 0.15f;

	// ----- the stance: where the feet go (body space units, on a 72-unit body) -----
	/// <summary> Feet apart side to side. </summary>
	[Property, Range( 4f, 18f )] public float StanceWidth { get; set; } = 10f;
	/// <summary> Front (right) foot ahead of the back one. </summary>
	[Property, Range( 0f, 20f )] public float StanceDepth { get; set; } = 9f;
	/// <summary> How side-on the hips turn (degrees, positive = sword side forward). </summary>
	[Property, Range( -10f, 70f )] public float StanceTurn { get; set; } = 15f;
	/// <summary> Hips forward of the feet (positive leans in, negative sits back). </summary>
	[Property, Range( -6f, 6f )] public float StanceShift { get; set; } = 0f;
	/// <summary> How far the upper body leans into the stance (degrees; a predator's hunch forward, a duellist's upright back). </summary>
	[Property, Range( -10f, 20f )] public float StanceBend { get; set; } = 3f;

	// ----- how it cuts -----
	/// <summary> How steep the side cuts run (0 = flat, 1 = a steep diagonal chop). </summary>
	[Property, Range( 0f, 1f )] public float CutTilt { get; set; } = 0.42f;
	/// <summary> How far the hands follow the blade round (lower = more of the cut from the wrists). </summary>
	[Property, Range( 0.4f, 1f )] public float WristLead { get; set; } = 0.78f;
	/// <summary> How much the body turns into a cut. </summary>
	[Property, Range( 0.3f, 1.6f )] public float BodyDrive { get; set; } = 1f;

	// ----- the guard raised (mouse 2) -----
	/// <summary> How far out from the chest the blocking blade is held. </summary>
	[Property, Range( 8f, 18f )] public float BlockOut { get; set; } = 13f;
	/// <summary> The blocking blade turned about the forward axis (positive = more upright, negative = flatter). </summary>
	[Property, Range( -40f, 40f )] public float BlockLean { get; set; } = 0f;
	/// <summary> The blocking hands raised (positive) or lowered. </summary>
	[Property, Range( -6f, 8f )] public float BlockHeight { get; set; } = 1f;

	public ComboStep Step( int index ) => Combo.Count == 0 ? new ComboStep() : Combo[Math.Clamp( index, 0, Combo.Count - 1 )];

	// ----- the registry -----

	static List<SaberForm> _builtIn;
	static int _builtInFor;
	const int BuiltInRevision = 8;

	/// <summary> Every form: the assets, with the built-in ones filling any gap. In order of the canon numbering. </summary>
	public static IReadOnlyList<SaberForm> All
	{
		get
		{
			// (rebuilt when the table changes: a hotload keeps static fields, so bump BuiltInRevision with any edit below)
			if ( _builtIn is null || _builtInFor != BuiltInRevision ) { _builtIn = BuiltIn(); _builtInFor = BuiltInRevision; }
			var assets = ResourceLibrary.GetAll<SaberForm>().Where( f => !string.IsNullOrEmpty( f.Id ) && f.Version >= DataVersion ).ToList();
			if ( assets.Count == 0 ) return _builtIn.OrderBy( f => f.FormId ).ToList();
			var list = new List<SaberForm>( assets );
			foreach ( var b in _builtIn )
				if ( !list.Any( a => a.Id == b.Id ) ) list.Add( b );
			return list.OrderBy( f => f.FormId ).ToList();
		}
	}

	public static SaberForm Get( string id ) => All.FirstOrDefault( f => f.Id == id ) ?? All[0];
	public static SaberForm Default => All[0];
	public static int IndexOf( string id ) => Math.Max( 0, All.ToList().FindIndex( f => f.Id == id ) );

	static List<SaberForm> BuiltIn() => new()
	{
		new SaberForm
		{
			Version = DataVersion, Id = "shiicho", Title = "Shii-Cho", Numeral = "I", FormId = 0, AnimationSet = "saber_shiicho", RequiredLevel = 1,
			Description = "The way of the sarlacc. The first form: the widest, cheapest sweeps, made for fighting many at once; its parries are the plainest.",
			Tempo = 1f, Damage = 1f, FocusCost = 6f, BlockCost = 1.05f, ParryWindow = 0.15f, DeflectSkill = 0.45f, Riposte = 1.2f, BlockMove = 0.75f, LockStrength = 1f,
			Combo = { new( SwingDir.Any, 1f, 1f, 175f ), new( SwingDir.Any, 1.05f, 1.05f, 185f ), new( SwingDir.Spin, 0.95f, 1.25f, 360f ) },
			GuardHand = new( 13f, -4f, -6f ), GuardPitch = 55f, GuardYaw = 10f, Crouch = 0.15f, Reach = 1.06f, StanceWidth = 10f, StanceDepth = 10f, StanceTurn = 18f, StanceShift = 0.5f, StanceBend = 3f, CutTilt = 0.4f, WristLead = 0.8f, BodyDrive = 1.1f, BlockOut = 13f, BlockLean = 0f, BlockHeight = 1f,
		},
		new SaberForm
		{
			Version = DataVersion, Id = "makashi", Title = "Makashi", Numeral = "II", FormId = 1, AnimationSet = "saber_makashi", RequiredLevel = 3,
			Description = "The way of the ysalamiri. The duellist's form: one hand, precise thrusts and quick cuts, the widest parry window against other blades.",
			Tempo = 1.12f, Damage = 0.92f, FocusCost = 6f, BlockCost = 0.9f, ParryWindow = 0.22f, DeflectSkill = 0.35f, Riposte = 1.45f, BlockMove = 0.85f, LockStrength = 0.9f,
			Combo = { new( SwingDir.Any, 1.1f, 0.95f, 110f ), new( SwingDir.Any, 1.15f, 1f, 110f ), new( SwingDir.Any, 1.15f, 1f, 120f ), new( SwingDir.Thrust, 1.05f, 1.35f, 0f ) },
			GuardHand = new( 16f, -6f, -2f ), GuardPitch = 12f, GuardYaw = 4f, OneHanded = true, Crouch = 0.1f, Reach = 1.12f, StanceWidth = 8.5f, StanceDepth = 16f, StanceTurn = 50f, StanceShift = 1f, StanceBend = -4f, CutTilt = 0.25f, WristLead = 0.55f, BodyDrive = 0.55f, BlockOut = 16f, BlockLean = -15f, BlockHeight = 3f,
		},
		new SaberForm
		{
			Version = DataVersion, Id = "soresu", Title = "Soresu", Numeral = "III", FormId = 2, AnimationSet = "saber_soresu", RequiredLevel = 3,
			Description = "The way of the mynock. Tight, economical defence: blocks cost far less focus and deflected bolts go where you look.",
			Tempo = 0.96f, Damage = 0.85f, FocusCost = 7f, BlockCost = 0.6f, ParryWindow = 0.18f, DeflectSkill = 0.95f, Riposte = 1.2f, BlockMove = 0.9f, LockStrength = 1.05f,
			Combo = { new( SwingDir.Any, 1f, 0.95f, 120f ), new( SwingDir.Any, 1.05f, 1f, 120f ) },
			GuardHand = new( 12f, -1f, -3f ), GuardPitch = 70f, GuardYaw = 25f, Crouch = 0.25f, Reach = 0.9f, StanceWidth = 11f, StanceDepth = 7f, StanceTurn = 22f, StanceShift = -1.5f, StanceBend = -1f, CutTilt = 0.5f, WristLead = 0.85f, BodyDrive = 0.75f, BlockOut = 10f, BlockLean = 25f, BlockHeight = 0f,
		},
		new SaberForm
		{
			Version = DataVersion, Id = "ataru", Title = "Ataru", Numeral = "IV", FormId = 3, AnimationSet = "saber_ataru", RequiredLevel = 6, Requires = "shiicho",
			Description = "The way of the hawk-bat. Acrobatic and relentless: long chains of fast cuts ending in a spin, but a thin guard.",
			Tempo = 1.2f, Damage = 0.95f, FocusCost = 11f, BlockCost = 1.25f, ParryWindow = 0.14f, DeflectSkill = 0.4f, Riposte = 1.25f, BlockMove = 0.8f, LockStrength = 0.9f,
			Combo = { new( SwingDir.Any, 1.1f, 0.9f, 150f ), new( SwingDir.Any, 1.15f, 0.95f, 160f ), new( SwingDir.Rising, 1.15f, 1f, 150f ), new( SwingDir.Any, 1.2f, 1f, 160f ), new( SwingDir.Spin, 1.05f, 1.3f, 360f ) },
			GuardHand = new( 9f, -7f, 4f ), GuardPitch = 80f, GuardYaw = -35f, Crouch = 0.3f, ForceCost = 0.9f, StanceWidth = 12f, StanceDepth = 12f, StanceTurn = 30f, StanceShift = 2f, StanceBend = 9f, CutTilt = 0.35f, WristLead = 0.75f, BodyDrive = 1.4f, BlockOut = 13f, BlockLean = -10f, BlockHeight = 2f,
		},
		new SaberForm
		{
			Version = DataVersion, Id = "djemso", Title = "Shien / Djem So", Numeral = "V", FormId = 4, AnimationSet = "saber_djemso", RequiredLevel = 6, Requires = "soresu",
			Description = "The way of the krayt dragon. Power: heavy blows that drive through guards, ripostes that punish, bolts sent back at the shooter.",
			Tempo = 0.88f, Damage = 1.25f, FocusCost = 10f, BlockCost = 0.85f, ParryWindow = 0.15f, DeflectSkill = 0.75f, Riposte = 1.6f, BlockMove = 0.7f, LockStrength = 1.35f,
			Combo = { new( SwingDir.Any, 0.95f, 1.1f, 150f ), new( SwingDir.Any, 1f, 1.15f, 150f ), new( SwingDir.Overhead, 0.9f, 1.45f, 160f ) },
			GuardHand = new( 11f, -3f, 6f ), GuardPitch = 72f, GuardYaw = 15f, Crouch = 0.2f, StanceWidth = 13f, StanceDepth = 13f, StanceTurn = 25f, StanceShift = 0f, StanceBend = 5f, CutTilt = 0.75f, WristLead = 0.85f, BodyDrive = 1.3f, BlockOut = 14f, BlockLean = -28f, BlockHeight = 5f,
		},
		new SaberForm
		{
			Version = DataVersion, Id = "juyo", Title = "Juyo", Numeral = "VII", FormId = 6, AnimationSet = "saber_juyo", RequiredLevel = 12, Requires = "djemso",
			Description = "The way of the vornskr. Ferocious and unpredictable: the fastest, hardest chains there are, at a heavy cost in focus and guard.",
			Tempo = 1.25f, Damage = 1.2f, FocusCost = 14f, BlockCost = 1.3f, ParryWindow = 0.12f, DeflectSkill = 0.5f, Riposte = 1.4f, BlockMove = 0.8f, LockStrength = 1.2f,
			Combo = { new( SwingDir.Any, 1.15f, 1.05f, 160f ), new( SwingDir.Any, 1.2f, 1.1f, 160f ), new( SwingDir.Rising, 1.25f, 1.1f, 150f ), new( SwingDir.Any, 1.25f, 1.15f, 170f ), new( SwingDir.Overhead, 1.1f, 1.25f, 170f ), new( SwingDir.Spin, 1.1f, 1.4f, 360f ) },
			GuardHand = new( 12f, -8f, -12f ), GuardPitch = -20f, GuardYaw = -30f, Crouch = 0.35f, Reach = 1.05f, ForceCost = 1.1f, StanceWidth = 14f, StanceDepth = 15f, StanceTurn = 40f, StanceShift = 2.5f, StanceBend = 14f, CutTilt = 0.6f, WristLead = 0.7f, BodyDrive = 1.35f, BlockOut = 12f, BlockLean = -18f, BlockHeight = -3f,
		},
		new SaberForm
		{
			Version = DataVersion, Id = "niman", Title = "Niman", Numeral = "VI", FormId = 5, AnimationSet = "saber_niman", RequiredLevel = 5, Requires = "shiicho",
			Description = "The way of the rancor. The diplomat's form: moderate in everything, the blade and the Force woven together. Tight cuts and a thrust, a forgiving rhythm to weave powers in; Force powers cost 30% less. Suits a shoto or a staff.",
			Tempo = 1f, Damage = 0.9f, FocusCost = 7f, BlockCost = 0.85f, ParryWindow = 0.18f, DeflectSkill = 0.65f, Riposte = 1.2f, BlockMove = 0.85f, LockStrength = 0.95f,
			Combo = { new( SwingDir.Any, 1f, 1f, 120f ), new( SwingDir.Thrust, 1.1f, 1.1f, 60f ), new( SwingDir.Rising, 1f, 1.2f, 130f ) },
			GuardHand = new( 12f, -5f, 0f ), GuardPitch = 40f, GuardYaw = 0f, Crouch = 0.15f, Reach = 0.95f, ForceCost = 0.7f, ChainWindow = 0.6f, StanceWidth = 9f, StanceDepth = 7f, StanceTurn = 10f, StanceShift = 0f, StanceBend = 1f, CutTilt = 0.42f, WristLead = 0.78f, BodyDrive = 1f, BlockOut = 12f, BlockLean = 5f, BlockHeight = 1f,
		},
	};
}
