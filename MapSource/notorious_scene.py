"""
Builds Assets/scenes/notorious.scene from the converted Notorious map: the map model, its prop and corridor-kit chunks, spawn
points, a clustered light set and the brig's jail points, on top of the gamemode objects (UI, camera and effects, scene
settings) taken from the previous scene.

    python MapSource/notorious_scene.py

Reads MapSource/notorious_entities.json (written by notorious_build.py) and Assets/scenes/venator.scene or, once that is gone,
an existing notorious.scene as the template for the gamemode objects.
"""
import json
import os
import uuid
import copy

HERE = os.path.dirname( os.path.abspath( __file__ ) )
PROJECT = os.path.dirname( HERE )
SCENES = os.path.join( PROJECT, "Assets", "scenes" )
OUT = os.path.join( SCENES, "notorious.scene" )

KEEP = ( "Scene Information", "SWRP Settings", "Space", "UI", "Main Camera", "Perf Probe" )


def guid():
	return str( uuid.uuid4() )


def load( path ):
	return json.load( open( path, encoding="utf-8-sig" ) )


def find( objs, name ):
	for g in objs:
		if g["Name"] == name:
			return g
		r = find( g.get( "Children", [] ), name )
		if r:
			return r
	return None


def reguid( o ):
	"""Fresh ids everywhere, so copies never collide."""
	o["__guid"] = guid()
	for c in o.get( "Components", [] ):
		c["__guid"] = guid()
	for ch in o.get( "Children", [] ):
		reguid( ch )


def make( name, pos="0,0,0", rot="0,0,0,1", components=None, children=None ):
	return {
		"__guid": guid(), "__version": 2, "Flags": 0, "Name": name, "Position": pos, "Rotation": rot, "Scale": "1,1,1", "Tags": "",
		"Enabled": True, "NetworkMode": 2, "NetworkFlags": 0, "NetworkOrphaned": 0, "NetworkTransmit": True, "OwnerTransfer": 1,
		"Components": components or [], "Children": children or [],
	}


def in_brig( p ):
	"""Inside the brig's light zone (Code/Lighting/LightZones.cs, "Brig")."""
	return 1150 <= p["x"] <= 3800 and 4200 <= p["y"] <= 6200 and -1260 <= p["z"] <= -640


def comp( typ, **props ):
	d = { "__type": typ, "__guid": guid(), "__enabled": True, "Flags": 0 }
	d.update( props )
	return d


def yaw_rotation( yaw_deg ):
	import math
	h = math.radians( yaw_deg ) / 2
	return f"0,0,{math.sin( h ):.6f},{math.cos( h ):.6f}"


BACTA_STATIONS = [
	( "Medbay Bacta Station", -1700, 5192, -1220 ),
	( "Medbay Bacta Station 2", -2300, 5198, -1220 ),
	( "Hangar Bacta Station", 400, 950, -1220 ),
	( "Barracks Bacta Station", 980, 3336, -1220 ),
	( "Barracks Bacta Station 2", -980, 3336, -1220 ),
]

MEDBAY_BEDS = [
	( -1500, 5192, -1220, 180 ),
	( -1900, 5192, -1220, 0 ),
	( -2113, 5450, -1220, 270 ),
	( -1450, 5000, -1220, 90 ),
]

# (name, VMF position of the original table prop or room centre); dropped onto the floor below at build time.
HOLO_TABLES = [
	( "War Room", 0.0, 2385.0, -400.0 ),
	( "Bridge", -659.0, 4620.0, 3260.0 ),
	( "Briefing Hall", 0.0, 2060.0, -1000.0 ),
]


def holo_tables():
	import sys
	sys.path.insert( 0, HERE )
	import vmf_parse as vp
	import notorious_doors as nd
	root = vp.parse( r"X:\SWRP Projects\Notorious_VMF\rp_venator_notorious_os.vmf" )
	solids = list( vp.find( vp.find( root, "world" )[0][2], "solid" ) )
	for e in vp.find( root, "entity" ):
		if e[1].get( "classname" ) == "func_detail":
			solids += vp.find( e[2], "solid" )
	index = nd.SolidIndex( solids )
	out = []
	for name, x, y, z in HOLO_TABLES:
		zz = z
		while zz > z - 600 and not index.inside( ( x, y, zz ) ):
			zz -= 2.0
		out.append( ( name, x, y, zz + 2.0 ) )
		print( f"holo-table {name}: floor at {zz + 2.0:.0f}" )
	return out


