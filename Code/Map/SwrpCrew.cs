using System;
using Sandbox.Navigation;

namespace Swrp;

/// <summary>
/// One of the ship's ambient crew: a navy crewman (a clone in grey fatigues) at a console or walking a beat, an astromech
/// trundling about with its dome turning, or a mouse droid scurrying along a corridor. Walks its route back and forth,
/// pausing at each end; one point means it stands there. Between waypoints it follows a navmesh path (built on this machine
/// the first time it comes near your camera), so it keeps clear of walls, consoles and parked ships instead of cutting
/// straight through them; until that path is ready, or if there is none, it waits at its first point. Purely cosmetic: no
/// collision, no networking, and it only runs near your camera (part of swrp_ship_life). Placed by MapSource/notorious_scene.py.
/// </summary>
[Title( "SWRP Crew" ), Category( "SWRP" ), Icon( "groups" )]
public sealed class SwrpCrew : Component
{
	/// <summary> officer, astromech or mouse. </summary>
	[Property] public string Kind { get; set; } = "officer";
	/// <summary> Waypoints "x y z; x y z; ...", each dropped onto the floor below it. One point: stands there. </summary>
	[Property] public string Route { get; set; } = "";
	/// <summary> The way it faces while standing still, in degrees. </summary>
	[Property] public float Yaw { get; set; }

	readonly List<Vector3> _points = new();
	bool _built;

	// the route as walked: navmesh paths between the waypoints, and which of those points are real waypoints (pause there)
	List<Vector3> _path;
	HashSet<int> _stops;
	bool _navRequested, _navFailed;
	string _navNote = "not planned yet";
	RealTimeSince _sinceNavTry = 10;
	RealTimeSince _sinceNavStart;

	/// <summary> How long to wait for the navmesh around a route before giving up (the crewman then just stands). </summary>
	const float NavTimeout = 40f;
	int _target = 1;
	int _step = 1;
	float _yaw;
	float _look;
	Vector3 _velocity;
	RealTimeUntil _wait;
	RealTimeUntil _nextLook;
	RealTimeUntil _nextSound;

	GameObject _body;
	SwrpOfficerTasks _tasks;
	SkinnedModelRenderer _skin;
	GameObject _dome;
	float _domeYaw, _domeTarget;
	SkinnedModelRenderer _astro;
	Rotation _domeRest;          // the BF2 dome bone's rest rotation relative to the body
	bool _domeBone;

	float Speed => Kind switch { "mouse" => 150f, "astromech" => 55f, _ => 72f };

	protected override void OnUpdate()
	{
		if ( !_built ) Build();
		if ( !_body.IsValid() ) return;

		var p = _body.WorldPosition;
		var show = SwrpLivingShip.Enabled && SwrpLivingShip.Near( Scene, p, 4200f );
		if ( show != _body.Enabled ) _body.Enabled = show;
		if ( !_body.Enabled || !SwrpLivingShip.Near( Scene, p, SwrpLivingShip.ActiveRange ) )
		{
			_velocity = Vector3.Zero;
			return;
		}

		var dt = Time.Delta;
		if ( _path is null ) Plan();
		if ( _path is not null && _path.Count > 1 ) Walk( dt );
		else Stand( dt );

		_body.WorldRotation = Rotation.FromYaw( _yaw + _look );
		Animate( dt );
	}

