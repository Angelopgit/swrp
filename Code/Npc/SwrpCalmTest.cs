using System;
using System.Text.Json;

namespace Swrp;

/// <summary>
/// Dev: the calm-down ladder's scripted test. A squad of five clones spawns and is left alone until all of them relax, then
/// idles for <c>idle</c> seconds (filmed from an off-screen camera to data/shots/calm_###.png, every half second), then B1s
/// appear in front of them. Writes data/anim_rec/calm_test.json: how long they took to relax, every ambient idle started and
/// how many of a squad started within 2 s of each other, the worst bone stretch seen while relaxed, and per trooper the time
/// from the ambush to its rifle coming up.
/// </summary>
public sealed class SwrpCalmTest : Component
{
	List<SwrpNpc> _squad;
	Vector3 _at, _fwd;
	float _idle;
	TimeSince _age;
	float? _relaxedAt, _ambushAt;
	readonly Dictionary<string, float> _snapped = new();
	float _worstStretch;
	int _stretchedFrames, _frames;
	string _worstBone;
	CameraComponent _cam;
	int _shot;
	TimeSince _sinceShot;

	static readonly string[] Chain = { "pelvis", "spine_0", "spine_1", "spine_2", "neck_0", "head", "clavicle_L", "arm_upper_L", "arm_lower_L", "hand_L", "clavicle_R", "arm_upper_R", "arm_lower_R", "hand_R" };

	/// <summary> Dev (code_run_static_method), host. </summary>
	public static void Run( float x, float y, float z, float yaw = 0f, float idle = 120f )
	{
		var scene = Game.ActiveScene;
		if ( !Networking.IsHost || scene is null ) return;
		SwrpNpc.AmbientLog.Clear();
		SwrpNpc.ReactionLog.Clear();
		var go = scene.CreateObject();
		go.Name = "Calm Test";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		var t = go.AddComponent<SwrpCalmTest>();
		t._at = new Vector3( x, y, z );
		t._fwd = Rotation.FromYaw( yaw ).Forward;
		t._idle = idle;
		t._squad = SwrpNpcDirector.Spawn( scene, NpcRegistry.Get( NpcRegistry.Clone ), t._at, yaw, 5 );
		t._age = 0;
		t._cam = go.AddComponent<CameraComponent>();
		t._cam.IsMainCamera = false; t._cam.Priority = -100; t._cam.FieldOfView = 50f; t._cam.ZNear = 1f;
	}

	protected override void OnUpdate()
	{
		var alive = _squad.Where( n => n.IsValid() && !n.IsDead ).ToList();
		if ( _relaxedAt is null && alive.Count > 0 && alive.All( n => n.Calm == SwrpNpc.CalmRelaxed ) ) _relaxedAt = _age;

		if ( _relaxedAt is { } r && _ambushAt is null )
		{
			// idling: worst bone stretch against the animation, and the film
			foreach ( var n in alive ) Stretch( n.GetComponent<SkinnedModelRenderer>() );
			if ( _sinceShot > 0.5f ) { _sinceShot = 0; Film( alive ); }
			if ( _age - r > _idle )
			{
				_ambushAt = _age;
				SwrpNpcDirector.Spawn( Scene, NpcRegistry.Get( NpcRegistry.B1 ), _at + _fwd * 900f, Rotation.LookAt( -_fwd ).Yaw(), 3 );
			}
		}

		if ( _ambushAt is { } a )
		{
			foreach ( var n in _squad.Where( n => n.IsValid() ) )
				if ( !_snapped.ContainsKey( n.DisplayName ) && n.GetComponent<SkinnedModelRenderer>()?.Model?.ResourcePath?.Contains( "_gmod_ar2" ) == true )
					_snapped[n.DisplayName] = _age - a;
			if ( _age - a < 15f && _snapped.Count < _squad.Count ) return;
			Report();
		}
		else if ( _age > _idle + 200f ) Report();
	}

