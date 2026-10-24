"""
Battalion-era Battlefront II (2017) props for SWRP, from the local extraction in X:\\SWRP Projects\\BF2_Extracted (Angelo's own
game, private fan research): the clone and droid blasters, the Jedi and Sith hilts, the thermal detonator, the bacta
canister and the stun baton. LOCAL ONLY: never publish or commit these models or textures (CLAUDE.md, IP rules).

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/bf2_props.py

Each prop is re-based into the frame the game holds it by (Blender units = map units, inches):
  gun:  the pistol grip at the origin, barrel along +X, up +Z (WeaponStats places nothing more); the muzzle point is printed
        and written as a "muzzle" attachment.
  hilt: along +X from the pommel's end (x = 0) to the emitter (x = the hilt length the saber code expects), +Z the front.
  item: centred on the origin, the long side along +X, resting on z = 0.
Textures: Frostbite _CS (colour + smoothness in alpha) -> colour + roughness, _N* (normal XY, then two masks) -> normal with
its Z rebuilt, metalness and AO from the masks. Writes Assets/models/<dir>/<name>_<rev>.fbx (+ .meta; keeps the previous
revision), <name>.vmdl and its vmats and PNGs beside it.
"""
import os
import sys
import time
import uuid
import math

import bpy
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname( os.path.abspath( __file__ ) )
PROJECT = os.path.dirname( HERE )
ASSETS = os.path.join( PROJECT, "Assets" )
BF2 = r"X:\SWRP Projects\BF2_Extracted\Models"
REV = format( int( time.time() ), "x" )[-6:]
M = 39.3701

# (asset path without .vmdl, source fbx, frame, extra)
PROPS = [
	( "models/weapons/bf2_dc15a", "Weapon_DC15A/dc15a_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_dc15le", "Weapon_DC15/dc15_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_dc17", "Weapon_DC17/dc17_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_dc17_ext", "Weapon_DC17_Extended/dc17_extended_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_z6", "Weapon_Z6/z6rotary_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_e5", "Weapon_E5/e5_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_dc17m", "Weapon_DC17M/dc17m_rifle_mesh_3p_mesh.fbx", "gun", None ),
	# the TFA SWRP weapon port (2026-10-12): BF2's own versions of the guns the TFA pack adds
	( "models/weapons/bf2_e5c", "Weapon_E5C/e5c_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_e5s", "Weapon_E5S/e5s_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_valken38x", "Weapon_Valken38X/valken38x_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_rg4d", "Weapon_RG4D/rg4d_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_se14c", "Weapon_SE14C/se14c_mesh3p_mesh.fbx", "gun", None ),
	# TFA Imperial/Galactic packs, second port (2026-10-19)
	( "models/weapons/bf2_e11", "Weapon_E11/e11_3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_e11d", "Weapon_E11D/e_11d_3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_dlt19", "Weapon_DLT19/dlt19_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_dlt19x", "Weapon_DLT19X/dlt19x_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_t21", "Weapon_T21/t21_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_t21b", "Weapon_T21B/t21b_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_tl50", "Weapon_TL50/tl50_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_rk3", "Weapon_RK3/rk3_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_ec17", "Weapon_ScoutPistol/scouttrooperpistol_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_dl44", "Weapon_DL44/dl44_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_ee3", "Weapon_EE3/ee3_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_cr2", "Weapon_CR2/cr2_3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_s5", "Weapon_S5/s5_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_relbyk23", "Weapon_RelbyK23/relbyk23_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_relbyv10", "Weapon_RelbyV10/relbyv10_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_iqa11", "Weapon_IQA11/iqa11_3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_nt242", "Weapon_NT242/nt242_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_x8", "Weapon_X8/x8nightsniper_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_cycler", "Weapon_Cycler/tuskencyclerrifle_unmodded_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_bowcaster", "Weapon_Bowcaster/bowcaster_mesh3p_mesh.fbx", "gun", None ),
	( "models/weapons/bf2_defender", "Weapon_Defender/defenderleia_mesh3p_mesh.fbx", "gun", None ),
	( "models/jedi/hilt_anakin", "Saber_Anakin/lightsaberanakin_meshp_mesh.fbx", "hilt", 11.0 ),
	( "models/jedi/hilt_obiwan", "Saber_ObiWan/lightsaberobiwan_meshp_mesh.fbx", "hilt", 11.0 ),
	( "models/jedi/hilt_yoda", "Saber_Yoda/lightsaberyoda_meshp_mesh.fbx", "hilt", 11.0 ),
	( "models/jedi/hilt_shoto", "Saber_Yoda/lightsaberyoda_meshp_mesh.fbx", "hilt", 8.5 ),
	( "models/jedi/hilt_dooku", "Saber_Dooku/lightsaberdooku_gameplay_meshp_mesh.fbx", "hilt", 11.0 ),
	( "models/jedi/hilt_maul", "Saber_Maul/lightsabermaul_meshp_mesh.fbx", "hilt", 21.0 ),
	# more hero hilts for the saber designer's emitter styles (Angelo, 2026-10-13)
	( "models/jedi/hilt_grievous1", "Saber_Grievous1/lightsabergrievous_meshp_mesh.fbx", "hilt", ( 11.0, "mouth" ) ),
	( "models/jedi/hilt_grievous2", "Saber_Grievous2/lightsabergrievous_02_mesh.fbx", "hilt", ( 11.0, "mouth" ) ),
	( "models/jedi/hilt_grievous3", "Saber_Grievous3/lightsabergreviousru_rots_mesh.fbx", "hilt", ( 11.0, "mouth" ) ),
	( "models/jedi/hilt_luke", "Saber_Luke/lightsaberlukeskywalker_meshp_mesh.fbx", "hilt", ( 11.0, "mouth" ) ),
	( "models/jedi/hilt_vader", "Saber_Vader/lightsaberdarthvader_meshp_mesh.fbx", "hilt", ( 11.5, "mouth" ) ),
	( "models/jedi/hilt_rey", "Saber_ReyYellow/meshp_lightsaberreyyellow_mesh.fbx", "hilt", ( 10.5, "mouth" ) ),
	( "models/props/thermal_detonator", "Item_ThermalDetonator/thermal_detonator_projectile_mesh.fbx", "item", None ),
	( "models/props/bacta_canister", "Item_Bacta/bacta_projectile_mesh.fbx", "item", None ),
	( "models/props/stun_baton", "Item_StunBaton/stunbaton_mesh.fbx", "gun", None ),
]


