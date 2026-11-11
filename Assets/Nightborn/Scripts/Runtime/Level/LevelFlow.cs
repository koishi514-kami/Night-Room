using System;
using System.Collections.Generic;
using UnityEngine;
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

        [Header("被抓")]
        [Tooltip("被抓后停顿多久再回检查点。")]
        [SerializeField] private float caughtFreeze = 0.8f;

        private readonly List<Checkpoint> _checkpoints = new List<Checkpoint>();
        private Vector3 _spawnPoint;
        private float _spawnBlood = -1f;
        private int _relics;
        private int _relicTotal;
        private bool _complete;
        private bool _resolving;
        private float _freezeTimer;

        public bool IsComplete => _complete;
        public int Relics => _relics;
        public int RelicTotal => _relicTotal;
        public string LevelId => levelId;
        public int LevelIndex => levelIndex;

        public event Action<int, int> RelicChanged;
        public event Action LevelCompleted;
        public event Action CaughtEvent;

        private void Awake()
        {
            Instance = this;
            if (player == null)
            {
                var go = GameObject.Find("PLAYER_Ada");
                if (go != null) player = go.transform;
            }
            if (player != null)
            {
                if (blood == null) blood = player.GetComponent<BloodSystem>();
                if (detection == null) detection = player.GetComponent<DetectionMeter>();
                if (shiftForm == null) shiftForm = player.GetComponent<ShiftForm>();
                if (motor == null) motor = player.GetComponent<PlayerMotor>();
            }
            _spawnPoint = player != null ? player.position : Vector3.zero;
            _spawnBlood = blood != null ? blood.Current : -1f;

            // 血储归零也要走「被抓」的同一套回滚流程
            if (blood != null) blood.Died += HandleBloodDeath;
        }

        private void HandleBloodDeath()
        {
            if (_complete || _resolving) return;
            Debug.Log("[Nightborn] 血储归零 → 回检查点");
            Caught(null);
        }

        private void OnDestroy()
        {
            if (blood != null) blood.Died -= HandleBloodDeath;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_complete)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame)
                    UnityEngine.SceneManagement.SceneManager.LoadScene("L_Main");
                return;
            }

            if (_freezeTimer <= 0f) return;
            _freezeTimer -= Time.deltaTime;
            if (_freezeTimer <= 0f) RespawnAtCheckpoint();
        }

        public void RegisterCheckpoint(Checkpoint cp)
        {
            if (cp == null || _checkpoints.Contains(cp)) return;
            _checkpoints.Add(cp);
            _checkpoints.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        public void RegisterRelic(Relic relic)
        {
            _relicTotal++;
            RelicChanged?.Invoke(_relics, _relicTotal);
        }

        public void ActivateCheckpoint(Checkpoint cp)
        {
            if (cp == null) return;
            _spawnPoint = cp.transform.position + Vector3.up * 0.2f;
            _spawnBlood = blood != null ? Mathf.Max(blood.Current, 40f) : -1f;
            SaveSystem.SetCheckpoint(levelId, cp.Index);
            Debug.Log("[Nightborn] 检查点 " + cp.Index + " 已记录");
        }

        public void CollectRelic(Relic relic)
        {
            if (relic == null || relic.Collected) return;
            relic.Collect();
            _relics++;
            RelicChanged?.Invoke(_relics, _relicTotal);
            SaveSystem.AddRelic(levelId, relic.RelicId);
            Debug.Log("[Nightborn] 获得血之遗物：" + relic.RelicId + "（" + _relics + "/" + _relicTotal + "）");
        }

        /// <summary>被守卫抓住。</summary>
        public void Caught(GuardAI guard)
        {
            if (_complete || _resolving) return;
            _resolving = true;
            _freezeTimer = caughtFreeze;

            CaughtEvent?.Invoke();
            Debug.Log("[Nightborn] 被抓住 → 回检查点");

            if (motor != null) motor.SetSpeedMultiplier("freeze", 0f);
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

            if (blood != null)
            {
                blood.Revive(_spawnBlood > 0f ? _spawnBlood : blood.Max);
            }

            if (detection != null) detection.ResetDetection();
            for (int i = 0; i < GuardAI.All.Count; i++)
            {
                if (GuardAI.All[i] != null) GuardAI.All[i].ResetAlert();
            }

            if (motor != null) motor.ClearSpeedMultiplier("freeze");
            _resolving = false;
        }

        /// <summary>抵达出口。</summary>
        public void CompleteLevel()
        {
            if (_complete) return;
            _complete = true;

            float t = Time.timeSinceLevelLoad;
            SaveSystem.CompleteLevel(levelId, levelIndex, t, _relics, _relicTotal);
            LevelCompleted?.Invoke();
            Debug.Log(string.Format("[Nightborn] 通关：{0}  用时 {1:F1}s  遗物 {2}/{3}",
                levelId, t, _relics, _relicTotal));
        }
    }
}
