# 设计：时间性原生超分后端（FSR 2/3、DLSS、MetalFX）

状态：**仅设计**。代码里只有选择器中的名字位置，没有实现。

## 目标
在 `Zfw.Upscaling` 里加三个后端，游戏层不改代码：
- `FsrUpscaler`：FSR 2/3。
- `DlssUpscaler`：Windows + RTX。
- `MetalFxUpscaler`：macOS。

## 怎么接
1. 打开 URP 的 `ENABLE_UPSCALER_FRAMEWORK`。每个后端同时实现我们的 `IUpscaler` 和 SRP 的 `AbstractUpscaler`，由 URP 在正确位置（取代 STP/TAA 的地方）调用 `RecordRenderGraph`。
2. 输入从 `UpscalerInputs` 取：
   - 颜色、深度、运动向量（URP 的 MotionVector 纹理；FSR 要的是像素单位、"当前到上一帧"方向，需要乘 −渲染尺寸）
   - Reactive Mask（已在 `_WP_ReactiveMask` 生成）
   - 抖动（换成 Halton(2,3)，相位数 8×(显示/渲染)²，`UpscalerMath` 已实现并测试）
   - 重置标志
3. 原生调用走 Native Plugin + `CommandBuffer.IssuePluginEventAndData`，在渲染线程执行。

## 各后端要点
| 后端 | SDK / 入口 | 关键行为 | 风险 |
|---|---|---|---|
| FSR 2/3 | FidelityFX SDK（DX12 / Vulkan 后端），自己编 Native Plugin | 渲染分辨率由 SDK 的 `GetRenderResolutionFromQualityMode` 给出；Reactive Mask 和透明合成遮罩都用上 | Metal 上没有官方后端，只能 DX12 / Vulkan |
| DLSS | `UnityEngine.NVIDIA.GraphicsDevice` 创建 DLSS Feature | `GetOptimalSettings` 查渲染分辨率；分辨率变化时销毁重建 Feature；在 Command Buffer 里执行；失败时退回下一个后端 | URP 17 没有内置，只能用 NVIDIA 模块；代码只能在 Windows 上第一次运行 |
| MetalFX | Objective-C++ 插件，`MTLFXTemporalScaler` | 先查 `MTLFXTemporalScalerDescriptor.supportsDevice`；需要 macOS 13+ | Unity 的 Metal 纹理要用 `GetNativeTexturePtr` 拿出来，跨线程同步 |

## 选择与回退
顺序不变：DLSS → MetalFX → STP → FSR → RenderScale。
- 每个后端的 `IsSupported` 先看 GpuCaps 的 schema 2 字段（`dlss` / `fsr` / `metalfx`，已实现），再查自己的 SDK。
- 初始化失败就记日志，并自动退回下一个，不能崩溃。

## 验证计划
- UpscaleBench 加配置后直接复用（`tools/bench.sh`）。
- DLSS 在 5060 Ti 上测，FSR 2 在 1070 Ti 上测，MetalFX 在 M5 Max 上测。
