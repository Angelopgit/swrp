namespace Swrp;

/// <summary>
/// The named commanders' playermodels (Angelo, 2026-10-16): the Commander of the 104th wears Commander Wolffe and the
/// Commander of the 501st Captain Rex, Galactic Contention's Phase II models put on the trooper rig like every other body
/// (ModelSource/gc_commanders.py; the GC team's work, local only). A battalion's Commander is whoever holds its Commander
/// post (staff set it, SwrpBattalions) or the Commander rank. Battalions without an entry keep the class model.
/// </summary>
public static class SwrpCommanders
{
	public static readonly Dictionary<string, string> Models = new()
	{
		["104th"] = "models/troopers/gc_cmdr_wolffe.vmdl",
		["501st"] = "models/troopers/gc_cmdr_rex.vmdl",
	};

	/// <summary> The commander model this trooper wears, or null (not a Commander, no model for the battalion, an event character). </summary>
	public static string ModelFor( SwrpPlayer p )
	{
		if ( p is null || p.EventDef is not null || string.IsNullOrEmpty( p.FactionId ) ) return null;
		if ( !p.BattalionCommander && p.RankIndex < SwrpRanks.CommanderRank ) return null;
		return Models.TryGetValue( p.FactionId, out var path ) ? path : null;
	}

	/// <summary>
	/// The model, in the same variants as a class's (ClassDefinition.LoadModel): "_squad" (the Squad movement set) with a weapon
	/// out or prone, "_drill" (the clone set) otherwise; null when it isn't in the project, so the class model stays.
	/// </summary>
	public static Model Load( string path, bool squad, string gmod = null )
	{
		// stock: the commander's GMod set ("_gmod_<hold>"), as a class's (ClassDefinition.LoadGmod)
		if ( gmod is not null && ClassDefinition.GmodAnims && path?.EndsWith( ".vmdl" ) == true && FileSystem.Mounted.FileExists( path[..^5] + $"_gmod_{gmod}.vmdl_c" )
			&& Model.Load( path[..^5] + $"_gmod_{gmod}.vmdl" ) is { IsError: false } g )
			return g;
		if ( string.IsNullOrEmpty( path ) || !path.EndsWith( ".vmdl" ) ) return null;
		var stem = path[..^5];
		var tries = new List<string>();
		if ( squad && ClassDefinition.Custom ) tries.Add( stem + "_squad.vmdl" );
		if ( ClassDefinition.Custom ) tries.Add( stem + "_drill.vmdl" );
		tries.Add( path );
		foreach ( var t in tries )
			if ( FileSystem.Mounted.FileExists( t + "_c" ) && Model.Load( t ) is { IsError: false } m )
				return m;
		return null;
	}
}
