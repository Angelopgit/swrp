using System;

namespace Swrp;

/// <summary>
/// How a non-clone character looks (the Jedi Order's padawans and knights): body, face, skin, hair, eyes and robes. Saved
/// on the character as a short text ("sex=1;face=2;..."), synced to everyone and worn by the body through
/// SwrpOutfit. Every option is an index into the lists here, so adding a hair style or colour is adding to a list.
/// </summary>
public sealed partial class Appearance
{
	public static readonly string[] Bodies = { "Male", "Female" };
	// the face and robes are the BF2 Jedi heads and outfits (SwrpOutfit.JediHeads / JediRobes, same order); the other lists are
	// kept for saved characters but the BF2 models have no way to show them yet, so the enlist menu doesn't offer them
	public static readonly string[] Faces = { "Bearded, auburn", "Young, long hair", "Elder, grey beard" };
	public static readonly string[] Skins = { "Fair", "Light", "Olive", "Tan", "Brown", "Dark" };
	public static readonly string[] HairStyles = { "Padawan cut and braid", "Short crop", "Long", "Ponytail", "Topknot bun", "Shaved" };
	public static readonly string[] HairColors = { "Black", "Dark brown", "Brown", "Auburn", "Blonde", "Grey", "Red" };
	public static readonly string[] Eyes = { "Brown", "Blue", "Green", "Grey", "Amber" };
	public static readonly string[] Robes = { "Tunic and tabards", "Tunic and travel robe", "Clone Wars armour", "Dark tunic", "Dark tunic and cloak", "Blue-grey campaign tunic" };

	public int Body { get; set; }
	public int Face { get; set; }
	public int Skin { get; set; } = 1;
	public int Hair { get; set; }
	public int HairColor { get; set; } = 1;
	public int Eye { get; set; }
	public int Robe { get; set; }

	public bool Female => Body == 1;

	public override string ToString() => $"body={Body};face={Face};skin={Skin};hair={Hair};haircol={HairColor};eye={Eye};robe={Robe}" + SaberText();

	public static Appearance Parse( string text )
	{
		var a = new Appearance();
		foreach ( var kv in ( text ?? "" ).Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var parts = kv.Split( '=' );
			if ( parts.Length != 2 || !int.TryParse( parts[1], out var v ) ) continue;
			switch ( parts[0] )
			{
				case "body": a.Body = Math.Clamp( v, 0, Bodies.Length - 1 ); break;
				case "face": a.Face = Math.Clamp( v, 0, Faces.Length - 1 ); break;
				case "skin": a.Skin = Math.Clamp( v, 0, Skins.Length - 1 ); break;
				case "hair": a.Hair = Math.Clamp( v, 0, HairStyles.Length - 1 ); break;
				case "haircol": a.HairColor = Math.Clamp( v, 0, HairColors.Length - 1 ); break;
				case "eye": a.Eye = Math.Clamp( v, 0, Eyes.Length - 1 ); break;
				case "robe": a.Robe = Math.Clamp( v, 0, Robes.Length - 1 ); break;
				default: a.ParseSaber( parts[0], v ); break;      // the lightsaber (Code/Jedi/SaberDesign.cs)
			}
		}
		return a;
	}

	/// <summary> Clean up text from a client (anything unknown or out of range is dropped). </summary>
	public static string Sanitize( string text ) => Parse( text ).ToString();
}
