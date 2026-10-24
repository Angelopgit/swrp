using System;

namespace Swrp;

public enum ChatKind
{
	Say,
	Whisper,
	Yell,
	Me,
	Do,
	Roll,
	Radio,
	Broadcast,
	Ooc,
	Looc,
	Staff,
	System,
	Dispatch,
}

public sealed class ChatLine
{
	public ChatKind Kind { get; init; }
	public string Text { get; init; }
	public RealTimeSince Age { get; set; }
}

/// <summary>
/// Roleplay chat. The host decides who is in range of every line, so a client can't read what it wasn't sent.
///
///   (text)            say, heard as far as your voice range (whisper / normal / yell)
///   /w  /whisper      whisper, very close range
///   /y  /yell         yell, long range
///   /me               local emote          * SGT Hardcase checks his rifle.
///   /do               local scene line     ** The door hisses open. (SGT Hardcase)
///   /roll [max]       local dice roll      * SGT Hardcase rolls 57 (1-100)
///   /radio  /r        your whole legion, any distance
///   /broadcast        whole ship (officers Captain and up, or staff)
///   /ooc  //          out of character, everyone
///   /looc             out of character, local
///   /examine          read the description of the trooper nearest you
///   /voice, /holster, /draw, /clear run on your own machine
/// Staff commands (/kick, /ban, ...) are handled by <see cref="SwrpStaff"/>.
/// </summary>
public static class SwrpChat
{
	public const float WhisperRange = 160f;
	public const float TalkRange = 700f;
	public const float YellRange = 2200f;
	const int MaxLength = 220;

	/// <summary> Index of Captain in the rank ladder; Captain and above may /broadcast. </summary>
	const int BroadcastRank = 8;

	public static readonly string[] VoiceNames = { "Whisper", "Normal", "Yell" };

	public static float RangeFor( int voiceMode ) => voiceMode switch
	{
		0 => WhisperRange,
		2 => YellRange,
		_ => TalkRange,
	};

	// ----- Client side state -----

	public static List<ChatLine> Lines { get; } = new();
	public static int Version { get; private set; }

	public static void Reset()
	{
		Lines.Clear();
		Version++;
	}

	public static void AddLocal( ChatKind kind, string text )
	{
		Lines.Add( new ChatLine { Kind = kind, Text = text, Age = 0 } );
		if ( Lines.Count > 80 ) Lines.RemoveAt( 0 );
		Version++;

		// A soft ping for real chat; system lines and staff logs stay quiet.
		if ( kind is not (ChatKind.System or ChatKind.Staff) && SwrpPlayer.Local is not null )
			SwrpAudio.Ui( SwrpAudio.ChatMessage );
	}

	public static void CycleVoice()
	{
		if ( SwrpPlayer.Local is not { } me ) return;
		me.VoiceMode = (me.VoiceMode + 1) % 3;
		AddLocal( ChatKind.System, $"Voice range: {VoiceNames[me.VoiceMode]}" );
	}

	/// <summary>
	/// Called by the chat box. A few commands only affect this machine; everything else goes to the host.
	/// </summary>
	public static void Submit( string text )
	{
		text = text?.Trim();
		if ( string.IsNullOrEmpty( text ) ) return;

		var me = SwrpPlayer.Local;
		var cmd = text.Split( ' ', 2 )[0].ToLowerInvariant();
		var rest = text.Length > cmd.Length ? text[cmd.Length..].Trim() : "";

		// the guide, rules, keys and directions run on this machine (SwrpGuide)
		if ( SwrpGuide.TryLocal( cmd, rest ) ) return;

		switch ( cmd )
		{
			case "/clear":
				Reset();
				return;

			case "/voice":
				if ( me is null ) return;
				var mode = Array.FindIndex( VoiceNames, n => n.StartsWith( rest, StringComparison.OrdinalIgnoreCase ) && rest.Length > 0 );
				if ( mode < 0 ) { CycleVoice(); return; }
				me.VoiceMode = mode;
				AddLocal( ChatKind.System, $"Voice range: {VoiceNames[mode]}" );
				return;

			case "/coms":
			case "/comms":
			case "/comlink":
				ComlinkPanel.Open = rest.Length == 0 ? !ComlinkPanel.Open : rest.Equals( "on", StringComparison.OrdinalIgnoreCase );
				AddLocal( ChatKind.System, ComlinkPanel.Open ? "Comlink open (/coms again to close). Radio traffic shows there." : "Comlink closed. Radio traffic shows in chat." );
				return;

			case "/holster":
			case "/draw":
				if ( me?.GetComponent<SwrpBlaster>() is { } blaster )
				{
					blaster.SetDrawn( cmd == "/draw" );
					AddLocal( ChatKind.System, blaster.Drawn ? "Weapon drawn." : "Weapon holstered." );
				}
				return;
		}

		SendToHost( text );
	}

