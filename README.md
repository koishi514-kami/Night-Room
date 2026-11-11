# Nightborn《夜裔 · 血月临城》

Unity 3D 哥特吸血鬼潜行解谜。玩家 Ada 自带一圈 3.5 m 柔光（走哪照哪），靠熄灯 / 推箱遮光 / 压光药剂
在暗处穿行，吸夜巡者的血续命，被看见就会被围猎。

**接手先读 [`HANDOVER.md`](HANDOVER.md)** —— 给接手的 AI 写的现状说明书：怎么连、怎么跑、怎么验、有哪些坑、
以及「先查现成方案 / 拆成多子 agent」的协作方式。本文件只留最少的入口信息。

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
4. 关卡内 **F1** = 调试浮层（含当前移速惩罚来源），**F2** = 跑一遍 12 项功能自检；
   **Esc** 只释放鼠标（再按一次回菜单）。

## 验收工具（2026-11-12 新增）

| 想验什么 | 怎么起 |
| --- | --- |
| 这一关能不能走通 | `execute_code: Nightborn.DebugTools.LevelAutoPilot.Launch(true);`（合成按键真的驱动玩家走到出口） |
| 各功能接对没有 | 关卡内按 **F2**（12 项，`⏭` 表示本关本来没有这个物件） |
| 数字口径 | `HANDOVER.md` §2.4；过程与原始数据见 `HANDOVER_DEVLOG.md` §25–§26 |

> 玩家身上的光源是「自体光」（`Player/SelfGlow.cs`）。早期方案里的提灯 / `LanternFollow` **已废弃删除**，
> 看到旧文档提到提灯一律按过期处理。

## 存档与设置

`%USERPROFILE%\AppData\LocalLow\NightbornStudio\Nightborn\`
- `nightborn_save.json`（+`.bak`）：通关/检查点/遗物，原子写；菜单「新的夜晚」会删它
- `nightborn_settings.json`（+`.bak`）：音量/灵敏度/画质/分辨率等，**清档不会删**

## 仓库

- 远端：**https://github.com/koishi514-kami/Night-Room**（分支 `master`）
- GitHub 仓库名是 **Night-Room**，工程内部项目名是 **Nightborn** —— 同一个东西，只是建仓库时名字不同。
- 接手后先 `git pull`；改完 `git push origin master`。凭据在 Windows 凭据管理器（`credential.helper=wincred`），
  换机器要重配（`gh auth login` 或 `git credential approve`）。

## 文档

| 文件 | 内容 |
| --- | --- |
| `HANDOVER.md` | **现状 / 操作 / 验收口径 / 参数表 / 协作方式 / 坑 —— 接手必读** |
| `HANDOVER_DEVLOG.md` | 实施过程与原始验证记录（§0–§9 是 M0 前的旧交接，**已作废**；§25–§26 是最新一轮） |
| `THIRD_PARTY.md` | 外部来源与许可（引入任何 GitHub 代码前先查 LICENSE 并抄进去） |
