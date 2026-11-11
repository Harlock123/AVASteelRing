#!/usr/bin/env python3
"""Synthesises AVASteelRing's sound effects and tunes into ../src/AVASteelRing/Sounds as 16-bit mono WAV.

Nothing is sampled: every effect is built here from square, triangle, sine and noise voices, and the tunes are new
compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASteelRing", "Sounds")
RNG = random.Random(1991)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def echo(samples, delay=0.11, feedback=0.35, repeats=3):
    """The voice in a big hall: quieter copies of itself, later and later."""
    d = int(RATE * delay)
    out = samples + [0.0] * (d * repeats)
    for k in range(1, repeats + 1):
        g = feedback ** k
        for i, v in enumerate(samples):
            out[i + d * k] += v * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def line(bpm, notes, kind="pulse", mult=1.0, gain=0.55, release=0.6):
    """A tune as (note, beats), '-' a rest, a list of notes a chord; mult transposes it."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        elif isinstance(note, list):
            out.extend(mix(*[tone(d, hz(n) * mult, kind=kind, release=release, gain=gain / len(note) ** 0.5) for n in note]))
        else:
            out.extend(tone(d, hz(note) * mult, kind=kind, release=release, gain=gain))
    return out


def kick(seconds, gain=1.0):
    return cat(tone(0.07, 150, 45, "sine", attack=0.001, release=1.0, gain=gain), silence(max(0, seconds - 0.07)))


def hat(seconds, gain=0.3):
    return cat(tone(0.03, 9000, kind="noise", attack=0.001, release=2.0, gain=gain), silence(max(0, seconds - 0.03)))


def snare(seconds, gain=0.6):
    return cat(mix(tone(0.12, 3000, kind="noise", attack=0.001, release=1.6, gain=gain),
                   tone(0.08, 220, 160, "tri", attack=0.001, release=1.0, gain=gain * 0.6)), silence(max(0, seconds - 0.12)))



def drums(bpm, bars, fill=False):
    """Four bars of a rock beat: kick on one and three, snare on two and four, eighth-note hats."""
    beat = 60.0 / bpm
    out = []
    for bar in range(bars):
        for b in range(4):
            last = fill and bar == bars - 1 and b >= 2
            if last:
                out += mix(snare(beat * 0.25, 0.4), hat(beat * 0.25)) * 1
                out += snare(beat * 0.25, 0.3) + snare(beat * 0.25, 0.35) + snare(beat * 0.25, 0.45)
                continue
            first = kick(beat * 0.5, 0.7) if b % 2 == 0 else snare(beat * 0.5, 0.5)
            out += mix(first, hat(beat * 0.5))
            out += mix(kick(beat * 0.5, 0.45) if b == 3 else silence(beat * 0.5), hat(beat * 0.5, 0.2))
    return out


