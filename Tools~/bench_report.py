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
        frame = mean([fnum(r.get("frame_ms")) for r in rs])
        out[cfg] = {
            "backend": rs[0]["backend"], "quality": rs[0]["quality"], "aa": rs[0]["aa"],
            "render": f'{rs[0]["render_w"]}x{rs[0]["render_h"]}',
            "gpu_ms": gpu, "frame_ms": frame,
            # frame rate from wall-clock frame time (v-sync off, uncapped): what the player gets. FrameTimingManager's
            # GPU time on Metal includes frames in flight (it reads ~2x the frame time), so it is shown as relative only.
            "fps": (1000.0 / frame) if frame else ((1000.0 / gpu) if gpu else None),
            "upscale_ms": mean([x for x in (fnum(r["upscale_ms"]) for r in rs) if x is not None and x > 0]),
            "mem": mean([x for x in (fnum(r["gfx_mem_mb"]) for r in rs) if x is not None and x >= 0]),
            "temporal": mean([fnum(r["temporal_diff"]) for r in rs]),
            "flip": mean([fnum(x.get("flip")) for x in q]),
            "ssim": mean([fnum(x.get("ssim")) for x in q]),
            "psnr": mean([fnum(x.get("psnr")) for x in q]),
            "scenes": len(rs),
        }
    return out


def recommend(summary, flip_budget=None):
    """Fastest configuration whose FLIP is within flip_budget of the best native AA (default: +10 % relative)."""
    # "native anti-aliasing" = native_taa / native_stp. native_off is no anti-aliasing: the 2x supersampled reference has
    # no temporal filter either, so FLIP favours it while its flicker (temporal column) is about twice as high.
    natives = [v["flip"] for k, v in summary.items() if k.startswith("native_") and k != "native_off" and v["flip"] is not None]
    if not natives:
        return None, "没有画质数据，无法推荐"
    best = min(natives)
    budget = flip_budget if flip_budget is not None else best * 1.10
    ok = [(k, v) for k, v in summary.items() if not k.startswith("ref") and k != "native_off" and v["flip"] is not None and v["flip"] <= budget and v["fps"]
          and not k.endswith("_noMV")]
    if not ok:
        return None, "没有配置达到画质门槛"
    k, v = max(ok, key=lambda kv: kv[1]["fps"])
    return k, f"FLIP {v['flip']:.4f} 不超过最佳原生抗锯齿（{best:.4f}）的 110%，其中帧率最高（{v['fps']:.0f} FPS）"


def nfr01(summary):
    """NFR-01: quality-tier frame rate vs native with the same anti-aliasing, >= +40 %."""
    pairs = [("STP_Quality", "native_stp"), ("FSR1_Quality", "native_taa"), ("RenderScale_Quality", "native_taa")]
    out = []
    for up, nat in pairs:
        if up in summary and nat in summary and summary[up]["fps"] and summary[nat]["fps"]:
            gain = summary[up]["fps"] / summary[nat]["fps"] - 1.0
            out.append((up, nat, gain, gain >= 0.40))
    return out


def fmt(v, p=3):
    return "—" if v is None else f"{v:.{p}f}"


