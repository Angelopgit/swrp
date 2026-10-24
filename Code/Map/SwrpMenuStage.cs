using System;

namespace Swrp;

/// <summary>
/// The character screen's backdrop: a hangar bay of its own (models/hangar/menu_hangar, the ship's panels round a window onto
/// space) far outside the ship where nobody walks, set up locally (nothing here is networked or seen by anyone else) while you
/// pick or enlist a trooper before deploying. Your selected trooper stands under a spotlight with
/// a DC-15A at the low ready, in its battalion markings; behind, a Republic heavy fighter sits on its gear beside a
/// maintenance gantry with a mechanic welding under the nose, and astromechs and a mouse droid roll about. Your trooper stands
/// on a holo plinth and is shot like a film still (2026-10-21, board "Trooper Select Shot"): a long lens from far back puts him
/// on the right third with the bay dim and out of focus behind; white light only (hard key from screen right, two rims, low
/// fill) with the battalion's colour as a thin kicker on the back edge. The screen opens with a dolly in, a new pick racks
/// focus off and back while the model swaps, and Deploy pushes in towards the visor before the fade. Gone once you deploy.
/// </summary>
[Title( "SWRP Menu Stage" ), Category( "SWRP" ), Icon( "theaters" )]
public sealed class SwrpMenuStage : Component
{
	/// <summary> Set by the character screen every frame: show the hangar (visible, and not yet deployed). </summary>
	public static bool Wanted { get; set; }

	/// <summary> The trooper on show: the roster pick, or the class and battalion being enlisted. </summary>
	public static string ClassId { get; set; }
	public static string FactionId { get; set; }
	/// <summary> A Jedi's looks while one is being made or shown. </summary>
	public static string Appearance { get; set; }

	/// <summary> Armour pieces and helmet off for the trooper on show (the selected character, or the customiser's preview). </summary>
	public static string Armor { get; set; }
	public static bool HelmetOff { get; set; }

	/// <summary> Dev: build it anyway and film it with a hidden camera (swrp_menu_stage), for checking it while playing. </summary>
	public static bool Preview { get; set; }

	// ----- layout: where the trooper stands and which way he faces (the camera looks back at him) -----
	public static Vector3 Spot { get; set; } = new( 0f, 0f, -9000f );
	public static float Facing { get; set; } = 0f;

	// how the set is turned (dev: swrp_menu_stage_yaw)
	public static float TrooperYaw { get; set; } = -24f;
	public static float FighterYaw { get; set; } = -30f;
	public static float LineYaw { get; set; } = 20f;

	/// <summary> The hero trooper's rifle, moved off the hand so it doesn't sink into the armour (dev: swrp_menu_stage_gun). </summary>
	public static Vector3 HeroGunOffset { get; set; } = new( 5f, 0f, 1.5f );

	// ----- the shot (dev: swrp_menu_cam, swrp_menu_light) -----
	/// <summary> Horizontal field of view, the camera's distance in front of the trooper, its height and the height it aims at. </summary>
	public static float CamFov { get; set; } = 21f;
	public static float CamDistance { get; set; } = 400f;
	public static float CamHeight { get; set; } = 60f;
	public static float AimHeight { get; set; } = 53f;
	/// <summary> How far to the trooper's right the camera aims, which puts him on the right third of the screen. </summary>
	public static float AimShift { get; set; } = 30f;
	public static float KeyLevel { get; set; } = 7f;
	public static float RimLevel { get; set; } = 6f;
	public static float FillLevel { get; set; } = 0.15f;
	/// <summary> The bay's own lamps, as a share of what they were before the shot was relit. </summary>
	public static float BayLevel { get; set; } = 0.5f;
	/// <summary> The scene's ambient fill while the stage is up (LightZones.StageAmbient): low, so the key and rims shape the armour. </summary>
	public static float StageFill { get; set; } = 0.4f;

	/// <summary> Read by SwrpPostFx: focus on the trooper (0 = no depth of field), how wide the sharp band is, and how soft the rest. </summary>
	public static float FocusDistance { get; private set; }
	public static float FocusRange { get; private set; } = 60f;
	public static float FocusBlur { get; private set; } = 8f;

	/// <summary> True while the stage is up (SwrpPostFx gives it its own grade). </summary>
	public static bool Showing { get; private set; }

