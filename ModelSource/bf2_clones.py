"""
The playable models from Battlefront II (2017), all on ONE skeleton: the Citizen human rig the clone troopers already used
(X:\\SWRP Projects\\Clonetrooper\\sbox_build\\clone_trooper.blend, "Human Citizen Armature", with its bindPose take for the
vmdl's proportion trick), so every class and battalion shares the animgraph, the clone animation set, emotes, sabers and
held-item grips. LOCAL ONLY: never publish or commit (CLAUDE.md, IP rules). Source: X:\\SWRP Projects\\BF2_Extracted.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/bf2_clones.py [-- <class> ...]

How a BF2 part gets onto the rig:
  1. its Frostbite skeleton ("Walrus_HumanMale") is scaled and posed onto the shape the converted clone was built in (the
     blend's "clone_armature": the same skeleton, already renamed to Citizen names): every limb segment, finger and the
     spine is turned to point where the clone's does, hands and feet also by their spread, so the armour lands exactly
     where the old trooper's did (checked against the old body, same mesh: the error is printed);
  2. the pose is applied to the mesh, the Frostbite weights are renamed to Citizen bones (helpers split between the two
     joints they sit between, everything else to its nearest mapped parent), weights with no bone are filled from the
     nearest weighted vertex, each vertex keeps four influences summing to one;
  3. it is bound to the Citizen armature and exported with the bindPose take.
Size: the BF2 clone is 1.84 m with helmet; the rig is the clone scaled to Citizen proportions (0.942), and the vmdl scales
it back up (SCALE below), so a trooper stands 72 units (1.83 m) tall: true to the films against the 1-inch-unit ship.
Textures go through the bf2_props converters; battalion looks are material groups (bf2_markings.py).
"""
import os
import sys
import math
import time
import uuid
import shutil

import bpy
from mathutils import Vector, Matrix, Quaternion
from mathutils.kdtree import KDTree

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import bf2_props as bp

RIG_BLEND = r"X:\SWRP Projects\Clonetrooper\sbox_build\clone_trooper.blend"
BF2 = bp.BF2
OUT = os.path.join( bp.ASSETS, "models", "troopers" )
REV = bp.REV
SCALE = 0.3937 / 0.942          # cm -> inches, and the clone back to its BF2 size (1.84 m with helmet)

