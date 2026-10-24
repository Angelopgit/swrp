using System;
using Sandbox.UI;

namespace Swrp;

/// <summary>
/// Staff power hotkeys. Each staff member picks their own keys for noclip, god mode and invisibility in the staff panel
/// (saved on their own machine). A press sends the same chat command as the panel's buttons, so the host still checks the
/// role, duty and the staff log. Keys only do anything while the host says you're staff on duty (<see cref="SwrpPlayer.StaffOnDuty"/>).
/// </summary>
public static class SwrpStaffKeys
{
	[ConVar( "swrp_staffkey_noclip", Help = "Staff: the key that toggles noclip (blank = none). Set it in the staff panel.", Saved = true )]
	public static string NoclipKey { get; set; } = "v";

	[ConVar( "swrp_staffkey_god", Help = "Staff: the key that toggles god mode (blank = none). Set it in the staff panel.", Saved = true )]
	public static string GodKey { get; set; } = "";

	[ConVar( "swrp_staffkey_invis", Help = "Staff: the key that toggles invisibility (blank = none). Set it in the staff panel.", Saved = true )]
	public static string InvisibleKey { get; set; } = "";

	public readonly record struct Power( string Id, string Label, string Command );

	public static readonly Power[] Powers =
	{
		new( "noclip", "NOCLIP", "/noclip" ),
		new( "god", "GOD", "/god" ),
		new( "invis", "INVISIBLE", "/invis" ),
	};

	/// <summary> A press of a key shared with push-to-talk shorter than this is a tap (the power); longer is talking. </summary>
	public const float TapSeconds = 0.25f;

	/// <summary> The power waiting for its new key in the staff panel (null when not setting one). </summary>
	public static string Capturing { get; set; }

	/// <summary> Since a key was last bound: that same press mustn't also fire the power or another hotkey. </summary>
	public static RealTimeSince SinceBound { get; private set; } = 10;

	static readonly Dictionary<string, RealTimeSince> _taps = new();

	/// <summary> Keys watched while setting a hotkey (the capture layer catches the rest). </summary>
	static readonly string[] Common = Enumerable.Range( 'a', 26 ).Select( c => ( (char)c ).ToString() )
		.Concat( Enumerable.Range( 0, 10 ).Select( i => i.ToString() ) )
		.Concat( Enumerable.Range( 1, 12 ).Select( i => $"f{i}" ) )
		.Concat( new[] { "mouse4", "mouse5" } ).ToArray();

	public static string KeyOf( string id ) => ( id switch { "noclip" => NoclipKey, "god" => GodKey, _ => InvisibleKey } )?.Trim() ?? "";

	static void SetKey( string id, string key )
	{
		switch ( id )
		{
			case "noclip": NoclipKey = key; break;
			case "god": GodKey = key; break;
			default: InvisibleKey = key; break;
		}
	}

	/// <summary> Give a power a key (blank clears it). Another power on the same key loses it. </summary>
	public static void Bind( string id, string key )
	{
		key = key?.Trim() ?? "";
		if ( key.Length > 0 )
			foreach ( var p in Powers )
				if ( p.Id != id && string.Equals( KeyOf( p.Id ), key, StringComparison.OrdinalIgnoreCase ) ) SetKey( p.Id, "" );
		SetKey( id, key );
		Capturing = null;
		SinceBound = 0;
		_taps.Clear();
	}

	/// <summary> The key's name for the panel (V, F1, MOUSE 4, NUM 1...), or NONE. </summary>
	public static string KeyName( string key )
	{
		if ( string.IsNullOrEmpty( key ) ) return "NONE";
		var k = key.ToUpperInvariant();
		if ( k.StartsWith( "MOUSE" ) ) return $"MOUSE {k[5..]}";
		if ( k.StartsWith( "KP_" ) ) return $"NUM {k[3..]}";
		return k.Replace( '_', ' ' );
	}

