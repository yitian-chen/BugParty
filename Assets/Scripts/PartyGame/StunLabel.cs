using UnityEngine;
using UnityEngine.UI;

namespace PartyGame
{
    /// <summary>
    /// Shows a floating "眩晕" text above the player while stunned.
    ///
    /// Previously this used a prefab-attached TextMeshPro with the "ICE SDF" font — which does
    /// not contain the character 眩 (or many other CJK glyphs) and rendered as a tofu box.
    /// TMP fallback tables in this project also don't cover it, and importing a full CJK
    /// TMP atlas is heavyweight.
    ///
    /// This rewrite drops the prefab reference entirely and builds a world-space Canvas + Legacy
    /// UI Text at runtime, using `Microsoft YaHei` / `SimHei` via `Font.CreateDynamicFontFromOSFontNames`.
    /// Dynamic OS fonts rasterize on demand and cover the full CJK range, so 眩晕 (and any future
    /// Chinese string we throw at the label) just works. Font size 一并调大到 easy-to-read。
    ///
    /// Non-owner instances still see it — driven purely by netStunTimer.
    /// </summary>
    public class StunLabel : MonoBehaviour
    {
        [SerializeField] private PartyPlayer owner;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Vector3 offset = new Vector3(0f, 3.2f, 0f);
        [SerializeField] private int fontSize = 64;
        [SerializeField] private string message = "眩晕";
        [SerializeField] private Color textColor = new Color(1f, 0.85f, 0.15f, 1f);

        private Canvas canvas;
        private RectTransform labelRT;
        private Text label;
        private Outline outline;

        private void Awake()
        {
            if (owner == null) owner = GetComponentInParent<PartyPlayer>();
            if (followTarget == null) followTarget = owner != null ? owner.transform : transform.parent;
            EnsureVisual();
            SetVisible(false);
        }

        private void EnsureVisual()
        {
            if (canvas != null) return;

            // World-space canvas parented under this transform — moves with the label GameObject,
            // which we reposition to follow the player each frame.
            var canvasGO = new GameObject("StunLabelCanvas");
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            canvasGO.AddComponent<CanvasScaler>();

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(canvasGO.transform, false);
            labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.sizeDelta = new Vector2(600f, 200f);
            // Shrink the canvas' world scale so a 64pt UI font renders at a nice ~1.2m tall size.
            canvasGO.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            label = labelGO.AddComponent<Text>();
            label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontStyle = FontStyle.Bold;
            label.fontSize = fontSize;
            label.color = textColor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = message;
            // Dynamic OS font: covers every CJK glyph without shipping a TTF atlas.
            label.font = LoadCjkFont();

            outline = labelGO.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(3f, -3f);
        }

        private static Font LoadCjkFont()
        {
            // Try common Windows CJK families first; TryDynamicFont on Windows will rasterize any glyph
            // that exists in the OS font. Fall back to Unity's built-in Arial (which uses OS fonts on
            // Windows and covers CJK there too).
            var f = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun", "Arial Unicode MS", "Arial" },
                48);
            if (f != null) return f;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void LateUpdate()
        {
            if (followTarget != null) transform.position = followTarget.position + offset;
            bool show = owner != null && owner.IsStunned;
            SetVisible(show);

            var cam = Camera.main;
            if (cam != null && canvas != null)
                canvas.transform.rotation = cam.transform.rotation;
        }

        private void SetVisible(bool v)
        {
            if (canvas != null && canvas.gameObject.activeSelf != v)
                canvas.gameObject.SetActive(v);
        }
    }
}
