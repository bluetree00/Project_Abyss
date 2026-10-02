using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 쉼터 — 챕터 중간(보스까지 전투방 절반을 지난 뒤) 한 번 들르는 휴게 공간.
/// 모닥불에서 「쉰다」(체력 40%) 또는 모루에서 「벼린다」(지금 근접 무기 강화 +1, 재료 · 확률 없음) 중 <b>하나만</b>.
/// 사용자(10-01): 「챕터 중간 중간 휴게 공간 스테이지가 필요」. 쉬느냐 · 강해지느냐를 고르게 해 쉼터도 결정이 되게 한다.
///
/// 배치는 방 CSV 토큰을 그대로 쓴다 — NS = 모닥불 자리 · NC = 모루 자리 · NP&lt;n&gt; = 소품(서비스 방과 같은 규약).
/// 전투가 없는 방이라 들어오면 출구가 바로 열린다 — 아무것도 고르지 않고 지나가도 된다.
/// </summary>
public sealed class RestRoomController : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float RestHealRatio          = 0.4f;
    private const float NightmareRestHealRatio = 0.25f;
    private const float FirePromptHeight       = 1.8f;
    private const float AnvilPromptHeight      = 1.6f;
    private const string AnvilName             = "RestAnvil";
    private static readonly Color RestInk  = new(1f, 0.72f, 0.38f, 1f);
    private static readonly Color ForgeInk = new(1f, 0.85f, 0.55f, 1f);

    // ── Private ─────────────────────────────────────────────
    private GameRunSession     _run;
    private ShopNpcInteraction _restIt;
    private ShopNpcInteraction _forgeIt;
    private bool               _chosen;

    // ── Lifecycle ───────────────────────────────────────────
    private void OnDestroy()
    {
        if (_restIt  != null) _restIt.OnInteract  -= Rest;
        if (_forgeIt != null) _forgeIt.OnInteract -= Forge;
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
        _restIt = ChapterSpring.AddInteraction(fire.transform, Prompt("쉰다", $"체력 +{Mathf.RoundToInt(HealRatio * 100f)}%"), FirePromptHeight);
        _restIt.OnInteract += Rest;

        if (decor != null && decor.Length > 0)
            ServiceRoomDecorPlacer.TryPlaceFromAnchors(transform, decor, pos, groundY, AnvilName);
        var anvil = transform.Find(AnvilName);
        if (anvil != null)
        {
            bool canForge = CanForge(out _);
            _forgeIt = ChapterSpring.AddInteraction(anvil, canForge ? Prompt("벼린다", "지금 무기 강화 +1")
                                                                    : "<color=#9AA0A6>모루 — 더 벼릴 수 없다(최대 강화)</color>", AnvilPromptHeight);
            _forgeIt.OnInteract += Forge;
            if (!canForge) _forgeIt.SetInteractable(false);
        }

        AnnounceAsync().Forget();
        Debug.Log($"[RestRoom] 쉼터 준비 — 모닥불 {(fire != null ? "O" : "X")} · 모루 {(anvil != null ? "O" : "X")}");
    }

    // ── Private Methods ─────────────────────────────────────
    private static float HealRatio => NightmareRules.IsActive ? NightmareRestHealRatio : RestHealRatio;

    private static string Prompt(string verb, string effect) => $"<color=#FFD700>[F]</color> {verb} — {effect}";

    private bool CanForge(out WeaponData weapon)
    {
        weapon = _run?.Player?.WeaponManager?.Weapon0Data;
        var table = WeaponEnhanceService.Table;
        return weapon != null && table != null && !WeaponEnhanceService.IsMaxed(weapon, table);
    }

    /// <summary>들어오면 한 줄로 규칙을 알린다 — 「하나만」이 안 보이면 둘 다 하려다 하나가 꺼진 걸 고장으로 읽는다.</summary>
    private async UniTaskVoid AnnounceAsync()
    {
        try { await UniTask.Delay(1200, ignoreTimeScale: true, cancellationToken: destroyCancellationToken); }
        catch (System.OperationCanceledException) { return; }
        Notice("<color=#FFC982>쉼터</color> — 모닥불에서 쉬거나, 모루에서 벼린다(하나만)");
    }

    private void Close()
    {
        _chosen = true;
        if (_restIt  != null) _restIt.SetInteractable(false);
        if (_forgeIt != null) _forgeIt.SetInteractable(false);
    }

    private static void Notice(string message)
    {
        var hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (hud != null) hud.ShowBuffNotice(message);
    }

    // ── Event Handlers ──────────────────────────────────────
    private void Rest()
    {
        if (_chosen) return;
        var stats = _run?.Player?.RuntimeStats;
        if (stats == null) return;
        if (stats.Hp >= stats.MaxHp)
        {
            Notice("<color=#9AA0A6>지금은 쉴 필요가 없다 — 체력이 가득하다</color>");
            return;
        }
        int before = stats.Hp;
        _run.Player.Heal(Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * HealRatio)));
        Close();
        RunFx.Play(RunFxSlot.Ring, _restIt.transform.position + Vector3.up * 0.1f, 1f, RestInk);
        Notice($"<color=#FFC982>쉼터</color> — 불가에서 숨을 골랐다 · 체력 +{stats.Hp - before}");
    }

    private void Forge()
    {
        if (_chosen || !CanForge(out var weapon)) return;
        if (!WeaponEnhanceService.GrantLevel(weapon, WeaponEnhanceService.Table)) return;
        _run.ReportEnhanceLevel(weapon.enhanceLevel);
        Close();
        RunFx.Play(RunFxSlot.Burst, _forgeIt.transform.position + Vector3.up * 0.8f, 0.6f, ForgeInk);
        Notice($"<color=#FFC982>쉼터</color> — {weapon.displayName} 강화 +{weapon.enhanceLevel}");
    }
}
