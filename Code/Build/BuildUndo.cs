using System;

namespace Swrp;

/// <summary>
/// Host: what each staff member built, newest last, so U takes back the last spawn or constraint (Z is prone). Adapted from the
/// undo stack, undo history page and cleanup page in Facepunch's Sandbox (MIT, see LICENSE-facepunch-sandbox.txt). Removals are
/// not undoable (Sandbox neither).
/// </summary>
public static class SwrpBuildUndo
{
	sealed class Entry
	{
		public string Name;
		public List<GameObject> Objects = new();
		public bool HasObjects => Objects.Any( o => o.IsValid() );
	}

	const int MaxSteps = 128;
	static readonly Dictionary<Guid, List<Entry>> _stacks = new();

	/// <summary> Host: remember a build step for this connection. </summary>
	public static void Add( Connection c, string name, params GameObject[] objects )
	{
		if ( c is null || objects.Length == 0 ) return;
		if ( !_stacks.TryGetValue( c.Id, out var stack ) ) _stacks[c.Id] = stack = new();
		stack.Add( new Entry { Name = name, Objects = objects.Where( o => o.IsValid() ).ToList() } );
		if ( stack.Count > MaxSteps ) stack.RemoveAt( 0 );
		SendHistory( c );
	}

	/// <summary> Client (U): undo my newest build step that still has something in the world. </summary>
	[Rpc.Host]
	public static void Undo()
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) || !_stacks.TryGetValue( caller.Id, out var stack ) ) return;
		while ( stack.Count > 0 )
		{
			var entry = stack[^1];
			stack.RemoveAt( stack.Count - 1 );
			if ( !entry.HasObjects ) continue;
			Run( caller, entry );
			SendHistory( caller );
			return;
		}
		SwrpBuild.Notify( caller, "Nothing left to undo" );
	}

	/// <summary> Client (undo history page): undo one entry, <paramref name="index"/> counted from the newest. </summary>
	[Rpc.Host]
	public static void UndoAt( int index )
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) || !_stacks.TryGetValue( caller.Id, out var stack ) ) return;
		stack.RemoveAll( e => !e.HasObjects );
		var i = stack.Count - 1 - index;
		if ( i < 0 || i >= stack.Count ) return;
		var entry = stack[i];
		stack.RemoveAt( i );
		Run( caller, entry );
		SendHistory( caller );
	}

	static void Run( Connection caller, Entry entry )
	{
		foreach ( var go in entry.Objects ) if ( go.IsValid() ) go.Destroy();
		SwrpBuild.Notify( caller, $"Undone: {entry.Name}" );
		SwrpBuild.Log( caller, $"undid {entry.Name}" );
	}

	// ----- history page -----

	/// <summary> Client: my undo list, newest first (filled by the host on request and after every change). </summary>
	public static List<string> History { get; private set; } = new();
	public static int HistoryVersion { get; private set; }

	[Rpc.Host]
	public static void RequestHistory()
	{
		if ( SwrpBuild.May( Rpc.Caller ) ) SendHistory( Rpc.Caller );
	}

	static void SendHistory( Connection c )
	{
		if ( c is null ) return;
		var names = _stacks.TryGetValue( c.Id, out var stack )
			? stack.Where( e => e.HasObjects ).Reverse().Take( 40 ).Select( e => e.Name ).ToArray()
			: Array.Empty<string>();
		using ( Rpc.FilterInclude( c ) ) ReceiveHistory( string.Join( "\n", names ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ReceiveHistory( string names )
	{
		History = string.IsNullOrEmpty( names ) ? new() : names.Split( '\n' ).ToList();
		HistoryVersion++;
	}

	// ----- cleanup page -----

	/// <summary>
	/// 0: everything I built. 1: every constraint I made. 2: every staff prop, gadget and constraint on the server (admins only).
	/// </summary>
	[Rpc.Host]
	public static void Cleanup( int what )
	{
		var caller = Rpc.Caller;
		if ( !SwrpBuild.May( caller ) ) return;
		var gone = 0;
		if ( what == 2 )
		{
			if ( !Whitelist.IsAdmin( caller ) ) { SwrpBuild.Notify( caller, "Only admins can clean up everyone's builds" ); return; }
			foreach ( var go in Game.ActiveScene.GetAllObjects( true ).Where( g => g.Tags.Has( SwrpBuild.PropTag ) && g.Parent is Sandbox.Scene ).ToArray() ) { go.Destroy(); gone++; }
			foreach ( var link in Game.ActiveScene.GetAllComponents<SwrpConstraintLink>().ToArray() ) { link.GameObject.Destroy(); gone++; }
			_stacks.Clear();
		}
		else if ( _stacks.TryGetValue( caller.Id, out var stack ) )
		{
			foreach ( var entry in stack.ToArray() )
			{
				var constraint = entry.Objects.Any( o => o.IsValid() && o.Components.Get<SwrpConstraintLink>() is not null );
				if ( what == 1 && !constraint ) continue;
				foreach ( var go in entry.Objects ) if ( go.IsValid() ) { go.Destroy(); gone++; }
				stack.Remove( entry );
			}
		}
		var label = what switch { 1 => "your constraints", 2 => "every build on the server", _ => "everything you built" };
		SwrpBuild.Notify( caller, $"Cleaned up {label} ({gone} objects)" );
		SwrpBuild.Log( caller, $"cleaned up {label} ({gone} objects)" );
		SendHistory( caller );
	}
}
