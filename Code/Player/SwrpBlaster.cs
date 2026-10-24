using System;

namespace Swrp;

/// <summary>
/// The trooper's blasters: any blaster in the inventory can be taken in hand (the class's own guns first, then anything picked
/// up or spawned). Which one is in hand is chosen through <see cref="SwrpHands"/> (quick
/// slots 1-5, the wheel, H, the inventory's Equip). The host owns the equipped weapon and its magazine, validates and traces
/// every shot (with spread), applies damage through <see cref="SwrpCombat"/>, and saves each weapon's magazine on the
/// character. The owner handles input, recoil and aiming down the sights (right mouse); every machine draws the gun model and
/// the shot effects. Reloading spends one blaster cell from the inventory to refill the magazine.
/// </summary>
public sealed class SwrpBlaster : Component
{
	public const string CellItem = "blaster_cell";

	/// <summary> Owner controlled: weapon raised or holstered. </summary>
	[Sync] public bool Drawn { get; set; }

	/// <summary> Owner controlled: stun rounds (X) on weapons that support them. The host reads it on every shot. </summary>
	[Sync] public bool StunMode { get; set; }

	/// <summary> Owner synced: aiming down the sights (TFA's iron-sight hold type for everyone watching). </summary>
	[Sync] public bool Sighted { get; set; }

	[Sync( SyncFlags.FromHost )] public string WeaponId { get; set; }
	[Sync( SyncFlags.FromHost )] public int Clip { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Reloading { get; set; }

	public ItemDefinition Weapon => ItemRegistry.Get( WeaponId );
	public WeaponStats Stats => Weapon?.Weapon;
	public bool Stunning => StunMode && Stats?.CanStun == true;

	/// <summary> Blaster cells left in the inventory. Each refills a whole magazine. </summary>
	public int ReserveCells => Player?.Inventory.Where( x => x.ItemId == CellItem ).Sum( x => x.Count ) ?? 0;
	public int ReserveRounds => ReserveCells * ( Stats?.MagazineSize ?? 0 );

	/// <summary> Owner only: 0 at the hip, 1 fully aimed down the sights. </summary>
	public float AimAmount => _ads;

	// Last hit confirmation for this machine's own shots (read by the HUD).
	public static RealTimeSince SinceHitMarker { get; private set; } = 10;
	public static bool HitMarkerHead { get; private set; }
	public static bool HitMarkerKill { get; private set; }
	public static bool HitMarkerStun { get; private set; }
	public static bool HitMarkerFriendly { get; private set; }

	SwrpPlayer _player;
	SwrpPlayer Player => _player ??= GetComponent<SwrpPlayer>();
	SwrpVitals Vitals => GetComponent<SwrpVitals>();

	RealTimeSince _sinceShot = 10;

	/// <summary> Seconds since this blaster last fired (the HUD shows the ammo for a while after). </summary>
	public float SinceFired => _sinceShot;
	RealTimeSince _sinceHostShot = 10;
	RealTimeUntil _reloadDone;
	bool _wasReloading;
	float _ads;

	/// <summary> 0 hip, 1 fully aimed down the sights (the camera's aim focus reads it). </summary>
	public float AimBlend => _ads;

	/// <summary> Long scopes (an aimed field of view of 30 or less: the DC-15X, the E-5s) look through a scope view instead of over the gun. </summary>
	public bool Scoped => Stats is { AdsFov: <= 30f };

	/// <summary> Owner, first person: fully aimed through a long scope (the HUD draws the scope; the gun is hidden). </summary>
	public bool ScopeView { get; private set; }

	// the highest point of the gun model above the grip (from its bounds), so aiming holds the whole gun under the sight line
	float _sightTop = 3f;

	GameObject _model;
	ModelRenderer _modelRenderer;
	float _lowered;
	string _modelPath;

	/// <summary> Every blaster this character carries: the class's own guns first (in the class's order), then the rest as they sit in the inventory. </summary>
	public IEnumerable<string> AllowedWeapons
	{
		get
		{
			var p = Player;
			if ( p is null ) return Enumerable.Empty<string>();
			var carried = p.Inventory.Where( i => i.Count > 0 && i.Definition?.Weapon is not null ).Select( i => i.ItemId ).Distinct().ToList();
			var own = p.Class.Weapons;
			return own.Where( carried.Contains ).Concat( carried.Where( id => !own.Contains( id ) ) );
		}
	}

	/// <summary> The carried blasters in key order: primaries (key 1), then sidearms (key 2). The HUD's weapon bar shows these. </summary>
	public List<string> Loadout => AllowedWeapons.OrderBy( id => ItemRegistry.Get( id )?.Weapon?.Slot ?? 9 ).ToList();

	public string WeaponForSlot( int slot ) => AllowedWeapons.FirstOrDefault( id => ItemRegistry.Get( id )?.Weapon?.Slot == slot );

	/// <summary> Owner: holster the gun in hand, or take out another (the inventory's Equip / Holster). </summary>
	public void Toggle( string id )
	{
		if ( Drawn && id == WeaponId ) Drawn = false;
		else Select( id );
	}

	string _offered;
	RealTimeSince _sinceOffered;

	/// <summary> Host to the owner: you just picked up this blaster; with empty hands, take it out (once the inventory has synced). </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void OfferPickedUp( string id )
	{
		_offered = id;
		_sinceOffered = 0;
	}

	/// <summary>
	/// Host, before the character spawns: give the class's weapons to a character that lacks them, once per class (a gun you
	/// dropped or handed over doesn't come back on the next spawn; a class change issues the new class's guns). True if it changed.
	/// </summary>
	public static bool IssueClassWeapons( CharacterRecord record )
	{
		var cls = ClassRegistry.Get( record.ClassId ) ?? ClassRegistry.Default;
		if ( record.IssuedClass == cls.Id ) return false;
		record.IssuedClass = cls.Id;
		var changed = true;
		foreach ( var id in cls.Weapons )
		{
			if ( record.Inventory.Any( i => i.ItemId == id ) ) continue;
			if ( record.AddItem( id, 1 ) == 0 ) changed = true;
		}

		return changed;
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost ) HostUpdate();
		UpdateAnimation();
		if ( !IsProxy ) OwnerInput();
	}

