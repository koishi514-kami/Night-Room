using UnityEngine;
using UnityEngine.InputSystem;
using Nightborn.Level;

namespace Nightborn.Player
{
    /// <summary>
    /// 吸血（M1）：鼠标左键 → 1.4 秒锁定 → 目标昼睡、血储 +30。
    ///
    /// 设计依据 HANDOVER §12 决策 7/22（左键 = 吸血，攻击性动作有代价：过程不能动）。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerFeeder : MonoBehaviour
    {
        [Header("节奏")]
        [SerializeField] private float feedDuration = 1.4f;
        [SerializeField] private float searchRadius = 1.6f;

        [Header("引用")]
        [SerializeField] private BloodSystem blood;
        [SerializeField] private PlayerMotor motor;

        private FeedTarget _target;
        private float _timer;

        public bool IsFeeding => _target != null;
        public FeedTarget Target => _target;
        public float Progress01 => _target == null ? 0f : Mathf.Clamp01(1f - _timer / Mathf.Max(0.05f, feedDuration));

        private void Awake()
        {
            if (blood == null) blood = GetComponent<BloodSystem>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
        }

        private void Update()
        {
            if (_target != null)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f) Complete();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            var candidate = FindFeedable();
            if (candidate == null) return;
            Begin(candidate);
        }

        /// <summary>外部（UI / 测试）触发一次吸血，成功返回 true。</summary>
        public bool TryFeed(FeedTarget target)
        {
            if (_target != null || target == null || !target.CanBeFed(transform)) return false;
            Begin(target);
            return true;
        }

        private FeedTarget FindFeedable()
        {
            FeedTarget best = null;
            float bestDist = float.MaxValue;

            var all = Object.FindObjectsOfType<FeedTarget>();
            foreach (var t in all)
            {
                if (t == null || !t.CanBeFed(transform)) continue;
                float d = Vector3.Distance(transform.position, t.transform.position);
                if (d > searchRadius || d >= bestDist) continue;
                best = t;
                bestDist = d;
            }

            return best;
        }

        private void Begin(FeedTarget target)
        {
            _target = target;
            _timer = feedDuration;
            if (motor != null) motor.SetSpeedMultiplier("feed", 0f); // 吸血过程不能动
            Debug.Log("[Nightborn] 开始吸血：" + target.name);
        }

        private void Complete()
        {
            var target = _target;
            _target = null;
            if (motor != null) motor.ClearSpeedMultiplier("feed");

            if (target == null) return;
            target.ApplyFeed();
            if (blood != null) blood.Feed(target.FeedReward);
            Debug.Log("[Nightborn] 血储 +" + (blood != null ? blood.Current.ToString("F0") : "?"));
        }

        /// <summary>被抓 / 复活时打断。</summary>
        public void Cancel()
        {
            _target = null;
            if (motor != null) motor.ClearSpeedMultiplier("feed");
        }
    }
}
