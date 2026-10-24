using System;

namespace Swrp;

/// <summary>
/// Training dummies for the firing range (and for testing combat without a second player): a clone in armour that takes
/// hits, can be stunned and "killed", then stands back up. Admin only; they are networked so everyone sees them.
/// </summary>
public static class SwrpTraining
{
	public const string DummyTag = "dummy";

	[ConCmd( "swrp_dummy", Help = "swrp_dummy - admin: put a training dummy in front of you" )]
	public static void SpawnDummy() => RequestDummy();

	[ConCmd( "swrp_dummy_clear", Help = "swrp_dummy_clear - admin: remove every training dummy" )]
	public static void ClearDummies() => RequestClear();

	/// <summary> Use an item from your own inventory by id (bind it to a key: bind f "swrp_use bacta_patch"). </summary>
	[ConCmd( "swrp_use", Help = "swrp_use <item id> - use one of that item from your inventory" )]
	public static void UseItem( string itemId )
	{
		var p = SwrpPlayer.Local;
		if ( p is null ) return;
		var slot = p.Inventory.ToList().FindIndex( i => i.ItemId == itemId );
		if ( slot < 0 ) { SwrpNet.ShowLocal( $"You have no {ItemRegistry.Get( itemId )?.Name ?? itemId}.", true ); return; }
		p.UseItem( slot );
	}

	[ConCmd( "swrp_callmedic", Help = "swrp_callmedic - while downed, call for a medic (same as E)" )]
	public static void CallMedic() => SwrpPlayer.Local?.GetComponent<SwrpVitals>()?.CallMedic();

	[ConCmd( "swrp_giveup", Help = "swrp_giveup - while downed, give up (same as holding K)" )]
	public static void GiveUp() => SwrpPlayer.Local?.GetComponent<SwrpVitals>()?.GiveUp();

	/// <summary> Host only: start reviving the nearest downed trooper or dummy, as if holding E on them. </summary>
	[ConCmd( "swrp_test_revive", Help = "swrp_test_revive - host only: revive the nearest downed trooper as if holding E" )]
	public static void TestRevive()
	{
		if ( !Networking.IsHost ) return;
		var me = SwrpPlayer.Local?.GetComponent<SwrpVitals>();
		if ( me is null ) return;
		var target = me.Scene.GetAllComponents<SwrpVitals>().Where( v => v != me && v.IsWounded )
			.OrderBy( v => v.WorldPosition.DistanceSquared( me.WorldPosition ) ).FirstOrDefault();
		if ( target is null ) { Log.Info( "[SWRP] Nobody downed nearby." ); return; }
		me.BeginRevive( target );
	}

	/// <summary> Same as pressing X: toggle stun rounds on the weapon in your hands. </summary>
	[ConCmd( "swrp_stun", Help = "swrp_stun - toggle your blaster's stun setting (same as X)" )]
	public static void ToggleStun()
	{
		var blaster = SwrpPlayer.Local?.GetComponent<SwrpBlaster>();
		if ( blaster?.Stats is null ) return;
		if ( !blaster.Stats.CanStun ) { SwrpNet.ShowLocal( $"The {blaster.Weapon.Name} has no stun setting.", true ); return; }
		blaster.StunMode = !blaster.StunMode;
		SwrpNet.ShowLocal( blaster.StunMode ? "Stun setting: ON" : "Stun setting: OFF", false );
	}

	/// <summary> Host only (the host is always admin): fire your drawn blaster once, as if you clicked. For testing. </summary>
	[ConCmd( "swrp_test_fire", Help = "swrp_test_fire [shots] - host only: fire your drawn blaster as if you clicked" )]
	public static void TestFire( int shots = 1 )
	{
		if ( !Networking.IsHost ) { Log.Warning( "[SWRP] swrp_test_fire is host only." ); return; }
		var blaster = SwrpPlayer.Local?.GetComponent<SwrpBlaster>();
		if ( blaster is null ) return;
		for ( var i = 0; i < Math.Clamp( shots, 1, 30 ); i++ ) blaster.TestShot();
	}

	[Rpc.Host]
	static void RequestDummy()
	{
		var caller = Rpc.Caller;
		if ( caller is null || !Whitelist.IsAdmin( caller ) ) { SwrpNet.NotifyConnection( caller, "Admins only.", true ); return; }

		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !p.IsValid() ) { SwrpNet.NotifyConnection( caller, "Deploy first.", true ); return; }

		var fwd = Rotation.FromYaw( p.Controller.EyeAngles.yaw ).Forward;
		var pos = p.WorldPosition + fwd * 140f;
		var scene = p.Scene;

		var go = scene.CreateObject();
		go.Name = "Training Dummy";
		go.Tags.Add( DummyTag );
		go.WorldPosition = pos;
		go.WorldRotation = Rotation.LookAt( -fwd.WithZ( 0 ) );

