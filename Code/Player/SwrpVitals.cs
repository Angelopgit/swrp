using System;

namespace Swrp;

/// <summary>
/// Health, armour, stamina and the injury loop for a spawned player. The host owns everything health related (FromHost
/// syncs) and is the only one that applies damage or healing; stamina is simulated by the owning client.
///
/// At zero health a trooper goes down instead of dying: they can crawl, call a medic (E) or give up (hold K), and bleed out
/// when the timer (swrp_bleedout) ends. Anyone can revive a downed trooper by holding E on them; a medic (the Medic
/// class) is much faster and restores more, and with the defibrillator in hand brings them back at full health; a medic's
/// medkit in hand treats a trooper who is up (hold E: full health, bleeding stopped). Heavy hits start a bleed that bacta or a medkit stops. When a trooper dies a
/// ragdoll is left behind and the host redeploys the character at the nearest medbay after swrp_respawn_time seconds.
/// </summary>
public sealed class SwrpVitals : Component
{
	public const float MaxHealth = 100f;
	public const float MaxStamina = 100f;

	[ConVar( "swrp_bleedout", Help = "Seconds a downed trooper has before bleeding out (host)", Saved = true )]
	public static float BleedOutSeconds { get; set; } = 90f;

	[ConVar( "swrp_respawn_time", Help = "Seconds between death and redeploying (host)", Saved = true )]
	public static float RespawnSeconds { get; set; } = 8f;

	/// <summary> Kept for older callers; the real delay is <see cref="RespawnSeconds"/>. </summary>
	public static float RespawnDelay => RespawnSeconds;

	public const int DownNone = 0, DownStunned = 1, DownWounded = 2;

	/// <summary> Fraction of incoming damage the armour soaks while it lasts. </summary>
	const float ArmorAbsorb = 0.7f;
	const float SprintDrain = 18f;
	const float JumpCost = 10f;
	const float StaminaRegen = 22f;
	const float RegenDelay = 1.2f;
	/// <summary> Once drained, sprinting stays locked until stamina is back above this. </summary>
	const float ExhaustRecover = 30f;
	const float FallDamageSpeed = 650f;

	/// <summary> A single hit that takes this much health (after armour) starts a bleed. </summary>
	const float BleedThreshold = 18f;
	const float BleedPerSecond = 1.1f;
	/// <summary> Damage that finishes a downed trooper off. </summary>
	const float FinishThreshold = 25f;
	const float CrawlSpeed = 34f;
	const float ReviveRange = 96f;

	[Sync( SyncFlags.FromHost )] public float Health { get; set; } = MaxHealth;
	[Sync( SyncFlags.FromHost )] public float Armor { get; set; }
	[Sync( SyncFlags.FromHost )] public float MaxArmor { get; set; }
	[Sync( SyncFlags.FromHost )] public bool IsDead { get; set; }

	/// <summary> Knocked down but alive: stunned (gets up by itself) or wounded (bleeding out). </summary>
	[Sync( SyncFlags.FromHost )] public bool IsDowned { get; set; }
	[Sync( SyncFlags.FromHost )] public int DownReason { get; set; }

	/// <summary> Seconds the current knock-down lasts (stun) or until bleeding out (wounded), for the HUD countdown. </summary>
	[Sync( SyncFlags.FromHost )] public float DownedSeconds { get; set; }

	[Sync( SyncFlags.FromHost )] public bool Bleeding { get; set; }
	[Sync( SyncFlags.FromHost )] public bool MedicRequested { get; set; }

	/// <summary> 0..1 while someone revives this trooper. </summary>
	[Sync( SyncFlags.FromHost )] public float ReviveProgress { get; set; }

	/// <summary> 0..1 while this trooper revives someone else. </summary>
	[Sync( SyncFlags.FromHost )] public float RevivingProgress { get; set; }
	[Sync( SyncFlags.FromHost )] public string RevivingName { get; set; }
	/// <summary> The medkit on a trooper who is up (treating, not reviving). </summary>
	[Sync( SyncFlags.FromHost )] public bool RevivingTreat { get; set; }

