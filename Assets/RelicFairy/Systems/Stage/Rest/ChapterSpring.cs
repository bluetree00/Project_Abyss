using UnityEngine;

/// <summary>
/// 챕터 시작 대기방의 「요정의 샘」 — 한 번 마시면 체력이 크게 차오르고 포션 칸이 채워진다.
/// 사용자(10-01): 「각 챕터를 시작할 때 회복을 챙겨 주는 기믹이 필요」. 챕터 전환은 직전 체력 비율을 그대로 이어받고
/// (GameRunSession.BindPlayer) 회복은 「챕터 시작 특수 오브젝트」가 맡기로 했으나 그 오브젝트가 없었다(RestAtSanctuary 호출처 0).
/// 적에게서 빼앗는 회복이 아니라 쉼이다 — 흡혈 금지 원칙(효과의 결)과 겹치지 않는다.
/// 대기방당 한 번. 이미 가득이면 마시지 않고 알려만 준다.
/// </summary>
public sealed class ChapterSpring : MonoBehaviour
{
    // ── Constants ───────────────────────────────────────────
    private const float HealRatio          = 0.5f;    // 최대 체력의 50%
    private const float NightmareHealRatio = 0.3f;    // 악몽 모드 — 샘물도 흐리다
    private const int   NightmarePotions   = 1;       // 악몽 모드는 포션 한 개만(가득 채우지 않는다)
    private const float TriggerRadius      = 2.4f;
    private const float PromptHeight       = 2.2f;
    private static readonly Color SpringInk = new(0.55f, 0.88f, 1f, 1f);

    // ── Private ─────────────────────────────────────────────
    private ShopNpcInteraction _interaction;
    private Light _light;
    private bool  _used;

    // ── Lifecycle ───────────────────────────────────────────
    private void Start()
    {
        _light = GetComponentInChildren<Light>();
        if (_interaction != null) _interaction.OnInteract += Drink;
    }

    private void OnDestroy()
    {
        if (_interaction != null) _interaction.OnInteract -= Drink;
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
        if (!hpFull) run.Player.Heal(Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * (nightmare ? NightmareHealRatio : HealRatio))));
        int potionsBefore = state.PotionCount;
        if (nightmare) state.AddPotion(NightmarePotions);
        else           state.RefillPotions();

        _used = true;
        _interaction.SetInteractable(false);
        if (_light != null) _light.intensity *= 0.35f;   // 마신 샘은 빛이 잦아든다
        RunFx.Play(RunFxSlot.Ring, transform.position + Vector3.up * 0.1f, 1f, SpringInk);
        Notice($"<color=#8FD9FF>요정의 샘</color> — 체력 +{stats.Hp - before} · 포션 {potionsBefore}→{state.PotionCount}");
    }

    private static void Notice(string message)
    {
        var hud = FindFirstObjectByType<HudPresenter>(FindObjectsInactive.Include);
        if (hud != null) hud.ShowBuffNotice(message);
    }
}
