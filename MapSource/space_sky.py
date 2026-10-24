"""
Original space skies for SWRP, painted with numpy (no photos, no ripped textures): a starfield with a faint galaxy band and a
nebula, and a planet per hyperspace destination, plus the two hyperspace skies (star streaks and the blue tunnel), and the
daytime skies seen from the voxel planets' surfaces (surface_tatooine, surface_kamino, surface_hoth).

    ... -P MapSource/space_sky.py -- tatooine hoth surface_tatooine      (only those)

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P MapSource/space_sky.py

Writes Assets/materials/space/sky_<name>.png (equirectangular, 4096 x 2048) and sky_<name>.vmat. SwrpSpace (Code/Map)
switches between them. Every direction here is in s&box world space: +X starboard, -Y the bow (forward), +Z up. The bridge
looks out over the bow (its windows face the bow and the +X side), and the hangar bays open to either beam (a cruiser is
docked off +X), so each planet sits just off the bow where the bridge sees it, and a big moon hangs off the -X beam in
the hangar bay's opening.
"""
import os
import sys
import math
import numpy as np

HERE = os.path.dirname( os.path.abspath( __file__ ) )
OUT = os.path.join( os.path.dirname( HERE ), "Assets", "materials", "space" )
W, H = 4096, 2048           # the sky
NW, NH = 2048, 1024         # the nebula is smooth: painted at half size and scaled up

ONLY = [a for a in sys.argv[sys.argv.index( "--" ) + 1:]] if "--" in sys.argv else []


# ----- directions <-> equirect pixels (the sky shader's layout: world Y runs the other way round the texture) ----------

def grid( w, h ):
	u = ( np.arange( w, dtype=np.float32 ) + 0.5 ) / w
	v = ( np.arange( h, dtype=np.float32 ) + 0.5 ) / h
	U, V = np.meshgrid( u, v )
	lon = 2 * np.pi * U
	lat = np.pi / 2 - np.pi * V
	cl = np.cos( lat )
	return np.stack( [cl * np.cos( lon ), -cl * np.sin( lon ), np.sin( lat )], -1 ).astype( np.float32 )


def to_pixel( d, w, h ):
	lon = np.arctan2( -d[..., 1], d[..., 0] ) % ( 2 * np.pi )
	lat = np.arcsin( np.clip( d[..., 2], -1, 1 ) )
	x = lon / ( 2 * np.pi ) * w
	y = ( np.pi / 2 - lat ) / np.pi * h
	return x, y


def dir_from( yaw, pitch ):
	"""World direction from a yaw (0 = +X starboard, -90 = the bow) and a pitch (up positive), in degrees."""
	y, p = math.radians( yaw ), math.radians( pitch )
	return np.array( [math.cos( p ) * math.cos( y ), math.cos( p ) * math.sin( y ), math.sin( p )], np.float32 )


def norm( v ):
	return v / np.maximum( np.linalg.norm( v, axis=-1, keepdims=True ), 1e-9 )


def smooth( a, b, x ):
	t = np.clip( ( x - a ) / ( b - a ), 0, 1 )
	return t * t * ( 3 - 2 * t )


# ----- 3D value noise on directions (seamless all round the sky) --------------------------------------------------------

