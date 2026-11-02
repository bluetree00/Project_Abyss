#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 11-02 Q11 L1 — 런타임 코드의 한글 글을 훑어 문자열 표 초안(Temp/strings_extract.csv: ko, en(빈칸), 파일:줄, 감쌌나)을 만든다.
/// 로그(Debug.Log* · RFLog) · 주석 · 에디터 · 디버그 · 수입 에셋은 뺀다. 같은 글은 한 줄(처음 나온 자리). 번역은 en 열을 채운 뒤 STRINGS 표로.
/// </summary>
public static class StringExtractEditor
{
    private static readonly Regex Literal = new(@"\$?@?""((?:[^""\\\n]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex Hangul  = new(@"[가-힣]", RegexOptions.Compiled);
    private static readonly string[] SkipDirs = { "/Editor/", "/Debug/", "/_Imported/", "/_ThirdParty/" };

    [MenuItem("RelicFairy/Localization/한글 글 추출 → Temp/strings_extract.csv")]
    private static void Extract()
    {
        var seen = new Dictionary<string, string>();
        int files = 0, wrapped = 0;
        foreach (var path in Directory.GetFiles("Assets/RelicFairy", "*.cs", SearchOption.AllDirectories))
        {
            string p = path.Replace('\\', '/');
            bool skip = false;
            foreach (var d in SkipDirs) if (p.Contains(d)) { skip = true; break; }
            if (skip) continue;
            var lines = File.ReadAllLines(path);
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string code = StripComment(lines[i]);
                if (code.Contains("Debug.Log") || code.Contains("RFLog.") || code.TrimStart().StartsWith("[")) continue;
                foreach (Match m in Literal.Matches(code))
                {
                    string s = m.Groups[1].Value;
                    if (!Hangul.IsMatch(s) || seen.ContainsKey(s)) continue;
                    bool isWrapped = code.Substring(0, m.Index).TrimEnd().EndsWith("Loc.T(") || code.Substring(0, m.Index).TrimEnd().EndsWith("Loc.F(");
                    if (isWrapped) wrapped++;
                    seen[s] = $"{p}:{i + 1},{(isWrapped ? "Y" : "")}";
                    any = true;
                }
            }
            if (any) files++;
        }

        var sb = new StringBuilder("ko,en,where,wrapped\n");
        foreach (var kv in seen) sb.Append(Csv(kv.Key)).Append(",,").Append(kv.Value).Append('\n');
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/strings_extract.csv", sb.ToString(), new UTF8Encoding(true));
        Debug.Log($"[Loc] 한글 글 {seen.Count}종 · 파일 {files} · 감싼 것 {wrapped} → Temp/strings_extract.csv");
    }

    /// <summary>줄 주석을 뗀다 — 글 안의 「//」(주소 등)는 남긴다.</summary>
    private static string StripComment(string line)
    {
        bool inStr = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inStr = !inStr;
            if (!inStr && line[i] == '/' && line[i + 1] == '/') return line.Substring(0, i);
        }
        return line;
    }

    private static string Csv(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
#endif
