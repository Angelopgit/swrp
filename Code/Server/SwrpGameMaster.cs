using System;

namespace Swrp;

/// <summary>
/// Game Masters run events: spawn droids, send waves, set the ship's alert level, make announcements, clean up, and play
/// a CIS character themselves. Admins are always Game Masters; an admin can make a Moderator one with /gm grant (saved in the
/// whitelist file). Everything goes through /gm chat commands (the GM menu, J or /gm, types the same commands), and every
/// action is written to the staff log. Host side; the menu is <see cref="GmPanel"/>.
/// </summary>
public static class SwrpGameMaster
{
	public const string Usage = "/gm spawn <b1|b2|droideka|clone> [count] · /gm wave <type|mix> <per wave> <waves> <seconds> · /gm wave stop · /gm goto · /gm hunt · /gm hold · /gm alert <1-5> [reason] · /gm board [off] · /gm outpost [off] (Tatooine attack site) · /gm announce <text> · /gm become <b1|b2|commander|off> · /gm jedi max (your Jedi: every level, form and power, for testing) · /gm cleanup · /gm list · admins: /gm grant|revoke <player> · every staff member: /gm spawn, /gm list, /gm cleanup (or hold Q)";

	static readonly string[] Mix = { NpcRegistry.B1, NpcRegistry.B1, NpcRegistry.B1, NpcRegistry.B2, NpcRegistry.B1, NpcRegistry.Droideka };

	public static bool IsGameMaster( Connection c ) => c is not null && ( Whitelist.IsAdmin( c ) || Whitelist.IsStaff( c ) && Whitelist.IsGameMaster( c.SteamId.ValueUnsigned ) );

