using System;

namespace Swrp;

/// <summary>
/// The roleplay identity of a spawned player. The host owns all the data (FromHost syncs) and pushes it here
/// from the <see cref="CharacterRecord"/>; the owning client only reads it and asks the host to change things.
/// </summary>
public sealed class SwrpPlayer : Component
{
	[Property] public PlayerController Controller { get; set; }
	[Property] public SkinnedModelRenderer Body { get; set; }

	[Sync( SyncFlags.FromHost )] public string CharacterId { get; set; }
	[Sync( SyncFlags.FromHost )] public string CharacterName { get; set; }
	[Sync( SyncFlags.FromHost )] public string Designation { get; set; }
	[Sync( SyncFlags.FromHost )] public string FactionId { get; set; }

	/// <summary> A Jedi's looks (Appearance text): the saber design and the outfit. </summary>
	[Sync( SyncFlags.FromHost )] public string Appearance { get; set; }
	[Sync( SyncFlags.FromHost )] public string ClassId { get; set; }
	[Sync( SyncFlags.FromHost )] public int RankIndex { get; set; }

	/// <summary> Holds their battalion's Commander post (host-checked every second): the battalion's commander model (SwrpCommanders). </summary>
	[Sync( SyncFlags.FromHost )] public bool BattalionCommander { get; set; }

