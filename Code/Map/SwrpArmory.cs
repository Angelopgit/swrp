using System;

namespace Swrp;

/// <summary>
/// Something in the world you use with E that isn't a door or one of the older special cases: the armory racks and
/// crates, the bacta pods. <see cref="InteractPrompt"/> asks it what to show and calls <see cref="Use"/> on E.
/// </summary>
public interface ISwrpUsable
{
	/// <summary> The prompt: title, the line under it, and its tone (ok, info, denied, ok restricted). </summary>
	(string Title, string Sub, string Tone) Prompt( SwrpPlayer me );

	/// <summary> Client: E was pressed on it (send the request to the host). </summary>
	void Use( SwrpPlayer me );
}

/// <summary>
/// The armory's stock: a wall rack of DC-15A rifles (E takes one into your inventory; the rack restocks one at a time)
/// or a blaster-cell crate (E takes a handful of cells, once every little while). Host-authoritative: the host checks
/// the trooper and the stock, writes the inventory and syncs what's left. The fitting finds its own wall (or floor) when
/// it starts, like the wall screens, and each machine hangs the rifles it shows on it.
/// </summary>
[Title( "SWRP Armory" ), Category( "SWRP" ), Icon( "security" )]
public sealed class SwrpArmory : Component, ISwrpUsable
{
	/// <summary> rifles (a wall rack) or ammo (a crate on the floor against a wall). </summary>
	[Property] public string Kind { get; set; } = "rifles";
	/// <summary> Which way to look for the wall it goes on. </summary>
	[Property] public Vector3 Probe { get; set; } = Vector3.Forward;

	public const int Slots = 5;
	public const float RestockSeconds = 90f;
	public const int MaxCarried = 2;
	public const int CellsPerTake = 6;
	public const float CellCooldown = 20f;
	public const float Reach = 170f;
	const string RifleId = "dc15a";
	const string CellId = "blaster_cell";

	[Sync( SyncFlags.FromHost )] public int Stock { get; set; } = Slots;

	bool IsRack => Kind != "ammo";

	GameObject _fitting;
	readonly List<GameObject> _rifles = new();
	int _shownStock = -1;
	bool _placed;
	TimeSince _sinceRestock;
	readonly Dictionary<ulong, RealTimeSince> _lastCells = new();
	RealTimeSince _sinceLocalCells = 100f;

	protected override void OnUpdate()
	{
		if ( !_placed )
		{
			_placed = true;
			Place();
		}

		if ( Networking.IsHost && IsRack )
		{
			if ( Stock >= Slots ) _sinceRestock = 0;
			else if ( _sinceRestock > RestockSeconds )
			{
				_sinceRestock = 0;
				Stock++;
			}
		}

		if ( IsRack && _shownStock != Stock )
		{
			_shownStock = Stock;
			for ( var i = 0; i < _rifles.Count; i++ )
				if ( _rifles[i].IsValid() ) _rifles[i].Enabled = i < Stock;
		}
	}

	void Place()
	{
		var model = Model.Load( IsRack ? "models/armory/rifle_rack.vmdl" : "models/armory/ammo_crate.vmdl" );
		Vector3 at, n;
		if ( IsRack )
		{
			if ( !SwrpLivingShip.FindMount( Scene, WorldPosition, Probe, 70f, 41f, 58f, 14f, out at, out n ) )
			{
				Log.Warning( $"[SWRP] Armory rack at {SwrpLivingShip.At( WorldPosition )}: no flat wall found" );
				return;
			}
		}
		else
		{
			// a crate stands on the floor with its back to the nearest wall the probe finds
			if ( !SwrpLivingShip.FindWall( Scene, WorldPosition, Probe, out var wall, out n ) ) { wall = WorldPosition; n = -Probe.Normal; }
			n = n.WithZ( 0 ).Normal;
			var spot = wall + n * 18f;
			at = spot.WithZ( SwrpLivingShip.FloorBelow( Scene, spot ) );
		}

		_fitting = SwrpLivingShip.Child( GameObject, IsRack ? "rifle_rack" : "ammo_crate" );
		_fitting.WorldPosition = at;
		_fitting.WorldRotation = Rotation.LookAt( n, Vector3.Up );
		var r = _fitting.Components.Create<ModelRenderer>();
		r.Model = model;
		var col = _fitting.Components.Create<ModelCollider>();
		col.Model = model;
		col.Static = true;

		if ( !IsRack ) return;

		// the rifles hang upright in the cradles, barrel up, their left side to the room
		var def = ItemRegistry.Get( RifleId );
		var path = def?.Weapon?.ModelPath;
		if ( path is null ) return;
		var along = _fitting.WorldRotation.Right;
		for ( var i = 0; i < Slots; i++ )
		{
			var holder = SwrpLivingShip.Child( _fitting, $"rifle_{i}" );
			holder.LocalPosition = new Vector3( 9f, ( i - ( Slots - 1 ) / 2f ) * 24f, -16f );
			holder.WorldRotation = Rotation.LookAt( Vector3.Up, -along );
			var mesh = SwrpLivingShip.Child( holder, "Mesh" );
			var mr = mesh.Components.Create<ModelRenderer>();
			mr.Model = Model.Load( path );
			mr.RenderType = ModelRenderer.ShadowRenderType.Off;
			def.Weapon.PlaceMesh( mesh );
			_rifles.Add( holder );
		}
		_shownStock = -1;
	}

