using System;
using UnityEngine;
using Nightborn.Detection;
using Nightborn.Level;
using Nightborn.Player;

namespace Nightborn.Boss
{
    /// <summary>
    /// 主教 Boss：三段潜行仪式（HANDOVER §12 决策 16）。
    ///
    ///  第一段 熄圣灯：吹灭 3 盏圣灯（`DouseableLamp`）→ 祭坛的护罩裂开；
    ///  第二段 穿幕：从两道薄幕穿到侧廊（影身），两道 `RitualGate` 都触发 → 侧门打开；
    ///  第三段 压光：在**压光药剂**生效（自体光 ≤ 1.25 m）时靠近祭坛 3 m —— 否则主教立刻锁定你。
    ///
    /// 失败不重置仪式：灯灭着、门开着，只是玩家被送回检查点。
    /// </summary>
    [DisallowMultipleComponent]
    public class BossRitual : MonoBehaviour
    {
        public enum Stage
        {
            HolyLamps = 0,
            Veils = 1,
            Altar = 2,
            Done = 3
        }

        [Header("第一段：圣灯")]
        [SerializeField] private DouseableLamp[] holyLamps = new DouseableLamp[0];

        [Header("第三段：祭坛")]
        [SerializeField] private Transform altar;
        [SerializeField] private float altarRadius = 3f;
        [Tooltip("靠近祭坛时自体光半径上限，超过就算暴露。")]
        [SerializeField] private float dimRadiusLimit = 1.25f;
        [Tooltip("判「贴近祭坛」时忽略的垂直高差（米）。祭坛台面比地面高 1 m，玩家爬不上去，只按水平距离算。")]
        [SerializeField] private float altarVerticalTolerance = 2.5f;

        [Header("引用")]
        [SerializeField] private LevelFlow flow;
        [SerializeField] private SelfGlow glow;
        [SerializeField] private GuardAI bossGuard;
        [SerializeField] private Transform player;

        private DouseableLamp[] _allLamps;
        private RitualGate[] _gatesInScene;
        private readonly System.Collections.Generic.HashSet<RitualGate> _credited = new System.Collections.Generic.HashSet<RitualGate>();
        private Collider _altarSurface;
        private bool _altarSurfaceResolved;
        private float _altarLogTimer;
        private int _gates;
        private int _lampsSeen;
        private Stage _stage = Stage.HolyLamps;

        public Stage CurrentStage => _stage;
        public int GatesPassed => _gates;
        public int LampsDoused => _lampsSeen;
        public float DistanceToAltar => AltarDistance();

        /// <summary>当前阶段该往哪走（HUD 目标导航用；没有可去的目标时返回 null）。</summary>
        public Transform CurrentObjectivePoint
        {
            get
            {
                switch (_stage)
                {
                    case Stage.HolyLamps:
                        return NearestOf(_allLamps, lamp => !lamp.Doused);
                    case Stage.Veils:
                        return NearestOf(_gatesInScene, gate => !gate.Reported);
                    case Stage.Altar:
                        return altar;
                    default:
                        return null;
                }
            }
        }

