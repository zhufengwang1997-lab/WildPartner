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
- [x] 整改 3 密度与构图：林地 6.5 m 网格高密度成林、树下灌木林下层、空地 3–5 棵成组、主路两侧每 26–36 m 手摆小景（树+灌木+石），路边花丛和树根蘑菇（已验证：`docs/screenshots/art/3_before_after.jpg`）
- [x] 整改 4 光照与后期：16–17 点暖低阳光、蓝紫阴影、阴影 4096+4 级级联、SSAO（不透明物后）、对比/饱和/分离色调/轻泛光/暗角、自写高度雾（远处淡蓝、朝阳偏暖）（已验证：`docs/screenshots/art/4_before_after.jpg`）
- [x] 整改 5 天空与远山：天空着色器噪声云（受光面/背光面、软边、随风飘）；金字塔雪山换成 4 层环形山脊剪影（`FarRidge.shader`），逐层变淡变蓝、远两层有积雪；分离色调减弱，天顶更蓝（已验证：`docs/screenshots/art/5_before_after.jpg`）
- [ ] 1–5 验收四景（出门第一眼/草地小路/林中/湖边）：`docs/screenshots/art/acceptance_1-5.jpg`。自评：草地小路接近目标；出门镜头太俯、湖水有粉色斑块、秋色树块面感重，尚未达到"宣传图"标准，靠整改 6（镜头）和 7（水）继续补
- [x] 整改 6 镜头与呈现：镜头半径 5.4 m、俯角 7°、视野 52°，主角约占画面高度 1/3 且偏左；碰撞改为平滑前拉（半径 0.35、平滑 0.4 s）；主角和宠物加天光色边缘光（阴影面也有）；界面换成圆角深紫玻璃 HUD + 羊皮纸卡片、细描边、星形/日月图标、文字阴影；主菜单镜头移回 600 m 地图中心并加底板（已验证：`docs/screenshots/art/6_before_after.jpg`、冒烟 19/19）
- [ ] 整改 7–8：水、猫动作
- 已知问题：
  - Tripo"白桦/秋色树"长得像针叶树，清单要求重新生成，但 Tripo API 余额为 0，需用户在网站上重做；暂以阔叶树为主替代
  - 云量和对比仍偏淡（时间盒到，后续有空再调）
  - 木屋悬崖正面岩石堆仍像一面"石墙"

## 下一步
- 阶段 2：com.zfw.upscaling 超分中间件（IUpscaler、RenderScale/FSR1/STP/MetalFX/DLSS、F3 调试面板）
