# 《夜裔 · 血月临城》工程交接文档（2026-11-11 交接版）

> 这份是**给接手人的现状说明书**，不是开发日记。
> 逐条实施过程、每次验证的原始数据、踩过的坑，都在 `HANDOVER_DEVLOG.md`（开发日志归档，可查细节）。

**工程路径**：`D:\unity\Nightborn` 　**Unity**：2022.3.34f1c1 　**当前 HEAD**：`41e6973` 前后

---

## 0. 一分钟交接

**是什么**：Unity 3D 哥特吸血鬼潜行解谜。玩家是 Ada，**身体自带一圈 3 m 柔光**（走哪照哪），靠熄灯 / 遮光 / 压光在阴影里穿行，吸夜巡者的血续命，被看见就会被围猎。

**现在能玩到什么**：

- **11 个场景** = 主菜单 + 街区 ×3 + 教堂 ×3 + 宅邸 ×3 + 主教座堂（终章）
- 完整闭环：菜单选关 → 检查点 → 熄灯/推箱/压光/吸血/血视/化形 → 被抓或烧死回检查点 → 拾遗物 → 出口或 Boss 仪式 → 存档 → 回菜单
- **4 种敌人 + 1 个 Boss**：巡逻守卫、执灯修士（灯吹不灭）、夜巡犬（气味追踪）、钟楼看守（重甲吸不动）、主教（三段仪式）

**三个最大的坑（接手先看）**：

1. **美术资产为 0** —— 全部是灰盒体块，只有 5 个纯色材质。这是"能不能上架"的唯一硬门槛。
2. **手感参数没经过真人验证** —— 守卫转身/检测衰减/推箱力度/药剂时长都是我用数值和脚本推的，真人一玩大概率要重调。
3. **包里没有"目标提示"和"教程文字"** —— 玩家进关卡不知道要去哪，设计要求的"关键处一行提示"没实现。

---

## 1. 环境与启动

| 项 | 值 |
| --- | --- |
| Unity | **2022.3.34f1c1**（必须同版本） |
| 路径 | `D:\Unity\2022.3.34f1c1` |
| 管线 | URP 14.0.6，Forward+，HDR，MSAA 4× |
| 输入 | Input System 1.7.0，Active Input Handling = **Both** |
| 寻路 | AI Navigation **1.1.5**（2026-11-11 新增） |
| 色彩 | Linear；目标 Windows 1080p60 |

**打开顺序**：Unity Hub 打开工程 → 等编译 → 菜单 `Nightborn → Build Base Scene` 重新生成全部场景（可选）→ Play（默认进 `L_Main` 主菜单）。

**MCP 桥接（重要）**：本机 MCP for Unity 默认走 **HTTP**（Unity 自己在 8080 起服务），而 IDE 侧的 stdio MCP 客户端只认 Unity 的 **6400** 端口。已把 EditorPrefs `MCPForUnity.UseHttpTransport` 置 0 切回 stdio。若哪天 MCP 连不上，先查这条注册表项（`HKCU\Software\Unity Technologies\Unity Editor 5.x`，名字带哈希后缀）。

---

## 2. 内容清单

### 2.1 场景（`Assets/Nightborn/Scenes/`）

