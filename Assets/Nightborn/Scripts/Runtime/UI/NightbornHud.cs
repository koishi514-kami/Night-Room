using UnityEngine;
using UnityEngine.UI;
using Nightborn.Detection;
using Nightborn.Player;
using Nightborn.Level;

namespace Nightborn.UI
{
    /// <summary>
    /// 正式 HUD（HANDOVER §12 决策 21 的"混合形态"）：
    ///
    ///  · 血储  → 屏幕边缘红晕 + 心跳脉动（不显示数字）；
    ///  · 检测  → 敌人头顶的圆点提示（白 → 烛黄 → 血红）+ 贴边冷灰警戒框，屏幕整体不挡视线；
    ///  · 能力  → 底部中央三个小格：化形(Tab) / 血视(Q) / 药剂(1)，带冷却读数；
    ///  · 遗物  → 右上角计数；
    ///  · 通关  → 中央提示"按 Esc 回菜单"。
    ///
    /// 全部用代码建，不依赖 prefab / 贴图资源（径向渐变、圆点、边框都是运行时生成的）。
    ///
    /// 本轮（UI/HUD 静态审计）改动要点：
    ///  1. 红晕系数 0.75 → 0.5 + 下限 0.05 + 上限 0.62（HANDOVER §5 第 5 条：26% 血不再糊屏）；
    ///  2. 检测度不再和血储共用一路红晕 —— 单独一层**冷灰贴边框**，颜色 / 形状 / 节奏都不同；
    ///  3. 敌人标记补「出屏」与「被墙挡」两个隐藏条件（旧实现隔墙也能看到头顶点，等于白送情报）；
    ///  4. 化形格改为跟 `ShiftForm` 的真实状态对齐（影身进行中 / 硬直 / 冷却 / 就绪）；
    ///  5. 所有 Text 只在数值变化时写入，去掉每帧字符串拼接（GC）。
    /// </summary>
    public class NightbornHud : MonoBehaviour
    {
        [Header("敌人头顶标记")]
        [SerializeField] private float markerMaxDistance = 40f;
        [Tooltip("标记挂的世界高度（相对敌人原点）。")]
        [SerializeField] private float markerHeight = 2.1f;
        [Tooltip("出屏判定余量（屏幕像素）。")]
        [SerializeField] private float markerScreenMargin = 16f;

        [Header("边缘红晕（血储）")]
        [Tooltip("系数：(1−血) × 该值。0.75 时 26% 血 alpha 到 0.55，整屏发糊；改 0.5。")]
        [SerializeField] private float vignetteScale = 0.5f;
        [Tooltip("红晕下限：满血时留一层极淡的边，让画面基调一致。")]
        [SerializeField] private float vignetteMin = 0.05f;
        [Tooltip("红晕上限：再低血也不能盖住画面。")]
        [SerializeField] private float vignetteMax = 0.62f;

        [Header("贴边警戒框（检测度）")]
        [Tooltip("检测度 100 时的边框 alpha（用冷灰 #7A8A8F，与血储的血红分开）。")]
        [SerializeField] private float huntFrameMax = 0.45f;
        [Tooltip("被围猎时的边框脉动频率（Hz）。血储心跳是低频闷跳，这里是快频告警。")]
        [SerializeField] private float huntPulseHz = 2.2f;

        [Header("灼伤通道（光里在烧）")]
        [SerializeField] private float burnFrameMax = 0.24f;
        [SerializeField] private float burnPulseHz = 0.9f;

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private Image _vignette;
        private Image _huntFrame;
        private Image _burnFrame;
        private Image[] _markers;
        private Image[] _slotFill;
        private Text[] _slotText;
        private Text _relicText;
        private Text _banner;

        private PlayerMotor _motor;
        private BloodSystem _blood;
        private LightExposureProbe _probe;
        private DetectionMeter _detection;
        private ShiftForm _shift;
        private BloodSight _sight;
        private DimPotion _potion;
        private PlayerFeeder _feeder;
        private LevelFlow _flow;

