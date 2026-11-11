# Nightborn 交接文档 · 2026-11-11

> **读者是接手的 AI。** 这里只写「现状 + 怎么干活」。逐条实施过程、原始验证数据、每次改动的原因在
> `HANDOVER_DEVLOG.md`（按 § 号索引，见 §8）。文中的路径、工具名、命令都实测过，可直接照抄。

---

## 0. 先读这三条，再动手

1. **工作树应是干净的。** 上一轮的改动已全部提交，HEAD = `git log -1`。开工前先 `git status`：
   脏了先搞清楚是谁改的。别 `git checkout`、别重跑 `Build Base Scene`、别删 `Assets/Screenshots/`
   —— 那里的截图是本项目的验证证据，按里程碑入库。
2. **`Build Base Scene` 是破坏性的**：会删掉并重建材质 / 设置 / 全部 11 个场景。任何场景内容改动都要写进
   `Editor/NightbornBootstrap.cs`（几何体块）或 `Editor/NightbornObjectiveSetup.cs`（文案与提示位置），**不要手改场景**。
3. **只认真机结论。** 编辑器截图 + `execute_code` 读数才算验证。`HANDOVER.docx` 是 2026-11-06 的旧 Word 版，
   **已过期，不要参考**；旧 DEVLOG §0–§9 也已作废（见 §8）。

---

## 1. 一眼看完的现状

| 项 | 值 |
| --- | --- |
| 是什么 | Unity 3D 哥特吸血鬼潜行解谜。玩家 Ada 自带 3.5 m 柔光（走哪照哪），靠熄灯 / 推箱遮光 / 压光药剂在暗处穿行，吸血续命，被看见就被围猎 |
| 工程 | `D:\unity\Nightborn`　Unity **2022.3.34f1c1**（必须同版本，换版本会重序列化 URP 资产） |
| 管线 / 输入 | URP（manifest 写 14.0.6、**实际解析 14.0.11**）、Forward+、HDR、MSAA 4×、Linear；Input System 1.7.0，Active Input Handling = **Both** |
| 规模 | 11 个场景（菜单 + 9 关 + 终章）· 41 个 C# 脚本 · **6,538 行**（运行时 4,806 / 编辑器 1,732，含空行注释） |
| 版本库 | 本地 git 分支 `master`；远端 **https://github.com/koishi514-kami/Night-Room**（仓库名 `Night-Room`，与工程名 Nightborn 不同，同一个项目）。推送见 §2.4 |
| HEAD | 见 `git log -1` |
| 完成度 | 完整闭环可玩：菜单选关 → 检查点 → 光/血/化形 → 被抓回滚 → 遗物 → 出口或 Boss 仪式 → 存档 → 回菜单。真机自检 **10/10** |
| 最大短板 | **美术 0 资产**（全灰盒 + 5 色材质，上架唯一硬门槛）；**手感参数全未真人验证** |

**本轮已修**（细节见 DEVLOG §22–§24）：检查点开局白块糊屏、P0 目标提示、P0 教程一行文案、菜单「新的夜晚」、菜单「继续」、`Play 当前场景`。

---

## 2. 连上 / 跑 / 验

### 2.1 MCP（工程内已装 `com.coplaydev.unity-mcp@10.3.0`，stdio 6400）

```
read_mcp_resource unity "mcpforunity://instances"      # 取实例名 Nightborn@<hash>（hash 每次可能变）
set_active_instance "Nightborn@<hash>"
manage_editor play | stop                              # play / stop；动场景前一律先 stop
manage_scene load path=Assets/Nightborn/Scenes/L_Ch01_S01_Street.unity    # 参数名是 path，不是 scene_path
execute_code code="..."                                # 跑任意 C#：反射、传送玩家、读组件
manage_camera screenshot capture_source=game_view include_image=true      # 截图落 Assets/Screenshots
execute_menu_item menu_path="Nightborn/Play 当前场景"
refresh_unity compile=request mode=force wait_for_ready=true              # 改完 .cs 必须刷新编译
```

