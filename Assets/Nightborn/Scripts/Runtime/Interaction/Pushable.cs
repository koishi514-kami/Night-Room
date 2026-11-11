using UnityEngine;

namespace Nightborn.Interaction
{
    /// <summary>
    /// 可推货箱（M1 光操纵三件套之二：遮光）。
    /// 玩家用身体顶住箱子即可推动（CharacterController 的 OnControllerColliderHit 驱动）。
    /// Rigidbody 冻结 Y 与旋转，保证只在水平面滑动、不会翻倒。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Pushable : MonoBehaviour
    {
        [SerializeField] private float mass = 30f;
        [SerializeField] private float drag = 4f;
        [SerializeField] private float pushGain = 1.2f;
        [SerializeField] private float minPushSpeed = 0.6f;

        private Rigidbody _rb;

        public Rigidbody Body => _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.mass = mass;
            _rb.drag = drag;
            _rb.angularDrag = 6f;
            _rb.useGravity = true;
            _rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        /// <summary>玩家顶上来时调用：给一个水平速度，速度越大推得越快。</summary>
        public void Push(Vector3 worldDirection, float playerSpeed)
        {
            if (_rb == null) return;
            _rb.WakeUp();

            Vector3 dir = worldDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            float speed = Mathf.Max(minPushSpeed, playerSpeed) * pushGain;
            Vector3 target = dir * speed;
            Vector3 current = _rb.velocity;
            current.y = 0f;
            _rb.velocity = Vector3.Lerp(current, target, 0.35f);
        }
    }
}
