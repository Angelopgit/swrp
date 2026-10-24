using System;
using System.Threading.Tasks;

namespace Swrp.Tatooine;

/// <summary>
/// Tatooine's surface: an original desert (no map port) built on every machine from code, far below the Venator at
/// <see cref="Origin"/>. An s&amp;box <see cref="Terrain"/> whose heightmap is generated here (wind-cut dunes, sandstone mesas,
/// a ring of mountains hiding the edge, a flat landing field at the middle), painted with the procedural sand, rock and
/// hardpan from MapSource/tatooine_textures.py. Twin suns (swrp_tatooine_sky.shader) with <see cref="TatooineClock"/>'s day
/// and night, desert haze and <see cref="TatooineWeather"/>'s wind while this machine's camera is down there. Local and deterministic, never networked.
/// It loads while the ship is at (or jumping to) Tatooine or anyone is still on the surface; <see cref="TatooineShuttle"/>
/// carries troopers down and back.
/// </summary>
public static class TatooinePlanet
{
	/// <summary> The landing field's centre, in the scene: far below the Venator, outside its flyable space. </summary>
	public static readonly Vector3 Origin = new( 0f, 0f, -200000f );

	/// <summary> Anything below this height is on the planet (the Venator's space ends at z -16000). </summary>
	public const float PlanetCeiling = -100000f;

	public const string SkyMaterial = "materials/tatooine/sky_tatooine.vmat";
	static readonly string[] Materials = { "materials/tatooine/sand.tmat", "materials/tatooine/rock.tmat", "materials/tatooine/hardpan.tmat", "materials/tatooine/sand_red.tmat" };


	[ConVar( "swrp_tatooine", Help = "Tatooine's surface: -1 = load while the ship is at Tatooine or anyone is down there (default), 1 = always, 0 = never (your machine only)" )]
	public static int Mode { get; set; } = -1;

	[ConVar( "swrp_tatooine_sunlight", Help = "Tatooine's sunlight strength (default 4)", Saved = true )]
	public static float SunScale { get; set; } = 4f;

	[ConVar( "swrp_tatooine_haze", Help = "Tatooine's distance haze strength (0 = off, default 1)", Saved = true )]
	public static float HazeAmount { get; set; } = 1f;

	public static bool IsOnPlanet( Vector3 p ) => p.z < PlanetCeiling;

	/// <summary> True while this machine's camera is down on the planet. </summary>
	public static bool CameraOnPlanet { get; private set; }

	/// <summary> The shuttle runs down only while the Venator is over Tatooine. </summary>
	public static bool Open => SwrpSpace.Current is { InHyperspace: false } && SwrpSpace.Here.Id == "tatooine" && !SwrpSpace.Jumping;

	public static bool Loaded => _root.IsValid() && _terrain.IsValid();

	static GameObject _root;
	static Terrain _terrain;
	static GameObject _sun;
	static GradientFog _fog;
	/// <summary> 1 in full day, 0 at night (for the weather's sand colour). </summary>
	public static float Daylight { get; private set; } = 1f;

	/// <summary> The light falling on open ground now (see UpdateDayNight); white until the planet has a sun. </summary>
	public static Color GroundLight { get; private set; } = Color.White;
	static bool _building;
	static float _sinceCheck = 10f;
	static float _shipZFar;
	const float PlanetZFar = 220000f;

	static bool Wanted( Scene scene )
	{
		if ( Mode == 0 ) return false;
		if ( Mode == 1 ) return true;
		if ( SwrpSpace.Here.Id == "tatooine" || SwrpSpace.JumpDestination?.Id == "tatooine" ) return true;
		foreach ( var p in SwrpPlayers.Valid )
			if ( IsOnPlanet( p.WorldPosition ) ) return true;
		return false;
	}

