using System;
using Sandbox.Audio;

namespace Swrp;

/// <summary>
/// Voice comms. Everyone talks through a clone helmet (a light band-limited "helmet" filter on proximity voice, at the
/// whisper / normal / yell range picked with B). Y switches your voice to the radio: while it's on, what you say (V) goes
/// out on your radio net (your /channel: squad, legion, command or open) to everyone on that net with their comms on (N),
/// wherever they are, through a crackly sci-fi radio filter with a squelch chirp at each end. People near you still hear
/// you through your helmet as normal. Each listener's machine decides how to play each speaker, so it costs nothing
/// on the network beyond the voice itself.
/// </summary>
public static class SwrpComms
{
	[ConVar( "swrp_radio_volume", Help = "Volume of voices heard over the radio (0 to 1.5)", Saved = true )]
	public static float RadioVolume { get; set; } = 1f;

	[ConVar( "swrp_helmet_voice", Help = "Helmet filter on nearby voices (1 = on, 0 = plain voices)", Saved = true )]
	public static bool HelmetVoice { get; set; } = true;

	static Mixer _radio, _helmet;
	static Mixer _voiceRoot;

	static Mixer VoiceRoot => _voiceRoot ??= Mixer.FindMixerByName( "Voice" );

	/// <summary> The radio: hard band-limited, driven, warbling and hissing. </summary>
	public static Mixer RadioMixer
	{
		get
		{
			if ( _radio is not null || VoiceRoot is null ) return _radio;
			_radio = VoiceRoot.AddChild();
			_radio.Name = "Radio";
			_radio.AddProcessor( new RadioVoiceProcessor { Drive = 3.2f, Warble = 0.18f, Hiss = 0.012f } );
			_radio.AddProcessor( new HighPassProcessor { Cutoff = 0.018f } );
			_radio.AddProcessor( new LowPassProcessor { Cutoff = 0.14f } );
			return _radio;
		}
	}

	/// <summary> Through the helmet: a little boxy and band-limited, no crackle. </summary>
	public static Mixer HelmetMixer
	{
		get
		{
			if ( _helmet is not null || VoiceRoot is null ) return _helmet;
			_helmet = VoiceRoot.AddChild();
			_helmet.Name = "Helmet";
			_helmet.AddProcessor( new RadioVoiceProcessor { Drive = 1.4f, Warble = 0.04f, Hiss = 0f } );
			_helmet.AddProcessor( new HighPassProcessor { Cutoff = 0.008f } );
			_helmet.AddProcessor( new LowPassProcessor { Cutoff = 0.32f } );
			return _helmet;
		}
	}

	/// <summary> Is <paramref name="listener"/> on <paramref name="speaker"/>'s radio net? </summary>
	public static bool OnNet( SwrpPlayer speaker, SwrpPlayer listener )
	{
		if ( !speaker.IsValid() || !listener.IsValid() || speaker == listener ) return false;
		if ( speaker.CharacterId is null || listener.CharacterId is null ) return false;
		return ( speaker.RadioChannel ?? "legion" ) switch
		{
			"squad" => SwrpRoleplay.Current?.SquadOf( speaker.OwnerSteamId ) is { } sq && sq.Members.Contains( listener.OwnerSteamId ),
			"command" => listener.RankIndex >= SwrpRoleplay.LieutenantRank,
			"open" => true,
			"pa" => true,       // the ship's PA: everyone aboard
			_ => listener.FactionId == speaker.FactionId,
		};
	}

	/// <summary> The net a player transmits on, for the comlink. </summary>
	public static string NetName( SwrpPlayer p ) => ( p.RadioChannel ?? "legion" ) switch
	{
		"squad" => SwrpRoleplay.Current?.SquadOf( p.OwnerSteamId ) is { } sq ? $"SQUAD · {sq.Name.ToUpperInvariant()}" : "SQUAD · NONE",
		"command" => "COMMAND NET",
		"open" => "OPEN BAND",
		"pa" => "SHIP PA",
		_ => $"{p.Faction.ShortName.ToUpperInvariant()} NET",
	};

