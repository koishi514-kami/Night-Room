# 《夜裔 · 血月临城》工程交接文档

> Unity 3D 哥特潜行解谜 · 项目代号 **Nightborn** · 交接日期 2026-11-06
> 基线版本：git `671bdd2`（第 7 次提交，交接文档）· 工程路径 `D:\Unity\Nightborn`
> **P0-1（光照即地形）已于 2026-11-11 实现并通过真机验证，改动尚未提交，详见 §10**

---

## 0. 一句话交接

工程已经把"能跑起来的底座"做完了：URP 渲染管线、开场街区灰盒、可走可潜行的玩家、跟随提灯、后处理与调试浮层全部就位并通过编译验证。**P0-1「光照即地形」已接通**（提灯实时阴影 + 光照采样 + 灼伤判定），血储 / 暴露度 / 巡逻 AI 尚未开始写，接手者从这里继续往上垒。

---

## 1. 五分钟上手

### 1.1 环境

| 项 | 值 |
| --- | --- |
| Unity 版本 | **2022.3.34f1c1**（必须同版本，否则 URP 资产会重序列化） |
| 安装路径 | `D:\Unity\2022.3.34f1c1` |
| 渲染管线 | Universal RP **14.0.6**，渲染器模式 **Forward+** |
| 输入 | Input System **1.7.0**，Active Input Handling = **Both** |
| 色彩空间 | Linear |
| 目标平台 | Windows / 1920×1080 / 60 fps |

### 1.2 跑起来

1. Unity Hub 打开 `D:\Unity\Nightborn`（不要新建工程、不要换版本）。
2. 首次导入要等 URP / Input System 编译完，Console 出现 `Tundra build success` 才是真的编过。
3. 场景唯一：`Assets/Nightborn/Scenes/L_Ch01_S01_Street.unity`（编辑器守卫会自动打开，并把 Play 默认场景钉死为它）。
4. 按 **Play**，左上角出现 `Nightborn 自检` 浮层，按 **F1** 开关。

### 1.3 操作

| 按键 | 行为 |
| --- | --- |
| W A S D / 方向键 | 移动（相机相对方向） |
| Shift（按住） | 潜行，速度 3.2 → 1.3 m/s |
| 鼠标移动 | 转视角（pitch 限 -25°~55°） |
| 滚轮 | 相机拉远拉近（2.0–6.0 m，默认 3.4） |
| Esc / 鼠标左键 | 解锁 / 重新锁定鼠标 |
| F1 | 调试浮层 |

---

## 2. 仓库与目录

### 2.1 版本控制

- git 仓库在工程根目录，`user.name = Nightborn Dev`，`user.email = dev@nightborn.local`
- 当前 7 次提交，75 个受控文件（P0-1 改动未提交）：

| 提交 | 说明 |
| --- | --- |
| `f25eb48` | 初始化工程、URP 14 基础设施、开场街区灰盒 |
| `5bca5b6` | README：环境、键位、光照基线 |
| `9ea9c51` | 相机穿墙拉近、输入兜底、F1 浮层、Play 场景守卫；删除模板 SampleScene |
| `c825adc` | 空场景自动打开街区、场景连线检查菜单 |
| `48350b6` | 修复 ThirdPersonCamera 的四元数取负编译错误 |
| `997f736` | 修复后处理：VolumeProfile 子资源落盘 + 渲染器挂 PostProcessData |
| `671bdd2` | 交接文档 HANDOVER.md / .docx |

- `.gitignore` 已排除 `Library/ Temp/ Logs/ obj/ Build/ _Preview/ .vs/ *.csproj *.sln`。
- 换行符 LF→CRLF 的警告是 Windows 正常现象，忽略即可。

### 2.2 目录约定

```
Assets/Nightborn/
├─ Scenes/      L_Ch01_S01_Street.unity      唯一场景（由脚本生成，见 §6 陷阱 1）
├─ Scripts/
│  ├─ Runtime/Player/   PlayerMotor.cs, LanternFollow.cs, LightExposureProbe.cs
│  ├─ Runtime/Camera/   ThirdPersonCamera.cs
│  ├─ Runtime/Debug/    NightbornDebugHud.cs, NightbornRuntimeBoot.cs
│  └─ Editor/           NightbornBootstrap.cs, NightbornEditorGuard.cs
├─ Materials/   M_InkStone / M_BloodBrick / M_CandleGold / M_IronGrey / M_MistTeal
├─ Screenshots/ p01_burning_under_lamp.png, p01_lantern_shadow_demo.png（P0-1 验收图）
└─ Settings/    Nightborn_URP.asset, Nightborn_Renderer.asset, VP_Global.asset
```

命名前缀：场景 `L_`、材质 `M_`、预制体 `P_`、Volume `VP_`、几何 `GEO_`、建筑 `BLD_`、灯 `LAMP_`、道具 `PROP_`、玩家 `PLAYER_`、相机 `CAM_`。

---

## 3. 代码清单与职责

| 文件 | 行数 | 职责 | 关键点 |
| --- | --- | --- | --- |
| `Scripts/Runtime/Player/PlayerMotor.cs` | 115 | CharacterController 驱动移动 | walk 3.2 / sneak 1.3 / accel 14 / gravity −22；相对 `CAM_Rig` 求方向并 Slerp 转身；掉出 y<−20 回出生点；对外暴露 `IsSneaking`、`CurrentSpeed`、`RawInput`；**`SetExternalSpeedMultiplier()` 供灼伤/减速区调用**（P0-1） |
| `Scripts/Runtime/Player/LightExposureProbe.cs` | 205 | **光照即地形判定（P0-1）** | 10Hz 采样胸口点：环境底光 0.5×亮度 + 方向光 0.12×强度 + 点光 `intensity×(1−d/range)²`；遮挡用「玩家→灯」反打 `RaycastNonAlloc`；0.65 进 / 0.55 出滞回；灼伤时移速 ×0.45；手持提灯不参与灼伤；对外 `LightLevel` / `IsBurning` / `CarriedLanternIntensity` |
| `Scripts/Runtime/Camera/ThirdPersonCamera.cs` | 125 | 第三人称跟随 | 鼠标 Δ 转 yaw/pitch；`SphereCastNonAlloc`（半径 0.28）遇墙把相机夹到 `hit.distance−0.05`（下界 0.35m，不穿墙）；**跳过玩家自身碰撞体**；target 为空按 `PLAYER_Ada` / tag Player 自愈 |
| `Scripts/Runtime/Player/LanternFollow.cs` | 78 | 提灯跟随 + 烛光抖动 | `Vector3.SmoothDamp` 0.12 s；offset (0.35, 1.25, 0.28)；Perlin 抖动 1.6±0.12、频率 7.5；**阴影由 `castShadows` 字段控制（默认 Soft / 512 / 强度 0.8），Awake 统一应用** |
| `Scripts/Runtime/Debug/NightbornDebugHud.cs` | 104 | F1 自检浮层 | 显示 Unity 版本、fps、场景名、玩家坐标/输入向量/速度、相机位置与 clip、**光照值 / 灼伤状态 / 移速倍率 / 提灯光强**、Input System 状态、最近报错 |
| `Scripts/Runtime/Debug/NightbornRuntimeBoot.cs` | 18 | 运行时自动挂 HUD | `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` |
| `Scripts/Editor/NightbornBootstrap.cs` | 408 | **场景生成器**（菜单 Nightborn → Build Base Scene） | 重建渲染器/管线/材质/Volume/街区场景并渲染预览图；已在生成时挂 `LightExposureProbe`、提灯开 Soft 阴影 |
| `Scripts/Editor/NightbornEditorGuard.cs` | 114 | 编辑器守卫 | 钉死 `playModeStartScene`；空场景自动切街区；菜单「打开开场街区场景」「检查场景连线」 |

