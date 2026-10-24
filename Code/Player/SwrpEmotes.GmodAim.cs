using System;
using System.Text.Json;

namespace Swrp;

/// <summary>
/// Garry's Mod's layers on the GMod animation sets (Angelo, 2026-10-19: "identical" to GMod and TFA). The aim layer: GMod
/// bends the spine and arms towards where you look with nine delta poses per hold (up/mid/down by right/centre/left),
/// blended by aim pitch and yaw. The gestures: the fire kick (range_ar2, range_pistol) and the reload (reload_ar2,
/// reload_pistol, the left hand to the cell). ModelSource/gc_droid_anims.py ("-- aim") bakes both from m_anm into
/// Assets/data/gmod_aim.json as each bone's rotation change in model space over the hold's idle; this blends them the same
/// way and turns the bones by them every frame.
/// </summary>
public static partial class SwrpEmotes
{
	/// <summary> Bodies on a GMod set with an aim layer: the hold ("ar2" or "pistol"), set by whoever drives the body. </summary>
	public static readonly Dictionary<SkinnedModelRenderer, string> GmodAim = new();

	sealed class AimLayer { public float[] Yaw; public float[] Pitch; public string[] Bones; public List<Dictionary<string, Rotation>> Poses; }
	sealed class Gesture { public float Fps; public float[] Ramp; public string[] Bones; public List<Dictionary<string, Rotation>> Frames; }

	static Dictionary<string, AimLayer> _aimLayers;
	static Dictionary<string, Gesture> _gmodGestures;
	static readonly Dictionary<SkinnedModelRenderer, (Gesture G, RealTimeSince T)> _playing = new();

	/// <summary> Dev (code_run_static_method): re-read data/gmod_aim.json after the pipeline re-bakes it. </summary>
	public static void ReloadGmodLayers() { _aimLayers = null; _gmodGestures = null; }

	static void LoadGmodLayers()
	{
		if ( _aimLayers is not null && _gmodGestures is not null ) return;      // (both: a hotload keeps one and not the other)
		_aimLayers = new();
		_gmodGestures = new();
		try
		{
			using var doc = JsonDocument.Parse( SwrpDataFile.ReadText( "data/gmod_aim.json" ) );
			static Dictionary<string, Rotation> Pose( JsonElement e )
			{
				var d = new Dictionary<string, Rotation>();
				foreach ( var p in e.EnumerateObject() )
					d[p.Name] = new Rotation( p.Value[0].GetSingle(), p.Value[1].GetSingle(), p.Value[2].GetSingle(), p.Value[3].GetSingle() );
				return d;
			}
			static string[] Names( JsonElement e ) => e.EnumerateArray().Select( x => x.GetString() ).ToArray();
			foreach ( var hold in doc.RootElement.EnumerateObject() )
			{
				if ( hold.Name == "_gestures" )
				{
					foreach ( var g in hold.Value.EnumerateObject() )
						_gmodGestures[g.Name] = new Gesture
						{
							Fps = g.Value.GetProperty( "fps" ).GetSingle(),
							Ramp = g.Value.GetProperty( "ramp" ).ValueKind == JsonValueKind.Array ? g.Value.GetProperty( "ramp" ).EnumerateArray().Select( x => x.GetSingle() ).ToArray() : null,
							Bones = Names( g.Value.GetProperty( "bones" ) ),
							Frames = g.Value.GetProperty( "frames" ).EnumerateArray().Select( Pose ).ToList(),
						};
					continue;
				}
				_aimLayers[hold.Name] = new AimLayer
				{
					Yaw = hold.Value.GetProperty( "yaw" ).EnumerateArray().Select( x => x.GetSingle() ).ToArray(),
					Pitch = hold.Value.GetProperty( "pitch" ).EnumerateArray().Select( x => x.GetSingle() ).ToArray(),
					Bones = Names( hold.Value.GetProperty( "bones" ) ),
					Poses = hold.Value.GetProperty( "poses" ).EnumerateArray().Select( Pose ).ToList(),
				};
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] no GMod layers: {e.Message}" );
		}
	}

