# 荒原伙伴（WildPartner）——给 AI 的项目说明

先读 `docs/最终指令.md`（工作规则、时间安排、文件优先级），再读 `docs/PROGRESS.md` 末尾和 `docs/玩法设计.md`。

## Obsidian 进度卡片（必须维护）
- 位置：`/Users/guangzhi/Documents/Obsidian Vault/图形平台工程师/03 荒原伙伴/进度/`
  - `当前状态.md`：每次整篇覆盖。
  - `进度日志/YYYY-MM-DD-HHmm-<标题>.md`：每次新建一张短卡片。
  - 图片放 `进度/附件/`，文件名以 `荒原伙伴-` 开头，用 `![[文件名]]` 嵌入。
- 规则：每完成一项任务或一个阶段、每次提交推送后，以及会话结束前，必须更新 Obsidian 进度卡片（当前状态.md + 新建一张进度日志），路径见本文件。
- 卡片内容必须与实际一致：不夸大，未验证的写未验证。卡片里不能出现 Tripo 密钥等任何凭据。
- 时间一律写悉尼时间（`TZ=Australia/Sydney date`）。

## 常用命令
- 重建世界：`tools/unity.sh WildPartner.EditorTools.WorldBuilder.Build`
- 打 Mac 包：`tools/unity.sh WildPartner.EditorTools.BuildScript.BuildMac`
- 冒烟测试（提交前必须通过）：`tools/smoke.sh`，结果在 `Logs/smoke/smoke_result.txt`
- 展示录屏（白天近景 GIF）：`tools/showcase.sh <目标目录>`；头发裙摆录屏：`tools/motion_demo.sh`
- 长任务前后用 `caffeinate -dimsu -t <秒> & ...; kill $!` 防止休眠。

## 密钥
Tripo 密钥只在 `~/.config/tripo/api_key`（权限 600），只用 `TRIPO_API_KEY="$(tr -d ' \n\r\t' < ~/.config/tripo/api_key)"` 读取；绝不能写进项目、Git、日志、回复或 Obsidian 卡片。
