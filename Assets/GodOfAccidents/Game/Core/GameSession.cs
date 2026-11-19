using System;
using System.Collections.Generic;
using System.IO;
using GOA.Services;
using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    public enum SessionMode { Menu, Playing, Replay, Results }

    /// <summary>
    /// Owns the timelines and the flow of simulated time, and is the only place player inputs reach the simulation.
    /// The view and UI read from it; ads, analytics and saves hang off it.
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public static readonly float[] Speeds = { 0, 120, 600, 3600, 43200, 172800 };
        public static readonly string[] SpeedNames = { "Paused", "Living", "Brisk", "Days", "Seasons", "Ages" };

        public WorldView View;
        public CameraRig Rig;
        public ServicesHub Services;
        public SessionMode Mode { get; private set; } = SessionMode.Menu;
        public readonly List<Timeline> Timelines = new List<Timeline>();
        public int ActiveIndex { get; private set; }
        public Timeline Timeline => Timelines.Count > 0 ? Timelines[ActiveIndex] : null;
        public World World => _replayWorld ?? Timeline?.World;
        public double DisplayTime { get; private set; }
        public int SpeedIndex { get; private set; } = 1;
        public bool AcceptsInput => Mode == SessionMode.Playing && !_modal;
        public bool InReplay => Mode == SessionMode.Replay;
        public ChallengeStatus Challenge { get; private set; } = new ChallengeStatus();
        public int BranchAllowance = 2;
        public bool GlimpseArmed;
        public int InsightTokens;
        public bool BonusUnlocked;
        public SimEvent ReplayEvent { get; private set; }

        public event Action<SimEvent> OnEvent;
        public event Action<SimEvent> OnTurningPoint;
        public event Action<string> OnToast;
        public event Action<Codex.Entry> OnCodex;
        public event Action OnWorldChanged;
        public event Action<ChallengeStatus> OnChallengeResolved;
        public event Action<Counterfactual.Glimpse, PlayerInput, Vector3> OnGlimpse;
        public event Action<PowerResult, PlayerInput> OnActed;

        World _menuWorld;
        World _replayWorld;
        readonly List<PlayerInput> _replayPending = new List<PlayerInput>();
        double _replayEnd;
        float _lastCinematic = -100;
        float _challengeCheck;
        float _autosave;
        bool _modal;
        int _speedBeforeModal = 1;
        World _subscribed;

        string SavePath => Path.Combine(Application.persistentDataPath, "goa_save.json");
        public bool HasSave => File.Exists(SavePath);

        // ------------------------------------------------------------------ lifecycle

        public void StartMenu()
        {
            Mode = SessionMode.Menu;
            _menuWorld = new World(2026, ScenarioDef.Sandbox);
            _menuWorld.AdvanceTo(World.StartTime + 3 * SimTime.Hour);
            Timelines.Clear();
            _replayWorld = null;
            DisplayTime = _menuWorld.Time;
            BindView(_menuWorld);
            Rig.AutoOrbit = true;
            Rig.FocusOn(new Vector3(6, 0, 0), 120, 0.01f, 210, 40);
        }

        World MenuOrActive => Mode == SessionMode.Menu ? _menuWorld : World;

        public void NewGame(ScenarioDef scenario, ulong seed)
        {
            var s = scenario.fixedSeed != 0 ? scenario.fixedSeed : seed;
            Timelines.Clear();
            Timelines.Add(new Timeline(s, scenario) { Name = "Prime Timeline" });
            ActiveIndex = 0;
            _replayWorld = null;
            Mode = SessionMode.Playing;
            Challenge = Challenges.Evaluate(Timeline.World);
            DisplayTime = Timeline.World.Time;
            SpeedIndex = 1;
            BranchAllowance = 2;
            BindView(Timeline.World);
            Rig.AutoOrbit = false;
            Rig.FocusOn(new Vector3(10, 0, 0), 85, 2.2f, 200, 46);
            Services.Analytics.Track("world_start", ("scenario", scenario.id), ("seed", s.ToString()));
            Services.Ads.NotifyGameplay();
            OnWorldChanged?.Invoke();
        }

        void BindView(World w)
        {
            if (_subscribed != null) _subscribed.OnEvent -= HandleEvent;
            _subscribed = w;
            w.OnEvent += HandleEvent;
            View.Bind(w);
        }

        public void ReturnToMenu()
        {
            if (Mode == SessionMode.Playing || Mode == SessionMode.Results) Save();
            Services.Ads.TryShowInterstitial(Placements.ReturnToWorldSelect);
            StartMenu();
            OnWorldChanged?.Invoke();
        }

        // ------------------------------------------------------------------ time

        public void SetSpeed(int idx)
        {
            SpeedIndex = Mathf.Clamp(idx, 0, Speeds.Length - 1);
            Services.Ads.NotifyGameplay();
        }

        public void TogglePause() => SetSpeed(SpeedIndex == 0 ? Math.Max(1, _speedBeforeModal) : 0);

        public void SetModal(bool on)
        {
            if (on == _modal) return;
            _modal = on;
            if (on) { _speedBeforeModal = SpeedIndex; SpeedIndex = 0; }
            else SpeedIndex = Math.Max(_speedBeforeModal, Mode == SessionMode.Menu ? 1 : _speedBeforeModal);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var w = MenuOrActive;
            if (w == null) return;

            float speed = Mode == SessionMode.Menu ? 240 : Speeds[SpeedIndex];
            if (Mode == SessionMode.Replay) speed = SpeedIndex == 0 ? 0 : 120 * Mathf.Max(1, SpeedIndex);
            DisplayTime += dt * speed;

            if (Mode == SessionMode.Replay) StepReplay();
            else w.AdvanceTo((long)DisplayTime);

            View.Tick(DisplayTime, dt, speed, Rig.Cam);
            View.Atmosphere.Tick(w, DisplayTime, dt, GameSettings.Quality == 0);

            if (Mode == SessionMode.Playing)
            {
                _challengeCheck -= dt;
                if (_challengeCheck <= 0 && Timeline.Scenario.isChallenge)
                {
                    _challengeCheck = 0.4f;
                    Challenge = Challenges.Evaluate(Timeline.World);
                    if (Challenge.state == ChallengeState.Won || Challenge.state == ChallengeState.Failed) ResolveChallenge();
                }
                _autosave -= dt;
                if (_autosave <= 0) { _autosave = 60; Save(); }
            }
        }

        void ResolveChallenge()
        {
            Mode = SessionMode.Results;
            SpeedIndex = 0;
            Services.Analytics.Track(Challenge.state == ChallengeState.Won ? "challenge_won" : "challenge_failed",
                ("scenario", Timeline.Scenario.id), ("interventions", Timeline.World.InterventionCount.ToString()), ("year", SimTime.Year1(Timeline.World.Time).ToString()));
            if (Challenge.state == ChallengeState.Won) PlayerPrefs.SetInt("goa_stars_" + Timeline.Scenario.id, Mathf.Max(PlayerPrefs.GetInt("goa_stars_" + Timeline.Scenario.id, 0), Challenge.stars));
            Save();
            OnChallengeResolved?.Invoke(Challenge);
        }

        /// <summary>After the results card: keep playing in this world as a sandbox.</summary>
        public void ContinueAfterResults()
        {
            Mode = SessionMode.Playing;
            SpeedIndex = 1;
            Services.Ads.TryShowInterstitial(Placements.AfterChallengeResults);
        }

        public void SecondChance()
        {
            var rewound = Timeline.RewindLastIntervention();
            Timelines[ActiveIndex] = rewound;
            DisplayTime = rewound.World.Time;
            Mode = SessionMode.Playing;
            SpeedIndex = 0;
            BindView(rewound.World);
            Challenge = Challenges.Evaluate(rewound.World);
            Toast("Time folds back to the moment before your last intervention.");
            OnWorldChanged?.Invoke();
        }

        public void Retry()
        {
            var sc = Timeline.Scenario; var seed = Timeline.Seed;
            NewGame(sc, seed);
        }

        // ------------------------------------------------------------------ powers

        public PowerResult Act(PlayerInput input, Vector3 at)
        {
            if (!AcceptsInput) return PowerResult.Fail("Not now.");
            if (GlimpseArmed && input.kind == InputKind.Power)
            {
                var pre = Timeline.World.Validate(input);
                if (!pre.ok) { Toast(pre.message); return pre; }
                GlimpseArmed = false;
                var g = Counterfactual.Foresee(Timeline, input, 2 * SimTime.Year);
                SetModal(true);
                OnGlimpse?.Invoke(g, input, at);
                return new PowerResult { ok = true, message = "glimpse" };
            }
            return ActNow(input);
        }

        public PowerResult ActNow(PlayerInput input)
        {
            var r = Timeline.Act(input);
            if (!r.ok) Toast(r.message);
            else
            {
                Services.Analytics.Track("power_used", ("power", input.power.ToString()), ("year", SimTime.Year1(Timeline.World.Time).ToString()));
                Services.Ads.NotifyGameplay();
            }
            OnActed?.Invoke(r, input);
            return r;
        }

        public void GrantEnergy(float amount)
        {
            if (Timeline == null) return;
            Timeline.Act(PlayerInput.Grant(amount));
            Toast($"+{amount:0} divine energy");
        }

        // ------------------------------------------------------------------ events

        void HandleEvent(SimEvent e)
        {
            OnEvent?.Invoke(e);
            if (View) View.OnSimEvent(e);
            if (Mode != SessionMode.Playing && Mode != SessionMode.Replay) return;
            var cx = Codex.Consider(e);
            if (cx.HasValue) { OnCodex?.Invoke(cx.Value); Services.Analytics.Track("codex_unlock", ("entry", cx.Value.id)); }
            bool chained = e.roots.Length > 0;
            bool big = e.magnitude >= 4 || (e.turningPoint && chained);
            if (big && Mode == SessionMode.Playing)
            {
                OnTurningPoint?.Invoke(e);
                if (GameSettings.Cinematics && Time.unscaledTime - _lastCinematic > 6 && e.hasPos && !Rig.UserMovedRecently)
                {
                    _lastCinematic = Time.unscaledTime;
                    if (SpeedIndex >= 3) SpeedIndex = 2;
                    Rig.FocusOn(TerrainBuilder.Ground(e.pos), 34, GameSettings.ReduceMotion ? 0.01f : 1.8f);
                }
            }
        }

        public void Toast(string msg) => OnToast?.Invoke(msg);

        /// <summary>True while ads must not interrupt: a chain reaction or cinematic is unfolding, a replay, or a power gesture.</summary>
        public bool IsCriticalMoment(PowerController pc)
        {
            if (Mode == SessionMode.Replay) return true;
            if (pc != null && pc.Captured) return true;
            if (Time.unscaledTime - _lastCinematic < 8) return true;
            var w = World;
            if (w == null) return false;
            foreach (var f in w.Fires) if (f.active) return true;
            if (w.Cart != null && !w.Cart.done) return true;
            foreach (var b in w.Birds) if (!b.landed) return true;
            int recent = 0;
            for (int i = w.Ledger.Count - 1; i >= 0 && i > w.Ledger.Count - 30; i--)
            {
                var e = w.Ledger.Events[i];
                if (w.Time - e.time > 2 * SimTime.Hour) break;
                if (e.roots.Length > 0) recent++;
            }
            return recent > 0;
        }

        // ------------------------------------------------------------------ timelines, replay

        public bool CanBranch => Timelines.Count < BranchAllowance + 1;

        public void Branch(long at)
        {
            if (!CanBranch) { Toast("No more alternate timelines available. A Timeline Experiment can open another."); return; }
            var nb = Timeline.Branch(at, NumeralName(Timelines.Count + 1));
            Timelines.Add(nb);
            SwitchTimeline(Timelines.Count - 1);
            SpeedIndex = 0;
            Services.Analytics.Track("timeline_branch");
            Toast($"{nb.Name} begins at {SimTime.Format(at)}. Change what happens next.");
        }

        static string NumeralName(int n)
        {
            string[] r = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return "Timeline " + (n < r.Length ? r[n] : n.ToString());
        }

        public void SwitchTimeline(int idx)
        {
            if (idx < 0 || idx >= Timelines.Count) return;
            ExitReplay(false);
            ActiveIndex = idx;
            DisplayTime = Timeline.World.Time;
            BindView(Timeline.World);
            Challenge = Challenges.Evaluate(Timeline.World);
            OnWorldChanged?.Invoke();
        }

        public void EnterReplay(SimEvent e)
        {
            if (Timeline == null || e == null) return;
            long start = Math.Max(World.StartTime, e.time - 25 * SimTime.Minute);
            var kept = new List<PlayerInput>();
            _replayPending.Clear();
            foreach (var i in Timeline.Inputs) { if (i.time < start) kept.Add(i); else _replayPending.Add(i); }
            _replayWorld = Timeline.Replay(Timeline.Seed, Timeline.Scenario, kept, start);
            _replayEnd = Math.Min(Timeline.World.Time, e.time + 3 * SimTime.Hour);
            ReplayEvent = e;
            Mode = SessionMode.Replay;
            DisplayTime = start;
            SpeedIndex = 1;
            BindView(_replayWorld);
            if (e.hasPos) Rig.FocusOn(TerrainBuilder.Ground(e.pos), 26, 1.2f);
            Services.Analytics.Track("replay_start", ("event", e.type.ToString()));
            OnWorldChanged?.Invoke();
        }

        void StepReplay()
        {
            long target = (long)Math.Min(DisplayTime, _replayEnd);
            while (_replayPending.Count > 0 && _replayPending[0].time <= target)
            {
                var i = _replayPending[0];
                _replayPending.RemoveAt(0);
                _replayWorld.AdvanceTo(i.time);
                _replayWorld.Apply(i);
            }
            _replayWorld.AdvanceTo(target);
            if (DisplayTime >= _replayEnd) { DisplayTime = _replayEnd; SpeedIndex = 0; }
        }

        public void ExitReplay(bool notify = true)
        {
            if (Mode != SessionMode.Replay) return;
            _replayWorld = null;
            ReplayEvent = null;
            Mode = SessionMode.Playing;
            DisplayTime = Timeline.World.Time;
            SpeedIndex = 0;
            BindView(Timeline.World);
            if (notify) OnWorldChanged?.Invoke();
        }

        // ------------------------------------------------------------------ save / load

        public void Save()
        {
            if (Timelines.Count == 0) return;
            try
            {
                var f = new SaveFile { active = ActiveIndex, savedAtUtc = DateTime.UtcNow.ToString("o") };
                foreach (var t in Timelines) f.timelines.Add(t.ToSave());
                File.WriteAllText(SavePath, JsonUtility.ToJson(f));
                Services.Analytics.Flush();
            }
            catch (Exception ex) { Debug.LogWarning("[GOA] save failed: " + ex.Message); Toast("Could not save: " + ex.Message); }
        }

        public bool Load()
        {
            try
            {
                if (!HasSave) return false;
                var f = JsonUtility.FromJson<SaveFile>(File.ReadAllText(SavePath));
                if (f == null || f.timelines == null || f.timelines.Count == 0) return false;
                Timelines.Clear();
                bool allValid = true;
                foreach (var ts in f.timelines)
                {
                    var t = Timeline.FromSave(ts);
                    if (!t.VerifyHash(ts)) allValid = false;
                    Timelines.Add(t);
                }
                ActiveIndex = Mathf.Clamp(f.active, 0, Timelines.Count - 1);
                BranchAllowance = Math.Max(2, Timelines.Count - 1);
                Mode = SessionMode.Playing;
                _replayWorld = null;
                DisplayTime = Timeline.World.Time;
                SpeedIndex = 0;
                BindView(Timeline.World);
                Challenge = Challenges.Evaluate(Timeline.World);
                Rig.AutoOrbit = false;
                Rig.FocusOn(new Vector3(10, 0, 0), 85, 1.5f);
                if (!allValid) Toast("This save was made by a different version; history was re-simulated and may differ slightly.");
                Services.Analytics.Track("world_load");
                OnWorldChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GOA] load failed: " + ex);
                Toast("The saved history could not be read.");
                return false;
            }
        }

        void OnApplicationPause(bool paused) { if (paused && Mode == SessionMode.Playing) Save(); }
        void OnApplicationQuit() { if (Mode == SessionMode.Playing) Save(); Services?.Shutdown(); }
    }
}
