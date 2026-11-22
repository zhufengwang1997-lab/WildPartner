# 进度

## 当前状态（2026-11-22）
- [x] 阶段 0：MineEmbers 重构并打 `v0.2.0`；WildPartner 用 Git URL 引用三个包；GpuCaps 报告 `docs/caps/caps_AppleM5Max_Metal.json`（source=native）
- [x] 阶段 1：完成（标签 `stage-1`）
  - [x] 模型：4 个用户角色模型 + 7 个木屋道具处理入库；35 个程序化模型（树/灌木/岩石/草花/木屋/地标/林鸮/锦鲤）
  - [x] Shader：Toon / ToonTerrain / Water / Sky
  - [x] 玩法代码：输入、存档、流程、玩家、程序化动画、5 种生物、捕捉、UI、程序化音频、昼夜、天气
  - [x] 世界生成 `WorldBuilder`，编辑器多视角截图自检（`SceneShots`）
  - [x] Mac 包 + 运行时冒烟测试 `tools/smoke.sh`：18/18 通过（已验证，结果见 `docs/screenshots/stage1/smoke_result.txt`）
  - [x] 已知小问题：河流汇入池塘处有一块方形水面接缝；角色无骨骼，用程序化动画

## 下一步
- 阶段 2：com.zfw.upscaling 超分中间件（IUpscaler、RenderScale/FSR1/STP/MetalFX/DLSS、F3 调试面板）