---

## 4. 场景 L_Ch01_S01_Street（64 个 GameObject）

### 4.1 内容

| 组 | 对象 | 尺寸 / 说明 |
| --- | --- | --- |
| 地面 | `GEO_Ground` | 90 × 60，材质 M_InkStone |
| 人行道 | `GEO_Sidewalk_L/R` | 两侧抬升条带，M_IronGrey |
| 建筑 | `BLD_L_1..5`、`BLD_R_1..5` | 高 9 / 10 / 12 / 13 / 15，血砖 M_BloodBrick + 铁灰腰线 `BLD_TRIM_0..3` |
| 封口墙 | `GEO_EndWall_S`、`GEO_EndWall_N_L/R/Top` | 北端留门洞（当前是"目标出口"的占位） |
| 拱桥 | `GEO_Arch_L/R`、`GEO_Arch_Beam` | 柱在 x=±8.5，梁在 y=11 |
| 街灯 | `LAMP_Post_1..6` + `LAMP_Bulb_*` + `LAMP_Light_*` | 点光 强度 2.2 / 范围 9 m / 无阴影；灯泡发光球 |
| 道具 | `PROP_Crate_1..6` | 可当掩体的货箱 |
| 玩家 | `PLAYER_Ada`（含 `PLAYER_Body`）、`PLAYER_Lantern`（含 `LANTERN_Glass`、`LANTERN_Light`） | CharacterController 高 1.8 / 半径 0.34；出生点 z≈−22 |
| 相机 | `CAM_Rig` → `CAM_Main` | 第三人称，MainCamera tag，挂 UniversalAdditionalCameraData + AudioListener |
| 光照 | `LIGHT_Moonlight` | 方向光 强度 0.8、Euler(55,−30,0)、Soft 阴影 2048 |
| 后处理 | `POSTFX_GlobalVolume` | Global Volume，profile = VP_Global |

### 4.2 光照与后处理基线（改之前先看这里）

| 项 | 值 |
| --- | --- |
| 环境光 | Ambient Mode = Flat，颜色 (0.15, 0.16, 0.20)（比 0.05 亮，别调回去，会全黑） |
| 雾 | Exponential Squared，密度 0.012，颜色 M_Mist (0.478, 0.541, 0.561) |
| 主光阴影 | 距离 35 m、4 级级联、贴图 2048 |
| URP | HDR 开、MSAA 4×、Render Scale 1、Forward+、不生成深度/不透明贴图 |
| Post FX | ACES Tonemapping / Bloom(0.6, 阈值 0.9, scatter 0.7) / Vignette(0.35, 平滑 0.4) / ColorAdjustments(postExposure −0.2, saturation −15, contrast +8) |

### 4.3 五色限定（美术底线，新增资产必须落在这五个色里）

| 名称 | Hex | 材质 |
| --- | --- | --- |
| 深夜黑 | `#0E0F12` | M_InkStone |
| 血砂红 | `#8B1E28` | M_BloodBrick |
| 烛黄 | `#E0A44B` | M_CandleGold（带自发光） |
| 铁灰 | `#4A4F57` | M_IronGrey |
| 雾青 | `#7A8A8F` | M_MistTeal |

---

## 5. 已完成 / 未完成

### 5.1 已完成

- [x] URP 14 + Forward+ 管线、后处理（已验证生效，见 `_Preview/preview_street_01.png`）
- [x] 开场街区灰盒（64 对象，含拱桥、街灯、货箱、门洞）
- [x] 玩家移动 / 潜行、相机跟随与穿墙拉近、提灯跟随与烛光抖动
- [x] F1 调试浮层、场景守卫、场景连线自检
- [x] git 版本控制 + README
- [x] **P0-1 提灯实时阴影 +「光照即地形」判定**（2026-11-11，真机验证见 §10）

### 5.2 未完成（按设计文档的优先级）

| 优先级 | 内容 | 验收标准 |
| --- | --- | --- |
| ~~P0~~ ✅ | ~~**提灯实时阴影 +「光照即地形」判定**~~ | 已完成：见 §10，F1 浮层直接可读 `光照 / 灼伤 / 移速倍率` |
| P0 | 血储系统 | 血储 100，夜行 −6/min，吸血 +30，血视 5/次，灯芯 +25（扣血接 `LightExposureProbe.IsBurning`） |
| P1 | 暴露度 0–100 | 光照 +12/s、阴影 −6/s，>60 触发巡逻，满 100 触发"围猎"（读 `LightLevel` / `CarriedLanternIntensity`） |
| P1 | 巡逻 AI | NavMesh 巡逻 + 视锥检测 + 追击状态机（可用 `unity-navmesh` 技能；光照遮挡已有反打射线可复用） |
| P2 | 关卡 ×3 章 ×9 关、教程引导、存档 | 单关 3–4 分钟，全流程约 36 分钟 |
| P2 | UI / 音频 / 打包 | HUD（血储、暴露条）、Volume 混音、Windows 64 位包 ≤ 300 MB |

---

## 6. 陷阱与坑（接手先读这段）

