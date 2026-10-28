#!/usr/bin/env python3
"""Procedural sound bank for KORDON.

Everything here is synthesised from noise, sine partials and envelopes, so the
whole audio set is original, redistributable and reproducible: run the script and
the WAVs in Assets/_Project/Art/Resources/Kordon/Audio are regenerated.

Design notes
------------
* Gunshots are a transient crack + pitched body + tail, with the crack brightness
  and tail length driven by the weapon class.  "far" variants are the same shot
  low-passed: the AudioDirector uses them past ~35 m so distance actually changes
  the sound, not just the volume.
* Footsteps are short noise bursts shaped by the surface: concrete = hard click,
  metal = resonant ring, gravel = granular crumble, wood = hollow knock,
  sand = soft shuffle.  Stance/gear change loudness and length, never the sample.
* Reload sounds are the real handling beats the magazine system emits, so the
  audio you hear is always in sync with the state machine.
"""

from __future__ import annotations

import math
import os
import struct
import wave

import numpy as np

OUT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "_Project", "Art", "Resources", "Kordon", "Audio",
)
RATE = 44100
rng = np.random.default_rng(0x4B4F52444F4E)


def write_wav(name: str, samples: np.ndarray, gain: float = 0.9) -> None:
    os.makedirs(OUT, exist_ok=True)
    data = np.clip(samples * gain, -1.0, 1.0)
    pcm = (data * 32767.0).astype("<i2")
    with wave.open(os.path.join(OUT, name), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(RATE)
        handle.writeframes(pcm.tobytes())


def t(seconds: float) -> np.ndarray:
    return np.linspace(0.0, seconds, int(RATE * seconds), endpoint=False)


def noise(seconds: float) -> np.ndarray:
    return rng.normal(0.0, 1.0, int(RATE * seconds))


def lowpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    alpha = 1.0 - math.exp(-2.0 * math.pi * cutoff / RATE)
    y = np.empty_like(x)
    acc = 0.0
    for i, value in enumerate(x):
        acc += alpha * (value - acc)
        y[i] = acc
    return y


def highpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    return x - lowpass(x, cutoff)


def env(length: int, attack: float, decay: float, power: float = 2.0) -> np.ndarray:
    attack_n = max(1, int(attack * RATE))
    decay_n = max(1, length - attack_n)
    a = np.linspace(0.0, 1.0, attack_n) ** 0.5
    d = np.exp(-np.linspace(0.0, decay, decay_n) * power)
    return np.concatenate([a, d])[:length]


def gunshot(seconds: float, crack_hz: float, body_hz: float, tail: float,
            bright: float = 1.0) -> np.ndarray:
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    crack = highpass(noise(seconds), crack_hz) * env(n, 0.0006, 18.0, 3.0)
    body = np.sin(2 * math.pi * body_hz * time) * env(n, 0.001, 9.0, 2.2)
    sub = np.sin(2 * math.pi * (body_hz * 0.4) * time) * env(n, 0.002, 6.0, 2.0)
    tail_noise = lowpass(noise(seconds), 2400.0 * bright) * env(n, 0.02, tail, 1.6)
    return 0.55 * crack + 0.5 * body + 0.35 * sub + 0.30 * tail_noise


def distant(x: np.ndarray, cutoff: float = 900.0) -> np.ndarray:
    return lowpass(lowpass(x, cutoff), cutoff)


def footstep(kind: str, seconds: float = 0.22) -> np.ndarray:
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    base = noise(seconds)
    if kind == "concrete":
        body = highpass(base, 900.0) * env(n, 0.0008, 26.0, 2.4)
        thud = np.sin(2 * math.pi * 120 * time) * env(n, 0.001, 22.0, 2.0)
        return 0.8 * body + 0.5 * thud
    if kind == "metal":
        ring = sum(np.sin(2 * math.pi * f * time) * amp for f, amp in
                   ((412.0, 0.5), (1030.0, 0.3), (1780.0, 0.2)))
        hit = highpass(base, 1400.0) * env(n, 0.0006, 20.0, 2.2)
        return 0.55 * hit + 0.45 * ring * env(n, 0.001, 12.0, 1.8)
    if kind == "gravel":
        grains = lowpass(base, 5200.0) * env(n, 0.004, 8.0, 1.5)
        grit = highpass(noise(seconds), 2200.0) * env(n, 0.01, 5.0, 1.2)
        return 0.7 * grains + 0.5 * grit
    if kind == "wood":
        knock = np.sin(2 * math.pi * 190 * time) * env(n, 0.001, 20.0, 2.0)
        hollow = np.sin(2 * math.pi * 320 * time) * env(n, 0.002, 26.0, 2.0)
        return 0.6 * knock + 0.35 * hollow + 0.15 * highpass(base, 2000.0)
    if kind == "sand":
        soft = lowpass(base, 2600.0) * env(n, 0.006, 7.0, 1.4)
        return 0.7 * soft
    if kind == "water":
        splash = lowpass(noise(seconds), 4200.0) * env(n, 0.004, 6.0, 1.2)
        bubbles = np.sin(2 * math.pi * 220 * time) * env(n, 0.02, 8.0, 1.4)
        return 0.6 * splash + 0.25 * bubbles
    return base * env(n, 0.001, 20.0)


def click(seconds: float, hz: float, decay: float) -> np.ndarray:
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    tick = highpass(noise(seconds), 3000.0) * env(n, 0.0003, 40.0, 3.0)
    tone = np.sin(2 * math.pi * hz * time) * env(n, 0.0004, decay, 2.0)
    return 0.6 * tick + 0.5 * tone


def metallic(seconds: float, base_hz: float) -> np.ndarray:
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    partials = sum(np.sin(2 * math.pi * base_hz * k * time) / (k ** 1.4)
                   for k in range(1, 7))
    rattle = highpass(noise(seconds), 2500.0) * env(n, 0.001, 30.0, 2.0)
    return 0.55 * partials * env(n, 0.001, 16.0, 2.0) + 0.4 * rattle


def hit_flesh() -> np.ndarray:
    seconds = 0.18
    n = int(RATE * seconds)
    body = lowpass(noise(seconds), 700.0) * env(n, 0.001, 22.0, 2.0)
    thump = np.sin(2 * math.pi * 95 * (np.arange(n) / RATE)) * env(n, 0.001, 18.0, 2.0)
    return 0.7 * body + 0.6 * thump


def hit_armor() -> np.ndarray:
    seconds = 0.16
    n = int(RATE * seconds)
    ping = np.sin(2 * math.pi * 2450 * (np.arange(n) / RATE)) * env(n, 0.0005, 26.0, 2.4)
    scrape = highpass(noise(seconds), 4000.0) * env(n, 0.001, 30.0, 2.4)
    return 0.6 * ping + 0.45 * scrape


def whizz() -> np.ndarray:
    seconds = 0.28
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    sweep = np.sin(2 * math.pi * (900 + 1600 * time / seconds) * time)
    air = highpass(noise(seconds), 3000.0) * env(n, 0.01, 9.0, 1.6)
    return 0.35 * sweep * env(n, 0.02, 12.0, 1.8) + 0.6 * air


def beep(seconds: float, hz: float) -> np.ndarray:
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    square = np.sign(np.sin(2 * math.pi * hz * time))
    return 0.4 * square * env(n, 0.005, 6.0, 1.6)


def breathing(seconds: float, inhale: bool) -> np.ndarray:
    n = int(RATE * seconds)
    envelope = np.sin(np.linspace(0, math.pi, n)) ** 2
    body = lowpass(noise(seconds), 900.0 if inhale else 700.0)
    return 0.55 * body * envelope


def ambience(seconds: float, wind: bool) -> np.ndarray:
    n = int(RATE * seconds)
    length = int(RATE * 2.0)
    base = lowpass(noise(seconds), 380.0 if wind else 210.0)
    hum = 0.0
    if not wind:
        time = np.arange(n) / RATE
        hum = (0.12 * np.sin(2 * math.pi * 50 * time) +
               0.08 * np.sin(2 * math.pi * 100 * time) +
               0.05 * np.sin(2 * math.pi * 150 * time))
    fade = np.minimum(1.0, np.minimum(np.arange(n), np.arange(n)[::-1]) / length)
    return (0.6 * base + hum) * fade


def radio() -> np.ndarray:
    seconds = 0.9
    n = int(RATE * seconds)
    static = highpass(noise(seconds), 1800.0) * env(n, 0.01, 5.0, 1.2)
    squelch = beep(0.08, 1200.0)
    out = np.zeros(n)
    out[: len(squelch)] += 0.5 * squelch
    return 0.5 * static + out


def chime() -> np.ndarray:
    seconds = 1.4
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    tones = (np.sin(2 * math.pi * 660 * time) * 0.5 +
             np.sin(2 * math.pi * 990 * time) * 0.35 +
             np.sin(2 * math.pi * 1320 * time) * 0.2)
    return tones * env(n, 0.01, 4.0, 1.6)


def siren() -> np.ndarray:
    seconds = 2.4
    n = int(RATE * seconds)
    time = np.arange(n) / RATE
    sweep = 620 + 220 * np.sin(2 * math.pi * 0.7 * time)
    return 0.4 * np.sin(2 * math.pi * sweep * time) * env(n, 0.05, 3.0, 1.2)


WEAPONS = {
    # name: (crack, body, tail, bright, seconds)
    "rifle_545": (2400, 118, 5.2, 1.0, 0.85),
    "rifle_762": (2000, 96, 6.4, 0.85, 1.05),
    "smg_9": (3000, 168, 4.0, 1.1, 0.65),
    "dmr_54r": (1800, 82, 7.2, 0.75, 1.25),
    "sniper_338": (1500, 62, 8.6, 0.6, 1.5),
    "shotgun_12": (1200, 74, 5.6, 0.7, 1.15),
    "pistol_9": (3200, 190, 3.6, 1.15, 0.6),
}

SURFACES = ("concrete", "metal", "gravel", "wood", "sand", "water")


def main() -> None:
    count = 0
    for name, (crack, body, tail, bright, seconds) in WEAPONS.items():
        for variant in range(3):
            shot = gunshot(seconds, crack * (1 + 0.02 * variant), body * (1 - 0.01 * variant),
                           tail, bright)
            write_wav(f"fire_{name}_{variant + 1}.wav", shot, 0.82)
            count += 1
        far = distant(gunshot(seconds, crack, body, tail + 1.2, bright), 780.0)
        write_wav(f"fire_{name}_far.wav", far, 0.7)
        count += 1

    for surface in SURFACES:
        for variant in range(3):
            write_wav(f"step_{surface}_{variant + 1}.wav",
                      footstep(surface, 0.22 if surface != "metal" else 0.3), 0.7)
            count += 1

    for variant in range(2):
        landing = footstep("concrete", 0.34) * 1.1 + lowpass(noise(0.34), 300.0) * env(
            int(RATE * 0.34), 0.002, 12.0, 1.8)
        write_wav(f"land_{variant + 1}.wav", landing, 0.75)
        count += 1

    write_wav("mag_out.wav", metallic(0.34, 780.0), 0.7)
    write_wav("mag_in.wav", metallic(0.30, 620.0), 0.75)
    write_wav("bolt_release.wav", click(0.18, 1500.0, 30.0), 0.8)
    write_wav("charge_handle.wav", metallic(0.42, 900.0), 0.7)
    write_wav("dry_click.wav", click(0.09, 2100.0, 40.0), 0.7)
    write_wav("inspect_rustle.wav",
              lowpass(noise(0.5), 2200.0) * env(int(RATE * 0.5), 0.02, 6.0, 1.4), 0.4)
    write_wav("hit_flesh_1.wav", hit_flesh(), 0.85)
    write_wav("hit_flesh_2.wav", hit_flesh()[::-1] * 0.9, 0.8)
    write_wav("hit_armor_1.wav", hit_armor(), 0.8)
    write_wav("hit_armor_2.wav", hit_armor()[::-1] * 0.95, 0.75)
    write_wav("impact_concrete.wav", highpass(noise(0.2), 1800.0) *
              env(int(RATE * 0.2), 0.0008, 24.0, 2.2), 0.6)
    write_wav("impact_metal.wav", metallic(0.26, 1300.0), 0.6)
    write_wav("whizz_1.wav", whizz(), 0.7)
    write_wav("whizz_2.wav", whizz()[::-1], 0.65)
    write_wav("grenade_pin.wav", click(0.2, 900.0, 26.0), 0.7)
    write_wav("grenade_bounce.wav", metallic(0.3, 520.0), 0.65)
    write_wav("grenade_fuse.wav", click(0.25, 700.0, 18.0), 0.6)
    write_wav("bodycam_beep.wav", beep(0.12, 1750.0), 0.45)
    write_wav("bodycam_beep_double.wav",
              np.concatenate([beep(0.09, 1750.0), np.zeros(int(RATE * 0.05)),
                              beep(0.09, 1750.0)]), 0.45)
    write_wav("breath_in.wav", breathing(0.85, True), 0.5)
    write_wav("breath_out.wav", breathing(0.95, False), 0.5)
    write_wav("heartbeat.wav",
              np.concatenate([hit_flesh() * 0.7,
                              np.zeros(int(RATE * 0.12)),
                              hit_flesh() * 0.45]), 0.45)
    write_wav("wind_loop.wav", ambience(6.0, True), 0.35)
    write_wav("indoor_hum_loop.wav", ambience(6.0, False), 0.3)
    write_wav("radio_static.wav", radio(), 0.4)
    write_wav("objective_chime.wav", chime(), 0.5)
    write_wav("extraction_siren.wav", siren(), 0.5)
    count += 25

    print(f"wrote {count} clips to {OUT}")
    total = sum(os.path.getsize(os.path.join(OUT, f)) for f in os.listdir(OUT))
    print(f"bank size: {total / 1024:.0f} KB")


if __name__ == "__main__":
    main()
