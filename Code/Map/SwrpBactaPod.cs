using System;

namespace Swrp;

/// <summary>
/// A bacta pod in the medbay. E on an empty pod: its glass sinks into the floor, you step in, the glass rises, bacta floods
/// in and you float in it while it heals you completely (and stops any bleeding), then it drains, the glass drops and you
/// step out in front of it. The host runs the cycle (it moves you in and out, holds you still, heals you); every machine
/// plays the same animation from the host's start signal. Placed by MEDBAY_PODS in MapSource/notorious_scene.py; models
/// LordTrilobite's bacta tank (ModelSource/medbay_tank.py) over the original pod from ModelSource/bacta_pod.py, sounds from SoundSource/ship_life.py.
/// </summary>
[Title( "SWRP Bacta Pod" ), Category( "SWRP" ), Icon( "medical_services" )]
public sealed class SwrpBactaPod : Component, ISwrpUsable
{
	/// <summary> Which way to look for the wall the pod's back stands against. </summary>
	[Property] public Vector3 Probe { get; set; } = Vector3.Forward;
	[Property] public string PodName { get; set; } = "Bacta pod";

	// the cycle, in seconds from E
	const float OpenEnd = 1.2f;          // glass down
	const float StepIn = 1.3f;           // you're moved in
	const float CloseStart = 1.6f, CloseEnd = 2.8f;
	const float FillStart = 3.0f, FillEnd = 5.2f;
	public const float SoakSeconds = 10f;
	const float DrainTime = 2.2f, GlassTime = 1.2f, OutTime = 1.2f;
	static float SoakEnd => FillEnd + SoakSeconds;
	static float DrainEnd => SoakEnd + DrainTime;
	static float OpenAgainEnd => DrainEnd + GlassTime;
	static float StepOutAt => OpenAgainEnd + 0.1f;
	static float CloseAgainEnd => StepOutAt + OutTime;
	public static float CycleSeconds => CloseAgainEnd;

	const float GlassDrop = 104f;        // the glass sinks this far into the floor to open

	[Sync( SyncFlags.FromHost )] public bool Busy { get; set; }
	[Sync( SyncFlags.FromHost )] public ulong OccupantSteam { get; set; }
	[Sync( SyncFlags.FromHost )] public string OccupantName { get; set; }

	/// <summary> How far under the bacta your own trooper is (0..1): the screen tints green and sound goes muffled. </summary>
	public static float LocalSubmerged { get; private set; }

	GameObject _frame, _glass, _liquid;
	// the tank's inside: floor height, how tall the bacta gets, how far the glass sinks to open (set by the model in use)
	float _floor = 12.5f, _fill = 98f, _drop = GlassDrop;
	PointLight _light;
	readonly List<(GameObject Go, SpriteRenderer R, float Phase, float Speed, Vector2 Offset)> _bubbles = new();
	static Sprite _bubbleSprite;
	Vector3 _origin, _front;
	bool _placed;

	float _t = -1f;
	int _beat;
	ulong _occupantSteam;
	SoundHandle _bubbleLoop;
	RealTimeSince _bubbleLoopAge;
	SwrpPlayer _shownOccupant;

	SwrpPlayer _hostOccupant;

	protected override void OnUpdate()
	{
		if ( !_placed )
		{
			_placed = true;
			Place();
		}
		if ( !_frame.IsValid() ) return;

		var dt = Time.Delta;
		if ( _t >= 0f )
		{
			_t += dt;
			if ( Networking.IsHost ) HostBeats();
			Beats();
			if ( _t >= CycleSeconds ) _t = -1f;
		}

		Animate();
	}

