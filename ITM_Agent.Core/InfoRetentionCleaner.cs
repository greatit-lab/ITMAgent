// ITM_Agent.Core/InfoRetentionCleaner.cs
using ITM_Agent.Common.Interfaces;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace ITM_Agent.Core
{
    /// <summary>
    /// 설정된 보존 기간에 따라 오래된 파일을 주기적으로 삭제하는 서비스 클래스입니다.
    /// Baseline 폴더의 *.info 파일 및 하위 폴더의 날짜 패턴 파일들을 대상으로 합니다.
    /// </summary>
    public sealed class InfoRetentionCleaner : IDisposable
    {
        private readonly ISettingsManager _settings;
        private readonly ILogManager _log;
        private readonly Timer _timer;

        // 파일명에서 날짜/시간 패턴을 추출하기 위한 정규식
        private static readonly Regex TsRegex = new Regex(@"^(?<ts>\d{8}_\d{6})_", RegexOptions.Compiled);
        private static readonly Regex RxYmdHms = new Regex(@"(?<!\d)(?<ymd>\d{8})_(?<hms>\d{6})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex RxHyphen = new Regex(@"(?<!\d)(?<date>\d{4}-\d{2}-\d{2})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex RxYmd = new Regex(@"(?<!\d)(?<ymd>\d{8})(?!\d)", RegexOptions.Compiled);

        // 테스트를 위해 5분 간격으로 설정 (기존 코드 유지)
        private const int SCAN_INTERVAL_MS = 5 * 60 * 1000;

        public InfoRetentionCleaner(ISettingsManager settings, ILogManager logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = logger ?? throw new ArgumentNullException(nameof(logger));

            // 생성 시 즉시 1회 실행 후, 설정된 간격으로 주기적 실행
            _timer = new Timer(_ => ExecuteCleaning(), null, 0, SCAN_INTERVAL_MS);
        }

        /// <summary>
        /// 파일 삭제 작업을 실행합니다.
        /// </summary>
        private void ExecuteCleaning()
        {
            if (!_settings.IsInfoDeletionEnabled) return;

            int retentionDays = _settings.InfoRetentionDays;
            if (retentionDays <= 0) return;

            string baseFolder = _settings.GetBaseFolder();
            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
            {
                _log.LogDebug("[InfoCleaner] BaseFolder is not set or does not exist. Skipping cleanup.");
                return;
            }

            _log.LogDebug($"[InfoCleaner] Starting cleanup task with {retentionDays} days retention.");

            // 1. Baseline 폴더의 .info 파일 정리
            CleanBaselineFolder(baseFolder, retentionDays);

            // 2. BaseFolder 하위 모든 폴더의 날짜 패턴 파일 정리
            CleanFolderRecursively(baseFolder, retentionDays);

            _log.LogDebug("[InfoCleaner] Cleanup task finished.");
        }

        private void CleanBaselineFolder(string baseFolder, int days)
        {
            string baselineDir = Path.Combine(baseFolder, "Baseline");
            if (!Directory.Exists(baselineDir)) return;

            DateTime now = DateTime.Now;

            try
            {
                foreach (string file in Directory.EnumerateFiles(baselineDir, "*.info"))
                {
                    string name = Path.GetFileName(file);
                    Match m = TsRegex.Match(name);
                    if (!m.Success) continue;

                    if (DateTime.TryParseExact(m.Groups["ts"].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime ts))
                    {
                        if ((now - ts).TotalDays >= days)
                        {
                            TryDeleteFile(file, name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"[InfoCleaner] Error while cleaning Baseline folder: {ex.Message}");
            }
        }

        private void CleanFolderRecursively(string rootDir, int days)
        {
            DateTime today = DateTime.Today;
            try
            {
                foreach (var file in Directory.EnumerateFiles(rootDir, "*.*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(file);
                    DateTime? fileDate = TryExtractDateFromFileName(name);

                    if (fileDate.HasValue)
                    {
                        if ((today - fileDate.Value.Date).TotalDays >= days)
                        {
                            TryDeleteFile(file, name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"[InfoCleaner] Error during recursive clean of {rootDir}: {ex.Message}");
            }
        }

        private static DateTime? TryExtractDateFromFileName(string fileName)
        {
            // 1) yyyyMMdd_HHmmss -> yyyyMMdd
            var m1 = RxYmdHms.Match(fileName);
            if (m1.Success && DateTime.TryParseExact(m1.Groups["ymd"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d1))
                return d1.Date;

            // 2) yyyy-MM-dd
            var m2 = RxHyphen.Match(fileName);
            if (m2.Success && DateTime.TryParseExact(m2.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d2))
                return d2.Date;

            // 3) yyyyMMdd
            var m3 = RxYmd.Match(fileName);
            if (m3.Success && DateTime.TryParseExact(m3.Groups["ymd"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d3))
                return d3.Date;

            return null;
        }

        /// <summary>
        /// 파일 삭제를 시도하고, 읽기 전용 속성 해제 및 예외 처리를 포함합니다.
        /// </summary>
        private void TryDeleteFile(string filePath, string displayName)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _log.LogDebug($"[InfoCleaner] Skip (already removed): {displayName}");
                    return;
                }

                FileAttributes attrs = File.GetAttributes(filePath);
                if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                }

                File.Delete(filePath);
                _log.LogEvent($"[InfoCleaner] Deleted old file: {displayName}");
            }
            catch (UnauthorizedAccessException)
            {
                _log.LogError($"[InfoCleaner] Delete failed (Unauthorized): {displayName}. Retrying after setting attributes.");
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                    File.Delete(filePath);
                    _log.LogEvent($"[InfoCleaner] Deleted old file after attribute change: {displayName}");
                }
                catch (Exception ex2)
                {
                    _log.LogError($"[InfoCleaner] Delete retry failed for {displayName}: {ex2.Message}");
                }
            }
            catch (IOException ex)
            {
                _log.LogError($"[InfoCleaner] Delete failed (IO Exception) for {displayName}: {ex.Message}");
            }
            catch (Exception ex)
            {
                _log.LogError($"[InfoCleaner] An unexpected error occurred while deleting {displayName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Timer 리소스를 해제합니다.
        /// </summary>
        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}