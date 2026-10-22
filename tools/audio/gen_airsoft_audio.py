#!/usr/bin/env python3
"""Generates every sound of the Airsoft FPS procedurally (no samples, no downloads).

    python3 -I tools/audio/gen_airsoft_audio.py              # all sounds
    python3 -I tools/audio/gen_airsoft_audio.py shot_ hit_   # only names starting with these

Writes 16-bit WAVs to Assets/Resources/AirsoftAudio/Sfx (mono, 44.1 kHz) and the music / ambience
loops to Assets/Resources/AirsoftAudio/Music (stereo .ogg via ffmpeg if available, else .wav).
Variations are numbered name_1.wav, name_2.wav, ...; the game picks one at random (AirsoftAudio.cs).

Everything here is original work made for this project and released under CC0 1.0.
Each sound has its own fixed random seed, so a re-run reproduces the same files.
"""
import math
import os
import random
import shutil
import subprocess
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dsp import *  # noqa: E402,F401,F403

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'AirsoftAudio')
SFX = os.path.join(OUT, 'Sfx')
MUSIC = os.path.join(OUT, 'Music')

REGISTRY = []   # (name, variations, fn(rng, index) -> signal, kind)


def sound(name, variations=1, kind='sfx'):
    def deco(fn):
        REGISTRY.append((name, variations, fn, kind))
        return fn
    return deco


def rng_for(name, i):
    return random.Random(zlib.crc32(f'{name}#{i}'.encode()))


def J(rng, x, pct=0.05):
    """x jittered by +-pct."""
    return x * rng.uniform(1 - pct, 1 + pct)


# ═════════════════════════════════ weapons: shots ═════════════════════════════════
# Airsoft guns are not loud: a piston / gas "thwack", no gunpowder bang. The owner hears these
# up close (2D); everyone else hears them in 3D with distance low-pass (AirsoftAudio.cs).

@sound('shot_aeg', 4)
def shot_aeg(rng, i):
    """Electric gun: piston slam 'thwack', air 'pff', plastic body, gearbox whir for the next cycle."""
    s = silence(0.24)
    mix(s, burst(0.05, rng, 0.006, 0.0004, 'bp', J(rng, 1900), 0.9), 0, 0.9)          # piston slam
    mix(s, burst(0.08, rng, 0.016, 0.0008, 'lp', J(rng, 3200)), 0.001, 0.55)          # air out of the barrel
    mix(s, thump(0.09, J(rng, 170), 95, 0.02), 0, 0.55)                                # body thump
    mix(s, modes(0.12, [(J(rng, 380), 0.03, 0.35), (J(rng, 640), 0.025, 0.3),
                        (J(rng, 1150), 0.02, 0.25), (J(rng, 2300), 0.012, 0.2)], rng), 0, 0.6)
    # gearbox: gear-tooth buzz spinning down after the shot (overlaps into the next one on full auto)
    gb = osc(0.11, lambda t: J(rng, 118, 0.02) * (1.0 - 0.25 * t / 0.11), 'saw')
    gb = bp(hp(gb, 300), J(rng, 950), 1.2)
    mix(s, env_points(gb, [(0, 0), (0.008, 1), (0.07, 0.6), (0.11, 0)]), 0.0, 0.32)
    mix(s, click(rng, [J(rng, 3100), J(rng, 4700)], 0.006, 0.4, 0.4), J(rng, 0.045, 0.2), 0.25)  # tappet plate
    return normalize(hp(s, 70))


@sound('shot_gbb', 4)
def shot_gbb(rng, i):
    """Gas blowback: sharp gas pop plus the slide slamming back and forward (metal clack-clack)."""
    s = silence(0.22)
    mix(s, burst(0.05, rng, 0.010, 0.0003, 'bp', J(rng, 2600), 0.7), 0, 1.0)          # gas pop
    mix(s, burst(0.05, rng, 0.004, 0.0002, 'hp', 1800), 0, 0.5)                         # crack
    mix(s, thump(0.06, J(rng, 210), 120, 0.014), 0, 0.45)
    mix(s, click(rng, [J(rng, 2100), J(rng, 3400), J(rng, 5200), J(rng, 7900)], 0.018, 0.9), 0.002, 0.55)
    mix(s, click(rng, [J(rng, 1600), J(rng, 2700), J(rng, 4100)], 0.022, 0.9), J(rng, 0.03, 0.1), 0.45)  # slide home
    mix(s, env(hp(noise(0.12, rng), 4200), 0.002, 0.035), 0.004, 0.14)                 # gas hiss
    return normalize(hp(s, 90))


@sound('shot_co2', 3)
def shot_co2(rng, i):
    """CO2 revolver: hammer click, then a deeper, longer gas pop and a cylinder ring."""
    s = silence(0.32)
    mix(s, click(rng, [J(rng, 2900), J(rng, 4600)], 0.008, 0.7, 0.4), 0, 0.35)          # hammer falls
    mix(s, burst(0.08, rng, 0.016, 0.0004, 'bp', J(rng, 1500), 0.6), 0.004, 1.0)
    mix(s, burst(0.06, rng, 0.005, 0.0002, 'hp', 1500), 0.004, 0.5)
    mix(s, thump(0.1, J(rng, 150), 80, 0.03), 0.004, 0.75)
    mix(s, modes(0.2, [(J(rng, 2600), 0.05, 0.25), (J(rng, 4400), 0.035, 0.15)], rng), 0.004, 0.5)
    mix(s, env(hp(noise(0.2, rng), 3500), 0.003, 0.06), 0.01, 0.16)                     # CO2 hiss
    return normalize(hp(s, 70))


def spring_twang(rng, base, tau, sec):
    """A coiled spring's 'boing': a few inharmonic partials with a decaying wobble."""
    out = silence(sec)
    for ratio, a in ((1.0, 1.0), (2.27, 0.55), (3.81, 0.35), (5.6, 0.2)):
        f0 = J(rng, base * ratio, 0.02)
        wob = rng.uniform(22, 34)
        part = osc(sec, lambda t, f0=f0, wob=wob: f0 * (1 + 0.035 * math.exp(-t / 0.06) * math.sin(TAU * wob * t)))
        mix(out, env(part, 0.001, tau / (1 + ratio * 0.3)), 0, a)
    return out


def slide(rng, sec, f, amp=1.0):
    """Metal-on-metal / plastic friction: band-passed noise swelling and fading."""
    return gain(env_points(bp(noise(sec, rng), f, 0.9), [(0, 0), (sec * 0.3, 1), (sec, 0)]), amp)


def bolt_cycle(rng, t0, speed=1.0):
    """Spring bolt action: lift, pull back, push forward, lock."""
    s = silence(t0 + 0.6 / speed)
    t = t0
    mix(s, click(rng, [J(rng, 1900), J(rng, 3100), J(rng, 4800)], 0.012, 0.8), t, 0.5)
    t += 0.1 / speed
    mix(s, slide(rng, 0.07 / speed, J(rng, 1700)), t, 0.35)
    mix(s, click(rng, [J(rng, 1500), J(rng, 2600)], 0.014, 0.8), t + 0.07 / speed, 0.55)
    t += 0.17 / speed
    mix(s, slide(rng, 0.06 / speed, J(rng, 2000)), t, 0.35)
    mix(s, click(rng, [J(rng, 1700), J(rng, 2900)], 0.012, 0.8), t + 0.06 / speed, 0.5)
    t += 0.12 / speed
    mix(s, click(rng, [J(rng, 1300), J(rng, 2200), J(rng, 3600)], 0.016, 1.0), t, 0.65)
    mix(s, thump(0.05, 260, 180, 0.012), t, 0.3)
    return s


