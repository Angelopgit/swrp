"""
Blasters from the Garry's Mod TFA Star Wars packs (Workshop 3751368978 "Aurora Content #2", which bundles the TFA SWRP
Republic/CIS weapons) for the guns Battlefront II itself doesn't have. LOCAL ONLY, like the TFA sounds: never publish or commit
these models or textures. Source files are pulled out of the .gma into X:\\SWRP Projects\\TFA_Extracted first (models, VMTs,
VTFs; see the TFA port notes in Data/Blasters.cs).

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/tfa_props.py [-- name ...]

Each gun is put in the frame the game holds it by (grip at the origin, barrel +X, up +Z), the same as ModelSource/bf2_props.py.
The TFA weapons place their world models on the Gmod right hand (SWEP Construction Kit WElements: pos/angle/size on
ValveBiped.Bip01_R_Hand), so every model is first moved into that hand frame, then into our grip frame with one hand-to-grip
transform calibrated on the E-5 (TFA's E-5 model laid over BF2's E-5, whose grip we know), then levelled so its long axis is
horizontal. Writes Assets/models/weapons/tfa_<name>_<rev>.fbx, tfa_<name>.vmdl and its vmats/PNGs in tfatex/.
"""
import os
import sys
import math
import uuid

import bpy
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname( os.path.abspath( __file__ ) )
PROJECT = os.path.dirname( HERE )
sys.path.insert( 0, HERE )
sys.path.insert( 0, os.path.join( PROJECT, "MapSource" ) )
import bf2_props as bp                      # VMDL/ATTACH templates, bounds, import_lod0, frame_matrix (main() is guarded)
from source_mdl import load_model

SRC = r"X:\SWRP Projects\TFA_Extracted"
OUT_DIR = os.path.join( PROJECT, "Assets", "models", "weapons" )
TEX_DIR = os.path.join( OUT_DIR, "tfatex" )

# the VTF decoder from MapSource/gmod_textures.py (that file runs its whole build when imported, so only its helpers are taken)
_gt = open( os.path.join( PROJECT, "MapSource", "gmod_textures.py" ), encoding="utf-8" ).read()
_ns = { "__file__": os.path.join( PROJECT, "MapSource", "gmod_textures.py" ), "__name__": "gmod_textures_helpers" }
exec( compile( _gt[:_gt.index( "\ndef main(" )], "gmod_textures.py", "exec" ), _ns )
read_vtf, write_png = _ns["read_vtf"], _ns["write_png"]

