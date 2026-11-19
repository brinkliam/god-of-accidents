using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GOA.Game
{
    /// <summary>Material factory. All shaders live under Resources so they ship with every build.</summary>
    public static class Mats
    {
        static readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        static Shader _stylized, _water, _sky, _particle, _glow;

        public static Shader StylizedShader => _stylized ? _stylized : (_stylized = Load("GOA_Stylized", "GOA/Stylized"));
        static Shader Load(string res, string name)
        {
            var s = Resources.Load<Shader>("Shaders/" + res);
            if (s == null) s = Shader.Find(name);
            if (s == null) Debug.LogError($"[GOA] Missing shader {name}");
            return s;
        }

        public static Material Stylized(string key, float wind = 0, float foliage = 0, float rim = 0.25f, float wrap = 0.45f, float gloss = 0.05f, float snow = 1f, bool uvMask = false)
        {
            if (_cache.TryGetValue(key, out var m)) return m;
            m = new Material(StylizedShader) { name = "GOA_" + key, enableInstancing = true };
            m.SetFloat("_Wind", wind);
            m.SetFloat("_Foliage", foliage);
            m.SetFloat("_Rim", rim);
            m.SetFloat("_Wrap", wrap);
            m.SetFloat("_Gloss", gloss);
            m.SetFloat("_SnowAmount", snow);
            m.SetFloat("_UVMask", uvMask ? 1 : 0);
            _cache[key] = m;
            return m;
        }

        public static Material Emissive(string key, Color emission, float nightOnly)
        {
            if (_cache.TryGetValue(key, out var m)) return m;
            m = new Material(StylizedShader) { name = "GOA_" + key, enableInstancing = true };
            m.SetColor("_EmissionColor", emission);
            m.SetFloat("_NightGlow", nightOnly);
            m.SetFloat("_SnowAmount", 0);
            _cache[key] = m;
            return m;
        }

        public static Material Water(string key)
        {
            if (_cache.TryGetValue(key, out var m)) return m;
            _water = _water ? _water : Load("GOA_Water", "GOA/Water");
            m = new Material(_water) { name = "GOA_" + key };
            _cache[key] = m;
            return m;
        }

        public static Material Sky()
        {
            if (_cache.TryGetValue("sky", out var m)) return m;
            _sky = _sky ? _sky : Load("GOA_Sky", "GOA/Sky");
            m = new Material(_sky) { name = "GOA_Sky" };
            _cache["sky"] = m;
            return m;
        }

        public static Material Particle(string key, Color color, bool additive, float softness = 0.6f, float shape = 0, float fog = 1)
        {
            if (_cache.TryGetValue(key, out var m)) return m;
            _particle = _particle ? _particle : Load("GOA_Particle", "GOA/Particle");
            m = new Material(_particle) { name = "GOA_P_" + key };
            m.SetColor("_Color", color);
            m.SetFloat("_Softness", softness);
            m.SetFloat("_Shape", shape);
            m.SetFloat("_FogAmount", fog);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.renderQueue = 3000 + (additive ? 10 : 0);
            _cache[key] = m;
            return m;
        }

        public static Material Glow(string key, Color color, float pulseSpeed = 1.5f, float pulseCount = 4, float soft = 0.6f)
        {
            if (_cache.TryGetValue(key, out var m)) return m;
            _glow = _glow ? _glow : Load("GOA_Glow", "GOA/Glow");
            m = new Material(_glow) { name = "GOA_G_" + key };
            m.SetColor("_Color", color);
            m.SetFloat("_PulseSpeed", pulseSpeed);
            m.SetFloat("_PulseCount", pulseCount);
            m.SetFloat("_EdgeSoft", soft);
            _cache[key] = m;
            return m;
        }

        public static Material NewGlow(Color color, float pulseSpeed = 1.5f, float pulseCount = 4)
        {
            _glow = _glow ? _glow : Load("GOA_Glow", "GOA/Glow");
            var m = new Material(_glow);
            m.SetColor("_Color", color);
            m.SetFloat("_PulseSpeed", pulseSpeed);
            m.SetFloat("_PulseCount", pulseCount);
            return m;
        }
    }
}
