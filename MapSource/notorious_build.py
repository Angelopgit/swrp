"""
Ports X:\\SWRP Projects\\Notorious_VMF\\rp_venator_notorious_os.vmf (Source engine map) into the swrp s&box project.

    blender -b --factory-startup -P MapSource/notorious_build.py

What comes across: world brushes plus func_detail and func_brush brushes (rendered and as collision), the player spawn
points, a clustered set of the map's lights, and the brig cell positions. What doesn't: props (MDL models), displacements,
doors/buttons/triggers and all entity logic. Source textures aren't available, so each material is mapped to one of the
project's PBR texture sets with a tint.

Outputs (Assets/models/notorious/): notorious_<rev>.fbx + _phys.fbx (+ .meta so the editor registers them) and notorious.vmdl,
Assets/materials/notorious/*.vmat, and MapSource/notorious_entities.json (spawns, lights, jail points) for the scene.

LICENCE: the VMF's README says edits must be released as a public, open-source Steam Workshop map crediting all authors
(Bananakin, Goldie, Halves, 122jimbo, Lord Trilobite, Grady, Bishop). Keep this port local until those terms are met.
"""
import os
import sys
import json
import math
import re
import time
import uuid

import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import vmf_parse as vp
import notorious_doors as nd
import notorious_transit as nt
import notorious_holo as nh
import regions
import notorious_props as npr
import notorious_corridors as ncor

CORRIDOR_KIT = False         # lay the Venator corridor kit into the corridors (notorious_corridors.py)

PROJECT = os.path.dirname( HERE )
VMF = r"X:\SWRP Projects\Notorious_VMF\rp_venator_notorious_os.vmf"
MODEL_DIR = os.path.join( PROJECT, "Assets", "models", "notorious" )
MAT_DIR = os.path.join( PROJECT, "Assets", "materials", "notorious" )
os.makedirs( MODEL_DIR, exist_ok=True )
os.makedirs( MAT_DIR, exist_ok=True )


REV = format( int( time.time() ), "x" )[-6:]

# ---------------------------------------------------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------------------------------------------------

# Source tool textures. Never drawn; the solid ones still collide.
NO_DRAW = ( "TOOLS/TOOLSNODRAW", "TOOLS/TOOLSPLAYERCLIP", "TOOLS/TOOLSCLIP", "TOOLS/TOOLSBLOCKLIGHT", "TOOLS/TOOLSSKIP", "TOOLS/TOOLSHINT" )
NON_SOLID = ( "TOOLS/TOOLSTRIGGER", "TOOLS/TOOLSSKYBOX", "TOOLS/TOOLSINVISIBLELADDER", "TOOLS/TOOLSBLOCKLIGHT", "TOOLS/TOOLSSKIP",
              "TOOLS/TOOLSHINT", "TOOLS/TOOLSAREAPORTAL", "TOOLS/TOOLSOCCLUDER", "TOOLS/TOOLSFOG", "TOOLS/TOOLSBLACK",
              "TOOLS/TOOLSNPCCLIP" )     # NPC clip never blocked players


def is_tool( m ):
	return m.startswith( "TOOLS/" )


def classify( name ):
	"""Source material name -> (texture set or None, tint, emissive rgb or None, opacity or None, uv scale)."""
	n = name.upper()

	def h( s, lo=0.55, hi=1.0 ):
		v = ( sum( ord( c ) * ( i + 1 ) for i, c in enumerate( s ) ) % 1000 ) / 1000
		return lo + ( hi - lo ) * v

	if "LIGHT" in n and any( k in n for k in ( "RED", "GREEN", "BLUE", "WHITE", "PILLAR", "YELLOW" ) ):
		col = ( 1.0, 0.18, 0.12 ) if "RED" in n else ( 0.15, 1.0, 0.25 ) if "GREEN" in n else ( 0.3, 0.6, 1.0 ) if "BLUE" in n else ( 1.0, 0.8, 0.3 ) if "YELLOW" in n else ( 0.95, 0.97, 1.0 )
		return None, col, col, None, 64
	if "GLASS" in n or "WINDOW" in n:
		return None, ( 0.5, 0.65, 0.8 ), None, 0.12, 128
	if "RAYSHIELD" in n or "FORCE" in n:
		return None, ( 0.3, 0.6, 1.0 ), ( 0.03, 0.08, 0.2 ), 0.06, 128
	if "BACTA" in n:
		return None, ( 0.2, 0.8, 0.9 ), ( 0.1, 0.5, 0.6 ), 0.45, 128
	if "FLOOR" in n or "GRID" in n or "HOLOCRON" in n:
		tint = ( 1.5, 1.5, 1.6 )
		if "BLUE" in n: tint = ( 0.7, 1.0, 1.9 )
		elif "RED" in n: tint = ( 1.9, 0.7, 0.7 )
		return ( "deck_hangar" if "REFLECTIVE" in n else "deck" ), tint, None, None, 256
	if "METAL" in n or "GRATE" in n:
		return "metal", ( 0.7, 0.72, 0.78 ), None, None, 64
	if "HULL" in n or "EXTERIOR" in n:
		v = 0.35 if "DARK" in n else 0.95 if "LIGHT" in n else 0.7
		return "hull_wall", ( v, v, v * 1.05 ), None, None, 256
	# wall panels and everything else
	v = 1.0
	if "DARKEST" in n: v = 0.32
	elif "DARKER" in n: v = 0.5
	elif "NOTASDARK" in n: v = 0.7
	elif "LIGHTEST" in n: v = 1.1
	elif "GREY" in n: v = 0.85
	else: v = h( n, 0.65, 1.05 )
	tint = ( v, v, v * 1.04 )
	if "YELLOW" in n: tint = ( v * 1.1, v * 0.95, v * 0.45 )
	elif "RED" in n: tint = ( v * 1.0, v * 0.45, v * 0.4 )
	elif "BLUE" in n: tint = ( v * 0.5, v * 0.7, v * 1.15 )
	return "hull_wall", tint, None, None, 256


GMOD = {}     # Source material name -> info from gmod_textures.py (texture size, whether it matched)


def slug( name ):
	return re.sub( r"[^a-z0-9]+", "_", name.lower() ).strip( "_" )


FAKE_WHITE = "SKYBOX/SKY_FAKE_WHITE"
LIGHT_PANEL = "light_panel_white"
LIGHT_PANEL_GLOW = 1.6         # strips use 3.0; the panels are big, so softer keeps the bloom from washing rooms out
GLASS = "glass_pane"
_written = set()


