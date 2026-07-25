# 빌드 배포(패키징) 체크리스트

Standalone(Windows/Steam) 빌드를 **배포용으로 패키징**할 때 참고. 게임 자산/코드 변경이
아니라 "빌드 산출물을 어떻게 골라 담느냐"에 대한 운영 지침이다.

## 1. 배포에서 제외할 폴더 (빌드 산출물 옆에 생성됨)

IL2CPP / Burst 개발 빌드는 실행 파일 옆에 아래 폴더를 생성한다. 이름 그대로 **배포본에
포함하면 안 된다** — 심볼/디버그 백업일 뿐이고, 용량만 크게 차지한다.

| 폴더 패턴 | 정체 | 배포 |
|---|---|---|
| `*_BackUpThisFolder_ButDontShipItWithYourGame` | IL2CPP 심볼 백업(크래시 심볼리케이션용) | **제외** |
| `*_BurstDebugInformation_DoNotShip` | Burst 컴파일 디버그 정보 | **제외** |

예: 빌드 경로가 `C:/Users/u/Desktop/RelicFairyBuild/RelicFairy.exe` 이면
- `RelicFairyBuild/RelicFairy_BackUpThisFolder_ButDontShipItWithYourGame/`
- `RelicFairyBuild/RelicFairy_BurstDebugInformation_DoNotShip/`
두 폴더는 **압축/설치본에 넣지 않는다.**

> 심볼 폴더(BackUp…)는 크래시 로그 해석에 필요하니 **삭제하지 말고 별도 보관**하고,
> 배포 패키지에만 넣지 않는 것을 권장한다.

### (선택) 자동화하려면
배포 빌드를 릴리즈(비-Development)로 뽑는다면 `[UnityEditor.Callbacks.PostProcessBuild]`
콜백에서 위 두 패턴 폴더를 산출물 디렉터리에서 제거하는 후처리를 붙일 수 있다.
단, 현재 개발 빌드(`BuildOptions.Development`)에서는 디버깅에 필요할 수 있어
**개발 빌드에서는 남기고 릴리즈 빌드에서만 제거**하도록 조건을 두는 것이 안전하다.
(현 시점에서는 워크플로 급변을 피하려 코드 자동화 대신 이 문서로 남긴다.)

## 2. Static Batching (보류 — 확인 필요)

`ProjectSettings/ProjectSettings.asset`의 `m_StaticBatching: 1`을 `0`으로 끄면
URP + SRP Batcher 환경에서 시각적으로 동일하면서 `level*.resS`(정적 결합 메시) 산출물이
크게 줄어든다. 다만 `ProjectSettings.asset`은 "건드리지 말 것" 목록에 포함돼 있어
**작업 보류 상태**다. 적용을 원하면 확인 요망. (SRP Batcher 활성 여부 선행 확인 필요.)
