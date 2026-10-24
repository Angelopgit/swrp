using System;
using Sandbox.Network;

namespace Swrp;

/// <summary>
/// Runs the server side of the gamemode: starts the lobby, spawns characters picked from the character menu
/// and cleans up when players leave. Exists once per scene automatically.
/// </summary>
public sealed class SwrpGameManager : GameObjectSystem<SwrpGameManager>, ISceneStartup, Component.INetworkListener
{
	public const string PlayerPrefabPath = "prefabs/clone_trooper.prefab";

	public SwrpGameManager( Scene scene ) : base( scene )
	{
		SwrpStaff.Reset();
		// Statics survive between play sessions in the editor, so start every scene clean.
		SwrpNet.ResetClientState();
		CharacterStore.ClearCache();
		Whitelist.Reload();
		SwrpUiState.InventoryOpen = false;
		SwrpUiState.CharacterMenuOpen = false;
		SwrpUiState.ChatOpen = false;
		SwrpChat.Reset();
		SwrpRanks.Reset();
		SwrpCorpses.Reset();
		SwrpStaffTools.Reset();
		SwrpUiState.StaffOpen = false;
	}

	void ISceneStartup.OnHostInitialize()
	{
		// Only run the gamemode in scenes that ask for it (the Venator scene has a SwrpSceneSettings object).
		if ( !Scene.GetAllComponents<SwrpSceneSettings>().Any() ) return;

		// Squads, orders and radio live on one networked object; scenes built by notorious_scene.py already have it.
		if ( !Scene.GetAllComponents<SwrpRoleplay>().Any() )
		{
			var go = Scene.CreateObject();
			go.Name = "SWRP Roleplay";
			go.AddComponent<SwrpRoleplay>();
			go.NetworkSpawn();
		}

		if ( !Networking.IsActive )
		{
			Networking.CreateLobby( new LobbyConfig
			{
				MaxPlayers = 64,
				Privacy = LobbyPrivacy.Public,
				Name = "SWRP | Venator-class Star Destroyer",
			} );
		}
	}

	bool Component.INetworkListener.AcceptConnection( Connection connection, ref string reason )
	{
		if ( SwrpStaff.IsBanned( connection.SteamId.ValueUnsigned, out var message ) )
		{
			reason = message;
			Log.Info( $"[SWRP] Refused {connection.DisplayName}: {message}" );
			return false;
		}

		return true;
	}

	void Component.INetworkListener.OnActive( Connection connection )
	{
		Log.Info( $"[SWRP] {connection.DisplayName} ({connection.SteamId.ValueUnsigned}) joined" );
		SwrpBattalions.MarkSeen( connection );
		SwrpNet.SendMenuState( connection );
		SwrpGuide.OnJoin( connection );      // rules, qualifications, the board, the event HUD
	}

	void Component.INetworkListener.OnDisconnected( Connection connection )
	{
		Log.Info( $"[SWRP] {connection.DisplayName} left" );
		SwrpBattalions.OnLeft( connection );
		SwrpRoleplay.Current?.OnPlayerLeft( connection.SteamId.ValueUnsigned );
		DespawnPlayer( connection );
		_lastSpawn.Remove( connection.Id );
	}

	public SwrpPlayer FindPlayer( Connection connection )
		=> Scene.GetAllComponents<SwrpPlayer>().Where( p => p.Network.OwnerId == connection.Id )
			.OrderByDescending( p => p.CharacterId is not null ).FirstOrDefault();      // a deployed body before any stray one

	public void DespawnPlayer( Connection connection )
	{
		// Match on SteamID too, so a body left behind by an earlier connection of the same player is cleaned up as well.
		var steamId = connection.SteamId.ValueUnsigned;
		foreach ( var player in Scene.GetAllComponents<SwrpPlayer>()
			.Where( p => p.Network.OwnerId == connection.Id || (steamId != 0 && p.OwnerSteamId == steamId) ).ToList() )
		{
			player.GameObject.Destroy();
		}
	}

	readonly Dictionary<Guid, RealTimeSince> _lastSpawn = new();

