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

        // 전체 증강 수를 제한하여 무한 루프 방지
        HashSet<string> attemptedGrades = new HashSet<string>();

        while (true)
        {
            // 현재 등급에서 선택 가능한 증강 필터링
            var availablePool = pool.Except(_selectedAugments).ToArray();

            if (availablePool.Length == 0)
            {
                attemptedGrades.Add(grade);

                // 모든 등급을 순환했는지 확인
                if (attemptedGrades.Count == 3) // 3개 등급 (Common, Rare, Unique)
                {
                    Debug.LogWarning("모든 증강 등급에서 더 이상 선택할 수 있는 증강이 없습니다.");
                    return null; // 선택 불가능한 상태
                }

                // 다음 등급으로 이동
                grade = grade switch
                {
                    "Common" => "Rare",
                    "Rare" => "Unique",
                    "Unique" => "Common",
                    _ => null
                };

                pool = grade switch
                {
                    "Common" => CommonAugments,
                    "Rare" => RareAugments,
                    "Unique" => UniqueAugments,
                    _ => null
                };

                continue; // 다른 등급에서 다시 시도
            }

            // 선택 가능한 증강에서 무작위 선택
            int randomIndex = Random.Range(0, availablePool.Length);
            selectedAugment = availablePool[randomIndex];

            break;
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
        string augmentName = GetRandomAugmentFromGrade(grade); // 중복 방지 없이 증강 선택

        Debug.Log(augmentName);

        return augmentName; // 하나의 증강 이름만 반환
    }

    /// <summary>
    /// 외부에서 증강을 선택했을 때 해당 증강을 중복 목록에 추가
    /// </summary>
    public static void AddSelectedAugment(string augmentName)
    {
        if (!_selectedAugments.Contains(augmentName))
        {
            _selectedAugments.Add(augmentName); // 선택된 증강만 중복 처리
        }
    }

    /// <summary>
    /// 선택된 증강 초기화
    /// </summary>
    public static void ResetSelection()
    {
        _selectedAugments.Clear();
    }
}
