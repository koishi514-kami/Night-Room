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
        [Tooltip("归零时打断正在进行的吸血（留空自动取同物体上的组件）。")]
        [SerializeField] private PlayerFeeder feeder;
        [Tooltip("归零时清掉压光药剂状态（留空自动取同物体上的组件）。")]
        [SerializeField] private DimPotion potion;

        [Tooltip("归零后等多久还没人来救（回检查点）就自己复位。防止没有 LevelFlow / 已通关时永久卡死。")]
        [SerializeField] private float respawnWatchdog = 4f;

        private bool _dead;
        private float _deadTimer;
        private Nightborn.Level.LevelFlow _flow;

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
            if (feeder == null) feeder = GetComponent<PlayerFeeder>();
            if (potion == null) potion = GetComponent<DimPotion>();
        }

        private void Start()
        {
            Changed?.Invoke(Current, maxBlood);
            _flow = FindObjectOfType<Nightborn.Level.LevelFlow>();
        }

        private void Update()
        {
            if (_dead)
            {
                // 看门狗：归零后本该由 LevelFlow 在 0.8 s 内送回检查点。
                // 但如果没人订阅 Died（场景没 LevelFlow）或者已经通关（LevelFlow.Update 提前 return），
                // 玩家会永久停在「血 0 + 动不了」。这里兜底自己复活，绝不允许状态机卡死。
                _deadTimer += Time.deltaTime;
                if (_deadTimer >= respawnWatchdog)
                {
                    Debug.LogWarning("[Nightborn] 血储归零 " + respawnWatchdog.ToString("F0") +
                                     " s 无人救援 → 自动复位（检查 LevelFlow / 是否已通关）");
                    Revive(Mathf.Max(40f, maxBlood * 0.4f));
                }
                return;
            }

            // 通关后不再灼伤：结算横幅期间把玩家烧死毫无意义（而且会连锁出「冻结」观感）。
            if (_flow != null && _flow.IsComplete) return;

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
            _deadTimer = 0f;
            Current = Mathf.Clamp(amount > 0f ? amount : maxBlood, 0f, maxBlood);
            if (motor != null)
            {
                motor.enabled = true;
                motor.ClearSpeedMultiplier("dead");
            }
            Changed?.Invoke(Current, maxBlood);
        }

        private void Die()
        {
            _dead = true;
            Current = 0f;
            Changed?.Invoke(Current, maxBlood);

            // 归零到回检查点之间有 0.8 s：如果不打断，正在跑的 1.4 s 吸血会在回滚途中
            // 照常结算（守卫被吸睡 + 回血），等于把死亡换成了一次免费吸血。
            if (feeder != null) feeder.Cancel();
            if (potion != null) potion.Cancel();

            Died?.Invoke();
            Debug.Log("[Nightborn] 血储归零：玩家死亡");

            // 2026-11-11：原来这里直接 motor.enabled = false。但只有 Revive() 会把它打开，
            // 一旦没人来救（没有 LevelFlow / 已通关），玩家就永久冻住（真机实测踩到过）。
            // 改成走移速倍率：视觉/移动一样停住，但状态是可恢复的，而且 Update 里的看门狗能兜底。
            _deadTimer = 0f;
            if (motor != null) motor.SetSpeedMultiplier("dead", 0f);
        }
    }
}
