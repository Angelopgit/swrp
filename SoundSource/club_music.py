"""
Original synthesized club tracks for the cantina DJ booth (Code/Club). Pure numpy, no samples and no existing music: four
original loops, each with a drums-only first and last four bars so a DJ can mix in and out of them.

    underlevel   house      124 BPM  A minor   Am7 - Fmaj7 - Cmaj7 - G
    hyperlane    techno     130 BPM  F minor   rolling 16th bass, stab riff
    outer_rim    downtempo   96 BPM  D dorian  Dm9 - G13 pads, soft pluck
    kessel       breaks     112 BPM  E minor   syncopated break, brass stabs

    blender -b --factory-startup -P SoundSource/club_music.py   (Blender ships numpy; plain Python may not have it)

Writes Assets/sounds/music/club/<id>.wav (16-bit stereo, 44.1 kHz). Each is a seamless loop (the reverb tail is folded onto
the start); SwrpClub plays them with Repeat on.
"""
import os
import sys
import wave
import numpy as np

SR = 44100
OUT = os.path.join( os.path.dirname( os.path.abspath( __file__ ) ), "..", "Assets", "sounds", "music", "club" )
OUT = os.environ.get( "CLUB_OUT", OUT )
os.makedirs( OUT, exist_ok=True )

NOTES = { "C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6, "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11 }


def hz( note ):
	name, octave = note[:-1], int( note[-1] )
	return 440.0 * 2 ** ( ( NOTES[name] - 9 + 12 * ( octave - 4 ) ) / 12 )


class Track:
	def __init__( self, bpm, bars, seed, tail=4.0 ):
		self.bpm = bpm
		self.beat = 60.0 / bpm
		self.bars = bars
		self.length = bars * 4 * self.beat
		self.n = int( SR * self.length )
		self.N = int( SR * ( self.length + tail ) )
		self.rng = np.random.default_rng( seed )
		self.drums = np.zeros( ( 2, self.N ) )
		self.music = np.zeros( ( 2, self.N ) )
		self.kick_env = np.zeros( self.N )

	def at( self, bar, beat ):
		return int( SR * ( bar * 4 + beat ) * self.beat )

	def add( self, bus, sig, start, pan=0.0, gain=1.0 ):
		end = min( self.N, start + len( sig ) )
		if end <= start:
			return
		l = np.cos( ( pan + 1 ) * np.pi / 4 ) * gain
		r = np.sin( ( pan + 1 ) * np.pi / 4 ) * gain
		bus[0, start:end] += sig[:end - start] * l
		bus[1, start:end] += sig[:end - start] * r


# ----- instruments -----

def env( n, attack, decay ):
	t = np.arange( n ) / SR
	e = np.exp( -t / max( decay, 1e-4 ) )
	a = max( 1, int( SR * attack ) )
	e[:a] *= np.linspace( 0, 1, a )
	return e


def kick( tr, punch=1.0, length=0.45 ):
	n = int( SR * length )
	t = np.arange( n ) / SR
	f = 46 + 110 * np.exp( -t / 0.035 )
	ph = 2 * np.pi * np.cumsum( f ) / SR
	body = np.sin( ph ) * np.exp( -t / ( 0.16 * punch ) )
	click = tr.rng.standard_normal( n ) * np.exp( -t / 0.004 ) * 0.25
	return np.tanh( ( body + click ) * 1.6 ) * 0.9


def noise_hit( tr, length, decay, lo, hi ):
	n = int( SR * length )
	x = tr.rng.standard_normal( n )
	spec = np.fft.rfft( x )
	f = np.fft.rfftfreq( n, 1 / SR )
	spec *= ( f > lo ) * ( f < hi ) * 1.0
	x = np.fft.irfft( spec, n )
	x /= np.max( np.abs( x ) ) + 1e-9
	return x * env( n, 0.0005, decay )


def clap( tr ):
	n = int( SR * 0.3 )
	out = np.zeros( n )
	for k, d in enumerate( ( 0, 0.011, 0.022 ) ):
		s = noise_hit( tr, 0.3 - d, 0.012 if k < 2 else 0.09, 900, 6000 )
		out[int( d * SR ):int( d * SR ) + len( s )] += s
	return out * 0.55


