using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Nightborn.Audio;
using Nightborn.Level;
using Nightborn.Settings;
using Nightborn.UI;

namespace Nightborn.Menu
{
    /// <summary>
    /// 主菜单 / 选关。
    ///
    /// 全部用代码建 UI（不依赖 prefab），一个场景一个空物体就能跑：
    ///  · 顶部三个动作：「继续」直接进该去的那一关；「新的夜晚」清档（带确认框，HANDOVER §4 P0-4）；
    ///    「设置」打开设置面板（音量 / 灵敏度 / 画质 / 分辨率，独立文件，清档不会连设置一起删）；
    ///  · 关卡两列排布，第一次进入只有第 1 关可点，其余按存档线性解锁；
    ///  · 底部显示通关进度与存档文件名，方便确认自己看的是哪一份档。
    ///
    /// 「继续」的目标判定（见 <see cref="ResolveContinueIndex"/>）：
    ///  1. 优先 `lastLevelId`，前提是它还没通关（中途被抓回检查点、退出游戏那种）；
    ///  2. 否则取「已解锁但还没通关」的第一关 —— 也就是主线前沿；
    ///  3. 全通了就禁用按钮。
    /// 注意：`CompleteLevel()` 也会把 `lastLevelId` 写成刚通关的那一关，所以只靠它是不够的，
    /// 必须再过一道「已完成就往后找」的判断。
    ///
    /// 本轮（UI/HUD 静态审计）改动要点：
    ///  1. 进场强制复位 `Time.timeScale` 与光标：光标目前靠 `ThirdPersonCamera.LateUpdate` 里的 Esc
    ///     分支持解锁，能work但属于「恰好」；菜单自己兜一次，免得哪天相机改了 Esc 行为就点不动按钮。
    ///  2. 键盘/手柄可操作：`StandaloneInputModule` 没有默认选中项时方向键、回车全是死的（旧实现
    ///     只能鼠标点）。现在建完 UI 主动选中「继续 / 第一关」，Esc 关确认框。
    ///  3. 菜单场景补 `AudioListener` + 一层极轻的底噪：`L_Main` 的相机没有监听器，整个菜单是静音的
    ///     （Unity 还会在第一次出声时报 "There are no audio listeners in the scene"）。
    ///     底噪音量 `menuAmbienceVolume`，设 0 就是纯静音。
    ///  4. 清档后原地重建：确认框引用、默认选中项一起重置（旧实现重建后 EventSystem 的选中项指向
    ///     已销毁对象，键盘操作从此失效）。
    ///
    /// 2026-11-12「设置菜单」那一轮：顶部动作行由 2 个按钮改 3 个（「设置」并进同一行，**y 仍是 214**，
    /// 横向重排成与关卡网格同宽 ±580），Esc 优先级固定为「确认框 → 设置面板 → 退出游戏」。
    /// </summary>
    public class NightbornMenu : MonoBehaviour
    {
        [SerializeField] private string[] levelScenes = { "L_Ch01_S01_Street" };
        [SerializeField] private string[] levelLabels = { "第一章 · 第一夜　街区" };
        [Tooltip("与 levelScenes 一一对应的关卡 id（存档里用的那个）。")]
        [SerializeField] private string[] levelIds = { "Ch01_S01" };

        [Header("环境音")]
        [Tooltip("菜单底噪音量；0 = 不播（仍然会补一个 AudioListener）。关卡里的环境音是 0.35。")]
        [Range(0f, 1f)] [SerializeField] private float menuAmbienceVolume = 0.3f;

        private readonly Color _candle = new Color(0.878f, 0.643f, 0.294f, 1f);
        private readonly Color _ink = new Color(0.055f, 0.059f, 0.071f, 1f);
        private readonly Color _mist = new Color(0.478f, 0.541f, 0.561f, 1f);
        private readonly Color _blood = new Color(0.545f, 0.118f, 0.157f, 1f);
        private readonly Color _paper = new Color(0.95f, 0.92f, 0.88f, 1f);
        private readonly Color _lockedFill = new Color(0.16f, 0.17f, 0.19f, 0.8f);
        private readonly Color _lockedText = new Color(0.45f, 0.47f, 0.5f, 1f);

        private const int GridColumns = 2;
        private const float ColumnWidth = 560f;
        private const float ColumnGap = 40f;
        private const float RowHeight = 76f;
        private const float RowStartY = 96f;

