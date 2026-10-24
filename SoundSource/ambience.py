"""
Original synthesized ship ambience for SWRP: per-area room tones (seamless loops), distant one-shot ship sounds, and
footsteps with a light armour rattle. Pure numpy, no samples, no ripped audio.

    blender -b --factory-startup -P SoundSource/ambience.py      (Blender ships numpy)

Writes 16-bit mono 44.1 kHz WAVs and their .sound events into Assets/sounds/ambient/. SwrpAmbience (Code/Player) plays
the room tones and distant sounds; SwrpFootsteps the steps.
"""
import os
import json
import wave
import numpy as np

SR = 44100
OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "ambient" )
os.makedirs( OUT, exist_ok=True )
rng = np.random.default_rng( 1138 )


def t_axis( dur ):
	return np.arange( int( SR * dur ) ) / SR


def noise( dur ):
	return rng.normal( 0, 1, int( SR * dur ) )


def band( x, lo, hi ):
	"""Smooth band filter in the frequency domain (no ringing, no python loops)."""
	f = np.fft.rfft( x )
	fr = np.fft.rfftfreq( len( x ), 1 / SR )
	g = np.ones_like( fr )
	if lo > 0:
		g *= 1 / ( 1 + ( lo / np.maximum( fr, 1e-3 ) ) ** 4 )
	if hi > 0:
		g *= 1 / ( 1 + ( fr / hi ) ** 4 )
	return np.fft.irfft( f * g, len( x ) )


def sine( f, dur, phase=0.0 ):
	return np.sin( 2 * np.pi * f * t_axis( dur ) + phase )


def env( n, attack, release, decay=0.0 ):
	e = np.ones( n )
	a = max( 1, int( SR * attack ) )
	r = max( 1, int( SR * release ) )
	e[:a] = np.linspace( 0, 1, a )
	e[-r:] *= np.linspace( 1, 0, r )
	if decay > 0:
		e *= np.exp( -np.arange( n ) / ( SR * decay ) )
	return e


def place( buf, x, start ):
	o = int( SR * start ) % len( buf )
	end = min( len( buf ), o + len( x ) )
	buf[o:end] += x[:end - o]
	rest = len( x ) - ( end - o )
	if rest > 0:                                  # wrap round: loops stay seamless
		buf[:rest] += x[end - o:]


def lfo( dur, rate, depth, phase=0.0 ):
	return 1.0 + depth * np.sin( 2 * np.pi * rate * t_axis( dur ) + phase )


def reverb( x, size=0.25, mix=0.35, loop=False ):
	"""A cheap hall: a few decaying echoes, wrapped round for loops."""
	out = x.copy()
	for d, g in ( ( 0.031, 0.5 ), ( 0.047, 0.4 ), ( 0.071, 0.33 ), ( 0.113, 0.26 ), ( 0.17, 0.2 ), ( 0.26, 0.14 ), ( 0.39, 0.09 ) ):
		o = int( SR * d * size / 0.25 )
		if loop:
			out += np.roll( x, o ) * g * mix
		else:
			out[o:] += x[:len( x ) - o] * g * mix
	return out


def save( name, x, peak=0.8, loop=False, volume=0.5, distance=None, ui=False, files=None ):
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
	if files is None:
		sound = { "volume": volume, "sounds": [f"sounds/ambient/{name}.wav"], "VolumeRandom": 0.0, "Pitch": 1.0, "PitchRandom": 0.0,
			"DistanceMax": distance or 2000.0, "UI": ui }
		with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as fh:
			json.dump( sound, fh )
	print( f"{name}.wav  {len( x ) / SR:.2f} s" )


def event( name, files, volume, distance, pitch_random=0.06, volume_random=0.15 ):
	"""One .sound event over several WAV variants (picked at random)."""
	sound = { "volume": volume, "sounds": [f"sounds/ambient/{f}.wav" for f in files], "VolumeRandom": volume_random, "Pitch": 1.0,
		"PitchRandom": pitch_random, "DistanceMax": distance, "UI": False }
	with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as fh:
		json.dump( sound, fh )


