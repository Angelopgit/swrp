"""
Writes the trooper vmdls for the models bf2_clones.py built (Assets/models/troopers/manifest.json), from the converted
clone's vmdl (models/clones/clone_trooper.vmdl: the Citizen anim constraints, prefabs, hitboxes, IK, eye attachments) with
the new mesh, the scale back to true size, the backpack as a bodygroup, and one material group per battalion when
bf2_markings.py has made them (Assets/models/troopers/markings.json). Plain Python:

    python ModelSource/bf2_clone_vmdl.py

Each class gets clone_<class>.vmdl (stock Citizen animations) and clone_<class>_drill.vmdl (the clone rifle set).
"""
import os
import re
import json

HERE = os.path.dirname( os.path.abspath( __file__ ) )
ASSETS = os.path.join( os.path.dirname( HERE ), "Assets" )
OUT = os.path.join( ASSETS, "models", "troopers" )
TEMPLATE = os.path.join( ASSETS, "models", "clones", "clone_trooper.vmdl" )
SCALE = 0.3937             # the animation set is authored at this scale; true size comes from swrp_player_scale (1.06)
# bodygroups: name -> choices (each a list of meshes; the first choice is the default)
GROUPS = {
	"backpack": [[], ["backpack"]],
	# the Jedi's robes (Appearance.Robe; Characters/SwrpOutfit.cs JediRobes is the same order) and heads (Appearance.Face)
	"robe": [["k1_body", "k1_vest", "k1_flaps", "k1_skirt", "k1_sleeves", "k1_hands"],
		["k2_body", "k2_vest", "k2_flaps", "k2_skirt", "k2_robe", "k2_hands"],
		["k3_body", "k3_skirt"],
		["a1_body", "a1_flaps", "a1_skirt", "a1_sleeves"],
		["a2_body", "a2_flaps", "a2_robe"],
		["a3_body", "a3_skirt"]],
	"head": [["head_kenobi"], ["head_skywalker", "hair_skywalker"], ["head_dooku"]],
}
OPTIONAL = { m for choices in GROUPS.values() for c in choices for m in c }


def mesh_nodes( fbx, meshes ):
	node = """					{{
						_class = "RenderMeshFile"
						name = "{name}"
						filename = "{fbx}"
						import_translation = [ 0.0, 0.0, 0.0 ]
						import_rotation = [ 0.0, 0.0, 0.0 ]
						import_scale = 1.0
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						parent_bone = ""
						import_filter =
						{{
							exclude_by_default = {excl}
							exception_list =
							[
{items}
							]
						}}
					}},"""
	opt = [m for m in meshes if m in OPTIONAL]
	out = []
	if any( m not in OPTIONAL for m in meshes ):         # the Jedi have no always-on mesh: every piece is a robe or a head
		out.append( node.format( name="body", fbx=fbx, excl="false", items="".join( f'\t\t\t\t\t\t\t\t"{m}",\n' for m in opt ).rstrip( "\n" ) ) )
	for m in opt:
		out.append( node.format( name=m, fbx=fbx, excl="true", items=f'\t\t\t\t\t\t\t\t"{m}",' ) )
	return "\n".join( out )


def material_groups( mats, markings, cls ):
	def remaps( table ):
		return "\n".join( f"""							{{
								from = "{src}.vmat"
								to = "{dst}"
							}},""" for src, dst in table )
	base = [( m, f"models/troopers/materials/{m}.vmat" ) for m in mats]
	groups = [f"""					{{
						_class = "DefaultMaterialGroup"
						remaps =
						[
{remaps( base )}
						]
						use_global_default = false
						global_default_material = ""
					}},"""]
	# the other groups swap from the default group's materials (not the FBX's names)
	for battalion, table in sorted( markings.get( cls, {} ).items() ):
		swaps = [( f"models/troopers/materials/{m}", table[m] ) for m in mats if m in table]
		groups.append( f"""					{{
						_class = "MaterialGroup"
						name = "{battalion}"
						remaps =
						[
{remaps( swaps )}
						]
					}},""" )
	return "\n".join( groups )


def bodygroups( meshes ):
	out = []
	for name, choices in GROUPS.items():
		if not any( m in meshes for c in choices for m in c ):
			continue
		kids = "\n".join( "\t\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\t_class = \"BodyGroupChoice\"\n\t\t\t\t\t\t\t\tmeshes = [ %s ]\n\t\t\t\t\t\t\t},"
			% ", ".join( f'"{m}"' for m in c ) for c in choices )
		out.append( f"\t\t\t\t\t{{\n\t\t\t\t\t\t_class = \"BodyGroup\"\n\t\t\t\t\t\tname = \"{name}\"\n\t\t\t\t\t\tchildren = \n\t\t\t\t\t\t[\n{kids}\n"
			"\t\t\t\t\t\t]\n\t\t\t\t\t\thidden_in_tools = false\n\t\t\t\t\t}," )
	return "\n".join( out )


def replace_list( text, cls_name, inner ):
	"""Replace the children of the first `_class = "<cls_name>"` list node."""
	i = text.index( f'_class = "{cls_name}"' )
	j = text.index( "children = ", i )
	a = text.index( "[", j )
	depth = 0
	for k in range( a, len( text ) ):
		if text[k] == "[":
			depth += 1
		elif text[k] == "]":
			depth -= 1
			if depth == 0:
				return text[:a + 1] + "\n" + inner + "\n\t\t\t\t" + text[k:]
	raise ValueError( cls_name )


def main():
	manifest = json.load( open( os.path.join( OUT, "manifest.json" ) ) )
	mk = os.path.join( OUT, "markings.json" )
	markings = json.load( open( mk ) ) if os.path.exists( mk ) else {}
	tpl = open( TEMPLATE, encoding="utf-8" ).read()
	for cls, info in manifest.items():
		meshes = [m for m, _ in info["meshes"]]
		mats = sorted( { v for _, vs in info["meshes"] for v in ( vs if isinstance( vs, list ) else [vs] ) } )
		v = tpl
		v = replace_list( v, "RenderMeshList", mesh_nodes( info["fbx"], meshes ) )
		v = replace_list( v, "MaterialGroupList", material_groups( mats, markings, cls ) )
		v = replace_list( v, "BodyGroupList", bodygroups( meshes ) )
		v = v.replace( 'source_filename = "models/clones/clone_trooper_r4.fbx"', f'source_filename = "{info["fbx"]}"' )
		v = re.sub( r"scale = 0\.3937\b", f"scale = {SCALE:.5f}", v )
		assert info["fbx"] in v and f"{SCALE:.5f}" in v
		for suffix, anims in ( ( "", "models/citizen_human/prefabs/human_animationlist.vmdl_prefab" ), ( "_drill", "models/clones/clone_animationlist.vmdl_prefab" ) ):
			text = v.replace( 'target_file = "models/citizen_human/prefabs/human_animationlist.vmdl_prefab"', f'target_file = "{anims}"' )
			path = os.path.join( OUT, f"clone_{cls}{suffix}.vmdl" )
			with open( path, "w", newline="\n" ) as fh:
				fh.write( text )
			print( "wrote", path )
		# keep this and the previous revision of the fbx
		revs = sorted( ( f for f in os.listdir( OUT ) if re.fullmatch( rf"clone_{cls}_[0-9a-f]{{6}}\.fbx", f ) ), key=lambda f: os.path.getmtime( os.path.join( OUT, f ) ) )
		for old in revs[:-2]:
			for fn in ( old, old + ".meta" ):
				try:
					os.remove( os.path.join( OUT, fn ) )
				except OSError:
					pass


main()
