"""
Converts the Garry's Mod Notorious map's textures (VTF/VMT) into s&box PNGs and .vmat materials.

    blender -b --factory-startup -P MapSource/gmod_textures.py        (Blender's Python ships numpy)

Reads the material names the VMF uses, finds each .vmt in the extracted workshop addon, decodes its $basetexture (and $bumpmap)
from VTF, writes PNGs to Assets/materials/gmod/ and one .vmat per Source material to Assets/materials/notorious/, and records
texture sizes and what matched in MapSource/gmod_materials.json for notorious_build.py (which then uses the real texture
projection instead of a tinted stand-in).

LICENCE: textures are by the Notorious map's authors (Goldie, 122jimbo and others). Keep local; credit them if ever published.
"""
import json
import os
import re
import struct
import sys
import zlib

import numpy as np

HERE = os.path.dirname( os.path.abspath( __file__ ) )
sys.path.insert( 0, HERE )
import vmf_parse as vp

PROJECT = os.path.dirname( HERE )
ADDON = r"X:\SteamLibrary\steamapps\workshop\content\4000\3466442938\gmpublisher\materials"
VMF = r"X:\SWRP Projects\Notorious_VMF\rp_venator_notorious_os.vmf"
TEX_DIR = os.path.join( PROJECT, "Assets", "materials", "gmod" )
MAT_DIR = os.path.join( PROJECT, "Assets", "materials", "notorious" )
os.makedirs( TEX_DIR, exist_ok=True )
os.makedirs( MAT_DIR, exist_ok=True )


# ---------------------------------------------------------------------------------------------------------------------
# VTF decoding
# ---------------------------------------------------------------------------------------------------------------------

BPP = { 0: 4, 1: 4, 2: 3, 3: 3, 5: 1, 6: 2, 8: 1, 11: 4, 12: 4, 16: 4, 22: 2 }
DXT = { 13: ( 8, "dxt1" ), 14: ( 16, "dxt3" ), 15: ( 16, "dxt5" ), 20: ( 8, "dxt1" ) }