接不上先查 `HKCU\Software\Unity Technologies\Unity Editor 5.x`（键名带哈希后缀）里的
`MCPForUnity.UseHttpTransport`，必须是 **0**（Unity 曾自己起 HTTP 8080，而客户端只认 6400）。

**进关卡只有两条路**（直接点 Play 永远进菜单 —— 守卫把 `playModeStartScene` 钉死在 `L_Main`）：

- 菜单 `Nightborn → Play 当前场景`（**F5**）：先打开关卡场景，再点它（内部用 `SessionState` 记一次性请求，不留状态）；
- 运行时 `SceneManager.LoadScene("L_Ch01_S01_Street")`。

### 2.2 自驾测试流程（照做即可）

1. `play` → 等 6 s → 截图，确认菜单；
2. 进关卡（2.1 的两条路任选）→ 挂自检：
   `new GameObject("TEST_Walkthrough").AddComponent<Nightborn.DebugTools.WalkthroughTest>();`
3. 等 ~45 s → `read_console types=[log]` 看 10 条 ✅/❌。

**期望值**：`L_Ch01_S02_Street` = **10/10**；`L_Ch01_S01_Street` = **8/10** —— S01 按设计没有执灯修士，
第 1 项（场景接线）与第 9 项（被发现回检查点）必失败，**不是 bug**。

> 脚本环境产生不了持续移动输入：推箱位移只有 0.03–0.04 m（最小推力）、气味追踪无法端到端验证。这两项必须真人。

### 2.3 重建全部场景

菜单 `Nightborn → Build Base Scene`（**Play 模式下点会静默失败**），或命令行：

```powershell
& "D:\Unity\2022.3.34f1c1\Editor\Unity.exe" `
  -projectPath "D:\unity\Nightborn" -batchmode -quit `
  -executeMethod Nightborn.EditorTools.NightbornBootstrap.BuildAll `
  -logFile "D:\unity\Nightborn\_rebuild.log"
```

末尾会自动调 `NightbornObjectiveSetup.DecorateAll()`（目标点 + 教程提示，幂等）。

### 2.4 推送

远端是 **https://github.com/koishi514-kami/Night-Room**（分支 `master`）。仓库名 `Night-Room` 与工程名
Nightborn 是同一个项目，别以为是两个仓库。

```powershell
git -C D:\unity\Nightborn remote -v          # 应看到 origin → 上面那个 URL
git -C D:\unity\Nightborn pull origin master # 先拉
git -C D:\unity\Nightborn push origin master # 再推
```

首次 push 需要凭据（三种任选）：`gh auth login`（GitHub CLI，已装但未登录）、
用 PAT 走 `git -c credential.helper=store`、或配 SSH key。
**不要把 PAT 写进 remote URL 或 `.git/config`** —— 会跟着仓库一起被看到。

---

## 3. 内容地图

### 3.1 场景（`Assets/Nightborn/Scenes/`）

| 场景 | id / 序 | 敌人 | 备注 |
| --- | --- | --- | --- |
| `L_Main` | — | — | 菜单：继续 / 新的夜晚 / 10 关两列 |
| `L_Ch01_S01_Street` | `Ch01_S01` / 0 | 守卫 | 教学关 |
| `L_Ch01_S02_Street` | `Ch01_S02` / 1 | 守卫 + 修士 | 引入「吹不灭的灯」 |
| `L_Ch01_S03_Street` | `Ch01_S03` / 2 | 守卫 + 修士 | 掩体 5→3，街灯在必经路 (0,0,8) |
| `L_Ch02_S01_Church` | `Ch02_S01` / 3 | 猎犬 | 40×40 柱廊，2 盏可吹烛台 |
| `L_Ch02_S02_Church` | `Ch02_S02` / 4 | 猎犬 ×2 | 第二只巡侧廊 |
| `L_Ch02_S03_Church` | `Ch02_S03` / 5 | 猎犬 ×2 + 修士 | 烛台只剩 1 盏 |
| `L_Ch03_S01_Mansion` | `Ch03_S01` / 6 | 看守（二层） | 两层 + 23.5° 坡道 |
| `L_Ch03_S02_Mansion` | `Ch03_S02` / 7 | 看守 + 守卫 | 上下同时施压 |
| `L_Ch03_S03_Mansion` | `Ch03_S03` / 8 | 四敌同场 | |
| `L_Ch04_S01_Cathedral` | `Ch04_S01` / 9 | 主教 Boss | 三段仪式，**无出口门** |

每关固定：2 个检查点（`(0,0,-16)` + 各关一处）、1 处血之遗物、1 个出口（终章除外）、出生点 `(0,0.2,-17)`；
玩家 + 相机 + 全套组件由 `NightbornBootstrap.CreatePlayer()` 生成，**未 prefab 化**。

### 3.2 代码地图（`Assets/Nightborn/Scripts/`）

```
Runtime/
  Player/      PlayerMotor SelfGlow LightExposureProbe DetectionMeter ShiftForm
               BloodSight PlayerFeeder BloodSystem DimPotion
  Detection/   GuardAI LanternMonk Bloodhound Warden
  Boss/        BossRitual RitualGate
  Level/       LevelFlow SaveSystem Checkpoint ExitGoal Relic DouseableLamp
               FeedTarget NavMeshRuntimeBake ObjectiveTarget TutorialHint
  Interaction/ IInteractable PlayerInteractor Pushable
  Camera/      ThirdPersonCamera
  Audio/       NightbornAudio SynthAudio
  UI/          NightbornHud ObjectiveHud
  Menu/        NightbornMenu
  Debug/       NightbornDebugHud NightbornRuntimeBoot WalkthroughTest
