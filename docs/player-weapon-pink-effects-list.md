# 플레이어 무기 핑크(매젠타) 이펙트 목록

> 참고: [docs/lee-pink-reimport-guide.md](./lee-pink-reimport-guide.md) — 핑크(매젠타)는 텍스처 누락이 아니라 **셰이더 참조 누락/컴파일 실패** 신호.

## 원인

`Assets/RelicFairy/_Imported/INab Studio/Vfx Assets/Weapon Trails FX/Core/Weapon Trail Template.vfx`에서
텍스처 참조 2곳(라인 1228, 1373, GUID `8a82ebcf11cab5a42bd794e00e0997cb`)이 끊어져 있고,
VFX Graph InputSlot 67개가 일괄 제거된 상태입니다. 이 템플릿을 인스턴싱하는 모든 무기 트레일 프리팹이 영향을 받습니다.

## 핑크로 표시되는 플레이어 무기 이펙트

| 이펙트(프리팹) | 사용 무기 | 파일 경로 | 비고 |
|---|---|---|---|
| Blood 1 | Katana (검) T1/T2/T3 공격 트레일 | `Assets/RelicFairy/_Imported/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/Blood 1.prefab` | Weapon Trail Template.vfx 참조 |
| Water 4 | Greatsword (대검) T1/T2/T3 공격 트레일 | `Assets/RelicFairy/_Imported/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/Water 4.prefab` | Weapon Trail Template.vfx 참조 |
| Subtle 1 | 대시(Dash) 트레일 (무기 공통) | `Assets/RelicFairy/_Imported/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Trail Prefabs/Subtle 1.prefab` | Weapon Trail Template.vfx 참조, `CharacterData.dashTrailVfxPrefab` 경유 |

연결 스크립트: [PlayerWeaponTrailVfx.cs](../Assets/RelicFairy/Characters/Player/Scripts/PlayerWeaponTrailVfx.cs) — `WeaponData.trailVfxPrefab` 필드로 무기별 트레일 프리팹을 로드.

## 미해결 항목

- 새로 추가된 서브그래프 3종(`Adjustable Smoothstep`, `Simple Depth Mask Refraction`, `Smoothstep With Feather`, `Assets/RelicFairy/_Imported/INab Studio/Common/Subgraphs/Utilities/`)이 아직 어떤 VFX Graph에도 연결되어 있지 않음 — `Weapon Trail Template.vfx` 복구 시 이 서브그래프 연결 여부 확인 필요.
- 플레이어 무기용 **오라(Aura) 이펙트는 현재 프로젝트 내 존재하지 않음** (INab Studio 패키지 내 검색 결과 0건).

## 필요 에셋 요청

- **무기 트레일 이펙트**
- **오라 이펙트**
