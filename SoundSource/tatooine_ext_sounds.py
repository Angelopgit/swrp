"""
Tatooine's wind from recorded sources (Angelo asked for real desert sounds over the synthesized ones, 2026-10-20). LOCAL ONLY:
taken from Angelo's own Garry's Mod workshop download "gWeather" (workshop 3322707383, sound/atmosphere and
sound/weather/haboob); like the BF2/TFA assets these must never be published or uploaded.

    py SoundSource/tatooine_ext_sounds.py

Writes Assets/sounds/tatooine/ext/: the calm desert wind bed and whistle loops as they are, and four gust one-shots cut
from the heavy gust and haboob (dust storm) recordings at their loudest swells, faded in and out. Code/Map/Tatooine/
TatooineWeather.cs prefers these when present and falls back to SoundSource/tatooine_sounds.py's synthesized set.
"""
import json
import os
import struct
import wave

import numpy as np

GMA = r"X:\SteamLibrary\steamapps\workshop\content\4000\3322707383\gmpublisher.gma"
OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "tatooine", "ext" )


def read_gma( path, wanted ):
	"""{name: bytes} for the files in a .gma whose path is in `wanted`."""
	f = open( path, "rb" )
	assert f.read( 4 ) == b"GMAD"
	ver = f.read( 1 )[0]
	f.read( 16 )

	def cstr():
		b = bytearray()
		while ( c := f.read( 1 ) ) != b"\0":
			b += c
		return b.decode( "utf-8", "replace" )

	if ver > 1:
		while cstr():
			pass
	cstr(); cstr(); cstr(); f.read( 4 )
	files = []
	while struct.unpack( "<I", f.read( 4 ) )[0]:
		name = cstr()
		size = struct.unpack( "<q", f.read( 8 ) )[0]
		f.read( 4 )
		files.append( ( name, size ) )
	out, off, base = {}, 0, f.tell()
	for name, size in files:
		if name in wanted:
			f.seek( base + off )
			out[name] = f.read( size )
		off += size
	return out


def load( data ):
	import io
	w = wave.open( io.BytesIO( data ) )
	sr, ch = w.getframerate(), w.getnchannels()
	x = np.frombuffer( w.readframes( w.getnframes() ), np.int16 ).astype( np.float32 ) / 32768
	return x.reshape( -1, ch ), sr


def save( name, x, sr, peak=None ):
	if peak:
		x = x / max( 1e-6, np.abs( x ).max() ) * peak
	pcm = ( np.clip( x, -1, 1 ) * 32767 ).astype( np.int16 )
	with wave.open( os.path.join( OUT, name + ".wav" ), "wb" ) as w:
		w.setnchannels( x.shape[1] )
		w.setsampwidth( 2 )
		w.setframerate( sr )
		w.writeframes( pcm.tobytes() )
	print( f"{name}.wav  {len( x ) / sr:.1f} s" )


def event( name, wavs, volume, pitch_random=0.0 ):
	with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as f:
		json.dump( { "volume": volume, "sounds": [f"sounds/tatooine/ext/{w}.wav" for w in wavs], "VolumeRandom": 0.1 if pitch_random else 0.0,
			"Pitch": 1.0, "PitchRandom": pitch_random, "DistanceMax": 2000.0, "UI": True }, f )


def gust( x, sr, length ):
	"""The loudest `length` seconds of a recording, faded in over most of its rise and out over its tail."""
	c = np.concatenate( ( [0.0], np.cumsum( ( x ** 2 ).mean( 1 ) ) ) )
	w = sr // 4
	env = np.sqrt( np.maximum( np.concatenate( ( np.zeros( w // 2 ), ( c[w:] - c[:-w] ) / w, np.zeros( w - w // 2 ) ) ), 0 ) )[:len( x )]
	n = int( length * sr )
	peak = int( np.argmax( env[n // 2:len( env ) - n // 2] ) ) + n // 2
	seg = x[peak - int( n * 0.45 ):peak - int( n * 0.45 ) + n].copy()
	t = np.linspace( 0, 1, len( seg ) )[:, None]
	seg *= np.where( t < 0.45, ( t / 0.45 ) ** 1.5, ( ( 1 - t ) / 0.55 ) ** 1.8 )
	return seg


if __name__ == "__main__":
	os.makedirs( OUT, exist_ok=True )
	names = ["sound/atmosphere/light_wind_gusts_loop.wav", "sound/atmosphere/light_wind_whistle_loop.wav",
		"sound/atmosphere/heavy_wind_gusts_loop.wav", "sound/atmosphere/heavy_wind_gusts2_loop.wav", "sound/weather/haboob/haboob_loop.wav"]
	src = read_gma( GMA, set( names ) )
	bed, sr = load( src[names[0]] )
	save( "wind_bed", bed, sr )
	event( "wind_bed", ["wind_bed"], 0.5 )
	whistle, sr = load( src[names[1]] )
	save( "wind_whistle", whistle, sr )
	event( "wind_whistle", ["wind_whistle"], 0.25 )
	k = 0
	for nm, length in ( ( names[2], 5.0 ), ( names[3], 4.5 ), ( names[4], 5.5 ) ):
		x, sr = load( src[nm] )
		half = len( x ) // 2
		for part in ( x[:half], x[half:] ):
			save( f"gust_{k}", gust( part, sr, length ), sr, peak=0.7 )
			k += 1
	event( "gust", [f"gust_{i}" for i in range( k )], 0.45, 0.06 )
