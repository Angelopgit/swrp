using System;
using System.Text.Json.Serialization;

namespace Swrp;

/// <summary>
/// The Game Master's event night tools, on top of <see cref="SwrpGameMaster"/>: bring a whole battalion or squad, an event
/// spawn point that redeploys troopers at the event instead of the medbay, an objective and countdown on everyone's HUD,
/// saved event layouts (props and NPCs) and event music. Host side apart from the HUD state and the music, which every
/// client keeps. All of it goes through /gm, so it is logged like the rest.
///
/// /gm bring &lt;all|battalion|squad name&gt; · /gm eventspawn set [battalion|all] · /gm eventspawn clear ·
/// /gm objective &lt;text&gt; · /gm objective clear · /gm timer &lt;minutes&gt; [label] · /gm timer stop ·
/// /gm layout save|load|list|delete &lt;name&gt; · /gm music &lt;track|stop&gt;
/// </summary>
public static class SwrpEvents
{
	public const string Usage = "/gm bring <all|battalion|squad> · /gm eventspawn set [battalion|all] · /gm eventspawn clear · /gm objective <text>|clear · /gm timer <minutes> [label] · /gm timer stop · /gm layout save|load|list|delete <name> · /gm music <" + "theme|underlevel|hyperlane|outer_rim|kessel|stop>";

	static readonly string[] Tracks = { "theme", "underlevel", "hyperlane", "outer_rim", "kessel" };

	// ----- host state -----

	static Vector3? _spawnAt;
	static float _spawnYaw;
	static string _spawnFor;          // battalion id, or null for everyone
	static string _objective;
	static string _timerLabel;
	static RealTimeUntil _timerEnds;
	static bool _timerOn;
	static string _music;

	public static void Reset()
	{
		_spawnAt = null;
		_objective = null;
		_timerOn = false;
		_music = null;
	}

	/// <summary> Host: where a trooper of that battalion deploys while an event spawn is set (null: the usual spawn). </summary>
	public static Transform? SpawnOverride( CharacterRecord record )
	{
		if ( _spawnAt is not { } at ) return null;
		if ( _spawnFor is not null && record?.FactionId != _spawnFor ) return null;
		// spread a little so a squad doesn't stack
		var r = Game.Random;
		var offset = new Vector3( r.Float( -60f, 60f ), r.Float( -60f, 60f ), 0f );
		var spot = at + offset;
		var scene = Game.ActiveScene;
		if ( scene is not null && scene.Trace.Ray( at + Vector3.Up * 40f, spot + Vector3.Up * 40f ).WithoutTags( "player", "trigger" ).Run().Hit ) spot = at;
		return new Transform( spot + Vector3.Up * 4f, Rotation.FromYaw( _spawnYaw ) );
	}

	/// <summary> Host: everything after /gm that the Game Master code doesn't know. True when handled. </summary>
	public static bool Command( Connection caller, SwrpPlayer me, string sub, string rest )
	{
		switch ( sub )
		{
			case "bring":
				Bring( caller, me, rest );
				return true;
			case "eventspawn":
			case "spawnpoint":
				EventSpawn( caller, me, rest );
				return true;
			case "objective":
			case "obj":
				Objective( caller, rest );
				return true;
			case "timer":
				Timer( caller, rest );
				return true;
			case "layout":
				Layout( caller, me, rest );
				return true;
			case "music":
				Music( caller, rest );
				return true;
			case "help":
				SwrpChat.SystemTo( caller, SwrpGameMaster.Usage );
				SwrpChat.SystemTo( caller, Usage );
				return true;
		}
		return false;
	}

	// ----- bring -----

