using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Swrp;

/// <summary>
/// The droideka's body (Angelo, 2026-10-13): Battlefront II's droideka model and rig (ModelSource/bf2_droideka.py; local only)
/// posed by code, since the export has no animations. BF2's bind pose is the deployed stance; Assets/data/droideka_poses.json
/// holds the rolled-into-a-wheel pose as each bone's model-space turn from the bind pose. Every frame the bones are blended
/// between the two (the unfold is staged: shell and spine first, then legs, then arms and head), the whole body spins about the
/// wheel's axle while rolling, and on top come the aim (head and arms follow the target), the alternating arm recoil when it
/// fires, a slight breathing sway, and a slump when it dies. Purely local and visual; SwrpNpc drives it.
/// </summary>
public sealed class DroidekaRig : Component
{
	public const string ModelPath = "models/droids/bf2_droideka.vmdl";
	const string PoseFile = "data/droideka_poses.json";

	/// <summary> 1 deployed, 0 rolled up (set by SwrpNpc from its synced Deployed flag; eased here). </summary>
	public bool WantDeployed { get; set; }
	/// <summary> Ground speed for the wheel spin (units/s). </summary>
	public float Speed { get; set; }
	/// <summary> World point it's aiming at (zero = straight ahead). </summary>
	public Vector3 AimAt { get; set; }
	public bool Dead { get; set; }

	/// <summary> How far unfolded the body is (0 rolled .. 1 standing). </summary>
	public float Unfold => _unfold;
	public SkinnedModelRenderer Body => _body;

	SkinnedModelRenderer _body;
	GameObject _bodyGo;
	bool _ready;
	float _unfold;
	float _spin;          // wheel angle, degrees
	float _dead;
	bool _burnt;
	TimeSince _sinceSmoke;
	float _kickL, _kickR;
	bool _leftNext = true;

	// calibration: Blender axes -> model axes, and Blender inches -> model units
	Rotation _c = Rotation.Identity;
	float _k = 1f;
	Rotation _face = Rotation.Identity;     // model -> facing +X, up +Z
	Vector3 _wheelCentre;
	float _wheelRadius = 22f;
	Vector3 _wheelAxis = Vector3.Forward;

	sealed class BoneState
	{
		public GameObject Go;
		public int Parent = -1;
		public Vector3 RestPos;               // model space (relative to the body object)
		public Rotation RestRot;
		public Rotation Rolled = Rotation.Identity;     // model-space turn from the bind pose (absolute, includes parents)
		public Rotation RolledOwn = Rotation.Identity;  // its own share: parent's inverse * its own
		public int Stage;                     // 0 spine/shell, 1 legs, 2 arms/head
		public Rotation Extra = Rotation.Identity;
	}
	readonly List<BoneState> _bones = new();
	readonly Dictionary<string, int> _index = new();

	sealed class PoseData
	{
		[JsonPropertyName( "refs" )] public Dictionary<string, float[]> Refs { get; set; }
		[JsonPropertyName( "wheel_centre" )] public float[] WheelCentre { get; set; }
		[JsonPropertyName( "wheel_radius" )] public float WheelRadius { get; set; }
		[JsonPropertyName( "poses" )] public Dictionary<string, Dictionary<string, float[]>> Poses { get; set; }
	}
	static PoseData _poses;
	static bool _logged;

	protected override void OnStart()
	{
		_bodyGo = new GameObject( GameObject, true, "Droideka Body" );
		_bodyGo.NetworkMode = NetworkMode.Never;
		_bodyGo.Flags |= GameObjectFlags.NotSaved;
		_body = _bodyGo.AddComponent<SkinnedModelRenderer>();
		_body.Model = Model.Load( ModelPath );
		_body.UseAnimGraph = false;
		_body.CreateBoneObjects = true;
		_unfold = WantDeployed ? 1f : 0f;
	}

	static bool _posesTried;

	static PoseData Poses
	{
		get
		{
			if ( _poses is not null || _posesTried ) return _poses;
			_posesTried = true;      // once: a missing file used to warn every frame per droideka
			try { _poses = Json.Deserialize<PoseData>( SwrpDataFile.ReadText( PoseFile ) ); }
			catch ( Exception e ) { Log.Warning( $"[SWRP] droideka poses: {e.Message}" ); }
			return _poses;
		}
	}

