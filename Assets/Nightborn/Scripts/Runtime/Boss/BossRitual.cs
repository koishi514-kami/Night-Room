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

        [Header("引用")]
        [SerializeField] private LevelFlow flow;
        [SerializeField] private SelfGlow glow;
        [SerializeField] private GuardAI bossGuard;
        [SerializeField] private Transform player;

        private int _gates;
        private int _lampsSeen;
        private Stage _stage = Stage.HolyLamps;

        public Stage CurrentStage => _stage;
        public int GatesPassed => _gates;
        public int LampsDoused => _lampsSeen;
        public float DistanceToAltar => altar != null && player != null
            ? Vector3.Distance(player.position, altar.position)
            : -1f;

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
        }

        private void Update()
        {
            if (_stage == Stage.HolyLamps)
            {
                int doused = 0;
                foreach (var lamp in holyLamps)
                    if (lamp != null && lamp.Doused) doused++;

                if (doused != _lampsSeen)
                {
                    _lampsSeen = doused;
                    Debug.Log("[Boss] 圣灯 " + doused + " / " + holyLamps.Length);
                }

                if (_lampsSeen >= holyLamps.Length && holyLamps.Length > 0)
                    SetStage(Stage.Veils);
            }
            else if (_stage == Stage.Altar)
            {
                TryEnterAltar();
            }
        }

        /// <summary>第二段：任一道仪式门被穿过。</summary>
        public void NotifyGatePassed()
        {
            if (_stage != Stage.Veils) return;
            _gates++;
            Debug.Log("[Boss] 侧廊门 " + _gates + " / 2");
            if (_gates >= 2) SetStage(Stage.Altar);
        }

        private void TryEnterAltar()
        {
            if (altar == null || player == null) return;
            if (Vector3.Distance(player.position, altar.position) > altarRadius) return;

            float radius = glow != null ? glow.Radius : 3.5f;
            if (radius > dimRadiusLimit)
            {
                // 没压光就凑近祭坛：主教立刻锁定
                if (bossGuard != null) bossGuard.AddDetection(1f);
                return;
            }

            Complete();
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
            _stage = next;
            StageChanged?.Invoke(next);
            Debug.Log("[Boss] 阶段 → " + next);
        }
    }
}
