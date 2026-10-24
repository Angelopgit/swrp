"""
The droideka NPC from Battlefront II (2017), from the local extraction in X:\\SWRP Projects\\BF2_Extracted (Angelo's own game,
private fan research; LOCAL ONLY, never publish or commit). BF2's rig comes with it (104 bones, no animations: the exporter
can't pull Frostbite animation), so the game poses the bones itself (Code/Npc/DroidekaRig.cs): rolled into a wheel, the
unfold, the deployed stance, firing.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/bf2_droideka.py

Writes Assets/models/droids/bf2_droideka_<rev>.fbx (skinned, bind pose = BF2's deployed stance), bf2_droideka.vmdl, its
material/PNGs, and Assets/data/droideka_poses.json: per pose, each bone's model-space rotation from the bind pose as a
quaternion in Blender's axes (w, x, y, z), plus the reference points the game uses to line Blender's axes up with the model's
(it measures the same bones at runtime) and the rolled wheel's centre and radius. The rolled pose is laid out bone by bone
in ROLLED below (curl the spine forward into a ring round the hip ball, fold the legs and arms inside); checked on renders.
"""
import os
import sys
import json
import math
import time
import uuid

import bpy
from mathutils import Vector, Matrix, Quaternion

HERE = os.path.dirname( os.path.abspath( __file__ ) )
PROJECT = os.path.dirname( HERE )
sys.path.insert( 0, HERE )
import bf2_props as bp

SRC = r"X:\SWRP Projects\BF2_Extracted\Models\Droid_Droideka"
OUT = os.path.join( PROJECT, "Assets", "models", "droids" )
TEX = os.path.join( OUT, "bf2tex" )
DATA = os.path.join( PROJECT, "Assets", "data", "droideka_poses.json" )
REV = format( int( time.time() ), "x" )[-6:]
IN = 1 / 39.37

# Rolled into a wheel. Per bone, applied down the hierarchy in bind-pose world axes (Blender: front -Y, up +Z, left +X):
#   ["X"|"Y"|"Z", degrees]: turn about that axis; ["DIR", tip, x, y, z]: point the bone (head -> tip bone) along a direction;
#   ["AT", tip, x, y, z]: point it at a spot (inches).
ROLLED = {
	"Spine": [["X", 30]], "Spine1": [["X", 30]], "Spine2": [["X", 30]], "Spine3": [["X", 30]], "Spine4": [["X", 30]], "Spine5": [["X", 30]],
	"LeftArm": [["AT", "LeftForeArm", 5, -12, 22]], "LeftForeArm": [["AT", "LeftHand", 4, 2, 24]],
	"RightArm": [["AT", "RightForeArm", -5, -12, 22]], "RightForeArm": [["AT", "RightHand", -4, 2, 24]],
	"LeftUpLeg": [["DIR", "LeftLeg", -0.15, -0.45, -0.88]], "LeftLeg": [["DIR", "LeftToeBase", 0, 0.55, 0.83]],
	"RightUpLeg": [["DIR", "RightLeg", 0.15, -0.45, -0.88]], "RightLeg": [["DIR", "RightToeBase", 0, 0.55, 0.83]],
	"BackUpLeg": [["DIR", "BackLeg", 0, -0.2, -0.98]], "BackLeg": [["DIR", "BackToeBase", 0, -0.95, 0.3]],
}
# the rolled wheel (Blender inches, measured on the renders): its centre and radius, turning about X
WHEEL_CENTRE = ( 0.0, -5.5, 22.8 )
WHEEL_RADIUS = 22.0
# bones the game measures to line the axes up
REFS = ["Hips", "Head", "LeftArm"]


def axis_rot( spec ):
	q = Quaternion()
	for ax, deg in spec:
		q = Quaternion( Vector( { "X": ( 1, 0, 0 ), "Y": ( 0, 1, 0 ), "Z": ( 0, 0, 1 ) }[ax] ), math.radians( deg ) ) @ q
	return q


def solve_pose( arm, pose ):
	"""Model-space rotation from the bind pose for every bone (and its new head position), following ROLLED's rules."""
	W = arm.matrix_world
	rest = { b.name: W @ b.matrix_local for b in arm.data.bones }
	delta, head = {}, {}
	for b in arm.data.bones:                     # parents come first
		h0 = rest[b.name].to_translation()
		spec = pose.get( b.name, [] )
		if b.parent:
			pd, ph, ph0 = delta[b.parent.name], head[b.parent.name], rest[b.parent.name].to_translation()
			head[b.name] = ph + pd @ ( h0 - ph0 )
		else:
			pd = Quaternion()
			head[b.name] = h0
		if spec and spec[0][0] in ( "DIR", "AT" ):
			kind, tip = spec[0][0], spec[0][1]
			cur = pd @ ( rest[tip].to_translation() - h0 )
			target = Vector( spec[0][2:5] ) if kind == "DIR" else Vector( spec[0][2:5] ) * IN - head[b.name]
			delta[b.name] = cur.rotation_difference( target.normalized() * cur.length ) @ pd
		else:
			delta[b.name] = pd @ axis_rot( spec )
	return rest, delta, head


