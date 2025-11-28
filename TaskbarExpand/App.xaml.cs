using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TaskbarExpand
{
    /// <summary>
    /// App.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarExpand",
            "crash.log"
        );

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 로그 디렉토리 생성
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

            // 전역 예외 핸들러 등록
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            WriteLog("앱 시작됨");
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            WriteLog($"[FATAL] 처리되지 않은 예외:\n{ex?.ToString() ?? "알 수 없는 오류"}");
            MessageBox.Show($"치명적 오류 발생:\n{ex?.Message}\n\n로그 위치: {LogPath}", "TaskbarExpand 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteLog($"[ERROR] UI 스레드 예외:\n{e.Exception}");
            MessageBox.Show($"오류 발생:\n{e.Exception.Message}\n\n로그 위치: {LogPath}", "TaskbarExpand 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // 앱 종료 방지
        }

        public static void WriteLog(string message)
        {
            try
            {
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                File.AppendAllText(LogPath, logMessage);
            }
            catch { }
        }
    }
}
