"""
Security doors for the Notorious port: picks which VMF doors become working SwrpDoor objects, gives each one an access rule,
places E-key panels beside them, finds the hangar bay console and the real brig cells.

Used by notorious_build.py (pure Python, no bpy). Writes:
    Assets/data/notorious_doors.json   door geometry (triangles with UVs, per part), rules, panels, console; read by the game and
                                       by notorious_scene.py
    MapSource/doors_review.md          the list of doorways that got doors, for review

Edit RULES below to change names or access levels, then rebuild (notorious_build.py, then notorious_scene.py).
"""
import os
import re
import math
import json

import vmf_parse as vp
import regions

HERE = os.path.dirname( os.path.abspath( __file__ ) )
PROJECT = os.path.dirname( HERE )
OUT_JSON = os.path.join( PROJECT, "Assets", "data", "notorious_doors.json" )
OUT_REVIEW = os.path.join( HERE, "doors_review.md" )


# Rank ladder indices (Code/Data/Factions.cs): 0 Cadet, 1 Private, 2 PFC, 3 Corporal, 4 Sergeant, 5 Staff Sergeant,
# 6 2nd Lieutenant, 7 1st Lieutenant, 8 Captain, 9 Major, 10 Commander.
CAPTAIN = 8

# (regex on the VMF targetname, display name, area, required rank, auto-close seconds). First match wins.
RULES = [
	( r"^cell(\d+)_gate$", "Brig Cell {0}", "brig cell", CAPTAIN, 0 ),
	( r"^medical_quar(\d+)$", "Quarantine Cell {0}", "medbay", 0, 0 ),
	( r"^brig_ray_(\d+)$", "Brig Entrance Shield {0}", "brig", CAPTAIN, 0 ),
	( r"^brig_fightcage(\d+)$", "Fight Cage Shield {0}", "brig", CAPTAIN, 0 ),
	( r"^(brig_door\d|brigdoor_bot|brig_door_top)$", "Brig", "brig", 4, 6 ),
	( r"^brig_smalldoor", "Brig Office", "brig", 4, 6 ),
	( r"^bullpen_", "Brig Bullpen", "brig", 4, 6 ),
	( r"^interro_", "Interrogation", "brig", 6, 6 ),
	( r"^database_", "Brig Records", "brig", 6, 6 ),
	( r"armory", "Armory", "armory", 3, 6 ),
	( r"^bridge_office", "Bridge Office", "command", CAPTAIN, 6 ),
	( r"^bridgecontrols_", "Bridge Control Room", "command", 6, 6 ),
	( r"^bridge_", "Bridge", "command", 6, 6 ),
	( r"^highcommandbunks", "High Command Quarters", "command", CAPTAIN, 6 ),
	( r"^highcommandoffice", "High Command Office", "command", CAPTAIN, 6 ),
	( r"^door_engineroom$", "Engine Room", "engineering", 4, 6 ),
	( r"^door_shieldgen$", "Shield Generator", "engineering", 4, 6 ),
	( r"^door_contrafuge$", "Centrifuge", "engineering", 4, 6 ),
	( r"^door_storageroom$", "Storage", "engineering", 0, 6 ),
	( r"^door_shitterfuck$", "Maintenance", "engineering", 0, 6 ),
	( r"^turret_door", "Turret Access", "hangar", 3, 6 ),
	( r"^(hangardoor_|hangar_|sidehangar_)", "Hangar", "hangar", 0, 6 ),
	( r"^(medbay|med_smalldoor|medbaycold)", "Medbay", "medbay", 0, 6 ),
	( r"^quar_", "Quarantine", "medbay", 0, 6 ),
	( r"^bunk", "Barracks", "barracks", 0, 6 ),
	( r"^cadet_room$", "Cadet Quarters", "barracks", 0, 6 ),
	( r"^cantina", "Cantina", "crew", 0, 6 ),
	( r"^theater", "Theater", "crew", 0, 6 ),
	( r"^bathroom", "Washroom", "crew", 0, 6 ),
	( r"^briefingdoor", "Briefing Room", "crew", 0, 6 ),
	( r"^office_lowlevel", "Office", "crew", 0, 6 ),
	( r"^(side_door|side_room|maindoor|ladder_floor)", "Corridor", "corridor", 0, 6 ),
	( r"^elevator1?_ex_\d+_jedi_temple$", "Temple Lift", "temple", 0, 5 ),
	( r"_sim_rooms$", "Simulation Deck", "training", 0, 6 ),
	( r"^elevator1?_ex_", "Turbolift", "turbolift", 0, 5 ),
	( r".*", "Door", "corridor", 0, 6 ),
]

