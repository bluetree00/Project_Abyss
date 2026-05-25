using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// [TokenHandler] Attribute가 붙은 ITokenHandler 구현체를 런타임에 자동 탐색·등록한다.
/// 새 핸들러는 클래스 작성 + Attribute 추가만으로 자동 등록되며 이 파일을 수정할 필요 없다.
/// </summary>
public static class TokenRegistry
{
    public struct HandlerEntry
    {
        public string Code;
        public TokenCategory Category;
        public string Description;
        public bool IsPrefix;
        public ITokenHandler Handler;
    }

    private static readonly Dictionary<string, HandlerEntry> _exact   = new();
    private static readonly List<HandlerEntry>               _prefix  = new();
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        var types = Assembly.GetExecutingAssembly().GetTypes();
        int count = 0;

        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (!typeof(ITokenHandler).IsAssignableFrom(type)) continue;

            var attr = type.GetCustomAttribute<TokenHandlerAttribute>();
            if (attr == null) continue;

            var entry = new HandlerEntry
            {
                Code        = attr.Code,
                Category    = attr.Category,
                Description = attr.Description,
                IsPrefix    = attr.IsPrefix,
                Handler     = (ITokenHandler)Activator.CreateInstance(type),
            };

            if (attr.IsPrefix)
                _prefix.Add(entry);
            else
                _exact[attr.Code] = entry;

            count++;
        }

        Debug.Log($"[TokenRegistry] {count}개 핸들러 등록 완료");
    }

    /// <summary>rawToken에 매칭되는 핸들러 반환. 없으면 null.</summary>
    public static ITokenHandler Resolve(string rawToken)
    {
        EnsureInitialized();
        if (string.IsNullOrEmpty(rawToken)) return null;

        if (_exact.TryGetValue(rawToken, out var exact)) return exact.Handler;

        for (int i = 0; i < _prefix.Count; i++)
            if (rawToken.StartsWith(_prefix[i].Code, StringComparison.Ordinal))
                return _prefix[i].Handler;

        return null;
    }

    // Editor Window / 디버그용 읽기 전용 접근
    public static IReadOnlyDictionary<string, HandlerEntry> ExactHandlers
    {
        get { EnsureInitialized(); return _exact; }
    }

    public static IReadOnlyList<HandlerEntry> PrefixHandlers
    {
        get { EnsureInitialized(); return _prefix; }
    }

    // 에디터에서 도메인 리로드 후 재스캔용
    public static void Reset()
    {
        _exact.Clear();
        _prefix.Clear();
        _initialized = false;
    }
}
