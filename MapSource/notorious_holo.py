"""
The bridge holo-map's deck plan: every floor a trooper can stand on in the ship, as merged rectangles on a 64-unit grid,
written to Assets/data/notorious_holo.json for SwrpHoloMap (which builds a glowing see-through model from it at runtime).

A floor is the up-facing face of a world or func_detail brush with a real material (no tool textures), open space above it
and a ceiling somewhere overhead (so the hull's outer roof doesn't count). Plain Python, no bpy; notorious_build.py calls it.

    import notorious_holo as nh; nh.export( root, out_json )
"""
import json
import math
import vmf_parse as vp
import regions
import notorious_doors as nd

CELL = 64.0
ZSTEP = 16.0


def _floor_faces( root ):
	solids = list( vp.find( vp.find( root, "world" )[0][2], "solid" ) )
	for e in vp.find( root, "entity" ):
		if e[1].get( "classname" ) == "func_detail":
			solids += vp.find( e[2], "solid" )
	ship = [so for so in solids if _in_ship( so )]
	return ship, solids


def _in_ship( so ):
	faces = vp.solid_faces( so )
	if not faces:
		return False
	pts = [p for f in faces for p in f["verts"]]
	c = [sum( p[i] for p in pts ) / len( pts ) for i in range( 3 )]
	return regions.inside( c, regions.SHIP )


def _inside_poly( x, y, poly ):
	inside = False
	n = len( poly )
	for i in range( n ):
		x1, y1 = poly[i][0], poly[i][1]
		x2, y2 = poly[( i + 1 ) % n][0], poly[( i + 1 ) % n][1]
		if ( y1 > y ) != ( y2 > y ) and x < ( x2 - x1 ) * ( y - y1 ) / ( y2 - y1 ) + x1:
			inside = not inside
	return inside


def cells( root ):
	ship, solids = _floor_faces( root )
	# ceilings can be doors and movers too (the hangar's dorsal roof is a func_door)
	movers = [so for e in vp.find( root, "entity" ) if e[1].get( "classname" ) in ( "func_door", "func_brush", "func_movelinear", "func_door_rotating" )
		for so in vp.find( e[2], "solid" )]
	index = nd.SolidIndex( ship + [so for so in movers if _in_ship( so )] )
	grid = set()
	for so in ship:
		for f in vp.solid_faces( so ):
			if f["disp"] or f["normal"][2] < 0.9 or f["material"].startswith( "TOOLS/" ):
				continue
			vs = f["verts"]
			z = sum( p[2] for p in vs ) / len( vs )
			xs = [p[0] for p in vs]
			ys = [p[1] for p in vs]
			if max( xs ) - min( xs ) < 24 and max( ys ) - min( ys ) < 24:
				continue                       # trim, handrails and such
			zq = round( z / ZSTEP ) * ZSTEP
			for ix in range( int( math.floor( min( xs ) / CELL ) ), int( math.ceil( max( xs ) / CELL ) ) ):
				for iy in range( int( math.floor( min( ys ) / CELL ) ), int( math.ceil( max( ys ) / CELL ) ) ):
					px, py = ( ix + 0.5 ) * CELL, ( iy + 0.5 ) * CELL
					if ( ix, iy, zq ) in grid or not _inside_poly( px, py, vs ):
						continue
					# room to stand, and a ceiling within reach overhead (the hull's outer roof has none)
					if index.inside( ( px, py, z + 40 ) ):
						continue
					if not any( index.inside( ( px, py, z + h ) ) for h in range( 96, 2600, 48 ) ):
						continue
					grid.add( ( ix, iy, zq ) )
	return grid


def rects( grid ):
	"""Greedy merge of each height layer into rectangles: [x0, y0, x1, y1, z]."""
	out = []
	layers = {}
	for ix, iy, z in grid:
		layers.setdefault( z, set() ).add( ( ix, iy ) )
	for z, cs in sorted( layers.items() ):
		left = set( cs )
		for ix, iy in sorted( cs ):
			if ( ix, iy ) not in left:
				continue
			w = 1
			while ( ix + w, iy ) in left:
				w += 1
			h = 1
			while all( ( ix + k, iy + h ) in left for k in range( w ) ):
				h += 1
			for a in range( w ):
				for b in range( h ):
					left.discard( ( ix + a, iy + b ) )
			out.append( [ix * CELL, iy * CELL, ( ix + w ) * CELL, ( iy + h ) * CELL, z] )
	return out


def export( root, path ):
	r = rects( cells( root ) )
	lo = [min( x[0] for x in r ), min( x[1] for x in r ), min( x[4] for x in r )]
	hi = [max( x[2] for x in r ), max( x[3] for x in r ), max( x[4] for x in r )]
	with open( path, "w", newline="\n" ) as fh:
		json.dump( { "cell": CELL, "lo": lo, "hi": hi, "rects": r }, fh, separators=( ",", ":" ) )
	return r


if __name__ == "__main__":
	import os
	import time
	t = time.time()
	here = os.path.dirname( os.path.abspath( __file__ ) )
	root = vp.parse( r"X:\SWRP Projects\Notorious_VMF\rp_venator_notorious_os.vmf" )
	r = export( root, os.path.join( os.path.dirname( here ), "Assets", "data", "notorious_holo.json" ) )
	zs = sorted( { x[4] for x in r } )
	print( f"holo-map: {len( r )} rectangles on {len( zs )} heights in {time.time() - t:.0f} s" )