		var body = new GameObject( go, true, "Body" );
		var r = body.AddComponent<SkinnedModelRenderer>();
		r.Model = ( ClassRegistry.Default.LoadModel() ) ?? Model.Load( "models/citizen/citizen.vmdl" );
		if ( p.Body.IsValid() ) r.BodyGroups = p.Body.BodyGroups;      // same armour pieces (helmet included) as a deployed trooper

		var col = go.AddComponent<CapsuleCollider>();
		col.Start = new Vector3( 0, 0, 12 );
		col.End = new Vector3( 0, 0, 60 );
		col.Radius = 12f;

		var vitals = go.AddComponent<SwrpVitals>();
		vitals.DummyBody = r;
		vitals.ResetFor( ClassRegistry.Default );

		go.NetworkSpawn();
		SwrpNet.NotifyConnection( caller, "Training dummy deployed. Remove with swrp_dummy_clear.", false );
	}

	[Rpc.Host]
	static void RequestClear()
	{
		var caller = Rpc.Caller;
		if ( caller is null || !Whitelist.IsAdmin( caller ) ) return;
		var scene = SwrpGameManager.Current?.Scene ?? Game.ActiveScene;
		foreach ( var v in scene.GetAllComponents<SwrpVitals>().Where( v => v.IsValid() && v.GameObject.IsValid() && v.GameObject.Tags.Has( DummyTag ) ).ToList() )
			v.GameObject.Destroy();
	}
}

/// <summary>
/// Local animation test stand: a clone body in front of you driven through the same animgraph parameters a trooper uses,
/// so the clone weapon set (swrp_clone_anims) can be checked from any side without a second player. Only you see it.
/// </summary>
public sealed class SwrpAnimMannequin : Component
{
	public int HoldType { get; set; } = 2;
	public float Duck { get; set; }

	/// <summary> The battalion whose armour a mannequin wears. </summary>
	public string Faction { get; set; }

	/// <summary> The model of the gun in its hand (the support hand's grip is looked up by it). </summary>
	public string GunPath { get; set; }
	/// <summary> A Jedi mannequin's looks (Appearance text). </summary>
	public string Appearance { get; set; }
	public float Speed { get; set; }
	public float FireEvery { get; set; }
	TimeSince _sinceShot;

	[ConCmd( "swrp_anim_test", Help = "swrp_anim_test [holdtype 0-5] [duck 0-1] [speed] [fire every s] [yaw] [battalion id] [class id] [weapon id] - local clone animation stand in front of you; swrp_anim_test -1 removes it" )]
	public static void Spawn( int holdType = 2, float duck = 0, float speed = 0, float fireEvery = 0, float yaw = 90, string battalion = "", string cls = "", string weaponId = "" )
	{
		var p = SwrpPlayer.Local;
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		// by name: a Jedi stand's mannequin is disabled (JediStand drives it), so a component search misses it
		foreach ( var old in scene.GetAllObjects( true ).Where( g => g.Name == "Anim Test" ).ToList() )
			old.Destroy();
		if ( holdType < 0 ) return;

		// in front of your trooper, or of the camera when you haven't deployed (so testing never needs a character)
		var cam = scene.Camera;
		var fwd = p is not null ? p.Controller.EyeAngles.WithPitch( 0 ).Forward : ( cam?.WorldRotation.Forward.WithZ( 0 ).Normal ?? Vector3.Forward );
		var origin = p is not null ? p.WorldPosition : cam is not null ? scene.Trace.Ray( cam.WorldPosition + fwd * 110f, cam.WorldPosition + fwd * 110f + Vector3.Down * 400f ).Run().EndPosition - fwd * 110f : Vector3.Zero;
		var go = scene.CreateObject();
		go.Name = "Anim Test";
		go.Tags.Add( SwrpTurbolift.RiderTag );       // rides lifts and teleporters, for testing them without a trooper
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = origin + fwd * 110f;
		go.WorldRotation = Rotation.LookAt( -fwd ) * Rotation.FromYaw( yaw );
		var r = go.AddComponent<SkinnedModelRenderer>();
		// "npc:<type>" as the class: that NPC's body and gun instead (swrp_anim_test 1 0 0 0 90 "" npc:b2); "cmdr:<battalion>" that
		// battalion's commander model (swrp_anim_test 2 0 0 0 90 "" cmdr:104th)
		var npc = cls.StartsWith( "npc:" ) ? NpcRegistry.Get( cls[4..] ) : null;
		var commander = cls.StartsWith( "cmdr:" ) ? SwrpCommanders.Load( SwrpCommanders.Models.GetValueOrDefault( cls[5..] ), holdType > 0 ) : null;
		r.Model = npc is not null ? Model.Load( npc.ModelPath ) : commander ?? ( ClassRegistry.Get( cls ) ?? ClassRegistry.Default ).LoadModel( squad: holdType > 0 ) ?? Model.Load( "models/citizen/citizen.vmdl" );
		if ( p?.Body.IsValid() == true && npc is null ) r.BodyGroups = p.Body.BodyGroups;
		var m = go.AddComponent<SwrpAnimMannequin>();
		m.HoldType = holdType; m.Duck = duck; m.Speed = speed; m.FireEvery = fireEvery;
		m.Faction = npc is not null ? null : ( FactionRegistry.Get( battalion ) ?? p?.Faction )?.Id;      // the trooper's battalion armour

		var weapon = npc is not null ? npc.Gun : ItemRegistry.Get( string.IsNullOrEmpty( weaponId ) ? ( holdType == 1 ? "dc17" : "dc15a" ) : weaponId )?.Weapon;
		m.GunPath = weapon?.ModelPath;
		if ( holdType > 0 && weapon?.ModelPath is { } path )
		{
			var gun = new GameObject( go, true, "Gun" );
			var gunMesh = new GameObject( gun, true, "Mesh" );
			gunMesh.AddComponent<ModelRenderer>().Model = Model.Load( path );
			weapon.PlaceMesh( gunMesh );
			gun.AddComponent<SwrpMannequinGun>().Body = r;
		}
	}

