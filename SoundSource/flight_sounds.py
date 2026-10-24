"""
Original synthesized sounds for SWRP's flyable craft (Code/Flight). Pure numpy, no samples, no ripped or film audio.

    "C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe" SoundSource/flight_sounds.py

Writes 16-bit mono 44.1 kHz WAVs and their .sound events into Assets/sounds/flight/: the ARC-170's and gunship's laser
cannons, the chin and ball turrets, torpedo and missile launches, blasts, hull hits and crashes, the cockpit hatch, the
engines spooling up and down, the two engine loops (seamless, 4 s; SwrpFlightShip replays them end to end) and the
hull-failure alarm. Everything is heard from a point in the world.
"""
import os
import sys
import json
import wave
import numpy as np

sys.path.insert( 0, os.path.dirname( os.path.abspath( __file__ ) ) )
from ambience import SR, t_axis, band, sine, env, lfo     # the shared synth helpers
from ambience import reverb as _reverb


def reverb( x, size=0.25, mix=0.35, loop=False ):
	"""The shared hall, with room for its tail (short shots would otherwise be shorter than the echoes)."""
	if loop:
		return _reverb( x, size, mix, True )
	tail = int( SR * 0.4 * size / 0.25 ) + 1
	return _reverb( np.concatenate( [x, np.zeros( tail )] ), size, mix )

OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "flight" )
os.makedirs( OUT, exist_ok=True )
rng = np.random.default_rng( 170 )


def noise( dur ):
	return rng.normal( 0, 1, int( SR * dur ) )


def save( name, x, peak=0.85, volume=0.7, loop=False, distance=4000.0, pitch_random=0.0 ):
	x = np.asarray( x, dtype=np.float64 )
	x = x / max( 1e-6, np.max( np.abs( x ) ) ) * peak
	if not loop:
		fade = int( SR * 0.02 )
		x[-fade:] *= np.linspace( 1, 0, fade )
	pcm = ( np.clip( x, -1, 1 ) * 32767 ).astype( np.int16 )
	with wave.open( os.path.join( OUT, name + ".wav" ), "wb" ) as w:
		w.setnchannels( 1 )
		w.setsampwidth( 2 )
		w.setframerate( SR )
		w.writeframes( pcm.tobytes() )
	sound = { "volume": volume, "sounds": [f"sounds/flight/{name}.wav"], "VolumeRandom": 0.05, "Pitch": 1.0, "PitchRandom": pitch_random,
		"DistanceMax": distance, "UI": False }
	with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as fh:
		json.dump( sound, fh )
	print( f"{name}.wav  {len( x ) / SR:.2f} s" )


def glide( f0, f1, dur, curve=1.0 ):
	"""Phase of a tone gliding from f0 to f1 Hz (curve > 1 drops fast, then lingers)."""
	t = t_axis( dur )
	k = 1 - ( 1 - t / dur ) ** curve
	f = f0 + ( f1 - f0 ) * k
	return 2 * np.pi * np.cumsum( f ) / SR


def laser( d, f0, f1, curve, buzz, body, thump ):
	"""A laser cannon: a falling tone with a buzzing edge, a noisy crack at the start and a low thump under it."""
	n = int( SR * d )
	ph = glide( f0, f1, d, curve )
	tone = np.sin( ph ) + buzz * np.sign( np.sin( ph * 2.0 ) ) * 0.35 + 0.3 * np.sin( ph * 1.5 + 0.4 )
	tone *= env( n, 0.002, d * 0.5, d * 0.35 )
	crack = band( noise( d ), 1500, 9000 ) * env( n, 0.001, 0.03, 0.015 ) * 1.4
	low = np.sin( glide( thump, thump * 0.5, d, 2 ) ) * env( n, 0.002, d * 0.6, 0.06 ) * body
	return reverb( tone + crack + low, 0.4, 0.3 )