Editor/
  NightbornBootstrap        场景生成器（11 个场景 / 材质 / 设置，破坏性）
  NightbornEditorGuard      Play 默认进菜单；空场景自动开街区
  NightbornObjectiveSetup   目标点 + 教程提示装饰（幂等，BuildAll 末尾自动跑）
  NightbornScenePlay        「Play 当前场景」F5
```

---

## 4. 系统参数（改手感就看这张表）

> 等级：**A** = 真机 Play 验过；**B** = 只验了脚本/API 路径；**C** = 未验证。

| 系统 | 文件 | 关键参数 | 级 |
| --- | --- | --- | --- |
| 自体光 | `Player/SelfGlow` | 3.5 m / 0.9 强度 / Soft 512 / 呼吸 5% | A |
| 光照采样 | `Player/LightExposureProbe` | 环境 0.5×、月 0.12×、点光 `I×(1−d/range)²`；遮挡反打射线；排除自体光 | A |
| 检测度 | `Player/DetectionMeter` | 取所有敌人最高确认进度 ×100；60 可疑 / 100 围猎 | A |
| 守卫 | `Detection/GuardAI` | 视锥 65–75°/11 m；光下 0.3 s、暗处 3 s 确认；衰减 0.25/s；搜索 20 s | A |
| 执灯修士 | `Detection/LanternMonk` | 灯 5 m/1.4；每 8 s 巡照 2.2 s → 8.5 m/2.4；吸血后灯灭 | A |
| 夜巡犬 | `Detection/Bloodhound` | 视锥 35°/7 m；10 m 内移动 +0.55/s 气味；追击 4.6 | B |
| 钟楼看守 | `Detection/Warden` | 90°/14 m；巡逻 1.0 / 追击 1.6；搜索 60 s；**不可吸血** | B |
| 主教 Boss | `Boss/BossRitual` | 3 圣灯 → 2 道门 → 压光（自体光 ≤1.25 m）贴近祭坛 3 m | B |
| 化形 | `Player/ShiftForm` | Tab；灭光 + 穿 `Veil` 层；6 s / 冷却 8 s；强光弹回硬直 3 s | A |
| 血视 | `Player/BloodSight` | Q；3 s / 冷却 25 s；标出敌人与遗物 | A |
| 吸血 | `Player/PlayerFeeder` | 左键；仅背后/侧面；1.4 s 锁定不能动；+30 血；目标昼睡 60 s | A |
| 血储 | `Player/BloodSystem` | 上限 100；灼伤 −6/s；吸血 +30；血瓶 +25；归零→检查点 | A |
| 压光药剂 | `Player/DimPotion` | 数字键 1；30 s；3.5 m → 1 m；**终章 2 瓶** | A |
| 推箱 | `Interaction/Pushable` | Rigidbody 冻结 Y/旋转、drag 4；`OnControllerColliderHit` 推 | B |
| 交互 | `Interaction/PlayerInteractor` | E；从胸口发射线、方向跟相机，2.6 m | A |
| 玩家移动 | `Player/PlayerMotor` | 3.2 / 1.3 m/s；移速倍率是**带 key 的最小值栈**（burn/stagger/freeze/feed） | A |
| 相机 | `Camera/ThirdPersonCamera` | 2–6 m；`SphereCastNonAlloc` 跳过玩家自身；撞墙按命中距离夹取 | A |
| 关卡流程 | `Level/LevelFlow` | 检查点、被抓回滚、血储归零回滚、通关、Esc 回菜单 | A |
| 存档 | `Level/SaveSystem` | 单 JSON，见 §7.2 | A |
| HUD | `UI/NightbornHud` | 边缘红晕+心跳、敌人头顶标记、三格能力、遗物计数、通关提示 | A |
| 目标提示 | `UI/ObjectiveHud` + `Level/ObjectiveTarget` | 一行目标+距离；屏内圆点 / 出屏贴边箭头（48 px 三角形，运行时画）；终章按 Boss 阶段换文案 | A |
| 教程提示 | `Level/TutorialHint` | 不可见触发体 + 一行文案 5.5 s；每关两个，文案表在 `NightbornObjectiveSetup` | A |
| 主菜单 | `Menu/NightbornMenu` | 继续（按存档判定）/ 新的夜晚（带确认框）/ 10 关两列；纯代码建 UI | A |
| 音频 | `Audio/NightbornAudio` + `SynthAudio` | 4 条音源，**全部运行时合成**；无 Mixer、无音量设置 | B |

---

## 5. 未完成的活（按价值排序）

| 优先 | 事项 | 现状 / 怎么下手 |
| --- | --- | --- |
| 1 | **美术 0 资产** | 全灰盒 + 5 色材质。接免费资产包并统一五色（`#0E0F12 #8B1E28 #E0A44B #4A4F57 #7A8A8F`）。上架唯一硬门槛 |
| 2 | **手感五参数真人验证** | 守卫转身 `turnSpeed 90` / 检测衰减 `0.25` / 推箱 `drag 4` / 药剂 30 s / Boss 视野 100°/16 m。全是推出来的数值 |
| 3 | **设置菜单** | 音量要先把 `NightbornAudio` 接进 Audio Mixer（现在纯 `PlayOneShot`）；键位重绑要先补 `.inputactions`（现在各处直接读 `Keyboard.current`） |
| 4 | 夜巡犬 / Boss 真实动线 | 只验了算法与 API，没跑图。脚本产生不了持续输入 |
| 5 | HUD 红晕偏重 | 系数 `(1−血)×0.75`，26% 血时画面糊；建议 0.5 并加下限 |
| 6 | NavMesh 不随推箱更新 | 箱子推走后敌人可能穿过；给箱子挂 `NavMeshObstacle`(carve) |
| 7 | 叙事文本 | 设计要「每关一句独白 + 结局」，一个字没写；文案硬编码在代码里，没抽本地化表 |
| 8 | 打包与发布 | 图标/版本号/IL2CPP 验证（`WalkthroughTest` 用了 `GetField` 反射，发布前排除）、1080p60 压测、Steam 页素材 |

