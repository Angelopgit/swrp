using System;

namespace Swrp;

/// <summary>
/// A thermal detonator. Thrown by a trooper (the Grenade key, T by default, or Throw on it in the inventory) or by an NPC
/// (SwrpNpc decides when). It arms with a chirp, bounces about, and its red lamp blinks and beeps faster as the
/// <see cref="Fuse"/> burns down, then it goes off: deadly at your feet, falling away to nothing at <see cref="Radius"/>, and
/// only for those with a clear line to it (walls and cover stop it), with the blasters' friendly-fire rule (swrp_friendlyfire;
/// the thrower is never spared). The host owns it and deals the damage; every machine draws its own flash, sparks, smoke,
/// scorch, shake and ringing ears. The detonator itself is an original, built from primitives.
/// </summary>
[Title( "SWRP Thermal Detonator" ), Category( "SWRP" ), Icon( "brightness_high" )]
public sealed class SwrpGrenade : Component
{
	public const string ItemId = "thermal_detonator";
	public const string WeaponName = "Thermal Detonator";

	/// <summary> Seconds from the throw to the blast. </summary>
	public const float Fuse = 3f;

	/// <summary> The blast reaches this far; closer than <see cref="LethalRadius"/> it kills outright. </summary>
	public const float Radius = 380f;
	public const float LethalRadius = 60f;
	public const float MaxDamage = 150f;

	/// <summary> The thrower's side (friendly fire, and so NPCs know whose it is). </summary>
	[Sync( SyncFlags.FromHost )] public string Allegiance { get; set; }
	[Sync( SyncFlags.FromHost )] public string ThrowerName { get; set; }

	/// <summary> Host: who threw it (a trooper gets the kill credit). </summary>
	public SwrpPlayer Thrower { get; set; }
	public SwrpNpc NpcThrower { get; set; }

	/// <summary> Seconds since it appeared on this machine. </summary>
	public float Age => _age;

	TimeSince _age;
	bool _exploded;
	SpriteRenderer _lamp;
	PointLight _lampLight;
	float _blinkAt;
	bool _lit;

	/// <summary> Host: throw one from <paramref name="from"/>. </summary>
	public static SwrpGrenade Throw( Scene scene, Vector3 from, Vector3 velocity, string allegiance, string throwerName, SwrpPlayer player = null, SwrpNpc npc = null )
	{
		var go = new GameObject( true, "Thermal Detonator" );
		go.WorldPosition = from;
		go.WorldRotation = Rotation.Random;
		go.Tags.Add( "grenade" );
		var g = go.AddComponent<SwrpGrenade>();
		g.Allegiance = allegiance ?? "";
		g.ThrowerName = throwerName ?? "";
		g.Thrower = player;
		g.NpcThrower = npc;

		var col = go.AddComponent<SphereCollider>();
		col.Radius = 2.6f;
		col.Elasticity = 0.35f;
		col.Friction = 0.6f;
		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 3f;
		rb.LinearDamping = 0f;
		rb.AngularDamping = 0.8f;
		rb.Velocity = velocity;
		rb.AngularVelocity = Vector3.Random * 10f;
		go.NetworkSpawn();
		return g;
	}

	protected override void OnStart()
	{
		_age = 0;

		// a dark metal ball with a raised band, a trigger cap and a red arming lamp
		Part( "Shell", "models/dev/sphere.vmdl", new Vector3( 5f ), Vector3.Zero, new Color( 0.34f, 0.35f, 0.37f ) );
		Part( "Band", "models/dev/sphere.vmdl", new Vector3( 5.5f, 5.5f, 1.3f ), Vector3.Zero, new Color( 0.16f, 0.16f, 0.18f ) );
		Part( "Cap", "models/dev/box.vmdl", new Vector3( 1.8f, 1.8f, 1.2f ), Vector3.Up * 2.5f, new Color( 0.55f, 0.55f, 0.58f ) );

		var lamp = new GameObject( GameObject, true, "Lamp" );
		lamp.NetworkMode = NetworkMode.Never;
		lamp.Flags |= GameObjectFlags.NotSaved;
		lamp.LocalPosition = new Vector3( 2.4f, 0f, 0.9f );
		if ( SwrpFx.GlowSprite is { } sprite )
		{
			_lamp = lamp.Components.Create<SpriteRenderer>();
			_lamp.Sprite = sprite;
			_lamp.Additive = true;
			_lamp.Lighting = false;
			_lamp.Shadows = false;
			_lamp.Size = new Vector2( 3.5f, 3.5f );
		}
		if ( SwrpFx.Near( Scene, WorldPosition, 1800f ) )
		{
			_lampLight = lamp.Components.Create<PointLight>();
			_lampLight.Radius = 40f;
			_lampLight.Shadows = false;
			_lampLight.LightColor = Color.Black;
		}
		Blink( true );
		Sound.Play( SwrpAudio.DetonatorArm, WorldPosition );
	}

