using System;

namespace Swrp.Flight;

/// <summary>
/// The pilot's side of the 2026-10-21 flight pass (Angelo: boost "like afterburner", an "impressive first person hud",
/// missiles that look triple-A): the afterburner's heat, the FOV kick and engine flare while it's lit, and a missile lock
/// that builds while an enemy stays near the nose, with the lead point to put a torpedo on. FlightHud draws all of it.
/// </summary>
public sealed partial class SwrpFlightShip
{
	/// <summary> The afterburner's heat (0 cold, 1 burnt out); the owner flies it, every HUD aboard reads it. </summary>
	[Sync] public float Heat { get; set; }
	/// <summary> Run dry: no afterburner until it has cooled most of the way. </summary>
	[Sync] public bool Overheated { get; set; }

	bool _overheated;
	float _bankRate, _yawVel;
	Vector2 _turnVel;      // the fighter's yaw and pitch rates (deg/s)

	/// <summary> The local seat's view: true in the cockpit, false in the chase camera. </summary>
	public static bool LocalCockpitView => _localShip.IsValid() && _localSeat >= 0
		&& ( _localShip.Def.Seats[_localSeat].Role == SeatRole.Pilot ? _altView : !_altView );

	// ----- lock (the pilot's machine) -----------------------------------------------------------------------------------

	/// <summary> What the pilot is locking on to, and how far the lock has built (1 = locked). </summary>
	public GameObject LockTarget { get; private set; }
	public float LockAmount { get; private set; }
	/// <summary> Where to aim a torpedo to meet the locked target (its speed times the torpedo's flight time ahead of it). </summary>
	public Vector3? LeadPoint { get; private set; }

	const float LockCone = 12f, LockRange = 12000f, LockSeconds = 1.1f;
	RealTimeSince _sinceLockScan = 10f;

	/// <summary> Local, every frame in the pilot's seat: keep the target nearest the nose (in a 12 degree cone) and build the lock. </summary>
	void UpdateLock()
	{
		var w = Def.Seats[_localSeat].Weapons.Select( id => Def.Weapon( id ) ).FirstOrDefault( x => x is { Kind: WeaponKind.Missile } );
		if ( w is null || Wrecked ) { LockTarget = null; LockAmount = 0f; LeadPoint = null; return; }
		var fwd = WorldRotation.Forward;
		if ( _sinceLockScan > 0.25f )
		{
			_sinceLockScan = 0;
			var me = SwrpPlayer.Local;
			GameObject best = null;
			var bestAngle = LockCone;
			foreach ( var n in Scene.GetAllComponents<SwrpNpc>() )
			{
				if ( n.IsDead || me.IsValid() && !AllegianceRegistry.AreHostile( me.Allegiance, n.Allegiance ) ) continue;
				var to = n.WorldPosition + Vector3.Up * 40f - WorldPosition;
				if ( to.Length > LockRange ) continue;
				var a = Vector3.GetAngle( fwd, to );
				if ( a < bestAngle ) { bestAngle = a; best = n.GameObject; }
			}
			foreach ( var s in All )
			{
				if ( s == this || s.Wrecked || !s.Pilot.IsValid() || me.IsValid() && !AllegianceRegistry.AreHostile( me.Allegiance, s.Pilot.Allegiance ) ) continue;
				var to = s.WorldPosition - WorldPosition;
				if ( to.Length > LockRange ) continue;
				var a = Vector3.GetAngle( fwd, to );
				if ( a < bestAngle ) { bestAngle = a; best = s.GameObject; }
			}
			if ( best != LockTarget ) LockAmount = 0f;
			LockTarget = best;
		}
		if ( !LockTarget.IsValid() ) { LockTarget = null; LockAmount = 0f; LeadPoint = null; return; }
		var p = LockTarget.WorldPosition + Vector3.Up * 40f;
		var inCone = Vector3.GetAngle( fwd, p - WorldPosition ) < LockCone;
		LockAmount = Math.Clamp( LockAmount + ( inCone ? Time.Delta / LockSeconds : -Time.Delta * 2f ), 0f, 1f );
		var vel = LockTarget.Components.Get<Rigidbody>()?.Velocity ?? Vector3.Zero;
		var t = p.Distance( WorldPosition ) / MathF.Max( 1f, w.Speed + Vector3.Dot( Velocity, ( p - WorldPosition ).Normal ) );
		LeadPoint = p + vel * t;
	}

