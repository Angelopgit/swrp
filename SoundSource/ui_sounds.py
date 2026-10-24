"""
Original synthesized sci-fi UI and weapon handling sounds for SWRP. Pure numpy + wave, no samples, no ripped audio.

    blender -b --factory-startup -P SoundSource/ui_sounds.py   (Blender ships numpy; plain Python may not have it)

Writes 16-bit mono 44.1 kHz WAVs into Assets/sounds/ui/. Tweak the recipes below and re-run; the .sound events in
Assets/sounds/ui/ reference these files, so volume/pitch can also be changed there without re-synthesizing.
"""
import os
import wave
import numpy as np

SR = 44100
OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "ui" )
os.makedirs( OUT, exist_ok=True )
rng = np.random.default_rng( 501 )


def t_axis( dur ):
	return np.arange( int( SR * dur ) ) / SR


def env( n, attack=0.004, decay=0.0, release=0.02, curve=3.0 ):
	"""Attack, optional exponential decay, release."""
	e = np.ones( n, dtype=np.float32 )
	a = max( 1, int( SR * attack ) )
	r = max( 1, int( SR * release ) )
	e[:a] = np.linspace( 0, 1, a )
	e[-r:] *= np.linspace( 1, 0, r )
	if decay > 0:
		e *= np.exp( -np.arange( n ) / ( SR * decay ) * curve / 3 )
	return e


def sweep( f0, f1, dur ):
	"""Sine with an exponential frequency glide from f0 to f1."""
	t = t_axis( dur )
	k = t / dur
	f = f0 * ( f1 / f0 ) ** k
	phase = 2 * np.pi * np.cumsum( f ) / SR
	return np.sin( phase ).astype( np.float32 )


def tone( f, dur, harmonics=( 1.0, ) ):
	t = t_axis( dur )
	out = np.zeros_like( t, dtype=np.float32 )
	for i, a in enumerate( harmonics ):
		out += a * np.sin( 2 * np.pi * f * ( i + 1 ) * t )
	return out / max( 1e-6, sum( harmonics ) )


def noise( dur ):
	return rng.normal( 0, 1, int( SR * dur ) ).astype( np.float32 )


def lowpass( x, cutoff ):
	rc = 1 / ( 2 * np.pi * cutoff )
	a = ( 1 / SR ) / ( rc + 1 / SR )
	y = np.zeros_like( x )
	acc = 0.0
	for i in range( len( x ) ):
		acc += a * ( x[i] - acc )
		y[i] = acc
	return y


def highpass( x, cutoff ):
	return x - lowpass( x, cutoff )


def bandpass( x, lo, hi ):
	return lowpass( highpass( x, lo ), hi )


def echo( x, delays=( 0.035, 0.07 ), gains=( 0.28, 0.14 ), tail=0.12 ):
	"""A little metallic ring, like a small hard-surfaced room."""
	out = np.zeros( len( x ) + int( SR * tail ), dtype=np.float32 )
	out[:len( x )] += x
	for d, g in zip( delays, gains ):
		o = int( SR * d )
		out[o:o + len( x )] += x * g
	return out


def mix( *parts ):
	n = max( len( p ) for p in parts )
	out = np.zeros( n, dtype=np.float32 )
	for p in parts:
		out[:len( p )] += p
	return out


def at( x, start, total ):
	"""Place x at a time offset inside a buffer of `total` seconds."""
	out = np.zeros( int( SR * total ), dtype=np.float32 )
	o = int( SR * start )
	chunk = x[:max( 0, len( out ) - o )]
	out[o:o + len( chunk )] += chunk
	return out


LOOPS = set()          # recipes made to loop seamlessly: saved as they are (no trimming, no fade at the end)


def save( name, x, peak=0.8 ):
	x = x / max( 1e-6, np.max( np.abs( x ) ) ) * peak
	if name not in LOOPS:
		loud = np.nonzero( np.abs( x ) > 0.004 )[0]          # trim the silent tail left by the layering buffers
		if len( loud ): x = x[:min( len( x ), loud[-1] + int( SR * 0.03 ) )]
		x[-int( SR * 0.02 ):] *= np.linspace( 1, 0, len( x[-int( SR * 0.02 ):] ) )
	pcm = ( np.clip( x, -1, 1 ) * 32767 ).astype( np.int16 )
	with wave.open( os.path.join( OUT, name + ".wav" ), "wb" ) as w:
		w.setnchannels( 1 )
		w.setsampwidth( 2 )
		w.setframerate( SR )
		w.writeframes( pcm.tobytes() )
	print( f"{name}.wav  {len( x ) / SR * 1000:.0f} ms" )


def blip( f, dur, vol=1.0, glide=None ):
	n = int( SR * dur )
	s = sweep( f, glide, dur ) if glide else tone( f, dur, ( 1.0, 0.25, 0.1 ) )
	return s * env( n, 0.002, dur * 0.35, 0.01 ) * vol


# ---------------------------------------------------------------------------------------------------------------------
# Menu sounds
# ---------------------------------------------------------------------------------------------------------------------

def ui_hover():
	a = blip( 2100, 0.05, 0.6, glide=2500 )
	b = blip( 3150, 0.035, 0.18 )
	return echo( mix( a, b ), ( 0.02, ), ( 0.18, ), 0.05 )


def ui_click():
	tick = highpass( noise( 0.012 ), 2500 ) * env( int( SR * 0.012 ), 0.0005, 0.004, 0.004 ) * 0.5
	lo = blip( 880, 0.07, 0.8, glide=520 )
	hi = blip( 1760, 0.045, 0.45 )
	return echo( mix( tick, lo, hi ), ( 0.025, 0.05 ), ( 0.2, 0.1 ), 0.08 )


def ui_confirm():
	notes = [660, 990, 1320]
	parts = []
	for i, f in enumerate( notes ):
		parts.append( at( mix( blip( f, 0.13, 0.7 ), blip( f * 2, 0.1, 0.18 ) ), i * 0.075, 0.5 ) )
	shimmer = sweep( 1800, 3600, 0.22 ) * env( int( SR * 0.22 ), 0.05, 0.1, 0.1 ) * 0.14
	return echo( mix( *parts, at( shimmer, 0.12, 0.5 ) ), ( 0.04, 0.09 ), ( 0.3, 0.16 ), 0.25 )


