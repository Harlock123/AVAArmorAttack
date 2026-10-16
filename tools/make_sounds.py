#!/usr/bin/env python3
"""Synthesises AVAArmorAttack's sound effects and tunes into ../src/AVAArmorAttack/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAArmorAttack", "Sounds")
RNG = random.Random(1980)


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


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The jeep's gun: a sharp crack.
    write("Fire.wav", mix(tone(0.14, 1400, 300, "square", release=0.7), tone(0.1, 3000, 800, "noise", release=0.8, gain=0.6)), 0.24)
    # A shell striking a wall: a whine off the brickwork.
    write("Ricochet.wav", mix(tone(0.22, 2600, 1500, "tri", release=0.6, vibrato=0.04, vib_rate=40), tone(0.05, 2000, 800, "noise", release=0.8, gain=0.5)), 0.2)
    # A tank's big gun: a deep boom.
    write("TankFire.wav", mix(tone(0.4, 600, 60, "noise", release=0.6), tone(0.35, 140, 50, "saw", release=0.5, gain=0.7)), 0.4)
    # A hit on the armour: a ringing clang.
    write("Clang.wav", mix(tone(0.4, 880, 860, "square", release=1.2, gain=0.5), tone(0.4, 1320, 1300, "tri", release=1.4, gain=0.5), tone(0.06, 3000, 1000, "noise", release=0.8)), 0.36)
    write("TankBlast.wav", mix(tone(0.9, 1200, 40, "noise", release=0.35), tone(0.8, 220, 30, "saw", release=0.35, gain=0.7)), 0.52)
    # A tank rolling in: tracks grinding.
    write("Rumble.wav", mix(tone(0.7, 90, 70, "noise", attack=0.08, release=0.6), tone(0.7, 55, 50, "pulse", attack=0.08, release=0.6, vibrato=0.2, vib_rate=14, gain=0.4)), 0.3)
    # The gunship: a rocket's whoosh, and coming down in pieces.
    write("Rocket.wav", tone(0.3, 3000, 600, "noise", attack=0.02, release=0.6), 0.22)
    write("GunshipDown.wav", cat(mix(tone(0.6, 1500, 100, "noise", release=0.4), tone(0.6, 700, 60, "square", release=0.4, gain=0.5)), *[tone(0.07, hz(n), kind="pulse", release=0.4) for n in ["G5", "C6", "E6", "G6"]]), 0.42)
    write("Death.wav", cat(mix(tone(0.9, 1200, 40, "noise", release=0.3), tone(0.8, 400, 30, "saw", release=0.3, gain=0.7)), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["D5", "Bb4", "G4"]]), 0.5)
    # A bugle call for a new district, and another when it's clear.
    write("WaveStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.3) for n, d in [("G4", 0.1), ("C5", 0.1), ("E5", 0.1), ("G5", 0.25)]]), 0.3)
    write("WaveClear.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.3) for n, d in [("C5", 0.09), ("E5", 0.09), ("G5", 0.09), ("C6", 0.18), ("G5", 0.09), ("C6", 0.3)]]), 0.36)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "D6", "G5", "D6", "G6"]]), 0.38)

    # The gunship's rotor: a chopping beat, exactly one second, so it loops cleanly (eight blade passes).
    rotor = []
    for i in range(8):
        rotor.extend(tone(1 / 8, 140, 90, "noise", attack=0.002, release=2.5))
    write("Music.wav", rotor, 0.2)

    # The title: a marching theme in G minor (an original composition).
    lead = [("G4", 0.5), ("G4", 0.25), ("G4", 0.25), ("D5", 1), ("C5", 0.5), ("Bb4", 0.5), ("A4", 1),
            ("G4", 0.5), ("Bb4", 0.5), ("D5", 0.5), ("G5", 0.5), ("F5", 1), ("D5", 1),
            ("Eb5", 0.5), ("D5", 0.5), ("C5", 0.5), ("Bb4", 0.5), ("A4", 1), ("F#4", 1),
            ("G4", 0.5), ("D4", 0.5), ("G4", 0.5), ("Bb4", 0.5), ("G4", 2)]
    bass = ([("G2", 0.5), ("D3", 0.5)] * 4 + [("Bb2", 0.5), ("F3", 0.5)] * 4
            + [("C3", 0.5), ("G3", 0.5)] * 2 + [("D3", 0.5), ("A3", 0.5)] * 2 + [("G2", 0.5), ("D3", 0.5)] * 2 + [("G2", 2)])
    write("TitleTune.wav", cat(tune(120, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(150, [("D5", 0.25), ("D5", 0.25), ("G5", 0.5), ("D5", 0.5), ("G5", 0.5), ("Bb5", 0.5), ("G5", 1.5)], [("G2", 1), ("D3", 1), ("G2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(66, [("D5", 1), ("Bb4", 1), ("A4", 1), ("F#4", 1), ("G4", 4)], [("G2", 2), ("Eb2", 2), ("D2", 2), ("G1", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
