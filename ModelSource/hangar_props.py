"""
Original low-poly hangar set dressing for the character screen, built from primitives in the same style as starships.py
(no ripped or downloaded models):

- arc_fighter: a heavy three-seat Republic fighter in the spirit of the ARC-170 (not a copy): a long fuselage with a
  stepped glazed cockpit and a rear gunner's bubble, split wing foils carrying two big engine nacelles, long wingtip
  cannons, twin tail fins, red unit markings, an astromech socket, and its landing gear down.
- hangar_gantry: a maintenance scaffold to stand beside it: four posts, a deck with railings, a ladder, hazard stripes and
  a work lamp.
- hangar_cart: a service cart with two fuel tanks, a hose reel and a tool chest.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/hangar_props.py

Units are s&box units, +X forward, +Z up, origin on the floor under the middle. Writes Assets/models/hangar/<id>_<rev>.fbx
(+ .meta, keeping the previous revision) and <id>.vmdl; materials are the starship set plus a few, in
Assets/materials/starships/.
"""
import os
import sys
import time
import uuid

import bpy

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import starships as ss

PROJECT = os.path.dirname( HERE )
MODEL_DIR = os.path.join( PROJECT, "Assets", "models", "hangar" )
REV = format( int( time.time() ), "x" )[-6:]

ss.MATERIALS.update( {
	"hazard": ( ( 0.85, 0.62, 0.08 ), 0.1, 0.6, None ),          # yellow work stripes
	"steel": ( ( 0.22, 0.23, 0.25 ), 0.7, 0.4, None ),           # scaffold and tools
	"lamp": ( ( 1.0, 0.95, 0.85 ), 0.0, 0.3, ( 1.0, 0.92, 0.78 ) ),  # a work lamp's lens
	"tank": ( ( 0.55, 0.12, 0.1 ), 0.3, 0.45, None ),            # fuel tanks
} )


def arc_fighter():
	"""About 430 long and 640 across, sitting on its gear: belly at 52, so the origin is on the floor."""
	s = ss.Ship( "arc_fighter" )
	z = 78      # fuselage centre height
	# fuselage, nose and chin cannon housing
	s.box( "hull", ( 10, 0, z ), ( 330, 64, 54 ) )
	s.box( "hull", ( 210, 0, z - 4 ), ( 90, 60, 46 ), taper=( 0.55, 0.55 ) )
	s.box( "hull_dark", ( 250, 0, z - 22 ), ( 50, 26, 16 ), taper=( 0.6, 0.6 ) )
	s.cyl( "steel", ( 300, 0, z - 22 ), 50, 3.5, 8 )
	s.box( "accent", ( 200, 0, z + 23 ), ( 70, 30, 4 ), taper=( 0.5, 1.0 ) )           # red nose marking
	# the stepped canopy: pilot, co-pilot, and the rear gunner's bubble
	s.box( "canopy", ( 140, 0, z + 34 ), ( 70, 40, 24 ), taper=( 0.55, 0.45 ) )
	s.box( "canopy", ( 80, 0, z + 36 ), ( 60, 42, 26 ) )
	s.box( "hull", ( 30, 0, z + 30 ), ( 50, 50, 16 ) )
	s.box( "canopy", ( -120, 0, z + 28 ), ( 40, 34, 22 ), taper=( 0.7, 0.6 ) )
	# astromech socket behind the cockpit
	s.cyl( "hull_dark", ( -10, 0, z + 34 ), 22, 10, 12, rot=( 0, 90, 0 ) )
	# tail and twin fins
	s.box( "hull_dark", ( -175, 0, z ), ( 50, 70, 50 ) )
	for side in ( 1, -1 ):
		s.box( "hull", ( -185, side * 30, z + 48 ), ( 70, 6, 70 ), rot=( side * 12, 0, 0 ), taper=( 1.0, 0.45 ) )
		s.box( "accent", ( -195, side * 34, z + 72 ), ( 34, 7, 12 ), rot=( side * 12, 0, 0 ) )
	for side in ( 1, -1 ):
		# engine nacelle at the wing root, intake in front, glowing exhaust behind
		ny = side * 70
		s.cyl( "hull", ( -40, ny, z ), 290, 30, 14 )
		s.cyl( "hull_dark", ( 112, ny, z ), 16, 26, 14 )
		s.cyl( "hull_dark", ( -192, ny, z ), 18, 28, 14, radius2=24 )
		s.cyl( "engine", ( -203, ny, z ), 4, 22, 14 )
		s.box( "accent", ( 40, ny, z + 30 ), ( 90, 10, 3 ) )
		# the split wing foils, opened a little like in flight trim
		for foil, tilt in ( ( 1, 7 ), ( -1, -7 ) ):
			s.box( "hull", ( -60, side * 200, z + foil * 9 ), ( 150, 240, 7 ), rot=( side * tilt, 0, 0 ), taper=( 1.0, 1.0 ) )
			s.box( "hull_dark", ( -10, side * 200, z + foil * 13 ), ( 30, 230, 4 ), rot=( side * tilt, 0, 0 ) )
			s.box( "accent", ( -100, side * 260, z + foil * 16 ), ( 50, 60, 4 ), rot=( side * tilt, 0, 0 ) )
		# wingtip cannon pods and long barrels
		s.cyl( "hull_dark", ( -40, side * 322, z ), 150, 10, 10 )
		s.cyl( "steel", ( 110, side * 322, z ), 170, 4, 8 )
		s.cyl( "hull_dark", ( 196, side * 322, z ), 12, 6, 8 )
		# main gear under the nacelles
		s.box( "steel", ( -40, ny, 36 ), ( 10, 10, 40 ) )
		s.box( "hull_dark", ( -40, ny, 12 ), ( 30, 18, 24 ) )
	# nose gear
	s.box( "steel", ( 200, 0, 34 ), ( 8, 8, 40 ) )
	s.box( "hull_dark", ( 200, 0, 10 ), ( 22, 14, 20 ) )
	return s.build()


