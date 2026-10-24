using System;

namespace Swrp;

/// <summary>
/// The ship's cosmetic life: flickering and pulsing lights, steam vents, sparking panels, animated wall screens and the
/// ambient crew (<see cref="SwrpCrew"/>). Placed by MapSource/notorious_scene.py. Nothing here is networked, solid or
/// gameplay-relevant; each machine runs its own, only near its camera. swrp_ship_life 0 turns it all off.
/// </summary>
public static class SwrpLivingShip
{
	[ConVar( "swrp_ship_life", Help = "The ship's cosmetic life: flickering lights, steam, sparks, wall screens and ambient crew (1 = on, 0 = off)", Saved = true )]
	public static bool Enabled { get; set; } = true;

	/// <summary> How far from the camera things keep running (and drawing). </summary>
	public const float ActiveRange = 3200f;

	public static bool Near( Scene scene, Vector3 p, float range = ActiveRange )
	{
		var cam = scene?.Camera;
		return cam.IsValid() && cam.WorldPosition.DistanceSquared( p ) < range * range;
	}

	/// <summary> Where a placement's probe ray meets a wall: the point and the wall's normal (false if nothing within reach). </summary>
	public static bool FindWall( Scene scene, Vector3 from, Vector3 dir, out Vector3 point, out Vector3 normal, float reach = 700f )
	{
		var tr = Ray( scene, from, from + dir.Normal * reach );
		point = tr.Pos;
		normal = tr.Normal;
		return tr.Hit && !tr.StartedSolid;
	}

	/// <summary> A ray for placing fittings against the map, its props and the corridor kit (doors, consoles, walls). </summary>
	static (bool Hit, bool StartedSolid, Vector3 Pos, Vector3 Normal, GameObject Go) Ray( Scene scene, Vector3 a, Vector3 b )
	{
		var tr = scene.Trace.Ray( a, b ).WithoutTags( "trigger", "player", "npc" ).Run();
		return ( tr.Hit, tr.StartedSolid, tr.HitPosition, tr.Normal, tr.GameObject );
	}

	/// <summary>
	/// Somewhere to hang a flat fitting (a screen, a rack): from the wall the probe finds, look along it for a flat, clear
	/// stretch the whole fitting fits on (half width and height, plus a margin) with its centre <paramref name="height"/>
	/// above the floor: no doors, door panels or consoles behind it, and nothing within <paramref name="clearance"/> in
	/// front. Returns the wall point under the centre and the wall's normal.
	/// </summary>
	public static bool FindMount( Scene scene, Vector3 from, Vector3 probe, float halfWidth, float halfHeight, float height, float clearance, out Vector3 surface, out Vector3 normal )
	{
		surface = default;
		normal = default;
		if ( !FindWall( scene, from, probe, out var p, out var n ) || MathF.Abs( n.z ) > 0.3f ) return false;
		n = n.WithZ( 0 ).Normal;
		var along = Vector3.Cross( Vector3.Up, n ).Normal;
		var floor = FloorBelow( scene, p + n * 20f );
		foreach ( var off in new[] { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 130f, -130f, 160f, -160f } )
		{
			foreach ( var h in new[] { height, height + 10f, height - 8f } )
			{
				var guess = new Vector3( p.x, p.y, floor + h ) + along * off;
				if ( !FlatWall( scene, guess, n, along, halfWidth + 2f, halfHeight + 2f, clearance, out var at ) ) continue;
				surface = at;
				normal = n;
				return true;
			}
		}
		return false;
	}

	static bool FlatWall( Scene scene, Vector3 centre, Vector3 n, Vector3 along, float w, float h, float clearance, out Vector3 surface )
	{
		surface = default;
		var depth = 0f;
		var first = true;
		foreach ( var dx in new[] { -w, 0f, w } )
		{
			foreach ( var dz in new[] { -h, 0f, h } )
			{
				var s = centre + along * dx + Vector3.Up * dz;
				var tr = Ray( scene, s + n * 40f, s - n * 40f );
				if ( !tr.Hit || tr.StartedSolid || Vector3.Dot( tr.Normal, n ) < 0.97f ) return false;
				var go = tr.Go;
				if ( go is not null && ( go.Components.GetInAncestorsOrSelf<SwrpDoor>() is not null || go.Components.GetInAncestorsOrSelf<SwrpDoorPanel>() is not null
					|| go.Components.GetInAncestorsOrSelf<SwrpShipConsole>() is not null || go.Components.GetInAncestorsOrSelf<SwrpArmory>() is not null ) ) return false;
				var d = Vector3.Dot( tr.Pos - centre, n );
				if ( first ) { depth = d; first = false; }
				else if ( MathF.Abs( d - depth ) > 1.5f ) return false;
				if ( dx == 0f && dz == 0f ) surface = tr.Pos;
			}
		}
		// nothing standing in front of it (pipes, railings, furniture)
		foreach ( var dx in new[] { -w * 0.8f, 0f, w * 0.8f } )
		{
			var s = surface + along * dx;
			if ( Ray( scene, s + n * 3f, s + n * clearance ).Hit ) return false;
		}
		return true;
	}

