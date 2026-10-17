"""
A small formant speech synthesiser, the way the 8-bit machines talked: no recordings and no speech engine, just a
buzz (the voice) and a hiss (the breath) shaped by a few resonant filters (the mouth). The words are written as
phonemes by hand, the way the old speech programs took them.

    from speech import say
    samples = say("W EH1 L K AH M , V IH1 Z IH T ER", rate=22050)

Each phoneme is a target for the filters; the voice glides from one target to the next, which is most of what
makes it sound like speech. A 1 after a vowel stresses it (longer, higher); , and ... are pauses; ! ends on a fall.
"""
import math
import random

# name: (F1, F2, F3, voice, breath, hiss, hiss centre, hiss width, milliseconds)
#   voice  - the buzz of the vocal cords through the mouth's resonances
#   breath - noise through the same resonances (an h, a k's puff of air)
#   hiss   - noise through its own filter (the z of "visitor", the click of a t)
PHONEMES = {
    # Vowels.
    "IY": (270, 2290, 3010, 1.0, 0, 0, 0, 0, 130),
    "IH": (390, 1990, 2550, 1.0, 0, 0, 0, 0, 85),
    "EH": (530, 1840, 2480, 1.0, 0, 0, 0, 0, 105),
    "AH": (640, 1190, 2390, 1.0, 0, 0, 0, 0, 100),
    "UW": (310, 900, 2250, 1.0, 0, 0, 0, 0, 150),
    "ER": (490, 1350, 1690, 0.9, 0, 0, 0, 0, 120),
    "AE": (660, 1720, 2410, 1.0, 0, 0, 0, 0, 120),
    "AA": (730, 1090, 2440, 1.0, 0, 0, 0, 0, 120),
    "AO": (570, 840, 2410, 1.0, 0, 0, 0, 0, 120),
    "OW": (500, 900, 2450, 1.0, 0, 0, 0, 0, 140),
    "EY": (480, 1900, 2550, 1.0, 0, 0, 0, 0, 130),
    # Glides and liquids.
    "W": (290, 620, 2150, 0.75, 0, 0, 0, 0, 70),
    "Y": (260, 2070, 3020, 0.75, 0, 0, 0, 0, 60),
    "L": (340, 950, 2450, 0.7, 0, 0, 0, 0, 70),
    "R": (310, 1060, 1380, 0.7, 0, 0, 0, 0, 70),
    # Nasals: a low, muffled hum.
    "M": (260, 1050, 2200, 0.45, 0, 0, 0, 0, 85),
    "N": (260, 1550, 2600, 0.45, 0, 0, 0, 0, 70),
    "NG": (260, 2250, 2750, 0.45, 0, 0, 0, 0, 80),
    # Voiced fricatives: buzz and hiss together.
    "V": (230, 1100, 2100, 0.4, 0, 0.18, 2800, 3000, 75),
    "Z": (240, 1450, 2500, 0.22, 0, 2.6, 5000, 2600, 150),
    "ZH": (240, 1700, 2500, 0.35, 0, 0.8, 3000, 1500, 70),
    # Unvoiced fricatives, and the breath of an h.
    "S": (300, 1700, 2600, 0, 0, 1.1, 5500, 1500, 110),
    "F": (300, 1100, 2400, 0, 0, 0.35, 3800, 3500, 90),
    "TH": (300, 1400, 2500, 0, 0, 0.3, 4500, 4000, 90),
    "HH": (None, None, None, 0, 0.5, 0, 0, 0, 70),
    # Stops: a closure (silence), a burst, then a puff of breath into the next sound.
    "K": (300, 1900, 2500, 0, 0, 0, 0, 0, 60),
    "Kb": (300, 1900, 2500, 0, 0, 0.9, 1900, 500, 14),
    "Kh": (None, None, None, 0, 0.35, 0, 0, 0, 45),
    "T": (300, 1700, 2600, 0, 0, 0, 0, 0, 35),
    "Tb": (300, 1700, 2600, 0, 0, 0.7, 4600, 2200, 10),
    "Th": (None, None, None, 0, 0.2, 0, 0, 0, 20),
    "P": (300, 1000, 2400, 0, 0, 0, 0, 0, 60),
    "Pb": (300, 1000, 2400, 0, 0, 0.5, 1000, 1800, 10),
    "Ph": (None, None, None, 0, 0.3, 0, 0, 0, 40),
    # Voiced stops: a murmur through the closure, a burst, and straight on.
    "G": (300, 1900, 2500, 0.12, 0, 0, 0, 0, 50),
    "Gb": (300, 1900, 2500, 0.2, 0, 0.6, 1900, 600, 12),
    "D": (300, 1700, 2600, 0.12, 0, 0, 0, 0, 40),
    "Db": (300, 1700, 2600, 0.2, 0, 0.6, 3800, 2000, 10),
}
BANDWIDTHS = {"M": (120, 300, 400), "N": (120, 300, 400)}
VOWELS = {"IY", "IH", "EH", "AH", "UW", "ER"}
PAUSES = {",": 220, "...": 520}
FRAME = 64