1. **`NightbornBootstrap.BuildAll` 是破坏性的**：它会 `DeleteAsset` 并重建渲染器、管线、5 个材质、VP_Global 和整个场景。**在编辑器里手工改场景后再跑它 = 全部丢失**。要改场景请改脚本，或另存新场景并把新逻辑放进 `Scripts/Runtime`。
2. **后处理曾经整份失效**（`997f736` 已修）：`VolumeProfile.Add<T>()` 只把组件加进内存，必须 `AssetDatabase.AddObjectToAsset(component, profile)` 才会落盘，否则序列化成 `fileID: 0`。同时 `UniversalRendererData.postProcessData` 必须指向 URP 的 PostProcessData.asset，否则 Volume 完全不起作用。
3. **提灯阴影由脚本接管，改 Inspector 的 Light 组件无效**：`LanternFollow.Awake()` 会按 `castShadows / shadowResolution / shadowStrength` 三个字段重写 `LANTERN_Light`，所以要让提灯不投影就取消勾选 `castShadows`。街灯仍是 `shadows: None`（性能取舍，6 盏点光全开阴影会掉帧）。
4. **编辑器开着时，外部工具写工程内文件会失败**：报 `ReplaceFileW EIO (Win32 32)`。要走命令行改文件时先关 Unity，或用 `[IO.File]::Open(..., Truncate, Write, ReadWrite)`。
5. **Console 里的红色 `error CS` 可能是历史记录**：判断编译是否成功，看 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 里最后一条 `*** Tundra build success` 和 `Library/ScriptAssemblies/Assembly-CSharp.dll` 的时间戳。
6. **`D:\Unity\Nightborn_compiletest`** 是早期做编译验证留下的副本，被僵尸 Unity 进程占着，重启后可整目录删除，与主工程无关。
7. **输入全部走 Input System**（`Keyboard.current` / `Mouse.current`）：若浮层显示"Keyboard.current 为 null"，去 Project Settings → Player → Active Input Handling 确认是 Both / Input System Package。
8. **不要删除 `NightbornEditorGuard.cs`**：它负责把 Play 默认场景钉死；删掉后可能出现"点 Play 进空场景全黑"的老问题。
9. 本机无 dotnet SDK，离线编译校验要用 Unity 自带的 Roslyn（命令见 §7.2）。
10. **QualitySettings 只有 level 5 绑定了 URP**，其余等级的 `customRenderPipeline` 是 `fileID: 0`。切质量等级会静默掉回 Built-in（画面变粉/全黑）。要改质量请只动 level 5，或把 `Nightborn_URP` 写到所有等级。
11. **相机碰撞曾经把玩家自己算成墙**：`SphereCast` 起点在玩家胶囊体内部，旧写法会把相机永久拉到 1.2m（贴脸）。现在用 `SphereCastNonAlloc` + 跳过 `target` 根节点，并且下界改成按命中距离夹取（0.35m 起），不要再退回 `minDistance*0.6` 的老写法。
12. **Unity MCP 桥接模式**：本机 MCP for Unity 10.3.0 默认走 HTTP（Unity 自己在 8080 起服务），而 IDE 侧的 stdio MCP 客户端只认 Unity 的 6400 端口。已经通过 EditorPrefs `MCPForUnity.UseHttpTransport = 0` 切回 stdio 模式；若哪天 MCP 又连不上，先查这条注册表项（`HKCU\Software\Unity Technologies\Unity Editor 5.x`，名字带哈希后缀）。

---

## 7. 常用命令

### 7.1 无 GUI 重建场景

```powershell
& "D:\Unity\2022.3.34f1c1\Editor\Unity.exe" `
  -projectPath "D:\Unity\Nightborn" `
  -batchmode -quit `
  -executeMethod Nightborn.EditorTools.NightbornBootstrap.BuildAll `
  -logFile "D:\Unity\Nightborn\_rebuild.log"
```

跑完检查 `_rebuild.log` 里的 `[Nightborn]` 行与 `_Preview/preview_street_01.png`。

### 7.2 不开编辑器做编译校验（2022.3 自带 Roslyn）

```powershell
$dotnet = "D:\Unity\2022.3.34f1c1\Editor\Data\NetCoreRuntime\dotnet.exe"
$csc    = "D:\Unity\2022.3.34f1c1\Editor\Data\DotNetSdkRoslyn\csc.dll"
$base   = "D:\Unity\2022.3.34f1c1\Editor\Data"
$refs   = @()
$refs  += Get-ChildItem "$base\Managed\UnityEngine" -Filter *.dll | % FullName
$refs  += Get-ChildItem "$base\Managed" -Filter "UnityEditor.*Module.dll" | % FullName
$refs  += Get-ChildItem "$base\NetStandard\ref\2.1.0" -Filter *.dll | % FullName
$refs  += Get-ChildItem "$base\NetStandard\compat\2.1.0\shims\netstandard" -Filter *.dll | % FullName
$refs  += Get-ChildItem "D:\Unity\Nightborn\Library\ScriptAssemblies" -Filter *.dll | % FullName
$src    = Get-ChildItem "D:\Unity\Nightborn\Assets\Nightborn\Scripts" -Recurse -Filter *.cs | % FullName
& $dotnet $csc /nologo /target:library /langversion:9.0 /nostdlib+ /out:"_checkall.dll" ($refs | % { "/r:$_" }) @src
```

0 error 才算过。

### 7.3 编辑器内自检

菜单 **Nightborn → 检查场景连线**：一次性打印玩家、相机、灯笼、Volume、当前管线、相机前方 40 m 射线结果。

### 7.4 不进 Play 也能验「光照即地形」

Play 后用 `execute_code`（MCP）直接读/传送玩家，比手跑一遍快得多：

```csharp
var player = GameObject.Find("PLAYER_Ada");
var probe  = player.GetComponent<Nightborn.Player.LightExposureProbe>();
var cc     = player.GetComponent<CharacterController>();
cc.enabled = false; player.transform.position = new Vector3(-4.4f, 0.2f, -22f); cc.enabled = true;
probe.Sample();
// → 灯下应为 LightLevel≈0.99 / IsBurning=True；空旷处 ≈0.25 / False
```

---

## 8. 交接验收清单

- [ ] 打开工程，Console 无红字，`Tundra build success`
- [ ] 场景为 `L_Ch01_S01_Street`，Play 后 F1 浮层正常
- [ ] WASD 能动、Shift 变慢、鼠标能转视角、滚轮能拉近距离
- [ ] `_Preview/preview_street_01.png` 与编辑器画面一致（血砖峡谷 + 拱桥 + 6 盏烛黄街灯 + 暗角）
- [ ] 能独立跑通 §7.1 的重建命令
- [ ] **P0-1**：F1 浮层出现「光照 / 灼伤 / 移速倍率 / 手持提灯」一行；走到街灯下（约 4.8 m 内）显示 `灼伤`、移速倍率变 0.45；躲到货箱后立刻回 `安全`

---

## 9. 变更记录

| 日期 | 内容 | 提交 |
| --- | --- | --- |
| 2026-11-01 | 工程初始化、URP、街区灰盒、README | `f25eb48` `5bca5b6` |
| 2026-11-01 | 相机穿墙、输入兜底、F1 浮层、Play 场景守卫 | `9ea9c51` `c825adc` |
| 2026-11-01 | 修复四元数取负编译错误 | `48350b6` |
| 2026-11-06 | 修复后处理整份失效（子资源 + PostProcessData） | `997f736` |
| 2026-11-06 | 本文档 | `671bdd2` |
| 2026-11-11 | **P0-1 光照即地形**：LightExposureProbe、提灯 Soft 阴影、相机自碰撞修复、HUD 光照行 | 未提交 |

---

## 10. P0-1 实施记录（2026-11-11）

### 10.1 改了什么