	int Carried( SwrpPlayer me, string itemId ) => me.Inventory.Where( s => s.ItemId == itemId ).Sum( s => s.Count );

	public (string Title, string Sub, string Tone) Prompt( SwrpPlayer me )
	{
		if ( IsRack )
		{
			if ( Stock <= 0 ) return ("Rifle rack empty", $"Restocks one every {RestockSeconds:0} s", "denied");
			if ( Carried( me, RifleId ) >= MaxCarried ) return ("Take a DC-15A", $"You already carry {MaxCarried}", "denied");
			return ("Take a DC-15A", $"Armory · {Stock} of {Slots} on the rack", "ok");
		}
		var wait = CellCooldown - _sinceLocalCells;
		if ( wait > 0f ) return ("Blaster cell crate", $"Take more in {MathF.Ceiling( wait ):0} s", "denied");
		return ($"Take {CellsPerTake} blaster cells", $"Armory · you carry {Carried( me, CellId )}", "ok");
	}

	public void Use( SwrpPlayer me )
	{
		if ( !IsRack ) _sinceLocalCells = 0;
		RequestTake();
	}

	[Rpc.Host]
	void RequestTake()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !p.IsValid() || p.CharacterId is null || p.Jailed || !_fitting.IsValid() ) return;
		if ( p.WorldPosition.Distance( _fitting.WorldPosition ) > Reach + 80f ) return;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } ) return;
		var record = CharacterStore.Find( p.OwnerSteamId, p.CharacterId );
		if ( record is null ) return;
		var steam = caller.SteamId.ValueUnsigned;

		if ( IsRack )
		{
			if ( Stock <= 0 ) { SwrpNet.NotifyConnection( caller, "The rack is empty. It restocks shortly.", true ); return; }
			if ( record.Inventory.Where( s => s.ItemId == RifleId ).Sum( s => s.Count ) >= MaxCarried )
			{
				SwrpNet.NotifyConnection( caller, $"You already carry {MaxCarried} DC-15As.", true );
				return;
			}
			if ( record.AddItem( RifleId, 1 ) > 0 ) { SwrpNet.NotifyConnection( caller, "Your inventory is full.", true ); return; }
			Stock--;
			Taken( _fitting.WorldPosition );
			SwrpNet.NotifyConnection( caller, "Took a DC-15A from the armory.", false );
		}
		else
		{
			if ( _lastCells.TryGetValue( steam, out var since ) && since < CellCooldown )
			{
				SwrpNet.NotifyConnection( caller, $"Wait {MathF.Ceiling( CellCooldown - since ):0} s before taking more cells.", true );
				return;
			}
			var left = record.AddItem( CellId, CellsPerTake );
			if ( left >= CellsPerTake ) { SwrpNet.NotifyConnection( caller, "Your inventory is full.", true ); return; }
			_lastCells[steam] = 0;
			Taken( _fitting.WorldPosition );
			SwrpNet.NotifyConnection( caller, $"Took {CellsPerTake - left} blaster cells.", false );
		}
		CharacterStore.Save( p.OwnerSteamId );
		p.InventoryJson = Json.Serialize( record.Inventory );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Taken( Vector3 at ) => Sound.Play( SwrpAudio.WeaponPickup, at + Vector3.Up * 20f );

	[ConCmd( "swrp_armory_info", Help = "swrp_armory_info - list the armory racks and crates, their stock and where they ended up" )]
	public static void InfoCommand()
	{
		foreach ( var a in Game.ActiveScene?.GetAllComponents<SwrpArmory>() ?? Enumerable.Empty<SwrpArmory>() )
			Log.Info( $"[SWRP] armory {a.Kind} at {( a._fitting.IsValid() ? SwrpLivingShip.At( a._fitting.WorldPosition ) : "nowhere (no wall)" )}, stock {a.Stock}" );
	}
}