def snare( tr ):
	n = int( SR * 0.25 )
	t = np.arange( n ) / SR
	tone = np.sin( 2 * np.pi * 190 * t ) * np.exp( -t / 0.04 ) * 0.5
	return ( tone + noise_hit( tr, 0.25, 0.07, 1200, 9000 ) * 0.7 ) * 0.7


def hat( tr, open_=False ):
	return noise_hit( tr, 0.4 if open_ else 0.08, 0.12 if open_ else 0.018, 7000, 16000 ) * ( 0.32 if open_ else 0.25 )


def rim( tr ):
	n = int( SR * 0.08 )
	t = np.arange( n ) / SR
	return ( np.sin( 2 * np.pi * 1700 * t ) + 0.6 * np.sin( 2 * np.pi * 820 * t ) ) * np.exp( -t / 0.012 ) * 0.35


def saw( freq, n, cutoff, detune=0.0, phase=0.0 ):
	"""Band-limited saw: harmonics only up to the cutoff (so it doubles as the filter)."""
	t = np.arange( n ) / SR
	f = freq * 2 ** ( detune / 1200 )
	k_max = int( max( 1, min( 48, cutoff / f, ( SR / 2 - 200 ) / f ) ) )
	out = np.zeros( n )
	for k in range( 1, k_max + 1 ):
		roll = 1.0 / ( 1.0 + ( k * f / cutoff ) ** 4 )
		out += np.sin( 2 * np.pi * k * f * t + phase * k ) / k * roll
	return out * 0.6


def square( freq, n, cutoff ):
	t = np.arange( n ) / SR
	out = np.zeros( n )
	k = 1
	while k * freq < min( cutoff, SR / 2 - 200 ) and k < 40:
		out += np.sin( 2 * np.pi * k * freq * t ) / k
		k += 2
	return out * 0.7


def bass( tr, note, length, cutoff=700, sub=0.6, shape="saw" ):
	n = int( SR * length )
	f = hz( note )
	t = np.arange( n ) / SR
	body = saw( f, n, cutoff ) if shape == "saw" else square( f, n, cutoff )
	s = np.sin( 2 * np.pi * f * t ) * sub
	e = env( n, 0.004, length * 0.9 )
	e[-min( n, 300 ):] *= np.linspace( 1, 0, min( n, 300 ) )
	return np.tanh( ( body + s ) * 1.3 ) * e * 0.5


def chord( tr, notes, length, cutoff=2400, attack=0.005, decay=0.25, voices=2, gain=0.22 ):
	n = int( SR * length )
	out = np.zeros( n )
	for note in notes:
		for v in range( voices ):
			out += saw( hz( note ), n, cutoff, detune=( v - ( voices - 1 ) / 2 ) * 9, phase=tr.rng.random() * 6.28 )
	out /= max( 1, len( notes ) * voices ) ** 0.5
	e = env( n, attack, decay )
	e[-min( n, 400 ):] *= np.linspace( 1, 0, min( n, 400 ) )
	return out * e * gain


def pad( tr, notes, length, cutoff=1500 ):
	n = int( SR * length )
	out = np.zeros( n )
	for note in notes:
		for v in range( 3 ):
			out += saw( hz( note ), n, cutoff, detune=( v - 1 ) * 12, phase=tr.rng.random() * 6.28 )
	out /= ( len( notes ) * 3 ) ** 0.5
	a = int( SR * min( 0.6, length * 0.3 ) )
	r = int( SR * min( 0.8, length * 0.3 ) )
	e = np.ones( n )
	e[:a] = np.linspace( 0, 1, a )
	e[-r:] *= np.linspace( 1, 0, r )
	return out * e * 0.16


def pluck( tr, note, length, bright=3500, decay=0.22 ):
	n = int( SR * length )
	f = hz( note )
	t = np.arange( n ) / SR
	out = np.zeros( n )
	k = 1
	while k * f < min( bright * 2, SR / 2 - 200 ) and k < 30:
		out += np.sin( 2 * np.pi * k * f * t ) / k * np.exp( -t * ( 1 / decay ) * ( 1 + k * 0.35 ) )
		k += 1
	return out * 0.35