def _expand(text):
    """Phoneme names to (name, stressed) steps, stops split into closure, burst and breath."""
    steps = []
    for word in text.split():
        if word in PAUSES or word == "!":
            steps.append((word, False))
            continue
        stressed = word.endswith("1")
        name = word.rstrip("012")
        if name == "JH":
            # J: a d's closure and burst, sliding into a buzzing zh.
            steps += [("D", False), ("Db", False), ("ZH", stressed)]
            continue
        if name not in PHONEMES:
            raise ValueError(f"no phoneme {word!r}")
        steps.append((name, stressed))
        if name in ("K", "T", "P"):
            steps.append((name + "b", False))
            steps.append((name + "h", False))
        elif name in ("G", "D"):
            steps.append((name + "b", False))
    return steps


class _Resonator:
    """A two-pole filter ringing at one frequency: one of the mouth's resonances."""

    def __init__(self, rate):
        self.rate = rate
        self.a = self.b = self.c = 0.0
        self.y1 = self.y2 = 0.0

    def tune(self, freq, width):
        t = 1.0 / self.rate
        self.c = -math.exp(-2 * math.pi * width * t)
        self.b = 2 * math.exp(-math.pi * width * t) * math.cos(2 * math.pi * freq * t)
        self.a = 1 - self.b - self.c

    def __call__(self, x):
        y = self.a * x + self.b * self.y1 + self.c * self.y2
        self.y2, self.y1 = self.y1, y
        return y