@sound('shot_shotgun', 3)
def shot_shotgun(rng, i):
    """Spring pump shotgun (3 BBs): heavy piston slam with a spring twang, then the pump (chk-chk)."""
    s = silence(0.8)
    mix(s, thump(0.16, J(rng, 120), 62, 0.045), 0, 1.0)
    mix(s, burst(0.08, rng, 0.02, 0.0005, 'lp', J(rng, 2300)), 0, 0.75)
    mix(s, burst(0.04, rng, 0.006, 0.0003, 'bp', J(rng, 950), 0.9), 0, 0.6)
    mix(s, spring_twang(rng, J(rng, 520), 0.11, 0.35), 0.003, 0.22)
    mix(s, burst(0.1, rng, 0.03, 0.001, 'lp', 3600), 0.002, 0.4)
    # pump back ...
    t = J(rng, 0.36, 0.04)
    mix(s, slide(rng, 0.05, J(rng, 1400)), t, 0.4)
    mix(s, click(rng, [J(rng, 2200), J(rng, 3600)], 0.014, 0.8), t + 0.05, 0.55)
    mix(s, thump(0.05, 320, 200, 0.015), t + 0.05, 0.35)
    # ... and forward
    t += 0.17
    mix(s, slide(rng, 0.045, J(rng, 1800)), t, 0.35)
    mix(s, click(rng, [J(rng, 2500), J(rng, 4000)], 0.012, 0.8), t + 0.045, 0.6)
    mix(s, thump(0.05, 380, 230, 0.012), t + 0.045, 0.3)
    return normalize(hp(s, 50))


def spring_rifle(rng, crack, twang_base, bolt_at, bolt_speed):
    s = silence(bolt_at + 0.7)
    mix(s, burst(0.04, rng, 0.009, 0.0002, 'bp', J(rng, 2800), 0.8), 0, crack)          # sharp crack
    mix(s, burst(0.03, rng, 0.004, 0.0001, 'hp', 2500), 0, crack * 0.6)
    mix(s, thump(0.18, J(rng, 130), 70, 0.05), 0, 0.95)                                  # piston
    mix(s, spring_twang(rng, J(rng, twang_base), 0.2, 0.5), 0.002, 0.25)
    whoosh = burst(0.2, rng, 0.05, 0.001, 'lp', 5000)
    mix(s, whoosh, 0.001, 0.5)
    s = echo(s, J(rng, 0.09), 0.15, 0.18, 2500, 0.1)                                    # outdoor slapback
    mix(s, bolt_cycle(rng, bolt_at, bolt_speed), 0, 1.0)
    return normalize(hp(s, 50))


@sound('shot_sniper', 3)
def shot_sniper(rng, i):
    """Upgraded spring sniper (L96): the loudest gun in the game, with a slow bolt cycle after it."""
    return spring_rifle(rng, 1.0, 430, J(rng, 0.5, 0.04), 0.9)


@sound('shot_springbolt', 3)
def shot_springbolt(rng, i):
    """Lighter spring bolt rifle: same family, less crack, quicker bolt."""
    return spring_rifle(rng, 0.7, 560, J(rng, 0.3, 0.04), 1.4)


# ═════════════════════════════════ weapons: handling ═════════════════════════════════

@sound('reload_out', 3)
def reload_out(rng, i):
    s = silence(0.35)
    mix(s, click(rng, [J(rng, 3200), J(rng, 5100)], 0.008, 0.8), 0, 0.6)               # mag release
    mix(s, slide(rng, J(rng, 0.12), J(rng, 1200)), 0.025, 0.5)
    mix(s, click(rng, [J(rng, 900), J(rng, 1700)], 0.02, 0.8, 0.3, 800), J(rng, 0.15), 0.35)  # mag clears the well
    return normalize(trim_tail(s))


@sound('reload_in', 3)
def reload_in(rng, i):
    s = silence(0.32)
    mix(s, slide(rng, J(rng, 0.08), J(rng, 1000)), 0, 0.45)
    t = J(rng, 0.085)
    mix(s, thump(0.06, 260, 160, 0.018), t, 0.6)
    mix(s, click(rng, [J(rng, 1800), J(rng, 2900), J(rng, 4300)], 0.012, 1.0), t, 0.75)
    return normalize(trim_tail(s))


@sound('reload_rack', 2)
def reload_rack(rng, i):
    """Slide / charging handle pulled and released (end of a gas gun reload)."""
    s = silence(0.3)
    mix(s, slide(rng, 0.05, J(rng, 1900)), 0, 0.4)
    mix(s, click(rng, [J(rng, 2400), J(rng, 3800)], 0.01, 0.8), 0.05, 0.5)
    t = J(rng, 0.13)
    mix(s, click(rng, [J(rng, 1700), J(rng, 2800), J(rng, 4400)], 0.016, 1.0), t, 0.8)
    mix(s, thump(0.05, 300, 200, 0.012), t, 0.35)
    return normalize(trim_tail(s))


@sound('dryfire', 2)
def dryfire(rng, i):
    s = silence(0.08)
    mix(s, click(rng, [J(rng, 2400), J(rng, 4100)], 0.006, 0.8, 0.5), 0, 1.0)
    mix(s, thump(0.03, 400, 260, 0.006), 0, 0.25)
    return normalize(trim_tail(s), 0.7)


@sound('switch', 3)
def weapon_switch(rng, i):
    """Gun drawn: sling / cloth rustle, then a light clack of the gun settling in the hands."""
    s = silence(0.4)
    r = bp(noise(0.22, rng), J(rng, 2200), 0.6)
    am = [abs(math.sin(TAU * 11 * (k / SR) + rng.uniform(0, 1))) ** 2 for k in range(len(r))]
    r = env_points([a * b for a, b in zip(r, am)], [(0, 0), (0.05, 1), (0.22, 0)])
    mix(s, r, 0, 0.5)
    t = J(rng, 0.17)
    mix(s, click(rng, [J(rng, 1500), J(rng, 2600)], 0.014, 0.9, 0.4), t, 0.6)
    mix(s, thump(0.05, 240, 150, 0.014), t, 0.4)
    return normalize(trim_tail(s))


# ═════════════════════════════════ feedback ═════════════════════════════════

@sound('hitmarker', 1)
def hitmarker(rng, i):
    s = modes(0.06, [(2950, 0.011, 1.0), (5200, 0.007, 0.45), (8100, 0.004, 0.25)])
    mix(s, burst(0.004, rng, 0.0008, 0.0001, 'hp', 4000), 0, 0.4)
    return normalize(s, 0.8)


@sound('killconfirm', 1)
def killconfirm(rng, i):
    s = modes(0.12, [(2600, 0.02, 1.0), (5200, 0.01, 0.3)])
    mix(s, modes(0.2, [(3480, 0.05, 1.0), (6960, 0.02, 0.3), (10440, 0.01, 0.12)]), 0.065, 0.9)
    mix(s, thump(0.06, 220, 140, 0.015), 0.065, 0.3)
    return normalize(s, 0.8)


