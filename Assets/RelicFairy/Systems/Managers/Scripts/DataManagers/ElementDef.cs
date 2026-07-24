using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 멀린 룬 그리드의 속성(존) 단일 정의 테이블.
/// 존 코드(char) ↔ zone_id(string) ↔ 표시명/아이콘/색을 한 곳에서 관리한다.
/// 기존엔 MerlinRuneHexGridView / RuneDataManager /
/// MerlinRuneSynergyStatusView / CharacterInfoPanelView / UI_GridPanel 6곳에 중복돼 있었다.
///
/// 존 코드: F=불, I=얼음, T=번개, P=독, L=빛, D=어둠, '+'=중심(CENTER)
/// CENTER('+')는 시너지 존이 아니므로 Order에는 포함하지 않는다(보너스 전용).
/// </summary>
public static class ElementDef
{
    public sealed class Entry
    {
        public readonly string Id;
        public readonly char   Code;
        public readonly string Name;
        public readonly string Icon;
        public readonly Color  Color;

        public Entry(string id, char code, string name, string icon, Color color)
        {
            Id = id; Code = code; Name = name; Icon = icon; Color = color;
        }
    }

    // 시너지 존 6속성 — 표시 순서 = 이 배열 순서
    // 보드 코드(char)는 ZONE_MAP과 호환 유지: 'T'=전기(Electric), 'P'=풀(Grass).
    private static readonly Entry[] s_Elements =
    {
        new("FIRE",     'F', "불",   "▲", new Color(1.00f, 0.38f, 0.22f)),
        new("ICE",      'I', "얼음", "◇",  new Color(0.45f, 0.80f, 1.00f)),
        new("ELECTRIC", 'T', "전기", "↗", new Color(1.00f, 0.88f, 0.25f)),
        new("GRASS",    'P', "풀",   "▼",  new Color(0.55f, 0.82f, 0.30f)),
        new("LIGHT",    'L', "빛",   "◆",  new Color(1.00f, 0.95f, 0.65f)),
        new("DARK",     'D', "어둠", "■", new Color(0.62f, 0.40f, 0.92f)),
    };

    // CENTER 특수 존 (시너지 목록 제외, 보너스 전용)
    public const  char   CenterCode  = '+';
    public const  string CenterId    = "CENTER";
    public static readonly Color CenterColor = new(0.85f, 0.85f, 0.90f);

    private static readonly Dictionary<char, Entry>   s_ByCode = new();
    private static readonly Dictionary<string, Entry> s_ById   = new();
    private static readonly string[]                  s_Order;

    static ElementDef()
    {
        s_Order = new string[s_Elements.Length];
        for (int i = 0; i < s_Elements.Length; i++)
        {
            var e = s_Elements[i];
            s_ByCode[e.Code] = e;
            s_ById[e.Id]     = e;
            s_Order[i]       = e.Id;
        }
    }

    /// <summary>시너지 존 표시 순서 (CENTER 제외).</summary>
    public static IReadOnlyList<string> Order => s_Order;

    public static Entry GetById(string id)   => id != null && s_ById.TryGetValue(id, out var e) ? e : null;
    public static Entry GetByCode(char code) => s_ByCode.TryGetValue(code, out var e) ? e : null;

    /// <summary>존 코드 → zone_id. '+'=CENTER, 미정의=null.</summary>
    public static string CodeToId(char code)
    {
        if (code == CenterCode) return CenterId;
        return s_ByCode.TryGetValue(code, out var e) ? e.Id : null;
    }

    /// <summary>zone_id → 존 코드. CENTER='+', 미정의='\0'.</summary>
    public static char IdToCode(string id)
    {
        if (id == CenterId) return CenterCode;
        return id != null && s_ById.TryGetValue(id, out var e) ? e.Code : '\0';
    }

    /// <summary>존 코드 기본 색(alpha 1) 조회. '+'=중심색. 미정의 시 false.</summary>
    public static bool TryGetCodeColor(char code, out Color color)
    {
        if (code == CenterCode) { color = CenterColor; return true; }
        if (s_ByCode.TryGetValue(code, out var e)) { color = e.Color; return true; }
        color = default;
        return false;
    }

    /// <summary>zone_id 색(alpha 1). 미정의 시 fallback 반환.</summary>
    public static Color IdColor(string id, Color fallback)
    {
        var e = GetById(id);
        return e != null ? e.Color : fallback;
    }

    /// <summary>zone_id 색의 "#RRGGBB" 문자열(TMP richtext용). 미정의 시 fallback.</summary>
    public static string IdHex(string id, string fallback = "#AAAAAA")
    {
        var e = GetById(id);
        return e != null ? "#" + ColorUtility.ToHtmlStringRGB(e.Color) : fallback;
    }
}
