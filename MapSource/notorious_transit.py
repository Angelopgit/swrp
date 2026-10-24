"""
Turbolifts and teleporters of the Notorious Venator, for the SwrpTurbolift / SwrpTeleporter components.

Turbolifts: the VMF has two lift shafts behind the main corridor (x = +300 and x = -300) with a sliding door on every
deck it serves ("elevator1_ex_N" / "elevator_ex_N"), a call button beside each door and a moving car (func_movelinear).
The port keeps every shaft door as a lift door and gives each deck its own copy of the car, so riding is a short trip in a
sealed car: doors close, the car "moves", everyone aboard arrives in the same spot on the chosen deck. The cars' own inner
doors (elevN_int) are left out, the shaft doors do that job.

Teleporters: every trigger_teleport outside the 3D skybox with its info_teleport_destination points (the bridge lifts, the
simulation rooms and the Jedi temple, which sit far above the ship).

    import notorious_transit as nt; nt.export( root, out_json )   (called by notorious_build.py; plain Python, no bpy)
"""
import re
import json
import vmf_parse as vp
import regions
import notorious_doors as nd

# lift car templates (func_movelinear) per shaft, and their sibling copies the VMF parks on other decks (dropped)
CAR_TEMPLATES = { "elev2": "starboard", "elev3": "port" }
CAR_DUPLICATES = ( "elev4", "elev5" )
SHAFT_DOOR = re.compile( r"^elevator1?_ex_\d+$" )
CAR_DOOR = re.compile( r"^elev\d+_int$" )
DECKS = { -1868: "Engineering Deck", -1222: "Main Deck", -818: "Operations Deck", -206: "Command Deck", 224: "Jedi Temple" }


def bounds( solids ):
	pts = [p for so in solids for f in vp.solid_faces( so ) for p in f["verts"]]
	return [min( p[i] for p in pts ) for i in range( 3 )], [max( p[i] for p in pts ) for i in range( 3 )]


def deck_name( z ):
	best = min( DECKS, key=lambda k: abs( k - z ) )
	return DECKS[best] if abs( best - z ) < 80 else f"Deck {round( z )}"


def shafts( root ):
	"""[{ id, name, car: { template, floor_z }, floors: [{ z, name, door, door_center, panel }] }]"""
	ents = vp.find( root, "entity" )
	cars = {}
	for e in ents:
		tn = e[1].get( "targetname", "" )
		if e[1].get( "classname" ) == "func_movelinear" and tn in CAR_TEMPLATES:
			sol = vp.find( e[2], "solid" )
			lo, hi = bounds( sol )
			cars[CAR_TEMPLATES[tn]] = { "template": tn, "lo": lo, "hi": hi, "index": nd.SolidIndex( sol ) }
	doors = {}
	for e in ents:
		tn = e[1].get( "targetname", "" )
		if e[1].get( "classname" ) == "func_door" and SHAFT_DOOR.match( tn ):
			lo, hi = bounds( vp.find( e[2], "solid" ) )
			cx = ( lo[0] + hi[0] ) / 2
			if regions.area_of( lo ) != "ship" or not 150 < abs( cx ) < 450:
				continue          # the temple's "lift" door is a plain door with the return teleporter in front of it
			side = "starboard" if ( lo[0] + hi[0] ) / 2 > 0 else "port"
			doors.setdefault( side, [] ).append( ( tn, lo, hi ) )
	buttons = []
	for e in ents:
		if e[1].get( "classname" ) == "func_button" and re.match( r"^elev\d+_button_\d+$", e[1].get( "targetname", "" ) ):
			lo, hi = bounds( vp.find( e[2], "solid" ) )
			buttons.append( [( lo[i] + hi[i] ) / 2 for i in range( 3 )] )

	out = []
	for side, ds in sorted( doors.items() ):
		car = cars.get( side )
		if car is None:
			continue
		car_floor = car["lo"][2] + 6.0                     # the car's deck plate is 6 units thick
		cx = ( car["lo"][0] + car["hi"][0] ) / 2
		sign = 1 if cx > 0 else -1
		# the back wall's inner face (the car is open towards the shaft door at its low-y side); the deck panel hangs on it
		# beside the wall's centre seam, facing the door
		px = cx + 40 * sign
		back = car["hi"][1]
		y = ( car["lo"][1] + car["hi"][1] ) / 2
		while y < car["hi"][1]:
			if car["index"].inside( ( px, y, car_floor + 52 ) ):
				back = y
				break
			y += 0.5
		inside = { "lo": [car["lo"][0] + 10, car["lo"][1] + 4, car["lo"][2]], "hi": [car["hi"][0] - 10, back, car["hi"][2]] }
		panel_in = { "pos": [px, back - 1.0, car_floor + 52], "yaw": 270.0 }
		floors = []
		for tn, lo, hi in sorted( ds, key=lambda d: d[1][2] ):
			z = lo[2]
			center = [( lo[i] + hi[i] ) / 2 for i in range( 3 )]
			# call panel: the VMF's button beside this door if there is one, else the same spot by symmetry
			near = [b for b in buttons if abs( b[2] - ( z + 57 ) ) < 40 and abs( b[0] - center[0] ) < 140]
			panel = near[0] if near else [center[0] - 92 * ( 1 if center[0] > 0 else -1 ), lo[1] - 26, z + 57]
			floors.append( { "z": z, "name": deck_name( z ), "door": tn, "door_center": center, "panel": panel, "car_offset": z - car_floor } )
		out.append( {
			"id": side, "name": f"{side.title()} Turbolift",
			"car": { "template": car["template"], "lo": car["lo"], "hi": car["hi"], "floor_z": car_floor, "inside": inside, "panel": panel_in },
			"floors": floors,
		} )
	return out


def teleporters( root ):
	ents = vp.find( root, "entity" )
	dests = {}
	for e in ents:
		if e[1].get( "classname" ) == "info_teleport_destination":
			o = [float( x ) for x in e[1]["origin"].split()]
			if regions.in_skybox( o ):
				continue
			yaw = float( e[1].get( "angles", "0 0 0" ).split()[1] )
			dests.setdefault( e[1].get( "targetname", "" ), [] ).append( { "x": o[0], "y": o[1], "z": o[2], "yaw": yaw } )
	out = []
	for e in ents:
		if e[1].get( "classname" ) != "trigger_teleport":
			continue
		sol = vp.find( e[2], "solid" )
		if not sol:
			continue
		lo, hi = bounds( sol )
		c = [( lo[i] + hi[i] ) / 2 for i in range( 3 )]
		target = e[1].get( "target", "" )
		if regions.in_skybox( c ) or target not in dests:
			continue
		names = { "to_bridge1": "Port Bridge Lift", "to_bridge2": "Starboard Bridge Lift", "from_bridge1": "Bridge Lift Down",
			"to_simroom1": "Simulation Room 1", "to_simroom2": "Simulation Room 2", "from_simroom1": "Simulation Exit",
			"from_simroom2": "Simulation Exit", "to_jedi1": "Jedi Temple", "from_jedi1": "Jedi Temple Exit" }
		out.append( { "name": names.get( target, target ), "target": target, "lo": lo, "hi": hi, "area": regions.area_of( c ) } )
	return out, dests


def export( root, path ):
	lifts = shafts( root )
	tps, dests = teleporters( root )
	with open( path, "w", newline="\n" ) as fh:
		json.dump( { "lifts": lifts, "teleporters": tps, "destinations": dests }, fh, indent=1 )
	return lifts, tps
