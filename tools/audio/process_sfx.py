"""Turns the downloaded CC0 recordings (docs/CREDITS.md) into the game's footstep / rain clips.
Usage: python3 tools/audio/process_sfx.py <fantozzi flac dir> <splash dir> <amb_rain_loop_1.ogg>
Writes Assets/Resources/Audio/*.ogg (mono 44.1 kHz except the rain bed)."""
import sys, os, numpy as np, soundfile as sf
from scipy.signal import butter, sosfilt, resample_poly

OUT = "Assets/Resources/Audio"; os.makedirs(OUT, exist_ok=True)
SR = 44100
fz, splash, rain = sys.argv[1], sys.argv[2], sys.argv[3]

def load(p):
    d, sr = sf.read(p, always_2d=True); m = d.mean(1)
    if sr != SR: m = resample_poly(m, SR, sr)
    return m

def filt(x, kind, f, order=2):
    return sosfilt(butter(order, f, kind, fs=SR, output="sos"), x)

def pitch(x, factor):      # >1 = higher and shorter
    up, down = 100, int(round(100 * factor))
    return resample_poly(x, up, down)

def trim_onset(x, thr=0.04):
    i = np.argmax(np.abs(x) > thr * np.abs(x).max()); return x[max(i - 60, 0):]

def finish(x, length, peak, fade=0.12):
    x = x[:int(length * SR)]
    n = len(x); f = int(fade * SR)
    env = np.ones(n); env[-f:] = np.linspace(1, 0, f) ** 2; env[:40] = np.linspace(0, 1, 40)
    x = x * env
    return x / (np.abs(x).max() + 1e-9) * peak

def save(name, x): sf.write(f"{OUT}/{name}.ogg", x.astype(np.float32), SR, format="OGG", subtype="VORBIS"); print("wrote", name, f"{len(x)/SR:.2f}s")

sand = [load(f"{fz}/Fantozzi-Sand{s}.flac") for s in ["L1", "L2", "L3", "R1", "R2", "R3"]]
stone = [load(f"{fz}/Fantozzi-Stone{s}.flac") for s in ["L1", "L2", "L3", "R1"]]
for i, s in enumerate(sand):
    s = trim_onset(s)
    # grass: soft and rustly — roll off the hard transient highs, keep the swish body
    g = filt(filt(s, "lowpass", 3800, 4), "highpass", 160)
    save(f"step_grass_{i}", finish(g, 0.34, 0.55, 0.16))
    # dirt path: drier and crisper — keep the highs, cut the low body, slightly higher pitch, shorter tail
    d = filt(pitch(s, 1.07), "highpass", 450)
    save(f"step_dirt_{i}", finish(d, 0.24, 0.5, 0.1))
for i, s in enumerate(stone):
    # wooden floor: a soft low knock — heavy low-pass and pitched down
    w = filt(pitch(trim_onset(s), 0.8), "lowpass", 900, 4)
    save(f"step_wood_{i}", finish(w, 0.28, 0.6, 0.15))
for i, n in enumerate(["splash_02", "splash_04", "splash_09", "splash_06"]):
    x = filt(trim_onset(load(f"{splash}/{n}.ogg")), "lowpass", 5500, 2)
    save(f"step_water_{i}", finish(x, 0.42, 0.5, 0.2))

# rain bed: stereo, soft (tame the hiss above ~6 kHz), loop seam cross-faded over 2 s
d, sr = sf.read(rain, always_2d=True)
d = np.stack([resample_poly(d[:, c], SR, sr) for c in range(d.shape[1])], 1)
d = np.stack([filt(filt(d[:, c], "lowpass", 6000, 2), "highpass", 90) for c in range(2)], 1)
cf = 2 * SR
body, tail = d[:-cf], d[-cf:]
t = np.linspace(0, 1, cf)[:, None]
body[:cf] = body[:cf] * np.sqrt(t) + tail * np.sqrt(1 - t)
body = body / np.abs(body).max() * 0.7
sf.write(f"{OUT}/rain_loop.ogg", body.astype(np.float32), SR, format="OGG", subtype="VORBIS"); print("wrote rain_loop", f"{len(body)/SR:.1f}s")
