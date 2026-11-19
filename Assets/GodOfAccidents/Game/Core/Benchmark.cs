using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>
    /// Player-build performance probe. Launch with <c>-goaBenchmark [-goaBenchSeconds N]</c>: it starts the Forgotten Genius,
    /// flies a few camera shots at normal and fast speed, records every frame time, writes
    /// <c>goa_benchmark.txt</c> to the persistent data path, and quits. It does nothing without the flag.
    /// </summary>
    public class Benchmark : MonoBehaviour
    {
        public static void MaybeStart(GameObject host)
        {
            var args = System.Environment.GetCommandLineArgs();
            if (!args.Contains("-goaBenchmark")) return;
            host.AddComponent<Benchmark>();
        }

        IEnumerator Start()
        {
            float total = 40;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-goaBenchSeconds");
            if (i >= 0 && i + 1 < args.Length) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out total);

            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; // measure headroom, not the monitor
            yield return null; yield return null;
            DevTools.Start("forgotten_genius");
            DevTools.Speed(1);
            yield return new WaitForSeconds(3); // warm-up: shader compile, first allocations

            var shots = new (string name, float x, float z, float dist, float yaw, float pitch, int speed)[]
            {
                ("village", 0, -8, 70, 30, 42, 1),
                ("close", 4, -14, 26, 140, 28, 1),
                ("aerial", 0, 0, 180, 0, 60, 2),
                ("fast-forward", 0, -8, 90, 300, 38, 3),
            };
            var report = new List<string>
            {
                $"God of Accidents benchmark  {System.DateTime.Now:yyyy-MM-dd HH:mm}",
                $"{SystemInfo.graphicsDeviceName} | {SystemInfo.graphicsDeviceType} | {SystemInfo.processorType} | RAM {SystemInfo.systemMemorySize} MB",
                $"Resolution {Screen.width}x{Screen.height}  quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}  vSync {QualitySettings.vSyncCount}  targetFps {Application.targetFrameRate}",
            };
            var all = new List<float>();
            foreach (var s in shots)
            {
                DevTools.Cam(s.x, s.z, s.dist, s.yaw, s.pitch);
                DevTools.Speed(s.speed);
                yield return new WaitForSeconds(0.5f);
                var ft = new List<float>();
                float end = Time.realtimeSinceStartup + total / shots.Length;
                while (Time.realtimeSinceStartup < end) { yield return null; ft.Add(Time.unscaledDeltaTime); }
                all.AddRange(ft);
                ScreenCapture.CaptureScreenshot(Path.Combine(Application.persistentDataPath, "bench_" + s.name + ".png"));
                yield return null; yield return null;
                report.Add(Line(s.name, ft));
            }
            report.Add(Line("ALL", all));
            report.Add(DevTools.State());
            string path = Path.Combine(Application.persistentDataPath, "goa_benchmark.txt");
            File.WriteAllLines(path, report);
            Debug.Log("[GOA] benchmark written to " + path + "\n" + string.Join("\n", report));
            Application.Quit();
        }

        static string Line(string name, List<float> ft)
        {
            if (ft.Count == 0) return name + ": no frames";
            var sorted = ft.OrderBy(x => x).ToList();
            float avg = ft.Average();
            float p99 = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            float p50 = sorted[sorted.Count / 2];
            return $"{name,-13} frames {ft.Count,5}  avg {1f / avg,6:0.0} fps  median {p50 * 1000,6:0.0} ms  1% worst {p99 * 1000,6:0.0} ms ({1f / p99:0} fps)";
        }
    }
}
