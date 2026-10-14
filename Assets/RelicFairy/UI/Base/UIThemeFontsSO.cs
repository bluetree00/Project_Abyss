using TMPro;
using UnityEngine;

/// <summary>
/// 톤 v2 글꼴 세 벌 — Noto Sans KR 300 · 400 · 500 정적 굵기(10-07 UI 톤 v2 「얇은 금선」).
/// 가변 폰트를 그대로 구우면 기본 마스터(Thin 100)로 구워져 작은 글자가 끊겼다 — 굵기별로 뽑은 TTF로 만든다.
/// 값은 에디터 메뉴 「RelicFairy/UI/10-07 톤 v2 글꼴 만들기·등록」이 채운다. 런타임 상태 없음.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Theme Fonts", fileName = "UIThemeFonts")]
public sealed class UIThemeFontsSO : ScriptableObject
{
    [SerializeField] private TMP_FontAsset light;
    [SerializeField] private TMP_FontAsset regular;
    [SerializeField] private TMP_FontAsset medium;

    public TMP_FontAsset Light   => light;
    public TMP_FontAsset Regular => regular;
    public TMP_FontAsset Medium  => medium;

    public TMP_FontAsset Get(UIWeight weight) => weight switch
    {
        UIWeight.Light  => light,
        UIWeight.Medium => medium,
        _               => regular,
    };

#if UNITY_EDITOR
    /// <summary>에디터 빌더 전용 — 런타임에서 부르지 않는다.</summary>
    public void EditorAssign(TMP_FontAsset l, TMP_FontAsset r, TMP_FontAsset m)
    {
        light = l; regular = r; medium = m;
    }
#endif
}
