# Procedural 3D Map Baker

## 시연 영상

[![Backrooms-like Map Generation in Unity (WIP)](https://img.youtube.com/vi/0ACLtyFWc5w/maxresdefault.jpg)](https://www.youtube.com/watch?v=0ACLtyFWc5w)

## 프로젝트 소개

- **게임**: 백룸풍 실내 공간에서 괴물 1명과 생존자 3명이 대결하는 비대칭 서바이벌 PvP. 생존자는 제한 시간 안에 탈출해야 하고, 괴물은 이를 막아야 합니다.
- **개발**: 5인 팀 (기획 2 · 프로그래밍 2 · 아트 1) / 프로그래머로 참여 · 팀 프로젝트 코드명 ProjectFS
- **환경**: Unity 6 (URP Render Graph), C#, FishNet
- **기간**: 2026.09
- 이 저장소는 팀 프로젝트 중 **본인이 담당한 코드**만 발췌했습니다. 2D 절차적 맵 생성([Asymmetric-PvP-FishNet](https://github.com/Youngdu-Min/Asymmetric-PvP-FishNet))을 3D 맵으로 전환한 작업입니다.

## 구현 범위

- **3D 맵 베이크**: 2D 그리드 생성 결과를 3D 모듈러 프리팹(바닥 · 벽 · 문틀 · 천장)으로 배치
- **맵 종류별 가중치**: 모듈 프리팹을 가중 랜덤으로 골라 빈칸 없이 채움
- **LevelModule 툴**: 모듈러 프리팹 제작 규격 검증, 에셋 이름을 해석해 래퍼 프리팹을 일괄 생성하는 에디터 툴
- **Convolution 포스트 프로세싱**: URP Render Graph 기반 커널 합성곱 필터로 화면 연출

## 코드 구성

| 폴더 | 설명 |
|---|---|
| `01_LevelGeneration_3D` | `GridToWorld`: 그리드 좌표를 월드 좌표로 변환합니다. `PrefabBaker`: 바닥 → 벽 2단계 배치. `PrefabBakeTestRunner`: 경계 방향별 벽·문틀 배치, 크루스칼 방식으로 고립되는 짧은 경계만 개방, 가중 랜덤 모듈 채움, 통행 불가 모듈 영역 제한, 전용 난수로 배치 순서 보호 |
| `02_LevelModule` | `LevelModule`: 피벗·마감면·돌출 허용치 같은 모듈 규격을 검사합니다. `Editor/LevelModuleWrapperGenerator`: 에셋 이름(크기·종류)을 해석해 래퍼 프리팹을 일괄 생성·갱신합니다. |
| `03_ConvolutionFilter` | Renderer Feature, Render Graph Pass, 커널 정의(3x3 / 5x5, 흑백 변환 옵션), 합성곱 셰이더 |

## 기술 문서

모듈 규격과 래퍼 자동 생성, 바닥 패킹, 벽·문틀 배치(이음선·모서리·끝단 캡), Union-Find 기반 짧은 경계 처리, NavMesh, 호스트/클라이언트 결정적 배치, Render Graph 합성곱 필터는 [기술 문서](docs/TechnicalDesign.md)에 정리했습니다.

## 참고 자료

- Convolution 커널 종류와 가중치: Jephraim Manansala, [Image Processing with Python: Image Effects using Convolutional Filters and Kernels](https://medium.com/swlh/image-processing-with-python-convolutional-filters-and-kernels-b9884d91a8fd), The Startup, 2021-01-28