	// ----- Client -> host -----

	[Rpc.Host]
	static void SendToHost( string text )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;

		text = Sanitize( text );
		if ( string.IsNullOrEmpty( text ) ) return;
		if ( !Whitelist.IsStaff( caller ) && !FloodOk( caller ) ) { SystemTo( caller, "Slow down: too many messages." ); return; }

		var cmd = text.StartsWith( '/' ) ? text.Split( ' ', 2 )[0].ToLowerInvariant() : null;
		var rest = cmd is not null && text.Length > cmd.Length ? text[cmd.Length..].Trim() : "";

		// Officers (Captain+) promote and demote within limits; admins go through the staff command below.
		if ( cmd is "/promote" or "/demote" && !Whitelist.IsAdmin( caller ) )
		{
			SwrpRanks.OfficerStep( caller, SwrpGameManager.Current?.FindPlayer( caller ), rest, cmd == "/promote" ? 1 : -1 );
			return;
		}

		if ( cmd is "/sc" or "/staffchat" && Whitelist.IsTrialStaff( caller ) )
		{
			if ( rest.Length > 0 ) StaffLog( $"{caller.DisplayName}: {rest}" );
			return;
		}

		// Staff commands work even when the sender is muted.
		if ( cmd is not null && SwrpStaff.TryRun( caller, cmd, rest ) )
			return;
		if ( cmd is not null && SwrpStaffTools.TryRun( caller, cmd, rest ) )
			return;

		// /report <player> <reason> for everyone; /report claim|close <id> for staff.
		if ( cmd == "/report" )
		{
			var sub = rest.Split( ' ', 2 );
			if ( sub[0] is "claim" or "close" or "list" && SwrpStaffTools.ReportStaffCommand( caller, sub[0], sub.Length > 1 ? sub[1] : "" ) ) return;
			SwrpStaffTools.FileReport( caller, rest );
			return;
		}

		if ( SwrpStaff.IsMuted( caller.SteamId.ValueUnsigned, out var muteLeft ) )
		{
			SystemTo( caller, muteLeft is null ? "You are muted." : $"You are muted for another {muteLeft}." );
			return;
		}

		var sender = SwrpGameManager.Current?.FindPlayer( caller );

		// careers (qualifications, class, cadet training, medals, leave, stats) and /where
		if ( cmd is not null && ( SwrpCareer.TryChat( caller, sender, cmd, rest ) || SwrpGuide.TryChat( caller, sender, cmd, rest ) ) )
			return;

		// Ship systems: staff can use them without a deployed character.
		if ( cmd is "/battalion" or "/bat" or "/roster" )
		{
			SwrpBattalions.Command( caller, sender, rest );
			return;
		}

		if ( cmd == "/gm" )
		{
			SwrpGameMaster.Command( caller, rest );
			return;
		}

		if ( cmd is "/hyperspace" or "/jump" )
		{
			SwrpSpace.Command( caller, rest );
			return;
		}

		if ( cmd == "/time" )
		{
			Tatooine.TatooineClock.Command( caller, rest );
			return;
		}

		if ( cmd == "/training" )
		{
			SwrpTrainingCourse.BoardCommand( caller, sender );
			return;
		}

		if ( cmd is "/alert" or "/defcon" or "/pa" )
		{
			if ( SwrpShip.Current is { } ship ) ship.Command( caller, sender, cmd, rest );
			else SystemTo( caller, "This map has no ship systems." );
			return;
		}