def brass( tr, notes, length ):
	n = int( SR * length )
	out = np.zeros( n )
	t = np.arange( n ) / SR
	cut = 900 + 2600 * np.exp( -t / 0.08 )
	for note in notes:
		f = hz( note )
		for k in range( 1, 24 ):
			if k * f > SR / 2 - 200:
				break
			out += np.sin( 2 * np.pi * k * f * t ) / k * ( 1 / ( 1 + ( k * f / cut ) ** 4 ) )
	e = env( n, 0.012, length * 0.5 )
	e[-min( n, 400 ):] *= np.linspace( 1, 0, min( n, 400 ) )
	return np.tanh( out * 0.9 ) * e * 0.24


# ----- mixing -----

def reverb( x, seconds=1.8, mix=0.18, seed=7 ):
	rng = np.random.default_rng( seed )
	n = int( SR * seconds )
	ir = rng.standard_normal( ( 2, n ) ) * np.exp( -np.arange( n ) / SR / ( seconds / 6.9 ) )
	ir[:, :int( SR * 0.012 )] = 0
	ir /= np.sqrt( np.sum( ir ** 2, axis=1, keepdims=True ) )
	L = x.shape[1] + n
	size = 1 << ( L - 1 ).bit_length()
	out = np.zeros_like( x )
	for c in range( 2 ):
		y = np.fft.irfft( np.fft.rfft( x[c], size ) * np.fft.rfft( ir[c], size ), size )[:x.shape[1]]
		out[c] = y
	return x + out * mix


def finish( tr, name, music_reverb=0.22, duck=0.55 ):
	# sidechain: the music ducks under the kick
	k = tr.kick_env
	if k.any():
		kernel = np.exp( -np.arange( int( SR * 0.18 ) ) / ( SR * 0.06 ) )
		pump = np.convolve( k, kernel )[:tr.N]
		pump = 1 - duck * np.clip( pump, 0, 1 )
		tr.music *= pump
	mix = reverb( tr.music, 2.2, music_reverb ) + reverb( tr.drums, 0.9, 0.06, seed=11 )
	# fold the tail onto the start so the loop is seamless
	loop = mix[:, :tr.n].copy()
	tail = mix[:, tr.n:]
	loop[:, :tail.shape[1]] += tail
	# gentle master: high-pass the rumble, soft clip, normalise
	for c in range( 2 ):
		spec = np.fft.rfft( loop[c] )
		f = np.fft.rfftfreq( tr.n, 1 / SR )
		spec *= 1 / ( 1 + ( 28 / np.maximum( f, 1 ) ) ** 4 )
		loop[c] = np.fft.irfft( spec, tr.n )
	loop = np.tanh( loop / ( np.max( np.abs( loop ) ) + 1e-9 ) * 1.4 )
	loop /= np.max( np.abs( loop ) ) + 1e-9
	loop *= 0.89
	data = ( loop.T * 32767 ).astype( np.int16 )
	path = os.path.join( OUT, f"{name}.wav" )
	with wave.open( path, "wb" ) as w:
		w.setnchannels( 2 )
		w.setsampwidth( 2 )
		w.setframerate( SR )
		w.writeframes( data.tobytes() )
	print( f"wrote {path} ({tr.length:.1f} s)" )


def four_floor( tr, bar, clap_=True, hats=True, open_hats=False, ghost=False ):
	for b in range( 4 ):
		p = tr.at( bar, b )
		tr.add( tr.drums, kick( tr ), p )
		tr.kick_env[p] = 1.0
		if clap_ and b in ( 1, 3 ):
			tr.add( tr.drums, clap( tr ), p, pan=0.05 )
		if hats:
			tr.add( tr.drums, hat( tr, open_hats ), tr.at( bar, b + 0.5 ), pan=0.25 )
		if ghost:
			tr.add( tr.drums, hat( tr ) * 0.5, tr.at( bar, b + 0.25 ), pan=-0.3 )
			tr.add( tr.drums, hat( tr ) * 0.5, tr.at( bar, b + 0.75 ), pan=-0.3 )


# ----- the tracks -----