# class -> [(mesh name, source fbx)]
CLASSES = {
	"trooper": [
		( "body", "Clone_Trooper/l_assault_preq_01_mesh.fbx" ),
		( "gloves", "Clone_Trooper/gloves_05_mesh.fbx" ),
		( "helmet", "Clone_Trooper/l_assault_preq_01_helmet_mesh.fbx" ),
		( "backpack", "Clone_Trooper/l_assault_preq_01_backpack_mesh.fbx" ),
	],
	"heavy": [
		( "body", "Clone_Heavy/l_assault_preq_01_mesh.fbx" ),
		( "gloves", "Clone_Heavy/gloves_05_mesh.fbx" ),
		( "helmet", "Clone_Trooper/l_assault_preq_01_helmet_mesh.fbx" ),
		( "heavy_gear", "Clone_Heavy/l_heavy_preq_01_parts_mesh.fbx" ),
		( "heavy_visor", "Clone_Heavy/l_heavy_preq_01_visor_mesh.fbx" ),
	],
	"specialist": [
		( "body", "Clone_Specialist/l_assault_preq_01_mesh.fbx" ),
		( "gloves", "Clone_Specialist/gloves_05_mesh.fbx" ),
		( "helmet", "Clone_Trooper/l_assault_preq_01_helmet_mesh.fbx" ),
		( "specialist_gear", "Clone_Specialist/l_specialist_preq_01_parts_mesh.fbx" ),
		( "specialist_visor", "Clone_Specialist/l_specialist_preq_01_visor_mesh.fbx" ),
	],
	"officer": [
		( "body", "Clone_Officer/l_assault_preq_01_mesh.fbx" ),
		( "gloves", "Clone_Officer/gloves_05_mesh.fbx" ),
		( "helmet", "Clone_Trooper/l_assault_preq_01_helmet_mesh.fbx" ),
		( "officer_gear", "Clone_Officer/l_officer_preq_01_headgear_parts_mesh.fbx" ),
	],
	# a Jedi of the Clone Wars in Kenobi's tunic, tabards and robe sleeves; the head is a bodygroup (Kenobi, or Skywalker with
	# his hair) picked from the character's Appearance
	"jedi": [
		# robes: one set of meshes per outfit (the "robe" bodygroup, Appearance.Robe); Kenobi's and Skywalker's three looks each
		( "k1_body", "Jedi_ObiWan_Kenobi/obiwan_01_body_mesh.fbx" ),
		( "k1_vest", "Jedi_ObiWan_Kenobi/obiwan_01_vest_mesh.fbx" ),
		( "k1_flaps", "Jedi_ObiWan_Kenobi/obiwan_01_flaps_cloth_mesh.fbx" ),
		( "k1_skirt", "Jedi_ObiWan_Kenobi/obiwan_01_skirt_cloth_mesh.fbx" ),
		( "k1_sleeves", "Jedi_ObiWan_Kenobi/obiwan_01_sleeves_cloth_mesh.fbx" ),
		( "k1_hands", "Jedi_ObiWan_Kenobi/obiwan_01_hand_mesh.fbx" ),
		( "k2_body", "Jedi_ObiWan_Kenobi/obiwan_02_body_mesh.fbx" ),
		( "k2_vest", "Jedi_ObiWan_Kenobi/obiwan_02_vest_mesh.fbx" ),
		( "k2_flaps", "Jedi_ObiWan_Kenobi/obiwan_02_flaps_cloth_mesh.fbx" ),
		( "k2_skirt", "Jedi_ObiWan_Kenobi/obiwan_02_skirt_cloth_mesh.fbx" ),
		( "k2_robe", "Jedi_ObiWan_Kenobi/obiwan_02_sleeves_robe_cloth_mesh.fbx" ),
		( "k2_hands", "Jedi_ObiWan_Kenobi/obiwan_02_hand_mesh.fbx" ),
		( "k3_body", "Jedi_ObiWan_Kenobi/obiwan_03_body_mesh.fbx" ),
		( "k3_skirt", "Jedi_ObiWan_Kenobi/obiwan_03_skirt_cloth_mesh.fbx" ),
		( "a1_body", "Jedi_Anakin_Skywalker/anakin_01_mesh.fbx" ),
		( "a1_flaps", "Jedi_Anakin_Skywalker/anakin_01_flaps_mesh.fbx" ),
		( "a1_skirt", "Jedi_Anakin_Skywalker/anakin_01_skirt_mesh.fbx" ),
		( "a1_sleeves", "Jedi_Anakin_Skywalker/anakin_01_sleeves_mesh.fbx" ),
		( "a2_body", "Jedi_Anakin_Skywalker/anakin_02_mesh.fbx" ),
		( "a2_flaps", "Jedi_Anakin_Skywalker/anakin_02_flaps_mesh.fbx" ),
		( "a2_robe", "Jedi_Anakin_Skywalker/anakin_02_robe_mesh.fbx" ),
		( "a3_body", "Jedi_Anakin_Skywalker/anakin_03_mesh.fbx" ),
		( "a3_skirt", "Jedi_Anakin_Skywalker/anakin_03_skirt_mesh.fbx" ),
		# heads (the "head" bodygroup, Appearance.Face)
		( "head_kenobi", "Jedi_ObiWan_Kenobi/heads_obiwan_rots_01_mesh.fbx" ),
		( "head_skywalker", "Jedi_Anakin_Skywalker/heads_anakin_01_mesh.fbx" ),
		( "hair_skywalker", "Jedi_Anakin_Skywalker/hair_anakin_01_mesh.fbx" ),
		( "head_dooku", "Sith_Count_Dooku/heads_countdooku_01_mesh.fbx" ),
	],
	# the Separatist NPC droids (Angelo, 2026-10-12): the B1 on its own skeleton (BF2 D_Assault_Preq_01_Ske; the first export
	# used the human one and scrambled its weights) and the B2 on B2_01_Ske; both keep their own Frostbite weights
	"droid_b1": [
		( "body", "Droid_B1/d_assault_preq_01_mesh.fbx" ),
		( "droid_pack", "Droid_B1/d_assault_preq_01_backpack_mesh.fbx" ),
	],
	"droid_b2": [
		( "body", "Droid_B2/b2_01_mesh.fbx" ),
	],
}
DROIDS = { "droid_b1", "droid_b2" }

# Frostbite bone -> Citizen bone (the converter's table, sbox_build/convert.py)
MAP = { "Hips": "pelvis", "Spine": "spine_0", "Spine1": "spine_1", "Spine2": "spine_2", "Neck": "neck_0", "Head": "head" }
for S, s in ( ( "Left", "L" ), ( "Right", "R" ) ):
	MAP.update( { f"{S}Shoulder": f"clavicle_{s}", f"{S}Arm": f"arm_upper_{s}", f"{S}ArmRoll": f"arm_upper_{s}_twist1",
		f"{S}ForeArm": f"arm_lower_{s}", f"{S}ForeArmRoll1": f"arm_lower_{s}_twist1", f"{S}Hand": f"hand_{s}",
		f"{S}UpLeg": f"leg_upper_{s}", f"{S}UpLegRoll": f"leg_upper_{s}_twist1", f"{S}Leg": f"leg_lower_{s}",
		f"{S}Foot": f"ankle_{s}", f"{S}ToeBase": f"ball_{s}",
		f"{S}HandThumb1": f"finger_thumb_0_{s}", f"{S}HandThumb2": f"finger_thumb_1_{s}", f"{S}HandThumb3": f"finger_thumb_2_{s}" } )
	for f, c in ( ( "Index", "index" ), ( "Middle", "middle" ), ( "Ring", "ring" ), ( "Pinky", "pinky" ) ):
		MAP.update( { f"{S}Hand{f}0": f"finger_{c}_meta_{s}", f"{S}Hand{f}1": f"finger_{c}_0_{s}",
			f"{S}Hand{f}2": f"finger_{c}_1_{s}", f"{S}Hand{f}3": f"finger_{c}_2_{s}" } )
