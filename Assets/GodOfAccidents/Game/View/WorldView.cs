using System;
using System.Collections.Generic;
using GOA.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace GOA.Game
{
    public enum PickKind { None, Citizen, Prop, Building, Ground }

    public struct PickResult
    {
        public PickKind kind; public int id; public Vector3 point; public float screenDistance;
    }

    /// <summary>Presents a World as a living miniature: buildings, villagers, props, fires, birds, crops and sheep.</summary>
    public sealed class WorldView : MonoBehaviour
    {
        public World World { get; private set; }
        public Foliage Foliage { get; private set; }
        public Atmosphere Atmosphere;
        Transform _static, _dynamic;
        readonly Dictionary<int, CitizenView> _citizens = new Dictionary<int, CitizenView>();
        readonly List<BuildingView> _buildings = new List<BuildingView>();
        readonly Dictionary<int, PropView> _props = new Dictionary<int, PropView>();
        readonly Dictionary<int, FireView> _fires = new Dictionary<int, FireView>();
        readonly List<(Glimmer g, ParticleSystem ps, Light l)> _glimmers = new List<(Glimmer, ParticleSystem, Light)>();
        Transform _crow;
        Transform _crowWingL, _crowWingR;
        readonly List<Transform> _sheep = new List<Transform>();
        readonly List<Transform> _flock = new List<Transform>();
        ParticleSystem _gustFx;
        ParticleSystem _dust, _sparkles, _splash;
        ParticleSystem _waterfallMist;
        Mesh[] _cropStage;
        readonly List<Matrix4x4[]> _cropBatches = new List<Matrix4x4[]>();
        Material _cropMat;
        Transform _selection;
        Material _selectionMat;
        public PickResult Selected;
        public float Quality = 1f;
        float _ripplePhase;
        // opportunity markers: flashing rings where a nudge would cause something juicy right now
        public bool ShowOpportunities;
        public readonly List<Opportunity> Opportunities = new List<Opportunity>();
        readonly List<(Transform t, Material m)> _oppMarkers = new List<(Transform, Material)>();
        float _oppTimer;
        // armed power: valid targets glow, invalid ones are dimmed
        public PowerType? Armed;
        readonly List<(Transform t, Material m)> _targetRings = new List<(Transform, Material)>();

        static float RoofHeight(Building b) => b.type switch
        {
            BuildingType.Chapel => 15f, BuildingType.ClockTower => 21f, BuildingType.Windmill => 17f, BuildingType.Mill => 11f,
            BuildingType.ElderHall => 10f, BuildingType.Library => 10f, BuildingType.Well => 3.5f, BuildingType.Haystack => 4.5f,
            BuildingType.Wall => 4f, BuildingType.House => 7f + b.tier * 1.8f, BuildingType.Tavern => 12f, BuildingType.Barn => 10f,
            BuildingType.Granary => 10f, BuildingType.Workshop => 9f, BuildingType.Market => 7f, _ => 9f,
        };

        /// <summary>Lowest camera height over this point: clear of every roof, with a little headroom.</summary>
        public float CameraFloor(Vector3 p)
        {
            float floor = float.MinValue;
            if (World == null) return floor;
            var hp = new Vec2(p.x, p.z);
            foreach (var b in World.Buildings)
            {
                if (b.destroyed && b.buildProgress <= 0) continue;
                var local = hp - b.pos;
                float cs = Mathf.Cos(-b.rotation), sn = Mathf.Sin(-b.rotation);
                float lx = Mathf.Abs(local.x * cs - local.z * sn) - b.size.x * 0.5f, lz = Mathf.Abs(local.x * sn + local.z * cs) - b.size.z * 0.5f;
                float outside = Mathf.Max(lx, lz);
                if (outside > 6f) continue;
                // full clearance over the roof, tapering to nothing three metres beyond the walls
                float h = TerrainBuilder.Height(b.pos.x, b.pos.z) + RoofHeight(b) * (b.Complete ? 1 : Mathf.Max(0.3f, b.buildProgress)) + 2.5f;
                // full clearance within a metre of the walls (meshes overhang their footprints), tapering out to six metres
                floor = Mathf.Max(floor, Mathf.Lerp(h, h - 8f, Mathf.Clamp01((outside - 1f) / 5f)));
            }
            return floor;
        }

        public void Init(World w, float quality)
        {
            Quality = quality;
            _static = new GameObject("Static").transform; _static.SetParent(transform, false);
            var terrainMat = Mats.Stylized("terrain", foliage: 1f, rim: 0.03f, wrap: 0.3f, uvMask: true);
            TerrainBuilder.Build(w, _static, terrainMat, quality >= 0.75f ? 1f : 0.7f);
            WaterBuilder.Build(_static);
            Foliage = new Foliage();
            Foliage.Build(w, quality);
            BuildFences(w);
            BuildCrops(w);
            BuildAnimals();
            _waterfallMist = Fx.Make(_static, "WaterfallMist", Fx.MistCfg, TerrainBuilder.Ground(VillageLayout.RiverX(TerrainBuilder.WaterfallZ - 4), TerrainBuilder.WaterfallZ - 4) + Vector3.up);
            _dust = Fx.Make(transform, "Dust", Fx.DustCfg); Fx.SetRate(_dust, 0);
            var stepCfg = Fx.DustCfg; stepCfg.size0 = 0.25f; stepCfg.size1 = 0.5f; stepCfg.life0 = 0.5f; stepCfg.life1 = 0.9f; stepCfg.speed0 = 0.2f; stepCfg.speed1 = 0.6f; stepCfg.radius = 0.15f; stepCfg.max = 200;
            var steps = Fx.Make(transform, "Footsteps", stepCfg); Fx.SetRate(steps, 0);
            CitizenView.StepDust = quality >= 0.7f ? steps : null;
            var spc = Fx.SparkCfg; spc.c0 = new Color(0.75f, 0.9f, 1f, 0.9f); spc.c1 = new Color(0.9f, 0.97f, 1f, 0.8f); spc.additive = false; spc.gravity = 1.4f; spc.speed0 = 3; spc.speed1 = 6; spc.size0 = 0.08f; spc.size1 = 0.18f; spc.life0 = 0.5f; spc.life1 = 1.1f; spc.max = 300; spc.angle = 35;
            _splash = Fx.Make(transform, "Splash", spc); Fx.SetRate(_splash, 0);
            var sc = Fx.GlimmerCfg; sc.rate = 0; _sparkles = Fx.Make(transform, "Sparkles", sc);
            _selectionMat = Mats.NewGlow(new Color(1.4f, 1.1f, 0.5f), 1.2f, 8);
            var selGo = new GameObject("Selection");
            selGo.transform.SetParent(transform, false);
            selGo.AddComponent<MeshFilter>().sharedMesh = Fx.Ring(0.82f, 1f, 48);
            var mr = selGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = _selectionMat; mr.shadowCastingMode = ShadowCastingMode.Off;
            _selection = selGo.transform;
            _selection.gameObject.SetActive(false);
            Bind(w);
        }

        /// <summary>Attach to a (possibly different) world - used for replays and branches.</summary>
        public void Bind(World w)
        {
            if (_dynamic) Destroy(_dynamic.gameObject);
            _citizens.Clear(); _buildings.Clear(); _props.Clear(); _fires.Clear(); _glimmers.Clear();
            World = w;
            _dynamic = new GameObject("Dynamic").transform; _dynamic.SetParent(transform, false);
            foreach (var b in w.Buildings) _buildings.Add(new BuildingView(b, _dynamic));
            BuildCrow();
            var gc = Fx.WindCfg; gc.rate = 0; gc.max = 600;
            _gustFx = Fx.Make(_dynamic, "Gust", gc);
            var sh = _gustFx.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(30, 6, 4);
            Selected = default;
        }

        // ------------------------------------------------------------------ static decoration

        void BuildFences(World w)
        {
            var k = new MeshKit();
            void FenceRect(FieldRect f, bool full)
            {
                float hx = f.size.x * 0.5f + 1.2f, hz = f.size.z * 0.5f + 1.2f;
                var corners = new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(hx, hz), new Vector2(-hx, hz) };
                float cs = Mathf.Cos(f.rotation), sn = Mathf.Sin(f.rotation);
                Vector3 W(Vector2 l) { var x = f.center.x + l.x * cs - l.y * sn; var z = f.center.z + l.x * sn + l.y * cs; return TerrainBuilder.Ground(x, z); }
                for (int e = 0; e < 4; e++)
                {
                    if (!full && e % 2 == 1) continue;
                    var a = corners[e]; var b = corners[(e + 1) % 4];
                    int posts = Mathf.CeilToInt(Vector2.Distance(a, b) / 2.6f);
                    for (int i = 0; i <= posts; i++)
                    {
                        var p = W(Vector2.Lerp(a, b, i / (float)posts));
                        if (VillageLayout.DistanceToRiver(new Vec2(p.x, p.z)) < VillageLayout.RiverHalfWidth + 1) continue;
                        k.Box(p + Vector3.up * 0.45f, new Vector3(0.12f, 0.9f, 0.12f), Palette.Timber * 1.2f);
                        if (i < posts)
                        {
                            var q = W(Vector2.Lerp(a, b, (i + 1) / (float)posts));
                            var mid = (p + q) * 0.5f;
                            var dir = q - p;
                            k.M = Matrix4x4.TRS(mid, Quaternion.LookRotation(new Vector3(dir.x, dir.y, dir.z)), Vector3.one);
                            k.Box(new Vector3(0, 0.7f, 0), new Vector3(0.07f, 0.07f, dir.magnitude), Palette.Timber * 1.35f);
                            k.Box(new Vector3(0, 0.38f, 0), new Vector3(0.07f, 0.07f, dir.magnitude), Palette.Timber * 1.35f);
                            k.M = Matrix4x4.identity;
                        }
                    }
                }
            }
            FenceRect(VillageLayout.Pasture, true);
            foreach (var f in VillageLayout.Fields) FenceRect(f, false);
            // a little bridge over the river on the east road
            float rx = VillageLayout.RiverX(0);
            var bridgeY = TerrainBuilder.WaterLevel + 0.9f;
            k.Box(new Vector3(rx, bridgeY, 0), new Vector3(VillageLayout.RiverHalfWidth * 2 + 6, 0.35f, 3.4f), Palette.Timber * 1.25f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Box(new Vector3(rx, bridgeY + 0.6f, s * 1.6f), new Vector3(VillageLayout.RiverHalfWidth * 2 + 6, 0.12f, 0.12f), Palette.Timber);
                for (int i = -4; i <= 4; i++) k.Box(new Vector3(rx + i * 2f, bridgeY + 0.35f, s * 1.6f), new Vector3(0.14f, 0.7f, 0.14f), Palette.Timber);
            }
            k.Box(new Vector3(rx - 4, bridgeY - 0.8f, 0), new Vector3(1.2f, 1.6f, 3.6f), Palette.Stone);
            k.Box(new Vector3(rx + 4, bridgeY - 0.8f, 0), new Vector3(1.2f, 1.6f, 3.6f), Palette.Stone);
            // signpost at the east road and a bench by the green
            var sp = TerrainBuilder.Ground(30, -2.5f);
            k.Box(sp + Vector3.up, new Vector3(0.12f, 2, 0.12f), Palette.Timber);
            k.Box(sp + new Vector3(0.6f, 1.7f, 0), new Vector3(1.2f, 0.25f, 0.06f), Palette.PlasterWarm);
            var bench = TerrainBuilder.Ground(20, 15);
            k.Box(bench + Vector3.up * 0.45f, new Vector3(2, 0.1f, 0.5f), Palette.Timber * 1.3f);
            k.Box(bench + new Vector3(-0.8f, 0.22f, 0), new Vector3(0.12f, 0.45f, 0.45f), Palette.Timber);
            k.Box(bench + new Vector3(0.8f, 0.22f, 0), new Vector3(0.12f, 0.45f, 0.45f), Palette.Timber);
            var go = new GameObject("Fences");
            go.transform.SetParent(_static, false);
            go.AddComponent<MeshFilter>().sharedMesh = k.ToMesh("Fences");
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Stylized("props", rim: 0.2f);
        }

        void BuildCrops(World w)
        {
            _cropMat = Mats.Stylized("crops", wind: 2.4f, foliage: 0.3f, rim: 0.2f, wrap: 0.7f, snow: 0.5f);
            _cropStage = new[]
            {
                Foliage.CropMesh(1, Palette.WheatGreen * 1.1f, 0.25f),
                Foliage.CropMesh(2, Palette.WheatGreen, 0.7f),
                Foliage.CropMesh(3, Palette.Wheat, 0.95f),
                Foliage.CropMesh(4, Palette.Wheat * 0.85f, 0.18f),
            };
            var list = new List<Matrix4x4>();
            foreach (var f in VillageLayout.Fields)
            {
                float cs = Mathf.Cos(f.rotation), sn = Mathf.Sin(f.rotation);
                for (float lz = -f.size.z * 0.5f + 1.2f; lz < f.size.z * 0.5f - 1; lz += 1.43f)
                    for (float lx = -f.size.x * 0.5f + 1.0f; lx < f.size.x * 0.5f - 0.8f; lx += 1.05f)
                    {
                        float x = f.center.x + lx * cs - lz * sn, z = f.center.z + lx * sn + lz * cs;
                        list.Add(Matrix4x4.TRS(TerrainBuilder.Ground(x, z), Quaternion.Euler(0, f.rotation * -Mathf.Rad2Deg + (lx * 13 % 20), 0), Vector3.one));
                    }
            }
            for (int i = 0; i < list.Count; i += 1023) _cropBatches.Add(list.GetRange(i, Mathf.Min(1023, list.Count - i)).ToArray());
        }

        static Mesh SheepMesh()
        {
            var k = new MeshKit();
            k.Blob(new Vector3(0, 0.6f, 0), new Vector3(0.55f, 0.4f, 0.7f), new Color(0.95f, 0.93f, 0.88f), 5, 0.2f, 1);
            k.Blob(new Vector3(0, 0.75f, 0.65f), new Vector3(0.2f, 0.22f, 0.26f), new Color(0.2f, 0.18f, 0.17f), 6, 0.1f, 0);
            foreach (var x in new[] { -0.25f, 0.25f }) foreach (var z in new[] { -0.35f, 0.35f })
                    k.Box(new Vector3(x, 0.2f, z), new Vector3(0.1f, 0.4f, 0.1f), new Color(0.2f, 0.18f, 0.17f));
            return k.ToMesh("Sheep");
        }

        static Mesh BirdMesh(Color c, float span, out Mesh wing)
        {
            var k = new MeshKit();
            k.Blob(Vector3.zero, new Vector3(0.14f, 0.12f, 0.32f) * span, c, 3, 0.05f, 0);
            k.Blob(new Vector3(0, 0.05f, 0.3f) * span, new Vector3(0.09f, 0.09f, 0.1f) * span, c, 4, 0.05f, 0);
            k.Cone(new Vector3(0, 0.05f, 0.38f) * span, 0.03f * span, 0.12f * span, 4, Palette.Brass * 0.7f);
            var w = new MeshKit();
            w.Tri(Vector3.zero, new Vector3(0.8f, 0, -0.1f) * span, new Vector3(0, 0, -0.3f) * span, c);
            w.Tri(new Vector3(0, 0, -0.3f) * span, new Vector3(0.8f, 0, -0.1f) * span, Vector3.zero, c);
            wing = w.ToMesh("Wing");
            return k.ToMesh("Bird");
        }

        void BuildAnimals()
        {
            var mat = Mats.Stylized("animals", rim: 0.4f, wrap: 0.6f);
            var sheep = SheepMesh();
            for (int i = 0; i < 9; i++)
            {
                var go = new GameObject("Sheep" + i);
                go.transform.SetParent(_static, false);
                go.AddComponent<MeshFilter>().sharedMesh = sheep;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                _sheep.Add(go.transform);
            }
            var body = BirdMesh(new Color(0.25f, 0.22f, 0.2f), 0.7f, out var wing);
            for (int i = 0; i < 13; i++) // 7 swallows high up, 6 sparrows flitting low over the green
            {
                var go = new GameObject("Swallow" + i);
                go.transform.SetParent(_static, false);
                go.AddComponent<MeshFilter>().sharedMesh = body;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                AddWings(go.transform, wing, mat, out _, out _);
                if (i >= 7) go.transform.localScale = Vector3.one * 0.6f;
                _flock.Add(go.transform);
            }
        }

        static void AddWings(Transform t, Mesh wing, Material mat, out Transform l, out Transform r)
        {
            var wl = new GameObject("WingL"); wl.transform.SetParent(t, false);
            wl.AddComponent<MeshFilter>().sharedMesh = wing; wl.AddComponent<MeshRenderer>().sharedMaterial = mat;
            wl.transform.localScale = new Vector3(-1, 1, 1);
            var wr = new GameObject("WingR"); wr.transform.SetParent(t, false);
            wr.AddComponent<MeshFilter>().sharedMesh = wing; wr.AddComponent<MeshRenderer>().sharedMaterial = mat;
            l = wl.transform; r = wr.transform;
        }

        void BuildCrow()
        {
            var mat = Mats.Stylized("crow", rim: 0.6f, gloss: 0.4f);
            var body = BirdMesh(new Color(0.08f, 0.08f, 0.1f), 1.3f, out var wing);
            var go = new GameObject("Crow");
            go.transform.SetParent(_dynamic, false);
            go.AddComponent<MeshFilter>().sharedMesh = body;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            AddWings(go.transform, wing, mat, out _crowWingL, out _crowWingR);
            _crow = go.transform;
        }

        // ------------------------------------------------------------------ per-frame

        public void Tick(double t, float dt, float simSpeed, Camera cam)
        {
            var w = World;
            if (w == null) return;

            CitizenView.CameraPos = cam ? cam.transform.position : Vector3.zero;
            // villagers
            foreach (var c in w.Citizens)
            {
                if (!_citizens.TryGetValue(c.id, out var v))
                {
                    if (!c.IsActive) continue;
                    v = CitizenView.Create(_dynamic, c, (long)t);
                    _citizens[c.id] = v;
                }
                if (!c.IsActive) { if (v) { Destroy(v.gameObject); } _citizens.Remove(c.id); continue; }
                v.Tick(w, c, t, dt, simSpeed);
            }

            foreach (var bv in _buildings) bv.Tick(w, t, dt, Atmosphere ? Atmosphere.Night : 0);
            TickProps(t, dt);
            TickFires(dt);
            TickGlimmers(t);
            TickBirds(t, dt);
            TickGust(t);
            TickSelection(t, dt);
            TickOpportunities(dt);
            TickArmedTargets(t);

            if (Foliage != null) Foliage.Draw(cam);
            DrawCrops(t, cam);
        }

        void DrawCrops(double t, Camera cam)
        {
            var season = SimTime.SeasonOf((long)t);
            float sp = (float)((t / SimTime.Day) % SimTime.DaysPerSeason) / SimTime.DaysPerSeason;
            Mesh m = season switch
            {
                Season.Spring => sp < 0.5f ? _cropStage[0] : _cropStage[1],
                Season.Summer => sp < 0.6f ? _cropStage[1] : _cropStage[2],
                Season.Autumn => sp < 0.55f ? _cropStage[2] : _cropStage[3],
                _ => null
            };
            if (m == null) return;
            var rp = new RenderParams(_cropMat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = new Bounds(new Vector3(0, 0, -68), new Vector3(120, 10, 70)) };
            foreach (var b in _cropBatches) Graphics.RenderMeshInstanced(rp, m, 0, b, b.Length);
        }

        void TickProps(double t, float dt)
        {
            var w = World;
            foreach (var p in w.Props)
            {
                if (p.type == PropType.CrowNest) continue;
                if (!_props.TryGetValue(p.id, out var pv)) { pv = new PropView(p, _dynamic); _props[p.id] = pv; }
                pv.Tick(w, p, t, dt, Atmosphere ? Atmosphere.Night : 0, Selected.kind == PickKind.Prop && Selected.id == p.id);
            }
        }

        void TickFires(float dt)
        {
            foreach (var f in World.Fires)
            {
                _fires.TryGetValue(f.id, out var fv);
                if (f.active && fv == null) { fv = new FireView(f, World.GetBuilding(f.buildingId), _dynamic); _fires[f.id] = fv; }
                if (fv != null) { fv.Tick(f, dt); if (!f.active && fv.Done) { fv.Dispose(); _fires.Remove(f.id); } }
            }
        }

        void TickGlimmers(double t)
        {
            foreach (var g in World.Glimmers)
            {
                bool active = g.until > t;
                int idx = _glimmers.FindIndex(x => x.g == g);
                if (active && idx < 0)
                {
                    var pos = TerrainBuilder.Ground(g.pos) + Vector3.up * 1.9f;
                    var ps = Fx.Make(_dynamic, "Glimmer", Fx.GlimmerCfg, pos);
                    var l = Fx.PointLight(ps.transform, new Color(1f, 0.85f, 0.5f), 6, 2.5f, Vector3.zero);
                    _glimmers.Add((g, ps, l));
                }
                else if (!active && idx >= 0)
                {
                    var e = _glimmers[idx];
                    if (e.ps) { Fx.SetRate(e.ps, 0); e.ps.gameObject.AddComponent<AutoDestroy>().after = 3; }
                    if (e.l) Destroy(e.l.gameObject);
                    _glimmers.RemoveAt(idx);
                }
            }
            float pulse = 2f + Mathf.Sin(Time.time * 4) * 0.8f;
            foreach (var e in _glimmers) if (e.l) e.l.intensity = pulse;
        }

        void TickBirds(double t, float dt)
        {
            // the crow: perched by the oak unless one of its flights is under way
            BirdFlight active = null;
            foreach (var b in World.Birds) if (!b.landed && b.end > t && b.start <= t) active = b;
            var oak = TerrainBuilder.Ground(VillageLayout.OakPos) + new Vector3(1.5f, 11.5f, 0.5f);
            Vector3 pos; Quaternion rot; float flap;
            if (active != null)
            {
                float u = Mathf.Clamp01((float)((t - active.start) / (active.end - active.start)));
                var a = oak; var b = TerrainBuilder.Ground(active.to) + Vector3.up * 2.5f;
                pos = Vector3.Lerp(a, b, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 18f;
                var ahead = Vector3.Lerp(a, b, Mathf.Min(1, u + 0.02f)) + Vector3.up * Mathf.Sin(Mathf.Min(1, u + 0.02f) * Mathf.PI) * 18f;
                rot = Quaternion.LookRotation((ahead - pos).sqrMagnitude > 1e-4f ? ahead - pos : Vector3.forward);
                flap = Mathf.Sin(Time.time * 14) * 50;
            }
            else
            {
                // lazy circles above the oak
                float a = (float)(t * 0.0025);
                pos = oak + new Vector3(Mathf.Cos(a) * 6, 4 + Mathf.Sin(a * 2) * 1.5f, Mathf.Sin(a) * 6);
                rot = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)));
                flap = Mathf.Sin(Time.time * 6) * 25;
            }
            _crow.position = pos; _crow.rotation = rot;
            _crowWingL.localRotation = Quaternion.Euler(0, 0, -flap);
            _crowWingR.localRotation = Quaternion.Euler(0, 0, flap);

            for (int i = 0; i < _flock.Count; i++)
            {
                float a = Time.time * 0.25f + i * 0.4f;
                bool low = i >= 7;
                Vector3 Path(float aa) => low
                    ? new Vector3(Mathf.Cos(aa * 1.1f) * 26 + 4, 6.5f + Mathf.Abs(Mathf.Sin(aa * 6f + i)) * 1.6f, Mathf.Sin(aa * 0.8f) * 20)
                    : new Vector3(Mathf.Cos(aa * 0.7f) * 70, 32 + Mathf.Sin(aa * 1.3f + i) * 4, Mathf.Sin(aa * 0.9f) * 50 - 10);
                if (low) a = Time.time * 0.32f + i * 0.9f;
                var c = Path(a);
                if (low) c.y += TerrainBuilder.Height(c.x, c.z);
                var off = new Vector3(Mathf.Cos(i * 2.1f) * 3, Mathf.Sin(i * 1.7f), Mathf.Sin(i * 2.1f) * 3);
                var p = c + off;
                var next = Path(a + 0.01f) + off;
                if (low) next.y += TerrainBuilder.Height(next.x, next.z);
                _flock[i].position = p;
                _flock[i].rotation = Quaternion.LookRotation(next - p);
                float f = Mathf.Sin(Time.time * 12 + i) * 45;
                _flock[i].GetChild(0).localRotation = Quaternion.Euler(0, 0, -f);
                _flock[i].GetChild(1).localRotation = Quaternion.Euler(0, 0, f);
            }

            // sheep graze in the pasture (purely decorative)
            var pa = VillageLayout.Pasture;
            for (int i = 0; i < _sheep.Count; i++)
            {
                float tt = (float)(t / 600.0) + i * 17.3f;
                float x = pa.center.x + Mathf.Sin(tt * 0.13f + i) * pa.size.x * 0.38f;
                float z = pa.center.z + Mathf.Cos(tt * 0.11f + i * 2) * pa.size.z * 0.36f;
                var p = TerrainBuilder.Ground(x, z);
                var dir = new Vector3(Mathf.Cos(tt * 0.13f + i) * 0.13f * pa.size.x, 0, -Mathf.Sin(tt * 0.11f + i * 2) * 0.11f * pa.size.z);
                _sheep[i].position = p;
                if (dir.sqrMagnitude > 1e-4f) _sheep[i].rotation = Quaternion.Slerp(_sheep[i].rotation, Quaternion.LookRotation(dir), dt * 2);
            }
        }

        void TickGust(double t)
        {
            var g = World.Gust;
            bool on = g != null && g.end > t;
            Fx.SetRate(_gustFx, on ? 220 : 0);
            if (on)
            {
                var dir = new Vector3(g.dir.x, 0, g.dir.z);
                _gustFx.transform.position = TerrainBuilder.Ground(g.origin) + Vector3.up * 3 - dir * 12;
                _gustFx.transform.rotation = Quaternion.LookRotation(dir);
                var sh = _gustFx.shape; sh.rotation = Vector3.zero;
                var m = _gustFx.main; m.startSpeed = 22; m.simulationSpace = ParticleSystemSimulationSpace.World;
            }
        }

        void TickOpportunities(float dt)
        {
            _oppTimer -= Time.unscaledDeltaTime;
            if (_oppTimer <= 0)
            {
                _oppTimer = 0.5f;
                Opportunities.Clear();
                if (ShowOpportunities) Opportunities.AddRange(World.FindOpportunities(4));
            }
            while (_oppMarkers.Count < Opportunities.Count)
            {
                var go = new GameObject("Opportunity");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Fx.Ring(0.7f, 1f, 40);
                var m = Mats.NewGlow(new Color(1.6f, 1.2f, 0.4f), 2.4f, 6);
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off;
                _oppMarkers.Add((go.transform, m));
            }
            float tm = Time.unscaledTime;
            for (int i = 0; i < _oppMarkers.Count; i++)
            {
                var (tr, m) = _oppMarkers[i];
                bool on = i < Opportunities.Count;
                if (tr.gameObject.activeSelf != on) tr.gameObject.SetActive(on);
                if (!on) continue;
                var o = Opportunities[i];
                // a quick double flash every second and a half, gold for Nudge, violet for Distract
                float ph = (tm * 0.66f + i * 0.23f) % 1f;
                float flash = Mathf.Max(Mathf.Exp(-ph * 9f), Mathf.Exp(-Mathf.Abs(ph - 0.22f) * 18f) * 0.7f);
                var baseCol = o.power == PowerType.Distract ? new Color(1.1f, 0.6f, 1.8f) : new Color(1.8f, 1.3f, 0.45f);
                m.SetColor("_Color", baseCol * (0.55f + flash * 1.6f * o.heat));
                float s = 1.6f + ph * 1.4f;
                tr.position = TerrainBuilder.Ground(o.pos) + Vector3.up * 0.15f;
                tr.localScale = new Vector3(s, 1, s);
            }
        }

        void TickArmedTargets(double t)
        {
            int n = 0;
            void Ring(Vector3 at, float r, bool valid)
            {
                if (n >= _targetRings.Count)
                {
                    var go = new GameObject("Target");
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = Fx.Ring(0.84f, 1f, 32);
                    var m = Mats.NewGlow(Color.white, 1.2f, 4);
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off;
                    _targetRings.Add((go.transform, m));
                }
                var (tr, mat) = _targetRings[n++];
                if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
                tr.position = at + Vector3.up * 0.1f;
                float pulse = valid ? 1 + Mathf.Sin(Time.unscaledTime * 5f + n) * 0.08f : 1;
                tr.localScale = new Vector3(r * pulse, 1, r * pulse);
                mat.SetColor("_Color", valid ? new Color(0.5f, 1.3f, 1.7f) : new Color(0.22f, 0.22f, 0.26f));
            }
            if (Armed == PowerType.Nudge)
            {
                foreach (var p in World.Props)
                {
                    if (!p.active || !p.nudgeable) continue;
                    bool valid = (p.type != PropType.Lantern && p.type != PropType.Cart) || p.state == 0;
                    var pos = p.type == PropType.Cart ? World.CartPosition(t) : p.pos;
                    Ring(TerrainBuilder.Ground(pos), p.type == PropType.Cart ? 2.4f : 1.1f, valid);
                }
            }
            else if (Armed == PowerType.Distract)
            {
                foreach (var kv in _citizens)
                {
                    if (!kv.Value || !kv.Value.Visible) continue;
                    var c = World.GetCitizen(kv.Key);
                    if (c == null) continue;
                    bool valid = World.IsAwakeOutside(c) && c.activity.kind != ActivityKind.Distracted;
                    Ring(kv.Value.transform.position, 0.9f, valid);
                }
            }
            for (int i = n; i < _targetRings.Count; i++) if (_targetRings[i].t.gameObject.activeSelf) _targetRings[i].t.gameObject.SetActive(false);
        }

        void TickSelection(double t, float dt)
        {
            Vector3? at = null; float r = 1.2f;
            switch (Selected.kind)
            {
                case PickKind.Citizen:
                    if (_citizens.TryGetValue(Selected.id, out var cv) && cv.Visible) at = cv.transform.position; break;
                case PickKind.Prop:
                    var p = World.GetProp(Selected.id);
                    if (p != null && p.active) { at = TerrainBuilder.Ground(p.type == PropType.Cart ? World.CartPosition(t) : p.pos); r = p.type == PropType.Cart ? 2.6f : 1.0f; }
                    break;
                case PickKind.Building:
                    var b = World.GetBuilding(Selected.id);
                    if (b != null) { at = TerrainBuilder.Ground(b.pos); r = Mathf.Max(b.size.x, b.size.z) * 0.75f; }
                    break;
            }
            _selection.gameObject.SetActive(at.HasValue);
            if (at.HasValue)
            {
                _ripplePhase += dt;
                float s = r * (1 + Mathf.Sin(_ripplePhase * 3) * 0.06f);
                _selection.position = at.Value + Vector3.up * 0.12f;
                _selection.localScale = new Vector3(s, 1, s);
            }
        }

        // ------------------------------------------------------------------ events and effects

        public void OnSimEvent(SimEvent e)
        {
            if (!e.hasPos) return;
            var p = TerrainBuilder.Ground(e.pos);
            switch (e.type)
            {
                case SimEventType.Intervention:
                {
                    // each power has its own colour and shape of impact
                    var col = e.power switch
                    {
                        PowerType.Gust => new Color(0.6f, 1.2f, 1.7f), PowerType.Distract => new Color(1.3f, 0.75f, 1.8f),
                        PowerType.Drop => new Color(0.9f, 0.9f, 1.1f), _ => Palette.Fate
                    };
                    RippleFx.Spawn(p, col, e.power == PowerType.Gust ? 16 : 10);
                    RippleFx.Spawn(p, col * 0.7f, e.power == PowerType.Gust ? 9 : 5);
                    Burst(_sparkles, p + Vector3.up, 60);
                    if (e.power == PowerType.Nudge) Burst(_dust, p, 5);
                    break;
                }
                case SimEventType.Mishap:
                case SimEventType.FoodLoss:
                    Burst(_dust, p, 14);
                    if (e.causes.Length > 0 || e.roots.Length > 0) RippleFx.Spawn(p, Palette.FateCool, 5);
                    break;
                case SimEventType.Meeting:
                case SimEventType.Friendship:
                case SimEventType.Courtship:
                    Burst(_sparkles, p + Vector3.up * 1.6f, e.magnitude >= 2 ? 35 : 15);
                    if (e.roots.Length > 0) RippleFx.Spawn(p, Palette.Fate, 5);
                    break;
                case SimEventType.Rivalry:
                case SimEventType.Brawl:
                    Burst(_dust, p, 8);
                    RippleFx.Spawn(p, new Color(1.6f, 0.35f, 0.25f), 5);
                    break;
                case SimEventType.FireStarted:
                case SimEventType.FireSpread:
                    Burst(_dust, p, 20);
                    RippleFx.Spawn(p, new Color(1.9f, 0.7f, 0.2f), 12);
                    break;
                case SimEventType.FireExtinguished:
                    Burst(_splash, p + Vector3.up * 1.5f, 120);
                    Burst(_dust, p + Vector3.up * 2, 10);
                    break;
                case SimEventType.ItemFound:
                    Burst(_sparkles, p + Vector3.up * 0.6f, 20);
                    break;
                case SimEventType.Trip:
                case SimEventType.ItemDropped:
                    Burst(_dust, p, 6);
                    break;
                case SimEventType.WellBroken:
                    Burst(_dust, p, 40);
                    Burst(_splash, p + Vector3.up, 220);
                    DebrisFx.Spawn(p + Vector3.up * 0.5f, Palette.Stone, 14, 5f);
                    break;
                case SimEventType.BuildingDestroyed:
                    Burst(_dust, p, 70);
                    DebrisFx.Spawn(p + Vector3.up * 2f, Palette.Timber, 22, 7f);
                    RippleFx.Spawn(p, new Color(1.6f, 0.6f, 0.25f), 14);
                    break;
                case SimEventType.CartRoll:
                    Burst(_dust, p, 40);
                    DebrisFx.Spawn(p + Vector3.up * 0.6f, Palette.Wheat, 12, 4f);
                    break;
                case SimEventType.LightningStrike:
                    if (Atmosphere) Atmosphere.Flash(p);
                    break;
                case SimEventType.Construction:
                case SimEventType.Heroism:
                case SimEventType.Rescue:
                case SimEventType.Spark:
                case SimEventType.Apprenticeship:
                    Burst(_sparkles, p + Vector3.up * 1.5f, e.magnitude >= 3 ? 60 : 25);
                    if (e.magnitude >= 3) RippleFx.Spawn(p, Palette.Fate, 7);
                    break;
                case SimEventType.Invention:
                case SimEventType.Election:
                case SimEventType.Marriage:
                case SimEventType.Birth:
                    Burst(_sparkles, p + Vector3.up * 2, 90);
                    RippleFx.Spawn(p, e.roots.Length > 0 ? Palette.Fate : Palette.FateCool, 16);
                    break;
            }
        }


        /// <summary>A golden burst at a point (codex discoveries).</summary>
        public void Celebrate(Vector3 at)
        {
            var p = TerrainBuilder.Ground(at.x, at.z);
            Burst(_sparkles, p + Vector3.up * 2f, 170);
            RippleFx.Spawn(p, Palette.Fate, 18);
            RippleFx.Spawn(p, Palette.Fate * 0.6f, 9);
        }
        static void Burst(ParticleSystem ps, Vector3 at, int n)
        {
            if (!ps) return;
            ps.transform.position = at;
            ps.Emit(n);
        }

        public Vector3 PositionOf(PickKind kind, int id, double t)
        {
            switch (kind)
            {
                case PickKind.Citizen:
                    var c = World.GetCitizen(id);
                    if (c != null) return TerrainBuilder.Ground(World.PositionAt(c, t));
                    break;
                case PickKind.Building: var b = World.GetBuilding(id); if (b != null) return TerrainBuilder.Ground(b.pos); break;
                case PickKind.Prop: var p = World.GetProp(id); if (p != null) return TerrainBuilder.Ground(p.pos); break;
            }
            return Vector3.zero;
        }

        public bool CitizenVisible(int id) => _citizens.TryGetValue(id, out var v) && v && v.Visible;

        /// <summary>Nearest visible villager to a screen point that passes the filter, within a pixel radius.</summary>
        public bool SnapCitizen(Camera cam, Vector2 screen, float radiusPx, Func<Citizen, bool> valid, out int id, out Vector3 at)
        {
            id = -1; at = default; float best = radiusPx;
            foreach (var kv in _citizens)
            {
                if (!kv.Value || !kv.Value.Visible) continue;
                var c = World.GetCitizen(kv.Key);
                if (c == null || !valid(c)) continue;
                var wp = kv.Value.transform.position + Vector3.up * 0.8f * kv.Value.transform.localScale.y;
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z < 0) continue;
                float d = Vector2.Distance(screen, new Vector2(sp.x, sp.y));
                if (d < best) { best = d; id = kv.Key; at = kv.Value.transform.position; }
            }
            return id >= 0;
        }

        /// <summary>Screen-space picking: nearest villager / prop within reach, else the building or ground under the cursor.</summary>
        public PickResult Pick(Camera cam, Vector2 screen, double t, bool propsFirst, float radiusPx)
        {
            var best = new PickResult { kind = PickKind.None, screenDistance = float.MaxValue };
            void Consider(PickKind k, int id, Vector3 world, float bias)
            {
                var sp = cam.WorldToScreenPoint(world);
                if (sp.z < 0) return;
                float d = Vector2.Distance(screen, new Vector2(sp.x, sp.y)) - bias;
                if (d < radiusPx && d < best.screenDistance) best = new PickResult { kind = k, id = id, point = world, screenDistance = d };
            }
            foreach (var kv in _citizens)
                if (kv.Value && kv.Value.Visible) Consider(PickKind.Citizen, kv.Key, kv.Value.transform.position + Vector3.up * 0.8f * kv.Value.transform.localScale.y, propsFirst ? -6 : 4);
            foreach (var p in World.Props)
            {
                if (!p.active || p.type == PropType.CrowNest) continue;
                var pos = p.type == PropType.Cart ? World.CartPosition(t) : p.pos;
                float lift = p.type == PropType.Lantern ? 2.4f : 0.3f;
                if (p.type == PropType.Lantern && p.state != 0) lift = 0.2f;
                Consider(PickKind.Prop, p.id, TerrainBuilder.Ground(pos) + Vector3.up * lift, propsFirst ? 10 : 2);
            }
            if (best.kind != PickKind.None) return best;
            if (TerrainBuilder.Raycast(cam.ScreenPointToRay(screen), out var hit))
            {
                var hp = new Vec2(hit.x, hit.z);
                foreach (var b in World.Buildings)
                {
                    if (b.destroyed && b.buildProgress <= 0) continue;
                    var local = hp - b.pos;
                    float cs = Mathf.Cos(-b.rotation), sn = Mathf.Sin(-b.rotation);
                    float lx = local.x * cs - local.z * sn, lz = local.x * sn + local.z * cs;
                    if (Mathf.Abs(lx) < b.size.x * 0.6f + 0.5f && Mathf.Abs(lz) < b.size.z * 0.6f + 0.5f)
                        return new PickResult { kind = PickKind.Building, id = b.id, point = hit };
                }
                return new PickResult { kind = PickKind.Ground, id = -1, point = hit };
            }
            return best;
        }

        public void SetQuality(float q)
        {
            Quality = q;
            if (Foliage != null) Foliage.DrawGrass = q >= 0.5f;
        }

        // ==================================================================== nested views

        sealed class BuildingView
        {
            readonly Building _b;
            readonly GameObject _go;
            readonly MeshFilter _mf;
            readonly MeshRenderer _mr;
            Transform _spinner;
            GameObject _scaffold, _ruin;
            ParticleSystem _smoke;
            Vector3 _chimney;
            string _key = "";
            float _height;
            static Material[] _mats;

            public BuildingView(Building b, Transform parent)
            {
                _b = b;
                _go = new GameObject($"B_{b.type}_{b.id}");
                _go.transform.SetParent(parent, false);
                float y = float.MaxValue;
                float hx = b.size.x * 0.45f, hz = b.size.z * 0.45f;
                float cs = Mathf.Cos(b.rotation), sn = Mathf.Sin(b.rotation);
                foreach (var (dx, dz) in new[] { (0f, 0f), (hx, hz), (-hx, hz), (hx, -hz), (-hx, -hz) })
                    y = Mathf.Min(y, TerrainBuilder.Height(b.pos.x + dx * cs - dz * sn, b.pos.z + dx * sn + dz * cs));
                _go.transform.position = new Vector3(b.pos.x, y - 0.05f, b.pos.z);
                _go.transform.rotation = Quaternion.Euler(0, 90 - b.rotation * Mathf.Rad2Deg, 0);
                _mf = _go.AddComponent<MeshFilter>();
                _mr = _go.AddComponent<MeshRenderer>();
                _mats ??= new[]
                {
                    Mats.Stylized("building", rim: 0.18f, wrap: 0.22f, gloss: 0.04f),
                    Mats.Emissive("windows", Palette.WindowGlow * 2.6f, 1f),
                    Mats.Emissive("hot", new Color(2.6f, 1.0f, 0.3f), 0f),
                };
                _mr.sharedMaterials = _mats;
            }

            public void Tick(World w, double t, float dt, float night)
            {
                var b = _b;
                bool hidden = b.destroyed && b.type != BuildingType.Haystack;
                bool ash = b.destroyed && b.type == BuildingType.Haystack;
                int progressBucket = b.buildProgress >= 1 ? 10 : Mathf.FloorToInt(b.buildProgress * 10);
                string key = $"{b.tier}|{hidden}|{ash}|{progressBucket}";
                if (key != _key)
                {
                    _key = key;
                    var r = BuildingFactory.Build(b);
                    _mf.sharedMesh = r.mesh;
                    _height = r.height;
                    _chimney = r.chimney;
                    if (_spinner) UnityEngine.Object.Destroy(_spinner.gameObject);
                    if (r.spinner != null)
                    {
                        var sg = new GameObject("Spinner");
                        sg.transform.SetParent(_go.transform, false);
                        sg.transform.localPosition = r.spinnerPos;
                        sg.AddComponent<MeshFilter>().sharedMesh = r.spinner;
                        sg.AddComponent<MeshRenderer>().sharedMaterial = _mats[0];
                        _spinner = sg.transform;
                        _spinner.gameObject.name = r.spinnerAxis == Vector3.right ? "WheelX" : "SailsZ";
                    }
                    if (_smoke == null && r.chimney != Vector3.zero)
                    {
                        _smoke = Fx.Make(_go.transform, "Chimney", Fx.ChimneyCfg, r.chimney);
                        Fx.SetRate(_smoke, 0);
                    }
                    bool building = b.buildProgress < 1 && !b.destroyed;
                    _mr.enabled = !hidden && !ash;
                    _go.transform.localScale = new Vector3(1, building ? Mathf.Max(0.12f, b.buildProgress) : 1, 1);
                    if (_scaffold) UnityEngine.Object.Destroy(_scaffold);
                    if (building)
                    {
                        _scaffold = new GameObject("Scaffold");
                        _scaffold.transform.SetParent(_go.transform.parent, false);
                        _scaffold.transform.SetPositionAndRotation(_go.transform.position, _go.transform.rotation);
                        _scaffold.AddComponent<MeshFilter>().sharedMesh = BuildingFactory.Scaffold(b, Mathf.Max(3, _height * 0.8f));
                        _scaffold.AddComponent<MeshRenderer>().sharedMaterial = _mats[0];
                    }
                    if (_ruin) UnityEngine.Object.Destroy(_ruin);
                    if (ash)
                    {
                        _ruin = new GameObject("Ash");
                        _ruin.transform.SetParent(_go.transform.parent, false);
                        _ruin.transform.SetPositionAndRotation(_go.transform.position, _go.transform.rotation);
                        _ruin.AddComponent<MeshFilter>().sharedMesh = BuildingFactory.Ruin(b);
                        _ruin.AddComponent<MeshRenderer>().sharedMaterial = _mats[0];
                    }
                    if (hidden && _smoke) Fx.SetRate(_smoke, 0);
                }
                if (!b.Complete) { if (_smoke) Fx.SetRate(_smoke, 0); return; }

                if (_spinner)
                {
                    float speed = b.type == BuildingType.Mill ? 40 : 25 * (0.4f + w.WindStrength);
                    _spinner.Rotate(_spinner.gameObject.name == "WheelX" ? Vector3.right : Vector3.forward, speed * dt, Space.Self);
                }
                if (_smoke)
                {
                    double h = SimTime.HourOfDay(t);
                    bool winter = SimTime.SeasonOf((long)t) == Season.Winter;
                    bool on = b.type switch
                    {
                        BuildingType.Forge => h >= 8 && h < 17,
                        BuildingType.Tavern => h >= 10 && h < 23,
                        _ => (h >= 6 && h < 8.5) || (h >= 11 && h < 13) || (h >= 17 && h < 22.5) || (winter && h >= 6 && h < 22.5)
                    };
                    // a lit hearth smokes properly; an idle one still breathes a thin wisp by day
                    Fx.SetRate(_smoke, on ? (b.type == BuildingType.Forge ? 8 : 3.6f) : (h >= 7 && h < 21 ? 0.6f : 0));
                }
            }
        }

        sealed class PropView
        {
            readonly GameObject _go;
            readonly Light _light;
            readonly ParticleSystem _glint;
            readonly Transform _wheelA, _wheelB;
            float _spin;
            static Mesh _pebble, _lantern, _cart, _wheel;

            public PropView(Prop p, Transform parent)
            {
                _go = new GameObject($"P_{p.type}_{p.id}");
                _go.transform.SetParent(parent, false);
                var mf = _go.AddComponent<MeshFilter>();
                var mr = _go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Mats.Stylized("props", rim: 0.3f);
                switch (p.type)
                {
                    case PropType.Pebble:
                        if (_pebble == null) { var k = new MeshKit(); k.Blob(new Vector3(0, 0.12f, 0), new Vector3(0.3f, 0.2f, 0.26f), new Color(0.62f, 0.6f, 0.58f), 11, 0.25f, 1); _pebble = k.ToMesh("Pebble"); }
                        mf.sharedMesh = _pebble;
                        break;
                    case PropType.Lantern:
                        if (_lantern == null)
                        {
                            var k = new MeshKit();
                            k.Box(new Vector3(0, 0, 0), new Vector3(0.32f, 0.42f, 0.32f), Palette.Iron);
                            k.Sub(1);
                            k.Box(new Vector3(0, 0, 0), new Vector3(0.26f, 0.32f, 0.36f), Palette.WindowGlow);
                            k.Box(new Vector3(0, 0, 0), new Vector3(0.36f, 0.32f, 0.26f), Palette.WindowGlow);
                            k.Sub(0);
                            k.Pyramid(new Vector3(0, 0.21f, 0), 0.38f, 0.22f, Palette.Iron);
                            _lantern = k.ToMesh("Lantern");
                        }
                        mf.sharedMesh = _lantern;
                        mr.sharedMaterials = new[] { Mats.Stylized("props", rim: 0.3f), Mats.Emissive("lanternGlow", Palette.WindowGlow * 3f, 1f) };
                        _light = Fx.PointLight(_go.transform, new Color(1f, 0.7f, 0.35f), 9, 0, Vector3.zero);
                        break;
                    case PropType.Cart:
                        if (_cart == null)
                        {
                            var k = new MeshKit();
                            k.Box(new Vector3(0, 0.75f, 0), new Vector3(1.5f, 0.15f, 2.6f), Palette.Timber * 1.3f);
                            k.Box(new Vector3(0.7f, 1.05f, 0), new Vector3(0.1f, 0.5f, 2.6f), Palette.Timber * 1.2f);
                            k.Box(new Vector3(-0.7f, 1.05f, 0), new Vector3(0.1f, 0.5f, 2.6f), Palette.Timber * 1.2f);
                            k.Blob(new Vector3(0, 1.35f, 0), new Vector3(0.75f, 0.55f, 1.25f), Palette.Thatch, 4, 0.15f, 1);
                            k.Box(new Vector3(0, 0.6f, 1.9f), new Vector3(0.08f, 0.08f, 1.6f), Palette.Timber);
                            _cart = k.ToMesh("Cart");
                            var wk = new MeshKit();
                            wk.M = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one);
                            wk.Cylinder(new Vector3(0, -0.06f, 0), 0.5f, 0.12f, 10, Palette.Timber);
                            _wheel = wk.ToMesh("Wheel");
                        }
                        mf.sharedMesh = _cart;
                        _wheelA = Wheel(new Vector3(0.82f, 0.5f, -0.6f));
                        _wheelB = Wheel(new Vector3(-0.82f, 0.5f, -0.6f));
                        break;
                    case PropType.GroundItem:
                        mf.sharedMesh = ItemMesh(p.item);
                        var gc = Fx.GlimmerCfg; gc.rate = 4; gc.radius = 0.15f; gc.size0 = 0.06f; gc.size1 = 0.14f; gc.c0 = new Color(1.8f, 1.6f, 1.0f, 1);
                        _glint = Fx.Make(_go.transform, "Glint", gc, Vector3.up * 0.25f);
                        break;
                }
            }

            Transform Wheel(Vector3 at)
            {
                var g = new GameObject("Wheel");
                g.transform.SetParent(_go.transform, false);
                g.transform.localPosition = at;
                g.AddComponent<MeshFilter>().sharedMesh = _wheel;
                g.AddComponent<MeshRenderer>().sharedMaterial = Mats.Stylized("props", rim: 0.3f);
                return g.transform;
            }

            static Mesh ItemMesh(ItemType t)
            {
                var k = new MeshKit();
                switch (t)
                {
                    case ItemType.Gear:
                        k.Cylinder(Vector3.zero, 0.22f, 0.06f, 10, Palette.Brass);
                        for (int i = 0; i < 10; i++) { float a = i * Mathf.PI / 5; k.Box(new Vector3(Mathf.Cos(a) * 0.26f, 0.03f, Mathf.Sin(a) * 0.26f), new Vector3(0.07f, 0.06f, 0.07f), Palette.Brass); }
                        break;
                    case ItemType.Lens: k.Cylinder(Vector3.zero, 0.2f, 0.04f, 12, new Color(0.75f, 0.92f, 1f)); k.Cylinder(Vector3.zero, 0.23f, 0.03f, 12, Palette.Brass); break;
                    case ItemType.Book: k.Box(new Vector3(0, 0.05f, 0), new Vector3(0.36f, 0.1f, 0.48f), new Color(0.45f, 0.2f, 0.18f)); k.Box(new Vector3(0.01f, 0.05f, 0), new Vector3(0.34f, 0.08f, 0.44f), Palette.Plaster); break;
                    case ItemType.Coin: k.Cylinder(Vector3.zero, 0.14f, 0.03f, 10, new Color(0.85f, 0.87f, 0.9f)); break;
                    case ItemType.Basket:
                        k.Cylinder(Vector3.zero, 0.34f, 0.26f, 10, new Color(0.62f, 0.44f, 0.22f));
                        for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3; k.Box(new Vector3(Mathf.Cos(a) * 0.16f, 0.3f, Mathf.Sin(a) * 0.16f), new Vector3(0.14f, 0.12f, 0.14f), new Color(0.78f, 0.12f, 0.1f)); }
                        for (int i = 0; i < 4; i++) { float a = i * 1.7f + 0.4f; k.Box(new Vector3(Mathf.Cos(a) * 0.75f, 0.06f, Mathf.Sin(a) * 0.6f), new Vector3(0.13f, 0.12f, 0.13f), new Color(0.82f, 0.16f, 0.1f)); }
                        break;
                    case ItemType.Key: k.Box(new Vector3(0, 0.03f, 0), new Vector3(0.06f, 0.05f, 0.4f), Palette.Iron); k.Cylinder(new Vector3(0, 0, 0.22f), 0.08f, 0.05f, 8, Palette.Iron); break;
                    default: k.Box(new Vector3(0, 0.03f, 0), new Vector3(0.06f, 0.02f, 0.4f), new Color(0.1f, 0.1f, 0.12f)); break;
                }
                return k.ToMesh("Item");
            }

            public void Tick(World w, Prop p, double t, float dt, float night, bool selected)
            {
                bool show = p.active && !(p.type == PropType.Lantern && p.state == 2);
                if (_go.activeSelf != show) _go.SetActive(show);
                if (!show) return;
                var g = TerrainBuilder.Ground(p.type == PropType.Cart ? w.CartPosition(t) : p.pos);
                switch (p.type)
                {
                    case PropType.Pebble:
                        _go.transform.position = g;
                        _go.transform.rotation = Quaternion.Euler(0, p.id * 47, 0);
                        _go.transform.localScale = Vector3.one * (selected ? 1.25f : 1f);
                        break;
                    case PropType.Lantern:
                        if (p.state == 0) { _go.transform.position = g + Vector3.up * 2.35f; _go.transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * 3, 0, 0); }
                        else { _go.transform.position = g + new Vector3(0.3f, 0.15f, 0.2f); _go.transform.rotation = Quaternion.Euler(80, 30, 0); }
                        double h = SimTime.HourOfDay(t);
                        bool lit = (h >= 18 || h < 1) && p.state == 0;
                        if (_light) _light.intensity = lit ? 2.2f + Mathf.Sin(Time.time * 9) * 0.15f : 0;
                        break;
                    case PropType.Cart:
                    {
                        _go.transform.position = g;
                        var dir = new Vector3(VillageLayout.CartEnd.x - VillageLayout.CartStart.x, 0, VillageLayout.CartEnd.z - VillageLayout.CartStart.z);
                        float tilt = p.state == 2 ? 14 : 0;
                        _go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(-tilt, 0, tilt * 0.5f);
                        if (w.Cart != null && !w.Cart.done) _spin += dt * 600;
                        if (_wheelA) _wheelA.localRotation = Quaternion.Euler(_spin, 0, 0);
                        if (_wheelB) _wheelB.localRotation = Quaternion.Euler(_spin, 0, 0);
                        break;
                    }
                    case PropType.GroundItem:
                        _go.transform.position = g + Vector3.up * 0.04f;
                        _go.transform.rotation = Quaternion.Euler(0, (float)(t * 0.01) % 360, 0);
                        break;
                }
            }
        }

        sealed class FireView
        {
            readonly GameObject _root;
            readonly ParticleSystem _flames, _smoke, _embers;
            readonly Light _light;
            float _fade = 1;
            public bool Done => _fade <= 0;

            public FireView(Fire f, Building b, Transform parent)
            {
                _root = new GameObject("Fire" + f.id);
                _root.transform.SetParent(parent, false);
                float h = b != null && b.type == BuildingType.Haystack ? 1.5f : 2.5f;
                _root.transform.position = TerrainBuilder.Ground(f.pos) + Vector3.up * h;
                float size = b != null ? Mathf.Max(b.size.x, b.size.z) * 0.35f : 1.5f;
                var fc = Fx.FlameCfg; fc.radius = size; _flames = Fx.Make(_root.transform, "Flames", fc);
                var sc = Fx.SmokeCfg; sc.radius = size; _smoke = Fx.Make(_root.transform, "Smoke", sc, Vector3.up * 2);
                var ec = Fx.EmberCfg; ec.radius = size; _embers = Fx.Make(_root.transform, "Embers", ec);
                _light = Fx.PointLight(_root.transform, new Color(1f, 0.55f, 0.2f), 30, 0, Vector3.up);
                // the moment it catches: a gout of embers
                _embers.Emit(90);
            }

            public void Tick(Fire f, float dt)
            {
                float k = f.active ? Mathf.Clamp01(f.intensity + 0.15f) : 0;
                if (!f.active) _fade -= dt * 0.3f;
                Fx.SetRate(_flames, 110 * k);
                Fx.SetRate(_embers, 55 * k);
                Fx.SetRate(_smoke, f.active ? 6 + 14 * k : 4 * _fade);
                _light.intensity = (5f + Mathf.PerlinNoise(Time.time * 6, 0) * 3.5f) * k; // the fire lights up the whole lane
            }

            public void Dispose() { if (_root) UnityEngine.Object.Destroy(_root); }
        }
    }
}
