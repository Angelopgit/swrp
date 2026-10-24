"""
Where things are in the Notorious VMF. The playable ship sits around the origin; far above it are two more playable areas
the map reaches by teleporter (the Jedi temple and the simulation rooms), and the 3D skybox (miniature ships and planets,
scaled 1/128 around the sky_camera at 0 32 14592). Only the skybox is left out of the port.
"""

# The skybox: the sky_camera's miniatures and the shell around them, plus the huge floor plane at z 12288 (centre at 0,0).
SKYBOX_MIN = ( -4700.0, -4700.0, 12000.0 )
SKYBOX_MAX = ( 4700.0, 4700.0, 16000.0 )

# Playable areas (padded bounds of the brush clusters the teleport destinations sit in).
SHIP = ( ( -6100.0, -12020.0, -2900.0 ), ( 6100.0, 12030.0, 3430.0 ) )
JEDI_TEMPLE = ( ( 6250.0, -14300.0, 13350.0 ), ( 14320.0, -3350.0, 15050.0 ) )
SIM_ROOMS = ( ( -13100.0, 2960.0, 13400.0 ), ( -6450.0, 11500.0, 15260.0 ) )
PLAYABLE = { "ship": SHIP, "jedi_temple": JEDI_TEMPLE, "sim_rooms": SIM_ROOMS }


def inside( p, box ):
	lo, hi = box
	return all( lo[i] <= p[i] <= hi[i] for i in range( 3 ) )


def in_skybox( p ):
	return inside( p, ( SKYBOX_MIN, SKYBOX_MAX ) )


def area_of( p ):
	"""'ship', 'jedi_temple', 'sim_rooms' or None."""
	for name, box in PLAYABLE.items():
		if inside( p, box ):
			return name
	return None