def ui_deny():
	n = int( SR * 0.2 )
	s = sweep( 240, 150, 0.2 )
	buzz = lowpass( np.sign( s ) * 0.35 + s * 0.5, 1800 ) * env( n, 0.003, 0.12, 0.05 )
	tick = blip( 440, 0.06, 0.5, glide=330 )
	return echo( mix( buzz, tick, at( tick * 0.8, 0.09, 0.2 ) ), ( 0.03, ), ( 0.2, ), 0.06 )


def ui_open():
	"""Inventory / menu open: a rising power-up sweep with a soft air whoosh and a bright ping at the top."""
	dur = 0.34
	n = int( SR * dur )
	up = sweep( 300, 1500, dur ) * env( n, 0.02, dur * 0.7, 0.08 ) * 0.55
	whoosh = bandpass( noise( dur ), 600, 5000 ) * env( n, 0.08, dur * 0.5, 0.1 ) * 0.28
	ping = at( mix( blip( 2000, 0.16, 0.5 ), blip( 3000, 0.12, 0.2 ) ), 0.2, 0.55 )
	hum = tone( 110, dur, ( 1.0, 0.5 ) ) * env( n, 0.05, 0.2, 0.1 ) * 0.25
	return echo( mix( up, whoosh, ping, hum ), ( 0.04, 0.08 ), ( 0.24, 0.12 ), 0.2 )


def ui_close():
	dur = 0.27
	n = int( SR * dur )
	down = sweep( 1400, 260, dur ) * env( n, 0.01, dur * 0.6, 0.07 ) * 0.5
	whoosh = bandpass( noise( dur ), 500, 3500 ) * env( n, 0.02, dur * 0.4, 0.1 ) * 0.2
	thud = blip( 140, 0.09, 0.5 ) * 0.7
	return echo( mix( down, whoosh, at( thud, 0.16, 0.45 ) ), ( 0.035, ), ( 0.2, ), 0.12 )


def ui_chat_open():
	return echo( mix( blip( 1400, 0.045, 0.6 ), at( blip( 1900, 0.05, 0.6 ), 0.045, 0.2 ) ), ( 0.03, ), ( 0.16, ), 0.06 )


def ui_chat_msg():
	return echo( mix( blip( 1320, 0.09, 0.5 ), blip( 2640, 0.07, 0.15 ) ), ( 0.04, ), ( 0.22, ), 0.1 )


def ui_use_item():
	"""Medical / ration use: a soft rising chirp with a hiss, like an injector."""
	hiss = bandpass( noise( 0.18 ), 3000, 9000 ) * env( int( SR * 0.18 ), 0.01, 0.1, 0.06 ) * 0.35
	chirp = sweep( 500, 1300, 0.14 ) * env( int( SR * 0.14 ), 0.01, 0.08, 0.04 ) * 0.5
	return echo( mix( hiss, chirp, at( blip( 1950, 0.1, 0.4 ), 0.12, 0.3 ) ), ( 0.03, ), ( 0.18, ), 0.1 )


# ---------------------------------------------------------------------------------------------------------------------
# Weapon handling
# ---------------------------------------------------------------------------------------------------------------------

def servo( f0, f1, dur ):
	"""Small motor whine."""
	n = int( SR * dur )
	whine = sweep( f0, f1, dur ) * 0.5 + sweep( f0 * 2, f1 * 2, dur ) * 0.18
	buzz = lowpass( np.sign( sweep( f0 * 0.5, f1 * 0.5, dur ) ), 900 ) * 0.2
	return ( whine + buzz ) * env( n, 0.02, dur, 0.04, curve=1.5 )


def metal_clack( vol=1.0 ):
	n = int( SR * 0.09 )
	body = bandpass( noise( 0.09 ), 1800, 7000 ) * env( n, 0.0005, 0.018, 0.02 )
	ring = tone( 2400, 0.09, ( 1.0, 0.4 ) ) * env( n, 0.0005, 0.03, 0.02 ) * 0.5
	thunk = blip( 110, 0.08, 0.9, glide=70 )
	return mix( body, ring, thunk ) * vol


def weapon_draw():
	"""Unholster: a slide out of the holster, a servo spin-up, a locking clack and a soft power-on rise."""
	slide = bandpass( noise( 0.14 ), 500, 3500 ) * env( int( SR * 0.14 ), 0.03, 0.08, 0.05 ) * 0.4
	spin = at( servo( 380, 1250, 0.2 ), 0.06, 0.6 )
	clack = at( metal_clack( 1.0 ), 0.27, 0.6 )
	power = at( sweep( 700, 2200, 0.16 ) * env( int( SR * 0.16 ), 0.02, 0.1, 0.06 ) * 0.28, 0.3, 0.6 )
	return echo( mix( slide, spin, clack, power ), ( 0.03, 0.065 ), ( 0.2, 0.1 ), 0.15 )


def weapon_holster():
	"""Holster: a power-down fall, a servo wind-down, and a firm click into the holster."""
	power = sweep( 1900, 600, 0.14 ) * env( int( SR * 0.14 ), 0.01, 0.08, 0.05 ) * 0.3
	spin = at( servo( 1100, 330, 0.18 ), 0.04, 0.55 )
	slide = at( bandpass( noise( 0.12 ), 400, 3000 ) * env( int( SR * 0.12 ), 0.02, 0.07, 0.05 ) * 0.35, 0.12, 0.55 )
	clack = at( metal_clack( 0.9 ), 0.25, 0.55 )
	return echo( mix( power, spin, slide, clack ), ( 0.03, 0.06 ), ( 0.2, 0.1 ), 0.12 )


# ---------------------------------------------------------------------------------------------------------------------
# Doors
# ---------------------------------------------------------------------------------------------------------------------

def pneumatic( dur, vol=1.0 ):
	"""Air hiss, bright at the start and dying away."""
	n = int( SR * dur )
	return bandpass( noise( dur ), 1500, 9000 ) * env( n, 0.01, dur * 0.6, dur * 0.3, curve=2.0 ) * vol


def door_open():
	"""Security door opening: a latch release, an air burst and a servo run as the panel slides up."""
	latch = metal_clack( 0.7 )
	hiss = at( pneumatic( 0.35, 0.45 ), 0.03, 1.0 )
	run = at( servo( 160, 420, 0.55 ) * 0.9, 0.06, 1.0 )
	return echo( mix( latch, hiss, run ), ( 0.04, 0.09 ), ( 0.25, 0.12 ), 0.2 )


