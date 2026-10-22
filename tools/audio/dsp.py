"""Tiny DSP toolkit for the Airsoft FPS sound generator (Python standard library only).

Signals are plain lists of floats at SR samples per second. Everything is deterministic for a
given random.Random, so re-running the generator reproduces the same files.
"""
import math
import struct
import wave

SR = 44100
TAU = 2.0 * math.pi


def N(sec):
    return max(0, int(round(sec * SR)))


def silence(sec):
    return [0.0] * N(sec)


def mix(dst, src, at=0.0, gain=1.0):
    """Add src into dst starting at `at` seconds (dst grows as needed). Returns dst."""
    i0 = N(at)
    end = i0 + len(src)
    if end > len(dst):
        dst.extend([0.0] * (end - len(dst)))
    for i, v in enumerate(src):
        dst[i0 + i] += v * gain
    return dst


def gain(sig, g):
    return [v * g for v in sig]


def peak(sig):
    return max((abs(v) for v in sig), default=0.0)


def normalize(sig, target=0.891):   # -1 dBFS
    p = peak(sig)
    return gain(sig, target / p) if p > 1e-9 else sig


def rms(sig):
    return math.sqrt(sum(v * v for v in sig) / max(1, len(sig)))


def normalize_rms(sig, db=-18.0, ceiling=0.95):
    """Match loudness (RMS over the whole clip), soft-limiting any peak above the ceiling."""
    r = rms(sig)
    if r < 1e-9:
        return sig
    out = gain(sig, 10 ** (db / 20.0) / r)
    if peak(out) > ceiling:
        out = [ceiling * math.tanh(v / ceiling) for v in out]
    return out


def trim_tail(sig, thresh=0.0005, keep=0.01):
    """Drop the near-silent end (after the last sample above thresh), keep a short pad."""
    last = 0
    for i in range(len(sig) - 1, -1, -1):
        if abs(sig[i]) > thresh:
            last = i
            break
    return sig[:min(len(sig), last + N(keep))]


def fade(sig, fin=0.0, fout=0.0):
    out = list(sig)
    n_in, n_out = N(fin), N(fout)
    for i in range(min(n_in, len(out))):
        out[i] *= i / n_in
    for i in range(min(n_out, len(out))):
        out[len(out) - 1 - i] *= i / n_out
    return out


def noise(sec, rng):
    return [rng.uniform(-1.0, 1.0) for _ in range(N(sec))]


def brown(sec, rng, leak=0.997):
    """Brownian (red) noise, roughly unit peak."""
    out = [0.0] * N(sec)
    y = 0.0
    for i in range(len(out)):
        y = y * leak + rng.uniform(-1.0, 1.0) * 0.06
        out[i] = y
    return normalize(out, 1.0)


def pink(sec, rng):
    """Paul Kellet's economy pink noise filter."""
    out = [0.0] * N(sec)
    b0 = b1 = b2 = 0.0
    for i in range(len(out)):
        w = rng.uniform(-1.0, 1.0)
        b0 = 0.99765 * b0 + w * 0.0990460
        b1 = 0.96300 * b1 + w * 0.2965164
        b2 = 0.57000 * b2 + w * 1.0526913
        out[i] = (b0 + b1 + b2 + w * 0.1848) * 0.2
    return out


def env(sig, attack=0.001, tau=0.1, hold=0.0):
    """Linear attack, optional hold, then exponential decay with time constant tau (seconds)."""
    out = [0.0] * len(sig)
    na, nh = max(1, N(attack)), N(hold)
    k = math.exp(-1.0 / (tau * SR)) if tau > 0 else 0.0
    g = 1.0
    for i, v in enumerate(sig):
        if i < na:
            out[i] = v * (i / na)
        elif i < na + nh:
            out[i] = v
        else:
            g *= k
            out[i] = v * g
    return out