	/// <summary> Local: the locked target for a missile fired now (none until the lock has built). </summary>
	Guid LockedId => LockAmount >= 1f && LockTarget.IsValid() ? LockTarget.Id : Guid.Empty;

	// ----- boost: FOV kick and engine flare -----------------------------------------------------------------------------

	float _fovKick;
	readonly List<SpriteRenderer> _flares = new();

	/// <summary> Local camera: widen the view a little while the afterburner is lit, as in Battlefront II. </summary>
	void BoostFov( CameraComponent cam )
	{
		_fovKick = _fovKick.Approach( Boosting ? 1f : 0f, Time.Delta * ( Boosting ? 2.5f : 1.5f ) );
		cam.FieldOfView = Preferences.FieldOfView + 9f * _fovKick * _fovKick * ( 3f - 2f * _fovKick );
	}

	readonly List<(LineRenderer Line, List<Vector3> Points)> _trails = new();

	/// <summary>
	/// Every screen: thin, faint vapour trails off the wingtips (the LAAT: behind it), only at speed, stronger in a hard turn,
	/// gone in a hover (Angelo, 2026-10-21). Each is the tip's last 40 positions.
	/// </summary>
	void Contrails()
	{
		if ( Def.Contrails.Length == 0 ) return;
		if ( _trails.Count == 0 )
			foreach ( var _ in Def.Contrails )
			{
				var go = new GameObject( true, "Contrail" );
				go.NetworkMode = NetworkMode.Never;
				go.Flags |= GameObjectFlags.NotSaved;
				var line = go.Components.Create<LineRenderer>();
				line.UseVectorPoints = true;
				line.Additive = false;
				line.Lighting = false;
				line.CastShadows = false;
				line.Width = new Curve( new Curve.Frame( 0f, 7f ), new Curve.Frame( 1f, 2f ) );
				_trails.Add( (line, new List<Vector3>()) );
			}
		var speed = Velocity.Length;
		var turn = Math.Clamp( MathF.Abs( WorldRotation.Angles().roll ) / 60f, 0f, 1f );
		var alpha = Wrecked ? 0f : Math.Clamp( ( speed - 900f ) / 1600f, 0f, 1f ) * ( 0.12f + 0.2f * turn );
		for ( var i = 0; i < _trails.Count; i++ )
		{
			var (line, pts) = _trails[i];
			if ( !line.IsValid() ) continue;
			pts.Add( WorldTransform.PointToWorld( Def.Contrails[i] ) );
			if ( pts.Count > 40 ) pts.RemoveAt( 0 );
			line.Enabled = alpha > 0.005f && pts.Count > 1;
			line.VectorPoints = pts;
			line.Color = new Gradient( new Gradient.ColorFrame( 0f, Color.White.WithAlpha( 0f ) ), new Gradient.ColorFrame( 1f, Color.White.WithAlpha( alpha ) ) );
		}
	}

	/// <summary> Every screen: the engines glow with power, and flare long and hot on the afterburner. </summary>
	void EngineFlares()
	{
		if ( Def.EngineGlows.Length == 0 ) return;
		if ( _flares.Count == 0 )
			foreach ( var e in Def.EngineGlows )
			{
				var go = new GameObject( GameObject, true, "Engine glow" );
				go.NetworkMode = NetworkMode.Never;
				go.Flags |= GameObjectFlags.NotSaved;
				go.LocalPosition = e;
				var r = go.Components.Create<SpriteRenderer>();
				r.Sprite = SwrpFx.GlowSprite;
				r.Additive = true;
				r.Lighting = false;
				r.Shadows = false;
				_flares.Add( r );
			}
		var on = Wrecked ? 0f : Spool;
		var hot = Boosting ? 1f : 0f;
		foreach ( var r in _flares )
		{
			if ( !r.IsValid() ) continue;
			r.Enabled = on > 0.02f;
			var size = Def.EngineGlowSize * ( 0.5f + 0.5f * Throttle + 0.9f * hot ) * on;
			r.Size = new Vector2( size, size );
			r.Color = Color.Lerp( new Color( 0.55f, 0.75f, 1f ), new Color( 1f, 0.75f, 0.55f ), hot * 0.35f ) * ( 2.5f + 4f * hot ) * on;
		}
	}
}
