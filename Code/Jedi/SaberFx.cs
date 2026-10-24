using System;
using System.Collections.Generic;

namespace Swrp.Jedi;

/// <summary> Every lightsaber and Force sound (synthesized by SoundSource/jedi_sounds.py into Assets/sounds/jedi). </summary>
public static class SaberSounds
{
	public const string Ignite = "sounds/jedi/saber_on.sound";
	public const string Retract = "sounds/jedi/saber_off.sound";
	public const string Hum = "sounds/jedi/saber_hum.sound";
	public const float HumLoopSeconds = 2.4f;
	public const float SpinLoopSeconds = 1.2f;
	public const string Swing1 = "sounds/jedi/saber_swing1.sound";
	public const string Swing2 = "sounds/jedi/saber_swing2.sound";
	public const string Swing3 = "sounds/jedi/saber_swing3.sound";
	public const string SwingHeavy = "sounds/jedi/saber_swing_heavy.sound";
	public const string HitFlesh = "sounds/jedi/saber_hit_flesh.sound";
	public const string HitDroid = "sounds/jedi/saber_hit_droid.sound";
	public const string Clash = "sounds/jedi/saber_clash.sound";
	public const string Block = "sounds/jedi/saber_block.sound";
	public const string Parry = "sounds/jedi/saber_parry.sound";
	public const string GuardBreak = "sounds/jedi/saber_guardbreak.sound";
	public const string LockGrind = "sounds/jedi/saber_lock.sound";
	public const string Burn = "sounds/jedi/saber_burn.sound";
	public const string Deflect = "sounds/jedi/saber_deflect.sound";
	public const string Throw = "sounds/jedi/saber_throw.sound";
	public const string ThrowSpin = "sounds/jedi/saber_spin.sound";
	public const string Catch = "sounds/jedi/saber_catch.sound";
	public const string Push = "sounds/jedi/force_push.sound";
	public const string Pull = "sounds/jedi/force_pull.sound";
	public const string Leap = "sounds/jedi/force_leap.sound";
	public const string Speed = "sounds/jedi/force_speed.sound";
	public const string Heal = "sounds/jedi/force_heal.sound";
	public const string Lightning = "sounds/jedi/force_lightning.sound";
	public const string LevelUp = "sounds/jedi/jedi_levelup.sound";
	public const string FormChange = "sounds/jedi/jedi_form.sound";
	public const string Deny = "sounds/ui/ui_deny.sound";

	public static SoundHandle Play( string sound, Vector3 at )
	{
		if ( !Game.IsPlaying ) return null;
		return Sound.Play( Real( sound ), at );
	}

	static readonly Dictionary<string, string> _real = new();

	/// <summary>
	/// The Galactic Contention mod's recorded saber sound for one of ours when that copy has it (Assets/sounds/jedi_ext, local
	/// only; Angelo 2026-10-13): ignite, retract, hum, swings, clashes, blocks, parries, deflects, hits. Otherwise ours.
	/// </summary>
	public static string Real( string sound )
	{
		if ( sound is null ) return null;
		if ( _real.TryGetValue( sound, out var r ) ) return r;
		var ext = sound.Replace( "sounds/jedi/", "sounds/jedi_ext/" );
		r = ext != sound && ResourceLibrary.Get<SoundEvent>( ext ) is not null ? ext : sound;
		_real[sound] = r;
		return r;
	}

	static int _swing;

	/// <summary> A swing's whoosh: one of three, pitched by how fast the form swings it. </summary>
	public static void Swing( Vector3 at, bool heavy, float speed, float crystalPitch )
	{
		var name = heavy ? SwingHeavy : ( _swing++ % 3 ) switch { 0 => Swing1, 1 => Swing2, _ => Swing3 };
		var h = Play( name, at );
		if ( h is not null ) h.Pitch = Math.Clamp( 0.85f + 0.2f * speed, 0.8f, 1.35f ) * crystalPitch;
	}
}

/// <summary>
/// Local lightsaber and Force effects (every machine draws its own; nothing networked): cuts burned into walls, impacts on
/// bodies and droids, clashes, parries, deflected bolts, and the Force powers. Kept within the shared spark and light
/// budgets of <see cref="SwrpFx"/>.
/// </summary>
public static class SaberFx
{
	const int MaxBurns = 240;
	static readonly Queue<GameObject> _burns = new();
	static Model _burnQuad;
	static Sprite _ring;