def hangar_gantry():
	"""A scaffold about 160 x 90 and 150 tall: four posts, a deck at 110 with railings, a ladder, a work lamp."""
	s = ss.Ship( "hangar_gantry" )
	for x in ( -75, 75 ):
		for y in ( -40, 40 ):
			s.box( "steel", ( x, y, 75 ), ( 6, 6, 150 ) )
			s.box( "hazard", ( x, y, 8 ), ( 7, 7, 16 ) )
		s.box( "steel", ( x, 0, 30 ), ( 5, 80, 5 ) )
	s.box( "steel", ( 0, 0, 110 ), ( 160, 90, 5 ) )                                   # deck
	s.box( "hazard", ( 0, 46, 110 ), ( 160, 3, 6 ) )
	s.box( "hazard", ( 0, -46, 110 ), ( 160, 3, 6 ) )
	for y in ( -42, 42 ):
		s.box( "steel", ( 0, y, 140 ), ( 154, 3, 3 ) )                                  # top rails
		s.box( "steel", ( 0, y, 126 ), ( 154, 2, 2 ) )
	s.box( "steel", ( -78, 0, 140 ), ( 3, 84, 3 ) )
	# ladder up the front
	for x in ( 82, 98 ):
		s.box( "steel", ( x, -30, 56 ), ( 3, 3, 112 ) )
	for i in range( 7 ):
		s.box( "steel", ( 90, -30, 12 + i * 15 ), ( 18, 2, 2 ) )
	# work lamp on a pole
	s.box( "steel", ( -70, 36, 170 ), ( 3, 3, 60 ) )
	s.box( "hull_dark", ( -64, 36, 198 ), ( 16, 12, 9 ), rot=( 0, 30, 0 ) )
	s.box( "lamp", ( -56, 36, 194 ), ( 2, 9, 6 ), rot=( 0, 30, 0 ) )
	return s.build()


