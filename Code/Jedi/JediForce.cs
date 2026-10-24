using System;

namespace Swrp.Jedi;

/// <summary> One Force power: what it costs and how long before it's ready again. </summary>
public sealed record ForcePower( int Index, string Id, string Title, string Skill, float Cost, float Cooldown, float CastSeconds, string Glyph );

/// <summary>
/// The Force powers: Push, Pull, Leap, Speed, Heal and (only where the host allows it) Lightning. One shared pool of Force
/// points, a cooldown each, picked on the Force wheel (Z) and used with F. The host decides everything that touches anyone
/// else; the owner only moves its own body for a leap. Effects are drawn on every machine (SaberFx).
/// </summary>
public static class JediForce
{
	public const int Push = 0, Pull = 1, Leap = 2, Speed = 3, Heal = 4, Lightning = 5;

	public static readonly string[] Ids = { "push", "pull", "leap", "speed", "heal", "lightning" };

	public static readonly ForcePower[] Powers =
	{
		new( Push, "push", "Push", "force_push", 25f, 3.5f, 0.45f, "⇶" ),
		new( Pull, "pull", "Pull", "force_pull", 20f, 4f, 0.55f, "⇇" ),
		new( Leap, "leap", "Leap", "force_leap", 15f, 1.5f, 0.35f, "⤊" ),
		new( Speed, "speed", "Speed", "force_speed", 30f, 14f, 0.4f, "≫" ),
		new( Heal, "heal", "Heal", "force_heal", 40f, 16f, 1.2f, "✚" ),
		new( Lightning, "lightning", "Lightning", "force_lightning", 6f, 0f, 0.3f, "ϟ" ),
	};

	public static ForcePower Get( int index ) => index >= 0 && index < Powers.Length ? Powers[index] : null;

	/// <summary> The powers a Jedi has learned, in wheel order. </summary>
	public static List<ForcePower> Known( JediCombat j ) => Powers.Where( p => j.Knows( p.Skill ) && ( p.Index != Lightning || JediCombat.LightningAllowed ) ).ToList();

	/// <summary> The wheel: which known power sits at an angle (radians, 0 = up, clockwise). </summary>
	public static int AtAngle( JediCombat j, float angle )
	{
		var known = Known( j );
		if ( known.Count == 0 ) return -1;
		var step = MathF.PI * 2f / known.Count;
		var a = ( angle + MathF.PI * 2f + step * 0.5f ) % ( MathF.PI * 2f );
		return known[Math.Clamp( (int)( a / step ), 0, known.Count - 1 )].Index;
	}

	/// <summary> Owner: the leap itself (the owner simulates its own movement). </summary>
	public static void OwnerLeap( JediCombat j )
	{
		var c = j.Controller;
		var rb = j.GetComponent<Rigidbody>();
		if ( !c.IsValid() || rb is null ) return;
		var fwd = Rotation.FromYaw( c.EyeAngles.yaw ).Forward;
		var strong = j.Knows( "force_leap" );
		var flat = c.Velocity.WithZ( 0 );
		c.PreventGrounding( 0.3f );
		rb.Velocity = flat * 0.6f + fwd * ( strong ? 380f : 300f ) + Vector3.Up * 560f;
		j.StartLeap();
	}
}

public sealed partial class JediCombat
{
	RealTimeUntil _speedEnd;
	bool _channel;
	RealTimeSince _sinceChannelTick;
	readonly Dictionary<int, RealTimeUntil> _hostCooldowns = new();

	[Rpc.Host]
	void RequestForce( int power, Vector3 eye, Vector3 dir, int castSerial )
	{
		if ( !IsCallerOwner() || !IsJedi || !Alive || JediForce.Get( power ) is not { } def || !Knows( def.Skill ) ) return;
		if ( power == JediForce.Lightning && !LightningAllowed ) return;
		if ( _hostCooldowns.TryGetValue( power, out var cd ) && cd > 0f ) return;
		var cost = def.Cost * Form.ForceCost;
		if ( ForcePoints < cost ) return;
		if ( Player is { } p && ( p.Jailed || p.Frozen || p.InPod ) ) return;

		// the cast must come from this player's eyes
		if ( Controller.IsValid() && eye.Distance( Controller.EyePosition ) > 96f ) eye = Controller.EyePosition;
		dir = dir.Normal;

		ForcePoints -= cost;
		_sinceForce = 0;
		_sinceAction = 0;
		if ( def.Cooldown > 0f )
		{
			_hostCooldowns[power] = def.Cooldown;
			ReceiveCooldown( power, def.Cooldown );
		}
		CastPower = power;
		CastSeconds = def.CastSeconds;
		CastSerial = Math.Max( castSerial, CastSerial + 1 );
		_host.Cast( power, def.CastSeconds );
		Award( 2 );

		switch ( power )
		{
			case JediForce.Push: HostPush( eye, dir ); break;
			case JediForce.Pull: HostPull( eye, dir ); break;
			case JediForce.Leap: FxForce( JediForce.Leap, WorldPosition, dir, 0f, Guid.Empty ); break;
			case JediForce.Speed:
				SpeedActive = true;
				_speedEnd = 6f;
				FxForce( JediForce.Speed, WorldPosition, dir, 6f, Guid.Empty );
				break;
			case JediForce.Heal: HostHeal( eye, dir ); break;
			case JediForce.Lightning:
				_channel = true;
				_sinceChannelTick = 1f;
				break;
		}
	}

