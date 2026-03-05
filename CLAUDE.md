# Project Abyss - Claude 지침

## 세션 시작 시 자동 실행
- 대화 시작 시 `git fetch origin`으로 원격 패치를 확인한다
- 업데이트가 있으면 `git pull --ff-only`로 자동 풀 받는다
- 현재 브랜치: `dev/lee-SO`, 메인 브랜치: `main`, 통합 브랜치: `develop`

## 커밋 컨벤션
- `feat:` 새 기능
- `fix:` 버그 수정
- `docs:` 문서 변경
- `refactor:` 리팩토링
- `chore:` 기타 작업
