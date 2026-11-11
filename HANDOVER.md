# Nightborn 交接文档 · 给接手的 AI

> **这份文件是给下一个 AI 看的现状说明书**，不是给玩家看的。
> 目标：让你在 30 分钟内知道「这是什么、现在什么状态、怎么跑、怎么验、哪里会炸」，然后就能开工。
> 逐条实施过程与原始验证数据在 [`HANDOVER_DEVLOG.md`](HANDOVER_DEVLOG.md)（**§25 / §26 是最新一轮**，§0–§9 是 M0 之前的旧交接、**已作废**，别当现状读）。

---

## 0. 先读这三条硬规矩（违反过，代价是砸环境 / 浪费半天）

1. **`Nightborn → Build Base Scene` 必须在「非 Play」状态下执行。**
   它会**先删掉** URP / Renderer / Volume 资产，再重建 11 个场景。在 Play 模式下点，`EditorSceneManager.NewScene` 会抛
   `InvalidOperationException: This cannot be used during play mode`，渲染管线半残 + `NullReferenceException` 刷屏 +
   **编辑器卡死（Responding=False，MCP 实例消失）**。真要救：`Stop-Process -Name Unity -Force` → 重开工程 → 重跑一次 Build 即自愈（2026-11-12 真踩过）。
2. **场景内容不要手改 `.unity`。** 11 个场景全部由代码生成，任何几何/文案改动都要写进
   `Editor/NightbornBootstrap.cs`（体块、敌人、检查点、出口）或 `Editor/NightbornObjectiveSetup.cs`（目标文案 + 教程提示），
   否则下一次 Build 会被整份覆盖。
3. **只认真机结论。** 编辑器截图 + `execute_code` 读运行时状态才算验证过；「代码看起来对」不算。
   `HANDOVER.docx`（2026-11-06 的 Word 版）**已删除**，别去找；旧 DEVLOG 的 §0–§9 已作废。

---

## 1. 一分钟看完现状

| 项 | 值 |
| --- | --- |
| 是什么 | Unity 3D 哥特吸血鬼潜行解谜。玩家 Ada 自带 3.5 m 柔光（走哪照哪），靠熄灯 / 推箱遮光 / 压光药剂在暗处穿行，吸血续命，被看见就被围猎 |
| 工程 | `D:\unity\Nightborn`　Unity **2022.3.34f1c1**（必须同版本，换版本会重序列化 URP 资产） |
| 管线 / 输入 | URP（manifest 写 14.0.6、**实际解析 14.0.11**）、Forward+、HDR、MSAA 4×、Linear；Input System 1.7.0，Active Input Handling = **Both** |
| 规模 | 11 个场景（菜单 + 9 关 + 终章）· **44 个 C# 脚本 / ≈11,770 行**（运行时 40 个 **9,877** 行；编辑器 4 个 **1,896** 行，按 LF 字节计） |
| 版本库 | 远端 **https://github.com/koishi514-kami/Night-Room**（仓库名 `Night-Room` ≠ 工程名 Nightborn，**同一个项目**）。本地 `master` 跟踪 `origin/master`，凭据在 Windows 凭据管理器（`git:https://github.com`，账号 `koishi514-kami`，`credential.helper=wincred`） |
| HEAD | `git log -1`。**2026-11-12 这两轮的改动还没提交**（见 §10 交接尾巴） |
| 完成度 | 完整闭环可玩：菜单选关 → 检查点 → 光/血/化形 → 被抓回滚 → 遗物 → 出口或 Boss 仪式 → 存档 → 回菜单。**10 个关卡全部真机跑通**（§2.4 有数字），功能自检 0 失败 |
| 最大短板 | **美术 0 资产**（全灰盒 + 5 色材质，上架唯一硬门槛）；**叙事文本 0 字**；**键位重绑没做**（缺 `.inputactions`） |

**最近两轮（2026-11-12）做了什么**（细节见 DEVLOG §25 / §26）：

- 五个子系统并行审计 + 全 10 关可达性实测，修掉一批 P0：宅邸上不了二层、出生走廊被柜子堵死、街区中路被封死、遗物 ID 撞车、终章第二/三段不可能完成、敌人穿墙/永不回巡逻、影身状态跨场景泄漏、存档非原子写、通关后血归零永久冻结……
- 新增 **`LevelAutoPilot`**（合成按键真的驱动玩家走完整路线）与 **重写 `WalkthroughTest`**（12 项按类型自适配）；
- 新增 **设置菜单**（8 项设置 + 独立 JSON 持久化）；夜巡犬气味加**遮挡判定**、半径 10 → 8 m；
- 「人物突然变慢走不动」查清并修掉：软惩罚不再被当硬锁（见 §4 移速模型）。

---

## 2. 连上 / 跑 / 验

### 2.1 MCP（工程内已装 `com.coplaydev.unity-mcp@10.3.0`，stdio 6400）