	/// <summary> Seconds since Deploy was pressed on the stage, while its push-in plays (the menu fades its panels out). </summary>
	public static bool Deploying => _deploy is not null && _sinceDeploy < DeployPush + 1f;     // time-bound, so a lost stage can't leave the menu hidden
	static Action _deploy;
	static RealTimeSince _sinceDeploy;
	const float DeployPush = 0.75f;

	/// <summary>
	/// Deploy from the character screen: with the hangar up, push in towards the visor and dip to black first, then run
	/// <paramref name="go"/> (the real deploy request, whose own fade holds until the trooper is in the world). Without it, at once.
	/// </summary>
	public static void Deploy( Action go )
	{
		if ( Deploying ) return;     // a second click during the push-in
		if ( !Wanted ) { _deploy = null; go?.Invoke(); return; }
		_deploy = go;
		_sinceDeploy = 0;
		Feel.FeelTransition.Dip( Color.Black, DeployPush, 2f, Feel.FeelMotion.Reveal );
	}

	[ConCmd( "swrp_menu_cam", Help = "swrp_menu_cam <fov> <distance> <height> <aim height> <aim shift> - dev: frame the character screen's shot" )]
	public static void CamCommand( float fov, float distance, float height, float aim, float shift )
	{
		CamFov = fov;
		CamDistance = distance;
		CamHeight = height;
		AimHeight = aim;
		AimShift = shift;
	}

	[ConCmd( "swrp_menu_light", Help = "swrp_menu_light <key> <rim> <fill> <bay> - dev: light the character screen's shot" )]
	public static void LightCommand( float key, float rim, float fill, float bay )
	{
		KeyLevel = key;
		RimLevel = rim;
		FillLevel = fill;
		BayLevel = bay;
		foreach ( var s in Game.ActiveScene?.GetAllComponents<SwrpMenuStage>() ?? Enumerable.Empty<SwrpMenuStage>() ) s.Teardown();
	}

	[ConCmd( "swrp_menu_stage_gun", Help = "swrp_menu_stage_gun <forward> <left> <up> - dev: nudge the menu trooper's rifle off his hand" )]
	public static void GunCommand( float forward, float left, float up )
	{
		HeroGunOffset = new Vector3( forward, left, up );
		foreach ( var g in Game.ActiveScene?.GetAllComponents<SwrpMannequinGun>() ?? Enumerable.Empty<SwrpMannequinGun>() )
			if ( g.Offset != Vector3.Zero || g.GameObject.Parent?.Name == "Your Trooper" ) g.Offset = HeroGunOffset;
	}

	[ConCmd( "swrp_menu_stage_yaw", Help = "swrp_menu_stage_yaw <trooper> <fighter> <line> - dev: turn the menu backdrop's trooper, fighter and inspection line" )]
	public static void YawCommand( float trooper, float fighter, float line )
	{
		TrooperYaw = trooper;
		FighterYaw = fighter;
		LineYaw = line;
		foreach ( var s in Game.ActiveScene?.GetAllComponents<SwrpMenuStage>() ?? Enumerable.Empty<SwrpMenuStage>() ) s.Teardown();
	}

	GameObject _root;
	SkinnedModelRenderer _trooper;
	string _shownClass, _shownFaction, _shownLook;
	CameraComponent _previewCam;
	RealTimeSince _sinceModelCheck;
	TimeUntil _nextWeld;
	Vector3 _weldAt;
	PointLight _weldLight;
	SpotLight _key, _kicker;
	RealTimeSince _sinceBuilt;
	RealTimeSince _sinceSwitch = 100f;
	bool _swapPending;
	Color _kickerColor = Color.White;
	const float Intro = 2.4f;
	const float Switch = 0.45f;