	/// <summary> A made-up frequency per net, for the comlink's readout. </summary>
	public static string Frequency( SwrpPlayer p )
		=> FrequencyFor( ( p.RadioChannel ?? "legion" ) + ( p.RadioChannel == "squad" ? SwrpRoleplay.Current?.SquadOf( p.OwnerSteamId )?.Name : p.FactionId ) );

	/// <summary> A squad's own net frequency (the squad menu shows it whether or not you're tuned in). </summary>
	public static string SquadFrequency( SquadInfo squad ) => FrequencyFor( "squad" + squad?.Name );

	static string FrequencyFor( string key )
	{
		var h = 0;
		foreach ( var c in key ?? "" ) h = h * 31 + c;
		return $"{120 + Math.Abs( h ) % 380}.{Math.Abs( h / 7 ) % 10} MHz";
	}

	/// <summary> Is this speaker talking right now (their voice played in the last moment)? </summary>
	public static bool Speaking( SwrpPlayer p ) => p.IsProxy
		? p.Transmitting || p.GetComponent<Voice>() is { } v && v.LastPlayed < 0.25f
		: p.VoiceKeyHeld && !p.Gagged;

	static readonly Dictionary<Guid, bool> _wasTalking = new();

	/// <summary> Closer than this, a radio talker on your net is heard in person through their helmet, not over the radio. </summary>
	const float InPersonRange = 220f;

	/// <summary>
	/// Every machine, every frame, for every player: how this machine hears their voice. Radio for the nets you're on
	/// (comms on); otherwise their helmet, positioned, at their range. Also plays the squelch chirps.
	/// </summary>
	public static void Route( SwrpPlayer speaker, Voice voice )
	{
		var me = SwrpPlayers.Local;      // cached once a frame: this runs for every voice every frame
		// the ship's PA plays through the speakers whether your receiver is on or not
		var pa = speaker.RadioChannel == "pa";
		var radio = speaker.IsProxy && me.IsValid() && ( me.CommsOn || pa ) && speaker.RadioTx && OnNet( speaker, me ) && !speaker.Gagged
			&& ( pa || me.WorldPosition.Distance( speaker.WorldPosition ) > InPersonRange );
		var mixer = radio ? RadioMixer : HelmetVoice ? HelmetMixer : VoiceRoot;
		if ( voice.WorldspacePlayback == radio ) voice.WorldspacePlayback = !radio;
		if ( mixer is not null && voice.VoiceMixer.Name != mixer.Name ) voice.VoiceMixer = mixer;
		voice.Distance = radio ? 1000000f : SwrpChat.RangeFor( speaker.VoiceMode );
		voice.Volume = speaker.Gagged ? 0f : radio ? Math.Clamp( RadioVolume, 0f, 1.5f ) : 1f;

		// the squelch: a chirp when a transmission on your net starts and ends (your own too)
		var transmitting = speaker.RadioTx && !speaker.Gagged && ( radio || !speaker.IsProxy ) && Speaking( speaker );
		var id = speaker.GameObject.Id;
		_wasTalking.TryGetValue( id, out var was );
		if ( transmitting != was )
		{
			_wasTalking[id] = transmitting;
			SwrpAudio.Ui( transmitting ? SwrpAudio.RadioOpen : SwrpAudio.RadioClose );
			if ( transmitting ) LastTransmission = (speaker, 0f);
		}
		if ( transmitting ) Live[id] = speaker;
		else Live.Remove( id );
	}

	/// <summary> Who is talking on a net you hear (or you, on your own), for the comlink. </summary>
	public static readonly Dictionary<Guid, SwrpPlayer> Live = new();
	public static (SwrpPlayer Who, RealTimeSince Since) LastTransmission;

	/// <summary> Owner: Y switches your voice to the radio, N your receiver. </summary>
	public static void OwnerInput( SwrpPlayer me )
	{
		if ( SwrpUiState.ChatOpen || SwrpUiState.AnyOpen ) return;
		if ( Input.Pressed( "RadioTx" ) ) SetTx( me, !me.RadioTx );
		if ( Input.Pressed( "Comms" ) ) SetComms( me, !me.CommsOn );
	}

