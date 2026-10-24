namespace Swrp;

/// <summary> Dev helpers for checking NPC bodies without deploying a trooper. </summary>
public static class SwrpNpcDev
{
	[ConCmd( "swrp_npc_kill", Help = "swrp_npc_kill [count] - host, dev: kill the NPCs nearest the camera (checks corpses and ragdolls)" )]
	public static void KillCommand( int count = 1 )
	{
		var scene = Game.ActiveScene;
		if ( !Networking.IsHost || scene?.Camera is not { } cam ) return;
		foreach ( var npc in scene.GetAllComponents<SwrpNpc>().Where( n => !n.IsDead ).OrderBy( n => n.WorldPosition.Distance( cam.WorldPosition ) ).Take( count ).ToList() )
			npc.TakeHit( 100000f, null, "dev", cam.WorldPosition, "dev", false, false, 0f );
	}
}
