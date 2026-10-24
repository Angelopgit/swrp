"""
Original synthesized sounds for SWRP's living ship: sparks from damaged panels, steam vent bursts, astromech bleeps,
mouse droid chirps, and the medbay's bacta pods (glass hiss, filling, bubbling, draining). Pure numpy, no samples, no ripped or film audio.

    blender -b --factory-startup -P SoundSource/ship_life.py      (Blender ships numpy)

Writes 16-bit mono 44.1 kHz WAVs and their .sound events into Assets/sounds/ambient/ (with the ship ambience).
SwrpLivingShip (Code/Map) plays them from where they happen.
"""
import os
import sys
import numpy as np

sys.path.insert( 0, os.path.dirname( os.path.abspath( __file__ ) ) )
import ambience as A
from ambience import SR, band, env, reverb

rng = np.random.default_rng( 4066 )


def noise( dur ):
	return rng.normal( 0, 1, int( SR * dur ) )


def chirp( f0, f1, dur, wobble=0.0, wob_rate=30.0 ):
	"""A sine sliding from f0 to f1 Hz, with an optional fast warble."""
	n = int( SR * dur )
	t = np.arange( n ) / SR
	f = f0 + ( f1 - f0 ) * ( t / dur )
	if wobble:
		f = f * ( 1 + wobble * np.sin( 2 * np.pi * wob_rate * t ) )
	ph = 2 * np.pi * np.cumsum( f ) / SR
	return ( np.sin( ph ) + 0.25 * np.sign( np.sin( ph ) ) * 0.4 ) * env( n, 0.004, min( 0.03, dur / 3 ) )


def sequence( parts, gap=0.02 ):
	out = []
	for p in parts:
		out.append( p )
		out.append( np.zeros( int( SR * gap ) ) )
	return np.concatenate( out )


def spark( seed ):
	"""An electrical crackle: a fizzing burst of clicks over a short buzz."""
	r = np.random.default_rng( seed )
	d = r.uniform( 0.5, 0.9 )
	n = int( SR * d )
	x = np.zeros( n )
	for _ in range( r.integers( 25, 45 ) ):
		o = int( r.uniform( 0, 0.7 ) * n )
		k = int( SR * r.uniform( 0.002, 0.012 ) )
		x[o:o + k] += r.normal( 0, 1, min( k, n - o ) ) * r.uniform( 0.4, 1.0 ) * np.exp( -np.arange( min( k, n - o ) ) / ( k / 3 ) )
	buzz = np.sign( np.sin( 2 * np.pi * 120 * np.arange( n ) / SR ) ) * 0.12 * np.exp( -np.arange( n ) / ( SR * 0.15 ) )
	x = band( x, 1200, 9000 ) * 1.4 + band( buzz, 100, 3000 )
	return reverb( x * env( n, 0.001, 0.05 ), 0.2, 0.25 )


def steam( seed ):
	"""A pressurised hiss that bursts out and tails off."""
	r = np.random.default_rng( seed )
	d = r.uniform( 1.6, 2.4 )
	n = int( SR * d )
	t = np.arange( n ) / SR
	shape = np.minimum( t / 0.05, 1 ) * np.exp( -t / ( d * 0.45 ) )
	hiss = band( noise( d ), 1800, 9000 ) * shape + band( noise( d ), 300, 1500 ) * shape * 0.4
	return reverb( hiss, 0.3, 0.3 )


def astromech( seed ):
	"""A burst of bleeps and warbles: a few rising and falling chirps, a trill, sometimes a low 'boop'."""
	r = np.random.default_rng( seed )
	parts = []
	for _ in range( r.integers( 3, 7 ) ):
		kind = r.integers( 0, 4 )
		if kind == 0:
			parts.append( chirp( r.uniform( 900, 1600 ), r.uniform( 1700, 3200 ), r.uniform( 0.05, 0.12 ) ) * 0.8 )
		elif kind == 1:
			parts.append( chirp( r.uniform( 2000, 3000 ), r.uniform( 700, 1200 ), r.uniform( 0.06, 0.14 ) ) * 0.8 )
		elif kind == 2:
			parts.append( chirp( r.uniform( 1400, 2200 ), r.uniform( 1400, 2200 ), r.uniform( 0.12, 0.25 ), 0.12, r.uniform( 22, 40 ) ) * 0.7 )
		else:
			parts.append( chirp( r.uniform( 400, 600 ), r.uniform( 300, 450 ), r.uniform( 0.08, 0.14 ) ) * 0.9 )
	return reverb( sequence( parts, r.uniform( 0.01, 0.05 ) ), 0.25, 0.3 )


def mouse( seed ):
	"""Two to four quick high chirps."""
	r = np.random.default_rng( seed )
	parts = [chirp( r.uniform( 2600, 3600 ), r.uniform( 3000, 4400 ), r.uniform( 0.025, 0.05 ) ) * 0.7 for _ in range( r.integers( 2, 5 ) )]
	return sequence( parts, 0.03 )


