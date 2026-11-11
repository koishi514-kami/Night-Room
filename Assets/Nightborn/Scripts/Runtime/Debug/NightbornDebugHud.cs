using UnityEngine;
using UnityEngine.InputSystem;
using Nightborn.Player;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// F1 自检浮层（M0/M1 已接入检测度、血瓶、血视、遗物）。
    /// 正式 HUD 按 HANDOVER §12 决策 21 是"混合形态"，这个浮层只做开发期读数。
    /// </summary>
    public class NightbornDebugHud : MonoBehaviour
    {
        [SerializeField] private bool visible = false; // 正式 HUD 在跑，调试浮层默认收起（F1 打开）
        [SerializeField] private Key toggleKey = Key.F1;

        private PlayerMotor _motor;
        private LightExposureProbe _probe;
        private SelfGlow _glow;
        private BloodSystem _blood;
        private DetectionMeter _detection;
        private ShiftForm _shift;
        private BloodSight _sight;
        private Level.LevelFlow _flow;
        private GUIStyle _style;
        private string _lastError = "";

        private void Awake()
        {
            _motor = FindObjectOfType<PlayerMotor>();
            _probe = FindObjectOfType<LightExposureProbe>();
            _glow = FindObjectOfType<SelfGlow>();
            _blood = FindObjectOfType<BloodSystem>();
            _detection = FindObjectOfType<DetectionMeter>();
            _shift = FindObjectOfType<ShiftForm>();
            _sight = FindObjectOfType<BloodSight>();
            _flow = FindObjectOfType<Level.LevelFlow>();
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) _lastError = condition;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.UpperLeft,
                    richText = true
                };
            }

            var cam = UnityEngine.Camera.main;
            string playerLine = _motor != null
                ? string.Format("玩家 {0}  pos {1}  速度 {2:F2}  潜行 {3}",
                    _motor.name, _motor.transform.position.ToString("F2"), _motor.CurrentSpeed, _motor.IsSneaking)
                : "<color=#FF6B6B>未找到 PLAYER_Ada</color>";

            string camLine = cam != null
                ? string.Format("相机 {0}  pos {1}  FOV {2:F0}", cam.name, cam.transform.position.ToString("F2"), cam.fieldOfView)
                : "<color=#FF6B6B>没有 MainCamera</color>";

            string lightLine = _probe != null
                ? string.Format("外光 {0:F2}  {1}  自体光 {2}{3}",
                    _probe.LightLevel,
                    _probe.IsBurning ? "<color=#FF6B6B>灼伤</color>" : "<color=#7ADF9B>安全</color>",
                    _glow != null && _glow.IsGlowing ? "<color=#E0A44B>亮</color>" : "<color=#9FA8B0>灭</color>",
                    _shift != null && _shift.IsShadow ? "  <color=#B07BFF>影身 " + _shift.ShadowRemaining.ToString("F1") + "s</color>" : "")
                : "<color=#FFB86B>未找到 LightExposureProbe</color>";

            string sightText = _sight == null
                ? "?"
                : _sight.IsActive
                    ? "<color=#FF6B6B>开启 " + _sight.ActiveRemaining.ToString("F1") + "s</color>"
                    : _sight.CooldownRemaining > 0f
                        ? "冷却 " + _sight.CooldownRemaining.ToString("F0") + "s"
                        : "<color=#7ADF9B>就绪</color>";

            string bloodLine = _blood != null
                ? string.Format("血储 {0:F1}/{1:F0}  血瓶 {2}  血视 {3}{4}",
                    _blood.Current, _blood.Max, _blood.Vials, sightText,
                    _blood.IsDead ? "  <color=#FF6B6B>死亡</color>" : "")
                : "<color=#FFB86B>未找到 BloodSystem</color>";

            string detectLine = _detection != null
                ? string.Format("检测 {0:F1}/{1:F0}  {2}", _detection.Detection, _detection.Max, DetectColor(_detection.State))
                : "<color=#FFB86B>未找到 DetectionMeter</color>";

            string relicLine = _flow != null
                ? string.Format("遗物 {0}/{1}  {2}", _flow.Relics, _flow.RelicTotal,
                    _flow.IsComplete ? "<color=#7ADF9B>通关</color>" : "")
                : "";

            string inputLine = Keyboard.current != null
                ? "Input System 正常"
                : "<color=#FF6B6B>Keyboard.current 为空</color>";

            string text = string.Format(
                "<b>Nightborn 自检</b>  Unity {0}  {1:F0} fps  场景 <b>{2}</b>\n{3}\n{4}\n{5}\n{6}\n{7}\n{8}\n{9}\n<color=#9FA8B0>F1 隐藏本浮层</color>",
                Application.unityVersion,
                1f / Mathf.Max(0.0001f, Time.smoothDeltaTime),
                gameObject.scene.name,
                playerLine, camLine, lightLine, bloodLine, detectLine, relicLine, inputLine);

            float errorHeight = string.IsNullOrEmpty(_lastError) ? 0f : 18f;
            float panelWidth = Mathf.Min(820f, Screen.width - 24f);
            var rect = new Rect(12, 12, panelWidth, 210 + errorHeight);

            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 36), text, _style);

            if (!string.IsNullOrEmpty(_lastError))
                GUI.Label(new Rect(rect.x + 10, rect.y + rect.height - 40, rect.width - 20, 18),
                    "<color=#FFB86B>最近错误：" + _lastError + "</color>", _style);

            float barY = rect.y + rect.height - 16;
            DrawBar(new Rect(rect.x + 10, barY, rect.width - 20, 5),
                _blood != null ? _blood.Normalized : 0f, new Color(0.55f, 0.12f, 0.16f, 0.95f));
            DrawBar(new Rect(rect.x + 10, barY + 8, rect.width - 20, 5),
                _detection != null ? _detection.Normalized : 0f, DetectBarColor(_detection));
        }

        private static void DrawBar(Rect rect, float fill, Color color)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.14f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            if (fill > 0f)
            {
                GUI.color = color;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
        }

        private static string DetectColor(DetectionMeter.DetectionState state)
        {
            switch (state)
            {
                case DetectionMeter.DetectionState.Hunted: return "<color=#FF4D4D>围猎</color>";
                case DetectionMeter.DetectionState.Suspicious: return "<color=#E0A44B>可疑</color>";
                default: return "<color=#7ADF9B>平静</color>";
            }
        }

        private static Color DetectBarColor(DetectionMeter meter)
        {
            if (meter == null) return new Color(0.4f, 0.6f, 0.6f, 0.95f);
            switch (meter.State)
            {
                case DetectionMeter.DetectionState.Hunted: return new Color(1f, 0.3f, 0.3f, 0.95f);
                case DetectionMeter.DetectionState.Suspicious: return new Color(0.88f, 0.64f, 0.29f, 0.95f);
                default: return new Color(0.48f, 0.87f, 0.61f, 0.95f);
            }
        }
    }
}
