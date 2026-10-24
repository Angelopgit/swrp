"""
Tatooine's ground, authored offline (replaces the runtime noise heightmap of 2026-10-20). Modelled on the Tunisian
locations the films used, from published field descriptions (not data files): the Ong Jemel basin near Tozeur (Mos Espa:
a flat, pale hardpan floor under an eroded clay-sandstone escarpment with gullies and talus aprons), and the Grand Erg
Oriental's dune forms (crescent barchans a few metres to ~12 m high with ~32 degree slip faces on the lee side and horns
pointing downwind; long linear dunes parallel to the wind further out).

    py MapSource/tatooine_terrain.py [--preview]

Layout (metres from the landing field; the wind blows towards +x, TatooineWeather.Direction):
  landing field (0, 0) and the town just south of it (0, -230) on the basin floor; the escarpment runs north-south on the
  west side with eroded gullies; a barchan field to the east; linear dunes to the north; the event site (a hollow ringed
  by rock outcrops, open to the south-west) at (430, 470); a rim of eroded mountains hides the world's edge.

Writes Assets/data/tatooine/terrain.bin (uint16 heights then uint32 control, both 2048 x 2048, row = y, column = x),
terrain.json (sizes) and props.json (rocks, town and event-site pieces with ground heights), which
Code/Map/Tatooine/TatooinePlanet.cs loads. --preview also renders preview.png (hillshade + material colours).
"""
import json
import math
import os
import struct
import sys
import time

import numpy as np

HERE = os.path.dirname( os.path.abspath( __file__ ) )
OUT = os.path.join( os.path.dirname( HERE ), "Assets", "data", "tatooine" )

N = 2048                 # final heightmap resolution
W = 1024                 # working resolution for shaping and erosion
SIZE = 65536.0           # units across
M = 39.37                # units per metre
RANGE = 21000.0          # height range stored (units)
BASE = 2000.0            # landing field height inside the range (units)
HALF = SIZE / M / 2      # half width in metres (~832)
WIND = np.array( [1.0, 0.22] ) / math.hypot( 1.0, 0.22 )

TOWN = ( 0.0, -230.0 )
SITE = ( 430.0, 470.0 )
rng = np.random.default_rng( 1138 )


# ----- helpers --------------------------------------------------------------------------------------------------------

def grid( n ):
	c = ( np.arange( n ) - ( n - 1 ) / 2 ) * ( 2 * HALF / ( n - 1 ) )
	return np.meshgrid( c, c )        # X[row=y][col=x], Y


def spectral( n, lo, hi, beta, seed ):
	"""Noise with power ~ 1/f^beta between lo..hi cycles per metre, normalised to -1..1."""
	r = np.random.default_rng( seed )
	f = np.fft.fftfreq( n, d=2 * HALF / n )
	fx, fy = np.meshgrid( f, f )
	fr = np.sqrt( fx * fx + fy * fy )
	fr[0, 0] = 1
	amp = fr ** ( -beta / 2 ) * ( ( fr >= lo ) & ( fr <= hi ) )
	x = np.real( np.fft.ifft2( np.fft.fft2( r.standard_normal( ( n, n ) ) ) * amp ) )
	return x / max( 1e-9, np.abs( x ).max() )


def smooth( a, b, x ):
	t = np.clip( ( x - a ) / ( b - a ), 0, 1 )
	return t * t * ( 3 - 2 * t )


def blur( h, metres ):
	f = np.fft.fftfreq( h.shape[0], d=2 * HALF / h.shape[0] )
	fx, fy = np.meshgrid( f, f )
	return np.real( np.fft.ifft2( np.fft.fft2( h ) * np.exp( -2 * ( math.pi * metres ) ** 2 * ( fx * fx + fy * fy ) ) ) )


def sample( h, x, y ):
	"""Bilinear height at metres (x, y) on grid h."""
	n = h.shape[0]
	fx = ( np.asarray( x ) / ( 2 * HALF ) + 0.5 ) * ( n - 1 )
	fy = ( np.asarray( y ) / ( 2 * HALF ) + 0.5 ) * ( n - 1 )
	i0 = np.clip( np.floor( fx ).astype( int ), 0, n - 2 )
	j0 = np.clip( np.floor( fy ).astype( int ), 0, n - 2 )
	tx, ty = np.clip( fx - i0, 0, 1 ), np.clip( fy - j0, 0, 1 )
	a = h[j0, i0] * ( 1 - tx ) + h[j0, i0 + 1] * tx
	b = h[j0 + 1, i0] * ( 1 - tx ) + h[j0 + 1, i0 + 1] * tx
	return a * ( 1 - ty ) + b * ty


