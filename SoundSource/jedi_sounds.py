"""
Original synthesized lightsaber and Force sounds for SWRP's Jedi (Code/Jedi). Pure numpy + wave, no samples:

    blender -b --factory-startup -P SoundSource/jedi_sounds.py   (Blender ships numpy)

Writes 16-bit mono 44.1 kHz WAVs and their .sound events into Assets/sounds/jedi/. Uses the helpers in ui_sounds.py.
The hum and the lock grind and the spinning throw are seamless loops (Code/Jedi/SaberVisual.cs crossfades two copies).
"""
import json
import os
import sys

sys.path.insert( 0, os.path.dirname( os.path.abspath( __file__ ) ) )
import numpy as np
import ui_sounds as u
from ui_sounds import SR, t_axis, env, sweep, noise, lowpass, highpass, bandpass, mix, at


def looped( x, dur, fade ):
	"""ui_sounds.looped, padded first (rounding can leave the buffer a sample short of dur + fade)."""
	need = int( SR * dur ) + int( SR * fade )
	if len( x ) < need: x = np.pad( x, ( 0, need - len( x ) ), mode="edge" )
	return u.looped( x, dur, fade )

OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "jedi" )
os.makedirs( OUT, exist_ok=True )
u.OUT = OUT
rng = np.random.default_rng( 1138 )


def hum_core( dur, f0=86.0, wobble=0.0, glide=None ):
	"""The blade's hum: two slightly detuned buzzing tones beating together, a projector whine above, gently filtered."""
	t = t_axis( dur )
	f = np.full_like( t, f0 ) if glide is None else glide
	f = f * ( 1 + wobble * np.sin( 2 * np.pi * 0.7 * t ) )
	ph1 = 2 * np.pi * np.cumsum( f ) / SR
	ph2 = 2 * np.pi * np.cumsum( f * 1.012 ) / SR
	# a soft sawtooth: a few harmonics falling off
	saw = sum( np.sin( ph1 * k ) / k ** 1.3 for k in range( 1, 9 ) ) + sum( np.sin( ph2 * k ) / k ** 1.3 for k in range( 1, 9 ) ) * 0.8
	whine = np.sin( ph1 * 23.3 ) * 0.035 + np.sin( ph1 * 31.1 ) * 0.02
	buzz = bandpass( noise( dur ), 1800, 3600 ) * 0.02
	return ( lowpass( saw.astype( np.float32 ), 1500 ) + whine + buzz ).astype( np.float32 )


def saber_hum():
	dur, fade = 2.4, 0.4
	x = hum_core( dur + fade, wobble=0.004 )
	t = t_axis( dur + fade )
	x *= 0.9 + 0.1 * np.sin( 2 * np.pi * ( 1 / dur ) * 3 * t )
	return looped( x, dur, fade )
u.LOOPS.add( "saber_hum" )


def saber_on():
	dur = 0.9
	t = t_axis( dur )
	glide = 46 + ( 86 - 46 ) * ( 1 - np.exp( -t / 0.09 ) )
	body = hum_core( dur, glide=glide ) * np.minimum( 1, t / 0.05 ) * ( 0.75 + 0.25 * np.exp( -t / 0.3 ) )
	snap = bandpass( noise( 0.25 ), 1500, 9000 ) * env( int( SR * 0.25 ), 0.001, 0.06, 0.05 ) * 0.9
	zap = sweep( 3200, 300, 0.22 ) * env( int( SR * 0.22 ), 0.001, 0.07, 0.04 ) * 0.25
	return mix( body * 1.2, snap, zap ) * env( len( t ), 0.001, 0, 0.12 )


def saber_off():
	dur = 0.75
	t = t_axis( dur )
	glide = 86 * np.exp( -t / 0.32 ) + 30
	body = hum_core( dur, glide=glide ) * np.exp( -t / 0.22 )
	hiss = bandpass( noise( 0.3 ), 1200, 6000 ) * env( int( SR * 0.3 ), 0.01, 0.08, 0.1 ) * 0.3
	click = bandpass( noise( 0.03 ), 600, 3000 ) * env( int( SR * 0.03 ), 0.001, 0.01, 0.01 ) * 0.6
	return mix( body * 1.3, hiss, at( click, 0.5, 0.6 ) )


def swing( dur=0.5, lift=0.65, f0=86, air=0.55, seed=0 ):
	"""A pass of the blade: the hum shifting up and back down as it sweeps past (Doppler), with a rush of air."""
	t = t_axis( dur )
	bell = np.exp( -( ( t - dur * 0.42 ) / ( dur * 0.2 ) ) ** 2 )
	glide = f0 * ( 1 + lift * bell - 0.15 * ( t / dur ) )
	body = hum_core( dur, f0, glide=glide ) * ( 0.25 + 0.75 * bell )
	rush = bandpass( noise( dur ), 500 + seed * 120, 3500 ) * bell * air
	return mix( body * 1.4, rush ) * env( len( t ), 0.01, 0, 0.06 )