# name -> (model parts, WElements pos, angle (p, y, r), size) from the TFA weapon scripts (lua/weapons/rw_sw_*.lua, at_sw_*.lua)
GUNS = {
	"westarm5": ( ["models/sw_battlefront/weapons/new/westar_m5_blaster_rifle.mdl"], ( 8, 1.2, -3 ), ( 180, -90, 10 ), 1.0 ),
	"dp23": ( ["models/cs574/weapons/dp23.mdl"], ( 3.5, 0.5, -1.5 ), ( -12, 0, 172 ), 1.0 ),
	"dp24": ( ["models/cs574/weapons/dp24.mdl"], ( 3.5, 0.5, -1.25 ), ( -13, 0, 180 ), 1.0 ),
	"sg6": ( ["models/swbf3/weapons/cisshotgun.mdl"], ( 4.5, 1, -3 ), ( 13, 180, 180 ), 1.1 ),
	"dc17s": ( ["models/fisher/dc17s/dc17s.mdl"], ( 8.2, 1.5, -3.25 ), ( 0, -90, 175 ), 0.9 ),
	"se14": ( ["models/sw_battlefront/weapons/2019/se14_pistol.mdl"], ( 4, 1.5, 1.5 ), ( -8, 0, 180 ), 1.0 ),
	"dc15se": ( ["models/cs574/weapons/dc15se.mdl"], ( 2.8, 0.5, -0.5 ), ( -12, 0, 172 ), 1.0 ),
	"dc15sa": ( ["models/cs574/weapons/dc15sa_base.mdl", "models/cs574/weapons/dc15sa_cell.mdl", "models/cs574/weapons/dc15sa_cross.mdl"],
		( 8.5, 1.5, 1.45 ), ( -5, 0, 180 ), 1.0 ),
	# second port (2026-10-19): Imperial / Galactic / Republic leftovers the BF2 export lacks
	"dc17c": ( ["models/cs574/weapons/dc17c.mdl"], ( 3.7, 1.2, -1.5 ), ( 180.0, 180.0, 0.0 ), 1.0 ),
	"chaingun": ( ["models/swbf3/rep/republicchaingun.mdl"], ( -7.6, 0.8, 10.0 ), ( 188.0, 180.0, 0.0 ), 0.8 ),
	"dt29": ( ["models/rising/w_dt29.mdl"], ( 9.75, 0.5, -4.0 ), ( 0.0, -90.0, 168.0 ), 1.0 ),
	"e10": ( ["models/rising/w_e10.mdl"], ( 10.5, 0.0, -5.2 ), ( 0.0, -90.0, 169.0 ), 1.2 ),
	"e22": ( ["models/rising/w_e22.mdl"], ( 9.5, 0.0, -4.8 ), ( 0.0, -87.0, 165.0 ), 1.0 ),
	"se14r": ( ["models/hauptmann/star wars/weapons/se14c.mdl"], ( 4.0, 1.5, 2.9 ), ( -8.0, 0.0, 180.0 ), 1.0 ),
	"tl40": ( ["models/sw_battlefront/weapons/tl50_repeater.mdl"], ( 2.0, 0.4, 0.5 ), ( -12.0, 0.0, 172.0 ), 1.0 ),
	"cj9": ( ["models/rising/w_bo.mdl"], ( 8.8, 0.95, -3.5 ), ( 0.0, -90.0, 170.0 ), 1.0 ),
	"defenderp": ( ["models/sw_battlefront/weapons/2019/defender_base.mdl"], ( 4.0, 1.5, -1.2 ), ( -12.0, 0.0, 180.0 ), 1.0 ),
	"dl18": ( ["models/hauptmann/star wars/weapons/dl18.mdl"], ( 7.0, 2.0, 2.0 ), ( -5.0, -7.0, 180.0 ), 1.0 ),
	"dt12": ( ["models/hauptmann/star wars/weapons/ddt12.mdl"], ( 6.0, 1.5, 2.65 ), ( -10.0, 0.0, 180.0 ), 1.0 ),
	"huntershotgun": ( ["models/kuro/sw_battlefront/weapons/bf1/scattergun.mdl"], ( 8.0, 1.0, 0.75 ), ( -11.0, 0.0, 180.0 ), 1.1 ),
	"ib94": ( ["models/cs574/weapons/dc92.mdl"], ( 7.5, 2.0, -1.0 ), ( -5.0, -7.0, 180.0 ), 1.0 ),
	"ib94s": ( ["models/cs574/weapons/dc92s.mdl"], ( 7.5, 1.8, -0.75 ), ( -3.0, 0.0, 173.0 ), 1.0 ),
	"k16": ( ["models/hauptmann/star wars/weapons/bryar_pistol.mdl"], ( 8.3, 1.75, 3.25 ), ( 0.0, 0.0, 180.0 ), 1.0 ),
	"ll30": ( ["models/weapons/ll30.mdl"], ( 2.5, 1.75, 0.85 ), ( 180.0, 90.0, -5.0 ), 1.0 ),
	"m57": ( ["models/sw_battlefront/weapons/dl44_pistol_ext.mdl"], ( 3.5, 1.5, -1.4 ), ( 0.0, -90.0, 170.0 ), 1.0 ),
	"nn14": ( ["models/cs574/weapons/nn14.mdl"], ( 9.5, 1.4, 0.0 ), ( 0.0, 0.0, 180.0 ), 1.0 ),
	"scattershotgun": ( ["models/kuro/sw_battlefront/weapons/bf1/scattergun.mdl"], ( 8.0, 1.0, 0.75 ), ( -11.0, 0.0, 180.0 ), 1.1 ),
	"umb1": ( ["models/jay/unbaran_gun.mdl"], ( 11.75, 0.7, -6.0 ), ( 0.0, -90.0, 160.0 ), 1.0 ),
	"westar11": ( ["models/sw_battlefront/weapons/westar_35_rifle.mdl"], ( 4.0, 1.7, -1.5 ), ( 0.0, -85.0, 165.0 ), 1.0 ),
	"westar34": ( ["models/cs574/weapons/westar34.mdl"], ( 7.5, 2.0, -1.2 ), ( -5.0, -7.0, 180.0 ), 0.8 ),
	"westar35": ( ["models/sw_battlefront/weapons/westar_35_pistol.mdl"], ( 4.0, 1.5, -1.3 ), ( 0.0, -95.0, 170.0 ), 1.0 ),
	"m5d": ( ["models/weapons/m5d_arsenal/tfa_m5d_single.mdl"], ( 7.9, 1.0, 1.0 ), ( -8.0, 0.0, 180.0 ), 1.0 ),
	# launchers and the flamethrower (2026-10-20)
	"e60r": ( ["models/cs574/weapons/e60r.mdl"], ( 2.0, 1.0, -5.5 ), ( -10.0, 0.0, 180.0 ), 0.8 ),
	"hh12": ( ["models/cs574/weapons/hh12.mdl"], ( 3.0, 1.7, -7.5 ), ( -12.0, 0.0, 180.0 ), 0.8 ),
	"hh15": ( ["models/weapons/star_wars_battlefront/reb_rocket_launcher.mdl"], ( 8.0, 1.0, -4.5 ), ( 0.0, 90.0, 192.0 ), 1.0 ),
	"minimagplt": ( ["models/weapons/star_wars_battlefront/imp_rocket_launcher.mdl"], ( 7.5, 1.0, -4.5 ), ( 0.0, 90.0, 192.0 ), 1.0 ),
	"plx1": ( ["models/fisher/launcher/plx1.mdl"], ( -5.0, 1.0, -4.0 ), ( 0.0, 90.0, 192.0 ), 0.8 ),
	"rps6": ( ["models/rps6/Zl_RPS-6.mdl"], ( -6.0, 2.0, -3.5 ), ( -7.0, 0.0, 90.0 ), 1.5 ),
	"smartlauncher": ( ["models/sw_battlefront/weapons/rocketlauncher.mdl"], ( 2.5, 1.7, 3.2 ), ( -13.0, 0.0, 190.0 ), 1.0 ),
	"pinglauncher": ( ["models/sw_battlefront/weapons/pinglauncher.mdl"], ( 2.5, 1.1, 2.8 ), ( -12.0, 0.0, 180.0 ), 1.1 ),
	"flamethrower": ( ["models/weapons/nsn/w_flamethrower.mdl"], ( 0.0, 0.0, 0.0 ), ( 0.0, 0.0, 0.0 ), 1.0 ),
}
# not guns: kept in their own model space, centred on their bounds (shields, grenades, the rocket in flight)
RAW = {
	"shield_rep": "models/cs574/weapons/shields/blast_shield.mdl", "shield_cis": "models/cs574/weapons/shields/droid_shield.mdl",
	"shield_imp": "models/cs574/weapons/shields/empireshield.mdl", "shield_rebel": "models/cs574/weapons/shields/rebel_shield.mdl",
	"shield_cg": "models/cs574/weapons/shields/shield2.mdl", "rocket": "models/cs574/ammo/reworked_rocket.mdl",
	"nade_thermal": "models/cs574/explosif/grenade_base.mdl", "nade_bacta": "models/cs574/explosif/grenade_bacta.mdl",
	"nade_dioxis": "models/cs574/explosif/grenade_dioxis.mdl", "nade_flash": "models/cs574/explosif/grenade_flash.mdl",
	"nade_impact": "models/cs574/explosif/grenade_impact.mdl", "nade_shock": "models/cs574/explosif/grenade_shock.mdl",
	"nade_smoke": "models/cs574/explosif/grenade_smoke.mdl", "nade_training": "models/cs574/explosif/grenade_train.mdl",
}
# extra turn about the grip (degrees, + raises the nose) after levelling
PITCH_FIX = { "westarm5": 22.0, "ib94": -30.0, "ib94s": -30.0, "westar34": -20.0 }
# long-stocked guns the barrel test turns the wrong way round (checked on a side-view render, 2026-10-19)
FLIP = { "dt29", "e10", "e22", "cj9", "nn14", "m5d" }
# hand placement came out off the grip: re-seat on the pistol grip
GRIP_FIX = FLIP | { "ib94", "ib94s" }
# the calibration gun: TFA's E-5 placement, and BF2's E-5 (grip known)
CAL = ( "models/kuro/sw_battlefront/weapons/e5_blaster.mdl", ( 5, 1, 2 ), ( -8, 0, 180 ), 1.0 )
CAL_BF2 = "Weapon_E5/e5_mesh3p_mesh.fbx"