	/// <summary> Up but hurt or bleeding: what a medic's medkit treats. </summary>
	public bool NeedsCare => !IsDead && !IsDowned && ( Health < MaxHealth || Bleeding );

	/// <summary> A timed treatment in progress (bacta, medkit, bacta station), for the HUD bar. </summary>
	[Sync( SyncFlags.FromHost )] public string Treating { get; set; }
	[Sync( SyncFlags.FromHost )] public float TreatProgress { get; set; }

	/// <summary> Bumped by the host on every hit, so clients can flash the screen without a separate RPC. </summary>
	[Sync( SyncFlags.FromHost )] public int HitCount { get; set; }

	[Sync] public float Stamina { get; set; } = MaxStamina;

	public bool IsWounded => IsDowned && DownReason == DownWounded;
	public bool IsStunned => IsDowned && DownReason == DownStunned;

	/// <summary> Owner only: sprint is locked out after running dry. </summary>
	public bool Exhausted { get; private set; }

	/// <summary> Seconds until redeploy, for the HUD. Only meaningful while <see cref="IsDead"/>. </summary>
	public float RespawnIn => Math.Max( 0f, _respawnAt );

	SwrpPlayer _player;
	SwrpPlayer Player => _player ??= GetComponent<SwrpPlayer>();

	/// <summary> Training dummies have no SwrpPlayer; their body is set here (host) or found as a child. </summary>
	public SkinnedModelRenderer DummyBody { get; set; }
	SkinnedModelRenderer Body => Player?.Body ?? ( DummyBody ??= GetComponentInChildren<SkinnedModelRenderer>() );
	PlayerController Controller => Player?.Controller;

	RealTimeSince _sinceStaminaUse = 10;
	RealTimeUntil _respawnAt;
	RealTimeUntil _downedUntil;
	bool _respawnQueued;
	bool _wasOnGround = true;
	float _fallSpeed;

	// Host: what last hurt us (for the kill feed, /examine on the corpse, and finishing off).
	string _lastAttacker, _lastWeapon;
	bool _lastHead;
	RealTimeSince _sinceDamaged = 100;
	public float SinceDamaged => _sinceDamaged;
	Vector3 _deathPosition;

	// Host: healing over time and timed treatments.
	float _hotLeft, _hotRate;
	RealTimeUntil _treatDone;
	float _treatLength;
	Action _treatFinish;
	Vector3 _treatStart;

	// Host: reviving someone else.
	SwrpVitals _reviveTarget;
	RealTimeSince _reviveStart;
	float _reviveLength;
	Vector3 _reviveFrom;
	string _reviveTool;

	RealTimeSince _sinceMedicCall = 100;

	/// <summary> Host only, before NetworkSpawn: full health and the class's armour. </summary>
	public void ResetFor( ClassDefinition cls )
	{
		IsDowned = false;
		DownReason = DownNone;
		Bleeding = false;
		MedicRequested = false;
		ReviveProgress = 0;
		Treating = null;
		Health = MaxHealth;
		MaxArmor = cls?.MaxArmor ?? 0f;
		Armor = MaxArmor;
		IsDead = false;
		Stamina = MaxStamina;
	}

	// ----- Damage (host) -----

	/// <summary> Host only. Armour soaks most of the hit until it runs out. </summary>
	public void TakeDamage( float amount ) => TakeDamage( amount, null, null, null, false );

