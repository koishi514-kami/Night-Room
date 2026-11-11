using System;
using UnityEngine;
using Nightborn.Detection;
using Nightborn.Player;

namespace Nightborn.Player
{
    /// <summary>
    /// 暴露度 → **检测进度**（M0 重构，替换原 ExposureMeter）。
    ///
    /// 设计依据 HANDOVER §12 决策 13：暴露度不再是"光照积分"，而是
    /// **所有守卫里最高的确认进度**（0–100）。光照只影响守卫确认得多快（在 GuardAI 里）。
    ///
    /// 阈值：>60 可疑（巡逻 AI 开始搜索）、=100 围猎。
    /// </summary>
    [DisallowMultipleComponent]
    public class DetectionMeter : MonoBehaviour
    {
        public enum DetectionState
        {
            Calm = 0,
            Suspicious = 1,
            Hunted = 2
        }

        [SerializeField] private float maxDetection = 100f;
        [SerializeField] private float suspiciousThreshold = 60f;
        [SerializeField] private float huntedThreshold = 100f;

        private float _detection;
        private DetectionState _state = DetectionState.Calm;

        public float Detection => _detection;
        public float Max => maxDetection;
        public float Normalized => maxDetection > 0f ? Mathf.Clamp01(_detection / maxDetection) : 0f;
        public DetectionState State => _state;

        public event Action<float, float> Changed;
        public event Action<DetectionState> StateChanged;

        private void Start()
        {
            Changed?.Invoke(_detection, maxDetection);
        }

        private void Update()
        {
            float target = GuardAI.MaxProgress() * maxDetection;
            if (Mathf.Approximately(target, _detection)) return;

            _detection = Mathf.MoveTowards(_detection, target, Mathf.Abs(target - _detection) * 12f * Time.deltaTime + 8f * Time.deltaTime);
            Changed?.Invoke(_detection, maxDetection);
            EvaluateState();
        }

        private void EvaluateState()
        {
            DetectionState next;
            if (_detection >= huntedThreshold) next = DetectionState.Hunted;
            else if (_detection >= suspiciousThreshold) next = DetectionState.Suspicious;
            else next = DetectionState.Calm;

            if (next == _state) return;
            _state = next;
            StateChanged?.Invoke(_state);
            Debug.Log(string.Format("[Nightborn] 检测度 {0:F0} → {1}", _detection, StateName(_state)));
        }

        public static string StateName(DetectionState state)
        {
            switch (state)
            {
                case DetectionState.Hunted: return "围猎";
                case DetectionState.Suspicious: return "可疑";
                default: return "平静";
            }
        }

        public void ResetDetection()
        {
            _detection = 0f;
            Changed?.Invoke(_detection, maxDetection);
            EvaluateState();
        }
    }
}
