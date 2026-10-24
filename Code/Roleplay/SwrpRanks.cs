using System;

namespace Swrp;

/// <summary>
/// Rank changes: a persisted promotion log (swrp/ranklog.json on the host) and officer promotions with limits.
/// Admins promote anyone through the staff commands; officers (Captain+) can promote and demote troopers below their own
/// rank, never to their own rank or above; the Commander can promote anyone.
/// </summary>
public static class SwrpRanks
{
	public const string LogFile = "swrp/ranklog.json";
	public const int OfficerRank = 8;      // Captain
	public const int CommanderRank = 10;

	public sealed class Entry
	{
		public DateTime When { get; set; }
		public string By { get; set; }
		public ulong BySteamId { get; set; }
		public string Target { get; set; }
		public ulong TargetSteamId { get; set; }
		public string From { get; set; }
		public string To { get; set; }
		public string How { get; set; }
	}

	static List<Entry> _log;
	static List<Entry> Entries => _log ??= FileSystem.Data.ReadJson<List<Entry>>( LogFile, null ) ?? new();

	public static void Reset() => _log = null;

	/// <summary> Host: record a rank change. </summary>
	public static void Record( string by, ulong bySteam, CharacterRecord target, int fromIndex, int toIndex, string how )
	{
		if ( target is null || fromIndex == toIndex ) return;
		var ranks = target.Faction.Ranks;
		Entries.Add( new Entry
		{
			When = DateTime.UtcNow,
			By = by,
			BySteamId = bySteam,
			Target = target.FullTitle,
			TargetSteamId = target.OwnerSteamId,
			From = ranks[Math.Clamp( fromIndex, 0, ranks.Count - 1 )].Name,
			To = ranks[Math.Clamp( toIndex, 0, ranks.Count - 1 )].Name,
			How = how,
		} );
		if ( Entries.Count > 1000 ) Entries.RemoveRange( 0, Entries.Count - 1000 );

		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( LogFile, Entries );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not save the rank log: {e.Message}" );
		}
	}

	/// <summary> Host: record a battalion action that isn't a rank change (whitelisting, removal, roles, transfers, posts). </summary>
	public static void RecordNote( string by, ulong bySteam, string target, ulong targetSteam, string how )
	{
		Entries.Add( new Entry { When = DateTime.UtcNow, By = by, BySteamId = bySteam, Target = target, TargetSteamId = targetSteam, From = "", To = "", How = how } );
		if ( Entries.Count > 1000 ) Entries.RemoveRange( 0, Entries.Count - 1000 );
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( LogFile, Entries );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not save the rank log: {e.Message}" );
		}
	}

	/// <summary> Host: /ranklog [name] for Lieutenants and above and staff. </summary>
	public static void ShowLog( Connection caller, SwrpPlayer sender, string filter )
	{
		var staff = Whitelist.IsStaff( caller );
		if ( !staff && ( sender is null || sender.RankIndex < SwrpRoleplay.LieutenantRank ) )
		{
			SwrpChat.SystemTo( caller, "The rank log is for Lieutenants and above (and staff)." );
			return;
		}

		filter = filter?.Trim();
		var rows = Entries.Where( e => string.IsNullOrEmpty( filter ) || e.Target.Contains( filter, StringComparison.OrdinalIgnoreCase ) || e.By.Contains( filter, StringComparison.OrdinalIgnoreCase ) )
			.TakeLast( 10 ).ToList();
		if ( rows.Count == 0 ) { SwrpChat.SystemTo( caller, "No rank changes logged." ); return; }

		SwrpChat.SystemTo( caller, $"Rank log ({rows.Count} most recent{( string.IsNullOrEmpty( filter ) ? "" : $" matching '{filter}'" )}):" );
		foreach ( var e in rows )
			SwrpChat.SystemTo( caller, string.IsNullOrEmpty( e.From ) ? $"{e.When:yyyy-MM-dd HH:mm} UTC  {e.By}: {e.How}" : $"{e.When:yyyy-MM-dd HH:mm} UTC  {e.By} {e.How}: {e.Target} {e.From} -> {e.To}" );
	}

	/// <summary> Host: /promote or /demote from an officer who isn't an admin. </summary>
	public static void OfficerStep( Connection caller, SwrpPlayer officer, string args, int step )
	{
		if ( officer is null || officer.CharacterId is null )
		{
			SwrpChat.SystemTo( caller, "Deploy your character first." );
			return;
		}

		if ( officer.RankIndex < OfficerRank )
		{
			SwrpChat.SystemTo( caller, "Only Captains and above can promote or demote." );
			return;
		}

		var name = args?.Trim();
		if ( string.IsNullOrEmpty( name ) ) { SwrpChat.SystemTo( caller, $"Usage: /{( step > 0 ? "promote" : "demote" )} <player>" ); return; }

		var conn = SwrpNet.FindConnection( name );
		var target = conn is not null ? SwrpGameManager.Current?.FindPlayer( conn )
			: officer.Scene.GetAllComponents<SwrpPlayer>().FirstOrDefault( p => p.CharacterId is not null && p.CharacterName?.Contains( name, StringComparison.OrdinalIgnoreCase ) == true );
		if ( !target.IsValid() || target.CharacterId is null ) { SwrpChat.SystemTo( caller, $"No deployed trooper matches '{name}'." ); return; }
		if ( target == officer ) { SwrpChat.SystemTo( caller, "You can't change your own rank." ); return; }

		var record = CharacterStore.Find( target.OwnerSteamId, target.CharacterId );
		if ( record is null ) return;

		var commander = officer.RankIndex >= CommanderRank;
		if ( !commander && record.FactionId != officer.FactionId )
		{
			SwrpChat.SystemTo( caller, $"{target.CharacterName} is in another legion; only the Commander can change their rank." );
			return;
		}

		var ranks = record.Faction.Ranks;
		var from = record.RankIndex;
		var to = Math.Clamp( from + step, 0, ranks.Count - 1 );
		if ( to == from ) { SwrpChat.SystemTo( caller, $"{record.Name} is already {ranks[from].Name}." ); return; }

		if ( !commander && ( from >= officer.RankIndex || to >= officer.RankIndex ) )
		{
			SwrpChat.SystemTo( caller, $"A {officer.Rank.Name} can only promote or demote below their own rank (up to {ranks[Math.Max( 0, officer.RankIndex - 1 )].Name})." );
			return;
		}

		record.RankIndex = to;
		CharacterStore.Save( record.OwnerSteamId );
		target.RefreshFromStore();
		var by = $"{officer.Rank.Abbreviation} {officer.CharacterName}";
		Record( by, caller.SteamId.ValueUnsigned, record, from, to, step > 0 ? "promoted" : "demoted" );

		SwrpChat.SystemTo( caller, $"{record.Name} is now {ranks[to].Name}." );
		if ( target.Network.Owner is { } tc )
		{
			SwrpNet.NotifyConnection( tc, $"{by} {( step > 0 ? "promoted" : "demoted" )} you to {ranks[to].Name}.", step < 0 );
			SwrpNet.SendMenuState( tc );
		}

		SwrpChat.StaffLog( $"{by} ({caller.DisplayName}) {( step > 0 ? "promoted" : "demoted" )} {record.Name} to {ranks[to].Name}" );
	}
}