	internal static void Tick( Scene scene )
	{
		if ( scene is null || !Game.IsPlaying || scene != Game.ActiveScene ) return;
		if ( _root.IsValid() && _root.Scene != scene ) { _root = null; _terrain = null; }
		_sinceCheck += Time.Delta;
		if ( _sinceCheck > 1f )
		{
			_sinceCheck = 0f;
			var want = Wanted( scene );
			if ( want && !_root.IsValid() && !_building ) _ = Build( scene );
			else if ( !want && _root.IsValid() ) Unload();
			if ( want ) TatooineClock.EnsureOnHost( scene );
		}

		var cam = scene.Camera;
		CameraOnPlanet = Loaded && cam.IsValid() && IsOnPlanet( cam.WorldPosition );
		if ( cam.IsValid() )
		{
			if ( CameraOnPlanet && cam.ZFar < PlanetZFar ) { _shipZFar = cam.ZFar; cam.ZFar = PlanetZFar; }
			else if ( !CameraOnPlanet && _shipZFar > 0f ) { cam.ZFar = _shipZFar; _shipZFar = 0f; }
		}
		if ( Loaded )
		{
			_sun.Enabled = CameraOnPlanet;
			if ( _site.IsValid() ) _site.Enabled = TatooineClock.Current?.SiteActive ?? false;
			_fog.Enabled = CameraOnPlanet && HazeAmount > 0f;
			if ( CameraOnPlanet ) UpdateDayNight();
		}
		// the ship's ambient fill lights the planet too: dim it to moonlight at night
		LightZones.OutsideScale = CameraOnPlanet ? MathX.Lerp( 0.22f, 0.55f, Daylight ) : 1f;
		TatooineWeather.Tick( scene, CameraOnPlanet, Daylight );
	}

	// ----- the ground ----------------------------------------------------------------------------------------------------

	const string TerrainFile = "data/tatooine/terrain.bin";
	const string TerrainInfo = "data/tatooine/terrain.json";
	const string PropsFile = "data/tatooine/props.json";

	sealed record TerrainHeader( int Resolution, float Size, float Range, float Base );
	public sealed record Prop( string Model, float X, float Y, float Z, float Yaw, float Scale, string Group );

	/// <summary> The event site's pieces (hidden until a Game Master opens it, see <see cref="TatooineClock.SiteActive"/>). </summary>
	static GameObject _site;

	/// <summary> The event site's garrison spots: NPC id, where, facing (from props.json's "spawn" entries). </summary>
	public static readonly List<(string Npc, Vector3 At, float Yaw)> Garrison = new();

	/// <summary> Everything here is authored offline by MapSource/tatooine_terrain.py: the eroded heightmap, the material
	/// mix and every rock, building and cover piece with its ground height. </summary>
	static async Task Build( Scene scene )
	{
		_building = true;
		try
		{
			var started = RealTime.Now;
			if ( !SwrpDataFile.Exists( TerrainFile ) ) { Log.Warning( $"[SWRP] Tatooine: {TerrainFile} missing (run MapSource/tatooine_terrain.py)" ); Mode = 0; return; }
			var info = Json.Deserialize<TerrainHeader>( SwrpDataFile.ReadText( TerrainInfo ) );
			var props = Json.Deserialize<List<Prop>>( SwrpDataFile.ReadText( PropsFile ) ) ?? new();
			var bytes = SwrpDataFile.ReadBytes( TerrainFile );
			var n = info.Resolution;
			ushort[] heights = null;
			uint[] control = null;
			await GameTask.RunInThreadAsync( () =>
			{
				heights = new ushort[n * n];
				control = new uint[n * n];
				Buffer.BlockCopy( bytes, 0, heights, 0, n * n * 2 );
				for ( var k = 0; k < n * n; k++ )
				{
					// the file stores base | overlay << 8 | blend << 16; the engine has its own packing
					var c = BitConverter.ToUInt32( bytes, n * n * 2 + k * 4 );
					control[k] = new CompactTerrainMaterial( (byte)c, (byte)( c >> 8 ), (byte)( c >> 16 ), false ).Packed;
				}
			} );
			if ( scene != Game.ActiveScene || !Game.IsPlaying ) return;

			var storage = new TerrainStorage();
			storage.SetResolution( n );
			storage.TerrainSize = info.Size;
			storage.TerrainHeight = info.Range;
			storage.HeightMap = heights;
			_heights = heights; _hn = n; _hSize = info.Size; _hRange = info.Range; _hBase = info.Base;
			storage.ControlMap = control;
			storage.Materials = Materials.Select( m => ResourceLibrary.Get<TerrainMaterial>( m ) ).Where( m => m is not null ).ToList();
			if ( storage.Materials.Count < Materials.Length ) Log.Warning( "[SWRP] Tatooine: terrain materials missing (run MapSource/tatooine_textures.py)" );

			_root = scene.CreateObject();
			_root.Name = "Tatooine";
			_root.NetworkMode = NetworkMode.Never;
			_root.Flags |= GameObjectFlags.NotSaved;
			_root.WorldPosition = Origin;

			// the terrain grows from its object's corner: put the landing field's middle at Origin
			var ground = new GameObject( _root, false, "Tatooine ground" );
			ground.NetworkMode = NetworkMode.Never;
			ground.WorldPosition = Origin - new Vector3( info.Size * 0.5f, info.Size * 0.5f, info.Base );
			_terrain = ground.Components.Create<Terrain>();
			_terrain.Storage = storage;
			_terrain.EnableCollision = true;
			ground.Enabled = true;

			PlaceProps( props );
			BuildLighting();
			Log.Info( $"[SWRP] Tatooine: surface built in {( RealTime.Now - started ) * 1000f:0} ms ({props.Count} props)" );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Tatooine: build failed: {e.Message}" );
			Mode = 0;
		}
		finally { _building = false; }
	}

