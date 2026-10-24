"""
Wraps every Assets/data file the game reads at runtime as a SwrpDataFile asset (<file>.swdat beside it), because clients
only receive compiled assets: the loose JSON/bin exists on the host alone (Code/Data/SwrpDataFile.cs).
Run with system Python after notorious_build.py, notorious_scene.py, tatooine_terrain.py or the droideka/GMod aim bakes,
then compile the .swdat files in the editor (asset_compile, or they compile on next load).
"""
import base64, json, os, sys

ROOT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "data" )
FILES = [ "notorious_doors.json", "notorious_holo.json", "droideka_poses.json", "gmod_aim.json",
	"tatooine/terrain.json", "tatooine/props.json", "tatooine/terrain.bin" ]

def wrap( rel ):
	src = os.path.join( ROOT, rel )
	raw = open( src, "rb" ).read()
	binary = rel.endswith( ".bin" )
	text = base64.b64encode( raw ).decode() if binary else raw.decode( "utf-8-sig" )
	out = src + ".swdat"
	with open( out, "w", encoding="utf-8", newline="\n" ) as fh:
		json.dump( { "Text": text, "Base64": binary }, fh )
	back = json.load( open( out, encoding="utf-8" ) )["Text"]
	assert ( base64.b64decode( back ) if binary else back.encode( "utf-8" ) ) == ( raw if binary else text.encode( "utf-8" ) )
	print( f"wrapped data/{rel} ({len( raw ) // 1024} KB)" )

if __name__ == "__main__":
	for rel in sys.argv[1:] or FILES:
		wrap( rel )
