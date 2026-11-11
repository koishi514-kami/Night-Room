using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nightborn.Player
{
    /// <summary>
    /// 化形（M0 步骤 5）：光身 = 实体；影身 = 熄自体光 + 穿过「薄幕」。
    ///
    /// 规则（HANDOVER §12 决策 12）：
    ///  · Tab 切换；影身最长 6 秒，到时自动弹回；
    ///  · 影身期间守卫看不见你（GuardAI 读取 IsShadow）；
    ///  · 影身只穿"Veil"层的薄幕（窗帘/栅栏/薄墙），实体场景照旧碰撞；
    ///  · 在强光里切影身 → 立刻被灼回并硬直 3 秒（LightExposureProbe.IsBurning）。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShiftForm : MonoBehaviour
    {
        [Header("时长")]
        [SerializeField] private float shadowDuration = 6f;
        [SerializeField] private float cooldown = 8f;
        [SerializeField] private float staggerDuration = 3f;

        [Header("引用")]
        [SerializeField] private SelfGlow glow;
        [SerializeField] private LightExposureProbe probe;
        [SerializeField] private PlayerMotor motor;

        [Header("碰撞层")]
        [Tooltip("薄幕所在层（Project Settings → Layers 里的 Veil）。")]
        [SerializeField] private int veilLayer = 9;

        private bool _shadow;
        private float _timer;
        private float _cooldownTimer;
        private float _staggerTimer;
        private int _playerLayer;

        public bool IsShadow => _shadow;
        public bool IsStaggered => _staggerTimer > 0f;
        public float CooldownRemaining => _cooldownTimer;
        public float ShadowRemaining => _shadow ? Mathf.Max(0f, shadowDuration - _timer) : 0f;

        public event Action<bool> FormChanged;

        private void Awake()
        {
            if (glow == null) glow = GetComponent<SelfGlow>();
            if (probe == null) probe = GetComponent<LightExposureProbe>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
            _playerLayer = gameObject.layer;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_cooldownTimer > 0f) _cooldownTimer = Mathf.Max(0f, _cooldownTimer - dt);
            if (_staggerTimer > 0f)
            {
                _staggerTimer = Mathf.Max(0f, _staggerTimer - dt);
                if (_staggerTimer <= 0f && motor != null) motor.ClearSpeedMultiplier("stagger");
            }

            if (_shadow)
            {
                _timer += dt;

                // 强光里立刻被灼回
                if (probe != null && probe.IsBurning)
                {
                    ExitShadow(true);
                    return;
                }

                if (_timer >= shadowDuration) ExitShadow(false);
            }

            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.wasPressedThisFrame && !IsStaggered && _cooldownTimer <= 0f)
            {
                if (_shadow) ExitShadow(false);
                else EnterShadow();
            }
        }

        private void EnterShadow()
        {
            // 强光下不允许切影身：直接惩罚，避免"无脑开影身穿光"
            if (probe != null && probe.IsBurning)
            {
                Debug.Log("[Nightborn] 强光下无法化形 → 硬直");
                Stagger();
                return;
            }

            _shadow = true;
            _timer = 0f;
            if (glow != null) glow.SetGlowing(false);
            Physics.IgnoreLayerCollision(_playerLayer, veilLayer, true);
            FormChanged?.Invoke(true);
            Debug.Log("[Nightborn] 化形：影身（光灭）");
        }

        private void ExitShadow(bool burned)
        {
            if (!_shadow) return;
            _shadow = false;
            _timer = 0f;
            _cooldownTimer = cooldown;
            if (glow != null) glow.SetGlowing(true);
            Physics.IgnoreLayerCollision(_playerLayer, veilLayer, false);
            FormChanged?.Invoke(false);

            if (burned)
            {
                Debug.Log("[Nightborn] 强光灼回影身 → 硬直");
                Stagger();
            }
            else
            {
                Debug.Log("[Nightborn] 化形：光身");
            }
        }

        private void Stagger()
        {
            _staggerTimer = staggerDuration;
            if (motor != null) motor.SetSpeedMultiplier("stagger", 0.15f);
        }

        /// <summary>被抓 / 复活时强制回到光身。</summary>
        public void ForceLightForm()
        {
            if (_shadow) ExitShadow(false);
            _staggerTimer = 0f;
            _cooldownTimer = 0f;
            if (motor != null) motor.ClearSpeedMultiplier("stagger");
        }
    }
}