def write_text( path, text ):
	"""Write a generated file only when it changed (the editor recompiles on every write and may hold the file open)."""
	if os.path.exists( path ) and open( path, encoding="utf-8" ).read() == text:
		return
	for attempt in range( 10 ):
		try:
			with open( path, "w", newline="\n", encoding="utf-8" ) as fh:
				fh.write( text )
			return
		except PermissionError:
			time.sleep( 0.5 )


def special_material( name ):
	"""
	Materials the map gets wrong on its own. "sky_fake_white" is what the authors used for every glowing light bar, lamp and
	screen backlight (742 props and the hangar strips); its texture isn't in the addon, so it becomes a warm white emitter.
	The clear window glass is nearly invisible in the source texture, so windows get a faint blue, glossy pane instead.
	Returns a vmat key under materials/notorious, or None.
	"""
	n = name.upper()
	if "SKY_FAKE_WHITE" in n:
		path = os.path.join( MAT_DIR, LIGHT_PANEL + ".vmat" )
		if LIGHT_PANEL not in _written:
			_written.add( LIGHT_PANEL )
			write_vmat( LIGHT_PANEL + "_tmp", None, ( 1.0, 0.98, 0.95 ), ( 1.0, 0.97, 0.92 ), None )
			tmp = os.path.join( MAT_DIR, LIGHT_PANEL + "_tmp.vmat" )
			text = open( tmp, encoding="utf-8" ).read().replace( 'g_flSelfIllumBrightness "3.000"', f'g_flSelfIllumBrightness "{LIGHT_PANEL_GLOW:.3f}"' )
			os.remove( tmp )
			write_text( path, text )
		return LIGHT_PANEL
	if "GLASS_CLEAR" in n:
		write_text( os.path.join( MAT_DIR, GLASS + ".vmat" ), "\n".join( [
			"// Generated by MapSource/notorious_build.py: bridge and window glass", "", "Layer0", "{", '\tshader "shaders/complex.shader"', "",
			"\tF_TRANSLUCENT 1", "\tF_RENDER_BACKFACES 1", "",
			'\tg_flModelTintAmount "1.000"', '\tg_vColorTint "[0.60000 0.78000 1.00000 0.000000]"',
			'\tTextureColor "materials/default/default_color.tga"', '\tTextureNormal "materials/default/default_normal.tga"',
			'\tTextureRoughness "materials/default/default_rough.tga"', '\tTextureAmbientOcclusion "materials/default/default_ao.tga"',
			'\tg_flMetalness "0.300"', '\tg_flRoughnessScaleFactor "0.080"', '\tg_bFogEnabled "1"',
			'\tg_flOpacityScale "0.200"', '\tTextureTranslucency "[1.000000 1.000000 1.000000 0.000000]"', "}", "" ] ) )
		return GLASS
	return None


def write_vmat( name, tex, tint, emissive, opacity ):
	lines = [
		"// Generated by MapSource/notorious_build.py", "", "Layer0", "{", '\tshader "shaders/complex.shader"', "",
	]
	if emissive:
		lines.append( "\tF_SELF_ILLUM 1" )
	if opacity is not None:
		lines.append( "\tF_TRANSLUCENT 1" )
	lines += ["", '\tg_flModelTintAmount "1.000"', '\tg_vColorTint "[%.5f %.5f %.5f 0.000000]"' % tuple( tint )]
	if tex:
		for key, suffix in ( ( "TextureColor", "color" ), ( "TextureNormal", "normal" ), ( "TextureRoughness", "rough" ), ( "TextureAmbientOcclusion", "ao" ), ( "TextureMetalness", "metal" ) ):
			lines.append( f'\t{key} "materials/notorious/{tex}_{suffix}.png"' )
		lines.append( '\tg_flRoughnessScaleFactor "1.000"' )
	else:
		lines += [
			'\tTextureColor "materials/default/default_color.tga"', '\tTextureNormal "materials/default/default_normal.tga"',
			'\tTextureRoughness "materials/default/default_rough.tga"', '\tTextureAmbientOcclusion "materials/default/default_ao.tga"',
			'\tg_flMetalness "0.100"', '\tg_flRoughnessScaleFactor "0.700"',
		]
	lines.append( '\tg_bFogEnabled "1"' )
	if emissive:
		lines += [
			'\tg_flSelfIllumAlbedoFactor "0.000"', '\tg_flSelfIllumBrightness "3.000"', '\tg_flSelfIllumScale "1.000"',
			'\tg_vSelfIllumTint "[%.5f %.5f %.5f 0.000000]"' % tuple( emissive ),
			'\tTextureSelfIllumMask "[1.000000 1.000000 1.000000 0.000000]"',
		]
	if opacity is not None:
		lines += [f'\tg_flOpacityScale "{opacity:.3f}"', '\tTextureTranslucency "[1.000000 1.000000 1.000000 0.000000]"']
	lines.append( "}" )
	with open( os.path.join( MAT_DIR, name + ".vmat" ), "w", newline="\n" ) as fh:
		fh.write( "\n".join( lines ) + "\n" )


# ---------------------------------------------------------------------------------------------------------------------
# Geometry
# ---------------------------------------------------------------------------------------------------------------------

def uv_for( verts, normal, scale ):
	if isinstance( scale, tuple ) and scale and scale[0] == "uv":
		return [( u, -v ) for ( u, v ) in scale[1]]          # model UVs (Source v points down)
	if isinstance( scale, dict ):
		# Source texture projection: u = (dot(p, axis) / scale + shift) / texture width
		( uv3, ush, usc ), ( vv3, vsh, vsc ) = scale["u"], scale["v"]
		w, h = scale["w"], scale["h"]
		return [( ( ( p[0] * uv3[0] + p[1] * uv3[1] + p[2] * uv3[2] ) / usc + ush ) / w, -( ( ( p[0] * vv3[0] + p[1] * vv3[1] + p[2] * vv3[2] ) / vsc + vsh ) / h ) ) for p in verts]
	ax = max( range( 3 ), key=lambda i: abs( normal[i] ) )
	out = []
	for v in verts:
		if ax == 0:
			u, w = v[1], v[2]
			if normal[0] < 0: u = -u
		elif ax == 1:
			u, w = v[0], v[2]
			if normal[1] > 0: u = -u
		else:
			u, w = v[0], v[1]
			if normal[2] < 0: w = -w
		out.append( ( u / scale, w / scale ) )
	return out


