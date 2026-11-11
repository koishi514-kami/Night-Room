using UnityEngine;
using UnityEngine.InputSystem;

namespace Nightborn.Player
{
    /// <summary>
    /// 灰盒阶段的玩家移动：WASD 相对相机方向移动 + Shift 潜行减速 + 重力贴地。
    /// 用 CharacterController 而不是 Rigidbody，避免灰盒阶段物理抖动，也便于后续做「光照判定减速」。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("速度 (m/s)")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float sneakSpeed = 1.3f;
        [SerializeField] private float acceleration = 14f;

        [Header("重力")]
        [SerializeField] private float gravity = -22f;

        [Header("外部引用")]
        [SerializeField] private Transform cameraRig;

        private CharacterController _cc;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;
        private Vector3 _spawnPoint;
        private Vector3 _lastSafePoint;
        private bool _hasSafePoint;
        private float _safeTimer;
        private float _lockTimer;
        private readonly System.Collections.Generic.Dictionary<string, float> _speedModifiers =
            new System.Collections.Generic.Dictionary<string, float>();

        public bool IsSneaking { get; private set; }
        public float CurrentSpeed => _planarVelocity.magnitude;
        public Vector2 RawInput { get; private set; }

        [Header("软惩罚下限")]
        [Tooltip("burn / stagger 这类「惩罚性减速」叠起来后的最小倍率。硬锁（freeze / feed / dead）不受此限。")]
        [SerializeField] private float minSoftMultiplier = 0.3f;

        [Tooltip("硬锁最长持续时间：超过就强行清掉并警告。兜底用，正常流程都远短于此（freeze 0.8s / feed 1.4s / dead ≤4s）。")]
        [SerializeField] private float hardLockWatchdog = 6f;

        /// <summary>硬锁键：这些来源生效时玩家是真的「不能动」，不做下限钳制。</summary>
        private static readonly string[] HardLockKeys = { "freeze", "feed", "dead" };

        /// <summary>
        /// 是否处于「硬锁」（被抓冻结 / 吸血锁定 / 血储归零）。
        /// 2026-11-12：以前 Update 用 `SpeedMultiplier <= 0.001` 判断锁定，等于把所有软惩罚
        /// 也当硬锁 —— 强光里按 Tab（burn 0.45 × stagger 0.15 = 0.0675）时速度只剩 0.22 m/s，
        /// 玩家体感就是「突然变慢、走不动」。现在硬锁与软惩罚分开。
        /// </summary>
        public bool IsHardLocked
        {
            get
            {
                for (int i = 0; i < HardLockKeys.Length; i++)
                {
                    float v;
                    if (_speedModifiers.TryGetValue(HardLockKeys[i], out v) && v <= 0.001f) return true;
                }
                return false;
            }
        }

        /// <summary>实际生效的移速倍率：硬锁为 0；否则所有来源取最小，再夹一个下限。</summary>
        public float SpeedMultiplier
        {
            get
            {
                if (IsHardLocked) return 0f;

                float result = 1f;
                foreach (var v in _speedModifiers.Values) result = Mathf.Min(result, v);
                return Mathf.Max(minSoftMultiplier, result);
            }
        }

