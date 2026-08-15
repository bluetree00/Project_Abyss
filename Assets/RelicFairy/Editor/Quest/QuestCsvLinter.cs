using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <c>QuestDefinition.csv</c>의 빌드 전 품질 게이트.
///
/// <para><b>왜 필요한가</b> — <c>boss_first</c> 업적은 오랫동안 진척이 0이었다.
/// <c>category=Boss</c>인데 <c>QuestEvents.Report("Boss", …)</c>를 부르는 코드가 프로젝트에 없었기 때문이다.
/// 컴파일도 통과하고 에셋도 정상 생성되므로 <b>플레이해보기 전엔 아무도 모른다.</b>
/// 이 린터는 그 종류의 결함을 정적으로 잡는다.</para>
///
/// <para>업적을 CDN으로 옮겨도 이 결함은 그대로 생긴다 — 서버가 보내는 카테고리를
/// 클라이언트가 세지 않으면 죽은 업적이다. 그래서 전달 방식과 무관하게 필요한 검사다.</para>
///
/// 에디터 전용. 런타임/세이브에 영향 없음.
/// </summary>
public static class QuestCsvLinter
{
    public enum Severity { Error, Warning }

    public struct Finding
    {
        public Severity severity;
        public int      line;
        public string   codeName;
        public string   message;
    }

    private const string CsvPath = "Assets/RelicFairy/Data/Quest/QuestDefinition.csv";

    // CSV 컬럼 인덱스 (QuestSOGenerator와 동일 규약)
    private const int ColType = 0, ColCode = 1, ColName = 2, ColDesc = 3, ColCategory = 4,
                      ColTarget = 5, ColAction = 6, ColNeed = 7, ColRewardType = 8, ColRewardAmt = 9,
                      ColAutoComplete = 10;
    private const int MinColumns = 13;

    private static readonly HashSet<string> KnownActions = new()
    { "SimpleCount", "SimpleSet", "PositiveCount", "NegativeCount", "ContinuosCount" };

    /// <summary>생성기가 실제로 만들 수 있는 보상 타입(<c>QuestSOGenerator.CreateReward</c>).</summary>
    private static readonly HashSet<string> KnownRewards = new()
    { "Gold", "Essence", "None" };

    // ── 메뉴 ────────────────────────────────────────────────────────────────

    [MenuItem("Tools/RelicFairy/Validate Quest CSV")]
    public static void ValidateMenu()
    {
        var findings = Lint(out int rows);

        int errors   = findings.Count(f => f.severity == Severity.Error);
        int warnings = findings.Count - errors;

        var sb = new StringBuilder();
        sb.AppendLine($"[QuestCsvLinter] {rows}행 검사 — 오류 {errors} / 경고 {warnings}");
        foreach (var f in findings)
            sb.AppendLine($"  {(f.severity == Severity.Error ? "ERR " : "WARN")} L{f.line} [{f.codeName}] {f.message}");

        if (errors > 0)      Debug.LogError(sb.ToString());
        else if (warnings > 0) Debug.LogWarning(sb.ToString());
        else                 Debug.Log(sb + "  결함 없음.");
    }

    // ── 검사 ────────────────────────────────────────────────────────────────

