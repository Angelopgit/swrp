using System;

namespace Swrp.Flight;

/// <summary>
/// A proton torpedo or mass-driver missile. Fired with a lock it homes on the target (each screen's copy steers toward the
/// same networked target, so they stay together); without one it flies dead straight. The host's copy does the hitting and
/// its blast message ends them all. Big and loud (Angelo, 2026-10-21: "triple-A star wars missile and bigger explosion"):
/// a hot core with a long glowing streak and a smoke trail behind it, and a blast with a flash, a shockwave, a large fireball,
/// a dust ring, heavy debris, Battlefront II's own explosion sounds and a camera shake that reaches the cockpit.
/// </summary>
public sealed class FlightMissile : Component
{
	static int _nextId;
	static readonly Dictionary<int, FlightMissile> _live = new();

	public int MissileId;
	public bool HostCopy;
	public Vector3 Direction;
	public float Speed;
	public float Life = 5f;
	public Color Color;
	/// <summary> What it homes on (null: straight). </summary>
	public GameObject Target;

	/// <summary> How hard a locked missile turns toward its target (deg/s). </summary>
	public const float TurnRate = 75f;

	// host only
	public SwrpFlightShip From;
	public SwrpPlayer Shooter;
	public WeaponDef Weapon;

	TimeSince _age;
	LineRenderer _trail, _smoke;
	SpriteRenderer _glow, _core;
	PointLight _light;
	readonly List<Vector3> _points = new(), _smokePoints = new();