def rot_axis( axis, deg ):
	return Matrix.Rotation( math.radians( deg ), 3, Vector( axis ).normalized() )


def sck_rotation( ang ):
	"""SWEP Construction Kit: start at the bone's angles, rotate about Up by yaw, Right by pitch, Forward by roll (each about
	the current axes). Source axes: forward +X, left +Y (right = -Y), up +Z."""
	p, y, r = ang
	R = Matrix.Identity( 3 )
	R = rot_axis( R @ Vector( ( 0, 0, 1 ) ), y ) @ R
	R = rot_axis( -( R @ Vector( ( 0, 1, 0 ) ) ), p ) @ R
	R = rot_axis( R @ Vector( ( 1, 0, 0 ) ), r ) @ R
	return R


def to_hand( pos, ang, size ):
	"""Model space -> hand-bone space for one WElement: v -> pos + R * (size * v); pos is forward/right/up of the bone."""
	R = sck_rotation( ang )
	t = Vector( ( pos[0], -pos[1], pos[2] ) )
	return t, R, size


def mdl_points( paths ):
	pts = []
	for p in paths:
		m = load_model( os.path.join( SRC, p ) )
		pts += [Vector( q ) for tri in m.tris for q in tri[1]]
	return pts


def profile( pts, axis, flip, lo, hi, bins=12 ):
	"""Height (z extent) of a gun along its length, front to back, normalised: used to tell muzzle from stock."""
	n = [[] for _ in range( bins )]
	for p in pts:
		f = ( p[axis] - lo[axis] ) / max( 1e-6, hi[axis] - lo[axis] )
		if flip:
			f = 1 - f
		n[min( bins - 1, int( f * bins ) )].append( p[2] )
	h = [( max( b ) - min( b ) ) if b else 0 for b in n]
	m = max( h ) or 1
	return [x / m for x in h]


