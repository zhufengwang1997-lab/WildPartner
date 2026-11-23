"""TC-10: soak.csv -> markdown (frame rate first vs last minute, memory growth, crash or not)."""
import csv, os, sys


def main(d, out_md):
    path = os.path.join(d, "soak.csv")
    lines = open(path, encoding="utf-8").read().splitlines()
    meta = dict(kv.split("=", 1) for kv in lines[0][1:].strip().split(";") if "=" in kv)
    rows = list(csv.DictReader([l for l in lines if not l.startswith("#")]))
    t = [int(r["t"]) for r in rows]; fps = [int(r["fps"]) for r in rows]; mem = [float(r["total_mem_mb"]) for r in rows]
    worst = [float(r["worst_ms"]) for r in rows]
    first = [f for tt, f in zip(t, fps) if 30 <= tt < 90]       # first minute after 30 s warmup
    last = [f for tt, f in zip(t, fps) if tt >= t[-1] - 60]
    fa, la = sum(first) / max(len(first), 1), sum(last) / max(len(last), 1)
    m0 = sum(m for tt, m in zip(t, mem) if 30 <= tt < 90) / max(len(first), 1)
    m1 = sum(m for tt, m in zip(t, mem) if tt >= t[-1] - 60) / max(len(last), 1)
    exitcode = open(os.path.join(d, "exit.txt")).read().strip() if os.path.exists(os.path.join(d, "exit.txt")) else "?"
    finished = t[-1] >= float(meta.get("seconds", 0)) - 2
    drop = (la / fa - 1) if fa else 0
    ok = finished and drop > -0.10
    rel = os.path.relpath(d, os.path.dirname(out_md) or ".")
    L = [f"# TC-10 挂机测试：{meta.get('gpu', '?')}（{meta.get('api', '?')}）", "",
         f"数据：`{rel}/soak.csv`。{meta.get('res', '?')}，画质档 {meta.get('tier', '?')}，超分 {meta.get('upscaler', '?')}，时长 {meta.get('seconds')} s（说明书要求 30 分钟，最终指令压缩为 10 分钟）。",
         "女巫自动随机走跑，每 2 分钟随机切换晴雨，昼夜照常流动。帧率按游戏默认的垂直同步。", "",
         "| 项目 | 结果 |", "|---|---|",
         f"| 跑完全程不崩溃 | {'是' if finished else '否'}（{t[-1]} s，{exitcode}） |",
         f"| 帧率：第 1 分钟（30–90 s）→ 最后 1 分钟 | {fa:.1f} → {la:.1f} FPS（{drop * 100:+.1f}%） |",
         f"| 最慢一帧（全程） | {max(worst):.1f} ms |",
         f"| 内存：第 1 分钟 → 最后 1 分钟 | {m0:.0f} → {m1:.0f} MB（{m1 - m0:+.0f} MB） |",
         f"| GC 次数（全程） | {rows[-1]['gc_count']} |", "",
         f"**结论**：{'通过' if ok else '未通过'}（标准：跑完不崩溃，最后 1 分钟帧率不低于第 1 分钟的 90%）。"]
    try:
        import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
        fig, ax = plt.subplots(figsize=(8, 3), dpi=110)
        ax.plot(t, fps, lw=0.8); ax.set_xlabel("seconds"); ax.set_ylabel("FPS")
        ax2 = ax.twinx(); ax2.plot(t, mem, color="tab:orange", lw=0.8); ax2.set_ylabel("total memory MB")
        fig.tight_layout(); png = os.path.splitext(out_md)[0] + ".png"; fig.savefig(png); plt.close(fig)
        L += ["", f"![帧率与内存曲线]({os.path.basename(png)})"]
    except ImportError:
        pass
    open(out_md, "w", encoding="utf-8").write("\n".join(L) + "\n")
    print(f"soak {'PASS' if ok else 'FAIL'} fps {fa:.1f}->{la:.1f} mem {m0:.0f}->{m1:.0f} -> {out_md}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