def door_close():
	"""Closing: the servo runs down, then a heavy seal thunk and a short hiss."""
	run = servo( 420, 150, 0.5 ) * 0.9
	thunk = at( blip( 80, 0.16, 1.0, glide=45 ), 0.48, 0.9 )
	clack = at( metal_clack( 0.8 ), 0.49, 0.9 )
	hiss = at( pneumatic( 0.22, 0.3 ), 0.5, 0.9 )
	return echo( mix( run, thunk, clack, hiss ), ( 0.04, 0.09 ), ( 0.25, 0.12 ), 0.2 )


def door_lock():
	"""Bolts driving home and a low confirmation tone."""
	bolts = mix( metal_clack( 0.8 ), at( metal_clack( 0.7 ), 0.07, 0.4 ) )
	tone_ = at( blip( 330, 0.16, 0.5, glide=250 ), 0.12, 0.4 )
	return mix( bolts, tone_ )


def door_unlock():
	"""Bolts retracting and a rising tone."""
	bolts = mix( metal_clack( 0.7 ), at( metal_clack( 0.6 ), 0.06, 0.4 ) )
	tone_ = at( blip( 280, 0.14, 0.5, glide=520 ), 0.1, 0.4 )
	return mix( bolts, tone_ )


def hangar_alarm():
	"""Hangar bay announcement: three falling two-tone klaxon pulses and a soft chime, original synthesis."""
	pulses = []
	for i in range( 3 ):
		hi = tone( 620, 0.32, ( 1.0, 0.5, 0.25 ) ) * env( int( SR * 0.32 ), 0.02, 0.2, 0.08 )
		lo = tone( 465, 0.32, ( 1.0, 0.5, 0.25 ) ) * env( int( SR * 0.32 ), 0.02, 0.2, 0.08 )
		pulses.append( at( lowpass( mix( hi, at( lo, 0.33, 0.66 ) ), 3500 ) * 0.55, i * 0.75, 2.8 ) )
	chime = at( mix( blip( 880, 0.5, 0.35 ), at( blip( 1320, 0.5, 0.25 ), 0.12, 0.7 ) ), 2.25, 2.8 )
	return echo( mix( *pulses, chime ), ( 0.09, 0.19 ), ( 0.3, 0.15 ), 0.5 )


# ---------------------------------------------------------------------------------------------------------------------
# Combat
# ---------------------------------------------------------------------------------------------------------------------

def blaster_shot( f0=2600, f1=180, dur=0.32, body=1.0, crack=1.0 ):
	"""The classic sci-fi zap built from scratch: a fast falling sweep with a metallic ring, an electric crack and a low thump."""
	n = int( SR * dur )
	zap = sweep( f0, f1, dur ) * env( n, 0.001, dur * 0.25, dur * 0.4, curve=2.5 )
	ring = sweep( f0 * 1.51, f1 * 1.51, dur ) * env( n, 0.001, dur * 0.15, dur * 0.3, curve=3.0 ) * 0.35
	crk = highpass( noise( 0.05 ), 2500 ) * env( int( SR * 0.05 ), 0.0005, 0.012, 0.02 ) * 0.6 * crack
	thump = blip( 95, 0.12, 0.8 * body, glide=55 )
	return echo( mix( zap, ring, crk, thump ), ( 0.05, 0.11, 0.2 ), ( 0.22, 0.12, 0.06 ), 0.3 )


def wire_chirp( f0, f1, dur, tau, partials=( ( 1.0, 1.0, 1.0 ), ( 1.47, 0.45, 0.7 ), ( 2.12, 0.25, 0.5 ), ( 2.93, 0.12, 0.35 ) ), wobble=0.012 ):
	"""
	The falling 'pew' of a struck wire or spring, built from scratch: each partial's pitch drops along a hyperbolic curve
	(fast at first, then settling), the way a dispersive wire smears a strike into a chirp, with slightly inharmonic
	partials for the metallic ring and a little vibrato. partials: (frequency ratio, level, decay scale).
	"""
	t = t_axis( dur )
	n = len( t )
	out = np.zeros( n, dtype=np.float32 )
	vib = 1.0 + wobble * np.sin( 2 * np.pi * 27 * t )
	for ratio, level, decay in partials:
		f = ( f1 + ( f0 - f1 ) / ( 1.0 + t / tau ) ** 2 ) * ratio * vib
		phase = 2 * np.pi * np.cumsum( f ) / SR
		shape = np.minimum( t / 0.0015, 1 ) * np.exp( -t / ( dur * 0.32 * decay ) )
		out += ( np.sin( phase ) * shape * level ).astype( np.float32 )
	return out


# ----- Battlefront II-style blaster voices (synthesized; nothing sampled) -----------------------------------------------
# What the BF2 clone blasters have that our older zaps didn't: a hard transient with real mid-range body (a buzzy,
# harmonic "tyew" from ~1.5 kHz down, not just a pure whistle), a short low punch under it, an electric crackle, and a
# long space tail. Each gun gets its own pitch, length and grit so they read apart in a firefight.

def buzz_sweep( f0, f1, dur, tau, bright=0.6 ):
	"""A falling, harmonic-rich tone (a soft-clipped saw): the gritty middle of a blaster shot."""
	t = t_axis( dur )
	f = f1 + ( f0 - f1 ) / ( 1.0 + t / tau ) ** 2
	phase = np.cumsum( f ) / SR
	saw = 2.0 * ( phase - np.floor( phase + 0.5 ) )
	x = np.tanh( saw * ( 1.0 + 3.0 * bright ) ).astype( np.float32 )
	return lowpass( x, 7000 ) * np.minimum( t / 0.001, 1 ) * np.exp( -t / ( dur * 0.3 ) )


def space_tail( dry, length=0.7, level=0.18, tone=2600 ):
	"""A diffuse tail like a shot ringing down a hangar: dense decaying taps, darkened."""
	n = len( dry ) + int( SR * length )
	out = np.zeros( n, dtype=np.float32 )
	out[:len( dry )] += dry
	taps = rng.uniform( 0.02, length * 0.8, 28 )
	for d in taps:
		i = int( d * SR )
		g = level * np.exp( -d / ( length * 0.35 ) ) * rng.uniform( 0.5, 1.0 )
		out[i:i + len( dry )] += dry[:max( 0, min( len( dry ), n - i ) )] * g
	return lowpass( out, tone ) * 0.5 + out * 0.5


