using UnityEngine;

namespace Nightborn.Detection
{
    /// <summary>
    /// 夜巡犬（M3 第三种敌人）：眼睛差、鼻子灵。
    ///
    /// 与守卫的差别：
    ///  · 视锥很小（Bootstrap 里配 35° / 7 m），正面很难被它"看见"；
    ///  · **气味追踪**：玩家在 8 m 内、**在移动**、且猎犬与玩家之间没有实体墙时，
    ///    检测进度稳定上涨（0.55/s，约 1.8 秒到满）；
    ///  · 影身躲得过眼睛，**躲不过气味**；
    ///  · 站着不动、离远了、或者隔着一堵实体墙，进度按 GuardAI 的衰减走。
    ///
    /// 2026-11-12 气味遮挡（DEVLOG §25.7 点名的待修项）：
    ///  · 旧逻辑只看 3D 距离，隔一栋楼也照嗅 → 体感不公平。现在从猎犬**胸口**到玩家
    ///    **胸口**打一条射线，命中任何「不是自己、且不在 Player/Guard/Veil 层」的碰撞体就算被挡；
    ///  · 遮挡语义与层掩码**复用 GuardAI.VisionCheck**（同一份 occluderMask、同样
    ///    QueryTriggerInteraction.Ignore、同样"打到自己 root 不算遮挡"）；
    ///  · 判定节流 0.2 s（记录上次结果），射线走 RaycastNonAlloc + 预分配缓冲，运行期零分配；
    ///  · 薄幕（Veil 层）**不挡气味**：布/挂帘透气，也保住"隔着帘子被闻到"的紧张感；
    ///  · 半径 10 m → 8 m：墙不再白送侦查之后，10 m 明显过强。HANDOVER §25.7 的建议值是 7 m，
    ///    但 7 m 正好等于猎犬视距（HANDOVER §4），气味会被视觉吃掉；取 8 m，留 1 m
    ///    "没看见但已经闻到"的余味。
    /// </summary>
    [DisallowMultipleComponent]
    public class Bloodhound : MonoBehaviour
    {
        [Header("气味")]
        [Tooltip("气味半径。旧场景里序列化的是 10 m，Awake 会按 maxScentRange 夹回设计值。")]
        [SerializeField] private float scentRange = 8f;
        [SerializeField] private float scentRatePerSecond = 0.55f;
        [Tooltip("玩家速度低于该值算“静止”，气味不生效。")]
        [SerializeField] private float stillnessSpeed = 0.4f;
        [Tooltip("气味半径上限（设计值 8 m）。有它就不必重建场景，也能把旧场景的 10 m 夹回来。")]
        [SerializeField] private float maxScentRange = 8f;

        [Header("气味遮挡")]
        [Tooltip("遮挡判定的节流间隔（秒）。0.15–0.25 手感都在；0.2 是折中：贴墙绕柱时最多漏嗅 0.2 s。")]
        [SerializeField] private float occlusionInterval = 0.2f;
        [Tooltip("胸口高度（相对根节点）。与 GuardAI.VisionCheck 里的 chest = +1.1 m 保持一致。")]
        [SerializeField] private float chestHeight = 1.1f;
        [Tooltip("「闻得到 ↔ 被挡住」翻转时打一条日志（只在翻转时打，不刷屏）。")]
        [SerializeField] private bool logScentState = true;

        [Header("引用")]
        [SerializeField] private GuardAI guard;
        [SerializeField] private Transform player;
        [SerializeField] private Player.PlayerMotor motor;

        // 气味遮挡层 = GuardAI.occluderMask，再剔掉 Player(8)（否则射线一出发就打玩家胶囊）、
        // Guard(10)（猎犬自己的胶囊在 Guard 层）与 Veil(9)（薄幕透气，不参与气味遮挡）。
        private int _scentMask;
        private readonly RaycastHit[] _scentHits = new RaycastHit[8]; // 预分配，运行期零分配
        private float _occlusionTimer;
        private bool _hasOcclusionResult;
        private string _scentBlocker;

        private Vector3 _lastPlayerPosition;
        private float _movedWindow;   // 0.25 s 窗口内累计的玩家位移
        private float _timeWindow;

        /// <summary>上一次遮挡判定的结果：true = 气味被实体挡住（不累积）。玩家不在 ScentRange 内时复位为 false（未判定）。</summary>
        public bool ScentBlocked { get; private set; }

        /// <summary>挡住气味的物体 root 名（未被挡时为 null）。真机验收直接读这个看"被哪面墙挡的"。</summary>
        public string ScentBlocker => _scentBlocker;

        /// <summary>当前生效的气味半径（米），已按 maxScentRange 夹过。</summary>
        public float ScentRange => scentRange;

        /// <summary>气味每秒给 GuardAI 加多少检测进度。</summary>
        public float ScentRate => scentRatePerSecond;

        /// <summary>上一帧到玩家的 3D 距离（米）。</summary>
        public float ScentDistance { get; private set; }

        /// <summary>最近一次 0.25 s 窗口的平均速度（m/s）。</summary>
        public float ScentSpeed { get; private set; }

        /// <summary>上一帧是否真的在累积气味（范围内 + 未被遮挡 + 在移动）。</summary>
        public bool ScentActive { get; private set; }

        private void Awake()
        {
            if (guard == null) guard = GetComponent<GuardAI>();

            // 旧场景（改代码前生成的 L_Ch02_* / L_Ch03_S03）里序列化的是 10 m。
            // 这里夹回设计值：改手感不用重建场景，也不用手改 .unity。
            scentRange = Mathf.Clamp(scentRange, 1f, Mathf.Max(1f, maxScentRange));

            BuildScentMask();
            _occlusionTimer = 0f; // 第一帧立刻判一次，避免进场 0.2 s 内白嗅
        }

