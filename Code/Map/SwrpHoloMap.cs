using System;
using System.Text.Json.Serialization;

namespace Swrp;

/// <summary>
/// The bridge holo-map: a see-through hologram of the ship's decks above a holo-table, with a glowing dot for every deployed
/// trooper aboard in their squad's colour (battalion colour when not in a squad; squad leaders a bigger dot). The deck plan
/// comes from Assets/data/notorious_holo.json (MapSource/notorious_holo.py). Everything here is local to each machine: the
/// player positions are already networked, so the map costs no traffic. It only draws while you are near it.
/// E on the projector prints where each squad is.
/// </summary>
[Title( "SWRP Holo-map" ), Category( "SWRP" ), Icon( "map" )]
public sealed class SwrpHoloMap : Component
{
	public const string FilePath = "data/notorious_holo.json";
	public const float ShowRange = 1400f;

	/// <summary> Map units per ship unit. </summary>
	[Property] public float Scale { get; set; } = 1f / 250f;

	/// <summary> Height of the hologram's lowest deck above this object (the table top plus a gap). </summary>
	[Property] public float Height { get; set; } = 52f;

	sealed class FileData
	{
		[JsonPropertyName( "lo" )] public float[] Lo { get; set; }
		[JsonPropertyName( "hi" )] public float[] Hi { get; set; }
		[JsonPropertyName( "rects" )] public List<float[]> Rects { get; set; } = new();
	}

	static Model _deckModel;
	static Vector3 _center;
	static float _bottom;

	GameObject _holo;
	ModelRenderer _ship;
	readonly List<ModelRenderer> _dots = new();
	TimeSince _sinceDots = 10;
	bool _shown = true;

	protected override void OnStart()
	{
		if ( !BuildModel() ) return;

		_holo = new GameObject( GameObject, true, "Hologram" );
		_holo.NetworkMode = NetworkMode.Never;
		_holo.Flags |= GameObjectFlags.NotSaved;
		_holo.LocalPosition = Vector3.Up * Height;
		_holo.LocalScale = Scale;

		var shipGo = new GameObject( _holo, true, "Deck Plan" );
		shipGo.NetworkMode = NetworkMode.Never;
		shipGo.Flags |= GameObjectFlags.NotSaved;
		_ship = shipGo.AddComponent<ModelRenderer>();
		_ship.Model = _deckModel;
		_ship.RenderType = ModelRenderer.ShadowRenderType.Off;
		_ship.Tint = new Color( 0.3f, 0.75f, 1f );

		// the projector you press E on: a thin slab just over the table top
		var col = AddComponent<BoxCollider>();
		col.Center = Vector3.Up * ( Height - 7f );
		col.Scale = new Vector3( 70, 70, 6 );
	}

