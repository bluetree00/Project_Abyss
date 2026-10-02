using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 보스 처치 뒤 「되찾은 기억」 연출(10-02 사용자 「보스를 잡고 유물이 확장되는 이유와 연출 시나리오」 → 축 「기억을 되찾아간다」).
/// 기획 `RelicFairy_유물기억_되찾는연출_시나리오_20261002.md` §2의 ② · ③ · ⑤를 맡는다 — 고르기 창(④)은 유물 성장 쪽(f7)이 맡는다.
/// <list type="bullet">
/// <item>② 기억의 빛 — 쓰러진 자리에서 빛이 떠올라 플레이어의 유물로 스며든다(유물 계열색 · 흔들림 없음).</item>
/// <item>③ 회상 한 줄 — 화면이 옅어지고 기사 1인칭 기억 한 줄. 대사 CSV <c>RelicMemory_Recall_{유물 id}</c>(시기 꼬리) 중 한 줄.</item>
/// <item>⑤ 한 마디 — 고른 뒤 멀린/그림자 자막. 대사 CSV <c>RelicMemory_Return</c>(시기 꼬리 · 반복 단계).</item>
/// </list>
/// 모든 시간은 실시간(unscaled)이다 — 이어서 뜨는 고르기 창이 시간을 멈춘다.
/// </summary>
public static class RelicMemoryRecall
{
    // ── Constants ─────────────────────────────────────────────────
    private const string RecallKeyPrefix = "RelicMemory_Recall_";
    private const string ReturnKey       = "RelicMemory_Return";

    private const float RiseSeconds    = 0.35f;   // 쓰러진 자리에서 떠오름
    private const float RiseHeight     = 1.6f;
    private const float FlySeconds     = 0.85f;   // 플레이어에게 날아감
    private const float ArcHeight      = 1.4f;
    private const float AbsorbHold     = 0.25f;   // 스며든 뒤 고리가 퍼지는 동안
    private const float PlayerChest    = 1.1f;
    private const float OrbScale       = 0.55f;
    private const float RingScale      = 0.7f;

    private const float LineFadeIn     = 0.35f;
    private const float LineHold       = 1.8f;
    private const float LineFadeOut    = 0.45f;
    private const float LineSize       = 40f;
    private const float DimAlpha       = 0.28f;   // 화면을 옅게 — 회상의 결(은은하게)
    private const float BandAlpha      = 0.62f;

    private const float EndSceneWaitCap = 10f;    // 끝 장면 · 자막 대기 상한(넘으면 그대로 진행 — 진행 보장)

    private static readonly Color DefaultTint = new(1.00f, 0.82f, 0.45f, 1f);   // 유물 계열색이 없을 때 = 금빛
    private static readonly Color RadiantGold = new(1.00f, 0.86f, 0.40f, 1f);   // 찬란 = 유물과 무관하게 금빛
    private static readonly Color LineColor   = new(0.93f, 0.92f, 0.88f, 1f);

    // 등급별 빛(시나리오 §2 ② — 「빛의 세기가 등급을 예고한다」): 흐릿 = 옅은 빛 · 선명 = 유물 계열색 · 찬란 = 금빛 분출
    private const float FaintWash      = 0.55f;   // 흐릿 — 계열색을 흰빛 쪽으로 이만큼 바랜다
    private const float FaintScale     = 0.8f;
    private const float RadiantScale   = 1.35f;
    private const float RadiantBurst   = 0.9f;    // 찬란 — 쓰러진 자리에서 먼저 터지는 분출 크기

    // ── Public Methods ────────────────────────────────────────────

    /// <summary>
    /// 보스 끝 장면(봉인 · 처치 — 런이 컷신 HUD인 동안)과 그 자막이 끝날 때까지 기다린다.
    /// 예전엔 클리어 신호 직후 고르기 창이 시간을 멈추며 떠 끝 장면을 덮고, 남은 자막을 걷어 버렸다(09-25 실측).
    /// </summary>
    public static async UniTask WaitBossEndSceneAsync(GameRunSession run, CancellationToken ct)
    {
        float t0 = Time.unscaledTime;
        await UniTask.WaitUntil(() =>
                (run == null || !run.InCutscene) && !UI_BossBark.IsShowing
                || Time.unscaledTime - t0 > EndSceneWaitCap,
            PlayerLoopTiming.Update, ct);
        if (Time.unscaledTime - t0 > EndSceneWaitCap)
            Debug.LogWarning($"[RelicMemory] 끝 장면 대기 상한 {EndSceneWaitCap}초 — 그대로 진행");
    }