	void Stretch( SkinnedModelRenderer r )
	{
		if ( r?.Model is null ) return;
		_frames++;
		var bad = false;
		for ( var i = 1; i < Chain.Length; i++ )
		{
			var b = r.Model.Bones.GetBone( Chain[i] );
			if ( b?.Parent is not { } p ) continue;
			if ( !r.TryGetBoneTransformAnimation( in p, out var pa ) || !r.TryGetBoneTransformAnimation( in b, out var ba ) ) continue;
			if ( !r.TryGetBoneTransform( in p, out var pf ) || !r.TryGetBoneTransform( in b, out var bf ) ) continue;
			var d = MathF.Abs( pa.Position.Distance( ba.Position ) - pf.Position.Distance( bf.Position ) );
			if ( d > 1f ) { bad = true; Log.Info( $"[SWRP] calm test stretch: {r.GameObject.Name} {Chain[i]} {d:0.0} gesture {SwrpEmotes.PlayingGesture( r )} speed {r.GetFloat( "move_groundspeed" ):0} model {r.Model.ResourcePath.Split( '/' ).Last()} t {(float)_age:0.00}" ); }
			if ( d > _worstStretch ) { _worstStretch = d; _worstBone = Chain[i]; }
		}
		if ( bad ) _stretchedFrames++;
	}

	void Film( List<SwrpNpc> alive )
	{
		if ( alive.Count == 0 ) return;
		var c = alive.Aggregate( Vector3.Zero, ( s, n ) => s + n.WorldPosition ) / alive.Count;
		_cam.WorldPosition = c + _fwd * 240f + Rotation.FromYaw( 90 ) * _fwd * 90f + Vector3.Up * 70f;
		_cam.WorldRotation = Rotation.LookAt( c + Vector3.Up * 40f - _cam.WorldPosition );
		var bmp = new Bitmap( 640, 400 );
		_cam.RenderToBitmap( bmp );
		FileSystem.Data.CreateDirectory( "shots" );
		FileSystem.Data.WriteAllBytes( $"shots/calm_{_shot++:000}.png", bmp.ToPng() );
	}

	void Report()
	{
		var log = SwrpNpc.AmbientLog.Where( e => _squad.Any( n => n.IsValid() && n.DisplayName == e.Npc ) ).ToList();
		var overlaps = 0;
		for ( var i = 0; i < log.Count; i++ )
			for ( var j = i + 1; j < log.Count; j++ )
				if ( log[i].Squad == log[j].Squad && MathF.Abs( log[i].T - log[j].T ) < 2f ) overlaps++;
		var report = new
		{
			relaxed_after = _relaxedAt,
			idle_seconds = _idle,
			idles = log.Select( e => new { t = MathF.Round( e.T - log.FirstOrDefault().T, 2 ), npc = e.Npc, idle = e.Name } ),
			idles_count = log.Count,
			overlaps_within_2s = overlaps,
			worst_stretch = _worstStretch,
			worst_bone = _worstBone,
			stretched_body_frames = $"{_stretchedFrames} of {_frames}",
			reaction_from_cue = SwrpNpc.ReactionLog.Where( c => _snapped.ContainsKey( c.Npc ) && c.Cue >= Time.Now - _age + ( _ambushAt ?? 0f ) ).GroupBy( c => c.Npc )
				.ToDictionary( g => g.Key, g => _snapped[g.Key] - ( g.First().Cue - ( Time.Now - _age + _ambushAt.Value ) ) ),
			reaction_seconds = _snapped,
			frames = _shot,
		};
		FileSystem.Data.CreateDirectory( "anim_rec" );
		FileSystem.Data.WriteAllText( "anim_rec/calm_test.json", JsonSerializer.Serialize( report ) );
		Log.Info( $"[SWRP] calm test: {JsonSerializer.Serialize( report )}" );
		GameObject.Destroy();
	}
}