def blast( d, low, size ):
	"""A blast: a sharp crack, a rolling low boom, debris rattle settling."""
	n = int( SR * d )
	crack = band( noise( d ), 800, 10000 ) * env( n, 0.001, 0.15, 0.05 ) * 2.0
	boom = band( noise( d ), 20, low ) * env( n, 0.004, d * 0.6, d * 0.35 ) * 5.0
	thud = np.sin( glide( 70, 28, d, 2 ) ) * env( n, 0.003, d * 0.5, 0.35 ) * 1.6
	rattle = band( noise( d ), 1500, 6000 ) * env( n, 0.2, d * 0.5, d * 0.3 ) * 0.25 * ( lfo( d, 17, 1.0 ) - 0.5 ).clip( 0 )
	return reverb( crack + boom + thud + rattle, size, 0.45 )


def engine_loop( d, base, whine, roar ):
	"""A seamless engine loop: every partial is a whole number of cycles in d seconds; noise is filtered round the loop."""
	t = t_axis( d )
	def cyc( f ):
		return np.round( f * d ) / d
	hum = sum( a * np.sin( 2 * np.pi * cyc( base * h ) * t ) for h, a in ( ( 1, 1.0 ), ( 2, 0.55 ), ( 3, 0.3 ), ( 4.5, 0.12 ) ) )
	wh = np.sin( 2 * np.pi * cyc( whine ) * t + 0.6 * np.sin( 2 * np.pi * cyc( 2.0 ) * t ) ) * 0.25
	wh += np.sin( 2 * np.pi * cyc( whine * 1.503 ) * t ) * 0.1
	r = band( noise( d ), roar[0], roar[1] ) * 1.6
	wobble = 1 + 0.08 * np.sin( 2 * np.pi * cyc( 3.0 ) * t )
	return reverb( ( hum * 0.5 + wh + r ) * wobble, 0.3, 0.2, loop=True )


# ----- guns ---------------------------------------------------------------------------------------------------------------

save( "arc_cannon", laser( 0.42, 2100, 210, 2.6, 0.5, 1.0, 140 ), volume=0.8, distance=7000.0, pitch_random=0.05 )
save( "laat_cannon", laser( 0.46, 1500, 160, 2.2, 0.8, 1.2, 110 ), volume=0.8, distance=7000.0, pitch_random=0.05 )
save( "laat_chin", laser( 0.22, 2600, 520, 2.0, 0.35, 0.4, 220 ), volume=0.6, distance=5000.0, pitch_random=0.08 )
save( "turret", laser( 0.3, 2400, 300, 2.4, 0.6, 0.7, 170 ), volume=0.7, distance=6000.0, pitch_random=0.06 )


def launch( d, hiss_lo, hiss_hi, tone0, tone1 ):
	n = int( SR * d )
	kick = np.sin( glide( 90, 40, d, 3 ) ) * env( n, 0.002, 0.2, 0.08 ) * 1.5
	hiss = band( noise( d ), hiss_lo, hiss_hi ) * env( n, 0.01, d * 0.7, d * 0.5 ) * 1.4
	tone = np.sin( glide( tone0, tone1, d, 0.6 ) ) * env( n, 0.05, d * 0.5, d * 0.6 ) * 0.4
	return reverb( kick + hiss + tone, 0.5, 0.35 )


save( "torpedo", launch( 1.3, 600, 5000, 300, 1400 ), volume=0.8, distance=7000.0 )
save( "missile", launch( 1.0, 900, 7000, 500, 900 ), volume=0.75, distance=7000.0, pitch_random=0.06 )

# ----- damage -------------------------------------------------------------------------------------------------------------

save( "explode_small", blast( 1.6, 180, 0.6 ), volume=0.9, distance=9000.0 )
save( "explode_big", blast( 3.4, 120, 1.0 ), volume=1.0, distance=14000.0 )


def clang( d, freqs, noise_amt ):
	n = int( SR * d )
	ring = sum( np.sin( 2 * np.pi * f * t_axis( d ) ) * np.exp( -t_axis( d ) * ( 6 + i * 3 ) ) / ( 1 + i ) for i, f in enumerate( freqs ) )
	hit = band( noise( d ), 300, 5000 ) * env( n, 0.001, 0.08, 0.03 ) * noise_amt
	return reverb( ring + hit, 0.35, 0.3 )


