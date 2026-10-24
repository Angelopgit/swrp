using System;

namespace Swrp;

/// <summary>
/// A droideka's deflector bubble (Angelo, 2026-10-13: "a realistic look shield"). Two shells round the body: an inner one of
/// refractive glass (materials/fx/shield_glass.vmat, the stock glass shader) that bends what's behind it, and an outer
/// additive one (shaders/swrp_shield.shader) with a bright rim, a faint hex lattice, energy bands crawling over it and a
/// ripple from every hit. It blooms out from the generator when the droideka finishes unfolding, hums and flickers as it
/// weakens, and pops with a flash when it breaks or the droideka rolls up. Local and visual; SwrpNpc drives it.
/// </summary>
public sealed class DroidekaShield : Component
{
	public const float Radius = 54f;
	static readonly Color Blue = new( 0.28f, 0.58f, 1f );

	/// <summary> Should be up (deployed and holding charge). </summary>
	public bool Up { get; set; }
	/// <summary> Charge left, 0..1. </summary>
	public float Strength { get; set; } = 1f;
	/// <summary> World centre of the bubble. </summary>
	public Vector3 Centre { get; set; }

	GameObject _root;
	ModelRenderer _glass, _bubble;
	float _unit = 64f;        // the dev sphere's size
	float _scale;             // 0 gone .. 1 full
	float _pop;               // a burst when it collapses
	bool _wasUp;
	TimeSince _sinceHit = 10;

	protected override void OnStart()
	{
		_root = new GameObject( GameObject, true, "Deflector" );
		_root.NetworkMode = NetworkMode.Never;
		_root.Flags |= GameObjectFlags.NotSaved;
		_glass = Shell( "Glass", "materials/fx/shield_glass.vmat", 0.985f );
		_bubble = Shell( "Bubble", "materials/fx/shield_bubble.vmat", 1f );
		_unit = MathF.Max( 1f, _bubble.Model?.Bounds.Size.x ?? 64f );
		_root.Enabled = false;
	}

	ModelRenderer Shell( string name, string material, float size )
	{
		var go = new GameObject( _root, true, name );
		go.Flags |= GameObjectFlags.NotSaved;
		go.LocalScale = size;
		var r = go.AddComponent<ModelRenderer>();
		r.Model = Model.Load( "models/dev/sphere.vmdl" );
		r.MaterialOverride = Material.Load( material );
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		return r;
	}

	/// <summary> A bolt struck the bubble from that direction: ripple out from the point it hit. </summary>
	public void Hit( Vector3 from )
	{
		var dir = ( from - Centre ).Normal;
		var at = Centre + dir * Radius;
		_sinceHit = 0;
		SetAttr( "ShieldHitPos", at );
		SetAttr( "ShieldHitTime", Time.Now );
	}

	void SetAttr( string key, Vector3 v )
	{
		if ( _bubble.IsValid() && _bubble.SceneObject is { } so ) so.Attributes.Set( key, v );
	}

	void SetAttr( string key, float v )
	{
		if ( _bubble.IsValid() && _bubble.SceneObject is { } so ) so.Attributes.Set( key, v );
	}

	protected override void OnUpdate()
	{
		if ( !_root.IsValid() ) return;
		var dt = Time.Delta;

		if ( _wasUp && !Up && _scale > 0.3f ) _pop = 1f;       // broke or rolled up: a quick bright burst outwards
		_wasUp = Up;
		_scale = _scale.Approach( Up ? 1f : 0f, dt / ( Up ? 0.45f : 0.12f ) );
		_pop = _pop.Approach( 0f, dt / 0.25f );

		var visible = _scale > 0.001f || _pop > 0.001f;
		_root.Enabled = visible;
		if ( !visible ) return;

		// blooms out with a little overshoot; the pop swells it as it fades
		var s = _scale;
		var grow = s < 1f ? 1f - MathF.Pow( 1f - s, 3f ) + MathF.Sin( s * MathF.PI ) * 0.08f : 1f;
		var size = Radius * 2f / _unit * ( grow + _pop * 0.25f );
		_root.WorldPosition = Centre;
		_root.WorldRotation = Rotation.Identity;
		_root.WorldScale = MathF.Max( 0.01f, size );

		// weaker charge: dimmer, and it stutters
		var str = Math.Clamp( Strength, 0f, 1f );
		var flicker = 1f;
		if ( str < 0.35f ) flicker = Game.Random.Float() < ( 0.35f - str ) * 0.8f ? 0.25f : 1f;
		var hum = 0.92f + 0.08f * MathF.Sin( Time.Now * 11f );
		var power = ( 0.45f + 0.55f * str ) * hum * flicker * MathF.Max( s, _pop * 1.6f ) + ( _sinceHit < 0.15f ? 0.3f : 0f );
		SetAttr( "ShieldPower", power );
		_bubble.Tint = Blue;
		_glass.Enabled = s > 0.6f;
		_glass.Tint = Color.White.WithAlpha( s );
	}
}
