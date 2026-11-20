using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>Developer hooks for automated verification (screenshots, scripted play-throughs). Not shown to players.</summary>
    public static class DevTools
    {
        static Bootstrap B => Bootstrap.Instance;

        public static string CloseModals()
        {
            GameSettings.ConsentAsked = true;
            GameSettings.TutorialDone = true;
            var m = typeof(UIRoot).GetMethod("CloseAllModals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            m?.Invoke(B.UI, null);
            return "ok";
        }

        public static string Start(string scenario, ulong seed = 0)
        {
            CloseModals();
            B.Session.NewGame(ScenarioDef.Get(scenario), seed == 0 ? 1207 : seed);
            return $"started {scenario} at {SimTime.Format(B.Session.World.Time)}";
        }

        public static string Cam(float x, float z, float distance, float yaw, float pitch)
        {
            var r = B.Rig;
            r.AutoOrbit = false;
            r.Follow = null;
            r.FocusOn(new Vector3(x, 0, z), distance, 0.01f, yaw, pitch);
            return "cam";
        }

        public static string Speed(int idx) { B.Session.SetSpeed(idx); return GameSession.SpeedNames[idx]; }

        /// <summary>Jump the displayed time forward (the simulation catches up next frame).</summary>
        public static string Skip(double hours)
        {
            var s = B.Session;
            var f = typeof(GameSession).GetProperty("DisplayTime");
            f.SetValue(s, s.DisplayTime + hours * SimTime.Hour);
            s.World.AdvanceTo((long)s.DisplayTime);
            return SimTime.Format(s.World.Time);
        }

        public static string SetWeather(string w)
        {
            B.Session.World.Weather = (Weather)System.Enum.Parse(typeof(Weather), w);
            return w;
        }

        public static string Screen() => UnityEngine.Screen.width + "x" + UnityEngine.Screen.height;

        public static string State()
        {
            var w = B.Session.World;
            return $"{B.Session.Mode} {SimTime.Format(w.Time)} pop={w.Population} era={w.Era} events={w.Ledger.Count} weather={w.Weather} fps={1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}";
        }

        /// <summary>Nudge the tavern lantern (for testing the fire, the dramatic moment and the codex reveal).</summary>
        public static string NudgeLantern()
        {
            var w = B.Session.World;
            foreach (var p in w.Props)
                if (p.type == PropType.Lantern)
                {
                    Cam(p.pos.x, p.pos.z, 40, 210, 38);
                    var r = B.Session.Act(GOA.Sim.PlayerInput.Nudge(p.id, p.pos), TerrainBuilder.Ground(p.pos));
                    return r.ok ? "nudged: " + r.message : "refused: " + r.message;
                }
            return "no lantern";
        }

        /// <summary>
        /// Text commands for scripted browser play-throughs, sent from the page with
        /// unityInstance.SendMessage("GOA", "Dev", "skip 9"). Only active when the page URL contains dev=1.
        /// </summary>
        public static string Run(string cmd)
        {
            if (!Application.isEditor && (Application.absoluteURL == null || !Application.absoluteURL.Contains("dev=1"))) return "dev commands are off";
            var a = cmd.Split(' ');
            float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
            string r = a[0] switch
            {
                "start" => Start(a[1], a.Length > 2 ? ulong.Parse(a[2]) : 0),
                "skip" => Skip(F(1)),
                "speed" => Speed(int.Parse(a[1])),
                "cam" => Cam(F(1), F(2), F(3), F(4), F(5)),
                "lantern" => NudgeLantern(),
                "weather" => SetWeather(a[1]),
                _ => State(),
            };
            Debug.Log("[GOA dev] " + cmd + " -> " + r);
            return r;
        }
    }
}
