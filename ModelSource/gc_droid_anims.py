"""
The droids' animations from the Galactic Contention Squad mod (exported as USD by Squad_Mod_2428425228\\Tools\\GcExport),
retargeted onto the s&box human reference skeleton and installed as one animation list per droid. LOCAL ONLY (GC's work, see
gc_droids.py). The B1 gets the mod's B1 walk, run, crouch and sprint loops (the clone rifle poses stay on top, it carries an
E-5); the B2 gets its own walk, jog, crouch walk and sprint and its idle and crouch (it uses holdtype 0: its clips already
hold the wrist guns up, and the pistol holdtype layer raised an arm in game).

    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --factory-startup -P ModelSource/gc_droid_anims.py
    (same) -- --install                                    writes gc_droid_<id>_animationlist.vmdl_prefab and points the vmdls at it

Retarget: the droid's Squad skeleton is posed onto the reference rest exactly as gc_droids.py poses it onto the rig (P0).
Each frame, every mapped reference bone takes the source bone's world rotation change from P0 (R = S(t) S(P0)^-1 R_rest);
the pelvis also takes the source pelvis' movement. The clips are in place in Squad (the capsule moves the soldier), so
root motion is added at the speed the planted foot slides back, in the direction it slides from, which is what the
animgraph's speed matching reads. The IK helpers (hand/foot targets, the two-hand ikrule bones) follow the hands and feet,
so the graph's IK holds the clip's own pose. Clips go to models/troopers/gc_anims/<id>@<slot>.fbx (30 fps).
"""
import os
import re
import sys
import json
import math
import uuid
import shutil

HERE = os.path.dirname( os.path.abspath( __file__ ) )
ASSETS = os.path.join( os.path.dirname( HERE ), "Assets" )
ANIM_DIR = os.path.join( ASSETS, "models", "troopers", "gc_anims" )
GC = r"X:\SWRP Projects\Squad_Mod_2428425228\Export"
ART = os.path.join( GC, "ANE_BASE", "Art" )
FPS = 30

# droid -> animgraph slot -> GC clip (<name>.usda under its folder). Slots without a clip keep the clone/stock one.
SETS = {
	"b1": { "dir": os.path.join( ART, "Animation", "B1_and_Magna_Animations" ), "glb": "b1", "slots": {
	"WalkFast_N": "B1_Rifle_WalkFwdLoop201707",
	"WalkFast_NE": "B1_Rifle_WalkFwdLoop201707_DiagonalRight",
	"WalkFast_NW": "B1_Rifle_WalkFwdLoop201707_DiagonalLeft",
	"WalkFast_S": "B1_Rifle_WalkBwdLoop201707",
	"WalkFast_E": "B1_Walk_Strafe_Right2",
	"WalkFast_W": "B1_Walk_Strafe_Left2",
	"Run_N_m": "B1_Rifle_RunFwdLoop_201708",
	"Run_NE": "B1_Rifle_RunFwdLoop_201708_DiagonalRight",
	"Run_NW": "B1_Rifle_RunFwdLoop_201708_DiagonalLeft",
	"Run_S": "B1_Rifle_RunBwdLoop_201708_v2",
	"Run_SE": "B1_Rifle_RunBwdLoop_201708_DiagonalRight_v2",
	"Run_SW": "B1_Rifle_RunBwdLoop_201708_DiagonalLeft_v2",
	"Run_E": "B1_Pistol_RunRightLoop_Onehanded",
	"Run_W": "B1_Pistol_RunLeftLoop_Onehanded",
	"CrouchWalk_N": "B1_Crouch_Forward",
	"CrouchWalk_NE": "B1_Crouch_Forward_DiagonalRight",
	"CrouchWalk_NW": "B1_Crouch_Forward_DiagonalLeft",
	"CrouchWalk_S": "B1_Crouch_Backward",
	"CrouchWalk_SE": "B1_Crouch_Backward_DiagonalRight",
	"CrouchWalk_SW": "B1_Crouch_Backward_DiagonalLeft",
	"CrouchWalk_E": "B1_Crouch_Strafe_Right",
	"CrouchWalk_W": "B1_Crouch_Strafe_Left",
	"Sprint_N_m": "B1_A_Char_Rifle_Sprint_2017",
} },
	"b2": { "dir": os.path.join( ART, "Weapons", "B2_SBD" ), "glb": "b2", "slots": {
		"WalkFast_N": "A_Char_B2_fwd", "WalkFast_NE": "A_Char_B2_fwd_r", "WalkFast_NW": "A_Char_B2_fwd_l",
		"WalkFast_S": "A_Char_B2_bwd", "WalkFast_SE": "A_Char_B2_bwd_r", "WalkFast_SW": "A_Char_B2_bwd_l",
		"WalkFast_E": "A_Char_B2_right", "WalkFast_W": "A_Char_B2_left",
		"Run_N_m": "A_Char_B2_Stand_Jog_fwd", "Run_NE": "A_Char_B2_Stand_Jog_fwd_r", "Run_NW": "A_Char_B2_Stand_Jog_fwd_l",
		"Run_S": "A_Char_B2_Stand_Jog_bwd", "Run_SE": "A_Char_B2_Stand_Jog_bwd_r", "Run_SW": "A_Char_B2_Stand_Jog_bwd_l",
		"Run_E": "A_Char_B2_Stand_Jog_right", "Run_W": "A_Char_B2_Stand_Jog_left",
		"CrouchWalk_N": "A_Char_B2_Crouch_Walk_fwd", "CrouchWalk_NE": "A_Char_B2_Crouch_Walk_fwd_r", "CrouchWalk_NW": "A_Char_B2_Crouch_Walk_fwd_l",
		"CrouchWalk_S": "A_Char_B2_Crouch_Walk_bwd", "CrouchWalk_SE": "A_Char_B2_Crouch_Walk_bwd_r", "CrouchWalk_SW": "A_Char_B2_Crouch_Walk_bwd_l",
		"CrouchWalk_E": "A_Char_B2_Crouch_Walk_r", "CrouchWalk_W": "A_Char_B2_Crouch_Walk_l",
		"Sprint_N_m": "A_B2_Stand_Sprint",
		"IdlePose_Default": "A_Char_B2_Stand", "CrouchIdlePose_Default": "A_Char_B2_Crouch_3p",
	} },
}
# the clone troopers (players and NPCs): Squad's own soldier set (Angelo, 2026-10-13; Offworld's work, local only). Exported
# by GcExport with GC_ROOT=base into Squad_Mod_2428425228\Export_SquadBase. The body comes from these; the rifle stays
# pinned to both hands by SwrpEmotes' shoulder IK, which follows the animated shoulder.
SQUAD = r"X:\SWRP Projects\Squad_Mod_2428425228\Export_SquadBase\SquadGame\Content\Art\Soldier_Animations"
CLONE_GLB = os.path.join( GC, r"ANE_BASE\Art\Soldier2\CloneArmy\NEW\X_NewTest\Phase_II.glb" )     # a soldier on Squad's skeleton
SETS["clone"] = { "dir": SQUAD, "glb": CLONE_GLB, "list": "squad_clone", "slots": {
	"WalkFast_N": "Rifle_WalkFwdLoop201707", "WalkFast_NE": "Rifle_WalkFwdLoop201707_DiagonalRight",
	"WalkFast_NW": "Rifle_WalkFwdLoop201707_DiagonalLeft", "WalkFast_S": "Rifle_WalkBwdLoop201707",
	"WalkFast_SE": "Rifle_WalkBwdLoop201707_DiagonalRight", "WalkFast_SW": "Rifle_WalkBwdLoop201707_DiagonalLeft",
	"WalkFast_E": "Walk_Strafe_Right2", "WalkFast_W": "Walk_Strafe_Left2",
	"Run_N_m": "Rifle_RunFwdLoop_201708", "Run_NE": "Rifle_RunFwdLoop_201708_DiagonalRight", "Run_NW": "Rifle_RunFwdLoop_201708_DiagonalLeft",
	"Run_S": "Rifle_RunBwdLoop_201708_v2", "Run_SE": "Rifle_RunBwdLoop_201708_DiagonalRight_v2", "Run_SW": "Rifle_RunBwdLoop_201708_DiagonalLeft_v2",
	"Run_E": "Rifle_RunLoop_201708_Strafe_Right", "Run_W": "Rifle_RunLoop_201708_Strafe_Left",
	"CrouchWalk_N": "Crouch_Forward", "CrouchWalk_NE": "Crouch_Forward_DiagonalRight", "CrouchWalk_NW": "Crouch_Forward_DiagonalLeft",
	"CrouchWalk_S": "Crouch_Backward", "CrouchWalk_SE": "Crouch_Backward_DiagonalRight", "CrouchWalk_SW": "Crouch_Backward_DiagonalLeft",
	"CrouchWalk_E": "Crouch_Strafe_Right", "CrouchWalk_W": "Crouch_Strafe_Left",
	"Sprint_N_m": "A_Char_Rifle_Sprint_2017",
	# (the rifle stances stay the clone set's: Squad's bladed, wide stances under SwrpEmotes' shoulder IK twisted the torso as
	# the trooper turned, Angelo 2026-10-13)
} }
# prone: the graph has no prone, so these play as sequences with the graph off (SwrpProne), authored on the trooper rig itself
# (no proportion trick outside the graph) and kept in place (the controller moves the body)
SETS["prone"] = { "dir": SQUAD, "glb": CLONE_GLB, "list": "squad_clone", "target": "rig", "slots": {
	"squad_prone_idle": "Rifle_ProneIdle201603", "squad_prone_fwd": "Generic_Prone_WalkFwdLoop201708",
	"squad_prone_bwd": "Generic_Prone_WalkBwdLoop201708", "squad_prone_left": "Prone_Strafe_Left", "squad_prone_right": "Prone_Strafe_Right",
	"squad_stand_to_prone": "rifle_stand2prone_nonadditive", "squad_prone_to_stand": "Prone_to_Stand_Nonadditive",
} }
# relaxed (rifle on safety, carried low): Squad's patrol walk forward, the rest as the Squad set (Angelo, 2026-10-19: "a
# relaxed patrol walk with the legs"; the human graph's only walk is WalkFast, a brisk half jog)
SETS["patrol"] = { "dir": SQUAD, "glb": CLONE_GLB, "list": "patrol_clone", "slots": {
	"WalkFast_N": "Patrol_Forward_Rifle_UsaPouch",
} }
# Garry's Mod's own player animations (HL2MP m_anm.mdl, Valve's; LOCAL ONLY), decompiled to SMD with Crowbar
# (X:\SWRP Projects\GMod_Extracted\m_anm). One set per GMod hold type, each its own "_gmod_<hold>" model variant
# (Angelo, 2026-10-19: "Use Garry's Mod Animations Standing, Running, Sprinting"; "just like how Gary's mod is with TFA")
GMOD = r"X:\SWRP Projects\GMod_Extracted\m_anm\m_anm_anims"
GMOD_QC = r"X:\SWRP Projects\GMod_Extracted\m_anm\m_anm.qc"
GMOD_DIRS = ( "N", "NE", "NW", "S", "SE", "SW", "E", "W" )
GMOD_HOLDS = {      # hold -> (walk clip, run clip, crouch walk clip) with {d} for the direction, idle, crouch idle
	"normal": ( "a_Walk{d}", "a_Run{d}", "a_Cwalk{d}", "idle_all_01", "cidle_all" ),
	"ar2": ( "a_walking_walk_ar2_{d}", "a_running_run_ar2_{d}", "a_CrouchWalking_cwalk_ar2_{d}", "idle_ar2", "cidle_ar2" ),
	"passive": ( "a_walking_walk_passive_{d}", "a_running_run_passive_{d}", "a_CrouchWalking_cwalk_passive_{d}", "idle_passive", "cidle_passive" ),
	"pistol": ( "a_walking_walk_pistol_{d}", "a_running_run_pistol_{d}", "a_CrouchWalking_cwalk_pistol_{d}", "idle_pistol", "cidle_pistol" ),
}
# TFA's other hold types (its base, common/anims.lua): smg and shotgun guns, and its aim-down-sights holds: rpg (rifles,
# shouldered) and revolver (pistols, two-handed); the TFA Star Wars pack's guns are ar2, smg, pistol, shotgun (scratchpad
# tfa_holdtypes.txt, 2026-10-19)
for _h in ( "smg1", "shotgun", "rpg", "revolver" ):
	GMOD_HOLDS[_h] = ( f"a_walking_walk_{_h}_{{d}}", f"a_running_run_{_h}_{{d}}", f"a_CrouchWalking_cwalk_{_h}_{{d}}", f"idle_{_h}", f"cidle_{_h}" )
