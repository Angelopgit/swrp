using System;
using Swrp.Jedi;

namespace Swrp;

/// <summary>
/// What the trooper has in their hands, and the one place that changes it. The quick bar is everything you can take out:
/// the lightsaber, then primaries, sidearms, then gear held for roleplay (items with <see cref="ItemDefinition.Holdable"/>,
/// a datapad, binders, a stun baton). Keys 1-5 take out quick slots 1-5 (the same key again puts it away), the mouse wheel
/// and mouse 4 / 5 go through the whole bar (in first and third person; hold Walk to zoom the third-person camera
/// instead), H puts away what's in hand or takes the last thing out again, G drops what's in hand, and left click with
/// gear in hand acts it out (/me). The blaster itself still lives in <see cref="SwrpBlaster"/> and the saber in
/// <see cref="JediCombat"/>; this only chooses between them. Gear in hand is owner-synced and drawn by every machine.
/// </summary>
public sealed class SwrpHands : Component
{
	public const int QuickSlots = 5;

	/// <summary> Owner controlled: the gear item held for roleplay (never a blaster or the saber), null for none. </summary>
	[Sync] public string HeldItem { get; set; }

	SwrpPlayer _player;
	SwrpPlayer Player => _player ??= GetComponent<SwrpPlayer>();
	SwrpBlaster Blaster => GetComponent<SwrpBlaster>();
	JediCombat Jedi => GetComponent<JediCombat>();
	SwrpVitals Vitals => GetComponent<SwrpVitals>();

	public static SwrpHands Local => SwrpPlayer.Local?.GetComponent<SwrpHands>();

	bool SaberLit => Jedi is { } j && ( j.SaberOn || j.Thrown );

	/// <summary> The held gear, if it's really in hand: still carried, and no blaster or saber up. </summary>
	public string HeldGear
	{
		get
		{
			var id = HeldItem;
			if ( id is null || Player is null || Blaster is { Drawn: true } || SaberLit ) return null;
			return Player.Inventory.Any( i => i.ItemId == id && i.Count > 0 ) ? id : null;
		}
	}

	/// <summary> The item in hand: the lit saber, the drawn blaster, or held gear. Null for empty hands. </summary>
	public string InHand => SaberLit ? JediCombat.SaberItem : Blaster is { Drawn: true, WeaponId: { } w } ? w : HeldGear;

	/// <summary> Animgraph holdtype for held gear (0 with nothing held); SwrpBlaster sets it when no blaster is up. </summary>
	public int GearHoldType => HeldGear is { } id ? ItemRegistry.Get( id )?.HoldType ?? 0 : 0;

	IReadOnlyList<ItemStack> _barFor;
	bool _barSaber;
	List<string> _bar = new();

	/// <summary> Everything that can be taken out, in quick-slot order (keys 1-5 take the first five). </summary>
	public List<string> QuickBar
	{
		get
		{
			var p = Player;
			if ( p is null ) return _bar;
			var inv = p.Inventory;
			var saber = Jedi is { HasSaber: true };
			if ( ReferenceEquals( inv, _barFor ) && saber == _barSaber ) return _bar;
			_barFor = inv;
			_barSaber = saber;

			var bar = new List<string>();
			if ( saber ) bar.Add( JediCombat.SaberItem );
			if ( Blaster is { } b ) bar.AddRange( b.Loadout );
			// a thermal detonator right after the guns, so it's always on a low number key while you carry one
			if ( inv.Any( i => i.ItemId == SwrpGrenade.ItemId && i.Count > 0 ) ) bar.Add( SwrpGrenade.ItemId );
			bar.AddRange( inv.Where( i => i.Count > 0 && i.ItemId != SwrpGrenade.ItemId && i.Definition is { Holdable: true } ).Select( i => i.ItemId ).Distinct() );
			_bar = bar;
			return _bar;
		}
	}

	/// <summary> The quick-slot key (1-5) an item is on, or 0. </summary>
	public int KeyFor( string itemId )
	{
		var i = QuickBar.IndexOf( itemId );
		return i >= 0 && i < QuickSlots ? i + 1 : 0;
	}

	// ----- owner -----

	string _pending;
	RealTimeSince _sincePending;
	string _last;
	RealTimeSince _sinceAction = 10;

