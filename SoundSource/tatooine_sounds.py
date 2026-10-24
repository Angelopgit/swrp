"""
Tatooine's soundscape, all synthesized (no samples): the desert wind bed, gusts with blown sand, a grainy sand hiss and
far-off moans of wind over the mesas. Writes 16-bit mono 44.1 kHz WAVs and their .sound events into
Assets/sounds/tatooine (Code/Map/Tatooine/TatooineWeather.cs plays them while your camera is on the planet).

    py SoundSource/tatooine_sounds.py        (numpy only; filters are done in the frequency domain, so it's quick)
"""
import json
import os
import wave

import numpy as np

SR = 44100
OUT = os.environ.get( "SWRP_TATOOINE_SOUND_OUT" ) or os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "tatooine" )
rng = np.random.default_rng( 77 )


def t_axis( dur ):
	return np.arange( int( SR * dur ) ) / SR


def noise( dur ):
	return rng.standard_normal( int( SR * dur ) )


def band( x, lo, hi, q=0.0 ):
	"""Band-pass in the frequency domain; q > 0 adds a resonant bump at the band's centre (howl)."""
	X = np.fft.rfft( x )
	f = np.fft.rfftfreq( len( x ), 1 / SR )
	g = ( ( f >= lo ) & ( f <= hi ) ).astype( float )
	if q:
		c = np.sqrt( lo * hi )
		g += q * np.exp( -( ( np.log( np.maximum( f, 1 ) ) - np.log( c ) ) / 0.12 ) ** 2 )
	return np.fft.irfft( X * g, len( x ) )


def moving_band( x, centre, width ):
	"""A resonant band whose centre (Hz, per sample) moves: done in overlapping blocks."""
	out = np.zeros_like( x )
	block = 4096
	win = np.hanning( block * 2 )
	for s in range( 0, len( x ) - block, block ):
		seg = x[s:s + block * 2]
		if len( seg ) < block * 2: break
		c = centre[s + block]
		out[s:s + block * 2] += band( seg * win, c / width, c * width ) * 1.0
	return out


def looped( x, dur, fade ):
	n, f = int( SR * dur ), int( SR * fade )
	out = x[:n].copy()
	ramp = np.linspace( 0, 1, f )
	out[:f] = x[:f] * ramp + x[n:n + f] * ( 1 - ramp )
	return out


def slow( dur, rate, seed_off=0.0 ):
	"""A slow wandering 0..1 curve."""
	t = t_axis( dur )
	v = 0.5 + 0.25 * np.sin( 2 * np.pi * rate * t + seed_off ) + 0.25 * np.sin( 2 * np.pi * rate * 2.37 * t + seed_off * 3.1 )
	return np.clip( v, 0, 1 )


def desert_wind():
	"""The bed: a deep, breathing roar, a hollow howl drifting in pitch, and a thin high whistle that comes and goes."""
	dur, fade = 24.0, 2.0
	full = dur + fade
	breathe = 0.55 + 0.45 * slow( full, 0.045, 0.4 )
	roar = band( noise( full ), 40, 420 ) * 2.6 * breathe
	howl_c = 260 + 220 * slow( full, 0.03, 1.7 )
	howl = moving_band( noise( full ), howl_c, 1.18 ) * 1.4 * slow( full, 0.06, 2.2 ) ** 1.5
	whistle = moving_band( noise( full ), 1400 + 500 * slow( full, 0.05, 0.9 ), 1.04 ) * 0.5 * slow( full, 0.08, 3.3 ) ** 3
	return looped( roar + howl + whistle, dur, fade )