class Noise:
	def __init__( self, seed ):
		r = np.random.default_rng( seed )
		self.perm = np.concatenate( [r.permutation( 256 )] * 2 ).astype( np.int32 )
		self.vals = r.random( 256 ).astype( np.float32 )

	def value( self, p ):
		x, y, z = p[..., 0], p[..., 1], p[..., 2]
		xi, yi, zi = np.floor( x ), np.floor( y ), np.floor( z )
		xf, yf, zf = x - xi, y - yi, z - zi
		xi = xi.astype( np.int32 ) & 255
		yi = yi.astype( np.int32 ) & 255
		zi = zi.astype( np.int32 ) & 255
		u, v, w = xf * xf * ( 3 - 2 * xf ), yf * yf * ( 3 - 2 * yf ), zf * zf * ( 3 - 2 * zf )
		P, V = self.perm, self.vals

		def h( a, b, c ):
			return V[P[P[P[a] + b] + c] & 255]

		x1, y1, z1 = ( xi + 1 ) & 255, ( yi + 1 ) & 255, ( zi + 1 ) & 255
		c000, c100, c010, c110 = h( xi, yi, zi ), h( x1, yi, zi ), h( xi, y1, zi ), h( x1, y1, zi )
		c001, c101, c011, c111 = h( xi, yi, z1 ), h( x1, yi, z1 ), h( xi, y1, z1 ), h( x1, y1, z1 )
		a = c000 + ( c100 - c000 ) * u
		b = c010 + ( c110 - c010 ) * u
		c = c001 + ( c101 - c001 ) * u
		d = c011 + ( c111 - c011 ) * u
		e = a + ( b - a ) * v
		f = c + ( d - c ) * v
		return e + ( f - e ) * w

	def fbm( self, p, octaves=5, lac=2.0, gain=0.5 ):
		total = np.zeros( p.shape[:-1], np.float32 )
		amp, norm_ = 1.0, 0.0
		q = p.astype( np.float32 )
		for i in range( octaves ):
			total += amp * self.value( q + np.float32( i * 17.31 ) )
			norm_ += amp
			amp *= gain
			q = q * np.float32( lac )
		return total / norm_

	def ridged( self, p, octaves=5 ):
		total = np.zeros( p.shape[:-1], np.float32 )
		amp, norm_ = 1.0, 0.0
		q = p.astype( np.float32 )
		for i in range( octaves ):
			n = 1 - np.abs( self.value( q + np.float32( i * 9.7 ) ) * 2 - 1 )
			total += amp * n * n
			norm_ += amp
			amp *= 0.5
			q = q * np.float32( 2.03 )
		return total / norm_


def upscale( img, w, h ):
	"""Bilinear upscale of an equirect image, wrapping round horizontally."""
	sh, sw = img.shape[:2]
	x = ( np.arange( w ) + 0.5 ) * sw / w - 0.5
	y = np.clip( ( np.arange( h ) + 0.5 ) * sh / h - 0.5, 0, sh - 1 )
	x0 = np.floor( x ).astype( int )
	y0 = np.floor( y ).astype( int )
	fx = ( x - x0 )[None, :, None]
	fy = ( y - y0 )[:, None, None]
	x1 = ( x0 + 1 ) % sw
	x0 = x0 % sw
	y1 = np.minimum( y0 + 1, sh - 1 )
	a = img[y0][:, x0] * ( 1 - fx ) + img[y0][:, x1] * fx
	b = img[y1][:, x0] * ( 1 - fx ) + img[y1][:, x1] * fx
	return ( a * ( 1 - fy ) + b * fy ).astype( np.float32 )


# ----- the shared starfield -------------------------------------------------------------------------------------------

GALAXY = norm( np.array( [0.15, 0.25, 1.0], np.float32 ) )     # the galaxy band runs round near the horizon
STAR_TINTS = np.array( [[0.7, 0.8, 1.0], [0.85, 0.9, 1.0], [1.0, 1.0, 1.0], [1.0, 0.95, 0.85], [1.0, 0.85, 0.65], [1.0, 0.7, 0.55]], np.float32 )


def stars( img, seed ):
	r = np.random.default_rng( seed )

	def scatter( n, band ):
		d = norm( r.normal( size=( n, 3 ) ).astype( np.float32 ) )
		if band:
			# pull towards the galaxy plane
			k = d @ GALAXY
			d = norm( d - GALAXY[None, :] * ( k * r.uniform( 0.6, 0.97, n ).astype( np.float32 ) )[:, None] )
		return d

	def plot( d, bright, size ):
		x, y = to_pixel( d, W, H )
		tint = STAR_TINTS[r.integers( 0, len( STAR_TINTS ), len( d ) )]
		col = tint * bright[:, None]
		if size <= 0:
			xi = x.astype( int ) % W
			yi = np.clip( y.astype( int ), 0, H - 1 )
			np.add.at( img, ( yi, xi ), col )
			return
		rad = int( math.ceil( size * 2.5 ) )
		for dy in range( -rad, rad + 1 ):
			for dx in range( -rad, rad + 1 ):
				px = np.floor( x ) + dx
				py = np.floor( y ) + dy
				g = np.exp( -( ( px + 0.5 - x ) ** 2 + ( py + 0.5 - y ) ** 2 ) / ( 2 * size * size ) )
				ok = ( py >= 0 ) & ( py < H )
				np.add.at( img, ( py[ok].astype( int ), px[ok].astype( int ) % W ), col[ok] * g[ok, None] )

	plot( scatter( 110000, False ), r.uniform( 0.04, 0.3, 110000 ).astype( np.float32 ) ** 1.5 * 2.2, 0 )
	plot( scatter( 70000, True ), r.uniform( 0.03, 0.22, 70000 ).astype( np.float32 ) ** 1.5 * 2.0, 0 )
	plot( scatter( 5000, False ), r.uniform( 0.35, 0.9, 5000 ).astype( np.float32 ), 0.6 )
	plot( scatter( 350, False ), r.uniform( 1.0, 2.2, 350 ).astype( np.float32 ), 0.9 )