	/// <summary> Rocks, the town and the event site, each with static collision (same file on every machine). </summary>
	static void PlaceProps( List<Prop> props )
	{
		_site = new GameObject( _root, false, "Tatooine event site" );
		_site.NetworkMode = NetworkMode.Never;
		var groups = new Dictionary<string, GameObject> { ["site"] = _site };
		Garrison.Clear();
		foreach ( var p in props )
		{
			if ( p.Group == "spawn" ) { Garrison.Add( (p.Model, Origin + new Vector3( p.X, p.Y, p.Z + 8f ), p.Yaw) ); continue; }
			if ( !groups.TryGetValue( p.Group, out var parent ) )
			{
				parent = new GameObject( _root, true, $"Tatooine {p.Group}" );
				parent.NetworkMode = NetworkMode.Never;
				groups[p.Group] = parent;
			}
			var m = Model.Load( p.Model );
			if ( m is null || m.IsError ) continue;
			var go = new GameObject( parent, true, p.Group );
			go.NetworkMode = NetworkMode.Never;
			go.WorldPosition = Origin + new Vector3( p.X, p.Y, p.Z );
			go.WorldRotation = Rotation.FromYaw( p.Yaw );
			go.WorldScale = p.Scale;
			go.Components.Create<ModelRenderer>().Model = m;
			var c = go.Components.Create<ModelCollider>();
			c.Model = m;
			c.Static = true;
		}
	}

	static ushort[] _heights;
	static int _hn;
	static float _hSize, _hRange, _hBase;

	/// <summary> The bare terrain's height under a point (bilinear on the heightmap: cheap, no trace; ignores rocks and buildings). </summary>
	public static float TerrainHeight( Vector3 p )
	{
		if ( _heights is null ) return Origin.z;
		var fx = Math.Clamp( ( p.x - Origin.x ) / _hSize + 0.5f, 0f, 1f ) * ( _hn - 1 );
		var fy = Math.Clamp( ( p.y - Origin.y ) / _hSize + 0.5f, 0f, 1f ) * ( _hn - 1 );
		var i = Math.Min( (int)fx, _hn - 2 );
		var j = Math.Min( (int)fy, _hn - 2 );
		var tx = fx - i; var ty = fy - j;
		float H( int a, int b ) => _heights[b * _hn + a];
		var h = MathX.Lerp( MathX.Lerp( H( i, j ), H( i + 1, j ), tx ), MathX.Lerp( H( i, j + 1 ), H( i + 1, j + 1 ), tx ), ty );
		return Origin.z - _hBase + h / 65535f * _hRange;
	}

