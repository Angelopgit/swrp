"""
Tatooine's ground textures, all procedural (original, no source images): fine sand with wind ripples, wind-cut sandstone
and packed hardpan. Writes tileable 1024 px colour/normal/rough/height/ao PNGs and one .tmat (terrain material) per
surface into Assets/materials/tatooine. Code/Map/Tatooine/TatooinePlanet.cs paints the terrain with them.

Run with system Python (numpy + Pillow): py MapSource/tatooine_textures.py
"""
import json
import os

import numpy as np
from PIL import Image

N = 1024
OUT = os.environ.get( "SWRP_TATOOINE_OUT" ) or os.path.join( os.path.dirname( __file__ ), "..", "Assets", "materials", "tatooine" )
rng = np.random.default_rng( 1138 )

fy, fx = np.meshgrid( np.fft.fftfreq( N ), np.fft.fftfreq( N ), indexing="ij" )
FR = np.sqrt( fx * fx + fy * fy )
FR[0, 0] = 1.0


def noise( beta, lo=0.0, hi=1.0, aniso=1.0 ):
	"""Tileable noise with a 1/f^beta spectrum between frequencies lo..hi (cycles per pixel), normalised to 0..1.
	aniso > 1 stretches it along x (wind-swept)."""
	w = np.fft.fft2( rng.standard_normal( ( N, N ) ) )
	f = np.sqrt( ( fx / aniso ) ** 2 + fy ** 2 )
	f[0, 0] = 1.0
	amp = f ** ( -beta / 2.0 )
	amp[( FR < lo ) | ( FR > hi )] = 0.0
	amp[0, 0] = 0.0
	n = np.real( np.fft.ifft2( w * amp ) )
	n -= n.min()
	return n / max( n.max(), 1e-9 )


def ripples( period_px, warp ):
	"""Wind ripples: ridges across x, bent by low-frequency noise so they meander; asymmetric (steep lee side). Tileable
	because the period divides N and the warp is periodic."""
	y = np.arange( N )[:, None].repeat( N, 1 ).astype( np.float64 )
	phase = ( y / period_px + warp ) % 1.0
	return np.where( phase < 0.72, phase / 0.72, ( 1.0 - phase ) / 0.28 )   # long windward slope, short lee


def normal_map( h, strength ):
	dx = ( np.roll( h, -1, 1 ) - np.roll( h, 1, 1 ) ) * 0.5 * strength
	dy = ( np.roll( h, -1, 0 ) - np.roll( h, 1, 0 ) ) * 0.5 * strength
	n = np.stack( [-dx, dy, np.ones_like( h )], -1 )
	n /= np.linalg.norm( n, axis=-1, keepdims=True )
	return ( n * 0.5 + 0.5 )


def ao_from( h, radius_px=6 ):
	"""Cheap cavity AO: how far below its blurred surroundings each texel sits."""
	k = np.fft.fft2( h )
	blur = np.real( np.fft.ifft2( k * np.exp( -( FR * radius_px * 2.0 ) ** 2 ) ) )
	return np.clip( 1.0 - np.maximum( blur - h, 0.0 ) * 6.0, 0.35, 1.0 )


def tint( base, var, *layers ):
	"""base RGB (0..1) varied by noise layers: each (noise, rgb_delta)."""
	c = np.ones( ( N, N, 3 ) ) * np.array( base )
	for n, d in layers:
		c += ( n[..., None] - 0.5 ) * np.array( d )
	c *= 1.0 + ( var - 0.5 )[..., None] * 0.12
	return np.clip( c, 0, 1 )


def save( name, kind, arr ):
	a = np.clip( arr, 0, 1 )
	img = Image.fromarray( ( a * 255 + 0.5 ).astype( np.uint8 ), "RGB" if a.ndim == 3 else "L" )
	path = os.path.join( OUT, f"{name}_{kind}.png" )
	img.save( path )
	return f"materials/tatooine/{name}_{kind}.png"


def write( name, colour, height, rough, normal_strength, uv_scale, surface ):
	paths = {
		"AlbedoImage": save( name, "color", colour ),
		"RoughnessImage": save( name, "rough", rough ),
		"NormalImage": save( name, "normal", normal_map( height, normal_strength ) ),
		"HeightImage": save( name, "height", height ),
		"AOImage": save( name, "ao", ao_from( height ) ),
	}
	tmat = dict( paths, UVScale=uv_scale, Metalness=0, NormalStrength=1, HeightBlendStrength=1, DisplacementScale=1,
		NoTiling=True, Surface=surface, __references=[], __version=0 )
	with open( os.path.join( OUT, f"{name}.tmat" ), "w", newline="\n" ) as f:
		json.dump( tmat, f, indent=2 )
	print( "wrote", name )


