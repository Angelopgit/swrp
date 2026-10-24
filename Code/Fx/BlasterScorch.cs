using System;
using System.Collections.Generic;

namespace Swrp;

/// <summary>
/// Blaster burns on walls and floors (Angelo, 2026-10-12): a projected <see cref="Decal"/> using s&amp;box's own scorch marks
/// (core decals/scorchmarks), so it wraps edges and takes the surface's shine instead of floating on a quad. A bolt leaves a hot
/// orange glow that cools in about a second, then the soot stays, fades out and is removed. Local only, nothing networked.
/// Kept cheap: a hard cap (the oldest go first), no new marks far from your camera, and rapid fire into one spot darkens the
/// mark already there instead of stacking more. Called from <see cref="SwrpFx"/>'s Scorch; returns false (the old quad is used)
/// when the decals aren't available.
/// </summary>
public static class BlasterScorch
{
	[ConVar( "swrp_scorch_max", Help = "Blaster scorch marks kept at once (8-256, oldest removed first)", Saved = true )]
	public static int MaxMarks { get; set; } = 64;

	[ConVar( "swrp_scorch_life", Help = "Seconds a blaster scorch stays before it starts to fade (3-300)", Saved = true )]
	public static float Life { get; set; } = 25f;

	/// <summary> Seconds the fade-out takes. </summary>
	public const float FadeTime = 5f;
	/// <summary> No new marks further than this from your camera. </summary>
	public const float Range = 4000f;
	/// <summary> A hit closer than this to a live mark (same facing) reuses it. </summary>
	const float MergeDistance = 4f;

	static readonly string[] Paths = { "decals/scorchmarks/decal_scorchmark_02.decal", "decals/scorchmarks/decal_scorchmark_01.decal" };
	static DecalDefinition[] _defs;
	static readonly LinkedList<BlasterScorchFx> _live = new();

	static DecalDefinition[] Defs
	{
		get
		{
			if ( _defs is not null ) return _defs;
			var list = new List<DecalDefinition>();
			foreach ( var p in Paths )
				if ( ResourceLibrary.Get<DecalDefinition>( p ) is { } d ) list.Add( d );
			_defs = list.ToArray();
			if ( _defs.Length == 0 ) Log.Warning( "[SWRP] scorch decals not found, using the old quad marks" );
			return _defs;
		}
	}

	/// <summary> Burn a mark at <paramref name="pos"/> on a surface facing <paramref name="normal"/>; size in world units (0 = a bolt). </summary>
	public static bool Place( Scene scene, Vector3 pos, Vector3 normal, float size = 0f )
	{
		var defs = Defs;
		if ( defs.Length == 0 ) return false;
		if ( !SwrpFx.Near( scene, pos, Range ) ) return true;

		var bolt = size <= 0f;
		if ( bolt ) size = Game.Random.Float( 8f, 12f );     // Angelo 2026-10-12: 4-6 was too small, asked for double

		// a hot core that cools from white-orange to a dull ember (bolts only; blasts have their own fireball)
		if ( bolt && SwrpFx.Near( scene, pos, 2600f ) )
		{
			// a quick flicker only: the longer two-glow version read as orange dots stuck on the wall (Angelo 2026-10-12)
			SwrpFx.Glow( scene, pos + normal * 0.8f, new Color( 1f, 0.5f, 0.15f ) * 1.2f, size * 0.3f, size * 0.1f, 0.5f );
		}

		// rapid fire into one spot: darken and grow the mark already there
		foreach ( var fx in _live )
		{
			if ( !fx.IsValid() || fx.WorldPosition.DistanceSquared( pos + normal ) > MergeDistance * MergeDistance ) continue;
			if ( fx.Normal.Dot( normal ) < 0.9f ) continue;
			fx.Refresh( Math.Min( fx.Size * 1.06f, size * 1.8f ) );
			return true;
		}

		var go = scene.CreateObject();
		go.Name = "blaster_scorch";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos + normal;     // the decal projects forward, into the surface
		go.WorldRotation = Rotation.LookAt( -normal );

		var decal = go.Components.Create<Decal>();
		var def = defs[Game.Random.Int( 0, defs.Length - 1 )];
		decal.Decals = new List<DecalDefinition> { def };
		decal.Size = SizeFor( def, size );
		decal.Rotation = Game.Random.Float( 0f, 360f );
		decal.Depth = 6f;
		decal.AttenuationAngle = 0.6f;
		decal.Transient = true;

		var mark = go.Components.Create<BlasterScorchFx>();
		mark.Normal = normal;
		mark.Size = size;
		mark.Def = def;
		_live.AddLast( mark );

		var max = Math.Clamp( MaxMarks, 8, 256 );
		while ( _live.Count > max )
		{
			var old = _live.First.Value;
			_live.RemoveFirst();
			if ( old.IsValid() ) old.GameObject.Destroy();
		}
		return true;
	}

	internal static void Forget( BlasterScorchFx fx ) => _live.Remove( fx );

	/// <summary> Decal.Size multiplies the definition's own Width/Height (the stock bullet decal prefab uses Size 1,1 on a 6-unit
	/// decal), it isn't world units; the engine scorch marks are 64 and 128 wide. </summary>
	internal static Vector2 SizeFor( DecalDefinition def, float wanted ) =>
		new( wanted / MathF.Max( 1f, def?.Width ?? 64f ), wanted / MathF.Max( 1f, def?.Height ?? 64f ) );
}

/// <summary> One scorch decal: stays for <see cref="BlasterScorch.Life"/> seconds, fades and removes itself. </summary>
public sealed class BlasterScorchFx : Component
{
	public Vector3 Normal { get; set; }
	public float Size { get; set; }
	public DecalDefinition Def { get; set; }
	TimeSince _age;
	Decal _decal;
	float _shade = 0.85f;

	protected override void OnStart()
	{
		_age = 0;
		_decal = GetComponent<Decal>();
		Apply( 1f );
	}

	/// <summary> Hit again: back to full strength, a little darker and bigger. </summary>
	public void Refresh( float size )
	{
		_age = 0;
		_shade = Math.Min( 1f, _shade + 0.05f );
		Size = size;
		if ( _decal.IsValid() ) _decal.Size = BlasterScorch.SizeFor( Def, size );
		Apply( 1f );
	}

	protected override void OnUpdate()
	{
		var life = Math.Clamp( BlasterScorch.Life, 3f, 300f );
		if ( _age < life ) return;
		var a = 1f - ( _age - life ) / BlasterScorch.FadeTime;
		if ( a <= 0f ) { GameObject.Destroy(); return; }
		Apply( a );
	}

	void Apply( float a )
	{
		if ( _decal.IsValid() ) _decal.ColorTint = Color.White.WithAlpha( a * _shade );
	}

	protected override void OnDestroy() => BlasterScorch.Forget( this );
}
