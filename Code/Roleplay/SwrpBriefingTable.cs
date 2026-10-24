using System;

namespace Swrp;

/// <summary>
/// A holo-table an officer can put a text briefing on with /briefing. The text is synced from the host; nearby players see
/// it as a hologram above the table (drawn by <see cref="BriefingView"/>). Placed by the scene builder where the original
/// map had its war-room and bridge holo-tables.
/// </summary>
[Title( "SWRP Briefing Table" ), Category( "SWRP" ), Icon( "table_restaurant" )]
public sealed class SwrpBriefingTable : Component
{
	public const float ReadRange = 420f;

	[Property] public string TableName { get; set; } = "Holo-table";

	[Sync( SyncFlags.FromHost )] public string Title { get; set; }
	[Sync( SyncFlags.FromHost )] public string Text { get; set; }
	[Sync( SyncFlags.FromHost )] public string Author { get; set; }

	public bool HasBriefing => !string.IsNullOrEmpty( Title );

	/// <summary> Where the hologram floats. </summary>
	public Vector3 HoloPoint => WorldPosition + Vector3.Up * 62f;

	ModelRenderer _glow;
	PointLight _light;

	protected override void OnStart()
	{
		// An original, simple table: dark pedestal, lit projector top, and a soft blue light. Built from the stock box.
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box.Bounds.Size;

		Part( "Pedestal", new Vector3( 36, 36, 34 ), new Vector3( 0, 0, 17 ), new Color( 0.1f, 0.11f, 0.13f ) );
		Part( "Rim", new Vector3( 52, 52, 4 ), new Vector3( 0, 0, 36 ), new Color( 0.22f, 0.24f, 0.28f ) );
		_glow = Part( "Projector", new Vector3( 44, 44, 1 ), new Vector3( 0, 0, 38.6f ), new Color( 0.35f, 0.8f, 1f ) );

		var lightGo = new GameObject( GameObject, true, "Holo Light" );
		lightGo.NetworkMode = NetworkMode.Never;
		lightGo.Flags |= GameObjectFlags.NotSaved;
		lightGo.LocalPosition = new Vector3( 0, 0, 60 );
		_light = lightGo.AddComponent<PointLight>();
		_light.Radius = 180f;
		_light.Shadows = false;

		var col = AddComponent<BoxCollider>();
		col.Center = new Vector3( 0, 0, 19 );
		col.Scale = new Vector3( 52, 52, 38 );

		ModelRenderer Part( string name, Vector3 size, Vector3 pos, Color tint )
		{
			var go = new GameObject( GameObject, true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = pos;
			go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = box;
			r.Tint = tint;
			return r;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_light.IsValid() ) return;
		var pulse = 0.85f + 0.15f * MathF.Sin( Time.Now * 2.2f );
		_light.LightColor = new Color( 0.3f, 0.7f, 1f ) * ( HasBriefing ? 2.2f : 0.8f ) * pulse;
	}

	/// <summary> Host. </summary>
	public void Show( string title, string text, string author )
	{
		Title = title.Length > 60 ? title[..60] : title;
		Text = text.Length > 400 ? text[..400] : text;
		Author = author;
		Chime();
	}

	/// <summary> Host. </summary>
	public void Clear()
	{
		Title = null;
		Text = null;
		Author = null;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Chime() => Sound.Play( SwrpAudio.OrderPosted, HoloPoint );
}
