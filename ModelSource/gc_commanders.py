"""
Battalion commanders from the Galactic Contention Squad mod (Steam workshop 2428425228, exported to
X:\\SWRP Projects\\Squad_Mod_2428425228\\Export): Commander Wolffe (104th) and Captain Rex (501st) in Phase II armour, put on
the trooper rig like the BF2 clones (bf2_clones.py), so they share the animgraph, the clone and Squad animation sets, emotes
and the rifle hold. Worn by a battalion's Commander (Code/Characters/SwrpCommanders.cs). Battlefront II (2017) has no Rex
of its own (checked its asset lists 2026-10-16), so both come from GC. LOCAL ONLY: this is the GC team's work; never publish
or commit it.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/gc_commanders.py [-- wolffe rex]

Same steps as gc_droids.build: the Squad biped is turned to face the rig's way, scaled to its leg length and posed onto the
Citizen rest; the mesh is baked in that pose and its weights renamed to Citizen bones; the rig's joints move onto the
commander's (translation only) and the vmdl's SkeletonDelta carries the difference onto every clip. Writes
models/troopers/gc_cmdr_<id>_<rev>.fbx, its materials (materials/gc_*.vmat) and gc_cmdr_<id>.vmdl, _drill.vmdl and
_squad.vmdl (the officer's three, with their animation lists; ClassDefinition.LoadModel picks between them), plus a
preview render in Squad_Mod_2428425228\\Export\\sbox.
"""
import os
import re
import sys
import uuid

import bpy
from mathutils import Vector, Matrix

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import bf2_props as bp
import bf2_clones as bc
import gc_droids as gd      # (sets bf2_clones' bone tables to the Squad biped's)

GC = gd.GC
OUT = gd.OUT
REV = bp.REV
COMMANDERS = {
	"wolffe": r"ANE_BASE\Art\Soldier2\CloneArmy\NEW\Phase_2\Commanders\Wolffe\Commander_Wolffe_P2.glb",
	"rex": r"ANE_BASE\Art\Soldier2\CloneArmy\NEW\Phase_2\Commanders\Rex\Captain_Rex_P2.glb",
}
# the officer's models, one per animation set: stock human, the clone drill set, the Squad set
VARIANTS = { "": "clone_officer.vmdl", "_drill": "clone_officer_drill.vmdl", "_squad": "clone_officer_squad.vmdl" }


def log( *a ):
	print( "GCCMDR", *a, flush=True )


def find_json( name ):
	"""The clone armour's UE material notes live under CloneArmy (gc_droids looks under Droid)."""
	for root, _, files in os.walk( os.path.join( GC, "ANE_BASE", "Art", "Soldier2", "CloneArmy" ) ):
		if name + ".json" in files:
			return os.path.join( root, name + ".json" )
	return None


gd.find_json = find_json


def build( cid, glb ):
	bpy.ops.wm.open_mainfile( filepath=bc.RIG_BLEND )
	rig = bpy.data.objects["Human Citizen Armature"]
	for o in list( bpy.data.objects ):
		if o.type == "MESH":
			bpy.data.objects.remove( o, do_unlink=True )
	out_coll = bpy.data.collections["Output"]

	squad, meshes = gd.import_glb( os.path.join( GC, glb ) )
	s = gd.fit_by_legs( squad, rig )
	worst = bc.pose_onto( squad, rig )
	mats = []
	for m in meshes:
		for i, slot in enumerate( m.data.materials ):
			vm = gd.material( slot.name.split( "." )[0] )
			mat = bpy.data.materials.get( vm + ".vmat" ) or bpy.data.materials.new( vm + ".vmat" )
			m.data.materials[i] = mat
			mats.append( vm )
	bc.bake_pose( meshes )
	bc.remap_weights( meshes, squad )
	moved = gd.joints_onto_droid( rig, squad )
	bpy.data.objects.remove( squad, do_unlink=True )
	if len( meshes ) > 1:
		bpy.ops.object.select_all( action="DESELECT" )
		for m in meshes:
			m.select_set( True )
		bpy.context.view_layer.objects.active = meshes[0]
		bpy.ops.object.join()
	body = meshes[0]
	body.name = body.data.name = "body"
	zs = [( body.matrix_world @ v.co ).z for v in body.data.vertices]
	log( cid, f"scale {s:.4f}, worst segment off by {worst * 100:.2f} cm before the joints moved, joints moved up to {moved * 100:.1f} cm, "
		f"{len( body.data.vertices )} vertices, height {( max( zs ) - min( zs ) ) * 100:.1f} cm (rig units)" )
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
	fbx = f"gc_cmdr_{cid}_{REV}"
	bpy.ops.export_scene.fbx( filepath=os.path.join( OUT, fbx + ".fbx" ), use_selection=True, object_types={ "ARMATURE", "MESH" },
		add_leaf_bones=False, primary_bone_axis="X", secondary_bone_axis="Z", bake_anim=True, bake_anim_use_all_actions=False,
		bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, mesh_smooth_type="FACE" )
	with open( os.path.join( OUT, fbx + ".fbx.meta" ), "w", newline="\n" ) as fh:
		fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	os.makedirs( os.path.join( GC, "sbox" ), exist_ok=True )
	bpy.ops.wm.save_as_mainfile( filepath=os.path.join( GC, "sbox", f"gc_cmdr_{cid}.blend" ) )
	write_vmdls( cid, fbx, sorted( set( mats ) ) )
	preview( cid, rig, body )
	log( cid, "exported", fbx, mats )