def bf_blaster( f0, f1, dur, tau, buzz=0.5, chirp=0.8, punch=0.7, crackle=0.4, tail=0.18, tail_len=0.7, bright=0.6, zing=0.25 ):
	n = int( SR * dur )
	main = wire_chirp( f0, f1, dur, tau ) * chirp
	mid = buzz_sweep( f0 * 0.5, f1 * 0.9, dur * 0.8, tau * 1.3, bright ) * buzz
	hi = wire_chirp( f0 * 2.3, f0 * 0.7, 0.07, 0.01, ( ( 1.0, 1.0, 1.0 ), ( 1.33, 0.4, 0.8 ) ), 0.0 ) * zing
	cn = int( SR * 0.03 )
	crk = bandpass( noise( 0.03 ), 1800, 9000 ) * env( cn, 0.0002, 0.004, 0.012 ) * ( 0.6 + 0.4 * np.sin( 2 * np.pi * 90 * np.arange( cn ) / SR ) ) * crackle
	body = sweep( 160, 55, 0.11 ) * env( int( SR * 0.11 ), 0.0008, 0.03, 0.04 ) * punch
	dry = mix( main, at( mid, 0.0, dur ), at( hi, 0.0, dur ), at( crk, 0.0, dur ), at( body, 0.001, dur ) )
	dry = np.tanh( dry * 1.6 ).astype( np.float32 ) * env( len( dry ), 0.0004, 0.0, 0.05 )
	wet = space_tail( dry, tail_len, tail )
	return wet * env( len( wet ), 0.0004, 0.0, 0.12 )


def sweep_lowpass( x, c0, c1, tau ):
	"""One-pole lowpass whose cutoff falls from c0 towards c1 (time constant tau): a crack that darkens as it dies."""
	n = len( x )
	c = c1 + ( c0 - c1 ) * np.exp( -np.arange( n ) / ( SR * tau ) )
	a = ( 2 * np.pi * c / SR ) / ( 1 + 2 * np.pi * c / SR )
	y = np.zeros_like( x )
	acc = 0.0
	for i in range( n ):
		acc += a[i] * ( x[i] - acc )
		y[i] = acc
	return y


def blaster_dc15a():
	"""
	The DC-15A, matched by measurement to the reference Angelo picked (2026-10-12): a 1.2 s shot (loud and level for 0.6 s, then dying away) that is mostly a loud
	broadband crack (up to ~15 kHz, darkening as it dies), a falling "pew" sweeping ~2.5 kHz to ~500 Hz in the first 0.2 s
	and on down to ~90 Hz, and a deep 45-70 Hz boom under the whole thing (the strongest band). Synthesized; nothing sampled.
	"""
	dur = 1.35
	t = t_axis( dur )
	n = len( t )
	hold = np.where( t < 0.6, 1.0, np.exp( -( t - 0.6 ) / 0.25 ) ).astype( np.float32 )     # loud for 0.6 s, then ~35 dB/s
	crack = sweep_lowpass( noise( dur ), 8000, 900, 0.22 )
	crack = crack * np.minimum( t / 0.002, 1 ) * hold
	crack = crack + bandpass( noise( dur ), 120, 600 ) * hold * 1.6
	pew = wire_chirp( 3800, 90, 0.75, 0.11, ( ( 1.0, 1.0, 1.0 ), ( 2.0, 0.35, 0.6 ), ( 3.01, 0.15, 0.4 ) ), 0.006 )
	f = 70 - 25 * np.minimum( t / 0.6, 1 )
	boom = np.sin( 2 * np.pi * np.cumsum( f ) / SR ).astype( np.float32 ) * np.minimum( t / 0.004, 1 ) * hold
	x = mix( crack * 1.0, at( pew, 0.0, dur ) * 0.6, boom * 0.55 )
	x = np.tanh( x * 1.8 ).astype( np.float32 )
	x = lowpass( lowpass( x, 9000 ), 9000 )        # the reference has almost nothing above 10 kHz
	return x * env( n, 0.0005, 0.0, 0.15 )
def blaster_dc15s(): return bf_blaster( 3100, 260, 0.28, 0.04, buzz=0.45, punch=0.6, crackle=0.5, tail=0.15 )         # carbine: snappier, lighter
def blaster_dc17(): return bf_blaster( 3600, 380, 0.22, 0.03, buzz=0.35, punch=0.45, crackle=0.55, tail=0.12, zing=0.35 )  # pistol: a high, quick pip
def blaster_z6(): return bf_blaster( 2200, 170, 0.24, 0.045, buzz=0.75, punch=0.9, crackle=0.6, tail=0.14, tail_len=0.5, bright=0.8 )  # rotary: chunky, each round short
def blaster_dc15x(): return bf_blaster( 2400, 120, 0.6, 0.07, buzz=0.6, punch=1.2, crackle=0.7, tail=0.3, tail_len=1.2, zing=0.4 )  # sniper: heavy crack, long tail
def blaster_e5(): return bf_blaster( 1900, 240, 0.3, 0.04, buzz=0.9, chirp=0.5, punch=0.5, crackle=0.8, tail=0.15, bright=1.0, zing=0.1 )  # droid rifle: buzzier, harsher
def blaster_rifle(): return blaster_dc15a()
def blaster_carbine(): return blaster_shot( 2900, 220, 0.26, 0.8 )
def blaster_pistol(): return blaster_shot( 3300, 300, 0.22, 0.6, 0.8 )
def blaster_repeater(): return blaster_shot( 2000, 140, 0.24, 1.2, 1.2 )


def blaster_stun():
	"""Stun round: a wobbling ring-shaped tone instead of a zap."""
	n = int( SR * 0.4 )
	t = np.arange( n ) / SR
	wob = np.sin( 2 * np.pi * ( 900 * t + 60 * np.sin( 2 * np.pi * 18 * t ) ) )
	return echo( wob * env( n, 0.004, 0.15, 0.2 ) * 0.7 + sweep( 1800, 600, 0.4 ) * env( n, 0.002, 0.08, 0.2 ) * 0.3, ( 0.06, 0.12 ), ( 0.2, 0.1 ), 0.2 )


def blaster_reload():
	"""Spent cell ejects, new cell clicks in, capacitor whine rises."""
	eject = metal_clack( 0.7 )
	hiss = at( pneumatic( 0.15, 0.25 ), 0.02, 1.1 )
	insert = at( metal_clack( 1.0 ), 0.55, 1.1 )
	charge = at( sweep( 400, 2400, 0.45 ) * env( int( SR * 0.45 ), 0.05, 0.3, 0.1 ) * 0.25, 0.62, 1.1 )
	return mix( eject, hiss, insert, charge )