def calibrate():
	"""The hand-to-grip transform: Q and G such that grip = Q^T (hand - G), found by laying TFA's E-5 over BF2's E-5 (whose
	grip frame we know). The E-5 model's long axis and which end is the muzzle are worked out from its shape."""
	t, R, s = to_hand( CAL[1], CAL[2], CAL[3] )
	pts = [p * s for p in mdl_points( [CAL[0]] )]
	objs = bp.import_lod0( os.path.join( bp.BF2, CAL_BF2 ) )
	mtx = bp.frame_matrix( objs, "gun", None )
	for o in objs:
		o.data.transform( mtx )
	bpts = [v.co.copy() for o in objs for v in o.data.vertices]
	blo, bhi = bp.bounds( objs )
	for o in objs:
		bpy.data.objects.remove( o, do_unlink=True )
	lo = Vector( [min( p[i] for p in pts ) for i in range( 3 )] )
	hi = Vector( [max( p[i] for p in pts ) for i in range( 3 )] )
	axis = 0 if hi.x - lo.x >= hi.y - lo.y else 1
	ref = profile( bpts, 0, False, blo, bhi )
	err = [sum( ( a - b ) ** 2 for a, b in zip( profile( pts, axis, f, lo, hi ), ref ) ) for f in ( False, True )]
	sign = 1 if err[0] <= err[1] else -1
	fwd = Vector( [sign if i == axis else 0 for i in range( 3 )] )
	up = Vector( ( 0, 0, 1 ) )
	C = Matrix( ( fwd, up.cross( fwd ), up ) )          # rows: grip axes in model coordinates
	cp = [C @ p for p in pts]
	clo = Vector( [min( p[i] for p in cp ) for i in range( 3 )] )
	chi = Vector( [max( p[i] for p in cp ) for i in range( 3 )] )
	print( f"TFAPROP calibrate: E-5 model axis {axis} sign {sign}, TFA size {[round( x, 1 ) for x in chi - clo]}, BF2 size {[round( x, 1 ) for x in bhi - blo]}" )
	shift = ( blo + bhi ) / 2 - ( clo + chi ) / 2
	Q = R @ C.transposed()
	G = t - Q @ shift
	return Q, G


def level( verts ):
	"""Turn about the grip (Y axis) so the gun's long axis is horizontal; then make sure the barrel points +X."""
	a = np.array( [( v.x, v.z ) for v in verts] )
	c = a.mean( axis=0 )
	_, _, vt = np.linalg.svd( a - c, full_matrices=False )
	d = vt[0]
	if d[0] < 0:
		d = -d
	pitch = math.degrees( math.atan2( d[1], d[0] ) )
	return Matrix.Rotation( math.radians( pitch ), 3, "Y" )      # rotating about +Y by +pitch lowers a nose that's up


