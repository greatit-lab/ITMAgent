// ITM_Agent/Panels/ucImageTransPanel.cs
using ITM_Agent.Common.Interfaces;
using ITM_Agent.Core;
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
    /// 이미지 파일을 감시하고 조건에 따라 PDF로 자동 병합하는 UI 패널입니다.
    /// </summary>
    public partial class ucImageTransPanel : UserControl, IDisposable
    {
        #region --- Fields & Services ---

        private readonly ISettingsManager _settingsManager;
        private readonly ILogManager _logManager;
        private readonly ucConfigurationPanel _configPanel;
        private readonly PdfMergeManager _pdfMergeManager;

        private FileSystemWatcher _imageWatcher;
        // CS0104 오류 해결: System.Threading.Timer를 명시적으로 사용
        private System.Threading.Timer _checkTimer;

        private readonly Dictionary<string, DateTime> _changedFiles = new Dictionary<string, DateTime>();
        private readonly object _changedFilesLock = new object();

        private static readonly HashSet<string> _mergedBaseNames = new HashSet<string>();

        private bool _isRunning = false;

        #endregion

        #region --- Initialization ---

        public ucImageTransPanel(ISettingsManager settingsManager, ucConfigurationPanel configPanel, ILogManager logManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _configPanel = configPanel ?? throw new ArgumentNullException(nameof(configPanel));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

            InitializeComponent();

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _pdfMergeManager = new PdfMergeManager(baseDirectory, _logManager);

            RegisterEventHandlers();
            LoadAllSettings();
        }

        private void RegisterEventHandlers()
        {
            btn_SetFolder.Click += Btn_SetFolder_Click;
            btn_FolderClear.Click += Btn_FolderClear_Click;
            btn_SetTime.Click += Btn_SetTime_Click;
            btn_TimeClear.Click += Btn_TimeClear_Click;
            btn_SelectOutputFolder.Click += Btn_SelectOutputFolder_Click;
        }

        #endregion

        #region --- Settings Loading ---

        private void LoadAllSettings()
        {
            _logManager.LogDebug("[ucImageTransPanel] Loading all settings.");
            LoadRegexFolderPaths();
            LoadWaitTimes();
            LoadOutputFolder();
            _logManager.LogDebug("[ucImageTransPanel] All settings loaded.");
        }

        public void LoadRegexFolderPaths()
        {
            _logManager.LogDebug("[ucImageTransPanel] Loading regex folder paths for target image folder dropdown.");
            cb_TargetImageFolder.Items.Clear();
            var regexFolders = _configPanel.GetRegexTargetFolders();
            cb_TargetImageFolder.Items.AddRange(regexFolders.ToArray());

            string selectedPath = _settingsManager.GetValueFromSection("ImageTrans", "Target");
            if (!string.IsNullOrEmpty(selectedPath) && cb_TargetImageFolder.Items.Contains(selectedPath))
            {
                cb_TargetImageFolder.SelectedItem = selectedPath;
                _logManager.LogDebug($"[ucImageTransPanel] Loaded target image folder: {selectedPath}");
            }
            else
            {
                cb_TargetImageFolder.SelectedIndex = -1;
            }
        }

        public void LoadWaitTimes()
        {
            _logManager.LogDebug("[ucImageTransPanel] Loading wait times for dropdown.");
            cb_WaitTime.Items.Clear();
            cb_WaitTime.Items.AddRange(new object[] { "30", "60", "120", "180", "240", "300" });

            string savedWaitTime = _settingsManager.GetValueFromSection("ImageTrans", "Wait");
            cb_WaitTime.SelectedItem = savedWaitTime;
            if (cb_WaitTime.SelectedIndex < 0) cb_WaitTime.SelectedIndex = 0;
            _logManager.LogDebug($"[ucImageTransPanel] Loaded wait time: {cb_WaitTime.SelectedItem}");
        }

        private void LoadOutputFolder()
        {
            _logManager.LogDebug("[ucImageTransPanel] Loading output folder setting.");
            string outputFolder = _settingsManager.GetValueFromSection("ImageTrans", "SaveFolder");
            if (!string.IsNullOrEmpty(outputFolder) && Directory.Exists(outputFolder))
            {
                lb_ImageSaveFolder.Text = outputFolder;
                _pdfMergeManager.UpdateOutputFolder(outputFolder);
                _logManager.LogDebug($"[ucImageTransPanel] Loaded output folder: {outputFolder}");
            }
            else
            {
                lb_ImageSaveFolder.Text = "출력 폴더가 설정되지 않았습니다.";
                _logManager.LogDebug("[ucImageTransPanel] Output folder is not set or does not exist.");
            }
        }

        public void RefreshUI()
        {
            _logManager.LogEvent("[ucImageTransPanel] RefreshUI called externally.");
            LoadAllSettings();
        }

        #endregion

        #region --- Event Handlers ---

        private void Btn_SetFolder_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucImageTransPanel] Set target folder button clicked.");
            if (cb_TargetImageFolder.SelectedItem is string selectedFolder)
            {
                _settingsManager.SetValueToSection("ImageTrans", "Target", selectedFolder);
                MessageBox.Show($"감시 폴더가 설정되었습니다: {selectedFolder}", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _logManager.LogEvent($"[ucImageTransPanel] Target folder set to: {selectedFolder}");
                if (_isRunning)
                {
                    _logManager.LogDebug("[ucImageTransPanel] Restarting watcher due to folder change.");
                    StartWatchingFolder();
                }
            }
            else
            {
                MessageBox.Show("목록에서 폴더를 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Btn_FolderClear_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucImageTransPanel] Clear target folder button clicked.");
            cb_TargetImageFolder.SelectedIndex = -1;
            _settingsManager.RemoveKeyFromSection("ImageTrans", "Target");
            if (_isRunning)
            {
                _logManager.LogDebug("[ucImageTransPanel] Stopping watcher due to folder clear.");
                StopWatchingFolder();
            }
            MessageBox.Show("감시 폴더 설정이 초기화되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Btn_SetTime_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucImageTransPanel] Set wait time button clicked.");
            if (cb_WaitTime.SelectedItem is string selectedWaitTime)
            {
                _settingsManager.SetValueToSection("ImageTrans", "Wait", selectedWaitTime);
                MessageBox.Show($"대기 시간이 {selectedWaitTime}초로 설정되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _logManager.LogEvent($"[ucImageTransPanel] Wait time set to: {selectedWaitTime} seconds.");
            }
        }

        private void Btn_TimeClear_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucImageTransPanel] Clear wait time button clicked.");
            cb_WaitTime.SelectedIndex = 0; // 기본값 "30"
            _settingsManager.SetValueToSection("ImageTrans", "Wait", "30");
            MessageBox.Show("대기 시간이 기본값(30초)으로 초기화되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _logManager.LogEvent("[ucImageTransPanel] Wait time cleared to default (30 seconds).");
        }

        private void Btn_SelectOutputFolder_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucImageTransPanel] Select output folder button clicked.");
            string baseFolder = _configPanel.BaseFolderPath;
            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
            {
                _logManager.LogError("[ucImageTransPanel] Base Folder is not configured. Cannot select output folder.");
                MessageBox.Show("기준 폴더(Base Folder)가 먼저 설정되어야 합니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var folderDialog = new FolderBrowserDialog { SelectedPath = baseFolder })
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    lb_ImageSaveFolder.Text = folderDialog.SelectedPath;
                    _settingsManager.SetValueToSection("ImageTrans", "SaveFolder", folderDialog.SelectedPath);
                    _pdfMergeManager.UpdateOutputFolder(folderDialog.SelectedPath);
                    MessageBox.Show("PDF 출력 폴더가 설정되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _logManager.LogEvent($"[ucImageTransPanel] PDF output folder set to: {folderDialog.SelectedPath}");
                }
                else
                {
                    _logManager.LogDebug("[ucImageTransPanel] Select output folder dialog was canceled.");
                }
            }
        }

        #endregion

        #region --- File Watcher & Processing Logic ---

        private void StartWatchingFolder()
        {
            StopWatchingFolder(); // 기존 감시자 정리

            string targetFolder = _settingsManager.GetValueFromSection("ImageTrans", "Target");
            if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
            {
                _logManager.LogError($"[ucImageTransPanel] Cannot start watcher: Target folder '{targetFolder}' is not set or does not exist.");
                return;
            }

            try
            {
                _imageWatcher = new FileSystemWatcher(targetFolder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _imageWatcher.Created += OnImageFileEvent;
                _imageWatcher.Changed += OnImageFileEvent;
                _imageWatcher.Renamed += OnImageFileEvent;

                _logManager.LogEvent($"[ucImageTransPanel] Image folder watcher started for: {targetFolder}");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucImageTransPanel] Failed to create FileSystemWatcher for '{targetFolder}': {ex.Message}");
            }
        }

        private void StopWatchingFolder()
        {
            if (_imageWatcher != null)
            {
                _imageWatcher.EnableRaisingEvents = false;
                _imageWatcher.Dispose();
                _imageWatcher = null;
                _logManager.LogEvent("[ucImageTransPanel] Image folder watcher stopped.");
            }

            if (_checkTimer != null)
            {
                _checkTimer.Dispose();
                _checkTimer = null;
                _logManager.LogDebug("[ucImageTransPanel] File stability check timer stopped.");
            }

            lock (_changedFilesLock)
            {
                _changedFiles.Clear();
            }
        }

        private void OnImageFileEvent(object sender, FileSystemEventArgs e)
        {
            if (!_isRunning || !File.Exists(e.FullPath)) return;

            _logManager.LogDebug($"[ucImageTransPanel] File event '{e.ChangeType}' detected for: {e.FullPath}");

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(e.FullPath);

            // 병합 대상이 아닌 파일 형식 필터링 (예: 임시 파일)
            if (fileNameWithoutExt.Contains("_#1_"))
            {
                _logManager.LogDebug($"[ucImageTransPanel] File '{e.FullPath}' skipped because it contains '_#1_'.");
                return;
            }
            if (!Regex.IsMatch(fileNameWithoutExt, @"_(?<page>\d+)$"))
            {
                _logManager.LogDebug($"[ucImageTransPanel] File '{e.FullPath}' skipped because it does not match the page number pattern.");
                return;
            }

            _logManager.LogDebug($"[ucImageTransPanel] File '{e.FullPath}' is a valid target. Adding to changed files list.");
            lock (_changedFilesLock)
            {
                _changedFiles[e.FullPath] = DateTime.Now;
            }

            if (_checkTimer == null)
            {
                _logManager.LogDebug("[ucImageTransPanel] Stability check timer started.");
                _checkTimer = new System.Threading.Timer(_ => CheckFilesForMerging(), null, 1000, 1000);
            }
        }

        private void CheckFilesForMerging()
        {
            if (!_isRunning) return;

            int waitSec = GetWaitSecondsSafely();
            if (waitSec <= 0) return;

            _logManager.LogDebug($"[ucImageTransPanel] Checking for stable files with wait time: {waitSec}s. {_changedFiles.Count} files in queue.");

            var now = DateTime.Now;
            var filesToProcess = new List<string>();

            lock (_changedFilesLock)
            {
                var snapshot = _changedFiles.Keys.ToList();
                foreach (var filePath in snapshot)
                {
                    if ((now - _changedFiles[filePath]).TotalSeconds >= waitSec)
                    {
                        filesToProcess.Add(filePath);
                    }
                }

                filesToProcess.ForEach(p => _changedFiles.Remove(p));

                if (_changedFiles.Count == 0 && _checkTimer != null)
                {
                    _checkTimer.Dispose();
                    _checkTimer = null;
                    _logManager.LogDebug("[ucImageTransPanel] File queue is empty. Stability check timer stopped.");
                }
            }

            if (filesToProcess.Count == 0) return;

            var groupedFiles = filesToProcess
                .Select(p => new {
                    FullPath = p,
                    BaseName = Regex.Match(Path.GetFileNameWithoutExtension(p), @"^(?<base>.+?)_(?<page>\d+)$").Groups["base"].Value
                })
                .Where(x => !string.IsNullOrEmpty(x.BaseName))
                .GroupBy(x => x.BaseName);

            foreach (var group in groupedFiles)
            {
                _logManager.LogEvent($"[ucImageTransPanel] File group '{group.Key}' is stable. Starting merge process for {group.Count()} file(s).");
                MergeImagesForBaseName(group.Key);
            }
        }

        private int GetWaitSecondsSafely()
        {
            if (this.InvokeRequired)
            {
                try
                {
                    return (int)this.Invoke((Func<int>)GetWaitSecondsSafely);
                }
                catch (ObjectDisposedException)
                {
                    _logManager.LogDebug("[ucImageTransPanel] Control was disposed, cannot get wait time from UI.");
                    return 30; // 폼이 닫히는 중일 때 기본값 반환
                }
            }
            else
            {
                if (cb_WaitTime.SelectedItem is string selectedValue && int.TryParse(selectedValue, out int seconds))
                {
                    return seconds;
                }
                if (int.TryParse(_settingsManager.GetValueFromSection("ImageTrans", "Wait"), out int settingSeconds))
                {
                    return settingSeconds;
                }
                return 30;
            }
        }

        private void MergeImagesForBaseName(string baseName)
        {
            lock (_mergedBaseNames)
            {
                if (_mergedBaseNames.Contains(baseName))
                {
                    _logManager.LogDebug($"[ucImageTransPanel] Skip duplicate merge attempt for baseName: {baseName}");
                    return;
                }
                _mergedBaseNames.Add(baseName);
            }

            string watchFolder = _settingsManager.GetValueFromSection("ImageTrans", "Target");
            string outputFolder = _settingsManager.GetValueFromSection("ImageTrans", "SaveFolder");
            if (string.IsNullOrEmpty(outputFolder) || !Directory.Exists(outputFolder))
            {
                _logManager.LogDebug($"[ucImageTransPanel] Output folder is not set. Using watch folder '{watchFolder}' as fallback.");
                outputFolder = watchFolder;
            }

            string safeBaseName = baseName.Replace('.', '_');
            string outputPdfPath = Path.Combine(outputFolder, $"{safeBaseName}.pdf");

            try
            {
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" };

                _logManager.LogDebug($"[ucImageTransPanel] Searching for images with base name '{baseName}' in '{watchFolder}'.");
                var imageList = Directory.GetFiles(watchFolder, $"{baseName}_*.*")
                    .Where(p => imageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    .Select(p => {
                        var match = Regex.Match(Path.GetFileNameWithoutExtension(p), @"_(?<page>\d+)$");
                        return new
                        {
                            Path = p,
                            IsMatch = match.Success,
                            Page = match.Success ? int.Parse(match.Groups["page"].Value) : -1
                        };
                    })
                    .Where(x => x.IsMatch)
                    .OrderBy(x => x.Page)
                    .Select(x => x.Path)
                    .ToList();

                if (imageList.Count > 0)
                {
                    _logManager.LogEvent($"[ucImageTransPanel] Found {imageList.Count} images to merge into '{Path.GetFileName(outputPdfPath)}'.");
                    _pdfMergeManager.MergeImagesToPdf(imageList, outputPdfPath);
                }
                else
                {
                    _logManager.LogEvent($"[ucImageTransPanel] No image files found for merging with base name: {baseName}");
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucImageTransPanel] PDF merge failed for baseName '{baseName}': {ex.Message}");
            }
        }

        #endregion

        #region --- Public Control Methods ---

        public void UpdateStatusOnRun(bool isRunning)
        {
            _isRunning = isRunning;
            _logManager.LogEvent($"[ucImageTransPanel] Run status updated. IsRunning: {isRunning}");

            SetControlsEnabled(!isRunning);

            if (isRunning)
            {
                StartWatchingFolder();
            }
            else
            {
                StopWatchingFolder();
            }
        }

        private void SetControlsEnabled(bool isEnabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate { SetControlsEnabled(isEnabled); });
                return;
            }
            btn_SetFolder.Enabled = isEnabled;
            btn_FolderClear.Enabled = isEnabled;
            btn_SetTime.Enabled = isEnabled;
            btn_TimeClear.Enabled = isEnabled;
            btn_SelectOutputFolder.Enabled = isEnabled;
            cb_TargetImageFolder.Enabled = isEnabled;
            cb_WaitTime.Enabled = isEnabled;
        }

        public new void Dispose()
        {
            _logManager.LogDebug("[ucImageTransPanel] Disposing panel...");
            StopWatchingFolder();
            base.Dispose(true);
        }

        #endregion
    }
}
