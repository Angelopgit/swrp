"""
Original synthesized sounds for SWRP's hyperspace jump. Pure numpy, no samples, no ripped or film audio.

    blender -b --factory-startup -P SoundSource/space_sounds.py      (Blender ships numpy)

Writes 16-bit mono 44.1 kHz WAVs and their .sound events into Assets/sounds/space/: the jump alarm chime, the engines
spooling up (with the hull's shudder), the jump itself, the tunnel's rumble (a seamless 8 s loop) and the drop out.
SwrpSpace (Code/Map) plays them; they are heard everywhere aboard, not from a point.
"""
import os
import sys
import json
import wave
import numpy as np

sys.path.insert( 0, os.path.dirname( os.path.abspath( __file__ ) ) )
from ambience import SR, t_axis, band, sine, env, reverb, lfo     # the shared synth helpers

OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "space" )
os.makedirs( OUT, exist_ok=True )
rng = np.random.default_rng( 1977 )


def noise( dur ):
	return rng.normal( 0, 1, int( SR * dur ) )


def save( name, x, peak=0.85, volume=0.7, loop=False ):
	x = np.asarray( x, dtype=np.float64 )
	x = x / max( 1e-6, np.max( np.abs( x ) ) ) * peak
	if not loop:
		fade = int( SR * 0.03 )
		x[-fade:] *= np.linspace( 1, 0, fade )
	pcm = ( np.clip( x, -1, 1 ) * 32767 ).astype( np.int16 )
	with wave.open( os.path.join( OUT, name + ".wav" ), "wb" ) as w:
		w.setnchannels( 1 )
		w.setsampwidth( 2 )
		w.setframerate( SR )
		w.writeframes( pcm.tobytes() )
	sound = { "volume": volume, "sounds": [f"sounds/space/{name}.wav"], "VolumeRandom": 0.0, "Pitch": 1.0, "PitchRandom": 0.0,
		"DistanceMax": 2000.0, "UI": True }
	with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as fh:
		json.dump( sound, fh )
	print( f"{name}.wav  {len( x ) / SR:.2f} s" )


def sweep( f0, f1, dur, curve=1.0 ):
	"""A sine gliding from f0 to f1 Hz (curve > 1 lingers low, then rises fast)."""
	t = t_axis( dur )
	k = ( t / dur ) ** curve
	f = f0 + ( f1 - f0 ) * k
	return np.sin( 2 * np.pi * np.cumsum( f ) / SR )


def hyper_alarm():
	"""Three rising two-note chimes over a soft tone bed: 'stand by for the jump'."""
	d = 3.2
	out = np.zeros( int( SR * d ) )
	for i, ( a, b ) in enumerate( ( ( 523.25, 659.25 ), ( 587.33, 739.99 ), ( 659.25, 830.61 ) ) ):
		for j, f in enumerate( ( a, b ) ):
			start = i * 0.9 + j * 0.22
			n = int( SR * 0.55 )
			tone = ( np.sin( 2 * np.pi * f * np.arange( n ) / SR ) + 0.35 * np.sin( 2 * np.pi * f * 2.01 * np.arange( n ) / SR )
				+ 0.12 * np.sin( 2 * np.pi * f * 3.0 * np.arange( n ) / SR ) ) * env( n, 0.006, 0.08, 0.16 )
			o = int( SR * start )
			out[o:o + n] += tone[:len( out ) - o] * ( 0.8 + 0.1 * i )
	bed = sine( 110, d ) * 0.06 * env( len( out ), 0.3, 0.8 )
	return reverb( out + bed, 0.6, 0.45 )


