"""
Tatooine's town and event site layouts (called by tatooine_terrain.py, which flattens their ground first). Pieces come from
ModelSource/tatooine_town.py; every doorway faces the piece's +X, so `facing` turns it towards a point.

Town (Ajim/Mos Espa-like, ~200 m across): one main street east-west with a plaza in the middle, the cantina on the
plaza's north side, domed houses and blocks along the street with alleys between them, a ksar of stacked granaries at the
west end, market stalls, vaporators on the edges, low walls and crates as cover.

Event site (a Separatist forward outpost in a hollow ringed by rock, its open side facing the town): a command bunker
facing the approach, two watchtowers covering it, a forward line of barricades, a perimeter of wall panels with gaps,
a landing pad and comms mast behind. Hidden until a Game Master opens it (/gm outpost); "spawn" entries are the garrison.
"""
import math

T = "models/tatooine/town/"


def facing( x, y, tx, ty ):
	return math.degrees( math.atan2( ty - y, tx - x ) )


def town( ground, centre, m ):
	cx, cy = centre
	out = []

	def put( name, x, y, yaw, scale=1.0, sink=0.15 ):
		x, y = cx + x, cy + y
		out.append( { "Model": T + name + ".vmdl", "X": round( x * m, 1 ), "Y": round( y * m, 1 ), "Z": round( ( ground( x, y ) - sink ) * m, 1 ),
			"Yaw": round( yaw, 1 ), "Scale": scale, "Group": "town" } )

	# the plaza and the cantina on its north side, door facing the plaza
	put( "cantina", 0, 30, -90 )
	for x, y in ( ( -12, -8 ), ( 10, -10 ) ):
		put( "stall", x, y, facing( x, y, 0, 0 ) )
	put( "vaporator", 3, 4, 0 )
	put( "crates", -18, 6, 20 )
	put( "crates", 16, -2, 140 )
	# along the main street (y = 0): houses both sides, doors to the street, alleys between
	north = [( -70, 22, "hut_small" ), ( -48, 26, "block_a" ), ( 36, 24, "hut_large" ), ( 62, 22, "block_b" ), ( 88, 20, "hut_small" )]
	south = [( -82, -24, "hut_large" ), ( -56, -22, "hut_small" ), ( -30, -26, "block_b" ), ( 30, -24, "hut_small" ), ( 54, -28, "block_a" ), ( 80, -22, "hut_large" )]
	for x, y, n in north + south:
		put( n, x, y, facing( x, y, x, 0 ) )
	# the ksar at the west end, its stair facing the street
	put( "ghorfa", -112, 4, 0 )
	put( "ghorfa", -112, -24, 0 )
	# low walls: yard walls behind the houses and two in the street for cover
	for x, y, yaw, n in ( ( -40, 48, 0, "wall_straight" ), ( 50, 50, 10, "wall_straight" ), ( -60, -48, 0, "wall_straight" ),
		( 40, -52, -10, "wall_straight" ), ( -22, -3, 90, "wall_low" ), ( 46, 4, 90, "wall_low" ), ( 108, 0, 0, "wall_low" ) ):
		put( n, x, y, yaw )
	# vaporators on the outskirts, crates by doors
	for x, y in ( ( -100, 40 ), ( 110, 36 ), ( 100, -46 ), ( -96, -50 ), ( 20, 60 ), ( -10, -62 ) ):
		put( "vaporator", x, y, 0 )
	for x, y in ( ( -64, 10 ), ( 72, -10 ), ( 24, 12 ), ( -38, -12 ) ):
		put( "crates", x, y, ( x * 7 ) % 360 )
	return out


def site( ground, centre, m ):
	cx, cy = centre
	out = []
	# the approach comes from the south-west (the town): "front" is that direction
	front = math.radians( -135 )
	fx, fy = math.cos( front ), math.sin( front )
	rx, ry = -fy, fx          # to the right when facing the front

	def at( f, r ):
		return cx + fx * f + rx * r, cy + fy * f + ry * r

	def put( name, f, r, yaw_off=0.0, group="site", sink=0.1 ):
		x, y = at( f, r )
		out.append( { "Model": T + name + ".vmdl" if group == "site" else name, "X": round( x * m, 1 ), "Y": round( y * m, 1 ),
			"Z": round( ( ground( x, y ) - sink ) * m, 1 ), "Yaw": round( math.degrees( front ) + yaw_off, 1 ), "Scale": 1.0, "Group": group } )

	put( "cis_bunker", 0, 0 )                         # door faces the approach
	put( "cis_pad", -32, 8, 0, sink=0.0 )
	put( "cis_mast", -18, -16 )
	put( "cis_tower", 14, -22 )
	put( "cis_tower", 12, 24 )
	# perimeter wall panels with gaps (attackers can flank through them)
	for f, r, yaw in ( ( 22, -12, 90 ), ( 22, 10, 90 ), ( 6, -30, 0 ), ( -8, -32, 0 ), ( 4, 30, 0 ), ( -10, 32, 0 ), ( -28, -26, 30 ), ( -26, 26, -30 ) ):
		put( "cis_wall", f, r, yaw )
	# forward barricades and crates: defenders' first line, attackers' cover once taken
	for f, r in ( ( 34, -18 ), ( 38, 0 ), ( 34, 18 ), ( 48, -8 ), ( 50, 12 ) ):
		put( "cis_barricade", f, r )
	for f, r in ( ( 26, -2 ), ( 10, -14 ), ( 8, 14 ), ( -20, 0 ) ):
		put( "crates", f, r, 37 * f )
	# the approach (40-100 m out): fallen rock, a wrecked supply crawler's crates and old adobe ruins, so attackers can
	# bound from cover to cover (a trace check at crouch height found half the approach open to four or more droids)
	for f, r, name in ( ( 58, -26, "rock:boulder_c" ), ( 64, 6, "rock:slab_a" ), ( 72, 30, "rock:boulder_b" ), ( 82, -10, "wall_low" ),
		( 90, 18, "rock:boulder_c" ), ( 70, -40, "wall_straight" ), ( 96, -30, "rock:slab_b" ), ( 54, 34, "crates" ), ( 46, -6, "cis_barricade" ) ):
		x, y = at( f, r )
		model = "models/tatooine/" + name[5:] + ".vmdl" if name.startswith( "rock:" ) else T + name + ".vmdl"
		out.append( { "Model": model, "X": round( x * m, 1 ), "Y": round( y * m, 1 ), "Z": round( ( ground( x, y ) - 0.3 ) * m, 1 ),
			"Yaw": round( math.degrees( front ) + 90 + ( f * 13 ) % 40, 1 ), "Scale": 1.4 if name.startswith( "rock:" ) else 1.0, "Group": "town" } )
	# the garrison (spawned by /gm outpost): droids behind the barricades, in the towers' lee and round the bunker
	for f, r, kind in ( ( 30, -10, "b1" ), ( 32, 8, "b1" ), ( 28, 22, "b1" ), ( 16, -4, "b1" ), ( 14, 18, "b1" ), ( 4, -20, "b1" ), ( -4, 20, "b1" ),
		( 10, 0, "b2" ), ( -12, -6, "b2" ) ):
		put( kind, f, r, group="spawn" )
	return out