    /// <summary>
    /// ② 기억의 빛 + ③ 회상 한 줄. <paramref name="from"/> = 보스가 쓰러진 자리(없으면 방 중심).
    /// <paramref name="intensity"/> = 크기 배율(1 = 기본), 색은 유물 계열색. 등급이 있으면 아래 등급 오버로드를 쓴다(시나리오 B1).
    /// </summary>
    public static UniTask PlayAsync(Vector3 from, PlayerController player, RelicClassSO relic, CancellationToken ct, float intensity = 1f)
        => PlayCoreAsync(from, player, relic, ThemeOf(relic), intensity, false, ct);

    /// <summary>
    /// 등급으로 빛을 정한다(유물 성장 v2 — 창이 보여 줄 드래프트의 최고 등급). 색과 크기를 함께 바꾼다:
    /// 흐릿 = 계열색을 바랜 옅은 빛 ×0.8 · 선명(없음 포함) = 계열색 ×1.0 · 찬란 = 금빛 ×1.35 + 쓰러진 자리 분출.
    /// </summary>
    public static UniTask PlayAsync(Vector3 from, PlayerController player, RelicClassSO relic, RelicMemoryGrade grade, CancellationToken ct)
    {
        Color theme = ThemeOf(relic);
        return grade switch
        {
            RelicMemoryGrade.Faint   => PlayCoreAsync(from, player, relic, Color.Lerp(theme, Color.white, FaintWash), FaintScale, false, ct),
            RelicMemoryGrade.Radiant => PlayCoreAsync(from, player, relic, RadiantGold, RadiantScale, true, ct),
            _                        => PlayCoreAsync(from, player, relic, theme, 1f, false, ct),
        };
    }

    private static Color ThemeOf(RelicClassSO relic)
    {
        Color c = relic != null ? relic.ThemeColor(DefaultTint) : DefaultTint;
        c.a = 1f;
        return c;
    }