# (name, profile, min, max, priority): the areas with their own room tone. Boxes in map units; highest priority wins.
AMBIENT_ZONES = [
	( "Brig", "brig", ( 1150, 4200, -1260 ), ( 3800, 6200, -640 ), 3 ),
	( "Medbay", "medbay", ( -2500, 4800, -1260 ), ( -1300, 5600, -1000 ), 3 ),
	( "Port Bridge", "bridge", ( -1700, 3000, 2900 ), ( -200, 6200, 3800 ), 3 ),
	( "Starboard Bridge", "bridge", ( 200, 3000, 2900 ), ( 1700, 6200, 3800 ), 3 ),
	( "Hangar", "hangar", ( -2600, -11000, -1300 ), ( 2600, 2800, 1000 ), 2 ),
	( "Engineering", "engineering", ( -6100, -12000, -2900 ), ( 6100, 12000, -1500 ), 1 ),
	( "Jedi Temple", "temple", ( 6250, -14300, 13350 ), ( 14320, -3350, 15050 ), 1 ),
]


# The living ship (see the scene above). Lights: the nearest ceiling light to each point flickers or pulses.
LIVING_LIGHTS = [
	( "flicker", ( 0, 3950, -1700 ) ),          # engineering corridor
	( "flicker", ( -800, 3900, -1700 ) ),
	( "flicker", ( -1500, 4275, -1600 ) ),      # contrafuge room
	( "flicker", ( 2200, 4700, -800 ) ),        # brig
	( "flicker", ( 0, 4300, -1000 ) ),          # barracks corridor by the turbolift
	( "pulse", ( 0, 5300, -1600 ) ),            # the engine room throbs with the reactor
	( "pulse", ( 0, 5800, -1600 ) ),
]
# (point inside the room, direction to the wall it sits on)
LIVING_STEAM = [
	( ( 0, 5300, -1830 ), ( 1, 0, 0 ) ),        # engine room walkway
	( ( 0, 5700, -1830 ), ( -1, 0, 0 ) ),
	( ( -600, 3950, -1840 ), ( 0, 1, 0 ) ),     # engineering corridor
	( ( -1500, 4275, -1800 ), ( -1, 0, 0 ) ),   # contrafuge room
]
LIVING_SPARKS = [
	( ( 500, 3950, -1800 ), ( 0, 1, 0 ) ),      # engineering corridor
	( ( 0, 5500, -1780 ), ( -1, 0, 0 ) ),       # engine room
]
# (point, direction to the wall, status | radar | graph). Each screen looks along that wall for a flat, clear stretch at
# eye height and skips the spot if there is none. The bridge has none: its wall screens are the map's own props, which
# have no collision, so a screen there would sit inside them.
LIVING_SCREENS = [
	( ( 0, 4300, -1150 ), ( -1, 0, 0 ), "status" ),       # barracks corridor
	( ( -450, 3950, -1800 ), ( 0, 1, 0 ), "graph" ),      # engineering corridor
	( ( -1800, 5200, -1150 ), ( 0, 1, 0 ), "status" ),    # medbay
	( ( 2200, 4700, -1100 ), ( 0, -1, 0 ), "radar" ),     # brig
	( ( 100, 5300, -1180 ), ( 1, 0, 0 ), "status" ),      # the deployment hall
	( ( 100, 5500, -1180 ), ( -1, 0, 0 ), "radar" ),
]
# (officer | astromech | mouse, route points (one = stands there), facing when standing)
LIVING_CREW = [
	( "officer", [( -505, 3670, 3170 )], 0 ),                                   # port bridge crew pits
	( "officer", [( -815, 3770, 3170 )], 180 ),
	( "officer", [( -659, 3560, 3240 ), ( -659, 4230, 3240 )], 0 ),           # port bridge walkway
	( "officer", [( 505, 3870, 3170 )], 180 ),                                  # starboard bridge
	( "officer", [( 659, 4230, 3240 ), ( 659, 3560, 3240 )], 0 ),
	( "astromech", [( -700, -3600, -1210 ), ( 700, -3600, -1210 ), ( 700, -2000, -1210 ), ( -700, -2000, -1210 )], 0 ),   # hangar
	( "mouse", [( 1250, -4800, -1210 ), ( 1250, -1200, -1210 )], 0 ),
	( "mouse", [( -900, 3900, -1850 ), ( 900, 3900, -1850 )], 0 ),           # engineering corridor
	( "officer", [( 0, 5650, -1850 )], 90 ),                                    # engine room, watching the reactor
	( "astromech", [( 250, 5150, -1850 )], 200 ),
	( "officer", [( -1850, 5020, -1210 )], 90 ),                                # medbay
]


