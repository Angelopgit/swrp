using System;
using System.Text;

namespace Swrp;

/// <summary>
/// A data file (map JSON, the Tatooine terrain) wrapped as a compiled asset. Clients only get compiled assets, so a loose
/// Assets/data file was readable on the host and missing on every client (doors without geometry, empty holo-map, no
/// Tatooine ground; found with the first second-client test, 2026-10-21). <c>MapSource/wrap_data.py</c> writes
/// <c>&lt;file&gt;.swdat</c> beside each data file: rerun it after regenerating map or terrain data.
/// </summary>
[AssetType( Name = "SWRP Data", Extension = "swdat", Category = "SWRP" )]
public sealed class SwrpDataFile : GameResource
{
	[Property] public string Text { get; set; }
	[Property] public bool Base64 { get; set; }

	static SwrpDataFile Wrapped( string path ) => ResourceLibrary.Get<SwrpDataFile>( path + ".swdat" );

	public static bool Exists( string path ) => FileSystem.Mounted.FileExists( path ) || Wrapped( path ) is not null;

	/// <summary> The loose file when this machine has it, else the wrapped copy; null when neither exists. </summary>
	public static string ReadText( string path ) =>
		FileSystem.Mounted.FileExists( path ) ? FileSystem.Mounted.ReadAllText( path ) : Wrapped( path )?.Text;

	public static byte[] ReadBytes( string path )
	{
		if ( FileSystem.Mounted.FileExists( path ) ) return FileSystem.Mounted.ReadAllBytes( path ).ToArray();
		if ( Wrapped( path ) is not { Text: { } text } d ) return null;
		return d.Base64 ? Convert.FromBase64String( text ) : Encoding.UTF8.GetBytes( text );
	}
}