	void Place()
	{
		Vector3 n;
		if ( !SwrpLivingShip.FindWall( Scene, WorldPosition, Probe, out var wall, out n ) || MathF.Abs( n.z ) > 0.3f )
		{
			wall = WorldPosition;
			n = -Probe.Normal;
		}
		_front = n.WithZ( 0 ).Normal;
		var spot = wall + _front * 44f;
		_origin = spot.WithZ( SwrpLivingShip.FloorBelow( Scene, spot ) );

		_frame = SwrpLivingShip.Child( GameObject, "bacta_pod" );
		_frame.WorldPosition = _origin;
		_frame.WorldRotation = Rotation.LookAt( _front, Vector3.Up );
		// LordTrilobite's bacta tank (ModelSource/medbay_tank.py; Angelo 2026-10-13); the original pod is the fallback. The
		// original frame's box walls stay as the collision either way (the tank's open middle has none).
		var frameModel = Model.Load( "models/medbay/bacta_pod_frame.vmdl" );
		var tank = Model.Load( "models/medbay/tank_frame.vmdl" );
		var tankGlass = Model.Load( "models/medbay/tank_glass.vmdl" );
		var real = tank is { IsError: false } && tankGlass is { IsError: false };
		_frame.Components.Create<ModelRenderer>().Model = real ? tank : frameModel;
		var col = _frame.Components.Create<ModelCollider>();
		col.Model = frameModel;
		col.Static = true;
		if ( real ) { _floor = 31f; _fill = 81f; _drop = 86f; }

		_glass = SwrpLivingShip.Child( _frame, "glass" );
		_glass.Components.Create<ModelRenderer>().Model = real ? tankGlass : Model.Load( "models/medbay/bacta_pod_glass.vmdl" );

		_liquid = SwrpLivingShip.Child( _frame, "bacta" );
		_liquid.LocalPosition = Vector3.Up * _floor;
		_liquid.Components.Create<ModelRenderer>().Model = Model.Load( "models/medbay/bacta_pod_liquid.vmdl" );
		_liquid.Enabled = false;

		var lightGo = SwrpLivingShip.Child( _frame, "light" );
		lightGo.LocalPosition = Vector3.Up * 100f + Vector3.Forward * 10f;
		_light = lightGo.Components.Create<PointLight>();
		_light.LightColor = new Color( 0.35f, 1f, 0.6f ) * 0.8f;
		_light.Radius = 150f;
		_light.Shadows = false;

		_bubbleSprite ??= Sprite.FromTexture( Texture.Load( "materials/fx/bubble.png" ) );
		var rnd = new Random( (int)( MathF.Abs( _origin.x + _origin.y ) ) );
		for ( var i = 0; i < 14; i++ )
		{
			var go = SwrpLivingShip.Child( _frame, "bubble" );
			var r = go.Components.Create<SpriteRenderer>();
			r.Sprite = _bubbleSprite;
			r.Lighting = false;
			r.Shadows = false;
			r.Size = new Vector2( 1.2f, 1.2f );
			r.Color = new Color( 0.8f, 1f, 0.9f, 0.7f );
			go.Enabled = false;
			var a = (float)rnd.NextDouble() * MathF.PI * 2f;
			var d = (float)rnd.NextDouble() * 22f;
			_bubbles.Add( (go, r, (float)rnd.NextDouble(), 0.25f + (float)rnd.NextDouble() * 0.35f, new Vector2( MathF.Cos( a ) * d, MathF.Sin( a ) * d )) );
		}
	}

	// ----- the cycle ------------------------------------------------------------------------------------------------------

	public (string Title, string Sub, string Tone) Prompt( SwrpPlayer me )
	{
		if ( Busy ) return (PodName, OccupantSteam == me.OwnerSteamId ? "Healing..." : $"In use · {OccupantName}", "denied");
		return ($"Enter the {PodName.ToLowerInvariant()}", $"Heals you fully and stops bleeding · about {CycleSeconds:0} s", "ok");
	}

	public void Use( SwrpPlayer me ) => RequestUse();