def underlevel():
	tr = Track( 124, 32, 1313 )
	prog = [( "A2", ["A3", "C4", "E4", "G4"] ), ( "F2", ["F3", "A3", "C4", "E4"] ), ( "C2", ["C4", "E4", "G4", "B4"] ), ( "G2", ["G3", "B3", "D4", "E4"] )]
	arp_pat = [0, 2, 1, 3, 2, 0, 3, 1]
	for bar in range( 32 ):
		full = 4 <= bar < 28
		four_floor( tr, bar, clap_=bar >= 2, open_hats=full, ghost=8 <= bar < 28 )
		root, notes = prog[bar % 4]
		if not full:
			continue
		for b in range( 4 ):
			# offbeat bass, octave jump on the last eighth
			tr.add( tr.music, bass( tr, root, tr.beat * 0.45, cutoff=600 ), tr.at( bar, b + 0.5 ), gain=0.9 )
			if b == 3:
				tr.add( tr.music, bass( tr, root[:-1] + str( int( root[-1] ) + 1 ), tr.beat * 0.2, cutoff=900 ), tr.at( bar, b + 0.75 ), gain=0.7 )
			# organ-ish stabs on the offbeats
			if bar >= 8:
				tr.add( tr.music, chord( tr, notes, tr.beat * 0.3, cutoff=2600, decay=0.12, gain=0.3 ), tr.at( bar, b + 0.5 ), pan=-0.15 )
		if bar >= 12 and bar < 26:
			for i in range( 8 ):
				note = notes[arp_pat[i] % len( notes )]
				note = note[:-1] + str( int( note[-1] ) + 1 )
				tr.add( tr.music, pluck( tr, note, tr.beat * 0.5, bright=4000, decay=0.18 ), tr.at( bar, i * 0.5 ), pan=0.35 if i % 2 else -0.35, gain=0.6 )
		if bar % 4 == 0 and 16 <= bar < 28:
			tr.add( tr.music, pad( tr, notes, tr.beat * 16 ), tr.at( bar, 0 ), gain=0.8 )
	finish( tr, "underlevel" )


