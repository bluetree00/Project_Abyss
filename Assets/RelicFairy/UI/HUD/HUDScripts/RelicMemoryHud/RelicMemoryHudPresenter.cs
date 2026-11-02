using UnityEngine;

/// <summary>
/// 유물 HUD 확장(유물 성장 v2 §6-1) Presenter — 조각이 「어디서 켜지는지」를 유물 게이지 위에 얹는다.
/// <list type="bullet">
/// <item>Provider: 유물 허브(<see cref="GawainMemoryHub"/> · <see cref="LancelotMemoryHub"/>) · <see cref="PlayerLoadout"/> · <see cref="ZenithGauge"/>.</item>
/// <item>View: <see cref="RelicMemoryHudView"/> — 게이지 루트 아래에 코드로 짓는 위젯(해시계 세 호 / 광기 눈금 넷 · 원한 불씨).</item>
/// </list>
/// 허브가 생기기 전엔 숨긴다(<c>Bound</c> 사건으로 깨어난다) — 10-22 D11부터 허브는 유물을 붙일 때 생겨 조각 없이 첫 전투부터 보인다(예전엔 첫 조각과 함께).
/// 갱신은 사건으로(조각 · 원한 · 시간대 · 계단 넘기 · 엇갈린 빛) — 해시계 바늘 · 광기 막대 떨림 · 멈춤(피 냄새 남은 초)만 매 프레임 읽는다.
/// <para>10-22 E1 D9 — 유물 기본 규칙을 처음 겪는 순간 멀린 한 줄(<see cref="RelicFirstTips"/>)도 여기서 부른다: 허브와 상관없이 게이지 · 광기 사건을 듣는다
/// (첫 여명 = 해가 흐르기 시작한 첫 전투 · 첫 정오 · 첫 황혼 / 첫 광기 · 첫 광란 · 광란 끝 · 해방기 첫 런의 진화 한 줄). 묶는 동안 사건마다 한 번만 묻는다.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class RelicMemoryHudPresenter : MonoBehaviour
{
    // 해시계 처음 안내(10-18 사용자 「색별로 있는 UI바는 뭘 의미하는지도 모르겠어」) — 계정에 한 번(빌드 세이브 초기화가 지운다)
    // 10-30 H-05 — 첫 전투(조각 0개) · 6초 한 번이라 무슨 말인지 몰랐다 → <b>첫 조각을 고른 뒤</b> 8초 · 조각이 보스 보상이라는 말부터(키 v2 — 옛 안내를 본 계정도 한 번 더)
    private const string DialHintKey  = "relic_hud_dial_hint_v2";
    private const string DialHintText = "보스 보상 기억 조각 — 같은 시간대 2개 · 4개면 그 시간대가 강해진다";
    private const float  DialHintSec  = 8f;
    // 광기 눈금 처음 안내(10-22 E1 A6 — 하루 띠 안내와 짝) — 10-30 H-12 · H-10: 첫 조각 뒤에 · 이어서 마름모 색 범례
    private const string LadderHintKey    = "relic_hud_ladder_hint_v2";
    private const string LadderHintText   = "꼬리표가 광기 10~40인 조각 — 2 · 3 · 4개면 한 단계씩 강해진다";
    private const string LadderLegendText = "마름모 붉음 = 그 광기 칸 조각을 가졌다 · 금 = 조각 2개 이상이라 단계가 켜짐";
    private const float  LadderLegendSec  = 6f;
    // 엇갈린 빛 별(10-30 H-07 — 별이 무엇인지 아무 단서가 없었다): 빈 별이 처음 뜰 때 한 번(계정) · 켜지는 순간마다 짧게
    private const string CrossHintKey = "relic_hud_crosslight_hint_v1";
    private const float  CrossHintSec = 6f;
    private const float  CrossOnSec   = 2.5f;
    private const string GawainCrossOnText   = "엇갈린 빛 켜짐 — 다음 정오 폭발 때 태양흔이 남는다";
    private const string LancelotCrossOnText = "엇갈린 빛 켜짐 — 다음 낙인 폭발 때 낙인이 남는다";
    private const float  FirstFightProgress = 0.05f;   // 여명이 이만큼 흘렀으면 싸우는 중(해는 적이 있을 때만 돈다)

    private RelicMemoryHudView _view;
    private IRelicResource     _res;
    private RectTransform      _root;
    private PlayerLoadout      _loadout;
    private GawainMemoryHub    _gHub;
    private LancelotMemoryHub  _lHub;
    private GrudgeStore        _grudge;
    private readonly bool[]    _rungs = new bool[4];
    // 처음 겪는 순간(10-22 E1 D9)
    private ZenithGauge  _tipSun;
    private MadnessStack _tipMadness;
    private bool _tipFightDone, _tipNoonDone, _tipDuskDone, _tipFrenzyDone, _tipFrenzyEndDone;

    /// <summary>런(던전) 안인가 — 처음 겪는 순간 한 줄은 거점(허수아비)에선 내지 않는다.</summary>
    private static bool InRun => GameRunBootstrapper.Instance?.Run?.IsRunning ?? false;

    /// <summary>CombatPanelView가 유물 리소스를 묶을 때 부른다. root = 그 게이지의 루트(가웨인 태양 · 랜슬롯 막대), 없으면 숨김.</summary>
    public static void Bind(Component host, RectTransform root, IRelicResource res)
    {
        if (host == null) return;
        if (!host.TryGetComponent<RelicMemoryHudPresenter>(out var p))
        {
            if (res == null) return;
            p = host.gameObject.AddComponent<RelicMemoryHudPresenter>();
        }
        p.Attach(root, res);
    }

    // ── Lifecycle ────────────────────────────────────────────────

    private void OnEnable()
    {
        GawainMemoryHub.Bound   += OnGawainHub;
        LancelotMemoryHub.Bound += OnLancelotHub;
        Loc.LanguageChanged     += OnLanguageChanged;   // 11-02 Q11

        // 꺼져 있는 동안 허브가 생기면 Bound를 놓친다 — Ch2 · 심연 루프는 @HUD가 꺼진 채 첫 조각이 붙고
        // Bound는 다시 오지 않아 유물 HUD가 끝까지 안 떴다. 켜질 때 지금 허브를 다시 묻는다(10-09 출시 점검).
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (GawainMemoryHub.Of(player) is { } g && g != _gHub) OnGawainHub(g);
        if (LancelotMemoryHub.Of(player) is { } l && l != _lHub) OnLancelotHub(l);
    }

    private void Update()
    {
        // 첫 여명 — 해가 흐르기 시작했다 = 첫 전투(게이지는 시간대가 바뀔 때만 사건을 낸다). 플래그 하나만 본다(할당 없음)
        if (!_tipFightDone && _tipSun != null && _tipSun.CurrentPhase == ZenithGauge.ZPhase.Charging
            && _tipSun.PhaseProgress01 >= FirstFightProgress && InRun)
            OnFirstFight(RelicFirstTips.GawainDawn);

        if (_view == null || !_view.Visible) return;
        if (_gHub != null)
        {
            var g = _gHub.Gauge;
            if (g != null) _view.TickDial(g.CurrentPhase, g.PhaseProgress01, g.IsHoldingDawn);
        }
        else if (_lHub != null)
        {
            // 멈춤 — 피 냄새(MadnessStack.HoldDecayUntil까지 감쇠 멈춤 · 광란 중엔 감쇠가 없어 뜻이 없다) · 끝의 문턱(HoldingThreshold)
            var m = _lHub.Madness;
            float holdLeft = m != null && !m.IsFrenzy ? m.HoldDecayUntil - Time.time : 0f;
            _view.TickLadder(_lHub.HoldingThreshold, holdLeft, _res != null ? _res.Fill : 0f);
        }
    }

    private void OnDisable()
    {
        GawainMemoryHub.Bound   -= OnGawainHub;
        LancelotMemoryHub.Bound -= OnLancelotHub;
        Loc.LanguageChanged     -= OnLanguageChanged;
    }

    /// <summary>11-02 Q11 — 띠 이름 · 계단 값은 SetText(형식, 값)라 안전망이 다시 못 쓴다. 언어가 바뀌면 지금 값으로 다시 쓴다.</summary>
    private void OnLanguageChanged()
    {
        if (_view == null) return;
        if (_lHub != null) for (int k = 1; k <= 4; k++) _view.SetRungValue(k - 1, _lHub.Rung(k), brand: k == 2);
        RefreshAll();
    }

    private void OnDestroy() => Detach();

    // ── 묶기 ────────────────────────────────────────────────────

    private void Attach(RectTransform root, IRelicResource res)
    {
        Detach();
        _res  = res;
        _root = root;
        if (_view == null) _view = gameObject.AddComponent<RelicMemoryHudView>();
        if (res == null || root == null) { _view.Hide(); return; }
        BindTips(res);

        _loadout = AppBootstrapper.Instance?.Loadout;
        if (_loadout != null) _loadout.RelicPartsChanged += RefreshAll;

        // 이미 조각이 있는 런(이어하기 · 실측)이면 허브가 먼저 있다
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (res is ZenithGauge && GawainMemoryHub.Of(player) is { } g) OnGawainHub(g);
        else if (res is MadnessStack && LancelotMemoryHub.Of(player) is { } l) OnLancelotHub(l);
        else _view.Hide();
    }

    private void Detach()
    {
        if (_loadout != null) _loadout.RelicPartsChanged -= RefreshAll;
        _loadout = null;
        UnhookHubs();
        UnbindTips();
    }

    /// <summary>처음 겪는 순간 한 줄 — 유물 리소스 사건을 듣는다(허브 · HUD 표시와 상관없이 · 10-22 E1 D9).</summary>
    private void BindTips(IRelicResource res)
    {
        _tipFightDone = _tipNoonDone = _tipDuskDone = _tipFrenzyDone = _tipFrenzyEndDone = false;
        _tipSun     = res as ZenithGauge;
        _tipMadness = res as MadnessStack;
        if (_tipSun != null) _tipSun.PhaseChanged += OnSunPhase;
        if (_tipMadness != null)
        {
            _tipMadness.OnChanged       += OnMadnessChanged;
            _tipMadness.OnFrenzyChanged += OnFrenzyChanged;
        }
    }

    private void UnbindTips()
    {
        if (_tipSun != null) _tipSun.PhaseChanged -= OnSunPhase;
        if (_tipMadness != null)
        {
            _tipMadness.OnChanged       -= OnMadnessChanged;
            _tipMadness.OnFrenzyChanged -= OnFrenzyChanged;
        }
        _tipSun = null;
        _tipMadness = null;
    }

    private void UnhookHubs()
    {
        if (_gHub != null)
        {
            _gHub.DawnStarted -= OnDawn;
            _gHub.NoonStarted -= OnNoon;
            _gHub.DuskStarted -= OnDusk;
            _gHub.CrossLightChanged -= OnCrossLight;
            _gHub = null;
        }
        if (_lHub != null)
        {
            _lHub.RungCrossed -= OnRung;
            _lHub.CrossLightChanged -= OnCrossLight;
            _lHub = null;
        }
        if (_grudge != null) _grudge.Changed -= RefreshGrudge;
        _grudge = null;
    }

    private void OnGawainHub(GawainMemoryHub hub)
    {
        if (hub == null || _res is not ZenithGauge || _root == null || _view == null) return;
        UnhookHubs();
        _gHub = hub;
        hub.DawnStarted += OnDawn;
        hub.NoonStarted += OnNoon;
        hub.DuskStarted += OnDusk;
        hub.CrossLightChanged += OnCrossLight;
        _view.BuildDial(_root, hub.Gauge);
        RefreshAll();   // 처음 안내는 첫 조각을 고른 뒤 RefreshAll이 띄운다(10-30 H-05)
    }

    private void OnLancelotHub(LancelotMemoryHub hub)
    {
        if (hub == null || _res is not MadnessStack || _root == null || _view == null) return;
        UnhookHubs();
        _lHub   = hub;
        _grudge = hub.Grudge;
        hub.RungCrossed += OnRung;
        hub.CrossLightChanged += OnCrossLight;
        if (_grudge != null) _grudge.Changed += RefreshGrudge;
        _view.BuildLadder(_root);
        // 계단 표식 위 문턱 값(10-18) — 광기 20(계단 2)부터 맞힌 적에 낙인(허브 기본 규칙 AtRung(2)) → 「20 낙인」(10-30 H-10)
        for (int k = 1; k <= 4; k++) _view.SetRungValue(k - 1, hub.Rung(k), brand: k == 2);
        RefreshAll();   // 처음 안내는 첫 조각을 고른 뒤 RefreshAll이 띄운다(10-30 H-12)
    }

    // ── 갱신(사건) ───────────────────────────────────────────────

    /// <summary>조각 · 메아리 · 등급이 바뀌었다. 단계는 허브 필드가 아니라 로드아웃에서 직접 센다(허브보다 먼저 불릴 수 있다).</summary>
    private void RefreshAll()
    {
        if (_view == null || _loadout == null) return;
        if (_gHub != null)
        {
            int dawn = RelicResonance.GawainCount(_loadout, RelicPartAnchor.Dawn);
            int noon = RelicResonance.GawainCount(_loadout, RelicPartAnchor.Noon);
            int dusk = RelicResonance.GawainCount(_loadout, RelicPartAnchor.Dusk);
            _view.SetArcs(dawn, noon, dusk);
            bool possible = CrossLightPossible("gawain");
            _view.SetCrossLight(possible, _gHub.HasCrossLight, animate: false);
            if (dawn + noon + dusk > 0) TryHintOnce(DialHintKey, DialHintText, DialHintSec);   // 첫 조각을 고른 뒤(10-30 H-05)
            if (possible) TryCrossHint(GawainMemoryHub.CrossLightWindow);
        }
        else if (_lHub != null)
        {
            var rungs = RelicPartAnchor.LancelotRungs;
            for (int i = 0; i < _rungs.Length && i < rungs.Length; i++)
                _rungs[i] = RelicResonance.LancelotRungFilled(_loadout, rungs[i]);
            int count = RelicResonance.LancelotCount(_loadout);
            _view.SetRungs(_rungs, count);   // 10-22 E1 D7 — 광기 칸 조각 수
            RefreshGrudge();
            bool possible = CrossLightPossible("lancelot");
            _view.SetCrossLight(possible, _lHub.HasCrossLight, animate: false);
            if (count > 0) TryHintOnce(LadderHintKey, LadderHintText, DialHintSec, LadderLegendText, LadderLegendSec);   // 첫 조각 뒤 · 이어서 범례(10-30 H-12 · H-10)
            if (possible) TryCrossHint(LancelotMemoryHub.CrossLightWindow);
        }
    }

    /// <summary>계정에 한 번 처음 안내 한 줄(+ 이어지는 한 줄). 이미 띄운 키면 아무것도 안 한다.</summary>
    private void TryHintOnce(string key, string text, float seconds, string next = null, float nextSeconds = 0f)
    {
        if (PlayerPrefs.GetInt(key, 0) != 0) return;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();
        _view.ShowHint(text, seconds, next, nextSeconds);
    }

    /// <summary>빈 별(엇갈린 빛 대기)이 처음 뜰 때 한 번 — 무엇을 하면 켜지는지(10-30 H-07). 반응 조각 2개째를 고른 직후라 다른 처음 안내와 겹치지 않는다.</summary>
    private void TryCrossHint(float window)
    {
        if (PlayerPrefs.GetInt(CrossHintKey, 0) != 0) return;
        TryHintOnce(CrossHintKey, Loc.F("빈 별 = 엇갈린 빛 — 서로 다른 반응 2개를 {0:0}초 안에 일으키면 켜진다", window), CrossHintSec);
    }

    private void RefreshGrudge()
    {
        if (_view != null && _grudge != null) _view.SetGrudge(_grudge.Value, _grudge.Cap, _grudge.IsFull);
    }

    /// <summary>엇갈린 빛이 켜지거나(true) 써서 꺼졌다(false) — 별이 밝아지거나 빈 별로 돌아간다.</summary>
    private void OnCrossLight(bool on)
    {
        if (_view == null) return;
        if (_gHub != null) _view.SetCrossLight(CrossLightPossible("gawain"), on, animate: true);
        else if (_lHub != null) _view.SetCrossLight(CrossLightPossible("lancelot"), on, animate: true);
        // 켜지는 순간 무엇이 달라지는지 한 줄(10-30 H-07 — 별 옆 대신 처음 안내 자리를 빌린다 · 새 글자 칸 없이)
        if (on) _view.ShowHint(_gHub != null ? GawainCrossOnText : LancelotCrossOnText, CrossOnSec);
    }

    /// <summary>엇갈린 빛이 가능한가 = 그 유물의 반응 조각 2개 이상(허브의 판정과 같은 셈). 아니면 대기 별을 띄우지 않는다.</summary>
    private bool CrossLightPossible(string relicId)
    {
        var data = Managers.RelicParts;
        if (_loadout == null || data == null) return false;
        var ids = _loadout.RelicPartIds;
        int n = 0;
        for (int i = 0; i < ids.Count; i++)
        {
            var e = data.GetById(ids[i]);
            if (e != null && e.relic_id == relicId && e.IsReaction && ++n >= 2) return true;
        }
        return false;
    }

    /// <summary>첫 전투 — 그 유물의 첫 줄(여명 · 광기) 뒤에, 해방기면 「이제 조각 2개면 진화」(계정에 한 번 · D2).</summary>
    private void OnFirstFight(string tip)
    {
        _tipFightDone = true;
        RelicFirstTips.TryShowOnce(tip);
        if (RelicEvolution.Unlocked) RelicFirstTips.TryShowOnce(RelicFirstTips.EvolutionOpen);
    }

    private void OnDawn()             => _view?.FlashArc(0);
    private void OnNoon(bool shortNoon) => _view?.FlashArc(1);
    private void OnDusk()             => _view?.FlashArc(2);
    private void OnRung(int k)        => _view?.FlashRung(k - 1);

    private void OnSunPhase(ZenithGauge.ZPhase from, ZenithGauge.ZPhase to)
    {
        if (!InRun) return;
        if (to == ZenithGauge.ZPhase.Noon && !_tipNoonDone)
        {
            _tipNoonDone = true;
            RelicFirstTips.TryShowOnce(RelicFirstTips.GawainNoon);
        }
        else if (to == ZenithGauge.ZPhase.Cooldown && !_tipDuskDone)
        {
            _tipDuskDone = true;
            RelicFirstTips.TryShowOnce(RelicFirstTips.GawainDusk);
        }
    }

    private void OnMadnessChanged()
    {
        if (_tipFightDone || _tipMadness == null || _tipMadness.Stacks <= 0 || !InRun) return;
        OnFirstFight(RelicFirstTips.LancelotMadness);
    }

    private void OnFrenzyChanged(bool on)
    {
        if (!InRun) return;
        if (on && !_tipFrenzyDone)
        {
            _tipFrenzyDone = true;
            RelicFirstTips.TryShowOnce(RelicFirstTips.LancelotFrenzy);
        }
        else if (!on && !_tipFrenzyEndDone)
        {
            _tipFrenzyEndDone = true;
            RelicFirstTips.TryShowOnce(RelicFirstTips.LancelotFrenzyEnd);
        }
    }
}
