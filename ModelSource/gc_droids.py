"""
The NPC battle droids from the Galactic Contention Squad mod (Steam workshop 2428425228, exported 2026-10-13 to
X:\\SWRP Projects\\Squad_Mod_2428425228\\Export by its Tools\\GcExport): the B1 rifleman and the B2, put on the Citizen
human rig so they share the animgraph (and the droid clip set from gc_droid_anims.py). LOCAL ONLY: this is the GC team's
work; never publish or commit it, and ask them before it ships (Squad_Mod_2428425228\\ASSESSMENT.md).

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/gc_droids.py [-- b1 b2]

Same steps as bf2_clones.py: the droid's Squad skeleton (3ds Max biped, "Bip01_*") is scaled to the rig's leg length and
posed onto the Citizen rest, the mesh is baked in that pose and its weights renamed to Citizen bones. Unlike the BF2
droids, the rig's joints are then moved onto the droid's own (lengths only: every bone keeps the Citizen orientation), so
elbows and knees bend where the droid's do; the vmdl's proportion_trick carries the difference onto every clip.
Writes models/troopers/gc_droid_<id>_<rev>.fbx, its materials (models/troopers/materials/gc_*.vmat) and gc_droid_<id>.vmdl.
"""
import os
import re
import sys
import json
import uuid

import bpy
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import bf2_props as bp
import bf2_clones as bc

GC = r"X:\SWRP Projects\Squad_Mod_2428425228\Export"
OUT = os.path.join( bp.ASSETS, "models", "troopers" )
MAT_DIR = os.path.join( OUT, "materials" )
REV = bp.REV

DROIDS = {
	"b1": { "glb": r"ANE_BASE\Art\Soldier2\Droid\B1\Rifleman\B1_Rifleman.glb", "template": "clone_droid_b1_drill.vmdl" },
	"b2": { "glb": r"ANE_BASE\Art\Soldier2\Droid\B2_SBD\B2_new.glb", "template": "clone_droid_b2_drill.vmdl" },
}

# Squad biped bone -> Citizen bone
MAP = { "Bip01_Pelvis": "pelvis", "Bip01_Spine": "spine_0", "Bip01_Spine1": "spine_1", "Bip01_Spine2": "spine_2",
	"Bip01_Neck": "neck_0", "Bip01_Head": "head" }
for s in "LR":
	b = f"Bip01_{s}_"
	MAP.update( { b + "Clavicle": f"clavicle_{s}", b + "UpperArm": f"arm_upper_{s}", b + "Forearm": f"arm_lower_{s}",
		b + "ForeTwist": f"arm_lower_{s}_twist1", b + "Hand": f"hand_{s}", b + "Thigh": f"leg_upper_{s}", b + "Calf": f"leg_lower_{s}",
		b + "Foot": f"ankle_{s}", b + "Toe0": f"ball_{s}" } )
	# biped fingers: 0 thumb, 1 index, 2 middle, 3 ring, 4 pinky; three joints each
	for n, f in enumerate( ( "thumb", "index", "middle", "ring", "pinky" ) ):
		for j, suffix in enumerate( ( "", "1", "2" ) ):
			MAP[f"{b}Finger{n}{suffix}"] = f"finger_{f}_{j}_{s}"
SPLIT = {}
SEGMENTS = [( "Bip01_Spine", "Bip01_Spine1" ), ( "Bip01_Spine1", "Bip01_Spine2" ), ( "Bip01_Spine2", "Bip01_Neck" ), ( "Bip01_Neck", "Bip01_Head" )]
for s in "LR":
	b = f"Bip01_{s}_"
	SEGMENTS += [( b + "Clavicle", b + "UpperArm" ), ( b + "UpperArm", b + "Forearm" ), ( b + "Forearm", b + "Hand" ),
		( b + "Hand", b + "Finger2", ( b + "Finger4", b + "Finger1" ) ),
		( b + "Thigh", b + "Calf" ), ( b + "Calf", b + "Foot" ), ( b + "Foot", b + "Toe0" )]
	for n in range( 5 ):
		SEGMENTS += [( f"{b}Finger{n}", f"{b}Finger{n}1" ), ( f"{b}Finger{n}1", f"{b}Finger{n}2" )]

# bf2_clones' posing and weight helpers read these module tables
bc.MAP, bc.SPLIT, bc.SEGMENTS = MAP, SPLIT, SEGMENTS


def log( *a ):
	print( "GCDROID", *a, flush=True )


