using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 챕터 시작 대기방의 「요정의 샘」 — 한 번 마시면 체력이 크게 차오르고 포션 칸이 채워진다.
/// 사용자(10-01): 「각 챕터를 시작할 때 회복을 챙겨 주는 기믹이 필요」. 챕터 전환은 직전 체력 비율을 그대로 이어받고
/// (GameRunSession.BindPlayer) 회복은 「챕터 시작 특수 오브젝트」가 맡기로 했으나 그 오브젝트가 없었다(RestAtSanctuary 호출처 0).
/// 적에게서 빼앗는 회복이 아니라 쉼이다 — 흡혈 금지 원칙(효과의 결)과 겹치지 않는다.
/// 대기방당 한 번. 이미 가득이면 마시지 않고 알려만 준다.
/// 11-02 사용자 「근처로 가면 바로 지급하고 지급할 때 연출」 — [F] · 확인 창을 걷고 2.4 m 안에 들어서면 바로 마신다.
/// 결과는 한 줄(체력 +N · 포션 a→b)로 — 예전 확인 창(10-16 · 10-18)이 보여 주던 「지금 → 뒤」를 마신 뒤에 알린다.
/// </summary>
public sealed class ChapterSpring : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float HealRatio          = 0.5f;    // 최대 체력의 50%
    private const float NightmareHealRatio = 0.3f;    // 악몽 모드 — 샘물도 흐리다
    private const int   NightmarePotions   = 1;       // 악몽 모드는 포션 한 개만(가득 채우지 않는다)
    private const float TriggerRadius      = 2.4f;
    private const float PromptHeight       = 2.2f;
    private const float LabelHeight        = 2.9f;    // 늘 보이는 이름표 — [F] 안내 위(10-22 A7)
    private const float LabelSize          = 3f;
    private const float LabelMaxPx         = 40f;     // 서약 제단 「서약 조립」과 같은 화면 크기 상한
    private static readonly Color SpringInk = new(0.55f, 0.88f, 1f, 1f);

    // ── Private ─────────────────────────────────────────────
    private Light _light;
    private bool  _used;
    private bool  _wasInside;      // 11-02 들어서는 순간 한 번만
    private float _lightBase = -1f;
    private TextMeshPro _label;
    private Transform   _cam;

    // ── Lifecycle ───────────────────────────────────────────
    private void Start()
    {
        _light = GetComponentInChildren<Light>();
        if (_light != null) _lightBase = _light.intensity;
    }

    /// <summary>11-02 다가가면 바로 마신다 — 2.4 m 안에 들어서는 순간 한 번(나갔다 다시 들어오면 다시 본다).</summary>
    private void Update()
    {
        if (_used) return;
        var p = Managers.Player != null ? Managers.Player.PlayerTransform : null;
        if (p == null) return;
        Vector3 d = p.position - transform.position; d.y = 0f;
        bool inside = d.sqrMagnitude <= TriggerRadius * TriggerRadius;
        if (inside && !_wasInside) Approach();
        _wasInside = inside;
    }

    private void LateUpdate()
    {
        if (_label == null) return;
        if (_cam == null) { var c = Camera.main; if (c == null) return; _cam = c.transform; }
        _label.transform.rotation = _cam.rotation;
    }

    private void OnDestroy() { }

    // ── Public Methods ──────────────────────────────────────
    /// <summary>대기방에 샘을 세운다. 비주얼이 없으면 빛만 있는 샘(기능은 그대로).</summary>
    public static ChapterSpring SpawnAt(Vector3 position, GameObject visualPrefab)
    {
        var go = new GameObject("FairySpring");
        go.transform.position = new Vector3(position.x, ServiceRoomDecorPlacer.GroundYAt(position, position.y), position.z);
        if (visualPrefab != null) Instantiate(visualPrefab, go.transform.position, Quaternion.identity, go.transform);

        var spring = go.AddComponent<ChapterSpring>();   // 11-02 [F] 상호작용 없음 — 다가가면 바로(Update)
        spring.CreateLabel();
        return spring;
    }

    /// <summary>코드로 세운 오브젝트에 [F] 상호작용을 붙인다(트리거 자식 + 프롬프트). 쉼터 모닥불 · 모루도 같이 쓴다.</summary>
    public static ShopNpcInteraction AddInteraction(Transform host, string prompt, float promptHeight, float radius = TriggerRadius)
    {
        var go = new GameObject("Interact");
        go.transform.SetParent(host, false);
        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = radius;
        var it = go.AddComponent<ShopNpcInteraction>();
        it.Configure(prompt, promptHeight);
        return it;
    }

    // ── Event Handlers ──────────────────────────────────────
    /// <summary>11-02 들어섰다 — 가득이면 한 줄만(샘은 남는다), 아니면 바로 마신다.</summary>
    private void Approach()
    {
        var run   = GameRunBootstrapper.Instance?.Run;
        var stats = run?.Player?.RuntimeStats;
        var state = run?.PlayerState;
        if (stats == null || state == null) return;
        if (stats.Hp >= stats.MaxHp && state.PotionCount >= state.PotionCapacity)
        {
            Notice("<color=#9AA0A6>샘이 고요하다 — 이미 가득하다 · 다치거나 포션을 쓴 뒤 다시 오면 마실 수 있다</color>");
            return;
        }
        Drink();
    }

    private void Drink()
    {
        if (_used) return;
        var run   = GameRunBootstrapper.Instance?.Run;
        var stats = run?.Player?.RuntimeStats;
        var state = run?.PlayerState;
        if (stats == null || state == null) return;

        bool hpFull = stats.Hp >= stats.MaxHp;

        bool nightmare = NightmareRules.IsActive;
        int before = stats.Hp;
        if (!hpFull) run.Player.Heal(Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * HealRatioNow(nightmare))));
        int potionsBefore = state.PotionCount;
        if (nightmare) state.AddPotion(NightmarePotions);
        else           state.RefillPotions();

        _used = true;
        if (_label != null)
        {
            _label.text = "요정의 샘\n<size=70%>마셨다</size>";
            _label.color = new Color(SpringInk.r, SpringInk.g, SpringInk.b, 0.45f);
        }
        // 11-02 지급 연출(절제) — 샘 빛이 잠깐 밝아졌다 잦아들고, 샘 고리 하나 · 플레이어 발밑 작은 빛기둥
        FlareThenDimAsync(this.GetCancellationTokenOnDestroy()).Forget();
        RunFx.Play(RunFxSlot.Ring, transform.position + Vector3.up * 0.1f, 1f, SpringInk);
        if (run.Player != null) RunFx.Play(RunFxSlot.Pillar, run.Player.transform.position, 0.05f, SpringInk);
        Managers.Sound?.PlayEvent(SoundEvent.SpringDrink);
        Notice(Loc.F("<color=#8FD9FF>요정의 샘</color> — 체력 +{0} · 포션 {1}→{2}", stats.Hp - before, potionsBefore, state.PotionCount));
    }

    /// <summary>11-02 마신 순간 샘 빛 — 0.25초에 2배로 밝아졌다가 0.9초에 걸쳐 원래의 35%로 잦아든다(마신 샘).</summary>
    private async UniTaskVoid FlareThenDimAsync(System.Threading.CancellationToken ct)
    {
        if (_light == null) return;
        float baseI = _lightBase > 0f ? _lightBase : _light.intensity;
        try
        {
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                _light.intensity = Mathf.Lerp(baseI, baseI * 2f, t / 0.25f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            for (float t = 0f; t < 0.9f; t += Time.deltaTime)
            {
                _light.intensity = Mathf.Lerp(baseI * 2f, baseI * 0.35f, t / 0.9f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        catch (System.OperationCanceledException) { return; }
        if (_light != null) _light.intensity = baseI * 0.35f;   // 마신 샘은 빛이 잦아든다
    }

    /// <summary>멀리서도 보이는 이름표 — [F] 안내는 2.4 m 안에서만 떠 지나치기 쉬웠다(10-22 A7).</summary>
    private void CreateLabel()
    {
        var go = new GameObject("SpringLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * LabelHeight;
        _label = go.AddComponent<TextMeshPro>();
        _label.text = "요정의 샘\n<size=70%>다가가면 체력 · 포션 회복</size>";
        _label.fontSize = LabelSize;
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = SpringInk;
        _label.textWrappingMode = TextWrappingModes.NoWrap;
        _label.sortingOrder = UISortingOrder.WorldLabel;
        TMPOutlineHelper.ApplySoftShadow(_label);
        RelicFloatText.ApplyThin(_label);   // 3D 글자는 얇게
        WorldTextScreenCap.Attach(_label, LabelMaxPx, prompt: false);
    }

    /// <summary>이번 샘의 회복 비율 — 기본(50% · 악몽 30%) + 기억의 제단 「재정비의 샘」(10-22 해방 대비 · +20%p/단계 · 악몽 +10%p/단계).</summary>
    private static float HealRatioNow(bool nightmare)
        => (nightmare ? NightmareHealRatio : HealRatio) + MemoryAltarService.SpringHealBonus(nightmare);

    private static void Notice(string message)
    {
        var hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (hud != null) hud.ShowBuffNotice(message);
    }
}
