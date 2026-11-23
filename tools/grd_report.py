"""FR-RD-06 / NFR-03: GRD off vs on -> docs/GRD评估报告.md.
    python3 grd_report.py <dir with grd_off.csv, grd_on.csv> <out.md>
"""
import csv, os, sys


def read(path):
    meta, rows = {}, []
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()
    for ln in lines:
        if ln.startswith("#"):
            for kv in ln[1:].strip().split(";"):
                if "=" in kv:
                    k, v = kv.split("=", 1); meta[k.strip()] = v.strip()
    rows = list(csv.DictReader([l for l in lines if not l.startswith("#")]))
    return meta, {r["view"]: r for r in rows}


def pct(a, b):
    a, b = float(a), float(b)
    return "—" if a <= 0 else f"{(b / a - 1) * 100:+.0f}%"


def report(d, out_md):
    m0, off = read(os.path.join(d, "grd_off.csv"))
    m1, on = read(os.path.join(d, "grd_on.csv"))
    rel = os.path.relpath(d, os.path.dirname(out_md) or ".")
    L = ["# GRD 评估报告（GPU Resident Drawer 开 / 关）", "",
         f"设备：{m0.get('gpu', '?')}（{m0.get('api', '?')}），{m0.get('res', '?')}，画质档 {m0.get('tier', '?')}。数据：`{rel}/grd_off.csv`、`grd_on.csv`（`tools/grd.sh`）。",
         f"GRD 实际状态：关的一次 = {m0.get('grd')}，开的一次 = {m1.get('grd')}（请求 {m1.get('grd_requested')}），GPU 遮挡剔除 = {m1.get('occlusion')}。", "",
         "同一机位各采 3 秒取中位数。主线程 / 渲染线程来自 FrameTimingManager，帧时间是实际每帧耗时（关垂直同步）。", "",
         "| 机位 | Draw Calls 关→开 | 变化 | Batches 关→开 | SetPass 关→开 | 主线程 ms 关→开 | 渲染线程 ms 关→开 | 帧时间 ms 关→开 |",
         "|---|---|---|---|---|---|---|---|"]
    gains = []
    for v in off:
        if v not in on:
            continue
        a, b = off[v], on[v]
        gains.append(float(b["draw_calls"]) / max(float(a["draw_calls"]), 1) - 1)
        L.append(f"| {v} | {a['draw_calls']} → {b['draw_calls']} | {pct(a['draw_calls'], b['draw_calls'])} | {a['batches']} → {b['batches']} | "
                 f"{a['setpass_calls']} → {b['setpass_calls']} | {a['main_ms']} → {b['main_ms']} | {a['render_ms']} → {b['render_ms']} | {a['frame_ms']} → {b['frame_ms']} |")
    avg = sum(gains) / len(gains) if gains else 0
    ok = avg <= -0.5
    L += ["", f"**NFR-03（GRD 开启后 Draw Call 降低 ≥ 50%）**：四个机位平均 {avg * 100:+.0f}%，{'达标' if ok else '未达标'}。", "",
          f"**场景加载时间**：关 {float(m0.get('load_s', 0)):.2f} s，开 {float(m1.get('load_s', 0)):.2f} s，增量 {float(m1.get('load_s', 0)) - float(m0.get('load_s', 0)):+.2f} s"
          "（从启动到世界生成完毕，不含固定的 1.5 s 等待；只跑了一次，误差未知）。", ""]
    with open(out_md, "w", encoding="utf-8") as f:
        f.write("\n".join(L) + "\n")
    print(f"NFR-03 {avg * 100:+.0f}% -> {out_md}")


if __name__ == "__main__":
    report(sys.argv[1], sys.argv[2])