def hyperlane():
	tr = Track( 130, 32, 66 )
	riff = [( 0, "F4" ), ( 0.75, "Ab4" ), ( 1.5, "C5" ), ( 2.5, "Bb4" ), ( 3.25, "Ab4" )]
	roots = ["F1", "F1", "Db2", "Eb2"]
	for bar in range( 32 ):
		full = 4 <= bar < 28
		four_floor( tr, bar, clap_=bar >= 4, open_hats=True, ghost=True )
		if bar >= 6 and bar < 28:
			for s in range( 16 ):
				if s % 4 == 0:
					continue
				tr.add( tr.drums, hat( tr ) * 0.35, tr.at( bar, s / 4 ), pan=0.4 )
		if not full:
			continue
		root = roots[( bar // 2 ) % 4]
		for s in range( 16 ):
			if s % 4 == 0:
				continue        # the kick owns the downbeat
			cut = 400 + 500 * ( 0.5 + 0.5 * np.sin( 2 * np.pi * ( bar * 16 + s ) / 64 ) )
			tr.add( tr.music, bass( tr, root, tr.beat * 0.22, cutoff=cut, shape="square" ), tr.at( bar, s / 4 ), gain=0.75 )
		if bar >= 12:
			for at, note in riff:
				tr.add( tr.music, chord( tr, [note, note[:-1] + str( int( note[-1] ) - 1 )], tr.beat * 0.35, cutoff=3000, decay=0.09, gain=0.32 ), tr.at( bar, at ), pan=0.2 if bar % 2 else -0.2 )
		if bar % 8 == 4:
			tr.add( tr.music, pad( tr, ["F3", "Ab3", "C4", "Eb4"], tr.beat * 32, cutoff=900 ), tr.at( bar, 0 ), gain=0.7 )
	finish( tr, "hyperlane", music_reverb=0.26, duck=0.6 )


def outer_rim():
	tr = Track( 96, 24, 1977, tail=5.0 )
	prog = [( "D2", ["D3", "F3", "A3", "C4", "E4"] ), ( "G2", ["G3", "B3", "E4", "F4", "A4"] )]
	melody = [( 0, "A4" ), ( 1, "C5" ), ( 1.5, "D5" ), ( 3, "E5" ), ( 4.5, "D5" ), ( 5, "C5" ), ( 6, "A4" ), ( 7, "G4" )]
	for bar in range( 24 ):
		full = 4 <= bar < 20
		for b in range( 4 ):
			p = tr.at( bar, b )
			if b in ( 0, 2 ) or ( b == 3 and bar % 2 ):
				k = kick( tr, punch=1.4, length=0.5 ) * 0.8
				tr.add( tr.drums, k, p if b != 3 else tr.at( bar, 3.5 ) )
				tr.kick_env[p] = 0.6
			if b in ( 1, 3 ):
				tr.add( tr.drums, rim( tr ), p, pan=-0.2 )
			tr.add( tr.drums, hat( tr ) * 0.6, tr.at( bar, b + 0.5 ), pan=0.3 )
			tr.add( tr.drums, hat( tr ) * 0.3, tr.at( bar, b + 0.75 ), pan=0.3 )
		if not full:
			continue
		root, notes = prog[( bar // 2 ) % 2]
		if bar % 2 == 0:
			tr.add( tr.music, pad( tr, notes, tr.beat * 8, cutoff=1300 ), tr.at( bar, 0 ) )
			tr.add( tr.music, bass( tr, root, tr.beat * 2.8, cutoff=350, sub=1.0 ), tr.at( bar, 0 ), gain=0.9 )
			tr.add( tr.music, bass( tr, root, tr.beat * 1.0, cutoff=350, sub=1.0 ), tr.at( bar, 3 ), gain=0.7 )
		if bar >= 8 and bar % 2 == 0:
			for at, note in melody:
				tr.add( tr.music, pluck( tr, note, tr.beat * 1.5, bright=2500, decay=0.4 ), tr.at( bar, at ), pan=0.15, gain=0.7 )
	finish( tr, "outer_rim", music_reverb=0.32, duck=0.3 )


def kessel():
	tr = Track( 112, 32, 112 )
	break_k = [0, 1.5, 2.75]
	break_s = [1, 3, 3.75]
	stabs = [( 0, ["E4", "G4", "B4"] ), ( 0.75, ["E4", "G4", "B4"] ), ( 2.5, ["D4", "F#4", "A4"] ), ( 3.5, ["G4", "B4", "D5"] )]
	bassline = [( 0, "E2", 0.5 ), ( 0.75, "E2", 0.25 ), ( 1.5, "G2", 0.5 ), ( 2.5, "A2", 0.5 ), ( 3, "B1", 0.5 ), ( 3.5, "D2", 0.4 )]
	for bar in range( 32 ):
		full = 4 <= bar < 28
		for b in break_k:
			p = tr.at( bar, b )
			tr.add( tr.drums, kick( tr, punch=0.9 ), p )
			tr.kick_env[p] = 0.8
		for b in break_s:
			tr.add( tr.drums, snare( tr ) * ( 0.55 if b == 3.75 else 1.0 ), tr.at( bar, b ), pan=0.05 )
		for e in range( 8 ):
			tr.add( tr.drums, hat( tr, open_=( e == 7 and bar % 2 ) ) * 0.8, tr.at( bar, e / 2 ), pan=0.3 )
		if not full:
			continue
		for at, note, ln in bassline:
			tr.add( tr.music, bass( tr, note, tr.beat * ln, cutoff=800 ), tr.at( bar, at ), gain=0.85 )
		if bar >= 8 and bar % 2 == 1:
			for at, notes in stabs:
				tr.add( tr.music, brass( tr, notes, tr.beat * 0.4 ), tr.at( bar, at ), pan=-0.1 )
		if bar >= 16 and bar % 4 == 0 and bar < 26:
			tr.add( tr.music, pad( tr, ["E3", "G3", "B3", "D4"], tr.beat * 16, cutoff=1100 ), tr.at( bar, 0 ), gain=0.6 )
	finish( tr, "kessel", music_reverb=0.18, duck=0.4 )


if __name__ == "__main__":
	which = sys.argv[sys.argv.index( "--" ) + 1:] if "--" in sys.argv else []
	for name, fn in ( ( "underlevel", underlevel ), ( "hyperlane", hyperlane ), ( "outer_rim", outer_rim ), ( "kessel", kessel ) ):
		if not which or name in which:
			fn()