# ---------------------------------------------------------------------------------------------------------------------
# Textures
# ---------------------------------------------------------------------------------------------------------------------

def read( path ):
	im = bpy.data.images.load( path, check_existing=False )
	im.colorspace_settings.name = "Non-Color"
	w, h = im.size
	a = np.empty( w * h * 4, np.float32 )
	im.pixels.foreach_get( a )
	bpy.data.images.remove( im )
	return a.reshape( h, w, 4 ), w, h


def write( path, rgb, w, h ):
	img = bpy.data.images.new( os.path.basename( path ), w, h, alpha=False )
	img.colorspace_settings.name = "Non-Color"
	a = np.ones( ( h, w, 4 ), np.float32 )
	a[:, :, :3] = rgb if rgb.ndim == 3 else rgb[:, :, None]
	img.pixels.foreach_set( a.ravel() )
	img.filepath_raw = path
	img.file_format = "PNG"
	bpy.context.scene.render.image_settings.color_mode = "RGB"
	img.save()
	bpy.data.images.remove( img )


def texture_set( texdir, stem ):
	"""The _CS file and the matching normal file for a texture stem (e.g. T_DC15A)."""
	files = os.listdir( texdir )
	def find( suffixes ):
		for s in suffixes:
			for f in files:
				if f.lower() == ( stem + s ).lower():
					return os.path.join( texdir, f )
		return None
	return find( ( "_CS.png", "_01_CS.png" ) ), find( ( "_NAM.png", "_NMA.png", "_NMO.png", "_NM.png", "_NS.png", "_01_NMA.png", "_NAOSL.png" ) )