	/// <summary> The game action that also uses this key by default, if any (shown under the hotkey as a heads-up). </summary>
	public static string Clash( string key )
	{
		if ( string.IsNullOrEmpty( key ) ) return null;
		foreach ( var a in Input.GetActions() )
		{
			if ( !string.Equals( a.KeyboardCode, key, StringComparison.OrdinalIgnoreCase ) ) continue;
			return a.Name == "Voice" ? "TAP · HOLD TALKS" : $"ALSO {( string.IsNullOrEmpty( a.Title ) ? a.Name : a.Title ).ToUpperInvariant()}";
		}
		return null;
	}

	/// <summary> Staff panel, every frame while a hotkey is being set: Escape cancels, a common key binds. </summary>
	public static void TickCapture()
	{
		if ( Capturing is null ) return;
		if ( !SwrpUiState.StaffOpen ) { Capturing = null; return; }
		if ( Input.EscapePressed )
		{
			Input.EscapePressed = false;
			Capturing = null;
			return;
		}
		foreach ( var k in Common )
			if ( Input.Keyboard.Pressed( k ) ) { Bind( Capturing, k ); return; }
	}

	/// <summary> Owner, every frame: fire the powers whose keys were pressed. </summary>
	public static void Tick( SwrpPlayer me )
	{
		if ( Capturing is not null || SinceBound < 0.15f || !me.StaffOnDuty || me.CharacterId is null || InputFocus.Current is TextEntry )
		{
			_taps.Clear();
			return;
		}

		foreach ( var p in Powers )
		{
			var key = KeyOf( p.Id );
			if ( key.Length == 0 ) continue;

			if ( Input.Keyboard.Pressed( key ) )
			{
				// the same key as push-to-talk: wait to see if it's a tap (the power) or a hold (talking)
				if ( Input.Pressed( "Voice" ) ) _taps[p.Id] = 0;
				else SwrpChat.Submit( p.Command );
			}

			if ( _taps.TryGetValue( p.Id, out var held ) && !Input.Keyboard.Down( key ) )
			{
				_taps.Remove( p.Id );
				if ( held < TapSeconds ) SwrpChat.Submit( p.Command );
			}
		}
	}

	/// <summary> A push-to-talk key is down but might still be a power tap: don't count it as talking yet. </summary>
	public static bool HoldingTap
	{
		get
		{
			foreach ( var t in _taps.Values )
				if ( t < TapSeconds ) return true;
			return false;
		}
	}
}

/// <summary>
/// Staff panel: a layer over the screen while a power hotkey is being set. It catches the next key (including ones the
/// panel doesn't poll for, like the number pad); Escape or a click cancels.
/// </summary>
public sealed class StaffKeyCapture : Panel
{
	readonly Label _text;

	public StaffKeyCapture()
	{
		AcceptsFocus = true;
		_text = AddChild<Label>( "capture-text" );
	}

	public override void Tick()
	{
		base.Tick();
		var label = SwrpStaffKeys.Powers.FirstOrDefault( p => p.Id == SwrpStaffKeys.Capturing ).Label ?? "";
		_text.Text = $"PRESS A KEY FOR {label} · ESC OR CLICK CANCELS";
		if ( !HasFocus ) Focus();
	}

	public override void OnButtonEvent( ButtonEvent e )
	{
		base.OnButtonEvent( e );
		if ( !e.Pressed || SwrpStaffKeys.Capturing is null ) return;
		e.StopPropagation = true;

		var key = e.Button?.Trim() ?? "";
		if ( key.Length == 0 ) return;
		if ( key.Equals( "escape", StringComparison.OrdinalIgnoreCase ) || key.StartsWith( "mouse", StringComparison.OrdinalIgnoreCase ) )
		{
			SwrpStaffKeys.Capturing = null;
			return;
		}
		SwrpStaffKeys.Bind( SwrpStaffKeys.Capturing, key );
	}
}