def hangar_cart():
	"""A service cart about 90 x 50 x 50: two fuel tanks on a wheeled bed, a hose reel and a tool chest."""
	s = ss.Ship( "hangar_cart" )
	s.box( "hull_dark", ( 0, 0, 14 ), ( 90, 50, 8 ) )
	for x in ( -34, 34 ):
		for y in ( -22, 22 ):
			s.cyl( "steel", ( x, y, 8 ), 6, 8, 10, rot=( 0, 0, 90 ) )
	for y in ( -12, 12 ):
		s.cyl( "tank", ( -8, y, 30 ), 60, 11, 12 )
		s.cyl( "steel", ( 24, y, 30 ), 4, 7, 10 )
	s.cyl( "hull_dark", ( 40, 0, 40 ), 16, 12, 12, rot=( 0, 0, 90 ) )                 # hose reel
	s.box( "hazard", ( -40, 0, 20 ), ( 10, 52, 4 ) )
	s.box( "hull", ( 0, 0, 52 ), ( 30, 30, 10 ) )                                     # tool chest
	s.box( "accent", ( 0, 0, 58 ), ( 31, 31, 2 ) )
	return s.build()


# ----- the character screen's own hangar bay -----

NOTORIOUS = {
	"floor": "materials/notorious/roadside_2003_floor_panel_06.vmat",
	"wall": "materials/notorious/roadside_2003_wall_panel_darker.vmat",
	"rib": "materials/notorious/roadside_2003_wall_panel_darkest.vmat",
	"trim": "materials/notorious/roadside_2003_wall_panel_notasdark.vmat",
	"hull": "materials/notorious/bananakin_2003_fm_dark_hull.vmat",
	"detail": "materials/notorious/roadside_2003_wall_detailed_panel3.vmat",
	"light": "materials/notorious/light_panel_white.vmat",
}


class Room:
	"""Axis-aligned boxes with world-scaled UVs (the map's own panel materials tile instead of stretching)."""

	def __init__( self, name ):
		import bmesh
		self.name = name
		self.bm = bmesh.new()
		self.uv = self.bm.loops.layers.uv.new( "UVMap" )
		self.slots = []

	def box( self, mat, lo, hi, tile=128.0 ):
		import bmesh
		from mathutils import Vector
		if mat not in self.slots:
			self.slots.append( mat )
		idx = self.slots.index( mat )
		size = [hi[i] - lo[i] for i in range( 3 )]
		verts = bmesh.ops.create_cube( self.bm, size=1.0 )["verts"]
		for v in verts:
			v.co = Vector( ( lo[0] + ( v.co.x + 0.5 ) * size[0], lo[1] + ( v.co.y + 0.5 ) * size[1], lo[2] + ( v.co.z + 0.5 ) * size[2] ) )
		self.bm.normal_update()
		for f in { f for v in verts for f in v.link_faces }:
			f.material_index = idx
			n = f.normal
			ax = max( range( 3 ), key=lambda i: abs( n[i] ) )
			u, w = [i for i in range( 3 ) if i != ax]
			for loop in f.loops:
				loop[self.uv].uv = ( loop.vert.co[u] / tile, loop.vert.co[w] / tile )

	def build( self ):
		mesh = bpy.data.meshes.new( self.name )
		self.bm.normal_update()
		self.bm.to_mesh( mesh )
		self.bm.free()
		obj = bpy.data.objects.new( self.name, mesh )
		bpy.context.scene.collection.objects.link( obj )
		for m in self.slots:
			path = NOTORIOUS[m]
			obj.data.materials.append( bpy.data.materials.get( path ) or bpy.data.materials.new( path ) )
		return obj


