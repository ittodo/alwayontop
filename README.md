# Tray Always On Top

PowerToys의 **Always on Top** 핵심 동작을 작은 Windows 트레이 앱으로 구현한 프로젝트입니다.

## 기능

- 기본 전역 단축키 `Win + Ctrl + T`로 현재 활성 창 고정/해제
- 트레이 메뉴에서 직전에 사용한 창 고정/해제
- 트레이의 **열린 창 선택** 메뉴에서 원하는 창을 직접 선택
- 고정 창에 파란색 테두리 표시
- 고정 창 제목 표시줄에 클릭 가능한 핀 토글 표시
- 단축키, 테두리, 알림 설정 저장
- 설치 후 Windows 로그인 시 자동 실행(기본값: 켜짐)
- 앱을 끝낼 때 이 앱이 고정한 창을 원래 상태로 복원
- 중복 실행 방지
- Velopack 설치 및 업데이트 패키지 생성

관리자 권한으로 실행된 프로그램의 창은 Windows 보안 경계 때문에 일반 권한으로 실행한 이 앱에서 변경하지 못할 수 있습니다. 그런 창을 제어하려면 이 앱도 관리자 권한으로 실행해야 합니다.

## 개발 실행

```powershell
dotnet run --project .\src\TrayAlwaysOnTop\TrayAlwaysOnTop.csproj
```

앱은 별도의 기본 창 없이 알림 영역에서 실행됩니다. 트레이 아이콘을 두 번 클릭해도 직전 활성 창의 고정 상태가 전환됩니다.

## 빌드 및 Velopack 패키징

.NET SDK와 Velopack CLI 1.2.0이 필요합니다. CLI가 없다면 다음 명령으로 설치합니다.

```powershell
dotnet tool install -g vpk --version 1.2.0
```

설치 패키지를 생성합니다.

```powershell
.\build.ps1 -Version 1.0.0 -Runtime win-x64
```

결과는 `artifacts\releases`에 생성됩니다.

- `TrayAlwaysOnTop-win-Setup.exe`: 사용자별 설치 프로그램
- `TrayAlwaysOnTop-버전-full.nupkg`: 전체 업데이트 패키지
- `releases.win.json`: Velopack 업데이트 피드 메타데이터

후속 버전을 만들 때는 이전 `artifacts\releases` 폴더를 유지한 채 버전만 올려 실행하면 Velopack이 필요한 릴리스 파일을 갱신합니다.