	/// <summary> Host: everything after /gm. </summary>
	public static void Command( Connection caller, string args )
	{
		var parts = ( args ?? "" ).Trim().Split( ' ', 2, StringSplitOptions.RemoveEmptyEntries );
		var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
		var rest = parts.Length > 1 ? parts[1].Trim() : "";

		if ( sub is "grant" or "revoke" )
		{
			GrantCommand( caller, sub == "grant", rest );
			return;
		}

		// every staff member may spawn NPCs (droids or friendly clones, from the Q spawn menu) and clean them up; the rest is
		// for Game Masters
		var staffMay = sub is "spawn" or "cleanup" or "clear" or "list" && Whitelist.IsStaff( caller );
		if ( !IsGameMaster( caller ) && !staffMay )
		{
			SwrpChat.SystemTo( caller, "Game Master tools are for Game Masters (admins, or staff an admin made one with /gm grant). Staff can /gm spawn, /gm list and /gm cleanup." );
			return;
		}

		var scene = Game.ActiveScene;
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		var who = caller.DisplayName;

		switch ( sub )
		{
			case "":
			case "menu":
				using ( Rpc.FilterInclude( caller ) ) OpenMenu();
				return;

			case "spawn":
			{
				var a = rest.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				var def = a.Length > 0 ? NpcRegistry.Get( a[0] ) : null;
				if ( def is null || def.EventOnly ) { SwrpChat.SystemTo( caller, $"Usage: /gm spawn <{SpawnableIds}> [count 1-10]" ); return; }
				var count = a.Length > 1 && int.TryParse( a[1], out var n ) ? Math.Clamp( n, 1, 10 ) : 1;
				if ( !Crosshair( caller, me, out var at, out var yaw ) ) return;
				var made = SwrpNpcDirector.Spawn( scene, def, at, yaw, count );
				if ( made.Count == 0 ) { SwrpChat.SystemTo( caller, $"NPC limit reached ({SwrpNpcDirector.MaxNpcs}); /gm cleanup or raise swrp_npc_max." ); return; }
				Note( caller, $"spawned {made.Count} {def.Name}{( made.Count == 1 ? "" : "s" )} at {Where( at )}{( made.Count < count ? $" (limit {SwrpNpcDirector.MaxNpcs})" : "" )}" );
				return;
			}

			case "wave":
			{
				if ( rest.Equals( "stop", StringComparison.OrdinalIgnoreCase ) )
				{
					var waves = scene.GetAllComponents<SwrpNpcWave>().ToList();
					foreach ( var w in waves ) w.GameObject.Destroy();
					Note( caller, $"stopped {waves.Count} wave spawner{( waves.Count == 1 ? "" : "s" )}" );
					return;
				}
				var a = rest.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
				var type = a.Length > 0 ? a[0].ToLowerInvariant() : "mix";
				var types = type == "mix" ? Mix.ToList() : NpcRegistry.Get( type ) is { EventOnly: false } d ? new List<string> { d.Id } : null;
				if ( types is null ) { SwrpChat.SystemTo( caller, $"Usage: /gm wave <{SpawnableIds}|mix> [per wave 1-10] [waves 1-20] [seconds between 10-600]" ); return; }
				var per = a.Length > 1 && int.TryParse( a[1], out var p1 ) ? Math.Clamp( p1, 1, 10 ) : 4;
				var count = a.Length > 2 && int.TryParse( a[2], out var p2 ) ? Math.Clamp( p2, 1, 20 ) : 3;
				var every = a.Length > 3 && float.TryParse( a[3], out var p3 ) ? Math.Clamp( p3, 10f, 600f ) : 45f;
				if ( !Crosshair( caller, me, out var at, out var yaw ) ) return;
				SwrpNpcDirector.EnsureNavMesh( scene, at );

				var go = new GameObject( true, "NPC Wave" );
				go.NetworkMode = NetworkMode.Never;
				go.Flags |= GameObjectFlags.NotSaved;
				go.WorldPosition = at;
				go.WorldRotation = Rotation.FromYaw( yaw );
				var wave = go.AddComponent<SwrpNpcWave>();
				wave.Types = types;
				wave.PerWave = per;
				wave.WavesLeft = count;
				wave.Interval = every;
				wave.StartedBy = who;
				Note( caller, $"started {count} wave{( count == 1 ? "" : "s" )} of {per} {( type == "mix" ? "mixed droids" : NpcRegistry.Get( type ).Name + "s" )} every {every:0} s at {Where( at )}" );
				return;
			}

			case "goto":
			{
				if ( !Crosshair( caller, me, out var at, out _ ) ) return;
				SwrpNpcDirector.EnsureNavMesh( scene, at );
				var n = 0;
				foreach ( var npc in SwrpNpcDirector.All( scene ).Where( x => !x.IsDead && ( rest.Length == 0 || x.NpcId == rest.ToLowerInvariant() ) ) )
				{
					npc.Objective = at;
					npc.Hunt = false;
					npc.Voice( npc.NpcId == NpcRegistry.B1 ? "rogerroger" : "moving" );
					n++;
				}
				Note( caller, $"ordered {n} NPCs to {Where( at )}" );
				return;
			}

			case "hunt":
			case "hold":
			{
				var n = 0;
				foreach ( var npc in SwrpNpcDirector.All( scene ).Where( x => !x.IsDead ) )
				{
					npc.Hunt = sub == "hunt";
					npc.Objective = sub == "hold" ? npc.WorldPosition : null;
					n++;
				}
				Note( caller, sub == "hunt" ? $"set {n} NPCs hunting the nearest enemies" : $"told {n} NPCs to hold position" );
				return;
			}

			case "alert":
			{
				var a = rest.Split( ' ', 2 );
				if ( SwrpShip.Current is not { } ship || !int.TryParse( a[0], out var level ) ) { SwrpChat.SystemTo( caller, "Usage: /gm alert <1-5> [reason]" ); return; }
				ship.SetAlert( caller, me, level, a.Length > 1 ? a[1] : "" );     // logs itself (staff)
				return;
			}

			case "board":
			{
				if ( SwrpShip.Current is not { } s3 ) { SwrpChat.SystemTo( caller, "This map has no ship systems." ); return; }
				var on = rest.ToLowerInvariant() is not ( "off" or "stop" or "leave" );
				if ( s3.BoardingShip == on ) { SwrpChat.SystemTo( caller, on ? "The boarding ship is already here (/gm board off sends it away)." : "There is no boarding ship here." ); return; }
				s3.BoardingShip = on;
				Note( caller, on ? "started a boarding attack: the enemy warship dropped out of hyperspace" : "sent the boarding ship away" );
				return;
			}

			case "outpost":
			{
				var off = rest.ToLowerInvariant() is "off" or "stop" or "clear";
				if ( Tatooine.TatooineClock.Current is not { } clock || !Tatooine.TatooinePlanet.Loaded ) { SwrpChat.SystemTo( caller, "The Tatooine outpost only exists while the ship is over Tatooine (/hyperspace tatooine)." ); return; }
				clock.SiteActive = !off;
				if ( off ) { Note( caller, "stood down the Tatooine outpost (its droids stay until /gm cleanup)" ); return; }
				var spawned = 0;
				foreach ( var (npc, at, yaw) in Tatooine.TatooinePlanet.Garrison )
					spawned += SwrpNpcDirector.Spawn( scene, NpcRegistry.Get( npc ), at, yaw, 1 ).Count;
				Note( caller, $"opened the Tatooine outpost with {spawned} droids (/gm outpost off hides it)" );
				return;
			}

			case "announce":
				if ( SwrpShip.Current is not { } s2 ) { SwrpChat.SystemTo( caller, "This map has no ship systems." ); return; }
				s2.Announce( caller, me, rest );     // logs itself (staff)
				return;

			case "become":
				Become( caller, me, rest.ToLowerInvariant() );
				return;

			case "jedi":
				// the lightsaber system's test switch (Code/Jedi): your own Jedi gets the whole training at once
				if ( Swrp.Jedi.JediCombat.GmMaxTraining( caller, me ) ) Note( caller, "maxed out their Jedi training for testing" );
				return;

			case "cleanup":
			case "clear":
			{
				var n = SwrpNpcDirector.CleanUp( scene );
				Note( caller, $"cleaned up {n} NPCs, every wave and all wreckage" );
				return;
			}

			case "list":
			{
				var alive = SwrpNpcDirector.All( scene ).Where( x => !x.IsDead ).GroupBy( x => x.Def.Name ).Select( g => $"{g.Count()} {g.Key}" );
				var waves = scene.GetAllComponents<SwrpNpcWave>().Count();
				SwrpChat.SystemTo( caller, $"NPCs: {string.Join( ", ", alive.DefaultIfEmpty( "none" ) )} (limit {SwrpNpcDirector.MaxNpcs}); wave spawners: {waves}." );
				return;
			}

			default:
				// event night tools: bring, event spawn, objective, timer, layouts, music (SwrpEvents)
				if ( SwrpEvents.Command( caller, me, sub, rest ) ) return;
				SwrpChat.SystemTo( caller, Usage );
				SwrpChat.SystemTo( caller, SwrpEvents.Usage );
				return;
		}
	}