def env_points(sig, pts):
    """Piecewise-linear envelope; pts = [(time, level), ...] sorted by time."""
    out = [0.0] * len(sig)
    j = 0
    for i, v in enumerate(sig):
        t = i / SR
        while j < len(pts) - 2 and t > pts[j + 1][0]:
            j += 1
        (t0, a), (t1, b) = pts[j], pts[min(j + 1, len(pts) - 1)]
        if t <= t0:
            lv = a
        elif t >= t1:
            lv = b
        else:
            lv = a + (b - a) * (t - t0) / (t1 - t0)
        out[i] = v * lv
    return out


def biquad(sig, kind, f, q=0.707, db=0.0):
    """RBJ cookbook biquad: lp, hp, bp (0 dB peak), peak, lowshelf, highshelf."""
    f = min(max(f, 10.0), SR * 0.45)
    w0 = TAU * f / SR
    cw, sw = math.cos(w0), math.sin(w0)
    alpha = sw / (2.0 * q)
    A = 10.0 ** (db / 40.0)
    if kind == 'lp':
        b0, b1, b2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2
        a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
    elif kind == 'hp':
        b0, b1, b2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2
        a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
    elif kind == 'bp':
        b0, b1, b2 = alpha, 0.0, -alpha
        a0, a1, a2 = 1 + alpha, -2 * cw, 1 - alpha
    elif kind == 'peak':
        b0, b1, b2 = 1 + alpha * A, -2 * cw, 1 - alpha * A
        a0, a1, a2 = 1 + alpha / A, -2 * cw, 1 - alpha / A
    elif kind in ('lowshelf', 'highshelf'):
        s = 2 * math.sqrt(A) * alpha
        if kind == 'lowshelf':
            b0 = A * ((A + 1) - (A - 1) * cw + s)
            b1 = 2 * A * ((A - 1) - (A + 1) * cw)
            b2 = A * ((A + 1) - (A - 1) * cw - s)
            a0 = (A + 1) + (A - 1) * cw + s
            a1 = -2 * ((A - 1) + (A + 1) * cw)
            a2 = (A + 1) + (A - 1) * cw - s
        else:
            b0 = A * ((A + 1) + (A - 1) * cw + s)
            b1 = -2 * A * ((A - 1) + (A + 1) * cw)
            b2 = A * ((A + 1) + (A - 1) * cw - s)
            a0 = (A + 1) - (A - 1) * cw + s
            a1 = 2 * ((A - 1) - (A + 1) * cw)
            a2 = (A + 1) - (A - 1) * cw - s
    else:
        raise ValueError(kind)
    b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0
    out = [0.0] * len(sig)
    x1 = x2 = y1 = y2 = 0.0
    for i, x in enumerate(sig):
        y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2, x1, y2, y1 = x1, x, y1, y
        out[i] = y
    return out


def lp(sig, f, q=0.707):
    return biquad(sig, 'lp', f, q)


def hp(sig, f, q=0.707):
    return biquad(sig, 'hp', f, q)


def bp(sig, f, q=1.0):
    return biquad(sig, 'bp', f, q)


def onepole_lp(sig, f):
    a = math.exp(-TAU * f / SR)
    out = [0.0] * len(sig)
    y = 0.0
    for i, x in enumerate(sig):
        y = (1 - a) * x + a * y
        out[i] = y
    return out


def svf(sig, cutoff_fn, q=0.707, mode='bp', step=16):
    """Zavalishin TPT state-variable filter with a time-varying cutoff (Hz as a function of t)."""
    out = [0.0] * len(sig)
    ic1 = ic2 = 0.0
    k = 1.0 / q
    a1 = a2 = a3 = 0.0
    for i, x in enumerate(sig):
        if i % step == 0:
            fc = min(max(cutoff_fn(i / SR), 20.0), SR * 0.45)
            g = math.tan(math.pi * fc / SR)
            a1 = 1.0 / (1.0 + g * (g + k))
            a2 = g * a1
            a3 = g * a2
        v3 = x - ic2
        v1 = a1 * ic1 + a2 * v3
        v2 = ic2 + a2 * ic1 + a3 * v3
        ic1 = 2 * v1 - ic1
        ic2 = 2 * v2 - ic2
        if mode == 'lp':
            out[i] = v2
        elif mode == 'hp':
            out[i] = x - k * v1 - v2
        else:
            out[i] = v1
    return out