	/// <summary>
	/// Move the camera while you haven't deployed (the character screen), for looking around the map and at the test stand
	/// without a trooper. Does nothing once you have deployed: your trooper's camera owns the view then.
	/// </summary>
	static Scene _cameraPlacedIn;

	/// <summary> swrp_cam has placed the camera in this play session: the character screen steps aside and the area grade previews. </summary>
	public static bool CameraPlaced => _cameraPlacedIn is not null && _cameraPlacedIn == Game.ActiveScene;

	[ConCmd( "swrp_cam", Help = "swrp_cam <x> <y> <z> [pitch] [yaw] - before deploying: put the camera at map coordinates (for screenshots and the test stand)" )]
	public static void MoveCamera( float x, float y, float z, float pitch = 0, float yaw = 0 )
	{
		var cam = Game.ActiveScene?.Camera;
		if ( cam is null ) return;
		if ( SwrpPlayer.Local is not null )
		{
			Log.Info( "[SWRP] swrp_cam only works before you deploy." );
			return;
		}
		cam.WorldPosition = new Vector3( x, y, z );
		cam.WorldRotation = Rotation.From( pitch, yaw, 0 );
		_cameraPlacedIn = Game.ActiveScene;
	}

	/// <summary> Play one named sequence of the clone model on the test stand, outside the animgraph (e.g. clone_walk). </summary>
	[ConCmd( "swrp_anim_seq", Help = "swrp_anim_seq <sequence> - play a sequence straight on the animation test stand" )]
	public static void PlaySequence( string name )
	{
		var m = Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>().FirstOrDefault();
		var r = m?.GetComponent<SkinnedModelRenderer>();
		if ( r is null ) { Log.Warning( "[SWRP] Spawn the stand first: swrp_anim_test" ); return; }
		m.SequenceMode = !string.IsNullOrEmpty( name );
		r.UseAnimGraph = !m.SequenceMode;
		if ( m.SequenceMode )
		{
			r.Sequence.Name = name;
			r.Sequence.Looping = true;
			var names = r.Sequence.SequenceNames.ToList();
			Log.Info( $"[SWRP] playing {name} on {r.Model?.ResourcePath}; {names.Count} sequences, clone set: {string.Join( ", ", names.Where( n => n.Contains( "lone", StringComparison.OrdinalIgnoreCase ) ) )}" );
		}
	}

	public bool SequenceMode { get; set; }
	/// <summary> The way the stand aims (its body may be turned side-on to it, SwrpEmotes.BladeYaw). </summary>
	public float AimYaw => _aimYaw ?? WorldRotation.Yaw();
	float? _aimYaw;

	/// <summary>
	/// Dev: render the test stand from a point around it to data/shots/&lt;name&gt;.png with a camera of its own, so poses can be
	/// checked from any side even while a trooper is deployed (their camera is left alone). Offsets are in the stand's aim
	/// frame, in inches: forward, right, up from its feet; the second triple is where the camera looks.
	/// </summary>
	[ConCmd( "swrp_stand_shot", Help = "swrp_stand_shot <name> <fwd> <right> <up> [look fwd right up] [fov] [size] - dev: render the animation test stand to data/shots/<name>.png" )]
	public static void StandShot( string name, float f, float r, float u, float tf = 10f, float tr = 0f, float tu = 50f, float fov = 60f, int size = 800 )
	{
		var scene = Game.ActiveScene;
		var m = TestStand( scene );
		if ( m is null ) { Log.Warning( "[SWRP] Spawn the stand first: swrp_anim_test" ); return; }
		var aim = Rotation.FromYaw( m.AimYaw );
		var feet = m.WorldPosition;
		Vector3 At( float a, float b, float c ) => feet + aim.Forward * a + aim.Right * b + Vector3.Up * c;
		var go = scene.CreateObject();
		go.Name = "Stand Shot Camera";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var cam = go.AddComponent<CameraComponent>();
		cam.IsMainCamera = false;
		cam.Priority = -100;
		cam.FieldOfView = fov;
		cam.ZNear = 1f;
		go.WorldPosition = At( f, r, u );
		go.WorldRotation = Rotation.LookAt( At( tf, tr, tu ) - go.WorldPosition );
		var bmp = new Bitmap( size, size );
		cam.RenderToBitmap( bmp );
		FileSystem.Data.CreateDirectory( "shots" );
		FileSystem.Data.WriteAllBytes( $"shots/{name}.png", bmp.ToPng() );
		go.Destroy();
		Log.Info( $"[SWRP] stand shot shots/{name}.png" );
	}
	float _blade;

