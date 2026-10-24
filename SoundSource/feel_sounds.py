"""
Original synthesized "game feel" sounds for SWRP (Code/Feel): footsteps on planet ground, bolt impacts per surface, voxel
debris, bolts whizzing past your head, door and lift air whooshes, the heavy-hit thump and the screen transition swell.
Pure numpy + wave, no samples, nothing taken from any game or film.

    blender -b --factory-startup -P SoundSource/feel_sounds.py        (Blender ships numpy; plain Python may not have it)
    blender -b --factory-startup -P SoundSource/feel_sounds.py -- bolt_whizz door_whoosh      (just those)

Writes 16-bit mono 44.1 kHz WAVs and their .sound events into Assets/sounds/feel/. Volume and pitch can also be changed in
the .sound files without re-synthesizing (but rerunning this script rewrites them). The menu clicks, hover, open/close and
the heartbeat stay in ui_sounds.py (Assets/sounds/ui); the ship's own footsteps in ambience.py.
"""
import json
import os
import sys
import wave
import numpy as np

SR = 44100
OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "feel" )
os.makedirs( OUT, exist_ok=True )


def t_axis( dur ):
	return np.arange( int( SR * dur ) ) / SR


def noise( dur, rng ):
	return rng.normal( 0, 1, int( SR * dur ) )


def band( x, lo=0.0, hi=None, soft=0.15 ):
	"""FFT band filter with soft edges (fast, no per-sample loop). lo/hi in Hz; soft = edge width as a fraction."""
	n = len( x )
	if n == 0: return x
	f = np.fft.rfftfreq( n, 1 / SR )
	s = np.fft.rfft( x )
	m = np.ones_like( f )
	if lo > 0:
		m *= np.clip( ( f - lo * ( 1 - soft ) ) / max( 1e-6, lo * soft * 2 ), 0, 1 )
	if hi is not None:
		m *= np.clip( ( hi * ( 1 + soft ) - f ) / max( 1e-6, hi * soft * 2 ), 0, 1 )
	return np.fft.irfft( s * m, n )


def decay( n, tau, attack=0.002 ):
	t = np.arange( n ) / SR
	e = np.exp( -t / tau )
	a = max( 1, int( SR * attack ) )
	e[:a] *= np.linspace( 0, 1, a )
	return e


def sweep( f0, f1, dur, shape=None ):
	t = t_axis( dur )
	k = t / dur if shape is None else shape( t / dur )
	f = f0 * ( f1 / f0 ) ** k
	return np.sin( 2 * np.pi * np.cumsum( f ) / SR )


def at( x, start, total ):
	out = np.zeros( int( SR * total ) )
	o = int( SR * start )
	chunk = x[:max( 0, len( out ) - o )]
	out[o:o + len( chunk )] += chunk
	return out


def mix( *parts ):
	n = max( len( p ) for p in parts )
	out = np.zeros( n )
	for p in parts:
		out[:len( p )] += p
	return out


def grains( dur, count, rng, lo, hi, length=( 0.002, 0.006 ), spread=None, vol=( 0.3, 1.0 ) ):
	"""Lots of tiny noise ticks (sand grains, snow crystals, rock chips) scattered over `dur` (or the given envelope)."""
	out = np.zeros( int( SR * dur ) )
	for _ in range( count ):
		s = rng.uniform( 0, dur ) if spread is None else spread( rng )
		ln = rng.uniform( *length )
		g = band( noise( ln, rng ), lo, hi ) * decay( int( SR * ln ), ln / 3, 0.0005 ) * rng.uniform( *vol )
		out += at( g, min( s, dur - ln ), dur )
	return out


def ping( f0, dur, rng, partials=( 1.0, 2.76, 5.40, 8.93 ), tau=0.09 ):
	"""A struck metal plate: inharmonic partials, the high ones dying first."""
	t = t_axis( dur )
	out = np.zeros_like( t )
	for i, p in enumerate( partials ):
		out += np.sin( 2 * np.pi * f0 * p * t + rng.uniform( 0, 6.28 ) ) * np.exp( -t / ( tau / ( 1 + i * 0.7 ) ) ) / ( 1 + i )
	return out


