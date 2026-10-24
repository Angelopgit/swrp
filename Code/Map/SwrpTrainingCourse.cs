using System;

namespace Swrp;

/// <summary>
/// A timed shooting course in one of the simulation rooms. E on its console starts a run for you: a short countdown, then
/// targets pop up across the room for <see cref="RunSeconds"/> seconds, a few at a time, each for a few seconds. Hitting one
/// scores more the faster you are; only the runner's shots count. The best runs go on a leaderboard per course
/// (swrp/training_scores.json, top 10). The host runs everything; targets are networked so the room can watch.
/// Placed by notorious_scene.py (console position, firing line and target zone).
/// </summary>
[Title( "SWRP Training Course" ), Category( "SWRP" ), Icon( "sports_score" )]
public sealed class SwrpTrainingCourse : Component
{
	public const string ScoresFile = "swrp/training_scores.json";
	public const float RunSeconds = 40f;
	public const float Countdown = 3f;
	public const float TargetLife = 4f;
	public const int MaxActive = 3;

	[Property] public string CourseId { get; set; } = "sim_a";
	[Property] public string CourseName { get; set; } = "Simulation Room A";

	/// <summary> Targets appear inside this box (world space; its floor height is found by a trace). </summary>
	[Property] public Vector3 ZoneMin { get; set; }
	[Property] public Vector3 ZoneMax { get; set; }

	/// <summary> The room, for who sees the scoreboard and who can watch. </summary>
	[Property] public Vector3 RoomMin { get; set; }
	[Property] public Vector3 RoomMax { get; set; }

	[Sync( SyncFlags.FromHost )] public ulong RunnerSteamId { get; set; }
	[Sync( SyncFlags.FromHost )] public string RunnerName { get; set; }
	[Sync( SyncFlags.FromHost )] public float SecondsLeft { get; set; }
	[Sync( SyncFlags.FromHost )] public bool CountingDown { get; set; }
	[Sync( SyncFlags.FromHost )] public int Score { get; set; }
	[Sync( SyncFlags.FromHost )] public int Hits { get; set; }
	[Sync( SyncFlags.FromHost )] public int Spawned { get; set; }

	public bool Running => RunnerSteamId != 0;
	public BBox Room => new( RoomMin, RoomMax );

	ModelRenderer _screen;
	TimeSince _sinceStart;
	TimeSince _sinceSpawn;
	readonly List<SwrpTrainingTarget> _targets = new();
	int _lastBeep = -1;

	protected override void OnStart()
	{
		// the console: same family as the bridge console, built from stock boxes
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box.Bounds.Size;
		Part( "Pedestal", new Vector3( 18, 30, 36 ), new Vector3( -2, 0, 18 ), new Color( 0.1f, 0.11f, 0.13f ) );
		Part( "Desk", new Vector3( 24, 38, 4 ), new Vector3( 0, 0, 38 ), new Color( 0.22f, 0.24f, 0.28f ), new Angles( -18, 0, 0 ) );
		_screen = Part( "Screen", new Vector3( 18, 32, 1 ), new Vector3( 0.6f, 0, 40.6f ), new Color( 0.4f, 1f, 0.55f ), new Angles( -18, 0, 0 ) );

		var col = AddComponent<BoxCollider>();
		col.Center = new Vector3( 0, 0, 21 );
		col.Scale = new Vector3( 28, 40, 44 );

		ModelRenderer Part( string name, Vector3 size, Vector3 pos, Color tint, Angles? angles = null )
		{
			var go = new GameObject( GameObject, true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = pos;
			go.LocalRotation = ( angles ?? Angles.Zero ).ToRotation();
			go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = box;
			r.Tint = tint;
			return r;
		}
	}

	protected override void OnUpdate()
	{
		if ( _screen.IsValid() )
			_screen.Tint = !Running ? new Color( 0.4f, 1f, 0.55f ) : CountingDown ? new Color( 1f, 0.7f, 0.2f ) : new Color( 0.35f, 0.75f, 1f ) * ( 0.8f + 0.2f * MathF.Sin( Time.Now * 6f ) );

		// countdown beeps for whoever is watching
		if ( Running && CountingDown )
		{
			var n = (int)MathF.Ceiling( SecondsLeft );
			if ( n != _lastBeep && n > 0 )
			{
				_lastBeep = n;
				if ( Scene.Camera is { } cam && Room.Contains( cam.WorldPosition ) ) Sound.Play( SwrpAudio.Click, WorldPosition );
			}
		}
		else _lastBeep = -1;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Running ) return;

		var runner = SwrpRoleplay.PlayerBySteam( Scene, RunnerSteamId );
		if ( !runner.IsValid() || runner.CharacterId is null || !Room.Contains( runner.WorldPosition ) )
		{
			Finish( runner, "left the course" );
			return;
		}

		if ( CountingDown )
		{
			SecondsLeft = MathF.Max( 0, Countdown - _sinceStart );
			if ( _sinceStart >= Countdown )
			{
				CountingDown = false;
				_sinceStart = 0;
				_sinceSpawn = 10;
				Cue( SwrpAudio.Confirm );
			}
			return;
		}

		SecondsLeft = MathF.Max( 0, RunSeconds - _sinceStart );
		_targets.RemoveAll( t => !t.IsValid() );

		if ( SecondsLeft <= 0 )
		{
			Finish( runner, null );
			return;
		}

		// a new target every 0.9 s while fewer than MaxActive are up; they get quicker towards the end
		var interval = SecondsLeft < 12f ? 0.6f : 0.9f;
		if ( _sinceSpawn > interval && _targets.Count( t => t.Up ) < MaxActive )
		{
			_sinceSpawn = 0;
			if ( SpawnTarget( runner ) ) Spawned++;
		}
	}

