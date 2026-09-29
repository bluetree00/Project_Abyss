using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 화룡 바닥 예고 — <see cref="PatternGuideHelper"/> 데칼(리치 가이드를 화룡 문양으로 바꾼 것)을 감싼다.
/// 원(물기·얼음 강타)과 직선(돌진). 선·채움 색 = 호출자가 준 색(속성 색이 뜻을 가진다).
/// 채움은 <see cref="BeginFill"/>로 차오르고, 판정 순간 <see cref="TransitionToHitPhase"/>로 판정 색이 되어 잠시 남는다.
/// </summary>
public sealed class DragonBossWarningZone : MonoBehaviour
{
    private GameObject _guide;
    private float _lifeTimer;
    private float _fillAnimDuration;
    private float _fillAnimElapsed;
    private bool  _animateFill;

    /// <param name="outlineWidth">예전 선 굵기 — 문양 텍스처가 정하므로 쓰지 않는다(호출부 호환).</param>
    public static DragonBossWarningZone CreateCircle(
        string name,
        Vector3 center,
        float radius,
        Color color,
        float lifetime,
        float heightOffset = 0.05f,
        float outlineWidth = 0.12f,
        bool startEmpty = false)
    {
        var zone = Create(name, lifetime);
        zone.Attach(PatternGuideHelper.Disc(center + Vector3.up * heightOffset, radius, color), color, startEmpty ? 0f : 1f);
        return zone;
    }

    /// <param name="outlineWidth">예전 선 굵기 — 문양 텍스처가 정하므로 쓰지 않는다(호출부 호환).</param>
    public static DragonBossWarningZone CreateRectangle(
        string name,
        Vector3 center,
        Quaternion rotation,
        float width,
        float length,
        Color color,
        float lifetime,
        float heightOffset = 0.05f,
        float outlineWidth = 0.12f)
    {
        var zone = Create(name, lifetime);
        Vector3 forward = rotation * Vector3.forward;
        Vector3 origin  = center - forward * (length * 0.5f) + Vector3.up * heightOffset;
        zone.Attach(PatternGuideHelper.Beam(origin, forward, length, width, color), color, 1f);
        return zone;
    }

    private void Update()
    {
        if (_animateFill)
        {
            _fillAnimElapsed += Time.deltaTime;
            float progress = _fillAnimDuration <= 0f ? 1f : Mathf.Clamp01(_fillAnimElapsed / _fillAnimDuration);
            SetFill(progress);
            if (progress >= 1f)
                _animateFill = false;
        }

        if (_lifeTimer <= 0f)
            return;

        _lifeTimer -= Time.deltaTime;
        if (_lifeTimer <= 0f)
            Destroy(gameObject);
    }

    /// <summary>판정 순간 — 판정 색으로 꽉 채워 밝히고 <paramref name="hitPhaseDuration"/>초 뒤 사라진다.</summary>
    public void TransitionToHitPhase(float hitPhaseDuration)
    {
        _animateFill = false;
        PatternGuideHelper.Arm(_guide);
        _lifeTimer = Mathf.Max(0.01f, hitPhaseDuration);
    }

    /// <summary>비운 채로 시작해 <paramref name="duration"/>초에 걸쳐 차오른다(원은 중심에서 밖으로, 직선은 꼬리에서 머리로).</summary>
    public void BeginFill(float duration)
    {
        _fillAnimDuration = Mathf.Max(0f, duration);
        _fillAnimElapsed = 0f;
        _animateFill = true;
        SetFill(0f);
    }

    public void SetFill(float normalized) => PatternGuideHelper.SetProgress(_guide, normalized);

    private static DragonBossWarningZone Create(string name, float lifetime)
    {
        var root = new GameObject(name);
        var zone = root.AddComponent<DragonBossWarningZone>();
        zone._lifeTimer = lifetime;
        return zone;
    }

    private void Attach(GameObject guide, Color color, float fill)
    {
        _guide = guide;
        _guide.transform.SetParent(transform, true);   // 루트를 지우면 가이드도 같이 사라진다
        PatternGuideHelper.SetFlow(_guide, color);
        SetFill(fill);
    }
}
}
