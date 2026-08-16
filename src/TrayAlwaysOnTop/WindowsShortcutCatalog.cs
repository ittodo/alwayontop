namespace TrayAlwaysOnTop;

internal static class WindowsShortcutCatalog
{
    public static IReadOnlyList<WindowsShortcut> Shortcuts { get; } =
    [
        new(HotKeyModifiers.Alt, Keys.Tab, "열려 있는 앱 사이 전환"),
        new(HotKeyModifiers.Alt, Keys.F4, "현재 앱 또는 창 닫기"),
        new(HotKeyModifiers.Alt, Keys.Space, "현재 창의 시스템 메뉴 열기"),
        new(HotKeyModifiers.Control, Keys.A, "모두 선택"),
        new(HotKeyModifiers.Control, Keys.C, "복사"),
        new(HotKeyModifiers.Control, Keys.X, "잘라내기"),
        new(HotKeyModifiers.Control, Keys.V, "붙여넣기"),
        new(HotKeyModifiers.Control, Keys.Z, "실행 취소"),
        new(HotKeyModifiers.Control, Keys.Y, "다시 실행"),
        new(HotKeyModifiers.Control | HotKeyModifiers.Shift, Keys.Escape, "작업 관리자 열기"),
        new(HotKeyModifiers.Control | HotKeyModifiers.Alt, Keys.Delete, "Windows 보안 화면 열기"),
        new(HotKeyModifiers.None, Keys.Snapshot, "전체 화면을 클립보드에 캡처"),
        new(HotKeyModifiers.Alt, Keys.Snapshot, "활성 창을 클립보드에 캡처"),
        new(HotKeyModifiers.Win, Keys.A, "빠른 설정 열기"),
        new(HotKeyModifiers.Win, Keys.D, "바탕 화면 표시 또는 복원"),
        new(HotKeyModifiers.Win, Keys.E, "파일 탐색기 열기"),
        new(HotKeyModifiers.Win, Keys.G, "Xbox Game Bar 열기"),
        new(HotKeyModifiers.Win, Keys.H, "음성 입력 열기"),
        new(HotKeyModifiers.Win, Keys.I, "설정 열기"),
        new(HotKeyModifiers.Win, Keys.K, "캐스트 빠른 설정 열기"),
        new(HotKeyModifiers.Win, Keys.L, "PC 잠금"),
        new(HotKeyModifiers.Win, Keys.N, "알림 센터와 일정 열기"),
        new(HotKeyModifiers.Win, Keys.P, "화면 표시 모드 선택"),
        new(HotKeyModifiers.Win, Keys.R, "실행 창 열기"),
        new(HotKeyModifiers.Win, Keys.S, "검색 열기"),
        new(HotKeyModifiers.Win, Keys.U, "접근성 설정 열기"),
        new(HotKeyModifiers.Win, Keys.V, "클립보드 기록 열기"),
        new(HotKeyModifiers.Win, Keys.W, "위젯 열기"),
        new(HotKeyModifiers.Win, Keys.X, "빠른 링크 메뉴 열기"),
        new(HotKeyModifiers.Win, Keys.Z, "스냅 레이아웃 열기"),
        new(HotKeyModifiers.Win, Keys.OemPeriod, "이모지 패널 열기"),
        new(HotKeyModifiers.Win, Keys.Oemcomma, "바탕 화면 잠시 보기"),
        new(HotKeyModifiers.Win, Keys.Tab, "작업 보기 열기"),
        new(HotKeyModifiers.Win, Keys.Space, "입력 언어와 키보드 레이아웃 전환"),
        new(HotKeyModifiers.Win, Keys.Home, "활성 창을 제외한 모든 창 최소화 또는 복원"),
        new(HotKeyModifiers.Win, Keys.Left, "창을 화면 왼쪽에 맞춤"),
        new(HotKeyModifiers.Win, Keys.Right, "창을 화면 오른쪽에 맞춤"),
        new(HotKeyModifiers.Win, Keys.Up, "활성 창 최대화"),
        new(HotKeyModifiers.Win, Keys.Down, "활성 창 최소화 또는 복원"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Shift, Keys.S, "화면 캡처 영역 선택"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Shift, Keys.Left, "창을 왼쪽 모니터로 이동"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Shift, Keys.Right, "창을 오른쪽 모니터로 이동"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Control, Keys.D, "새 가상 데스크톱 만들기"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Control, Keys.Left, "왼쪽 가상 데스크톱으로 전환"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Control, Keys.Right, "오른쪽 가상 데스크톱으로 전환"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Control, Keys.F4, "현재 가상 데스크톱 닫기"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Control, Keys.Return, "내레이터 켜기 또는 끄기"),
        new(HotKeyModifiers.Win | HotKeyModifiers.Control | HotKeyModifiers.Shift, Keys.B, "그래픽 드라이버 다시 시작")
    ];
}

internal sealed record WindowsShortcut(HotKeyModifiers Modifiers, Keys Key, string Description)
{
    public string Shortcut => HotKeyFormatter.Format(Modifiers, Key);
}
