using System;

namespace Swrp.Feel;

/// <summary> What a point in the world sounds like underfoot or under fire. </summary>
public enum FeelSurface { Metal, Sand, Snow, Wet }

/// <summary>
/// The feel sounds (synthesized by <c>SoundSource/feel_sounds.py</c> into <c>Assets/sounds/feel</c>; original audio only):
/// footsteps on planet ground (the ship's own grate and plate steps stay in SwrpFootsteps), a surface layer on every bolt
/// impact, debris breaking off and landing, bolts whizzing past your head, the rush of air when a door near you slides
/// and when your lift moves. The menu click, hover, open and close and the heartbeat are the existing ones in
/// <see cref="SwrpAudio"/>; use those for UI.
/// </summary>
public static class FeelSounds
{
	public const string StepSand = "sounds/feel/step_sand.sound";
	public const string StepSnow = "sounds/feel/step_snow.sound";
	public const string StepWet = "sounds/feel/step_wet.sound";
	public const string ImpactMetal = "sounds/feel/impact_metal.sound";
	public const string ImpactSand = "sounds/feel/impact_sand.sound";
	public const string ImpactSnow = "sounds/feel/impact_snow.sound";
	public const string ImpactDebris = "sounds/feel/impact_debris.sound";
	public const string DebrisCollapse = "sounds/feel/debris_collapse.sound";
	public const string BoltWhizz = "sounds/feel/bolt_whizz.sound";
	public const string DoorWhoosh = "sounds/feel/door_whoosh.sound";
	public const string LiftWhoosh = "sounds/feel/lift_whoosh.sound";
	public const string HitThump = "sounds/feel/hit_thump.sound";
	public const string TransitionWhoosh = "sounds/feel/transition_whoosh.sound";

	// the UI set lives in SwrpAudio (ui_sounds.py); listed here so feel code has one place to look
	public const string UiHover = SwrpAudio.Hover;
	public const string UiClick = SwrpAudio.Click;
	public const string UiOpen = SwrpAudio.Open;
	public const string UiClose = SwrpAudio.Close;
	public const string Heartbeat = SwrpAudio.Heartbeat;

	/// <summary> The ground at <paramref name="pos"/>: the planet you're on, or the ship's metal. </summary>
	public static FeelSurface SurfaceAt( Vector3 pos )
	{
		// the ship is all metal; Tatooine's surface is sand
		return Tatooine.TatooinePlanet.IsOnPlanet( pos ) ? FeelSurface.Sand : FeelSurface.Metal;
	}

	/// <summary>
	/// The footstep for a trooper at <paramref name="pos"/> on planet ground, or null on the ship (keep the ship's own grate
	/// and plate steps). Hook: SwrpFootsteps.Step.
	/// </summary>
	public static string Step( Vector3 pos )
	{
		if ( !Feel.Sounds ) return null;
		return SurfaceAt( pos ) switch
		{
			FeelSurface.Sand => StepSand,
			FeelSurface.Snow => StepSnow,
			FeelSurface.Wet => StepWet,
			_ => null,
		};
	}

	public static SoundHandle Play( string sound, Vector3 pos, float volume = 1f )
	{
		if ( !Game.IsPlaying ) return null;
		var h = Sound.Play( sound, pos );
		if ( h is not null ) h.Volume *= volume * Math.Clamp( Feel.Volume, 0f, 1.5f );
		return h;
	}

	public static SoundHandle PlayUi( string sound, float volume = 1f )
	{
		if ( !Game.IsPlaying ) return null;
		var h = Sound.Play( sound );
		if ( h is not null ) h.Volume *= volume * Math.Clamp( Feel.Volume, 0f, 1.5f );
		return h;
	}

	// a firefight can put dozens of impacts in a frame: only so many surface layers at once
	static RealTimeSince _sinceImpact;
	static int _impactsThisWindow;

	/// <summary>
	/// A bolt hit something (hook: SwrpFx.Impact, every machine). <paramref name="world"/> false means it hit a body: no
	/// surface sound, but it's remembered for the ragdoll push.
	/// </summary>
	public static void Impact( Scene scene, Vector3 pos, Vector3 normal, bool world )
	{
		if ( !Feel.On ) return;
		if ( !world ) { FeelHits.FleshHit( pos, normal ); return; }
		if ( !Feel.Sounds || !SwrpFx.Near( scene, pos, 1800f ) ) return;
		if ( _sinceImpact > 0.08f ) { _sinceImpact = 0; _impactsThisWindow = 0; }
		if ( ++_impactsThisWindow > 3 ) return;
		var sound = SurfaceAt( pos ) switch
		{
			FeelSurface.Sand => ImpactSand,
			FeelSurface.Snow => ImpactSnow,
			FeelSurface.Wet => ImpactMetal,
			_ => ImpactMetal,
		};
		// on the ship the blaster's own impact crack is already there: the metal ring sits under it
		Play( sound, pos, sound == ImpactMetal ? 0.6f : 1f );
	}

	static RealTimeSince _sinceDebris;

