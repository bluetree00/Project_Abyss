using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// <see cref="RuneArtLibrarySO"/> 접근 창구. Addressable "RuneArtLibrary"를 1회 로드해 캐싱한다.
/// 코드 생성 UI(선택 팝업 등)가 동기적으로 아트를 읽을 수 있도록, 앱 부트에서 <see cref="PreloadAsync"/>로 미리 로드한다.
/// 미로드/실패 시 GetArt 등은 null을 반환 → 호출부는 색상 폴백으로 동작.
/// </summary>
public static class RuneArt
{
    private const string Address = "RuneArtLibrary";

    private static RuneArtLibrarySO _lib;
    private static bool _loading;

    public static bool IsLoaded => _lib != null;

    /// <summary>라이브러리 직접 접근 — 판 외곽 액자처럼 조각을 여러 개 꺼내 쓰는 곳에서 사용. 미로드면 null.</summary>
    public static RuneArtLibrarySO Library => _lib;

    // 도메인리로드 비활성(fast play mode)에서도 정적 상태가 새 세션으로 새로 시작하도록 초기화
    // (라이브러리는 불변이라 성능 폴백일 뿐이지만, UISkin/EffectIconRegistry 관례와 맞춘다).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _lib = null;
        _loading = false;
        _iconsByName = null;
    }

    /// <summary>앱 부트에서 1회 호출. 이미 로드됐으면 즉시 반환.</summary>
    public static async UniTask PreloadAsync()
    {
        if (_lib != null || _loading) return;
        _loading = true;
        try
        {
            _lib = await Managers.AddressableManager.TryLoadAssetAsync<RuneArtLibrarySO>(Address);
            if (_lib == null)
                Debug.LogWarning("[RuneArt] RuneArtLibrary 로드 실패 — 룬 아트 없이 색상 폴백으로 동작");
        }
        finally { _loading = false; }
    }

    /// <summary>등급 룬 아트(미로드 시 null → 색상 폴백).</summary>
    /// <summary>기능(effect_type)에 대응하는 문양. 없으면 null.</summary>
    public static Sprite GetIconByEffect(string effectType)
        => _lib != null ? _lib.GetIconByEffect(effectType) : null;

    public static Sprite GetArt(ItemRarity rarity) => _lib != null ? _lib.GetArt(rarity) : null;

    /// <summary>등급 룬 테두리(미로드 시 null).</summary>
    public static Sprite GetBorder(ItemRarity rarity) => _lib != null ? _lib.GetBorder(rarity) : null;

    /// <summary>
    /// 속성 룬 각인석(ElementDef.Order — 룬1~5). 미로드/미할당 시 null → 색 틴트 폴백.
    /// 빛(index 4)은 전용 각인석이 없어 의도적으로 비어 있다(색 틴트로 처리).
    ///
    /// ※ UISkin.RuneSelect.elementPiece(룬조각)는 단색 둥근 사각형이라 여기 폴백으로 쓰면 안 된다 —
    ///   룬이 통째로 색 블록으로 보인다.
    /// </summary>
    public static Sprite GetArtByElement(string elementId)
        => _lib != null ? _lib.GetArtByElement(ElementIndex(elementId)) : null;

    /// <summary>속성 룬 테두리.</summary>
    public static Sprite GetBorderByElement(string elementId) => _lib != null ? _lib.GetBorderByElement(ElementIndex(elementId)) : null;

    /// <summary>
    /// 이 룬의 <b>대표 문양</b> — 컨셉 표(<see cref="ConceptIconName"/>) → 기능 → 등급 → 속성 순으로 떨어진다.
    ///
    /// <para>같은 룬이 화면마다 다른 얼굴이면 안 되므로 순서를 여기 한 곳에만 둔다.
    /// 실제로 획득 팝업만 기능 문양을 쓰고 대기열은 등급 아트에서 시작해,
    /// 「전투력의 룬」이 팝업에선 칼날인데 배치 화면에선 돌 각인석으로 보였다.</para>
    /// </summary>
    public static Sprite ResolveRuneIcon(RuntimeItemData data)
    {
        if (data == null) return null;

        // 존핵(정제소)은 정제소에서 고른 그 속성 보석 한 장 — 예전엔 등급마다 다른 얼굴(등급 그림 · 영웅 속성 문양)을 받아
        // 영웅 속성 룬 3종과 같은 그림이 됐다(09-27 「이름은 다른데 그림이 같다」).
        if (RefineryService.IsZoneCore(data))
        {
            var gem = UISkin.Refinery != null ? UISkin.Refinery.Glyph(ElementIndex(data.element)) : null;
            if (gem != null) return gem;
        }

        var art = IconByName(ConceptIconName(data));
        if (art == null) art = GetIconByEffect(EffectTypeOf(data));
        if (art == null) art = GetArt(data.rarity);
        if (art == null) art = GetArtByElement(data.element);
        return art;
    }

    // ── 컨셉별 문양 배정 (2026-09-19) ─────────────────────────────────
    // 수치만 다른 룬마다 그림을 따로 주지 않는다 — <b>컨셉이 같으면 같은 그림</b>이고, 등급은 테두리·빛이 가른다.
    //  · Common·Rare : 기본 도형. 효과 컨셉 7종 × (상시 / 조건부)
    //  · Epic        : 속성마다 문양 하나 — 같은 속성 영웅 룬 3종은 그림을 함께 쓰고 효과 글로 갈린다
    //  · Legendary   : 룬마다 고유한 모양 18장
    // 값은 스프라이트 이름(= 파일 이름, UI/PuzzleGrid/Sprites/Runes/Effects). 라이브러리에 없는 이름은 아래 폴백으로 떨어진다.
    // 예전엔 아이템 id 순으로 54장을 돌려 나눠서, 같은 컨셉 룬이 서로 무관한 얼굴을 받았다.

    /// <summary>효과 컨셉 → (상시 도형, 조건부 도형).</summary>
    private static readonly Dictionary<string, (string always, string conditional)> ConceptShapes = new()
    {
        ["공격"] = ("뾰족 룬",     "레이어 14"),
        ["치명"] = ("뾰족마름모룬", "삼각룬"),
        ["방어"] = ("오각룬",       "마름모룬"),
        ["체력"] = ("동글룬",       "핑크눈물 룬"),
        ["속도"] = ("동글뱅이룬",   "레이어 8"),
        ["스킬"] = ("레이어 10",    "주황룬"),
        ["발동"] = ("레이어 11",    "흰룬"),
    };

    /// <summary>대표 effect_type → 컨셉. 없는 타입(첫 타·구르기 직후 같은 1회성 발동)은 「발동」.</summary>
    private static readonly Dictionary<string, string> ConceptOfEffect = new()
    {
        ["AllDamage"] = "공격", ["AttackDamage"] = "공격", ["CondAllDamage"] = "공격", ["CondAttackPercent"] = "공격",
        ["CritChance"] = "치명", ["CritDamage"] = "치명",
        ["Defense"] = "방어", ["DefensePercent"] = "방어", ["DamageReduction"] = "방어", ["CondDefensePercent"] = "방어",
        ["DamageReflect"] = "방어", ["DefenseOnHit"] = "방어",
        ["MaxHP"] = "체력", ["MaxHPPercent"] = "체력", ["CondMaxHpPercent"] = "체력",
        ["AttackSpeed"] = "속도", ["MoveSpeed"] = "속도", ["RollDistance"] = "속도",
        ["CondAttackSpeed"] = "속도", ["CondMoveSpeed"] = "속도",
        ["SkillDamage"] = "스킬", ["SkillCooldownReduction"] = "스킬", ["CondSkillDamage"] = "스킬",
    };

    /// <summary>Epic — 속성 ID → 문양.</summary>
    private static readonly Dictionary<string, string> EpicShapeByElement = new()
    {
        ["FIRE"] = "불룬", ["ICE"] = "눈결정룬", ["ELECTRIC"] = "번개룬",
        ["GRASS"] = "레이어 12", ["LIGHT"] = "레이어 7", ["DARK"] = "레이어 4",
    };

    /// <summary>Legendary — 룬 id → 고유 문양.</summary>
    private static readonly Dictionary<string, string> LegendShapeById = new()
    {
        ["item_t4_fire_aoe"]  = "도넛룬",     ["item_t4_fire_single"]  = "호떡룬",     ["item_t4_fire_proj"]  = "꽃룬",
        ["item_t4_ice_aoe"]   = "소용돌이룬", ["item_t4_ice_single"]   = "달칼날룬",   ["item_t4_ice_field"]  = "하투룬",
        ["item_t4_elec_aoe"]  = "레이어 5",   ["item_t4_elec_single"]  = "십자표창룬", ["item_t4_elec_proj"]  = "무지개룬",
        ["item_t4_grass_aoe"] = "동글뾰족룬", ["item_t4_grass_single"] = "눈룬",       ["item_t4_grass_proj"] = "도끼머리룬",
        ["item_t4_light_aoe"] = "태양룬",     ["item_t4_light_single"] = "피라미드 룬", ["item_t4_light_proj"] = "송곳니룬",
        ["item_t4_dark_aoe"]  = "흑색룬",     ["item_t4_dark_single"]  = "고슴도치룬", ["item_t4_dark_proj"]  = "갈퀴룬",
    };

    private static Dictionary<string, Sprite> _iconsByName;

    /// <summary>이 룬이 받을 문양의 스프라이트 이름. 표에 없으면 null.</summary>
    private static string ConceptIconName(RuntimeItemData data)
    {
        switch (data.rarity)
        {
            case ItemRarity.Legendary:
                return !string.IsNullOrEmpty(data.itemId) && LegendShapeById.TryGetValue(data.itemId, out var l) ? l : null;
            case ItemRarity.Epic:
                return !string.IsNullOrEmpty(data.element) && EpicShapeByElement.TryGetValue(data.element, out var e) ? e : null;
        }

        string type = EffectTypeOf(data);
        if (string.IsNullOrEmpty(type)) return null;

        string concept = ConceptOfEffect.TryGetValue(type, out var c) ? c
                       : type.StartsWith("CondCrit", System.StringComparison.Ordinal) ? "치명"
                       : "발동";
        // 조건부 = 조건이 붙은 효과(Cond*)와 방 첫 타 보너스. 모양이 상시/조건을 말한다.
        bool conditional = type.StartsWith("Cond", System.StringComparison.Ordinal) || type == "FirstHitBonus";
        var shapes = ConceptShapes[concept];
        return conditional ? shapes.conditional : shapes.always;
    }

    private static Sprite IconByName(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName) || _lib == null) return null;
        if (_iconsByName == null)
        {
            _iconsByName = new Dictionary<string, Sprite>();
            foreach (var s in _lib.AllIconSprites())
                if (s != null) _iconsByName[s.name] = s;
        }
        if (_iconsByName.TryGetValue(spriteName, out var hit)) return hit;

        Debug.LogWarning($"[RuneArt] 문양 「{spriteName}」이 RuneArtLibrary에 없다 — 기능/등급 아트로 대신한다.");
        _iconsByName[spriteName] = null;   // 같은 경고를 매 프레임 되풀이하지 않는다
        return null;
    }

    /// <summary>
    /// 룬의 기능 키. 효과 정본은 ITEM_DATA라 거기서 첫 <c>effect_type</c>을 읽는다.
    /// 한 룬이 여러 슬롯을 가질 수 있는데 문양은 <b>대표 효과</b> 하나로 정한다.
    /// </summary>
    public static string EffectTypeOf(RuntimeItemData data)
    {
        if (data == null || string.IsNullOrEmpty(data.itemId)) return null;
        var entries = Managers.ItemData?.GetItem(data.itemId);
        if (entries == null) return null;
        for (int i = 0; i < entries.Count; i++)
            if (!string.IsNullOrEmpty(entries[i].effect_type)) return entries[i].effect_type;
        return null;
    }

    /// <summary>
    /// 룬 한 칸의 겉모습(각인석 스프라이트 + 틴트)을 정하는 <b>단일 창구</b>.
    ///
    /// 속성 전용 각인석이 있으면 이미 그 속성색으로 채색돼 있으므로 흰색으로 둔다.
    /// 없으면(속성은 6종인데 각인석은 5장 — 빛에 전용 아트가 없다) 등급 각인석을
    /// 속성색으로 틴트한다. 빛 전용 아트가 나중에 채워지면 자동으로 첫 갈래를 탄다.
    ///
    /// 예전엔 드래그 블록만 폴백을 흰색으로 강제해서, 같은 빛 룬이 보관함·선택 팝업에선
    /// 노란빛인데 손에 쥐면 흰 돌로 바뀌었다.
    /// </summary>
    /// <param name="fallbackColor">속성 ID가 미정의일 때 쓸 최후 색.</param>
    public static void ResolveRuneCell(string elementId, ItemRarity rarity, Color fallbackColor,
                                       out Sprite sprite, out Color tint)
    {
        var elemArt = GetArtByElement(elementId);
        sprite = elemArt != null ? elemArt : GetArt(rarity);
        tint   = elemArt != null ? Color.white : ElementDef.IdColor(elementId, fallbackColor);
    }

    /// <summary>
    /// 판에 놓인 룬이 <b>차지하는 칸</b>에 깔 속성 타일.
    ///
    /// 룬 아이콘과는 다른 슬롯이다 — 룬 자체는 자기 룬 아트로 정체성을 유지하고,
    /// 차지한 블록 모양은 그 룬의 속성 타일로 칠해 "어느 속성이 판 어디를 먹었는지"가 한눈에 보이게 한다.
    /// 미할당이면 null → 호출측이 기존 <see cref="ResolveRuneCell"/> 규칙으로 폴백한다.
    /// </summary>
    public static Sprite GetBlockTile(string elementId)
        => _lib != null ? _lib.GetBlockTile(ElementIndex(elementId)) : null;

    private static int ElementIndex(string elementId)
    {
        if (string.IsNullOrEmpty(elementId)) return -1;
        var order = ElementDef.Order;
        for (int i = 0; i < order.Count; i++) if (order[i] == elementId) return i;
        return -1;
    }
}