        private GameObject _canvasGo;
        private GameObject _confirm;      // 「新的夜晚」确认框根节点（null = 没开）
        private GameObject _defaultSelect; // 键盘/手柄的默认选中项
        private GameObject _newGameButton;
        private AudioSource _ambience;
        private SettingsPanel _settings;   // 设置面板（null = 没开）

        private void Start()
        {
            // 设置：先订阅再 Apply —— 「在关卡里改过设置、再回菜单」时底噪要按新主音量起播
            NightbornSettings.OnVolumeChanged -= OnVolumeSettingsChanged;
            NightbornSettings.OnVolumeChanged += OnVolumeSettingsChanged;
            NightbornSettings.Apply();

            EnsureEventSystem();
            EnsureAudio();

            // 从关卡回菜单时，光标与时间缩放可能停在关卡里的状态
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Camera.main?.gameObject.SetActive(true);
            BuildUi();
        }

        private void OnDestroy()
        {
            NightbornSettings.OnVolumeChanged -= OnVolumeSettingsChanged;
            if (_ambience != null) Destroy(_ambience.gameObject);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (_ambience != null) _ambience.volume = focused ? MenuVolume : 0f;
        }

        /// <summary>底噪实际音量 = 固定增益 × 设置里的主音量（在设置面板里一拉就变）。</summary>
        private float MenuVolume => menuAmbienceVolume * NightbornSettings.Master;

        private void OnVolumeSettingsChanged()
        {
            if (_ambience != null) _ambience.volume = Application.isFocused ? MenuVolume : 0f;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            // 优先级：确认框 → 设置面板 → 退出（Esc 永远只关一层，不会一下退到桌面）
            if (_confirm != null)
            {
                CloseConfirm();
                return;
            }

            if (_settings != null)
            {
                _settings.Close();   // Close 会回调把 _settings 置回 null
                return;
            }

            // 编辑器里不退出（Application.Quit 在编辑器里本来也是空操作）
            if (!Application.isEditor) Application.Quit();
        }

