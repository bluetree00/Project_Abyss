using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 런 내 서약 목록을 관리하고 이벤트를 각 서약에 디스패치하는 오케스트레이터.
/// GameRunSession이 소유하며, PlayerController/GameRunSession 이벤트를 수신해 전달한다.
/// </summary>
public sealed class CovenantHandler
{
    // ── 상수 ────────────────────────────────────────────
    /// <summary>
    /// 서약 칸의 <b>절대 상한</b>(HUD 칸 수). 지금 맺을 수 있는 수는 <see cref="Capacity"/>다 —
    /// 기억의 제단 「서약 칸 +1」 전에는 3이다.
    /// </summary>
    public const int MaxCovenants = 4;

    /// <summary>이번 런에 새로 맺을 수 있는 서약 수(3 → 해금 시 4).</summary>
    public static int Capacity => MemoryAltarService.CovenantSlots;

    // ── 상태 ────────────────────────────────────────────
    private readonly List<CovenantBase> _covenants = new();
    private CovenantContext _ctx;
    private bool _initialized;

    // 조건부 스탯(예: 저HP 보너스) 재평가 스로틀. Tick에서 누적 → 임계 시 RefreshStats.
    // (OnChanged 구독 금지 — RefreshCovenants→Recalculate→OnChanged 무한루프 회피)
    private const float RefreshInterval = 0.2f;
    private float _refreshAccum;

    public IReadOnlyList<CovenantBase> Covenants => _covenants;

    /// <summary>이번 런에 연쇄가 처음 끝 절까지 닿았다는 한 마디를 했는가(설계서 §4 — 런당 1회).</summary>
    public bool ChainEndSaid { get; set; }

    /// <summary>서약서(문장) — 「한 장의 서약서」에선 런마다 한 장. 없으면 null.</summary>
    public CovenantSentence Sentence
    {
        get
        {
            foreach (var c in _covenants) if (c is CovenantSentence s) return s;
            return null;
        }
    }

    // ── 이벤트 ──────────────────────────────────────────
    public event Action OnCovenantListChanged;

    // ── 초기화 ──────────────────────────────────────────
    public void Initialize(CovenantContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _initialized = true;
        CovenantFxService.Preload();   // 첫 발동에서 효과 VFX가 빠지지 않게

        foreach (var c in _covenants)
            c.Initialize(_ctx);
        RefreshStats();   // 다시 묶을 때(새 챕터의 새 플레이어) 서약 스탯 기여를 새 스탯에 다시 얹는다
    }

    // 무기 교체 어댑터 상태 (OnWeaponSwap 디스패치용)
    private PlayerWeaponManager _weapons;
    private WeaponData _lastWeapon;
    private Action<WeaponData, GameObject> _onWeaponChanged;

    private PlayerController _player;

    public void BindPlayer(PlayerController player)
    {
        _player = player;
        foreach (var c in _covenants)
            c.OnBoundToPlayer(player);

        // 무기 교체 → OnWeaponSwap 디스패치 (OnWeaponChanged는 새 무기만 주므로 이전 무기 캐싱)
        UnsubscribeWeapon();
        _weapons = player != null ? player.WeaponManager : null;
        if (_weapons != null)
        {
            _lastWeapon = _weapons.CurrentWeaponData;
            _onWeaponChanged = (newData, _) =>
            {
                var prev = _lastWeapon;
                _lastWeapon = newData;
                foreach (var c in _covenants) c.OnWeaponSwap(prev, newData);
            };
            _weapons.OnWeaponChanged += _onWeaponChanged;
        }
    }

    public void Cleanup()
    {
        UnsubscribeWeapon();
        foreach (var c in _covenants)
            c.Dispose();
        _covenants.Clear();
    }

    private void UnsubscribeWeapon()
    {
        if (_weapons != null && _onWeaponChanged != null)
            _weapons.OnWeaponChanged -= _onWeaponChanged;
        _onWeaponChanged = null;
        _weapons = null;
    }