	static readonly Dictionary<Guid, RealTimeSince> _lastCrackle = new();

	static Sprite Ring => _ring ??= Sprite.FromTexture( Texture.Load( "materials/jedi/force_ring.png" ) );

	static GameObject Make( Scene scene, string name, Vector3 pos )
	{
		var go = scene.CreateObject();
		go.Name = name;
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos;
		return go;
	}

	// ----- burns -----

	/// <summary> A short glowing cut in a surface: molten for a moment, then a scorch that lasts (and fades after half a minute). </summary>
	public static void Burn( Scene scene, Vector3 at, Vector3 normal, Vector3 along, Color blade, float scale )
	{
		_burnQuad ??= BuildQuad( "materials/jedi/saber_burn.vmat" );
		if ( _burnQuad is not null )
		{
			var go = Make( scene, "saber_burn", at + normal * 0.3f );
			var side = Vector3.Cross( normal, along );
			if ( side.LengthSquared < 0.001f ) side = Vector3.Cross( normal, Vector3.Up );
			go.WorldRotation = Rotation.LookAt( normal, side.Normal );
			go.WorldScale = new Vector3( 1f, 2.6f * scale, 1.1f * scale );
			var r = go.Components.Create<ModelRenderer>();
			r.Model = _burnQuad;
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			go.Components.Create<ScorchFx>();
			_burns.Enqueue( go );
			while ( _burns.Count > MaxBurns )
			{
				var old = _burns.Dequeue();
				if ( old.IsValid() ) old.Destroy();
			}
		}
		// the molten edge: a white-orange line cooling to dull red
		var hot = Make( scene, "saber_burn_hot", at );
		var line = hot.Components.Create<LineRenderer>();
		line.UseVectorPoints = true;
		line.VectorPoints = new List<Vector3> { at - along * 1.5f * scale + normal * 0.4f, at + along * 1.5f * scale + normal * 0.4f };
		line.Additive = true;
		line.Lighting = false;
		line.CastShadows = false;
		line.Width = new Curve( new Curve.Frame( 0f, 0.55f * scale ), new Curve.Frame( 1f, 0.55f * scale ) );
		var glow = hot.Components.Create<FxCooling>();
		glow.Line = line;
		glow.Life = 1.8f;
	}

	public static void BurnSparks( Scene scene, Vector3 at, Vector3 normal, Color blade )
	{
		if ( SparkFx.Live < SwrpFx.MaxSparks )
			for ( var i = 0; i < 3; i++ )
				SparkFx.Spawn( scene, at + normal, ( normal + Vector3.Random * 0.8f ).Normal * Game.Random.Float( 90f, 260f ), new Color( 1f, 0.62f, 0.25f ) );
		SwrpFx.Glow( scene, at + normal * 1.5f, new Color( 1f, 0.55f, 0.2f ) * 1.6f, 4f, 10f, 0.12f );
		var h = SaberSounds.Play( SaberSounds.Burn, at );
		if ( h is not null ) h.Pitch = Game.Random.Float( 0.9f, 1.15f );
	}