def material_group_list( mats, indent ):
	"""A MaterialGroupList with just the default group: the commander's own paint, no battalion groups."""
	t = "\t" * indent
	rows = "".join( f'{t}\t\t\t\t\t{{\n{t}\t\t\t\t\t\tfrom = "{m}.vmat"\n{t}\t\t\t\t\t\tto = "models/troopers/materials/{m}.vmat"\n{t}\t\t\t\t\t}},\n' for m in mats )
	return ( f'{{\n{t}\t_class = "MaterialGroupList"\n{t}\tchildren = \n{t}\t[\n{t}\t\t{{\n{t}\t\t\t_class = "DefaultMaterialGroup"\n'
		f'{t}\t\t\tremaps =\n{t}\t\t\t[\n{rows}{t}\t\t\t]\n{t}\t\t\tuse_global_default = false\n{t}\t\t\tglobal_default_material = ""\n'
		f'{t}\t\t}},\n{t}\t]\n{t}}}' )


def replace_block( text, marker, new ):
	"""Swap the { ... } node holding `marker` for `new` (brace matched; the vmdl's strings here hold no braces)."""
	at = text.index( marker )
	start = text.rindex( "{", 0, at )
	depth = 0
	for i in range( start, len( text ) ):
		if text[i] == "{":
			depth += 1
		elif text[i] == "}":
			depth -= 1
			if depth == 0:
				return text[:start] + new + text[i + 1:]
	raise ValueError( "unbalanced vmdl" )


def write_vmdls( cid, fbx, mats ):
	for suffix, template in VARIANTS.items():
		v = open( os.path.join( OUT, template ), encoding="utf-8" ).read()
		v = re.sub( r"models/troopers/clone_officer_\w+?\.fbx", f"models/troopers/{fbx}.fbx", v )
		line = v[:v.index( '_class = "MaterialGroupList"' )].rsplit( "\n", 1 )[1]
		v = replace_block( v, '_class = "MaterialGroupList"', material_group_list( mats, len( line ) - len( line.lstrip( "\t" ) ) - 1 ) )
		path = os.path.join( OUT, f"gc_cmdr_{cid}{suffix}.vmdl" )
		with open( path, "w", encoding="utf-8", newline="\n" ) as fh:
			fh.write( v )
		if not os.path.exists( path + ".meta" ):
			with open( path + ".meta", "w", newline="\n" ) as fh:
				fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
		log( cid, "wrote", os.path.basename( path ) )


def preview( cid, rig, body ):
	"""Front and side renders of the bound model in the rig's rest pose, with its colour textures."""
	sc = bpy.context.scene
	for m in body.data.materials:
		name = m.name[:-5]
		png = os.path.join( gd.MAT_DIR, name + "_color.png" )
		if not os.path.exists( png ):
			continue
		m.use_nodes = True
		nt = m.node_tree
		bsdf = next( ( n for n in nt.nodes if n.type == "BSDF_PRINCIPLED" ), None )
		if bsdf is None:
			continue
		tex = nt.nodes.new( "ShaderNodeTexImage" )
		tex.image = bpy.data.images.load( png )
		nt.links.new( tex.outputs["Color"], bsdf.inputs["Base Color"] )
	sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
	sc.render.resolution_x, sc.render.resolution_y = 700, 900
	world = sc.world or bpy.data.worlds.new( "w" )
	sc.world = world
	world.use_nodes = True
	bg = world.node_tree.nodes.get( "Background" )
	if bg:
		bg.inputs["Color"].default_value = ( 0.35, 0.37, 0.4, 1 )
		bg.inputs["Strength"].default_value = 1.0
	zs = [( body.matrix_world @ v.co ) for v in body.data.vertices]
	lo = Vector( ( min( p.x for p in zs ), min( p.y for p in zs ), min( p.z for p in zs ) ) )
	hi = Vector( ( max( p.x for p in zs ), max( p.y for p in zs ), max( p.z for p in zs ) ) )
	mid = ( lo + hi ) / 2
	h = hi.z - lo.z
	cam_data = bpy.data.cameras.new( "cam" )
	cam = bpy.data.objects.new( "cam", cam_data )
	sc.collection.objects.link( cam )
	sc.camera = cam
	sun = bpy.data.objects.new( "sun", bpy.data.lights.new( "sun", "SUN" ) )
	sun.data.energy = 4.0
	sun.rotation_euler = ( 0.8, 0.2, 0.6 )
	sc.collection.objects.link( sun )
	fwd = Vector( ( 0, -1, 0 ) )       # the rig faces -Y
	for tag, d in ( ( "front", fwd ), ( "side", Vector( ( 1, 0, 0 ) ) ), ( "back", -fwd ) ):
		cam.location = mid + d * h * 1.9 + Vector( ( 0, 0, h * 0.05 ) )
		cam.rotation_euler = ( mid - cam.location ).to_track_quat( "-Z", "Y" ).to_euler()
		cam_data.lens = 50
		sc.render.filepath = os.path.join( GC, "sbox", f"gc_cmdr_{cid}_{tag}.png" )
		bpy.ops.render.render( write_still=True )


def main():
	only = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	for cid, glb in COMMANDERS.items():
		if not only or cid in only:
			build( cid, glb )


if __name__ == "__main__":
	main()
