using System;
using UnityEngine;

namespace CoachingPropHunt.Core
{
    /// <summary>
    /// Procedural PCM audio synthesis & mixing hub. Generates crisp game SFX,
    /// spatial 3D whistle taunts, gentle coaching hall ambient drone, and persistent
    /// volume settings with zero external audio-file dependencies.
    /// </summary>
    public sealed class AudioSynthManager : MonoBehaviour
    {
        public static AudioSynthManager Instance { get; private set; }

        private const int SampleRate = 44100;
        private const string PrefMusicKey = "CoachingPropHunt_MusicVol";
        private const string PrefSfxKey = "CoachingPropHunt_SfxVol";

        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.85f;

        private AudioSource _sfxSource;
        private AudioSource _ambientSource;
        private AudioSource[] _spatialPool;
        private int _spatialCursor;

        private AudioClip _clipClick;
        private AudioClip _clipMorph;
        private AudioClip _clipSolidHit;
        private AudioClip _clipWrongBuzz;
        private AudioClip _clipEliminate;
        private AudioClip _clipWhistleA;
        private AudioClip _clipWhistleB;
        private AudioClip _clipRadarPing;
        private AudioClip _clipVictory;
        private AudioClip _clipAmbient;
        private AudioClip _clipFootstep;
        private AudioClip _clipLand;
        private AudioClip _clipBlaster;
        private AudioClip _clipStun;

        public float MusicVolume => musicVolume;
        public float SfxVolume => sfxVolume;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent == null && GetComponent<GameStateManager>() == null)
                DontDestroyOnLoad(gameObject);

            musicVolume = PlayerPrefs.GetFloat(PrefMusicKey, 0.55f);
            sfxVolume = PlayerPrefs.GetFloat(PrefSfxKey, 0.85f);

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f;

            _ambientSource = gameObject.AddComponent<AudioSource>();
            _ambientSource.loop = true;
            _ambientSource.spatialBlend = 0f;

            _spatialPool = new AudioSource[6];
            for (int i = 0; i < _spatialPool.Length; i++)
            {
                var spatialGo = new GameObject($"SpatialAudio_{i}");
                spatialGo.transform.SetParent(transform);
                var src = spatialGo.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1.0f;
                src.minDistance = 4.5f;
                src.maxDistance = 26.0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                _spatialPool[i] = src;
            }

            BuildAllClips();
            ApplyVolumes();

