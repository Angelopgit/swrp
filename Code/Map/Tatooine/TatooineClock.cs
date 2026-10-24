using System;

namespace Swrp.Tatooine;

/// <summary>
/// Tatooine's time of day. Everyone computes the same hour from the shared UTC clock (a full day takes
/// <see cref="DayMinutes"/> real minutes); a Game Master's /time shifts it for everybody through this host-synced
/// offset, so late joiners see the same sky. The host spawns one when the surface first loads.
/// </summary>
[Title( "SWRP Tatooine Clock" ), Category( "SWRP" ), Icon( "schedule" )]
public sealed class TatooineClock : Component
{
	[Sync( SyncFlags.FromHost )] public float HourOffset { get; set; }
	[Sync( SyncFlags.FromHost )] public float DayMinutes { get; set; } = 30f;
	[Sync( SyncFlags.FromHost )] public bool Frozen { get; set; }
	[Sync( SyncFlags.FromHost )] public float FrozenHour { get; set; } = 12f;

	/// <summary> The event site (the Separatist outpost) is up: its walls and towers show on every machine (/gm outpost). </summary>
	[Sync( SyncFlags.FromHost )] public bool SiteActive { get; set; }

	static TatooineClock _current;
	public static TatooineClock Current => _current.IsValid() ? _current : _current = Game.ActiveScene?.GetAllComponents<TatooineClock>().FirstOrDefault();

	/// <summary> Local fallback when there is no clock yet (or for a single machine's test). </summary>
	public static float? LocalOverride { get; set; }

	/// <summary> Hour of the day, 0..24 (noon = 12). </summary>
	public static float Hour
	{
		get
		{
			if ( LocalOverride is { } o ) return o;
			var c = Current;
			if ( c is { Frozen: true } ) return Mod24( c.FrozenHour );
			var minutes = c?.DayMinutes ?? 30f;
			var days = ( DateTime.UtcNow - DateTime.UnixEpoch ).TotalMinutes / Math.Max( 1f, minutes );
			return Mod24( (float)( days % 1.0 * 24.0 ) + ( c?.HourOffset ?? 0f ) );
		}
	}

	static float Mod24( float h ) => ( ( h % 24f ) + 24f ) % 24f;

	/// <summary> Host: make sure one synced clock exists. </summary>
	internal static void EnsureOnHost( Scene scene )
	{
		if ( !Networking.IsHost || Current.IsValid() ) return;
		var go = scene.CreateObject();
		go.Name = "Tatooine clock";
		go.Components.Create<TatooineClock>();
		go.NetworkSpawn();
	}

	/// <summary> Host: set the hour everyone sees now (the cycle carries on from there unless frozen). </summary>
	public static void SetHour( float hour, bool freeze )
	{
		var c = Current;
		if ( c is null || !Networking.IsHost ) return;
		c.Frozen = freeze;
		c.FrozenHour = Mod24( hour );
		if ( !freeze )
		{
			c.HourOffset = 0f;
			c.HourOffset = Mod24( hour - Hour );
		}
	}

	/// <summary> /time [hour 0-24 | dawn | noon | dusk | night] [freeze] , /time speed &lt;minutes per day&gt; (Game Masters). </summary>
	public static void Command( Connection caller, string rest )
	{
		var args = ( rest ?? "" ).Trim().ToLowerInvariant().Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var now = Hour;
		if ( args.Length == 0 || !SwrpGameMaster.IsGameMaster( caller ) )
		{
			SwrpChat.SystemTo( caller, $"Tatooine time: {FormatHour( now )}." + ( SwrpGameMaster.IsGameMaster( caller ) ? " GM: /time <0-24|dawn|noon|dusk|night> [freeze], /time speed <minutes per day>, /time run." : "" ) );
			return;
		}
		if ( Current is null ) { SwrpChat.SystemTo( caller, "Tatooine isn't loaded yet (the ship has to be there)." ); return; }
		if ( args[0] == "speed" && args.Length > 1 && float.TryParse( args[1], out var mins ) )
		{
			var keep = now;
			Current.DayMinutes = Math.Clamp( mins, 2f, 24f * 60f );
			SetHour( keep, Current.Frozen );
			SwrpChat.SystemTo( caller, $"GM: a Tatooine day now lasts {Current.DayMinutes:0} minutes." );
			return;
		}
		if ( args[0] == "run" ) { SetHour( now, false ); SwrpChat.SystemTo( caller, "GM: Tatooine's clock runs again." ); return; }
		float hour = args[0] switch { "dawn" => 6.2f, "morning" => 9f, "noon" => 12f, "afternoon" => 15f, "dusk" => 18.2f, "night" => 23f, "midnight" => 0f, _ => float.NaN };
		if ( float.IsNaN( hour ) && !float.TryParse( args[0], out hour ) ) { SwrpChat.SystemTo( caller, "Usage: /time <0-24|dawn|noon|dusk|night> [freeze]" ); return; }
		var freeze = args.Length > 1 && args[1] == "freeze";
		SetHour( hour, freeze );
		SwrpChat.StaffLog( $"[GM] {caller.DisplayName} set Tatooine's time to {FormatHour( hour )}{( freeze ? " (frozen)" : "" )}" );
		SwrpChat.SystemTo( caller, $"GM: Tatooine time set to {FormatHour( hour )}{( freeze ? ", frozen" : "" )}." );
	}

	public static string FormatHour( float h ) => $"{(int)h:00}:{(int)( h % 1f * 60f ):00}";

	[ConCmd( "swrp_tatooine_time", Help = "swrp_tatooine_time <hour 0-24> [1 = freeze] - host: set Tatooine's time for everyone (the /time chat command does the same)" )]
	public static void TimeCommand( float hour, int freeze = 0 )
	{
		if ( !Networking.IsHost ) { LocalOverride = hour; return; }
		SetHour( hour, freeze != 0 );
	}
}