    // ── 획득 / 강화 / 진화 ──────────────────────────────
    /// <summary>새 서약 추가 (런 시작 or 분기 구간)</summary>
    public bool TryAdd(string covenantId) => TryAdd(covenantId, restoring: false);

    /// <param name="restoring">
    /// 이어하기 복원 경로. 목록·스탯·HUD는 똑같이 세우되 <b>'획득'의 부작용</b>
    /// (퀘스트 보고·획득 토스트·즉시 저장)은 건너뛴다.
    ///
    /// 복원은 새로 얻는 게 아니라 이미 갖고 있던 것을 되세우는 일이다. 구분하지 않으면
    /// 이어하기 한 번마다 퀘스트에 서약 획득이 다시 쌓이고(퀘스트 진행도는 슬롯 공용
    /// PlayerPrefs라 오염이 다른 슬롯까지 남는다), 판이 뜨자마자 이미 가진 서약이
    /// "서약 획득!" 토스트로 떠오른다.
    /// </param>
    private bool TryAdd(string covenantId, bool restoring)
    {
        // 복원은 절대 상한까지 받는다 — 칸이 3으로 줄기 전에 4개를 맺어 둔 세이브가 네 번째를 잃지 않게.
        if (_covenants.Count >= (restoring ? MaxCovenants : Capacity)) return false;
        if (_covenants.Any(c => c.CovenantId == covenantId)) return false;

        var covenant = CovenantFactory.Create(covenantId);
        if (covenant == null) return false;

        // 봉인된 짝·이미 가진 짝(등급 무관)은 새로 벼릴 수 없다. 조립 화면은 애초에 그런 조합을 제시하지 않지만
        // 그것뿐이라, UI 밖 조립 진입점이 하나라도 생기면 그대로 들어온다 — 방어를 서비스단까지 내린다.
        // 복원(restoring)은 통과시킨다: 이미 이 짝을 저장해 둔 런의 서약이 통째로 사라지기 때문이다.
        if (!restoring && covenant is AssembledCovenant asm
            && !CovenantPalette.CanPair(asm.CauseId, asm.EffectId, _covenants))
            return false;

        if (_initialized)
            covenant.Initialize(_ctx);

        _covenants.Add(covenant);
        RefreshStats();
        OnCovenantListChanged?.Invoke();

        if (restoring) return true;

        // 퀘스트: 서약 획득 보고 (target='*'이면 어떤 서약이든 수용)
        QuestEvents.Report("Covenant", covenantId);

        // S3: 서약 획득 확정 → 즉시 저장(방 경계 전에 종료해도 보존)
        RunFlowController.Active?.SaveNow("covenant-add");
        return true;
    }

    /// <summary>
    /// 서약서를 쓰거나 바꾼다(첫 쓰기 · 이어 쓰기 · 고쳐 쓰기 = 새 문장 id) — 대기방 제단에서만(설계서 §3).
    /// 옛 문장은 내려놓고 새 문장을 맺는다(귀 · 창 · 충전이 새로 시작). 남아 있는 옛 조립 서약도 함께 걷는다 — 칸 개념은 끝났다.
    /// </summary>
    public bool TryWriteSentence(string sentenceId)
    {
        if (CovenantFactory.Create(sentenceId) is not CovenantSentence made) return false;

        for (int i = _covenants.Count - 1; i >= 0; i--)
        {
            if (_covenants[i] is not CovenantSentence && _covenants[i] is not AssembledCovenant) continue;
            _covenants[i].Dispose();
            _covenants.RemoveAt(i);
        }

        if (_initialized) made.Initialize(_ctx);
        if (_player != null) made.OnBoundToPlayer(_player);
        _covenants.Add(made);
        RefreshStats();
        OnCovenantListChanged?.Invoke();

        QuestEvents.Report("Covenant", sentenceId);
        RunFlowController.Active?.SaveNow("covenant-write");
        return true;
    }

