using System;

namespace Swrp;

/// <summary>
/// Location tags, for pointing someone (or Claude) at an exact spot on the map. F6 or /loc takes one where you stand and look:
/// a numbered line with the deck and area, your position and facing, what the crosshair is on (object, model, the point and
/// how far) and the nearest door's name, plus the swrp_goto line that brings you back. It is copied to the clipboard, shown
/// on screen for a few seconds (so it lands in screenshots), printed to the console and added to swrp/locations.md in the
/// data folder (X:\SteamLibrary\steamapps\common\sbox\data\squadgaming\swrp#local\swrp\ while developing), where "#12" can be
/// looked up later. swrp_goto takes a tag number or coordinates. All local to your machine.
/// </summary>
public static class SwrpLocTag
{
	const string FilePath = "swrp/locations.md";
	const int Keep = 300;

	/// <summary> The last tag taken on this machine, and how long ago (the HUD shows it for a few seconds). </summary>
	public static string Last { get; private set; }
	public static RealTimeSince SinceLast { get; private set; } = 100;
	public static bool Showing => Last is not null && SinceLast < 8f;

	/// <summary> Client: the F6 key (free in Input.config; read straight from the keyboard, so no restart is needed). </summary>
	public static void Tick()
	{
		if ( SwrpUiState.ChatOpen || !Input.Pressed( "LocTag" ) ) return;
		Take( null );
	}

	[ConCmd( "swrp_loc", Help = "swrp_loc [note] - take a location tag where you stand and look (same as F6 or /loc): clipboard, screen, console and swrp/locations.md" )]
	public static void LocCommand( string note = null ) => Take( note );

	/// <summary> Client: /loc [note] in chat. </summary>
	public static bool TryLocal( string cmd, string rest )
	{
		if ( cmd is not ( "/loc" or "/location" or "/pin" ) ) return false;
		Take( rest );
		return true;
	}

	public static void Take( string note )
	{
		var scene = Game.ActiveScene;
		var cam = scene?.Camera;
		if ( cam is null ) return;
		var me = SwrpPlayers.Local;
		var at = me?.WorldPosition ?? cam.WorldPosition;
		var ang = cam.WorldRotation.Angles();

		var trace = scene.Trace.Ray( cam.WorldPosition, cam.WorldPosition + cam.WorldRotation.Forward * 20000f ).WithoutTags( "trigger" );
		if ( me is not null ) trace = trace.IgnoreGameObjectHierarchy( me.GameObject );
		var tr = trace.Run();

		var number = NextNumber();
		var area = SwrpAmbientZone.ProfileAt( scene, at );
		var parts = new List<string>
		{
			$"#{number}",
			DateTime.Now.ToString( "dd MMM HH:mm" ),
			$"{SwrpShip.DeckName( at )} ({area})",
			$"pos {at.x:0} {at.y:0} {at.z:0}",
			$"facing yaw {ang.yaw:0} pitch {ang.pitch:0}",
		};
		if ( tr.Hit )
		{
			var model = tr.GameObject?.GetComponent<ModelRenderer>()?.Model?.ResourcePath ?? tr.GameObject?.GetComponentInParent<ModelRenderer>()?.Model?.ResourcePath;
			var door = tr.GameObject?.GetComponentInParent<SwrpDoor>();
			var what = door is not null ? $"door \"{door.DisplayName}\" ({door.DoorId})" : $"\"{tr.GameObject?.Name ?? "world"}\"";
			parts.Add( $"looking at {what}{( string.IsNullOrEmpty( model ) ? "" : $" {model}" )} @ {tr.HitPosition.x:0} {tr.HitPosition.y:0} {tr.HitPosition.z:0} ({tr.Distance:0} u, {tr.Distance * 0.0254f:0.0} m){( tr.Surface is { } s ? $" surface {s.ResourceName}" : "" )}" );
		}
		else parts.Add( "looking at open space" );
		if ( scene.GetAllComponents<SwrpDoor>().OrderBy( d => d.WorldPosition.DistanceSquared( at ) ).FirstOrDefault() is { } near && near.WorldPosition.Distance( at ) < 900f )
			parts.Add( $"nearest door \"{near.DisplayName}\" {near.WorldPosition.Distance( at ) * 0.0254f:0} m" );
		if ( !string.IsNullOrWhiteSpace( note ) ) parts.Add( $"note: {note.Trim()}" );
		parts.Add( $"swrp_goto {at.x:0} {at.y:0} {at.z:0} {ang.yaw:0} {ang.pitch:0}" );

		var line = string.Join( " · ", parts );
		Last = line;
		SinceLast = 0;
		try { Sandbox.UI.Clipboard.SetText( line ); }
		catch ( Exception e ) { Log.Warning( $"[SWRP] loc: no clipboard ({e.Message})" ); }
		Log.Info( $"[SWRP] LOC {line}" );
		Append( line );
		SwrpAudio.Ui( SwrpAudio.Confirm );
		SwrpChat.AddLocal( ChatKind.System, $"Location #{number} copied to the clipboard and saved in swrp/locations.md. Paste it, or just say \"loc #{number}\"." );
	}

