"""
Battlefront II's astromech droids (R2-D2 and R5-D4) for the ship's ambient crew and the character screen (Code/Map/SwrpCrew.cs),
from the local extraction in X:\\SWRP Projects\\BF2_Extracted (Angelo's own game, private fan research; LOCAL ONLY, never
publish or commit). They keep BF2's rig: the dome is the "Head" bone, which SwrpCrew turns.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/bf2_astromechs.py

Exported like ModelSource/bf2_droideka.py (skinned, metres, the vmdl imports at 0.3937 so BF2's metres come in as inches; the
front ends up facing +X). Writes Assets/models/crew/bf2_<name>_<rev>.fbx, bf2_<name>.vmdl and its vmats/PNGs in bf2tex/.
"""
import os
import sys
import time
import uuid

import bpy

HERE = os.path.dirname( os.path.abspath( __file__ ) )
PROJECT = os.path.dirname( HERE )
sys.path.insert( 0, HERE )
import bf2_props as bp
import bf2_droideka as dk           # its VMDL template (main() is guarded)

BF2 = r"X:\SWRP Projects\BF2_Extracted\Models"
OUT = os.path.join( PROJECT, "Assets", "models", "crew" )
TEX = os.path.join( OUT, "bf2tex" )
REV = format( int( time.time() ), "x" )[-6:]

# name -> (source folder/fbx, texture stem per BF2 material name)
DROIDS = {
	"r2d2": ( "Droid_R2D2/r2d2_01_mesh.fbx", { "R2D2_1001_MAT": "T_R2D2_1001", "R2D2_1002_MAT": "T_R2D2_1002" } ),
	"r5d4": ( "Droid_R5D4/r5d4_01_mesh.fbx", None ),
}


def build( name, src, stems ):
	bpy.ops.wm.read_factory_settings( use_empty=True )
	fbx_src = os.path.join( BF2, src )
	bpy.ops.import_scene.fbx( filepath=fbx_src )
	for o in list( bpy.data.objects ):
		if o.type == "MESH" and "lod0" not in o.name.lower():
			bpy.data.objects.remove( o, do_unlink=True )
	arm = next( o for o in bpy.data.objects if o.type == "ARMATURE" )
	meshes = [o for o in bpy.data.objects if o.type == "MESH"]
	texdir = os.path.join( os.path.dirname( fbx_src ), "Textures" )
	textures = [f for f in os.listdir( texdir ) if f.lower().endswith( "_cs.png" )] if os.path.isdir( texdir ) else []

	for m in meshes:
		key = m.name.split( ":" )[0]
		stem = ( stems or {} ).get( key ) or ( textures[0][:-7] if textures else None )
		mat_name = f"bf2_{name}_{( stem or 'flat' ).lower()}"
		cs, nrm = bp.texture_set( texdir, stem ) if stem else ( None, None )
		lines, metal = bp.convert_textures( cs, nrm, TEX, mat_name, metal_scale=0.5 )
		bp.write_vmat( os.path.join( TEX, mat_name + ".vmat" ), lines, metal )
		m.data.materials.clear()
		m.data.materials.append( bpy.data.materials.new( f"models/crew/bf2tex/{mat_name}.vmat" ) )

	bpy.ops.object.select_all( action="DESELECT" )
	arm.select_set( True )
	for m in meshes:
		m.select_set( True )
	bpy.context.view_layer.objects.active = arm
	os.makedirs( OUT, exist_ok=True )
	fbx = f"bf2_{name}_{REV}"
	bpy.ops.export_scene.fbx( filepath=os.path.join( OUT, fbx + ".fbx" ), use_selection=True, object_types={ "ARMATURE", "MESH" },
		add_leaf_bones=False, primary_bone_axis="X", secondary_bone_axis="Z", bake_anim=False, mesh_smooth_type="FACE" )
	with open( os.path.join( OUT, fbx + ".fbx.meta" ), "w", newline="\n" ) as fh:
		fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	with open( os.path.join( OUT, f"bf2_{name}.vmdl" ), "w", newline="\n" ) as fh:
		fh.write( dk.VMDL % { "fbx": f"models/crew/{fbx}.fbx" } )
	revs = sorted( ( f for f in os.listdir( OUT ) if f.startswith( f"bf2_{name}_" ) and f.endswith( ".fbx" ) ),
		key=lambda f: os.path.getmtime( os.path.join( OUT, f ) ) )
	for old in revs[:-2]:
		for fn in ( old, old + ".meta" ):
			try:
				os.remove( os.path.join( OUT, fn ) )
			except OSError:
				pass
	print( f"ASTRO {name}: {fbx}, {sum( len( m.data.vertices ) for m in meshes )} verts, {len( arm.data.bones )} bones" )


def main():
	only = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	for name, ( src, stems ) in DROIDS.items():
		if only and name not in only:
			continue
		build( name, src, stems )


if __name__ == "__main__":
	main()
