using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 룸 클리어 게이트 — 클리어 시점에 이펙트 시퀀스를 재생하고 행운치 기반으로 보상 오브젝트를 스폰한다.
///
/// 호출 흐름:
///   1) RoomWaveController → Initialize(run, luckTable, endEffect, endEffect2)
///   2) RoomWaveController.ClearRoomAsync 마지막 단계 → Activate(roomCenter)
///
/// Activate가 수행하는 일:
///   · roomCenter 위치에 EndEffect 재생
///   · 딜레이 후 EndEffect2 스폰 + ClearRewardTrigger 부착 (F키 보상 상호작용)
/// </summary>
public class RoomClearGate : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    // [임시] 드롭 확률 100% 강제. 추후 LuckRollService.TryRollDrop으로 복구할 때 false로 변경.
    private const bool ForceDropAlways = true;

    // ── [SerializeField] ───────────────────────────────────────
    [Header("클리어 이펙트")]
    [SerializeField, Tooltip("방 클리어 시 맵 중앙에 재생할 이펙트 프리팹 (EndEffect).")]
    private GameObject endEffectPrefab;

    [SerializeField, Tooltip("EndEffect 후 스폰되는 보상 오브젝트 이펙트 프리팹 (EndEffect2). ClearRewardTrigger가 자동 부착됨.")]
    private GameObject endEffect2Prefab;

    [SerializeField, Tooltip("EndEffect 스폰 후 EndEffect2 스폰까지의 딜레이(초)."), Min(0f)]
    private float endEffect2SpawnDelay = 2f;

    [Header("Drop Table (옵션 — Initialize에서 주입 권장)")]
    [SerializeField] private LuckRollTableSO luckTable;

    // ── Private fields ─────────────────────────────────────────
    private GameRunSession _run;
    private bool _activated;

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>RoomWaveController에서 호출. run/luckTable/이펙트 프리팹 주입.</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO table,
        GameObject endEffect = null, GameObject endEffect2 = null)
    {
        _run = run;
        if (table      != null) luckTable        = table;
        if (endEffect  != null) endEffectPrefab  = endEffect;
        if (endEffect2 != null) endEffect2Prefab = endEffect2;
    }

    /// <summary>방 클리어 시점에 호출. 이펙트 시퀀스 시작.</summary>
    public void Activate(Vector3 roomCenterWorld)
    {
        if (_activated) return;
        _activated = true;

        PlayClearEffectSequenceAsync(roomCenterWorld).Forget();
    }

    // ── Private Methods ────────────────────────────────────────

    private async UniTaskVoid PlayClearEffectSequenceAsync(Vector3 center)
    {
        var ct = this.GetCancellationTokenOnDestroy();

        if (endEffectPrefab != null)
            Instantiate(endEffectPrefab, center, Quaternion.identity);

        var (itemData, itemSO) = RollRewardItem();
        if (itemData == null) return;

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(endEffect2SpawnDelay), cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        SpawnRewardObject(center, itemData, itemSO);
    }

    private void SpawnRewardObject(Vector3 center, RuntimeItemData itemData, ItemSO itemSO)
    {
        var rewardGO = endEffect2Prefab != null
            ? Instantiate(endEffect2Prefab, center, Quaternion.identity)
            : new GameObject("ClearReward_Fallback");
        rewardGO.transform.position = center;

        var trigger = rewardGO.AddComponent<ClearRewardTrigger>();
        var rewards = new System.Collections.Generic.List<(RuntimeItemData, ItemSO)> { (itemData, itemSO) };
        trigger.Initialize(_run, rewards);
    }

    private (RuntimeItemData data, ItemSO so) RollRewardItem()
    {
        if (luckTable == null)
        {
            Debug.LogWarning("[RoomClearGate] luckTable 미할당 — 아이템 드랍 생략");
            return (null, null);
        }

        int luck = ResolvePlayerLuck();

        if (!ForceDropAlways && !LuckRollService.TryRollDrop(luck, luckTable))
        {
            Debug.Log($"[RoomClearGate] 드롭 확률 미통과 (luck={luck})");
            return (null, null);
        }

        var rarity = LuckRollService.RollRarity(luck, luckTable);
        var candidates = ItemSORegistry.GetByRarity(rarity);
        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning($"[RoomClearGate] 등급 {rarity} 풀 비었음 — 드랍 생략");
            return (null, null);
        }

        var so = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        var data = RuntimeItemData.FromSO(so);
        if (data == null)
        {
            Debug.LogWarning($"[RoomClearGate] RuntimeItemData 변환 실패 — itemId={so?.itemId}");
            return (null, null);
        }

        Debug.Log($"[RoomClearGate] 보상 아이템 선택: {so.itemId} (rarity={rarity}, luck={luck})");
        return (data, so);
    }

    private int ResolvePlayerLuck()
    {
        var player = _run?.Player;
        if (player == null) return 0;
        var stats = player.RuntimeStats;
        return stats != null ? stats.Luck : 0;
    }
}
