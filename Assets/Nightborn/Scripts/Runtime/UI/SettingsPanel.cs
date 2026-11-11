using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Nightborn.Settings;

namespace Nightborn.UI
{
    /// <summary>
    /// 设置面板（uGUI，纯代码建，可复用到关卡内）。
    ///
    /// 用法（一次性三行）：
    /// <code>
    /// var panel = SettingsPanel.Show(canvasTransform, () =&gt; { /* 关掉后的收尾，例如重设默认选中项 */ });
    /// if (panel != null &amp;&amp; panel.IsOpen) panel.Close();
    /// </code>
    ///
    /// 结构：全屏半透明遮罩（raycastTarget = true，挡住底下的菜单按钮）+ 一块面板。
    /// 风格跟主菜单一致：暗底 #0E0F12 / 血色 #8B1E28 / 烛黄 #E0A44B，字体用内置 LegacyRuntime.ttf。
    ///
    /// 为什么控件全部自己拼（不用 Dropdown）：
    ///  · `Dropdown` 的模板（Template / Viewport / Content / Item）必须在运行时整棵建出来，
    ///    少一个层级就静默不响应/报空引用，纯代码 UI 里最容易翻车的一件；
    ///  · 这里档位最多几个、分辨率一二十个，`< 值 >` 左右箭头按钮 40 行就能覆盖，且键盘左右直接能操作。
    ///
    /// 几个实现细节（都是踩过的）：
    ///  1. 滑杆的「填充」和「把手」由 `Slider.UpdateVisuals()` **驱动锚点**，所以它们的 `sizeDelta`
    ///     必须是 0（把手除外），否则会被撑歪；Fill Area / Handle Slide Area 负责留出把手的半宽。
    ///  2. 面板开着时用 `EnforceSelection()` 把 EventSystem 的选中项锁在面板内 —— 遮罩只挡鼠标，
    ///     挡不住方向键把焦点跳回底下的关卡按钮。
    ///  3. `Time.timeScale` 不动（菜单场景本来就不跑游戏逻辑），光标保持可见。
    ///  4. 写盘交给 `NightbornSettings`：改动只置 `IsDirty`，面板在「停止操作 0.45 s」和关闭时 flush，
    ///     免得拖滑杆时每帧一次 `File.Replace`。
    /// </summary>
    public class SettingsPanel : MonoBehaviour
    {
        // 五色板（与 NightbornMenu / NightbornHud 同一套）：暗底 #0E0F12 / 血色 #8B1E28 / 烛黄 #E0A44B
        // 面板底取 #0E0F12 抬亮一档（0.09 / 0.10 / 0.12），压在 74% 黑遮罩上才看得出层次。
        private static readonly Color ColBlood = new Color(0.545f, 0.118f, 0.157f, 1f);
        private static readonly Color ColCandle = new Color(0.878f, 0.643f, 0.294f, 1f);
        private static readonly Color ColMist = new Color(0.478f, 0.541f, 0.561f, 1f);
        private static readonly Color ColPaper = new Color(0.95f, 0.92f, 0.88f, 1f);

        private const float PanelWidth = 1240f;
        private const float PanelHeight = 820f;

        private const float LabelX = -340f;
        private const float LabelWidth = 440f;
        private const float ControlX = 90f;
        private const float ControlWidth = 380f;
        private const float ValueX = 450f;
        private const float ValueWidth = 190f;

        private const float RowStartY = 236f;
        private const float RowStep = 62f;

        /// <summary>停止操作多久后落盘（秒，不受 timeScale 影响）。</summary>
        private const float SaveDelay = 0.45f;

        private UnityEngine.Events.UnityAction _onClosed;
        private bool _closed;

        private GameObject _defaultSelect;
        private float _dirtySince = -1f;

        private Slider _masterSlider;
        private Slider _ambientSlider;
        private Slider _sfxSlider;
        private Slider _sensSlider;
        private Toggle _invertToggle;
        private Toggle _fullscreenToggle;

