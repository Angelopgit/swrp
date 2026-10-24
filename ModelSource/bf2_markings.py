"""
Battalion markings for the Battlefront II troopers (bf2_clones.py), from BF2's own unit marking masks in
X:\\SWRP Projects\\BF2_Extracted\\Models\\Clone_Unit_Markings (local only, never publish). The masks are drawn on the armour's
UVs: in the helmet masks blue is the unit's marking (the 212th's crest, the 104th's wolf face, the 327th's and 41st's
stripes, the Rex-pattern helmet the 501st and the Guard wear), green the wear and scuffs, red the visor and small details (left white); in the
501st body mask green and blue are the unit's arm, leg and chest markings. Each battalion's colour is painted into those
channels over the clone's own white armour (keeping its shading), and the result is a material group named by the
battalion id on every trooper model (bf2_clone_vmdl.py reads Assets/models/troopers/markings.json).

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/bf2_markings.py
    python ModelSource/bf2_clone_vmdl.py
"""
import os
import sys
import json

import bpy
import numpy as np

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import bf2_props as bp

MARKS = r"X:\SWRP Projects\BF2_Extracted\Models\Clone_Unit_Markings"
MAT_DIR = os.path.join( bp.ASSETS, "models", "troopers", "materials" )
REL = "models/troopers/materials"

HELMET = "t_l_assault_preq_01_helmet"
BODY = "t_l_assault_preq_01_body"
HEAVY = "t_l_heavy_preq_01"

REX_HELMET = "T_L_Preq_01_Assault_Heavy_Helmet_Phase2.png"

# Per class and battalion, as BF2 paints them (2026-10-13, Angelo: "find the models for each battalion... the appropriate model
# for each class"). BF2 has no separate battalion models: every unit is the same class armour with that unit's marking sheet
# and colour, read from the game's own skin variations (Characters/Markings/<unit>/L_<Class>_Preq_01_CM_T_<unit>: the
# Markings_Color and which channel of the sheet that class uses; dumped with Tools/BatchDump). Colours are sRGB.
# battalion -> (colour, body sheet, helmet sheet for Trooper/Heavy, helmet sheet for Specialist/Officer)
UNITS = {
	"501st": ( ( 18, 48, 93 ), "L_Preq_T_501_Marking_Aussault_Heavy_Specialist_Phase2.png", REX_HELMET, "T_L_Preq_01_Specialist_Officer_Helmet_Phase2.png" ),
	"212th": ( ( 162, 110, 26 ), "L_Preq_T_212_Markings.png", "L_Preq_T_212_Marking_Assault_Heavy_Helmet_Phase2.png", "L_Preq_T_212_Marking_Specialist_Officer_Helmet_Phase2.png" ),
	"104th": ( ( 88, 91, 96 ), "L_Preq_T_104_Markings.png", "T_L_Preq_T_104_Assault_Heavy_Helmets_Phase2.png", "T_L_Preq_T_104_Specialist_Officer_Helmets_Phase2.png" ),
	"327th": ( ( 169, 136, 41 ), "L_Preq_T_327_Brown.png", "T_L_Preq_T_327_Assault_Heavy_HelmetA_Phase2.png", "T_L_Preq_T_327_Specialist_Officer_HelmetO_Phase2.png" ),
	"41st": ( ( 71, 87, 74 ), "L_Preq_Marking_T41.png", "T_L_Preq_T_41Green_Assault_Heavy_HelmetA_Phase2.png", "T_L_Preq_T_41Green_Specialst_Officer_HelmetH_Phase2.png" ),
	"coruscant_guard": ( ( 123, 27, 34 ), "L_Preq_T_CoruscantGuards_Markings.png", REX_HELMET, "T_L_Preq_01_Specialist_Officer_Helmet_Phase2.png" ),
}
# the body sheet's channel each class wears, per unit (Markings_ChannelSelection; none given = R)
BODY_CHANNEL = {
	"501st": { "trooper": "R", "heavy": "G", "specialist": "B", "officer": "R" },
	"212th": { "trooper": "R", "heavy": "G", "specialist": "R", "officer": "G" },
	"104th": { "trooper": "R", "heavy": "G", "specialist": "R", "officer": "G" },
	"327th": { "trooper": "R", "heavy": "G", "specialist": "R", "officer": "G" },
	# (41st and Guard: their G pattern came out as scattered stripes on Heavy and Officer, so every class wears R)
	"41st": { "trooper": "R", "heavy": "R", "specialist": "R", "officer": "R" },
	"coruscant_guard": { "trooper": "R", "heavy": "R", "specialist": "R", "officer": "R" },
}
# The body sheets are drawn on the body's SECOND UV set (BF2's UV_Switch): read from MARKS/uv0, the same sheets baked into
# the first UV set our textures use (scratchpad uvremap.py, 2026-10-14). Reading them straight from MARKS put the paint in
# random patches ("random orange blotches", Angelo).
BODY_SHEETS = os.path.join( MARKS, "uv0" )
# (the 41st's and the Guard's sheets are drawn on the first UV set already: remapped they came out as scattered patches)
SECOND_UV = { "501st", "212th", "104th", "327th" }
CLASSES = ( "trooper", "heavy", "specialist", "officer" )


