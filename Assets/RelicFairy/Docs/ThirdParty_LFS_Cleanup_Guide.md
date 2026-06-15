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

1. 새 팩은 `Assets/_ThirdParty/`에 임포트 (git이 자동 무시)
2. 실제 쓸 에셋을 **Unity 안에서**(`AssetDatabase.MoveAsset` 또는 드래그) `Assets/RelicFairy/_Imported/<팩>/`로 이동
   - 반드시 **연쇄 의존(머티리얼·텍스처·메시)까지 함께** 이동 — 안 그러면 다른 사람에게 missing(핑크)
   - 파일 탐색기로 옮기면 GUID 깨짐 → 반드시 Unity 안에서 이동
3. `git add` → `.gitattributes` 규칙으로 자동 LFS 추적

**LFS로 잡히는 확장자**: `.png .jpg .tga .psd .fbx .obj .wav .ogg .mp4`
(그 외 큰 확장자를 쓰면 `.gitattributes`에 규칙 추가 필요)

## 5. 남은 작업 (향후)

추적 해제만으로는 **원격 LFS 용량이 줄지 않습니다**(과거 커밋이 객체를 참조). 실제 16GB 회수는 별도 진행 예정:

- `git filter-repo`로 전체 히스토리에서 `_ThirdParty` 제거 → force push (팀 전원 re-clone 필요)
- GitHub Support에 미참조 LFS 객체 purge 요청 (또는 repo 재생성)

→ 이 단계는 **팀 전체 일정 조율 후** 진행합니다. 그 전까지는 본 가이드대로 pull/작업하면 됩니다.