def resize( h, n ):
	"""Bicubic-ish upsample by FFT zero padding would ring at cliffs: use bilinear then a light blur."""
	c = ( np.arange( n ) - ( n - 1 ) / 2 ) * ( 2 * HALF / ( n - 1 ) )
	X, Y = np.meshgrid( c, c )
	return sample( h, X, Y )


def slope_deg( h, n ):
	cell = 2 * HALF / ( n - 1 )
	gy, gx = np.gradient( h, cell )
	return np.degrees( np.arctan( np.sqrt( gx * gx + gy * gy ) ) )


# ----- shaping ----------------------------------------------------------------------------------------------------------

def barchan( X, Y, cx, cy, height, width, rot ):
	"""One crescent dune: a gentle windward back (~10 deg) rising to a crest, a steep slip face behind it, and two horns
	trailing downwind. Built in dune-local coordinates (u downwind, v across)."""
	c, s = math.cos( rot ), math.sin( rot )
	u = ( X - cx ) * c + ( Y - cy ) * s
	v = -( X - cx ) * s + ( Y - cy ) * c
	r = width / 2
	vn = np.clip( np.abs( v ) / r, 0, 1.5 )
	# the crest bends downwind towards the horns
	crest_u = r * 0.25 + r * 0.9 * vn ** 2
	back = r * 1.6
	hv = height * np.clip( 1 - vn ** 2, 0, 1 ) ** 0.8
	windward = np.clip( ( u - ( crest_u - back ) ) / back, 0, 1 ) ** 1.4
	slip = np.clip( 1 - ( u - crest_u ) / ( hv / math.tan( math.radians( 32 ) ) + 0.5 ), 0, 1 )
	return hv * np.where( u < crest_u, windward, slip ) * ( u > crest_u - back )