	/// <summary> Every machine: play a GMod gesture on a body ("range_ar2", "reload_pistol", ...). </summary>
	public static void GmodGesture( SkinnedModelRenderer r, string name )
	{
		LoadGmodLayers();
		if ( !r.IsValid() || !_gmodGestures.TryGetValue( name, out var g ) || g.Frames.Count == 0 )
		{
			if ( GripDebug ) Log.Info( $"[SWRP] gmod gesture {name}: renderer {r.IsValid()}, loaded {string.Join( ",", _gmodGestures.Keys )}" );
			return;
		}
		if ( _playing.Count > 128 )
			foreach ( var gone in _playing.Keys.Where( k => !k.IsValid() ).ToList() ) _playing.Remove( gone );
		_playing[r] = (g, 0);
	}

	/// <summary> Degrees the passive (lowered-rifle) set's chest is brought forward (swrp_passive_lean). </summary>
	[ConVar( "swrp_passive_lean", Help = "Lowered-rifle stance: degrees the chest leans forward over GMod's idle (it leans back)", Saved = true )]
	public static float PassiveLean { get; set; } = 7f;
	static string[] LeanBones => new[] { "spine_0", "spine_1", "spine_2", "neck_0", "clavicle_L", "clavicle_R" };

	/// <summary> Every machine: stop whatever gesture a body is playing (an NPC's ambient idle when danger comes). </summary>
	public static void StopGmodGesture( SkinnedModelRenderer r ) => _playing.Remove( r );

	/// <summary> Dev: a gesture is playing on the body. </summary>
	public static bool PlayingGesture( SkinnedModelRenderer r ) => _playing.ContainsKey( r );

	/// <summary> The GMod aim layer and any playing gesture on a body. False if neither applies. </summary>
	static bool ApplyGmodAim( SkinnedModelRenderer r )
	{
		LoadGmodLayers();
		var delta = new Dictionary<string, Rotation>();

		// the aim layer: bilinear over the 3x3 grid, columns right/centre/left (aim_yaw low to high), rows up/middle/down
		if ( GmodAim.TryGetValue( r, out var hold ) && _aimLayers.TryGetValue( hold, out var layer ) && layer.Poses.Count == 9
			&& AimDir.TryGetValue( r, out var aim ) && aim.LengthSquared > 0.01f )
		{
			var look = Rotation.LookAt( aim.Normal ).Angles();
			var yaw = Angles.NormalizeAngle( look.yaw - r.WorldRotation.Yaw() );      // left positive, as GMod's aim_yaw
			var u = Math.Clamp( ( yaw - layer.Yaw[0] ) / ( layer.Yaw[1] - layer.Yaw[0] ), 0f, 1f ) * 2f;
			var v = Math.Clamp( ( look.pitch - layer.Pitch[0] ) / ( layer.Pitch[1] - layer.Pitch[0] ), 0f, 1f ) * 2f;     // down positive
			int c0 = Math.Min( (int)u, 1 ), r0 = Math.Min( (int)v, 1 );
			float fu = u - c0, fv = v - r0;
			foreach ( var name in layer.Bones )
			{
				Rotation At( int row, int col ) => layer.Poses[row * 3 + col][name];
				delta[name] = Rotation.Slerp( Rotation.Slerp( At( r0, c0 ), At( r0, c0 + 1 ), fu ), Rotation.Slerp( At( r0 + 1, c0 ), At( r0 + 1, c0 + 1 ), fu ), fv );
			}
		}

		// GMod's lowered-rifle idle stands leaning back about 7 degrees, head behind the hips (Angelo, 2026-10-21: "They're
		// leaning slightly back"): bring the chest up over the hips; the arms and head keep their own angles and ride along
		if ( r.Model?.ResourcePath?.Contains( "_gmod_passive" ) == true && PassiveLean != 0f )
		{
			var lean = Rotation.FromAxis( Vector3.Left, PassiveLean );
			foreach ( var name in LeanBones ) delta[name] = lean * ( delta.TryGetValue( name, out var had ) ? had : Rotation.Identity );
		}

		// a gesture on top: its frame at this time, faded in and out (the reload's own ramp, or 0.2 s as GMod's fades)
		if ( _playing.TryGetValue( r, out var play ) )
		{
			var g = play.G;
			var f = play.T * g.Fps;
			var last = g.Frames.Count - 1;
			if ( f >= last ) _playing.Remove( r );
			else
			{
				float w;
				if ( g.Ramp is { Length: 4 } k ) w = f < k[1] ? Math.Clamp( ( f - k[0] ) / Math.Max( 1f, k[1] - k[0] ), 0f, 1f ) : f > k[2] ? Math.Clamp( 1f - ( f - k[2] ) / Math.Max( 1f, k[3] - k[2] ), 0f, 1f ) : 1f;
				else w = 1f;
				int a = (int)f;
				var t = f - a;
				foreach ( var name in g.Bones )
				{
					var d = Rotation.Slerp( Rotation.Identity, Rotation.Slerp( g.Frames[a][name], g.Frames[Math.Min( a + 1, last )][name], t ), w );
					delta[name] = d * ( delta.TryGetValue( name, out var aimD ) ? aimD : Rotation.Identity );
				}
			}
		}

		var fading = _fades.TryGetValue( r, out var fade ) && fade.T < FadeTime;
		if ( !fading ) _fades.Remove( r );
		if ( delta.Count == 0 && !fading ) return false;
		if ( !r.CreateBoneObjects ) r.CreateBoneObjects = true;
		var body = r.WorldRotation;
		// parent first: each bone turned by its own whole change in the body's frame, from the animated pose; just after a
		// change of set, blended in from the pose it had (GMod blends its sequences over 0.3 s; a model swap would snap)
		var blend = fading ? fade.T / FadeTime : 1f;
		blend = blend * blend * ( 3f - 2f * blend );
		foreach ( var name in GmodBoneOrder )
		{
			var go = r.GetBoneObject( name );
			if ( !go.IsValid() || !RawBone( r, name, out var anim ) ) continue;
			var now = delta.TryGetValue( name, out var d ) ? body * d * body.Inverse * anim.Rotation : anim.Rotation;
			if ( fading && fade.Pose.TryGetValue( name, out var was ) ) now = Rotation.Slerp( body * was, now, blend );
			else if ( !delta.ContainsKey( name ) ) { Free( go ); continue; }      // (a fade just ended: hand the bone back)
			Take( go ).WorldRotation = now;
			// and where the animation has it from its parent: a taken bone keeps the offset it had when taken, and a kneeling
			// trooper's spine stretched to three or four times its length ("long torsos", Angelo 2026-10-21)
			if ( r.Model.Bones.GetBone( name )?.Parent is { } par && r.GetBoneObject( par ) is { } pgo && pgo.IsValid() && RawBone( r, par.Name, out var pa ) )
				go.WorldPosition = pgo.WorldTransform.PointToWorld( pa.PointToLocal( anim.Position ) );      // (from the parent as drawn: the animation's own world position lags a moving or falling body, and the spine stretched by the distance)
		}
		AimGunAtTarget( r );
		return true;
	}


