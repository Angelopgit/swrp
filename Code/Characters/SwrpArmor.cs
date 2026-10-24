using System;

namespace Swrp;

/// <summary>
/// The armour customiser's pieces: what a trooper can wear on top of their class's armour, one per slot, each earned by rank,
/// a battalion post, a qualification (SwrpCareer) or the battalion itself. Saved per character (CharacterRecord.Armor, a
/// comma list of part ids) and checked again by the host on every save and every deploy, so a demoted trooper loses what
/// their old rank gave them. The models are BF2 parts built on the trooper rig by ModelSource/bf2_armor.py and drawn
/// bone-merged onto the body (SwrpArmorLayer); the trooper models themselves are untouched.
/// </summary>
public static class SwrpArmorRegistry
{
	public sealed class Part
	{
		public string Id { get; init; }
		public string Name { get; init; }
		public string Slot { get; init; }
		public string Description { get; init; }
		/// <summary> Rank index that unlocks it (-1: rank alone never does). </summary>
		public int MinRank { get; init; } = -1;
		/// <summary> Qualifications (SwrpCareer) that unlock it in the trooper's battalion. </summary>
		public string[] Quals { get; init; } = Array.Empty<string>();
		/// <summary> A battalion post (Commander, XO, Jedi General) unlocks it. </summary>
		public bool Posts { get; init; }
		/// <summary> Only for these battalions (empty: any). </summary>
		public string[] Battalions { get; init; } = Array.Empty<string>();

		public string Model => $"models/troopers/armor/clone_armor_{Id}.vmdl";
	}

	public static readonly string[] Slots = { "shoulder", "kama", "gear", "head", "antenna", "hip" };

	public static string SlotName( string slot ) => slot switch
	{
		"kama" => "KAMA",
		"shoulder" => "PAULDRON",
		"gear" => "WEBBING",
		"head" => "HELMET GEAR",
		"antenna" => "ANTENNA",
		"hip" => "HOLSTER",
		_ => slot.ToUpperInvariant(),
	};

	// Rank ladder: 0 Cadet, 1 Private, 2 PFC, 3 Corporal, 4 Sergeant, 5 Staff Sergeant, 6 2nd Lt, 7 1st Lt, 8 Captain, 9 Major, 10 Commander
	public static readonly Part[] All =
	{
		new() { Id = "kama_arc", Name = "Kama", Slot = "kama", MinRank = 6, Quals = new[] { "arc" }, Posts = true, Description = "The split commando kama. Officers (2nd Lieutenant+) and ARC troopers." },
		new() { Id = "kama", Name = "Full kama", Slot = "kama", MinRank = 4, Quals = new[] { "officer", "heavy" }, Posts = true, Description = "A wrap-round half-skirt. Sergeants and up, officer candidates and heavy gunners." },
		new() { Id = "gear_212", Name = "Pauldron and bandolier", Slot = "shoulder", MinRank = 6, Quals = new[] { "arc" }, Posts = true, Description = "The commander's shoulder pauldron with an ammo bandolier. Officers and ARC troopers." },
		new() { Id = "gear_91", Name = "Shoulder strap", Slot = "gear", MinRank = 3, Quals = new[] { "pilot", "engineer" }, Posts = true, Description = "A webbing strap over the shoulder. Corporals and up, pilots and engineers." },
		new() { Id = "pauldron", Name = "Heavy kit", Slot = "gear", MinRank = 5, Quals = new[] { "heavy" }, Posts = true, Description = "Bandolier, satchel and the heavy's pack. Staff Sergeants and up, heavy gunners." },
		new() { Id = "rangefinder", Name = "Specialist visor", Slot = "head", MinRank = 3, Quals = new[] { "sniper", "medic" }, Posts = true, Description = "A visor over the T. Corporals and up, marksmen and medics." },
		new() { Id = "macrobinoculars", Name = "Macrobinocular visor", Slot = "head", MinRank = 5, Quals = new[] { "sniper", "heavy" }, Posts = true, Description = "The heavy's brow visor. Staff Sergeants and up, marksmen and heavy gunners." },
		new() { Id = "headgear", Name = "Helmet antenna", Slot = "head", MinRank = 6, Quals = new[] { "officer" }, Posts = true, Description = "The officer's helmet antenna. 2nd Lieutenants and up." },
		new() { Id = "antenna", Name = "Shoulder antenna", Slot = "antenna", MinRank = 4, Quals = new[] { "engineer", "pilot", "trainer" }, Posts = true, Description = "A comms antenna on the shoulder. Sergeants and up, engineers, pilots and trainers." },
		new() { Id = "holster", Name = "Thigh holster", Slot = "hip", MinRank = 1, Description = "A sidearm holster on the thigh. Every trooper past Cadet." },
	};