@sound('impact_body', 4)
def impact_body(rng, i):
    """A BB hitting a person (clothing / plate carrier): a dry 'thwack'."""
    s = silence(0.18)
    mix(s, burst(0.04, rng, 0.011, 0.0003, 'bp', J(rng, 1100, 0.15), 0.8), 0, 1.0)
    mix(s, thump(0.07, J(rng, 190), 110, 0.018), 0, 0.6)
    mix(s, burst(0.01, rng, 0.0015, 0.0001, 'hp', 3000), 0, 0.35)
    return normalize(trim_tail(s))


@sound('hurt', 3)
def hurt(rng, i):
    """You got hit (2D, for the victim): a stinging slap close to the ear plus a low thud."""
    s = silence(0.3)
    mix(s, burst(0.05, rng, 0.02, 0.0003, 'bp', J(rng, 900), 1.0), 0, 1.0)
    mix(s, thump(0.2, J(rng, 95), 55, 0.06), 0, 0.85)
    mix(s, env(hp(noise(0.08, rng), 5000), 0.001, 0.025), 0, 0.25)
    return normalize(trim_tail(lp(s, 6000)))


def glottal(sec, f0_fn, rng, tilt=0.85, jitter=0.01):
    """Voiced source: band-limited pulse train (harmonic sum with spectral tilt) + slight jitter."""
    out = [0.0] * N(sec)
    ph = 0.0
    jit = 0.0
    for k in range(len(out)):
        if k % 220 == 0:
            jit = rng.uniform(-jitter, jitter)
        f0 = f0_fn(k / SR) * (1 + jit)
        nh = int(5200 / f0)
        v = 0.0
        for h in range(1, nh + 1):
            v += math.sin(TAU * ph * h) / (h ** tilt)
        out[k] = v
        ph = (ph + f0 / SR) % 1.0
    return out


def formants(sig, fs):
    out = [0.0] * len(sig)
    for f, bw, a in fs:
        mix(out, klatt(sig, f, bw), 0, a)
    return out


@sound('hit_call', 4)
def hit_call(rng, i):
    """The airsoft 'HIT!' shout (Milsim / Medic): formant-synthesised /h/ + /ɪ/ + /t/, four voices."""
    voice = [0.82, 1.0, 1.22, 1.5][i - 1]
    fscale = [0.96, 1.0, 1.06, 1.14][i - 1]
    f0b = 205 * voice
    F = [(500 * fscale, 90), (1820 * fscale, 120), (2520 * fscale, 170), (3500 * fscale, 260)]
    vowel_amp = [1.0, 0.55, 0.32, 0.12]
    t_h, t_v = 0.07, 0.19                   # /h/ length, vowel length

    def f0(t):
        x = t / t_v
        return f0b * (0.96 + 0.12 * math.sin(math.pi * min(1.0, x * 1.4)) - 0.1 * x)

    v = glottal(t_v, f0, rng, 0.8)
    v = formants(v, [(f, bw, a) for (f, bw), a in zip(F, vowel_amp)])
    v = env_points(v, [(0, 0), (0.018, 1), (t_v - 0.05, 0.85), (t_v, 0)])
    h = formants(noise(t_h + 0.04, rng), [(f, bw * 1.6, a) for (f, bw), a in zip(F, vowel_amp)])
    h = env_points(h, [(0, 0), (t_h * 0.6, 1), (t_h + 0.04, 0)])
    s = silence(0.6)
    mix(s, normalize(h, 0.22), 0, 1.0)
    mix(s, normalize(v, 1.0), t_h, 1.0)
    t_t = t_h + t_v + 0.035                 # closure, then the /t/ release
    burst_t = bp(noise(0.05, rng), 4300 * fscale, 0.9)
    mix(s, env(burst_t, 0.0005, 0.007), t_t, 0.45)
    asp = formants(noise(0.06, rng), [(F[1][0], 300, 0.4), (F[2][0], 400, 0.4), (4400, 600, 0.5)])
    mix(s, env_points(normalize(asp, 1.0), [(0, 0.8), (0.06, 0)]), t_t + 0.006, 0.12)
    s = hp(s, 120)
    s = echo(s, 0.11, 0.2, 0.12, 2200, 0.25)  # outdoors
    return normalize(trim_tail(s))


@sound('eliminated', 1)
def eliminated(rng, i):
    """You're out (2D, victim): muffled thud and a falling tone."""
    s = silence(1.4)
    mix(s, thump(0.4, 90, 40, 0.12), 0, 1.0)
    tone = env_points(sweep(1.0, 620, 140, 'tri', 1.6), [(0, 0), (0.02, 1), (0.7, 0.5), (1.0, 0)])
    trem = [1 - 0.35 * (0.5 + 0.5 * math.sin(TAU * 7 * k / SR)) for k in range(len(tone))]
    mix(s, lp([a * b for a, b in zip(tone, trem)], 1800), 0.02, 0.45)
    s = reverb(s, 0.3, 0.7, 0.5, 0.8)
    return normalize(trim_tail(s))


@sound('down', 3)
def down(rng, i):
    """Someone else goes down (3D): body and gear hitting the ground."""
    s = silence(0.55)
    mix(s, thump(0.25, J(rng, 85), 45, 0.07), J(rng, 0.03), 1.0)
    mix(s, burst(0.08, rng, 0.02, 0.003, 'lp', 1500), 0.03, 0.5)
    for _ in range(5):
        mix(s, click(rng, [rng.uniform(1200, 3500), rng.uniform(3000, 6000)], 0.008, 0.6, 0.4),
            rng.uniform(0.04, 0.22), rng.uniform(0.1, 0.3))
    return normalize(trim_tail(s))


@sound('respawn', 1)
def respawn(rng, i):
    s = env_points(svf(noise(0.6, rng), lambda t: 400 * (3200 / 400) ** min(1.0, t / 0.45), 2.0, 'bp'),
                   [(0, 0), (0.35, 1), (0.6, 0)])
    mix(s, modes(0.5, [(1318, 0.15, 1.0), (2637, 0.08, 0.3)]), 0.4, 0.35)
    return normalize(trim_tail(reverb(s, 0.25, 0.6, 0.4, 0.6)), 0.7)


# ═════════════════════════════════ BB impacts ═════════════════════════════════

@sound('impact_concrete', 4)
def impact_concrete(rng, i):
    """6 mm BB on stone / asphalt: a hard tick, then the BB bouncing away (tic.. tic.tic)."""
    s = silence(0.4)
    t, a = 0.0, 1.0
    for b in range(rng.randint(2, 4)):
        mix(s, click(rng, [J(rng, 4800, 0.15), J(rng, 7200, 0.1)], 0.004, 1.0, 0.8, 3000), t, a)
        t += rng.uniform(0.06, 0.12) * (0.8 ** b)
        a *= rng.uniform(0.3, 0.45)
    return normalize(trim_tail(hp(s, 900)))


@sound('impact_metal', 4)
def impact_metal(rng, i):
    """BB on sheet metal (containers, cars, racks): a ringing 'tink' / 'tonk'."""
    s = silence(0.5)
    base = rng.uniform(780, 1300) if i <= 2 else rng.uniform(1700, 2600)
    parts = [(base * r * rng.uniform(0.98, 1.02), rng.uniform(0.06, 0.22) / (1 + k * 0.25), 1.0 / (1 + k * 0.5))
             for k, r in enumerate((1.0, 1.59, 2.14, 2.83, 3.71, 4.6))]
    mix(s, modes(0.5, parts, rng), 0, 0.8)
    mix(s, burst(0.006, rng, 0.0012, 0.0001, 'hp', 2500), 0, 0.6)
    mix(s, click(rng, [J(rng, 4800), J(rng, 6900)], 0.004, 0.6), rng.uniform(0.09, 0.15), 0.18)   # bounce
    return normalize(trim_tail(hp(s, 400)))


