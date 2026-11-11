using UnityEngine;
using UnityEngine.Audio;

namespace AirsoftFPS
{
    // Wind you can hear: a looping stereo noise bed (made here, no asset) whose loudness follows the match
    // wind (BBBallistics.Wind, so the lobby setting × the map's wind factor) with slow gusts on top. A gust
    // also opens the low-pass filter, so it whooshes brighter and dies back to a low rumble.
    // Under a roof (something solid straight above the listener) it drops to a muffled third.
    // Calm wind, no match or Settings > Audio > Ambience off = silent. Lives on the AudioHub (AirsoftAudio), plays on the SFX bus.
    public class WindAmbience : MonoBehaviour
    {
        const int RATE = 44100;
        const float LOOP_SECONDS = 7f;
        const float MAX_VOLUME = 0.42f;      // at FULL_SPEED outdoors, before the gusts
        const float FULL_SPEED = 6f;         // m/s (Strong wind on a fully exposed map is ~5-6)
        const float INDOOR = 0.32f;          // volume factor under a roof
        const float ROOF_CHECK = 14f;        // metres above the listener

        // Checks: the wind bed is playing right now (and how loud).
        public static bool Playing => instance != null && instance.src != null && instance.src.isPlaying;
        public static float Volume => Playing ? instance.src.volume : 0f;
        static WindAmbience instance;

        AudioSource src;
        AudioLowPassFilter lpf;
        AudioHub hub;
        float level, indoor, nextRoofCheck, seed;
        bool roof;

        public void Init(AudioHub owner, AudioMixerGroup sfx)
        {
            hub = owner;
            instance = this;
            seed = Random.Range(0f, 100f);
            var go = new GameObject("wind");
            go.transform.SetParent(transform, false);
            src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.priority = 32;
            src.volume = 0f;
            src.outputAudioMixerGroup = sfx;
            src.clip = MakeClip();
            lpf = go.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = 900f;
        }

        // Stereo wind noise: brown-ish rumble plus a softer hiss, independent per channel (wide image),
        // with the end cross-faded into the start so the loop has no click.
        static AudioClip MakeClip()
        {
            int n = (int)(RATE * LOOP_SECONDS);
            int fade = RATE / 2;
            var rng = new System.Random(4711);
            var ch = new float[2][];
            for (int c = 0; c < 2; c++)
            {
                var s = new float[n + fade];
                float brown = 0f, lp = 0f;
                for (int i = 0; i < s.Length; i++)
                {
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    brown = (brown + white * 0.02f) * 0.997f;      // leaky integrator: deep rumble
                    lp += (white - lp) * 0.08f;                     // gentle hiss
                    s[i] = brown * 3.2f + lp * 0.35f;
                }
                for (int i = 0; i < fade; i++)
                {
                    float t = i / (float)fade;
                    s[i] = s[i] * t + s[n + i] * (1f - t);
                }
                float peak = 0.0001f;
                for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(s[i]));
                for (int i = 0; i < n; i++) s[i] *= 0.9f / peak;
                ch[c] = s;
            }
            var data = new float[n * 2];
            for (int i = 0; i < n; i++) { data[2 * i] = ch[0][i]; data[2 * i + 1] = ch[1][i]; }
            var clip = AudioClip.Create("WindLoop", n, 2, RATE, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Update()
        {
            if (src == null) return;
            float dt = Time.unscaledDeltaTime;
            float speed = BBBallistics.Wind.magnitude;
            float target = GameSettings.Ambience ? Mathf.Pow(Mathf.Clamp01(speed / FULL_SPEED), 0.8f) : 0f;
            level = Mathf.MoveTowards(level, target, dt * 0.5f);   // fades in / out over ~2 s

            if (Time.unscaledTime >= nextRoofCheck && hub != null)
            {
                nextRoofCheck = Time.unscaledTime + 0.25f;
                var l = hub.ActiveListener();
                roof = l != null && Physics.Raycast(l.transform.position, Vector3.up, ROOF_CHECK, ~0, QueryTriggerInteraction.Ignore);
            }
            indoor = Mathf.MoveTowards(indoor, roof ? 1f : 0f, dt * 1.5f);

            if (level < 0.002f)
            {
                if (src.isPlaying) src.Stop();
                return;
            }
            if (!src.isPlaying)
            {
                src.timeSamples = Random.Range(0, src.clip.samples);
                src.Play();
            }

            // gusts: slow swell plus faster flutter, stronger wind = deeper gusts
            float t = Time.time + seed;
            float slow = Mathf.PerlinNoise(t * 0.13f, seed);
            float fast = Mathf.PerlinNoise(t * 0.7f, seed + 7.3f);
            float gust = Mathf.Clamp01(slow * 0.75f + fast * 0.25f);
            float depth = Mathf.Lerp(0.35f, 0.75f, target);
            float g = 1f - depth + depth * gust * 1.25f;

            float vol = MAX_VOLUME * level * g * Mathf.Lerp(1f, INDOOR, indoor);
            src.volume = vol * AirsoftAudio.FallbackBusVolume(AirsoftAudio.Bus.Sfx);
            float open = Mathf.Lerp(500f, 2600f, gust * (0.4f + 0.6f * target));
            lpf.cutoffFrequency = Mathf.Lerp(open, 380f, indoor * 0.8f);
        }
    }
}