	protected override void OnPreRender()
	{
		UpdateWeaponModel();
		if ( !IsProxy ) UpdateAimCamera();
	}

	protected override void OnDestroy()
	{
		if ( _model.IsValid() ) _model.Destroy();
		if ( Networking.IsHost ) SaveClip( true );
	}

	// ----- Host -----

	void HostUpdate()
	{
		// Keep the equipped weapon one the class allows and the character carries.
		var allowed = AllowedWeapons.ToList();
		if ( WeaponId is null || !allowed.Contains( WeaponId ) && !SwrpAimSweep.Running )      // (dev: an aim sweep holds any gun)
			EquipHost( allowed.FirstOrDefault() );

		if ( Reloading && _reloadDone <= 0 ) FinishReload();
	}

	CharacterRecord Record => Player is { } p ? CharacterStore.Find( p.OwnerSteamId, p.CharacterId ) : null;

	void EquipHost( string id )
	{
		if ( id == WeaponId ) return;
		SaveClip( true );

		WeaponId = id;
		Reloading = false;
		var stats = Stats;
		if ( stats is null ) { Clip = 0; return; }

		var record = Record;
		Clip = record is not null && record.WeaponClips.TryGetValue( id, out var saved ) ? Math.Clamp( saved, 0, stats.MagazineSize ) : stats.MagazineSize;
	}

	/// <summary> Host: write the current magazine onto the character (and to disk when asked). </summary>
	void SaveClip( bool toDisk )
	{
		if ( WeaponId is null || Player is null ) return;
		var record = Record;
		if ( record is null ) return;
		record.WeaponClips[WeaponId] = Clip;
		if ( toDisk ) CharacterStore.Save( Player.OwnerSteamId );
	}

	/// <summary> Owner: take this weapon out (weapon cycling and the slot keys after a lightsaber, Code/Jedi). </summary>
	public void Select( string id )
	{
		if ( id is null || !AllowedWeapons.Contains( id ) ) return;
		if ( id != WeaponId )
		{
			if ( Reloading ) CancelReload();
			RequestEquip( id );
		}
		Drawn = true;
	}

	[Rpc.Host]
	void RequestEquip( string id )
	{
		if ( !IsCallerOwner() || id is null || !AllowedWeapons.Contains( id ) ) return;
		EquipHost( id );
	}

	/// <param name="seed">The spread seed: the owner already drew these bolts on its own screen, so the host scatters them the same way.</param>
	/// <param name="claimId">What the owner's own trace hit first (a trooper or NPC), or empty: the lag-compensation claim (SwrpLagComp).</param>
	/// <param name="claimRoot">Where that target stood on the owner's screen.</param>
	[Rpc.Host]
	void Fire( Vector3 origin, Vector3 direction, bool aiming, int seed, Guid claimId, Vector3 claimRoot )
	{
		var stats = Stats;
		if ( !IsCallerOwner() || stats is null || Reloading || Clip <= 0 || Vitals is { } v && ( v.IsDead || v.IsDowned ) ) return;
		if ( Player is { } pl && ( pl.Jailed || pl.Frozen ) ) return;
		if ( _sinceHostShot < stats.FireDelay * 0.75f ) return;

		// The shot has to start near this player's eyes; anything else is a spoofed request.
		var eye = Player.Controller.EyePosition;
		if ( origin.Distance( eye ) > 96f ) origin = eye;

		// Spread is decided here, not by the client.
		var cone = Cone( stats, aiming );
		var aim = direction.Normal;

		_sinceHostShot = 0;
		Clip--;
		SaveClip( false );

		// the owner's claim: kept only when that target really stood near there within the rewind window
		var claim = claimId == Guid.Empty || Rpc.Caller is { IsHost: true } ? null : Scene.Directory.FindByGuid( claimId );
		if ( claim is not null && !SwrpLagComp.Plausible( claim, claimRoot ) )
		{
			if ( SwrpLagComp.Debug ) Log.Info( $"[SWRP] lagcomp: rejected a claim on {claim.Name} ({claim.WorldPosition.Distance( claimRoot ):0} units from it)" );
			claim = null;
		}

		var stun = Stunning;
		var rng = new System.Random( seed );
		// a scatter blaster sends several bolts, each its own trace
		for ( var pellet = 0; pellet < Math.Max( 1, stats.Pellets ); pellet++ )
			FireBolt( origin, Spread( aim, cone, rng ), stats, stun, claim, claimRoot );
	}

	/// <summary> The spread cone of a shot (degrees): aimed or from the hip, wider on the move. Same on the owner and the host. </summary>
	public float Cone( WeaponStats stats, bool aiming )
	{
		var moving = Player.Controller.IsValid() && Player.Controller.Velocity.WithZ( 0 ).Length > 60f;
		return ( aiming ? stats.AdsSpread : stats.Spread ) + ( moving ? stats.MoveSpread : 0f );
	}

