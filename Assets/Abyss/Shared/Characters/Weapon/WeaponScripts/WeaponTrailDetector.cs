using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무기 프리팹에 자동 부착되는 트레일 판정 컴포넌트.
/// BeginTrail() ~ EndTrail() 사이 매 FixedUpdate에서
/// Root→Tip SphereCast로 적 히트를 감지한다.
/// </summary>
public class WeaponTrailDetector : MonoBehaviour
{
    public event Action<RaycastHit, float, float> OnTrailHit; // hit, damage, knockback

    private WeaponInstance _weapon;
    private bool _active;

    private Vector3 _prevTipPos;
    private Vector3 _prevRootPos;
    private bool _firstFrame;

    private float _damage;
    private float _knockbackMultiplier;
    private float _radiusOverride;

    // 한 스윙에서 이미 히트한 콜라이더 중복 방지
    private readonly HashSet<Collider> _hitSet = new HashSet<Collider>();

    // NonAlloc 버퍼 (GC 없음)
    private static readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    public void Setup(WeaponInstance weapon)
    {
        _weapon = weapon;
    }

    /// <summary>
    /// 트레일 판정 시작. AE_BeginTrail에서 호출.
    /// </summary>
    public void BeginTrail(float damage, float knockbackMultiplier, float radiusOverride = 0f)
    {
        if (_weapon == null) return;

        _damage = damage;
        _knockbackMultiplier = knockbackMultiplier;
        _radiusOverride = radiusOverride;
        _hitSet.Clear();
        _firstFrame = true;
        _active = true;
    }

    /// <summary>
    /// 트레일 판정 종료. AE_EndTrail에서 호출.
    /// </summary>
    public void EndTrail()
    {
        _active = false;
        _hitSet.Clear();
    }

    private void FixedUpdate()
    {
        if (!_active || _weapon == null) return;

        Transform tip = _weapon.tipPoint;
        Transform root = _weapon.rootPoint;

        if (tip == null || root == null) return;

        Vector3 currentTip = tip.position;
        Vector3 currentRoot = root.position;

        // 첫 프레임은 이전 위치 초기화만
        if (_firstFrame)
        {
            _prevTipPos = currentTip;
            _prevRootPos = currentRoot;
            _firstFrame = false;
            return;
        }

        float radius = _radiusOverride > 0f ? _radiusOverride : _weapon.hitRadius;

        // Root → Tip 방향으로 SphereCast (무기 전체 길이 커버)
        Vector3 dir = currentTip - currentRoot;
        float length = dir.magnitude;
        if (length < 0.001f) return;

        int count = Physics.SphereCastNonAlloc(
            currentRoot,
            radius,
            dir.normalized,
            _hitBuffer,
            length,
            _weapon.hitLayer
        );

        for (int i = 0; i < count; i++)
        {
            var col = _hitBuffer[i].collider;
            if (col == null) continue;
            if (_hitSet.Contains(col)) continue; // 중복 히트 방지

            _hitSet.Add(col);
            OnTrailHit?.Invoke(_hitBuffer[i], _damage, _knockbackMultiplier);
        }

        _prevTipPos = currentTip;
        _prevRootPos = currentRoot;
    }

    private void OnDrawGizmos()
    {
        // 에디터 비플레이 상태에서도 동작하도록 직접 조회
        var weapon = _weapon != null ? _weapon : GetComponent<WeaponInstance>();
        if (weapon == null) return;

        Transform tip = weapon.tipPoint;
        Transform root = weapon.rootPoint;

        if (tip == null || root == null) return;

        float radius = (_radiusOverride > 0f ? _radiusOverride : weapon.hitRadius);

        if (_active)
        {
            // 판정 활성 중: 빨간색
            Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(root.position, radius);
            Gizmos.DrawWireSphere(tip.position, radius);
            Gizmos.DrawLine(root.position, tip.position);
        }
        else
        {
            // 비활성: 회색
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.4f);
            Gizmos.DrawWireSphere(root.position, radius);
            Gizmos.DrawWireSphere(tip.position, radius);
            Gizmos.DrawLine(root.position, tip.position);
        }

        // Root/Tip 라벨 (에디터 전용)
#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(root.position + Vector3.up * 0.05f, "Root");
        UnityEditor.Handles.Label(tip.position + Vector3.up * 0.05f, "Tip");
#endif
    }
}
