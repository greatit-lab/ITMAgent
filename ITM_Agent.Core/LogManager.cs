// ITM_Agent.Core/LogManager.cs
using ITM_Agent.Common.Interfaces;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ITM_Agent.Core
{
    /// <summary>
    /// ILogManager 인터페이스를 구현하는 로깅 서비스 클래스입니다.
    /// 이벤트, 에러, 디버그 로그를 파일로 기록하며, 로그 파일 크기에 따른 자동 회전 기능을 담당합니다.
    /// </summary>
    public class LogManager : ILogManager
    {
        private readonly string _logFolderPath;
        private const long MAX_LOG_SIZE = 5 * 1024 * 1024; // 5MB

        /// <summary>
        /// 애플리케이션 전역에서 공유되는 디버그 모드 활성화 플래그입니다.
        /// </summary>
        public static bool GlobalDebugEnabled { get; set; } = false;

        /// <summary>
        /// LogManager의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="baseDirectory">로그 파일이 저장될 'Logs' 폴더의 상위 기본 디렉터리입니다.</param>
        public LogManager(string baseDirectory)
        {
            _logFolderPath = Path.Combine(baseDirectory, "Logs");
            Directory.CreateDirectory(_logFolderPath);
        }

        #region --- Public Log Methods ---

        /// <summary>
        /// 일반적인 정보성 이벤트 로그를 기록합니다.
        /// </summary>
        public void LogEvent(string message)
        {
            string logLine = FormatLogLine("Event", message);
            WriteLogWithRotation(logLine, $"{DateTime.Now:yyyyMMdd}_event.log");
        }

        /// <summary>
        /// 오류 상황에 대한 로그를 기록합니다.
        /// </summary>
        public void LogError(string message)
        {
            string logLine = FormatLogLine("Error", message);
            WriteLogWithRotation(logLine, $"{DateTime.Now:yyyyMMdd}_error.log");
        }

        /// <summary>
        /// 디버그 모드가 활성화된 경우에만 상세 개발 로그를 기록합니다.
        /// </summary>
        public void LogDebug(string message)
        {
            if (!GlobalDebugEnabled) return;
            string logLine = FormatLogLine("Debug", message);
            WriteLogWithRotation(logLine, $"{DateTime.Now:yyyyMMdd}_debug.log");
        }

        /// <summary>
        /// 사용자 정의 로그 타입을 지정하여 기록합니다.
        /// </summary>
        /// <param name="message">기록할 메시지입니다.</param>
        /// <param name="logType">로그 파일명에 사용될 로그 타입입니다. (예: "info")</param>
        public void LogCustom(string message, string logType)
        {
            string logLine = FormatLogLine(logType.ToUpper(), message);
            WriteLogWithRotation(logLine, $"{DateTime.Now:yyyyMMdd}_{logType.ToLower()}.log");
        }

        #endregion

        #region --- Private Helper Methods ---

        /// <summary>
        /// 로그 메시지에 타임스탬프와 타입을 추가하여 최종 로그 라인을 포맷합니다.
        /// </summary>
        private string FormatLogLine(string type, string message)
        {
            return string.Format("{0} [{1}] {2}",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                type,
                message);
        }

        /// <summary>
        /// 로그 파일 크기를 확인하여 필요시 회전시킨 후, 파일에 로그를 기록합니다.
        /// 파일 잠금 충돌을 대비해 짧은 재시도 로직을 포함합니다.
        /// </summary>
        private void WriteLogWithRotation(string message, string fileName)
        {
            string filePath = Path.Combine(_logFolderPath, fileName);

            try
            {
                RotateLogFileIfNeeded(filePath);

                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // FileShare.ReadWrite 옵션으로 다른 프로세스와의 동시 접근 허용
                        using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        using (var sw = new StreamWriter(fs, Encoding.UTF8))
                        {
                            sw.WriteLine(message);
                        }
                        return; // 쓰기 성공 시 종료
                    }
                    catch (IOException) when (attempt < maxRetries)
                    {
                        Thread.Sleep(100); // 짧은 대기 후 재시도
                    }
                }
            }
            catch (Exception ex)
            {
                // 재시도 후에도 실패 시 콘솔에 에러 출력
                Console.WriteLine($"Failed to write log to {filePath} after retries: {ex.Message}");
            }
        }

        /// <summary>
        /// 로그 파일이 최대 크기(5MB)를 초과하면 새 번호를 붙여 백업 파일로 이동시킵니다.
        /// (예: event.log -> event_1.log)
        /// </summary>
        private void RotateLogFileIfNeeded(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length <= MAX_LOG_SIZE) return;

            string directory = Path.GetDirectoryName(filePath);
            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            int index = 1;
            string rotatedPath;
            do
            {
                rotatedPath = Path.Combine(directory, string.Format("{0}_{1}{2}", baseName, index++, extension));
            } while (File.Exists(rotatedPath));

            try
            {
                File.Move(filePath, rotatedPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to rotate log file {filePath}: {ex.Message}");
            }
        }

        #endregion

        #region --- Static Plugin-Related Methods ---

        /// <summary>
        /// 로드된 모든 플러그인(어셈블리)에 디버그 모드 상태를 전파합니다.
        /// 플러그인 내에 'SetDebugMode(bool)' 또는 'SetDebug(bool)' 정적 메서드가 있는 경우 이를 호출합니다.
        /// </summary>
        public static void BroadcastPluginDebug(bool enabled)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(t => t != null).ToArray();
                    }

                    if (types == null) continue;

                    foreach (var type in types)
                    {
                        if (!type.IsClass) continue;

                        MethodInfo method = type.GetMethod("SetDebugMode", BindingFlags.Public | BindingFlags.Static)
                                         ?? type.GetMethod("SetDebug", BindingFlags.Public | BindingFlags.Static);

                        if (method != null)
                        {
                            var parameters = method.GetParameters();
                            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                            {
                                try
                                {
                                    method.Invoke(null, new object[] { enabled });
                                }
                                catch { /* 특정 플러그인 호출 실패는 무시 */ }
                            }
                        }
                    }
                }
            }
            catch { /* 전체 브로드캐스트 실패는 무시 */ }
        }

        #endregion
    }
}