# The armory room (floor -818, between armory_door and back_armorydoor): (rifles | ammo, a point in the room, direction
# to its wall). Racks hang on the long +X wall between the pillars; crates stand against it.
ARMORY = [
	( "rifles", ( 869, 4610, -760 ), ( 1, 0, 0 ) ),
	( "rifles", ( 869, 4790, -760 ), ( 1, 0, 0 ) ),
	( "ammo", ( 900, 4470, -790 ), ( 1, 0, 0 ) ),
	( "ammo", ( 900, 4865, -790 ), ( 1, 0, 0 ) ),
]


# The medbay's bacta pods: (a point in the room, direction to the wall the pod backs onto). Two either side of the main
# room, between the pillars.
MEDBAY_PODS = [
	( ( -1480, 5200, -1150 ), ( 0, -1, 0 ) ),
	( ( -1480, 5200, -1150 ), ( 0, 1, 0 ) ),
	( ( -1660, 5200, -1150 ), ( 0, -1, 0 ) ),
	( ( -1660, 5200, -1150 ), ( 0, 1, 0 ) ),
]


def brig_ring():
	"""The brig's console ring (the anaxes_brig prop nearest the brig entrance shield; the model is used elsewhere too)."""
	import sys
	sys.path.insert( 0, HERE )
	import vmf_parse as vp
	root = vp.parse( r"X:\SWRP Projects\Notorious_VMF\rp_venator_notorious_os.vmf" )
	rings = [[float( x ) for x in e[1]["origin"].split()] for e in vp.find( root, "entity" )
		if e[1].get( "classname", "" ).startswith( "prop_" ) and e[1].get( "model", "" ).endswith( "anaxes_brig.mdl" )]
	ray = ( 2028.0, 5192.0, -1157.0 )
	return min( rings, key=lambda o: sum( ( o[i] - ray[i] ) ** 2 for i in range( 3 ) ) ) if rings else None