for _hold, ( _walk, _run, _cwalk, _idle, _cidle ) in GMOD_HOLDS.items():
	_slots = { "IdlePose_Default": _idle, "CrouchIdlePose_Default": _cidle }
	for _d in GMOD_DIRS:
		_slots[f"WalkFast_{_d}"] = _slots[f"Walk_{_d}"] = _walk.format( d=_d )
		_slots[f"Run_{_d}" if _d != "N" else "Run_N_m"] = _run.format( d=_d )
		_slots[f"CrouchWalk_{_d}"] = _cwalk.format( d=_d )
	_slots["Run_N"] = _slots["Run_N_f"] = _run.format( d="N" )
	for _d in ( "N", "NE", "NW" ):
		_slots[f"Sprint_{_d}_m"] = _run.format( d=_d )      # GMod sprints on its run, just faster
	SETS[f"gmod_{_hold}"] = { "dir": GMOD, "smd": True, "list": f"gmod_{_hold}", "slots": _slots }
CLONE_VMDLS = ["clone_trooper_drill.vmdl", "clone_heavy_drill.vmdl", "clone_specialist_drill.vmdl", "clone_officer_drill.vmdl"]
LOCOMOTION = ( "WalkFast_", "Walk_", "Run_", "CrouchWalk_", "Sprint_" )
# the expected travel direction per slot in the reference's space (faces -Y, its right is -X); used to check the clip
DIRS = { "N": ( 0, -1 ), "S": ( 0, 1 ), "E": ( -1, 0 ), "W": ( 1, 0 ), "NE": ( -0.707, -0.707 ), "NW": ( 0.707, -0.707 ),
	"SE": ( -0.707, 0.707 ), "SW": ( 0.707, 0.707 ) }


def slot_dir( slot ):
	return DIRS[slot.split( "_" )[1]]


def is_locomotion( slot ):
	return slot.startswith( LOCOMOTION )


# =================================================================================================== install (plain Python)
CITIZEN = r"X:\SteamLibrary\steamapps\common\sbox\addons\citizen\assets"


def process_nodes( prefab, steps, ns ):
	"""The children of a stock human_ani_process_* prefab (AnimAlign, ExtractMotion) with its AnimEvents swapped for ours."""
	t = open( os.path.join( CITIZEN, *prefab.split( "/" ) ), encoding="utf-8" ).read()
	lb = t.index( "[", t.index( "children =" ) )
	depth, rb = 0, lb
	for k in range( lb, len( t ) ):
		depth += { "[": 1, "]": -1 }.get( t[k], 0 )
		if depth == 0:
			rb = k
			break
	# top-level child blocks of the list, braces matched outside strings
	blocks, depth, start, in_str = [], 0, None, False
	for k in range( lb + 1, rb ):
		c = t[k]
		if c == '"' and t[k - 1] != "\\":
			in_str = not in_str
		elif not in_str and c == "{":
			if depth == 0:
				start = k
			depth += 1
		elif not in_str and c == "}":
			depth -= 1
			if depth == 0:
				blocks.append( t[start:k + 1] )
	inner = "".join( b + ",\n" for b in blocks if '_class = "AnimEvent"' not in b ).rstrip()
	vol = "0.5" if "crouch" in prefab else "0.75"
	for f, side in steps:
		inner += ( '\n{\n\t_class = "AnimEvent"\n\tevent_class = "AE_FOOTSTEP"\n\tevent_frame = %d\n\tevent_keys = \n\t{\n'
			'\t\tAttachment = "foot_%s"\n\t\tFoot = "%d"\n\t\tVolume = %s\n\t}\n},' % ( f, side, 0 if side == "L" else 1, vol ) )
	return inner.strip() + "\n"


def install():
	sys.path.insert( 0, r"X:\SWRP Projects\Clonetrooper\sbox_build\anim" )
	src = os.path.join( ASSETS, "models", "clones", "clone_animationlist.vmdl_prefab" )      # the clone rifle poses stay
	text = open( src, encoding="utf-8" ).read()
	# the block helpers from the clone installer (it runs main() on import, so lift them out of its source)
	code = open( r"X:\SWRP Projects\Clonetrooper\sbox_build\anim\install_clone_anims.py", encoding="utf-8" ).read()
	ns = {}
	exec( compile( "import re\n" + code[code.index( "def block_at" ):code.index( "def main" )], "install_clone_anims", "exec" ), ns )
	for did in SETS:
		if did not in ( "clone", "prone", "patrol" ) and not did.startswith( "gmod_" ):
			install_one( did, text, ns )
	install_clone( text, ns )
	install_patrol( text, ns )
	install_gmod( text, ns )
	install_slowwalk( ns )


