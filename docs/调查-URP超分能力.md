# 调查：本机 URP 的超分能力（2026-11-23）

读的是本机包缓存里的源码：URP `com.unity.render-pipelines.universal@029be7ffa662`、SRP Core `@3424017a4ec3`（Unity 6000.3.28f1 自带的 URP 17）。

## 1. 内置哪些放大方式，Metal / DX12 能不能用
管线资源的 `upscalingFilter`（`UpscalingFilterSelection`）：自动、双线性、点采样、**FSR**、**STP**（另有几种只用于调试的）。
- 双线性 / 点采样：最后一次 Blit，所有平台都能用。
- FSR：URP 内置的是 **FSR 1**（空间放大 EASU + 锐化 RCAS，像素着色器），不是 FSR 2/3。没有平台限制，Metal 和 DX12 都能用。可以用 `fsrOverrideSharpness` / `fsrSharpness` 调锐化。
- STP（Spatial-Temporal Post-processing，Unity 自研的时间性放大）：计算着色器，需要支持计算着色器的平台，Metal 和 DX12 都行（`STP.IsSupported()`）。100% 渲染比例时也会运行，相当于更好的 TAA。
  - **坑**：没有任何一份管线资源选 STP 时，打包会把 STP 的计算着色器剥离（`STPResourceStripper`），运行时再切 STP 就每帧空引用、黑屏。所以四档 URP 资源都存成 STP，运行时由代码改回所需模式。
- DLSS：URP 17 里**没有**内置 DLSS（运行时代码里搜不到 NVIDIA 模块的引用；HDRP 才有）。

## 2. 是否已有同名的 `IUpscaler`
有。SRP Core 里有 `UnityEngine.Rendering.IUpscaler`（`Runtime/Upscaling/IUpscaler.cs`），URP 里有对应的 `ImageUpscalingFilter.IUpscaler` 分支，可以让第三方超分接进 URP 的渲染图。但整套代码都包在 `#if ENABLE_UPSCALER_FRAMEWORK` 里，默认关闭，属于实验接口。
- 处理：我们的接口放在 `Zfw.Upscaling` 命名空间，名字相同不冲突。
- 本期三个后端（RenderScale、FSR1、STP）都用 URP 内置实现，由后端配置管线资源，不需要这个实验框架。
- 以后接 FSR 2/3、DLSS 这类原生插件时，再考虑打开 `ENABLE_UPSCALER_FRAMEWORK`，把我们的后端包成 `AbstractUpscaler`（设计见接入规范）。

## 3. 自定义 Pass 放在哪
URP 内置的放大发生在后处理的最后一步（`PostProcessPassRenderGraph` 里的 FinalPost / STP），在 UI 之前。
- STP 在后处理之前运行（取代 TAA 的位置），之后的后处理按显示分辨率执行。
- FSR1 和双线性在后处理之后、UI 之前。这正是说明书 2.1 的方案 B。
- 所以三个内置后端的 `RecordPass` 不自己声明 Pass，由 URP 记录；接口保留这个方法，留给以后的原生后端。这是对 FR-UP-01 的最小改动，记在 DECISIONS。

## 4. 抖动和运动向量
- 运动向量：相机开启 TAA 或 STP 时，URP 自动跑 Motion Vector Pass。我们的 Toon、草、蒙皮网格都写运动向量（阶段 1 已做）。
- 抖动：URP 在 TAA / STP 下自己给投影矩阵加抖动（`TemporalAA.CalculateJitter`，STP 用 `STP.Jit16`）。
  - 后端的 `GetJitter` 返回同一个值，供调试层显示和测试。
  - IN-04 要求的 Halton(2,3) 序列和相位数公式写在 `Zfw.Upscaling.Halton` 里并有测试，但内置 STP 用的是它自己的序列，这一点写进接入规范。
- Mip 偏移：URP 已经按渲染比例设置全局 `_GlobalMipBias = log2(渲染宽 / 显示宽)`，并与 TAA 设置里的 `mipBias` 取较小值。后端把 `taaSettings.mipBias` 设成 `GetMipBias()`（公式再减 1），就满足 FR-UP-01。
