# 项目状态记录

## 地图面积变更（2026-11-23）

- **变更**：可游玩区域由约 600 m × 600 m 缩小为约 **360 m × 360 m**（长宽各缩小 40%，面积约为原来的 36%，约 0.13 km²）。说明书 FR-G01 原写"约 1 km²"，实际面积以此为准。
- **原因**：用户试玩反馈 4（docs/screenshots/feedback-1123/）：原地图空跑路程长，要求更紧凑、更密集。
- **做法**：
  - 所有地标按参考地图 `docs/reference/world_map.webp` 的像素坐标乘比例换算（`WorldLayout.K = Size / 1024`），一处改 `Size` 全部跟着缩，相对位置不变。
  - 地形大尺度起伏的噪声频率按比例放大，避免地貌被"挤小"后变形。边缘山和西北高地降低、放缓，否则同样高度落在更短的距离上会变成光秃秃的陡岩坡。
  - 远景山环向内收 28%。
  - 树、模型、角色的尺寸不变。树、灌木按每平方米密度摆放；草甸树丛的间距由 34 m 收紧到 28 m，路边造景由每 26–36 m 一处收紧到 20–28 m。
  - 同步更新的内容：
    - 宠物出没点（也是参考地图坐标）
    - 小路、NavMesh、远景
    - 截图机位（特写机位按米保持原距离）
    - 冒烟测试路线（按宠物出没点自动走）
- **存档**：存档版本升为 2，旧地图的存档（版本 1）读取时直接作废，开新游戏。
- **验证**：冒烟测试 21/21 通过。俯瞰对比图在 `docs/screenshots/feedback-1123/4_map_shrink.jpg`。

## 技术部分需求状态总表（2026-11-23）

状态只有三种：已验证（写明设备）/ 已实现未验证 / 仅设计。另有"未达成"表示做了但结果不合格。

| 编号 | 内容 | 状态 | 证据 |
|---|---|---|---|
| FR-UP-01 | `IUpscaler` 统一接口 | 已验证（M5 Max） | `Packages/com.zfw.upscaling`，EditMode 7/7，冒烟"超分：3 个后端" |
| FR-UP-02 | 输入准备 IN-01~08 | 见下表 | 接入规范第 4 节 |
| FR-UP-03 | DLSS / FSR 2/3 原生后端 | 仅设计 | `docs/设计方案/FSR2-3与DLSS-MetalFX后端.md` |
| FR-UP-04 | 选择器与档位映射 | 已验证（M5 Max）；Windows 未验证 | EditMode 测试；日志 `[Upscaler] 选择` |
| FR-UP-05 | GpuCaps schema 2（dlss/fsr/metalfx） | 已验证（M5 Max） | MineEmbers `v0.3.0`，`docs/caps/` |
| FR-UP-06 | 设置菜单与 F3 调试层 | 已验证（M5 Max） | 冒烟截图 `7x_*`、`7y_*` |
| C3 | 游戏层不引用厂商 SDK | 已验证 | EditMode 测试 |
| IN-01 | 渲染分辨率与不重复抗锯齿 | 已验证（M5 Max） | 冒烟核对 URP 渲染尺寸 |
| IN-02 | 深度 / Reversed-Z | 已验证（M5 Max） | F3 面板 |
| IN-03 | 运动向量（蒙皮、风动） | 已验证（M5 Max） | 冒烟截图 `7y_view_motion_vectors`；TC-05 |
| IN-04 | Halton(2,3) 抖动 | 部分：公式已测，内置 STP 用自己的序列 | `UpscalerMath`、EditMode |
| IN-05 | 曝光 | 不适用（固定曝光） | 接入规范 |
| IN-06 | Reactive Mask | 已实现，当前后端不消费 | 冒烟（14 个来源，>30 帧） |
| IN-07 | UI 在放大之后 | 已验证（M5 Max） | 超分截图文字清晰 |
| IN-08 | 切镜头 / 改分辨率清历史 | 已验证（M5 Max） | 冒烟；SC-05 切后第 20 帧无残影 |
| FR-VB-01~03 | 场景、参考图、画质指标 | 已验证（M5 Max） | `docs/超分评估报告-mac-20261123.md` |
| FR-VB-04 | PerfRunner 超分参数与 upscale_ms | 已实现；发布版 upscale_ms 为空 | MineEmbers `v0.3.1` |
| FR-VB-05 | 一键报告 | 已验证（M5 Max） | `tools/bench.sh` |
| NFR-01 | 质量档帧率 +40% | **未达成**（STP +34%，FSR 1 +33%；渲染缩放 +42%） | 超分评估报告 |
| NFR-05 | 超分 Pass 耗时 | 未测（发布版无数据） | — |
| TC-02 | 连续切换 50 次 | 已验证（M5 Max） | 冒烟 |
| TC-03 | 改窗口分辨率 | 已验证（M5 Max） | 冒烟 |
| TC-05 | 关运动向量看重影 | 已验证（M5 Max）：FLIP +26% | 超分评估报告 |
| TC-06 | 关 Reactive Mask 看拖影 | 未做（当前后端不读遮罩） | — |
| TC-10 | 挂机（压缩为 10 分钟） | 已验证（M5 Max）：10 分钟不崩溃，118.0 → 117.9 FPS，内存 171 → 171 MB | `bench_results/soak-mac-20261123/` |
| FR-RD-01~04 | GRD、遮挡剔除、着色器收敛 | **未达成**：Mac 上打开后 GRD 绘制的物体都不显示（最小场景也复现），默认关闭 | `docs/GRD评估报告.md` |
| FR-RD-05 | 不走 GRD 的物体清单 | 已完成 | `docs/GRD评估报告.md` |
| FR-RD-06 / NFR-03 | GRD 开关对比、Draw Call −50% | 脚本已实现，无可信数据 | `tools/grd.sh` |
| FR-EV-01 | 光追阴影 | 仅设计 | `docs/设计方案/光追-VRS-CrashDiag.md` |
| FR-EV-02 | VRS | 仅设计 | 同上 |
| FR-CD-01~07 | 崩溃诊断 | 仅设计 | 同上 |
| — | Windows（5060 Ti / 1070 Ti） | 测试包已打好，未运行 | `~/dev/WildPartner-win-builds/`（不进 Git），脚本 `tools/win/` |