	public static float FloorBelow( Scene scene, Vector3 p )
	{
		var tr = Ray( scene, p + Vector3.Up * 60f, p + Vector3.Down * 400f );
		return tr.Hit ? tr.Pos.z : p.z;
	}

	[ConCmd( "swrp_life_info", Help = "swrp_life_info - list the living ship's lights, vents, sparking panels and screens and where they ended up" )]
	public static void InfoCommand()
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		Log.Info( $"[SWRP] ship life {( Enabled ? "on" : "off" )}" );
		foreach ( var c in scene.GetAllComponents<SwrpLightFx>() ) Log.Info( $"[SWRP] life {c.Describe()}" );
		foreach ( var c in scene.GetAllComponents<SwrpSteamVent>() ) Log.Info( $"[SWRP] life {c.Describe()}" );
		foreach ( var c in scene.GetAllComponents<SwrpSparkPanel>() ) Log.Info( $"[SWRP] life {c.Describe()}" );
		foreach ( var c in scene.GetAllComponents<SwrpWallScreen>() ) Log.Info( $"[SWRP] life {c.Describe()}" );
	}

	[ConCmd( "swrp_life_sparks", Help = "swrp_life_sparks - make every sparking panel near you spark now (for testing)" )]
	public static void SparksCommand()
	{
		foreach ( var c in Game.ActiveScene?.GetAllComponents<SwrpSparkPanel>() ?? Enumerable.Empty<SwrpSparkPanel>() ) c.SparkNow();
	}

	internal static string At( Vector3 p ) => $"({p.x:0} {p.y:0} {p.z:0})";

	internal static GameObject Child( GameObject parent, string name )
	{
		var go = new GameObject( parent, true, name );
		go.Flags |= GameObjectFlags.NotSaved;
		go.NetworkMode = NetworkMode.Never;
		return go;
	}

	public static Vector3 ParseVector( string s )
	{
		var p = s.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		return p.Length >= 3 && float.TryParse( p[0], out var x ) && float.TryParse( p[1], out var y ) && float.TryParse( p[2], out var z ) ? new Vector3( x, y, z ) : Vector3.Zero;
	}
}

/// <summary> A ceiling light that flickers (a failing tube) or pulses slowly (the reactor's throb): the nearest light to it. </summary>
[Title( "SWRP Light Fx" ), Category( "SWRP" ), Icon( "flare" )]
public sealed class SwrpLightFx : Component
{
	/// <summary> flicker or pulse. </summary>
	[Property] public string Mode { get; set; } = "flicker";
	[Property] public float Range { get; set; } = 450f;

	PointLight _light;
	float _baseRadius;
	bool _searched;
	RealTimeUntil _nextBurst;
	RealTimeUntil _burstEnd;
	RealTimeUntil _nextToggle;
	float _phase;
	bool _dipped;