	[Rpc.Host]
	void RequestForceStop()
	{
		if ( !IsCallerOwner() ) return;
		_channel = false;
	}

	void HostForceTick( float dt )
	{
		if ( SpeedActive && _speedEnd <= 0f ) SpeedActive = false;
		if ( !_channel ) return;
		if ( !Alive || ForcePoints < 4f || !LightningAllowed ) { _channel = false; return; }
		if ( _sinceChannelTick < 0.15f ) return;
		_sinceChannelTick = 0;
		ForcePoints -= 4f;
		_sinceForce = 0;
		CastSerial++;
		CastPower = JediForce.Lightning;
		CastSeconds = 0.3f;
		_host.Cast( JediForce.Lightning, 0.3f );

		var eye = Controller.IsValid() ? Controller.EyePosition : WorldPosition + Vector3.Up * 64f * Scale;
		var dir = Controller.IsValid() ? Controller.EyeAngles.Forward : WorldRotation.Forward;
		var target = BestTarget( eye, dir, 560f * Scale, 0.9f );
		var hand = LeftHand();
		Vector3 end;
		if ( target is { } t )
		{
			end = t.Point;
			if ( t.Vitals is { } v ) v.TakeDamage( 5f, Player, hand, "Force lightning", false );
			if ( t.Npc is { } n ) n.TakeHit( 5f, Player, null, hand, "Force lightning", false, Game.Random.Float() < 0.25f, 0.6f );
		}
		else
		{
			var tr = Scene.Trace.Ray( eye, eye + dir * 420f * Scale ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			end = tr.EndPosition;
		}
		FxForce( JediForce.Lightning, hand, end - hand, 0.3f, target?.Root?.Id ?? Guid.Empty );
	}

	/// <summary> The left hand, roughly (where powers come from). </summary>
	Vector3 LeftHand() => WorldPosition + Rotation.FromYaw( FacingYaw ) * ( SaberMotion.Chest + new Vector3( 22f, 6f, 6f ) ) * Scale;

	record struct ForceTarget( GameObject Root, SwrpVitals Vitals, SwrpNpc Npc, Vector3 Point );

	/// <summary> Everyone in a cone from the eyes, in sight, nearest first (not yourself). </summary>
	List<ForceTarget> ConeTargets( Vector3 eye, Vector3 dir, float range, float minDot )
	{
		var list = new List<ForceTarget>();
		foreach ( var v in Scene.GetAllComponents<SwrpVitals>() )
		{
			if ( v.GameObject == GameObject || v.IsDead ) continue;
			var at = v.WorldPosition + Vector3.Up * 40f * Scale;
			if ( InCone( eye, dir, at, range, minDot ) ) list.Add( new ForceTarget( v.GameObject, v, null, at ) );
		}
		foreach ( var n in Scene.GetAllComponents<SwrpNpc>() )
		{
			if ( n.IsDead ) continue;
			var at = n.Eye + Vector3.Down * 20f;
			if ( InCone( eye, dir, at, range, minDot ) ) list.Add( new ForceTarget( n.GameObject, null, n, at ) );
		}
		return list.OrderBy( t => t.Point.DistanceSquared( eye ) ).ToList();
	}

	bool InCone( Vector3 eye, Vector3 dir, Vector3 at, float range, float minDot )
	{
		var to = at - eye;
		var d = to.Length;
		if ( d > range || d < 1f || Vector3.Dot( to / d, dir ) < minDot ) return false;
		var tr = Scene.Trace.Ray( eye, at ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player", "npc", "ragdoll" ).Run();
		return !tr.Hit || tr.Distance > d - 20f;
	}

	ForceTarget? BestTarget( Vector3 eye, Vector3 dir, float range, float minDot )
	{
		var best = ConeTargets( eye, dir, range, minDot ).OrderByDescending( t => Vector3.Dot( ( t.Point - eye ).Normal, dir ) ).FirstOrDefault();
		return best.Root is null ? null : best;
	}

	bool Hostile( ForceTarget t )
	{
		if ( SwrpCombat.FriendlyFire ) return true;
		if ( t.Vitals?.GetComponent<SwrpPlayer>() is { } p ) return !SwrpCombat.SameSide( Player, p );
		if ( t.Npc is { } n ) return !AllegianceRegistry.AreFriendly( Player.Allegiance, n.Allegiance );
		return true;
	}

	void HostPush( Vector3 eye, Vector3 dir )
	{
		var strong = Knows( "force_push2" );
		var range = ( strong ? 480f : 400f ) * Scale;
		var flat = dir.WithZ( MathF.Max( dir.z, -0.1f ) ).Normal;
		foreach ( var t in ConeTargets( eye, dir, range, 0.72f ) )
		{
			var falloff = 1f - Math.Clamp( t.Point.Distance( eye ) / range, 0f, 0.7f );
			var push = flat * 720f * falloff + Vector3.Up * 260f * falloff;
			if ( t.Vitals is { } v )
			{
				// a guarding Jedi braces against it
				if ( v.GetComponent<JediCombat>() is { IsJedi: true, Blocking: true } other && other.Faces( WorldPosition ) )
				{
					other.Focus -= 15f;
					other.ReceiveKnock( push * 0.3f, 0.1f );
					other.React( ReactKind.BlockShake, 0.3f );
					continue;
				}
				// allies are moved, not hurt
				v.GetComponent<JediCombat>()?.ReceiveKnock( push, 0.3f );
				if ( Hostile( t ) )
				{
					v.TakeDamage( 5f, Player, WorldPosition, "Force push", false );
					if ( strong && v.GetComponent<JediCombat>() is not { IsJedi: true } ) v.Stun( 1.6f, WorldPosition );
				}
				if ( v.GetComponent<JediCombat>() is { IsJedi: true } j2 ) j2.React( ReactKind.Knocked, 0.8f );
			}
			else if ( t.Npc is { } n && Hostile( t ) )
			{
				ShoveNpc( n, flat * 150f * falloff * ( strong ? 1.3f : 1f ) );
				n.TakeHit( 6f, Player, null, WorldPosition, "Force push", false, true, strong ? 2.2f : 1.4f );
			}
		}
		// loose things on the floor go flying too
		foreach ( var go in Scene.FindInPhysics( new Sphere( eye + dir * range * 0.5f, range * 0.5f ) ) )
		{
			if ( go.Components.Get<Rigidbody>() is not { MotionEnabled: true } rb || go.Components.Get<SwrpPlayer>() is not null ) continue;
			var to = ( go.WorldPosition - eye ).Normal;
			if ( Vector3.Dot( to, dir ) < 0.6f ) continue;
			rb.Velocity += ( to + Vector3.Up * 0.3f ).Normal * 600f;
		}
		FxForce( JediForce.Push, eye + dir * 20f * Scale, dir, range, Guid.Empty );
	}

	void HostPull( Vector3 eye, Vector3 dir )
	{
		var strong = Knows( "force_pull" );
		var target = BestTarget( eye, dir, 650f * Scale, 0.94f );
		if ( target is not { } t )
		{
			FxForce( JediForce.Pull, eye + dir * 400f * Scale, -dir, 0f, Guid.Empty );
			return;
		}
		var toMe = ( WorldPosition - t.Root.WorldPosition ).WithZ( 0 );
		var dist = toMe.Length;
		if ( t.Vitals is { } v )
		{
			var j = v.GetComponent<JediCombat>();
			if ( j is { IsJedi: true, Blocking: true } && j.Faces( WorldPosition ) )
			{
				j.Focus -= 12f;
				j.React( ReactKind.BlockShake, 0.3f );
			}
			else
			{
				j?.ReceiveKnock( toMe.Normal * MathF.Min( 640f, dist * 2.2f ) + Vector3.Up * 200f, 0.3f );
				if ( strong && Hostile( t ) ) j?.ReceiveDisarm();
			}
		}
		else if ( t.Npc is { } n )
		{
			ShoveNpc( n, toMe.Normal * MathF.Min( dist - 60f * Scale, 260f ) );
			if ( Hostile( t ) ) n.TakeHit( 3f, Player, null, WorldPosition, "Force pull", false, true, 1.2f );
		}
		FxForce( JediForce.Pull, t.Point, ( LeftHand() - t.Point ), 0f, t.Root.Id );
	}

	void HostHeal( Vector3 eye, Vector3 dir )
	{
		// Healing Touch: the ally you look at, close by; otherwise yourself
		SwrpVitals target = Vitals;
		if ( Knows( "force_heal2" ) && BestTarget( eye, dir, 260f * Scale, 0.9f ) is { Vitals: { } v } t && !Hostile( t ) ) target = v;
		if ( target is null || target.IsDead ) return;
		target.HealOverTime( 32f, 3f );
		target.StopBleeding();
		FxForce( JediForce.Heal, target.WorldPosition, Vector3.Up, 3f, target.GameObject.Id );
	}

	/// <summary> Host: slide an NPC along the floor (stopping short of walls). </summary>
	static void ShoveNpc( SwrpNpc n, Vector3 offset )
	{
		if ( offset.Length < 1f ) return;
		var from = n.WorldPosition + Vector3.Up * 30f;
		var tr = n.Scene.Trace.Ray( from, from + offset ).Radius( 12f ).IgnoreGameObjectHierarchy( n.GameObject ).WithoutTags( "trigger", "player", "npc", "ragdoll" ).Run();
		var move = tr.Hit ? offset.Normal * MathF.Max( 0f, tr.Distance - 16f ) : offset;
		n.WorldPosition += move;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void FxForce( int power, Vector3 at, Vector3 dir, float size, Guid target )
	{
		var go = target == Guid.Empty ? null : Scene.Directory.FindByGuid( target );
		SaberFx.Force( Scene, this, power, at, dir, size, go );
	}
}