	static Model BuildQuad( string material )
	{
		var mat = Material.Load( material );
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

	// ----- blows -----

	public static void Hit( Scene scene, SaberHit kind, Vector3 at, Vector3 motion, Color blade, GameObject victim )
	{
		var hot = Color.Lerp( blade, Color.White, 0.6f );
		switch ( kind )
		{
			case SaberHit.Flesh:
				SaberSounds.Play( SaberSounds.HitFlesh, at );
				SwrpFx.Glow( scene, at, new Color( 1f, 0.6f, 0.3f ) * 2f, 5f, 16f, 0.16f );
				BodyBurn( victim, at );
				Embers( scene, at, motion, 5 );
				break;
			case SaberHit.Droid:
				SaberSounds.Play( SaberSounds.HitDroid, at );
				SwrpFx.Glow( scene, at, new Color( 1f, 0.7f, 0.35f ) * 2.4f, 6f, 22f, 0.18f );
				for ( var i = 0; i < 9 && SparkFx.Live < SwrpFx.MaxSparks; i++ )
					SparkFx.Spawn( scene, at, ( motion + Vector3.Random * 0.9f + Vector3.Up * 0.3f ).Normal * Game.Random.Float( 150f, 420f ), new Color( 1f, 0.7f, 0.3f ) );
				BodyBurn( victim, at );
				break;
			case SaberHit.Object:
				SaberSounds.Play( SaberSounds.Burn, at );
				Embers( scene, at, motion, 4 );
				break;
			case SaberHit.Block:
			case SaberHit.Clash:
				SaberSounds.Play( kind == SaberHit.Block ? SaberSounds.Block : SaberSounds.Clash, at );
				Flash( scene, at, hot, kind == SaberHit.Clash ? 1.2f : 0.9f );
				break;
			case SaberHit.Parry:
				SaberSounds.Play( SaberSounds.Parry, at );
				Flash( scene, at, Color.White, 1.6f );
				break;
			case SaberHit.GuardBreak:
				SaberSounds.Play( SaberSounds.GuardBreak, at );
				Flash( scene, at, hot, 1.8f );
				SwrpShake.AddAt( at, 0.25f, 300f );
				break;
			case SaberHit.LockStart:
				SaberSounds.Play( SaberSounds.Clash, at );
				Flash( scene, at, hot, 2f );
				break;
			case SaberHit.LockWon:
				SaberSounds.Play( SaberSounds.GuardBreak, at );
				Flash( scene, at, hot, 1.6f );
				Shockwave( scene, at, motion, new Color( 0.8f, 0.9f, 1f ), 0.5f );
				break;
			case SaberHit.DroidDown:
				// a droid cut down: a shower of sparks from the cut, a molten glow, a pop of smoke and a jolt
				SaberSounds.Play( SaberSounds.HitDroid, at );
				SwrpFx.Glow( scene, at, new Color( 1f, 0.6f, 0.25f ) * 3f, 8f, 40f, 0.3f );
				for ( var i = 0; i < 18 && SparkFx.Live < SwrpFx.MaxSparks; i++ )
					SparkFx.Spawn( scene, at, ( motion * 0.5f + Vector3.Random + Vector3.Up * 0.5f ).Normal * Game.Random.Float( 160f, 520f ), new Color( 1f, 0.68f, 0.3f ) );
				if ( SwrpFx.Near( scene, at, SwrpFx.LightRange ) ) Make( scene, "saber_cut_smoke", at ).Components.Create<BlastSmoke>();
				SwrpShake.AddAt( at, 0.15f, 260f );
				BodyBurn( victim, at );
				break;
			case SaberHit.Catch:
				SaberSounds.Play( SaberSounds.Catch, at );
				break;
		}
	}

	/// <summary> A bright clash: sparks thrown everywhere, a flash and a light. </summary>
	static void Flash( Scene scene, Vector3 at, Color color, float power )
	{
		SwrpFx.Glow( scene, at, color * 3f * power, 6f, 30f * power, 0.14f );
		var n = Math.Min( (int)( 12 * power ), SwrpFx.MaxSparks - SparkFx.Live );
		for ( var i = 0; i < n; i++ )
			SparkFx.Spawn( scene, at, Vector3.Random.Normal * Game.Random.Float( 180f, 520f ) * power, Color.Lerp( color, new Color( 1f, 0.8f, 0.5f ), 0.5f ) );
		if ( SwrpFx.TakeLight( scene, at ) )
		{
			var go = Make( scene, "saber_flash", at );
			SwrpFx.AddLight( go, color * 6f * power, 220f );
			go.Components.Create<FxLife>().Life = 0.1f;
		}
		SwrpShake.AddAt( at, 0.1f * power, 200f );
	}

	public static void LockSparks( Scene scene, Vector3 at, Color color )
	{
		if ( SparkFx.Live < SwrpFx.MaxSparks )
			for ( var i = 0; i < 2; i++ )
				SparkFx.Spawn( scene, at, ( Vector3.Random + Vector3.Up * 0.4f ).Normal * Game.Random.Float( 120f, 340f ), new Color( 1f, 0.85f, 0.6f ) );
		SwrpFx.Glow( scene, at, Color.Lerp( color, Color.White, 0.6f ) * 2f, 4f, Game.Random.Float( 9f, 16f ), 0.06f );
	}

	static void Embers( Scene scene, Vector3 at, Vector3 motion, int count )
	{
		for ( var i = 0; i < count && SparkFx.Live < SwrpFx.MaxSparks; i++ )
			SparkFx.Spawn( scene, at, ( motion * 0.6f + Vector3.Random * 0.6f + Vector3.Up * 0.3f ).Normal * Game.Random.Float( 60f, 180f ), new Color( 1f, 0.45f, 0.15f ) );
	}

	/// <summary> A cauterised burn on a body where the blade passed: riding the nearest bone, so it moves with them. </summary>
	static void BodyBurn( GameObject victim, Vector3 at )
	{
		if ( victim is null ) return;
		var r = victim.Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
		_burnQuad ??= BuildQuad( "materials/jedi/saber_burn.vmat" );
		if ( r is null || _burnQuad is null || r.Model is null ) return;
		// the nearest bone when the body has bone objects (ragdolls do), else the body itself
		GameObject best = r.CreateBoneObjects ? null : r.GameObject;
		var bestD = float.MaxValue;
		if ( r.CreateBoneObjects ) foreach ( var bone in r.Model.Bones.AllBones )
		{
			var go = r.GetBoneObject( bone );
			if ( !go.IsValid() ) continue;
			var d = go.WorldPosition.DistanceSquared( at );
			if ( d < bestD ) { bestD = d; best = go; }
		}
		if ( best is null ) return;
		var mark = new GameObject( best, true, "saber_body_burn" );
		mark.NetworkMode = NetworkMode.Never;
		mark.Flags |= GameObjectFlags.NotSaved;
		var normal = ( at - best.WorldPosition ).Normal;
		mark.WorldPosition = at + normal * 0.5f;
		mark.WorldRotation = Rotation.LookAt( normal, Vector3.Cross( normal, Vector3.Up ).LengthSquared > 0.01f ? Vector3.Cross( normal, Vector3.Up ) : Vector3.Forward );
		mark.WorldScale = new Vector3( 1f, 2.2f, 0.9f );
		var mr = mark.Components.Create<ModelRenderer>();
		mr.Model = _burnQuad;
		mr.RenderType = ModelRenderer.ShadowRenderType.Off;
		mark.Components.Create<ScorchFx>();
	}

	// ----- bolts turned on the blade -----

	public static void Deflect( Scene scene, Vector3 from, Vector3 to, Vector3 normal, bool hit, bool hitWorld, Color boltColor, Color blade )
	{
		SaberSounds.Play( SaberSounds.Deflect, from );
		SwrpFx.Glow( scene, from, Color.Lerp( blade, Color.White, 0.5f ) * 2.6f, 5f, 18f, 0.1f );
		for ( var i = 0; i < 4 && SparkFx.Live < SwrpFx.MaxSparks; i++ )
			SparkFx.Spawn( scene, from, Vector3.Random.Normal * Game.Random.Float( 120f, 300f ), boltColor );
		BlasterBolt.Spawn( scene, from, to, boltColor );
		if ( hit )
		{
			SwrpFx.Impact( scene, to, normal, boltColor, hitWorld );
			Sound.Play( SwrpAudio.BlasterImpact, to );
		}
	}

	// ----- the Force -----

	public static void Force( Scene scene, JediCombat who, int power, Vector3 at, Vector3 dir, float size, GameObject target )
	{
		var tint = new Color( 0.75f, 0.88f, 1f );
		switch ( power )
		{
			case JediForce.Push:
				SaberSounds.Play( SaberSounds.Push, at );
				Shockwave( scene, at, dir, tint, 1f );
				SwrpShake.AddAt( at, 0.2f, 400f );
				break;
			case JediForce.Pull:
				SaberSounds.Play( SaberSounds.Pull, at );
				Stream( scene, at, dir, tint );
				break;
			case JediForce.Leap:
				SaberSounds.Play( SaberSounds.Leap, at );
				ForceDust( scene, at, 1f );
				break;
			case JediForce.Speed:
				SaberSounds.Play( SaberSounds.Speed, at );
				if ( who.IsValid() ) who.GameObject.Components.GetOrCreate<FxSpeedTrail>().Run( size );
				break;
			case JediForce.Heal:
				SaberSounds.Play( SaberSounds.Heal, at );
				if ( target.IsValid() ) target.Components.GetOrCreate<FxHealAura>().Run( size );
				break;
			case JediForce.Lightning:
				{
					// the channel ticks every 0.15 s but the crackle is 0.4 s long: one at a time per caster, or they pile into a roar
					var key = who.IsValid() ? who.GameObject.Id : Guid.Empty;
					if ( !_lastCrackle.TryGetValue( key, out var since ) || since > 0.32f )
					{
						_lastCrackle[key] = 0;
						var h = SaberSounds.Play( SaberSounds.Lightning, at );
						if ( h is not null ) h.Pitch = Game.Random.Float( 0.9f, 1.1f );
					}
					var go = Make( scene, "force_lightning", at );
					var bolt = go.Components.Create<FxLightning>();
					bolt.From = at;
					bolt.To = at + dir;
					bolt.Life = 0.24f;
					break;
				}
		}
	}

	/// <summary> An air ripple rolling out ahead: soft rings and haze swelling as they go. </summary>
	static void Shockwave( Scene scene, Vector3 at, Vector3 dir, Color tint, float power )
	{
		var ring = Ring;
		// a ripple in the air close to the hand: faint rings swelling as they roll out, the first quick and bright, the
		// rest following (more a bend in the air than a solid hoop)
		var air = new Color( 0.72f, 0.84f, 1f );
		for ( var i = 0; i < 3; i++ )
		{
			var go = Make( scene, "force_wave", at + dir * i * 6f );
			var r = go.Components.Create<SpriteRenderer>();
			r.Sprite = ring ?? SwrpFx.GlowSprite;
			r.Additive = true;
			r.Lighting = false;
			r.Shadows = false;
			r.Color = air.WithAlpha( 0f );
			var m = go.Components.Create<FxMote>();
			m.Velocity = dir * ( 340f + i * 70f ) * power;
			m.Drag = 2.5f;
			m.Size0 = 6f + i * 3f;
			m.Size1 = ( 46f + i * 10f ) * power;
			m.Life = 0.28f + i * 0.05f;
			m.Tint = air.WithAlpha( 0.22f - i * 0.035f );
		}
		for ( var i = 0; i < 10; i++ )
		{
			var spread = ( dir + Vector3.Random * 0.45f ).Normal;
			Mote( scene, at, spread * Game.Random.Float( 300f, 520f ) * power, tint.WithAlpha( 0.1f ), 5f, 28f, 0.35f, drag: 2f );
		}
	}

	/// <summary> Haze drawn in along a line to the hand (Pull). </summary>
	static void Stream( Scene scene, Vector3 from, Vector3 toHand, Color tint )
	{
		// haze torn off the target and drawn in along the line to the hand, and a ring closing on the target
		for ( var i = 0; i < 18; i++ )
		{
			var p = from + Vector3.Random * 18f + toHand * Game.Random.Float( 0f, 0.25f );
			Mote( scene, p, toHand / 0.42f * Game.Random.Float( 0.75f, 1.1f ), tint.WithAlpha( 0.36f ), 16f, 6f, 0.42f );
		}
		// the pull itself: a taut thread of the Force from the target to the hand, rippling and fading
		var thread = Make( scene, "force_pull_thread", from );
		var t = thread.Components.Create<FxThread>();
		t.From = from;
		t.To = from + toHand;
		t.Tint = tint;
		var ring = Ring;
		if ( ring is not null )
		{
			var go = Make( scene, "force_pull_ring", from );
			var r = go.Components.Create<SpriteRenderer>();
			r.Sprite = ring;
			r.Additive = true;
			r.Lighting = false;
			r.Shadows = false;
			var m = go.Components.Create<FxMote>();
			m.Size0 = 60f;
			m.Size1 = 8f;
			m.Life = 0.35f;
			m.Tint = tint.WithAlpha( 0.3f );
		}
	}

	public static void ForceDust( Scene scene, Vector3 at, float power )
	{
		for ( var i = 0; i < (int)( 10 * power ); i++ )
		{
			var a = i / ( 10f * power ) * MathF.PI * 2f;
			var v = new Vector3( MathF.Cos( a ), MathF.Sin( a ), 0.1f ) * Game.Random.Float( 160f, 260f ) * power;
			Mote( scene, at + Vector3.Up * 3f, v, new Color( 0.75f, 0.72f, 0.66f, 0.18f ), 8f, 34f, 0.6f, drag: 3f );
		}
	}

	public static void Mote( Scene scene, Vector3 at, Vector3 velocity, Color color, float size0, float size1, float life, float drag = 0f )
	{
		var sprite = SwrpFx.GlowSprite;
		if ( sprite is null ) return;
		var go = Make( scene, "force_mote", at );
		var r = go.Components.Create<SpriteRenderer>();
		r.Sprite = sprite;
		r.Additive = true;
		r.Lighting = false;
		r.Shadows = false;
		r.Color = color;
		r.Size = new Vector2( size0, size0 );
		var m = go.Components.Create<FxMote>();
		m.Velocity = velocity;
		m.Size0 = size0;
		m.Size1 = size1;
		m.Life = life;
		m.Tint = color;
		m.Drag = drag;
	}
}

/// <summary> A sprite drifting, growing and fading out. </summary>
public sealed class FxMote : Component
{
	public Vector3 Velocity { get; set; }
	public float Size0 { get; set; }
	public float Size1 { get; set; }
	public float Life { get; set; } = 0.4f;
	public float Drag { get; set; }
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
		var t = _age / Life;
		if ( t >= 1f ) { GameObject.Destroy(); return; }
		Velocity *= MathF.Max( 0f, 1f - Drag * Time.Delta );
		WorldPosition += Velocity * Time.Delta;
		if ( !_r.IsValid() ) return;
		var size = MathX.Lerp( Size0, Size1, 1f - ( 1f - t ) * ( 1f - t ) );
		_r.Size = new Vector2( size, size );
		_r.Color = Tint.WithAlpha( Tint.a * ( 1f - t ) );
	}
}

