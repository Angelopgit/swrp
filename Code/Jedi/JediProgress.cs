using System;

namespace Swrp.Jedi;

/// <summary> Something a Jedi can learn with skill points: a form, a technique or a Force power. </summary>
public sealed record JediSkill( string Id, string Title, string Branch, string Description, int Level, int Cost = 1, string Requires = null, bool Starting = false );

/// <summary> A Jedi's training: level, experience, unspent points, what they have learned, and the form they fight in. </summary>
public sealed class JediRecord
{
	public int Version { get; set; } = 1;
	public int Level { get; set; } = 1;
	public int Xp { get; set; }
	public int Points { get; set; }
	public List<string> Skills { get; set; } = new();
	public string Form { get; set; } = "shiicho";
	public string Selected { get; set; } = "push";
}

/// <summary>
/// Levels and the skill tree, and the host's per-character save of them. XP comes from fighting (saber damage, kills,
/// parries, deflections, blocks, Force powers); every level gives a skill point. Saved per character on the host under
/// the s&amp;box data folder (swrp/jedi/&lt;steamid&gt;_&lt;character&gt;.json), written to a temporary file, read back and checked
/// before it replaces the save, with the previous good save kept beside it, so a crash or a full disk mid-write never
/// loses a Jedi's training.
/// </summary>
public static class JediProgress
{
	public const int MaxLevel = 30;
	const string Folder = "swrp/jedi";

	public const string BranchForms = "FORMS", BranchTech = "TECHNIQUES", BranchForce = "THE FORCE";

	public static readonly JediSkill[] Skills =
	{
		new( "form_shiicho", "Shii-Cho", BranchForms, "Form I. Wide, balanced cuts.", 1, 0, Starting: true ),
		new( "form_makashi", "Makashi", BranchForms, "Form II. One-handed duelling: precise, quick, the widest parry window.", 3 ),
		new( "form_soresu", "Soresu", BranchForms, "Form III. Defence: cheap blocks, true deflections.", 3 ),
		new( "form_ataru", "Ataru", BranchForms, "Form IV. Acrobatic chains of fast cuts.", 6, 1, "form_shiicho" ),
		new( "form_djemso", "Shien / Djem So", BranchForms, "Form V. Power and punishing ripostes.", 6, 1, "form_soresu" ),
		new( "form_niman", "Niman", BranchForms, "Form VI. Moderate, blending blade and Force: powers cost a quarter less.", 5, 1, "form_shiicho" ),
		new( "form_juyo", "Juyo", BranchForms, "Form VII. Ferocious, relentless, costly.", 12, 2, "form_djemso" ),

		new( "tech_focus", "Inner Focus", BranchTech, "+25 maximum focus.", 2 ),
		new( "tech_riposte", "Riposte", BranchTech, "The swing after a perfect parry hits 25% harder.", 4 ),
		new( "tech_deflect", "Deflection", BranchTech, "Bolts that hit you from the front while your saber is lit and you aren't attacking get deflected without guarding (40%).", 4 ),
		new( "tech_redirect", "Redirection", BranchTech, "Deflected bolts fly much truer.", 8, 1, "tech_deflect" ),
		new( "tech_throw", "Saber Throw", BranchTech, "Throw your lightsaber (middle mouse) and call it back.", 5 ),
		new( "tech_heavy", "Heavy Strikes", BranchTech, "Heavy attacks hit 15% harder and break guards faster.", 7 ),
		new( "tech_flow", "Flowing Chains", BranchTech, "Each step of a combo chain is 6% faster than the last.", 9 ),

		new( "force_push", "Force Push", BranchForce, "Throw everything in front of you back.", 1, 0, Starting: true ),
		new( "force_leap", "Force Leap", BranchForce, "A great Force-aided jump, and soft landings.", 1, 0, Starting: true ),
		new( "force_pull", "Force Pull", BranchForce, "Drag a target to you and tear the blaster from their hands.", 3 ),
		new( "force_speed", "Force Speed", BranchForce, "Move at a run beyond running for a few seconds.", 4 ),
		new( "force_heal", "Force Heal", BranchForce, "Mend your wounds; with Healing Touch, an ally's.", 5 ),
		new( "force_pool", "Force Attunement", BranchForce, "+30 maximum Force points and faster recovery.", 6 ),
		new( "force_push2", "Force Repulse", BranchForce, "Push knocks people off their feet.", 8, 1, "force_push" ),
		new( "force_heal2", "Healing Touch", BranchForce, "Force Heal works on the ally you look at.", 10, 1, "force_heal" ),
		new( "force_lightning", "Force Lightning", BranchForce, "A dark path. Only where the server allows it (swrp_jedi_lightning).", 15, 2, "force_pool" ),
	};

