using System;

namespace Swrp
{
/// <summary> The lightsaber a Jedi built in the character screen, saved in the same Appearance text as their looks. </summary>
public sealed partial class Appearance
{
	public int SaberEmitter { get; set; }
	public int SaberGrip { get; set; }
	public int SaberPommel { get; set; }
	public int SaberFinish { get; set; }
	public int SaberCrystal { get; set; }
	public int SaberMode { get; set; }
	public int SaberLength { get; set; } = 1;

	public Jedi.SaberDesign Saber => new( SaberEmitter, SaberGrip, SaberPommel, SaberFinish, SaberCrystal, SaberMode, SaberLength );

	/// <summary> The looks without the lightsaber: what the outfit is chosen from (a new saber doesn't rebuild the body). </summary>
	public string BodyKey => $"body={Body};face={Face};skin={Skin};hair={Hair};haircol={HairColor};eye={Eye};robe={Robe}";

	string SaberText() => $";s_em={SaberEmitter};s_gr={SaberGrip};s_po={SaberPommel};s_fi={SaberFinish};s_cr={SaberCrystal};s_mo={SaberMode};s_ln={SaberLength}";

	void ParseSaber( string key, int v )
	{
		switch ( key )
		{
			case "s_em": SaberEmitter = Math.Clamp( v, 0, Jedi.SaberDesign.Emitters.Length - 1 ); break;
			case "s_gr": SaberGrip = Math.Clamp( v, 0, Jedi.SaberDesign.Grips.Length - 1 ); break;
			case "s_po": SaberPommel = Math.Clamp( v, 0, Jedi.SaberDesign.Pommels.Length - 1 ); break;
			case "s_fi": SaberFinish = Math.Clamp( v, 0, Jedi.SaberDesign.Finishes.Length - 1 ); break;
			case "s_cr": SaberCrystal = Math.Clamp( v, 0, Jedi.SaberDesign.Crystals.Length - 1 ); break;
			case "s_mo": SaberMode = Math.Clamp( v, 0, Jedi.SaberDesign.Modes.Length - 1 ); break;
			case "s_ln": SaberLength = Math.Clamp( v, 0, Jedi.SaberDesign.Lengths.Length - 1 ); break;
		}
	}
}
}

namespace Swrp.Jedi
{
	/// <summary> One kyber crystal: the blade's colour and how it changes the saber's feel. </summary>
	public sealed record SaberCrystal( string Name, string Note, Color Blade, float Damage = 1f, float Speed = 1f, float Block = 1f, float Parry = 1f, float FocusRegen = 1f, float Deflect = 1f, float HumPitch = 1f );

	/// <summary> What the hilt and blade add up to: every number is a multiplier on the form's own. </summary>
	public readonly record struct SaberStats( float Damage, float Speed, float Block, float Parry, float FocusRegen, float Deflect, float Reach );