        private Color _candle = new Color(0.878f, 0.643f, 0.294f, 1f);
        private Color _bloodRed = new Color(0.62f, 0.10f, 0.14f, 1f);
        private Color _ink = new Color(0.055f, 0.059f, 0.071f, 1f);
        private Color _mist = new Color(0.478f, 0.541f, 0.561f, 1f);
        private Color _markerCalm = new Color(0.85f, 0.85f, 0.85f, 1f);
        private Color _markerAlert = new Color(1f, 0.2f, 0.2f, 1f);
        private Color _markerAsleep = new Color(0.4f, 0.45f, 0.5f, 1f);
        private static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        private Font _font;
        private Sprite _circle;
        private Sprite _vignetteSprite;
        private Sprite _huntFrameSprite;
        private Sprite _slotSprite;

        // 能力格状态缓存：只在（模式, 秒数）变化时重写 Text，避免每帧拼字符串
        private static readonly string[] SlotLabels = { "化形", "血视", "药剂" };
        private static readonly string[] SlotKeys = { "Tab", "Q", "1" };
        private readonly int[] _slotMode = { -99, -99, -99 };
        private readonly int[] _slotSecond = { -99, -99, -99 };

        private string _relicShown = "";
        private string _bannerShown = " ";
        private float _resolveTimer;

        private const int ModeReady = 0;    // 就绪 → 显示按键
        private const int ModeActive = 1;   // 进行中 → 显示剩余秒
        private const int ModeStagger = 2;  // 化形硬直
        private const int ModeCooldown = 3; // 冷却 → 显示剩余秒
        private const int ModeCount = 4;    // 药剂 → 显示剩余瓶数

        private void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build();
            Resolve();
        }

        private void Resolve()
        {
            _motor = FindObjectOfType<PlayerMotor>();
            if (_motor == null) return;
            _blood = _motor.GetComponent<BloodSystem>();
            _probe = _motor.GetComponent<LightExposureProbe>();
            _detection = _motor.GetComponent<DetectionMeter>();
            _shift = _motor.GetComponent<ShiftForm>();
            _sight = _motor.GetComponent<BloodSight>();
            _potion = _motor.GetComponent<DimPotion>();
            _feeder = _motor.GetComponent<PlayerFeeder>();
            _flow = FindObjectOfType<LevelFlow>();
        }

        private void Build()
        {
            var canvasGo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5;
            _canvasRect = canvasGo.GetComponent<RectTransform>();

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _vignetteSprite = MakeVignetteSprite(256, 0.42f, 0.45f);
            _huntFrameSprite = MakeVignetteSprite(256, 0.74f, 0.22f);
            _circle = MakeCircleSprite(64);
            _slotSprite = MakeRoundedSprite(64);

            _vignette = MakeImage(canvasGo.transform, "HUD_Vignette", _vignetteSprite, Clear);
            Stretch(_vignette.rectTransform);

            // 检测度单独一层：窄边框（形状）+ 冷灰（颜色）+ 快频脉动（节奏），和血储的宽红晕区分开
            _huntFrame = MakeImage(canvasGo.transform, "HUD_HuntFrame", _huntFrameSprite, Clear);
            Stretch(_huntFrame.rectTransform);

            // 灼伤单独一层：同样是窄边框，但用暖琥珀（烛色）+ 0.9 Hz 慢脉动
            _burnFrame = MakeImage(canvasGo.transform, "HUD_BurnFrame", _huntFrameSprite, Clear);
            Stretch(_burnFrame.rectTransform);

            _markers = new Image[8];
            for (int i = 0; i < _markers.Length; i++)
            {
                var m = MakeImage(canvasGo.transform, "HUD_Marker_" + i, _circle, Clear);
                var mrt = m.rectTransform;
                mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 0.5f);
                mrt.pivot = new Vector2(0.5f, 0.5f);
                mrt.sizeDelta = new Vector2(16f, 16f);
                mrt.anchoredPosition = new Vector2(-100f, -100f);
                _markers[i] = m;
            }

            // 能力槽
            _slotFill = new Image[3];
            _slotText = new Text[3];
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 150f;
                var slot = MakeImage(canvasGo.transform, "HUD_Slot_" + i, _slotSprite, new Color(_ink.r, _ink.g, _ink.b, 0.55f));
                var rt = slot.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(128f, 74f);
                rt.anchoredPosition = new Vector2(x, 40f);