        private Text _masterValue;
        private Text _ambientValue;
        private Text _sfxValue;
        private Text _sensValue;
        private Text _invertValue;
        private Text _fullscreenValue;
        private Text _qualityValue;
        private Text _resolutionValue;

        private List<Vector2Int> _resolutions;

        public bool IsOpen => this != null && !_closed;

        // ---------- 开 / 关 ----------

        /// <summary>在指定 Canvas 下打开设置面板（已经开着就直接返回旧的那个）。</summary>
        public static SettingsPanel Show(Transform canvasRoot, UnityEngine.Events.UnityAction onClosed)
        {
            if (canvasRoot == null) return null;

            var existing = canvasRoot.GetComponentInChildren<SettingsPanel>(true);
            if (existing != null && existing.IsOpen) return existing;

            var overlay = new GameObject("UI_Settings", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(canvasRoot, false);

            var overlayRect = overlay.GetComponent<RectTransform>();
            Stretch(overlayRect);

            var overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.74f);
            overlayImage.raycastTarget = true; // 挡住底下的按钮（鼠标）

            var panel = overlay.AddComponent<SettingsPanel>();
            panel._onClosed = onClosed;
            panel.Build();

            // 菜单场景里光标本来就是可见的；这里兜一次，免得哪天从关卡里调起面板时点不动
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            return panel;
        }

        /// <summary>关闭（幂等）：flush 未写盘的改动 + 销毁自己 + 回调调用方收尾。</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;

            if (NightbornSettings.IsDirty) NightbornSettings.Save();

            var callback = _onClosed;
            _onClosed = null;

            if (gameObject != null) Destroy(gameObject);
            if (callback != null) callback();
        }

        private void OnDestroy()
        {
            _closed = true;
        }

        // ---------- 建 UI ----------

        private void Build()
        {
            var panelGo = new GameObject("Panel", typeof(Image));
            panelGo.transform.SetParent(transform, false);

            var panelImage = panelGo.GetComponent<Image>();
            panelImage.color = new Color(0.09f, 0.10f, 0.12f, 0.98f);

            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            panelRect.anchoredPosition = Vector2.zero;

            var root = panelGo.transform;

            CreateText(root, "设置", new Vector2(0f, 340f), 40, ColCandle, 600f, 62f, TextAnchor.MiddleCenter);
            CreateText(root, "修改立即生效并自动保存", new Vector2(0f, 292f), 18, ColMist, 700f, 30f, TextAnchor.MiddleCenter);

            BuildAudioRows(root);
            BuildCameraRows(root);
            BuildDisplayRows(root);
            BuildFooter(root);

            Refresh();

            // 默认选中「返回」：误按回车不会把设置清回默认
            _defaultSelect = CreateButton(root, "UI_SettingsBack", "返回", new Vector2(160f, -300f), new Vector2(320f, 62f),
                new Color(0.20f, 0.22f, 0.24f, 0.95f), ColPaper, 26, Close);

            var es = EventSystem.current;
            if (es != null && _defaultSelect != null)
            {
                es.SetSelectedGameObject(null);
                es.SetSelectedGameObject(_defaultSelect);
            }
        }