        private void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("MENU_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }

        private void EnsureAudio()
        {
            // L_Main 的相机没有 AudioListener，整个场景一个都没有
            if (Object.FindObjectOfType<AudioListener>() == null)
            {
                var listenerGo = new GameObject("AUDIO_MenuListener");
                listenerGo.transform.SetParent(transform, false);
                listenerGo.AddComponent<AudioListener>();
            }

            if (menuAmbienceVolume <= 0f || _ambience != null) return;

            var go = new GameObject("AUDIO_MenuAmbience");
            go.transform.SetParent(transform, false);
            _ambience = go.AddComponent<AudioSource>();
            _ambience.playOnAwake = false;
            _ambience.loop = true;
            _ambience.spatialBlend = 0f;
            _ambience.volume = MenuVolume;
            _ambience.clip = SynthAudio.Drone();
            _ambience.Play();
        }

        private void BuildUi()
        {
            if (_canvasGo != null)
            {
                _canvasGo.SetActive(false);
                Destroy(_canvasGo);
            }
            _confirm = null;          // 跟着旧 canvas 一起销毁了
            _settings = null;         // 同上：面板是 canvas 的子物体，一起没了
            _defaultSelect = null;
            _newGameButton = null;

            _canvasGo = new GameObject("MENU_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);

            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = new GameObject("MENU_Background", typeof(Image));
            bg.transform.SetParent(_canvasGo.transform, false);
            bg.GetComponent<Image>().color = _ink;
            Stretch(bg.GetComponent<RectTransform>());

            var root = _canvasGo.transform;
            CreateText(root, "夜裔 · 血月临城", new Vector2(0f, 384f), 72, _candle, 1200f, 110f, TextAnchor.MiddleCenter);
            CreateText(root, "N I G H T B O R N", new Vector2(0f, 316f), 24, _mist, 900f, 40f, TextAnchor.MiddleCenter);

            BuildActionRow(root);
            BuildLevelGrid(root);

            CreateText(root, "WASD 移动　Shift 潜行　Tab 化形　Q 血视　E 交互　左键吸血　1 压光　Esc 关弹窗",
                new Vector2(0f, -330f), 20, new Color(0.62f, 0.67f, 0.69f, 1f), 1500f, 40f, TextAnchor.MiddleCenter);

            var progress = SaveSystem.Data;
            int done = 0;
            foreach (var record in progress.levels) if (record.completed) done++;

            CreateText(root, "已通关 " + done + " / " + levelScenes.Length,
                new Vector2(0f, -372f), 20, _candle, 600f, 36f, TextAnchor.MiddleCenter);

            CreateText(root, "存档 " + System.IO.Path.GetFileName(SaveSystem.SavePath),
                new Vector2(0f, -404f), 16, _mist, 900f, 30f, TextAnchor.MiddleCenter);

            if (_defaultSelect == null) _defaultSelect = _newGameButton;
            SelectDefault();
        }

        private void BuildActionRow(Transform parent)
        {
            bool allDone;
            int index = ResolveContinueIndex(out allDone);

            string label;
            if (allDone) label = "全部通关";
            else if (index >= 0) label = "继续 · " + LabelFor(index);
            else label = "继续";

            // 三个按钮一行，y 仍是 214（上下边界不变），横向重排成与关卡网格同宽 ±580：
            //   继续 -580..-100 · 新的夜晚 -70..310 · 设置 340..580（间隔各 30）
            var continueGo = CreateButton(parent, "MENU_Continue", label, new Vector2(-340f, 214f),
                new Vector2(480f, 58f),
                allDone ? _lockedFill : new Color(_blood.r, _blood.g, _blood.b, 0.9f),
                allDone ? _lockedText : _paper, 26, null);

            var continueButton = continueGo.GetComponent<Button>();
            if (!allDone && index >= 0)
            {
                string sceneName = levelScenes[index];
                continueButton.onClick.AddListener(() => LoadLevel(sceneName));
                _defaultSelect = continueGo;
            }
            else
            {
                continueButton.interactable = false;
            }

            _newGameButton = CreateButton(parent, "MENU_NewGame", "新的夜晚", new Vector2(120f, 214f),
                new Vector2(380f, 58f),
                new Color(0.17f, 0.18f, 0.20f, 0.92f), _paper, 26, ShowNewGameConfirm);

            CreateButton(parent, "MENU_Settings", "设置", new Vector2(460f, 214f),
                new Vector2(240f, 58f),
                new Color(0.17f, 0.18f, 0.20f, 0.92f), _paper, 26, ShowSettings);
        }

        private void BuildLevelGrid(Transform parent)
        {
            float offset = (ColumnWidth + ColumnGap) * 0.5f;

            for (int i = 0; i < levelScenes.Length; i++)
            {
                int column = i % GridColumns;
                int row = i / GridColumns;
                var anchoredPos = new Vector2(column == 0 ? -offset : offset, RowStartY - row * RowHeight);

                string sceneName = levelScenes[i];
                bool unlocked = IsLevelUnlocked(i);
                string label = LabelFor(i);

                var go = CreateButton(parent, "MENU_Level_" + sceneName,
                    unlocked ? label : label + "　（未解锁）",
                    anchoredPos, new Vector2(ColumnWidth, 62f),
                    unlocked ? new Color(_blood.r, _blood.g, _blood.b, 0.85f) : _lockedFill,
                    unlocked ? _paper : _lockedText, 26, null);

                var button = go.GetComponent<Button>();
                button.interactable = unlocked;
                if (unlocked)
                {
                    button.onClick.AddListener(() => LoadLevel(sceneName));
                    // 第一个已解锁的关卡当兜底选中项（「继续」不可用时）
                    if (_defaultSelect == null) _defaultSelect = go;
                }
            }
        }

        // ---------- 键盘 / 手柄 ----------

        private void SelectDefault()
        {
            var es = EventSystem.current;
            if (es == null) return;
            if (_defaultSelect == null || !_defaultSelect.activeInHierarchy) return;

            es.SetSelectedGameObject(null);
            es.SetSelectedGameObject(_defaultSelect);
        }

        // ---------- 进度判定 ----------

        private string LabelFor(int index)
        {
            if (index < 0 || index >= levelScenes.Length) return "";
            return index < levelLabels.Length ? levelLabels[index] : levelScenes[index];
        }

        /// <summary>线性推进：第一关常开，之后要求上一关已通关。</summary>
        private bool IsLevelUnlocked(int index)
        {
            if (index <= 0) return true;
            int previous = index - 1;
            if (previous >= levelIds.Length) return false;
            return SaveSystem.IsCompleted(levelIds[previous]);
        }

        private bool IsLevelCompleted(int index)
        {
            return index >= 0 && index < levelIds.Length && SaveSystem.IsCompleted(levelIds[index]);
        }

        private int IndexOfLevelId(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < levelIds.Length; i++)
                if (levelIds[i] == id) return i;
            return -1;
        }

        /// <summary>
        /// 「继续」该进哪一关。返回 −1 表示没有可继续的（全部通关）。
        /// </summary>
        private int ResolveContinueIndex(out bool allDone)
        {
            allDone = false;

            // 1) 上次玩的那一关，只要还没通关就优先回它
            int last = IndexOfLevelId(SaveSystem.Data.lastLevelId);
            if (last >= 0 && !IsLevelCompleted(last)) return last;

            // 2) 否则取主线前沿：已解锁且还没通关的第一关
            for (int i = 0; i < levelScenes.Length; i++)
                if (IsLevelUnlocked(i) && !IsLevelCompleted(i)) return i;

            // 3) 全通了
            allDone = true;
            return -1;
        }

        // ---------- 弹窗 ----------

        /// <summary>「新的夜晚」确认框：全屏压暗 + 一块面板，确认才清档（清档不可撤销）。</summary>
        private void ShowNewGameConfirm()
        {
            if (_confirm != null) return;

            var overlay = new GameObject("MENU_Confirm", typeof(Image));
            overlay.transform.SetParent(_canvasGo.transform, false);
            _confirm = overlay;

            var overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.72f);
            overlayImage.raycastTarget = true; // 挡住底下的按钮
            Stretch(overlay.GetComponent<RectTransform>());

            var panel = new GameObject("Panel", typeof(Image));
            panel.transform.SetParent(overlay.transform, false);

            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.09f, 0.10f, 0.12f, 0.97f);

            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(940f, 380f);
            panelRect.anchoredPosition = Vector2.zero;