	/// <summary> The animation test stand (or the pose stage): other mannequins (hangar crew, the character screen's trooper) aren't it. </summary>
	public static SwrpAnimMannequin TestStand( Scene scene )
	{
		var all = scene?.GetAllComponents<SwrpAnimMannequin>().ToList();
		return all?.FirstOrDefault( m => m.GameObject.Name == "Anim Test" ) ?? all?.FirstOrDefault( m => m.GameObject.Name == "Pose Stage" );
	}

	[ConVar( "swrp_stage_aim_pitch", Help = "Dev: the test stands aim this far down (degrees; 28 = the weapon-safety low ready, 0 = level)" )]
	public static float StageAimPitch { get; set; }

	[ConVar( "swrp_stand_npc_blade", Help = "Dev: 1 = the test stand turns side-on the way NPC clones do (the skeleton under the body), 0 = as players (the body)" )]
	public static bool StandNpcBlade { get; set; }

	[ConVar( "swrp_stage_ads", Help = "Dev: the test stands aim down the sights (as a player holding right mouse)" )]
	public static bool StageAds { get; set; }

	/// <summary> Dev (code_run_static_method): play a GMod gesture on the pose stage ("range_ar2", "reload_ar2", ...). </summary>
	public static void StageGesture( string name )
	{
		if ( Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>().FirstOrDefault( m => m.GameObject.Name == "Pose Stage" )?.GetComponent<SkinnedModelRenderer>() is { } r )
			SwrpEmotes.GmodGesture( r, name );
	}

	[ConVar( "swrp_stage_duck", Help = "Dev: how far the pose stage crouches (0-1, players crouch to SwrpPlayer.CrouchDepth)" )]
	public static float StageDuck { get; set; }

	[ConVar( "swrp_stage_relaxed", Help = "Dev: the test stands carry the rifle relaxed, muzzle down (the third Shift+E+R mode)" )]
	public static bool StageRelaxed { get; set; }

	[ConVar( "swrp_stage_aim_turn", Help = "Dev: the test stands aim this many degrees left of where they were put (to check the feet staying planted through small turns)" )]
	public static float StageAimTurn { get; set; }

	[ConVar( "swrp_stage_aim_up", Help = "Dev: the test stands keep the rifle shouldered and aim this far up (degrees; negative aims down)" )]
	public static float StageAimUp { get; set; }

	[ConVar( "swrp_stage_walk_dir", Help = "Dev: which way a walking test stand walks, degrees from its aim (0 forward, 90 left, 180 back, -90 right)" )]
	public static float StageWalkDir { get; set; }

	[ConVar( "swrp_stage_move", Help = "Dev: a walking test stand really moves (along its walk direction, back to its start every 120 inches) instead of walking on the spot" )]
	public static bool StageMove { get; set; }

	[ConVar( "swrp_stage_turn_rate", Help = "Dev: the test stands' aim keeps turning left at this many degrees a second (negative: right)" )]
	public static float StageTurnRate { get; set; }

	Vector3? _home;
	float _turned, _legs, _walk;

	/// <summary> Dev: put the test stand somewhere clear (map coordinates, its feet on the floor below), aiming along yaw. </summary>
	[ConCmd( "swrp_stand_place", Help = "swrp_stand_place <x> <y> <z> [yaw] - dev: move the animation test stand (the floor is found below the point)" )]
	public static void StandPlace( float x, float y, float z, float yaw = 0f )
	{
		var scene = Game.ActiveScene;
		var m = TestStand( scene );
		if ( m is null ) { Log.Warning( "[SWRP] Spawn the stand first: swrp_anim_test" ); return; }
		var at = new Vector3( x, y, z );
		var floor = scene.Trace.Ray( at + Vector3.Up * 40f, at + Vector3.Down * 400f ).WithoutTags( "player", "npc", "trigger" ).IgnoreGameObjectHierarchy( m.GameObject ).Run();
		if ( floor.Hit ) at = floor.HitPosition;
		m._home = at;
		m.WorldPosition = at;
		m._aimYaw = yaw;
		Log.Info( $"[SWRP] stand at {at.x:0} {at.y:0} {at.z:0}, aiming {yaw:0}" );
	}