def hyper_spool():
	"""The hyperdrive spooling up for six seconds: a rising whine and drone, the hull groaning and rattling."""
	d = 6.0
	t = t_axis( d )
	ramp = ( t / d ) ** 1.6
	drone = ( sweep( 38, 92, d, 1.4 ) + 0.5 * sweep( 76, 184, d, 1.4 ) + 0.25 * sweep( 114, 276, d, 1.4 ) ) * ( 0.3 + 0.7 * ramp )
	whine = ( sweep( 300, 1900, d, 2.2 ) * 0.25 + sweep( 452, 2850, d, 2.2 ) * 0.1 ) * ramp ** 1.5
	rumble = band( noise( d ), 25, 160 ) * ( 0.2 + 0.9 * ramp ) * 2.5
	# metal groans and rattles as the hull takes the strain
	rattle = band( noise( d ), 900, 4000 ) * np.clip( lfo( d, 13.0, 1.0 ) - 1.0, 0, None ) * ramp * 0.6
	groan = np.zeros( len( t ) )
	for start, f in ( ( 1.6, 61 ), ( 3.4, 47 ), ( 4.6, 73 ) ):
		n = int( SR * 1.2 )
		g = sweep( f, f * 0.82, 1.2 ) * env( n, 0.15, 0.6 ) * band( noise( 1.2 ), 30, 400 ) * 0.4
		o = int( SR * start )
		groan[o:o + n] += g[:len( groan ) - o]
	# a sharp intake right before the jump
	suck = band( noise( d ), 500, 6000 ) * np.clip( ( t - ( d - 0.7 ) ) / 0.7, 0, 1 ) ** 2 * 0.8
	return reverb( drone + whine + rumble + rattle + groan + suck, 0.5, 0.3 )


def hyper_jump():
	"""The jump: a deep boom, a sub drop and a roaring whoosh that tears away."""
	d = 2.4
	t = t_axis( d )
	boom = sweep( 70, 24, d, 0.5 ) * np.exp( -t / 0.7 ) * 1.4
	whoosh = band( noise( d ), 200, 5000 ) * np.exp( -t / 0.5 ) * 1.2
	tear = band( noise( d ), 2500, 9000 ) * np.exp( -t / 0.18 ) * 0.7
	ring = ( sine( 220, d ) * 0.15 + sine( 331, d ) * 0.08 ) * np.exp( -t / 0.9 )
	return reverb( boom + whoosh + tear + ring, 0.8, 0.5 )


def hyper_tunnel():
	"""Inside the tunnel: a deep, swirling rumble and airy rush. An 8 s seamless loop (every tone and LFO a whole number of cycles)."""
	d = 8.0
	rush = band( noise( d ), 150, 2200 ) * lfo( d, 0.5, 0.35 ) * lfo( d, 0.125, 0.25, 1.0 )
	low = band( noise( d ), 20, 120 ) * 2.2
	hum = ( sine( 55, d ) + 0.5 * sine( 110, d, 0.7 ) + 0.2 * sine( 165, d, 1.9 ) ) * 0.35 * lfo( d, 0.25, 0.2 )
	shimmer = band( noise( d ), 3000, 8000 ) * np.clip( lfo( d, 0.375, 1.0, 2.0 ) - 1.3, 0, None ) * 0.5
	# band() filters in the frequency domain, which wraps round, so the filtered noise already loops seamlessly
	return reverb( rush + low + hum + shimmer, 0.4, 0.3, loop=True )


def hyper_exit():
	"""Dropping out: a rising reverse whoosh that snaps into a heavy thump and settles."""
	d = 2.2
	t = t_axis( d )
	rise = band( noise( d ), 300, 6000 ) * np.clip( t / 0.55, 0, 1 ) ** 3 * ( t < 0.6 )
	thump_t = np.clip( t - 0.6, 0, None )
	thump = sweep( 90, 30, d, 0.4 ) * np.exp( -thump_t / 0.35 ) * ( t >= 0.6 ) * 1.4
	settle = band( noise( d ), 40, 300 ) * np.exp( -thump_t / 0.8 ) * ( t >= 0.6 ) * 0.9
	clank = band( noise( d ), 1500, 5000 ) * np.exp( -thump_t / 0.08 ) * ( t >= 0.6 ) * 0.5
	return reverb( rise + thump + settle + clank, 0.7, 0.45 )


if __name__ == "__main__":
	save( "hyper_alarm", hyper_alarm(), 0.75, 0.55 )
	save( "hyper_spool", hyper_spool(), 0.85, 0.7 )
	save( "hyper_jump", hyper_jump(), 0.9, 0.85 )
	save( "hyper_tunnel", hyper_tunnel(), 0.7, 0.45, loop=True )
	save( "hyper_exit", hyper_exit(), 0.9, 0.8 )
