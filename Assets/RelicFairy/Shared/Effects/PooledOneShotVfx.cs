using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 풀링되는 1회용 파티클 VFX. 스폰 직후 Play(lifetime[, scale]) 호출 →
/// 파티클을 처음부터 재생하고 lifetime 경과 시 ObjectPooler.Despawn으로 반환한다.
///
/// 풀 재사용 안전장치:
///  • 스케일은 Awake에서 캡처한 프리팹 원본(_baseScale) 기준으로 매번 절대 설정 → 곱연산 누적 방지
///  • Play마다 파티클 Clear+Play, OnDespawn에서 StopEmittingAndClear → 스테일 파티클 방지
///  • _playToken으로 진행 중 ReturnAfter가 재사용된 인스턴스를 조기 반환하지 않도록 무효화
///
/// 호출측이 풀 스폰 직후 AddComponent(자동) — 프리팹 데이터/에셋 변경 없음.
/// </summary>
public sealed class PooledOneShotVfx : MonoBehaviour, IPooledObject
{
    private ParticleSystem[] _systems;
    private Vector3 _baseScale = Vector3.one;
    private int _playToken;

    private void Awake()
    {
        _baseScale = transform.localScale;                  // 프리팹 원본 스케일
        _systems   = GetComponentsInChildren<ParticleSystem>(true);
    }

    /// <summary>스케일을 변경하지 않고 재생한다(호출측이 스케일을 직접 설정하는 경우 — 예: 히트 VFX).</summary>
    public void Play(float lifetime) => PlayInternal(lifetime, applyScale: false, 1f);

    /// <summary>스케일을 프리팹 원본 × scaleMultiplier로 설정한 뒤 재생한다(곱연산 누적 방지 — 예: 스폰 VFX).</summary>
    public void Play(float lifetime, float scaleMultiplier) => PlayInternal(lifetime, applyScale: true, scaleMultiplier);

    private void PlayInternal(float lifetime, bool applyScale, float scaleMultiplier)
    {
        if (applyScale) transform.localScale = _baseScale * scaleMultiplier;

        int token = ++_playToken;
        if (_systems != null)
        {
            for (int i = 0; i < _systems.Length; i++)
            {
                var main = _systems[i].main;
                main.loop = false;                          // 풀 재사용 시 무한 루프 방지
                _systems[i].Clear(true);
                _systems[i].Play(true);
            }
        }
        ReturnAfterAsync(token, Mathf.Max(0.05f, lifetime)).Forget();
    }

    public void OnSpawn(object param = null) { }

    public void OnDespawn()
    {
        _playToken++;                                       // 진행 중 ReturnAfter 무효화
        if (_systems == null) return;
        for (int i = 0; i < _systems.Length; i++)
            _systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private async UniTaskVoid ReturnAfterAsync(int token, float lifetime)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(lifetime),
                cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException) { return; }

        if (token != _playToken || this == null) return;    // 그 사이 재사용/파괴됐으면 무시
        Managers.ObjectPooler?.Despawn(gameObject);
    }
}
