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

        public bool IsSneaking { get; private set; }
        public float CurrentSpeed => _planarVelocity.magnitude;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (cameraRig == null && UnityEngine.Camera.main != null)
                cameraRig = UnityEngine.Camera.main.transform.root;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            Vector2 input = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                IsSneaking = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }

            Vector3 wish = Vector3.zero;
            if (input.sqrMagnitude > 0.0001f)
            {
                Vector3 fwd = cameraRig != null ? cameraRig.forward : Vector3.forward;
                Vector3 right = cameraRig != null ? cameraRig.right : Vector3.right;
                fwd.y = 0f; right.y = 0f;
                fwd.Normalize(); right.Normalize();
                wish = (fwd * input.y + right * input.x).normalized;
            }

            float targetSpeed = IsSneaking ? sneakSpeed : walkSpeed;
            Vector3 targetVelocity = wish * targetSpeed;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, acceleration * Time.deltaTime);

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = _planarVelocity + Vector3.up * _verticalVelocity;
            _cc.Move(motion * Time.deltaTime);

            if (wish.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(wish, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, 12f * Time.deltaTime);
            }
        }
    }
}