	static Vector3 V( float[] a ) => new( a[0], a[1], a[2] );

	/// <summary> Once the bone objects exist: rest transforms, the axis calibration and each bone's rolled turn. </summary>
	bool Setup()
	{
		var model = _body.Model;
		var data = Poses;
		if ( model is null || model.IsError || data is null ) return false;
		var all = model.Bones.AllBones;
		var gos = new List<GameObject>();
		foreach ( var b in all )
		{
			var go = _body.GetBoneObject( b );
			if ( go is null ) return false;
			gos.Add( go );
		}

		_bones.Clear();
		_index.Clear();
		var bodyTx = _bodyGo.WorldTransform;
		for ( var i = 0; i < all.Count; i++ )
		{
			var local = bodyTx.ToLocal( gos[i].WorldTransform );
			_bones.Add( new BoneState { Go = gos[i], RestPos = local.Position, RestRot = local.Rotation, Parent = all[i].Parent?.Index ?? -1 } );
			_index[all[i].Name] = i;
		}

		// line Blender's axes up with the model's from three bones measured both ways
		Vector3 Rest( string n ) => _index.TryGetValue( n, out var i ) ? _bones[i].RestPos : Vector3.Zero;
		var bHips = V( data.Refs["Hips"] ); var bHead = V( data.Refs["Head"] ); var bArm = V( data.Refs["LeftArm"] );
		var sHips = Rest( "Hips" ); var sHead = Rest( "Head" ); var sArm = Rest( "LeftArm" );
		Rotation Frame( Vector3 up, Vector3 side ) => Rotation.LookAt( Vector3.Cross( side, up ).Normal, up.Normal );
		var bUp = ( bHead - bHips ).Normal; var bSide = ( bArm - bHips ); bSide = ( bSide - bUp * Vector3.Dot( bSide, bUp ) ).Normal;
		var sUp = ( sHead - sHips ).Normal; var sSide = ( sArm - sHips ); sSide = ( sSide - sUp * Vector3.Dot( sSide, sUp ) ).Normal;
		_c = Frame( sUp, sSide ) * Frame( bUp, bSide ).Inverse;
		_k = ( sHead - sHips ).Length / MathF.Max( 0.001f, ( bHead - bHips ).Length );
		if ( !_logged ) { _logged = true; Log.Info( $"[SWRP] droideka rig: model units per BF2 inch {_k:0.###}, head {sHead}, hips {sHips}" ); }

		// facing: Blender's front is -Y, up +Z
		var front = _c * new Vector3( 0, -1, 0 );
		var up = _c * new Vector3( 0, 0, 1 );
		_face = Rotation.LookAt( front, up ).Inverse;
		_wheelCentre = _c * V( data.WheelCentre ) * _k;
		_wheelRadius = data.WheelRadius * _k;
		_wheelAxis = _c * new Vector3( 1, 0, 0 );

		// the rolled pose, converted into model axes
		var rolled = data.Poses.TryGetValue( "rolled", out var r ) ? r : new();
		foreach ( var (name, q) in rolled )
		{
			if ( !_index.TryGetValue( name, out var i ) ) continue;
			var rb = new Rotation( q[1], q[2], q[3], q[0] );       // stored w, x, y, z
			_bones[i].Rolled = _c * rb * _c.Inverse;
		}
		for ( var i = 0; i < _bones.Count; i++ )
		{
			var b = _bones[i];
			var parentRolled = b.Parent >= 0 ? _bones[b.Parent].Rolled : Rotation.Identity;
			b.RolledOwn = parentRolled.Inverse * b.Rolled;
			var n = all[i].Name;
			b.Stage = n.Contains( "Leg" ) || n.Contains( "Foot" ) || n.Contains( "Toe" ) || n.Contains( "Hips" ) ? 1
				: n.Contains( "Arm" ) || n.Contains( "Hand" ) || n.Contains( "Gun" ) || n.Contains( "Neck" ) || n.Contains( "Head" ) || n.Contains( "Sensor" ) || n.StartsWith( "PROC" ) ? 2 : 0;
			b.Go.Flags |= GameObjectFlags.ProceduralBone;
		}
		_ready = true;
		return true;
	}

	/// <summary> A shot: the next arm kicks back. Returns that arm's muzzle. </summary>
	public Vector3 Fire()
	{
		var left = _leftNext;
		_leftNext = !_leftNext;
		if ( left ) _kickL = 1f; else _kickR = 1f;
		return MuzzleWorld( left );
	}

