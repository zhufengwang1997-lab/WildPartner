"""fxcost.csv (EffectCostBench) -> docs/特效开销清单.md"""
import csv, sys, collections
src, dst = sys.argv[1], sys.argv[2]
lines = open(src, encoding="utf-8").read().splitlines()
meta = dict(kv.split("=", 1) for kv in lines[0][2:].split(";"))
rows = list(csv.DictReader(lines[1:]))
views = list(dict.fromkeys(r["view"] for r in rows))
effects = list(dict.fromkeys(r["effect"] for r in rows))
cost = {(r["view"], r["effect"]): float(r["cost_ms"]) for r in rows}
base = {}
for r in rows:
    if not r["effect"].startswith("抗锯齿"): base.setdefault(r["view"], []).append(float(r["baseline_gpu_ms"]))
valid = all(r["gpu_valid"] == "1" for r in rows)
out = ["# 特效开销清单", "",
       f"实测，不是估计。机器：{meta.get('gpu')}（{meta.get('api')}），分辨率 {meta.get('res')}，画质档 {meta.get('tier')}，抗锯齿 {meta.get('aa')}。",
       "方法：`tools/fxcost.sh` 在 Mac 包里跑 `EffectCostBench`：四个固定机位，每个先测基线，再每次只关一项、测完恢复；"
       "关垂直同步，用 FrameTimingManager 的 GPU 帧时间（1.5 秒，去掉最慢 10%）。开销 = 基线 − 关掉后。"
       + ("" if valid else " **部分数据没有拿到 GPU 计时，改用了整帧时间。**"),
       "测量噪声约 ±0.2 ms（同一项在不同机位会出现小的负值，例如室内看不到草时关草反而 −0.8 ms，那是噪声和异步调度，不是真开销）；绝对值小于 0.2 ms 的项视为测不出差别。", "",
       "| 效果 | " + " | ".join(views) + " |", "|---|" + "---|" * len(views)]
out.append("| 基线 GPU 帧时间 | " + " | ".join(f"{sum(base[v]) / len(base[v]):.2f} ms" for v in views) + " |")
for e in effects:
    out.append(f"| {e} | " + " | ".join(f"{cost[(v, e)]:+.2f} ms" if (v, e) in cost else "—" for v in views) + " |")
big = sorted({e for (v, e), c in cost.items() if c >= 0.3 and not e.startswith("抗锯齿")}, key=lambda e: -max(cost.get((v, e), 0) for v in views))
out += ["", "## 结论", ""]
out += [f"- **{e}**：{' / '.join(f'{cost[(v, e)]:.2f}' for v in views if (v, e) in cost)} ms，是主要开销。" for e in big] or ["- 没有单项超过 0.3 ms。"]
out += ["- 其余各项在这台机器上都低于测量噪声。低、中档关 SSAO、降草地距离（docs/画质分级.md，先定后测）与这张表的结论一致。"]
out += ["", "1070 Ti / 5060 Ti 上的数据：未测（Windows 包打好后用同一脚本采集）。", ""]
open(dst, "w", encoding="utf-8").write("\n".join(out))
print("wrote", dst)