/// <summary> Force Pull's thread: a rippling pale line between two points that thins and fades. </summary>
public sealed class FxThread : Component
{
	public Vector3 From { get; set; }
	public Vector3 To { get; set; }
	public Color Tint { get; set; }
	public float Life { get; set; } = 0.4f;
	TimeSince _age;
	LineRenderer _line;

	protected override void OnStart()
	{
		_age = 0;
		_line = Components.Create<LineRenderer>();
		_line.UseVectorPoints = true;
		_line.Additive = true;
		_line.Lighting = false;
		_line.CastShadows = false;
	}

	protected override void OnUpdate()
	{
		var t = _age / Life;
		if ( t >= 1f ) { GameObject.Destroy(); return; }
		var span = To - From;
		var side = Vector3.Cross( span.Normal, Vector3.Up ).Normal;
		var pts = new List<Vector3>();
		for ( var i = 0; i <= 12; i++ )
		{
			var u = i / 12f;
			var wave = MathF.Sin( u * 7f - _age * 26f ) * MathF.Sin( u * MathF.PI ) * 9f * ( 1f - t * 0.5f );
			pts.Add( From + span * u + side * wave + Vector3.Up * wave * 0.5f );
		}
		_line.VectorPoints = pts;
		var a = ( 1f - t ) * 0.28f;      // (a pale tug in the air, not a laser)
		_line.Color = new Gradient( new Gradient.ColorFrame( 0f, ( Tint * 1.2f ).WithAlpha( a * 0.3f ) ), new Gradient.ColorFrame( 1f, ( Tint * 1.5f ).WithAlpha( a ) ) );
		_line.Width = new Curve( new Curve.Frame( 0f, 3.5f * ( 1f - t ) ), new Curve.Frame( 1f, 1.2f ) );
	}
}

