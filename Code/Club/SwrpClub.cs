using System;
using System.Text.Json.Serialization;

namespace Swrp;

/// <summary> A track on a deck or in the queue. Source is "builtin:&lt;id&gt;", "file:&lt;mounted path&gt;", an http(s) audio URL or "youtube:&lt;video id&gt;". </summary>
public sealed class ClubTrack
{
	[JsonPropertyName( "s" )] public string Source { get; set; }
	[JsonPropertyName( "t" )] public string Title { get; set; }
}

/// <summary>
/// The cantina club: two DJ decks with a crossfader and a queue, plus the club lights. The host owns the state (what each deck
/// plays and when it started, in Unix seconds, so every machine seeks to the same point) and every machine plays it for itself:
/// only a camera inside the cantina hears the music (SwrpClub.Audio.cs) and sees the lights (SwrpClub.Fx.cs). Made at runtime by
/// <see cref="SwrpClubSystem"/> at the VMF DJ booth, so nothing is added to the generated scene. E on the booth opens <see cref="DjMenu"/>.
/// </summary>
[Title( "SWRP Club" ), Category( "SWRP" ), Icon( "queue_music" )]
public sealed partial class SwrpClub : Component, ISwrpUsable
{
	// ----- where it is (VMF/world units, from rp_venator_notorious_os.vmf) -----

	/// <summary> The DJ desk (func_detail x -1580..-1426, y 3951..3983, z -818..-781); the old music button sat on it at -1503 3968. </summary>
	public static readonly Vector3 BoothPos = new( -1503f, 3967f, -800f );
	/// <summary> The cantina (its trigger_soundscape, amb_cantina). Music and lights are only for cameras inside. </summary>
	public static readonly BBox Room = new( new Vector3( -1848f, 3868f, -830f ), new Vector3( -760f, 4829f, -590f ) );
	/// <summary> The light-up dance floor (club_penguin_floor). </summary>
	public static readonly Vector3 FloorPos = new( -1502f, 4145f, -817f );

	public const float BoothReach = 260f;
	public const int MaxQueue = 40;

	public static SwrpClub Current { get; private set; }

	/// <summary> The DJ menu is open on this machine (stops the player moving through SwrpUiState.AnyOpen). </summary>
	public static bool MenuOpen
	{
		get => SwrpUiState.ClubMenuOpen;
		set
		{
			if ( SwrpUiState.ClubMenuOpen != value && SwrpPlayer.Local is not null )
				SwrpAudio.Ui( value ? SwrpAudio.Open : SwrpAudio.Close );
			SwrpUiState.ClubMenuOpen = value;
		}
	}

	// ----- synced state (host writes) -----

	[Sync( SyncFlags.FromHost )] public string ASource { get; set; }
	[Sync( SyncFlags.FromHost )] public string ATitle { get; set; }
	/// <summary> Unix time (host clock) at which the track's position 0 played. </summary>
	[Sync( SyncFlags.FromHost )] public double AStart { get; set; }
	/// <summary> Paused at this many seconds in; below 0 = playing. </summary>
	[Sync( SyncFlags.FromHost )] public float APausedAt { get; set; } = -1f;
	/// <summary> Seconds, reported by the first machine that loaded it; 0 = not known yet or a live stream. </summary>
	[Sync( SyncFlags.FromHost )] public float ADuration { get; set; }

	[Sync( SyncFlags.FromHost )] public string BSource { get; set; }
	[Sync( SyncFlags.FromHost )] public string BTitle { get; set; }
	[Sync( SyncFlags.FromHost )] public double BStart { get; set; }
	[Sync( SyncFlags.FromHost )] public float BPausedAt { get; set; } = -1f;
	[Sync( SyncFlags.FromHost )] public float BDuration { get; set; }

	/// <summary> 0 = all deck A, 1 = all deck B, when no fade is running. </summary>
	[Sync( SyncFlags.FromHost )] public float Crossfader { get; set; }
	/// <summary> A crossfader move in progress: from, to, start (host Unix time), seconds. FadeSeconds 0 = none. </summary>
	[Sync( SyncFlags.FromHost )] public float FadeFrom { get; set; }
	[Sync( SyncFlags.FromHost )] public float FadeTo { get; set; }
	[Sync( SyncFlags.FromHost )] public double FadeStart { get; set; }
	[Sync( SyncFlags.FromHost )] public float FadeSeconds { get; set; }