	/// <summary> Host only. Returns true if this hit took the trooper down (or finished them). </summary>
	public bool TakeDamage( float amount, SwrpPlayer attacker, Vector3? from, string weapon, bool head, string attackerName = null )
	{
		if ( !Networking.IsHost || IsDead || amount <= 0 ) return false;
		if ( Player is { GodMode: true } ) return false;

		_sinceDamaged = 0;
		_lastAttacker = attacker.IsValid() ? attacker.ShortName : attackerName;
		_lastWeapon = weapon;
		_lastHead = head;
		HitCount++;
		NotifyHurt( from ?? WorldPosition, false, amount );
		if ( from is { } src ) Flinch( WorldPosition - src, Math.Clamp( amount / 45f, 0.3f, 1f ), head );

		// Massive damage (a staff slay, a detonator at your feet) kills outright; anything solid finishes a downed trooper.
		if ( amount >= 500f || IsWounded && amount >= FinishThreshold )
		{
			Health = 0;
			Die( head ? "headshot" : "killed" );
			return true;
		}

		if ( IsDowned ) return false;

		var soaked = Math.Min( Armor, amount * ArmorAbsorb );
		Armor -= soaked;
		var toHealth = amount - soaked;
		Health = Math.Max( 0f, Health - toHealth );

		if ( Health > 0f )
		{
			if ( toHealth >= BleedThreshold && !Bleeding ) StartBleeding();
			return false;
		}

		GoDown();
		SwrpCombat.AnnounceKill( attacker, Player, weapon, head, GameObject.Name, attackerName );
		return true;
	}

	void StartBleeding()
	{
		Bleeding = true;
		PlayAt( SwrpAudio.Bleed );
	}

	/// <summary> Host only. Stun round: knocked down for a few seconds, no damage. </summary>
	public void Stun( float seconds, Vector3 from )
	{
		if ( !Networking.IsHost || IsDead || IsWounded ) return;
		if ( Player is { GodMode: true } ) return;

		IsDowned = true;
		DownReason = DownStunned;
		DownedSeconds = seconds;
		_downedUntil = seconds;
		HitCount++;
		NotifyHurt( from, true, 0f );
		Flinch( WorldPosition - from, 1f, false );
		GetComponent<SwrpBlaster>()?.ForceHolster();
	}

	/// <summary> Host: health ran out. Down, bleeding out, until revived or the timer ends. </summary>
	void GoDown()
	{
		IsDowned = true;
		DownReason = DownWounded;
		DownedSeconds = BleedOutSeconds;
		_downedUntil = BleedOutSeconds;
		Bleeding = false;
		MedicRequested = false;
		ReviveProgress = 0;
		_hotLeft = 0;
		CancelTreatment();
		GetComponent<SwrpBlaster>()?.ForceHolster();
		Log.Info( $"[SWRP] {Player?.FullTitle ?? GameObject.Name} is down" );
	}

	/// <summary> Host: dead for real. The ragdoll stays behind and the character redeploys after a short wait. </summary>
	void Die( string how )
	{
		var cause = how switch
		{
			"bled out" => _lastAttacker is null ? "Bled out." : $"Bled out after being shot by {_lastAttacker}{( _lastWeapon is null ? "" : $" ({_lastWeapon})" )}.",
			"gave up" => "Succumbed to wounds.",
			_ => _lastAttacker is null ? "Killed." : $"{( _lastHead ? "Shot in the head" : "Shot" )} by {_lastAttacker}{( _lastWeapon is null ? "" : $" with a {_lastWeapon}" )}.",
		};

		IsDowned = false;
		DownReason = DownNone;
		IsDead = true;
		Bleeding = false;
		MedicRequested = false;
		ReviveProgress = 0;
		CancelTreatment();
		_deathPosition = WorldPosition;
		_respawnAt = RespawnSeconds;
		_respawnQueued = true;

		SwrpCorpses.Record( this, Player, cause );
		if ( Player is { } p && !string.IsNullOrEmpty( p.EventCharacter ) ) SwrpGameMaster.EndEventCharacter( p, "died" );
		SpawnCorpse();
		Log.Info( $"[SWRP] {Player?.FullTitle ?? GameObject.Name} died: {cause}" );
	}

