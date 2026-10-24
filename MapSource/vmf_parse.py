"""
Minimal Valve Map Format (.vmf) reader: just enough to pull out brush geometry and a few entities.

Pure Python, no dependencies, so it runs both in plain Python and inside Blender.
"""
import re
import math

TOKEN = re.compile( r'"((?:[^"\\]|\\.)*)"|([{}])' )


def parse( path ):
	"""Returns the root as a list of (name, props, children) tuples; props is a dict, children a list of the same."""
	with open( path, "r", encoding="utf-8", errors="replace" ) as fh:
		text = fh.read()

	# Block names are bare words followed by '{'; key/values are two quoted strings.
	pos = 0
	n = len( text )
	stack = [( "root", {}, [] )]
	line_re = re.compile( r'\s*("([^"]*)"\s+"([^"]*)"|([A-Za-z_][A-Za-z0-9_]*)|\{|\})' )
	pending = None
	while pos < n:
		m = line_re.match( text, pos )
		if not m:
			pos += 1
			continue
		pos = m.end()
		tok = m.group( 1 )
		if m.group( 2 ) is not None:
			stack[-1][1][m.group( 2 )] = m.group( 3 )
		elif m.group( 4 ) is not None:
			pending = m.group( 4 )
		elif tok == "{":
			block = ( pending or "block", {}, [] )
			stack[-1][2].append( block )
			stack.append( block )
			pending = None
		elif tok == "}":
			if len( stack ) > 1:
				stack.pop()
	return stack[0][2]


def find( children, name ):
	return [c for c in children if c[0] == name]


# ---------------------------------------------------------------------------------------------------------------------
# Brush geometry
# ---------------------------------------------------------------------------------------------------------------------

def _vec( s ):
	return tuple( float( x ) for x in s.replace( "(", " " ).replace( ")", " " ).split() )


def _sub( a, b ): return ( a[0] - b[0], a[1] - b[1], a[2] - b[2] )
def _dot( a, b ): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def _cross( a, b ): return ( a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] )
def _len( a ): return math.sqrt( _dot( a, a ) )


def _norm( a ):
	l = _len( a )
	return ( a[0] / l, a[1] / l, a[2] / l ) if l > 1e-9 else ( 0.0, 0.0, 1.0 )


def side_plane( side_props ):
	"""Returns (normal, d) so that dot(normal, p) == d for points on the plane, or None if degenerate."""
	pts = re.findall( r"\(([^)]*)\)", side_props["plane"] )
	if len( pts ) != 3:
		return None
	p1, p2, p3 = [_vec( p ) for p in pts]
	nrm = _cross( _sub( p3, p1 ), _sub( p2, p1 ) )       # Source winds planes clockwise
	if _len( nrm ) < 1e-9:
		return None
	nrm = _norm( nrm )
	return nrm, _dot( nrm, p1 )


def _clip( poly, nrm, d, eps=1e-4 ):
	"""Keep the part of a convex polygon with dot(nrm, p) <= d."""
	out = []
	for i in range( len( poly ) ):
		a = poly[i]
		b = poly[( i + 1 ) % len( poly )]
		da = _dot( nrm, a ) - d
		db = _dot( nrm, b ) - d
		if da <= eps:
			out.append( a )
		if ( da < -eps and db > eps ) or ( da > eps and db < -eps ):
			t = da / ( da - db )
			out.append( ( a[0] + ( b[0] - a[0] ) * t, a[1] + ( b[1] - a[1] ) * t, a[2] + ( b[2] - a[2] ) * t ) )
	return out


def _huge_quad( nrm, d, size=65536.0 ):
	"""A big quad lying in the plane."""
	ref = ( 0.0, 0.0, 1.0 ) if abs( nrm[2] ) < 0.9 else ( 1.0, 0.0, 0.0 )
	u = _norm( _cross( nrm, ref ) )
	v = _cross( nrm, u )
	c = ( nrm[0] * d, nrm[1] * d, nrm[2] * d )
	return [
		( c[0] + ( -u[0] - v[0] ) * size, c[1] + ( -u[1] - v[1] ) * size, c[2] + ( -u[2] - v[2] ) * size ),
		( c[0] + ( u[0] - v[0] ) * size, c[1] + ( u[1] - v[1] ) * size, c[2] + ( u[2] - v[2] ) * size ),
		( c[0] + ( u[0] + v[0] ) * size, c[1] + ( u[1] + v[1] ) * size, c[2] + ( u[2] + v[2] ) * size ),
		( c[0] + ( -u[0] + v[0] ) * size, c[1] + ( -u[1] + v[1] ) * size, c[2] + ( -u[2] + v[2] ) * size ),
	]


def _axis( text ):
	"""'[1 0 0 0] 0.25' -> ((x, y, z), shift, scale) or None."""
	nums = re.findall( r"-?\d+(?:\.\d+)?(?:e[+-]?\d+)?", text.lower() )
	if len( nums ) < 5:
		return None
	n = [float( x ) for x in nums[:5]]
	return ( ( n[0], n[1], n[2] ), n[3], n[4] or 0.25 )


def solid_faces( solid ):
	"""
	Turns a Source brush into faces: list of dict(material, verts, normal). Verts are wound counter-clockwise seen
	from outside. Displacement faces are returned with disp=True and no clipping, so callers can skip or tessellate them.
	"""
	sides = [c for c in solid[2] if c[0] == "side"]
	planes = []
	for s in sides:
		pl = side_plane( s[1] )
		planes.append( pl )

	faces = []
	for i, s in enumerate( sides ):
		if planes[i] is None:
			continue
		nrm, d = planes[i]
		poly = _huge_quad( nrm, d )
		for j, other in enumerate( planes ):
			if j == i or other is None:
				continue
			poly = _clip( poly, other[0], other[1] )
			if len( poly ) < 3:
				break
		if len( poly ) < 3:
			continue
		# winding: make the polygon's own normal agree with the plane normal (outward)
		area_n = ( 0.0, 0.0, 0.0 )
		for k in range( 1, len( poly ) - 1 ):
			c = _cross( _sub( poly[k], poly[0] ), _sub( poly[k + 1], poly[0] ) )
			area_n = ( area_n[0] + c[0], area_n[1] + c[1], area_n[2] + c[2] )
		if _dot( area_n, nrm ) < 0:
			poly.reverse()
		faces.append( {
			"uaxis": _axis( s[1].get( "uaxis", "" ) ),
			"vaxis": _axis( s[1].get( "vaxis", "" ) ),
			"material": s[1].get( "material", "" ).upper(),
			"verts": poly,
			"normal": nrm,
			"disp": any( c[0] == "dispinfo" for c in s[2] ),
			"dispinfo": next( ( c for c in s[2] if c[0] == "dispinfo" ), None ),
		} )
	return faces