	protected override void OnUpdate()
	{
		if ( !_searched )
		{
			_searched = true;
			_light = Scene.GetAllComponents<PointLight>().Where( l => l.GameObject.Parent?.Name == "Lights" )
				.OrderBy( l => l.WorldPosition.DistanceSquared( WorldPosition ) ).FirstOrDefault();
			if ( _light.IsValid() && _light.WorldPosition.Distance( WorldPosition ) > Range ) _light = null;
			if ( _light.IsValid() ) _baseRadius = _light.Radius;
			_nextBurst = Game.Random.Float( 1f, 6f );
			_phase = Game.Random.Float( 0f, 6f );
		}
		if ( !_light.IsValid() ) return;

		if ( !SwrpLivingShip.Enabled || LightZones.Simple || !SwrpLivingShip.Near( Scene, WorldPosition ) )      // simple lighting: steady lamps
		{
			Restore();
			return;
		}

		if ( Mode == "pulse" )
		{
			_light.Enabled = true;
			_light.Radius = _baseRadius * ( 0.78f + 0.22f * MathF.Sin( Time.Now * 1.6f + _phase ) );
			return;
		}

		// a failing tube: steady for a while, then a stutter of dropouts
		if ( _burstEnd > 0f )
		{
			// a dip, never a blackout: with the darker ship a lamp switching right off blacked out the whole corridor as you ran
			// past (Angelo, 2026-10-12: "light flickering")
			if ( _nextToggle <= 0f )
			{
				_dipped = !_dipped;
				_nextToggle = _dipped ? Game.Random.Float( 0.04f, 0.1f ) : Game.Random.Float( 0.08f, 0.25f );
			}
			_light.Enabled = true;
			_light.Radius = _baseRadius * ( _dipped ? 0.8f : 1f );
		}
		else
		{
			Restore();
			if ( _nextBurst <= 0f )
			{
				_burstEnd = Game.Random.Float( 0.25f, 0.7f );
				_nextBurst = Game.Random.Float( 12f, 30f );
			}
		}
	}

	public string Describe() => $"{Mode} light {( _light.IsValid() ? $"{SwrpLivingShip.At( _light.WorldPosition )}, {_light.WorldPosition.Distance( WorldPosition ):0} away" : "none found" )}";

	void Restore()
	{
		if ( !_light.IsValid() ) return;
		_light.Enabled = true;
		_light.Radius = _baseRadius;
	}

	protected override void OnDisabled() => Restore();
}

/// <summary> A pipe venting steam from a wall: a gentle trickle, and every so often a hissing burst. </summary>
[Title( "SWRP Steam Vent" ), Category( "SWRP" ), Icon( "cloud" )]
public sealed class SwrpSteamVent : Component
{
	/// <summary> Which way to look for the wall the vent sits on. </summary>
	[Property] public Vector3 Probe { get; set; } = Vector3.Forward;

	const int MaxPuffs = 22;
	const float SpriteScale = 14f;      // a sprite's Size is not quite world units
	static Sprite _sprite;

	sealed class Puff
	{
		public GameObject Go;
		public SpriteRenderer R;
		public Vector3 Vel;
		public float Age, Life, Size0, Size1, Alpha;
	}

	readonly List<Puff> _puffs = new();
	Vector3 _origin, _jet;
	bool _placed;
	float _emit;
	RealTimeUntil _nextBurst;
	RealTimeUntil _burstEnd;

	protected override void OnUpdate()
	{
		if ( !_placed )
		{
			_placed = true;
			if ( SwrpLivingShip.FindWall( Scene, WorldPosition, Probe, out var p, out var n ) )
			{
				_origin = p + n * 3f;
				_jet = ( n * 0.7f + Vector3.Up * 0.7f ).Normal;
			}
			else
			{
				_origin = WorldPosition;
				_jet = Vector3.Up;
			}
			_nextBurst = Game.Random.Float( 2f, 8f );
		}

		var dt = Time.Delta;
		var on = SwrpLivingShip.Enabled && SwrpLivingShip.Near( Scene, _origin, 2600f );
		if ( on )
		{
			var burst = _burstEnd > 0f;
			if ( !burst && _nextBurst <= 0f )
			{
				_burstEnd = Game.Random.Float( 0.9f, 1.6f );
				_nextBurst = Game.Random.Float( 7f, 16f );
				var h = Sound.Play( "sounds/ambient/steam.sound", _origin );
				if ( h is not null ) h.Volume *= Math.Clamp( SwrpAmbience.Volume / 0.5f, 0f, 1f );
				burst = true;
			}
			_emit += dt * ( burst ? 16f : 3f );
			while ( _emit >= 1f )
			{
				_emit -= 1f;
				Spawn( burst );
			}
		}

		var cam = Scene.Camera;
		for ( var i = _puffs.Count - 1; i >= 0; i-- )
		{
			var pf = _puffs[i];
			pf.Age += dt;
			if ( pf.Age >= pf.Life || !pf.Go.IsValid() )
			{
				pf.Go?.Destroy();
				_puffs.RemoveAt( i );
				continue;
			}
			var k = pf.Age / pf.Life;
			pf.Vel *= MathF.Max( 0f, 1f - dt * 1.8f );
			pf.Vel += Vector3.Up * 26f * dt;
			pf.Go.WorldPosition += pf.Vel * dt;
			var size = MathX.Lerp( pf.Size0, pf.Size1, MathF.Sqrt( k ) ) * SpriteScale;
			pf.R.Size = new Vector2( size, size );
			pf.R.Color = new Color( 0.82f, 0.85f, 0.88f, pf.Alpha * MathF.Min( 1f, k * 6f ) * ( 1f - k ) );
		}
	}

