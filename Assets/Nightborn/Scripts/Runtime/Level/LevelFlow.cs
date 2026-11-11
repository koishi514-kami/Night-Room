using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Nightborn.Detection;
using Nightborn.Player;

namespace Nightborn.Level
{
    /// <summary>
    /// 关卡流程（M0 步骤 6/7/8）：检查点、被抓回滚、出口通关、遗物收集、存档。
    ///
    /// 规则（HANDOVER §12 决策 17/18/23）：
    ///  · 抵达出口 = 通关；每关一处可选「血之遗物」；
    ///  · 检查点自动存档 + 关卡选择；
    ///  · 被抓 → 短停顿 → 回最近检查点，血量与检测度回滚。
    ///
    /// 回滚必须把「敌人的状态」一起复位，否则会出现「复活即被抓」的死循环：
    ///  · 清警觉（`GuardAI.ResetAlert`）——旧实现已经做了；
    ///  · 把还杵在检查点边上的敌人送回自己的航点（旧实现没做，守卫会原地站着继续看）；
    ///  · 清掉 `feed` / `stagger` 两个移速倍率（吸血 / 硬直 被打断后残留，复活了也动不了）；
    ///  · 复活后给一小段保护窗，重复被抓不叠加回滚。
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelFlow : MonoBehaviour
    {
        public static LevelFlow Instance { get; private set; }

        [Header("关卡")]
        [SerializeField] private string levelId = "Ch01_S01";
        [SerializeField] private int levelIndex = 0;

        [Header("引用")]
        [SerializeField] private Transform player;
        [SerializeField] private BloodSystem blood;
        [SerializeField] private DetectionMeter detection;
        [SerializeField] private ShiftForm shiftForm;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerFeeder feeder;

        [Header("被抓")]
        [Tooltip("被抓后停顿多久再回检查点。")]
        [SerializeField] private float caughtFreeze = 0.8f;
        [Tooltip("复活时把离检查点这么近的敌人送回它自己的航点（0 = 关掉）。")]
        [SerializeField] private float respawnClearRadius = 6f;
        [Tooltip("复活保护：这段时间内再次被抓不触发回滚，用来打断异常连抓。")]
        [SerializeField] private float respawnProtect = 1.2f;

        private readonly List<Checkpoint> _checkpoints = new List<Checkpoint>();
        private readonly HashSet<Relic> _relicRegistry = new HashSet<Relic>();
        private Vector3 _spawnPoint;
        private float _spawnBlood = -1f;
        private int _spawnCheckpointIndex = -1;
        private int _relics;
        private int _relicTotal;
        private bool _complete;
        private bool _resolving;
        private float _freezeTimer;
        private float _protectUntil;

        /// <summary>敌人航点字段（复活清场时要把卫兵送回自己路线的远端）。反射只做一次。</summary>
        private static readonly FieldInfo WaypointsField =
            typeof(GuardAI).GetField("waypoints", BindingFlags.NonPublic | BindingFlags.Instance);

        public bool IsComplete => _complete;
        public int Relics => _relics;
        public int RelicTotal => _relicTotal;
        public string LevelId => levelId;
        public int LevelIndex => levelIndex;

        /// <summary>当前复活点（自检工具读它，不用反射私有字段）。</summary>
        public Vector3 SpawnPoint => _spawnPoint;

        /// <summary>当前记录的检查点序号（-1 = 还没踩过任何检查点）。</summary>
        public int SpawnCheckpointIndex => _spawnCheckpointIndex;

        /// <summary>是否正处在「被抓 → 回检查点」的停顿窗口里。</summary>
        public bool IsResolving => _resolving;

        public event Action<int, int> RelicChanged;
        public event Action LevelCompleted;
        public event Action CaughtEvent;

        private void Awake()
        {
            Instance = this;
            ResolvePlayer();
            _spawnPoint = player != null ? player.position : Vector3.zero;
            _spawnBlood = blood != null ? blood.Current : -1f;

            // 血储归零也要走「被抓」的同一套回滚流程
            if (blood != null) blood.Died += HandleBloodDeath;
        }

        private void ResolvePlayer()
        {
            if (player == null)
            {
                var go = GameObject.Find("PLAYER_Ada");
                if (go != null) player = go.transform;
            }
            if (player == null) return;

            if (blood == null) blood = player.GetComponent<BloodSystem>();
            if (detection == null) detection = player.GetComponent<DetectionMeter>();
            if (shiftForm == null) shiftForm = player.GetComponent<ShiftForm>();
            if (motor == null) motor = player.GetComponent<PlayerMotor>();
            if (feeder == null) feeder = player.GetComponent<PlayerFeeder>();
        }

        private void HandleBloodDeath()
        {
            if (_complete || _resolving) return;
            Debug.Log("[Nightborn] 血储归零 → 回检查点");

            // 2026-11-12：血储归零必须**一定**回检查点，不能被「复活保护窗」吞掉。
            // 以前在保护窗内（复活后 1.2 s）再次归零时，Caught() 会直接 return，
            // 于是没人复活玩家 —— 只能等 BloodSystem 的 4 s 看门狗，这 4 秒玩家完全动不了，
            // 就是玩家说的「突然走不动」的一种。这里先把保护窗清掉。
            _protectUntil = 0f;
            Caught(null);
        }

        private void OnDestroy()
        {
            if (blood != null) blood.Died -= HandleBloodDeath;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && WantsExitToMenu())
            {
                ExitToMenu();
                return;
            }

            if (_complete) return;
            if (_freezeTimer <= 0f) return;

            _freezeTimer -= Time.deltaTime;
            if (_freezeTimer <= 0f) RespawnAtCheckpoint();
        }