	/// <summary>
	/// Dev: <see cref="StandShot"/> over time: <paramref name="frames"/> shots <paramref name="interval"/> seconds apart from one
	/// camera (shots/&lt;name&gt;_&lt;i&gt;.png), taken on the game's own clock so the timing is exact (walk cycles, recoil, turns).
	/// </summary>
	[ConCmd( "swrp_stand_film", Help = "swrp_stand_film <name> <frames> <interval s> <fwd> <right> <up> [look fwd right up] [fov] [size] - dev: a run of stand shots over time" )]
	public static void StandFilm( string name, int frames, float interval, float f, float r, float u, float tf = 10f, float tr = 0f, float tu = 50f, float fov = 60f, int size = 600 )
	{
		var scene = Game.ActiveScene;
		if ( TestStand( scene ) is null ) { Log.Warning( "[SWRP] Spawn the stand first: swrp_anim_test" ); return; }
		var go = scene.CreateObject();
		go.Name = "Stand Film";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var film = go.AddComponent<SwrpStandFilm>();
		film.Shot = i => StandShot( $"{name}_{i}", f, r, u, tf, tr, tu, fov, size );
		film.Frames = Math.Clamp( frames, 1, 64 );
		film.Interval = MathF.Max( interval, 0f );
	}
	public int Emote { get; set; }

	/// <summary> Carry the rifle low as when sprinting (the stand runs in place if Speed is set). </summary>
	public bool Carry { get; set; }

	/// <summary>
	/// A pose stage nobody sees: the test trooper set up at a map position (default: the engineering deck corridor) with its
	/// own camera, which is left disabled so it never draws to anyone's screen; render it with the editor's camera screenshot
	/// by the id this prints. For checking emotes, the rifle hold and the sprint carry while someone is playing.
	/// </summary>
	/// <summary> Dev: the pose stage holds its gun with the original drill set (1) instead of the Squad set players use (0). </summary>
	[ConVar( "swrp_pose_stage_drill", Help = "Dev: 1 = the pose stage holds its gun with the drill set; 0 (default) = the Squad set, like a player with a weapon out" )]
	public static bool StageDrill { get; set; }

	[ConCmd( "swrp_pose_stage", Help = "swrp_pose_stage <emote 0-3> [holdtype] [carry 0/1] [view yaw] [distance] [view height] [speed] [x y z] [weapon id] [class id] - local pose stage with its own hidden camera; swrp_pose_stage -1 removes it" )]
	public static void Stage( int emote, int holdType = 0, int carry = 0, float viewYaw = 0, float distance = 95, float viewHeight = 55, float speed = 0, float x = 0, float y = 3900, float z = -1850, string weaponId = "", string cls = "" )
	{
		var scene = Game.ActiveScene;
		if ( scene is null ) return;
		foreach ( var old in scene.GetAllComponents<SwrpAnimMannequin>().Where( m => m.GameObject.Name == "Pose Stage" ).ToList() )
			old.GameObject.Destroy();
		if ( emote < 0 ) return;

		var at = new Vector3( x, y, z );
		var floor = scene.Trace.Ray( at + Vector3.Up * 60f, at + Vector3.Down * 400f ).WithoutTags( "player", "npc", "trigger" ).Run();
		if ( floor.Hit ) at = floor.HitPosition;

		var go = scene.CreateObject();
		go.Name = "Pose Stage";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = at;
		go.WorldRotation = Rotation.FromYaw( 0 );
		go.WorldScale = SwrpPlayer.PlayerScaleFor( scene );
		var r = go.AddComponent<SkinnedModelRenderer>();
		// a gun out: the Squad movement set ("_squad" model), as on a player with a weapon drawn (SwrpPlayer.ApplyClassModel)
		var stageClass = ClassRegistry.Get( cls ) ?? ClassRegistry.Default;
		var gmodSet = ClassDefinition.Custom ? null : stageClass.LoadGmod( holdType == 0 ? "normal" : SwrpEmotes.TfaHold( weaponId, holdType, StageRelaxed, false, StageAds, false ) );
		r.Model = gmodSet ?? stageClass.LoadModel( squad: holdType > 0 && !StageDrill, patrol: StageRelaxed ) ?? Model.Load( "models/citizen/citizen.vmdl" );
		var m = go.AddComponent<SwrpAnimMannequin>();
		m.HoldType = holdType;
		m.Emote = emote;
		m.Carry = carry == 1;
		m.Speed = speed;
		m.Duck = StageDuck;
		if ( holdType > 0 && ItemRegistry.Get( string.IsNullOrEmpty( weaponId ) ? ( holdType == 1 ? "dc17" : "dc15a" ) : weaponId )?.Weapon is { ModelPath: { } path } weapon )
		{
			m.GunPath = path;
			var gun = new GameObject( go, true, "Gun" );
			var gunMesh = new GameObject( gun, true, "Mesh" );
			gunMesh.AddComponent<ModelRenderer>().Model = Model.Load( path );
			weapon.PlaceMesh( gunMesh );
			gun.AddComponent<SwrpMannequinGun>().Body = r;
		}

		// the camera: chest height, looking at the trooper from viewYaw (0 = from the front)
		var camGo = new GameObject( go, true, "Pose Stage Camera" );
		camGo.NetworkMode = NetworkMode.Never;
		var look = Rotation.FromYaw( viewYaw );
		var chest = at + Vector3.Up * viewHeight * go.WorldScale.z;
		camGo.WorldPosition = chest + look.Forward * distance + Vector3.Up * 8f;
		camGo.WorldRotation = Rotation.LookAt( chest - camGo.WorldPosition );
		var cam = camGo.AddComponent<CameraComponent>();
		cam.FieldOfView = 50f;
		cam.IsMainCamera = false;
		cam.Priority = -100;
		cam.Enabled = false;
		Log.Info( $"[SWRP] pose stage at {at.x:0} {at.y:0} {at.z:0}: camera {cam.Id} (object {camGo.Id})" );
	}

