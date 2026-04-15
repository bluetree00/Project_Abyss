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

    private void OnDrawGizmos()
    {
        if (tipPoint == null || rootPoint == null) return;

        bool active = TrailDetector != null && TrailDetector.IsActive;
        Gizmos.color = active
            ? new Color(1f, 0.1f, 0.1f, 0.8f)
            : new Color(0.6f, 0.6f, 0.6f, 0.4f);

        Gizmos.DrawWireSphere(rootPoint.position, hitRadius);
        Gizmos.DrawWireSphere(tipPoint.position, hitRadius);
#if UNITY_EDITOR
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.DrawLine(rootPoint.position, tipPoint.position);
        UnityEditor.Handles.color = Color.white;
        UnityEditor.Handles.Label(rootPoint.position + Vector3.up * 0.05f, "Root");
        UnityEditor.Handles.Label(tipPoint.position + Vector3.up * 0.05f, "Tip");
#endif
    }
}
