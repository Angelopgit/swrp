namespace Swrp;

/// <summary>
/// What a body wears. Every playable model (the clone classes and the Jedi) is a Battlefront II model rebuilt on the one
/// universal rig (the Citizen skeleton and animgraph, ModelSource/bf2_clones.py), so animations, emotes, sabers and held
/// items work the same on all of them. A battalion is a material group on the clone models (its BF2 unit markings,
/// ModelSource/bf2_markings.py), named by the battalion's id; the default group is the plain clone. A Jedi's face is the
/// "head" bodygroup and their robes the "robe" bodygroup, picked from the Appearance's face and robes. Called every frame on every machine (cheap: it only sets what changed).
/// </summary>
public static class SwrpOutfit
{
	public const string JediModel = "models/troopers/clone_jedi.vmdl";

	/// <summary> The Jedi heads (the "head" bodygroup's choices), in the order bf2_clone_vmdl.py writes them. </summary>
	public static readonly string[] JediHeads = { "Kenobi", "Skywalker", "Dooku" };

	/// <summary> The Jedi robes (the "robe" bodygroup's choices): BF2's Kenobi and Skywalker outfits, same order as bf2_clone_vmdl.py. </summary>
	public static readonly string[] JediRobes = { "Kenobi 1", "Kenobi 2", "Kenobi 3", "Skywalker 1", "Skywalker 2", "Skywalker 3" };

	/// <summary> Sets the Jedi's head and robe bodygroups from an Appearance (works on a SkinnedModelRenderer or a SceneModel). </summary>
	public static (int Head, int Robe) JediParts( string appearance )
	{
		var a = Appearance.Parse( appearance );
		return ( a.Face % JediHeads.Length, a.Robe % JediRobes.Length );
	}

	/// <param name="firstPerson">Your own body while you look out of its eyes (the controller hides it then).</param>
	public static void Apply( SkinnedModelRenderer body, string factionId, bool firstPerson = false, string appearance = null )
	{
		if ( !body.IsValid() || body.Model is null || body.Model.IsError ) return;
		var group = GroupFor( body.Model, factionId );
		if ( body.MaterialGroup != group ) body.MaterialGroup = group;
		if ( appearance is not null && HasHeads( body.Model ) )
		{
			var (head, robe) = JediParts( appearance );
			if ( body.GetBodyGroup( "head" ) != head ) body.SetBodyGroup( "head", head );
			if ( body.GetBodyGroup( "robe" ) != robe ) body.SetBodyGroup( "robe", robe );
		}
	}

	static readonly Dictionary<Model, HashSet<string>> _groups = new();
	static readonly Dictionary<Model, bool> _heads = new();

	static bool HasHeads( Model model )
	{
		if ( !_heads.TryGetValue( model, out var has ) )
			_heads[model] = has = model.Parts.All.Any( p => p.Name == "head" );
		return has;
	}

	[ConCmd( "swrp_outfit_info", Help = "swrp_outfit_info - dev: every body in the scene with its model, material groups and the one it wears" )]
	public static void InfoCommand()
	{
		foreach ( var r in Game.ActiveScene?.GetAllComponents<SkinnedModelRenderer>() ?? Enumerable.Empty<SkinnedModelRenderer>() )
		{
			if ( r.Model is null || !r.Model.ResourcePath.Contains( "troopers" ) ) continue;
			var names = Enumerable.Range( 0, r.Model.MaterialGroupCount ).Select( i => r.Model.GetMaterialGroupName( i ) );
			Log.Info( $"[SWRP] {r.GameObject.Name}: {r.Model.ResourcePath} groups [{string.Join( ", ", names )}] wearing '{r.MaterialGroup}'" );
		}
	}

	/// <summary> The material group for a battalion on this model, or "default" when the model has none for it. </summary>
	public static string GroupFor( Model model, string factionId )
	{
		if ( string.IsNullOrEmpty( factionId ) ) return "default";
		// (a model recompiled in the editor keeps its object, so the cache checks the group count too)
		if ( !_groups.TryGetValue( model, out var names ) || names.Count != model.MaterialGroupCount )
		{
			names = new HashSet<string>();
			for ( var i = 0; i < model.MaterialGroupCount; i++ ) names.Add( model.GetMaterialGroupName( i ) );
			_groups[model] = names;
		}
		return names.Contains( factionId ) ? factionId : "default";
	}
}
