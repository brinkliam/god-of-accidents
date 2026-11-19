using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GOA.Game
{
    /// <summary>Builds the entire game at runtime from a single object in an otherwise empty scene.</summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public static Bootstrap Instance { get; private set; }
        public GameSession Session { get; private set; }
        public CameraRig Rig { get; private set; }
        public WorldView View { get; private set; }
        public UIRoot UI { get; private set; }
        public ServicesHub Services { get; private set; }
        Volume _volume;
        Transform _weatherAnchor;
        DepthOfField _dof;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;
            QualitySettings.vSyncCount = Application.isMobilePlatform ? 0 : 1;
            Benchmark.MaybeStart(gameObject);
        }

        void Start()
        {
            Services = new ServicesHub(GameSettings.AnalyticsConsent);

            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();

            var rigGo = new GameObject("CameraRig");
            Rig = rigGo.AddComponent<CameraRig>();
            Rig.Init();
            var camData = Rig.Cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.requiresDepthTexture = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.Medium;
            BuildPostFx();

            var worldGo = new GameObject("World");
            View = worldGo.AddComponent<WorldView>();
            var atmo = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            _weatherAnchor = new GameObject("WeatherAnchor").transform;
            atmo.Init(_weatherAnchor);
            View.Atmosphere = atmo;

            var sessionGo = new GameObject("Session");
            Session = sessionGo.AddComponent<GameSession>();
            Session.View = View;
            Session.Rig = Rig;
            Session.Services = Services;

            var bootWorld = new GOA.Sim.World(2026, GOA.Sim.ScenarioDef.Sandbox);
            View.Init(bootWorld, GameSettings.QualityScale);

            var audio = new GameObject("Audio").AddComponent<AudioDirector>();
            audio.Session = Session; audio.Rig = Rig;
            audio.Init();
            atmo.OnThunder += p => audio.At("thunder", p == Vector3.zero ? Rig.Target + Vector3.up * 40 : p, p == Vector3.zero ? 0.5f : 1f);

            var pcGo = new GameObject("Powers");
            var pc = pcGo.AddComponent<PowerController>();
            pc.Session = Session; pc.Rig = Rig; pc.View = View;
            pc.Init();

            var uiGo = new GameObject("UI");
            UI = uiGo.AddComponent<UIRoot>();
            var ps = Resources.Load<PanelSettings>("UI/GOA_PanelSettings");
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/GOA_Theme");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1280, 720);
                ps.match = 0.5f;
            }
            Session.StartMenu();
            UI.Init(Session, pc, Rig, Services, audio, ps);
            Rig.IsOverUI = UI.IsOverUI;
            Rig.GestureCaptured = () => pc.Captured;
            pc.IsOverUI = UI.IsOverUI;
            ApplyQuality();
        }

        void BuildPostFx()
        {
            var go = new GameObject("PostFX");
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f); bloom.intensity.Override(0.4f); bloom.scatter.Override(0.6f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(-0.1f); ca.saturation.Override(22f); ca.contrast.Override(16f);
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(6f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.24f); vig.smoothness.Override(0.5f);
            _dof = profile.Add<DepthOfField>(true);
            _dof.mode.Override(DepthOfFieldMode.Bokeh);
            _dof.focusDistance.Override(90f); _dof.focalLength.Override(160f); _dof.aperture.Override(5.6f);
            _volume.sharedProfile = profile;
        }

        void LateUpdate()
        {
            if (Rig == null) return;
            if (_weatherAnchor) _weatherAnchor.position = Rig.Target;
            if (_dof == null) return;
            // a tilt-shift miniature effect: focus on the camera's target, more pronounced when zoomed out
            float d = Vector3.Distance(Rig.Cam.transform.position, Rig.Target);
            _dof.focusDistance.value = d;
            _dof.focalLength.value = Mathf.Lerp(70, 230, Mathf.InverseLerp(15, 300, d));
            _dof.aperture.value = Mathf.Lerp(8f, 4.5f, Mathf.InverseLerp(15, 300, d));
        }

        public static void ApplyQuality()
        {
            int q = GameSettings.Quality;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = q == 0 ? 120 : q == 1 ? 180 : 260;
                urp.shadowCascadeCount = q == 0 ? 1 : q == 1 ? 2 : 4;
                urp.renderScale = q == 0 ? 0.8f : 1f;
                urp.msaaSampleCount = q == 2 ? 4 : 1;
                urp.supportsCameraDepthTexture = true;
            }
            var b = Instance;
            if (b == null) return;
            if (b._dof != null) b._dof.active = q == 2;
            if (b.View != null) b.View.SetQuality(GameSettings.QualityScale);
        }

        void OnApplicationQuit() => Services?.Shutdown();
    }
}
