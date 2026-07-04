# 플레이어 대시 / 검 트레일 핑크(매젠타) 이펙트 분석

> 분석 일시: 2026-06-29, 브랜치 `dev/lee` (`f5f397f96`, develop 머지 직후)
> 분석 방식: **정적 분석**(에셋/씬 파일의 GUID 참조 추적). 이 세션에서는 Unity MCP(에디터 라이브 연결)가 비활성 상태라 Console 에러나 Inspector의 실제 Shader 필드 값은 확인하지 못했습니다 — 결론은 "현재 코드/에셋 참조에는 끊긴 곳이 없다"까지이며, 최종 확인은 에디터에서 직접 해야 합니다.

## 배경

- 핑크(매젠타)는 텍스처 누락이 아니라 **셰이더 참조 누락/컴파일 실패** 신호 (`docs/lee-pink-reimport-guide.md` 참고).
- 과거 동일 증상이 두 차례 있었음:
  1. `8da9cb9ab` — `docs/player-weapon-pink-effects-list.md` 작성. 원인: `Weapon Trail Template.vfx`의 텍스처 참조(GUID `8a82ebcf…`, INab_Noise_20) 끊김 + InputSlot 67개 누락.
  2. `a5e4c0de7` — 위 템플릿을 정상본으로 복원해 해소("플레이어 트레일 핑크 해소").

## 이번 분석 대상

| 효과 | 구동 스크립트 | 트레일 자산 체인 |
|---|---|---|
| 플레이어 대시 트레일 | [DodgePresentation.cs](../Assets/RelicFairy/Characters/Player/Scripts/DodgePresentation.cs) | `TrailRenderer` + [DashTrail.mat](../Assets/RelicFairy/Characters/Player/Materials/DashTrail.mat) (CharacterData.dashTrailMaterial 경유) |
| 플레이어 검 공격 트레일 | [PlayerWeaponTrailVfx.cs](../Assets/RelicFairy/Characters/Player/Scripts/PlayerWeaponTrailVfx.cs) | `WeaponTrailEffect` + `WeaponData.trailVfxPrefab` (예: Blood 1 / Water 4) → `Weapon Trail Template.vfx` |
| 데스나이트 보스 검 트레일 | [DeathKnightSwordController.cs](../Assets/RelicFairy/Characters/Monster/Monster/DeathKnightBoss/DeathKnightSwordController.cs) | `WeaponTrailEffect` + `Holy 1.prefab` / `Dark 1.prefab` → `Weapon Trail Template.vfx` |

> 참고: 기존 `docs/player-weapon-pink-effects-list.md`의 "대시 트레일 = `Subtle 1.prefab`(VFX) + `CharacterData.dashTrailVfxPrefab`" 설명은 **이제 코드와 맞지 않음(outdated)**. `dashTrailVfxPrefab` 필드는 `CharacterData.cs`에서 더 이상 존재하지 않고, 현재 대시 트레일은 `PlayerWeaponTrailVfx.cs` 주석에도 명시되어 있듯 **`DodgePresentation`의 `TrailRenderer`가 전담**한다 (`DashTrailMaterial` / `Sprites-Default` 셰이더 경로).

## 확인한 GUID 참조 체인 (전부 정상 해소됨)

- `DashTrail.mat` → `m_Shader: {fileID: 10753, guid: 0000000000000000f000000000000000}` = Unity 빌트인 `Sprites/Default`. 빌트인 리소스라 항상 해소됨, 끊긴 참조 아님.
- `PlayerCharacterData.asset.dashTrailMaterial` → guid `f8a6a786dd296664e8ae4c8eef1ef7a5` = `DashTrail.mat` 정상 매칭.
- `Weapon Trail Template.vfx` 내 텍스처 참조 guid `8a82ebcf11cab5a42bd794e00e0997cb`(INab_Noise_20.png) → **현재 존재함** (`a5e4c0de7` 수정사항이 머지 후에도 유지됨).
- `Holy 1.prefab` / `Dark 1.prefab` 내부 텍스처·서브스크립트 GUID 전수 검사 → 전부 해소(노이즈/노멀/코스틱 텍스처, `VFXLossyTransformBinder`, `VFXPropertyBinder` 모두 정상. 후자 둘은 Assets가 아니라 `com.unity.visualeffectgraph` 패키지 내부 스크립트라 GUID가 Assets에는 없는 게 정상).
- `DeathKnightBoss.prefab`의 `_whiteMaterial`/`_blackMaterial`/`_holyTrailPrefab`/`_darkTrailPrefab` 필드 → 전부 할당돼 있고 각 GUID(`DK_Sword_White.mat`, `DK_Sword_Black.mat`, `Holy 1.prefab`, `Dark 1.prefab`)도 정상 해소.
- `DK_Sword_White/Black.mat`의 셰이더 guid `933532a4fcc9baf4fa0491de14d08ed7` = URP 빌트인 `Universal Render Pipeline/Lit` (패키지 캐시에서 해소, Assets에는 당연히 없음).

**즉, 대시/검 트레일 양쪽 모두 끊긴 GUID(materal/shader/texture)는 발견되지 않았습니다.** 과거 두 차례의 핑크 원인(텍스처 GUID 끊김)은 현재 재발하지 않은 상태입니다.

## 가장 유력한 원인 — 로컬 셰이더 캐시(Library) 불일치

정적 참조가 모두 정상인데 화면에서는 핑크로 보인다면, `docs/lee-pink-reimport-guide.md`에 기록된 것과 동일한 패턴(로컬 `Library/` 셰이더 캐시가 최근 머지(특히 `_ThirdParty`→`_Imported` 경로 이동, 폰트 SDF 충돌 해결 등) 이전 상태로 남아있어 셰이더가 에러 상태로 표시됨)일 가능성이 가장 높습니다.

- 이번 세션에서 `dev/lee`에 `origin/develop`을 머지(`f5f397f96`)하면서 다수의 `_Imported` 하위 에셋 경로/참조가 변경됨 → 로컬 Library가 이 변경을 아직 반영하지 못했을 수 있음.
- `Library/` 폴더의 최종 수정 시각은 머지 커밋 이후로 확인되어 Unity가 한 번은 다시 열렸을 가능성이 있으나, **셰이더 재컴파일까지 완료됐는지는 정적 분석으로 확인 불가**.

## 권장 확인 절차 (에디터에서 직접)

1. Unity 에디터에서 핑크로 보이는 오브젝트(대시 트레일 / 검 트레일) 클릭 → Inspector → Material → **Shader 필드 값** 확인.
   - `Hidden/InternalErrorShader` 또는 빈 값이면 셰이더 컴파일 실패 확정.
2. `Console` 창에서 `shader` / `import` 키워드로 필터링해 에러 메시지 확인.
3. 그래도 안 풀리면 `docs/lee-pink-reimport-guide.md`의 3단계(`Library/` 폴더 삭제 후 재임포트) 수행.
4. 위 1~2번 결과(셰이더 이름, 에러 메시지, 머티리얼 파일 경로)를 알려주시면 정확한 GUID로 재조사 가능합니다.

## 미해결/확인 필요 항목

- 이 세션은 Unity MCP 연결이 없어 실제 Console 에러나 Inspector 값을 직접 읽지 못했습니다. 위 1~2번 정보를 받으면 후속 분석을 이어갈 수 있습니다.
- `docs/player-weapon-pink-effects-list.md`의 대시 트레일 설명(`Subtle 1.prefab`, `dashTrailVfxPrefab`)은 현재 코드 구조와 맞지 않아 갱신이 필요합니다(이번 문서가 최신 구조 반영).
