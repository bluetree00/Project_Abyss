#if UNITY_EDITOR
using System.Reflection;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 테스트 런 작은 패널(에디터 전용, 10-01) — 테스트 허브에서 시작한 런에만 오른쪽 위에 뜬다(접으면 버튼 하나).
///   · 지금 시기(봉인기 · 해방기 · 엔딩 뒤 · 악몽 모드)와 이 시기의 보스 · 리치가 어떻게 싸우는지
///   · 보스 → 다음 페이지 직전 : 숲 · 화룡 · 기사는 2페이지 경계(해방기부터), 리치는 다음 페이지 임계 바로 위
///   · 보스 → 빈사 : 끝 장면(봉인 · 처치 · 붕괴 · 엔딩) 확인
///   · 무적 켜기
/// 허브 화면을 가리지 않게 기본은 접혀 있다(09-19 UI 점검 — 허브 IMGUI가 전 화면을 가렸다).
/// </summary>
public sealed class TestRunOverlay : MonoBehaviour
{
    private const float Width = 330f;
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private bool     _open;
    private string   _last = "";
    private GUIStyle _small;

    /// <summary>테스트 허브가 런을 시작할 때 부른다 — 하나만 만든다.</summary>
    public static void Ensure()
    {
        if (FindFirstObjectByType<TestRunOverlay>() != null) return;
        var go = new GameObject("[TestRunOverlay]");
        DontDestroyOnLoad(go);
        go.AddComponent<TestRunOverlay>();
    }

    private void Update()
    {
        if (!TestRunRequest.Active) Destroy(gameObject);   // 허브로 돌아왔다
    }

    private void OnGUI()
    {
        _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        float scale = Mathf.Max(1f, Screen.height / 900f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = Screen.width / scale;

        if (!_open)
        {
            if (GUI.Button(new Rect(w - 96f, 8f, 88f, 26f), "테스트 ▾")) _open = true;
            return;
        }

        GUILayout.BeginArea(new Rect(w - Width - 8f, 8f, Width, 300f), GUI.skin.box);
        if (GUILayout.Button("테스트 ▴ 접기")) _open = false;
        GUILayout.Label($"시기: {EraLabel()}", _small);
        GUILayout.Label(EraRule(), _small);
        GUILayout.Space(4f);
        if (GUILayout.Button("보스 → 다음 페이지 직전")) _last = JumpBossToNextPage();
        if (GUILayout.Button("보스 → 빈사(끝 장면)"))   _last = JumpBossNearDeath();
        if (GUILayout.Button("플레이어 무적 1시간"))     _last = Invincible();
        GUILayout.Label(_last, _small);
        GUILayout.EndArea();
    }

    // ── 시기 ───────────────────────────────────────────────────

    public static string EraLabel() => StoryProgress.Era switch
    {
        StoryEra.Sealed        => "봉인기 — 리치 전",
        StoryEra.Liberated     => StoryProgress.HasEnded ? "엔딩 뒤 — 악몽 모드 끔" : "해방기 — 리치 붕괴 뒤 · 엔딩 전",
        StoryEra.NightmareMode => "악몽 모드",
        _                      => StoryProgress.Era.ToString(),
    };

    public static string EraRule() => StoryProgress.Era switch
    {
        StoryEra.Sealed        => "숲 · 화룡 · 기사 1줄(쓰러뜨리면 봉인) · 리치 2줄 → 봉인 의식 → 붕괴",
        StoryEra.Liberated     => StoryProgress.HasEnded
                                  ? "보스 2페이지(쓰러뜨리면 처치) · 리치 3줄 → 처치(엔딩은 이미 봄)"
                                  : "보스 2페이지(쓰러뜨리면 처치) · 리치 3줄 → 처치 → 엔딩",
        StoryEra.NightmareMode => "보스 2페이지 + 악몽 강화 · 악몽 규칙 · 리치 3줄",
        _                      => "",
    };

    // ── 보스 ───────────────────────────────────────────────────

    /// <summary>패널 버튼 · 확인 메뉴가 같이 쓴다 — 결과 한 줄.</summary>
    public static string JumpBossToNextPage()
    {
        var boss = FindBoss();
        if (boss == null) return "살아 있는 보스가 없다";

        if (boss is LichMonster lich)
        {
            var th = lich.PageThresholds;
            int page = lich.LichBB != null ? lich.LichBB.Page : 1;
            if (th == null || page - 1 >= th.Length) return $"리치 {page}페이지 — 마지막 페이지다(빈사를 쓰라)";
            SetHp(boss, Mathf.CeilToInt(th[page - 1] * boss.EffectiveMaxHp) + Mathf.Max(20, Mathf.RoundToInt(boss.EffectiveMaxHp * 0.01f)));
            return $"리치 {page}→{page + 1}페이지 직전 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp}";
        }

        if (boss is IPagedBoss paged && paged.Pages != null)
        {
            var p = paged.Pages;
            if (!p.Enabled) return $"{boss.name}: 봉인기라 1줄뿐 — 시기를 해방기로 바꾸고 보스방에 다시 들어가라";
            if (p.IsPage2)  return "이미 2페이지";
            SetHp(boss, p.Page2Hp + Mathf.Max(20, Mathf.RoundToInt(boss.EffectiveMaxHp * 0.01f)));
            return $"2페이지 직전 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp}";
        }
        return $"{boss.name}: 페이지 보스가 아니다";
    }

    public static string JumpBossNearDeath()
    {
        var boss = FindBoss();
        if (boss == null) return "살아 있는 보스가 없다";
        if (boss is IPagedBoss paged && paged.Pages != null && paged.Pages.Enabled && !paged.Pages.IsPage2)
            return "아직 1페이지 — 먼저 2페이지로";
        if (boss is LichMonster lich && lich.PageThresholds != null && lich.LichBB != null && lich.LichBB.Page - 1 < lich.PageThresholds.Length)
            return $"리치 {lich.LichBB.Page}페이지 — 마지막 페이지까지 넘긴 뒤에";
        SetHp(boss, Mathf.Max(1, Mathf.RoundToInt(boss.EffectiveMaxHp * 0.02f)));
        return $"빈사 — HP {boss.CurrentHp}/{boss.EffectiveMaxHp}";
    }

    private static string Invincible()
    {
        var player = GameRunBootstrapper.Instance?.Run?.Player;
        if (player == null) return "플레이어가 없다";
        player.SetInvincible(3600f);
        return "플레이어 1시간 무적";
    }

    private static MonsterBase FindBoss()
    {
        foreach (var m in FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
            if (m is IBoss && !m.IsDead) return m;
        return null;
    }

    private static void SetHp(MonsterBase boss, int hp)
    {
        var runtime = typeof(MonsterBase).GetField("_runtime", Inst)?.GetValue(boss);
        var field   = runtime?.GetType().GetField("CurrentHp", Inst);
        field?.SetValue(runtime, Mathf.Clamp(hp, 1, boss.EffectiveMaxHp));
    }
}
#endif
