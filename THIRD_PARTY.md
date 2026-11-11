# THIRD_PARTY / 参考实现清单

> 规则见 `HANDOVER.md` **§5.1**：不熟悉的功能先找现成实现，或用 REA 逆向同类游戏，别自己造。
> 本文件记录**每一条外部来源**：用了什么、许可如何、借鉴了什么、有没有采用。

---

## 〇、2026-11-12 两轮的结论（无新增外部代码）

| 做了什么 | 结论 | 为什么不用现成方案 |
| --- | --- | --- |
| 设置菜单（音量/灵敏度/画质/分辨率 + JSON 持久化） | **自写**（`Settings/NightbornSettings` + `UI/SettingsPanel`） | 市面 Unity 设置框架都绑自己的存档格式与 prefab UI；本工程 UI 全代码生成、美术资产为 0，接进来等于改半个工程 |
| 夜巡犬「气味遮挡」（`Bloodhound`） | **自写**，复用工程内 `GuardAI` 已有的遮挡射线语义 | 只加一条 `RaycastNonAlloc` + 0.2 s 节流，没有值得引入的库 |
| 移速惩罚模型（硬锁 / 软惩罚下限） | **自写** | 潜行游戏的「减速 vs 定身」是设计决策，不对应可复用组件 |
| 菜单键盘导航、Esc 优先级、模态遮罩 | **自写** | 依赖现有 `NightbornMenu` 的代码建 UI 结构，抽不出通用件 |

> 本轮唯一「参考过并否掉」的仍是上表的 **HUD Indicator v2（要求 Unity 6）** 与 **off-screen-indicator（GPL-3.0）**，结论不变。


---

## 一、已引入工程

| 名称 | 用途 | 许可 | 状态 |
| --- | --- | --- | --- |
| Unity MCP for Unity (`com.coplaydev.unity-mcp` 10.3.0) | 编辑器自动化（场景接线、真机验证、截图） | 见包内 LICENSE | 已在 `Packages/manifest.json` |
| AI Navigation (`com.unity.ai.navigation` 1.1.5) | 守卫寻路的 NavMeshSurface / NavMeshAgent | Unity Companion License | 2026-11-11 安装（M2） |
| `com.unity.feature.development` 1.0.1 | Unity 官方「开发」特性包，会拉 Test Framework / Code Coverage / IDE 集成 | Unity Companion License | 已在 manifest（**此前漏记，2026-11-12 补**） |
| Input System / URP / TextMeshPro / Timeline / VisualScripting | 官方包 | Unity Companion / UPM 条款 | 已在 manifest |

---

## 二、仅供设计参考（未引入代码或资产）

| 名称 | 链接 | 许可 | 可借鉴的点 | 是否采用 |
| --- | --- | --- | --- | --- |
| Unity Stealth Game（HenrySpartGlobal） | https://github.com/HenrySpartGlobal/Unity_Stealth_Game | 仓库未标注（待核，只读参考） | 守卫用 **Spotlight 当视线锥的可视化**；真实判定距离**短于**光斑，避免玩家误判；参数外露（转身速度、移速、停留时间、警觉计时）；障碍物同时遮挡视线与光斑 | 采用"光斑=视线锥可视化 + 真实判定略短"的思路 |
| Save System for Unity（IntoTheDev） | https://github.com/IntoTheDev/Save-System-for-Unity | DataSerializer **MIT**；OdinSerializer **Apache-2.0** | `DataSerializer.Save/Load` 的 PlayerPrefs 式 API、多档位（profile）、加密存档文件、IL2CPP/AOT 可用 | **M0 第 7 步候选**：先用它顶上，省掉自写序列化 |
| FS Stealth System 2（付费 Asset Store） | https://discussions.unity.com/t/released-fs-stealth-system-2-guard-ai-with-vision-hearing-and-touch-awareness-meter-hiding-spots-body-carry/1737131 | 商业授权 | 守卫的 **Awareness Meter（警觉条）分档**、调查行为、藏身点、尸体搬运 | 只作行为分档参考，不购买、不引入 |
| Stencil see-through（Divinity 式穿墙显形） | https://discussions.unity.com/t/stencil-see-through-effect/782486 | 论坛讨论 | Stencil + RenderObjects 做"遮挡物半透明/剪影" | **血视**的候选实现路线 |
| Show Objects Through Walls with URP Render Objects | https://www.youtube.com/watch?v=PM4MVgP1f0w | 教学视频 | URP Render Objects + 自定义 Shader 显示墙后物件 | 血视的备选方案 |
| How to Build Realistic AI Perception and Memory in Unity | https://www.youtube.com/watch?v=IspRyEDPCgM | 教学视频 | 按 Game AI Pro 的 "Target Tracks" 做**感知 + 记忆**（不完美信息） | 巡逻/围猎记忆的参考 |
| HUD Indicator v2（Hexloot-Games / lincolncpp） | https://github.com/Hexloot-Games/hud-Indicator | **MIT** | 屏幕内 icon / 出屏贴边箭头 / 距离标签一体的目标指示器；世界坐标→屏幕坐标→夹到内缩矩形边缘的算法 | **只借算法**。v2 要求 Unity 6（本工程 2022.3），且靠 prefab + 图标资源驱动；本项目场景与 HUD 全代码生成、美术资产为 0，引入反而增加 prefab 依赖 → 按 `HANDOVER.md` **§5.1**「耦合过深」自己写了一个 40 行的 `ObjectiveHud` |
| off-screen-indicator（jinincarnate） | https://github.com/jinincarnate/off-screen-indicator | **GPL-3.0** | 同上（箭头/方框指示器 + Target 组件） | **未采用**：GPL-3.0 与本工程商业发行不兼容 |

---

## 三、REA（Reverse Engineer Anything）用法

本机：`rea-agents` MCP（`dsh-rea` 插件，170 个 `mcp__rea__*` 工具）。

- 可以：反编译原生二进制、APK（类/方法 → Java）、.NET 程序集、JS/Electron 应用，提取资源与配置。
- 只用于**理解机制、参数、状态机结构**；不复制资产、不搬运受版权保护的代码。
- 逆向结论（阈值、状态转移、AI 参数）写进 `HANDOVER.md` 对应小节，并注明来源作品。

### 待逆向清单（M0/M1 期间按需执行）

| 想弄清楚的问题 | 参考对象 | 期望产出 |
| --- | --- | --- |
| 守卫"确认速度分档"的常用数值 | 同类潜行游戏（如 Styx / Aragami 的公开实现与拆包数据） | 光下/暗处/边缘三档的秒数区间 |
| 影身穿墙的视觉表现（残影、透视） | 有相位类技能的游戏 | 表现层参数 |
| 检查点触发与回滚的粒度 | 小体量关卡制游戏 | 存档字段清单 |

---

## 四、许可证待办

- [ ] 引入任何 GitHub 代码前，确认 LICENSE 类型并抄进本文件
- [ ] 免费资产包（美术/音频）逐项记录来源与许可，上架前核查是否允许商用
- [ ] 交付前跑一遍"来源清单 vs 工程实际引用"的核对