	public static Part Get( string id ) => All.FirstOrDefault( p => p.Id == id );

	/// <summary> The Heavy's and Specialist's visors are part of their class model's helmet, so their helmets stay on. </summary>
	public static bool CanTakeHelmetOff( string classId ) => classId is not ( "heavy" or "specialist" or ClassRegistry.Jedi );

	/// <summary> The pieces in a saved list, at most one per slot, unknown ids dropped. </summary>
	public static List<Part> Parse( string list )
	{
		var parts = new List<Part>();
		foreach ( var id in ( list ?? "" ).Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
			if ( Get( id ) is { } p && parts.All( x => x.Slot != p.Slot ) ) parts.Add( p );
		return parts;
	}

	public static string Join( IEnumerable<Part> parts ) => string.Join( ",", parts.Select( p => p.Id ) );

	/// <summary> Why this character can't wear a piece, or null when they can. Works on host and client data alike. </summary>
	public static string Locked( Part p, string battalion, string classId, int rank, Func<string, bool> hasQual, bool hasPost, bool admin )
	{
		if ( classId == ClassRegistry.Jedi || battalion == "jedi" ) return "Clone armour only";
		if ( p.Battalions.Length > 0 && !p.Battalions.Contains( battalion ) ) return $"{string.Join( " or ", p.Battalions )} only";
		if ( admin ) return null;
		if ( p.MinRank >= 0 && rank >= p.MinRank ) return null;
		if ( p.Quals.Any( hasQual ) ) return null;
		if ( p.Posts && hasPost ) return null;
		var ways = new List<string>();
		if ( p.MinRank >= 0 ) ways.Add( FactionRegistry.CloneRanks[Math.Clamp( p.MinRank, 0, FactionRegistry.CloneRanks.Count - 1 )].Name + "+" );
		ways.AddRange( p.Quals.Select( SwrpCareer.QualName ) );
		if ( p.Posts ) ways.Add( "a battalion post" );
		return "Needs " + string.Join( ", ", ways );
	}

	/// <summary> Host: keep only what this character may wear now. </summary>
	public static string Validate( CharacterRecord c, string list )
	{
		if ( c is null ) return "";
		var post = SwrpBattalions.PostOf( c.FactionId, c.OwnerSteamId ) != SwrpBattalions.Post.None;
		var admin = Whitelist.GetRole( c.OwnerSteamId ) >= StaffRole.Admin;
		return Join( Parse( list ).Where( p => Locked( p, c.FactionId, c.ClassId, c.RankIndex, q => SwrpCareer.HasQual( c.OwnerSteamId, c.FactionId, q ), post, admin ) is null ) );
	}

	/// <summary> Client: save a character's armour (the customiser's SAVE). </summary>
	[Rpc.Host]
	public static void RequestSet( string characterId, string parts, bool helmetOff )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		var record = CharacterStore.Find( caller.SteamId.ValueUnsigned, characterId );
		if ( record is null ) return;
		var wanted = Parse( parts );
		var kept = Validate( record, parts );
		record.Armor = kept;
		record.HelmetOff = helmetOff;
		CharacterStore.Save( record.OwnerSteamId );
		if ( SwrpGameManager.Current?.FindPlayer( caller ) is { } p && p.CharacterId == record.Id && p.GetComponent<SwrpArmor>() is { } armor )
		{
			armor.Parts = kept;
			armor.HelmetOff = helmetOff;
		}
		var dropped = wanted.Count - Parse( kept ).Count;
		SwrpNet.NotifyConnection( caller, dropped > 0 ? $"Armour saved; {dropped} piece{( dropped == 1 ? "" : "s" )} you haven't earned yet left off." : "Armour saved.", dropped > 0 );
		SwrpNet.SendMenuState( caller );
	}

