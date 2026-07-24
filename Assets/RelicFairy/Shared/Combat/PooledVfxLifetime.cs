using UnityEngine;

/// <summary>
/// 풀에서 나온 VFX가 <b>스스로</b> 수명을 다하면 풀로 돌아가게 하는 안전망.
///
/// 히트 이펙트 프리팹에 <see cref="EffectBehaviour"/>가 없으면 아무도 회수해주지 않아
/// 월드에 영구히 남는다(무한 지속 피격 이펙트). 그렇다고 스폰한 쪽에서 타이머를 들고 있으면
/// <b>풀 재사용과 충돌</b>한다 — 그 인스턴스가 먼저 반환됐다가 다른 히트로 다시 나간 뒤에
/// 예전 타이머가 깨어나 남의 이펙트를 꺼버린다.
///
/// 수명을 인스턴스 자신이 들고 <see cref="OnEnable"/>에서 경과를 0으로 되돌리면,
/// 풀에서 다시 나올 때마다 타이머가 자연히 리셋되어 그 문제가 <b>구조적으로 사라진다.</b>
/// </summary>
public sealed class PooledVfxLifetime : MonoBehaviour
{
    private const float MinLife = 0.05f;

    private float _life = 1f;
    private float _age;

    /// <summary>수명(초) 설정. 스폰 직후 호출한다.</summary>
    public void SetLife(float seconds) => _life = Mathf.Max(MinLife, seconds);

    // 풀에서 재활성화될 때마다 경과 리셋 — 이전 사용분의 나이가 이월되지 않는다.
    private void OnEnable() => _age = 0f;

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age < _life) return;

        _age = 0f;
        Managers.ObjectPooler?.Despawn(gameObject);
    }
}
