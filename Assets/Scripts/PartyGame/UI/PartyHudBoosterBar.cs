using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PartyGame.UI
{
    /// <summary>
    /// Bottom-center booster HUD: a "按住 Shift 加速" hint above a horizontal durability bar that
    /// drains as the player sprints. Auto-visible only when the local player has a booster
    /// equipped (BoosterEquipped == true); disappears the frame the thruster is spent.
    ///
    /// The bar + label are built at runtime as children of this GameObject so no scene wiring is
    /// needed — put a PartyHudBoosterBar component under the PartyHUD Canvas and it self-assembles.
    /// </summary>
    public class PartyHudBoosterBar : MonoBehaviour
    {
        [SerializeField] private PartyPlayer localPlayer;
        [SerializeField] private Vector2 anchoredPosition = new Vector2(0f, 100f); // above the bottom edge
        [SerializeField] private Vector2 barSize = new Vector2(360f, 22f);
        [SerializeField] private int hintFontSize = 26;
        [SerializeField] private string hintText = "按住 Shift 加速";
        [SerializeField] private Color fillColor = new Color(0.35f, 0.85f, 1f, 0.95f);
        [SerializeField] private Color bgColor   = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color activeFillColor = new Color(1f, 0.85f, 0.25f, 1f);

        private RectTransform root;
        private Image barBg;
        private Image barFill;
        private TextMeshProUGUI hintLabel;

        private void Awake()
        {
            BuildUI();
            SetVisibleAll(false);
        }

        private void BuildUI()
        {
            // Root container anchored to the bottom-center of whatever Canvas we're under.
            var go = new GameObject("BoosterBarRoot", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            root = (RectTransform)go.transform;
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot     = new Vector2(0.5f, 0f);
            root.anchoredPosition = anchoredPosition;
            root.sizeDelta = new Vector2(barSize.x + 20f, barSize.y + hintFontSize + 20f);

            hintLabel = CreateLabel(root, "Hint", hintText, hintFontSize,
                anchor: new Vector2(0.5f, 1f), pivot: new Vector2(0.5f, 1f),
                offset: new Vector2(0f, 0f), size: new Vector2(barSize.x, hintFontSize + 6f));

            // Bar background
            var bgGo = new GameObject("BarBG", typeof(RectTransform));
            bgGo.transform.SetParent(root, false);
            var bgRT = (RectTransform)bgGo.transform;
            bgRT.anchorMin = new Vector2(0.5f, 0f);
            bgRT.anchorMax = new Vector2(0.5f, 0f);
            bgRT.pivot     = new Vector2(0.5f, 0f);
            bgRT.anchoredPosition = new Vector2(0f, 0f);
            bgRT.sizeDelta = barSize;
            barBg = bgGo.AddComponent<Image>();
            barBg.color = bgColor;
            barBg.raycastTarget = false;

            // Fill (child of BarBG). We keep the RectTransform stretched across the full bar and
            // drive width via localScale.x — Image.Type.Filled requires a source Sprite which we
            // don't ship, and the scale-x path works with a bare colored Image.
            var fillGo = new GameObject("BarFill", typeof(RectTransform));
            fillGo.transform.SetParent(bgGo.transform, false);
            var fillRT = (RectTransform)fillGo.transform;
            fillRT.anchorMin = new Vector2(0f, 0f);
            fillRT.anchorMax = new Vector2(1f, 1f);
            fillRT.pivot     = new Vector2(0f, 0.5f); // scale grows from the left edge
            fillRT.offsetMin = new Vector2(2f, 2f);
            fillRT.offsetMax = new Vector2(-2f, -2f);
            barFill = fillGo.AddComponent<Image>();
            barFill.color = fillColor;
            barFill.raycastTarget = false;
            fillRT.localScale = Vector3.one;
        }

        private RectTransform BarFillRT => barFill != null ? (RectTransform)barFill.transform : null;

        private static TextMeshProUGUI CreateLabel(RectTransform parent, string name, string text, int fontSize,
            Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = pivot;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.enableAutoSizing = false;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        public void SetLocalPlayer(PartyPlayer p) { localPlayer = p; }

        // Client-side smoothed remaining-fuel counter, in fractional seconds. The server stores
        // fuel as whole-second int (SlotSync.durability), which would visibly step the bar 1× per
        // second. Between server ticks we decrement locally with Time.deltaTime while boosting;
        // whenever the server value changes we clamp back to the authoritative reading so drift
        // never lets the local view outrun / lag the truth by more than a second.
        private float smoothedFuel;
        private int lastServerFuel = int.MinValue;

        private void Update()
        {
            if (localPlayer == null)
            {
                // Self-bind to whichever PartyPlayer this client is controlling. Works in solo mode
                // (only one player) and networked mode (IsLocalController is true for our owner).
                foreach (var p in FindObjectsOfType<PartyPlayer>())
                {
                    if (p != null && p.IsLocalController) { localPlayer = p; break; }
                }
            }
            if (localPlayer == null || !localPlayer.BoosterEquipped)
            {
                SetVisibleAll(false);
                return;
            }
            SetVisibleAll(true);

            int serverFuel = localPlayer.BoosterRemainingSeconds;
            int total = localPlayer.BoosterTotalSeconds;
            if (serverFuel != lastServerFuel)
            {
                // Fresh tick from the server. Snap the smoothed value up to the new integer floor
                // so we don't accidentally show more fuel than the server thinks we have; if we
                // were already below the server value (e.g. cache-miss on first frame), take the
                // server number.
                smoothedFuel = Mathf.Min(serverFuel, Mathf.Max(smoothedFuel, serverFuel - 1f));
                if (lastServerFuel == int.MinValue) smoothedFuel = serverFuel;
                lastServerFuel = serverFuel;
            }
            if (localPlayer.BoosterActive)
            {
                smoothedFuel -= Time.deltaTime;
                // Local floor: don't dip below (serverFuel - 1); when the server catches up it'll
                // snap us to `serverFuel` on the next tick. That keeps the bar drifting smoothly
                // without letting it undershoot the true remaining time.
                smoothedFuel = Mathf.Max(smoothedFuel, serverFuel - 1f);
            }
            else
            {
                // Idle: park at the integer value.
                smoothedFuel = serverFuel;
            }
            smoothedFuel = Mathf.Clamp(smoothedFuel, 0f, total);

            if (barFill != null)
            {
                var rt = BarFillRT;
                if (rt != null)
                {
                    var s = rt.localScale;
                    s.x = total > 0 ? Mathf.Clamp01(smoothedFuel / total) : 0f;
                    rt.localScale = s;
                }
                barFill.color = localPlayer.BoosterActive ? activeFillColor : fillColor;
            }
        }

        private void SetVisibleAll(bool v)
        {
            if (root != null && root.gameObject.activeSelf != v) root.gameObject.SetActive(v);
        }
    }
}
