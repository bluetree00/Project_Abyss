using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// 플레이어 등장 연출의 템플릿 메서드 베이스.
/// 카메라 인트로가 끝난 뒤 컨트롤러가 <see cref="ExecuteAsync"/>를 호출하고,
/// 서브클래스는 <see cref="OnBegin"/>, <see cref="OnActivate"/>, <see cref="OnEnd"/> 훅을 오버라이드해
/// VFX / 사운드 / 애니메이션을 캐릭터마다 다르게 정의한다.
/// </summary>
public abstract class PlayerEntranceBehaviourSO : ScriptableObject
{
    // ── Constants ──
    private const float VFX_DESTROY_LINGER = 2f;

    // ── SerializeField ──
    [Header("Timing")]
    [SerializeField, Tooltip("연출 시작 후 플레이어가 보이기까지 대기 시간(초)")]
    protected float activateDelay = 0.8f;

    [SerializeField, Tooltip("연출 전체 길이(초). activateDelay 이후 남은 시간은 VFX 유지 시간")]
    protected float vfxDuration = 1.2f;

    // ── Properties ──
    public float ActivateDelay => activateDelay;
    public float VfxDuration => vfxDuration;

    // ── Public Methods ──

    /// <summary>
    /// 템플릿 메서드. 전체 등장 흐름을 고정 순서로 실행한다.
    /// </summary>
    /// <param name="player">등장할 플레이어</param>
    /// <param name="spawnPos">착지 지점</param>
    /// <param name="setPlayerVisible">플레이어 렌더러 on/off 콜백 (컨트롤러가 제공)</param>
    /// <param name="ct">취소 토큰</param>
    public async UniTask ExecuteAsync(
        PlayerController player,
        Vector3 spawnPos,
        Action<bool> setPlayerVisible,
        CancellationToken ct)
    {
        if (player == null) return;

        // 1단계: 연출 시작 — 서브클래스가 VFX/사운드 스폰
        GameObject vfx = OnBegin(player, spawnPos);
        if (vfx != null)
            DisableLoopOnAllParticles(vfx);

        // 2단계: activateDelay 대기 (카메라 인트로 이후 이펙트 선행 시간)
        if (activateDelay > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(activateDelay), cancellationToken: ct);

        if (player == null) return;

        // 3단계: 플레이어 노출 + 등장 애니메이션
        setPlayerVisible?.Invoke(true);
        OnActivate(player);

        // 4단계: 남은 VFX 시간 대기
        float remaining = vfxDuration - activateDelay;
        if (remaining > 0f)
            await UniTask.Delay(TimeSpan.FromSeconds(remaining), cancellationToken: ct);

        // 5단계: 정리
        OnEnd(player, vfx);
    }

    // ── Hooks (Override per character) ──

    /// <summary>VFX/사운드 스폰. 반환된 GameObject의 ParticleSystem은 loop이 자동 해제된다.</summary>
    protected abstract GameObject OnBegin(PlayerController player, Vector3 spawnPos);

    /// <summary>플레이어가 화면에 나타나는 순간 호출. 기본은 Entrance 트리거 발사.</summary>
    protected virtual void OnActivate(PlayerController player)
    {
        if (player?.Anim == null) return;
        player.Anim.SetTrigger("Entrance");
    }

    /// <summary>연출 종료 직전 호출. 기본은 VFX 2초 후 파괴.</summary>
    protected virtual void OnEnd(PlayerController player, GameObject vfx)
    {
        if (vfx != null)
            Destroy(vfx, VFX_DESTROY_LINGER);
    }

    // ── Utilities ──

    /// <summary>모든 자식 ParticleSystem의 loop을 강제 해제 (1회 재생 보장).</summary>
    protected static void DisableLoopOnAllParticles(GameObject root)
    {
        if (root == null) return;
        var systems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            var main = systems[i].main;
            main.loop = false;
        }
    }
}