	/// <summary> Host: one bolt along a direction: what it hits takes damage (less the further it flew). </summary>
	void FireBolt( Vector3 origin, Vector3 direction, WeaponStats stats, bool stun, GameObject claim = null, Vector3 claimRoot = default )
	{
		var tr = Scene.Trace.Ray( origin, origin + direction * stats.Range )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger" )
			.UseHitboxes()
			.Run();

		// lag compensation: the owner saw this bolt hit a target that has moved on since; if the shifted ray still meets it
		// before anything the real ray hit, that's the hit (SwrpLagComp)
		if ( claim is not null && !( tr.Hit && SwrpLagComp.IsPartOf( tr.GameObject, claim ) ) )
		{
			var blocked = tr.Hit ? tr.Distance : stats.Range;
			if ( SwrpLagComp.Trace( Scene, GameObject, claim, claimRoot, origin, direction, stats.Range, blocked + 1f ) is { } comp )
				tr = comp;
		}

		// a Jedi guarding with a lit saber turns the bolt on the blade (Code/Jedi)
		if ( Swrp.Jedi.JediCombat.TryDeflect( tr, GameObject, origin, stun ? 0f : stats.DamageAt( tr.Distance ), Weapon?.Name, stats.BoltColor ) )
		{
			ShowShot( tr.EndPosition, tr.Normal, true, false, stun );
			return;
		}

		SwrpNpc.HeardShot( Scene, origin, tr.EndPosition, Player?.Allegiance );      // (NPC clones stop idling)
		var hitVitals = tr.Hit ? tr.GameObject?.Components.GetInAncestorsOrSelf<SwrpVitals>() : null;
		if ( hitVitals is not null ) SwrpCombat.ApplyShot( this, tr, stats, Weapon?.Name, stun );
		else if ( tr.Hit && tr.GameObject?.Components.GetInAncestorsOrSelf<SwrpNpc>() is { } npc ) SwrpCombat.ApplyShotNpc( this, tr, stats, Weapon?.Name, stun, npc );
		else if ( tr.Hit && tr.GameObject?.Components.GetInAncestorsOrSelf<SwrpTrainingTarget>() is { } target ) target.HostHit( Player );
		else if ( tr.Hit && !stun && tr.GameObject?.Components.GetInAncestorsOrSelf<Swrp.Flight.SwrpFlightShip>() is { } craft ) craft.HostDamage( stats.DamageAt( tr.Distance ), tr.HitPosition, Player, Weapon?.Name );

		ShowShot( tr.EndPosition, tr.Hit ? tr.Normal : -direction, tr.Hit, hitVitals is null && tr.GameObject?.Components.GetInAncestorsOrSelf<SwrpNpc>() is null, stun );
	}

	static Vector3 Spread( Vector3 dir, float degrees, System.Random rng )
	{
		if ( degrees <= 0.01f ) return dir;
		var rot = Rotation.LookAt( dir );
		var r = MathF.Sqrt( (float)rng.NextDouble() ) * degrees;
		var a = (float)rng.NextDouble() * MathF.PI * 2f;
		return ( rot * Rotation.From( MathF.Sin( a ) * r, MathF.Cos( a ) * r, 0 ) ).Forward;
	}

	// ----- owner: the shot on your own screen, at once -----

	RealTimeSince _sincePredicted = 10;

	/// <summary>
	/// Owner: fire. The muzzle flash, sound, bolt and impact play on this screen straight away (they used to wait a whole round
	/// trip for the host's broadcast), the bolts scatter with the same seed the host will use, and whatever this screen's own
	/// trace hit goes along as the lag-compensation claim. The host still decides every hit.
	/// </summary>
	void ShootLocal( Vector3 origin, Vector3 direction, bool aiming )
	{
		var stats = Stats;
		if ( stats is null ) return;
		var seed = Game.Random.Next();
		var cone = Cone( stats, aiming );
		var rng = new System.Random( seed );
		var stun = Stunning;
		var claimId = Guid.Empty;
		var claimRoot = Vector3.Zero;

		for ( var pellet = 0; pellet < Math.Max( 1, stats.Pellets ); pellet++ )
		{
			var dir = Spread( direction.Normal, cone, rng );
			var tr = Scene.Trace.Ray( origin, origin + dir * stats.Range )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "trigger" )
				.UseHitboxes()
				.Run();
			var target = SwrpLagComp.TargetOf( tr );
			if ( target is not null && claimId == Guid.Empty )
			{
				claimId = target.Id;
				claimRoot = target.WorldPosition;
			}
			PlayShot( tr.Hit ? tr.EndPosition : origin + dir * stats.Range, tr.Hit ? tr.Normal : -dir, tr.Hit, tr.Hit && target is null, stun, pellet == 0 );
		}