def destroyed_material():
	"""The burnt-out wreck (BF2's own T_Droideka_01_Destoyed_CS): swapped on when a droideka dies (DroidekaRig)."""
	name = "bf2_droideka_destroyed"
	cs = os.path.join( SRC, "Textures", "T_Droideka_01_Destoyed_CS.png" )
	nrm = os.path.join( SRC, "Textures", "T_Droideka_01_NMA.png" )
	lines, metal = bp.convert_textures( cs, nrm, TEX, name, metal_scale=0.25 )
	bp.write_vmat( os.path.join( TEX, name + ".vmat" ), lines, metal )
	print( "DEKA wrote", name )


def main():
	bpy.ops.wm.read_factory_settings( use_empty=True )
	bpy.ops.import_scene.fbx( filepath=os.path.join( SRC, "droideka_01_mesh.fbx" ) )
	for o in list( bpy.data.objects ):
		if o.type == "MESH" and "lod0" not in o.name.lower():
			bpy.data.objects.remove( o, do_unlink=True )
	arm = next( o for o in bpy.data.objects if o.type == "ARMATURE" )
	mesh = next( o for o in bpy.data.objects if o.type == "MESH" )

	# poses first, in the imported (bind) frame
	rest, delta, head = solve_pose( arm, ROLLED )
	W = arm.matrix_world
	refs = { n: list( ( W @ arm.data.bones[n].matrix_local ).to_translation() / IN ) for n in REFS }
	poses = { "rolled": { n: [q.w, q.x, q.y, q.z] for n, q in delta.items() if abs( q.w ) < 0.99999 } }
	os.makedirs( os.path.dirname( DATA ), exist_ok=True )
	with open( DATA, "w", newline="\n" ) as fh:
		json.dump( { "note": "ModelSource/bf2_droideka.py: Blender-axis model-space bone rotations from the bind pose (w,x,y,z); refs/wheel in Blender inches",
			"refs": refs, "wheel_centre": WHEEL_CENTRE, "wheel_radius": WHEEL_RADIUS, "poses": poses }, fh, indent=1 )

	# material
	name = "bf2_droideka"
	cs = os.path.join( SRC, "Textures", "T_Droideka_01_CS.png" )
	nrm = os.path.join( SRC, "Textures", "T_Droideka_01_NMA.png" )
	lines, metal = bp.convert_textures( cs, nrm, TEX, name, metal_scale=0.5 )
	em = os.path.join( SRC, "Textures", "T_Droideka_01_EMISS.png" )
	if os.path.exists( em ):
		a, w, h = bp.read( em )
		bp.write( os.path.join( TEX, f"{name}_emiss.png" ), a[:, :, :3], w, h )
		lines += ["\tF_SELF_ILLUM 1", f'\tTextureSelfIllumMask "models/droids/bf2tex/{name}_emiss.png"', '\tg_flSelfIllumBrightness "4.000"']
	vmat = os.path.join( TEX, name + ".vmat" )
	bp.write_vmat( vmat, lines, metal )
	mat = bpy.data.materials.new( f"models/droids/bf2tex/{name}.vmat" )
	mesh.data.materials.clear()
	mesh.data.materials.append( mat )

	destroyed_material()

	# export skinned, bind pose
	bpy.ops.object.select_all( action="DESELECT" )
	arm.select_set( True )
	mesh.select_set( True )
	bpy.context.view_layer.objects.active = arm
	os.makedirs( OUT, exist_ok=True )
	fbx = f"{name}_{REV}"
	bpy.ops.export_scene.fbx( filepath=os.path.join( OUT, fbx + ".fbx" ), use_selection=True, object_types={ "ARMATURE", "MESH" },
		add_leaf_bones=False, primary_bone_axis="X", secondary_bone_axis="Z", bake_anim=False, mesh_smooth_type="FACE" )
	with open( os.path.join( OUT, fbx + ".fbx.meta" ), "w", newline="\n" ) as fh:
		fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	with open( os.path.join( OUT, name + ".vmdl" ), "w", newline="\n" ) as fh:
		fh.write( VMDL % { "fbx": f"models/droids/{fbx}.fbx" } )
	revs = sorted( ( f for f in os.listdir( OUT ) if f.startswith( name + "_" ) and f.endswith( ".fbx" ) ),
		key=lambda f: os.path.getmtime( os.path.join( OUT, f ) ) )
	for old in revs[:-2]:
		for fn in ( old, old + ".meta" ):
			try:
				os.remove( os.path.join( OUT, fn ) )
			except OSError:
				pass
	print( f"DEKA exported {fbx}, {len( mesh.data.vertices )} verts, {len( arm.data.bones )} bones, rolled pose {len( poses['rolled'] )} bones" )


# import_scale 0.3937: Blender metres otherwise come in 2.54x too big (measured in game with swrp_droideka_info, 2026-10-13)
VMDL = """<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->
{
	rootNode =
	{
		_class = "RootNode"
		children =
		[
			{
				_class = "RenderMeshList"
				children =
				[
					{
						_class = "RenderMeshFile"
						filename = "%(fbx)s"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = 0.3937
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{
							exclude_by_default = false
							exception_list = [  ]
						}
					},
				]
			},
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}
}
"""

if __name__ == "__main__":
	if "--material-only" in sys.argv:
		destroyed_material()
	else:
		main()
