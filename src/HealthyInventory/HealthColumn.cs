using System;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Health;
using UnityEngine;
using UnityEngine.UI;

namespace HealthyInventory
{
    /// <summary>
    /// The right-hand health column. It lives under ItemsPanel's "Stash Panel" (the column the
    /// loot panels use, empty for the player's own inventory in raid), and while the combined
    /// layout is on it borrows the vanilla InventoryScreenHealthPanel from "Left Panel" and hands
    /// it back afterwards.
    ///
    /// Built once per ItemsPanel and kept: CommonUI, and with it the InventoryScreen, lives for the
    /// whole game session. Nothing here searches the scene; every object is reached from the
    /// ItemsPanel's own serialized fields and their parents.
    /// </summary>
    internal sealed class HealthColumn : MonoBehaviour
    {
        private const string HostName = "HealthyInventory Health Column";

        private static readonly Vector3[] Corners = new Vector3[4];

        private static HealthColumn _current;
        private static bool _refreshPending;

        // Counters for the leak check in the log (test plan 8).
        internal static int Begins, Ends;

        private ItemsPanel _items;
        private RectTransform _host, _fit, _stash, _leftSide, _leftPanel;
        private InventoryScreenHealthPanel _panel;
        private GameObject _simpleLoot, _complexLoot;
        private Image _background;
        private GameObject _bottomBar, _border;

        // Where the health panel came from, restored exactly on End.
        private Transform _origParent;
        private int _origSibling;
        private Vector2 _oAnchorMin, _oAnchorMax, _oPivot, _oOffsetMin, _oOffsetMax;
        private Vector3 _oScale;

        private bool _combined;
        private Vector2 _lastStash, _lastLeftSide, _lastLeftPanel;

        // Read for the log line only.
        private float _scale;
        private Vector2 _design, _hostSize;

        internal static bool IsCombined => _current != null && _current._combined;

        /// <summary>The column for this ItemsPanel, built on first use. Null if the panel's
        /// hierarchy is not the one read from the prefab (another mod rebuilt it).</summary>
        internal static HealthColumn For(ItemsPanel items)
        {
            if (_current != null && _current._items == items) return _current;
            if (_current != null) _current.End();   // a different ItemsPanel: should not happen

            var stash = items._simpleStashPanel != null ? items._simpleStashPanel.transform.parent as RectTransform : null;
            var panel = items._healthPanel;
            var leftPanel = panel != null ? panel.transform.parent as RectTransform : null;
            if (stash == null || panel == null || leftPanel == null)
            {
                Plugin.Log.LogWarning("Inventory layout is not the expected one (no Stash Panel or Health Panel); leaving it vanilla.");
                return null;
            }
            if (items._complexStashPanel != null && items._complexStashPanel.transform.parent != stash)
            {
                Plugin.Log.LogWarning("The loot panels have been moved apart by something else; leaving the inventory vanilla.");
                return null;
            }

            var go = new GameObject(HostName, typeof(RectTransform));
            go.SetActive(false);
            var host = (RectTransform)go.transform;
            host.SetParent(stash, false);
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.pivot = new Vector2(0.5f, 0.5f);
            host.offsetMin = Vector2.zero;
            host.offsetMax = Vector2.zero;
            host.localScale = Vector3.one;

            var col = go.AddComponent<HealthColumn>();
            col._items = items;
            col._host = host;
            col._stash = stash;
            col._panel = panel;
            col._leftPanel = leftPanel;
            col._leftSide = leftPanel.parent as RectTransform;
            col._simpleLoot = items._simpleStashPanel.gameObject;
            col._complexLoot = items._complexStashPanel != null ? items._complexStashPanel.gameObject : null;
            col.Build();

            _current = col;
            return col;
        }

