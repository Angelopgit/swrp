using System;

namespace Swrp;

/// <summary>
/// Going prone (Z): the trooper drops flat, crawls slowly, and gets up again with Z or Jump. The human animgraph has no
/// prone, so while down the body plays Squad's prone sequences straight (graph off): stand to prone, a lying idle, crawls
/// forward, back and to the sides at the speed you move, and prone to stand. They are authored on the trooper rig itself
/// (ModelSource/gc_droid_anims.py, set "prone"; local only, Offworld's animation). The rifle stays on the shoulder IK
/// (SwrpEmotes), so it points where you look from the ground. <see cref="SwrpPlayer.Prone"/> is owner synced; every machine
/// animates its own copy of the body from it. Movement (crawl speed, no jump) is in SwrpVitals.UpdateStamina, the low eye
/// in SwrpPlayer.ApplyScale.
/// </summary>
public sealed class SwrpProne : Component
{
	[ConVar( "swrp_prone_speed", Help = "Crawling speed while prone (units a second at trooper scale 1, default 34)", Saved = true )]
	public static float CrawlSpeed { get; set; } = 34f;

	public const string DownSeq = "squad_stand_to_prone", UpSeq = "squad_prone_to_stand", IdleSeq = "squad_prone_idle";

	// how fast each crawl clip moves at playback rate 1 (inches a second, measured on the clips: the forward crawl's planted
	// knee; the side and back crawls read badly that way, so those are set by eye against the forward one)
	static readonly (string Seq, float Speed)[] Crawls = { ( "squad_prone_fwd", 45f ), ( "squad_prone_bwd", 22f ), ( "squad_prone_left", 25f ), ( "squad_prone_right", 25f ) };

	enum Phase { Standing, GoingDown, Lying, GettingUp }

	SwrpPlayer _player;
	Phase _phase;
	TimeSince _sincePhase;
	Vector3 _lastPos;
	Vector3 _velocity;
	string _playing;
	Model _playingOn;

	/// <summary> This body can lie down: its model has the prone sequences (troopers; not droids, Jedi or event characters). </summary>
	public static bool CanAnimate( SkinnedModelRenderer body ) => body.IsValid() && body.Model is { } m && !m.IsError
		&& body.Sequence.SequenceNames.Contains( IdleSeq );

	/// <summary> This trooper's class has a model with the prone sequences (its "_squad" variant, which SwrpPlayer puts on while
	/// prone; unarmed troopers otherwise wear the plain one). </summary>
	static bool CanProne( SwrpPlayer p ) => CanAnimate( p.Body ) || p.EventDef is null && p.Class.LoadModel( squad: true ) is { } m
		&& Enumerable.Range( 0, m.AnimationCount ).Any( i => m.GetAnimationName( i ) == IdleSeq );

	/// <summary> On the ground or on the way down or up: the body is out of the graph. </summary>
	public bool Down => _phase != Phase.Standing;

	protected override void OnUpdate()
	{
		_player ??= GetComponent<SwrpPlayer>();
		if ( !_player.IsValid() ) return;
		if ( !_player.IsProxy ) OwnerInput();
		Animate();
	}

	void OwnerInput()
	{
		var p = _player;
		var vitals = p.GetComponent<SwrpVitals>();
		// no lying down behind a Z-6 rotary (Angelo, 2026-10-13): drawing one while prone gets you up
		var heavy = p.GetComponent<SwrpBlaster>() is { Drawn: true, WeaponId: "z6" };
		var blocked = heavy || p.InVehicle || p.InPod || p.Noclip || p.Frozen || vitals is { IsDead: true } or { IsDowned: true } or { IsStunned: true } || p.CharacterId is null;
		if ( p.Prone )
		{
			var up = !SwrpUiState.AnyOpen && !SwrpUiState.ChatOpen && ( Input.Pressed( "Prone" ) || Input.Pressed( "Jump" ) );
			if ( up || blocked || !CanProne( p ) ) p.Prone = false;
			return;
		}
		if ( blocked || SwrpUiState.AnyOpen || SwrpUiState.ChatOpen || !Input.Pressed( "Prone" ) ) return;
		if ( p.Controller is not { IsOnGround: true } || !CanProne( p ) ) return;
		p.Prone = true;
		if ( p.Emote != SwrpEmotes.None ) p.StartEmote( SwrpEmotes.None );
	}

