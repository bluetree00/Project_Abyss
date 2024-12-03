using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class AugmentSelector
{
    private static readonly string[] CommonAugments = { "Common_Augment_1", "Common_Augment_2", "Common_Augment_3" };
    private static readonly string[] RareAugments = { "Rare_Augment_1", "Rare_Augment_2" };
    private static readonly string[] UniqueAugments = { "Unique_Augment_1" };

    private static readonly Dictionary<string, float> GradeProbabilities = new Dictionary<string, float>
    {
        { "Common", 80f },
        { "Rare", 15f },
        { "Unique", 5f }
    };

    /// <summary>
    /// 각 증강 등급에서 랜덤으로 하나의 이름을 선택
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

        int randomIndex = Random.Range(0, pool.Length);
        return pool[randomIndex];
    }

    /// <summary>
    /// 확률에 따라 증강 등급 선택
    /// </summary>
    private static string GetRandomGrade()
    {
        float totalProbability = 0f;
        foreach (var probability in GradeProbabilities.Values)
        {
            totalProbability += probability;
        }

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
    /// 최종적으로 랜덤 증강 선택 후 Resources에서 로드
    /// </summary>
    public static AugmentData GetRandomAugment()
    {
        string grade = GetRandomGrade(); // 등급 선택
        string augmentName = GetRandomAugmentFromGrade(grade); // 등급 내 증강 선택

        if (!string.IsNullOrEmpty(augmentName))
        {
            // Resources에서 증강 데이터 로드
            return Resources.Load<AugmentData>($"Augments/{augmentName}");
        }

        return null;
    }
}