NEBULAE = {
	# name: (seed offset, colour A, colour B, dust colour, strength)
	"coruscant": ( 0.0, ( 0.16, 0.22, 0.62 ), ( 0.42, 0.18, 0.6 ), ( 0.02, 0.02, 0.05 ), 1.0 ),
	"kamino": ( 3.7, ( 0.05, 0.36, 0.48 ), ( 0.12, 0.2, 0.55 ), ( 0.01, 0.03, 0.05 ), 0.9 ),
	"geonosis": ( 7.1, ( 0.62, 0.18, 0.12 ), ( 0.5, 0.12, 0.38 ), ( 0.05, 0.02, 0.02 ), 0.95 ),
	"christophsis": ( 11.3, ( 0.38, 0.2, 0.66 ), ( 0.12, 0.42, 0.62 ), ( 0.03, 0.02, 0.06 ), 1.0 ),
	"tatooine": ( 15.2, ( 0.5, 0.3, 0.15 ), ( 0.2, 0.22, 0.5 ), ( 0.04, 0.03, 0.02 ), 0.8 ),
	"hoth": ( 19.6, ( 0.15, 0.3, 0.55 ), ( 0.3, 0.42, 0.62 ), ( 0.02, 0.03, 0.05 ), 0.85 ),
	"streak": ( 0.0, ( 0.1, 0.16, 0.4 ), ( 0.2, 0.12, 0.4 ), ( 0.02, 0.02, 0.05 ), 0.35 ),
}


def nebula( name ):
	off, ca, cb, dust, strength = NEBULAE[name]
	d = grid( NW, NH )
	n = Noise( 42 )
	q = d * 1.6 + np.float32( off )
	warp = np.stack( [n.fbm( q + 3.1, 4 ), n.fbm( q + 7.7, 4 ), n.fbm( q + 1.9, 4 )], -1 ) - 0.5
	f = n.fbm( q + warp * 1.6, 6 )
	mix = n.fbm( q * 1.7 + 5.0, 4 )
	# densest along the galaxy band, with a few clouds elsewhere
	band = np.exp( -( ( d @ GALAXY ) ** 2 ) / 0.07 )
	dens = smooth( 0.5, 0.86, f ) ** 1.4 * ( 0.15 + 0.85 * band )
	wisps = smooth( 0.6, 0.92, n.fbm( q * 3.3 + warp * 2.4 + 11.0, 5 ) ) * 0.5
	lanes = smooth( 0.5, 0.72, n.fbm( q * 4.1 + 23.0, 5 ) )
	col = np.array( ca, np.float32 ) * ( 1 - mix[..., None] ) + np.array( cb, np.float32 ) * mix[..., None]
	img = col * ( ( dens + wisps * dens ) * strength * 0.75 )[..., None]
	img *= ( 1 - 0.75 * lanes * band )[..., None]
	img += np.array( dust, np.float32 ) * band[..., None]
	# the faint glow of the galaxy's core
	img += np.array( [0.045, 0.04, 0.06], np.float32 ) * ( band ** 3 * smooth( 0.3, 0.9, n.fbm( q * 0.8 + 31.0, 3 ) ) )[..., None]
	return upscale( img, W, H )


# ----- planets ----------------------------------------------------------------------------------------------------------

PLANET_DIR = dir_from( -75, -5 )        # ahead and a little to the left as you face the bow: filling the bridge windows
PLANET_RADIUS = math.radians( 27 )
MOON_DIR = dir_from( -170, 3 )          # its big moon hangs off the other beam, in the -X hangar bay's opening
MOON_RADIUS = math.radians( 7 )


