// ITM_Agent/Panels/ucOverrideNamesPanel.cs
using ITM_Agent.Common.Interfaces;
using ITM_Agent.Forms; // RegexConfigForm 사용을 위해 추가
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

        private FileSystemWatcher _baseDateFolderWatcher;
        private FileSystemWatcher _baselineFolderWatcher;

        private readonly Dictionary<string, FileTrackingInfo> _trackedFiles = new Dictionary<string, FileTrackingInfo>();
        // CS0104 오류 해결: System.Threading.Timer를 명시적으로 사용
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

        private void RegisterEventHandlers()
        {
            cb_BaseDatePath.SelectedIndexChanged += OnBaseDatePathChanged;
            btn_BaseClear.Click += OnBaseClearClick;
            btn_SelectFolder.Click += OnSelectTargetFolderClick;
            btn_Remove.Click += OnRemoveTargetFolderClick;
        }

        private void LoadAllSettings()
        {
            LoadRegexFolderPaths();
            LoadTargetComparePaths();
        }

        private void LoadRegexFolderPaths()
        {
            cb_BaseDatePath.Items.Clear();
            var folderPaths = _configPanel.GetRegexTargetFolders();
            cb_BaseDatePath.Items.AddRange(folderPaths.ToArray());

            string selectedPath = _settingsManager.GetValueFromSection("OverrideNames", "BaseDatePath");
            if (!string.IsNullOrEmpty(selectedPath) && cb_BaseDatePath.Items.Contains(selectedPath))
            {
                cb_BaseDatePath.SelectedItem = selectedPath;
            }
            else
            {
                cb_BaseDatePath.SelectedIndex = -1;
            }
        }

        private void LoadTargetComparePaths()
        {
            lb_TargetComparePath.Items.Clear();
            var folders = _settingsManager.GetFoldersFromSection("[TargetComparePath]");
            lb_TargetComparePath.Items.AddRange(folders.ToArray());
        }

        public void RefreshUI() => LoadAllSettings();

        #endregion

        #region --- UI Event Handlers ---

        private void OnBaseDatePathChanged(object sender, EventArgs e)
        {
            if (cb_BaseDatePath.SelectedItem is string selectedPath)
            {
                _settingsManager.SetValueToSection("OverrideNames", "BaseDatePath", selectedPath);
                if (_isRunning) StartWatchers();
            }
        }

        private void OnBaseClearClick(object sender, EventArgs e)
        {
            cb_BaseDatePath.SelectedIndex = -1;
            _settingsManager.RemoveKeyFromSection("OverrideNames", "BaseDatePath");
            if (_isRunning) StopWatchers();
        }

        private void OnSelectTargetFolderClick(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (!lb_TargetComparePath.Items.Contains(dialog.SelectedPath))
                    {
                        lb_TargetComparePath.Items.Add(dialog.SelectedPath);
                        UpdateTargetComparePathsInSettings();
                    }
                    else
                    {
                        MessageBox.Show("해당 폴더는 이미 추가되어 있습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
        }

        private void OnRemoveTargetFolderClick(object sender, EventArgs e)
        {
            if (lb_TargetComparePath.SelectedItems.Count > 0)
            {
                if (MessageBox.Show("선택한 항목을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    var itemsToRemove = lb_TargetComparePath.SelectedItems.Cast<string>().ToList();
                    foreach (var item in itemsToRemove)
                    {
                        lb_TargetComparePath.Items.Remove(item);
                    }
                    UpdateTargetComparePathsInSettings();
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
        }

        #endregion

        #region --- Watcher & File Processing Logic ---

        private void StartWatchers()
        {
            StopWatchers();

            string baseDatePath = cb_BaseDatePath.SelectedItem as string;
            if (!string.IsNullOrEmpty(baseDatePath) && Directory.Exists(baseDatePath))
            {
                _baseDateFolderWatcher = new FileSystemWatcher(baseDatePath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _baseDateFolderWatcher.Created += OnFileSystemEvent;
                _baseDateFolderWatcher.Changed += OnFileSystemEvent;
                _logManager.LogEvent($"[OverrideNames] Base date folder watching started: {baseDatePath}");
            }

            string baselineFolder = Path.Combine(_settingsManager.GetBaseFolder(), "Baseline");
            if (Directory.Exists(baselineFolder))
            {
                _baselineFolderWatcher = new FileSystemWatcher(baselineFolder, "*.info")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _baselineFolderWatcher.Created += OnBaselineFileChanged;
                _baselineFolderWatcher.Changed += OnBaselineFileChanged;
                _logManager.LogEvent($"[OverrideNames] Baseline folder watching started: {baselineFolder}");
            }
        }

        private void StopWatchers()
        {
            _baseDateFolderWatcher?.Dispose();
            _baseDateFolderWatcher = null;
            _baselineFolderWatcher?.Dispose();
            _baselineFolderWatcher = null;
            _stabilityTimer?.Dispose();
            _stabilityTimer = null;
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            if (!_isRunning || !File.Exists(e.FullPath)) return;

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
                    // CS0104 오류 해결: System.Threading.Timer를 명시적으로 사용
                    _stabilityTimer = new System.Threading.Timer(_ => CheckFileStability(), null, 2000, 2000);
                }
            }
        }

        private void CheckFileStability()
        {
            var stableFiles = new List<string>();
            lock (_trackingLock)
            {
                var now = DateTime.Now;
                foreach (var kvp in _trackedFiles.ToList())
                {
                    string filePath = kvp.Key;
                    FileTrackingInfo info = kvp.Value;

                    long currentSize = GetFileSizeSafe(filePath);
                    DateTime currentWriteTime = GetLastWriteTimeSafe(filePath);

                    if (currentSize != info.LastSize || currentWriteTime != info.LastWriteTime)
                    {
                        info.LastEventTime = now;
                        info.LastSize = currentSize;
                        info.LastWriteTime = currentWriteTime;
                        continue;
                    }

                    if ((now - info.LastEventTime).TotalSeconds >= STABILITY_CHECK_SECONDS)
                    {
                        stableFiles.Add(filePath);
                    }
                }
                stableFiles.ForEach(f => _trackedFiles.Remove(f));

                if (!_trackedFiles.Any())
                {
                    _stabilityTimer?.Dispose();
                    _stabilityTimer = null;
                }
            }
            stableFiles.ForEach(ProcessStableFile);
        }

        private void ProcessStableFile(string filePath)
        {
            try
            {
                if (!WaitForFileReady(filePath, maxRetries: 10, delayMs: 500))
                {
                    _logManager.LogError($"[OverrideNames] File is locked and could not be processed: {filePath}");
                    return;
                }

                if (!File.Exists(filePath)) return;

                DateTime? dateTimeInfo = ExtractDateTimeFromFile(filePath);
                if (dateTimeInfo.HasValue)
                {
                    string infoPath = CreateBaselineInfoFile(filePath, dateTimeInfo.Value);
                    if (!string.IsNullOrEmpty(infoPath))
                    {
                        _logManager.LogEvent($"[OverrideNames] Baseline file created for: {Path.GetFileName(filePath)} -> {Path.GetFileName(infoPath)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[OverrideNames] Error processing stable file {filePath}: {ex.Message}");
            }
        }

        private void OnBaselineFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!_isRunning || !File.Exists(e.FullPath)) return;

            var baselineData = ExtractBaselineData(new[] { e.FullPath });
            if (baselineData.Count == 0) return;

            foreach (string targetFolder in lb_TargetComparePath.Items)
            {
                if (Directory.Exists(targetFolder))
                {
                    RenameFilesInTargetFolder(targetFolder, baselineData);
                }
            }
        }

        private void RenameFilesInTargetFolder(string folder, Dictionary<string, (string TimeInfo, string Prefix, string CInfo)> baselineData)
        {
            try
            {
                foreach (var targetFile in Directory.GetFiles(folder))
                {
                    TryRenameTargetFile(targetFile, baselineData);
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[OverrideNames] Error during renaming in folder {folder}: {ex.Message}");
            }
        }
        #endregion

        #region --- Public Control Methods ---

        public string EnsureOverrideAndReturnPath(string originalPath, int timeoutMs = 180000)
        {
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
                            var baselineData = ExtractBaselineData(infoFiles);
                            string renamedPath = TryRenameTargetFile(originalPath, baselineData);
                            return renamedPath ?? originalPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[OverrideNames] Error while ensuring override for {originalPath}: {ex.Message}");
                }

                Thread.Sleep(500);
            }

            _logManager.LogEvent($"[OverrideNames] Timeout waiting for .info file for {originalPath}. Skipping rename.");
            return originalPath;
        }

        public void UpdateStatusOnRun(bool isRunning)
        {
            _isRunning = isRunning;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopWatchers();
                // CS1503 오류 해결: 'components' 대신 'disposing' 전달
                base.Dispose(disposing);
            }
        }

        #endregion

        #region --- Helper Methods (File Access, Parsing) ---

        private class FileTrackingInfo
        {
            public DateTime LastEventTime { get; set; }
            public long LastSize { get; set; }
            public DateTime LastWriteTime { get; set; }
        }

        private long GetFileSizeSafe(string filePath)
        {
            try { if (File.Exists(filePath)) return new FileInfo(filePath).Length; } catch { }
            return -1;
        }

        private DateTime GetLastWriteTimeSafe(string filePath)
        {
            try { if (File.Exists(filePath)) return new FileInfo(filePath).LastWriteTime; } catch { }
            return DateTime.MinValue;
        }

        private bool WaitForFileReady(string filePath, int maxRetries, int delayMs)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        return true;
                    }
                }
                catch (IOException) { Thread.Sleep(delayMs); }
                catch { return false; }
            }
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
                _logManager.LogDebug($"[OverrideNames] Could not extract date from {filePath}: {ex.Message}");
            }
            return null;
        }

        private string CreateBaselineInfoFile(string filePath, DateTime dateTime)
        {
            string baseFolder = _configPanel.BaseFolderPath;
            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
            {
                _logManager.LogError("[OverrideNames] BaseFolder is not configured. Cannot create .info file.");
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
                _logManager.LogError($"[OverrideNames] Failed to create .info file {newFilePath}: {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, (string TimeInfo, string Prefix, string CInfo)> ExtractBaselineData(string[] files)
        {
            var baselineData = new Dictionary<string, (string, string, string)>();
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
                    baselineData[fileName] = (timeInfo, prefix, cInfo);
                }
            }
            return baselineData;
        }

        private string TryRenameTargetFile(string targetFile, Dictionary<string, (string TimeInfo, string Prefix, string CInfo)> baselineData)
        {
            if (!WaitForFileReady(targetFile, 5, 200)) return null;

            string fileName = Path.GetFileName(targetFile);

            foreach (var data in baselineData.Values)
            {
                if (fileName.Contains(data.Prefix) && fileName.Contains("_#1_")) // 원본 패턴 확인
                {
                    string newName = fileName.Replace("_#1_", $"_{data.CInfo}_");
                    string newPath = Path.Combine(Path.GetDirectoryName(targetFile), newName);

                    try
                    {
                        if (File.Exists(newPath))
                        {
                            _logManager.LogDebug($"[OverrideNames] Renamed file already exists, skipping: {newPath}");
                            return newPath;
                        }
                        File.Move(targetFile, newPath);
                        _logManager.LogEvent($"[OverrideNames] File renamed: {fileName} -> {newName}");
                        return newPath;
                    }
                    catch (Exception ex)
                    {
                        _logManager.LogError($"[OverrideNames] Failed to rename {fileName} to {newName}: {ex.Message}");
                        return null;
                    }
                }
            }
            return null;
        }

        #endregion
    }
}