	bool SpawnTarget( SwrpPlayer runner )
	{
		var eye = runner is null ? WorldPosition + WorldRotation.Forward * 40f + Vector3.Up * 64f
			: runner.Controller.IsValid() ? runner.Controller.EyePosition : runner.WorldPosition + Vector3.Up * 64f;
		for ( var attempt = 0; attempt < 8; attempt++ )
		{
			var x = Game.Random.Float( ZoneMin.x, ZoneMax.x );
			var y = Game.Random.Float( ZoneMin.y, ZoneMax.y );
			var top = new Vector3( x, y, ZoneMax.z );
			var down = Scene.Trace.Ray( top, top.WithZ( ZoneMin.z ) ).Run();
			if ( !down.Hit ) continue;
			var foot = down.EndPosition;
			if ( _targets.Any( t => t.IsValid() && t.WorldPosition.Distance( foot ) < 120f ) ) continue;
			// the runner has to be able to see it
			var los = Scene.Trace.Ray( eye, foot + Vector3.Up * 40f ).IgnoreGameObjectHierarchy( runner?.GameObject ?? GameObject ).Run();
			if ( los.Hit && los.EndPosition.Distance( foot + Vector3.Up * 40f ) > 20f ) continue;

			var go = new GameObject( true, "Training Target" );
			go.WorldPosition = foot;
			go.WorldRotation = Rotation.FromYaw( ( eye - foot ).WithZ( 0 ).EulerAngles.yaw );
			var t = go.AddComponent<SwrpTrainingTarget>();
			t.Course = this;
			go.NetworkSpawn();
			_targets.Add( t );
			return true;
		}
		return false;
	}

	/// <summary> Host: a target was shot. </summary>
	public void TargetHit( SwrpTrainingTarget target, SwrpPlayer shooter )
	{
		if ( !Running || CountingDown || shooter?.OwnerSteamId != RunnerSteamId || !target.Up ) return;
		var points = 100 + (int)( MathF.Max( 0, TargetLife - target.Age ) * 50f );
		Score += points;
		Hits++;
		target.Down( true );
	}

	void Finish( SwrpPlayer runner, string why )
	{
		foreach ( var t in _targets )
			if ( t.IsValid() ) t.GameObject.Destroy();
		_targets.Clear();

		var name = RunnerName;
		var steam = RunnerSteamId;
		var score = Score;
		var hits = Hits;
		var spawned = Spawned;
		RunnerSteamId = 0;
		RunnerName = null;
		CountingDown = false;
		SecondsLeft = 0;

		var watchers = Watchers().ToList();
		if ( why is not null )
		{
			if ( runner.IsValid() ) SwrpChat.SendTo( watchers.Append( runner.Network.Owner ).Where( c => c is not null ).Distinct(), ChatKind.System, $"[{CourseName}] Run ended: {name} {why}." );
			return;
		}

		var place = Record( steam, name, score, hits, spawned );
		SwrpCareer.OnSimRun( runner, score );      // battalion stats, and the cadet checklist's simulation item
		var line = $"[{CourseName}] {name}: {score} points, {hits}/{spawned} targets{( place > 0 ? $" · #{place} on the board" : "" )}.";
		SwrpChat.SendTo( watchers.Append( runner?.Network.Owner ).Where( c => c is not null ).Distinct(), ChatKind.System, line );
		Cue( SwrpAudio.OrderPosted );
	}