def reload_eject():
	"""Reload, step one: the cell latch clicks, the spent power cell pops out venting its last charge, and a small
	electric fizzle."""
	latch = metal_clack( 0.6 )
	pop = at( blip( 520, 0.05, 0.6, glide=300 ), 0.04, 0.6 )
	vent = at( pneumatic( 0.28, 0.45 ), 0.05, 0.6 )
	fizz = at( bandpass( noise( 0.18 ), 3000, 9000 ) * env( int( SR * 0.18 ), 0.005, 0.05, 0.1 ) * 0.25, 0.08, 0.6 )
	clink = at( metal_clack( 0.35 ), 0.42, 0.6 )
	return mix( latch, pop, vent, fizz, clink )


def reload_insert():
	"""Reload, step two: a fresh cell slides home and locks with a solid clack."""
	slide = bandpass( noise( 0.14 ), 900, 4000 ) * env( int( SR * 0.14 ), 0.01, 0.08, 0.04 ) * 0.35
	lock = at( metal_clack( 1.0 ), 0.12, 0.35 )
	tick = at( blip( 2600, 0.02, 0.35 ), 0.2, 0.35 )
	return mix( slide, lock, tick )


def reload_charge():
	"""Reload, step three: the capacitor charges with a rising whine and a ready chirp."""
	dur = 0.55
	n = int( SR * dur )
	whine = ( sweep( 350, 2600, dur ) * 0.6 + sweep( 700, 5200, dur ) * 0.15 ) * env( n, 0.04, 0.0, 0.08 ) * 0.4
	hum = sweep( 90, 160, dur ) * env( n, 0.05, 0.0, 0.1 ) * 0.3
	ready = at( mix( blip( 1980, 0.045, 0.5 ), at( blip( 2640, 0.06, 0.5 ), 0.05, 0.15 ) ), dur - 0.02, dur + 0.15 )
	return mix( at( whine, 0.0, dur + 0.15 ), at( hum, 0.0, dur + 0.15 ), ready )


def blaster_empty():
	"""Dry trigger: a small click and a dead buzz."""
	click = metal_clack( 0.5 )
	buzz = at( lowpass( np.sign( tone( 70, 0.12 ) ), 600 ) * env( int( SR * 0.12 ), 0.005, 0.05, 0.05 ) * 0.3, 0.02, 0.2 )
	return mix( click, buzz )


def blaster_impact():
	"""Bolt hitting a wall: a sizzling burst and a short crack."""
	sizzle = bandpass( noise( 0.3 ), 2000, 9000 ) * env( int( SR * 0.3 ), 0.001, 0.08, 0.2, curve=3.0 ) * 0.6
	pop = blip( 180, 0.08, 0.7, glide=80 )
	return mix( sizzle, pop )


def hit_marker():
	"""Shooter confirmation: a short bright tick."""
	return mix( blip( 2400, 0.05, 0.8 ), at( blip( 3600, 0.04, 0.4 ), 0.015, 0.08 ) )


def hit_marker_kill():
	"""Kill confirmation: two descending ticks."""
	return mix( blip( 2600, 0.06, 0.8 ), at( blip( 1700, 0.08, 0.8 ), 0.07, 0.2 ) )


def hurt():
	"""Taking a hit: a dull armour thud with a muffled sting."""
	thud = blip( 70, 0.18, 1.0, glide=40 )
	sting = at( lowpass( noise( 0.15 ), 1500 ) * env( int( SR * 0.15 ), 0.002, 0.05, 0.08 ) * 0.5, 0.0, 0.3 )
	return mix( thud, sting )


# ---------------------------------------------------------------------------------------------------------------------
# Comms and orders
# ---------------------------------------------------------------------------------------------------------------------

def radio_static():
	"""Helmet radio: a squelch click, a short band-limited static burst and a closing chirp."""
	click = blip( 1800, 0.02, 0.6 )
	hiss = at( bandpass( noise( 0.22 ), 900, 3400 ) * env( int( SR * 0.22 ), 0.005, 0.12, 0.08 ) * 0.5, 0.015, 0.4 )
	chirp = at( blip( 1250, 0.05, 0.4, glide=900 ), 0.24, 0.4 )
	return mix( click, hiss, chirp )


def dispatch():
	"""Officer dispatch: static, then a firm two-tone priority chime."""
	st = radio_static() * 0.6
	tones = at( mix( blip( 740, 0.16, 0.7 ), at( blip( 988, 0.22, 0.7 ), 0.15, 0.4 ) ), 0.2, 0.7 )
	return echo( mix( st, tones ), ( 0.07, 0.14 ), ( 0.2, 0.1 ), 0.2 )


def order_posted():
	"""New order on the board: three rising tones."""
	return echo( mix( blip( 523, 0.14, 0.6 ), at( blip( 659, 0.14, 0.6 ), 0.12, 0.6 ), at( blip( 784, 0.24, 0.7 ), 0.24, 0.6 ) ), ( 0.08, 0.16 ), ( 0.2, 0.1 ), 0.25 )


def squad_join():
	"""Squad roster changed: a soft double blip."""
	return mix( blip( 880, 0.07, 0.6 ), at( blip( 1175, 0.09, 0.5 ), 0.07, 0.2 ) )


# ---------------------------------------------------------------------------------------------------------------------
# Medical
# ---------------------------------------------------------------------------------------------------------------------

def heartbeat():
	"""Lub-dub: two low, soft thumps."""
	lub = lowpass( blip( 62, 0.12, 1.0, glide=48 ), 300 )
	dub = at( lowpass( blip( 55, 0.1, 0.75, glide=42 ), 300 ), 0.17, 0.4 )
	return mix( lub, dub )


def bleed():
	"""A wet, stinging hiss when a wound opens."""
	hiss = bandpass( noise( 0.35 ), 600, 2500 ) * env( int( SR * 0.35 ), 0.01, 0.12, 0.2, curve=2.0 ) * 0.5
	sting = blip( 300, 0.14, 0.5, glide=180 )
	return mix( hiss, sting )


def revive_start():
	"""Med-scanner powering up: a rising whine with soft clicks."""
	whine = sweep( 400, 1600, 0.6 ) * env( int( SR * 0.6 ), 0.05, 0.4, 0.15 ) * 0.4
	clicks = mix( *[at( blip( 2200, 0.02, 0.4 ), t, 0.7 ) for t in ( 0.1, 0.25, 0.4 )] )
	return mix( whine, clicks )


