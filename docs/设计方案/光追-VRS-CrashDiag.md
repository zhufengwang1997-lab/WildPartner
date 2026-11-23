# 设计：硬件光追、VRS、CrashDiag

状态：三项都**仅设计**，没有代码。

## 硬件光追（DX12 / Metal）
- **用在哪**：URP 没有内置光追，所以只考虑单点效果。湖面反射可以把现在的实时反射探针（`WaterProbe`）换成光追反射；其次是木屋室内的接触阴影。
- **怎么做**：
  - 用 `RayTracingAccelerationStructure` 收集湖周围的静态网格，在 Render Graph 里加一个 `RayTracingShader` Pass，只对湖面像素发射线，结果写进反射纹理供 Water 着色器读取。
  - 不支持光追时（GpuCaps `raytracing` 为 unsupported）退回反射探针。1070 Ti 预期属于这种情况（没有 RT 核心，未实测）。
- **开销预估**：反射探针在特效开销清单里低于测量噪声，所以光追的收益是画质，不是性能。只在"极高"档开。
- **风险**：URP 的光追接口仍属实验性；Metal 光追需要 Apple Silicon。

## VRS（可变着色率）
- **用在哪**：草地和远景。现在草地实测 0.7–1.4 ms（`docs/特效开销清单.md`），是第二大开销。
- **怎么做**：
  - Unity 6 的 `ShadingRateInfo` / `ShadingRateImage`：在 Render Graph 里按亮度变化和运动生成着色率图。
  - 草地和天空用 2×2，人物和界面保持 1×1。
  - GpuCaps 的 `vrs` 字段决定是否开启。M5 Max 的能力报告是 supported（Metal 的 RasterizationRateMap，`docs/caps/caps_AppleM5Max_Metal.json`），但 Unity 的着色率图在 Metal 上能否用未验证；5060 Ti 预期支持、1070 Ti 预期不支持，都未测。
- **验证**：特效开销清单加一行"VRS 开/关"，UpscaleBench 看 FLIP 是否明显变差。

## CrashDiag（GPU 崩溃诊断）
- **目标**：Windows 上 GPU 挂掉（设备移除、TDR）时，留下能定位的信息。
- **怎么做**：
  - DX12 打开 DRED（自动面包屑 + 页错误），崩溃后从 `ID3D12DeviceRemovedExtendedData` 读出最后执行的命令列表和 Pass 名。
  - NVIDIA 卡可选 Nsight Aftermath。
  - 写到 `Logs/crash_<时间>.json`，下次启动时提示并收集。
  - Native Plugin 实现，Release 版默认关闭（说明书 NFR-07：Release 版不能有开销）。
- **风险**：DRED 本身有约 2–5% 的开销，只在开发版和测试包里开。