        private void BuildAudioRows(Transform root)
        {
            float y = RowStartY;

            CreateText(root, "主音量", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _masterSlider = CreateSlider(root, "UI_SettingsMaster", y, 0f, 1f, NightbornSettings.Master, v =>
            {
                NightbornSettings.SetMaster(v);
                Touch();
                Refresh();
            });
            _masterValue = CreateText(root, "", new Vector2(ValueX, y), 22, ColCandle, ValueWidth, 40f, TextAnchor.MiddleRight);

            y -= RowStep;
            CreateText(root, "环境音量", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _ambientSlider = CreateSlider(root, "UI_SettingsAmbient", y, 0f, 1f, NightbornSettings.Ambient, v =>
            {
                NightbornSettings.SetAmbient(v);
                Touch();
                Refresh();
            });
            _ambientValue = CreateText(root, "", new Vector2(ValueX, y), 22, ColCandle, ValueWidth, 40f, TextAnchor.MiddleRight);

            y -= RowStep;
            CreateText(root, "音效", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _sfxSlider = CreateSlider(root, "UI_SettingsSfx", y, 0f, 1f, NightbornSettings.Sfx, v =>
            {
                NightbornSettings.SetSfx(v);
                Touch();
                Refresh();
            });
            _sfxValue = CreateText(root, "", new Vector2(ValueX, y), 22, ColCandle, ValueWidth, 40f, TextAnchor.MiddleRight);
        }

        private void BuildCameraRows(Transform root)
        {
            float y = RowStartY - RowStep * 3f;

            CreateText(root, "鼠标灵敏度", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _sensSlider = CreateSlider(root, "UI_SettingsSensitivity", y, NightbornSettings.MinSensitivity,
                NightbornSettings.MaxSensitivity, NightbornSettings.Sensitivity, v =>
                {
                    NightbornSettings.SetSensitivity(v);
                    Touch();
                    Refresh();
                });
            _sensValue = CreateText(root, "", new Vector2(ValueX, y), 22, ColCandle, ValueWidth, 40f, TextAnchor.MiddleRight);

            y -= RowStep;
            CreateText(root, "反转 Y 轴", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _invertToggle = CreateToggle(root, "UI_SettingsInvertY", y, NightbornSettings.InvertY, v =>
            {
                NightbornSettings.SetInvertY(v);
                Touch();
                Refresh();
            });
            _invertValue = CreateText(root, "", new Vector2(ValueX, y), 22, ColCandle, ValueWidth, 40f, TextAnchor.MiddleRight);
        }

        private void BuildDisplayRows(Transform root)
        {
            float y = RowStartY - RowStep * 5f;

            CreateText(root, "画质", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _qualityValue = CreateSpinner(root, "UI_SettingsQuality", y, StepQuality);
            y -= RowStep;

            CreateText(root, "全屏", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _fullscreenToggle = CreateToggle(root, "UI_SettingsFullscreen", y, NightbornSettings.Fullscreen, v =>
            {
                NightbornSettings.SetFullscreen(v);
                Touch();
                Refresh();
            });
            _fullscreenValue = CreateText(root, "", new Vector2(ValueX, y), 22, ColCandle, ValueWidth, 40f, TextAnchor.MiddleRight);

            y -= RowStep;
            CreateText(root, "分辨率", new Vector2(LabelX, y), 24, ColPaper, LabelWidth, 40f, TextAnchor.MiddleLeft);
            _resolutionValue = CreateSpinner(root, "UI_SettingsResolution", y, StepResolution);
        }

        private void BuildFooter(Transform root)
        {
            CreateButton(root, "UI_SettingsReset", "恢复默认", new Vector2(-160f, -300f), new Vector2(320f, 62f),
                new Color(ColBlood.r, ColBlood.g, ColBlood.b, 0.9f), ColPaper, 24, () =>
                {
                    NightbornSettings.ResetToDefaults();
                    Touch();
                    Refresh();
                });

            CreateText(root, System.IO.Path.GetFileName(NightbornSettings.SettingsPath) + "　·　Esc 返回",
                new Vector2(0f, -368f), 15, new Color(0.42f, 0.46f, 0.48f, 1f), 900f, 28f, TextAnchor.MiddleCenter);
        }

        // ---------- 刷新显示 ----------

        /// <summary>把所有控件与数值文本对齐到当前设置值（不触发 onValueChanged）。</summary>
        private void Refresh()
        {
            float master = NightbornSettings.Master;
            float ambient = NightbornSettings.Ambient;
            float sfx = NightbornSettings.Sfx;
            float sens = NightbornSettings.Sensitivity;
            bool invert = NightbornSettings.InvertY;
            bool fullscreen = NightbornSettings.Fullscreen;

            if (_masterSlider != null) _masterSlider.SetValueWithoutNotify(master);
            if (_ambientSlider != null) _ambientSlider.SetValueWithoutNotify(ambient);
            if (_sfxSlider != null) _sfxSlider.SetValueWithoutNotify(sfx);
            if (_sensSlider != null) _sensSlider.SetValueWithoutNotify(sens);

            if (_masterValue != null) _masterValue.text = Percent(master);
            if (_ambientValue != null) _ambientValue.text = Percent(ambient);
            if (_sfxValue != null) _sfxValue.text = Percent(sfx);
            if (_sensValue != null) _sensValue.text = sens.ToString("F2");

            if (_invertToggle != null) _invertToggle.SetIsOnWithoutNotify(invert);
            if (_invertValue != null) _invertValue.text = invert ? "开" : "关";

            if (_fullscreenToggle != null) _fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
            if (_fullscreenValue != null) _fullscreenValue.text = fullscreen ? "开" : "关";

            if (_qualityValue != null)
                _qualityValue.text = NightbornSettings.QualityName(NightbornSettings.QualityLevel);

            if (_resolutionValue != null)
            {
                Vector2Int size = NightbornSettings.Resolution;
                _resolutionValue.text = size.x + " × " + size.y;
            }
        }

        private static string Percent(float value)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        }

        // ---------- 档位 / 分辨率 ----------

        private void StepQuality(int delta)
        {
            int count = QualitySettings.names.Length;
            if (count <= 0) return;

            int next = Mathf.Clamp(NightbornSettings.QualityLevel + delta, 0, count - 1);
            NightbornSettings.SetQualityLevel(next);
            Touch();
            Refresh();
        }

        private void StepResolution(int delta)
        {
            if (_resolutions == null) _resolutions = NightbornSettings.AvailableResolutions();
            if (_resolutions == null || _resolutions.Count == 0) return;

            Vector2Int current = NightbornSettings.Resolution;
            int index = _resolutions.IndexOf(current);
            if (index < 0)
            {
                // 当前分辨率不在候选表里（编辑器 Game View / 窗口被拉过）：退到"不超过它"的最后一档
                index = 0;
                for (int i = 0; i < _resolutions.Count; i++)
                {
                    Vector2Int candidate = _resolutions[i];
                    if (candidate.x <= current.x && candidate.y <= current.y) index = i;
                }
            }

            index = Mathf.Clamp(index + delta, 0, _resolutions.Count - 1);
            NightbornSettings.SetResolution(_resolutions[index].x, _resolutions[index].y);
            Touch();
            Refresh();
        }

        // ---------- 写盘节流 / 模态焦点 ----------

        private void Touch()
        {
            _dirtySince = Time.unscaledTime;
        }

        private void Update()
        {
            if (NightbornSettings.IsDirty)
            {
                if (_dirtySince < 0f) _dirtySince = Time.unscaledTime;
                else if (Time.unscaledTime - _dirtySince >= SaveDelay)
                {
                    NightbornSettings.Save();
                    _dirtySince = -1f;
                }
            }
            else
            {
                _dirtySince = -1f;
            }

            EnforceSelection();
        }

        /// <summary>
        /// 遮罩只挡鼠标，挡不住方向键：EventSystem 会顺着 Selectable 图找底下的关卡按钮。
        /// 所以每帧把选中项拉回面板里。
        /// </summary>
        private void EnforceSelection()
        {
            if (_defaultSelect == null) return;

            var es = EventSystem.current;
            if (es == null) return;

            GameObject current = es.currentSelectedGameObject;
            if (current == _defaultSelect) return;
            if (current != null && current.transform.IsChildOf(transform)) return;

            es.SetSelectedGameObject(null);
            es.SetSelectedGameObject(_defaultSelect);
        }

        // ---------- 基础件 ----------

        private Slider CreateSlider(Transform parent, string name, float y, float min, float max, float value,
            UnityEngine.Events.UnityAction<float> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ControlWidth, 34f);
            rect.anchoredPosition = new Vector2(ControlX, y);

            // 槽（同时当鼠标点击/拖动的命中区）
            var background = new GameObject("Background", typeof(Image));
            background.transform.SetParent(go.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 10f);
            backgroundRect.anchoredPosition = Vector2.zero;
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(0.16f, 0.17f, 0.19f, 1f);
            backgroundImage.raycastTarget = true;

            // 填充区（左右各让出把手半宽）
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.pivot = new Vector2(0.5f, 0.5f);
            fillAreaRect.sizeDelta = new Vector2(-20f, 10f);
            fillAreaRect.anchoredPosition = Vector2.zero;

            var fill = new GameObject("Fill", typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.sizeDelta = Vector2.zero;   // 锚点由 Slider.UpdateVisuals 驱动，尺寸必须为 0
            fillRect.anchoredPosition = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = ColBlood;
            fillImage.raycastTarget = true;

            // 把手区
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.pivot = new Vector2(0.5f, 0.5f);
            handleAreaRect.sizeDelta = new Vector2(-20f, 0f);
            handleAreaRect.anchoredPosition = Vector2.zero;

            var handle = new GameObject("Handle", typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0f, 0f);
            handleRect.anchorMax = new Vector2(0f, 1f);
            handleRect.pivot = new Vector2(0.5f, 0.5f);
            handleRect.sizeDelta = new Vector2(18f, 0f);
            handleRect.anchoredPosition = Vector2.zero;
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = ColCandle;
            handleImage.raycastTarget = true;

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = false;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = Mathf.Clamp(value, min, max);   // 先赋值再挂回调，避免建的时候就触发一次

            if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        private Toggle CreateToggle(Transform parent, string name, float y, bool value,
            UnityEngine.Events.UnityAction<bool> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(44f, 44f);
            rect.anchoredPosition = new Vector2(ControlX, y);

            var background = new GameObject("Background", typeof(Image));
            background.transform.SetParent(go.transform, false);
            Stretch(background.GetComponent<RectTransform>());
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(0.16f, 0.17f, 0.19f, 1f);
            backgroundImage.raycastTarget = true;

            var checkmark = new GameObject("Checkmark", typeof(Image));
            checkmark.transform.SetParent(background.transform, false);
            var checkRect = checkmark.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.offsetMin = new Vector2(7f, 7f);
            checkRect.offsetMax = new Vector2(-7f, -7f);
            var checkImage = checkmark.GetComponent<Image>();
            checkImage.color = ColBlood;
            checkImage.raycastTarget = false;

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkImage;   // 勾选态靠 canvasRenderer 的 alpha 切换
            toggle.isOn = value;           // 先赋值再挂回调

            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        /// <summary>`&lt; 值 &gt;` 左右箭头按钮，返回中间那格文本（调用方负责刷新）。</summary>
        private Text CreateSpinner(Transform parent, string name, float y, Action<int> onStep)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(ControlWidth, 48f);
            rootRect.anchoredPosition = new Vector2(ControlX, y);

            CreateArrowButton(root.transform, name + "Prev", "‹", new Vector2(-160f, 0f), () => onStep(-1));
            CreateArrowButton(root.transform, name + "Next", "›", new Vector2(160f, 0f), () => onStep(1));

            return CreateText(root.transform, "", new Vector2(0f, 0f), 22, ColCandle, 240f, 40f, TextAnchor.MiddleCenter);
        }

        private GameObject CreateArrowButton(Transform parent, string name, string label, Vector2 anchoredPos,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(52f, 48f);
            rect.anchoredPosition = anchoredPos;

            go.GetComponent<Image>().color = new Color(0.16f, 0.17f, 0.19f, 1f);

            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            var textGo = CreateText(go.transform, label, Vector2.zero, 30, ColCandle, 48f, 44f, TextAnchor.MiddleCenter);
            Stretch(textGo.GetComponent<RectTransform>());

            return go;
        }

        private GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size,
            Color fill, Color textColor, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            go.GetComponent<Image>().color = fill;

            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            var textGo = CreateText(go.transform, label, Vector2.zero, fontSize, textColor,
                size.x - 24f, size.y - 8f, TextAnchor.MiddleCenter);
            Stretch(textGo.GetComponent<RectTransform>());

            return go;
        }

        private Text CreateText(Transform parent, string content, Vector2 anchoredPos, int fontSize,
            Color color, float width, float height, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = anchoredPos;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