def saber_swing1(): return swing( 0.45, 0.6, 86, 0.5, 0 )
def saber_swing2(): return swing( 0.5, 0.75, 90, 0.6, 2 )
def saber_swing3(): return swing( 0.42, 0.5, 82, 0.45, 4 )
def saber_swing_heavy(): return swing( 0.75, 0.85, 74, 0.8, 1 ) * 1.1


def sizzle( dur, density=900, lo=2000, hi=11000, tau=0.15 ):
	"""Burning: fizzing noise with crackling pops, dying away."""
	t = t_axis( dur )
	fizz = bandpass( noise( dur ), lo, hi ) * np.exp( -t / tau ) * 0.6
	pops = np.zeros( len( t ), dtype=np.float32 )
	for _ in range( int( density * dur ) ):
		s0 = rng.uniform( 0, dur * 0.85 )
		d = highpass( noise( 0.004 ), 2500 ) * env( int( SR * 0.004 ), 0.0003, 0.001, 0.001 ) * rng.uniform( 0.2, 1.0 ) * np.exp( -s0 / tau )
		pops += at( d, s0, dur )
	return mix( fizz, pops )


def metal_ring( dur, base=820, partials=( 1.0, 2.37, 3.91, 5.13, 6.8 ), tau=0.35 ):
	t = t_axis( dur )
	out = np.zeros_like( t, dtype=np.float32 )
	for i, p in enumerate( partials ):
		out += np.sin( 2 * np.pi * base * p * t ) * np.exp( -t / ( tau / ( 1 + i * 0.4 ) ) ) / ( 1 + i )
	return out


def saber_hit_flesh():
	dur = 0.6
	thump = lowpass( noise( 0.12 ), 180 ) * env( int( SR * 0.12 ), 0.002, 0.04, 0.03 ) * 3.0
	zap = sweep( 1400, 200, 0.18 ) * env( int( SR * 0.18 ), 0.001, 0.06, 0.04 ) * 0.4
	return mix( sizzle( dur, 700, 1800, 9000, 0.18 ), thump, zap, hum_core( 0.3, 96 ) * env( int( SR * 0.3 ), 0.001, 0.1, 0.05 ) * 0.5 )


def saber_hit_droid():
	dur = 0.8
	return mix( sizzle( dur, 1100, 1500, 10000, 0.2 ) * 0.8, metal_ring( dur, 640, tau=0.3 ) * 0.5,
		bandpass( noise( 0.05 ), 800, 6000 ) * env( int( SR * 0.05 ), 0.0005, 0.02, 0.02 ) * 1.2 )


def clash( dur=0.7, power=1.0, ring=1.0 ):
	"""Blade on blade: a sharp crack, a burst of electric zap and buzz, and a ring that dies away."""
	t = t_axis( dur )
	crack = bandpass( noise( 0.06 ), 900, 12000 ) * env( int( SR * 0.06 ), 0.0003, 0.015, 0.02 ) * 1.6 * power
	zap = sweep( 4200, 500, 0.3 ) * env( int( SR * 0.3 ), 0.0005, 0.09, 0.05 ) * 0.45
	buzz = np.sign( np.sin( 2 * np.pi * 118 * t ) ) * np.exp( -t / 0.12 ) * 0.18 * power
	ringing = metal_ring( dur, 1180, ( 1.0, 1.73, 2.61, 3.4 ), 0.3 ) * 0.35 * ring
	return mix( crack, zap, lowpass( buzz.astype( np.float32 ), 3000 ), ringing, sizzle( 0.4, 500, 2500, 9000, 0.08 ) * 0.4 )


def saber_clash(): return clash( 0.75, 1.0, 1.0 )
def saber_block(): return clash( 0.45, 0.65, 0.5 )


def saber_parry():
	dur = 0.9
	ping = metal_ring( dur, 1650, ( 1.0, 2.01, 2.99, 4.2 ), 0.5 ) * 0.7
	return mix( clash( 0.5, 0.8, 0.3 ), ping )


def saber_guardbreak():
	dur = 1.0
	t = t_axis( dur )
	boom = lowpass( noise( dur ), 140 ) * np.exp( -t / 0.25 ) * 5
	fall = sweep( 900, 60, 0.6 ) * env( int( SR * 0.6 ), 0.002, 0.25, 0.1 ) * 0.4
	return mix( clash( 0.8, 1.3, 1.2 ), boom, fall )


