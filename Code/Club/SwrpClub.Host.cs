using System;

namespace Swrp;

/// <summary>
/// The club's host side: the DJ's requests (checked: a deployed trooper at the booth, or staff; staff only while locked) and the
/// auto-mix that rolls the queue onto the idle deck and crossfades to it as the live track runs out.
/// </summary>
public sealed partial class SwrpClub
{
	RealTimeSince _clockSent = 10f;
	bool _autoStarted;

	[ConVar( "swrp_club_autoplay", Help = "Host: start a built-in club track on deck A when the club comes up (1 = on)", Saved = true )]
	public static bool AutoPlay { get; set; } = true;

	/// <summary> Host console: put a built-in track (or a URL) on deck A and fade to it. </summary>
	[ConCmd( "swrp_club_play", Help = "Host: play a club track on deck A: a built-in id (underlevel, hyperlane, outer_rim, kessel) or a URL" )]
	public static void PlayCommand( string what = "underlevel" )
	{
		var c = Current;
		if ( c is null || Networking.IsActive && !Networking.IsHost ) { Log.Info( "[SWRP] club: host only, with the club up" ); return; }
		var input = BuiltIn.Any( b => b.Id == what ) ? "builtin:" + what : what;
		if ( !ParseInput( input, out var source, out var error ) ) { Log.Info( $"[SWRP] club: {error}" ); return; }
		c.Load( 0, source, DefaultTitle( source ), true );
		c.MixTo( 0, 0f );
		Log.Info( $"[SWRP] club: deck A now {source}" );
	}

	/// <summary> Who may work the decks. </summary>
	bool MayDj( Connection caller, out SwrpPlayer p )
	{
		p = caller is null ? null : SwrpGameManager.Current?.FindPlayer( caller );
		var staff = caller is not null && Whitelist.IsStaff( caller );
		if ( staff ) return true;
		if ( Locked )
		{
			SwrpNet.NotifyConnection( caller, "Staff have locked the DJ booth.", true );
			return false;
		}
		if ( !p.IsValid() || p.WorldPosition.Distance( WorldPosition ) > BoothReach + 60f )
		{
			SwrpNet.NotifyConnection( caller, "You need to be at the DJ booth.", true );
			return false;
		}
		return true;
	}

	void Credit( SwrpPlayer p )
	{
		if ( p.IsValid() ) DjName = p.ShortName;
	}

	// ----- requests -----

	[Rpc.Host]
	public void RequestLoad( int deck, string input, string title, bool play )
	{
		if ( deck is < 0 or > 1 || !MayDj( Rpc.Caller, out var p ) ) return;
		if ( !ParseInput( input, out var source, out var error ) )
		{
			SwrpNet.NotifyConnection( Rpc.Caller, error, true );
			return;
		}
		Load( deck, source, Clean( title ) ?? DefaultTitle( source ), play );
		Credit( p );
	}

	[Rpc.Host]
	public void RequestDeck( int deck, string action )
	{
		if ( deck is < 0 or > 1 || !MayDj( Rpc.Caller, out var p ) || !Loaded( deck ) ) return;
		switch ( action )
		{
			case "play":
				if ( PausedAt( deck ) >= 0f ) SetDeck( deck, start: Now - PausedAt( deck ), paused: -1f );
				break;
			case "pause":
				if ( PausedAt( deck ) < 0f ) SetDeck( deck, paused: Position( deck ) );
				break;
			case "restart":
				SetDeck( deck, start: Now, paused: PausedAt( deck ) >= 0f ? 0f : -1f );
				break;
			case "back":
				Nudge( deck, -10f );
				break;
			case "forward":
				Nudge( deck, 10f );
				break;
			case "eject":
				Eject( deck );
				break;
		}
		Credit( p );
	}

	void Nudge( int deck, float by )
	{
		var pos = Math.Max( 0f, Position( deck ) + by );
		if ( Duration( deck ) > 0f ) pos = Math.Min( pos, Duration( deck ) - 1f );
		if ( PausedAt( deck ) >= 0f ) SetDeck( deck, paused: pos );
		else SetDeck( deck, start: Now - pos );
	}

