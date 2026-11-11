using System;
using UnityEngine;

namespace Nightborn.Player
{
    /// <summary>
    /// 血储（M0 瘦身版）：**只做生命值**。
    ///
    /// 设计依据 HANDOVER §12 决策 10/11：
    ///  · 删除夜耗（−6/min 在 5 分钟关卡里等于不存在）；
    ///  · 血视不再抽血（改成冷却，见 BloodSight）；
    ///  · 原“灯芯 +25”改为**血瓶 +25**（拾取获得，R 使用）。
    /// </summary>
    [DisallowMultipleComponent]
    public class BloodSystem : MonoBehaviour
    {
        [Header("血储")]
        [SerializeField] private float maxBlood = 100f;
        [SerializeField] private float startBlood = 100f;

        [Header("回复")]
        [Tooltip("吸血一次回复量。")]
        [SerializeField] private float feedAmount = 30f;
        [Tooltip("血瓶一次回复量。")]
        [SerializeField] private float vialAmount = 25f;

        [Header("灼伤")]
        [Tooltip("强光下每秒掉血。")]
        [SerializeField] private float burnDamagePerSecond = 6f;

        [Header("引用")]
        [SerializeField] private LightExposureProbe probe;
        [SerializeField] private PlayerMotor motor;

        private bool _dead;

        public float Current { get; private set; }
        public float Max => maxBlood;
        public float Normalized => maxBlood > 0f ? Mathf.Clamp01(Current / maxBlood) : 0f;
        public bool IsDead => _dead;

        /// <summary>身上携带的血瓶数量。</summary>
        public int Vials { get; private set; }

        public event Action<float, float> Changed;
        public event Action Died;
        public event Action<int> VialsChanged;

        private void Awake()
        {
            Current = Mathf.Clamp(startBlood, 0f, maxBlood);
            if (probe == null) probe = GetComponent<LightExposureProbe>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
        }

        private void Start()
        {
            Changed?.Invoke(Current, maxBlood);
        }

        private void Update()
        {
            if (_dead) return;
            if (probe != null && probe.IsBurning && burnDamagePerSecond > 0f)
                ApplyDelta(-burnDamagePerSecond * Time.deltaTime);
        }

        private void ApplyDelta(float delta)
        {
            float before = Current;
            Current = Mathf.Clamp(Current + delta, 0f, maxBlood);
            if (!Mathf.Approximately(before, Current))
                Changed?.Invoke(Current, maxBlood);

            if (Current <= 0f && !_dead) Die();
        }

        /// <summary>吸血：默认 +30。</summary>
        public void Feed(float amount = -1f)
        {
            if (_dead) return;
            ApplyDelta(amount > 0f ? amount : feedAmount);
        }

        public void AddVial(int count = 1)
        {
            Vials = Mathf.Max(0, Vials + count);
            VialsChanged?.Invoke(Vials);
        }

        /// <summary>喝血瓶：+25，返回是否成功。</summary>
        public bool UseVial()
        {
            if (_dead || Vials <= 0) return false;
            Vials--;
            VialsChanged?.Invoke(Vials);
            ApplyDelta(vialAmount);
            return true;
        }

        public void Damage(float amount)
        {
            if (_dead || amount <= 0f) return;
            ApplyDelta(-amount);
        }

        public void Revive(float amount = -1f)
        {
            _dead = false;
            Current = Mathf.Clamp(amount > 0f ? amount : maxBlood, 0f, maxBlood);
            if (motor != null) motor.enabled = true;
            Changed?.Invoke(Current, maxBlood);
        }

        private void Die()
        {
            _dead = true;
            Current = 0f;
            Changed?.Invoke(Current, maxBlood);
            Died?.Invoke();
            Debug.Log("[Nightborn] 血储归零：玩家死亡");
            if (motor != null) motor.enabled = false;
        }
    }
}