	/// <summary> Where the next shot leaves (a gun muzzle bone), or a point in front if the rig isn't up yet. </summary>
	public Vector3 MuzzleWorld( bool? left = null )
	{
		var l = left ?? _leftNext;
		var name = l ? "LeftGunMuzzle1" : "RightGunMuzzle1";
		if ( _ready && _index.TryGetValue( name, out var i ) && _bones[i].Go.IsValid() ) return _bones[i].Go.WorldPosition;
		return WorldPosition + WorldRotation.Forward * 26f + Vector3.Up * 30f;
	}

	/// <summary> Centre of the body in the world (for the shield and sparks). </summary>
	public Vector3 Centre => WorldPosition + Vector3.Up * MathX.Lerp( _wheelRadius, 40f, _unfold );

	protected override void OnUpdate()
	{
		if ( !_body.IsValid() ) return;
		if ( !_ready && !Setup() ) return;
		var dt = Time.Delta;

		// unfold: about 1.1 s out of the wheel, 0.8 s back into it
		if ( !Dead ) _unfold = _unfold.Approach( WantDeployed ? 1f : 0f, dt / ( WantDeployed ? 1.1f : 0.8f ) );
		if ( Dead ) _dead = _dead.Approach( 1f, dt / 0.7f );
		if ( Dead && !_burnt ) Burn();
		if ( _burnt ) Smoulder();
		_kickL = _kickL.Approach( 0f, dt * 7f );
		_kickR = _kickR.Approach( 0f, dt * 7f );

		// the wheel spins with the ground speed; as it opens it rolls on to the nearest upright angle
		if ( _unfold < 0.02f ) _spin = ( _spin + Speed * dt / MathF.Max( 1f, _wheelRadius ) * 57.2958f ) % 360f;
		else _spin = _spin.Approach( MathF.Round( _spin / 360f ) * 360f, dt * 540f );

		PlaceBody();
		PoseBones();
	}

	float Stage( int stage )
	{
		// staged unfold: 0 shell/spine over the first 55 %, 1 legs 25-80 %, 2 arms/head 55-100 %
		var (a, b) = stage switch { 0 => (0f, 0.55f), 1 => (0.25f, 0.8f), _ => (0.55f, 1f) };
		var t = Math.Clamp( ( _unfold - a ) / ( b - a ), 0f, 1f );
		return t * t * ( 3f - 2f * t );
	}

	void PlaceBody()
	{
		// facing +X; while rolled, turning about the wheel's axle through its centre
		var spin = Rotation.FromAxis( _wheelAxis, -_spin );
		var rolledAmount = 1f - Stage( 0 );
		var pivot = _wheelCentre;
		var spunRot = _face * spin;
		var pos = _face * ( pivot - spin * pivot );
		// a rolled wheel sits on its rim: lift so the rim touches the ground
		var lift = Vector3.Up * MathF.Max( 0f, _wheelRadius - ( _face * _wheelCentre ).z ) * rolledAmount;
		_bodyGo.LocalRotation = spunRot;
		_bodyGo.LocalPosition = pos + lift;
	}