```
read_mcp_resource unity "mcpforunity://instances"      # 取实例名 Nightborn@<hash>（hash 可能变）
set_active_instance "Nightborn@<hash>"                 # 多实例时必须先钉；单实例也建议钉一次
manage_editor play | stop                              # 动场景 / 重建前一律先 stop
manage_scene load path=Assets/Nightborn/Scenes/L_Ch01_S01_Street.unity   # 参数名是 path
execute_code code="..."                                # 跑任意 C#：反射、传送玩家、读组件
manage_camera screenshot capture_source=game_view include_image=true     # 截图落 Assets/Screenshots
execute_menu_item menu_path="Nightborn/Play 当前场景"
refresh_unity compile=request mode=force wait_for_ready=true             # 改完 .cs 必须刷新编译
read_console types=["error"] count=20                                    # 编译/运行时错误
```

接不上时的排查顺序：① **`MCPForUnity.UseHttpTransport` 默认是 true**（包内 `EditorPrefs.GetBool(UseHttpTransport, true)`），会去起 HTTP 8080，而客户端只认 **6400**（`PortManager.cs`）——在 MCP 窗口或 `HKCU\Software\Unity Technologies\Unity Editor 5.x\<hash>` 里把它置 **0** 再重连；② 确认工程只有一个 Unity 实例（多开会互相抢 6400）；③ 编辑器被杀之后实例会短暂消失，**等 20–30 s** 再 `read_mcp_resource`。

### 2.2 进关卡的**唯一两条路**（直接点 Play 永远进菜单）

守卫把 `playModeStartScene` 钉死在 `L_Main`，所以：

- 菜单 `Nightborn → Play 当前场景`（**F5**）：先打开关卡场景，再点它（内部用 `SessionState` 记一次性请求）；
  > `[MenuItem]` 里的原始字符串是 `"Nightborn/Play 当前场景 _F5"`（`_F5` 是快捷键后缀）。`execute_menu_item menu_path="Nightborn/Play 当前场景"` 实测可用；万一哪次不认，就带上 ` _F5` 再试。
- 运行时 `SceneManager.LoadScene("L_Ch01_S01_Street")`。

### 2.3 ⭐ 验收三件套（2026-11-12 新增，这是最重要的一节）

| 工具 | 怎么起 | 看什么 |
| --- | --- | --- |
| **整关巡察** `LevelAutoPilot` | `execute_code: Nightborn.DebugTools.LevelAutoPilot.Launch(true);`（`true` = 幽灵模式只测几何；`false` = 带敌人） | Console 里逐段 `[PILOT] ✅ 0 出生走廊 (1.4s) (0.0,0.1,-13.9)`；末尾一行 `[PILOT] ===== <场景> ghost=True 总判定：✅ 通关 =====` + 用时 / 卡住次数 / 被抓次数 / 血最低。**它用每帧 `InputSystem.QueueStateEvent` 合成按键真的驱动 `PlayerMotor`**，不是瞬移 |
| **功能自检** `WalkthroughTest` | 按 **F2**，或 `execute_code: new GameObject("TEST_Walkthrough").AddComponent<Nightborn.DebugTools.WalkthroughTest>();` | `[TEST] ✅/❌/⏭` 共 12 项，末尾 `[TEST] ===== 流程自检结束：N 通过 / M 失败 / K 跳过（共 T 项）=====` |
| **调试浮层** | 按 **F1** | 玩家位置/速度、外光与灼伤、**当前移速惩罚来源**（`burn=0.45 → ×0.45`）、血储/血瓶/血视/化形、检测度、遗物/检查点、存档写盘耗时、NavMesh 烘焙耗时 |

### 2.4 期望读数（2026-11-12 实测，回归时照这个对）

**整关巡察（ghost=true）** —— 10 关全通，卡点 0：

| 场景 | 用时 | 场景 | 用时 |
| --- | --- | --- | --- |
| Ch01_S01 / S02 / S03 街区 | 18.8 / 18.5 / 20.6 s | Ch02_S01 / S02 / S03 教堂 | 15.7 / 18.1 / 16.0 s |
| Ch03_S01 / S02 / S03 宅邸 | 18.4 / 18.5 / 18.4 s | Ch04_S01 终章（三圣灯+两门+压光上台） | **42.9 s**，末尾 `Boss 阶段 Done 灯 3 门 2 距祭坛 2.14` |

**功能自检（12 项）** —— **跳过 ≠ 失败**（`K` 只是「本关本来就没有这个物件」）：

| 关卡类型 | 期望 |
| --- | --- |
| 街区（有货箱、可吸血） | **12 / 0 / 0** |
| 教堂（无货箱） | **11 / 0 / 1**（⏭ 推箱） |
| 宅邸（无货箱 + 看守不可吸血） | **10 / 0 / 2**（⏭ 推箱、⏭ 吸血） |
| 终章（无货箱/遗物/出口） | **9 / 0 / 3** |

> 旧文档写的「S02 = 10/10、S01 = 8/10」是**改写前**的口径，已作废。

### 2.5 重建全部场景（破坏性，Play 下千万别点）

菜单 `Nightborn → Build Base Scene`（**先 `stop`**），或命令行：