@sound('impact_wood', 4)
def impact_wood(rng, i):
    """BB on wood (crates, pallets, doors): a short hollow 'tok'."""
    s = silence(0.2)
    parts = [(J(rng, 720, 0.15), 0.025, 1.0), (J(rng, 1350, 0.12), 0.018, 0.6), (J(rng, 2150, 0.1), 0.012, 0.4)]
    mix(s, modes(0.2, parts, rng), 0, 0.9)
    mix(s, burst(0.02, rng, 0.004, 0.0002, 'lp', 2500), 0, 0.5)
    mix(s, click(rng, [J(rng, 3500)], 0.004, 0.5), rng.uniform(0.06, 0.1), 0.12)
    return normalize(trim_tail(hp(s, 200)))


@sound('impact_glass', 3)
def impact_glass(rng, i):
    """BB on glass / screens: a bright 'tink' (airsoft BBs don't break windows)."""
    s = silence(0.45)
    base = rng.uniform(2900, 3500)
    mix(s, modes(0.45, [(base, 0.16, 1.0), (base * 1.72, 0.1, 0.6), (base * 2.61, 0.06, 0.35),
                        (base * 3.3, 0.04, 0.2)], rng), 0, 0.7)
    mix(s, burst(0.004, rng, 0.0008, 0.0001, 'hp', 5000), 0, 0.6)
    return normalize(trim_tail(hp(s, 1500)))


@sound('impact_soft', 4)
def impact_soft(rng, i):
    """BB into grass, dirt, sandbags, cardboard: a dull 'thup'."""
    s = silence(0.12)
    mix(s, burst(0.06, rng, 0.01, 0.0004, 'lp', J(rng, 1300, 0.2)), 0, 1.0)
    mix(s, env(bp(noise(0.08, rng), 3500, 0.7), 0.002, 0.012), 0.002, 0.25)
    return normalize(trim_tail(hp(s, 120)))


# ═════════════════════════════════ footsteps ═════════════════════════════════

def heel_toe(rng, heel, toe, gap):
    s = silence(gap + 0.2)
    mix(s, heel, 0, 1.0)
    mix(s, toe, gap, 1.0)
    return s


@sound('step_concrete', 6)
def step_concrete(rng, i):
    """Boots on concrete / asphalt: heel thump + scuff, then the toe with a little grit."""
    heel = mix(gain(thump(0.05, J(rng, 130), 85, 0.012), 0.45), burst(0.04, rng, 0.009, 0.0008, 'lp', J(rng, 2600)), 0, 1.0)
    toe = burst(0.05, rng, 0.008, 0.001, 'bp', J(rng, 2300, 0.15), 0.8)
    grit = env(hp([v if rng.random() < 0.08 else 0.0 for v in noise(0.05, rng)], 3000), 0.002, 0.02)
    toe = mix(toe, grit, 0.005, 0.4)
    return normalize_rms(trim_tail(hp(heel_toe(rng, heel, gain(toe, J(rng, 0.7, 0.2)), J(rng, 0.05, 0.2)), 90)))


@sound('step_grass', 6)
def step_grass(rng, i):
    """Boots on grass / dirt: a soft thump with a crunchy rustle."""
    sec = 0.16
    r = bp(noise(sec, rng), J(rng, 3400, 0.15), 0.6)
    crackle = [1.0 + (4.0 if rng.random() < 0.015 else 0.0) for _ in range(len(r))]
    r = env_points([a * b for a, b in zip(r, crackle)], [(0, 0), (0.015, 1), (0.06, 0.5), (sec, 0)])
    s = silence(0.25)
    mix(s, r, 0, 0.7)
    mix(s, thump(0.05, J(rng, 110), 75, 0.015), 0, 0.3)
    mix(s, burst(0.08, rng, 0.02, 0.003, 'lp', 900), 0, 0.35)
    mix(s, gain(r, 0.4), J(rng, 0.05, 0.2), 1.0)
    return normalize_rms(trim_tail(hp(s, 60)))


@sound('step_metal', 5)
def step_metal(rng, i):
    """Boots on metal (container roofs, ramps, catwalks): thump with a hollow ring."""
    s = silence(0.4)
    mix(s, thump(0.08, J(rng, 110), 70, 0.02), 0, 0.7)
    ring = modes(0.35, [(J(rng, 190), 0.12, 1.0), (J(rng, 420), 0.09, 0.7), (J(rng, 770), 0.07, 0.5),
                        (J(rng, 1240), 0.05, 0.35), (J(rng, 2100), 0.03, 0.2)], rng)
    mix(s, ring, 0, 0.45)
    mix(s, click(rng, [J(rng, 2600), J(rng, 3900)], 0.01, 0.6, 0.5), 0, 0.3)
    mix(s, gain(click(rng, [J(rng, 2200)], 0.01, 0.5, 0.5), 0.6), J(rng, 0.05, 0.2), 0.3)
    return normalize_rms(trim_tail(hp(s, 60)))


@sound('step_wood', 5)
def step_wood(rng, i):
    """Boots on wood (pallets, floors, crates): a hollow knock."""
    s = silence(0.3)
    mix(s, thump(0.07, J(rng, 120), 75, 0.018), 0, 0.6)
    mix(s, modes(0.25, [(J(rng, 170), 0.05, 1.0), (J(rng, 340), 0.035, 0.6), (J(rng, 640), 0.02, 0.4),
                        (J(rng, 1150), 0.012, 0.25)], rng), 0, 0.6)
    mix(s, burst(0.03, rng, 0.006, 0.0006, 'bp', 2000, 0.8), 0, 0.3)
    mix(s, gain(modes(0.15, [(J(rng, 300), 0.03, 1.0), (J(rng, 900), 0.015, 0.4)], rng), 0.5), J(rng, 0.05, 0.2), 0.6)
    return normalize_rms(trim_tail(hp(s, 60)))


@sound('jump', 3)
def jump(rng, i):
    """Push-off: gear rustle and a short exhale."""
    s = silence(0.3)
    r = bp(noise(0.2, rng), J(rng, 2500), 0.6)
    am = [abs(math.sin(TAU * 13 * (k / SR) + rng.uniform(0, 1))) for k in range(len(r))]
    mix(s, env_points([a * b for a, b in zip(r, am)], [(0, 0), (0.03, 1), (0.2, 0)]), 0, 0.5)
    breath = formants(noise(0.16, rng), [(J(rng, 650), 200, 1.0), (J(rng, 1200), 250, 0.6)])
    mix(s, env_points(normalize(breath, 1.0), [(0, 0), (0.03, 1), (0.16, 0)]), 0.01, 0.3)
    mix(s, thump(0.05, 100, 65, 0.015), 0, 0.4)
    return normalize(trim_tail(hp(s, 60)), 0.8)


@sound('land', 3)
def land(rng, i):
    """Landing: heavy double footfall and a gear rattle."""
    s = silence(0.45)
    mix(s, thump(0.2, J(rng, 80), 42, 0.05), 0, 1.0)
    mix(s, burst(0.06, rng, 0.015, 0.001, 'lp', 2200), 0, 0.6)
    mix(s, thump(0.12, J(rng, 95), 50, 0.03), J(rng, 0.035, 0.2), 0.6)
    for _ in range(4):
        mix(s, click(rng, [rng.uniform(1500, 3500), rng.uniform(3500, 6500)], 0.007, 0.6, 0.4),
            rng.uniform(0.03, 0.14), rng.uniform(0.08, 0.2))
    return normalize(trim_tail(hp(s, 40)))


