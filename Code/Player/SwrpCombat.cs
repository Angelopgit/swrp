using System;

namespace Swrp;

/// <summary>
/// Combat rules the host applies to every blaster hit: hit zones, stun rounds, friendly fire, and the kill feed.
/// </summary>
public static class SwrpCombat
{
	/// <summary> Host setting. Off: blasters don't hurt troopers on the same side (stun rounds still work, for MPs and the brig). </summary>
	[ConVar( "swrp_friendlyfire", Help = "1 = blasters hurt troopers on the same side (read on the host, default 0)", Saved = true )]
	public static bool FriendlyFire { get; set; }

	public const float HeadMultiplier = 2f;
	public const float LimbMultiplier = 0.75f;

	public static bool SameSide( SwrpPlayer a, SwrpPlayer b ) => a.IsValid() && b.IsValid() && AllegianceRegistry.AreFriendly( a.Allegiance, b.Allegiance );

	/// <summary> Damage multiplier and whether it was a headshot: the model's head hitbox if the trace hit one, else by height. </summary>
	public static (float Multiplier, bool Head) Zone( SwrpPlayer victim, SceneTraceResult tr )
	{
		if ( tr.Hitbox is { } box && box.Tags.Has( "head" ) ) return (HeadMultiplier, true);
		if ( !victim.IsValid() ) return (1f, false);

		var feet = victim.WorldPosition.z;
		var eye = victim.Controller.IsValid() ? victim.Controller.EyePosition.z : feet + 64f;
		var z = tr.HitPosition.z;
		if ( z >= eye - 7f ) return (HeadMultiplier, true);
		if ( z <= feet + ( eye - feet ) * 0.42f ) return (LimbMultiplier, false);
		return (1f, false);
	}

	/// <summary> Host: what a bolt does to whatever it hit. </summary>
	public static void ApplyShot( SwrpBlaster blaster, SceneTraceResult tr, WeaponStats stats, string weaponName, bool stun )
	{
		var vitals = tr.GameObject?.Components.GetInAncestorsOrSelf<SwrpVitals>();
		if ( vitals is null || vitals.IsDead ) return;

		var shooter = blaster.GetComponent<SwrpPlayer>();
		var victim = vitals.GetComponent<SwrpPlayer>();
		var from = tr.StartPosition;

		if ( stun )
		{
			vitals.Stun( stats.StunSeconds, from );
			blaster.ConfirmHit( false, false, true, false );
			return;
		}

		if ( victim.IsValid() && victim != shooter && !FriendlyFire && SameSide( shooter, victim ) )
		{
			blaster.ConfirmHit( false, false, false, true );
			return;
		}

		var (mult, head) = Zone( victim, tr );
		var killed = vitals.TakeDamage( stats.DamageAt( tr.Distance ) * mult, shooter, from, weaponName, head );
		blaster.ConfirmHit( head, killed, false, false );
	}

	/// <summary> Host: a trooper's bolt hit an NPC. Same rules: stun, friendly fire by allegiance, head shots. </summary>
	public static void ApplyShotNpc( SwrpBlaster blaster, SceneTraceResult tr, WeaponStats stats, string weaponName, bool stun, SwrpNpc npc )
	{
		if ( npc is null || npc.IsDead ) return;
		var shooter = blaster.GetComponent<SwrpPlayer>();

		if ( !stun && !FriendlyFire && shooter.IsValid() && AllegianceRegistry.AreFriendly( shooter.Allegiance, npc.Allegiance ) )
		{
			blaster.ConfirmHit( false, false, false, true );
			return;
		}

		var head = tr.Hitbox is { } box && box.Tags.Has( "head" ) || npc.Def.Style != NpcStyle.Roller && tr.HitPosition.z > npc.Eye.z - 8f;
		var killed = npc.TakeHit( stats.DamageAt( tr.Distance ) * ( head ? HeadMultiplier : 1f ), shooter, null, tr.StartPosition, weaponName, head, stun, stats.StunSeconds );
		blaster.ConfirmHit( head && !stun, killed, stun, false );
	}

	// ----- Kill feed -----

	public sealed record FeedEntry( string Killer, string Victim, string Weapon, bool Head, RealTimeSince Age );

	public static List<FeedEntry> Feed { get; } = new();
	public static int FeedVersion { get; private set; }