def import_glb( path ):
	before = set( bpy.data.objects )
	bpy.ops.import_scene.gltf( filepath=path )
	new = [o for o in bpy.data.objects if o not in before]
	arm = next( o for o in new if o.type == "ARMATURE" )
	meshes = [o for o in new if o.type == "MESH" and o.vertex_groups]
	for o in new:
		if o is not arm and o not in meshes:
			bpy.data.objects.remove( o, do_unlink=True )
	# the glTF importer keeps a rest-pose action around; the bake must see the bind pose only
	if arm.animation_data:
		arm.animation_data.action = None
	for pb in arm.pose.bones:
		pb.matrix_basis = Matrix()
	return arm, meshes


def fit_by_legs( squad, rig ):
	"""Turn the droid to face the rig's way (Unreal characters face +X), scale it to the Citizen leg length (thigh to
	ankle) and put its pelvis on the rig's."""
	def side( arm, l, r ):
		v = bc.world_head( arm, l, False ) - bc.world_head( arm, r, False )
		return Vector( ( v.x, v.y, 0 ) ).normalized()
	turn = side( squad, "Bip01_L_Thigh", "Bip01_R_Thigh" ).rotation_difference( side( rig, "leg_upper_L", "leg_upper_R" ) )
	squad.matrix_world = turn.to_matrix().to_4x4() @ squad.matrix_world
	bpy.context.view_layer.update()
	w =( bc.world_head( squad, "Bip01_L_Foot", False ) - bc.world_head( squad, "Bip01_L_Thigh", False ) ).length
	t = ( bc.world_head( rig, "ankle_L", False ) - bc.world_head( rig, "leg_upper_L", False ) ).length
	s = t / w
	squad.matrix_world = Matrix.Scale( s, 4 ) @ squad.matrix_world
	bpy.context.view_layer.update()
	off = bc.world_head( rig, "pelvis", False ) - bc.world_head( squad, "Bip01_Pelvis", False )
	squad.matrix_world = Matrix.Translation( off ) @ squad.matrix_world
	bpy.context.view_layer.update()
	return s


# ----------------------------------------------------------------------------------------------------------- materials
def tex_png( obj_path ):
	p = obj_path.rsplit( ".", 1 )[0].lstrip( "/" )
	full = os.path.join( GC, *p.split( "/" ) ) + ".png"
	return full if os.path.exists( full ) else None


def find_json( name ):
	for root, _, files in os.walk( os.path.join( GC, "ANE_BASE", "Art", "Soldier2", "Droid" ) ):
		if name + ".json" in files:
			return os.path.join( root, name + ".json" )
	return None


