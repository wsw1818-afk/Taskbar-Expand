# Taskbar Expand - Windows 11 보조 작업 표시줄

Windows 11에서 실행 중인 모든 창을 세로 바 형태로 표시하고 빠르게 전환할 수 있는 보조 작업 표시줄 애플리케이션입니다.

## 주요 기능

### 핵심 기능
- **실시간 창 목록 표시**: 현재 실행 중인 모든 활성 윈도우를 실시간으로 표시
- **빠른 창 전환**: 리스트에서 더블클릭으로 해당 창을 즉시 활성화
- **자동 갱신**: Shell Hook을 통해 창이 열리거나 닫힐 때 자동으로 목록 갱신
- **스마트 필터링**: 백그라운드 프로세스, 시스템 창, 숨김 창 등을 자동으로 제외

### UI 특징
- 모니터 오른쪽 가장자리에 세로 바 형태로 고정
- 다크 테마 기반의 현대적인 디자인
- 창 제목과 아이콘을 함께 표시
- 항상 최상위에 고정 (Topmost)
- 작업 표시줄에 표시되지 않음 (ShowInTaskbar=False)

## 시스템 요구사항

- **운영체제**: Windows 11 (Windows 10에서도 동작 가능)
- **.NET Runtime**: .NET 8.0 이상
- **해상도**: 1920x1080 이상 권장

## 설치 및 실행

### 빌드 방법
```bash
# 1. 프로젝트 클론 또는 다운로드
cd "H:\Claude_work\Taskbar Expand\TaskbarExpand"

# 2. Release 빌드
dotnet build -c Release

# 3. 실행 파일 생성
cd bin\Release\net8.0-windows
```

### 실행
```bash
# 빌드 결과물 폴더에서
TaskbarExpand.exe
```

## 사용 방법

### 기본 조작
1. **애플리케이션 실행**: `TaskbarExpand.exe` 더블클릭
2. **창 활성화**: 목록에서 원하는 창을 더블클릭
3. **목록 새로고침**: 우측 상단의 🔄 버튼 클릭 또는 `F5` 키
4. **창 이동**: 타이틀 바를 드래그하여 위치 변경
5. **프로그램 종료**: 우측 상단의 ✕ 버튼 클릭

### 자동 실행 설정 (선택 사항)
Windows 시작 시 자동 실행되도록 설정:

1. `Win + R` → `shell:startup` 입력
2. TaskbarExpand.exe의 바로가기를 시작 프로그램 폴더에 복사

## 필터링 로직

아래 조건을 만족하는 창만 목록에 표시됩니다:

### 포함 조건
- 화면에 보이는 창 (`IsWindowVisible = true`)
- 제목이 있는 창 (빈 제목 제외)
- WS_CAPTION 또는 WS_SYSMENU 스타일이 있는 창
- 소유자(Owner)가 없는 독립 창

### 제외 조건
- **시스템 창**: 작업 표시줄, 시작 메뉴, 알림, 바탕화면 등
- **백그라운드 프로세스**: 창 제목이 없는 프로세스
- **도구 창**: WS_EX_TOOLWINDOW 스타일 (단, WS_EX_APPWINDOW는 예외)
- **활성화 불가 창**: WS_EX_NOACTIVATE 스타일
- **자식 대화상자**: 소유자가 있는 창 (단, 소유자가 숨겨진 경우 예외)

### 제외되는 주요 클래스
- `Shell_TrayWnd` (작업 표시줄)
- `DV2ControlHost` (시작 메뉴)
- `WorkerW`, `Progman` (바탕화면)
- `ApplicationFrameWindow` (UWP 빈 프레임)
- `SysShadow` (그림자 효과)

## 기술 스택

### 개발 환경
- **언어**: C# 12
- **프레임워크**: WPF (.NET 8.0)
- **IDE**: Visual Studio 2022 / JetBrains Rider

### 핵심 기술
- **Win32 API (User32.dll)**
  - `EnumWindows`: 창 열거
  - `IsWindowVisible`: 가시성 확인
  - `SetForegroundWindow`: 창 활성화
  - `RegisterShellHookWindow`: Shell Hook 이벤트 등록
  - `GetWindowLongPtr`: 창 스타일 조회

- **WPF 데이터 바인딩**
  - `ObservableCollection<T>`: 실시간 UI 업데이트
  - `INotifyPropertyChanged`: 속성 변경 알림

- **HwndSource**: WPF에서 Win32 메시지 처리

