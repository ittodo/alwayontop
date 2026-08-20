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
- 작업 표시줄, 숨겨진 트레이 아이콘, 시작 메뉴와 검색 같은 Windows 시스템 UI는 고정 대상에서 제외
- 중복 실행 방지
- Velopack 설치 및 업데이트 패키지 생성
- GitHub Releases에서 새 버전 자동 확인·다운로드
- 트레이에서 Windows 기본 단축키, 현재 등록 불가한 조합, 이 앱이 등록한 키를 키보드 오버레이와 함께 확인
- 키보드 도식의 일반 키와 보조키를 클릭해 연결된 단축키를 선택하고 아래 목록과 양방향 연동
- 앱 밖에서도 보조키를 잠시 누르면 현재 조합의 단축키를 화면 중앙에 표시
- Win 키를 짧게 누르면 시작 메뉴가 열리고, 길게 눌러 안내를 표시한 뒤 놓으면 시작 메뉴를 열지 않음
- VS Code 연동 확장을 설치하면 활성 편집기·선택 영역·언어·디버깅 상태에서 확실한 단축키를 Windows 목록과 함께 표시
- 항목이 많은 단축키 안내는 추천 항목부터 보여준 뒤 잠시 후 천천히 자동 스크롤
- Windows Terminal이 활성화되면 사용자 `settings.json`에 등록된 복사, 붙여넣기, 검색, 창 분할 등의 실제 키를 표시

## VS Code 연동

트레이 메뉴의 **VS Code 연동...**을 선택하면 함께 제공되는 확장을 설치할 수 있습니다. 설치 후 열려 있는 VS Code에서 **Developer: Reload Window**를 한 번 실행하거나 VS Code를 다시 시작합니다. 이후 VS Code가 활성 상태일 때 보조키를 길게 누르면 현재 컨텍스트 단축키가 보라색으로 구분되어 표시됩니다.

연동 정보는 현재 사용자만 접근할 수 있는 Windows 로컬 파이프로 전달되며 인터넷으로 전송되지 않습니다. 문서 내용은 읽거나 전달하지 않습니다. VS Code의 공개 API로 적용 여부를 확정할 수 없는 `when` 조건은 잘못된 안내를 막기 위해 목록에서 제외합니다.

Windows Terminal은 별도 확장을 설치하지 않습니다. 설치된 버전의 `defaults.json` 전체와 현재 사용자의 `settings.json`을 함께 읽어 실제 기본 키와 사용자 재정의·해제를 병합합니다. `Ctrl + Shift + W`, `Alt + Shift + D`, `Alt + Shift + -`, `Alt + Shift + +`를 포함한 Terminal 단축키를 한국어 설명과 함께 표시하며, 설정 변경은 다음 팝업부터 자동 반영합니다.

관리자 권한으로 실행된 프로그램의 창은 Windows 보안 경계 때문에 일반 권한으로 실행한 이 앱에서 변경하지 못할 수 있습니다. 그런 창을 제어하려면 이 앱도 관리자 권한으로 실행해야 합니다.

## 개발 실행

```powershell
dotnet run --project .\src\TrayAlwaysOnTop\TrayAlwaysOnTop.csproj
```

앱은 별도의 기본 창 없이 알림 영역에서 실행됩니다. 트레이 아이콘을 두 번 클릭해도 직전 활성 창의 고정 상태가 전환됩니다.

## 빌드 및 Velopack 패키징

.NET SDK, Node.js 22와 Velopack CLI 1.2.0이 필요합니다. CLI가 없다면 다음 명령으로 설치합니다.

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

## GitHub 자동 배포와 업데이트

설치된 앱은 시작 5초 후 [GitHub Releases](https://github.com/ittodo/alwayontop/releases)를 확인합니다. 새 버전이 있으면 백그라운드에서 내려받고 다음 앱 시작 때 자동으로 적용합니다. 트레이 메뉴의 **업데이트 확인...**을 선택하면 즉시 확인하거나 준비된 업데이트를 바로 적용할 수 있습니다.

새 버전을 배포하려면 `main`의 배포할 커밋에 버전 태그를 푸시합니다.

```powershell
git tag v1.0.3
git push origin v1.0.3
```

`.github/workflows/release.yml`이 Windows 실행 파일을 빌드하고 Velopack 전체·델타 패키지와 설치 프로그램을 GitHub Release에 자동 게시합니다. 태그는 `v1.2.3` 형태의 SemVer를 사용해야 합니다.
