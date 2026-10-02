using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;

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
///   · 보상을 세운 방은 그 보상이 사라질 때까지(수령·넘기기) 출구 공개를 묶는다 — 룬을 지나칠 수 없다
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
    /// <summary>첫 장을 보유 계열로 기울일 확률 — 늘 같은 계열만 나오면 두 번째 축을 만날 자리가 없다.</summary>
    private const float PreferFamilyChance = 0.6f;
    /// <summary>
    /// 보상 자리에서 걸을 수 있는 면을 찾는 반경(m). 보상 획득 반경(4.5 m)보다 작게 둔다 —
    /// 이 안에서 찾은 면이면 보상을 그리 옮겨도 원래 자리에서 멀지 않다. 못 찾으면 플레이어 자리로 간다.
    /// </summary>
    private const float RewardSnapRadius = 4f;
    // NavMesh 질의 조건 — 에이전트 종류를 밝힌다. 방 NavMesh는 에이전트 종류마다 따로 굽는데(BuildMapNavMeshAsync, 2종),
    // 종류를 안 밝힌 질의(areaMask 오버로드)는 다른 종류의 면을 집어 발밑 면까지도 길이 끊긴 것(PathPartial)으로 나온다(10-01 실측).
    private static readonly NavMeshQueryFilter s_walkFilter = new() { agentTypeID = 0, areaMask = NavMesh.AllAreas };
    /// <summary>보상을 안 받은 채 이만큼(초) 지나면 「보상을 받으면 길이 열린다」를 띄운다 — 문이 왜 안 열리는지 알려 준다.</summary>
    private const float ExitHintDelay  = 6f;
    /// <summary>그 뒤로도 안 받으면 되풀이하는 간격(초). 알림이 2.5초 떠 있으니 도배가 되지 않게 넉넉히.</summary>
    private const float ExitHintRepeat = 12f;
    private static readonly System.Collections.Generic.List<BuildFamily> s_topFamilies = new(2);
    private static readonly System.Collections.Generic.List<ItemSO>      s_familyPool  = new(16);

    // ── [SerializeField] ───────────────────────────────────────
    [Header("클리어 이펙트")]
    [SerializeField, Tooltip("방 클리어 시 맵 중앙에 재생할 이펙트 프리팹 (EndEffect).")]
    private GameObject endEffectPrefab;

    [SerializeField, Tooltip("EndEffect 후 스폰되는 보상 오브젝트 이펙트 프리팹 (EndEffect2). ClearRewardTrigger가 자동 부착됨.")]
    private GameObject endEffect2Prefab;

    [SerializeField, Tooltip("보상이 없는 방(보스방 등)의 클리어 뒤 대기(초). 보상이 있는 방은 등급 예고 길이(RewardPresentation.World)가 대신한다."), Min(0f)]
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
    private float _fuelScale = 1f;             // 챌린지 연료 배율(이벤트방 놀이 — 욕심의 상자 4단계 ×1.5)
    private bool _restoreMode;                 // 이어하기 복원 굴림(연료 재지급 금지 + 후보 결정적 고정)
    private int  _restoreSeed;

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

    /// <summary>챌린지 연료 배율 — Activate 전에 호출.</summary>
    public void SetFuelScale(float scale) => _fuelScale = Mathf.Max(0f, scale);

    /// <summary>방 클리어 시점에 호출. 이펙트 시퀀스 시작.</summary>
    public void Activate(Vector3 roomCenterWorld)
    {
        if (_activated) return;
        _activated = true;

        PlayClearEffectSequenceAsync(roomCenterWorld).Forget();
    }

    /// <summary>
    /// 이어하기 복원 전용 — 저장 당시 "클리어했지만 아직 [F]로 안 받은" 보상만 다시 세운다.
    ///
    /// 일반 Activate와 두 가지가 다르다:
    ///   · 연료(원석/강화재료)를 <b>재지급하지 않는다</b> — 클리어 시점에 이미 은행에 들어가 저장됐다.
    ///   · 후보 굴림을 rerollSeed로 고정한다 — 재접속을 반복해도 같은 3지선다가 나와 save-scum이 막힌다.
    /// </summary>
    public void ActivateRestoredReward(Vector3 roomCenterWorld, int rerollSeed)
    {
        if (_activated) return;
        _activated   = true;
        _restoreMode = true;
        _restoreSeed = rerollSeed;

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
            // 여기서는 아무 보상도 굴리지 않는다(보상 트리거도 스폰하지 않는다 — 아래 스폰 지점 주석 참조).
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
            cr.fuelAmount = Mathf.RoundToInt(cr.fuelAmount * _fuelScale);
            int count = Mathf.Max(1, cr.rewardCount);
            for (int i = 0; i < count; i++)
                rewards.AddRange(RollRewardChoices(RuneChoiceCount, cr.baseRarity, floorGuaranteesOneOnly: true));

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

            if (_restoreMode)
            {
                // 복원 굴림: 전역 Random 상태를 방 시드로 잠깐 갈아끼워 후보를 결정적으로 뽑고 되돌린다.
                // (되돌리지 않으면 이후 전투/연출의 난수까지 이 시드에 묶인다.)
                var prevState = UnityEngine.Random.state;
                UnityEngine.Random.InitState(_restoreSeed);
                rewards.AddRange(RollRewardChoices(rule.ChoiceCount, rule.RarityFloor));
                UnityEngine.Random.state = prevState;
            }
            else
            {
                rewards.AddRange(RollRewardChoices(rule.ChoiceCount, rule.RarityFloor));

                // 정제소 연료 — 방 클리어마다 원석 지급(설계 §2.8: 40방 × 4 ≈ 160).
                // 원석은 정제소의 유일한 정규 소비처이므로, 생산이 없으면 정제소 자체가 죽는다.
                // 복원 경로에서는 지급하지 않는다 — 클리어 시점에 이미 지급·저장됐기 때문(이중지급 방지).
                GrantClearFuel(rule);
            }

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

        // 보상이 있는 방은 그 보상을 받을 때까지 출구를 묶는다(09-30) — 예전엔 문이 먼저 열려 룬을 지나칠 수 있었다.
        // ⚠️ 첫 await <b>앞</b>이어야 한다. 호출부가 Activate 직후 같은 프레임에 클리어를 통지하고
        //    (OnRoomCleared · OnResolved), 그때 RunFlowController가 이 보류를 보고 출구 공개를 미룬다.
        // 보스방 · 후보 0(풀이 비었다)은 rewards가 비어 보류가 걸리지 않는다 — 지금처럼 바로 열린다.
        if (rewards.Count > 0)
            RunFlowController.Active?.HoldExitsForReward(this);

        // 등급 예고(10-01) — 후보는 위에서 이미 정해졌다. 최고 등급이 예고의 길이 · 빛깔 · 크기를 정한다
        // (일반 0.6초 ~ 전설 2.0초 — 낮은 등급은 빨리 서고, 높은 등급은 기다림 자체가 예고다).
        // 자리 · 등급 계산은 동기로 끝낸다 — 위 보류 호출과 아래 첫 await 사이에 다른 await를 끼우지 않는다.
        ItemRarity topRarity = RewardPresentation.MaxRarity(rewards);
        Vector3    spot      = rewards.Count > 0 ? ResolveReachablePosition(center) : center;
        float      floorY    = spot.y - effectHeightOffset;
        RewardAura aura      = null;

        try
        {
            if (rewards.Count > 0)
                aura = await RewardObjectPresenter.ForetellAsync(spot, floorY, topRarity, ct);
            else
                await UniTask.Delay(TimeSpan.FromSeconds(endEffect2SpawnDelay), cancellationToken: ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            // 예고는 연출이다 — 실패해도 아래에서 보상을 세우고, 받으면 출구가 풀린다(10-01 e0 검토)
            Debug.LogError($"[RoomClearGate] 등급 예고 실패 — 보상은 그대로 세운다: {e}");
            aura = null;
        }

        // 보스방은 룬 보상이 없고(위에서 미굴림) 클리어 후처리(드래프트·런클리어·길)를
        // GameRunBootstrapper.OnBossRoomClearedHandler가 전담하므로 트리거를 스폰하지 않는다.
        // 일반 방에서 아이템이 없으면 별도 처리 불필요 — 출구 게이트는 RunFlowController가 담당한다.
        if (rewards.Count == 0) return;

        try
        {
            var rewardGO = SpawnRewardObject(spot, rewards, isChoice, choiceRounds);
            // 등장 · 놓여 있는 빛 — 보상 오브젝트는 건드리지 않는다(이 오브젝트의 수명이 곧 출구 보류다)
            RewardObjectPresenter.Arrive(rewardGO, aura, floorY, topRarity);
            // 보상 오브젝트는 수령 · 넘기기 · 지급 실패 어느 길로 끝나든 스스로 파괴된다 — 사라지면 출구를 푼다.
            // 그때까지 문이 닫혀 있는 까닭을 가끔 알려 준다. Time.time 기준이라 보상 화면(시간 정지)이 떠 있는 동안에는 세지 않는다.
            float hintAt = Time.time + ExitHintDelay;
            while (rewardGO != null)
            {
                if (Time.time >= hintAt)
                {
                    hintAt = Time.time + ExitHintRepeat;
                    ShowExitHint();
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (OperationCanceledException) { return; }   // 방이 먼저 사라졌다 — 풀 출구도 없다
        catch (Exception e)
        {
            // 보상을 못 세웠어도 방에 가두지는 않는다.
            Debug.LogError($"[RoomClearGate] 보상 오브젝트 생성 실패 — 출구는 연다: {e}");
        }
        RunFlowController.Active?.ReleaseExitsForReward(this);
    }

    /// <summary>
    /// 보상 자리를 <b>걸어서 닿는 곳</b>으로 고른다. 보상은 마지막 처치 위치에 서는데, 막타가 넉백으로
    /// 맵 밖 · 구멍 위 · 벽 속 · 공중에서 나면 닿을 수 없는 자리가 된다 — 보상을 받아야 출구가 열리므로 그대로 두면 방에 갇힌다.
    ///
    /// 가까운 NavMesh로 당기되, 플레이어 자리에서 길이 이어지는 면만 받는다(벽 위 같은 외딴 면 제외).
    /// 그런 면이 없으면 플레이어가 서 있는 자리에 세운다 — 방 중심은 구멍이나 기둥일 수 있지만 이 자리는 확실히 닿는다.
    /// </summary>
    private Vector3 ResolveReachablePosition(Vector3 pos)
    {
        Vector3 lift   = Vector3.up * effectHeightOffset;   // pos는 바닥에서 이만큼 띄운 값이다
        var     player = _run?.Player;

        if (NavMesh.SamplePosition(pos - lift, out var hit, RewardSnapRadius, s_walkFilter)
            && IsConnectedToPlayer(hit.position, player))
        {
            Vector3 snapped = hit.position + lift;
            if ((snapped - pos).sqrMagnitude > 1f)
                Debug.Log($"[RoomClearGate] 보상 자리 보정 {pos} → {snapped} (가까운 걸을 수 있는 면)");
            return snapped;
        }

        if (player == null) return pos;

        Vector3 moved = player.transform.position + lift;
        Debug.LogWarning($"[RoomClearGate] 보상 자리 {pos}에 닿을 수 없다 — 플레이어 자리 {moved}로 옮긴다");
        return moved;
    }

    /// <summary>출구가 이 보상 때문에 닫혀 있을 때만 안내한다(절차 진행이 아닌 방 · 출구 없는 방에서는 말하지 않는다).</summary>
    private void ShowExitHint()
    {
        var flow = RunFlowController.Active;
        if (flow == null || !flow.IsExitHeldBy(this)) return;

        var hud = UnityEngine.Object.FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        hud?.ShowBuffNotice($"<color={UIPalette.GoldHex}>보상</color>을 받으면 길이 열린다");
        Debug.Log("[RoomClearGate] 출구 안내 — 보상을 받으면 길이 열린다");
    }

    /// <summary>플레이어 자리에서 그 면까지 NavMesh 길이 이어지는가. 잴 수 없으면(플레이어 없음 · NavMesh 밖) 이어진 것으로 본다.</summary>
    private static bool IsConnectedToPlayer(Vector3 navPos, PlayerController player)
    {
        if (player == null
            || !NavMesh.SamplePosition(player.transform.position, out var start, RewardSnapRadius, s_walkFilter))
            return true;

        var path = new NavMeshPath();
        return NavMesh.CalculatePath(start.position, navPos, s_walkFilter, path)
               && path.status == NavMeshPathStatus.PathComplete;
    }

    private GameObject SpawnRewardObject(Vector3 center, System.Collections.Generic.List<(RuntimeItemData, ItemSO)> rewards, bool isChoice, int choiceRounds)
    {
        var rewardGO = endEffect2Prefab != null
            ? Instantiate(endEffect2Prefab, center, Quaternion.identity)
            : new GameObject("ClearReward_Fallback");
        rewardGO.transform.position = center;
        RoomScopedDrop.Mark(rewardGO);   // 안 주웠으면 방 전환 시 정리(다음 방 잔존 방지)

        var trigger = rewardGO.AddComponent<ClearRewardTrigger>();
        trigger.Initialize(_run, rewards, _isBossRoom, isChoice, choiceRounds);

        // "보상은 떠 있는데 아직 안 받았다"를 세이브에 남긴다 — 이 상태로 종료해도 이어하기에서 되살아난다.
        RunFlowController.Active?.NotifyClearRewardSpawned();
        return rewardGO;
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
    /// <param name="floorGuaranteesOneOnly">
    /// true면 <paramref name="floor"/>를 <b>후보 한 장에만</b> 건다(나머지는 방 자체 하한으로 개별 굴림).
    /// 챌린지 등급 보상이 이 경로다 — 예전엔 하한을 후보 전부에 걸어 플래티넘이면 <b>4장이 모두 전설</b>로 떴고,
    /// 고를 것이 없어 3지선다가 형식만 남았다. 보장(최소 한 장)은 지키면서 등급은 장마다 다르게 굴린다.
    /// 정예방처럼 <b>방 자체가 정한 하한</b>은 그대로 전 후보에 걸린다(§3-2 정예=Rare 하한).
    /// </param>
    private System.Collections.Generic.List<(RuntimeItemData data, ItemSO so)> RollRewardChoices(
        int count, ItemRarity? floor = null, bool floorGuaranteesOneOnly = false)
    {
        // 룬 선택지는 3장 고정이다(10-01 — 제단 「룬 선택지 +1」 · 정예 4장 폐지).
        // 고행자의 인장이 켜져 있으면 한 장을 내놓는다.
        count = AsceticSigilService.ApplyChoiceCount(count);

        var result = new System.Collections.Generic.List<(RuntimeItemData, ItemSO)>(count);
        var rule   = RoomRewardTable.For(RoomKind());
        if (count <= 0 || !RoomRewardTable.RollDrop(rule)) return result;

        var picked = new System.Collections.Generic.HashSet<string>();
        // 풀이 작아 중복이 반복될 때를 대비한 안전장치(무한 루프 방지).
        int maxAttempts = count * 8;

        for (int attempt = 0; attempt < maxAttempts && result.Count < count; attempt++)
        {
            // 보장분은 첫 한 장에만. 나머지는 방이 정한 하한(정예=Rare, 일반=없음)으로 개별 굴림.
            var effFloor = floorGuaranteesOneOnly
                ? (result.Count == 0 ? floor : rule.RarityFloor)
                : floor;

            // 첫 장은 보유 계열 쪽으로 기울여 굴린다(발동 계열 — 「3장 중 1장은 내 빌드 쪽」, 09-29). 나머지는 넓게.
            var (d, s) = PickItemByRolledRarity(effFloor, rule.Weights, preferOwnedFamily: result.Count == 0);
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
        ItemRarity? floor, in RoomRewardTable.RarityWeights weights, bool preferOwnedFamily = false)
    {
        var rarity = RoomRewardTable.RollRarity(weights);
        if (floor.HasValue && rarity < floor.Value) rarity = floor.Value;   // 방 종류·챌린지 등급 rarity 하한
        // 하한 <b>뒤에</b> 다시 내린다 — RollRarity 안의 클램프만으로는 하한(챌린지 플래티넘=전설 · 골드=영웅)이
        // 기억의 제단 등급 해금을 도로 뚫는다.
        rarity = MemoryAltarService.ClampRarity(rarity);

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
        if (preferOwnedFamily && UnityEngine.Random.value < PreferFamilyChance)
        {
            var owned = PickOwnedFamily(candidates);
            if (owned != null) so = owned;
        }
        var data = RuntimeItemData.FromSO(so);
        if (data == null)
        {
            Debug.LogWarning($"[RoomClearGate] RuntimeItemData 변환 실패 — itemId={so?.itemId}");
            return (null, null);
        }

        Debug.Log($"[RoomClearGate] 보상 아이템 선택: {so.itemId} (rarity={rarity}, room={RoomKind()})");
        return (data, so);
    }

    /// <summary>
    /// 보유 각인 1 · 2위 계열의 룬 하나(없으면 null). 이 등급 풀에 그 계열이 없으면 기울이지 않는다.
    /// </summary>
    private static ItemSO PickOwnedFamily(System.Collections.Generic.IReadOnlyList<ItemSO> candidates)
    {
        BuildImprint.TopFamilies(2, s_topFamilies);
        if (s_topFamilies.Count == 0) return null;
        s_familyPool.Clear();
        foreach (var c in candidates)
            if (c != null && s_topFamilies.Contains(BuildFamilyRules.OfItemId(c.itemId))) s_familyPool.Add(c);
        return s_familyPool.Count > 0 ? s_familyPool[UnityEngine.Random.Range(0, s_familyPool.Count)] : null;
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