def material( ue_name ):
	"""models/troopers/materials/gc_<name>.vmat from the UE material's json (colour, normal, AORM split, emissive)."""
	name = "gc_" + re.sub( r"[^a-z0-9_]", "_", ue_name.lower() )
	vmat = os.path.join( MAT_DIR, name + ".vmat" )
	src = find_json( ue_name )
	if not src:
		log( "WARNING no material json for", ue_name )
		return name
	data = json.load( open( src ) )
	tex = { k: tex_png( v["ObjectPath"] ) for k, v in data.get( "Textures", {} ).items() if isinstance( v, dict ) and "ObjectPath" in v }
	rel = "models/troopers/materials"
	lines, flags = [], []
	color = tex.get( "Base Color" ) or tex.get( "PM_Diffuse" )
	normal = tex.get( "Normal" ) or tex.get( "PM_Normals" )
	orm = tex.get( "AORM" ) or tex.get( "PM_SpecularMasks" )
	emis = tex.get( "Emissive" ) or tex.get( "PM_Emissive" )
	os.makedirs( MAT_DIR, exist_ok=True )
	if color:
		a, w, h = bp.read( color )
		bp.write( os.path.join( MAT_DIR, f"{name}_color.png" ), a[:, :, :3], w, h )
		lines.append( f'TextureColor "{rel}/{name}_color.png"' )
	else:
		lines.append( 'TextureColor "materials/default/default_color.tga"' )
	if normal:
		a, w, h = bp.read( normal )
		x, y = a[:, :, 0] * 2 - 1, a[:, :, 1] * 2 - 1
		z = np.sqrt( np.clip( 1 - x * x - y * y, 0, 1 ) )
		# Unreal normal maps are DirectX style (green down); s&box reads OpenGL style
		bp.write( os.path.join( MAT_DIR, f"{name}_normal.png" ), np.dstack( ( a[:, :, 0], 1.0 - a[:, :, 1], z * 0.5 + 0.5 ) ), w, h )
		lines.append( f'TextureNormal "{rel}/{name}_normal.png"' )
	else:
		lines.append( 'TextureNormal "materials/default/default_normal.tga"' )
	if orm:
		a, w, h = bp.read( orm )
		bp.write( os.path.join( MAT_DIR, f"{name}_ao.png" ), a[:, :, 0], w, h )
		bp.write( os.path.join( MAT_DIR, f"{name}_rough.png" ), a[:, :, 1], w, h )
		metal = a[:, :, 2]
		if metal.mean() > 0.7:
			# all-metal droids (the B2) go near black without Unreal's reflection captures in the ship's dim rooms
			metal = metal * 0.45
		bp.write( os.path.join( MAT_DIR, f"{name}_metal.png" ), metal, w, h )
		flags.append( "F_METALNESS_TEXTURE 1" )
		lines += [f'TextureAmbientOcclusion "{rel}/{name}_ao.png"', f'TextureRoughness "{rel}/{name}_rough.png"',
			f'TextureMetalness "{rel}/{name}_metal.png"']
	else:
		lines += ['TextureAmbientOcclusion "materials/default/default_ao.tga"', 'TextureRoughness "materials/default/default_rough.tga"', 'g_flMetalness "0.400"']
	if emis:
		a, w, h = bp.read( emis )
		bp.write( os.path.join( MAT_DIR, f"{name}_glow.png" ), a[:, :, :3], w, h )
		tint = data.get( "Colors", {} ).get( "Emissive Tint" )
		tint = f"{tint['R']:.3f} {tint['G']:.3f} {tint['B']:.3f}" if tint else "1.000 1.000 1.000"
		flags.append( "F_SELF_ILLUM 1" )
		lines += [f'TextureSelfIllumMask "{rel}/{name}_glow.png"', f'g_vSelfIllumTint "[{tint} 0.000000]"',
			'g_flSelfIllumScale "%.3f"' % min( 4.0, data.get( "Scalars", {} ).get( "Emissive", 1.0 ) / 2.5 )]
	text = "// Generated by ModelSource/gc_droids.py from Galactic Contention textures (local only, never publish)\n\nLayer0\n{\n"
	text += '\tshader "shaders/complex.shader"\n' + "".join( f"\t{f}\n" for f in flags ) + "\n"
	text += "".join( f"\t{l}\n" for l in lines )
	text += '\tg_vColorTint "[1.000000 1.000000 1.000000 0.000000]"\n\tg_flModelTintAmount "1.000"\n\tg_bFogEnabled "1"\n}\n'
	with open( vmat, "w", newline="\n" ) as fh:
		fh.write( text )
	return name


# ----------------------------------------------------------------------------------------------------------- rig
def joints_onto_droid( rig, squad ):
	"""Move the rig's joints to the posed droid's (translation only); unmapped bones ride with their nearest mapped parent."""
	inv = rig.matrix_world.inverted()
	want = { c: inv @ bc.world_head( squad, b ) for b, c in MAP.items() if b in squad.pose.bones and c in rig.data.bones }
	bpy.context.view_layer.objects.active = rig
	bpy.ops.object.mode_set( mode="EDIT" )
	eb = rig.data.edit_bones
	for b in eb:
		b.use_connect = False
	offset = {}
	moved = 0.0
	def visit( b ):
		nonlocal moved
		d = ( want[b.name] - b.head ) if b.name in want else offset.get( b.parent.name, Vector() ) if b.parent else Vector()
		offset[b.name] = d
		moved = max( moved, d.length )
		for c in b.children:
			visit( c )
	for r in [b for b in eb if b.parent is None]:
		visit( r )
	for b in eb:
		d = offset[b.name]
		b.head += d
		b.tail += d
	bpy.ops.object.mode_set( mode="OBJECT" )
	return moved


