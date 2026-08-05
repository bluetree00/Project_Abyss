using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 룸 클리어 게이트 — 클리어 시점에 이펙트 시퀀스를 재생하고 <b>방 종류에 따라</b> 보상 오브젝트를 스폰한다.
///
/// 호출 흐름:
///   1) RoomWaveController → Initialize(run, luckTable, endEffect, endEffect2)
///   2) RoomWaveController.ClearRoomAsync 마지막 단계 → Activate(roomCenter)
///
/// Activate가 수행하는 일:
///   · roomCenter 위치에 EndEffect 재생
///   · 딜레이 후 EndEffect2 스폰 + ClearRewardTrigger 부착 (F키 보상 상호작용)
///
/// 드롭 확률·등급 분포·후보 수·연료는 <see cref="RoomRewardTable"/>(방 종류 기반)가 정한다.
/// 예전엔 행운(Luck) 테이블 하나가 전부를 정했는데, Luck이 사실상 움직이지 않아 정예방이 일반방과
/// 완전히 같은 보상을 줬다(통합설계서 §3-2 결함 F). 행운 계열 코드는 다른 경로에서 계속 쓰이므로 남긴다.
/// </summary>
public class RoomClearGate : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    // 후보 수·원석량은 RoomRewardTable이 방 종류별로 정한다. 챌린지 경로만 후보 수를 고정으로 쓴다.
    /// <summary>이벤트 챌린지 보상 1라운드의 룬 후보 수(3지선다).</summary>
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

    [Header("Drop Table (레거시 — 드롭 굴림에서 분리됨)")]
    // [레거시] 드롭 확률·등급은 이제 RoomRewardTable(방 종류)이 정한다. 이 참조는 주입 시그니처 호환을
    // 위해 남아 있을 뿐 클리어 보상 굴림에는 쓰이지 않는다. Luck 테이블 자체는 상점 진열 등 다른 경로에서 사용 중.
    [SerializeField] private LuckRollTableSO luckTable;

    // ── Private fields ─────────────────────────────────────────
    private GameRunSession _run;
    private bool _activated;
    private bool _isBossRoom;
    private ChallengeGrade? _challengeGrade;   // 이벤트 챌린지 성과(있으면 보상 스케일·연료 지급)
    private bool _isInteraction;               // 상호작용 챌린지 보상이면 천장 클램프(§6-3)

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

    /// <summary>상호작용 챌린지 보상 여부 — true면 개수/연료를 천장 비율로 클램프(§6-3). Activate 전에 호출.</summary>
    public void SetInteractionReward(bool isInteraction) => _isInteraction = isInteraction;

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
        bool isChoice = false;      // 룬 보상은 전부 선택형(3지선다). 보스방은 룬 자체가 없음.
        int  choiceRounds = 1;      // 3지선다를 몇 번 반복할지(챌린지 다중 보상 = 라운드 수)

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
            //
            // 룬 획득은 <b>언제나 3지선다</b>다. 예전엔 여기서만 후보 없이 전부 지급해
            // 레거시 아이템 획득 팝업이 떴다(일반 클리어와 UI가 갈렸다).
            // 보상 '개수'는 그대로 두고, 각 개수를 3지선다 <b>라운드</b>로 바꾼다 —
            // 경제는 유지하면서 획득 경험만 통일된다.
            var cr = ChallengeRewardTable.DefaultReward(_challengeGrade.Value, ChapterNum(), _isInteraction);
            int count = Mathf.Max(1, cr.rewardCount);
            for (int i = 0; i < count; i++)
                rewards.AddRange(RollRewardChoices(RuneChoiceCount, cr.baseRarity));

            choiceRounds = count;
            isChoice     = rewards.Count > 0;

            if (cr.fuelAmount > 0 && _run?.FuelBank != null)
            {
                _run.FuelBank.Add(cr.fuelKind, cr.fuelAmount);   // 연료는 아이템 인벤 밖(RunFuelBank) 직행
                ShowFuelNotice(cr, _challengeGrade.Value);
            }
        }
        else
        {
            // 일반 룸 클리어 = 3지선다. 후보 수·등급 하한·연료를 방 종류가 정한다(§2-2-①·§3-2).
            // 정예방은 여기서 후보 4 + Rare 하한 + 연료 증량으로 갈린다 — "정예를 피하는 게 최적"의 해소 지점.
            var rule = RoomRewardTable.For(RoomKind());
            rewards.AddRange(RollRewardChoices(rule.ChoiceCount, rule.RarityFloor));
            isChoice = rewards.Count > 0;

            // 정제소 연료 — 방 클리어마다 원석 지급(설계 §2.8: 40방 × 4 ≈ 160).
            // 원석은 정제소의 유일한 정규 소비처이므로, 생산이 없으면 정제소 자체가 죽는다.
            GrantClearFuel(rule);
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
            SpawnRewardObject(center, rewards, isChoice, choiceRounds);
        // 일반 방에서 아이템이 없으면 별도 처리 불필요 — 출구 게이트는 RunFlowController가 담당한다.
    }

    private void SpawnRewardObject(Vector3 center, System.Collections.Generic.List<(RuntimeItemData, ItemSO)> rewards, bool isChoice, int choiceRounds)
    {
        var rewardGO = endEffect2Prefab != null
            ? Instantiate(endEffect2Prefab, center, Quaternion.identity)
            : new GameObject("ClearReward_Fallback");
        rewardGO.transform.position = center;
        RoomScopedDrop.Mark(rewardGO);   // 안 주웠으면 방 전환 시 정리(다음 방 잔존 방지)

        var trigger = rewardGO.AddComponent<ClearRewardTrigger>();
        trigger.Initialize(_run, rewards, _isBossRoom, isChoice, choiceRounds);
    }

    /// <summary>방 클리어 연료 지급 — 정제소 원석 + (정예방) 재련소 강화재료. 드랍 판정과 무관하게 확정 지급.</summary>
    private void GrantClearFuel(in RoomRewardTable.Rule rule)
    {
        var bank = _run?.FuelBank;
        if (bank == null) return;

        if (rule.Ore > 0)
        {
            bank.Add(FuelKind.RuneOre, rule.Ore);
            ItemEffectVfxHelper.ShowNotice($"<color=#7FD0FF>원석 +{rule.Ore}</color>  (정제소 연료)");
        }

        if (rule.EnhanceMaterial > 0)
        {
            bank.Add(FuelKind.EnhanceMaterial, rule.EnhanceMaterial);
            ItemEffectVfxHelper.ShowNotice($"<color=#FFB466>강화재료 +{rule.EnhanceMaterial}</color>  (재련소 연료)");
        }
    }

    private (RuntimeItemData data, ItemSO so) RollRewardItem(ItemRarity? floor = null)
    {
        var rule = RoomRewardTable.For(RoomKind());
        if (!RoomRewardTable.RollDrop(rule)) return (null, null);
        return PickItemByRolledRarity(floor, rule.Weights);
    }

    /// <summary>현재 방 종류. 런 세션이 없으면(레거시 단일세계 경로) 일반방으로 본다.</summary>
    private RoomPlanKind RoomKind() => _run?.CurrentRoomKind ?? RoomPlanKind.Normal;

    /// <summary>
    /// 선택 팝업용 후보 N개를 뽑는다. 드랍 판정은 한 번만 하고, 같은 아이템이 겹치지 않도록 중복을 배제한다.
    /// 풀이 후보 수보다 작으면 뽑힌 만큼만 반환한다(호출부가 개수를 확인할 것).
    /// </summary>
    private System.Collections.Generic.List<(RuntimeItemData data, ItemSO so)> RollRewardChoices(
        int count, ItemRarity? floor = null)
    {
        var result = new System.Collections.Generic.List<(RuntimeItemData, ItemSO)>(count);
        var rule   = RoomRewardTable.For(RoomKind());
        if (count <= 0 || !RoomRewardTable.RollDrop(rule)) return result;

        var picked = new System.Collections.Generic.HashSet<string>();
        // 풀이 작아 중복이 반복될 때를 대비한 안전장치(무한 루프 방지).
        int maxAttempts = count * 8;

        for (int attempt = 0; attempt < maxAttempts && result.Count < count; attempt++)
        {
            var (d, s) = PickItemByRolledRarity(floor, rule.Weights);
            if (d == null || s == null) continue;
            if (!picked.Add(s.itemId)) continue;   // 이미 뽑힌 아이템 → 다시 굴림
            result.Add((d, s));
        }

        if (result.Count < count)
            Debug.LogWarning($"[RoomClearGate] 후보 부족 — 요청 {count} / 확보 {result.Count} (아이템 풀 크기 확인 필요)");

        return result;
    }

    /// <summary>등급을 굴려 아이템 1개를 뽑는다(등급 폴백 포함). 드랍 발생 판정은 호출부가 먼저 통과시킨다.</summary>
    private (RuntimeItemData data, ItemSO so) PickItemByRolledRarity(
        ItemRarity? floor, in RoomRewardTable.RarityWeights weights)
    {
        var rarity = RoomRewardTable.RollRarity(weights);
        if (floor.HasValue && rarity < floor.Value) rarity = floor.Value;   // 방 종류·챌린지 등급 rarity 하한

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

        Debug.Log($"[RoomClearGate] 보상 아이템 선택: {so.itemId} (rarity={rarity}, room={RoomKind()})");
        return (data, so);
    }

    /// <summary>[레거시] 플레이어 행운치. 드롭 굴림에서 분리됐다(RoomRewardTable로 이관). 삭제하지 않고 남긴다.</summary>
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
