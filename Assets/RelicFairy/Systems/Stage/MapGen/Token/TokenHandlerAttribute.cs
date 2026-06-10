using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TokenHandlerAttribute : Attribute
{
    public string Code { get; }
    public TokenCategory Category { get; }
    public string Description { get; }
    // true: code가 접두사로 매칭 ("d" → "dTR", "dWL" 등)
    // false: 정확히 일치 ("WP", "CP" 등)
    public bool IsPrefix { get; }
    /// <summary>핸들러가 실행될 페이즈. 기본값 PostBuild (NavMesh 빌드 후).</summary>
    public TokenPhase Phase { get; }
    /// <summary>에디터 윈도우에 표시할 CSV 사용 예시.</summary>
    public string CsvExample { get; }

    public TokenHandlerAttribute(string code, TokenCategory category, string description = "", bool isPrefix = false, TokenPhase phase = TokenPhase.PostBuild, string csvExample = "")
    {
        Code = code;
        Category = category;
        Description = description;
        IsPrefix = isPrefix;
        Phase = phase;
        CsvExample = csvExample;
    }
}
