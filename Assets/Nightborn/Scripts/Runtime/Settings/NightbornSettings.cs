using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nightborn.Settings
{
    /// <summary>
    /// 设置的持久化结构。字段名就是 JSON 键名（JsonUtility），改名等于改存档格式 —— 要一起改 Migrate。
    ///
    /// 初值 == 「第一次进游戏时的默认值」：音量/灵敏度是设计值，画质与分辨率是占位值，
    /// 真正「默认取当前」的逻辑在 <see cref="NightbornSettings.Load"/> 的 Sanitize 里
    /// （`quality &lt; 0` / 宽高非法 → 取当前 QualitySettings / Screen）。
    /// </summary>
    [Serializable]
    public class NightbornSettingsData
    {
        public int version = NightbornSettings.FormatVersion;

        // 音量：0–1，最终会再乘上 NightbornAudio 里各自的固定增益
        public float master = NightbornSettings.DefaultMaster;
        public float ambient = NightbornSettings.DefaultAmbient;
        public float sfx = NightbornSettings.DefaultSfx;

        // 相机：度/像素。0.16 = 一次满屏横划约 300°（DEVLOG §25.5）
        public float sensitivity = NightbornSettings.DefaultSensitivity;
        public bool invertY = false;

        // -1 = 还没定过，读取时取当前档位（见 Sanitize）
        public int quality = -1;

        public bool fullscreen = true;
        public int width = 1920;
        public int height = 1080;
    }

    /// <summary>
    /// 设置总控：一个独立的 JSON 文件 + 即时生效 + 原子写。
    ///
    /// 为什么单独一个文件（而不是塞进 nightborn_save.json）：
    /// 菜单「新的夜晚 → 清空并重来」会 `SaveSystem.Delete()` 把存档连同 .bak 一起删掉，
    /// 玩家调好的音量/画质不该跟着进度一起消失。
    ///
    /// 文件：`%USERPROFILE%\AppData\LocalLow\NightbornStudio\Nightborn\nightborn_settings.json`
    /// （= `Application.persistentDataPath` 下的 `nightborn_settings.json`）
    ///
    /// 设计要点：
    ///  1. **不抛异常打断启动**：读失败 / 手改坏 / 字段非法 → 全部回退默认值 + Console 警告，游戏照常进；
    ///  2. **原子写**：`.tmp` → `File.Replace`（旧主档自动进 `.bak`），中途断电不会留下半截 JSON；
    ///     网络盘 / FAT 上 `File.Replace` 可能不支持，退化成「删旧 + 改名」（同 SaveSystem 的降级路径）；
    ///  3. **默认值优先级的锚点**：`HasSensitivity` 只在**文件里真的写过 sensitivity 键**时为 true，
    ///     `ThirdPersonCamera` 靠它实现「设置里改过的值 &gt; 场景序列化值 &gt; 默认 0.16」——
    ///     否则玩家只调过音量，也会把场景里手调过的灵敏度覆盖成 0.16；
    ///  4. **即时生效**：每个 Set* 立即改内存值 + 立即 Apply，再发事件让使用方拉新值；
    ///     音量靠 `OnVolumeChanged`（合成音源没有 Mixer，只能各自乘 master），
    ///     灵敏度靠 `OnSensitivityChanged`，画质/分辨率靠 `OnDisplayChanged`；
    ///  5. **写盘节流**：Set* 只置 `IsDirty`，真正的 `Save()` 由面板在「停止拖动 0.4 s」或关闭面板时调
    ///     （拖滑杆会把 onValueChanged 打成每帧一次，每帧一次 File.Replace 没必要）。
    ///
    /// 启动时：`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 自动 Load + Apply（第一个场景加载前），
    /// 主菜单 `NightbornMenu.Start()` 里再 Apply 一次兜底。
    /// </summary>
    public static class NightbornSettings
    {
        /// <summary>当前设置结构版本。只在**结构**变化时 +1，并在 Migrate 里补分支。</summary>
        public const int FormatVersion = 1;

        public const float DefaultMaster = 1f;
        public const float DefaultAmbient = 0.3f;
        public const float DefaultSfx = 0.63f;
        public const float DefaultSensitivity = 0.16f;

        /// <summary>灵敏度下限（度/像素）。</summary>
        public const float MinSensitivity = 0.05f;
        /// <summary>灵敏度上限（度/像素）：0.40 时大约半屏横划就转 300°，再高会晕。</summary>
        public const float MaxSensitivity = 0.40f;

        private const string FileName = "nightborn_settings.json";
        private const string SensitivityKey = "\"sensitivity\"";

        private static NightbornSettingsData _data;
        private static bool _loaded;
        private static bool _hasSensitivity;
        private static List<Vector2Int> _resolutions;

        /// <summary>音量（master / ambient / sfx）变化。使用方在这里重新乘一遍 master。</summary>
        public static event Action OnVolumeChanged;
        /// <summary>灵敏度或反转 Y 变化（ThirdPersonCamera 订阅）。</summary>
        public static event Action OnSensitivityChanged;
        /// <summary>画质 / 全屏 / 分辨率变化。</summary>
        public static event Action OnDisplayChanged;

        // ---------- 路径 ----------

        private static string Dir => Application.persistentDataPath;

        /// <summary>设置文件绝对路径（验收时直接看这个）。</summary>
        public static string SettingsPath => Path.Combine(Dir, FileName);
        private static string TempPath => SettingsPath + ".tmp";
        /// <summary>上一次写盘前的主档备份（原子写入的副产物）。</summary>
        public static string SettingsBackupPath => SettingsPath + ".bak";

        // ---------- 状态 ----------

        /// <summary>累计写盘次数（验收用）。</summary>
        public static int SaveCount { get; private set; }

        /// <summary>最近一次写盘耗时（毫秒）。</summary>
        public static float LastSaveMs { get; private set; }

        /// <summary>本次读取没有可用文件 / 文件读坏，全部走默认值。</summary>
        public static bool LoadedFromDefaults { get; private set; }

        /// <summary>设置文件里有明确的 sensitivity（决定「设置 &gt; 场景序列化值」这条优先级）。</summary>
        public static bool HasSensitivity => EnsureLoaded() && _hasSensitivity;

        /// <summary>有未写盘的改动（面板按这个节流 flush）。</summary>
        public static bool IsDirty { get; private set; }

        /// <summary>设置数据（首次访问会自动 Load）。</summary>
        public static NightbornSettingsData Data
        {
            get
            {
                EnsureLoaded();
                return _data;
            }
        }

        // ---------- 只读取值 ----------

        public static float Master => Clamp01(Data.master, DefaultMaster);
        public static float Ambient => Clamp01(Data.ambient, DefaultAmbient);
        public static float Sfx => Clamp01(Data.sfx, DefaultSfx);

        public static float Sensitivity
        {
            get
            {
                float v = Data.sensitivity;
                if (float.IsNaN(v) || float.IsInfinity(v)) return DefaultSensitivity;
                return Mathf.Clamp(v, MinSensitivity, MaxSensitivity);
            }
        }

        public static bool InvertY => Data.invertY;

        public static int QualityLevel
        {
            get
            {
                int count = QualitySettings.names.Length;
                if (count <= 0) return 0;
                return Mathf.Clamp(Data.quality, 0, count - 1);
            }
        }

        public static bool Fullscreen => Data.fullscreen;

        /// <summary>目标分辨率（不是当前屏幕分辨率）。</summary>
        public static Vector2Int Resolution => new Vector2Int(Mathf.Max(1, Data.width), Mathf.Max(1, Data.height));

        /// <summary>当前运行分辨率（`Screen.width/height` 的只读拷贝，验收对照用）。</summary>
        public static Vector2Int CurrentScreenResolution => new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));

        // ---------- 读 ----------

        private static bool EnsureLoaded()
        {
            if (!_loaded) Load();
            return _loaded;
        }

        /// <summary>
        /// 读盘。**读不出来不抛异常**：回退默认值 + 警告，游戏照常启动。
        /// 缺字段用 `FromJsonOverwrite` 保住字段初值（JsonUtility 的 FromJson 会把缺失字段清零）。
        /// </summary>
        public static void Load()
        {
            var data = new NightbornSettingsData();
            bool fileExists = false;
            bool parsed = false;
            _hasSensitivity = false;
            LoadedFromDefaults = true;

            try
            {
                string path = SettingsPath;
                fileExists = File.Exists(path);
                if (fileExists)
                {
                    string json = File.ReadAllText(path);
                    if (!string.IsNullOrEmpty(json) && json.Trim().Length > 0)
                    {
                        // FromJsonOverwrite：JSON 里没有的键保留字段初值，不会变成 0/false
                        JsonUtility.FromJsonOverwrite(json, data);
                        parsed = true;
                        _hasSensitivity = json.IndexOf(SensitivityKey, StringComparison.Ordinal) >= 0;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 设置读取失败，用默认值：" + e.Message);
            }

            if (fileExists && !parsed)
                Debug.LogWarning("[Nightborn] 设置文件为空或解析失败，已回退默认值：" + SettingsPath);

            Sanitize(data);

            _data = data;
            _loaded = true;
            IsDirty = false;
            LoadedFromDefaults = !parsed;

            // 启动时一行日志，真机验收直接从 Console 看读到的是什么（§25.1 的读数习惯）
            Debug.Log("[Nightborn] 设置" + (parsed ? "已加载" : "用默认值（没有设置文件）") + "：" +
                      "master=" + data.master.ToString("F2") +
                      " ambient=" + data.ambient.ToString("F2") +
                      " sfx=" + data.sfx.ToString("F2") +
                      " sens=" + data.sensitivity.ToString("F2") +
                      " invertY=" + data.invertY +
                      " quality=" + data.quality +
                      " fullscreen=" + data.fullscreen +
                      " res=" + data.width + "x" + data.height +
                      "　→ " + SettingsPath);
        }

        /// <summary>读取后的字段卫生 + 版本对齐：坏值就地修，别让它传染到运行期。</summary>
        private static void Sanitize(NightbornSettingsData data)
        {
            data.master = Clamp01(data.master, DefaultMaster);
            data.ambient = Clamp01(data.ambient, DefaultAmbient);
            data.sfx = Clamp01(data.sfx, DefaultSfx);

            if (float.IsNaN(data.sensitivity) || float.IsInfinity(data.sensitivity))
                data.sensitivity = DefaultSensitivity;
            data.sensitivity = Mathf.Clamp(data.sensitivity, MinSensitivity, MaxSensitivity);

            // 画质：没定过 / 越界 → 取当前档位（「默认取当前」唯一落点）
            int qualityCount = QualitySettings.names.Length;
            if (qualityCount > 0)
            {
                if (data.quality < 0 || data.quality >= qualityCount) data.quality = QualitySettings.GetQualityLevel();
                data.quality = Mathf.Clamp(data.quality, 0, qualityCount - 1);
            }
            else
            {
                data.quality = 0;
            }

            // 分辨率：非法 → 取当前（编辑器 / 无头平台上 Screen.resolutions 可能为空，不能只靠它）
            if (data.width <= 0 || data.height <= 0)
            {
                data.width = Mathf.Max(1, Screen.width);
                data.height = Mathf.Max(1, Screen.height);
            }

            // 结构兼容：只有加字段才需要抬版本，这里保留版本号但不丢数据
            data.version = FormatVersion;
        }

        // ---------- 原子写 ----------

        /// <summary>整份写回（原子）：.tmp → File.Replace → 旧主档进 .bak。</summary>
        public static void Save()
        {
            var data = Data;
            data.version = FormatVersion;

            string json;
            try
            {
                json = JsonUtility.ToJson(data, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 设置序列化失败：" + e.Message);
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(TempPath, json);

                if (File.Exists(SettingsPath))
                    File.Replace(TempPath, SettingsPath, SettingsBackupPath, true); // 旧主档自动进 .bak
                else
                    File.Move(TempPath, SettingsPath);

                SaveCount++;
                LastSaveMs = (float)watch.Elapsed.TotalMilliseconds;
                IsDirty = false;
                Debug.Log("[Nightborn] 设置已保存：" + SettingsPath +
                          "（累计 " + SaveCount + " 次，本次 " + LastSaveMs.ToString("F1") + "ms）");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 设置写入失败：" + e.Message);
                FallbackWrite(json);
            }
        }

        /// <summary>File.Replace 在网络盘 / 部分 FAT 卷上不支持：退化成「删旧 + 改名」，至少不丢设置。</summary>
        private static void FallbackWrite(string json)
        {
            try
            {
                if (!File.Exists(TempPath)) File.WriteAllText(TempPath, json);
                if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
                File.Move(TempPath, SettingsPath);
                SaveCount++;
                IsDirty = false;
                Debug.LogWarning("[Nightborn] 设置已用降级路径写入（本次没有 .bak 备份）");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 设置降级写入也失败：" + e.Message);
                TryDelete(TempPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 清理临时文件失败不能再抛：写盘主流程已经结束了
            }
        }

        // ---------- 写（每项都即时生效） ----------

        public static void SetMaster(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(Data.master, value)) return;
            Data.master = value;
            MarkDirty();
            OnVolumeChanged?.Invoke();
        }

        public static void SetAmbient(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(Data.ambient, value)) return;
            Data.ambient = value;
            MarkDirty();
            OnVolumeChanged?.Invoke();
        }

        public static void SetSfx(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(Data.sfx, value)) return;
            Data.sfx = value;
            MarkDirty();
            OnVolumeChanged?.Invoke();
        }

        /// <summary>灵敏度。写过一次之后 `HasSensitivity` = true，从此压过场景里序列化的值。</summary>
        public static void SetSensitivity(float value)
        {
            value = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            var data = Data;              // 先保证 Load 跑完（Load 会按文件内容重置 _hasSensitivity）
            _hasSensitivity = true;       // 玩家明确指定过：从此压过场景序列化值
            if (Mathf.Approximately(data.sensitivity, value)) return;
            data.sensitivity = value;
            MarkDirty();
            OnSensitivityChanged?.Invoke();
        }

        public static void SetInvertY(bool value)
        {
            if (Data.invertY == value) return;
            Data.invertY = value;
            MarkDirty();
            OnSensitivityChanged?.Invoke();
        }

        public static void SetQualityLevel(int level)
        {
            int count = QualitySettings.names.Length;
            if (count <= 0) return;
            level = Mathf.Clamp(level, 0, count - 1);
            if (Data.quality == level) return;
            Data.quality = level;
            ApplyDisplay();
            MarkDirty();
            OnDisplayChanged?.Invoke();
        }

        public static void SetFullscreen(bool value)
        {
            if (Data.fullscreen == value) return;
            Data.fullscreen = value;
            ApplyDisplay();
            MarkDirty();
            OnDisplayChanged?.Invoke();
        }

        public static void SetResolution(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (Data.width == width && Data.height == height) return;
            Data.width = width;
            Data.height = height;
            ApplyDisplay();
            MarkDirty();
            OnDisplayChanged?.Invoke();
        }

        /// <summary>「恢复默认」：音量/灵敏度回设计值，画质与分辨率回**当前**（不是回硬编码值）。</summary>
        public static void ResetToDefaults()
        {
            var data = new NightbornSettingsData();
            data.quality = QualitySettings.names.Length > 0 ? QualitySettings.GetQualityLevel() : 0;
            data.width = Mathf.Max(1, Screen.width);
            data.height = Mathf.Max(1, Screen.height);
            data.fullscreen = Screen.fullScreen;

            _data = data;
            _loaded = true;
            _hasSensitivity = true; // 默认值 0.16 也是玩家明确要求的
            MarkDirty();

            ApplyDisplay();
            OnVolumeChanged?.Invoke();
            OnSensitivityChanged?.Invoke();
            OnDisplayChanged?.Invoke();
        }

        // ---------- 生效 ----------

        /// <summary>
        /// 把设置推到引擎（画质 / 全屏 / 分辨率），并让使用方重新拉音量与灵敏度。
        /// 启动时（第一个场景加载前）自动跑一次；主菜单里再跑一次兜底。
        /// </summary>
        public static void Apply()
        {
            ApplyDisplay();
            OnVolumeChanged?.Invoke();
            OnSensitivityChanged?.Invoke();
        }

        private static void ApplyDisplay()
        {
            var data = Data;

            int count = QualitySettings.names.Length;
            if (count > 0)
            {
                int level = Mathf.Clamp(data.quality, 0, count - 1);
                if (level != QualitySettings.GetQualityLevel())
                    QualitySettings.SetQualityLevel(level, true); // applyExpensiveChanges
            }

            // 只在和当前不一致时才调：编辑器 / 无头平台上 SetResolution 基本是空操作，
            // 但每帧调会刷日志；也不该在玩家没改过的时候动窗口。
            // 编辑器里 `Screen.SetResolution` 未必生效，但会把 Game View 顶成全屏、影响截图与验收，
            // 所以编辑器里只认「分辨率真的变了」这一条；Standalone 下全屏标志照常下发。
            bool fullscreen = data.fullscreen;
            bool resolutionChanged = data.width != Screen.width || data.height != Screen.height;
            bool modeChanged = !Application.isEditor && fullscreen != Screen.fullScreen;
            if (resolutionChanged || modeChanged)
            {
                try
                {
                    Screen.SetResolution(data.width, data.height, fullscreen);
                    Screen.fullScreen = fullscreen;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Nightborn] 分辨率设置失败（忽略）：" + e.Message);
                }
            }
        }

        // ---------- 分辨率候选列表 ----------

        /// <summary>
        /// `Screen.resolutions` 去重（按宽高，不按刷新率）+ 升序，并保证当前分辨率在列表里。
        /// 结果缓存：分辨率列表在一次运行里不会变，而面板每点一次都要算索引。
        /// </summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            if (_resolutions != null) return _resolutions;

            var list = new List<Vector2Int>();
            try
            {
                var raw = Screen.resolutions;
                for (int i = 0; i < raw.Length; i++)
                {
                    var candidate = new Vector2Int(Mathf.Max(1, raw[i].width), Mathf.Max(1, raw[i].height));
                    if (!list.Contains(candidate)) list.Add(candidate);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 读取分辨率列表失败，只用当前分辨率：" + e.Message);
            }

            if (list.Count == 0) list.Add(CurrentScreenResolution);

            Vector2Int current = CurrentScreenResolution;
            if (!list.Contains(current)) list.Add(current);

            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            _resolutions = list;
            return _resolutions;
        }

        /// <summary>画质档位名（`QualitySettings.names` 直接给，面板显示用）。</summary>
        public static string QualityName(int level)
        {
            var names = QualitySettings.names;
            if (names == null || names.Length == 0) return "档 " + level;
            if (level < 0 || level >= names.Length) level = Mathf.Clamp(level, 0, names.Length - 1);
            return names[level];
        }

        // ---------- 启动钩子 / 工具 ----------

        /// <summary>第一个场景加载前：读盘 + 生效（音量/灵敏度在各自 Awake 里拉，这里保证画质分辨率先到位）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBoot()
        {
            Load();
            Apply();

            // 2026-11-12（主 agent 补）：写盘节流原本只活在 SettingsPanel.Update 里，
            // 也就是「面板没开着」时改设置（脚本/调试/将来别处的入口）永远不会落盘。
            // 这里挂两条兜底：退出应用时 flush、失焦/切后台时也 flush。
            Application.quitting -= FlushIfDirty;
            Application.quitting += FlushIfDirty;
            Application.focusChanged -= OnFocusChangedFlush;
            Application.focusChanged += OnFocusChangedFlush;
        }

        private static void OnFocusChangedFlush(bool focused)
        {
            if (!focused) FlushIfDirty();
        }

        /// <summary>有脏改动才写盘（幂等，重复调用没有副作用）。</summary>
        public static void FlushIfDirty()
        {
            if (!IsDirty) return;
            Save();
        }

        private static void MarkDirty()
        {
            IsDirty = true;
        }

        private static float Clamp01(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Mathf.Clamp01(value);
        }
    }
}
