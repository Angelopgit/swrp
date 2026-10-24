using System;

namespace Swrp;

/// <summary>
/// An item lying in the world: dropped from an inventory (the inventory's Drop button, or G for the weapon in your hands),
/// picked up again with E. It falls and tumbles with physics; weapons clatter with a metal sound when they hit something.
/// Weapons keep the rounds left in their magazine. The host owns it; it disappears after <see cref="Lifetime"/> seconds.
/// </summary>
[Title( "SWRP World Item" ), Category( "SWRP" ), Icon( "inventory_2" )]
public sealed class SwrpWorldItem : Component, Component.ICollisionListener
{
	public const float Lifetime = 600f;
	public const float Reach = 140f;

	[Sync( SyncFlags.FromHost )] public string ItemId { get; set; }
	[Sync( SyncFlags.FromHost )] public int Count { get; set; } = 1;

	/// <summary> Rounds left in a dropped weapon's magazine (-1 = unknown, a full one). </summary>
	[Sync( SyncFlags.FromHost )] public int Clip { get; set; } = -1;

	public ItemDefinition Definition => ItemRegistry.Get( ItemId );

	TimeSince _age;
	TimeSince _sinceSound = 10;

	/// <summary> Host: put an item into the world, thrown with a velocity. </summary>
	public static SwrpWorldItem Spawn( Scene scene, string itemId, int count, Vector3 position, Rotation rotation, Vector3 velocity, int clip = -1 )
	{
		var def = ItemRegistry.Get( itemId );
		if ( def is null || count <= 0 ) return null;

		var go = new GameObject( true, def.Name );
		go.WorldPosition = position;
		go.WorldRotation = rotation;
		go.Tags.Add( "worlditem" );
		var item = go.AddComponent<SwrpWorldItem>();
		item.ItemId = itemId;
		item.Count = count;
		item.Clip = clip;

		// a box around the model to collide and to aim E at (the model itself is drawn by each machine, see OnStart)
		var (model, place) = VisualFor( def );
		var box = go.AddComponent<BoxCollider>();
		if ( model is not null )
		{
			var t = place.HasValue ? new Transform( place.Value.Offset, place.Value.Angles.ToRotation(), place.Value.Scale ) : new Transform( Vector3.Zero, Rotation.Identity, 1f );
			var bounds = model.Bounds;
			box.Center = t.Position + t.Rotation * bounds.Center * t.Scale.x;
			var size = ( t.Rotation * bounds.Size ).Abs() * t.Scale.x + 1f;
			box.Scale = new Vector3( MathF.Max( size.x, 5f ), MathF.Max( size.y, 5f ), MathF.Max( size.z, 5f ) );      // small things stay easy to aim E at
		}
		else box.Scale = new Vector3( 8f );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 6f;
		rb.Velocity = velocity;
		rb.AngularVelocity = Vector3.Random * 4f;

		go.NetworkSpawn();
		return item;
	}

	protected override void OnStart()
	{
		_age = 0;
		BuildVisual();
	}

	/// <summary>
	/// What an item looks like: a blaster's model with its placement in the grip frame, else the item's own model
	/// (ItemModels). Null for an unknown item.
	/// </summary>
	static (Model Model, (Vector3 Offset, Angles Angles, float Scale)? Place) VisualFor( ItemDefinition def )
	{
		if ( def is null ) return (null, null);
		if ( def.Weapon?.ModelPath is { } path )
			return (Model.Load( path ), (def.Weapon.ModelOffset, def.Weapon.ModelAngles, def.Weapon.ModelScale));
		return (ItemModels.For( def.Id ), null);
	}

	string _shownItem;

	/// <summary> Every machine: the item's model as a local child. </summary>
	void BuildVisual()
	{
		if ( _shownItem == ItemId ) return;
		_shownItem = ItemId;
		GameObject.Children.FirstOrDefault( c => c.Name == "Visual" )?.Destroy();
		var (model, place) = VisualFor( Definition ?? new ItemDefinition { Id = ItemId } );
		if ( model is null ) return;
		var mesh = new GameObject( GameObject, true, "Visual" );
		mesh.NetworkMode = NetworkMode.Never;
		mesh.Flags |= GameObjectFlags.NotSaved;
		if ( place is { } p )
		{
			mesh.LocalPosition = p.Offset;
			mesh.LocalRotation = p.Angles.ToRotation();
			mesh.LocalScale = p.Scale;
		}
		var r = mesh.AddComponent<ModelRenderer>();
		r.Model = model;
	}

	protected override void OnUpdate()
	{
		if ( _shownItem != ItemId ) BuildVisual();      // the item id can arrive a moment after the object on clients
	}

	[ConCmd( "swrp_item_spawn", Help = "swrp_item_spawn <item id> [count] - host only: throw an item into the world in front of the camera (to test pickups)" )]
	public static void ConsoleSpawn( string itemId, int count = 1 )
	{
		if ( !Networking.IsHost || Game.ActiveScene?.Camera is not { } cam ) return;
		var fwd = cam.WorldRotation.Forward;
		Spawn( cam.Scene, itemId, Math.Max( 1, count ), cam.WorldPosition + fwd * 60f, Rotation.LookAt( fwd.WithZ( 0 ) ), fwd * 120f + Vector3.Up * 40f );
	}

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost && _age > Lifetime ) GameObject.Destroy();
	}

	void ICollisionListener.OnCollisionStart( Collision c )
	{
		if ( !Networking.IsHost || _sinceSound < 0.12f ) return;
		var speed = c.Contact.Speed.Length;
		if ( speed < 60f ) return;
		_sinceSound = 0;
		Clatter( Math.Clamp( speed / 400f, 0.25f, 1f ) );
	}

	void ICollisionListener.OnCollisionUpdate( Collision c ) { }
	void ICollisionListener.OnCollisionStop( CollisionStop c ) { }

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Clatter( float strength )
	{
		var h = Sound.Play( Definition?.Weapon is not null ? SwrpAudio.WeaponDrop : SwrpAudio.WeaponPickup, WorldPosition );
		if ( h is not null ) h.Volume *= strength;
	}

	/// <summary> Client: E on the item. </summary>
	[Rpc.Host]
	public void RequestPickup()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !p.IsValid() || p.CharacterId is null || p.Jailed || !GameObject.IsValid() ) return;
		if ( p.WorldPosition.Distance( WorldPosition ) > Reach + 60f ) return;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } ) return;

		var record = CharacterStore.Find( p.OwnerSteamId, p.CharacterId );
		if ( record is null ) return;
		var left = record.AddItem( ItemId, Count );
		if ( left >= Count )
		{
			SwrpNet.NotifyConnection( caller, "Your inventory is full.", true );
			return;
		}
		if ( Clip >= 0 && Definition?.Weapon is not null && !record.WeaponClips.ContainsKey( ItemId ) ) record.WeaponClips[ItemId] = Clip;
		CharacterStore.Save( p.OwnerSteamId );
		p.InventoryJson = Json.Serialize( record.Inventory );
		Pickup( p.WorldPosition );
		if ( Definition?.Weapon is not null ) p.GetComponent<SwrpBlaster>()?.OfferPickedUp( ItemId );      // empty hands take it out
		SwrpNet.NotifyConnection( caller, $"Picked up {( Count - left > 1 ? $"{Count - left}x " : "" )}{Definition?.Name ?? ItemId}.", false );

		if ( left > 0 ) Count = left;
		else GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Pickup( Vector3 at ) => Sound.Play( SwrpAudio.WeaponPickup, at + Vector3.Up * 40f );
}