| 场景 | 关卡 id / 序 | 敌人 | 特点 |
| --- | --- | --- | --- |
| `L_Main` | — | — | 主菜单 + 选关（线性解锁） |
| `L_Ch01_S01_Street` | `Ch01_S01` / 0 | 守卫 | 教学关：躲光、熄灯、出口 |
| `L_Ch01_S02_Street` | `Ch01_S02` / 1 | 守卫 + 修士 | 引入"吹不灭的灯" |
| `L_Ch01_S03_Street` | `Ch01_S03` / 2 | 守卫 + 修士 | 掩体 5→3、街灯挪到必经之路 (0,0,8) |
| `L_Ch02_S01_Church` | `Ch02_S01` / 3 | 猎犬 | 40×40 室内柱廊，2 盏可吹灭烛台 |
| `L_Ch02_S02_Church` | `Ch02_S02` / 4 | 猎犬 ×2 | 第二只巡侧廊 |
| `L_Ch02_S03_Church` | `Ch02_S03` / 5 | 猎犬 ×2 + 修士 | 烛台只剩 1 盏 |
| `L_Ch03_S01_Mansion` | `Ch03_S01` / 6 | 看守（二层） | 两层：地面 + 回廊 + 23.5° 坡道 |
| `L_Ch03_S02_Mansion` | `Ch03_S02` / 7 | 看守 + 守卫 | 上下同时施压 |
| `L_Ch03_S03_Mansion` | `Ch03_S03` / 8 | 看守 + 守卫 + 猎犬 + 修士 | 四种同场 |
| `L_Ch04_S01_Cathedral` | `Ch04_S01` / 9 | 主教 Boss | 三段仪式，**无出口门** |

### 2.2 每关固定内容

- 2 个检查点（触发式，踏进去记录）
- 1 处血之遗物（`relic_ch0x_s0y`）
- 出口（除终章）
- 玩家 + 相机 + 全套组件由 `CreatePlayer()` 生成

---

## 3. 系统清单

> **验证等级**：A = 真机 Play 验证过且有数据；B = 脚本/API 验证；C = 未验证。

| 系统 | 文件 | 关键参数 | 等级 |
| --- | --- | --- | --- |
| 自体光 | `Player/SelfGlow.cs` | 3.5 m / 0.9 强度 / Soft 512 / 呼吸 5% | A |
| 光照采样 | `Player/LightExposureProbe.cs` | 环境 0.5×、月 0.12×、点光 `I×(1−d/range)²`；遮挡反打射线；排除自体光与灯具自身 | A |
| 检测度 | `Player/DetectionMeter.cs` | = 所有敌人最高确认进度 ×100；60 可疑 / 100 围猎 | A |
| 守卫 AI | `Detection/GuardAI.cs` | 视锥 65–75°/11 m；光下 0.3 s、暗处 3 s 确认；lostGrace 内不掉进度；衰减 0.25/s；搜索 20 s | A |
| 执灯修士 | `Detection/LanternMonk.cs` | 灯 5 m/1.4，每 8 s 巡照 2.2 s → 8.5 m/2.4；吸血后灯灭 | A |
| 夜巡犬 | `Detection/Bloodhound.cs` | 视锥 35°/7 m；10 m 内**移动**时 +0.55/s 气味进度；追击 4.6 | B（真实动线未跑） |
| 钟楼看守 | `Detection/Warden.cs` | 90°/14 m、巡逻 1.0/追击 1.6、搜索 60 s、**不可吸血** | B |
| 主教 Boss | `Boss/BossRitual.cs` + `RitualGate.cs` | 3 圣灯 → 2 道门 → 压光（≤1.25 m）靠近祭坛 3 m | B（API 路径验证，未真人跑图） |
| 化形 | `Player/ShiftForm.cs` | Tab；影身灭光 + 穿 Veil 层，6 s / 冷却 8 s；强光弹回硬直 3 s | A |
| 血视 | `Player/BloodSight.cs` | Q；3 s / 冷却 25 s；屏幕标记敌人与遗物 | A |
| 吸血 | `Player/PlayerFeeder.cs` + `Level/FeedTarget.cs` | 左键；仅背后/侧面；1.4 s 锁定（不能动）；+30 血；目标昼睡 60 s | A |
| 血储 | `Player/BloodSystem.cs` | 100；灼伤 −6/s；吸血 +30；血瓶 +25；归零 → 检查点 | A |
| 压光药剂 | `Player/DimPotion.cs` | 数字键 1；30 s；3.5 m → 1 m；**终章给 2 瓶** | A |
| 推箱 | `Interaction/Pushable.cs` | Rigidbody 冻结 Y/旋转、drag 4；`OnControllerColliderHit` 推动 | B（力度未调） |
| 交互 | `Interaction/PlayerInteractor.cs` | E；从胸口发射线、方向跟相机，2.6 m | A |
| 熄灯 | `Level/DouseableLamp.cs` | E；关灯 + 熄灯球 | A |
| 检查点/出口/遗物 | `Level/Checkpoint.cs` · `ExitGoal.cs` · `Relic.cs` | 触发体 3×2×3；出口触发通关 | A |
| 关卡流程 | `Level/LevelFlow.cs` | 检查点、被抓回滚、血储归零回滚、通关、Esc 回菜单 | A |
| 存档 | `Level/SaveSystem.cs` | `persistentDataPath/nightborn_save.json`；完成度/最好成绩/检查点/遗物 | A |
| 正式 HUD | `UI/NightbornHud.cs` | 边缘红晕 + 心跳、敌人头顶标记、三格能力、遗物计数、通关提示 | A |
| 音频 | `Audio/NightbornAudio.cs` + `SynthAudio.cs` | 4 条音源；**全部运行时合成**（脚步/吹灯/心跳/警觉/被抓/通关/drone） | B（听感未评） |
| 主菜单 | `Menu/NightbornMenu.cs` | 纯代码建 UI，10 关线性解锁 | A |
| 玩家移动 | `Player/PlayerMotor.cs` | 3.2/1.3 m/s；移速倍率是**带 key 的最小值栈**（burn/stagger/freeze/feed） | A |
| 相机 | `Camera/ThirdPersonCamera.cs` | 2–6 m；`SphereCastNonAlloc` 跳过玩家自身；撞墙按命中距离夹取 | A |
| 场景生成 | `Editor/NightbornBootstrap.cs` | 一键盘 11 个场景（**破坏性**） | A |
| 编辑器守卫 | `Editor/NightbornEditorGuard.cs` | Play 默认进菜单、空场景自动开菜单 | A |
| 自检浮层 | `Debug/NightbornDebugHud.cs` | F1 打开（默认收起） | A |
| 流程自检 | `Debug/WalkthroughTest.cs` | 进关卡挂上即跑 10 项 | A |

