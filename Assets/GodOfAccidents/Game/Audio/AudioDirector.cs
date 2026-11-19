using System.Collections;
using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>
    /// Layered soundscape: wind, river, rain, fire and night crickets that respond to where the camera is and what
    /// the world is doing; birdsong by day; event sounds placed in 3D; and adaptive music that shifts between
    /// peaceful, nocturnal, tense and triumphant moods.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        AudioSource _wind, _river, _rain, _fire, _crickets, _ui;
        AudioSource _musicA, _musicB;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        int _poolIdx;
        AudioClip[] _music = new AudioClip[4];
        int _mood = -1;
        float _moodHold;
        float _nextBird = 3;
        bool _ready;
        public GameSession Session;
        public CameraRig Rig;

        public void Init()
        {
            _ui = Src("UI", false, 0);
            _wind = Src("Wind", true, 0); _river = Src("River", true, 0); _rain = Src("Rain", true, 0);
            _fire = Src("Fire", true, 0); _crickets = Src("Crickets", true, 0);
            _musicA = Src("MusicA", true, 0); _musicB = Src("MusicB", true, 0);
            for (int i = 0; i < 8; i++) { var s = Src("Sfx" + i, false, 1); s.minDistance = 12; s.maxDistance = 260; s.rolloffMode = AudioRolloffMode.Linear; _pool.Add(s); }
            StartCoroutine(Generate());
        }

        AudioSource Src(string name, bool loop, float spatial)
        {
            var go = new GameObject("Audio_" + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.loop = loop; s.playOnAwake = false; s.spatialBlend = spatial; s.volume = 0;
            return s;
        }

        IEnumerator Generate()
        {
            // UI sounds first so the menu responds immediately
            _clips["click"] = Synth.Click(); _clips["hover"] = Synth.Hover(); _clips["select"] = Synth.Select(); _clips["error"] = Synth.Error();
            yield return null;
            _clips["begin"] = Synth.Arpeggio("begin", new[] { 293.66f, 369.99f, 440f, 587.33f, 739.99f }, 0.09f, 0.9f);
            _clips["codex"] = Synth.Arpeggio("codex", new[] { 880f, 1108.73f, 1318.51f, 1760f }, 0.07f, 0.8f, 0.4f);
            _clips["tp_yours"] = Synth.Arpeggio("tp_yours", new[] { 293.66f, 440f, 587.33f, 739.99f, 880f, 1174.66f }, 0.12f, 1.8f);
            _clips["bell"] = Synth.Bell("bell", 392, 4.5f);
            yield return null;
            _clips["nudge"] = Synth.Tok(); _clips["gust"] = Synth.Whoosh(); _clips["distract"] = Synth.Sparkle(); _clips["drop"] = Synth.Caw();
            _clips["thunder"] = Synth.Thunder(); _clips["crash"] = Synth.Crash();
            yield return null;
            Loop(_wind, Synth.Wind()); yield return null;
            Loop(_river, Synth.River()); yield return null;
            Loop(_rain, Synth.Rain()); yield return null;
            Loop(_fire, Synth.Fire()); yield return null;
            Loop(_crickets, Synth.Crickets()); yield return null;
            for (int i = 0; i < 4; i++) { _clips["bird" + i] = Synth.Bird(i); }
            yield return null;
            _ready = true;
            for (int m = 0; m < 4; m++) { _music[m] = Synth.Music("music" + m, m, (uint)(m * 97 + 5)); yield return null; }
        }

        static void Loop(AudioSource s, AudioClip c) { s.clip = c; s.Play(); }

        public void ApplyVolumes() { }

        float Sfx => GameSettings.SfxVolume;
        float Amb => GameSettings.AmbienceVolume;

        public void Ui(string name)
        {
            if (_clips.TryGetValue(name, out var c)) _ui.PlayOneShot(c, Sfx * (name == "hover" ? 0.5f : 0.8f));
        }

        public void Power(PowerType p)
        {
            string n = p switch { PowerType.Nudge => "nudge", PowerType.Gust => "gust", PowerType.Distract => "distract", _ => "drop" };
            if (_clips.TryGetValue(n, out var c)) _ui.PlayOneShot(c, Sfx);
        }

        public void TurningPoint(bool yours)
        {
            if (_clips.TryGetValue(yours ? "tp_yours" : "bell", out var c)) _ui.PlayOneShot(c, Sfx * 0.85f);
            _moodHold = 0;
        }

        public void At(string name, Vector3 pos, float vol = 1)
        {
            if (!_clips.TryGetValue(name, out var c)) return;
            var s = _pool[_poolIdx++ % _pool.Count];
            s.transform.position = pos;
            s.PlayOneShot(c, vol * Sfx);
        }

        public void SimEvent(SimEvent e, Vector3? pos)
        {
            if (!_ready || !pos.HasValue) return;
            switch (e.type)
            {
                case SimEventType.LightningStrike: At("thunder", pos.Value, 1); break;
                case SimEventType.WellBroken:
                case SimEventType.BuildingDestroyed:
                case SimEventType.CartRoll: At("crash", pos.Value, 0.9f); break;
                case SimEventType.Marriage: At("bell", pos.Value, 0.8f); break;
                case SimEventType.Trip: At("nudge", pos.Value, 0.5f); break;
                case SimEventType.ItemDropped: if (e.title.Contains("crow")) At("drop", pos.Value, 0.7f); break;
            }
        }

        void Update()
        {
            if (!_ready || Session == null) return;
            var w = Session.World;
            float dt = Time.unscaledDeltaTime;
            if (w == null) return;
            var cam = Rig.Cam.transform.position;
            float zoom = Mathf.InverseLerp(340, 10, Rig.Distance); // 1 = close
            float night = Session.View.Atmosphere ? Session.View.Atmosphere.Night : 0;
            var target = Rig.Target;

            float riverD = VillageLayout.DistanceToRiver(new Vec2(target.x, target.z));
            float windV = Mathf.Clamp01(0.15f + w.WindStrength * 0.6f + (1 - zoom) * 0.3f + (w.Gust != null && w.Gust.end > Session.DisplayTime ? 0.5f : 0));
            Fade(_wind, windV * 0.55f * Amb, dt);
            Fade(_river, Mathf.Clamp01(1 - riverD / 70f) * (0.3f + zoom * 0.5f) * Amb, dt);
            bool raining = w.Weather == Weather.Rain || w.Weather == Weather.Storm;
            Fade(_rain, raining ? (w.Weather == Weather.Storm ? 0.75f : 0.5f) * Amb : 0, dt);
            float fireNear = 0;
            foreach (var f in w.Fires) if (f.active) fireNear = Mathf.Max(fireNear, f.intensity * Mathf.Clamp01(1 - Vector3.Distance(target, TerrainBuilder.Ground(f.pos)) / 120f));
            Fade(_fire, fireNear * 0.9f * Amb, dt);
            var season = SimTime.SeasonOf((long)Session.DisplayTime);
            Fade(_crickets, night > 0.6f && season != Season.Winter && !raining ? 0.35f * Amb * (0.4f + zoom) : 0, dt);

            if (night < 0.3f && !raining && season != Season.Winter)
            {
                _nextBird -= dt;
                if (_nextBird <= 0)
                {
                    _nextBird = Random.Range(2.5f, 7f);
                    var p = target + new Vector3(Random.Range(-25f, 25f), 6, Random.Range(-25f, 25f));
                    At("bird" + Random.Range(0, 4), p, 0.35f + zoom * 0.4f);
                }
            }

            // adaptive music
            int mood = 0;
            if (night > 0.6f) mood = 1;
            if (w.FamineEventId >= 0 || fireNear > 0.2f || w.ScarcityEventId >= 0) mood = 2;
            else if (w.Prosperity > 0.78f && night < 0.5f) mood = 3;
            _moodHold -= dt;
            if (mood != _mood && _moodHold <= 0 && _music[mood] != null)
            {
                _mood = mood; _moodHold = 20;
                var swap = _musicA; _musicA = _musicB; _musicB = swap;
                _musicA.clip = _music[mood]; _musicA.Play();
            }
            Fade(_musicA, 0.5f * GameSettings.MusicVolume, dt * 0.25f);
            Fade(_musicB, 0, dt * 0.25f);
            if (_musicB.volume <= 0.001f && _musicB.isPlaying) _musicB.Stop();
        }

        static void Fade(AudioSource s, float target, float dt)
        {
            s.volume = Mathf.MoveTowards(s.volume, target, dt * 0.6f);
        }
    }
}