def modes(sec, partials, phase_rng=None):
    """Sum of exponentially decaying sines: partials = [(freq, tau, amp), ...]."""
    out = [0.0] * N(sec)
    for f, tau, a in partials:
        if f <= 0 or f >= SR * 0.45:
            continue
        w = TAU * f / SR
        r = math.exp(-1.0 / (tau * SR))
        c, s = math.cos(w) * r, math.sin(w) * r
        ph = phase_rng.uniform(0, TAU) if phase_rng else 0.0
        re, im = a * math.cos(ph), a * math.sin(ph)
        for i in range(len(out)):
            out[i] += im
            re, im = re * c - im * s, re * s + im * c
            if abs(re) + abs(im) < 1e-6:
                break
    return out


def klatt(sig, f, bw):
    """Klatt two-pole resonator (formant)."""
    T = 1.0 / SR
    C = -math.exp(-2 * math.pi * bw * T)
    B = 2 * math.exp(-math.pi * bw * T) * math.cos(TAU * f * T)
    A = 1 - B - C
    out = [0.0] * len(sig)
    y1 = y2 = 0.0
    for i, x in enumerate(sig):
        y = A * x + B * y1 + C * y2
        y2, y1 = y1, y
        out[i] = y
    return out


def osc(sec, freq_fn, shape='sine', phase=0.0):
    """Oscillator with a frequency function of time. Shapes: sine, saw, square, tri (naive, keep < ~2 kHz)."""
    out = [0.0] * N(sec)
    ph = phase
    for i in range(len(out)):
        f = freq_fn(i / SR)
        if shape == 'sine':
            out[i] = math.sin(TAU * ph)
        elif shape == 'saw':
            out[i] = 2.0 * (ph - math.floor(ph + 0.5))
        elif shape == 'square':
            out[i] = 1.0 if (ph % 1.0) < 0.5 else -1.0
        elif shape == 'tri':
            out[i] = 4.0 * abs(ph % 1.0 - 0.5) - 1.0
        ph += f / SR
    return out


def sine(sec, f, phase=0.0):
    return osc(sec, lambda t: f, 'sine', phase)


def sweep(sec, f0, f1, shape='sine', curve=1.0):
    """Exponential-ish glide from f0 to f1 over sec (curve > 1 = faster at the start)."""
    def fn(t):
        x = min(1.0, t / sec) ** (1.0 / curve)
        return f0 * (f1 / f0) ** x
    return osc(sec, fn, shape)


def thump(sec, f0, f1, tau, attack=0.0015):
    """Pitch-dropping sine thump (kick / body / heel)."""
    return env(sweep(sec, f0, f1, 'sine', 2.5), attack, tau)


def burst(sec, rng, tau, attack=0.0005, kind=None, f=1000.0, q=0.8):
    """Enveloped noise burst, optionally filtered."""
    s = noise(sec, rng)
    if kind:
        s = biquad(s, kind, f, q)
    return env(s, attack, tau)


def click(rng, freqs, tau=0.01, amp=1.0, noise_amp=0.6, hp_f=2500.0):
    """Short mechanical click: a tiny noise transient plus a few metallic partials."""
    sec = max(0.03, tau * 6)
    out = gain(burst(0.006, rng, 0.0012, 0.0002, 'hp', hp_f), noise_amp)
    parts = [(f * rng.uniform(0.97, 1.03), tau * rng.uniform(0.8, 1.2), amp / (1 + k * 0.6))
             for k, f in enumerate(freqs)]
    return mix(out, modes(sec, parts, rng))


def tanh_sat(sig, drive=2.0):
    k = math.tanh(drive)
    return [math.tanh(v * drive) / k for v in sig]


