// Frame-time graph for the advanced performance overlay (port of zenith-umbra src/engine/FrameGraph.ts, engine core):
// the last 160 frame intervals as bars against the display's refresh budget - green on time, amber late, red a dropped
// frame (2x+) - with the GPU's time per frame as a cyan line. Smoothness you can see, not just an average: one hitch
// shows as one red spike. The TS drew on a 2D canvas; here the ring (FrameGraph, pushed by Perf every frame) is data
// and FrameGraphElement draws it with UI Toolkit's Painter2D when it is in a panel and shown. The HUD owner adds the
// element; it is self-contained (280x72, rgba(0,0,0,.45), pickingMode Ignore). One deviation: the budget Perf pushes
// is the frame budget (the cap's interval when capped), not the TS's refresh interval - with a 60 cap on a 144 Hz
// display every frame is "on time" at 16.7 ms, and the TS graph painted them all red.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ZU.Engine
{
    public sealed class FrameGraph
    {
        /// <summary>frames kept</summary>
        public const int N = 160;
        readonly float[] dts = new float[N];
        readonly float[] gpu = new float[N];
        int i;
        /// <summary>the budget (ms) of the last push: the refresh interval</summary>
        public float BudgetMs { get; private set; } = 1000f / 60;
        /// <summary>frames pushed so far (the ring holds the last N)</summary>
        public int Count => Math.Min(i, N);
        /// <summary>after each push (the element repaints on it)</summary>
        public event Action Pushed;

        /// <summary>one rendered frame: its interval and GPU time (ms; gpu &lt; 0 = unknown) against the refresh interval</summary>
        public void Push(float dtMs, float gpuMs, float budgetMs)
        {
            dts[i % N] = dtMs; gpu[i % N] = gpuMs; i++;
            BudgetMs = budgetMs;
            Pushed?.Invoke();
        }

        /// <summary>frame interval k (0 = oldest .. N-1 = newest; 0 = not yet filled)</summary>
        public float Dt(int k) => dts[(i + k) % N];
        /// <summary>GPU time of frame k (0 = oldest .. N-1 = newest; &lt;= 0 = unknown)</summary>
        public float Gpu(int k) => gpu[(i + k) % N];
    }

    /// <summary>the graph as a HUD element: Perf.Graph (or the one given) drawn with Painter2D on every push while shown</summary>
    public sealed class FrameGraphElement : VisualElement
    {
        const float W = 280, H = 72;
        static readonly Color GREEN = new Color(0x5b / 255f, 0xe3 / 255f, 0x7d / 255f), AMBER = new Color(1f, 0xb3 / 255f, 0x47 / 255f),
            RED = new Color(1f, 0x4d / 255f, 0x5e / 255f), CYAN = new Color(0x4f / 255f, 0xd7 / 255f, 1f), LINE = new Color(1, 1, 1, 0.55f);

        readonly FrameGraph g;
        readonly Label label;
        float labelBudget = -1, labelH = -1;
        readonly Action onPush;

        public FrameGraphElement(FrameGraph graph = null)
        {
            g = graph ?? Perf.Graph;
            pickingMode = PickingMode.Ignore;
            style.width = W; style.height = H;
            style.backgroundColor = new Color(0, 0, 0, 0.45f);
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 2;
            style.overflow = Overflow.Hidden;
            // "xx.x ms" at the 1x budget line (TS: 600 10px Consolas at (3, H - budget*scale - 3); budget*scale = H/3 always)
            label = new Label { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = 3;
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(1, 1, 1, 0.8f);
            label.style.marginTop = label.style.marginBottom = label.style.marginLeft = label.style.marginRight = 0;
            label.style.paddingTop = label.style.paddingBottom = label.style.paddingLeft = label.style.paddingRight = 0;
            Add(label);
            onPush = OnPush;                               // one delegate, allocated once
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(_ => g.Pushed += onPush);
            RegisterCallback<DetachFromPanelEvent>(_ => g.Pushed -= onPush);
        }

        void OnPush()
        {
            if (resolvedStyle.display == DisplayStyle.None || !visible) return;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            float w = r.width > 0 ? r.width : W, h = r.height > 0 ? r.height : H;
            float budget = g.BudgetMs;
            if (!(budget > 0)) return;
            float scale = h / (budget * 3);                // the graph shows 0..3x the budget
            float bw = w / FrameGraph.N, bwid = Mathf.Max(1, bw - 0.4f);
            p.lineWidth = 1; p.lineCap = LineCap.Butt;
            // bars, one path per colour (three fills instead of 160)
            for (int c = 0; c < 3; c++)
            {
                p.fillColor = c == 0 ? GREEN : c == 1 ? AMBER : RED;
                p.BeginPath();
                bool any = false;
                for (int k = 0; k < FrameGraph.N; k++)
                {
                    float d = g.Dt(k);
                    if (!(d > 0)) continue;
                    int cls = d > budget * 1.9f ? 2 : d > budget * 1.15f ? 1 : 0;
                    if (cls != c) continue;
                    float bh = Mathf.Min(h, d * scale), x = k * bw, y = h - bh;
                    p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + bwid, y)); p.LineTo(new Vector2(x + bwid, y + bh)); p.LineTo(new Vector2(x, y + bh)); p.ClosePath();
                    any = true;
                }
                if (any) p.Fill();
            }
            // the 1x and 2x budget lines
            p.strokeColor = LINE;
            for (int m = 1; m <= 2; m++)
            {
                float y = Mathf.Round(h - budget * m * scale) + 0.5f;
                p.BeginPath(); p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(w, y)); p.Stroke();
            }
            // GPU time: a polyline broken wherever the sample is unknown
            p.strokeColor = CYAN; p.BeginPath();
            bool pen = false, drawn = false;
            for (int k = 0; k < FrameGraph.N; k++)
            {
                float gp = g.Gpu(k);
                if (!(gp > 0)) { pen = false; continue; }
                var pt = new Vector2(k * bw + bw / 2, h - Mathf.Min(h, gp * scale));
                if (pen) { p.LineTo(pt); drawn = true; } else { p.MoveTo(pt); pen = true; }
            }
            if (drawn) p.Stroke();
            // the label: text only when the budget changes (a string per change, not per frame)
            if (Mathf.Abs(budget - labelBudget) > 0.05f) { labelBudget = budget; label.text = budget.ToString("0.0") + " ms"; }
            if (Mathf.Abs(h - labelH) > 0.5f) { labelH = h; label.style.bottom = h / 3 + 1; }
        }
    }
}