def say(text, rate=22050, pitch=90.0, speed=1.0, size=1.0, seed=7, bend=None):
    """
    The phonemes spoken, as samples from -1 to 1. pitch is the voice's resting note in Hz; speed stretches
    (below 1) or hurries it; size above 1 is a bigger head, which lowers every resonance. bend, if given, takes
    the time through the speech (0 to 1) and gives a factor for the pitch, for a cry or a wail.
    """
    rng = random.Random(seed)
    steps = _expand(text)
    # Each step's targets, and how long it lasts, in frames.
    targets = []
    for i, (name, stressed) in enumerate(steps):
        if name in PAUSES or name == "!":
            ms = PAUSES.get(name, 0)
            if ms:
                targets.append(dict(f=None, v=0, br=0, h=0, hf=0, hw=0, ms=ms, accent=0, pause=True))
            continue
        f1, f2, f3, v, br, h, hf, hw, ms = PHONEMES[name]
        if stressed:
            ms *= 1.45
        targets.append(dict(f=None if f1 is None else (f1, f2, f3), bw=BANDWIDTHS.get(name, (70, 100, 140)),
                            v=v, br=br, h=h, hf=hf, hw=hw, ms=ms, accent=1 if stressed else 0, pause=False,
                            vowel=name in VOWELS))
    # Breath with no mouth shape of its own takes the next sound's.
    for i, t in enumerate(targets):
        if t["f"] is None and not t["pause"]:
            nxt = next((u for u in targets[i + 1:] if u["f"] is not None), None)
            t["f"] = nxt["f"] if nxt else (500, 1500, 2500)
            t["bw"] = nxt["bw"] if nxt else (90, 120, 160)
    # Pauses hold the mouth where it was.
    last = (500, 1500, 2500)
    for t in targets:
        if t["f"] is None:
            t["f"], t["bw"] = last, (90, 120, 160)
        last = t["f"]

    frames = []
    for t in targets:
        n = max(1, int(t["ms"] / 1000 * rate / speed / FRAME))
        for k in range(n):
            frames.append((t, k / n))
    if not frames:
        return []

    # The tune of the sentence: each phrase starts high and drifts down; stressed vowels lift; the last one falls away.
    phrase_start = 0
    contour = []
    for i, (t, _) in enumerate(frames):
        if t["pause"]:
            phrase_start = i + 1
        contour.append(i - phrase_start)
    last_accent = max((i for i, (t, _) in enumerate(frames) if t["accent"]), default=-1)

    # Smoothed tracks: the mouth glides (about 25 ms), loudness moves quicker (about 6 ms).
    def smooth(value, target, tau_ms):
        k = 1 - math.exp(-(FRAME / rate) / (tau_ms / 1000))
        return value + (target - value) * k

    fs = list(frames[0][0]["f"])
    bws = list(frames[0][0]["bw"])
    amp = {"v": 0.0, "br": 0.0, "h": 0.0}
    f0 = pitch
    voice_res = [_Resonator(rate) for _ in range(5)]
    hiss_res = _Resonator(rate)
    hiss_res2 = _Resonator(rate)
    tilt = 0.0
    phase = 0.0
    prev_g = 0.0
    prev_noise = 0.0
    hf, hw = 3000.0, 2000.0
    out = []
    for i, (t, at) in enumerate(frames):
        for j in range(3):
            fs[j] = smooth(fs[j], t["f"][j] / size, 22 if j else 18)
            bws[j] = smooth(bws[j], t["bw"][j], 22)
        for key in amp:
            amp[key] = smooth(amp[key], t[key], 3 if key == "h" else 7)
        if t["hf"]:
            hf, hw = t["hf"], t["hw"]
        # The note: a slow fall through the phrase, a lift on stress, and a long fall at the very end.
        aim = pitch * (1.08 - 0.004 * contour[i])
        if t["accent"]:
            aim *= 1.18 + 0.05 * math.sin(math.pi * at)
        if i >= last_accent >= 0:
            aim *= 1.15 - 0.45 * min(1.0, (i - last_accent) / max(1, len(frames) - last_accent))
        if bend is not None:
            aim = pitch * bend(i / len(frames))
        f0 = smooth(f0, max(55.0, aim), 40)
        voice_res[0].tune(fs[0], bws[0])
        voice_res[1].tune(fs[1], bws[1])
        voice_res[2].tune(fs[2], bws[2])
        voice_res[3].tune(3400 / size, 250)
        voice_res[4].tune(4300 / size, 300)
        hiss_res.tune(hf, hw)
        hiss_res2.tune(min(rate * 0.45, hf * 1.35), hw * 1.2)
        for _ in range(FRAME):
            # The buzz: a soft pulse of air once a cycle, with a little waver so it doesn't sound like a machine.
            wobble = 1 + 0.007 * math.sin(2 * math.pi * 5.2 * len(out) / rate) + rng.uniform(-0.004, 0.004)
            phase += f0 * wobble / rate
            if phase >= 1:
                phase -= 1
            open_q = 0.62
            if phase < open_q * 0.66:
                g = 0.5 * (1 - math.cos(math.pi * phase / (open_q * 0.66)))
            elif phase < open_q:
                g = math.cos(0.5 * math.pi * (phase - open_q * 0.66) / (open_q * 0.34))
            else:
                g = 0.0
            dg = g - prev_g
            prev_g = g
            noise = rng.uniform(-1, 1)
            # A little breath rides on every vowel; the buzz is darkened a touch.
            tilt = tilt * 0.35 + dg * 0.65
            x = amp["v"] * tilt * 9 + amp["br"] * noise * 0.22 + amp["v"] * noise * 0.012
            for r in voice_res:
                x = r(x)
            # The hiss: the change in the noise (no rumble under it), rung through its own band.
            h = amp["h"] * (noise - prev_noise)
            prev_noise = noise
            y = x + 0.012 * (hiss_res(h) + 0.6 * hiss_res2(h))
            out.append(y)
    peak = max(1e-9, max(abs(s) for s in out))
    return [s / peak for s in out]