	[Sync( SyncFlags.FromHost )] public float Master { get; set; } = 0.8f;
	[Sync( SyncFlags.FromHost )] public bool AutoMix { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public float MixSeconds { get; set; } = 8f;
	[Sync( SyncFlags.FromHost )] public string QueueJson { get; set; } = "[]";

	[Sync( SyncFlags.FromHost )] public bool Lights { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public bool Lasers { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public bool Strobe { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Haze { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public bool ShowScreen { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public int Palette { get; set; }

	/// <summary> Staff lock: only staff can work the decks. </summary>
	[Sync( SyncFlags.FromHost )] public bool Locked { get; set; }
	[Sync( SyncFlags.FromHost )] public string DjName { get; set; }

	/// <summary> The host's clock, refreshed every second so clients can measure their own clock's offset. </summary>
	[Sync( SyncFlags.FromHost )] public double HostClock { get; set; }

	// ----- time -----

	static double LocalUnix => ( DateTime.UtcNow - DateTime.UnixEpoch ).TotalSeconds;

	double _clockSkew;          // host clock minus ours (clients)
	double _lastHostClock;
	bool _haveSkew;

	/// <summary> Now on the host's clock, on any machine. </summary>
	public double Now => Networking.IsHost || !Networking.IsActive ? LocalUnix : LocalUnix + _clockSkew;

	void TrackClock()
	{
		if ( Networking.IsHost || !Networking.IsActive ) return;
		if ( HostClock == _lastHostClock || HostClock <= 0 ) return;
		_lastHostClock = HostClock;
		var skew = HostClock - LocalUnix;
		// the update arrives a little late, so the largest skew seen is the closest; drift slowly back in case clocks move
		_clockSkew = !_haveSkew ? skew : Math.Max( skew, _clockSkew - 0.02 );
		_haveSkew = true;
	}

	// ----- deck helpers (any machine) -----

	public string Source( int deck ) => deck == 0 ? ASource : BSource;
	public string Title( int deck ) => deck == 0 ? ATitle : BTitle;
	public double Start( int deck ) => deck == 0 ? AStart : BStart;
	public float PausedAt( int deck ) => deck == 0 ? APausedAt : BPausedAt;
	public float Duration( int deck ) => deck == 0 ? ADuration : BDuration;
	public bool Loaded( int deck ) => !string.IsNullOrEmpty( Source( deck ) );

	/// <summary> Seconds into the deck's track right now (on the host's clock). </summary>
	public float Position( int deck )
	{
		if ( !Loaded( deck ) ) return 0f;
		var p = PausedAt( deck );
		return p >= 0f ? p : (float)Math.Max( 0, Now - Start( deck ) );
	}

	/// <summary> The crossfader right now, with any running fade applied. </summary>
	public float CrossfaderNow
	{
		get
		{
			if ( FadeSeconds <= 0f ) return Crossfader;
			var t = (float)Math.Clamp( ( Now - FadeStart ) / FadeSeconds, 0, 1 );
			return MathX.Lerp( FadeFrom, FadeTo, t * t * ( 3f - 2f * t ) );
		}
	}

	/// <summary> Equal-power crossfade gain for a deck. </summary>
	public float Gain( int deck )
	{
		var x = Math.Clamp( CrossfaderNow, 0f, 1f );
		return deck == 0 ? MathF.Cos( x * MathF.PI * 0.5f ) : MathF.Sin( x * MathF.PI * 0.5f );
	}

	/// <summary> The deck the crowd mostly hears. </summary>
	public int LiveDeck => CrossfaderNow < 0.5f ? 0 : 1;

	public List<ClubTrack> Queue
	{
		get
		{
			try { return Json.Deserialize<List<ClubTrack>>( QueueJson ?? "[]" ) ?? new(); }
			catch { return new(); }
		}
	}

	public static bool InRoom( Vector3 p ) => Room.Contains( p );

	// ----- tracks -----

	/// <summary> The original tracks that ship with the game (SoundSource/club_music.py). </summary>
	public static readonly (string Id, string Title)[] BuiltIn =
	{
		( "underlevel", "Underlevel 1313 (house, 124)" ),
		( "hyperlane", "Hyperlane Run (techno, 130)" ),
		( "outer_rim", "Outer Rim Lounge (downtempo, 96)" ),
		( "kessel", "Kessel Shuffle (breaks, 112)" ),
	};

	public const string BuiltInFolder = "sounds/music/club";

	public static string BuiltInPath( string id ) => $"{BuiltInFolder}/{id}.wav";

	/// <summary> A short label for where a track comes from. </summary>
	public static string Kind( string source )
	{
		if ( string.IsNullOrEmpty( source ) ) return "";
		if ( source.StartsWith( "builtin:" ) ) return "BUILT-IN";
		if ( source.StartsWith( "file:" ) ) return "LOCAL FILE";
		if ( source.StartsWith( "youtube:" ) ) return "YOUTUBE VIDEO";
		return "STREAM";
	}

	/// <summary>
	/// Off by default: tested 2026-10-12, YouTube does not play in the engine's built-in browser. Its embedded player shows
	/// "Video player configuration error" (it needs to sit inside another website), and the full watch page loads but its video
	/// spins forever. Turn on to try again after an engine update.
	/// </summary>
	[ConVar( "swrp_club_youtube", Help = "Host: accept YouTube links at the DJ booth (experimental; they don't play in the current engine)", Saved = true )]
	public static bool YouTubeAllowed { get; set; }

	public const string YouTubeBlockedMessage = "YouTube can't play in the game: its player refuses to run in the game's built-in browser. Use a direct audio link (.mp3/.ogg) or an internet radio stream.";

	/// <summary> Turns what the DJ typed into a track source, or says why not. </summary>
	public static bool ParseInput( string input, out string source, out string error )
	{
		source = null;
		error = null;
		input = input?.Trim() ?? "";
		if ( input.Length == 0 ) { error = "Paste an audio URL first."; return false; }
		if ( input.Length > 400 ) { error = "That link is too long."; return false; }
		if ( input.StartsWith( "builtin:" ) || input.StartsWith( "file:" ) )
		{
			source = input;
			return true;
		}
		// already parsed by the menu (the host gets "youtube:<id>", not the link)
		if ( input.StartsWith( "youtube:" ) )
		{
			if ( !YouTubeAllowed ) { error = YouTubeBlockedMessage; return false; }
			var vid = input["youtube:".Length..];
			if ( vid.Length is < 1 or > 20 || !vid.All( c => char.IsLetterOrDigit( c ) || c == '-' || c == '_' ) ) { error = "That YouTube video id isn't valid."; return false; }
			source = input;
			return true;
		}
		// a link pasted without https:// (youtu.be/..., www.youtube.com/..., example.com/track.mp3)
		if ( !input.Contains( "://" ) && input.Contains( '.' ) && !input.Contains( ' ' ) ) input = "https://" + input;
		if ( !Uri.TryCreate( input, UriKind.Absolute, out var uri ) || ( uri.Scheme != "https" && uri.Scheme != "http" ) )
		{
			error = "That isn't a web link (it must start with https://).";
			return false;
		}
		var host = uri.Host.ToLowerInvariant();
		if ( host.EndsWith( "youtube.com" ) || host == "youtu.be" || host.EndsWith( "youtube-nocookie.com" ) )
		{
			if ( !YouTubeAllowed )
			{
				error = YouTubeBlockedMessage;
				return false;
			}
			var id = YouTubeId( uri );
			if ( id is null ) { error = "Couldn't find a video id in that YouTube link."; return false; }
			source = "youtube:" + id;
			return true;
		}
		if ( host.EndsWith( "spotify.com" ) || host.EndsWith( "soundcloud.com" ) || host.EndsWith( "music.apple.com" ) || host.EndsWith( "deezer.com" ) || host.EndsWith( "tidal.com" ) )
		{
			error = "That's a music service page, not an audio file. Use a direct .mp3/.ogg/.opus link or an internet radio stream URL.";
			return false;
		}
		source = uri.ToString();
		return true;
	}

	static string YouTubeId( Uri uri )
	{
		string id = null;
		if ( uri.Host.ToLowerInvariant() == "youtu.be" )
			id = uri.AbsolutePath.Trim( '/' ).Split( '/' ).FirstOrDefault();
		else if ( uri.AbsolutePath.StartsWith( "/embed/" ) || uri.AbsolutePath.StartsWith( "/shorts/" ) || uri.AbsolutePath.StartsWith( "/live/" ) )
			id = uri.AbsolutePath.Split( '/', StringSplitOptions.RemoveEmptyEntries ).ElementAtOrDefault( 1 );
		else
		{
			foreach ( var part in uri.Query.TrimStart( '?' ).Split( '&' ) )
				if ( part.StartsWith( "v=" ) ) id = part[2..];
		}
		if ( string.IsNullOrEmpty( id ) || id.Length > 20 || !id.All( c => char.IsLetterOrDigit( c ) || c == '-' || c == '_' ) ) return null;
		return id;
	}

	public static string DefaultTitle( string source )
	{
		if ( string.IsNullOrEmpty( source ) ) return "";
		if ( source.StartsWith( "builtin:" ) )
		{
			var id = source["builtin:".Length..];
			return BuiltIn.FirstOrDefault( b => b.Id == id ).Title ?? id;
		}
		if ( source.StartsWith( "file:" ) ) return System.IO.Path.GetFileNameWithoutExtension( source );
		if ( source.StartsWith( "youtube:" ) ) return "YouTube " + source["youtube:".Length..];
		if ( Uri.TryCreate( source, UriKind.Absolute, out var uri ) )
		{
			var name = System.IO.Path.GetFileNameWithoutExtension( uri.AbsolutePath );
			return string.IsNullOrEmpty( name ) ? uri.Host : $"{name} ({uri.Host})";
		}
		return source;
	}

	// ----- life -----

	protected override void OnStart()
	{
		Current = this;
		Tags.Add( "swrp_club" );

		// what E hits: a box just proud of the DJ desk
		var col = AddComponent<BoxCollider>();
		col.Scale = new Vector3( 162f, 40f, 42f );
		col.Center = new Vector3( 0f, 0f, 0f );
	}

	protected override void OnDestroy()
	{
		if ( Current == this ) Current = null;
		StopAudio();
		ClearFx();
		if ( MenuOpen ) MenuOpen = false;
	}

	protected override void OnUpdate()
	{
		Current = this;
		TrackClock();
		if ( Networking.IsHost || !Networking.IsActive ) HostTick();
		AudioTick();
		FxTick();
	}

	// ----- E on the booth -----

	public (string Title, string Sub, string Tone) Prompt( SwrpPlayer me )
	{
		if ( Locked && !( me?.StaffOnDuty ?? false ) ) return ( "DJ booth", "Locked by staff", "denied" );
		var playing = Loaded( LiveDeck ) ? $"Playing: {Title( LiveDeck )}" : "Open the decks";
		return ( "DJ booth", playing, "info" );
	}

	public void Use( SwrpPlayer me )
	{
		if ( Locked && !( me?.StaffOnDuty ?? false ) )
		{
			SwrpAudio.Ui( SwrpAudio.Close );
			return;
		}
		MenuOpen = true;
	}
}

/// <summary> Makes the club once per play session, on the host, when the Notorious map is loaded. </summary>
public sealed class SwrpClubSystem : GameObjectSystem
{
	[ConVar( "swrp_club", Help = "Host: put the DJ booth and club in the cantina (1) or not (0)", Saved = true )]
	public static bool Enabled { get; set; } = true;

	RealTimeSince _checked = 10f;
	GameObject _menu;

	public SwrpClubSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 30, Tick, "SwrpClub" );
	}

	void Tick()
	{
		if ( Scene is null || !Game.IsPlaying || Scene != Game.ActiveScene ) return;

		// the DJ menu's screen layer, on every machine
		if ( !_menu.IsValid() )
		{
			_menu = Scene.CreateObject();
			_menu.Name = "Club Menu";
			_menu.NetworkMode = NetworkMode.Never;
			_menu.Flags |= GameObjectFlags.NotSaved;
			var panel = _menu.Components.Create<ScreenPanel>();
			panel.ZIndex = 56;
			_menu.Components.Create<DjMenu>();
		}

		if ( _checked < 2f ) return;
		_checked = 0;
		if ( Networking.IsActive && !Networking.IsHost ) return;
		if ( !Enabled || SwrpShip.Current is null ) return;      // the Notorious only
		if ( Scene.GetAllComponents<SwrpClub>().Any() ) return;

		var go = Scene.CreateObject();
		go.Name = "Cantina Club";
		go.WorldPosition = SwrpClub.BoothPos;
		go.Components.Create<SwrpClub>();
		go.NetworkSpawn();
		Log.Info( "[SWRP] club: DJ booth up in the cantina" );
	}
}