	/// <summary> Per body: the held gun's muzzle in the gun's (hold_R's) frame (WeaponStats.MuzzleLocal), set by the player. </summary>
	public static Dictionary<SkinnedModelRenderer, Vector3> MuzzleOf => _muzzleOf ??= new();      // (lazy: a hotload can leave a new static null)
	static Dictionary<SkinnedModelRenderer, Vector3> _muzzleOf;

	/// <summary>
	/// The raised gun onto the crosshair: GMod's clips hold it a little low and across the body, so in third person the barrel
	/// missed what the trooper aims at (Angelo, 2026-10-19). The right hand turns at the wrist until the barrel, from the muzzle,
	/// points at the aim point (or along the aim), at most 45 degrees; the left hand's foregrip IK follows the gun. Aimed from the
	/// muzzle, not the grip (the grip line left 2-4 degrees at 30 degrees up or down, SwrpAimSweep); twice, as turning moves it.
	/// A point at or behind the muzzle (crouched, looking at your own feet) aims along the view instead.
	/// </summary>
	/// <summary> Dev: the wrist turn the last AimGunAtTarget wanted (degrees, before the limit). </summary>
	public static float LastAimTurn { get; set; }

	static void AimGunAtTarget( SkinnedModelRenderer r )
	{
		if ( !GmodAim.ContainsKey( r ) || r.GetBoneObject( "hand_R" ) is not { } hand || !hand.IsValid() ) return;
		if ( !RawBone( r, "hand_R", out var hb ) || !RawBone( r, "hold_R", out var hh ) ) return;
		var handToHold = hb.ToLocal( hh );
		var muzzle = MuzzleOf.TryGetValue( r, out var m ) ? m : new Vector3( 18f, 0f, 0f );
		var hasPoint = AimPoint.TryGetValue( r, out var target );
		var dir = AimDir.TryGetValue( r, out var d ) && d.LengthSquared > 0.01f ? d.Normal : Vector3.Zero;
		// the turn the gun needs, pivoting at the wrist (twice: turning moves the muzzle)
		Rotation Needed()
		{
			var total = Rotation.Identity;
			for ( var i = 0; i < 4; i++ )
			{
				var hold = new Transform( hand.WorldPosition, total * hand.WorldRotation ).ToWorld( handToHold );
				var from = hold.PointToWorld( muzzle );
				Vector3 want;
				if ( hasPoint && Vector3.Dot( target - from, hold.Rotation.Forward ) > 40f ) want = ( target - from ).Normal;
				else if ( dir != Vector3.Zero ) want = dir;
				else return Rotation.Identity;
				total = Rotation.FromToRotation( hold.Rotation.Forward, want ) * total;
			}
			return total;
		}
		var need = Needed();
		LastAimTurn = need.Angle();
		// half of a big turn from the shoulder, the whole arm with the gun, so a steep aim up or down doesn't bend the wrist
		// alone (it reached 64 degrees aiming 60 up, SwrpAimSweep); the rest at the wrist, at most 45 degrees
		if ( LastAimTurn > 15f && r.GetBoneObject( "arm_upper_R" ) is { } upper && upper.IsValid() )
		{
			Take( upper ).WorldRotation = Rotation.Slerp( Rotation.Identity, need, 0.5f ) * upper.WorldRotation;
			need = Needed();
		}
		var deg = need.Angle();
		if ( deg > 45f ) need = Rotation.Slerp( Rotation.Identity, need, 45f / deg );
		Take( hand ).WorldRotation = need * hand.WorldRotation;
	}

