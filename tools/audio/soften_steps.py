"""试玩反馈 1123e-4: softer footsteps - a quiet rustle, no click or slap.
Sources: Art/Audio/src/step_*.ogg (the CC0 clips made by process_sfx.py, kept as sources so this is repeatable).
Writes Assets/Resources/Audio/step_*.ogg.
 - grass: the body below ~2.2 kHz with a slow (18 ms) attack so the heel transient is gone, plus a faint
   band-passed rustle (grass blades brushing) shaped by the step's own envelope; 0.22 s.
 - dirt: same treatment, a little drier; wood: a soft low knock with a rounded attack; water: a gentle slosh.
All peaks are normalised low (loudness is balanced in the game's mixer, ProceduralAudio)."""
import glob, os, numpy as np, soundfile as sf
from scipy.signal import butter, sosfilt

SR = 44100
SRC, OUT = "Art/Audio/src", "Assets/Resources/Audio"
rng = np.random.default_rng(1123)

def filt(x, kind, f, order=2):
    return sosfilt(butter(order, f, kind, fs=SR, output="sos"), x)

def envelope(x, ms=12):
    k = max(int(SR * ms / 1000), 1)
    return np.convolve(np.abs(x), np.ones(k) / k, mode="same")

def soften(x, attack_ms, length, peak, fade=0.08):
    x = x[:int(length * SR)]
    n = len(x); a = int(attack_ms / 1000 * SR); f = int(fade * SR)
    env = np.ones(n)
    env[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 2          # rounded attack: no click
    env[-f:] *= np.linspace(1, 0, f) ** 2
    x = x * env
    x = np.tanh(x / (np.abs(x).max() + 1e-9) * 1.6) / np.tanh(1.6)  # gentle soft-knee: tames the remaining peak
    return x / (np.abs(x).max() + 1e-9) * peak

def rustle(x, lo, hi, gain):
    noise = rng.standard_normal(len(x))
    band = filt(filt(noise, "highpass", lo, 2), "lowpass", hi, 2)
    e = envelope(x, 20); e /= e.max() + 1e-9
    return band * e * gain

for p in sorted(glob.glob(f"{SRC}/step_*.ogg")):
    name = os.path.basename(p)[:-4]
    x, sr = sf.read(p); x = x if x.ndim == 1 else x.mean(1)
    if name.startswith("step_grass"):
        body = filt(filt(x, "lowpass", 2200, 4), "highpass", 140)
        y = soften(body + rustle(body, 1800, 5200, 0.10), 18, 0.22, 0.32)
    elif name.startswith("step_dirt"):
        body = filt(filt(x, "lowpass", 2600, 4), "highpass", 200)
        y = soften(body + rustle(body, 1500, 4000, 0.06), 14, 0.2, 0.3)
    elif name.startswith("step_wood"):
        body = filt(x, "lowpass", 700, 4)
        y = soften(body, 12, 0.22, 0.34)
    elif name.startswith("step_water"):
        body = filt(filt(x, "lowpass", 3200, 4), "highpass", 120)
        y = soften(body, 25, 0.36, 0.3, 0.14)
    else:
        continue
    # same loudness for every surface (RMS 0.045), never a peak above 0.35
    y = y * min(0.045 / (np.sqrt((y ** 2).mean()) + 1e-9), 0.35 / (np.abs(y).max() + 1e-9))
    sf.write(f"{OUT}/{name}.ogg", y.astype(np.float32), SR, format="OGG", subtype="VORBIS")
    print("wrote", name, f"{len(y)/SR:.2f}s peak {np.abs(y).max():.2f} rms {np.sqrt((y**2).mean()):.3f}")