	[Rpc.Host]
	public void RequestCrossfader( float value )
	{
		if ( !MayDj( Rpc.Caller, out _ ) ) return;
		Crossfader = Math.Clamp( value, 0f, 1f );
		FadeSeconds = 0f;
	}

	/// <summary> Glide the crossfader over to a deck (seconds 0 = cut). </summary>
	[Rpc.Host]
	public void RequestMix( int deck, float seconds )
	{
		if ( deck is < 0 or > 1 || !MayDj( Rpc.Caller, out var p ) ) return;
		MixTo( deck, Math.Clamp( seconds, 0f, 30f ) );
		Credit( p );
	}

	[Rpc.Host]
	public void RequestQueue( string action, string input, string title, int index )
	{
		if ( !MayDj( Rpc.Caller, out var p ) ) return;
		var q = Queue;
		switch ( action )
		{
			case "add":
				if ( q.Count >= MaxQueue )
				{
					SwrpNet.NotifyConnection( Rpc.Caller, $"The queue is full ({MaxQueue}).", true );
					return;
				}
				if ( !ParseInput( input, out var source, out var error ) )
				{
					SwrpNet.NotifyConnection( Rpc.Caller, error, true );
					return;
				}
				q.Add( new ClubTrack { Source = source, Title = Clean( title ) ?? DefaultTitle( source ) } );
				break;
			case "remove":
				if ( index >= 0 && index < q.Count ) q.RemoveAt( index );
				break;
			case "up":
				if ( index > 0 && index < q.Count ) ( q[index - 1], q[index] ) = ( q[index], q[index - 1] );
				break;
			case "clear":
				q.Clear();
				break;
			case "deck0":
			case "deck1":
				if ( index < 0 || index >= q.Count ) return;
				var t = q[index];
				q.RemoveAt( index );
				Load( action == "deck0" ? 0 : 1, t.Source, t.Title, true );
				break;
			default:
				return;
		}
		QueueJson = Json.Serialize( q );
		Credit( p );
	}

	[Rpc.Host]
	public void RequestFx( string fx, bool on )
	{
		if ( !MayDj( Rpc.Caller, out _ ) ) return;
		switch ( fx )
		{
			case "lights": Lights = on; break;
			case "lasers": Lasers = on; break;
			case "strobe": Strobe = on; break;
			case "haze": Haze = on; break;
			case "screen": ShowScreen = on; break;
			case "automix": AutoMix = on; break;
			case "lock":
				if ( !Whitelist.IsStaff( Rpc.Caller ) )
				{
					SwrpNet.NotifyConnection( Rpc.Caller, "Only staff can lock the DJ booth.", true );
					return;
				}
				Locked = on;
				break;
		}
	}

	[Rpc.Host]
	public void RequestSetting( string key, float value )
	{
		if ( !MayDj( Rpc.Caller, out _ ) ) return;
		switch ( key )
		{
			case "master": Master = Math.Clamp( value, 0f, 1f ); break;
			case "mix": MixSeconds = Math.Clamp( value, 0f, 30f ); break;
			case "palette": Palette = Math.Clamp( (int)value, 0, ClubPalettes.Count - 1 ); break;
		}
	}

	/// <summary> A machine that loaded a deck tells the host how long the track is (needed for the auto-mix). </summary>
	[Rpc.Host]
	public void ReportDuration( int deck, string source, float seconds )
	{
		if ( deck is < 0 or > 1 || source != Source( deck ) || Duration( deck ) > 0f ) return;
		if ( !float.IsFinite( seconds ) || seconds < 5f || seconds > 4 * 3600f ) return;
		if ( deck == 0 ) ADuration = seconds;
		else BDuration = seconds;
	}

	// ----- state changes (host) -----