	const float FadeTime = 0.3f;
	static readonly Dictionary<SkinnedModelRenderer, (Dictionary<string, Rotation> Pose, RealTimeSince T)> _fades = new();

	/// <summary> Every machine, just before a body changes animation set: keep its upper-body pose to blend from. </summary>
	// each GMod clip's own ground speed by direction (N NE E SE S SW W NW; ModelSource reports, the same for every hold)
	static readonly float[] WalkGs = { 74.3f, 64.3f, 48.2f, 49.7f, 59.6f, 49.7f, 48.2f, 64.3f };
	static readonly float[] RunGs = { 194.8f, 194.8f, 139.1f, 139.1f, 157.7f, 139.1f, 139.1f, 194.8f };
	const float CrouchGs = 55.7f;

	/// <summary>
	/// GMod's own pace on a GMod set, after the move parameters are set (GM:UpdateAnimation / CalcMainActivity): the walk clips
	/// under 150 u/s and the run ones above, played at speed / that clip's ground speed, at most twice. The graph is fed the
	/// clip's own speed so it plays it at 1 and the rate goes on PlaybackRate (its own speed matching played runs ~20% slow
	/// and the planted foot slid, measured 2026-10-19).
	/// </summary>
	static readonly Dictionary<SkinnedModelRenderer, (float X, float Y, float Wx, float Wy)> _paced = new();
	static Dictionary<SkinnedModelRenderer, (bool Run, float Rate, float Fed)> _paceState = new();
	static Dictionary<SkinnedModelRenderer, (float Target, float Now)> _dirState;      // (not readonly: a hotload can leave a new static null)

	/// <summary> Dev (code_run_static_method set_PaceOn): GmodPace off, to compare with the graph's own pace. </summary>
	public static bool PaceOn { get; set; } = true;