/// <summary> A molten cut cooling: white-orange to dull red to nothing. </summary>
public sealed class FxCooling : Component
{
	public LineRenderer Line { get; set; }
	public float Life { get; set; } = 1.5f;
	TimeSince _age;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var t = _age / Life;
		if ( t >= 1f ) { GameObject.Destroy(); return; }
		if ( !Line.IsValid() ) return;
		var c = Color.Lerp( new Color( 1f, 0.85f, 0.55f ) * 2.4f, new Color( 0.7f, 0.12f, 0.02f ) * 1.2f, MathF.Sqrt( t ) ).WithAlpha( 1f - t * t );
		Line.Color = new Gradient( new Gradient.ColorFrame( 0f, c.WithAlpha( 0f ) ), new Gradient.ColorFrame( 0.2f, c ), new Gradient.ColorFrame( 0.8f, c ), new Gradient.ColorFrame( 1f, c.WithAlpha( 0f ) ) );
	}
}

/// <summary> Force Lightning: forked, flickering arcs from the hand to the target, rebuilt every frame for a moment, lighting the room. </summary>
public sealed class FxLightning : Component
{
	public Vector3 From { get; set; }
	public Vector3 To { get; set; }
	public float Life { get; set; } = 0.25f;
	TimeSince _age;
	readonly List<LineRenderer> _arcs = new();
	PointLight _light;

