"""
Offline look at the built map: imports the map model, its prop chunks and the corridor kit chunks near a point, textures them
from their vmats (colour, normal, roughness, metalness, glow), lights the room with the scene's lights, and renders.

    blender -b --factory-startup -P MapSource/preview_render.py -- <out.png> <x y z> <yaw> [pitch] [radius]

Positions are map units; yaw 0 looks along +X, pitch positive looks down (like swrp_cam). Only geometry within radius
(default 2500) of the camera is loaded, so a view takes a minute rather than the whole ship.
"""
import os
import sys
import json
import math
import re

import bpy
from mathutils import Vector, Euler

HERE = os.path.dirname( os.path.abspath( __file__ ) )
PROJECT = os.path.dirname( HERE )
ASSETS = os.path.join( PROJECT, "Assets" )

argv = sys.argv[sys.argv.index( "--" ) + 1:]
out = argv[0]
cx, cy, cz = ( float( v ) for v in argv[1:4] )
yaw = float( argv[4] )
pitch = float( argv[5] ) if len( argv ) > 5 else 0.0
radius = float( argv[6] ) if len( argv ) > 6 else 2500.0
cam_pos = Vector( ( cx, cy, cz ) )

bpy.ops.wm.read_factory_settings( use_empty=True )


def fbx_of( vmdl ):
	text = open( os.path.join( ASSETS, vmdl ), encoding="utf-8" ).read()
	m = re.search( r'filename = "([^"]+\.fbx)"', text )
	return os.path.join( ASSETS, m.group( 1 ) ) if m else None


def import_near( vmdl ):
	f = fbx_of( vmdl )
	if not f or not os.path.exists( f ):
		return
	before = set( bpy.data.objects )
	bpy.ops.import_scene.fbx( filepath=f )
	for o in set( bpy.data.objects ) - before:
		if o.type != "MESH":
			bpy.data.objects.remove( o, do_unlink=True )
			continue
		# the vmdl rotates -90 about Y with scale 0.01: undo the importer's own to get map units back
		o.rotation_euler = ( 0, 0, 0 )
		o.scale = ( 1, 1, 1 )
		o.location = ( 0, 0, 0 )
		me = o.data
		# drop faces far from the camera
		import bmesh
		bm = bmesh.new()
		bm.from_mesh( me )
		far = [f for f in bm.faces if ( f.calc_center_median() - cam_pos ).length > radius]
		bmesh.ops.delete( bm, geom=far, context="FACES" )
		bm.to_mesh( me )
		bm.free()
		if not me.polygons:
			bpy.data.objects.remove( o, do_unlink=True )


import_near( "models/notorious/notorious.vmdl" )
for lst in ( "notorious_props.json", "notorious_corridors.json" ):
	p = os.path.join( ASSETS, "data", lst )
	if os.path.exists( p ):
		for m in json.load( open( p ) ):
			import_near( m )

# FBX import gives Blender units as cm*0.01 etc; measure what we got and rescale to map units by the map bounds
objs = [o for o in bpy.data.objects if o.type == "MESH"]
print( "PREVIEW objects", len( objs ) )

_tex_cache = {}


def image( rel, color ):
	path = os.path.join( ASSETS, rel )
	if not os.path.exists( path ):
		return None
	key = ( path, color )
	if key not in _tex_cache:
		im = bpy.data.images.load( path, check_existing=True )
		if not color:
			im.colorspace_settings.name = "Non-Color"
		_tex_cache[key] = im
	return _tex_cache[key]


