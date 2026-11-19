using UnityEngine;

namespace GOA.Game
{
    /// <summary>Particle and light effects, all built from code with the texture-free GOA/Particle shader.</summary>
    public static class Fx
    {
        public struct PSConfig
        {
            public float rate, life0, life1, size0, size1, speed0, speed1, gravity, radius, angle;
            public Color c0, c1;
            public bool additive, world, stretch;
            public float softness, shape;
            public int max;
            public ParticleSystemShapeType shapeType;
            public float noise;
        }

        public static ParticleSystem Make(Transform parent, string name, PSConfig cfg, Vector3 localPos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(cfg.life0, cfg.life1);
            main.startSize = new ParticleSystem.MinMaxCurve(cfg.size0, cfg.size1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(cfg.speed0, cfg.speed1);
            main.startColor = new ParticleSystem.MinMaxGradient(cfg.c0, cfg.c1);
            main.gravityModifier = cfg.gravity;
            main.maxParticles = cfg.max > 0 ? cfg.max : 300;
            main.simulationSpace = cfg.world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.playOnAwake = true;
            var em = ps.emission;
            em.rateOverTime = cfg.rate;
            var sh = ps.shape;
            sh.shapeType = cfg.shapeType == 0 ? ParticleSystemShapeType.Cone : cfg.shapeType;
            sh.radius = cfg.radius;
            sh.angle = cfg.angle;
            sh.rotation = new Vector3(-90, 0, 0);
            if (cfg.shapeType == ParticleSystemShapeType.Box) sh.rotation = Vector3.zero;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.3f));
            if (cfg.noise > 0)
            {
                var n = ps.noise;
                n.enabled = true;
                n.strength = cfg.noise;
                n.frequency = 0.6f;
                n.scrollSpeed = 0.4f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = Mats.Particle(name + (cfg.additive ? "_add" : "_alpha") + cfg.softness + cfg.shape, Color.white, cfg.additive, cfg.softness <= 0 ? 0.6f : cfg.softness, cfg.shape);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (cfg.stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.08f;
                r.lengthScale = 1.5f;
            }
            ps.Play();
            return ps;
        }

        public static void SetRate(ParticleSystem ps, float rate)
        {
            if (!ps) return;
            var em = ps.emission;
            em.rateOverTime = rate;
        }

        public static void Burst(ParticleSystem ps, int count)
        {
            if (ps) ps.Emit(count);
        }

        public static PSConfig FlameCfg => new PSConfig
        {
            rate = 40, life0 = 0.5f, life1 = 1.0f, size0 = 0.8f, size1 = 1.8f, speed0 = 1.5f, speed1 = 3.5f, gravity = -0.15f, radius = 1.6f, angle = 12,
            c0 = new Color(1.0f, 0.55f, 0.15f, 1), c1 = new Color(1.0f, 0.28f, 0.06f, 1), additive = true, world = true, softness = 0.9f, shape = 2, max = 400
        };

        public static PSConfig SmokeCfg => new PSConfig
        {
            rate = 8, life0 = 4, life1 = 7, size0 = 1.8f, size1 = 3.8f, speed0 = 1.2f, speed1 = 2.2f, gravity = -0.05f, radius = 1.2f, angle = 15,
            c0 = new Color(0.25f, 0.23f, 0.22f, 0.55f), c1 = new Color(0.42f, 0.4f, 0.38f, 0.45f), additive = false, world = true, softness = 1f, shape = 2, max = 200, noise = 0.6f
        };

        public static PSConfig EmberCfg => new PSConfig
        {
            rate = 14, life0 = 1.5f, life1 = 3f, size0 = 0.08f, size1 = 0.18f, speed0 = 3, speed1 = 7, gravity = -0.1f, radius = 1.4f, angle = 30,
            c0 = new Color(1.0f, 0.7f, 0.3f, 1), c1 = new Color(1.0f, 0.4f, 0.1f, 1), additive = true, world = true, softness = 0.5f, max = 200, noise = 1.2f
        };

