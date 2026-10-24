using System;
using System.Text.Json.Serialization;

namespace Swrp;

/// <summary>
/// A sliding security door. The host owns the open and locked state; every machine animates the door parts and their
/// collision from that state. Geometry comes from Assets/data/notorious_doors.json (written by MapSource/notorious_doors.py),
/// so the doors keep the map's own textures. Placement and access rules are generated into the scene by notorious_scene.py.
/// </summary>
[Title( "SWRP Door" ), Category( "SWRP" ), Icon( "sensor_door" )]
public sealed class SwrpDoor : Component
{
	/// <summary> Key into the door geometry file (the VMF targetname). </summary>
	[Property] public string DoorId { get; set; }
	[Property] public string DisplayName { get; set; } = "Door";

	/// <summary> Index into the rank ladder (0 = anyone, 8 = Captain). </summary>
	[Property] public int RequiredRank { get; set; }

	/// <summary> Optional faction id; empty means any faction. </summary>
	[Property] public string RequiredFaction { get; set; }
	[Property] public bool StaffOnly { get; set; }

	/// <summary> Optional faction id whose members get through whatever their rank (the Coruscant Guard on the brig shields). </summary>
	[Property] public string OrFaction { get; set; }

	/// <summary> Optional item id that also opens this door (a keycard), whatever the rank. </summary>
	[Property] public string KeycardItem { get; set; }

	/// <summary> Seconds before the door closes itself. 0 keeps it open until someone closes it. </summary>
	[Property] public float AutoCloseSeconds { get; set; } = 6f;

	/// <summary> Only a console moves this door (the hangar bay doors). </summary>
	[Property] public bool ConsoleOnly { get; set; }
	[Property] public float MoveTime { get; set; } = 1.1f;

