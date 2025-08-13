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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // OS UI 언어에 따른 문화권 설정
            var uiCulture = CultureInfo.CurrentUICulture;
            if (!uiCulture.Name.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            }

            // 'Library' 폴더의 외부 DLL을 동적으로 로드하기 위한 AssemblyResolve 이벤트 핸들러
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                string libraryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library", assemblyName);
                return File.Exists(libraryPath) ? Assembly.LoadFrom(libraryPath) : null;
            };

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string settingsPath = Path.Combine(baseDir, "Settings.ini");

            // --- 의존성 주입(Dependency Injection)을 위한 서비스 인스턴스 생성 ---
            ISettingsManager settingsManager = new SettingsManager(settingsPath);
            ILogManager logManager = new LogManager(baseDir);

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
                Environment.Exit(0);
            };

            // EqpidManager 생성자에 UI 동작 전달
            var eqpidManager = new EqpidManager(settingsManager, logManager, "v1.0.0", promptForEqpidAction, handleCanceledAction);
            var fileWatcherManager = new FileWatcherManager(settingsManager, logManager);
            var infoCleaner = new InfoRetentionCleaner(settingsManager, logManager);

            // MainForm에 모든 서비스 인스턴스 주입
            Application.Run(new MainForm(settingsManager, logManager, eqpidManager, fileWatcherManager, infoCleaner));

            // 프로그램 종료 시 Mutex 해제
            _mutex?.ReleaseMutex();
        }
    }
}