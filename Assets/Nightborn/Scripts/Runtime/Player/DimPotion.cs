using UnityEngine;

namespace Nightborn.Player
{
    /// <summary>
    /// 压光药剂（M1 光操纵三件套之三）：30 秒内把自体光从 3.5 m 收到 1 m。
    /// 用法：数字键 1；数量来源是拾取（`AddPotion`），M1 先给 1 瓶起手。
    /// </summary>
    [DisallowMultipleComponent]
    public class DimPotion : MonoBehaviour
    {
        [SerializeField] private int count = 1;
        [SerializeField] private float duration = 30f;
        [SerializeField] private float dimRadius = 1f;
        [SerializeField] private SelfGlow glow;

        private float _timer;
        private bool _active;

        public int Count => count;
        public bool IsActive => _active;
        public float Remaining => Mathf.Max(0f, _timer);

        private void Awake()
        {
            if (glow == null) glow = GetComponent<SelfGlow>();
        }

        private void Update()
        {
            if (_active)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f) End();
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.digit1Key.wasPressedThisFrame) return;
            Use();
        }

        /// <summary>喝一瓶压光药剂（键位与 UI 共用）。</summary>
        public bool Use()
        {
            if (_active || count <= 0) return false;

            count--;
            _active = true;
            _timer = duration;
            if (glow != null) glow.SetRadius(dimRadius);
            Debug.Log("[Nightborn] 压光药剂生效：" + duration + "s 内自体光 " + dimRadius + " m");
            return true;
        }

        private void End()
        {
            _active = false;
            _timer = 0f;
            if (glow != null) glow.ResetRadius();
            Debug.Log("[Nightborn] 压光药剂结束");
        }

        public void AddPotion(int amount = 1)
        {
            count = Mathf.Max(0, count + amount);
        }

        /// <summary>被抓 / 复活时清掉状态。</summary>
        public void Cancel()
        {
            if (_active) End();
        }
    }
}