def sand():
	warp = noise( 3.0, 0, 0.01 ) * 3.0 + noise( 2.5, 0, 0.03, aniso=4 ) * 0.6
	rip = ripples( 32, warp )                                   # 32 ripples across the tile
	grain = noise( 0.3, 0.15, 0.5 )
	mottle = noise( 2.6, 0, 0.05 )
	h = rip * 0.6 + noise( 1.2, 0.02, 0.2 ) * 0.2 + grain * 0.2
	mottle = 0.5 + ( mottle - 0.5 ) * 0.15                      # almost flat: anything tile-sized repeats visibly
	colour = tint( ( 0.80, 0.66, 0.47 ), mottle,
		( mottle, ( 0.07, 0.06, 0.05 ) ),
		( rip, ( 0.025, 0.022, 0.018 ) ),                        # crests catch a lighter, coarser sand (kept faint: the normal map carries the ripples)
		( grain, ( 0.10, 0.09, 0.08 ) ) )
	rough = np.clip( 0.88 + ( grain - 0.5 ) * 0.1, 0, 1 )
	write( "sand", colour, h, rough, 7.0, 1.2, "surfaces/dirt.surface" )


def sand_red():
	"""The windward faces and old dune cores: deeper ochre, coarser, longer smoother ripples. Blended in big patches by the
	terrain's control map so the dune field never reads as one repeated texture."""
	warp = noise( 3.0, 0, 0.01 ) * 2.0 + noise( 2.5, 0, 0.03, aniso=4 ) * 0.5
	rip = ripples( 16, warp )
	grain = noise( 0.3, 0.15, 0.5 )
	mottle = noise( 2.4, 0, 0.04 )
	h = rip * 0.55 + noise( 1.2, 0.02, 0.2 ) * 0.2 + grain * 0.25
	mottle = 0.5 + ( mottle - 0.5 ) * 0.15
	colour = tint( ( 0.76, 0.58, 0.40 ), mottle,
		( mottle, ( 0.08, 0.06, 0.04 ) ),
		( rip, ( 0.02, 0.018, 0.014 ) ),
		( grain, ( 0.12, 0.10, 0.08 ) ) )
	rough = np.clip( 0.9 + ( grain - 0.5 ) * 0.1, 0, 1 )
	write( "sand_red", colour, h, rough, 6.0, 0.9, "surfaces/dirt.surface" )


def rock():
	# wind-cut sandstone: horizontal strata (bands along y), cracks, pitting
	strata_warp = noise( 3.0, 0, 0.02 ) * 4.0
	y = np.arange( N )[:, None].repeat( N, 1 ) / N
	bands = 0.5 + 0.5 * np.sin( ( y * 24 + strata_warp ) * 2 * np.pi )
	fine_bands = 0.5 + 0.5 * np.sin( ( y * 96 + strata_warp * 3 ) * 2 * np.pi )
	cracks = np.abs( noise( 1.6, 0.004, 0.2 ) - 0.5 ) * 2
	cracks = np.clip( 1 - cracks * 9, 0, 1 ) ** 2
	pits = noise( 0.5, 0.1, 0.5 )
	big = noise( 2.4, 0, 0.06 )
	h = big * 0.45 + bands * 0.2 + fine_bands * 0.08 + pits * 0.15 - cracks * 0.35
	h = ( h - h.min() ) / ( h.max() - h.min() )
	colour = tint( ( 0.63, 0.49, 0.37 ), big,
		( bands, ( 0.10, 0.08, 0.06 ) ),
		( fine_bands, ( 0.04, 0.035, 0.03 ) ),
		( big, ( 0.10, 0.07, 0.05 ) ),
		( pits, ( 0.06, 0.05, 0.05 ) ) )
	colour *= ( 1 - cracks * 0.45 )[..., None]
	rough = np.clip( 0.8 + ( pits - 0.5 ) * 0.15, 0, 1 )
	write( "rock", colour, h, rough, 14.0, 0.6, "surfaces/dirt.surface" )


def hardpan():
	# baked flat ground: fine dust over a crazed crust (polygon cracks), pebbles
	cells = noise( 1.2, 0.01, 0.08 )
	edges = np.abs( np.roll( cells, 1, 0 ) - cells ) + np.abs( np.roll( cells, 1, 1 ) - cells )
	crack = np.clip( ( np.abs( cells - 0.5 ) < 0.012 ) * 1.0 + edges * 0, 0, 1 )
	pebbles = np.clip( ( noise( 0.2, 0.08, 0.5 ) - 0.72 ) * 6, 0, 1 )
	dust = 0.5 + ( noise( 2.4, 0, 0.05 ) - 0.5 ) * 0.25
	h = dust * 0.35 + pebbles * 0.45 - crack * 0.3 + noise( 0.6, 0.1, 0.5 ) * 0.1
	h = ( h - h.min() ) / ( h.max() - h.min() )
	colour = tint( ( 0.77, 0.67, 0.54 ), dust,
		( dust, ( 0.08, 0.07, 0.06 ) ),
		( pebbles, ( -0.10, -0.10, -0.09 ) ) )
	colour *= ( 1 - crack * 0.35 )[..., None]
	rough = np.clip( 0.9 - pebbles * 0.15, 0, 1 )
	write( "hardpan", colour, h, rough, 8.0, 1.0, "surfaces/dirt.surface" )