---

## 4. 已知问题（按严重度）

### P0 —— 会直接毁体验 / 卡关

| # | 问题 | 现状 / 建议 |
| --- | --- | --- |
| 1 | **没有目标提示**：玩家不知道要往哪走，也没有"出口在北"之类的引导 | 未做。建议：进入关卡时 HUD 顶部一行目标 + 出口方向箭头（或遗物指引） |
| 2 | **没有教程文字**：设计要求的"关键处一行提示"未实现（§12 决策 20） | 未做。建议：触发式 `TutorialHint` 组件 + 一行 HUD 文案 |
| 3 | **终章药剂曾可能不足导致卡关** | 已修：终章给 2 瓶（`BossRitual` 第三段必须压光）。仍建议：检查点补一瓶 |
| 4 | **存档无法在游戏内清除** | 只能代码 `SaveSystem.Delete()`。接手人带着旧存档会看到"部分关卡已解锁"的菜单 |

### P1 —— 影响成品质感

| # | 问题 | 说明 |
| --- | --- | --- |
| 5 | **美术 0 资产** | 全灰盒 + 5 色材质。唯一的上架硬门槛 |
| 6 | **手感全未真人验证** | `turnSpeed 90`、`decay 0.25`、推箱 drag 4、药剂 30 s、Boss 追 1.2 —— 都是我按数值推的 |
| 7 | **夜巡犬气味只验到算法** | `AddDetection` 管线与位移速度算法验过，**真实动线没跑**（脚本环境产生不了持续输入） |
| 8 | **Boss 仪式只走 API** | 三段状态机逐级验过，但"真人在座堂里跑图"没做过；主教 100°/16 m 视野可能过强 |
| 9 | **音频是合成占位** | 听感简陋，且没有混音分组/音量设置 |
| 10 | **没有暂停 / 设置 / 键位重绑 / 音量** | 按 Esc 只是释放鼠标 |
| 11 | **没有叙事文本** | 设计说"每关一句独白 + 结局"，一个字都没写 |

