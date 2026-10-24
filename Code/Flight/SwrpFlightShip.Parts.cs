using System;

namespace Swrp.Flight;

/// <summary>
/// The moving parts, on every screen from the craft's own state (no extra network traffic):
/// - the canopy swings open while the craft sits cold on the deck with someone at it, and shuts as the engines spool;
/// - the landing gear folds up into the hull once the craft is well off the ground under power, and drops for landing
///   (its collision box goes with it).
/// </summary>
public sealed partial class SwrpFlightShip
{
	GameObject _gearGo, _canopyGo;
	GameObject[] _extraGo = System.Array.Empty<GameObject>();
	float _doorK = 1f;
	readonly Dictionary<int, Vector3> _seatAim = new();
	RealTimeSince _sinceAimShared;

	/// <summary> The bay doors (gunship): open by default, as in the films; the pilot toggles them with 0. </summary>
	[Sync] public bool DoorsOpen { get; set; } = true;
	readonly List<BoxCollider> _boxes = new();
	bool _partsOn;
	float _gearK = 1f, _canopyK;
	bool _gearDown = true, _canopyOpen;
	RealTimeSince _sinceAltitude = 10f;
	float _altitude;

	/// <summary> The landing gear is down (or on its way down). </summary>
	public bool GearDown => _gearDown;
	public float GearAmount => _gearK;
	public bool CanopyOpen => _canopyOpen;

	/// <summary> Height above the ground, re-measured a few times a second. </summary>
	public float AltitudeCached
	{
		get
		{
			if ( _sinceAltitude > 0.2f ) { _sinceAltitude = 0; _altitude = Altitude; }
			return _altitude;
		}
	}