	/// <summary> Host: announce a kill to everyone. </summary>
	public static void AnnounceKill( SwrpPlayer killer, SwrpPlayer victim, string weapon, bool head, string victimName = null, string killerName = null )
	{
		string Short( SwrpPlayer p ) => p.IsValid() ? p.ShortName : victimName ?? "Unknown";
		if ( !victim.IsValid() && killer.IsValid() ) SwrpCareer.OnNpcKill( killer );      // battalion stats
		ReceiveKill( killer.IsValid() ? Short( killer ) : killerName ?? "", Short( victim ), weapon ?? "", head );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveKill( string killer, string victim, string weapon, bool head )
	{
		Feed.Add( new FeedEntry( killer, victim, weapon, head, 0 ) );
		while ( Feed.Count > 5 ) Feed.RemoveAt( 0 );
		FeedVersion++;
		Log.Info( string.IsNullOrEmpty( killer ) ? $"[SWRP] {victim} went down" : $"[SWRP] {killer} [{weapon}{(head ? ", headshot" : "")}] {victim}" );
	}
}

/// <summary>
/// Local, short-lived combat effects: muzzle flashes, bolt glow, impact flashes, sparks and scorch marks. Nothing here is
/// networked; every machine draws its own from the shot broadcast. Kept cheap for a full server: a shared budget of
/// dynamic lights (the oldest fade first, nothing far from your camera gets one), a cap on live sparks, and fewer extras
/// for shots you can't see from where you are.
/// </summary>
public static class SwrpFx
{
	const int MaxScorches = 48;
	static readonly Queue<GameObject> _scorches = new();
	static Model _scorchQuad;

	/// <summary> At most this many effect lights at once (bolts, flashes, impacts together). </summary>
	public const int MaxLights = 14;
	/// <summary> At most this many sparks flying at once. </summary>
	public const int MaxSparks = 90;
	/// <summary> Lights and sparks only within this distance of your camera. </summary>
	public const float LightRange = 2600f;

	static readonly List<PointLight> _lights = new();
	static Sprite _glow;

	/// <summary> The soft round glow sprite (materials/fx/glow.png): flashes, impacts and the bolts' haze. </summary>
	public static Sprite GlowSprite => _glow ??= Sprite.FromTexture( Texture.Load( "materials/fx/glow.png" ) );

	[ConVar( "swrp_fx_lights", Help = "Blaster bolts, muzzle flashes and impacts light the room (1 = on, 0 = off for slower PCs)", Saved = true )]
	public static bool LightsOn { get; set; } = true;

	/// <summary> A slot in the light budget near the camera, or false (then make the effect without a light). </summary>
	public static bool TakeLight( Scene scene, Vector3 at )
	{
		if ( !LightsOn ) return false;
		var cam = scene.Camera;
		if ( !cam.IsValid() || cam.WorldPosition.DistanceSquared( at ) > LightRange * LightRange ) return false;
		_lights.RemoveAll( l => !l.IsValid() );
		return _lights.Count < MaxLights;
	}

	public static PointLight AddLight( GameObject go, Color color, float radius )
	{
		var light = go.Components.Create<PointLight>();
		light.LightColor = color;
		light.Radius = radius;
		light.Shadows = false;
		_lights.Add( light );
		return light;
	}

	public static bool Near( Scene scene, Vector3 at, float range ) => scene.Camera is { } cam && cam.WorldPosition.DistanceSquared( at ) < range * range;

	/// <summary> A soft additive glow that swells and fades (flashes, impacts). </summary>
	public static void Glow( Scene scene, Vector3 pos, Color color, float size0, float size1, float life )
	{
		_glow ??= Sprite.FromTexture( Texture.Load( "materials/fx/glow.png" ) );
		if ( _glow is null ) return;
		var go = Make( scene, "fx_glow", pos );
		var r = go.Components.Create<SpriteRenderer>();
		r.Sprite = _glow;
		r.Additive = true;
		r.Lighting = false;
		r.Shadows = false;
		r.Color = color;
		r.Size = new Vector2( size0, size0 );
		var g = go.Components.Create<FxGlow>();
		g.Size0 = size0;
		g.Size1 = size1;
		g.Life = life;
		g.Tint = color;
	}

	static GameObject Make( Scene scene, string name, Vector3 pos )
	{
		var go = scene.CreateObject();
		go.Name = name;
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos;
		return go;
	}

