using System.Collections.Generic;

/// <summary>
/// 이야기 비트 대사 — <b>한 번씩만</b> 트는 장면(거점 복귀: 엔딩 에필로그 · 붕괴 · 봉인/처치 첫 기록 등 / 전투: 리치 첫 조우).
/// 조건은 <see cref="StoryProgress"/> 기록을 보고, 튼 대사는 <c>seen.*</c> 기록으로 남겨 다시 틀지 않는다.
/// 대사가 CSV에 없으면 기록하지 않고 건너뛴다(대사 추가 전에 조건이 먼저 충족돼도 나중에 나온다).
///
/// 우선순위(설계 v2 §4): 엔딩 에필로그(+ 악몽 모드 해금) → 악몽 시작 → 봉인·처치 첫 기록(한 번에 모아서) → 성소의 문.
/// </summary>
public static class StoryDialogue
{
    public const string EpilogueFirst  = "Epilogue_First";
    public const string NightmareModeUnlock = "NightmareMode_Unlock";   // 엔딩 뒤 첫 귀환 — 그림자가 게이트 곁 갈림길을 알린다(v3 D3)
    public const string NightmareBegin = "Nightmare_Begin";
    public const string MissionSeal    = "Mission_Seal";
    public const string Chapter4Door   = "Story_Chapter4Door";
    public const string LichIntroSealed    = "Lich_Intro_Sealed";
    public const string LichIntroNightmare = "Lich_Intro_Nightmare";

    /// <summary>이번 복귀에 틀 이야기 비트. 없으면 null — 호출부는 평소 복귀 대사로 간다.</summary>
    public static DialogueLine[] TakeReturnBeat(DialogueDataManager dlg)
    {
        if (dlg == null) return null;

        if (StoryProgress.HasEnded)
        {
            // 에필로그 바로 뒤에 해금 대사를 한 창으로 잇는다(그 뒤 게이트 곁 갈림길이 드러난다 — BaseCampFxDirector).
            // 에필로그를 이미 본 세이브라도 해금 대사를 아직 못 봤으면 그것만 튼다.
            var buf0 = new List<DialogueLine>();
            Append(buf0, TakeOnce(dlg, EpilogueFirst));
            Append(buf0, TakeOnce(dlg, NightmareModeUnlock));
            if (buf0.Count > 0) return buf0.ToArray();
        }

        if (StoryProgress.IsNightmare)
        {
            var begin = TakeOnce(dlg, NightmareBegin);
            if (begin != null) return begin;
        }

        // 봉인(봉인기)·처치(악몽기) 첫 기록 — 한 런에 여러 개가 쌓일 수 있어 한 창에 이어 붙인다.
        // 붕괴 뒤에는 봉인 대사를 틀지 않는다(이미 무너진 봉인을 기뻐하는 대사가 된다).
        var buf = new List<DialogueLine>();
        for (int n = 1; n <= 3; n++)
        {
            string boss = StoryProgress.BossIdForChapter((ChapterId)n);
            if (!StoryProgress.IsNightmare && StoryProgress.IsSealed(boss)) Append(buf, TakeOnce(dlg, $"Seal_Ch{n}_First"));
            if (StoryProgress.IsKilled(boss))                                 Append(buf, TakeOnce(dlg, $"Kill_Ch{n}_First"));
        }

        // 세 보스를 모두 봉인했다 → 성소의 문(제단의 「챕터 4」)을 알린다.
        if (!StoryProgress.IsNightmare && StoryProgress.IsSealed(StoryProgress.DeathKnight))
            Append(buf, TakeOnce(dlg, Chapter4Door));

        return buf.Count > 0 ? buf.ToArray() : null;
    }

    /// <summary>봉인기 사명 — 거점 첫 방문 대사 뒤에 한 번 붙인다.</summary>
    public static DialogueLine[] TakeMission(DialogueDataManager dlg)
        => dlg == null || StoryProgress.IsNightmare ? null : TakeOnce(dlg, MissionSeal);

    /// <summary>리치 첫 조우 대사 — 봉인된 리치·해방된 리치 각각 한 번. 이미 봤으면 null.</summary>
    public static DialogueLine[] TakeLichIntro(DialogueDataManager dlg, bool nightmare)
        => dlg == null ? null : TakeOnce(dlg, nightmare ? LichIntroNightmare : LichIntroSealed);

    private static DialogueLine[] TakeOnce(DialogueDataManager dlg, string key)
    {
        if (StoryProgress.IsSeen(key)) return null;
        var lines = dlg.GetLines(key);
        if (lines == null || lines.Length == 0) return null;
        StoryProgress.MarkSeen(key);
        return lines;
    }

    private static void Append(List<DialogueLine> buf, DialogueLine[] lines)
    {
        if (lines != null) buf.AddRange(lines);
    }
}
