using System;

namespace Swrp;

/// <summary>
/// An area of the map with its own room tone (hangar, bridge, brig...). Boxes placed by notorious_scene.py; where several
/// overlap the highest <see cref="Priority"/> wins, and anywhere else aboard is "corridor".
/// </summary>
[Title( "SWRP Ambient Zone" ), Category( "SWRP" ), Icon( "graphic_eq" )]
public sealed class SwrpAmbientZone : Component
{
	/// <summary> hangar, bridge, corridor, engineering, brig, medbay or temple. </summary>
	[Property] public string Profile { get; set; } = "corridor";
	[Property] public Vector3 Min { get; set; }
	[Property] public Vector3 Max { get; set; }
	[Property] public int Priority { get; set; } = 1;

	public BBox Box => new( Min, Max );

	static SwrpAmbientZone[] _zones = Array.Empty<SwrpAmbientZone>();
	static RealTimeSince _zonesAge = 100;

	/// <summary> The profile at a point ("corridor" when no zone holds it). </summary>
	public static string ProfileAt( Scene scene, Vector3 p )
	{
		if ( _zonesAge > 5f || _zones.Any( z => !z.IsValid() ) )
		{
			_zonesAge = 0;
			_zones = scene.GetAllComponents<SwrpAmbientZone>().OrderByDescending( z => z.Priority ).ToArray();
		}
		foreach ( var z in _zones )
			if ( z.IsValid() && z.Box.Contains( p ) ) return z.Profile;
		return "corridor";
	}
}

/// <summary>
/// The ship's soundscape, on every machine for its own camera: the room tone of the area you're in (crossfading as you move
/// between areas) and, now and then, a distant muffled ship sound (a hatch, cargo, a droid, a far-off PA). Original sounds
/// from SoundSource/ambience.py. swrp_ambient_volume sets the level (0 turns it off).
/// </summary>
[Title( "SWRP Ambience" ), Category( "SWRP" ), Icon( "surround_sound" )]
public sealed class SwrpAmbience : Component
{
	[ConVar( "swrp_ambient_volume", Help = "Ship ambience volume: room tones, distant sounds, footsteps (0 to 1, 0 = off)", Saved = true )]
	public static float Volume { get; set; } = 0.5f;

	const float LoopSeconds = 16f;
	const float Overlap = 0.08f;
	const float FadeRate = 0.5f;     // full crossfade in about two seconds

	sealed class Voice
	{
		public string Profile;
		public float Gain;
		public SoundHandle Current;
		public SoundHandle Previous;
		public RealTimeSince Started;
	}

	readonly Dictionary<string, Voice> _voices = new();

	/// <summary> Areas with no room tone or distant sounds (the brig: only its shields make a sound). </summary>
	static readonly HashSet<string> Silent = new() { "brig" };

	public static bool IsSilentAt( Scene scene, Vector3 p ) => Silent.Contains( SwrpAmbientZone.ProfileAt( scene, p ) );
	RealTimeUntil _nextDistant = 8f;
	public static string CurrentProfile { get; private set; } = "corridor";

	protected override void OnUpdate()
	{
		var cam = Scene.Camera;
		if ( !cam.IsValid() ) return;
		var master = Math.Clamp( Volume, 0f, 1f );

		// the character screen has its own music; the ship is quiet until you deploy
		var here = SwrpPlayer.Local is null ? null : SwrpAmbientZone.ProfileAt( Scene, cam.WorldPosition );
		if ( here is not null ) CurrentProfile = here;
		if ( here is not null && !Silent.Contains( here ) && !_voices.ContainsKey( here ) ) _voices[here] = new Voice { Profile = here };

		foreach ( var v in _voices.Values )
		{
			v.Gain = v.Gain.Approach( v.Profile == here ? 1f : 0f, Time.Delta * FadeRate );
			var level = v.Gain * master;
			if ( level <= 0.001f )
			{
				Stop( v );
				continue;
			}
			// restart a moment before the loop ends, overlapping, so the room tone never gaps
			if ( v.Current is null || !v.Current.IsPlaying || v.Started > LoopSeconds - Overlap )
			{
				if ( v.Previous is not null && v.Previous.IsPlaying ) v.Previous.Stop();
				v.Previous = v.Current;
				v.Current = Sound.Play( $"sounds/ambient/amb_{v.Profile}.sound" );
				v.Started = 0;
			}
			if ( v.Current is not null ) v.Current.Volume = level;
			if ( v.Previous is not null && v.Previous.IsPlaying ) v.Previous.Volume = level * Math.Clamp( 1f - v.Started / Overlap, 0f, 1f );
		}

		// distant ship sounds, somewhere off behind the bulkheads
		if ( here is not null && !Silent.Contains( here ) && master > 0.001f && _nextDistant <= 0 )
		{
			_nextDistant = Game.Random.Float( 9f, 26f );
			var dir = Vector3.Random.WithZ( Game.Random.Float( -0.2f, 0.3f ) ).Normal;
			var h = Sound.Play( "sounds/ambient/distant.sound", cam.WorldPosition + dir * Game.Random.Float( 700f, 1500f ) );
			if ( h is not null ) h.Volume *= master;
		}
	}