# The big port / starboard bay doors only move from the hangar console.
HANGAR_BAY = { "port_hangerdoor": "Port Hangar Bay Doors", "port_hangerdoor2": "Starboard Hangar Bay Doors" }
# The dorsal roof over the main hangar: two halves that slide apart so craft fly straight up and out. Console-only like the
# bay doors, but on their own button in the hangar control room (Code/Map/SwrpRoofDoors.cs keeps them in step with
# SwrpShip.RoofOpen).
TOPSIDE = { "roofdoor": "Topside Hangar Doors" }

# VMF doors that stay part of the static map: vents and hatches (closed), the dorsal roof is a working door now
# (TOPSIDE) and the lower hangar slides (left open, as before). Lift shaft doors are working turbolift doors.
BAKED_OPEN = ( r"^lowerh_", )
AREA_SUFFIXES = ( "_jedi_temple", "_sim_rooms" )

# The brig's blue shields: the cell gates, the two entrance rays and the three fight-cage walls. They are switched from the
# brig tower's buttons and the shield control in the brig's first room (the cell gates also from a panel outside each cell),
# by Captains and up or the Coruscant Guard (the ship's shock troopers / military police).
BRIG_SHIELDS = r"^(cell\d+_gate|brig_ray_\d+|brig_fightcage\d+)$"
# The medbay's two quarantine cells: blue fields across each cell's doorway, switched from a panel on the corridor side
# (Angelo, 2026-10-13: "no button to power down quarantine cells"; the map left them as solid static brushes).
QUAR_SHIELDS = r"^medical_quar\d+$"
QUAR_PANELS = { "medical_quar1": { "pos": [-2352.0, 5955.0, -1168.0], "yaw": 0.0 }, "medical_quar2": { "pos": [-1874.0, 5955.0, -1168.0], "yaw": 180.0 } }
QUAR_ROOMS = { "medical_quar1": -2574.0, "medical_quar2": -1652.0 }      # x of each cell's middle (the quar_N_heal volumes)
BRIG_CONSOLE_ONLY = r"^(brig_ray_\d+|brig_fightcage\d+)$"
BRIG_GUARD = "coruscant_guard"
LIFT_CAR_DOORS = r"^elev\d+_int$"      # the cars' own doors: the shaft doors (managed, see RULES) do the job instead
BAKED_CLOSED = ( r"^vent", )


def fwd( angles ):
	pch, yaw = [math.radians( float( x ) ) for x in angles.split()[:2]]
	return ( math.cos( pch ) * math.cos( yaw ), math.cos( pch ) * math.sin( yaw ), -math.sin( pch ) )


def bounds( solids ):
	pts = [p for so in solids for f in vp.solid_faces( so ) for p in f["verts"]]
	return [min( p[i] for p in pts ) for i in range( 3 )], [max( p[i] for p in pts ) for i in range( 3 )]


