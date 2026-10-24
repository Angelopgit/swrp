"""
Writes the armour attachment vmdls (models/troopers/armor/clone_armor_<part>.vmdl) for the parts bf2_armor.py built
(Assets/models/troopers/armor.json), from the converted clone's vmdl so they share the trooper skeleton (they are drawn
bone-merged onto the trooper's body by Code/Characters/SwrpArmor.cs), with one material group per battalion from
bf2_armor_paint.py (armor_markings.json) and the trooper markings (markings.json). Plain Python:

    python ModelSource/bf2_armor_vmdl.py
"""
import os
import re
import json

HERE = os.path.dirname( os.path.abspath( __file__ ) )
ASSETS = os.path.join( os.path.dirname( HERE ), "Assets" )
TROOPERS = os.path.join( ASSETS, "models", "troopers" )
OUT = os.path.join( TROOPERS, "armor" )
TEMPLATE = os.path.join( ASSETS, "models", "clones", "clone_trooper.vmdl" )
SCALE = 0.3937      # the trooper vmdls' scale (bf2_clone_vmdl.py); never different, or bone merge misplaces the parts


def replace_list( text, cls_name, inner ):
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


def mesh_node( fbx ):
	return f"""					{{
						_class = "RenderMeshFile"
						name = "armor"
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
							exclude_by_default = false
							exception_list = [  ]
						}}
					}},"""


def material_groups( mats, battalions ):
	def remaps( table ):
		return "\n".join( f"""							{{
								from = "{src}.vmat"
								to = "{dst}"
							}},""" for src, dst in table )
	base = [( m, f"models/troopers/materials/{m}.vmat" ) for m in mats]
	out = [f"""					{{
						_class = "DefaultMaterialGroup"
						remaps =
						[
{remaps( base )}
						]
						use_global_default = false
						global_default_material = ""
					}},"""]
	for battalion, table in sorted( battalions.items() ):
		swaps = [( f"models/troopers/materials/{m}", table[m] ) for m in mats if m in table]
		if not swaps:
			continue
		out.append( f"""					{{
						_class = "MaterialGroup"
						name = "{battalion}"
						remaps =
						[
{remaps( swaps )}
						]
					}},""" )
	return "\n".join( out )


def main():
	listing = json.load( open( os.path.join( TROOPERS, "armor.json" ) ) )
	# the battalion colours: the painted attachment materials first, then the trooper markings (helmet, body)
	battalions = {}
	for path in ( os.path.join( TROOPERS, "markings.json" ), os.path.join( TROOPERS, "armor_markings.json" ) ):
		if not os.path.exists( path ):
			continue
		data = json.load( open( path ) )
		tables = data.get( "trooper", data ) if "trooper" in data else data
		for battalion, table in tables.items():
			battalions.setdefault( battalion, {} ).update( table )
	tpl = open( TEMPLATE, encoding="utf-8" ).read()
	os.makedirs( OUT, exist_ok=True )
	for part, info in listing.items():
		mats = sorted( { v for _, vs in info["meshes"] for v in vs } )
		v = tpl
		v = replace_list( v, "RenderMeshList", mesh_node( info["fbx"] ) )
		v = replace_list( v, "MaterialGroupList", material_groups( mats, battalions ) )
		v = replace_list( v, "BodyGroupList", "" )
		v = v.replace( 'source_filename = "models/clones/clone_trooper_r4.fbx"', f'source_filename = "{info["fbx"]}"' )
		v = re.sub( r"scale = 0\.3937\b", f"scale = {SCALE:.5f}", v )
		assert info["fbx"] in v
		path = os.path.join( OUT, f"clone_armor_{part}.vmdl" )
		with open( path, "w", newline="\n" ) as fh:
			fh.write( v )
		print( "wrote", path, mats )
		# keep this and the previous revision of the fbx
		revs = sorted( ( f for f in os.listdir( TROOPERS ) if re.fullmatch( rf"clone_armor_{part}_[0-9a-f]{{6}}\.fbx", f ) ), key=lambda f: os.path.getmtime( os.path.join( TROOPERS, f ) ) )
		for old in revs[:-2]:
			for fn in ( old, old + ".meta" ):
				try:
					os.remove( os.path.join( TROOPERS, fn ) )
				except OSError:
					pass


main()
