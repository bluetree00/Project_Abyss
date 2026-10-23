using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 쉼터 — 챕터 중간(보스까지 전투방 절반을 지난 뒤) 한 번 들르는 휴게 공간.
/// 모닥불의 「체력 회복 40%」 또는 모루의 「무료 재련 1회」(근접 무기 재련 또는 원거리 파츠 재련, 재료 없음) 중 <b>하나만</b>.
/// 사용자(10-01): 「챕터 중간 중간 휴게 공간 스테이지가 필요」. 쉬느냐 · 강해지느냐를 고르게 해 쉼터도 결정이 되게 한다.
///
/// [F]는 모닥불 · 모루 가운데 한 곳 — 누르면 두 선택지를 한 창(<see cref="UI_ConfirmDialog"/>)에서 비교해 고른다.
/// 오른쪽 = 체력 회복 · 왼쪽 = 무기 강화 · ESC · 바깥 = 고르지 않고 닫기.
/// 10-18 사용자 「쉰다와 벼린다의 선택은 범위가 겹쳐서 F를 누르면 쉰다의 선택밖에 되지 않는다」 — 예전엔 둘이 따로 [F]를 가졌는데
/// 모닥불 프리팹 배율(1.5)이 트리거 반경까지 키워(2.4 → 3.6 m) 3 m 옆 모루 범위를 덮었다. F 한 번에 둘이 같이 불려 「쉰다」가 먼저 닫았다.
/// 「벼린다 같은 표현은 … 직관적인 표현으로」 — 동사 한 마디 대신 결과(체력 회복 N% · 무기 강화 +1).
///
/// 배치는 방 CSV 토큰을 그대로 쓴다 — NS = 모닥불 자리 · NC = 모루 자리 · NP&lt;n&gt; = 소품(서비스 방과 같은 규약).
/// 전투가 없는 방이라 들어오면 출구가 바로 열린다 — 아무것도 고르지 않고 지나가도 된다.
/// </summary>
public sealed class RestRoomController : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float RestHealRatio          = 0.4f;
    private const float NightmareRestHealRatio = 0.25f;
    private const float ChoiceReach            = 2.4f;    // 모닥불 · 모루 중심에서 이만큼까지는 [F]가 닿는다(예전 반경)
    private const float ChoicePromptHeight     = 2.2f;
    private const string AnvilName             = "RestAnvil";
    private const string ForgeLabel            = "무료 재련 1회";   // 10-18 재련 개편 D2 — 근접 +3 · 증강 고르기 / 원거리 파츠 카드(10-22 B7: 둘 다 되면 고른다)
    private static readonly Color RestInk  = new(1f, 0.72f, 0.38f, 1f);
    private static readonly Color ForgeInk = new(1f, 0.85f, 0.55f, 1f);

    // ── Private ─────────────────────────────────────────────
    private GameRunSession     _run;
    private ShopNpcInteraction _choiceIt;
    private Transform          _fire;
    private Transform          _anvil;
    private bool               _chosen;

    // ── Lifecycle ───────────────────────────────────────────
    private void OnDestroy()
    {
        if (_choiceIt != null) _choiceIt.OnInteract -= OpenChoice;
    }

    // ── Public Methods ──────────────────────────────────────
    /// <param name="decor">[0] = 모루(NC 자리) · [1~] = 소품(NP&lt;n&gt;)</param>
    public void Initialize(GameRunSession run, GameObject firePrefab, GameObject[] decor)
    {
        _run = run;
        ServiceRoomDecorPlacer.SyncPhysics();

        var anchor  = GetComponentInChildren<ShopNpcAnchor>(true);
        Vector3 pos = anchor != null ? anchor.transform.position : transform.position;
        float groundY = ServiceRoomDecorPlacer.GroundYAt(pos, pos.y);

        var fire = ServiceRoomDecorPlacer.Place(firePrefab, pos, 0f, groundY, transform, "RestFire");
        if (fire == null) fire = new GameObject("RestFire");
        if (fire.transform.parent != transform) fire.transform.SetParent(transform, true);
        _fire = fire.transform;

        if (decor != null && decor.Length > 0)
            ServiceRoomDecorPlacer.TryPlaceFromAnchors(transform, decor, pos, groundY, AnvilName);
        _anvil = transform.Find(AnvilName);

        // [F]는 하나 — 모닥불 · 모루 가운데의 배율 없는 빈 오브젝트에 단다(프리팹 배율이 반경 · 글자를 키우지 않게).
        Vector3 center = _anvil != null ? (_fire.position + _anvil.position) * 0.5f : _fire.position;
        Vector3 span   = _anvil != null ? _anvil.position - _fire.position : Vector3.zero;
        span.y = 0f;
        float radius = span.magnitude * 0.5f + ChoiceReach;
        var host = new GameObject("RestChoice").transform;
        host.position = new Vector3(center.x, groundY, center.z);
        host.SetParent(transform, true);
        _choiceIt = ChapterSpring.AddInteraction(host, Prompt(), ChoicePromptHeight, radius);
        _choiceIt.OnInteract += OpenChoice;

        AnnounceAsync().Forget();
        Debug.Log($"[RestRoom] 쉼터 준비 — 모닥불 {(fire != null ? "O" : "X")} · 모루 {(_anvil != null ? "O" : "X")} · [F] 하나(반경 {radius:0.0} m)");
    }

    // ── Private Methods ─────────────────────────────────────
    private static float HealRatio => NightmareRules.IsActive ? NightmareRestHealRatio : RestHealRatio;

    private static string RestLabel => $"체력 회복 {Mathf.RoundToInt(HealRatio * 100f)}%";

    private static int HealAmount(int maxHp) => Mathf.Max(1, Mathf.RoundToInt(maxHp * HealRatio));

    private static string Dim(string text) => $"<size=85%><color=#9AA0A6>{text}</color></size>";

    /// <summary>머리 위 [F] — 1행 「쉼터」 · 2행 고를 수 있는 것(결과로). 한 줄이면 너무 길어 두 줄로 나눈다.</summary>
    private string Prompt()
    {
        string options = _anvil == null       ? RestLabel
                       : CanForge(out _)      ? $"{RestLabel}  또는  {ForgeLabel}"
                                              : $"{RestLabel}  <color=#9AA0A6>· 무기는 최대 강화</color>";
        return $"<color=#FFD700>[F]</color> 쉼터\n<size=80%>{options}</size>";
    }

    /// <summary>
    /// 무료 재련을 할 수 있나 + 설명 — 근접(칸 0) · 원거리(칸 1)를 따로 본다. 둘 다 되면 두 줄(「무기 재련 — …」 · 「파츠 재련 — …」).
    /// 10-22 B7: 예전엔 지금 든 무기만 봐서, 런은 늘 근접을 든 채 시작하므로 원거리 재련은 무기를 바꿔 들어야만 나왔다.
    /// </summary>
    private bool CanForge(out string detail)
    {
        bool melee = CanForgeSlot(PlayerWeaponManager.Slot0, out var meleeDetail);
        bool ranged = CanForgeSlot(PlayerWeaponManager.Slot1, out var rangedDetail);
        detail = melee && ranged ? $"\n   무기 재련 — {meleeDetail}\n   파츠 재련 — {rangedDetail}"
               : melee ? meleeDetail : rangedDetail;
        return melee || ranged;
    }

    private bool CanForgeSlot(int slot, out string detail)
    {
        detail = null;
        var wm = _run?.Player?.WeaponManager;
        if (wm == null) return false;
        if (slot == PlayerWeaponManager.Slot1 && !SkillTierResolver.IsRanged(wm.Weapon1Data)) return false;   // 칸 1 = 원거리(재련소와 같은 규약)
        return WeaponReforgeService.CanFreeReforge(slot, out detail);
    }

    /// <summary>들어오면 한 줄로 규칙을 알린다 — 「하나만」이 안 보이면 둘 다 하려다 하나가 꺼진 걸 고장으로 읽는다.</summary>
    private async UniTaskVoid AnnounceAsync()
    {
        try { await UniTask.Delay(1200, ignoreTimeScale: true, cancellationToken: destroyCancellationToken); }
        catch (System.OperationCanceledException) { return; }
        Notice(_anvil != null ? $"<color=#FFC982>쉼터</color> — {RestLabel} 또는 {ForgeLabel} (하나만)"
                              : $"<color=#FFC982>쉼터</color> — {RestLabel}");
    }

    private void Close()
    {
        _chosen = true;
        if (_choiceIt != null) _choiceIt.SetInteractable(false);
    }

    private static void Notice(string message)
    {
        var hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (hud != null) hud.ShowBuffNotice(message);
    }

    // ── Event Handlers ──────────────────────────────────────
    /// <summary>[F] — 두 선택지를 한 창에서 비교한다. 지금 의미 없는 쪽(체력 가득 · 최대 강화)은 버튼 대신 회색 이유만.</summary>
    private void OpenChoice()
    {
        if (_chosen || UI_ConfirmDialog.IsOpen) return;
        var stats = _run?.Player?.RuntimeStats;
        if (stats == null) return;

        bool canRest = stats.Hp < stats.MaxHp;
        string forgeDetail = null;
        bool canForge = _anvil != null && CanForge(out forgeDetail);

        string body = $"{RestLabel}   " + (canRest ? $"체력 {stats.Hp} → {Mathf.Min(stats.MaxHp, stats.Hp + HealAmount(stats.MaxHp))}"
                                                  : Dim("체력이 가득하다"));
        if (_anvil != null)
            body += $"\n{ForgeLabel}   " + (canForge ? forgeDetail : Dim("더 올릴 것이 없다"));
        if (canRest && canForge) body += "\n" + Dim("ESC — 고르지 않고 닫기");
        body = UIKeywordInk.Values(body);

        if (canRest && canForge)
        {
            UI_ConfirmDialog.ShowChoice("쉼터 — 하나만 고를 수 있다", body, ForgeLabel, RestLabel, Forge, Rest);
        }
        else if (canRest)  UI_ConfirmDialog.Show("쉼터", body, RestLabel, "닫기", Rest);
        else if (canForge) UI_ConfirmDialog.Show("쉼터", body, ForgeLabel, "닫기", Forge);
        else               UI_ConfirmDialog.Show("쉼터", body + "\n" + Dim("지금은 고를 것이 없다"), "닫기", null, null);
    }


    private void Rest()
    {
        if (_chosen) return;
        var stats = _run?.Player?.RuntimeStats;
        if (stats == null) return;
        if (stats.Hp >= stats.MaxHp)
        {
            Notice("<color=#9AA0A6>체력이 가득하다 — 지금은 회복할 필요가 없다</color>");
            return;
        }
        int before = stats.Hp;
        _run.Player.Heal(HealAmount(stats.MaxHp));
        Close();
        RunFx.Play(RunFxSlot.Ring, _fire.position + Vector3.up * 0.1f, 1f, RestInk);
        Managers.Sound?.PlayEvent(SoundEvent.RestHeal);
        Notice($"<color=#FFC982>쉼터</color> — 체력 회복 +{stats.Hp - before}");
    }

    /// <summary>무료 재련 — 근접 · 원거리 중 되는 쪽. 둘 다 되면 한 번 더 고른다(재련소 「무엇을 재련할까」와 같은 말).</summary>
    private void Forge()
    {
        if (_chosen || _anvil == null) return;
        bool melee = CanForgeSlot(PlayerWeaponManager.Slot0, out var meleeDetail);
        bool ranged = CanForgeSlot(PlayerWeaponManager.Slot1, out var rangedDetail);
        if (melee && ranged)
        {
            string body = UIKeywordInk.Values($"무기 재련   {meleeDetail}\n파츠 재련   {rangedDetail}\n" + Dim("ESC — 고르지 않고 닫기"));
            UI_ConfirmDialog.ShowChoice("무료 재련 — 무엇을 재련할까", body, "무기 재련", "파츠 재련",
                                        () => ForgeSlot(PlayerWeaponManager.Slot0), () => ForgeSlot(PlayerWeaponManager.Slot1));
            return;
        }
        if (melee || ranged) ForgeSlot(melee ? PlayerWeaponManager.Slot0 : PlayerWeaponManager.Slot1);
    }

    private void ForgeSlot(int slot)
    {
        if (_chosen || _anvil == null || !CanForgeSlot(slot, out _)) return;
        var wm = _run?.Player?.WeaponManager;
        if (!WeaponReforgeService.GrantFreeReforge(slot)) return;   // 근접 = +3 · 증강 고르기 / 원거리 = 파츠 카드 고르기
        var weapon = slot == PlayerWeaponManager.Slot1 ? wm.Weapon1Data : wm.Weapon0Data;
        Close();
        RunFx.Play(RunFxSlot.Burst, _anvil.position + Vector3.up * 0.8f, 0.6f, ForgeInk);
        Managers.Sound?.PlayEvent(SoundEvent.RestForge);
        Notice(SkillTierResolver.IsRanged(weapon) ? $"<color=#FFC982>쉼터</color> — {ForgeLabel} · 파츠 카드를 고른다"
                                                  : $"<color=#FFC982>쉼터</color> — {ForgeLabel} · {weapon.displayName} 재련 {WeaponReforgeService.CountAt(weapon.enhanceLevel)}회");
    }
}