def shape():
	X, Y = grid( W )
	R = np.hypot( X, Y )
	edge = np.maximum( np.abs( X ), np.abs( Y ) ) * 0.6 + R * 0.4 + 70 * spectral( W, 1 / 1400, 1 / 250, 2.0, 17 )     # an organic rim, not a square

	# basin floor: almost flat, rising gently towards the dunes and the escarpment
	h = 2.0 * spectral( W, 1 / 900, 1 / 120, 2.5, 1 ) + 0.000012 * R ** 2

	# the escarpment (Ong Jemel's ridge) on the west side: a plateau ~45-70 m high with an eroded, gullied face
	line = -380 + 70 * spectral( W, 1 / 1600, 1 / 200, 2.0, 2 )
	ridge = smooth( 0, 60, line - X )
	plateau = 48 + 14 * spectral( W, 1 / 700, 1 / 60, 2.0, 3 )
	h = h + ridge * plateau
	rockmask = ridge.copy()

	# a few detached buttes in front of the ridge and the outcrops ringing the event site
	for ( bx, by, br, bh ) in ( ( -250, 320, 45, 34 ), ( -300, -520, 60, 40 ), ( -180, 560, 30, 22 ) ):
		d = np.hypot( X - bx, Y - by ) + 18 * spectral( W, 1 / 200, 1 / 20, 1.5, int( bx ) & 0xffff )
		b = smooth( br, br * 0.75, d )
		h = np.maximum( h, b * ( bh + 6 * spectral( W, 1 / 100, 1 / 15, 1.5, 9 ) ) )
		rockmask = np.maximum( rockmask, b )
	sd = np.hypot( X - SITE[0], Y - SITE[1] )
	ang = np.arctan2( Y - SITE[1], X - SITE[0] )
	open_sw = smooth( 0.55, 0.9, np.cos( ang - math.radians( -135 ) ) )      # the ring opens towards the town
	ring = smooth( 55, 75, sd ) * smooth( 135, 105, sd ) * ( 1 - open_sw )
	ring *= 0.6 + 0.4 * ( spectral( W, 1 / 120, 1 / 15, 1.2, 11 ) > -0.2 )
	h = np.maximum( h, ring * ( 16 + 8 * spectral( W, 1 / 80, 1 / 10, 1.5, 12 ) ) )
	rockmask = np.maximum( rockmask, ring )

	# barchan field to the east of the basin (scattered crescents, bigger further out)
	dunes = np.zeros_like( h )
	k = 0
	while k < 90:
		cx = rng.uniform( 140, 640 ); cy = rng.uniform( -560, 300 )
		if math.hypot( cx - SITE[0], cy - SITE[1] ) < 170: continue
		size = 0.6 + 0.6 * ( cx - 140 ) / 500
		dunes = np.maximum( dunes, barchan( X, Y, cx, cy, rng.uniform( 3, 11 ) * size, rng.uniform( 45, 110 ) * size, math.atan2( WIND[1], WIND[0] ) + rng.normal( 0, 0.12 ) ) )
		k += 1
	# linear dunes to the north: long ridges along the wind, sinuous, 12-28 m
	along = X * WIND[0] + Y * WIND[1]
	across = -X * WIND[1] + Y * WIND[0] + 40 * spectral( W, 1 / 900, 1 / 150, 2.0, 13 )
	lin = np.abs( np.sin( across / 140 * math.pi ) ) ** 0.6
	lin = ( 1 - lin ) ** 1.8 * ( 14 + 14 * ( spectral( W, 1 / 1200, 1 / 200, 2.0, 14 ) * 0.5 + 0.5 ) )
	lin *= smooth( 220, 360, Y ) * ( 1 - ridge ) * smooth( 60, 160, np.hypot( X - SITE[0], Y - SITE[1] ) )
	dunes = np.maximum( dunes, lin )
	# small dunes and drifts everywhere off the basin floor
	drift = np.maximum( 0, spectral( W, 1 / 150, 1 / 25, 1.5, 15 ) ) * 3.5
	h = h + np.maximum( dunes, drift * smooth( 120, 260, R ) ) * ( 1 - ridge * 0.9 )

	# rim mountains
	mtn = smooth( 600, 800, edge )
	peaks = 110 + 160 * ( spectral( W, 1 / 1500, 1 / 80, 2.2, 16 ) * 0.5 + 0.5 )
	h = h + mtn * peaks
	rockmask = np.maximum( rockmask, mtn )

	# flats: landing field, town, event site hollow
	for ( fx, fy, r0, r1 ) in ( ( 0, 0, 60, 120 ), ( TOWN[0], TOWN[1], 135, 200 ), ( SITE[0], SITE[1], 50, 70 ) ):
		d = np.hypot( X - fx, Y - fy )
		target = sample( blur( h, 40 ), fx, fy ) if ( fx, fy ) == SITE else 0.0
		k = smooth( r0, r1, d )
		h = h * k + target * ( 1 - k )
		rockmask *= np.maximum( k, 0 ) if ( fx, fy ) != SITE else 1
	return h, rockmask


# ----- erosion ----------------------------------------------------------------------------------------------------------

