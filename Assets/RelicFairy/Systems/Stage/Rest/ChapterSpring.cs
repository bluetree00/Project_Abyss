using TMPro;
using UnityEngine;

/// <summary>
/// 챕터 시작 대기방의 「요정의 샘」 — 한 번 마시면 체력이 크게 차오르고 포션 칸이 채워진다.
/// 사용자(10-01): 「각 챕터를 시작할 때 회복을 챙겨 주는 기믹이 필요」. 챕터 전환은 직전 체력 비율을 그대로 이어받고
/// (GameRunSession.BindPlayer) 회복은 「챕터 시작 특수 오브젝트」가 맡기로 했으나 그 오브젝트가 없었다(RestAtSanctuary 호출처 0).
/// 적에게서 빼앗는 회복이 아니라 쉼이다 — 흡혈 금지 원칙(효과의 결)과 겹치지 않는다.
/// 대기방당 한 번. 이미 가득이면 마시지 않고 알려만 준다.
/// [F]는 바로 마시지 않고 작은 확인 창(UI_ConfirmDialog)을 띄워 「지금 체력 a/b · 포션 c/d → 샘 뒤 a'/b · d'/d」 한 줄을 보여 준다 — [회복]에서 마신다
/// (10-16 사용자 「F키를 눌렀을때 상호작용 팝업과 내용을 보여줘야해」 · 10-18 「현재 체력 포션 상황을 모르니」).
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
    private ShopNpcInteraction _interaction;
    private Light _light;
    private bool  _used;
    private TextMeshPro _label;
    private Transform   _cam;

    // ── Lifecycle ───────────────────────────────────────────
    private void Start()
    {
        _light = GetComponentInChildren<Light>();
        if (_interaction != null) _interaction.OnInteract += OpenPrompt;
    }

    private void LateUpdate()
    {
        if (_label == null) return;
        if (_cam == null) { var c = Camera.main; if (c == null) return; _cam = c.transform; }
        _label.transform.rotation = _cam.rotation;
    }

    private void OnDestroy()
    {
        if (_interaction != null) _interaction.OnInteract -= OpenPrompt;
    }

    // ── Public Methods ──────────────────────────────────────
    /// <summary>대기방에 샘을 세운다. 비주얼이 없으면 빛만 있는 샘(기능은 그대로).</summary>
    public static ChapterSpring SpawnAt(Vector3 position, GameObject visualPrefab)
    {
        var go = new GameObject("FairySpring");
        go.transform.position = new Vector3(position.x, ServiceRoomDecorPlacer.GroundYAt(position, position.y), position.z);
        if (visualPrefab != null) Instantiate(visualPrefab, go.transform.position, Quaternion.identity, go.transform);

        var spring = go.AddComponent<ChapterSpring>();
        spring._interaction = AddInteraction(go.transform, "<color=#FFD700>[F]</color> 요정의 샘 — 체력 · 포션", PromptHeight);
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
    /// <summary>[F] — 마시면 무엇이 바뀌는지 먼저 보여 준다. 이미 가득이면 창 없이 알려만 준다.</summary>
    private void OpenPrompt()
    {
        if (_used) return;
        var run   = GameRunBootstrapper.Instance?.Run;
        var stats = run?.Player?.RuntimeStats;
        var state = run?.PlayerState;
        if (stats == null || state == null) return;

        bool hpFull     = stats.Hp >= stats.MaxHp;
        bool potionFull = state.PotionCount >= state.PotionCapacity;
        if (hpFull && potionFull)
        {
            // 가득이어도 [F]에 창은 뜬다 — 알림 한 줄만으로는 눌렀는지조차 몰랐다(10-16 실측). 마시지 않으니 샘은 그대로 남는다.
            string full =
                $"지금 체력 {stats.Hp}/{stats.MaxHp} · 포션 {state.PotionCount}/{state.PotionCapacity}\n" +
                "<size=85%><color=#9AA0A6>샘이 고요하다 — 이미 가득하다. 다치거나 포션을 쓴 뒤 다시 오면 마실 수 있다</color></size>";
            UI_ConfirmDialog.Show("요정의 샘", UIKeywordInk.Values(full), "닫기", null, null);
            return;
        }

        bool nightmare = NightmareRules.IsActive;
        float ratio    = HealRatioNow(nightmare);
        int heal       = Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * ratio));
        run.EffectManager?.ModifyHeal(ref heal);   // 마실 때(PlayerController.Heal)와 같은 회복 보정 — 창의 숫자가 실제 결과와 맞게
        int hpAfter    = hpFull ? stats.Hp : Mathf.Min(stats.MaxHp, stats.Hp + heal);
        int potAfter   = nightmare ? Mathf.Min(state.PotionCapacity, state.PotionCount + NightmarePotions) : state.PotionCapacity;
        // 지금 → 샘 뒤를 한 줄로(10-18 사용자 「현재 체력 포션 상황을 모르니 샘을 누를 때도 기분이 안 좋아」)
        string body =
            $"지금 체력 {stats.Hp}/{stats.MaxHp} · 포션 {state.PotionCount}/{state.PotionCapacity}   →   " +
            $"샘 뒤 {hpAfter}/{stats.MaxHp} · {potAfter}/{state.PotionCapacity}\n" +
            "<size=85%><color=#9AA0A6>" +
            (nightmare ? $"악몽 — 체력 회복 {Mathf.RoundToInt(ratio * 100f)}% · 포션 +{NightmarePotions}"
                       : $"체력 회복 {Mathf.RoundToInt(ratio * 100f)}% · 포션 가득") +
            " · 대기방마다 한 번</color></size>";
        UI_ConfirmDialog.Show("요정의 샘", UIKeywordInk.Values(body), "회복", "닫기", Drink);   // 10-18 「마신다」 → 하는 일(문구 통일)
    }

    private void Drink()
    {
        if (_used) return;
        var run   = GameRunBootstrapper.Instance?.Run;
        var stats = run?.Player?.RuntimeStats;
        var state = run?.PlayerState;
        if (stats == null || state == null) return;

        bool hpFull     = stats.Hp >= stats.MaxHp;
        bool potionFull = state.PotionCount >= state.PotionCapacity;
        if (hpFull && potionFull)
        {
            Notice("<color=#9AA0A6>샘이 고요하다 — 이미 가득하다</color>");
            return;
        }

        bool nightmare = NightmareRules.IsActive;
        int before = stats.Hp;
        if (!hpFull) run.Player.Heal(Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * HealRatioNow(nightmare))));
        int potionsBefore = state.PotionCount;
        if (nightmare) state.AddPotion(NightmarePotions);
        else           state.RefillPotions();

        _used = true;
        _interaction.SetInteractable(false);
        if (_light != null) _light.intensity *= 0.35f;   // 마신 샘은 빛이 잦아든다
        if (_label != null)
        {
            _label.text = "요정의 샘\n<size=70%>마셨다</size>";
            _label.color = new Color(SpringInk.r, SpringInk.g, SpringInk.b, 0.45f);
        }
        RunFx.Play(RunFxSlot.Ring, transform.position + Vector3.up * 0.1f, 1f, SpringInk);
        Managers.Sound?.PlayEvent(SoundEvent.SpringDrink);
        Notice($"<color=#8FD9FF>요정의 샘</color> — 체력 +{stats.Hp - before} · 포션 {potionsBefore}→{state.PotionCount}");
    }

    /// <summary>멀리서도 보이는 이름표 — [F] 안내는 2.4 m 안에서만 떠 지나치기 쉬웠다(10-22 A7).</summary>
    private void CreateLabel()
    {
        var go = new GameObject("SpringLabel");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * LabelHeight;
        _label = go.AddComponent<TextMeshPro>();
        _label.text = "요정의 샘\n<size=70%>체력 · 포션 회복</size>";
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
