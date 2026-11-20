using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>
    /// Scripted visual check for player builds. Launch with <c>-goaShowcase</c>: it plays the round-2 moments by itself
    /// (the Forgotten Genius guided moment, distracting Ambrose near Wren, the lantern fire at night, a midday overview),
    /// saves a screenshot of each to the persistent data path with a log of what happened, and quits.
    /// Does nothing without the flag.
    /// </summary>
    public class Showcase : MonoBehaviour
    {
        public static void MaybeStart(GameObject host)
        {
            if (System.Environment.GetCommandLineArgs().Contains("-goaShowcase")) host.AddComponent<Showcase>();
        }

        readonly List<string> _log = new List<string>();
        Bootstrap B => Bootstrap.Instance;

        void Note(string s) { _log.Add($"{Time.realtimeSinceStartup,7:0.0}s  {s}"); Debug.Log("[GOA showcase] " + s); }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath, "showcase_" + name + ".png"));
            Note("screenshot " + name + " at " + SimTime.Format(B.Session.World.Time));
            yield return null;
        }

        IEnumerator Start()
        {
            PlayerPrefs.DeleteKey("goa_codex"); // fresh codex so the reveal shows
            yield return new WaitForSeconds(2);
            var s = B.Session;
            int codex = 0, drama = 0, guide = -1;
            s.OnCodex += e => { codex++; Note("codex: " + e.title); };
            s.OnDrama += e => { drama++; Note("drama: " + e.title); };
            s.OnGuide += id => { guide = id; Note("guide on " + s.World.GetCitizen(id).Name); };
            s.OnEvent += e => { if (e.roots.Length > 0 && e.magnitude >= 2) Note("chain: " + e.title); };

            // 1. Forgotten Genius: the guided moment
            Note(DevTools.Start("forgotten_genius"));
            yield return null;
            Note(DevTools.Skip(8.0));
            DevTools.Speed(2);
            float until = Time.realtimeSinceStartup + 240; // up to two afternoons at Brisk speed
            while (guide < 0 && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(1.8f);
            yield return Shot("1_guide");

            // 2. act on it: Distract the tinkerer while Wren is near
            if (guide >= 0)
            {
                var r = s.ActNow(GOA.Sim.PlayerInput.DistractCitizen(guide));
                Note("distract: " + (r.ok ? r.message : "refused " + r.message));
                DevTools.Speed(1);
                yield return new WaitForSeconds(1.0f);
                yield return Shot("2_after_distract");
                until = Time.realtimeSinceStartup + 40;
                while (drama == 0 && codex < 2 && Time.realtimeSinceStartup < until) yield return null;
                yield return new WaitForSeconds(0.9f);
                yield return Shot("3_moment");
                yield return new WaitForSeconds(1.6f);
                yield return Shot("4_moment_later");
            }
            else Note("guide never fired");

            // 3. the lantern fire at night in a sandbox
            Note(DevTools.Start("sandbox", 2026));
            yield return null;
            Note(DevTools.Skip(14.5));
            DevTools.Speed(1);
            yield return new WaitForSeconds(1.5f);
            Note(DevTools.NudgeLantern());
            yield return new WaitForSeconds(4f);
            yield return Shot("5_lantern");
            until = Time.realtimeSinceStartup + 30;
            int before = drama;
            while (drama == before && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(1.2f);
            yield return Shot("6_fire");

            // 4. midday overview
            Note(DevTools.Start("sandbox", 2026));
            yield return null;
            Note(DevTools.Skip(6.2));
            DevTools.Speed(0);
            DevTools.Cam(0, -8, 90, 30, 42);
            yield return new WaitForSeconds(3f);
            yield return Shot("7_noon");

            Note($"done: codex reveals {codex}, dramatic moments {drama}");
            File.WriteAllLines(Path.Combine(Application.persistentDataPath, "goa_showcase.txt"), _log);
            yield return new WaitForSeconds(1);
            Application.Quit();
        }
    }
}