	public static void GmodPace( SkinnedModelRenderer r )
	{
		if ( !IsGmodSet( r ) || !PaceOn ) return;
		_paceState ??= new();
		// GMod's aim layer turns the body and arms (ApplyGmodAim): the graph's own body aim off, and its head look light, so the
		// clip's head (tilted onto the sights) stays (the full head look put it ~30 degrees off GMod's, anim_score.py)
		var aimed = GmodAim.ContainsKey( r );
		r.Set( "aim_body_weight", aimed ? 0f : 1f );
		r.Set( "aim_head_weight", aimed ? 0f : 1f );
		var x = r.Parameters.GetFloat( "move_x" );
		var y = r.Parameters.GetFloat( "move_y" );
		// a frame where nobody set the move parameters again still holds our scaled values: use the real ones we scaled from,
		// or the scaling compounds and the rate jumps between frames
		if ( _paced.TryGetValue( r, out var was ) && MathF.Abs( x - was.Wx ) < 1e-3f && MathF.Abs( y - was.Wy ) < 1e-3f ) { x = was.X; y = was.Y; }
		var v = MathF.Sqrt( x * x + y * y );
		if ( v < 0.5f ) { r.PlaybackRate = 1f; _paced.Remove( r ); _paceState.Remove( r ); _dirState?.Remove( r ); return; }
		var a = ( MathF.Atan2( -y, x ) * 180f / MathF.PI + 360f ) % 360f / 45f;      // 0 forward, 2 right, 4 back
		var i = (int)a % 8;
		var t = a - MathF.Floor( a );
		float Dir( float[] g ) => MathX.Lerp( g[i], g[( i + 1 ) % 8], t );
		var crouched = r.Parameters.GetFloat( "duck" ) > 0.3f;
		// walk or run at 115 u/s, between the Alt walk (75) and the jog (155) since the 2026-10-21 speeds (GMod's 150 sat right on
		// the new jog), with hysteresis so a speed hovering at the line doesn't swap clips every frame
		var st = _paceState.TryGetValue( r, out var s0 ) ? s0 : (Run: v > 115f, Rate: 1f, Fed: v);
		var run = st.Run ? v > 105f : v > 125f;
		var gs = crouched ? CrouchGs : run ? Dir( RunGs ) : Dir( WalkGs );
		// the graph's own idea of the walk and run clips' speed runs high of their measured travel: fed the travel it played them
		// 1.28x (walk) and 1.36x (run) too slow (scratchpad anim_score.py stride periods, 2026-10-19); crouch matched
		var k = gs * ( crouched ? 1f : run ? 1.36f : 1.28f ) / v;
		var rate = MathF.Min( v / gs, 2f );
		// starting and stopping (under ~60 u/s) eased back to the graph's own blend: fed the clip's full speed at a crawl, the
		// legs sat mid-stride and snapped in and out of the idle every frame as the speed passed 0.5 (Angelo, 2026-10-19:
		// "legs glitching in front of the torso, flickering")
		var w = Math.Clamp( ( v - 15f ) / 45f, 0f, 1f );
		k = MathX.Lerp( 1f, k, w );
		rate = MathX.Lerp( 1f, rate, w );
		// smoothed over ~0.1 s: the speed jitters frame to frame (turning, strafing, slopes) and the rate and the clip blend
		// jumped with it, 1x to 2x and back several times a second (recorded on Angelo's own trooper, 2026-10-19)
		var ease = 1f - MathF.Exp( -Time.Delta * 10f );
		var fed = MathX.Lerp( st.Fed, v * k, ease );
		rate = MathX.Lerp( st.Rate, rate, ease );
		if ( _paceState.Count > 256 ) _paceState.Clear();
		_paceState[r] = (run, rate, fed);
		k = fed / v;
		// the direction held on the nearest of GMod's 8 clips (8 degrees of hysteresis), turning to the next one over ~0.1 s: the
		// graph's blend of two neighbouring GMod clips (running 17 degrees off straight, say) jumped the planted foot ~15 units once
		// a stride; on one clip it doesn't (feet 1600 u/s peak blended vs 586 snapped, stand recordings 2026-10-20)
		_dirState ??= new();
		var raw = MathF.Atan2( -y, x ) * 180f / MathF.PI;
		var held = _dirState.TryGetValue( r, out var ds ) ? ds : (Target: MathF.Round( raw / 45f ) * 45f, Now: MathF.Round( raw / 45f ) * 45f);
		if ( MathF.Abs( MathX.DeltaDegrees( held.Target, raw ) ) > 22.5f + 8f ) held.Target = MathF.Round( raw / 45f ) * 45f;
		held.Now += MathX.DeltaDegrees( held.Now, held.Target ) * ( 1f - MathF.Exp( -Time.Delta * 25f ) );
		if ( _dirState.Count > 256 ) _dirState.Clear();
		_dirState[r] = held;
		var sa = held.Now * MathF.PI / 180f;
		var (fx, fy) = (MathF.Cos( sa ) * v, -MathF.Sin( sa ) * v);
		r.Set( "move_x", fx * k );
		r.Set( "move_y", fy * k );
		if ( w > 0f ) { r.Set( "wish_x", fx * k ); r.Set( "wish_y", fy * k ); }      // (stopping, the graph keeps its own wish: it reads it to settle)
		if ( _paced.Count > 256 ) _paced.Clear();
		_paced[r] = (x, y, fx * k, fy * k);
		r.Set( "move_groundspeed", v * k );
		r.Set( "move_speed", v * k );
		if ( w > 0f ) { r.Set( "wish_groundspeed", v * k ); r.Set( "wish_speed", v * k ); }
		r.PlaybackRate = rate;
	}

