using System;
using System.Text.RegularExpressions;

namespace Swrp;

/// <summary>
/// Staff build tools, after Facepunch's Sandbox (MIT; the ported code is in Code/Build, see LICENSE-facepunch-sandbox.txt there):
/// hold Q for the spawn menu (NPCs, weapons, craft, props from our list or searched on sbox.game, the tools). A picked prop goes
/// on the crosshair as a ghost to place (left mouse places, E + mouse turns it, Shift snaps 45°, right mouse puts it away); the
/// physgun (<see cref="SwrpPhysgun"/>) and tool gun (<see cref="SwrpToolgun"/>) work like Sandbox's; Z undoes your last spawn or
/// constraint (<see cref="SwrpBuildUndo"/>). Staff and Game Masters only; spawns and removals go in the staff log.
/// </summary>
public static class SwrpBuild
{
	public const string PropTag = "staffprop";

	public static readonly (string Name, string Model)[] Props =
	{
		( "Imperial crate", "models/empirecratecubebig.vmdl" ),
		( "Small crate", "models/empirecratecubesmall.vmdl" ),
		( "Cargo container", "models/starwars_empirecontaineropen.vmdl" ),
		( "Crate", "models/citizen_props/crate01.vmdl" ),
		( "Cardboard box", "models/citizen_props/cardboardbox01.vmdl" ),
		( "Chair", "models/citizen_props/chair02.vmdl" ),
		( "Office chair", "models/citizen_props/chair04blackleather.vmdl" ),
		( "Barrier", "models/citizen_props/concreteroaddivider01.vmdl" ),
		( "Bin", "models/citizen_props/trashcan02.vmdl" ),
		( "Cone", "models/citizen_props/roadcone01.vmdl" ),
		( "Mug", "models/citizen_props/coffeemug01.vmdl" ),
		( "Crowbar", "models/citizen_props/crowbar01.vmdl" ),
		( "Mannequin", "models/citizen_mannequin/mannequin.vmdl" ),
	};

	public enum Tool { None, Physgun, Toolgun, Spawner }

	// ----- client state -----

	/// <summary> Client: the host said this player may use the build tools. </summary>
	public static bool Allowed { get; private set; }
	public static Tool Active { get; set; }

	/// <summary> Client: the tool gun's mode (<see cref="BuildToolMode.Id"/>). </summary>
	public static string ToolMode { get; set; } = "remover";

	/// <summary> Client: what the spawner places (a model path or an sbox.game ident) and its name. </summary>
	public static string SpawnModel { get; set; }
	public static string SpawnName { get; set; }

	/// <summary> Client: something is held by the physgun right now (the mouse wheel moves it, not the camera). </summary>
	public static bool Grabbing { get; set; }

	/// <summary> Client: E belongs to the build tool right now (turning a held or placed prop), not to doors and consoles. </summary>
	public static bool UsesE => Allowed && ( Grabbing || Active == Tool.Spawner && SpawnModel is not null );

	/// <summary> Client: a short line from the host under the tool hint ("Unfroze 3 objects"). </summary>
	public static string Notice { get; private set; }
	public static RealTimeSince SinceNotice { get; private set; } = 99;

	public static void Pick( string model, string name )
	{
		SpawnModel = model;
		SpawnName = name;
		Active = Tool.Spawner;
	}