| 文件 | 改动 |
| --- | --- |
| `Scripts/Runtime/Player/LightExposureProbe.cs` | **新增**。10Hz 采样玩家胸口光照，输出 `LightLevel` / `IsBurning`；灼伤时把 `PlayerMotor` 移速压到 0.45 |
| `Scripts/Runtime/Player/LanternFollow.cs` | 阴影从硬编码 `None` 改成 `castShadows / shadowResolution / shadowStrength` 三字段，`Awake` 统一应用；默认 Soft + 512 |
| `Scripts/Runtime/Player/PlayerMotor.cs` | 新增 `SetExternalSpeedMultiplier()` / `SpeedMultiplier`，外部系统可减速 |
| `Scripts/Runtime/Camera/ThirdPersonCamera.cs` | `SphereCast` → `SphereCastNonAlloc` 并跳过玩家自身碰撞体；穿墙下界改为按命中距离夹取（0.35m），不再插进墙里 |
| `Scripts/Runtime/Debug/NightbornDebugHud.cs` | F1 浮层新增光照行 |
| `Scripts/Editor/NightbornBootstrap.cs` | 重建场景时也挂探针、提灯开 Soft 阴影（保证 `BuildAll` 与手工场景一致） |
| `Assets/Nightborn/Scenes/L_Ch01_S01_Street.unity` | `PLAYER_Ada` 挂上 `LightExposureProbe`（`carriedLantern` 指向 `LANTERN_Light`）；`LANTERN_Light` 阴影 Soft / 512 / 强度 0.8 |

### 10.2 数值模型

```
LightLevel = min(0.35, 环境光亮度 × 0.5)            // Flat (0.15,0.16,0.20) → 0.08
           + 方向光强度 × 0.12                      // 月光 0.8 → 0.096
           + Σ 点光 intensity × (1 − d/range)²       // 只算没被遮挡的
灼伤：≥ 0.65 进入，< 0.55 解除（滞回）；灼伤时移速 ×0.45
遮挡：从玩家胸口朝灯反打 RaycastNonAlloc（长度 dist−0.2），命中非玩家物体即视为被挡
手持提灯：默认排除出灼伤计算（`carriedLantern` 字段），但 `CarriedLanternIntensity` 仍对外提供
```

街灯（2.2 / 9 m）的灼伤半径约 **4.8 m**，与「走到灯下就危险、躲进箱子就安全」的设计意图一致。

### 10.3 真机验证数据（Unity 2022.3.34f1c1 / Play 模式）

| 场景 | LightLevel | IsBurning | 移速倍率 |
| --- | --- | --- | --- |
| 出生点 (0, −22) | 0.34 | False | 1.00 |
| 街灯正下 (−4.4, −22) | 0.99 ~ 1.02 | True | 0.45 |
| 空旷街心 (0, −17) | 0.25 | False | 1.00 |
| 灯下 + 插入 0.6 m 厚墙 | 0.18 | False | 1.00 |
| 拆掉墙 | 1.02 | True | 0.45 |

相机：空旷地 `|cam−rig| = 3.400`（不再被玩家自身碰撞体拉到 1.2）；贴墙 1 m 时 `|cam−rig| = 0.673` 且相机停在墙面外（x=−8.87 vs 墙面 −9.2）。

编辑器内 7 盏点光（1 盏投阴影，512×6 面）实测 **≈190 fps**（Play 模式，无 VSync 上限），性能余量充足。

### 10.4 截图

- `Assets/Screenshots/p02_burn_hud.png`：灯下 `光照 0.99 / 灼伤 / 移速倍率 0.45`
- `Assets/Screenshots/p01_lantern_shadow_demo.png`：提灯在货箱与墙面投出的实时阴影
- `Assets/Screenshots/p02_hud.png`：血储 / 暴露度读数与两条指示条（P0-2）

---

## 11. 工作方式：先找现成方案，别自己造（2026-11-11 追加，接手必读）

这条比任何技术细节都重要：**不熟悉的功能不需要从零发明，先去拿别人的实现。**

### 11.1 规则

1. **先检索，再动笔**。任何机制（视线锥、巡逻状态机、光照采样、穿墙、存档、光照遮罩）在 GitHub / Asset Store / 官方示例 / 论坛里几乎都有可用的实现，先找到再改。
2. **要理解"别人怎么做的"，就逆向它**。本机已装 **REA（Reverse Engineer Anything）**：`rea-agents` MCP，工具前缀 `mcp__rea__*`，能反编译原生二进制、APK（类/方法 → Java 源码）、.NET 程序集、JS/Electron 应用，并提取资源与配置。用它拆同类游戏，把**参数与结构**抄成结论，而不是抄资产。
3. **只在三种情况下自己写**：找不到可用实现、许可证不兼容、参考实现与本工程耦合过深。自己写在注释里注明"为什么没有复用"。
4. **复用也要本地化**：配色改到五色、命名改成本工程的 `PLAYER_/GEO_/BLD_` 前缀、包一层适配，别把一整个库的无关依赖拖进来。
5. **每一笔外部来源都记进 `THIRD_PARTY.md`**（名称 / 版本 / 许可证 / 用途 / 链接），上架前逐项核查许可证。

### 11.2 常用检索词（现成实现的关键词表）

| 需要的东西 | 检索词 |
| --- | --- |
| 视线锥 / 发现检测 | `unity stealth vision cone`、`unity field of view detection`、`unity guard ai state machine` |
| 巡逻 / 追击状态机 | `unity patrol state machine`、`unity navmesh guard` |
| 光照采样 / 光感 | `unity light detection shader`、`urp custom light probe sampling` |
| 影身穿幕 | `unity phase through walls`、`unity ghost wall walk` |
| 存档 / 检查点 | `unity save system json`、`unity checkpoint trigger` |
| 剪影 / 遮罩 | `unity stencil mask silhouette` |

### 11.3 REA 的边界

只用于理解机制、参数与结构；不复制资产、不搬运受版权保护的代码。逆向得到的结论（阈值、状态机转移、AI 参数）写进本文档对应小节，标明来源作品。

---

## 12. 设计决策记录（2026-11-11，逐条确认后固化）

以后不要再重新讨论这些条目；要改就在这里改一行，并注明日期。

### 12.1 产品

| # | 决策 | 结论 |
| --- | --- | --- |
| 1 | 规模 | **9 关 = 3 章 × 3 关**，单关 4–6 分钟，全流程约 45 分钟（原文档"3 章 × 9 关 / 36 分钟"数学不成立，作废） |
| 2 | 平台 | Windows 64 位 + Steam，1080p / 60fps，包体 ≤ 300 MB，键鼠 + 手柄 |
| 3 | 难度 | 单一难度，**不做**难度选项与辅助开关 |
| 4 | 叙事 | 轻叙事：每关一句独白 + 结局文本；无配音、无对话树 |
| 5 | 美术 | 现阶段用**免费资产包**，统一改到五色板（暂定，最终风格未定） |
| 6 | 音频 | 免费音效 + 环境 drone 循环；无配乐、无配音 |

### 12.2 玩法