    public static List<Finding> Lint(out int rowCount)
    {
        var findings = new List<Finding>();
        rowCount = 0;

        if (!File.Exists(CsvPath))
        {
            findings.Add(new Finding { severity = Severity.Error, line = 0, message = $"CSV 없음: {CsvPath}" });
            return findings;
        }

        var reported = ScanReportedCategories();
        var recordKeys = new HashSet<string>(MemoryAltarCatalog.Rec.All);
        var seenCodes  = new HashSet<string>();

        var lines = File.ReadAllLines(CsvPath);
        for (int i = 1; i < lines.Length; i++)   // 0행은 헤더
        {
            string raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var c = raw.Split(',');
            int line = i + 1;
            rowCount++;

            if (c.Length < MinColumns)
            {
                Add(findings, Severity.Error, line, "?", $"컬럼 {c.Length}개 — 최소 {MinColumns}개 필요");
                continue;
            }

            string type     = c[ColType].Trim();
            string code     = c[ColCode].Trim();
            string category = c[ColCategory].Trim();
            string target   = c[ColTarget].Trim();
            string action   = c[ColAction].Trim();
            string rewardT  = c[ColRewardType].Trim();
            string autoComp = c[ColAutoComplete].Trim();

            bool isAchievement = type == "achievement";

            if (type != "quest" && !isAchievement)
                Add(findings, Severity.Error, line, code, $"type '{type}' — quest | achievement 만 허용");

            if (string.IsNullOrEmpty(code))
                Add(findings, Severity.Error, line, "?", "codeName 비어 있음");
            else if (!seenCodes.Add(code))
                Add(findings, Severity.Error, line, code, "codeName 중복 — 뒤 행이 앞 행을 덮어쓴다");

            if (!int.TryParse(c[ColNeed], out int need) || need <= 0)
                Add(findings, Severity.Error, line, code, $"needCount '{c[ColNeed]}' — 1 이상이어야 한다(0이면 즉시 완료/영원히 미완)");

            // ── 액션 ──
            if (!KnownActions.Contains(action))
                Add(findings, Severity.Error, line, code, $"action '{action}' — 알 수 없음");

            // ── 카테고리: 이것이 boss_first를 죽였던 지점 ──
            if (category == QuestEvents.RecordCategory)
            {
                if (!recordKeys.Contains(target))
                    Add(findings, Severity.Error, line, code,
                        $"target '{target}' — 기록 키가 아니다. 진척이 영원히 0이 된다 (MemoryAltarCatalog.Rec)");

                if (action != "SimpleSet")
                    Add(findings, Severity.Error, line, code,
                        $"Record 카테고리인데 action='{action}' — 기록은 <b>절대값</b>이라 SimpleSet이어야 한다. " +
                        "SimpleCount면 매 런 누적돼 폭주한다");
            }
            else if (!reported.Contains(category))
            {
                Add(findings, Severity.Error, line, code,
                    $"category '{category}' — 이 카테고리를 보고하는 코드가 없다. 진척이 영원히 0이 된다 " +
                    $"(실제 보고: {string.Join(", ", reported.OrderBy(x => x))})");
            }

            // ── 보상 ──
            if (!KnownRewards.Contains(rewardT))
                Add(findings, Severity.Error, line, code, $"rewardType '{rewardT}' — 생성기가 만들 수 없다");

            if (isAchievement && rewardT == "Gold")
                Add(findings, Severity.Error, line, code,
                    "업적 보상이 Gold — 골드는 <b>런 재화</b>라 거점 수령 시 Run이 없어 증발한다. Essence를 쓸 것");

            if (isAchievement && autoComp.ToLowerInvariant() == "true")
                Add(findings, Severity.Warning, line, code,
                    "업적인데 autoComplete=true — 수령형(제단에서 받기) 설계와 어긋난다");
        }

        return findings;
    }

    /// <summary>
    /// 코드베이스에서 <b>실제로 보고되는</b> 카테고리를 긁어온다.
    /// <c>QuestEvents.Report("X", …)</c> 직접 호출과 래퍼 메서드 양쪽을 본다.
    /// </summary>
    private static HashSet<string> ScanReportedCategories()
    {
        var found = new HashSet<string>();

        // 래퍼가 고정으로 쓰는 카테고리 (QuestEvents 정의와 함께 유지)
        found.Add("Kill");
        found.Add("Room");
        found.Add("Item");
        found.Add("Gold");
        found.Add(QuestEvents.RecordCategory);

        var rx = new Regex(@"Report\(\s*""([A-Za-z_]+)""", RegexOptions.Compiled);
        string root = Path.Combine(Application.dataPath, "RelicFairy");

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // 린터 자신과 QuestEvents 정의부는 제외 — 정의만 있고 호출은 아니다.
            if (file.EndsWith("QuestCsvLinter.cs")) continue;

            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            if (!text.Contains("Report(")) continue;
            foreach (Match m in rx.Matches(text))
                found.Add(m.Groups[1].Value);
        }
        return found;
    }

    private static void Add(List<Finding> list, Severity sev, int line, string code, string msg)
        => list.Add(new Finding { severity = sev, line = line, codeName = code, message = msg });
}
