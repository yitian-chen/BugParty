using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>世界空间悬浮进度条。纯代码生成。</summary>
    public class FloatingBar : MonoBehaviour
    {
        [Header("跟随")]
        public Transform followTarget;
        public Vector3 worldOffset = Vector3.up * 1.4f;

        Transform _fill;
        Renderer _fillRenderer;
        Camera _cam;

        const float Width = 1.0f;
        const float Height = 0.14f;

        /// <summary>创建进度条。刻意不设父级，避免继承目标的非等比缩放。</summary>
        public static FloatingBar Create(Transform target, Vector3 worldOffset)
        {
            var root = new GameObject("FloatingBar_" + (target != null ? target.name : "None"));
            root.transform.localScale = Vector3.one;

            var bar = root.AddComponent<FloatingBar>();
            bar.followTarget = target;
            bar.worldOffset = worldOffset;

            if (target != null) root.transform.position = target.position + worldOffset;

            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "BG";
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(Width, Height, 1f);
            SafeDestroy(bg.GetComponent<Collider>());
            SetColorOn(bg.GetComponent<Renderer>(), new Color(0.06f, 0.06f, 0.08f, 1f));

            var pivot = new GameObject("FillPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(-Width * 0.5f, 0f, -0.01f);
            pivot.transform.localScale = new Vector3(0f, Height * 0.78f, 1f);

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(pivot.transform, false);
            fill.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            SafeDestroy(fill.GetComponent<Collider>());
            bar._fillRenderer = fill.GetComponent<Renderer>();
            SetColorOn(bar._fillRenderer, Color.white);

            bar._fill = pivot.transform;
            return bar;
        }

        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static void SetColorOn(Renderer r, Color c)
        {
            if (r == null) return;
            var m = r.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void LateUpdate()
        {
            if (followTarget == null) { Destroy(gameObject); return; }

            transform.position = followTarget.position + worldOffset;

            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
                transform.rotation = Quaternion.LookRotation(
                    transform.position - _cam.transform.position, Vector3.up);
        }

        public void SetFill(float t)
        {
            if (_fill == null) return;
            var s = _fill.localScale;
            s.x = Mathf.Clamp01(t) * Width;
            _fill.localScale = s;
        }

        public void SetColor(Color c) => SetColorOn(_fillRenderer, c);

        public void SetVisible(bool v)
        {
            if (gameObject.activeSelf != v) gameObject.SetActive(v);
        }
    }
}
