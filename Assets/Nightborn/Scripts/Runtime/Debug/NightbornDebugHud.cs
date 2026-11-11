#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
using Nightborn.Player;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// F1 自检浮层（M0/M1 已接入检测度、血瓶、血视、遗物）。
    /// 正式 HUD 按 HANDOVER §12 决策 21 是"混合形态"，这个浮层只做开发期读数。
    ///
    /// 整个类用 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包起来：发布构建里连 F1/F2 的按键监听、
    /// `OnGUI` 字符串拼装和 `Application.logMessageReceived` 订阅都不参与编译（HANDOVER §5 第 8 条）。
    /// </summary>
    public class NightbornDebugHud : MonoBehaviour
    {
        [SerializeField] private bool visible = false; // 正式 HUD 在跑，调试浮层默认收起（F1 打开）
        [SerializeField] private Key toggleKey = Key.F1;
        [SerializeField] private Key runTestKey = Key.F2;

        private PlayerMotor _motor;
        private LightExposureProbe _probe;
        private SelfGlow _glow;
        private BloodSystem _blood;
        private DetectionMeter _detection;
        private ShiftForm _shift;
        private BloodSight _sight;
        private Level.LevelFlow _flow;
        private UnityEngine.Camera _cam;
        private GUIStyle _style;
        private string _lastError = "";
        private float _resolveTimer;

        private void Awake()
        {
            Resolve();
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        /// <summary>
        /// 只在 Awake 解析一次会漏：浮层比玩家早生成、或玩家中途被重建时，浮层会永远显示「未找到」。
        /// 这里每秒补一次（找不到才补，找到就什么都不做）——比每帧 FindObjectOfType 便宜得多。
        /// </summary>
        private void Resolve()
        {
            if (_motor == null) _motor = FindObjectOfType<PlayerMotor>();
            if (_probe == null) _probe = FindObjectOfType<LightExposureProbe>();
            if (_glow == null) _glow = FindObjectOfType<SelfGlow>();
            if (_blood == null) _blood = FindObjectOfType<BloodSystem>();
            if (_detection == null) _detection = FindObjectOfType<DetectionMeter>();
            if (_shift == null) _shift = FindObjectOfType<ShiftForm>();
            if (_sight == null) _sight = FindObjectOfType<BloodSight>();
            if (_flow == null) _flow = FindObjectOfType<Level.LevelFlow>();
            _cam = UnityEngine.Camera.main;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) _lastError = condition;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[toggleKey].wasPressedThisFrame) visible = !visible;
                if (kb[runTestKey].wasPressedThisFrame) RunWalkthrough();
            }

            _resolveTimer += Time.unscaledDeltaTime;
            if (_resolveTimer < 1f) return;
            _resolveTimer = 0f;
            if (_motor == null || _flow == null || _cam == null) Resolve();
        }

        /// <summary>F2：跑一遍流程自检（等价于手动挂 WalkthroughTest）。</summary>
        private void RunWalkthrough()
        {
            if (FindObjectOfType<WalkthroughTest>() != null)
            {
                Debug.Log("[TEST] 自检已经在跑，忽略这次 F2");
                return;
            }

            var go = new GameObject("TEST_Walkthrough");
            go.AddComponent<WalkthroughTest>();
            Debug.Log("[TEST] F2 触发自检：" + gameObject.scene.name);
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

            var cam = _cam;
            string playerLine = _motor != null
                ? string.Format("玩家 {0}  pos {1}  速度 {2:F2}  潜行 {3}",
                    _motor.name, _motor.transform.position.ToString("F2"), _motor.CurrentSpeed, _motor.IsSneaking)
                : "<color=#FF6B6B>未找到 PLAYER_Ada</color>";

            string camLine = cam != null
                ? string.Format("相机 {0}  pos {1}  FOV {2:F0}", cam.name, cam.transform.position.ToString("F2"), cam.fieldOfView)
                : "<color=#FF6B6B>没有 MainCamera</color>";

            // 2026-11-12：把「为什么变慢」直接摆出来。以前玩家只会感觉「突然走不动」，
            // 但看不出是灼伤(burn)/硬直(stagger)/被抓冻结(freeze)/吸血(feed)/血储归零(dead) 哪一条。
            string speedLine = _motor != null
                ? (_motor.IsHardLocked
                    ? "<color=#FF6B6B>移速 硬锁（不能动）</color>  " + _motor.SpeedModifierReport()
                    : "移速 " + _motor.SpeedModifierReport())
                : "<color=#FFB86B>未找到 PlayerMotor</color>";

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
                ? string.Format("遗物 {0}/{1}  检查点 {2}  {3}", _flow.Relics, _flow.RelicTotal,
                    _flow.SpawnCheckpointIndex,
                    _flow.IsComplete ? "<color=#7ADF9B>通关</color>" : "")
                : "";

            string perfLine = string.Format("存档写盘 {0} 次（上次 {1:F1}ms）  NavMesh 烘焙 {2:F1}ms × {3}",
                Nightborn.Level.SaveSystem.SaveCount,
                Nightborn.Level.SaveSystem.LastSaveMs,
                Nightborn.Level.NavMeshRuntimeBake.LastBakeMs,
                Nightborn.Level.NavMeshRuntimeBake.BakeCount);

            string inputLine = Keyboard.current != null
                ? "Input System 正常"
                : "<color=#FF6B6B>Keyboard.current 为空</color>";

            string text = string.Format(
                "<b>Nightborn 自检</b>  Unity {0}  {1:F0} fps  场景 <b>{2}</b>\n{3}\n{4}\n{5}\n{6}\n{7}\n{8}\n{9}\n{10}\n{11}\n<color=#9FA8B0>F1 隐藏本浮层　F2 跑一遍流程自检</color>",
                Application.unityVersion,
                1f / Mathf.Max(0.0001f, Time.smoothDeltaTime),
                gameObject.scene.name,
                playerLine, camLine, lightLine, speedLine, bloodLine, detectLine, relicLine, perfLine, inputLine);

            float errorHeight = string.IsNullOrEmpty(_lastError) ? 0f : 18f;
            float panelWidth = Mathf.Min(820f, Screen.width - 24f);
            var rect = new Rect(12, 12, panelWidth, 246 + errorHeight);

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
#endif