		_sincePredicted = 0;
		Fire( origin, direction, aiming, seed, claimId, claimRoot );
	}

	public void RequestReload()
	{
		if ( Reloading || Stats is null || Clip >= Stats.MagazineSize ) return;
		Reload();
	}

	[Rpc.Host]
	void Reload()
	{
		if ( !IsCallerOwner() || Reloading || Stats is null || Clip >= Stats.MagazineSize ) return;

		if ( ReserveCells <= 0 )
		{
			SwrpNet.NotifyConnection( Rpc.Caller, "No blaster cells left.", true );
			return;
		}

		Reloading = true;
		_reloadDone = Stats.ReloadTime;
	}

	[Rpc.Host]
	void CancelReload()
	{
		if ( !IsCallerOwner() ) return;
		Reloading = false;
	}

	void FinishReload()
	{
		Reloading = false;

		var record = Record;
		var slot = record?.Inventory.FindIndex( x => x.ItemId == CellItem ) ?? -1;
		if ( slot < 0 ) return;

		record.TakeFromSlot( slot );
		Clip = Stats.MagazineSize;
		record.WeaponClips[WeaponId] = Clip;
		CharacterStore.Save( Player.OwnerSteamId );
		Player.InventoryJson = Json.Serialize( record.Inventory );
	}

	/// <summary> Host to the shooter: your bolt landed. </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void ConfirmHit( bool head, bool kill, bool stun, bool friendly )
	{
		SinceHitMarker = 0;
		HitMarkerHead = head;
		HitMarkerKill = kill;
		HitMarkerStun = stun;
		HitMarkerFriendly = friendly;
		if ( !friendly ) SwrpAudio.Ui( kill ? SwrpAudio.HitMarkerKill : SwrpAudio.HitMarker );
	}

	// ----- Every machine -----

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowShot( Vector3 end, Vector3 normal, bool hit, bool hitWorld, bool stun )
	{
		// the shooter drew this shot the moment they fired (ShootLocal)
		if ( !IsProxy && _sincePredicted < 1f ) return;
		PlayShot( end, normal, hit, hitWorld, stun, true );
	}

	/// <summary> The shot's look and sound: muzzle flash and report (once per trigger pull), the bolt, the impact. </summary>
	void PlayShot( Vector3 end, Vector3 normal, bool hit, bool hitWorld, bool stun, bool report )
	{
		var stats = Stats;
		var body = Player?.Body;
		if ( report && body.IsValid() ) { body.Set( "b_attack", true ); SwrpEmotes.Kick( body ); }
		// a GMod set: GMod's own fire gesture (range_ar2 / range_pistol)
		if ( report && body.IsValid() && Player.UsesGmodSet && Player.GmodSet is { } set && set is not ( "normal" or "passive" ) ) SwrpEmotes.GmodGesture( body, "range_" + set );

		var start = MuzzlePosition();
		var color = stun ? new Color( 0.85f, 0.95f, 1f ) : Player?.EventDef?.BoltColor ?? stats?.BoltColor ?? Color.Cyan;

		if ( report )
		{
			Sound.Play( stun ? SwrpAudio.BlasterStun : stats?.FireSound ?? SwrpAudio.BlasterStun, start );
			SwrpFx.MuzzleFlash( Scene, start, color );
		}
		BlasterBolt.Spawn( Scene, start, end, color, stun );

		if ( hit )
		{
			SwrpFx.Impact( Scene, end, normal, color, hitWorld && !stun );
			Sound.Play( SwrpAudio.BlasterImpact, end );
		}
	}

	/// <summary> Dev: the muzzle and the way the barrel points right now (SwrpAimSweep). </summary>
	public (Vector3 Muzzle, Vector3 Barrel) MuzzleRay => (MuzzlePosition(), _model.IsValid() ? _model.WorldRotation.Forward : Vector3.Forward);

	Vector3 MuzzlePosition()
	{
		var stats = Stats;
		if ( _model.IsValid() && _model.Enabled && stats is not null )
			return _model.WorldPosition + _model.WorldRotation * stats.MuzzleLocal;

		var body = Player?.Body;
		return body.IsValid() && body.GetAttachment( "hold_R" ) is { } hold
			? hold.Position + hold.Rotation.Forward * 18f
			: Player.Controller.EyePosition + Vector3.Down * 12f;
	}

	bool _soundInit, _lastDrawn;
	RealTimeSince _reloadClock;
	int _reloadStep;

	void UpdateAnimation()
	{
		var body = Player?.Body;
		if ( !body.IsValid() ) return;

		// Drawing and holstering is audible to everyone near the trooper (the first frame only records the state).
		if ( _soundInit && _lastDrawn != Drawn )
			SwrpAudio.At( Drawn ? SwrpAudio.WeaponDraw : SwrpAudio.WeaponHolster, WorldPosition + Vector3.Up * 48f );
		_lastDrawn = Drawn;
		// the weapon light's switch, heard by everyone near (on and off sound different)
		if ( _soundInit && _lastFlash != FlashlightOn && HasFlashlight )
			SwrpAudio.At( FlashlightOn ? SwrpAudio.FlashlightOn : SwrpAudio.FlashlightOff, WorldPosition + Vector3.Up * 50f );
		_lastFlash = FlashlightOn;
		_soundInit = true;

		// a pistol on safety hangs at the side (holdtype 0, still in hand); rifles keep their hold and are lowered by SwrpEmotes.
		// Relaxed walks on the unarmed legs too: the rifle hold's walk is a bent-knee half jog (Angelo, 2026-10-19: "a relaxed
		// patrol walk"); the low carry puts the hands on the rifle over it
		body.Set( "holdtype", Drawn && Stats is not null ? ( Player.WeaponSafety && ClassDefinition.Custom && ( Stats.HoldType == 1 || Player.WeaponRelaxed ) ? 0 : Stats.HoldType ) : GetComponent<SwrpHands>()?.GearHoldType ?? 0 );
		// and the graph's walk, not its jog, at walking pace (move_style 1 = walk; 0 = it picks)
		// stock (TFA in Garry's Mod): on safety (Shift+E+R) or sprinting the gun drops to the lowered hold, like TFA's passive
		body.Set( "b_weapon_lower", !ClassDefinition.Custom && !Player.UsesGmodSet && Drawn && ( Player.WeaponSafety || Player.Sprinting ) && !Reloading );
		if ( Player.UsesGmodSet ) body.Set( "holdtype", 0 );
		body.Set( "move_style", ClassDefinition.Custom && Drawn && Player.WeaponRelaxed && Player.WeaponSafety && !Player.Sprinting ? 1 : 0 );

		if ( Reloading && !_wasReloading )
		{
			body.Set( "b_reload", true );
			if ( Player.UsesGmodSet ) SwrpEmotes.GmodGesture( body, "reload_" + SwrpEmotes.TfaBaseHold( WeaponId, Stats?.HoldType ?? 2 ) );      // GMod's own reload, TFA's ReloadHoldTypes
			_reloadClock = 0;
			_reloadStep = 0;
		}
		_wasReloading = Reloading;

		// the reload, heard by everyone near: the spent cell out, a fresh one in, the capacitor charging to ready
		if ( Reloading && Stats is { } st )
		{
			var at = WorldPosition + Vector3.Up * 48f;
			var t = st.ReloadTime;
			if ( _reloadStep == 0 ) { _reloadStep = 1; Sound.Play( SwrpAudio.ReloadEject, at ); }
			if ( _reloadStep == 1 && _reloadClock > t * 0.45f ) { _reloadStep = 2; Sound.Play( SwrpAudio.ReloadInsert, at ); }
			if ( _reloadStep == 2 && _reloadClock > MathF.Max( t * 0.6f, t - 0.6f ) ) { _reloadStep = 3; Sound.Play( SwrpAudio.ReloadCharge, at ); }
		}
	}

	void UpdateWeaponModel()
	{
		var stats = Stats;
		var vitals = Vitals;
		var show = Drawn && stats?.ModelPath is not null && vitals is not { IsDead: true } && vitals is not { IsDowned: true } && !( IsProxy && Player is { Invisible: true } )
			&& Player is not { InVehicle: true } && Player is not { InPod: true };      // (in a craft seat it floated by the screen, Angelo 2026-10-21)

		ScopeView = false;
		if ( !show )
		{
			if ( _model.IsValid() ) _model.Enabled = false;
			return;
		}

		if ( !_model.IsValid() )
		{
			_model = new GameObject( false, "Blaster Model" );
			_model.NetworkMode = NetworkMode.Never;
			_model.Flags |= GameObjectFlags.NotSaved;
			// the renderer sits on a child, so a model that isn't built grip-at-origin can be moved into the hand
			var mesh = new GameObject( _model, true, "Mesh" );
			mesh.NetworkMode = NetworkMode.Never;
			mesh.Flags |= GameObjectFlags.NotSaved;
			_modelRenderer = mesh.AddComponent<ModelRenderer>();
			_modelPath = null;
		}

		// the weapon's own model, placed in the grip frame
		var key = stats.ModelPath;
		if ( _modelPath != key )
		{
			_modelPath = key;
			_modelRenderer.Model = Model.Load( stats.ModelPath );
			stats.PlaceMesh( _modelRenderer.GameObject );

			// the top of the gun in the grip frame: the bounds' corners through the mesh's placement
			var mesh = _modelRenderer.GameObject;
			var b = _modelRenderer.Model?.Bounds ?? new BBox( Vector3.Zero, Vector3.One * 3f );
			_sightTop = 0f;
			for ( var c = 0; c < 8; c++ )
			{
				var corner = new Vector3( ( c & 1 ) == 0 ? b.Mins.x : b.Maxs.x, ( c & 2 ) == 0 ? b.Mins.y : b.Maxs.y, ( c & 4 ) == 0 ? b.Mins.z : b.Maxs.z );
				_sightTop = MathF.Max( _sightTop, ( mesh.LocalPosition + mesh.LocalRotation * corner * mesh.LocalScale.x ).z );
			}
		}

		_model.Enabled = true;
		var cam = Scene.Camera;
		var firstPerson = !IsProxy && cam.IsValid() && Player.Controller.IsValid() && !Player.Controller.ThirdPerson && SwrpUiState.SpectateSteamId == 0;

		if ( firstPerson )
		{
			// Held in front of the camera; aiming brings it to the centre so the sights line up. Sprinting with a rifle lowers
			// it and turns it across the body, as the trooper carries it in third person.
			var lowerWanted = ( Player.Sprinting && stats.HoldType == 2 || Player.WeaponSafety ) && !Reloading && _sinceShot > 0.3f && _ads < 0.1f;
			_lowered = _lowered.Approach( lowerWanted ? 1f : 0f, Time.Delta * ( lowerWanted ? 5f : 9f ) );
			var low = _lowered * _lowered * ( 3f - 2f * _lowered );
			var hip = new Vector3( 20f, -7.5f, -9.5f );
			var aim = AimOffset( stats );
			var lowered = new Vector3( 17f, -5f, -15f );
			var kick = MathF.Max( 0f, 1f - _sinceShot / 0.08f ) * 1.4f;
			var offset = Vector3.Lerp( Vector3.Lerp( hip, aim, _ads ), lowered, low ) - new Vector3( kick, 0, 0 );
			_model.WorldPosition = cam.WorldPosition + cam.WorldRotation * offset;
			_model.WorldRotation = cam.WorldRotation * Rotation.From( 22f * low, 38f * low, -12f * low );
			_model.WorldScale = 1f;
			_modelRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			UpdateFlashlight( true );

			// a long scope: once aimed you look through it (the HUD draws the scope), so the gun gets out of the way
			ScopeView = Scoped && _ads > 0.85f && !Reloading;
			if ( ScopeView ) _model.Enabled = false;
			return;
		}

		var body = Player?.Body;
		if ( body.IsValid() && body.GetAttachment( "hold_R" ) is { } raw )
		{
			var hold = raw;
			_model.WorldPosition = hold.Position;
			_model.WorldRotation = hold.Rotation;
			_model.WorldScale = body.WorldScale.z;     // the same size against the trooper as on NPCs and the test stand
		}
		else
		{
			_model.WorldPosition = WorldPosition + WorldRotation * new Vector3( 12f, -8f, 44f );
			_model.WorldRotation = WorldRotation;
		}

		_modelRenderer.RenderType = ModelRenderer.ShadowRenderType.On;
		UpdateFlashlight( false );
	}

	// ----- weapon light -----

	/// <summary> Owner synced: the weapon light is on (F). Everyone draws it from this on their own copy of the gun. </summary>
	[Sync] public bool FlashlightOn { get; set; }
	bool _lastFlash;

	/// <summary> Guns with an under-barrel light (Angelo, 2026-10-13: the DC-15A). </summary>
	public static readonly HashSet<string> FlashlightWeapons = new() { "dc15a" };
	public bool HasFlashlight => Drawn && WeaponId is not null && FlashlightWeapons.Contains( WeaponId );

	GameObject _light;
	SpotLight _spot;
	PointLight _glow;
	string _lightFor;

	/// <summary>
	/// Every machine: a spot light at the muzzle end of the gun, along the barrel, that lights dark rooms; a small warm glow
	/// on the lens so the light reads from the front. Lives under the gun model, so it goes where the gun goes (in the hand
	/// for everyone else, in front of the camera in your own first person).
	/// </summary>
	void UpdateFlashlight( bool firstPerson )
	{
		var on = FlashlightOn && HasFlashlight && _model.IsValid();
		if ( !on )
		{
			if ( _light.IsValid() ) _light.Enabled = false;
			return;
		}
		if ( !_light.IsValid() )
		{
			_light = new GameObject( _model, true, "Weapon Light" );
			_light.NetworkMode = NetworkMode.Never;
			_light.Flags |= GameObjectFlags.NotSaved;
			_spot = _light.Components.Create<SpotLight>();
			_spot.LightColor = new Color( 1f, 0.95f, 0.85f ) * 6f;
			_spot.Radius = 1400f;
			_spot.ConeInner = 14f;
			_spot.ConeOuter = 30f;
			_spot.Shadows = true;
			var lens = new GameObject( _light, true, "Lens" );
			lens.NetworkMode = NetworkMode.Never;
			lens.Flags |= GameObjectFlags.NotSaved;
			lens.LocalPosition = Vector3.Forward * 1.5f;
			_glow = lens.Components.Create<PointLight>();
			_glow.LightColor = new Color( 1f, 0.95f, 0.85f ) * 0.6f;
			_glow.Radius = 14f;
			_glow.Shadows = false;
		}
		// the front of the gun: the mesh's furthest point along the barrel, a little under the bore
		if ( _lightFor != _modelPath && _modelRenderer.IsValid() )
		{
			_lightFor = _modelPath;
			var mesh = _modelRenderer.GameObject;
			var b = _modelRenderer.Model?.Bounds ?? new BBox( Vector3.Zero, Vector3.One * 20f );
			var front = 0f;
			for ( var c = 0; c < 8; c++ )
			{
				var corner = new Vector3( ( c & 1 ) == 0 ? b.Mins.x : b.Maxs.x, ( c & 2 ) == 0 ? b.Mins.y : b.Maxs.y, ( c & 4 ) == 0 ? b.Mins.z : b.Maxs.z );
				front = MathF.Max( front, ( mesh.LocalPosition + mesh.LocalRotation * corner * mesh.LocalScale.x ).x );
			}
			_light.LocalPosition = new Vector3( front - 4f, 0f, -0.5f );
		}
		_light.LocalRotation = Rotation.Identity;
		_light.Enabled = true;
		// your own lens glow sits right in front of your eyes: keep it off in first person
		if ( _glow.IsValid() ) _glow.Enabled = !firstPerson;
	}

	/// <summary>
	/// First person, fully aimed: the gun centred under the crosshair with its highest point (scope, sights, carry handle) a
	/// little below the middle of the screen at the weapon's aimed field of view, so you aim over it and nothing of it covers
	/// what you're aiming at.
	/// </summary>
	Vector3 AimOffset( WeaponStats stats )
	{
		const float forward = 18f;
		const float below = 0.42f;      // how far down the top of the gun sits: a share of the way from the centre to the bottom edge
		var aspect = Screen.Height > 0 ? Screen.Width / Screen.Height : 16f / 9f;
		var tanHalfV = MathF.Tan( stats.AdsFov.DegreeToRadian() * 0.5f ) / MathF.Max( 1f, aspect );      // the field of view is horizontal
		return new Vector3( forward, 0f, -( _sightTop + forward * tanHalfV * below ) );
	}

	// ----- Owner -----

	void OwnerInput()
	{
		// a blaster just picked up goes into empty hands (the inventory sync can arrive a moment after the pickup)
		if ( _offered is not null )
		{
			if ( _sinceOffered > 2f || Drawn || Swrp.Jedi.JediCombat.HoldsSaber( Player ) ) _offered = null;
			else if ( AllowedWeapons.Contains( _offered ) && !SwrpUiState.AnyOpen ) { Select( _offered ); _offered = null; }
		}

		// a staff build tool is in your hands instead (SwrpHands puts it away on a quick-slot key)
		if ( SwrpBuild.Active != SwrpBuild.Tool.None && SwrpBuild.Allowed ) return;

		var stats = Stats;
		var aimWanted = false;

		try
		{
			// a Jedi's lit lightsaber has their hands (Code/Jedi): the blaster stays holstered
			if ( Swrp.Jedi.JediCombat.HoldsSaber( Player ) )
			{
				Drawn = false;
				return;
			}

			if ( SwrpUiState.AnyOpen || Vitals is { IsDead: true } || Vitals is { IsDowned: true } || stats is null || Player is { InPod: true } or { InVehicle: true } )
			{
				if ( stats is null || Vitals is { IsDowned: true } ) Drawn = false;
				return;
			}

			// Jailed or frozen troopers keep their weapon down.
			if ( Player is { } p && ( p.Jailed || p.Frozen ) )
			{
				Drawn = false;
				return;
			}

			// the quick-slot keys, the wheel, H and G (what's in your hands) are SwrpHands'

			if ( !Drawn )
			{
				if ( Reloading ) CancelReload();
				if ( Player.WeaponSafety ) Player.WeaponSafety = false;      // drawn again: up and ready
				if ( Player.WeaponRelaxed ) Player.WeaponRelaxed = false;
				return;
			}

			// F: the weapon light, on or off (works on safety too)
			if ( HasFlashlight && Input.Pressed( "Flashlight" ) )
			{
				FlashlightOn = !FlashlightOn;      // the click plays on every machine when the synced state changes (UpdateAnimation)
			}

			// Shift+E+R (in any order, the last of the three pressed) steps through: off safety -> on safety -> relaxed -> off safety.
			// Relaxed (rifles) is on safety with the rifle carried low and the unarmed walk, for walking the ship (Angelo, 2026-10-18)
			var run = Input.Down( "Run" );
			if ( run && ( Input.Down( "Use" ) && Input.Pressed( "Reload" ) || Input.Down( "Reload" ) && Input.Pressed( "Use" ) ) )
			{
				var rifle = stats.HoldType == 2;
				if ( !Player.WeaponSafety ) { Player.WeaponSafety = true; Player.WeaponRelaxed = false; }
				else if ( rifle && !Player.WeaponRelaxed && ClassDefinition.Custom ) Player.WeaponRelaxed = true;
				else { Player.WeaponSafety = false; Player.WeaponRelaxed = false; }
				if ( Player.WeaponSafety && Reloading ) CancelReload();
				SwrpAudio.At( Player.WeaponSafety ? SwrpAudio.WeaponHolster : SwrpAudio.WeaponDraw, WorldPosition + Vector3.Up * 48f );
				if ( !ClassDefinition.Custom ) SwrpNet.ShowLocal( Player.WeaponSafety ? "Weapon lowered (Shift+E+R to raise it)" : "Weapon raised", false );
				else SwrpNet.ShowLocal( !Player.WeaponSafety ? "Weapon off safety: shouldered on target"
					: Player.WeaponRelaxed ? "Weapon relaxed: carried low (Shift+E+R to shoulder it)"
					: rifle ? "Weapon on safety: held ready across the chest (Shift+E+R to relax)" : "Weapon on safety (Shift+E+R to raise it)", false );
				return;
			}
			if ( Player.WeaponSafety )
			{
				if ( Input.Pressed( "Attack1" ) ) SwrpNet.ShowLocal( "Weapon on safety: Shift+E+R to raise it", true );
				return;
			}

			if ( Input.Pressed( "StunMode" ) )
			{
				if ( stats.CanStun )
				{
					StunMode = !StunMode;
					SwrpNet.ShowLocal( StunMode ? "Stun setting: ON" : "Stun setting: OFF", false );
				}
				else SwrpNet.ShowLocal( $"The {Weapon.Name} has no stun setting.", true );
			}

			// Sprinting cancels a reload in progress.
			if ( Reloading && Input.Down( "Run" ) && Player.Controller.Velocity.WithZ( 0 ).Length > Player.Controller.WalkSpeed * 1.1f )
				CancelReload();

			if ( Input.Pressed( "Reload" ) ) RequestReload();

			aimWanted = Input.Down( "Attack2" ) || SwrpAimSweep.Sights;

			var trigger = stats.Automatic ? Input.Down( "Attack1" ) : Input.Pressed( "Attack1" );
			if ( trigger && _sinceShot >= stats.FireDelay && !Reloading )
			{
				_sinceShot = 0;
				if ( Clip > 0 )
				{
					var eye = Player.Controller.EyeTransform;
					ShootLocal( eye.Position, eye.Rotation.Forward, _ads > 0.5f );

					// Recoil: kick the view up, a little sideways; aiming steadies it.
					var k = stats.Recoil * ( _ads > 0.5f ? 0.55f : 1f );
					Player.Controller.EyeAngles += new Angles( -k, Game.Random.Float( -k, k ) * 0.35f, 0 );
				}
				else if ( Input.Pressed( "Attack1" ) )
				{
					Sound.Play( SwrpAudio.BlasterEmpty, WorldPosition + Vector3.Up * 48f );
					RequestReload();
				}
			}
		}
		finally
		{
			_ads = _ads.Approach( aimWanted && Drawn ? 1f : 0f, Time.Delta * 7f );
			if ( Sighted != _ads > 0.5f ) Sighted = _ads > 0.5f;
		}
	}

	void UpdateAimCamera()
	{
		var cam = Scene.Camera;
		if ( !cam.IsValid() || SwrpUiState.SpectateSteamId != 0 ) return;

		var baseFov = Preferences.FieldOfView;
		var target = Stats?.AdsFov ?? baseFov;
		cam.FieldOfView = MathX.Lerp( baseFov, target, _ads );
	}

	/// <summary> Owner: one shot straight down the sights, as if the trigger was pulled (swrp_test_fire). </summary>
	public void TestShot()
	{
		if ( IsProxy || Stats is null || !Drawn || Reloading ) return;
		if ( Clip <= 0 ) { RequestReload(); return; }
		var eye = Player.Controller.EyeTransform;
		_sinceShot = 0;
		ShootLocal( eye.Position, eye.Rotation.Forward, true );
	}

	/// <summary> Owner: raise or lower the weapon (from chat /draw and /holster). </summary>
	public void SetDrawn( bool drawn )
	{
		if ( IsProxy ) return;
		if ( drawn && (Stats is null || Player is { Jailed: true } or { Frozen: true }) ) return;
		Drawn = drawn;
	}

	/// <summary> Host: make the owner put the weapon away (jail, freeze, stun). </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void ForceHolster() => Drawn = false;

	bool IsCallerOwner() => Rpc.Caller is { } caller && caller.Id == Network.OwnerId;
}

