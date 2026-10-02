using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LitJson;
using UnityEngine;

/// <summary>
/// 대화 데이터 로드 — <b>서버(뒤끝 CDN) 우선, 프로젝트 CSV 폴백</b>.
///
/// 서버를 먼저 보는 이유는 <b>라이브 대사 수정</b> 때문이다. 차트만 갈아끼우면
/// 클라이언트 재빌드 없이 대사가 바뀐다. 차트가 없거나 오프라인이면 프로젝트에
/// 동봉된 Addressable TextAsset으로 폴백하므로, 서버 미등록 상태에서도 그대로 동작한다.
///
/// 두 소스의 컬럼은 동일하다:
///   sequence_id, line_order, speaker, illustration_key, text
///   StartRoom, 0, God, Illust_God_Default, "잠에서 깨어나라..."
/// </summary>
public class DialogueDataManager
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    private const string AddressableKey = "DIALOGUE_DATA";
    private const string ChartName      = "DIALOGUE_DATA";

    // ─────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────

    public bool IsInitialized { get; private set; }

    // ─────────────────────────────────────────────────────────
    // Private
    // ─────────────────────────────────────────────────────────

    private readonly Dictionary<string, List<DialogueLine>> _sequences = new();

    // ─────────────────────────────────────────────────────────
    // Public Methods
    // ─────────────────────────────────────────────────────────

    public async UniTask InitializeAsync()
    {
        try
        {
            // ① 서버(CDN) 우선 — 라이브 대사 수정이 재빌드 없이 반영된다.
            int fromServer = LoadFromServer();

            // ② 프로젝트에 동봉된 CSV — 서버가 없으면 전부, 있으면 <b>서버에 없는 시퀀스만</b> 보충한다.
            //    새 대사(이야기 비트 등)를 CSV에 추가하면 차트 재업로드 전에도 동작하고,
            //    서버에 있는 시퀀스는 서버 것이 이긴다(라이브 수정 우선 유지).
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(AddressableKey);
            if (fromServer > 0)
            {
                int added = textAsset != null ? MergeMissingFromCsv(textAsset.text) : 0;
                Debug.Log($"[DialogueDataManager] CDN에서 {_sequences.Count - added}개 시퀀스 로드 ({fromServer}행)" +
                          (added > 0 ? $" + 로컬 CSV 보충 {added}개" : ""));
                return;
            }

            if (textAsset != null)
            {
                ParseCsv(textAsset.text);
                Debug.Log($"[DialogueDataManager] 로컬 CSV에서 {_sequences.Count}개 시퀀스 로드 완료");
            }
            else
            {
                Debug.LogWarning("[DialogueDataManager] DIALOGUE_DATA Addressable 없음 — SO 폴백 사용");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DialogueDataManager] 로드 실패: {e.Message}");
        }
        finally
        {
            IsInitialized = true;
        }
    }

    /// <summary>sequenceId로 대화 라인 배열을 가져온다. 없으면 null 반환.</summary>
    public DialogueLine[] GetLines(string sequenceId)
    {
        if (_sequences.TryGetValue(sequenceId, out var list))
            return list.ToArray();
        return null;
    }

    public bool HasSequence(string sequenceId) => _sequences.ContainsKey(sequenceId);

    /// <summary>
    /// 방문 횟수 기반 변형 대사 선택(로그라이크 반복 재미). 호출 시 방문 횟수를 1 증가(PlayerPrefs 영속).
    /// 시기판 키(<see cref="EraKey"/>)가 있으면 그 키로 고르고 횟수도 그 키로 센다 — 시기가 바뀌면 그 시기의 첫 대사부터.
    /// 첫 방문(count 0) → <c>{key}_First</c>(없으면 key), 재방문 → 횟수 단계 풀(<see cref="PickRepeat"/>).
    /// 예: GetVisitLines("Chapter1_Enter"), GetVisitLines("Lich_Encounter").
    /// </summary>
    public DialogueLine[] GetVisitLines(string baseKey)
    {
        if (string.IsNullOrEmpty(baseKey)) return null;

        string key = EraKey(baseKey);
        string prefsKey = VisitPrefix + key;
        int count = PlayerPrefs.GetInt(prefsKey, 0);
        PlayerPrefs.SetInt(prefsKey, count + 1);
        RememberVisitKey(key); // 새 게임 시 일괄 초기화할 수 있도록 키를 인덱스에 남긴다

        if (count == 0)
        {
            var first = GetLines(key + "_First") ?? GetLines(key);
            if (first != null) return first;
        }
        return PickRepeat(key, count) ?? GetLines(key + "_First") ?? GetLines(key);
    }

    /// <summary>
    /// 시기판 키 — 같은 자리라도 시기마다 맞는 대사가 다르다(봉인기 → 해방기 → 엔딩 뒤 → 악몽 모드).
    /// <c>{baseKey}{꼬리}</c>에 대사가 하나라도 있으면 그 키를, 없으면 다음 꼬리 → 평소 키. 꼬리를 안 쓴 자리는 그대로다.
    /// </summary>
    public string EraKey(string baseKey)
    {
        foreach (var suffix in EraSuffixes())
        {
            string k = baseKey + suffix;
            if (HasSequence(k) || HasSequence(k + "_First") || HasSequence(k + "_R1") || HasSequence(k + "_T10_R1"))
                return k;
        }
        return baseKey;
    }

    /// <summary>
    /// 보스 <b>조우</b> 대사 전용 조회. 인트로(프롤로그) 씬에서는 <b>항상 null</b>을 준다.
    ///
    /// 조우 대사 키는 보스 클래스명에서 자동 유도되는데(<c>DeathKnightMonster → DeathKnight_Encounter</c>),
    /// 인트로는 실제 챕터 보스 프리팹을 '타락한 모르드레드' 대역으로 쓴다. 그래서 프롤로그 한복판에
    /// 그 보스의 챕터 조우 대사가 끼어들어, 인트로 전용 대사와 뒤섞였다.
    /// 인트로의 대사는 IntroMordredDirector가 전부 소유하므로 여기서는 내보내지 않는다.
    ///
    /// 방문 횟수도 올리지 않는다 — 인트로에서 소비되면 실제 첫 조우가 '재방문'이 되어버린다.
    /// </summary>
    public DialogueLine[] GetBossEncounterLines(string baseKey)
        => IntroBootstrapper.Instance != null ? null : GetVisitLines(baseKey);

    // ── 방문 횟수 영속 관리 ────────────────────────────────────────────
    private const string VisitPrefix   = "dlgVisit_";
    private const string VisitIndexKey = "dlgVisit_index";

    /// <summary>방문 키를 인덱스(쉼표 구분)에 누적 기록. ResetVisitCounts가 이걸로 전부 지운다.</summary>
    private static void RememberVisitKey(string baseKey)
    {
        string index = PlayerPrefs.GetString(VisitIndexKey, string.Empty);
        if (index.Length == 0)
        {
            PlayerPrefs.SetString(VisitIndexKey, baseKey);
            return;
        }
        // 이미 기록된 키면 스킵(중복 누적 방지)
        foreach (var k in index.Split(','))
            if (k == baseKey) return;

        PlayerPrefs.SetString(VisitIndexKey, index + "," + baseKey);
    }

    /// <summary>새 게임 시작 — 모든 방문 횟수를 초기화한다.
    /// 이게 없으면 이전 플레이의 카운트가 남아 첫 진입부터 재방문(_R*) 대사가 나온다.</summary>
    public static void ResetVisitCounts()
    {
        string index = PlayerPrefs.GetString(VisitIndexKey, string.Empty);
        if (index.Length > 0)
            foreach (var k in index.Split(','))
                if (!string.IsNullOrEmpty(k)) PlayerPrefs.DeleteKey(VisitPrefix + k);

        PlayerPrefs.DeleteKey(VisitIndexKey);
        PlayerPrefs.Save();
    }

    // 반복 대사 단계(방문 · 횟수 공용, 내림차순) — 그 횟수 이상이면 {key}_T{n}_R* 풀.
    // 사용자(10-01): 「초회 · 10~20회 · 30~40회 · 그 뒤 무한 뒷부분 랜덤」. 50부터는 끝없는 구간이라 뒤쪽 풀을 모두 섞는다.
    private static readonly int[] _tierThresholds = { 50, 30, 10 };
    private const int EndlessFrom = 50;

    private static readonly string[] SuffixNightmare = { "_Nightmare", "_Ended", "_Liberation" };
    private static readonly string[] SuffixEnded     = { "_Ended", "_Liberation" };
    private static readonly string[] SuffixLiberated = { "_Liberation" };

    /// <summary>
    /// 카운트 기반 대사(사망/클리어 복귀 등). 반복 지루함 방지용 랜덤 풀 + 특정 횟수 특별 대사. 시기판 키가 있으면 그 키로.
    /// 선택 우선순위: 첫 회 <c>{key}_First</c> → 정확 마일스톤 <c>{key}_M{count}</c>(엔딩 전엔 평소 키의 마일스톤도) →
    /// 횟수 단계 풀(<see cref="PickRepeat"/>) → key.
    /// (호출측이 count 관리 — RunReturnTracker.)
    /// </summary>
    public DialogueLine[] GetCountLines(string baseKey, int count)
    {
        if (string.IsNullOrEmpty(baseKey)) return null;
        string key = EraKey(baseKey);

        if (count <= 1)
        {
            var first = GetLines(key + "_First");
            if (first != null) return first;
        }

        // 특정 횟수 딱 그때만 나오는 특별 대사. 평소 키의 마일스톤은 설계를 흘리는 떡밥이라 정체가 드러난(엔딩) 뒤엔 낡는다.
        var exact = GetLines(key + "_M" + count)
                 ?? (key != baseKey && !StoryProgress.HasEnded ? GetLines(baseKey + "_M" + count) : null);
        if (exact != null) return exact;

        return PickRepeat(key, count) ?? GetLines(key + "_First") ?? GetLines(key);
    }

    /// <summary>
    /// 반복 대사 하나 — count(지난 횟수)가 넘긴 가장 높은 단계 풀(<c>{key}_T{n}_R*</c>)에서, 없으면 아래 단계 → 기본 풀 <c>{key}_R*</c>.
    /// 끝없는 구간(50~)은 뒤쪽 단계 풀(T10 · T30 · T50)을 모두 섞어 고른다. 풀 대사에는 횟수를 적지 않는다(마일스톤 몫).
    /// </summary>
    private DialogueLine[] PickRepeat(string key, int count)
    {
        List<string> pool = null;
        if (count >= EndlessFrom)
        {
            pool = new List<string>();
            foreach (int t in _tierThresholds) pool.AddRange(CollectVariantKeys(key + "_T" + t + "_R"));
        }
        else
        {
            foreach (int t in _tierThresholds)
            {
                if (count < t) continue;
                var tier = CollectVariantKeys(key + "_T" + t + "_R");
                if (tier.Count > 0) { pool = tier; break; }
            }
        }
        if (pool == null || pool.Count == 0) pool = CollectVariantKeys(key + "_R");
        return pool.Count > 0 ? GetLines(pool[UnityEngine.Random.Range(0, pool.Count)]) : null;
    }

    private static string[] EraSuffixes() =>
        StoryProgress.IsNightmareMode ? SuffixNightmare
      : StoryProgress.IsLiberated     ? (StoryProgress.HasEnded ? SuffixEnded : SuffixLiberated)
      : Array.Empty<string>();

    private List<string> CollectVariantKeys(string prefix)
    {
        var list = new List<string>();
        foreach (var key in _sequences.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                list.Add(key);
        return list;
    }

    // ─────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 뒤끝 CDN 차트에서 대사를 읽는다. 로드된 행 수를 반환하고, 실패·미등록이면 0.
    ///
    /// CSV 파일과 달리 <b>행 순서가 보장되지 않으므로</b> line_order로 직접 정렬한다.
    /// 실패해도 예외를 밖으로 내보내지 않는다 — 폴백이 이어받아야 하기 때문.
    /// </summary>
    private int LoadFromServer()
    {
        var buffer = new List<(string seq, int order, int idx, DialogueLine line)>();
        int rows;

        try
        {
            rows = ChartLoader.Load(ChartName, row =>
            {
                string seqId = row.TryGetString("sequence_id");
                if (string.IsNullOrEmpty(seqId)) return;

                if (!Enum.TryParse<DialogueSpeaker>(row.TryGetString("speaker"), ignoreCase: true, out var spk))
                    spk = DialogueSpeaker.None;

                buffer.Add((seqId, row.TryGetInt("line_order"), buffer.Count, new DialogueLine
                {
                    speaker         = spk,
                    illustrationKey = row.TryGetString("illustration_key"),
                    text            = row.TryGetString("text"),
                }));
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DialogueDataManager] CDN 로드 실패 — 로컬 CSV로 폴백: {e.Message}");
            return 0;
        }

        if (rows <= 0 || buffer.Count == 0) return 0;

        // line_order 우선, 동률(또는 미기입 0)이면 도착 순으로 안정 정렬.
        buffer.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.idx.CompareTo(b.idx));

        _sequences.Clear();
        foreach (var (seq, _, _, line) in buffer)
        {
            if (!_sequences.TryGetValue(seq, out var list))
                _sequences[seq] = list = new List<DialogueLine>();
            list.Add(line);
        }
        return buffer.Count;
    }

    private void ParseCsv(string csv)
    {
        _sequences.Clear();
        ParseCsvInto(csv, _sequences);
    }

    /// <summary>로컬 CSV에서 <see cref="_sequences"/>에 없는 시퀀스만 더한다. 더한 시퀀스 수를 반환.</summary>
    private int MergeMissingFromCsv(string csv)
    {
        var local = new Dictionary<string, List<DialogueLine>>();
        ParseCsvInto(csv, local);

        int added = 0;
        foreach (var kv in local)
        {
            if (_sequences.ContainsKey(kv.Key)) continue;
            _sequences[kv.Key] = kv.Value;
            added++;
        }
        return added;
    }

    private static void ParseCsvInto(string csv, Dictionary<string, List<DialogueLine>> target)
    {
        var lines = csv.Split('\n');
        for (int i = 1; i < lines.Length; i++) // i=0 헤더 스킵
        {
            var row = lines[i].Trim();
            if (string.IsNullOrEmpty(row)) continue;

            var cols = SplitCsvRow(row);
            if (cols.Length < 5) continue;

            string seqId           = cols[0].Trim();
            string speaker         = cols[2].Trim();
            string illustrationKey = cols[3].Trim();
            string text            = cols[4].Trim();

            if (!Enum.TryParse<DialogueSpeaker>(speaker, ignoreCase: true, out var spkEnum))
                spkEnum = DialogueSpeaker.None;

            if (!target.ContainsKey(seqId))
                target[seqId] = new List<DialogueLine>();

            target[seqId].Add(new DialogueLine
            {
                speaker         = spkEnum,
                illustrationKey = illustrationKey,
                text            = text,
            });
        }
    }

    /// <summary>따옴표 내부 쉼표 및 "" 이스케이프를 처리하는 CSV 열 분리.</summary>
    private static string[] SplitCsvRow(string row)
    {
        var result = new List<string>();
        var field = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < row.Length; i++)
        {
            char c = row[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // "" → 리터럴 " (이스케이프), 아니면 따옴표 닫힘
                    if (i + 1 < row.Length && row[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    result.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
        }

        result.Add(field.ToString());
        return result.ToArray();
    }
}
