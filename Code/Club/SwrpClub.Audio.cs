using System;

namespace Swrp;

/// <summary>
/// The club's sound, on every machine for its own camera: while the camera is inside the cantina each loaded deck plays, seeked to
/// the host's position and kept within about half a second of it, mixed by the crossfader. Built-in tracks and local files play
/// through the engine's sound system (the same path as the menu music: <c>sounds/music/club/&lt;id&gt;.sound</c>); web URLs and radio
/// stream through a MusicPlayer. Leave the room and it fades out and the players are closed, so nobody outside hears it.
/// YouTube tracks play in the booth screen's embedded player instead (ClubScreen).
/// </summary>
public sealed partial class SwrpClub
{
	[ConVar( "swrp_club_volume", Help = "Your own cantina club music level, 0 to 1 (0 = off)", Saved = true )]
	public static float LocalVolume { get; set; } = 0.8f;

	[ConVar( "swrp_club_listen_anywhere", Help = "Debug: hear the club wherever your camera is (0 = only inside the cantina)" )]
	public static bool ListenAnywhere { get; set; }

	[ConVar( "swrp_club_debug", Help = "Log the club's deck players (1 = on)" )]
	public static bool DebugLog { get; set; }

	sealed class DeckPlayer
	{
		public MusicPlayer Mp;          // streams
		public SoundHandle Snd;         // game files
		public SoundFile File;
		public string Source;
		public RealTimeSince SinceSeek;
		public RealTimeSince Age;
		public RealTimeSince SinceStart;
		public bool Reported;
		public bool Failed;
		public float Volume;

		public float Duration => Mp is not null ? Mp.Duration : File is { IsLoaded: true } f ? f.Duration : 0f;
		public float Time => Mp is not null ? Mp.PlaybackTime : Snd is { } s ? s.Time : 0f;
		public float Amplitude => Mp is not null ? Mp.Amplitude : Snd is { } s ? s.Amplitude : 0f;
	}

	readonly DeckPlayer[] _decks = new DeckPlayer[2];
	float _roomLevel;

	/// <summary> This machine's camera is in the cantina. </summary>
	public bool ListenerInside { get; private set; }

	/// <summary> Loudness of what this machine hears (0..~1), for the lights. </summary>
	public float Level { get; private set; }

	/// <summary> 32 smoothed spectrum bands (low to high) of what this machine hears, for the booth screen. </summary>
	public readonly float[] Bands = new float[32];

	/// <summary> The video (YouTube) the live deck wants on the booth screen, if any. </summary>
	public string LiveVideo => Source( LiveDeck ) is { } s && s.StartsWith( "youtube:" ) ? s["youtube:".Length..] : null;

	readonly float[] _bandsNow = new float[32];

	void AudioTick()
	{
		var cam = Scene.Camera;
		ListenerInside = ListenAnywhere || cam.IsValid() && InRoom( cam.WorldPosition );
		_roomLevel = _roomLevel.Approach( ListenerInside ? 1f : 0f, Time.Delta * 2.5f );

		ProbeTick();
		var level = 0f;
		Array.Clear( _bandsNow );
		for ( var d = 0; d < 2; d++ )
			level += TickDeck( d );

		Level = Level.LerpTo( level, Time.Delta * 12f );
		for ( var i = 0; i < Bands.Length; i++ )
			Bands[i] = _bandsNow[i] > Bands[i] ? _bandsNow[i] : Bands[i].LerpTo( _bandsNow[i], Time.Delta * 6f );
	}

