using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Util
{

    // 컴포넌트가 없을 때 추가해주는 매서드 
    public static T GetOrAddComponent<T>(GameObject go) where T : UnityEngine.Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
            component = go.AddComponent<T>();
        return component;
    }

    //자식 탐색 매서드
    public static GameObject FindChild(GameObject go, string name = null, bool recursive = false)
    {
        Transform transform = FindChild<Transform>(go, name, recursive);

        if (transform == null)
        {
            return null;
        }

        return transform.gameObject;
    }


    public static T FindChild<T>(GameObject go, string name = null, bool recursive = false) where T : UnityEngine.Object
    {
        if (go == null)
            return null;

        if (recursive == false)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                Transform transform = go.transform.GetChild(i);
                if (string.IsNullOrEmpty(name) || transform.name == name)
                {
                    T component = transform.GetComponent<T>();

                    if (component != null)
                    {
                        return component;
                    }
                }
            }

        }
        else
        {
            foreach (T component in go.GetComponentsInChildren<T>())
            {
                if (string.IsNullOrEmpty(name) || component.name == name)
                    return component;

            }

        }

        return null;

    }

    public static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }
        return null;
    }

    // 새 형태: bool이 아닌 Define.AttackPurpose를 직접 반환
    public static Define.AttackPurpose CastTool(Define.AttackPurpose purpose, bool increment)
    {
        return CastToolPurposePlus(purpose, increment);
    }

    // 하위 호환 오버로드: out 매개변수를 유지하되 반환형을 AttackPurpose로 변경
    [Obsolete("Use CastTool(AttackPurpose, bool) returning AttackPurpose.")]
    public static Define.AttackPurpose CastTool(Define.AttackPurpose input, bool increment, out Define.AttackPurpose output)
    {
        output = CastToolPurposePlus(input, increment);
        return output;
    }

    // 새 구현 (언더스코어 제거된 네이밍: Normal01, Normal02 ...)
    /// <summary>
    /// AttackPurpose 끝의 숫자(2자리 등)를 파싱해 increment면 +1, 아니면 01로 리셋.
    /// 숫자 없는 항목(Special, Ultimate)은 그대로 유지.
    /// </summary>
    public static Define.AttackPurpose CastToolPurposePlus(Define.AttackPurpose purpose, bool increment)
    {
        string name = purpose.ToString();

        // 뒤에서부터 연속 숫자 구간 찾기
        int digitStart = name.Length;
        while (digitStart > 0 && char.IsDigit(name[digitStart - 1]))
            digitStart--;

        string baseName = name.Substring(0, digitStart);
        string digitPart = name.Substring(digitStart);

        int num = 0;
        if (digitPart.Length > 0 && int.TryParse(digitPart, out int parsedNum))
            num = parsedNum;

        // 숫자 없는 경우(예: Special, Ultimate) → 증가/리셋 모두 자기 자신 유지
        if (digitPart.Length == 0)
            return purpose;

        int target = increment ? num + 1 : 1;

        string candidate = baseName + target.ToString("D2");
        if (Enum.TryParse(candidate, out Define.AttackPurpose parsed))
            return parsed;

        // 클램프: baseName 으로 시작하고 숫자 가진 것 중 최대 찾기
        int max = 0;
        foreach (var n in Enum.GetNames(typeof(Define.AttackPurpose)))
        {
            if (!n.StartsWith(baseName))
                continue;
            string tail = n.Substring(baseName.Length);
            if (tail.Length == 0) // 숫자 없는 것 스킵 (Normal01 패턴 아닌 경우)
                continue;
            if (int.TryParse(tail, out int v) && v > max)
                max = v;
        }

        if (max > 0)
        {
            int clamped = Math.Min(target, max);
            string clampedName = baseName + clamped.ToString("D2");
            if (Enum.TryParse(clampedName, out parsed))
                return parsed;
        }

        return purpose; // 실패 시 원본 유지
    }

    // Animator Override 슬롯 키를 AttackPurpose에서 유도 (새 네이밍 Normal01 등)
    // Normal01 -> Attack01, Normal02 -> Attack02, Special -> Special01(또는 필요시 Special), Ultimate -> Ultimate01
    public static string GetAnimatorSlotKeyByPurpose(Define.AttackPurpose purpose)
    {
        string name = purpose.ToString();

        // 뒤의 숫자 추출
        int digitStart = name.Length;
        while (digitStart > 0 && char.IsDigit(name[digitStart - 1]))
            digitStart--;
        string baseName = name.Substring(0, digitStart);
        string digitPart = name.Substring(digitStart);

        int index = 0;
        if (digitPart.Length > 0 && int.TryParse(digitPart, out int parsed))
            index = parsed;
        else
            index = 1; // 숫자 없으면 01 간주

        string suffix = index.ToString("D2");
        switch (baseName)
        {
            case "Normal":
                return $"Attack{suffix}"; // Animator에는 Attack01, Attack02 ... 형태라고 가정
            case "Special":
                return $"Special{suffix}"; // 필요 시 "Special"만 반환하도록 조정 가능
            case "Ultimate":
                return $"Ultimate{suffix}";
            default:
                return baseName + suffix; // 기타 패턴 일반화
        }
    }

   
}