	/// <summary> Host: fire one along a line (the launching craft's speed is added so it leaves the rails ahead). </summary>
	public static void HostLaunch( Scene scene, SwrpFlightShip from, SwrpPlayer shooter, WeaponDef w, Vector3 pos, Vector3 dir, Vector3 shipVelocity, Guid target = default )
	{
		var speed = w.Speed + MathF.Max( 0f, Vector3.Dot( shipVelocity, dir ) );
		var id = ++_nextId;
		var life = w.Range / speed * ( target == Guid.Empty ? 1f : 1.4f );
		var m = Make( scene, id, pos, dir, speed, w.BoltColor, life, target );
		m.HostCopy = true;
		m.From = from;
		m.Shooter = shooter;
		m.Weapon = w;
		Launched( id, pos, dir, speed, w.BoltColor, life, w.Sound, target );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Launched( int id, Vector3 pos, Vector3 dir, float speed, Color color, float life, string sound, Guid target )
	{
		if ( sound is not null ) { var h = Sound.Play( sound, pos ); if ( h is not null ) h.Volume *= 1.4f; }
		if ( Networking.IsHost || Game.ActiveScene is not { } scene ) return;          // the host already has its copy
		Make( scene, id, pos, dir, speed, color, life, target );
	}

	static FlightMissile Make( Scene scene, int id, Vector3 pos, Vector3 dir, float speed, Color color, float life, Guid target )
	{
		var go = scene.CreateObject();
		go.Name = "flight_missile";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = pos;
		go.WorldRotation = Rotation.LookAt( dir );
		var m = go.Components.Create<FlightMissile>();
		m.MissileId = id;
		m.Direction = dir.Normal;
		m.Speed = speed;
		m.Color = color;
		m.Life = life;
		m.Target = target == Guid.Empty ? null : scene.Directory.FindByGuid( target );
		_live[id] = m;
		return m;
	}

	protected override void OnStart()
	{
		_age = 0;
		_points.Add( WorldPosition );
		_points.Add( WorldPosition );
		_smokePoints.Add( WorldPosition );
		_smokePoints.Add( WorldPosition );
		// the smoke it leaves: wide, grey, lingering
		_smoke = Components.Create<LineRenderer>();
		_smoke.UseVectorPoints = true;
		_smoke.VectorPoints = _smokePoints;
		_smoke.Color = new Gradient( new Gradient.ColorFrame( 0f, new Color( 0.55f, 0.55f, 0.57f, 0f ) ), new Gradient.ColorFrame( 0.7f, new Color( 0.62f, 0.62f, 0.64f, 0.35f ) ), new Gradient.ColorFrame( 1f, new Color( 0.8f, 0.78f, 0.75f, 0.5f ) ) );
		_smoke.Width = new Curve( new Curve.Frame( 0f, 70f ), new Curve.Frame( 1f, 16f ) );
		_smoke.Additive = false;
		_smoke.Lighting = true;
		_smoke.CastShadows = false;
		// the hot streak right behind it
		_trail = Components.Create<LineRenderer>();
		_trail.UseVectorPoints = true;
		_trail.VectorPoints = _points;
		_trail.Color = new Gradient( new Gradient.ColorFrame( 0f, new Color( 0.8f, 0.8f, 0.8f, 0f ) ), new Gradient.ColorFrame( 1f, ( Color * 2.5f ).WithAlpha( 1f ) ) );
		_trail.Width = new Curve( new Curve.Frame( 0f, 6f ), new Curve.Frame( 1f, 34f ) );
		_trail.Additive = true;
		_trail.Lighting = false;
		_trail.CastShadows = false;
		_glow = Components.Create<SpriteRenderer>();
		_glow.Sprite = SwrpFx.GlowSprite;
		_glow.Color = Color * 6f;
		_glow.Size = 170f;
		_glow.Additive = true;
		_core = Components.Create<SpriteRenderer>();
		_core.Sprite = SwrpFx.GlowSprite;
		_core.Color = new Color( 1f, 0.95f, 0.85f ) * 8f;
		_core.Size = 48f;
		_core.Additive = true;
		if ( SwrpFx.TakeLight( Scene, WorldPosition ) ) _light = SwrpFx.AddLight( GameObject, Color * 5f, 700f );
	}

	protected override void OnUpdate()
	{
		// a locked missile turns onto its target (the same on every screen: the target is networked)
		if ( Target.IsValid() && _age > 0.15f )
		{
			var want = ( Target.WorldPosition + Vector3.Up * 40f - WorldPosition ).Normal;
			var gap = Vector3.GetAngle( Direction, want );
			if ( gap > 0.01f && gap < 100f ) Direction = Vector3.Slerp( Direction, want, MathF.Min( 1f, TurnRate * Time.Delta / gap ) ).Normal;
			WorldRotation = Rotation.LookAt( Direction );
		}

		var step = Direction * Speed * Time.Delta;
		var from = WorldPosition;
		var to = from + step;

		var tr = Scene.Trace.Ray( from, to ).WithoutTags( "trigger", "debris" );
		if ( HostCopy && From.IsValid() ) tr = tr.IgnoreGameObjectHierarchy( From.GameObject );
		var hit = _age > 0.02f ? tr.Run() : default;
		if ( HostCopy && From.IsValid() )
			for ( var i = 0; i < ( From.Def?.Seats.Length ?? 0 ); i++ )
				if ( hit.Hit && From.PlayerIn( i ) is { } rider && rider.IsValid() && hit.GameObject?.Root == rider.GameObject.Root ) hit = default;
		// a locked missile that reaches its target bursts beside it even if the trace slips past
		if ( HostCopy && !hit.Hit && Target.IsValid() && to.Distance( Target.WorldPosition + Vector3.Up * 40f ) < 90f ) { HostExplode( to, default ); return; }

		if ( hit.Hit || _age > Life )
		{
			var at = hit.Hit ? hit.HitPosition : to;
			if ( HostCopy ) HostExplode( at, hit );
			else if ( hit.Hit ) Fade();                                      // wait out of sight for the host's blast
			else GameObject.Destroy();
			return;
		}

		WorldPosition = to;
		// the streak 30 points long, the smoke 90
		_points.Add( to );
		if ( _points.Count > 30 ) _points.RemoveAt( 0 );
		_trail.VectorPoints = _points;
		_smokePoints.Add( to - Direction * 60f );
		if ( _smokePoints.Count > 90 ) _smokePoints.RemoveAt( 0 );
		_smoke.VectorPoints = _smokePoints;
		_core.Size = 40f + 14f * MathF.Sin( _age * 60f );
	}

	void Fade()
	{
		if ( _glow.IsValid() ) _glow.Enabled = false;
		if ( _core.IsValid() ) _core.Enabled = false;
		if ( _light.IsValid() ) _light.Enabled = false;
		Speed = 0f;
		if ( _age > Life + 2f ) GameObject.Destroy();
	}

	void HostExplode( Vector3 at, SceneTraceResult hit )
	{
		var w = Weapon;
		if ( hit.Hit ) SwrpFlightShip.HostHit( hit, w.Damage, w.ShipDamage, Shooter, w.Name, 0f );
		SwrpFlightShip.HostBlast( Scene, at, w.BlastRadius, w.Damage * 0.6f, w.ShipDamage * 0.5f, 0f, Shooter, w.Name, hit.Hit && hit.GameObject?.Components.GetInAncestorsOrSelf<SwrpFlightShip>() is { } s ? s.GameObject : null );
		Boom( MissileId, at, hit.Hit ? hit.Normal : Vector3.Up, w.BlastRadius );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Boom( int id, Vector3 at, Vector3 normal, float radius )
	{
		if ( _live.Remove( id, out var m ) && m.IsValid() ) m.GameObject.Destroy();
		if ( Game.ActiveScene is not { } scene ) return;
		var floor = scene.Trace.Ray( at + normal * 8f, at + Vector3.Down * 260f ).WithoutTags( "player", "npc", "trigger", "debris" ).Run();
		Blast( scene, at, floor.Hit ? floor.HitPosition : Vector3.Zero, floor.Hit ? floor.Normal : Vector3.Up, radius );
	}

	/// <summary> Every screen: a torpedo's blast, several times a detonator's. </summary>
	public static void Blast( Scene scene, Vector3 at, Vector3 floorPoint, Vector3 floorNormal, float radius )
	{
		// Battlefront II's rocket crack and heavy boom over our own blast, heard far off
		Sound.Play( "sounds/flight/bf2/missile_blast.sound", at );
		Sound.Play( "sounds/flight/bf2/missile_boom.sound", at );
		Sound.Play( SwrpAudio.DetonatorBlast, at );
		Sound.Play( SwrpAudio.DetonatorBoom, at );
		SwrpShake.Blast( at, 3.2f, radius * 12f );
		if ( !SwrpFx.Near( scene, at, 15000f ) ) return;

		// the flash, a shockwave racing out, three fireballs, a wide dust ring, heavy debris and sparks
		SwrpFx.Glow( scene, at, new Color( 1f, 0.92f, 0.78f ) * 10f, radius * 0.5f, radius * 2.2f, 0.2f );
		SwrpFx.Glow( scene, at, new Color( 0.9f, 0.95f, 1f, 0.35f ) * 2.5f, radius * 0.6f, radius * 6f, 0.45f );
		for ( var i = 0; i < 3; i++ )
		{
			var go = scene.CreateObject();
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = at + Vector3.Up * 20f + ( i == 0 ? Vector3.Zero : Vector3.Random * radius * 0.35f );
			go.Components.Create<Fireball>().Scale = i == 0 ? radius / 80f : radius / 130f;
		}
		if ( floorPoint != Vector3.Zero )
		{
			var go = scene.CreateObject();
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = floorPoint + floorNormal * 6f;
			var ring = go.Components.Create<DustRing>();
			ring.Up = floorNormal;
			ring.Scale = radius / 110f;
		}
		{
			var go = scene.CreateObject();
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = at;
			go.Components.Create<Debris>().Scale = 2.4f;
		}
		SwrpFx.Explosion( scene, at, floorPoint, floorNormal, radius, silentTest: true );      // (the detonator's look on top: sparks, scorch, light)
	}

	protected override void OnDestroy()
	{
		if ( _live.TryGetValue( MissileId, out var m ) && m == this ) _live.Remove( MissileId );
	}
}
