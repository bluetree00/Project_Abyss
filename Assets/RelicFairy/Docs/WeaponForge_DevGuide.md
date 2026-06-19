# 무기 선택(모루) 시스템 설계

## 목표
시작방에서 **모루(Anvil)** 오브젝트와 상호작용하면 선택 팝업이 떠서,
- 근거리: **대검 / 카타나** 중 택1
- 원거리: **보우 / 석궁** 중 택1

을 고르고 확정하면 두 무기를 동시에 장착한다.

## 스킬 모델 (현행 기준)
- **Q** = 유물(Relic) 고유 능력 — `RelicClassSO.QSkillClipKey`
- **E / R** = 현재 활성 무기의 기술 — `WeaponSO.skillE` (+ R은 후속)
- 무기는 근거리/원거리 2종을 동시에 보유하고 Tab으로 전환. Q는 무기와 무관하게 항상 유물.

> 참고: `PlayerLoadout.WeaponSlot1` 주석의 "Q 스킬 전용"은 레거시. 현재는 슬롯1=원거리 무기.

## 데이터 흐름
```
WeaponForgeAltar (모루)                 UI_WeaponForgePopup
  OnTriggerEnter(Player) → [F]
  Claim()
    ├ Managers.UI.ShowPopupUIAndGetAsync<UI_WeaponForgePopup>()
    ├ popup.Setup(meleeOptions, rangedOptions)
    └ await popup.WaitForChoiceAsync()  ──▶  근거리/원거리 각 택1 + [확정]
                                        ◀──  ForgeChoice? (melee, ranged) / null(취소)
  확정 시:
    loadout.SetWeaponSlot0(melee)
    loadout.SetWeaponSlot1(ranged)
    EquipWeaponToPlayerAsync(melee)   → slot0 (활성)
    EquipWeaponToPlayerAsync(ranged)  → slot1
    모루 소비 (DissolveEffect.PlayDisappear)
```

## 재사용한 기존 패턴
- 상호작용: `RelicAltar` (trigger → [F] → claim → loadout 반영 → 다른 제단 제거)
- 팝업: `UI_WeaponReplacePopup`의 `UniTaskCompletionSource` + `WaitForChoiceAsync()`
- 장착: `GameRunBootstrapper.EquipWeaponToPlayerAsync` (빈 슬롯 자동 장착)

## 신규 구조
| 파일 | 역할 |
|------|------|
| `Systems/Stage/StartRoom/WeaponForgeAltar.cs` | 모루 상호작용. 4개 `MainWeaponSO` 직렬화(melee[2]/ranged[2]) |
| `UI/Popup/UI_WeaponForgePopup.cs` | 근/원 카테고리 각 택1 + 확정 팝업 |
| `UI/Popup/UI_WeaponForgePopup.prefab` (Addressable) | 팝업 UI (1920×1080 기준) |

### 카탈로그 위치 (대안 검토)
- **채택**: 모루 프리팹에 4개 WeaponSO 직접 직렬화. 단일 모루엔 가장 단순.
- 미채택: `WeaponForgeCatalogSO`(별도 에셋). 모루 다수·티어 분기 필요 시 재검토.

## HUD 연동 (후속, 별도 작업)
현재 `HudPresenter.RefreshWeaponSlots`는 Q/E/R 아이콘을 모두 활성 무기에서 가져온다.
새 모델에선 **Q는 유물에서** 가져와야 하므로:
- `RelicClassSO`에 Q 스킬 아이콘 Sprite 필드 추가 (현재 portrait/clipKey만 존재)
- `HudPresenter`가 유물 바인딩을 받아 Q 아이콘을 유물 기준으로 세팅, E/R만 무기 기준
- 무기 슬롯 2개에 "현재 활성" 하이라이트 추가

이 항목은 HUD 개선 방향 확정 후 진행.