        public static PSConfig ChimneyCfg => new PSConfig
        {
            rate = 2.2f, life0 = 4, life1 = 6, size0 = 0.5f, size1 = 1.1f, speed0 = 0.5f, speed1 = 0.9f, gravity = -0.04f, radius = 0.15f, angle = 6,
            c0 = new Color(0.8f, 0.8f, 0.82f, 0.45f), c1 = new Color(0.65f, 0.65f, 0.68f, 0.35f), additive = false, world = true, softness = 1, shape = 2, max = 60, noise = 0.25f
        };

        public static PSConfig SparkCfg => new PSConfig
        {
            rate = 6, life0 = 0.3f, life1 = 0.8f, size0 = 0.05f, size1 = 0.1f, speed0 = 2, speed1 = 4, gravity = 0.6f, radius = 0.3f, angle = 45,
            c0 = new Color(1, 0.8f, 0.4f, 1), c1 = new Color(1, 0.5f, 0.1f, 1), additive = true, world = true, softness = 0.4f, stretch = true, max = 80
        };

        public static PSConfig GlimmerCfg => new PSConfig
        {
            rate = 30, life0 = 1.2f, life1 = 2.4f, size0 = 0.08f, size1 = 0.22f, speed0 = 0.2f, speed1 = 0.8f, gravity = -0.05f, radius = 0.6f, angle = 60,
            c0 = new Color(1.6f, 1.2f, 0.5f, 1), c1 = new Color(0.8f, 1.2f, 1.8f, 1), additive = true, world = true, softness = 0.5f, max = 200, noise = 0.8f,
            shapeType = ParticleSystemShapeType.Sphere
        };

        public static PSConfig DustCfg => new PSConfig
        {
            rate = 0, life0 = 1.5f, life1 = 3f, size0 = 1.2f, size1 = 2.6f, speed0 = 1, speed1 = 3.5f, gravity = 0.05f, radius = 1.2f, angle = 70,
            c0 = new Color(0.7f, 0.6f, 0.48f, 0.6f), c1 = new Color(0.55f, 0.48f, 0.4f, 0.5f), additive = false, world = true, softness = 1, shape = 2, max = 120,
            shapeType = ParticleSystemShapeType.Hemisphere
        };

        public static PSConfig RainCfg => new PSConfig
        {
            rate = 1400, life0 = 1.4f, life1 = 1.6f, size0 = 0.05f, size1 = 0.07f, speed0 = 28, speed1 = 34, gravity = 0, radius = 1,
            c0 = new Color(0.75f, 0.82f, 0.95f, 0.45f), c1 = new Color(0.8f, 0.85f, 1f, 0.3f), additive = false, world = true, softness = 0.6f, stretch = true, max = 3000,
            shapeType = ParticleSystemShapeType.Box
        };

        public static PSConfig SnowCfg => new PSConfig
        {
            rate = 500, life0 = 6f, life1 = 9f, size0 = 0.12f, size1 = 0.25f, speed0 = 2.5f, speed1 = 4f, gravity = 0, radius = 1,
            c0 = new Color(1, 1, 1, 0.9f), c1 = new Color(0.9f, 0.93f, 1f, 0.8f), additive = false, world = true, softness = 0.6f, max = 4000, noise = 0.8f,
            shapeType = ParticleSystemShapeType.Box
        };

        public static PSConfig FireflyCfg => new PSConfig
        {
            rate = 18, life0 = 4, life1 = 8, size0 = 0.12f, size1 = 0.22f, speed0 = 0.1f, speed1 = 0.4f, gravity = -0.01f, radius = 1,
            c0 = new Color(1.6f, 1.8f, 0.5f, 1), c1 = new Color(1.0f, 1.6f, 0.4f, 1), additive = true, world = true, softness = 0.5f, max = 160, noise = 1.0f,
            shapeType = ParticleSystemShapeType.Box
        };

        public static PSConfig ButterflyCfg => new PSConfig
        {
            rate = 5, life0 = 6, life1 = 10, size0 = 0.16f, size1 = 0.26f, speed0 = 0.4f, speed1 = 1.0f, gravity = -0.005f, radius = 1,
            c0 = new Color(1f, 0.75f, 0.25f, 1), c1 = new Color(0.55f, 0.7f, 1f, 1), additive = false, world = true, softness = 0.3f, shape = 1, max = 60, noise = 1.6f,
            shapeType = ParticleSystemShapeType.Box
        };

