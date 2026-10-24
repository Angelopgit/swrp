"""
The Notorious map's props (consoles, chairs, light panels, pipes, the hull exterior...) converted from the addon's Source
models (see source_mdl.py) and placed where the VMF puts them. Used by notorious_build.py and gmod_textures.py.

Props are baked, already transformed, into a few spatial chunks so the engine can cull what you can't see. Each solid
prop adds its real collision shape (the convex pieces from its .phy, as in Garry's Mod) to its chunk's own collision mesh,
so consoles block you, archways, pipes and statues don't block more than they did there, and a chunk that fails to load
takes its collision with it (no invisible walls).
"""
import os
import math

import vmf_parse as vp
import regions
from source_mdl import load_model, load_phy, material_candidates

ADDON = r"X:\SteamLibrary\steamapps\workshop\content\4000\3466442938\gmpublisher"
CHUNK = 3072.0

_models = {}
_phys = {}


def instances( root ):
	"""Every prop in the playable ship: (model path, origin, angles, skin, scale, solid)."""
	out = []
	for e in vp.find( root, "entity" ):
		cls = e[1].get( "classname", "" )
		model = e[1].get( "model", "" ).lower()
		if not cls.startswith( "prop_" ) or not model.endswith( ".mdl" ):
			continue
		o = tuple( float( x ) for x in e[1].get( "origin", "0 0 0" ).split() )
		if regions.in_skybox( o ):
			continue
		a = tuple( float( x ) for x in e[1].get( "angles", "0 0 0" ).split() )
		skin = int( float( e[1].get( "skin", "0" ) or 0 ) )
		scale = float( e[1].get( "modelscale", "1" ) or 1 )
		solid = e[1].get( "solid", "6" ) != "0" and cls != "prop_vehicle_prisoner_pod"
		out.append( ( model, o, a, skin, scale, solid ) )
	return out + bridge_dressing()


CHAIR = "models/kingpommes/starwars/misc/imp_chaira.mdl"
HOLOTABLE = "models/halves/props/goldievenator/holotablesmall.mdl"
WALL_SCREEN = "models/bananakin/rp_venator_roadside_v1_3_3/fleetrom_5.mdl"


def bridge_dressing():
	"""
	Extra crew furniture for the two bridges, which the map left bare: a chair at every crew-pit station, a holo-table on the
	command deck behind the pits and a wall screen either side of it. Laid out for the port bridge (walkway at x = -659,
	pits x -885..-732 and -586..-433 with their floor at z 3156, deck z 3229) and mirrored for the starboard one.
	"""
	port = []
	for y in ( 3520.0, 3620.0, 3720.0, 3820.0, 3920.0 ):
		port.append( ( CHAIR, ( -470.0, y, 3156.0 ), 0.0 ) )        # inner pit, facing its console wall
		port.append( ( CHAIR, ( -850.0, y, 3156.0 ), 180.0 ) )      # outer pit
	port.append( ( HOLOTABLE, ( -659.0, 4170.0, 3229.0 ), 0.0 ) )
	port.append( ( WALL_SCREEN, ( -374.0, 4270.0, 3289.0 ), 180.0 ) )
	port.append( ( WALL_SCREEN, ( -944.0, 4270.0, 3289.0 ), 0.0 ) )
	out = []
	for path, ( x, y, z ), yaw in port:
		out.append( ( path, ( x, y, z ), ( 0.0, yaw, 0.0 ), 0, 1.0, True ) )
		out.append( ( path, ( -x, y, z ), ( 0.0, ( 180.0 - yaw ) % 360.0, 0.0 ), 0, 1.0, True ) )
	return out


def model( path, skin=0 ):
	key = ( path, skin )
	if key not in _models:
		try:
			_models[key] = load_model( os.path.join( ADDON, *path.split( "/" ) ), skin )
		except Exception as ex:
			print( "PROP failed", path, ex )
			_models[key] = None
	return _models[key]


def resolve_material( mdl, tex ):
	"""The material name (lower case, relative to materials/) the addon actually has for this texture slot."""
	cands = material_candidates( mdl, tex )
	for c in cands:
		if os.path.exists( os.path.join( ADDON, "materials", *( c + ".vmt" ).split( "/" ) ) ):
			return c
	for c in cands:
		if os.path.exists( os.path.join( ADDON, "materials", *( c + ".vtf" ).split( "/" ) ) ):
			return c
	return cands[0] if cands else "missing"


def used_materials( root ):
	"""material name -> triangle count, for gmod_textures.py."""
	used = {}
	for path, o, a, skin, scale, solid in instances( root ):
		m = model( path, skin )
		if m is None:
			continue
		for tex in set( t[0] for t in m.tris ):
			name = resolve_material( m, tex )
			used[name] = used.get( name, 0 ) + 1
	return used


def _matrix( angles, scale ):
	"""Source angles (pitch yaw roll) to a 3x3 matrix, Rz(yaw) * Ry(pitch) * Rx(roll), times the model scale."""
	p, y, r = [math.radians( v ) for v in angles]
	cp, sp, cy, sy, cr, sr = math.cos( p ), math.sin( p ), math.cos( y ), math.sin( y ), math.cos( r ), math.sin( r )
	return (
		( cy * cp * scale, ( cy * sp * sr - sy * cr ) * scale, ( cy * sp * cr + sy * sr ) * scale ),
		( sy * cp * scale, ( sy * sp * sr + cy * cr ) * scale, ( sy * sp * cr - cy * sr ) * scale ),
		( -sp * scale, cp * sr * scale, cp * cr * scale ),
	)


def _apply( m, o, v ):
	return (
		m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2] + o[0],
		m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2] + o[1],
		m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2] + o[2],
	)


