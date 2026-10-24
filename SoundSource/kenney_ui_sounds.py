"""
Character screen UI sounds from Kenney's "Interface Sounds" pack (CC0 1.0, https://kenney.nl/assets/interface-sounds).

    blender -b --factory-startup -P SoundSource/kenney_ui_sounds.py -- <folder the pack zip was extracted to>

Converts the picks below to 16-bit mono 44.1 kHz WAVs in Assets/sounds/ui/ (Blender's aud decodes the .ogg files).
Volume lives in the matching .sound events, not here.
"""
import os
import sys
import wave
import aud
import numpy as np

PICKS = {
	"charmenu_hover": "select_001",       # short console blip
	"charmenu_click": "toggle_002",       # switch throw
	"charmenu_confirm": "confirmation_001",  # ENLIST / DEPLOY
	"charmenu_deny": "error_008",
}

src = os.path.join( sys.argv[sys.argv.index( "--" ) + 1], "Audio" )
out = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "ui" )
for name, pick in PICKS.items():
	sound = aud.Sound( os.path.join( src, pick + ".ogg" ) ).rechannel( 1 ).resample( 44100, True )
	pcm = ( np.clip( sound.data()[:, 0], -1, 1 ) * 32767 ).astype( np.int16 )
	with wave.open( os.path.join( out, name + ".wav" ), "wb" ) as w:
		w.setnchannels( 1 ); w.setsampwidth( 2 ); w.setframerate( 44100 ); w.writeframes( pcm.tobytes() )
	print( "wrote", name, "<-", pick )