        /// <summary>
        /// Esc 的两段语义（保住菜单里那行「Esc 释放鼠标」）：
        ///  · 通关后：一次 Esc 直接回菜单；
        ///  · 未通关：第一次 Esc 由相机释放光标（ThirdPersonCamera），光标已经放开后再按一次才回菜单。
        /// </summary>
        private bool WantsExitToMenu()
        {
            if (_complete) return true;
            return Cursor.lockState != CursorLockMode.Locked;
        }

        /// <summary>回菜单必须把时间缩放和光标一起复原，否则菜单 UI 点不动、鼠标也出不来。</summary>
        private void ExitToMenu()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("[Nightborn] 退出到菜单（通关=" + _complete + "）");
            SceneManager.LoadScene("L_Main");
        }

        public void RegisterCheckpoint(Checkpoint cp)
        {
            if (cp == null || _checkpoints.Contains(cp)) return;
            _checkpoints.Add(cp);
            _checkpoints.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        public void RegisterRelic(Relic relic)
        {
            if (relic == null || !_relicRegistry.Add(relic)) return;   // 幂等：同一个遗物只计一次
            _relicTotal++;
            RelicChanged?.Invoke(_relics, _relicTotal);
        }

        public void ActivateCheckpoint(Checkpoint cp)
        {
            if (cp == null) return;

            // 2026-11-11 主 agent 改：检查点物体本身抬在 y≈1（3×2×3 触发盒的中心），
            // 直接 +0.2 会让每次复活都从 1.2 m 高处落地（体感像被扔下去）。
            // 改成向下打一条射线贴地，回廊上的检查点也能正确贴到 5.3 的板面。
            Vector3 spawn = cp.transform.position + Vector3.up * 0.2f;
            RaycastHit groundHit;
            if (Physics.Raycast(cp.transform.position + Vector3.up * 1.5f, Vector3.down, out groundHit, 6f,
                    ~0, QueryTriggerInteraction.Ignore))
            {
                spawn = groundHit.point + Vector3.up * 0.2f;
            }
            _spawnPoint = spawn;
            _spawnBlood = blood != null ? Mathf.Max(blood.Current, 40f) : -1f;
            _spawnCheckpointIndex = cp.Index;
            SaveSystem.SetCheckpoint(levelId, cp.Index, levelIndex);
            Debug.Log("[Nightborn] 检查点 " + cp.Index + " 已记录");
        }

        public void CollectRelic(Relic relic)
        {
            if (relic == null || relic.Collected) return;
            relic.Collect();
            _relics++;
            RelicChanged?.Invoke(_relics, _relicTotal);
            SaveSystem.AddRelic(levelId, relic.RelicId, levelIndex);
            Debug.Log("[Nightborn] 获得血之遗物：" + relic.RelicId + "（" + _relics + "/" + _relicTotal + "）");
        }

        /// <summary>被守卫抓住。</summary>
        public void Caught(GuardAI guard)
        {
            if (_complete) return;
            if (_resolving) return;                      // 多个敌人同一帧扑上来：只结算一次
            if (Time.time < _protectUntil) return;       // 复活保护窗内：吞掉，不叠回滚

            _resolving = true;
            _freezeTimer = caughtFreeze;

            CaughtEvent?.Invoke();
            Debug.Log("[Nightborn] 被抓住 → 回检查点" + (guard != null ? "（" + guard.name + "）" : ""));

            // 吸血锁定中被打断：不清掉 "feed" 倍率的话，复活后玩家一样动不了
            if (feeder != null) feeder.Cancel();
            if (motor != null)
            {
                motor.ClearSpeedMultiplier("feed");
                motor.SetSpeedMultiplier("freeze", 0f);
            }
            if (shiftForm != null) shiftForm.ForceLightForm();
        }

        private void RespawnAtCheckpoint()
        {
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.position = _spawnPoint;
                if (cc != null) cc.enabled = true;
            }

            if (blood != null) blood.Revive(_spawnBlood > 0f ? _spawnBlood : blood.Max);
            if (detection != null) detection.ResetDetection();

            // 敌人复位：清警觉 + 把还杵在检查点旁边的敌人送回航点，否则「复活即被抓」会循环
            for (int i = 0; i < GuardAI.All.Count; i++)
                if (GuardAI.All[i] != null) GuardAI.All[i].ResetAlert();
            ClearGuardsNearSpawn(_spawnPoint);

            if (feeder != null) feeder.Cancel();
            if (motor != null)
            {
                motor.ClearSpeedMultiplier("freeze");
                motor.ClearSpeedMultiplier("feed");
            }
            if (shiftForm != null) shiftForm.ForceLightForm();

            _resolving = false;
            _freezeTimer = 0f;
            _protectUntil = Time.time + respawnProtect;

            Debug.Log("[Nightborn] 已回到检查点 " + _spawnCheckpointIndex + "  " +
                      _spawnPoint.ToString("F1") + "  血 " + (blood != null ? blood.Current.ToString("F0") : "-"));
        }

        /// <summary>把检查点附近的敌人送回它自己路线的最远端，避免刚复活就被同一个人再抓一次。</summary>
        private void ClearGuardsNearSpawn(Vector3 spawn)
        {
            if (respawnClearRadius <= 0f) return;

            float radiusSqr = respawnClearRadius * respawnClearRadius;
            for (int i = 0; i < GuardAI.All.Count; i++)
            {
                var guard = GuardAI.All[i];
                if (guard == null || guard.IsDormant) continue;   // 被吸过血的先不管：它本来就不构成威胁
                if ((guard.transform.position - spawn).sqrMagnitude > radiusSqr) continue;

                Vector3 target = PickGuardFallback(guard, spawn);
                var agent = guard.GetComponent<NavMeshAgent>();
                if (agent != null && agent.enabled && agent.isOnNavMesh &&
                    NavMesh.SamplePosition(target, out var hit, 4f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
                else
                {
                    guard.transform.position = target;
                }

                guard.ResetAlert();
                Debug.Log("[Nightborn] 复活清场：" + guard.name + " → " + target.ToString("F1"));
            }
        }

        private static Vector3 PickGuardFallback(GuardAI guard, Vector3 spawn)
        {
            var waypoints = WaypointsField != null ? WaypointsField.GetValue(guard) as Transform[] : null;
            Vector3 best = spawn;
            float bestSqr = -1f;

            if (waypoints != null)
            {
                for (int i = 0; i < waypoints.Length; i++)
                {
                    var wp = waypoints[i];
                    if (wp == null) continue;
                    float sqr = (wp.position - spawn).sqrMagnitude;
                    if (sqr > bestSqr)
                    {
                        bestSqr = sqr;
                        best = wp.position;
                    }
                }
            }

            if (bestSqr > 0f) return best;

            Vector3 away = guard.transform.position - spawn;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            return spawn + away.normalized * 6f;
        }

        /// <summary>抵达出口。</summary>
        public void CompleteLevel()
        {
            if (_complete) return;
            _complete = true;

            // 通关窗口里如果还压着一次回滚，别让它在切场景前把玩家拽回检查点
            _resolving = false;
            _freezeTimer = 0f;
            if (feeder != null) feeder.Cancel();
            if (motor != null)
            {
                motor.ClearSpeedMultiplier("freeze");
                motor.ClearSpeedMultiplier("feed");
            }

            float t = Time.timeSinceLevelLoad;
            SaveSystem.CompleteLevel(levelId, levelIndex, t, _relics, _relicTotal);
            LevelCompleted?.Invoke();
            Debug.Log(string.Format("[Nightborn] 通关：{0}  用时 {1:F1}s  遗物 {2}/{3}",
                levelId, t, _relics, _relicTotal));
        }
    }
}
