"""
Post-pass over Assets/materials/notorious/*.vmat to push the converted GMod materials toward the original look: glossy dark floors,
bright emissive light strips / lamps / holographic screens. Idempotent; run after gmod_textures.py.   python MapSource/gmod_style.py
"""
import os, re, glob

HERE = os.path.dirname( os.path.abspath( __file__ ) )
DIR = os.path.join( os.path.dirname( HERE ), "Assets", "materials", "notorious" )
EMIT = ( "blue_floor", "outline", "light", "lamp", "holo", "screen", "console", "display", "glow", "neon", "strip", "monitor", "button", "panel_l", "led" )
FLOOR = ( "floor", "deck", "tile" )


def setp( text, key, value ):
	pat = re.compile( r'(\t' + key + r' )"[^"]*"' )
	if pat.search( text ):
		return pat.sub( lambda m: m.group( 1 ) + f'"{value}"', text )
	return text.replace( "\tg_bFogEnabled", f'\t{key} "{value}"\n\tg_bFogEnabled', 1 )


n_emit = n_floor = 0
for p in glob.glob( os.path.join( DIR, "*.vmat" ) ):
	name = os.path.basename( p )[:-5].lower()
	if name.startswith( "space_sky" ) or name.startswith( "hull_" ) or name.startswith( "deck_" ) or name == "metal":
		continue
	t = open( p, encoding="utf-8" ).read()
	if "gmod_textures.py" not in t:
		continue
	o = t
	if any( k in name for k in EMIT ) and "Translucent" not in t:
		color = re.search( r'TextureColor "([^"]+)"', t )
		if color:
			# A flat mask: the albedo already darkens what shouldn't glow. Pointing the mask at the colour PNG made the editor
			# build two textures from one file and recompile some of these materials forever.
			t = re.sub( r'\n\tTextureSelfIllumMask "[^"]*"', "", t )
			t = setp( t, "TextureSelfIllumMask", "[1.000000 1.000000 1.000000 0.000000]" )
			t = setp( t, "g_flSelfIllumBrightness", "4.0" )
			t = setp( t, "g_flSelfIllumAlbedoFactor", "1.000" )
			t = setp( t, "g_flSelfIllumScale", "1.000" )
			n_emit += 1
	elif any( k in name for k in FLOOR ):
		t = setp( t, "g_flRoughnessScaleFactor", "0.28" )
		t = setp( t, "g_flMetalness", "0.25" )
		n_floor += 1
	if t != o:
		open( p, "w", encoding="utf-8", newline="\n" ).write( t )
print( f"styled: {n_emit} emissive, {n_floor} glossy floors" )
