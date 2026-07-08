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

    [SerializeField, Tooltip("클리어 이펙트/보상 오브젝트를 바닥(사망 위치)에서 위로 띄우는 높이(m)."), Min(0f)]
    private float effectHeightOffset = 0.6f;

    [Header("Drop Table (옵션 — Initialize에서 주입 권장)")]
    [SerializeField] private LuckRollTableSO luckTable;

    // ── Private fields ─────────────────────────────────────────
    private GameRunSession _run;
    private bool _activated;
    private bool _isBossRoom;
    private ChallengeGrade? _challengeGrade;   // 이벤트 챌린지 성과(있으면 보상 스케일·연료 지급)

    // ── Public Methods ─────────────────────────────────────────

    /// <summary>RoomWaveController에서 호출. run/luckTable/이펙트 프리팹 주입.</summary>
    public void Initialize(GameRunSession run, LuckRollTableSO table,
        GameObject endEffect = null, GameObject endEffect2 = null, bool isBossRoom = false,
        ChallengeGrade? challengeGrade = null)
    {
        _run            = run;
        _isBossRoom     = isBossRoom;
        _challengeGrade = challengeGrade;
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

        // 바닥(사망 위치)에서 살짝 띄워 이펙트/보상이 지면에 파묻히지 않게 한다.
        center += Vector3.up * effectHeightOffset;

        if (endEffectPrefab != null)
            Instantiate(endEffectPrefab, center, Quaternion.identity);

        var rewards = new System.Collections.Generic.List<(RuntimeItemData, ItemSO)>();

        if (_challengeGrade.HasValue)
        {
            // 이벤트 챌린지 성과 보상 — 등급×챕터로 개수/rarity floor/연료 스케일(§3-5·§4-2).
            var cr = ChallengeRewardTable.DefaultReward(_challengeGrade.Value, ChapterNum(), false);
            int count = Mathf.Max(1, cr.rewardCount);
            for (int i = 0; i < count; i++)
            {
                var (d, s) = RollRewardItem(cr.baseRarity);
                if (d != null) rewards.Add((d, s));
            }
            if (cr.fuelAmount > 0 && _run?.FuelBank != null)
            {
                _run.FuelBank.Add(cr.fuelKind, cr.fuelAmount);   // 연료는 아이템 인벤 밖(RunFuelBank) 직행
                ShowFuelNotice(cr, _challengeGrade.Value);
            }
        }
        else
        {
            var (itemData, itemSO) = RollRewardItem();
            if (itemData != null) rewards.Add((itemData, itemSO));
        }

        // [설계 ④] 보스방: 보스드랍 아이템 보유 시 추가 행운표 롤을 기존 풀에서 append.
        if (_isBossRoom)
        {
            int bonus = _run?.EffectManager?.GetBonusBossDropCount() ?? 0;
            for (int i = 0; i < bonus; i++)
            {
                var (bd, bso) = RollRewardItem();
                if (bd != null) rewards.Add((bd, bso));
            }
            if (bonus > 0) Debug.Log($"[RoomClearGate] 보스드랍 보너스 +{bonus}롤 → 총 보상 {rewards.Count}개");
        }

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(endEffect2SpawnDelay), cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        if (rewards.Count > 0)
            SpawnRewardObject(center, rewards);
        // 아이템이 없으면 별도 처리 불필요 — 절차 진행에선 RunFlowController가 출구 게이트를 담당한다.
    }

    private void SpawnRewardObject(Vector3 center, System.Collections.Generic.List<(RuntimeItemData, ItemSO)> rewards)
    {
        var rewardGO = endEffect2Prefab != null
            ? Instantiate(endEffect2Prefab, center, Quaternion.identity)
            : new GameObject("ClearReward_Fallback");
        rewardGO.transform.position = center;

        var trigger = rewardGO.AddComponent<ClearRewardTrigger>();
        trigger.Initialize(_run, rewards, _isBossRoom);
    }

    private (RuntimeItemData data, ItemSO so) RollRewardItem(ItemRarity? floor = null)
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
        if (floor.HasValue && rarity < floor.Value) rarity = floor.Value;   // 챌린지 등급 rarity 하한
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

    private int ChapterNum() => _run != null ? (int)_run.CurrentChapter : 1;

    private void ShowFuelNotice(ChallengeReward cr, ChallengeGrade grade)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        string fuelName = cr.fuelKind == FuelKind.RuneOre ? "원석" : "강화재료";
        hud?.ShowBuffNotice($"<color=#8fd3ff>{GradeName(grade)}</color> · {fuelName} +{cr.fuelAmount}");
    }

    private static string GradeName(ChallengeGrade g) => g switch
    {
        ChallengeGrade.Platinum => "플래티넘",
        ChallengeGrade.Gold     => "골드",
        ChallengeGrade.Silver   => "실버",
        ChallengeGrade.Bronze   => "브론즈",
        _                       => "실패",
    };
}