    /// <summary>서약 강화 — Basic → Enhanced (이벤트 방)</summary>
    public bool TryEnhance(string covenantId)
    {
        var covenant = Find(covenantId);
        if (covenant == null || covenant.Stage != CovenantStage.Basic) return false;

        covenant.Stage = CovenantStage.Enhanced;
        RefreshStats();
        OnCovenantListChanged?.Invoke();
        return true;
    }

    /// <summary>서약 진화 — Enhanced → Evolved (보스 처치)</summary>
    public bool TryEvolve(string covenantId)
    {
        var covenant = Find(covenantId);
        if (covenant == null || covenant.Stage != CovenantStage.Enhanced) return false;

        covenant.Stage = CovenantStage.Evolved;
        RefreshStats();
        OnCovenantListChanged?.Invoke();
        return true;
    }

    /// <summary>이어하기: 저장된 서약 목록(id + 단계)을 복원한다. Initialize 이후 호출.</summary>
    public void RestoreSelections(IEnumerable<CovenantSaveEntry> entries)
    {
        if (entries == null) return;

        // 옛 조립 서약(asm: 여러 개) → 서약서 한 장(sen:) — 「한 장의 서약서」 이행(10-02)
        var list = new List<CovenantSaveEntry>(entries);
        var asmIds = new List<string>();
        foreach (var e in list) if (e != null && e.id != null && e.id.StartsWith(AssembledCovenant.Prefix)) asmIds.Add(e.id);
        if (asmIds.Count > 0)
        {
            string migrated = CovenantSentenceMigration.FromAssembled(asmIds);
            list.RemoveAll(e => e != null && e.id != null && e.id.StartsWith(AssembledCovenant.Prefix));
            if (migrated != null && !list.Exists(e => e != null && e.id != null && e.id.StartsWith(CovenantSentence.Prefix)))
                list.Insert(0, new CovenantSaveEntry { id = migrated, stage = 0 });
            Debug.Log($"[CovenantHandler] 옛 조립 서약 {asmIds.Count}개 → 서약서 {migrated ?? "(없음)"}");
        }

        foreach (var e in list)
        {
            if (e == null || string.IsNullOrEmpty(e.id)) continue;
            if (!TryAdd(e.id, restoring: true))
            {
                // 해석 못 한 id는 슬롯을 차지하지 않는다(CovenantFactory가 null 반환).
                // 조용히 넘기면 "서약이 하나 사라졌다"가 버그 리포트로만 돌아온다 — 흔적을 남긴다.
                Debug.LogWarning($"[CovenantHandler] 서약 복원 실패(미해결 id·중복·슬롯 초과): {e.id}");
                continue;
            }

            var stage = (CovenantStage)e.stage;
            if (stage >= CovenantStage.Enhanced) TryEnhance(e.id);
            if (stage >= CovenantStage.Evolved)  TryEvolve(e.id);
        }
    }

    // ── 스탯 레이어 연동 ────────────────────────────────
    /// <summary>모든 서약의 StatModifier 합산 → PlayerRuntimeStats.RefreshCovenants()에서 사용</summary>
    public IEnumerable<StatModifier> GetAllModifiers()
        => _covenants.SelectMany(c => c.GetStatModifiers());

    private void RefreshStats()
    {
        if (_initialized)
            _ctx.Stats.RefreshCovenants(this);
    }

    // ── 이벤트 디스패치 ─────────────────────────────────
    public void OnRoomEnter()
    {
        foreach (var c in _covenants) c.OnRoomEnter();
    }

    public void OnRoomClear()
    {
        foreach (var c in _covenants) c.OnRoomClear();
    }

    public void OnKill(GameObject target)
    {
        foreach (var c in _covenants) c.OnKill(target);
    }

    public void OnAttackHit(GameObject target, float damage)
    {
        foreach (var c in _covenants) c.OnAttackHit(target, damage);
    }

