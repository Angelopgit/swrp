"""
Original synthesized menu theme for the SWRP character screen ("Republic Muster"). Pure numpy, no samples, no existing music:
an original D minor chord progression (Dm - Bb - F - C) with string/brass pads, a low bass, soft timpani swells and an
original horn melody, 70 BPM, 20 bars, rendered as a seamless loop (the reverb tail is folded back onto the start).

    blender -b --factory-startup -P SoundSource/menu_music.py   (Blender ships numpy; plain Python may not have it)

Writes Assets/sounds/music/menu_theme.wav (16-bit stereo, 44.1 kHz). The game plays it through menu_theme.sound while
the character screen is open; a track you own can replace it locally as Assets/sounds/music/menu_custom.(mp3|wav|ogg).
"""
import os
import wave
import numpy as np

SR = 44100
BPM = 70.0
BEAT = 60.0 / BPM
BARS = 20
LENGTH = BARS * 4 * BEAT
OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "music" )
os.makedirs( OUT, exist_ok=True )
rng = np.random.default_rng( 1138 )

N = int( SR * ( LENGTH + 6.0 ) )        # room for the tail, folded back onto the start at the end
mix = np.zeros( ( 2, N ), dtype=np.float64 )


def hz( note ):
	"""'D4' -> Hz (A4 = 440)."""
	names = { "C": -9, "C#": -8, "D": -7, "D#": -6, "E": -5, "F": -4, "F#": -3, "G": -2, "G#": -1, "A": 0, "A#": 1, "Bb": 1, "B": 2 }
	name, octave = note[:-1], int( note[-1] )
	return 440.0 * 2 ** ( ( names[name] + 12 * ( octave - 4 ) ) / 12 )


def adsr( n, attack, release, sustain=1.0, decay=0.0 ):
	e = np.full( n, sustain, dtype=np.float64 )
	a = max( 1, int( SR * attack ) )
	r = max( 1, int( SR * release ) )
	e[:a] = np.linspace( 0, 1, a ) ** 1.5
	if decay > 0:
		d = min( n - a, int( SR * decay ) )
		e[a:a + d] = np.linspace( 1, sustain, d )
	e[-r:] *= np.linspace( 1, 0, r ) ** 1.3
	return e


def spectral_lowpass( x, cutoff, slope=2.0 ):
	"""Smooth lowpass in the frequency domain (no ringing, no python loops)."""
	spec = np.fft.rfft( x )
	f = np.fft.rfftfreq( len( x ), 1 / SR )
	spec *= 1.0 / ( 1.0 + ( f / cutoff ) ** ( 2 * slope ) )
	return np.fft.irfft( spec, len( x ) )


def saw( freq, n, detune_cents=0.0, vibrato=0.0, phase=None ):
	t = np.arange( n ) / SR
	f = np.full( n, freq * 2 ** ( detune_cents / 1200 ) )
	if vibrato:
		f = f * 2 ** ( vibrato * np.sin( 2 * np.pi * 5.0 * t ) * np.clip( ( t - 0.35 ) / 0.6, 0, 1 ) / 1200 )
	ph = np.cumsum( f ) / SR + ( rng.random() if phase is None else phase )
	return 2 * ( ph % 1.0 ) - 1


def place( buf, start_s, left=1.0, right=1.0 ):
	i = int( start_s * SR )
	j = min( N, i + len( buf ) )
	if j <= i:
		return
	mix[0, i:j] += buf[:j - i] * left
	mix[1, i:j] += buf[:j - i] * right


# ------------------------------------------------------------------------------------------------------- harmony
CHORDS = [
	( "D", ["D3", "A3", "D4", "F4", "A4"] ),
	( "Bb", ["Bb2", "F3", "Bb3", "D4", "F4"] ),
	( "F", ["F2", "C3", "F3", "A3", "C4"] ),
	( "C", ["C3", "G3", "C4", "E4", "G4"] ),
]
BAR = 4 * BEAT


def pad( notes, start, dur, gain, bright ):
	n = int( SR * ( dur + 1.6 ) )
	for k, note in enumerate( notes ):
		f = hz( note )
		voice = sum( saw( f, n, det ) for det in ( -7, 0, 7 ) ) / 3
		voice = spectral_lowpass( voice, bright * ( 1 + 0.3 * k / len( notes ) ) )
		voice *= adsr( n, 0.9, 1.6 )
		pan = 0.5 + 0.35 * ( ( k / max( 1, len( notes ) - 1 ) ) - 0.5 )
		place( voice * gain / len( notes ) ** 0.6, start, 1 - pan * 0.6, 0.4 + pan * 0.6 )


def bass( note, start, dur, gain ):
	n = int( SR * ( dur + 0.8 ) )
	f = hz( note )
	t = np.arange( n ) / SR
	b = np.sin( 2 * np.pi * f * t ) + 0.25 * spectral_lowpass( saw( f, n ), 300 )
	place( b * adsr( n, 0.25, 0.8 ) * gain, start )


def timpani( start, gain, pitch="D2" ):
	n = int( SR * 2.2 )
	t = np.arange( n ) / SR
	f = hz( pitch ) * ( 1 + 0.12 * np.exp( -t * 18 ) )
	body = np.sin( 2 * np.pi * np.cumsum( f ) / SR ) * np.exp( -t * 2.4 )
	hit = spectral_lowpass( rng.normal( 0, 1, n ), 900 ) * np.exp( -t * 30 ) * 0.5
	place( ( body + hit ) * gain, start )