	protected override void OnUpdate()
	{
		// the push-in keeps the stage up until its own fade has covered the screen, then sends the real deploy
		if ( _deploy is not null && _sinceDeploy > DeployPush )
		{
			var go = _deploy;
			_deploy = null;
			go();
		}
		var want = Wanted || Preview || Deploying;
		if ( want && !_root.IsValid() ) Build();
		if ( !want && _root.IsValid() ) Teardown();
		Showing = _root.IsValid();
		if ( !_root.IsValid() ) return;

		// a new pick racks focus off the trooper, swaps him at the softest point and racks back (Angelo, 2026-10-14: also
		// re-dressed once a second if the model on show isn't the class's current one, e.g. it was rebuilt or failed to load)
		var changed = _shownClass != ClassId || _shownFaction != FactionId || _shownLook != Appearance;
		if ( changed && !_swapPending && _sinceSwitch > Switch ) { _swapPending = true; _sinceSwitch = 0; }
		if ( _swapPending && _sinceSwitch > Switch * 0.45f ) { _swapPending = false; DressTrooper(); }
		else if ( !changed && _sinceModelCheck > 1f && _trooper.IsValid() )
		{
			_sinceModelCheck = 0;
			var current = ( ClassRegistry.Get( ClassId ) ?? ClassRegistry.Default ).LoadModel();
			if ( current is not null && current != _trooper.Model ) DressTrooper();
		}
		// the armour customiser's pieces on the trooper on show (Characters/SwrpArmor.cs)
		if ( _trooper.IsValid() )
		{
			// a Jedi on show just stands with the saber (JediStand would otherwise swing it every few seconds)
			if ( _trooper.Components.Get<Jedi.JediStand>( FindMode.EverythingInSelf ) is { } stand ) stand.Still = true;
			var layer = _trooper.Components.GetOrCreate<SwrpArmorLayer>();
			layer.Body = _trooper;
			var clone = ClassId != ClassRegistry.Jedi && FactionId != "jedi";
			layer.Parts = clone ? Armor ?? "" : "";
			layer.Faction = FactionId;
			layer.HelmetOff = clone && HelmetOff && SwrpArmorRegistry.CanTakeHelmetOff( ClassId );
			layer.Cape = clone && ClassId == ClassRegistry.Officer;
		}
		Weld();
		LightTrooper();
		PlaceCamera();
	}

	protected override void OnDestroy() => Teardown();

	// ----- building -----

	Vector3 Fwd => Rotation.FromYaw( Facing ).Forward;
	Vector3 Side => Rotation.FromYaw( Facing ).Right;

	/// <summary> A point on the stage: forward of the trooper, to his right, and up from the floor. </summary>
	Vector3 At( float forward, float right, float up = 0f )
	{
		var p = Spot + Fwd * forward + Side * right;
		return p.WithZ( SwrpLivingShip.FloorBelow( Scene, p.WithZ( Spot.z + 40f ) ) + up );
	}