def texture( vmt_dir, tex_name, cd ):
	"""Converts a VMT's base and bump textures to PNGs and writes a vmat; returns the vmat's asset path."""
	vmt = None
	for d in cd:
		p = os.path.join( SRC, "materials", d.replace( "\\", "/" ), tex_name + ".vmt" )
		for cand in ( p, p.lower() ):
			if os.path.exists( cand ):
				vmt = cand
				break
		if vmt:
			break
	if not vmt:
		# case-insensitive search
		want = tex_name.lower() + ".vmt"
		for root, _, files in os.walk( os.path.join( SRC, "materials" ) ):
			for f in files:
				if f.lower() == want:
					vmt = os.path.join( root, f )
	name = "tfa_" + tex_name.lower()
	out_vmat = os.path.join( TEX_DIR, name + ".vmat" )
	rel = f"models/weapons/tfatex/{name}.vmat"
	if os.path.exists( out_vmat ):
		return rel
	base = bump = None
	if vmt:
		txt = open( vmt, encoding="latin-1" ).read()
		import re
		mb = re.search( r'\$basetexture"?\s+"?([^"\r\n]+)', txt, re.I )
		mn = re.search( r'\$bumpmap"?\s+"?([^"\r\n]+)', txt, re.I )
		base = mb.group( 1 ).strip() if mb else None
		bump = mn.group( 1 ).strip() if mn else None
	os.makedirs( TEX_DIR, exist_ok=True )

	def vtf_png( texpath, suffix ):
		if not texpath:
			return None
		want = ( "materials/" + texpath.replace( "\\", "/" ) + ".vtf" ).lower()
		for root, _, files in os.walk( os.path.join( SRC, "materials" ) ):
			for f in files:
				full = os.path.join( root, f )
				if os.path.relpath( full, SRC ).replace( "\\", "/" ).lower() == want:
					rgba, _ = read_vtf( full )
					png = os.path.join( TEX_DIR, f"{name}_{suffix}.png" )
					write_png( png, rgba[..., :4].copy(), alpha=False )
					return f"models/weapons/tfatex/{name}_{suffix}.png"
		print( "TFAPROP missing texture", texpath )
		return None

	color = vtf_png( base, "color" )
	normal = vtf_png( bump, "normal" )
	lines = ["// Generated by ModelSource/tfa_props.py from the Gmod TFA Star Wars packs (local only, never publish)", "", "Layer0", "{",
		'\tshader "shaders/complex.shader"', "", '\tg_flModelTintAmount "1.000"', '\tg_vColorTint "[1.000000 1.000000 1.000000 0.000000]"',
		f'\tTextureColor "{color or "materials/default/default_color.tga"}"',
		f'\tTextureNormal "{normal or "materials/default/default_normal.tga"}"',
		'\tTextureRoughness "materials/default/default_rough.tga"', '\tg_flMetalness "0.300"', "}"]
	with open( out_vmat, "w", newline="\n" ) as fh:
		fh.write( "\n".join( lines ) + "\n" )
	return rel