	/// <summary> Owner: take this item out (a blaster, the saber, or gear); null empties the hands. </summary>
	public void Select( string id )
	{
		if ( IsProxy ) return;
		var blaster = Blaster;
		var jedi = Jedi;

		if ( id is null )
		{
			HeldItem = null;
			if ( blaster is { Drawn: true } ) blaster.Drawn = false;
			jedi?.OwnerSetSaber( false );
			_pending = null;
			return;
		}

		var def = ItemRegistry.Get( id );
		if ( id == JediCombat.SaberItem )
		{
			HeldItem = null;
			if ( blaster is { Drawn: true } ) blaster.Drawn = false;
			jedi?.OwnerSetSaber( true );
			_pending = null;
		}
		else if ( def?.Weapon is not null )
		{
			HeldItem = null;
			// a lit saber has the hands: the blaster comes out once it's off (SwrpBlaster keeps itself down until then)
			if ( SaberLit ) { jedi?.OwnerSetSaber( false ); _pending = id; _sincePending = 0; }
			else blaster?.Select( id );
		}
		else if ( def is { Holdable: true } )
		{
			if ( blaster is { Drawn: true } ) blaster.Drawn = false;
			jedi?.OwnerSetSaber( false );
			HeldItem = id;
			_pending = null;
		}
		else return;

		_last = id;
	}

	/// <summary> Owner: put it away if it's what's in hand, else take it out (the inventory's Equip / Hold, the quick-slot keys). </summary>
	public void Toggle( string id )
	{
		if ( id is not null && InHand == id ) Select( null );
		else Select( id );
	}

	/// <summary> Owner: G, drop what's in hand (E picks it up again). </summary>
	public void DropInHand()
	{
		var id = InHand;
		var p = Player;
		if ( id is null || p is null ) return;
		var slot = p.Inventory.ToList().FindIndex( i => i.ItemId == id && i.Count > 0 );
		if ( slot < 0 ) return;
		if ( id == JediCombat.SaberItem ) Jedi?.OwnerSetSaber( false );
		if ( id == HeldItem ) HeldItem = null;
		p.DropItem( slot );
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;
		var p = Player;
		if ( p is null ) return;

		// held gear goes away when a blaster or the saber comes up, when it's gone, or when you can't hold anything
		var down = Vitals is { IsDead: true } or { IsDowned: true };
		if ( HeldItem is not null && ( HeldGear is null || down || p.Jailed || p.Frozen ) ) HeldItem = null;
		if ( InHand is { } now ) _last = now;

		// a blaster chosen while the saber was lit comes out once the saber is off
		if ( _pending is not null )
		{
			if ( _sincePending > 1.5f ) _pending = null;
			else if ( !SaberLit ) { Blaster?.Select( _pending ); _pending = null; }
		}

		if ( SwrpUiState.AnyOpen || down || p.Jailed || p.Frozen || p.InPod || p.InVehicle ) return;

		var slotPressed = 0;
		for ( var i = 1; i <= QuickSlots; i++ )
			if ( Input.Pressed( $"Slot{i}" ) ) { slotPressed = i; break; }
		var holster = Input.Pressed( "Holster" );

		// a staff build tool is in your hands instead: a quick-slot key or H puts it away
		if ( SwrpBuild.Active != SwrpBuild.Tool.None && SwrpBuild.Allowed )
		{
			if ( slotPressed == 0 && !holster ) return;
			SwrpBuild.Active = SwrpBuild.Tool.None;
		}

		var bar = QuickBar;
		if ( slotPressed > 0 && slotPressed <= bar.Count ) Toggle( bar[slotPressed - 1] );

		if ( holster )
		{
			if ( InHand is not null ) Select( null );
			else if ( _last is not null && bar.Contains( _last ) ) Select( _last );
			else if ( bar.Count > 0 ) Select( bar[0] );
		}

		WheelInput( bar );

		if ( Input.Pressed( "Drop" ) ) DropInHand();

		// left click with a thermal detonator in hand throws it (the next one stays in hand while you have more)
		if ( HeldGear == SwrpGrenade.ItemId && Input.Pressed( "Attack1" ) )
		{
			p.ThrowGrenade();
			return;
		}

		// left click with gear in hand acts it out for everyone near
		if ( HeldGear is { } gear && Input.Pressed( "Attack1" ) && _sinceAction > 2f && ItemRegistry.Get( gear )?.HoldAction is { } action )
		{
			_sinceAction = 0;
			SwrpChat.Submit( $"/me {action}" );
		}
	}