	static string SpawnableIds => string.Join( "|", NpcRegistry.All.Where( n => !n.EventOnly ).Select( n => n.Id ) );

	static string Where( Vector3 p ) => $"{SwrpShip.DeckName( p )} ({p.x:0} {p.y:0} {p.z:0})";

	internal static void Note( Connection caller, string text )
	{
		SwrpChat.StaffLog( $"[GM] {caller.DisplayName} {text}" );
		SwrpStaffTools.RecordText( caller, "gm", text );
		SwrpChat.SystemTo( caller, $"GM: {text}." );
	}

	/// <summary> Where the Game Master is aiming: the floor under their crosshair, pulled back a little from walls. </summary>
	internal static bool Crosshair( Connection caller, SwrpPlayer me, out Vector3 at, out float yaw )
	{
		at = default;
		yaw = 0;
		if ( !me.IsValid() || me.CharacterId is null || !me.Controller.IsValid() )
		{
			SwrpChat.SystemTo( caller, "Deploy a character first (noclip is fine) so the server can see where you are aiming." );
			return false;
		}
		var eye = me.Controller.EyePosition;
		var fwd = me.Controller.EyeAngles.ToRotation().Forward;
		var tr = me.Scene.Trace.Ray( eye, eye + fwd * 8000f ).IgnoreGameObjectHierarchy( me.GameObject ).WithoutTags( "trigger", "npc", "ragdoll" ).Run();
		if ( !tr.Hit )
		{
			SwrpChat.SystemTo( caller, "Aim at the floor where they should appear." );
			return false;
		}
		at = tr.EndPosition + tr.Normal * ( tr.Normal.z > 0.6f ? 2f : 40f );
		// face back towards the Game Master
		yaw = ( eye - at ).WithZ( 0 ).EulerAngles.yaw;
		return true;
	}

