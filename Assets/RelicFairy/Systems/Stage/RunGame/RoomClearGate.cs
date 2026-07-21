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

    /// <summary>일반 룸 클리어 시 제시할 룬 후보 수(3지선다).</summary>
    private const int RuneChoiceCount = 3;

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
        bool isChoice = false;   // 일반 클리어만 선택형(3지선다). 챌린지는 전부 지급, 보스는 룬 자체가 없음.

        if (_isBossRoom)
        {
            // 보스 보상은 룬이 아니라 '유물 파츠 드래프트(3지선다)'가 담당한다.
            // 여기서는 아무 보상도 굴리지 않는다. 단, 보상 트리거는 아래에서 반드시 스폰해야 한다
            // — 최종 보스 런 클리어·챕터 전환 처리가 ClearRewardTrigger에 달려 있기 때문.
            Debug.Log("[RoomClearGate] 보스방 — 룬 보상 없음(유물 파츠 드래프트 담당)");
        }
        else if (_challengeGrade.HasValue)
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
            // 일반 룸 클리어 = 3지선다. 후보를 담고 ClearRewardTrigger에 '선택형'으로 넘긴다.
            rewards.AddRange(RollRewardChoices(RuneChoiceCount));
            isChoice = rewards.Count > 0;
        }

        // [보류] 보스드랍 아이템(EffectManager.GetBonusBossDropCount)의 '보스방 추가 롤' 보너스.
        //        보스 보상이 룬 → 유물 파츠 드래프트로 바뀌면서 얹을 대상이 사라져 호출을 끊었다.
        //        효과를 어디로 옮길지(드래프트 후보 +1 등) 정해지면 되살리거나 정리할 것. 코드는 남겨둔다.
        // if (_isBossRoom)
        // {
        //     int bonus = _run?.EffectManager?.GetBonusBossDropCount() ?? 0;
        //     for (int i = 0; i < bonus; i++)
        //     {
        //         var (bd, bso) = RollRewardItem();
        //         if (bd != null) rewards.Add((bd, bso));
        //     }
        // }

        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(endEffect2SpawnDelay), cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }

        // 보스방은 룬 보상이 없고(위에서 미굴림) 클리어 후처리(드래프트·런클리어·길)를
        // GameRunBootstrapper.OnBossRoomClearedHandler가 전담하므로 트리거를 스폰하지 않는다.
        if (rewards.Count > 0)
            SpawnRewardObject(center, rewards, isChoice);
        // 일반 방에서 아이템이 없으면 별도 처리 불필요 — 출구 게이트는 RunFlowController가 담당한다.
    }

    private void SpawnRewardObject(Vector3 center, System.Collections.Generic.List<(RuntimeItemData, ItemSO)> rewards, bool isChoice)
    {
        var rewardGO = endEffect2Prefab != null
            ? Instantiate(endEffect2Prefab, center, Quaternion.identity)
            : new GameObject("ClearReward_Fallback");
        rewardGO.transform.position = center;
        RoomScopedDrop.Mark(rewardGO);   // 안 주웠으면 방 전환 시 정리(다음 방 잔존 방지)

        var trigger = rewardGO.AddComponent<ClearRewardTrigger>();
        trigger.Initialize(_run, rewards, _isBossRoom, isChoice);
    }

    private (RuntimeItemData data, ItemSO so) RollRewardItem(ItemRarity? floor = null)
    {
        if (!PassDropGate()) return (null, null);
        return PickItemByRolledRarity(floor);
    }

    /// <summary>
    /// 선택 팝업용 후보 N개를 뽑는다. 드랍 판정은 한 번만 하고, 같은 아이템이 겹치지 않도록 중복을 배제한다.
    /// 풀이 후보 수보다 작으면 뽑힌 만큼만 반환한다(호출부가 개수를 확인할 것).
    /// </summary>
    private System.Collections.Generic.List<(RuntimeItemData data, ItemSO so)> RollRewardChoices(
        int count, ItemRarity? floor = null)
    {
        var result = new System.Collections.Generic.List<(RuntimeItemData, ItemSO)>(count);
        if (count <= 0 || !PassDropGate()) return result;

        var picked = new System.Collections.Generic.HashSet<string>();
        // 풀이 작아 중복이 반복될 때를 대비한 안전장치(무한 루프 방지).
        int maxAttempts = count * 8;

        for (int attempt = 0; attempt < maxAttempts && result.Count < count; attempt++)
        {
            var (d, s) = PickItemByRolledRarity(floor);
            if (d == null || s == null) continue;
            if (!picked.Add(s.itemId)) continue;   // 이미 뽑힌 아이템 → 다시 굴림
            result.Add((d, s));
        }

        if (result.Count < count)
            Debug.LogWarning($"[RoomClearGate] 후보 부족 — 요청 {count} / 확보 {result.Count} (아이템 풀 크기 확인 필요)");

        return result;
    }

    /// <summary>드랍 자체가 발생하는지(행운·드랍확률) 판정. 후보를 여러 개 뽑을 때도 이 관문은 1회만 통과한다.</summary>
    private bool PassDropGate()
    {
        if (luckTable == null)
        {
            Debug.LogWarning("[RoomClearGate] luckTable 미할당 — 아이템 드랍 생략");
            return false;
        }

        if (!ForceDropAlways && !LuckRollService.TryRollDrop(ResolvePlayerLuck(), luckTable))
        {
            Debug.Log($"[RoomClearGate] 드롭 확률 미통과 (luck={ResolvePlayerLuck()})");
            return false;
        }

        return true;
    }

    /// <summary>등급을 굴려 아이템 1개를 뽑는다(등급 폴백 포함). 드랍 판정은 <see cref="PassDropGate"/>가 담당.</summary>
    private (RuntimeItemData data, ItemSO so) PickItemByRolledRarity(ItemRarity? floor)
    {
        int luck = ResolvePlayerLuck();
        var rarity = LuckRollService.RollRarity(luck, luckTable);
        if (floor.HasValue && rarity < floor.Value) rarity = floor.Value;   // 챌린지 등급 rarity 하한

        // 등급 폴백 — 굴린 등급에 보유 아이템이 없을 수 있다(예: 아이템 풀이 Common/Rare뿐인데
        // LuckRollTable은 luck 1부터 Epic/Legendary를 굴린다). 드랍을 통째로 날리는 대신 한 단계씩
        // 낮춰 실제 보유한 등급을 찾는다. 상위 등급 아이템이 추가되면 폴백 없이 자연히 그 등급이 나온다.
        var rolledRarity = rarity;
        var candidates = ItemSORegistry.GetByRarity(rarity);
        while ((candidates == null || candidates.Count == 0) && rarity > ItemRarity.Common)
        {
            rarity--;
            candidates = ItemSORegistry.GetByRarity(rarity);
        }

        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning($"[RoomClearGate] 모든 등급 풀이 비었음 — 드랍 생략 (굴림={rolledRarity})");
            return (null, null);
        }

        if (rarity != rolledRarity)
            Debug.Log($"[RoomClearGate] 등급 폴백 {rolledRarity} → {rarity} (해당 등급 풀 비어 하향)");

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