> 终章药剂卡关已修（第三段必须压光，所以给 2 瓶）；仍建议检查点补一瓶。

---

## 6. 坑（每条都真踩过）

| 坑 | 表现 | 解法 |
| --- | --- | --- |
| Unity 持着 `.cs` 句柄 | `edit` 工具报 `ReplaceFileW EIO (Win32 32)` | 用 `[System.IO.File]::Open(path,'Open','Write','ReadWrite')` 写入；改完用 read 工具复核 |
| PowerShell 写中文源码 | 注释乱码、行被合并（`锛`/`鐨`/`姝`） | 中文一律走 UTF-8 文件写入（`UTF8Encoding($false)`），别用字符串拼接管道；改完 grep 自检 |
| 替换锚点 | 文件混用 LF/CRLF，多行锚点匹配不到 | 用**单行/短锚点**；找不到就 `throw`，别静默失败 |
| 场景里没有 `HUD_Nightborn` | 编辑器脚本 `FindByName` 永远扑空 | 它是 `NightbornRuntimeBoot.EnsureHud()` **运行时**建的；要给 HUD 挂组件就改那里 |
| 相机朝向 | 改 `CAM_Rig.rotation` 无效（截图以为相机没动） | 朝向是 `ThirdPersonCamera._yaw` 私有字段，反射改，或模拟鼠标 delta |
| 编辑器会话活着时手改存档 | 从备份恢复的 `nightborn_save.json` 被静默冲掉 | `SaveSystem` 把 `_data` 缓存在内存，任何 `Save()` 都整份写回。**改存档前先 `stop`**，测清档流程必须先备份 |
| `Build Base Scene` | Play 模式下点了报"已尝试"但场景没重建 | 先 `stop` 再点 |
| 场景重建 | `BuildAll` 删光重建材质/设置/场景 | 手工改的场景会被全丢；改动只落进 Bootstrap |
| MCP 连不上 | 实例列表为空 | 见 §2.1 的 `UseHttpTransport=0` |

