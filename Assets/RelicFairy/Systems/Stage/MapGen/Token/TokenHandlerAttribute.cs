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

    public TokenHandlerAttribute(string code, TokenCategory category, string description = "", bool isPrefix = false)
    {
        Code = code;
        Category = category;
        Description = description;
        IsPrefix = isPrefix;
    }
}