	/// <summary>
	/// Host, staff /heal: full health and armour, stamina topped up, bleeding stopped, and a downed (wounded or stunned) trooper
	/// back on their feet. Not the dead: they redeploy. Returns false if there was nothing to heal from.
	/// </summary>
	public bool StaffHeal()
	{
		if ( !Networking.IsHost || IsDead ) return false;
		IsDowned = false;
		DownReason = DownNone;
		DownedSeconds = 0;
		ReviveProgress = 0;
		MedicRequested = false;
		Treating = null;
		Bleeding = false;
		_hotLeft = 0;
		Health = MaxHealth;
		Armor = MaxArmor;
		Stamina = MaxStamina;
		PlayAt( SwrpAudio.ReviveDone );
		return true;
	}

	/// <summary> Host only. </summary>
	public void Heal( float amount )
	{
		if ( !Networking.IsHost || IsDead ) return;
		Health = Math.Min( MaxHealth, Health + amount );
	}

	/// <summary> Host: heal a total amount over a number of seconds (stacks with what's left). </summary>
	public void HealOverTime( float amount, float seconds )
	{
		if ( !Networking.IsHost || IsDead || amount <= 0 ) return;
		_hotLeft += amount;
		_hotRate = Math.Max( _hotRate, amount / Math.Max( 0.5f, seconds ) );
	}

	/// <summary> Host: stop a bleed. </summary>
	public void StopBleeding() => Bleeding = false;

	/// <summary> Host: a timed action (bandaging, injecting) that completes unless the trooper moves away or goes down. </summary>
	public bool StartTreatment( string label, float seconds, Action finish )
	{
		if ( !Networking.IsHost || IsDead || IsDowned ) return false;
		if ( Treating is not null ) return false;
		Treating = label;
		TreatProgress = 0;
		_treatLength = Math.Max( 0.1f, seconds );
		_treatDone = _treatLength;
		_treatFinish = finish;
		_treatStart = WorldPosition;
		return true;
	}

	void CancelTreatment()
	{
		Treating = null;
		TreatProgress = 0;
		_treatFinish = null;
	}

	// ----- Revive (host) -----

	/// <summary> The defibrillator or medkit in a Medic's hands (SwrpHands), else null. Both machines can read it. </summary>
	public string MedicTool => Player?.Class.IsMedic == true && GetComponent<SwrpHands>()?.HeldGear is { } g && ( g == ItemRegistry.Defibrillator || g == ItemRegistry.Medkit ) ? g : null;

	/// <summary>
	/// Host: start reviving a downed trooper (medics are faster and restore more; a medic's defibrillator brings them back at
	/// full health), or, with a medic's medkit in hand, treating one who is up but hurt or bleeding (back to full).
	/// </summary>
	public void BeginRevive( SwrpVitals target )
	{
		if ( !Networking.IsHost || IsDead || IsDowned || !target.IsValid() || target == this ) return;
		var tool = MedicTool;
		var treat = tool == ItemRegistry.Medkit && target.NeedsCare;
		if ( !target.IsWounded && !treat ) return;
		if ( target.WorldPosition.Distance( WorldPosition ) > ReviveRange ) return;
		if ( Scene.GetAllComponents<SwrpVitals>().Any( v => v != this && v._reviveTarget == target ) ) return;

		var medic = Player?.Class.IsMedic == true;
		_reviveTarget = target;
		_reviveTool = tool;
		RevivingTreat = treat;
		_reviveStart = 0;
		_reviveLength = treat ? 4f : tool == ItemRegistry.Defibrillator ? 3f : medic ? 3.5f : 9f;
		_reviveFrom = WorldPosition;
		RevivingName = target.Player is { } tp ? $"{tp.Rank.Abbreviation} {tp.CharacterName}" : target.GameObject.Name;
		GetComponent<SwrpBlaster>()?.ForceHolster();
		PlayAt( SwrpAudio.ReviveStart );
	}

