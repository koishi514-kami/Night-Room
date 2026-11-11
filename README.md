# Nightborn《夜裔·血月临城》

哥特风 3D 吸血鬼潜行解谜。当前进度：**第 1 阶段 — 基础场景（灰盒 + 可走玩家 + 提灯跟随）**。

## 环境

| 项 | 值 |
| --- | --- |
| Unity | 2022.3.34f1c1 |
| 渲染管线 | URP 14.0.6（Forward+，HDR，MSAA 4x，renderScale 1） |
| 输入 | Input System 1.7.0（activeInputHandler = Both） |
| 目标平台 | Windows / 1080p / 60fps |

打开方式：Unity Hub → Add project from disk → `D:\Unity\Nightborn` → 打开 `Assets/Nightborn/Scenes/L_Ch01_S01_Street.unity` → Play。

## 操作

| 按键 | 行为 |
| --- | --- |
| W A S D | 以相机朝向为基准移动（walk 3.2 m/s） |
| Shift | 潜行（1.3 m/s） |
| 鼠标移动 | 转动第三人称相机（pitch 限 -25°~55°） |
| 滚轮 | 相机距离 2.0–6.0 m |
| **Tab** | **化形**：切影身（熄自体光、可穿薄幕，6 秒 / 冷却 8 秒） |
| **E** | **上下文交互**：吹灭街灯、拾取血之遗物 |
| F1 | 调试浮层 |

提灯挂在玩家身上，由 `LanternFollow` 以 0.12 s 阻尼跟随，光强在 1.6 基础上做 Perlin 抖动（±0.12），模拟烛火。**自 P0-1 起提灯投实时软阴影**（512 贴图，由 `LanternFollow.castShadows` 控制，改 Inspector 的 Light 组件无效）。

按 **F1** 打开自检浮层，其中「光照 / 灼伤 / 移速倍率」一行即 P0-1 的光照即地形判定：站在街灯 4.8 m 内会被灼伤（移速 ×0.45），躲到货箱或建筑后面立刻解除。

## 目录约定

```
Assets/Nightborn/
  Scenes/     L_<章节>_<关卡>_<场景>.unity
  Scripts/    Runtime/{Player,Camera,...}、Editor/
  Materials/  M_<名称>.mat
  Settings/   Nightborn_URP.asset、Nightborn_Renderer.asset、VP_Global.asset
  Prefabs/    P_<名称>.prefab
  Art/Graybox/
```

## 美术与光照基线（全关卡共用）

五色限定：Ink `#0E0F12`、Blood `#8B1E28`、Candle `#E0A44B`、Iron `#4A4F57`、Mist `#7A8A8F`。

- 月光：冷色 `(0.55, 0.68, 0.85)`，强度 0.8，Soft 阴影 2048，角度 `Euler(55, -30, 0)`
- 环境光：Flat `(0.15, 0.16, 0.20)`；指数平方雾 density 0.012，色 Mist
- 街灯：点光 2.2 强度 / 9 m / 不投阴影，灯球自发光 Candle
- 后处理 VP_Global：ACES Tonemapping、Bloom(0.6/0.9/0.7)、Vignette(0.35/0.4)、ColorAdjustments(postExposure -0.2, saturation -15, contrast 8)

## 场景内容（L_Ch01_S01_Street）

90×60 石板路面 + 两侧人行道；街两侧各 5 栋血砖楼（高 9/10/12/13/15，带铁灰腰线）；跨街拱桥（柱 ±8.5，梁 y=11）；6 盏街灯、6 个货箱；北端门洞为目标出口。玩家 `PLAYER_Ada` 用 CharacterController（高 1.8 / 半径 0.34）驱动。

## 重建场景

改完 `Scripts/Editor/NightbornBootstrap.cs` 后，菜单 **Nightborn → Build Base Scene** 可整场景重建（幂等，重建会覆盖 `L_Ch01_S01_Street.unity`）。命令行：

```
Unity.exe -batchmode -projectPath D:\Unity\Nightborn ^
  -executeMethod Nightborn.EditorTools.NightbornBootstrap.BuildAll ^
  -logFile build.log
```

## 下一步

1. ~~提灯实时阴影 + "光照即地形"~~ ✅（已按新设计改为**自体光**，见 `SelfGlow`）
2. ~~血储 / 暴露度~~ ✅（血储=生命值；暴露度改为**检测度**，由守卫视线驱动）
3. ~~Tab 化形~~ ✅（影身=灭光+穿薄幕，6 s / 冷却 8 s）
4. 第 1 关 5 分钟闭环：吸血交互、血视、推箱与压光药剂、选关 UI（详见 `HANDOVER.md` §13.4）