	// ----- event characters -----

	static void Become( Connection caller, SwrpPlayer me, string args )
	{
		// /gm become <type> [health]: an optional health total (1-5000) for a boss or a fragile scout
		var words = ( args ?? "" ).Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var id = words.Length > 0 ? words[0] : "";
		float? health = words.Length > 1 && float.TryParse( words[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h ) ? Math.Clamp( h, 1f, 5000f ) : null;
		if ( !me.IsValid() || me.CharacterId is null )
		{
			SwrpChat.SystemTo( caller, "Deploy a character first; the event character replaces it until /gm become off." );
			return;
		}
		if ( id is "off" or "" or "clone" )
		{
			if ( string.IsNullOrEmpty( me.EventCharacter ) ) { SwrpChat.SystemTo( caller, "You are not playing an event character." ); return; }
			EndEventCharacter( me, "stepped out" );
			return;
		}
		var def = NpcRegistry.Get( id );
		if ( def is null || def.Style == NpcStyle.Roller || def.Allegiance?.Id == AllegianceRegistry.Gar )
		{
			SwrpChat.SystemTo( caller, "Usage: /gm become <b1|b2|commander|off> [health 1-5000]" );
			return;
		}

		me.EventCharacter = def.Id;
		var total = health ?? def.Health + def.Armor;
		if ( me.GetComponent<SwrpVitals>() is { } v )
		{
			// health up to the usual 100, anything above that as armour (so the HUD bars still read right)
			v.Health = MathF.Min( 100f, total );
			v.MaxArmor = MathF.Max( 0f, total - 100f );
			v.Armor = v.MaxArmor;
		}
		SwrpChat.StaffLog( $"[GM] {caller.DisplayName} is playing a {def.Name} ({me.CharacterName}, {total:0} health)" );
		SwrpStaffTools.RecordText( caller, "gm become", $"{def.Name} ({total:0} health)" );
		SwrpChat.SystemTo( caller, $"You are now a {def.Name} with {total:0} health. Clones see you as an enemy; droids as one of their own. /gm become off to stop." );
	}

	/// <summary> Host: back to their own character (on /gm become off, or when the event character dies). </summary>
	public static void EndEventCharacter( SwrpPlayer p, string why )
	{
		if ( string.IsNullOrEmpty( p.EventCharacter ) ) return;
		var name = p.EventDef?.Name ?? p.EventCharacter;
		p.EventCharacter = null;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: false } v )
		{
			v.Health = MathF.Min( v.Health, 100f );
			v.MaxArmor = p.Class?.MaxArmor ?? 0f;
			v.Armor = MathF.Min( v.Armor, v.MaxArmor );
		}
		SwrpChat.StaffLog( $"[GM] {p.Network.Owner?.DisplayName ?? p.CharacterName} {why} as a {name}" );
	}

	// ----- granting the role -----

	static void GrantCommand( Connection caller, bool grant, string target )
	{
		if ( !Whitelist.IsAdmin( caller ) )
		{
			SwrpChat.SystemTo( caller, "Only admins can make Game Masters." );
			return;
		}
		if ( !SwrpNet.TryResolveSteamId( target, out var steamId, out var label ) )
		{
			SwrpChat.SystemTo( caller, $"No player or SteamID matches '{target}'." );
			return;
		}
		if ( grant && Whitelist.GetRole( steamId ) < StaffRole.Moderator )
		{
			SwrpChat.SystemTo( caller, $"{label} isn't staff. Make them a Moderator first; Game Master is a flag on staff." );
			return;
		}
		Whitelist.SetGameMaster( steamId, grant );
		SwrpChat.StaffLog( $"[GM] {caller.DisplayName} {( grant ? "made" : "removed" )} {label} {( grant ? "a" : "as" )} Game Master" );
		SwrpStaffTools.Record( caller, grant ? "gm grant" : "gm revoke", $"{steamId} {label}" );
		SwrpChat.SystemTo( caller, $"{label} is {( grant ? "now" : "no longer" )} a Game Master." );
	}

	/// <summary> Client: open the GM menu (the host already checked). </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void OpenMenu() => SwrpUiState.GmOpen = true;

	/// <summary> Client: does the host say I may use the GM menu? Answered with <see cref="GmPanel.Allowed"/>. </summary>
	[Rpc.Host]
	public static void RequestAccess()
	{
		var caller = Rpc.Caller;
		using ( Rpc.FilterInclude( caller ) ) ReceiveAccess( IsGameMaster( caller ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveAccess( bool allowed )
	{
		GmPanel.Allowed = allowed;
		if ( !allowed ) SwrpUiState.GmOpen = false;
	}

	// ----- test helpers (host console) -----

	[ConCmd( "swrp_npc_spawn", Help = "swrp_npc_spawn <type> [count] [x y z] - host only: spawn NPCs at a point (default: in front of the camera)" )]
	public static void ConsoleSpawn( string type, int count = 1, float x = float.NaN, float y = float.NaN, float z = float.NaN )
	{
		if ( !Networking.IsHost || Game.ActiveScene is not { } scene ) return;
		var def = NpcRegistry.Get( type );
		if ( def is null || def.EventOnly ) { Log.Info( $"[SWRP] types: {SpawnableIds}" ); return; }
		Vector3 at;
		float yaw;
		if ( !float.IsNaN( z ) ) { at = new Vector3( x, y, z ); yaw = 0; }
		else
		{
			var cam = scene.Camera;
			var tr = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 4000f ).WithoutTags( "npc", "trigger" ).Run();
			at = tr.EndPosition + tr.Normal * 4f;
			yaw = ( cam.WorldPosition - at ).WithZ( 0 ).EulerAngles.yaw;
		}
		var made = SwrpNpcDirector.Spawn( scene, def, at, yaw, Math.Clamp( count, 1, 20 ) );
		Log.Info( $"[SWRP] spawned {made.Count} {def.Name} at {at.x:0},{at.y:0},{at.z:0}" );
	}

	[ConCmd( "swrp_npc_wave", Help = "swrp_npc_wave <type|mix> [per wave] [waves] [seconds] - host only: start a wave spawner in front of the camera" )]
	public static void ConsoleWave( string type = "mix", int per = 4, int waves = 2, float every = 20f )
	{
		if ( !Networking.IsHost || Game.ActiveScene is not { } scene ) return;
		var cam = scene.Camera;
		var tr = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 4000f ).WithoutTags( "npc", "trigger" ).Run();
		var types = type == "mix" ? Mix.ToList() : NpcRegistry.Get( type ) is { EventOnly: false } d ? new List<string> { d.Id } : null;
		if ( types is null ) { Log.Info( $"[SWRP] types: {SpawnableIds}, mix" ); return; }
		var go = new GameObject( true, "NPC Wave" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = tr.EndPosition + tr.Normal * 4f;
		var w = go.AddComponent<SwrpNpcWave>();
		w.Types = types;
		w.PerWave = Math.Clamp( per, 1, 10 );
		w.WavesLeft = Math.Clamp( waves, 1, 20 );
		w.Interval = Math.Clamp( every, 5f, 600f );
		w.StartedBy = "console";
		SwrpNpcDirector.EnsureNavMesh( scene, go.WorldPosition );
		Log.Info( $"[SWRP] wave spawner at {go.WorldPosition}" );
	}

	[ConCmd( "swrp_npc_clear", Help = "swrp_npc_clear - host only: remove every NPC, wave and wreck" )]
	public static void ConsoleClear()
	{
		if ( !Networking.IsHost || Game.ActiveScene is not { } scene ) return;
		Log.Info( $"[SWRP] removed {SwrpNpcDirector.CleanUp( scene )} NPCs" );
	}

	[ConCmd( "swrp_npc_info", Help = "swrp_npc_info - print every NPC: type, health, state" )]
	public static void ConsoleInfo()
	{
		if ( Game.ActiveScene is not { } scene ) return;
		foreach ( var n in SwrpNpcDirector.All( scene ) )
			Log.Info( $"[SWRP] {n.DisplayName} {( n.IsDead ? "DEAD" : $"hp {n.Health:0} armor {n.Armor:0} shield {n.Shield:0}" )}{( n.Stunned ? " stunned" : "" )}{( n.Deployed ? " deployed" : "" )} at {n.WorldPosition.x:0},{n.WorldPosition.y:0},{n.WorldPosition.z:0}" );
	}
}