		switch ( cmd )
		{
			case "/ooc":
			case "//":
				if ( rest.Length == 0 ) return;
				SendTo( Connection.All, ChatKind.Ooc, $"[OOC] {caller.DisplayName}: {rest}" );
				return;

			case "/help":
				SystemTo( caller, "New here? F2 or /guide: first steps, every key, and /dir <place> for directions (/guide tour walks you round). /rules shows the server rules." );
				SystemTo( caller, "Career: /train (cadet checklist), /qual, /class, /medals, /stats, /loa <days>, /totw. Officers: /qual grant, /commend, /where <trooper>, /train pass." );
				SystemTo( caller, "Chat: /w /y /me /do /roll /radio /broadcast /ooc /looc /examine. Local: /voice /holster /draw /clear. B cycles voice range. E uses doors and consoles." );
				SystemTo( caller, "Doors (Captain+ or staff): /lock and /unlock the door you are looking at." );
				SystemTo( caller, "Ship: /alert shows the alert level (DEFCON 5 normal to 1 maximum); officers (2nd Lieutenant+) set it with /alert <1-5> [reason] and announce with /pa <text>, or use a bridge console. DEFCON 1 locks doors to 2nd Lieutenant+." );
				SystemTo( caller, "Battalion: L or /battalion opens your battalion's roster. Officers (2nd Lieutenant+) and the Commander, XO and Jedi General: /battalion add|remove|promote|demote|role|transfer, approve|deny transfers, /battalion log." );
				SystemTo( caller, "Bridge: E on the holo-map lists where every squad is. Simulation rooms: E on a course console starts a timed target run; /training shows the leaderboard." );
				SystemTo( caller, "Turbolifts: E on a lift door calls the car, E on the panel inside picks a deck (or press its number). Walk onto a lift pad for the bridge, the simulation rooms or the Jedi temple (top of the port turbolift)." );
				SystemTo( caller, "Jedi: no CT number, ranks Padawan to Master of the Council (/ranks); enlisting deploys you as the new character." );
				SystemTo( caller, "Combat: 1 primary, 2 sidearm (again for the next one you carry), H holster, Mouse2 aim, R reload, X stun setting. Any blaster you carry works, spawned or picked up: Equip it in the inventory (I) or double-click it." );
				SystemTo( caller, "Medical: downed at 0 HP - E calls a medic, hold K to give up. Hold E on a downed trooper to revive (Medics are faster; a Medic's defibrillator in hand brings them back at full health, the medkit in hand heals a wounded trooper to full). Bacta patch/injector/medkit from the inventory; E on a medbay bacta pod. /examine a body (medics and officers see the cause)." );
				SystemTo( caller, "Units: /squad (create, invite, accept, leave, kick, promote, disband, list), /platoon (Lieutenant+). Radio: /r [channel] <msg>, /channel squad|legion|command|open, /dispatch <channel> <msg> (Lieutenant+)." );
				SystemTo( caller, "Orders: /order <title> | <objective> | <rally or 'here'> | <priority> (Captain+), /order complete|fail|cancel. Hold Tab for the mission board. /briefing <title> | <text> at a holo-table (Lieutenant+). /ranklog (Lieutenant+). Captains can /promote and /demote below their rank." );
				SystemTo( caller, "Report a problem to staff: /report <player> <what happened>. /hyperspace says where the ship is." );
				SystemTo( caller, "Comms: hold V to talk, B voice range, Y puts your voice on the radio (your /channel net), N turns your comm receiver on or off. /coms opens or closes the comlink." );
				SystemTo( caller, "HUD: F8 hides it for screenshots, hold Tab to see every readout. Settings: swrp_visor 0/1 (helmet visor), swrp_hud_fade 0/1." );
				SystemTo( caller, "Armory: E on a rack takes a DC-15A (two at most), E on a crate takes blaster cells. Medbay: E on a bacta pod heals you fully." );
				SystemTo( caller, "Esc: the Escape menu (scoreboard, characters, settings, exit)." );
				SystemTo( caller, "Items: G drops the blaster in your hands, E picks up a dropped item, T throws a thermal detonator (3 s fuse). The main hangar control room's button opens the topside hangar doors. Your inventory shows each blaster's stats (accuracy, hip fire, fire rate, mobility, damage, stability, drop-off). Settings (console): swrp_ambient_volume 0-1, swrp_postfx 0/1 (cinematic look), swrp_cinematic 0/1 (head bob, shake, deploy fly-in, death camera), swrp_space_ships 0/1 (passing ships), swrp_ship_life 0/1 (steam, sparks, screens, crew), swrp_menu_music 0-1, swrp_thirdperson_distance/_side/_height, swrp_voxel_mode 0/1 (preview the voxel troopers and blasters)." );
				if ( Whitelist.IsTrialStaff( caller ) )
					foreach ( var line in SwrpStaff.HelpFor( Whitelist.GetRole( caller ) ).Split( '\n' ) )
						SystemTo( caller, line );
				if ( SwrpGameMaster.IsGameMaster( caller ) )
				{
					SystemTo( caller, $"Game Master: J or /gm opens the event menu. {SwrpGameMaster.Usage}" );
					SystemTo( caller, $"Hyperspace: /hyperspace <destination> [seconds in the tunnel, 4-60] jumps the ship ({SwrpSpace.List})." );
				}
				return;
		}

