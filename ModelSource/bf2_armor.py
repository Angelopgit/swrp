"""
Armour attachments for the clone troopers: the pieces a trooper earns or picks in the armour customiser (Code/Characters/
SwrpArmor.cs) - the split and full kamas, the commander's pauldron and bandolier (gear_212), a shoulder strap (gear_91), the
heavy kit (part id "pauldron": bandolier, satchel, pack), visors, the helmet and shoulder antennas, the thigh holster - and
the bare clone head for helmet off (Jango Fett's face from BF2, as the clones are his). Each one is its
own skinned model on the same Citizen rig as the troopers (built with bf2_clones.py's posing and weight transfer), drawn
bone-merged onto the trooper's body, so the trooper models themselves are never touched or rescaled. LOCAL ONLY, like
every Battlefront II asset (CLAUDE.md IP rules). Source: X:\\SWRP Projects\\BF2_Extracted (Clone_Head was exported with
Tools/BatchExport/clone_head.txt).

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/bf2_armor.py [-- <part> ...]
    python ModelSource/bf2_armor_vmdl.py

Writes models/troopers/clone_armor_<part>_<rev>.fbx and the list Assets/models/troopers/armor.json.
"""
import os
import sys
import json

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import bf2_clones as bc

# part id -> [(mesh name, source fbx)]
PARTS = {
	"head": [( "head", "Clone_Head/heads_jangofett_01_mesh.fbx" )],
	"kama": [( "kama", "Clone_Heavy/l_heavy_preq_01_skirt_mesh.fbx" )],
	"kama_arc": [( "kama", "Clone_ARC_Trooper/l_assault_preq_01_arc_01_skirt_mesh.fbx" )],
	"pauldron": [( "pauldron", "Clone_Heavy/l_heavy_preq_01_parts_mesh.fbx" )],
	"gear_212": [( "gear", "Clone_Officer/l_officer_preq_01_parts_212_mesh.fbx" )],
	"gear_91": [( "gear", "Clone_Officer/l_officer_preq_01_parts_91_mesh.fbx" )],
	"rangefinder": [( "rangefinder", "Clone_Specialist/l_specialist_preq_01_visor_mesh.fbx" )],
	"macrobinoculars": [( "visor", "Clone_Heavy/l_heavy_preq_01_visor_mesh.fbx" )],
	"headgear": [( "headgear", "Clone_Officer/l_officer_preq_01_headgear_parts_mesh.fbx" )],
	"antenna": [( "antenna", "Clone_Officer/l_officer_shoulder_antenna_preq_01_mesh.fbx" )],
	"holster": [( "holster", "Clone_Officer/l_officer_preq_501_mesh.fbx" )],
	# BF2's clone commander cape (characters/bodyparts/cape_01, exported with Tools/BatchExport/officer_cape.txt): Officers
	# wear it by default (Angelo, 2026-10-14), in the battalion's colour (bf2_armor_paint.py)
	"cape": [( "cape", "Clone_Officer_Cape/cape_01_cloth_mesh.fbx" )],
}

# Jango's face and hair (the shared eye and teeth overrides in bf2_clones cover the rest)
bc.OVERRIDES.update( {
	"Clone_Head:mat_head": ( "T_Heads_JangoFett_01_C.png", "T_Heads_JangoFett_01_NS.png", "skin" ),
	"Clone_Head:mat_hair": ( "T_Hair_JangoFett_C.png", "T_Hair_JangoFett_N.png", "skin" ),
} )

# submeshes whose texture the by-name guess gets wrong (checked against each part's Textures folder)
bc.HINTS.update( {
	"l_assault_preq_01_arc_01_skirt_mesh:m_Arc_Trooper": "T_L_Assault_Preq_01_Arc_Skirt",
	"l_officer_preq_01_parts_212_mesh:M_L_Heavy_Preq_01": "../../Clone_Heavy/Textures/T_L_Heavy_Preq_01",
	"l_officer_preq_01_parts_91_mesh:M_gear": "../../Clone_Heavy/Textures/T_L_Heavy_Preq_91",
	"l_officer_preq_501_mesh:M_L_Heavy_Preq_01": "../../Clone_Heavy/Textures/T_L_Heavy_Preq_01",
	"l_officer_shoulder_antenna_preq_01_mesh:lambert5": "T_Officer_Headgear_Parts_Preq_01",
} )


def main():
	only = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	path = os.path.join( bc.OUT, "armor.json" )
	listing = json.load( open( path ) ) if os.path.exists( path ) else {}
	for part, meshes in PARTS.items():
		if only and part not in only:
			continue
		# weights come from the old trooper by nearest surface (bf2_clones.transfer_weights falls back to clone_trooper_r4),
		# so a kama follows the hips and thighs and a rangefinder the head
		fbx, mats = bc.build( f"armor_{part}", meshes )
		listing[part] = { "fbx": f"models/troopers/{fbx}.fbx", "meshes": mats }
		bc.log( "armor", part, "->", fbx )
	with open( path, "w", newline="\n" ) as fh:
		json.dump( listing, fh, indent=1 )


main()
