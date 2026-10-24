using System;

namespace Swrp;

/// <summary>
/// Corpses: every machine drops a local ragdoll of the body where a trooper dies (capped, then it sinks away), and the host
/// remembers the cause of death so a medic or an officer can /examine the body.
/// </summary>
public static class SwrpCorpses
{
	public const int MaxRagdolls = 12;
	public const float Lifetime = 75f;

	public sealed class Entry
	{
		public Vector3 Position;
		public RealTimeSince Age;
		public string Name;
		public string Cause;
	}

	static readonly List<Entry> _entries = new();
	static readonly Queue<GameObject> _ragdolls = new();

	public static void Reset()
	{
		_entries.Clear();
		_ragdolls.Clear();
	}

	/// <summary> Host. </summary>
	public static void Record( SwrpVitals vitals, SwrpPlayer player, string cause )
	{
		_entries.RemoveAll( e => e.Age > Lifetime );
		_entries.Add( new Entry
		{
			Position = vitals.WorldPosition,
			Age = 0,
			Name = player.IsValid() ? player.FullTitle : vitals.GameObject.Name,
			Cause = cause,
		} );
		if ( _entries.Count > 32 ) _entries.RemoveAt( 0 );
	}

	/// <summary> Host: /examine with nobody alive close by looks at the nearest body. </summary>
	public static bool Examine( Connection caller, SwrpPlayer sender )
	{
		_entries.RemoveAll( e => e.Age > Lifetime );
		var body = _entries.OrderBy( e => e.Position.DistanceSquared( sender.WorldPosition ) ).FirstOrDefault();
		if ( body is null || body.Position.Distance( sender.WorldPosition ) > 220f ) return false;

		var qualified = sender.Class.IsMedic || sender.RankIndex >= SwrpRoleplay.LieutenantRank || Whitelist.IsStaff( caller );
		SwrpChat.SystemTo( caller, $"The body of {body.Name}." );
		SwrpChat.SystemTo( caller, qualified ? $"Cause of death: {body.Cause}" : "You can't tell what happened. A medic or an officer could." );
		return true;
	}

	/// <summary> Every machine: a ragdoll copy of the body in its last pose. </summary>
	public static GameObject SpawnRagdoll( Scene scene, SkinnedModelRenderer source )
	{
		if ( source?.Model is null ) return null;

		var go = scene.CreateObject();
		go.Name = "Corpse";
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( "ragdoll" );
		go.WorldTransform = source.WorldTransform;

		var r = go.AddComponent<SkinnedModelRenderer>();
		r.Model = source.Model;
		r.BodyGroups = source.BodyGroups;
		r.Tint = source.Tint;
		r.MaterialGroup = source.MaterialGroup;      // the battalion's armour
		r.UseAnimGraph = false;

		var physics = go.AddComponent<ModelPhysics>();
		physics.Model = source.Model;
		physics.Renderer = r;
		physics.CopyBonesFrom( source, true );

		go.AddComponent<CorpseFade>();

		_ragdolls.Enqueue( go );
		while ( _ragdolls.Count > MaxRagdolls )
		{
			var old = _ragdolls.Dequeue();
			if ( old.IsValid() ) old.Destroy();
		}
		return go;
	}
}

/// <summary> Keeps a ragdoll for a while, then lets it sink through the floor and removes it. </summary>
public sealed class CorpseFade : Component
{
	TimeSince _age;
	bool _sinking;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		if ( _age < SwrpCorpses.Lifetime - 4f ) return;

		if ( !_sinking )
		{
			_sinking = true;
			if ( GetComponent<ModelPhysics>() is { } p ) p.Enabled = false;
		}

		WorldPosition += Vector3.Down * 12f * Time.Delta;
		if ( _age > SwrpCorpses.Lifetime ) GameObject.Destroy();
	}
}

/// <summary>
/// A bacta station: press E to get a slow bacta treatment (heals over time and stops bleeding), then a cooldown before the
/// same trooper can use it again. Placed by the scene builder in the medbay, the hangar and the barracks.
/// </summary>
[Title( "SWRP Bacta Station" ), Category( "SWRP" ), Icon( "medical_services" )]
public sealed class SwrpBactaStation : Component
{
	[Property] public string StationName { get; set; } = "Bacta station";
	[Property] public float HealAmount { get; set; } = 45f;
	[Property] public float HealSeconds { get; set; } = 12f;
	[Property] public float Cooldown { get; set; } = 60f;

	readonly Dictionary<ulong, RealTimeSince> _lastUse = new();
	PointLight _light;

	protected override void OnStart()
	{
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box.Bounds.Size;
		Part( "Base", new Vector3( 26, 26, 8 ), new Vector3( 0, 0, 4 ), new Color( 0.12f, 0.13f, 0.15f ) );
		Part( "Column", new Vector3( 14, 14, 56 ), new Vector3( 0, 0, 36 ), new Color( 0.85f, 0.87f, 0.9f ) );
		Part( "Tank", new Vector3( 16, 16, 30 ), new Vector3( 0, 0, 44 ), new Color( 0.35f, 1f, 0.6f ) );
		Part( "Cap", new Vector3( 20, 20, 4 ), new Vector3( 0, 0, 66 ), new Color( 0.2f, 0.22f, 0.25f ) );

		var lightGo = new GameObject( GameObject, true, "Bacta Light" );
		lightGo.NetworkMode = NetworkMode.Never;
		lightGo.Flags |= GameObjectFlags.NotSaved;
		lightGo.LocalPosition = new Vector3( 0, 0, 50 );
		_light = lightGo.AddComponent<PointLight>();
		_light.Radius = 140f;
		_light.Shadows = false;
		_light.LightColor = new Color( 0.3f, 1f, 0.55f ) * 1.4f;

		var col = AddComponent<BoxCollider>();
		col.Center = new Vector3( 0, 0, 34 );
		col.Scale = new Vector3( 26, 26, 68 );

		void Part( string name, Vector3 size, Vector3 pos, Color tint )
		{
			var go = new GameObject( GameObject, true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = pos;
			go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = box;
			r.Tint = tint;
		}
	}

	[Rpc.Host]
	public void RequestUse()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		var v = p?.GetComponent<SwrpVitals>();
		if ( v is null || v.IsDead || v.IsDowned ) return;
		if ( p.WorldPosition.Distance( WorldPosition ) > 160f ) { SwrpNet.NotifyConnection( caller, "You are too far away.", true ); return; }

		if ( _lastUse.TryGetValue( p.OwnerSteamId, out var since ) && since < Cooldown )
		{
			SwrpNet.NotifyConnection( caller, $"The station is recharging your dose ({(int)( Cooldown - since )} s).", true );
			return;
		}

		if ( v.Health >= SwrpVitals.MaxHealth && !v.Bleeding )
		{
			SwrpNet.NotifyConnection( caller, "You're already at full health.", false );
			return;
		}

		if ( !v.StartTreatment( StationName, 2.5f, () =>
			{
				v.StopBleeding();
				v.HealOverTime( HealAmount, HealSeconds );
				SwrpNet.NotifyConnection( caller, "Bacta flows in. The wounds close.", false );
			} ) )
		{
			SwrpNet.NotifyConnection( caller, "You're already being treated.", true );
			return;
		}

		_lastUse[p.OwnerSteamId] = 0;
		Hum();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Hum() => Sound.Play( SwrpAudio.BactaStation, WorldPosition + Vector3.Up * 40f );
}
