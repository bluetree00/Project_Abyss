using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 서비스 NPC 애니메이터를 만든다 — 상태만 두고 전이는 그리지 않는다(되돌림은 <see cref="ServiceNpcReactor"/>가 한다).
/// 상태 이름은 반응 이름과 같다: Idle · Notice · Talk · Thanks · Shrug · Farewell · Work. 모든 상태 writeDefaultValues = false.
/// 메뉴: RelicFairy/Setup/Build Service NPC Animators (다시 눌러도 같은 결과 — 덮어쓴다).
/// </summary>
public static class ServiceNpcAnimatorBuilder
{
    private const string Suri   = "Assets/RelicFairy/_Imported/Suriyun/Animations/Animations_v3/AnimSD@";
    private const string Mush = "Assets/RelicFairy/_Imported/RPGMonsterBundlePolyart/RPGMonsterWave03Polyart/Animation/Mushroom/Mushroom_";

    // 상태 · 클립(FBX) · 재생 속도 — 10-01 몸짓 띠 렌더(Temp/npc_candidates/s_*)로 골랐다.
    // 행상(웃는 버섯): 혼자면 손님을 두리번 · 다가오면 움츠렸다 튀어 오름 · 말 걸면 들썩 · 사면 빙글 뛰고 · 안 사면 어질
    private static readonly (string state, string clip, float speed)[] Peddler =
    {
        ("Idle",     Mush + "IdleNormalSmile.fbx",             1f),
        ("Notice",   Mush + "IdlePlantToBattleSmile.fbx",      1f),
        ("Talk",     Mush + "TauntingSmile.fbx",               1f),
        ("Thanks",   Mush + "VictorySmile.fbx",                1f),
        ("Shrug",    Mush + "DizzySmile.fbx",                  0.8f),
        ("Farewell", Mush + "SenseSomethingStartSmile.fbx",    1f),
        ("Work",     Mush + "SenseSomethingMaintainSmile.fbx", 0.9f),
    };

    // 대장장이(M02, 투구 · 창 대신 망치): 혼자면 모루를 내리침 · 다가오면 자세를 잡고 · 말 걸면 두리번 · 벼리고 나가면 망치를 치켜듦 · 안 맡기면 지친 숨
    private static readonly (string state, string clip, float speed)[] Smith =
    {
        ("Idle",     Suri + "IdleB.fbx",    1f),
        ("Notice",   Suri + "Aert.fbx",     1f),
        ("Talk",     Suri + "IdleC.fbx",    1f),
        ("Thanks",   Suri + "VictoryB.fbx", 1f),
        ("Shrug",    Suri + "Tired.fbx",    1f),
        ("Work",     Suri + "ATK1.fbx",     0.8f),
    };

    [MenuItem("RelicFairy/Setup/Build Service NPC Animators")]
    public static void Build()
    {
        Make("Assets/RelicFairy/Systems/Stage/Shop/Prefabs/NpcPeddler.controller", Peddler);
        Make("Assets/RelicFairy/Systems/Stage/Crucible/Prefabs/NpcSmith.controller", Smith);
        AssetDatabase.SaveAssets();
        Debug.Log("[ServiceNpcAnimators] 만들기 끝 — NpcPeddler · NpcSmith");
    }

    private static void Make(string path, (string state, string clip, float speed)[] table)
    {
        // 다시 만들 때도 같은 에셋을 비워 쓴다 — 지우고 새로 만들면 GUID가 바뀌어 NPC 프리팹 연결이 끊긴다.
        var ctrl = File.Exists(path)
            ? AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(path)
            : UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm = ctrl.layers[0].stateMachine;
        foreach (var child in sm.states) sm.RemoveState(child.state);
        foreach (var (state, clipPath, speed) in table)
        {
            var clip = LoadClip(clipPath);
            if (clip == null) { Debug.LogWarning($"[ServiceNpcAnimators] 클립 없음 — {clipPath} ({state} 건너뜀)"); continue; }
            var s = sm.AddState(state);
            s.motion             = clip;
            s.speed              = speed;
            s.writeDefaultValues = false;   // 프로젝트 규약 — 새 상태는 기본값 쓰기 끔
            if (state == "Idle") sm.defaultState = s;
        }
        EditorUtility.SetDirty(ctrl);
        Debug.Log($"[ServiceNpcAnimators] {path} — 상태 {sm.states.Length}개");
    }

    private static AnimationClip LoadClip(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }
}
