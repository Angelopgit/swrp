using System;

namespace Swrp;

/// <summary>
/// Host: which staff role each staff command needs. The code tables (<c>Commands</c> in SwrpStaff and SwrpStaffTools) are the
/// defaults; Senior Admins override them from the staff panel's PERMISSIONS tab or <c>/perm</c>, saved in
/// <c>swrp/permissions.json</c> as command -> role name.
/// </summary>
public static class SwrpPermissions
{
	const string FilePath = "swrp/permissions.json";

	/// <summary> Never overridable, so nobody can lock the Senior Admins out or hand the grid to a lower role. </summary>
	static readonly HashSet<string> Fixed = new() { "/perm", "/setrole" };

	static Dictionary<string, string> _data;

	static Dictionary<string, string> Data => _data ??= FileSystem.Data.ReadJson<Dictionary<string, string>>( FilePath, null ) ?? new();

	public static StaffRole Need( string cmd, StaffRole fallback ) =>
		!Fixed.Contains( cmd ) && Data.TryGetValue( cmd, out var name ) && Enum.TryParse<StaffRole>( name, true, out var role ) ? role : fallback;

	public static bool IsOverridden( string cmd ) => Data.ContainsKey( cmd );

	/// <summary> null role = back to the default. </summary>
	public static bool Set( string cmd, StaffRole? role )
	{
		if ( Fixed.Contains( cmd ) ) return false;
		if ( role is { } r ) Data[cmd] = r.ToString();
		else Data.Remove( cmd );
		try
		{
			FileSystem.Data.CreateDirectory( "swrp" );
			FileSystem.Data.WriteJson( FilePath, _data );
		}
		catch ( Exception e )
		{
			Log.Error( $"[SWRP] Failed to save permissions: {e.Message}" );
		}
		return true;
	}

	/// <summary> "cmd|need|default" for every staff command, for the panel grid. </summary>
	public static List<string> Rows() => SwrpStaff.DefaultRoles().Concat( SwrpStaffTools.DefaultRoles() )
		.Where( kv => !Fixed.Contains( kv.Key ) )
		.OrderBy( kv => kv.Key )
		.Select( kv => $"{kv.Key}|{Need( kv.Key, kv.Value )}|{kv.Value}" )
		.ToList();
}
