using System;

namespace Swrp;

/// <summary>
/// The physgun and tool gun in your hands, the way Facepunch's Sandbox shows them (MIT, see LICENSE-facepunch-sandbox.txt): in first
/// person its viewmodel, the animated gun (facepunch.v_physgun / facepunch.v_toolgun, their own animgraphs) with Facepunch's human
/// arms bone-merged onto it (facepunch.v_first_person_arms_human), driven with the same parameters as Sandbox's ViewModel; in third
/// person and for everyone else the world model (facepunch.w_physgun / facepunch.wtoolgun) in the right hand with the citizen
/// hold pose Sandbox uses (holdtype Physgun = 8, the tool gun Pistol = 1). Models come from sbox.game at runtime (<c>Cloud.Load</c>).
/// The holder's machine says what's out (<see cref="Show"/>); the host passes it on to everyone.
/// </summary>
public static class SwrpHeldTools
{
	public const string PhysgunWorld = "facepunch.w_physgun";
	public const string ToolgunWorld = "facepunch.wtoolgun";
	public const string PhysgunView = "facepunch.v_physgun";
	public const string ToolgunView = "facepunch.v_toolgun";
	public const string Arms = "facepunch.v_first_person_arms_human";

	/// <summary> Every machine: what each connection holds (1 physgun, 2 tool gun). </summary>
	public static readonly Dictionary<Guid, int> Held = new();

	static readonly Dictionary<string, Model> _models = new();
	static readonly HashSet<string> _loading = new();

	/// <summary> A model from sbox.game, or null while it's still downloading. </summary>
	public static Model Get( string ident )
	{
		if ( ident is null ) return null;
		if ( _models.TryGetValue( ident, out var m ) ) return m;
		if ( _loading.Add( ident ) ) Load( ident );
		return null;
	}

	public static Model ModelFor( int kind ) => Get( kind == 1 ? PhysgunWorld : kind == 2 ? ToolgunWorld : null );
	public static Model ViewFor( int kind ) => Get( kind == 1 ? PhysgunView : kind == 2 ? ToolgunView : null );

	static async void Load( string ident )
	{
		try
		{
			var m = await Cloud.Load<Model>( ident );
			if ( m is not null && !m.IsError ) _models[ident] = m;
			else Log.Warning( $"[BUILD] couldn't load {ident} from sbox.game" );
		}
		catch ( Exception e ) { Log.Warning( $"[BUILD] couldn't load {ident}: {e.Message}" ); }
	}

	static int _sent = -1;

	/// <summary> Client, every frame: tell everyone what's in my hands when it changes. </summary>
	public static void Tick( int kind )
	{
		ModelFor( kind );
		ViewFor( kind );
		if ( kind != 0 ) Get( Arms );
		if ( kind == _sent ) return;
		_sent = kind;
		Show( kind );
	}