def wind_gust( k ):
	"""One gust: the wind swells and rises in pitch, sand rattles across everything, then it drops away."""
	dur = [3.6, 4.4, 5.2, 4.0][k]
	t = t_axis( dur )
	peak = [0.38, 0.45, 0.5, 0.42][k]
	shape = np.where( t / dur < peak, ( t / dur / peak ) ** 1.6, ( ( 1 - t / dur ) / ( 1 - peak ) ) ** 2.2 )
	body = band( noise( dur ), 60, 900 ) * 2.4 * shape
	rise = moving_band( noise( dur ), 300 + 500 * shape, 1.15 ) * 1.6 * shape
	sand = band( noise( dur ), 2500, 12000 ) * 0.9 * shape ** 1.5 * ( 0.6 + 0.4 * np.abs( band( noise( dur ), 3, 18 ) ) * 8 )
	return body + rise + sand


def sand_hiss():
	"""Sand grains skittering over the dunes: a fine high hiss with tiny ticks, swelling gently."""
	dur, fade = 14.0, 1.5
	full = dur + fade
	hiss = band( noise( full ), 3000, 14000 ) * 0.5 * ( 0.5 + 0.5 * slow( full, 0.11, 0.3 ) )
	ticks = np.zeros( int( SR * full ) )
	for _ in range( 2600 ):
		s = int( rng.uniform( 0, full - 0.01 ) * SR )
		n = int( SR * 0.002 )
		ticks[s:s + n] += rng.standard_normal( n ) * np.exp( -np.arange( n ) / ( SR * 0.0004 ) ) * rng.uniform( 0.05, 0.35 )
	ticks = band( ticks, 2000, 16000 )
	return looped( hiss + ticks, dur, fade )


def distant_moan( k ):
	"""Wind over the mesas, far away: a low hollow tone that swells, wavers and fades, muffled by distance."""
	dur = [7.0, 9.0][k]
	t = t_axis( dur )
	f0 = [118, 92][k]
	shape = np.sin( np.pi * t / dur ) ** 2
	vib = 1 + 0.03 * np.sin( 2 * np.pi * 0.7 * t ) + 0.02 * np.sin( 2 * np.pi * 0.23 * t + 1 )
	phase = np.cumsum( 2 * np.pi * f0 * vib * np.linspace( 1.0, 0.94, len( t ) ) / SR )
	tone = ( np.sin( phase ) + 0.35 * np.sin( 2 * phase ) + 0.12 * np.sin( 3 * phase ) )
	breath = band( noise( dur ), f0 * 0.7, f0 * 3.5 ) * 0.9
	x = ( tone * 0.5 + breath ) * shape
	return band( x, 50, 900 )


def save( name, x, peak=0.8 ):
	x = x / max( 1e-6, np.max( np.abs( x ) ) ) * peak
	pcm = ( np.clip( x, -1, 1 ) * 32767 ).astype( np.int16 )
	with wave.open( os.path.join( OUT, name + ".wav" ), "wb" ) as w:
		w.setnchannels( 1 )
		w.setsampwidth( 2 )
		w.setframerate( SR )
		w.writeframes( pcm.tobytes() )
	print( f"{name}.wav  {len( x ) / SR:.1f} s" )


def event( name, wavs, volume, pitch_random=0.0 ):
	with open( os.path.join( OUT, name + ".sound" ), "w", newline="\n" ) as f:
		json.dump( { "volume": volume, "sounds": [f"sounds/tatooine/{w}.wav" for w in wavs], "VolumeRandom": 0.1 if pitch_random else 0.0,
			"Pitch": 1.0, "PitchRandom": pitch_random, "DistanceMax": 2000.0, "UI": True }, f )


if __name__ == "__main__":
	os.makedirs( OUT, exist_ok=True )
	save( "desert_wind", desert_wind() )
	event( "desert_wind", ["desert_wind"], 0.55 )
	for k in range( 4 ):
		save( f"wind_gust_{k}", wind_gust( k ) )
	event( "wind_gust", [f"wind_gust_{k}" for k in range( 4 )], 0.6, 0.08 )
	save( "sand_hiss", sand_hiss(), 0.6 )
	event( "sand_hiss", ["sand_hiss"], 0.3 )
	for k in range( 2 ):
		save( f"distant_moan_{k}", distant_moan( k ), 0.7 )
	event( "distant_moan", ["distant_moan_0", "distant_moan_1"], 0.35, 0.1 )