                var fill = MakeImage(slot.transform, "Fill", _slotSprite, new Color(_candle.r, _candle.g, _candle.b, 0.18f));
                Stretch(fill.rectTransform);

                _slotFill[i] = fill;
                _slotText[i] = MakeText(slot.transform, SlotLabels[i] + "\n" + SlotKeys[i], 20, Color.white, TextAnchor.MiddleCenter);
                Stretch(_slotText[i].rectTransform);
            }

            _relicText = MakeText(canvasGo.transform, "", 24, _candle, TextAnchor.MiddleRight);
            var rrt = _relicText.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(1f, 1f);
            rrt.sizeDelta = new Vector2(420f, 40f);
            // y −36 → −140：旧位置和目标一行（顶部 44–88）、教程文案（92–132）处在同一条水平带上，
            // 文案一长就会在右上角互相压字；挪到教程文案下面，纵向彻底让开。
            rrt.anchoredPosition = new Vector2(-40f, -140f);

            _banner = MakeText(canvasGo.transform, "", 46, _candle, TextAnchor.MiddleCenter);
            var brt = _banner.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(1200f, 90f);
            brt.anchoredPosition = new Vector2(0f, 210f);
        }

        private void Update()
        {
            if (_motor == null)
            {
                // 不再每帧 FindObjectOfType：HUD 由 NightbornRuntimeBoot 在场景加载后建，
                // 万一玩家还没生成，就每 0.5 s 重试一次（旧实现是每帧全场景查找）。
                _resolveTimer -= Time.unscaledDeltaTime;
                if (_resolveTimer > 0f) return;
                _resolveTimer = 0.5f;
                Resolve();
                return;
            }

            UpdateVignette();
            UpdateBurnFrame();
            UpdateMarkers();
            UpdateSlots();
            UpdateTexts();
        }

        private void UpdateVignette()
        {
            // ---- 血储通道：宽红晕，慢心跳 ----
            if (_blood != null && _vignette != null)
            {
                float health = _blood.Normalized;
                float baseAlpha = Mathf.Max(vignetteMin, Mathf.Clamp01(1f - health) * vignetteScale);

                // 低血量心跳：0.35 以下开始脉动
                float pulse = 0f;
                if (health < 0.35f)
                {
                    float t = 1f - health / 0.35f;
                    pulse = Mathf.Sin(Time.time * Mathf.Lerp(4f, 9f, t)) * 0.08f * t;
                }

                var c = _bloodRed;
                c.a = Mathf.Clamp(baseAlpha + pulse, vignetteMin, vignetteMax);
                if (_vignette.color != c) _vignette.color = c;
            }

            // ---- 检测通道：窄冷灰边框，快频告警（>60 才出现） ----
            if (_huntFrame != null)
            {
                float hunted = 0f;
                if (_detection != null)
                    hunted = Mathf.Clamp01((_detection.Detection - 60f) / 40f);

                if (hunted <= 0.001f)
                {
                    if (_huntFrame.color.a > 0f) _huntFrame.color = Clear;
                }
                else
                {
                    float beat = 0.72f + 0.28f * Mathf.Sin(Time.time * huntPulseHz * Mathf.PI * 2f);
                    var hc = _mist;
                    hc.a = huntFrameMax * hunted * beat;
                    if (_huntFrame.color != hc) _huntFrame.color = hc;
                }
            }
        }

        /// <summary>
        /// 灼伤通道（2026-11-12 新增）：站在光里会掉血 + 移速 ×0.45，但以前没有任何画面提示，
        /// 玩家只会觉得「怎么突然变慢、走不动」。这里用一条**暖琥珀色**的窄边框（与「围猎」的
        /// 冷灰窄框在色相上分得开）慢速脉动，作为「你在被烧」的读秒提示。
        /// </summary>
        private void UpdateBurnFrame()
        {
            if (_burnFrame == null) return;

            bool burning = _probe != null && _probe.IsBurning;
            if (!burning)
            {
                if (_burnFrame.color.a > 0f) _burnFrame.color = Clear;
                return;
            }

            float beat = 0.78f + 0.22f * Mathf.Sin(Time.time * burnPulseHz * Mathf.PI * 2f);
            var c = _candle;
            c.a = burnFrameMax * beat;
            if (_burnFrame.color != c) _burnFrame.color = c;
        }

        private void UpdateMarkers()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null || _markers == null || _motor == null) return;

            var guards = GuardAI.All;
            for (int i = 0; i < _markers.Length; i++)
            {
                var img = _markers[i];
                if (img == null) continue;

                GuardAI guard = i < guards.Count ? guards[i] : null;
                if (guard == null || !TryGetMarkerScreenPoint(cam, guard, out Vector3 screen))
                {
                    if (img.enabled)
                    {
                        img.color = Clear;
                        img.enabled = false; // 只在状态切换时动一次，不是每帧 SetActive
                    }
                    continue;
                }

                if (!img.enabled) img.enabled = true;

                float p = Mathf.Clamp01(guard.Progress);
                // 白 → 烛黄 → 血红（HANDOVER 的设计描述是三段，旧实现是白直接到红）
                Color col = p < 0.5f
                    ? Color.Lerp(_markerCalm, _candle, p * 2f)
                    : Color.Lerp(_candle, _markerAlert, (p - 0.5f) * 2f);
                col.a = Mathf.Lerp(0.55f, 1f, p);
                if (guard.IsDormant)
                {
                    col = _markerAsleep;
                    col.a = 0.35f;
                }

                if (img.color != col) img.color = col;

                Vector2 size = Vector2.one * Mathf.Lerp(14f, 26f, p);
                if (img.rectTransform.sizeDelta != size) img.rectTransform.sizeDelta = size;

                // 屏幕像素 → 画布局部坐标（CanvasScaler 生效时两者不成正比，不能直接把像素当坐标用）
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out Vector2 local))
                    img.rectTransform.anchoredPosition = local;
            }
        }

        /// <summary>标记是否该画：在射程内、在屏幕内、且没有被墙挡。</summary>
        private bool TryGetMarkerScreenPoint(UnityEngine.Camera cam, GuardAI guard, out Vector3 screen)
        {
            screen = Vector3.zero;

            Vector3 guardPos = guard.transform.position;
            if ((guardPos - _motor.transform.position).sqrMagnitude > markerMaxDistance * markerMaxDistance)
                return false;

            Vector3 world = guardPos + Vector3.up * markerHeight;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return false;
            if (sp.x < -markerScreenMargin || sp.x > Screen.width + markerScreenMargin) return false;
            if (sp.y < -markerScreenMargin || sp.y > Screen.height + markerScreenMargin) return false;

            // 遮挡：从相机往标记点打一条线，命中的既不是「这个敌人」也不是「玩家自己」就是被墙挡住了。
            // 旧实现没有这一步，隔墙也能看到头顶点 —— 等于把潜行信息白送给玩家。
            if (Physics.Linecast(cam.transform.position, world, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = hit.transform;
                bool self = t == guard.transform || t.IsChildOf(guard.transform);
                bool player = t == _motor.transform || t.IsChildOf(_motor.transform);
                if (!self && !player) return false;
            }

            screen = sp;
            return true;
        }

        private void UpdateSlots()
        {
            if (_slotFill == null) return;
            UpdateShiftSlot();
            UpdateSightSlot();
            UpdatePotionSlot();
        }

        /// <summary>
        /// 化形格。真实状态有四种：影身进行中 / 硬直 / 冷却 / 就绪。
        /// 旧实现只看 `CooldownRemaining`：影身进行中（冷却还是 0）会显示"就绪"，硬直时显示"0s"。
        /// </summary>
        private void UpdateShiftSlot()
        {
            int mode = ModeReady;
            int sec = 0;
            Color fill;

            if (_shift == null)
            {
                fill = Fade(_candle, 0.10f);
            }
            else if (_shift.IsShadow)
            {
                mode = ModeActive;
                sec = Mathf.Max(1, Mathf.CeilToInt(_shift.ShadowRemaining));
                fill = new Color(_ink.r, _ink.g, _ink.b, 0.85f);
            }
            else if (_shift.IsStaggered)
            {
                mode = ModeStagger;
                fill = Fade(_bloodRed, 0.45f);
            }
            else
            {
                float cd = _shift.CooldownRemaining;
                if (cd > 0.001f)
                {
                    mode = ModeCooldown;
                    sec = Mathf.CeilToInt(cd);
                    fill = Fade(_candle, 0.10f);
                }
                else fill = Fade(_candle, 0.30f);
            }

            SetSlot(0, mode, sec, fill);
        }

        private void UpdateSightSlot()
        {
            int mode = ModeReady;
            int sec = 0;
            Color fill;

            if (_sight == null)
            {
                fill = Fade(_candle, 0.10f);
            }
            else if (_sight.IsActive)
            {
                mode = ModeActive;
                sec = Mathf.Max(1, Mathf.CeilToInt(_sight.ActiveRemaining));
                fill = Fade(_bloodRed, 0.55f);
            }
            else
            {
                float cd = _sight.CooldownRemaining;
                if (cd > 0.001f)
                {
                    mode = ModeCooldown;
                    sec = Mathf.CeilToInt(cd);
                    fill = Fade(_candle, 0.10f);
                }
                else fill = Fade(_candle, 0.30f);
            }

            SetSlot(1, mode, sec, fill);
        }

        private void UpdatePotionSlot()
        {
            int mode;
            int sec;
            Color fill;

            if (_potion == null)
            {
                mode = ModeReady;
                sec = 0;
                fill = Fade(_candle, 0.10f);
            }
            else if (_potion.IsActive)
            {
                mode = ModeActive;
                sec = Mathf.Max(1, Mathf.CeilToInt(_potion.Remaining));
                fill = Fade(_mist, 0.45f); // 旧值紫色 (0.55,0.4,0.95) 不在五色板里
            }
            else
            {
                mode = ModeCount;
                sec = _potion.Count;
                fill = Fade(_candle, sec > 0 ? 0.30f : 0.10f);
            }

            SetSlot(2, mode, sec, fill);
        }

        private void SetSlot(int i, int mode, int seconds, Color fill)
        {
            if (_slotMode[i] != mode || _slotSecond[i] != seconds)
            {
                _slotMode[i] = mode;
                _slotSecond[i] = seconds;

                string sub;
                switch (mode)
                {
                    case ModeActive:
                    case ModeCooldown: sub = seconds + "s"; break;
                    case ModeStagger: sub = "硬直"; break;
                    case ModeCount: sub = "x" + seconds; break;
                    default: sub = SlotKeys[i]; break;
                }
                _slotText[i].text = SlotLabels[i] + "\n" + sub;
            }

            if (_slotFill[i].color != fill) _slotFill[i].color = fill;
        }

        private void UpdateTexts()
        {
            if (_relicText != null && _flow != null)
            {
                string relics = "血之遗物 " + _flow.Relics + " / " + _flow.RelicTotal;
                if (relics != _relicShown)
                {
                    _relicShown = relics;
                    _relicText.text = relics;
                }
                _relicText.color = _flow.Relics > 0 ? _candle : _mist;
            }

            if (_banner != null && _flow != null)
            {
                string banner = "";
                Color bannerColor = _candle;

                if (_flow.IsComplete)
                {
                    banner = "通关　—　Esc 返回菜单";
                }
                else if (_feeder != null && _feeder.IsFeeding)
                {
                    banner = "吸血中…";
                    bannerColor = _bloodRed;
                }

                if (banner != _bannerShown)
                {
                    _bannerShown = banner;
                    _banner.text = banner;
                }
                _banner.color = bannerColor;
            }
        }

        private static Color Fade(Color c, float alpha)
        {
            c.a = alpha;
            return c;
        }

        // ---------- 运行时贴图 ----------

        /// <summary>径向渐变：inner 以内全透明，inner + softness 处满不透明。</summary>
        private Sprite MakeVignetteSprite(int size, float inner, float softness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - inner) / Mathf.Max(0.01f, softness)));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private Sprite MakeCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(half, half)) / half;
                    float a = d <= 0.78f ? 1f : Mathf.Clamp01((1f - d) / 0.22f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private Sprite MakeRoundedSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            float radius = size * 0.22f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = Mathf.Abs(x - half) - (half - radius);
                    float py = Mathf.Abs(y - half) - (half - radius);
                    float outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude;
                    float a = outside <= radius ? 1f : 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

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

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