SPLIT = { "HeadRoll": { "Neck": 0.5, "Head": 0.5 } }
for S in ( "Left", "Right" ):
	SPLIT.update( { f"{S}ArmBend": { f"{S}Arm": 0.5, f"{S}ForeArm": 0.5 }, f"{S}Armpit": { "Spine2": 0.5, f"{S}Arm": 0.5 },
		f"{S}DeltoidBulge": { f"{S}Shoulder": 0.25, f"{S}Arm": 0.75 }, f"{S}HipsRoll": { "Hips": 0.5, f"{S}UpLeg": 0.5 },
		f"{S}KneeRoll": { f"{S}UpLeg": 0.5, f"{S}Leg": 0.5 }, f"{S}Ankle": { f"{S}Leg": 0.5, f"{S}Foot": 0.5 },
		f"{S}ForeArmRoll2": { f"{S}ForeArmRoll1": 1.0 } } )

# segments to line up (Frostbite from -> to, matched by the Citizen names they map to); a third entry is a spread pair
SEGMENTS = [( "Spine", "Spine1" ), ( "Spine1", "Spine2" ), ( "Spine2", "Neck" ), ( "Neck", "Head" )]
for S in ( "Left", "Right" ):
	SEGMENTS += [( f"{S}Shoulder", f"{S}Arm" ), ( f"{S}Arm", f"{S}ForeArm" ), ( f"{S}ForeArm", f"{S}Hand" ),
		( f"{S}Hand", f"{S}HandMiddle1", ( f"{S}HandPinky1", f"{S}HandIndex1" ) ),
		( f"{S}HandThumb1", f"{S}HandThumb2" ), ( f"{S}HandThumb2", f"{S}HandThumb3" ),
		( f"{S}UpLeg", f"{S}Leg" ), ( f"{S}Leg", f"{S}Foot" ), ( f"{S}Foot", f"{S}ToeBase" )]
	for f in ( "Index", "Middle", "Ring", "Pinky" ):
		SEGMENTS += [( f"{S}Hand{f}0", f"{S}Hand{f}1" ), ( f"{S}Hand{f}1", f"{S}Hand{f}2" ), ( f"{S}Hand{f}2", f"{S}Hand{f}3" )]


def log( *a ):
	print( "BF2CLONE", *a, flush=True )


def import_part( src ):
	"""lod0 meshes (still skinned) and the armature of one BF2 part."""
	before = set( bpy.data.objects )
	bpy.ops.import_scene.fbx( filepath=os.path.join( BF2, src ), ignore_leaf_bones=True )
	new = [o for o in bpy.data.objects if o not in before]
	arm = next( o for o in new if o.type == "ARMATURE" )
	meshes = [o for o in new if o.type == "MESH" and "lod0" in o.name.lower()]
	drop = [o for o in new if o.type == "EMPTY" or o.type == "MESH" and o not in meshes]
	for o in drop:
		bpy.data.objects.remove( o, do_unlink=True )
	return arm, meshes


def world_head( arm, name, pose=True ):
	if pose:
		return arm.matrix_world @ arm.pose.bones[name].head
	return arm.matrix_world @ arm.data.bones[name].head_local


def fit( walrus, target, by_legs=False ):
	"""Scale and move the Frostbite rig (object level) onto the clone's: pelvis to pelvis, the same pelvis-to-head length."""
	top = next( n for n in ( "Head", "Neck1", "Neck", "Spine2" ) if n in walrus.data.bones )
	w = ( world_head( walrus, top, False ) - world_head( walrus, "Hips", False ) ).length
	t = ( world_head( target, "head", False ) - world_head( target, "pelvis", False ) ).length
	if by_legs and "LeftFoot" in walrus.data.bones:
		# droids: same leg length as the rig (their necks and torsos aren't human, so their own height carries over)
		w = ( world_head( walrus, "LeftFoot", False ) - world_head( walrus, "LeftUpLeg", False ) ).length
		t = ( world_head( target, "ankle_L", False ) - world_head( target, "leg_upper_L", False ) ).length
	s = t / w
	walrus.matrix_world = Matrix.Scale( s, 4 ) @ walrus.matrix_world
	bpy.context.view_layer.update()
	off = world_head( target, "pelvis", False ) - world_head( walrus, "Hips", False )
	walrus.matrix_world = Matrix.Translation( off ) @ walrus.matrix_world
	bpy.context.view_layer.update()
	return s


def rotate_bone( arm, name, q ):
	"""Turn a pose bone by world rotation q about its head (children follow)."""
	pb = arm.pose.bones[name]
	mw = arm.matrix_world
	world = mw @ pb.matrix
	head = world.to_translation()
	new = Matrix.Translation( head ) @ q.to_matrix().to_4x4() @ Matrix.Translation( -head ) @ world
	pb.matrix = mw.inverted() @ new
	bpy.context.view_layer.update()