	protected override void OnStart()
	{
		_age = 0;
		for ( var i = 0; i < 3; i++ )
		{
			var go = new GameObject( GameObject, true, "arc" );
			go.NetworkMode = NetworkMode.Never;
			var l = go.Components.Create<LineRenderer>();
			l.UseVectorPoints = true;
			l.Additive = true;
			l.Lighting = false;
			l.CastShadows = false;
			// thin blue-violet arcs with a white-hot core on the main one (a glow line under each would double the cost)
			l.Color = new Gradient( new Gradient.ColorFrame( 0f, ( i == 0 ? new Color( 0.85f, 0.88f, 1f ) : new Color( 0.6f, 0.62f, 1f ) ) * 3f ), new Gradient.ColorFrame( 1f, new Color( 0.5f, 0.45f, 1f ) * 2.2f ) );
			l.Width = new Curve( new Curve.Frame( 0f, i == 0 ? 0.5f : 0.28f ), new Curve.Frame( 1f, 0.15f ) );
			_arcs.Add( l );
		}
		if ( SwrpFx.TakeLight( Scene, From ) ) _light = SwrpFx.AddLight( GameObject, new Color( 0.6f, 0.65f, 1f ) * 5f, 260f );
	}

	protected override void OnUpdate()
	{
		if ( _age > Life ) { GameObject.Destroy(); return; }
		var span = To - From;
		var len = span.Length;
		var dir = span / MathF.Max( 1f, len );
		var side = Vector3.Cross( dir, Vector3.Up ).Normal;
		var up = Vector3.Cross( side, dir ).Normal;
		for ( var a = 0; a < _arcs.Count; a++ )
		{
			var pts = new List<Vector3>();
			var segs = 20;
			var end = a == 0 ? To : From + span * Game.Random.Float( 0.45f, 0.85f ) + ( side * Game.Random.Float( -1f, 1f ) + up * Game.Random.Float( -1f, 1f ) ) * len * 0.12f;
			for ( var i = 0; i <= segs; i++ )
			{
				var t = i / (float)segs;
				var jag = MathF.Sin( t * MathF.PI ) * len * 0.035f;
				pts.Add( Vector3.Lerp( From, end, t ) + ( side * Game.Random.Float( -1f, 1f ) + up * Game.Random.Float( -1f, 1f ) ) * jag );
			}
			_arcs[a].VectorPoints = pts;
		}
		if ( _light.IsValid() )
		{
			_light.WorldPosition = Vector3.Lerp( From, To, 0.5f );
			_light.LightColor = new Color( 0.6f, 0.65f, 1f ) * Game.Random.Float( 2f, 7f );
		}
	}
}