def door_policy( e ):
	"""'managed', 'open', 'closed' or 'skip' for a func_door entity (and for the brig cell gate brushes)."""
	name = e[1].get( "targetname", "" )
	sol = vp.find( e[2], "solid" )
	if not sol:
		return "skip"
	lo, hi = bounds( sol )
	if regions.in_skybox( [( lo[i] + hi[i] ) / 2 for i in range( 3 )] ):
		return "skip"
	if e[1].get( "classname" ) == "func_brush":
		return "managed" if re.match( BRIG_SHIELDS, name ) or re.match( QUAR_SHIELDS, name ) else None
	if re.match( LIFT_CAR_DOORS, name ):
		return "skip"
	if any( re.match( r, name ) for r in BAKED_OPEN ):
		return "open"
	if any( re.match( r, name ) for r in BAKED_CLOSED ):
		return "closed"
	if not name and hi[2] - lo[2] < 60:
		return "closed"      # unnamed floor hatches
	return "managed"


def move_for( e, lo, hi ):
	if e[1].get( "classname" ) == "func_brush":
		return ( 0.0, 0.0, ( hi[2] - lo[2] ) - 2.0 )         # cell gates slide up into the frame
	d = fwd( e[1].get( "movedir", "0 0 0" ) )
	dist = sum( abs( d[i] ) * ( hi[i] - lo[i] ) for i in range( 3 ) ) - float( e[1].get( "lip", "0" ) )
	return ( d[0] * dist, d[1] * dist, d[2] * dist )


# ---------------------------------------------------------------------------------------------------------------------
# Point-in-brush tests for panel placement
# ---------------------------------------------------------------------------------------------------------------------

