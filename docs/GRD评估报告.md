# GRD 评估报告（GPU Resident Drawer）

**结论：未达成。** 在 Mac（Apple M5 Max，Metal，发布版）上打开 GRD 后，所有交给 GRD 绘制的物体都不显示：树、灌木、岩石、木屋家具，连 URP 自带 Lit 材质的方块也不显示。所以 GRD 默认关闭，NFR-03（Draw Call 降低 ≥ 50%）没有可信的实测值。物体没画出来时 Draw Call 自然会少，这种数字不能当结果。

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
3. 诊断模式 `-grdbench -grddiag`：在镜头前放两个方块，一个用 URP 自带的 Lit 材质（已知支持 GRD），一个用 Toon。GRD 打开时**两个都看不见**，关掉后都正常（`screenshots/grd/diag_grd_on.png` / `diag_grd_off.png`）。
   - 第一次排查时，"能画出来"的那个方块其实用的是粉色错误着色器（URP Lit 当时没打进包），它根本不走 GRD，所以当时"问题在 Toon 着色器"的结论是错的，已更正。
4. 再加 `-grdminimal`：把整个游戏的物体全部关掉，只留一个新相机、一盏灯和两个方块，结果相同。**所以不是游戏里的物体、着色器或自定义渲染 Pass 造成的。**
5. 运行时监测 60 帧：GRD 实例没有被反复重建（0 次）。
6. 打包日志里没有着色器编译错误；玩家日志里没有 GRD 相关警告；四档管线资源都开了 GRD，BRG 变体全保留。
7. 已排除：GPU 遮挡剔除（关掉也一样）、超分和抗锯齿（原生分辨率、关抗锯齿也一样）。

## 下一步（未做）
- **Windows 测试包里加了 GRD 诊断**（`-grddiag -grdminimal`，开 / 关各一次）。24 日在 DX12 上看方块能不能画出来，就能判断是不是 Metal 发布版特有的问题。
- 在编辑器播放模式下试（编辑器里 GRD 有更多校验和日志）。
- 用 Xcode 的 Metal 帧捕获看 GRD 的绘制命令有没有发出。

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