def pose_onto( walrus, target ):
	err = []
	for seg in SEGMENTS:
		a, b = seg[0], seg[1]
		if a not in walrus.pose.bones or b not in walrus.pose.bones:
			continue
		ca, cb = MAP.get( a ), MAP.get( b )
		if not ca or not cb or ca not in target.data.bones or cb not in target.data.bones:
			continue
		want = world_head( target, cb, False ) - world_head( target, ca, False )
		have = world_head( walrus, b ) - world_head( walrus, a )
		if want.length < 1e-5 or have.length < 1e-5:
			continue
		rotate_bone( walrus, a, have.normalized().rotation_difference( want.normalized() ) )
		if len( seg ) > 2:
			# spin about the segment so the spread (pinky -> index, etc.) matches too
			p, i = seg[2]
			if p in walrus.pose.bones and i in walrus.pose.bones and MAP.get( p ) in target.data.bones and MAP.get( i ) in target.data.bones:
				axis = want.normalized()
				sw = world_head( target, MAP[i], False ) - world_head( target, MAP[p], False )
				sh = world_head( walrus, i ) - world_head( walrus, p )
				sw = ( sw - axis * sw.dot( axis ) ).normalized()
				sh = ( sh - axis * sh.dot( axis ) ).normalized()
				if sw.length > 0.5 and sh.length > 0.5:
					ang = math.atan2( sh.cross( sw ).dot( axis ), sh.dot( sw ) )
					rotate_bone( walrus, a, Quaternion( axis, ang ) )
		err.append( ( world_head( walrus, b ) - world_head( target, cb, False ) ).length )
	return max( err ) if err else 0.0


def bake_pose( meshes ):
	for m in meshes:
		bpy.context.view_layer.objects.active = m
		for md in list( m.modifiers ):
			if md.type == "ARMATURE":
				bpy.ops.object.modifier_apply( modifier=md.name )
		mw = m.matrix_world.copy()
		m.parent = None
		m.matrix_world = mw
		bpy.ops.object.select_all( action="DESELECT" )
		m.select_set( True )
		bpy.ops.object.transform_apply( location=True, rotation=True, scale=True )


def remap_weights( meshes, walrus ):
	"""Frostbite vertex groups -> Citizen names (helpers split, the rest to the nearest mapped parent)."""
	bones = walrus.data.bones
	def target( name ):
		if name in MAP:
			return { name: 1.0 }
		if name in SPLIT:
			return SPLIT[name]
		b = bones[name].parent if name in bones else None
		while b and b.name not in MAP:
			b = b.parent
		return { b.name: 1.0 } if b else {}
	for m in meshes:
		acc = {}
		for vg in m.vertex_groups:
			tg = target( vg.name )
			for v in m.data.vertices:
				for g in v.groups:
					if g.group == vg.index and g.weight > 0:
						for tn, f in tg.items():
							cn = MAP.get( tn, tn )
							acc.setdefault( v.index, {} )
							acc[v.index][cn] = acc[v.index].get( cn, 0.0 ) + g.weight * f
		for vg in list( m.vertex_groups ):
			m.vertex_groups.remove( vg )
		# fill unweighted vertices from the nearest weighted one; keep four, sum to one
		weighted = [i for i in acc if acc[i]]
		if not weighted:
			log( "WARNING no weights on", m.name, "- bound to the nearest bone" )
		kd = KDTree( len( weighted ) )
		for k, i in enumerate( weighted ):
			kd.insert( m.data.vertices[i].co, k )
		kd.balance()
		filled = 0
		groups = {}
		for v in m.data.vertices:
			w = acc.get( v.index )
			if not w:
				if not weighted:
					continue
				_, k, _ = kd.find( v.co )
				w = acc[weighted[k]]
				filled += 1
			keep = sorted( ( x for x in w.items() if x[1] >= 0.01 ), key=lambda x: -x[1] )[:4] or [max( w.items(), key=lambda x: x[1] )]
			tot = sum( x[1] for x in keep )
			for n, x in keep:
				if n not in groups:
					groups[n] = m.vertex_groups.new( name=n )
				groups[n].add( [v.index], x / tot, "REPLACE" )
		log( m.name, "weights:", len( groups ), "bones,", filled, "vertices filled" )