def setup( mat ):
	name = mat.name.split( ".vmat" )[0] + ".vmat"
	vp = os.path.join( ASSETS, name )
	if not os.path.exists( vp ):
		return
	text = open( vp, encoding="utf-8" ).read()
	def val( k ):
		m = re.search( r'\b' + k + r'\s+"([^"]+)"', text )
		return m.group( 1 ) if m else None
	mat.use_nodes = True
	nt = mat.node_tree
	b = nt.nodes["Principled BSDF"]
	tint = val( "g_vColorTint" )
	if val( "TextureColor" ) and ( im := image( val( "TextureColor" ), True ) ):
		t = nt.nodes.new( "ShaderNodeTexImage" ); t.image = im
		if tint:
			c = [float( x ) for x in tint.strip( "[]" ).split()[:3]]
			mix = nt.nodes.new( "ShaderNodeMix" ); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"; mix.inputs[0].default_value = 1.0
			nt.links.new( t.outputs[0], mix.inputs[6] ); mix.inputs[7].default_value = ( *c, 1 )
			nt.links.new( mix.outputs[2], b.inputs["Base Color"] )
		else:
			nt.links.new( t.outputs[0], b.inputs["Base Color"] )
	elif tint:
		b.inputs["Base Color"].default_value = ( *[float( x ) for x in tint.strip( "[]" ).split()[:3]], 1 )
	if val( "TextureRoughness" ) and ( im := image( val( "TextureRoughness" ), False ) ) and "default" not in val( "TextureRoughness" ):
		t = nt.nodes.new( "ShaderNodeTexImage" ); t.image = im
		nt.links.new( t.outputs[0], b.inputs["Roughness"] )
	elif val( "g_flRoughnessScaleFactor" ):
		b.inputs["Roughness"].default_value = float( val( "g_flRoughnessScaleFactor" ) )
	if val( "TextureMetalness" ) and ( im := image( val( "TextureMetalness" ), False ) ):
		t = nt.nodes.new( "ShaderNodeTexImage" ); t.image = im
		nt.links.new( t.outputs[0], b.inputs["Metallic"] )
	elif val( "g_flMetalness" ):
		b.inputs["Metallic"].default_value = float( val( "g_flMetalness" ) )
	if val( "TextureNormal" ) and "default" not in val( "TextureNormal" ) and ( im := image( val( "TextureNormal" ), False ) ):
		t = nt.nodes.new( "ShaderNodeTexImage" ); t.image = im
		n = nt.nodes.new( "ShaderNodeNormalMap" )
		nt.links.new( t.outputs[0], n.inputs[1] ); nt.links.new( n.outputs[0], b.inputs["Normal"] )
	if "F_SELF_ILLUM 1" in text:
		st = val( "g_vSelfIllumTint" )
		c = [float( x ) for x in st.strip( "[]" ).split()[:3]] if st else [1, 1, 1]
		mask = val( "TextureSelfIllumMask" )
		if mask and not mask.startswith( "[" ) and ( im := image( mask, True ) ):
			t = nt.nodes.new( "ShaderNodeTexImage" ); t.image = im
			nt.links.new( t.outputs[0], b.inputs["Emission Color"] )
		else:
			b.inputs["Emission Color"].default_value = ( *c, 1 )
		b.inputs["Emission Strength"].default_value = float( val( "g_flSelfIllumBrightness" ) or 3 ) * 1.5
	if "F_TRANSLUCENT 1" in text:
		b.inputs["Alpha"].default_value = float( val( "g_flOpacityScale" ) or 0.3 )


for m in bpy.data.materials:
	setup( m )

# lights: the scene's (from notorious_entities.json), near the camera
ent = json.load( open( os.path.join( HERE, "notorious_entities.json" ) ) )
nl = 0
for group, energy, col in ( ( "lights", 1.0, None ), ( "strip_lights", 1.0, ( 1, 0.92, 0.8 ) ), ( "corridor_lights", 1.2, ( 0.92, 0.96, 1 ) ) ):
	for l in ent.get( group, [] ):
		p = Vector( ( l["x"], l["y"], l["z"] ) )
		if ( p - cam_pos ).length > radius * 0.8:
			continue
		ld = bpy.data.lights.new( "L", "POINT" )
		ld.energy = 2.5e5 * energy * l.get( "energy", 1.0 )
		ld.shadow_soft_size = 8
		ld.use_shadow = False
		ld.color = col or ( min( 1, l.get( "r", 1 ) * 1.2 ), min( 1, l.get( "g", 1 ) * 1.2 ), min( 1, l.get( "b", 1 ) * 1.2 ) )
		ob = bpy.data.objects.new( "L", ld )
		ob.location = p
		bpy.context.scene.collection.objects.link( ob )
		nl += 1
print( "PREVIEW lights", nl )

sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x, sc.render.resolution_y = 1280, 720
w = bpy.data.worlds.new( "w" ); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[1].default_value = 0.02
cam = bpy.data.objects.new( "cam", bpy.data.cameras.new( "cam" ) )
sc.collection.objects.link( cam )
sc.camera = cam
cam.data.lens = 16
cam.data.clip_start = 1
cam.data.clip_end = 20000
cam.location = cam_pos
cam.rotation_euler = Euler( ( math.radians( 90 - pitch ), 0, math.radians( yaw - 90 ) ), "XYZ" )
sc.view_settings.view_transform = "AgX"
sc.view_settings.exposure = 0.0
sc.render.filepath = out
bpy.ops.render.render( write_still=True )
print( "PREVIEW saved", out )