	/// <summary> Once: the moving parts that have their own models (gear, canopy, doors, turrets) as children. </summary>
	void PartsTick()
	{
		if ( _partsOn || Def is null ) return;
		_partsOn = true;
		if ( Def.GearModel is { } gear )
		{
			_gearGo = new GameObject( GameObject, true, "Landing gear" );
			_gearGo.NetworkMode = NetworkMode.Never;
			_gearGo.Flags |= GameObjectFlags.NotSaved;
			_gearGo.Components.Create<ModelRenderer>().Model = Model.Load( gear );
		}
		_extraGo = new GameObject[Def.Parts.Length];
		for ( var i = 0; i < Def.Parts.Length; i++ )
		{
			if ( Def.Parts[i].ModelPath is not { } mp ) continue;
			var m = Model.Load( mp );
			var go = new GameObject( GameObject, true, Def.Parts[i].Name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = Def.Parts[i].Pivot;
			go.Components.Create<ModelRenderer>().Model = m;
			_extraGo[i] = go;
		}
		if ( Def.CanopyModel is { } canopy )
		{
			_canopyGo = new GameObject( GameObject, true, "Canopy" );
			_canopyGo.NetworkMode = NetworkMode.Never;
			_canopyGo.Flags |= GameObjectFlags.NotSaved;
			_canopyGo.LocalPosition = Def.CanopyHinge;
			_canopyGo.Components.Create<ModelRenderer>().Model = Model.Load( canopy );
		}
	}

	void AnimateParts()
	{
		if ( Def is null ) return;
		var dt = Time.Delta;

		// gear: up once climbing away under power, down for landing or with the engines off
		var powered = EngineOn && Spool > 0.6f && !Wrecked;
		if ( !powered ) _gearDown = true;
		else
		{
			var alt = AltitudeCached;
			if ( _gearDown && alt > 320f ) _gearDown = false;
			else if ( !_gearDown && alt < 220f ) _gearDown = true;
		}
		if ( ForceGear >= 0 ) _gearDown = ForceGear == 1;
		_gearK = _gearK.Approach( _gearDown ? 1f : 0f, dt / 1.2f );
		if ( _gearGo.IsValid() )
		{
			var s = 0.06f + 0.94f * _gearK;
			_gearGo.LocalScale = new Vector3( 1f, 1f, s );
			_gearGo.LocalPosition = new Vector3( 0f, 0f, Def.GearTop * ( 1f - s ) );
			_gearGo.Enabled = _gearK > 0.02f;
		}
		if ( Def.GearBox >= 0 && Def.GearBox < _boxes.Count && _boxes[Def.GearBox].IsValid() ) _boxes[Def.GearBox].Enabled = _gearK > 0.6f;

		// doors slide back along their rails; turrets swing to whoever mans them (or back to rest)
		var doorWas = _doorK;
		_doorK = _doorK.Approach( DoorsOpen ? 1f : 0f, dt / 1.6f );
		if ( ( doorWas == 0f || doorWas == 1f ) && doorWas != _doorK && Def.HasDoors ) Sound.Play( "sounds/flight/door.sound", WorldPosition );
		for ( var i = 0; i < _extraGo.Length; i++ )
		{
			var go = _extraGo[i];
			if ( !go.IsValid() ) continue;
			var pd = Def.Parts[i];
			if ( pd.Motion == PartMotion.Slide )
			{
				var k = _doorK * _doorK * ( 3f - 2f * _doorK );
				go.LocalPosition = pd.Pivot + pd.Slide * k;
			}
			else
			{
				var want = pd.Rest;
				if ( pd.Seat >= 0 && Occupant( pd.Seat ) != Guid.Empty && _seatAim.TryGetValue( pd.Seat, out var aim ) )
					want = Clamp( WorldRotation.Inverse * aim, pd.Rest, pd.Arc );
				go.LocalRotation = Rotation.Slerp( go.LocalRotation, Rotation.LookAt( want ), MathF.Min( 1f, dt * 8f ) );      // (modelled with the guns along +x)
			}
		}

		// canopy: open on the deck, cold, with someone at the cockpit or aboard; shut to fly
		var open = !Wrecked && !EngineOn && Spool < 0.2f && Velocity.Length < 150f && ( Count > 0 || SomeoneNear( WorldTransform.PointToWorld( Def.CanopyCenter ), 420f ) );
		if ( ForceCanopy >= 0 ) open = ForceCanopy == 1;
		if ( open != _canopyOpen )
		{
			_canopyOpen = open;
			if ( _canopyGo.IsValid() ) Sound.Play( "sounds/flight/seat.sound", _canopyGo.WorldPosition );
		}
		_canopyK = _canopyK.Approach( _canopyOpen ? 1f : 0f, dt / 1.3f );
		if ( _canopyGo.IsValid() )
		{
			var k = _canopyK * _canopyK * ( 3f - 2f * _canopyK );
			_canopyGo.LocalRotation = Rotation.FromPitch( -Def.CanopyOpenAngle * k );
		}
	}

	/// <summary> Dev, this machine: hold every craft's gear / canopy (-1 = automatic, 0 = up / shut, 1 = down / open). </summary>
	[ConVar( "swrp_ship_gear", Help = "Dev, this machine: -1 automatic (default), 0 every craft's gear up, 1 down" )]
	public static int ForceGear { get; set; } = -1;

	[ConVar( "swrp_ship_canopy", Help = "Dev, this machine: -1 automatic (default), 0 every canopy shut, 1 open" )]
	public static int ForceCanopy { get; set; } = -1;

	/// <summary> A gunner's aim, for their turret on every screen (sent a few times a second by the gunner's machine). </summary>
	[Rpc.Broadcast( NetFlags.Unreliable )]
	void ShareAim( int seat, Vector3 dir )
	{
		var p = PlayerIn( seat );
		if ( !p.IsValid() || p.Network.Owner != Rpc.Caller ) return;
		_seatAim[seat] = dir.Normal;
	}

	/// <summary> Local: this machine's trooper is in a seat with turrets: aim them and tell everyone else. </summary>
	void LocalTurretAim( int seat, Vector3 dir )
	{
		_seatAim[seat] = dir;
		if ( !Def.Parts.Any( p => p.Seat == seat ) || _sinceAimShared < 0.1f ) return;
		_sinceAimShared = 0;
		ShareAim( seat, dir );
	}

	/// <summary> Pilot to host, before the craft is theirs: open or shut the bay doors. </summary>
	[Rpc.Host]
	void RequestDoors( bool open )
	{
		var p = SwrpGameManager.Current?.FindPlayer( Rpc.Caller );
		if ( p.IsValid() && SeatOf( p ) == Def.PilotSeat && !IsProxy ) DoorsOpen = open;
	}

	RealTimeSince _sinceNear = 10f;
	bool _near;

	bool SomeoneNear( Vector3 at, float range )
	{
		if ( _sinceNear < 0.3f ) return _near;
		_sinceNear = 0;
		_near = Scene.GetAllComponents<SwrpPlayer>().Any( p => p.CharacterId is not null && p.WorldPosition.Distance( at ) < range );
		return _near;
	}
}