	static void Bring( Connection caller, SwrpPlayer me, string rest )
	{
		var what = rest.Trim();
		if ( what.Length == 0 ) { SwrpChat.SystemTo( caller, "Usage: /gm bring <all|battalion id|squad name>" ); return; }
		if ( !SwrpGameMaster.Crosshair( caller, me, out var at, out _ ) ) return;

		var everyone = SwrpPlayers.Deployed.Where( p => p != me && !p.Jailed && !p.InVehicle && !p.InPod && p.GetComponent<SwrpVitals>() is not { IsDead: true } );
		List<SwrpPlayer> group;
		string label;
		if ( what.Equals( "all", StringComparison.OrdinalIgnoreCase ) )
		{
			group = everyone.ToList();
			label = "everyone";
		}
		else if ( FactionRegistry.All.FirstOrDefault( f => f.Id.Equals( what, StringComparison.OrdinalIgnoreCase ) || f.ShortName.Equals( what, StringComparison.OrdinalIgnoreCase ) ) is { } f )
		{
			group = everyone.Where( p => p.FactionId == f.Id ).ToList();
			label = f.Name;
		}
		else if ( SwrpRoleplay.Current?.State.Squads.FirstOrDefault( s => s.Name.Equals( what, StringComparison.OrdinalIgnoreCase ) ) is { } squad )
		{
			group = everyone.Where( p => squad.Members.Contains( p.OwnerSteamId ) ).ToList();
			label = $"squad {squad.Name}";
		}
		else
		{
			SwrpChat.SystemTo( caller, $"No battalion or squad called '{what}'. Battalions: {string.Join( ", ", FactionRegistry.All.Select( x => x.Id ) )}." );
			return;
		}
		if ( group.Count == 0 ) { SwrpChat.SystemTo( caller, $"Nobody to bring from {label}." ); return; }

		var slots = Slots( me.Scene, at, group.Count );
		var yaw = me.Controller.EyeAngles.yaw + 180f;
		for ( var i = 0; i < group.Count; i++ )
		{
			SwrpStaff.Remember( group[i] );
			group[i].TeleportTo( slots[i % slots.Count], yaw );
			if ( group[i].Network.Owner is { } c ) SwrpNet.NotifyConnection( c, "A Game Master brought you to the event. Staff can /return you.", false );
		}
		SwrpGameMaster.Note( caller, $"brought {group.Count} ({label}) to {SwrpShip.DeckName( at )}" );
	}

