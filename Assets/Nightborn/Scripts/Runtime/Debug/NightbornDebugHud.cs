using UnityEngine;
using UnityEngine.InputSystem;
using Nightborn.Player;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// 按 F1 打开的自检浮层：确认「场景对不对、输入有没有读到、相机在哪」。
    /// 灰盒阶段专门用来排查「点 Play 一片黑 / 角色不动」。
    /// </summary>
    public class NightbornDebugHud : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Key toggleKey = Key.F1;

        private PlayerMotor _motor;
        private GUIStyle _style;
        private string _lastError = "";

        private void Awake()
        {
            _motor = FindObjectOfType<PlayerMotor>();
            Application.logMessageReceived += OnLog;
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                _lastError = condition;
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
            string scene = gameObject.scene.name;
            string playerLine = _motor != null
                ? string.Format("玩家 {0}  pos {1}  输入 {2}  速度 {3:F2}  潜行 {4}",
                    _motor.name,
                    _motor.transform.position.ToString("F2"),
                    _motor.RawInput.ToString("F2"),
                    _motor.CurrentSpeed,
                    _motor.IsSneaking)
                : "玩家 <color=#FF6B6B>未找到 PLAYER_Ada</color>";

            string camLine = cam != null
                ? string.Format("相机 {0}  pos {1}  clip {2:F1}-{3:F0}  FOV {4:F0}",
                    cam.name, cam.transform.position.ToString("F2"),
                    cam.nearClipPlane, cam.farClipPlane, cam.fieldOfView)
                : "<color=#FF6B6B>没有 MainCamera（Camera.main 为空）</color>";

            string inputLine = Keyboard.current != null
                ? "Input System 正常（Keyboard.current 可读）"
                : "<color=#FF6B6B>Keyboard.current 为空：Active Input Handling 要设为 Both / New</color>";

            string text = string.Format(
                "<b>Nightborn 自检</b>  Unity {0}  {1:F0} fps\n场景：<b>{2}</b>\n{3}\n{4}\n{5}\n{6}\n<color=#9FA8B0>F1 隐藏本浮层</color>",
                Application.unityVersion,
                1f / Mathf.Max(0.0001f, Time.smoothDeltaTime),
                scene,
                playerLine,
                camLine,
                inputLine,
                string.IsNullOrEmpty(_lastError) ? "无报错" : "<color=#FFB86B>最近错误：" + _lastError + "</color>");

            var rect = new Rect(12, 12, 760, 152);
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16), text, _style);
        }
    }
}