	public static void MuzzleFlash( Scene scene, Vector3 pos, Color color )
	{
		if ( !Near( scene, pos, 6000f ) ) return;
		var hot = Color.Lerp( LightProfile.Vivid( color ), Color.White, 0.18f );
		Glow( scene, pos, hot * 1.7f, 10f, 22f, 0.07f );
		if ( LightingSystem.On ) { Lighting.MuzzleFlash( pos, color ); return; }     // the lighting framework (Code/Lighting)
		if ( !TakeLight( scene, pos ) ) return;
		var go = Make( scene, "muzzle_flash", pos );
		AddLight( go, color * 7f, 210f );
		go.Components.Create<FxLife>().Life = 0.08f;
	}

	/// <param name="scale"> How big the hit is: 1 a rifle bolt, more for a starfighter's cannon (FlightBolt). </param>
	public static void Impact( Scene scene, Vector3 pos, Vector3 normal, Color color, bool scorch, float scale = 1f )
	{
		SwrpBuild.HostImpact( scene, pos, normal );     // knocks loose staff props and ragdolls (Code/Build)
		Feel.FeelSounds.Impact( scene, pos, normal, scorch );     // surface sound, ragdoll push (Code/Feel)
		if ( Near( scene, pos, 6000f ) )
		{
			// saturated, no white: the old white-hot flash blew out to a white blob (Angelo 2026-10-12), plus the energy dispersing
			Glow( scene, pos + normal * ( 3f * scale ), LightProfile.Vivid( color ) * 1.1f, 6f * scale, 24f * scale, 0.18f * MathF.Sqrt( scale ) );
			BoltImpactFx.Spawn( scene, pos, normal, color, scale );
			if ( Near( scene, pos, 3000f ) ) BoltSmoke.Spawn( scene, pos, normal, scale );      // the wisp of smoke off the hit (Angelo, 2026-10-19)
			if ( LightingSystem.On ) Lighting.Impact( pos, normal, color );
			else if ( TakeLight( scene, pos ) )
			{
				var go = Make( scene, "bolt_impact", pos + normal * 6f );
				AddLight( go, LightProfile.Vivid( color ) * 2.6f, 180f * scale );
				go.Components.Create<FxLife>().Life = 0.18f;
			}

			// sparks, fewer when the air is already full of them or you're far away
			var many = SparkFx.Live > MaxSparks * 2 / 3 || !Near( scene, pos, LightRange );
			var count = SparkFx.Live >= MaxSparks ? 0 : many ? 2 : (int)( 7 * MathF.Min( scale, 2.5f ) );
			for ( var i = 0; i < count; i++ )
			{
				var dir = ( normal + Vector3.Random * 0.9f ).Normal;
				SparkFx.Spawn( scene, pos + normal, dir * Game.Random.Float( 140f, 360f ) * MathF.Sqrt( scale ), color );
			}
		}

		if ( scorch && Near( scene, pos, 4000f ) ) Scorch( scene, pos, normal );

		// a bolt hitting right beside you jolts the view a little
		SwrpShake.AddAt( pos, 0.12f * MathF.Min( scale, 3f ), 140f * scale );
	}

	/// <summary>
	/// A thermal detonator going off (every machine): the boom, a white-orange flash and a wide glow, a big short-lived light,
	/// a shower of sparks, a scorch on the floor below, rising smoke, a shake and (close by) ringing ears, and loose physics
	/// objects on this machine thrown about.
	/// </summary>
	public static void Explosion( Scene scene, Vector3 pos, Vector3 floorPoint, Vector3 floorNormal, float radius, bool silentTest = false )
	{
		if ( silentTest ) { _testNear = true; ExplosionLook( scene, pos, floorPoint, floorNormal, radius ); _testNear = false; return; }
		Sound.Play( SwrpAudio.DetonatorBlast, pos );
		if ( SwrpAudio.DetonatorBlast != SwrpAudio.DetonatorBoom ) Sound.Play( SwrpAudio.DetonatorBoom, pos );     // the low thump under the crack
		SwrpShake.Blast( pos, 1.6f, radius * 5f );
		if ( !Near( scene, pos, 7000f ) ) return;
		ExplosionLook( scene, pos, floorPoint, floorNormal, radius );
	}

	static bool _testNear;

