using UnityEngine;

namespace RelicFairy.Monster
{
[DisallowMultipleComponent]
internal sealed class VfxFadeIn : MonoBehaviour
{
    private Vector3 _targetScale;
    private float   _duration;
    private float   _elapsed;

    internal void Init(Vector3 targetScale, float duration)
    {
        _targetScale         = targetScale;
        _duration            = duration;
        transform.localScale = Vector3.zero;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / _duration));
        transform.localScale = _targetScale * t;
        if (_elapsed >= _duration)
            Destroy(this);
    }
}
}