| # | 决策 | 结论 |
| --- | --- | --- |
| 7 | 玩家定位 | 掠食者、**不战斗**：吸血使目标昼睡、不致死；被发现只能逃 |
| 8 | 光源 | **取消提灯 → 角色自体光**：固定 3 m 柔光，不可调；变暗只能靠影身或遮挡 |
| 9 | 光操纵 | 三件套：**吹灭街灯 / 推箱挂布遮光 / 压光药剂（30 s，3 m→1 m）** |
| 10 | 血储 | **纯生命值**；删除夜耗（−6/min 在 5 分钟关卡里等于不存在）；吸血 +30 是主要回复 |
| 11 | 血视 | 冷却型：3 秒墙后心跳与血迹，**冷却 25 秒、不耗血**；原"灯芯 +25"改为**血瓶 +25** |
| 12 | 化形 | 光身=实体；**影身=光灭 + 穿薄幕，6 秒时限，强光下被弹回并硬直 3 秒** |
| 13 | 敌人感知 | 双层：**视线锥定"看得见"，光照定"看得多快"**（强光 0.3 s / 暗处 3 s / 影身不可见） |
| 14 | 围猎 | 敌人向**最后已知位置**搜索 20 秒后归位巡逻 |
| 15 | 敌人种类 | 4 种：巡逻守卫 → 执灯修士 → 夜巡犬 → 主教 Boss |
| 16 | Boss | 三段潜行仪式：熄 3 盏圣灯 → 影身穿两道幕 → 压光靠近祭坛；失败只重来该段 |
| 17 | 关卡目标 | 抵达出口为主目标；每关一处可选**血之遗物**（影响结局） |
| 18 | 失败 | 检查点（3–5 处）+ 短追逐；被抓回检查点，血量与暴露回滚 |
| 19 | 空间 | 40×40 箱庭（含拱桥上层与巷道），**3 张场景 × 3 关**：街区 / 教堂 / 宅邸 |
| 20 | 教学 | 关键处弹**一行文字**提示；第 1 关只教移动、潜行、躲光、熄灯 |
| 21 | HUD | 混合：血=屏幕边缘红晕 + 心跳，能力=角色微光，暴露=敌人头顶检测提示 |
| 22 | 键位 | 左键吸血 / Tab 化形 / Q 血视 / E 上下文交互 / Shift 潜行 / R 血瓶 / 1 压光药剂 |
| 23 | 存档 | 检查点自动存档 + 关卡选择（按章分组，显示遗物收集） |
| 24 | 里程碑 | **M0 按新设计重构现有系统 → M1 第 1 关 5 分钟完整闭环**，之后才铺 9 关 |

### 12.3 这些决策让哪些代码失效

| 现有文件 | 新定位 |
| --- | --- |
| `LanternFollow.cs` | → `SelfGlow.cs`：3 m 柔光挂在角色身上、投软阴影、可被影身关闭 |
| `LightExposureProbe.cs` | 灼伤只算**外部光**，自发光不参与；新增 `IsGlowing` 供 AI 使用 |
| `ExposureMeter.cs` | → `DetectionMeter.cs`：由守卫视线驱动，光照只决定确认速度 |
| `BloodSystem.cs` | 删夜耗、血视移出（改冷却）、灯芯→血瓶 |
| 无 | 新增 `ShiftForm` / 光操纵交互 / `GuardAI`（视锥·巡逻·搜索）/ `Checkpoint` + `SaveSystem` / `LevelGoal` |

### 12.4 M0 任务清单（执行序）

| # | 任务 | 状态 |
| --- | --- | --- |
| 1 | `SelfGlow`（替换 `LanternFollow`） | ✅ 完成 |
| 2 | `LightExposureProbe` 收紧（只算外部光 + 排除自体光/灯具自身） | ✅ 完成 |
| 3 | `ExposureMeter` → `DetectionMeter`（守卫驱动） | ✅ 完成 |
| 4 | `BloodSystem` 瘦身（删夜耗、血视移出、血瓶） | ✅ 完成 |
| 5 | `ShiftForm`（Tab / 6 s / 穿幕 Veil 层 / 强光弹回） | ✅ 完成 |
| 6 | 场景 90×60 → 40×40 箱庭（守卫、可吹灭灯、薄幕、检查点、出口、遗物） | ✅ 完成 |
| 7 | 最小存档（JSON）+ 选关 | ⚠️ 存档 ✅ / 选关 UI 待 M1 |
| 8 | 跑通 5 分钟闭环 | ✅ 逻辑闭环已验证（见 §13.3） |

---

## 13. M0 实施记录（2026-11-11）

### 13.1 新增 / 重构的代码

| 文件 | 说明 |
| --- | --- |
| `Runtime/Player/SelfGlow.cs` | 自体光：3 m 固定柔光、Soft 512 阴影、呼吸、`SetGlowing()` |
| `Runtime/Player/ShiftForm.cs` | Tab 化形：影身灭光 + 穿 Veil 层 + 6 s 时限 + 强光弹回硬直 3 s |
| `Runtime/Player/DetectionMeter.cs` | 检测度 0–100 = 所有守卫的最高确认进度；60 可疑 / 100 围猎 |
| `Runtime/Detection/GuardAI.cs` | 航点巡逻 + 视线锥（光决定确认速度：亮 0.3 s / 暗 3 s / 影身不可见）+ 追逐 + 搜索 20 s |
| `Runtime/Level/LevelFlow.cs` | 检查点、被抓回滚、通关、遗物、事件 |
| `Runtime/Level/SaveSystem.cs` | `persistentDataPath/nightborn_save.json`，记录完成度 / 最好成绩 / 检查点 / 遗物 |
| `Runtime/Level/Checkpoint.cs` · `ExitGoal.cs` · `Relic.cs` | 触发体与交互物 |
| `Runtime/Level/DouseableLamp.cs` | 吹灭街灯（光操纵三件套之一） |
| `Runtime/Interaction/IInteractable.cs` · `PlayerInteractor.cs` | E 键上下文交互（从角色胸口发射线，方向跟相机） |
| 删除 | `LanternFollow.cs`、`ExposureMeter.cs`（无引用后清理） |

### 13.2 场景（`L_Ch01_S01_Street`，45 个 GameObject）

40×40 箱庭，四面围墙北侧留 4 m 门洞；两栋楼（BLD_A/B）+ 一道矮墙（GEO_LowWall）切出三条路线；5 个货箱做掩体；两道薄幕（`VEIL_Curtain_A/B`，Veil 层）挡直路、影身可穿；1 盏可吹灭街灯；1 名守卫（4 个航点 + 视线锥灯）；2 个检查点；北门出口；1 处血之遗物。

### 13.3 真机验证（Unity 2022.3.34f1c1 / Play 模式）

| 验证项 | 结果 |
| --- | --- |
| 吹灭街灯 | 灯下外光 **1.06 → 0.18**，灼伤解除 ✅ |
| 站在锥内（强光） | 确认 0.3 s 级；暗处约 3 s；检测度 60 → 可疑、100 → 围猎 ✅ |
| 被抓 | 日志 `被抓住 → 回检查点`，玩家回检查点、检测度清零、血量回滚 ✅ |
| 检查点 | 走入触发区 → `检查点 0 已记录` ✅ |
| 影身 | 暗处：`IsShadow=true`、自体光灭、Player↔Veil 碰撞忽略；退出后恢复 + 8 s 冷却 ✅ |
| 强光下开影身 | 拒绝进入 + 3 s 硬直 ✅ |
| 遗物 | 拾取 → 1/1，写入存档 `relicIds` ✅ |
| 出口 | 进入触发区 → `IsComplete=true`，存档写入 `completed/bestTime` ✅ |
| 编译 | Console 0 error / 0 warning；`Assembly-CSharp.dll` 28 KB → 46 KB |