```powershell
& "D:\Unity\2022.3.34f1c1\Editor\Unity.exe" `
  -projectPath "D:\unity\Nightborn" -batchmode -quit `
  -executeMethod Nightborn.EditorTools.NightbornBootstrap.BuildAll `
  -logFile "D:\unity\Nightborn\Logs\rebuild.log"
```

末尾自动调 `NightbornObjectiveSetup.DecorateAll()`（目标点 + 每关 3 条教程提示，幂等）。
最近一次实测：**10 个关卡场景、30 个提示触发体**，Console 打 `目标 / 教程装饰完成：10 个关卡场景，新增提示触发体 30 个`。

### 2.6 存档与设置文件在哪（改文件前先 `stop`）

| 文件 | 路径 | 说明 |
| --- | --- | --- |
| 存档 | `%USERPROFILE%\AppData\LocalLow\NightbornStudio\Nightborn\nightborn_save.json`（+`.bak`） | 单 JSON，**原子写**（`.tmp → File.Replace → .bak`），坏档自动回退 `.bak`。`SaveSystem` 把数据缓存在内存，任何 `Save()` 都整份写回 → **改存档前先退出 Play，且先备份** |
| 设置 | 同目录 `nightborn_settings.json`（+`.bak`） | 主音量/环境/音效/灵敏度/反转Y/画质/全屏/分辨率。**清档（菜单「新的夜晚」）不会删它** |

### 2.7 推送

```powershell
git -C D:\unity\Nightborn pull
git -C D:\unity\Nightborn push
```
**不要把 PAT 写进 remote URL 或 `.git/config`**（会跟着仓库被看到）。

---

## 3. 内容地图

### 3.1 场景（`Assets/Nightborn/Scenes/`）

| 场景 | id / 序 | 敌人 | 备注 |
| --- | --- | --- | --- |
| `L_Main` | — | — | 菜单：继续 / 新的夜晚 / **设置** / 10 关两列（按章节顺序，行优先） |
| `L_Ch01_S01_Street` | `Ch01_S01` / 0 | 守卫 | 教学关 |
| `L_Ch01_S02_Street` | `Ch01_S02` / 1 | 守卫 + 修士 | 引入「吹不灭的灯」 |
| `L_Ch01_S03_Street` | `Ch01_S03` / 2 | 守卫 + 修士 | 掩体 5→3，街灯在必经路 (0,0,8) |
| `L_Ch02_S01_Church` | `Ch02_S01` / 3 | 猎犬 | 40×40 柱廊，2 盏可吹烛台 |
| `L_Ch02_S02_Church` | `Ch02_S02` / 4 | 猎犬 ×2 | 第二只巡侧廊 |
| `L_Ch02_S03_Church` | `Ch02_S03` / 5 | 猎犬 ×2 + 修士 | 烛台只剩 1 盏 |
| `L_Ch03_S01_Mansion` | `Ch03_S01` / 6 | 看守（二层） | 两层 + 南北向坡道（坡脚 z≈−13.1、坡顶 z≈−0.70） |
| `L_Ch03_S02_Mansion` | `Ch03_S02` / 7 | 看守 + 守卫 | 上下同时施压 |
| `L_Ch03_S03_Mansion` | `Ch03_S03` / 8 | 四敌同场 | |
| `L_Ch04_S01_Cathedral` | `Ch04_S01` / 9 | 主教 Boss | 三段仪式，**无出口门** |

每关固定：**2 个检查点**（0 恒在 `(0,0,−16)`；1 各关不同：街区 `(−8.5,0,13.5)`、教堂 `(−4.5,0,7.5)`、宅邸 `(−4.5,5.35,10.5)`、终章 `(0,0,−6)`）、
**1 处血之遗物 + 1 个出口 —— 但终章两者都没有**（`L_Ch04_S01` 只有 3 盏圣灯 + 2 道仪式门 + 祭坛，靠三段仪式结关）、
出生点 `(0,0.2,−17)`；玩家 + 相机 + 全套组件由 `NightbornBootstrap.CreatePlayer()` 生成，**未 prefab 化**（工程里 0 个 prefab）。

**门 / 薄幕（Tab 化形穿过的 Veil 层）是硬门槛**：街区北门、教堂两座拱门、宅邸塔门、终章两道侧堂门都靠薄幕封住，
所以每关的教程提示第 3 条就是讲「按 Tab」。改几何时别把这条路绕通了。

### 3.2 代码地图（`Assets/Nightborn/Scripts/`，44 个文件）

```
Runtime/  (40 文件)
  Player/      PlayerMotor SelfGlow LightExposureProbe DetectionMeter ShiftForm
               BloodSight PlayerFeeder BloodSystem DimPotion
  Detection/   GuardAI LanternMonk Bloodhound Warden          ← GuardAI 是四类敌人的共用状态机
  Boss/        BossRitual RitualGate
  Level/       LevelFlow SaveSystem Checkpoint ExitGoal Relic DouseableLamp
               FeedTarget NavMeshRuntimeBake ObjectiveTarget TutorialHint
  Interaction/ IInteractable PlayerInteractor Pushable
  Camera/      ThirdPersonCamera
  Audio/       NightbornAudio SynthAudio                       ← 全部运行时合成，无 Mixer
  UI/          NightbornHud ObjectiveHud SettingsPanel
  Menu/        NightbornMenu
  Settings/    NightbornSettings                                ← 2026-11-12 新增
  Debug/       NightbornDebugHud NightbornRuntimeBoot WalkthroughTest LevelAutoPilot