save( "hull_hit", clang( 0.45, ( 610, 1420, 2330, 3570 ), 1.2 ), volume=0.6, distance=4000.0, pitch_random=0.1 )
save( "impact", clang( 1.2, ( 95, 230, 410, 780 ), 3.0 ) + 0.0, volume=0.9, distance=6000.0, pitch_random=0.05 )


def hatch():
	d = 0.9
	n = int( SR * d )
	thunk = clang( d, ( 140, 330, 610 ), 1.5 ) * 0.8
	hiss = band( noise( d ), 2000, 9000 ) * env( n, 0.08, 0.4, 0.25 ) * 0.4
	out = thunk.copy()
	o = int( SR * 0.12 )
	out[o:o + len( hiss )] += hiss[:len( out ) - o]
	return out


save( "seat", hatch(), volume=0.5, distance=1500.0 )


def door():
	"""The gunship's bay door: a latch clack, a long rumbling slide along its rails, a thud at the end."""
	d = 1.9
	n = int( SR * d )
	t = t_axis( d )
	slide = band( noise( d ), 120, 1400 ) * env( n, 0.15, 0.25 ) * ( 0.7 + 0.3 * np.sin( 2 * np.pi * 11 * t ) ) * 1.3
	whine = np.sin( glide( 180, 140, d, 1.0 ) ) * env( n, 0.2, 0.3 ) * 0.25
	out = slide + whine
	clack = clang( 0.3, ( 900, 1700, 2600 ), 1.0 )
	out[:len( clack )] += clack[:n] * 0.8
	thud = clang( 0.5, ( 110, 260, 480 ), 2.0 )
	o = int( SR * 1.55 )
	out[o:o + len( thud )] += thud[:n - o]
	return out


save( "door", door(), volume=0.7, distance=2500.0 )

# ----- engines ------------------------------------------------------------------------------------------------------------


def spool( d, up ):
	t = t_axis( d )
	k = ( t / d ) if up else 1 - ( t / d )
	k = k ** ( 1.6 if up else 0.7 )
	ph = glide( 120, 900, d, 1.0 ) if up else glide( 900, 120, d, 1.0 )
	whine = np.sin( ph ) * 0.3 + np.sin( ph * 1.5 ) * 0.12
	rumble = band( noise( d ), 30, 300 ) * 1.6
	clicks = band( noise( d ), 2000, 6000 ) * ( lfo( d, 9.0, 1.0 ) - 1.6 ).clip( 0 ) * 0.4
	x = ( whine + rumble * ( 0.3 + 0.7 * k ) + clicks * ( 1 - k ) ) * ( 0.2 + 0.8 * k if up else 0.15 + 0.85 * k )
	return reverb( x * env( len( x ), 0.05, 0.3 ), 0.4, 0.3 )


save( "engine_start", spool( 2.6, True ), volume=0.7, distance=5000.0 )
save( "engine_stop", spool( 2.2, False ), volume=0.6, distance=5000.0 )
save( "engine_fighter", engine_loop( 4.0, 62, 1350, ( 400, 3200 ) ), volume=0.65, loop=True, distance=8000.0 )
save( "engine_gunship", engine_loop( 4.0, 38, 520, ( 60, 900 ) ), volume=0.75, loop=True, distance=9000.0 )


def alarm():
	d = 2.4
	out = np.zeros( int( SR * d ) )
	for i in range( 4 ):
		n = int( SR * 0.38 )
		f = 880 if i % 2 == 0 else 660
		tone = ( np.sign( np.sin( 2 * np.pi * f * np.arange( n ) / SR ) ) * 0.4 + np.sin( 2 * np.pi * f * np.arange( n ) / SR ) ) * env( n, 0.005, 0.05 )
		o = int( SR * i * 0.55 )
		out[o:o + n] += tone[:len( out ) - o]
	return reverb( out, 0.3, 0.3 )


save( "alarm", alarm(), volume=0.55, distance=3000.0 )
