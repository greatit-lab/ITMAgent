// ITM_Agent.Core/FileWatcherManager.cs
using ITM_Agent.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ITM_Agent.Core
{
    /// <summary>
    /// 대상 폴더의 파일 시스템 변경(생성, 수정, 삭제)을 감시하고,
    /// 정규식에 맞는 파일을 지정된 폴더로 복사하는 서비스 클래스입니다.
    /// </summary>
    public class FileWatcherManager
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ILogManager _logManager;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Dictionary<string, DateTime> _fileProcessTracker = new Dictionary<string, DateTime>();
        private readonly TimeSpan _duplicateEventThreshold = TimeSpan.FromSeconds(2); // 중복 이벤트 방지 시간

        private volatile bool _isRunning = false;

        public FileWatcherManager(ISettingsManager settings, ILogManager logger)
        {
            _settingsManager = settings;
            _logManager = logger;
        }

        /// <summary>
        /// 설정 파일에 정의된 TargetFolders에 대한 파일 감시를 시작합니다.
        /// </summary>
        public void StartWatching()
        {
            if (_isRunning)
            {
                _logManager.LogEvent("[FileWatcherManager] File monitoring is already running.");
                return;
            }

            _logManager.LogEvent("[FileWatcherManager] Initializing watchers...");
            StopWatchers(); // 기존 Watcher 정리

            var targetFolders = _settingsManager.GetFoldersFromSection("[TargetFolders]");
            if (targetFolders == null || !targetFolders.Any())
            {
                _logManager.LogEvent("[FileWatcherManager] No target folders configured for monitoring.");
                return;
            }

            foreach (var folder in targetFolders)
            {
                if (!Directory.Exists(folder))
                {
                    _logManager.LogError($"[FileWatcherManager] Target folder does not exist: {folder}");
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
                    };

                    // 버퍼 크기를 64KB(최대값)로 늘려 버퍼 오버플로를 방지합니다.
                    watcher.InternalBufferSize = 65536;

                    watcher.Created += OnFileEvent;
                    watcher.Changed += OnFileEvent;
                    watcher.Deleted += OnFileEvent;
                    watcher.Error += OnWatcherError; // 에러 핸들러 추가

                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                    _logManager.LogDebug($"[FileWatcherManager] Watcher started for folder: {folder}");
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[FileWatcherManager] Failed to start watcher for {folder}. Error: {ex.Message}");
                }
            }

            _isRunning = true;
            _logManager.LogEvent($"[FileWatcherManager] File monitoring started for {_watchers.Count} folder(s).");
        }

        /// <summary>
        /// 모든 파일 감시를 중지합니다.
        /// </summary>
        public void StopWatchers()
        {
            if (!_isRunning && !_watchers.Any()) return;

            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnFileEvent;
                watcher.Changed -= OnFileEvent;
                watcher.Deleted -= OnFileEvent;
                watcher.Error -= OnWatcherError;
                watcher.Dispose();
            }
            _watchers.Clear();
            _isRunning = false;
            _logManager.LogEvent("[FileWatcherManager] File monitoring stopped.");
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            _logManager.LogError($"[FileWatcherManager] A watcher error occurred: {e.GetException()?.Message}");
        }

        private async void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            if (!_isRunning) return;

            // 디렉터리 이벤트는 무시
            if (Directory.Exists(e.FullPath)) return;

            if (IsDuplicateEvent(e.FullPath))
            {
                _logManager.LogDebug($"[FileWatcherManager] Duplicate event ignored: {e.ChangeType} - {e.FullPath}");
                return;
            }

            _logManager.LogDebug($"[FileWatcherManager] Event detected: {e.ChangeType} on {e.FullPath}");

            if (e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Changed)
            {
                await ProcessFileAsync(e.FullPath);
            }
            else if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                _logManager.LogEvent($"[FileWatcherManager] File Deleted: {Path.GetFileName(e.FullPath)}");
            }
        }

        private async Task ProcessFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logManager.LogDebug($"[FileWatcherManager] File no longer exists, skipping processing: {filePath}");
                return;
            }

            if (!await WaitForFileReadyAsync(filePath))
            {
                _logManager.LogEvent($"[FileWatcherManager] File skipped (locked after retries): {Path.GetFileName(filePath)}");
                return;
            }

            string fileName = Path.GetFileName(filePath);
            var regexList = _settingsManager.GetRegexList();

            foreach (var kvp in regexList)
            {
                try
                {
                    if (Regex.IsMatch(fileName, kvp.Key, RegexOptions.IgnoreCase))
                    {
                        string destinationFolder = kvp.Value;
                        string destinationFile = Path.Combine(destinationFolder, fileName);

                        Directory.CreateDirectory(destinationFolder); // 대상 폴더가 없으면 생성

                        File.Copy(filePath, destinationFile, true); // 덮어쓰기
                        _logManager.LogEvent($"[FileWatcherManager] File copied: {fileName} -> {destinationFolder}");
                        return; // 첫 번째 매칭되는 규칙에 따라 처리 후 종료
                    }
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[FileWatcherManager] Error copying file {fileName}: {ex.Message}");
                    return; // 에러 발생 시 더 이상 처리하지 않음
                }
            }
            _logManager.LogDebug($"[FileWatcherManager] No matching regex for file: {fileName}");
        }

        private bool IsDuplicateEvent(string filePath)
        {
            lock (_fileProcessTracker)
            {
                DateTime now = DateTime.Now;
                if (_fileProcessTracker.TryGetValue(filePath, out DateTime lastProcessed))
                {
                    if ((now - lastProcessed) < _duplicateEventThreshold)
                    {
                        return true; // 중복 이벤트로 간주
                    }
                }
                _fileProcessTracker[filePath] = now; // 이벤트 처리 시간 갱신
                return false;
            }
        }

        private async Task<bool> WaitForFileReadyAsync(string filePath, int maxRetries = 10, int delayMs = 300)
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        return true; // 파일 접근 성공
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(delayMs); // 파일이 잠겨있으면 대기
                }
                catch (Exception)
                {
                    return false; // 그 외 예외는 즉시 실패 처리
                }
            }
            return false;
        }
    }
}