	public void CancelRevive()
	{
		if ( _reviveTarget.IsValid() ) _reviveTarget.ReviveProgress = 0;
		_reviveTarget = null;
		RevivingProgress = 0;
		RevivingName = null;
		RevivingTreat = false;
	}

	void UpdateRevive()
	{
		if ( !_reviveTarget.IsValid() ) { if ( RevivingProgress > 0 ) CancelRevive(); return; }

		var t = _reviveTarget;
		var interrupted = IsDead || IsDowned || ( RevivingTreat ? t.IsDead || t.IsDowned : !t.IsWounded )
			|| _reviveTool is not null && MedicTool != _reviveTool       // put the tool away
			|| WorldPosition.Distance( _reviveFrom ) > 48f
			|| t.WorldPosition.Distance( WorldPosition ) > ReviveRange
			|| _sinceDamaged < _reviveStart;           // took a hit since starting

		if ( interrupted )
		{
			if ( Network.Owner is { } o ) SwrpNet.NotifyConnection( o, "Revive interrupted.", true );
			CancelRevive();
			return;
		}

		var p = Math.Clamp( _reviveStart / _reviveLength, 0f, 1f );
		RevivingProgress = p;
		t.ReviveProgress = p;
		if ( p < 1f ) return;

		var medic = Player?.Class.IsMedic == true;
		if ( RevivingTreat )
		{
			t.Health = MaxHealth;
			t.Bleeding = false;
			t.PlayAt( SwrpAudio.ReviveDone );
			if ( t.Player is not null && t.Network.Owner is { } to ) SwrpNet.NotifyConnection( to, "A medic treated your wounds. Full health.", false );
			if ( Network.Owner is { } o2 ) SwrpNet.NotifyConnection( o2, $"You treated {RevivingName}: full health, bleeding stopped.", false );
			CancelRevive();
			return;
		}
		t.Revived( _reviveTool == ItemRegistry.Defibrillator ? MaxHealth : medic ? 50f : 20f, medic );
		SwrpCareer.OnRevive( Player );      // battalion stats
		if ( Network.Owner is { } owner ) SwrpNet.NotifyConnection( owner, $"You got {RevivingName} back on their feet.", false );
		CancelRevive();
	}

	/// <summary> Host: back on your feet. A medic also closes the wounds; anyone else leaves you bleeding. </summary>
	void Revived( float health, bool byMedic )
	{
		IsDowned = false;
		DownReason = DownNone;
		ReviveProgress = 0;
		MedicRequested = false;
		Health = health;
		Bleeding = !byMedic;
		PlayAt( SwrpAudio.ReviveDone );
		if ( Player is not null && Network.Owner is { } o ) SwrpNet.NotifyConnection( o, byMedic ? "A medic patched you up." : "You're back up, but still bleeding. Use bacta.", !byMedic );
	}

	// ----- Owner requests -----

	[Rpc.Host]
	public void RequestRevive( SwrpVitals target )
	{
		if ( Rpc.Caller?.Id != Network.OwnerId ) return;
		BeginRevive( target );
	}

	[Rpc.Host]
	public void RequestCancelRevive()
	{
		if ( Rpc.Caller?.Id != Network.OwnerId ) return;
		CancelRevive();
	}

	/// <summary> Owner: same as pressing E while downed. </summary>
	public void CallMedic() => RequestMedic();

	/// <summary> Owner: same as holding K while downed. </summary>
	public void GiveUp() => RequestGiveUp();

