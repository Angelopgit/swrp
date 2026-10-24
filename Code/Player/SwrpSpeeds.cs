namespace Swrp;

/// <summary>
/// One speed table for troopers and riflemen NPCs, in metres a second at trooper scale (a clone is 72 units, 1.83 m; 1 unit
/// is an inch). Players walk (Alt), jog (the default move), sprint (Shift, only a little faster than the jog) and crouch.
/// NPC riflemen walk on patrol, jog when advancing and sprint only to dash for cover (Angelo, 2026-10-21: NPC clones "run and
/// move too fast", players "too slow"). The GMod sets' playback follows the speed (SwrpEmotes.GmodPace), so the feet keep
/// matching the ground at any of these. Each tier is a saved convar.
/// </summary>
public static class SwrpSpeeds
{
	[ConVar( "swrp_speed_walk", Help = "Players: Alt slow walk (m/s)", Saved = true )] public static float Walk { get; set; } = 1.9f;
	[ConVar( "swrp_speed_jog", Help = "Players: normal move, a jog (m/s)", Saved = true )] public static float Jog { get; set; } = 4.4f;
	[ConVar( "swrp_speed_sprint", Help = "Players: sprint (m/s), a little over the jog", Saved = true )] public static float Sprint { get; set; } = 5.3f;
	[ConVar( "swrp_speed_crouch", Help = "Players: crouched (m/s)", Saved = true )] public static float Crouch { get; set; } = 1.45f;
	[ConVar( "swrp_speed_npc_patrol", Help = "NPC riflemen: walking on patrol and in formation (m/s)", Saved = true )] public static float NpcPatrol { get; set; } = 1.9f;
	[ConVar( "swrp_speed_npc_advance", Help = "NPC riflemen: jogging when advancing or chasing (m/s)", Saved = true )] public static float NpcAdvance { get; set; } = 3.8f;
	[ConVar( "swrp_speed_npc_dash", Help = "NPC riflemen: sprinting, only to dash for cover (m/s)", Saved = true )] public static float NpcDash { get; set; } = 5.0f;

	/// <summary> Units a second for metres a second at the trooper scale (swrp_player_scale 1.06 = true size). </summary>
	public static float U( float metresPerSecond ) => metresPerSecond * 39.37f * ( SwrpPlayer.PlayerScale / 1.06f );

	/// <summary> Changes when any tier does, so whoever applies them can pick up a convar change live. </summary>
	public static int Version => System.HashCode.Combine( Walk, Jog, Sprint, Crouch, SwrpPlayer.PlayerScale );
}
