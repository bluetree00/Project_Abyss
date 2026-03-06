// WeaponInstance.cs (attach on weapon prefab)
using UnityEngine;

public class WeaponInstance : MonoBehaviour
{
    [Header("Trail Hit Detection")]
    [Tooltip("칼끝 Transform (빈 오브젝트, 인스펙터에서 연결)")]
    public Transform tipPoint;

    [Tooltip("손잡이 끝 Transform (빈 오브젝트, 인스펙터에서 연결)")]
    public Transform rootPoint;

    [Tooltip("트레일 SphereCast 반경")]
    public float hitRadius = 0.1f;

    [Tooltip("판정 레이어")]
    public LayerMask hitLayer;

    private WeaponData _data;

    public WeaponTrailDetector TrailDetector { get; private set; }

    public void Initialize(WeaponData data)
    {
        _data = data;
        TrailDetector = GetComponent<WeaponTrailDetector>() ?? gameObject.AddComponent<WeaponTrailDetector>();
        TrailDetector.Setup(this);
    }

    public void OnEquip() { }

    public void OnUnequip()
    {
        TrailDetector?.EndTrail();
    }
}