def revive_done():
	"""Stim delivered: a pressure hiss and a bright confirmation chord."""
	hiss = pneumatic( 0.25, 0.4 )
	chord = at( mix( blip( 660, 0.3, 0.5 ), blip( 990, 0.3, 0.35 ), blip( 1320, 0.3, 0.25 ) ), 0.12, 0.5 )
	return echo( mix( hiss, chord ), ( 0.06, 0.12 ), ( 0.2, 0.1 ), 0.2 )


def bacta_station():
	"""Bacta pump: bubbling low hum."""
	n = int( SR * 1.2 )
	hum = tone( 110, 1.2, ( 1.0, 0.5, 0.25 ) ) * env( n, 0.15, 0.8, 0.25 ) * 0.4
	bubbles = mix( *[at( blip( float( f ), 0.05, 0.3, glide=float( f ) * 1.6 ), t, 1.2 ) for f, t in ( ( 420, 0.2 ), ( 520, 0.45 ), ( 380, 0.62 ), ( 610, 0.85 ) )] )
	return mix( hum, bubbles )


def lift_ride():
	"""Turbolift travel: a low motor hum that rises and settles, with air rushing past the car."""
	dur = 2.8
	n = int( SR * dur )
	t = t_axis( dur )
	f = 70 + 30 * np.sin( np.pi * t / dur )
	phase = 2 * np.pi * np.cumsum( f ) / SR
	hum = ( np.sin( phase ) + 0.45 * np.sin( 2 * phase ) + 0.2 * np.sin( 3 * phase ) ) * env( n, 0.5, 0.0, 0.8 ) * 0.45
	air = bandpass( noise( dur ), 300, 1400 ) * env( n, 0.7, 0.0, 0.9 ) * 0.25 * np.sin( np.pi * t / dur )
	return mix( hum, air )


def bell( f, dur, vol=1.0 ):
	n = int( SR * dur )
	return ( tone( f, dur, ( 1.0, ) ) + 0.35 * tone( f * 2.76, dur, ( 1.0, ) ) + 0.15 * tone( f * 5.4, dur, ( 1.0, ) ) ) * env( n, 0.004, dur * 0.5, 0.1 ) * vol


def lift_arrive():
	"""Car arrived: a soft two-tone chime."""
	return echo( mix( at( bell( 880, 0.7, 0.5 ), 0.0, 1.1 ), at( bell( 1318, 0.8, 0.45 ), 0.22, 1.1 ) ), ( 0.08, 0.16 ), ( 0.2, 0.1 ), 0.3 )


def teleport():
	"""Transit field: a rising shimmer over filtered air."""
	dur = 0.95
	n = int( SR * dur )
	t = t_axis( dur )
	shimmer = sweep( 500, 3200, dur ) * ( 0.6 + 0.4 * np.sin( 2 * np.pi * 28 * t ) ) * env( n, 0.08, 0.0, 0.35 ) * 0.35
	air = bandpass( noise( dur ), 1500, 6000 ) * env( n, 0.2, 0.0, 0.5 ) * 0.25
	return echo( mix( shimmer, air ), ( 0.05, 0.11 ), ( 0.25, 0.12 ), 0.25 )


def alert_klaxon():
	"""Battle stations: two rising whoops."""
	one = sweep( 380, 920, 0.55 ) * env( int( SR * 0.55 ), 0.02, 0.0, 0.08 )
	one = np.tanh( one * 2.2 ) * 0.5
	return echo( mix( at( one, 0.0, 1.4 ), at( one, 0.68, 1.4 ) ), ( 0.09, 0.19 ), ( 0.3, 0.15 ), 0.3 )


def alert_change():
	"""Alert level changed: three short descending tones."""
	return mix( *[at( blip( float( f ), 0.12, 0.6 ), t, 0.6 ) for f, t in ( ( 1180, 0.0 ), ( 980, 0.16 ), ( 780, 0.32 ) )] )


def ship_chime():
	"""Ship-wide announcement: an ascending three-bell chime."""
	return echo( mix( at( bell( 784, 0.9, 0.5 ), 0.0, 1.6 ), at( bell( 988, 0.9, 0.45 ), 0.24, 1.6 ), at( bell( 1175, 1.1, 0.45 ), 0.48, 1.6 ) ),
		( 0.1, 0.2 ), ( 0.22, 0.1 ), 0.35 )


def weapon_drop():
	"""A metal rifle hitting the deck: a hard clack, two short rattles as it settles, and a dull ringing body."""
	dur = 0.6
	n = int( SR * dur )
	ring = mix( tone( 1320, dur, ( 1.0, 0.5, 0.25 ) ), tone( 2170, dur, ( 1.0, 0.3 ) ) * 0.6 ) * env( n, 0.0005, 0.05, 0.45, curve=4.0 ) * 0.22
	hit = metal_clack( 1.0 )
	rattle = mix( at( metal_clack( 0.45 ), 0.11, dur ), at( metal_clack( 0.25 ), 0.19, dur ) )
	thud = blip( 70, 0.12, 0.8, glide=50 )
	return mix( at( hit, 0.0, dur ), rattle, ring, at( thud, 0.0, dur ) )


def weapon_pickup():
	"""Picking a rifle up: a grab rustle and a short latch click."""
	grab = bandpass( noise( 0.12 ), 600, 3000 ) * env( int( SR * 0.12 ), 0.01, 0.03, 0.06 ) * 0.4
	return mix( at( grab, 0.0, 0.3 ), at( metal_clack( 0.6 ), 0.08, 0.3 ) )


def shield_on():
	"""A brig ray shield powering up: a rising electric hum with a buzzing edge and a crackle as it locks on."""
	dur = 0.9
	n = int( SR * dur )
	t = t_axis( dur )
	f = 70 + 170 * ( t / dur ) ** 0.6
	ph = 2 * np.pi * np.cumsum( f ) / SR
	hum = ( np.sin( ph ) + 0.45 * np.sin( 2 * ph ) + 0.25 * np.sign( np.sin( 3 * ph ) ) * 0.4 ) * env( n, 0.08, 0.0, 0.25 )
	crackle = bandpass( noise( dur ), 2500, 7000 ) * np.exp( -np.maximum( t - 0.55, 0 ) / 0.05 ) * ( t > 0.55 ) * 0.5
	return mix( hum * 0.7, crackle )