Editor/   (4 文件)
  NightbornBootstrap        场景生成器（11 场景 / 材质 / 设置，破坏性）
  NightbornEditorGuard      Play 默认进菜单；空场景自动开街区
  NightbornObjectiveSetup   目标点 + 教程提示装饰（幂等，BuildAll 末尾自动跑）
  NightbornScenePlay        「Play 当前场景」F5
```

---

## 4. 系统参数（改手感就看这张表）

> 等级：**A** = 真机 Play 验过；**B** = 只验了脚本/API 路径；**C** = 未验证。

| 系统 | 文件 | 关键参数 | 级 |
| --- | --- | --- | --- |
| 自体光 | `Player/SelfGlow` | 3.5 m / 0.9 强度 / Soft 512 / 呼吸 5%；半径**缩小瞬时、放大 2.5 m/s** | A |
| 光照采样 | `Player/LightExposureProbe` | 环境 0.5×、月 0.12×、点光 `I×(1−d/range)²`，多光源**取最大**（不是求和）；遮挡反打射线；排除自体光；Veil 算遮挡 | A |
| **移速惩罚** | `Player/PlayerMotor` | 3.2 / 1.3 m/s。倍率是**带 key 的最小值栈**：`burn` 0.45（光里，且 −6 血/秒）/ `stagger` 0.15（强光按 Tab，3 s）是**软惩罚**，有下限 `minSoftMultiplier 0.3`；`freeze`(被抓 0.8 s)/`feed`(吸血 1.4 s)/`dead`(血归零) 是**硬锁**（`IsHardLocked`）。另有 6 s 硬锁看门狗。**F1 浮层直接写出当前来源** | A |
| 检测度 | `Player/DetectionMeter` | 取所有敌人最高确认进度 ×100；60 可疑 / 100 围猎（60 线在 `GuardAI.suspiciousProgress` 落地为「停下盯住」） | A |
| 守卫 | `Detection/GuardAI` | 视锥 **65–70°**/11 m（75° 只是脚本默认，没有场景用到）；光下 0.3 s、暗处 3 s 确认；衰减 0.25/s；搜索 20 s（**搜索预算是无条件累加**，到点原地 ±60° 扫视）；**贴身 ≤ catchDistance×0.8 直接算看见**；追击转身 ×1.8（守卫 90→162 °/s）；复活后 2 s 盲期 | A |
| 执灯修士 | `Detection/LanternMonk` | 灯 5 m/1.4；每 8 s 巡照 2.2 s → 8.5 m/2.4；吸血后灯灭，醒来 0.6 s 渐亮 | A |
| 夜巡犬 | `Detection/Bloodhound` | 视锥 35°/7 m；**8 m 内移动 +0.55/s 气味，且必须无实体遮挡**（`RaycastNonAlloc`，0.2 s 节流；**薄幕不挡气味**）；`catchDistance 1.7`、追击 4.6 | A |
| 钟楼看守 | `Detection/Warden` | 90°/14 m；巡逻 1.0 / 追击 1.6；**转身 70 °/s**、`catchDistance 2.0`；搜索 60 s；**不可吸血** | B |
| 主教 Boss | `Boss/BossRitual` | 3 圣灯 → 2 道门（穿幕进侧堂）→ 压光（自体光 ≤1.25 m）贴近祭坛 3 m；`AltarDistance` 用**台面最近点**；阶段只前进不回退；**主教不可吸血** | A |
| 化形 | `Player/ShiftForm` | Tab；灭光 + 穿 `Veil` 层；6 s / 冷却 8 s；强光弹回硬直 3 s；退出影身时若与薄幕重叠会沿法线推出；`OnDisable` 还原全局碰撞 | A |
| 血视 | `Player/BloodSight` | Q；3 s / 冷却 25 s（从视野结束起算）；4 Hz 刷新标记 | A |
| 吸血 | `Player/PlayerFeeder` | 左键；仅背后/侧面；1.4 s 锁定不能动；+30 血；目标昼睡 60 s；被抓/归零/目标丢失都会打断并回滚 | A |
| 血储 | `Player/BloodSystem` | 上限 100；灼伤 −6/s；吸血 +30；血瓶 +25；归零→检查点（4 s 看门狗兜底）；**通关后不再灼伤** | A |
| 压光药剂 | `Player/DimPotion` | 数字键 1；30 s；3.5 m → 1 m；**终章 2 瓶** | A |
| 推箱 | `Interaction/Pushable` | Rigidbody 冻结 Y/旋转、drag 4、mass 30；`pushGain 1.0`；运行时挂 `NavMeshObstacle(carve)` 让敌人绕开 | A |
| 交互 | `Interaction/PlayerInteractor` | E；从胸口 `SphereCastNonAlloc`、方向跟相机、2.6 m；取最近的可交互物 | A |
| 相机 | `Camera/ThirdPersonCamera` | 2–6 m；灵敏度 **0.16 °/px**（设置可改 0.05–0.40）；pitch −45/60；`SphereCastNonAlloc` 跳过玩家自身；距离「拉近即时、退远平滑」 | A |
| 关卡流程 | `Level/LevelFlow` | 检查点（复活点**向下打射线贴地**）、被抓回滚（清场 + 1.2 s 保护）、血储归零回滚、通关、Esc 回菜单（`timeScale`/光标复位） | A |
| 存档 | `Level/SaveSystem` | 单 JSON + `.bak`；`version` 迁移；脏写抑制 | A |
| HUD | `UI/NightbornHud` | 血晕（0.5 系数 + 下限）、**冷却独立的冷灰「围猎」窄框**、**暖琥珀「灼伤」窄框**、敌人头顶标记（有遮挡判定）、三格能力、遗物计数、通关提示 | A |
| 目标/教程 | `UI/ObjectiveHud` + `Level/ObjectiveTarget` + `Level/TutorialHint` | 一行目标+距离；屏内圆点 / 出屏贴边箭头（顶部/底部内缩不同）；提示排队不互相顶掉；每关 3 条 | A |
| 主菜单 | `Menu/NightbornMenu` | 继续（按存档判定）/ 新的夜晚（带确认框）/ **设置** / 10 关两列；纯代码建 UI；键盘可操作 | A |
| 设置 | `Settings/NightbornSettings` + `UI/SettingsPanel` | 主/环境/音效音量、灵敏度、反转Y、画质、全屏、分辨率；即时生效；独立 JSON | A |
| 音频 | `Audio/NightbornAudio` + `SynthAudio` | 4 条音源，**全部运行时合成**；无 Mixer（接入点见文件末尾注释）；失焦静音 | B |

---

## 5. 协作方式（怎么把活干对 —— 这一节比代码更重要）

### 5.1 不熟悉的功能：**先查现成方案，别自己造**

顺序：**GitHub / 官方示例 / Asset Store / 本地 `rea-agents` MCP（可反编译同类游戏）** → 找到就用 → 实在没有再自己写。

- 「实在没有」只有三种情况：**真找不到**、**许可证不兼容**（本项目是商用闭源，GPL-3.0 不能用）、**耦合过深**（比如它自带存档格式/prefab 依赖，接进来要改半个工程）。
- 自己写的时候**必须在代码注释里写明为什么不用现成方案**（例如菜单「新的夜晚」那段注释）。
- 引入任何外部代码/资产，**先查 LICENSE 并抄进 [`THIRD_PARTY.md`](THIRD_PARTY.md)**。
- 经验参考：本轮曾砍掉两个 HUD 指示器包（一个要求 Unity 6、一个 GPL-3.0）；设置菜单、气味遮挡这类「纯逻辑 + 已有 API」的东西，自己写比接框架便宜得多。

### 5.2 复杂任务：拆成多个简单任务 → 多个子 agent → 主 agent 验收

**这是本项目验证过的最有效分工方式**（2026-11-12 用它一轮修掉 40+ 缺陷）。规则：

1. **按文件域切分**，一个子 agent 一份互不重叠的文件清单，写进它的 prompt 里，并明确「其他文件一律不碰（别的 agent 正在改）」。
2. **只有一个 Unity 编辑器实例** → **子 agent 一律禁止使用 `unity` MCP**；真机验证由主 agent 独占、串行执行。子 agent 只做「读代码 + 改代码 + 给出真机验证清单」。
3. 每个子 agent 的 prompt 必须自带：环境事实（Unity 版本 / 路径 / 相关文件现状）、**文件范围**、**它不该碰什么**（`.unity`、`Editor/`、`Build Base Scene`、git commit）、**硬约束**（只用 2022.3 API；`edit` 报 `ReplaceFileW EIO` 时的共享句柄写法；UTF-8 无 BOM；中文注释）、**交付格式**（缺陷表 `文件:行 | 现象 | 严重度 | 根因`、已改动、未改及原因、真机验证清单）。
4. **主 agent 的职责**（不要把这些下放）：
   - 先出「验收口径」（例如「10 关都能走到出口」「自检 0 失败」），再派活，验收时只对这个口径负责；
   - 合并后**自己编译 + 真机跑**，不采信子 agent 说的「编译通过」；
   - 对每个结果给**明确判定**（通过 / 打回 + 具体读数差异），必要时直接改；
   - 最后**把结论写进 DEVLOG**（数字、复现步骤、踩到的坑），而不是留在聊天里。
5. 典型分组（可直接复用）：敌人 AI+Boss / 玩家手感+相机 / UI+HUD+音频 / 关卡几何+文案 / 流程+存档+自检工具；设置菜单、气味遮挡这类单点任务单独派一个。

### 5.3 主 agent 的验收清单（每次合并后都过一遍）

1. `refresh_unity compile=request mode=force` → `read_console types=["error"]` **必须 0 条**（warning 也要看一眼）；
2. 至少跑一关 `WalkthroughTest`（12 项）+ 至少跑一关 `LevelAutoPilot`（几何）；改动涉及几何就重跑 BuildBase 后跑**全部 10 关**；
3. 涉及存档/设置的改动：退出 Play 看 JSON 与 `.bak`，别在 Play 里手改文件；
4. 涉及 HUD/菜单：截图核对（`manage_camera screenshot capture_source=game_view`）；
5. 收尾：`git status` 看有没有异常产物（例如工具缓存目录）；把结论写进 `HANDOVER_DEVLOG.md` 新章节；更新本文件的参数表与待办。

### 5.4 写代码的工程约定

- **改 `.cs` 一律可能撞 `ReplaceFileW EIO`**（Unity 长期持有句柄，**非 Play 模式也会报**）。用共享句柄覆盖写：
  ```powershell
  $fs=[System.IO.File]::Open($p,'Open','Write','ReadWrite'); $fs.SetLength(0); $fs.Write($b,0,$b.Length); $fs.Flush(); $fs.Dispose()
  ```
  锚点替换前把 `\r\n` 统一成 `\n`。全工程基本是**纯 LF**（44 个 `.cs` 里 42 个零 CR），只有 `Editor/NightbornEditorGuard.cs` 与 `Interaction/Pushable.cs` 残留共 3 个孤立 CR —— 多行锚点匹配不到时先查这两个。
- **中文一律走 UTF-8 文件写入**（`UTF8Encoding($false)`）。用 PowerShell 字符串拼中文会出 `锛/鐨/姝` 这类乱码并把行合并。
- **数行数别信 `Get-Content`**：PowerShell 5.1 默认按 GBK 解码 UTF-8 中文，会吞掉约 6% 的换行（同一文件 `Get-Content` 报 534 行、`read` 报 571 行）。要准确就数字节 `0x0A`。
- 只用 **Unity 2022.3** 存在的 API。`linearVelocity`（2022 用 `velocity`）、`Awaitable` 是 Unity 6 的；
  注意 `FindObjectsByType` / `Object.FindObjectsByType` **2022.3 就有**（`LightExposureProbe` 里已在用）。uGUI 是 `UnityEngine.UI`，不是 UI Toolkit。
- 不新增第三方包（除非 §5.1 的结论是「必须用」并已记进 `THIRD_PARTY.md`）。
- **不要主动 `git commit` / `git push`**，除非人类明确要求。

---

## 6. 坑（每条都真踩过）

| 坑 | 表现 | 解法 |
| --- | --- | --- |
| **`Build Base Scene` 在 Play 下执行** | 先删 URP 资产再抛 `cannot be used during play mode` → 渲染半残 + NRE 刷屏 → **编辑器卡死、MCP 实例消失** | **先 `manage_editor stop`**；已卡死就 `Stop-Process -Name Unity -Force` 重开工程再重跑 Build |
| `edit` / `write` 工具改 `.cs` | `ReplaceFileW EIO (Win32 32)`，非 Play 也报 | 共享句柄覆盖写（§5.4） |
| PowerShell 写中文源码 | 注释乱码、行被合并（`锛`/`鐨`/`姝`） | UTF-8 文件写入，别用字符串拼接管道；改完 grep 自检 |
| 多行锚点替换 | 文件混用 LF/CRLF，匹配不到 | 用单行/短锚点；替换前统一 `\n`；找不到就 `throw`，别静默失败 |
| MCP `read_console` 的 `count` | 拿不到最新一条：`count` 是**从最旧的匹配项开始取** | 用更具体的 `filter_text`（场景名/`[TEST]`/`[PILOT]`），别指望 `count=1` 是最后一条 |
| 合成输入 | 只 `QueueStateEvent` 发一次按键，玩家不动 | **每帧**重新 Queue（`LevelAutoPilot` 已封装）；编辑器失焦时设备会被重置，单次注入不可靠 |
| 幽灵模式 | 只禁用 `GuardAI` 组件不够：敌人身体仍是实心碰撞体（挡住路）、它的灯还会烧你 | 整只 `SetActive(false)`（`LevelAutoPilot.ApplyGhost` 已这么做） |
| 场景里没有 `HUD_Nightborn` | 编辑器脚本 `FindByName` 永远扑空 | 它是 `NightbornRuntimeBoot.EnsureHud()` **运行时**建的；要给 HUD 挂组件就改那里 |
| 相机朝向 | 改 `CAM_Rig.rotation` 无效（截图以为相机没动） | 朝向是 `ThirdPersonCamera._yaw` 私有字段，反射改，或模拟鼠标 delta |
| 编辑器会话活着时手改存档 | 从备份恢复的 `nightborn_save.json` 被静默冲掉 | `SaveSystem` 把数据缓存在内存，任何 `Save()` 整份写回 → **改存档前先 `stop`**，测清档流程必须先备份 |
| 改完 `.cs` 不刷新 | 真机跑的还是旧代码 | `refresh_unity compile=request mode=force wait_for_ready=true`，再看 `read_console` |
| 场景重建 | `BuildAll` 删光重建材质/设置/场景 | 手工改的场景会被全丢；改动只落进 Bootstrap / ObjectiveSetup |
| 射线用旧位置 | 脚本里改了 `transform.position` 之后立即 `Physics.Raycast` | 2022 默认 `autoSyncTransforms=false`，跨帧或在同一段代码里先 `Physics.SyncTransforms()` |
| 数行数 / 读源码被 GBK 解码 | PowerShell 5.1 `Get-Content` 把 UTF-8 中文按 GBK 解，**吞换行**（534 vs 571 行），还可能把中文显示成 `锛/鐨` | 数行数数字节 `0x0A`；读文件用 `read` 工具；写文件用 `UTF8Encoding($false)` |
| 菜单 `Nightborn → 检查场景连线` 报 `PLAYER_Lantern <缺失>` | 提灯方案早已删除（改用自体光），校验脚本没跟着改 | **不是故障**，忽略这条；真要清就删 `NightbornEditorGuard.ValidateScene()` 里那行 `Find("PLAYER_Lantern")` |

---

## 7. 约定

### 7.1 命名与层

- 场景 `L_` / 材质 `M_` / Volume `VP_` / 几何 `GEO_` / 建筑 `BLD_` / 灯 `LAMP_` / 道具 `PROP_` / 遗物 `RELIC_` / 薄幕 `VEIL_` / 环境根 `ENV_` / 灯柱 `LIGHT_`
- 角色与系统：`PLAYER_` `NPC_` `BOSS_` `GUARD_`（敌人的机身/视线锥子物体）`MONK_`（提灯）`CAM_`（相机 rig）`HUD_` `MENU_` `POSTFX_` `AUDIO_` `LEVEL_` `HINT_` `PREVIEW_`
- 本轮新增：`UI_Settings*`（设置面板所有控件）、`TEST_`（测试夹具，如 `TEST_Walkthrough` / `TEST_LevelAutoPilot` / `TEST_ScentBlocker`）
- **预制 `P_` 前缀暂无对象**：工程里 0 个 prefab（玩家/敌人都是 `CreatePlayer()` / `MakeGuard()` 代码生成）
- 层：`Player`=8、`Veil`=9、`Guard`=10（影身穿幕依赖 `Veil` 层；气味遮挡**排除**这三层，只被实体挡）
- 五色板：`#0E0F12`（墨）/ `#8B1E28`（血）/ `#E0A44B`（烛）/ `#4A4F57`（铁）/ `#7A8A8F`（雾）

