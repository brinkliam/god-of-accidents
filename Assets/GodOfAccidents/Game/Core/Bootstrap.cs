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
        ColorAdjustments _ca;
        WhiteBalance _wb;
        Vignette _vig;
        Vector4 _grade = new Vector4(6, 22, 16, -0.1f); // temperature, saturation, contrast, exposure
        Color _filter = Color.white;

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
            pc.PressBeganOnUI = () => UI.PressBeganOnUI;
            Rig.IsOverUI = p => UI.IsOverUI(p) || UI.PressBeganOnUI;
            Rig.CameraFloor = View.CameraFloor;
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
            var ca = _ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(-0.1f); ca.saturation.Override(22f); ca.contrast.Override(16f);
            var wb = _wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(6f);
            var vig = _vig = profile.Add<Vignette>(true);
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
            Grade();
            if (_dof == null) return;
            // a tilt-shift miniature effect: focus on the camera's target, more pronounced when zoomed out
            float d = Vector3.Distance(Rig.Cam.transform.position, Rig.Target);
            _dof.focusDistance.value = d;
            _dof.focalLength.value = Mathf.Lerp(70, 230, Mathf.InverseLerp(15, 300, d));
            _dof.aperture.value = Mathf.Lerp(8f, 4.5f, Mathf.InverseLerp(15, 300, d));
        }

        /// <summary>Colour grading that follows the season and the hour: warm golden hours, cool blue nights, rich autumns, crisp winters.</summary>
        void Grade()
        {
            if (_ca == null || _wb == null || Session == null || View == null || View.Atmosphere == null) return;
            long t = (long)Session.DisplayTime;
            var season = GOA.Sim.SimTime.SeasonOf(t);
            float night = View.Atmosphere.Night;
            float sun = View.Atmosphere.SunElevation;               // degrees above the horizon
            float golden = (1 - night) * Mathf.Clamp01(1 - Mathf.Abs(sun - 8f) / 14f);
            // season baselines: temperature, saturation, contrast, exposure, colour filter
            Vector4 g; Color f;
            switch (season)
            {
                case GOA.Sim.Season.Spring: g = new Vector4(3, 24, 19, -0.16f); f = new Color(0.98f, 1.02f, 0.97f); break;
                case GOA.Sim.Season.Summer: g = new Vector4(9, 28, 21, -0.18f); f = new Color(1.02f, 1.0f, 0.95f); break;
                case GOA.Sim.Season.Autumn: g = new Vector4(15, 32, 22, -0.15f); f = new Color(1.05f, 0.96f, 0.86f); break;
                default: g = new Vector4(-9, 8, 17, -0.08f); f = new Color(0.93f, 0.98f, 1.06f); break;
            }
            g += new Vector4(20, 14, 4, 0.05f) * golden;             // golden hour glows
            g += new Vector4(-26, -18, 6, -0.25f) * night;           // moonlit blue
            f = Color.Lerp(f, new Color(0.82f, 0.9f, 1.12f), night * 0.6f);
            float k = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 1.5f);
            _grade = Vector4.Lerp(_grade, g, k);
            _filter = Color.Lerp(_filter, f, k);
            _wb.temperature.value = _grade.x;
            _ca.saturation.value = _grade.y;
            _ca.contrast.value = _grade.z;
            _ca.postExposure.value = _grade.w;
            _ca.colorFilter.Override(_filter);
            if (_vig != null) _vig.intensity.value = 0.22f + night * 0.12f;
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
            // anti-aliasing follows quality: none on Low, SMAA on Medium, MSAA x4 on High
            if (b.Rig != null && b.Rig.Cam != null)
            {
                var cd = b.Rig.Cam.GetUniversalAdditionalCameraData();
                cd.antialiasing = q == 1 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                cd.antialiasingQuality = AntialiasingQuality.Medium;
            }
            if (b.View != null) b.View.SetQuality(GameSettings.QualityScale);
        }

        void OnApplicationQuit() => Services?.Shutdown();
    }
}