	/// <summary>
	/// A Jedi's lightsaber: hilt parts, finish, crystal, single / dual / staff, and blade length. Sizes are kept to what a
	/// real hilt and blade would be against a 72-unit (1.83 m) body: a single hilt is 11 units (28 cm), its blade 34-39 units
	/// (0.86-0.99 m); the shoto in the off hand is 8.5 and 23; a staff's hilt is 21 units (53 cm) with two 31-35 unit blades.
	/// All scaled with the body (swrp_player_scale).
	/// </summary>
	public readonly record struct SaberDesign( int Emitter, int Grip, int Pommel, int Finish, int Crystal, int Mode, int Length )
	{
		public static readonly string[] Emitters = { "Shrouded", "Crowned", "Flared", "Ringed", "Slotted", "Vented", "Banded", "Stepped", "Caged", "Scavenged" };
		public static readonly string[] Grips = { "Ridged", "Leather-wrapped", "Ribbed rubber", "Knurled", "Finned", "Smooth banded" };
		public static readonly string[] Pommels = { "Flat cap", "Domed", "Spiked", "Belt ring", "Weighted", "Tapered" };
		public static readonly string[] Finishes = { "Polished silver", "Black anodised", "Brass", "Bronze and chrome", "Two-tone", "White ceramic" };
		public static readonly string[] Modes = { "Single blade", "Dual (with shoto)", "Saberstaff" };
		public static readonly string[] Lengths = { "Short", "Standard", "Long" };

		public const int Single = 0, Dual = 1, Staff = 2;

		/// <summary> No red: those are bled crystals, and nobody in the Order carries one. </summary>
		public static readonly SaberCrystal[] Crystals =
		{
			new( "Ilum blue", "Balanced. The Guardian's colour.", new Color( 0.22f, 0.5f, 1f ) ),
			new( "Ilum green", "Calm: focus comes back 15% faster, blocks a little firmer.", new Color( 0.2f, 1f, 0.3f ), Block: 1.05f, FocusRegen: 1.15f, HumPitch: 0.97f ),
			new( "Sentinel yellow", "Light and quick: 8% faster swings, 5% less damage.", new Color( 1f, 0.82f, 0.18f ), Damage: 0.95f, Speed: 1.08f, HumPitch: 1.05f ),
			new( "Adegan violet", "Aggressive: 10% more damage, 10% weaker blocks.", new Color( 0.52f, 0.26f, 1f ), Damage: 1.1f, Speed: 0.98f, Block: 0.9f, HumPitch: 0.92f ),
			new( "Jedha cyan", "Precise: a 25% wider parry window.", new Color( 0.15f, 0.92f, 1f ), Parry: 1.25f, HumPitch: 1.02f ),
			new( "Bright amber", "Steady: deflected bolts fly truer.", new Color( 1f, 0.48f, 0.08f ), Deflect: 1.35f, HumPitch: 0.95f ),
			new( "Purified white", "Defensive: 15% stronger blocks, 5% less damage.", new Color( 0.92f, 0.94f, 1f ), Damage: 0.95f, Block: 1.15f, HumPitch: 1.03f ),
		};

		public static SaberDesign Default => new( 0, 0, 0, 0, 0, Single, 1 );

		public SaberCrystal CrystalDef => Crystals[Math.Clamp( Crystal, 0, Crystals.Length - 1 )];
		public Color BladeColor => CrystalDef.Blade;
		public bool IsStaff => Mode == Staff;
		public bool IsDual => Mode == Dual;

		float LengthScale => Length switch { 0 => 0.9f, 2 => 1.03f, _ => 0.97f };

		/// <summary> Hilt length in model units (before the body scale). </summary>
		public float HiltLength => IsStaff ? 21f : 11f;
		/// <summary> Blade length in model units (each blade of a staff). </summary>
		public float BladeLength => ( IsStaff ? 34f : 38f ) * LengthScale;
		public const float ShotoHilt = 8.5f;
		public const float ShotoBlade = 23f;

		public SaberStats Stats
		{
			get
			{
				var c = CrystalDef;
				var len = Length switch { 0 => ( 0.92f, 1.05f ), 2 => ( 1.06f, 0.96f ), _ => ( 1f, 1f ) };
				var mode = Mode switch
				{
					Dual => ( Damage: 0.9f, Speed: 1.12f, Block: 0.92f ),
					Staff => ( Damage: 1.05f, Speed: 0.94f, Block: 1.1f ),
					_ => ( Damage: 1f, Speed: 1f, Block: 1f ),
				};
				return new SaberStats( c.Damage * mode.Damage, c.Speed * mode.Speed * len.Item2, c.Block * mode.Block, c.Parry, c.FocusRegen, c.Deflect, len.Item1 );
			}
		}

		public static SaberDesign From( string appearance ) => string.IsNullOrEmpty( appearance ) ? Default : Appearance.Parse( appearance ).Saber;

		/// <summary> A key for caching built hilt models: only the parts that change the hilt's shape and colour. </summary>
		public string HiltKey => $"{Emitter}.{Grip}.{Pommel}.{Finish}.{Crystal}.{Mode}";
	}
}
