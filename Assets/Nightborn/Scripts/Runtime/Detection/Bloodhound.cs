using UnityEngine;

namespace Nightborn.Detection
{
    /// <summary>
    /// 夜巡犬（M3 第三种敌人）：眼睛差、鼻子灵。
    ///
    /// 与守卫的差别：
    ///  · 视锥很小（Bootstrap 里配 35° / 7 m），正面很难被它"看见"；
    ///  · **气味追踪**：玩家在 10 m 内且**在移动**时，检测进度会稳定上涨（约 1.8 秒到满）；
    ///  · 影身躲得过眼睛，**躲不过气味**；
    ///  · 站着不动、或离远了，进度按 GuardAI 的衰减走。
    /// </summary>
    [DisallowMultipleComponent]
    public class Bloodhound : MonoBehaviour
    {
        [Header("气味")]
        [SerializeField] private float scentRange = 10f;
        [SerializeField] private float scentRatePerSecond = 0.55f;
        [Tooltip("玩家速度低于该值算“静止”，气味不生效。")]
        [SerializeField] private float stillnessSpeed = 0.4f;

        [Header("引用")]
        [SerializeField] private GuardAI guard;
        [SerializeField] private Transform player;
        [SerializeField] private Player.PlayerMotor motor;

        private Vector3 _lastPlayerPosition;

        private void Awake()
        {
            if (guard == null) guard = GetComponent<GuardAI>();
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

            float distance = Vector3.Distance(transform.position, player.position);
            if (distance > scentRange) return;

            // 用位移差值算速度：比读 PlayerMotor.CurrentSpeed 更可靠（被推、下落、传送都能识别）
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float moved = Vector3.Distance(player.position, _lastPlayerPosition);
            _lastPlayerPosition = player.position;
            float speed = moved / dt;

            if (speed <= stillnessSpeed) return;

            guard.AddDetection(scentRatePerSecond * dt);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.6f, 0.2f, 0.9f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, scentRange);
        }
    }
}