	[Rpc.Host]
	void RequestMedic()
	{
		if ( Rpc.Caller?.Id != Network.OwnerId || !IsWounded || Player is null ) return;
		if ( _sinceMedicCall < 15f )
		{
			SwrpNet.NotifyConnection( Rpc.Caller, "You already called for a medic. Hang on.", true );
			return;
		}

		_sinceMedicCall = 0;
		MedicRequested = true;
		var name = $"{Player.Rank.Abbreviation} {Player.CharacterName}";
		var medics = Scene.GetAllComponents<SwrpPlayer>().Where( p => p.Class.IsMedic && p != Player ).Select( p => p.Network.Owner );
		var squad = SwrpRoleplay.Current?.SquadOf( Player.OwnerSteamId );
		var squadmates = squad is null ? Enumerable.Empty<Connection>()
			: Scene.GetAllComponents<SwrpPlayer>().Where( p => squad.Members.Contains( p.OwnerSteamId ) ).Select( p => p.Network.Owner );
		SwrpChat.SendTo( medics.Concat( squadmates ).Append( Rpc.Caller ), ChatKind.Radio, $"[MEDIC] {name} is down and needs a medic! (marked on medics' HUD)" );
	}

	[Rpc.Host]
	void RequestGiveUp()
	{
		if ( Rpc.Caller?.Id != Network.OwnerId || !IsWounded ) return;
		Die( "gave up" );
	}

	// ----- Feedback -----

	// Last hit on this machine's own trooper, for the HUD's direction marker.
	public static Vector3 LastHurtFrom { get; private set; }
	public static RealTimeSince SinceHurt { get; private set; } = 10;
	public static bool LastHurtWasStun { get; private set; }

	[Rpc.Owner( NetFlags.HostOnly )]
	void NotifyHurt( Vector3 from, bool stun, float amount )
	{
		// Training dummies belong to the host; their hits are not the host's own.
		if ( Player is null ) return;

		// the camera takes the hit too; a heavy one leaves your ears ringing
		SwrpShake.Add( stun ? 0.35f : Math.Clamp( amount / 70f, 0.12f, 0.6f ) );
		if ( !stun && amount >= 30f ) SwrpShake.Ring( Math.Clamp( amount / 80f, 0.35f, 1f ) );

		LastHurtFrom = from;
		SinceHurt = 0;
		LastHurtWasStun = stun;
		Feel.FeelHits.Hurt( from, amount, stun );     // flinch, edge flash, heavy-hit blur (Code/Feel)
		SwrpAudio.Ui( stun ? SwrpAudio.BlasterStun : SwrpAudio.Hurt );
	}

