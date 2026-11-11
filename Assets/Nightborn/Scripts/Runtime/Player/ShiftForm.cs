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
        private bool _veilCollisionIgnoredBefore;

        // 出幕时做重叠检测用的缓冲（每帧不分配）
        private readonly Collider[] _overlap = new Collider[8];

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

            if (veilLayer < 0 || veilLayer > 31)
            {
                Debug.LogError("[Nightborn] ShiftForm.veilLayer 非法（" + veilLayer + "），回落到 9。");
                veilLayer = 9;
            }

            _playerLayer = gameObject.layer;
            // 记下场景原本的设置：有些关卡可能本来就允许穿幕，别在退出影身时把它改掉。
            _veilCollisionIgnoredBefore = Physics.GetIgnoreLayerCollision(_playerLayer, veilLayer);
        }

        private void OnDisable()
        {
            // IgnoreLayerCollision 是**全局物理设置**：影身状态下按 Esc 回菜单 / 切场景，
            // 这个"Player↔Veil 不碰撞"会留到下一关——光身也能穿帘子。离开时必须还原。
            SetVeilCollisionIgnored(false);
            _shadow = false;
            _timer = 0f;
            _staggerTimer = 0f;
            if (motor != null) motor.ClearSpeedMultiplier("stagger");
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

        /// <summary>外部（UI / 测试）切换形态，返回切换后是否处于影身。</summary>
        public bool Toggle()
        {
            if (IsStaggered || _cooldownTimer > 0f) return _shadow;
            if (_shadow) ExitShadow(false);
            else EnterShadow();
            return _shadow;
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
            SetVeilCollisionIgnored(true);
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
            SetVeilCollisionIgnored(false);
            // 恢复碰撞的同一帧里胶囊可能正嵌在薄幕体内（穿到一半时限/强光把形态弹回来了），
            // 必须立刻推到幕外，否则 CharacterController 的 overlap recovery 会把人往随机一侧顶。
            EscapeVeilOverlap();
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

        /// <summary>Player↔Veil 碰撞开关。IgnoreLayerCollision 是全局状态，退出影身时按原值还原。</summary>
        private void SetVeilCollisionIgnored(bool ignore)
        {
            Physics.IgnoreLayerCollision(_playerLayer, veilLayer, ignore || _veilCollisionIgnoredBefore);
        }

        /// <summary>把胶囊从薄幕体内推到"玩家中心所在那一侧"的外面（最多迭代 3 次）。</summary>
        private void EscapeVeilOverlap()
        {
            var cc = GetComponent<CharacterController>();
            if (cc == null) return;

            for (int pass = 0; pass < 3; pass++)
            {
                Bounds body = cc.bounds;
                int count = Physics.OverlapBoxNonAlloc(body.center, body.extents * 0.9f, _overlap, Quaternion.identity,
                    1 << veilLayer, QueryTriggerInteraction.Ignore);

                bool moved = false;
                for (int i = 0; i < count; i++)
                {
                    var other = _overlap[i];
                    if (other == null || other.transform.root == transform.root) continue;
                    var box = other as BoxCollider;
                    if (box == null) continue;
                    if (!TryPushOutOfBox(cc, box)) continue;
                    moved = true;
                    break;
                }

                if (!moved) return;
            }
        }

        /// <summary>按薄幕最薄的那根轴把玩家推出去；没嵌进去就返回 false。</summary>
        private bool TryPushOutOfBox(CharacterController cc, BoxCollider box)
        {
            Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
            Vector3 axisX = box.transform.right;
            Vector3 axisY = box.transform.up;
            Vector3 axisZ = box.transform.forward;

            int thin = 0;
            if (size.y < size.x) thin = 1;
            if (size.z < (thin == 0 ? size.x : size.y)) thin = 2;
            Vector3 normal = thin == 0 ? axisX : (thin == 1 ? axisY : axisZ);
            float halfThick = (thin == 0 ? size.x : (thin == 1 ? size.y : size.z)) * 0.5f;

            Vector3 center = transform.TransformPoint(cc.center);
            Vector3 rel = center - box.transform.TransformPoint(box.center);
            float along = Vector3.Dot(rel, normal);

            // 横向已经出了幕的边界（只是 AABB 相交），不算卡住
            Vector3[] axes = { axisX, axisY, axisZ };
            for (int a = 0; a < 3; a++)
            {
                if (a == thin) continue;
                float halfAxis = (a == 0 ? size.x : (a == 1 ? size.y : size.z)) * 0.5f;
                if (Mathf.Abs(Vector3.Dot(rel, axes[a])) > halfAxis) return false;
            }

            float need = halfThick + cc.radius + 0.03f - Mathf.Abs(along);
            if (need <= 0f) return false; // 已经在外侧

            Vector3 dir = normal * (along >= 0f ? 1f : -1f);
            cc.enabled = false;
            transform.position += dir * need;
            cc.enabled = true;
            Debug.Log("[Nightborn] 影身出幕与薄幕重叠 → 沿幕法线推出 " + need.ToString("F2") + " m");
            return true;
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
