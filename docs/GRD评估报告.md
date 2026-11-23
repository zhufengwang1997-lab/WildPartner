# GRD 评估报告（GPU Resident Drawer）

**结论：未达成。** 在 Mac（Apple M5 Max，Metal）上打开 GRD 后，所有用 `WildPartner/Toon` 着色器的物体都不绘制，包括树、灌木、岩石和木屋内的家具。所以 GRD 默认关闭，NFR-03（Draw Call 降低 ≥ 50%）没有可信的实测值。物体没画出来时 Draw Call 自然会少，这种数字不能当结果。

## 做了什么（FR-RD-01 ~ 04）
| 项 | 状态 |
|---|---|
| URP 渲染路径 Forward+ | 已是（`PC_Renderer` 渲染模式 2） |
| GRD = Instanced Drawing | 四档 URP 资源都存成开启，运行时按设置关掉（防止打包时被剥离，同 STP 的教训） |
| BatchRendererGroup Variants = Keep All | 已设（GraphicsSettings `m_BrgStripping: 1`） |
| Toon 着色器兼容 SRP Batcher 和 DOTS Instancing | 材质参数都在 `UnityPerMaterial`；各 Pass 引入 `DOTS.hlsl`；`_BaseColor` 声明为 DOTS 实例属性 |
| GPU 遮挡剔除开关 | 已接（`-occlusion=`、设置菜单、F3） |
| 开关 | `-grd=on|off`、设置菜单"GPU 驻留绘制 / GPU 遮挡剔除"、F3 面板按钮，F3 上实时显示 Draw Calls / Batches / SetPass |
| 度量脚本（FR-RD-06） | `tools/grd.sh`：四个机位，GRD 关、开各启动一次游戏，采 Draw Calls、Batches、SetPass、主线程、渲染线程、帧时间和加载时间，生成本报告的数据表（`tools/grd_report.py`） |

## 问题定位（已做的排查）
1. 冒烟截图：GRD 打开时草地机位的树和灌木消失，关掉后恢复（`screenshots/grd/smoke_grd_on.png` / `smoke_grd_off.png`）。木屋室内整屋家具消失。
2. 关掉 GPU 遮挡剔除（`-occlusion=off`）后仍然消失，所以不是遮挡剔除的问题。
3. 诊断模式 `-grdbench -grddiag`：在镜头前放三个方块。用默认材质的方块在 GRD 下能画出来（显示成粉色，因为 URP Lit 没打进包）；用 Toon 着色器的方块在 GRD 下看不见，关掉 GRD 正常（`screenshots/grd/diag_grd_on.png` / `diag_grd_off.png`）。**结论：GRD 本身在 Metal 上能工作，问题在 Toon 着色器的 DOTS Instancing 变体。**
4. 打包日志里 Toon 没有编译错误；玩家日志里没有 GRD 相关警告。

## 下一步（未做）
- 用 Xcode 的 Metal 帧捕获看 Toon 的 DOTS 变体：顶点位置是否正确，是否真的发出了绘制。
- 怀疑点：`_BaseColor` 在 `UnityPerMaterial` 里是 `half4`，DOTS 属性声明成 `float4`；顶点着色器里的风动用到 `TransformWorldToObject`（需要逆矩阵）。可以先去掉 DOTS 属性块再试。
- 在 Windows（DX12）上试一次，确认是不是 Metal 特有的问题。

## 不走 GRD 的物体（FR-RD-05）
| 类别 | 原因 | 处理方式 |
|---|---|---|
| 生物和女巫（蒙皮网格） | GRD 不支持 SkinnedMeshRenderer | 照常走 SRP Batcher；数量少（十几只） |
| 草地 | 几何草叶由 `GrassRenderer` 用 `Graphics.RenderMeshPrimitives` 直接实例化绘制 | 本来就是 GPU 实例化绘制，不需要 GRD |
| 粒子（雨、瀑布水雾、星尘、萤火虫） | 粒子系统不走 GRD | 照常绘制；透明粒子登记进 Reactive Mask |
| UI | 不经过相机 | 不受影响 |
| 用 MaterialPropertyBlock 的物体：月影蝶翅膀发光、月光花、环境小动物（白天的蝴蝶、鸭子等，带颜色的） | GRD 遇到 MaterialPropertyBlock 会退回普通绘制 | 数量少，保留；如要走 GRD，改成材质参数或实例数据 |
| 水面、瀑布、天空、远山 | 自定义着色器没有 DOTS 变体 | 每样只有一两个物体，不需要 |
| 地形 | 运行时生成的网格，`ToonTerrain` 着色器没有 DOTS 变体 | 分块数量少，不处理 |