	/// <summary> TFA Star Wars pack hold types that aren't ar2 (rifles) or pistol (HoldType 1): its SWEP.HoldType per gun. </summary>
	static readonly Dictionary<string, string> TfaHolds = new() { ["dc15s"] = "smg1", ["dc19"] = "smg1", ["dc19le"] = "smg1", ["e5c"] = "shotgun" };

	/// <summary>
	/// The GMod animation set a gun is held in right now, by TFA's own rules (tfa_gun_base common/anims.lua ProcessHoldType):
	/// sprinting or on safety its SprintHoldTypes (rifles passive, pistols normal), crouched its CrouchHoldTypes (ar2/smg stay),
	/// down the sights its IronSightHoldTypes (ar2/smg rpg = shouldered, pistol revolver = two hands, shotgun ar2), reloading
	/// its own. Sets this build has no clips for fall back to ar2 / pistol.
	/// </summary>
	public static string TfaHold( string weaponId, int holdType, bool lowered, bool crouched, bool sights, bool reloading )
	{
		var base_ = weaponId is not null && TfaHolds.TryGetValue( weaponId, out var h ) ? h : holdType == 1 ? "pistol" : "ar2";
		if ( lowered ) return base_ == "pistol" ? "normal" : "passive";
		if ( reloading || crouched && base_ is "ar2" or "smg1" ) return base_;
		if ( sights ) return base_ switch { "pistol" => "revolver", "shotgun" => "ar2", _ => "rpg" };
		return base_;
	}

	/// <summary> The base hold (no sights or sprint), for the reload gesture: TFA's ReloadHoldTypes keep each gun's own. </summary>
	public static string TfaBaseHold( string weaponId, int holdType ) => TfaHold( weaponId, holdType, false, false, false, true );

	/// <summary> A GMod set whose clip holds a long gun in both hands (the left hand goes onto the real gun's foregrip). </summary>
	public static bool TwoHandGmodSet( SkinnedModelRenderer r ) => r?.Model?.ResourcePath is { } p && ( p.Contains( "_gmod_ar2" ) || p.Contains( "_gmod_smg1" ) || p.Contains( "_gmod_shotgun" ) || p.Contains( "_gmod_rpg" ) );

	public static void FadeFromHere( SkinnedModelRenderer r )
	{
		if ( !r.IsValid() || !r.CreateBoneObjects ) return;
		var pose = new Dictionary<string, Rotation>();
		foreach ( var name in GmodBoneOrder )
			if ( r.GetBoneObject( name ) is { } go && go.IsValid() ) pose[name] = r.WorldRotation.Inverse * go.WorldRotation;
		if ( _fades.Count > 128 )
			foreach ( var gone in _fades.Keys.Where( x => !x.IsValid() ).ToList() ) _fades.Remove( gone );
		_fades[r] = (pose, 0);
	}

	// the hips and legs too (parent first), so a change of set or a start or stop blends the legs as well: a set swap restarts
	// the animgraph, and the legs snapped to a fresh stride in one frame, "kicking ahead" (Angelo, 2026-10-20; kick.py)
	static readonly string[] GmodBoneOrder = { "pelvis", "leg_upper_L", "leg_lower_L", "ankle_L", "leg_upper_R", "leg_lower_R", "ankle_R", "spine_0", "spine_1", "spine_2", "neck_0", "clavicle_L", "arm_upper_L", "arm_lower_L", "hand_L",
		"clavicle_R", "arm_upper_R", "arm_lower_R", "hand_R" };

	static void ReleaseGmodAim( SkinnedModelRenderer r )
	{
		if ( !r.CreateBoneObjects ) return;
		foreach ( var name in GmodBoneOrder )
			Free( r.GetBoneObject( name ) );
	}
}