def save( name, x, peak=0.8 ):
	x = np.asarray( x, dtype=np.float64 )
	x = x / max( 1e-9, np.max( np.abs( x ) ) ) * peak
	loud = np.nonzero( np.abs( x ) > 0.003 )[0]
	if len( loud ): x = x[:min( len( x ), loud[-1] + int( SR * 0.02 ) )]
	fade = min( len( x ), int( SR * 0.015 ) )
	x[-fade:] *= np.linspace( 1, 0, fade )
	pcm = ( np.clip( x, -1, 1 ) * 32767 ).astype( np.int16 )
	with wave.open( os.path.join( OUT, name + ".wav" ), "wb" ) as w:
		w.setnchannels( 1 )
		w.setsampwidth( 2 )
		w.setframerate( SR )
		w.writeframes( pcm.tobytes() )
	print( f"{name}.wav  {len( x ) / SR * 1000:.0f} ms" )


def event( name, files, volume, distance, ui=False, pitch_random=0.06, volume_random=0.12 ):
	"""One .sound event over one or more WAV variants (the engine picks one at random)."""
	sound = { "volume": volume, "sounds": [f"sounds/feel/{f}.wav" for f in files], "VolumeRandom": volume_random, "Pitch": 1.0,
		"PitchRandom": pitch_random, "DistanceMax": distance, "UI": ui }
	with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as fh:
		json.dump( sound, fh )


# ----- footsteps on planet ground ------------------------------------------------------------------------------------

def step_sand( seed ):
	"""A boot sinking into loose sand: a soft thud under a short dry crunch of grains."""
	rng = np.random.default_rng( seed )
	dur = 0.22
	thud = band( noise( dur, rng ), 40, 260 ) * decay( int( SR * dur ), 0.035, 0.006 ) * 2.2
	crunch = grains( 0.14, 140, rng, 900, 5200, spread=lambda r: abs( r.normal( 0.03, 0.03 ) ) )
	hiss = band( noise( dur, rng ), 1500, 7000 ) * decay( int( SR * dur ), 0.05, 0.01 ) * 0.25
	return mix( thud, at( crunch, 0.004, dur ), hiss )


def step_snow( seed ):
	"""Packed snow: a muffled press and the squeaky crunch of crystals collapsing, a little longer than sand."""
	rng = np.random.default_rng( seed )
	dur = 0.28
	press = band( noise( dur, rng ), 50, 340 ) * decay( int( SR * dur ), 0.05, 0.012 ) * 1.8
	crunch = grains( 0.2, 260, rng, 1800, 7500, length=( 0.0015, 0.004 ), spread=lambda r: abs( r.normal( 0.06, 0.045 ) ) )
	squeak = sweep( 1300 + rng.uniform( -150, 150 ), 900, 0.07 ) * decay( int( SR * 0.07 ), 0.025, 0.01 ) * 0.12
	return mix( press, at( crunch, 0.01, dur ), at( squeak, 0.05, dur ) )


def step_wet( seed ):
	"""A boot on a rain-soaked metal platform: a dull plate knock and a splash of standing water."""
	rng = np.random.default_rng( seed )
	dur = 0.3
	knock = ping( 170 + rng.uniform( -20, 20 ), dur, rng, tau=0.05 ) * 0.5
	thud = band( noise( dur, rng ), 50, 300 ) * decay( int( SR * dur ), 0.03, 0.004 ) * 1.5
	splash = band( noise( 0.2, rng ), 700, 6000 ) * decay( int( SR * 0.2 ), 0.06, 0.008 ) * 0.9
	drops = grains( 0.25, 25, rng, 1200, 4000, length=( 0.004, 0.01 ), spread=lambda r: r.uniform( 0.03, 0.22 ), vol=( 0.1, 0.4 ) )
	return mix( knock, thud, at( splash, 0.005, dur ), drops )


# ----- bolt impacts per surface --------------------------------------------------------------------------------------

def impact_metal( seed ):
	"""A bolt striking durasteel: a hot sizzle crack and a short metallic ring."""
	rng = np.random.default_rng( seed )
	dur = 0.45
	crack = band( noise( 0.03, rng ), 2000, 12000 ) * decay( int( SR * 0.03 ), 0.006, 0.0005 ) * 1.6
	ring = ping( rng.uniform( 820, 1250 ), dur, rng, tau=0.14 ) * 0.7
	sizzle = band( noise( 0.25, rng ), 3000, 9000 ) * decay( int( SR * 0.25 ), 0.07, 0.004 ) * 0.35
	return mix( crack, ring, sizzle )