# submeshes whose texture can't be matched by name: (colour file, normal file, kind); kind "hair" is alpha-tested cards,
# "flat:<r> <g> <b>" a plain colour (teeth); "eye" a glossy eyeball from the shared BF2 eye texture (BF2_Extracted/Models/
# Eyes_Shared, exported 2026-10-12: the heads' own folders have none, so the eyes were plain white balls); "hidden" invisible
OVERRIDES = {
	"Jedi_ObiWan_Kenobi:M_Head": ( "T_Heads_ObiWan_01_C.png", "T_Heads_ObiWan_01_NS.png", "skin" ),
	"Jedi_ObiWan_Kenobi:M_HairCap": ( "T_ObiWan_01_HairCap_CS.png", "T_ObiWan_01_HairCap_NS.png", "skin" ),
	"Jedi_ObiWan_Kenobi:M_HairCards": ( "T_ObiWan_01_HairCards_CA.png", "T_ObiWan_01_HairCards_NS.png", "hair" ),
	"Jedi_ObiWan_Kenobi:M_BeardCap": ( "T_ObiWan_01_BeardCap_CA.png", "T_ObiWan_01_BeardCap_NS.png", "hair" ),
	"Jedi_ObiWan_Kenobi:M_BeardCards": ( "T_ObiWan_01_BeardCards_CA.png", "T_ObiWan_01_BeardCards_NS.png", "hair" ),
	"Jedi_ObiWan_Kenobi:M_hands": ( "T_ObiWan_Hands_01_CS.png", "T_ObiWan_Hands_01_N.png", "cs" ),
	"Jedi_Anakin_Skywalker:mat_head": ( "T_Heads_Anakin_01_C.png", "T_Heads_Anakin_01_NS.png", "skin" ),
	"Jedi_Anakin_Skywalker:hair_cap": ( "T_Heads_Anakin_01_HairCap_CA.png", "T_Heads_Anakin_01_HairCap_NS.png", "hair" ),
	"Jedi_Anakin_Skywalker:HairPlanes_M": ( "T_Heads_Anakin_01_HairPlanes_CA.png", "T_Heads_Anakin_01_HairPlanes_NS.png", "hair" ),
	"Sith_Count_Dooku:mat_head": ( "T_Heads_CountDooku_01_C.png", "T_Heads_CountDooku_01_NS.png", "skin" ),
	"Sith_Count_Dooku:mat_haircap": ( "T_CountDooku_01_HairCap_CS.png", "T_CountDooku_01_HairCap_NS.png", "skin" ),
	"Sith_Count_Dooku:mat_haircards": ( "T_CountDooku_01_HairCards_CA.png", "T_CountDooku_01_HairCards_NS.png", "hair" ),
	"Sith_Count_Dooku:mat_beardcap": ( "T_CountDooku_01_BeardCap_CA.png", "T_CountDooku_01_BeardCap_NS.png", "hair" ),
	"Sith_Count_Dooku:mat_beardcards": ( "T_CountDooku_01_BeardCards_CA.png", "T_CountDooku_01_BeardCards_NS.png", "hair" ),
	"Sith_Count_Dooku:mat_eyelashes": ( "T_CountDooku_01_HairCards_CA.png", None, "hair" ),
	"*:mat_eyes": ( "@eyes/T_Eyes_Cine_Blue_C.png", None, "eye" ),
	"*:mat_teeth": ( None, None, "flat:0.8 0.76 0.68" ),
	"*:lambert1": ( None, None, "hidden" ),      # the eye occlusion shell: drawn flat white it covered the irises
}


# Parts whose submeshes share generic names (M_body) across outfits: "<part file stem>:<submesh>" -> texture stem (relative to the
# part's Textures folder) for the by-name path below. Anakin has no hand texture of his own: Kenobi's (the same BF2 hand UVs).
HINTS = {
	"anakin_01_mesh:M_body": "T_Anakin_01_Body", "anakin_01_mesh:M_Glove": "T_Anakin_01_Glove",
	"anakin_01_mesh:M_Hand": "../../Jedi_ObiWan_Kenobi/Textures/T_ObiWan_Hands_01",
	"anakin_01_flaps_mesh:M_body": "T_Anakin_01_Body", "anakin_01_skirt_mesh:M_body": "T_Anakin_01_Body",
	"anakin_01_sleeves_mesh:M_body": "T_Anakin_01_Body",
	"anakin_02_mesh:M_body": "T_Anakin_01_Body", "anakin_02_mesh:M_Glove": "T_Anakin_01_Glove",
	"anakin_02_mesh:M_Hand": "../../Jedi_ObiWan_Kenobi/Textures/T_ObiWan_Hands_01",
	"anakin_02_flaps_mesh:M_body": "T_Anakin_01_Body", "anakin_02_robe_mesh:Mat_Cape": "T_Anakin_02_Robe",
	"anakin_03_mesh:M_Hand": "../../Jedi_ObiWan_Kenobi/Textures/T_ObiWan_Hands_01",
	"obiwan_02_body_mesh:M_body": "T_ObiWan_02", "obiwan_02_vest_mesh:M_body": "T_ObiWan_02",
	"obiwan_02_flaps_cloth_mesh:M_body": "T_ObiWan_02", "obiwan_02_skirt_cloth_mesh:M_body": "T_ObiWan_02",
	"obiwan_02_sleeves_robe_cloth_mesh:M_body": "T_ObiWan_02", "obiwan_02_sleeves_robe_cloth_mesh:M_robe": "T_ObiWan_02_Robe",
}