### P2 —— 结构与工具链

| # | 问题 | 说明 |
| --- | --- | --- |
| 12 | HUD 红晕偏重 | 26% 血时画面较糊（系数 `(1−血)×0.75`），建议降到 0.5 并加下限 |
| 13 | 守卫转身仍可能"视野断续" | 已加 lostGrace 缓解，但仍建议真人观察 |
| 14 | NavMesh 不随推箱更新 | 箱子推走后敌人可能穿过去；建议给箱子挂 `NavMeshObstacle`（carve） |
| 15 | 影身穿幕依赖 `Veil` 层 | 美术挪动物体若忘改层，穿幕会失效（且无提示） |
| 16 | 多场景共用 `CreatePlayer()` 但**未 prefab 化** | 每次生成都新建，调整玩家要改代码；§12 决策 4 的 prefab 复用没做 |
| 17 | 菜单只有关卡列表 | 没有"新游戏/继续/设置" |
| 18 | 中文本地化未抽表 | 文案硬编码在代码里 |

### P3 —— 发布相关

| # | 问题 |
| --- | --- |
| 19 | 打包配置未做（icon / 版本号 / IL2CPP 验证 / 构建脚本） |
| 20 | 未做打包后性能压测（编辑器里 60–200 fps，不代表成品） |
| 21 | 无 Steam 集成 / 成就 / 云存档 |
| 22 | 无商店页素材（截图、宣传片、文案） |

---

## 5. 怎么验证（接手第一件会做的事）

### 5.1 自动化流程自检（最快）

Play → 进任意关卡 → 挂 `WalkthroughTest` 到任意物体：

```csharp
var go = new GameObject("TEST_Walkthrough");
go.AddComponent<Nightborn.DebugTools.WalkthroughTest>();
```

约 40 秒后 Console 输出 10 条 ✅/❌（场景接线、检查点、吹灯、推箱、吸血、血视、化形穿幕、被抓回滚、遗物、通关）。**最近一次：10 / 10**（在 `L_Ch01_S02_Street`）。

> 注意：脚本环境产生不了持续移动输入，所以"推箱位移"只有 0.03–0.04 m（最小推力），"气味追踪"无法端到端验证。这两项必须真人验。

### 5.2 MCP 快速操作（推荐）

```
set_active_instance("Nightborn@5beff319")
execute_code(code)        // 直接跑 C#，可传送到任意位置、读任意组件
manage_camera(screenshot) // 截图到 Assets/Screenshots
```

**注意**：`Build Base Scene` **不能在 Play 模式调用**（会抛 `This can only be used during play mode`，且场景不会重建 —— 菜单项却返回"已尝试"）。

### 5.3 重新生成全部场景

菜单 `Nightborn → Build Base Scene`，或命令行：

```powershell
& "D:\Unity\2022.3.34f1c1\Editor\Unity.exe" `
  -projectPath "D:\unity\Nightborn" -batchmode -quit `
  -executeMethod Nightborn.EditorTools.NightbornBootstrap.BuildAll `
  -logFile "D:\unity\Nightborn\_rebuild.log"
