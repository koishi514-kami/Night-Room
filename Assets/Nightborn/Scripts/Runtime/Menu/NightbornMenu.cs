using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Nightborn.Level;

namespace Nightborn.Menu
{
    /// <summary>
    /// 主菜单 / 选关。
    ///
    /// 全部用代码建 UI（不依赖 prefab），一个场景一个空物体就能跑：
    ///  · 顶部两个动作：「继续」直接进该去的那一关；「新的夜晚」清档（带确认框，HANDOVER §4 P0-4）；
    ///  · 关卡两列排布，第一次进入只有第 1 关可点，其余按存档线性解锁；
    ///  · 底部显示通关进度与存档文件名，方便确认自己看的是哪一份档。
    ///
    /// 「继续」的目标判定（见 <see cref="ResolveContinueIndex"/>）：
    ///  1. 优先 `lastLevelId`，前提是它还没通关（中途被抓回检查点、退出游戏那种）；
    ///  2. 否则取「已解锁但还没通关」的第一关 —— 也就是主线前沿；
    ///  3. 全通了就禁用按钮。
    /// 注意：`CompleteLevel()` 也会把 `lastLevelId` 写成刚通关的那一关，所以只靠它是不够的，
    /// 必须再过一道「已完成就往后找」的判断。
    /// </summary>
    public class NightbornMenu : MonoBehaviour
    {
        [SerializeField] private string[] levelScenes = { "L_Ch01_S01_Street" };
        [SerializeField] private string[] levelLabels = { "第一章 · 第一夜　街区" };
        [Tooltip("与 levelScenes 一一对应的关卡 id（存档里用的那个）。")]
        [SerializeField] private string[] levelIds = { "Ch01_S01" };

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

        private void Start()
        {
            EnsureEventSystem();
            Camera.main?.gameObject.SetActive(true);
            BuildUi();
        }

        private void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var go = new GameObject("MENU_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
        }

        private void BuildUi()
        {
            if (_canvasGo != null)
            {
                _canvasGo.SetActive(false);
                Destroy(_canvasGo);
            }

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

            CreateText(root, "WASD 移动　Shift 潜行　Tab 化形　Q 血视　E 交互　左键吸血　1 压光　Esc 释放鼠标",
                new Vector2(0f, -330f), 20, new Color(0.62f, 0.67f, 0.69f, 1f), 1500f, 40f, TextAnchor.MiddleCenter);

            var progress = SaveSystem.Data;
            int done = 0;
            foreach (var record in progress.levels) if (record.completed) done++;

            CreateText(root, "已通关 " + done + " / " + levelScenes.Length,
                new Vector2(0f, -372f), 20, _candle, 600f, 36f, TextAnchor.MiddleCenter);

            CreateText(root, "存档 " + System.IO.Path.GetFileName(SaveSystem.SavePath),
                new Vector2(0f, -404f), 16, _mist, 900f, 30f, TextAnchor.MiddleCenter);
        }

        private void BuildActionRow(Transform parent)
        {
            bool allDone;
            int index = ResolveContinueIndex(out allDone);

            string label;
            if (allDone) label = "全部通关";
            else if (index >= 0) label = "继续 · " + LabelFor(index);
            else label = "继续";

            var continueGo = CreateButton(parent, "MENU_Continue", label, new Vector2(-280f, 214f),
                new Vector2(520f, 58f),
                allDone ? _lockedFill : new Color(_blood.r, _blood.g, _blood.b, 0.9f),
                allDone ? _lockedText : _paper, 26, null);

            var continueButton = continueGo.GetComponent<Button>();
            if (!allDone && index >= 0)
            {
                string sceneName = levelScenes[index];
                continueButton.onClick.AddListener(() => LoadLevel(sceneName));
            }
            else
            {
                continueButton.interactable = false;
            }

            CreateButton(parent, "MENU_NewGame", "新的夜晚", new Vector2(290f, 214f), new Vector2(440f, 58f),
                new Color(0.17f, 0.18f, 0.20f, 0.92f), _paper, 26, ShowNewGameConfirm);
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
                if (unlocked) button.onClick.AddListener(() => LoadLevel(sceneName));
            }
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
            var overlay = new GameObject("MENU_Confirm", typeof(Image));
            overlay.transform.SetParent(_canvasGo.transform, false);

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
                    BuildUi();
                });

            CreateButton(panel.transform, "MENU_ConfirmNo", "返回", new Vector2(210f, -108f), new Vector2(320f, 64f),
                new Color(0.20f, 0.22f, 0.24f, 0.95f), _paper, 26, () => Destroy(overlay));
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