/// <summary>
/// A glowing bolt that flies from the muzzle to the impact point, then removes itself. It reads as light rather than a solid
/// shape: a hair-thin hot filament inside a haze of soft round glow sprites strung along it (brightest at the head, fading
/// to nothing at the tail), plus a wider halo round the head, all bright enough to bloom. It lights the walls as it passes
/// when it's near your camera and the effect-light budget allows (SwrpFx). Purely visual and local.
/// </summary>
public sealed class BlasterBolt : Component
{
	const float Speed = 7000f;
	const float Length = 60f;
	const int Haze = 6;

	Vector3 _start, _end;
	float _travelled;
	LineRenderer _core;
	readonly List<SpriteRenderer> _haze = new();
	SpriteRenderer _halo;
	PointLight _light;
	Color _color;

	public static void Spawn( Scene scene, Vector3 start, Vector3 end, Color color, bool stun = false )
	{
		var go = scene.CreateObject();
		go.Name = "blaster_bolt";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = start;

		var bolt = go.Components.Create<BlasterBolt>();
		bolt._start = start;
		bolt._end = end;
		bolt._color = LightProfile.Vivid( color );
		var scale = stun ? 1.5f : 1f;

		// the filament: thin and even (no wedge), fading in from the tail, a little hotter than the bolt's colour. Brightness comes
		// from alpha and size, not from multiplying the colour: overdriven channels clip, and blue clipped into cyan-white
		// (Angelo 2026-10-12: "not blue enough"). LightProfile.Vivid deepens blues and leaves other colours alone.
		var deep = LightProfile.Vivid( color );
		var hot = Color.Lerp( deep, Color.White, 0.06f );     // was 0.2: the core still read white
		bolt._core = go.Components.Create<LineRenderer>();
		bolt._core.UseVectorPoints = true;
		bolt._core.VectorPoints = new List<Vector3> { start, start };
		bolt._core.Color = new Gradient( new Gradient.ColorFrame( 0f, hot.WithAlpha( 0f ) ), new Gradient.ColorFrame( 0.45f, ( hot * 1.15f ).WithAlpha( 0.8f ) ), new Gradient.ColorFrame( 1f, ( hot * 1.3f ).WithAlpha( 1f ) ) );
		bolt._core.Width = new Curve( new Curve.Frame( 0f, 0.7f * scale ), new Curve.Frame( 1f, 0.7f * scale ) );
		bolt._core.Additive = true;
		bolt._core.Lighting = false;
		bolt._core.CastShadows = false;

		// the haze: soft glow sprites along the bolt, small and faint at the tail, bigger and brighter at the head
		var sprite = SwrpFx.GlowSprite;
		if ( sprite is not null )
		{
			for ( var i = 0; i < Haze; i++ )
			{
				var t = i / (float)( Haze - 1 );
				var r = Glow( go, sprite, ( deep * ( 1f + 0.3f * t ) ).WithAlpha( 0.25f + 0.55f * t * t ), ( 6f + 8f * t ) * scale );
				bolt._haze.Add( r );
			}
			bolt._halo = Glow( go, sprite, ( deep * 1.15f ).WithAlpha( 0.5f ), 24f * scale );
		}

		if ( LightingSystem.On ) Lighting.Bolt( go, color );     // the lighting framework (Code/Lighting)
		else if ( SwrpFx.TakeLight( scene, start ) || SwrpFx.TakeLight( scene, end ) )
			bolt._light = SwrpFx.AddLight( go, deep * 3.2f, 150f );
	}