SLOWWALK_VMDLS = ["clone_trooper.vmdl", "clone_heavy.vmdl", "clone_specialist.vmdl", "clone_officer.vmdl", "gc_cmdr_rex.vmdl", "gc_cmdr_wolffe.vmdl"]


def install_slowwalk( ns ):
	"""The "_slowwalk" variants (Alt held, stock animations): the s&box human list with Garry's Mod's own slow walk
	(a_Walk*, retargeted as gmod_normal) in the walk slots (Angelo, 2026-10-19: "implement the GMod Alt slow walk")."""
	report = json.load( open( os.path.join( ANIM_DIR, "report_gmod_normal.json" ) ) )
	walk = { k: v for k, v in report.items() if k.startswith( "WalkFast_" ) }
	text = open( os.path.join( CITIZEN, "models", "citizen_human", "prefabs", "human_animationlist.vmdl_prefab" ), encoding="utf-8" ).read()
	lst = patch_slots( "gmod_normal", text, ns, walk )
	dst = os.path.join( ASSETS, "models", "troopers", "slowwalk_animationlist.vmdl_prefab" )
	with open( dst, "w", encoding="utf-8", newline="\n" ) as fh:
		fh.write( lst )
	if not os.path.exists( dst + ".meta" ):
		with open( dst + ".meta", "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	for v in SLOWWALK_VMDLS:
		src = os.path.join( ASSETS, "models", "troopers", v )
		out = src.replace( ".vmdl", "_slowwalk.vmdl" )
		s = open( src, encoding="utf-8" ).read()
		assert "models/citizen_human/prefabs/human_animationlist.vmdl_prefab" in s, v
		with open( out, "w", encoding="utf-8", newline="\n" ) as fh:
			fh.write( s.replace( "models/citizen_human/prefabs/human_animationlist.vmdl_prefab", "models/troopers/slowwalk_animationlist.vmdl_prefab" ) )
		if not os.path.exists( out + ".meta" ):
			with open( out + ".meta", "w", newline="\n" ) as fh:
				fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	print( "INSTALLED slow walk", ", ".join( sorted( walk ) ), "->", ", ".join( v.replace( ".vmdl", "_slowwalk.vmdl" ) for v in SLOWWALK_VMDLS ) )


def install_gmod( text, ns ):
	"""The "_gmod_<hold>" variants: the clone list with that GMod hold's idle, walk, run, sprint and crouch in the slots."""
	for did in [d for d in SETS if d.startswith( "gmod_" )]:
		rp = os.path.join( ANIM_DIR, f"report_{did}.json" )
		if not os.path.exists( rp ):
			continue
		have = { m[0] for m in ns["anim_file_blocks"]( text ) }      # (Walk_* and Run_N are the graph's, not clips in the list)
		lst = patch_slots( did, text, ns, { k: v for k, v in json.load( open( rp ) ).items() if k in have } )
		dst = os.path.join( ASSETS, "models", "troopers", f"{did}_animationlist.vmdl_prefab" )
		with open( dst, "w", encoding="utf-8", newline="\n" ) as fh:
			fh.write( lst )
		if not os.path.exists( dst + ".meta" ):
			with open( dst + ".meta", "w", newline="\n" ) as fh:
				fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
		for v in CLONE_VMDLS + ["gc_cmdr_rex_drill.vmdl", "gc_cmdr_wolffe_drill.vmdl"]:      # the commanders too
			drill = os.path.join( ASSETS, "models", "troopers", v )
			out = drill.replace( "_drill.vmdl", f"_{did}.vmdl" )
			with open( out, "w", encoding="utf-8", newline="\n" ) as fh:
				fh.write( open( drill, encoding="utf-8" ).read().replace( "models/clones/clone_animationlist.vmdl_prefab",
					f"models/troopers/{did}_animationlist.vmdl_prefab" ) )
			if not os.path.exists( out + ".meta" ):
				with open( out + ".meta", "w", newline="\n" ) as fh:
					fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
		print( "INSTALLED", did, "->", ", ".join( v.replace( "_drill", "_" + did ) for v in CLONE_VMDLS ) )


def install_patrol( text, ns ):
	"""The "_patrol" variants (relaxed): the Squad set with the patrol walk in WalkFast_N."""
	clone = json.load( open( os.path.join( ANIM_DIR, "report_clone.json" ) ) )
	patrol = json.load( open( os.path.join( ANIM_DIR, "report_patrol.json" ) ) )
	text = patch_slots( "clone", text, ns, { k: v for k, v in clone.items() if k not in patrol } )
	text = patch_slots( "patrol", text, ns, patrol )
	dst = os.path.join( ASSETS, "models", "troopers", "patrol_clone_animationlist.vmdl_prefab" )
	with open( dst, "w", encoding="utf-8", newline="\n" ) as fh:
		fh.write( text )
	if not os.path.exists( dst + ".meta" ):
		with open( dst + ".meta", "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	for v in CLONE_VMDLS:
		squad = os.path.join( ASSETS, "models", "troopers", v.replace( "_drill.vmdl", "_squad.vmdl" ) )
		out = squad.replace( "_squad.vmdl", "_patrol.vmdl" )
		with open( out, "w", encoding="utf-8", newline="\n" ) as fh:
			fh.write( open( squad, encoding="utf-8" ).read().replace( "models/troopers/squad_clone_animationlist.vmdl_prefab",
				"models/troopers/patrol_clone_animationlist.vmdl_prefab" ) )
		if not os.path.exists( out + ".meta" ):
			with open( out + ".meta", "w", newline="\n" ) as fh:
				fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	print( "INSTALLED patrol", ", ".join( patrol ), "->", ", ".join( v.replace( "_drill", "_patrol" ) for v in CLONE_VMDLS ) )


def install_clone( text, ns ):
	report = json.load( open( os.path.join( ANIM_DIR, "report_clone.json" ) ) )
	prone = json.load( open( os.path.join( ANIM_DIR, "report_prone.json" ) ) )
	text = patch_slots( "clone", text, ns, report )
	seqs = ""
	for name, e in prone.items():
		looping = "true" if e.get( "loop", True ) else "false"
		seqs += ( '\n\t\t\t\t\t{\n\t\t\t\t\t\t_class = "AnimFile"\n\t\t\t\t\t\tname = "%s"\n\t\t\t\t\t\tactivity_name = ""\n'
			'\t\t\t\t\t\tactivity_weight = 1\n\t\t\t\t\t\tweight_list_name = ""\n\t\t\t\t\t\tfade_in_time = 0.2\n\t\t\t\t\t\tfade_out_time = 0.2\n'
			'\t\t\t\t\t\tlooping = %s\n\t\t\t\t\t\tdelta = false\n\t\t\t\t\t\tworldSpace = false\n\t\t\t\t\t\thidden = false\n'
			'\t\t\t\t\t\tanim_markup_ordered = false\n\t\t\t\t\t\tdisable_compression = false\n\t\t\t\t\t\tdisable_interpolation = false\n'
			'\t\t\t\t\t\tenable_scale = false\n\t\t\t\t\t\tsource_filename = "models/troopers/gc_anims/prone@%s.fbx"\n'
			'\t\t\t\t\t\tstart_frame = -1\n\t\t\t\t\t\tend_frame = -1\n\t\t\t\t\t\tframerate = -1.0\n\t\t\t\t\t\ttake = 0\n\t\t\t\t\t\treverse = false\n\t\t\t\t\t},' ) % ( name, looping, name )
	# the list's top level children: append the sequences after its first "children =\n[" (the AnimationList root)
	i = text.index( "[", text.index( "children =" ) )
	text = text[:i + 1] + seqs + text[i + 1:]
	dst = os.path.join( ASSETS, "models", "troopers", "squad_clone_animationlist.vmdl_prefab" )
	with open( dst, "w", encoding="utf-8", newline="\n" ) as fh:
		fh.write( text )
	if not os.path.exists( dst + ".meta" ):
		with open( dst + ".meta", "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	# the "_squad" variants: the _drill model with this list. Unarmed troopers keep the _drill one (the original walk and run,
	# Angelo 2026-10-13); SwrpPlayer swaps to _squad while a weapon is out, NPC clones always use it
	for v in CLONE_VMDLS:
		path = os.path.join( ASSETS, "models", "troopers", v )
		s = open( path, encoding="utf-8" ).read()
		s = re.sub( r'target_file = "models/troopers/squad_clone_animationlist\.vmdl_prefab"',
			'target_file = "models/clones/clone_animationlist.vmdl_prefab"', s )
		with open( path, "w", encoding="utf-8", newline="\n" ) as fh:
			fh.write( s )
		squad = path.replace( "_drill.vmdl", "_squad.vmdl" )
		with open( squad, "w", encoding="utf-8", newline="\n" ) as fh:
			fh.write( s.replace( 'target_file = "models/clones/clone_animationlist.vmdl_prefab"',
				'target_file = "models/troopers/squad_clone_animationlist.vmdl_prefab"' ) )
		if not os.path.exists( squad + ".meta" ):
			with open( squad + ".meta", "w", newline="\n" ) as fh:
				fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	print( "INSTALLED clone", len( report ), "slots,", len( prone ), "prone sequences ->", ", ".join( CLONE_VMDLS ) )


def patch_slots( did, text, ns, report ):
	changed = []
	for m in sorted( ns["anim_file_blocks"]( text ), key=lambda x: -x[1] ):
		name, s, e = m
		if name not in report:
			continue
		blk = text[s:e]
		blk = re.sub( r'source_filename = "[^"]+"', f'source_filename = "models/troopers/gc_anims/{did}@{name}.fbx"', blk, count=1 )
		blk = ns["drop_children"]( blk, r'anim_name = "_CONVERT_CITIZEN_ANIMS"' )
		pm = re.search( r'target_file = "(models/citizen_human/prefabs/human_ani_process_\w+\.vmdl_prefab)"', blk )
		if pm and "steps" in report[name]:
			ps, pe = ns["block_at"]( blk, pm.start() )
			while pe < len( blk ) and blk[pe] == ",":
				pe += 1
			blk = blk[:ps] + process_nodes( pm.group( 1 ), report[name]["steps"], ns ) + blk[pe:]
		text = text[:s] + blk + text[e:]
		changed.append( name )
	missing = set( report ) - set( changed )
	assert not missing, f"slots not in the list: {missing}"
	return text


def install_one( did, text, ns ):
	report = json.load( open( os.path.join( ANIM_DIR, f"report_{did}.json" ) ) )
	changed = []
	for m in sorted( ns["anim_file_blocks"]( text ), key=lambda x: -x[1] ):
		name, s, e = m
		if name not in report:
			continue
		blk = text[s:e]
		blk = re.sub( r'source_filename = "[^"]+"', f'source_filename = "models/troopers/gc_anims/{did}@{name}.fbx"', blk, count=1 )
		blk = ns["drop_children"]( blk, r'anim_name = "_CONVERT_CITIZEN_ANIMS"' )
		# the stock motion prefab's align and extract, with footsteps on this clip's own landings
		pm = re.search( r'target_file = "(models/citizen_human/prefabs/human_ani_process_\w+\.vmdl_prefab)"', blk )
		if pm and "steps" in report[name]:
			ps, pe = ns["block_at"]( blk, pm.start() )
			while pe < len( blk ) and blk[pe] == ",":
				pe += 1
			blk = blk[:ps] + process_nodes( pm.group( 1 ), report[name]["steps"], ns ) + blk[pe:]
		text = text[:s] + blk + text[e:]
		changed.append( name )
	missing = set( report ) - set( changed )
	assert not missing, f"slots not in the list: {missing}"
	dst = os.path.join( ASSETS, "models", "troopers", f"gc_droid_{did}_animationlist.vmdl_prefab" )
	with open( dst, "w", encoding="utf-8", newline="\n" ) as fh:
		fh.write( text )
	if not os.path.exists( dst + ".meta" ):
		with open( dst + ".meta", "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	v = os.path.join( ASSETS, "models", "troopers", f"gc_droid_{did}.vmdl" )
	if os.path.exists( v ):
		t = open( v, encoding="utf-8" ).read()
		t = re.sub( r'target_file = "models/(clones/clone|troopers/gc_droid\w*)_animationlist\.vmdl_prefab"',
			f'target_file = "models/troopers/gc_droid_{did}_animationlist.vmdl_prefab"', t )
		with open( v, "w", encoding="utf-8", newline="\n" ) as fh:
			fh.write( t )
	print( "INSTALLED", did, len( changed ), "slots:", ", ".join( sorted( changed ) ) )


if "--install" in sys.argv:
	install()
	sys.exit( 0 )

# =================================================================================================== retarget (Blender)
import bpy
import numpy as np
from mathutils import Vector, Matrix, Quaternion, Euler
from pxr import Usd, UsdSkel

sys.path.insert( 0, HERE )
import gc_droids as gd
import bf2_clones as bc

HUMAN = r"X:\SteamLibrary\steamapps\common\sbox\addons\citizen\assets\models\citizen_human"
REF = os.path.join( HUMAN, "citizen_human_male_REF.fbx" )
AXES = dict( primary_bone_axis="X", secondary_bone_axis="Z" )
# GC_PREVIEW=<dir>: also render four frames of every clip on the reference mesh (preview_<slot>_<k>.png)
PREVIEW = os.environ.get( "GC_PREVIEW" )


def log( *a ):
	print( "GCANIM", *a, flush=True )


def reset():
	bpy.ops.wm.read_factory_settings( use_empty=True )
	sc = bpy.context.scene
	sc.render.fps, sc.render.fps_base = FPS, 1.0


def load_rig():
	"""The trooper rig itself (bf2_clones.RIG_BLEND), for clips that play as sequences outside the animgraph."""
	bpy.ops.wm.open_mainfile( filepath=bc.RIG_BLEND )
	rig = bpy.data.objects["Human Citizen Armature"]
	for o in list( bpy.data.objects ):
		if o is not rig and not ( PREVIEW and o.type == "MESH" and o.parent is rig ):
			bpy.data.objects.remove( o, do_unlink=True )
	for a in list( bpy.data.actions ):
		bpy.data.actions.remove( a )
	rig.animation_data_create()
	rig.animation_data.action = None
	for pb in rig.pose.bones:
		pb.matrix_basis = Matrix()
	sc = bpy.context.scene
	sc.render.fps, sc.render.fps_base = FPS, 1.0
	return rig


def rig_scale( arm ):
	"""Metres per armature unit (the reference is applied to metres; the rig object may carry its own scale)."""
	return arm.matrix_world.to_scale()[0]


def foot_speed( mats ):
	n = len( mats ) - 1
	v = []
	for f in range( n ):
		for s in "LR":
			a, b = mats[f][f"ankle_{s}"].translation, mats[f + 1][f"ankle_{s}"].translation
			o = mats[f][f"ankle_{'R' if s == 'L' else 'L'}"].translation
			if a.z <= o.z:
				v.append( ( Vector( ( b.x - a.x, b.y - a.y ) ) * FPS ).length )
	v.sort()
	return v[len( v ) // 2] if v else 0.0


def load_reference():
	before = set( bpy.data.objects )
	# keep the leaf bones: ignoring them dropped head, ball_*, the finger tips and the hand IK targets, so the head never
	# moved with the clip (it hung off the neck at rest: the head-down hunch, 2026-10-19)
	bpy.ops.import_scene.fbx( filepath=REF, ignore_leaf_bones=False, **AXES )
	objs = [o for o in bpy.data.objects if o not in before]
	arm = next( o for o in objs if o.type == "ARMATURE" )
	arm.name = "Human Citizen Armature"
	keep = [o for o in objs if o is arm or PREVIEW and o.type == "MESH"]
	for o in objs:
		if o not in keep:
			bpy.data.objects.remove( o, do_unlink=True )
	bpy.ops.object.select_all( action="DESELECT" )
	for o in keep:
		o.select_set( True )
	bpy.context.view_layer.objects.active = arm
	bpy.ops.object.transform_apply( location=True, rotation=True, scale=True )
	if arm.animation_data:
		arm.animation_data.action = None
	return arm


def rot( m ):
	return m.to_3x3().normalized().to_quaternion()


class Source:
	"""The droid's Squad skeleton in Blender, fitted onto the reference, with the USD clip's frames mapped onto it."""

	def __init__( self, ref, did ):
		glb = SETS[did]["glb"]
		self.arm, meshes = gd.import_glb( glb if os.path.isabs( glb ) else os.path.join( GC, gd.DROIDS[glb]["glb"] ) )
		for m in meshes:
			bpy.data.objects.remove( m, do_unlink=True )
		gd.fit_by_legs( self.arm, ref )
		bc.pose_onto( self.arm, ref )
		bpy.context.view_layer.update()
		mw = self.arm.matrix_world
		self.p0 = { b: rot( mw @ self.arm.pose.bones[b].matrix ) for b in gd.MAP if b in self.arm.pose.bones }
		self.p0_pelvis = ( mw @ self.arm.pose.bones["Bip01_Pelvis"].matrix ).translation.copy()
		for pb in self.arm.pose.bones:
			pb.matrix_basis = Matrix()
		bpy.context.view_layer.update()
		self.names = [b.name for b in self.arm.data.bones]
		self.rest_arm = { b.name: np.array( b.matrix_local ) for b in self.arm.data.bones }

	def frames( self, usda ):
		"""World matrices of the source bones for each 30 fps frame of the clip."""
		stage = Usd.Stage.Open( usda )
		skel = UsdSkel.Skeleton( next( p for p in stage.Traverse() if p.IsA( UsdSkel.Skeleton ) ) )
		anim = UsdSkel.Animation( next( p for p in stage.Traverse() if p.IsA( UsdSkel.Animation ) ) )
		joints = list( skel.GetJointsAttr().Get() )
		names = [j.split( "/" )[-1] for j in joints]
		parent = [joints.index( j.rsplit( "/", 1 )[0] ) if "/" in j else -1 for j in joints]
		bind = [np.array( m ).T for m in skel.GetBindTransformsAttr().Get()]
		rest_local = [np.array( m ).T for m in skel.GetRestTransformsAttr().Get()]
		aj = [j.split( "/" )[-1] for j in anim.GetJointsAttr().Get()]
		amap = [aj.index( n ) if n in aj else -1 for n in names]
		# armature space = G @ usd space, fitted on the bind pose heads (apply_usd_anim.py)
		pairs = [( bind[i][:3, 3], self.rest_arm[n][:3, 3] ) for i, n in enumerate( names ) if n in self.rest_arm]
		src = np.array( [np.append( p[0], 1.0 ) for p in pairs] )
		sol, *_ = np.linalg.lstsq( src, np.array( [p[1] for p in pairs] ), rcond=None )
		G = np.eye( 4 ); G[:3, :] = sol.T
		Ginv = np.linalg.inv( G )
		tcps = stage.GetTimeCodesPerSecond()
		times = anim.GetRotationsAttr().GetTimeSamples() or [0.0]
		t0, t1 = times[0], times[-1]
		n = max( 2, round( ( t1 - t0 ) / tcps * FPS ) + 1 )
		mw = np.array( self.arm.matrix_world )
		out = []
		for f in range( n ):
			t = t0 + ( t1 - t0 ) * f / ( n - 1 )
			tr, ro, sc = anim.GetTranslationsAttr().Get( t ), anim.GetRotationsAttr().Get( t ), anim.GetScalesAttr().Get( t )
			loc = []
			for i in range( len( names ) ):
				if amap[i] < 0:
					loc.append( rest_local[i] )
					continue
				q = ro[amap[i]]
				m = np.eye( 4 )
				m[:3, :3] = np.array( Quaternion( ( q.real, *q.imaginary ) ).to_matrix() ) * ( np.array( sc[amap[i]] )[None, :] if sc else 1.0 )
				m[:3, 3] = tr[amap[i]]
				loc.append( m )
			world = [None] * len( names )
			for i, m in enumerate( loc ):
				world[i] = m if parent[i] < 0 else world[parent[i]] @ m
			frame = {}
			for i, nm in enumerate( names ):
				if nm in gd.MAP and nm in self.rest_arm:
					arm_m = G @ world[i] @ np.linalg.inv( bind[i] ) @ Ginv @ self.rest_arm[nm]
					frame[nm] = Matrix( ( mw @ arm_m ).tolist() )
			out.append( frame )
		return out


# ValveBiped -> the Bip01 names gc_droids.MAP knows (ValveBiped has Spine, Spine1, Spine2, Spine4: Spine, Spine2 and Spine4
# take spine_0..2, Spine1 rides along unmapped)
VB_RENAME = { "Bip01_Spine1": "Bip01_Spine1x", "Bip01_Spine2": "Bip01_Spine1", "Bip01_Spine4": "Bip01_Spine2",
	"Bip01_Neck1": "Bip01_Neck", "Bip01_Head1": "Bip01_Head" }


def vb_name( n ):
	n = n.replace( "ValveBiped.", "" )
	return VB_RENAME.get( n, n )


def read_smd( path ):
	"""(names, parents, [frame: [local 4x4 per bone]]) from a Source SMD animation."""
	names, parents, frames, cur = [], [], [], None
	section = None
	for line in open( path, encoding="latin-1" ):
		s = line.split()
		if not s or s[0].startswith( "//" ):
			continue
		if s[0] in ( "nodes", "skeleton" ):
			section = s[0]
			continue
		if s[0] == "end":
			section = None
			continue
		if section == "nodes":
			i = line.index( '"' )
			j = line.index( '"', i + 1 )
			names.append( vb_name( line[i + 1:j] ) )
			parents.append( int( line[j + 1:].split()[0] ) )
		elif section == "skeleton":
			if s[0] == "time":
				cur = [None] * len( names )
				frames.append( cur )
				continue
			b = int( s[0] )
			x, y, z, rx, ry, rz = map( float, s[1:7] )
			m = Euler( ( rx, ry, rz ), "XYZ" ).to_matrix().to_4x4()
			m.translation = ( x, y, z )
			cur[b] = m
	for f in range( 1, len( frames ) ):      # a bone left out of a frame keeps its last pose
		for b in range( len( names ) ):
			if frames[f][b] is None:
				frames[f][b] = frames[f - 1][b]
	return names, parents, frames


def smd_world( names, parents, local ):
	world = [None] * len( names )
	for i in range( len( names ) ):
		world[i] = local[i] if parents[i] < 0 else world[parents[i]] @ local[i]
	return world


def qc_fps( clip ):
	"""The clip's own frame rate from the decompiled QC (30 unless it says otherwise)."""
	if not hasattr( qc_fps, "text" ):
		qc_fps.text = open( GMOD_QC, encoding="latin-1" ).read()
	m = re.search( r'\$(?:animation|sequence) "%s"[^{]*\{([^}]*)\}' % re.escape( clip ), qc_fps.text )
	f = m and re.search( r"\bfps (\d+(?:\.\d+)?)", m.group( 1 ) )
	return float( f.group( 1 ) ) if f else 30.0


TORSO = ( "spine_0", "spine_1", "spine_2", "neck_0", "head", "clavicle_L", "clavicle_R" )


def stock_idle( ref ):
	"""World rotations (in the reference's space) of the torso bones in s&box's own standing idle."""
	before = set( bpy.data.objects )
	bpy.ops.import_scene.fbx( filepath=os.path.join( HUMAN, "animations", "Human@IdlePose_Default.fbx" ), ignore_leaf_bones=False, **AXES )
	new = [o for o in bpy.data.objects if o not in before]
	arm = next( o for o in new if o.type == "ARMATURE" )
	bpy.context.scene.frame_set( 0 )
	bpy.context.view_layer.update()
	out = { n: rot( arm.matrix_world @ arm.pose.bones[n].matrix ) for n in TORSO if n in arm.pose.bones }
	for o in new:
		bpy.data.objects.remove( o, do_unlink=True )
	assert len( out ) == len( TORSO ), out.keys()
	return out


class SmdSource:
	"""GMod's ValveBiped skeleton built from a standing frame, fitted onto the reference like the droids' (gc_droids)."""

	def __init__( self, ref, pose_clip="idle_all_01" ):
		names, parents, frames = read_smd( os.path.join( GMOD, pose_clip + ".smd" ) )
		rest = smd_world( names, parents, frames[0] )
		data = bpy.data.armatures.new( "ValveBiped" )
		self.arm = bpy.data.objects.new( "ValveBiped", data )
		bpy.context.scene.collection.objects.link( self.arm )
		bpy.context.view_layer.objects.active = self.arm
		bpy.ops.object.mode_set( mode="EDIT" )
		ebs = {}
		for i, n in enumerate( names ):
			eb = data.edit_bones.new( n )
			eb.head, eb.tail = ( 0, 0, 0 ), ( 0, 2, 0 )
			eb.matrix = rest[i]
			if parents[i] >= 0:
				eb.parent = ebs[names[parents[i]]]
			ebs[n] = eb
		bpy.ops.object.mode_set( mode="OBJECT" )
		gd.fit_by_legs( self.arm, ref )
		bc.pose_onto( self.arm, ref )
		bpy.context.view_layer.update()
		mw = self.arm.matrix_world
		self.p0 = { b: rot( mw @ self.arm.pose.bones[b].matrix ) for b in gd.MAP if b in self.arm.pose.bones }
		self.p0_pelvis = ( mw @ self.arm.pose.bones["Bip01_Pelvis"].matrix ).translation.copy()
		for pb in self.arm.pose.bones:
			pb.matrix_basis = Matrix()
		bpy.context.view_layer.update()
		# the torso: GMod's standing idle onto s&box's standing idle, not onto the bind pose (the bind's spine is rounded
		# forward, which gave every GMod pose a hunched upper back next to GMod's own, 2026-10-19)
		# the thumbs stay the reference's own off the hand: ValveBiped's thumb roll doesn't line up with the Citizen's, and copied
		# over the left thumb stood straight up through the gun (Angelo, 2026-10-19); the game's grip curl closes it
		for b in [b for b in self.p0 if gd.MAP.get( b, "" ).startswith( "finger_thumb" )]:
			del self.p0[b]
		frame0 = self.frames( os.path.join( GMOD, pose_clip + ".smd" ) )[0]
		stock = stock_idle( ref )
		self.tgt = {}
		for src, dst in gd.MAP.items():
			if dst in TORSO and src in frame0 and dst in stock:
				self.p0[src] = rot( frame0[src] )
				self.tgt[dst] = stock[dst]

	def frames( self, path ):
		names, parents, frames = read_smd( path )
		fps = qc_fps( os.path.splitext( os.path.basename( path ) )[0] )
		n = max( 2, round( ( len( frames ) - 1 ) * FPS / fps ) + 1 )
		mw = self.arm.matrix_world
		out = []
		for f in range( n ):
			src = frames[min( len( frames ) - 1, round( f * fps / FPS ) )]
			world = smd_world( names, parents, src )
			fr = { nm: mw @ world[i] for i, nm in enumerate( names ) if nm in gd.MAP }
			if "Anim_Attachment_RH" in names:
				fr["Anim_Attachment_RH"] = mw @ world[names.index( "Anim_Attachment_RH" )]
			out.append( fr )
		return out


def retarget( ref, source, frames ):
	"""Armature-space matrices of the reference bones for every frame (rest for bones the droid has no match for)."""
	bones = ref.data.bones
	order = []
	def visit( b ):
		order.append( b )
		for c in b.children:
			visit( c )
	for r in [b for b in bones if b.parent is None]:
		visit( r )
	inv_map = { c: b for b, c in gd.MAP.items() }
	rest_rel = { b.name: ( b.parent.matrix_local.inverted() @ b.matrix_local if b.parent else b.matrix_local.copy() ) for b in bones }
	out = []
	for fr in frames:
		mats = {}
		for b in order:
			parent = mats.get( b.parent.name ) if b.parent else None
			base = ( parent @ rest_rel[b.name] ) if parent is not None else b.matrix_local.copy()
			src = inv_map.get( b.name )
			if src and src in fr and src in source.p0:
				r = rot( fr[src] ) @ source.p0[src].inverted() @ getattr( source, "tgt", {} ).get( b.name, rot( b.matrix_local ) )
				pos = base.translation
				if b.name == "pelvis":
					pos = b.matrix_local.translation + ( fr[src].translation - source.p0_pelvis )
				mats[b.name] = Matrix.Translation( pos ) @ r.to_matrix().to_4x4()
			else:
				mats[b.name] = base
		# the gun bone (hold_R) turned as GMod's own right hand attachment, where a GMod/TFA world model sits: its bone Z is the
		# barrel and Y up. In Blender's bone axes (FBX primary X -> bone Y, secondary Z -> bone X) the game's forward is the
		# bone's Y and its up the bone's X (the first try, X as the barrel, hung every gun muzzle down). Position stays in the palm
		if "Anim_Attachment_RH" in fr and "hold_R" in mats:
			a = fr["Anim_Attachment_RH"].to_3x3().normalized()
			barrel, up = a.col[2].normalized(), a.col[1].normalized()
			up = ( up - barrel * up.dot( barrel ) ).normalized()
			rot3 = Matrix( ( up, barrel, up.cross( barrel ) ) ).transposed()
			mats["hold_R"] = Matrix.Translation( mats["hold_R"].translation ) @ rot3.to_4x4()
		# the IK helpers: targets on the hands and feet, each two-hand ikrule bone on the other hand, all keeping their
		# rest offset from the limb they stand for, so the graph's IK leaves the clip's pose as it is
		for helper, limb in IK_FOLLOW:
			if helper in mats and limb in mats:
				mats[helper] = mats[limb] @ bones[limb].matrix_local.inverted() @ bones[helper].matrix_local
		out.append( mats )
	return out


IK_FOLLOW = [( "hand_R_IK_target", "hand_R" ), ( "hand_L_IK_target", "hand_L" ), ( "hand_R_to_L_ikrule", "hand_R" ), ( "hand_L_to_R_ikrule", "hand_L" ),
	( "foot_R_IK_target", "ankle_R" ), ( "foot_L_IK_target", "ankle_L" )]


def ground( ref, mats ):
	"""Shift the clip up or down so the planted (lower) foot sits at the reference's rest ankle height: Squad's pelvis
	height differs from its bind pose, which left the droids floating on their walk (Angelo, 2026-10-13)."""
	rest = min( ref.data.bones["ankle_L"].matrix_local.translation.z, ref.data.bones["ankle_R"].matrix_local.translation.z )
	low = sorted( min( m["ankle_L"].translation.z, m["ankle_R"].translation.z ) for m in mats )
	off = low[len( low ) // 4] - rest      # the lower quartile: the planted foot, not a foot mid-step
	for m in mats:
		for k in m:
			m[k] = Matrix.Translation( ( 0, 0, -off ) ) @ m[k]
	return off


def footfalls( mats ):
	"""[(frame, "L"|"R")]: the frames where a foot becomes the lower one (it lands), for the footstep events."""
	n = len( mats ) - 1
	low = ["L" if mats[f]["ankle_L"].translation.z <= mats[f]["ankle_R"].translation.z else "R" for f in range( n )]
	return [( f, low[f] ) for f in range( n ) if low[f] != low[f - 1]]


def phase_align( mats ):
	"""Start the loop on the left foot's landing, like s&box's own clips: GMod's directional clips each start at a different
	point of the stride (forward on the plant, sideways and back a third of the way on), so the graph's direction blend mixed
	legs out of step and they crossed through each other (Angelo, 2026-10-19). Cyclic, keeping the clip's own travel."""
	n = len( mats ) - 1        # the last frame repeats the first
	land = [f for f, side in footfalls( mats ) if side == "L"]
	s = land[0] if land else 0
	if s <= 0 or n < 2:
		return 0
	p0, ps, pn = ( mats[k]["pelvis"].translation for k in ( 0, s, n ) )
	before = Vector( ( ps.x - p0.x, ps.y - p0.y, 0 ) )      # travel up to the new start
	after = Vector( ( pn.x - ps.x, pn.y - ps.y, 0 ) )        # and from it to the end
	out = []
	for i in range( n ):
		k = i + s
		off = Matrix.Translation( -before ) if k < n else Matrix.Translation( after )
		out.append( { b: off @ m for b, m in mats[k % n].items() } )
	travel = Matrix.Translation( before + after )
	out.append( { b: travel @ m for b, m in out[0].items() } )
	mats[:] = out
	return s


def add_root_motion( mats, slot ):
	"""Measure how fast and which way the planted foot slides back and move the whole clip the other way at that speed."""
	n = len( mats ) - 1        # the last frame repeats the first
	vel = []
	for f in range( n ):
		for side in "LR":
			a, b = mats[f][f"ankle_{side}"].translation, mats[f + 1][f"ankle_{side}"].translation
			other = mats[f][f"ankle_{'R' if side == 'L' else 'L'}"].translation
			if a.z <= other.z:     # the lower foot is the planted one
				vel.append( Vector( ( b.x - a.x, b.y - a.y ) ) * FPS )
	v = -Vector( np.median( np.array( [list( x ) for x in vel] ), axis=0 ) )
	travel = mats[-1]["pelvis"].translation - mats[0]["pelvis"].translation
	if Vector( ( travel.x, travel.y ) ).length > 0.1:
		log( slot, "already travels", round( travel.length, 3 ) )
		return Vector( ( travel.x, travel.y ) ).length * FPS / n, Vector( ( travel.x, travel.y ) ).normalized()
	want = Vector( slot_dir( slot ) )
	speed = v.length
	d = v.normalized() if speed > 1e-4 else want
	if d.dot( want ) < 0.5:
		log( slot, f"WARNING foot slide points {tuple( round( x, 2 ) for x in d )}, expected {want[:]}: using the slot direction" )
		d = want
	for f, m in enumerate( mats ):
		off = Matrix.Translation( ( d.x * speed * f / FPS, d.y * speed * f / FPS, 0 ) )
		for k in m:
			m[k] = off @ m[k]      # every bone: the matrices are armature space, not parent relative
	return speed, d


def key_and_export( ref, did, slot, mats ):
	act = bpy.data.actions.new( f"{did}@{slot}" )
	ref.animation_data_create()
	ref.animation_data.action = act
	for pb in ref.pose.bones:
		pb.rotation_mode = "QUATERNION"
	rest_rel = { b.name: ( b.parent.matrix_local.inverted() @ b.matrix_local if b.parent else b.matrix_local.copy() ) for b in ref.data.bones }
	for f, m in enumerate( mats ):
		for pb in ref.pose.bones:
			# local basis straight from the target matrices (pb.matrix would read the parent's not yet updated pose)
			parent = m[pb.parent.name] if pb.parent else Matrix()
			pb.matrix_basis = rest_rel[pb.name].inverted() @ parent.inverted() @ m[pb.name]
		for pb in ref.pose.bones:
			pb.keyframe_insert( "location", frame=f )
			pb.keyframe_insert( "rotation_quaternion", frame=f )
			pb.keyframe_insert( "scale", frame=f )
	sc = bpy.context.scene
	sc.frame_start, sc.frame_end = 0, len( mats ) - 1
	bpy.ops.object.select_all( action="DESELECT" )
	ref.select_set( True )
	bpy.context.view_layer.objects.active = ref
	path = os.path.join( ANIM_DIR, f"{did}@{slot}.fbx" )
	bpy.ops.export_scene.fbx( filepath=path, use_selection=True, object_types={ "ARMATURE" }, add_leaf_bones=False, bake_anim=True,
		bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
		bake_anim_simplify_factor=0.0, bake_anim_step=1.0, **AXES )
	if not os.path.exists( path + ".meta" ):
		with open( path + ".meta", "w", newline="\n" ) as fh:
			fh.write( '{\n  "guid": "%s"\n}' % uuid.uuid4() )
	if PREVIEW:
		render_preview( ref, f"{did}_{slot}", len( mats ) - 1 )
	ref.animation_data.action = None
	for pb in ref.pose.bones:
		pb.matrix_basis = Matrix()
	bpy.data.actions.remove( act )


def render_preview( ref, slot, n ):
	import math
	sc = bpy.context.scene
	cam = bpy.data.objects.get( "pv_cam" )
	if not cam:
		cam = bpy.data.objects.new( "pv_cam", bpy.data.cameras.new( "pv_cam" ) ); sc.collection.objects.link( cam )
		cam.data.type = "ORTHO"; cam.data.ortho_scale = 2.4
		sun = bpy.data.objects.new( "pv_sun", bpy.data.lights.new( "pv_sun", "SUN" ) ); sun.rotation_euler = ( 0.9, 0.2, 2.2 ); sc.collection.objects.link( sun )
		w = bpy.data.worlds.new( "pv_w" ); w.color = ( 0.4, 0.4, 0.43 ); sc.world = w
		sc.camera = cam; sc.render.resolution_x = 360; sc.render.resolution_y = 480
	# the same cameras as the GMod reference renders (scratchpad gmod_ref.py, in inches there): side and three-quarter front
	sc.render.engine = "BLENDER_WORKBENCH"; sc.render.resolution_x, sc.render.resolution_y = 520, 620
	sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "SINGLE"; sc.display.shading.single_color = ( 0.75, 0.75, 0.78 )
	cam.data.ortho_scale = 95 * 0.0254
	for k in range( 4 ):
		f = int( n * k / 4 )
		sc.frame_set( f )
		p = ref.matrix_world @ ref.pose.bones["pelvis"].head
		for tag, ang in ( ( "side", 0 ), ( "front34", -50 ) ):
			r = math.radians( ang )
			cam.location = ( p.x + math.cos( r ) * 5.08, p.y + math.sin( r ) * 5.08, 36 * 0.0254 )
			cam.rotation_euler = ( math.radians( 90 ), 0, math.radians( 90 + ang ) )
			sc.render.filepath = os.path.join( PREVIEW, f"preview_{slot}_{k}_{tag}.png" )
			bpy.ops.render.render( write_still=True )


# GMod's aim layers: nine delta poses per hold (up/mid/down x right/centre/left) that bend the spine and arms towards the
# aim. Baked as each upper-body bone's rotation change in the model's space, over the hold's idle; SwrpGmodAim.cs blends
# them by aim pitch and yaw at runtime (Angelo, 2026-10-19: identical to GMod)
GMOD_AIM = { "ar2": ( "aimlayer_ar2", "idle_ar2" ), "pistol": ( "aimlayer_pistol", "idle_pistol" ), "smg1": ( "aimlayer_smg", "idle_smg1" ),
	"shotgun": ( "aimlayer_shotgun", "idle_shotgun" ), "rpg": ( "aimlayer_rpg", "idle_rpg" ), "revolver": ( "aimlayer_revolver", "idle_revolver" ) }
AIM_BONES = ( "spine_0", "spine_1", "spine_2", "clavicle_L", "arm_upper_L", "arm_lower_L", "hand_L",
	"clavicle_R", "arm_upper_R", "arm_lower_R", "hand_R" )
AIM_OUT = os.path.join( ASSETS, "data", "gmod_aim.json" )
# GMod's fire and reload gestures, layered over the body the same way: name -> (clip, "delta" or "pose", weight list,
# base idle, clip fps, blend ramp in frames (in start, in end, out start, out end) or None)
GMOD_GESTURES = {
	"range_ar2": ( "a_range_ar2", "delta", "weights_range_ar2", "idle_ar2", 30, None ),
	"range_pistol": ( "a_range_pistol", "delta", "weights_range_ar2", "idle_pistol", 45, None ),
	"reload_ar2": ( "reload_ar2_base_layer", "pose", "weights_reload_ar2_base_layer", "idle_ar2", 32, ( 0, 15, 52, 65 ) ),
	"reload_pistol": ( "reload_pistol_base_layer", "pose", "weights_reload_ar2_base_layer", "idle_pistol", 32, ( 0, 14, 35, 45 ) ),
	# TFA's other holds (fps and ramps from each reload sequence's blendlayer in m_anm.qc)
	"range_smg1": ( "a_range_smg1", "delta", "weights_range_ar2", "idle_smg1", 30, None ),
	"range_shotgun": ( "a_range_shotgun", "delta", "weights_range_ar2", "idle_shotgun", 30, None ),
	"range_rpg": ( "a_range_rpg", "delta", "weights_range_ar2", "idle_rpg", 30, None ),
	"range_revolver": ( "a_range_revolver", "delta", "weights_range_ar2", "idle_revolver", 30, None ),
	"reload_smg1": ( "reload_smg1_base_layer", "pose", "weights_reload_ar2_base_layer", "idle_smg1", 34, ( 0, 15, 52, 65 ) ),
	"reload_shotgun": ( "reload_shotgun_base_layer", "pose", "weights_reload_ar2_base_layer", "idle_shotgun", 30, ( 0, 15, 68, 80 ) ),
	"reload_revolver": ( "reload_revolver_base_layer", "pose", "weights_reload_ar2_base_layer", "idle_revolver", 38, ( 0, 15, 90, 110 ) ),
}
GESTURE_BONES = AIM_BONES + ( "neck_0", "head" )


def weightlist( qc, name ):
	m = re.search( r'\$weightlist "%s" \{([^}]*)\}' % re.escape( name ), qc )
	return { vb_name( a ): float( b ) for a, b in re.findall( r'"([^"]+)"\s+([0-9.]+)', m.group( 1 ) ) } if m else {}


def export_aim():
	reset()
	ref = load_reference()
	source = SmdSource( ref )
	qc = open( GMOD_QC, encoding="latin-1" ).read()
	g = g_rot = Quaternion( ( 0, 0, 1 ), math.radians( 90 ) )     # the reference faces -Y; s&box models face +X
	out = {}
	for hold, ( seq, idle ) in GMOD_AIM.items():
		m = re.search( r'\$sequence "%s" \{([^}]*)\}' % seq, qc )
		body = m.group( 1 )
		clips = re.findall( r'^\s*"([^"]+)"\s*$', body, re.M )
		yaw = [float( x ) for x in re.search( r'blend "aim_yaw" (\S+) (\S+)', body ).groups()]
		pitch = [float( x ) for x in re.search( r'blend "aim_pitch" (\S+) (\S+)', body ).groups()]
		names, parents, base_frames = read_smd( os.path.join( GMOD, idle + ".smd" ) )
		base_local = base_frames[0]
		def frame_of( local ):
			world = smd_world( names, parents, local )
			return { nm: source.arm.matrix_world @ world[i] for i, nm in enumerate( names ) if nm in gd.MAP }
		base = retarget( ref, source, [frame_of( base_local )] )[0]
		poses = []
		for clip in clips:
			dn, _, dframes = read_smd( os.path.join( GMOD, clip + ".smd" ) )
			delta = { n: dframes[0][i] for i, n in enumerate( dn ) }
			local = []
			for i, n in enumerate( names ):
				d = delta.get( n )
				if d is None or parents[i] < 0:      # the root's delta is subtracted out again (the corrective clip)
					local.append( base_local[i] )
					continue
				r = d.to_quaternion() @ base_local[i].to_quaternion()      # Source: q = delta * base, in the parent's frame
				mm = r.to_matrix().to_4x4()
				mm.translation = base_local[i].translation
				local.append( mm )
			posed = retarget( ref, source, [frame_of( local )] )[0]
			pose = {}
			for b in AIM_BONES:
				dq = rot( posed[b] ) @ rot( base[b] ).inverted()
				dq = g @ dq @ g.inverted()
				pose[b] = [round( dq.x, 5 ), round( dq.y, 5 ), round( dq.z, 5 ), round( dq.w, 5 )]
			poses.append( pose )
			log( "aim", hold, clip, "spine_2", pose["spine_2"] )
		out[hold] = { "yaw": yaw, "pitch": pitch, "bones": list( AIM_BONES ), "poses": poses }
	gestures = {}
	for gname, ( clip, kind, wl, idle, fps, ramp ) in GMOD_GESTURES.items():
		weights = weightlist( qc, wl )
		names, parents, base_frames = read_smd( os.path.join( GMOD, idle + ".smd" ) )
		base_local = base_frames[0]
		def frame_of( local ):
			world = smd_world( names, parents, local )
			return { nm: source.arm.matrix_world @ world[i] for i, nm in enumerate( names ) if nm in gd.MAP }
		base = retarget( ref, source, [frame_of( base_local )] )[0]
		gn, _, gframes = read_smd( os.path.join( GMOD, clip + ".smd" ) )
		gi = { n: i for i, n in enumerate( gn ) }
		frames = []
		for gf in gframes:
			local = []
			for i, n in enumerate( names ):
				w = weights.get( n, 0.0 )
				g = gf[gi[n]] if n in gi else None
				if g is None or w <= 0 or parents[i] < 0:
					local.append( base_local[i] )
					continue
				if kind == "delta":
					r = Quaternion().slerp( g.to_quaternion(), w ) @ base_local[i].to_quaternion()
				else:
					r = base_local[i].to_quaternion().slerp( g.to_quaternion(), w )
				mm = r.to_matrix().to_4x4()
				mm.translation = base_local[i].translation
				local.append( mm )
			posed = retarget( ref, source, [frame_of( local )] )[0]
			pose = {}
			for b in [x for x in GESTURE_BONES if x in posed]:
				dq = g_rot @ ( rot( posed[b] ) @ rot( base[b] ).inverted() ) @ g_rot.inverted()
				pose[b] = [round( dq.x, 5 ), round( dq.y, 5 ), round( dq.z, 5 ), round( dq.w, 5 )]
			frames.append( pose )
		gestures[gname] = { "fps": fps, "ramp": list( ramp ) if ramp else None, "bones": [x for x in GESTURE_BONES if x in frames[0]], "frames": frames }
		log( "gesture", gname, len( frames ), "frames" )
	out["_gestures"] = gestures
	with open( AIM_OUT, "w", newline="\n" ) as fh:
		json.dump( out, fh, indent=1 )
	log( "wrote", AIM_OUT )


# relaxed NPC clones' ambient gestures (Angelo, 2026-10-21: "wipe their visor, in a calm state"): Squad's own 3P emotes
# (Offworld's; LOCAL ONLY), exported with GcExport to Export_SquadEmotes. Each is baked like a GMod gesture: every upper-body
# bone's rotation change in model space over Squad's gesture base pose, per frame, added on top of whatever the body plays
SQUAD_EMOTES = r"X:\SWRP Projects\Squad_Mod_2428425228\Export_SquadEmotes"
AMBIENT = {      # name -> (clip, base pose clip)
	"amb_visorwipe": ( "A_Char_Gestures_HideFace_3P", "A_Char_Gestures_Basepose_3P" ),
	"amb_thumbsup": ( "A_Char_Gestures_ThumbsUp_3P", "A_Char_Gestures_Basepose_3P" ),
}


def find_usda( root, clip ):
	return next( ( os.path.join( r, clip + ".usda" ) for r, _, fs in os.walk( root ) if clip + ".usda" in fs ), None )


def export_ambient():
	"""Adds the AMBIENT gestures to gmod_aim.json's _gestures (the GMod ones stay as they are)."""
	reset()
	ref = load_reference()
	# the left arm goes where Squad's does, from the pose our relaxed clone stands in (GMod's idle_passive), so the hand
	# really reaches the helmet; the spine and head only take Squad's movement over its own base; the right arm, which
	# holds our rifle, is left alone (its Squad motion swung the rifle about)
	smd = SmdSource( ref )
	names, parents, base_frames = read_smd( os.path.join( GMOD, "idle_passive.smd" ) )
	world = smd_world( names, parents, base_frames[0] )
	passive = retarget( ref, smd, [{ nm: smd.arm.matrix_world @ world[i] for i, nm in enumerate( names ) if nm in gd.MAP }] )[0]
	source = Source( ref, "patrol" )
	g_rot = Quaternion( ( 0, 0, 1 ), math.radians( 90 ) )
	out = json.load( open( AIM_OUT ) )
	for name, ( clip, base_clip ) in AMBIENT.items():
		path, base_path = find_usda( SQUAD_EMOTES, clip ), find_usda( SQUAD_EMOTES, base_clip )
		if not path or not base_path:
			log( "ambient", name, "MISSING", clip )
			continue
		base = retarget( ref, source, source.frames( base_path )[:1] )[0]
		mats = retarget( ref, source, source.frames( path ) )
		frames = []
		for posed in mats:
			pose = {}
			for b in [x for x in GESTURE_BONES if x in posed and not x.endswith( "_R" )]:
				over = passive if b.endswith( "_L" ) else base
				dq = g_rot @ ( rot( posed[b] ) @ rot( over[b] ).inverted() ) @ g_rot.inverted()
				pose[b] = [round( dq.x, 5 ), round( dq.y, 5 ), round( dq.z, 5 ), round( dq.w, 5 )]
			frames.append( pose )
		# Squad's clips start and end on their rifle-ready base, ours on the lowered rifle: the left arm's change at the
		# clip's own start (reaching for the rifle) is blended out at both ends by the ramp
		n = len( frames )
		# in and out over 0.3 s, as GMod blends its gestures
		out["_gestures"][name] = { "fps": FPS, "ramp": [0, 9, max( 10, n - 10 ), n - 1], "bones": list( frames[0] ), "frames": frames }
		peak = max( max( 2 * math.degrees( math.acos( min( 1.0, abs( f[b][3] ) ) ) ) for b in f ) for f in frames )
		log( "ambient", name, n, "frames", round( n / FPS, 2 ), "s, biggest turn", round( peak ), "deg" )
	with open( AIM_OUT, "w", newline="\n" ) as fh:
		json.dump( out, fh, indent=1 )
	log( "wrote", AIM_OUT )


def main():
	os.makedirs( ANIM_DIR, exist_ok=True )
	args = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	if "aim" in args:
		export_aim()
		return
	if "ambient" in args:
		export_ambient()
		return
	droids = [a for a in args if a in SETS] or list( SETS )
	only = [a for a in args if a not in SETS]
	for did in droids:
		reset()
		spec = SETS[did]
		ref = load_rig() if spec.get( "target" ) == "rig" else load_reference()
		source = SmdSource( ref ) if spec.get( "smd" ) else Source( ref, did )
		report_path = os.path.join( ANIM_DIR, f"report_{did}.json" )
		report = json.load( open( report_path ) ) if os.path.exists( report_path ) else {}
		for slot, clip in spec["slots"].items():
			if only and slot not in only:
				continue
			ext = ".smd" if spec.get( "smd" ) else ".usda"
			usda = next( ( os.path.join( r, clip + ext ) for r, _, fs in os.walk( spec["dir"] ) if clip + ext in fs ), "" )
			if not os.path.exists( usda ):
				log( did, slot, "MISSING", clip )
				continue
			frames = source.frames( usda )
			mats = retarget( ref, source, frames )
			entry = { "clip": clip, "frames": len( mats ), "seconds": round( ( len( mats ) - 1 ) / FPS, 3 ),
				"grounded_cm": round( ground( ref, mats ) * 100, 1 ) }
			if spec.get( "target" ) == "rig":
				# sequences played outside the graph: in place; how fast the planted foot slides tells the code the crawl speed
				entry["speed_in_per_s"] = round( foot_speed( mats ) * 39.37 / rig_scale( ref ), 1 )
				entry["loop"] = "_to_" not in slot
			elif is_locomotion( slot ):
				if spec.get( "smd" ):
					entry["phase_shift"] = phase_align( mats )
				entry["steps"] = footfalls( mats )
				speed, d = add_root_motion( mats, slot )
				entry.update( { "speed_in_per_s": round( speed * 39.37, 1 ), "dir": [round( d.x, 3 ), round( d.y, 3 )] } )
			key_and_export( ref, did, slot, mats )
			report[slot] = entry
			log( did, slot, clip, { k: v for k, v in entry.items() if k != "steps" } )
		with open( report_path, "w", newline="\n" ) as fh:
			json.dump( report, fh, indent=1 )


main()