            CreateText(panel.transform, "开始新的夜晚？", new Vector2(0f, 112f), 40, _candle, 860f, 60f, TextAnchor.MiddleCenter);
            CreateText(panel.transform, "这会清空通关进度、检查点与血之遗物记录，无法撤销。",
                new Vector2(0f, 40f), 24, _mist, 860f, 70f, TextAnchor.MiddleCenter);
            CreateText(panel.transform, "存档：" + SaveSystem.SavePath,
                new Vector2(0f, -12f), 16, new Color(0.42f, 0.46f, 0.48f, 1f), 860f, 30f, TextAnchor.MiddleCenter);

            CreateButton(panel.transform, "MENU_ConfirmYes", "清空并重来", new Vector2(-210f, -108f), new Vector2(320f, 64f),
                new Color(_blood.r, _blood.g, _blood.b, 0.95f), _paper, 26, () =>
                {
                    SaveSystem.Delete();
                    Debug.Log("[Nightborn] 存档已清空，回到第一夜。");
                    _confirm = null;
                    BuildUi(); // 原地重建（旧 canvas 连同这个按钮一起销毁）
                });

            var noButton = CreateButton(panel.transform, "MENU_ConfirmNo", "返回", new Vector2(210f, -108f), new Vector2(320f, 64f),
                new Color(0.20f, 0.22f, 0.24f, 0.95f), _paper, 26, CloseConfirm);

            // 默认选中「返回」：误按回车不会清档
            var es = EventSystem.current;
            if (es != null && noButton != null)
            {
                es.SetSelectedGameObject(null);
                es.SetSelectedGameObject(noButton);
            }
        }

        /// <summary>
        /// 打开设置面板：全屏遮罩挡住底下的按钮（鼠标），`Time.timeScale` 不动，光标保持可见；
        /// 面板自己把 EventSystem 的选中项锁在面板内，Esc 由 <see cref="Update"/> 的优先级链关掉。
        /// </summary>
        private void ShowSettings()
        {
            if (_settings != null || _canvasGo == null) return;

            _settings = SettingsPanel.Show(_canvasGo.transform, () =>
            {
                _settings = null;
                SelectDefault();
            });
        }

        private void CloseConfirm()
        {
            if (_confirm == null) return;
            Destroy(_confirm);
            _confirm = null;
            SelectDefault();
        }

        // ---------- 基础件 ----------

        private void LoadLevel(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            Debug.Log("[Nightborn] 载入关卡：" + sceneName);
            Time.timeScale = 1f;
            SceneManager.LoadScene(sceneName);
        }

        private GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size,
            Color fill, Color textColor, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            go.GetComponent<Image>().color = fill;

            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            var textGo = CreateText(go.transform, label, Vector2.zero, fontSize, textColor,
                size.x - 24f, size.y - 8f, TextAnchor.MiddleCenter);
            Stretch(textGo.GetComponent<RectTransform>());

            return go;
        }

        private GameObject CreateText(Transform parent, string content, Vector2 anchoredPos, int fontSize,
            Color color, float width, float height, TextAnchor anchor)
        {
            var go = new GameObject("MENU_Text", typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = anchoredPos;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