def saber_lock():
	"""Two blades grinding together: a rough buzz and a steady crackle."""
	dur, fade = 1.6, 0.3
	full = dur + fade
	t = t_axis( full )
	rough = sum( np.sin( 2 * np.pi * 112 * k * t + rng.uniform( 0, 6 ) ) / k for k in range( 1, 12 ) )
	rough *= 0.6 + 0.4 * np.abs( np.sin( 2 * np.pi * 7.3 * t ) )
	crackle = np.zeros( len( t ), dtype=np.float32 )
	for _ in range( 500 ):
		s0 = rng.uniform( 0, full - 0.01 )
		crackle += at( highpass( noise( 0.006 ), 2000 ) * env( int( SR * 0.006 ), 0.0003, 0.002, 0.002 ) * rng.uniform( 0.3, 1 ), s0, full )
	return looped( mix( lowpass( rough.astype( np.float32 ), 2500 ) * 0.4, crackle * 0.8, hum_core( full, 92 ) * 0.6 ), dur, fade )
u.LOOPS.add( "saber_lock" )


def saber_burn(): return sizzle( 0.4, 600, 2500, 11000, 0.1 )


def saber_deflect():
	dur = 0.45
	zing = sweep( 2600, 700, 0.25 ) * env( int( SR * 0.25 ), 0.0005, 0.07, 0.05 ) * 0.6
	return mix( clash( 0.35, 0.5, 0.3 ), zing )


def saber_throw(): return swing( 0.55, 0.9, 80, 0.9, 3 )


def saber_spin():
	"""The thrown saber whirling: the hum swooping up and down ten times a second."""
	dur, fade = 1.2, 0.2
	full = dur + fade
	t = t_axis( full )
	glide = 86 * ( 1 + 0.35 * np.sin( 2 * np.pi * ( 10 / dur * dur ) * t ) )
	body = hum_core( full, glide=glide ) * ( 0.6 + 0.4 * np.sin( 2 * np.pi * 10 * t ) ** 2 )
	rush = bandpass( noise( full ), 600, 3000 ) * ( 0.5 + 0.5 * np.sin( 2 * np.pi * 10 * t ) ) * 0.4
	return looped( mix( body, rush ), dur, fade )
u.LOOPS.add( "saber_spin" )


def saber_catch():
	click = bandpass( noise( 0.04 ), 500, 4000 ) * env( int( SR * 0.04 ), 0.0005, 0.01, 0.02 ) * 1.2
	return mix( click, hum_core( 0.4, 90 ) * env( int( SR * 0.4 ), 0.01, 0.15, 0.1 ) * 0.7 )


def force_push():
	dur = 1.2
	t = t_axis( dur )
	swell = lowpass( noise( dur ), 420 ) * np.exp( -( ( t - 0.12 ) / 0.18 ) ** 2 ) * 6
	air = bandpass( noise( dur ), 300, 2400 ) * np.exp( -( ( t - 0.18 ) / 0.25 ) ** 2 ) * 0.9
	sub = np.sin( 2 * np.pi * 42 * t ) * np.exp( -t / 0.25 ) * np.minimum( 1, t / 0.03 ) * 0.9
	tail = lowpass( noise( dur ), 200 ) * np.exp( -t / 0.5 ) * 1.5
	return mix( swell, air, sub, tail ) * env( len( t ), 0.005, 0, 0.2 )


def force_pull():
	dur = 0.9
	t = t_axis( dur )
	rise = ( t / dur ) ** 2.5
	air = bandpass( noise( dur ), 300, 2600 ) * rise * 1.2
	low = lowpass( noise( dur ), 300 ) * rise * 4
	snap = lowpass( noise( 0.08 ), 600 ) * env( int( SR * 0.08 ), 0.001, 0.02, 0.03 ) * 2
	return mix( air, low, at( snap, dur - 0.08, dur + 0.1 ) )


def force_leap():
	dur = 0.7
	t = t_axis( dur )
	return mix( bandpass( noise( dur ), 250, 2000 ) * np.exp( -( ( t - 0.15 ) / 0.15 ) ** 2 ) * 1.4,
		lowpass( noise( 0.15 ), 160 ) * env( int( SR * 0.15 ), 0.002, 0.05, 0.05 ) * 3 )


def force_speed():
	dur = 1.1
	t = t_axis( dur )
	shimmer = sum( np.sin( 2 * np.pi * f * t * ( 1 + 0.15 * t ) ) for f in ( 880, 1320, 1760 ) ) / 3
	shimmer *= np.minimum( 1, t / 0.2 ) * np.exp( -t / 0.6 ) * 0.25
	whoosh = bandpass( noise( dur ), 400, 3000 ) * np.exp( -( ( t - 0.25 ) / 0.22 ) ** 2 ) * 1.2
	return mix( shimmer.astype( np.float32 ), whoosh )