    private static async UniTask PlayCoreAsync(Vector3 from, PlayerController player, RelicClassSO relic, Color tint, float intensity, bool burst, CancellationToken ct)
    {
        if (player == null) return;

        try { await LightAsync(from, player.transform, tint, intensity, burst, ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { Debug.LogWarning($"[RelicMemory] 기억의 빛 예외(건너뜀): {e.Message}"); }

        string line = PickRecallLine(relic);
        Debug.Log($"[RelicMemory] 기억의 빛 끝 · 회상 「{line}」");
        if (string.IsNullOrEmpty(line)) return;
        await LineAsync(line, ct);
    }

    /// <summary>
    /// 카드의 기억 한 줄(시나리오 B2 · 데이터 <c>memory_line</c> / <c>memory_line_radiant</c>) — 등급이 가림을 정한다.
    /// 흐릿 = 앞 절반(어절)만 보이고 뒤는 「…」 · 선명 = 온전히 · 찬란 = 온전히 + 둘째 줄. 줄이 비면 빈 문자열.
    /// </summary>
    public static string MemoryLineFor(RelicPartEntry entry, RelicMemoryGrade grade)
    {
        if (entry == null || string.IsNullOrEmpty(entry.memory_line)) return string.Empty;
        return grade switch
        {
            RelicMemoryGrade.Radiant => string.IsNullOrEmpty(entry.memory_line_radiant)
                                        ? entry.memory_line
                                        : entry.memory_line + "\n" + entry.memory_line_radiant,
            RelicMemoryGrade.Clear   => entry.memory_line,
            _                        => FaintLine(entry.memory_line),
        };
    }

    /// <summary>⑤ 고른 뒤 한 마디 — 시기 · 반복 단계에 맞는 멀린/그림자 자막(비모달).</summary>
    public static void SayReturn()
    {
        var lines = Managers.DialogueData?.GetVisitLines(ReturnKey);
        if (lines == null) return;
        foreach (var l in lines)
        {
            if (l == null || string.IsNullOrEmpty(l.text)) continue;
            UI_BossBark.Show(l.text, BossBarkType.MerlinNarration, l.speaker);
        }
    }

    // ── Private Methods ───────────────────────────────────────────

    /// <summary>떠오름 → 플레이어 가슴으로 호를 그리며 날아감 → 스며들며 고리. 공용 이펙트(RunFx)가 없으면 건너뛴다.</summary>
    private static async UniTask LightAsync(Vector3 from, Transform player, Color tint, float intensity, bool burst, CancellationToken ct)
    {
        // 공용 이펙트는 쓰는 쪽이 처음 쓸 때 불러온다(보상 · 게이트 · 놀이방) — 보스 처치 순간엔 아직 없을 수 있다(10-02 1차 실측: 빛 0회).
        if (!RunFx.IsReady)
        {
            try { await RunFx.LoadAsync(); }
            catch (Exception e) { Debug.LogWarning($"[RelicMemory] 공용 이펙트 로드 실패: {e.Message}"); }
            if (!RunFx.IsReady) return;
        }
        ct.ThrowIfCancellationRequested();

        Vector3 start = from + Vector3.up * 0.4f;
        Vector3 top   = from + Vector3.up * RiseHeight;
        if (burst) RunFx.Play(RunFxSlot.Burst, start, RadiantBurst, tint);   // 찬란 — 빛이 떠오르기 전에 쓰러진 자리가 먼저 터진다
        var orb = RunFx.PlayLoop(RunFxSlot.Orb, start, OrbScale * intensity, tint);
        try
        {
            float t = 0f;
            while (t < RiseSeconds)
            {
                t += Time.unscaledDeltaTime;
                if (orb != null) orb.transform.position = Vector3.Lerp(start, top, Mathf.SmoothStep(0f, 1f, t / RiseSeconds));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            t = 0f;
            while (t < FlySeconds && player != null)
            {
                t += Time.unscaledDeltaTime;
                float k   = Mathf.SmoothStep(0f, 1f, t / FlySeconds);
                Vector3 to = player.position + Vector3.up * PlayerChest;   // 플레이어가 움직여도 따라간다
                Vector3 p  = Vector3.Lerp(top, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * ArcHeight);
                if (orb != null) orb.transform.position = p;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
        finally
        {
            RunFx.Stop(ref orb, 0.15f);
        }

        if (player != null)
            RunFx.Play(RunFxSlot.Ring, player.position + Vector3.up * 0.1f, RingScale * intensity, tint);
        await UniTask.Delay(TimeSpan.FromSeconds(AbsorbHold), DelayType.UnscaledDeltaTime, cancellationToken: ct);
    }

    /// <summary>흐릿한 기억 — 앞 절반 어절만 남기고 「…」. 한 어절짜리는 그대로 둔다.</summary>
    private static string FaintLine(string line)
    {
        var words = line.Split(' ');
        int keep = (words.Length + 1) / 2;
        return keep >= words.Length ? line : string.Join(" ", words, 0, keep) + " …";
    }

    /// <summary>유물 회상 줄 중 하나 — 시기 꼬리(봉인기 영광 · 해방기 상실 · 악몽 배신)는 대사 CSV가 고른다.</summary>
    private static string PickRecallLine(RelicClassSO relic)
    {
        var dlg = Managers.DialogueData;
        if (dlg == null || relic == null) return null;
        string key = RecallKeyPrefix + relic.Id.ToString().ToLower();
        var lines = dlg.GetLines(dlg.EraKey(key));
        if (lines == null || lines.Length == 0) return null;
        var pick = lines[UnityEngine.Random.Range(0, lines.Length)];
        return pick?.text;
    }

    /// <summary>회상 한 줄 화면 — 옅게 가라앉은 화면 가운데 흐린 띠 위에 한 줄. 런타임 캔버스(엔딩 카드와 같은 틀).</summary>
    private static async UniTask LineAsync(string text, CancellationToken ct)
    {
        var canvasGO = new GameObject("RelicMemoryLineCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        try
        {
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UISortingOrder.MetaRecall;

            // 프로젝트 캔버스 기준 — 1920×1080, Scale With Screen Size, Match 0.5
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight  = 0.5f;

            var group = canvasGO.GetComponent<CanvasGroup>();
            group.alpha          = 0f;
            group.blocksRaycasts = false;
            group.interactable   = false;

            var dim = MakeImage(canvasGO.transform, "Dim", new Color(0f, 0f, 0f, DimAlpha), null);
            Stretch(dim.rectTransform);

            var band = MakeImage(canvasGO.transform, "Band", new Color(0.02f, 0.02f, 0.04f, BandAlpha), UITheme.SoftBand);
            band.rectTransform.anchorMin = band.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            band.rectTransform.sizeDelta = new Vector2(1700f, 150f);

            var label = MakeText(canvasGO.transform, text);

            await Fade(group, 0f, 1f, LineFadeIn, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(LineHold), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            await Fade(group, 1f, 0f, LineFadeOut, ct);
        }
        finally
        {
            if (canvasGO != null) Object.Destroy(canvasGO);
        }
    }

    private static async UniTask Fade(CanvasGroup group, float a, float b, float seconds, CancellationToken ct)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(a, b, t / seconds);
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        group.alpha = b;
    }

    private static Image MakeImage(Transform parent, string name, Color color, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite        = sprite;
        img.color         = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string text)
    {
        var go = new GameObject("Line", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1500f, 120f);

        var t = go.AddComponent<TextMeshProUGUI>();
        var f = TMP_Settings.defaultFontAsset;
        if (f != null) t.font = f;
        t.text             = text;
        t.fontSize         = LineSize;
        t.alignment        = TextAlignmentOptions.Center;
        t.color            = LineColor;
        t.raycastTarget    = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        TMPOutlineHelper.ApplySoftShadow(t);   // 글자 정본 — 가짜 굵게 · 두꺼운 테두리 금지
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
