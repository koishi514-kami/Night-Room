using UnityEngine;

namespace Nightborn.Level
{
    /// <summary>
    /// 可吸血目标（M1）：吸血鬼掠食但不杀（HANDOVER §12 决策 7）。
    ///
    /// 规则：
    ///  · 只能从**背后/侧面**下手（正面点积过大就不给吸），正面靠近等于被发现；
    ///  · 吸血让目标昼睡 60 秒：守卫进入休眠（巡逻停、视线锥熄灭、检测清零）；
    ///  · 目标醒来后恢复巡逻。
    /// </summary>
    [DisallowMultipleComponent]
    public class FeedTarget : MonoBehaviour
    {
        [Header("收益")]
        [SerializeField] private float feedReward = 30f;
        [Tooltip("昼睡时长（秒）。")]
        [SerializeField] private float sleepDuration = 60f;

        [Header("是否可吸")]
        [Tooltip("重甲单位（钟楼看守）设为 false。")]
        [SerializeField] private bool feedable = true;

        [Header("判定")]
        [SerializeField] private float feedRange = 1.6f;
        [Tooltip("朝向点积阈值：小于该值算“在背后/侧面”。0 表示只能正后方，1 表示 360 度都能吸。")]
        [SerializeField] private float behindDotThreshold = 0.35f;

        [Header("引用")]
        [Tooltip("目标身上的守卫 AI（有则喂完进入休眠）。")]
        [SerializeField] private Nightborn.Detection.GuardAI guard;
        [Tooltip("昏睡时倒下的视觉体（可选）。")]
        [SerializeField] private Transform bodyVisual;

        private bool _asleep;
        private float _sleepTimer;

        public bool IsAsleep => _asleep;
        public float FeedReward => feedReward;
        /// <summary>是否允许被吸（钟楼看守是重甲，恒为 false）。</summary>
        public bool Feedable => feedable;

        private void Awake()
        {
            if (guard == null) guard = GetComponent<Nightborn.Detection.GuardAI>();
        }

        /// <summary>玩家现在能不能吸这个目标。</summary>
        public bool CanBeFed(Transform player)
        {
            if (!feedable || _asleep || player == null) return false;

            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.magnitude > feedRange) return false;

            // 目标正前方与"目标→玩家"的点积：> 阈值说明玩家在正面
            Vector3 toPlayerDir = toPlayer.normalized;
            if (Vector3.Dot(transform.forward, toPlayerDir) > behindDotThreshold) return false;

            // 目标已经在追人时不给吸
            if (guard != null && guard.CurrentState == Nightborn.Detection.GuardAI.State.Chase) return false;

            return true;
        }

        public void SetFeedable(bool value) => feedable = value;

        /// <summary>被吸：进入昼睡。</summary>
        public void ApplyFeed()
        {
            if (_asleep) return;

            // 1.4 秒的吸血锁定里目标可能已经转过身开始追你（CanBeFed 只在「开始吸」那一刻把关）。
            // 这时候不该还能把它吸睡：否则玩家被抓的同帧守卫倒地，等于白嫖一次脱身 + 一分钟空场。
            if (guard != null && guard.CurrentState == Nightborn.Detection.GuardAI.State.Chase)
            {
                Debug.Log("[Nightborn] 吸血被打断：目标已经进入追击");
                return;
            }

            _asleep = true;
            _sleepTimer = sleepDuration;

            if (guard != null) guard.SetDormant(true);
            if (bodyVisual != null)
                bodyVisual.localRotation = Quaternion.Euler(0f, 0f, 90f);

            Debug.Log("[Nightborn] 吸血完成：目标昼睡 " + sleepDuration + "s");
        }

        private void Update()
        {
            if (!_asleep) return;

            _sleepTimer -= Time.deltaTime;
            if (_sleepTimer > 0f) return;

            _asleep = false;
            if (guard != null) guard.SetDormant(false);
            if (bodyVisual != null) bodyVisual.localRotation = Quaternion.identity;
            Debug.Log("[Nightborn] 目标醒来");
        }
    }
}