	/// <summary> Everything you see of a blast (no sound or shake). </summary>
	static void ExplosionLook( Scene scene, Vector3 pos, Vector3 floorPoint, Vector3 floorNormal, float radius )
	{

		// the flash, then a rolling fireball, a dust ring along the deck and chunks of debris (Angelo, 2026-10-12: it has to look
		// like a grenade just went off)
		var hot = new Color( 1f, 0.86f, 0.62f );
		Glow( scene, pos, hot * 6f, 40f, 260f, 0.16f );
		Glow( scene, pos + Vector3.Up * 10f, new Color( 1f, 0.5f, 0.2f, 0.7f ) * 2f, 80f, 420f, 0.5f );
		Make( scene, "blast_fireball", pos + Vector3.Up * 8f ).Components.Create<Fireball>();
		if ( floorPoint != Vector3.Zero ) Make( scene, "blast_dust", floorPoint + floorNormal * 4f ).Components.Create<DustRing>().Up = floorNormal;
		Make( scene, "blast_debris", pos ).Components.Create<Debris>();
		if ( LightingSystem.On ) Lighting.Explosion( pos, radius );
		else if ( TakeLight( scene, pos ) )
		{
			var go = Make( scene, "blast_light", pos + Vector3.Up * 20f );
			AddLight( go, new Color( 1f, 0.62f, 0.32f ) * 14f, radius * 2f );
			go.Components.Create<FxLife>().Life = 0.45f;
		}

		var near = _testNear || Near( scene, pos, LightRange );
		var sparks = Math.Min( near ? 48 : 12, Math.Max( 0, MaxSparks - SparkFx.Live ) );
		for ( var i = 0; i < sparks; i++ )
		{
			var dir = ( Vector3.Random + Vector3.Up * 0.6f ).Normal;
			SparkFx.Spawn( scene, pos, dir * Game.Random.Float( 260f, 900f ), new Color( 1f, 0.7f, 0.35f ) );
		}

		if ( floorPoint != Vector3.Zero && Near( scene, pos, 4000f ) ) Scorch( scene, floorPoint, floorNormal, Game.Random.Float( 22f, 30f ) );
		if ( near ) Make( scene, "blast_smoke", pos ).Components.Create<BlastSmoke>();

		// loose things here (props, dropped items, ragdolls' bodies) get thrown about
		foreach ( var go in scene.FindInPhysics( new Sphere( pos, radius ) ) )
		{
			if ( go.IsProxy || go.Components.Get<Rigidbody>() is not { MotionEnabled: true } rb ) continue;
			var to = go.WorldPosition - pos;
			var k = 1f - Math.Clamp( to.Length / radius, 0f, 1f );
			rb.Velocity += ( to.Normal + Vector3.Up * 0.5f ).Normal * 700f * k;
		}
	}

	static void Scorch( Scene scene, Vector3 pos, Vector3 normal, float scale = 0f )
	{
		if ( BlasterScorch.Place( scene, pos, normal, scale * 2f ) ) return;     // projected decals (Code/Fx); the quad is the fallback
		_scorchQuad ??= BuildQuad();
		if ( _scorchQuad is null ) return;

		var go = Make( scene, "scorch", pos + normal * 0.35f );
		go.WorldRotation = Rotation.LookAt( normal ) * Rotation.FromRoll( Game.Random.Float( 0f, 360f ) );
		go.WorldScale = scale > 0f ? scale : Game.Random.Float( 5f, 8f );
		var r = go.Components.Create<ModelRenderer>();
		r.Model = _scorchQuad;
		r.RenderType = ModelRenderer.ShadowRenderType.Off;
		go.Components.Create<ScorchFx>();

		_scorches.Enqueue( go );
		while ( _scorches.Count > MaxScorches )
		{
			var old = _scorches.Dequeue();
			if ( old.IsValid() ) old.Destroy();
		}
	}