def basis( c, tilt ):
	"""An orthonormal frame round a planet's centre direction, with its pole tilted by `tilt` degrees."""
	up = np.array( [0, 0, 1], np.float32 )
	right = norm( np.cross( c, up ) )
	up = norm( np.cross( right, c ) )
	t = math.radians( tilt )
	pole = norm( up * math.cos( t ) + right * math.sin( t ) )
	east = norm( np.cross( pole, c ) )
	return pole, east


def sphere_hits( d, c, ang ):
	"""Where each view direction meets a sphere seen at angular radius `ang` in direction c: (hit mask, surface normal)."""
	r = math.sin( ang )
	b = d @ c
	disc = b * b - ( 1 - r * r )
	hit = disc > 0
	t = b - np.sqrt( np.maximum( disc, 0 ) )
	p = d * t[..., None] - c
	return hit & ( b > 0 ), norm( p )


def planet_surface( style, nrm, pole, east, n ):
	"""Albedo (and night lights) for a style, from the surface normal in the planet's own frame."""
	north = norm( np.cross( east, pole ) )
	local = np.stack( [nrm @ east, nrm @ north, nrm @ pole], -1 )
	lat = local[..., 2]
	if style == "coruscant":
		base = n.fbm( local * 6.0, 6 )
		grid_ = n.value( local * 160.0 )
		alb = np.array( [0.36, 0.3, 0.22], np.float32 ) * ( 0.35 + 1.2 * base[..., None] )
		alb = alb * ( 0.7 + 0.6 * grid_[..., None] )
		towers = smooth( 0.7, 0.85, n.value( local * 55.0 ) )                 # the brighter tower districts
		alb = alb + np.array( [0.18, 0.17, 0.16], np.float32 ) * towers[..., None]
		clouds = smooth( 0.62, 0.8, n.fbm( local * 3.0 + 4.0, 5 ) ) * 0.55
		alb = alb * ( 1 - clouds[..., None] ) + clouds[..., None] * 0.85
		city = smooth( 0.5, 0.7, n.fbm( local * 18.0, 4 ) ) * smooth( 0.55, 0.85, n.value( local * 420.0 ) ) * ( 1 - clouds )
		lanes = smooth( 0.88, 0.97, n.value( local * 120.0 ) )      # bright arterial clusters
		lights = np.array( [1.0, 0.72, 0.38], np.float32 ) * ( city * 0.5 + lanes * 0.35 )[..., None]
		return alb, lights, np.array( [0.45, 0.65, 1.0], np.float32 ), 0.8
	if style == "kamino":
		sea = np.array( [0.02, 0.1, 0.2], np.float32 ) * ( 0.8 + 0.4 * n.fbm( local * 5.0, 4 )[..., None] )
		bands = local.copy()
		bands[..., 2] *= 4.0                     # storms stretched along the latitudes
		w = n.fbm( local * 2.0 + 9.0, 4 ) - 0.5
		cl = n.fbm( bands * 3.5 + w[..., None] * 2.5, 6 )
		clouds = smooth( 0.45, 0.72, cl ) * ( 0.75 + 0.25 * np.abs( lat ) )
		alb = sea * ( 1 - clouds[..., None] ) + clouds[..., None] * np.array( [0.85, 0.9, 0.95], np.float32 )
		return alb, None, np.array( [0.5, 0.78, 1.0], np.float32 ), 1.3
	if style == "geonosis":
		r = n.ridged( local * 4.0, 6 )
		dust = n.fbm( local * 2.0 + 13.0, 4 )
		alb = np.array( [0.55, 0.26, 0.12], np.float32 ) * ( 0.6 + 0.7 * r[..., None] )
		alb = alb * ( 1 - 0.3 * dust[..., None] ) + np.array( [0.85, 0.55, 0.3], np.float32 ) * ( 0.3 * dust[..., None] )
		crater = smooth( 0.82, 0.9, n.value( local * 22.0 ) )
		alb = alb * ( 1 - 0.35 * crater[..., None] )
		return alb, None, np.array( [1.0, 0.6, 0.35], np.float32 ), 0.55
	if style == "christophsis":
		base = n.fbm( local * 5.0, 5 )
		cells = n.value( local * 70.0 )
		crystal = smooth( 0.45, 0.65, base ) * smooth( 0.3, 0.8, cells )
		alb = np.array( [0.5, 0.48, 0.58], np.float32 ) * ( 0.6 + 0.5 * base[..., None] )
		alb = alb * ( 1 - crystal[..., None] ) + crystal[..., None] * np.array( [0.35, 0.75, 1.0], np.float32 )
		glint = smooth( 0.93, 0.99, n.value( local * 260.0 ) ) * crystal
		return alb, np.array( [0.6, 0.9, 1.0], np.float32 ) * glint[..., None] * 0.35, np.array( [0.75, 0.62, 1.0], np.float32 ), 0.9
	if style == "tatooine":
		dunes = n.ridged( local * np.array( [9.0, 3.0, 9.0], np.float32 ), 5 )
		rock = smooth( 0.55, 0.75, n.fbm( local * 3.5 + 2.0, 5 ) )
		alb = np.array( [0.78, 0.6, 0.38], np.float32 ) * ( 0.75 + 0.35 * dunes[..., None] )
		alb = alb * ( 1 - rock[..., None] ) + rock[..., None] * np.array( [0.52, 0.34, 0.22], np.float32 )
		haze = smooth( 0.6, 0.85, n.fbm( local * 2.5 + 7.0, 4 ) ) * 0.2
		alb = alb * ( 1 - haze[..., None] ) + haze[..., None] * np.array( [0.9, 0.85, 0.75], np.float32 )
		return alb, None, np.array( [0.9, 0.75, 0.55], np.float32 ), 0.5
	if style == "hoth":
		r = n.ridged( local * 6.0, 6 )
		rock = smooth( 0.7, 0.85, r ) * 0.5
		alb = np.array( [0.86, 0.9, 0.96], np.float32 ) * ( 0.85 + 0.15 * n.fbm( local * 9.0, 4 )[..., None] )
		alb = alb * ( 1 - rock[..., None] ) + rock[..., None] * np.array( [0.42, 0.45, 0.52], np.float32 )
		clouds = smooth( 0.55, 0.8, n.fbm( local * 3.0 + 5.0, 5 ) ) * 0.4
		alb = alb * ( 1 - clouds[..., None] ) + clouds[..., None]
		return alb, None, np.array( [0.65, 0.82, 1.0], np.float32 ), 1.0
	# a grey moon
	r = n.fbm( local * 5.0, 5 )
	crater = smooth( 0.8, 0.9, n.value( local * 14.0 ) )
	alb = np.array( [0.42, 0.4, 0.38], np.float32 ) * ( 0.7 + 0.5 * r[..., None] ) * ( 1 - 0.3 * crater[..., None] )
	return alb, None, np.array( [0.6, 0.6, 0.65], np.float32 ), 0.0


