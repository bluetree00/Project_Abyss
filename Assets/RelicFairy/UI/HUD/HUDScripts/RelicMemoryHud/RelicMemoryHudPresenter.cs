using UnityEngine;

/// <summary>
/// 유물 HUD 확장(유물 성장 v2 §6-1) Presenter — 조각이 「어디서 켜지는지」를 유물 게이지 위에 얹는다.
/// <list type="bullet">
/// <item>Provider: 유물 허브(<see cref="GawainMemoryHub"/> · <see cref="LancelotMemoryHub"/>) · <see cref="PlayerLoadout"/> · <see cref="ZenithGauge"/>.</item>
/// <item>View: <see cref="RelicMemoryHudView"/> — 게이지 루트 아래에 코드로 짓는 위젯(해시계 세 호 / 광기 눈금 넷 · 원한 불씨).</item>
/// </list>
/// 허브는 첫 조각과 함께 생기므로 그 전엔 숨긴다(<c>Bound</c> 사건으로 깨어난다).
/// 갱신은 사건으로(조각 · 원한 · 시간대 · 계단 넘기) — 해시계 바늘과 광기 막대 떨림만 매 프레임 읽는다.
/// </summary>
[DisallowMultipleComponent]
public sealed class RelicMemoryHudPresenter : MonoBehaviour
{
    private RelicMemoryHudView _view;
    private IRelicResource     _res;
    private RectTransform      _root;
    private PlayerLoadout      _loadout;
    private GawainMemoryHub    _gHub;
    private LancelotMemoryHub  _lHub;
    private GrudgeStore        _grudge;
    private readonly bool[]    _rungs = new bool[4];

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
    }

    private void Update()
    {
        if (_view == null || !_view.Visible) return;
        if (_gHub != null)
        {
            var g = _gHub.Gauge;
            if (g != null) _view.TickDial(g.CurrentPhase, g.PhaseProgress01, g.IsHoldingDawn);
        }
        else if (_lHub != null) _view.TickLadder(_lHub.HoldingThreshold);
    }

    private void OnDisable()
    {
        GawainMemoryHub.Bound   -= OnGawainHub;
        LancelotMemoryHub.Bound -= OnLancelotHub;
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
    }

    private void UnhookHubs()
    {
        if (_gHub != null)
        {
            _gHub.DawnStarted -= OnDawn;
            _gHub.NoonStarted -= OnNoon;
            _gHub.DuskStarted -= OnDusk;
            _gHub = null;
        }
        if (_lHub != null)
        {
            _lHub.RungCrossed -= OnRung;
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
        _view.BuildDial(_root, hub.Gauge);
        RefreshAll();
    }

    private void OnLancelotHub(LancelotMemoryHub hub)
    {
        if (hub == null || _res is not MadnessStack || _root == null || _view == null) return;
        UnhookHubs();
        _lHub   = hub;
        _grudge = hub.Grudge;
        hub.RungCrossed += OnRung;
        if (_grudge != null) _grudge.Changed += RefreshGrudge;
        _view.BuildLadder(_root);
        RefreshAll();
    }

    // ── 갱신(사건) ───────────────────────────────────────────────

    /// <summary>조각 · 메아리 · 등급이 바뀌었다. 단계는 허브 필드가 아니라 로드아웃에서 직접 센다(허브보다 먼저 불릴 수 있다).</summary>
    private void RefreshAll()
    {
        if (_view == null || _loadout == null) return;
        if (_gHub != null)
        {
            _view.SetArcs(RelicResonance.GawainCount(_loadout, RelicPartAnchor.Dawn),
                          RelicResonance.GawainCount(_loadout, RelicPartAnchor.Noon),
                          RelicResonance.GawainCount(_loadout, RelicPartAnchor.Dusk));
        }
        else if (_lHub != null)
        {
            var rungs = RelicPartAnchor.LancelotRungs;
            for (int i = 0; i < _rungs.Length && i < rungs.Length; i++)
                _rungs[i] = RelicResonance.LancelotRungFilled(_loadout, rungs[i]);
            _view.SetRungs(_rungs, RelicResonance.LancelotTier(RelicResonance.LancelotLadder(_loadout)));
            RefreshGrudge();
        }
    }

    private void RefreshGrudge()
    {
        if (_view != null && _grudge != null) _view.SetGrudge(_grudge.Value, _grudge.Cap);
    }

    private void OnDawn()             => _view?.FlashArc(0);
    private void OnNoon(bool shortNoon) => _view?.FlashArc(1);
    private void OnDusk()             => _view?.FlashArc(2);
    private void OnRung(int k)        => _view?.FlashRung(k - 1);
}