def hydraulic( h, rock, drops=160000, seed=7 ):
	"""Droplet erosion (after Hans Theobald Beyer): rain on the rock areas carves gullies and drops sediment at the foot."""
	n = h.shape[0]
	cell = 2 * HALF / ( n - 1 )
	r = np.random.default_rng( seed )
	h = h.copy()
	deposit = np.zeros_like( h )
	cand = np.argwhere( blur( rock, 15 ) > 0.3 )
	inertia, capacity_k, min_slope, erode_k, deposit_k, evap, gravity = 0.05, 8.0, 0.01, 0.5, 0.3, 0.015, 9.0
	for d in range( drops ):
		j, i = cand[r.integers( len( cand ) )]
		x, y = i + r.random(), j + r.random()
		dx = dy = 0.0
		vel, water, sed = 1.0, 1.0, 0.0
		for _ in range( 45 ):
			xi, yi = int( x ), int( y )
			if xi < 1 or yi < 1 or xi >= n - 2 or yi >= n - 2: break
			fx, fy = x - xi, y - yi
			h00, h10, h01, h11 = h[yi, xi], h[yi, xi + 1], h[yi + 1, xi], h[yi + 1, xi + 1]
			gx = ( h10 - h00 ) * ( 1 - fy ) + ( h11 - h01 ) * fy
			gy = ( h01 - h00 ) * ( 1 - fx ) + ( h11 - h10 ) * fx
			hh = h00 * ( 1 - fx ) * ( 1 - fy ) + h10 * fx * ( 1 - fy ) + h01 * ( 1 - fx ) * fy + h11 * fx * fy
			dx = dx * inertia - gx * ( 1 - inertia )
			dy = dy * inertia - gy * ( 1 - inertia )
			l = math.hypot( dx, dy )
			if l < 1e-6: break
			dx /= l; dy /= l
			nx, ny = x + dx, y + dy
			nxi, nyi = int( nx ), int( ny )
			if nxi < 0 or nyi < 0 or nxi >= n - 1 or nyi >= n - 1: break
			nfx, nfy = nx - nxi, ny - nyi
			nh = ( h[nyi, nxi] * ( 1 - nfx ) * ( 1 - nfy ) + h[nyi, nxi + 1] * nfx * ( 1 - nfy )
				+ h[nyi + 1, nxi] * ( 1 - nfx ) * nfy + h[nyi + 1, nxi + 1] * nfx * nfy )
			dh = nh - hh
			cap = max( -dh / cell, min_slope ) * vel * water * capacity_k
			if sed > cap or dh > 0:
				amt = min( dh, sed ) if dh > 0 else ( sed - cap ) * deposit_k
				sed -= amt
				w = ( ( 1 - fx ) * ( 1 - fy ), fx * ( 1 - fy ), ( 1 - fx ) * fy, fx * fy )
				for ( oy, ox ), ww in zip( ( ( 0, 0 ), ( 0, 1 ), ( 1, 0 ), ( 1, 1 ) ), w ):
					h[yi + oy, xi + ox] += amt * ww
					deposit[yi + oy, xi + ox] += amt * ww
			else:
				amt = min( ( cap - sed ) * erode_k, -dh )
				w = ( ( 1 - fx ) * ( 1 - fy ), fx * ( 1 - fy ), ( 1 - fx ) * fy, fx * fy )
				for ( oy, ox ), ww in zip( ( ( 0, 0 ), ( 0, 1 ), ( 1, 0 ), ( 1, 1 ) ), w ):
					h[yi + oy, xi + ox] -= amt * ww
					deposit[yi + oy, xi + ox] -= amt * ww
				sed += amt
			vel = math.sqrt( max( 0.0, vel * vel + dh * gravity * -1 ) ) if dh < 0 else max( 0.3, vel * 0.9 )
			water *= 1 - evap
			x, y = nx, ny
		if d % 40000 == 0: print( f"  erosion {d}/{drops}", flush=True )
	return h, deposit


def thermal( h, rock, iters=40 ):
	"""Talus: anything steeper than the angle of repose slumps (sand 33 deg, rock 52 deg), making aprons at cliff feet."""
	n = h.shape[0]
	cell = 2 * HALF / ( n - 1 )
	tal = np.tan( np.radians( 33 + 19 * np.clip( rock, 0, 1 ) ) ) * cell
	for _ in range( iters ):
		move = np.zeros_like( h )
		for sy, sx in ( ( 0, 1 ), ( 0, -1 ), ( 1, 0 ), ( -1, 0 ) ):
			nb = np.roll( np.roll( h, sy, 0 ), sx, 1 )
			d = h - nb - tal
			m = np.where( d > 0, d * 0.12, 0 )
			move -= m
			move += np.roll( np.roll( m, -sy, 0 ), -sx, 1 )
		h = h + move
	return h


# ----- materials --------------------------------------------------------------------------------------------------------

SAND, ROCK, PAN, RED = 0, 1, 2, 3


