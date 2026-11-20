using System;
using GOA.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = GOA.Sim.PlayerInput;

namespace GOA.Game
{
    /// <summary>
    /// Turns pointer gestures into divine interventions. Each power has its own gesture:
    /// Nudge = drag a small object (or tap the lantern / cart chock), Gust = press and drag a direction,
    /// Distract = tap a villager, Drop = tap a spot for the crow. With no power selected, taps inspect.
    /// </summary>
    public sealed class PowerController : MonoBehaviour
    {
        public PowerType? Active;
        public GameSession Session;
        public CameraRig Rig;
        public WorldView View;
        public Func<Vector2, bool> IsOverUI = _ => false;
        public Func<bool> PressBeganOnUI = () => false;
        float _blockUntil;
        public bool Captured { get; private set; }
        public event Action<PickResult> OnSelect;
        public event Action<string> OnHint;

        Vector2 _downScreen;
        Vector3 _downWorld;
        int _dragProp = -1;
        bool _pressed;
        LineRenderer _line;
        Transform _ghost;
        Transform _hover;
        Material _lineMat, _hoverMat;

        public void Init()
        {
            var go = new GameObject("PowerPreview");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _lineMat = Mats.NewGlow(new Color(1.6f, 1.2f, 0.5f), 2.5f, 6);
            _line.material = _lineMat;
            _line.widthMultiplier = 0.35f;
            _line.positionCount = 0;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var g = new GameObject("Ghost");
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = Fx.Ring(0.6f, 1f, 40);
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mats.NewGlow(new Color(1.6f, 1.2f, 0.5f), 3, 10);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ghost = g.transform; _ghost.gameObject.SetActive(false);
            var h = new GameObject("Hover");
            h.transform.SetParent(transform, false);
            h.AddComponent<MeshFilter>().sharedMesh = Fx.Ring(0.75f, 1f, 40);
            var hr = h.AddComponent<MeshRenderer>();
            _hoverMat = Mats.NewGlow(new Color(0.6f, 1.1f, 1.6f), 1.5f, 8);
            hr.sharedMaterial = _hoverMat;
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _hover = h.transform; _hover.gameObject.SetActive(false);
        }

        public void Select(PowerType? p)
        {
            Active = Active == p ? null : p;
            Cancel();
            if (Active.HasValue) OnHint?.Invoke(HintFor(Active.Value));
        }

        public static string HintFor(PowerType p) => p switch
        {
            PowerType.Nudge => "Tap a pebble or trinket to roll it into someone's path (drag to aim it yourself) - or tap the tavern lantern or the hay cart's wheel chock.",
            PowerType.Gust => "Press where the wind should start, then drag the direction it blows.",
            PowerType.Distract => "Tap a villager who is outdoors. A glimmer will stop them in their tracks.",
            PowerType.Drop => "Tap a spot. The crow will fly from the old oak and drop whatever it carries.",
            _ => ""
        };

        /// <summary>Touch targets in screen pixels, grown on large or high-density screens (about 44pt minimum).</summary>
        static float Px(float r) => r * Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 640f);

        Texture2D[] _cursors;
        PowerType? _cursorFor = (PowerType)(-1);

