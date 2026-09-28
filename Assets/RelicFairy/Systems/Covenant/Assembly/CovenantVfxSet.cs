using UnityEngine;

/// <summary>
/// 서약 효과 → 실제 VFX 프리팹 매핑(정적 데이터 SO). <see cref="ElementVfxRegistry"/>와 같은 결이다 —
/// 프리팹을 직접 참조하므로 이 에셋 하나만 Addressable("CovenantVfxSet")로 두면 번들에 함께 들어간다.
/// 재생은 <see cref="CovenantFxService"/>가 <see cref="ElementVfxPlayer"/>의 풀로 한다(출시 빌드에서도 보인다).
///
/// 소스: _Imported/EffectSource (Spells Pack LWRP · Hovl Studio). 고른 근거와 실측 반경은
/// 바탕화면 구현설계 「서약_구현·리소스_조합별감사」 §7.
/// </summary>
[CreateAssetMenu(fileName = "CovenantVfxSet", menuName = "RelicFairy/Covenant/Covenant VFX Set")]
public sealed class CovenantVfxSet : ScriptableObject
{
    [System.Serializable]
    private struct Entry
    {
        [Tooltip("CovenantFxService의 키(효과 id 또는 ruby 등)")]
        public string key;
        public GameObject prefab;
        [Tooltip("기본 배율. nativeRadius가 있으면 여기에 (판정 반경 ÷ nativeRadius)를 곱한다")]
        [Min(0.01f)] public float scale;
        [Tooltip("배율 1에서 파티클이 퍼지는 반경(m, 실측). 0이면 판정 반경에 맞추지 않는다 — " +
                 "파티클 배율 모드가 Hierarchy가 아닌 프리팹은 부모 배율을 일부 무시하므로 0으로 둔다")]
        [Min(0f)] public float nativeRadius;
        [Tooltip("즉발 회수 시각 / 부착 기본 지속(초)")]
        [Min(0.05f)] public float life;
    }

    [SerializeField] private Entry[] entries = new Entry[0];

    /// <summary>키의 프리팹과 재생 배율·수명. radius>0이고 반경 맞춤 항목이면 배율에 반경비를 곱한다.</summary>
    public bool TryGet(string key, float radius, out GameObject prefab, out float scale, out float life)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.key != key || e.prefab == null) continue;
            prefab = e.prefab;
            scale  = e.scale * (radius > 0f && e.nativeRadius > 0f ? radius / e.nativeRadius : 1f);
            life   = e.life;
            return true;
        }
        prefab = null;
        scale  = 1f;
        life   = 0f;
        return false;
    }
}
