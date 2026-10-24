using System;

namespace Swrp;

/// <summary>
/// Character-screen music. Plays the original synthesized theme (SoundSource/menu_music.py), or a track you own if you drop
/// it in locally as Assets/sounds/music/menu_custom.mp3 (or .wav / .ogg). Fades in while the character screen is showing and
/// out when you deploy. Only you hear it; nothing is networked.
/// </summary>
public static class SwrpMenuMusic
{
	[ConVar( "swrp_menu_music", Help = "Character screen music volume, 0 to 1 (0 = off)", Saved = true )]
	public static float Volume { get; set; } = 0.5f;

	const string Theme = "sounds/music/menu_theme.sound";
	static readonly string[] Custom = { "sounds/music/menu_custom.mp3", "sounds/music/menu_custom.wav", "sounds/music/menu_custom.ogg" };

	static SoundHandle _handle;
	static float _level;

	/// <summary> Called every frame by the character screen with whether it is showing. </summary>
	public static void Tick( bool showing )
	{
		var target = showing ? Math.Clamp( Volume, 0f, 1f ) : 0f;
		_level = _level.Approach( target, Time.Delta * ( showing ? 0.35f : 0.6f ) );     // ~1.5 s in, ~1 s out

		if ( _level <= 0.001f )
		{
			if ( _handle is not null && _handle.IsPlaying ) _handle.Stop();
			_handle = null;
			return;
		}

		// the theme is rendered as a seamless loop, so restarting it when it ends keeps it going
		if ( _handle is null || !_handle.IsPlaying )
			_handle = Start();
		if ( _handle is not null )
			_handle.Volume = _level;
	}

	static SoundHandle Start()
	{
		foreach ( var path in Custom )
		{
			if ( !FileSystem.Mounted.FileExists( path ) ) continue;
			var file = SoundFile.Load( path );
			if ( file is null ) continue;
			return Sound.Play( new SoundEvent { Sounds = new List<SoundFile> { file }, UI = true, Volume = 1f } );
		}
		return Sound.Play( Theme );
	}
}
