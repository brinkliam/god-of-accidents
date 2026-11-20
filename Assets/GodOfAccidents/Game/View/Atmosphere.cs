using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>Sun, moon, sky, fog, seasons, snow, weather particles, drifting clouds and lightning.</summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        public Light Sun;
        public float Night { get; private set; }
        public float SunElevation { get; private set; }
        public Weather CurrentWeather { get; private set; }
        /// <summary>What the sky is showing right now (it eases between weathers), for the HUD.</summary>
        public string WeatherLabel { get; private set; } = "";
        ParticleSystem _rain, _snow, _fireflies, _butterflies, _windStreaks, _leaves;
        Transform _weatherAnchor;
        readonly List<Transform> _clouds = new List<Transform>();
        readonly List<Vector3> _cloudBase = new List<Vector3>();
        Material _cloudMat;
        float _flash, _nextFlash = 5;
        float _weatherBlend; // 0 clear .. 1 storm darkness
        float _snowCover;
        Vector4 _windSmoothed = new Vector4(1, 0, 0, 0.2f);
        LineRenderer _bolt;
        float _boltT;
        public System.Action<Vector3> OnThunder;

        static readonly int IdZenith = Shader.PropertyToID("_GOA_SkyZenith"), IdHorizon = Shader.PropertyToID("_GOA_SkyHorizon"), IdGround = Shader.PropertyToID("_GOA_SkyGround"),
            IdSunCol = Shader.PropertyToID("_GOA_SunColor"), IdSunDir = Shader.PropertyToID("_GOA_SunDir"), IdMoonDir = Shader.PropertyToID("_GOA_MoonDir"),
            IdNight = Shader.PropertyToID("_GOA_Night"), IdStorm = Shader.PropertyToID("_GOA_Storm"), IdAmbSky = Shader.PropertyToID("_GOA_AmbientSky"),
            IdAmbGround = Shader.PropertyToID("_GOA_AmbientGround"), IdRim = Shader.PropertyToID("_GOA_RimColor"), IdSeason = Shader.PropertyToID("_GOA_SeasonTint"),
            IdSnow = Shader.PropertyToID("_GOA_Snow"), IdWind = Shader.PropertyToID("_GOA_WindDir");

        public void Init(Transform weatherAnchor)
        {
            _weatherAnchor = weatherAnchor;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            Sun = sunGo.AddComponent<Light>();
            Sun.type = LightType.Directional;
            Sun.shadows = LightShadows.Soft;
            Sun.shadowStrength = 0.85f;
            Sun.shadowBias = 0.04f;
            Sun.shadowNormalBias = 0.35f;
            RenderSettings.sun = Sun;
            RenderSettings.skybox = Mats.Sky();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            var wa = weatherAnchor;
            var rc = Fx.RainCfg; _rain = Fx.Make(wa, "Rain", rc, new Vector3(0, 40, 0)); Box(_rain, new Vector3(140, 1, 140)); Fx.SetRate(_rain, 0);
            var sc = Fx.SnowCfg; _snow = Fx.Make(wa, "Snow", sc, new Vector3(0, 35, 0)); Box(_snow, new Vector3(140, 1, 140)); Fx.SetRate(_snow, 0);
            var main = _rain.main; main.startSpeed = new ParticleSystem.MinMaxCurve(28, 34);
            var sh = _rain.shape; sh.rotation = new Vector3(90, 0, 0);
            var sh2 = _snow.shape; sh2.rotation = new Vector3(90, 0, 0);
            _fireflies = Fx.Make(transform, "Fireflies", Fx.FireflyCfg, new Vector3(24, 1.5f, 9)); Box(_fireflies, new Vector3(160, 3, 160));
            _butterflies = Fx.Make(transform, "Butterflies", Fx.ButterflyCfg, new Vector3(0, 1.2f, -10)); Box(_butterflies, new Vector3(150, 2, 150));
            _windStreaks = Fx.Make(wa, "WindStreaks", Fx.WindCfg, new Vector3(0, 6, 0)); Box(_windStreaks, new Vector3(120, 10, 120)); Fx.SetRate(_windStreaks, 0);
            _leaves = Fx.Make(wa, "Leaves", Fx.LeafCfg, new Vector3(0, 5, 0)); Box(_leaves, new Vector3(110, 8, 110)); Fx.SetRate(_leaves, 0);

            BuildClouds();

            var boltGo = new GameObject("Lightning");
            boltGo.transform.SetParent(transform, false);
            _bolt = boltGo.AddComponent<LineRenderer>();
            _bolt.material = Mats.Glow("bolt", new Color(3f, 3.2f, 4f), 0, 1, 0.5f);
            _bolt.positionCount = 0;
            _bolt.widthMultiplier = 0.9f;
            _bolt.useWorldSpace = true;
            _bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void Box(ParticleSystem ps, Vector3 scale)
        {
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = scale;
        }

        void BuildClouds()
        {
            _cloudMat = new Material(Mats.StylizedShader) { name = "Clouds" };
            _cloudMat.SetFloat("_Wrap", 0.9f);
            _cloudMat.SetFloat("_Rim", 0.6f);
            _cloudMat.SetFloat("_SnowAmount", 0);
            _cloudMat.SetColor("_EmissionColor", new Color(0.12f, 0.12f, 0.14f));
            var rng = new System.Random(77);
            for (int i = 0; i < 26; i++)
            {
                var k = new MeshKit();
                int puffs = 4 + rng.Next(4);
                for (int p = 0; p < puffs; p++)
                {
                    var off = new Vector3((float)(rng.NextDouble() - 0.5) * 30, (float)rng.NextDouble() * 4, (float)(rng.NextDouble() - 0.5) * 14);
                    float s = 6 + (float)rng.NextDouble() * 7;
                    k.Blob(off, new Vector3(s, s * 0.55f, s * 0.8f), Color.white, i * 31 + p, 0.15f, 1);
                }
                var go = new GameObject("Cloud" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Cloud");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _cloudMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var basePos = new Vector3((float)(rng.NextDouble() - 0.5) * 640, 85 + (float)rng.NextDouble() * 40, (float)(rng.NextDouble() - 0.5) * 640);
                _clouds.Add(go.transform);
                _cloudBase.Add(basePos);
            }
        }

        static Color Lerp3(Color night, Color dusk, Color golden, Color noon, float e)
        {
            if (e < -6) return night;
            if (e < 0) return Color.Lerp(night, dusk, (e + 6) / 6f);
            if (e < 10) return Color.Lerp(dusk, golden, e / 10f);
            if (e < 30) return Color.Lerp(golden, noon, (e - 10) / 20f);
            return noon;
        }

        public void Tick(World w, double t, float dt, bool lowQuality)
        {
            double hour = SimTime.HourOfDay(t);
            long day = (long)(t / SimTime.Day);
            float doy = (float)((t / SimTime.Day) % SimTime.DaysPerYear);
            float seasonPos = doy / SimTime.DaysPerYear; // 0 = start of spring
            float summerness = Mathf.Cos((seasonPos - 0.375f) * Mathf.PI * 2) * 0.5f + 0.5f;
            float maxElev = Mathf.Lerp(32, 64, summerness);

            float dayFrac = (float)((hour - 6) / 14.0);          // 06:00 sunrise .. 20:00 sunset
            float elev = Mathf.Sin(dayFrac * Mathf.PI) * maxElev;
            if (dayFrac < 0 || dayFrac > 1) elev = -Mathf.Abs(Mathf.Sin(dayFrac * Mathf.PI)) * 30 - 2;
            float az = (90 + 180 * dayFrac) * Mathf.Deg2Rad; // rises in the east, crosses the south, sets in the west
            SunElevation = elev;
            float er = elev * Mathf.Deg2Rad;
            var sunDir = new Vector3(Mathf.Sin(az) * Mathf.Cos(er), Mathf.Sin(er), Mathf.Cos(az) * Mathf.Cos(er)).normalized;
            var moonDir = new Vector3(-sunDir.x, Mathf.Max(0.35f, -sunDir.y), -sunDir.z).normalized;
            Night = Mathf.Clamp01(Mathf.InverseLerp(2f, -8f, elev));

            // weather blend
            CurrentWeather = w.Weather;
            float targetStorm = w.Weather switch { Weather.Storm => 1f, Weather.Rain => 0.65f, Weather.Cloudy => 0.35f, Weather.Snow => 0.5f, _ => 0f };
            _weatherBlend = Mathf.MoveTowards(_weatherBlend, targetStorm, dt * 0.25f);
            string wl = w.Weather == Weather.Cloudy ? "Overcast" : w.Weather.ToString();
            if (_weatherBlend < targetStorm - 0.15f) wl += " rolling in";
            else if (_weatherBlend > targetStorm + 0.15f) wl = (w.Weather == Weather.Clear ? "Clearing" : wl + ", easing");
            WeatherLabel = wl;

            Color zenith = Lerp3(new Color(0.02f, 0.03f, 0.09f), new Color(0.16f, 0.18f, 0.38f), new Color(0.33f, 0.47f, 0.74f), new Color(0.3f, 0.55f, 0.88f), elev);
            Color horizon = Lerp3(new Color(0.06f, 0.08f, 0.17f), new Color(0.78f, 0.42f, 0.42f), new Color(1.0f, 0.74f, 0.52f), new Color(0.74f, 0.86f, 0.96f), elev);
            Color sunCol = Lerp3(new Color(0.35f, 0.45f, 0.75f), new Color(0.95f, 0.42f, 0.32f), new Color(1.0f, 0.7f, 0.42f), new Color(1.0f, 0.96f, 0.88f), elev);
            Color ambSky = Lerp3(new Color(0.11f, 0.14f, 0.27f), new Color(0.3f, 0.26f, 0.4f), new Color(0.44f, 0.4f, 0.46f), new Color(0.38f, 0.46f, 0.6f), elev);
            Color ambGround = Lerp3(new Color(0.04f, 0.045f, 0.07f), new Color(0.14f, 0.1f, 0.12f), new Color(0.26f, 0.2f, 0.16f), new Color(0.26f, 0.24f, 0.18f), elev);
            float sunI = elev > 0 ? Mathf.Lerp(0.6f, 1.2f, Mathf.Clamp01(elev / 25f)) : Mathf.Lerp(0.28f, 0.05f, Mathf.Clamp01(-elev / 4f) * (1 - Night)) ;

            var grey = new Color(0.52f, 0.56f, 0.6f);
            float wb = _weatherBlend;
            zenith = Color.Lerp(zenith, grey * Mathf.Lerp(1, 0.25f, Night) * 0.85f, wb * 0.8f);
            horizon = Color.Lerp(horizon, grey * Mathf.Lerp(1.1f, 0.3f, Night), wb * 0.75f);
            sunCol = Color.Lerp(sunCol, grey, wb * 0.5f);
            sunI *= 1 - wb * 0.6f;
            ambSky = Color.Lerp(ambSky, grey * Mathf.Lerp(0.95f, 0.25f, Night), wb * 0.5f);

            // lightning flashes during storms
            if (w.Weather == Weather.Storm && !lowQuality)
            {
                _nextFlash -= dt;
                if (_nextFlash <= 0) { Flash(null); _nextFlash = Random.Range(6f, 18f); }
            }
            _flash = Mathf.MoveTowards(_flash, 0, dt * 4f);
            if (_flash > 0)
            {
                zenith += new Color(0.6f, 0.65f, 0.8f) * _flash;
                horizon += new Color(0.6f, 0.65f, 0.8f) * _flash;
                ambSky += new Color(0.8f, 0.85f, 1f) * _flash;
            }
            if (_boltT > 0) { _boltT -= dt; if (_boltT <= 0) _bolt.positionCount = 0; }

            Shader.SetGlobalColor(IdZenith, zenith);
            Shader.SetGlobalColor(IdHorizon, horizon);
            Shader.SetGlobalColor(IdGround, ambGround * 0.8f);
            Shader.SetGlobalColor(IdSunCol, sunCol * (1 - wb * 0.7f));
            Shader.SetGlobalVector(IdSunDir, sunDir);
            Shader.SetGlobalVector(IdMoonDir, moonDir);
            Shader.SetGlobalFloat(IdNight, Night);
            Shader.SetGlobalFloat(IdStorm, wb);
            Shader.SetGlobalColor(IdAmbSky, ambSky);
            Shader.SetGlobalColor(IdAmbGround, ambGround);
            Shader.SetGlobalColor(IdRim, Color.Lerp(new Color(0.55f, 0.62f, 0.8f), new Color(1f, 0.7f, 0.45f), Mathf.Clamp01(1 - Mathf.Abs(elev - 6) / 14f)) * (1 - Night * 0.6f));

            // main light follows the sun by day and the moon by night
            bool moon = elev < -1;
            var lightDir = moon ? moonDir : sunDir;
            Sun.transform.rotation = Quaternion.LookRotation(-lightDir);
            Sun.color = moon ? new Color(0.55f, 0.65f, 0.95f) : sunCol;
            Sun.intensity = moon ? 0.22f * (1 - wb * 0.6f) : sunI;
            Sun.shadowStrength = moon ? 0.5f : Mathf.Lerp(0.85f, 0.35f, wb);

            RenderSettings.fogColor = Color.Lerp(horizon, ambSky, 0.3f);
            RenderSettings.fogStartDistance = Mathf.Lerp(170, 50, wb);
            RenderSettings.fogEndDistance = Mathf.Lerp(820, 330, wb);
            RenderSettings.ambientLight = ambSky;

            // seasons
            Color tint; float tintA;
            var season = SimTime.SeasonOf((long)t);
            float sp = (doy % SimTime.DaysPerSeason) / SimTime.DaysPerSeason;
            switch (season)
            {
                case Season.Spring: tint = new Color(0.48f, 0.72f, 0.3f); tintA = 0.2f; break;
                case Season.Summer: tint = new Color(0.72f, 0.66f, 0.32f); tintA = 0.18f + sp * 0.12f; break;
                case Season.Autumn: tint = new Color(0.98f, 0.52f, 0.18f); tintA = 0.35f + sp * 0.35f; break;
                default: tint = new Color(0.68f, 0.64f, 0.55f); tintA = 0.5f; break;
            }
            Shader.SetGlobalColor(IdSeason, new Color(tint.r, tint.g, tint.b, tintA));
            float snowTarget = season == Season.Winter ? (w.Weather == Weather.Snow ? 0.95f : 0.75f) : (season == Season.Spring && sp < 0.15f ? 0.3f : 0f);
            _snowCover = Mathf.MoveTowards(_snowCover, snowTarget, dt * 0.15f);
            Shader.SetGlobalFloat(IdSnow, _snowCover);

            // wind (sim wind plus any divine gust)
            var wd = new Vector2(w.WindDir.x, w.WindDir.z);
            float ws = w.WindStrength;
            if (w.Gust != null && w.Gust.end > t) { wd = new Vector2(w.Gust.dir.x, w.Gust.dir.z); ws = Mathf.Max(ws, 1.2f); }
            var target = new Vector4(wd.x, 0, wd.y, ws);
            _windSmoothed = Vector4.Lerp(_windSmoothed, target, 1 - Mathf.Exp(-dt * 1.5f));
            Shader.SetGlobalVector(IdWind, _windSmoothed);

            // weather particles
            bool raining = w.Weather == Weather.Rain || w.Weather == Weather.Storm;
            Fx.SetRate(_rain, raining ? (w.Weather == Weather.Storm ? 2600 : 1300) * (lowQuality ? 0.4f : 1) : 0);
            Fx.SetRate(_snow, w.Weather == Weather.Snow ? 700 * (lowQuality ? 0.4f : 1) : 0);
            bool warm = season == Season.Summer || season == Season.Spring;
            Fx.SetRate(_fireflies, Night > 0.5f && warm && !raining ? 22 : 0);
            Fx.SetRate(_butterflies, Night < 0.2f && warm && !raining ? 6 : 0);
            Fx.SetRate(_windStreaks, ws > 0.65f ? (ws - 0.5f) * 70 : 0);
            Fx.SetRate(_leaves, ws > 0.75f || (season == Season.Autumn && ws > 0.4f) ? 16 : 0);
            if (_windStreaks)
            {
                var m = _windStreaks.main; m.startSpeed = new ParticleSystem.MinMaxCurve(10 + ws * 10, 16 + ws * 14);
                var sh = _windStreaks.shape; sh.rotation = new Vector3(0, Mathf.Atan2(_windSmoothed.x, _windSmoothed.z) * Mathf.Rad2Deg, 0);
                var lsh = _leaves.shape; lsh.rotation = sh.rotation;
                var lm = _leaves.main; lm.startSpeed = new ParticleSystem.MinMaxCurve(5 + ws * 6, 9 + ws * 10);
            }

            // clouds drift downwind and thicken with bad weather
            float drift = (float)(t * 0.0006);
            for (int i = 0; i < _clouds.Count; i++)
            {
                var b = _cloudBase[i];
                var p = b + new Vector3(_windSmoothed.x, 0, _windSmoothed.z) * (drift * 40 % 640);
                p.x = Mathf.Repeat(p.x + 320, 640) - 320;
                p.z = Mathf.Repeat(p.z + 320, 640) - 320;
                _clouds[i].position = p;
                bool show = i < 8 + (int)(wb * 18);
                if (_clouds[i].gameObject.activeSelf != show) _clouds[i].gameObject.SetActive(show);
            }
            _cloudMat.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(0.45f, 0.47f, 0.52f), wb) * Mathf.Lerp(1f, 0.35f, Night));
        }

        /// <summary>Flash the sky; with a target, draw a bolt down to it.</summary>
        public void Flash(Vector3? target)
        {
            _flash = 1.1f;
            if (target.HasValue)
            {
                var end = target.Value;
                var start = end + new Vector3(Random.Range(-20f, 20f), 95, Random.Range(-20f, 20f));
                int n = 14;
                _bolt.positionCount = n;
                for (int i = 0; i < n; i++)
                {
                    float u = i / (float)(n - 1);
                    var p = Vector3.Lerp(start, end, u);
                    if (i > 0 && i < n - 1) p += new Vector3(Random.Range(-4f, 4f), 0, Random.Range(-4f, 4f)) * (1 - u * 0.6f);
                    _bolt.SetPosition(i, p);
                }
                _boltT = 0.35f;
                OnThunder?.Invoke(end);
            }
            else OnThunder?.Invoke(Vector3.zero);
        }
    }
}