	/// <summary> A unit quad facing +X with the scorch material. </summary>
	static Model BuildQuad()
	{
		var mat = Material.Load( "materials/fx/scorch.vmat" );
		if ( mat is null ) return null;

		var n = Vector3.Forward;
		var t = Vector3.Left;
		var verts = new List<Vertex>
		{
			new( new Vector3( 0, -1, -1 ), n, t, new Vector4( 1, 1, 0, 0 ) ),
			new( new Vector3( 0, 1, -1 ), n, t, new Vector4( 0, 1, 0, 0 ) ),
			new( new Vector3( 0, 1, 1 ), n, t, new Vector4( 0, 0, 0, 0 ) ),
			new( new Vector3( 0, -1, 1 ), n, t, new Vector4( 1, 0, 0, 0 ) ),
		};
		var mesh = new Mesh( mat );
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( 6, new List<int> { 0, 2, 1, 0, 3, 2 } );
		mesh.Bounds = new BBox( new Vector3( -0.1f, -1, -1 ), new Vector3( 0.1f, 1, 1 ) );
		return Model.Builder.AddMesh( mesh ).Create();
	}
}

/// <summary> An additive glow sprite that swells from Size0 to Size1 and fades out over its life. </summary>
public sealed class FxGlow : Component
{
	public float Size0 { get; set; }
	public float Size1 { get; set; }
	public float Life { get; set; } = 0.1f;
	public Color Tint { get; set; }
	TimeSince _age;
	SpriteRenderer _r;

	protected override void OnStart()
	{
		_age = 0;
		_r = GetComponent<SpriteRenderer>();
	}

	protected override void OnUpdate()
	{
		var k = _age / MathF.Max( 0.01f, Life );
		if ( k >= 1f || !_r.IsValid() ) { GameObject.Destroy(); return; }
		var s = MathX.Lerp( Size0, Size1, MathF.Sqrt( k ) );
		_r.Size = new Vector2( s, s );
		_r.Color = Tint.WithAlpha( Tint.a * ( 1f - k ) * ( 1f - k ) );
	}
}

/// <summary> Destroys its object after <see cref="Life"/> seconds, fading any light on it. </summary>
public sealed class FxLife : Component
{
	public float Life { get; set; } = 0.1f;
	TimeSince _age;
	float _startBrightness = -1;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var light = GetComponent<PointLight>();
		if ( light.IsValid() )
		{
			if ( _startBrightness < 0 ) _startBrightness = 1f;
			light.LightColor = light.LightColor * MathF.Max( 0f, 1f - Time.Delta / MathF.Max( 0.01f, Life ) );
		}

		if ( _age > Life ) GameObject.Destroy();
	}
}

/// <summary> A detonator's smoke: a few dark puffs that swell, rise and thin out over a few seconds. Local and cheap. </summary>
public sealed class BlastSmoke : Component
{
	const int Puffs = 16;
	const float Life = 5f;
	static Sprite _sprite;
	Sprite[][] _sheets;
	readonly List<Sprite[]> _frames = new();
	readonly List<(SpriteRenderer R, Vector3 Vel, float Size, float Delay)> _puffs = new();
	TimeSince _age;

	protected override void OnStart()
	{
		_age = 0;
		_sprite ??= Sprite.FromTexture( Texture.Load( "materials/fx/steam.png" ) );
		_sheets = Billow.Sheets;
		if ( _sprite is null && _sheets is null ) { GameObject.Destroy(); return; }
		for ( var i = 0; i < Puffs; i++ )
		{
			var go = new GameObject( GameObject, true, "puff" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = Vector3.Random.WithZ( 0 ) * 45f + Vector3.Up * Game.Random.Float( 6f, 50f );
			var r = go.Components.Create<SpriteRenderer>();
			var frames = _sheets?[Game.Random.Int( 0, _sheets.Length - 1 )];
			_frames.Add( frames );
			r.Sprite = frames?[0] ?? _sprite;
			r.Lighting = true;
			r.Shadows = false;
			r.Color = Color.Transparent;
			var vel = ( Vector3.Random.WithZ( 0 ) * 60f ) + Vector3.Up * Game.Random.Float( 30f, 70f );
			_puffs.Add( (r, vel, Game.Random.Float( 110f, 210f ), Game.Random.Float( 0.1f, 0.5f )) );
		}
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		if ( _age > Life + 0.4f ) { GameObject.Destroy(); return; }
		for ( var i = 0; i < _puffs.Count; i++ )
		{
			var (r, vel, size, delay) = _puffs[i];
			if ( !r.IsValid() ) continue;
			var t = _age - delay;
			if ( t < 0f ) continue;
			var k = Math.Clamp( t / Life, 0f, 1f );
			if ( _frames[i] is { } fr ) r.Sprite = fr[Math.Min( 63, (int)( MathF.Sqrt( k ) * 63f ) )];
			r.WorldPosition += vel * ( 1f - k ) * dt;
			var s = size * ( 0.35f + 0.65f * MathF.Sqrt( k ) );
			r.Size = new Vector2( s, s );
			var shade = 0.09f + 0.08f * k;
			r.Color = new Color( shade, shade, shade * 1.05f, 0.7f * MathF.Min( 1f, k * 8f ) * ( 1f - k ) );
		}
	}
}

/// <summary> A spark streak that flies out of an impact, falls, and burns out. </summary>
public sealed class SparkFx : Component
{
	Vector3 _velocity;
	LineRenderer _line;
	TimeSince _age;
	float _life;