def convert_textures( cs, nrm, out_dir, name, metal_scale=1.0 ):
	"""Writes <name>_color/_rough/_normal/_metal/_ao.png; returns the vmat lines for them."""
	os.makedirs( out_dir, exist_ok=True )
	rel = os.path.relpath( out_dir, ASSETS ).replace( "\\", "/" )
	lines = []
	if cs:
		a, w, h = read( cs )
		write( os.path.join( out_dir, f"{name}_color.png" ), a[:, :, :3], w, h )
		write( os.path.join( out_dir, f"{name}_rough.png" ), 1.0 - a[:, :, 3], w, h )
		lines += [f'\tTextureColor "{rel}/{name}_color.png"', f'\tTextureRoughness "{rel}/{name}_rough.png"']
	else:
		lines += ['\tTextureColor "materials/default/default_color.tga"', '\tTextureRoughness "materials/default/default_rough.tga"']
	metal = False
	if nrm:
		a, w, h = read( nrm )
		x = a[:, :, 0] * 2 - 1
		y = a[:, :, 1] * 2 - 1
		z = np.sqrt( np.clip( 1 - x * x - y * y, 0, 1 ) )
		write( os.path.join( out_dir, f"{name}_normal.png" ), np.dstack( ( a[:, :, 0], a[:, :, 1], z * 0.5 + 0.5 ) ), w, h )
		lines.append( f'\tTextureNormal "{rel}/{name}_normal.png"' )
		low = os.path.basename( nrm ).lower()
		# the two masks after the normal: NMA = metal, AO; NAM = AO, metal
		if low.endswith( "_nma.png" ) or low.endswith( "_nam.png" ) or low.endswith( "_nmo.png" ):      # NMO (the droids): metal, occlusion
			mi, ai = ( 3, 2 ) if low.endswith( "_nam.png" ) else ( 2, 3 )
			write( os.path.join( out_dir, f"{name}_metal.png" ), a[:, :, mi] * metal_scale, w, h )
			write( os.path.join( out_dir, f"{name}_ao.png" ), a[:, :, ai], w, h )
			# the second mask isn't occlusion on the weapons (it averages 2-20 %, so it blacked the guns out with white
			# specks where it was bright; Angelo, 2026-10-13): metalness only, the default AO
			lines += [f'\tTextureMetalness "{rel}/{name}_metal.png"', '\tTextureAmbientOcclusion "materials/default/default_ao.tga"']
			metal = True
	else:
		lines.append( '\tTextureNormal "materials/default/default_normal.tga"' )
	if not metal:
		lines += ['\tTextureAmbientOcclusion "materials/default/default_ao.tga"', '\tg_flMetalness "0.400"']
	return lines, metal


def write_vmat( path, tex_lines, metal ):
	lines = ["// Generated by ModelSource/bf2_props.py from Battlefront II textures (local only, never publish)", "", "Layer0", "{",
		'\tshader "shaders/complex.shader"', ""]
	if metal:
		lines.append( "\tF_METALNESS_TEXTURE 1" )
	lines += ["", '\tg_flModelTintAmount "1.000"', '\tg_vColorTint "[1.000000 1.000000 1.000000 0.000000]"'] + tex_lines + [
		'\tg_flRoughnessScaleFactor "1.000"', '\tg_bFogEnabled "1"', "}"]
	with open( path, "w", newline="\n" ) as fh:
		fh.write( "\n".join( lines ) + "\n" )


# ---------------------------------------------------------------------------------------------------------------------
# Meshes
# ---------------------------------------------------------------------------------------------------------------------

def import_lod0( fbx ):
	"""Imports an FBX, keeps the lod0 meshes with their skinning applied (rest pose), as plain meshes in metres."""
	bpy.ops.wm.read_factory_settings( use_empty=True )
	bpy.ops.import_scene.fbx( filepath=fbx )
	dg = bpy.context.evaluated_depsgraph_get()
	imported = list( bpy.data.objects )
	out = []
	for o in imported:
		if o.type != "MESH" or "lod0" not in o.name.lower():
			continue
		ev = o.evaluated_get( dg )
		me = bpy.data.meshes.new_from_object( ev, preserve_all_data_layers=True, depsgraph=dg )
		me.transform( o.matrix_world )
		out.append( ( o.name.split( ":" )[0], me ) )
	for o in imported:
		bpy.data.objects.remove( o, do_unlink=True )
	out = [bpy.data.objects.new( n, me ) for ( n, me ) in out]
	for ob in out:
		bpy.context.scene.collection.objects.link( ob )
	return out


def bounds( objs ):
	pts = [v.co for o in objs for v in o.data.vertices]
	lo = Vector( [min( p[i] for p in pts ) for i in range( 3 )] )
	hi = Vector( [max( p[i] for p in pts ) for i in range( 3 )] )
	return lo, hi


