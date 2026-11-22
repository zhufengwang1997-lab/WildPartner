# 技术决策记录

每条：决定 → 理由。按时间追加。

## 2026-11-22 阶段 0
- **Unity 版本用 6000.3.28f1（Unity 6.3 LTS）**：本机已安装，用户确认不再装 6000.0.x。
- **TierSettings 扩展机制用 `[SerializeReference] List<TierExtension>`**，而不是让游戏继承 TierSettings：多个游戏/模块可以各挂自己的扩展，且已有资产不需要换脚本类型。MineEmbers 的 heatHaze / lakeReflection 移到 `MineTierExtension`。结果：MineEmbers EditMode 21/21（原 20 + 新增 1）、Python 13/13 通过，打标签 `v0.2.0`。
- **三个包用 Git URL + `#v0.2.0` 固定版本**，manifest 中三个都显式写出。MineEmbers 目前是**私有仓库**：本机通过 `gh auth setup-git` 让 Unity 的 git 能拉取。若面试官要从零克隆运行，需要把 MineEmbers 也设为公开（公开前向用户确认，见 PRD 7.3）。
- **项目用 URP 3D 模板（Forward+）创建**，配置写在 `Assets/_Game/Editor/ProjectSetup.cs`（配置即代码，可重复执行）。Windows 只开 D3D12（PRD 要求 DX12），Mac 只开 Metal。脚本后端 Mono（Mac 上交叉编译 Windows 包只能用 Mono）。
- **MSAA 全档位关闭**：抗锯齿交给阶段 2 的超分/时间性方案（IN-01），避免重复抗锯齿。

## 2026-11-22 美术与模型
- **用户已有的 Tripo 模型（女巫、月影兔、月影蝶、白色神鹿）都没有骨骼和动画** → 采用 PRD 4.2 第 6 条退路：程序化动画（弹跳、摆动、扇翅）。
- **GLB 不直接进 Unity**：Unity 不原生支持 GLB，引入 glTFast 会多一个依赖。改为 `tools/blender/process_glb.py` 在 Blender 无界面模式下批处理：合并网格 → 减面 → 统一尺寸 → 原点放到底部中心 → 导出 FBX + 基础色贴图 PNG。可重复执行。
- **木屋室内家具（门、窗、桌、凳、床、壁炉、壁灯）原始面数约 190 万/个、56MB/个**，减到 0.8–1.5 万面。原始 GLB（共约 400MB）不进仓库，只放在用户桌面 `素材/`；四个角色/生物的原始 GLB（共约 14MB）放进 `Assets/_Game/Models/_Inbox/` 走 LFS。
- **缺失的模型（树、灌木、岩石、木屋外观、石阵、瞭望塔、木桥、两种生物等）由 Blender 脚本程序化生成**，而不是请用户在 Tripo 补做：用户要求“能自己做的全部自己做”，且程序化生成可重复、风格统一、面数可控。
- **卡通光照用一个自写 HLSL Shader（`WildPartner/Toon`）**：色阶化明暗 + 蓝紫阴影色 + 边缘光 + 可选风动。所有不透明物体共用它（FR-RD-04），参数全部放在 `UnityPerMaterial`，兼容 SRP Batcher 与 GPU Resident Drawer；带 DOTS instancing。
- **风动运动向量**：MotionVectors Pass 用上一帧的时间（`_WP_WindTimePrev`）和上一帧的物体矩阵各算一次风动位移，二者之差就是真实屏幕运动（FR-G07 / IN-03）。

## 2026-11-22 世界与流程
- **整个场景由 `WorldBuilder.Build` 一次生成**（地形高度、4 层地表、水面、地标、植被摆放表、灯光、后处理、相机、系统对象、NavMesh），固定种子 20261122，可重复。场景文件只是生成结果。
- **植被是摆放表（位置/朝向/缩放）+ 运行时实例化 Prefab**（MeshRenderer + LOD Group），不用 Terrain 自带树草（PRD 4.3，便于阶段 4 GRD）。共约 3.2 万个实例。
- **NavMesh 用物理碰撞体烘焙**：烘焙前临时生成植被，让树干、岩石成为障碍；烘焙后清除。
- **Blender 导出的 FBX 单网格节点带 90° X 旋转**，摆放时设置根节点旋转会把它覆盖（桥侧翻、角色趴下）。在导入规则里把节点旋转烘焙进顶点并清零，所有模型统一处理。
- **地形贴图 UV 用 Terrain 自带的 uv × `_SplatN_ST`**（Unity 传入的是"整块地形重复次数"，不是世界坐标缩放）。
- **草叶法线统一朝上**（`_UpNormal`），避免竖直面片在侧光下发黑，与地面颜色衔接。
- **运行时冒烟测试 `-smoketest`**：在打包好的游戏里通过与 UI/按键相同的入口自动跑完整循环，输出截图和逐步 PASS/FAIL；只读测试钩子 `GameInput.SimCapture/SimSneak` 仅在该参数下使用。
