"""
Explosion and smoke flipbook frames from Battlefront II's FX textures (X:\\SWRP Projects\\BF2_Extracted\\Models\\FX_Smoke and
FX_Fire, exported 2026-10-12 with Tools/BatchExport/fx.txt). LOCAL ONLY: never publish or commit (CLAUDE.md, IP rules).

    python ModelSource/bf2_fx.py      (needs numpy and Pillow)

Frostbite packs its particle sheets for its own lighting (RGB = light from several directions, A = density), so each frame
is cut out and flattened to a grey shade with the density as alpha: Code/Player/SwrpCombat.cs (FlipbookPuff) tints and
lights them. Writes Assets/materials/fx/bf2/billow_<sheet>_<frame>.png (two 8x8 sheets of 64 billowing smoke frames).
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname( os.path.abspath( __file__ ) )
OUT = os.path.join( os.path.dirname( HERE ), "Assets", "materials", "fx", "bf2" )
SRC = r"X:\SWRP Projects\BF2_Extracted\Models\FX_Smoke\T_billowSmokeHi_Gnomon_8x64_NoAtlas_01_D.png"


def main():
	os.makedirs( OUT, exist_ok=True )
	a = np.asarray( Image.open( SRC ).convert( "RGBA" ) ).astype( np.float32 ) / 255.0
	h, w = a.shape[:2]
	cell = h // 8                         # 16 x 8 cells of 128: the left and right halves are two 64-frame sheets
	for sheet in range( 2 ):
		for i in range( 64 ):
			r, c = divmod( i, 8 )
			x0 = ( sheet * 8 + c ) * cell
			f = a[r * cell:( r + 1 ) * cell, x0:x0 + cell]
			shade = f[..., :3].mean( axis=2 )
			shade = 0.35 + 0.65 * shade        # lift the dark side a little: the sprite is lit again in game
			alpha = np.clip( f[..., 3] * 1.25, 0, 1 )
			img = np.dstack( ( shade, shade, shade, alpha ) )
			Image.fromarray( ( img * 255 ).astype( np.uint8 ), "RGBA" ).save( os.path.join( OUT, f"billow_{sheet}_{i:02d}.png" ) )
	print( "wrote 128 frames to", OUT )


if __name__ == "__main__":
	main()
