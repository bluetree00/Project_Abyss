using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지금 할 수 있는 것에 은은한 불 — 버튼 바깥으로 번지는 금빛 후광이 천천히 숨 쉰다.
/// 사용자(09-29): 「할 수 있는 내용에 불을 미세하게 준다던지 소모할 수 있다는 걸 표현하지 않으면 가이드가 되지 않아」.
///
/// <para>후광은 버튼의 자식이지만 안쪽이 비어 있어(<see cref="UIProceduralSprites.SoftHalo"/>) 버튼 면을 덮지 않는다.
/// 켜고 끌 때 0.2초에 걸쳐 스민다(순간 등장 금지). 시간이 멈춘 팝업 안에서도 돈다(unscaled).</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class UIAffordGlow : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const float Feather = 16f;     // 버튼 가장자리 밖으로 번지는 폭(px)
    private const float Period  = 1.8f;    // 숨 한 번(초)
    private const float MinA    = 0.16f;
    private const float MaxA    = 0.42f;
    private const float FadeSec = 0.2f;
    private static readonly Color GlowInk = new(1f, 0.80f, 0.42f, 1f);

    // ── Private ──────────────────────────────────────────────
    private Image _glow;
    private bool  _on;
    private float _k;   // 켜짐 정도 0~1(스밈)

    // ── Lifecycle ────────────────────────────────────────────
    private void Update()
    {
        _k = Mathf.MoveTowards(_k, _on ? 1f : 0f, Time.unscaledDeltaTime / FadeSec);
        if (_glow == null) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / Period));
        var c = GlowInk;
        c.a = _k * Mathf.Lerp(MinA, MaxA, pulse);
        _glow.color = c;
        if (!_on && _k <= 0f) { _glow.enabled = false; enabled = false; }
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>대상(버튼 등)에 불을 켜거나 끈다. 꺼 둘 때는 컴포넌트를 새로 붙이지 않는다.</summary>
    public static void Set(Component target, bool on)
    {
        if (target == null) return;
        if (!target.TryGetComponent<UIAffordGlow>(out var g))
        {
            if (!on) return;
            g = target.gameObject.AddComponent<UIAffordGlow>();
        }
        g.SetOn(on);
    }

    public void SetOn(bool on)
    {
        _on = on;
        if (on) EnsureGlow();
        if (_glow != null) _glow.enabled = true;
        enabled = true;
    }

    // ── Private Methods ──────────────────────────────────────

    private void EnsureGlow()
    {
        if (_glow != null) return;
        var go = new GameObject("AffordGlow", typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(transform, false);
        go.transform.SetAsFirstSibling();   // 글자·아이콘보다 뒤
        go.AddComponent<LayoutElement>().ignoreLayout = true;   // 버튼에 레이아웃 그룹이 있어도 끌려가지 않게
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-Feather, -Feather);
        rt.offsetMax = new Vector2(Feather, Feather);
        _glow = go.AddComponent<Image>();
        _glow.sprite        = UIProceduralSprites.SoftHalo(radius: 10f, feather: Feather);
        _glow.type          = Image.Type.Sliced;
        _glow.raycastTarget = false;
        _glow.color         = new Color(GlowInk.r, GlowInk.g, GlowInk.b, 0f);
    }
}
