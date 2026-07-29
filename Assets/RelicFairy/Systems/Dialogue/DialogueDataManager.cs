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
            if (fromServer > 0)
            {
                Debug.Log($"[DialogueDataManager] CDN에서 {_sequences.Count}개 시퀀스 로드 ({fromServer}행)");
                return;
            }

            // ② 폴백 — 프로젝트에 동봉된 CSV(오프라인 · 차트 미등록 시)
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(AddressableKey);
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
    /// 첫 방문(count 0) → <c>{baseKey}_First</c>(없으면 baseKey), 재방문 → <c>{baseKey}_R*</c> 중 랜덤(없으면 First/base).
    /// 예: GetVisitLines("Chapter1_Enter"), GetVisitLines("Lich_Encounter").
    /// </summary>
    public DialogueLine[] GetVisitLines(string baseKey)
    {
        if (string.IsNullOrEmpty(baseKey)) return null;

        string prefsKey = VisitPrefix + baseKey;
        int count = PlayerPrefs.GetInt(prefsKey, 0);
        PlayerPrefs.SetInt(prefsKey, count + 1);
        RememberVisitKey(baseKey); // 새 게임 시 일괄 초기화할 수 있도록 키를 인덱스에 남긴다

        if (count == 0)
            return GetLines(baseKey + "_First") ?? GetLines(baseKey);

        var variants = CollectVariantKeys(baseKey + "_R");
        if (variants.Count > 0)
            return GetLines(variants[UnityEngine.Random.Range(0, variants.Count)]);

        return GetLines(baseKey + "_First") ?? GetLines(baseKey);
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

    // 복귀 대사 티어 임계(내림차순). count가 이 값 이상이면 해당 티어 풀({base}_T{n}_R*)에서 뽑는다.
    private static readonly int[] _tierThresholds = { 25, 10 };

    /// <summary>
    /// 카운트 기반 대사(사망/클리어 복귀 등). 반복 지루함 방지용 랜덤 풀 + 특정 횟수 특별 대사.
    /// 선택 우선순위: 첫 회 <c>{base}_First</c> → 정확 마일스톤 <c>{base}_M{count}</c> →
    /// count가 넘긴 최고 티어 풀 <c>{base}_T{n}_R*</c> 랜덤 → 기본 풀 <c>{base}_R*</c> 랜덤 → base.
    /// (호출측이 count 관리 — RunReturnTracker.)
    /// </summary>
    public DialogueLine[] GetCountLines(string baseKey, int count)
    {
        if (string.IsNullOrEmpty(baseKey)) return null;

        if (count <= 1)
        {
            var first = GetLines(baseKey + "_First");
            if (first != null) return first;
        }

        var exact = GetLines(baseKey + "_M" + count);   // 특정 횟수 딱 그때만 나오는 특별 대사
        if (exact != null) return exact;

        foreach (int t in _tierThresholds)              // count가 넘긴 최고 티어 풀
        {
            if (count < t) continue;
            var tierVariants = CollectVariantKeys(baseKey + "_T" + t + "_R");
            if (tierVariants.Count > 0)
                return GetLines(tierVariants[UnityEngine.Random.Range(0, tierVariants.Count)]);
        }

        var variants = CollectVariantKeys(baseKey + "_R");   // 기본 랜덤 풀
        if (variants.Count > 0)
            return GetLines(variants[UnityEngine.Random.Range(0, variants.Count)]);

        return GetLines(baseKey + "_First") ?? GetLines(baseKey);
    }

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

            if (!_sequences.ContainsKey(seqId))
                _sequences[seqId] = new List<DialogueLine>();

            _sequences[seqId].Add(new DialogueLine
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
