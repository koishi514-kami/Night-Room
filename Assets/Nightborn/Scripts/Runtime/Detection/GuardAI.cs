using System.Collections.Generic;
using UnityEngine;
using Nightborn.Player;
using Nightborn.Level;

namespace Nightborn.Detection
{
    /// <summary>
    /// 守卫 AI（M0 切片版）：航点巡逻 + 视线锥 + 光照决定确认速度。
    ///
    /// 设计依据 HANDOVER §12：
    ///  · 决策 13：视线定"看得见"，光定"看得多快"（强光 0.3 s / 暗处 3 s / 影身不可见）；
    ///  · 决策 14：丢失目标后去"最后已知位置"搜索 20 秒再归位；
    ///  · 参考实现（THIRD_PARTY.md）：Spotlight 当作视线锥的可视化，
    ///    真实判定距离略短于光斑，避免玩家被"看起来没照到"的光抓到。
    ///
    /// 说明：M0 用航点 + 直线移动（场地是平的），M2 再接 NavMesh。
    /// </summary>
    [DisallowMultipleComponent]
    public class GuardAI : MonoBehaviour
    {
        public enum State
        {
            Patrol = 0,
            Chase = 1,
            Search = 2
        }

        private static readonly List<GuardAI> Active = new List<GuardAI>();
        public static IReadOnlyList<GuardAI> All => Active;

        /// <summary>所有守卫里最高的确认进度（0~1），供 DetectionMeter 读取。</summary>
        public static float MaxProgress()
        {
            float max = 0f;
            for (int i = 0; i < Active.Count; i++)
                if (Active[i] != null) max = Mathf.Max(max, Active[i].Progress);
            return max;
        }

        /// <summary>围猎：全体守卫扑向最后已知位置。</summary>
        public static void AlertAll(Vector3 lastKnown)
        {
            for (int i = 0; i < Active.Count; i++)
                if (Active[i] != null) Active[i].Alert(lastKnown);
        }

        [Header("巡逻")]
        [SerializeField] private Transform[] waypoints;
        [SerializeField] private float patrolSpeed = 1.6f;
        [SerializeField] private float waitAtWaypoint = 1.2f;
        [SerializeField] private float turnSpeed = 150f;

        [Header("视觉")]
        [Tooltip("视线锥总张角（度）。")]
        [SerializeField] private float viewAngle = 65f;
        [Tooltip("真实判定距离，应比光斑略短。")]
        [SerializeField] private float viewDistance = 11f;
        [SerializeField] private float eyeHeight = 1.6f;
        [SerializeField] private LayerMask occluderMask = ~0;

        [Header("确认速度")]
        [Tooltip("玩家光照高于该值时按“强光”速度确认。")]
        [SerializeField] private float brightThreshold = 0.65f;
        [SerializeField] private float confirmBright = 0.3f;
        [SerializeField] private float confirmDark = 3f;
        [SerializeField] private float decayPerSecond = 0.6f;

        [Header("反应")]
        [SerializeField] private float chaseSpeed = 3.4f;
        [SerializeField] private float catchDistance = 1.5f;
        [SerializeField] private float lostGrace = 1.5f;
        [SerializeField] private float searchDuration = 20f;

        [Header("表现")]
        [Tooltip("视线锥可视化用的 Spot Light，留空自动找子节点。")]
        [SerializeField] private Light coneLight;
        [SerializeField] private Color calmColor = new Color(0.878f, 0.643f, 0.294f, 1f);
        [SerializeField] private Color alertColor = new Color(0.86f, 0.16f, 0.18f, 1f);

        private Transform _player;
        private LightExposureProbe _probe;
        private ShiftForm _shift;
        private State _state = State.Patrol;
        private int _wp;
        private float _wait;
        private float _progress;
        private float _lostTimer;
        private float _searchTimer;
        private float _lookAround;
        private Vector3 _lastKnown;

        public float Progress => _progress;
        public State CurrentState => _state;
        public bool CanSeePlayer { get; private set; }

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        private void Awake()
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) occluderMask &= ~(1 << playerLayer);