	/// <summary>
	/// Host only. Replaces whatever the connection is currently playing with a fresh body for <paramref name="record"/>.
	/// </summary>
	[ConVar( "swrp_respawn_medbay", Help = "1 = troopers who die redeploy at the nearest medbay (host)", Saved = true )]
	public static bool RespawnAtMedbay { get; set; } = true;

	public void SpawnCharacter( Connection connection, CharacterRecord record, bool force = false, Vector3? near = null )
	{
		// Double clicks and repeated requests used to leave several bodies for one client; one spawn per second is plenty.
		if ( !force && _lastSpawn.TryGetValue( connection.Id, out var since ) && since < 1f )
			return;
		_lastSpawn[connection.Id] = 0;

		DespawnPlayer( connection );

		var prefab = GameObject.GetPrefab( PlayerPrefabPath );
		if ( !prefab.IsValid() )
		{
			Log.Warning( $"[SWRP] Player prefab missing at {PlayerPrefabPath}" );
			return;
		}

		var spawn = SwrpEvents.SpawnOverride( record )      // a Game Master's event spawn wins while it's set
			?? ( near is { } deathAt && RespawnAtMedbay && PickMedbay( deathAt ) is { } medbay ? medbay : PickSpawn( record.FactionId ) );
		var go = prefab.Clone( spawn.Position, spawn.Rotation );
		go.Name = $"Player - {record.FullTitle} ({connection.DisplayName})";

		// Class weapons (DC-15A and DC-17 for troopers, Z-6 for heavies, DC-15S for specialists) are issued if missing.
		if ( SwrpBlaster.IssueClassWeapons( record ) )
			CharacterStore.Save( record.OwnerSteamId );
		// a Jedi always has their lightsaber (Code/Jedi)
		if ( Swrp.Jedi.JediCombat.IssueSaber( record ) )
			CharacterStore.Save( record.OwnerSteamId );

		var player = go.GetComponent<SwrpPlayer>();
		player?.ApplyRecord( record );

		// Set the class body, health and armour before the spawn goes out, so nobody sees a Citizen for a frame.
		var cls = ClassRegistry.Get( record.ClassId ) ?? ClassRegistry.Default;
		if ( player?.Body is { } body && ( SwrpCommanders.Load( SwrpCommanders.ModelFor( player ), false ) ?? cls.LoadModel() ) is { } model )
			body.Model = model;
		go.Components.GetOrCreate<SwrpVitals>().ResetFor( cls );
		go.Components.GetOrCreate<SwrpBlaster>();
		go.Components.GetOrCreate<SwrpHands>();      // what's in hand: quick slots, wheel, held gear
		go.Components.GetOrCreate<Swrp.Jedi.JediCombat>();      // the lightsaber and the Force (wakes only for a Jedi)
		go.Components.GetOrCreate<SwrpFreeLook>();

		// the armour customiser's pieces, re-checked against today's rank, posts and qualifications (Characters/SwrpArmor.cs)
		var armor = go.Components.GetOrCreate<SwrpArmor>();
		armor.Parts = SwrpArmorRegistry.Validate( record, record.Armor );
		armor.HelmetOff = record.HelmetOff;

		// Proximity voice: plays from the trooper's position, lip-synced to the body. Range follows the voice mode.
		var voice = go.Components.GetOrCreate<Voice>();
		voice.WorldspacePlayback = true;
		voice.Distance = SwrpChat.TalkRange;
		if ( player?.Body is { } voiceBody ) voice.Renderer = voiceBody;

		// Look the way the spawn point faces.
		if ( go.GetComponent<PlayerController>() is { } controller )
			controller.EyeAngles = spawn.Rotation.Angles();

		go.NetworkSpawn( connection );
		Log.Info( $"[SWRP] {connection.DisplayName} spawned as {record.FullTitle}" );
		SwrpCareer.OnDeployed( player );      // cadets get their training card
	}

	Transform? PickMedbay( Vector3 near )
	{
		var medbays = Scene.GetAllComponents<SwrpSpawnPoint>().Where( p => p.Medbay ).ToList();
		if ( medbays.Count == 0 ) return null;
		return medbays.OrderBy( p => p.WorldPosition.DistanceSquared( near ) ).First().WorldTransform;
	}