	/// <summary> Sparks in flight right now (SwrpFx keeps it under a cap). </summary>
	public static int Live { get; private set; }

	protected override void OnDestroy() => Live = Math.Max( 0, Live - 1 );

	public static void Spawn( Scene scene, Vector3 pos, Vector3 velocity, Color color )
	{
		Live++;
		var go = scene.CreateObject();
		go.Name = "spark";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos;
		var s = go.Components.Create<SparkFx>();
		s._velocity = velocity;
		s._age = 0;
		s._life = Game.Random.Float( 0.18f, 0.4f );
		s._line = go.Components.Create<LineRenderer>();
		s._line.UseVectorPoints = true;
		s._line.VectorPoints = new List<Vector3> { pos, pos };
		s._line.Color = new Gradient( new Gradient.ColorFrame( 0f, Color.White * 2f ), new Gradient.ColorFrame( 1f, color * 1.6f ) );
		s._line.Width = new Curve( new Curve.Frame( 0f, 0.5f ), new Curve.Frame( 1f, 0.12f ) );
		s._line.Additive = true;
		s._line.Lighting = false;
		s._line.CastShadows = false;
	}

	protected override void OnUpdate()
	{
		if ( _age > _life ) { GameObject.Destroy(); return; }
		var dt = Time.Delta;
		_velocity += Vector3.Down * 600f * dt;
		var head = WorldPosition + _velocity * dt;
		_line.VectorPoints = new List<Vector3> { head - _velocity.Normal * 3f, head };
		WorldPosition = head;
	}
}

/// <summary> A scorch mark that stays a while, then fades out and removes itself. </summary>
public sealed class ScorchFx : Component
{
	public const float Stay = 20f;
	public const float Fade = 4f;
	TimeSince _age;
	ModelRenderer _r;

	protected override void OnStart()
	{
		_age = 0;
		_r = GetComponent<ModelRenderer>();
	}

	protected override void OnUpdate()
	{
		if ( _age < Stay ) return;
		var a = 1f - ( _age - Stay ) / Fade;
		if ( a <= 0f ) { GameObject.Destroy(); return; }
		if ( _r.IsValid() ) _r.Tint = Color.White.WithAlpha( a );
	}
}

/// <summary>
/// Billowing puffs played from Battlefront II's smoke flipbook (Assets/materials/fx/bf2, ModelSource/bf2_fx.py; local only):
/// the frames are cached as sprites, each puff steps through 64 frames over its life.
/// </summary>
public static class Billow
{
	static Sprite[][] _sheets;

	/// <summary> The two 64-frame sheets, or null when the frames aren't in this copy (then the old glow puffs are used). </summary>
	public static Sprite[][] Sheets
	{
		get
		{
			if ( _sheets is not null ) return _sheets.Length > 0 ? _sheets : null;
			var list = new List<Sprite[]>();
			for ( var s = 0; s < 2; s++ )
			{
				var frames = new Sprite[64];
				for ( var i = 0; i < 64; i++ )
				{
					var tex = Texture.Load( $"materials/fx/bf2/billow_{s}_{i:00}.png" );
					if ( tex is null || tex.IsError ) { _sheets = Array.Empty<Sprite[]>(); return null; }
					frames[i] = Sprite.FromTexture( tex );
				}
				list.Add( frames );
			}
			_sheets = list.ToArray();
			return _sheets;
		}
	}
}

/// <summary>
/// The fireball of a thermal detonator: billowing flame puffs (the smoke flipbook drawn additive and fire-coloured) that burst
/// out and roll upward, cooling from white-yellow through orange to a deep red as they go out; dark smoke takes over behind
/// them (BlastSmoke). Falls back to plain glow puffs without the flipbook frames.
/// </summary>
public sealed class Fireball : Component
{
	/// <summary> Size and reach of the fireball (a starfighter's torpedo is several times a detonator's). </summary>
	public float Scale { get; set; } = 1f;
	const int Count = 12;
	const float Life = 0.95f;
	readonly List<(SpriteRenderer R, Vector3 Vel, float Size, float Life, Sprite[] Frames)> _puffs = new();
	TimeSince _age;