/// <summary> Force Speed: faint after-images of blue haze left behind while moving fast. </summary>
public sealed class FxSpeedTrail : Component
{
	RealTimeUntil _end;
	RealTimeSince _sinceMote;
	Vector3 _last;

	public void Run( float seconds )
	{
		_end = seconds;
		_last = WorldPosition;
	}

	protected override void OnUpdate()
	{
		if ( _end <= 0f ) { Destroy(); return; }
		var moved = WorldPosition.Distance( _last );
		_last = WorldPosition;
		if ( _sinceMote < 0.04f || moved < 0.5f ) return;
		_sinceMote = 0;
		var scale = WorldScale.z;
		for ( var i = 0; i < 2; i++ )
			SaberFx.Mote( Scene, WorldPosition + Vector3.Up * Game.Random.Float( 20f, 60f ) * scale + Vector3.Random * 6f, Vector3.Zero, new Color( 0.5f, 0.75f, 1f, 0.14f ), 22f, 30f, 0.3f );
	}
}

/// <summary> Force Heal: soft motes rising round the body for a few seconds. </summary>
public sealed class FxHealAura : Component
{
	RealTimeUntil _end;
	RealTimeSince _sinceMote;

	public void Run( float seconds ) => _end = seconds;

	protected override void OnUpdate()
	{
		if ( _end <= 0f ) { Destroy(); return; }
		if ( _sinceMote < 0.07f ) return;
		_sinceMote = 0;
		var a = Game.Random.Float( 0f, MathF.PI * 2f );
		var at = WorldPosition + new Vector3( MathF.Cos( a ) * 14f, MathF.Sin( a ) * 14f, Game.Random.Float( 5f, 50f ) );
		SaberFx.Mote( Scene, at, Vector3.Up * Game.Random.Float( 20f, 45f ), new Color( 0.7f, 1f, 0.78f, 0.45f ), 7f, 2f, 1.1f );
	}
}

