using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GOA.EditorTools
{
    /// <summary>One-click project configuration and builds. Safe to run repeatedly.</summary>
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string UiDir = "Assets/GodOfAccidents/Resources/UI";

        [MenuItem("God of Accidents/Setup Project")]
        public static void Run()
        {
            Directory.CreateDirectory(UiDir);
            string tss = UiDir + "/GOA_Theme.tss";
            if (!File.Exists(tss)) { File.WriteAllText(tss, "@import url(\"unity-theme://default\");\n"); AssetDatabase.ImportAsset(tss); }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tss);
            string psPath = UiDir + "/GOA_PanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);
            if (ps == null) { ps = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(ps, psPath); }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1280, 720);
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);

            // Forward rendering (mobile-friendly; our shaders use the classic additional-light loop) + depth texture
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                data.renderingMode = RenderingMode.Forward;
                // The template's SSAO feature loses its shader resources in player builds and then URP fails to create the
                // pipeline every frame. Our stylised look doesn't use SSAO, so remove it.
                var mapField = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var map = mapField?.GetValue(data) as System.Collections.Generic.List<long>;
                for (int i = data.rendererFeatures.Count - 1; i >= 0; i--)
                {
                    var f = data.rendererFeatures[i];
                    if (f != null && f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    data.rendererFeatures.RemoveAt(i);
                    if (map != null && i < map.Count) map.RemoveAt(i);
                    if (f != null) { AssetDatabase.RemoveObjectFromAsset(f); UnityEngine.Object.DestroyImmediate(f, true); }
                }
                data.SetDirty();
                EditorUtility.SetDirty(data);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (a == null) continue;
                a.supportsCameraDepthTexture = true;
                a.shadowDistance = 220;
                EditorUtility.SetDirty(a);
            }

            PlayerSettings.companyName = "God of Accidents";
            PlayerSettings.productName = "God of Accidents";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.godofaccidents.game");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.godofaccidents.game");
            PlayerSettings.runInBackground = true;

            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("GOA");
            go.AddComponent<GOA.Game.Bootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[GOA] Project setup complete.");
        }

        [MenuItem("God of Accidents/Build Web")]
        public static void BuildWeb()
        {
            // Switch first: URP gathers its shader-stripping feature list from the active target's quality levels, and
            // building WebGL while Windows was active stripped every post-processing variant (black screen).
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            Run();
            // Brotli for hosting (web/vercel.json sends Content-Encoding: br). The fallback lets the loader decompress in
            // JavaScript if a host or proxy ever drops that header, so the game still starts, just a little slower.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            // full-window, resizable page with a themed loader (Assets/WebGLTemplates/GodOfAccidents)
            PlayerSettings.WebGL.template = "PROJECT:GodOfAccidents";
            // URP needs more than the 96 MB default: start at 256 MB and let the heap grow
            SetWebGLProp("initialMemorySize", 256);
            SetWebGLProp("maximumMemorySize", 2048);
            SetWebGLProp("memoryGrowthMode", "Geometric");
            // Keep the Emscripten link inside this machine's memory: smaller IL2CPP output and a cheaper optimiser pass.
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.WebGL, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
            var ubs = System.Type.GetType("UnityEditor.WebGL.UserBuildSettings, UnityEditor.WebGL.Extensions");
            var prop = ubs?.GetProperty("codeOptimization", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop != null) { prop.SetValue(null, System.Enum.Parse(prop.PropertyType, "BuildTimes")); Debug.Log("[GOA] WebGL code optimization: BuildTimes"); }
            else Debug.LogWarning("[GOA] WebGL UserBuildSettings.codeOptimization not found");
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Builds/Web", target = BuildTarget.WebGL, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[GOA] Web build: " + report.summary.result + " " + report.summary.totalSize / (1024 * 1024) + " MB");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        static void SetWebGLProp(string name, object value)
        {
            var p = typeof(PlayerSettings.WebGL).GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p == null) { Debug.LogWarning("[GOA] PlayerSettings.WebGL." + name + " not found"); return; }
            var v = p.PropertyType.IsEnum ? System.Enum.Parse(p.PropertyType, value.ToString()) : System.Convert.ChangeType(value, p.PropertyType);
            p.SetValue(null, v);
            Debug.Log("[GOA] PlayerSettings.WebGL." + name + " = " + v);
        }

        [MenuItem("God of Accidents/Build Windows")]
        public static void BuildWindows()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            Run();
            var opts = new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = "Builds/Windows/GodOfAccidents.exe", target = BuildTarget.StandaloneWindows64 };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[GOA] Windows build: " + report.summary.result);
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
