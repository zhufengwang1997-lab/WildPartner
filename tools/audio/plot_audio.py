"""Spectrogram sheets for docs/screenshots/feedback-1123 (old synthesised vs new recorded footsteps / rain)."""
import numpy as np, soundfile as sf, matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
plt.rcParams["font.sans-serif"] = ["Hiragino Sans GB", "PingFang SC", "Arial Unicode MS"]
SR = 44100
rng = np.random.default_rng(7)

def old_step():
    n = SR // 12; y = 0; d = np.zeros(n)
    for i in range(n):
        y += 0.3 * ((rng.random() * 2 - 1) - y); d[i] = y * np.exp(-i / n * 7) * 0.8
    return d

def old_rain(sec=6):
    n = SR * sec; d = np.zeros(n); y = y2 = 0
    w = rng.random(n) * 2 - 1
    for i in range(n):
        y += 0.25 * (w[i] - y); y2 += 0.125 * (y - y2); d[i] = y2 * 0.6 * 6
    return d

def ld(n):
    d, sr = sf.read(f"Assets/Resources/Audio/{n}.ogg", always_2d=True); return d.mean(1)

def spec(ax, x, title):
    ax.specgram(x, NFFT=512, Fs=SR, noverlap=384, cmap="magma", vmin=-120)
    ax.set_ylim(0, 16000); ax.set_title(title, fontsize=11); ax.set_xlabel("秒"); ax.set_ylabel("Hz")

fig, axs = plt.subplots(2, 3, figsize=(15, 7.5))
spec(axs[0, 0], old_step(), "旧：合成噪声脚步（硬、电子味）")
spec(axs[0, 1], np.concatenate([ld(f"step_grass_{i}") for i in range(3)]), "新：草地 ×3 变体（CC0 录音，柔化高频）")
spec(axs[0, 2], np.concatenate([ld(f"step_dirt_{i}") for i in range(3)]), "新：土路 ×3（更干、更脆）")
spec(axs[1, 0], np.concatenate([ld(f"step_wood_{i}") for i in range(3)]), "新：木地板 ×3（低沉的咚）")
spec(axs[1, 1], np.concatenate([ld(f"step_water_{i}") for i in range(3)]), "新：浅水 ×3（哗）")
axs[1, 2].axis("off")
axs[1, 2].text(0, 0.9, "触发方式：女巫走/跑/潜行动画的落脚帧（动画事件）\n冒烟日志：走 3 秒触发 5 次，间隔 0.43–0.45 s\n地面识别：草地 / 土路（地形贴图）/ 木屋内 / 浅水\n每次随机变体（不连续重复）、音高 ±5%、音量 ±15%\n跑：音高 0.95、音量大；潜行：音量约为走路的一半", va="top", fontsize=11)
fig.suptitle("反馈 1：脚步声", fontsize=14); fig.tight_layout()
fig.savefig("docs/screenshots/feedback-1123/1_footsteps.png", dpi=90)

fig, axs = plt.subplots(1, 3, figsize=(15, 4.2))
spec(axs[0], old_rain(), "旧：合成噪声雨声")
r = ld("rain_loop")[: SR * 6]
spec(axs[1], r, "新：CC0 雨声录音（6 kHz 以上压低，2 s 交叉淡化接缝）")
t = np.linspace(0, 20, 400)
vol = np.clip(t / 4, 0, 1) * 0.16 * (t < 12) + np.clip(1 - (t - 12) / 4, 0, 1) * 0.16 * (t >= 12)
axs[2].plot(t, vol, label="雨声音量"); axs[2].axhline(0.18, ls="--", c="g", label="背景音乐 0.18"); axs[2].axhline(0.36, ls=":", c="orange", label="走路脚步 0.36")
axs[2].set_title("开始/停止约 4 秒淡入淡出；室内低通 650 Hz（闷）"); axs[2].set_xlabel("秒"); axs[2].legend(fontsize=9)
fig.suptitle("反馈 2：雨声", fontsize=14); fig.tight_layout()
fig.savefig("docs/screenshots/feedback-1123/2_rain.png", dpi=90)
print("ok")
