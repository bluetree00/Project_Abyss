#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 10-07 톤 v2 · 런 결과창 자가 검사(편집 모드) — 순수 계산과 에셋 배선을 플레이 없이 확인한다.
/// 결과: Temp/tv2_selfcheck.txt · 로그 「[TV2Check] 끝 PASS n · FAIL m」. FAIL 0이 통과.
/// 태스크마다 아래 Check… 묶음을 하나씩 늘린다.
/// </summary>
public static class TV2SelfCheckEditor
{
    private static StringBuilder s_sb;
    private static int s_pass, s_fail;

    [MenuItem("RelicFairy/Debug/10-07 톤 v2 · 런 결과 자가 검사 (편집 모드)")]
    private static void Run()
    {
        s_sb = new StringBuilder("톤 v2 · 런 결과 자가 검사\n");
        s_pass = s_fail = 0;
        Group(CheckFonts);
        Group(CheckThinKit);
        Group(CheckRunStats);
        Group(CheckPresenter);
        Group(CheckStageFit);
        Group(CheckDamageScope);
        Group(CheckThinArt);
        Group(CheckReviewFixes);
        File.WriteAllText("Temp/tv2_selfcheck.txt", s_sb.ToString());
        Debug.Log($"[TV2Check] 끝 PASS {s_pass} · FAIL {s_fail}\n{s_sb}");
    }

    private static void Check(string name, bool ok)
    {
        if (ok) { s_pass++; s_sb.AppendLine("  PASS " + name); }
        else    Fail(name);
    }

    private static void Fail(string name) { s_fail++; s_sb.AppendLine("  FAIL " + name); }

    /// <summary>묶음 하나를 돌린다 — 던지면 그 묶음만 FAIL 한 줄로 남기고 다음 묶음으로.</summary>
    private static void Group(System.Action check)
    {
        try { check(); }
        catch (System.Exception e) { Fail($"예외({check.Method.Name}): {e.GetType().Name} {e.Message}"); }
    }