def materials( h, rock, deposit ):
	n = h.shape[0]
	X, Y = grid( n )
	slope = slope_deg( h, n )
	cell = 2 * HALF / ( n - 1 )
	gy, gx = np.gradient( h, cell )
	# rock where it's steep or bare on the ridge/rim; fresh sediment and gentle slopes are covered
	w_rock = np.clip( np.maximum( smooth( 30, 42, slope ), rock * smooth( 12, 24, slope ) ) - np.clip( deposit * 0.25, 0, 0.6 ), 0, 1 )
	# hardpan on the basin floor: flat and low
	basin = smooth( 6, 2, h ) * smooth( 8, 3, slope )
	w_pan = np.clip( basin * ( 0.7 + 0.3 * spectral( n, 1 / 300, 1 / 40, 1.5, 21 ) ), 0, 1 ) * ( 1 - w_rock )
	# deeper ochre on windward dune flanks and in big patches; pale sand on crests and lee faces
	windward = np.clip( -( gx * WIND[0] + gy * WIND[1] ) * 3, 0, 1 )
	patch = smooth( -0.1, 0.5, spectral( n, 1 / 700, 1 / 90, 2.0, 22 ) )
	w_red = np.clip( 0.55 * windward + 0.6 * patch, 0, 1 ) * ( 1 - w_rock ) * ( 1 - w_pan )
	w_sand = np.clip( 1 - w_rock - w_pan - w_red, 0, 1 )
	W4 = np.stack( [w_sand, w_rock, w_pan, w_red], -1 )
	order = np.argsort( -W4, axis=-1 )
	a, b = order[..., 0], order[..., 1]
	wa = np.take_along_axis( W4, a[..., None], -1 )[..., 0]
	wb = np.take_along_axis( W4, b[..., None], -1 )[..., 0]
	blend = np.clip( wb / np.maximum( wa + wb, 1e-6 ), 0, 1 )
	# stored as base | overlay << 8 | blend << 16; TatooinePlanet repacks it with CompactTerrainMaterial (the engine's layout)
	return a.astype( np.uint32 ) | ( b.astype( np.uint32 ) << 8 ) | ( ( blend * 255 ).astype( np.uint32 ) << 16 ), W4


# ----- props --------------------------------------------------------------------------------------------------------------

def props( h, rock, W4 ):
	out = []
	n = h.shape[0]
	X, Y = grid( n )
	slope = slope_deg( h, n )
	r = np.random.default_rng( 99 )
	keep = []

	def free( x, y, rad ):
		return all( math.hypot( x - a, y - b ) > rad + rr for a, b, rr in keep )

	def add( model, x, y, yaw, scale, sink, group, rad ):
		z = float( sample( h, x, y ) )
		out.append( { "Model": model, "X": round( x * M, 1 ), "Y": round( y * M, 1 ), "Z": round( ( z - sink ) * M, 1 ), "Yaw": round( yaw, 1 ), "Scale": round( scale, 3 ), "Group": group } )
		keep.append( ( x, y, rad ) )

	# keep the flats clear
	for fx, fy, rr in ( ( 0, 0, 70 ), ( TOWN[0], TOWN[1], 150 ), ( SITE[0], SITE[1], 60 ) ):
		keep.append( ( fx, fy, rr ) )
	# talus: boulders where the rock meets the floor
	foot = ( blur( W4[..., 1], 6 ) > 0.15 ) & ( W4[..., 1] < 0.5 ) & ( slope < 26 )
	cand = np.argwhere( foot )
	r.shuffle( cand )
	boulders = ["boulder_a", "boulder_b", "boulder_c", "boulder_d"]
	for j, i in cand[:4000]:
		if len( out ) >= 300: break
		x, y = X[j, i], Y[j, i]
		s = r.uniform( 0.6, 1.9 )
		if not free( x, y, 3.5 * s ): continue
		slab = r.random() < 0.15
		add( f"models/tatooine/{'slab_a' if slab else boulders[r.integers( 4 )]}.vmdl", x, y, r.uniform( 0, 360 ), s, 0.4 if slab else 0.15, "rocks", ( 8 if slab else 3.5 ) * s )
	# lone rocks out on the sand, and hoodoo spires along the basin edge
	for _ in range( 400 ):
		if len( out ) >= 360: break
		x, y = r.uniform( -600, 600 ), r.uniform( -600, 600 )
		if not free( x, y, 25 ) or W4[int( ( y / ( 2 * HALF ) + 0.5 ) * ( n - 1 ) ), int( ( x / ( 2 * HALF ) + 0.5 ) * ( n - 1 ) ), 1] > 0.3: continue
		add( f"models/tatooine/{boulders[r.integers( 4 )]}.vmdl", x, y, r.uniform( 0, 360 ), r.uniform( 0.5, 1.2 ), 0.2, "rocks", 6 )
	spires = 0
	for _ in range( 600 ):
		if spires >= 9: break
		x, y = r.uniform( -330, 200 ), r.uniform( -600, 600 )
		if not free( x, y, 45 ) or slope[int( ( y / ( 2 * HALF ) + 0.5 ) * ( n - 1 ) ), int( ( x / ( 2 * HALF ) + 0.5 ) * ( n - 1 ) )] > 12: continue
		add( f"models/tatooine/spire_{'abc'[r.integers( 3 )]}.vmdl", x, y, r.uniform( 0, 360 ), r.uniform( 0.7, 1.3 ), 1.5, "rocks", 30 )
		spires += 1
	import tatooine_layout
	out += tatooine_layout.town( lambda x, y: float( sample( h, x, y ) ), TOWN, M )
	out += tatooine_layout.site( lambda x, y: float( sample( h, x, y ) ), SITE, M )
	return out