def scatter(summary, png):
    try:
        import matplotlib; matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return False
    pts = [(k, v) for k, v in summary.items() if not k.startswith("ref") and v["flip"] is not None and v["fps"]]
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
         "", "| 配置 | 渲染分辨率 | FLIP ↓ | SSIM ↑ | PSNR ↑ | 时间稳定性 ↓ | 帧时间 ms | FPS | GPU ms（相对） | 超分 Pass ms | 显存 MB |",
         "|---|---|---|---|---|---|---|---|---|---|---|"]
    order = sorted(s.keys(), key=lambda k: (not k.startswith("ref"), not k.startswith("native"), k))
    for k in order:
        v = s[k]
        L.append(f"| {k} | {v['render']} | {fmt(v['flip'], 4)} | {fmt(v['ssim'], 4)} | {fmt(v['psnr'], 2)} | {fmt(v['temporal'], 5)} | {fmt(v['frame_ms'], 2)} | {fmt(v['fps'], 0)} | {fmt(v['gpu_ms'], 2)} | {fmt(v['upscale_ms'], 3)} | {fmt(v['mem'], 0)} |")
    L += ["", "时间稳定性只在静止场景（SC-02 草地、SC-06 远山）测：60 帧相邻帧亮度差的平均值，越大越闪。",
          "帧时间：关垂直同步、不限帧率时的实际每帧耗时，FPS 由它算出。GPU ms 来自 FrameTimingManager，在 Metal 上包含多帧并行的排队时间（约为帧时间的 2 倍），只能用来比较配置之间的相对高低。",
          "超分 Pass ms、显存：来自 URP 性能采样器和 Gfx Used Memory 计数器，发布版不提供时显示「—」。", ""]
    floor = s.get("ref2", {}).get("flip")
    if floor is not None:
        spread = [v["flip"] for k, v in s.items() if not k.startswith("ref") and not k.endswith("_noMV") and v["flip"] is not None]
        L += [f"**噪声底**：同一参考配置拍两次（ref2 对 ref），FLIP = {floor:.4f}。这是测试本身控制不住的差异（鱼群、小动物等按真实时间动的东西）。"
              f"其余配置之间 FLIP 最大差 {max(spread) - min(spread):.4f}。所有配置都带着同一份噪声，所以单个差值要谨慎看；更可靠的依据是下面逐场景表里的排序是否在 6 个场景中一致。", ""]
    scenes = sorted({sc for (sc, _c) in quality})
    if scenes:
        L += ["### 逐场景 FLIP", "", "| 配置 | " + " | ".join(scenes) + " |", "|---|" + "---|" * len(scenes)]
        for k in order:
            if k == "ref":
                continue
            L.append(f"| {k} | " + " | ".join(fmt(fnum(quality[(sc, k)]["flip"]), 3) if (sc, k) in quality else "—" for sc in scenes) + " |")
        def wins(a, b):
            common = [sc for sc in scenes if (sc, a) in quality and (sc, b) in quality]
            return sum(fnum(quality[(sc, a)]["flip"]) < fnum(quality[(sc, b)]["flip"]) for sc in common), len(common)
        L += [""]
        for a, b in [("STP_Quality", "FSR1_Quality"), ("STP_Quality", "RenderScale_Quality"), ("FSR1_Quality", "RenderScale_Quality"), ("STP_Quality", "STP_Performance")]:
            if a in s and b in s:
                w, n = wins(a, b)
                L.append(f"- {a} 比 {b} 误差小的场景：{w}/{n}")
        L += [""]
    a, b = quality.get(("SC-01", "STP_Quality")), quality.get(("SC-01", "STP_Quality_noMV"))
    if a and b:
        L += ["## TC-05：去掉物体运动向量", "",
              f"SC-01（跑动）上 STP 质量档：有运动向量 FLIP {fnum(a['flip']):.4f}，强制关掉物体运动向量后 {fnum(b['flip']):.4f}"
              f"（{(fnum(b['flip']) / fnum(a['flip']) - 1) * 100:+.0f}%）。对比图：`SC-01_STP_Quality.png` 与 `SC-01_STP_Quality_noMV.png`。", ""]
    n = nfr01(s)
    if n:
        L += ["## NFR-01：质量档帧率比原生提升 ≥ 40%", "", "| 超分配置 | 对比的原生配置 | 帧率提升 | 结论 |", "|---|---|---|---|"]
        L += [f"| {u} | {nn} | {g * 100:+.0f}% | {'达标' if ok else '未达标'} |" for u, nn, g, ok in n]
        L += [""]
    if has_plot:
        L += [f"![画质–帧率]({os.path.basename(png)})", ""]
    L += ["## 默认配置建议", "", f"**{rec}**：{why}。" if rec else why, "",
          "规则：在 FLIP 不超过最佳原生抗锯齿（native_taa / native_stp）110% 的配置里取帧率最高的。"
          "native_off（不抗锯齿）不参加：参考图是不带时间滤波的超采样，FLIP 天然偏向它，但它的闪烁（时间稳定性一列）约是 STP 的 2 倍。"
          "这条规则只看画质和帧率，游戏里的自动档位仍按说明书映射（高档 → 质量档），推荐结果供演示前手动调整参考。", ""]
    open(out_md, "w", encoding="utf-8").write("\n".join(L))
    return s, rec


if __name__ == "__main__":
    s, rec = report(sys.argv[1], sys.argv[2])
    print(f"{len(s)} configs, recommended {rec} -> {sys.argv[2]}")
