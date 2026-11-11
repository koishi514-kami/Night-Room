using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Nightborn.Boss;
using Nightborn.Level;
using Nightborn.Player;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// 自动巡察器（开发用真机验收工具，非发布内容）。
    ///
    /// 为什么需要它：`WalkthroughTest` 是「逐项反射点测」，回答的是「各个系统接没接对」；
    /// 而这个组件会**真的驱动玩家沿一条设计路线走到出口**，回答的是唯一重要的问题：
    /// 「这一关到底能不能走通、卡在哪一段、花了多久」。
    ///
    /// 工作方式：
    ///  · 路线表按场景名硬编码在 <see cref="Routes"/>（几何改了就要同步改路线）；
    ///  · 每帧把「目标方向」喂给输入层（InputSystem.QueueStateEvent 合成按键状态），
    ///    让 PlayerMotor 走自己的正常流程 —— 测的是真实移动，不是瞬移；
    ///  · 同时用反射把 `ThirdPersonCamera._yaw` 对准行进方向，这样只需按住 W；
    ///    反射不上就退化成 8 方向按键组合（也能走，只是会走 Z 字）；
    ///  · 到达半径内切换到下一步；某一步超时或位移停滞就判定「卡住」并就地记录，
    ///    然后瞬移到该步目标继续测后面的路段（一次跑完拿到全图结论）。
    ///
    /// 用法（MCP execute_code）：
    ///   Nightborn.DebugTools.LevelAutoPilot.Launch(true);   // ghost=true 先关敌人，只验几何
    ///   Nightborn.DebugTools.LevelAutoPilot.Launch(false);  // 再开敌人，验难度
    /// Console 里看 `[PILOT]` 行，末尾一行是总判定。
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelAutoPilot : MonoBehaviour
    {
        public enum Act
        {
            Walk,
            Douse,
            Shift,
            Potion,
            Wait
        }

        public class Step
        {
            public Vector3 Target;
            public Act Action;
            public string Label;
            public float Radius;
            public float Hold;

            public Step(float x, float z, string label, Act action = Act.Walk, float radius = 0.9f, float hold = 0f)
            {
                Target = new Vector3(x, 0f, z);
                Label = label;
                Action = action;
                Radius = radius;
                Hold = hold;
            }
        }

        [Header("行为")]
        [SerializeField] private bool ghost = true;
        [SerializeField] private float stepTimeout = 25f;
        [SerializeField] private float arriveRadius = 0.9f;
        [SerializeField] private float stuckSeconds = 2.5f;
        [SerializeField] private float stuckDistance = 0.12f;

        private PlayerMotor _motor;
        private CharacterController _cc;
        private ShiftForm _shift;
        private BloodSystem _blood;
        private DimPotion _potion;
        private LevelFlow _flow;
        private BossRitual _ritual;
        private Transform _cameraRig;
        private Nightborn.Cameras.ThirdPersonCamera _cam;
        private FieldInfo _yawField;

        private List<Step> _steps;
        private int _stepIndex;
        private float _stepTimer;
        private Vector3 _lastPos;
        private float _stuckTimer;
        private int _stuckCount;
        private int _caughtCount;
        private float _minBlood;
        private bool _finished;
        private bool _injectConfirmed;
        private bool _injectFallback;
        private bool _arrived;
        private float _runTimer;
        private bool _waitDone;

        private readonly List<string> _report = new List<string>();
        private readonly List<GameObject> _ghostDisabled = new List<GameObject>();

        public static LevelAutoPilot Launch(bool ghostMode)
        {
            var existing = Object.FindObjectOfType<LevelAutoPilot>();
            if (existing != null) Object.Destroy(existing.gameObject);

            var go = new GameObject("TEST_LevelAutoPilot");
            var pilot = go.AddComponent<LevelAutoPilot>();
            pilot.ghost = ghostMode;
            Debug.Log("[PILOT] 启动：场景=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                      " ghost=" + ghostMode);
            return pilot;
        }

        private void Start()
        {
            _motor = FindPlayerPart<PlayerMotor>();
            if (_motor == null)
            {
                Debug.LogError("[PILOT] 找不到 PLAYER_Ada / PlayerMotor，终止");
                return;
            }

            _cc = _motor.GetComponent<CharacterController>();
            _shift = _motor.GetComponent<ShiftForm>();
            _blood = _motor.GetComponent<BloodSystem>();
            _potion = _motor.GetComponent<DimPotion>();
            _flow = Object.FindObjectOfType<LevelFlow>();
            _ritual = Object.FindObjectOfType<BossRitual>();

            var rig = GameObject.Find("CAM_Rig");
            if (rig != null)
            {
                _cameraRig = rig.transform;
                _cam = rig.GetComponent<Nightborn.Cameras.ThirdPersonCamera>();
                if (_cam != null)
                    _yawField = typeof(Nightborn.Cameras.ThirdPersonCamera)
                        .GetField("_yaw", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            if (ghost) ApplyGhost(true);

            if (_flow != null) _flow.CaughtEvent += OnCaught;

            _minBlood = _blood != null ? _blood.Current : 0f;

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            _steps = Routes.Build(scene);
            if (_steps == null || _steps.Count == 0)
            {
                Debug.LogError("[PILOT] ❌ 没有这个场景的路线：" + scene + "（去 LevelAutoPilot.Routes 补）");
                Finish(false);
                return;
            }

            _lastPos = _motor.transform.position;
            Debug.Log("[PILOT] 路线 " + _steps.Count + " 段，起点 " + _lastPos.ToString("F1"));
        }

        private void OnDestroy()
        {
            if (_flow != null) _flow.CaughtEvent -= OnCaught;
            if (ghost) ApplyGhost(false);
            ReleaseKeys();
        }

        private void OnCaught()
        {
            _caughtCount++;
        }

        private T FindPlayerPart<T>() where T : Component
        {
            var go = GameObject.Find("PLAYER_Ada");
            return go != null ? go.GetComponent<T>() : Object.FindObjectOfType<T>();
        }

        /// <summary>
        /// 幽灵模式：只测几何可达性 → 把敌人**整只关掉**。
        /// 只禁用 GuardAI 组件不够：身体胶囊还是实心碰撞体（真机实测在教堂西拱门被一个
        /// 站着不动的猎犬堵死 25 s），它的灯也还在照（会把玩家烧到血 0 再回检查点）。
        /// </summary>
        private void ApplyGhost(bool on)
        {
            var guards = Object.FindObjectsOfType<Nightborn.Detection.GuardAI>();
            for (int i = 0; i < guards.Length; i++)
            {
                if (guards[i] == null) continue;
                var go = guards[i].gameObject;
                if (on)
                {
                    if (!_ghostDisabled.Contains(go)) _ghostDisabled.Add(go);
                    go.SetActive(false);
                }
                else if (_ghostDisabled.Contains(go))
                {
                    go.SetActive(true);
                    _ghostDisabled.Remove(go);
                }
            }
        }

        private void Update()
        {
            if (_finished || _steps == null || _motor == null) return;

            _runTimer += Time.deltaTime;
            if (_blood != null) _minBlood = Mathf.Min(_minBlood, _blood.Current);

            if (_flow != null && _flow.IsComplete)
            {
                Finish(true);
                return;
            }

            // 被抓会传送回检查点：路线进度作废，从头再走（也顺带验证了回滚）
            if (_caughtCount > 0 && _stepIndex > 0 &&
                Vector3.Distance(_motor.transform.position, _steps[_stepIndex - 1].Target) > 6f)
            {
                _report.Add("⚠ 被抓 " + _caughtCount + " 次，路线从第 1 段重走");
                _stepIndex = 0;
                _stepTimer = 0f;
                _waitDone = false;
            }

            var step = _steps[Mathf.Min(_stepIndex, _steps.Count - 1)];
            Vector3 pos = _motor.transform.position;
            Vector3 target = step.Target;
            float flatDistance = Vector3.Distance(new Vector3(pos.x, 0f, pos.z), target);

            // Wait 步骤：站定不动，等够 hold 秒（等化形冷却、让触发器结算）
            if (step.Action == Act.Wait)
            {
                ReleaseKeys();
                _stepTimer += Time.deltaTime;
                if (_stepTimer < step.Hold) return;
                CompleteStep(step, pos);
                return;
            }

            if (flatDistance <= Mathf.Max(step.Radius, arriveRadius))
            {
                CompleteStep(step, pos);
                return;
            }

            _stepTimer += Time.deltaTime;
            if (_stepTimer > stepTimeout)
            {
                _report.Add("❌ " + _stepIndex + " " + step.Label + " 超时 " + stepTimeout.ToString("F0") +
                            "s，停在 " + Fmt(pos) + "（目标 " + Fmt(target) + "，距离 " + flatDistance.ToString("F2") + "m）");
                Debug.Log("[PILOT] ❌ 超时：" + step.Label);
                _stuckCount++;
                TeleportTo(target + Vector3.up * 0.2f);
                _stepTimer = 0f;
                _lastPos = _motor.transform.position;
                _stuckTimer = 0f;
                return;
            }

            // 位移停滞检测
            float moved = Vector3.Distance(pos, _lastPos);
            _lastPos = pos;
            if (moved < stuckDistance * Time.deltaTime * 10f) _stuckTimer += Time.deltaTime;
            else _stuckTimer = 0f;

            if (_stuckTimer > stuckSeconds)
            {
                _report.Add("⚠ " + _stepIndex + " " + step.Label + " 卡住 " +
                            stuckSeconds.ToString("F1") + "s（停在 " + Fmt(pos) + "）");
                _stuckTimer = 0f;
            }

            DriveTowards(target);

            // 输入注入自检：跑了 1.5 秒还没有任何位移，就说明输入合成没生效
            if (!_injectConfirmed && !_injectFallback)
            {
                if (_runTimer > 1.5f && _motor.RawInput.sqrMagnitude < 0.01f && _motor.CurrentSpeed < 0.05f)
                {
                    Debug.LogWarning("[PILOT] ⚠ 输入合成似乎没生效（RawInput=0）→ 退化为直接位移");
                    _injectFallback = true;
                }
                else if (_motor.RawInput.sqrMagnitude > 0.01f)
                {
                    _injectConfirmed = true;
                    Debug.Log("[PILOT] ✅ 输入合成生效（RawInput=" + _motor.RawInput + "）");
                }
            }
        }

        private void CompleteStep(Step step, Vector3 pos)
        {
            string line = (DoAction(step) ? "✅ " : "⚠ ") + _stepIndex + " " + step.Label +
                          "  (" + _stepTimer.ToString("F1") + "s)  " + Fmt(pos);
            _report.Add(line);
            Debug.Log("[PILOT] " + line);

            _stepIndex++;
            _stepTimer = 0f;
            _lastPos = pos;
            _stuckTimer = 0f;

            if (_stepIndex >= _steps.Count)
            {
                // 最后一步走完还没通关：等 3 秒让触发器（出口 / 祭坛）结算
                _arrived = true;
                FinishAfterGrace();
            }
        }

        private bool DoAction(Step step)
        {
            switch (step.Action)
            {
                case Act.Douse:
                    return DouseNearest();
                case Act.Shift:
                    if (_shift == null) return false;
                    bool shadow = _shift.Toggle();
                    Debug.Log("[PILOT] 化形 → " + (shadow ? "影身" : "光身"));
                    return true;
                case Act.Potion:
                    if (_potion == null) return false;
                    _potion.Use();
                    Debug.Log("[PILOT] 喝压光药剂（剩余 " + _potion.Count + " 瓶）");
                    return true;
                case Act.Wait:
                    return true;
                default:
                    return true;
            }
        }

        private bool DouseNearest()
        {
            var lamps = Object.FindObjectsOfType<DouseableLamp>();
            DouseableLamp best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (lamps[i] == null || lamps[i].Doused) continue;
                float d = Vector3.Distance(lamps[i].transform.position, _motor.transform.position);
                if (d < bestDistance) { bestDistance = d; best = lamps[i]; }
            }

            if (best == null)
            {
                Debug.Log("[PILOT] ⚠ 附近没有可吹灭的灯");
                return false;
            }

            best.Interact(gameObject);
            Debug.Log("[PILOT] 吹灭 " + best.name + "（doused=" + best.Doused + "）");
            return best.Doused;
        }

        private void TeleportTo(Vector3 p)
        {
            if (_cc != null) _cc.enabled = false;
            _motor.transform.position = p;
            if (_cc != null) _cc.enabled = true;
        }

        /// <summary>把「往哪走」翻译成真的按键。</summary>
        private void DriveTowards(Vector3 target)
        {
            Vector3 pos = _motor.transform.position;
            Vector3 dir = target - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            if (_injectFallback)
            {
                // 退化路径：直接位移（仍然会被 CharacterController 的碰撞挡住，所以能测几何）
                if (_cc != null) _cc.Move(dir * 3.0f * Time.deltaTime);
                return;
            }

            bool steered = false;
            if (_yawField != null && _cam != null)
            {
                float baseYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float a = DotForward(baseYaw, dir);
                float b = DotForward(baseYaw + 180f, dir);
                float yaw = a >= b ? baseYaw : baseYaw + 180f;
                _yawField.SetValue(_cam, yaw);
                steered = true;
            }

            if (steered)
            {
                QueueKeys(Key.W);
                return;
            }

            // 没有相机偏航可改：按相机自身朝向做 8 方向按键
            Vector3 fwd = _cameraRig != null ? _cameraRig.forward : Vector3.forward;
            Vector3 right = _cameraRig != null ? _cameraRig.right : Vector3.right;
            fwd.y = 0f; right.y = 0f;
            fwd.Normalize(); right.Normalize();

            float f = Vector3.Dot(dir, fwd);
            float r = Vector3.Dot(dir, right);
            var keys = new List<Key>(2);
            if (f > 0.35f) keys.Add(Key.W);
            else if (f < -0.35f) keys.Add(Key.S);
            if (r > 0.35f) keys.Add(Key.D);
            else if (r < -0.35f) keys.Add(Key.A);
            if (keys.Count == 0) keys.Add(Key.W);
            QueueKeys(keys.ToArray());
        }

        private float DotForward(float yaw, Vector3 dir)
        {
            Vector3 f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            return Vector3.Dot(f, dir);
        }

        private void QueueKeys(params Key[] keys)
        {
            if (Keyboard.current == null) return;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        }

        private void ReleaseKeys()
        {
            if (Keyboard.current == null) return;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }

        private void FinishAfterGrace()
        {
            if (!IsInvoking(nameof(FinishGrace))) Invoke(nameof(FinishGrace), 3f);
        }

        private void FinishGrace()
        {
            Finish(_flow != null && _flow.IsComplete);
        }

        private void Finish(bool complete)
        {
            if (_finished) return;
            _finished = true;
            ReleaseKeys();
            if (_flow != null) _flow.CaughtEvent -= OnCaught;
            CancelInvoke();

            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var sb = new StringBuilder();
            sb.Append("[PILOT] ===== ").Append(scene).Append(" ghost=").Append(ghost)
              .Append(" 总判定：").Append(complete ? "✅ 通关" : "❌ 未通关").Append(" =====\n");
            sb.Append("[PILOT] 用时 ").Append(_runTimer.ToString("F1")).Append("s，段 ")
              .Append(Mathf.Min(_stepIndex, _steps != null ? _steps.Count : 0)).Append("/")
              .Append(_steps != null ? _steps.Count : 0)
              .Append("，卡住/超时 ").Append(_stuckCount)
              .Append("，被抓 ").Append(_caughtCount)
              .Append("，血最低 ").Append(_minBlood.ToString("F0"))
              .Append("，遗物 ").Append(_flow != null ? _flow.Relics + "/" + _flow.RelicTotal : "-")
              .Append("\n");
            for (int i = 0; i < _report.Count; i++) sb.Append("[PILOT] ").Append(_report[i]).Append("\n");
            if (_ritual != null)
                sb.Append("[PILOT] Boss 阶段 ").Append(_ritual.CurrentStage)
                  .Append(" 灯 ").Append(_ritual.LampsDoused)
                  .Append(" 门 ").Append(_ritual.GatesPassed)
                  .Append(" 距祭坛 ").Append(_ritual.DistanceToAltar.ToString("F2")).Append("\n");

            Debug.Log(sb.ToString());
        }

        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("F1") + "," + v.y.ToString("F1") + "," + v.z.ToString("F1") + ")";
        }

        /// <summary>
        /// 设计路线表。**几何改了就要同步改这里**（坐标来自 NightbornBootstrap 生成的场景）。
        ///
        /// 2026-11-11 按关卡子系统的几何重做同步过一次：
        ///  · 街区：北门 z=16 有横墙 + 薄幕（Tab 是硬门槛），中庭有两只箱挡住 x≈0；
        ///  · 教堂：z=9 隔屏只留 x ∓14..∓11 / 11..14 两座拱门，各挂薄幕；
        ///  · 宅邸：坡道改成南北走向（坡脚 z≈−13.1、坡顶 z=−0.70），栏杆缺口 z −2.7..2.7；
        ///  · 终章：圣灯三盏 + 侧堂两道门（薄幕在 x ±12.9）+ 祭坛两级台阶（z 8.7 / 9.7）。
        /// </summary>
        public static class Routes
        {
            public static List<Step> Build(string scene)
            {
                var list = new List<Step>();

                switch (scene)
                {
                    case "L_Ch01_S01_Street":
                    case "L_Ch01_S02_Street":
                    case "L_Ch01_S03_Street":
                        // 出生走廊 → 中庭（绕开 (−0.8,5.2)/(0.6,6.2) 两只箱）→ 北门前暗角检查点
                        // (−8.5,13.5) → 回中轴 → Tab 穿 z=16 的薄幕 → 出口 (0,19)
                        // 矮墙（x −8..2）与 BLD_A / BLD_B 之间留出 x 2..4 的中路缺口，沿 x≈2.4 一路北上
                        list.Add(new Step(0f, -13f, "出生走廊"));
                        list.Add(new Step(2.4f, -8f, "对准中路缺口"));
                        list.Add(new Step(2.4f, -1f, "穿过矮墙缺口"));
                        list.Add(new Step(2.4f, 9f, "中庭北上"));
                        list.Add(new Step(-2f, 12.8f, "折向北门区"));
                        list.Add(new Step(-8.5f, 13.5f, "检查点 1"));
                        list.Add(new Step(0f, 14.4f, "回到门洞前"));
                        list.Add(new Step(0f, 14.6f, "化形（北门薄幕）", Act.Shift, 1.2f));
                        list.Add(new Step(0f, 18.2f, "穿过薄幕"));
                        list.Add(new Step(0f, 19f, "北门出口"));
                        break;

                    case "L_Ch02_S01_Church":
                    case "L_Ch02_S02_Church":
                    case "L_Ch02_S03_Church":
                        list.Add(new Step(0f, -13f, "出生走廊"));
                        list.Add(new Step(-3f, 0f, "中殿北上"));
                        list.Add(new Step(-4.5f, 7.5f, "检查点 1"));
                        // 柱廊柱子在 x=±6 / z=−14,−7,0,7,14，所以横移要避开 z≈7 那一根
                        list.Add(new Step(-9f, 8.2f, "西拱门前"));
                        list.Add(new Step(-12.4f, 8.3f, "化形（西拱门薄幕）", Act.Shift, 1.2f));
                        list.Add(new Step(-12.5f, 11f, "进入圣所侧廊"));
                        list.Add(new Step(-10f, 17f, "侧廊北上"));
                        list.Add(new Step(-10f, 18.6f, "绕过祭坛"));
                        list.Add(new Step(0f, 19f, "北门出口"));
                        break;

                    case "L_Ch03_S01_Mansion":
                    case "L_Ch03_S02_Mansion":
                    case "L_Ch03_S03_Mansion":
                        list.Add(new Step(0f, -13f, "出生走廊"));
                        list.Add(new Step(-10f, -13.4f, "坡脚"));
                        list.Add(new Step(-10f, -8f, "上坡下段"));
                        list.Add(new Step(-10f, -1.2f, "坡道顶端"));
                        list.Add(new Step(-6f, -0.5f, "走上二层回廊"));
                        list.Add(new Step(-4.5f, 10.5f, "二层检查点 1"));
                        list.Add(new Step(4f, 11f, "塔门前"));
                        list.Add(new Step(6f, 11.2f, "化形（塔门薄幕）", Act.Shift, 1.2f));
                        list.Add(new Step(6f, 14f, "穿过塔门"));
                        list.Add(new Step(13f, 17.5f, "二层出口"));
                        break;

                    case "L_Ch04_S01_Cathedral":
                        list.Add(new Step(0f, -13f, "出生走廊"));
                        list.Add(new Step(-10f, -4f, "圣灯 A", Act.Douse, 2.2f));
                        list.Add(new Step(10f, -4f, "圣灯 B", Act.Douse, 2.2f));
                        list.Add(new Step(0f, 6f, "圣灯 C", Act.Douse, 2.2f));
                        list.Add(new Step(-11.6f, 6f, "西堂口前"));
                        list.Add(new Step(-11.8f, 6f, "化形（西堂口薄幕）", Act.Shift, 1.2f));
                        list.Add(new Step(-15.2f, 6f, "西仪式门"));
                        list.Add(new Step(-11.6f, 6f, "退回西堂口"));
                        list.Add(new Step(0f, 5f, "回中轴"));
                        // 化形冷却 8 s：等够再进东堂（否则 Toggle 被冷却挡掉，人物撞在幕上）
                        list.Add(new Step(0f, 5f, "等化形冷却", Act.Wait, 1.2f, 9f));
                        list.Add(new Step(11.6f, 6f, "东堂口前"));
                        list.Add(new Step(11.8f, 6f, "化形（东堂口薄幕）", Act.Shift, 1.2f));
                        list.Add(new Step(15.2f, 6f, "东仪式门"));
                        list.Add(new Step(11.6f, 6f, "退回东堂口"));
                        list.Add(new Step(0f, 8f, "祭坛台阶前"));
                        list.Add(new Step(0f, 8f, "压光（1 号）", Act.Potion, 1.2f));
                        list.Add(new Step(0f, 12.5f, "走上祭坛"));
                        break;

                    case "L_Main":
                        Debug.Log("[PILOT] 菜单场景不需要巡察");
                        break;
                }

                return list;
            }
        }
    }
}