def force_heal():
	dur = 2.0
	t = t_axis( dur )
	chord = sum( np.sin( 2 * np.pi * f * t ) * ( 1 + 0.3 * np.sin( 2 * np.pi * 0.5 * t + i ) ) for i, f in enumerate( ( 220, 277.2, 329.6, 440 ) ) ) / 4
	shape = np.minimum( 1, t / 0.5 ) * np.exp( -np.maximum( 0, t - 0.9 ) / 0.5 )
	sparkle = sum( np.sin( 2 * np.pi * f * t ) * np.exp( -( ( t - d ) / 0.08 ) ** 2 ) for f, d in ( ( 1760, 0.3 ), ( 2217, 0.5 ), ( 2637, 0.7 ) ) ) * 0.25
	return ( chord * shape * 0.8 + sparkle ).astype( np.float32 )


def force_lightning():
	dur = 0.4
	t = t_axis( dur )
	crackle = np.zeros( len( t ), dtype=np.float32 )
	for _ in range( 260 ):
		s0 = rng.uniform( 0, dur * 0.9 )
		crackle += at( bandpass( noise( 0.008 ), 1200, 9000 ) * env( int( SR * 0.008 ), 0.0003, 0.003, 0.003 ) * rng.uniform( 0.3, 1 ), s0, dur )
	buzz = np.sign( np.sin( 2 * np.pi * 60 * t ) ) * 0.12 * ( 0.5 + 0.5 * rng.random( len( t ) ) )
	return mix( crackle, lowpass( buzz.astype( np.float32 ), 4000 ) ) * env( len( t ), 0.005, 0, 0.05 )


def jedi_levelup():
	notes = ( 523.3, 659.3, 784.0, 1046.5 )
	dur = 1.4
	out = np.zeros( int( SR * dur ), dtype=np.float32 )
	for i, f in enumerate( notes ):
		n = u.blip( f, 0.6, 0.6 )
		out += at( n, i * 0.09, dur )
	return out


def jedi_form():
	return mix( u.blip( 660, 0.12, 0.5 ), at( u.blip( 990, 0.14, 0.4 ), 0.05, 0.2 ) )


RECIPES = {
	"saber_hum": ( saber_hum, 0.32, 1400 ), "saber_on": ( saber_on, 0.7, 1600 ), "saber_off": ( saber_off, 0.6, 1500 ),
	"saber_swing1": ( saber_swing1, 0.6, 1500 ), "saber_swing2": ( saber_swing2, 0.6, 1500 ), "saber_swing3": ( saber_swing3, 0.6, 1500 ),
	"saber_swing_heavy": ( saber_swing_heavy, 0.75, 1700 ),
	"saber_hit_flesh": ( saber_hit_flesh, 0.75, 1800 ), "saber_hit_droid": ( saber_hit_droid, 0.8, 2000 ),
	"saber_clash": ( saber_clash, 0.9, 2600 ), "saber_block": ( saber_block, 0.75, 2200 ), "saber_parry": ( saber_parry, 0.9, 2600 ),
	"saber_guardbreak": ( saber_guardbreak, 0.95, 2800 ), "saber_lock": ( saber_lock, 0.6, 2200 ), "saber_burn": ( saber_burn, 0.35, 1200 ),
	"saber_deflect": ( saber_deflect, 0.7, 2400 ), "saber_throw": ( saber_throw, 0.75, 1800 ), "saber_spin": ( saber_spin, 0.45, 1800 ),
	"saber_catch": ( saber_catch, 0.6, 1400 ),
	"force_push": ( force_push, 0.85, 2600 ), "force_pull": ( force_pull, 0.75, 2200 ), "force_leap": ( force_leap, 0.6, 1600 ),
	"force_speed": ( force_speed, 0.6, 1600 ), "force_heal": ( force_heal, 0.55, 1400 ), "force_lightning": ( force_lightning, 0.75, 2400 ),
	"jedi_levelup": ( jedi_levelup, 0.55, 1000 ), "jedi_form": ( jedi_form, 0.45, 800 ),
}

if __name__ == "__main__":
	only = set( a for a in sys.argv if a in RECIPES )
	for name, ( fn, vol, dist ) in RECIPES.items():
		if only and name not in only: continue
		u.save( name, fn() )
		ui = name in ( "jedi_levelup", "jedi_form" )
		pitch_random = 0.0 if name in u.LOOPS or ui else 0.05
		with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as fh:
			fh.write( json.dumps( { "volume": vol, "sounds": [f"sounds/jedi/{name}.wav"], "VolumeRandom": 0.0 if name in u.LOOPS else 0.05,
				"Pitch": 1.0, "PitchRandom": pitch_random, "DistanceMax": float( dist ), "UI": ui } ) )
