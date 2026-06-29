using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// IconKey(string) → Sprite 해석의 단일 진입점(표시 전용 레이어 C).
///
/// 우선순위:
///  1) 주입/자동로드된 <see cref="EffectIconSetSO"/>에 해당 키 스프라이트가 있으면 그것(=실제 아트).
///  2) 없으면 런타임 생성 플레이스홀더(키별 색 토큰, 다크판타지 톤). 캐시 1회 생성.
///
/// ■ 아트 교체 방법(코드 수정 불필요):
///   - EffectIconSet.asset 생성(메뉴: RelicFairy/Effect/Effect Icon Set) → 키별 스프라이트 할당
///   - 해당 에셋을 Addressable 키 "UI/EffectIconSet"로 등록 → 자동 채택
///   - 또는 부트스트랩에서 <see cref="SetIconSet"/>로 직접 주입
/// </summary>
public static class EffectIconRegistry
{
    private const string IconSetAddressKey = "UI/EffectIconSet";
    private const int    PlaceholderSize   = 40;

    private static EffectIconSetSO _iconSet;
    private static bool _loadAttempted;
    private static readonly Dictionary<string, Sprite> _placeholders = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _iconSet = null;
        _loadAttempted = false;
        _placeholders.Clear();   // 이전 도메인의 Texture는 리로드로 이미 무효
    }

    /// <summary>아트 세트 직접 주입(부트스트랩용). null이면 플레이스홀더로 폴백.</summary>
    public static void SetIconSet(EffectIconSetSO iconSet)
    {
        _iconSet = iconSet;
        _loadAttempted = true;
    }

    /// <summary>현재 실제 아트 세트가 채택됐는지(플레이스홀더가 아닌지).</summary>
    public static bool HasIconSet => _iconSet != null;

    /// <summary>
    /// IconKey → Sprite. 실제 아트가 있으면 그것을, 없으면 플레이스홀더를 동기 반환.
    /// 최초 호출 시 Addressable 자동 로드를 1회 시도(에셋 없으면 조용히 폴백).
    /// </summary>
    public static Sprite GetSprite(string iconKey)
    {
        if (string.IsNullOrEmpty(iconKey)) iconKey = "unknown";
        string key = iconKey.ToLowerInvariant();

        if (!_loadAttempted)
            TryAutoLoadAsync().Forget();

        if (_iconSet != null && _iconSet.TryGet(key, out var art) && art != null)
            return art;

        return GetOrCreatePlaceholder(key);
    }

    // ── 자동 로드(조용한 폴백) ───────────────────────────────────
    private static async UniTaskVoid TryAutoLoadAsync()
    {
        _loadAttempted = true;
        var am = Managers.AddressableManager;
        if (am == null) return;
        try
        {
            var so = await am.TryLoadAssetAsync<EffectIconSetSO>(IconSetAddressKey);
            if (so != null) _iconSet = so;
        }
        catch (System.OperationCanceledException) { }
    }

    // ── 플레이스홀더(키별 색 토큰) ──────────────────────────────
    private static Sprite GetOrCreatePlaceholder(string key)
    {
        if (_placeholders.TryGetValue(key, out var cached) && cached != null)
            return cached;

        var sprite = BuildTokenSprite(KeyColor(key));
        _placeholders[key] = sprite;
        return sprite;
    }

    /// <summary>원형 토큰 스프라이트 생성 — 중심 채움 + 가장자리 어둡게 + 외곽 AA. 다크판타지 톤.</summary>
    private static Sprite BuildTokenSprite(Color baseColor)
    {
        int size = PlaceholderSize;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp,
            name       = "EffectTokenPlaceholder",
        };

        var pixels = new Color32[size * size];
        Color rim = baseColor * 0.45f; rim.a = 1f;

        for (int y = 0; y < size; y++)
        {
            float ny = (y + 0.5f) / size * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float r  = Mathf.Sqrt(nx * nx + ny * ny);

                Color c;
                float a;
                if (r >= 1f)
                {
                    c = rim; a = 0f;
                }
                else if (r >= 0.82f)
                {
                    // 외곽 림: 어둡게 + 바깥쪽 알파 페이드(AA)
                    c = rim;
                    a = Mathf.Clamp01((1f - r) / 0.18f);
                }
                else
                {
                    // 내부: 중심 밝고 가장자리로 갈수록 약간 어둡게 + 상단 살짝 하이라이트
                    float shade = Mathf.Lerp(1f, 0.72f, r / 0.82f);
                    float hi    = Mathf.Clamp01(-ny) * 0.12f;
                    c = baseColor * (shade + hi);
                    a = 1f;
                }

                c.a = a;
                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    /// <summary>IconKey → 플레이스홀더 색. 속성계는 ElementDef 색 재사용, 그 외 다크판타지 팔레트.</summary>
    private static Color KeyColor(string key)
    {
        switch (key)
        {
            // ── 속성계: ElementDef 색 재사용 ──
            case "fire":       return ElementDef.GetById("FIRE")?.Color     ?? new Color(1.00f, 0.38f, 0.22f);
            case "freeze":     return ElementDef.GetById("ICE")?.Color      ?? new Color(0.45f, 0.80f, 1.00f);
            case "lightning":  return ElementDef.GetById("ELECTRIC")?.Color ?? new Color(1.00f, 0.88f, 0.25f);
            case "poison":     return ElementDef.GetById("GRASS")?.Color    ?? new Color(0.55f, 0.82f, 0.30f);
            case "element":    return new Color(0.90f, 0.78f, 0.45f);

            // ── 공격계: 적/주황 ──
            case "dmg":        return new Color(0.85f, 0.30f, 0.25f);
            case "atk":        return new Color(0.90f, 0.45f, 0.20f);
            case "crit":       return new Color(0.95f, 0.30f, 0.35f);
            case "critdmg":    return new Color(0.95f, 0.38f, 0.30f);

            // ── 방어계: 강철청 ──
            case "def":        return new Color(0.40f, 0.55f, 0.75f);
            case "shield":     return new Color(0.45f, 0.62f, 0.85f);

            // ── 생존/자원계 ──
            case "hp":         return new Color(0.80f, 0.30f, 0.35f);
            case "heal":       return new Color(0.45f, 0.80f, 0.45f);
            case "lifesteal":  return new Color(0.70f, 0.20f, 0.35f);
            case "gold":       return new Color(0.95f, 0.78f, 0.30f);
            case "luck":       return new Color(0.60f, 0.82f, 0.45f);

            // ── 유틸/이동계 ──
            case "speed":      return new Color(0.40f, 0.78f, 0.85f);
            case "atkspeed":   return new Color(0.55f, 0.80f, 0.80f);
            case "cooldown":   return new Color(0.40f, 0.70f, 0.72f);
            case "roll":       return new Color(0.55f, 0.58f, 0.66f);
            case "projectile": return new Color(0.80f, 0.70f, 0.45f);
            case "range":      return new Color(0.78f, 0.68f, 0.50f);

            // ── 속성(빛/어둠) — GuidelineVisual 팔레트와 정합 ──
            case "light":      return new Color(1.00f, 0.95f, 0.55f);
            case "dark":       return new Color(0.60f, 0.35f, 0.85f);

            // ── 기타 ──
            case "skill":      return new Color(0.65f, 0.45f, 0.90f);
            case "stun":       return new Color(0.92f, 0.85f, 0.40f);
            case "allstats":   return new Color(0.90f, 0.78f, 0.45f);
            case "utility":    return new Color(0.60f, 0.58f, 0.72f);
            case "special":    return new Color(0.70f, 0.45f, 0.85f);

            case "unknown":
            default:           return new Color(0.55f, 0.55f, 0.60f);
        }
    }
}