	static SpriteRenderer Glow( GameObject parent, Sprite sprite, Color color, float size )
	{
		var go = new GameObject( parent, true, "haze" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var r = go.Components.Create<SpriteRenderer>();
		r.Sprite = sprite;
		r.Additive = true;
		r.Lighting = false;
		r.Shadows = false;
		r.Color = color;
		r.Size = new Vector2( size, size );
		return r;
	}

	protected override void OnUpdate()
	{
		var total = _start.Distance( _end );
		_travelled += Speed * Time.Delta;

		if ( _travelled - Length >= total )
		{
			GameObject.Destroy();
			return;
		}

		var dir = ( _end - _start ).Normal;
		var head = _start + dir * Math.Min( _travelled, total );
		var tail = _start + dir * Math.Clamp( _travelled - Length, 0f, total );

		_core.VectorPoints = new List<Vector3> { tail, head };
		WorldPosition = head;
		for ( var i = 0; i < _haze.Count; i++ )
			if ( _haze[i].IsValid() ) _haze[i].WorldPosition = Vector3.Lerp( tail, head, i / (float)( _haze.Count - 1 ) );
		if ( _halo.IsValid() ) _halo.WorldPosition = head;
		if ( _light.IsValid() ) _light.LightColor = _color * ( 2.9f + 0.5f * MathF.Sin( _travelled * 0.02f ) );
	}
}