def build_object( name, faces ):
	"""faces: list of (material_path, verts, normal, uv_scale). Windings are already outward."""
	mesh = bpy.data.meshes.new( name )
	obj = bpy.data.objects.new( name, mesh )
	bpy.context.scene.collection.objects.link( obj )
	bm = bmesh.new()
	uvl = bm.loops.layers.uv.new( "UVMap" )
	mat_index = {}
	vcache = {}
	for ( mat, verts, normal, scale ) in faces:
		if mat not in mat_index:
			m = bpy.data.materials.get( mat ) or bpy.data.materials.new( mat )
			obj.data.materials.append( m )
			mat_index[mat] = len( obj.data.materials ) - 1
		bverts = []
		for v in verts:
			key = ( round( v[0], 2 ), round( v[1], 2 ), round( v[2], 2 ) )
			if key not in vcache:
				vcache[key] = bm.verts.new( Vector( v ) )
			bverts.append( vcache[key] )
		try:
			f = bm.faces.new( bverts )
		except ValueError:
			continue
		f.material_index = mat_index[mat]
		for loop, uv in zip( f.loops, uv_for( verts, normal, scale ) ):
			loop[uvl].uv = uv
	bm.to_mesh( mesh )
	bm.free()
	return obj


VMDL_TEMPLATE = """<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->
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
						filename = "models/notorious/%(render)s.fbx"
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
			},
			{
				_class = "PhysicsShapeList"
				children =
				[
					{
						_class = "PhysicsMeshFile"
						name = "%(phys)s"
						parent_bone = ""
						surface_prop = "metal"
						collision_tags = "solid"
						recenter_on_parent_bone = false
						offset_origin = [ 0.0, 0.0, 0.0 ]
						offset_angles = [ 0.0, -90.0, 0.0 ]
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						filename = "models/notorious/%(phys)s.fbx"
						import_scale = 0.01
						maxMeshVertices = 0
						qemError = 0.0
						import_filter =
						{
							exclude_by_default = false
							exception_list = [  ]
						}
					},
				]
			},
		]
		model_archetype = "static_prop_model"
		primary_associated_entity = "prop_static"
		anim_graph_name = ""
		base_model_name = ""
	}
}
"""


def export( render_faces, phys_faces ):
	rname = f"notorious_{REV}"
	pname = f"notorious_{REV}_phys"
	ob = build_object( rname, render_faces )
	bpy.ops.object.select_all( action="DESELECT" )
	ob.select_set( True )
	bpy.ops.export_scene.fbx( filepath=os.path.join( MODEL_DIR, rname + ".fbx" ), use_selection=True, mesh_smooth_type="FACE", axis_forward="Y", axis_up="Z" )
	pb = build_object( pname, [( "materials/notorious/phys.vmat", v, n, 256 ) for ( _, v, n, _ ) in phys_faces] )
	bpy.ops.object.select_all( action="DESELECT" )
	pb.select_set( True )
	bpy.ops.export_scene.fbx( filepath=os.path.join( MODEL_DIR, pname + ".fbx" ), use_selection=True, mesh_smooth_type="FACE", axis_forward="Y", axis_up="Z" )

	# Register the new files ourselves, otherwise the editor can loop recompiling the model (see README "Map").
	for fn in ( rname + ".fbx", pname + ".fbx" ):
		meta = os.path.join( MODEL_DIR, fn + ".meta" )
		if not os.path.exists( meta ):
			with open( meta, "w", newline="\n" ) as fh:
				fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	with open( os.path.join( MODEL_DIR, "notorious.vmdl" ), "w", newline="\n" ) as fh:
		fh.write( VMDL_TEMPLATE % { "render": rname, "phys": pname } )

	# keep this and the previous revision (the editor still lists the previous one as an input)
	revs = {}
	for fn in os.listdir( MODEL_DIR ):
		if fn.endswith( ".fbx" ) and fn.startswith( "notorious_" ) and not fn.endswith( "_phys.fbx" ):
			revs[fn[:-4]] = os.path.getmtime( os.path.join( MODEL_DIR, fn ) )
	for stem in sorted( revs, key=revs.get )[:-2]:
		for fn in ( f"{stem}.fbx", f"{stem}.fbx.meta", f"{stem}_phys.fbx", f"{stem}_phys.fbx.meta" ):
			try:
				os.remove( os.path.join( MODEL_DIR, fn ) )
			except OSError:
				pass


STRIP_LIGHTS = []


