using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;

/// <summary>
/// 유물 기본 규칙을 처음 겪는 순간 — 멀린 한 줄(10-22 E1 D9 「처음 겪는 순간 한 줄」 · 계정에 한 번 · 유물마다).
/// <list type="bullet">
/// <item>가웨인 — 첫 여명(첫 전투) · 첫 정오 · 첫 황혼. 랜슬롯 — 첫 광기 · 첫 광란 · 광란 끝.</item>
/// <item>해방기 첫 런 — 「이제 조각 2개면 진화」(D2 · <see cref="RelicEvolution.Unlocked"/>).</item>
/// <item>「되살아난 기억」 첫 설명 팝업 기록도 유물마다(<see cref="TakeGuide"/>).</item>
/// </list>
/// 대사는 DIALOGUE_DATA(<c>RelicTip_*</c> — CDN · 프로젝트 보충 CSV) 우선, 없으면 코드 폴백. 기록 = 세이브 records <c>seen.relic_tip.&lt;키&gt;</c>.
/// 기록을 못 쓰면(세이브 없음) 띄우지 않는다 — 매 런 같은 말이 반복되느니 안 보이는 쪽이 낫다(<see cref="RelicMemoryHints"/>와 같은 판단).
/// 언제 부르는지는 유물 HUD가 정한다(<see cref="RelicMemoryHudPresenter"/> — 게이지 · 광기 사건).
/// </summary>
public static class RelicFirstTips
{
    // ── Constants ────────────────────────────────────────
    public const string GawainDawn        = "gawain.dawn";
    public const string GawainNoon        = "gawain.noon";
    public const string GawainDusk        = "gawain.dusk";
    public const string LancelotMadness   = "lancelot.madness";
    public const string LancelotFrenzy    = "lancelot.frenzy";
    public const string LancelotFrenzyEnd = "lancelot.frenzy_end";
    public const string EvolutionOpen     = "evolution_open";

    public const string RecordPrefix = "seen.relic_tip.";
    public const string GuidePrefix  = "seen.relic_draft_guide.";   // 옛 키 「seen.relic_draft_guide」(세이브당 1번)는 읽지 않는다

    // ── Static ───────────────────────────────────────────
#if UNITY_EDITOR
    /// <summary>실측 도구 전용 — null이 아니면 세이브 대신 이 집합에 적고 저장하지 않는다(실측이 진짜 세이브를 건드리지 않게).</summary>
    public static HashSet<string> ProbeRecords;
#endif

    // ── Public Methods ───────────────────────────────────

    /// <summary>그 순간을 처음 겪으면 멀린 한 줄을 띄우고 기록한다. 띄웠으면 true.</summary>
    public static bool TryShowOnce(string tip)
    {
        if (string.IsNullOrEmpty(tip) || UI_BossBark.Instance == null) return false;   // 자막이 없으면 기록하지 않는다 — 다음 기회에
        if (!MarkOnce(RecordPrefix + tip, showWithoutSave: false)) return false;
        if (!UI_BossBark.ShowDialogue(SequenceOf(tip)))
            UI_BossBark.Show(Fallback(tip), BossBarkType.MerlinNarration, DialogueSpeaker.Merlin);
        return true;
    }

    /// <summary>
    /// 첫 설명 팝업을 띄울 차례인가 — 유물마다 한 번(기록한다). 기록을 못 쓰면(세이브 없음) 매번 true —
    /// 고르기 전에 읽어야 하는 글이라 안 보이는 쪽이 더 나쁘다(옛 판단 그대로).
    /// </summary>
    public static bool TakeGuide(string relicId)
        => MarkOnce(GuidePrefix + (string.IsNullOrEmpty(relicId) ? "relic" : relicId), showWithoutSave: true);

    /// <summary>그 순간의 한 줄(대사 표 첫 줄 → 없으면 코드 폴백) — 실측 · 기록용.</summary>
    public static string TextOf(string tip)
    {
        string seq = SequenceOf(tip);
        var lines = seq != null ? Managers.DialogueData?.GetLines(seq) : null;
        return lines != null && lines.Length > 0 && lines[0] != null && !string.IsNullOrEmpty(lines[0].text) ? lines[0].text : Fallback(tip);
    }

    /// <summary>대사 표 시퀀스 키 — 「RelicTip_Gawain_Dawn」 꼴.</summary>
    public static string SequenceOf(string tip) => tip switch
    {
        GawainDawn        => "RelicTip_Gawain_Dawn",
        GawainNoon        => "RelicTip_Gawain_Noon",
        GawainDusk        => "RelicTip_Gawain_Dusk",
        LancelotMadness   => "RelicTip_Lancelot_Madness",
        LancelotFrenzy    => "RelicTip_Lancelot_Frenzy",
        LancelotFrenzyEnd => "RelicTip_Lancelot_FrenzyEnd",
        EvolutionOpen     => "RelicTip_Evolution_Open",
        _                 => null,
    };

    // ── Private Methods ──────────────────────────────────

    /// <summary>처음이면 기록하고 true. 세이브가 없으면 <paramref name="showWithoutSave"/>.</summary>
    private static bool MarkOnce(string key, bool showWithoutSave)
    {
#if UNITY_EDITOR
        if (ProbeRecords != null) return ProbeRecords.Add(key);
#endif
        var backend = BackendGameData.Instance;
        var data    = backend != null ? backend.Data : null;
        if (data == null) return showWithoutSave;
        if (!data.SetRecordMax(key, 1)) return false;   // 이미 봤다
        backend.SaveAsync().Forget();
        return true;
    }

    /// <summary>대사 표에 아직 없을 때(CDN 업로드 전) — DIALOGUE_DATA 「RelicTip_*」 행과 같은 글. 멀린 = 동료 반말 · 뜻이 바로.</summary>
    private static string Fallback(string tip) => tip switch
    {
        GawainDawn        => "가웨인의 해는 싸울 때만 돌아. 지금은 여명 — 적을 칠 때마다 태양흔이 쌓여.",
        GawainNoon        => "정오야! 쌓아 둔 태양흔이 한꺼번에 터지고 너도 세져. 지금 Q로 해를 떨어뜨려.",
        GawainDusk        => "황혼엔 Q가 잠겨. 적을 쓰러뜨릴수록 다음 해가 빨리 떠.",
        LancelotMadness   => "칠수록 광기가 차올라. 세지는 만큼 더 아프게 맞으니 조심해.",
        LancelotFrenzy    => "광기 40 — 광란이야! 지금 Q로 심판을 내려.",
        LancelotFrenzyEnd => "광란이 끝나면 광기는 0부터 다시 쌓여.",
        EvolutionOpen     => "이제 기억 조각 2개를 모은 채 보스를 쓰러뜨리면 유물이 진화해.",
        _                 => string.Empty,
    };
}
