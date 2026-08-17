using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// World-space progress bar rendered above the player's head while fishing.
    /// Driven by polling `owner.ActiveFishing` every frame — no event subscription
    /// so we're immune to Awake/OnEnable ordering issues.
    /// </summary>
    public class FishingProgressBar : MonoBehaviour
    {
        [SerializeField] private PartyPlayer owner;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Transform fillTransform;
        [SerializeField] private Transform barRoot;
        [SerializeField] private Vector3 offset = new Vector3(0, 2.3f, 0);
        [SerializeField] private float maxFillWidth = 1.5f;

        private float progress;
        private bool visible;
        private float progressCache;
        private FishingAction subscribedAction;

        private void Awake()
        {
            SetVisible(false);
        }

        private void OnDisable()
        {
            if (subscribedAction != null)
            {
                subscribedAction.OnProgressChanged -= HandleProgressChanged;
                subscribedAction = null;
            }
        }

        public void Bind(PartyPlayer p, Transform target)
        {
            owner = p;
            followTarget = target;
        }

        private void Update()
        {
            FishingAction current = owner != null ? owner.ActiveFishing : null;
            if (current != subscribedAction)
            {
                if (subscribedAction != null) subscribedAction.OnProgressChanged -= HandleProgressChanged;
                subscribedAction = current;
                progressCache = 0f;
                if (subscribedAction != null) subscribedAction.OnProgressChanged += HandleProgressChanged;
            }

            // Direct set (no `!= visible` check) so if something external re-enables barRoot we still reconcile.
            SetVisible(current != null);
        }

        private void LateUpdate()
        {
            if (followTarget != null)
            {
                transform.position = followTarget.position + offset;
            }

            FishingAction current = owner != null ? owner.ActiveFishing : null;
            if (visible && current != null)
            {
                progress = current.ProgressNormalized;
                ApplyFill();
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                // Fully face the camera (billboard).
                transform.rotation = cam.transform.rotation;
            }
        }

        private void HandleProgressChanged(object sender, IHasProgress.OnProgressChangedEventArgs e)
        {
            progressCache = e.progressNormalized;
        }

        private void ApplyFill()
        {
            if (fillTransform == null) return;
            float w = Mathf.Clamp01(progress) * maxFillWidth;

            if (fillTransform.childCount > 0)
            {
                Transform quad = fillTransform.GetChild(0);
                Vector3 s = quad.localScale;
                s.x = w;
                quad.localScale = s;
                Vector3 lp = quad.localPosition;
                lp.x = w * 0.5f;
                quad.localPosition = lp;
            }
            else
            {
                Vector3 s = fillTransform.localScale;
                s.x = w;
                fillTransform.localScale = s;
            }
        }

        private void SetVisible(bool v)
        {
            visible = v;
            GameObject target = null;
            if (barRoot != null && barRoot != transform) target = barRoot.gameObject;
            else if (fillTransform != null && fillTransform.parent != null && fillTransform.parent != transform) target = fillTransform.parent.gameObject;
            if (target != null && target.activeSelf != v) target.SetActive(v);
        }
    }
}