LOOP = 16.0       # seconds; every room tone is a seamless loop of this length (keep every tone and LFO a whole number of cycles in it)


def hum( dur, f, harmonics=( 1.0, 0.5, 0.25 ), wobble=0.0 ):
	out = np.zeros( int( SR * dur ) )
	for i, a in enumerate( harmonics ):
		out += a * sine( f * ( i + 1 ), dur, i * 1.3 )
	return out * ( lfo( dur, 0.25, wobble ) if wobble else 1.0 )


# ----- room tones -----------------------------------------------------------------------------------------------------

def amb_hangar():
	"""Open hangar: wind across the shield openings, a deep idling-engine hum, slow swells."""
	d = LOOP
	wind = band( noise( d ), 50, 320 ) * lfo( d, 1 / 8, 0.5 ) * lfo( d, 1 / 16, 0.3, 1.0 )
	gust = band( noise( d ), 300, 900 ) * np.clip( lfo( d, 1 / 16, 1.0, 2.0 ) - 1.4, 0, None ) * 0.5
	engine = hum( d, 41.0, ( 1.0, 0.6, 0.35, 0.2 ), 0.08 ) * 0.9
	return reverb( wind * 0.2 + gust * 0.1 + engine, 0.5, 0.4, loop=True )


def amb_bridge():
	"""Bridge: a soft electronic bed, air, and console chirps here and there."""
	d = LOOP
	bed = hum( d, 120.0, ( 1.0, 0.3 ), 0.03 ) * 0.25 + band( noise( d ), 150, 700 ) * 0.05
	out = bed.copy()
	r = np.random.default_rng( 7 )
	for _ in range( 22 ):
		f = r.choice( [1320, 1568, 1760, 2093, 2349, 2637] )
		n = int( SR * r.uniform( 0.04, 0.09 ) )
		blip = np.sin( 2 * np.pi * f * np.arange( n ) / SR ) * env( n, 0.003, 0.02 ) * r.uniform( 0.08, 0.2 )
		place( out, blip, r.uniform( 0, d ) )
		if r.random() < 0.4:
			place( out, blip * 0.6, r.uniform( 0, d ) )
	return reverb( out, 0.3, 0.3, loop=True )


def amb_corridor():
	"""Corridors: air handling through the vents and a faint electrical hum."""
	d = LOOP
	air = band( noise( d ), 50, 260 ) * lfo( d, 1 / 4, 0.12 ) * 0.22
	return air + hum( d, 60.0, ( 1.0, 0.4, 0.15 ) ) * 0.2


def amb_engineering():
	"""Engineering: the reactor's slow throb, a high whine, machinery rumble."""
	d = LOOP
	throb = hum( d, 52.0, ( 1.0, 0.7, 0.4 ) ) * ( 0.6 + 0.4 * np.sin( 2 * np.pi * 1.25 * t_axis( d ) ) ** 2 )
	whine = sine( 880.0, d ) * 0.05 * lfo( d, 0.5, 0.3 )
	rumble = band( noise( d ), 30, 150 ) * 0.3
	return reverb( throb + whine + rumble, 0.4, 0.35, loop=True )


def amb_brig():
	"""Brig: the containment fields' electric buzz, flickering, over still air."""
	d = LOOP
	t = t_axis( d )
	saw = 2 * ( ( t * 120.0 ) % 1.0 ) - 1
	buzz = band( saw, 100, 2500 ) * 0.25 * ( 1 + 0.25 * np.sin( 2 * np.pi * 7.25 * t ) * np.sin( 2 * np.pi * 0.3125 * t ) )
	air = band( noise( d ), 60, 280 ) * 0.12
	return buzz * 0.8 + air