def impact_sand( seed ):
	"""A bolt hitting sand: a soft puff, a spray of grains that patters back down."""
	rng = np.random.default_rng( seed )
	dur = 0.5
	puff = band( noise( dur, rng ), 120, 2600 ) * decay( int( SR * dur ), 0.05, 0.002 ) * 1.4
	spray = grains( 0.45, 220, rng, 1200, 6000, spread=lambda r: abs( r.normal( 0.08, 0.12 ) ) )
	sizzle = band( noise( 0.15, rng ), 3000, 9000 ) * decay( int( SR * 0.15 ), 0.04 ) * 0.3
	return mix( puff, at( spray, 0.01, dur ), sizzle )


def impact_snow( seed ):
	"""A bolt into snow: a muffled thump and a hiss of steam as it melts in."""
	rng = np.random.default_rng( seed )
	dur = 0.55
	thump = band( noise( dur, rng ), 60, 900 ) * decay( int( SR * dur ), 0.04, 0.002 ) * 1.5
	steam = band( noise( dur, rng ), 2500, 10000 ) * decay( int( SR * dur ), 0.14, 0.02 ) * 0.55
	powder = grains( 0.3, 120, rng, 2000, 8000, length=( 0.0015, 0.004 ), spread=lambda r: abs( r.normal( 0.05, 0.06 ) ), vol=( 0.1, 0.5 ) )
	return mix( thump, steam, at( powder, 0.01, dur ) )


def impact_debris( seed ):
	"""Voxels breaking off: a dull crack and a clatter of chunks bouncing away."""
	rng = np.random.default_rng( seed )
	dur = 0.75
	crack = band( noise( 0.05, rng ), 300, 5000 ) * decay( int( SR * 0.05 ), 0.012, 0.0005 ) * 1.6
	thud = band( noise( 0.2, rng ), 50, 400 ) * decay( int( SR * 0.2 ), 0.05 ) * 1.4
	clatter = np.zeros( int( SR * dur ) )
	for _ in range( 18 ):
		s = rng.uniform( 0.03, dur - 0.05 )
		ln = rng.uniform( 0.008, 0.025 )
		chunk = band( noise( ln, rng ), rng.uniform( 400, 1500 ), rng.uniform( 2500, 6000 ) ) * decay( int( SR * ln ), ln / 4, 0.0005 )
		clatter += at( chunk * rng.uniform( 0.2, 0.8 ) * np.exp( -s / 0.35 ), s, dur )
	return mix( crack, thud, clatter )


def debris_collapse():
	"""Something big coming apart: a deep grinding rumble with a long tail of falling chunks."""
	rng = np.random.default_rng( 77 )
	dur = 2.2
	t = t_axis( dur )
	rumble = band( noise( dur, rng ), 25, 180 ) * np.minimum( t / 0.05, 1 ) * np.exp( -t / 0.7 ) * 4.0
	grind = band( noise( dur, rng ), 200, 1400 ) * np.minimum( t / 0.1, 1 ) * np.exp( -t / 0.5 ) * 0.8
	clatter = np.zeros( len( t ) )
	for _ in range( 60 ):
		s = abs( rng.normal( 0.4, 0.45 ) )
		ln = rng.uniform( 0.01, 0.04 )
		chunk = band( noise( ln, rng ), rng.uniform( 200, 900 ), rng.uniform( 1800, 5000 ) ) * decay( int( SR * ln ), ln / 4, 0.0005 )
		clatter += at( chunk * rng.uniform( 0.2, 1.0 ), min( s, dur - ln ), dur )
	return mix( rumble, grind, clatter * 0.8 )


# ----- bolts whizzing past -------------------------------------------------------------------------------------------

def bolt_whizz( seed ):
	"""A bolt passing your head: a buzzing tone dropping in pitch (Doppler) as it goes by, with a crackle of air."""
	rng = np.random.default_rng( seed )
	dur = 0.42
	t = t_axis( dur )
	mid = 0.14 + rng.uniform( -0.02, 0.02 )
	f_hi, f_lo = rng.uniform( 1500, 1900 ), rng.uniform( 520, 700 )
	# Doppler: high while it comes, sliding fast through the pass, low as it leaves
	f = f_lo + ( f_hi - f_lo ) / ( 1 + np.exp( ( t - mid ) / 0.022 ) )
	phase = 2 * np.pi * np.cumsum( f ) / SR
	buzz = np.sin( phase ) + 0.45 * np.sin( 2 * phase + 0.4 ) + 0.25 * np.sign( np.sin( 3 * phase ) ) * 0.4
	amp = np.exp( -( ( t - mid ) / 0.05 ) ** 2 ) + 0.35 * np.exp( -np.maximum( 0, t - mid ) / 0.08 ) * ( t > mid )
	air = band( noise( dur, rng ), 2500, 11000 ) * np.exp( -( ( t - mid ) / 0.03 ) ** 2 ) * 0.5
	return buzz * amp * 0.7 + air