        /// <summary>气味遮挡层：复用 GuardAI 的视线遮挡掩码，剔掉 Player / Guard / Veil。</summary>
        private void BuildScentMask()
        {
            // 序列化值若是 0（漏配），退回默认射线层，别让气味变成"永远闻得到"
            int raw = guard != null ? guard.OccluderMask.value : Physics.DefaultRaycastLayers;
            if (raw == 0) raw = Physics.DefaultRaycastLayers;

            _scentMask = raw;
            ClearLayer(ref _scentMask, "Player"); // 打到自己/玩家胶囊不算遮挡
            ClearLayer(ref _scentMask, "Guard");  // 猎犬自己的胶囊在 Guard 层：不剔掉就 100% "被挡住"
            ClearLayer(ref _scentMask, "Veil");   // 薄幕透气：不参与气味遮挡
        }

        private static void ClearLayer(ref int mask, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) mask &= ~(1 << layer);
        }

        private void Update()
        {
            if (guard == null || guard.IsDormant) return;

            if (player == null)
            {
                var go = GameObject.Find("PLAYER_Ada");
                if (go == null) return;
                player = go.transform;
                _lastPlayerPosition = player.position;
                motor = go.GetComponent<Player.PlayerMotor>();
            }

            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float moved = Vector3.Distance(player.position, _lastPlayerPosition);
            _lastPlayerPosition = player.position;

            // 传送（检查点复活 / 影身被光弹回）那一帧的位移是假的，直接丢掉整个窗口
            if (moved > 3f)
            {
                ResetWindow();
                ScentActive = false;
                return;
            }

            ScentDistance = Vector3.Distance(transform.position, player.position);
            if (ScentDistance > scentRange)
            {
                // 出了半径：不判定遮挡，读数也复位（下次进范围重新判）
                ResetWindow();
                ScentActive = false;
                ScentBlocked = false;
                _scentBlocker = null;
                _hasOcclusionResult = false;
                _occlusionTimer = 0f;
                return;
            }

            // 遮挡判定不必每帧做：0.2 s 节流 + 缓存上次结果（每帧只读缓存，零物理开销）
            _occlusionTimer -= dt;
            if (!_hasOcclusionResult || _occlusionTimer <= 0f)
            {
                _occlusionTimer = Mathf.Max(0.05f, occlusionInterval);
                bool blocked = CheckScentBlocked(out string blocker);
                ApplyOcclusion(blocked, blocker);
            }

            if (ScentBlocked)
            {
                // 隔着实体墙：不累积气味。窗口一起清掉，免得把墙那边的位移攒到露头那一帧再结算。
                ResetWindow();
                ScentActive = false;
                return;
            }

            // 用 0.25 s 窗口的平均速度，而不是「这一帧位移 / dt」：
            // 高帧率下 dt≈0.007，角色贴地时的 0.3 cm 浮点抖动就能算出 0.4 m/s 的假移动，
            // 站着不动也会被「嗅」到。窗口平均把这个噪声压掉。
            _movedWindow += moved;
            _timeWindow += dt;
            if (_timeWindow < 0.25f)
            {
                ScentActive = false;
                return;
            }

            float window = _timeWindow;
            float speed = _movedWindow / window;
            ResetWindow();

            ScentSpeed = speed;
            if (speed <= stillnessSpeed)
            {
                ScentActive = false;
                return;
            }

            ScentActive = true;
            guard.AddDetection(scentRatePerSecond * window);
        }

        private void ResetWindow()
        {
            _movedWindow = 0f;
            _timeWindow = 0f;
        }

        /// <summary>
        /// 气味遮挡：胸口 → 胸口的射线。语义与 GuardAI.VisionCheck 一致
        /// （QueryTriggerInteraction.Ignore、打到自己 root 不算遮挡），只是两端都用胸口高度。
        /// </summary>
        private bool CheckScentBlocked(out string blocker)
        {
            blocker = null;

            Vector3 from = transform.position + Vector3.up * chestHeight;
            Vector3 to = player.position + Vector3.up * chestHeight;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.01f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / dist, _scentHits, dist, _scentMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = _scentHits[i].collider;
                if (hit == null) continue;

                Transform hitRoot = hit.transform.root;
                if (hitRoot == transform.root) continue; // 打到自己身上的碰撞体不算遮挡

                blocker = hitRoot.name;
                return true;
            }

            return false;
        }

        /// <summary>写入遮挡状态；只在「闻得到 ↔ 被挡住」翻转时打日志，避免每帧刷屏。</summary>
        private void ApplyOcclusion(bool blocked, string blocker)
        {
            bool flipped = _hasOcclusionResult && blocked != ScentBlocked;

            ScentBlocked = blocked;
            _scentBlocker = blocked ? blocker : null;
            if (flipped && logScentState)
            {
                if (blocked)
                    Debug.Log("[Nightborn] " + name + " 气味被墙挡住：" + blocker + "（距离 " + ScentDistance.ToString("0.0") + " m）");
                else
                    Debug.Log("[Nightborn] " + name + " 气味重新嗅到（遮挡消失，距离 " + ScentDistance.ToString("0.0") + " m）");
            }

            _hasOcclusionResult = true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 0.9f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, scentRange);

            // 真机验收用：青=通、红=被挡（只画中心射线，和实际判定完全一致）
            if (player == null) return;
            Gizmos.color = ScentBlocked ? Color.red : new Color(0.2f, 0.9f, 0.9f, 0.8f);
            Gizmos.DrawLine(transform.position + Vector3.up * chestHeight, player.position + Vector3.up * chestHeight);
        }
    }
}