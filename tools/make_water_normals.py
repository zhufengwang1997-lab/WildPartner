"""Two tileable water normal maps for WildPartner/Water (试玩反馈 1123c-C2), from band-limited FFT noise:
  WaterNormal_A.png - broad soft swells;  WaterNormal_B.png - fine choppy ripples.
Usage: python3 tools/make_water_normals.py  (writes Assets/_Game/Textures/Water/)"""
import numpy as np, os
from PIL import Image
OUT = "Assets/_Game/Textures/Water"; os.makedirs(OUT, exist_ok=True)
N = 512
def height(seed, k_lo, k_hi, slope, aniso=1.0):
    rng = np.random.default_rng(seed)
    kx = np.fft.fftfreq(N) * N; ky = np.fft.fftfreq(N) * N
    KX, KY = np.meshgrid(kx, ky)
    k = np.sqrt(KX ** 2 + (KY * aniso) ** 2) + 1e-6
    amp = np.where((k >= k_lo) & (k <= k_hi), k ** -slope, 0.0)
    ph = rng.uniform(0, 2 * np.pi, (N, N))
    h = np.real(np.fft.ifft2(amp * np.exp(1j * ph)))
    return (h - h.mean()) / (h.std() + 1e-9)
def normal_map(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-dx, -dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB")
normal_map(height(11, 2, 14, 1.6), 0.35).save(f"{OUT}/WaterNormal_A.png")
normal_map(height(23, 8, 60, 1.2, 1.3), 0.22).save(f"{OUT}/WaterNormal_B.png")
print("written", OUT)
