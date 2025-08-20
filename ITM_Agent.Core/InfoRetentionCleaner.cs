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

        // 5분 간격으로 실행
        private const int SCAN_INTERVAL_MS = 5 * 60 * 1000;

        public InfoRetentionCleaner(ISettingsManager settings, ILogManager logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _log = logger ?? throw new ArgumentNullException(nameof(logger));

            // 생성 시 즉시 1회 실행 후, 설정된 간격으로 주기적 실행
            _log.LogEvent("[InfoCleaner] Service initialized. Starting cleaning timer.");
            _timer = new Timer(_ => ExecuteCleaning(), null, 0, SCAN_INTERVAL_MS);
        }

        /// <summary>
        /// 파일 삭제 작업을 실행합니다.
        /// </summary>
        private void ExecuteCleaning()
        {
            _log.LogDebug("[InfoCleaner] Timer ticked. Executing cleaning task...");

            if (!_settings.IsInfoDeletionEnabled)
            {
                _log.LogDebug("[InfoCleaner] Auto-deletion is disabled in settings. Skipping cleanup.");
                return;
            }

            int retentionDays = _settings.InfoRetentionDays;
            if (retentionDays <= 0)
            {
                _log.LogDebug($"[InfoCleaner] Retention days is set to {retentionDays}. Skipping cleanup.");
                return;
            }

            string baseFolder = _settings.GetBaseFolder();
            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
            {
                _log.LogDebug($"[InfoCleaner] BaseFolder '{baseFolder}' is not set or does not exist. Skipping cleanup.");
                return;
            }

            _log.LogEvent($"[InfoCleaner] Starting cleanup task with {retentionDays} days retention in folder '{baseFolder}'.");

            try
            {
                // 1. Baseline 폴더의 .info 파일 정리
                CleanBaselineFolder(baseFolder, retentionDays);

                // 2. BaseFolder 하위 모든 폴더의 날짜 패턴 파일 정리
                CleanFolderRecursively(baseFolder, retentionDays);
            }
            catch (Exception ex)
            {
                // 최상위 레벨에서 예외 처리
                _log.LogError($"[InfoCleaner] A critical error occurred during the cleaning execution: {ex.Message}");
                _log.LogDebug($"[InfoCleaner] Cleaning execution exception details: {ex.ToString()}");
            }

            _log.LogEvent("[InfoCleaner] Cleanup task finished.");
        }

        private void CleanBaselineFolder(string baseFolder, int days)
        {
            string baselineDir = Path.Combine(baseFolder, "Baseline");
            if (!Directory.Exists(baselineDir))
            {
                _log.LogDebug($"[InfoCleaner] Baseline folder does not exist, skipping: {baselineDir}");
                return;
            }

            _log.LogDebug($"[InfoCleaner] Scanning Baseline folder: {baselineDir}");
            DateTime now = DateTime.Now;

            try
            {
                foreach (string file in Directory.EnumerateFiles(baselineDir, "*.info"))
                {
                    string name = Path.GetFileName(file);
                    Match m = TsRegex.Match(name);
                    if (!m.Success)
                    {
                        _log.LogDebug($"[InfoCleaner] File '{name}' in Baseline does not match the expected timestamp pattern. Skipping.");
                        continue;
                    }

                    if (DateTime.TryParseExact(m.Groups["ts"].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime ts))
                    {
                        if ((now - ts).TotalDays >= days)
                        {
                            _log.LogDebug($"[InfoCleaner] Deleting old .info file '{name}' (Timestamp: {ts}, Older than {days} days).");
                            TryDeleteFile(file, name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"[InfoCleaner] Error while cleaning Baseline folder '{baselineDir}': {ex.Message}");
                _log.LogDebug($"[InfoCleaner] CleanBaselineFolder exception details: {ex.ToString()}");
            }
        }

        private void CleanFolderRecursively(string rootDir, int days)
        {
            _log.LogDebug($"[InfoCleaner] Starting recursive scan in root folder: {rootDir}");
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
                            _log.LogDebug($"[InfoCleaner] Deleting old file '{name}' (Date: {fileDate.Value:yyyy-MM-dd}, Older than {days} days).");
                            TryDeleteFile(file, name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError($"[InfoCleaner] Error during recursive clean of '{rootDir}': {ex.Message}");
                _log.LogDebug($"[InfoCleaner] CleanFolderRecursively exception details: {ex.ToString()}");
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
                    _log.LogDebug($"[InfoCleaner] Skip deletion (file already removed): {displayName}");
                    return;
                }

                FileAttributes attrs = File.GetAttributes(filePath);
                if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    _log.LogDebug($"[InfoCleaner] File '{displayName}' is read-only. Attempting to remove read-only attribute.");
                    File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                }

                File.Delete(filePath);
                _log.LogEvent($"[InfoCleaner] Deleted old file successfully: {displayName}");
            }
            catch (UnauthorizedAccessException uae)
            {
                _log.LogError($"[InfoCleaner] Delete failed for '{displayName}' (UnauthorizedAccessException): {uae.Message}. Check file/folder permissions.");
            }
            catch (IOException ioe)
            {
                _log.LogError($"[InfoCleaner] Delete failed for '{displayName}' (IOException): {ioe.Message}. The file might be in use.");
            }
            catch (Exception ex)
            {
                _log.LogError($"[InfoCleaner] An unexpected error occurred while deleting '{displayName}': {ex.Message}");
                _log.LogDebug($"[InfoCleaner] Delete file exception details: {ex.ToString()}");
            }
        }

        /// <summary>
        /// Timer 리소스를 해제합니다.
        /// </summary>
        public void Dispose()
        {
            _log.LogEvent("[InfoCleaner] Disposing service and stopping timer.");
            _timer?.Dispose();
        }
    }
}
