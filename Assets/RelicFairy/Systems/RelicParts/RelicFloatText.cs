using TMPro;
using UnityEngine;

/// <summary>
/// 유물 성장 v2 — 적 위에 잠깐 뜨는 짧은 글자(반응 이름 · 조각 발동, 설계 v2 §6-3).
/// 임시로 쓰던 GuidelineVisual.Toast는 개발 빌드에서만 돈다 — 출시 빌드에선 반응이 일어나도 아무 글자도 안 떴다. 이 글자는 출시에서도 뜬다.
/// 월드 TextMeshPro 16개를 돌려 쓴다 · 부드러운 그림자 · 카메라를 본다 · 0.5 m 떠오르며 0.9초 · 흔들림 없음.
/// 글꼴은 <b>얇은 본문체(NotoSansKR)</b> — 기본 폰트(DNFForgedBlade Bold)는 굵은 디스플레이체라 3D 인게임 글자엔 무겁다(10-02 사용자: 기존 글자 연출은 레거시, 더 얇게).
/// 같은 자리에 0.4초 안 연달아 뜨면 위로 비켜 쌓는다(겹치면 둘 다 안 읽힌다). 컷신 중엔 띄우지 않는다.
/// </summary>
public sealed class RelicFloatText : MonoBehaviour
{
    private const int   PoolSize    = 16;
    private const float Life        = 0.9f;
    private const float Rise        = 0.5f;
    private const float FadeFrom    = 0.6f;    // 수명의 이 비율부터 흐려진다
    private const float FontSize    = 4.2f;
    private const float BaseHeight  = 2.4f;    // 적 발밑 기준 글자 높이
    private const float StackStep   = 0.45f;
    private const float StackWindow = 0.4f;
    private const float StackRadius = 3f;
    private const float ThinDilate  = 0.08f;   // Noto는 Thin 마스터 SDF라 0이면 획이 끊긴다 · 기본 재질 0.18은 다시 굵다 — 그 사이

    private sealed class Item
    {
        public TextMeshPro Tmp;
        public Transform   Tf;
        public Vector3     From;
        public Color       Color;
        public float       Age;
        public bool        Live;
    }

    private static RelicFloatText s_instance;
    private static TMP_FontAsset  s_thinFont;
    private static Material       s_thinMat;

    private readonly Item[] _items = new Item[PoolSize];
    private int     _next;
    private float   _lastAt = -1f;
    private int     _stack;
    private Vector3 _lastPos;
    private Camera  _cam;

    /// <summary>글자 하나를 띄운다. <paramref name="at"/>은 대상 발밑(높이는 여기서 더한다).</summary>
    public static void Show(Vector3 at, string text, Color color)
    {
        if (string.IsNullOrEmpty(text) || !Application.isPlaying) return;
        if (GameRunBootstrapper.Instance?.Run?.InCutscene == true) return;
        if (s_instance == null) s_instance = new GameObject("@RelicFloatText").AddComponent<RelicFloatText>();
        s_instance.Spawn(at, text, color);
    }

    private void Spawn(Vector3 at, string text, Color color)
    {
        bool near = Time.unscaledTime - _lastAt < StackWindow && (at - _lastPos).sqrMagnitude < StackRadius * StackRadius;
        _stack   = near ? Mathf.Min(_stack + 1, 3) : 0;
        _lastAt  = Time.unscaledTime;
        _lastPos = at;

        var it = _items[_next] ??= CreateItem();
        _next = (_next + 1) % PoolSize;
        it.From  = at + Vector3.up * (BaseHeight + StackStep * _stack);
        it.Color = new Color(color.r, color.g, color.b, 1f);
        it.Age   = 0f;
        it.Live  = true;
        it.Tmp.text  = text;
        it.Tmp.color = it.Color;
        it.Tf.position = it.From;
        it.Tmp.gameObject.SetActive(true);
    }

    private Item CreateItem()
    {
        var go = new GameObject("RelicFloatText_Item");
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.fontSize           = FontSize;
        tmp.textWrappingMode   = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
        tmp.fontStyle          = FontStyles.Normal;
        var thin = ThinFont();
        if (thin != null) tmp.font = thin;
        TMPOutlineHelper.ApplySoftShadow(tmp);
        if (thin != null) tmp.fontSharedMaterial = ThinMaterial(tmp.fontSharedMaterial);
        go.SetActive(false);
        return new Item { Tmp = tmp, Tf = go.transform };
    }

    /// <summary>얇은 본문체 — 기본 폰트의 폴백 목록에 이미 들어 있어(DnfFontAssetGenerator가 NotoSansKR를 폴백으로 건다) 따로 불러오지 않는다.</summary>
    private static TMP_FontAsset ThinFont()
    {
        if (s_thinFont != null) return s_thinFont;
        var def = TMP_Settings.defaultFontAsset;
        if (def == null) return null;
        if (def.name.Contains("NotoSansKR")) return s_thinFont = def;
        var table = def.fallbackFontAssetTable;
        if (table == null) return null;
        for (int i = 0; i < table.Count; i++)
            if (table[i] != null && table[i].name.Contains("NotoSansKR")) return s_thinFont = table[i];
        return null;
    }

    /// <summary>부드러운 그림자 재질을 한 벌 복사해 획 두께만 줄인다(글자마다 재질을 만들지 않는다 — 배칭 유지).</summary>
    private static Material ThinMaterial(Material soft)
    {
        if (s_thinMat != null) return s_thinMat;
        if (soft == null) return null;
        s_thinMat = new Material(soft) { name = soft.name + " (RelicThin)" };
        if (s_thinMat.HasProperty(ShaderUtilities.ID_FaceDilate)) s_thinMat.SetFloat(ShaderUtilities.ID_FaceDilate, ThinDilate);
        return s_thinMat;
    }

    private void Update()
    {
        if (_cam == null) _cam = Camera.main;
        float dt = Time.unscaledDeltaTime;   // 슬로모 · 막타 정지 동안에도 읽히는 속도로
        for (int i = 0; i < PoolSize; i++)
        {
            var it = _items[i];
            if (it == null || !it.Live) continue;
            it.Age += dt;
            float t = it.Age / Life;
            if (t >= 1f)
            {
                it.Live = false;
                it.Tmp.gameObject.SetActive(false);
                continue;
            }
            float ease = 1f - (1f - t) * (1f - t);
            it.Tf.position = it.From + Vector3.up * (Rise * ease);
            if (_cam != null) it.Tf.rotation = _cam.transform.rotation;
            var c = it.Color;
            c.a = t < FadeFrom ? 1f : 1f - (t - FadeFrom) / (1f - FadeFrom);
            it.Tmp.color = c;
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }
}