	protected override void OnStart()
	{
		_age = 0;
		var sheets = Billow.Sheets;
		var glow = sheets is null ? Sprite.FromTexture( Texture.Load( "materials/fx/glow.png" ) ) : null;
		if ( sheets is null && glow is null ) { GameObject.Destroy(); return; }
		for ( var i = 0; i < Count; i++ )
		{
			var go = new GameObject( GameObject, true, "flame" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = Vector3.Random * 14f * Scale;
			var r = go.Components.Create<SpriteRenderer>();
			var frames = sheets?[Game.Random.Int( 0, sheets.Length - 1 )];
			r.Sprite = frames?[0] ?? glow;
			r.Additive = true;
			r.Lighting = false;
			r.Shadows = false;
			var dir = ( Vector3.Random + Vector3.Up * 0.8f ).Normal;
			_puffs.Add( (r, dir * Game.Random.Float( 110f, 260f ) * Scale, Game.Random.Float( 150f, 260f ) * Scale, Life * Game.Random.Float( 0.65f, 1.1f ) * MathF.Sqrt( Scale ), frames) );
		}
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		if ( _age > Life * 1.2f * MathF.Sqrt( Scale ) ) { GameObject.Destroy(); return; }
		foreach ( var (r, vel, size, life, frames) in _puffs )
		{
			if ( !r.IsValid() ) continue;
			var k = Math.Clamp( _age / life, 0f, 1f );
			if ( frames is not null ) r.Sprite = frames[Math.Min( 63, (int)( k * 63f ) )];
			// fast out, then drifting up as it slows
			r.WorldPosition += ( vel * MathF.Pow( 1f - k, 3f ) + Vector3.Up * 70f * k ) * dt;
			var s = size * ( 0.3f + 0.7f * MathF.Sqrt( k ) );
			r.Size = new Vector2( s, s );
			var col = k < 0.2f ? Color.Lerp( new Color( 1f, 0.95f, 0.8f ), new Color( 1f, 0.62f, 0.22f ), k / 0.2f )
				: Color.Lerp( new Color( 1f, 0.62f, 0.22f ), new Color( 0.5f, 0.1f, 0.02f ), ( k - 0.2f ) / 0.8f );
			r.Color = col * ( 3f * ( 1f - k * k ) );
		}
	}
}

/// <summary> Dust and smoke blown out along the floor from a blast: a ring of lit puffs racing outward and settling. </summary>
public sealed class DustRing : Component
{
	public Vector3 Up { get; set; } = Vector3.Up;
	public float Scale { get; set; } = 1f;
	const int Count = 18;
	const float Life = 2.2f;
	readonly List<(SpriteRenderer R, Vector3 Dir, float Speed, Sprite[] Frames)> _puffs = new();
	TimeSince _age;