def mip_size( fmt, w, h ):
	if fmt in DXT:
		return max( 1, ( w + 3 ) // 4 ) * max( 1, ( h + 3 ) // 4 ) * DXT[fmt][0]
	if fmt in BPP:
		return w * h * BPP[fmt]
	raise ValueError( f"unsupported VTF format {fmt}" )


def _rgb565( c ):
	r = ( ( c >> 11 ) & 31 ) * 255 // 31
	g = ( ( c >> 5 ) & 63 ) * 255 // 63
	b = ( c & 31 ) * 255 // 31
	return np.stack( [r, g, b], axis=-1 ).astype( np.int32 )


def decode_dxt( data, w, h, kind ):
	bs = 8 if kind == "dxt1" else 16
	bx, by = max( 1, ( w + 3 ) // 4 ), max( 1, ( h + 3 ) // 4 )
	n = bx * by
	arr = np.frombuffer( data, dtype=np.uint8, count=n * bs ).reshape( n, bs ).astype( np.int32 )
	cb = arr[:, bs - 8:]
	c0 = cb[:, 0] | ( cb[:, 1] << 8 )
	c1 = cb[:, 2] | ( cb[:, 3] << 8 )
	p0, p1 = _rgb565( c0 ), _rgb565( c1 )
	four = ( c0 > c1 )[:, None]
	p2 = np.where( four, ( 2 * p0 + p1 ) // 3, ( p0 + p1 ) // 2 )
	p3 = np.where( four, ( p0 + 2 * p1 ) // 3, 0 )
	pal = np.stack( [p0, p1, p2, p3], axis=1 )
	bits = cb[:, 4] | ( cb[:, 5] << 8 ) | ( cb[:, 6] << 16 ) | ( cb[:, 7] << 24 )
	shifts = np.arange( 16 ) * 2
	idx = ( bits[:, None] >> shifts[None, :] ) & 3
	px = pal[np.arange( n )[:, None], idx]                      # (n, 16, 3)
	alpha = np.full( ( n, 16 ), 255, dtype=np.int32 )
	if kind == "dxt1":
		alpha = np.where( ( ~four ) & ( idx == 3 ), 0, 255 )
	elif kind == "dxt3":
		a = arr[:, :8]
		nib = np.stack( [( a[:, i // 2] >> ( 4 * ( i % 2 ) ) ) & 15 for i in range( 16 )], axis=1 )
		alpha = nib * 17
	else:
		a0, a1 = arr[:, 0], arr[:, 1]
		tab = np.zeros( ( n, 8 ), dtype=np.int32 )
		tab[:, 0], tab[:, 1] = a0, a1
		gt = ( a0 > a1 )
		for i in range( 2, 8 ):
			tab[:, i] = np.where( gt, ( ( 8 - i ) * a0 + ( i - 1 ) * a1 ) // 7, np.where( i < 6, ( ( 6 - i ) * a0 + ( i - 1 ) * a1 ) // 5, 0 if i == 6 else 255 ) )
		ab = np.zeros( n, dtype=np.int64 )
		for i in range( 6 ):
			ab |= arr[:, 2 + i].astype( np.int64 ) << ( 8 * i )
		ai = ( ab[:, None] >> ( np.arange( 16 ) * 3 )[None, :] ) & 7
		alpha = tab[np.arange( n )[:, None], ai]
	rgba = np.concatenate( [px, alpha[..., None]], axis=-1 ).astype( np.uint8 )   # (n,16,4)
	rgba = rgba.reshape( by, bx, 4, 4, 4 ).transpose( 0, 2, 1, 3, 4 ).reshape( by * 4, bx * 4, 4 )
	return rgba[:h, :w]


def decode_raw( data, w, h, fmt ):
	bpp = BPP[fmt]
	a = np.frombuffer( data, dtype=np.uint8, count=w * h * bpp ).reshape( h, w, bpp )
	out = np.full( ( h, w, 4 ), 255, dtype=np.uint8 )
	if fmt == 0:   out[:] = a
	elif fmt == 1: out[..., 0], out[..., 1], out[..., 2], out[..., 3] = a[..., 3], a[..., 2], a[..., 1], a[..., 0]
	elif fmt == 2: out[..., :3] = a
	elif fmt == 3: out[..., :3] = a[..., ::-1]
	elif fmt in ( 12, ): out[..., 0], out[..., 1], out[..., 2], out[..., 3] = a[..., 2], a[..., 1], a[..., 0], a[..., 3]
	elif fmt == 16: out[..., 0], out[..., 1], out[..., 2] = a[..., 2], a[..., 1], a[..., 0]
	elif fmt == 11: out[..., 0], out[..., 1], out[..., 2], out[..., 3] = a[..., 1], a[..., 2], a[..., 3], a[..., 0]
	elif fmt == 5: out[..., 0] = out[..., 1] = out[..., 2] = a[..., 0]
	elif fmt == 6: out[..., 0] = out[..., 1] = out[..., 2] = a[..., 0]; out[..., 3] = a[..., 1]
	elif fmt == 8: out[..., 0] = out[..., 1] = out[..., 2] = 255; out[..., 3] = a[..., 0]
	elif fmt == 22: out[..., 0], out[..., 1], out[..., 2] = a[..., 0], a[..., 1], 255
	return out


def read_vtf( path ):
	"""Returns (rgba uint8 array (h, w, 4), format id). Frame 0, largest mip."""
	with open( path, "rb" ) as fh:
		d = fh.read()
	if d[:4] != b"VTF\0":
		raise ValueError( "not a VTF" )
	major, minor = struct.unpack_from( "<II", d, 4 )
	header_size = struct.unpack_from( "<I", d, 12 )[0]
	w, h = struct.unpack_from( "<HH", d, 16 )
	flags = struct.unpack_from( "<I", d, 20 )[0]
	frames = struct.unpack_from( "<H", d, 24 )[0]
	fmt = struct.unpack_from( "<i", d, 52 )[0]
	mips = d[56]
	low_fmt = struct.unpack_from( "<i", d, 57 )[0]
	lw, lh = d[61], d[62]
	faces = 6 if flags & 0x4000 else 1

	data_off = None
	if ( major, minor ) >= ( 7, 3 ):
		nres = struct.unpack_from( "<I", d, 68 )[0]
		for i in range( nres ):
			tag = d[80 + i * 8:80 + i * 8 + 3]
			off = struct.unpack_from( "<I", d, 80 + i * 8 + 4 )[0]
			if tag == b"\x30\x00\x00":
				data_off = off
	if data_off is None:
		data_off = header_size + ( mip_size( low_fmt, lw, lh ) if lw and lh and low_fmt >= 0 else 0 )

	# mips are stored smallest first; skip every smaller mip's frames to reach mip 0
	skip = 0
	for m in range( mips - 1, 0, -1 ):
		mw, mh = max( 1, w >> m ), max( 1, h >> m )
		skip += mip_size( fmt, mw, mh ) * frames * faces
	start = data_off + skip
	size = mip_size( fmt, w, h )
	chunk = d[start:start + size]
	if fmt in DXT:
		return decode_dxt( chunk, w, h, DXT[fmt][1] ), fmt
	return decode_raw( chunk, w, h, fmt ), fmt


# ---------------------------------------------------------------------------------------------------------------------
# PNG writer (stdlib zlib, no Pillow)
# ---------------------------------------------------------------------------------------------------------------------

def write_png( path, rgba, alpha=True ):
	h, w = rgba.shape[:2]
	if not alpha:
		raw = np.concatenate( [np.zeros( ( h, 1 ), dtype=np.uint8 ), rgba[..., :3].reshape( h, w * 3 )], axis=1 )
		ctype = 2
	else:
		raw = np.concatenate( [np.zeros( ( h, 1 ), dtype=np.uint8 ), rgba.reshape( h, w * 4 )], axis=1 )
		ctype = 6

	def chunk( t, b ):
		c = struct.pack( ">I", len( b ) ) + t + b
		return c + struct.pack( ">I", zlib.crc32( t + b ) & 0xffffffff )

	png = b"\x89PNG\r\n\x1a\n" + chunk( b"IHDR", struct.pack( ">IIBBBBB", w, h, 8, ctype, 0, 0, 0 ) ) + chunk( b"IDAT", zlib.compress( raw.tobytes(), 6 ) ) + chunk( b"IEND", b"" )
	os.makedirs( os.path.dirname( path ), exist_ok=True )
	with open( path, "wb" ) as fh:
		fh.write( png )


# ---------------------------------------------------------------------------------------------------------------------
# VMT
# ---------------------------------------------------------------------------------------------------------------------

def read_vmt( path ):
	text = open( path, "r", encoding="utf-8", errors="replace" ).read()
	text = re.sub( r"//[^\n]*", "", text )
	m = re.search( r'"?([A-Za-z0-9_]+)"?\s*\{', text )
	shader = m.group( 1 ).lower() if m else ""
	props = {}
	for k, v in re.findall( r'"?(\$[A-Za-z0-9_]+)"?\s+"?([^"\n\r]*?)"?\s*(?:\n|\r|$)', text ):
		props[k.lower()] = v.strip()
	return shader, props


def find_vtf( name ):
	name = name.replace( "\\", "/" ).strip().lower()
	if name.endswith( ".vtf" ):
		name = name[:-4]
	p = os.path.join( ADDON, name + ".vtf" )
	return p if os.path.exists( p ) else None


# glowing materials that are light fittings (not screens, holograms, signs or force fields)
LAMP_WORDS = ( "light", "lamp", "ceiling" )


def whiten_lamp( rgba ):
	"""
	Glowing panels with yellow light in them go to the ship's one lamp white (1, 0.97, 0.92), keeping their brightness, so the
	whole ship has one colour temperature (Angelo, 2026-10-12: the front half's ceiling panels were yellow). Blue, red and
	green glows (screens, holograms, accents) are left alone: only warm bright texels change.
	"""
	c = rgba[..., :3].astype( np.float32 ) / 255.0
	r, g, b = c[..., 0], c[..., 1], c[..., 2]
	warm = ( r >= g ) & ( g >= b ) & ( g > 0.6 * r ) & ( 0.3 * r + 0.59 * g + 0.11 * b > 0.55 )      # bright warm light only
	lum = 0.3 * r + 0.59 * g + 0.11 * b
	white = np.stack( ( lum / 0.9706, lum * 0.97 / 0.9706, lum * 0.92 / 0.9706 ), axis=-1 )      # (1, .97, .92) at that luminance
	c = np.where( warm[..., None], np.clip( white, 0, 1 ), c )
	out = rgba.copy()
	out[..., :3] = ( c * 255 ).astype( np.uint8 )
	return out


def slug( name ):
	return re.sub( r"[^a-z0-9]+", "_", name.lower() ).strip( "_" )


def main():
	root = vp.parse( VMF )
	world = vp.find( root, "world" )[0]
	solids = list( vp.find( world[2], "solid" ) )
	for e in vp.find( root, "entity" ):
		if e[1].get( "classname" ) in ( "func_detail", "func_brush", "func_button", "func_movelinear", "func_rotating", "func_illusionary",
										  "func_tracktrain", "func_door", "func_door_rotating" ):
			solids += vp.find( e[2], "solid" )
	used = {}
	for s in solids:
		for sd in vp.find( s[2], "side" ):
			m = sd[1].get( "material", "" ).lower()
			if m and not m.startswith( "tools/" ) and not m.startswith( "skybox/" ):
				used[m] = used.get( m, 0 ) + 1

	# the props' materials (consoles, chairs, light panels...) as well
	import notorious_props
	for name, n in notorious_props.used_materials( root ).items():
		used[name] = used.get( name, 0 ) + n

	manifest = {}
	counts = { "matched": 0, "no_vmt": 0, "no_vtf": 0, "decode_failed": 0 }
	for name in sorted( used ):
		vmt_path = os.path.join( ADDON, name + ".vmt" )
		entry = { "faces": used[name], "matched": False, "reason": "" }
		manifest[name.upper()] = entry
		if not os.path.exists( vmt_path ):
			# a bare VTF with no VMT still works
			vtf = find_vtf( name )
			if not vtf:
				entry["reason"] = "no vmt or vtf in the addon"
				counts["no_vmt"] += 1
				continue
			shader, props = "lightmappedgeneric", { "$basetexture": name }
		else:
			shader, props = read_vmt( vmt_path )

		base = props.get( "$basetexture" )
		vtf = find_vtf( base ) if base else None
		if not vtf:
			entry["reason"] = f"basetexture '{base}' not found ({shader})"
			counts["no_vtf"] += 1
			continue
		try:
			rgba, fmt = read_vtf( vtf )
		except Exception as ex:
			entry["reason"] = f"decode failed: {ex}"
			counts["decode_failed"] += 1
			continue

		h, w = rgba.shape[:2]
		key = slug( name )
		translucent = props.get( "$translucent", "0" ) not in ( "0", "" ) or props.get( "$alphatest", "0" ) not in ( "0", "" )
		selfillum = props.get( "$selfillum", "0" ) not in ( "0", "" )
		has_alpha = bool( translucent and rgba[..., 3].min() < 250 )
		rel = f"materials/gmod/{key}"
		if selfillum and any( k in key for k in LAMP_WORDS ):
			rgba = whiten_lamp( rgba )
		write_png( os.path.join( TEX_DIR, key + "_color.png" ), rgba, alpha=False )
		if has_alpha:
			a = rgba[..., 3:4]
			write_png( os.path.join( TEX_DIR, key + "_alpha.png" ), np.repeat( a, 3, axis=2 ).astype( np.uint8 ), alpha=False )

		normal = None
		bump = props.get( "$bumpmap" ) or props.get( "$normalmap" )
		bvtf = find_vtf( bump ) if bump else None
		if bvtf:
			try:
				n_rgba, _ = read_vtf( bvtf )
				n = n_rgba[..., :3].copy()
				n[..., 1] = 255 - n[..., 1]                    # Source normal maps are Y-down; s&box wants Y-up
				write_png( os.path.join( TEX_DIR, key + "_normal.png" ), n, alpha=False )
				normal = f"{rel}_normal.png"
			except Exception as ex:
				entry["note"] = f"bumpmap skipped: {ex}"

		surf = props.get( "$surfaceprop", "" ).lower()
		metal = 0.55 if "metal" in surf else 0.0
		rough = 0.45 if "metal" in surf else 0.75
		if "glass" in surf or "glass" in name:
			rough = 0.1

		lines = ["// Generated by MapSource/gmod_textures.py from the Notorious Garry's Mod addon", "", "Layer0", "{", '\tshader "shaders/complex.shader"', ""]
		if selfillum or shader == "unlitgeneric":
			lines.append( "\tF_SELF_ILLUM 1" )
		if has_alpha:
			lines.append( "\tF_TRANSLUCENT 1" )
		lines += ["", '\tg_flModelTintAmount "1.000"', '\tg_vColorTint "[1.000000 1.000000 1.000000 0.000000]"', f'\tTextureColor "{rel}_color.png"']
		lines.append( f'\tTextureNormal "{normal}"' if normal else '\tTextureNormal "materials/default/default_normal.tga"' )
		lines += [
			'\tTextureRoughness "materials/default/default_rough.tga"', '\tTextureAmbientOcclusion "materials/default/default_ao.tga"',
			f'\tg_flMetalness "{metal:.3f}"', f'\tg_flRoughnessScaleFactor "{rough:.3f}"', '\tg_bFogEnabled "1"',
		]
		if selfillum or shader == "unlitgeneric":
			lines += ['\tg_flSelfIllumAlbedoFactor "1.000"', '\tg_flSelfIllumBrightness "2.500"', '\tg_flSelfIllumScale "1.000"',
					'\tg_vSelfIllumTint "[1.000000 1.000000 1.000000 0.000000]"', '\tTextureSelfIllumMask "[1.000000 1.000000 1.000000 0.000000]"']
		if has_alpha:
			lines += ['\tg_flOpacityScale "1.000"', f'\tTextureTranslucency "{rel}_alpha.png"']
		lines.append( "}" )
		with open( os.path.join( MAT_DIR, key + ".vmat" ), "w", newline="\n" ) as fh:
			fh.write( "\n".join( lines ) + "\n" )

		entry.update( { "matched": True, "width": int( w ), "height": int( h ), "format": int( fmt ), "shader": shader, "normal": bool( normal ), "alpha": has_alpha,
						"emissive": bool( selfillum ), "surfaceprop": surf } )
		counts["matched"] += 1

	json.dump( manifest, open( os.path.join( HERE, "gmod_materials.json" ), "w" ), indent=1 )
	print( f"GMOD: {len( used )} materials used by the map. {counts}" )
	for k, v in manifest.items():
		if not v["matched"]:
			print( "GMOD fallback:", k, "-", v["reason"] )


main()