	Transform PickSpawn( string factionId )
	{
		var points = Scene.GetAllComponents<SwrpSpawnPoint>().Where( p => !p.Medbay ).ToList();
		var factionPoints = points.Where( p => p.FactionId == factionId ).ToList();
		var genericPoints = points.Where( p => string.IsNullOrEmpty( p.FactionId ) ).ToList();

		var pool = factionPoints.Count > 0 ? factionPoints : genericPoints.Count > 0 ? genericPoints : points;
		if ( pool.Count > 0 )
			return pool[Game.Random.Next( pool.Count )].WorldTransform;

		var fallback = Scene.GetAllComponents<SpawnPoint>().ToList();
		if ( fallback.Count > 0 )
			return fallback[Game.Random.Next( fallback.Count )].WorldTransform;

		return new Transform( Vector3.Up * 64f );
	}
}

/// <summary>
/// Marks a scene as an SWRP scene and holds per-map settings.
/// </summary>
[Title( "SWRP Scene Settings" ), Category( "SWRP" ), Icon( "rocket_launch" )]
public sealed class SwrpSceneSettings : Component
{
	[Property, TextArea] public string MapName { get; set; } = "Venator-class Star Destroyer";
}

/// <summary>
/// Where characters spawn. Leave FactionId empty for anyone, or set it so a legion spawns in its own barracks.
/// </summary>
[Title( "SWRP Spawn Point" ), Category( "SWRP" ), Icon( "person_pin_circle" )]
public sealed class SwrpSpawnPoint : Component
{
	[Property] public string FactionId { get; set; }

	/// <summary> A medbay bed: only used to redeploy troopers who died, never for a normal deploy. </summary>
	[Property] public bool Medbay { get; set; }

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost ) SwrpBattalions.TickSeen();
	}

	protected override void DrawGizmos()
	{
		var faction = FactionRegistry.Get( FactionId );
		Gizmo.Draw.Color = faction?.Color ?? Color.White;
		Gizmo.Draw.LineBBox( new BBox( new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 72 ) ) );
		Gizmo.Draw.Arrow( Vector3.Up * 36, Vector3.Up * 36 + Vector3.Forward * 32 );
	}
}

/// <summary>
/// Console commands. They run on whoever types them and forward to the host, which checks admin rights.
/// </summary>
public static class SwrpCommands
{
	[ConCmd( "swrp_whitelist", Help = "swrp_whitelist <steamid|name> <faction> - allow a player to make characters in a legion" )]
	public static void WhitelistAdd( string target, string faction ) => SwrpNet.AdminWhitelist( target, faction, true );

	[ConCmd( "swrp_unwhitelist", Help = "swrp_unwhitelist <steamid|name> <faction>" )]
	public static void WhitelistRemove( string target, string faction ) => SwrpNet.AdminWhitelist( target, faction, false );

	[ConCmd( "swrp_whitelist_list", Help = "Print admins and whitelisted players" )]
	public static void WhitelistList() => SwrpNet.AdminListWhitelist();

	[ConCmd( "swrp_setrank", Help = "swrp_setrank <steamid|name> <rank index, 0 = Cadet ... 10 = Commander>" )]
	public static void SetRank( string target, int rank ) => SwrpNet.AdminSetRank( target, rank );

	[ConCmd( "swrp_addadmin", Help = "swrp_addadmin <steamid|name>" )]
	public static void AddAdmin( string target ) => SwrpNet.AdminAddAdmin( target );

	[ConCmd( "swrp_char_create", Help = "swrp_char_create <name> <faction> [trooper|heavy|specialist] - same as enlisting from the character menu" )]
	public static void CharCreate( string name, string faction, string cls = "trooper", string look = "" ) => SwrpNet.CreateCharacter( name, faction, cls, look );

	[ConCmd( "swrp_char_select", Help = "swrp_char_select <slot number, starting at 1>" )]
	public static void CharSelect( int slot )
	{
		var characters = SwrpNet.MenuState?.Characters;
		if ( characters is null || slot < 1 || slot > characters.Count )
		{
			Log.Warning( "[SWRP] No character in that slot. Open the character menu (F4) to see your list." );
			return;
		}

		SwrpNet.SelectCharacter( characters[slot - 1].Id );
	}

