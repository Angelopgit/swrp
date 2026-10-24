using System;

namespace Swrp;

/// <summary>
/// The wall keypad beside a door. It lights green for open access, amber for restricted and red when locked; pressing E on it
/// (see <see cref="SwrpInteract"/>) asks the host to open or close its door. Sits as a child of the door object.
/// </summary>
[Title( "SWRP Door Panel" ), Category( "SWRP" ), Icon( "dialpad" )]
public sealed class SwrpDoorPanel : Component
{
	public SwrpDoor Door => GameObject.Parent?.GetComponent<SwrpDoor>();

	static readonly Vector3 PanelSize = new( 2f, 9f, 13f );
	ModelRenderer _screen;

	protected override void OnStart()
	{
		// A slim housing with a lit screen, built from the stock box so it needs no asset of its own.
		var box = Model.Load( "models/dev/box.vmdl" );
		var unit = box.Bounds.Size;

		var housing = AddBox( "Housing", box, PanelSize, Vector3.Zero, new Color( 0.09f, 0.1f, 0.12f ) );
		_screen = AddBox( "Screen", box, new Vector3( 0.6f, 6.5f, 8f ), new Vector3( 1.1f, 0, 1.2f ), Color.White );

		var col = AddComponent<BoxCollider>();
		col.Scale = PanelSize + 1f;

		ModelRenderer AddBox( string name, Model model, Vector3 size, Vector3 offset, Color tint )
		{
			var go = new GameObject( GameObject, true, name );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.LocalPosition = offset;
			go.LocalScale = new Vector3( size.x / MathF.Max( 1, unit.x ), size.y / MathF.Max( 1, unit.y ), size.z / MathF.Max( 1, unit.z ) );
			var r = go.AddComponent<ModelRenderer>();
			r.Model = model;
			r.Tint = tint;
			return r;
		}
	}

	protected override void OnUpdate()
	{
		if ( !_screen.IsValid() || Door is not { } d ) return;

		var c = d.Locked ? new Color( 1f, 0.15f, 0.12f ) : d.EffectiveRank > 0 || d.StaffOnly || !string.IsNullOrEmpty( d.RequiredFaction )
			? new Color( 1f, 0.68f, 0.15f ) : new Color( 0.2f, 1f, 0.45f );
		if ( d.IsOpen ) c = new Color( 0.35f, 0.75f, 1f );
		_screen.Tint = c;
	}
}

/// <summary>
/// The hangar control console: opens and closes the main hangar bay doors for everyone, with an alarm and a ship-wide notice.
/// </summary>
[Title( "SWRP Hangar Console" ), Category( "SWRP" ), Icon( "flight_takeoff" )]
public sealed class SwrpHangarConsole : Component
{
	/// <summary> Comma separated door ids this console drives. </summary>
	[Property] public string Doors { get; set; } = "port_hangerdoor,port_hangerdoor2";
	[Property] public float Cooldown { get; set; } = 5f;

	TimeSince _lastUse = 100;

	public IEnumerable<SwrpDoor> Targets
	{
		get
		{
			var ids = ( Doors ?? "" ).Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
			return Scene.GetAllComponents<SwrpDoor>().Where( d => ids.Contains( d.DoorId ) );
		}
	}

	public bool BayOpen => Targets.Any( d => d.IsOpen );

	[Rpc.Host]
	public void RequestUse()
	{
		var caller = Rpc.Caller;
		var p = SwrpGameManager.Current?.FindPlayer( caller );
		var staff = caller is not null && Whitelist.IsStaff( caller );
		if ( !staff && ( !p.IsValid() || p.CharacterId is null || p.Jailed ) )
		{
			SwrpNet.NotifyConnection( caller, "Access denied.", true );
			return;
		}

		if ( p.IsValid() && p.WorldPosition.Distance( WorldPosition ) > 260f )
		{
			SwrpNet.NotifyConnection( caller, "You are too far away.", true );
			return;
		}
		if ( _lastUse < Cooldown )
		{
			SwrpNet.NotifyConnection( caller, "The bay doors are still cycling.", true );
			return;
		}

		_lastUse = 0;
		var open = !BayOpen;
		foreach ( var d in Targets ) d.SetOpen( open );

		var who = p.IsValid() ? $"{p.Rank.Abbreviation} {p.CharacterName}" : caller?.DisplayName;
		var text = open ? "Attention: main hangar bay doors opening." : "Attention: main hangar bay doors closing.";
		SwrpChat.SendTo( Connection.All, ChatKind.System, $"{text} ({who})" );
		Announce();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Announce() => Sound.Play( SwrpAudio.HangarAlarm );
}