	/// <summary> Every machine: the body is a named commander's model (SwrpCommanders), so the armour customiser's pieces stay off. </summary>
	public bool WearsCommanderModel { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Credits { get; set; }
	[Sync( SyncFlags.FromHost )] public string InventoryJson { get; set; }
	[Sync( SyncFlags.FromHost )] public ulong OwnerSteamId { get; set; }
	[Sync( SyncFlags.FromHost )] public string Description { get; set; }

	// Staff and roleplay state
	[Sync( SyncFlags.FromHost )] public bool Frozen { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Jailed { get; set; }
	/// <summary> In a bacta pod: held still (you can look about) until the pod lets you out. </summary>
	[Sync( SyncFlags.FromHost )] public bool InPod { get; set; }

	/// <summary> Aboard a flyable craft (Flight.SwrpFlightShip holds them in their seat and flies the camera). </summary>
	[Sync( SyncFlags.FromHost )] public bool InVehicle { get; set; }

	/// <summary> Local, every machine: standing in a craft's troop bay (SwrpFlightShip.HoldRiders): the body faces the seat's way
	/// (<see cref="RideRotation"/>) whatever you look at, with a hand up on the overhead line. </summary>
	public bool RideHold { get; set; }
	public Rotation RideRotation { get; set; }

	/// <summary> Local: sitting inside a closed cockpit or turret, so the body isn't drawn. </summary>
	public bool HideInVehicle { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Gagged { get; set; }

	// Staff powers (see SwrpStaffTools): only granted on duty.
	[Sync( SyncFlags.FromHost )] public bool Noclip { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Invisible { get; set; }
	[Sync( SyncFlags.FromHost )] public bool GodMode { get; set; }
	/// <summary> Moderator or Admin and on duty (host-set): their power hotkeys work (SwrpStaffKeys). </summary>
	[Sync( SyncFlags.FromHost )] public bool StaffOnDuty { get; set; }

	/// <summary> Radio channel /r talks on: squad, legion, command or open. Set by the host (/channel). </summary>
	[Sync( SyncFlags.FromHost )] public string RadioChannel { get; set; } = "legion";

	/// <summary> 0 whisper, 1 normal, 2 yell. Owner controlled; sets both how far text and the microphone carry. </summary>
	[Sync] public int VoiceMode { get; set; } = 1;
	/// <summary> Your voice goes out on your radio net (Y). </summary>
	[Sync] public bool RadioTx { get; set; }
	/// <summary> Your comm receiver is on: you hear voices on your nets (N). </summary>
	[Sync] public bool CommsOn { get; set; } = true;
	/// <summary> The emote the trooper is holding (SwrpEmotes: none, salute, at ease). </summary>
	[Sync] public int Emote { get; set; }
	/// <summary> Talking on the radio right now (voice on the radio and push-to-talk held): the wrist comm comes up. </summary>
	[Sync] public bool Transmitting { get; set; }

	/// <summary> Sprinting (Shift held and moving fast on the ground): a drawn rifle is carried low across the body. </summary>
	[Sync] public bool Sprinting { get; set; }

	/// <summary> Owner synced: the weapon on safety (Shift+E+R): carried lowered, relaxed, and it won't fire or aim. </summary>
	[Sync] public bool WeaponSafety { get; set; }

	/// <summary> Owner synced: on safety and relaxed (the third Shift+E+R step, for walking the ship): the rifle carried low in
	/// both hands with the unarmed walk (Angelo, 2026-10-18). Always with <see cref="WeaponSafety"/>, so it won't fire. </summary>
	[Sync] public bool WeaponRelaxed { get; set; }

	/// <summary> Walking pace (no Shift), before the class's speed scale: 110 read as a light jog (Angelo, 2026-10-18). </summary>
	[ConVar( "swrp_walk_speed", Help = "Walking pace without Shift (units/s, before the class speed scale; was 110)", Saved = true )]
	public static float WalkPace { get; set; } = 80f;
	/// <summary> Walking relaxed (the patrol walk clip covers ~40 in/s): a little over it, so the steps stay unhurried. </summary>
	public static float PatrolPace { get; set; } = 50f;
	// dev (swrp_npc_foot_debug): how fast this player's planted foot slides against their speed, once a second
	Vector3 _footL, _footR; float _minFoot = 9999f, _sumSpeed; int _footN; RealTimeSince _sinceFootLog;
	void FootProbe()
	{
		if ( Body.GetBoneObject( "ankle_L" ) is not { } l || Body.GetBoneObject( "ankle_R" ) is not { } r ) { Body.CreateBoneObjects = true; return; }
		var dt = MathF.Max( Time.Delta, 0.001f );
		_minFoot = MathF.Min( _minFoot, MathF.Min( ( l.WorldPosition - _footL ).WithZ( 0 ).Length, ( r.WorldPosition - _footR ).WithZ( 0 ).Length ) / dt );
		_footL = l.WorldPosition; _footR = r.WorldPosition;
		_sumSpeed += Controller.Velocity.WithZ( 0 ).Length; _footN++;
		if ( _sinceFootLog < 1f ) return;
		Log.Info( $"[SWRP] player feet: body {_sumSpeed / _footN:0} u/s, slowest foot {_minFoot:0} u/s, ducking {Controller.IsDucking}" );
		_sinceFootLog = 0; _minFoot = 9999f; _sumSpeed = 0; _footN = 0;
	}

	/// <summary>
	/// Stock animations: the speeds the s&amp;box human walk and run are made for (the controller's own 110 and 320; Angelo,
	/// 2026-10-19: "remeasure the speed for walking and running according to animation" — Garry's Mod's 200/400 outran the
	/// clips), and holding Walk (Alt) the graph's slow walk.
	/// </summary>
	public const float GModWalk = 110f, GModRun = 320f, SlowWalk = 60f;
	/// <summary> Garry's Mod's own speeds on its animation sets: walk, sprint, Alt slow walk, crouched (Angelo, 2026-10-19). </summary>
	// set from the clips' own stride so they play near 1x (Angelo, 2026-10-21: GMod's 200/400 "really fast"): the run clip covers
	// 195 u/s, the walk 74, the crouch walk 56. Default 155 is the run clip at 0.8x (a jog, ~3.9 m/s), sprint 210 at 1.08x
	// (~5.3 m/s), Alt the walk clip at 1x. 1 u = 1 inch
	// 2026-10-21: from the shared speed table (SwrpSpeeds, m/s): the jog 4.4 m/s (was 3.9), the sprint 5.3, a little over it
	public static float GmodSetWalk => SwrpSpeeds.U( SwrpSpeeds.Jog );
	public static float GmodSetRun => SwrpSpeeds.U( SwrpSpeeds.Sprint );
	public static float GmodSetSlow => SwrpSpeeds.U( SwrpSpeeds.Walk );
	public static float GmodSetDucked => SwrpSpeeds.U( SwrpSpeeds.Crouch );
	int _speedVersion;

	/// <summary> Owner, every frame: a speed convar changed (swrp_speed_*): put the new speeds on the controller. </summary>
	void SpeedTableTick()
	{
		if ( IsProxy || !Controller.IsValid() || !UsesGmodSet || _speedVersion == SwrpSpeeds.Version ) return;
		_speedVersion = SwrpSpeeds.Version;
		Controller.WalkSpeed = GmodSetWalk * Class.SpeedScale;
		Controller.RunSpeed = GmodSetRun * Class.SpeedScale;
		Controller.DuckedSpeed = GmodSetDucked * Class.SpeedScale;
		ClassRunSpeed = Controller.RunSpeed;
		ClassWalkSpeed = Controller.WalkSpeed;
	}

	/// <summary> How far down the stock crouch goes (the graph's duck, 1 = its full crouch). </summary>
	public static float CrouchDepth { get; set; } = 0.55f;

	/// <summary> Owner synced: lying prone (Z; SwrpProne animates it, SwrpVitals slows it to a crawl). </summary>
	[Sync] public bool Prone { get; set; }

	public ClassDefinition Class => ClassRegistry.Get( ClassId ) ?? ClassRegistry.Default;
	public FactionDefinition Faction => FactionRegistry.Get( FactionId ) ?? FactionRegistry.Default;
	public RankDefinition Rank => Faction.GetRank( RankIndex );
	public string FullTitle => EventDef is { } ev ? ev.Name : Faction.TitleFor( Rank, Designation, CharacterName );

	/// <summary> "SGT Hardcase", or the event character's name while a Game Master plays one. </summary>
	public string ShortName => EventDef is { } ev ? ev.Name : Faction.ShortTitleFor( Rank, CharacterName );

	/// <summary> A Game Master playing an event character (an <see cref="NpcRegistry"/> id: a droid or a CIS commander). Not saved. </summary>
	[Sync( SyncFlags.FromHost )] public string EventCharacter { get; set; }
	public NpcDefinition EventDef => string.IsNullOrEmpty( EventCharacter ) ? null : NpcRegistry.Get( EventCharacter );

	/// <summary> Which side this trooper fights for: their battalion's (the GAR), or their event character's. </summary>
	public string Allegiance => EventDef?.AllegianceId ?? Faction.Allegiance;

	/// <summary> The player this machine controls, if it has spawned one. </summary>
	public static SwrpPlayer Local => SwrpPlayers.Local;      // cached once a frame (Server/SwrpPlayers.cs)

	string _cachedJson;
	List<ItemStack> _cachedInventory = new();

	public IReadOnlyList<ItemStack> Inventory
	{
		get
		{
			if ( _cachedJson != InventoryJson )
			{
				_cachedJson = InventoryJson;
				_cachedInventory = string.IsNullOrEmpty( InventoryJson ) ? new() : Json.Deserialize<List<ItemStack>>( InventoryJson ) ?? new();
			}

			return _cachedInventory;
		}
	}

	/// <summary>
	/// Host only. Copies a character record onto this player. Call before NetworkSpawn so the values go out with the spawn.
	/// </summary>
	public void ApplyRecord( CharacterRecord record )
	{
		CharacterId = record.Id;
		CharacterName = record.Name;
		Designation = record.Designation;
		FactionId = record.FactionId;
		Appearance = record.Appearance ?? "";
		ClassId = ClassRegistry.ForRank( record.FactionId, record.ClassId, record.RankIndex );      // Sergeant+ troopers are Officers
		Description = record.Description ?? "";
		RankIndex = record.RankIndex;
		BattalionCommander = SwrpBattalions.PostOf( record.FactionId, record.OwnerSteamId ) == SwrpBattalions.Post.Commander;
		Credits = record.Credits;
		OwnerSteamId = record.OwnerSteamId;
		InventoryJson = Json.Serialize( record.Inventory );
	}

	/// <summary>
	/// How big troopers are against the map (model, collision hull, eye height and step height). The playermodels stand 68
	/// units at scale 1 (the clone animation set is authored at that size: scaling the model itself twisted the arms), so 1.06
	/// makes a clone in armour 72 units = 1.83 m, true to the films against the inch-unit ship (corridors 2.8 m high).
	/// </summary>
	[ConVar( "swrp_player_scale", Help = "Trooper size against the map (host, default 1.06 = true size: a clone is 72 units, 1.83 m)", Saved = true )]
	public static float PlayerScale { get; set; } = 1.06f;

	/// <summary> The host's swrp_player_scale, synced so every machine sizes this trooper the same. </summary>
	[Sync( SyncFlags.FromHost )] public float BodyScale { get; set; } = 1f;

	float _appliedScale = -1f;
	bool _appliedProne;

	/// <summary> The trooper scale everyone uses: the host's setting, as synced on any trooper (or the local setting). </summary>
	public static float PlayerScaleFor( Scene scene ) => Networking.IsHost || scene is null ? Math.Clamp( PlayerScale, 0.5f, 2f )
		: scene.GetAllComponents<SwrpPlayer>().FirstOrDefault()?.BodyScale ?? 1f;

	void ApplyScale()
	{
		if ( Networking.IsHost ) BodyScale = Math.Clamp( PlayerScale, 0.5f, 2f );
		var s = BodyScale;
		var model = s * ( EventDef?.Scale ?? 1f );
		if ( Body.IsValid() && Body.GameObject.LocalScale.x != model ) Body.GameObject.LocalScale = model;
		if ( _appliedScale == s && _appliedProne == Prone || !Controller.IsValid() ) return;
		_appliedScale = s;
		_appliedProne = Prone;

		// prone: the eye comes down to a trooper lying on their front with the rifle up
		Controller.BodyHeight = ( Prone ? 26f : 72f ) * s;
		Controller.DuckedHeight = ( Prone ? 26f : 36f ) * s;
		Controller.EyeDistanceFromTop = 8f * s;
		if ( GetComponent<Sandbox.Movement.MoveModeWalk>() is { } walk )
		{
			walk.StepUpHeight = 18f * s;
			walk.StepDownHeight = 18f * s;
		}
		if ( Controller.ColliderObject is { } cols )
		{
			if ( cols.Components.Get<CapsuleCollider>() is { } cap )
			{
				cap.Start = Vector3.Up * 60.69f * s;
				cap.End = Vector3.Up * 36f * s;
			}
			if ( cols.Components.Get<BoxCollider>() is { } box )
			{
				box.Center = Vector3.Up * 18f * s;
				box.Scale = new Vector3( 16f, 16f, 36f * s );
			}
		}
		if ( GameObject.Children.FirstOrDefault( c => c.Name == "Nametag" ) is { } tag ) tag.LocalPosition = Vector3.Up * ( 88f * s );
	}

	protected override void OnUpdate()
	{
		ApplyScale();
		if ( GetComponent<SwrpFootsteps>() is null ) Components.Create<SwrpFootsteps>();
		if ( GetComponent<SwrpProne>() is null ) Components.Create<SwrpProne>();
		UpdateEmote();
		// the battalion's armour, helmet and class kit on the body (Characters/SwrpOutfit.cs)
		SwrpOutfit.Apply( Body, FactionId, !IsProxy && Controller.IsValid() && !Controller.ThirdPerson && SwrpUiState.SpectateSteamId == 0, Appearance );

		// Troopers deployed before free look existed get it too.
		if ( !IsProxy && GetComponent<SwrpFreeLook>() is null ) Components.Create<SwrpFreeLook>();

		// Scenes built before the staff panel existed get it on the HUD object.
		if ( !IsProxy && !_checkedStaffPanel )
		{
			_checkedStaffPanel = true;
			if ( !Scene.GetAllComponents<StaffPanel>().Any() && Scene.GetAllComponents<SwrpHud>().FirstOrDefault() is { } hud )
				hud.GameObject.Components.Create<StaffPanel>();
		}

		ApplyClassModel();
		UpdateVoice();
		if ( Networking.IsHost && CharacterId is not null )
		{
			SwrpStaff.TickPlayer( this );
			SwrpStaffTools.TickPlayer( this );
			// the Commander post can change while they play (staff, /battalion role): their commander model follows it
			if ( _sinceCommanderCheck > 1f )
			{
				_sinceCommanderCheck = 0;
				var commander = SwrpBattalions.PostOf( FactionId, OwnerSteamId ) == SwrpBattalions.Post.Commander;
				if ( BattalionCommander != commander ) BattalionCommander = commander;
			}
		}

		UpdateInvisibility();
		UpdateNoclip();

		if ( IsProxy ) return;

		if ( SwrpUiState.SpectateSteamId != 0 && ( Input.Pressed( "Jump" ) || Input.Pressed( "Use" ) ) )
			SwrpUiState.SpectateSteamId = 0;

		if ( SwrpUiState.ChatOpen )
		{
			// Typing: don't let hotkeys fire, and Escape backs out.
			if ( Input.EscapePressed )
			{
				Input.EscapePressed = false;
				SwrpUiState.ChatOpen = false;
			}
		}
		else
		{
			if ( Input.Pressed( "Inventory" ) )
				SwrpUiState.InventoryOpen = !SwrpUiState.InventoryOpen;

			if ( Input.Pressed( "Characters" ) )
				SwrpUiState.CharacterMenuOpen = !SwrpUiState.CharacterMenuOpen;

			if ( Input.Pressed( "VoiceRange" ) )
				SwrpChat.CycleVoice();

			// the Grenade key (T by default; it exists once the game restarts with the new input settings)
			if ( !SwrpUiState.AnyOpen && HasGrenadeKey && Input.Pressed( "Grenade" ) )
				ThrowGrenade();

			SwrpComms.OwnerInput( this );
			SwrpStaffKeys.Tick( this );

			if ( Input.Pressed( "Chat" ) && SwrpUiState.ChatClosedFor > 0.3f && !SwrpUiState.InventoryOpen && !SwrpUiState.CharacterMenuOpen )
				SwrpUiState.ChatOpen = true;
		}

		if ( Controller.IsValid() )
		{
			// Stunned or dead: no control. Wounded: you can still crawl (SwrpVitals slows you down).
			var uiOpen = SwrpUiState.AnyOpen || Frozen || GetComponent<SwrpVitals>() is { IsDead: true } or { IsStunned: true };
			Controller.UseInputControls = !uiOpen && !Noclip && !InPod && !InVehicle && !SwrpPoseRecorder.Walking && !SwrpAimSweep.Running;      // (dev: a DevWalk or aim sweep drives it)
			Controller.UseLookControls = !uiOpen && !InVehicle;
			Controller.UseCameraControls = !InVehicle;
			if ( uiOpen || InPod || InVehicle ) Controller.WishVelocity = Vector3.Zero;
		}
	}

	bool _wasNoclip;
	bool _checkedStaffPanel;
	RealTimeSince _sinceCommanderCheck;

	/// <summary>
	/// Owner: push-to-talk really held. A staff hotkey on the same key as push-to-talk fires on a tap, so the first moment
	/// of a press doesn't count as talking (no squelch or raised wrist for a power tap).
	/// </summary>
	public bool VoiceKeyHeld => Input.Down( "Voice" ) && !SwrpStaffKeys.HoldingTap;
	GameObject _colliders;

	/// <summary> Owner: fly through walls while noclipping (WASD along the view, Space up, Ctrl down, Shift fast). </summary>
	void UpdateNoclip()
	{
		if ( IsProxy || !Controller.IsValid() ) return;

		if ( Noclip != _wasNoclip )
		{
			_wasNoclip = Noclip;
			_colliders ??= GameObject.Children.FirstOrDefault( c => c.Name == "Colliders" );
			if ( _colliders.IsValid() ) _colliders.Enabled = !Noclip;
			if ( GetComponent<Rigidbody>() is { } rb )
			{
				rb.Gravity = !Noclip;
				rb.Velocity = Vector3.Zero;
				rb.MotionEnabled = !Noclip;
			}
		}

		if ( !Noclip ) return;
		// the controller keeps the velocity it had when noclip went on (and the rigidbody any it picks up): left alone it
		// slid you along with no key held (Angelo, 2026-10-13). Noclip moves you by position only, so hold both at rest.
		Controller.WishVelocity = Vector3.Zero;
		if ( GetComponent<Rigidbody>() is { } body ) { body.Velocity = Vector3.Zero; body.AngularVelocity = Vector3.Zero; }
		if ( SwrpUiState.AnyOpen ) return;
		var look = Controller.EyeAngles.ToRotation();
		var move = Input.AnalogMove;
		var dir = look.Forward * move.x + look.Left * move.y;
		if ( Input.Down( "Jump" ) ) dir += Vector3.Up;
		if ( Input.Down( "Duck" ) ) dir += Vector3.Down;
		var speed = Input.Down( "Run" ) ? 1400f : 450f;
		WorldPosition += dir.ClampLength( 1f ) * speed * Time.Delta;
	}

	/// <summary> Every machine: an invisible staff member's body, nametag and gun are hidden from everyone else. </summary>
	void UpdateInvisibility()
	{
		if ( !Body.IsValid() ) return;
		var hide = Invisible && IsProxy || HideInVehicle && InVehicle;
		var scale = hide ? 0.001f : BodyScale * ( EventDef?.Scale ?? 1f );     // the trooper scale (ApplyScale) when visible
		if ( Body.LocalScale.x != scale ) Body.LocalScale = scale;
	}

	// ----- emotes -----

	float _emoteWeight;
	float _commWeight;
	int _shownEmote;
	RealTimeSince _sinceEmote;
	bool _emoteUiChecked;
	float _carryWeight, _stride, _gripWeight, _groundSpeed;
	bool _carryShown;
	Vector3 _lastCarryPos;
	bool _commShown;
	bool _ridePosed;

	/// <summary> Empty hands, on your feet and free to move: you can salute or stand at ease. </summary>
	public bool CanEmote => GetComponent<SwrpBlaster>() is not { Drawn: true } && !Swrp.Jedi.JediCombat.HoldsSaber( this ) && GetComponent<SwrpVitals>() is not ( { IsDead: true } or { IsDowned: true } )
		&& !InPod && !InVehicle && !Frozen && !Jailed && CharacterId is not null;

	/// <summary> Owner: toggle an emote (picking the one you're in, or None, ends it). </summary>
	public void StartEmote( int emote )
	{
		if ( IsProxy ) return;
		Emote = !CanEmote || emote == Emote ? SwrpEmotes.None : emote;
		_sinceEmote = 0;
	}

	/// <summary> Every machine: blend the body into and out of the emote; the owner also ends it when it no longer fits. </summary>
	void UpdateEmote()
	{
		if ( !IsProxy )
		{
			// the emote picker lives on the HUD; add it if this scene was built before it existed
			if ( !_emoteUiChecked )
			{
				_emoteUiChecked = true;
				var ui = Scene.GetAllComponents<ScreenPanel>().FirstOrDefault( p => p.GameObject.Name == "UI" );
				if ( ui.IsValid() && ui.GetComponent<EmoteWheel>() is null ) ui.Components.Create<EmoteWheel>();
			}
			if ( Emote != SwrpEmotes.None && !CanEmote ) Emote = SwrpEmotes.None;

			// on the radio and holding push-to-talk: the wrist comm comes up (not while down or in a pod)
			var tx = RadioTx && !Gagged && VoiceKeyHeld && !SwrpUiState.ChatOpen && GetComponent<SwrpVitals>() is not ( { IsDead: true } or { IsDowned: true } ) && !InPod;
			if ( Transmitting != tx ) Transmitting = tx;

			// held while Shift stays down above a fast walk: dipping under the threshold for a moment (turning, a step, a brief
			// slowdown) used to drop the sprint, swap the GMod set and restart the legs mid-stride ("kicking ahead", 2026-10-20)
			var flat = Controller.IsValid() ? Controller.Velocity.WithZ( 0 ).Length : 0f;
			var sprint = Controller.IsValid() && ( Controller.IsOnGround || Sprinting ) && !Noclip && !InPod && ( Input.Down( "Run" ) || SwrpPoseRecorder.DevSprint ) && !SwrpUiState.AnyOpen
				&& flat > Controller.WalkSpeed * ( Sprinting ? 0.6f : 1.15f );
			// and it rides out a short drop (a wall bump, a quick let-go of W, a tight turn) for 0.4 s: each drop swapped the GMod set
			// and its first frame flashed GMod's idle stance mid-run, the "random" kick (Shift-run test, 2026-10-21)
			if ( sprint ) _sinceSprintWanted = 0;
			sprint = sprint || Sprinting && _sinceSprintWanted < 0.4f && ( Input.Down( "Run" ) || SwrpPoseRecorder.DevSprint || SwrpPoseRecorder.Walking );
			if ( Sprinting != sprint ) Sprinting = sprint;
		}

		if ( Emote != SwrpEmotes.None ) _shownEmote = Emote;
		_emoteWeight = _emoteWeight.Approach( Emote != SwrpEmotes.None ? 1f : 0f, Time.Delta * 4f );
		_commWeight = _commWeight.Approach( Transmitting ? 1f : 0f, Time.Delta * 5f );

		// how fast the body is moving over the ground (every machine, from its own position)
		var moved = ( WorldPosition - _lastCarryPos ).WithZ( 0 ).Length;
		_lastCarryPos = WorldPosition;
		if ( moved < 60f ) _groundSpeed = _groundSpeed.LerpTo( moved / MathF.Max( Time.Delta, 0.001f ), Time.Delta * 6f );

		// a rifle carried low across the body while sprinting; up again at once to aim, fire or reload. Standing still keeps
		// the two-handed hold (the old relaxed low ready put the long DC-15A through the left forearm, Angelo 2026-10-12)
		var blaster = GetComponent<SwrpBlaster>();
		// sprinting: the rifle stays in both hands at the low ready (the safety hold, muzzle down and across) instead of the old
		// one-handed low carry, which hung the long DC-15A muzzle-down at the trooper's side (Angelo, 2026-10-14)
		var sprintReady = Sprinting && !InVehicle && blaster is { Drawn: true, Reloading: false, Stats.HoldType: 2 } && blaster.SinceFired > 0.3f && blaster.AimAmount < 0.1f;
		// relaxed: the rifle carried low across the body in both hands (the carry pose; SwrpEmotes.CarryPose)
		var carry = WeaponRelaxed && WeaponSafety && !InVehicle && blaster is { Drawn: true, Reloading: false, Stats.HoldType: 2 };
		// the support hand on a two-handed rifle's real handguard (not while reloading: that needs the hand)
		var rifle = !InVehicle && blaster is { Drawn: true, Reloading: false } && ( blaster.Stats?.HoldType == 2 || SwrpEmotes.Pistols.Contains( blaster.Stats?.ModelPath ?? "" ) );     // not in a craft seat; pistols held out in both hands too
		_gripWeight = _gripWeight.Approach( rifle ? 1f : 0f, Time.Delta * 6f );
		if ( Body.IsValid() ) SwrpEmotes.HeldModel[Body] = blaster is { Drawn: true } ? blaster.Stats?.ModelPath : null;
		if ( Body.IsValid() ) SwrpEmotes.MuzzleOf[Body] = blaster?.Stats?.MuzzleLocal ?? new Vector3( 18f, 0f, 0f );
		// on safety the rifle stays in the shoulder with the muzzle low and the hands still on it (the low ready)
		if ( Body.IsValid() ) { if ( WeaponSafety || sprintReady ) SwrpEmotes.Lowered.Add( Body ); else SwrpEmotes.Lowered.Remove( Body ); }
		// the armour takes on the colour of the lamps around this trooper (Lighting/RoomLight.cs); event characters keep their own tint
		if ( Body.IsValid() && EventDef is null ) Body.Tint = RoomLight.BodyTint( this, WorldPosition );
		if ( Body.IsValid() ) { if ( blaster is { AimAmount: > 0.5f } ) SwrpEmotes.Aiming.Add( Body ); else SwrpEmotes.Aiming.Remove( Body ); }
		if ( Body.IsValid() ) { if ( carry ) SwrpEmotes.Relaxed.Add( Body ); else SwrpEmotes.Relaxed.Remove( Body ); }
		// a GMod set raised (ar2 or pistol): GMod's aim layer bends the spine and arms to where this trooper looks
		if ( Body.IsValid() )
		{
			if ( UsesGmodSet && blaster is { Drawn: true } && GmodSet is not ( null or "normal" or "passive" ) ) SwrpEmotes.GmodAim[Body] = GmodSet;
			else SwrpEmotes.GmodAim.Remove( Body );
			// stock set, gun lowered (Shift+E+R or sprinting): keep it lowered walking too (SwrpEmotes.Lowered.cs)
			if ( !ClassDefinition.Custom && !UsesGmodSet && blaster is { Drawn: true, Reloading: false } && ( WeaponSafety || Sprinting ) ) SwrpEmotes.LoweredWalk[Body] = _groundSpeed;
			else SwrpEmotes.LoweredWalk.Remove( Body );
		}
		if ( Body.IsValid() && Controller.IsValid() )
		{
			// on safety: the ready hold across the chest along the view (what off safety was; Angelo, 2026-10-13). Off safety the
			// rifle is shouldered at the eye and points at the crosshair point (every machine traces from the synced eye)
			SwrpEmotes.AimDir[Body] = Controller.EyeAngles.Forward;
			if ( !WeaponSafety && blaster is { Drawn: true } )
			{
				// the owner aims from the camera (in third person the crosshair is the camera's, not the eye's); everyone else from the eye
				var cam = !IsProxy && Scene.Camera.IsValid() ? Scene.Camera : null;
				var eye = cam?.WorldPosition ?? Controller.EyePosition;
				var dir = cam?.WorldRotation.Forward ?? Controller.EyeAngles.Forward;
				var tr = Scene.Trace.Ray( eye, eye + dir * 4000f ).IgnoreGameObjectHierarchy( GameObject ).Run();
				SwrpEmotes.AimPoint[Body] = tr.Hit ? tr.EndPosition : eye + dir * 4000f;
			}
			else SwrpEmotes.AimPoint.Remove( Body );
		}
		_carryWeight = _carryWeight.Approach( carry ? 1f : 0f, Time.Delta * ( carry ? 5f : 9f ) );
		// the step cycle for the carry's bounce, from how far the body has moved
		if ( moved < 60f ) _stride = ( _stride + moved / ( 92f * BodyScale ) ) % 1f;

		if ( RideHold && Body.IsValid() )
		{
			// riding standing in a gunship bay: still, facing the way the craft faces, a hand on the line
			_bladeW = _stanceLegs = 0f;
			_bladeYaw = null;
			if ( _aimBodyBase is { } aimBody && Controller.IsValid() ) Controller.AimStrengthBody = aimBody;
			SwrpEmotes.EndStance( Body );
			Body.WorldRotation = RideRotation;
			SwrpEmotes.Apply( Body, SwrpEmotes.Handhold, 1f, _commWeight * _commWeight * ( 3f - 2f * _commWeight ) );
			_ridePosed = true;
			return;
		}
		if ( _ridePosed && Body.IsValid() ) { _ridePosed = false; SwrpEmotes.Apply( Body, SwrpEmotes.None, 0f ); }

		// shouldered off safety: side-on to the target and, standing still, the shooting stance (kept through a reload)
		UpdateBlade( ClassDefinition.Custom && blaster is { Drawn: true, Stats.HoldType: 2 } && SwrpEmotes.ShoulderedAtEye( blaster.Stats.ModelPath ), sprintReady );

		var wantsPose = _shownEmote != SwrpEmotes.None || _commWeight > 0f || _commShown || _carryWeight > 0f || _carryShown || _gripWeight > 0f || blaster is { Drawn: true };
		if ( wantsPose ) _sincePoseWanted = 0;
		// a little past the last reason: holstering swaps the model and fades the arms and legs over 0.3 s, and stopping here as
		// the grip faded out left those bones held mid-fade, the empty right hand stuck up in a half grip (Angelo, 2026-10-21)
		if ( ( wantsPose || _sincePoseWanted < 0.6f ) && Body.IsValid() )
		{
			static float Ease( float x ) => x * x * ( 3f - 2f * x );
			// a GMod set starting or stopping: blend the legs across the graph's jump between GMod's idle stance and the stride,
			// from this frame (the velocity changes here, before the graph sees it; kick.py, 2026-10-20)
			var movingNow = Controller.IsValid() && Controller.Velocity.WithZ( 0 ).Length > 0.5f;
			if ( UsesGmodSet && movingNow != _wasMoving ) SwrpEmotes.FadeFromHere( Body );
			_wasMoving = movingNow;
			SwrpEmotes.Apply( Body, _shownEmote, Ease( _emoteWeight ), Ease( _commWeight ), Ease( _carryWeight ), _stride, Ease( _gripWeight ) );
			if ( _emoteWeight <= 0f ) _shownEmote = SwrpEmotes.None;
			_commShown = _commWeight > 0f;
			_carryShown = _carryWeight > 0f;
		}
	}

	float _bladeW, _stanceLegs;
	float? _bladeYaw;

	/// <summary>
	/// Every machine: a rifle shouldered off safety turns the trooper side-on, left shoulder to the target, and standing still
	/// brings the shooting stance: hips down, feet split along the aim, chest leaned into the gun (Angelo's references,
	/// 2026-10-16; SwrpEmotes.Stance). The controller turns the body towards the view before this runs; this sets it after,
	/// every frame it's turned, and hands it back facing the view.
	/// </summary>
	void UpdateBlade( bool rifle, bool sprintReady )
	{
		if ( !Body.IsValid() || !Controller.IsValid() ) return;
		// lying down, seated or flying: straight off (blending out would bend the legs of the prone or seated pose for a moment)
		var off = Prone || GetComponent<SwrpProne>() is { Down: true } || InVehicle || InPod || Noclip
			|| GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true };
		if ( off ) _bladeW = _stanceLegs = 0f;
		var shouldered = rifle && !off && !WeaponSafety && !sprintReady && GetComponent<SwrpVitals>() is not { IsStunned: true };
		_bladeW = _bladeW.Approach( shouldered ? 1f : 0f, Time.Delta * 5f );
		var still = Controller.IsOnGround && !Controller.IsDucking && _groundSpeed < 30f;
		_stanceLegs = _stanceLegs.Approach( shouldered && still ? 1f : 0f, Time.Delta * ( still ? 4f : 8f ) );
		_walkW = _walkW.Approach( _groundSpeed >= 30f ? 1f : 0f, Time.Delta * 6f );
		var w = _bladeW * _bladeW * ( 3f - 2f * _bladeW );
		var eyeYaw = Controller.EyeAngles.yaw;
		// walking, the hips turn back towards the aim so the legs walk forward, and the chest alone stays side-on
		var bodyBlade = SwrpEmotes.BladeYaw * w * ( 1f - SwrpEmotes.WalkChestShare * _walkW );
		var chestYaw = -SwrpEmotes.BladeYaw * w * SwrpEmotes.WalkChestShare * _walkW;
		if ( _bladeW > 0f )
		{
			// the body follows the aim (a little behind a fast flick), turned the blade's angle right of it
			var now = _bladeYaw ?? Body.WorldRotation.Yaw();
			var step = 720f * Time.Delta;
			_bladeYaw = now + Math.Clamp( MathX.DeltaDegrees( now, eyeYaw - bodyBlade ), -step, step );
			Body.WorldRotation = Rotation.FromYaw( _bladeYaw.Value );
		}
		else _bladeYaw = null;
		// the controller's look-at would twist the chest back square to the view (and straighten the support arm): turned
		// side-on the chest stays along the body, whichever of us runs first this frame
		_aimBodyBase ??= Controller.AimStrengthBody;
		Controller.AimStrengthBody = _aimBodyBase.Value * ( 1f - w );
		if ( _bladeW > 0f ) Body.SetLookDirection( "aim_body", SwrpEmotes.BladedBodyLook( Controller.EyeAngles.Forward, Body.WorldRotation, w ) );
		SwrpEmotes.Stance( Body, w * _stanceLegs, w, eyeYaw, 0f, chestYaw );
	}
	float? _aimBodyBase;
	RealTimeSince _sincePoseWanted = 10f, _sinceSprintWanted = 10f;
	bool _wasMoving;
	float _walkW;

	/// <summary> Microphone reach, helmet or radio playback and gag state, on every machine for its own ears. </summary>
	void UpdateVoice()
	{
		var voice = GetComponent<Voice>();
		if ( !voice.IsValid() ) return;

		// range, helmet or radio, per listener (SwrpComms)
		SwrpComms.Route( this, voice );
		voice.Enabled = !Gagged || IsProxy;
	}

	/// <summary> Host to owner: move this player. The owner simulates its own movement, so only the owner can teleport it. </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void TeleportTo( Vector3 position, float yaw )
	{
		WorldPosition = position;
		if ( Controller.IsValid() )
		{
			Controller.EyeAngles = new Angles( 0, yaw, 0 );
			if ( Controller.Body.IsValid() ) Controller.Body.Velocity = Vector3.Zero;
		}

		Network.ClearInterpolation();
	}

	/// <summary> Host to owner: start or stop watching another player (0 stops). </summary>
	[Rpc.Owner( NetFlags.HostOnly )]
	public void SetSpectate( ulong steamId )
	{
		SwrpUiState.SpectateSteamId = steamId;
	}

	protected override void OnPreRender()
	{
		// stock set crouching: only part way down, the same depth as the NPC clones (SwrpNpc). Here, after every update, so the
		// controller's own full "duck" doesn't win (Angelo, 2026-10-19: "make sure the player crouch matches the NPC")
		// a GMod set: the controller's animation update run here, first, so GMod's pace (SwrpEmotes.GmodPace, below) is the last
		// word on the move parameters. Left to the controller it rewrote them after us some frames: the graph blended the walk
		// into the run, feet ahead of the hips and legs flickering (Angelo, 2026-10-19; recorded with SwrpPoseRecorder.DevWalk)
		if ( Body.IsValid() && Controller.IsValid() )
		{
			var ours = UsesGmodSet && !ClassDefinition.Custom;
			if ( Controller.UseAnimatorControls == ours ) Controller.UseAnimatorControls = !ours;
			// moving, the body faces the aim at once, as GMod's player does: the controller's body yaw trailed the view by ~45
			// degrees while running and turning, so the legs ran in a sideways blend at up to 2x speed and kicked across the torso
			// (kick.py on the scripted run, 2026-10-20). Standing still the controller's own turn-when-past-the-limit stays
			if ( ours && Controller.Velocity.WithZ( 0 ).Length > 20f )
				Body.WorldRotation = Rotation.Slerp( Body.WorldRotation, Rotation.FromYaw( Controller.EyeAngles.yaw ), 1f - MathF.Exp( -Time.Delta * 20f ) );
			if ( ours ) Controller.UpdateAnimation( Body );
		}
		if ( !ClassDefinition.Custom && Body.IsValid() && Controller.IsValid() && Controller.IsDucking ) Body.Set( "duck", UsesGmodSet ? 1f : CrouchDepth );      // a GMod set: GMod's own crouch clips, all the way
		// stock set: the clips at their own pace, and the graph's slow walk (not a slowed fast walk) when walking slowly
		if ( !ClassDefinition.Custom && Body.IsValid() )
		{
			Body.PlaybackRate = 1f;
			SwrpEmotes.GmodPace( Body );
			Body.Set( "move_style", !UsesGmodSet && _groundSpeed > 5f && _groundSpeed < 85f ? 1 : 0 );
		}
		// owner: Walk (Alt) held, a slow walk
		SpeedTableTick();
		if ( !ClassDefinition.Custom && !IsProxy && Controller.IsValid() && ClassWalkSpeed > 0f && !InVehicle && !Noclip && !Prone )
		{
			var want = Input.Down( "Walk" ) && !SwrpUiState.AnyOpen ? ( UsesGmodSet ? GmodSetSlow : SlowWalk ) * Class.SpeedScale : ClassWalkSpeed;
			if ( Controller.WalkSpeed != want && GetComponent<SwrpVitals>() is not { IsDowned: true } ) Controller.WalkSpeed = want;
		}
		if ( Swrp.SwrpNpc.FootDebug && !IsProxy ) FootProbe();
		if ( IsProxy || SwrpUiState.SpectateSteamId == 0 ) return;

		var target = Scene.GetAllComponents<SwrpPlayer>().FirstOrDefault( x => x.OwnerSteamId == SwrpUiState.SpectateSteamId && x != this );
		if ( !target.IsValid() || !target.Controller.IsValid() )
		{
			SwrpUiState.SpectateSteamId = 0;
			return;
		}

		var cam = Scene.Camera;
		if ( !cam.IsValid() ) return;

		cam.WorldPosition = target.Controller.EyePosition;
		cam.WorldRotation = target.Controller.EyeAngles.ToRotation();
	}

	string _appliedClass;
	float _baseWalk, _baseRun, _baseDucked;

	/// <summary> The class's sprint speed. Stamina caps running to walking pace without losing this. </summary>
	public float ClassRunSpeed { get; private set; }

	/// <summary> The class's walking speed, restored after crawling while downed. </summary>
	public float ClassWalkSpeed { get; private set; }

	/// <summary> Every machine: the body wears a Garry's Mod animation set (the clip holds the gun; the graph's hold layer stays off). </summary>
	public bool UsesGmodSet { get; private set; }

	/// <summary> Every machine: the GMod hold type the body wears (ar2, smg1, shotgun, pistol, rpg, revolver, passive, normal), or null. </summary>
	public string GmodSet { get; private set; }

	/// <summary>
	/// Puts the class's playermodel on the body (every machine does this from the synced ClassId) and scales the owner's
	/// movement speed. If the model file is missing, falls back to a dressed Citizen so the game still runs.
	/// </summary>
	void ApplyClassModel()
	{
		// keyed on the battalion and the animation-set switch too, so markings and swrp_clone_anims update without redeploying
		// a weapon out or lying prone: the Squad movement set (the "_squad" model); empty hands keep the original walk and run
		// (relaxed keeps the original unarmed walk too)
		var squad = GetComponent<SwrpBlaster>() is { Drawn: true } && !( WeaponRelaxed && WeaponSafety ) || Prone || GetComponent<SwrpProne>() is { Down: true };
		// relaxed with a rifle: Squad's patrol walk, at its own easy pace (any faster and the graph plays the clip sped up,
		// which read as a jog; Angelo, 2026-10-19)
		var patrol = ClassDefinition.Custom && !squad && WeaponRelaxed && WeaponSafety && GetComponent<SwrpBlaster>() is { Drawn: true, Stats.HoldType: 2 };
		// a battalion's Commander wears its named commander (SwrpCommanders: Wolffe for the 104th, Rex for the 501st)
		var commander = SwrpCommanders.ModelFor( this );
		var gun = GetComponent<SwrpBlaster>() is { Drawn: true } b ? b : null;
		// Garry's Mod's sets by TFA's own hold-type rules (SwrpEmotes.TfaHold): sprint/safety, crouch, sights, reload
		var gmod = ClassDefinition.Custom ? null : gun is null ? "normal"
			// sprinting keeps the gun raised (Angelo, 2026-10-21): TFA's lowered sprint swapped the set on every sprint start and
			// stop, and a swapped-in set's first frame flashed its idle stance; only safety (Shift+E+R) lowers it now
			: SwrpEmotes.TfaHold( gun.WeaponId, gun.Stats?.HoldType ?? 2, WeaponSafety, Controller.IsValid() && Controller.IsDucking, gun.Sighted, gun.Reloading );
		var key = $"{ClassId}:{FactionId}:{ClassDefinition.Custom}:{EventCharacter}:{squad}:{patrol}:{commander}:{gmod}:{ClassDefinition.GmodAnims}";
		if ( !Body.IsValid() || string.IsNullOrEmpty( ClassId ) || _appliedClass == key ) return;
		_appliedClass = key;

		var dresser = GetComponent<Dresser>();
		WearsCommanderModel = false;
		if ( EventDef is { } ev )
		{
			// a Game Master's event character: the droid's (placeholder) model, colour and size
			Body.Model = Model.Load( string.IsNullOrEmpty( ev.ModelPath ) ? "models/citizen/citizen.vmdl" : ev.ModelPath );
			Body.Tint = ev.Tint;
			if ( dresser.IsValid() ) dresser.Enabled = false;
			return;
		}

		var commanderModel = commander is not null ? SwrpCommanders.Load( commander, squad, gmod ) : null;
		WearsCommanderModel = commanderModel is not null;
		var gmodModel = commanderModel is null && gmod is not null ? Class.LoadGmod( gmod ) : null;
		UsesGmodSet = gmodModel is not null || commanderModel?.ResourcePath?.Contains( "_gmod_" ) == true;
		GmodSet = UsesGmodSet ? gmod : null;
		var model = commanderModel ?? gmodModel ?? Class.LoadModel( squad, patrol );

		if ( model is not null )
		{
			var swapped = Body.Model != model;
			if ( swapped && UsesGmodSet ) SwrpEmotes.FadeFromHere( Body );      // GMod set to GMod set: blend, not snap
			Body.Model = model;
			// the new model's animgraph starts empty: give it this frame's movement now, or its first frame is GMod's idle stance
			// mid-run (the legs flicked to it on every sprint or safety toggle; kick.py, 2026-10-20)
			if ( swapped && UsesGmodSet && Controller.IsValid() ) { Controller.UpdateAnimation( Body ); SwrpEmotes.GmodPace( Body ); }
			Body.Tint = Color.White;      // battalion markings are material groups (SwrpOutfit.Apply)
			if ( dresser.IsValid() ) dresser.Enabled = false;
		}
		else
		{
			Log.Warning( $"[SWRP] Model for class '{ClassId}' is missing ({Class.ModelPath}), using the Citizen fallback." );
			if ( dresser.IsValid() ) dresser.Enabled = true;
		}

		if ( !IsProxy && Controller.IsValid() )
		{
			if ( _baseWalk == 0 ) { _baseWalk = Controller.WalkSpeed; _baseRun = Controller.RunSpeed; }
			if ( _baseDucked == 0 ) _baseDucked = Controller.DuckedSpeed;
			_speedVersion = SwrpSpeeds.Version;
			Controller.WalkSpeed = ( UsesGmodSet ? GmodSetWalk : !ClassDefinition.Custom ? GModWalk : patrol ? PatrolPace : WalkPace ) * Class.SpeedScale;
			Controller.RunSpeed = ( UsesGmodSet ? GmodSetRun : !ClassDefinition.Custom ? GModRun : _baseRun ) * Class.SpeedScale;
			Controller.DuckedSpeed = UsesGmodSet ? GmodSetDucked * Class.SpeedScale : _baseDucked;
			ClassRunSpeed = Controller.RunSpeed;
			ClassWalkSpeed = Controller.WalkSpeed;
		}
	}

	/// <summary> Ask the host to use the item in an inventory slot. </summary>
	[Rpc.Host]
	public void UseItem( int slot )
	{
		if ( !IsCallerOwner() ) return;

		var record = CharacterStore.Find( OwnerSteamId, CharacterId );
		if ( record is null || slot < 0 || slot >= record.Inventory.Count ) return;

		var def = record.Inventory[slot].Definition;
		if ( def is null ) return;

		if ( def.Id == Swrp.Jedi.JediCombat.SaberItem )
		{
			GetComponent<Swrp.Jedi.JediCombat>()?.HostToggleSaber();
			return;
		}

		if ( def.Id == SwrpGrenade.ItemId )
		{
			HostThrowGrenade( Rpc.Caller );
			return;
		}

		if ( !def.Consumable )
		{
			SwrpNet.NotifyConnection( Rpc.Caller, $"{def.Name}: {def.Description}", false );
			return;
		}

		var vitals = GetComponent<SwrpVitals>();
		var caller = Rpc.Caller;
		if ( vitals is { IsDead: true } or { IsDowned: true } )
		{
			SwrpNet.NotifyConnection( caller, "You can't do that while you're down. Call a medic (E).", true );
			return;
		}

		if ( def.Category == ItemCategory.Medical && vitals is not null && vitals.Health >= SwrpVitals.MaxHealth && !( def.StopsBleed && vitals.Bleeding ) )
		{
			SwrpNet.NotifyConnection( caller, "You're already at full health.", false );
			return;
		}

		var itemId = def.Id;
		void Apply()
		{
			// The slot may have moved while applying, so look the item up again.
			var rec = CharacterStore.Find( OwnerSteamId, CharacterId );
			var at = rec?.Inventory.FindIndex( x => x.ItemId == itemId ) ?? -1;
			if ( at < 0 ) return;

			rec.TakeFromSlot( at );
			CharacterStore.Save( OwnerSteamId );
			InventoryJson = Json.Serialize( rec.Inventory );
			if ( def.Heals > 0 ) vitals?.Heal( def.Heals );
			if ( def.HealOverTime > 0 ) vitals?.HealOverTime( def.HealOverTime, def.HealSeconds );
			if ( def.StopsBleed ) vitals?.StopBleeding();
			SwrpNet.NotifyConnection( caller, def.UseMessage ?? $"Used {def.Name}.", false );
		}

		if ( def.UseTime > 0 && vitals is not null )
		{
			if ( !vitals.StartTreatment( $"Applying {def.Name}", def.UseTime, Apply ) )
				SwrpNet.NotifyConnection( caller, "You're already treating yourself.", true );
			return;
		}

		Apply();
	}

	/// <summary> Ask the host to drop one of the items in a slot (it lands in front of you; E picks it up again). </summary>
	[Rpc.Host]
	public void DropItem( int slot )
	{
		if ( IsCallerOwner() ) HostDrop( Rpc.Caller, slot, 1 );
	}

	/// <summary> Ask the host to drop the whole stack in a slot as one pile. </summary>
	[Rpc.Host]
	public void DropStack( int slot )
	{
		if ( IsCallerOwner() ) HostDrop( Rpc.Caller, slot, int.MaxValue );
	}

	void HostDrop( Connection caller, int slot, int count )
	{
		var record = CharacterStore.Find( OwnerSteamId, CharacterId );
		if ( record is null || slot < 0 || slot >= record.Inventory.Count ) return;

		var stack = record.Inventory[slot];
		var itemId = stack.ItemId;
		var def = stack.Definition;
		var name = def?.Name ?? itemId ?? "item";
		count = Math.Clamp( count, 1, Math.Max( 1, stack.Count ) );
		record.TakeFromSlot( slot, count );

		// a lit lightsaber goes out before it leaves the hand
		if ( itemId == Swrp.Jedi.JediCombat.SaberItem && GetComponent<Swrp.Jedi.JediCombat>() is { SaberOn: true } jedi && !record.Inventory.Any( i => i.ItemId == itemId ) )
			jedi.HostToggleSaber();

		// a weapon takes its magazine with it once the last one is gone
		var clip = -1;
		if ( def?.Weapon is not null && !record.Inventory.Any( i => i.ItemId == itemId ) && record.WeaponClips.Remove( itemId, out var c ) ) clip = c;
		CharacterStore.Save( OwnerSteamId );
		InventoryJson = Json.Serialize( record.Inventory );

		// an item the game no longer knows is just thrown away
		if ( def is null )
		{
			SwrpNet.NotifyConnection( caller, $"Discarded {name}.", false );
			return;
		}

		// it lands in front of you instead of vanishing; E picks it up again
		var eye = Controller.IsValid() ? Controller.EyePosition : WorldPosition + Vector3.Up * 60f;
		var fwd = Controller.IsValid() ? Controller.EyeAngles.WithPitch( 0 ).Forward : WorldRotation.Forward;
		var vel = ( Controller.IsValid() && Controller.Body.IsValid() ? Controller.Body.Velocity : Vector3.Zero ) + fwd * 160f + Vector3.Up * 60f;
		SwrpWorldItem.Spawn( Scene, itemId, count, eye + fwd * 26f + Vector3.Down * 14f, Rotation.LookAt( fwd ) * Rotation.FromRoll( 70f ), vel, clip );
		SwrpNet.NotifyConnection( caller, $"Dropped {count}x {name}.", false );
	}

	/// <summary> Host only. Re-reads the stored record after an admin change (rank, faction, items). </summary>
	public void RefreshFromStore()
	{
		var record = CharacterStore.Find( OwnerSteamId, CharacterId );
		if ( record is not null ) ApplyRecord( record );
	}

	bool IsCallerOwner()
	{
		var caller = Rpc.Caller;
		return caller is not null && caller.Id == Network.OwnerId;
	}

	// ----- thermal detonators -----

	static bool HasGrenadeKey => Input.ActionNames.Contains( "Grenade" );
	RealTimeSince _sinceGrenade = 10;

	/// <summary> Owner: throw a thermal detonator where you're looking (the Grenade key, or Throw in the inventory). </summary>
	[Rpc.Host]
	public void ThrowGrenade()
	{
		if ( !IsCallerOwner() ) return;
		HostThrowGrenade( Rpc.Caller );
	}

	/// <summary> Host: take a detonator from the inventory and throw it from the trooper's hand along their view. </summary>
	void HostThrowGrenade( Connection caller )
	{
		if ( CharacterId is null || Jailed || Frozen || InPod || InVehicle || !Controller.IsValid() || _sinceGrenade < 1.2f ) return;
		if ( GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } )
		{
			SwrpNet.NotifyConnection( caller, "You can't throw anything while you're down.", true );
			return;
		}
		var record = CharacterStore.Find( OwnerSteamId, CharacterId );
		var slot = record?.Inventory.FindIndex( x => x.ItemId == SwrpGrenade.ItemId ) ?? -1;
		if ( slot < 0 )
		{
			SwrpNet.NotifyConnection( caller, "You have no thermal detonators.", true );
			return;
		}
		record.TakeFromSlot( slot );
		CharacterStore.Save( OwnerSteamId );
		InventoryJson = Json.Serialize( record.Inventory );
		_sinceGrenade = 0;

		var s = BodyScale;
		var eye = Controller.EyePosition;
		var look = Controller.EyeAngles.ToRotation();
		var from = eye + look.Forward * 22f * s + look.Right * 7f * s + Vector3.Down * 6f * s;
		var velocity = look.Forward * 760f + Vector3.Up * 190f + Controller.Velocity * 0.5f;
		SwrpGrenade.Throw( Scene, from, velocity, Allegiance, ShortName, this );
		ThrowMotion();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ThrowMotion()
	{
		if ( Body.IsValid() ) Body.Set( "b_attack", true );
	}
}

/// <summary>
/// Client-side UI toggles shared between panels.
/// </summary>
public static class SwrpUiState
{
	static bool _inventoryOpen, _characterMenuOpen;

	public static bool InventoryOpen
	{
		get => _inventoryOpen;
		set
		{
			if ( _inventoryOpen != value && SwrpPlayer.Local is not null )
				SwrpAudio.Ui( value ? SwrpAudio.Open : SwrpAudio.Close );
			_inventoryOpen = value;
		}
	}

	public static bool CharacterMenuOpen
	{
		get => _characterMenuOpen;
		set
		{
			if ( _characterMenuOpen != value && SwrpPlayer.Local is not null )
				SwrpAudio.Ui( value ? SwrpAudio.Open : SwrpAudio.Close );
			_characterMenuOpen = value;
		}
	}

	static bool _chatOpen;

	public static bool ChatOpen
	{
		get => _chatOpen;
		set
		{
			if ( _chatOpen && !value ) ChatClosedFor = 0;
			if ( _chatOpen != value && SwrpPlayer.Local is not null )
				SwrpAudio.Ui( value ? SwrpAudio.ChatOpen : SwrpAudio.Close );
			_chatOpen = value;
		}
	}

	/// <summary> Enter both sends a message and opens chat, so ignore the open key for a moment after closing. </summary>
	public static RealTimeSince ChatClosedFor { get; private set; } = 10;

	/// <summary> Non-zero while staff are watching that player. Controls stay locked and the camera follows them. </summary>
	public static ulong SpectateSteamId { get; set; }

	/// <summary> The staff panel (StaffMenu key or /staff). </summary>
	public static bool StaffOpen { get; set; }

	/// <summary> The turbolift deck selector (E on a lift panel). </summary>
	public static SwrpTurbolift LiftMenu { get; set; }

	/// <summary> The bridge console's ship systems menu (alert level, announcements). </summary>
	public static bool ShipConsoleOpen { get; set; }

	/// <summary> The Game Master event menu (GmMenu key or /gm). </summary>
	public static bool GmOpen { get; set; }

	/// <summary> The battalion roster and officer tools (BattalionMenu key or /battalion). </summary>
	public static bool BattalionOpen { get; set; }

	/// <summary> The staff spawn menu (hold Q). </summary>
	public static bool BuildOpen { get; set; }
	/// <summary> The emote picker (hold right mouse with empty hands). </summary>
	public static bool EmoteOpen { get; set; }

	/// <summary> The brig shield control menu (E at the console ring in the brig's first room). </summary>
	public static SwrpBrigConsole BrigMenu { get; set; }

	/// <summary> The DJ booth menu (Code/Club). SwrpClub sets this, so this file builds with or without the club code. </summary>
	public static bool ClubMenuOpen { get; set; }

	public static bool AnyOpen => ClubMenuOpen || SquadMenu.Open || InventoryOpen || CharacterMenuOpen || ChatOpen || StaffOpen || LiftMenu is not null || ShipConsoleOpen || GmOpen || BattalionOpen || BrigMenu.IsValid() || BuildOpen || EmoteOpen || Swrp.Jedi.JediUi.SkillsOpen || EscapeMenu.Open || SpectateSteamId != 0 || SwrpPlayer.Local is null;
}
