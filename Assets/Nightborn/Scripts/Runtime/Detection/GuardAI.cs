using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
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
        [SerializeField] private float turnSpeed = 90f;

        [Header("视觉")]
        [Tooltip("视线锥总张角（度）。")]
        [SerializeField] private float viewAngle = 75f;
        [Tooltip("真实判定距离，应比光斑略短。")]
        [SerializeField] private float viewDistance = 11f;
        [SerializeField] private float eyeHeight = 1.6f;
        [SerializeField] private LayerMask occluderMask = ~0;

        [Header("确认速度")]
        [Tooltip("玩家光照高于该值时按“强光”速度确认。")]
        [SerializeField] private float brightThreshold = 0.65f;
        [SerializeField] private float confirmBright = 0.3f;
        [SerializeField] private float confirmDark = 3f;
        [SerializeField] private float decayPerSecond = 0.25f;

        [Header("反应")]
        [SerializeField] private float chaseSpeed = 3.4f;
        [SerializeField] private float catchDistance = 1.5f;
        [SerializeField] private float lostGrace = 1.5f;
        [SerializeField] private float searchDuration = 20f;

        [Header("可疑 / 手感")]
        [Tooltip("进度超过该值、且此刻看得见玩家时，守卫停下转身盯住玩家（对应 DetectionMeter 的 60% 可疑线）。")]
        [SerializeField] private float suspiciousProgress = 0.6f;
        [Tooltip("追击 / 搜索时转身速度 = turnSpeed × 该倍数。巡逻的 90°/s 用来追人像「不转身」。")]
        [SerializeField] private float chaseTurnMultiplier = 1.8f;
        [Tooltip("复活保护：ResetAlert 之后这段时间内看不见玩家，避免检查点正好落在敌人脚边时无限二次被抓。")]
        [SerializeField] private float respawnGrace = 2f;

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
        private Vector3 _lastKnown;

        private bool _dormant;
        private NavMeshAgent _agent;

        // 手感 / 兜底用的运行态
        private float _graceTimer;          // 复活保护剩余时间
        private bool _caughtLatch;          // 已经触发过 Caught，等 LevelFlow 把玩家送走
        private bool _suspicious;           // 进度过线但还没满：停下盯着玩家
        private bool _scanning;             // 正在手动转朝向（此时 Agent 的 updateRotation 必须关掉）
        private float _scanAngle;
        private float _scanBaseYaw;
        private bool _hasDest;              // SetDestination 的缓存，避免每帧重算路径
        private Vector3 _dest;
        private float _placementTimer;
        private int _placementTries;
        private bool _warnedNoNavMesh;

        /// <summary>被吸血 / 剧情强制休眠：停巡逻、熄视线锥、清警觉。</summary>
        public void SetDormant(bool dormant)
        {
            _dormant = dormant;
            if (dormant)
            {
                _progress = 0f;
                _state = State.Patrol;
                CanSeePlayer = false;
                _suspicious = false;
                _lostTimer = 0f;
                _searchTimer = 0f;
                _graceTimer = 0f;
                _caughtLatch = false;
                _hasDest = false;
                EndScan();
            }
            if (coneLight != null) coneLight.enabled = !dormant;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = dormant;
                if (dormant) _agent.ResetPath();
            }
        }

        public bool IsDormant => _dormant;

        public float Progress => _progress;
        public State CurrentState => _state;
        public bool CanSeePlayer { get; private set; }
        /// <summary>视线遮挡层（Awake 里已剔除 Player 层）。只读暴露：Bloodhound 的气味遮挡复用同一套掩码与语义。</summary>
        public LayerMask OccluderMask => occluderMask;

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
            _agent = GetComponent<NavMeshAgent>();
            if (_agent != null)
            {
                _agent.speed = patrolSpeed;
                _agent.angularSpeed = turnSpeed;
                _agent.acceleration = patrolSpeed * 4f;
                _agent.stoppingDistance = 0.2f;
                _agent.autoBraking = true;
            }

            // 场景是运行时烘焙 NavMesh 的：Agent 的 OnEnable 早于烘焙，可能还没贴到网格上。
            // 没贴上去就只能走「直接平移」的兜底分支（= 穿墙），所以第一帧就试着 Warp 一次。
            _placementTimer = 0f;
            EnsureOnNavMesh();

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
            float dt = Time.deltaTime;
            if (_graceTimer > 0f) _graceTimer = Mathf.Max(0f, _graceTimer - dt);

            if (_player == null)
            {
                Resolve();
                if (_player == null) return;
            }

            if (_dormant) return;

            EnsureOnNavMesh();

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
                // 刚丢失视野的宽限期内不掉进度：否则守卫一转身，玩家就"永远发现不了"
                _lostTimer += dt;
                if (_lostTimer > lostGrace)
                {
                    // 追击期间进度就是 1：DetectionMeter 只取最大值，掉下去 HUD 会显示「可疑」而敌人正在追你
                    float floor = _state == State.Chase ? 1f : 0f;
                    _progress = Mathf.Max(floor, Mathf.Clamp01(_progress - dt * decayPerSecond));
                }
            }

            // 可疑：进度过线但还看得见 → 停下转身盯住玩家（DetectionMeter 那条 60% 线在这里落地）
            _suspicious = CanSeePlayer && _state == State.Patrol && _progress >= suspiciousProgress;
            if (_suspicious)
            {
                FaceDirection(_player.position - transform.position, dt);
            }
            else
            {
                switch (_state)
                {
                    case State.Patrol: PatrolStep(dt); break;
                    case State.Chase: ChaseStep(dt); break;
                    case State.Search: SearchStep(dt); break;
                }
            }

            UpdateConeVisual();
        }

        private bool VisionCheck()
        {
            if (_graceTimer > 0f) return false; // 复活保护：刚被送回检查点，这段时间先不判视线
            if (_shift != null && _shift.IsShadow) return false; // 影身不可见

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 chest = _player.position + Vector3.up * 1.1f;
            Vector3 to = chest - eye;
            float dist = to.magnitude;

            // 2026-11-12 主 agent 补：**贴身不算视锥**。
            // 真机实测：猎犬追到 0.68 m 时，方位角因为距离太近而冲到 39.8°（视锥半角只有 17.5°），
            // 于是它在玩家身上转圈却「看不见」，永远抓不到人（教堂 S02 卡了 12 s 进 Search）。
            // 近距离就是已经贴脸了，直接算看见。
            if (dist <= Mathf.Max(0.8f, catchDistance * 0.8f)) return true;

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
            Transform target = PickWaypoint();
            if (target == null)
            {
                // 没有航点（或全被清成空引用）：原地慢慢扫视，不移动也不越界
                IdleTurn(dt);
                return;
            }

            if (MoveTowards(target.position, patrolSpeed, dt))
            {
                _wait += dt;
                if (_wait >= waitAtWaypoint)
                {
                    _wait = 0f;
                    _wp = (_wp + 1) % Mathf.Max(1, waypoints.Length);
                }
            }
        }

        private void ChaseStep(float dt)
        {
            if (CanSeePlayer) _lastKnown = _player.position;

            if (_caughtLatch) return; // 已经交给 LevelFlow，等它把玩家送回检查点

            if (CanSeePlayer && _graceTimer <= 0f &&
                Vector3.Distance(transform.position, _player.position) <= catchDistance)
            {
                _caughtLatch = true;
                LevelFlow.Instance?.Caught(this);
                return;
            }

            MoveTowards(_lastKnown, chaseSpeed, dt);

            if (!CanSeePlayer && _lostTimer > lostGrace) EnterSearch();
        }

        /// <summary>进入搜索：计时从头算，扫视的基准角也在这里重置。</summary>
        private void EnterSearch()
        {
            if (_state == State.Search) return;
            _state = State.Search;
            _searchTimer = 0f;
            _scanAngle = 0f;
            _scanBaseYaw = transform.eulerAngles.y;
            EndScan();
            _hasDest = false;
        }

        private void SearchStep(float dt)
        {
            // searchDuration 是整段搜索的「总预算」：走不到最后已知位置也必须能放弃。
            // 原来只在 arrived 之后才累计计时 —— 目的地落在 NavMesh 外时永远不会 arrived，
            // 守卫就永久卡在 Search 里不回巡逻（实测：追丢一次之后再也不会归位）。
            _searchTimer += dt;

            bool arrived = MoveTowards(_lastKnown, chaseSpeed * 0.8f, dt);
            if (arrived) ScanAround(dt); // 到点后原地左右扫视，而不是站着不动（原来 IdleTurn 被 Agent 覆盖成静止）

            if (_searchTimer >= searchDuration) GiveUp();
        }

        private void GiveUp()
        {
            _state = State.Patrol;
            _progress = 0f;
            _searchTimer = 0f;
            _wait = 0f;
            _suspicious = false;
            _hasDest = false;
            EndScan();
            Debug.Log("[Nightborn] " + name + " 搜索超时 → 归位巡逻");
        }

        private void EnterChase()
        {
            if (_state == State.Chase) return;
            EndScan();          // 追击必须让 Agent 重新接管朝向
            _caughtLatch = false;
            _hasDest = false;
            _state = State.Chase;

            _lastKnown = _player.position;
            AlertAll(_lastKnown); // 围猎：全体扑向最后已知位置
            Debug.Log("[Nightborn] 守卫发现玩家 → 围猎");
        }

        /// <summary>外部叠加检测进度（夜巡犬的气味追踪用）。满 1 会直接进入追逐。</summary>
        public void AddDetection(float amount)
        {
            if (_dormant || amount <= 0f) return;
            _progress = Mathf.Clamp01(_progress + amount);
            if (_progress >= 1f) EnterChase();
        }

        /// <summary>外部（围猎 / 剧情）唤醒。</summary>
        public void Alert(Vector3 lastKnown)
        {
            if (_dormant) return; // 睡着的守卫不该被拉进围猎：进度会被钉死在 100% 且没有人去衰减
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
            _lostTimer = 0f;
            _wait = 0f;
            _wp = 0;
            _suspicious = false;
            _caughtLatch = false;
            _hasDest = false;
            // 复活保护：检查点可能正好落在敌人脚边，没有这段宽限会「复活即被抓」死循环
            _graceTimer = Mathf.Max(0.5f, respawnGrace);
            EndScan();
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.ResetPath();
        }

        private bool MoveTowards(Vector3 target, float speed, float dt)
        {
            bool arrived;

            // 有 NavMeshAgent 就让它寻路，绕开建筑与矮墙
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.speed = speed;

                // 追击 / 搜索时把 Agent 的转身速度拉高：巡逻的 turnSpeed（默认 90°/s）追人像「不转身」。
                // 巡逻本身保留原值，所以只在速度高于巡逻速度时乘倍数。
                bool pursuit = speed > patrolSpeed * 1.05f;
                _agent.angularSpeed = pursuit ? turnSpeed * Mathf.Max(1f, chaseTurnMultiplier) : turnSpeed;

                // 每帧 SetDestination 会让寻路每帧重算一遍，拐角处表现为抖动；
                // 只有目标明显移动（>0.3 m）时才重设。
                if (!_hasDest || (_dest - target).sqrMagnitude > 0.09f)
                {
                    _agent.SetDestination(target);
                    _dest = target;
                    _hasDest = true;
                }

                if (_agent.pathPending) arrived = false;
                // 目的地不可达时 remainingDistance 会读 0，被误判成「到了」；交给调用方的超时兜底
                else if (_agent.pathStatus == NavMeshPathStatus.PathInvalid) arrived = true;
                else arrived = _agent.remainingDistance <= Mathf.Max(0.3f, _agent.stoppingDistance + 0.1f);
            }
            else
            {
                _hasDest = false;
                Vector3 flat = new Vector3(target.x, transform.position.y, target.z);
                Vector3 delta = flat - transform.position;
                if (delta.sqrMagnitude < 0.04f)
                {
                    arrived = true;
                }
                else
                {
                    Vector3 dir = delta.normalized;
                    transform.position += dir * speed * dt;
                    RotateTowards(dir, dt);
                    arrived = false;
                }
            }

            // 只要还在移动就把朝向交还给 Agent —— 扫视 / 盯人必须给移动让位，
            // 否则 updateRotation 关着的时候 Agent 会侧着身滑出去。
            if (!arrived) EndScan();
            return arrived;
        }

        /// <summary>
        /// 运行时烘焙 NavMesh 的场子里，Agent 的 OnEnable 早于烘焙，可能一直没贴到网格上。
        /// 没贴上的 Agent 会让 MoveTowards 掉进「直接平移」的兜底分支 —— 那就是追击穿墙的根因。
        /// 这里轮询 SamplePosition + Warp 把它贴回去。
        /// </summary>
        private void EnsureOnNavMesh()
        {
            if (_agent == null || !_agent.enabled || _agent.isOnNavMesh) return;

            _placementTimer -= Time.deltaTime;
            if (_placementTimer > 0f) return;
            _placementTimer = 0.5f;
            _placementTries++;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas) &&
                _agent.Warp(hit.position))
            {
                _hasDest = false;
                Debug.Log("[Nightborn] " + name + " 已贴到 NavMesh：" + hit.position);
                return;
            }

            if (_placementTries == 20 && !_warnedNoNavMesh)
            {
                _warnedNoNavMesh = true;
                Debug.LogWarning("[Nightborn] " + name + " 连续 10 秒没贴上 NavMesh，退化为直线移动（可能穿墙）：" + transform.position);
            }
        }

        /// <summary>取当前航点；长度为 0 或全是空引用时返回 null（调用方原地扫视，不会死循环 / 除零）。</summary>
        private Transform PickWaypoint()
        {
            if (waypoints == null || waypoints.Length == 0) return null;
            if (_wp < 0 || _wp >= waypoints.Length) _wp = 0; // 航点被运行时改短时不让索引越界

            for (int i = 0; i < waypoints.Length; i++)
            {
                int index = (_wp + i) % waypoints.Length;
                if (waypoints[index] != null)
                {
                    _wp = index;
                    return waypoints[index];
                }
            }
            return null;
        }

        private void RotateTowards(Vector3 dir, float dt)
        {
            if (_agent != null && _agent.updateRotation) return; // 交给 Agent 转
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion look = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
        }

        /// <summary>
        /// 开始手动转朝向。必须先摘掉 Agent 的 updateRotation：
        /// Agent 每帧都会用自己的朝向覆盖 transform，原地扫视/盯人就是被它吞掉的。
        /// </summary>
        private void BeginScan()
        {
            if (_scanning) return;
            _scanning = true;
            _scanAngle = 0f;
            _scanBaseYaw = transform.eulerAngles.y;
            if (_agent != null) _agent.updateRotation = false;
        }

        private void EndScan()
        {
            if (!_scanning) return;
            _scanning = false;
            if (_agent != null) _agent.updateRotation = true;
        }

        private void StopAgent()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
        }

        /// <summary>原地左右扫视（搜索时用，也兼作「没有航点」时的待机动作）。</summary>
        private void ScanAround(float dt)
        {
            BeginScan();
            StopAgent();
            _scanAngle += dt * Mathf.Max(20f, turnSpeed * 0.6f);
            float offset = Mathf.Sin(_scanAngle * Mathf.Deg2Rad) * 60f;
            transform.rotation = Quaternion.Euler(0f, _scanBaseYaw + offset, 0f);
        }

        /// <summary>转身盯住某个水平方向（可疑阶段用）。</summary>
        private void FaceDirection(Vector3 dir, float dt)
        {
            BeginScan();
            StopAgent();
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            Quaternion look = Quaternion.LookRotation(flat);
            float degPerSecond = turnSpeed * Mathf.Max(1f, chaseTurnMultiplier);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, degPerSecond * dt);
        }

        private void IdleTurn(float dt)
        {
            // 待机不再绕着 Y 轴匀速转圈（看着像 bug），改成慢慢左右扫视
            ScanAround(dt * 0.6f);
        }

        /// <summary>
        /// 视线锥的可视化：颜色 / 强度跟着确认进度走（平静=暖黄 1.2，围猎=血红 2.0）。
        /// 2026-11-11 主 agent 补回：上一轮重写时调用留着、定义掉了，整仓编译不过。
        /// 注意这盏 Spot 是真实光源，会被 LightExposureProbe 计入外光——这是有意的设计
        /// （守卫把灯举到你身上 = 你被照亮），不要在这里改成不参与光照。
        /// </summary>
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
