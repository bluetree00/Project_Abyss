using System;
using UnityEngine;

/// <summary>
/// WeaponEffectSO 데이터를 Runtime에서 안전하게 복사하여 사용하는 클래스
/// 멀티 인스턴스 안전하며, 서버 JSON 데이터로 덮어쓰기도 가능
/// </summary>
[Serializable]
public class WeaponEffectData
{
    // Identifier
    public string id;

    // Addressable / Prefab Key
    public string addressableKey;

    // Attach / Transform
    public string attachPoint;
    public Vector3 localPosition;
    public Vector3 localEuler;
    public Vector3 localScale;
    public bool followAttach;

    // Lifetime / Behavior
    public float lifetime;
    public string syncColliderId;

    // Variant / Meta
    public string variantTag;
    public int recommendedPoolSize;

    // 생성자: SO를 기반으로 RuntimeData 초기화
    public WeaponEffectData(WeaponEffectSO so)
    {
        if (so == null) throw new ArgumentNullException(nameof(so));

        // id = so.id;
        // addressableKey = so.addressableKey;
        // attachPoint = so.attachPoint;
        // localPosition = so.localPosition;
        // localEuler = so.localEuler;
        // localScale = so.localScale;
        // followAttach = so.followAttach;
        // lifetime = so.lifetime;
        // syncColliderId = so.syncColliderId;
        // variantTag = so.variantTag;
        // recommendedPoolSize = so.recommendedPoolSize;
    }

    /// <summary>
    /// 서버 데이터나 런타임 변경 사항을 덮어쓸 수 있도록 지원
    /// </summary>
    public void ApplyServerData(WeaponEffectData serverData)
    {
        if (serverData == null) return;

        // 필요한 필드만 덮어쓰기 가능
        lifetime = serverData.lifetime;
        variantTag = serverData.variantTag;
        // 필요하다면 다른 필드도 선택적으로 덮어쓰기
    }
}