    /// <summary>
    /// 코드로 구운 스프라이트는 메모리 절약으로 읽기 금지(Apply(false, true))라 GetPixel이 던진다 —
    /// GPU로 같은 크기 사본을 떠서 읽는다(선형 · 알파 그대로).
    /// </summary>
    private static Texture2D Readable(Texture2D src)
    {
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    // ── A1 글꼴 ──
    private static void CheckFonts()
    {
        s_sb.AppendLine("[A1 글꼴]");
        var so = AssetDatabase.LoadAssetAtPath<UIThemeFontsSO>("Assets/RelicFairy/UI/Base/UIThemeFonts.asset");
        Check("UIThemeFonts.asset 있음", so != null);
        if (so != null)
        {
            foreach (var w in new[] { UIWeight.Light, UIWeight.Regular, UIWeight.Medium })
            {
                var f = so.Get(w);
                Check($"{w} 글꼴 있음", f != null);
                if (f == null) continue;
                Check($"{w} 동적 · 원본 TTF 연결", f.atlasPopulationMode == AtlasPopulationMode.Dynamic && f.sourceFontFile != null);
                Check($"{w} 한글 · 기호 글리프", f.HasCharacters("공격력 쓰러짐 걸린 시간 ·×—…←→●", out _, false, true));
            }
        }
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var entry = settings != null && so != null
            ? settings.FindAssetEntry(AssetDatabase.AssetPathToGUID("Assets/RelicFairy/UI/Base/UIThemeFonts.asset")) : null;
        Check("Addressable 주소 UI/ThemeFonts", entry != null && entry.address == UISkin.ThemeFontsAddress);
        // Review Focus 5 — 글꼴이 아직 안 올라와도(편집 모드엔 UISkin 미로드) 기본 글꼴로 폴백한다.
        Check("UITheme.Font 폴백 non-null", UITheme.Font(UIWeight.Medium) != null);
    }

    // ── A2 얇은 테 부품 ──
    private static void CheckThinKit()
    {
        s_sb.AppendLine("[A2 얇은 테]");
        var o = UIThinShapes.Outline(ThinShape.Square, 56f, 56f);
        Check("윤곽 2배 해상도(112)", o != null && o.texture.width == 112 && o.texture.height == 112);
        Check("윤곽 캐시(같은 크기 = 같은 스프라이트)", ReferenceEquals(o, UIThinShapes.Outline(ThinShape.Square, 56f, 56f)));
        var tex = Readable(o.texture);
        Check("윤곽 위변 가운데 불투명", tex.GetPixel(56, 110).a > 0.6f);
        Check("윤곽 가운데 투명", tex.GetPixel(56, 56).a < 0.01f);
        var d = Readable(UIThinShapes.Outline(ThinShape.Diamond, 64f, 64f).texture);
        Check("마름모 꼭짓점 쪽 불투명 · 모서리 투명", d.GetPixel(64, 125).a > 0.4f && d.GetPixel(2, 2).a < 0.01f);
        var t = Readable(UIThinShapes.Ticks(64f, 64f).texture);
        Check("눈금 모서리 불투명 · 변 가운데 투명", t.GetPixel(1, 6).a > 0.6f && t.GetPixel(64, 1).a < 0.01f);
        var f = Readable(UIThinShapes.Fill(ThinShape.Diamond, 64f, 64f).texture);
        Check("마름모 채움 가운데 불투명", f.GetPixel(64, 64).a > 0.99f);
        Check("EaseOut 0 · 0.5 · 1", Mathf.Approximately(UIMotion.EaseOut(0f), 0f)
                                     && Mathf.Abs(UIMotion.EaseOut(0.5f) - 0.875f) < 1e-4f
                                     && Mathf.Approximately(UIMotion.EaseOut(1f), 1f));
        // Review Focus 4 — 1px 선 최소 1 물리 픽셀
        Check("PixelStroke 720p(배율 0.667) = 1.5", Mathf.Abs(UITheme.Thin.PixelStroke(2f / 3f) - 1.5f) < 1e-3f);
        Check("PixelStroke 4K(배율 2) = 1", Mathf.Approximately(UITheme.Thin.PixelStroke(2f), 1f));
        Check("CanvasScale 1920×1080 = 1", Mathf.Abs(UITheme.Thin.CanvasScale(1920f, 1080f) - 1f) < 1e-4f);
    }

    // ── B1 런 집계 ──
    private static void CheckRunStats()
    {
        s_sb.AppendLine("[B1 런 집계]");
        var s = new RunStats();
        s.RecordDamage(DamageSource.Basic, 100);
        s.RecordDamage(DamageSource.Covenant, 300);
        s.RecordDamage(DamageSource.Dot, 100);
        s.RecordDamage(DamageSource.Skill, 0);           // 0은 세지 않는다
        Check("피해 합계 500", s.TotalDamage == 500);
        Check("서약 비중 0.6", Mathf.Abs(s.ShareOf(DamageSource.Covenant) - 0.6f) < 1e-4f);
        Check("가장 큰 한 방 300 · 서약", s.BiggestHit == 300 && s.BiggestHitSource == DamageSource.Covenant);
        s.ReportFuelGained(FuelKind.EnhanceMaterial, 5);
        s.ReportFuelGained(FuelKind.RuneOre, 3);
        s.ReportFuelGained(FuelKind.RuneOre, -2);        // 쓴 것은 획득이 아니다
        Check("연료 획득 5 · 3", s.EnhanceMaterialGained == 5 && s.RuneOreGained == 3);
        s.ReportChapterEnd(1, 665);
        s.ReportChapterEnd(2, 1545);
        s.ReportHitBy("죽음의 기사");
        s.ReportHitBy("");                               // 빈 이름은 덮어쓰지 않는다
        s.ReportCovenantFire(); s.ReportCovenantFire();
        // Review Focus 1 — 이어하기 왕복 · 구 세이브
        var back = RunStats.FromJson(s.ToJson());
        Check("JSON 왕복 — 피해 · 챕터 · 사인 · 서약", back.TotalDamage == 500 && back.ChapterEndCount == 2
              && back.ChapterEndAt(1) == (2, 1545) && back.LastHitBy == "죽음의 기사" && back.CovenantFires == 2
              && back.EnhanceMaterialGained == 5);
        Check("빈 JSON(구 세이브) = 새 집계", RunStats.FromJson(null).TotalDamage == 0 && RunStats.FromJson("").ChapterEndCount == 0);
        Check("깨진 JSON = 예외 없이 새 집계", RunStats.FromJson("{깨짐").TotalDamage == 0);
        var empty = RunStats.FromJson("{}");
        empty.RecordDamage(DamageSource.Dot, 7);         // 배열 길이가 맞춰져 있어야 던지지 않는다
        Check("{} = 출처 6칸 복구", empty.DamageOf(DamageSource.Dot) == 7);
        var r = new EndRunResult(false, ChapterId.Chapter1, 10, 128, null, "t", contractEssence: 30, firstRunBonus: 12);
        Check("EndRunResult 정산 내역", r.ContractEssence == 30 && r.FirstRunBonus == 12 && r.GainedEssence == 128);
    }

    // ── B3 결과 글자 ──
    private static void CheckPresenter()
    {
        s_sb.AppendLine("[B3 결과 글자]");
        Check("시간 0:00", RunResultPresenter.FormatTime(0) == "0:00");
        Check("시간 0:59", RunResultPresenter.FormatTime(59) == "0:59");
        Check("시간 47:12", RunResultPresenter.FormatTime(2832) == "47:12");
        Check("시간 1:02:05", RunResultPresenter.FormatTime(3725) == "1:02:05");

        var s = new RunResultSnapshot { Cleared = false, ChapterNumber = 3, ChapterName = "성채", RoomClears = 23, KilledBy = "죽음의 기사",
                                        Essence = 128, EssenceFromContracts = 30, ContractsDone = 2, EssenceFromFirstRun = 12 };
        Check("제목 쓰러짐 · 완주", RunResultPresenter.Title(s) == "쓰러짐"
              && RunResultPresenter.Title(new RunResultSnapshot { Cleared = true }) == "완주");
        Check("부제 = Ch3 성채 · 방 23개 정리 · 죽음의 기사에게",
              RunResultPresenter.Subtitle(s) == "Ch3 성채  ·  방 23개 정리  ·  죽음의 기사에게");
        Check("완주 부제엔 「에게」 없음", !RunResultPresenter.Subtitle(new RunResultSnapshot { Cleared = true, ChapterNumber = 4, KilledBy = "리치" }).Contains("에게"));
        var rows = RunResultPresenter.EssenceRows(s);
        Check("정수 내역 3줄(86 · 30 · 12)", rows.Count == 3 && rows[0].value == 86 && rows[1].label == "계약 2건 달성" && rows[2].value == 12);
        Check("정수 내역 — 계약 · 초행 없으면 1줄", RunResultPresenter.EssenceRows(new RunResultSnapshot { Essence = 40 }).Count == 1);

        var segs = new List<RunResultSnapshot.Segment>
        {
            new() { Label = "Ch1", Seconds = 600, Done = true }, new() { Label = "Ch2", Seconds = 300, Done = true },
            new() { Label = "Ch3", Seconds = 100, Done = true }, new() { Label = "Ch4", Seconds = 0, Done = false },
        };
        var w = RunResultPresenter.SegmentWidths(segs, 1000f);
        Check("띠 폭 516 · 258 · 86 · 140", Mathf.Abs(w[0] - 516f) < 0.01f && Mathf.Abs(w[2] - 86f) < 0.01f && Mathf.Abs(w[3] - 140f) < 0.01f);
        Check("띠 폭 합 = 전체", Mathf.Abs(w[0] + w[1] + w[2] + w[3] - 1000f) < 0.01f);
        Check("다 마친 띠 = 전체 폭", Mathf.Abs(RunResultPresenter.DoneWidth(segs.GetRange(0, 3), 1000f) - 1000f) < 0.01f);

        // Review Focus 3 — 빈 사본(유물 · 서약 · 룬 · 무기 · 피해 없음)
        var empty = new RunResultSnapshot();
        bool ok = true;
        try
        {
            ok &= RunResultPresenter.WeaponTitle(null) == "무기 없음";
            ok &= RunResultPresenter.MemoryLine(empty) == "되살아난 기억 없음";
            ok &= RunResultPresenter.Subtitle(empty).Length > 0;
            ok &= RunResultPresenter.CombatLine(empty).Length > 0;
            ok &= RunResultPresenter.EssenceRows(empty).Count == 1;
            ok &= RunResultPresenter.SegmentWidths(empty.Segments, 1000f).Length == 0;
        }
        catch (System.Exception e) { ok = false; s_sb.AppendLine("    " + e.Message); }
        Check("빈 사본 — 예외 없이 「없음」", ok);
        Check("긴 무기 이름 그대로 넘김(줄임은 화면이)", RunResultPresenter.WeaponTitle(new RunResultSnapshot.WeaponLine { Name = new string('가', 20) }).Length == 20);
    }

    // ── B4 결과창 무대 ──
    private static void CheckStageFit()
    {
        s_sb.AppendLine("[B4 결과창 무대]");
        Check("1920×1080 = 1", Mathf.Abs(RunResultView.StageFit(1920f, 1080f) - 1f) < 1e-3f);
        // 울트라와이드(Match 0.5) — 캔버스가 2217×935라 세로가 모자라 줄어드는 게 맞다. 무대가 캔버스 안에 다 들어가는지만 본다.
        float fUw = RunResultView.StageFit(2560f, 1080f);
        float kUw = UITheme.Thin.CanvasScale(2560f, 1080f);
        Check($"2560×1080(울트라와이드) 안에 들어감 ({fUw:0.000})", 1080f * fUw <= 1080f / kUw + 0.5f && 1920f * fUw <= 2560f / kUw + 0.5f);
        float f43 = RunResultView.StageFit(1440f, 1080f);
        Check($"1440×1080(4:3) < 1 · 잘리지 않음 ({f43:0.000})", f43 < 1f && 1920f * f43 <= 1440f / UITheme.Thin.CanvasScale(1440f, 1080f) + 0.5f);
    }

    // ── C1 피해 출처 ──
    private static void CheckDamageScope()
    {
        s_sb.AppendLine("[C1 피해 출처]");
        Check("스코프 없음 · 주 피해 = 평타", DamageSourceScope.Resolve(DamageKind.Normal) == DamageSource.Basic);
        Check("스코프 없음 · 2차 = 룬", DamageSourceScope.Resolve(DamageKind.Synergy) == DamageSource.Rune);
        using (DamageSourceScope.Enter(DamageSource.Skill))
        {
            Check("스킬 안 · 주 피해 = 스킬", DamageSourceScope.Resolve(DamageKind.Normal) == DamageSource.Skill);
            Check("스킬 안 · 2차(룬 발동) = 룬", DamageSourceScope.Resolve(DamageKind.Synergy) == DamageSource.Rune);
            Check("스킬 안 · 지속 = 지속", DamageSourceScope.Resolve(DamageKind.Dot) == DamageSource.Dot);
            using (DamageSourceScope.Enter(DamageSource.Covenant))
                Check("서약 안 · 2차 = 서약", DamageSourceScope.Resolve(DamageKind.Synergy) == DamageSource.Covenant);
            Check("서약에서 나오면 스킬로 되돌림", DamageSourceScope.Current == DamageSource.Skill);
        }
        Check("다 나오면 비어 있음", DamageSourceScope.Current == null);
        Check("Q = 유물 Q · E = 스킬 · 평타 = 평타",
              DamageSourceScope.FromAction(WeaponActionType.QSkill) == DamageSource.RelicQ
              && DamageSourceScope.FromAction(WeaponActionType.RSkill) == DamageSource.Skill
              && DamageSourceScope.FromAction(WeaponActionType.GroundLight) == DamageSource.Basic);
    }

    // ── 최종 리뷰 수정(10-08) ──
    private static void CheckReviewFixes()
    {
        s_sb.AppendLine("[리뷰 수정]");
        foreach (var (w, h) in new[] { (1440f, 1080f), (1280f, 800f), (1280f, 720f), (2560f, 1080f) })
        {
            float physical = RunResultView.LineStroke(w, h) * UITheme.Thin.CanvasScale(w, h) * RunResultView.StageFit(w, h);
            Check($"M1 결과창 선 ≥ 1 물리 px — {w}×{h} ({physical:0.00})", physical >= 0.999f);
        }
        Check("M2 좁은 구간(60) 이름 생략 · 넓은 구간(120) 이름", !RunResultPresenter.SegmentShowsLabel(60f) && RunResultPresenter.SegmentShowsLabel(120f));
        Check("M2 시간 글자 — 보통 80 생략 · 120 표시 · 마지막 200 생략 · 300 표시",
              !RunResultPresenter.SegmentShowsTime(80f, false) && RunResultPresenter.SegmentShowsTime(120f, false)
              && !RunResultPresenter.SegmentShowsTime(200f, true) && RunResultPresenter.SegmentShowsTime(300f, true));
    }

    // ── D5 납품 테 얇게(10-08 사용자 「테두리도 조금은 사용하되 얇게」) ──
    private static void CheckThinArt()
    {
        s_sb.AppendLine("[D5 납품 테 얇게]");
        // 16×16 · x 4~11 불투명 막대(폭 8) — 3×3 최소 침식이면 x 5~10만 남는다(폭 6).
        var bar = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        var cols = new Color32[16 * 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                cols[y * 16 + x] = x >= 4 && x <= 11 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        bar.SetPixels32(cols);
        bar.Apply();
        var src = Sprite.Create(bar, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f), 100f);
        var thin = UISpriteMaster.Thinned(src);
        Check("얇게 깎은 사본이 생김(원본과 다름)", thin != null && thin != src);
        var t = Readable(thin.texture);
        Check("막대 바깥 끝(x=4) 깎임", t.GetPixel(4, 8).a < 0.05f);
        Check("막대 안쪽 끝(x=5) 남음", t.GetPixel(5, 8).a > 0.95f);
        Check("막대 가운데(x=8) 남음 · 무채색 흰", t.GetPixel(8, 8).a > 0.95f && t.GetPixel(8, 8).r > 0.95f);
        Check("같은 원본 = 같은 사본(캐시)", ReferenceEquals(thin, UISpriteMaster.Thinned(src)));
        Check("null = null", UISpriteMaster.Thinned(null) == null);
        // 유물 컨셉 색은 남긴다(Q 유물칸) — keepColor면 침식만, 색은 그대로.
        var red = new Texture2D(16, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        var rc = new Color32[16 * 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                rc[y * 16 + x] = x >= 4 && x <= 11 ? new Color32(230, 60, 30, 255) : new Color32(0, 0, 0, 0);
        red.SetPixels32(rc);
        red.Apply();
        var redSrc = Sprite.Create(red, new Rect(0f, 0f, 16f, 16f), new Vector2(0.5f, 0.5f), 100f);
        var keep = Readable(UISpriteMaster.Thinned(redSrc, keepColor: true).texture);
        var c = keep.GetPixel(8, 8);
        // Readable()은 선형으로 떠 sRGB 230 → 0.79 · 60 → 0.05 — 「붉은 쪽 그대로」만 본다.
        Check($"keepColor — 색 그대로(붉은 주황) · 끝은 깎임 (r {c.r:0.00} g {c.g:0.00})", c.r > 0.6f && c.g < 0.2f && keep.GetPixel(4, 8).a < 0.05f);
        Check("keepColor 사본 ≠ 무채색 사본", UISpriteMaster.Thinned(redSrc, keepColor: true) != UISpriteMaster.Thinned(redSrc));
    }
}
#endif
