<h1 align="center">🎮 Project Abyss</h1>
<h3 align="center">Roguelike Action PC Game</h3>

<p align="center">
  <b>확장 가능한 아키텍처 설계</b>와 <b>모듈형 시스템 구조</b>를 목표로 개발한 로그라이크 액션 게임
</p>

---

## 📌 About Project

<strong>Project Abyss</strong>는  
스테이지 클리어 기반의 로그라이크 액션 게임입니다.

플레이어는 전투를 통해 장비와 퍼즐을 획득하고  
이를 조합하여 캐릭터를 성장시키며 최종 챕터 클리어를 목표로 합니다.

<br>

### 🎯 Core Gameplay

- ⚔️ 액션 중심 전투 구조
- 🧩 퍼즐 기반 능력 강화 시스템
- 🛡 장비 모듈화 성장 구조
- 🔁 로그라이크 반복 플레이 설계

---

# 🛠 Tech Stack

<p>
  <img src="https://img.shields.io/badge/Engine-Unity-000000?style=for-the-badge&logo=unity"/>
  <img src="https://img.shields.io/badge/Language-C%23-239120?style=for-the-badge&logo=c-sharp"/>
  <img src="https://img.shields.io/badge/Input-New%20Input%20System-00599C?style=for-the-badge"/>
  <img src="https://img.shields.io/badge/Resource-Addressables-FF6F00?style=for-the-badge"/>
  <img src="https://img.shields.io/badge/Backend-뒤끝-1E88E5?style=for-the-badge"/>
</p>

---

# 🧠 Architecture & Design

## 1️⃣ Layer FSM 기반 캐릭터 구조

- 플레이어 / 몬스터 Layer 기반 FSM 설계
- 상태 분리 구조로 확장성 확보
- 행동 계층 구조를 통한 유지보수 용이성 확보

---

## 2️⃣ Managers 패턴 기반 시스템 구조

- 전역 매니저 통합 관리 구조 설계
- GameRun 단위 생명주기 분리
- 기능별 책임 분리 구조

```text
Managers
 ├── GameRunManager
 ├── StagePointManager
 ├── RoomManager
 └── Data Managers