        public static PSConfig WindCfg => new PSConfig
        {
            rate = 60, life0 = 1.2f, life1 = 2.2f, size0 = 0.15f, size1 = 0.35f, speed0 = 14, speed1 = 22, gravity = 0, radius = 1,
            c0 = new Color(1, 1, 1, 0.35f), c1 = new Color(0.85f, 0.95f, 1f, 0.25f), additive = true, world = true, softness = 0.8f, stretch = true, max = 400,
            shapeType = ParticleSystemShapeType.Box
        };

        public static PSConfig LeafCfg => new PSConfig
        {
            rate = 14, life0 = 2, life1 = 3.5f, size0 = 0.18f, size1 = 0.3f, speed0 = 8, speed1 = 14, gravity = 0.15f, radius = 1,
            c0 = new Color(0.55f, 0.7f, 0.25f, 1), c1 = new Color(0.85f, 0.55f, 0.2f, 1), additive = false, world = true, softness = 0.3f, shape = 1, max = 120, noise = 2f,
            shapeType = ParticleSystemShapeType.Box
        };

        public static PSConfig MistCfg => new PSConfig
        {
            rate = 10, life0 = 3, life1 = 5, size0 = 3, size1 = 6, speed0 = 0.5f, speed1 = 1.5f, gravity = -0.02f, radius = 4, angle = 50,
            c0 = new Color(0.95f, 0.97f, 1f, 0.35f), c1 = new Color(0.9f, 0.95f, 1f, 0.25f), additive = false, world = true, softness = 1, shape = 2, max = 80, noise = 0.4f
        };

        public static Light PointLight(Transform parent, Color c, float range, float intensity, Vector3 localPos)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        /// <summary>A flat ring mesh (uv.x around, uv.y across) for ripples and selection halos.</summary>
        public static Mesh Ring(float inner, float outer, int segs = 64)
        {
            var m = new Mesh { name = "Ring" };
            var v = new Vector3[(segs + 1) * 2];
            var uv = new Vector2[v.Length];
            var c = new Color[v.Length];
            var t = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float a = i * Mathf.PI * 2 / segs;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * inner; v[i * 2 + 1] = d * outer;
                uv[i * 2] = new Vector2(i / (float)segs, 0); uv[i * 2 + 1] = new Vector2(i / (float)segs, 1);
                c[i * 2] = c[i * 2 + 1] = Color.white;
            }
            for (int i = 0; i < segs; i++)
            {
                int a = i * 2;
                t[i * 6] = a; t[i * 6 + 1] = a + 1; t[i * 6 + 2] = a + 3;
                t[i * 6 + 3] = a; t[i * 6 + 4] = a + 3; t[i * 6 + 5] = a + 2;
            }
            m.vertices = v; m.uv = uv; m.colors = c; m.triangles = t;
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Expanding golden shockwave when a power touches the world.</summary>
    public sealed class RippleFx : MonoBehaviour
    {
        float _t;
        public float duration = 1.6f, maxRadius = 9f;
        Material _mat;
        static Mesh _ring;

        public static void Spawn(Vector3 pos, Color color, float radius = 9f)
        {
            var go = new GameObject("Ripple");
            go.transform.position = pos + Vector3.up * 0.15f;
            _ring = _ring ? _ring : Fx.Ring(0.85f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = _ring;
            var mr = go.AddComponent<MeshRenderer>();
            var r = go.AddComponent<RippleFx>();
            r._mat = Mats.NewGlow(color, 2f, 6);
            r.maxRadius = radius;
            mr.sharedMaterial = r._mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float u = _t / duration;
            float s = Mathf.Lerp(0.3f, maxRadius, 1 - Mathf.Pow(1 - Mathf.Clamp01(u), 3));
            transform.localScale = new Vector3(s, 1, s);
            _mat.SetFloat("_Alpha", 1 - Mathf.Clamp01(u));
            if (u >= 1) { Destroy(_mat); Destroy(gameObject); }
        }
    }

    /// <summary>One-shot particle burst that cleans itself up.</summary>
    public sealed class AutoDestroy : MonoBehaviour
    {
        public float after = 5;
        void Update() { after -= Time.unscaledDeltaTime; if (after <= 0) Destroy(gameObject); }
    }
}