	IEnumerable<Connection> Watchers()
	{
		foreach ( var p in SwrpPlayers.Valid )
			if ( p.Network.Owner is { } c && Room.Contains( p.WorldPosition ) ) yield return c;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Cue( string sound ) => Sound.Play( sound, WorldPosition );

	/// <summary> Client: start a run (E on the console). </summary>
	[Rpc.Host]
	public void RequestStart()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		if ( !p.IsValid() || p.CharacterId is null || p.Jailed ) return;
		if ( p.WorldPosition.Distance( WorldPosition ) > 200f )
		{
			SwrpNet.NotifyConnection( caller, "Stand at the course console to start.", true );
			return;
		}
		if ( Running )
		{
			if ( RunnerSteamId == p.OwnerSteamId ) Finish( p, "stopped the run" );
			else SwrpNet.NotifyConnection( caller, $"{RunnerName} is on the course. Wait for the run to end.", true );
			return;
		}
		if ( p.GetComponent<SwrpBlaster>() is not { Stats: not null } )
		{
			SwrpNet.NotifyConnection( caller, "Draw a blaster first.", true );
			return;
		}

		RunnerSteamId = p.OwnerSteamId;
		RunnerName = $"{p.Rank.Abbreviation} {p.CharacterName}";
		Score = 0;
		Hits = 0;
		Spawned = 0;
		CountingDown = true;
		SecondsLeft = Countdown;
		_sinceStart = 0;
		SwrpChat.SendTo( Watchers(), ChatKind.System, $"[{CourseName}] {RunnerName} starts a run. {RunSeconds:0} seconds, targets pop up across the room." );
	}

	[ConCmd( "swrp_course_demo", Help = "swrp_course_demo [course id] - host only: pop up a few targets on a training course without a run (for checking the room)" )]
	public static void Demo( string courseId = "sim_a" )
	{
		if ( !Networking.IsHost ) return;
		var c = Game.ActiveScene?.GetAllComponents<SwrpTrainingCourse>().FirstOrDefault( x => x.CourseId == courseId );
		if ( c is null ) { Log.Info( "[SWRP] No course with that id (sim_a, sim_b)." ); return; }
		var n = 0;
		for ( var i = 0; i < 6; i++ ) if ( c.SpawnTarget( null ) ) n++;
		Log.Info( $"[SWRP] {c.CourseName}: {n} demo targets up for {TargetLife:0} s." );
	}

	// ----- leaderboard (host) -----

	public sealed class ScoreEntry
	{
		public ulong SteamId { get; set; }
		public string Name { get; set; }
		public int Score { get; set; }
		public int Hits { get; set; }
		public int Targets { get; set; }
		public DateTime When { get; set; }
	}

	static Dictionary<string, List<ScoreEntry>> _scores;

	static Dictionary<string, List<ScoreEntry>> Scores
	{
		get
		{
			if ( _scores is null )
			{
				try { _scores = FileSystem.Data.ReadJson<Dictionary<string, List<ScoreEntry>>>( ScoresFile, null ); }
				catch ( Exception e ) { Log.Warning( $"[SWRP] Could not read {ScoresFile}: {e.Message}" ); }
				_scores ??= new();
			}
			return _scores;
		}
	}

	/// <summary> Adds a run; returns its place on the top 10, or 0. A trooper keeps only their best run. </summary>
	int Record( ulong steam, string name, int score, int hits, int targets )
	{
		if ( !Scores.TryGetValue( CourseId, out var list ) ) Scores[CourseId] = list = new();
		var old = list.FirstOrDefault( e => e.SteamId == steam );
		if ( old is not null && old.Score >= score ) return 0;
		if ( old is not null ) list.Remove( old );
		list.Add( new ScoreEntry { SteamId = steam, Name = name, Score = score, Hits = hits, Targets = targets, When = DateTime.UtcNow } );
		list.Sort( ( a, b ) => b.Score.CompareTo( a.Score ) );
		if ( list.Count > 10 ) list.RemoveRange( 10, list.Count - 10 );
		var place = list.FindIndex( e => e.SteamId == steam ) + 1;
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( ScoresFile, Scores );
		}
		catch ( Exception e ) { Log.Warning( $"[SWRP] Could not save {ScoresFile}: {e.Message}" ); }
		return place;
	}

	/// <summary> Host: /training shows the board of the course you are in (or every course). </summary>
	public static void BoardCommand( Connection caller, SwrpPlayer sender )
	{
		var courses = Game.ActiveScene?.GetAllComponents<SwrpTrainingCourse>().ToList() ?? new();
		var here = sender.IsValid() ? courses.Where( c => c.Room.Contains( sender.WorldPosition ) ).ToList() : new();
		if ( here.Count > 0 ) courses = here;
		if ( courses.Count == 0 )
		{
			SwrpChat.SystemTo( caller, "This map has no training courses." );
			return;
		}
		foreach ( var c in courses )
		{
			var list = Scores.TryGetValue( c.CourseId, out var l ) ? l : new();
			SwrpChat.SystemTo( caller, list.Count == 0 ? $"[{c.CourseName}] No runs yet. E on the course console to start one." : $"[{c.CourseName}] Top runs:" );
			for ( var i = 0; i < list.Count; i++ )
				SwrpChat.SystemTo( caller, $"  {i + 1}. {list[i].Name} · {list[i].Score} ({list[i].Hits}/{list[i].Targets})" );
		}
	}
}

