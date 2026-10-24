using System;
using Swrp.Feel;
using Swrp.Flight;

namespace Swrp.Tatooine;

/// <summary>
/// The Tatooine shuttle: a parked LAAT/i that never flies (the LAAT model and its open bay doors, collision boxes, no
/// SwrpFlightShip). One stands in the Venator's main hangar and one on Tatooine's landing field; E on either is the ride:
/// the screen dips to black over the gunship-ride sound and the host moves you to the other end. Down only while the ship
/// is over Tatooine (/hyperspace tatooine); up always, so nobody gets stranded when the ship leaves. Made on every machine
/// at the same fixed spots (not networked); the host checks every ride.
/// </summary>
[Title( "SWRP Tatooine Shuttle" ), Category( "SWRP" ), Icon( "flight_land" )]
public sealed class TatooineShuttle : Component, ISwrpUsable
{
	/// <summary> The hangar shuttle, clear of the flyable craft's pads (x 0) on the hangar floor. </summary>
	public static readonly Vector3 HangarSpot = new( 1500f, -6000f, -1222f );
	public const float HangarYaw = 90f;

	/// <summary> The planet shuttle, at the edge of the landing field. </summary>
	public static Vector3 PlanetSpot => TatooinePlanet.Origin + new Vector3( 0f, 900f, 0f );
	public const float PlanetYaw = 0f;

	const float RideSeconds = 4.2f;    // the length of sounds/ui/gunship_ride.sound
	const float MoveAfter = 1.3f;      // the screen is black by then
	const float Reach = 1000f;         // how near the shuttle you must be (host check)
	const string RideSound = "sounds/ui/gunship_ride.sound";

	/// <summary> True: this one flies you down to Tatooine; false: back up to the hangar. </summary>
	[Property] public bool ToPlanet { get; set; }

	static GameObject _hangar, _planet;

	public (string Title, string Sub, string Tone) Prompt( SwrpPlayer me )
	{
		if ( !ToPlanet ) return ("LAAT/i shuttle: back to the Venator", "Ride up to the main hangar", "ok");
		if ( !TatooinePlanet.Open ) return ("LAAT/i shuttle to Tatooine", $"The ship is {( SwrpSpace.Jumping ? "in hyperspace" : $"at {SwrpSpace.Here.Name}" )}: it flies once we're over Tatooine", "denied");
		return ("LAAT/i shuttle to Tatooine", "Ride down to the surface", "ok");
	}

	public void Use( SwrpPlayer me )
	{
		if ( Prompt( me ).Tone == "denied" || me.InVehicle ) return;
		FeelTransition.Landing( RideSeconds );
		SwrpAudio.Ui( RideSound );
		RequestRide( ToPlanet );
	}

	// ----- host ----------------------------------------------------------------------------------------------------------

	static readonly Dictionary<Guid, RealTimeSince> _lastRide = new();

	[Rpc.Host]
	static void RequestRide( bool toPlanet )
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !p.IsValid() || p.CharacterId is null || p.InVehicle ) return;
		if ( _lastRide.TryGetValue( caller.Id, out var last ) && last < RideSeconds ) return;
		var from = toPlanet ? HangarSpot : PlanetSpot;
		if ( p.WorldPosition.Distance( from ) > Reach ) return;
		if ( toPlanet && !TatooinePlanet.Open ) { SwrpChat.SystemTo( caller, "The shuttle only flies down while the ship is over Tatooine." ); return; }
		_lastRide[caller.Id] = 0;
		_ = Ride( p, toPlanet );
	}

	static async System.Threading.Tasks.Task Ride( SwrpPlayer p, bool toPlanet )
	{
		await GameTask.DelaySeconds( MoveAfter );
		if ( !p.IsValid() ) return;
		var rot = Rotation.FromYaw( toPlanet ? PlanetYaw : HangarYaw );
		// step off the side of the bay, spread out so a squad doesn't land in one spot
		var side = Game.Random.Int( 0, 1 ) == 0 ? -1f : 1f;
		var off = rot * new Vector3( Game.Random.Float( -220f, 160f ), side * Game.Random.Float( 520f, 700f ), 0f );
		Vector3 at;
		if ( toPlanet ) at = TatooinePlanet.GroundAt( PlanetSpot + off ) + Vector3.Up * 4f;
		else
		{
			at = HangarSpot + off;
			at = at.WithZ( ( SwrpFlightDirector.DeckAt( p.Scene, at ) ?? HangarSpot.z ) + 4f );
		}
		p.TeleportTo( at, rot.Yaw() + side * 90f );     // facing away from the gunship
	}

	// ----- the parked gunships, on every machine -------------------------------------------------------------------------

	internal static void Tick( Scene scene )
	{
		if ( scene is null || !Game.IsPlaying || scene != Game.ActiveScene ) return;
		if ( !_hangar.IsValid() || _hangar.Scene != scene )
			_hangar = SwrpShip.Current is null ? null : Make( scene, "Tatooine shuttle (hangar)", HangarSpot.WithZ( SwrpFlightDirector.DeckAt( scene, HangarSpot ) ?? HangarSpot.z ), HangarYaw, true );
		var planet = TatooinePlanet.Loaded;
		if ( planet && ( !_planet.IsValid() || _planet.Scene != scene ) )
			_planet = Make( scene, "Tatooine shuttle (surface)", TatooinePlanet.GroundAt( PlanetSpot ), PlanetYaw, false );
		else if ( !planet && _planet.IsValid() ) { _planet.Destroy(); _planet = null; }
	}

	static GameObject Make( Scene scene, string name, Vector3 at, float yaw, bool toPlanet )
	{
		var def = CraftRegistry.Find( "laat" );
		if ( def is null ) return null;
		var go = scene.CreateObject();
		go.Name = name;
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = at;
		go.WorldRotation = Rotation.FromYaw( yaw );
		go.Components.Create<ModelRenderer>().Model = Model.Load( def.ModelPath );
		foreach ( var (center, size) in def.Boxes )
		{
			var b = go.Components.Create<BoxCollider>();
			b.Center = center;
			b.Scale = size;
			b.Static = true;
		}
		// gear down, bay doors open
		if ( def.GearModel is { } gear ) Part( go, "Landing gear", gear, Vector3.Zero );
		foreach ( var pd in def.Parts )
			if ( pd.ModelPath is { } mp ) Part( go, pd.Name, mp, pd.Motion == PartMotion.Slide ? pd.Pivot + pd.Slide : pd.Pivot );
		if ( def.CanopyModel is { } canopy ) Part( go, "Canopy", canopy, def.CanopyHinge );
		go.Components.Create<TatooineShuttle>().ToPlanet = toPlanet;
		return go;
	}

	static void Part( GameObject parent, string name, string model, Vector3 at )
	{
		var go = new GameObject( parent, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.LocalPosition = at;
		go.Components.Create<ModelRenderer>().Model = Model.Load( model );
	}
}