### 7.2 存档格式（`nightborn_save.json`）

```json
{
  "version": 1,
  "lastLevelId": "Ch01_S02",
  "levels": [
    { "id": "Ch01_S01", "index": 0, "completed": true, "bestTime": 12.1,
      "checkpoint": 0, "relicIds": ["relic_ch01_s01"], "relicTotal": 1 }
  ]
}
```

解锁规则：`index==0` 常开，其余要求**上一关 `completed`**。
「继续」= `lastLevelId` 指向的关若**未通关**就用它，否则取第一个「已解锁且未通关」的关；全通则按钮置灰。
注意 `IsUnlocked(id)` 的语义其实是「**这一关**已完成」（菜单拿它判上一关），内部另有 `IsCompleted(id)`。
遗物 id 逐关唯一：`relic_ch01_s01`…`relic_ch03_s03`（2026-11-12 修掉了三关共用同一 id 的 bug）。

### 7.3 设置格式（`nightborn_settings.json`）

```json
{ "version": 1, "master": 1.0, "ambient": 0.3, "sfx": 0.63,
  "sensitivity": 0.16, "invertY": false, "quality": 5,
  "fullscreen": true, "width": 1920, "height": 1080 }
```
主音量是**线性相乘**（不是 dB 曲线，因为还没有 Audio Mixer）。灵敏度优先级：**设置里明确改过 > 场景序列化值 > 0.16**（`HasSensitivity` 决定）。