# ----- output -------------------------------------------------------------------------------------------------------------

def preview( h, W4, path ):
	from PIL import Image
	n = h.shape[0]
	cell = 2 * HALF / ( n - 1 )
	gy, gx = np.gradient( h, cell )
	nrm = np.dstack( [-gx, -gy, np.ones_like( h )] )
	nrm /= np.linalg.norm( nrm, axis=2, keepdims=True )
	L = np.array( [-0.45, 0.4, 0.8] ); L /= np.linalg.norm( L )
	shade = np.clip( nrm @ L, 0, 1 ) * 0.85 + 0.2
	cols = np.array( [[0.84, 0.66, 0.45], [0.62, 0.42, 0.29], [0.78, 0.68, 0.55], [0.75, 0.53, 0.34]] )
	img = ( W4 @ cols ) * shade[..., None]
	Image.fromarray( ( np.clip( img[::-1], 0, 1 ) * 255 ).astype( np.uint8 ) ).resize( ( 1024, 1024 ) ).save( path )
	print( "preview", path )


def main():
	t0 = time.time()
	sys.path.insert( 0, HERE )
	h, rock = shape()
	print( f"shaped {time.time() - t0:.0f}s, h {h.min():.1f}..{h.max():.1f} m" )
	h = thermal( h, rock, 25 )
	h, deposit = hydraulic( h, rock )
	h = thermal( h, rock, 25 )
	print( f"eroded {time.time() - t0:.0f}s" )
	hN = resize( h, N )
	rockN = resize( rock, N )
	depN = resize( deposit, N )
	# fine relief the working grid can't hold: sand ripples are in the texture, this is the metre-scale lumpiness
	hN += 0.25 * spectral( N, 1 / 30, 1 / 4, 1.5, 31 ) * ( 1 - rockN ) + 0.6 * spectral( N, 1 / 40, 1 / 3, 1.2, 32 ) * rockN
	control, W4 = materials( hN, rockN, depN )
	os.makedirs( OUT, exist_ok=True )
	units = np.clip( ( BASE + hN * M ) / RANGE * 65535, 0, 65535 ).astype( "<u2" )
	with open( os.path.join( OUT, "terrain.bin" ), "wb" ) as f:
		f.write( units.tobytes() )
		f.write( control.astype( "<u4" ).tobytes() )
	with open( os.path.join( OUT, "terrain.json" ), "w", newline="\n" ) as f:
		json.dump( { "Resolution": N, "Size": SIZE, "Range": RANGE, "Base": BASE }, f, indent=1 )
	pr = props( hN, rockN, W4 )
	with open( os.path.join( OUT, "props.json" ), "w", newline="\n" ) as f:
		json.dump( pr, f, indent=0 )
	print( f"wrote terrain ({h.min():.1f}..{h.max():.1f} m), {len( pr )} props, {time.time() - t0:.0f}s" )
	if "--preview" in sys.argv:
		preview( hN, W4, os.path.join( os.environ.get( "TEMP", HERE ), "tatooine_preview.png" ) )


if __name__ == "__main__":
	main()