	/// <summary> Client: /helmet takes your helmet off or puts it back on. </summary>
	public static bool TryLocal( string cmd, string rest )
	{
		if ( cmd is "/helmet" or "/helm" )
		{
			if ( SwrpPlayers.Local?.GetComponent<SwrpArmor>() is { } a ) a.ToggleHelmet();
			return true;
		}
		if ( cmd is "/armor" or "/armour" or "/customize" )
		{
			ArmorCustomizer.OpenFor( SwrpPlayers.Local?.CharacterId );
			return true;
		}
		return false;
	}
}

/// <summary>
/// On the trooper (networked, added at spawn): which armour pieces they wear (the host sets it from the saved character) and
/// whether their helmet is off (their own toggle, /helmet). The drawing is SwrpArmorLayer's job on every machine.
/// </summary>
public sealed class SwrpArmor : Component
{
	[Sync( SyncFlags.FromHost )] public string Parts { get; set; } = "";
	[Sync] public bool HelmetOff { get; set; }

	SwrpPlayer _player;
	SwrpArmorLayer _layer;

	public void ToggleHelmet()
	{
		if ( IsProxy ) return;
		if ( !SwrpArmorRegistry.CanTakeHelmetOff( GetComponent<SwrpPlayer>()?.ClassId ) )
		{
			SwrpChat.AddLocal( ChatKind.System, "Your class's visor is built into the helmet: it stays on." );
			return;
		}
		HelmetOff = !HelmetOff;
		SwrpAudio.Ui( SwrpAudio.Click );
		SwrpChat.AddLocal( ChatKind.System, HelmetOff ? "Helmet off. /helmet puts it back on." : "Helmet on." );
	}

	protected override void OnUpdate()
	{
		_player ??= GetComponent<SwrpPlayer>();
		if ( _player is null ) return;
		_layer ??= Components.GetOrCreate<SwrpArmorLayer>();
		// (a named commander's model, SwrpCommanders, wears its own kit: no customiser pieces, cape or bare head on it)
		var clone = _player.EventDef is null && _player.FactionId != "jedi" && !_player.WearsCommanderModel;
		_layer.Body = _player.Body;
		_layer.Parts = clone ? Parts : "";
		_layer.Faction = _player.FactionId;
		_layer.HelmetOff = clone && HelmetOff && SwrpArmorRegistry.CanTakeHelmetOff( _player.ClassId );
		_layer.Cape = clone && _player.ClassId == ClassRegistry.Officer;
	}
}

/// <summary>
/// Draws armour pieces bone-merged onto a body (a trooper, or the character screen's mannequin), takes the helmet off
/// (an invisible material on the helmet's slot, the bare clone head drawn instead) and puts the battalion's body markings
/// on (bf2_armor_paint.py; the 501st's are in the trooper models already). Local and cosmetic.
/// </summary>
public sealed class SwrpArmorLayer : Component
{
	public SkinnedModelRenderer Body { get; set; }
	public string Parts { get; set; } = "";
	public string Faction { get; set; }
	public bool HelmetOff { get; set; }

	/// <summary> An Officer's commander cape (BF2's cape_01, dyed the battalion's colour), on top of the chosen pieces. </summary>
	public bool Cape { get; set; }
	const string CapeModel = "models/troopers/armor/clone_armor_cape.vmdl";

	const string Hidden = "models/troopers/materials/lambert1.vmat";
	const string BodyMat = "t_l_assault_preq_01_body";
	const string HeavyMat = "t_l_heavy_preq_01";

	readonly List<SkinnedModelRenderer> _parts = new();
	string _built;
	string _overridesFor;