# ═════════════════════════════════ objectives / match ═════════════════════════════════

def whistle_blast(rng, sec, f=2900.0, amp_pts=None):
    """Referee pea whistle: a ~3 kHz tone warbled by the pea (~30 Hz) plus breath noise."""
    trill = rng.uniform(28, 36)
    tone = osc(sec, lambda t: f * (1 + 0.012 * math.sin(TAU * trill * t)))
    am = [0.6 + 0.4 * math.sin(TAU * trill * k / SR) for k in range(len(tone))]
    h2 = osc(sec, lambda t: 2 * f * (1 + 0.012 * math.sin(TAU * trill * t)))
    s = [a * (x + 0.25 * y) for a, x, y in zip(am, tone, h2)]
    mix(s, gain(bp(noise(sec, rng), f, 1.5), 0.25))
    pts = amp_pts or [(0, 0), (0.02, 1), (sec - 0.06, 0.9), (sec, 0)]
    return env_points(s, pts)


@sound('whistle', 1)
def whistle(rng, i):
    """Round / match start: one sharp blast."""
    return normalize(trim_tail(reverb(whistle_blast(rng, 0.55), 0.18, 0.6, 0.4, 0.5)), 0.8)


@sound('whistle_end', 1)
def whistle_end(rng, i):
    """End of round / match: three blasts, the last one long."""
    s = silence(1.6)
    mix(s, whistle_blast(rng, 0.16), 0, 1.0)
    mix(s, whistle_blast(rng, 0.16), 0.24, 1.0)
    mix(s, whistle_blast(rng, 0.7), 0.48, 1.0)
    return normalize(trim_tail(reverb(s, 0.18, 0.6, 0.4, 0.5)), 0.8)


def beep(sec, f, soft=0.004):
    s = osc(sec, lambda t: f, 'sine')
    mix(s, gain(osc(sec, lambda t: 2 * f, 'sine'), 0.15))
    return env_points(s, [(0, 0), (soft, 1), (sec - soft * 2, 1), (sec, 0)])


@sound('countdown', 1)
def countdown(rng, i):
    return normalize(beep(0.12, 1046.5), 0.6)


@sound('countdown_go', 1)
def countdown_go(rng, i):
    return normalize(beep(0.4, 1568.0, 0.006), 0.6)


@sound('clock_tick', 1)
def clock_tick(rng, i):
    s = modes(0.06, [(1800, 0.012, 1.0), (3700, 0.006, 0.3)])
    return normalize(s, 0.5)


def bell(sec, f, tau=0.6, bright=1.0):
    """FM-ish bell from inharmonic partials."""
    return modes(sec, [(f, tau, 1.0), (f * 2.0, tau * 0.6, 0.45 * bright), (f * 2.76, tau * 0.4, 0.3 * bright),
                       (f * 5.4, tau * 0.2, 0.12 * bright)])


def pluck_chord(rng, notes, step, sec, tau=0.5):
    s = silence(sec)
    for k, f in enumerate(notes):
        mix(s, bell(sec - k * step, f, tau), k * step, 1.0)
    return s


def hz(note):
    """'A4' / 'C#5' / 'Eb3' -> Hz."""
    names = {'C': -9, 'D': -7, 'E': -5, 'F': -4, 'G': -2, 'A': 0, 'B': 2}
    n = names[note[0]]
    rest = note[1:]
    if rest.startswith('#'):
        n += 1; rest = rest[1:]
    elif rest.startswith('b'):
        n -= 1; rest = rest[1:]
    octave = int(rest)
    return 440.0 * 2 ** ((n + (octave - 4) * 12) / 12)


@sound('obj_good', 1)
def obj_good(rng, i):
    s = pluck_chord(rng, [hz('C5'), hz('E5'), hz('G5'), hz('C6')], 0.07, 1.0, 0.35)
    return normalize(trim_tail(reverb(s, 0.25, 0.65, 0.4, 0.6)), 0.7)


@sound('obj_bad', 1)
def obj_bad(rng, i):
    s = silence(0.9)
    for k, n in enumerate(('A4', 'F4')):
        tone = lp(osc(0.22, lambda t, f=hz(n): f, 'square'), 2200)
        mix(s, env_points(tone, [(0, 0), (0.01, 1), (0.18, 0.8), (0.22, 0)]), k * 0.24, 0.6)
    return normalize(trim_tail(reverb(s, 0.2, 0.6, 0.4, 0.5)), 0.65)


@sound('obj_neutral', 1)
def obj_neutral(rng, i):
    s = bell(0.7, hz('E5'), 0.3)
    return normalize(trim_tail(reverb(s, 0.2, 0.6, 0.4, 0.5)), 0.6)


@sound('progress_tick', 1)
def progress_tick(rng, i):
    s = beep(0.05, 1975.5, 0.003)
    return normalize(s, 0.45)


def saw_chord(sec, notes, cutoff=1800.0, detune=0.004):
    s = silence(sec)
    for f in notes:
        for d in (-detune, detune):
            mix(s, osc(sec, lambda t, ff=f * (1 + d): ff, 'saw'), 0, 0.5)
    return lp(s, cutoff)


@sound('flag_capture_good', 1)
def flag_capture_good(rng, i):
    s = silence(1.8)
    for k, n in enumerate(('G4', 'C5', 'E5')):
        mix(s, env_points(saw_chord(0.16, [hz(n)], 2600), [(0, 0), (0.01, 1), (0.16, 0)]), k * 0.12, 0.5)
    chord = saw_chord(1.2, [hz('C4'), hz('G4'), hz('C5'), hz('E5'), hz('G5')], 2400)
    mix(s, env_points(chord, [(0, 0), (0.02, 1), (0.5, 0.7), (1.2, 0)]), 0.36, 0.55)
    mix(s, bell(1.2, hz('G5'), 0.5), 0.36, 0.35)
    mix(s, thump(0.3, 120, 50, 0.1), 0.36, 0.6)
    return normalize(trim_tail(reverb(s, 0.25, 0.7, 0.4, 0.8)), 0.75)


@sound('flag_capture_bad', 1)
def flag_capture_bad(rng, i):
    s = silence(1.6)
    for k, n in enumerate(('E5', 'C5', 'A4')):
        mix(s, env_points(saw_chord(0.2, [hz(n)], 1800), [(0, 0), (0.01, 1), (0.2, 0)]), k * 0.15, 0.5)
    chord = saw_chord(1.0, [hz('A3'), hz('E4'), hz('A4'), hz('C5')], 1500)
    mix(s, env_points(chord, [(0, 0), (0.03, 1), (0.4, 0.6), (1.0, 0)]), 0.45, 0.55)
    mix(s, thump(0.35, 90, 40, 0.12), 0.45, 0.7)
    return normalize(trim_tail(reverb(s, 0.25, 0.7, 0.4, 0.8)), 0.75)


@sound('flag_pickup', 1)
def flag_pickup(rng, i):
    """Cloth flap (3D at the flag)."""
    s = silence(0.45)
    r = bp(noise(0.35, rng), 1600, 0.5)
    am = [0.3 + 0.7 * abs(math.sin(TAU * 9 * (k / SR))) ** 3 for k in range(len(r))]
    mix(s, env_points([a * b for a, b in zip(r, am)], [(0, 0), (0.03, 1), (0.35, 0)]), 0, 1.0)
    return normalize(trim_tail(hp(s, 200)), 0.7)


