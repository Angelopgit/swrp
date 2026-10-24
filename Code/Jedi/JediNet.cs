using System;

namespace Swrp.Jedi;

/// <summary> Client-to-host requests for the Jedi that don't belong to a spawned player (the character screen). </summary>
public static class JediNet
{
	/// <summary> Rebuild a Jedi character's lightsaber: only the saber parts are taken from <paramref name="appearance"/>. </summary>
	[Rpc.Host]
	public static void RequestSaber( string characterId, string appearance )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		var steamId = caller.SteamId.ValueUnsigned;
		var record = CharacterStore.Find( steamId, characterId );
		if ( record is null || !string.Equals( record.FactionId, "jedi", StringComparison.OrdinalIgnoreCase ) ) return;

		var look = Appearance.Parse( record.Appearance );
		var want = Appearance.Parse( appearance );
		look.SaberEmitter = want.SaberEmitter;
		look.SaberGrip = want.SaberGrip;
		look.SaberPommel = want.SaberPommel;
		look.SaberFinish = want.SaberFinish;
		look.SaberCrystal = want.SaberCrystal;
		look.SaberMode = want.SaberMode;
		look.SaberLength = want.SaberLength;
		record.Appearance = look.ToString();
		CharacterStore.Save( steamId );

		// deployed as that character: everyone sees the new saber at once
		var player = SwrpGameManager.Current?.FindPlayer( caller );
		if ( player.IsValid() && player.CharacterId == characterId ) player.Appearance = record.Appearance;

		SwrpNet.SendMenuState( caller );
		SwrpNet.NotifyConnection( caller, $"Lightsaber rebuilt: {look.Saber.CrystalDef.Name}, {SaberDesign.Modes[look.SaberMode].ToLowerInvariant()}.", false );
	}
}