	GameObject Child( string name, Vector3 pos, float yaw )
	{
		var go = new GameObject( _root, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos;
		go.WorldRotation = Rotation.FromYaw( yaw );
		return go;
	}

	void Build()
	{
		_root = new GameObject( true, "Menu Stage" );
		_root.NetworkMode = NetworkMode.Never;
		_root.Flags |= GameObjectFlags.NotSaved;
		_shownClass = _shownFaction = null;
		_sinceBuilt = 0;
		_kickerColor = Color.Black;
		if ( !Preview ) Feel.FeelTransition.Reveal( Color.Black, 1.2f );
		// the scene's flat ambient lit the armour as much as the key did: a dark zone round the bay lets the key and rims shape him
		LightZones.StageAmbient = StageFill;
		var scale = SwrpPlayer.PlayerScaleFor( Scene );

		// the bay itself, with a floor to stand on (and for the droids' navmesh)
		var bay = Child( "Bay", Spot, Facing );
		bay.Components.Create<ModelRenderer>().Model = Model.Load( "models/hangar/menu_hangar.vmdl" );
		var floor = bay.Components.Create<BoxCollider>();
		floor.Center = new Vector3( -340f, 0f, -12f );
		floor.Scale = new Vector3( 1920f, 1800f, 24f );
		floor.Static = true;
		// the bay's lights, held low so the trooper is the brightest thing in frame: white panels overhead, faint starlight
		foreach ( var x in new[] { -1000f, -400f, 200f } )
			foreach ( var y in new[] { -450f, 450f } )
				Lamp( "Ceiling", bay.WorldTransform.PointToWorld( new Vector3( x, y, 690f ) ), Color.White * 1.9f * BayLevel, 1200f );
		Lamp( "Starlight", bay.WorldTransform.PointToWorld( new Vector3( -1250f, 0f, 340f ) ), new Color( 0.45f, 0.62f, 1f ) * 2.2f * BayLevel, 1500f );
		foreach ( var (x, y) in new[] { ( -1150f, -760f ), ( -1150f, -650f ), ( -1040f, -760f ), ( -300f, 760f ), ( -190f, 760f ), ( -1180f, 720f ) } )
			Child( "Crate", bay.WorldTransform.PointToWorld( new Vector3( x, y, 0f ) ), Facing + Game.Random.Float( -8f, 8f ) )
				.Components.Create<ModelRenderer>().Model = Model.Load( "models/empirecratecubebig.vmdl" );

		// The camera looks back at the trooper from in front of him, so his right (Side) is the left of the screen and the
		// set sits behind him (negative forward). The menu's panels cover the outer quarters; the set fills the middle.

		// the fighter on its gear behind him to the right of the screen, nose turned towards the camera
		var fighter = Child( "Fighter", At( -620f, -175f ), Facing + FighterYaw );
		fighter.Components.Create<ModelRenderer>().Model = Model.Load( Flight.CraftModels.Arc170 );
		fighter.WorldScale = 0.62f;      // the ARC-170 is full size; the bay is small
		var gantry = Child( "Gantry", fighter.WorldTransform.PointToWorld( new Vector3( -60f, -230f, 0f ) ), Facing + FighterYaw );
		gantry.Components.Create<ModelRenderer>().Model = Model.Load( "models/hangar/hangar_gantry.vmdl" );
		var cart = Child( "Cart", fighter.WorldTransform.PointToWorld( new Vector3( 150f, 190f, 0f ) ), Facing + FighterYaw + 70f );
		cart.Components.Create<ModelRenderer>().Model = Model.Load( "models/hangar/hangar_cart.vmdl" );

		// a mechanic crouched under the nose, welding; another up on the gantry
		_weldAt = fighter.WorldTransform.PointToWorld( new Vector3( 190f, 0f, 46f ) );
		var mechAt = fighter.WorldTransform.PointToWorld( new Vector3( 235f, 30f, 0f ) );
		var mech = Mannequin( "Mechanic", mechAt.WithZ( At( 0, 0 ).z ), ( _weldAt - mechAt ).WithZ( 0 ).EulerAngles.yaw, scale );
		mech.Duck = 1f;
		// a tech at the foot of the gantry's ladder, looking up at the work
		var foot = gantry.WorldTransform.PointToWorld( new Vector3( 120f, -55f, 0f ) );
		Mannequin( "Gantry Tech", foot.WithZ( At( 0, 0 ).z ), Facing + FighterYaw + 150f, scale );
		var wl = Child( "Weld Light", _weldAt, 0 );
		_weldLight = wl.Components.Create<PointLight>();
		_weldLight.Radius = 220f;
		_weldLight.LightColor = Color.Black;
		_weldLight.Shadows = false;

		// (the row of troopers at inspection stood behind the roster panel and read as ghosts through it: gone)

		// droids about their business
		var r2 = Child( "Astromech", At( -260f, 220f ), 0 );
		var a = r2.Components.Create<SwrpCrew>();
		a.Kind = "astromech";
		a.Route = Route( At( -260f, 230f ), At( -330f, -60f ), At( -470f, -180f ) );
		var r3 = Child( "Astromech 2", fighter.WorldTransform.PointToWorld( new Vector3( 60f, 160f, 0f ) ), 0 );
		var a2 = r3.Components.Create<SwrpCrew>();
		a2.Kind = "astromech";
		a2.Yaw = Facing + FighterYaw - 90f;
		a2.Route = Route( r3.WorldPosition );
		var mouse = Child( "Mouse Droid", At( -150f, 330f ), 0 );
		var md = mouse.Components.Create<SwrpCrew>();
		md.Kind = "mouse";
		md.Route = Route( At( -170f, 380f ), At( -210f, -330f ) );

		// light, white only (project rule): a hard key high on the trooper's left (screen right, away from the menu), a strong
		// rim behind his right that cuts his silhouette off the dark menu side, a low fill from the lens, and a kicker behind
		// his left in the battalion's colour (the one accent, set each frame by LightTrooper). Shadows on the key and rim only.
		_key = SpotLamp( "Trooper Key", At( 210f, -150f, 150f ), At( 0f, 0f, 52f ), Color.Black, 700f, 30f );
		Lamp( "Trooper Fill", At( 260f, 30f, 55f ), Color.White * FillLevel, 520f );
		SpotLamp( "Trooper Rim", At( -150f, 120f, 150f ), At( 0f, 0f, 60f ), Color.White * RimLevel, 480f, 30f );
		_kicker = SpotLamp( "Trooper Kicker", At( -140f, -120f, 110f ), At( 0f, 0f, 50f ), Color.Black, 420f, 32f );
		_kicker.Shadows = false;
		Plinth( At( 0f, 0f ) );
		SpotLamp( "Fighter Spot", At( -450f, -100f, 420f ), At( -620f, -175f, 60f ), Color.White * 3f, 900f, 40f ).Shadows = false;

		// your trooper at a relaxed low ready, the rifle across the body in both hands
		var you = Mannequin( "Your Trooper", At( 0f, 0f ), Facing + TrooperYaw, scale, gun: true );
		you.Carry = true;
		_trooper = you.GetComponent<SkinnedModelRenderer>();
		DressTrooper();

		if ( Preview )
		{
			var cam = Child( "Menu Stage Preview Camera", At( 0, 0 ), 0 );
			_previewCam = cam.Components.Create<CameraComponent>();
			_previewCam.IsMainCamera = false;
			_previewCam.Priority = -100;
			_previewCam.Enabled = false;
			Log.Info( $"[SWRP] menu stage preview camera {_previewCam.Id}" );
		}
	}

	/// <summary> A low holo plinth under your trooper: a dark disc with a lit cyan ring, and a soft glow up from it. </summary>
	void Plinth( Vector3 at )
	{
		var go = Child( "Plinth", at + Vector3.Down * 3f, Facing );
		go.Components.Create<ModelRenderer>().Model = Model.Load( "models/props/holo_plinth.vmdl" );
		Lamp( "Plinth Glow", at + Vector3.Up * 10f, new Color( 0.4f, 0.75f, 1f ) * 0.8f, 130f );
	}

	static string Route( params Vector3[] points ) => string.Join( "; ", points.Select( p => $"{p.x:0} {p.y:0} {p.z + 20f:0}" ) );

	SwrpAnimMannequin Mannequin( string name, Vector3 pos, float yaw, float scale, bool gun = false )
	{
		var go = Child( name, pos, yaw );
		go.WorldScale = scale;
		var r = go.Components.Create<SkinnedModelRenderer>();
		r.Model = ClassRegistry.Default.LoadModel() ?? Model.Load( "models/citizen/citizen.vmdl" );
		var m = go.Components.Create<SwrpAnimMannequin>();
		m.HoldType = gun ? 2 : 0;
		if ( gun && ItemRegistry.Get( "dc15a" )?.Weapon is { ModelPath: { } path } weapon )
		{
			var g = new GameObject( go, true, "Gun" );
			var mesh = new GameObject( g, true, "Mesh" );
			mesh.Components.Create<ModelRenderer>().Model = Model.Load( path );
			weapon.PlaceMesh( mesh );
			var held = g.Components.Create<SwrpMannequinGun>();
			held.Body = r;
		}
		return m;
	}

	SpotLight SpotLamp( string name, Vector3 from, Vector3 target, Color color, float radius, float cone )
	{
		var go = Child( name, from, 0 );
		go.WorldRotation = Rotation.LookAt( target - from );
		var l = go.Components.Create<SpotLight>();
		l.LightColor = color;
		l.Radius = radius;
		l.ConeOuter = cone;
		l.ConeInner = cone * 0.6f;
		l.Shadows = true;
		return l;
	}

	void Lamp( string name, Vector3 at, Color color, float radius )
	{
		var l = Child( name, at, 0 ).Components.Create<PointLight>();
		l.LightColor = color;
		l.Radius = radius;
		l.Shadows = false;
	}

	/// <summary> The trooper on show in the chosen class's armour and the battalion's colours. </summary>
	void DressTrooper()
	{
		_shownClass = ClassId;
		_shownFaction = FactionId;
		_shownLook = Appearance;
		if ( !_trooper.IsValid() ) return;
		var cls = ClassRegistry.Get( ClassId ) ?? ClassRegistry.Default;
		_trooper.Model = cls.LoadModel() ?? _trooper.Model;
		_trooper.Tint = Color.White;      // the battalion is a material group (SwrpOutfit, through the mannequin)
		if ( _trooper.GetComponent<SwrpAnimMannequin>() is { } m ) { m.Faction = FactionId; m.Appearance = Appearance; }
	}

	/// <summary> The mechanic's torch: a burst of sparks and a flicker of blue-white light, on and off. </summary>
	void Weld()
	{
		if ( !_weldLight.IsValid() ) return;
		var on = _nextWeld.Relative < 0f && _nextWeld.Relative > -1.4f;
		_weldLight.LightColor = on ? new Color( 0.7f, 0.85f, 1f ) * Game.Random.Float( 2.5f, 6f ) : Color.Black;
		if ( on && Game.Random.Float() < Time.Delta * 22f )
			SparkFx.Spawn( Scene, _weldAt, ( Vector3.Random + Vector3.Down * 0.6f ).Normal * Game.Random.Float( 90f, 240f ), new Color( 1f, 0.8f, 0.45f ) );
		if ( _nextWeld.Relative < -1.4f ) _nextWeld = Game.Random.Float( 0.6f, 2.4f );
	}

	/// <summary> The key snaps on after the intro's dolly gets going and drops for the deploy push; the kicker takes the battalion's colour. </summary>
	void LightTrooper()
	{
		var intro = Feel.FeelMotion.OutCubic( ( _sinceBuilt - 0.7f ) / 0.5f );
		var push = _deploy is not null ? Feel.FeelMotion.InOutCubic( _sinceDeploy / DeployPush ) : 0f;
		if ( _key.IsValid() ) _key.LightColor = Color.White * KeyLevel * intro * ( 1f - 0.7f * push );
		var unit = FactionRegistry.Get( FactionId )?.Color ?? Color.White;
		_kickerColor = Color.Lerp( _kickerColor, unit, Math.Min( 1f, Time.Delta * 6f ) );
		if ( _kicker.IsValid() ) _kicker.LightColor = _kickerColor * RimLevel * 0.8f * intro;
	}

	/// <summary>
	/// The long-lens shot: far in front of the trooper, a little below his chest, aimed to his right so he sits on the right third.
	/// It dollies in from wide when the screen opens, drifts a little, and pushes towards the visor on Deploy.
	/// </summary>
	void PlaceCamera()
	{
		var t = Time.Now;
		var intro = Feel.FeelMotion.Smoother( _sinceBuilt / Intro );
		var push = _deploy is not null ? Feel.FeelMotion.InOutCubic( _sinceDeploy / DeployPush ) : 0f;
		var distance = MathX.Lerp( CamDistance * 1.8f, CamDistance, intro );
		var fov = MathX.Lerp( CamFov * 1.45f, CamFov, intro ) * ( 1f - 0.25f * push );
		var target = Vector3.Lerp( At( 0f, AimShift, AimHeight ), At( 0f, AimShift * 0.4f, 62f ), push );
		var pos = At( distance, AimShift + 12f, CamHeight ) + Side * ( MathF.Sin( t * 0.13f ) * 8f ) + Vector3.Up * ( MathF.Sin( t * 0.21f ) * 2f );
		pos = Vector3.Lerp( pos, target, 0.45f * push );
		var rot = Rotation.LookAt( target - pos );

		// focus on his chest; a new pick racks the sharp band forward off him and back
		var rack = _sinceSwitch < Switch ? Feel.FeelMotion.Pulse( _sinceSwitch / Switch ) : 0f;
		FocusDistance = pos.Distance( At( 0f, 0f, 50f ) ) * ( 1f - 0.55f * rack );
		FocusRange = 70f;
		FocusBlur = 7f + 7f * rack;

		var cam = _previewCam.IsValid() ? _previewCam : Scene.Camera;
		if ( cam is null ) return;
		cam.WorldPosition = pos;
		cam.WorldRotation = rot;
		cam.FieldOfView = fov;
	}

	void Teardown()
	{
		FocusDistance = 0f;
		Showing = false;
		_root?.Destroy();
		_root = null;
		_trooper = null;
		_previewCam = null;
		_weldLight = null;
		_key = _kicker = null;
		LightZones.StageAmbient = 1f;
	}

	/// <summary> The stage's own component in the scene (made on demand by the character screen). </summary>
	public static void Ensure( Scene scene )
	{
		if ( scene is null || scene.GetAllComponents<SwrpMenuStage>().Any() ) return;
		var go = new GameObject( true, "Menu Stage Driver" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.Components.Create<SwrpMenuStage>();
	}

	[ConCmd( "swrp_menu_stage", Help = "swrp_menu_stage <0/1> [x y z facing] - dev: build the character screen's hangar backdrop and film it with a hidden camera (prints its id)" )]
	public static void PreviewCommand( int on, float x = float.NaN, float y = 0, float z = 0, float facing = float.NaN )
	{
		if ( !float.IsNaN( x ) ) Spot = new Vector3( x, y, z );
		if ( !float.IsNaN( facing ) ) Facing = facing;
		Ensure( Game.ActiveScene );
		foreach ( var s in Game.ActiveScene?.GetAllComponents<SwrpMenuStage>() ?? Enumerable.Empty<SwrpMenuStage>() ) s.Teardown();
		Preview = on == 1;
		ClassId ??= ClassRegistry.Trooper;
	}
}
