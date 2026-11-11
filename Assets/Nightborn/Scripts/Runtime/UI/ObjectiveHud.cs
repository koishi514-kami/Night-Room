using System.Collections.Generic;
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
    ///
    /// 本轮（UI/HUD 静态审计）改动要点：
    ///  1. 贴边内缩改成**非对称安全区**并按 `Canvas.scaleFactor` 换算：旧实现四边都缩 72 px，
    ///     于是「朝上」的箭头压在顶部目标/教程文案上（文案底边在 132 px），「朝下」的箭头压在
    ///     底部能力三格上（格子占 40–114 px）；换成 1080p 以外分辨率时这两个数字还会继续漂。
    ///  2. 箭头位置/角度加平滑与退化保护：旧实现目标穿过相机平面时方向整体取反 → 箭头 180° 瞬移，
    ///     正对屏幕中心时方向为零 → 强制朝上。现在退化时沿用上一帧方向，并做 ~80 ms 插值。
    ///  3. 教程提示改成**队列**：5.5 s 内连踩两个触发体时旧实现是「后一条直接顶掉前一条」，
    ///     前一条只剩半句；现在排队依次播，队列满 3 条丢最旧。
    ///  4. 所有文案只在数值/阶段变化时重建（旧实现每帧拼 "目标 · xx　NN m"，是 UI 侧最大的每帧 GC 来源）。
    ///  5. 终章第三段补距离显示（BossRitual.DistanceToAltar），距离取整下限 1 m（旧实现贴脸显示 "0 m"）。
    /// </summary>
    public class ObjectiveHud : MonoBehaviour
    {
        public static ObjectiveHud Instance { get; private set; }

        [Header("贴边箭头（画布单位，1080 参考下的像素）")]
        [Tooltip("左右内缩。")]
        [SerializeField] private float screenMargin = 72f;
        [Tooltip("顶部内缩：要避开目标一行（28 px / y −44）与教程文案（24 px / y −92，底边 132），旧值 72 会压字。")]
        [SerializeField] private float topInset = 170f;
        [Tooltip("底部内缩：要避开能力三格（底部 y 40–114），旧值 72 会盖在“药剂”格上。")]
        [SerializeField] private float bottomInset = 150f;
        [Tooltip("箭头位置/角度平滑时间（秒）。0 = 不平滑。")]
        [SerializeField] private float pointerSmooth = 0.08f;

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
        private float _resolveTimer;

        private Color _candle = new Color(0.878f, 0.643f, 0.294f, 1f);
        private Color _mist = new Color(0.478f, 0.541f, 0.561f, 1f);
        private static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        private Font _font;
        private Sprite _arrowSprite;
        private Sprite _dotSprite;

        // 文案缓存：只在数值/阶段变化时重写 Text
        private string _objectiveShown = "";
        private int _objBranch;          // 0 未定 / 1 普通目标 / 2 终章仪式
        private int _objMeters = -999;
        private string _objLabel = "";
        private BossRitual.Stage _objStage = (BossRitual.Stage)(-1);
        private int _objLamps = -1;
        private int _objGates = -1;
        private int _objAltarMeters = -1;

        // 提示队列：连踩两个触发体时不叠字、也不丢句子
        private struct HintEntry
        {
            public string Text;
            public float Duration;
        }

        private const int HintQueueMax = 3;
        private readonly Queue<HintEntry> _hintQueue = new Queue<HintEntry>(HintQueueMax);

        // 箭头平滑状态
        private Vector2 _ptrPos;
        private float _ptrAngle;
        private Vector2 _ptrLastDir = Vector2.up;
        private bool _ptrValid;
        private bool _ptrWasEdge;

        // ---------- 对外入口 ----------

        /// <summary>显示一行教程 / 关键提示（场景里没有 ObjectiveHud 时安全忽略）。</summary>
        public static void ShowHint(string text, float duration = 5f)
        {
            if (Instance == null || string.IsNullOrEmpty(text)) return;
            Instance.EnqueueHint(text, Mathf.Max(0.5f, duration));
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
            // 提示计时先走：玩家对象还没就绪时也不能把已显示的提示冻在屏幕上
            UpdateHint();

            if (_motor == null)
            {
                // 旧实现这里每帧 FindObjectOfType；改成 0.5 s 重试一次
                _resolveTimer -= Time.unscaledDeltaTime;
                if (_resolveTimer > 0f) return;
                _resolveTimer = 0.5f;
                Resolve();
                if (_motor == null) return;
            }

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

            // 1100 宽：右上角的「血之遗物 n / m」从 x=1460 起，旧的 1200 宽 + 溢出换行会和它撞上
            _objective = MakeText(canvasGo.transform, "", 28, _candle, TextAnchor.MiddleCenter);
            var ort = _objective.rectTransform;
            ort.anchorMin = ort.anchorMax = new Vector2(0.5f, 1f);
            ort.pivot = new Vector2(0.5f, 1f);
            ort.sizeDelta = new Vector2(1100f, 44f);
            ort.anchoredPosition = new Vector2(0f, -44f);

            _hint = MakeText(canvasGo.transform, "", 24, _mist, TextAnchor.MiddleCenter);
            var hrt = _hint.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(1100f, 40f);
            hrt.anchoredPosition = new Vector2(0f, -92f);

            _arrow = MakeImage(canvasGo.transform, "HUD_Objective_Arrow", _arrowSprite, Clear);
            _arrow.rectTransform.anchorMin = _arrow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _arrow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _arrow.rectTransform.sizeDelta = new Vector2(48f, 48f); // 贴图是 48 px 三角形，旧值 44 会缩一点

            _pin = MakeImage(canvasGo.transform, "HUD_Objective_Pin", _dotSprite, Clear);
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
                SetObjective("");
                HidePointer();
                return;
            }

            // 终章：文案和导航都跟着仪式阶段走
            if (_ritual != null)
            {
                UpdateRitualObjective();
                return;
            }

            if (_objBranch != 1)
            {
                _objBranch = 1;
                _objMeters = -999;
                _objLabel = "";
            }

            var target = ObjectiveTarget.Resolve(_motor.transform.position);
            if (target == null)
            {
                SetObjective("");
                HidePointer();
                return;
            }

            float distance = Vector3.Distance(_motor.transform.position, target.AimPoint);
            int meters = Mathf.Max(1, Mathf.RoundToInt(distance)); // 贴脸时不再显示 "0 m"
            string label = target.HasLabel ? target.Label : "目标";

            if (meters != _objMeters || label != _objLabel)
            {
                _objMeters = meters;
                _objLabel = label;
                SetObjective("目标 · " + label + "　" + meters + " m");
            }

            PlacePointer(target.AimPoint);
        }

        private void UpdateRitualObjective()
        {
            var stage = _ritual.CurrentStage;
            int lamps = _ritual.LampsDoused;
            int gates = _ritual.GatesPassed;

            int altarMeters = -1;
            if (stage == BossRitual.Stage.Altar)
            {
                float d = _ritual.DistanceToAltar; // 没接线时返回 −1
                if (d >= 0f) altarMeters = Mathf.Max(1, Mathf.RoundToInt(d));
            }

            if (_objBranch != 2 || stage != _objStage || lamps != _objLamps ||
                gates != _objGates || altarMeters != _objAltarMeters)
            {
                _objBranch = 2;
                _objStage = stage;
                _objLamps = lamps;
                _objGates = gates;
                _objAltarMeters = altarMeters;
                SetObjective(RitualText(stage, lamps, gates, altarMeters));
            }

            var point = _ritual.CurrentObjectivePoint;
            if (point == null)
            {
                HidePointer();
                return;
            }
            PlacePointer(point.position + Vector3.up * 1f);
        }

        /// <summary>
        /// 终章文案。条数（3 盏 / 2 道）目前硬编码 —— `BossRitual` 只公开了已完成的计数，
        /// 没公开总数（它的 `holyLamps` 是私有序列化数组）；真要改总数得动 BossRitual，不在本轮范围。
        /// </summary>
        private string RitualText(BossRitual.Stage stage, int lamps, int gates, int altarMeters)
        {
            switch (stage)
            {
                case BossRitual.Stage.HolyLamps:
                    return "目标 · 吹灭 3 盏圣灯　" + lamps + " / 3";
                case BossRitual.Stage.Veils:
                    return "目标 · 用影身穿过两道薄幕　" + gates + " / 2";
                case BossRitual.Stage.Altar:
                    return altarMeters > 0
                        ? "目标 · 喝压光药剂后贴近祭坛　" + altarMeters + " m"
                        : "目标 · 喝压光药剂后贴近祭坛";
                default:
                    return "";
            }
        }

        private void UpdateHint()
        {
            if (_hint == null) return;

            if (_hintRemaining > 0f)
            {
                _hintRemaining -= Time.unscaledDeltaTime;
                if (_hintRemaining <= 0f) _hintRemaining = 0f;
            }

            // 当前这条播完就取队列里的下一条
            if (_hintRemaining <= 0f && _hintQueue.Count > 0)
            {
                var next = _hintQueue.Dequeue();
                StartHint(next.Text, next.Duration);
            }

            if (_hintRemaining <= 0f)
            {
                if (_hint.text.Length != 0) _hint.text = "";
                return;
            }

            if (_hint.text != _hintText) _hint.text = _hintText;

            // 最后 0.8 s 淡出
            var c = _mist;
            c.a = Mathf.Clamp01(_hintRemaining / 0.8f);
            if (_hint.color != c) _hint.color = c;
        }

        private void EnqueueHint(string text, float duration)
        {
            // 同一条正在播：只续时长，不排第二遍
            if (_hintRemaining > 0f && _hintText == text)
            {
                _hintRemaining = duration;
                return;
            }

            if (_hintRemaining <= 0f && _hintQueue.Count == 0)
            {
                StartHint(text, duration);
                return;
            }

            // 已在播或者前面还有排队：排队（满了丢最旧，避免无限堆积）
            if (_hintQueue.Count >= HintQueueMax) _hintQueue.Dequeue();
            _hintQueue.Enqueue(new HintEntry { Text = text, Duration = duration });
        }

        private void StartHint(string text, float duration)
        {
            _hintText = text;
            _hintRemaining = Mathf.Max(0.5f, duration);
            if (_hint != null) _hint.text = text;
        }

        private void SetObjective(string text)
        {
            if (text == _objectiveShown) return;
            _objectiveShown = text;
            if (_objective != null) _objective.text = text;
        }

        private void HidePointer()
        {
            if (_arrow != null && _arrow.color.a > 0f) _arrow.color = Clear;
            if (_pin != null && _pin.color.a > 0f) _pin.color = Clear;
            _ptrValid = false;
            _ptrWasEdge = false;
        }

        /// <summary>
        /// 世界坐标 → 屏幕。屏幕内画圆点；出屏 / 在背后则沿「屏幕中心 → 目标」方向贴边画箭头。
        /// 算法与类注释里链接的参考实现一致；本轮补了非对称安全区、缩放换算、退化保护和插值。
        /// </summary>
        private void PlacePointer(Vector3 world)
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || _canvasRect == null) { HidePointer(); return; }

            Vector3 sp3 = cam.WorldToScreenPoint(world);
            bool behind = Vector3.Dot(cam.transform.forward, world - cam.transform.position) <= 0f;

            // 内缩量按画布单位给，这里换算成屏幕像素：CanvasScaler 生效时 1 画布单位 ≠ 1 像素
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            if (scale <= 0.0001f) scale = 1f;

            float x0 = screenMargin * scale;
            float x1 = Screen.width - screenMargin * scale;
            float y0 = bottomInset * scale;
            float y1 = Screen.height - topInset * scale;
            if (x1 <= x0) { x0 = 1f; x1 = Screen.width - 1f; }
            if (y1 <= y0) { y0 = 1f; y1 = Screen.height - 1f; }

            bool onScreen = !behind &&
                            sp3.x >= x0 && sp3.x <= x1 &&
                            sp3.y >= y0 && sp3.y <= y1;

            if (onScreen)
            {
                var pc = _candle;
                pc.a = 0.85f;
                if (_pin.color != pc) _pin.color = pc;
                if (_arrow.color.a > 0f) _arrow.color = Clear;
                SetScreenPosition(_pin.rectTransform, new Vector2(sp3.x, sp3.y), 0f);
                _ptrValid = false; // 回到屏内：下次出屏重新定位，不做跨模式插值
                _ptrWasEdge = false;
                return;
            }

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // 方向改成在**相机空间**算，不再用「屏幕中心 → 目标」反推：
            // 目标贴近相机平面时 WorldToScreenPoint 会给出 ±1e6 量级的坐标（z≈0），
            // 归一化后可能直接 NaN，箭头会凭空消失。相机空间只是点乘，永远有限。
            Vector3 v = world - cam.transform.position;
            Vector2 dir = new Vector2(
                Vector3.Dot(v, cam.transform.right),
                Vector3.Dot(v, cam.transform.up));
            if (behind) dir = -dir; // 透视投影在相机背后是镜像的，取反才对得上屏幕方向

            if (dir.sqrMagnitude < 1e-6f || !IsFinite(dir))
            {
                // 退化：目标正对屏幕中心 / 正好在相机平面后方 —— 旧实现强制朝上，会瞬间跳到屏幕顶部
                dir = _ptrLastDir.sqrMagnitude > 1e-6f ? _ptrLastDir : Vector2.up;
            }
            dir.Normalize();
            if (IsFinite(dir)) _ptrLastDir = dir; // 别把 NaN 记下来，否则之后每帧都会沿用坏方向

            // 推到内缩矩形的边界上（取先撞到的那条边）
            float dx = Mathf.Max(1f, dir.x > 0f ? x1 - center.x : center.x - x0);
            float dy = Mathf.Max(1f, dir.y > 0f ? y1 - center.y : center.y - y0);
            float tx = Mathf.Abs(dir.x) > 1e-4f ? dx / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 1e-4f ? dy / Mathf.Abs(dir.y) : float.MaxValue;
            Vector2 edge = center + dir * Mathf.Min(tx, ty);

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            // 平滑：只在上一次也是「贴边箭头」时插值，避免从屏内切出去时从旧位置滑一段
            if (_ptrWasEdge && _ptrValid && pointerSmooth > 0.0001f)
            {
                float k = Mathf.Clamp01(1f - Mathf.Exp(-Time.unscaledDeltaTime / pointerSmooth));
                edge = Vector2.Lerp(_ptrPos, edge, k);
                angle = Mathf.LerpAngle(_ptrAngle, angle, k);
            }

            _ptrPos = edge;
            _ptrAngle = angle;
            _ptrValid = true;
            _ptrWasEdge = true;

            if (_pin.color.a > 0f) _pin.color = Clear;
            var ac = _candle;
            ac.a = 0.9f;
            if (_arrow.color != ac) _arrow.color = ac;
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

        private static bool IsFinite(Vector2 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y);
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