def _rot( m, v ):
	x = m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2]
	y = m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2]
	z = m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2]
	l = math.sqrt( x * x + y * y + z * z ) or 1.0
	return ( x / l, y / l, z / l )


def build( root, material_path ):
	"""
	material_path(name) -> vmat path for a prop material.
	Returns ( { chunk name: [ (vmat, verts, normal, ("uv", uvs)) ] }, { chunk name: [ collision triangles (verts, normal) ] }, stats ).
	"""
	chunks = {}
	boxes = {}
	count = tris = fallback = 0
	for path, o, a, skin, scale, solid in instances( root ):
		m = model( path, skin )
		if m is None or not m.tris:
			continue
		count += 1
		rot = _matrix( a, scale )
		cell = f"{int( math.floor( o[0] / CHUNK ) ) + 8}_{int( math.floor( o[1] / CHUNK ) ) + 8}_{int( math.floor( o[2] / CHUNK ) ) + 4}"
		out = chunks.setdefault( cell, [] )
		mats = {}
		for tex, ps, ns, uvs in m.tris:
			if tex not in mats:
				mats[tex] = material_path( resolve_material( m, tex ) )
			w = [_apply( rot, o, p ) for p in ps]
			# Source winds triangles clockwise; flip them to counter-clockwise like the brushes.
			w = [w[0], w[2], w[1]]
			uv = [uvs[0], uvs[2], uvs[1]]
			e1 = ( w[1][0] - w[0][0], w[1][1] - w[0][1], w[1][2] - w[0][2] )
			e2 = ( w[2][0] - w[0][0], w[2][1] - w[0][1], w[2][2] - w[0][2] )
			n = ( e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] )
			ln = math.sqrt( n[0] ** 2 + n[1] ** 2 + n[2] ** 2 )
			if ln < 1e-6:
				continue
			out.append( ( mats[tex], w, ( n[0] / ln, n[1] / ln, n[2] / ln ), ( "uv", uv ) ) )
			tris += 1

		if solid:
			shape = _phy_tris( path, rot, o )
			if shape is None:
				shape = _hull_box( m, rot, o )
				fallback += 1
			boxes.setdefault( cell, [] ).extend( shape )

	return chunks, boxes, { "props": count, "triangles": tris, "chunks": len( chunks ), "collision triangles": sum( len( b ) for b in boxes.values() ), "box fallbacks": fallback }


def _phy_tris( path, rot, o ):
	"""The prop's .phy convex pieces placed in the world, each triangle wound to face out of its piece. A model without a .phy
	was never solid in Source (weeds, banners, fish), so it gets none here either ([]); None when the .phy can't be read."""
	if path not in _phys:
		f = os.path.join( ADDON, *path.split( "/" ) )[:-4] + ".phy"
		try:
			_phys[path] = load_phy( f ) if os.path.exists( f ) else []
		except Exception as ex:
			print( "PROP phy failed", path, ex )
			_phys[path] = None
	pieces = _phys[path]
	if pieces is None:
		return None
	out = []
	for piece in pieces:
		w = [[_apply( rot, o, p ) for p in t] for t in piece]
		pts = [p for t in w for p in t]
		c = tuple( sum( p[i] for p in pts ) / len( pts ) for i in range( 3 ) )
		for v in w:
			e1 = [v[1][k] - v[0][k] for k in range( 3 )]
			e2 = [v[2][k] - v[0][k] for k in range( 3 )]
			n = ( e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] )
			ln = math.sqrt( sum( x * x for x in n ) )
			if ln < 1e-6:
				continue
			n = ( n[0] / ln, n[1] / ln, n[2] / ln )
			if sum( n[k] * ( v[0][k] - c[k] ) for k in range( 3 ) ) < 0:
				v = [v[0], v[2], v[1]]
				n = ( -n[0], -n[1], -n[2] )
			out.append( ( v, n ) )
	return out


def _hull_box( m, rot, o ):
	"""Six quads around the model's triangles' bounds (tight, unlike the MDL hull), skipping flat or tiny props."""
	if not hasattr( m, "bounds" ):
		xs = [p[0] for _, ps, _, _ in m.tris for p in ps]
		ys = [p[1] for _, ps, _, _ in m.tris for p in ps]
		zs = [p[2] for _, ps, _, _ in m.tris for p in ps]
		m.bounds = ( ( min( xs ), min( ys ), min( zs ) ), ( max( xs ), max( ys ), max( zs ) ) )
	lo, hi = m.bounds
	size = sorted( hi[i] - lo[i] for i in range( 3 ) )
	if size[0] < 2.0 or size[2] < 12.0 or size[2] > 1500.0:
		return []          # decals, wall plates, and huge hull pieces (those are brush-backed already)
	c = [( x, y, z ) for x in ( lo[0], hi[0] ) for y in ( lo[1], hi[1] ) for z in ( lo[2], hi[2] )]
	w = [_apply( rot, o, p ) for p in c]
	faces = [( 0, 1, 3, 2 ), ( 4, 6, 7, 5 ), ( 0, 4, 5, 1 ), ( 2, 3, 7, 6 ), ( 0, 2, 6, 4 ), ( 1, 5, 7, 3 )]
	out = []
	for f in faces:
		v = [w[i] for i in f]
		e1 = [v[1][k] - v[0][k] for k in range( 3 )]
		e2 = [v[2][k] - v[0][k] for k in range( 3 )]
		n = ( e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0] )
		ln = math.sqrt( sum( x * x for x in n ) ) or 1.0
		out.append( ( v, ( n[0] / ln, n[1] / ln, n[2] / ln ) ) )
	return out