---

## 7. 约定

### 7.1 命名与层

- 场景 `L_` / 材质 `M_` / 预制 `P_` / Volume `VP_` / 几何 `GEO_` / 建筑 `BLD_` / 灯 `LAMP_` / 道具 `PROP_` / `PLAYER_` `NPC_` `BOSS_` `LEVEL_` `HINT_`
- 层：`Player`=8、`Veil`=9、`Guard`=10（影身穿幕依赖 `Veil` 层）
- 五色板：`#0E0F12` / `#8B1E28` / `#E0A44B` / `#4A4F57` / `#7A8A8F`

### 7.2 存档格式

`%USERPROFILE%\AppData\LocalLow\NightbornStudio\Nightborn\nightborn_save.json`

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
注意 `IsUnlocked(id)` 的语义其实是「**这一关**已完成」（菜单拿它判上一关），算法内部用 `IsCompleted(id)`。

---

## 8. 文档地图

| 想知道 | 去哪 |
| --- | --- |
| 现状、怎么跑、怎么验 | 本文件 |
| 某个功能怎么实现的、验过什么 | `HANDOVER_DEVLOG.md`：§10 P0-1 光照即地形 · §11 工作方式 · §12 设计决策 25 条 · §13–§21 M0–M3 各里程碑 · **§22 目标/教程提示 + Play 当前场景** · **§23 菜单「新的夜晚」** · **§24 菜单「继续」** |
| 外部来源与许可 | `THIRD_PARTY.md`（引入前必须先查 LICENSE 并抄进去） |
| 旧文档 | `HANDOVER.docx`（2026-11-06，**过期**）；DEVLOG §0–§9 是 M0 之前的旧交接，**已作废**，别当现状读 |

**工作方式硬规矩**：不熟悉的功能先去 GitHub / Asset Store / 官方示例找现成方案，或用本机 `rea-agents` MCP
（可反编译二进制 / APK / .NET / JS-Electron）逆向同类游戏；只有在**找不到、许可证不兼容、耦合过深**时才自己写，
并在代码注释里写明原因。每一笔外部来源记进 `THIRD_PARTY.md`。（本轮就按这条砍掉了两个 HUD 指示器包：一个要求
Unity 6、一个 GPL-3.0。）

---

**交接人**：__________ 　**接手人**：__________ 　**日期**：__________
