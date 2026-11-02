using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 각인 고르기 카드 그림(11-02 사용자 「무기 스킬 각인 — 설명이 제대로 필요 · 있는 리소스로 표현」) — 효과가 들어가는 모양을
/// 위에서 본 작은 그림으로 되풀이한다. 나 = 그 스킬 아이콘 · 적 = 얇은 고리 · X자 = 환영베기 X자 이펙트의 베기 그림(재련소 스킨
/// <see cref="CrucibleSkinSO.EngraveSlash"/>)을 게임처럼 ±45°로 엇갈려.
///   번짐(Spread) — 앞 적에 X자 → 가까운 적으로 차례로 옮겨 벤다(잔상 추격).
///   모음(Focus)  — 맞은 적마다 작은 X자 → 가장 큰 적(체력 최다)에게 모여 큰 X자 + 둘레 고리(응축).
/// 그림 칸 기준 좌표(240×150 · 왼쪽 아래 0)로 그린다. 창이 꺼지면 Update가 멈춘다 · 실제 시간(재련소 창은 시간을 멈춘다).
/// </summary>
public sealed class UI_EngraveDiagram : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const float Loop      = 2.8f;    // 한 바퀴(초)
    private const float FadeIn    = 0.2f;
    private const float FadeOut   = 0.35f;
    private const float Pop       = 0.16f;   // X자가 맺히는 시간
    private const float CutAt     = 0.35f;   // 첫 X자(칼을 턴 순간)
    private const float HopFirst  = 0.8f;    // 번짐 — 첫 옮겨 벰
    private const float HopGap    = 0.3f;    //   · 다음까지
    private const float LineGrow  = 0.14f;   // 옮겨 가는 선이 그어지는 시간
    private const float GatherAt  = 0.85f;   // 모음 — 선이 큰 적으로 모이기 시작
    private const float GatherDur = 0.28f;
    private const float BigCutAt  = 1.2f;    // 모음 — 큰 X자
    private const float PulseDur  = 0.4f;
    private const float MarkSize  = 44f, BigMarkSize = 72f;   // 적 고리보다 크게 — 작으면 베기 모양이 안 읽히고 「⊗」로 뭉갰다(11-02 사진)
    private const float EnemySize = 18f, BigEnemySize = 30f, PulseSize = 84f;
    private const float IconSize  = 30f;

    private const float BurstAt   = 0.85f;   // Burst — 맞은 적마다 고리가 퍼지는 때
    private static readonly Color CutColorDefault = new(1f, 0.70f, 0.66f, 1f);    // 카타나 베기(붉은 섬광) — 그림은 흰 베기라 곱해 물든다
    private static readonly Color EnemyColor = new(0.925f, 0.902f, 0.847f, 0.35f);   // 적 고리는 옅게 — X자가 앞에 선다
    private static readonly Color BigColor   = new(0.925f, 0.902f, 0.847f, 0.6f);

    private struct Mark { public RectTransform Rt; public Image A, B; public float At, DimAt; }
    private struct Line { public RectTransform Rt; public Image Img; public float At, Dur, Len; }

    // ── Private ───────────────────────────────────────────────────
    private readonly List<Mark> _marks = new(6);
    private readonly List<Line> _lines = new(4);
    private readonly List<(Image Img, float At)> _pulses = new(3);
    private CanvasGroup _group;
    private Sprite      _slash;
    private Color       _cut = CutColorDefault;   // 표식 색(카타나 붉은 섬광 · 대검 속성 호박색)
    private Color       _trail;
    private float       _t;
    private bool        _on;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake() => EnsureGroup();

    private void Update()
    {
        if (!_on) return;
        _t += Time.unscaledDeltaTime;
        if (_t >= Loop) _t -= Loop;
        Apply();
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>그림 종류로 다시 짓는다. None이면 칸을 숨긴다. <paramref name="mark"/> = 표식 그림(카타나 X자 베기 · 대검 속성 고리) · <paramref name="tint"/> = 표식 색.</summary>
    public void Show(SkillSO.EngraveIllustration kind, Sprite mark, Sprite icon, Color tint)
    {
        _cut   = tint.a > 0f ? tint : CutColorDefault;
        _trail = new Color(_cut.r, _cut.g, _cut.b, 0.4f);
        EnsureGroup();   // 꺼진 창에서 처음 불려도(Awake 전) 그린다
        Clear();
        _on = kind != SkillSO.EngraveIllustration.None;
        gameObject.SetActive(_on);
        if (!_on) return;

        _slash = mark;
        AddIcon(icon, new Vector2(120f, 22f));
        if (kind == SkillSO.EngraveIllustration.Spread)     BuildSpread();
        else if (kind == SkillSO.EngraveIllustration.Burst) BuildBurst();
        else                                                BuildFocus();
        _t = 0f;
        Apply();
    }

    // ── Private Methods ───────────────────────────────────────────
    // 잔상 추격 — 앞 적 X자 → 가까운 적 셋으로 차례로(선이 그어지며 옮겨 간다)
    private void BuildSpread()
    {
        var a  = new Vector2(120f, 66f);
        var h1 = new Vector2(190f, 72f);
        var h2 = new Vector2(172f, 124f);
        var h3 = new Vector2(66f, 112f);
        AddEnemy(a, EnemySize, EnemyColor);
        AddEnemy(h1, EnemySize, EnemyColor);
        AddEnemy(h2, EnemySize, EnemyColor);
        AddEnemy(h3, EnemySize, EnemyColor);
        AddMark(a, MarkSize, CutAt, -1f);
        float at = HopFirst;
        AddLine(a, h1, at - LineGrow, LineGrow);  AddMark(h1, MarkSize * 0.8f, at, -1f); at += HopGap;
        AddLine(h1, h2, at - LineGrow, LineGrow); AddMark(h2, MarkSize * 0.8f, at, -1f); at += HopGap;
        AddLine(h2, h3, at - LineGrow, LineGrow); AddMark(h3, MarkSize * 0.8f, at, -1f);
    }

    // 응축 — 맞은 적마다 작은 X자 → 가장 큰 적에게 모여 큰 X자 + 둘레
    private void BuildFocus()
    {
        var a   = new Vector2(64f, 70f);
        var b   = new Vector2(176f, 74f);
        var big = new Vector2(120f, 114f);
        AddEnemy(a, EnemySize, EnemyColor);
        AddEnemy(b, EnemySize, EnemyColor);
        AddEnemy(big, BigEnemySize, BigColor);
        AddMark(a,   MarkSize * 0.8f, CutAt, GatherAt);
        AddMark(b,   MarkSize * 0.8f, CutAt, GatherAt);
        AddMark(big, MarkSize * 0.8f, CutAt, GatherAt);
        AddLine(a, big, GatherAt, GatherDur);
        AddLine(b, big, GatherAt, GatherDur);
        AddPulse(big, BigCutAt, PulseSize);
        AddMark(big, BigMarkSize, BigCutAt, -1f);
    }

    // 번지는 속성(대검) — 휘두름에 맞은 적 둘 → 그 자리마다 고리가 퍼져 곁의 적 셋까지
    private void BuildBurst()
    {
        var h1 = new Vector2(96f, 70f);
        var h2 = new Vector2(152f, 74f);
        var n1 = new Vector2(48f, 110f);
        var n2 = new Vector2(124f, 124f);
        var n3 = new Vector2(200f, 108f);
        AddEnemy(h1, EnemySize, EnemyColor);
        AddEnemy(h2, EnemySize, EnemyColor);
        AddEnemy(n1, EnemySize, EnemyColor);
        AddEnemy(n2, EnemySize, EnemyColor);
        AddEnemy(n3, EnemySize, EnemyColor);
        AddMark(h1, MarkSize, CutAt, -1f);
        AddMark(h2, MarkSize, CutAt, -1f);
        AddPulse(h1, BurstAt, PulseSize);
        AddPulse(h2, BurstAt, PulseSize);
        AddMark(n1, MarkSize * 0.75f, BurstAt + 0.15f, -1f);
        AddMark(n2, MarkSize * 0.75f, BurstAt + 0.15f, -1f);
        AddMark(n3, MarkSize * 0.75f, BurstAt + 0.15f, -1f);
    }

    private void AddPulse(Vector2 at, float appearAt, float size)
        => _pulses.Add((NewImage("Pulse", UIProceduralSprites.Ring(0.06f, 128), Color.clear, at, new Vector2(size, size)), appearAt));

    private void Apply()
    {
        _group.alpha = Mathf.Min(Mathf.Clamp01(_t / FadeIn), Mathf.Clamp01((Loop - _t) / FadeOut));

        for (int i = 0; i < _marks.Count; i++)
        {
            var m = _marks[i];
            float k = Mathf.Clamp01((_t - m.At) / Pop);
            float e = 1f - (1f - k) * (1f - k);
            float a = k;
            if (m.DimAt >= 0f) a *= Mathf.Lerp(1f, 0.25f, Mathf.Clamp01((_t - m.DimAt) / GatherDur));   // 모음 — 큰 적으로 빨려 든다
            m.Rt.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, e);
            var c = _cut; c.a = a;
            m.A.color = c;
            m.B.color = c;
        }

        for (int i = 0; i < _lines.Count; i++)
        {
            var l = _lines[i];
            float k = Mathf.Clamp01((_t - l.At) / l.Dur);
            l.Rt.sizeDelta = new Vector2(l.Len * k, 1.5f);
            l.Img.color = k > 0f ? _trail : Color.clear;
        }

        for (int i = 0; i < _pulses.Count; i++)
        {
            var (img, at) = _pulses[i];
            float k = Mathf.Clamp01((_t - at) / PulseDur);
            img.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.45f, 1f, 1f - (1f - k) * (1f - k));
            var c = _cut; c.a = k > 0f ? Mathf.Lerp(0.75f, 0.3f, k) : 0f;
            img.color = c;
        }
    }

    private void EnsureGroup()
    {
        if (_group != null) return;
        if (!TryGetComponent(out _group)) _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;   // 카드 누르기를 막지 않는다
        _group.interactable   = false;
    }

    private void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
        _marks.Clear();
        _lines.Clear();
        _pulses.Clear();
    }

    private void AddIcon(Sprite icon, Vector2 at)
    {
        if (icon != null) { NewImage("Me", icon, Color.white, at, new Vector2(IconSize, IconSize)).preserveAspect = true; return; }
        var p = NewImage("Me", UIProceduralSprites.Pointer(), UITheme.Thin.Gold, at, new Vector2(18f, 18f));
        p.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // 위(앞)를 가리킨다
    }

    private void AddEnemy(Vector2 at, float size, Color color)
        => NewImage("Enemy", UIProceduralSprites.Ring(0.1f, 64), color, at, new Vector2(size, size));

    // X자 — 같은 베기 둘을 엇갈려(게임의 SpawnCross와 같다). 그림이 없으면 얇은 선 둘.
    private void AddMark(Vector2 at, float size, float appearAt, float dimAt)
    {
        var root = new GameObject("Cut", typeof(RectTransform));
        var rt   = (RectTransform)root.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.anchoredPosition = at;
        rt.sizeDelta = new Vector2(size, size);
        Vector2 stroke = _slash != null ? new Vector2(size, size) : new Vector2(size, 2f);
        var a = NewImageUnder(rt, _slash, stroke,  45f);
        var b = NewImageUnder(rt, _slash, stroke, -45f);
        _marks.Add(new Mark { Rt = rt, A = a, B = b, At = appearAt, DimAt = dimAt });
    }

    private void AddLine(Vector2 from, Vector2 to, float appearAt, float dur)
    {
        var img = NewImage("Trail", null, Color.clear, from, new Vector2(0f, 1.5f));
        var rt  = img.rectTransform;
        rt.pivot = new Vector2(0f, 0.5f);   // 시작점에서 그어 간다
        Vector2 d = to - from;
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        _lines.Add(new Line { Rt = rt, Img = img, At = appearAt, Dur = dur, Len = d.magnitude });
    }

    private Image NewImage(string name, Sprite sprite, Color color, Vector2 at, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.anchoredPosition = at;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color  = color;
        img.raycastTarget = false;
        return img;
    }

    private static Image NewImageUnder(RectTransform parent, Sprite sprite, Vector2 size, float angle)
    {
        var go = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color  = Color.clear;
        img.raycastTarget = false;
        return img;
    }
}