**踩到并修掉的坑（都写进代码注释了）**

1. 遮挡射线打到**灯具自身灯柱** → 站在灯下永远读不到光；修正为忽略灯具根的碰撞体。
2. 灯被吹灭后光照值仍持续到下一次 1 s 缓存刷新 → 在 `Evaluate` 里直接跳过 disabled 的灯。
3. 相机射线打到自己 → 已排除玩家层；交互射线从**相机**出发够不到近处物体 → 改为从角色胸口出发。
4. 检查点触发体做成 0.2 m 贴地薄板时，CharacterController 擦边不触发 → 改成 3×2×3 立式触发区。
5. 守卫航点被运行时改短会 IndexOutOfRange → 加索引夹取。

### 13.4 已知缺口（M1 起逐条消化）

| 项 | 状态 |
| --- | --- |
| 吸血交互 | ✅ M1 完成（见 §14） |
| 血视 | ✅ M1 完成（冷却型，屏幕投影标记；M2 换 URP 剪影） |
| 推箱 / 压光药剂 | ✅ M1 完成（见 §14.4） |
| 选关 UI | ✅ M1 完成（`L_Main` 菜单场景） |
| 守卫寻路 | ✅ M2 完成：接 AI Navigation（见 §15） |
| 守卫平衡 | ⚠️ 站锥里 0.3 s 秒确认偏硬，M1 已把锥灯强度降到 1.2 |

---

## 14. M1 实施记录（2026-11-11）

### 14.1 新增

| 文件 | 说明 |
| --- | --- |
| `Runtime/Level/FeedTarget.cs` | 可吸目标：**只能从背后/侧面**下手；吸完昼睡 60 s；守卫进入休眠（停巡逻、熄锥灯、清检测） |
| `Runtime/Player/PlayerFeeder.cs` | 鼠标左键吸血：1.4 s 锁定（期间不能动）→ 血 +30 |
| `Runtime/Player/BloodSight.cs` | Q 血视：3 s / 冷却 25 s，穿墙标出守卫（红）、可吸目标（橙）、遗物（金）；零 Shader 的屏幕投影实现 |
| `GuardAI.SetDormant(bool)` | 休眠状态：`Update` 直接返回，锥灯关闭 |

### 14.2 真机验证

| 验证 | 结果 |
| --- | --- |
| 背后靠近 `CanBeFed` | True（正面点积 0.99 时为 False） |
| 吸血 | 血 49 → 77（+30 扣掉灼伤损耗）；目标 `IsAsleep=true`；守卫 `IsDormant=true`、锥灯 `enabled=false`、身体倾倒 90° |
| 血视 | `IsActive=true`、剩余 3.0 s、冷却 25 s；HUD 显示「血视 冷却 11s → 就绪」 |
| 布局修复 | 遗物从楼内挪到 (−13, 1, 16)；4 个航点全部落在开阔带（原有两个在 BLD_A/BLD_B 里，守卫会穿墙走） |

### 14.3 顺手修掉的问题

1. **移速倍率互相覆盖**：`LightExposureProbe` 每 0.1 s 写一次倍率，会把 `ShiftForm` 的硬直和 `LevelFlow` 的冻结冲掉。改成 `PlayerMotor.SetSpeedMultiplier(key, value)` + 取最小值的栈（burn / stagger / freeze）。
2. **遗物埋在建筑体内**（不可达）→ 移到楼外空地。
3. **两个巡逻航点在建筑体内**（守卫直线移动会穿墙）→ 全部改到开阔带。
4. **自体光过曝**（1.6 强度把脚下地面打爆）→ 降到 0.9、范围 3.5。
5. HUD 文件在 PowerShell 往返中被编码搞坏（注释变乱码、行被合并）→ 用干净重写修复，后续中文源码一律用文件写入工具而不是字符串替换。

### 14.4 M1 收尾：推箱 / 压光 / 选关

| 文件 | 说明 |
| --- | --- |
| `Interaction/Pushable.cs` | 可推货箱：Rigidbody 冻结 Y 与旋转、drag 4；玩家 `OnControllerColliderHit` 把速度转成推力（遮光用） |
| `Player/DimPotion.cs` | 压光药剂：1 瓶起手、30 s、自体光 3.5 m → 1 m；数字键 1，`Use()` 供 UI 复用 |
| `Menu/NightbornMenu.cs` | 主菜单 / 选关：纯代码建 UI（Canvas + 按钮 + 文案），按存档解锁，点击加载关卡 |
| `Scenes/L_Main.unity` | 由 Bootstrap 生成；构建列表为 `[L_Main, L_Ch01_S01_Street]` |

**行为变化**

- 编辑器 Play 默认场景改为 **`L_Main`**（`NightbornEditorGuard` 已同步；没有菜单时自动退回街区）。
- 通关后按 **Esc** 回菜单（`LevelFlow`）。
- 菜单场景不挂调试浮层。

**验证**

| 项 | 结果 |
| --- | --- |
| 启动场景 | `L_Main`，菜单 UI 生成：标题 / 关卡按钮（可点）/ 操作提示 / 已通关 1 of 1 |
| 点按钮 | 载入 `L_Ch01_S01_Street`，构建索引 1 |
| 进关卡后 HUD | 存在（修复了 `RuntimeInitializeOnLoadMethod` 只跑一次导致从菜单进关卡没浮层的问题） |
| 压光药剂 | 数量 1 → 0，半径 3.5 → 1.0，重复使用返回 False |
| 推箱 | `Push` 后速度 1.34 m/s、drag 4、约束冻结 Y/旋转；场景里 5 个货箱均已带 `Rigidbody + Pushable` |

---

## 15. M2 实施记录（2026-11-11 起）

### 15.1 NavMesh 寻路

| 内容 | 说明 |
| --- | --- |
| 包 | `com.unity.ai.navigation@1.1.5`（AI Navigation，MCP 安装） |
| `Level/NavMeshRuntimeBake.cs` | 场景是代码生成的，NavMesh 数据没做子资源预存；改为进场景时烘焙一次（40×40 箱庭约几十毫秒），并**排除 Player / Guard 层**避免把角色烘成障碍 |
| `GuardAI` | 有 `NavMeshAgent` 时走寻路（`SetDestination` + `remainingDistance`），没有则回退到原来的直线移动；`SetDormant` 会 `isStopped + ResetPath` |
| 层 | 新增 `Guard`（10）；Player=8、Veil=9 |

**验证（Play 模式）**