	public string Describe() => $"steam vent {SwrpLivingShip.At( _origin )} jet {SwrpLivingShip.At( _jet * 100f )}, {_puffs.Count} puffs";

	void Spawn( bool burst )
	{
		if ( _puffs.Count >= MaxPuffs ) return;
		_sprite ??= Sprite.FromTexture( Texture.Load( "materials/fx/steam.png" ) );
		if ( _sprite is null ) return;
		var go = SwrpLivingShip.Child( GameObject, "steam" );
		go.WorldPosition = _origin;
		var r = go.Components.Create<SpriteRenderer>();
		r.Sprite = _sprite;
		r.Lighting = false;
		r.Shadows = false;
		r.Size = new Vector2( 4f, 4f );
		r.Color = Color.White.WithAlpha( 0f );
		var spread = ( _jet + Vector3.Random * 0.25f ).Normal;
		_puffs.Add( new Puff
		{
			Go = go, R = r,
			Vel = spread * ( burst ? Game.Random.Float( 220f, 320f ) : Game.Random.Float( 60f, 110f ) ),
			Life = burst ? Game.Random.Float( 1.4f, 2.2f ) : Game.Random.Float( 1.6f, 2.6f ),
			Size0 = burst ? 6f : 3f, Size1 = burst ? Game.Random.Float( 30f, 42f ) : Game.Random.Float( 16f, 24f ),
			Alpha = burst ? 0.42f : 0.24f,
		} );
	}

	protected override void OnDestroy()
	{
		foreach ( var p in _puffs ) p.Go?.Destroy();
		_puffs.Clear();
	}
}

/// <summary> A damaged wall panel that spits sparks now and then, with a crackle and a flash. </summary>
[Title( "SWRP Spark Panel" ), Category( "SWRP" ), Icon( "electric_bolt" )]
public sealed class SwrpSparkPanel : Component
{
	[Property] public Vector3 Probe { get; set; } = Vector3.Forward;

	Vector3 _point, _normal;
	bool _placed;
	RealTimeUntil _next;
	int _repeat;

	public void SparkNow() => _next = 0f;

	public string Describe() => $"sparking panel {SwrpLivingShip.At( _point )} facing {SwrpLivingShip.At( _normal * 100f )}";

	protected override void OnUpdate()
	{
		if ( !_placed )
		{
			_placed = true;
			if ( !SwrpLivingShip.FindWall( Scene, WorldPosition, Probe, out _point, out _normal ) )
			{
				_point = WorldPosition;
				_normal = -Probe.Normal;
			}
			_next = Game.Random.Float( 1f, 6f );
			// the broken panel itself: a dark access plate hanging a little open off the wall
			var plate = SwrpLivingShip.Child( GameObject, "broken_panel" );
			plate.WorldPosition = _point + _normal * 1.5f;
			plate.WorldRotation = Rotation.LookAt( _normal, Vector3.Up ) * Rotation.FromRoll( 7f ) * Rotation.FromYaw( 12f );
			var box = Model.Load( "models/dev/box.vmdl" );
			plate.WorldScale = new Vector3( 1.2f, 18f, 13f ) / box.Bounds.Size;
			var r = plate.Components.Create<ModelRenderer>();
			r.Model = box;
			r.MaterialOverride = Material.Load( "materials/crew/droid_dark.vmat" );
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
		if ( !SwrpLivingShip.Enabled || _next > 0f || !SwrpLivingShip.Near( Scene, _point, 2400f ) ) return;

		// a burst, sometimes a quick second one
		if ( _repeat > 0 ) { _repeat--; _next = Game.Random.Float( 0.15f, 0.4f ); }
		else { _repeat = Game.Random.Float() < 0.35f ? 1 : 0; _next = Game.Random.Float( 3f, 10f ); }

		var color = new Color( 1f, 0.7f, 0.3f );
		for ( var i = 0; i < Game.Random.Int( 12, 22 ); i++ )
		{
			var dir = ( _normal + Vector3.Random * 0.8f + Vector3.Down * 0.3f ).Normal;
			SparkFx.Spawn( Scene, _point + _normal * 3f, dir * Game.Random.Float( 120f, 320f ), color );
		}
		var flash = SwrpLivingShip.Child( GameObject, "spark_flash" );
		flash.WorldPosition = _point + _normal * 8f;
		var light = flash.Components.Create<PointLight>();
		light.LightColor = new Color( 1f, 0.75f, 0.4f ) * 8f;
		light.Radius = 260f;
		light.Shadows = false;
		flash.Components.Create<FxLife>().Life = 0.12f;
		if ( SwrpAmbience.IsSilentAt( Scene, _point ) ) return;      // (the brig stays quiet)
		var h = Sound.Play( "sounds/ambient/spark.sound", _point );
		if ( h is not null ) h.Volume *= Math.Clamp( SwrpAmbience.Volume / 0.5f, 0f, 1f );
	}
}

/// <summary>
/// An animated wall screen (<see cref="WallScreenPanel"/>) mounted on the wall its probe ray finds: it looks along the wall
/// for a flat stretch big enough for the whole screen (no doors, consoles or edges under it) at the standard height,
/// sets a dark backing plate flush on the wall and the screen just in front of it.
/// </summary>
[Title( "SWRP Wall Screen" ), Category( "SWRP" ), Icon( "monitor" )]
public sealed class SwrpWallScreen : Component
{
	[Property] public Vector3 Probe { get; set; } = Vector3.Forward;
	/// <summary> status, radar or graph. </summary>
	[Property] public string Mode { get; set; } = "status";
	/// <summary> Height of the screen's centre above the floor. </summary>
	[Property] public float Height { get; set; } = 72f;