/// <summary>
/// A pop-up training target: an original droid-silhouette plate that rises out of the floor, stays up a few seconds and drops
/// when hit or when its time runs out. Spawned and removed by the host's <see cref="SwrpTrainingCourse"/>.
/// </summary>
[Title( "SWRP Training Target" ), Category( "SWRP" ), Icon( "my_location" )]
public sealed class SwrpTrainingTarget : Component
{
	public SwrpTrainingCourse Course { get; set; }

	[Sync( SyncFlags.FromHost )] public bool Up { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public bool WasHit { get; set; }

	public float Age => _age;

	TimeSince _age;
	TimeSince _sinceDown;
	float _shown;
	GameObject _plate;
	readonly List<ModelRenderer> _parts = new();

	protected override void OnStart()
	{
		_age = 0;
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box.Bounds.Size;
		_plate = new GameObject( GameObject, true, "Plate" );
		_plate.NetworkMode = NetworkMode.Never;
		_plate.Flags |= GameObjectFlags.NotSaved;

		// a battle droid's outline, roughly: legs, thin body, long head, arms holding a rifle
		var orange = new Color( 1f, 0.55f, 0.15f );
		var white = new Color( 0.95f, 0.92f, 0.85f );
		Part( "Base", new Vector3( 30, 30, 3 ), new Vector3( 0, 0, 1.5f ), new Color( 0.15f, 0.15f, 0.17f ) );
		Part( "Leg L", new Vector3( 4, 5, 34 ), new Vector3( 0, -6, 20 ), orange );
		Part( "Leg R", new Vector3( 4, 5, 34 ), new Vector3( 0, 6, 20 ), orange );
		Part( "Body", new Vector3( 4, 18, 22 ), new Vector3( 0, 0, 48 ), orange );
		Part( "Neck", new Vector3( 3, 4, 8 ), new Vector3( 0, 0, 62 ), orange );
		Part( "Head", new Vector3( 4, 7, 14 ), new Vector3( 2, 0, 70 ), white );
		Part( "Arms", new Vector3( 4, 30, 4 ), new Vector3( 0, 0, 52 ), orange );

		var col = _plate.AddComponent<BoxCollider>();
		col.Center = new Vector3( 0, 0, 40 );
		col.Scale = new Vector3( 8, 30, 78 );

		_plate.LocalScale = new Vector3( 1, 1, 0.01f );

		void Part( string name, Vector3 size, Vector3 pos, Color tint )
		{
			var go = new GameObject( _plate, true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = pos;
			go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = box;
			r.Tint = tint;
			_parts.Add( r );
		}
	}

	protected override void OnUpdate()
	{
		// rise and fall
		var target = Up ? 1f : 0f;
		_shown = _shown.Approach( target, Time.Delta / 0.18f );
		if ( _plate.IsValid() ) _plate.LocalScale = new Vector3( 1, 1, MathF.Max( 0.01f, _shown ) );
		if ( WasHit )
			foreach ( var r in _parts )
				if ( r.IsValid() ) r.Tint = new Color( 0.35f, 1f, 0.45f );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( Up && _age > SwrpTrainingCourse.TargetLife ) Down( false );
		if ( !Up && _sinceDown > 0.5f ) GameObject.Destroy();
	}

	/// <summary> Host. </summary>
	public void Down( bool hit )
	{
		if ( !Up ) return;
		Up = false;
		WasHit = hit;
		_sinceDown = 0;
		if ( hit ) Ping();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Ping() => Sound.Play( SwrpAudio.Confirm, WorldPosition + Vector3.Up * 50f );

	/// <summary> Host: a blaster bolt hit this target. </summary>
	public void HostHit( SwrpPlayer shooter ) => Course?.TargetHit( this, shooter );
}