	/// <summary> The ground under a point on the planet (the terrain or a rock), or the landing field's height. </summary>
	public static Vector3 GroundAt( Vector3 p )
	{
		if ( !_root.IsValid() ) return p.WithZ( Origin.z );
		var tr = _root.Scene.Trace.Ray( p.WithZ( Origin.z + 20000f ), p.WithZ( Origin.z - 6000f ) )
			.WithoutTags( "player", "npc", "trigger", "debris", "ragdoll", "worlditem" ).Run();
		return tr.Hit ? tr.HitPosition : p.WithZ( Origin.z );
	}

	/// <summary> The terrain's material colour under a point (for dust kicked up there): the dominant layer's average albedo. </summary>
	public static Color GroundColour( Vector3 p )
	{
		var info = _terrain.IsValid() ? _terrain.GetMaterialAtWorldPosition( p ) : null;
		if ( info is not { } i ) return SandAlbedo[0];
		var a = SandAlbedo[Math.Clamp( i.BaseTextureId, 0, 3 )];
		var b = SandAlbedo[Math.Clamp( i.OverlayTextureId, 0, 3 )];
		return Color.Lerp( a, b, i.BlendFactor );
	}

	// sand, rock, hardpan, ochre sand: the textures' mean colours (MapSource/tatooine_textures.py)
	static readonly Color[] SandAlbedo = { new( 0.80f, 0.66f, 0.47f ), new( 0.63f, 0.49f, 0.37f ), new( 0.77f, 0.67f, 0.54f ), new( 0.76f, 0.58f, 0.40f ) };

	static float Smooth( float a, float b, float x )
	{
		var t = Math.Clamp( ( x - a ) / ( b - a ), 0f, 1f );
		return t * t * ( 3f - 2f * t );
	}

	// ----- suns, haze, wind ----------------------------------------------------------------------------------------------

	static readonly Vector3 NoonSun = new Vector3( -0.43f, 0.43f, 0.79f ).Normal;

	static void BuildLighting()
	{
		_sun = new GameObject( _root, false, "Tatooine suns" );
		_sun.NetworkMode = NetworkMode.Never;
		var light = _sun.Components.Create<DirectionalLight>();
		light.Shadows = true;
		light.ContactShadows = true;

		_fog = _root.Components.Create<GradientFog>();
		_fog.Enabled = false;
	}

	/// <summary> Towards the sun at an hour: noon high in the south-west, rising ~06:00 and setting ~18:00. </summary>
	public static Vector3 SunDirection( float hour )
	{
		var flat = NoonSun.WithZ( 0f ).Normal;
		var pole = ( -flat * MathF.Cos( 38f.DegreeToRadian() ) + Vector3.Up * MathF.Sin( 38f.DegreeToRadian() ) ).Normal;
		return Rotation.FromAxis( pole, -( hour - 12f ) * 15f ) * NoonSun;
	}