	const float ScreenScale = 1.2f;
	// a world panel draws about 0.05 units a pixel: 760 x 440 at this scale is about 45.6 x 26.4 units
	const float HalfWidth = 22.8f, HalfHeight = 13.2f, Bezel = 1.6f;

	GameObject _mount;
	bool _placed;
	string _why;

	public string Describe() => _mount.IsValid()
		? $"{Mode} screen {SwrpLivingShip.At( _mount.WorldPosition )} yaw {_mount.WorldRotation.Yaw():0} {( _mount.Enabled ? "on" : "off" )}"
		: $"{Mode} screen at {SwrpLivingShip.At( WorldPosition )}: {_why ?? "not placed yet"}";

	protected override void OnUpdate()
	{
		if ( !_placed )
		{
			_placed = true;
			Place();
		}
		if ( _mount.IsValid() )
			_mount.Enabled = SwrpLivingShip.Enabled && SwrpLivingShip.Near( Scene, _mount.WorldPosition, 1800f );
	}

	void Place()
	{
		// the nearest flat, clear spot: slide along the wall and nudge the height until the whole screen fits
		if ( SwrpLivingShip.FindMount( Scene, WorldPosition, Probe, HalfWidth + Bezel, HalfHeight + Bezel, Height, 30f, out var surface, out var n ) )
			Mount( surface, n, Vector3.Cross( Vector3.Up, n ).Normal );
		else _why = "no flat stretch of wall near it";
	}

	void Mount( Vector3 surface, Vector3 n, Vector3 along )
	{
		_mount = SwrpLivingShip.Child( GameObject, "screen_mount" );
		_mount.WorldPosition = surface;
		_mount.WorldRotation = Rotation.LookAt( n, Vector3.Up );

		// the dark housing, flush on the wall
		var plate = SwrpLivingShip.Child( _mount, "housing" );
		var box = Model.Load( "models/dev/box.vmdl" );
		plate.LocalPosition = Vector3.Forward * 0.75f;
		plate.WorldScale = new Vector3( 1.5f, ( HalfWidth + Bezel ) * 2f, ( HalfHeight + Bezel ) * 2f ) / box.Bounds.Size;
		var r = plate.Components.Create<ModelRenderer>();
		r.Model = box;
		r.MaterialOverride = Material.Load( "materials/crew/droid_dark.vmat" );
		r.RenderType = ModelRenderer.ShadowRenderType.Off;

		// the screen just proud of the housing
		var panel = SwrpLivingShip.Child( _mount, "screen" );
		panel.LocalPosition = Vector3.Forward * 1.7f;
		panel.WorldScale = ScreenScale;
		var wp = panel.Components.Create<WorldPanel>();
		wp.PanelSize = new Vector2( 760, 440 );
		wp.LookAtCamera = false;
		wp.RenderOptions.Game = true;
		var screen = panel.Components.Create<WallScreenPanel>();
		screen.Mode = Mode;
		screen.Seed = (int)( MathF.Abs( surface.x * 7f + surface.y * 13f ) % 997 );
	}
}
