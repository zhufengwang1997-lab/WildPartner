# 进度

## 当前状态（2026-11-22）
- [x] 阶段 0：MineEmbers 重构并打 `v0.2.0`；WildPartner 用 Git URL 引用三个包；GpuCaps 报告 `docs/caps/caps_AppleM5Max_Metal.json`（source=native）
- [ ] 阶段 1：进行中
  - [x] 模型：4 个用户角色模型 + 7 个木屋道具处理入库；35 个程序化模型（树/灌木/岩石/草花/木屋/地标/林鸮/锦鲤）
  - [x] Shader：Toon / ToonTerrain / Water / Sky
  - [x] 玩法代码：输入、存档、流程、玩家、程序化动画、5 种生物、捕捉、UI、程序化音频、昼夜、天气
  - [x] 世界生成 `WorldBuilder`，编辑器多视角截图自检（`SceneShots`）
  - [ ] Mac 包 + 运行时冒烟测试（`-smoketest`）
  - [ ] 提交、打 `stage-1`

## 下一步
- 冒烟测试通过后提交并打标签，进入阶段 2（超分中间件）
