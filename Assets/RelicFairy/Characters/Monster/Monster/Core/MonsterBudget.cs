using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <b>방 전체 동시 적 하드캡.</b>
///
/// 기존 문제: <see cref="MonsterSpawner"/>는 각자 <c>maxMonsterCount</c>(자기 몫)만 본다.
/// 방에 스포너가 여러 개 있으면 <b>아무도 합계를 안 막아서</b> 동시 적 수가 폭증하고,
/// 화면이 몹으로 꽉 차 가독성과 성능이 무너진다.
///
/// 해법(Vampire Survivors 방식): <b>동시 생존 수에 전역 상한</b>을 두고, 넘으면 스폰을 잠시 멈춘다.
/// 죽어서 자리가 나면 다시 스폰된다 → <b>"많이 잡는 재미"는 유지하되 화면은 통제</b>된다.
///
/// 스포너들이 자기 생존 수를 보고하는 방식이라 별도 사망 훅이 필요 없다
/// (스포너가 이미 <c>PurgeReturnedMonsters</c>로 죽은 몹을 정리한다).
/// </summary>
public static class MonsterBudget
{
    /// <summary>동시에 살아있을 수 있는 최대 적 수(보스 제외). 3인칭 근접이라 호드 게임보다 훨씬 작다.</summary>
    public static int AliveCap { get; set; } = 25;

    private static readonly List<MonsterSpawner> s_spawners = new();

    /// <summary>도메인 리로드 OFF 대비 — 플레이 시작마다 초기화.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_spawners.Clear();
        AliveCap = 25;
    }

    public static void Register(MonsterSpawner s)
    {
        if (s != null && !s_spawners.Contains(s)) s_spawners.Add(s);
    }

    public static void Unregister(MonsterSpawner s)
    {
        if (s != null) s_spawners.Remove(s);
    }

    /// <summary>현재 살아있는 총 적 수(전 스포너 합산). 스포너 수가 적어 매번 합산해도 부담 없다.</summary>
    public static int TotalAlive
    {
        get
        {
            int n = 0;
            for (int i = s_spawners.Count - 1; i >= 0; i--)
            {
                var s = s_spawners[i];
                if (s == null) { s_spawners.RemoveAt(i); continue; }   // 파괴된 스포너 정리
                n += s.AliveCount;
            }
            return n;
        }
    }

    /// <summary>지금 한 마리 더 스폰해도 되는지. 상한에 걸리면 스포너는 이번 턴 스폰을 건너뛴다.</summary>
    public static bool CanSpawn => AliveCap <= 0 || TotalAlive < AliveCap;
}