def draw_planet( img, d, style, c, ang, sun, tilt, seed, rings=None ):
	n = Noise( seed )
	pole, east = basis( c, tilt )
	cosang = math.cos( min( ang * ( 1.35 if rings is None else rings[1] * 1.6 ), math.pi * 0.95 ) )     # the rings reach further out
	near = ( d @ c ) > cosang                      # the disc and its halo
	idx = np.nonzero( near )
	dv = d[idx]
	hit, nrm = sphere_hits( dv, c, ang )
	alb, lights, atm, atm_k = planet_surface( style, nrm, pole, east, n )
	ndl = nrm @ sun
	day = smooth( -0.08, 0.35, ndl )
	view = -dv
	mu = np.clip( np.sum( nrm * view, -1 ), 0, 1 )
	col = alb * ( 0.015 + 1.15 * day )[..., None]
	if lights is not None:
		col += lights * ( 1 - smooth( -0.15, 0.1, ndl ) )[..., None]
	rim = ( 1 - mu ) ** 2.6 * atm_k
	col += atm * ( rim * smooth( -0.35, 0.45, ndl ) )[..., None] * 0.9
	# the halo just off the limb
	theta = np.arccos( np.clip( dv @ c, -1, 1 ) )
	limb = norm( dv - c[None, :] * ( dv @ c )[..., None] )
	halo = np.exp( -np.maximum( theta - ang, 0 ) / ( ang * 0.045 ) ) * atm_k
	halo_lit = smooth( -0.4, 0.5, limb @ sun )
	outside = ~hit
	out = img[idx]
	blend = np.where( hit[..., None], col, out )
	blend = blend + np.where( outside[..., None], atm[None, :] * ( halo * halo_lit )[..., None] * 0.7, 0 )
	if rings is not None:
		blend = draw_rings( blend, dv, hit, nrm, c, ang, sun, rings, n )
	img[idx] = blend


