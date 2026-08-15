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
