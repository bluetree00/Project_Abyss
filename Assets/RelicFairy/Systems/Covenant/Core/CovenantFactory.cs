using System;
using System.Collections.Generic;

/// <summary>
/// 서약 ID 문자열로 CovenantBase 인스턴스를 생성하는 팩토리.
/// 새 서약 추가 시 Register() 항목만 추가한다.
/// </summary>
public static class CovenantFactory
{
    // ── ID 상수 ──────────────────────────────────────────
    public const string Nimue      = "nimue";
    public const string Morgana    = "morgana";
    public const string Lionel      = "lionel";      // v2 신규 (행동 조건형)
    public const string Elaine      = "elaine";      // v2 신규 (전투 리듬형)
    public const string Kay         = "kay";         // v2 신규 (전투 리듬형)
    public const string Bedivere    = "bedivere";    // v2 신규 (트레이드오프형)
    public const string Tristan     = "tristan";     // v2 신규 (행동 조건형)
    public const string Isolde      = "isolde";      // v2 신규 (전투 리듬형)
    public const string Leodegrance = "leodegrance"; // v2 신규 (런 구조형 — 임시 스텁)
    public const string Guinevere   = "guinevere";   // v2 신규 (런 구조형 — 임시 스텁)
    public const string Arthur     = "arthur";
    public const string Galahad    = "galahad";

    // ── 등록 테이블 ─────────────────────────────────────
    private static readonly Dictionary<string, Func<CovenantBase>> _registry
        = new(StringComparer.OrdinalIgnoreCase)
    {
        { Nimue,      () => new NimueCovenant()      },
        { Morgana,    () => new MorganaCovenant()    },
        { Lionel,      () => new LionelCovenant()      },
        { Elaine,      () => new ElaineCovenant()      },
        { Kay,         () => new KayCovenant()         },
        { Bedivere,    () => new BedivereCovenant()    },
        { Tristan,     () => new TristanCovenant()     },
        { Isolde,      () => new IsoldeCovenant()      },
        { Leodegrance, () => new LeodegranceCovenant() },
        { Guinevere,   () => new GuinevereCovenant()   },
        { Arthur,     () => new ArthurCovenant()     },
        { Galahad,    () => new GalahadCovenant()    },
        // v2 로스터 확정: 위 12종이 랜덤 3지선다 풀.
        // 구버전 8종(mordred/morrigan/cuchulainn/lugh/balor/hecate/solomon/prometheus)은
        // 2026-06-12 구현체·데이터·테이블 참조 전부 삭제 완료.
    };

    // ── API ─────────────────────────────────────────────
    /// <summary>ID로 서약 인스턴스 생성. 미등록 ID는 null 반환.</summary>
    public static CovenantBase Create(string covenantId)
    {
        if (_registry.TryGetValue(covenantId, out var factory))
            return factory();

        UnityEngine.Debug.LogWarning($"[CovenantFactory] 미등록 서약 ID: {covenantId}");
        return null;
    }

    public static IReadOnlyCollection<string> AllIds => _registry.Keys;
}