def draw_rings( col, dv, hit, nrm, c, ang, sun, rings, n ):
	"""A flat ring round the planet: in front of it, or behind it where the planet hides it, shadowed on its night side."""
	inner, outer, tilt_deg, tint = rings
	r = math.sin( ang )
	pole, east = basis( c, tilt_deg )
	# the ring plane through the planet's centre (at distance 1 along c), facing `ring_n`
	ring_n = norm( pole + c * 0.16 )         # nearly edge-on, opened a little towards us
	denom = dv @ ring_n
	t = ( c @ ring_n ) / np.where( np.abs( denom ) < 1e-6, 1e-6, denom )
	p = dv * t[..., None] - c
	dist = np.linalg.norm( p, axis=-1 ) / r
	on = ( t > 0 ) & ( dist > inner ) & ( dist < outer )
	# behind the planet: hidden where the disc is nearer
	b = dv @ c
	t_sphere = b - np.sqrt( np.maximum( b * b - ( 1 - r * r ), 0 ) )
	on &= ~( hit & ( t_sphere < t ) )
	radial = ( dist - inner ) / ( outer - inner )
	bands = 0.35 + 0.65 * n.fbm( np.stack( [radial * 30, radial * 0, radial * 0], -1 ), 4 )
	alpha = np.clip( bands * smooth( 0, 0.08, radial ) * smooth( 1, 0.85, radial ), 0, 1 ) * 0.8
	# the planet's shadow on the ring
	pn = norm( p )
	shadow = np.where( ( pn @ sun ) < -0.2, 0.25, 1.0 )
	rc = np.array( tint, np.float32 ) * ( 0.25 + 0.75 * shadow )[..., None]
	a = np.where( on, alpha, 0 )[..., None]
	return col * ( 1 - a ) + rc * a


def draw_sun( img, d, sun ):
	k = np.clip( d @ sun, -1, 1 )
	ang = np.arccos( k )
	core = np.exp( -ang / 0.006 ) * 6
	glow = np.exp( -ang / 0.08 ) * 0.5 + np.exp( -ang / 0.35 ) * 0.08
	img += np.array( [1.0, 0.93, 0.8], np.float32 ) * ( core + glow )[..., None]


PLANETS = {
	# name: (sun yaw, sun pitch, axial tilt, seed, rings). The planet's lit side faces the sun; the angle between the sun and
	# the planet's own view of the ship (yaw 105) sets how much of it is lit: about 85 degrees on Coruscant shows the night side.
	# The sun sits on the moon's side of the sky so the moon in the hangar bay is lit too.
	"coruscant": ( 105 - 85, 18, 18, 5, None ),
	"kamino": ( 105 - 50, 25, -12, 6, None ),
	"geonosis": ( 105 - 55, 28, 24, 7, ( 1.12, 1.42, 24, ( 0.78, 0.6, 0.45 ) ) ),
	"christophsis": ( 105 - 60, 18, 8, 8, None ),
	"tatooine": ( 105 - 45, 20, 10, 9, None ),
	"hoth": ( 105 - 65, 22, -16, 10, None ),
}


# ----- skies from the voxel planets' surfaces ---------------------------------------------------------------------------

SURFACES = {
	# name: (zenith, horizon, ground below the horizon, cloud colour, cloud cover 0..1, suns as (yaw, pitch, rgb, size))
	"tatooine": ( ( 0.16, 0.36, 0.78 ), ( 0.86, 0.8, 0.68 ), ( 0.55, 0.42, 0.28 ), ( 1.0, 0.97, 0.92 ), 0.12,
		[( 35, 38, ( 1.0, 0.95, 0.82 ), 1.0 ), ( 52, 30, ( 1.0, 0.72, 0.45 ), 0.7 )] ),
	"kamino": ( ( 0.07, 0.09, 0.11 ), ( 0.22, 0.26, 0.3 ), ( 0.04, 0.07, 0.09 ), ( 0.2, 0.23, 0.27 ), 0.95, [] ),
	"hoth": ( ( 0.42, 0.55, 0.74 ), ( 0.86, 0.9, 0.95 ), ( 0.8, 0.85, 0.92 ), ( 0.92, 0.94, 0.97 ), 0.6,
		[( -30, 18, ( 1.0, 0.97, 0.92 ), 0.6 )] ),
}


