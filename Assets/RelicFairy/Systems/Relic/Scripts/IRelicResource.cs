using System;
using UnityEngine;

/// <summary>
/// HUD 아이덴티티 바가 이 리소스를 어떤 형태로 그릴지 — 유물 종류를 몰라도 형태만 고른다.
/// Bar: 수평 게이지 바(랜슬롯 광기 등). Sun: 가웨인 정오 태양(열림→충전 / 닫힘→정오 감소).
/// </summary>
public enum RelicGaugeStyle { Bar = 0, Sun = 1 }

/// <summary>
/// 차세대 유물의 고유 리소스 메커닉 공통 계약 (시간형 게이지 / 적중형 스택 등).
/// HUD·스킬 게이팅·세이브·변형아이템이 **유물 종류를 몰라도** 이 계약만으로 연동한다.
/// 구현체는 MonoBehaviour로 플레이어에 부착(SolarTimer 선례 일반화).
/// </summary>
public interface IRelicResource
{
    /// <summary>0~1 정규화 진행도 (HUD 바 공통).</summary>
    float Fill { get; }
    /// <summary>HUD 아이덴티티 바 표현 형태(수평 바 / 태양). 종류를 몰라도 HUD가 형태만 분기.</summary>
    RelicGaugeStyle Style { get; }
    /// <summary>HUD 표시 텍스트("정오까지", "광기 32").</summary>
    string Label { get; }
    /// <summary>구간/상태 인덱스 (HUD 색·라벨 분기).</summary>
    int Phase { get; }
    /// <summary>HUD 아이덴티티 바 색(유물별 단계 색). 유물 종류를 몰라도 바가 그대로 사용.</summary>
    Color BarColor { get; }
    /// <summary>스킬 발동 가능 여부(정오 구간 / 스택 MAX 등). 스킬 게이팅이 읽음.</summary>
    bool IsSkillReady { get; }

    /// <summary>리소스 변화 신호 — HUD/스탯 갱신.</summary>
    event Action OnChanged;

    // ── 라이프사이클 (PlayerController/유물이 라우팅) ──
    void Tick(float deltaTime);
    void OnAttackLanded(GameObject target);
    void OnKill(GameObject target);

    // ── 세이브/이어하기 ──
    RelicResourceState Capture();
    void Restore(RelicResourceState state);

    // ── 변형 아이템(보류 — 자리만 확보) ──
    /// <summary>변형 아이템이 메커닉 수치를 런타임 오버라이드.</summary>
    void ApplyConfig(RelicResourceConfig config);
}
