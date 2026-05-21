using UnityEngine;

/// <summary>
/// 기본 등장 연출. 단일 VFX 프리팹을 착지 지점에 스폰하고 Entrance 트리거를 발사한다.
/// 대부분의 캐릭터는 이 SO에 VFX만 교체해서 사용.
/// </summary>
[CreateAssetMenu(fileName = "DefaultPlayerEntrance", menuName = "RelicFairy/Player/Entrance/Default")]
public sealed class DefaultPlayerEntranceSO : PlayerEntranceBehaviourSO
{
    // ── SerializeField ──
    [Header("VFX")]
    [SerializeField, Tooltip("착지 지점에 스폰할 VFX 프리팹")]
    private GameObject vfxPrefab;

    [Header("Animation")]
    [SerializeField, Tooltip("플레이어 노출 시 발사할 Animator 트리거 이름 (비우면 스킵)")]
    private string animationTrigger = "Entrance";

    // ── Overrides ──

    protected override GameObject OnBegin(PlayerController player, Vector3 spawnPos)
    {
        if (vfxPrefab == null) return null;
        return Instantiate(vfxPrefab, spawnPos, Quaternion.identity);
    }

    protected override void OnActivate(PlayerController player)
    {
        if (string.IsNullOrEmpty(animationTrigger)) return;
        if (player?.Anim == null) return;
        player.Anim.SetTrigger(animationTrigger);
    }
}