```

---

## 6. 代码地图

```
Assets/Nightborn/Scripts/
├─ Runtime/
│  ├─ Player/      PlayerMotor, SelfGlow, LightExposureProbe, DetectionMeter,
│  │               ShiftForm, BloodSight, PlayerFeeder, BloodSystem, DimPotion
│  ├─ Detection/   GuardAI, LanternMonk, Bloodhound, Warden
│  ├─ Boss/        BossRitual, RitualGate
│  ├─ Level/       LevelFlow, SaveSystem, Checkpoint, ExitGoal, Relic,
│  │               DouseableLamp, FeedTarget, NavMeshRuntimeBake
│  ├─ Interaction/ IInteractable, PlayerInteractor, Pushable
│  ├─ Camera/      ThirdPersonCamera
│  ├─ Audio/       NightbornAudio, SynthAudio
│  ├─ UI/          NightbornHud
│  ├─ Menu/        NightbornMenu
│  └─ Debug/       NightbornDebugHud, NightbornRuntimeBoot, WalkthroughTest
└─ Editor/         NightbornBootstrap（场景生成器）, NightbornEditorGuard
```

规模：约 **5,280 行** C#（运行时 3,955 + 编辑器 1,325）。

---

## 7. 构建与发布待办

- [ ] Player Settings：产品名/图标/版本号/公司名
- [ ] 切 IL2CPP + 验证托管剥离后无反射依赖（`SaveSystem` 用 `JsonUtility`，安全；`WalkthroughTest` 用了 `GetField` 反射，**发布前应排除**）
- [ ] 打包 Windows 64 位，实测 1080p60 帧率与包体（目标 ≤ 300 MB）
- [ ] 关掉编辑器专属物体：`WalkthroughTest` 依赖手动挂载，无场景引用，安全
- [ ] Steam 商店页素材

---

## 8. 工作方式与踩坑（务必先读）

### 8.1 先找现成方案，别自己造

不熟悉的功能先检索（GitHub / Asset Store / 官方示例），或**用 REA 逆向同类游戏**（本机 `rea-agents` MCP，能反编译原生二进制 / APK → Java / .NET / JS-Electron）。只有在找不到、许可证不兼容、或耦合过深时才自己写，并在注释里写明原因。每一笔外部来源记进 `THIRD_PARTY.md`。

### 8.2 工具链陷阱（都踩过）

| 陷阱 | 表现 | 解法 |
| --- | --- | --- |
| Unity 开着时外部写 `.cs` | `ReplaceFileW EIO (Win32 32)` | 用 `[IO.File]::Open(..., Truncate, Write, ReadWrite)`；或先关 Unity |
| 用 PowerShell 写中文源码 | 注释变乱码、行被合并（`鈥?` / `锛`） | **中文源码一律用文件写入工具**，不要字符串替换；替换后 grep `锛|鐨|姝` 自检 |
| 字符串替换锚点 | 文件混用 LF/CRLF，多行锚点匹配不到 | 用**单行锚点**；找不到就 `throw`，别静默失败 |
| `Build Base Scene` | Play 模式下调用会抛异常但菜单项返回"已尝试" | 调用前必须先 `stop` |
| 场景重建 | `BuildAll` 会删除并重建设置/材质/全部场景 | 手工改过场景后再跑 = 全丢，改动请落进 `NightbornBootstrap` |
| MCP 端口 | Unity 走 HTTP(8080)、客户端只认 6400 | 见 §1 的 EditorPrefs |

### 8.3 设计约定

- 五色板：`#0E0F12` / `#8B1E28` / `#E0A44B` / `#4A4F57` / `#7A8A8F`
- 命名前缀：`L_` 场景、`M_` 材质、`P_` 预制体、`VP_` Volume、`GEO_` 几何、`BLD_` 建筑、`LAMP_` 灯、`PROP_` 道具、`PLAYER_`、`NPC_`、`BOSS_`、`LEVEL_`
- 层：`Player`=8、`Veil`=9、`Guard`=10

---

## 9. 设计决策速查（25 条，细节见 DEVLOG §12）