	[ConCmd( "swrp_inventory", Help = "Toggle the inventory" )]
	public static void ToggleInventory() => SwrpUiState.InventoryOpen = !SwrpUiState.InventoryOpen;

	[ConCmd( "me", Help = "me <action> - a roleplay emote only people nearby see" )]
	public static void Me( string a = "", string b = "", string c = "", string d = "", string e = "", string f = "", string g = "", string h = "", string i = "", string j = "" )
		=> SwrpChat.Submit( $"/me {string.Join( ' ', new[] { a, b, c, d, e, f, g, h, i, j }.Where( x => !string.IsNullOrEmpty( x ) ) )}" );

	/// <summary> Same as typing in chat; handy from the console, e.g. swrp_say "/jail Bob 10m". </summary>
	[ConCmd( "swrp_say", Help = "swrp_say \"<anything you could type in chat>\"" )]
	public static void Say( string text ) => SwrpChat.Submit( text );

	/// <summary> Same as pressing E on a door (the host still checks reach and access). Handy for binds and testing. </summary>
	[ConCmd( "swrp_door_use", Help = "swrp_door_use <door id|hangar> - open or close a door you are standing at, like pressing E" )]
	public static void DoorUse( string doorId )
	{
		if ( doorId == "hangar" )
		{
			Game.ActiveScene?.GetAllComponents<SwrpHangarConsole>().FirstOrDefault()?.RequestUse();
			return;
		}

		var door = Game.ActiveScene?.GetAllComponents<SwrpDoor>().FirstOrDefault( d => d.DoorId == doorId );
		if ( door is null ) { Log.Warning( $"[SWRP] No door '{doorId}'. Door ids are listed in MapSource/doors_review.md." ); return; }
		door.RequestUse();
	}

	[ConCmd( "swrp_door_info", Help = "swrp_door_info <door id> - print a door's state, access rule and part positions" )]
	public static void DoorInfo( string doorId )
	{
		var door = Game.ActiveScene?.GetAllComponents<SwrpDoor>().FirstOrDefault( d => d.DoorId == doorId );
		Log.Info( door is null ? $"[SWRP] No door '{doorId}'." : $"[SWRP] {door.Describe()}" );
	}

	/// <summary>
	/// Host debug: looks for see-through gaps around every closed door. From a point in the doorway on each side (where a
	/// player would stand) rays aim at the door's edges, just past the door, and on through to the other side; a ray that
	/// hits nothing is a gap you could see (or walk) through. The map collides as a hollow mesh, so rays have to start in
	/// open air like this rather than run along the wall.
	/// </summary>
	[ConCmd( "swrp_door_gaps", Help = "swrp_door_gaps [door id] [1 = include open doors] - host only: report see-through gaps around closed doors" )]
	public static void DoorGaps( string doorId = "", int includeOpen = 0 )
	{
		if ( !Networking.IsHost || Game.ActiveScene is not { } scene ) return;
		int checkedDoors = 0, gapDoors = 0;
		foreach ( var door in scene.GetAllComponents<SwrpDoor>().OrderBy( d => d.DoorId ) )
		{
			if ( !string.IsNullOrEmpty( doorId ) && door.DoorId != doorId ) continue;
			var data = SwrpDoorGeometry.Get( door.DoorId );
			if ( data is null || data.Parts.Count == 0 ) continue;
			if ( door.IsOpen && includeOpen == 0 ) { Log.Info( $"[gaps] {door.DoorId}: open, skipped" ); continue; }

			var lo = data.Parts.Select( p => p.Lo.ToVector() ).Aggregate( ( x, y ) => x.ComponentMin( y ) ) + door.WorldPosition;
			var hi = data.Parts.Select( p => p.Hi.ToVector() ).Aggregate( ( x, y ) => x.ComponentMax( y ) ) + door.WorldPosition;
			var size = hi - lo;
			int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
			int a = thin == 0 ? 1 : 0, b = thin == 2 ? 1 : 2;
			var center = ( lo + hi ) * 0.5f;

			// targets: rings 3 and 8 units outside the door's edges, every 6 units
			var targets = new List<(Vector3 Pos, string Edge)>();
			foreach ( var off in new[] { 3f, 8f } )
			{
				for ( var u = lo[a] - off; u <= hi[a] + off; u += 6f )
				{
					var t1 = center; t1[a] = u; t1[b] = hi[b] + off; targets.Add( (t1, "above") );
					var t2 = center; t2[a] = u; t2[b] = lo[b] - off; targets.Add( (t2, "below") );
				}
				for ( var v = lo[b]; v <= hi[b]; v += 6f )
				{
					var t1 = center; t1[a] = lo[a] - off; t1[b] = v; targets.Add( (t1, "side-") );
					var t2 = center; t2[a] = hi[a] + off; t2[b] = v; targets.Add( (t2, "side+") );
				}
			}

			var through = new Dictionary<string, int>();
			int rays = 0;
			foreach ( var side in new[] { -1f, 1f } )
			{
				var eye = center; eye[thin] += side * 64f;
				foreach ( var (pos, edge) in targets )
				{
					rays++;
					var end = pos + ( pos - eye );
					var tr = scene.Trace.Ray( eye, end ).WithoutTags( "player" ).Run();
					if ( tr.StartedSolid || tr.Hit && tr.Fraction < 0.52f ) continue;     // stopped before the door's plane
					through[edge] = through.GetValueOrDefault( edge ) + 1;
				}
			}

			checkedDoors++;
			if ( through.Count == 0 ) continue;
			gapDoors++;
			Log.Info( $"[gaps] {door.DoorId} at {door.WorldPosition:0}: {through.Values.Sum()}/{rays} rays through ({string.Join( ", ", through.Select( kv => $"{kv.Key} {kv.Value}" ) )})" );
		}
		Log.Info( $"[gaps] checked {checkedDoors} closed doors, {gapDoors} with gaps" );
	}

