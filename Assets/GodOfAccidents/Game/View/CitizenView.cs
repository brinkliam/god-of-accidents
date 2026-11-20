using GOA.Sim;
using UnityEngine;

namespace GOA.Game
{
    /// <summary>A tiny hand-built villager: torso, head and hat in one mesh, with limbs on pivots for procedural animation.</summary>
    public sealed class CitizenView : MonoBehaviour
    {
        public int citizenId;
        Transform _body, _legL, _legR, _armL, _armR, _carry, _pack;
        /// <summary>Shared puff emitter for footsteps (set by the world view).</summary>
        public static ParticleSystem StepDust;
        public static Vector3 CameraPos;
        float _lastStepSign;
        float _phase, _yaw;
        Vector3 _lastPos;
        bool _hasLast;
        Occupation _builtFor = (Occupation)(-1);
        int _builtAge = -1;
        float _scale = 1f;
        MeshRenderer[] _renderers;
        public Vector3 HeadPosition => transform.position + Vector3.up * 1.45f * _scale;
        public bool Visible { get; private set; }

        static Material Mat => Mats.Stylized("people", rim: 0.45f, wrap: 0.55f, gloss: 0.1f, snow: 0f);

        public static CitizenView Create(Transform parent, Citizen c, long now)
        {
            var go = new GameObject("Citizen_" + c.firstName + "_" + c.id);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<CitizenView>();
            v.citizenId = c.id;
            v.Rebuild(c, now);
            return v;
        }

        Transform Part(string name, Mesh m, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_body ? _body : transform, false);
            t.localPosition = localPos;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return t;
        }

