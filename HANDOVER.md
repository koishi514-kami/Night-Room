# 《夜裔 · 血月临城》工程交接文档

> Unity 3D 哥特潜行解谜 · 项目代号 **Nightborn** · 交接日期 2026-11-06
> 当前版本：git `997f736`（第 6 次提交）· 工程路径 `D:\Unity\Nightborn`

---

## 0. 一句话交接

工程已经把"能跑起来的底座"做完了：URP 渲染管线、开场街区灰盒、可走可潜行的玩家、跟随提灯、后处理与调试浮层全部就位并通过编译验证；**核心玩法（光照即地形 / 血储 / 暴露度 / 巡逻 AI）尚未开始写**，接手者从这里继续往上垒。

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
- 当前 6 次提交，73 个受控文件：

| 提交 | 说明 |
| --- | --- |
| `f25eb48` | 初始化工程、URP 14 基础设施、开场街区灰盒 |
| `5bca5b6` | README：环境、键位、光照基线 |
| `9ea9c51` | 相机穿墙拉近、输入兜底、F1 浮层、Play 场景守卫；删除模板 SampleScene |
| `c825adc` | 空场景自动打开街区、场景连线检查菜单 |
| `48350b6` | 修复 ThirdPersonCamera 的四元数取负编译错误 |
| `997f736` | 修复后处理：VolumeProfile 子资源落盘 + 渲染器挂 PostProcessData |

- `.gitignore` 已排除 `Library/ Temp/ Logs/ obj/ Build/ _Preview/ .vs/ *.csproj *.sln`。
- 换行符 LF→CRLF 的警告是 Windows 正常现象，忽略即可。

### 2.2 目录约定

```
Assets/Nightborn/
├─ Scenes/      L_Ch01_S01_Street.unity      唯一场景（由脚本生成，见 §6 陷阱 1）
├─ Scripts/
│  ├─ Runtime/Player/   PlayerMotor.cs, LanternFollow.cs
│  ├─ Runtime/Camera/   ThirdPersonCamera.cs
│  ├─ Runtime/Debug/    NightbornDebugHud.cs, NightbornRuntimeBoot.cs
│  └─ Editor/           NightbornBootstrap.cs, NightbornEditorGuard.cs
├─ Materials/   M_InkStone / M_BloodBrick / M_CandleGold / M_IronGrey / M_MistTeal
└─ Settings/    Nightborn_URP.asset, Nightborn_Renderer.asset, VP_Global.asset
```

命名前缀：场景 `L_`、材质 `M_`、预制体 `P_`、Volume `VP_`、几何 `GEO_`、建筑 `BLD_`、灯 `LAMP_`、道具 `PROP_`、玩家 `PLAYER_`、相机 `CAM_`。

---

## 3. 代码清单与职责

| 文件 | 行数 | 职责 | 关键点 |
| --- | --- | --- | --- |
| `Scripts/Runtime/Player/PlayerMotor.cs` | 90 | CharacterController 驱动移动 | walk 3.2 / sneak 1.3 / accel 14 / gravity −22；相对 `CAM_Rig` 求方向并 Slerp 转身；掉出 y<−20 回出生点；对外暴露 `IsSneaking`、`CurrentSpeed`、`RawInput` |
| `Scripts/Runtime/Camera/ThirdPersonCamera.cs` | 97 | 第三人称跟随 | 鼠标 Δ 转 yaw/pitch；`Physics.SphereCast`（半径 0.28）遇墙把相机拉到 `max(minDistance*0.6, hit.distance-0.05)`；target 为空按 `PLAYER_Ada` / tag Player 自愈 |
| `Scripts/Runtime/Player/LanternFollow.cs` | 44 | 提灯跟随 + 烛光抖动 | `Vector3.SmoothDamp` 0.12 s；offset (0.35, 1.25, 0.28)；Perlin 抖动 1.6±0.12、频率 7.5；**shadows 显式 None** |
| `Scripts/Runtime/Debug/NightbornDebugHud.cs` | 80 | F1 自检浮层 | 显示 Unity 版本、fps、场景名、玩家坐标/输入向量/速度、相机位置与 clip、Input System 状态、最近报错 |
| `Scripts/Runtime/Debug/NightbornRuntimeBoot.cs` | 17 | 运行时自动挂 HUD | `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` |
| `Scripts/Editor/NightbornBootstrap.cs` | ~404 | **场景生成器**（菜单 Nightborn → Build Base Scene） | 重建渲染器/管线/材质/Volume/街区场景并渲染预览图 |
| `Scripts/Editor/NightbornEditorGuard.cs` | 99 | 编辑器守卫 | 钉死 `playModeStartScene`；空场景自动切街区；菜单「打开开场街区场景」「检查场景连线」 |

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

