using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 재련소(Crucible) 방 런타임 컨트롤러 (NPC + UI 방식). ShopRoomController 뼈대 차용.
///
/// 책임: 재련공 NPC 스폰 → 상호작용 시 UI_CruciblePanel 오픈 → 무기 강화/승급 요청 처리.
/// 강화 계산은 WeaponEnhanceService(순수 로직), 재료는 RunFuelBank(강화재료), 데이터는 EnhanceTableSO.
///
/// 결정성: _roomRng(masterSeed+visitCount 파생, bootstrap 주입)의 롤 스트림이 이어하기 재현 → save-scum 무력.
/// 도파민 레이어: 연속 성공 스트릭 + 스트릭에 비례한 잭팟(강화재료 환불) 굴림.
/// </summary>
public class CrucibleRoomController : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float NpcStandHeight = 1f; // 캡슐 바닥(발)이 지면에 닿도록 — 앵커 · 폴백 모두.

    /// <summary>NPC 앞 작업대까지의 거리(m) — NPC가 카운터 뒤에 선 구도를 만든다.</summary>
    private const float CounterDistance = 2.5f;

    /// <summary>재련공 잡담 — 무기 강화/승급 컨셉.</summary>
    private static readonly string[] ChatterLines =
    {
        "쇠는 두드릴수록 단단해지지.",
        "그 무기… 아직 잠재력이 남았군.",
        "불을 지펴뒀다. 올려놓기만 해라.",
        "승급은 큰 결심이야. 각오는 됐나?",
        "좋은 재료를 가져오면 좋은 결과가 나온다.",
    };
    private const float DiscountCostMult    = 0.5f;   // 반값 재련
    private const int   FeverExtraCards     = 1;      // 열기 오른 화로 — 이번 방문 카드 +1장(10-18 재련 개편)
    private const int   BountyVisitRerolls  = 1;      // 풍요로운 화로 — 이번 방문 다시 뽑기 +1(10-18 재련 개편)

    // ── 비공개 필드 ─────────────────────────────────────────
    private GameRunSession _run;
    private EnhanceTableSO _table;
    private System.Random  _roomRng;
    private System.Random  _dialogueRng = new();   // 대사 전용 — _roomRng(강화/잭팟 결정성 스트림)과 절대 공유 금지

    private GameObject _npcInstance;
    private ShopNpcInteraction _npc;
    private NpcAmbientChatter _chatter;   // 머리 위 잡담 — 방이 드러나면 화로 이벤트를 외친다
    private ServiceNpcReactor _reactor;   // 혼자면 망치질 · 다가오면 알아봄 · 벼리고 나가면 반김
    private int _fuelAtOpen;              // 패널을 열 때 재료 — 닫을 때 달라졌으면 일을 맡긴 것

    private CrucibleEvent _event;  // 이 방 돌발 이벤트(결정적 롤)

    private bool _initialized;
    private bool _uiOpen;
    private bool _eventsHooked;
    private bool _goldHooked;

    // ── Properties (UI가 읽음) ──────────────────────────────
    /// <summary>이 재련소가 붙은 런 세션(진화·연료 조회용). UI가 참조한다.</summary>
    public GameRunSession Run => _run;

    public EnhanceTableSO Table => _table;
    public int FuelAmount => _run?.FuelBank?.EnhanceMaterial ?? 0;
    /// <summary>
    /// 승급 후보 전설 목록. 「전설 3종 개방」 전에는 <b>맨 앞 1종</b>만 보인다(기억의 제단).
    /// 승급 자체는 해금과 무관하게 되고, 해금이 넓히는 것은 고를 수 있는 폭이다.
    /// </summary>
    public EnhanceTableSO.LegendDef[] Legends
    {
        get
        {
            var all = _table != null ? _table.Legends : Array.Empty<EnhanceTableSO.LegendDef>();
            int allowed = MemoryAltarService.LegendChoiceCount;
            if (all.Length <= allowed) return all;

            var cut = new EnhanceTableSO.LegendDef[allowed];
            Array.Copy(all, cut, allowed);
            return cut;
        }
    }

    public CrucibleEvent ActiveEvent => _event;
    public bool HasEvent => _event != CrucibleEvent.None;
    private float CostMult     => _event == CrucibleEvent.Discount ? DiscountCostMult : 1f;
    /// <summary>돌발 이벤트 배너 문구(없으면 빈 문자열).</summary>
    public string EventBanner => _event switch
    {
        CrucibleEvent.Discount => "↗ 반값 재련! 지금 안 두드리면 손해지…",
        CrucibleEvent.Fever    => "▲ 열기 오른 화로 — 이번 방문 카드 +1장!",
        CrucibleEvent.Bounty   => "◆ 풍요로운 화로 — 다시 뽑기 +1!",
        _                      => string.Empty,
    };

    /// <summary>돌발 이벤트 이름(입장 배너 · 10-15) — 없으면 빈 문자열.</summary>
    public string EventTitle => _event switch
    {
        CrucibleEvent.Discount => "반값 재련",
        CrucibleEvent.Fever    => "열기 오른 화로",
        CrucibleEvent.Bounty   => "풍요로운 화로",
        _                      => string.Empty,
    };

    /// <summary>돌발 이벤트 효과 요약(정보 패널 디테일 표시용).</summary>
    public string EventEffectDesc => _event switch
    {
        CrucibleEvent.Discount => "재련 비용 50%",
        CrucibleEvent.Fever    => "고르는 카드 +1장",
        CrucibleEvent.Bounty   => "다시 뽑기 +1",
        _                      => string.Empty,
    };

    /// <summary>강화/승급/연료 변동 시 UI 재렌더 통지.</summary>
    public event Action OnCrucibleChanged;
    /// <summary>재련 1회가 끝났다(true = 원거리) — 망치 연출 · 카드 창은 각 고르기(증강 · 파츠)가 연다.</summary>
    public event Action<bool> OnReforged;

    // ── Lifecycle ───────────────────────────────────────────

    private void OnEnable()  => RunFlowController.RoomRevealed += HandleRoomRevealed;
    private void OnDisable() => RunFlowController.RoomRevealed -= HandleRoomRevealed;

    private void OnDestroy()
    {
        // 이번 방문 몫(화로 열기 · 풍요)은 방을 나가면 사라진다
        MeleeAugmentDraft.ExtraCards = 0;
        MeleeAugmentDraft.VisitRerolls = 0;
        RangedPartsDraft.VisitRerolls = 0;
        UnhookRunEvents();
        if (_npc != null) _npc.OnInteract -= HandleNpcInteract;
    }

    // ── Public Methods ──────────────────────────────────────

    /// <summary>
    /// Bootstrapper에서 호출. table: 강화 데이터(Addressable). roomRng: 결정성 롤(이어하기 재현).
    /// npcPrefab: 재련공 NPC(Addressable). null이면 NPC 없이 방만 존재.
    /// </summary>
    public void Initialize(GameRunSession run, EnhanceTableSO table,
                           System.Random roomRng = null, GameObject npcPrefab = null)
    {
        if (_initialized) { Debug.LogWarning("[Crucible] 이미 초기화됨"); return; }

        _run = run;
        _table = table;
        _roomRng = roomRng ?? new System.Random();

        if (_table != null) WeaponEnhanceService.InstallTable(_table);

        // 대사 스트림 분기 — 대사 롤이 _roomRng를 소비하면 BumpRoll에 안 잡혀 복원 시 스트림 위치가
        // 어긋나고, 대사 호출이 플레이어 조작(슬롯 토글/호버)에 좌우돼 save-scum 통로가 된다.
        // 분기 자체는 Initialize의 고정 위치에서 1회만 소비하므로 결정성은 그대로 유지된다.
        _dialogueRng = new System.Random(_roomRng.Next());

        _event = RollEvent();   // 결정적 돌발 이벤트
        // 풍요로운 화로 — 이번 방문 다시 뽑기 +1(근접 · 원거리 고르기 각각 · 10-18 재련 개편)
        int visitRerolls = _event == CrucibleEvent.Bounty ? BountyVisitRerolls : 0;
        MeleeAugmentDraft.VisitRerolls = visitRerolls;
        RangedPartsDraft.VisitRerolls  = visitRerolls;

        // ── save-scum 방지 ──
        // 방 안에서 저장(S3)하고 재접속하면 _roomRng 스트림이 처음으로 리셋된다.
        // 그러면 "이미 굴린 롤"을 다시 굴릴 수 있어 좋은 결과만 반복 취득이 가능해진다.
        // → 세션에 기록된 소비 수만큼 스트림을 미리 진행시켜 이어서 굴리게 한다.
        int consumed = _run != null ? _run.CrucibleRollIndex : 0;
        for (int i = 0; i < consumed; i++) _roomRng.NextDouble();
        if (consumed > 0)
            Debug.Log($"[Crucible] 결정적 롤 스트림 재개 — 소비 {consumed}회 건너뜀");

        var (npcPos, npcRot) = ResolveNpcPlacement();
        SpawnNpc(npcPrefab, npcPos, npcRot);
        SpawnDecor(npcPos, npcRot);   // 대장간 소품을 NPC 뒤/옆에 배치
        HookRunEvents();

        _initialized = true;
        Debug.Log($"[Crucible] 초기화 완료(NPC+UI). table={(table != null ? "OK" : "null")} / rng={(roomRng != null ? "seeded" : "global")}");
    }

    // ── 슬롯 조회 (UI가 읽음) ────────────────────────────────

    public WeaponData GetSlot(int slot)
    {
        var wm = _run?.Player?.WeaponManager;
        if (wm == null) return null;
        return slot == PlayerWeaponManager.Slot1 ? wm.Weapon1Data : wm.Weapon0Data;
    }

    public bool CanEnhance(int slot)
    {
        var w = GetSlot(slot);
        return w != null && _table != null && !WeaponEnhanceService.IsMaxed(w, _table);
    }

    public bool  IsDiscountEvent    => _event == CrucibleEvent.Discount;
    public int   MaxAt(int slot)           => WeaponEnhanceService.MaxEnhance(GetSlot(slot), _table);
    public float EffectiveAttackAt(int slot) => WeaponEnhanceService.EffectiveAttack(GetSlot(slot), _table);

    /// <summary>승급 가능 여부(강화 MAX + 미승급 + 검류).</summary>
    public bool CanPromote(int slot)
    {
        var w = GetSlot(slot);
        if (w == null || _table == null) return false;
        if (!string.IsNullOrEmpty(w.legendId)) return false;
        if (w.enhanceLevel < WeaponEnhanceService.MaxEnhance(w, _table)) return false;
        return w.weaponType == WeaponType.Katana || w.weaponType == WeaponType.Greatsword;
    }

    /// <summary>진화 가능 — 갈래 하나라도 강화 조건(requiredEnhanceLevel)을 채웠다. D6: +5. 승급(CanPromote · 최대 강화)과 다르다.</summary>
    public bool CanEvolve(int slot)
    {
        var w = GetSlot(slot);
        if (w?.evolution == null) return false;
        foreach (var b in w.evolution.Branches)
            if (WeaponEvolutionSO.IsUnlocked(b, w.enhanceLevel)) return true;
        return false;
    }

    /// <summary>다음 형 단련 단계와 비용(강화재료). 형이 없거나 Ⅲ까지 벼렸으면 false.</summary>
    public bool CanTemperForm(int slot, out int nextTier, out int cost)
        => WeaponFormService.CanTemper(GetSlot(slot), out nextTier, out cost);

    /// <summary>형 단련 — 확정 성공 · 하락 없음. 다음 단계 변주가 아니거나 재료가 모자라면 false.</summary>
    public bool TryTemperForm(int slot, string variantId)
    {
        if (!WeaponFormService.TryTemper(GetSlot(slot), _run?.FuelBank, variantId)) return false;
        SaveNow("crucible-temper");
        OnCrucibleChanged?.Invoke();
        RefreshEquippedIfCurrent(slot);
        return true;
    }

    // ── 재련(10-18 재련 개편 · 기획/RelicFairy_재련개편_설계_20261018.md) ─────
    // 확률 강화 대신 재련 1회 = 근접 +3 확정 + 증강 고르기 / 원거리 파츠 카드 3장 중 1. 실패 · 하락 · 연속 · 잭팟 없음.
    // 이번 방문 n번째 = 방마다 0으로 돌아가는 굴림 번호(CrucibleRollIndex) — 재련 1회가 굴림 1개를 쓴다(저장 · 이어하기가 그대로 센다).

    /// <summary>이번 방문에 한 재련 수(근접 · 원거리 합).</summary>
    public int ReforgeCountThisVisit => _run != null ? _run.CrucibleRollIndex : 0;

    /// <summary>다음 재련 비용(재료) — 근접 = Slot0 무기 강화 단계 띠 · 원거리 = 이 런 원거리 재련 수 띠 · 반값 · 인장 반영.</summary>
    public int ReforgeCost(bool ranged)
    {
        float band = ranged ? WeaponReforgeService.RangedBand(RangedPartsState.Current.ReforgeCount + 1)
                            : WeaponReforgeService.MeleeBand(GetSlot(PlayerWeaponManager.Slot0));
        return WeaponReforgeService.Cost(ReforgeCountThisVisit + 1, band, CostMult);
    }

    /// <summary>재련할 수 있나 — 안 되면 <paramref name="reason"/>(화면 회색 이유).</summary>
    public bool CanReforge(bool ranged, out string reason)
    {
        reason = null;
        if (_run == null || !_run.IsRunning || _table == null || _run.FuelBank == null) { reason = "지금은 재련할 수 없다"; return false; }
        if (MeleeAugmentDraft.Pending != null || RangedPartsDraft.IsOpen) { reason = "카드를 먼저 고른다"; return false; }
        if (ranged)
        {
            var rw = GetSlot(PlayerWeaponManager.Slot1);
            if (rw == null || !SkillTierResolver.IsRanged(rw)) { reason = "원거리 무기가 없다"; return false; }
            if (!RangedPartsDraft.HasCandidates(RangedPartsState.Current)) { reason = $"열린 파츠가 모두 Lv{RangedPartsDraft.PartCap}이다 · 다른 파츠는 기억의 제단에서 연다"; return false; }   // 10-23 W6 — 제단에서 연 파츠만 재련(c1 RangedPartsDraft.IsUnlocked)
        }
        else if (!WeaponReforgeService.CanReforgeMelee(GetSlot(PlayerWeaponManager.Slot0), _table)) { reason = "이미 최대 강화"; return false; }
        if (FuelAmount < ReforgeCost(ranged)) { reason = "재료가 모자란다"; return false; }
        return true;
    }

    /// <summary>
    /// 재련 1회 — 재료를 내고 근접(Slot0)은 +3 확정 · 증강 고르기, 원거리는 파츠 카드 고르기를 연다.
    /// 화로 「열기」면 카드 +1장. 근접은 바로 저장 · 원거리는 카드를 고를 때 저장한다(고르기 전에 끄면 재료 · 순번도 되돌아간다).
    /// </summary>
    public bool TryReforge(bool ranged)
    {
        if (!CanReforge(ranged, out _)) return false;
        int cost = ReforgeCost(ranged);
        if (!_run.FuelBank.TrySpend(FuelKind.EnhanceMaterial, cost)) return false;
        int seed = (int)(_roomRng.NextDouble() * int.MaxValue);   // 재련 1회 = 굴림 1개(이어하기에서 같은 카드)
        BumpRoll(1);
        var rng = new System.Random(seed);
        int extra = _event == CrucibleEvent.Fever ? FeverExtraCards : 0;
        bool ok = ranged ? WeaponReforgeService.ReforgeRanged(rng, extra)
                         : WeaponReforgeService.ReforgeMelee(GetSlot(PlayerWeaponManager.Slot0), _table, _run, rng, extra);
        if (!ok)
        {
            _run.FuelBank.Add(FuelKind.EnhanceMaterial, cost);   // 열 카드가 없었다 — 되돌린다(CanReforge가 먼저 막으므로 거의 없다)
            return false;
        }
        if (!ranged)
        {
            RefreshEquippedIfCurrent(PlayerWeaponManager.Slot0);
            SaveNow("crucible-reforge");
        }
        OnReforged?.Invoke(ranged);
        OnCrucibleChanged?.Invoke();
        return true;
    }

    // ── 승급 (UI가 호출) ────────────────────────────────────

    /// <summary>파츠 지급(티어 보상 선택) 확정 후 즉시 저장 — 방 안에서 끊겨도 지급이 날아가지 않게.</summary>
    public void SavePartGrant()
    {
        SaveNow("crucible-part-grant");
        OnCrucibleChanged?.Invoke();
    }

    /// <summary>결정적 롤 소비 수를 세션에 누적(세이브에 기록 → 복원 시 스트림 재개).</summary>
    private void BumpRoll(int n)
    {
        if (_run != null) _run.CrucibleRollIndex += n;
    }

    /// <summary>행동 확정 즉시 저장(S3). 방 경계가 아니어도 진행분이 보존된다.</summary>
    private static void SaveNow(string reason) => RunFlowController.Active?.SaveNow(reason);

    /// <summary>해당 전설이 지금 고를 수 있는 후보인지(기억의 제단 「전설 3종 개방」).</summary>
    private bool IsLegendAllowed(string legendId)
    {
        if (string.IsNullOrEmpty(legendId)) return false;

        foreach (var l in Legends)
            if (string.Equals(l.legendId, legendId, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>승급 시도(확정 성공, 재료 대량). legendId는 Legends에서 택1.</summary>
    public PromoteResult TryPromote(int targetSlot, string legendId)
    {
        if (_run == null || !_run.IsRunning || _table == null)
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);

        var target = GetSlot(targetSlot);
        var fuel   = _run.FuelBank;
        if (target == null || fuel == null)
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);

        // 후보 제한을 여기서도 검증한다 — Legends(표시용)만 자르면 UI를 우회한 호출이 통과한다.
        if (!IsLegendAllowed(legendId))
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);

        var result = WeaponEnhanceService.TryPromote(target, legendId, _table, fuel);
        if (result.IsSuccess)
        {
            RefreshEquippedIfCurrent(targetSlot);
            SaveNow("crucible-promote");   // S3: 승급 확정 → 즉시 저장 (롤 미소비 — 확정 성공)
        }

        OnCrucibleChanged?.Invoke();
        return result;
    }

    // ── NPC / UI ────────────────────────────────────────────

    private GameObject[] _decorPrefabs;

    /// <summary>Initialize 전에 호출 — NPC 주변에 배치할 방 소품(대장간 등) 프리팹 배열.</summary>
    public void SetDecorPrefabs(GameObject[] prefabs) => _decorPrefabs = prefabs;

    /// <summary>
    /// 무대 구성: 배열 첫 소품을 <b>작업대</b>로 승격해 NPC 정면(플레이어 쪽)에 놓고,
    /// 나머지는 NPC 뒤쪽 반원에 배치해 '작업장'을 만든다. 결정적(_roomRng).
    /// → NPC가 카운터 뒤에 선 대장장이처럼 읽힌다.
    /// </summary>
    private void SpawnDecor(Vector3 npcPos, Quaternion npcRot)
    {
        if (_decorPrefabs == null || _decorPrefabs.Length == 0) return;

        ServiceRoomDecorPlacer.SyncPhysics();   // 갓 생성된 벽 콜라이더를 쿼리에 반영

        // grid_csv가 무대를 지정했으면(NC/NP 토큰) 그대로 쓴다. 없으면 아래 탐색 배치로 폴백.
        if (ServiceRoomDecorPlacer.TryPlaceFromAnchors(transform, _decorPrefabs, npcPos,
                                                       npcPos.y - NpcStandHeight, "CrucibleCounter"))
            return;

        Vector3 fwd   = npcRot * Vector3.forward; // NPC가 바라보는 방향(=플레이어 쪽)
        Vector3 back  = -fwd;                     // 무대 안쪽
        Vector3 right = npcRot * Vector3.right;
        float groundY = npcPos.y - NpcStandHeight;

        // 1) 작업대 — NPC 앞. 벽이면 빈 자리를 탐색(못 찾으면 생략).
        if (_decorPrefabs[0] != null &&
            ServiceRoomDecorPlacer.TryFindSpot(npcPos, fwd, CounterDistance, groundY, out var tablePos))
        {
            Vector3 faceBack = npcPos - tablePos;
            float tableYaw = Mathf.Atan2(-faceBack.x, -faceBack.z) * Mathf.Rad2Deg;
            ServiceRoomDecorPlacer.Place(_decorPrefabs[0], tablePos, tableYaw, groundY, transform, "CrucibleCounter");
        }

        // 2) 나머지 소품 — NPC 뒤쪽 반원(-70°~+70°)
        int n = _decorPrefabs.Length;
        for (int i = 1; i < n; i++)
        {
            var prefab = _decorPrefabs[i];
            if (prefab == null) continue;

            float t     = n > 2 ? (float)(i - 1) / (n - 2) : 0.5f;
            float ang   = Mathf.Lerp(-70f, 70f, t) * Mathf.Deg2Rad;
            float rad   = 3.5f + (float)_roomRng.NextDouble() * 1.2f;
            Vector3 dir = back * Mathf.Cos(ang) + right * Mathf.Sin(ang);

            if (!ServiceRoomDecorPlacer.TryFindSpot(npcPos, dir, rad, groundY, out var pos)) continue;

            Vector3 toNpc = npcPos - pos;
            float yaw = Mathf.Atan2(-toNpc.x, -toNpc.z) * Mathf.Rad2Deg; // 소품이 NPC를 향하게
            ServiceRoomDecorPlacer.Place(prefab, pos, yaw, groundY, transform);
        }
    }

    private void SpawnNpc(GameObject npcPrefab, Vector3 pos, Quaternion rot)
    {
        if (npcPrefab == null)
        {
            Debug.LogWarning("[Crucible] NPC 프리팹 없음 — 재련소 UI를 열 수 없습니다.");
            return;
        }

        _npcInstance = Instantiate(npcPrefab, pos, rot, transform);
        _npc = _npcInstance.GetComponent<ShopNpcInteraction>();
        if (_npc == null) _npc = _npcInstance.GetComponentInChildren<ShopNpcInteraction>(true);

        if (_npc != null) _npc.OnInteract += HandleNpcInteract;
        else Debug.LogWarning("[Crucible] NPC 프리팹에 ShopNpcInteraction 없음");
        _npcInstance.TryGetComponent(out _reactor);

        // 주기적 월드스페이스 잡담 — 방이 '사람이 일하는 장소'로 읽히게.
        _chatter = _npcInstance.AddComponent<NpcAmbientChatter>();
        _chatter.Initialize(ChatterLines, 9f, new Color(1f, 0.82f, 0.55f), _npc != null ? _npc.ChatterHeight : null);
        TintForge();
    }

    /// <summary>방이 화면에 드러난 순간 — 화로 이벤트가 있으면 화면 위 배너 + 대장장이 외침(10-15 · 덮개 뒤에서 외치지 않게 이때).</summary>
    private void HandleRoomRevealed(GameObject room)
    {
        if (room == null || !transform.IsChildOf(room.transform) || !HasEvent) return;
        RoomEventNotice.Raise(EventTitle, $"{EventEffectDesc} — 이 방에서만");
        if (_chatter != null) _chatter.ShoutFirst(EventShout);
    }

    /// <summary>화로 빛 — 이벤트마다 모루 불빛(Heat) 색 · 세기만 바꾼다(10-15 · 연출 절제: 새 이펙트 없이 있는 빛으로).</summary>
    private void TintForge()
    {
        if (_npcInstance == null || _event == CrucibleEvent.None) return;
        foreach (var l in _npcInstance.GetComponentsInChildren<Light>(true))
        {
            if (l.name != "Heat") continue;
            switch (_event)
            {
                case CrucibleEvent.Fever:    l.color = new Color(1f, 0.48f, 0.16f); l.intensity *= 1.6f; l.range *= 1.25f; break;   // 열기 — 불이 커진다
                case CrucibleEvent.Discount: l.color = new Color(1f, 0.80f, 0.38f); break;                                         // 반값 — 금빛
                case CrucibleEvent.Bounty:   l.color = new Color(1f, 0.90f, 0.60f); l.intensity *= 1.3f; l.range *= 1.4f; break;   // 풍요 — 모루 위로 빛이 번진다
                case CrucibleEvent.Curse:    l.color = new Color(0.62f, 0.30f, 0.55f); l.intensity *= 0.7f; break;                  // 저주 — 검붉게 가라앉는다
            }
        }
    }

    private void HandleNpcInteract()
    {
        if (_uiOpen) return;
        OpenCrucibleUIAsync().Forget();
    }

    private async UniTaskVoid OpenCrucibleUIAsync()
    {
        _uiOpen = true;
        if (_npc != null) _npc.SetInteractable(false);
        _fuelAtOpen = FuelAmount;
        if (_reactor != null) _reactor.BeginTalk();

        var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
        if (panel == null)
        {
            _uiOpen = false;
            if (_npc != null) _npc.SetInteractable(true);
            if (_reactor != null) _reactor.EndTalk(false);
            Debug.LogWarning("[Crucible] UI_CruciblePanel 로드 실패");
            return;
        }
        panel.Bind(this);
    }

    /// <summary>UI_CruciblePanel이 닫힐 때 호출.</summary>
    public void NotifyPanelClosed()
    {
        _uiOpen = false;
        if (_npc != null) _npc.SetInteractable(true);
        // 강화 · 진화 · 파츠는 모두 강화 재료를 쓴다(잭팟은 돌려준다) — 달라졌으면 일을 맡긴 것
        if (_reactor != null) _reactor.EndTalk(FuelAmount != _fuelAtOpen);
    }

    // ── 배치 ────────────────────────────────────────────────

    private (Vector3 pos, Quaternion rot) ResolveNpcPlacement()
    {
        // 앵커는 바닥 높이 점 — 서는 높이를 더한다(안 더해 NPC·작업대가 1.1 m 박혔다, 09-29).
        var anchor = GetComponentInChildren<ShopNpcAnchor>(true);
        if (anchor != null)
            return (ServiceRoomDecorPlacer.NpcStandPoint(anchor.transform.position, NpcStandHeight), anchor.transform.rotation);

        Vector3 pos = transform.position;
        pos.y += NpcStandHeight;
        // 고정 +Z 대신 가장 트인 쪽 — 벽을 보고 서거나 작업대가 벽에 박히는 것을 방지.
        return (pos, ServiceRoomDecorPlacer.ResolveFacing(pos, Quaternion.identity));
    }

    // ── 스탯 갱신 ───────────────────────────────────────────

    /// <summary>방 진입 시 돌발 이벤트 결정적 롤. Discount 18 / Fever 15 / Bounty 12 / None 55 — 저주는 없앴다(실패가 없으니 뜻이 없다 · 10-18 재련 개편).</summary>
    private CrucibleEvent RollEvent()
    {
        double r = _roomRng.NextDouble();
        if (r < 0.18) return CrucibleEvent.Discount;
        if (r < 0.33) return CrucibleEvent.Fever;
        if (r < 0.45) return CrucibleEvent.Bounty;
        return CrucibleEvent.None;
    }

    /// <summary>상황별 재련공 동기부여 대사(도발/부추김/축하/위로). UI가 표시.</summary>
    public string GetDialogue(CrucibleMood mood)
    {
        if (mood == CrucibleMood.Event)
        {
            string greet = EventGreeting;
            if (!string.IsNullOrEmpty(greet)) return greet;
        }
        var pool = mood switch
        {
            CrucibleMood.Taunt    => TauntLines,
            CrucibleMood.Streak   => StreakLines,
            CrucibleMood.Jackpot  => JackpotLines,
            CrucibleMood.Fail     => FailLines,
            CrucibleMood.Success  => SuccessLines,
            CrucibleMood.NearMiss => NearMissLines,
            CrucibleMood.NoFuel   => NoFuelLines,
            CrucibleMood.Evolve   => EvolveLines,
            CrucibleMood.Promote  => PromoteLines,
            CrucibleMood.Temper   => TemperLines,
            _                     => IdleLines,
        };
        if (pool == null || pool.Length == 0) return string.Empty;
        return pool[_dialogueRng.Next(pool.Length)];
    }

    private static readonly string[] IdleLines =
    {
        "쇠는 두드릴수록 강해지지. 무엇을 붙일지는 네가 골라.",
        "그 마법사가 날 여기 처박아둔 지도 천 년이야. 심심하던 참이지.",
    };
    private static readonly string[] TauntLines =
    {
        "망설이나? 두드리면 반드시 단단해진다. 고르기만 하면 돼.",
        "안 두드릴 거면 왜 왔어? 화로만 식잖아.",
        "재료를 아끼다 무기를 놓친다, 애송이.",
    };
    private static readonly string[] StreakLines =
    {
        "오, 손맛 좀 보는데? 여기서 멈추면 바보지.",
        "불꽃이 춤춘다! 한 번 더 가자고!",
    };
    private static readonly string[] JackpotLines =
    {
        "크하하! 오늘 대장간 신이 미소짓는군!",
        "재료가 되돌아왔어! 이게 바로 재련의 낭만이지.",
    };
    private static readonly string[] FailLines =
    {
        "쯧, 쇠가 배신했군. …한 번 더 안 할 거야?",
        "실패? 쇠가 원래 그래. 다시 두드리면 되잖아.",
    };
    private static readonly string[] SuccessLines =
    {
        "좋아, 제법인데. 더 두드려볼까?",
        "쇠가 노래하는군. 멈추지 마.",
    };
    private static readonly string[] NearMissLines =
    {
        "아깝다! 머리카락 한 올 차이였어.",
        "거의 붙었는데… 쇠가 마지막에 튕겼군.",
    };
    private static readonly string[] NoFuelLines =
    {
        "재료 없이는 손도 못 댄다.",
        "빈손으로 망치를 들 순 없지. 재료부터 가져와.",
    };
    private static readonly string[] EvolveLines =
    {
        "껍질을 깼군. 이제 제 이름을 가진 무기다.",
        "모양이 완전히 바뀌었어. 손에 새로 익혀 둬.",
    };
    private static readonly string[] PromoteLines =
    {
        "전설이다… 천 년 만에 이런 걸 두드려 보는군.",
        "내 평생 최고의 작품이다. 잘 써라.",
    };
    private static readonly string[] TemperLines =
    {
        "형이 단단해졌다. 손에 더 붙을 거야.",
        "이 형, 이제 제법 길이 들었군.",
    };

    /// <summary>패널을 열 때 화로 이벤트 이야기(10-15) — 없으면 빈 문자열(평상 대사로).</summary>
    private string EventGreeting => _event switch
    {
        CrucibleEvent.Discount => "오늘은 반값이다. 이럴 때 많이 두드려 둬.",
        CrucibleEvent.Fever    => "화로가 잔뜩 달았어. 오늘은 고를 게 하나 더 나온다.",
        CrucibleEvent.Bounty   => "불 기운이 좋군. 마음에 안 들면 한 번 더 뽑아 주지.",
        CrucibleEvent.Curse    => "화로가 시커멓다… 오늘은 쇠가 잘 안 붙을 거야.",
        _                      => string.Empty,
    };

    /// <summary>방에 들어올 때 머리 위 외침(10-15 · 월드 글자라 기호 없이) — 화로 이벤트가 없으면 null(평소 잡담만).</summary>
    private string EventShout => _event switch
    {
        CrucibleEvent.Discount => "오늘은 반값이다! 이럴 때 두드려 둬!",
        CrucibleEvent.Fever    => "화로가 잔뜩 달았다! 오늘은 하나 더 골라라!",
        CrucibleEvent.Bounty   => "불 기운이 좋아! 한 번 더 뽑아 주지!",
        CrucibleEvent.Curse    => "화로가 시커멓군… 오늘은 조심해라.",
        _                      => null,
    };

    /// <summary>변경된 슬롯이 현재 장착 무기면 데미지/HUD 스탯을 즉시 재적용.</summary>
    private void RefreshEquippedIfCurrent(int changedSlot)
    {
        var wm = _run?.Player?.WeaponManager;
        if (wm == null || changedSlot < 0) return;
        if (wm.GetCurrentSlotIndex() == changedSlot)
            wm.RaiseEquippedWeaponRefreshed();
    }

    // ── 이벤트 훅 (연료/골드 변동 → UI 재렌더) ───────────────

    private void HookRunEvents()
    {
        if (_eventsHooked || _run == null) return;

        if (_run.FuelBank != null)
            _run.FuelBank.OnFuelChanged += OnFuelChanged;

        if (_run.PlayerState != null)
        {
            _run.PlayerState.OnGoldChanged += OnGoldChanged;
            _goldHooked = true;
        }

        _eventsHooked = true;
    }

    private void UnhookRunEvents()
    {
        if (!_eventsHooked) return;
        _eventsHooked = false;

        if (_run?.FuelBank != null)
            _run.FuelBank.OnFuelChanged -= OnFuelChanged;

        if (_goldHooked && _run?.PlayerState != null)
            _run.PlayerState.OnGoldChanged -= OnGoldChanged;
        _goldHooked = false;
    }

    // ── Event Handlers ──────────────────────────────────────

    private void OnFuelChanged() => OnCrucibleChanged?.Invoke();
    private void OnGoldChanged(int _) => OnCrucibleChanged?.Invoke();
}