def load( path, size=None ):
	im = bpy.data.images.load( path, check_existing=False )
	im.colorspace_settings.name = "Non-Color"
	if size and tuple( im.size ) != size:
		im.scale( *size )
	w, h = im.size
	a = np.empty( w * h * 4, np.float32 )
	im.pixels.foreach_get( a )
	bpy.data.images.remove( im )
	return a.reshape( h, w, 4 )


def paint( base, mask, channels, colour ):
	out = base.copy()
	shade = np.clip( base.mean( axis=2, keepdims=True ) * 1.08, 0, 1 )        # the armour's own shading and wear
	col = np.array( colour, np.float32 ) * shade
	for ch in "RGB":
		k = mask[:, :, "RGB".index( ch ):"RGB".index( ch ) + 1]
		if ch in channels:
			out = out * ( 1 - k ) + col * k
	return out


def mark( mat, name, mask_file, channels, colour, size_cache ):
	src = os.path.join( MAT_DIR, f"{mat}_color.png" )
	vmat = os.path.join( MAT_DIR, f"{mat}.vmat" )
	if not os.path.exists( src ) or not os.path.exists( vmat ):
		print( "MARKINGS skip (no base):", mat )
		return None
	if mat not in size_cache:
		size_cache[mat] = load( src )
	base = size_cache[mat]
	h, w = base.shape[:2]
	mask = load( os.path.join( MARKS, mask_file ), ( w, h ) )
	bp.write( os.path.join( MAT_DIR, f"{name}_color.png" ), paint( base[:, :, :3], mask, channels, colour ), w, h )
	text = open( vmat, encoding="utf-8" ).read().replace( f'{REL}/{mat}_color.png', f'{REL}/{name}_color.png' )
	with open( os.path.join( MAT_DIR, name + ".vmat" ), "w", newline="\n" ) as fh:
		fh.write( text )
	print( "MARKINGS", name, "<-", mask_file, channels )
	return f"{REL}/{name}.vmat"


def main():
	cache = {}
	manifest = json.load( open( os.path.join( bp.ASSETS, "models", "troopers", "manifest.json" ) ) )
	out = { cls: {} for cls in manifest }
	for battalion, ( rgb, body_sheet, helmet_ah, helmet_so ) in UNITS.items():
		colour = tuple( c / 255.0 for c in rgb )
		# the helmet: one per helmet sheet (Trooper and Heavy share one, Specialist and Officer the other); the unit's marking is
		# the sheet's blue channel
		helm = {
			"ah": mark( HELMET, f"{HELMET}_{battalion}", helmet_ah, "B", colour, cache ),
			"so": mark( HELMET, f"{HELMET}_{battalion}_so", helmet_so, "B", colour, cache ),
		}
		for cls in CLASSES:
			if cls not in out:
				continue
			sheet = os.path.join( BODY_SHEETS if battalion in SECOND_UV else MARKS, body_sheet )
			body = mark( BODY, f"{BODY}_{battalion}_{cls}", sheet, BODY_CHANNEL[battalion][cls], colour, cache )
			g = out[cls].setdefault( battalion, {} )
			if body: g[BODY] = body
			h = helm["ah" if cls in ( "trooper", "heavy" ) else "so"]
			if h: g[HELMET] = h
		if battalion == "coruscant_guard" and "heavy" in out:
			cg = mark( HEAVY, f"{HEAVY}_{battalion}", "T_L_Heavy_Preq_01_CGuard.png", "R", colour, cache )
			if cg: out["heavy"][battalion][HEAVY] = cg
	with open( os.path.join( bp.ASSETS, "models", "troopers", "markings.json" ), "w", newline="\n" ) as fh:
		json.dump( out, fh, indent=1 )

main()
