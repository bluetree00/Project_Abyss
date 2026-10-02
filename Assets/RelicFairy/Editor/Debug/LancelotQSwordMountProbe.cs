#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [실측 · 에디터] 랜슬롯 Q 전용 검을 어디에 붙여야 손에 쥐어지는가 — 플레이어 프리팹을 기본 자세로 띄워
/// hand_r · add_weapon_r · WeaponMount의 상대 위치와, 카타나(장착 기준)의 칼끝 방향 · Q 검 메시의 긴 축을 잰다.
/// 결과: Temp/lancelot_qsword_mount.txt
/// </summary>
public static class LancelotQSwordMountProbe
{
    private const string PlayerPrefab = "Assets/RelicFairy/Characters/Player/Gawain/Prefabs/PlayerCharacter.prefab";
    private const string QSwordPrefab = "Assets/RelicFairy/Systems/Relic/Prefabs/Lancelot_QSword.prefab";
    private const string KatanaPrefab = "Assets/RelicFairy/Weapon/Katana/Prefabs/T1_Katana_Weapon.prefab";

    [MenuItem("RelicFairy/Debug/유물 성장 v2/10 랜슬롯 Q 검 손잡이 위치 실측 (에디터)")]
    private static void Run()
    {
        var sb = new StringBuilder();
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab));
        root.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var hand  = Find(root.transform, "hand_r");
            var add   = Find(root.transform, "add_weapon_r");
            var mount = Find(root.transform, "WeaponMount");
            sb.AppendLine($"hand_r {hand != null} · add_weapon_r {add != null} (부모 {add?.parent?.name}) · WeaponMount {mount != null} (부모 {mount?.parent?.name})");
            if (hand == null || add == null || mount == null) return;

            sb.AppendLine($"기본 자세 hand_r→add_weapon_r 거리 {Vector3.Distance(hand.position, add.position):0.000} m · hand_r 기준 위치 {V(hand.InverseTransformPoint(add.position))} · 회전 {E(Quaternion.Inverse(hand.rotation) * add.rotation)}");
            sb.AppendLine($"WeaponMount: hand_r 기준 위치 {V(hand.InverseTransformPoint(mount.position))} · 회전 {E(Quaternion.Inverse(hand.rotation) * mount.rotation)}");

            // 팩 원래 붙임: add_weapon_r 아래 X −90°
            var q = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(QSwordPrefab), add, false);
            q.transform.localPosition = Vector3.zero;
            q.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var mf = q.GetComponentInChildren<MeshFilter>();
            var mb = mf != null ? mf.sharedMesh.bounds : new Bounds();
            sb.AppendLine($"Q 검 메시 bounds(로컬) center {V(mb.center)} size {V(mb.size)}");
            // 긴 축 = 칼날. 손잡이 쪽 끝 = 원점에 가까운 끝
            Vector3 axis = mb.size.x >= mb.size.y && mb.size.x >= mb.size.z ? Vector3.right : mb.size.y >= mb.size.z ? Vector3.up : Vector3.forward;
            float half = Vector3.Dot(mb.extents, axis);
            Vector3 endA = mb.center + axis * half, endB = mb.center - axis * half;
            Vector3 tipLocal = endA.sqrMagnitude > endB.sqrMagnitude ? endA : endB;
            Vector3 qTipWorld = mf.transform.TransformPoint(tipLocal);
            Vector3 qGripWorld = mf.transform.TransformPoint(Vector3.zero);
            sb.AppendLine($"Q 검(팩 붙임) 손잡이 원점 hand_r 기준 {V(hand.InverseTransformPoint(qGripWorld))} · 칼끝 hand_r 기준 {V(hand.InverseTransformPoint(qTipWorld))} · 칼날 방향(hand_r) {V(hand.InverseTransformDirection((qTipWorld - qGripWorld).normalized))}");
            sb.AppendLine($"Q 검 메시 축 {V(axis)} · 칼끝 로컬 {V(tipLocal)} · 길이 {(tipLocal).magnitude:0.000}");

            // 카타나(장착과 같은 방식: WeaponMount 아래, 프리팹 루트 값 그대로)
            var k = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(KatanaPrefab), mount, false);
            var tip = Find(k.transform, "Tip");
            var kRoot = Find(k.transform, "Root");
            if (tip != null)
            {
                Vector3 grip = kRoot != null ? kRoot.position : k.transform.position;
                sb.AppendLine($"카타나 Tip hand_r 기준 {V(hand.InverseTransformPoint(tip.position))} · WeaponMount 기준 {V(mount.InverseTransformPoint(tip.position))} · 칼날 방향(WeaponMount) {V(mount.InverseTransformDirection((tip.position - mount.position).normalized))}");
                var kmf = k.GetComponentInChildren<MeshFilter>();
                if (kmf != null)
                {
                    var kb = kmf.sharedMesh.bounds;
                    sb.AppendLine($"카타나 메시 bounds(로컬) center {V(kb.center)} size {V(kb.size)} · 메시 원점 WeaponMount 기준 {V(mount.InverseTransformPoint(kmf.transform.position))}");
                }
            }
            sb.AppendLine($"Q 검(팩 붙임) 원점 WeaponMount 기준 {V(mount.InverseTransformPoint(q.transform.position))} · 회전(WeaponMount 기준) {E(Quaternion.Inverse(mount.rotation) * q.transform.rotation)} · 칼날 방향(WeaponMount) {V(mount.InverseTransformDirection((qTipWorld - qGripWorld).normalized))}");
        }
        finally
        {
            Object.DestroyImmediate(root);
            File.WriteAllText("Temp/lancelot_qsword_mount.txt", sb.ToString());
            Debug.Log("[QSwordMount] 끝 → Temp/lancelot_qsword_mount.txt\n" + sb);
        }
    }

    private static Transform Find(Transform r, string n)
    {
        if (r.name == n) return r;
        for (int i = 0; i < r.childCount; i++) { var h = Find(r.GetChild(i), n); if (h != null) return h; }
        return null;
    }

    private static string V(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
    private static string E(Quaternion q) { var e = q.eulerAngles; return $"({e.x:0.0}, {e.y:0.0}, {e.z:0.0})"; }
}
#endif