	[Rpc.Host]
	public static void RequestAccess()
	{
		var caller = Rpc.Caller;
		using ( Rpc.FilterInclude( caller ) ) ReceiveAccess( May( caller ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveAccess( bool allowed )
	{
		Allowed = allowed;
		if ( !allowed ) { Active = Tool.None; SwrpUiState.BuildOpen = false; }
	}

	public static bool May( Connection c ) => c is not null && ( Whitelist.IsStaff( c ) || SwrpGameMaster.IsGameMaster( c ) );

	public static void Log( Connection c, string text )
	{
		SwrpChat.StaffLog( $"[BUILD] {c.DisplayName} {text}" );
		SwrpStaffTools.RecordText( c, "build", text );
	}

	/// <summary> Host: a line under the caller's tool hint. </summary>
	public static void Notify( Connection c, string text )
	{
		using ( Rpc.FilterInclude( c ) ) ReceiveNotice( text );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveNotice( string text )
	{
		Notice = text;
		SinceNotice = 0;
	}

	// ----- spawning (host) -----

	static readonly Regex CloudIdent = new( @"^[a-z0-9_\-]+\.[a-z0-9_\-]+$", RegexOptions.IgnoreCase );

	/// <summary> Our list, any model in the game's files, or an sbox.game model package (searched in the Q menu). </summary>
	public static bool IsCloud( string model ) => model is not null && CloudIdent.IsMatch( model );
	static bool Spawnable( string model ) => model is not null && ( Props.Any( p => p.Model == model ) || model.EndsWith( ".vmdl" ) || IsCloud( model ) || Entities.Any( e => e.Id == model ) );

	/// <summary> Sandbox's entities tab, the ones that fit SWRP: seats that drive contraptions, and lights (colour from the colour tool). </summary>
	public static readonly (string Id, string Name, string Model)[] Entities =
	{
		( "entity:seat:chair02", "Seat", "models/citizen_props/chair02.vmdl" ),
		( "entity:seat:chair04blackleather", "Office seat", "models/citizen_props/chair04blackleather.vmdl" ),
		( "entity:seat:chair05bluefabric", "Armchair seat", "models/citizen_props/chair05bluefabric.vmdl" ),
		( "entity:seat:chair01", "Wooden seat", "models/citizen_props/chair01.vmdl" ),
		( "entity:light", "Light", "models/dev/sphere.vmdl" ),
		( "entity:spot", "Spotlight", "models/dev/sphere.vmdl" ),
	};

	/// <summary> The model the spawner's ghost shows for a pick (an entity's own model). </summary>
	public static string GhostModel( string pick ) => Entities.FirstOrDefault( e => e.Id == pick ).Model ?? pick;

	/// <summary> How big an entity is drawn and spawned (the lights are a small bulb made from the dev sphere). </summary>
	public static float GhostScale( string pick ) => pick is "entity:light" or "entity:spot" ? 0.12f : 1f;

	/// <summary> Place a prop where the spawner's ghost stands (Sandbox's spawner weapon). </summary>
	[Rpc.Host]
	public static async void SpawnPropAt( string model, Vector3 at, Rotation rotation, Color tint )
	{
		var caller = Rpc.Caller;
		if ( !May( caller ) || !Spawnable( model ) ) return;
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !me.IsValid() || !me.Controller.IsValid() ) { SwrpChat.SystemTo( caller, "Deploy (noclip is fine) to spawn props where you aim." ); return; }
		if ( me.Controller.EyePosition.Distance( at ) > SwrpPhysgun.Range + 200f ) return;

		if ( Entities.FirstOrDefault( e => e.Id == model ) is { Id: not null } entity )
		{
			EntityColour = tint;
			var made = SpawnEntity( entity, at, rotation );
			if ( made is null ) return;
			SwrpBuildUndo.Add( caller, entity.Name, made );
			Log( caller, $"spawned a {entity.Name.ToLowerInvariant()} at {SwrpShip.DeckName( at )}" );
			return;
		}

		Model mdl;
		if ( IsCloud( model ) )
		{
			PreloadCloud( model );      // everyone fetches the package too, so they can see it
			mdl = await Cloud.Load<Model>( model );
		}
		else mdl = Model.Load( model );
		if ( mdl is null || mdl.IsError ) { SwrpChat.SystemTo( caller, $"Couldn't load {model}." ); return; }

		var name = Props.FirstOrDefault( p => p.Model == model ).Name ?? mdl.ResourceName ?? "Prop";
		var go = CreateProp( mdl, name, at, rotation, false );
		if ( go is null ) return;
		SwrpBuildUndo.Add( caller, name, go );
		Log( caller, $"spawned a {go.Name}{( IsCloud( model ) ? $" ({model})" : "" )} at {SwrpShip.DeckName( at )}" );
	}

	public static Color EntityColour { get; set; } = Color.White;

	static GameObject SpawnEntity( (string Id, string Name, string Model) e, Vector3 at, Rotation rotation )
	{
		var mdl = Model.Load( e.Model );
		if ( mdl is null || mdl.IsError ) return null;
		if ( e.Id.StartsWith( "entity:seat:" ) )
			return CreateProp( mdl, e.Name, at, rotation, false, g => g.AddComponent<SwrpSeat>() );

		// a small glowing bulb; frozen where you put it, the physgun moves it
		var spot = e.Id == "entity:spot";
		var colour = EntityColour;
		return CreateProp( mdl, e.Name, at, rotation, true, g =>
		{
			g.WorldScale = 0.12f;
			g.Components.Get<ModelRenderer>().Tint = colour;
			var lamp = new GameObject( g, true, "lamp" );
			if ( spot )
			{
				var l = lamp.AddComponent<SpotLight>();
				l.LightColor = colour * 3f;
				l.Radius = 900f;
				l.ConeOuter = 40f;
				l.ConeInner = 25f;
				l.Shadows = true;
			}
			else
			{
				var l = lamp.AddComponent<PointLight>();
				l.LightColor = colour * 2f;
				l.Radius = 400f;
				l.Shadows = false;
			}
		} );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PreloadCloud( string ident )
	{
		if ( !Networking.IsHost ) _ = Cloud.Load<Model>( ident );
	}

	/// <summary> Host: a networked staff prop (also how a saved event layout comes back, see <see cref="SwrpEvents"/>). </summary>
	public static GameObject CreateProp( string model, string name, Vector3 at, Rotation rotation, bool frozen )
	{
		var mdl = Model.Load( model );
		if ( mdl is null || mdl.IsError ) return null;
		return CreateProp( mdl, name, at, rotation, frozen );
	}

	/// <summary> <paramref name="configure"/> runs before the prop is networked (gadget components, joints). </summary>
	public static GameObject CreateProp( Model mdl, string name, Vector3 at, Rotation rotation, bool frozen, Action<GameObject> configure = null )
	{
		var go = new GameObject( true, string.IsNullOrEmpty( name ) ? "Prop" : name );
		go.WorldPosition = at;
		go.WorldRotation = rotation;
		go.Tags.Add( PropTag );

		if ( IsRagdoll( mdl ) )
		{
			// a character (a clone, a droid): a ragdoll like Sandbox's, every limb its own body
			var skinned = go.AddComponent<SkinnedModelRenderer>();
			skinned.Model = mdl;
			skinned.UseAnimGraph = false;
			var physics = go.AddComponent<ModelPhysics>();
			physics.Model = mdl;
			physics.Renderer = skinned;
			if ( frozen ) physics.MotionEnabled = false;
			configure?.Invoke( go );
			go.NetworkSpawn();
			return go;
		}

		go.AddComponent<ModelRenderer>().Model = mdl;
		var col = go.AddComponent<ModelCollider>();
		col.Model = mdl;
		if ( !mdl.Physics?.Parts.Any() ?? true )
		{
			// no collision mesh in the model: a box around it
			col.Enabled = false;
			var box = go.AddComponent<BoxCollider>();
			box.Center = mdl.Bounds.Center;
			box.Scale = mdl.Bounds.Size;
		}
		var rb = go.AddComponent<Rigidbody>();
		if ( frozen ) rb.MotionEnabled = false;
		configure?.Invoke( go );
		go.NetworkSpawn();
		return go;
	}

	/// <summary> A model with bones and more than one physics body spawns as a ragdoll. </summary>
	public static bool IsRagdoll( Model mdl ) => mdl is not null && ( mdl.Physics?.Parts.Count ?? 0 ) > 1 && mdl.BoneCount > 1;

	/// <summary>
	/// Host: a bolt or blast landed here (hook: SwrpCombat.Impact). A loose staff prop, gadget, dropped item or ragdoll limb there
	/// gets knocked back, like Sandbox's bullets push props.
	/// </summary>
	public static void HostImpact( Scene scene, Vector3 pos, Vector3 normal )
	{
		if ( !Networking.IsHost || scene is null ) return;
		var tr = scene.Trace.Ray( pos + normal * 3f, pos - normal * 6f ).WithoutTags( "trigger", "player" ).Run();
		if ( !tr.Hit || Grabbable( tr.GameObject ) is null ) return;
		var rb = tr.GameObject.Components.GetInAncestorsOrSelf<Rigidbody>();
		if ( rb is null || !rb.MotionEnabled ) return;
		rb.ApplyImpulseAt( tr.HitPosition, -normal * 1500f );
	}

	/// <summary> The spawn menu's WEAPONS row: every weapon, plus blaster cells and thermal detonators. </summary>
	public static IEnumerable<ItemDefinition> SpawnableItems => ItemRegistry.All.Where( i => i.Category is ItemCategory.Weapon or ItemCategory.Ammo or ItemCategory.Explosive );

	/// <summary> How many of an item one click drops (one weapon; a handful of cells or detonators). </summary>
	public static int SpawnCount( ItemDefinition def ) => def.Category == ItemCategory.Weapon ? 1 : Math.Clamp( def.MaxStack, 1, 10 );

	/// <summary> Drop a weapon (or ammo) where you aim, as a world item anyone can pick up with E (and the physgun can carry). </summary>
	[Rpc.Host]
	public static void SpawnItem( string itemId )
	{
		var caller = Rpc.Caller;
		var def = ItemRegistry.Get( itemId );
		if ( !May( caller ) || def is null || !SpawnableItems.Contains( def ) ) return;
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !me.IsValid() || !me.Controller.IsValid() ) { SwrpChat.SystemTo( caller, "Deploy (noclip is fine) to spawn weapons where you aim." ); return; }

		var eye = me.Controller.EyePosition;
		var fwd = me.Controller.EyeAngles.ToRotation().Forward;
		var tr = me.Scene.Trace.Ray( eye, eye + fwd * 1500f ).IgnoreGameObjectHierarchy( me.GameObject ).WithoutTags( "trigger" ).Run();
		var at = tr.EndPosition + tr.Normal * 14f;
		var count = SpawnCount( def );
		// lying across your view, so you see the whole gun
		var item = SwrpWorldItem.Spawn( me.Scene, itemId, count, at, Rotation.FromYaw( me.Controller.EyeAngles.yaw + 90f ), Vector3.Zero );
		if ( item is null ) return;
		SwrpBuildUndo.Add( caller, def.Name, item.GameObject );
		Log( caller, $"spawned {( count > 1 ? $"{count}x " : "a " )}{def.Name} at {SwrpShip.DeckName( at )}" );
	}

	/// <summary> What the build tools may touch: staff props, NPCs and dropped items (never the map, doors or players). </summary>
	public static GameObject Grabbable( GameObject hit )
	{
		var root = hit?.Root;
		if ( root is null ) return null;
		if ( root.Tags.Has( PropTag ) || root.Components.Get<SwrpNpc>() is not null || root.Components.Get<SwrpWorldItem>() is not null ) return root;
		return null;
	}
}