    public void OnTakeDamage(float damage)
    {
        foreach (var c in _covenants) c.OnTakeDamage(damage);
    }

    public void OnSkillUse(SkillType skill)
    {
        foreach (var c in _covenants) c.OnSkillUse(skill);
    }

    public void Tick(float deltaTime)
    {
        foreach (var c in _covenants) c.Tick(deltaTime);

        // 조건부 스탯 주기적 재평가 — 서약 보유 시에만(0개면 불필요한 Recalculate/OnChanged 방지)
        if (_covenants.Count == 0) return;
        _refreshAccum += deltaTime;
        if (_refreshAccum >= RefreshInterval)
        {
            _refreshAccum = 0f;
            RefreshStats();
        }
    }

    // ── 피해 파이프라인 ──────────────────────────────────
    public void ModifyOutgoingDamage(ref float damage, CombatContext ctx)
    {
        foreach (var c in _covenants) c.ModifyOutgoingDamage(ref damage, ctx);
    }

    public void ModifyIncomingDamage(ref float damage, CombatContext ctx)
    {
        foreach (var c in _covenants) c.ModifyIncomingDamage(ref damage, ctx);
    }

    /// <summary>통보 한 줄용 float 반환 래퍼 — 호출부: dmg = handler?.ModifyIncoming(dmg, ctx) ?? dmg;</summary>
    public float ModifyIncoming(float damage, CombatContext ctx)
    {
        ModifyIncomingDamage(ref damage, ctx);
        return damage;
    }

    /// <summary>통보 한 줄용 float 반환 래퍼 — 호출부: dmg = handler?.ModifyOutgoing(dmg, ctx) ?? dmg;</summary>
    public float ModifyOutgoing(float damage, CombatContext ctx)
    {
        ModifyOutgoingDamage(ref damage, ctx);
        return damage;
    }

    /// <summary>HP 0 시 호출. 어느 하나라도 true 반환하면 사망 방지.</summary>
    public bool TryPreventDeath()
    {
        foreach (var c in _covenants)
        {
            if (c.TryPreventDeath()) return true;
        }
        return false;
    }

    /// <summary>치명타 오버라이드 질의. 첫 응답 서약(갤러해드)의 결과를 반환. CombatCalculator.RollCrit이 호출.</summary>
    public bool TryGetCritOverride(WeaponData weapon, out bool forceCrit, out float minFloorRatio)
    {
        foreach (var c in _covenants)
        {
            if (c.TryProvideCritOverride(weapon, out forceCrit, out minFloorRatio))
                return true;
        }
        forceCrit = false; minFloorRatio = 0f; return false;
    }

    // ── 메커닉 파이프라인 ────────────────────────────────
    public void ModifySkillEffect(SkillType skill, ref SkillEffectContext ctx)
    {
        foreach (var c in _covenants) c.ModifySkillEffect(skill, ref ctx);
    }

    public void ModifySkillCost(SkillType skill, ref SkillCostContext ctx)
    {
        foreach (var c in _covenants) c.OverrideSkillCost(skill, ref ctx);
    }

    // ── 버프창 표시 수집 ────────────────────────────────
    /// <summary>
    /// 현재 "발동/지속 상태"를 노출하는 서약만 버프창 표시 항목으로 수집(옵트인).
    /// 보유 서약 상시 목록은 CovenantPanelView가 담당하므로 여기선 일시 상태만 모은다. 읽기 전용.
    /// </summary>
    public void CollectBuffViews(List<BuffViewItem> into)
    {
        if (into == null) return;
        for (int i = 0; i < _covenants.Count; i++)
            if (_covenants[i].TryGetBuffView(out var item))
                into.Add(item);
    }

    // ── 헬퍼 ────────────────────────────────────────────
    public CovenantBase Find(string covenantId)
        => _covenants.Find(c => c.CovenantId == covenantId);

    public bool Has(string covenantId)
        => _covenants.Any(c => c.CovenantId == covenantId);
}