	static void Stop( Voice v )
	{
		if ( v.Current is not null && v.Current.IsPlaying ) v.Current.Stop();
		if ( v.Previous is not null && v.Previous.IsPlaying ) v.Previous.Stop();
		v.Current = null;
		v.Previous = null;
	}

	protected override void OnDisabled()
	{
		foreach ( var v in _voices.Values ) Stop( v );
	}
}

/// <summary>
/// Footsteps for every trooper, heard by everyone nearby: metal grate in the hangar and engineering, deck plating elsewhere,
/// with a light armour rattle. Replaces the controller's stock footsteps. Quieter crouched or walking.
/// </summary>
[Title( "SWRP Footsteps" ), Category( "SWRP" ), Icon( "directions_walk" )]
public sealed class SwrpFootsteps : Component
{
	SwrpPlayer _player;
	Vector3 _last;
	float _travelled;
	bool _wasGrounded = true;

	protected override void OnStart()
	{
		_player = GetComponent<SwrpPlayer>();
		_last = WorldPosition;
	}

	protected override void OnUpdate()
	{
		var c = _player?.Controller;
		if ( !c.IsValid() ) return;
		if ( c.EnableFootstepSounds ) c.EnableFootstepSounds = false;      // ours instead of the stock ones

		var pos = WorldPosition;
		var moved = ( pos - _last ).WithZ( 0 ).Length;
		_last = pos;
		var grounded = c.IsOnGround;
		var master = Math.Clamp( SwrpAmbience.Volume / 0.5f, 0f, 1f ) * 0.6f;     // (steps keep their loudness at the default)

		// landing
		if ( grounded && !_wasGrounded ) Step( 1.2f, master );
		_wasGrounded = grounded;
		if ( !grounded || _player.Noclip || moved > 60f ) { _travelled = 0; return; }

		var speed = c.Velocity.WithZ( 0 ).Length;
		if ( speed < 20f ) { _travelled = 0; return; }
		var stride = 58f * MathF.Max( 0.5f, _player.BodyScale ) * ( speed > 200f ? 1.25f : 1f );
		_travelled += moved;
		if ( _travelled < stride ) return;
		_travelled = 0;
		var loud = c.IsDucking ? 0.35f : speed > 200f ? 1f : 0.65f;
		Step( loud, master );
	}

	void Step( float loud, float master )
	{
		if ( master <= 0.001f || _player.Invisible && _player.IsProxy ) return;
		var profile = SwrpAmbientZone.ProfileAt( Scene, WorldPosition );
		var grate = profile is "hangar" or "engineering";
		var h = Sound.Play( Feel.FeelSounds.Step( WorldPosition ) ?? ( grate ? "sounds/ambient/step_grate.sound" : "sounds/ambient/step_plate.sound" ), WorldPosition );     // planet ground: Code/Feel
		if ( h is not null ) h.Volume *= loud * master;
		if ( Game.Random.Float() < 0.7f )
		{
			var a = Sound.Play( "sounds/ambient/armour.sound", WorldPosition + Vector3.Up * 40f );
			if ( a is not null ) a.Volume *= loud * master;
		}
	}
}