            if (coneLight == null)
            {
                var l = GetComponentInChildren<Light>();
                if (l != null && l.type == LightType.Spot) coneLight = l;
            }
        }

        private void Start()
        {
            Resolve();
            if (coneLight != null)
            {
                coneLight.type = LightType.Spot;
                coneLight.spotAngle = viewAngle;
                coneLight.range = viewDistance + 1f;
                coneLight.shadows = LightShadows.None;
                coneLight.color = calmColor;
            }
        }

        private void Resolve()
        {
            if (_player == null)
            {
                var go = GameObject.Find("PLAYER_Ada");
                if (go != null) _player = go.transform;
            }
            if (_player == null) return;
            if (_probe == null) _probe = _player.GetComponent<LightExposureProbe>();
            if (_shift == null) _shift = _player.GetComponent<ShiftForm>();
        }

        private void Update()
        {
            if (_player == null)
            {
                Resolve();
                if (_player == null) return;
            }

            float dt = Time.deltaTime;
            CanSeePlayer = VisionCheck();

            if (CanSeePlayer)
            {
                float confirm = (_probe != null && _probe.LightLevel >= brightThreshold) ? confirmBright : confirmDark;
                _progress = Mathf.Clamp01(_progress + dt / Mathf.Max(0.05f, confirm));
                _lastKnown = _player.position;
                _lostTimer = 0f;
                if (_progress >= 1f) EnterChase();
            }
            else
            {
                _progress = Mathf.Clamp01(_progress - dt * decayPerSecond);
                _lostTimer += dt;
            }

            switch (_state)
            {
                case State.Patrol: PatrolStep(dt); break;
                case State.Chase: ChaseStep(dt); break;
                case State.Search: SearchStep(dt); break;
            }

            UpdateConeVisual();
        }

        private bool VisionCheck()
        {
            if (_shift != null && _shift.IsShadow) return false; // 影身不可见

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 chest = _player.position + Vector3.up * 1.1f;
            Vector3 to = chest - eye;
            float dist = to.magnitude;

            if (dist > viewDistance) return false;
            if (Vector3.Angle(transform.forward, to) > viewAngle * 0.5f) return false;

            if (Physics.Raycast(eye, to / dist, out RaycastHit hit, dist, occluderMask, QueryTriggerInteraction.Ignore))
            {
                // 打到自己身上的碰撞体不算遮挡
                if (hit.collider.transform.root != transform.root) return false; // 被遮挡
            }

            return true;
        }

        private void PatrolStep(float dt)
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                IdleTurn(dt);
                return;
            }

            if (_wp < 0 || _wp >= waypoints.Length) _wp = 0; // 航点被运行时改短时不让索引越界

            Transform target = waypoints[_wp];
            if (target == null) { _wp = (_wp + 1) % waypoints.Length; return; }

            if (MoveTowards(target.position, patrolSpeed, dt))
            {
                _wait += dt;
                if (_wait >= waitAtWaypoint)
                {
                    _wait = 0f;
                    _wp = (_wp + 1) % waypoints.Length;
                }
            }
        }

        private void ChaseStep(float dt)
        {
            if (CanSeePlayer) _lastKnown = _player.position;

            if (Vector3.Distance(transform.position, _player.position) <= catchDistance && CanSeePlayer)
            {
                LevelFlow.Instance?.Caught(this);
                return;
            }

            MoveTowards(_lastKnown, chaseSpeed, dt);

            if (!CanSeePlayer && _lostTimer > lostGrace)
            {
                _state = State.Search;
                _searchTimer = 0f;
            }
        }

        private void SearchStep(float dt)
        {
            bool arrived = MoveTowards(_lastKnown, chaseSpeed * 0.8f, dt);
            if (arrived)
            {
                IdleTurn(dt);
                _searchTimer += dt;
                if (_searchTimer >= searchDuration) GiveUp();
            }
        }

        private void GiveUp()
        {
            _state = State.Patrol;
            _progress = 0f;
            _searchTimer = 0f;
            _wait = 0f;
        }

        private void EnterChase()
        {
            if (_state == State.Chase) return;
            _state = State.Chase;
            _lastKnown = _player.position;
            AlertAll(_lastKnown); // 围猎：全体扑向最后已知位置
            Debug.Log("[Nightborn] 守卫发现玩家 → 围猎");
        }

        /// <summary>外部（围猎 / 剧情）唤醒。</summary>
        public void Alert(Vector3 lastKnown)
        {
            _lastKnown = lastKnown;
            _progress = 1f;
            if (_state == State.Patrol) _state = State.Search;
            _searchTimer = 0f;
        }

        /// <summary>被抓 / 复活后清空警觉。</summary>
        public void ResetAlert()
        {
            _state = State.Patrol;
            _progress = 0f;
            _searchTimer = 0f;
            _wait = 0f;
            _wp = 0;
        }

        private bool MoveTowards(Vector3 target, float speed, float dt)
        {
            Vector3 flat = new Vector3(target.x, transform.position.y, target.z);
            Vector3 delta = flat - transform.position;
            if (delta.sqrMagnitude < 0.04f) return true;

            Vector3 dir = delta.normalized;
            transform.position += dir * speed * dt;
            RotateTowards(dir, dt);
            return false;
        }

        private void RotateTowards(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion look = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
        }

        private void IdleTurn(float dt)
        {
            transform.rotation *= Quaternion.Euler(0f, 25f * dt, 0f);
        }

        private void UpdateConeVisual()
        {
            if (coneLight == null) return;
            coneLight.color = Color.Lerp(calmColor, alertColor, _progress);
            coneLight.intensity = Mathf.Lerp(1.2f, 2.0f, _progress);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, catchDistance);

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Quaternion left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f);
            Quaternion right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f);
            Gizmos.DrawLine(eye, eye + left * transform.forward * viewDistance);
            Gizmos.DrawLine(eye, eye + right * transform.forward * viewDistance);

            if (waypoints == null) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.7f);
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawWireSphere(waypoints[i].position, 0.25f);
                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (next != null) Gizmos.DrawLine(waypoints[i].position, next.position);
            }
        }
    }
}