def shield_off():
	"""A ray shield dropping: the hum winds down and fizzles out."""
	dur = 0.8
	n = int( SR * dur )
	t = t_axis( dur )
	f = 240 - 190 * ( t / dur ) ** 0.7
	ph = 2 * np.pi * np.cumsum( f ) / SR
	hum = ( np.sin( ph ) + 0.4 * np.sin( 2 * ph ) ) * env( n, 0.01, 0.0, 0.5 )
	fizz = bandpass( noise( dur ), 1800, 6000 ) * np.exp( -t / 0.12 ) * 0.35
	return mix( hum * 0.7, fizz )


def radio_open():
	"""Keying the helmet comm: a squelch click, a quick rising two-tone chirp and a breath of static."""
	click = blip( 2100, 0.015, 0.7 )
	chirp = mix( at( blip( 1400, 0.045, 0.45 ), 0.02, 0.2 ), at( blip( 1900, 0.05, 0.45 ), 0.07, 0.2 ) )
	hiss = at( bandpass( noise( 0.12 ), 1200, 4200 ) * env( int( SR * 0.12 ), 0.003, 0.05, 0.05 ) * 0.35, 0.0, 0.2 )
	return mix( click, chirp, hiss )


def radio_close():
	"""Letting go of the comm: a falling chirp and a short static tail."""
	chirp = mix( at( blip( 1700, 0.05, 0.45 ), 0.0, 0.32 ), at( blip( 1150, 0.06, 0.45, glide=850 ), 0.055, 0.32 ) )
	tail = at( bandpass( noise( 0.2 ), 900, 3600 ) * env( int( SR * 0.2 ), 0.002, 0.08, 0.1 ) * 0.4, 0.1, 0.32 )
	return mix( chirp, tail )


def comms_on():
	"""Comm receiver on: two rising pips."""
	return mix( at( blip( 880, 0.05, 0.5 ), 0.0, 0.18 ), at( blip( 1320, 0.06, 0.5 ), 0.08, 0.18 ) )


def comms_off():
	"""Comm receiver off: two falling pips."""
	return mix( at( blip( 1320, 0.05, 0.5 ), 0.0, 0.18 ), at( blip( 700, 0.07, 0.5 ), 0.08, 0.18 ) )


def ear_ring():
	"""The ringing in your ears after a heavy hit: two close high tones beating slowly, swelling in and fading away."""
	dur = 3.6
	n = int( SR * dur )
	t = t_axis( dur )
	beat = np.sin( 2 * np.pi * 3420 * t ) + 0.8 * np.sin( 2 * np.pi * 3427 * t ) + 0.25 * np.sin( 2 * np.pi * 6850 * t )
	shape = np.minimum( t / 0.12, 1 ) * np.exp( -t / 1.3 )
	hiss = highpass( noise( dur ), 5000 ) * 0.04 * shape
	return beat * shape * 0.5 + hiss


def detonator_arm():
	"""A thermal detonator armed: the trigger cap clicks down and the fuse chirps twice, rising."""
	click = metal_clack( 0.55 )
	c1 = at( blip( 1500, 0.05, 0.5, glide=2300 ), 0.05, 0.3 )
	c2 = at( blip( 2300, 0.06, 0.55, glide=3100 ), 0.12, 0.3 )
	return mix( at( click, 0.0, 0.3 ), c1, c2 )


def detonator_beep():
	"""One tick of the fuse: a short, bright electronic beep."""
	n = int( SR * 0.055 )
	t = t_axis( 0.055 )
	beep = ( np.sin( 2 * np.pi * 3150 * t ) + 0.35 * np.sin( 2 * np.pi * 6300 * t ) ) * env( n, 0.002, 0.03, 0.01 )
	return beep * 0.7


def detonator_blast():
	"""The detonator going off: a sharp crack, a deep sub thump, a rolling roar, debris rattling down and a long rumble."""
	dur = 2.8
	n = int( SR * dur )
	t = t_axis( dur )
	crack = bandpass( noise( 0.16 ), 1400, 9000 ) * env( int( SR * 0.16 ), 0.0004, 0.035, 0.03 ) * 1.6
	thump = sweep( 95, 30, 1.3 ) * np.exp( -t_axis( 1.3 ) / 0.42 ) * 1.4
	roar = bandpass( noise( 1.6 ), 140, 1300 ) * env( int( SR * 1.6 ), 0.004, 0.42, 0.25 ) * 1.1
	body = lowpass( noise( dur ), 220 ) * np.exp( -t / 0.55 ) * 5.0
	rumble = lowpass( noise( dur ), 70 ) * np.minimum( t / 0.08, 1 ) * np.exp( -t / 1.1 ) * 7.0
	debris = np.zeros( n, dtype=np.float32 )
	for _ in range( 14 ):
		s = rng.uniform( 0.25, 1.9 )
		debris += at( metal_clack( rng.uniform( 0.06, 0.2 ) ), s, dur )
	return mix( at( crack, 0.0, dur ), at( thump, 0.0, dur ), at( roar, 0.0, dur ), body, rumble, debris )


# ----- planets: the gunship ride, Kamino's storm, the wind on Tatooine and Hoth ------------------------------------------

def looped( x, dur, fade=0.5 ):
	"""Make the first `dur` seconds of x (which runs `fade` seconds longer) loop: its overrun is crossfaded into its start."""
	n = int( SR * dur )
	f = int( SR * fade )
	out = x[:n].copy()
	ramp = np.linspace( 0, 1, f, dtype=np.float32 )
	out[:f] = x[:f] * ramp + x[n:n + f] * ( 1 - ramp )
	return out


def gunship_ride():
	"""Riding a gunship down or up: turbines spool up to a thrumming whine, wind roars past the open bay, then it settles."""
	dur = 4.2
	t = t_axis( dur )
	shape = np.minimum( t / 1.0, 1 ) * np.minimum( ( dur - t ) / 0.9, 1 )
	spool = sweep( 160, 520, dur ) * 0.5 * shape
	whine = sweep( 1100, 1700, dur ) * 0.12 * shape
	hull = lowpass( noise( dur ), 90 ) * 7.0 * shape
	thrum = np.sin( 2 * np.pi * 41 * t ) * ( 0.6 + 0.4 * np.sin( 2 * np.pi * 6.5 * t ) ) * 0.6 * shape
	rush = bandpass( noise( dur ), 300, 2600 ) * np.exp( -( ( t - 2.1 ) / 0.9 ) ** 2 ) * 1.3
	return mix( spool, whine, hull, thrum, rush )


