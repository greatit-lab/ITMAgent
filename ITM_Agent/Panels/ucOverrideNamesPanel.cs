// ITM_Agent/Panels/ucOverrideNamesPanel.cs
using ITM_Agent.Common.Interfaces;
using ITM_Agent.Forms;
using ITM_Agent.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ITM_Agent.Panels
{
    /// <summary>
    /// Baseline(.info) 파일을 기준으로 대상 파일의 이름을 변경하는 UI 패널입니다.
    /// 파일 시스템 감시, 파일 안정화 체크, 이름 변경 로직을 수행합니다.
    /// </summary>
    public partial class ucOverrideNamesPanel : UserControl, IDisposable
    {
        #region --- Services and Fields ---

        private readonly ISettingsManager _settingsManager;
        private readonly ucConfigurationPanel _configPanel;
        private readonly ILogManager _logManager;
        private ucUploadPanel _ucUploadPanel;

        private FileSystemWatcher _baseDateFolderWatcher;
        private FileSystemWatcher _baselineFolderWatcher;

        private readonly Dictionary<string, FileTrackingInfo> _trackedFiles = new Dictionary<string, FileTrackingInfo>();
        private System.Threading.Timer _stabilityTimer;
        private readonly object _trackingLock = new object();
        private const double STABILITY_CHECK_SECONDS = 2.0;

        private bool _isRunning = false;

        #endregion

        #region --- Initialization & UI Loading ---

        public ucOverrideNamesPanel(ISettingsManager settingsManager, ucConfigurationPanel configPanel, ILogManager logManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _configPanel = configPanel ?? throw new ArgumentNullException(nameof(configPanel));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

            InitializeComponent();
            RegisterEventHandlers();
            LoadAllSettings();
        }

        public void LinkUploadPanel(ucUploadPanel uploadPanel)
        {
            _ucUploadPanel = uploadPanel ?? throw new ArgumentNullException(nameof(uploadPanel));
            _logManager.LogDebug("[ucOverrideNamesPanel] ucUploadPanel linked successfully.");
        }

        private void RegisterEventHandlers()
        {
            cb_BaseDatePath.SelectedIndexChanged += OnBaseDatePathChanged;
            btn_BaseClear.Click += OnBaseClearClick;
            btn_SelectFolder.Click += OnSelectTargetFolderClick;
            btn_Remove.Click += OnRemoveTargetFolderClick;
        }

        private void LoadAllSettings()
        {
            _logManager.LogDebug("[ucOverrideNamesPanel] Loading all settings.");
            LoadRegexFolderPaths();
            LoadTargetComparePaths();
        }

        private void LoadRegexFolderPaths()
        {
            _logManager.LogDebug("[ucOverrideNamesPanel] Loading regex folder paths for BaseDatePath dropdown.");
            cb_BaseDatePath.Items.Clear();
            var folderPaths = _configPanel.GetRegexTargetFolders();
            cb_BaseDatePath.Items.AddRange(folderPaths.ToArray());

            string selectedPath = _settingsManager.GetValueFromSection("OverrideNames", "BaseDatePath");
            if (!string.IsNullOrEmpty(selectedPath) && cb_BaseDatePath.Items.Contains(selectedPath))
            {
                cb_BaseDatePath.SelectedItem = selectedPath;
                _logManager.LogDebug($"[ucOverrideNamesPanel] Loaded BaseDatePath: {selectedPath}");
            }
            else
            {
                cb_BaseDatePath.SelectedIndex = -1;
            }
        }

        private void LoadTargetComparePaths()
        {
            _logManager.LogDebug("[ucOverrideNamesPanel] Loading target compare paths.");
            lb_TargetComparePath.Items.Clear();
            var folders = _settingsManager.GetFoldersFromSection("[TargetComparePath]");
            lb_TargetComparePath.Items.AddRange(folders.ToArray());
        }

        public void RefreshUI()
        {
            _logManager.LogEvent("[ucOverrideNamesPanel] RefreshUI called externally.");
            LoadAllSettings();
        }

        #endregion

        #region --- UI Event Handlers ---

        private void OnBaseDatePathChanged(object sender, EventArgs e)
        {
            if (cb_BaseDatePath.SelectedItem is string selectedPath)
            {
                _logManager.LogEvent($"[ucOverrideNamesPanel] BaseDatePath changed to: {selectedPath}");
                _settingsManager.SetValueToSection("OverrideNames", "BaseDatePath", selectedPath);
                if (_isRunning)
                {
                    _logManager.LogDebug("[ucOverrideNamesPanel] Restarting watchers due to BaseDatePath change.");
                    StartWatchers();
                }
            }
        }

        private void OnBaseClearClick(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucOverrideNamesPanel] BaseDatePath cleared.");
            cb_BaseDatePath.SelectedIndex = -1;
            _settingsManager.RemoveKeyFromSection("OverrideNames", "BaseDatePath");
            if (_isRunning)
            {
                _logManager.LogDebug("[ucOverrideNamesPanel] Stopping watchers due to BaseDatePath clear.");
                StopWatchers();
            }
        }

        private void OnSelectTargetFolderClick(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucOverrideNamesPanel] Select target compare folder button clicked.");
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (!lb_TargetComparePath.Items.Contains(dialog.SelectedPath))
                    {
                        lb_TargetComparePath.Items.Add(dialog.SelectedPath);
                        UpdateTargetComparePathsInSettings();
                        _logManager.LogEvent($"[ucOverrideNamesPanel] Added target compare folder: {dialog.SelectedPath}");
                    }
                    else
                    {
                        _logManager.LogDebug($"[ucOverrideNamesPanel] Folder '{dialog.SelectedPath}' already exists in the target compare list.");
                        MessageBox.Show("해당 폴더는 이미 추가되어 있습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    _logManager.LogDebug("[ucOverrideNamesPanel] Select folder dialog was canceled.");
                }
            }
        }

        private void OnRemoveTargetFolderClick(object sender, EventArgs e)
        {
            if (lb_TargetComparePath.SelectedItems.Count > 0)
            {
                _logManager.LogEvent("[ucOverrideNamesPanel] Remove target compare folder button clicked.");
                if (MessageBox.Show("선택한 항목을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var itemsToRemove = lb_TargetComparePath.SelectedItems.Cast<string>().ToList();
                    _logManager.LogDebug($"[ucOverrideNamesPanel] User confirmed removal of {itemsToRemove.Count} target compare folder(s).");
                    foreach (var item in itemsToRemove)
                    {
                        lb_TargetComparePath.Items.Remove(item);
                    }
                    UpdateTargetComparePathsInSettings();
                    _logManager.LogEvent($"[ucOverrideNamesPanel] Removed {itemsToRemove.Count} target compare folder(s).");
                }
                else
                {
                    _logManager.LogDebug("[ucOverrideNamesPanel] Folder removal was canceled by user.");
                }
            }
            else
            {
                MessageBox.Show("삭제할 항목을 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void UpdateTargetComparePathsInSettings()
        {
            var folders = lb_TargetComparePath.Items.Cast<string>().ToList();
            _settingsManager.SetFoldersToSection("[TargetComparePath]", folders);
            _logManager.LogDebug("[ucOverrideNamesPanel] Updated '[TargetComparePath]' section in settings.ini.");
        }

        #endregion

        #region --- Watcher & File Processing Logic ---

        private void StartWatchers()
        {
            _logManager.LogDebug("[ucOverrideNamesPanel] Attempting to start watchers.");
            StopWatchers();

            string baseDatePath = cb_BaseDatePath.SelectedItem as string;
            if (!string.IsNullOrEmpty(baseDatePath) && Directory.Exists(baseDatePath))
            {
                try
                {
                    _baseDateFolderWatcher = new FileSystemWatcher(baseDatePath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true
                    };
                    _baseDateFolderWatcher.Created += OnFileSystemEvent;
                    _baseDateFolderWatcher.Changed += OnFileSystemEvent;
                    _logManager.LogEvent($"[ucOverrideNamesPanel] Base date folder watcher started: {baseDatePath}");
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[ucOverrideNamesPanel] Failed to start BaseDateFolderWatcher on '{baseDatePath}': {ex.Message}");
                }
            }
            else
            {
                _logManager.LogDebug("[ucOverrideNamesPanel] BaseDatePath is not set or does not exist. Watcher not started.");
            }

            string baselineFolder = Path.Combine(_settingsManager.GetBaseFolder(), "Baseline");
            if (Directory.Exists(baselineFolder))
            {
                try
                {
                    _baselineFolderWatcher = new FileSystemWatcher(baselineFolder, "*.info")
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true
                    };
                    _baselineFolderWatcher.Created += OnBaselineFileChanged;
                    _baselineFolderWatcher.Changed += OnBaselineFileChanged;
                    _logManager.LogEvent($"[ucOverrideNamesPanel] Baseline folder watcher started: {baselineFolder}");
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[ucOverrideNamesPanel] Failed to start BaselineFolderWatcher on '{baselineFolder}': {ex.Message}");
                }
            }
            else
            {
                _logManager.LogDebug($"[ucOverrideNamesPanel] Baseline folder '{baselineFolder}' does not exist. Watcher not started.");
            }
        }

        private void StopWatchers()
        {
            if (_baseDateFolderWatcher != null)
            {
                _baseDateFolderWatcher.EnableRaisingEvents = false;
                _baseDateFolderWatcher.Dispose();
                _baseDateFolderWatcher = null;
                _logManager.LogDebug("[ucOverrideNamesPanel] BaseDateFolderWatcher stopped.");
            }
            if (_baselineFolderWatcher != null)
            {
                _baselineFolderWatcher.EnableRaisingEvents = false;
                _baselineFolderWatcher.Dispose();
                _baselineFolderWatcher = null;
                _logManager.LogDebug("[ucOverrideNamesPanel] BaselineFolderWatcher stopped.");
            }
            if (_stabilityTimer != null)
            {
                _stabilityTimer.Dispose();
                _stabilityTimer = null;
                _logManager.LogDebug("[ucOverrideNamesPanel] Stability timer stopped.");
            }
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            if (!_isRunning || !File.Exists(e.FullPath)) return;

            _logManager.LogDebug($"[ucOverrideNamesPanel] File event '{e.ChangeType}' detected for: {e.FullPath}");

            lock (_trackingLock)
            {
                _trackedFiles[e.FullPath] = new FileTrackingInfo
                {
                    LastEventTime = DateTime.Now,
                    LastSize = GetFileSizeSafe(e.FullPath),
                    LastWriteTime = GetLastWriteTimeSafe(e.FullPath)
                };

                if (_stabilityTimer == null)
                {
                    _logManager.LogDebug("[ucOverrideNamesPanel] Starting file stability check timer.");
                    _stabilityTimer = new System.Threading.Timer(_ => CheckFileStability(), null, 2000, 2000);
                }
            }
        }

        private void CheckFileStability()
        {
            var stableFiles = new List<string>();
            lock (_trackingLock)
            {
                _logManager.LogDebug($"[ucOverrideNamesPanel] Checking file stability for {_trackedFiles.Count} file(s).");
                var now = DateTime.Now;
                foreach (var kvp in _trackedFiles.ToList())
                {
                    string filePath = kvp.Key;
                    FileTrackingInfo info = kvp.Value;

                    long currentSize = GetFileSizeSafe(filePath);
                    DateTime currentWriteTime = GetLastWriteTimeSafe(filePath);

                    if (currentSize != info.LastSize || currentWriteTime != info.LastWriteTime)
                    {
                        _logManager.LogDebug($"[ucOverrideNamesPanel] File '{Path.GetFileName(filePath)}' is still changing. Resetting timer.");
                        info.LastEventTime = now;
                        info.LastSize = currentSize;
                        info.LastWriteTime = currentWriteTime;
                        continue;
                    }

                    if ((now - info.LastEventTime).TotalSeconds >= STABILITY_CHECK_SECONDS)
                    {
                        _logManager.LogDebug($"[ucOverrideNamesPanel] File '{Path.GetFileName(filePath)}' is stable.");
                        stableFiles.Add(filePath);
                    }
                }
                stableFiles.ForEach(f => _trackedFiles.Remove(f));

                if (!_trackedFiles.Any() && _stabilityTimer != null)
                {
                    _stabilityTimer.Dispose();
                    _stabilityTimer = null;
                    _logManager.LogDebug("[ucOverrideNamesPanel] File tracking queue is empty. Stability timer stopped.");
                }
            }
            stableFiles.ForEach(ProcessStableFile);
        }

        private void ProcessStableFile(string filePath)
        {
            _logManager.LogEvent($"[ucOverrideNamesPanel] Processing stable file: {Path.GetFileName(filePath)}");
            try
            {
                if (!WaitForFileReady(filePath, maxRetries: 10, delayMs: 500))
                {
                    _logManager.LogError($"[ucOverrideNamesPanel] File is locked and could not be processed: {filePath}");
                    return;
                }

                if (!File.Exists(filePath))
                {
                    _logManager.LogDebug($"[ucOverrideNamesPanel] File no longer exists, skipping: {filePath}");
                    return;
                }

                DateTime? dateTimeInfo = ExtractDateTimeFromFile(filePath);
                if (dateTimeInfo.HasValue)
                {
                    _logManager.LogDebug($"[ucOverrideNamesPanel] Extracted date '{dateTimeInfo.Value}' from file.");
                    string infoPath = CreateBaselineInfoFile(filePath, dateTimeInfo.Value);
                    if (!string.IsNullOrEmpty(infoPath))
                    {
                        _logManager.LogEvent($"[ucOverrideNamesPanel] Baseline file created: {Path.GetFileName(filePath)} -> {Path.GetFileName(infoPath)}");
                    }
                }
                else
                {
                    _logManager.LogEvent($"[ucOverrideNamesPanel] Could not extract date information from file: {Path.GetFileName(filePath)}");
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucOverrideNamesPanel] Error processing stable file {filePath}: {ex.Message}");
            }
        }

        private void OnBaselineFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!_isRunning || !File.Exists(e.FullPath)) return;

            _logManager.LogEvent($"[ucOverrideNamesPanel] Baseline file event '{e.ChangeType}' for: {Path.GetFileName(e.FullPath)}");

            var baselineData = ExtractBaselineData(new[] { e.FullPath });
            if (baselineData.Count == 0)
            {
                _logManager.LogDebug("[ucOverrideNamesPanel] Baseline file did not contain valid data. Skipping rename process.");
                return;
            }

            var targetFolders = _settingsManager.GetFoldersFromSection("[TargetComparePath]");
            _logManager.LogDebug($"[ucOverrideNamesPanel] Found {targetFolders.Count} target folder(s) to process for renaming.");
            foreach (string targetFolder in targetFolders)
            {
                if (Directory.Exists(targetFolder))
                {
                    RenameAndProcessFilesInTargetFolder(targetFolder, baselineData);
                }
                else
                {
                    _logManager.LogError($"[ucOverrideNamesPanel] Target folder for renaming does not exist: {targetFolder}");
                }
            }
        }

        private void RenameAndProcessFilesInTargetFolder(string folder, Dictionary<string, (string TimeInfo, string Prefix, string CInfo)> baselineData)
        {
            _logManager.LogDebug($"[ucOverrideNamesPanel] Scanning folder '{folder}' for files to rename.");
            try
            {
                foreach (var targetFile in Directory.GetFiles(folder))
                {
                    string renamedFilePath = TryRenameTargetFile(targetFile, baselineData);

                    if (!string.IsNullOrEmpty(renamedFilePath))
                    {
                        // Wafer Flat 데이터 파일 패턴(_WF_)을 포함하는지 확인
                        if (renamedFilePath.ToUpper().Contains("_WF_"))
                        {
                            _logManager.LogEvent($"[ucOverrideNamesPanel] WaferFlat data file '{Path.GetFileName(renamedFilePath)}' detected. Requesting immediate processing.");
                            _ucUploadPanel?.ProcessFileImmediately(renamedFilePath, "WaferFlat");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucOverrideNamesPanel] Error during renaming and processing in folder {folder}: {ex.Message}");
            }
        }
        #endregion

        #region --- Public Control Methods ---

        public string EnsureOverrideAndReturnPath(string originalPath, int timeoutMs = 180000)
        {
            _logManager.LogEvent($"[ucOverrideNamesPanel] Ensuring override for '{Path.GetFileName(originalPath)}' with timeout {timeoutMs}ms.");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string baselineFolder = Path.Combine(_settingsManager.GetBaseFolder(), "Baseline");
            string fileNameNoExt = Path.GetFileNameWithoutExtension(originalPath);
            string waferId = fileNameNoExt.Contains('_') ? fileNameNoExt.Split('_')[0] : fileNameNoExt;
            string searchPattern = $"{waferId}*.info";

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    if (Directory.Exists(baselineFolder))
                    {
                        var infoFiles = Directory.GetFiles(baselineFolder, searchPattern);
                        if (infoFiles.Any())
                        {
                            _logManager.LogDebug($"[ucOverrideNamesPanel] Found matching .info file(s) for '{waferId}'.");
                            var baselineData = ExtractBaselineData(infoFiles);
                            string renamedPath = TryRenameTargetFile(originalPath, baselineData);
                            if (renamedPath != null)
                            {
                                _logManager.LogEvent($"[ucOverrideNamesPanel] Override successful. Returning new path: '{renamedPath}'");
                                return renamedPath;
                            }
                            // 이름 변경 조건에 맞지 않았지만 .info 파일은 찾았으므로 더 기다릴 필요 없음
                            _logManager.LogDebug($"[ucOverrideNamesPanel] .info file found but rename conditions not met for '{originalPath}'. Returning original path.");
                            return originalPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[ucOverrideNamesPanel] Error while ensuring override for {originalPath}: {ex.Message}");
                }

                Thread.Sleep(500);
            }

            stopwatch.Stop();
            _logManager.LogEvent($"[ucOverrideNamesPanel] Timeout waiting for .info file for '{originalPath}'. Skipping rename.");
            return originalPath;
        }

        public void UpdateStatusOnRun(bool isRunning)
        {
            _isRunning = isRunning;
            _logManager.LogEvent($"[ucOverrideNamesPanel] Run status updated. IsRunning: {isRunning}");
            SetControlsEnabled(!isRunning);

            if (isRunning) StartWatchers();
            else StopWatchers();
        }

        private void SetControlsEnabled(bool isEnabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate { SetControlsEnabled(isEnabled); });
                return;
            }
            cb_BaseDatePath.Enabled = isEnabled;
            btn_BaseClear.Enabled = isEnabled;
            lb_TargetComparePath.Enabled = isEnabled;
            btn_SelectFolder.Enabled = isEnabled;
            btn_Remove.Enabled = isEnabled;
        }

        #endregion

        #region --- Helper Methods (File Access, Parsing) ---

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logManager.LogDebug("[ucOverrideNamesPanel] Disposing panel and stopping watchers.");
                StopWatchers();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        private class FileTrackingInfo
        {
            public DateTime LastEventTime { get; set; }
            public long LastSize { get; set; }
            public DateTime LastWriteTime { get; set; }
        }

        private long GetFileSizeSafe(string filePath)
        {
            try { if (File.Exists(filePath)) return new FileInfo(filePath).Length; }
            catch (Exception ex) { _logManager.LogDebug($"[ucOverrideNamesPanel] Could not get file size for '{filePath}': {ex.Message}"); }
            return -1;
        }

        private DateTime GetLastWriteTimeSafe(string filePath)
        {
            try { if (File.Exists(filePath)) return new FileInfo(filePath).LastWriteTime; }
            catch (Exception ex) { _logManager.LogDebug($"[ucOverrideNamesPanel] Could not get last write time for '{filePath}': {ex.Message}"); }
            return DateTime.MinValue;
        }

        private bool WaitForFileReady(string filePath, int maxRetries, int delayMs)
        {
            _logManager.LogDebug($"[ucOverrideNamesPanel] Waiting for file to be ready: '{Path.GetFileName(filePath)}'");
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        _logManager.LogDebug($"[ucOverrideNamesPanel] File is ready after {i} retries.");
                        return true;
                    }
                }
                catch (IOException) { Thread.Sleep(delayMs); }
                catch (Exception ex)
                {
                    _logManager.LogError($"[ucOverrideNamesPanel] Unexpected error while waiting for file '{filePath}': {ex.Message}");
                    return false;
                }
            }
            _logManager.LogDebug($"[ucOverrideNamesPanel] File was not ready after {maxRetries} retries.");
            return false;
        }

        private DateTime? ExtractDateTimeFromFile(string filePath)
        {
            string datePattern = @"Date and Time:\s*(\d{1,2}/\d{1,2}/\d{4} \d{1,2}:\d{2}:\d{2} (AM|PM))";
            try
            {
                string content = File.ReadAllText(filePath);
                Match match = Regex.Match(content, datePattern);
                if (match.Success && DateTime.TryParse(match.Groups[1].Value, out DateTime result))
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucOverrideNamesPanel] Could not read or extract date from '{filePath}': {ex.Message}");
            }
            return null;
        }

        private string CreateBaselineInfoFile(string filePath, DateTime dateTime)
        {
            string baseFolder = _configPanel.BaseFolderPath;
            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
            {
                _logManager.LogError("[ucOverrideNamesPanel] BaseFolder is not configured. Cannot create .info file.");
                return null;
            }

            string baselineFolder = Path.Combine(baseFolder, "Baseline");
            Directory.CreateDirectory(baselineFolder);

            string originalName = Path.GetFileNameWithoutExtension(filePath);
            string newFileName = $"{dateTime:yyyyMMdd_HHmmss}_{originalName}.info";
            string newFilePath = Path.Combine(baselineFolder, newFileName);

            try
            {
                File.Create(newFilePath).Close();
                return newFilePath;
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucOverrideNamesPanel] Failed to create .info file '{newFilePath}': {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, (string TimeInfo, string Prefix, string CInfo)> ExtractBaselineData(string[] files)
        {
            var baselineData = new Dictionary<string, (string TimeInfo, string Prefix, string CInfo)>();
            var regex = new Regex(@"(\d{8}_\d{6})_([^_]+?)_(C\dW\d+)");

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                var match = regex.Match(fileName);
                if (match.Success)
                {
                    string timeInfo = match.Groups[1].Value;
                    string prefix = match.Groups[2].Value;
                    string cInfo = match.Groups[3].Value;
                    baselineData[fileName] = (TimeInfo: timeInfo, Prefix: prefix, CInfo: cInfo);
                    _logManager.LogDebug($"[ucOverrideNamesPanel] Extracted baseline data from '{fileName}': Time={timeInfo}, Prefix={prefix}, CInfo={cInfo}");
                }
                else
                {
                    _logManager.LogDebug($"[ucOverrideNamesPanel] No baseline data pattern found in file name: '{fileName}'");
                }
            }
            return baselineData;
        }

        private string TryRenameTargetFile(string targetFile, Dictionary<string, (string TimeInfo, string Prefix, string CInfo)> baselineData)
        {
            if (!File.Exists(targetFile))
            {
                _logManager.LogDebug($"[ucOverrideNamesPanel] Target file for rename does not exist: '{targetFile}'");
                return null;
            }

            if (!WaitForFileReady(targetFile, 5, 200)) return null;

            string fileName = Path.GetFileName(targetFile);
            _logManager.LogDebug($"[ucOverrideNamesPanel] Attempting to rename '{fileName}' using {baselineData.Count} baseline entries.");

            foreach (var kvp in baselineData)
            {
                var data = kvp.Value; // (string TimeInfo, string Prefix, string CInfo)

                if (fileName.Contains(data.TimeInfo) && fileName.Contains(data.Prefix) && fileName.Contains("_#1_"))
                {
                    _logManager.LogDebug($"[ucOverrideNamesPanel] Match found for '{fileName}'. Criteria: Time='{data.TimeInfo}', Prefix='{data.Prefix}'.");
                    string newName = fileName.Replace("_#1_", $"_{data.CInfo}_");
                    string newPath = Path.Combine(Path.GetDirectoryName(targetFile), newName);

                    try
                    {
                        if (File.Exists(newPath))
                        {
                            _logManager.LogDebug($"[ucOverrideNamesPanel] Renamed file already exists, skipping: {newPath}");
                            return newPath; // 이미 변경된 파일도 성공으로 간주하고 경로 반환
                        }
                        File.Move(targetFile, newPath);
                        _logManager.LogEvent($"[ucOverrideNamesPanel] File renamed successfully: {fileName} -> {newName}");
                        return newPath;
                    }
                    catch (Exception ex)
                    {
                        _logManager.LogError($"[ucOverrideNamesPanel] Failed to rename {fileName} to {newName}: {ex.Message}");
                        return null; // 이름 변경 실패
                    }
                }
            }
            _logManager.LogDebug($"[ucOverrideNamesPanel] No matching baseline data found to rename '{fileName}'.");
            return null; // 매칭되는 규칙 없음
        }
        #endregion
    }
}