def surface_sky( name, d ):
	zen, hor, ground, cloud, cover, suns = SURFACES[name]
	n = Noise( 77 )
	up = d[..., 2]
	t = np.clip( up, 0, 1 ) ** 0.45
	img = np.array( hor, np.float32 ) * ( 1 - t[..., None] ) + np.array( zen, np.float32 ) * t[..., None]
	# below the horizon: a band of haze fading into the ground colour
	below = smooth( 0.0, -0.12, up )
	img = img * ( 1 - below[..., None] ) + np.array( ground, np.float32 ) * below[..., None]
	# clouds: flattened noise over the dome, thicker towards the horizon
	q = d / np.maximum( up[..., None] + 0.12, 0.05 ) * np.float32( 1.4 )
	q[..., 2] = 0
	c = n.fbm( q * 1.3 + 3.0, 6 )
	dens = smooth( 1.0 - cover - 0.15, 1.0 - cover + 0.35, c ) * smooth( -0.02, 0.08, up )
	shade = 0.75 + 0.35 * n.fbm( q * 3.0 + 9.0, 4 )
	img = img * ( 1 - dens[..., None] * 0.9 ) + ( np.array( cloud, np.float32 ) * shade[..., None] ) * ( dens[..., None] * 0.9 )
	for ( yaw, pitch, rgb, size ) in suns:
		sd = dir_from( yaw, pitch )
		ang = np.arccos( np.clip( d @ sd, -1, 1 ) )
		core = smooth( 0.012 * size, 0.008 * size, ang ) * 8.0
		glow = np.exp( -ang / ( 0.06 * size ) ) * 0.8 + np.exp( -ang / 0.4 ) * 0.15
		img += np.array( rgb, np.float32 ) * ( core * ( 1 - dens * 0.8 ) + glow )[..., None]
	return img.astype( np.float32 )


# ----- hyperspace skies -------------------------------------------------------------------------------------------------

FWD = np.array( [0, -1, 0], np.float32 )


def polar( d ):
	"""Angle from the bow (0..pi) and the angle round it, for every direction."""
	phi = np.arccos( np.clip( d @ FWD, -1, 1 ) )
	right = np.array( [1, 0, 0], np.float32 )
	up = np.array( [0, 0, 1], np.float32 )
	alpha = np.arctan2( d @ up, d @ right )
	return phi, alpha


def sky_streak( d ):
	phi, alpha = polar( d )
	img = nebula( "streak" ) * 0.6
	r = np.random.default_rng( 9 )
	lines = 2400
	a = ( alpha + np.pi ) / ( 2 * np.pi ) * lines
	i = np.floor( a ).astype( int ) % lines
	frac = a - np.floor( a )
	start = r.uniform( 0.05, 2.4, lines ).astype( np.float32 )[i]
	length = r.uniform( 0.15, 0.9, lines ).astype( np.float32 )[i]
	lum = ( r.uniform( 0, 1, lines ).astype( np.float32 ) ** 3 * 1.6 )[i]
	tint = np.array( [[0.75, 0.85, 1.0], [1.0, 1.0, 1.0], [0.6, 0.75, 1.0]], np.float32 )[r.integers( 0, 3, lines )][i]
	along = ( phi - start ) / length
	seg = smooth( 0, 0.1, along ) * smooth( 1, 0.4, along ) * ( along > 0 ) * ( along < 1 )
	# thin lines: a few pixels wide whatever the angle from the bow
	width = np.clip( 0.0035 / np.maximum( np.sin( phi ), 0.02 ) * lines / ( 2 * np.pi ), 0.08, 0.5 )
	prof = np.exp( -( ( frac - 0.5 ) ** 2 ) / ( 2 * width * width ) )
	img += tint * ( lum * seg * prof )[..., None]
	img += np.array( [0.55, 0.75, 1.0], np.float32 ) * ( np.exp( -phi / 0.12 ) * 1.4 + np.exp( -phi / 0.5 ) * 0.25 )[..., None]
	return img


