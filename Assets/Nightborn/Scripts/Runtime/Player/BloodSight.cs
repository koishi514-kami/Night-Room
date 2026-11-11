using System.Collections.Generic;
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

        [Header("刷新")]
        [Tooltip("标记刷新间隔（秒）。血视期间全场扫描一次是有分配的，不能每帧都扫。")]
        [SerializeField] private float markerRefresh = 0.25f;

        private struct Mark
        {
            public Vector3 Position;
            public Color Tint;
            public float Size;
        }

        private float _activeTimer;
        private float _cooldownTimer;
        private float _refreshTimer;
        private UnityEngine.Camera _cam;
        private readonly List<Mark> _marks = new List<Mark>();

        public bool IsActive => _activeTimer > 0f;
        public float ActiveRemaining => Mathf.Max(0f, _activeTimer);
        public float CooldownRemaining => Mathf.Max(0f, _cooldownTimer);
        public float Cooldown01 => cooldown <= 0f ? 0f : Mathf.Clamp01(_cooldownTimer / cooldown);

        /// <summary>外部（UI / 测试）主动开启血视，成功返回 true。</summary>
        public bool Activate()
        {
            if (_activeTimer > 0f || _cooldownTimer > 0f) return false;
            _activeTimer = duration;
            // 冷却从"视野结束"才开始走（和 ShiftForm 的 6 s / 冷却 8 s 一致）：
            // 旧写法在按下瞬间就开始倒计时，3 秒视野期间白扣了 3 秒，实际是 22 s 冷却。
            _cooldownTimer = 0f;
            RefreshMarkers();
            Debug.Log("[Nightborn] 血视开启（" + duration + "s / 冷却 " + cooldown + "s）");
            return true;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_activeTimer > 0f)
            {
                _activeTimer = Mathf.Max(0f, _activeTimer - dt);
                if (_activeTimer <= 0f) _cooldownTimer = cooldown;
            }

            if (_cooldownTimer > 0f) _cooldownTimer = Mathf.Max(0f, _cooldownTimer - dt);

            var kb = Keyboard.current;
            if (kb != null && kb[key].wasPressedThisFrame) Activate();

            if (_activeTimer <= 0f) return;

            _refreshTimer += dt;
            if (_refreshTimer < markerRefresh) return;
            _refreshTimer = 0f;
            RefreshMarkers();
        }

        private void RefreshMarkers()
        {
            _marks.Clear();

            var guards = GuardAI.All;
            for (int i = 0; i < guards.Count; i++)
            {
                var guard = guards[i];
                if (guard == null) continue;
                AddMark(guard.transform.position + Vector3.up * 1.2f, new Color(0.95f, 0.2f, 0.25f, 0.95f), 9f);
            }

            foreach (var target in Object.FindObjectsOfType<FeedTarget>())
            {
                if (target == null || target.IsAsleep) continue;
                AddMark(target.transform.position + Vector3.up * 1.3f, new Color(0.95f, 0.62f, 0.3f, 0.9f), 7f);
            }

            foreach (var relic in Object.FindObjectsOfType<Relic>())
            {
                if (relic == null || relic.Collected) continue;
                AddMark(relic.transform.position, new Color(0.95f, 0.82f, 0.35f, 0.9f), 6f);
            }
        }

        private void AddMark(Vector3 world, Color color, float size)
        {
            if ((world - transform.position).sqrMagnitude > radius * radius) return;
            _marks.Add(new Mark { Position = world, Tint = color, Size = size });
        }

        private void OnGUI()
        {
            if (!IsActive) return;
            // OnGUI 每帧至少跑 Layout + Repaint 两遍，只在 Repaint 画，白跑的那遍直接跳过。
            if (Event.current != null && Event.current.type != EventType.Repaint) return;

            if (_cam == null) _cam = UnityEngine.Camera.main;
            if (_cam == null) return;

            for (int i = 0; i < _marks.Count; i++) DrawMarker(_cam, _marks[i]);
        }

        private void DrawMarker(UnityEngine.Camera cam, Mark mark)
        {
            Vector3 sp = cam.WorldToScreenPoint(mark.Position);
            if (sp.z <= 0f) return;

            float y = Screen.height - sp.y;
            var rect = new Rect(sp.x - mark.Size * 0.5f, y - mark.Size * 0.5f, mark.Size, mark.Size);

            GUI.color = mark.Tint;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