	void Part( string name, string model, Vector3 size, Vector3 pos, Color tint )
	{
		var m = Model.Load( model );
		var unit = m.Bounds.Size;
		var go = new GameObject( GameObject, true, name );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.LocalPosition = pos;
		go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
		var r = go.AddComponent<ModelRenderer>();
		r.Model = m;
		r.Tint = tint;
	}

	protected override void OnUpdate()
	{
		// the lamp blinks and beeps faster and faster as the fuse runs down
		if ( _age < _blinkAt ) return;
		var k = Math.Clamp( _age / Fuse, 0f, 1f );
		var period = MathX.Lerp( 0.6f, 0.08f, MathF.Pow( k, 1.4f ) );
		Blink( !_lit );
		_blinkAt = _age + ( _lit ? MathF.Min( 0.07f, period * 0.5f ) : period );
		if ( _lit && k < 0.97f ) Sound.Play( SwrpAudio.DetonatorBeep, WorldPosition );
	}

	void Blink( bool on )
	{
		_lit = on;
		var red = new Color( 1f, 0.12f, 0.08f );
		if ( _lamp.IsValid() ) _lamp.Color = on ? red * 4f : red.WithAlpha( 0.25f ) * 0.4f;
		if ( _lampLight.IsValid() ) _lampLight.LightColor = on ? red * 1.6f : Color.Black;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || _exploded || _age < Fuse ) return;
		Explode();
	}

	// ----- host: the blast -----

	void Explode()
	{
		_exploded = true;
		var pos = WorldPosition + Vector3.Up * 4f;
		var scene = Scene;

		// troopers and training dummies
		foreach ( var v in scene.GetAllComponents<SwrpVitals>().ToList() )
		{
			if ( v.IsDead ) continue;
			var victim = v.GetComponent<SwrpPlayer>();
			var amount = DamageAt( pos, v.WorldPosition + Vector3.Up * 32f, v.GameObject );
			if ( amount <= 0f ) continue;
			if ( victim.IsValid() && victim != Thrower && !SwrpCombat.FriendlyFire && AllegianceRegistry.AreFriendly( Allegiance, victim.Allegiance ) ) continue;
			v.TakeDamage( amount, Thrower, pos, WeaponName, false, ThrowerName );
		}

		// NPCs
		foreach ( var n in scene.GetAllComponents<SwrpNpc>().ToList() )
		{
			if ( n.IsDead ) continue;
			var amount = DamageAt( pos, n.WorldPosition + Vector3.Up * 32f * n.WorldScale.z, n.GameObject );
			if ( amount <= 0f ) continue;
			if ( n != NpcThrower && !SwrpCombat.FriendlyFire && AllegianceRegistry.AreFriendly( Allegiance, n.Allegiance ) ) continue;
			n.TakeHit( amount, Thrower, ThrowerName, pos, WeaponName, false, false, 0f );
		}

		Swrp.Flight.SwrpFlightShip.HostBlast( scene, pos, Radius, 0f, MaxDamage * 1.5f, 0f, Thrower, WeaponName );     // and flyable craft caught in it
		var floor = scene.Trace.Ray( pos, pos + Vector3.Down * 48f ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc", "ragdoll", "trigger", "grenade", "worlditem" ).Run();
		ShowBlast( pos, floor.Hit ? floor.HitPosition : Vector3.Zero, floor.Hit ? floor.Normal : Vector3.Up );
		GameObject.Destroy();
	}

	/// <summary> Damage to something at <paramref name="point"/>: nothing behind a wall, everything at your feet. </summary>
	float DamageAt( Vector3 pos, Vector3 point, GameObject who )
	{
		var d = point.Distance( pos );
		if ( d >= Radius ) return 0f;
		var tr = Scene.Trace.Ray( pos, point ).IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( who )
			.WithoutTags( "player", "npc", "ragdoll", "trigger", "grenade", "worlditem" ).Run();
		if ( tr.Hit ) return 0f;
		if ( d <= LethalRadius ) return 500f;
		var k = 1f - ( d - LethalRadius ) / ( Radius - LethalRadius );
		return MaxDamage * k * k;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowBlast( Vector3 pos, Vector3 floorPoint, Vector3 floorNormal )
	{
		if ( Game.ActiveScene is { } scene ) SwrpFx.Explosion( scene, pos, floorPoint, floorNormal, Radius );
	}

	[ConCmd( "swrp_grenade_test", Help = "swrp_grenade_test - host only: throw a thermal detonator from the camera (it goes off after the fuse)" )]
	public static void TestCommand()
	{
		if ( !Networking.IsHost || Game.ActiveScene?.Camera is not { } cam ) return;
		var fwd = cam.WorldRotation.Forward;
		Throw( cam.Scene, cam.WorldPosition + fwd * 40f, fwd * 700f + Vector3.Up * 160f, "", "Test" );
	}
}