def build( name, Q, G ):
	raw = name in RAW
	paths, pos, ang, size = ( [RAW[name]], ( 0, 0, 0 ), ( 0, 0, 0 ), 1.0 ) if raw else GUNS[name]
	t, R, s = to_hand( pos, ang, size )
	Qi = Q.transposed()
	if raw:
		t, R, s, Qi, G = Vector( ( 0, 0, 0 ) ), Matrix.Identity( 3 ), 1.0, Matrix.Identity( 3 ), Vector( ( 0, 0, 0 ) )
	verts, faces, uvs, mats, mat_names = [], [], [], [], []
	for p in paths:
		m = load_model( os.path.join( SRC, p ) )
		for tex, ps, ns, tuv in m.tris:
			tn = m.textures[tex] if isinstance( tex, int ) and tex < len( m.textures ) else str( tex )
			if tn not in mat_names:
				mat_names.append( tn )
				mats.append( texture( None, tn, m.cd ) )
			base = len( verts )
			for q in ps:
				h = t + R @ ( Vector( q ) * s )
				verts.append( Qi @ ( h - G ) )
			faces.append( ( base, base + 1, base + 2, mat_names.index( tn ) ) )
			uvs.append( tuv )
	if raw:
		c = Vector( [( max( v[i] for v in verts ) + min( v[i] for v in verts ) ) / 2 for i in range( 3 )] )
		verts = [v - c for v in verts]
	# a few models lie across the hand after the WElements maths (their yaw is a quarter turn off ours): lay them along X
	if not raw and max( v.y for v in verts ) - min( v.y for v in verts ) > max( v.x for v in verts ) - min( v.x for v in verts ):
		quarter = Matrix.Rotation( math.pi / 2, 3, "Z" )
		verts = [quarter @ v for v in verts]
	L = Matrix.Identity( 3 ) if raw else level( verts )
	verts = [L @ v for v in verts]
	if name in PITCH_FIX:      # short chunky guns the levelling can't read (checked on a side-view render)
		fix = Matrix.Rotation( math.radians( PITCH_FIX[name] ), 3, "Y" )
		verts = [fix @ v for v in verts]
	if not raw and -min( v.x for v in verts ) > max( v.x for v in verts ):      # the barrel came out pointing back: turn it round
		turn = Matrix.Rotation( math.pi, 3, "Z" )
		verts = [turn @ v for v in verts]
	if name in FLIP:
		verts = [Matrix.Rotation( math.pi, 3, "Z" ) @ v for v in verts]
	if name in GRIP_FIX:
		# their hand placement came out at the muzzle: put the bottom of the pistol grip where the BF2 DC-15's sits instead
		lo_x, hi_x = min( v.x for v in verts ), max( v.x for v in verts )
		mid = [v for v in verts if lo_x + ( hi_x - lo_x ) * 0.1 < v.x < lo_x + ( hi_x - lo_x ) * 0.75]
		zmin = min( v.z for v in mid )
		bottom = [v for v in mid if v.z < zmin + 1.0]
		gx = sum( v.x for v in bottom ) / len( bottom )
		verts = [v + Vector( ( 2.0 - gx, 0, -3.1 - zmin ) ) for v in verts]
	# the grip sits on the gun's centre line
	yc = ( max( v.y for v in verts ) + min( v.y for v in verts ) ) / 2
	verts = [v - Vector( ( 0, yc, 0 ) ) for v in verts]

	me = bpy.data.meshes.new( name )
	me.from_pydata( [tuple( v ) for v in verts], [], [f[:3] for f in faces] )
	uvl = me.uv_layers.new()
	for i, poly in enumerate( me.polygons ):
		poly.material_index = faces[i][3]
		for k, li in enumerate( poly.loop_indices ):
			u, v = uvs[i][k]
			uvl.data[li].uv = ( u, 1.0 - v )
	for rel in mats:
		me.materials.append( bpy.data.materials.get( rel ) or bpy.data.materials.new( rel ) )
	me.validate()
	ob = bpy.data.objects.new( "tfa_" + name, me )
	bpy.context.scene.collection.objects.link( ob )

	lo, hi = bp.bounds( [ob] )
	asset = f"models/weapons/tfa_{name}"
	fbx_name = f"tfa_{name}_{bp.REV}"
	bpy.ops.object.select_all( action="DESELECT" )
	ob.select_set( True )
	bpy.context.view_layer.objects.active = ob
	bpy.ops.export_scene.fbx( filepath=os.path.join( OUT_DIR, fbx_name + ".fbx" ), use_selection=True, mesh_smooth_type="FACE", axis_forward="Y", axis_up="Z" )
	meta = os.path.join( OUT_DIR, fbx_name + ".fbx.meta" )
	if not os.path.exists( meta ):
		with open( meta, "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	tip = [v.co for v in ob.data.vertices if v.co.x > hi.x - 1.0]
	mz = sum( p.z for p in tip ) / len( tip ) if tip else 0.0
	with open( os.path.join( OUT_DIR, f"tfa_{name}.vmdl" ), "w", newline="\n" ) as fh:
		fh.write( bp.VMDL % { "fbx": f"models/weapons/{fbx_name}.fbx", "extra": bp.ATTACH % { "x": hi.x, "z": mz } } )
	revs = sorted( ( f for f in os.listdir( OUT_DIR ) if f.startswith( f"tfa_{name}_" ) and f.endswith( ".fbx" ) ),
		key=lambda f: os.path.getmtime( os.path.join( OUT_DIR, f ) ) )
	for old in revs[:-2]:
		for fn in ( old, old + ".meta" ):
			try:
				os.remove( os.path.join( OUT_DIR, fn ) )
			except OSError:
				pass
	print( f"TFAPROP {asset}: {len( me.polygons )} faces, muzzle forward {hi.x:.1f} up {mz:.1f}, bounds {[round( x, 1 ) for x in lo]} {[round( x, 1 ) for x in hi]}" )
	bpy.data.objects.remove( ob, do_unlink=True )


def main():
	only = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	bpy.ops.wm.read_factory_settings( use_empty=True )
	Q, G = calibrate()
	for name in list( GUNS ) + list( RAW ):
		if only and name not in only:
			continue
		build( name, Q, G )


if __name__ == "__main__":
	main()