            if (_clipAmbient != null && !_ambientSource.isPlaying)
            {
                _ambientSource.clip = _clipAmbient;
                _ambientSource.Play();
            }
        }

        public void SetVolumes(float music, float sfx)
        {
            musicVolume = Mathf.Clamp01(music);
            sfxVolume = Mathf.Clamp01(sfx);
            PlayerPrefs.SetFloat(PrefMusicKey, musicVolume);
            PlayerPrefs.SetFloat(PrefSfxKey, sfxVolume);
            PlayerPrefs.Save();
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (_sfxSource != null) _sfxSource.volume = sfxVolume;
            if (_spatialPool != null)
            {
                for (int i = 0; i < _spatialPool.Length; i++)
                    if (_spatialPool[i] != null) _spatialPool[i].volume = sfxVolume;
            }
            if (_ambientSource != null) _ambientSource.volume = musicVolume * 0.48f;
        }

        public void PlayClick() => Play2D(_clipClick, 0.55f);
        public void PlayMorphPop() => Play2D(_clipMorph, 0.78f);
        public void PlayHitSolid() => Play2D(_clipSolidHit, 0.85f);
        public void PlayWrongHitBuzz() => Play2D(_clipWrongBuzz, 0.75f);
        public void PlayEliminationSting() => Play2D(_clipEliminate, 0.90f);
        public void PlayRadarPing() => Play2D(_clipRadarPing, 0.82f);
        public void PlayVictorySting() => Play2D(_clipVictory, 0.95f);

        public void PlayBlasterShotAt(Vector3 worldPos) => PlaySpatial(_clipBlaster, worldPos, 0.80f, UnityEngine.Random.Range(0.95f, 1.07f));
        public void PlayBlasterShot() => Play2D(_clipBlaster, 0.65f);
        public void PlayStunBashAt(Vector3 worldPos) => PlaySpatial(_clipStun, worldPos, 0.95f, UnityEngine.Random.Range(0.96f, 1.05f));

        public void PlayFootstep(Vector3 worldPos, bool isLanding = false)
        {
            var clip = isLanding ? _clipLand : _clipFootstep;
            PlaySpatial(clip, worldPos, isLanding ? 0.85f : 0.62f, UnityEngine.Random.Range(0.90f, 1.10f));
        }

        public void PlayWhistleTaunt(Vector3 worldPos, int variant = 0)
        {
            var clip = (variant % 2 == 0) ? _clipWhistleA : _clipWhistleB;
            PlaySpatial(clip, worldPos, 1.0f, UnityEngine.Random.Range(0.95f, 1.06f));
        }

        private void PlaySpatial(AudioClip clip, Vector3 worldPos, float volumeScale, float pitch)
        {
            if (clip == null || _spatialPool == null || _spatialPool.Length == 0 || sfxVolume <= 0.001f) return;
            var src = _spatialPool[_spatialCursor];
            _spatialCursor = (_spatialCursor + 1) % _spatialPool.Length;
            if (src == null) return;
            src.transform.position = worldPos;
            src.pitch = pitch;
            src.PlayOneShot(clip, sfxVolume * volumeScale);
        }

        private void Play2D(AudioClip clip, float scale)
        {
            if (clip == null || _sfxSource == null || sfxVolume <= 0.001f) return;
            _sfxSource.pitch = UnityEngine.Random.Range(0.97f, 1.03f);
            _sfxSource.PlayOneShot(clip, sfxVolume * scale);
        }

        private void BuildAllClips()
        {
            _clipClick = SynthSweep("sfx_click", 0.055f, 740f, 980f, 0.35f, false);
            _clipMorph = SynthArpeggio("sfx_morph", new[] { 392f, 523.25f, 659.25f }, 0.075f, 0.48f);
            _clipSolidHit = SynthImpact("sfx_hit_solid", 0.14f, 180f, 68f, 0.65f);
            _clipWrongBuzz = SynthDualTone("sfx_wrong_buzz", 0.24f, 145f, 152f, 0.45f);
            _clipEliminate = SynthArpeggio("sfx_eliminate", new[] { 587.3f, 440f, 293.7f }, 0.11f, 0.58f);
            _clipWhistleA = SynthSweep("sfx_whistle_a", 0.38f, 1250f, 1880f, 0.55f, true);
            _clipWhistleB = SynthArpeggio("sfx_whistle_b", new[] { 1320f, 1660f, 1480f }, 0.10f, 0.50f);
            _clipRadarPing = SynthSweep("sfx_radar", 0.32f, 880f, 1760f, 0.52f, false);
            _clipVictory = SynthArpeggio("sfx_victory", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.12f, 0.60f);
            _clipAmbient = SynthBackgroundMusicLoop("bgm_parishram_groove", 8.0f);
            _clipFootstep = SynthImpact("sfx_footstep", 0.068f, 185f, 58f, 0.72f);
            _clipLand = SynthImpact("sfx_land", 0.115f, 155f, 48f, 0.85f);
            _clipBlaster = SynthSweep("sfx_blaster", 0.14f, 1460f, 290f, 0.48f, false);
            _clipStun = SynthImpact("sfx_stun_bash", 0.25f, 340f, 72f, 0.72f);
        }

        private static AudioClip SynthSweep(string clipName, float dur, float f0, float f1, float amp, bool vibrato)
        {
            int len = Mathf.Max(256, Mathf.RoundToInt(dur * SampleRate));
            var data = new float[len];
            int fade = Mathf.Max(32, SampleRate / 200);
            double phase = 0.0;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / len;
                float freq = Mathf.Lerp(f0, f1, Mathf.SmoothStep(0f, 1f, t));
                if (vibrato) freq += Mathf.Sin(t * 28f * Mathf.PI) * 55f;
                phase += 2.0 * Math.PI * freq / SampleRate;
                float env = Envelope(i, len, fade) * (1f - t * 0.45f);
                data[i] = (float)(Math.Sin(phase) * amp * env);
            }
            var clip = AudioClip.Create(clipName, len, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip SynthArpeggio(string clipName, float[] notes, float noteDur, float amp)
        {
            int perNote = Mathf.RoundToInt(noteDur * SampleRate);
            int total = perNote * notes.Length;
            var data = new float[total];
            int fade = Mathf.Max(32, SampleRate / 220);
            for (int n = 0; n < notes.Length; n++)
            {
                double freq = notes[n];
                for (int i = 0; i < perNote; i++)
                {
                    int idx = n * perNote + i;
                    float localT = (float)i / perNote;
                    float env = Envelope(i, perNote, fade) * (1f - localT * 0.35f);
                    double s = Math.Sin(2.0 * Math.PI * freq * i / SampleRate) * 0.78
                             + Math.Sin(4.0 * Math.PI * freq * i / SampleRate) * 0.22;
                    data[idx] = (float)(s * amp * env);
                }
            }
            var clip = AudioClip.Create(clipName, total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip SynthImpact(string clipName, float dur, float f0, float f1, float amp)
        {
            int len = Mathf.RoundToInt(dur * SampleRate);
            var data = new float[len];
            int fade = Mathf.Max(16, SampleRate / 400);
            var rng = new System.Random(42);
            double phase = 0.0;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / len;
                float freq = Mathf.Lerp(f0, f1, Mathf.Sqrt(t));
                phase += 2.0 * Math.PI * freq / SampleRate;
                float punch = (float)Math.Sin(phase);
                float click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 24f) * 0.35f;
                float env = Envelope(i, len, fade) * Mathf.Exp(-t * 8.5f);
                data[i] = (punch + click) * amp * env;
            }
            var clip = AudioClip.Create(clipName, len, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip SynthDualTone(string clipName, float dur, float f1, float f2, float amp)
        {
            int len = Mathf.RoundToInt(dur * SampleRate);
            var data = new float[len];
            int fade = Mathf.Max(32, SampleRate / 200);
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / len;
                double s = Math.Sign(Math.Sin(2.0 * Math.PI * f1 * i / SampleRate)) * 0.5
                         + Math.Sign(Math.Sin(2.0 * Math.PI * f2 * i / SampleRate)) * 0.5;
                data[i] = (float)(s * amp * Envelope(i, len, fade) * (1f - t * 0.3f));
            }
            var clip = AudioClip.Create(clipName, len, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip SynthBackgroundMusicLoop(string clipName, float dur)
        {
            int len = Mathf.RoundToInt(dur * SampleRate);
            var data = new float[len];

            double[][] chords =
            {
                new[] { 220.00, 261.63, 329.63, 392.00 },
                new[] { 174.61, 220.00, 261.63, 329.63 },
                new[] { 261.63, 329.63, 392.00, 493.88 },
                new[] { 196.00, 246.94, 293.66, 329.63 }
            };
            double[] bassRoots = { 110.00, 87.31, 130.81, 98.00 };
            double[] melodyNotes =
            {
                523.25, 659.25, 587.33, 523.25, 440.00, 523.25, 659.25, 783.99,
                698.46, 659.25, 523.25, 440.00, 587.33, 493.88, 392.00, 440.00
            };

            const float beatDur = 0.5f;
            for (int i = 0; i < len; i++)
            {
                float tSec = (float)i / SampleRate;
                int bar = Mathf.Clamp((int)(tSec / 2.0f), 0, 3);
                float inBar = tSec - bar * 2.0f;
                int beatIdx = Mathf.Clamp((int)(tSec / beatDur), 0, 15);
                float inBeat = tSec - beatIdx * beatDur;
                float barEnv = Mathf.Clamp01(inBar / 0.04f) * Mathf.Clamp01((2.0f - inBar) / 0.04f);

                double chordSum = 0.0;
                double[] ch = chords[bar];
                for (int k = 0; k < ch.Length; k++)
                {
                    double f = Math.Round(ch[k] * dur) / dur;
                    chordSum += Math.Sin(2.0 * Math.PI * f * tSec) * (0.14 / (1.0 + k * 0.25));
                }

                double bf = Math.Round(bassRoots[bar] * dur) / dur;
                float bassEnv = Mathf.Exp(-inBeat * 4.5f);
                double bass = (Math.Sin(2.0 * Math.PI * bf * tSec) * 0.75 + Math.Sin(4.0 * Math.PI * bf * tSec) * 0.25) * bassEnv * 0.30;

                double mf = Math.Round(melodyNotes[beatIdx] * dur) / dur;
                float melEnv = Mathf.Clamp01(inBeat / 0.008f) * Mathf.Exp(-inBeat * 7.2f);
                double melody = (Math.Sin(2.0 * Math.PI * mf * tSec) * 0.8 + Math.Sin(4.0 * Math.PI * mf * tSec) * 0.2) * melEnv * 0.19;

                float subBeat = (tSec % beatDur) / beatDur;
                double kick = (beatIdx % 2 == 0)
                    ? Math.Sin(2.0 * Math.PI * (52.0 + 90.0 * Math.Exp(-inBeat * 32f)) * inBeat) * Math.Exp(-inBeat * 18f) * 0.22
                    : 0.0;
                double shaker = Math.Sin(2.0 * Math.PI * 5200.0 * tSec + Math.Sin(tSec * 97.0)) * Math.Exp(-Math.Abs(subBeat - 0.5f) * 28f) * 0.028;

                double mix = chordSum * barEnv + bass + melody + kick + shaker;
                data[i] = Mathf.Clamp((float)(mix * 0.62), -0.92f, 0.92f);
            }

            var clip = AudioClip.Create(clipName, len, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Envelope(int i, int total, int fadeSamples)
        {
            float fadeIn = i < fadeSamples ? (float)i / fadeSamples : 1f;
            int rem = total - 1 - i;
            float fadeOut = rem < fadeSamples ? (float)rem / fadeSamples : 1f;
            return Mathf.Min(fadeIn, fadeOut);
        }
    }
}
