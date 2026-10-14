using UnityEngine;

/// <summary>
/// 유물 HUD 확장(유물 성장 v2 §6-1) Presenter — 조각이 「어디서 켜지는지」를 유물 게이지 위에 얹는다.
/// <list type="bullet">
/// <item>Provider: 유물 허브(<see cref="GawainMemoryHub"/> · <see cref="LancelotMemoryHub"/>) · <see cref="PlayerLoadout"/> · <see cref="ZenithGauge"/>.</item>
/// <item>View: <see cref="RelicMemoryHudView"/> — 게이지 루트 아래에 코드로 짓는 위젯(해시계 세 호 / 광기 눈금 넷 · 원한 불씨).</item>
/// </list>
/// 허브는 첫 조각과 함께 생기므로 그 전엔 숨긴다(<c>Bound</c> 사건으로 깨어난다).
/// 갱신은 사건으로(조각 · 원한 · 시간대 · 계단 넘기 · 엇갈린 빛) — 해시계 바늘 · 광기 막대 떨림 · 멈춤(피 냄새 남은 초)만 매 프레임 읽는다.
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

        // 꺼져 있는 동안 허브가 생기면 Bound를 놓친다 — Ch2 · 심연 루프는 @HUD가 꺼진 채 첫 조각이 붙고
        // Bound는 다시 오지 않아 유물 HUD가 끝까지 안 떴다. 켜질 때 지금 허브를 다시 묻는다(10-09 출시 점검).
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (GawainMemoryHub.Of(player) is { } g && g != _gHub) OnGawainHub(g);
        if (LancelotMemoryHub.Of(player) is { } l && l != _lHub) OnLancelotHub(l);
    }

    private void Update()
    {
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
        RefreshAll();
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
            _view.SetCrossLight(CrossLightPossible("gawain"), _gHub.HasCrossLight, animate: false);
        }
        else if (_lHub != null)
        {
            var rungs = RelicPartAnchor.LancelotRungs;
            for (int i = 0; i < _rungs.Length && i < rungs.Length; i++)
                _rungs[i] = RelicResonance.LancelotRungFilled(_loadout, rungs[i]);
            _view.SetRungs(_rungs, RelicResonance.LancelotTier(RelicResonance.LancelotLadder(_loadout)));
            RefreshGrudge();
            _view.SetCrossLight(CrossLightPossible("lancelot"), _lHub.HasCrossLight, animate: false);
        }
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

    private void OnDawn()             => _view?.FlashArc(0);
    private void OnNoon(bool shortNoon) => _view?.FlashArc(1);
    private void OnDusk()             => _view?.FlashArc(2);
    private void OnRung(int k)        => _view?.FlashRung(k - 1);
}