def horn( note, start, beats, gain, bright, octave_up=False ):
	dur = beats * BEAT
	n = int( SR * ( dur + 0.5 ) )
	f = hz( note ) * ( 2 if octave_up else 1 )
	tone = sum( saw( f, n, det, vibrato=7.0 ) for det in ( -4, 4 ) ) / 2
	# brass "blat": the filter opens on the attack and settles
	opening = spectral_lowpass( tone, bright * 1.8 )
	settled = spectral_lowpass( tone, bright )
	w = np.clip( np.arange( n ) / ( SR * 0.18 ), 0, 1 )
	tone = opening * ( 1 - w ) * 0.6 + settled * ( 0.4 + 0.6 * w )
	place( tone * adsr( n, 0.07, 0.45, sustain=0.85, decay=0.25 ) * gain, start, 0.55, 0.45 )


# ------------------------------------------------------------------------------------------------- arrangement
for rep in range( BARS // 4 ):                  # the four chords, one bar each, five times: intro, phrase A, phrase B
	for c, ( root, notes ) in enumerate( CHORDS ):
		start = ( rep * 4 + c ) * BAR
		full = rep >= 3
		pad( notes, start, BAR, 0.55 if full else 0.42, 2300 if full else 1500 )
		bass( notes[0], start, BAR, 0.12 )
		if c == 0:
			timpani( start, 0.30 if full else 0.18 )
		if full and c == 3:
			timpani( start + 3 * BEAT, 0.14, "A1" )
			timpani( start + 3.5 * BEAT, 0.17, "A1" )

# original horn melody, one note per (pitch, beats); phrase A low and quiet, phrase B an octave up and fuller
PHRASE = [
	( "A4", 1 ), ( "D5", 1 ), ( "E5", 1 ), ( "F5", 1 ),          # Dm
	( "F5", 2 ), ( "E5", 1 ), ( "D5", 1 ),                       # Bb
	( "C5", 2 ), ( "A4", 1 ), ( "C5", 1 ),                       # F
	( "E5", 3 ), ( "D5", 1 ),                                    # C
	( "D5", 2 ), ( "F5", 1 ), ( "A5", 1 ),                       # Dm
	( "G5", 2 ), ( "F5", 1 ), ( "D5", 1 ),                       # Bb
	( "C5", 1 ), ( "D5", 1 ), ( "E5", 1 ), ( "F5", 1 ),          # F
	( "E5", 3 ), ( "C5", 1 ),                                    # C
]
t = 4 * BAR                                     # enters after the four-bar intro
for note, beats in PHRASE:
	low = note[:-1] + str( int( note[-1] ) - 1 )
	horn( low, t, beats, 0.42, 1200 )
	t += beats * BEAT
for note, beats in PHRASE[:-2] + [( "E5", 2 ), ( "D5", 2 )]:      # answer phrase resolves home to D
	horn( note, t, beats, 0.40, 1900 )
	horn( note[:-1] + str( int( note[-1] ) - 1 ), t, beats, 0.22, 1200 )
	t += beats * BEAT


# ------------------------------------------------------------------------------------------------------- reverb
def comb( x, delay_s, g ):
	d = int( SR * delay_s )
	y = x.copy()
	for k in range( d, len( y ), d ):
		y[k:k + d] += g * y[k - d:k][:len( y[k:k + d] )]
	return y


def allpass( x, delay_s, g ):
	d = int( SR * delay_s )
	y = np.zeros_like( x )
	buf = np.zeros( len( x ) + d )
	# block-wise: v[n] = x[n] + g v[n-d]; y[n] = -g v[n] + v[n-d]
	v = np.zeros( len( x ) + d )
	for k in range( 0, len( x ), d ):
		seg = x[k:k + d]
		v[k + d:k + d + len( seg )] = seg + g * v[k:k + len( seg )]
		y[k:k + len( seg )] = -g * v[k + d:k + d + len( seg )] + v[k:k + len( seg )]
	return y


def reverb( x, side ):
	offs = 0.0007 * side
	wet = sum( comb( x, d + offs, g ) for d, g in ( ( 0.0297, 0.80 ), ( 0.0371, 0.79 ), ( 0.0411, 0.78 ), ( 0.0437, 0.77 ) ) ) / 4
	for d, g in ( ( 0.005, 0.7 ), ( 0.0017, 0.7 ) ):
		wet = allpass( wet, d, g )
	return spectral_lowpass( wet, 4500 )


out = np.stack( [mix[0] * 0.78 + reverb( mix[0], -1 ) * 0.42, mix[1] * 0.78 + reverb( mix[1], 1 ) * 0.42] )

# seamless loop: fold everything past the loop point back onto the start
L = int( SR * LENGTH )
tail = out[:, L:]
out = out[:, :L].copy()
out[:, :tail.shape[1]] += tail
# gentle master: soft clip and -3 dB peak
out = np.tanh( out / ( np.abs( out ).max() + 1e-9 ) * 1.2 ) / np.tanh( 1.2 ) * 0.707

pcm = ( out.T * 32767 ).astype( np.int16 )
path = os.path.join( OUT, "menu_theme.wav" )
with wave.open( path, "wb" ) as w:
	w.setnchannels( 2 )
	w.setsampwidth( 2 )
	w.setframerate( SR )
	w.writeframes( pcm.tobytes() )
print( f"MUSIC {path} {LENGTH:.1f}s" )