	/// <summary> Pieces broken off (a crack and a clatter, louder for more). </summary>
	public static void Debris( Vector3 pos, int removed )
	{
		if ( !Feel.Sounds || removed <= 0 || _sinceDebris < 0.05f ) return;
		_sinceDebris = 0;
		Play( ImpactDebris, pos, Math.Clamp( 0.45f + removed / 30f, 0.45f, 1.2f ) );
	}

	static RealTimeSince _sinceLanding;

	/// <summary> A loose piece landing (strength 0..2): a big piece gives the long collapse. </summary>
	public static void Landed( Vector3 pos, float strength )
	{
		if ( !Feel.Sounds || _sinceLanding < 0.08f ) return;
		_sinceLanding = 0;
		if ( strength > 1.1f ) Play( DebrisCollapse, pos, Math.Clamp( strength * 0.5f, 0.5f, 1f ) );
		else Play( ImpactDebris, pos, Math.Clamp( 0.4f + strength * 0.5f, 0.4f, 1f ) );
	}

	// ----- every frame: bolts passing close, doors and lifts near you -----

	sealed class Tracked { public Vector3 First, Last; public bool Done; }
	static readonly Dictionary<Component, Tracked> _bolts = new();
	static readonly List<Component> _gone = new();

	static List<SwrpDoor> _doors;
	static readonly Dictionary<SwrpDoor, bool> _doorOpen = new();
	static List<SwrpTurbolift> _lifts;
	static readonly Dictionary<SwrpTurbolift, bool> _liftMoving = new();
	static RealTimeSince _sinceScan = 100;

	internal static void Tick( Scene scene, float dt )
	{
		var cam = scene.Camera;
		if ( !cam.IsValid() ) return;
		Whizz( scene, cam.WorldPosition );
		if ( _sinceScan > 5f )
		{
			_sinceScan = 0;
			_doors = scene.GetAllComponents<SwrpDoor>().ToList();
			_lifts = scene.GetAllComponents<SwrpTurbolift>().ToList();
			_doorOpen.Clear();
			_liftMoving.Clear();
		}
		Doors( cam.WorldPosition );
	}

	/// <summary> Every blaster bolt in flight: when one passes within a metre or so of your head (not your own), it whizzes. </summary>
	static void Whizz( Scene scene, Vector3 ear )
	{
		if ( !Feel.Whizz ) { _bolts.Clear(); return; }
		foreach ( var b in scene.GetAllComponents<BlasterBolt>() ) Follow( b, ear, 110f );
		foreach ( var b in scene.GetAllComponents<Flight.FlightBolt>() ) Follow( b, ear, 220f );
		_gone.Clear();
		foreach ( var (b, _) in _bolts ) if ( !b.IsValid() ) _gone.Add( b );
		foreach ( var b in _gone ) _bolts.Remove( b );
	}

	static void Follow( Component bolt, Vector3 ear, float reach )
	{
		var pos = bolt.WorldPosition;
		if ( !_bolts.TryGetValue( bolt, out var t ) )
		{
			_bolts[bolt] = new Tracked { First = pos, Last = pos, Done = pos.DistanceSquared( ear ) < 250f * 250f };      // fired from right beside you (your own)
			return;
		}
		if ( t.Done ) return;
		// the closest point of this frame's stretch of flight to your ear
		var seg = pos - t.Last;
		var len = seg.Length;
		var closest = len < 0.01f ? pos : t.Last + seg / len * Math.Clamp( Vector3.Dot( ear - t.Last, seg / len ), 0f, len );
		t.Last = pos;
		if ( len < 0.01f || Vector3.Dot( pos - ear, seg ) < 0f ) return;      // still coming: wait until it's gone past
		t.Done = true;      // it has passed you, near or far: judged once
		if ( closest.DistanceSquared( ear ) > reach * reach ) return;
		Play( BoltWhizz, closest );
	}

	static void Doors( Vector3 ear )
	{
		if ( !Feel.Sounds ) return;
		if ( _doors is not null )
			foreach ( var d in _doors )
			{
				if ( !d.IsValid() ) continue;
				var open = d.IsOpen;
				if ( _doorOpen.TryGetValue( d, out var was ) && was != open && !d.IsBrigShield && d.WorldPosition.DistanceSquared( ear ) < 900f * 900f )
					Play( DoorWhoosh, d.WorldPosition, open ? 1f : 0.8f );
				_doorOpen[d] = open;
			}
		if ( _lifts is not null )
			foreach ( var l in _lifts )
			{
				if ( !l.IsValid() ) continue;
				var moving = l.Moving;
				// you're riding it (standing in the car): the rush of the shaft, in your ears
				var me = Feel.LocalPlayer.IsValid() ? Feel.LocalPlayer.WorldPosition + Vector3.Up * 40f : ear;
				if ( moving && _liftMoving.TryGetValue( l, out var was ) && !was && l.DeckAt( me ) >= 0 )
					PlayUi( LiftWhoosh, 0.9f );
				_liftMoving[l] = moving;
			}
	}
}