def submesh_material( folder, bf2_name, texdir, part="" ):
	"""The vmat (models/troopers/materials/<name>.vmat) for one BF2 submesh, written on first use; returns its name."""
	mat_dir = os.path.join( OUT, "materials" )
	os.makedirs( mat_dir, exist_ok=True )
	rel = "models/troopers/materials"
	ov = OVERRIDES.get( f"{folder}:{bf2_name}" ) or OVERRIDES.get( f"*:{bf2_name}" )
	if ov:
		color, normal, kind = ov
		name = bf2_name.lower() if kind in ( "eye", "hidden" ) or not color else color[:-4].lower()
		if color and color.startswith( "@eyes/" ):
			texdir, color = os.path.join( bp.BF2, "Eyes_Shared" ), color[6:]
		vmat = os.path.join( mat_dir, name + ".vmat" )
		if os.path.exists( vmat ):
			return name
		body = []
		flags = []
		if kind == "hidden":
			flags.append( "F_TRANSLUCENT 1" )
			body += ['TextureColor "materials/default/default_color.tga"', 'TextureNormal "materials/default/default_normal.tga"',
				'TextureRoughness "materials/default/default_rough.tga"', 'g_flOpacityScale "0.000"', 'TextureTranslucency "[0.000000 0.000000 0.000000 0.000000]"']
		elif kind == "eye":
			a, w, h = bp.read( os.path.join( texdir, color ) )
			bp.write( os.path.join( mat_dir, f"{name}_color.png" ), a[:, :, :3], w, h )
			body += [f'TextureColor "{rel}/{name}_color.png"', 'TextureNormal "materials/default/default_normal.tga"',
				'TextureRoughness "materials/default/default_rough.tga"', 'g_flRoughnessScaleFactor "0.120"']
		elif kind.startswith( "flat:" ):
			body += ['TextureColor "materials/default/default_color.tga"', 'TextureNormal "materials/default/default_normal.tga"',
				'TextureRoughness "materials/default/default_rough.tga"', 'g_flRoughnessScaleFactor "0.300"',
				f'g_vColorTint "[{kind[5:]} 0.000000]"']
		else:
			a, w, h = bp.read( os.path.join( texdir, color ) )
			bp.write( os.path.join( mat_dir, f"{name}_color.png" ), a[:, :, :3], w, h )
			body.append( f'TextureColor "{rel}/{name}_color.png"' )
			if kind == "cs":
				bp.write( os.path.join( mat_dir, f"{name}_rough.png" ), 1.0 - a[:, :, 3], w, h )
				body.append( f'TextureRoughness "{rel}/{name}_rough.png"' )
			else:
				body += ['TextureRoughness "materials/default/default_rough.tga"', 'g_flRoughnessScaleFactor "%s"' % ( "0.75" if kind == "hair" else "0.55" )]
			if kind == "hair":
				bp.write( os.path.join( mat_dir, f"{name}_alpha.png" ), a[:, :, 3], w, h )
				flags.append( "F_ALPHA_TEST 1" )
				body += [f'TextureTranslucency "{rel}/{name}_alpha.png"', 'g_flAlphaTestReference "0.350"']
			if normal and os.path.exists( os.path.join( texdir, normal ) ):
				import numpy as np
				n, w2, h2 = bp.read( os.path.join( texdir, normal ) )
				x = n[:, :, 0] * 2 - 1
				y = n[:, :, 1] * 2 - 1
				z = np.sqrt( np.clip( 1 - x * x - y * y, 0, 1 ) )
				bp.write( os.path.join( mat_dir, f"{name}_normal.png" ), np.dstack( ( n[:, :, 0], n[:, :, 1], z * 0.5 + 0.5 ) ), w2, h2 )
				body.append( f'TextureNormal "{rel}/{name}_normal.png"' )
			else:
				body.append( 'TextureNormal "materials/default/default_normal.tga"' )
		if not any( b.startswith( "g_vColorTint" ) for b in body ):
			body.append( 'g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"' )
		body += ['TextureAmbientOcclusion "materials/default/default_ao.tga"', 'g_flMetalness "0.000"', 'g_flModelTintAmount "1.000"', 'g_bFogEnabled "1"']
		text = "// Generated by ModelSource/bf2_clones.py from Battlefront II textures (local only, never publish)\n\nLayer0\n{\n"
		text += '\tshader "shaders/complex.shader"\n' + "".join( f"\t{f}\n" for f in flags ) + "\n" + "".join( f"\t{b}\n" for b in body ) + "}\n"
		with open( vmat, "w", newline="\n" ) as fh:
			fh.write( text )
		return name
	# by name: the _CS file sharing the most of the submesh's name
	files = os.listdir( texdir )
	key = bf2_name.lower().replace( "m_", "" ).replace( "_", "" )
	cs = [f for f in files if f.lower().endswith( "_cs.png" ) and "black" not in f.lower()]
	best = max( cs, key=lambda t: sum( 1 for k in range( len( key ) - 2 ) if key[k:k + 3] in t.lower().replace( "_", "" ) ), default=None )
	stem = best[:-7] if best else None
	if f"{part}:{bf2_name}" in HINTS:
		stem = HINTS[f"{part}:{bf2_name}"]
		if "/" in stem:
			texdir, stem = os.path.normpath( os.path.join( texdir, os.path.dirname( stem ) ) ), os.path.basename( stem )
	name = ( os.path.basename( stem ) if stem else "flat" ).lower()
	vmat = os.path.join( mat_dir, name + ".vmat" )
	if not os.path.exists( vmat ):
		c, n = bp.texture_set( texdir, stem ) if stem else ( None, None )
		lines, metal = bp.convert_textures( c, n, mat_dir, name )
		ao = os.path.join( texdir, ( stem or "" ) + "_AOSL.png" )
		if os.path.exists( ao ):
			a, w, h = bp.read( ao )
			bp.write( os.path.join( mat_dir, name + "_ao.png" ), a[:, :, 0], w, h )
			lines = [l for l in lines if "TextureAmbientOcclusion" not in l] + [f'	TextureAmbientOcclusion "{rel}/{name}_ao.png"']
		lines = [l.replace( '"0.400"', '"0.000"' ) for l in lines]
		# the droids' colour textures are grey: BF2 paints them with a tint (B1 tan, B2 blue-grey)
		for prefix, tint in ( ( "t_d_assault_preq_01", "0.82 0.70 0.48" ), ( "t_b2_01", "0.52 0.57 0.68" ) ):
			if name.startswith( prefix ):
				lines = [l for l in lines if "g_vColorTint" not in l] + [f'	g_vColorTint "[{tint} 0.000000]"']
		bp.write_vmat( vmat, lines, metal )
	return name