	protected override void OnStart()
	{
		_age = 0;
		var sheets = Billow.Sheets;
		var sprite = sheets is null ? Sprite.FromTexture( Texture.Load( "materials/fx/steam.png" ) ) : null;
		if ( sprite is null && sheets is null ) { GameObject.Destroy(); return; }
		var basis = Rotation.LookAt( Up );
		for ( var i = 0; i < Count; i++ )
		{
			var a = i * MathF.PI * 2f / Count + Game.Random.Float( -0.15f, 0.15f );
			var dir = basis * new Vector3( 0f, MathF.Cos( a ), MathF.Sin( a ) );
			var go = new GameObject( GameObject, true, "dust" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			var r = go.Components.Create<SpriteRenderer>();
			var frames = sheets?[Game.Random.Int( 0, sheets.Length - 1 )];
			r.Sprite = frames?[Game.Random.Int( 0, 20 )] ?? sprite;
			r.Lighting = true;
			r.Shadows = false;
			r.Color = Color.Transparent;
			_puffs.Add( (r, dir, Game.Random.Float( 380f, 560f ), frames) );
		}
	}

	protected override void OnUpdate()
	{
		if ( _age > Life ) { GameObject.Destroy(); return; }
		var k = Math.Clamp( _age / Life, 0f, 1f );
		var dist = MathF.Sqrt( k );
		foreach ( var (r, dir, speed, frames) in _puffs )
		{
			if ( !r.IsValid() ) continue;
			if ( frames is not null ) r.Sprite = frames[Math.Min( 63, 20 + (int)( k * 43f ) )];
			r.WorldPosition = WorldPosition + dir * ( speed * 0.45f * dist * Scale ) + Up * ( 10f + 30f * k ) * Scale;
			var s = ( 90f + 200f * dist ) * Scale;
			r.Size = new Vector2( s, s );
			r.Color = new Color( 0.34f, 0.32f, 0.3f, 0.6f * ( 1f - k ) * MathF.Min( 1f, k * 10f ) );
		}
	}
}

/// <summary> Chunks of the detonator casing and the deck thrown out, tumbling, falling and bouncing before they fade. </summary>
public sealed class Debris : Component
{
	public float Scale { get; set; } = 1f;
	const int Count = 10;
	const float Life = 2.2f;
	readonly List<(GameObject Go, Vector3 Vel, Rotation Spin)> _bits = new();
	TimeSince _age;

	protected override void OnStart()
	{
		_age = 0;
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box?.Bounds.Size.x ?? 50f;
		for ( var i = 0; i < Count; i++ )
		{
			var go = new GameObject( GameObject, true, "chunk" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalScale = new Vector3( Game.Random.Float( 1.2f, 3f ), Game.Random.Float( 1f, 2.4f ), Game.Random.Float( 0.6f, 1.6f ) ) / unit * Scale;
			go.LocalRotation = Rotation.Random;
			var r = go.Components.Create<ModelRenderer>();
			r.Model = box;
			r.Tint = Game.Random.Float() < 0.5f ? new Color( 0.15f, 0.15f, 0.16f ) : new Color( 0.35f, 0.33f, 0.3f );
			var dir = ( Vector3.Random.WithZ( 0 ) + Vector3.Up * Game.Random.Float( 0.6f, 1.4f ) ).Normal;
			_bits.Add( (go, dir * Game.Random.Float( 300f, 700f ) * MathF.Sqrt( Scale ), Rotation.From( Game.Random.Float( -720f, 720f ), Game.Random.Float( -720f, 720f ), 0f )) );
		}
	}

	protected override void OnUpdate()
	{
		var dt = Time.Delta;
		if ( _age > Life ) { GameObject.Destroy(); return; }
		for ( var i = 0; i < _bits.Count; i++ )
		{
			var (go, vel, spin) = _bits[i];
			if ( !go.IsValid() ) continue;
			vel += Vector3.Down * 900f * dt;
			var from = go.WorldPosition;
			var to = from + vel * dt;
			var tr = Scene.Trace.Ray( from, to ).WithoutTags( "player", "npc", "trigger", "debris" ).Run();
			if ( tr.Hit ) { to = tr.HitPosition + tr.Normal; vel = Vector3.Reflect( vel, tr.Normal ) * 0.35f; }
			go.WorldPosition = to;
			go.WorldRotation *= Rotation.Slerp( Rotation.Identity, spin, dt );
			if ( _age > Life - 0.5f ) go.LocalScale *= 1f - dt * 2f;
			_bits[i] = (go, vel, spin);
		}
	}
}

/// <summary> Dev: the blast's look and sound on its own (no damage, this machine only). </summary>
public static class SwrpBlastTest
{
	[ConCmd( "swrp_blast_fx", Help = "swrp_blast_fx <x> <y> <z> - dev: show a thermal detonator blast at a point (the look only: no sound, shake or damage)" )]
	public static void BlastFx( float x, float y, float z )
	{
		if ( Game.ActiveScene is not { } scene ) return;
		var pos = new Vector3( x, y, z );
		var floor = scene.Trace.Ray( pos + Vector3.Up * 10f, pos + Vector3.Down * 120f ).WithoutTags( "player", "npc", "trigger", "debris" ).Run();
		SwrpFx.Explosion( scene, pos, floor.Hit ? floor.HitPosition : Vector3.Zero, floor.Hit ? floor.Normal : Vector3.Up, 260f, silentTest: true );
	}
}