	/// <summary>
	/// Read-only: sweep a trooper-sized box (your hull at the current player scale, feet lifted by the step height) straight
	/// through every doorway, both ways, and name whatever it bumps into: the map, a prop chunk, something spawned in game,
	/// or the door's own panel collision while the door is open. Closed doors skip their own panels.
	/// </summary>
	[ConCmd( "swrp_doorway_check", Help = "swrp_doorway_check [door id or name] - sweep a trooper-sized box through every doorway and report what blocks it (read-only)" )]
	public static void DoorwayCheck( string filter = "" )
	{
		if ( Game.ActiveScene is not { } scene ) return;
		var scale = SwrpPlayer.PlayerScaleFor( scene );
		var pc = SwrpPlayer.Local?.Controller;
		var height = pc.IsValid() ? pc.BodyHeight : 72f * scale;
		var radius = pc.IsValid() ? pc.BodyRadius : 16f;
		var step = 18f * scale;
		int checkedDoors = 0, blocked = 0;
		foreach ( var door in scene.GetAllComponents<SwrpDoor>().OrderBy( d => d.DoorId ) )
		{
			if ( !string.IsNullOrEmpty( filter ) && door.DoorId != filter && !door.DisplayName.Contains( filter, StringComparison.OrdinalIgnoreCase ) ) continue;
			var data = SwrpDoorGeometry.Get( door.DoorId );
			if ( data is null || data.Parts.Count == 0 ) continue;

			var lo = data.Parts.Select( p => p.Lo.ToVector() ).Aggregate( ( x, y ) => x.ComponentMin( y ) ) + door.WorldPosition;
			var hi = data.Parts.Select( p => p.Hi.ToVector() ).Aggregate( ( x, y ) => x.ComponentMax( y ) ) + door.WorldPosition;
			var size = hi - lo;
			int thin = size.x <= size.y ? 0 : 1, wide = 1 - thin;
			var center = ( lo + hi ) * 0.5f;
			checkedDoors++;

			var problems = new List<string>();
			if ( size[wide] < radius * 2f + 2f ) problems.Add( $"opening {size[wide]:0} wide, hull needs {radius * 2f:0}" );
			if ( size.z < height + step * 0.5f ) problems.Add( $"opening {size.z:0} tall, hull is {height:0}" );

			// the hull, feet lifted by the step height, no taller than the opening
			var half = Vector3.Zero;
			half[wide] = radius;
			half[thin] = radius;
			var top = MathF.Min( height, size.z - 2f );
			var box = new BBox( new Vector3( -half.x, -half.y, step ), new Vector3( half.x, half.y, MathF.Max( step + 4f, top ) ) );
			var floor = center.WithZ( lo.z );

			foreach ( var side in new[] { -1f, 1f } )
			{
				var from = floor; from[thin] -= side * 64f;
				var to = floor; to[thin] += side * 64f;
				var trace = scene.Trace.Box( box, from, to ).WithoutTags( "player" );
				if ( !door.IsOpen ) trace = trace.IgnoreGameObjectHierarchy( door.GameObject );
				var tr = trace.Run();
				if ( !tr.Hit ) continue;
				var what = tr.GameObject;
				var path = what is null ? "?" : string.Join( "/", new[] { what.Parent?.Parent?.Name, what.Parent?.Name, what.Name }.Where( n => !string.IsNullOrEmpty( n ) && n != scene.Name ) );
				var own = what is not null && what.IsDescendant( door.GameObject ) ? " (this door's own panel, still solid while open)" : "";
				var model = what?.GetComponent<ModelCollider>()?.Model?.ResourcePath ?? what?.GetComponent<ModelRenderer>()?.Model?.ResourcePath;
				problems.Add( $"{( side < 0 ? "walking +" : "walking -" )}{( thin == 0 ? "x" : "y" )}: {( tr.StartedSolid ? "starts inside" : $"hits at {tr.Distance - 64f:+0;-0} from the door" )} {path}{own} [{tr.Collider?.GetType().Name ?? "no collider"}{( model is null ? "" : $" {model}" )}] at {tr.HitPosition.x:0} {tr.HitPosition.y:0} {tr.HitPosition.z:0}" );
			}
			if ( problems.Count == 0 ) continue;
			blocked++;
			Log.Info( $"[doorway] {door.DoorId} ({door.DisplayName}, {( door.IsOpen ? "open" : "closed" )}) at {center.x:0} {center.y:0} {lo.z:0}: {string.Join( " | ", problems )}" );
		}
		Log.Info( $"[doorway] checked {checkedDoors} doorways with a {radius * 2f:0} x {height:0} hull (step {step:0}): {blocked} with something in the way" );
	}