	/// <summary> Everyone sees the body flinch away from the shot (Code/Fx/HitReact). </summary>
	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Unreliable )]
	void Flinch( Vector3 dir, float strength, bool head ) => HitReact.Play( Body, dir, strength, head );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayAt( string sound ) => Sound.Play( sound, WorldPosition + Vector3.Up * 40f );

	/// <summary> Every machine leaves a ragdoll of the body where the trooper died. </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnCorpse()
	{
		if ( Body is { } body ) SwrpCorpses.SpawnRagdoll( Scene, body );
	}

	// ----- Update -----

	RealTimeSince _sinceHeartbeat;
	bool _wasBleeding;

	protected override void OnUpdate()
	{
		// Every machine hides the body of a dead trooper (the ragdoll takes its place).
		if ( Body is { } body && body.Enabled == IsDead )
			body.Enabled = !IsDead;

		// Downed troopers fall and lie wounded on the ground, and get back up when revived (SwrpDownedPose; it only touches
		// the body while down or rising, so the controller still turns it to face where the trooper walks).
		if ( Body is { } downBody ) SwrpDownedPose.Apply( downBody, IsDowned && !IsDead, IsStunned );

		if ( Networking.IsHost ) HostUpdate();
		if ( !IsProxy && Player is not null ) OwnerUpdate();
	}

	void HostUpdate()
	{
		var dt = Time.Delta;

		if ( IsDowned && _downedUntil <= 0 )
		{
			if ( IsStunned ) { IsDowned = false; DownReason = DownNone; }
			else if ( IsWounded ) Die( "bled out" );
		}

		if ( !IsDead && !IsDowned )
		{
			if ( Bleeding )
			{
				Health -= BleedPerSecond * dt;
				if ( Health <= 0f )
				{
					Health = 0;
					GoDown();
				}
			}

			if ( _hotLeft > 0f )
			{
				var h = Math.Min( _hotLeft, _hotRate * dt );
				_hotLeft -= h;
				Health = Math.Min( MaxHealth, Health + h );
				if ( _hotLeft <= 0f ) _hotRate = 0;
			}

			if ( Treating is not null )
			{
				if ( WorldPosition.Distance( _treatStart ) > 64f )
				{
					if ( Network.Owner is { } o ) SwrpNet.NotifyConnection( o, $"{Treating} interrupted.", true );
					CancelTreatment();
				}
				else
				{
					TreatProgress = Math.Clamp( 1f - _treatDone / _treatLength, 0f, 1f );
					if ( _treatDone <= 0 )
					{
						var finish = _treatFinish;
						CancelTreatment();
						finish?.Invoke();
					}
				}
			}
		}

		UpdateRevive();
		UpdateRespawn();
	}

	void OwnerUpdate()
	{
		UpdateStamina();

		// Downed controls: E calls a medic, hold K to give up.
		if ( IsWounded && !SwrpUiState.AnyOpen )
		{
			if ( Input.Pressed( "Use" ) ) RequestMedic();
			if ( Input.Down( "GiveUp" ) ) { if ( _giveUpHeld > 2f ) { _giveUpHeld = 0; RequestGiveUp(); } }
			else _giveUpHeld = 0;
		}
		else _giveUpHeld = 0;

		// Heartbeat when badly hurt or downed; a wet sting when a bleed starts.
		var critical = !IsDead && ( IsWounded || Health <= 30f );
		if ( critical && _sinceHeartbeat > ( Health <= 15f || IsWounded ? 0.65f : 0.9f ) )
		{
			_sinceHeartbeat = 0;
			SwrpAudio.Ui( SwrpAudio.Heartbeat );
		}

		if ( Bleeding && !_wasBleeding ) SwrpAudio.Ui( SwrpAudio.Bleed );
		_wasBleeding = Bleeding;
	}

	RealTimeSince _giveUpHeld;
	public float GiveUpHeld => Input.Down( "GiveUp" ) ? Math.Min( 2f, _giveUpHeld ) : 0f;

	protected override void OnPreRender()
	{
		// A downed trooper sees the world from the floor.
		if ( IsProxy || Player is null || !IsDowned || IsDead ) return;
		var cam = Scene.Camera;
		if ( !cam.IsValid() || SwrpUiState.SpectateSteamId != 0 ) return;
		cam.WorldPosition += Vector3.Down * 46f;
		cam.WorldRotation *= Rotation.FromRoll( 14f );
	}

	void UpdateRespawn()
	{
		if ( !_respawnQueued || _respawnAt > 0 ) return;
		_respawnQueued = false;

		// Training dummies just stand back up.
		if ( Player is null )
		{
			ResetFor( ClassRegistry.Default );
			return;
		}

		var record = CharacterStore.Find( Player.OwnerSteamId, Player.CharacterId );
		if ( record is not null && Network.Owner is { } owner )
			SwrpGameManager.Current?.SpawnCharacter( owner, record, near: _deathPosition );
	}

	void UpdateStamina()
	{
		var controller = Controller;
		if ( !controller.IsValid() || IsDead ) return;

		// Downed: crawl only, no sprint or jump; held still while someone revives you (crawling off breaks the revive).
		if ( IsDowned )
		{
			var crawl = ReviveProgress > 0f ? 0f : CrawlSpeed;
			controller.WalkSpeed = crawl;
			controller.RunSpeed = crawl;
			if ( _baseJump < 0 ) _baseJump = controller.JumpSpeed;
			controller.JumpSpeed = 0;
			return;
		}

		// Prone (SwrpProne): a slow crawl, no sprint or jump (Jump gets you up instead)
		if ( Player.Prone )
		{
			controller.WalkSpeed = controller.RunSpeed = SwrpProne.CrawlSpeed * Player.BodyScale;
			if ( _baseJump < 0 ) _baseJump = controller.JumpSpeed;
			controller.JumpSpeed = 0;
			return;
		}

		if ( _baseJump >= 0 )
		{
			controller.JumpSpeed = _baseJump;
			_baseJump = -1;
			if ( Player.ClassWalkSpeed > 0 ) controller.WalkSpeed = Player.ClassWalkSpeed;
		}

		// the blaster in your hands sets how fast you move (its Mobility on the weapon base)
		var mobility = ( Player.GetComponent<SwrpBlaster>()?.Stats?.Mobility ?? 1f ) * Swrp.Jedi.JediCombat.MoveScale( Player );      // Force Speed, a Jedi's guard
		if ( Player.ClassWalkSpeed > 0 ) controller.WalkSpeed = Player.ClassWalkSpeed * mobility;

		var dt = Time.Delta;
		var canAct = !SwrpUiState.AnyOpen;
		var flatSpeed = controller.Velocity.WithZ( 0 ).Length;

		var sprinting = canAct && !Exhausted && Input.Down( "Run" ) && controller.IsOnGround
			&& flatSpeed > controller.WalkSpeed * 1.05f;

		if ( sprinting )
		{
			Stamina -= SprintDrain * dt;
			_sinceStaminaUse = 0;
		}

		if ( canAct && controller.IsOnGround && Input.Pressed( "Jump" ) )
		{
			Stamina -= JumpCost;
			_sinceStaminaUse = 0;
		}

		if ( _sinceStaminaUse > RegenDelay )
			Stamina += StaminaRegen * dt;

		Stamina = Math.Clamp( Stamina, 0f, MaxStamina );

		if ( Stamina <= 0f ) Exhausted = true;
		else if ( Exhausted && Stamina >= ExhaustRecover ) Exhausted = false;

		// Out of breath: running is capped to walking pace until stamina recovers.
		if ( Player.ClassRunSpeed > 0 )
			controller.RunSpeed = Exhausted ? controller.WalkSpeed : Player.ClassRunSpeed * mobility;

		// Fall damage is measured where the movement is simulated (the owner) and reported to the host.
		if ( !controller.IsOnGround ) _fallSpeed = Math.Min( _fallSpeed, controller.Velocity.z );
		else if ( !_wasOnGround )
		{
			if ( -_fallSpeed > FallDamageSpeed ) ReportFall( -_fallSpeed );
			_fallSpeed = 0;
		}
		_wasOnGround = controller.IsOnGround;
	}

	float _baseJump = -1;

	[Rpc.Host]
	void ReportFall( float speed )
	{
		if ( Rpc.Caller is null || Rpc.Caller.Id != Network.OwnerId ) return;
		TakeDamage( Math.Clamp( (speed - FallDamageSpeed) * 0.12f, 0f, MaxHealth ) );
	}

	/// <summary> Admin testing aid: hurt (or with a negative number, heal) your own trooper. </summary>
	[ConCmd( "swrp_hurt", Help = "swrp_hurt <amount> - admin only, damages your own trooper (negative heals)" )]
	public static void HurtSelf( float amount ) => RequestHurtSelf( amount );

	[Rpc.Host]
	static void RequestHurtSelf( float amount )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		if ( !Whitelist.IsAdmin( caller ) )
		{
			SwrpNet.NotifyConnection( caller, "You need to be an admin to do that.", true );
			return;
		}

		var vitals = SwrpGameManager.Current?.FindPlayer( caller )?.GetComponent<SwrpVitals>();
		if ( vitals is null ) return;

		if ( amount >= 0 ) vitals.TakeDamage( amount );
		else vitals.Heal( -amount );
	}
}