        public void Rebuild(Citizen c, long now)
        {
            foreach (Transform ch in transform) Destroy(ch.gameObject);
            _body = null;
            int age = c.AgeYears(now);
            _builtFor = c.occupation;
            _builtAge = age < 5 ? 0 : age < 14 ? 1 : age < 60 ? 2 : 3;
            _scale = age < 5 ? 0.5f : age < 14 ? 0.62f + age * 0.02f : 1f;

            var rng = new System.Random(c.outfitSeed);
            var skin = Palette.Skin[c.skinTone % Palette.Skin.Length];
            var hair = age >= 60 ? Palette.Hair[5] : Palette.Hair[c.hairColor % Palette.Hair.Length];
            var outfit = Palette.Outfit[rng.Next(Palette.Outfit.Length)];
            var outfit2 = Palette.Outfit[rng.Next(Palette.Outfit.Length)] * 0.8f;
            var pants = Color.Lerp(new Color(0.3f, 0.25f, 0.22f), outfit2, 0.3f);
            switch (c.occupation)
            {
                case Occupation.Priest: outfit = new Color(0.88f, 0.86f, 0.8f); outfit2 = outfit; break;
                case Occupation.Smith: outfit2 = new Color(0.35f, 0.25f, 0.18f); break;
                case Occupation.Elder: outfit = new Color(0.36f, 0.22f, 0.4f); break;
                case Occupation.Merchant: outfit = new Color(0.7f, 0.2f, 0.18f); break;
            }

            // torso + head + headwear
            var k = new MeshKit();
            bool skirt = c.female && rng.NextDouble() < 0.75;
            if (skirt) k.Cylinder(new Vector3(0, 0.12f, 0), 0.27f, 0.55f, 8, outfit2, null, 0.18f);
            k.Cylinder(new Vector3(0, 0.55f, 0), 0.2f, 0.5f, 8, outfit, null, 0.17f);
            if (c.occupation == Occupation.Smith || c.occupation == Occupation.Shopkeeper || c.occupation == Occupation.Innkeeper)
                k.Box(new Vector3(0, 0.7f, 0.17f), new Vector3(0.3f, 0.5f, 0.05f), c.occupation == Occupation.Smith ? new Color(0.4f, 0.3f, 0.2f) : Palette.Plaster);
            k.Box(new Vector3(0, 1.08f, 0), new Vector3(0.12f, 0.08f, 0.12f), skin);
            k.Blob(new Vector3(0, 1.24f, 0), new Vector3(0.17f, 0.18f, 0.17f), skin, c.id, 0.04f, 1);
            // eyes
            k.Box(new Vector3(-0.06f, 1.26f, 0.155f), new Vector3(0.035f, 0.045f, 0.02f), new Color(0.12f, 0.1f, 0.1f));
            k.Box(new Vector3(0.06f, 1.26f, 0.155f), new Vector3(0.035f, 0.045f, 0.02f), new Color(0.12f, 0.1f, 0.1f));
            // hair
            k.Blob(new Vector3(0, 1.32f, -0.03f), new Vector3(0.18f, 0.12f, 0.17f), hair, c.id + 5, 0.08f, 1);
            if (c.female && age >= 8) k.Blob(new Vector3(0, 1.13f, -0.14f), new Vector3(0.12f, 0.16f, 0.07f), hair, c.id + 9, 0.08f, 0);
            switch (c.occupation)
            {
                case Occupation.Farmer:
                case Occupation.Shepherd:
                    k.Cylinder(new Vector3(0, 1.36f, 0), 0.34f, 0.03f, 10, Palette.Thatch);
                    k.Cylinder(new Vector3(0, 1.38f, 0), 0.16f, 0.12f, 10, Palette.Thatch);
                    break;
                case Occupation.Merchant:
                    k.Cylinder(new Vector3(0, 1.36f, 0), 0.32f, 0.04f, 10, new Color(0.3f, 0.15f, 0.12f));
                    k.Cylinder(new Vector3(0, 1.39f, 0), 0.17f, 0.18f, 10, new Color(0.3f, 0.15f, 0.12f));
                    k.Box(new Vector3(0.12f, 1.52f, -0.05f), new Vector3(0.03f, 0.22f, 0.03f), Palette.Cloth3);
                    break;
                case Occupation.Priest:
                    k.Blob(new Vector3(0, 1.3f, -0.03f), new Vector3(0.21f, 0.2f, 0.2f), outfit, c.id + 2, 0.02f, 1);
                    break;
                case Occupation.Tinkerer:
                    k.Box(new Vector3(0, 1.33f, 0.12f), new Vector3(0.28f, 0.06f, 0.06f), Palette.Timber);
                    k.Cylinder(new Vector3(-0.07f, 1.31f, 0.15f), 0.045f, 0.06f, 6, Palette.Brass);
                    k.Cylinder(new Vector3(0.07f, 1.31f, 0.15f), 0.045f, 0.06f, 6, Palette.Brass);
                    break;
                case Occupation.Woodcutter:
                case Occupation.Builder:
                    k.Blob(new Vector3(0, 1.36f, 0), new Vector3(0.18f, 0.09f, 0.18f), Palette.Cloth1 * 0.8f, c.id, 0.05f, 0);
                    break;
                case Occupation.Elder:
                    k.Box(new Vector3(0, 1.1f, 0.13f), new Vector3(0.14f, 0.16f, 0.06f), hair);
                    break;
            }
            var torsoMesh = k.ToMesh("Torso");

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(transform, false);
            _body = bodyGo.transform;
            _body.localScale = Vector3.one * _scale;
            bodyGo.AddComponent<MeshFilter>().sharedMesh = torsoMesh;
            var br = bodyGo.AddComponent<MeshRenderer>();
            br.sharedMaterial = Mat;

            var lk = new MeshKit();
            lk.Box(new Vector3(0, -0.24f, 0), new Vector3(0.11f, 0.48f, 0.12f), pants);
            lk.Box(new Vector3(0, -0.47f, 0.03f), new Vector3(0.12f, 0.06f, 0.18f), new Color(0.25f, 0.18f, 0.13f));
            var legMesh = lk.ToMesh("Leg");
            var ak = new MeshKit();
            ak.Box(new Vector3(0, -0.19f, 0), new Vector3(0.085f, 0.38f, 0.095f), outfit * 0.95f);
            ak.Blob(new Vector3(0, -0.41f, 0), new Vector3(0.05f, 0.05f, 0.05f), skin, 3, 0.05f, 0);
            var armMesh = ak.ToMesh("Arm");
            _legL = Part("LegL", legMesh, new Vector3(-0.08f, 0.5f, 0));
            _legR = Part("LegR", legMesh, new Vector3(0.08f, 0.5f, 0));
            _armL = Part("ArmL", armMesh, new Vector3(-0.25f, 1.0f, 0));
            _armR = Part("ArmR", armMesh, new Vector3(0.25f, 1.0f, 0));

            // tools and carried things
            var tk = new MeshKit();
            switch (c.occupation)
            {
                case Occupation.Farmer: tk.Box(new Vector3(0, -0.4f, 0.1f), new Vector3(0.04f, 0.04f, 0.9f), Palette.Timber); tk.Box(new Vector3(0, -0.46f, 0.55f), new Vector3(0.18f, 0.12f, 0.04f), Palette.Iron); break;
                case Occupation.Smith: tk.Box(new Vector3(0, -0.42f, 0.12f), new Vector3(0.04f, 0.04f, 0.3f), Palette.Timber); tk.Box(new Vector3(0, -0.42f, 0.28f), new Vector3(0.08f, 0.1f, 0.14f), Palette.Iron); break;
                case Occupation.Woodcutter: tk.Box(new Vector3(0, -0.42f, 0.2f), new Vector3(0.04f, 0.04f, 0.5f), Palette.Timber); tk.Box(new Vector3(0, -0.42f, 0.42f), new Vector3(0.03f, 0.16f, 0.14f), Palette.Iron); break;
                case Occupation.Merchant: tk.Box(new Vector3(-0.25f, 0.05f, -0.22f), new Vector3(0.38f, 0.42f, 0.24f), new Color(0.5f, 0.35f, 0.22f)); break;
                case Occupation.Elder: tk.Box(new Vector3(0, -0.2f, 0.12f), new Vector3(0.04f, 1.1f, 0.04f), Palette.Timber); break;
                case Occupation.Shepherd: tk.Box(new Vector3(0, -0.15f, 0.1f), new Vector3(0.04f, 1.2f, 0.04f), Palette.Timber); break;
                case Occupation.Fisher: tk.M = Matrix4x4.TRS(new Vector3(0, -0.4f, 0.1f), Quaternion.Euler(-35, 0, 0), Vector3.one); tk.Box(new Vector3(0, 0, 0.9f), new Vector3(0.03f, 0.03f, 1.8f), Palette.Timber); break;
            }
            if (tk.v.Count > 0)
            {
                _carry = new GameObject("Tool").transform;
                _carry.SetParent(c.occupation == Occupation.Merchant ? _body : _armR, false);
                _carry.gameObject.AddComponent<MeshFilter>().sharedMesh = tk.ToMesh("Tool");
                _carry.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Mat;
            }
            // loads carried on the road: a log bundle, a sack on the back or a basket on the hip
            if (c.occupation != Occupation.Merchant && c.occupation != Occupation.Toddler)
            {
                var pk = new MeshKit();
                if (c.occupation == Occupation.Woodcutter)
                {
                    pk.M = Matrix4x4.TRS(new Vector3(0, 1.32f, -0.05f), Quaternion.Euler(0, 0, 90), Vector3.one);
                    for (int i = 0; i < 3; i++) pk.Cylinder(new Vector3(-0.07f + i * 0.07f, -0.35f, 0.02f * i), 0.06f, 0.7f, 6, Palette.Timber * (1.1f + i * 0.08f));
                }
                else if (c.id % 2 == 0)
                    pk.Blob(new Vector3(0, 0.95f, -0.2f), new Vector3(0.2f, 0.24f, 0.13f), new Color(0.72f, 0.62f, 0.44f), c.id, 0.06f, 0);
                else
                {
                    pk.Cylinder(new Vector3(0.3f, 0.62f, 0.08f), 0.15f, 0.16f, 8, new Color(0.62f, 0.44f, 0.22f));
                    pk.Blob(new Vector3(0.3f, 0.79f, 0.08f), new Vector3(0.12f, 0.06f, 0.12f), c.id % 3 == 0 ? new Color(0.8f, 0.16f, 0.1f) : new Color(0.55f, 0.7f, 0.3f), c.id, 0.05f, 0);
                }
                _pack = new GameObject("Pack").transform;
                _pack.SetParent(_body, false);
                _pack.gameObject.AddComponent<MeshFilter>().sharedMesh = pk.ToMesh("Pack");
                _pack.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Mat;
                _pack.gameObject.SetActive(false);
            }
            _renderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        public void SetVisible(bool v)
        {
            if (Visible == v) return;
            Visible = v;
            foreach (var r in _renderers) if (r) r.enabled = v;
        }

        public void Tick(World w, Citizen c, double t, float dt, float simSpeed)
        {
            if (c.occupation != _builtFor || AgeBucket(c.AgeYears((long)t)) != _builtAge) Rebuild(c, (long)t);
            bool show = c.IsActive && c.activity != null && !w.IsIndoors(c);
            SetVisible(show);
            if (!show) { _hasLast = false; return; }

            var p2 = w.PositionAt(c, t);
            var pos = TerrainBuilder.Ground(p2.x, p2.z);
            var kind = c.activity.kind;
            if (kind == ActivityKind.Sleep) pos.y -= 0.3f;

            Vector3 vel = _hasLast && dt > 0 ? (pos - _lastPos) / dt : Vector3.zero;
            _lastPos = pos; _hasLast = true;
            float planar = new Vector2(vel.x, vel.z).magnitude;
            bool moving = kind == ActivityKind.Travel && planar > 0.02f;

            // facing
            float targetYaw = _yaw;
            if (moving) targetYaw = Mathf.Atan2(vel.x, vel.z) * Mathf.Rad2Deg;
            else if (kind == ActivityKind.Distracted || kind == ActivityKind.FightFire)
            {
                var d = w.GetCitizen(c.id);
                targetYaw = _yaw + Mathf.Sin((float)t * 0.01f) * 0.2f;
            }
            _yaw = Mathf.LerpAngle(_yaw, targetYaw, 1 - Mathf.Exp(-dt * 10));

            float animRate = Mathf.Clamp(simSpeed / 120f, 0.6f, 4f);
            _phase += dt * (moving ? 9f * animRate : 3f);
            float s = Mathf.Sin(_phase);
            float bob = 0;
            float legA = 0, armL = 0, armR = 0, tilt = 0, armSideL = 0, armSideR = 0;
            switch (kind)
            {
                case ActivityKind.Travel:
                    legA = s * 32; armL = -s * 26; armR = s * 26; bob = Mathf.Abs(s) * 0.05f; break;
                case ActivityKind.Work:
                case ActivityKind.Build:
                    if (c.occupation == Occupation.Farmer || c.occupation == Occupation.Builder || c.occupation == Occupation.Woodcutter || c.occupation == Occupation.Smith)
                    { float ws = Mathf.Sin(_phase * 1.6f); armR = -60 - ws * 50; armL = -40 - ws * 40; tilt = 12 + ws * 6; }
                    else { armR = -20 + s * 10; tilt = 4; }
                    break;
                case ActivityKind.Play:
                case ActivityKind.Festival:
                    bob = Mathf.Abs(Mathf.Sin(_phase * 2.2f)) * 0.18f; armL = -120 + s * 40; armR = -120 - s * 40; armSideL = -15; armSideR = 15; break;
                case ActivityKind.Distracted:
                    tilt = -16; armL = -50 + s * 5; armR = -70 - s * 8; bob = Mathf.Sin(_phase * 0.5f) * 0.02f; break;
                case ActivityKind.FightFire:
                    { float f = Mathf.Sin(_phase * 3f); armL = -150 + f * 40; armR = -150 - f * 40; tilt = -6; legA = f * 10; }
                    break;
                case ActivityKind.Pray:
                    armL = -55; armR = -55; armSideL = 18; armSideR = -18; tilt = 8; break;
                case ActivityKind.Socialize:
                case ActivityKind.Trade:
                    armR = -30 - Mathf.Max(0, s) * 50; tilt = Mathf.Sin(_phase * 0.4f) * 3; break;
                case ActivityKind.Fetch:
                    armL = -10; armR = -10; armSideL = -10; armSideR = 10; break;
                default:
                    armL = s * 3; armR = -s * 3; bob = Mathf.Sin(_phase * 0.6f) * 0.01f; break;
            }
            if (c.AgeYears((long)t) > 64) tilt += 10;

            if (_pack)
            {
                bool carrying = kind == ActivityKind.Travel && c.AgeYears((long)t) >= 14
                    && (c.cargoItem != ItemType.None || c.occupation == Occupation.Woodcutter || (c.id + SimTime.DayIndex((long)t)) % 3 != 0);
                if (_pack.gameObject.activeSelf != carrying) _pack.gameObject.SetActive(carrying);
            }
            // a puff of dust at each footfall, only near the camera and at watchable speeds
            if (kind == ActivityKind.Travel && StepDust && simSpeed <= 700 && Visible)
            {
                float sign = Mathf.Sign(s);
                if (sign != _lastStepSign)
                {
                    _lastStepSign = sign;
                    if ((pos - CameraPos).sqrMagnitude < 55 * 55) { StepDust.transform.position = pos + Vector3.up * 0.05f; StepDust.Emit(1); }
                }
            }
            transform.position = pos + Vector3.up * bob * _scale;
            transform.rotation = Quaternion.Euler(0, _yaw, 0);
            _body.localRotation = Quaternion.Euler(tilt, 0, 0);
            _legL.localRotation = Quaternion.Euler(legA, 0, 0);
            _legR.localRotation = Quaternion.Euler(-legA, 0, 0);
            _armL.localRotation = Quaternion.Euler(armL, 0, armSideL);
            _armR.localRotation = Quaternion.Euler(armR, 0, armSideR);
        }

        static int AgeBucket(int age) => age < 5 ? 0 : age < 14 ? 1 : age < 60 ? 2 : 3;
    }
}