	/// <summary> Says whether your character's rank and faction would get through a door, ignoring staff rights. </summary>
	[ConCmd( "swrp_door_check", Help = "swrp_door_check <door id> - would your character (not counting staff rights) get through?" )]
	public static void DoorCheck( string doorId )
	{
		var door = Game.ActiveScene?.GetAllComponents<SwrpDoor>().FirstOrDefault( d => d.DoorId == doorId );
		if ( door is null ) { Log.Warning( $"[SWRP] No door '{doorId}'." ); return; }
		var reason = door.CheckAccess( SwrpPlayer.Local, null );
		Log.Info( $"[SWRP] {door.DisplayName} ({door.AccessLabel}{(door.Locked ? ", locked" : "")}): {reason ?? "access granted"}" );
	}

	/// <summary> Host only (the host is always an admin): move your trooper to a map position. For building and testing. </summary>
	[ConCmd( "swrp_tp", Help = "swrp_tp <x> <y> <z> [yaw] - host only: teleport your trooper to map coordinates" )]
	public static void Teleport( float x, float y, float z, float yaw = 0 )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[SWRP] swrp_tp is host only." ); return; }
		SwrpPlayer.Local?.TeleportTo( new Vector3( x, y, z ), yaw );
	}

	[ConCmd( "swrp_chatlog", Help = "swrp_chatlog [count] - print your most recent chat lines to the console" )]
	public static void ChatLog( int count = 10 )
	{
		foreach ( var line in SwrpChat.Lines.TakeLast( Math.Max( 1, count ) ) )
			Log.Info( $"[chat] {line.Text}" );
	}

	[ConCmd( "swrp_factions", Help = "List faction ids" )]
	public static void ListFactions()
	{
		foreach ( var f in FactionRegistry.All )
			Log.Info( $"{f.Id,-16} {f.Name}{(f.Whitelisted ? " (whitelisted)" : "")}" );
	}
}