### 5.2 未完成（按设计文档的优先级）

| 优先级 | 内容 | 验收标准 |
| --- | --- | --- |
| P0 | **提灯实时阴影 +「光照即地形」判定** | 提灯 `LANTERN_Light` 开 Soft 阴影；采样光照强度 > 0.65 时对玩家施加"灼伤"（掉血/硬直），远处暗处安全 |
| P0 | 血储系统 | 血储 100，夜行 −6/min，吸血 +30，血视 5/次，灯芯 +25 |
| P1 | 暴露度 0–100 | 光照 +12/s、阴影 −6/s，>60 触发巡逻，满 100 触发"围猎" |
| P1 | 巡逻 AI | NavMesh 巡逻 + 视锥检测 + 追击状态机（可用 `unity-navmesh` 技能） |
| P2 | 关卡 ×3 章 ×9 关、教程引导、存档 | 单关 3–4 分钟，全流程约 36 分钟 |
| P2 | UI / 音频 / 打包 | HUD（血储、暴露条）、Volume 混音、Windows 64 位包 ≤ 300 MB |

---

## 6. 陷阱与坑（接手先读这段）

1. **`NightbornBootstrap.BuildAll` 是破坏性的**：它会 `DeleteAsset` 并重建渲染器、管线、5 个材质、VP_Global 和整个场景。**在编辑器里手工改场景后再跑它 = 全部丢失**。要改场景请改脚本，或另存新场景并把新逻辑放进 `Scripts/Runtime`。
2. **后处理曾经整份失效**（`997f736` 已修）：`VolumeProfile.Add<T>()` 只把组件加进内存，必须 `AssetDatabase.AddObjectToAsset(component, profile)` 才会落盘，否则序列化成 `fileID: 0`。同时 `UniversalRendererData.postProcessData` 必须指向 URP 的 PostProcessData.asset，否则 Volume 完全不起作用。
3. **提灯与街灯目前全部 `shadows: None`**：这是当初为了性能做的取舍，做"光照即地形"时必须把提灯的阴影打开（点光阴影在 Forward+ 下可用，注意 2048 贴图开销）。
4. **编辑器开着时，外部工具写工程内文件会失败**：报 `ReplaceFileW EIO (Win32 32)`。要走命令行改文件时先关 Unity，或用 `[IO.File]::Open(..., Truncate, Write, ReadWrite)`。
5. **Console 里的红色 `error CS` 可能是历史记录**：判断编译是否成功，看 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 里最后一条 `*** Tundra build success` 和 `Library/ScriptAssemblies/Assembly-CSharp.dll` 的时间戳。
6. **`D:\Unity\Nightborn_compiletest`** 是早期做编译验证留下的副本，被僵尸 Unity 进程占着，重启后可整目录删除，与主工程无关。
7. **输入全部走 Input System**（`Keyboard.current` / `Mouse.current`）：若浮层显示"Keyboard.current 为 null"，去 Project Settings → Player → Active Input Handling 确认是 Both / Input System Package。
8. **不要删除 `NightbornEditorGuard.cs`**：它负责把 Play 默认场景钉死；删掉后可能出现"点 Play 进空场景全黑"的老问题。
9. 本机无 dotnet SDK，离线编译校验要用 Unity 自带的 Roslyn（命令见 §7.2）。

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

---

## 8. 交接验收清单

- [ ] 打开工程，Console 无红字，`Tundra build success`
- [ ] 场景为 `L_Ch01_S01_Street`，Play 后 F1 浮层正常
- [ ] WASD 能动、Shift 变慢、鼠标能转视角、滚轮能拉近距离
- [ ] `_Preview/preview_street_01.png` 与编辑器画面一致（血砖峡谷 + 拱桥 + 6 盏烛黄街灯 + 暗角）
- [ ] 能独立跑通 §7.1 的重建命令

---

## 9. 变更记录

| 日期 | 内容 | 提交 |
| --- | --- | --- |
| 2026-11-01 | 工程初始化、URP、街区灰盒、README | `f25eb48` `5bca5b6` |
| 2026-11-01 | 相机穿墙、输入兜底、F1 浮层、Play 场景守卫 | `9ea9c51` `c825adc` |
| 2026-11-01 | 修复四元数取负编译错误 | `48350b6` |
| 2026-11-06 | 修复后处理整份失效（子资源 + PostProcessData） | `997f736` |
| 2026-11-06 | 本文档 | — |

---

**交接人**：__________  **接手人**：__________  **日期**：__________
