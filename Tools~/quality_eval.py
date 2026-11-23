"""FR-VB-03: image quality of every bench capture against its scene's reference.

    python quality_eval.py <ref_dir> <test_dir> [--out quality.csv] [--heatmaps <dir>]

<ref_dir> holds "<scene>_ref.png" (2x supersampled), <test_dir> the "<scene>_<config>.png" captures (they may be the
same directory). Per image: FLIP (main metric, NVIDIA flip-evaluator, LDR, 67 ppd; lower is better), SSIM (luma,
scikit-image), PSNR (dB). FLIP heat maps (magma) go to --heatmaps. Images of different sizes are resized to the
reference size first (they should all be the display size).
"""
import argparse, csv, glob, os, sys
import numpy as np
from PIL import Image


def load(path, size=None):
    im = Image.open(path).convert("RGB")
    if size and im.size != size:
        im = im.resize(size, Image.LANCZOS)
    return np.asarray(im).astype(np.float32) / 255.0


def luma(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def psnr(ref, test):
    mse = float(np.mean((ref - test) ** 2))
    return float("inf") if mse == 0 else 10.0 * np.log10(1.0 / mse)


def ssim(ref, test):
    from skimage.metrics import structural_similarity
    return float(structural_similarity(luma(ref), luma(test), data_range=1.0))


def flip(ref, test):
    import flip_evaluator
    err, mean, _ = flip_evaluator.evaluate(ref, test, "LDR", inputsRGB=True, applyMagma=True, computeMeanError=True)
    return float(mean), err


def split_name(path):
    """'SC-02_STP_Quality.png' -> ('SC-02', 'STP_Quality')"""
    base = os.path.splitext(os.path.basename(path))[0]
    scene, _, config = base.partition("_")
    return scene, config


def evaluate(ref_dir, test_dir, heat_dir=None, use_flip=True):
    rows = []
    for path in sorted(glob.glob(os.path.join(test_dir, "SC-*_*.png"))):
        scene, config = split_name(path)
        if config == "ref":
            continue
        ref_path = os.path.join(ref_dir, f"{scene}_ref.png")
        if not os.path.exists(ref_path):
            print(f"warning: no reference for {scene}", file=sys.stderr)
            continue
        ref = load(ref_path)
        test = load(path, (ref.shape[1], ref.shape[0]))
        row = {"scene": scene, "config": config, "psnr": round(psnr(ref, test), 3), "ssim": round(ssim(ref, test), 5)}
        if use_flip:
            m, err = flip(ref, test)
            row["flip"] = round(m, 5)
            if heat_dir:
                os.makedirs(heat_dir, exist_ok=True)
                Image.fromarray((np.clip(err, 0, 1) * 255).astype(np.uint8)).save(os.path.join(heat_dir, f"{scene}_{config}_flip.png"))
        rows.append(row)
    return rows


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("ref_dir"); ap.add_argument("test_dir")
    ap.add_argument("--out", default=None); ap.add_argument("--heatmaps", default=None)
    a = ap.parse_args(argv)
    rows = evaluate(a.ref_dir, a.test_dir, a.heatmaps)
    out = a.out or os.path.join(a.test_dir, "quality.csv")
    with open(out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["scene", "config", "flip", "ssim", "psnr"])
        w.writeheader(); w.writerows(rows)
    print(f"{len(rows)} images -> {out}")


if __name__ == "__main__":
    main()