@sound('bomb_plant', 1)
def bomb_plant(rng, i):
    """Keypad code, a clunk as it's set down, then two 'armed' beeps."""
    s = silence(1.2)
    for k in range(4):
        mix(s, beep(0.05, hz('E6') * (1 + 0.06 * (k % 2)), 0.003), k * 0.09, 0.4)
    mix(s, thump(0.12, 160, 90, 0.03), 0.42, 0.7)
    mix(s, click(rng, [1400, 2600], 0.015, 0.8), 0.42, 0.5)
    mix(s, beep(0.12, 2637.0), 0.62, 0.55)
    mix(s, beep(0.12, 2637.0), 0.8, 0.55)
    return normalize(trim_tail(s), 0.8)


@sound('bomb_beep', 1)
def bomb_beep(rng, i):
    s = lp(osc(0.11, lambda t: 2637.0, 'square'), 5000)
    return normalize(env_points(s, [(0, 0), (0.004, 1), (0.1, 1), (0.11, 0)]), 0.6)


@sound('bomb_defuse', 1)
def bomb_defuse(rng, i):
    """Wire snip and a power-down tone."""
    s = silence(1.0)
    mix(s, click(rng, [3300, 5600, 8100], 0.006, 1.0, 1.0, 3000), 0, 0.8)
    tone = env_points(sweep(0.7, 1500, 180, 'tri', 1.0), [(0, 0), (0.01, 1), (0.7, 0)])
    mix(s, lp(tone, 3000), 0.08, 0.5)
    return normalize(trim_tail(s), 0.8)


@sound('bomb_explode', 1)
def bomb_explode(rng, i):
    """Big pyro / gas charge: crack, low boom, rumble and falling debris."""
    s = silence(3.5)
    mix(s, burst(0.2, rng, 0.03, 0.0003), 0, 0.9)
    mix(s, tanh_sat(thump(1.6, 62, 28, 0.55, 0.002), 2.5), 0, 1.0)
    rum = env(lp(brown(3.0, rng), 260), 0.01, 0.9)
    mix(s, rum, 0.01, 0.9)
    mid = env(lp(noise(1.5, rng), 1400), 0.002, 0.25)
    mix(s, mid, 0, 0.6)
    for _ in range(40):
        t = rng.uniform(0.15, 2.0)
        mix(s, click(rng, [rng.uniform(800, 2500), rng.uniform(2500, 5000)], 0.01, 0.5, 0.5, 1500), t,
            rng.uniform(0.04, 0.18) * math.exp(-t / 0.9))
    s = reverb(s, 0.3, 0.85, 0.5, 1.5)
    return normalize(trim_tail(fade(s, 0, 0.3), 0.0008))


@sound('revive', 1)
def revive(rng, i):
    """Medic patches you up: velcro rip and a rising chime."""
    s = silence(1.1)
    rip = hp(noise(0.28, rng), 1500)
    am = [1.0 if rng.random() < 0.35 else 0.15 for _ in range(len(rip))]
    am = onepole_lp(am, 900)
    mix(s, env_points([a * b for a, b in zip(rip, am)], [(0, 0), (0.02, 1), (0.25, 0.8), (0.28, 0)]), 0, 0.6)
    mix(s, bell(0.8, hz('C5'), 0.3), 0.3, 0.45)
    mix(s, bell(0.7, hz('G5'), 0.35), 0.42, 0.45)
    return normalize(trim_tail(reverb(s, 0.2, 0.6, 0.4, 0.6)), 0.75)


@sound('vip_extract', 1)
def vip_extract(rng, i):
    """Radio squelch and a rising synth chord."""
    s = silence(2.0)
    sq = env(bp(noise(0.12, rng), 2400, 0.8), 0.002, 0.04)
    mix(s, sq, 0, 0.5)
    chord = saw_chord(1.5, [hz('D4'), hz('A4'), hz('D5'), hz('F#5')], 12000)
    swept = svf(chord, lambda t: 500 * (4500 / 500) ** min(1.0, t / 1.0), 0.9, 'lp')
    mix(s, env_points(swept, [(0, 0), (0.6, 1), (1.1, 0.8), (1.5, 0)]), 0.12, 0.6)
    mix(s, bell(1.0, hz('A5'), 0.5), 0.75, 0.3)
    return normalize(trim_tail(reverb(s, 0.25, 0.7, 0.4, 0.7)), 0.75)


@sound('round_win', 1)
def round_win(rng, i):
    s = silence(1.6)
    chord = saw_chord(1.3, [hz('D4'), hz('F#4'), hz('A4'), hz('D5')], 2600)
    mix(s, env_points(chord, [(0, 0), (0.02, 1), (0.6, 0.6), (1.3, 0)]), 0, 0.6)
    mix(s, bell(1.2, hz('A5'), 0.5), 0.05, 0.3)
    mix(s, thump(0.3, 110, 45, 0.1), 0, 0.6)
    return normalize(trim_tail(reverb(s, 0.25, 0.7, 0.4, 0.7)), 0.75)


@sound('round_lose', 1)
def round_lose(rng, i):
    s = silence(1.6)
    chord = saw_chord(1.3, [hz('D4'), hz('F4'), hz('A4'), hz('C5')], 1300)
    mix(s, env_points(chord, [(0, 0), (0.04, 1), (0.6, 0.55), (1.3, 0)]), 0, 0.6)
    mix(s, thump(0.35, 90, 38, 0.12), 0, 0.6)
    return normalize(trim_tail(reverb(s, 0.25, 0.7, 0.4, 0.7)), 0.75)


def stinger(rng, chords, bar, cutoff, lead=None, drums=True):
    s = silence(bar * len(chords) + 2.0)
    for k, notes in enumerate(chords):
        last = k == len(chords) - 1
        sec = bar * (2.2 if last else 1.0)
        c = saw_chord(sec, [hz(n) for n in notes], cutoff)
        mix(s, env_points(c, [(0, 0), (0.015, 1), (sec * 0.5, 0.7), (sec, 0)]), k * bar, 0.5)
        if drums:
            mix(s, thump(0.3, 120, 45, 0.09), k * bar, 0.7)
    if lead:
        for t, n in lead:
            mix(s, bell(0.9, hz(n), 0.45), t * bar, 0.35)
    return normalize(trim_tail(reverb(s, 0.3, 0.75, 0.4, 1.2)), 0.8)


@sound('match_start', 1)
def match_start(rng, i):
    """A drum hit and a rising chord under the start whistle."""
    s = silence(1.8)
    mix(s, tanh_sat(thump(0.6, 95, 38, 0.18), 1.5), 0, 0.9)
    chord = saw_chord(1.4, [hz('A3'), hz('E4'), hz('A4'), hz('C#5')], 12000)
    swept = svf(chord, lambda t: 300 * (3500 / 300) ** min(1.0, t / 0.8), 0.9, 'lp')
    mix(s, env_points(swept, [(0, 0), (0.5, 1), (1.0, 0.7), (1.4, 0)]), 0.05, 0.5)
    return normalize(trim_tail(reverb(s, 0.25, 0.7, 0.4, 0.8)), 0.75)


