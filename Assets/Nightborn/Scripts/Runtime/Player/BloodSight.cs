using UnityEngine;
using UnityEngine.InputSystem;
using Nightborn.Detection;
using Nightborn.Level;

namespace Nightborn.Player
{
    /// <summary>
    /// 血视（M1）：Q 键 → 3 秒内看穿墙体，标出活物（守卫 / 可吸目标）与血之遗物。
    ///
    /// 设计依据 HANDOVER §12 决策 11：**冷却型，不耗血**（3 秒 / 冷却 25 秒）。
    /// 表现层先用屏幕投影标记顶上（零 Shader 成本），M2 换成 URP RenderObjects 的剪影描边。
    /// </summary>
    [DisallowMultipleComponent]
    public class BloodSight : MonoBehaviour
    {
        [Header("节奏")]
        [SerializeField] private float duration = 3f;
        [SerializeField] private float cooldown = 25f;
        [SerializeField] private Key key = Key.Q;

        [Header("范围")]
        [SerializeField] private float radius = 30f;

        private float _activeTimer;
        private float _cooldownTimer;

        public bool IsActive => _activeTimer > 0f;
        public float ActiveRemaining => Mathf.Max(0f, _activeTimer);
        public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);
        public float Cooldown01 => cooldown <= 0f ? 0f : Mathf.Clamp01(_cooldownTimer / cooldown);

        /// <summary>外部（UI / 测试）主动开启血视，成功返回 true。</summary>
        public bool Activate()
        {
            if (_activeTimer > 0f || _cooldownTimer > 0f) return false;
            _activeTimer = duration;
            _cooldownTimer = cooldown;
            Debug.Log("[Nightborn] 血视开启（" + duration + "s / 冷却 " + cooldown + "s）");
            return true;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_activeTimer > 0f) _activeTimer = Mathf.Max(0f, _activeTimer - dt);
            if (_cooldownTimer > 0f) _cooldownTimer = Mathf.Max(0f, _cooldownTimer - dt);

            var kb = Keyboard.current;
            if (kb == null || !kb[key].wasPressedThisFrame) return;
            if (_activeTimer > 0f || _cooldownTimer > 0f) return;

            _activeTimer = duration;
            _cooldownTimer = cooldown;
            Debug.Log("[Nightborn] 血视开启（" + duration + "s / 冷却 " + cooldown + "s）");
        }

        private void OnGUI()
        {
            if (!IsActive) return;

            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            foreach (var guard in GuardAI.All)
            {
                if (guard == null) continue;
                DrawMarker(cam, guard.transform.position + Vector3.up * 1.2f, new Color(0.95f, 0.2f, 0.25f, 0.95f), 9f);
            }

            foreach (var target in Object.FindObjectsOfType<FeedTarget>())
            {
                if (target == null || target.IsAsleep) continue;
                DrawMarker(cam, target.transform.position + Vector3.up * 1.3f, new Color(0.95f, 0.62f, 0.3f, 0.9f), 7f);
            }

            foreach (var relic in Object.FindObjectsOfType<Relic>())
            {
                if (relic == null || relic.Collected) continue;
                DrawMarker(cam, relic.transform.position, new Color(0.95f, 0.82f, 0.35f, 0.9f), 6f);
            }
        }

        private void DrawMarker(UnityEngine.Camera cam, Vector3 world, Color color, float size)
        {
            if ((world - transform.position).sqrMagnitude > radius * radius) return;

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;

            float y = Screen.height - sp.y;
            var rect = new Rect(sp.x - size * 0.5f, y - size * 0.5f, size, size);

            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