def thunder():
	"""Thunder over the sea: a near crack, then a long rumble rolling away in swells."""
	dur = 4.0
	t = t_axis( dur )
	crack = bandpass( noise( 0.3 ), 500, 5000 ) * env( int( SR * 0.3 ), 0.002, 0.08, 0.05 ) * 1.2
	swell = 0.55 + 0.45 * np.sin( 2 * np.pi * 0.9 * t + 1.3 ) * np.sin( 2 * np.pi * 0.37 * t )
	rumble = lowpass( noise( dur ), 110 ) * np.minimum( t / 0.15, 1 ) * np.exp( -t / 1.5 ) * swell * 9.0
	return mix( at( crack, 0.02, dur ), rumble )


def rain_loop():
	"""Steady rain on metal decks: a hiss, a low patter and countless small drops."""
	dur, fade = 6.0, 0.6
	full = dur + fade
	hiss = bandpass( noise( full ), 900, 9000 ) * 0.35
	patter = lowpass( noise( full ), 450 ) * 1.6
	drops = np.zeros( int( SR * full ), dtype=np.float32 )
	for _ in range( 900 ):
		s0 = rng.uniform( 0, full - 0.02 )
		d = highpass( noise( 0.012 ), 2500 ) * env( int( SR * 0.012 ), 0.0005, 0.004, 0.003 ) * rng.uniform( 0.1, 0.5 )
		drops += at( d, s0, full )
	return looped( mix( hiss, patter, drops ), dur, fade )
LOOPS.add( "rain_loop" )


def wind_loop():
	"""Wind across open ground: a low roar that breathes, with gusts whistling higher now and then."""
	dur, fade = 8.0, 0.8
	full = dur + fade
	t = t_axis( full )
	breathe = 0.6 + 0.4 * np.sin( 2 * np.pi * 0.21 * t ) * np.sin( 2 * np.pi * 0.08 * t + 0.7 )
	roar = lowpass( noise( full ), 380 ) * 3.0 * breathe
	gust = bandpass( noise( full ), 500, 1600 ) * np.clip( np.sin( 2 * np.pi * 0.13 * t + 2.0 ), 0, 1 ) ** 2 * 0.5
	return looped( mix( roar, gust ), dur, fade )
LOOPS.add( "wind_loop" )


def flashlight_click( up ):
	"""
	A real tactical light's tail-cap switch, no tones (Angelo found the first version cartoony): the button's spring
	snapping over (a hard, very short broadband transient), the plastic-and-aluminium body ringing for a few
	milliseconds, and the release a moment later, quieter. On is the full press; off a slightly duller, quicker one.
	"""
	def transient( vol, lo, hi, ring_f, dur=0.012 ):
		n = int( SR * dur )
		crack = bandpass( noise( dur ), lo, hi ) * np.exp( -np.arange( n ) / ( SR * 0.0012 ) )
		ring = np.sin( 2 * np.pi * ring_f * t_axis( dur )[:n] ) * np.exp( -np.arange( n ) / ( SR * 0.0025 ) ) * 0.35
		thump = lowpass( noise( dur ), 600 ) * np.exp( -np.arange( n ) / ( SR * 0.002 ) ) * 0.6
		return ( crack + ring + thump ) * vol
	total = 0.09
	press = transient( 1.0, 1800 if up else 1500, 11000 if up else 8000, 4300 if up else 3900 )
	release = transient( 0.42 if up else 0.5, 2200, 10000, 4700, 0.008 )
	gap = 0.034 if up else 0.026
	room = lowpass( noise( 0.05 ), 3000 ) * env( int( SR * 0.05 ), 0.001, 0.0, 0.045, curve=4.0 ) * 0.04     # a touch of the space around it
	return mix( at( press, 0.0, total ), at( release, gap, total ), at( room, 0.002, total ) )


def flashlight_on():
	return flashlight_click( True )


def flashlight_off():
	return flashlight_click( False )


RECIPES = {
	"flashlight_on": flashlight_on, "flashlight_off": flashlight_off,
	"ui_hover": ui_hover, "ui_click": ui_click, "ui_confirm": ui_confirm, "ui_deny": ui_deny,
	"ui_open": ui_open, "ui_close": ui_close, "ui_chat_open": ui_chat_open, "ui_chat_msg": ui_chat_msg,
	"ui_use_item": ui_use_item, "weapon_draw": weapon_draw, "weapon_holster": weapon_holster,
	"door_open": door_open, "door_close": door_close, "door_lock": door_lock, "door_unlock": door_unlock,
	"hangar_alarm": hangar_alarm,
	"blaster_rifle": blaster_rifle, "blaster_dc15a": blaster_dc15a, "blaster_dc15s": blaster_dc15s, "blaster_dc17": blaster_dc17, "blaster_z6": blaster_z6, "blaster_dc15x": blaster_dc15x, "blaster_e5": blaster_e5, "blaster_carbine": blaster_carbine, "blaster_pistol": blaster_pistol,
	"blaster_repeater": blaster_repeater, "blaster_stun": blaster_stun, "blaster_reload": blaster_reload, "blaster_empty": blaster_empty, "reload_eject": reload_eject, "reload_insert": reload_insert, "reload_charge": reload_charge,
	"blaster_impact": blaster_impact, "hit_marker": hit_marker, "hit_marker_kill": hit_marker_kill, "hurt": hurt,
	"radio_static": radio_static, "dispatch": dispatch, "order_posted": order_posted, "squad_join": squad_join,
	"heartbeat": heartbeat, "bleed": bleed, "revive_start": revive_start, "revive_done": revive_done, "bacta_station": bacta_station,
	"lift_ride": lift_ride, "lift_arrive": lift_arrive, "teleport": teleport, "alert_klaxon": alert_klaxon, "alert_change": alert_change,
	"ship_chime": ship_chime, "weapon_drop": weapon_drop, "weapon_pickup": weapon_pickup, "ear_ring": ear_ring,
	"radio_open": radio_open, "radio_close": radio_close, "shield_on": shield_on, "shield_off": shield_off, "comms_on": comms_on, "comms_off": comms_off,
	"detonator_arm": detonator_arm, "detonator_beep": detonator_beep, "detonator_blast": detonator_blast,
	"gunship_ride": gunship_ride, "thunder": thunder, "rain_loop": rain_loop, "wind_loop": wind_loop,
}

if __name__ == "__main__":
	only = set( a for a in __import__( "sys" ).argv if a in RECIPES )      # optional: names after "--" to render just those
	for name, fn in RECIPES.items():
		if not only or name in only:
			save( name, fn() )
