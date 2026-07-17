using UnityEngine;

/// <summary>
/// 가이드라인 비주얼(플레이스홀더) 정적 파사드 — 룬/서약/유물 효과를 단순 도형+색+라벨로 인게임 표시.
///
/// • 전역 토글 <see cref="Enabled"/>(에디터·개발빌드만 기본 ON, 출시 빌드는 OFF).
///   OFF면 모든 호출이 즉시 무동작(분기 1개) — 회귀 0.
/// • 효과 발생 지점에서 이 파사드의 메서드를 "통지"로 1줄 호출한다(효과 로직은 변경하지 않음).
/// • 카테고리(즉발/상태/장판/원뿔/체인/토스트) → 색/도형/수명 스펙은 여기 한 곳에 모았다.
///   나중에 진짜 VFX 프리팹을 같은 메서드 본문(GuidelineVisualRunner.Spawn*)에 끼우면 그대로 교체된다.
///
/// 색 팔레트: 6속성(불/얼음/전기/풀/빛/어둠) 고정색 + 서약/유물/스킬/리소스 구분색.
/// </summary>
public static class GuidelineVisual
{
    // ── 토글 ────────────────────────────────────────────
    // 개발용 플레이스홀더라 출시 빌드에는 나가면 안 된다. Enabled/Toggle 호출처가 0이라
    // 기본값이 곧 유일한 스위치 — 에디터·개발빌드만 ON으로 둔다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static bool s_enabled = true;
#else
    private static bool s_enabled = false;
#endif

    /// <summary>전역 디버그 표시 토글. OFF로 바꾸면 기존 표시도 즉시 정리된다.</summary>
    public static bool Enabled
    {
        get => s_enabled;
        set
        {
            if (s_enabled == value) return;
            s_enabled = value;
            if (!value) GuidelineVisualRunner.InstanceIfExists?.ClearAll();
        }
    }

    public static void Toggle() => Enabled = !s_enabled;

    public enum ToastKind { Relic, Covenant, Skill, Crit, Resource }
    public enum BadgeTint { Relic, Fire, Ice, Electric, Grass, Light, Dark, Resource, Generic }

    // ── 색 팔레트 ───────────────────────────────────────
    private static readonly Color Fire     = new(1.00f, 0.45f, 0.12f, 0.85f);
    private static readonly Color Ice      = new(0.45f, 0.80f, 1.00f, 0.90f);
    private static readonly Color IceDeep  = new(0.30f, 0.55f, 1.00f, 0.95f);
    private static readonly Color Electric = new(1.00f, 0.92f, 0.20f, 0.90f);
    private static readonly Color Grass    = new(0.45f, 0.90f, 0.35f, 0.85f);
    private static readonly Color Light    = new(1.00f, 0.96f, 0.60f, 0.80f);
    private static readonly Color Dark     = new(0.60f, 0.30f, 0.90f, 0.90f);

    private static readonly Color Synergy  = new(1.00f, 0.70f, 0.25f, 0.85f);
    private static readonly Color CritCol  = new(1.00f, 0.30f, 0.30f, 0.90f);
    private static readonly Color RelicCol = new(1.00f, 0.72f, 0.20f, 1.00f);
    private static readonly Color Coven    = new(0.25f, 0.85f, 0.85f, 1.00f);
    private static readonly Color SkillCol = new(0.90f, 0.90f, 1.00f, 1.00f);
    private static readonly Color ResCol   = new(0.95f, 0.35f, 0.95f, 1.00f);
    private static readonly Color Neutral  = new(0.75f, 0.75f, 0.75f, 0.30f);

    // ════════════════════════════════════════════════════
    // 룬 — 전투 통지
    // ════════════════════════════════════════════════════

    /// <summary>시너지 즉발/지속(DoT) 피해 — 대상 위치에 짧은 색 구체 플래시.</summary>
    public static void SynergyDamage(Vector3 pos, bool isCrit)
    {
        if (!s_enabled) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;
        r.SpawnFlash(pos, isCrit ? CritCol : Synergy, isCrit ? 0.85f : 0.55f, 0.30f, squashed: false);
    }

    /// <summary>치명타(룬 활성 시) — 대상 위에 짧은 "CRIT" 토스트.</summary>
    public static void Crit(Vector3 pos)
    {
        if (!s_enabled) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;
        r.SpawnToast(pos + Vector3.up * 1.6f, "CRIT", CritCol, 0.6f);
    }

    /// <summary>적 상태이상 마커 — id에 따라 색/라벨 자동(점화/빙결/서리/독/기절 등). 만료 시 자동 제거.</summary>
    /// <summary>
    /// ⚠️ 상태이상 마커는 <b>기본 OFF</b>다.
    ///
    /// 원래 개발용 플레이스홀더였는데 켜진 채로 게임에 노출돼 있었다. 상태가 여러 개 걸리면
    /// 전부 머리 위 한 점(2.2m)에 포개져 읽히지도 않았다. 이제 정식 UI(MonsterHPBar 디버프 아이콘 행)가
    /// 그 역할을 하므로 마커는 끈다 — 켜면 둘이 겹쳐 화면만 지저분해진다.
    /// 디버깅이 필요하면 StatusMarkersEnabled 를 켠다(콘·범위 등 다른 가이드라인은 영향 없음).
    /// </summary>
    public static bool StatusMarkersEnabled { get; set; } = false;