def sky_tunnel( d ):
	phi, alpha = polar( d )
	n = Noise( 21 )
	s = np.log( np.tan( np.clip( phi, 1e-3, np.pi - 1e-3 ) / 2 ) )        # runs from the bow (-inf) to the stern (+inf)
	tw = alpha + s * 0.55                                                  # a slow twist down the tunnel
	p = np.stack( [np.cos( tw ) * 6.0, np.sin( tw ) * 6.0, s * 0.9], -1 )
	streaks = n.fbm( p, 6 )
	fine = n.fbm( np.stack( [np.cos( tw ) * 22.0, np.sin( tw ) * 22.0, s * 2.2], -1 ), 4 )
	k = smooth( 0.45, 0.85, streaks * 0.7 + fine * 0.45 )
	deep = np.array( [0.02, 0.05, 0.22], np.float32 )
	mid = np.array( [0.1, 0.35, 0.95], np.float32 )
	hi = np.array( [0.75, 0.92, 1.0], np.float32 )
	img = deep + mid * k[..., None] * 0.9 + hi * ( smooth( 0.75, 0.98, k ) * 0.9 )[..., None]
	ends = np.exp( -phi / 0.18 ) * 2.2 + np.exp( -( np.pi - phi ) / 0.25 ) * 0.6 + np.exp( -phi / 0.6 ) * 0.25
	img += np.array( [0.7, 0.88, 1.0], np.float32 ) * ends[..., None]
	return img.astype( np.float32 )


# ----- output -------------------------------------------------------------------------------------------------------

def save( name, img, exposure=0.0 ):
	import bpy
	os.makedirs( OUT, exist_ok=True )
	# a gentle filmic shoulder so bright cores roll off instead of clipping, then sRGB
	x = np.maximum( img, 0 )
	x = x / ( 1 + x * 0.35 ) * 1.35
	x = np.where( x <= 0.0031308, x * 12.92, 1.055 * np.power( np.clip( x, 0.0031308, None ), 1 / 2.4 ) - 0.055 )
	rgba = np.ones( ( H, W, 4 ), np.float32 )
	# a little dither so the soft glows don't band in 8 bits
	rgba[..., :3] = np.clip( x + ( np.random.default_rng( 5 ).random( x.shape, np.float32 ) - 0.5 ) / 255.0, 0, 1 )
	im = bpy.data.images.new( name, W, H, alpha=False, float_buffer=False )
	im.colorspace_settings.name = "Non-Color"            # the values are already sRGB
	im.pixels.foreach_set( rgba[::-1].ravel() )
	path = os.path.join( OUT, f"sky_{name}.png" )
	im.filepath_raw = path
	im.file_format = "PNG"
	im.save()
	bpy.data.images.remove( im )
	with open( os.path.join( OUT, f"sky_{name}.vmat" ), "w", newline="\n" ) as fh:
		fh.write( "// Generated by MapSource/space_sky.py\n\nLayer0\n{\n\tshader \"shaders/sky.shader\"\n"
			f"\tSkyTexture \"materials/space/sky_{name}.png\"\n\tg_flBrightnessExposureBias \"{exposure:.3f}\"\n}}\n" )
	print( f"SKY {name}" )


def main():
	d = grid( W, H )
	base = np.zeros( ( H, W, 3 ), np.float32 )
	stars( base, 1977 )
	for name, ( sy, sp, tilt, seed, rings ) in PLANETS.items():
		if ONLY and name not in ONLY:
			continue
		img = base + nebula( name )
		sun = dir_from( sy, sp )
		draw_sun( img, d, sun )
		draw_planet( img, d, "moon", MOON_DIR, MOON_RADIUS, sun, 5, seed + 100 )
		draw_planet( img, d, name, PLANET_DIR, PLANET_RADIUS, sun, tilt, seed, rings )
		save( name, img )
	for name in SURFACES:
		if f"surface_{name}" in ONLY:
			save( f"surface_{name}", surface_sky( name, d ) )
	if not ONLY or "streak" in ONLY:
		save( "streak", sky_streak( d ) + base * 0.25, 0.3 )
	if not ONLY or "tunnel" in ONLY:
		save( "tunnel", sky_tunnel( d ), 0.3 )


main()
