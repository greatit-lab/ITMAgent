// ITM_Agent/Program.cs
using ITM_Agent.Common.Interfaces;
using ITM_Agent.Core;
using ITM_Agent.Forms; // Form 클래스들의 네임스페이스
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ITM_Agent
{
    internal static class Program
    {
        private static Mutex _mutex = null;
        private const string AppGuid = "c0a76b5a-12ab-45c5-b9d9-d693faa6e7b9"; // 고유 ID

        [STAThread]
        static void Main()
        {
            // 애플리케이션 중복 실행 방지
            _mutex = new Mutex(true, AppGuid, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("ITM Agent가 이미 실행 중입니다.", "실행 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // --- 의존성 주입(Dependency Injection)을 위한 서비스 인스턴스 생성 ---
            // 로그 매니저를 가장 먼저 생성하여 프로그램 시작부터 로깅 가능하도록 함
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            ILogManager logManager = new LogManager(baseDir);
            logManager.LogEvent("==================================================");
            logManager.LogEvent("Application starting...");

            // 처리되지 않은 예외에 대한 글로벌 핸들러
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                logManager.LogError($"[CRITICAL] Unhandled Exception: {(e.ExceptionObject as Exception)?.ToString()}");
                MessageBox.Show("치명적인 오류가 발생했습니다. 프로그램을 종료합니다. 로그 파일을 확인해주세요.", "Unhandled Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // OS UI 언어에 따른 문화권 설정
            var uiCulture = CultureInfo.CurrentUICulture;
            if (!uiCulture.Name.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                logManager.LogDebug("UI culture set to en-US.");
            }

            // 'Library' 폴더의 외부 DLL을 동적으로 로드하기 위한 AssemblyResolve 이벤트 핸들러
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                string libraryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library", assemblyName);
                logManager.LogDebug($"AssemblyResolve trying to load: {assemblyName} from {libraryPath}");
                return File.Exists(libraryPath) ? Assembly.LoadFrom(libraryPath) : null;
            };

            string settingsPath = Path.Combine(baseDir, "Settings.ini");

            logManager.LogDebug("Initializing services for Dependency Injection...");
            ISettingsManager settingsManager = new SettingsManager(settingsPath);
            logManager.LogDebug("SettingsManager initialized.");

            // EqpidManager에 주입할 UI 동작 정의
            Func<(string Eqpid, string Type)> promptForEqpidAction = () =>
            {
                using (var form = new EqpidInputForm())
                {
                    if (form.ShowDialog() == DialogResult.OK)
                    {
                        return (form.Eqpid, form.Type);
                    }
                    return (null, null); // 사용자가 취소한 경우
                }
            };

            Action handleCanceledAction = () =>
            {
                MessageBox.Show("Eqpid 입력이 취소되었습니다. 애플리케이션을 종료합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                logManager.LogEvent("Eqpid input was canceled by user. Application will exit.");
                Environment.Exit(0);
            };

            var eqpidManager = new EqpidManager(settingsManager, logManager, "v1.0.0", promptForEqpidAction, handleCanceledAction);
            logManager.LogDebug("EqpidManager initialized.");

            var fileWatcherManager = new FileWatcherManager(settingsManager, logManager);
            logManager.LogDebug("FileWatcherManager initialized.");

            var infoCleaner = new InfoRetentionCleaner(settingsManager, logManager);
            logManager.LogDebug("InfoRetentionCleaner initialized.");

            logManager.LogEvent("All services initialized. Starting MainForm.");
            // MainForm에 모든 서비스 인스턴스 주입
            Application.Run(new MainForm(settingsManager, logManager, eqpidManager, fileWatcherManager, infoCleaner));

            // 프로그램 종료 시 Mutex 해제
            _mutex?.ReleaseMutex();
            logManager.LogEvent("Application shutting down.");
            logManager.LogEvent("==================================================");
        }
    }
}
