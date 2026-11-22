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

## 画面与动作阶段（2026-11-22 晚起，依据《最终指令》21:00 修订版和《画面整改清单》）
- [x] 女巫骨骼与动作（rig_humanoid.py）：待机/走/跑/跳（起跳、下落、落地），发丝裙摆二级摆动，步幅与移速匹配（已验证：动作拼图、冒烟测试）
- [x] 猫替换林鸮，开局即为伙伴（已验证：冒烟测试）
- [x] 用户 Tripo 模型 23 个 + 锦鲤入库：焊接后减面、2–3 级 LOD、贴图、树木风摆权重；替换全部脚本占位体并删除（已验证：`docs/screenshots/tripo_models.png`、场景截图）
- [x] 整改 1 地面：GPU 密集矮草（`GrassRenderer` + `Grass.shader`，按 8 m 区块提交、随风摆、主角推开草、带运动矢量）、手绘地面色块图（树荫、阳光斑、林地偏蓝）、不规则土路边、花丛与路边碎石（已验证：`docs/screenshots/art/1_before_after.jpg`、Mac 包冒烟测试）
- [x] 整改 2 地形：地图按 PRD-补充 缩到 600 m；谷地起伏、河岸下切；去掉岩石纹理横条（"阶梯"）和低空云块（"浮空石头"）；边缘山坡覆盖针叶林；木屋移到岩石悬崖平台，院子道具成组摆放（已验证：`docs/screenshots/art/2_before_after.jpg`）
- [ ] 整改 3–8：大树与林下层、光照后处理、天空云、镜头与界面、水、猫动作
- 已知问题：Tripo"白桦/秋色树"长得像针叶树，清单要求重新生成，但 Tripo API 余额为 0，需用户在网站上重做（见汇报）

## 下一步
- 阶段 2：com.zfw.upscaling 超分中间件（IUpscaler、RenderScale/FSR1/STP/MetalFX/DLSS、F3 调试面板）