def main():
	template = None
	for name in ( "venator.scene", "notorious.scene" ):
		p = os.path.join( SCENES, name )
		if os.path.exists( p ):
			template = load( p )
			break
	assert template, "no template scene found"

	ent = json.load( open( os.path.join( HERE, "notorious_entities.json" ) ) )
	scene = copy.deepcopy( template )
	scene["__guid"] = guid()
	scene["Title"] = "notorious"
	scene["SceneProperties"]["Metadata"] = { "Title": "notorious" }
	# NPC navigation: on, but built on demand around where NPCs are spawned (SwrpNpcDirector.EnsureNavMesh), never in the
	# editor and never for the whole ship at load. Doors are left out so doorways stay walkable (droids open doors they reach).
	nav = scene["SceneProperties"].setdefault( "NavMesh", {} )
	nav.update( { "Enabled": True, "DeferGeneration": True, "EditorAutoUpdate": False, "IncludeStaticBodies": True,
		"IncludeKeyframedBodies": False, "AgentHeight": 64, "AgentRadius": 16, "AgentStepSize": 18, "AgentMaxSlope": 40 } )

	keep = []
	for n in KEEP:
		g = find( template["GameObjects"], n )
		if g:
			keep.append( copy.deepcopy( g ) )

	objs = []
	for g in keep:
		if g["Name"] == "Space":
			if not any( c["__type"] == "Swrp.SwrpSpace" for c in g["Components"] ):
				g["Components"].append( comp( "Swrp.SwrpSpace" ) )     # sky per planet, passing ships, hyperspace
			for c in g["Components"]:
				if c["__type"].endswith( "SkyBox2D" ):
					c["SkyMaterial"] = "materials/space/sky_coruscant.vmat"
				if c["__type"].endswith( "AmbientLight" ):
					c["Color"] = "0.5,0.52,0.58,1"         # one even, slightly cool fill for the simple ship-wide lighting (Angelo, 2026-10-12)
		if g["Name"] == "UI":
			for t in ( "Swrp.InteractPrompt", "Swrp.RoleplayHud", "Swrp.MissionBoard", "Swrp.StaffPanel", "Swrp.LiftMenu", "Swrp.ShipConsoleMenu", "Swrp.GmPanel", "Swrp.BattalionMenu", "Swrp.BrigMenu", "Swrp.BuildMenu", "Swrp.ComlinkPanel", "Swrp.EmoteWheel", "Swrp.EscapeMenu" ):    # E prompt, HUDs, Tab board, deck menu, ship systems, GM, battalion
				if not any( c["__type"] == t for c in g["Components"] ):
					g["Components"].append( comp( t ) )
		if g["Name"] == "SWRP Settings":
			for c in g["Components"]:
				if "MapName" in c:
					c["MapName"] = "Venator-class Star Destroyer (Notorious)"
		if g["Name"] == "Main Camera":
			if not any( c["__type"] == "Swrp.SwrpPostFx" for c in g["Components"] ):
				g["Components"].append( comp( "Swrp.SwrpPostFx" ) )
			for c in g["Components"]:
				t = c["__type"]
				if t.endswith( "Bloom" ):
					c["Strength"] = 1.0         # SwrpPostFx holds these
					c["Threshold"] = 0.75
				elif t.endswith( "Tonemapping" ):
					c["MinimumExposure"] = 0.6        # dark rooms may stay dark (was 1.6)
					c["MaximumExposure"] = 2.2
				elif t.endswith( "ChromaticAberration" ):
					c["Scale"] = 0.015
				elif t.endswith( "FilmGrain" ):
					c["__enabled"] = False      # no film grain (Angelo, 2026-10-12)
				elif t.endswith( "Vignette" ):
					c["Intensity"] = 0.12
		if g["Name"] == "Main Camera" and ent["spawns"]:
			s = ent["spawns"][0]
			g["Position"] = f"{s['x']},{s['y']},{s['z'] + 64}"
		objs.append( g )

	# the map itself
	objs.append( make( "Notorious Venator", components=[
		comp( "Sandbox.ModelRenderer", Model="models/notorious/notorious.vmdl", BodyGroups=18446744073709551615, RenderType="On", Tint="1,1,1,1" ),
		comp( "Sandbox.ModelCollider", Model="models/notorious/notorious.vmdl", Static=True, IsTrigger=False ),
	] ) )

	spawns = [make( "Spawn", f"{s['x']},{s['y']},{s['z'] + 4}", yaw_rotation( s["yaw"] ), [comp( "Swrp.SwrpSpawnPoint", FactionId=None )] ) for s in ent["spawns"]]
	objs.append( make( "Spawns", children=spawns ) )

	lights = []
	brig_red = 0
	for i, l in enumerate( ent["lights"] ):
		e = l["energy"] * 1.7
		# near-white lamps read as bright white emitters; VMF red / blue accent tints are kept
		r, g_, b = l["r"], l["g"], l["b"]
		# every white, warm or yellow lamp the same white, so the whole ship has one colour temperature (Angelo, 2026-10-12: the
		# front half was yellow, the back white); blue and red accents are kept
		mx = max( r, g_, b )
		# white light only (Angelo, 2026-10-12): amber, yellow and orange lamps go white too
		amber = mx > 0 and ( ( min( r, g_ ) >= 0.7 * mx and b < 0.85 * mx ) or ( r >= mx and g_ >= 0.3 * r and b < 0.6 * g_ ) )
		if max( r, g_, b ) - min( r, g_, b ) < 0.25 or ( mx > 0 and r >= mx and g_ >= 0.8 * r and b >= 0.4 * r ) or ( amber and not in_brig( l ) ):
			r, g_, b = 1.0, 0.97, 0.92        # near-white lamps (the old 1.0, 0.9, 0.76 tinted white armour beige, 2026-10-12)
			e = 3.0 / 1.0       # every regular lamp the same strength too (Angelo, 2026-10-12: one general lighting)
		elif in_brig( l ) and g_ < 0.2 * r and b < 0.2 * r:
			# the brig's all-red cell lamps drowned the room (Angelo, 2026-10-12): white cell light, every third one a dim red accent
			brig_red += 1
			if brig_red % 3:
				r, g_, b = 1.0, 0.92, 0.86
			else:
				e *= 0.5
		lights.append( make( "Light", f"{l['x']:.1f},{l['y']:.1f},{l['z']:.1f}", components=[
			comp( "Sandbox.PointLight", Radius=680.0, LightColor=f"{r * e:.3f},{g_ * e:.3f},{b * e:.3f},1", Shadows=False, FogStrength=0.6, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	# the engineering bay's four ceiling lamps (VMF light, brightness 150): notorious_build keeps only the 340 heaviest lamp
	# clusters, so these single lamps were dropped and the room went dark (Angelo, 2026-10-21, loc #10)
	# and, as the original's baked light filled the whole bay from those four, a grid of the same white lamps lower down
	for x, y, z in [( -309, -591, -1529 ), ( 309, -591, -1529 ), ( 309, 79, -1529 ), ( -309, 79, -1529 )] + [( gx, gy, -1580 ) for gx in ( -560, 0, 560 ) for gy in ( -820, -300, 240 )]:
		lights.append( make( "Light", f"{x:.1f},{y:.1f},{z:.1f}", components=[
			comp( "Sandbox.PointLight", Radius=680.0, LightColor="3.000,2.910,2.760,1", Shadows=False, FogStrength=0.6, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	for i, sp in enumerate( ent.get( "spill", [] ) ):
		lights.append( make( "Blue Spill", f"{sp['x']:.1f},{sp['y']:.1f},{sp['z']:.1f}", components=[
			comp( "Sandbox.PointLight", Radius=460.0, LightColor="0.3,0.6,1.5,1", Shadows=False, FogStrength=0.8, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	for sl in ent.get( "strip_lights", [] ):
		lights.append( make( "Strip Light", f"{sl['x']:.1f},{sl['y']:.1f},{sl['z']:.1f}", components=[
			comp( "Sandbox.PointLight", Radius=500.0, LightColor="2.5,2.2,1.8,1", Shadows=False, FogStrength=0.5, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	# cool white light under the corridor kit's ceiling panels
	for cl in ent.get( "corridor_lights", [] ):
		lights.append( make( "Corridor Light", f"{cl['x']:.1f},{cl['y']:.1f},{cl['z']:.1f}", components=[
			comp( "Sandbox.PointLight", Radius=420.0, LightColor="1.9,2.0,2.2,1", Shadows=False, FogStrength=0.4, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	# a blue glow over the command-deck holo-tables added to both bridges (notorious_props.bridge_dressing)
	for x in ( -659.0, 659.0 ):
		lights.append( make( "Bridge Holo Light", f"{x:.1f},4170.0,3300.0", components=[
			comp( "Sandbox.PointLight", Radius=260.0, LightColor="0.35,0.75,1.6,1", Shadows=False, FogStrength=0.6, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	# extra fill in the main-deck corridor by the briefing room, which the map left dark (Angelo, 2026-10-12)
	for ( x, y, z ) in ( ( 55, 3777, -880 ), ( -300, 3700, -880 ), ( 300, 3700, -880 ), ( -600, 3500, -880 ), ( 600, 3500, -880 ), ( 55, 3450, -880 ), ( -600, 3342, -880 ) ):
		lights.append( make( "Light (corridor fill)", f"{x},{y},{z}", components=[
			comp( "Sandbox.PointLight", Radius=1100.0, LightColor="6.000,5.820,5.520,1", Shadows=False, FogStrength=0.6, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	# the command-deck corridor outside High Command Quarters, far too dark (Angelo, 2026-10-15: loc #5, 1682 3584 -269 looking
	# down the corridor towards 1680 3132): a row of fills just over head height along it
	for ( x, y, z ) in ( ( 1682, 3640, -225 ), ( 1682, 3460, -225 ), ( 1682, 3290, -225 ), ( 1682, 3120, -225 ) ):
		lights.append( make( "Light (command deck fill)", f"{x},{y},{z}", components=[
			comp( "Sandbox.PointLight", Radius=900.0, LightColor="5.000,4.900,4.700,1", Shadows=False, FogStrength=0.6, FogMode="Enabled", Attenuation=1.0 ),
		] ) )
	objs.append( make( "Lights", children=lights ) )

	# Working doors, their wall panels and the hangar console (MapSource/notorious_doors.py writes the data).
	doors_path = os.path.join( PROJECT, "Assets", "data", "notorious_doors.json" )
	door_data = json.load( open( doors_path ) ) if os.path.exists( doors_path ) else { "doors": [], "console": None }
	door_objs = []
	for d in door_data["doors"]:
		cx, cy, cz = d["center"]
		panels = []
		for i, pn in enumerate( d["panels"] ):
			px, py, pz = pn["pos"]
			panels.append( make( f"Panel {i + 1}", f"{px - cx:.2f},{py - cy:.2f},{pz - cz:.2f}", yaw_rotation( pn["yaw"] ), [comp( "Swrp.SwrpDoorPanel" )] ) )
		door_objs.append( make( d["name"], f"{cx:.2f},{cy:.2f},{cz:.2f}", components=[
			comp( "Swrp.SwrpDoor", DoorId=d["id"], DisplayName=d["name"], RequiredRank=d["rank"], RequiredFaction="", OrFaction=d.get( "orFaction", "" ), StaffOnly=False,
				KeycardItem="", AutoCloseSeconds=float( d["autoClose"] ), ConsoleOnly=bool( d["console"] ),
				MoveTime=( 9.0 if d["id"].startswith( "roofdoor" ) else 3.0 ) if d["console"] else 1.1 ),
		], children=panels ) )
	objs.append( make( "Doors", children=door_objs ) )

	con = door_data.get( "console" )
	if con:
		cx, cy, cz = con["center"]
		sx, sy, sz = con["size"]
		objs.append( make( "Hangar Console", f"{cx:.2f},{cy:.2f},{cz:.2f}", components=[
			comp( "Swrp.SwrpHangarConsole", Doors=",".join( con["doors"] ), Cooldown=5.0 ),
			comp( "Sandbox.BoxCollider", Center="0,0,0", Scale=f"{sx},{sy},{sz}", IsTrigger=False, Static=True ),
		] ) )


	# Turbolifts and teleporters (MapSource/notorious_transit.py writes the data). Each lift object sits at its car on the car's
	# own deck, with a deck panel inside every deck's copy of the car.
	transit_path = os.path.join( PROJECT, "Assets", "data", "notorious_transit.json" )
	if os.path.exists( transit_path ):
		transit = json.load( open( transit_path ) )
		lifts = []
		for lift in transit["lifts"]:
			car = lift["car"]
			lo, hi = car["inside"]["lo"], car["inside"]["hi"]
			ox, oy, oz = [( lo[i] + hi[i] ) / 2 for i in range( 3 )]
			px, py, pz = car["panel"]["pos"]
			panels = [make( f"Deck Panel {f['name']}", f"{px - ox:.2f},{py - oy:.2f},{pz - oz + f['car_offset']:.2f}", yaw_rotation( car["panel"]["yaw"] ),
				[comp( "Swrp.SwrpLiftPanel" )] ) for f in lift["floors"]]
			floors = ";".join( f"{f['car_offset']:.1f}|{f['name']}|{f['door']}" for f in lift["floors"] )
			lifts.append( make( lift["name"], f"{ox:.2f},{oy:.2f},{oz:.2f}", components=[
				comp( "Swrp.SwrpTurbolift", LiftId=lift["id"], LiftName=lift["name"], Floors=floors,
					CarMin=f"{lo[0]:.1f},{lo[1]:.1f},{lo[2]:.1f}", CarMax=f"{hi[0]:.1f},{hi[1]:.1f},{hi[2]:.1f}", RideSeconds=2.6 ),
			], children=panels ) )
		objs.append( make( "Turbolifts", children=lifts ) )

		pads = []
		for t in transit["teleporters"]:
			lo, hi = t["lo"], t["hi"]
			c = [( lo[i] + hi[i] ) / 2 for i in range( 3 )]
			dests = ";".join( f"{d['x']:.0f},{d['y']:.0f},{d['z']:.0f},{d['yaw']:.0f}" for d in transit["destinations"][t["target"]] )
			pads.append( make( t["name"], f"{c[0]:.1f},{c[1]:.1f},{c[2]:.1f}", components=[
				comp( "Swrp.SwrpTeleporter", TeleporterName=t["name"], Size=f"{hi[0] - lo[0]:.0f},{hi[1] - lo[1]:.0f},{hi[2] - lo[2]:.0f}", Destinations=dests ),
			] ) )
		objs.append( make( "Teleporters", children=pads ) )
		print( f"transit: {len( lifts )} turbolifts, {len( pads )} teleporters" )

	# The brig's shield buttons (the tower's per-cell buttons, the entrance-ray and fight-cage buttons) and the shield control
	# ring in its first room.
	brig = []
	for i, b in enumerate( door_data.get( "brig_buttons", [] ) ):
		cx, cy, cz = b["center"]
		sx, sy, sz = b["size"]
		brig.append( make( f"Brig Button {i + 1}", f"{cx:.2f},{cy:.2f},{cz:.2f}", components=[
			comp( "Swrp.SwrpShieldButton", Doors=",".join( b["doors"] ) ),
			comp( "Sandbox.BoxCollider", Center="0,0,0", Scale=f"{sx},{sy},{sz}", IsTrigger=False, Static=True ),
		] ) )
	ring = brig_ring()
	if ring:
		x, y, z = ring
		brig.append( make( "Brig Shield Control", f"{x:.1f},{y:.1f},{z:.1f}", components=[comp( "Swrp.SwrpBrigConsole" )] ) )
	objs.append( make( "Brig Controls", children=brig ) )

	# Ambience: the room-tone areas (SwrpAmbientZone; anywhere else aboard sounds like a corridor) and the player of it.
	zones = [make( f"Zone {name}", components=[comp( "Swrp.SwrpAmbientZone", Profile=profile, Min=f"{lo[0]},{lo[1]},{lo[2]}", Max=f"{hi[0]},{hi[1]},{hi[2]}", Priority=prio )] )
		for name, profile, lo, hi, prio in AMBIENT_ZONES]
	objs.append( make( "Ambience", components=[comp( "Swrp.SwrpAmbience" )], children=zones ) )

	# Screen FX: the area colour grade and the helmet visor, on their own screen panel under the HUD (hiding the HUD with
	# swrp_hud 0 / F1 keeps them).
	objs.append( make( "Screen FX", components=[
		comp( "Sandbox.ScreenPanel", AutoScreenScale=True, Opacity=1.0, Scale=1.0, ZIndex=50 ),
		comp( "Swrp.GradeOverlay" ),
		comp( "Swrp.VisorOverlay" ),
	] ) )

	# The living ship: flickering and pulsing lights, steam vents, sparking panels, wall screens and the ambient crew. All
	# cosmetic and local (Code/Map/SwrpLivingShip.cs, SwrpCrew.cs); each finds its wall, light or floor when it starts.
	def at( p ):
		return f"{p[0]},{p[1]},{p[2]}"

	def vec( d ):
		return f"{d[0]},{d[1]},{d[2]}"

	life = []
	for i, ( mode, p ) in enumerate( LIVING_LIGHTS ):
		life.append( make( f"Light {mode} {i}", at( p ), components=[comp( "Swrp.SwrpLightFx", Mode=mode, Range=450.0 )] ) )
	for i, ( p, probe ) in enumerate( LIVING_STEAM ):
		life.append( make( f"Steam Vent {i}", at( p ), components=[comp( "Swrp.SwrpSteamVent", Probe=vec( probe ) )] ) )
	for i, ( p, probe ) in enumerate( LIVING_SPARKS ):
		life.append( make( f"Sparking Panel {i}", at( p ), components=[comp( "Swrp.SwrpSparkPanel", Probe=vec( probe ) )] ) )
	for i, ( p, probe, mode ) in enumerate( LIVING_SCREENS ):
		life.append( make( f"Wall Screen {i}", at( p ), components=[comp( "Swrp.SwrpWallScreen", Probe=vec( probe ), Mode=mode, Height=72.0 )] ) )
	for i, ( kind, route, yaw ) in enumerate( LIVING_CREW ):
		first = route[0]
		life.append( make( f"Crew {kind} {i}", at( first ), components=[comp( "Swrp.SwrpCrew", Kind=kind,
			Route="; ".join( f"{x} {y} {z}" for x, y, z in route ), Yaw=float( yaw ) )] ) )
	objs.append( make( "Living Ship", children=life ) )

	# The armory's stock: DC-15A racks on the wall and blaster-cell crates (Code/Map/SwrpArmory.cs). Each finds its wall.
	armory = [make( f"Armory {kind} {i}", at( p ), components=[comp( "Swrp.SwrpArmory", Kind=kind, Probe=vec( probe ) )] )
		for i, ( kind, p, probe ) in enumerate( ARMORY )]
	objs.append( make( "Armory", children=armory ) )

	# Bacta pods in the medbay (Code/Map/SwrpBactaPod.cs): each stands with its back to the wall its probe finds.
	pods = [make( f"Bacta Pod {i + 1}", at( p ), components=[comp( "Swrp.SwrpBactaPod", PodName="Bacta pod", Probe=vec( probe ) )] )
		for i, ( p, probe ) in enumerate( MEDBAY_PODS )]
	objs.append( make( "Bacta Pods", children=pods ) )

	# The map's props, baked into spatial chunks by notorious_build.py, and the corridor kit (notorious_corridors.py).
	for label, data in ( ( "Props", "notorious_props.json" ), ( "Corridors", "notorious_corridors.json" ) ):
		path = os.path.join( PROJECT, "Assets", "data", data )
		if os.path.exists( path ):
			# render and collision come from the same chunk model, so what isn't drawn can't be bumped into either
			chunks = [make( os.path.basename( m )[:-5], components=[
				comp( "Sandbox.ModelRenderer", Model=m, BodyGroups=18446744073709551615, RenderType="On", Tint="1,1,1,1" ),
				comp( "Sandbox.ModelCollider", Model=m, Static=True, IsTrigger=False ),
			] ) for m in json.load( open( path ) )]
			objs.append( make( label, children=chunks ) )

	# Squads, orders and radio state (host-owned, networked).
	objs.append( make( "SWRP Roleplay", components=[comp( "Swrp.SwrpRoleplay" )] ) )

	# Ship systems: alert level and public address (host-owned, networked), a command console and a holo-map of the decks
	# on each bridge's command deck, by the holo-table notorious_props.bridge_dressing() puts there.
	objs.append( make( "SWRP Ship", components=[comp( "Swrp.SwrpShip" )] ) )
	bridge = []
	for side, sx in ( ( "Port", -1 ), ( "Starboard", 1 ) ):
		bridge.append( make( f"{side} Bridge Command Console", f"{sx * 520.0:.1f},4170.0,3229.0", yaw_rotation( 0 if sx > 0 else 180 ),
			[comp( "Swrp.SwrpShipConsole", ConsoleName=f"{side} Bridge Command Console" )] ) )
		bridge.append( make( f"{side} Bridge Holo-map", f"{sx * 659.0:.1f},4170.0,3229.0", components=[comp( "Swrp.SwrpHoloMap", Scale=0.0045, Height=48.0 )] ) )
	objs.append( make( "Bridge Systems", children=bridge ) )

	# Training courses in the two simulation rooms (the holo-grid halls either side of the simulation deck corridor). The
	# console stands just inside each room's door; targets pop up across the far part of the room.
	courses = []
	for cid, name, cy, yaw, zone_y, room_y in (
		( "sim_a", "Simulation Room A", 7700.0, 270, ( 8150.0, 9700.0 ), ( 7592.0, 11384.0 ) ),
		( "sim_b", "Simulation Room B", 6750.0, 90, ( 4250.0, 6300.0 ), ( 3068.0, 6860.0 ) ),
	):
		courses.append( make( name, f"-11640.0,{cy:.1f},13619.0", yaw_rotation( yaw ), [comp( "Swrp.SwrpTrainingCourse", CourseId=cid, CourseName=name,
			ZoneMin=f"-12850,{zone_y[0]:.0f},13600", ZoneMax=f"-10790,{zone_y[1]:.0f},13900",
			RoomMin=f"-13013,{room_y[0]:.0f},13560", RoomMax=f"-10625,{room_y[1]:.0f},14400" )] ) )
	objs.append( make( "Training Courses", children=courses ) )

	# Holo-tables for /briefing, where the original map had its war-room and bridge tables, plus the briefing hall.
	tables = []
	for name, x, y, z in holo_tables():
		tables.append( make( name, f"{x:.1f},{y:.1f},{z:.1f}", components=[comp( "Swrp.SwrpBriefingTable", TableName=name )] ) )
	objs.append( make( "Holo Tables", children=tables ) )

	# Medbay beds where the dead redeploy. Floors checked against
	# the VMF brushes (open floor, nothing solid within 30 units at waist height).
	# (no bacta stations: Angelo had them all removed, 2026-10-13; healing is the bacta items, medics and the medbay pods)
	beds = [make( "Medbay Bed", f"{x},{y},{z + 4}", yaw_rotation( yaw ), [comp( "Swrp.SwrpSpawnPoint", FactionId=None, Medbay=True )] )
		for x, y, z, yaw in MEDBAY_BEDS]
	objs.append( make( "Medbay Respawn", children=beds ) )

	jails = []
	for i, c in enumerate( ent["cells"] ):
		jails.append( make( f"Jail Cell {i + 1}", f"{c['x']},{c['y']},{c.get( 'floor', c['z'] - 60 )}", yaw_rotation( 90 ), [comp( "Swrp.SwrpJailPoint" )] ) )
	objs.append( make( "Brig", children=jails ) )

	scene["GameObjects"] = objs
	json.dump( scene, open( OUT, "w", encoding="utf-8" ), indent=2 )
	print( f"wrote {OUT}: {len( spawns )} spawns, {len( lights )} lights, {len( jails )} jail cells" )


main()
