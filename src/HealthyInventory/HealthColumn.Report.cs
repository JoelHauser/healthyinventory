using System.Collections.Generic;
using System.Text;
using EFT.UI.Health;
using UnityEngine;
using UnityEngine.UI;

namespace HealthyInventory
{
    /// <summary>
    /// The layout report: one block in the log with every rectangle that matters, in canvas units,
    /// plus the overlap and clipping checks the test plan asks for by eye. Written on the first
    /// combined open of a session and after a size change, two frames late so layout groups have
    /// settled. Off with "Log layout decisions". Allocates only when it writes.
    /// </summary>
    internal sealed partial class HealthColumn
    {
        private const int ReportDelayFrames = 2;
        private static bool _reportedThisSession;

        private int _reportIn;
        private string _reportReason;

        private void ScheduleReport(string reason)
        {
            if (!Plugin.VerboseLog.Value) return;
            if (_reportIn <= 0) _reportReason = reason;   // a pending first-open report keeps its reason
            _reportIn = ReportDelayFrames;   // restarted while a window is being dragged: one report after it stops
        }

        private void PumpReport()
        {
            if (_reportIn <= 0 || --_reportIn > 0) return;
            _reportedThisSession = true;
            try
            {
                Plugin.Log.LogInfo(BuildReport(_reportReason));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Layout report failed (layout itself unaffected): {e.GetType().Name}: {e.Message}");
            }
        }

        private string BuildReport(string reason)
        {
            Canvas canvas = _host.GetComponentInParent<Canvas>();
            canvas = canvas != null ? canvas.rootCanvas : null;
            var root = canvas != null ? canvas.transform as RectTransform : null;

            var sb = new StringBuilder(2048);
            sb.Append("Layout report (").Append(reason).Append("). Units are canvas units from its bottom-left.\n");
            sb.Append($"  Screen {Screen.width}x{Screen.height}");
            if (canvas != null && root != null)
                sb.Append($", canvas scale {canvas.scaleFactor:0.###}, canvas {root.rect.width:0}x{root.rect.height:0} units");
            sb.Append('\n');

            Box gear = Line(sb, root, "Gear column (LeftSide)", _leftSide);
            Line(sb, root, "Left Panel", _leftPanel);
            Line(sb, root, "Stash Panel", _stash);
            Box column = Line(sb, root, "Health column", _host);
            Line(sb, root, "Fit box", _fit);
            Box panel = Line(sb, root, "Health panel", _panel.transform as RectTransform);
            sb.Append($"  Fit: design {_design.x:0}x{_design.y:0}, column {_hostSize.x:0}x{_hostSize.y:0}, scale {_scale:0.###}\n");
            if (_leftSide != null) sb.Append($"  Gap between gear column and health column: {column.XMin - gear.XMax:0.#}\n");

            // The panel's own top-level pieces (body diagram, effects, parameters strip...).
            var pt = _panel.transform;
            for (int i = 0; i < pt.childCount; i++)
            {
                var c = pt.GetChild(i) as RectTransform;
                if (c == null) continue;
                Line(sb, root, "  child " + c.name + (c.gameObject.activeSelf ? "" : " (inactive)"), c);
            }

            var parts = _panel.GetComponentsInChildren<BodyPartView>(false);
            sb.Append($"  Body parts shown: {parts.Length}\n");
            foreach (var p in parts) Line(sb, root, "    " + p.name, p.transform as RectTransform);

            // Checks: every visible graphic of the panel inside the column, none over the gear column.
            int visible = 0, outside = 0, overGear = 0;
            var examples = new List<string>();
            foreach (var g in _panel.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.enabled || g.color.a <= 0f) continue;
                visible++;
                Box b = ToCanvas(root, g.rectTransform);
                bool o = !Rules.Inside(b, column, Rules.ReportTolerance);
                bool v = _leftSide != null && Rules.Overlaps(b, gear, Rules.ReportTolerance);
                if (o) outside++;
                if (v) overGear++;
                if ((o || v) && examples.Count < 6) examples.Add($"{Path(g.transform)} {b}");
            }
            sb.Append($"  Check: {visible} visible graphics; {outside} outside the column, {overGear} over the gear column");
            sb.Append(outside == 0 && overGear == 0 ? ". OK\n" : ". LOOK:\n");
            foreach (var e in examples) sb.Append("    ").Append(e).Append('\n');
            if (!Rules.Inside(panel, column, Rules.ReportTolerance))
                sb.Append("  Check: the health panel's own rect extends past the column. LOOK\n");
            sb.Append($"  Decorations: background {(_background != null ? (_background.enabled ? "on" : "off") : "missing")}, " +
                      $"bottom bar {State(_bottomBar)}, border {State(_border)}");
            return sb.ToString();
        }

        private static string State(GameObject go) => go == null ? "missing" : (go.activeSelf ? "on" : "off");

        private static Box Line(StringBuilder sb, RectTransform root, string label, RectTransform rt)
        {
            if (rt == null)
            {
                sb.Append("  ").Append(label).Append(": missing\n");
                return default;
            }
            Box b = ToCanvas(root, rt);
            sb.Append("  ").Append(label).Append(' ').Append(b).Append($" {b.Width:0.#}x{b.Height:0.#}\n");
            return b;
        }

        private static readonly Vector3[] ReportCorners = new Vector3[4];

        /// <summary>A RectTransform's box in the root canvas's units, origin at its bottom-left.
        /// Without a canvas, world units.</summary>
        private static Box ToCanvas(RectTransform root, RectTransform rt)
        {
            rt.GetWorldCorners(ReportCorners);
            Vector3 a = ReportCorners[0], b = ReportCorners[2];
            if (root != null)
            {
                Vector2 o = root.rect.min;
                a = root.InverseTransformPoint(a) - (Vector3)o;
                b = root.InverseTransformPoint(b) - (Vector3)o;
            }
            return new Box(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != _panel.transform.parent; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
