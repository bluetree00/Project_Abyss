using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버프 그리드의 정사각형 셀 1개(절차 생성, 풀 재사용). 아이콘 + 스택 숫자 + <b>잔여 시간 스윕</b>.
///
/// 잔여 시간은 예전엔 그리드 아래 별도 막대 영역에 그렸는데, 좌측 도크(무기 패널 위 · 서약 박스 아래)에
/// 막대가 들어갈 세로 여유가 없어 무기 패널을 침범했다. 그래서 칸 위에 시계방향으로 걷히는
/// 어두운 부채꼴(Radial360)로 얹는다 — 공간을 전혀 더 쓰지 않고 남은 시간이 그대로 읽힌다.
///
/// 데이터는 BuffViewItem(읽기 전용 뷰모델)만 받는다. 위치/크기는 부모 GridLayoutGroup이 제어.
/// </summary>
public sealed class BuffCell : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private static readonly Color BuffBg   = new(0.10f, 0.12f, 0.16f, 0.85f);
    private static readonly Color DebuffBg = new(0.35f, 0.12f, 0.12f, 0.88f);

    // 스킨 모드: 배경 아트를 유지한 채 디버프만 붉게 틴트(디버프 신호 보존).
    private static readonly Color SkinBuffTint   = Color.white;
    private static readonly Color SkinDebuffTint = new(1f, 0.62f, 0.62f, 1f);

    // 잔여 시간 스윕 — 남은 비율만큼 '덜 가려지는' 어두운 부채꼴(0=거의 다 가림, 1=안 가림).
    private static readonly Color SweepColor = new(0.02f, 0.02f, 0.05f, 0.62f);

    private static Sprite s_white;

    /// <summary>Filled 이미지용 1×1 흰 스프라이트(전 셀 공용, 1회 생성).</summary>
    private static Sprite WhiteSprite
    {
        get
        {
            if (s_white != null) return s_white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "BuffSweepWhite" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            s_white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            s_white.name = "BuffSweepWhite";
            return s_white;
        }
    }

    private Image    _bg;
    private Image    _icon;
    private Image    _sweep;
    private TMP_Text _stack;

    private bool _hasSkin;
    private Func<string, Sprite> _iconResolver;

    private BuffViewItem _item;
    private Action<BuffCell, bool> _onHover;

    public BuffViewItem Item => _item;
    public RectTransform Rect => (RectTransform)transform;

    /// <summary>
    /// 셀 내부 위젯을 1회 생성. 부모 GridLayoutGroup이 셀 크기를 제어하므로 자식만 anchor로 배치.
    /// innerSprite/frameSprite를 주면 디자이너 아트로, 없으면 기존 단색 배경으로 그린다.
    /// iconResolver는 IconKey → Sprite 해석기(미지정 시 EffectIconRegistry 직접 사용).
    /// </summary>
    public void Initialize(TMP_FontAsset font, Action<BuffCell, bool> onHover,
                           Sprite innerSprite = null, Sprite frameSprite = null,
                           Func<string, Sprite> iconResolver = null)
    {
        _onHover      = onHover;
        _iconResolver = iconResolver;
        _hasSkin      = innerSprite != null;

        _bg = gameObject.GetComponent<Image>();
        if (_bg == null) _bg = gameObject.AddComponent<Image>();
        if (_hasSkin)
        {
            _bg.sprite = innerSprite;
            _bg.type   = Image.Type.Sliced;
            _bg.color  = SkinBuffTint;
        }
        else
        {
            _bg.color = BuffBg;
        }
        _bg.raycastTarget = true;   // 호버 감지

        // 아이콘(칸 거의 가득)
        _icon = CreateChildImage("Icon", new Vector2(0.14f, 0.14f), new Vector2(0.86f, 0.86f));
        _icon.preserveAspect = true;
        _icon.raycastTarget = false;

        // 잔여 시간 스윕 — 아이콘 위, 장식 테두리 아래. 남은 비율만큼 덜 가려진다.
        // Image.Type.Filled는 스프라이트가 없으면 fillAmount를 무시하고 통짜로 그려지므로
        // 1×1 흰 스프라이트를 물려준다.
        _sweep = CreateChildImage("Sweep", Vector2.zero, Vector2.one);
        _sweep.sprite         = WhiteSprite;
        _sweep.color          = SweepColor;
        _sweep.raycastTarget  = false;
        _sweep.type           = Image.Type.Filled;
        _sweep.fillMethod     = Image.FillMethod.Radial360;
        _sweep.fillOrigin     = (int)Image.Origin360.Top;
        _sweep.fillClockwise  = true;
        _sweep.fillAmount     = 0f;
        _sweep.gameObject.SetActive(false);

        // 테두리 오버레이(아이콘 위) — 스킨 시에만 생성
        if (frameSprite != null)
        {
            var frame = CreateChildImage("Frame", Vector2.zero, Vector2.one);
            frame.sprite        = frameSprite;
            frame.type          = Image.Type.Sliced;
            frame.raycastTarget = false;
        }

        // 스택 숫자(칸 우하단)
        var stackGo = new GameObject("Stack", typeof(RectTransform));
        stackGo.transform.SetParent(transform, false);
        var srt = stackGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(1f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot     = new Vector2(1f, 0f);
        srt.anchoredPosition = new Vector2(-2f, 1f);
        srt.sizeDelta = new Vector2(28f, 16f);
        _stack = stackGo.AddComponent<TextMeshProUGUI>();
        if (font != null) _stack.font = font;
        _stack.fontSize  = 13f;
        _stack.fontStyle = FontStyles.Bold;
        _stack.color     = new Color(1f, 0.95f, 0.7f, 1f);
        _stack.alignment = TextAlignmentOptions.BottomRight;
        _stack.raycastTarget = false;
        var ol = stackGo.AddComponent<Outline>();
        ol.effectColor    = new Color(0f, 0f, 0f, 0.9f);
        ol.effectDistance = new Vector2(1f, -1f);
        stackGo.SetActive(false);
    }

    /// <summary>버프 데이터 바인드(구조 변경 시). 셀 활성화 + 아이콘/스택 갱신.</summary>
    public void Bind(in BuffViewItem item)
    {
        _item = item;
        gameObject.SetActive(true);

        _bg.color = _hasSkin
            ? (item.IsDebuff ? SkinDebuffTint : SkinBuffTint)
            : (item.IsDebuff ? DebuffBg : BuffBg);

        _icon.sprite = _iconResolver != null
            ? _iconResolver(item.IconKey)
            : EffectIconRegistry.GetSprite(item.IconKey);

        bool hasStack = item.Stacks > 1;
        _stack.gameObject.SetActive(hasStack);
        if (hasStack) _stack.text = "×" + item.Stacks;

        ApplyRemaining(item.Remaining01);
    }

    /// <summary>잔여 비율(0~1)을 칸 위 스윕으로 반영. 음수(무한/해당없음)면 숨긴다.</summary>
    private void ApplyRemaining(float remaining01)
    {
        if (_sweep == null) return;

        bool show = remaining01 >= 0f;
        if (_sweep.gameObject.activeSelf != show) _sweep.gameObject.SetActive(show);
        if (show) _sweep.fillAmount = 1f - Mathf.Clamp01(remaining01);   // 지난 만큼 가린다
    }

    /// <summary>동적 값(스택 수·잔여 시간)만 in-place 갱신.</summary>
    public void UpdateValues(in BuffViewItem item)
    {
        _item = item;
        if (_stack.gameObject.activeSelf && item.Stacks > 1)
            _stack.text = "×" + item.Stacks;

        ApplyRemaining(item.Remaining01);
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData) => _onHover?.Invoke(this, true);
    public void OnPointerExit(PointerEventData eventData)  => _onHover?.Invoke(this, false);

    private Image CreateChildImage(string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return go.GetComponent<Image>();
    }
}
