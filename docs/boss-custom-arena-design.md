# 보스 커스텀 아레나 설계 (길 1 + 갈래 1)

작성일: 2026-06-12 · 상태: 코드 배선 구현·컴파일 검증 완료 / 아레나 프리팹(콘텐츠) 제작 대기

## 1. 목표

보스 방을 **손으로 만든 커스텀맵 프리팹**으로 구성한다. 플레이어가 그 맵 안의
**진입로(통로/배경)를 걸어 내려가** 끝에 다다르면 보스가 등장한다. 방 경계를 넘는
가림 전환 없이 **한 화면 안에서 매끄럽게** "걸어가서 도달 → 등장"이 이뤄진다.

- **길 1**: 보스/트리거/배리어/지형을 전부 커스텀 프리팹 안에 직접 배치. 위치는 자유.
- **갈래 1**: 진입로를 보스 커스텀맵 *안에* 포함. 별도 PreBoss 방으로 진입 연출을 하지 않는다.

## 2. 왜 이 구조인가 (기술적 근거)

방은 격리형으로 빌드되고 방 이동은 화면 커버(EnterRoomAsync) + 리프프로그 전환이다
([GameRunBootstrapper.BuildProcRoomAsync](../Assets/RelicFairy/Systems/Bootstrapper/Scripts/GameRunBootstrapper.cs#L854) 주석).
따라서 **방 경계를 넘으면 매끄러운 걸어 들어가기 연출이 불가능**하다. "걸어가서 도달 시
등장"을 살리려면 진입로와 아레나가 **같은 방(같은 roomGO)** 안에 있어야 한다 → 갈래 1.

## 3. 컴포넌트 흐름 (전부 기존 코드 재사용)

진입 트리거 → 보스 등장 → 카메라 연출은 이미 구현되어 있다. 새 게임플레이 코드 없음.

- [BossSpawner](../Assets/RelicFairy/Characters/Monster/Monster/Core/BossSpawner.cs)
  - `placedBoss` = 프리팹에 비활성으로 배치한 보스
  - `waitForExternalTrigger = true` → `Trigger()` 호출까지 대기
- [BossRoomController](../Assets/RelicFairy/Systems/Stage/World/BossRoomController.cs)
  - 트리거 콜라이더(isTrigger)를 플레이어가 통과 → `barrier` 닫고 `bossSpawner.Trigger()`
  - `bossZoneCenter`로 카메라 팬 → 보스 Appear 연출 → 입력 복구
- [RoomClearController](../Assets/RelicFairy/Systems/Stage/RunGame/RoomClearController.cs)
  - `BossSpawner.OnMonsterSpawned` 체이닝으로 보스 사망 추적 → 런 종료

## 4. 손맵 프리팹 제작 규약

한 프리팹에 "진입로 + 아레나"를 통째로 만든다. **로컬 원점 (0,0,0) = 방 중앙(anchor)**.

```
[grid 입구 문 위치] → 통로/배경(석상·제단 등) → [트리거 콜라이더] → 아레나(보스)
```

프리팹 구성:
- 지형/콜라이더/장식/조명 — 로컬 원점 기준 자유 배치
- **바닥은 NavMesh에 구워져야 함** (§6) — 보스가 이동하려면 필수
- 보스(MonsterBase) 원하는 자리에 **비활성**으로 배치
- `BossSpawner`(waitForExternalTrigger=true, placedBoss 연결)
- `BossRoomController`(bossSpawner/barrier/bossZoneCenter 연결) + 트리거 콜라이더
- 배리어 오브젝트(입구 봉쇄용)

**정합 규약**: 프리팹의 통로 시작점은 grid 입구 문(§5)의 로컬 좌표와 맞춘다.
플레이어는 grid 입구에서 스폰되어 통로 시작점에 서야 하므로, grid를 고정 소형 사이즈로
두고 입구를 정해진 변(edge)에 두어 로컬 좌표를 예측 가능하게 한다.

## 5. 데이터 배선

보스 풀 엔트리(ZonePoolEntry):
- `arena_template_key` = 커스텀 프리팹 Addressable 키 (예: `Arena_Boss_Ch1`)
  - 현재 CSV·파서에 칸만 존재하고 **소비처가 없음** → §6에서 연결
- `grid_csv`의 역할 **축소**: 내부 지형 없음. **입구 문 1개 + 최소 외곽틀**만.
  - 보스 방은 종착(Phase.Boss → 런 종료)이라 forward/turn 출구 불필요
  - 입구 문은 직전 방에서 오는 복도 연결에 필요
  - `B`/스포너/장식 토큰 불필요 (전부 프리팹이 담당)

## 6. 코드 변경점 (신규 — 구현 대상)

[BuildProcRoomAsync](../Assets/RelicFairy/Systems/Bootstrapper/Scripts/GameRunBootstrapper.cs#L900)
한 군데. roomGO 생성 직후(~L904) ~ **`BuildMapNavMeshAsync(roomGO)`(L955) 이전**에
아레나 프리팹을 인스턴스화해 바닥이 NavMesh에 포함되게 한다.

```
// roomGO @ anchor 직후
if (!string.IsNullOrEmpty(entry.arena_template_key))
{
    var arena = await Managers.AddressableManager.InstantiateAsync(entry.arena_template_key);
    if (arena != null) arena.transform.SetParent(roomGO.transform, false); // 로컬원점=방중앙
}
```

부수 결정:
- **블록 빌드 억제**: `arena_template_key`가 있으면 프리팹이 바닥/벽을 제공하므로
  `MapBuilder.Build/BuildCeiling/BuildRoomLights`를 건너뛰어 중복/Z파이팅 방지.
  단, **입구 복도 스텁**(BuildDoorCorridor, L920)과 입구 문 개방은 유지 — 직전 방 연결.
  → grid는 입구 문만 둔 빈 외곽이므로 Build를 건너뛰어도 안전.
- 인스턴스 해제: roomGO Destroy 시 자식으로 함께 파괴되나, Addressable 인스턴스는
  `ReleaseInstance` 경로 확인 필요 (§7).

## 7. 확인/검증 필요 (구현 시)

1. **RoomClearController 연결** — [AttachRoomClearController](../Assets/RelicFairy/Systems/Bootstrapper/Scripts/GameRunBootstrapper.cs#L959)가
   프리팹 안 BossSpawner를 자식 탐색으로 잡는지. 못 잡으면 명시 연결 추가.
2. **NavMesh** — 프리팹 바닥이 BuildMapNavMeshAsync에 포함되는지(레이어/Static 플래그).
3. **Addressable 해제** — roomGO 파괴 시 arena 인스턴스 누수 없는지.
4. **입구 정합** — 플레이어 스폰(grid 입구)과 프리팹 통로 시작점이 맞물리는지.
5. **PreBoss 방** — 갈래 1에서는 순수 숨고르기/전투 방으로 의미 변경(진입 연출 책임 제거).
   현행 시퀀서 흐름은 그대로 둠.

## 8. 작업 순서 (구현 착수 시)

1. arena_template_key 소비 배선 + 블록 빌드 억제 → 보스 풀 엔트리에 키 입력
2. 더미 아레나 프리팹(통로+트리거+placedBoss)으로 빌드/진입/등장 검증
   - verify: 입구 스폰 → 통로 보행 → 트리거 도달 → 배리어/보스/카메라 → 처치 → 런 종료
3. §7 항목 점검 → 콘솔 에러 0 (refresh_unity → read_console)
