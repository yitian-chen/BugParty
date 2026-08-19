using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>Bug 会议室氛围：家具悬浮 + 随机故障闪烁。</summary>
    public class BugAmbience : MonoBehaviour
    {
        [Header("悬浮")]
        public float bobAmplitude = 0.12f;
        public float bobSpeed = 1.1f;
        public float driftSpin = 4f;

        [Header("故障闪烁")]
        public bool enableGlitch = true;
        public float glitchInterval = 3.5f;
        public float glitchDuration = 0.09f;
        public float glitchOffset = 0.14f;
        public Color glitchColor = new Color(0.25f, 0.7f, 1f);

        Vector3 _basePos;
        float _phase;
        float _nextGlitch;
        float _glitchEnd;
        Renderer _renderer;
        Color _baseColor;
        bool _hasColor;
        bool _glitching;

        void Start()
        {
            _basePos = transform.localPosition;
            _phase = Random.Range(0f, Mathf.PI * 2f);
            _nextGlitch = Time.time + Random.Range(0.5f, glitchInterval);

            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null)
            {
                var m = _renderer.material;
                if (m.HasProperty("_BaseColor")) { _baseColor = m.GetColor("_BaseColor"); _hasColor = true; }
                else if (m.HasProperty("_Color")) { _baseColor = m.GetColor("_Color"); _hasColor = true; }
            }
        }

        void Update()
        {
            var p = _basePos;
            p.y += Mathf.Sin(Time.time * bobSpeed + _phase) * bobAmplitude;

            if (enableGlitch)
            {
                if (!_glitching && Time.time >= _nextGlitch)
                {
                    _glitching = true;
                    _glitchEnd = Time.time + glitchDuration;
                    ApplyGlitchColor(true);
                }
                else if (_glitching && Time.time >= _glitchEnd)
                {
                    _glitching = false;
                    _nextGlitch = Time.time + glitchInterval * Random.Range(0.55f, 1.6f);
                    ApplyGlitchColor(false);
                }

                if (_glitching)
                {
                    p.x += Random.Range(-glitchOffset, glitchOffset);
                    p.z += Random.Range(-glitchOffset, glitchOffset);
                }
            }

            transform.localPosition = p;

            if (driftSpin != 0f)
                transform.Rotate(Vector3.up, driftSpin * Time.deltaTime, Space.Self);
        }

        void ApplyGlitchColor(bool on)
        {
            if (_renderer == null || !_hasColor) return;
            var m = _renderer.material;
            var c = on ? Color.Lerp(_baseColor, glitchColor, 0.75f) : _baseColor;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}