	float TickDeck( int deck )
	{
		var source = Source( deck );
		var want = _roomLevel > 0.001f && !string.IsNullOrEmpty( source ) && !source.StartsWith( "youtube:" ) && LocalVolume > 0f;
		var dp = _decks[deck];

		if ( !want || dp is not null && dp.Source != source )
		{
			Close( deck );
			dp = null;
			if ( !want ) return 0f;
		}

		var paused = PausedAt( deck ) >= 0f;
		var expected = Position( deck );
		var loop = IsLoop( source );

		if ( dp is null )
		{
			dp = _decks[deck] = new DeckPlayer { Source = source, Age = 0, SinceSeek = 10f, SinceStart = 0 };
			Open( dp, source );
			if ( DebugLog || dp.Failed ) Log.Info( $"[SWRP] club: deck {( deck == 0 ? "A" : "B" )} open {source} -> {( dp.Failed ? "FAILED" : dp.Mp is not null ? "stream" : "sound" )}" );
		}
		if ( dp.Failed ) return 0f;

		var dur = dp.Duration;
		var ended = false;
		if ( dur > 0f )
		{
			if ( loop ) expected %= dur;
			else if ( expected >= dur ) ended = true;
		}
		dp.Volume = Math.Clamp( Master * Gain( deck ) * _roomLevel * LocalVolume, 0f, 1f );

		if ( dp.Mp is { } mp )
		{
			mp.Repeat = loop;
			mp.Paused = paused || ended;
			mp.Volume = dp.Volume;
			if ( dur > 0f && !ended && dp.SinceSeek > 1.5f && MathF.Abs( mp.PlaybackTime - expected ) > 0.6f )
			{
				mp.Seek( expected );
				dp.SinceSeek = 0;
			}
		}
		else
		{
			// a game sound: restart it if it ran out (loops, or the track was moved back), then keep it in step
			if ( !ended && ( dp.Snd is null || dp.Snd.IsStopped || !dp.Snd.IsPlaying && !dp.Snd.Paused || loop && dur > 0f && dp.Snd.Time >= dur - 0.05f ) && dp.SinceStart > 1.5f )
			{
				dp.Snd?.Stop();
				dp.Snd = PlayFile( dp.File );
				dp.SinceSeek = 10f;
				dp.SinceStart = 0;
				if ( dp.Snd is null ) { dp.Failed = true; return 0f; }
			}
			if ( dp.Snd is { } s )
			{
				s.Paused = paused || ended;
				s.Volume = dp.Volume;
				if ( dur > 0f && !ended && dp.SinceSeek > 1.5f && MathF.Abs( s.Time - expected ) > 0.6f )
				{
					s.Time = expected;
					dp.SinceSeek = 0;
				}
			}
		}

		if ( DebugLog && dp.Age > 2f && (int)( dp.Age * 2f ) != (int)( ( dp.Age - Time.Delta ) * 2f ) && (int)dp.Age % 5 == 0 )
			Log.Info( $"[SWRP] club: deck {( deck == 0 ? "A" : "B" )} t={dp.Time:0.0}/{dur:0.0} want {expected:0.0} vol {dp.Volume:0.00} amp {dp.Amplitude:0.000}" );

		if ( !dp.Reported && dur > 0f && Duration( deck ) <= 0f && dp.Age > 1f )
		{
			dp.Reported = true;
			ReportDuration( deck, source, dur );
		}

		if ( paused || ended ) return 0f;
		var amp = dp.Amplitude * dp.Volume;

		if ( dp.Mp is { } m && m.Spectrum.Length > 0 )
		{
			var spec = m.Spectrum;
			// 512 linear bins folded into 32 roughly logarithmic bands
			for ( var b = 0; b < 32; b++ )
			{
				var lo = (int)( 2 * MathF.Pow( 256f, b / 32f ) );
				var hi = Math.Max( lo + 1, (int)( 2 * MathF.Pow( 256f, ( b + 1 ) / 32f ) ) );
				var sum = 0f;
				for ( var i = lo; i < hi && i < spec.Length; i++ ) sum += spec[i];
				_bandsNow[b] += sum / ( hi - lo ) * dp.Volume;
			}
		}
		else
		{
			// no spectrum from a game sound: shape the loudness into a lively, bass-heavy set of bars
			var t = (float)( Now % 1000.0 );
			for ( var b = 0; b < 32; b++ )
			{
				var shape = ( 1.1f - b / 40f ) * ( 0.55f + 0.45f * MathF.Sin( t * ( 3f + b * 0.37f ) + b * 1.9f ) );
				_bandsNow[b] += amp * 0.6f * shape;
			}
		}
		return amp;
	}

	static void Open( DeckPlayer dp, string source )
	{
		try
		{
			if ( source.StartsWith( "builtin:" ) || source.StartsWith( "file:" ) )
			{
				var path = source.StartsWith( "builtin:" ) ? BuiltInPath( source["builtin:".Length..] ) : source["file:".Length..];
				dp.File = SoundFile.Load( path );
				if ( dp.File is null )
				{
					dp.Failed = true;
					Log.Warning( $"[SWRP] club: no sound file at {path}" );
					return;
				}
				dp.Snd = PlayFile( dp.File );
				dp.Failed = dp.Snd is null;
				return;
			}
			var mp = MusicPlayer.PlayUrl( source );
			if ( mp is null ) { dp.Failed = true; return; }
			mp.ListenLocal = true;      // a PA filling the room; the room check decides who hears it
			mp.Volume = 0f;
			dp.Mp = mp;
		}
		catch ( Exception e )
		{
			dp.Failed = true;
			Log.Warning( $"[SWRP] club: couldn't play {source}: {e.Message}" );
		}
	}

