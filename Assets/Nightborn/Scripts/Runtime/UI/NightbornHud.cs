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
    ///  · 检测  → 敌人头顶的圆点提示（白 → 黄 → 红），屏幕整体不挡视线；
    ///  · 能力  → 底部中央三个小格：化形(Tab) / 血视(Q) / 药剂(1)，带冷却读数；
    ///  · 遗物  → 右上角计数；
    ///  · 通关  → 中央提示"按 Esc 回菜单"。
    ///
    /// 全部用代码建，不依赖 prefab / 贴图资源（径向渐变与圆点都是运行时生成的）。
    /// </summary>
    public class NightbornHud : MonoBehaviour
    {
        [SerializeField] private float markerMaxDistance = 40f;

        private Canvas _canvas;
        private Image _vignette;
        private Image[] _markers;
        private GuardAI[] _markerOwners;
        private Image[] _slotFill;
        private Text[] _slotText;
        private Text _relicText;
        private Text _banner;

        private PlayerMotor _motor;
        private BloodSystem _blood;
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

        private Font _font;
        private Sprite _circle;
        private Sprite _vignetteSprite;
        private Sprite _slotSprite;

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

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _vignetteSprite = MakeVignetteSprite(256);
            _circle = MakeCircleSprite(64);
            _slotSprite = MakeRoundedSprite(64);

            _vignette = MakeImage(canvasGo.transform, "HUD_Vignette", _vignetteSprite, new Color(1, 1, 1, 0));
            Stretch(_vignette.rectTransform);

            _markers = new Image[8];
            _markerOwners = new GuardAI[8];
            for (int i = 0; i < _markers.Length; i++)
            {
                var m = MakeImage(canvasGo.transform, "HUD_Marker_" + i, _circle, new Color(1, 1, 1, 0));
                m.rectTransform.sizeDelta = new Vector2(16f, 16f);
                m.transform.position = new Vector3(-100f, -100f, 0f);
                _markers[i] = m;
            }

            // 能力槽
            string[] labels = { "化形\nTab", "血视\nQ", "药剂\n1" };
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
                _slotText[i] = MakeText(slot.transform, labels[i], 20, Color.white, TextAnchor.MiddleCenter);
                Stretch(_slotText[i].rectTransform);
            }

            _relicText = MakeText(canvasGo.transform, "", 24, _candle, TextAnchor.MiddleRight);
            var rrt = _relicText.rectTransform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(1f, 1f);
            rrt.sizeDelta = new Vector2(420f, 40f);
            rrt.anchoredPosition = new Vector2(-40f, -36f);

            _banner = MakeText(canvasGo.transform, "", 46, _candle, TextAnchor.MiddleCenter);
            var brt = _banner.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(1200f, 90f);
            brt.anchoredPosition = new Vector2(0f, 210f);
        }

        private void Update()
        {
            if (_motor == null) { Resolve(); return; }

            UpdateVignette();
            UpdateMarkers();
            UpdateSlots();
            UpdateTexts();
        }

        private void UpdateVignette()
        {
            if (_blood == null || _vignette == null) return;

            float health = _blood.Normalized;
            float baseAlpha = Mathf.Clamp01(1f - health) * 0.75f;

            // 低血量心跳：0.35 以下开始脉动
            float pulse = 0f;
            if (health < 0.35f)
            {
                float t = 1f - health / 0.35f;
                pulse = Mathf.Sin(Time.time * Mathf.Lerp(4f, 9f, t)) * 0.12f * t;
            }

            // 被盯上时再叠一层红
            float hunted = 0f;
            if (_detection != null)
                hunted = Mathf.Clamp01((_detection.Detection - 60f) / 40f) * 0.25f;

            var c = _bloodRed;
            c.a = Mathf.Clamp01(baseAlpha + pulse + hunted);
            _vignette.color = c;
        }

        private void UpdateMarkers()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            var guards = GuardAI.All;
            for (int i = 0; i < _markers.Length; i++)
            {
                if (i >= guards.Count || guards[i] == null)
                {
                    _markers[i].color = new Color(1, 1, 1, 0);
                    continue;
                }

                var guard = guards[i];
                Vector3 world = guard.transform.position + Vector3.up * 2.1f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool visible = sp.z > 0f && Vector3.Distance(_motor.transform.position, guard.transform.position) < markerMaxDistance;

                if (!visible)
                {
                    _markers[i].color = new Color(1, 1, 1, 0);
                    continue;
                }

                float p = guard.Progress;
                Color col = Color.Lerp(new Color(0.85f, 0.85f, 0.85f, 0.55f), new Color(1f, 0.2f, 0.2f, 1f), p);
                if (guard.IsDormant) col = new Color(0.4f, 0.45f, 0.5f, 0.35f);

                _markers[i].color = col;
                _markers[i].rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(14f, 26f, p);
                _markers[i].transform.position = new Vector3(sp.x, sp.y, 0f);
            }
        }

        private void UpdateSlots()
        {
            if (_slotFill == null) return;

            // 化形
            float shiftCd = _shift != null ? _shift.CooldownRemaining : 0f;
            bool shiftReady = shiftCd <= 0.001f && (_shift == null || !_shift.IsStaggered);
            _slotFill[0].color = new Color(_candle.r, _candle.g, _candle.b, shiftReady ? 0.30f : 0.10f);
            _slotText[0].text = shiftReady ? "化形\nTab" : "化形\n" + Mathf.CeilToInt(shiftCd) + "s";

            // 血视
            float sightCd = _sight != null ? _sight.CooldownRemaining : 0f;
            bool sightOn = _sight != null && _sight.IsActive;
            _slotFill[1].color = sightOn
                ? new Color(1f, 0.25f, 0.25f, 0.45f)
                : new Color(_candle.r, _candle.g, _candle.b, sightCd <= 0.001f ? 0.30f : 0.10f);
            _slotText[1].text = sightOn ? "血视\n开" : sightCd > 0.001f ? "血视\n" + Mathf.CeilToInt(sightCd) + "s" : "血视\nQ";

            // 药剂
            int potions = _potion != null ? _potion.Count : 0;
            bool potionOn = _potion != null && _potion.IsActive;
            _slotFill[2].color = potionOn
                ? new Color(0.55f, 0.4f, 0.95f, 0.45f)
                : new Color(_candle.r, _candle.g, _candle.b, potions > 0 ? 0.30f : 0.10f);
            _slotText[2].text = potionOn ? "药剂\n" + Mathf.CeilToInt(_potion.Remaining) + "s" : "药剂\nx" + potions;
        }

        private void UpdateTexts()
        {
            if (_relicText != null && _flow != null)
            {
                _relicText.text = "血之遗物 " + _flow.Relics + " / " + _flow.RelicTotal;
                _relicText.color = _flow.Relics > 0 ? _candle : _mist;
            }

            if (_banner != null && _flow != null)
            {
                if (_flow.IsComplete)
                {
                    _banner.text = "通关　—　Esc 返回菜单";
                    _banner.color = _candle;
                }
                else if (_feeder != null && _feeder.IsFeeding)
                {
                    _banner.text = "吸血中…";
                    _banner.color = _bloodRed;
                }
                else
                {
                    _banner.text = "";
                }
            }
        }

        // ---------- 运行时贴图 ----------

        private Sprite MakeVignetteSprite(int size)
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
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.42f) / 0.45f));
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
