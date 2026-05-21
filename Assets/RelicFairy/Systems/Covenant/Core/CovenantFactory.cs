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
    public const string Arthur     = "arthur";
    public const string Galahad    = "galahad";
    public const string Mordred    = "mordred";
    public const string Morrigan   = "morrigan";
    public const string CuChulainn = "cuchulainn";
    public const string Lugh       = "lugh";
    public const string Balor      = "balor";
    public const string Hecate     = "hecate";
    public const string Solomon    = "solomon";
    public const string Prometheus = "prometheus";

    // ── 등록 테이블 ─────────────────────────────────────
    private static readonly Dictionary<string, Func<CovenantBase>> _registry
        = new(StringComparer.OrdinalIgnoreCase)
    {
        { Nimue,      () => new NimueCovenant()      },
        { Morgana,    () => new MorganaCovenant()    },
        { Arthur,     () => new ArthurCovenant()     },
        { Galahad,    () => new GalahadCovenant()    },
        { Mordred,    () => new MordredCovenant()    },
        { Morrigan,   () => new MorriganCovenant()   },
        { CuChulainn, () => new CuChulainnCovenant() },
        { Lugh,       () => new LughCovenant()       },
        { Balor,      () => new BalorCovenant()      },
        { Hecate,     () => new HecateCovenant()     },
        { Solomon,    () => new SolomonCovenant()    },
        { Prometheus, () => new PrometheusCovenant() },
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