	[Sync( SyncFlags.FromHost )] public bool IsOpen { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Locked { get; set; }

	/// <summary> Held shut by a machine (a turbolift in motion). Not saved, and nobody can open it meanwhile. </summary>
	[Sync( SyncFlags.FromHost )] public bool HeldShut { get; set; }

	/// <summary> Inventory hook for keycards: return true to let a player through. Checked on the host. </summary>
	public static Func<SwrpPlayer, SwrpDoor, bool> KeycardHook { get; set; }

	public const int OfficerRank = 8;

	public bool IsBrigShield => DoorId is not null && ( DoorId.StartsWith( "brig_ray_" ) || DoorId.StartsWith( "brig_fightcage" ) || ( DoorId.StartsWith( "cell" ) && DoorId.EndsWith( "_gate" ) ) );

	readonly List<(GameObject Go, Vector3 Move)> _parts = new();
	float _progress;
	bool _shownOpen;
	TimeSince _stateChanged;

	/// <summary> The rank this door needs right now: its own, or 2nd Lieutenant during a ship lockdown (DEFCON 1). </summary>
	public int EffectiveRank => SwrpShip.Lockdown && !ConsoleOnly ? Math.Max( RequiredRank, SwrpShip.OfficerRank ) : RequiredRank;

	public string AccessLabel => ConsoleOnly ? ( IsBrigShield ? "Brig shield control" : "Hangar console" ) : StaffOnly ? "Staff only"
		: !string.IsNullOrEmpty( OrFaction ) && RequiredRank > 0 ? $"{FactionRegistry.CloneRanks[Math.Clamp( RequiredRank, 0, 10 )].Name}+ or {FactionRegistry.Get( OrFaction )?.Name ?? OrFaction}"
		: SwrpShip.Lockdown && RequiredRank < SwrpShip.OfficerRank ? $"Lockdown · {FactionRegistry.CloneRanks[SwrpShip.OfficerRank].Name}+"
		: RequiredRank > 0 ? $"{FactionRegistry.CloneRanks[Math.Clamp( RequiredRank, 0, 10 )].Name}+"
		: !string.IsNullOrEmpty( RequiredFaction ) ? FactionRegistry.Get( RequiredFaction )?.Name ?? RequiredFaction : "Open access";

	protected override void OnStart()
	{
		BuildParts();
		_shownOpen = IsOpen;
		_progress = IsOpen ? 1 : 0;
		Apply();

		if ( Networking.IsHost )
			Locked = SwrpDoorStore.IsLocked( Scene, DoorId );
	}

	void BuildParts()
	{
		var data = SwrpDoorGeometry.Get( DoorId );
		if ( data is null )
		{
			Log.Warning( $"[SWRP] No geometry for door '{DoorId}'." );
			return;
		}

		foreach ( var part in data.Parts )
		{
			var go = new GameObject( GameObject, true, "Door Part" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = Vector3.Zero;

			var model = SwrpDoorGeometry.BuildModel( part );
			if ( model is not null )
				go.AddComponent<ModelRenderer>().Model = model;

			var lo = part.Lo.ToVector();
			var hi = part.Hi.ToVector();
			var box = go.AddComponent<BoxCollider>();
			box.Center = ( lo + hi ) * 0.5f;
			box.Scale = hi - lo;
			_parts.Add( (go, part.Move.ToVector()) );
		}
	}

	protected override void OnUpdate()
	{
		if ( _shownOpen != IsOpen )
		{
			_shownOpen = IsOpen;
			_stateChanged = 0;
			Sound.Play( IsBrigShield ? ( IsOpen ? SwrpAudio.ShieldOff : SwrpAudio.ShieldOn ) : IsOpen ? SwrpAudio.DoorOpen : SwrpAudio.DoorClose, WorldPosition );
		}

		var target = IsOpen ? 1f : 0f;
		if ( _progress != target )
		{
			_progress = _progress.Approach( target, Time.Delta / MathF.Max( 0.05f, MoveTime ) );
			Apply();
		}

		if ( Networking.IsHost && IsOpen && AutoCloseSeconds > 0 && _stateChanged > AutoCloseSeconds && !Occupied() )
			IsOpen = false;
	}

	void Apply()
	{
		// ease in and out, so the panel starts and stops softly
		var t = _progress * _progress * ( 3f - 2f * _progress );
		// lifting doors go a little further than their opening, and a fully open panel stops colliding, so its bottom edge
		// never catches a trooper's head
		foreach ( var (go, move) in _parts )
		{
			if ( !go.IsValid() ) continue;
			var extra = move.z > MathF.Abs( move.x ) + MathF.Abs( move.y ) ? move.Normal * 32f : Vector3.Zero;
			go.LocalPosition = ( move + extra ) * t;
			// EverythingInSelf: a plain Get skips disabled components, so a fully opened door never found its collision again
			// and stayed walk-through once shut (Angelo, 2026-10-19)
			if ( go.Components.Get<BoxCollider>( FindMode.EverythingInSelf ) is { } box ) box.Enabled = _progress < 0.97f;
		}
	}

	/// <summary> Somebody standing in the doorway keeps it open. </summary>
	bool Occupied()
	{
		foreach ( var p in SwrpPlayers.Valid )
			if ( p.WorldPosition.WithZ( WorldPosition.z ).Distance( WorldPosition ) < 64f && MathF.Abs( p.WorldPosition.z - WorldPosition.z ) < 90f )
				return true;
		return false;
	}

	// ----- Access (host) -----

	/// <summary> Null if this player may open the door, otherwise the reason they may not. </summary>
	public string CheckAccess( SwrpPlayer p, Connection connection )
	{
		var staff = connection is not null && Whitelist.IsStaff( connection );
		if ( ConsoleOnly ) return IsBrigShield ? "This shield is switched from the brig tower or the brig shield control." : "These doors are run from the hangar control console.";
		if ( HeldShut ) return "The turbolift is in motion.";
		if ( staff ) return null;
		if ( p is null || p.CharacterId is null ) return "Deploy a character first.";
		if ( p.Jailed ) return "Access denied.";
		if ( Locked ) return $"{DisplayName} is locked.";
		if ( StaffOnly ) return $"{DisplayName}: staff only.";
		if ( HasKeycard( p ) ) return null;
		if ( !string.IsNullOrEmpty( OrFaction ) && p.FactionId == OrFaction ) return null;
		if ( !string.IsNullOrEmpty( RequiredFaction ) && p.FactionId != RequiredFaction )
			return $"{DisplayName}: {FactionRegistry.Get( RequiredFaction )?.Name ?? RequiredFaction} only.";
		if ( p.RankIndex < EffectiveRank )
			return EffectiveRank > RequiredRank
				? $"Ship lockdown: {DisplayName} opens for {FactionRegistry.CloneRanks[EffectiveRank].Name}s and up."
				: $"{DisplayName}: requires {FactionRegistry.CloneRanks[Math.Clamp( RequiredRank, 0, 10 )].Name} or higher.";
		return null;
	}

	bool HasKeycard( SwrpPlayer p )
	{
		if ( KeycardHook?.Invoke( p, this ) == true ) return true;
		return !string.IsNullOrEmpty( KeycardItem ) && p.Inventory.Any( i => i.ItemId == KeycardItem && i.Count > 0 );
	}

	/// <summary> Client: ask the host to open or close this door. </summary>
	[Rpc.Host]
	public void RequestUse()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( p.IsValid() && p.WorldPosition.Distance( WorldPosition ) > 260f + Size() )
		{
			SwrpNet.NotifyConnection( caller, "You are too far away.", true );
			return;
		}

		var reason = CheckAccess( p, caller );
		if ( reason is not null )
		{
			SwrpNet.NotifyConnection( caller, reason, true );
			Fx( SwrpAudio.Deny );
			return;
		}

		SetOpen( !IsOpen );
	}

	/// <summary> Host. </summary>
	public void SetOpen( bool open )
	{
		if ( IsOpen == open ) return;
		IsOpen = open;
		_stateChanged = 0;
	}

	/// <summary> Host: lock or unlock and remember it for this map. </summary>
	public void SetLocked( bool locked )
	{
		Locked = locked;
		SwrpDoorStore.SetLocked( Scene, DoorId, locked );
		Fx( locked ? SwrpAudio.DoorLock : SwrpAudio.DoorUnlock );
		if ( locked ) SetOpen( false );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Fx( string sound ) => Sound.Play( sound, WorldPosition );

	/// <summary> One line for the console: state, access and where the moving parts are. </summary>
	public string Describe()
	{
		var parts = string.Join( " ", _parts.Select( p => p.Go.IsValid() ? $"[{p.Go.LocalPosition.x:0},{p.Go.LocalPosition.y:0},{p.Go.LocalPosition.z:0}{( p.Go.GetComponent<ModelRenderer>() is { Model: not null } ? "" : " no-mesh" )}]" : "[gone]" ) );
		return $"{DisplayName} ({DoorId}) {(IsOpen ? "OPEN" : "closed")}{(Locked ? " LOCKED" : "")}, {AccessLabel}, {_parts.Count} parts {parts}";
	}

	float Size()
	{
		var biggest = 0f;
		foreach ( var (go, _) in _parts )
			if ( go.GetComponent<BoxCollider>() is { } b ) biggest = MathF.Max( biggest, b.Scale.Length * 0.5f );
		return biggest;
	}

	// ----- /lock and /unlock (host) -----

	public static void LockCommand( Connection caller, SwrpPlayer sender, bool locked )
	{
		var staff = Whitelist.IsStaff( caller );
		if ( !staff && sender.RankIndex < OfficerRank )
		{
			SwrpChat.SystemTo( caller, "Only Captains and above (or staff) can lock doors." );
			return;
		}

		var door = LookedAt( sender );
		if ( door is null )
		{
			SwrpChat.SystemTo( caller, "Look at a door or its panel first." );
			return;
		}

		if ( door.ConsoleOnly )
		{
			SwrpChat.SystemTo( caller, "The hangar bay doors are run from the hangar console." );
			return;
		}

		if ( door.Locked == locked )
		{
			SwrpChat.SystemTo( caller, $"{door.DisplayName} is already {(locked ? "locked" : "unlocked")}." );
			return;
		}

		door.SetLocked( locked );
		SwrpNet.NotifyConnection( caller, $"{door.DisplayName} {(locked ? "locked" : "unlocked")}.", false );
		if ( staff ) SwrpChat.StaffLog( $"{caller.DisplayName} {(locked ? "locked" : "unlocked")} {door.DisplayName} ({door.DoorId})" );
	}

	/// <summary> The door a player is looking at: a trace from the eyes, else the nearest door in front within reach. </summary>
	public static SwrpDoor LookedAt( SwrpPlayer p )
	{
		var c = p.Controller;
		if ( !c.IsValid() ) return null;

		var eye = c.EyePosition;
		var fwd = c.EyeAngles.ToRotation().Forward;
		var tr = p.Scene.Trace.Ray( eye, eye + fwd * 220f ).IgnoreGameObjectHierarchy( p.GameObject ).Run();
		if ( tr.Hit && tr.GameObject is { } hit )
		{
			if ( hit.Components.GetInAncestorsOrSelf<SwrpDoor>() is { } d ) return d;
			if ( hit.Components.GetInAncestorsOrSelf<SwrpDoorPanel>() is { } panel && panel.Door is { } pd ) return pd;
		}

		var doors = p.Scene.GetAllComponents<SwrpDoor>().Where( d => !d.ConsoleOnly ).ToList();
		return doors
			.Where( d => d.WorldPosition.Distance( eye ) < 220f && Vector3.Dot( ( d.WorldPosition - eye ).Normal, fwd ) > 0.6f )
			.OrderBy( d => d.WorldPosition.Distance( eye ) )
			.FirstOrDefault()
			// standing right in a doorway counts too
			?? doors.Where( d => d.WorldPosition.Distance( p.WorldPosition ) < 120f ).OrderBy( d => d.WorldPosition.Distance( p.WorldPosition ) ).FirstOrDefault();
	}
}

/// <summary> Locked doors per map, saved on the host in the data folder (swrp/doors_&lt;map&gt;.json). </summary>
public static class SwrpDoorStore
{
	static string _map;
	static Dictionary<string, bool> _locked;

	static string PathFor( Scene scene ) => $"swrp/doors_{MapKey( scene )}.json";

	static string MapKey( Scene scene )
	{
		var name = scene?.Name;
		if ( string.IsNullOrWhiteSpace( name ) ) name = "map";
		return new string( name.ToLowerInvariant().Select( ch => char.IsLetterOrDigit( ch ) ? ch : '_' ).ToArray() );
	}

	static Dictionary<string, bool> Load( Scene scene )
	{
		var key = MapKey( scene );
		if ( _locked is null || _map != key )
		{
			_map = key;
			_locked = FileSystem.Data.ReadJson<Dictionary<string, bool>>( PathFor( scene ), null ) ?? new();
		}

		return _locked;
	}

	public static bool IsLocked( Scene scene, string doorId ) => doorId is not null && Load( scene ).TryGetValue( doorId, out var v ) && v;

	public static void SetLocked( Scene scene, string doorId, bool locked )
	{
		if ( doorId is null ) return;
		var data = Load( scene );
		if ( locked ) data[doorId] = true; else data.Remove( doorId );
		FileSystem.Data.CreateDirectory( "swrp" );
		FileSystem.Data.WriteJson( PathFor( scene ), data );
	}
}

/// <summary> Door meshes from Assets/data/notorious_doors.json, loaded once and shared by every door. </summary>
public static class SwrpDoorGeometry
{
	public sealed class FileData
	{
		[JsonPropertyName( "doors" )] public List<DoorData> Doors { get; set; } = new();
	}

	public sealed class DoorData
	{
		[JsonPropertyName( "id" )] public string Id { get; set; }
		[JsonPropertyName( "parts" )] public List<PartData> Parts { get; set; } = new();
	}

	public sealed class PartData
	{
		[JsonPropertyName( "move" )] public float[] Move { get; set; }
		[JsonPropertyName( "lo" )] public float[] Lo { get; set; }
		[JsonPropertyName( "hi" )] public float[] Hi { get; set; }

		/// <summary> Material path -> triangles, 11 floats per vertex: position, normal, uv, tangent. </summary>
		[JsonPropertyName( "meshes" )] public Dictionary<string, float[]> Meshes { get; set; } = new();
	}

	public const string FilePath = "data/notorious_doors.json";

	static Dictionary<string, DoorData> _doors;

	public static DoorData Get( string id )
	{
		if ( _doors is null )
		{
			_doors = new();
			try
			{
				var file = Json.Deserialize<FileData>( SwrpDataFile.ReadText( FilePath ) );
				foreach ( var d in file?.Doors ?? new() )
					if ( d.Id is not null ) _doors[d.Id] = d;
			}
			catch ( Exception e )
			{
				Log.Warning( $"[SWRP] Could not read {FilePath}: {e.Message}" );
			}
		}

		return id is not null && _doors.TryGetValue( id, out var data ) ? data : null;
	}

	public static Model BuildModel( PartData part )
	{
		var builder = Model.Builder;
		var any = false;
		foreach ( var (path, f) in part.Meshes )
		{
			var count = f.Length / 11;
			if ( count < 3 ) continue;

			var verts = new List<Vertex>( count );
			var bounds = new BBox( new Vector3( f[0], f[1], f[2] ), new Vector3( f[0], f[1], f[2] ) );
			for ( var i = 0; i < count; i++ )
			{
				var o = i * 11;
				var pos = new Vector3( f[o], f[o + 1], f[o + 2] );
				verts.Add( new Vertex( pos, new Vector3( f[o + 3], f[o + 4], f[o + 5] ), new Vector3( f[o + 8], f[o + 9], f[o + 10] ), new Vector4( f[o + 6], f[o + 7], 0, 0 ) ) );
				bounds = bounds.AddPoint( pos );
			}

			// The build writes triangles counter-clockwise seen from outside; the renderer wants them the other way round.
			var indices = new List<int>( count * 2 );
			for ( var i = 0; i < count; i += 3 )
			{
				indices.Add( i );
				indices.Add( i + 2 );
				indices.Add( i + 1 );
			}
			// ...and every face drawn from its back too (its normal flipped): some VMF door brushes come out wound inside out, and
			// the topside hangar doors vanished from above, leaving a black open trench in the hull (Angelo, 2026-10-15)
			for ( var i = 0; i < count; i++ )
			{
				var v = verts[i];
				verts.Add( new Vertex( v.Position, -v.Normal, -v.Tangent, v.TexCoord0 ) );
			}
			for ( var i = 0; i < count; i += 3 )
			{
				indices.Add( count + i );
				indices.Add( count + i + 1 );
				indices.Add( count + i + 2 );
			}

			var mesh = new Mesh( Material.Load( path ) );
			mesh.CreateVertexBuffer( verts.Count, verts );
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = bounds;
			builder = builder.AddMesh( mesh );
			any = true;
		}

		return any ? builder.Create() : null;
	}

	static Vector3 ToVec( float[] a ) => a is { Length: >= 3 } ? new Vector3( a[0], a[1], a[2] ) : Vector3.Zero;

	public static Vector3 ToVector( this float[] a ) => ToVec( a );
}