## 프로젝트 구조

```
TaskbarExpand/
├── NativeMethods.cs       # Win32 API P/Invoke 선언
├── WindowInfo.cs          # 창 정보 모델 및 필터링 로직
├── MainWindow.xaml        # UI 레이아웃 (XAML)
├── MainWindow.xaml.cs     # 비즈니스 로직 (C#)
├── App.xaml               # 애플리케이션 진입점
├── App.xaml.cs            # 애플리케이션 코드 비하인드
└── TaskbarExpand.csproj   # 프로젝트 파일
```

## 주요 클래스 설명

### NativeMethods.cs
Win32 API 함수들의 P/Invoke 선언을 담당합니다.
- **창 열거**: `EnumWindows`, `EnumWindowsProc`
- **창 속성 조회**: `GetWindowText`, `GetClassName`, `GetWindowLongPtr`
- **창 제어**: `SetForegroundWindow`, `ShowWindow`, `IsIconic`
- **이벤트 감지**: `RegisterShellHookWindow`, `RegisterWindowMessage`

### WindowInfo.cs
창 정보를 담는 모델 클래스입니다.
- **속성**: `Handle`, `Title`, `Icon`, `ProcessName`, `ProcessId`
- **핵심 메서드**:
  - `FromHandle(IntPtr)`: 창 핸들로부터 정보 추출
  - `IsValidTaskbarWindow(IntPtr)`: 유효한 작업 표시줄 창인지 필터링

### MainWindow.xaml.cs
메인 윈도우의 로직을 담당합니다.
- **Shell Hook 메시지 처리**: `WndProc` 메서드
- **창 목록 갱신**: `RefreshWindowList` 메서드
- **창 활성화**: `ActivateWindow` 메서드
- **이벤트 핸들러**: 더블클릭, 새로고침, 드래그 등

## 개선 아이디어

### 계획된 기능
- [ ] 창 검색 기능 (텍스트 필터링)
- [ ] 즐겨찾기 기능 (고정 창 목록)
- [ ] 키보드 단축키 지원 (Ctrl+1~9로 빠른 전환)
- [ ] 테마 변경 (라이트/다크 모드 토글)
- [ ] 창 그룹화 (프로세스별 또는 수동)
- [ ] 미리보기 기능 (호버 시 창 썸네일 표시)
- [ ] 창 닫기 버튼 추가 (리스트에서 바로 종료)
- [ ] 설정 창 (폭, 위치, 테마 등)

### 성능 최적화
- [ ] 아이콘 캐싱 (동일 프로세스의 아이콘 재사용)
- [ ] 가상화 (많은 창이 있을 때 UI 가상화 적용)
- [ ] Debounce 적용 (짧은 시간 내 연속 갱신 방지)

## 라이선스

MIT License

## 제작자

Claude AI (Anthropic)

## 버전 히스토리

### v1.0.0 (2025-11-28)
- 초기 릴리스
- 기본 창 목록 표시 및 전환 기능
- Shell Hook 기반 실시간 갱신
- 스마트 필터링 로직 구현
- 다크 테마 UI

## 트러블슈팅

### 프로그램이 실행되지 않아요
- .NET 8.0 런타임이 설치되어 있는지 확인하세요
- Windows 버전이 Windows 10 이상인지 확인하세요

### 일부 창이 목록에 표시되지 않아요
- 해당 창이 실제로 "작업 표시줄에 표시 가능한 창"인지 확인하세요
- 백그라운드 프로세스나 시스템 창은 의도적으로 제외됩니다

### 목록이 자동으로 갱신되지 않아요
- F5 키를 눌러 수동 새로고침을 시도하세요
- Shell Hook 등록이 실패했을 수 있습니다 (프로그램 재시작)

### 창을 더블클릭했는데 활성화되지 않아요
- 일부 관리자 권한 프로세스는 일반 권한에서 제어할 수 없습니다
- 프로그램을 관리자 권한으로 실행해보세요

## 기여 방법

이 프로젝트는 Claude AI가 생성한 오픈소스 프로젝트입니다.
개선 아이디어나 버그 리포트는 GitHub Issues를 통해 제출해주세요.

---

**제작 배경**: Windows 11의 작업 표시줄은 공간이 좁아서 많은 창을 동시에 열면 찾기 어렵습니다. 이 프로그램은 재미나이 3.0의 아이디어를 기반으로 Claude AI가 보완하여 제작한 보조 도구입니다.