	static int _next;

	static int NextNumber()
	{
		if ( _next > 0 ) return ++_next;
		var last = 0;
		foreach ( var l in ReadLines() )
			if ( l.StartsWith( "- #" ) && int.TryParse( new string( l[3..].TakeWhile( char.IsDigit ).ToArray() ), out var n ) ) last = Math.Max( last, n );
		_next = last + 1;
		return _next;
	}

	static List<string> ReadLines()
	{
		try
		{
			return FileSystem.Data.FileExists( FilePath ) ? FileSystem.Data.ReadAllText( FilePath ).Split( '\n' ).Select( l => l.TrimEnd( '\r' ) ).ToList() : new List<string>();
		}
		catch ( Exception )
		{
			return new List<string>();
		}
	}

	static void Append( string line )
	{
		try
		{
			var entries = ReadLines().Where( l => l.StartsWith( "- #" ) ).ToList();
			entries.Add( $"- {line}" );
			if ( entries.Count > Keep ) entries.RemoveRange( 0, entries.Count - Keep );
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteAllText( FilePath, "# SWRP location tags (F6 or /loc in game; newest last; swrp_goto #N goes back)\n\n" + string.Join( "\n", entries ) + "\n" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] loc: could not save {FilePath}: {e.Message}" );
		}
	}

	/// <summary> The coordinates in a saved tag, by its number. </summary>
	static bool Lookup( int number, out float[] values )
	{
		values = null;
		var line = ReadLines().LastOrDefault( l => l.StartsWith( $"- #{number} " ) );
		var i = line?.LastIndexOf( "swrp_goto ", StringComparison.Ordinal ) ?? -1;
		if ( i < 0 ) return false;
		values = line[( i + 10 )..].Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Select( t => t.ToFloat() ).ToArray();
		return values.Length >= 3;
	}

	[ConCmd( "swrp_goto", Help = "swrp_goto <#tag | x y z [yaw] [pitch]> - go to a location tag: host teleports the trooper, before deploying the camera moves there" )]
	public static void Goto( string a, string b = null, string c = null, string d = null, string e = null )
	{
		float[] v;
		if ( a?.StartsWith( '#' ) == true || b is null && int.TryParse( a, out _ ) )
		{
			if ( !int.TryParse( a.TrimStart( '#' ), out var n ) || !Lookup( n, out v ) ) { Log.Warning( $"[SWRP] no location tag {a} in {FilePath}" ); return; }
		}
		else
		{
			v = new[] { a, b, c, d, e }.Where( t => t is not null ).Select( t => t.ToFloat() ).ToArray();
			if ( v.Length < 3 ) { Log.Warning( "[SWRP] swrp_goto <#tag | x y z [yaw] [pitch]>" ); return; }
		}
		var pos = new Vector3( v[0], v[1], v[2] );
		var yaw = v.Length > 3 ? v[3] : 0f;
		var pitch = v.Length > 4 ? v[4] : 0f;

		var me = SwrpPlayers.Local;
		if ( me is null )
		{
			// before deploying: the camera (swrp_cam), eye height above the spot
			SwrpAnimMannequin.MoveCamera( pos.x, pos.y, pos.z + 64f, pitch, yaw );
			return;
		}
		if ( !Networking.IsHost ) { Log.Warning( "[SWRP] swrp_goto moves a deployed trooper on the host only (staff: /tp x y z in chat)." ); return; }
		me.TeleportTo( pos, yaw );
		if ( me.Controller.IsValid() ) me.Controller.EyeAngles = new Angles( pitch, yaw, 0 );
		Log.Info( $"[SWRP] went to {pos.x:0} {pos.y:0} {pos.z:0} ({SwrpShip.DeckName( pos )})" );
	}
}