# ----- doors and lifts -----------------------------------------------------------------------------------------------

def door_whoosh():
	"""The rush of air as a heavy blast door slides: a filtered noise swell that rises, then settles."""
	rng = np.random.default_rng( 31 )
	dur = 0.9
	t = t_axis( dur )
	n = noise( dur, rng )
	low = band( n, 150, 700 )
	high = band( n, 700, 3200 )
	shape = np.sin( np.pi * np.clip( t / 0.8, 0, 1 ) ) ** 1.5
	sweep_mix = np.clip( t / 0.45, 0, 1 )
	return ( low * ( 1 - 0.6 * sweep_mix ) + high * 0.6 * sweep_mix ) * shape


def lift_whoosh():
	"""A turbolift car accelerating up the shaft: air rushing past the doors with a rising hum under it."""
	rng = np.random.default_rng( 33 )
	dur = 2.0
	t = t_axis( dur )
	shape = np.minimum( t / 0.6, 1 ) * np.minimum( ( dur - t ) / 0.7, 1 )
	rush = band( noise( dur, rng ), 250, 2400 ) * shape * ( 0.6 + 0.4 * np.sin( 2 * np.pi * 0.7 * t ) )
	hum = sweep( 70, 140, dur ) * 0.5 * shape + sweep( 140, 280, dur ) * 0.15 * shape
	return mix( rush, hum )


# ----- the player's own feel -----------------------------------------------------------------------------------------

def hit_thump():
	"""A heavy hit landing on your armour, felt more than heard: a low body thump and a dull plastoid knock."""
	rng = np.random.default_rng( 41 )
	dur = 0.5
	body = sweep( 95, 38, dur ) * decay( int( SR * dur ), 0.11, 0.003 ) * 1.4
	knock = band( noise( 0.06, rng ), 200, 1800 ) * decay( int( SR * 0.06 ), 0.015, 0.0008 ) * 0.9
	return mix( body, knock )


def transition_whoosh():
	"""The screen fading through black (deploy, character screen): a soft swell of air that rises and is cut off."""
	rng = np.random.default_rng( 43 )
	dur = 0.7
	t = t_axis( dur )
	k = t / dur
	swell = band( noise( dur, rng ), 300, 4000 ) * k ** 2.2 * np.minimum( ( dur - t ) / 0.06, 1 )
	tone = sweep( 180, 420, dur ) * k ** 3 * 0.25 * np.minimum( ( dur - t ) / 0.06, 1 )
	return mix( swell, tone )


VARIANTS = {          # name: (recipe(seed), variant count, volume, distance)
	"step_sand": ( step_sand, 4, 0.45, 900 ),
	"step_snow": ( step_snow, 4, 0.45, 900 ),
	"step_wet": ( step_wet, 4, 0.45, 900 ),
	"impact_metal": ( impact_metal, 3, 0.5, 1500 ),
	"impact_sand": ( impact_sand, 3, 0.55, 1500 ),
	"impact_snow": ( impact_snow, 3, 0.55, 1500 ),
	"impact_debris": ( impact_debris, 3, 0.6, 2200 ),
	"bolt_whizz": ( bolt_whizz, 3, 0.55, 600 ),
}

SINGLES = {           # name: (recipe(), volume, distance, ui)
	"debris_collapse": ( debris_collapse, 0.8, 4000, False ),
	"door_whoosh": ( door_whoosh, 0.35, 900, False ),
	"lift_whoosh": ( lift_whoosh, 0.4, 600, True ),
	"hit_thump": ( hit_thump, 0.6, 100, True ),
	"transition_whoosh": ( transition_whoosh, 0.3, 100, True ),
}

if __name__ == "__main__":
	args = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	only = set( args )
	for name, ( fn, count, volume, distance ) in VARIANTS.items():
		if only and name not in only: continue
		files = []
		for i in range( count ):
			save( f"{name}_{i}", fn( 100 * len( name ) + i ) )
			files.append( f"{name}_{i}" )
		event( name, files, volume, distance )
	for name, ( fn, volume, distance, ui ) in SINGLES.items():
		if only and name not in only: continue
		save( name, fn() )
		event( name, [name], volume, distance, ui=ui, pitch_random=0.04 if ui else 0.06 )
