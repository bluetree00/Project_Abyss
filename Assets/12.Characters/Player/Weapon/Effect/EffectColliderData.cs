using UnityEngine;

[DisallowMultipleComponent]
public class EffectColliderData : MonoBehaviour
{
    public Vector3 localCenter; // 루트 Transform 기준
    public Vector3 localSize;   // BoxCollider 기준
    public float duration = 0.5f; // 선택적으로 사용
}
