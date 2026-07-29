# _ThirdParty LFS 정리 — 작업자 가이드

> 적용 시점: 2026-06-16 / 대상 브랜치: `develop` (커밋 `2dc8e4362` 이후)

## 1. 무엇을 / 왜 했나

Git LFS 저장 용량이 한도(10GB)를 초과해, 외부 에셋 폴더 `Assets/_ThirdParty`(약 24GB)를 정리했습니다.

- **실제 사용하는 에셋만** 골라(연쇄 의존 포함 3,083개 / 8.2GB) `Assets/RelicFairy/_Imported/`로 이동(GUID 보존)
- 나머지 미사용 원본(16GB)이 있는 `Assets/_ThirdParty/`는 **git 추적에서 제외**(`.gitignore`)
- 로컬 파일은 보존되지만 git은 더 이상 추적하지 않음

결과: 앞으로 `_ThirdParty`는 LFS에 새로 쌓이지 않고, 실제 쓰는 에셋만 `_Imported`에서 추적됩니다.

## 2. 구조 변경 요약

| 경로 | 변화 |
|---|---|
| `Assets/_ThirdParty/` | **git 추적 제외**(.gitignore). 로컬엔 미사용 원본만 남음 |
| `Assets/RelicFairy/_Imported/<팩>/` | **신규**. 실제 사용 에셋(출처 팩별 구조 보존) |

이동 규칙: `Assets/_ThirdParty/<팩>/<상대경로>` → `Assets/RelicFairy/_Imported/<팩>/<상대경로>`

## 3. develop pull 받는 방법

### 일반 작업자 (`_ThirdParty`를 직접 수정하지 않은 분)

```bash
# 1. 본인 작업 먼저 커밋 or stash (작업트리 clean 필수)
git status
git stash            # 미커밋 변경이 있으면

# 2. Unity 에디터 닫기 (대량 파일 변동 + 재import 충돌 방지)

# 3. develop 최신화 (LFS 파일도 자동 수신)
git pull origin develop

# 4. Unity 다시 열기 → 라이브러리 재import (시간 걸림, 정상)
git stash pop        # 3에서 stash 했으면
```

**pull 후 일어나는 일**
- `Assets/_ThirdParty/` → 워킹트리에서 삭제됨 (미사용 원본이라 게임 영향 없음)
- `Assets/RelicFairy/_Imported/` → 새로 수신 (실제 사용 에셋, LFS 포함)
- `.gitignore`에 `_ThirdParty` 추가됨

### `_ThirdParty` 원본을 로컬에 계속 두고 싶다면 (선택)

```bash
mv Assets/_ThirdParty ../_ThirdParty_backup   # pull 전: 폴더 밖으로
git pull origin develop
mv ../_ThirdParty_backup Assets/_ThirdParty    # pull 후: 되돌리기(이후 .gitignore라 무시됨)
```

### ⚠️ `_ThirdParty`를 작업 중인 분 (`dev/lee-SO`, `dev/KBG-N` 등)

**단순 pull 금지.** 본인 브랜치에서 `_ThirdParty`를 수정/이동 중이면 머지 시 modify/delete 충돌이 대량 발생하고,
해당 브랜치가 쓰는 `_ThirdParty` 에셋이 `_Imported`(3,083개)에 없으면 참조가 깨질 수 있습니다.

→ **머지 전에 직접 조율** 필요:
- `_ThirdParty` 정리 방식을 통일(`_Imported` 기준으로)
- 본인 브랜치가 쓰는 `_ThirdParty` 에셋도 같은 방식으로 함께 옮긴 뒤 머지

## 4. 앞으로의 워크플로우 — 새 외부 에셋을 쓸 때

### 핵심 변경점 (왜 이렇게 하나)