def build( did, spec ):
	bpy.ops.wm.open_mainfile( filepath=bc.RIG_BLEND )
	rig = bpy.data.objects["Human Citizen Armature"]
	for o in list( bpy.data.objects ):
		if o.type == "MESH":
			bpy.data.objects.remove( o, do_unlink=True )
	out_coll = bpy.data.collections["Output"]

	squad, meshes = import_glb( os.path.join( GC, spec["glb"] ) )
	s = fit_by_legs( squad, rig )
	worst = bc.pose_onto( squad, rig )
	mats = []
	for m in meshes:
		for i, slot in enumerate( m.data.materials ):
			vm = material( slot.name.split( "." )[0] )
			mat = bpy.data.materials.get( vm + ".vmat" ) or bpy.data.materials.new( vm + ".vmat" )
			m.data.materials[i] = mat
			mats.append( vm )
	bc.bake_pose( meshes )
	bc.remap_weights( meshes, squad )
	moved = joints_onto_droid( rig, squad )
	bpy.data.objects.remove( squad, do_unlink=True )
	if len( meshes ) > 1:
		bpy.ops.object.select_all( action="DESELECT" )
		for m in meshes:
			m.select_set( True )
		bpy.context.view_layer.objects.active = meshes[0]
		bpy.ops.object.join()
	body = meshes[0]
	body.name = body.data.name = "body"
	log( did, f"scale {s:.4f}, worst segment off by {worst * 100:.2f} cm before the joints moved, joints moved up to {moved * 100:.1f} cm, "
		f"{len( body.data.vertices )} vertices" )
	for c in list( body.users_collection ):
		c.objects.unlink( body )
	out_coll.objects.link( body )
	body.parent = rig
	md = body.modifiers.new( "Armature", "ARMATURE" )
	md.object = rig

	# the bindPose take (see bf2_clones.build)
	for a in list( bpy.data.actions ):
		bpy.data.actions.remove( a )
	bpy.context.view_layer.objects.active = rig
	rig.animation_data_create()
	act = bpy.data.actions.new( "bindPose" )
	rig.animation_data.action = act
	bpy.ops.object.mode_set( mode="POSE" )
	for pb in rig.pose.bones:
		pb.location = ( 0, 0, 0 )
		pb.rotation_mode = "QUATERNION"
		pb.rotation_quaternion = ( 1, 0, 0, 0 )
		pb.scale = ( 1, 1, 1 )
		for path in ( "location", "rotation_quaternion", "scale" ):
			pb.keyframe_insert( path, frame=0 )
			pb.keyframe_insert( path, frame=1 )
	bpy.ops.object.mode_set( mode="OBJECT" )
	bpy.context.scene.frame_start, bpy.context.scene.frame_end = 0, 1
	bpy.ops.object.select_all( action="DESELECT" )
	rig.select_set( True )
	body.select_set( True )
	bpy.context.view_layer.objects.active = rig
	fbx = f"gc_droid_{did}_{REV}"
	bpy.ops.export_scene.fbx( filepath=os.path.join( OUT, fbx + ".fbx" ), use_selection=True, object_types={ "ARMATURE", "MESH" },
		add_leaf_bones=False, primary_bone_axis="X", secondary_bone_axis="Z", bake_anim=True, bake_anim_use_all_actions=False,
		bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, mesh_smooth_type="FACE" )
	with open( os.path.join( OUT, fbx + ".fbx.meta" ), "w", newline="\n" ) as fh:
		fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	# a .blend next to the scripts for the animation retarget (gc_droid_anims.py reads the posed droid joints from it)
	bpy.ops.wm.save_as_mainfile( filepath=os.path.join( GC, "sbox", f"gc_droid_{did}.blend" ) )
	write_vmdl( did, spec, fbx, sorted( set( mats ) ) )
	log( did, "exported", fbx, mats )


def write_vmdl( did, spec, fbx, mats ):
	v = open( os.path.join( OUT, spec["template"] ), encoding="utf-8" ).read()
	v = re.sub( r'models/troopers/clone_droid_\w+?\.fbx', f"models/troopers/{fbx}.fbx", v )
	remaps = "".join( f'\t\t\t\t\t\t\t{{\n\t\t\t\t\t\t\t\tfrom = "{m}.vmat"\n\t\t\t\t\t\t\t\tto = "models/troopers/materials/{m}.vmat"\n\t\t\t\t\t\t\t}},\n' for m in mats )
	v = re.sub( r"(remaps =\s*\[\s*\n).*?(\n\s*\]\s*\n\s*use_global_default)", lambda mm: mm.group( 1 ) + remaps.rstrip( "\n" ) + mm.group( 2 ), v, count=1, flags=re.S )
	anim_list = f"models/troopers/gc_droid_{did}_animationlist.vmdl_prefab"
	if os.path.exists( os.path.join( bp.ASSETS, anim_list ) ):
		v = v.replace( "models/clones/clone_animationlist.vmdl_prefab", anim_list )
	path = os.path.join( OUT, f"gc_droid_{did}.vmdl" )
	with open( path, "w", encoding="utf-8", newline="\n" ) as fh:
		fh.write( v )
	if not os.path.exists( path + ".meta" ):
		with open( path + ".meta", "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )


def main():
	os.makedirs( os.path.join( GC, "sbox" ), exist_ok=True )
	only = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	for did, spec in DROIDS.items():
		if not only or did in only:
			build( did, spec )


if __name__ == "__main__":
	main()