def amb_medbay():
	"""Medbay: a quiet clean hum and soft monitor beeps."""
	d = LOOP
	out = hum( d, 180.0, ( 1.0, 0.2 ) ) * 0.12 + band( noise( d ), 100, 400 ) * 0.05
	for i in range( 16 ):
		n = int( SR * 0.12 )
		beep = np.sin( 2 * np.pi * 1046.5 * np.arange( n ) / SR ) * env( n, 0.005, 0.05 ) * 0.1
		place( out, beep, i * 1.0 )
	return reverb( out, 0.25, 0.3, loop=True )


def amb_temple():
	"""Jedi temple: a calm, airy hall."""
	d = LOOP
	pad = ( hum( d, 110.0, ( 1.0, 0.0, 0.3 ) ) + hum( d, 165.0, ( 1.0, 0.0, 0.2 ) ) ) * 0.08 * lfo( d, 1 / 16, 0.4 )
	air = band( noise( d ), 100, 500 ) * 0.07 * lfo( d, 1 / 8, 0.3 )
	return reverb( pad + air, 0.8, 0.5, loop=True )


# ----- distant one-shots (played far off, muffled) --------------------------------------------------------------------

def muffle( x ):
	return reverb( band( x, 60, 1400 ), 0.6, 0.6 )


def dist_hatch():
	d = 1.6
	hiss = band( noise( 0.5 ), 800, 5000 ) * env( int( SR * 0.5 ), 0.01, 0.3 )
	clunk = sine( 70, 0.3 ) * env( int( SR * 0.3 ), 0.002, 0.2, 0.08 )
	out = np.zeros( int( SR * d ) )
	place( out, hiss * 0.5, 0.0 )
	place( out, clunk, 0.45 )
	return muffle( out )


def dist_cargo():
	d = 2.0
	out = np.zeros( int( SR * d ) )
	for i, s in enumerate( ( 0.0, 0.22, 0.31, 0.6 ) ):
		n = int( SR * 0.35 )
		hit = band( noise( 0.35 ), 300, 3000 ) * env( n, 0.001, 0.1, 0.05 ) + sine( rng.uniform( 180, 420 ), 0.35 ) * env( n, 0.001, 0.2, 0.12 ) * 0.7
		place( out, hit * ( 1.0 - i * 0.2 ), s )
	return muffle( out )


def dist_droid():
	"""A little astromech-style warble: chirps gliding up and down (original)."""
	d = 1.4
	out = np.zeros( int( SR * d ) )
	t0 = 0.0
	for _ in range( 7 ):
		dur = rng.uniform( 0.05, 0.14 )
		f0, f1 = rng.uniform( 900, 2600 ), rng.uniform( 900, 2600 )
		t = t_axis( dur )
		f = f0 + ( f1 - f0 ) * t / dur
		ph = 2 * np.pi * np.cumsum( f ) / SR
		place( out, np.sin( ph ) * env( len( t ), 0.004, 0.02 ) * 0.6, t0 )
		t0 += dur + rng.uniform( 0.01, 0.06 )
	return reverb( band( out, 300, 3200 ), 0.5, 0.5 )


def dist_pa():
	"""Far-off public address: a two-tone chime and garbled 'speech' (filtered pulses, no words), muffled by bulkheads."""
	d = 3.6
	out = np.zeros( int( SR * d ) )
	for f, s in ( ( 784, 0.0 ), ( 659, 0.28 ) ):
		n = int( SR * 0.45 )
		place( out, np.sin( 2 * np.pi * f * np.arange( n ) / SR ) * env( n, 0.005, 0.3, 0.2 ) * 0.6, s )
	t = 0.8
	while t < 3.1:
		syl = rng.uniform( 0.08, 0.22 )
		n = int( SR * syl )
		f0 = rng.uniform( 110, 150 )
		pulses = 2 * ( ( np.arange( n ) / SR * f0 ) % 1.0 ) - 1
		formant = band( pulses, rng.uniform( 300, 600 ), rng.uniform( 900, 2200 ) )
		place( out, formant * env( n, 0.015, 0.03 ) * rng.uniform( 0.4, 0.8 ), t )
		t += syl + rng.uniform( 0.02, 0.12 ) + ( 0.25 if rng.random() < 0.15 else 0 )
	return reverb( band( out, 250, 1800 ), 0.9, 0.7 )