        private void Build()
        {
            // The scaled box the panel is laid out in, at the size of the column it was designed for.
            var fitGo = new GameObject("Fit", typeof(RectTransform));
            _fit = (RectTransform)fitGo.transform;
            _fit.SetParent(_host, false);
            _fit.anchorMin = _fit.anchorMax = new Vector2(0.5f, 1f);
            _fit.pivot = new Vector2(0.5f, 1f);
            _fit.anchoredPosition = Vector2.zero;

            // Left Panel's own background, copied: the vanilla Health tab draws over it.
            var srcBg = _leftPanel.GetComponent<Image>();
            if (srcBg != null)
            {
                _background = fitGo.AddComponent<Image>();
                _background.sprite = srcBg.sprite;
                _background.type = srcBg.type;
                _background.color = srcBg.color;
                _background.material = srcBg.material;   // the default material when none is set
                _background.pixelsPerUnitMultiplier = srcBg.pixelsPerUnitMultiplier;
                _background.preserveAspect = srcBg.preserveAspect;
                _background.fillCenter = srcBg.fillCenter;
                _background.raycastTarget = false;
            }

            // The strip behind the parameters row. In Left Panel it is a later sibling than the
            // health panel, so it is kept after it here too; End/Begin keep that order.
            var bar = _leftPanel.Find("Bottom Bar");
            if (bar != null)
            {
                _bottomBar = Instantiate(bar.gameObject, _fit, false);
                _bottomBar.name = "Bottom Bar";
                NoRaycasts(_bottomBar);
            }

            // The frame drawn round the gear and containers, so the two columns match.
            var border = _leftSide != null ? _leftSide.Find("Border") : null;
            if (border != null)
            {
                _border = Instantiate(border.gameObject, _host, false);
                _border.name = "Border";
                var le = _border.GetComponent<LayoutElement>();
                if (le != null) le.ignoreLayout = true;
                var brt = (RectTransform)_border.transform;
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;
                brt.SetAsLastSibling();
                NoRaycasts(_border);
            }

            // Event-driven loot detection: these fire only when a loot panel opens or closes.
            AddRelay(_simpleLoot);
            AddRelay(_complexLoot);
            ApplyDecorations();

            Plugin.Log.LogInfo($"Health column built (background {(srcBg != null ? "copied" : "missing")}, " +
                               $"bottom bar {(bar != null ? "copied" : "missing")}, border {(border != null ? "copied" : "missing")}).");
        }

        private static void NoRaycasts(GameObject go)
        {
            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }

        private void AddRelay(GameObject go)
        {
            if (go == null) return;
            // Not '??': it skips Unity's null check.
            if (go.GetComponent<LootPanelRelay>() == null) go.AddComponent<LootPanelRelay>();
        }

        internal void ApplyDecorations()
        {
            bool frame = Plugin.ColumnFrame.Value;
            if (_background != null) _background.enabled = frame;
            if (_bottomBar != null) _bottomBar.SetActive(frame);
            if (_border != null) _border.SetActive(frame);
        }

        internal static void ApplyDecorationsToCurrent()
        {
            if (_current != null) _current.ApplyDecorations();
        }

        /// <summary>Borrow the health panel and show it with the game's own Show.</summary>
        internal void Begin(IHealthController health, Inventory inventory, SkillManager skills)
        {
            if (_combined) End();

            var t = (RectTransform)_panel.transform;
            _origParent = t.parent;
            _origSibling = t.GetSiblingIndex();
            _oAnchorMin = t.anchorMin; _oAnchorMax = t.anchorMax; _oPivot = t.pivot;
            _oOffsetMin = t.offsetMin; _oOffsetMax = t.offsetMax; _oScale = t.localScale;

            // Marked before anything moves, so a failure anywhere below is undone by End.
            _combined = true;
            Begins++;

            t.SetParent(_fit, false);
            t.SetSiblingIndex(0);   // under the bottom bar, as in Left Panel
            t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one; t.pivot = new Vector2(0.5f, 0.5f);
            t.offsetMin = Vector2.zero; t.offsetMax = Vector2.zero; t.localScale = Vector3.one;

            Relayout();
            _panel.Show(health, inventory, skills);
            Refresh();

            if (Plugin.VerboseLog.Value)
                Plugin.Log.LogInfo($"Combined layout on: health column {_hostSize.x:0}x{_hostSize.y:0}, panel {_design.x:0}x{_design.y:0} at scale {_scale:0.###}. " +
                                   $"Opens {Begins}, closes {Ends}. {Diagnostics.HandlerSummary(health)}");
        }

