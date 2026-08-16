namespace TrayAlwaysOnTop;

internal static class WindowsShortcutCatalog
{
    public static IReadOnlyList<WindowsShortcut> Shortcuts { get; } =
    [
        new("Alt + Tab", "열려 있는 앱 사이 전환"),
        new("Alt + F4", "현재 앱 또는 창 닫기"),
        new("Alt + Space", "현재 창의 시스템 메뉴 열기"),
        new("Ctrl + A", "모두 선택"),
        new("Ctrl + C", "복사"),
        new("Ctrl + X", "잘라내기"),
        new("Ctrl + V", "붙여넣기"),
        new("Ctrl + Z", "실행 취소"),
        new("Ctrl + Y", "다시 실행"),
        new("Ctrl + Shift + Esc", "작업 관리자 열기"),
        new("Ctrl + Alt + Delete", "Windows 보안 화면 열기"),
        new("PrintScreen", "전체 화면을 클립보드에 캡처"),
        new("Alt + PrintScreen", "활성 창을 클립보드에 캡처"),
        new("Win + A", "빠른 설정 열기"),
        new("Win + D", "바탕 화면 표시 또는 복원"),
        new("Win + E", "파일 탐색기 열기"),
        new("Win + G", "Xbox Game Bar 열기"),
        new("Win + H", "음성 입력 열기"),
        new("Win + I", "설정 열기"),
        new("Win + K", "캐스트 빠른 설정 열기"),
        new("Win + L", "PC 잠금"),
        new("Win + N", "알림 센터와 일정 열기"),
        new("Win + P", "화면 표시 모드 선택"),
        new("Win + R", "실행 창 열기"),
        new("Win + S", "검색 열기"),
        new("Win + U", "접근성 설정 열기"),
        new("Win + V", "클립보드 기록 열기"),
        new("Win + W", "위젯 열기"),
        new("Win + X", "빠른 링크 메뉴 열기"),
        new("Win + Z", "스냅 레이아웃 열기"),
        new("Win + .", "이모지 패널 열기"),
        new("Win + ,", "바탕 화면 잠시 보기"),
        new("Win + Tab", "작업 보기 열기"),
        new("Win + Space", "입력 언어와 키보드 레이아웃 전환"),
        new("Win + Home", "활성 창을 제외한 모든 창 최소화 또는 복원"),
        new("Win + Left", "창을 화면 왼쪽에 맞춤"),
        new("Win + Right", "창을 화면 오른쪽에 맞춤"),
        new("Win + Up", "활성 창 최대화"),
        new("Win + Down", "활성 창 최소화 또는 복원"),
        new("Win + Shift + S", "화면 캡처 영역 선택"),
        new("Win + Shift + Left", "창을 왼쪽 모니터로 이동"),
        new("Win + Shift + Right", "창을 오른쪽 모니터로 이동"),
        new("Win + Ctrl + D", "새 가상 데스크톱 만들기"),
        new("Win + Ctrl + Left", "왼쪽 가상 데스크톱으로 전환"),
        new("Win + Ctrl + Right", "오른쪽 가상 데스크톱으로 전환"),
        new("Win + Ctrl + F4", "현재 가상 데스크톱 닫기"),
        new("Win + Ctrl + Enter", "내레이터 켜기 또는 끄기"),
        new("Win + Ctrl + Shift + B", "그래픽 드라이버 다시 시작")
    ];
}

internal sealed record WindowsShortcut(string Shortcut, string Description);