@sound('victory', 1)
def victory(rng, i):
    return stinger(rng, [['C4', 'E4', 'G4', 'C5'], ['F4', 'A4', 'C5', 'F5'], ['G4', 'B4', 'D5', 'G5'],
                         ['C4', 'G4', 'C5', 'E5', 'G5']], 0.42, 2800,
                   [(0, 'G5'), (1, 'A5'), (2, 'B5'), (3, 'C6')])


@sound('defeat', 1)
def defeat(rng, i):
    return stinger(rng, [['A3', 'C4', 'E4', 'A4'], ['F3', 'A3', 'C4', 'F4'], ['D3', 'F3', 'A3', 'D4'],
                         ['E3', 'G#3', 'B3', 'E4']], 0.55, 1500, [(0, 'E5'), (1, 'C5'), (2, 'A4'), (3, 'G#4')])


@sound('draw', 1)
def draw(rng, i):
    return stinger(rng, [['D4', 'G4', 'A4'], ['C4', 'F4', 'G4'], ['D4', 'G4', 'A4', 'D5']], 0.5, 1900,
                   [(0, 'A5'), (2, 'D5')], drums=False)


# ═════════════════════════════════ UI ═════════════════════════════════

@sound('ui_hover', 1)
def ui_hover(rng, i):
    s = modes(0.03, [(4200, 0.005, 1.0), (6100, 0.003, 0.3)])
    return normalize(s, 0.35)


@sound('ui_click', 1)
def ui_click(rng, i):
    s = modes(0.06, [(1650, 0.012, 1.0), (3300, 0.007, 0.5), (5100, 0.004, 0.25)])
    mix(s, burst(0.004, rng, 0.0008, 0.0001, 'hp', 2500), 0, 0.5)
    return normalize(s, 0.6)


@sound('ui_back', 1)
def ui_back(rng, i):
    s = modes(0.07, [(1050, 0.014, 1.0), (2100, 0.008, 0.45)])
    mix(s, burst(0.004, rng, 0.0008, 0.0001, 'hp', 2000), 0, 0.4)
    return normalize(s, 0.55)


@sound('ui_toggle', 1)
def ui_toggle(rng, i):
    s = modes(0.05, [(2200, 0.008, 1.0), (4400, 0.004, 0.35)])
    mix(s, modes(0.05, [(2900, 0.007, 1.0)]), 0.022, 0.6)
    return normalize(s, 0.55)


@sound('ui_slider', 1)
def ui_slider(rng, i):
    return normalize(modes(0.025, [(3000, 0.004, 1.0)]), 0.3)


@sound('ui_ready', 1)
def ui_ready(rng, i):
    s = silence(0.7)
    mix(s, bell(0.5, hz('E5'), 0.25), 0, 1.0)
    mix(s, bell(0.6, hz('B5'), 0.3), 0.09, 1.0)
    return normalize(trim_tail(reverb(s, 0.15, 0.5, 0.4, 0.4)), 0.6)


@sound('ui_unready', 1)
def ui_unready(rng, i):
    s = silence(0.6)
    mix(s, bell(0.4, hz('B4'), 0.2), 0, 1.0)
    mix(s, bell(0.5, hz('E4'), 0.25), 0.09, 1.0)
    return normalize(trim_tail(reverb(s, 0.15, 0.5, 0.4, 0.4)), 0.5)


@sound('ui_error', 1)
def ui_error(rng, i):
    s = silence(0.35)
    for k in range(2):
        tone = lp(osc(0.1, lambda t: 196.0, 'saw'), 1400)
        mix(s, env_points(tone, [(0, 0), (0.005, 1), (0.09, 1), (0.1, 0)]), k * 0.13, 0.7)
    return normalize(s, 0.55)


# ═════════════════════════════════ music / ambience ═════════════════════════════════

def loop_wrap(sig, length):
    """Fold everything after `length` samples back onto the start (seamless loop with tails)."""
    out = sig[:length] + [0.0] * max(0, length - len(sig))
    for k in range(length, len(sig)):
        out[(k - length) % length] += sig[k]
    return out


def crossfade_loop(sig, length, xf):
    """Loop by crossfading the material after `length` into the first xf samples."""
    out = list(sig[:length])
    for k in range(xf):
        a = k / xf
        if length + k < len(sig):
            out[k] = out[k] * a + sig[length + k] * (1 - a)
    return out


def pan(sig, p):
    """Equal-power pan, p in -1..1 -> (left, right)."""
    a = (p + 1) * math.pi / 4
    return gain(sig, math.cos(a)), gain(sig, math.sin(a))


@sound('music_menu', 1, kind='music')
def music_menu(rng, i):
    """Quiet menu loop: 84 BPM, Am9 - Fmaj7 - C(add9) - Em7, pads, plucks, soft bass and brushed drums."""
    bpm = 84.0
    beat = 60.0 / bpm
    bar = beat * 4
    prog = [['A2', 'E3', 'B3', 'C4', 'G4'], ['F2', 'C3', 'E3', 'A3', 'C4'],
            ['C3', 'G3', 'D4', 'E4', 'G4'], ['E2', 'B2', 'D3', 'G3', 'B3']]
    bars = 16
    length = N(bar * bars)
    L = silence(bar * bars + 4)
    R = silence(bar * bars + 4)
    pads_l, pads_r = silence(bar * bars + 4), silence(bar * bars + 4)
    for b in range(bars):
        chord = prog[b % 4]
        t0 = b * bar
        # pad: detuned saws, slow attack, low-passed, slightly different per channel
        for k, n in enumerate(chord[1:]):
            f = hz(n)
            for ch, det in ((pads_l, -0.0035), (pads_r, 0.0035)):
                v = osc(bar + 1.2, lambda t, ff=f * (1 + det): ff, 'saw', rng.uniform(0, 1))
                v = env_points(v, [(0, 0), (0.8, 1), (bar, 0.85), (bar + 1.2, 0)])
                mix(ch, v, t0, 0.09)
        # bass: root on 1 and the 'and' of 3
        root = hz(chord[0])
        for bt, ln in ((0, 2.2), (2.5, 1.3)):
            v = osc(beat * ln, lambda t: root, 'sine')
            v = mix(v, gain(lp(osc(beat * ln, lambda t: root, 'saw'), 400), 0.25))
            v = env_points(v, [(0, 0), (0.01, 1), (beat * ln * 0.7, 0.7), (beat * ln, 0)])
            mix(L, v, t0 + bt * beat, 0.17)
            mix(R, v, t0 + bt * beat, 0.17)
        # plucked arpeggio (8ths), a few notes skipped at random, panned around
        tones = chord[2:] + [chord[3]]
        for s8 in range(8):
            if rng.random() < 0.22 and s8 != 0:
                continue
            n = tones[(s8 * 3 + b) % len(tones)]
            f = hz(n) * 2
            v = karplus(1.4, f, rng, 0.9965, 0.35)
            v = lp(v, 3200)
            l, r = pan(v, rng.uniform(-0.6, 0.6))
            amp = 0.17 if s8 % 2 == 0 else 0.12
            mix(L, l, t0 + s8 * beat / 2, amp)
            mix(R, r, t0 + s8 * beat / 2, amp)
    pads_l = svf(pads_l, lambda t: 700 + 300 * math.sin(TAU * t / (bar * 4)), 0.8, 'lp', 64)
    pads_r = svf(pads_r, lambda t: 700 + 300 * math.sin(TAU * t / (bar * 4) + 0.6), 0.8, 'lp', 64)
    mix(L, pads_l)
    mix(R, pads_r)
    # brushed drums in bars 5-14
    for b in range(4, 14):
        t0 = b * bar
        for bt in (0, 2):
            k = thump(0.25, 95, 45, 0.07)
            mix(L, k, t0 + bt * beat, 0.3)
            mix(R, k, t0 + bt * beat, 0.3)
        for bt in (1, 3):
            sn = env(bp(noise(0.25, rng), 1800, 0.6), 0.004, 0.06)
            l, r = pan(sn, 0.1)
            mix(L, l, t0 + bt * beat, 0.09)
            mix(R, r, t0 + bt * beat, 0.09)
        for s16 in range(16):
            sh = env(hp(noise(0.05, rng), 6000), 0.002, 0.012 if s16 % 2 else 0.02)
            l, r = pan(sh, 0.35)
            mix(L, l, t0 + s16 * beat / 4, 0.035 if s16 % 4 else 0.05)
            mix(R, r, t0 + s16 * beat / 4, 0.035 if s16 % 4 else 0.05)
    # stereo reverb, then wrap the tails into the loop start
    revL = Reverb(0.8, 0.45).process(L, 0)
    revR = Reverb(0.8, 0.45, 23).process(R, 0)
    revL = normalize(revL, peak(L) * 0.5)
    revR = normalize(revR, peak(R) * 0.5)
    L = [a * 0.85 + b * 0.35 for a, b in zip(L, revL)]
    R = [a * 0.85 + b * 0.35 for a, b in zip(R, revR)]
    L, R = loop_wrap(L, length), loop_wrap(R, length)
    p = max(peak(L), peak(R))
    return gain(L, 0.7 / p), gain(R, 0.7 / p)