/// <summary> The local view's kick when a saber blow lands, and the wider view of Force Speed. </summary>
public static class JediCamera
{
	static float _fov;

	/// <summary> A jolt of the view (0..1) and a quick widening of the field of view by <paramref name="fovKick"/> degrees. </summary>
	public static void Punch( float shake, float fovKick )
	{
		if ( !CinematicCamera.SwayEnabled ) return;
		SwrpShake.Add( shake * 0.35f );
		_fov = MathF.Max( _fov, fovKick );
	}

	/// <summary> Degrees to add to the view this frame (decays). </summary>
	public static float Fov( float speedBoost )
	{
		_fov = MathF.Max( 0f, _fov - Time.Delta * 22f );
		return _fov + speedBoost;
	}
}

/// <summary>
/// A blow's recoil on a body that has no saber rig posing it (a trooper, a droid, an NPC): the spine and head snap away
/// from the hit and settle back over a third of a second, on top of whatever the animation plays. A Jedi whose rig is
/// posing takes it through the rig instead.
/// </summary>
public sealed class FxFlinch : Component
{
	SkinnedModelRenderer _r;
	Vector3 _axis;
	float _kick;
	TimeSince _age;
	readonly List<GameObject> _taken = new();
	static readonly (string Bone, float Share)[] Bones = { ( "spine_0", 0.25f ), ( "spine_1", 0.3f ), ( "spine_2", 0.3f ), ( "head", 0.25f ) };

	public static void Hit( GameObject victim, Vector3 at, Vector3 motion )
	{
		if ( victim is null ) return;
		var r = victim.Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
		if ( r is null || r.Model is null ) return;
		var rig = r.Components.Get<SaberRig>( FindMode.EverythingInSelf );
		var away = ( r.WorldPosition - at ).WithZ( 0 );
		if ( away.LengthSquared < 0.01f ) away = motion.WithZ( 0 );
		if ( rig is not null && rig.Active && rig.Posing ) { rig.Flinch( -away.Normal ); return; }
		var f = r.Components.Get<FxFlinch>( FindMode.EverythingInSelf ) ?? r.Components.Create<FxFlinch>();
		f._r = r;
		// tip away from the blow: round the axis across it
		f._axis = Vector3.Cross( Vector3.Up, away.Normal ).Normal;
		f._kick = 26f;
		f._age = 0;
	}

	protected override void OnUpdate()
	{
		if ( !_r.IsValid() ) { Destroy(); return; }
		const float Life = 0.35f;
		if ( _age > Life ) { Release(); Destroy(); return; }
		if ( !_r.CreateBoneObjects ) _r.CreateBoneObjects = true;
		// snap out fast, ease back
		var t = _age / Life;
		var amount = _kick * ( t < 0.15f ? t / 0.15f : 1f - ( t - 0.15f ) / 0.85f );
		foreach ( var (name, share) in Bones )
		{
			var bone = _r.Model.Bones.GetBone( name );
			var go = bone is null ? null : _r.GetBoneObject( bone );
			if ( !go.IsValid() || !_r.TryGetBoneTransformAnimation( in bone, out var anim ) ) continue;
			if ( !go.Flags.HasFlag( GameObjectFlags.ProceduralBone ) ) { go.Flags |= GameObjectFlags.ProceduralBone; _taken.Add( go ); }
			go.WorldRotation = Rotation.FromAxis( _axis, amount * share ) * anim.Rotation;
		}
	}

	void Release()
	{
		foreach ( var go in _taken )
			if ( go.IsValid() ) go.Flags &= ~GameObjectFlags.ProceduralBone;
		_taken.Clear();
	}

	protected override void OnDestroy() => Release();
}