	static void UpdateDayNight()
	{
		var sun = SunDirection( TatooineClock.Hour );
		var elev = MathF.Asin( Math.Clamp( sun.z, -1f, 1f ) ).RadianToDegree();
		var day = Smooth( -6f, 10f, elev );
		Daylight = day;
		var golden = 1f - Smooth( 2f, 22f, elev );
		var night = 1f - Smooth( -10f, -3f, elev );
		var sunColor = Color.Lerp( new Color( 1.0f, 0.93f, 0.82f ), new Color( 1.0f, 0.55f, 0.26f ), golden );
		var intensity = SunScale * Smooth( -2f, 6f, elev );

		Vector3 dir;
		Color color;
		if ( night > 0f && intensity < 0.05f )
		{
			// the night light is the moonlight: dim blue from the far side (the sky shader draws it as a moon, not two suns)
			dir = ( Rotation.FromAxis( Vector3.Up, 160f ) * -sun ).WithZ( 0f ).Normal.WithZ( 0.6f ).Normal;
			color = new Color( 0.45f, 0.58f, 1.0f ) * 0.3f * night;
		}
		else
		{
			dir = sun;
			color = sunColor * intensity;
		}
		_sun.WorldRotation = Rotation.LookAt( -dir );
		var light = _sun.Components.Get<DirectionalLight>( FindMode.EverythingInSelf );
		light.LightColor = color;
		// a hard desert sun: the blue sky is the only fill, so shadows stay crisp and cool
		light.SkyColor = Color.Lerp( new Color( 0.05f, 0.07f, 0.12f ), Color.Lerp( new Color( 0.52f, 0.62f, 0.80f ), new Color( 0.7f, 0.5f, 0.42f ), golden ), day ) * 0.42f;
		// roughly what lights the open ground (sun on flat sand + sky fill + the ship's ambient): unlit effects like the
		// footstep dust multiply their albedo by it, so they come out as bright and warm as the sand they rise from
		var amb = new Color( 0.5f, 0.52f, 0.58f ) * LightZones.OutsideScale;
		GroundLight = new Color( color.r * MathF.Max( 0f, dir.z ) + light.SkyColor.r + amb.r, color.g * MathF.Max( 0f, dir.z ) + light.SkyColor.g + amb.g, color.b * MathF.Max( 0f, dir.z ) + light.SkyColor.b + amb.b );

		// aerial perspective: distance fades to a pale, slightly blue-cream, not brown (warm only when the sun is low)
		var haze = Color.Lerp( new Color( 0.06f, 0.08f, 0.13f ), Color.Lerp( new Color( 0.80f, 0.80f, 0.79f ), new Color( 0.95f, 0.62f, 0.40f ), golden ), day );
		// a gust thickens the air and pulls the haze in close
		var gust = TatooineWeather.Gust;
		_fog.Color = haze.WithAlpha( Math.Clamp( MathX.Lerp( 0.35f, 0.42f, day ) * ( 1f + gust * 0.8f ) * Math.Clamp( HazeAmount, 0f, 2f ), 0f, 0.95f ) );
		_fog.StartDistance = MathX.Lerp( 6000f, 800f, gust );
		_fog.EndDistance = MathX.Lerp( 90000f, 24000f, gust );
		_fog.FalloffExponent = 1.3f;
		_fog.Height = Origin.z + 12000f;
		_fog.VerticalFalloffExponent = 1.2f;
	}

	static void Unload()
	{
		if ( _root.IsValid() ) _root.Destroy();
		_root = null;
		_terrain = null;
		_sun = null;
		_fog = null;
		CameraOnPlanet = false;
		TatooineWeather.Clear();
		Log.Info( "[SWRP] Tatooine: surface unloaded" );
	}

	[ConCmd( "swrp_tatooine_info", Help = "swrp_tatooine_info - print the Tatooine surface's state" )]
	public static void InfoCommand()
	{
		Log.Info( $"[SWRP] Tatooine: loaded {Loaded}, mode {Mode}, open {Open}, camera on planet {CameraOnPlanet}, hour {TatooineClock.FormatHour( TatooineClock.Hour )}, ground at origin {GroundAt( Origin ).z - Origin.z:0.0}" );
	}
}

/// <summary> Ticks <see cref="TatooinePlanet"/> and keeps the shuttles in place. </summary>
public sealed class TatooineSystem : GameObjectSystem<TatooineSystem>
{
	public TatooineSystem( Scene scene ) : base( scene )
	{
		// the desert sky while this machine's camera is on the planet (SwrpSpace keeps the space sky everywhere else)
		SwrpSpace.LocalSkyOverride = () => TatooinePlanet.CameraOnPlanet ? TatooinePlanet.SkyMaterial : null;
		Listen( Stage.StartUpdate, 20, () =>
		{
			TatooinePlanet.Tick( Scene );
			TatooineShuttle.Tick( Scene );
		}, "SwrpTatooine" );
	}
}