        /// <summary>Hand the panel back to Left Panel exactly as it was. Safe to call twice.</summary>
        internal void End()
        {
            if (!_combined) return;
            _combined = false;
            Ends++;
            try
            {
                _panel.Close();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Closing the borrowed health panel failed: {e}");
            }

            var t = (RectTransform)_panel.transform;
            if (_origParent != null)
            {
                t.SetParent(_origParent, false);
                t.SetSiblingIndex(_origSibling);
            }
            t.anchorMin = _oAnchorMin; t.anchorMax = _oAnchorMax; t.pivot = _oPivot;
            t.offsetMin = _oOffsetMin; t.offsetMax = _oOffsetMax; t.localScale = _oScale;
            _origParent = null;

            if (gameObject.activeSelf) gameObject.SetActive(false);

            if (Plugin.VerboseLog.Value)
                Plugin.Log.LogInfo($"Combined layout off; health panel returned to {(t.parent != null ? t.parent.name : "nothing")}. Opens {Begins}, closes {Ends}.");
        }

        internal static void EndFor(ItemsPanel items)
        {
            if (_current != null && _current._items == items) _current.End();
        }

        internal static void EndCurrent()
        {
            if (_current != null) _current.End();
        }

        internal static void MarkRefresh() => _refreshPending = true;

        /// <summary>Called from the plugin's LateUpdate. A relay cannot toggle the column itself:
        /// its OnDisable fires while the hierarchy is being deactivated, and Unity refuses
        /// SetActive inside that.</summary>
        internal static void PumpRefresh()
        {
            if (!_refreshPending) return;
            _refreshPending = false;
            if (_current != null) _current.Refresh();
        }

        private void Refresh()
        {
            bool loot = (_simpleLoot != null && _simpleLoot.activeSelf) || (_complexLoot != null && _complexLoot.activeSelf);
            bool visible = Rules.ColumnVisible(_combined, loot);
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
                if (_combined && loot && Plugin.VerboseLog.Value)
                    Plugin.Log.LogInfo("A loot panel opened under the combined layout; health column hidden.");
            }
        }

        /// <summary>Only runs while the column is visible. Recomputes on a size change only:
        /// three rect reads and compares a frame, no allocation.</summary>
        private void LateUpdate()
        {
            if (!_combined) return;
            Vector2 s = _stash.rect.size;
            Vector2 l = _leftSide != null ? _leftSide.rect.size : Vector2.zero;
            Vector2 p = _leftPanel.rect.size;
            if (s != _lastStash || l != _lastLeftSide || p != _lastLeftPanel)
            {
                Relayout();
                if (Plugin.VerboseLog.Value)
                    Plugin.Log.LogInfo($"Layout changed; health column now {_hostSize.x:0}x{_hostSize.y:0}, scale {_scale:0.###}.");
            }
        }

        private void Relayout()
        {
            Rect sr = _stash.rect;
            _lastStash = sr.size;
            _lastLeftSide = _leftSide != null ? _leftSide.rect.size : Vector2.zero;
            _lastLeftPanel = _leftPanel.rect.size;

            var stash = new Box(sr.xMin, sr.yMin, sr.xMax, sr.yMax);
            bool known = _leftSide != null && _leftSide.gameObject.activeInHierarchy;
            Box left = default;
            if (known)
            {
                _leftSide.GetWorldCorners(Corners);
                Vector3 a = _stash.InverseTransformPoint(Corners[0]);   // bottom-left
                Vector3 b = _stash.InverseTransformPoint(Corners[2]);   // top-right
                left = new Box(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
            }

            Offsets o = Rules.ColumnOffsets(stash, left, known);
            _host.offsetMin = new Vector2(o.Left, o.Bottom);
            _host.offsetMax = new Vector2(o.Right, o.Top);

            float hostW = stash.Width - o.Left + o.Right;
            float hostH = stash.Height - o.Bottom + o.Top;
            Vector2 design = _lastLeftPanel;
            if (!Rules.DesignUsable(design.x, design.y)) design = new Vector2(hostW, hostH);
            float k = Rules.FitScale(hostW, hostH, design.x, design.y);

            _fit.sizeDelta = design;
            _fit.localScale = new Vector3(k, k, 1f);

            _scale = k;
            _design = design;
            _hostSize = new Vector2(hostW, hostH);
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }
    }

    /// <summary>Sits on the vanilla loot panels and reports when one opens or closes.</summary>
    internal sealed class LootPanelRelay : MonoBehaviour
    {
        private void OnEnable() => HealthColumn.MarkRefresh();
        private void OnDisable() => HealthColumn.MarkRefresh();
    }
}