OLD_TROOPERS = os.path.join( bp.ASSETS, "models", "clones" )


def transfer_weights( meshes, cls ):
	"""
	Copy the skin weights from the old converted troopers (models/clones/clone_<class>_r4.fbx: the same BF2 armour on the
	same rig, weights hand-fixed by sbox_build/anim/fix_weights.py and proven with the clone animation set) onto the new
	meshes, nearest surface interpolated. The weights remapped from Frostbite's own skeleton left the hands open and the
	wrists bent on the rifle; the old ones close the hands on the grip. Four influences, summing to one.
	"""
	src_file = os.path.join( OLD_TROOPERS, f"clone_{cls}_r4.fbx" )
	if not os.path.exists( src_file ):
		src_file = os.path.join( OLD_TROOPERS, "clone_trooper_r4.fbx" )
	before = set( bpy.data.objects )
	bpy.ops.import_scene.fbx( filepath=src_file )
	new = [o for o in bpy.data.objects if o not in before]
	srcs = []
	for o in new:
		if o.type == "MESH":
			mw = o.matrix_world.copy()
			o.parent = None
			o.matrix_world = mw
			for md in list( o.modifiers ):
				o.modifiers.remove( md )
			srcs.append( o )
	for o in new:
		if o.type != "MESH":
			bpy.data.objects.remove( o, do_unlink=True )
	bpy.ops.object.select_all( action="DESELECT" )
	for o in srcs:
		o.select_set( True )
	bpy.context.view_layer.objects.active = srcs[0]
	bpy.ops.object.transform_apply( location=True, rotation=True, scale=True )
	if len( srcs ) > 1:
		bpy.ops.object.join()
	src = bpy.context.view_layer.objects.active
	groups = [g.name for g in src.vertex_groups]
	for m in meshes:
		# the gloves keep their own Frostbite finger weights (remapped to the Citizen fingers): copied from the old trooper by
		# nearest surface, each finger picked up its neighbours' bones and the glove tore into shards once the fingers curled
		# round a grip (Angelo, 2026-10-12)
		if m.name in ( "gloves", "hands" ) or m.name.endswith( "_hands" ):
			continue
		# a cape hangs from the shoulders: weighted by height to the upper spine (top) and the pelvis (hem), never to the
		# thighs it hangs behind, or it would tear apart with every step (officer cape, 2026-10-14)
		if m.name == "cape":
			for vg in list( m.vertex_groups ):
				m.vertex_groups.remove( vg )
			zs = [( m.matrix_world @ v.co ).z for v in m.data.vertices]
			lo, hi = min( zs ), max( zs )
			top = m.vertex_groups.new( name="spine_2" )
			mid = m.vertex_groups.new( name="spine_0" )
			low = m.vertex_groups.new( name="pelvis" )
			for v, z in zip( m.data.vertices, zs ):
				h = ( z - lo ) / max( hi - lo, 1e-6 )
				a = min( max( ( h - 0.55 ) / 0.45, 0.0 ), 1.0 )         # shoulders
				c = min( max( ( 0.55 - h ) / 0.55, 0.0 ), 1.0 ) * 0.6    # hem leans on the pelvis
				b = max( 0.0, 1.0 - a - c )
				for g, w in ( ( top, a ), ( mid, b ), ( low, c ) ):
					if w > 0.001:
						g.add( [v.index], w, "REPLACE" )
			log( "cape weights: spine_2 / spine_0 / pelvis by height" )
			continue
		for vg in list( m.vertex_groups ):
			m.vertex_groups.remove( vg )
		for g in groups:
			m.vertex_groups.new( name=g )
		md = m.modifiers.new( "weights", "DATA_TRANSFER" )
		md.object = src
		md.use_vert_data = True
		md.data_types_verts = { "VGROUP_WEIGHTS" }
		md.vert_mapping = "POLYINTERP_NEAREST"
		md.layers_vgroup_select_src = "ALL"
		md.layers_vgroup_select_dst = "NAME"
		bpy.context.view_layer.objects.active = m
		bpy.ops.object.select_all( action="DESELECT" )
		m.select_set( True )
		bpy.ops.object.modifier_apply( modifier=md.name )
		# four strongest, normalised; drop empty groups
		names = { g.index: g.name for g in m.vertex_groups }
		used = set()
		for v in m.data.vertices:
			ws = sorted( ( ( g.weight, g.group ) for g in v.groups if g.weight > 0.01 ), reverse=True )[:4]
			tot = sum( w for w, _ in ws ) or 1.0
			keep = { gi for _, gi in ws }
			for g in list( v.groups ):
				if g.group not in keep:
					m.vertex_groups[g.group].remove( [v.index] )
			for w, gi in ws:
				m.vertex_groups[gi].add( [v.index], w / tot, "REPLACE" )
				used.add( gi )
		for gi in sorted( set( names ) - used, reverse=True ):
			m.vertex_groups.remove( m.vertex_groups[gi] )
	log( cls, "weights copied from", os.path.basename( src_file ) )
	bpy.data.objects.remove( src, do_unlink=True )


