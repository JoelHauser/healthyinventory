// Pure decisions: no Unity, no game types. The test project links this file directly, because a
// net10 test host cannot load the net472 plugin with its BepInEx and Unity references. Keep it
// dependency free or the tests stop building.

using System;

namespace HealthyInventory
{
    /// <summary>What the inventory screen is showing, reduced to the facts the decision needs.</summary>
    public struct ScreenFacts
    {
        /// <summary>The ItemsPanel being shown is the InventoryScreen's own one.</summary>
        public bool PanelBelongsToInventoryScreen;

        /// <summary>The screen controller is the in-raid one (RaidInventoryScreenController).</summary>
        public bool RaidController;

        /// <summary>The controller was handed a loot item (body, container, loose item).</summary>
        public bool ControllerHasLoot;

        /// <summary>The screen itself holds a loot item (after the grid-content wrapping).</summary>
        public bool ScreenHasLoot;

        /// <summary>ItemsPanel is on its Gear sub-tab, not Health.</summary>
        public bool GearTab;

        /// <summary>The inRaid argument ItemsPanel was given.</summary>
        public bool InRaidArgument;
    }

    public enum Decision
    {
        Combine,
        Disabled,
        NotInventoryScreen,
        NotRaid,
        Looting,
        HealthTab,
    }

    /// <summary>A rectangle as min/max edges, in one shared space.</summary>
    public struct Box
    {
        public float XMin, YMin, XMax, YMax;

        public Box(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin; YMin = yMin; XMax = xMax; YMax = yMax;
        }

        public float Width => XMax - XMin;
        public float Height => YMax - YMin;

        public override string ToString() =>
            $"[{XMin:0.#},{YMin:0.#} .. {XMax:0.#},{YMax:0.#}]";
    }

    /// <summary>RectTransform offsets for the column inside Stash Panel (stretch anchors).</summary>
    public struct Offsets
    {
        public float Left, Bottom, Right, Top;   // offsetMin = (Left, Bottom), offsetMax = (Right, Top)
    }

    public static class Rules
    {
        /// <summary>Gap kept between the containers column and the health column.</summary>
        public const float MinGap = 8f;

        /// <summary>How far the column may be stretched beyond Stash Panel vertically. The
        /// prefab difference is 27 units at the top and 2 at the bottom; anything larger means
        /// another mod has moved the panels and following it would be guessing.</summary>
        public const float MaxVerticalStretch = 64f;

        /// <summary>A design size smaller than this was read before layout ran.</summary>
        public const float MinDesignEdge = 200f;

        public static Decision Decide(bool enabled, ScreenFacts f)
        {
            if (!enabled) return Decision.Disabled;
            if (!f.PanelBelongsToInventoryScreen) return Decision.NotInventoryScreen;
            if (!f.RaidController || !f.InRaidArgument) return Decision.NotRaid;
            if (f.ControllerHasLoot || f.ScreenHasLoot) return Decision.Looting;
            if (!f.GearTab) return Decision.HealthTab;
            return Decision.Combine;
        }

        /// <summary>Whether the column should be visible right now. A loot panel appearing while
        /// combined (another mod, or a vanilla path not seen in the IL) always wins.</summary>
        public static bool ColumnVisible(bool combined, bool lootPanelActive) => combined && !lootPanelActive;

        /// <summary>
        /// Offsets that make the column cover Stash Panel's width (minus a gap from the left
        /// side), and the gear column's height, so the two columns share a top and bottom edge.
        /// Both boxes are in Stash Panel's local space.
        /// </summary>
        public static Offsets ColumnOffsets(Box stash, Box leftSide, bool leftSideKnown)
        {
            var o = new Offsets();
            if (!leftSideKnown || leftSide.Width <= 0f || leftSide.Height <= 0f)
                return o;

            o.Top = Clamp(leftSide.YMax - stash.YMax, -MaxVerticalStretch, MaxVerticalStretch);
            o.Bottom = Clamp(leftSide.YMin - stash.YMin, -MaxVerticalStretch, MaxVerticalStretch);

            // Never let the column reach into the containers column.
            float wantLeft = leftSide.XMax + MinGap;
            o.Left = Math.Max(0f, wantLeft - stash.XMin);
            if (o.Left > stash.Width * 0.5f) o.Left = stash.Width * 0.5f;   // pathological layout: keep half
            return o;
        }

        /// <summary>
        /// Uniform scale for the vanilla health panel, laid out at its design size (the size of
        /// the gear column it was built for), so it fits the column. Never above 1: the panel is
        /// shown at its native size or shrunk, never stretched, so the body diagram keeps its
        /// proportions.
        /// </summary>
        public static float FitScale(float hostW, float hostH, float designW, float designH)
        {
            if (hostW <= 0f || hostH <= 0f) return 1f;
            if (!DesignUsable(designW, designH)) return 1f;
            float s = Math.Min(hostW / designW, hostH / designH);
            return s >= 1f ? 1f : s;
        }

        public static bool DesignUsable(float w, float h) => w >= MinDesignEdge && h >= MinDesignEdge;

        /// <summary>Canvas size in reference units, exactly as UICanvasScalerController sets it:
        /// ConstantPixelSize with scaleFactor = min(w/1920, h/1080).</summary>
        public static void CanvasUnits(int screenW, int screenH, out float w, out float h)
        {
            float s = Math.Min(screenW / 1920f, screenH / 1080f);
            w = screenW / s;
            h = screenH / s;
        }

        /// <summary>Slack for the layout report's checks: rounding in world-to-canvas conversion
        /// and one-unit borders drawn on an edge are not clipping.</summary>
        public const float ReportTolerance = 1.5f;

        /// <summary>Whether <paramref name="inner"/> lies within <paramref name="outer"/>, allowing
        /// <paramref name="tolerance"/> units on each edge.</summary>
        public static bool Inside(Box inner, Box outer, float tolerance) =>
            inner.XMin >= outer.XMin - tolerance && inner.XMax <= outer.XMax + tolerance &&
            inner.YMin >= outer.YMin - tolerance && inner.YMax <= outer.YMax + tolerance;

        /// <summary>Whether two boxes share more than <paramref name="tolerance"/> units in both
        /// directions. Touching edges are not an overlap.</summary>
        public static bool Overlaps(Box a, Box b, float tolerance) =>
            Math.Min(a.XMax, b.XMax) - Math.Max(a.XMin, b.XMin) > tolerance &&
            Math.Min(a.YMax, b.YMax) - Math.Max(a.YMin, b.YMin) > tolerance;

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