	void Animate()
	{
		var body = _player.Body;
		if ( !body.IsValid() ) return;
		var dt = MathF.Max( Time.Delta, 0.001f );
		_velocity = Vector3.Lerp( _velocity, ( ( WorldPosition - _lastPos ) / dt ).WithZ( 0 ), MathF.Min( 1f, dt * 8f ) );
		_lastPos = WorldPosition;

		var want = _player.Prone && CanAnimate( body );
		switch ( _phase )
		{
			case Phase.Standing when want:
				Enter( Phase.GoingDown );
				Play( body, DownSeq, false, 1f );
				break;
			case Phase.GoingDown when !want:
			case Phase.Lying when !want:
				Enter( Phase.GettingUp );
				Play( body, UpSeq, false, 1.15f );
				break;
			case Phase.GettingUp when want:
				Enter( Phase.GoingDown );
				Play( body, DownSeq, false, 1f );
				break;
			case Phase.GoingDown when _sincePhase > 0.3f && body.Sequence.IsFinished || _sincePhase > 2.2f:
				Enter( Phase.Lying );
				break;
			case Phase.GettingUp when _sincePhase > 0.3f && body.Sequence.IsFinished || _sincePhase > 2.2f:
				Enter( Phase.Standing );
				_playing = null;
				body.UseAnimGraph = true;
				break;
		}
		// keep the transition playing (Play only restarts it if the body lost it, e.g. a model swap)
		if ( _phase == Phase.GoingDown ) Play( body, DownSeq, false, 1f );
		if ( _phase == Phase.GettingUp ) Play( body, UpSeq, false, 1.15f );
		if ( _phase != Phase.Lying ) return;

		// crawl by how the body is moving over the ground, the clip sped to match so the elbows and knees don't slide
		var local = body.WorldRotation.Inverse * _velocity;
		var speed = local.Length;
		var s = MathF.Max( 0.1f, body.WorldScale.z );
		if ( speed < 4f * s ) { Play( body, IdleSeq, true, 1f ); return; }
		var pick = MathF.Abs( local.x ) >= MathF.Abs( local.y ) ? ( local.x > 0 ? 0 : 1 ) : ( local.y > 0 ? 2 : 3 );
		var crawl = Crawls[pick];
		Play( body, crawl.Seq, true, Math.Clamp( speed / ( crawl.Speed * s ), 0.35f, 2.2f ) );
	}

	void Enter( Phase phase )
	{
		_phase = phase;
		_sincePhase = 0;
	}

	void Play( SkinnedModelRenderer body, string seq, bool loop, float rate )
	{
		if ( body.UseAnimGraph ) body.UseAnimGraph = false;
		// a new model (drawing or holstering swaps the trooper's model, SwrpPlayer.ApplyClassModel) starts with no sequence:
		// set it again whenever the body isn't actually playing it, not just when the wanted clip changes (Angelo saw prone
		// fail now and then, 2026-10-13)
		if ( _playing != seq || _playingOn != body.Model || body.Sequence.Name != seq )
		{
			_playing = seq;
			_playingOn = body.Model;
			body.Sequence.Name = seq;
			body.Sequence.Looping = loop;
			body.Sequence.Blending = true;
		}
		body.Sequence.PlaybackRate = rate;
	}

	protected override void OnDisabled()
	{
		if ( _player?.Body is { } b && b.IsValid() && _phase != Phase.Standing ) b.UseAnimGraph = true;
		_phase = Phase.Standing;
		_playing = null;
	}
}
