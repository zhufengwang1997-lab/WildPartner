"""FR-VB-05: one platform's report from bench.csv (+ quality.csv).

    python bench_report.py <bench_dir> <out.md>

Writes the "backend x quality" table (FLIP, SSIM, PSNR, temporal stability, FPS, upscale ms, memory), a
quality-vs-frame-rate scatter plot (PNG next to the report) and a recommended default with the reason. Every number
comes from the CSVs in <bench_dir>.
"""
import csv, os, sys
from collections import defaultdict


def read_bench(path):
    lines = open(path, encoding="utf-8").read().splitlines()
    meta = dict(kv.split("=", 1) for kv in lines[0][2:].split(";")) if lines and lines[0].startswith("#") else {}
    rows = list(csv.DictReader(lines[1:] if meta else lines))
    return meta, rows


def read_quality(path):
    if not os.path.exists(path):
        return {}
    return {(r["scene"], r["config"]): r for r in csv.DictReader(open(path, encoding="utf-8"))}


def fnum(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def mean(xs):
    xs = [x for x in xs if x is not None]
    return sum(xs) / len(xs) if xs else None


def summarise(rows, quality):
    """config -> averaged metrics over scenes."""
    by = defaultdict(list)
    for r in rows:
        by[r["config"]].append(r)
    out = {}
    for cfg, rs in by.items():
        q = [quality.get((r["scene"], cfg), {}) for r in rs]
        gpu = mean([fnum(r["gpu_ms"]) for r in rs])
        out[cfg] = {
            "backend": rs[0]["backend"], "quality": rs[0]["quality"], "aa": rs[0]["aa"],
            "render": f'{rs[0]["render_w"]}x{rs[0]["render_h"]}',
            "gpu_ms": gpu, "fps": (1000.0 / gpu) if gpu else None,
            "upscale_ms": mean([fnum(r["upscale_ms"]) for r in rs]),
            "mem": mean([fnum(r["gfx_mem_mb"]) for r in rs]),
            "temporal": mean([fnum(r["temporal_diff"]) for r in rs]),
            "flip": mean([fnum(x.get("flip")) for x in q]),
            "ssim": mean([fnum(x.get("ssim")) for x in q]),
            "psnr": mean([fnum(x.get("psnr")) for x in q]),
            "scenes": len(rs),
        }
    return out


def recommend(summary, flip_budget=None):
    """Fastest configuration whose FLIP is within flip_budget of the best native AA (default: +10 % relative)."""
    natives = [v["flip"] for k, v in summary.items() if k.startswith("native_") and v["flip"] is not None]
    if not natives:
        return None, "没有画质数据，无法推荐"
    best = min(natives)
    budget = flip_budget if flip_budget is not None else best * 1.10
    ok = [(k, v) for k, v in summary.items() if k != "ref" and v["flip"] is not None and v["flip"] <= budget and v["gpu_ms"]]
    if not ok:
        return None, "没有配置达到画质门槛"
    k, v = min(ok, key=lambda kv: kv[1]["gpu_ms"])
    return k, f"FLIP {v['flip']:.4f} 不超过最佳原生抗锯齿（{best:.4f}）的 110%，其中 GPU 帧时间最短（{v['gpu_ms']:.2f} ms）"


def fmt(v, p=3):
    return "—" if v is None else f"{v:.{p}f}"


def scatter(summary, png):
    try:
        import matplotlib; matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return False
    pts = [(k, v) for k, v in summary.items() if k != "ref" and v["flip"] is not None and v["fps"]]
    if not pts:
        return False
    fig, ax = plt.subplots(figsize=(7, 4.5), dpi=120)
    for k, v in pts:
        ax.scatter(v["fps"], v["flip"], s=36)
        ax.annotate(k, (v["fps"], v["flip"]), fontsize=7, xytext=(4, 3), textcoords="offset points")
    ax.set_xlabel("FPS (GPU frame time, higher is better)"); ax.set_ylabel("mean FLIP vs 2x reference (lower is better)")
    ax.grid(alpha=0.3); fig.tight_layout(); fig.savefig(png); plt.close(fig)
    return True


def report(bench_dir, out_md):
    meta, rows = read_bench(os.path.join(bench_dir, "bench.csv"))
    quality = read_quality(os.path.join(bench_dir, "quality.csv"))
    s = summarise(rows, quality)
    png = os.path.splitext(out_md)[0] + "_scatter.png"
    has_plot = scatter(s, png)
    rec, why = recommend(s)
    rel = os.path.relpath(bench_dir, os.path.dirname(out_md) or ".")
    L = [f"# 超分评估报告：{meta.get('gpu', '?')}（{meta.get('api', '?')}）", "",
         f"数据：`{rel}/bench.csv`、`quality.csv`（{meta.get('date', '')}，显示 {meta.get('display', '?')}，画质档 {meta.get('tier', '?')}，Unity {meta.get('unity', '?')}）。"
         f"每行是 {len(set(r['scene'] for r in rows))} 个场景的平均值；参考图 = 2 倍超采样（渲染比例 2 再缩小）。",
         "", "| 配置 | 渲染分辨率 | FLIP ↓ | SSIM ↑ | PSNR ↑ | 时间稳定性 ↓ | GPU ms | FPS | 超分 Pass ms | 显存 MB |",
         "|---|---|---|---|---|---|---|---|---|---|"]
    order = sorted(s.keys(), key=lambda k: (k != "ref", not k.startswith("native"), k))
    for k in order:
        v = s[k]
        L.append(f"| {k} | {v['render']} | {fmt(v['flip'], 4)} | {fmt(v['ssim'], 4)} | {fmt(v['psnr'], 2)} | {fmt(v['temporal'], 5)} | {fmt(v['gpu_ms'], 2)} | {fmt(v['fps'], 0)} | {fmt(v['upscale_ms'], 3)} | {fmt(v['mem'], 0)} |")
    L += ["", "时间稳定性只在静止场景（SC-02 草地、SC-06 远山）测：60 帧相邻帧亮度差的平均值，越大越闪。",
          "超分 Pass ms 来自 URP 的性能采样器（STP 三个 Pass；FSR1 / 双线性的最终放大 Pass）；为 0 表示该平台的发布版不提供采样器的 GPU 计时。", ""]
    if has_plot:
        L += [f"![画质–帧率]({os.path.basename(png)})", ""]
    L += ["## 默认配置建议", "", f"**{rec}**：{why}。" if rec else why, ""]
    open(out_md, "w", encoding="utf-8").write("\n".join(L))
    return s, rec


if __name__ == "__main__":
    s, rec = report(sys.argv[1], sys.argv[2])
    print(f"{len(s)} configs, recommended {rec} -> {sys.argv[2]}")