class SolidIndex:
	def __init__( self, solids ):
		self.cells = {}
		self.items = []
		for so in solids:
			planes = [vp.side_plane( s[1] ) for s in vp.find( so[2], "side" )]
			planes = [p for p in planes if p]
			faces = vp.solid_faces( so )
			if not faces or not planes:
				continue
			pts = [p for f in faces for p in f["verts"]]
			lo = [min( p[i] for p in pts ) for i in range( 3 )]
			hi = [max( p[i] for p in pts ) for i in range( 3 )]
			if regions.in_skybox( [( lo[i] + hi[i] ) / 2 for i in range( 3 )] ):
				continue
			idx = len( self.items )
			self.items.append( ( lo, hi, planes ) )
			for gx in range( int( lo[0] // 256 ), int( hi[0] // 256 ) + 1 ):
				for gy in range( int( lo[1] // 256 ), int( hi[1] // 256 ) + 1 ):
					self.cells.setdefault( ( gx, gy ), [] ).append( idx )

	def inside( self, p ):
		for idx in self.cells.get( ( int( p[0] // 256 ), int( p[1] // 256 ) ), () ):
			lo, hi, planes = self.items[idx]
			if not all( lo[i] - 0.5 <= p[i] <= hi[i] + 0.5 for i in range( 3 ) ):
				continue
			if all( n[0] * p[0] + n[1] * p[1] + n[2] * p[2] - d <= 0.01 for ( n, d ) in planes ):
				return True
		return False


def panel_spots( index, lo, hi ):
	"""Wall spots beside a door, one per side of the doorway where there is wall: [{pos, yaw}]."""
	size = [hi[i] - lo[i] for i in range( 3 )]
	wide = 0 if size[0] >= size[1] else 1
	nrm = 1 - wide
	c = [( lo[i] + hi[i] ) / 2 for i in range( 3 )]
	out = []
	for s in ( 1, -1 ):
		placed = False
		for lat in ( 1, -1 ):
			for extra in ( 16, 28 ):
				p = list( c )
				p[wide] += lat * ( size[wide] / 2 + extra )
				p[2] = lo[2] + 52
				if not index.inside( p ):
					continue
				t = 0.0
				q = list( p )
				while t < 48 and index.inside( q ):
					t += 1.0
					q = list( p )
					q[nrm] += s * t
				if t >= 48:
					continue
				q[nrm] = p[nrm] + s * ( t + 1.0 )
				n = [0.0, 0.0]
				n[nrm] = float( s )
				out.append( { "pos": [round( v, 2 ) for v in q], "yaw": round( math.degrees( math.atan2( n[1], n[0] ) ), 1 ) } )
				placed = True
				break
			if placed:
				break
	return out


# ---------------------------------------------------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------------------------------------------------

def export( root, static_solids, face_material, uv_for ):
	"""
	face_material(face) -> (vmat path, uv scale) or None for faces that are not drawn. uv_for(verts, normal, scale) -> UVs, the same
	projection the static map uses.
	"""
	ents = vp.find( root, "entity" )
	index = SolidIndex( static_solids )

	groups = {}
	for e in ents:
		if e[1].get( "classname" ) not in ( "func_door", "func_brush" ):
			continue
		if door_policy( e ) != "managed":
			continue
		# The Jedi temple and simulation rooms reuse some of the ship's door names; keep their doors apart. Ship door ids stay
		# exactly as before those areas were ported (unnamed ones are numbered among ship doors only), since /lock saves use them.
		lo, hi = bounds( vp.find( e[2], "solid" ) )
		area = regions.area_of( [( lo[i] + hi[i] ) / 2 for i in range( 3 )] )
		suffix = f"_{area}" if area not in ( None, "ship" ) else ""
		name = e[1].get( "targetname", "" )
		if not name:
			name = f"door_{len( groups )}" if suffix else f"door_{sum( 1 for k in groups if not k.endswith( AREA_SUFFIXES ) )}"
		groups.setdefault( name + suffix, [] ).append( e )

	# The real brig cells: the cell trigger volumes (the cell_N sprites are a status display on one console).
	cells = {}
	for e in ents:
		m = re.match( r"^cell_(\d+)_trigger$", e[1].get( "targetname", "" ) )
		if m and e[1].get( "classname" ) == "trigger_multiple":
			lo, hi = bounds( vp.find( e[2], "solid" ) )
			cells[int( m.group( 1 ) )] = { "x": round( ( lo[0] + hi[0] ) / 2, 1 ), "y": round( ( lo[1] + hi[1] ) / 2, 1 ), "z": round( lo[2] + 2, 1 ) }

	doors = []
	for name, es in sorted( groups.items() ):
		parts = []
		all_lo, all_hi = [1e9] * 3, [-1e9] * 3
		for e in es:
			sol = vp.find( e[2], "solid" )
			lo, hi = bounds( sol )
			all_lo = [min( all_lo[i], lo[i] ) for i in range( 3 )]
			all_hi = [max( all_hi[i], hi[i] ) for i in range( 3 )]
			parts.append( ( e, sol, lo, hi ) )
		center = [( all_lo[i] + all_hi[i] ) / 2 for i in range( 3 )]

		out_parts = []
		for ( e, sol, lo, hi ) in parts:
			meshes = {}
			for so in sol:
				for f in vp.solid_faces( so ):
					fm = face_material( f )
					if not fm:
						continue
					mat, scale = fm
					# the topside doors are red on the outside of the hull too, not just from inside the hangar (Angelo, 2026-10-12)
					if name in TOPSIDE and f["normal"][2] < -0.5:
						red = next( ( face_material( g ) for g in vp.solid_faces( so ) if g["normal"][2] > 0.5 and face_material( g ) ), None )
						if red:
							mat, scale = red
					# ...but grey hull panelling on the inside, seen looking up from the hangar (Angelo, 2026-10-13: the red inside
					# "looks off"): a flat face below the slab's middle takes the material of one of its side faces
					if name in TOPSIDE and abs( f["normal"][2] ) > 0.5:
						fz = sum( v[2] for v in f["verts"] ) / len( f["verts"] )
						if fz < ( lo[2] + hi[2] ) / 2:
							side = next( ( face_material( g ) for g in vp.solid_faces( so ) if abs( g["normal"][2] ) < 0.5 and face_material( g ) ), None )
							if side:
								mat, scale = side
					uvs = uv_for( f["verts"], f["normal"], scale )
					vs = f["verts"]
					tri = meshes.setdefault( mat, [] )
					ax = f["uaxis"][0] if f.get( "uaxis" ) else ( 1.0, 0.0, 0.0 )
					for k in range( 1, len( vs ) - 1 ):
						for j in ( 0, k, k + 1 ):
							p = vs[j]
							tri += [round( p[0] - center[0], 3 ), round( p[1] - center[1], 3 ), round( p[2] - center[2], 3 ),
									round( f["normal"][0], 4 ), round( f["normal"][1], 4 ), round( f["normal"][2], 4 ),
									round( uvs[j][0], 5 ), round( -uvs[j][1], 5 ), round( ax[0], 4 ), round( ax[1], 4 ), round( ax[2], 4 )]
			out_parts.append( {
				"move": [round( v, 2 ) for v in move_for( e, lo, hi )],
				"lo": [round( lo[i] - center[i], 2 ) for i in range( 3 )],
				"hi": [round( hi[i] - center[i], 2 ) for i in range( 3 )],
				"meshes": meshes,
			} )

		rule = next( r for r in RULES if re.search( r[0], name ) )
		m = re.search( rule[0], name )
		display = rule[1].format( *m.groups() ) if m and m.groups() else rule[1]
		console = name in HANGAR_BAY or name in TOPSIDE or bool( re.match( BRIG_CONSOLE_ONLY, name ) )
		panels = [] if console else panel_spots( index, all_lo, all_hi )
		cm = re.match( r"^cell(\d+)_gate$", name )
		if cm and int( cm.group( 1 ) ) in cells:
			# only the panel in the corridor: the one on the cell's side would let the prisoner see (and press) it
			c = cells[int( cm.group( 1 ) )]
			panels = [p for p in panels if ( p["pos"][0] - center[0] ) * ( c["x"] - center[0] ) + ( p["pos"][1] - center[1] ) * ( c["y"] - center[1] ) < 0]
		if name in QUAR_ROOMS:
			# only the corridor-side panel: a patient inside mustn't be able to switch their own field off
			panels = [p for p in panels if ( p["pos"][0] - center[0] ) * ( QUAR_ROOMS[name] - center[0] ) < 0]
			if not panels:
				# the walls beside these doorways are props, not brushes, so panel_spots finds nothing: the corridor-side wall
				# face (traced in the editor, 2026-10-13) just past the doorway's near end
				panels = [QUAR_PANELS[name]]
		door = {
			"id": name,
			"name": HANGAR_BAY.get( name, TOPSIDE.get( name, display ) ),
			"area": "hangar bay" if name in HANGAR_BAY or name in TOPSIDE else rule[2],
			"rank": 0 if name in HANGAR_BAY or name in TOPSIDE else rule[3],
			"autoClose": 0 if console else rule[4],
			"console": console,
			"orFaction": BRIG_GUARD if re.match( BRIG_SHIELDS, name ) else "",
			"center": [round( v, 2 ) for v in center],
			"size": [round( all_hi[i] - all_lo[i], 1 ) for i in range( 3 )],
			"parts": out_parts,
			"panels": panels,
		}
		doors.append( door )

	# The hangar console: the VMF button that drives the bay doors.
	console = None
	for e in ents:
		if e[1].get( "classname" ) != "func_button":
			continue
		con = vp.find( e[2], "connections" )
		if not con:
			continue
		if not any( re.match( r"port_hangerdoor", v.replace( "\x1b", "," ).split( "," )[0] ) for v in con[0][1].values() ):
			continue
		lo, hi = bounds( vp.find( e[2], "solid" ) )
		console = {
			"center": [round( ( lo[i] + hi[i] ) / 2, 2 ) for i in range( 3 )],
			"size": [round( max( 6.0, hi[i] - lo[i] + 4 ), 1 ) for i in range( 3 )],
			"doors": sorted( HANGAR_BAY ),
		}
		break

	# The brig's shield buttons (the tower's per-cell buttons light a cellon_N sprite in the VMF; here they switch cell N's
	# shield), the entrance-ray and fight-cage buttons, and the shield control console ring in the brig's first room.
	brig_buttons = []
	for e in ents:
		if e[1].get( "classname" ) != "func_button":
			continue
		con = vp.find( e[2], "connections" )
		if not con:
			continue
		targets = []
		for v in con[0][1].values():
			t = v.replace( "\x1b", "," ).split( "," )[0]
			m = re.match( r"^cellon_(\d+)_sprite$", t )
			if m:
				targets.append( f"cell{m.group( 1 )}_gate" )
			elif re.match( BRIG_CONSOLE_ONLY, t ):
				targets.append( t )
		if not targets:
			continue
		lo, hi = bounds( vp.find( e[2], "solid" ) )
		brig_buttons.append( {
			"center": [round( ( lo[i] + hi[i] ) / 2, 2 ) for i in range( 3 )],
			"size": [round( max( 6.0, hi[i] - lo[i] + 4 ), 1 ) for i in range( 3 )],
			"doors": sorted( set( targets ) ),
		} )
	# the console ring prop in the brig's first room (the map uses the same model elsewhere: take the one nearest the brig
	# entrance shield)
	brig_control = None
	ray = next( ( d["center"] for d in doors if d["id"] == "brig_ray_1" ), None )
	rings = [[float( x ) for x in e[1]["origin"].split()] for e in ents
		if e[1].get( "classname", "" ).startswith( "prop_" ) and e[1].get( "model", "" ).endswith( "anaxes_brig.mdl" )]
	if rings and ray:
		brig_control = [round( v, 1 ) for v in min( rings, key=lambda o: sum( ( o[i] - ray[i] ) ** 2 for i in range( 3 ) ) )]

	os.makedirs( os.path.dirname( OUT_JSON ), exist_ok=True )
	with open( OUT_JSON, "w", newline="\n" ) as fh:
		json.dump( { "doors": doors, "console": console, "brig_buttons": brig_buttons, "brig_control": brig_control, "brig_guard": BRIG_GUARD }, fh, separators=( ",", ":" ) )

	write_review( doors, console )
	return doors, console, [cells[k] for k in sorted( cells )]


def write_review( doors, console ):
	ranks = ["Anyone", "Private+", "PFC+", "Corporal+", "Sergeant+", "Staff Sergeant+", "2nd Lieutenant+", "1st Lieutenant+", "Captain+", "Major+", "Commander"]
	lines = [
		"# Notorious doors",
		"",
		"Generated by `MapSource/notorious_doors.py` (via `notorious_build.py`). Edit `RULES` there to rename doors or change access,",
		"then rebuild. Staff can open every door. Captain+ and staff can `/lock` and `/unlock`.",
		"",
		f"{len( doors )} working doors. Hangar console: {'at ' + str( console['center'] ) if console else 'not found'}.",
		"",
		"| VMF name | Door | Area | Access | Auto-close | Panels | Position |",
		"|---|---|---|---|---|---|---|",
	]
	for d in sorted( doors, key=lambda d: ( d["area"], d["name"], d["id"] ) ):
		access = "Hangar console" if d["console"] else ranks[d["rank"]]
		auto = "stays open" if not d["autoClose"] else f"{d['autoClose']} s"
		c = d["center"]
		lines.append( f"| `{d['id']}` | {d['name']} | {d['area']} | {access} | {auto} | {len( d['panels'] )} | {c[0]:.0f}, {c[1]:.0f}, {c[2]:.0f} |" )
	lines += [
		"",
		"Left as part of the static map: elevator doors (open, no lift logic yet), vents and floor hatches (closed), the dorsal roof",
		"(closed), the lower hangar slides (open), and everything in the 3D skybox area.",
	]
	with open( OUT_REVIEW, "w", newline="\n", encoding="utf-8" ) as fh:
		fh.write( "\n".join( lines ) + "\n" )
