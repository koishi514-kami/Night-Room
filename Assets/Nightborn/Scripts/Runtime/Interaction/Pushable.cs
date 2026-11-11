using UnityEngine;
using UnityEngine.AI;

namespace Nightborn.Interaction
{
    /// <summary>
    /// 可推货箱（M1 光操纵三件套之二：遮光）。
    /// 玩家用身体顶住箱子即可推动（CharacterController 的 OnControllerColliderHit 驱动）。
    /// Rigidbody 冻结 Y 与旋转，保证只在水平面滑动、不会翻倒。
    ///
    /// 另外挂一个 carving 的 NavMeshObstacle：箱子原本被 NavMeshRuntimeBake 烘进了寻路网格，
    /// 推开之后那块洞不会自己补、新位置也不挡路（敌人直接穿箱）。carve 之后箱子所到之处实时挖洞。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Pushable : MonoBehaviour
    {
        [SerializeField] private float mass = 30f;
        [SerializeField] private float drag = 4f;
        [Tooltip("推力倍率。>1 时箱子跑得比玩家快，接触断断续续（推起来一顿一顿），所以取 1。")]
        [SerializeField] private float pushGain = 1.0f;
        [SerializeField] private float minPushSpeed = 0.6f;
        [Tooltip("顶着推但箱子在这段时间内没位移 → 判定卡在墙角，停掉推力。")]
        [SerializeField] private float stuckWindow = 0.25f;
        [Tooltip("顶着推但箱子速度低于这个值（米/秒）→ 判定卡死。")]
        [SerializeField] private float stationarySpeed = 0.02f;

        private Rigidbody _rb;
        private NavMeshObstacle _obstacle;
        private Vector3 _lastPosition;
        private float _stuckTimer;
        private float _lastPushTime = -10f;

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

            _lastPosition = transform.position;

            SetupObstacle();
        }

        /// <summary>挂一个 carving 的 NavMeshObstacle（形状/尺寸跟 BoxCollider 对齐）。</summary>
        private void SetupObstacle()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null)
            {
                Debug.LogWarning("[Nightborn] Pushable 需要 BoxCollider 才能生成寻路障碍：" + name);
                return;
            }

            _obstacle = GetComponent<NavMeshObstacle>();
            if (_obstacle == null) _obstacle = gameObject.AddComponent<NavMeshObstacle>();

            _obstacle.shape = NavMeshObstacleShape.Box;
            _obstacle.center = box.center;
            _obstacle.size = box.size;
            _obstacle.carving = true;
            // 只在停下来后挖洞：推的过程里每帧重算 carve 是纯浪费。
            _obstacle.carveOnlyStationary = true;
            _obstacle.carvingMoveThreshold = 0.1f;
            _obstacle.carvingTimeToStationary = 0.3f;
        }

        private void Update()
        {
            // 玩家松手之后把卡死计时和位移基准清掉，下一次推重新开始算。
            if (Time.time - _lastPushTime > stuckWindow * 2f)
            {
                _stuckTimer = 0f;
                _lastPosition = transform.position;
            }
        }

        /// <summary>玩家顶上来时调用：给一个水平速度，速度越大推得越快。</summary>
        public void Push(Vector3 worldDirection, float playerSpeed)
        {
            if (_rb == null) return;
            _lastPushTime = Time.time;
            _rb.WakeUp();

            Vector3 dir = worldDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            // 卡死检测：顶着推但箱子没位移（墙角 / 贴墙）→ 停掉推力。
            // 否则每帧硬塞速度会把箱子往墙里挤，Rigidbody 解算回来就是抖动 + 玩家被顶穿。
            float moved = (transform.position - _lastPosition).magnitude;
            _lastPosition = transform.position;

            Vector3 current = _rb.velocity;
            current.y = 0f;

            // 用"速度"而不是"每帧位移"判断：位移阈值写死会和帧率绑在一起，
            // 0.002 m/帧 在 60 fps 下等于 0.12 m/s，微速推动（脚本自检那种）会被误判成卡死。
            float moveRate = moved / Mathf.Max(0.0001f, Time.deltaTime);
            if (moveRate < stationarySpeed)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer >= stuckWindow)
                {
                    _rb.velocity = Vector3.zero;
                    return;
                }
            }
            else
            {
                _stuckTimer = 0f;
            }

            float speed = Mathf.Max(minPushSpeed, playerSpeed) * pushGain;
            Vector3 target = dir * speed;

            // 原来按帧插值 0.35：144 fps 下箱子的加速比 60 fps 快 2.4 倍。
            // 换成按时间的指数收敛，26/s 正好对应 60 fps 下的 0.35/帧。
            _rb.velocity = Vector3.Lerp(current, target, 1f - Mathf.Exp(-26f * Time.deltaTime));
        }
    }
}