def frame_matrix( objs, frame, extra ):
	"""The re-basing for each kind (see the module docstring). BF2 hand props: the long axis is Y (barrel towards -Y), up Z,
	the grip at the origin; hilts lie along Z."""
	lo, hi = bounds( objs )
	size = hi - lo
	if frame == "gun":
		# barrel along the longest horizontal axis, towards its far end from the grip (origin)
		if size.x >= size.y:
			fwd = Vector( ( 1, 0, 0 ) ) if hi.x > -lo.x else Vector( ( -1, 0, 0 ) )
		else:
			fwd = Vector( ( 0, 1, 0 ) ) if hi.y > -lo.y else Vector( ( 0, -1, 0 ) )
		up = Vector( ( 0, 0, 1 ) )
		left = up.cross( fwd )
		rot = Matrix( ( fwd, left, up ) )          # rows: new axes in old coordinates
		return Matrix.Scale( M, 4 ) @ rot.to_4x4()
	if frame == "hilt":
		# extra: the hilt length, or (length, "mouth"): then the emitter is the end that's hollow (an open mouth, no
		# vertices near the axis) when only one is, else the wider end as before (checked on renders, 2026-10-13)
		top = isinstance( extra, tuple )
		if top:
			extra = extra[0]
		axis = max( range( 3 ), key=lambda i: size[i] )
		along = Vector( [1 if i == axis else 0 for i in range( 3 )] )
		# the emitter: the end with the wider cross-section
		def width_at( sign ):
			pts = [v.co for o in objs for v in o.data.vertices]
			end = hi[axis] if sign > 0 else lo[axis]
			ring = [p for p in pts if abs( p[axis] - end ) < size[axis] * 0.12]
			return max( ( Vector( p ) - Vector( p ).project( along ) ).length for p in ring ) if ring else 0
		def hole_at( sign ):
			pts = [v.co for o in objs for v in o.data.vertices]
			end = hi[axis] if sign > 0 else lo[axis]
			c = ( lo + hi ) / 2
			ring = [p for p in pts if abs( p[axis] - end ) < size[axis] * 0.04]
			return min( ( ( Vector( p ) - c ) - ( Vector( p ) - c ).project( along ) ).length for p in ring ) if ring else 0
		if top and ( ( hole_at( -1 ) > 0.006 ) != ( hole_at( 1 ) > 0.006 ) ):     # metres: ~0.24 in
			if hole_at( -1 ) > hole_at( 1 ):
				along = -along
		elif width_at( -1 ) > width_at( 1 ):
			along = -along
		other = [i for i in range( 3 ) if i != axis]
		front = Vector( [1 if i == other[0] else 0 for i in range( 3 )] )
		left = front.cross( along )
		rot = Matrix( ( along, left, front ) )
		k = extra / ( size[axis] )                  # metres -> units so the hilt is exactly its length
		m = Matrix.Scale( k, 4 ) @ rot.to_4x4()
		# pommel's end at x = 0, centred across
		end = lo[axis] if along[axis] > 0 else -hi[axis]
		c = ( lo + hi ) / 2
		t = Vector( ( -end * k, -( left.dot( c ) ) * k, -( front.dot( c ) ) * k ) )
		return Matrix.Translation( t ) @ m
	# item: centred, longest side along x, resting on z = 0
	axis = max( ( 0, 1 ), key=lambda i: size[i] )
	fwd = Vector( ( 1, 0, 0 ) ) if axis == 0 else Vector( ( 0, 1, 0 ) )
	up = Vector( ( 0, 0, 1 ) )
	rot = Matrix( ( fwd, up.cross( fwd ), up ) )
	c = ( lo + hi ) / 2
	m = Matrix.Scale( M, 4 ) @ rot.to_4x4()
	return Matrix.Translation( ( 0, 0, ( c.z - lo.z ) * M ) ) @ m @ Matrix.Translation( -c )


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
						import_rotation = [ 0.0, -90.0, 0.0 ]
						import_scale = 0.01
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
			},%(extra)s
		]
		model_archetype = ""
		primary_associated_entity = ""
		anim_graph_name = ""
		base_model_name = ""
	}
}
"""

ATTACH = """
			{
				_class = "AttachmentList"
				children =
				[
					{
						_class = "Attachment"
						name = "muzzle"
						parent_bone = ""
						relative_origin = [ %(x).2f, 0.0, %(z).2f ]
						relative_angles = [ 0.0, 0.0, 0.0 ]
						weight = 1.0
						ignore_rotation = false
					},
				]
			},"""

PHYS = """
			{
				_class = "PhysicsHullFromRender"
				parent_mesh_name = ""
				surface_prop = "metal"
				collision_tags = "solid"
				faces_per_hull = 0
			},"""


def build( asset, src, frame, extra ):
	fbx = os.path.join( BF2, src )
	objs = import_lod0( fbx )
	if not objs:
		print( "BF2PROP skip (no lod0):", asset )
		return None
	mtx = frame_matrix( objs, frame, extra )
	out_dir = os.path.join( ASSETS, os.path.dirname( asset ) )
	name = os.path.basename( asset )
	tex_dir = os.path.join( out_dir, "bf2tex" )
	texdir_src = os.path.join( os.path.dirname( fbx ), "Textures" )
	textures = [f for f in os.listdir( texdir_src ) if f.lower().endswith( "_cs.png" )] if os.path.isdir( texdir_src ) else []
	for o in objs:
		o.data.transform( mtx )
		if mtx.determinant() < 0:
			o.data.flip_normals()
		# which texture set: the CS file sharing the most of the mesh's name, else the only one
		key = o.name.lower().replace( "m_", "" ).replace( "_", "" )
		best = max( textures, key=lambda t: sum( 1 for k in range( len( key ) - 2 ) if key[k:k + 3] in t.lower().replace( "_", "" ) ), default=None )
		stem = best[:-7] if best else None
		mat_name = f"{name}_{( stem or 'flat' ).lower()}"
		vmat_rel = f"{os.path.relpath( tex_dir, ASSETS ).replace( chr( 92 ), '/' )}/{mat_name}.vmat"
		if not os.path.exists( os.path.join( tex_dir, mat_name + ".vmat" ) ):
			cs, nrm = texture_set( texdir_src, stem ) if stem else ( None, None )
			# hand-held props at 40% metalness: full BF2 metal reflected the ship's dim interiors back as near-black guns (2026-10-12)
			lines, metal = convert_textures( cs, nrm, tex_dir, mat_name, metal_scale=0.4 )
			write_vmat( os.path.join( tex_dir, mat_name + ".vmat" ), lines, metal )
		mat = bpy.data.materials.get( vmat_rel ) or bpy.data.materials.new( vmat_rel )
		o.data.materials.clear()
		o.data.materials.append( mat )
	# join and export
	bpy.ops.object.select_all( action="DESELECT" )
	for o in objs:
		o.select_set( True )
	bpy.context.view_layer.objects.active = objs[0]
	if len( objs ) > 1:
		bpy.ops.object.join()
	ob = bpy.context.view_layer.objects.active
	ob.name = name
	lo, hi = bounds( [ob] )
	fbx_name = f"{name}_{REV}"
	bpy.ops.export_scene.fbx( filepath=os.path.join( out_dir, fbx_name + ".fbx" ), use_selection=True, mesh_smooth_type="FACE", axis_forward="Y", axis_up="Z" )
	meta = os.path.join( out_dir, fbx_name + ".fbx.meta" )
	if not os.path.exists( meta ):
		with open( meta, "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	extra_nodes = ""
	if frame == "gun":
		# the muzzle: the barrel's far end, at the height of the barrel's tip
		pts = [v.co for v in ob.data.vertices if v.co.x > hi.x - 1.0]
		mz = sum( p.z for p in pts ) / len( pts ) if pts else 0.0
		extra_nodes = ATTACH % { "x": hi.x, "z": mz }
		print( f"BF2PROP {asset}: muzzle forward {hi.x:.1f} up {mz:.1f}, length {hi.x - lo.x:.1f}" )
	elif frame == "item":
		extra_nodes = PHYS
	rel_fbx = f"{os.path.dirname( asset )}/{fbx_name}.fbx"
	with open( os.path.join( out_dir, name + ".vmdl" ), "w", newline="\n" ) as fh:
		fh.write( VMDL % { "fbx": rel_fbx, "extra": extra_nodes } )
	revs = sorted( ( f for f in os.listdir( out_dir ) if f.startswith( name + "_" ) and f.endswith( ".fbx" ) and f[len( name ) + 1:-4].isalnum() ),
		key=lambda f: os.path.getmtime( os.path.join( out_dir, f ) ) )
	for old in revs[:-2]:
		for fn in ( old, old + ".meta" ):
			try:
				os.remove( os.path.join( out_dir, fn ) )
			except OSError:
				pass
	print( f"BF2PROP {asset}: {len( ob.data.polygons )} faces, bounds {[round( x, 1 ) for x in lo]} {[round( x, 1 ) for x in hi]}" )
	return ob


def main():
	only = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	for asset, src, frame, extra in PROPS:
		if only and not any( o in asset for o in only ):
			continue
		build( asset, src, frame, extra )


if __name__ == "__main__":
	main()