	static bool BuildModel()
	{
		if ( _deckModel is not null ) return true;
		FileData data;
		try
		{
			data = Json.Deserialize<FileData>( SwrpDataFile.ReadText( FilePath ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[SWRP] Could not read {FilePath}: {e.Message}" );
			return false;
		}
		if ( data?.Rects is not { Count: > 0 } || data.Lo is null || data.Hi is null ) return false;

		var lo = new Vector3( data.Lo[0], data.Lo[1], data.Lo[2] );
		var hi = new Vector3( data.Hi[0], data.Hi[1], data.Hi[2] );
		_center = ( lo + hi ) * 0.5f;
		_bottom = lo.z;

		// every floor rectangle as a low see-through block (a deck's slice of the ship), a little inset so neighbouring cells read
		// as a grid; both sides of every face are drawn so the far walls show through the near ones
		const float slice = 70f;
		var verts = new List<Vertex>( data.Rects.Count * 48 );
		var indices = new List<int>( data.Rects.Count * 72 );
		var bounds = new BBox( new Vector3( lo.x - _center.x, lo.y - _center.y, 0 ), new Vector3( hi.x - _center.x, hi.y - _center.y, hi.z - lo.z + slice ) );
		foreach ( var r in data.Rects )
		{
			if ( r.Length < 5 ) continue;
			var z = r[4] - _bottom;
			var x0 = r[0] + 6 - _center.x;
			var y0 = r[1] + 6 - _center.y;
			var x1 = r[2] - 6 - _center.x;
			var y1 = r[3] - 6 - _center.y;
			var z1 = z + slice;
			Face( new( x0, y0, z1 ), new( x1, y0, z1 ), new( x1, y1, z1 ), new( x0, y1, z1 ) );   // top
			Face( new( x0, y1, z ), new( x1, y1, z ), new( x1, y0, z ), new( x0, y0, z ) );       // bottom
			Face( new( x0, y0, z ), new( x1, y0, z ), new( x1, y0, z1 ), new( x0, y0, z1 ) );     // -y
			Face( new( x1, y1, z ), new( x0, y1, z ), new( x0, y1, z1 ), new( x1, y1, z1 ) );     // +y
			Face( new( x0, y1, z ), new( x0, y0, z ), new( x0, y0, z1 ), new( x0, y1, z1 ) );     // -x
			Face( new( x1, y0, z ), new( x1, y1, z ), new( x1, y1, z1 ), new( x1, y0, z1 ) );     // +x
		}

		// a quad given counter-clockwise from its front, added facing both ways
		void Face( Vector3 a, Vector3 b, Vector3 c, Vector3 d )
		{
			var n = Vector3.Cross( b - a, c - a ).Normal;
			var t = ( b - a ).Normal;
			foreach ( var side in new[] { 1f, -1f } )
			{
				var i = verts.Count;
				verts.Add( new Vertex( a, n * side, t, new Vector4( 0, 0, 0, 0 ) ) );
				verts.Add( new Vertex( b, n * side, t, new Vector4( 1, 0, 0, 0 ) ) );
				verts.Add( new Vertex( c, n * side, t, new Vector4( 1, 1, 0, 0 ) ) );
				verts.Add( new Vertex( d, n * side, t, new Vector4( 0, 1, 0, 0 ) ) );
				// the renderer wants front faces clockwise
				if ( side > 0 ) indices.AddRange( new[] { i, i + 2, i + 1, i, i + 3, i + 2 } );
				else indices.AddRange( new[] { i, i + 1, i + 2, i, i + 2, i + 3 } );
			}
		}

		var mesh = new Mesh( Material.Load( "materials/fx/holo.vmat" ) );
		mesh.CreateVertexBuffer( verts.Count, verts );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = bounds;
		_deckModel = Model.Builder.AddMesh( mesh ).Create();
		_bottom = lo.z;
		return true;
	}

	/// <summary> Where a point in the ship sits in the hologram (local to the hologram object, in ship units). </summary>
	static Vector3 ToHolo( Vector3 p ) => new( p.x - _center.x, p.y - _center.y, p.z - _bottom );

	protected override void OnUpdate()
	{
		if ( !_holo.IsValid() || Scene.Camera is not { } cam ) return;

		var show = cam.WorldPosition.DistanceSquared( WorldPosition ) < ShowRange * ShowRange;
		if ( show != _shown )
		{
			_shown = show;
			_holo.Enabled = show;
		}
		if ( !show ) return;

		// shimmer, and the ship's alert colour at DEFCON 3 and above
		var ship = SwrpShip.Current;
		var baseTint = ship is { AlertLevel: <= 3 } ? ( Color.Parse( ship.Alert.Hex ) ?? Color.White ) : new Color( 0.3f, 0.75f, 1f );
		var flicker = 0.85f + 0.1f * MathF.Sin( Time.Now * 3.1f ) + 0.05f * MathF.Sin( Time.Now * 17f );
		_ship.Tint = ( baseTint * flicker ).WithAlpha( 1f );

		// a slow turn, so everyone round the table sees the ship from every side
		_holo.LocalRotation = Rotation.FromYaw( Time.Now * 6f % 360f );

		if ( _sinceDots > 0.25f )
		{
			_sinceDots = 0;
			UpdateDots();
		}
	}

	void UpdateDots()
	{
		var rp = SwrpRoleplay.Current;
		var n = 0;
		foreach ( var p in SwrpPlayers.Valid )
		{
			if ( p.CharacterId is null || p.Invisible || !p.IsValid() ) continue;
			if ( p.GetComponent<SwrpVitals>() is { IsDead: true } ) continue;
			var pos = p.WorldPosition;
			if ( !SwrpShip.InShip( pos ) ) continue;

			var squad = rp?.SquadOf( p.OwnerSteamId );
			var color = squad is not null ? Color.Parse( squad.ColorHex ) ?? Color.White : p.Faction.Color * 0.8f;
			var leader = squad is not null && squad.Leader == p.OwnerSteamId;

			// dots are a fixed size in the world (1.6 units, 2.4 for leaders), whatever the hologram's scale
			var dot = Dot( n++ );
			var size = ( leader ? 2.4f : 1.6f ) / Scale / MathF.Max( 1f, _dotModelSize );
			dot.GameObject.LocalPosition = ToHolo( pos ) + Vector3.Up * ( 1.2f / Scale );
			dot.GameObject.LocalScale = size;
			dot.Tint = color;
			dot.GameObject.Enabled = true;
		}
		for ( var i = n; i < _dots.Count; i++ )
			if ( _dots[i].IsValid() ) _dots[i].GameObject.Enabled = false;
	}

	float _dotModelSize = 1f;

	ModelRenderer Dot( int i )
	{
		while ( _dots.Count <= i )
		{
			var go = new GameObject( _holo, true, "Trooper" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			var r = go.AddComponent<ModelRenderer>();
			r.Model = Model.Load( "models/dev/sphere.vmdl" );
			_dotModelSize = r.Model.Bounds.Size.x;
			r.MaterialOverride = Material.Load( "materials/fx/holo_dot.vmat" );
			r.RenderType = ModelRenderer.ShadowRenderType.Off;
			_dots.Add( r );
		}
		return _dots[i];
	}

	// ----- E on the projector: where is everyone (host) -----

	[Rpc.Host]
	public void RequestSummary()
	{
		var caller = Rpc.Caller;
		var me = SwrpGameManager.Current?.FindPlayer( caller );
		if ( me.IsValid() && me.WorldPosition.Distance( WorldPosition ) > 300f ) return;
		foreach ( var line in Summary( Scene ) )
			SwrpChat.SystemTo( caller, line );
	}

	/// <summary> One line per squad (and one for troopers in none): how many, and on which decks. </summary>
	public static IEnumerable<string> Summary( Scene scene )
	{
		var rp = SwrpRoleplay.Current;
		var groups = new Dictionary<string, List<string>>();
		foreach ( var p in scene.GetAllComponents<SwrpPlayer>() )
		{
			if ( p.CharacterId is null || p.Invisible ) continue;
			var squad = rp?.SquadOf( p.OwnerSteamId );
			var key = squad?.Name ?? "No squad";
			if ( !groups.TryGetValue( key, out var list ) ) groups[key] = list = new();
			list.Add( SwrpShip.DeckName( p.WorldPosition ) );
		}
		if ( groups.Count == 0 )
		{
			yield return "Holo-map: no troopers deployed.";
			yield break;
		}
		yield return "Holo-map: troopers aboard by squad.";
		foreach ( var (name, decks) in groups.OrderBy( g => g.Key == "No squad" ).ThenBy( g => g.Key ) )
		{
			var where = string.Join( ", ", decks.GroupBy( d => d ).OrderByDescending( g => g.Count() ).Select( g => g.Count() > 1 ? $"{g.Key} ({g.Count()})" : g.Key ) );
			yield return $"  {name} · {decks.Count}: {where}";
		}
	}
}
