# 素材来源与许可

| 素材 | 来源 | 许可 |
|---|---|---|
| 女巫、月影兔、月影蝶、白色神鹿、木屋室内家具（GLB） | 用户本人使用 Tripo 生成 | 用户自有 |
| 参考图（docs/reference/） | 用户提供 | 仅作参考，不进游戏 |
| 其余模型、贴图、Shader | 本项目用 Blender 脚本 / 代码程序化生成 | 本项目自有 |
| 字体 | 运行时调用系统字体（macOS 苹方 / Windows 微软雅黑），不随包分发 | 系统自带 |

## 音效（CC0，2026-11-23 加入）

| 游戏里的用途 | 原素材 | 作者 / 发布者 | 链接 | 许可 |
|---|---|---|---|---|
| 草地、土路脚步（`Resources/Audio/step_grass_*`、`step_dirt_*`） | Fantozzi's Footsteps — Sand L1–L3 / R1–R3 | Fantozzi 录制，qubodup 发布 | https://opengameart.org/content/fantozzis-footsteps-grasssand-stone | CC0 |
| 木地板脚步（`step_wood_*`） | 同上 — Stone L1–L3 / R1（低通、降调） | 同上 | 同上 | CC0 |
| 浅水脚步（`step_water_*`） | 40 CC0 water / splash / slime SFX — splash_02/04/06/09 | rubberduck | https://opengameart.org/content/40-cc0-water-splash-slime-sfx | CC0 |
| 雨声（`rain_loop`） | amb_rain_loop_1 | Kresiek The Furry | https://opengameart.org/content/amb-rain-loop-1 | CC0 |

处理方式见 `tools/audio/process_sfx.py`（滤波、变调、截短、淡出、雨声接缝交叉淡化）。雨中树叶滴答声、风声、捕捉音效、猫叫和背景音乐仍是运行时合成。
