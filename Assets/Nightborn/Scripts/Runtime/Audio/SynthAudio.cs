using UnityEngine;

namespace Nightborn.Audio
{
    /// <summary>
    /// 运行时合成音效（占位资源）。
    ///
    /// 工程里没有音频素材，也不希望为了占位去背版权风险，所以先全部用代码合成：
    /// 脚步（噪声脉冲）、吹灯（嘶声）、心跳（低频砰）、警觉（上滑音）、通关（双音和弦）、环境（低频 drone）。
    /// M3 接入正式音效包时，把 <see cref="NightbornAudio"/> 里的 clip 引用换成 AudioClip 资源即可。
    /// </summary>
    public static class SynthAudio
    {
        private const int SampleRate = 44100;

        public static AudioClip Footstep()
        {
            int n = (int)(SampleRate * 0.10f);
            var data = new float[n];
            var rng = new System.Random(7);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-18f * t);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.12f; // 简易低通，去掉刺耳的高频
                data[i] = lp * env * 0.55f;
            }

            return Make("SFX_Footstep", data);
        }

        public static AudioClip Douse()
        {
            int n = (int)(SampleRate * 0.45f);
            var data = new float[n];
            var rng = new System.Random(11);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-4f * t) * (1f - Mathf.Exp(-40f * t));
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.35f;
                data[i] = lp * env * 0.4f;
            }

            return Make("SFX_Douse", data);
        }

        public static AudioClip Heartbeat()
        {
            int n = (int)(SampleRate * 0.22f);
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-14f * t);
                float s = Mathf.Sin(2f * Mathf.PI * 52f * t) * 0.8f + Mathf.Sin(2f * Mathf.PI * 104f * t) * 0.2f;
                data[i] = s * env * 0.6f;
            }

            return Make("SFX_Heartbeat", data);
        }

        public static AudioClip Alert()
        {
            int n = (int)(SampleRate * 0.25f);
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float freq = Mathf.Lerp(620f, 1180f, t);
                float env = Mathf.Sin(Mathf.PI * t) * 0.7f;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)SampleRate)) * env * 0.35f;
            }

            return Make("SFX_Alert", data);
        }

        public static AudioClip Caught()
        {
            int n = (int)(SampleRate * 0.7f);
            var data = new float[n];
            var rng = new System.Random(3);

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-4f * t);
                float low = Mathf.Sin(2f * Mathf.PI * 46f * (i / (float)SampleRate));
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.25f;
                data[i] = (low * 0.7f + noise) * env * 0.5f;
            }

            return Make("SFX_Caught", data);
        }

        public static AudioClip Complete()
        {
            int n = (int)(SampleRate * 1.4f);
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-2.2f * t);
                float a = Mathf.Sin(2f * Mathf.PI * 523f * t);
                float b = Mathf.Sin(2f * Mathf.PI * 784f * t) * 0.7f;
                data[i] = (a + b) * env * 0.22f;
            }

            return Make("SFX_Complete", data);
        }

        public static AudioClip Drone(float seconds = 6f)
        {
            int n = (int)(SampleRate * seconds);
            var data = new float[n];
            var rng = new System.Random(23);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float baseTone = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 62f * t) * 0.25f;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.01f;
                data[i] = (baseTone + lp * 2.5f) * 0.10f;
            }

            // 首尾交叉淡化，保证循环无缝
            int fade = (int)(SampleRate * 0.25f);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] *= k;
                data[n - 1 - i] *= k;
            }

            return Make("SFX_Drone", data);
        }

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