	/// <summary>
	/// Dev: a weapon model's side profile in the grip frame (x forward along the barrel from the grip, z up), one line per
	/// 2 units: the lowest and highest point of the gun there. With the pose stage up, also where its hands sit in that frame.
	/// </summary>
	[ConCmd( "swrp_gun_profile", Help = "swrp_gun_profile [item id] - dev: print a weapon model's side profile in the grip frame (and the pose stage's hands)" )]
	public static void GunProfile( string itemId = "dc15a" )
	{
		var w = ItemRegistry.Get( itemId )?.Weapon;
		if ( w?.ModelPath is null ) { Log.Info( "[SWRP] no weapon model" ); return; }
		var model = Model.Load( w.ModelPath );
		var verts = model.GetVertices();
		if ( verts is null || verts.Length == 0 ) { Log.Info( "[SWRP] no vertices" ); return; }
		var place = new Transform( w.ModelOffset, w.ModelAngles.ToRotation(), w.ModelScale );
		var bins = new SortedDictionary<int, (float Lo, float Hi, float YLo, float YHi)>();
		foreach ( var v in verts )
		{
			var p = place.PointToWorld( v.Position );
			var k = (int)MathF.Floor( p.x / 2f );
			bins[k] = bins.TryGetValue( k, out var b ) ? ( MathF.Min( b.Lo, p.z ), MathF.Max( b.Hi, p.z ), MathF.Min( b.YLo, p.y ), MathF.Max( b.YHi, p.y ) ) : ( p.z, p.z, p.y, p.y );
		}
		Log.Info( $"[SWRP] {itemId} {w.ModelPath}: {verts.Length} verts, muzzle at x {w.MuzzleForward} z {w.MuzzleUp}" );
		foreach ( var (k, b) in bins )
			Log.Info( $"[gun] x {k * 2,4}..{k * 2 + 2,-4} z {b.Lo,6:0.0} .. {b.Hi,6:0.0}   y {b.YLo,5:0.0} .. {b.YHi,5:0.0}" );

		var stage = Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>().FirstOrDefault();
		var r = stage?.GetComponent<SkinnedModelRenderer>();
		if ( r is not null && r.GetAttachment( "hold_R" ) is { } hold )
		{
			foreach ( var name in new[] { "hand_R", "finger_middle_0_R", "finger_middle_1_R", "finger_middle_2_R", "finger_thumb_2_R", "hand_L", "finger_middle_0_L", "finger_middle_1_L", "finger_middle_2_L", "finger_thumb_2_L" } )
			{
				var bone = r.Model.Bones.GetBone( name );
				if ( bone is not null && r.TryGetBoneTransform( in bone, out var t ) )
				{
					var local = hold.PointToLocal( t.Position ) / r.WorldScale.z;
					var rot = hold.Rotation.Inverse * t.Rotation;
					Log.Info( $"[gun] {name} in grip frame (unscaled): {local.x:0.0} {local.y:0.0} {local.z:0.0}, fingers {( rot.Forward ):0.00} palm/back {( rot.Up ):0.00}" );
				}
			}
		}
	}

	[ConCmd( "swrp_anim_emote", Help = "swrp_anim_emote <0 none | 1 salute | 2 at ease | 3 wrist comm> - pose the animation test stand" )]
	public static void EmoteCommand( int emote )
	{
		foreach ( var m in Game.ActiveScene?.GetAllComponents<SwrpAnimMannequin>() ?? Enumerable.Empty<SwrpAnimMannequin>() ) m.Emote = emote;
	}

