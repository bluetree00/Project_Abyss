using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public static class AugmentSelector
{
    private static readonly string[] CommonAugments = { "Common_Augment_1", "Common_Augment_2", "Common_Augment_3" };
    private static readonly string[] RareAugments = { "Rare_Augment_1", "Rare_Augment_2", "Rare_Augment_3" };
    private static readonly string[] UniqueAugments = { "Unique_Augment_1", "Unique_Augment_2", "Unique_Augment_3" };

    private static readonly Dictionary<string, float> GradeProbabilities = new Dictionary<string, float>
    {
        { "Common", 80f },
        { "Rare", 15f },
        { "Unique", 5f }
    };

    private static List<string> _selectedAugments = new List<string>();

    /// <summary>
    /// 각 증강 등급에서 중복 없는 랜덤 증강 이름을 선택
    /// </summary>
    private static string GetRandomAugmentFromGrade(string grade)
    {
        string[] pool = grade switch
        {
            "Common" => CommonAugments,
            "Rare" => RareAugments,
            "Unique" => UniqueAugments,
            _ => null
        };

        if (pool == null || pool.Length == 0)
            return null;

        string selectedAugment = null;

        // 증강이 남아있을 때까지 계속해서 증강을 선택
        while (true)
        {
            // 이미 선택된 증강 제외
            var availablePool = pool.Except(_selectedAugments).ToArray();

            if (availablePool.Length == 0)
            {
                // 해당 등급에서 더 이상 선택할 수 있는 증강이 없을 경우
                Debug.LogWarning($"선택 가능한 {grade} 등급 증강이 없습니다. 다른 등급에서 선택합니다.");
                
                // 선택할 다른 등급 찾기
                // Common -> Rare -> Unique 순으로 검사
                string[] nextPool = grade switch
                {
                    "Common" => RareAugments,
                    "Rare" => UniqueAugments,
                    "Unique" => CommonAugments, // 마지막엔 다시 Common으로 돌아가도록 할 수도 있음
                    _ => null
                };

                if (nextPool != null && nextPool.Length > 0)
                {
                    pool = nextPool;  // 다른 등급으로 변경
                    continue;  // 다른 등급에서 다시 시도
                }
                else
                {
                    Debug.LogWarning("모든 증강 등급에서 더 이상 선택할 수 있는 증강이 없습니다.");
                    return null;  // 더 이상 선택할 증강이 없으면 null 반환
                }
            }

            // 증강이 남아있으면 랜덤으로 선택
            int randomIndex = Random.Range(0, availablePool.Length);
            selectedAugment = availablePool[randomIndex];

            // 선택된 증강이 유효하다면 반복 종료
            if (selectedAugment != null)
            {
                _selectedAugments.Add(selectedAugment);
                break;
            }
        }

        return selectedAugment;
    }

    /// <summary>
    /// 확률에 따라 증강 등급을 선택
    /// </summary>
    private static string GetRandomGrade()
    {
        float totalProbability = GradeProbabilities.Values.Sum();

        float randomValue = Random.Range(0, totalProbability);
        float cumulativeProbability = 0f;

        foreach (var grade in GradeProbabilities)
        {
            cumulativeProbability += grade.Value;
            if (randomValue <= cumulativeProbability)
                return grade.Key;
        }

        return "Common"; // 기본값
    }

    /// <summary>
    /// 랜덤 증강을 선택하고 해당 이름을 반환
    /// </summary>
    public static string GetRandomAugmentName()
    {
        string grade = GetRandomGrade(); // 등급 선택
        string augmentName = GetRandomAugmentFromGrade(grade); // 중복 방지된 증강 선택

        Debug.Log(augmentName);

        return augmentName; // 하나의 증강 이름만 반환
    }

    /// <summary>
    /// 선택된 증강 초기화
    /// </summary>
    public static void ResetSelection()
    {
        _selectedAugments.Clear();
    }
}

