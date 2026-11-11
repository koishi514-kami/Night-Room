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
        private readonly System.Collections.Generic.Dictionary<string, float> _speedModifiers =
            new System.Collections.Generic.Dictionary<string, float>();

        public bool IsSneaking { get; private set; }
        public float CurrentSpeed => _planarVelocity.magnitude;
        public Vector2 RawInput { get; private set; }

        /// <summary>外部施加的移速倍率（取所有来源里最小的那个），1 = 无影响。</summary>
        public float SpeedMultiplier
        {
            get
            {
                float result = 1f;
                foreach (var v in _speedModifiers.Values) result = Mathf.Min(result, v);
                return result;
            }
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

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _spawnPoint = transform.position;

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
            pushable.Push(hit.moveDirection, CurrentSpeed);
        }

        private void Update()
        {
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

            Vector3 wish = Vector3.zero;
            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 fwd = cameraRig != null ? cameraRig.forward : Vector3.forward;
                Vector3 right = cameraRig != null ? cameraRig.right : Vector3.right;
                fwd.y = 0f; right.y = 0f;
                if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
                if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
                fwd.Normalize(); right.Normalize();
                wish = (fwd * input.y + right * input.x).normalized;
            }

            float targetSpeed = (IsSneaking ? sneakSpeed : walkSpeed) * SpeedMultiplier;
            Vector3 targetVelocity = wish * targetSpeed;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, acceleration * Time.deltaTime);

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = _planarVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);

            if (transform.position.y < -20f)
            {
                _cc.enabled = false;
                transform.position = _spawnPoint + Vector3.up * 0.2f;
                _cc.enabled = true;
                _planarVelocity = Vector3.zero;
                _verticalVelocity = 0f;
            }

            if (wish.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 12f * Time.deltaTime);
            }
        }
    }
}