	static SoundHandle PlayFile( SoundFile file )
	{
		if ( file is null ) return null;
		var h = Sound.Play( new SoundEvent { Sounds = new List<SoundFile> { file }, UI = true, Volume = 1f } );
		if ( h is null ) return null;
		h.Volume = 0f;
		return h;
	}

	void Close( int deck )
	{
		var dp = _decks[deck];
		_decks[deck] = null;
		if ( dp is null ) return;
		try
		{
			if ( dp.Snd is { } s ) s.Stop();
			if ( dp.Mp is { } mp )
			{
				mp.Stop();
				( mp as IDisposable )?.Dispose();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] club: closing a deck: {e.Message}" );
		}
	}

	void StopAudio()
	{
		Close( 0 );
		Close( 1 );
	}

	/// <summary> This machine couldn't open a deck (missing file, bad stream). </summary>
	public bool DeckFailed( int deck ) => _decks[deck] is { Failed: true };

	/// <summary> This machine's player for a deck is buffering (opened but not producing time yet). </summary>
	public bool DeckBuffering( int deck ) => _decks[deck] is { Failed: false } dp && dp.Age > 0.5f && dp.Time <= 0f && PausedAt( deck ) < 0f;

	static SoundHandle _probe;
	static RealTimeSince _probeAge;
	static float _probeStart;

	/// <summary> Plays a built-in track almost silently on this machine for three seconds and logs whether it advanced. </summary>
	[ConCmd( "swrp_club_probe", Help = "Play a built-in club track very quietly for 3 s on this machine and report if the sound system plays it" )]
	public static void Probe( string id = "underlevel" )
	{
		var file = SoundFile.Load( BuiltInPath( id ) );
		Log.Info( $"[SWRP] club probe: {BuiltInPath( id )} file={( file is null ? "null" : $"loaded={file.IsLoaded} dur={file.Duration:0.0}" )}" );
		_probe = PlayFile( file );
		if ( _probe is null ) { Log.Info( "[SWRP] club probe: Sound.Play returned null" ); return; }
		_probe.Volume = 0.02f;
		_probe.Time = 20f;
		_probeStart = _probe.Time;
		_probeAge = 0;
	}

	static void ProbeTick()
	{
		if ( _probe is null || _probeAge < 3f ) return;
		Log.Info( $"[SWRP] club probe: after 3 s playing={_probe.IsPlaying} stopped={_probe.IsStopped} time {_probeStart:0.0} -> {_probe.Time:0.0} amp={_probe.Amplitude:0.000} file dur={_probe.SoundFile?.Duration:0.0}" );
		_probe.Stop();
		_probe = null;
	}

	/// <summary> Console check of what this machine's decks are doing. </summary>
	[ConCmd( "swrp_club_info", Help = "Print the cantina club's decks and what this machine is playing" )]
	public static void Info()
	{
		var c = Current;
		if ( c is null ) { Log.Info( "[SWRP] club: none in this scene" ); return; }
		Log.Info( $"[SWRP] club: inside={c.ListenerInside} room={c._roomLevel:0.00} xf={c.CrossfaderNow:0.00} master={c.Master:0.00} local={LocalVolume:0.00} level={c.Level:0.000}" );
		for ( var d = 0; d < 2; d++ )
		{
			var dp = c._decks[d];
			Log.Info( $"[SWRP] club: deck {( d == 0 ? "A" : "B" )} src={c.Source( d ) ?? "-"} pos={c.Position( d ):0.0} dur={c.Duration( d ):0.0} paused={c.PausedAt( d ) >= 0f} gain={c.Gain( d ):0.00}"
				+ ( dp is null ? " player=none" : $" player={( dp.Failed ? "FAILED" : dp.Mp is not null ? "stream" : "sound" )} t={dp.Time:0.0}/{dp.Duration:0.0} vol={dp.Volume:0.00} amp={dp.Amplitude:0.000}" ) );
		}
	}
}