def pod_hiss():
	"""The bacta pod's glass sliding: a pneumatic hiss over a low servo hum and a soft thunk at the end."""
	d = 1.3
	n = int( SR * d )
	t = np.arange( n ) / SR
	hiss = band( noise( d ), 2000, 9000 ) * np.minimum( t / 0.04, 1 ) * np.exp( -t / 0.5 ) * 0.8
	servo = np.sin( 2 * np.pi * np.cumsum( 90 + 50 * t / d ) / SR ) * 0.25 * env( n, 0.1, 0.2 )
	thunk = band( noise( d ), 60, 400 ) * np.exp( -np.maximum( t - 1.1, 0 ) / 0.06 ) * ( t > 1.1 ) * 0.9
	return reverb( hiss + servo + thunk, 0.3, 0.3 )


def pod_fill():
	"""Bacta flooding in: a rising, bubbling gurgle."""
	d = 2.2
	n = int( SR * d )
	t = np.arange( n ) / SR
	rush = band( noise( d ), 200, 1600 ) * np.minimum( t / 0.3, 1 ) * ( 0.6 + 0.4 * t / d )
	glug = np.zeros( n )
	r = np.random.default_rng( 77 )
	for _ in range( 40 ):
		o = int( r.uniform( 0, d - 0.1 ) * SR )
		k = int( SR * r.uniform( 0.03, 0.07 ) )
		f0 = r.uniform( 250, 600 ) * ( 1 + 0.6 * o / n )
		tt = np.arange( k ) / SR
		glug[o:o + k] += np.sin( 2 * np.pi * ( f0 * tt + 900 * tt * tt ) ) * np.exp( -tt / 0.02 ) * r.uniform( 0.3, 0.8 )
	return reverb( rush * 0.5 + glug, 0.4, 0.35 ) * env( n, 0.05, 0.3 )


def pod_bubbles():
	"""Inside the full tank: a soft bubbling hum, a seamless 4 s loop."""
	d = 4.0
	n = int( SR * d )
	t = np.arange( n ) / SR
	hum = ( np.sin( 2 * np.pi * 60 * t ) + 0.4 * np.sin( 2 * np.pi * 120 * t ) ) * 0.12
	bub = np.zeros( n )
	r = np.random.default_rng( 91 )
	for _ in range( 36 ):
		o = int( r.uniform( 0, d ) * SR )
		k = int( SR * r.uniform( 0.02, 0.05 ) )
		f0 = r.uniform( 500, 1400 )
		tt = np.arange( k ) / SR
		blip = np.sin( 2 * np.pi * ( f0 * tt + 2500 * tt * tt ) ) * np.exp( -tt / 0.012 ) * r.uniform( 0.2, 0.6 )
		end = min( n, o + k )
		bub[o:end] += blip[:end - o]
		if o + k > n:
			bub[:o + k - n] += blip[end - o:]
	wash = band( noise( d ), 150, 900 ) * 0.25
	return reverb( hum + bub + wash, 0.3, 0.3, loop=True )


def pod_drain():
	"""The bacta draining away: a falling gurgle and a sucking swirl."""
	x = pod_fill()[::-1].copy()
	n = len( x )
	return x * env( n, 0.05, 0.4 )


if __name__ == "__main__":
	A.save( "pod_hiss", pod_hiss(), 0.8, files=[] )
	A.event( "pod_hiss", ["pod_hiss"], 0.45, 900, 0.04, 0.05 )
	A.save( "pod_fill", pod_fill(), 0.75, files=[] )
	A.event( "pod_fill", ["pod_fill"], 0.45, 900, 0.03, 0.05 )
	A.save( "pod_bubbles", pod_bubbles(), 0.6, loop=True, files=[] )
	A.event( "pod_bubbles", ["pod_bubbles"], 0.3, 700, 0.0, 0.0 )
	A.save( "pod_drain", pod_drain(), 0.75, files=[] )
	A.event( "pod_drain", ["pod_drain"], 0.45, 900, 0.03, 0.05 )
	for i in range( 3 ):
		A.save( f"spark_{i}", spark( 100 + i ), 0.85, files=[] )
	A.event( "spark", [f"spark_{i}" for i in range( 3 )], 0.5, 1200 )
	for i in range( 2 ):
		A.save( f"steam_{i}", steam( 200 + i ), 0.7, files=[] )
	A.event( "steam", [f"steam_{i}" for i in range( 2 )], 0.4, 1100 )
	for i in range( 5 ):
		A.save( f"astromech_{i}", astromech( 300 + i ), 0.7, files=[] )
	A.event( "astromech", [f"astromech_{i}" for i in range( 5 )], 0.4, 1000, 0.05, 0.15 )
	for i in range( 3 ):
		A.save( f"mouse_{i}", mouse( 400 + i ), 0.6, files=[] )
	A.event( "mouse_droid", [f"mouse_{i}" for i in range( 3 )], 0.3, 700, 0.08, 0.2 )