	protected override void OnUpdate()
	{
		if ( !Body.IsValid() || Body.Model is null || Body.Model.IsError ) return;

		var want = $"{Parts}|{( HelmetOff ? "head" : "" )}|{Faction}|{Body.Model.ResourcePath}|{Cape}";
		if ( want != _built ) Rebuild( want );

		// follow the body: hidden in first person (shadow only), in a craft seat or for invisible staff, and its room tint
		foreach ( var r in _parts )
		{
			if ( !r.IsValid() ) continue;
			if ( r.RenderType != Body.RenderType ) r.RenderType = Body.RenderType;
			var show = Body.Enabled && Body.GameObject.Active;
			if ( r.Enabled != show ) r.Enabled = show;
			r.Tint = Body.Tint;
		}

		var overrides = $"{HelmetOff}|{Faction}|{Body.MaterialGroup}|{Body.Model.ResourcePath}";
		if ( overrides != _overridesFor ) ApplyOverrides( overrides );
	}

	void Rebuild( string key )
	{
		_built = key;
		foreach ( var r in _parts ) if ( r.IsValid() ) r.GameObject.Destroy();
		_parts.Clear();

		// helmet off: the pieces that sit on the helmet come off with it
		var models = SwrpArmorRegistry.Parse( Parts ).Where( p => !HelmetOff || p.Slot != "head" ).Select( p => p.Model ).ToList();
		if ( HelmetOff ) models.Add( "models/troopers/armor/clone_armor_head.vmdl" );
		if ( Cape ) models.Add( CapeModel );
		foreach ( var path in models )
		{
			var model = Model.Load( path );
			if ( model is null || model.IsError ) { Log.Warning( $"[SWRP] armour: no model {path}" ); continue; }
			var go = new GameObject( Body.GameObject, true, "Armour " + System.IO.Path.GetFileNameWithoutExtension( path ) );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			var r = go.Components.Create<SkinnedModelRenderer>();
			r.Model = model;
			r.BoneMergeTarget = Body;
			r.UseAnimGraph = false;
			if ( !string.IsNullOrEmpty( Faction ) && Enumerable.Range( 0, model.MaterialGroupCount ).Any( i => model.GetMaterialGroupName( i ) == Faction ) )
				r.MaterialGroup = Faction;
			_parts.Add( r );
		}
	}

	/// <summary> The helmet's slot hidden (helmet off), and the battalion's body and heavy-plate markings on the body's own slots. </summary>
	void ApplyOverrides( string key )
	{
		_overridesFor = key;
		var mats = Body.Materials;
		var marked = !string.IsNullOrEmpty( Faction ) && Faction != "501st";
		for ( var i = 0; i < mats.Count; i++ )
		{
			var path = mats.GetOriginal( i )?.ResourcePath ?? "";
			var name = System.IO.Path.GetFileNameWithoutExtension( path );
			Material want = null;
			if ( HelmetOff && ( name.Contains( "helmet" ) || name.Contains( "headgear" ) ) ) want = Material.Load( Hidden );
			// (the body's battalion markings are per class now, in the trooper models' material groups: bf2_markings.py, 2026-10-13)
			else if ( marked && name == HeavyMat && Faction != "coruscant_guard" ) want = Load( $"{HeavyMat}_mark_{Faction}" );
			mats.SetOverride( i, want );
		}
	}

	static Material Load( string name )
	{
		// battalions without a marking (the plain clone_trooper unit) have no file, and loading a missing material hands
		// back the red error checkerboard, which IsError doesn't catch: check the file first
		var path = $"models/troopers/materials/{name}.vmat";
		if ( !FileSystem.Mounted.FileExists( path + "_c" ) && !FileSystem.Mounted.FileExists( path ) ) return null;
		var m = Material.Load( path );
		return m is null || m.IsError ? null : m;
	}

	protected override void OnDestroy()
	{
		foreach ( var r in _parts ) if ( r.IsValid() ) r.GameObject.Destroy();
		_parts.Clear();
		if ( Body.IsValid() )
			for ( var i = 0; i < Body.Materials.Count; i++ ) Body.Materials.SetOverride( i, null );
	}
}
