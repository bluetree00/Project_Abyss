// EnhanceResult.cs
using System;

/// <summary>강화 시도 결과 종류.</summary>
public enum EnhanceOutcome
{
    Success,        // 성공 — 단계 +1
    FailDropped,    // 실패 — 단계 하락(하한 0)
    RejectMaxed,    // 거부 — 이미 등급 상한
    RejectNoFuel,   // 거부 — 강화재료 부족
    RejectInvalid,  // 거부 — 대상 없음/무효
}

/// <summary>강화 1회 시도의 결과. UI 연출·로그용 전후값 포함.</summary>
[Serializable]
public struct EnhanceResult
{
    public EnhanceOutcome outcome;
    public int  beforeLevel;   // 변경된 무기의 시도 전 단계
    public int  afterLevel;    // 변경된 무기의 시도 후 단계
    public int  spent;         // 소모한 강화재료

    // 연출 전용 부가 필드(로직 무관, 기본 0). 니어미스 강조 판정에 사용.
    public double roll;        // 실패 판정에 쓰인 롤값(0~1). Reject 시 0.
    public float  chance;      // 이 시도에 적용된 유효 성공확률(0~1). Reject 시 0.

    public bool IsSuccess => outcome == EnhanceOutcome.Success;
    public bool IsReject  => outcome == EnhanceOutcome.RejectMaxed
                          || outcome == EnhanceOutcome.RejectNoFuel
                          || outcome == EnhanceOutcome.RejectInvalid;

    public static EnhanceResult Reject(EnhanceOutcome o)
        => new EnhanceResult { outcome = o };
}

/// <summary>승급(전설 분기) 결과 종류.</summary>
public enum PromoteOutcome
{
    Success,               // 승급 성공 — legendId 부여
    RejectNotMaxed,        // 거부 — 강화 상한 미달
    RejectAlreadyLegend,   // 거부 — 이미 승급됨
    RejectNoFuel,          // 거부 — 강화재료 부족
    RejectInvalidLegend,   // 거부 — 잘못된 전설/무기 타입 불일치
}

/// <summary>승급 1회 시도 결과.</summary>
[Serializable]
public struct PromoteResult
{
    public PromoteOutcome outcome;
    public string legendId;
    public int    spent;

    public bool IsSuccess => outcome == PromoteOutcome.Success;

    public static PromoteResult Reject(PromoteOutcome o)
        => new PromoteResult { outcome = o };
}
