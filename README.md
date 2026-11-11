# Nightborn《夜裔 · 血月临城》

Unity 3D 哥特吸血鬼潜行解谜。玩家 Ada 自带一圈 3.5 m 柔光（走哪照哪），靠熄灯 / 推箱遮光 / 压光药剂
在暗处穿行，吸夜巡者的血续命，被看见就会被围猎。

**接手先读 [`HANDOVER.md`](HANDOVER.md)** —— 给接手的 AI 写的现状说明书：怎么连、怎么跑、怎么验、有哪些坑。
本文件只留最少的入口信息。

## 环境

| 项 | 值 |
| --- | --- |
| Unity | **2022.3.34f1c1**（必须同版本，换版本会重序列化 URP 资产） |
| 工程路径 | `D:\unity\Nightborn` |
| 管线 / 输入 | URP（Forward+ / HDR / MSAA 4× / Linear）· Input System 1.7.0（Active Handling = Both） |
| 目标平台 | Windows / 1920×1080 / 60 fps |

## 跑起来

1. Unity Hub 打开工程，等编译完成；
2. 点 **Play** → 默认进主菜单 `L_Main`（这是产品行为，不是空场景），在菜单里选关；
3. 想直接调某一关：先打开该关卡场景，再点菜单 **`Nightborn → Play 当前场景`（F5）**；
4. 关卡内按 **F1** 来自检浮层；**Esc** 只释放鼠标。

> 玩家身上的光源是「自体光」（`Player/SelfGlow.cs`）。早期方案里的提灯 / `LanternFollow` **已废弃删除**，
> 看到旧文档提到提灯一律按过期处理。

## 仓库

- 远端：**https://github.com/koishi514-kami/Night-Room**（分支 `master`）
- GitHub 仓库名是 **Night-Room**，工程内部项目名是 **Nightborn** —— 同一个东西，只是建仓库时名字不同。
- 接手后先 `git pull`；改完 `git push origin master`。首次 push 需要 GitHub 凭据（PAT 或 `gh auth login`）。

## 文档

| 文件 | 内容 |
| --- | --- |
| `HANDOVER.md` | **现状 / 操作 / 坑 / 参数表 —— 接手必读** |
| `HANDOVER_DEVLOG.md` | 实施过程与原始验证记录（§0–§9 是 M0 前的旧交接，**已作废**） |
| `THIRD_PARTY.md` | 外部来源与许可（引入任何 GitHub 代码前先查 LICENSE 并抄进去） |
| `HANDOVER.docx` | 2026-11-06 旧 Word 版，**已过期，不要参考** |
