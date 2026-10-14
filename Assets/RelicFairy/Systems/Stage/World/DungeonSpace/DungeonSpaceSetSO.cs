using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// S4 「던전 공간」 챕터별 색 — 하늘 대신 탑 안(10-03 설계 · 10-02 구현 설계 §1-1).
/// <see cref="DungeonSpaceDirector"/>가 런 씬에서 읽는다. Addressable 주소 <see cref="Address"/>(만들기 메뉴가 등록).
/// 탑 벽 재료는 여기 두지 않는다 — 지금 방 벽 재질을 실행 중에 집어 와 방과 같은 돌로 보이게 한다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Stage/Dungeon Space Set", fileName = "DungeonSpaceSet")]
public sealed class DungeonSpaceSetSO : ScriptableObject
{
    public const string Address = "DungeonSpaceSet";

    [Serializable]
    public struct Entry
    {
        public ChapterId chapter;
        [Tooltip("카메라 배경(하늘 자리) — 탑 안 어둠")]
        public Color voidColor;
        [Tooltip("탑 벽 원통에 곱하는 색 — 방 벽 재질을 어둡게 · 챕터 빛깔로")]
        public Color shellTint;
        [Tooltip("구덩이 바닥에 곱하는 색 — 더 어둡게")]
        public Color pitTint;
        [Tooltip("S4-3 천장 틈 빛줄기 · 먼지 빛깔")]
        public Color shaftColor;
        [Tooltip("대기방 도착 자리(스폰 칸) 마법진 — 바닥에 눕는 반복 이펙트")]
        public GameObject spawnMark;
        [Tooltip("도착 마법진 배율")]
        public float spawnMarkScale;

        // ── 10-12 챕터 공기(구현설계 「챕터별_톤·포그·배경_점검_20261012」) ──
        [Tooltip("직사광 색 — 알파 0이면 씬 값 그대로. MCP가 조명 Color를 못 바꿔 런 시작 때 여기서 입힌다")]
        public Color sunColor;
        [Tooltip("해방기 · 악몽 모드 색 — 안개 · 환경광 · 직사광 · 그림자를 이 색조로 민다(밝기는 그대로). 알파 = 세기, 0 = 시기 변화 없음")]
        public Color eraTint;
        [Tooltip("분위기 입자 1종(절제) — 플레이어를 따라가며 월드 공간에 흩날린다. 비우면 없음")]
        public GameObject ambientFx;
        [Tooltip("분위기 입자 방출 배율 — 0이면 원본 그대로")]
        public float ambientFxRate;
    }

    [SerializeField] private List<Entry> entries = new();
    [Tooltip("S4-3 빛줄기 · 먼지 재질(가산 · 양면 파티클 재질) — 텍스처는 빛줄기가 코드 그라데이션으로 덮는다")]
    [SerializeField] private Material shaftMaterial;

    public Material ShaftMaterial => shaftMaterial;

    public bool TryGet(ChapterId chapter, out Entry entry)
    {
        foreach (var e in entries)
            if (e.chapter == chapter) { entry = e; return true; }
        entry = default;
        return false;
    }
}
