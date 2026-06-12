using System.Collections.Generic;

/// <summary>
/// 변형 아이템이 유물 리소스의 메커닉 수치를 런타임 오버라이드하는 키-값 설정.
/// ⚠️ 변형 아이템 자체는 보류(아이템 시스템 완성 후) — 계약 자리만 확보한다.
/// 키 규약: 리소스 구현체가 정의(예 "noon_duration", "max_stacks").
/// </summary>
public sealed class RelicResourceConfig
{
    private readonly Dictionary<string, float> _overrides = new();

    public void Set(string key, float value) => _overrides[key] = value;
    public bool Has(string key) => _overrides.ContainsKey(key);
    public float Get(string key, float fallback) => _overrides.TryGetValue(key, out var v) ? v : fallback;
}