		if ( !sender.IsValid() )
		{
			SystemTo( caller, "Deploy a character before you speak in character." );
			return;
		}

		var name = $"{sender.Rank.Abbreviation} {sender.CharacterName}";

		switch ( cmd )
		{
			case null:
				SendTo( Nearby( sender, RangeFor( sender.VoiceMode ) ), sender.VoiceMode switch
				{
					0 => ChatKind.Whisper,
					2 => ChatKind.Yell,
					_ => ChatKind.Say,
				}, sender.VoiceMode switch
				{
					0 => $"{name} whispers: {text}",
					2 => $"{name} yells: {text}",
					_ => $"{name}: {text}",
				} );
				return;

			case "/lock":
			case "/unlock":
				SwrpDoor.LockCommand( caller, sender, cmd == "/lock" );
				return;

			case "/w":
			case "/whisper":
				if ( rest.Length == 0 ) return;
				SendTo( Nearby( sender, WhisperRange ), ChatKind.Whisper, $"{name} whispers: {rest}" );
				return;

			case "/y":
			case "/yell":
				if ( rest.Length == 0 ) return;
				SendTo( Nearby( sender, YellRange ), ChatKind.Yell, $"{name} yells: {rest}" );
				return;

			case "/me":
				if ( rest.Length == 0 ) { SystemTo( caller, "Usage: /me <action>" ); return; }
				SendTo( Nearby( sender, TalkRange ), ChatKind.Me, $"* {name} {rest}" );
				return;

			case "/do":
				if ( rest.Length == 0 ) { SystemTo( caller, "Usage: /do <what happens>" ); return; }
				SendTo( Nearby( sender, TalkRange ), ChatKind.Do, $"** {rest} ({name})" );
				return;

			case "/roll":
				var max = int.TryParse( rest, out var m ) ? Math.Clamp( m, 2, 1000 ) : 100;
				var roll = Game.Random.Next( 1, max + 1 );
				SendTo( Nearby( sender, TalkRange ), ChatKind.Roll, $"* {name} rolls {roll} (1-{max})" );
				return;

			case "/radio":
			case "/r":
			{
				if ( rest.Length == 0 ) { SystemTo( caller, $"Usage: /r <message> (on {sender.RadioChannel}) or /r <squad|legion|command|open> <message>. /channel switches." ); return; }
				var first = rest.Split( ' ', 2 );
				var named = first.Length == 2 && SwrpRoleplay.Channels.Contains( first[0].ToLowerInvariant() );
				SwrpRoleplay.Current?.Radio( caller, sender, named ? first[0] : null, named ? first[1] : rest, false );
				return;
			}

			case "/channel":
			case "/ch":
				SwrpRoleplay.Current?.ChannelCommand( caller, sender, rest );
				return;

			case "/dispatch":
			{
				if ( sender.RankIndex < SwrpRoleplay.LieutenantRank && !Whitelist.IsStaff( caller ) )
				{
					SystemTo( caller, "Dispatch is for Lieutenants and above (or staff)." );
					return;
				}
				var parts = rest.Split( ' ', 2 );
				if ( parts.Length < 2 || !SwrpRoleplay.Channels.Contains( parts[0].ToLowerInvariant() ) )
				{
					SystemTo( caller, "Usage: /dispatch <squad|legion|command|open> <message>" );
					return;
				}
				SwrpRoleplay.Current?.Radio( caller, sender, parts[0], parts[1], true );
				return;
			}

			case "/squad":
				SwrpRoleplay.Current?.SquadCommand( caller, sender, rest );
				return;

			case "/platoon":
				SwrpRoleplay.Current?.PlatoonCommand( caller, sender, rest );
				return;

			case "/order":
			case "/orders":
				SwrpRoleplay.Current?.OrderCommand( caller, sender, rest );
				return;

			case "/briefing":
				SwrpRoleplay.Current?.BriefingCommand( caller, sender, rest );
				return;

			case "/ranklog":
				SwrpRanks.ShowLog( caller, sender, rest );
				return;

			case "/broadcast":
			case "/bc":
				if ( rest.Length == 0 ) return;
				if ( sender.RankIndex < BroadcastRank && !Whitelist.IsStaff( caller ) )
				{
					SystemTo( caller, "Only Captains and above (or staff) can broadcast to the ship." );
					return;
				}
				SendTo( Connection.All, ChatKind.Broadcast, $"[SHIPWIDE] {name}: {rest}" );
				return;

			case "/looc":
				if ( rest.Length == 0 ) return;
				SendTo( Nearby( sender, TalkRange ), ChatKind.Looc, $"[LOOC] {caller.DisplayName}: {rest}" );
				return;

			case "/examine":
			case "/ex":
				Examine( caller, sender );
				return;

			default:
				SystemTo( caller, "Unknown command. Type /help for the list." );
				return;
		}
	}

	static void Examine( Connection caller, SwrpPlayer sender )
	{
		var target = sender.Scene.GetAllComponents<SwrpPlayer>()
			.Where( p => p != sender && p.CharacterId is not null )
			.OrderBy( p => p.WorldPosition.DistanceSquared( sender.WorldPosition ) )
			.FirstOrDefault();

		if ( !target.IsValid() || target.WorldPosition.Distance( sender.WorldPosition ) > 300f )
		{
			if ( !SwrpCorpses.Examine( caller, sender ) )
				SystemTo( caller, "Nobody close enough to examine." );
			return;
		}

		SystemTo( caller, $"{target.FullTitle}, {target.Faction.Name} ({target.Faction.AllegianceInfo?.Name ?? "unaligned"}), {target.Class.Name}." );
		SystemTo( caller, string.IsNullOrWhiteSpace( target.Description ) ? "No description." : target.Description );
		if ( SwrpCareer.ExamineLine( target ) is { } career ) SystemTo( caller, career );
	}

	/// <summary> Text from a player for a ship-wide line: no control characters, trimmed, at most maxLength. Never null. </summary>
	public static string Clean( string text, int maxLength )
	{
		text = Sanitize( text ) ?? "";
		return text.Length > maxLength ? text[..maxLength] : text;
	}

	static string Sanitize( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return null;

		text = new string( text.Where( c => !char.IsControl( c ) ).ToArray() ).Trim();
		return text.Length > MaxLength ? text[..MaxLength] : text;
	}

	public static IEnumerable<Connection> Nearby( SwrpPlayer sender, float range )
	{
		var origin = sender.WorldPosition;
		var r2 = range * range;

		foreach ( var other in sender.Scene.GetAllComponents<SwrpPlayer>() )
		{
			if ( other.Network.Owner is not { } owner ) continue;
			if ( other.WorldPosition.DistanceSquared( origin ) <= r2 )
				yield return owner;
		}
	}

	public static void SendTo( IEnumerable<Connection> connections, ChatKind kind, string text )
	{
		foreach ( var connection in connections.Where( c => c is not null ).Distinct().ToList() )
		{
			using ( Rpc.FilterInclude( connection ) )
			{
				Receive( (int)kind, text );
			}
		}
	}

	/// <summary> Host only. A grey system line for one person. </summary>
	public static void SystemTo( Connection connection, string text ) => SendTo( new[] { connection }, ChatKind.System, text );

	// Flood limit: 5 lines in any 3 s per player (staff exempt).
	static readonly Dictionary<Guid, List<RealTimeSince>> _sent = new();

	static bool FloodOk( Connection c )
	{
		if ( !_sent.TryGetValue( c.Id, out var list ) ) _sent[c.Id] = list = new();
		list.RemoveAll( t => t > 3f );
		if ( list.Count >= 5 ) return false;
		list.Add( 0 );
		return true;
	}

	/// <summary> Host only. A line every online staff member sees. </summary>
	public static void StaffLog( string text )
	{
		Log.Info( $"[SWRP STAFF] {text}" );
		SendTo( Connection.All.Where( Whitelist.IsTrialStaff ), ChatKind.Staff, $"[STAFF] {text}" );
	}

	// ----- Host -> client -----

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Receive( int kind, string text )
	{
		AddLocal( (ChatKind)kind, text );

		// Radio and dispatch traffic crackles, and lights the HUD radio indicator.
		if ( (ChatKind)kind is ChatKind.Radio or ChatKind.Dispatch )
		{
			SinceRadio = 0;
			var close = text.IndexOf( ']' );
			LastRadioTag = text.StartsWith( '[' ) && close > 1 ? text[1..close] : "RADIO";
			SwrpAudio.Ui( (ChatKind)kind == ChatKind.Dispatch ? SwrpAudio.Dispatch : SwrpAudio.RadioStatic );
		}
	}

	/// <summary> Client: time since the last radio or dispatch line arrived, and its channel tag, for the HUD. </summary>
	public static RealTimeSince SinceRadio { get; private set; } = 100;
	public static string LastRadioTag { get; private set; }
}