	static string Clean( string title )
	{
		title = title?.Trim();
		if ( string.IsNullOrEmpty( title ) ) return null;
		return title.Length > 80 ? title[..80] : title;
	}

	void Load( int deck, string source, string title, bool play )
	{
		if ( deck == 0 ) { ASource = source; ATitle = title; ADuration = 0f; }
		else { BSource = source; BTitle = title; BDuration = 0f; }
		SetDeck( deck, start: Now, paused: play ? -1f : 0f );

		// nothing on the other deck: send the crossfader over so the new track is heard straight away
		if ( play && !Loaded( 1 - deck ) && FadeSeconds <= 0f ) Crossfader = deck;
		// a YouTube video only shows (and sounds) from the live deck: cut straight to it
		if ( play && source.StartsWith( "youtube:" ) ) MixTo( deck, 0f );
	}

	void Eject( int deck )
	{
		if ( deck == 0 ) { ASource = null; ATitle = null; ADuration = 0f; APausedAt = -1f; }
		else { BSource = null; BTitle = null; BDuration = 0f; BPausedAt = -1f; }
	}

	void SetDeck( int deck, double? start = null, float? paused = null )
	{
		if ( deck == 0 )
		{
			if ( start is { } s ) AStart = s;
			if ( paused is { } p ) APausedAt = p;
		}
		else
		{
			if ( start is { } s ) BStart = s;
			if ( paused is { } p ) BPausedAt = p;
		}
	}

	void MixTo( int deck, float seconds )
	{
		var from = CrossfaderNow;
		if ( seconds <= 0.05f )
		{
			Crossfader = deck;
			FadeSeconds = 0f;
			return;
		}
		FadeFrom = from;
		FadeTo = deck;
		FadeStart = Now;
		FadeSeconds = seconds;
		Crossfader = deck;      // where it ends up
	}

	// ----- every frame on the host -----

	void HostTick()
	{
		if ( !_autoStarted )
		{
			_autoStarted = true;
			if ( AutoPlay && !Loaded( 0 ) && !Loaded( 1 ) )
			{
				Load( 0, "builtin:underlevel", DefaultTitle( "builtin:underlevel" ), true );
				Crossfader = 0f;
			}
		}

		if ( _clockSent > 1f )
		{
			_clockSent = 0;
			HostClock = LocalUnix;
		}

		// a finished fade: settle it
		if ( FadeSeconds > 0f && Now - FadeStart >= FadeSeconds )
		{
			Crossfader = FadeTo;
			FadeSeconds = 0f;
			if ( AutoMix )
			{
				// the auto-mix leaves the faded-out deck empty for the next track
				var off = Crossfader < 0.5f ? 1 : 0;
				if ( Loaded( off ) && Gain( off ) < 0.01f && ( Duration( off ) <= 0f || Position( off ) >= Duration( off ) - 0.5f ) ) Eject( off );
			}
		}

		var live = LiveDeck;
		if ( !Loaded( live ) || PausedAt( live ) >= 0f ) return;
		var dur = Duration( live );
		if ( dur <= 0f ) return;        // unknown or a live stream: the DJ moves on by hand
		var pos = Position( live );

		if ( AutoMix && FadeSeconds <= 0f )
		{
			var next = 1 - live;
			var lead = Math.Max( MixSeconds, 0.5f );
			if ( pos >= dur - lead )
			{
				var q = Queue;
				if ( q.Count > 0 && ( !Loaded( next ) || Gain( next ) < 0.01f ) )
				{
					var t = q[0];
					q.RemoveAt( 0 );
					QueueJson = Json.Serialize( q );
					Load( next, t.Source, t.Title, true );
					MixTo( next, MixSeconds );
					return;
				}
			}
		}

		// built-in loops go round forever; other tracks stop at the end
		if ( pos > dur + 1f && !IsLoop( Source( live ) ) ) Eject( live );
	}

	public static bool IsLoop( string source ) => source?.StartsWith( "builtin:" ) == true;
}
