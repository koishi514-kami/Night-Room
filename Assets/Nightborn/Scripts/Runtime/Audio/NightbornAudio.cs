using UnityEngine;
using Nightborn.Detection;
using Nightborn.Level;
using Nightborn.Player;

namespace Nightborn.Audio
{
    /// <summary>
    /// 音频总控（M2）：环境 drone + 事件音效 + 脚步 + 低血心跳。
    ///
    /// 音源全部是运行时合成的占位 clip（见 <see cref="SynthAudio"/>），
    /// 正式音效包接入时只需把这里的字段换成 AudioClip 资源引用。
    /// </summary>
    [DisallowMultipleComponent]
    public class NightbornAudio : MonoBehaviour
    {
        [Header("音量")]
        [Range(0f, 1f)] [SerializeField] private float ambientVolume = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.7f;
        [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.28f;

        [Header("脚步节奏")]
        [SerializeField] private float walkStepInterval = 0.52f;
        [SerializeField] private float sneakStepInterval = 1.05f;
        [SerializeField] private float minSpeedForStep = 0.4f;

        private AudioSource _ambient;
        private AudioSource _sfx;
        private AudioSource _step;
        private AudioSource _heart;

        private AudioClip _footstep;
        private AudioClip _douse;
        private AudioClip _heartbeat;
        private AudioClip _alert;
        private AudioClip _caught;
        private AudioClip _complete;

        private PlayerMotor _motor;
        private BloodSystem _blood;
        private DetectionMeter _detection;
        private ShiftForm _shift;
        private PlayerFeeder _feeder;
        private LevelFlow _flow;

        private float _stepTimer;
        private float _heartTimer;
        private bool _wasFeeding;

        private void Awake()
        {
            _footstep = SynthAudio.Footstep();
            _douse = SynthAudio.Douse();
            _heartbeat = SynthAudio.Heartbeat();
            _alert = SynthAudio.Alert();
            _caught = SynthAudio.Caught();
            _complete = SynthAudio.Complete();

            _ambient = MakeSource("AUDIO_Ambient", true, ambientVolume);
            _ambient.clip = SynthAudio.Drone();
            _ambient.Play();

            _sfx = MakeSource("AUDIO_Sfx", true, sfxVolume);
            _step = MakeSource("AUDIO_Step", false, footstepVolume);
            _heart = MakeSource("AUDIO_Heart", true, sfxVolume);
        }

        private void Start()
        {
            _motor = GetComponent<PlayerMotor>();
            _blood = GetComponent<BloodSystem>();
            _detection = GetComponent<DetectionMeter>();
            _shift = GetComponent<ShiftForm>();
            _feeder = GetComponent<PlayerFeeder>();
            _flow = FindObjectOfType<LevelFlow>();

            if (_blood != null) _blood.Died += () => Play(_caught, 0.9f);
            if (_detection != null) _detection.StateChanged += OnDetectionStateChanged;
            if (_shift != null) _shift.FormChanged += OnFormChanged;
            if (_flow != null)
            {
                _flow.CaughtEvent += () => Play(_caught, 0.85f);
                _flow.LevelCompleted += () => Play(_complete, 0.8f);
            }

            DouseableLamp.DousedEvent += OnLampDoused;
            Relic.CollectedAny += OnRelicCollected;
        }

        private void OnDestroy()
        {
            DouseableLamp.DousedEvent -= OnLampDoused;
            Relic.CollectedAny -= OnRelicCollected;
        }

        private AudioSource MakeSource(string name, bool loop, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.volume = volume;
            src.spatialBlend = 0f; // 占位阶段统一 2D，避免听感位置错乱
            return src;
        }

        private void Play(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || _sfx == null) return;
            _sfx.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * volumeScale));
        }

        private void OnLampDoused(Vector3 position)
        {
            _step.transform.position = position;
            Play(_douse, 0.9f);
        }

        private void OnRelicCollected(Vector3 position)
        {
            Play(_complete, 0.35f);
        }

        private void OnFormChanged(bool shadow)
        {
            Play(_douse, shadow ? 0.55f : 0.35f);
        }

        private void OnDetectionStateChanged(DetectionMeter.DetectionState state)
        {
            if (state == DetectionMeter.DetectionState.Calm) return;
            Play(_alert, state == DetectionMeter.DetectionState.Hunted ? 1f : 0.6f);
        }

        private void Update()
        {
            if (_motor == null) return;
            UpdateFootsteps();
            UpdateHeartbeat();
            UpdateFeeding();
        }

        private void UpdateFootsteps()
        {
            float speed = _motor.CurrentSpeed;
            if (speed < minSpeedForStep)
            {
                _stepTimer = 0f;
                return;
            }

            float interval = _motor.IsSneaking ? sneakStepInterval : walkStepInterval;
            _stepTimer -= Time.deltaTime;
            if (_stepTimer > 0f) return;

            _stepTimer = interval;
            _step.pitch = Random.Range(0.9f, 1.1f);
            _step.volume = footstepVolume * (_motor.IsSneaking ? 0.5f : 1f);
            _step.PlayOneShot(_footstep, _step.volume);
        }

        private void UpdateHeartbeat()
        {
            if (_blood == null) return;

            float health = _blood.Normalized;
            if (health >= 0.35f)
            {
                _heartTimer = 0f;
                return;
            }

            float t = 1f - health / 0.35f;
            float interval = Mathf.Lerp(0.95f, 0.42f, t);
            _heartTimer -= Time.deltaTime;
            if (_heartTimer > 0f) return;

            _heartTimer = interval;
            _heart.PlayOneShot(_heartbeat, sfxVolume * Mathf.Lerp(0.25f, 0.6f, t));
        }

        private void UpdateFeeding()
        {
            if (_feeder == null) return;
            if (_feeder.IsFeeding == _wasFeeding) return;

            _wasFeeding = _feeder.IsFeeding;
            if (_wasFeeding) Play(_heartbeat, 0.5f);
        }
    }
}