	void PoseBones()
	{
		var bodyTx = _bodyGo.WorldTransform;
		var stageT = new[] { Stage( 0 ), Stage( 1 ), Stage( 2 ) };
		var breath = MathF.Sin( Time.Now * 1.7f ) * 1.2f * _unfold;

		// aim in model axes: yaw and pitch for the head and arms
		var aimPitch = 0f;
		if ( AimAt != Vector3.Zero && _unfold > 0.6f )
		{
			var from = WorldPosition + Vector3.Up * 35f;
			var d = AimAt - from;
			aimPitch = Math.Clamp( MathF.Atan2( d.z, d.WithZ( 0 ).Length ) * 57.2958f, -35f, 35f );
		}
		var side = _c * new Vector3( 1, 0, 0 );      // model left-right axis (Blender X)

		var finalRot = new Rotation[_bones.Count];
		var finalPos = new Vector3[_bones.Count];
		for ( var i = 0; i < _bones.Count; i++ )
		{
			var b = _bones[i];
			var own = Rotation.Slerp( b.RolledOwn, Rotation.Identity, stageT[b.Stage] );
			var name = b.Go.Name;
			var extra = Rotation.Identity;
			if ( name == "Spine3" ) extra = Rotation.FromAxis( side, breath );
			if ( name == "Neck" ) extra = Rotation.FromAxis( side, -aimPitch * 0.6f * stageT[2] );
			if ( name == "LeftArm" ) extra = Rotation.FromAxis( side, ( -aimPitch * 0.8f - _kickL * 9f ) * stageT[2] );
			if ( name == "RightArm" ) extra = Rotation.FromAxis( side, ( -aimPitch * 0.8f - _kickR * 9f ) * stageT[2] );
			if ( name == "LeftForeArm" ) extra = Rotation.FromAxis( side, _kickL * 14f * stageT[2] );
			if ( name == "RightForeArm" ) extra = Rotation.FromAxis( side, _kickR * 14f * stageT[2] );
			// dying: the spine sags forward, the head drops, the legs buckle outwards
			if ( _dead > 0f )
			{
				if ( name == "Spine1" || name == "Spine3" ) extra *= Rotation.FromAxis( side, 18f * _dead );
				if ( name == "Neck" ) extra *= Rotation.FromAxis( side, 35f * _dead );
				if ( name.EndsWith( "UpLeg" ) ) extra *= Rotation.FromAxis( _c * new Vector3( 0, 1, 0 ), ( name.StartsWith( "Left" ) ? -22f : name.StartsWith( "Right" ) ? 22f : 0f ) * _dead );
			}

			var parentRot = b.Parent >= 0 ? finalRot[b.Parent] : Rotation.Identity;
			var delta = parentRot * own * extra;
			finalRot[i] = delta;
			finalPos[i] = b.Parent >= 0
				? finalPos[b.Parent] + parentRot * ( b.RestPos - _bones[b.Parent].RestPos )
				: b.RestPos;
			if ( b.Go.IsValid() ) b.Go.WorldTransform = bodyTx.ToWorld( new Transform( finalPos[i], delta * b.RestRot ) );
		}
		// the body sinks as it dies
		if ( _dead > 0f ) _bodyGo.LocalPosition += Vector3.Down * 10f * _dead;
	}

	[ConCmd( "swrp_droideka_info", Help = "Dev: print each droideka body's measured scale and state" )]
	public static void InfoCommand()
	{
		foreach ( var r in Game.ActiveScene.GetAllComponents<DroidekaRig>() )
			Log.Info( $"[SWRP] droideka {r.GameObject.Name}: ready {r._ready}, units per BF2 inch {r._k:0.###}, face {r._face.Angles()}, c {r._c.Angles()}, unfold {r._unfold:0.##}, wheel r {r._wheelRadius:0.#}, bounds {r._body?.Bounds.Size}" );
	}

	/// <summary>
	/// Death (Angelo, 2026-10-13): it blows apart in a blast, and what's left is a charred, grey wreck (BF2's own destroyed
	/// droideka texture, darkened; the eye goes out) that keeps smoking and sparking until it's cleared away.
	/// </summary>
	void Burn()
	{
		_burnt = true;
		_sinceSmoke = 0;
		var centre = Centre;
		SwrpFx.Explosion( Scene, centre, WorldPosition, Vector3.Up, 140f );
		if ( Material.Load( "models/droids/bf2tex/bf2_droideka_destroyed.vmat" ) is { } burnt ) _body.MaterialOverride = burnt;
		_body.Tint = new Color( 0.33f, 0.31f, 0.3f );
		Smoke( centre );
	}

	void Smoulder()
	{
		if ( _sinceSmoke > 4.5f ) { _sinceSmoke = 0; Smoke( Centre ); }
		if ( Game.Random.Float() < Time.Delta * 1.5f )
			SparkFx.Spawn( Scene, Centre + Vector3.Random * 14f, Vector3.Random * 160f + Vector3.Up * 90f, new Color( 1f, 0.7f, 0.35f ) );
	}

	void Smoke( Vector3 at )
	{
		if ( !SwrpFx.Near( Scene, at, 4000f ) ) return;
		var go = Scene.CreateObject();
		go.Name = "droideka_smoke";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = at + Vector3.Down * 10f;
		go.Components.Create<BlastSmoke>();
	}

	protected override void OnDestroy()
	{
		if ( _bodyGo.IsValid() ) _bodyGo.Destroy();
	}
}