	void WheelInput( List<string> bar )
	{
		if ( bar.Count == 0 || JediUi.WheelOpen || SwrpBuild.Grabbing || Jedi is { Thrown: true } ) return;
		var step = 0;
		if ( JediUi.Has( "SlotNext" ) && Input.Pressed( "SlotNext" ) ) step = 1;
		if ( JediUi.Has( "SlotPrev" ) && Input.Pressed( "SlotPrev" ) ) step = -1;
		// the wheel too, in both views (holding Walk in third person zooms the camera instead, SwrpFreeLook)
		var thirdZoom = Player.Controller is { ThirdPerson: true } && Input.Down( "Walk" );
		if ( step == 0 && !thirdZoom && Input.MouseWheel.y != 0 ) step = Input.MouseWheel.y > 0 ? -1 : 1;
		if ( step == 0 ) return;

		var current = _pending ?? InHand;
		var i = bar.IndexOf( current );
		var next = bar[i < 0 ? ( step > 0 ? 0 : bar.Count - 1 ) : ( i + step + bar.Count ) % bar.Count];
		if ( next != current ) Select( next );
	}

	// ----- every machine: the gear in hand -----

	GameObject _model;
	ModelRenderer _renderer;
	string _shown;
	SwrpOfficerTasks _reader;
	SkinnedModelRenderer _readerBody;

	void StopReading()
	{
		_reader?.Stop( _readerBody );
		_reader = null;
	}

	protected override void OnPreRender()
	{
		var gear = HeldGear;
		var p = Player;
		var show = gear is not null && p is not null && Vitals is not ( { IsDead: true } or { IsDowned: true } ) && !( IsProxy && p.Invisible );
		if ( !show )
		{
			if ( _model.IsValid() ) _model.Enabled = false;
			StopReading();
			return;
		}

		if ( !_model.IsValid() )
		{
			_model = new GameObject( false, "Held Item" );
			_model.NetworkMode = NetworkMode.Never;
			_model.Flags |= GameObjectFlags.NotSaved;
			_renderer = _model.AddComponent<ModelRenderer>();
			_shown = null;
		}
		if ( _shown != gear )
		{
			_shown = gear;
			_renderer.Model = ItemModels.For( gear );
		}
		_model.Enabled = true;

		var cam = Scene.Camera;
		var firstPerson = !IsProxy && cam.IsValid() && p.Controller.IsValid() && !p.Controller.ThirdPerson && SwrpUiState.SpectateSteamId == 0;
		if ( firstPerson )
		{
			StopReading();
			// low in front of you, tipped up so you can read it
			_model.WorldPosition = cam.WorldPosition + cam.WorldRotation * new Vector3( 17f, -6f, -10f );
			_model.WorldRotation = cam.WorldRotation * Rotation.From( -40f, 12f, 0f );
			_model.WorldScale = 1f;
			_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			return;
		}

		var body = p.Body;
		// a datapad: held up in the left hand and read, like the ship's officers (SwrpOfficerTasks), not hung off the right
		// hand's grip (Angelo, 2026-10-19: "fix how clones hold data pads")
		if ( gear == "datapad" && body.IsValid() )
		{
			_model.Enabled = false;
			_reader ??= new SwrpOfficerTasks( false, readOnly: true );
			_reader.Tick( body, Time.Delta, p.Controller.IsValid() && p.Controller.Velocity.WithZ( 0 ).Length > 20f );
			_readerBody = body;
			return;
		}
		StopReading();
		if ( body.IsValid() && body.GetAttachment( "hold_R" ) is { } hold )
		{
			_model.WorldPosition = hold.Position;
			_model.WorldRotation = hold.Rotation;
			_model.WorldScale = body.WorldScale.z;
		}
		else
		{
			_model.WorldPosition = WorldPosition + WorldRotation * new Vector3( 12f, -8f, 44f );
			_model.WorldRotation = WorldRotation;
		}
		_renderer.RenderType = ModelRenderer.ShadowRenderType.On;
	}

	protected override void OnDestroy()
	{
		if ( _model.IsValid() ) _model.Destroy();
	}
}