# ----- footsteps -------------------------------------------------------------------------------------------------------

def step_grate( seed ):
	r = np.random.default_rng( seed )
	d = 0.35
	n = int( SR * d )
	hit = band( r.normal( 0, 1, n ), 400, 6000 ) * env( n, 0.001, 0.05, 0.03 )
	ring = sum( np.sin( 2 * np.pi * f * np.arange( n ) / SR ) * a for f, a in ( ( r.uniform( 900, 1300 ), 0.5 ), ( r.uniform( 2100, 2900 ), 0.3 ) ) ) * env( n, 0.001, 0.1, 0.09 )
	thud = np.sin( 2 * np.pi * 90 * np.arange( n ) / SR ) * env( n, 0.002, 0.05, 0.04 )
	return hit * 0.6 + ring * 0.35 + thud * 0.7


def step_plate( seed ):
	r = np.random.default_rng( seed )
	d = 0.25
	n = int( SR * d )
	thud = np.sin( 2 * np.pi * r.uniform( 70, 95 ) * np.arange( n ) / SR ) * env( n, 0.002, 0.08, 0.05 )
	click = band( r.normal( 0, 1, n ), 1200, 5000 ) * env( n, 0.0005, 0.03, 0.012 )
	scuff = band( r.normal( 0, 1, n ), 300, 2000 ) * env( n, 0.01, 0.1, 0.06 ) * 0.25
	return thud + click * 0.35 + scuff


def armour_rattle( seed ):
	r = np.random.default_rng( seed )
	d = 0.3
	out = np.zeros( int( SR * d ) )
	for _ in range( r.integers( 2, 4 ) ):
		n = int( SR * 0.05 )
		clack = band( r.normal( 0, 1, n ), 1800, 7000 ) * env( n, 0.0005, 0.02, 0.01 )
		place( out, clack * r.uniform( 0.4, 1.0 ), r.uniform( 0.0, 0.14 ) )
	return out


if __name__ == "__main__":
	for name, fn, vol in ( ( "amb_hangar", amb_hangar, 0.2 ), ( "amb_bridge", amb_bridge, 0.16 ), ( "amb_corridor", amb_corridor, 0.11 ),
			( "amb_engineering", amb_engineering, 0.2 ), ( "amb_brig", amb_brig, 0.13 ), ( "amb_medbay", amb_medbay, 0.12 ), ( "amb_temple", amb_temple, 0.14 ) ):
		save( name, fn(), 0.7, loop=True, volume=vol, ui=True )
	save( "dist_hatch", dist_hatch(), files=[] )
	save( "dist_cargo", dist_cargo(), files=[] )
	save( "dist_droid", dist_droid(), files=[] )
	save( "dist_pa", dist_pa(), files=[] )
	event( "distant", ["dist_hatch", "dist_cargo", "dist_droid", "dist_pa"], 0.28, 3000, 0.08, 0.2 )
	for i in range( 4 ):
		save( f"step_grate_{i}", step_grate( 10 + i ), 0.75, files=[] )
		save( f"step_plate_{i}", step_plate( 20 + i ), 0.75, files=[] )
		save( f"armour_{i}", armour_rattle( 30 + i ), 0.6, files=[] )
	event( "step_grate", [f"step_grate_{i}" for i in range( 4 )], 0.45, 900 )
	event( "step_plate", [f"step_plate_{i}" for i in range( 4 )], 0.4, 800 )
	event( "armour", [f"armour_{i}" for i in range( 4 )], 0.18, 500, 0.12, 0.3 )