	public static JediSkill Skill( string id ) => Skills.FirstOrDefault( s => s.Id == id );

	/// <summary> Experience needed to go from <paramref name="level"/> to the next. </summary>
	public static int XpToNext( int level ) => (int)( 120f * MathF.Pow( Math.Max( 1, level ), 1.55f ) );

	public static IEnumerable<string> StartingSkills => Skills.Where( s => s.Starting ).Select( s => s.Id );

	/// <summary> Whether a skill can be learned now (and why not). </summary>
	public static bool CanLearn( JediRecord r, JediSkill s, out string why )
	{
		why = null;
		if ( r.Skills.Contains( s.Id ) ) { why = "Already learned."; return false; }
		if ( r.Level < s.Level ) { why = $"Needs level {s.Level}."; return false; }
		if ( s.Requires is { } req && !r.Skills.Contains( req ) ) { why = $"Needs {Skill( req )?.Title ?? req} first."; return false; }
		if ( r.Points < s.Cost ) { why = s.Cost == 1 ? "Needs a skill point." : $"Needs {s.Cost} skill points."; return false; }
		return true;
	}

	// ----- host save and load -----

	static readonly Dictionary<string, JediRecord> _cache = new();

	static string KeyFor( ulong steamId, string characterId ) => $"{steamId}_{Clean( characterId )}";
	static string Clean( string s ) => new( ( s ?? "none" ).Where( c => char.IsLetterOrDigit( c ) || c == '-' || c == '_' ).Take( 64 ).ToArray() );

	/// <summary> Host: a character's training, loaded (or begun) on first use. </summary>
	public static JediRecord Load( ulong steamId, string characterId )
	{
		var key = KeyFor( steamId, characterId );
		if ( _cache.TryGetValue( key, out var cached ) ) return cached;
		var r = Read( $"{Folder}/{key}.json" ) ?? Read( $"{Folder}/{key}.previous.json" ) ?? new JediRecord();
		Repair( r );
		_cache[key] = r;
		return r;
	}

	static JediRecord Read( string path )
	{
		try
		{
			if ( !FileSystem.Data.FileExists( path ) ) return null;
			var text = FileSystem.Data.ReadAllText( path );
			if ( string.IsNullOrWhiteSpace( text ) ) return null;
			return Json.Deserialize<JediRecord>( text );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] jedi save {path} unreadable: {e.Message}" );
			return null;
		}
	}

	/// <summary> Keep a loaded record sane: levels in range, starting skills present, nothing unknown. </summary>
	static void Repair( JediRecord r )
	{
		r.Level = Math.Clamp( r.Level, 1, MaxLevel );
		r.Xp = Math.Max( 0, r.Xp );
		r.Points = Math.Clamp( r.Points, 0, MaxLevel * 3 );
		r.Skills ??= new();
		r.Skills = r.Skills.Where( id => Skill( id ) is not null ).Distinct().ToList();
		foreach ( var id in StartingSkills )
			if ( !r.Skills.Contains( id ) ) r.Skills.Add( id );
		if ( !r.Skills.Contains( "form_" + r.Form ) ) r.Form = "shiicho";
		r.Selected ??= "push";
	}

	/// <summary> Host: write a character's training to disk, safely. </summary>
	public static void Save( ulong steamId, string characterId )
	{
		var key = KeyFor( steamId, characterId );
		if ( !_cache.TryGetValue( key, out var r ) ) return;
		var path = $"{Folder}/{key}.json";
		var temp = $"{Folder}/{key}.tmp";
		try
		{
			FileSystem.Data.CreateDirectory( Folder );
			var json = Json.Serialize( r );
			FileSystem.Data.WriteAllText( temp, json );
			// read it back: only a save that parses replaces the good one
			if ( Read( temp ) is null ) { Log.Warning( $"[SWRP] jedi save for {key} didn't verify; kept the old one" ); return; }
			if ( FileSystem.Data.FileExists( path ) ) FileSystem.Data.WriteAllText( $"{Folder}/{key}.previous.json", FileSystem.Data.ReadAllText( path ) );
			FileSystem.Data.WriteAllText( path, json );
			FileSystem.Data.DeleteFile( temp );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] couldn't save jedi training {key}: {e.Message}" );
		}
	}

	/// <summary> Host: add experience; returns the number of levels gained. </summary>
	public static int AddXp( JediRecord r, int amount )
	{
		if ( amount <= 0 || r.Level >= MaxLevel ) return 0;
		r.Xp += amount;
		var gained = 0;
		while ( r.Level < MaxLevel && r.Xp >= XpToNext( r.Level ) )
		{
			r.Xp -= XpToNext( r.Level );
			r.Level++;
			r.Points++;
			gained++;
		}
		if ( r.Level >= MaxLevel ) r.Xp = 0;
		return gained;
	}
}
