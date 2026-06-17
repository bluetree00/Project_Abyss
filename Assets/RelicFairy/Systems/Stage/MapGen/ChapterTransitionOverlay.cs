using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 챕터 전환 풀스크린 연출 — 검은 커버 페이드인 + "CHAPTER N" 타이틀, 다음 챕터 빌드 후 페이드아웃.
/// 보스 클리어 후 다음 챕터 procgen이 커버 아래에서 빌드되는 동안 화면을 가린다(ScreenFade 와이프 위 최상단).
/// 단발성: PlayInAsync로 가리고 → 호출자가 다음 방 빌드 → PlayOutAsync로 드러낸 뒤 자동 파괴.
/// </summary>
public sealed class ChapterTransitionOverlay
{
    private const int   SortingOrder = 32000; // ScreenFade 와이프보다 위
    private const float FadeInDur    = 0.4f;
    private const float HoldDur      = 1.2f;
    private const float FadeOutDur   = 0.5f;

    private GameObject  _root;
    private CanvasGroup _group;

    /// <summary>검은 커버 페이드인 + 타이틀 표시 후 잠시 홀드(화면이 가려진 상태로 반환).</summary>
    public async UniTask PlayInAsync(string title, string subtitle, CancellationToken ct)
    {
        Build(title, subtitle);
        await FadeAsync(0f, 1f, FadeInDur, ct);
        try { await UniTask.Delay(TimeSpan.FromSeconds(HoldDur), ignoreTimeScale: true, cancellationToken: ct); }
        catch (OperationCanceledException) { }
    }

    /// <summary>커버 페이드아웃 후 오버레이 파괴(다음 챕터 방 노출).</summary>
    public async UniTask PlayOutAsync(CancellationToken ct)
    {
        await FadeAsync(1f, 0f, FadeOutDur, ct);
        if (_root != null) UnityEngine.Object.Destroy(_root);
    }

    private void Build(string title, string subtitle)
    {
        _root = new GameObject("ChapterTransitionOverlay");

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        _group       = _root.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        var bgGO  = new GameObject("BG");
        bgGO.transform.SetParent(_root.transform, false);
        var bg    = bgGO.AddComponent<Image>();
        bg.color  = Color.black;
        var bgRT  = bg.rectTransform;
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        CreateText(title, 90f, new Vector2(0f, 30f), FontStyles.Bold);
        if (!string.IsNullOrEmpty(subtitle))
            CreateText(subtitle, 38f, new Vector2(0f, -70f), FontStyles.Normal);
    }

    private void CreateText(string text, float size, Vector2 pos, FontStyles style)
    {
        var go = new GameObject("Title");
        go.transform.SetParent(_root.transform, false);
        var tmp       = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        var rt = tmp.rectTransform;
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(1400f, 200f);
        rt.anchoredPosition = pos;
    }

    private async UniTask FadeAsync(float from, float to, float dur, CancellationToken ct)
    {
        if (_group == null) return;
        float t = 0f;
        try
        {
            while (t < dur)
            {
                ct.ThrowIfCancellationRequested();
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
                await UniTask.Yield();
            }
        }
        catch (OperationCanceledException) { }
        if (_group != null) _group.alpha = to;
    }
}