def pitch(sig, ratio):
    """Resample by ratio (> 1 = higher and shorter), linear interpolation."""
    n = int(len(sig) / ratio)
    out = [0.0] * n
    for i in range(n):
        x = i * ratio
        j = int(x)
        fr = x - j
        a = sig[j] if j < len(sig) else 0.0
        b = sig[j + 1] if j + 1 < len(sig) else 0.0
        out[i] = a + (b - a) * fr
    return out


def echo(sig, delay, fb=0.3, wet=0.3, lp_f=3000.0, tail=0.5):
    d = N(delay)
    out = list(sig) + [0.0] * N(tail)
    line = [0.0] * len(out)
    a = math.exp(-TAU * lp_f / SR)
    y = 0.0
    for i in range(len(out)):
        dl = line[i - d] if i >= d else 0.0
        y = (1 - a) * dl + a * y
        x = (sig[i] if i < len(sig) else 0.0)
        line[i] = x + y * fb
        out[i] = x + y * wet
    return out


class Reverb:
    """Freeverb-style mono reverb (4 damped combs + 2 allpasses). Stereo = two instances with offsets."""

    def __init__(self, room=0.75, damp=0.35, spread=0):
        self.combs = [[[0.0] * (d + spread), 0, 0.0] for d in (1116, 1277, 1422, 1557)]
        self.aps = [[[0.0] * (d + spread), 0] for d in (556, 341)]
        self.fb = room * 0.28 + 0.7
        self.damp = damp

    def process(self, sig, tail=2.0):
        out = [0.0] * (len(sig) + N(tail))
        fb, d1, d2 = self.fb, self.damp, 1 - self.damp
        combs, aps = self.combs, self.aps
        for i in range(len(out)):
            x = (sig[i] if i < len(sig) else 0.0) * 0.015
            acc = 0.0
            for c in combs:
                buf, idx, store = c
                y = buf[idx]
                store = y * d2 + store * d1
                buf[idx] = x + store * fb
                idx += 1
                if idx >= len(buf):
                    idx = 0
                c[1] = idx
                c[2] = store
                acc += y
            for a in aps:
                buf, idx = a
                b = buf[idx]
                buf[idx] = acc + b * 0.5
                acc = b - acc
                idx += 1
                if idx >= len(buf):
                    idx = 0
                a[1] = idx
            out[i] = acc
        return out


def reverb(sig, wet=0.25, room=0.75, damp=0.35, tail=1.5):
    r = Reverb(room, damp).process(sig, tail)
    r = normalize(r, peak(sig) * 0.6)   # wet level relative to the dry peak
    out = list(sig) + [0.0] * (len(r) - len(sig))
    return [o * (1.0 - wet * 0.5) + w * wet for o, w in zip(out, r)]


def karplus(sec, f, rng, decay=0.996, bright=0.5):
    """Karplus-Strong plucked string."""
    period = max(2, int(SR / f))
    line = [rng.uniform(-1, 1) for _ in range(period)]
    # soften the initial excitation (darker pluck)
    for _ in range(int((1 - bright) * 4)):
        line = [(line[i] + line[i - 1]) * 0.5 for i in range(period)]
    out = [0.0] * N(sec)
    idx = 0
    prev = 0.0
    for i in range(len(out)):
        v = line[idx]
        nv = decay * 0.5 * (v + prev)
        prev = v
        line[idx] = nv
        out[i] = v
        idx += 1
        if idx >= period:
            idx = 0
    return out


def write_wav(path, sig, channels=1, sr=SR):
    """16-bit PCM. For stereo pass an interleaved list or a (left, right) tuple."""
    if channels == 2 and isinstance(sig, tuple):
        l, r = sig
        n = max(len(l), len(r))
        inter = [0.0] * (n * 2)
        for i in range(n):
            inter[2 * i] = l[i] if i < len(l) else 0.0
            inter[2 * i + 1] = r[i] if i < len(r) else 0.0
        sig = inter
    frames = bytearray()
    for v in sig:
        v = max(-1.0, min(1.0, v))
        frames += struct.pack('<h', int(round(v * 32767)))
    with wave.open(path, 'wb') as w:
        w.setnchannels(channels)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(bytes(frames))