| 项 | 结果 |
| --- | --- |
| 烘焙 | `NavMesh.CalculateTriangulation()` 366 个顶点；`NavMeshSurface` 在 `ENV_TownBlock` 上 |
| 守卫 | `NavMeshAgent` `isOnNavMesh=true`，layer 10，巡逻状态正常推进 |
| 绕障 | 让守卫从 (−6, −14) 去 (−2, 6)，路径自动绕开矮墙，**9 个拐点**（x 先绕到 −8.5 再过墙端） |
| 回归 | 重新启用 `GuardAI` 后巡逻继续推进，`state=Patrol` |

**已知限制**：NavMesh 只在进场景时烘一次，**推箱移动后导航网格不会更新**。M2 后续给货箱挂 `NavMeshObstacle`（carve）或改局部重烘。

### 15.2 第二种敌人：执灯修士

| 内容 | 说明 |
| --- | --- |
| `Detection/LanternMonk.cs` | 自带一盏**吹不灭**的点光（基础 5 m / 1.4 强度）；每 8 秒「举灯巡照」2.2 秒（半径 → 8.5 m、强度 → 2.4），带呼吸抖动；被吸血后灯直接熄灭 |
| `NightbornBootstrap.MakeGuard(...)` | 把敌人生成抽成工厂方法：机身 + 视线锥 + `GuardAI` + `NavMeshAgent` + `FeedTarget`（可选提灯），场景里现在两个敌人共用同一套代码 |
| `NPC_Monk_1` | 北侧走廊巡逻（(2,2)↔(−2,2)↔(−2,10)↔(2,10)），视锥 45° / 9 m、移速 1.3 —— 比普通守卫窄而慢，但**灯照范围大**，把"北边那条路"变成高危区 |
| `NPC_Guard_1` | 南侧开阔带，65° / 11 m、移速 1.6（原守卫） |

**玩法含义**：街灯可以吹灭，修士的灯吹不灭 —— 想去北门，只能等巡照间隙穿过去，或者推箱遮出一条暗路。

**验证**

| 项 | 结果 |
| --- | --- |
| 修士灯 | `enabled=true`，巡照峰值实测 `range 8.44 / intensity 2.38`，间隙回落到 `5.00 / 1.36` |
| 站灯下 | 玩家外光 **2.63** → `IsBurning=true`（会把玩家烧死） |
| 吸血 | 修士 `IsDormant=true`，提灯 `enabled=false`、锥灯 `enabled=false` |
| 死亡回滚 | 补上了「血储归零 → 回检查点」：血量 2 → 烧死 → 自动回 CP0，`IsDead=false`、血量恢复 |

**顺手修掉**：截图时发现血储归零只是冻结移动、没有关卡循环 —— `LevelFlow` 现在订阅 `BloodSystem.Died`，走与被抓相同的回滚流程。

### 15.3 自动化流程自检（`WalkthroughTest`）

`Runtime/Debug/WalkthroughTest.cs`：一个协程式集成测试，进关卡后挂到任意物体上就会把整条主循环跑一遍，Console 输出 ✅/❌。

为了能脚本调用，三个能力补了公开入口（UI 以后也要用）：`PlayerFeeder.TryFeed(FeedTarget)`、`BloodSight.Activate()`、`ShiftForm.Toggle()`。

**最近一次结果：10 通过 / 0 失败**

| # | 项目 | 实测 |
| --- | --- | --- |
| 1 | 场景接线 | LevelFlow / HUD / 2 个敌人都存在 |
| 2 | 检查点 | 复活点记录为 (0, 1.2, −16) |
| 3 | 吹灭街灯 | 外光 1.91 → 1.03（残量是修士的灯） |
| 4 | 推箱 | 位移 0.03 m（无输入只有最小推力，实操按 W 更远） |
| 5 | 吸血 | 血 53 → 83，目标昼睡 |
| 6 | 血视 | 生效，冷却 25 s |
| 7 | 影身穿薄幕 | 穿过后 z = 14.70（幕在 z = 14） |
| 8 | 被发现→回检查点 | 回程距离 0.12 m，血 83 → 100，修士状态复位为 Patrol |
| 9 | 拾取遗物 | 1/1 且写入存档 |
| 10 | 抵达出口 | `IsComplete=true` |

**注意**：测试脚本早期版本有 3 个**测试自身的坑**（移动错物体、把玩家传到箱顶、让修士在巡逻中转身），排查过程已记录，不要误判成玩法 bug。

### 15.4 感知手感调参（按自检结论）

自检发现"守卫转身时检测进度掉得比涨得快，看起来像瞎了"。三处改动：

| 参数 | 旧 | 新 | 原因 |
| --- | --- | --- | --- |
| `turnSpeed` | 150 | **90** | 守卫转身慢一点，视野不会一秒扫过 |
| `decayPerSecond` | 0.6 | **0.25** | 掉得太快，玩家一躲就清零，等于没有"可疑"状态 |
| 丢失视野后的宽限期 | 无 | **`lostGrace` 内不掉进度** | 守卫转头瞬间不该清零，否则永远攒不满 |
| 修士锥角 | 45 | **60** | 45° 太窄，加上巡照转身几乎抓不到人 |

调完重跑流程自检 **10/10 全绿**。

---

## 16. 正式 HUD（2026-11-11）

`Runtime/UI/NightbornHud.cs`：按 §12 决策 21 的"混合形态"，全部用代码建（无 prefab / 无贴图资源，径向渐变与圆点都是运行时生成的 `Texture2D`）。

| 元素 | 表现 |
| --- | --- |
| 血储 | 屏幕边缘**红晕**（`1 − 血量` 映射到透明度），血量 < 35% 叠加**心跳脉动**；不显示数字 |
| 检测 | **敌人头顶圆点**：白 → 红按确认进度插值，尺寸随进度变大；敌人昼睡时变灰；60% 以上再给屏幕叠一层淡红 |
| 能力 | 底部中央三格：**化形 Tab / 血视 Q / 药剂 x1**，冷却时变暗并显示秒数，血视开启时整格变红，药剂生效时变紫 |
| 遗物 | 右上角 `血之遗物 n / m`，拿到后变烛黄色 |
| 状态 | 吸血时中央显示"吸血中…"；通关显示"通关 — Esc 返回菜单" |

调试浮层（F1）改为**默认收起**，正式 HUD 常驻；菜单场景不挂任何 HUD。

**验证**：低血量（26%）+ 敌人在视野内时截图，红晕、能力槽、遗物计数、敌人标记均正常；调参后流程自检 10/10。

---

## 17. 音频（2026-11-11）

工程里没有任何音频素材，为了不背版权风险，先用**运行时合成**做占位（`Runtime/Audio/SynthAudio.cs`），之后换成正式音效包时只改 clip 引用。

| 文件 | 说明 |
| --- | --- |
| `Audio/SynthAudio.cs` | 6 个合成 clip：脚步（噪声脉冲 + 低通）、吹灯（嘶声）、心跳（52/104 Hz 砰）、警觉（620→1180 Hz 上滑）、被抓（低频 + 噪声）、通关（523/784 Hz 和弦）、环境 drone（6 s 无缝循环，首尾交叉淡化） |
| `Audio/NightbornAudio.cs` | 4 条音源：`AUDIO_Ambient`（循环 drone）、`AUDIO_Sfx`（事件音）、`AUDIO_Step`（脚步，带 pitch 抖动）、`AUDIO_Heart`（低血心跳） |

