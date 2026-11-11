using System.Collections.Generic;
using EFT.UI.Health;
using HarmonyLib;
using UnityEngine;

namespace HealthyInventory
{
    /// <summary>
    /// The parameters strip along the bottom of the borrowed health panel, adjusted while it is in
    /// the column and put back exactly on End:
    ///
    /// - Overall health, hydration and energy are hidden: the gear column's own bottom strip
    ///   already shows them right beside the column. Hidden with a CanvasGroup at alpha 0, not
    ///   SetActive, so the game's once-a-second updates and the buff arrows' animators keep
    ///   running exactly as they do on the Health tab.
    /// - Poisoning and radiation take the hydration and energy slots (row below), each swapping
    ///   parent, sibling index and rect with the row it replaces, so it works whether the strip is
    ///   laid out by hand or by a layout group.
    /// </summary>
    internal sealed partial class HealthColumn
    {
        // Report only: the colour HealthParameterPanel repaints its value with, saved in its Awake.
        private static readonly AccessTools.FieldRef<HealthParameterPanel, Color> DefaultColor =
            AccessTools.FieldRefAccess<HealthParameterPanel, Color>("_defaultColor");

        private readonly List<CanvasGroup> _hiddenRows = new List<CanvasGroup>(3);
        private RectTransform _swapA1, _swapB1, _swapA2, _swapB2;

        private void AdjustParameters()
        {
            var pp = _panel._parametersPanel;
            if (pp == null) return;

            Hide(pp._overallHealth);
            Hide(pp._hydration);
            Hide(pp._energy);

            if (Swap(pp._poisoning, pp._hydration)) { _swapA1 = Rect(pp._poisoning); _swapB1 = Rect(pp._hydration); }
            if (Swap(pp._radiation, pp._energy)) { _swapA2 = Rect(pp._radiation); _swapB2 = Rect(pp._energy); }
        }

        /// <summary>Undo everything AdjustParameters did, in reverse. Only touches what it changed,
        /// so a failure halfway through Begin is undone cleanly.</summary>
        private void RestoreParameters()
        {
            if (_swapA2 != null && _swapB2 != null) SwapSlots(_swapA2, _swapB2);
            if (_swapA1 != null && _swapB1 != null) SwapSlots(_swapA1, _swapB1);
            _swapA1 = _swapB1 = _swapA2 = _swapB2 = null;

            foreach (var g in _hiddenRows)
            {
                if (g == null) continue;
                g.alpha = 1f;
                g.blocksRaycasts = true;
            }
            _hiddenRows.Clear();
        }

        private void Hide(HealthParameterPanel row)
        {
            if (row == null) return;
            // Our own group, kept between opens; at alpha 1 with raycasts on it is neutral.
            var g = row.GetComponent<CanvasGroup>();
            if (g == null) g = row.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            g.blocksRaycasts = false;   // no tooltip from an invisible row
            _hiddenRows.Add(g);
        }

        private static RectTransform Rect(Component c) => c != null ? c.transform as RectTransform : null;

        private static bool Swap(HealthParameterPanel a, HealthParameterPanel b)
        {
            var ra = Rect(a);
            var rb = Rect(b);
            if (ra == null || rb == null || ra == rb) return false;
            SwapSlots(ra, rb);
            return true;
        }

        /// <summary>Each takes the other's parent, sibling index and rect. Its own inverse.</summary>
        private static void SwapSlots(RectTransform a, RectTransform b)
        {
            Transform pa = a.parent, pb = b.parent;
            int ia = a.GetSiblingIndex(), ib = b.GetSiblingIndex();
            Vector2 aMin = a.anchorMin, aMax = a.anchorMax, aPivot = a.pivot, aPos = a.anchoredPosition, aSize = a.sizeDelta;

            if (pa == pb)
            {
                // Move the lower index first; the higher one is then one place lower than it was.
                if (ia < ib) { a.SetSiblingIndex(ib); b.SetSiblingIndex(ia); }
                else { b.SetSiblingIndex(ia); a.SetSiblingIndex(ib); }
            }
            else
            {
                a.SetParent(pb, false);
                a.SetSiblingIndex(ib);
                b.SetParent(pa, false);
                b.SetSiblingIndex(ia);
            }

            a.anchorMin = b.anchorMin; a.anchorMax = b.anchorMax; a.pivot = b.pivot;
            a.anchoredPosition = b.anchoredPosition; a.sizeDelta = b.sizeDelta;
            b.anchorMin = aMin; b.anchorMax = aMax; b.pivot = aPivot;
            b.anchoredPosition = aPos; b.sizeDelta = aSize;
        }

        private static float DefaultAlpha(HealthParameterPanel row) => row != null ? DefaultColor(row).a : -1f;
    }
}