	void Build()
	{
		_built = true;
		foreach ( var part in ( Route ?? "" ).Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
		{
			var v = SwrpLivingShip.ParseVector( part );
			_points.Add( v.WithZ( SwrpLivingShip.FloorBelow( Scene, v ) ) );
		}
		if ( _points.Count == 0 ) _points.Add( WorldPosition.WithZ( SwrpLivingShip.FloorBelow( Scene, WorldPosition ) ) );

		_body = SwrpLivingShip.Child( GameObject, Kind );
		_body.WorldPosition = _points[0];
		_yaw = _points.Count > 1 ? Rotation.LookAt( ( _points[1] - _points[0] ).WithZ( 0 ) ).Yaw() : Yaw;
		_wait = Game.Random.Float( 0f, 3f );
		_nextSound = Game.Random.Float( 4f, 15f );

		switch ( Kind )
		{
			case "astromech":
				// Battlefront II's R2-D2, or now and then R5-D4 (ModelSource/bf2_astromechs.py; local only); the dome is its
				// "Head" bone. The original primitive droid is the fallback when the BF2 models aren't in this copy.
				var bf2 = Model.Load( Math.Abs( GameObject.Id.GetHashCode() ) % 3 == 0 ? "models/crew/bf2_r5d4.vmdl" : "models/crew/bf2_r2d2.vmdl" );
				if ( bf2 is { IsError: false } )
				{
					_astro = _body.Components.Create<SkinnedModelRenderer>();
					_astro.Model = bf2;
					_astro.UseAnimGraph = false;
					_astro.CreateBoneObjects = true;
					_body.WorldScale = 1.1f;
					break;
				}
				_body.Components.Create<ModelRenderer>().Model = Model.Load( "models/crew/astromech_body.vmdl" );
				_dome = SwrpLivingShip.Child( _body, "dome" );
				_dome.LocalPosition = Vector3.Up * 33.7f;
				_dome.Components.Create<ModelRenderer>().Model = Model.Load( "models/crew/astromech_dome.vmdl" );
				_body.WorldScale = 1.15f;
				break;
			case "mouse":
				_body.Components.Create<ModelRenderer>().Model = Model.Load( "models/crew/mouse_droid.vmdl" );
				break;
			default:
				_skin = _body.Components.Create<SkinnedModelRenderer>();
				// a clone in grey navy fatigues (ModelSource/crew_droids.py)
				var navy = Model.Load( "models/crew/crewman_navy.vmdl" );
				_skin.Model = navy is { IsError: false } ? navy : ClassRegistry.Default.LoadModel() ?? Model.Load( "models/citizen/citizen.vmdl" );
				_skin.UseAnimGraph = true;
				_body.WorldScale = SwrpPlayer.PlayerScaleFor( Scene );
				_tasks = new SwrpOfficerTasks( _points.Count < 2 );
				break;
		}
		foreach ( var r in _body.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	/// <summary>
	/// Turn the waypoints into navmesh paths: ask for the tiles round the route (built in the background), then path each leg.
	/// Droids standing at one spot are moved onto the navmesh too, so they don't sit half inside a wall or console.
	/// </summary>
	void Plan()
	{
		if ( _navFailed || _sinceNavTry < 0.5f ) return;
		_sinceNavTry = 0;
		var droid = Kind is "astromech" or "mouse";
		if ( _points.Count < 2 && !droid ) { _path = new List<Vector3>( _points ); _navNote = "stands at its post"; return; }

		var nav = Scene.NavMesh;
		if ( nav is null ) { Fail( "no navmesh in this scene" ); return; }
		if ( !_navRequested )
		{
			_navRequested = true;
			_sinceNavStart = 0;
			if ( !nav.IsEnabled ) nav.IsEnabled = true;
			var lo = _points.Aggregate( ( a, b ) => a.ComponentMin( b ) ) - new Vector3( 400f, 400f, 200f );
			var hi = _points.Aggregate( ( a, b ) => a.ComponentMax( b ) ) + new Vector3( 400f, 400f, 200f );
			nav.RequestTilesGeneration( new BBox( lo, hi ) );
			_navNote = "waiting for the navmesh";
			return;
		}

		var path = new List<Vector3>();
		var stops = new HashSet<int>();
		for ( var i = 0; i < Math.Max( 1, _points.Count - 1 ); i++ )
		{
			var from = _points[i];
			var to = _points.Count > 1 ? _points[i + 1] : from;
			var leg = nav.CalculatePath( new CalculatePathRequest { Start = from, Target = to } );
			var pts = leg.IsValid ? leg.Points.Select( p => p.Position ).ToList() : null;
			var reached = pts is { Count: > 0 } && ( leg.Status == NavMeshPathStatus.Complete || pts[^1].WithZ( 0 ).Distance( to.WithZ( 0 ) ) < 48f );
			if ( !reached )
			{
				if ( _sinceNavStart > NavTimeout ) Fail( $"no navmesh path from waypoint {i + 1} to {i + 2} ({leg.Status})" );
				return;     // tiles still building: try again shortly
			}
			if ( path.Count == 0 ) { path.Add( Grounded( pts[0] ) ); stops.Add( 0 ); }
			foreach ( var p in pts.Skip( 1 ) )
				if ( p.Distance( path[^1] ) > 2f ) path.Add( Grounded( p ) );
			stops.Add( path.Count - 1 );
		}

		_path = path;
		_stops = stops;
		_target = Math.Min( 1, path.Count - 1 );
		_step = 1;
		_body.WorldPosition = path[0];
		_navNote = $"{path.Count} path points over {_points.Count} waypoints";
		if ( path.Count > 1 ) _yaw = Rotation.LookAt( ( path[1] - path[0] ).WithZ( 0 ) ).Yaw();
	}

	Vector3 Grounded( Vector3 p ) => p.WithZ( SwrpLivingShip.FloorBelow( Scene, p + Vector3.Up * 20f ) );

	void Fail( string why )
	{
		_navFailed = true;
		_navNote = $"stands still: {why}";
		_path = new List<Vector3> { _body.WorldPosition };
	}

	void Walk( float dt )
	{
		if ( _wait > 0f )
		{
			_velocity = Vector3.Zero;
			return;
		}
		var pos = _body.WorldPosition;
		var goal = _path[_target];
		var to = goal - pos;
		var flat = to.WithZ( 0 );
		var dist = flat.Length;
		if ( dist < 6f )
		{
			_body.WorldPosition = goal;
			var stop = _stops.Contains( _target );
			// at the end of the beat turn round; mouse droids sometimes dart back early
			if ( _target == 0 || _target == _path.Count - 1 ) _step = -_step;
			_target = Math.Clamp( _target + _step, 0, _path.Count - 1 );
			if ( !stop ) return;     // a corner of the path: keep going
			_wait = Kind switch { "mouse" => Game.Random.Float( 0.3f, 1.8f ), "astromech" => Game.Random.Float( 1f, 4f ), _ => Game.Random.Float( 5f, 12f ) };     // officers stop for a task
			_velocity = Vector3.Zero;
			if ( Kind == "mouse" && Game.Random.Float() < 0.45f ) Beep( "sounds/ambient/mouse_droid.sound" );
			return;
		}
		if ( Kind == "mouse" && Game.Random.Float() < dt * 0.08f )
		{
			_step = -_step;
			_target = Math.Clamp( _target + _step, 0, _path.Count - 1 );
			_wait = Game.Random.Float( 0.2f, 0.6f );
			return;
		}

		var dir = flat / dist;
		var wantYaw = Rotation.LookAt( dir ).Yaw();
		var turnRate = Kind == "mouse" ? 540f : 200f;
		var delta = MathX.DeltaDegrees( _yaw, wantYaw );
		_yaw += Math.Clamp( delta, -turnRate * dt, turnRate * dt );
		// walk once roughly facing the way
		var speed = MathF.Abs( delta ) < 45f ? Speed : Speed * 0.2f;
		var stepLen = MathF.Min( dist, speed * dt );
		var next = pos + dir * stepLen;
		// follow the slope between points (stairs and ramps)
		var seg = _path[_target] - _path[Math.Clamp( _target - _step, 0, _path.Count - 1 )];
		var segFlat = seg.WithZ( 0 ).Length;
		if ( segFlat > 1f ) next.z = goal.z - ( dist - stepLen ) / segFlat * seg.z;
		_velocity = ( next - pos ) / MathF.Max( dt, 0.0001f );
		_body.WorldPosition = next;
		_look = _look.LerpTo( 0f, dt * 3f );
	}

	void Stand( float dt )
	{
		_velocity = Vector3.Zero;
		if ( _points.Count < 2 ) _yaw = Yaw;
		// crew at their posts glance about now and then
		if ( _nextLook <= 0f )
		{
			_nextLook = Game.Random.Float( 5f, 14f );
			_domeTarget = Game.Random.Float( -120f, 120f );
			_lookTarget = Game.Random.Float() < 0.5f ? 0f : Game.Random.Float( -25f, 25f );
		}
		_look = _look.LerpTo( _lookTarget, dt * 1.5f );
	}

	float _lookTarget;

	void Animate( float dt )
	{
		if ( _skin.IsValid() )
		{
			var local = _body.WorldRotation.Inverse * _velocity;
			var speed = _velocity.Length;
			_skin.Set( "b_grounded", true );
			_skin.Set( "holdtype", 0 );
			_skin.Set( "move_groundspeed", speed );
			_skin.Set( "move_speed", speed );
			_skin.Set( "move_x", local.x );
			_skin.Set( "move_y", local.y );
			_skin.Set( "wish_groundspeed", speed );
			_skin.Set( "wish_speed", speed );
			_skin.Set( "wish_x", local.x );
			_skin.Set( "wish_y", local.y );
			_tasks?.Tick( _skin, dt, speed > 4f );
			return;
		}

		if ( Kind == "astromech" )
		{
			// rocks a little as it rolls, and its dome looks about
			var rolling = _velocity.Length > 1f;
			_body.WorldRotation = Rotation.FromYaw( _yaw ) * Rotation.FromRoll( rolling ? MathF.Sin( Time.Now * 7f ) * 2.5f : 0f );
			if ( _nextLook <= 0f )
			{
				_nextLook = Game.Random.Float( 2f, 6f );
				_domeTarget = Game.Random.Float( -110f, 110f );
			}
			_domeYaw = _domeYaw.Approach( _domeTarget, dt * 90f );
			if ( _astro.IsValid() && !_domeBone && _astro.GetBoneObject( "Head" ) is { } head )
			{
				_dome = head;
				_domeRest = _body.WorldRotation.Inverse * head.WorldRotation;
				head.Flags |= GameObjectFlags.ProceduralBone;
				_domeBone = true;
			}
			if ( _domeBone && _dome.IsValid() ) _dome.WorldRotation = _body.WorldRotation * Rotation.FromYaw( _domeYaw ) * _domeRest;
			else if ( _dome.IsValid() ) _dome.LocalRotation = Rotation.FromYaw( _domeYaw );
			if ( _nextSound <= 0f )
			{
				_nextSound = Game.Random.Float( 8f, 22f );
				Beep( "sounds/ambient/astromech.sound" );
			}
		}
	}

	[ConCmd( "swrp_crew_info", Help = "swrp_crew_info - list the ambient crew: kind, where, and whether it is running near you" )]
	public static void InfoCommand()
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		foreach ( var c in scene.GetAllComponents<SwrpCrew>() )
		{
			var p = c._body.IsValid() ? c._body.WorldPosition : c.WorldPosition;
			Log.Info( $"[SWRP] crew {c.Kind} at ({p.x:0} {p.y:0} {p.z:0}) {( c._body.IsValid() && c._body.Enabled ? "shown" : "hidden" )}, route: {c._navNote}, {( c._tasks is null ? "" : $"task {c._tasks.Current}, " )}{c._skin?.Model?.ResourcePath} {( c._skin.IsValid() ? string.Join( ", ", Enumerable.Range( 0, c._skin.Materials.Count ).Select( i => c._skin.Materials.GetOriginal( i )?.ResourcePath ) ) : "" )}" );
		}
	}

	void Beep( string sound )
	{
		var h = Sound.Play( sound, _body.WorldPosition + Vector3.Up * 20f );
		if ( h is not null ) h.Volume *= Math.Clamp( SwrpAmbience.Volume / 0.5f, 0f, 1f );
	}
}