	/// <summary> Free floor spots in rings round a point, out of sight of nothing (a wall between drops the spot). </summary>
	static List<Vector3> Slots( Scene scene, Vector3 at, int count )
	{
		var list = new List<Vector3>();
		var centre = at + Vector3.Up * 40f;
		for ( var ring = 0; ring < 6 && list.Count < count; ring++ )
		{
			var radius = 70f + ring * 60f;
			var n = Math.Max( 6, (int)( radius * MathF.PI * 2f / 60f ) );
			for ( var k = 0; k < n && list.Count < count; k++ )
			{
				var a = k * MathF.PI * 2f / n;
				var spot = at + new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0f ) * radius;
				if ( scene.Trace.Ray( centre, spot + Vector3.Up * 40f ).WithoutTags( "player", "trigger", "npc", "ragdoll" ).Run().Hit ) continue;
				var down = scene.Trace.Ray( spot + Vector3.Up * 40f, spot - Vector3.Up * 120f ).WithoutTags( "player", "trigger", "npc", "ragdoll" ).Run();
				if ( !down.Hit ) continue;
				list.Add( down.EndPosition + Vector3.Up * 4f );
			}
		}
		if ( list.Count == 0 ) list.Add( at + Vector3.Up * 4f );
		return list;
	}

	// ----- event spawn -----

	static void EventSpawn( Connection caller, SwrpPlayer me, string rest )
	{
		var (sub, who) = SwrpStaff.Split( rest );
		if ( sub is "clear" or "off" )
		{
			_spawnAt = null;
			SwrpGameMaster.Note( caller, "cleared the event spawn point" );
			return;
		}
		if ( sub != "set" ) { SwrpChat.SystemTo( caller, _spawnAt is null ? "No event spawn. /gm eventspawn set [battalion|all] where you aim." : $"Event spawn at {SwrpShip.DeckName( _spawnAt.Value )} for {_spawnFor ?? "everyone"}. /gm eventspawn clear ends it." ); return; }
		if ( !SwrpGameMaster.Crosshair( caller, me, out var at, out _ ) ) return;
		string battalion = null;
		if ( who.Length > 0 && !who.Equals( "all", StringComparison.OrdinalIgnoreCase ) )
		{
			battalion = FactionRegistry.All.FirstOrDefault( f => f.Id.Equals( who, StringComparison.OrdinalIgnoreCase ) || f.ShortName.Equals( who, StringComparison.OrdinalIgnoreCase ) )?.Id;
			if ( battalion is null ) { SwrpChat.SystemTo( caller, $"Unknown battalion '{who}'." ); return; }
		}
		_spawnAt = at;
		_spawnYaw = me.Controller.EyeAngles.yaw;
		_spawnFor = battalion;
		SwrpGameMaster.Note( caller, $"set the event spawn at {SwrpShip.DeckName( at )} for {battalion ?? "everyone"} (deaths and new deploys come back here)" );
	}

	// ----- objective and timer -----

	static void Objective( Connection caller, string rest )
	{
		var text = SwrpChat.Clean( rest, 140 );
		if ( text.Length == 0 ) { SwrpChat.SystemTo( caller, $"Objective: {_objective ?? "none"}. /gm objective <text> or /gm objective clear." ); return; }
		_objective = text.Equals( "clear", StringComparison.OrdinalIgnoreCase ) ? null : text;
		SwrpGameMaster.Note( caller, _objective is null ? "cleared the objective" : $"set the objective: {_objective}" );
		BroadcastState();
	}

	static void Timer( Connection caller, string rest )
	{
		var (first, label) = SwrpStaff.Split( rest );
		if ( first is "stop" or "clear" or "off" )
		{
			_timerOn = false;
			SwrpGameMaster.Note( caller, "stopped the event timer" );
			BroadcastState();
			return;
		}
		if ( !float.TryParse( first, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes ) || minutes <= 0 || minutes > 180 )
		{
			SwrpChat.SystemTo( caller, "Usage: /gm timer <minutes, up to 180> [label] · /gm timer stop" );
			return;
		}
		_timerOn = true;
		_timerEnds = minutes * 60f;
		_timerLabel = label.Length > 0 ? SwrpChat.Clean( label, 40 ) : "EVENT";
		SwrpGameMaster.Note( caller, $"started a {minutes:0.#} minute timer ({_timerLabel})" );
		BroadcastState();
	}

	static void BroadcastState()
	{
		foreach ( var c in Connection.All ) SendState( c );
	}

	/// <summary> Host: the HUD's event state to one client (on join and on every change). </summary>
	public static void SendState( Connection c )
	{
		if ( c is null ) return;
		using ( Rpc.FilterInclude( c ) )
			ReceiveState( _objective ?? "", _timerOn && _timerEnds > 0f ? _timerLabel : "", _timerOn ? Math.Max( 0f, (float)_timerEnds ) : 0f, _music ?? "" );
	}

	// ----- client -----

	public static string HudObjective { get; private set; }
	public static string HudTimerLabel { get; private set; }
	static RealTimeUntil _hudTimer;
	public static float HudTimerLeft => HudTimerLabel is null ? 0f : Math.Max( 0f, (float)_hudTimer );
	public static bool HudTimerOn => HudTimerLabel is not null;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveState( string objective, string timerLabel, float secondsLeft, string music )
	{
		HudObjective = string.IsNullOrWhiteSpace( objective ) ? null : objective;
		HudTimerLabel = string.IsNullOrWhiteSpace( timerLabel ) ? null : timerLabel;
		_hudTimer = secondsLeft;
		PlayMusic( music );
	}

	// ----- music (client) -----

	[ConVar( "swrp_event_music", Help = "Volume of Game Master event music, your machine only (0 = off)", Saved = true )]
	public static float MusicVolume { get; set; } = 0.6f;

	static SoundHandle _musicHandle;
	static string _playing;

	static void PlayMusic( string track )
	{
		if ( string.IsNullOrEmpty( track ) )
		{
			_playing = null;
			_musicHandle?.Stop( 2f );
			_musicHandle = null;
			return;
		}
		if ( track == _playing && _musicHandle is { IsPlaying: true } ) return;
		_playing = track;
		_musicHandle?.Stop( 1f );
		_musicHandle = null;
		if ( MusicVolume <= 0f ) return;
		var path = track == "theme" ? "sounds/music/menu_theme.sound" : $"sounds/music/club/{track}.sound";
		_musicHandle = Sound.Play( path );
		if ( _musicHandle is not null ) _musicHandle.Volume = Math.Clamp( MusicVolume, 0f, 1f );
	}

	/// <summary> Client, every frame: keep the event track going (they're rendered as loops) at the set volume. </summary>
	public static void TickMusic()
	{
		if ( _playing is null ) return;
		if ( _musicHandle is null || !_musicHandle.IsPlaying ) { var t = _playing; _playing = null; PlayMusic( t ); return; }
		_musicHandle.Volume = Math.Clamp( MusicVolume, 0f, 1f );
	}

	static void Music( Connection caller, string rest )
	{
		var t = rest.Trim().ToLowerInvariant();
		if ( t is "stop" or "off" ) _music = null;
		else if ( Tracks.Contains( t ) ) _music = t;
		else { SwrpChat.SystemTo( caller, $"Usage: /gm music <{string.Join( "|", Tracks )}|stop>" ); return; }
		SwrpGameMaster.Note( caller, _music is null ? "stopped the event music" : $"started the event music ({_music})" );
		BroadcastState();
	}

	// ----- layouts -----

	public sealed class LayoutFile
	{
		public List<PropSpot> Props { get; set; } = new();
		public List<NpcSpot> Npcs { get; set; } = new();
		public string By { get; set; }
		public DateTime Saved { get; set; }
	}

	public sealed class PropSpot
	{
		public string Model { get; set; }
		public string Name { get; set; }
		public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }
		public float Pitch { get; set; }
		public float Yaw { get; set; }
		public float Roll { get; set; }
		public bool Frozen { get; set; }
		[JsonIgnore] public Vector3 Position => new( X, Y, Z );
	}

	public sealed class NpcSpot
	{
		public string Type { get; set; }
		public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }
		public float Yaw { get; set; }
		public bool Hold { get; set; }
	}

	static string LayoutPath( string name ) => $"swrp/layouts/{name}.json";

	static void Layout( Connection caller, SwrpPlayer me, string rest )
	{
		var (sub, nameRaw) = SwrpStaff.Split( rest );
		var name = new string( nameRaw.ToLowerInvariant().Where( ch => char.IsLetterOrDigit( ch ) || ch is '_' or '-' ).ToArray() );
		var scene = Game.ActiveScene;
		switch ( sub )
		{
			case "list":
			{
				var files = FileSystem.Data.DirectoryExists( "swrp/layouts" ) ? FileSystem.Data.FindFile( "swrp/layouts", "*.json" ).Select( f => f[..^5] ).ToList() : new List<string>();
				SwrpChat.SystemTo( caller, files.Count == 0 ? "No saved layouts. /gm layout save <name>." : $"Layouts: {string.Join( ", ", files )}" );
				return;
			}
			case "save":
			{
				if ( name.Length == 0 ) { SwrpChat.SystemTo( caller, "Usage: /gm layout save <name>" ); return; }
				var file = new LayoutFile { By = caller.DisplayName, Saved = DateTime.UtcNow };
				foreach ( var go in scene.GetAllObjects( true ).Where( g => g.Tags.Has( SwrpBuild.PropTag ) ) )
				{
					var model = go.Components.Get<ModelRenderer>()?.Model?.ResourcePath;
					if ( string.IsNullOrEmpty( model ) ) continue;
					var ang = go.WorldRotation.Angles();
					file.Props.Add( new PropSpot
					{
						Model = model, Name = go.Name, X = go.WorldPosition.x, Y = go.WorldPosition.y, Z = go.WorldPosition.z,
						Pitch = ang.pitch, Yaw = ang.yaw, Roll = ang.roll, Frozen = go.Components.Get<Rigidbody>() is { MotionEnabled: false },
					} );
				}
				foreach ( var npc in SwrpNpcDirector.All( scene ).Where( n => !n.IsDead ) )
					file.Npcs.Add( new NpcSpot { Type = npc.Def.Id, X = npc.WorldPosition.x, Y = npc.WorldPosition.y, Z = npc.WorldPosition.z, Yaw = npc.WorldRotation.Yaw(), Hold = !npc.Hunt } );
				FileSystem.Data.CreateDirectory( "swrp/layouts" );
				FileSystem.Data.WriteJson( LayoutPath( name ), file );
				SwrpGameMaster.Note( caller, $"saved layout '{name}' ({file.Props.Count} props, {file.Npcs.Count} NPCs)" );
				return;
			}
			case "load":
			{
				var file = name.Length == 0 ? null : FileSystem.Data.ReadJson<LayoutFile>( LayoutPath( name ), null );
				if ( file is null ) { SwrpChat.SystemTo( caller, $"No layout called '{name}'. /gm layout list." ); return; }
				var props = 0;
				foreach ( var p in file.Props )
				{
					if ( SwrpBuild.CreateProp( p.Model, p.Name, p.Position, new Angles( p.Pitch, p.Yaw, p.Roll ).ToRotation(), p.Frozen ) is not null ) props++;
				}
				var npcs = 0;
				foreach ( var n in file.Npcs )
				{
					if ( NpcRegistry.Get( n.Type ) is not { } def ) continue;
					foreach ( var made in SwrpNpcDirector.Spawn( scene, def, new Vector3( n.X, n.Y, n.Z ), n.Yaw, 1 ) )
					{
						npcs++;
						made.Hunt = !n.Hold;
						made.Objective = n.Hold ? made.WorldPosition : null;
					}
				}
				SwrpGameMaster.Note( caller, $"loaded layout '{name}' ({props} props, {npcs}/{file.Npcs.Count} NPCs{( npcs < file.Npcs.Count ? ", NPC limit reached" : "" )})" );
				return;
			}
			case "delete":
				if ( name.Length == 0 || !FileSystem.Data.FileExists( LayoutPath( name ) ) ) { SwrpChat.SystemTo( caller, $"No layout called '{name}'." ); return; }
				FileSystem.Data.DeleteFile( LayoutPath( name ) );
				SwrpGameMaster.Note( caller, $"deleted layout '{name}'" );
				return;
		}
		SwrpChat.SystemTo( caller, "Usage: /gm layout save|load|list|delete <name> (props from the spawn menu and every NPC; /gm cleanup and the remover clear them)" );
	}
}