	/// <summary> Dev (code_run_static_method): force the stands' graph move_style (1 walk, 2 run); -1 leaves it. </summary>
	public static int StageMoveStyle { get; set; } = -1;

	// dev (swrp_npc_foot_debug): the stand stays put, so its planted foot slides back at the speed the clip really covers
	Vector3 _probeL, _probeR; readonly List<float> _probe = new(); RealTimeSince _sinceProbe;
	void ProbeFeet( SkinnedModelRenderer r )
	{
		if ( r.GetBoneObject( "ankle_L" ) is not { } l || r.GetBoneObject( "ankle_R" ) is not { } rr ) { r.CreateBoneObjects = true; return; }
		var dt = MathF.Max( Time.Delta, 0.001f );
		// the lower foot is the one on the ground (the slower one misreads a run's airborne moments)
		var low = l.WorldPosition.z <= rr.WorldPosition.z ? ( l.WorldPosition - _probeL ) : ( rr.WorldPosition - _probeR );
		_probe.Add( low.WithZ( 0 ).Length / dt );
		_probeL = l.WorldPosition; _probeR = rr.WorldPosition;
		if ( _sinceProbe < 1.5f ) return;
		_probe.Sort();
		Log.Info( $"[SWRP] stand feet: speed {Speed:0} style {r.Parameters.GetInt( "move_style" )} duck {Duck:0.0}, planted foot {_probe[_probe.Count / 2]:0} u/s (median), playback {r.PlaybackRate:0.00}" );
		_probe.Clear(); _sinceProbe = 0;
	}