    public static void StatusApplied(Transform target, string statusId, float duration)
    {
        if (!StatusMarkersEnabled) return;
        if (!s_enabled || target == null || string.IsNullOrEmpty(statusId)) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;

        Color c; string label;
        switch (statusId)
        {
            case "ignite": case "burn":   c = Fire;     label = "점화"; break;
            case "frost":                 c = Ice;      label = "서리"; break;
            case "freeze":                c = IceDeep;  label = "빙결"; break;
            case "shatter":               c = IceDeep;  label = "분쇄"; break;
            case "poison": case "item_poison": c = Grass; label = "독";   break;
            case "poison_atk":            c = Grass;    label = "약화"; break;
            case "vulnerable":            c = Grass;    label = "취약"; break;
            case "item_mark":             c = Dark;     label = "표식"; break;
            case "shock": case "static":  c = Electric; label = "감전"; break;
            case "stun":                  c = Electric; label = "기절"; break;
            case "brand":                 c = Dark;     label = "낙인"; break;
            default:                      c = Neutral; c.a = 0.9f; label = statusId; break;
        }
        r.SpawnMarker(target, statusId, label, c, duration);
    }

    /// <summary>장판/필드 — 대상 추적형 반투명 디스크. 타입명으로 속성색 추정. 반환=해제 핸들.</summary>
    public static int GroundField(Transform field, float radius, string typeName)
    {
        if (!s_enabled || field == null) return 0;
        var r = GuidelineVisualRunner.Instance; if (r == null) return 0;
        return r.SpawnField(field, radius, FieldColor(typeName));
    }

    public static void ReleaseGroundField(int handle)
    {
        if (handle == 0) return;
        GuidelineVisualRunner.Instance?.ReleaseField(handle);
    }

    /// <summary>원뿔/부채꼴 광역 질의 — 바닥에 부채꼴 윤곽 순간 표시.</summary>
    public static void Cone(Vector3 origin, Vector3 forward, float range, float halfAngleDeg)
    {
        if (!s_enabled) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;
        r.SpawnCone(origin, forward, range, halfAngleDeg, Light, 0.4f);
    }

    /// <summary>체인/연결 — 두 점 사이 색 라인.</summary>
    public static void Chain(Vector3 from, Vector3 to)
    {
        if (!s_enabled) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;
        r.SpawnLine(from, to, Electric, 0.3f);
    }

    /// <summary>광역 폭발(서약 AoE/작열 등) — 바닥 디스크 플래시.</summary>
    public static void AoeBurst(Vector3 center, float radius, ToastKind tint)
    {
        if (!s_enabled) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;
        Color c = TintColor(tint); c.a = 0.5f;
        r.SpawnFlash(center, c, radius * 2f, 0.4f, squashed: true);
    }

    // ════════════════════════════════════════════════════
    // 서약/유물 — 발동 통지
    // ════════════════════════════════════════════════════

    /// <summary>서약/유물/스킬/리소스 발동 토스트 — 위치에 이름 라벨 + 색.</summary>
    public static void Toast(Vector3 pos, string label, ToastKind kind)
    {
        if (!s_enabled || string.IsNullOrEmpty(label)) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;
        r.SpawnToast(pos, label, TintColor(kind), 1.1f);
    }

    /// <summary>플레이어 머리 위 지속 배지 — 상태키별 1개(정오/각인/광기 N/전기 N 등). 같은 키 재호출=갱신.</summary>
    public static void SetBadge(Transform anchor, string key, string text, BadgeTint tint)
    {
        if (!s_enabled || anchor == null || string.IsNullOrEmpty(key)) return;
        var r = GuidelineVisualRunner.Instance; if (r == null) return;
        r.SetBadge(anchor, key, text, BadgeColor(tint));
    }

    public static void ClearBadge(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        GuidelineVisualRunner.InstanceIfExists?.ClearBadge(key);
    }

    // ── 내부 ────────────────────────────────────────────
    private static Color BadgeColor(BadgeTint t) => t switch
    {
        BadgeTint.Fire     => new Color(1.00f, 0.45f, 0.12f, 1f),
        BadgeTint.Ice      => new Color(0.45f, 0.80f, 1.00f, 1f),
        BadgeTint.Electric => new Color(1.00f, 0.92f, 0.20f, 1f),
        BadgeTint.Grass    => new Color(0.45f, 0.90f, 0.35f, 1f),
        BadgeTint.Light    => new Color(1.00f, 0.96f, 0.60f, 1f),
        BadgeTint.Dark     => new Color(0.70f, 0.45f, 1.00f, 1f),
        BadgeTint.Resource => new Color(0.95f, 0.35f, 0.95f, 1f),
        BadgeTint.Relic    => new Color(1.00f, 0.72f, 0.20f, 1f),
        _                  => new Color(0.90f, 0.90f, 0.90f, 1f),
    };

    private static Color TintColor(ToastKind kind) => kind switch
    {
        ToastKind.Relic    => RelicCol,
        ToastKind.Covenant => Coven,
        ToastKind.Skill    => SkillCol,
        ToastKind.Crit     => CritCol,
        ToastKind.Resource => ResCol,
        _                  => SkillCol,
    };

    private static Color FieldColor(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) { var n = Neutral; n.a = 0.3f; return n; }
        if (typeName.Contains("Grass") || typeName.Contains("Mist") || typeName.Contains("Poison"))
            { var c = Grass; c.a = 0.30f; return c; }
        if (typeName.Contains("Light") || typeName.Contains("Sanctuary") || typeName.Contains("Radiance") || typeName.Contains("Field"))
            { var c = Light; c.a = 0.28f; return c; }
        if (typeName.Contains("Fire") || typeName.Contains("Lava") || typeName.Contains("Scorch"))
            { var c = Fire; c.a = 0.30f; return c; }
        var d = Neutral; d.a = 0.30f; return d;
    }
}
