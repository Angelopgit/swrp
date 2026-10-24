"""
Battalion colours for the armour customiser (Code/Characters/SwrpArmor.cs), painted the way bf2_markings.py paints the
helmets, but into separate materials so nothing that script or the trooper vmdls own is touched:

  * body markings for every battalion: BF2 only ships a 501st body mask (arm, leg and chest stripes), so the other
    battalions get the same stripes in their own colour (t_l_assault_preq_01_body_mark_<battalion>);
  * kamas, pauldron gear and holsters in the battalion colour from the Coruscant Guard heavy mask's red channel
    (t_l_heavy_preq_01_mark_<battalion>).

SwrpArmor puts them on at runtime (a material override on the body's slot, and the attachments' material groups). LOCAL ONLY.

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/bf2_armor_paint.py
    python ModelSource/bf2_armor_vmdl.py
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

# the same colours as bf2_markings.py's helmets
COLOURS = {
	"501st": ( 0.16, 0.27, 0.58 ),
	"212th": ( 0.80, 0.42, 0.08 ),
	"104th": ( 0.43, 0.47, 0.51 ),
	"327th": ( 0.82, 0.66, 0.14 ),
	"41st": ( 0.27, 0.40, 0.18 ),
	"coruscant_guard": ( 0.62, 0.10, 0.11 ),
}

# material -> (mask, channels, battalions); the 501st already has body markings in the trooper models
JOBS = {
	"t_l_assault_preq_01_body": ( "L_Preq_T_501_Marking_Aussault_Heavy_Specialist_Phase2.png", "GB", [b for b in COLOURS if b != "501st"] ),
	"t_l_heavy_preq_01": ( "T_L_Heavy_Preq_01_CGuard.png", "R", list( COLOURS ) ),
	# the officer cape: the whole cloth dyed the battalion colour (its own folds and wear kept)
	"t_cape_01": ( None, "R", list( COLOURS ) ),
}


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
	shade = np.clip( base.mean( axis=2, keepdims=True ) * 1.08, 0, 1 )
	col = np.array( colour, np.float32 ) * shade
	for ch in channels:
		k = mask[:, :, "RGB".index( ch ):"RGB".index( ch ) + 1]
		out = out * ( 1 - k ) + col * k
	return out


def main():
	table = {}
	for mat, ( mask_file, channels, battalions ) in JOBS.items():
		src = os.path.join( MAT_DIR, f"{mat}_color.png" )
		vmat = os.path.join( MAT_DIR, f"{mat}.vmat" )
		if not os.path.exists( src ) or not os.path.exists( vmat ):
			print( "ARMORPAINT skip (no base):", mat )
			continue
		base = load( src )
		h, w = base.shape[:2]
		mask = load( os.path.join( MARKS, mask_file ), ( w, h ) ) if mask_file else np.ones( ( h, w, 4 ), np.float32 )
		for battalion in battalions:
			name = f"{mat}_mark_{battalion}"
			bp.write( os.path.join( MAT_DIR, f"{name}_color.png" ), paint( base[:, :, :3], mask, channels, COLOURS[battalion] ), w, h )
			text = open( vmat, encoding="utf-8" ).read().replace( f"{REL}/{mat}_color.png", f"{REL}/{name}_color.png" )
			with open( os.path.join( MAT_DIR, name + ".vmat" ), "w", newline="\n" ) as fh:
				fh.write( text )
			table.setdefault( battalion, {} )[mat] = f"{REL}/{name}.vmat"
			print( "ARMORPAINT", battalion, mat, "<-", mask_file, channels, flush=True )
	with open( os.path.join( bp.ASSETS, "models", "troopers", "armor_markings.json" ), "w", newline="\n" ) as fh:
		json.dump( table, fh, indent=1 )


main()