        /// <summary>调试用：把当前所有移速来源拼成一行（F1 浮层 / execute_code 直接读）。</summary>
        public string SpeedModifierReport()
        {
            if (_speedModifiers.Count == 0) return "无惩罚（×1.00）";

            var sb = new System.Text.StringBuilder();
            foreach (var kv in _speedModifiers)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("F2"));
            }
            sb.Append(" → ×").Append(SpeedMultiplier.ToString("F2"));
            if (IsHardLocked) sb.Append("（硬锁：不能动）");
            else if (SpeedMultiplier <= minSoftMultiplier) sb.Append("（已触底 ×").Append(minSoftMultiplier.ToString("F2")).Append("）");
            return sb.ToString();
        }

        /// <summary>
        /// 按来源设置移速倍率：灼伤（burn）/ 硬直（stagger）/ 被抓冻结（freeze）各自独立，
        /// 取最小值生效——避免一个系统每帧覆写掉另一个系统的惩罚。
        /// </summary>
        public void SetSpeedMultiplier(string key, float multiplier)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (multiplier >= 0.999f) _speedModifiers.Remove(key);
            else _speedModifiers[key] = Mathf.Clamp(multiplier, 0f, 3f);
        }

        public void ClearSpeedMultiplier(string key)
        {
            if (!string.IsNullOrEmpty(key)) _speedModifiers.Remove(key);
        }

        /// <summary>某个来源的惩罚是否正在生效（吸血/灼伤要判断"是不是被别人冻住了"）。</summary>
        public bool HasSpeedModifier(string key)
        {
            return !string.IsNullOrEmpty(key) && _speedModifiers.ContainsKey(key);
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _spawnPoint = transform.position;
            _lastSafePoint = _spawnPoint;

            if (cameraRig == null && UnityEngine.Camera.main != null)
                cameraRig = UnityEngine.Camera.main.transform.root;
            if (cameraRig == null)
            {
                var rig = GameObject.Find("CAM_Rig");
                if (rig != null) cameraRig = rig.transform;
            }
        }

        private void Start()
        {
            if (Keyboard.current == null)
                Debug.LogError("[Nightborn] 读不到 Keyboard.current：Player Settings → Active Input Handling 需要是 Input System Package (New) 或 Both。");
            if (cameraRig == null)
                Debug.LogWarning("[Nightborn] PlayerMotor 没绑定 cameraRig，移动方向会退化成世界坐标。");
        }

        /// <summary>顶住可推物体（货箱）时把玩家速度转成推力。</summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var pushable = hit.collider.GetComponent<Nightborn.Interaction.Pushable>();
            if (pushable == null) return;

            // 推力方向用接触法线的水平投影（指向箱子），而不是 moveDirection：
            // 斜着蹭箱子时 moveDirection 会把箱子往斜前方顶，玩家就会"推着推着从箱子上滑开"。
            Vector3 dir = -hit.normal;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = hit.moveDirection;
                dir.y = 0f;
            }

            pushable.Push(dir, CurrentSpeed);
        }

        private void Update()
        {
            // 单帧 dt 兜底：编辑器切出去再回来、或首帧加载时 dt 会涨到几百毫秒，
            // 不夹住的话一帧 CharacterController.Move 就能走好几米（穿墙 / 直接穿过薄幕）。
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            var kb = Keyboard.current;
            Vector2 input = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
                IsSneaking = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            RawInput = input;

            float mult = SpeedMultiplier;
            bool locked = IsHardLocked; // 只有 freeze / feed / dead 才是真「不能动」

            // 硬锁看门狗：任何来源把玩家锁住超过 hardLockWatchdog 秒，一律强行解锁并警告。
            // 玩家不该因为某条状态的清理路径漏了，就永久「走不动」。
            if (locked)
            {
                _lockTimer += dt;
                if (_lockTimer >= hardLockWatchdog)
                {
                    Debug.LogWarning("[Nightborn] 硬锁持续 " + hardLockWatchdog.ToString("F0") +
                                     " s（" + SpeedModifierReport() + "）→ 强制解锁");
                    ClearSpeedMultiplier("freeze");
                    ClearSpeedMultiplier("feed");
                    ClearSpeedMultiplier("dead");
                    _lockTimer = 0f;
                    locked = false;
                    mult = SpeedMultiplier;
                }
            }
            else
            {
                _lockTimer = 0f;
            }

            Vector3 wish = Vector3.zero;
            if (!locked && input.sqrMagnitude > 0.0001f)
            {
                Vector3 fwd = cameraRig != null ? cameraRig.forward : Vector3.forward;
                Vector3 right = cameraRig != null ? cameraRig.right : Vector3.right;
                fwd.y = 0f; right.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
                fwd.Normalize(); right.Normalize();
                wish = (fwd * input.y + right * input.x).normalized;
            }

            if (locked)
            {
                // 锁定期间必须立刻停住：只靠加速度衰减会继续滑 0.2 s，而锁定解除的瞬间
                // 还带着残留速度窜一下（喂食结束后人往前扑）。
                _planarVelocity = Vector3.zero;
            }
            else
            {
                float targetSpeed = (IsSneaking ? sneakSpeed : walkSpeed) * mult;
                Vector3 targetVelocity = wish * targetSpeed;
                _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, acceleration * dt);
            }

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * dt;

            Vector3 motion = _planarVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(motion * dt);

            // 记录最近一次稳定落地的位置：掉出世界时回这里，而不是 Awake 抓的关卡出生点
            // （过检查点之后出生点早就过期了，掉下去会被送回关卡入口）。
            if (_cc.isGrounded)
            {
                _safeTimer += dt;
                if (_safeTimer >= 0.5f)
                {
                    _safeTimer = 0f;
                    _lastSafePoint = transform.position;
                    _hasSafePoint = true;
                }
            }
            else
            {
                _safeTimer = 0f;
            }

            if (transform.position.y < -20f)
            {
                _cc.enabled = false;
                transform.position = (_hasSafePoint ? _lastSafePoint : _spawnPoint) + Vector3.up * 0.2f;
                _cc.enabled = true;
                _planarVelocity = Vector3.zero;
                _verticalVelocity = 0f;
            }

            if (wish.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(wish, Vector3.up);
                // 指数收敛而不是 Slerp(t = 12 * dt)：后者在 dt 大（掉帧、编辑器卡一下）时
                // t 会超过 1，转身会过冲然后来回摆。
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-12f * dt));
            }
        }
    }
}