---

## 8. 下一轮做什么（按价值排序）

| 优先 | 事项 | 怎么下手 |
| --- | --- | --- |
| 1 | **美术 0 资产** | 全灰盒 + 5 色材质。接免费资产包并统一五色。上架唯一硬门槛 |
| 2 | **手感真人验证** | 守卫转身 90°/s、检测衰减 0.25、推箱 drag 4、药剂 30 s、Boss 视野 100°/16 m、相机 0.16 °/px —— 全是推出来的数值，脚本只能证明「能跑通」，好不好玩必须人上手 |
| 3 | **键位重绑** | 前置：工程没有 `.inputactions`，十几个脚本直接读 `Keyboard.current`。要先补 action asset 再在设置面板加「按键」分区（面板打开期间 `InputSystem.DisableDevice(Keyboard)` 防误触发） |
| 4 | 叙事文本 | 设计要「每关一句独白 + 结局」，现在 0 字；文案硬编码在代码里，建议同时抽本地化表 |
| 5 | 打包与发布 | 图标/版本号/IL2CPP 验证（调试类脚本已整类 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`）、1080p60 压测、Steam 素材 |
| 6 | 细调 | 猎犬绕柱是否要加 ±0.25 m 副射线；`NavMeshRuntimeBake` 同步烘焙几十毫秒是否挪到下一帧；HUD 灼伤/围猎两框在极低血量时是否互相干扰 |

---

## 9. 文档与资产地图

| 想知道 | 去哪 |
| --- | --- |
| 现状、怎么跑、怎么验、有哪些坑 | **本文件** |
| 某个功能怎么实现的、原始验证数据 | [`HANDOVER_DEVLOG.md`](HANDOVER_DEVLOG.md)：§10 光照即地形 · §11 工作方式 · §12 设计决策 25 条 · §13–§21 M0–M3 · §22 目标/教程提示 · §23 菜单「新的夜晚」 · §24 菜单「继续」 · **§25 全关可达性 + 五子系统审计（2026-11-12 第一轮）** · **§26 移速模型/灼伤可视化/设置菜单/气味遮挡（第二轮）** · **§27 交接轮：文档重写 + 仓库清理 + 本届文档被校验出的错** |
| 外部来源与许可 | [`THIRD_PARTY.md`](THIRD_PARTY.md)（引入前必须先查 LICENSE 并抄进去） |
| 真机验证截图 | `Assets/Screenshots/`（**34 张，这些是证据，不要删**）。命名并不整齐，按「谁需要它」来查：<br>**M0–M2 里程碑**：`m0_selfglow` `m0_block_guard` `m1_scene` `m1_menu` `m1_feed_bloodsight` `m2_hud` `p01_lantern_shadows` `p01_lantern_shadow_demo` `p02_burn_hud` `p02_hud` `p02_rebuild_smoke`；<br>**§22 目标/教程提示**：`objective_spawn` `hint_fired` `church_objective` `boss_objective` `arrow_behind` `arrow_behind2` `spawn_fixed` `spawn_fixed2` `ch01_s01` `ch01_s01_live` `street_view` `cathedral`；<br>**§23/§24 菜单**（「新的夜晚」「继续」的唯一物证）：`menu_confirm` `menu_wiped` `menu_restored` `menu_continue` `menu_allDone` `menu_final` `continue_loaded`；<br>**§25/§26（2026-11-12）**：`baseline_menu`（改动前基线）`27_burn_frame_light`（灼伤框亮起）`28_menu_settings_button`（菜单三按钮）`29_settings_panel`（设置面板）。<br>同议题的多版本（`arrow_behind` vs `arrow_behind2`、`spawn_fixed` vs `spawn_fixed2`、`p01_*` 两张）保留旧的，属过程证据 |
| 场景重建日志 | `Logs/rebuild.log`（命令行构建时 `-logFile` 指定的输出名；**当前还没跑过命令行构建，所以文件不存在是正常的**） |
| 已作废、不要参考 | `HANDOVER.docx`（已删除）；DEVLOG §0–§9；`Assets/Scenes/`（空目录，已删） |

---

## 10. 交接尾巴（2026-11-12 下班前状态）

- **已提交，工作树干净**（`git status --porcelain` 为空）。本轮三个提交，按内容分层，便于 review：
  | 提交 | 内容 |
  | --- | --- |
  | `86ca1e0` `fix+feat+test: 全 10 关可达性实测 + 五子系统修复，新增整关巡察工具` | 44 个 `.cs`（含新增 `LevelAutoPilot` / `NightbornSettings` / `SettingsPanel`）+ `.gitignore` / `.gitattributes` |
  | `501d97f` `assets: 按新 Bootstrap 重建 11 个场景 + 材质 / URP 设置` | 11 个 `.unity` + 5 个材质 + 2 个 URP 设置（纯 `BuildAll` 产物） |
  | `fdc107a` `docs: 交接文档重写（面向接手的 AI）+ 仓库清理` | 四份文档 + 截图增删 + 删空目录 `Assets/Scenes` |
  > **尚未 push**。要发布就 `git push origin master`（凭据在 Windows 凭据管理器，见 §2.7）。
- **Unity 编辑器状态**：非 Play、Console 0 error / 0 warning、程序集比所有 `.cs` 新（无待编译）。
- **存档/设置**：本机 `nightborn_save.json` 被测试改动过（10 关全通、`master=1.0`）。要干净体验先备份，再在菜单「新的夜晚 → 清空并重来」。
- 已验证通过的口径见 §2.4；下次改动后请重跑并**更新 §2.4 的数字**，别让文档和现实脱节。

**原交接人**：— 　**本轮接手**：AI（2026-11-12）　**下一次接手**：__________