def fight_music():
    """The fight: a driving loop in A minor, a riff over a pumping bass."""
    bpm = 144
    riff = [("A4", 0.5), ("A4", 0.5), ("C5", 0.5), ("A4", 0.5), ("D5", 0.5), ("C5", 0.5), ("A4", 0.5), ("G4", 0.5),
            ("A4", 0.5), ("A4", 0.5), ("C5", 0.5), ("A4", 0.5), ("E5", 1), ("D5", 1),
            ("F5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 0.5), ("D5", 0.5), ("E5", 1),
            ("D5", 0.5), ("C5", 0.5), ("B4", 0.5), ("G4", 0.5), ("A4", 2)]
    bass_bar = lambda n: [(n, 0.5)] * 8
    bass = bass_bar("A2") + bass_bar("A2") + bass_bar("F2") + [("G2", 0.5)] * 4 + [("E2", 0.5)] * 4
    lead = line(bpm, riff * 2, "pulse", gain=0.45, release=0.5)
    low = line(bpm, bass * 2, "saw", gain=0.35, release=0.4)
    return mix(lead, low, drums(bpm, 8, fill=True))


def title_tune():
    """The title: a rising call and a steady answer, in D minor, heavy on the drums."""
    bpm = 120
    tune = [("D5", 1), ("A4", 0.5), ("D5", 0.5), ("F5", 1), ("E5", 0.5), ("D5", 0.5),
            ("C5", 1), ("A4", 0.5), ("C5", 0.5), ("E5", 2),
            ("D5", 1), ("A4", 0.5), ("D5", 0.5), ("F5", 1), ("G5", 0.5), ("A5", 0.5),
            ("Bb5", 1), ("A5", 0.5), ("G5", 0.5), ("A5", 2)]
    chords = [(["D3", "A3"], 4), (["C3", "G3"], 4), (["Bb2", "F3"], 4), (["A2", "E3"], 4)]
    return mix(line(bpm, tune, "pulse", gain=0.5), line(bpm, chords, "saw", gain=0.35, release=0.2), drums(bpm, 4, fill=True))


def hit(low, high, length, gain=1.0):
    """A punch landing: a thump under a slap of noise."""
    return mix(tone(length, low * 2, low * 0.6, "sine", attack=0.001, release=1.4, gain=gain),
               tone(length * 0.6, high, high * 0.4, "noise", attack=0.001, release=1.8, gain=gain * 0.9))


def bell(times=1):
    """A ring bell: a bright strike with long overtones."""
    one = mix(tone(0.7, 1240, kind="sine", attack=0.001, release=2.2), tone(0.7, 1240 * 2.76, kind="sine", attack=0.001, release=3.0, gain=0.4),
              tone(0.7, 1240 * 5.4, kind="sine", attack=0.001, release=4.0, gain=0.2))
    out = []
    for _ in range(times):
        out = mix(out + [0.0] * int(RATE * 0.22), [0.0] * len(out) + one) if out else one
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # A swing through the air; a heavy one, slower and lower.
    write("Swish.wav", tone(0.12, 2600, 900, "noise", attack=0.03, release=1.4), 0.25)
    write("SwishHeavy.wav", tone(0.2, 1500, 400, "noise", attack=0.05, release=1.2), 0.35)
    # Hits: light and heavy; a block is a dull knock.
    write("HitLight.wav", hit(110, 3500, 0.12), 0.5)
    write("HitHeavy.wav", mix(hit(70, 2400, 0.25), tone(0.1, 300, 120, "square", attack=0.001, release=1.6, gain=0.25)), 0.7)
    write("Block.wav", mix(tone(0.08, 400, 300, "tri", attack=0.001, release=1.5), tone(0.05, 1500, kind="noise", attack=0.001, release=2.0, gain=0.4)), 0.4)
    # A fireball: a rising roar; any other special: a sharp rising sweep.
    write("Fireball.wav", mix(tone(0.45, 200, 600, "noise", attack=0.02, release=0.9), tone(0.4, 180, 360, "saw", attack=0.02, release=1.0, gain=0.4)), 0.45)
    write("Special.wav", tone(0.22, 400, 1600, "pulse", attack=0.005, release=1.0), 0.25)
    # The super: a bright flash and a chord swelling.
    write("Super.wav", cat(tone(0.08, 2400, 3600, "square", release=0.5),
                           mix(*[tone(0.6, hz(n), kind="saw", attack=0.05, release=0.8, gain=0.5) for n in ("A4", "C#5", "E5", "A5")])), 0.5)
    write("Land.wav", tone(0.07, 160, 70, "sine", attack=0.001, release=1.5), 0.3)
    write("Knockdown.wav", mix(tone(0.35, 90, 40, "sine", attack=0.001, release=1.2), tone(0.25, 800, 200, "noise", attack=0.001, release=1.5, gain=0.6)), 0.6)
    write("Ko.wav", cat(mix(hit(60, 2000, 0.4), tone(0.4, 220, 55, "square", release=1.0, gain=0.4)), echo(tone(0.5, 110, 50, "sine", release=1.2), 0.15, 0.4, 3)), 0.75)
    write("Bell.wav", bell(2), 0.4)
    # The round card: a low gong; the menus: a tick.
    write("Round.wav", mix(tone(1.4, 98, kind="sine", attack=0.005, release=1.6), tone(1.4, 98 * 2.4, kind="sine", attack=0.005, release=2.4, gain=0.5),
                           tone(0.3, 600, 200, "noise", attack=0.002, release=2.0, gain=0.3)), 0.5)
    write("Select.wav", tone(0.05, 1100, kind="pulse", release=0.8), 0.16)
    write("Perfect.wav", line(180, [("A5", 0.5), ("C#6", 0.5), ("E6", 0.5), ("A6", 1.5)], "pulse", gain=0.6), 0.35)
    # Winning the bout: a fanfare; losing it: a falling line.
    write("Victory.wav", mix(line(132, [("A4", 0.5), ("A4", 0.5), ("A4", 0.5), ("D5", 1.5), ("C#5", 0.5), ("D5", 0.5), ("E5", 0.5), (["A5", "C#6"], 3)], "pulse"),
                             line(132, [("D3", 1), ("A2", 1), ("D3", 1), ("A2", 1), ("A2", 3)], "tri")), 0.45)
    write("Defeat.wav", mix(line(90, [("E5", 1), ("D5", 1), ("C5", 1), ("B4", 1), (["A4", "C5"], 3)], "tri", gain=0.6),
                            line(90, [("A2", 2), ("F2", 2), ("E2", 3)], "saw", gain=0.3)), 0.4)
    write("Music.wav", fight_music(), 0.3)
    write("TitleTune.wav", cat(title_tune(), silence(0.5)), 0.32)


if __name__ == "__main__":
    main()