| | 기존 | 변경 후 |
|---|---|---|
| 팩을 받으면 | 전체가 git 추적 → **받자마자 전부 서버로** (안 쓰는 것도 용량 차지) | `_ThirdParty`라 **git 무시 → 서버에 안 올라감** |
| 서버에 올라가는 시점 | 받자마자 전부 | **쓸 것만 RelicFairy로 가져왔을 때** |

→ 받은 팩 전체가 아니라 **실제 쓰는 에셋만** 서버 용량을 쓰게 됨. 이게 LFS 용량 절약의 핵심.

### 절차

1. 새 팩은 `Assets/_ThirdParty/`에 임포트 (git이 자동 무시 → 서버에 안 올라감)
2. 쓸 에셋 + **연쇄 의존 전부**를 RelicFairy로 이동:
   - Project 창에서 가져올 에셋 선택 → 우클릭 → **Select Dependencies**
     (그 에셋이 참조하는 머티리얼·텍스처·메시·셰이더가 함께 선택됨)
   - 선택된 것 중 **`_ThirdParty` 경로의 것만** → `Assets/RelicFairy/_Imported/<팩>/`로 드래그(이동)
   - ⚠️ 반드시 **Unity 안에서** 이동 — 파일 탐색기로 옮기면 GUID 깨짐
   - ⚠️ 연쇄 의존을 빠뜨리면 **본인 로컬은 멀쩡한데 협업자에게만 missing(핑크)**
3. `git add` → `.gitattributes` 규칙으로 자동 LFS 추적 → 서버에 올라감

### 검증 (가끔)

옮길 때 의존성을 빠뜨렸는지는 본인 로컬에선 안 보입니다(로컬엔 `_ThirdParty` 원본이 남아 있어 정상으로 보임).
주기적으로 **"RelicFairy가 `_ThirdParty`를 참조하는지"** GUID 교차 점검 → 발견되면 그 에셋도 마저 이동.

**LFS로 잡히는 확장자**: `.png .jpg .tga .psd .fbx .obj .wav .ogg .mp4`
(그 외 큰 확장자를 쓰면 `.gitattributes`에 규칙 추가 필요)

## 5. LFS 용량 운영 방침

### 핵심 — purge는 필수가 아니라 선택

LFS storage는 **GiB-hours(시간당 사용량)**로 측정되고 **매월 1일 0으로 리셋**됩니다(저장 객체는 그대로 남아도). `$0 budget`이면 한도 소진 시 그 달만 막히고, 다음 달 리셋되면 새 push가 다시 가능합니다.

→ 따라서 **월 리셋 주기를 활용한 점진 추가**로 운영합니다:

```
받은 팩 → _ThirdParty (추적 없이 통째 보관, 서버 용량 0)
              ↓ 매월 리셋(월초 여유)에
          쓸 에셋만 → _Imported (점진적으로 서버에 추가)
```

- 받은 팩은 `_ThirdParty`에 통째 보관 → 서버 용량 0
- 매월 **월초(리셋 직후 여유가 가장 큼)**에 쓸 에셋을 `_Imported`로 옮겨 추가
- 한 번에 올리는 양은 여유 내로 조절 (연쇄 의존까지 포함하면 생각보다 큼)

### 한계 / 향후

`_Imported` 누적이 무료 한도(10GB)를 넘을수록 월 후반이 빠듯해집니다(현재 `_Imported` 8.2GB + 게임 자체 2.3GB ≈ 10.5GB로 이미 약간 초과). 운영하다 정말 빠듯해지면 그때 아래를 **선택적으로** 진행:

- `git filter-repo`로 전체 히스토리에서 `_ThirdParty` 제거 → force push (팀 전원 re-clone) → GitHub Support purge 요청
- 또는 data pack 구매 ($5 / 50GB)

→ **기존 에셋은 서버에 온전**하며(clone 테스트 확인) 협업은 정상입니다. purge는 "매월 빠듯함"을 없애려는 안정화 작업일 뿐, 지금 당장 필수는 아닙니다.