**接线的事件**：`BloodSystem.Died`、`DetectionMeter.StateChanged`（可疑/围猎各一档）、`ShiftForm.FormChanged`、`LevelFlow.CaughtEvent` / `LevelCompleted`、`DouseableLamp.DousedEvent`、`Relic.CollectedAny`（后两个是本轮新增的静态事件）。

**规则**：脚步间隔 0.52 s（潜行 1.05 s 且音量减半）；血量 < 35% 心跳从 0.95 s 加速到 0.42 s；吸血过程中有一下心跳。

**验证**：4 条音源都在、`AUDIO_Ambient` 正在循环 6 s drone；切换化形后 `AUDIO_Sfx.isPlaying=true`（事件链路通）；流程自检回到 **10/10**。

**注意**：自检曾出现 8/10，原因是**我手动试玩污染了状态**（灯已被吹灭、化形还在冷却）。测试现在对这两种情况免疫（灯已灭则标注跳过、化形前清冷却）。

---

## 18. M3 内容：教堂场景 + 夜巡犬（2026-11-11）

### 18.1 场景 `L_Ch02_S01_Church`

40×40 室内教堂（构建列表第 3 位，菜单第 2 项）：

| 内容 | 说明 |
| --- | --- |
| 结构 | 四面外墙（南北各留门洞）+ 两列各 5 根柱子（中殿/侧廊）+ 6 条长椅 + 抬高的祭坛 |
| 光 | 2 盏**可吹灭**烛台（`MakeCandle` 助手）+ 月光（62°, −25°）；整体比街区更暗 |
| 捷径 | 祭坛两侧各一道薄幕（Veil 层），影身可穿 |
| 目标 | 出口在北门；遗物在西南侧廊（`relic_ch02_s01`） |
| 关卡 | 2 个检查点；`LevelFlow` levelId = `Ch02_S01`、index = 1 |

### 18.2 第三种敌人：夜巡犬 `Bloodhound`

| 参数 | 值 | 意图 |
| --- | --- | --- |
| 视锥 | 35° / 7 m | 正面很难被它"看见" |
| 移速 / 追击 | 2.4 / **4.6** | 追起来比守卫更难甩 |
| 气味 | 10 m 内、玩家**在移动**时每秒 +0.55 进度 | 约 1.8 s 到满 |
| 静止 | 速度 < 0.4 m/s 时气味不生效 | "站住就不被闻到"是玩家可用的应对 |

**关键实现**：气味用**位移差值**算速度（`位移 / dt`），而不是读 `PlayerMotor.CurrentSpeed` —— 后者只反映输入，被推、掉落、传送都读不到。影身躲得过眼睛，**躲不过气味**（设计上的对位）。

### 18.3 顺手做的重构

`CreatePlayer(spawn, bodyMaterial, levelId, levelIndex)` —— 之前玩家/相机/血储/检测/化形/交互/HUD 依赖/关卡流程/药剂全写死在街区生成器里，现在抽成多场景共用；否则第二张场景要复制 200 行。

### 18.4 验证

| 项 | 结果 |
| --- | --- |
| 场景生成 | `L_Ch02_S01_Church.unity` 149 KB；构建列表 = 菜单 / 街区 / 教堂 |
| NavMesh | 598 个三角化顶点（教堂柱廊自动成为障碍） |
| 猎犬 | `Bloodhound` 组件在位；`chaseSpeed=4.6`、`viewAngle=35`、`viewDistance=7` |
| 关卡绑定 | `LevelFlow` id=`Ch02_S01`、index=1；2 检查点 / 2 烛台 / 1 遗物 |
| 检测管线 | `AddDetection(0.45)` → 进度 0.45（气味最终走的就是这条路） |

**已知限制**：气味需要**真人持续走动**才能完整验证 —— 脚本环境没法持续产生输入，所以只验到"位移速度算法 + 检测管线"两段。

---

## 19. 关卡拆分：每章 3 关（2026-11-11）

按 §12 决策 1（9 关 = 3 章 × 3 关），先把已完成的两张场景各拆成 3 关。**同一套几何 + `variant` 参数**，不为每关重画地图。

### 19.1 变体表

| 关卡 | id / 索引 | 敌人 | 货箱 | 光照 | 设计意图 |
| --- | --- | --- | --- | --- | --- |
| 第一章 第一夜 | `Ch01_S01` / 0 | 守卫 ×1 | 5 | 街灯 ×1 | 教学：躲光、熄灯、出口 |
| 第一章 第二夜 | `Ch01_S02` / 1 | 守卫 + 执灯修士 | 5 | 街灯 + 烛台 | 引入"吹不灭的灯" |
| 第一章 第三夜 | `Ch01_S03` / 2 | 守卫 + 执灯修士 | **3** | 街灯**挪到必经之路** (0,8) + 烛台 | 掩体变少、路被灯堵死 |
| 第二章 第一夜 | `Ch02_S01` / 3 | 夜巡犬 ×1 | — | 烛台 ×2 | 引入气味追踪 |
| 第二章 第二夜 | `Ch02_S02` / 4 | 夜巡犬 **×2**（第二只巡侧廊） | — | 烛台 ×2 | 双犬夹击 |
| 第二章 第三夜 | `Ch02_S03` / 5 | 夜巡犬 ×2 + **执灯修士** | — | 烛台 **×1** | 鼻 + 眼 + 灯三重压力 |

### 19.2 菜单与推进

- 构建列表：`L_Main` + 6 个关卡场景。
- 解锁规则改为**线性推进**：第 1 关常开，之后每关要求**上一关已通关**（`levelIds[i-1]`）。
- 修了一个真 bug：菜单原来用**场景名**（`L_Ch01_S01_Street`）去查存档，而存档键是**关卡 id**（`Ch01_S01`），导致通关后下一关永远不解锁。现在菜单带 `levelIds` 数组，一一对应。

### 19.3 验证

| 项 | 结果 |
| --- | --- |
| 场景生成 | 6 个关卡场景 + 菜单，全部进构建列表 |
| 变体差异 | S01：45 对象 / 5 箱 / 无修士；S02：57 对象 / 有修士 / 街灯+烛台；S03：55 对象 / **3 箱** / 街灯 (0,0,8) |
| 教堂变体 | S01 猎犬×1；S02 猎犬×2；S03 猎犬×2 + 修士 + 烛台仅 1 |
| 菜单 | 6 个按钮，**2 个可点**（S01 已通关 → S02 解锁，其余锁定）✅ |
| 关卡绑定 | S03 `LevelFlow` id=`Ch01_S03`、index=2；NavMesh 326 顶点 |
| 回归 | 在 S02 上跑 `WalkthroughTest`：**10 / 10** |

---

**交接人**：__________  **接手人**：__________  **日期**：__________
