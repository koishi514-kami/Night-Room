using UnityEngine;
using UnityEngine.UI;
using Nightborn.Boss;
using Nightborn.Level;
using Nightborn.Player;

namespace Nightborn.UI
{
    /// <summary>
    /// 目标提示 + 教程一行文案（HANDOVER §4 P0-1 / P0-2）。
    ///
    ///  · 顶部一行目标：读 <see cref="ObjectiveTarget"/> 里优先级最小的未完成目标，带直线距离；
    ///  · 目标在屏幕内 → 目标处画一个圆点；在屏幕外 / 背后 → 贴边箭头指向它；
    ///  · 终章特殊处理：文案和导航都跟着 <see cref="BossRitual"/> 的阶段走（吹灯 / 穿幕 / 压光）；
    ///  · 教程触发体（<see cref="TutorialHint"/>）踩到 → 目标下面出一行提示，几秒后淡出。
    ///
    /// 全部代码建 UI，不依赖 prefab / 贴图：箭头和圆点都是运行时画出来的。
    ///
    /// 参考实现：https://github.com/Hexloot-Games/hud-Indicator （MIT）
    ///   —— 它 v2 只支持 Unity 6，且靠 prefab + 图标资源驱动；本项目场景与 HUD 全是代码生成、
    ///      美术资产为 0，接进来反而要引入 prefab 依赖（HANDOVER §8.1「耦合过深」），所以只借用
    ///      「世界坐标 → 屏幕坐标 → 出屏贴边」的算法，不引包。
    ///   —— 另一个候选 https://github.com/jinincarnate/off-screen-indicator 是 GPL-3.0，商业发行不适用。
    /// </summary>
    public class ObjectiveHud : MonoBehaviour
    {
        public static ObjectiveHud Instance { get; private set; }

        /// <summary>贴边箭头距屏幕边缘的内缩量（屏幕像素）。</summary>
        [SerializeField] private float screenMargin = 72f;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private Text _objective;
        private Text _hint;
        private Image _arrow;
        private Image _pin;

        private PlayerMotor _motor;
        private LevelFlow _flow;
        private BossRitual _ritual;

        private float _hintRemaining;
        private string _hintText = "";

        private Color _candle = new Color(0.878f, 0.643f, 0.294f, 1f);
        private Color _mist = new Color(0.478f, 0.541f, 0.561f, 1f);

        private Font _font;
        private Sprite _arrowSprite;
        private Sprite _dotSprite;

        // ---------- 对外入口 ----------

        /// <summary>显示一行教程 / 关键提示（场景里没有 ObjectiveHud 时安全忽略）。</summary>
        public static void ShowHint(string text, float duration = 5f)
        {
            if (Instance == null || string.IsNullOrEmpty(text)) return;
            Instance._hintText = text;
            Instance._hintRemaining = Mathf.Max(0.5f, duration);
        }

        // ---------- 生命周期 ----------