def menu_hangar():
	"""
	A closed hangar bay about 1900 deep, 1800 wide and 780 high, lined with the ship's own panels: ribbed side walls, a
	ceiling of beams with light panels, and a tall window across the back wall onto space (the bay's ray-shielded mouth).
	Origin on the floor where the trooper stands; +X runs towards the camera, the back wall is at x -1300.
	"""
	r = Room( "menu_hangar" )
	x0, x1, w, h = -1300, 620, 900, 780
	r.box( "floor", ( x0, -w, -24 ), ( x1, w, 0 ), 128 )
	for side in ( -1, 1 ):
		y = side * w
		r.box( "wall", ( x0, min( y, y + side * 24 ), 0 ), ( x1, max( y, y + side * 24 ), h ), 256 )
		for x in range( x0 + 150, x1, 300 ):
			r.box( "rib", ( x - 26, min( y, y - side * 60 ), 0 ), ( x + 26, max( y, y - side * 60 ), h ), 128 )
			r.box( "detail", ( x - 120, min( y, y - side * 10 ), 60 ), ( x + 120, max( y, y - side * 10 ), 230 ), 128 )
		r.box( "trim", ( x0, min( y, y - side * 80 ), 0 ), ( x1, max( y, y - side * 80 ), 40 ), 64 )
	r.box( "hull", ( x0 - 24, -w, h ), ( x1, w, h + 24 ), 256 )
	for x in range( x0 + 150, x1, 300 ):
		r.box( "rib", ( x - 30, -w, h - 70 ), ( x + 30, w, h ), 128 )
		for y in ( -450, 0, 450 ):
			r.box( "light", ( x - 70, y - 160, h - 74 ), ( x + 70, y + 160, h - 70 ), 64 )
	# the back wall: a long window onto space between a low wall and the upper bulkhead, pillars at the ends
	r.box( "wall", ( x0 - 24, -w, 0 ), ( x0, w, 120 ), 256 )
	r.box( "trim", ( x0 - 8, -w, 112 ), ( x0 + 30, w, 128 ), 64 )
	r.box( "wall", ( x0 - 24, -w, 560 ), ( x0, w, h ), 256 )
	r.box( "rib", ( x0 - 8, -w, 548 ), ( x0 + 40, w, 572 ), 64 )
	for y in ( -w, w - 220 ):
		r.box( "wall", ( x0 - 24, y, 0 ), ( x0, y + 220, h ), 256 )
	for y in ( -450, 0, 450 ):
		r.box( "rib", ( x0 - 16, y - 18, 120 ), ( x0 + 16, y + 18, 560 ), 64 )
	# the front wall behind the camera
	r.box( "wall", ( x1, -w, 0 ), ( x1 + 24, w, h ), 256 )
	return r.build()


VMDL = ss.VMDL.replace( "models/space/", "models/hangar/" )


def export( obj ):
	os.makedirs( MODEL_DIR, exist_ok=True )
	name = obj.name
	fbx = f"{name}_{REV}"
	bpy.ops.object.select_all( action="DESELECT" )
	obj.select_set( True )
	bpy.ops.export_scene.fbx( filepath=os.path.join( MODEL_DIR, fbx + ".fbx" ), use_selection=True, mesh_smooth_type="FACE",
		axis_forward="Y", axis_up="Z" )
	meta = os.path.join( MODEL_DIR, fbx + ".fbx.meta" )
	if not os.path.exists( meta ):
		with open( meta, "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	with open( os.path.join( MODEL_DIR, name + ".vmdl" ), "w", newline="\n" ) as fh:
		fh.write( VMDL % { "fbx": fbx } )
	revs = sorted( ( f for f in os.listdir( MODEL_DIR ) if f.startswith( name + "_" ) and f.endswith( ".fbx" ) ),
		key=lambda f: os.path.getmtime( os.path.join( MODEL_DIR, f ) ) )
	for old in revs[:-2]:
		for fn in ( old, old + ".meta" ):
			try:
				os.remove( os.path.join( MODEL_DIR, fn ) )
			except OSError:
				pass


def main():
	bpy.ops.wm.read_factory_settings( use_empty=True )
	ss.write_materials()
	for build in ( arc_fighter, hangar_gantry, hangar_cart, menu_hangar ):
		obj = build()
		export( obj )
		print( f"HANGAR {obj.name}: {len( obj.data.polygons )} faces" )


if __name__ == "__main__":
	main()
