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
    }
}