	[Rpc.Host]
	static void Show( int kind )
	{
		var caller = Rpc.Caller;
		if ( caller is null ) return;
		if ( kind != 0 && !SwrpBuild.May( caller ) ) kind = 0;
		Relay( caller.Id, Math.Clamp( kind, 0, 2 ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Relay( Guid owner, int kind )
	{
		if ( kind == 0 ) Held.Remove( owner );
		else Held[owner] = kind;
	}

	static RealTimeSince _sinceResend;

	/// <summary> Host: now and then send everyone the whole list again, for players who joined since. </summary>
	internal static void HostResend()
	{
		if ( !Networking.IsHost || _sinceResend < 5f ) return;
		_sinceResend = 0;
		foreach ( var (owner, kind) in Held.ToArray() )
		{
			if ( Connection.All.All( c => c.Id != owner ) ) { Relay( owner, 0 ); continue; }
			Relay( owner, kind );
		}
	}
}

/// <summary>
/// Draws the held tools: the local viewmodel in first person, world models and hold poses for everyone else; gives the beam its
/// muzzle. Ticked by <see cref="SwrpPhysgunSystem"/> (a new GameObjectSystem only starts with a scene load, not on hotload).
/// </summary>
public static class SwrpHeldToolSystem
{
	static readonly Dictionary<Guid, ModelRenderer> _shown = new();

	// the local viewmodel
	static SkinnedModelRenderer _view, _arms;
	static int _viewKind;
	static bool _attack;

	/// <summary> Local: the tool gun fired (plays the viewmodel's attack). </summary>
	public static void Fire() => _attack = true;

	/// <summary> Where a holder's beam starts: the gun's muzzle (the viewmodel's for yourself in first person), otherwise null. </summary>
	public static Vector3? Muzzle( Guid owner )
	{
		if ( owner == Connection.Local?.Id && _view.IsValid() && _view.GameObject.Enabled )
		{
			if ( _view.GetAttachment( "muzzle" ) is { } vm ) return vm.Position;
			return _view.GameObject.WorldTransform.PointToWorld( new Vector3( 30f, -6f, -6f ) );
		}
		if ( !_shown.TryGetValue( owner, out var r ) || !r.IsValid() || !r.GameObject.Enabled ) return null;
		if ( r.Model?.GetAttachment( "muzzle" ) is { } a ) return r.GameObject.WorldTransform.ToWorld( a ).Position;
		return r.GameObject.WorldTransform.PointToWorld( Vector3.Forward * ( r.Model?.Bounds.Maxs.x ?? 20f ) );
	}

	internal static void Draw( Scene scene )
	{
		if ( scene is null || scene != Game.ActiveScene ) return;
		SwrpHeldTools.HostResend();

		foreach ( var owner in _shown.Keys.ToArray() )
		{
			if ( SwrpHeldTools.Held.ContainsKey( owner ) && _shown[owner].IsValid() ) continue;
			_shown[owner]?.GameObject?.Destroy();
			_shown.Remove( owner );
		}

		var cam = scene.Camera;
		var viewShown = false;

		foreach ( var (owner, kind) in SwrpHeldTools.Held )
		{
			var p = SwrpPlayers.Valid.FirstOrDefault( x => x.Network.Owner?.Id == owner );
			var model = SwrpHeldTools.ModelFor( kind );
			if ( !_shown.TryGetValue( owner, out var r ) || !r.IsValid() )
			{
				var go = scene.CreateObject();
				go.Name = "Held build tool";
				go.NetworkMode = NetworkMode.Never;
				go.Flags |= GameObjectFlags.NotSaved;
				r = go.Components.Create<ModelRenderer>();
				_shown[owner] = r;
			}

			var visible = p is not null && p.CharacterId is not null && !p.InVehicle
				&& p.GetComponent<SwrpVitals>() is not ( { IsDead: true } or { IsDowned: true } ) && !( p.IsProxy && p.Invisible );
			var firstPerson = visible && !p.IsProxy && cam.IsValid() && p.Controller.IsValid() && !p.Controller.ThirdPerson && SwrpUiState.SpectateSteamId == 0;

			// the hold pose: after every component's update (SwrpBlaster sets holdtype in its own), before the body animates
			if ( visible && p.Body.IsValid() ) p.Body.Set( "holdtype", kind == 1 ? 8 : 1 );

			if ( firstPerson && VisorOverlay.HudHidden ) { r.GameObject.Enabled = false; continue; }      // screenshot: no viewmodel
			if ( firstPerson && DrawView( scene, p, kind, cam ) )
			{
				viewShown = true;
				r.GameObject.Enabled = false;
				continue;
			}

			r.GameObject.Enabled = visible && model is not null && !firstPerson;
			if ( !r.GameObject.Enabled ) continue;
			if ( r.Model != model ) r.Model = model;

			var body = p.Body;
			if ( body.IsValid() && body.GetAttachment( "hold_R" ) is { } hold )
			{
				r.GameObject.WorldPosition = hold.Position;
				r.GameObject.WorldRotation = hold.Rotation;
				r.GameObject.WorldScale = body.WorldScale.z;
			}
			else
			{
				var aim = p.Controller.IsValid() ? p.Controller.EyeAngles.ToRotation() : p.WorldRotation;
				r.GameObject.WorldPosition = p.WorldPosition + Vector3.Up * 44f + aim * new Vector3( 14f, -8f, 0f );
				r.GameObject.WorldRotation = aim;
			}
			r.RenderType = ModelRenderer.ShadowRenderType.On;
		}

		if ( !viewShown && _view.IsValid() ) _view.GameObject.Enabled = false;
	}

	/// <summary> The local first-person viewmodel (Sandbox's ViewModel.Place and UpdateAnimation). False while it's still loading. </summary>
	static bool DrawView( Scene scene, SwrpPlayer p, int kind, CameraComponent cam )
	{
		var gun = SwrpHeldTools.ViewFor( kind );
		var arms = SwrpHeldTools.Get( SwrpHeldTools.Arms );
		if ( gun is null ) return false;

		if ( !_view.IsValid() )
		{
			var go = scene.CreateObject();
			go.Name = "Build viewmodel";
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			_view = go.Components.Create<SkinnedModelRenderer>();
			_view.RenderType = ModelRenderer.ShadowRenderType.Off;
			_view.UseAnimGraph = true;
			_viewKind = 0;
			var armsGo = new GameObject( go, true, "arms" );
			armsGo.NetworkMode = NetworkMode.Never;
			_arms = armsGo.Components.Create<SkinnedModelRenderer>();
			_arms.RenderType = ModelRenderer.ShadowRenderType.Off;
			_arms.BoneMergeTarget = _view;
		}

		if ( _viewKind != kind || _view.Model != gun )
		{
			_view.Model = gun;
			_viewKind = kind;
			_view.Set( "b_deploy", true );
		}
		if ( arms is not null && _arms.Model != arms ) _arms.Model = arms;

		_view.GameObject.Enabled = true;
		_view.GameObject.WorldPosition = cam.WorldPosition;
		_view.GameObject.WorldRotation = cam.WorldRotation;

		var c = p.Controller;
		var eye = c.EyeAngles;
		var velocity = c.Velocity;
		var eyeRotation = eye.ToRotation();
		var forward = eyeRotation.Forward.Dot( velocity );
		var sideward = eyeRotation.Right.Dot( velocity );
		_view.Set( "b_twohanded", true );
		_view.Set( "deploy_type", 1 );
		_view.Set( "b_grounded", c.IsOnGround );
		_view.Set( "move_bob", velocity.Length.Remap( 0, c.RunSpeed * 2f ) );
		_view.Set( "aim_pitch", eye.pitch );
		_view.Set( "aim_yaw", eye.yaw );
		_view.Set( "move_direction", MathF.Atan2( sideward, forward ).RadianToDegree().NormalizeDegrees() );
		_view.Set( "move_speed", velocity.Length );
		_view.Set( "move_groundspeed", velocity.WithZ( 0 ).Length );
		_view.Set( "move_y", sideward );
		_view.Set( "move_x", forward );
		_view.Set( "move_z", velocity.z );

		if ( kind == 1 )
		{
			// Physgun.UpdateViewModel: the claws open over something grabbable and fully while holding, the brake while busy
			var holding = SwrpBuild.Grabbing;
			_view.Set( "stylus", holding ? 1f : SwrpBuildTool.HoverGrabbable ? 0.5f : 0f );
			_view.Set( "b_button", SwrpBuildTool.Spinning );
			_view.Set( "brake", holding || SwrpBuildTool.Pulling ? 1 : 0 );
			_view.Set( "attack_hold", holding ? 1f : 0f );
			_view.Set( "b_attack", holding );
		}
		else if ( _attack )
		{
			_view.Set( "b_attack", true );
		}
		_attack = false;
		return true;
	}
}
