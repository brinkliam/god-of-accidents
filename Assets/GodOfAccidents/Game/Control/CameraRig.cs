using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GOA.Game
{
    /// <summary>
    /// The god's-eye camera: orbit, pan and zoom from a whole-valley vista down to a single villager, with smooth
    /// cinematic moves to events. Works with mouse + keyboard and with touch (pinch, twist, drag).
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public Vector3 Target = new Vector3(8, 0, -6);
        public float Yaw = 205, Pitch = 48, Distance = 95;
        float _yawS, _pitchS, _distS;
        Vector3 _targetS;
        public float MinDistance = 7, MaxDistance = 340;
        public Func<Vector2, bool> IsOverUI = _ => false;
        public Func<bool> GestureCaptured = () => false;   // a power is using the pointer
        public bool InputEnabled = true;
        public bool AutoOrbit;                             // main-menu vista
        public Func<Vector3> Follow;
        float _cineT = -1, _cineDur;
        Vector3 _cineFromT, _cineToT;
        float _cineFromD, _cineToD, _cineFromYaw, _cineToYaw, _cineFromPitch, _cineToPitch;
        Vector2 _lastMouse;
        bool _panning, _orbiting;
        float _lastPinch = -1, _lastTwist;
        Vector2 _lastTouchCenter;
        public bool UserMovedRecently => Time.unscaledTime - _lastUserInput < 1.5f;
        float _lastUserInput = -10;

        public void Init()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetParent(transform, false);
            Cam = go.AddComponent<Camera>();
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 2200;
            Cam.fieldOfView = 38;
            Cam.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<AudioListener>();
            _yawS = Yaw; _pitchS = Pitch; _distS = Distance; _targetS = Target;
        }

        public void FocusOn(Vector3 point, float distance, float duration = 1.6f, float? yaw = null, float? pitch = null)
        {
            Follow = null;
            _cineT = 0; _cineDur = Mathf.Max(0.05f, duration);
            _cineFromT = Target; _cineToT = point;
            _cineFromD = Distance; _cineToD = Mathf.Clamp(distance, MinDistance, MaxDistance);
            _cineFromYaw = Yaw; _cineToYaw = yaw ?? Yaw;
            _cineFromPitch = Pitch; _cineToPitch = pitch ?? Mathf.Lerp(34, 62, Mathf.InverseLerp(MinDistance, 200, distance));
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (InputEnabled && !AutoOrbit) HandleInput(dt);
            if (AutoOrbit) Yaw += dt * 2.2f;

            if (_cineT >= 0)
            {
                _cineT += dt;
                float u = Mathf.Clamp01(_cineT / _cineDur);
                float e = u * u * (3 - 2 * u);
                Target = Vector3.Lerp(_cineFromT, _cineToT, e);
                // arc the zoom: pull out a little mid-flight for a sense of travel
                float arc = Mathf.Sin(u * Mathf.PI) * Mathf.Min(60, Vector3.Distance(_cineFromT, _cineToT) * 0.35f);
                Distance = Mathf.Lerp(_cineFromD, _cineToD, e) + arc;
                Yaw = Mathf.LerpAngle(_cineFromYaw, _cineToYaw, e);
                Pitch = Mathf.Lerp(_cineFromPitch, _cineToPitch, e);
                if (u >= 1) _cineT = -1;
            }
            else if (Follow != null)
            {
                Target = Vector3.Lerp(Target, Follow(), 1 - Mathf.Exp(-dt * 4));
            }

            Target.x = Mathf.Clamp(Target.x, -205, 205);
            Target.z = Mathf.Clamp(Target.z, -205, 205);
            Target.y = Mathf.Max(TerrainBuilder.WaterLevel, TerrainBuilder.Height(Target.x, Target.z));
            Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
            // pitch eases toward top-down as you zoom out, like peering into a diorama
            float minPitch = Mathf.Lerp(16, 34, Mathf.InverseLerp(30, MaxDistance, Distance));
            Pitch = Mathf.Clamp(Pitch, minPitch, 82);

            float k = 1 - Mathf.Exp(-dt * 9);
            _yawS = Mathf.LerpAngle(_yawS, Yaw, k);
            _pitchS = Mathf.Lerp(_pitchS, Pitch, k);
            _distS = Mathf.Lerp(_distS, Distance, k);
            _targetS = Vector3.Lerp(_targetS, Target, k);

            var rot = Quaternion.Euler(_pitchS, _yawS, 0);
            var pos = _targetS - rot * Vector3.forward * _distS;
            float ground = TerrainBuilder.Height(pos.x, pos.z) + 2.5f;
            if (pos.y < ground) pos.y = ground;
            Cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_targetS - pos));
        }

        void UserInput() { _lastUserInput = Time.unscaledTime; _cineT = -1; }

        void HandleInput(float dt)
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            var touch = Touchscreen.current;
            bool touching = touch != null && touch.touches.Count > 0 && CountTouches(touch) > 0;

            if (kb != null)
            {
                Vector2 move = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (move != Vector2.zero) { Pan(move * dt * Distance * 0.9f); Follow = null; UserInput(); }
                if (kb.qKey.isPressed) { Yaw -= dt * 70; UserInput(); }
                if (kb.eKey.isPressed) { Yaw += dt * 70; UserInput(); }
                if (kb.rKey.isPressed) { Distance *= 1 - dt * 1.4f; UserInput(); }
                if (kb.fKey.isPressed) { Distance *= 1 + dt * 1.4f; UserInput(); }
            }

            if (!touching && mouse != null)
            {
                var mp = mouse.position.ReadValue();
                bool overUI = IsOverUI(mp);
                float scroll = mouse.scroll.ReadValue().y;
                if (!overUI && Mathf.Abs(scroll) > 0.01f)
                {
                    float factor = Mathf.Pow(0.9f, Mathf.Sign(scroll) * Mathf.Min(3, Mathf.Abs(scroll) / 60f + 0.5f));
                    ZoomToward(mp, factor);
                    UserInput();
                }
                if (mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame) { _orbiting = !overUI; _lastMouse = mp; }
                if (mouse.leftButton.wasPressedThisFrame) { _panning = !overUI && !GestureCaptured(); _lastMouse = mp; }
                if (!mouse.rightButton.isPressed && !mouse.middleButton.isPressed) _orbiting = false;
                if (!mouse.leftButton.isPressed || GestureCaptured()) _panning = false;
                var delta = mp - _lastMouse;
                _lastMouse = mp;
                if (_orbiting && delta.sqrMagnitude > 0) { Yaw += delta.x * 0.25f; Pitch -= delta.y * 0.2f; UserInput(); }
                if (_panning && delta.sqrMagnitude > 4) { PanScreen(delta); Follow = null; UserInput(); }
            }

            if (touching) HandleTouch(touch);
            else _lastPinch = -1;
        }

        static int CountTouches(Touchscreen ts)
        {
            int n = 0;
            foreach (var t in ts.touches) if (t.press.isPressed) n++;
            return n;
        }

        void HandleTouch(Touchscreen ts)
        {
            Vector2 a = default, b = default; int n = 0;
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                if (n == 0) a = t.position.ReadValue(); else if (n == 1) b = t.position.ReadValue();
                n++;
            }
            if (n >= 2)
            {
                float pinch = Vector2.Distance(a, b);
                float twist = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
                var center = (a + b) * 0.5f;
                if (_lastPinch > 0)
                {
                    ZoomToward(center, _lastPinch / Mathf.Max(1, pinch));
                    Yaw -= Mathf.DeltaAngle(_lastTwist, twist);
                    var cd = center - _lastTouchCenter;
                    Pitch -= cd.y * 0.15f;
                    UserInput();
                }
                _lastPinch = pinch; _lastTwist = twist; _lastTouchCenter = center;
            }
            else
            {
                _lastPinch = -1;
                foreach (var t in ts.touches)
                {
                    if (!t.press.isPressed) continue;
                    var d = t.delta.ReadValue();
                    if (!GestureCaptured() && !IsOverUI(t.position.ReadValue()) && d.sqrMagnitude > 1) { PanScreen(d); Follow = null; UserInput(); }
                    break;
                }
            }
        }

        void Pan(Vector2 v)
        {
            var fwd = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, Yaw, 0) * Vector3.right;
            Target += right * v.x + fwd * v.y;
        }

        void PanScreen(Vector2 deltaPx)
        {
            float worldPerPx = 2f * Distance * Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
            Pan(-deltaPx * worldPerPx * 1.05f);
        }

        void ZoomToward(Vector2 screen, float factor)
        {
            float before = Distance;
            Distance = Mathf.Clamp(Distance * factor, MinDistance, MaxDistance);
            if (TerrainBuilder.Raycast(Cam.ScreenPointToRay(screen), out var hit))
            {
                float moved = 1 - Distance / before;
                Target = Vector3.Lerp(Target, hit, moved * 0.85f);
            }
        }
    }
}