        /// <summary>按「离玩家最近」挑一个满足条件的目标。</summary>
        private Transform NearestOf<T>(T[] items, Func<T, bool> accept) where T : Component
        {
            if (items == null) return null;
            Transform best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null || !accept(item)) continue;
                float d = player != null ? Vector3.SqrMagnitude(item.transform.position - player.position) : 0f;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = item.transform;
                }
            }
            return best;
        }

        public event Action<Stage> StageChanged;

        private void Awake()
        {
            if (player == null)
            {
                var go = GameObject.Find("PLAYER_Ada");
                if (go != null) player = go.transform;
            }
            if (player != null)
            {
                if (glow == null) glow = player.GetComponent<SelfGlow>();
            }
            if (flow == null) flow = FindObjectOfType<LevelFlow>();

            // HUD 目标导航要按阶段找最近的目标（圣灯 / 仪式门）
            _allLamps = FindObjectsOfType<DouseableLamp>();
            _gatesInScene = FindObjectsOfType<RitualGate>();
        }

        private void Update()
        {
            if (_stage == Stage.HolyLamps)
            {
                int doused = CountDoused();
                if (doused != _lampsSeen)
                {
                    _lampsSeen = doused;
                    Debug.Log("[Boss] 圣灯 " + doused + " / " + LampTotal);
                }

                if (LampTotal > 0 && _lampsSeen >= LampTotal) SetStage(Stage.Veils);
            }
            else if (_stage == Stage.Altar)
            {
                TryEnterAltar();
            }
        }

        /// <summary>圣灯总数。holyLamps 没接线时退回场景里所有 DouseableLamp。</summary>
        private int LampTotal
        {
            get
            {
                if (holyLamps != null && holyLamps.Length > 0) return holyLamps.Length;
                return _allLamps != null ? _allLamps.Length : 0;
            }
        }

        /// <summary>
        /// 已熄灭的圣灯数。holyLamps 没接线时退回场景里所有 DouseableLamp ——
        /// 否则 _lampsSeen 恒为 0，仪式永远停在第一段（卡关）。
        /// </summary>
        private int CountDoused()
        {
            var lamps = (holyLamps != null && holyLamps.Length > 0) ? holyLamps : _allLamps;
            if (lamps == null) return 0;

            int doused = 0;
            for (int i = 0; i < lamps.Length; i++)
                if (lamps[i] != null && lamps[i].Doused) doused++;
            return doused;
        }

        /// <summary>
        /// 仪式门被穿过。返回 true 表示这一笔真的记下了（RitualGate 只在这时才上锁）。
        ///
        /// 原来 RitualGate 先把自己标「已上报」再调 NotifyGatePassed，而后者在第二段之前直接 return：
        /// 玩家只要在灭完圣灯之前路过一次侧廊门，这一笔就永久作废，第二段再也过不去（= 卡关）。
        /// 现在改成：第二段之前路过不上锁；第二段里同一道门重复穿过也不重复计数。
        /// </summary>
        public bool TryPassGate(RitualGate gate)
        {
            if (gate == null) return false;
            if (_stage != Stage.Veils) return false;
            if (!_credited.Add(gate)) return false;

            _gates = _credited.Count;
            Debug.Log("[Boss] 侧廊门 " + _gates + " / 2");
            if (_gates >= 2) SetStage(Stage.Altar);
            return true;
        }

        private void TryEnterAltar()
        {
            if (altar == null || player == null) return;

            // -1 = 量不到（没接线 / 玩家不在台面那一层）：按「不贴近」处理。
            // ObjectiveHud 也吃这个约定（< 0 就不显示距离）。
            float distance = AltarDistance();
            if (distance < 0f || distance > altarRadius)
            {
                _altarLogTimer = 0f;
                return;
            }

            float radius = glow != null ? glow.Radius : 3.5f;
            if (radius > dimRadiusLimit)
            {
                // 没压光就凑近祭坛：主教立刻锁定
                if (bossGuard != null) bossGuard.AddDetection(1f);

                if (_altarLogTimer <= 0f)
                {
                    _altarLogTimer = 3f;
                    Debug.Log("[Boss] 未压光就靠近祭坛（自体光 " + radius.ToString("F2") +
                              " m > " + dimRadiusLimit.ToString("F2") + " m）→ 主教锁定");
                }
                else
                {
                    _altarLogTimer -= Time.deltaTime;
                }
                return;
            }

            Complete();
        }

        /// <summary>
        /// 到祭坛的距离。祭坛是一块 1 m 高的台（Bootstrap: GEO_Altar），
        /// 而玩家 CharacterController 的 stepOffset 只有 0.4 —— 爬不上去。
        /// 所以按「台面最近点」量：贴着台边就算贴近祭坛，否则永远差 5 m，第三段不可能完成。
        /// </summary>
        private float AltarDistance()
        {
            if (altar == null || player == null) return -1f;

            ResolveAltarSurface();
            if (_altarSurface == null) return Vector3.Distance(player.position, altar.position);

            Vector3 p = player.position;
            Vector3 q = _altarSurface.ClosestPoint(p);
            if (Mathf.Abs(p.y - q.y) > altarVerticalTolerance) return -1f;

            float dx = p.x - q.x;
            float dz = p.z - q.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>从祭坛正上方往下打一条射线，取祭坛台面的碰撞体（第一下命中的是台面，不是地面）。</summary>
        private void ResolveAltarSurface()
        {
            if (_altarSurface != null || _altarSurfaceResolved || altar == null) return;
            _altarSurfaceResolved = true;

            if (Physics.Raycast(altar.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit,
                    6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (player != null && hit.collider.transform.root == player.root) return;
                _altarSurface = hit.collider;
                Debug.Log("[Boss] 祭坛台面取自：" + hit.collider.name);
            }
        }

        private void Complete()
        {
            if (_stage == Stage.Done) return;
            SetStage(Stage.Done);
            Debug.Log("[Boss] 仪式完成");
            if (flow != null) flow.CompleteLevel();
        }

        private void SetStage(Stage next)
        {
            if (_stage == next) return;
            if ((int)next < (int)_stage) return; // 阶段只前进不回退：回退会把已经做完的进度清掉 → 卡关
            _stage = next;
            StageChanged?.Invoke(next);
            Debug.Log("[Boss] 阶段 → " + next);
        }

    }
}