def wind_bed(rng, sec, base=420.0):
    w = pink(sec + 1, rng)
    lfo = [rng.uniform(0, TAU) for _ in range(3)]

    def cutoff(t):
        return base * (1 + 0.45 * math.sin(TAU * t / 11.0 + lfo[0]) + 0.25 * math.sin(TAU * t / 4.3 + lfo[1]))
    w = svf(w, cutoff, 0.9, 'lp', 64)
    amp = [0.55 + 0.3 * math.sin(TAU * (k / SR) / 7.7 + lfo[2]) + 0.15 * math.sin(TAU * (k / SR) / 2.9)
           for k in range(len(w))]
    return [a * b for a, b in zip(w, amp)]


def bird(rng):
    """A short chirp phrase: a few fast FM sweeps."""
    s = silence(1.0)
    t = 0.0
    base = rng.uniform(2800, 4800)
    for _ in range(rng.randint(2, 6)):
        d = rng.uniform(0.04, 0.09)
        f0, f1 = base * rng.uniform(0.8, 1.3), base * rng.uniform(0.7, 1.4)
        ch = sweep(d, f0, f1, 'sine', 1.0)
        mix(s, env_points(ch, [(0, 0), (d * 0.2, 1), (d, 0)]), t, 1.0)
        t += d + rng.uniform(0.02, 0.08)
    return trim_tail(s)


@sound('amb_outdoor', 1, kind='music')
def amb_outdoor(rng, i):
    """Outdoor ambience loop: wind, leaves and distant birds."""
    sec = 40.0
    length = N(sec)
    xf = N(3.0)
    L = wind_bed(rng, sec + 3.5)
    R = wind_bed(rng, sec + 3.5)
    leaves = env_points(hp(pink(sec + 3.5, rng), 2500), [(0, 0.5), (sec + 3.5, 0.5)])
    am = [0.4 + 0.6 * max(0.0, math.sin(TAU * (k / SR) / 6.3)) ** 2 for k in range(len(leaves))]
    leaves = [a * b * 0.25 for a, b in zip(leaves, am)]
    mix(L, leaves, 0, 0.6)
    mix(R, leaves, 0.37, 0.6)
    t = rng.uniform(1.0, 3.0)
    while t < sec - 1.0:
        b = reverb(bird(rng), 0.35, 0.7, 0.5, 0.8)
        l, r = pan(b, rng.uniform(-0.9, 0.9))
        a = rng.uniform(0.04, 0.09)
        mix(L, l, t, a)
        mix(R, r, t, a)
        t += rng.uniform(2.5, 7.0)
    L, R = crossfade_loop(L, length, xf), crossfade_loop(R, length, xf)
    p = max(peak(L), peak(R))
    return gain(L, 0.6 / p), gain(R, 0.6 / p)


@sound('amb_indoor', 1, kind='music')
def amb_indoor(rng, i):
    """Warehouse ambience loop: room tone, a faint electrical hum, ventilation and the odd metal creak."""
    sec = 40.0
    length = N(sec)
    xf = N(3.0)
    tot = sec + 3.5
    L = lp(brown(tot, rng), 220)
    R = lp(brown(tot, rng), 220)
    vent = lp(pink(tot, rng), 1800)
    hum = osc(tot, lambda t: 100.0)
    mix(hum, gain(osc(tot, lambda t: 200.0), 0.4))
    for ch in (L, R):
        mix(ch, vent, 0, 0.35)
        mix(ch, hum, 0, 0.05)
    t = rng.uniform(2.0, 5.0)
    while t < sec - 2.0:
        f = rng.uniform(140, 320)
        creak = modes(2.0, [(f, 0.5, 1.0), (f * 2.3, 0.3, 0.5), (f * 3.9, 0.2, 0.3)], rng)
        creak = env_points(creak, [(0, 0), (0.08, 1), (2.0, 0)])
        creak = reverb(creak, 0.5, 0.85, 0.5, 1.5)
        l, r = pan(creak, rng.uniform(-0.8, 0.8))
        a = rng.uniform(0.15, 0.3)
        mix(L, l, t, a)
        mix(R, r, t, a)
        t += rng.uniform(6.0, 12.0)
    L, R = crossfade_loop(L, length, xf), crossfade_loop(R, length, xf)
    p = max(peak(L), peak(R))
    return gain(L, 0.5 / p), gain(R, 0.5 / p)


# ═════════════════════════════════ driver ═════════════════════════════════

def write_music(name, lr):
    os.makedirs(MUSIC, exist_ok=True)
    wav = os.path.join(MUSIC, name + '.wav')
    write_wav(wav, lr, channels=2)
    ffmpeg = shutil.which('ffmpeg')
    if ffmpeg:
        ogg = os.path.join(MUSIC, name + '.ogg')
        r = subprocess.run([ffmpeg, '-y', '-loglevel', 'error', '-i', wav, '-c:a', 'vorbis', '-strict', '-2',
                            '-b:a', '160k', ogg])
        if r.returncode == 0 and os.path.exists(ogg):
            os.remove(wav)
            return ogg
    return wav


def main(prefixes):
    os.makedirs(SFX, exist_ok=True)
    for name, count, fn, kind in REGISTRY:
        if prefixes and not any(name.startswith(p) for p in prefixes):
            continue
        for i in range(1, count + 1):
            rng = rng_for(name, i)
            sig = fn(rng, i)
            if kind == 'music':
                path = write_music(name, sig)
            else:
                sig = fade(sig, 0.0, 0.004)   # no click at the end
                fname = f'{name}_{i}.wav' if count > 1 else f'{name}.wav'
                path = os.path.join(SFX, fname)
                write_wav(path, sig)
            print(f'{os.path.relpath(path, ROOT)}  ({os.path.getsize(path) // 1024} KB)', flush=True)


if __name__ == '__main__':
    main(sys.argv[1:])
