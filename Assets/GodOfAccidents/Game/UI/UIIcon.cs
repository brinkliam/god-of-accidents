using UnityEngine;
using UnityEngine.UIElements;

namespace GOA.Game
{
    /// <summary>Resolution-independent vector icons drawn with Painter2D (no textures, no font glyph dependence).</summary>
    public sealed class UIIcon : VisualElement
    {
        public string Kind;
        public Color Tint = new Color(0.98f, 0.9f, 0.72f);

        public UIIcon(string kind, float size)
        {
            Kind = kind;
            pickingMode = PickingMode.Ignore;
            style.width = size; style.height = size;
            style.flexShrink = 0;
            generateVisualContent += Draw;
        }

        public void Set(string kind) { Kind = kind; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            Vector2 c = r.center;
            Vector2 P(float x, float y) => c + new Vector2(x, y) * s * 0.5f;
            p.strokeColor = Tint; p.fillColor = Tint;
            p.lineWidth = Mathf.Max(1.5f, s * 0.07f);
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            switch (Kind)
            {
                case "nudge":
                    p.BeginPath(); p.Arc(P(-0.35f, 0.2f), s * 0.2f, 0, 360); p.Fill();
                    p.BeginPath(); p.MoveTo(P(-0.05f, 0.2f)); p.LineTo(P(0.7f, 0.2f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(0.45f, -0.05f)); p.LineTo(P(0.72f, 0.2f)); p.LineTo(P(0.45f, 0.45f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-0.7f, 0.55f)); p.LineTo(P(0.75f, 0.55f)); p.Stroke();
                    break;
                case "gust":
                    p.BeginPath(); p.MoveTo(P(-0.75f, -0.35f)); p.BezierCurveTo(P(-0.1f, -0.35f), P(0.5f, -0.35f), P(0.5f, -0.6f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-0.75f, 0.0f)); p.BezierCurveTo(P(0.1f, 0f), P(0.75f, 0f), P(0.7f, -0.25f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-0.55f, 0.35f)); p.BezierCurveTo(P(0f, 0.35f), P(0.45f, 0.35f), P(0.45f, 0.62f)); p.Stroke();
                    break;
                case "distract":
                    Star(p, c, s * 0.42f, s * 0.12f, 4);
                    p.BeginPath(); p.Arc(P(0.55f, -0.55f), s * 0.07f, 0, 360); p.Fill();
                    p.BeginPath(); p.Arc(P(-0.55f, 0.5f), s * 0.05f, 0, 360); p.Fill();
                    break;
                case "drop":
                    p.BeginPath(); p.MoveTo(P(-0.75f, -0.45f)); p.LineTo(P(-0.25f, -0.1f)); p.LineTo(P(0f, -0.35f)); p.LineTo(P(0.25f, -0.1f)); p.LineTo(P(0.75f, -0.45f)); p.Stroke();
                    p.BeginPath(); p.Arc(P(0f, 0.45f), s * 0.13f, 0, 360); p.Fill();
                    p.BeginPath(); p.MoveTo(P(0f, -0.05f)); p.LineTo(P(0f, 0.22f)); p.Stroke();
                    break;
                case "lock":
                    p.BeginPath(); p.MoveTo(P(-0.3f, -0.05f)); p.LineTo(P(-0.3f, -0.3f)); p.BezierCurveTo(P(-0.3f, -0.7f), P(0.3f, -0.7f), P(0.3f, -0.3f)); p.LineTo(P(0.3f, -0.05f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-0.45f, -0.05f)); p.LineTo(P(0.45f, -0.05f)); p.LineTo(P(0.45f, 0.6f)); p.LineTo(P(-0.45f, 0.6f)); p.ClosePath(); p.Fill();
                    break;
                case "pause":
                    p.BeginPath(); p.MoveTo(P(-0.25f, -0.5f)); p.LineTo(P(-0.25f, 0.5f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(0.25f, -0.5f)); p.LineTo(P(0.25f, 0.5f)); p.Stroke();
                    break;
                case "play1": Tri(p, P(-0.3f, -0.5f), P(0.45f, 0), P(-0.3f, 0.5f)); break;
                case "play2": Tri(p, P(-0.6f, -0.45f), P(0f, 0), P(-0.6f, 0.45f)); Tri(p, P(0f, -0.45f), P(0.6f, 0), P(0f, 0.45f)); break;
                case "play3": Tri(p, P(-0.8f, -0.4f), P(-0.3f, 0), P(-0.8f, 0.4f)); Tri(p, P(-0.3f, -0.4f), P(0.2f, 0), P(-0.3f, 0.4f)); Tri(p, P(0.2f, -0.4f), P(0.7f, 0), P(0.2f, 0.4f)); break;
                case "play4":
                    Tri(p, P(-0.75f, -0.4f), P(-0.2f, 0), P(-0.75f, 0.4f)); Tri(p, P(-0.2f, -0.4f), P(0.35f, 0), P(-0.2f, 0.4f));
                    p.BeginPath(); p.MoveTo(P(0.55f, -0.45f)); p.LineTo(P(0.55f, 0.45f)); p.Stroke();
                    break;
                case "ages":
                    p.BeginPath(); p.Arc(c, s * 0.42f, 0, 360); p.Stroke();
                    p.BeginPath(); p.MoveTo(c); p.LineTo(P(0f, -0.55f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(c); p.LineTo(P(0.4f, 0.1f)); p.Stroke();
                    break;
                case "timeline":
                    p.BeginPath(); p.MoveTo(P(-0.8f, 0.4f)); p.BezierCurveTo(P(-0.3f, 0.4f), P(-0.3f, -0.4f), P(0.2f, -0.4f)); p.LineTo(P(0.8f, -0.4f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(-0.1f, 0.4f)); p.LineTo(P(0.8f, 0.4f)); p.Stroke();
                    p.BeginPath(); p.Arc(P(-0.8f, 0.4f), s * 0.1f, 0, 360); p.Fill();
                    p.BeginPath(); p.Arc(P(0.8f, -0.4f), s * 0.1f, 0, 360); p.Fill();
                    p.BeginPath(); p.Arc(P(0.8f, 0.4f), s * 0.1f, 0, 360); p.Fill();
                    break;
                case "menu":
                    for (int i = -1; i <= 1; i++) { p.BeginPath(); p.MoveTo(P(-0.55f, i * 0.4f)); p.LineTo(P(0.55f, i * 0.4f)); p.Stroke(); }
                    break;
                case "book":
                    p.BeginPath(); p.MoveTo(P(0, -0.4f)); p.LineTo(P(-0.75f, -0.55f)); p.LineTo(P(-0.75f, 0.45f)); p.LineTo(P(0, 0.6f)); p.LineTo(P(0.75f, 0.45f)); p.LineTo(P(0.75f, -0.55f)); p.ClosePath(); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(0, -0.4f)); p.LineTo(P(0, 0.6f)); p.Stroke();
                    break;
                case "eye":
                    p.BeginPath(); p.MoveTo(P(-0.8f, 0)); p.BezierCurveTo(P(-0.35f, -0.6f), P(0.35f, -0.6f), P(0.8f, 0)); p.BezierCurveTo(P(0.35f, 0.6f), P(-0.35f, 0.6f), P(-0.8f, 0)); p.Stroke();
                    p.BeginPath(); p.Arc(c, s * 0.15f, 0, 360); p.Fill();
                    break;
                case "star":
                    Star(p, c, s * 0.45f, s * 0.2f, 5);
                    break;
                case "starEmpty":
                    p.fillColor = new Color(Tint.r, Tint.g, Tint.b, 0.2f);
                    Star(p, c, s * 0.45f, s * 0.2f, 5);
                    break;
                case "energy":
                    p.BeginPath(); p.MoveTo(P(0.15f, -0.8f)); p.LineTo(P(-0.4f, 0.1f)); p.LineTo(P(0.0f, 0.1f)); p.LineTo(P(-0.15f, 0.8f)); p.LineTo(P(0.4f, -0.1f)); p.LineTo(P(0.0f, -0.1f)); p.ClosePath(); p.Fill();
                    break;
                case "ad":
                    p.BeginPath(); p.MoveTo(P(-0.7f, -0.5f)); p.LineTo(P(0.7f, -0.5f)); p.LineTo(P(0.7f, 0.5f)); p.LineTo(P(-0.7f, 0.5f)); p.ClosePath(); p.Stroke();
                    Tri(p, P(-0.2f, -0.25f), P(0.3f, 0), P(-0.2f, 0.25f));
                    break;
                case "close":
                    p.BeginPath(); p.MoveTo(P(-0.5f, -0.5f)); p.LineTo(P(0.5f, 0.5f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(0.5f, -0.5f)); p.LineTo(P(-0.5f, 0.5f)); p.Stroke();
                    break;
                case "check":
                    p.BeginPath(); p.MoveTo(P(-0.55f, 0.05f)); p.LineTo(P(-0.15f, 0.45f)); p.LineTo(P(0.6f, -0.45f)); p.Stroke();
                    break;
                case "dotEmpty":
                    p.BeginPath(); p.Arc(c, s * 0.3f, 0, 360); p.Stroke();
                    break;
            }
        }

        static void Tri(Painter2D p, Vector2 a, Vector2 b, Vector2 c)
        {
            p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.LineTo(c); p.ClosePath(); p.Fill();
        }

        static void Star(Painter2D p, Vector2 c, float outer, float inner, int points)
        {
            p.BeginPath();
            for (int i = 0; i < points * 2; i++)
            {
                float a = -Mathf.PI / 2 + i * Mathf.PI / points;
                float r = i % 2 == 0 ? outer : inner;
                var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath(); p.Fill();
        }
    }
}
