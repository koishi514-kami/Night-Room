using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Nightborn.Level;

namespace Nightborn.Menu
{
    /// <summary>
    /// 主菜单 / 选关（M1 步骤 7 的选关 UI）。
    ///
    /// 全部用代码建 UI（不依赖 prefab），一个场景一个空物体就能跑：
    /// 关卡按章分组列出，第一次进入只有第 1 关可点，其余根据存档解锁。
    /// </summary>
    public class NightbornMenu : MonoBehaviour
    {
        [SerializeField] private string[] levelScenes = { "L_Ch01_S01_Street" };
        [SerializeField] private string[] levelLabels = { "第一章 · 第一夜　街区" };

        private readonly Color _candle = new Color(0.878f, 0.643f, 0.294f, 1f);
        private readonly Color _ink = new Color(0.055f, 0.059f, 0.071f, 1f);
        private readonly Color _mist = new Color(0.478f, 0.541f, 0.561f, 1f);
        private readonly Color _blood = new Color(0.545f, 0.118f, 0.157f, 1f);

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
            var canvasGo = new GameObject("MENU_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 背景
            var bg = new GameObject("MENU_Background", typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgImage = bg.GetComponent<Image>();
            bgImage.color = _ink;
            Stretch(bg.GetComponent<RectTransform>());

            CreateText(canvasGo.transform, "夜裔 · 血月临城", new Vector2(0f, 330f), 72, _candle, 1200f, 110f, TextAnchor.MiddleCenter);
            CreateText(canvasGo.transform, "N I G H T B O R N", new Vector2(0f, 262f), 24, _mist, 900f, 40f, TextAnchor.MiddleCenter);

            float y = 110f;
            for (int i = 0; i < levelScenes.Length; i++)
            {
                string sceneName = levelScenes[i];
                bool unlocked = i == 0 || SaveSystem.IsUnlocked(sceneName);
                string label = i < levelLabels.Length ? levelLabels[i] : sceneName;
                CreateLevelButton(canvasGo.transform, label, sceneName, unlocked, new Vector2(0f, y));
                y -= 96f;
            }

            CreateText(canvasGo.transform, "WASD 移动　Shift 潜行　Tab 化形　Q 血视　E 交互　左键吸血　1 压光　Esc 释放鼠标",
                new Vector2(0f, -390f), 20, new Color(0.62f, 0.67f, 0.69f, 1f), 1500f, 40f, TextAnchor.MiddleCenter);

            var progress = SaveSystem.Data;
            int done = 0;
            foreach (var record in progress.levels) if (record.completed) done++;
            CreateText(canvasGo.transform, "已通关 " + done + " / " + levelScenes.Length,
                new Vector2(0f, -430f), 20, _candle, 600f, 36f, TextAnchor.MiddleCenter);
        }

        private void CreateLevelButton(Transform parent, string label, string sceneName, bool unlocked, Vector2 anchoredPos)
        {
            var go = new GameObject("MENU_Level_" + sceneName, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(760f, 76f);
            rt.anchoredPosition = anchoredPos;

            var image = go.GetComponent<Image>();
            image.color = unlocked ? new Color(_blood.r, _blood.g, _blood.b, 0.85f) : new Color(0.16f, 0.17f, 0.19f, 0.8f);

            var button = go.GetComponent<Button>();
            button.interactable = unlocked;
            if (unlocked)
                button.onClick.AddListener(() => LoadLevel(sceneName));

            string text = unlocked ? label : label + "　（未解锁）";
            var textGo = CreateText(go.transform, text, Vector2.zero, 30,
                unlocked ? new Color(0.95f, 0.92f, 0.88f, 1f) : new Color(0.45f, 0.47f, 0.5f, 1f),
                740f, 60f, TextAnchor.MiddleCenter);
            Stretch(textGo.GetComponent<RectTransform>());
        }

        private void LoadLevel(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            Debug.Log("[Nightborn] 载入关卡：" + sceneName);
            Time.timeScale = 1f;
            SceneManager.LoadScene(sceneName);
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