        private void Awake()
        {
            Instance = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            Resolve();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Resolve()
        {
            _motor = FindObjectOfType<PlayerMotor>();
            _flow = FindObjectOfType<LevelFlow>();
            _ritual = FindObjectOfType<BossRitual>();
        }

        private void Update()
        {
            if (_motor == null)
            {
                Resolve();
                if (_motor == null) return;
            }

            UpdateHint();
            UpdateObjective();
        }

        // ---------- 建 UI ----------

        private void Build()
        {
            var canvasGo = new GameObject("HUD_Objective_Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 4; // 主 HUD 是 5，红晕与能力格盖在它上面

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRect = canvasGo.GetComponent<RectTransform>();

            _arrowSprite = MakeArrowSprite(48);
            _dotSprite = MakeDotSprite(24);

            _objective = MakeText(canvasGo.transform, "", 28, _candle, TextAnchor.MiddleCenter);
            var ort = _objective.rectTransform;
            ort.anchorMin = ort.anchorMax = new Vector2(0.5f, 1f);
            ort.pivot = new Vector2(0.5f, 1f);
            ort.sizeDelta = new Vector2(1200f, 44f);
            ort.anchoredPosition = new Vector2(0f, -44f);

            _hint = MakeText(canvasGo.transform, "", 24, _mist, TextAnchor.MiddleCenter);
            var hrt = _hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(1400f, 40f);
            hrt.anchoredPosition = new Vector2(0f, -92f);

            _arrow = MakeImage(canvasGo.transform, "HUD_Objective_Arrow", _arrowSprite, new Color(1f, 1f, 1f, 0f));
            _arrow.rectTransform.anchorMin = _arrow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _arrow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _arrow.rectTransform.sizeDelta = new Vector2(44f, 44f);

            _pin = MakeImage(canvasGo.transform, "HUD_Objective_Pin", _dotSprite, new Color(1f, 1f, 1f, 0f));
            _pin.rectTransform.anchorMin = _pin.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _pin.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _pin.rectTransform.sizeDelta = new Vector2(18f, 18f);
        }

        // ---------- 每帧 ----------

        private void UpdateObjective()
        {
            if (_objective == null) return;

            if (_flow != null && _flow.IsComplete)
            {
                _objective.text = "";
                HidePointer();
                return;
            }

            // 终章：文案和导航都跟着仪式阶段走
            if (_ritual != null)
            {
                _objective.text = RitualText();
                var point = _ritual.CurrentObjectivePoint;
                if (point == null) { HidePointer(); return; }
                PlacePointer(point.position + Vector3.up * 1f);
                return;
            }

            var target = ObjectiveTarget.Resolve(_motor.transform.position);
            if (target == null)
            {
                _objective.text = "";
                HidePointer();
                return;
            }

            float distance = Vector3.Distance(_motor.transform.position, target.AimPoint);
            string label = target.HasLabel ? target.Label : "目标";
            _objective.text = "目标 · " + label + "　" + Mathf.RoundToInt(distance) + " m";
            PlacePointer(target.AimPoint);
        }

        private string RitualText()
        {
            switch (_ritual.CurrentStage)
            {
                case BossRitual.Stage.HolyLamps:
                    return "目标 · 吹灭 3 盏圣灯　" + _ritual.LampsDoused + " / 3";
                case BossRitual.Stage.Veils:
                    return "目标 · 用影身穿过两道薄幕　" + _ritual.GatesPassed + " / 2";
                case BossRitual.Stage.Altar:
                    return "目标 · 喝压光药剂后贴近祭坛";
                default:
                    return "";
            }
        }

        private void UpdateHint()
        {
            if (_hint == null) return;

            if (_hintRemaining <= 0f)
            {
                if (_hint.text.Length != 0) _hint.text = "";
                return;
            }

            _hintRemaining -= Time.deltaTime;

            // 最后 0.8 s 淡出
            var c = _mist;
            c.a = Mathf.Clamp01(_hintRemaining / 0.8f);
            _hint.text = _hintText;
            _hint.color = c;
        }

        private void HidePointer()
        {
            if (_arrow != null) _arrow.color = new Color(1f, 1f, 1f, 0f);
            if (_pin != null) _pin.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>
        /// 世界坐标 → 屏幕。屏幕内画圆点；出屏 / 在背后则沿「屏幕中心 → 目标」方向贴边画箭头。
        /// 算法与类注释里链接的参考实现一致。
        /// </summary>
        private void PlacePointer(Vector3 world)
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || _canvasRect == null) { HidePointer(); return; }

            Vector3 sp = cam.WorldToScreenPoint(world);
            bool behind = Vector3.Dot(cam.transform.forward, world - cam.transform.position) <= 0f;

            float margin = screenMargin;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            bool onScreen = !behind &&
                            sp.x >= margin && sp.x <= Screen.width - margin &&
                            sp.y >= margin && sp.y <= Screen.height - margin;

            if (onScreen)
            {
                _pin.color = new Color(_candle.r, _candle.g, _candle.b, 0.85f);
                _arrow.color = new Color(1f, 1f, 1f, 0f);
                SetScreenPosition(_pin.rectTransform, new Vector2(sp.x, sp.y), 0f);
                return;
            }

            // 屏幕中心指向目标的二维方向；目标在背后时取反
            Vector2 dir = behind
                ? new Vector2(center.x - sp.x, center.y - sp.y)
                : new Vector2(sp.x - center.x, sp.y - center.y);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();

            // 推到内缩矩形的边界上（取先撞到的那条边）
            float halfW = Mathf.Max(1f, center.x - margin);
            float halfH = Mathf.Max(1f, center.y - margin);
            float tx = Mathf.Abs(dir.x) > 1e-4f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 1e-4f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
            Vector2 edge = center + dir * Mathf.Min(tx, ty);

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            _pin.color = new Color(1f, 1f, 1f, 0f);
            _arrow.color = new Color(_candle.r, _candle.g, _candle.b, 0.9f);
            SetScreenPosition(_arrow.rectTransform, edge, angle);
        }

        /// <summary>屏幕像素 → 画布内 anchoredPosition（CanvasScaler 生效时不能直接当像素用）。</summary>
        private void SetScreenPosition(RectTransform rt, Vector2 screenPoint, float rotationZ)
        {
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPoint, null, out local))
                rt.anchoredPosition = local;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
        }

        // ---------- 运行时贴图 ----------

        /// <summary>正三角形，尖端朝上（配合 atan2 − 90° 对齐）。四倍超采样做边缘抗锯齿。</summary>
        private Sprite MakeArrowSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var a = new Vector2(size * 0.5f, size * 0.96f);
            var b = new Vector2(size * 0.06f, size * 0.08f);
            var c = new Vector2(size * 0.94f, size * 0.08f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            var p = new Vector2(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f);
                            if (InTriangle(p, a, b, c)) inside++;
                        }
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, inside / 4f));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>圆点：实心点 + 一圈细环，暗背景上更容易认。</summary>
        private Sprite MakeDotSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                    float a = d <= 0.42f ? 1f : (d >= 0.72f && d <= 0.95f ? 0.75f : 0f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        // ---------- 小工具 ----------

        private Image MakeImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private Text MakeText(Transform parent, string content, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