	public static void SetTx( SwrpPlayer me, bool on )
	{
		me.RadioTx = on;
		SwrpAudio.Ui( on ? SwrpAudio.RadioOpen : SwrpAudio.RadioClose );
		SwrpChat.AddLocal( ChatKind.System, on ? $"Voice on the radio: {NetName( me )} (hold V to talk, Y for your helmet only)." : "Voice back to your helmet only." );
	}

	public static void SetComms( SwrpPlayer me, bool on )
	{
		me.CommsOn = on;
		SwrpAudio.Ui( on ? SwrpAudio.CommsOn : SwrpAudio.CommsOff );
		SwrpChat.AddLocal( ChatKind.System, on ? "Comms on: you hear radio traffic." : "Comms off: radio voice and text are silent." );
	}

	[ConCmd( "swrp_comms_info", Help = "swrp_comms_info - print the voice mixers and who is on which net" )]
	public static void InfoCommand()
	{
		var radio = RadioMixer;
		var helmet = HelmetMixer;
		Log.Info( $"[SWRP] comms: voice mixer {VoiceRoot?.Name}, radio {radio?.Name}, helmet {helmet?.Name}" );
		foreach ( var p in Game.ActiveScene?.GetAllComponents<SwrpPlayer>() ?? Enumerable.Empty<SwrpPlayer>() )
			Log.Info( $"[SWRP] comms: {p.CharacterName} net {NetName( p )}, radio tx {p.RadioTx}, comms {( p.CommsOn ? "on" : "off" )}, voice {VoiceNames( p )}" );
	}

	static string VoiceNames( SwrpPlayer p ) => SwrpChat.VoiceNames[Math.Clamp( p.VoiceMode, 0, 2 )];

	[ConCmd( "swrp_radio_tx", Help = "swrp_radio_tx - switch your voice between the radio and your helmet (same as Y)" )]
	public static void TxCommand()
	{
		if ( SwrpPlayer.Local is { } me ) SetTx( me, !me.RadioTx );
	}

	[ConCmd( "swrp_comms", Help = "swrp_comms - switch your comm receiver on or off (same as N)" )]
	public static void CommsCommand()
	{
		if ( SwrpPlayer.Local is { } me ) SetComms( me, !me.CommsOn );
	}
}

/// <summary>
/// The radio's grit: drives the voice into soft clipping, warbles it slightly (the sci-fi helmet buzz), and adds a hiss
/// that follows the voice. Filters before and after it band-limit it. Runs on the audio thread, so it keeps a tiny bit
/// of state per channel and nothing else.
/// </summary>
public sealed class RadioVoiceProcessor : AudioProcessor
{
	public float Drive { get; set; } = 3f;
	public float Warble { get; set; } = 0.15f;
	public float Hiss { get; set; } = 0.01f;

	readonly float[] _phase = new float[8];
	readonly float[] _env = new float[8];
	uint _seed = 22695477;

	protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{
		var c = ( channel.GetHashCode() & 0x7fffffff ) % 8;      // (a channel is a small struct; its hash picks its state slot)
		var phase = _phase[c];
		var env = _env[c];
		var norm = 1f / MathF.Tanh( Drive );
		const float step = 2f * MathF.PI * 31f / 48000f;      // the warble's rate
		for ( var i = 0; i < input.Length; i++ )
		{
			var x = input[i];
			env = env * 0.999f + MathF.Abs( x ) * 0.001f;
			phase += step;
			if ( phase > 2f * MathF.PI ) phase -= 2f * MathF.PI;
			x *= 1f + Warble * MathF.Sin( phase );
			x = MathF.Tanh( x * Drive ) * norm;
			_seed = _seed * 1664525u + 1013904223u;
			var noise = ( _seed >> 9 ) * ( 1f / 8388608f ) - 1f;
			input[i] = x + noise * Hiss * MathF.Min( 1f, env * 40f );
		}
		_phase[c] = phase;
		_env[c] = env;
	}
}