        static Texture2D MakeCursor(Color c)
        {
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f));
                    float ring = Mathf.Clamp01(1.5f - Mathf.Abs(d - 11.5f));
                    float halo = Mathf.Clamp01(1.5f - Mathf.Abs(d - 13.5f)) * 0.6f;
                    float dot = Mathf.Clamp01(3f - d);
                    float a = Mathf.Max(ring, dot);
                    var col = Color.Lerp(new Color(0, 0, 0, halo), new Color(c.r, c.g, c.b, 1), a);
                    px[y * S + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        void UpdateCursor()
        {
            var want = Session != null && Session.AcceptsInput ? Active : null;
            if (want == _cursorFor) return;
            _cursorFor = want;
            if (_cursors == null)
                _cursors = new[] { MakeCursor(new Color(1f, 0.85f, 0.4f)), MakeCursor(new Color(0.6f, 0.9f, 1f)), MakeCursor(new Color(0.85f, 0.6f, 1f)), MakeCursor(new Color(0.75f, 0.75f, 0.8f)) };
            if (want.HasValue && (int)want.Value < _cursors.Length) Cursor.SetCursor(_cursors[(int)want.Value], new Vector2(16, 16), CursorMode.Auto);
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        void Cancel()
        {
            _pressed = false; Captured = false; _dragProp = -1;
            _line.positionCount = 0;
            _ghost.gameObject.SetActive(false);
        }

        bool PointerState(out Vector2 pos, out bool down, out bool held, out bool up)
        {
            pos = default; down = held = up = false;
            var ts = Touchscreen.current;
            if (ts != null)
            {
                int pressed = 0;
                foreach (var t in ts.touches) if (t.press.isPressed) pressed++;
                if (pressed >= 2) { if (_pressed) Cancel(); return false; }
                var p = ts.primaryTouch;
                if (p.press.isPressed || p.press.wasReleasedThisFrame)
                {
                    pos = p.position.ReadValue();
                    down = p.press.wasPressedThisFrame; held = p.press.isPressed; up = p.press.wasReleasedThisFrame;
                    return true;
                }
            }
            var m = Mouse.current;
            if (m == null) return false;
            pos = m.position.ReadValue();
            down = m.leftButton.wasPressedThisFrame; held = m.leftButton.isPressed; up = m.leftButton.wasReleasedThisFrame;
            return true;
        }

        void Update()
        {
            if (View) View.Armed = Session != null && Session.AcceptsInput ? Active : null;
            UpdateCursor();
            if (Session == null || Session.Timeline == null || !Session.AcceptsInput) { Cancel(); _hover.gameObject.SetActive(false); _blockUntil = Time.unscaledTime + 0.25f; return; }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) Select(PowerType.Nudge);
                if (kb.digit2Key.wasPressedThisFrame) Select(PowerType.Gust);
                if (kb.digit3Key.wasPressedThisFrame) Select(PowerType.Distract);
                if (kb.digit4Key.wasPressedThisFrame) Select(PowerType.Drop);
                if (kb.escapeKey.wasPressedThisFrame && Active.HasValue) Select(null);
            }
            if (!PointerState(out var pos, out bool down, out bool held, out bool up)) { HasSnap = false; return; }
            var cam = Rig.Cam;
            double t = Session.DisplayTime;
            UpdateHover(cam, pos, t);
            // on touch there is no hover: the snapped target shows while the finger is down
            bool touch = Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed;
            UpdateSnap(cam, pos, !touch || held);

            if (down)
            {
                // a press on a button (or in the instant a dialog closes) never reaches the world behind it
                if (IsOverUI(pos) || PressBeganOnUI() || Time.unscaledTime < _blockUntil) return;
                _pressed = true;
                _downScreen = pos;
                TerrainBuilder.Raycast(cam.ScreenPointToRay(pos), out _downWorld);
                if (Active == PowerType.Nudge)
                {
                    var pick = View.Pick(cam, pos, t, true, Px(64));
                    if (pick.kind == PickKind.Prop)
                    {
                        var p = View.World.GetProp(pick.id);
                        if (p != null && (p.type == PropType.Pebble || p.type == PropType.GroundItem)) { _dragProp = p.id; Captured = true; }
                        else if (p != null) { Captured = true; }
                    }
                }
                else if (Active == PowerType.Gust) Captured = true;
            }

            if (_pressed && held)
            {
                if (Active == PowerType.Nudge && _dragProp >= 0) PreviewNudge(cam, pos);
                else if (Active == PowerType.Gust) PreviewGust(cam, pos);
            }

            if (_pressed && up)
            {
                bool tap = Vector2.Distance(pos, _downScreen) < Px(16);
                Release(cam, pos, tap, t);
                Cancel();
            }
        }

        void UpdateHover(Camera cam, Vector2 pos, double t)
        {
            if (Active == PowerType.Distract || Active == PowerType.Drop) return; // snapped targeting draws the ring
            _hoverMat.SetColor("_Color", new Color(0.6f, 1.1f, 1.6f));
            if (IsOverUI(pos) || _pressed) { _hover.gameObject.SetActive(false); return; }
            var pick = View.Pick(cam, pos, t, Active == PowerType.Nudge, Px(56));
            bool valid = pick.kind == PickKind.Citizen && (Active == null || Active == PowerType.Distract)
                      || pick.kind == PickKind.Prop && (Active == null || Active == PowerType.Nudge)
                      || pick.kind == PickKind.Building && Active == null;
            _hover.gameObject.SetActive(valid);
            if (valid)
            {
                float r = pick.kind == PickKind.Building ? 5 : 1.1f;
                var p = pick.kind == PickKind.Building ? TerrainBuilder.Ground(View.World.GetBuilding(pick.id).pos) : TerrainBuilder.Ground(pick.point.x, pick.point.z);
                _hover.position = p + Vector3.up * 0.1f;
                _hover.localScale = new Vector3(r, 1, r);
            }
        }


        // ------------------------------------------------------------------ snapped targeting (Distract, Drop)

        /// <summary>The target a tap would hit right now, shown before confirming.</summary>
        public bool HasSnap { get; private set; }
        public Vector3 SnapWorld { get; private set; }
        public string SnapLabel { get; private set; } = "";
        int _snapId = -1;
        Vector3 _snapAt;

        bool SnapTarget(Camera cam, Vector2 pos, out int id, out Vector3 at, out string label)
        {
            var w = View.World;
            id = -1; at = default; label = "";
            if (Active == PowerType.Distract)
            {
                if (!View.SnapCitizen(cam, pos, Px(110), c => w.IsAwakeOutside(c) && c.activity.kind != ActivityKind.Distracted, out id, out at)) return false;
                label = "Distract " + w.GetCitizen(id).Name;
                return true;
            }
            if (Active == PowerType.Drop)
            {
                // drop at a villager's feet if one is near the pointer, so they will find it; else on the ground
                if (View.SnapCitizen(cam, pos, Px(90), c => w.IsAwakeOutside(c), out id, out var feet))
                {
                    at = feet + (cam.transform.position - feet).normalized * 1.2f; at.y = feet.y;
                    label = "Drop beside " + w.GetCitizen(id).firstName;
                }
                else if (TerrainBuilder.Raycast(cam.ScreenPointToRay(pos), out at)) label = "Drop here";
                else return false;
                if (Vector2.Distance(new Vector2(at.x, at.z), new Vector2(VillageLayout.OakPos.x, VillageLayout.OakPos.z)) > 140) { label = "Too far from the crow's oak"; return true; }
                return true;
            }
            return false;
        }

        void UpdateSnap(Camera cam, Vector2 pos, bool pointerActive)
        {
            HasSnap = false;
            if (!pointerActive || IsOverUI(pos) || (Active != PowerType.Distract && Active != PowerType.Drop)) { _snapId = -1; return; }
            if (!SnapTarget(cam, pos, out _snapId, out _snapAt, out var label)) { _snapId = -1; return; }
            HasSnap = true; SnapWorld = _snapAt; SnapLabel = label;
            _hover.gameObject.SetActive(true);
            _hover.position = _snapAt + Vector3.up * 0.1f;
            float r = Active == PowerType.Drop ? 1.6f : 1.3f;
            r *= 1 + Mathf.Sin(Time.unscaledTime * 7f) * 0.06f;
            _hover.localScale = new Vector3(r, 1, r);
            _hoverMat.SetColor("_Color", Active == PowerType.Distract ? new Color(1.5f, 0.85f, 1.9f) : new Color(1.6f, 1.5f, 1.6f));
        }
        Vector3 ClampNudge(Prop p, Vector3 world)
        {
            var from = new Vector3(p.pos.x, 0, p.pos.z);
            var to = new Vector3(world.x, 0, world.z);
            var d = to - from;
            if (d.magnitude > World.NudgeRange) to = from + d.normalized * World.NudgeRange;
            return TerrainBuilder.Ground(to.x, to.z);
        }

        void PreviewNudge(Camera cam, Vector2 pos)
        {
            var p = View.World.GetProp(_dragProp);
            if (p == null || !TerrainBuilder.Raycast(cam.ScreenPointToRay(pos), out var hit)) return;
            var to = ClampNudge(p, hit);
            var from = TerrainBuilder.Ground(p.pos);
            _line.positionCount = 2;
            _line.SetPosition(0, from + Vector3.up * 0.3f);
            _line.SetPosition(1, to + Vector3.up * 0.3f);
            _ghost.gameObject.SetActive(true);
            _ghost.position = to + Vector3.up * 0.08f;
            _ghost.localScale = Vector3.one * 0.9f;
        }

        void PreviewGust(Camera cam, Vector2 pos)
        {
            if (!TerrainBuilder.Raycast(cam.ScreenPointToRay(pos), out var hit)) return;
            var dir = hit - _downWorld; dir.y = 0;
            if (dir.magnitude < 0.5f) { _line.positionCount = 0; return; }
            var a = _downWorld + Vector3.up * 2;
            var b = a + dir.normalized * 30;
            _line.positionCount = 3;
            _line.SetPosition(0, a);
            _line.SetPosition(1, Vector3.Lerp(a, b, 0.5f) + Vector3.up * 1.5f);
            _line.SetPosition(2, b);
            _ghost.gameObject.SetActive(true);
            _ghost.position = _downWorld + Vector3.up * 0.1f;
            _ghost.localScale = Vector3.one * 4;
        }

        void Release(Camera cam, Vector2 pos, bool tap, double t)
        {
            var w = View.World;
            if (Active == null)
            {
                if (!tap) return;
                var pick = View.Pick(cam, pos, t, false, Px(44));
                OnSelect?.Invoke(pick);
                return;
            }
            switch (Active.Value)
            {
                case PowerType.Nudge:
                {
                    if (_dragProp >= 0)
                    {
                        var p = w.GetProp(_dragProp);
                        if (p == null) return;
                        if (tap)
                        {
                            // one tap is enough: roll it onto the path of whoever is coming (or the nearest road)
                            var s = w.SuggestNudgeTarget(p.id);
                            var at = TerrainBuilder.Ground(s.x, s.z);
                            Session.Act(PlayerInput.Nudge(p.id, s), at);
                            return;
                        }
                        if (!TerrainBuilder.Raycast(cam.ScreenPointToRay(pos), out var hit)) return;
                        var to = ClampNudge(p, hit);
                        Session.Act(PlayerInput.Nudge(p.id, new Vec2(to.x, to.z)), to);
                        return;
                    }
                    var pick = View.Pick(cam, pos, t, true, Px(64));
                    if (pick.kind == PickKind.Prop)
                    {
                        var p = w.GetProp(pick.id);
                        if (p != null && (p.type == PropType.Lantern || p.type == PropType.Cart))
                        { Session.Act(PlayerInput.Nudge(p.id, p.pos), TerrainBuilder.Ground(p.pos)); return; }
                    }
                    if (tap) OnHint?.Invoke("Nudge works on small things: pebbles, trinkets, the lantern, the cart's chock."); // a drag was a camera pan
                    return;
                }
                case PowerType.Gust:
                {
                    if (!TerrainBuilder.Raycast(cam.ScreenPointToRay(pos), out var hit)) return;
                    var dir = hit - _downWorld; dir.y = 0;
                    if (dir.magnitude < 1.5f) { OnHint?.Invoke("Drag to show which way the wind should blow."); return; }
                    Session.Act(PlayerInput.GustAt(new Vec2(_downWorld.x, _downWorld.z), new Vec2(dir.x, dir.z)), _downWorld);
                    return;
                }
                case PowerType.Distract:
                {
                    if (!tap) return; // a drag is a camera move, not a choice
                    if (!SnapTarget(cam, pos, out var cid, out var cat, out _)) { OnHint?.Invoke("Tap near a villager who is outdoors (ringed targets)."); return; }
                    Session.Act(PlayerInput.DistractCitizen(cid), cat);
                    return;
                }
                case PowerType.Drop:
                {
                    if (!tap || !SnapTarget(cam, pos, out _, out var hit, out _)) return;
                    Session.Act(PlayerInput.DropAt(new Vec2(hit.x, hit.z)), hit);
                    return;
                }
            }
        }
    }
}