| 类别 | 结论 |
| --- | --- |
| 规模 | 9 关（3 章 × 3 关）+ 终章 Boss，单关 4–6 分钟，Windows/Steam，1080p60 |
| 难度 | 单一难度，不做开关 |
| 叙事 | 轻叙事：每关一句独白 + 结局（**未写**） |
| 美术 | 先用免费资产包并统一五色（**未接**） |
| 音频 | 免费音效 + drone（当前为合成占位） |
| 玩家 | 掠食者、不战斗；吸血让人昼睡 |
| 光源 | **自体光 3.5 m，不可调**（原提灯方案已废弃） |
| 光操纵 | 熄灯 / 推箱遮光 / 压光药剂 |
| 血储 | 纯生命值（夜耗已删） |
| 血视 | 冷却型 3 s / 25 s，不耗血 |
| 化形 | 影身 = 灭光 + 穿薄幕，6 s，强光弹回 |
| 感知 | 视线定"看得见"，光照定"看得多快" |
| 围猎 | 扑最后已知位置，搜索后归位 |
| 敌人 | 守卫 / 执灯修士 / 夜巡犬 / 钟楼看守 + 主教 Boss |
| 关卡目标 | 抵达出口 + 可选血之遗物（终章为仪式） |
| 失败 | 检查点 + 短追逐，回滚血量与检测 |
| 存档 | 检查点自动 + 选关（线性解锁） |
| 手感 | 见 §3 各系统参数（**待真人调**） |

---

## 10. 下一步优先级（建议）

**第一批（让游戏"能给人玩"）**
1. 目标提示 + 教程一行文字（P0-1/2）
2. 存档清除入口（菜单加"新游戏"）（P0-4）
3. 接美术资产并统一五色（P1-5）

**第二批（让手感成立）**
4. 真人试玩 → 调 5 个参数：守卫转身/衰减、推箱力度、药剂时长、Boss 视野、HUD 红晕系数
5. 跑通夜巡犬与 Boss 的真实动线（P1-7/8）
6. 音频换正式素材 + 音量/暂停设置（P1-9/10）

**第三批（能发布）**
7. 打包配置与性能压测（P3）
8. 叙事文本 + 商店素材（P1-11 / P3-21）

---

## 附录 A：提交历史（最近）

```
41e6973  feat(boss): 主教座堂 + 三段潜行仪式，九关+终章齐
41ff8e4  feat(m3): 第三章宅邸（两层）+ 钟楼看守，九关齐
0fcf8ea  feat(m3): 每章拆成 3 关（街区/教堂各 3 关变体）+ 线性解锁修复
5d9ff7d  feat(m3): 教堂场景 + 第三种敌人夜巡犬；抽出多场景共用的 CreatePlayer
e2b695a  feat(m2): 音频系统（运行时合成占位）+ 事件接线
1bdd4bb  feat(m2): 正式 HUD + 感知手感调参
72bf809  test: 自动化流程自检（10 项全绿）+ 能力公开入口
54ebdbb  feat(m2): 第二种敌人「执灯修士」+ 死亡接入关卡回滚
64345c6  feat(m2): 守卫接 NavMesh 寻路
075a396  feat(m1): 推箱/压光药剂/选关菜单，M1 收尾
cb8a4c1  feat(m1): 吸血与血视落地
170faea  feat(m0): 按新设计完成 M0 重构
```

## 附录 B：外部来源

见 `THIRD_PARTY.md`：AI Navigation（官方包）、参考实现（Stealth Game 视线锥、Save System for Unity MIT、FS Stealth 行为参考）、以及 REA 的使用边界。

## 附录 C：存档格式

`%USERPROFILE%\AppData\LocalLow\NightbornStudio\Nightborn\nightborn_save.json`

```json
{
  "version": 1,
  "lastLevelId": "Ch01_S01",
  "levels": [
    { "id": "Ch01_S01", "index": 0, "completed": true, "bestTime": 12.1,
      "checkpoint": 1, "relicIds": ["relic_ch01_s01"], "relicTotal": 1 }
  ]
}
```

解锁规则：`index==0` 常开，其余要求**上一关 completed**。

---

**交接人**：__________ 　**接手人**：__________ 　**日期**：__________
