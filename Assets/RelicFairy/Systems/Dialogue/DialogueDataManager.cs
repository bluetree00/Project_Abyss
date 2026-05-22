using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// CDN(Addressables) 또는 로컬 TextAsset에서 대화 CSV를 로드해 시퀀스를 제공.
/// Addressable 키: "DIALOGUE_DATA" (TextAsset, CSV 파일)
///
/// CSV 포맷 (헤더 1행 포함):
///   sequence_id, line_order, speaker, illustration_key, text
///   StartRoom, 0, God, Illust_God_Default, "잠에서 깨어나라..."
/// </summary>
public class DialogueDataManager
{
    // ─────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────

    private const string AddressableKey = "DIALOGUE_DATA";

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
            var textAsset = await Managers.AddressableManager.TryLoadAssetAsync<TextAsset>(AddressableKey);
            if (textAsset != null)
            {
                ParseCsv(textAsset.text);
                Debug.Log($"[DialogueDataManager] {_sequences.Count}개 시퀀스 로드 완료");
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

    // ─────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────

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