def build( cls, parts ):
	bpy.ops.wm.open_mainfile( filepath=RIG_BLEND )
	rig = bpy.data.objects["Human Citizen Armature"]
	target = rig      # pose onto the Citizen rig itself (its rest is what the old troopers were skinned in: hands and fingers included)
	old_body = bpy.data.objects.get( "body" )
	ref = [old_body.matrix_world @ v.co for v in old_body.data.vertices] if old_body else None
	for o in list( bpy.data.objects ):
		if o.type == "MESH":
			bpy.data.objects.remove( o, do_unlink=True )
	out_coll = bpy.data.collections["Output"]

	meshes = []
	for mesh_name, src in parts:
		walrus, ms = import_part( src )
		s = fit( walrus, target, cls in DROIDS )
		worst = pose_onto( walrus, target )
		texdir = os.path.join( BF2, os.path.dirname( src ), "Textures" )
		for m in ms:
			vm = submesh_material( os.path.dirname( src ), m.name.split( ":" )[0], texdir, os.path.basename( src )[:-4] )
			mat = bpy.data.materials.get( vm + ".vmat" ) or bpy.data.materials.new( vm + ".vmat" )
			m.data.materials.clear()
			m.data.materials.append( mat )
		bake_pose( ms )
		remap_weights( ms, walrus )
		bpy.data.objects.remove( walrus, do_unlink=True )
		if len( ms ) > 1:
			bpy.ops.object.select_all( action="DESELECT" )
			for m in ms:
				m.select_set( True )
			bpy.context.view_layer.objects.active = ms[0]
			bpy.ops.object.join()
			ms = [ms[0]]
		m = ms[0]
		m.name = mesh_name
		m.data.name = mesh_name
		meshes.append( m )
		log( cls, mesh_name, f"scale {s:.4f}, worst joint off by {worst * 100:.2f} cm, {len( m.data.vertices )} vertices" )
		if mesh_name == "body" and ref:
			kd = KDTree( len( ref ) )
			for i, p in enumerate( ref ):
				kd.insert( p, i )
			kd.balance()
			d = [kd.find( m.matrix_world @ v.co )[2] for v in m.data.vertices]
			log( cls, f"body against the old trooper: mean {sum( d ) / len( d ) * 100:.2f} cm, max {max( d ) * 100:.2f} cm" )

	if cls not in DROIDS:
		transfer_weights( meshes, cls )
	for m in meshes:
		for c in list( m.users_collection ):
			c.objects.unlink( m )
		out_coll.objects.link( m )
		m.parent = rig
		md = m.modifiers.new( "Armature", "ARMATURE" )
		md.object = rig

	# the bindPose take (the vmdl's proportion_trick subtracts it; without it the animations come out twisted): the only
	# action, every bone keyed at rest (as sbox_build/fix_bindpose.py did for the old troopers)
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
	bpy.context.scene.frame_start = 0
	bpy.context.scene.frame_end = 1
	bpy.ops.object.select_all( action="DESELECT" )
	rig.select_set( True )
	for m in meshes:
		m.select_set( True )
	bpy.context.view_layer.objects.active = rig
	os.makedirs( OUT, exist_ok=True )
	fbx = f"clone_{cls}_{REV}"
	bpy.ops.export_scene.fbx( filepath=os.path.join( OUT, fbx + ".fbx" ), use_selection=True, object_types={ "ARMATURE", "MESH" },
		add_leaf_bones=False, primary_bone_axis="X", secondary_bone_axis="Z", bake_anim=True, bake_anim_use_all_actions=False,
		bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, mesh_smooth_type="FACE" )
	meta = os.path.join( OUT, fbx + ".fbx.meta" )
	if not os.path.exists( meta ):
		with open( meta, "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	mats = [( m.name, [ms.name[:-5] for ms in m.data.materials] ) for m in meshes]
	log( cls, "exported", fbx, mats )
	return fbx, mats


def main():
	only = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	import json
	manifest_path = os.path.join( OUT, "manifest.json" )
	manifest = json.load( open( manifest_path ) ) if os.path.exists( manifest_path ) else {}
	for cls, parts in CLASSES.items():
		if only and cls not in only:
			continue
		fbx, mats = build( cls, parts )
		manifest[cls] = { "fbx": f"models/troopers/{fbx}.fbx", "meshes": mats }
	with open( manifest_path, "w", newline="\n" ) as fh:
		json.dump( manifest, fh, indent=1 )


if __name__ == "__main__":
	main()