def dust():
	"""Blown-sand sprite (TatooineWeather): a soft, ragged, wind-stretched wisp; white, the code tints it."""
	n = 256
	y, x = np.mgrid[0:n, 0:n] / ( n - 1 ) * 2 - 1
	r = np.sqrt( ( x / 1.0 ) ** 2 + ( y / 0.45 ) ** 2 )
	big = noise( 2.0, 0, 0.05 )[:n, :n]
	fine = noise( 1.0, 0.02, 0.3 )[:n, :n]
	a = np.clip( 1 - r, 0, 1 ) ** 1.5 * ( 0.4 + big * 0.9 ) * ( 0.7 + fine * 0.5 )
	a = np.clip( a / a.max(), 0, 1 )
	img = np.dstack( [np.ones( ( n, n ) ), np.ones( ( n, n ) ), np.ones( ( n, n ) ), a] )
	Image.fromarray( ( img * 255 ).astype( np.uint8 ), "RGBA" ).save( os.path.join( OUT, "dust.png" ) )
	print( "wrote dust" )


def plaster():
	"""Sun-bleached adobe render for the town's walls and domes (ModelSource/tatooine_town.py): hand-smoothed lumps, hairline
	cracks, dust-darkened streaks running down. Not a terrain layer, so no .tmat; tiles every 2 m on the models."""
	lumps = noise( 2.0, 0.002, 0.12 )
	fine = noise( 0.8, 0.05, 0.5 )
	cells = noise( 1.2, 0.01, 0.06 )
	crack = np.clip( 1 - np.abs( cells - 0.5 ) / 0.008, 0, 1 ) * ( noise( 2.0, 0, 0.03 ) > 0.45 )
	streak = noise( 1.5, 0, 0.2, aniso=0.08 )            # stretched vertically: grime washed down by the rare rain
	h = lumps * 0.6 + fine * 0.25 - crack * 0.3
	h = ( h - h.min() ) / ( h.max() - h.min() )
	colour = tint( ( 0.86, 0.80, 0.69 ), lumps,
		( lumps, ( 0.05, 0.05, 0.05 ) ),
		( streak, ( -0.10, -0.10, -0.10 ) ),
		( fine, ( 0.05, 0.05, 0.05 ) ) )
	colour *= ( 1 - crack * 0.4 )[..., None]
	save( "plaster", "color", colour )
	save( "plaster", "normal", normal_map( h, 6.0 ) )
	save( "plaster", "rough", np.clip( 0.85 + ( fine - 0.5 ) * 0.1, 0, 1 ) )
	save( "plaster", "ao", ao_from( h ) )
	print( "wrote plaster" )


def sand_streak():
	"""Blown-sand ribbon texture for TatooineWeather's streamers (LineRenderer, u along the wind, v across): many thin
	filaments of grains, dense in the middle, frayed and soft at both edges; tiles along u. White: the code tints it."""
	w, h = 1024, 128
	r = np.random.default_rng( 5 )
	fu = np.fft.fftfreq( w )[None, :]
	fv = np.fft.fftfreq( h )[:, None]
	# long along u, fine across v: streaky filaments
	amp = 1.0 / np.maximum( np.sqrt( ( fu * 40 ) ** 2 + ( fv * 1.2 ) ** 2 ), 0.02 )
	amp[0, 0] = 0
	n = np.real( np.fft.ifft2( np.fft.fft2( r.standard_normal( ( h, w ) ) ) * amp ) )
	n = ( n - n.min() ) / ( n.max() - n.min() )
	fil = np.clip( ( n - 0.45 ) * 3.0, 0, 1 ) ** 1.5
	v = np.linspace( -1, 1, h )[:, None]
	edge = np.clip( 1 - np.abs( v ) ** 2, 0, 1 ) ** 1.5
	grain = r.random( ( h, w ) ) ** 6 * 0.6 * edge
	a = np.clip( fil * edge * 0.9 + grain, 0, 1 )
	img = np.dstack( [np.ones( ( h, w ) )] * 3 + [a] )
	# the trail shader runs v along the line, so the filaments go down the image
	Image.fromarray( ( img * 255 ).astype( np.uint8 ), "RGBA" ).transpose( Image.Transpose.ROTATE_90 ).save( os.path.join( OUT, "sand_streak.png" ) )
	print( "wrote sand_streak" )


if __name__ == "__main__":
	os.makedirs( OUT, exist_ok=True )
	sand()
	rock()
	hardpan()
	sand_red()
	dust()
	plaster()
	sand_streak()