def ceiling_strips( render ):
	"""
	Two thin glowing white strips down the length of every corridor-shaped ceiling (a long, narrow face pointing down), like the
	light strips in the original map's corridors. Returns extra render faces and a list of blue spill light positions.
	"""
	out, seen = [], set()
	for ( mat, verts, normal, scale ) in list( render ):
		if normal[2] > -0.95 or len( verts ) != 4 or "nodraw_fill" in mat:
			continue
		xs = [v[0] for v in verts]
		ys = [v[1] for v in verts]
		z = sum( v[2] for v in verts ) / 4
		wx, wy = max( xs ) - min( xs ), max( ys ) - min( ys )
		short, long_ = min( wx, wy ), max( wx, wy )
		if not ( 110 <= short <= 420 and long_ >= 2.5 * short and long_ >= 380 ):
			continue
		key = ( round( min( xs ) ), round( min( ys ) ), round( max( xs ) ), round( max( ys ) ), round( z ) )
		if key in seen:
			continue
		seen.add( key )
		along_x = wx >= wy
		cx, cy = ( min( xs ) + max( xs ) ) / 2, ( min( ys ) + max( ys ) ) / 2
		inset = 24.0
		n_l = max( 1, int( long_ // 420 ) )
		for i in range( n_l ):
			t = ( i + 0.5 ) / n_l
			lx = min( xs ) + wx * t if along_x else cx
			ly = cy if along_x else min( ys ) + wy * t
			STRIP_LIGHTS.append( { "x": lx, "y": ly, "z": z - 40 } )
		for off in ( -0.27, 0.27 ):
			half_w = 6.0
			a0, a1 = ( min( xs ) + inset, max( xs ) - inset ) if along_x else ( min( ys ) + inset, max( ys ) - inset )
			c = ( cy if along_x else cx ) + off * short
			zz = z - 0.8
			if along_x:
				quad = [( a0, c - half_w, zz ), ( a0, c + half_w, zz ), ( a1, c + half_w, zz ), ( a1, c - half_w, zz )]
			else:
				quad = [( c - half_w, a0, zz ), ( c + half_w, a0, zz ), ( c + half_w, a1, zz ), ( c - half_w, a1, zz )]
			# wind so the face points down
			e1 = ( quad[1][0] - quad[0][0], quad[1][1] - quad[0][1] )
			e2 = ( quad[2][0] - quad[1][0], quad[2][1] - quad[1][1] )
			if e1[0] * e2[1] - e1[1] * e2[0] > 0:
				quad.reverse()
			out.append( ( "materials/notorious/strip_white.vmat", quad, ( 0.0, 0.0, -1.0 ), 64.0 ) )
	return out


def blue_spill( render ):
	"""Cluster the blue glowing floor / outline faces into soft blue light positions."""
	cells = {}
	for ( mat, verts, normal, scale ) in render:
		if "blue_floor" not in mat:
			continue
		cx = sum( v[0] for v in verts ) / len( verts )
		cy = sum( v[1] for v in verts ) / len( verts )
		cz = sum( v[2] for v in verts ) / len( verts )
		k = ( round( cx / 450 ), round( cy / 450 ), round( cz / 200 ) )
		c = cells.setdefault( k, [0, 0.0, 0.0, 0.0] )
		c[0] += 1; c[1] += cx; c[2] += cy; c[3] += cz
	out = [{ "x": c[1] / c[0], "y": c[2] / c[0], "z": c[3] / c[0] + 70, "n": c[0] } for c in cells.values()]
	out.sort( key=lambda d: -d["n"] )
	return out[:60]


# Materials that never converted get a close stand-in; hidden tool faces get a dark wall panel so props that are not ported
# (and used to cover them) leave no holes. FAKESKY window imposters are left out so the real space sky shows through.
ALIAS = {
	"BANANAKIN/VARDOS/BANANAKIN_VARDOS_WALL_EMPTY": "ROADSIDE/2003/WALL_PANEL_05_GREY",
	"ROADSIDE/2003/WALL_DETAIL_01": "ROADSIDE/2003/WALL_DETAILED_PANEL3",
	"TOOLS/TOOLSNODRAW": "ROADSIDE/2003/WALL_PANEL_DARKER",
}
HIDDEN = ( "FAKESKY/", )


# Hidden faces are only filled inside the playable areas (regions.py): the VMF also seals the whole level in huge nodraw boxes.


def fill_ok( f ):
	vs = f["verts"]
	c = [sum( p[i] for p in vs ) / len( vs ) for i in range( 3 )]
	if regions.area_of( c ) is None:
		return False
	return max( max( p[i] for p in vs ) - min( p[i] for p in vs ) for i in range( 3 ) ) < 3000.0


def _fwd( angles ):
	"""Source 'pitch yaw roll' -> forward vector."""
	pch, yaw = [math.radians( float( x ) ) for x in angles.split()[:2]]
	return ( math.cos( pch ) * math.cos( yaw ), math.cos( pch ) * math.sin( yaw ), -math.sin( pch ) )


def _bounds( solids ):
	pts = [p for so in solids for f in vp.solid_faces( so ) for p in f["verts"]]
	return [min( p[i] for p in pts ) for i in range( 3 )], [max( p[i] for p in pts ) for i in range( 3 )]


PROP_VMDL = """<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->
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
						filename = "models/notorious/props/%(fbx)s.fbx"
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
			},%(physics)s
		]
		model_archetype = "static_prop_model"
		primary_associated_entity = "prop_static"
		anim_graph_name = ""
		base_model_name = ""
	}
}
"""

PROP_PHYS = """
			{
				_class = "PhysicsShapeList"
				children =
				[
					{
						_class = "PhysicsMeshFile"
						name = "%(phys)s"
						parent_bone = ""
						surface_prop = "metal"
						collision_tags = "solid"
						recenter_on_parent_bone = false
						offset_origin = [ 0.0, 0.0, 0.0 ]
						offset_angles = [ 0.0, -90.0, 0.0 ]
						align_origin_x_type = "None"
						align_origin_y_type = "None"
						align_origin_z_type = "None"
						filename = "models/notorious/props/%(phys)s.fbx"
						import_scale = 0.01
						maxMeshVertices = 0
						qemError = 0.0
						import_filter =
						{
							exclude_by_default = false
							exception_list = [  ]
						}
					},
				]
			},"""


def export_props( chunks, phys ):
	"""
	One render FBX, one collision FBX and a vmdl per chunk in Assets/models/notorious/props, revisioned like the map model;
	list in Assets/data. Collision lives in the chunk's own model, so it only exists where the props actually load.
	"""
	pdir = os.path.join( MODEL_DIR, "props" )
	os.makedirs( pdir, exist_ok=True )
	names = []
	for cell, faces in sorted( chunks.items() ):
		if not faces:
			continue
		name = f"props_{cell}"
		fbx = f"{name}_{REV}"
		bpy.ops.object.select_all( action="DESELECT" )
		ob = build_object( fbx, faces )
		ob.select_set( True )
		bpy.ops.export_scene.fbx( filepath=os.path.join( pdir, fbx + ".fbx" ), use_selection=True, mesh_smooth_type="FACE", axis_forward="Y", axis_up="Z" )
		bpy.data.objects.remove( ob, do_unlink=True )
		meta = os.path.join( pdir, fbx + ".fbx.meta" )
		if not os.path.exists( meta ):
			with open( meta, "w", newline="\n" ) as fh:
				fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
		physics = ""
		if phys.get( cell ):
			pfbx = f"{fbx}_phys"
			bpy.ops.object.select_all( action="DESELECT" )
			pb = build_object( pfbx, [( "materials/notorious/phys.vmat", v, n, 256 ) for ( v, n ) in phys[cell]] )
			pb.select_set( True )
			bpy.ops.export_scene.fbx( filepath=os.path.join( pdir, pfbx + ".fbx" ), use_selection=True, mesh_smooth_type="FACE", axis_forward="Y", axis_up="Z" )
			bpy.data.objects.remove( pb, do_unlink=True )
			pmeta = os.path.join( pdir, pfbx + ".fbx.meta" )
			if not os.path.exists( pmeta ):
				with open( pmeta, "w", newline="\n" ) as fh:
					fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
			physics = PROP_PHYS % { "phys": pfbx }
		with open( os.path.join( pdir, name + ".vmdl" ), "w", newline="\n" ) as fh:
			fh.write( PROP_VMDL % { "fbx": fbx, "physics": physics } )
		names.append( f"models/notorious/props/{name}.vmdl" )

		# keep this and the previous revision only
		revs = sorted( ( f for f in os.listdir( pdir ) if f.startswith( name + "_" ) and f.endswith( ".fbx" ) and not f.endswith( "_phys.fbx" ) ),
			key=lambda f: os.path.getmtime( os.path.join( pdir, f ) ) )
		for old in revs[:-2]:
			for fn in ( old, old + ".meta", old[:-4] + "_phys.fbx", old[:-4] + "_phys.fbx.meta" ):
				try:
					os.remove( os.path.join( pdir, fn ) )
				except OSError:
					pass

	with open( os.path.join( PROJECT, "Assets", "data", "notorious_props.json" ), "w", newline="\n" ) as fh:
		json.dump( names, fh, indent=1 )
	print( f"NOTORIOUS props exported: {len( names )} chunks" )


SHIELD_OPACITY = 0.35
SHIELD_GLOW = 1.2


def shield_material( key ):
	"""A fainter copy of a shield's material ("<key>_open"): mostly see-through, with a soft blue glow."""
	name = key + "_open"
	src = os.path.join( MAT_DIR, key + ".vmat" )
	dst = os.path.join( MAT_DIR, name + ".vmat" )
	if not os.path.exists( src ):
		return key
	s = open( src, encoding="utf-8" ).read()
	if "F_TRANSLUCENT" not in s:
		s = s.replace( "\tF_SELF_ILLUM 1", "\tF_SELF_ILLUM 1\n\tF_TRANSLUCENT 1" )
		s = s.replace( "\n}", '\n\tg_flOpacityScale "1.000"\n\tTextureTranslucency "[1.000000 1.000000 1.000000 0.000000]"\n}' )
	s = re.sub( r'g_flOpacityScale "[^"]*"', f'g_flOpacityScale "{SHIELD_OPACITY:.3f}"', s )
	s = re.sub( r'g_flSelfIllumBrightness "[^"]*"', f'g_flSelfIllumBrightness "{SHIELD_GLOW:.3f}"', s )
	if os.path.exists( dst ) and open( dst, encoding="utf-8" ).read() == s:
		return name          # unchanged; rewriting it only makes the editor recompile (and it may hold the file open)
	for attempt in range( 10 ):
		try:
			with open( dst, "w", newline="\n" ) as fh:
				fh.write( s )
			break
		except PermissionError:
			time.sleep( 0.5 )        # the editor is compiling it right now
	return name


def _is_seal( so ):
	"""One of the six slabs (tool textures only, over 20000 units long) Source needs to seal the level against the void."""
	fs = vp.solid_faces( so )
	if not fs or not all( f["material"].startswith( "TOOLS/" ) for f in fs ):
		return False
	pts = [p for f in fs for p in f["verts"]]
	return max( max( p[i] for p in pts ) - min( p[i] for p in pts ) for i in range( 3 ) ) > 20000.0


def solids_of( root ):
	"""
	Yields (solid, xf). xf: None for static brushes, or dict(move=(x, y, z) | None, rot=(origin, degrees) | None, render, collide).
	Working doors (and the brig cell gates) are left out here: notorious_doors.py exports them for SwrpDoor. The lower
	hangar slides are baked open, vents, hatches and the dorsal roof closed; buttons and fans stay put. Each turbolift car is
	copied onto every deck its shaft serves (notorious_transit.py); the VMF's parked duplicates are dropped.
	"""
	world = vp.find( root, "world" )[0]
	lift_offsets = {}
	for shaft in nt.shafts( root ):
		lift_offsets[shaft["car"]["template"]] = [f["car_offset"] for f in shaft["floors"]]
	for so in vp.find( world[2], "solid" ):
		if _is_seal( so ):
			continue         # the VMF's huge sealing box: invisible walls ~13000 units out that stopped craft flying away (Angelo, 2026-10-12)
		yield so, None
	for e in vp.find( root, "entity" ):
		cls = e[1].get( "classname" )
		sol = vp.find( e[2], "solid" )
		if not sol:
			continue
		if cls == "func_brush" and nd.door_policy( e ) == "managed":
			continue         # brig cell gates become SwrpDoors
		if cls == "func_brush" and e[1].get( "Solidity" ) == "1":
			# never-solid ray shields (the hangar openings): see-through, and ships and players pass straight through
			for so in sol:
				yield so, { "move": None, "rot": None, "render": True, "collide": False, "shield": True }
			continue
		if cls == "func_movelinear" and e[1].get( "targetname", "" ) in nt.CAR_DUPLICATES:
			continue
		if cls == "func_movelinear" and e[1].get( "targetname", "" ) in lift_offsets:
			for dz in lift_offsets[e[1]["targetname"]]:
				for so in sol:
					yield so, { "move": ( 0.0, 0.0, dz ), "rot": None, "render": True, "collide": True }
			continue
		if cls in ( "func_detail", "func_brush", "func_button", "func_movelinear", "func_rotating" ):
			for so in sol:
				if cls != "func_detail" and all( f["material"].startswith( "TOOLS/" ) for f in vp.solid_faces( so ) ):
					continue     # an invisible button or mover volume: it does nothing here, so it mustn't block anyone
				yield so, None
		elif cls in ( "func_illusionary", "func_tracktrain" ):
			for so in sol:
				yield so, { "move": None, "rot": None, "render": True, "collide": False }
		elif cls == "func_door":
			pol = nd.door_policy( e )
			if pol in ( "managed", "skip" ):
				continue     # working SwrpDoors (notorious_doors.py), or outside the ship
			if pol == "closed":
				for so in sol:
					yield so, None
				continue
			d = _fwd( e[1].get( "movedir", "0 0 0" ) )
			lo, hi = _bounds( sol )
			dist = sum( abs( d[i] ) * ( hi[i] - lo[i] ) for i in range( 3 ) ) - float( e[1].get( "lip", "0" ) )
			if e[1].get( "spawnpos", "0" ) == "1":
				dist = 0.0       # already placed open
			mv = ( d[0] * dist, d[1] * dist, d[2] * dist )
			for so in sol:
				yield so, { "move": mv, "rot": None, "render": True, "collide": True }
		elif cls == "func_door_rotating":
			o = tuple( float( x ) for x in e[1].get( "origin", "0 0 0" ).split() )
			deg = float( e[1].get( "distance", "90" ) ) * ( -1 if int( e[1].get( "spawnflags", "0" ) ) & 2 else 1 )
			for so in sol:
				yield so, { "move": None, "rot": ( o, deg ), "render": True, "collide": False }


def _xf_point( p, xf ):
	if xf.get( "rot" ):
		( o, deg ) = xf["rot"]
		c, s_ = math.cos( math.radians( deg ) ), math.sin( math.radians( deg ) )
		x, y = p[0] - o[0], p[1] - o[1]
		p = ( o[0] + x * c - y * s_, o[1] + x * s_ + y * c, p[2] )
	if xf.get( "move" ):
		m = xf["move"]
		p = ( p[0] + m[0], p[1] + m[1], p[2] + m[2] )
	return p


def _xf_vec( v, xf ):
	if xf.get( "rot" ):
		c, s_ = math.cos( math.radians( xf["rot"][1] ) ), math.sin( math.radians( xf["rot"][1] ) )
		return ( v[0] * c - v[1] * s_, v[0] * s_ + v[1] * c, v[2] )
	return v


def _xf_axis( ax, xf ):
	"""Texture lock: keep the texture glued to the brush when it moves."""
	if not ax:
		return ax
	( a, shift, scale ) = ax
	na = _xf_vec( a, xf )
	# u = dot(p', na)/scale + shift'  must equal  dot(p, a)/scale + shift for p' = xf(p); use the origin to solve
	o = _xf_point( ( 0.0, 0.0, 0.0 ), xf )
	shift2 = shift - ( o[0] * na[0] + o[1] * na[1] + o[2] * na[2] ) / scale
	return ( na, shift2, scale )


def transform_face( f, xf ):
	if not xf or ( not xf.get( "move" ) and not xf.get( "rot" ) ):
		return f
	g = dict( f )
	g["verts"] = [_xf_point( p, xf ) for p in f["verts"]]
	g["normal"] = _xf_vec( f["normal"], xf )
	g["uaxis"] = _xf_axis( f.get( "uaxis" ), xf )
	g["vaxis"] = _xf_axis( f.get( "vaxis" ), xf )
	return g


def _rows( block, name, width ):
	b = vp.find( block[2], name )
	if not b:
		return None
	rows = []
	for i in range( 1000 ):
		r = b[0][1].get( f"row{i}" )
		if r is None:
			break
		vals = [float( x ) for x in r.split()]
		rows.append( [tuple( vals[j:j + width] ) if width > 1 else vals[j] for j in range( 0, len( vals ), width )] )
	return rows


def displacement_tris( f ):
	"""Tessellates a displacement face into triangles (list of 3-vertex polygons), or [] if it cannot."""
	d = f.get( "dispinfo" )
	if not d or len( f["verts"] ) != 4:
		return []
	power = int( d[1].get( "power", "2" ) )
	n = 2 ** power
	sp = tuple( float( x ) for x in d[1]["startposition"].strip( "[]" ).split() )
	# Source lists corners clockwise seen from the front; ours are counter-clockwise, so walk them backwards from the start
	cw = list( reversed( f["verts"] ) )
	k = min( range( 4 ), key=lambda i: sum( ( cw[i][j] - sp[j] ) ** 2 for j in range( 3 ) ) )
	c = cw[k:] + cw[:k]
	normals = _rows( d, "normals", 3 )
	dists = _rows( d, "distances", 1 )
	offs = _rows( d, "offsets", 3 )
	if not normals or not dists or len( normals ) < n + 1:
		return []

	def lerp( a, b, t ):
		return ( a[0] + ( b[0] - a[0] ) * t, a[1] + ( b[1] - a[1] ) * t, a[2] + ( b[2] - a[2] ) * t )

	grid = []
	for r in range( n + 1 ):
		left = lerp( c[0], c[1], r / n )
		right = lerp( c[3], c[2], r / n )
		row = []
		for col in range( n + 1 ):
			p = lerp( left, right, col / n )
			nv = normals[r][col]
			dd = dists[r][col]
			ov = offs[r][col] if offs else ( 0, 0, 0 )
			row.append( ( p[0] + nv[0] * dd + ov[0], p[1] + nv[1] * dd + ov[1], p[2] + nv[2] * dd + ov[2] ) )
		grid.append( row )
	tris = []
	for r in range( n ):
		for col in range( n ):
			a, b, cc, dd = grid[r][col], grid[r + 1][col], grid[r + 1][col + 1], grid[r][col + 1]
			tris += [[a, b, cc], [a, cc, dd]]
	# wind them like the base face (outward)
	out = []
	fn = f["normal"]
	for t in tris:
		e1 = [t[1][i] - t[0][i] for i in range( 3 )]
		e2 = [t[2][i] - t[0][i] for i in range( 3 )]
		nn = ( e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] )
		ln = math.sqrt( sum( x * x for x in nn ) ) or 1.0
		nn = ( nn[0] / ln, nn[1] / ln, nn[2] / ln )
		if nn[0] * fn[0] + nn[1] * fn[1] + nn[2] * fn[2] < 0:
			t = [t[0], t[2], t[1]]
			nn = ( -nn[0], -nn[1], -nn[2] )
		out.append( ( t, nn ) )
	return out


# The CIS frigate off the starboard side: every brush (its side doors and all) and prop inside this box goes into the one
# props_11_7_2 model, so the game can hide it and warp it in whole for a GM's boarding attack (Code/Map/SwrpBoardingShip.cs).
# Angelo 2026-10-19: "include it as well... so it looks like the whole frigate comes out of hyperspace". The Venator's hull
# stays inside |x| < 6100; the Jedi temple above (z ~13900) is outside the box.
FRIGATE_CHUNK = "11_7_2"
FRIGATE_BOX = ( ( 7000.0, -16600.0, -4600.0 ), ( 14800.0, 12900.0, 5900.0 ) )


def in_frigate( verts ):
	c = [sum( p[i] for p in verts ) / len( verts ) for i in range( 3 )]
	return all( FRIGATE_BOX[0][i] <= c[i] <= FRIGATE_BOX[1][i] for i in range( 3 ) )


def gather_frigate( render, phys, chunks, chunk_phys ):
	out = chunks.setdefault( FRIGATE_CHUNK, [] )
	out_phys = chunk_phys.setdefault( FRIGATE_CHUNK, [] )
	moved = { "brush faces": 0, "brush collision": 0, "prop faces": 0, "prop collision": 0 }
	keep = []
	for f in render:
		if in_frigate( f[1] ):
			out.append( f ); moved["brush faces"] += 1
		else:
			keep.append( f )
	keep_phys = []
	for f in phys:
		if in_frigate( f[1] ):
			out_phys.append( ( f[1], f[2] ) ); moved["brush collision"] += 1
		else:
			keep_phys.append( f )
	for cell in list( chunks ):
		if cell == FRIGATE_CHUNK:
			continue
		stay = [f for f in chunks[cell] if not in_frigate( f[1] )]
		moved["prop faces"] += len( chunks[cell] ) - len( stay )
		out.extend( f for f in chunks[cell] if in_frigate( f[1] ) )
		chunks[cell] = stay
	for cell in list( chunk_phys ):
		if cell == FRIGATE_CHUNK:
			continue
		stay = [f for f in chunk_phys[cell] if not in_frigate( f[0] )]
		moved["prop collision"] += len( chunk_phys[cell] ) - len( stay )
		out_phys.extend( f for f in chunk_phys[cell] if in_frigate( f[0] ) )
		chunk_phys[cell] = stay
	print( f"NOTORIOUS frigate: {moved}, now {len( out )} faces in props_{FRIGATE_CHUNK}" )
	return keep, keep_phys


def main():
	bpy.ops.wm.read_factory_settings( use_empty=True )
	root = vp.parse( VMF )

	render, phys = [], []
	used = {}
	matched, fallback = set(), set()
	gm_path = os.path.join( HERE, "gmod_materials.json" )
	GMOD.update( json.load( open( gm_path ) ) if os.path.exists( gm_path ) else {} )
	skipped_disp = 0
	disp_tris = 0
	for solid, xf in solids_of( root ):
		for f0 in vp.solid_faces( solid ):
			f = transform_face( f0, xf )
			parts = [( f["verts"], f["normal"] )]
			if f["disp"]:
				tris = displacement_tris( f )
				if not tris:
					skipped_disp += 1
					continue
				parts = tris
				disp_tris += len( tris )
			m = f["material"]
			if any( m.startswith( h ) for h in HIDDEN ):
				phys.append( ( "", f["verts"], f["normal"], 256 ) )
				continue
			# The VMF keeps its 3D skybox (miniature ships and planets) far above the ship; leave it out. The Jedi temple and
			# simulation rooms up there are playable (reached by teleporter) and stay.
			if regions.in_skybox( [sum( p[i] for p in f["verts"] ) / len( f["verts"] ) for i in range( 3 )] ):
				continue
			if m.startswith( "SKYBOX/" ) and m != FAKE_WHITE or m in NON_SOLID and m not in NO_DRAW:
				continue
			if m in ( "TOOLS/TOOLSSKYBOX", "TOOLS/TOOLSTRIGGER", "TOOLS/TOOLSINVISIBLELADDER", "TOOLS/TOOLSBLOCKLIGHT", "TOOLS/TOOLSSKIP", "TOOLS/TOOLSHINT" ):
				continue

			if not xf or xf.get( "collide", True ):
				for ( pv, pn ) in parts:
					phys.append( ( "", pv, pn, 256 ) )
			if xf and not xf.get( "render", True ):
				continue
			if xf and xf.get( "shield" ) and m == "TOOLS/TOOLSNODRAW":
				continue         # a shield's edges stay open
			if m == "TOOLS/TOOLSNODRAW" and not fill_ok( f ):
				continue
			m = ALIAS.get( m, m )
			if is_tool( m ):
				continue
			special = special_material( m ) if not ( xf and xf.get( "shield" ) ) else None
			if special:
				for ( pv, pn ) in parts:
					render.append( ( f"materials/notorious/{special}.vmat", pv, pn, 64 ) )
				continue
			key = slug( m )
			if f0["material"] == "TOOLS/TOOLSNODRAW":
				key = "nodraw_fill"      # same texture as its alias, but kept apart so light strips skip it
				src = os.path.join( MAT_DIR, slug( m ) + ".vmat" )
				if key not in used and os.path.exists( src ):
					with open( os.path.join( MAT_DIR, key + ".vmat" ), "w", newline="\n" ) as fh:
						fh.write( open( src, encoding="utf-8" ).read() )
			info = GMOD.get( m )
			if info and info.get( "matched" ) and f.get( "uaxis" ) and f.get( "vaxis" ):
				# the real Garry's Mod texture (converted by gmod_textures.py), mapped exactly as the VMF had it
				scale = { "u": f["uaxis"], "v": f["vaxis"], "w": info["width"], "h": info["height"] }
				if key not in used:
					used[key] = m
					matched.add( key )
			else:
				tex, tint, emissive, opacity, scale = classify( m )
				if key not in used:
					write_vmat( key, tex, tint, emissive, opacity )
					used[key] = m
					fallback.add( m )
			if xf and xf.get( "shield" ):
				key = shield_material( key )
			for ( pv, pn ) in parts:
				render.append( ( f"materials/notorious/{key}.vmat", pv, pn, scale ) )

	def door_face( f ):
		m = ALIAS.get( f["material"], f["material"] )
		if is_tool( m ) or any( m.startswith( h ) for h in HIDDEN ):
			return None
		key = slug( m )
		info = GMOD.get( m )
		if info and info.get( "matched" ) and f.get( "uaxis" ) and f.get( "vaxis" ):
			if key not in used:
				used[key] = m
				matched.add( key )
			return f"materials/notorious/{key}.vmat", { "u": f["uaxis"], "v": f["vaxis"], "w": info["width"], "h": info["height"] }
		tex, tint, emissive, opacity, scale = classify( m )
		if key not in used:
			write_vmat( key, tex, tint, emissive, opacity )
			used[key] = m
			fallback.add( m )
		return f"materials/notorious/{key}.vmat", scale

	world_solids = list( vp.find( vp.find( root, "world" )[0][2], "solid" ) )
	for e in vp.find( root, "entity" ):
		if e[1].get( "classname" ) == "func_detail":
			world_solids += vp.find( e[2], "solid" )
	doors, door_console, door_cells = nd.export( root, world_solids, door_face, uv_for )
	lifts, teleports = nt.export( root, os.path.join( PROJECT, "Assets", "data", "notorious_transit.json" ) )
	print( f"NOTORIOUS transit: {len( lifts )} turbolifts ({sum( len( l['floors'] ) for l in lifts )} decks), {len( teleports )} teleporters" )
	holo = nh.export( root, os.path.join( PROJECT, "Assets", "data", "notorious_holo.json" ) )
	print( f"NOTORIOUS holo-map: {len( holo )} deck plan rectangles" )
	print( f"NOTORIOUS doors: {len( doors )} working doors, {sum( len( d['panels'] ) for d in doors )} panels, console {'found' if door_console else 'MISSING'}, {len( door_cells )} brig cells" )

	# ---- props: baked into spatial chunks (render only); a box each in the collision mesh
	def prop_material( name ):
		special = special_material( name )
		if special:
			return f"materials/notorious/{special}.vmat"
		if "hirez_venator_exterior" in name.lower():
			name = "bananakin/2003/fm_light_hull"      # the bridge roof's missing hull textures
		key = slug( name )
		info = GMOD.get( name.upper() )
		if not ( info and info.get( "matched" ) ) and key not in used:
			write_vmat( key, None, ( 0.55, 0.56, 0.6 ), None, None )
		used[key] = name
		return f"materials/notorious/{key}.vmat"

	prop_chunks, prop_phys, prop_stats = npr.build( root, prop_material )
	print( f"NOTORIOUS props: {prop_stats}" )
	render, phys = gather_frigate( render, phys, prop_chunks, prop_phys )

	# the corridors, rebuilt with the Venator corridor kit (notorious_corridors.py); before the strips, so the corridors it
	# restyles get the kit's ceiling lights instead
	# Off since 2026-10-12: Angelo wants the map's original corridors (CORRIDOR_KIT = True lays the kit again).
	corridor_lights = []
	if CORRIDOR_KIT:
		render, corridor_lights = ncor.apply( render, phys, holo, build_object, PROP_VMDL, PROP_PHYS, REV, MODEL_DIR, PROJECT,
			os.path.join( HERE, "__pycache__", "corridor_kit" ) )
	else:
		with open( os.path.join( PROJECT, "Assets", "data", "notorious_corridors.json" ), "w", newline="\n" ) as fh:
			fh.write( "[]\n" )

	strips = ceiling_strips( render )
	render.extend( strips )
	write_vmat( "strip_white", None, ( 1.0, 0.95, 0.86 ), ( 1.0, 0.93, 0.82 ), None )   # warm white strips
	spill = blue_spill( render )
	print( f"NOTORIOUS: {len( strips )} ceiling light strips, {len( spill )} blue spill lights" )
	print( f"NOTORIOUS: displacement triangles {disp_tris}, displacements skipped {skipped_disp}" )
	print( f"NOTORIOUS textures: {len( matched )} real Garry's Mod materials, {len( fallback )} tinted fallbacks: {sorted( fallback )}" )
	write_vmat( "phys", None, ( 0.5, 0.5, 0.5 ), None, None )
	print( f"NOTORIOUS: render faces {len( render )}, collision faces {len( phys )}, materials {len( used )}, skipped displacement faces {skipped_disp}" )
	xs = [v[0] for ( _, vs, _, _ ) in render for v in vs]
	ys = [v[1] for ( _, vs, _, _ ) in render for v in vs]
	zs = [v[2] for ( _, vs, _, _ ) in render for v in vs]
	print( "NOTORIOUS bounds", min( xs ), min( ys ), min( zs ), max( xs ), max( ys ), max( zs ) )
	export( render, phys )
	export_props( prop_chunks, prop_phys )

	# ---- entities: spawns, clustered lights, jail cells
	ents = vp.find( root, "entity" )
	spawns = []
	for e in ents:
		if e[1].get( "classname" ) == "info_player_start":
			o = [float( x ) for x in e[1]["origin"].split()]
			yaw = float( e[1].get( "angles", "0 0 0" ).split()[1] )
			spawns.append( { "x": o[0], "y": o[1], "z": o[2], "yaw": yaw } )

	cells = {}
	for e in ents:
		tn = e[1].get( "targetname", "" )
		m = re.match( r"cell_(\d+)_sprite$", tn )
		if m and e[1].get( "classname" ) == "env_sprite":
			o = [float( x ) for x in e[1]["origin"].split()]
			cells[int( m.group( 1 ) )] = { "x": o[0], "y": o[1], "z": o[2] }

	grid = {}
	for e in ents:
		if e[1].get( "classname" ) not in ( "light", "light_spot" ):
			continue
		o = [float( x ) for x in e[1]["origin"].split()]
		if regions.in_skybox( o ):
			continue
		parts = [float( x ) for x in e[1].get( "_light", "255 255 255 200" ).split()]
		r, g, b = parts[0] / 255, parts[1] / 255, parts[2] / 255
		br = parts[3] if len( parts ) > 3 else 200.0
		key = ( int( o[0] // 320 ), int( o[1] // 320 ), int( o[2] // 200 ) )
		c = grid.setdefault( key, { "x": 0, "y": 0, "z": 0, "r": 0, "g": 0, "b": 0, "w": 0, "n": 0 } )
		w = max( 1.0, br )
		c["x"] += o[0] * w; c["y"] += o[1] * w; c["z"] += o[2] * w
		c["r"] += r * w; c["g"] += g * w; c["b"] += b * w; c["w"] += w; c["n"] += 1
	lights = []
	for c in sorted( grid.values(), key=lambda c: -c["w"] )[:340]:
		w = c["w"]
		lights.append( {
			"x": c["x"] / w, "y": c["y"] / w, "z": c["z"] / w,
			"r": c["r"] / w, "g": c["g"] / w, "b": c["b"] / w,
			"energy": min( 3.0, 0.9 + 0.35 * math.log( c["n"] + 1 ) + w / c["n"] / 250 ),
		} )

	with open( os.path.join( HERE, "notorious_entities.json" ), "w" ) as fh:
		json.dump( { "spawns": spawns, "lights": lights, "cells": door_cells or [cells[k] for k in sorted( cells )], "spill": spill, "strip_lights": STRIP_LIGHTS[:260],
			"corridor_lights": [{ "x": p[0], "y": p[1], "z": p[2] } for p in corridor_lights] }, fh, indent=1 )
	print( f"NOTORIOUS entities: spawns {len( spawns )}, lights {len( lights )} (from {sum( c['n'] for c in grid.values() )}), cells {len( cells )}" )


main()
