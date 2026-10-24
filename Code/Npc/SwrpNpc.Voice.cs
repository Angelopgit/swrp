using System;

namespace Swrp;

/// <summary>
/// Battle droids talk (Angelo, 2026-10-21: "Roger roger"): Star Wars Battlefront II's own B1 and B2 lines, from his copy of
/// the game (Assets/sounds/npc_vo, LOCAL ONLY, see LOCAL_ONLY.txt there). Each squad callout has its line (a B1 moving off is
/// "Roger, roger"), and they call out when hit, when they drop an enemy, throwing a detonator, and B1s scream as they go down.
/// One droid of a squad speaks at a time, so a squad doesn't answer in chorus. Clones keep their text callouts only.
/// </summary>
public sealed partial class SwrpNpc
{
	[ConVar( "swrp_npc_voice", Help = "Battle droid NPCs speak their Battlefront II lines", Saved = true )]
	public static bool VoiceOn { get; set; } = true;

	TimeSince _sinceVoice = 100;
	static Dictionary<int, float> _squadVoice;

	string VoiceSet => NpcId == NpcRegistry.B1 ? "b1" : NpcId == NpcRegistry.B2 ? "b2" : null;

	static string VoiceFor( string callout ) => callout switch
	{
		"Contact!" => "contact",
		"Moving!" => "moving",
		"Check fire!" => "checkfire",
		"Man down!" => "mandown",
		"Clear! Form up!" => "clear",
		_ => null,
	};

	/// <summary>
	/// Host: say a line of the kind (contact, moving, mandown, enemydown, hit, grenade, checkfire, clear, behind, explosion,
	/// death, rogerroger), if this droid has one and nobody in its squad has just spoken. <paramref name="urgent"/> lines (being
	/// hit, dying) ignore the squad's turn.
	/// </summary>
	public void Voice( string kind, bool urgent = false )
	{
		if ( !VoiceOn || kind is null || VoiceSet is not { } set || IsDead && kind != "death" ) return;
		if ( !urgent && _sinceVoice < 3f ) return;
		_squadVoice ??= new();
		if ( !urgent && SquadId != 0 && _squadVoice.TryGetValue( SquadId, out var last ) && Time.Now - last < 2.5f ) return;
		if ( set == "b2" && kind is "death" or "rogerroger" ) return;      // (B2s have neither in Battlefront II)
		_sinceVoice = 0;
		if ( SquadId != 0 ) _squadVoice[SquadId] = Time.Now;
		SayLine( $"sounds/npc_vo/{set}/{kind}.sound" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SayLine( string sound )
	{
		var at = WorldPosition + Vector3.Up * 60f * WorldScale.z;
		var h = Sound.Play( sound, at );
		if ( h is not null ) h.Parent = GameObject;      // (the voice moves with the droid)
	}
}