	protected override void OnUpdate()
	{
		var r = GetComponent<SkinnedModelRenderer>();
		if ( !r.IsValid() || SequenceMode ) return;
		if ( SwrpNpc.FootDebug && Speed > 0f ) ProbeFeet( r );
		// the stand aims where it was put facing; shouldered (rifle, not lowered) the body turns side-on to that (SwrpEmotes.BladeYaw)
		// and, standing still, takes the shooting stance. A player's body turns itself; an NPC's skeleton turns under its body
		// (swrp_stand_npc_blade 1 checks that path here)
		_aimYaw ??= WorldRotation.Yaw();
		_home ??= WorldPosition;
		// swrp_stage_aim_turn swings the aim without moving the cameras; swrp_stage_turn_rate keeps it turning
		_turned = StageTurnRate == 0f ? 0f : ( _turned + StageTurnRate * Time.Delta ) % 360f;
		var aimYaw = _aimYaw.Value + StageAimTurn + _turned;
		var shouldered = ClassDefinition.Custom && HoldType == 2 && StageAimPitch <= 0f && !Carry && !StageRelaxed && Emote == 0 && SwrpEmotes.ShoulderedAtEye( GunPath ?? ItemRegistry.Get( "dc15a" )?.Weapon?.ModelPath );
		_blade = _blade.Approach( shouldered ? 1f : 0f, Time.Delta * 6f );
		var bw = _blade * _blade * ( 3f - 2f * _blade );
		// as a player: walking, the hips turn back towards the aim and the chest alone stays side-on (SwrpEmotes.WalkChestShare)
		_walk = _walk.Approach( Speed > 0f ? 1f : 0f, Time.Delta * 6f );
		var bodyBlade = SwrpEmotes.BladeYaw * bw * ( 1f - SwrpEmotes.WalkChestShare * _walk );
		var chestYaw = -SwrpEmotes.BladeYaw * bw * SwrpEmotes.WalkChestShare * _walk;
		var skeletonYaw = StandNpcBlade ? -bodyBlade : 0f;
		WorldRotation = Rotation.FromYaw( aimYaw - ( StandNpcBlade ? 0f : bodyBlade ) );
		var fwd = Rotation.FromYaw( aimYaw ).Forward;
		// walking: swrp_stage_walk_dir from the aim, on the spot or (swrp_stage_move) really moving, back to the start every 120 in
		var walkDir = Rotation.FromYaw( _aimYaw.Value + StageAimTurn + StageWalkDir ).Forward;
		if ( StageMove && Speed > 0f )
		{
			WorldPosition += walkDir * Speed * Time.Delta;
			if ( WorldPosition.Distance( _home.Value ) > 120f ) WorldPosition = _home.Value;
		}
		else if ( WorldPosition != _home.Value ) WorldPosition = _home.Value;
		var local = ( Rotation.FromYaw( skeletonYaw ) * WorldRotation ).Inverse * ( walkDir * Speed );
		_legs = _legs.Approach( Speed > 0f ? 0f : 1f, Time.Delta * ( Speed > 0f ? 8f : 4f ) );      // as a player settling in (SwrpPlayer.UpdateBlade)
		var aimDir = Rotation.From( StageAimPitch > 0f ? 0f : -StageAimUp, aimYaw, 0f ).Forward;
		var look = Rotation.FromYaw( -skeletonYaw ) * aimDir;      // the graph reads the look-at against the unturned (root) body
		r.Set( "holdtype", StageRelaxed || r.Model?.ResourcePath?.Contains( "_gmod_" ) == true ? 0 : HoldType );      // as a player (SwrpBlaster)
		r.Set( "move_style", StageMoveStyle >= 0 ? StageMoveStyle : StageRelaxed && ClassDefinition.Custom ? 1 : 0 );
		r.Set( "b_weapon_lower", StageRelaxed && !ClassDefinition.Custom );      // stock: relaxed is the lowered gun (Shift+E+R)
		r.Set( "duck", Duck );
		r.Set( "b_grounded", true );
		r.Set( "move_groundspeed", Speed );
		r.Set( "move_speed", Speed );
		r.Set( "move_x", local.x );
		r.Set( "move_y", local.y );
		r.Set( "wish_groundspeed", Speed );
		r.Set( "wish_x", local.x );
		r.Set( "wish_y", local.y );
		r.Set( "wish_speed", Speed );
		SwrpEmotes.GmodPace( r );
		r.SetLookDirection( "aim_eyes", look );
		r.SetLookDirection( "aim_head", look );
		r.SetLookDirection( "aim_body", SwrpEmotes.BladedBodyLook( look, WorldRotation, bw ) );
		SwrpEmotes.Stance( r, bw * _legs * ( 1f - Duck ), bw, aimYaw, skeletonYaw, chestYaw );
		if ( FireEvery > 0 && _sinceShot > FireEvery )
		{
			_sinceShot = 0;
			r.Set( "b_attack", true );
			SwrpEmotes.Kick( r );
		}
		SwrpOutfit.Apply( r, Faction, false, Appearance );
		r.Tint = RoomLight.BodyTint( this, WorldPosition );
		if ( HoldType is 1 or 2 ) SwrpEmotes.HeldModel[r] = GunPath ?? ItemRegistry.Get( HoldType == 1 ? "dc17" : "dc15a" )?.Weapon?.ModelPath;
		SwrpEmotes.AimDir[r] = Rotation.From( StageAimPitch > 0f ? StageAimPitch : -StageAimUp, aimYaw, 0f ).Forward;
		if ( StageAimPitch > 0f ) SwrpEmotes.Lowered.Add( r ); else SwrpEmotes.Lowered.Remove( r );
		if ( StageAds ) SwrpEmotes.Aiming.Add( r ); else SwrpEmotes.Aiming.Remove( r );
		if ( StageRelaxed ) SwrpEmotes.Relaxed.Add( r ); else SwrpEmotes.Relaxed.Remove( r );
		if ( r.Model?.ResourcePath is { } mp && mp.Contains( "_gmod_" ) && HoldType is 1 or 2 && !StageRelaxed ) SwrpEmotes.GmodAim[r] = mp[( mp.LastIndexOf( "_gmod_" ) + 6 )..^5];
		else SwrpEmotes.GmodAim.Remove( r );
		SwrpEmotes.Apply( r, Emote == 3 ? SwrpEmotes.None : Emote, 1f, Emote == 3 ? 1f : 0f, Carry || StageRelaxed ? 1f : 0f, Speed > 0f ? Time.Now * 0.8f % 1f : 0f, HoldType is 1 or 2 ? 1f : 0f );
	}
}

/// <summary> Takes a run of test-stand shots on the game clock (swrp_stand_film), then removes itself. </summary>
public sealed class SwrpStandFilm : Component
{
	public Action<int> Shot { get; set; }
	public int Frames { get; set; }
	public float Interval { get; set; }
	int _taken;
	TimeSince _since;

	protected override void OnStart() => _since = Interval;

	protected override void OnPreRender()
	{
		// (after the stand has posed this frame: its OnUpdate has run)
		if ( _taken >= Frames || Shot is null ) { GameObject.Destroy(); return; }
		if ( _since < Interval ) return;
		_since = 0;
		Shot( _taken++ );
		if ( _taken >= Frames ) Log.Info( $"[SWRP] stand film done: {_taken} shots" );
	}
}

/// <summary> Keeps the test stand's blaster in its right hand, like SwrpBlaster does for troopers. </summary>
public sealed class SwrpMannequinGun : Component
{
	public SkinnedModelRenderer Body { get; set; }

	/// <summary> Nudge from the hand, in the body's frame (forward, left, up), for armour bulkier than the model the poses were made on. </summary>
	public Vector3 Offset { get; set; }

	protected override void OnPreRender()
	{
		if ( Body.IsValid() && Body.GetAttachment( "hold_R" ) is { } raw )
		{
			var hold = raw;
			WorldPosition = hold.Position + Body.WorldRotation * Offset * Body.WorldScale.z;
			WorldRotation = hold.Rotation;
		}
	}
}