	[Rpc.Host]
	void RequestUse()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !p.IsValid() || p.CharacterId is null || p.Jailed || p.InPod || !_frame.IsValid() ) return;
		if ( Busy ) { SwrpNet.NotifyConnection( caller, "That pod is in use.", true ); return; }
		if ( p.WorldPosition.Distance( _origin ) > 220f ) return;
		if ( p.GetComponent<SwrpVitals>() is { IsDead: true } or { IsDowned: true } ) return;

		Busy = true;
		OccupantSteam = p.OwnerSteamId;
		OccupantName = $"{p.Rank.Abbreviation} {p.CharacterName}";
		_hostOccupant = p;
		BeginCycle( p.OwnerSteamId );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void BeginCycle( ulong steam )
	{
		_occupantSteam = steam;
		_t = 0f;
		_beat = 0;
	}

	/// <summary> Host: move the trooper in and out, hold them, heal them. </summary>
	void HostBeats()
	{
		var p = _hostOccupant;
		var ok = p.IsValid() && p.GetComponent<SwrpVitals>() is { IsDead: false };
		if ( _t >= StepIn && _hostBeat == 0 )
		{
			_hostBeat = 1;
			if ( ok )
			{
				p.InPod = true;
				p.TeleportTo( _origin + Vector3.Up * ( _floor + 0.5f ), Rotation.LookAt( _front ).Yaw() );
			}
		}
		if ( _t >= FillEnd && _hostBeat == 1 )
		{
			_hostBeat = 2;
			if ( ok && p.GetComponent<SwrpVitals>() is { } v )
			{
				v.StopBleeding();
				v.HealOverTime( SwrpVitals.MaxHealth - v.Health + 1f, SoakSeconds * 0.85f );
			}
		}
		if ( _t >= StepOutAt && _hostBeat == 2 )
		{
			_hostBeat = 3;
			if ( p.IsValid() )
			{
				var outAt = _origin + _front * 72f;
				p.TeleportTo( outAt.WithZ( SwrpLivingShip.FloorBelow( Scene, outAt ) + 2f ), Rotation.LookAt( _front ).Yaw() );
				p.InPod = false;
			}
		}
		if ( _t >= CycleSeconds - 0.05f && _hostBeat == 3 )
		{
			_hostBeat = 0;
			Busy = false;
			OccupantSteam = 0;
			OccupantName = null;
			_hostOccupant = null;
		}
	}

	int _hostBeat;

	/// <summary> Every machine: the sounds at each step. </summary>
	void Beats()
	{
		void At( int beat, float time, string sound )
		{
			if ( _beat != beat || _t < time ) return;
			_beat++;
			if ( sound is not null ) Sound.Play( sound, _origin + Vector3.Up * 60f );
		}
		At( 0, 0f, "sounds/ambient/pod_hiss.sound" );
		At( 1, CloseStart, "sounds/ambient/pod_hiss.sound" );
		At( 2, FillStart, "sounds/ambient/pod_fill.sound" );
		At( 3, SoakEnd, "sounds/ambient/pod_drain.sound" );
		At( 4, DrainEnd, "sounds/ambient/pod_hiss.sound" );
		At( 5, StepOutAt + 0.1f, "sounds/ambient/pod_hiss.sound" );

		var soaking = _t > FillEnd - 0.5f && _t < SoakEnd;
		if ( soaking && _bubbleLoopAge > 3.95f )
		{
			_bubbleLoopAge = 0;
			_bubbleLoop = Sound.Play( "sounds/ambient/pod_bubbles.sound", _origin + Vector3.Up * 60f );
		}
		if ( !soaking && _bubbleLoop is not null )
		{
			_bubbleLoop.Stop( 0.5f );
			_bubbleLoop = null;
		}
	}

	/// <summary> Glass, bacta, bubbles, light and the floating trooper, from the local timeline. </summary>
	void Animate()
	{
		float open, level;
		if ( _t < 0f )
		{
			open = 0f;
			level = Busy ? 1f : 0f;          // (joined mid-cycle)
		}
		else
		{
			open = _t < OpenEnd ? _t / OpenEnd
				: _t < CloseStart ? 1f
				: _t < CloseEnd ? 1f - ( _t - CloseStart ) / ( CloseEnd - CloseStart )
				: _t < DrainEnd ? 0f
				: _t < OpenAgainEnd ? ( _t - DrainEnd ) / GlassTime
				: _t < StepOutAt + 0.1f ? 1f
				: 1f - Math.Clamp( ( _t - StepOutAt - 0.1f ) / ( OutTime - 0.1f ), 0f, 1f );
			level = _t < FillStart ? 0f
				: _t < FillEnd ? ( _t - FillStart ) / ( FillEnd - FillStart )
				: _t < SoakEnd ? 1f
				: _t < DrainEnd ? 1f - ( _t - SoakEnd ) / DrainTime
				: 0f;
		}
		open = open * open * ( 3f - 2f * open );
		_glass.LocalPosition = Vector3.Down * _drop * open;

		_liquid.Enabled = level > 0.01f;
		if ( _liquid.Enabled ) _liquid.LocalScale = new Vector3( 1f, 1f, MathF.Max( 0.01f, level * _fill ) );
		_light.LightColor = new Color( 0.35f, 1f, 0.6f ) * ( 0.8f + 1.4f * level + 0.15f * MathF.Sin( Time.Now * 2f ) * level );

		// bubbles rising through the bacta
		var top = _floor + level * _fill;
		for ( var i = 0; i < _bubbles.Count; i++ )
		{
			var (go, r, phase, speed, off) = _bubbles[i];
			var on = level > 0.25f && SwrpLivingShip.Near( Scene, _origin, 1500f );
			if ( go.Enabled != on ) go.Enabled = on;
			if ( !on ) continue;
			var k = ( Time.Now * speed + phase ) % 1f;
			go.LocalPosition = new Vector3( off.x + MathF.Sin( Time.Now * 3f + phase * 9f ) * 1.2f, off.y, _floor + 1.5f + k * ( top - _floor - 3.5f ) );
			var s = 0.8f + k * 1.4f;
			r.Size = new Vector2( s, s );
		}

		// the trooper floats while the bacta is up (and the local one sees it from inside)
		var occupant = _occupantSteam != 0 ? Scene.GetAllComponents<SwrpPlayer>().FirstOrDefault( p => p.OwnerSteamId == _occupantSteam ) : null;
		var floating = occupant.IsValid() && level > 0.6f;
		if ( _shownOccupant.IsValid() && ( !floating || _shownOccupant != occupant ) ) Float( _shownOccupant, false );
		if ( floating ) Float( occupant, true );
		_shownOccupant = floating ? occupant : null;
		if ( occupant.IsValid() && !occupant.IsProxy ) LocalSubmerged = Math.Clamp( ( level - 0.5f ) * 2f, 0f, 1f );
		if ( _t < 0f && _occupantSteam != 0 && SwrpPlayer.Local?.OwnerSteamId == _occupantSteam )
		{
			LocalSubmerged = 0f;
			_occupantSteam = 0;
		}
	}

	void Float( SwrpPlayer p, bool on )
	{
		var body = p.Body;
		if ( !body.IsValid() ) return;
		if ( !on )
		{
			body.LocalPosition = Vector3.Zero;
			body.Set( "b_swim", false );
			return;
		}
		body.LocalPosition = Vector3.Up * ( 7f + 2.5f * MathF.Sin( Time.Now * 1.3f ) );
		body.Set( "b_swim", true );
	}

	protected override void OnDestroy()
	{
		if ( _shownOccupant.IsValid() ) Float( _shownOccupant, false );
		_bubbleLoop?.Stop();
		LocalSubmerged = 0f;
	}

	[ConCmd( "swrp_pod_info", Help = "swrp_pod_info - list the bacta pods, where they stand and who is in them" )]
	public static void InfoCommand()
	{
		foreach ( var pod in Game.ActiveScene?.GetAllComponents<SwrpBactaPod>() ?? Enumerable.Empty<SwrpBactaPod>() )
			Log.Info( $"[SWRP] pod {pod.PodName} at {SwrpLivingShip.At( pod._origin )} facing {SwrpLivingShip.At( pod._front * 100f )}: {( pod.Busy ? $"in use by {pod.OccupantName}" : "free" )}" );
	}

	[ConCmd( "swrp_pod_demo", Help = "swrp_pod_demo - your view only: run every pod's open, fill, drain and close animation (nobody inside)" )]
	public static void DemoCommand()
	{
		foreach ( var pod in Game.ActiveScene?.GetAllComponents<SwrpBactaPod>() ?? Enumerable.Empty<SwrpBactaPod>() )
		{
			pod._occupantSteam = 0;
			pod._t = 0f;
			pod._beat = 0;
		}
	}
}
