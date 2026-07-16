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
    private const float NpcStandHeight = 1f; // 앵커 없는 폴백 스폰 시 캡슐 바닥이 지면에 닿도록.
    private const float JackpotBaseChance = 0.05f;
    private const float JackpotPerStreak  = 0.05f;
    private const float JackpotMaxChance   = 0.5f;

    private const float DiscountCostMult    = 0.5f;   // 반값 재련
    private const float FeverSuccessBonus   = 0.15f;  // 성공률 +15%p
    private const float BountyJackpotBonus  = 0.15f;  // 잭팟 확률 +15%p (방 전체)
    private const float CurseSuccessPenalty = 0.10f;  // 성공률 -10%p (방 전체)

    // ── 비공개 필드 ─────────────────────────────────────────
    private GameRunSession _run;
    private EnhanceTableSO _table;
    private System.Random  _roomRng;

    private GameObject _npcInstance;
    private ShopNpcInteraction _npc;

    private int  _streak;          // 연속 성공 수
    private bool _lastJackpot;     // 직전 시도가 잭팟이었나(UI 연출용)
    private int  _lastRefund;      // 직전 잭팟 환불량

    private CrucibleEvent _event;  // 이 방 돌발 이벤트(결정적 롤)

    private bool _initialized;
    private bool _uiOpen;
    private bool _eventsHooked;
    private bool _goldHooked;

    // ── Properties (UI가 읽음) ──────────────────────────────
    public EnhanceTableSO Table => _table;
    public int FuelAmount => _run?.FuelBank?.EnhanceMaterial ?? 0;
    public int Streak => _streak;
    public bool LastJackpot => _lastJackpot;
    public int LastRefund => _lastRefund;
    public EnhanceTableSO.LegendDef[] Legends => _table != null ? _table.Legends : Array.Empty<EnhanceTableSO.LegendDef>();

    public CrucibleEvent ActiveEvent => _event;
    public bool HasEvent => _event != CrucibleEvent.None;
    private float CostMult     => _event == CrucibleEvent.Discount ? DiscountCostMult : 1f;
    private float SuccessBonus => _event switch
    {
        CrucibleEvent.Fever => FeverSuccessBonus,
        CrucibleEvent.Curse => -CurseSuccessPenalty,
        _                   => 0f,
    };
    private float BountyBonus  => _event == CrucibleEvent.Bounty ? BountyJackpotBonus : 0f;

    /// <summary>돌발 이벤트 배너 문구(없으면 빈 문자열).</summary>
    public string EventBanner => _event switch
    {
        CrucibleEvent.Discount => "⚡ 반값 재련! 안 지르면 손해지…",
        CrucibleEvent.Fever    => "🔥 열기 오른 화로 — 성공률 상승 중!",
        CrucibleEvent.Bounty   => "💰 풍요로운 화로 — 잭팟이 가깝다!",
        CrucibleEvent.Curse    => "🩸 저주받은 화로 — 성공률이 떨어진다…",
        _                      => string.Empty,
    };

    /// <summary>돌발 이벤트 효과 요약(정보 패널 디테일 표시용).</summary>
    public string EventEffectDesc => _event switch
    {
        CrucibleEvent.Discount => "재료 비용 50%",
        CrucibleEvent.Fever    => "성공률 +15%p",
        CrucibleEvent.Bounty   => "잭팟 확률 +15%p",
        CrucibleEvent.Curse    => "성공률 -10%p",
        _                      => string.Empty,
    };

    /// <summary>다음 성공 시 잭팟(재료 환불) 확률(표시용) — 스트릭 비례 + 풍요 보너스.</summary>
    public float JackpotChance => Mathf.Min(JackpotMaxChance, JackpotBaseChance + JackpotPerStreak * _streak + BountyBonus);

    /// <summary>강화/승급/연료 변동 시 UI 재렌더 통지.</summary>
    public event Action OnCrucibleChanged;

    // ── Lifecycle ───────────────────────────────────────────

    private void OnDestroy()
    {
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

        _event = RollEvent();   // 결정적 돌발 이벤트

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

    public float SuccessChanceAt(int slot) => Mathf.Clamp01(WeaponEnhanceService.SuccessChance(GetSlot(slot), _table) + SuccessBonus);
    public int   MaxAt(int slot)           => WeaponEnhanceService.MaxEnhance(GetSlot(slot), _table);
    public int   CostAt(int slot)          { var w = GetSlot(slot); return (w != null && _table != null) ? WeaponEnhanceService.CostWith(_table, w.enhanceLevel, CostMult) : 0; }
    public int   DropAt(int slot)          { var w = GetSlot(slot); return (w != null && _table != null) ? _table.DropAt(w.enhanceLevel) : 0; }
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

    // ── 강화 / 승급 (UI가 호출) ─────────────────────────────

    /// <summary>강화 시도(대상 슬롯 단일). 실패 시 대상 단계 하락(하한 0).</summary>
    public EnhanceResult TryEnhance(int targetSlot)
    {
        if (_run == null || !_run.IsRunning || _table == null)
            return EnhanceResult.Reject(EnhanceOutcome.RejectInvalid);

        var target = GetSlot(targetSlot);
        var fuel   = _run.FuelBank;
        if (target == null || fuel == null)
            return EnhanceResult.Reject(EnhanceOutcome.RejectInvalid);

        _lastJackpot = false;
        _lastRefund  = 0;

        var result = WeaponEnhanceService.TryEnhance(target, _table, _roomRng, fuel, CostMult, SuccessBonus);

        // 롤 소비 기록 — 거부(재료부족/최대치)는 롤을 굴리지 않으므로 세지 않는다.
        if (!result.IsReject) BumpRoll(1);

        if (result.outcome == EnhanceOutcome.Success)
        {
            _streak++;
            RollJackpot(fuel, result.spent);   // 잭팟 굴림도 내부에서 롤 소비를 기록
        }
        else if (result.outcome == EnhanceOutcome.FailDropped)
        {
            _streak = 0;
        }

        // 대상이 현재 장착 무기면 데미지/HUD 스탯 즉시 갱신
        if (!result.IsReject)
        {
            RefreshEquippedIfCurrent(targetSlot);
            SaveNow("crucible-enhance");   // S3: 강화 결과 확정 → 즉시 저장
        }

        OnCrucibleChanged?.Invoke();
        return result;
    }

    /// <summary>결정적 롤 소비 수를 세션에 누적(세이브에 기록 → 복원 시 스트림 재개).</summary>
    private void BumpRoll(int n)
    {
        if (_run != null) _run.CrucibleRollIndex += n;
    }

    /// <summary>행동 확정 즉시 저장(S3). 방 경계가 아니어도 진행분이 보존된다.</summary>
    private static void SaveNow(string reason) => RunFlowController.Active?.SaveNow(reason);

    /// <summary>승급 시도(확정 성공, 재료 대량). legendId는 Legends에서 택1.</summary>
    public PromoteResult TryPromote(int targetSlot, string legendId)
    {
        if (_run == null || !_run.IsRunning || _table == null)
            return PromoteResult.Reject(PromoteOutcome.RejectInvalidLegend);

        var target = GetSlot(targetSlot);
        var fuel   = _run.FuelBank;
        if (target == null || fuel == null)
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

        var panel = await Managers.UI.ShowPopupUIAndGetAsync<UI_CruciblePanel>();
        if (panel == null)
        {
            _uiOpen = false;
            if (_npc != null) _npc.SetInteractable(true);
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
    }

    // ── 배치 ────────────────────────────────────────────────

    private (Vector3 pos, Quaternion rot) ResolveNpcPlacement()
    {
        var anchor = GetComponentInChildren<ShopNpcAnchor>(true);
        if (anchor != null)
            return (anchor.transform.position, anchor.transform.rotation);

        Vector3 pos = transform.position;
        pos.y += NpcStandHeight;
        return (pos, Quaternion.identity);
    }

    // ── 잭팟 / 스탯 갱신 ────────────────────────────────────

    /// <summary>방 진입 시 돌발 이벤트 결정적 롤. Discount 18 / Fever 15 / Bounty 12 / Curse 10 / None 45.</summary>
    private CrucibleEvent RollEvent()
    {
        double r = _roomRng.NextDouble();
        if (r < 0.18) return CrucibleEvent.Discount;
        if (r < 0.33) return CrucibleEvent.Fever;
        if (r < 0.45) return CrucibleEvent.Bounty;
        if (r < 0.55) return CrucibleEvent.Curse;
        return CrucibleEvent.None;
    }

    /// <summary>상황별 재련공 동기부여 대사(도발/부추김/축하/위로). UI가 표시.</summary>
    public string GetDialogue(CrucibleMood mood)
    {
        var pool = mood switch
        {
            CrucibleMood.Taunt   => TauntLines,
            CrucibleMood.Streak  => StreakLines,
            CrucibleMood.Jackpot => JackpotLines,
            CrucibleMood.Fail    => FailLines,
            CrucibleMood.Success => SuccessLines,
            _                    => IdleLines,
        };
        if (pool == null || pool.Length == 0) return string.Empty;
        return pool[_roomRng.Next(pool.Length)];
    }

    private static readonly string[] IdleLines =
    {
        "쇠는 두드릴수록 강해지지… 운이 따라준다면 말이야.",
        "멀린이 날 여기 처박아둔 지도 천 년이야. 심심하던 참이지.",
    };
    private static readonly string[] TauntLines =
    {
        "겁이 나나? 이 정도 확률에 벌벌 떨면 영웅은 무슨.",
        "안 지를 거면 왜 왔어? 화로만 식잖아.",
        "확률이 낮다고? 낮으니까 지르는 맛이 있는 거지, 애송이.",
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
        "실패? 원래 도박이 그런 거야. 다시 지르면 되잖아.",
    };
    private static readonly string[] SuccessLines =
    {
        "좋아, 제법인데. 더 두드려볼까?",
        "쇠가 노래하는군. 멈추지 마.",
    };

    /// <summary>성공 직후 스트릭 비례 잭팟 굴림(결정적) — 성공 시 소모 재료 환불.</summary>
    private void RollJackpot(RunFuelBank fuel, int spent)
    {
        if (spent <= 0) return;
        float chance = Mathf.Min(JackpotMaxChance, JackpotBaseChance + JackpotPerStreak * (_streak - 1) + BountyBonus);

        double roll = _roomRng.NextDouble();
        BumpRoll(1);   // 잭팟 굴림도 스트림을 소비 → 복원 시 동일하게 건너뛰어야 한다

        if (roll < chance)
        {
            fuel.Add(FuelKind.EnhanceMaterial, spent);
            _lastJackpot = true;
            _lastRefund  = spent;
            Debug.Log($"[Crucible] 잭팟! 강화재료 {spent} 환불 (streak={_streak})");
        }
    }

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
