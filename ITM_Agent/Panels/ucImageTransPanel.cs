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
            LoadRegexFolderPaths();
            LoadWaitTimes();
            LoadOutputFolder();
            _logManager.LogEvent("[ucImageTransPanel] All settings loaded.");
        }

        public void LoadRegexFolderPaths()
        {
            cb_TargetImageFolder.Items.Clear();
            var regexFolders = _configPanel.GetRegexTargetFolders();
            cb_TargetImageFolder.Items.AddRange(regexFolders.ToArray());

            string selectedPath = _settingsManager.GetValueFromSection("ImageTrans", "Target");
            if (!string.IsNullOrEmpty(selectedPath) && cb_TargetImageFolder.Items.Contains(selectedPath))
            {
                cb_TargetImageFolder.SelectedItem = selectedPath;
            }
            else
            {
                cb_TargetImageFolder.SelectedIndex = -1;
            }
        }

        public void LoadWaitTimes()
        {
            cb_WaitTime.Items.Clear();
            cb_WaitTime.Items.AddRange(new object[] { "30", "60", "120", "180", "240", "300" });

            string savedWaitTime = _settingsManager.GetValueFromSection("ImageTrans", "Wait");
            cb_WaitTime.SelectedItem = savedWaitTime;
            if (cb_WaitTime.SelectedIndex < 0) cb_WaitTime.SelectedIndex = 0;
        }

        private void LoadOutputFolder()
        {
            string outputFolder = _settingsManager.GetValueFromSection("ImageTrans", "SaveFolder");
            if (!string.IsNullOrEmpty(outputFolder) && Directory.Exists(outputFolder))
            {
                lb_ImageSaveFolder.Text = outputFolder;
                _pdfMergeManager.UpdateOutputFolder(outputFolder);
            }
            else
            {
                lb_ImageSaveFolder.Text = "출력 폴더가 설정되지 않았습니다.";
            }
        }

        public void RefreshUI() => LoadAllSettings();

        #endregion

        #region --- Event Handlers ---

        private void Btn_SetFolder_Click(object sender, EventArgs e)
        {
            if (cb_TargetImageFolder.SelectedItem is string selectedFolder)
            {
                _settingsManager.SetValueToSection("ImageTrans", "Target", selectedFolder);
                MessageBox.Show($"감시 폴더가 설정되었습니다: {selectedFolder}", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (_isRunning) StartWatchingFolder();
            }
            else
            {
                MessageBox.Show("목록에서 폴더를 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Btn_FolderClear_Click(object sender, EventArgs e)
        {
            cb_TargetImageFolder.SelectedIndex = -1;
            _settingsManager.RemoveKeyFromSection("ImageTrans", "Target");
            if (_isRunning) StopWatchingFolder();
            MessageBox.Show("감시 폴더 설정이 초기화되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Btn_SetTime_Click(object sender, EventArgs e)
        {
            if (cb_WaitTime.SelectedItem is string selectedWaitTime)
            {
                _settingsManager.SetValueToSection("ImageTrans", "Wait", selectedWaitTime);
                MessageBox.Show($"대기 시간이 {selectedWaitTime}초로 설정되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void Btn_TimeClear_Click(object sender, EventArgs e)
        {
            cb_WaitTime.SelectedIndex = 0;
            _settingsManager.SetValueToSection("ImageTrans", "Wait", "30");
            MessageBox.Show("대기 시간이 기본값(30초)으로 초기화되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Btn_SelectOutputFolder_Click(object sender, EventArgs e)
        {
            string baseFolder = _configPanel.BaseFolderPath;
            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
            {
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
                }
            }
        }

        #endregion

        #region --- File Watcher & Processing Logic ---

        private void StartWatchingFolder()
        {
            StopWatchingFolder();

            string targetFolder = _settingsManager.GetValueFromSection("ImageTrans", "Target");
            if (string.IsNullOrEmpty(targetFolder) || !Directory.Exists(targetFolder))
            {
                _logManager.LogError("[ucImageTransPanel] 감시 대상 폴더가 설정되지 않았거나 존재하지 않아 감시를 시작할 수 없습니다.");
                return;
            }

            _imageWatcher = new FileSystemWatcher(targetFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _imageWatcher.Created += OnImageFileEvent;
            _imageWatcher.Changed += OnImageFileEvent;
            _imageWatcher.Renamed += OnImageFileEvent;

            _logManager.LogEvent($"[ucImageTransPanel] 이미지 폴더 감시 시작: {targetFolder}");
        }

        private void StopWatchingFolder()
        {
            _imageWatcher?.Dispose();
            _imageWatcher = null;

            _checkTimer?.Dispose();
            _checkTimer = null;

            lock (_changedFilesLock)
            {
                _changedFiles.Clear();
            }
        }

        private void OnImageFileEvent(object sender, FileSystemEventArgs e)
        {
            if (!_isRunning || !File.Exists(e.FullPath)) return;

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(e.FullPath);

            if (fileNameWithoutExt.Contains("_#1_")) return;
            if (!Regex.IsMatch(fileNameWithoutExt, @"_(?<page>\d+)$")) return;

            lock (_changedFilesLock)
            {
                _changedFiles[e.FullPath] = DateTime.Now;
            }

            if (_checkTimer == null)
            {
                // CS0104 오류 해결: System.Threading.Timer를 명시적으로 사용
                _checkTimer = new System.Threading.Timer(_ => CheckFilesForMerging(), null, 1000, 1000);
            }
        }

        private void CheckFilesForMerging()
        {
            if (!_isRunning) return;

            int waitSec = GetWaitSecondsSafely();
            if (waitSec <= 0) return;

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

                if (_changedFiles.Count == 0)
                {
                    _checkTimer?.Dispose();
                    _checkTimer = null;
                }
            }

            var groupedFiles = filesToProcess
                .Select(p => new {
                    FullPath = p,
                    BaseName = Regex.Match(Path.GetFileNameWithoutExtension(p), @"^(?<base>.+?)_(?<page>\d+)$").Groups["base"].Value
                })
                .Where(x => !string.IsNullOrEmpty(x.BaseName))
                .GroupBy(x => x.BaseName);

            foreach (var group in groupedFiles)
            {
                MergeImagesForBaseName(group.Key);
            }
        }

        /// <summary>
        /// 크로스 스레드 오류를 방지하며 안전하게 cb_WaitTime 컨트롤의 선택된 값을 초 단위로 반환합니다.
        /// </summary>
        private int GetWaitSecondsSafely()
        {
            // *** 수정된 부분: 크로스 스레드 오류를 해결하는 핵심 로직입니다. ***
            // 현재 스레드가 UI 스레드가 아닌지 (InvokeRequired) 확인합니다.
            if (this.InvokeRequired)
            {
                // UI 스레드가 아니라면, Invoke를 통해 UI 스레드에 작업을 위임하고
                // 그 결과(대기 시간 값)를 안전하게 반환받습니다.
                return (int)this.Invoke((Func<int>)GetWaitSecondsSafely);
            }
            else
            {
                // 이미 UI 스레드라면, 컨트롤에 직접 안전하게 접근합니다.
                if (cb_WaitTime.SelectedItem is string selectedValue && int.TryParse(selectedValue, out int seconds))
                {
                    return seconds;
                }

                // UI에서 값을 가져오지 못한 경우, 설정 파일에서 값을 읽어옵니다.
                if (int.TryParse(_settingsManager.GetValueFromSection("ImageTrans", "Wait"), out int settingSeconds))
                {
                    return settingSeconds;
                }

                // 모든 경우에 실패하면 기본값 30초를 반환합니다.
                return 30;
            }
        }

        /// <summary>
        /// 동일한 BaseName을 가진 이미지 파일 그룹을 찾아 PDF로 병합합니다.
        /// </summary>
        /// <param name="baseName">파일 이름에서 페이지 번호를 제외한 공통 부분입니다.</param>
        private void MergeImagesForBaseName(string baseName)
        {
            // *** 수정된 부분: 누락되었던 메서드 전체를 추가합니다. ***

            // 1. 중복 병합 방지: 이미 이 baseName으로 병합을 시도했다면 건너뜁니다.
            lock (_mergedBaseNames)
            {
                if (_mergedBaseNames.Contains(baseName))
                {
                    _logManager.LogDebug($"[ucImageTransPanel] Skip duplicate merge attempt for baseName: {baseName}");
                    return;
                }
                _mergedBaseNames.Add(baseName);
            }

            // 2. 병합에 필요한 정보(감시 폴더, 출력 폴더 등)를 설정에서 가져옵니다.
            string watchFolder = _settingsManager.GetValueFromSection("ImageTrans", "Target");
            string outputFolder = _settingsManager.GetValueFromSection("ImageTrans", "SaveFolder");
            if (string.IsNullOrEmpty(outputFolder) || !Directory.Exists(outputFolder))
            {
                outputFolder = watchFolder; // 출력 폴더가 없으면 감시 폴더를 사용
            }

            // 파일 이름에 포함될 수 있는 '.' 문자를 '_'로 치환하여 안전한 파일명 생성
            string safeBaseName = baseName.Replace('.', '_');
            string outputPdfPath = Path.Combine(outputFolder, $"{safeBaseName}.pdf");

            try
            {
                // 3. 지원하는 이미지 확장자 목록을 정의합니다.
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" };

                // 4. 감시 폴더에서 baseName을 포함하는 모든 이미지 파일을 찾아서 페이지 번호순으로 정렬합니다.
                var imageList = Directory.GetFiles(watchFolder, $"{baseName}_*.*")
                    .Where(p => imageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    .Select(p => {
                        var match = Regex.Match(Path.GetFileNameWithoutExtension(p), @"_(?<page>\d+)$");
                        return new {
                            Path = p,
                            IsMatch = match.Success,
                            Page = match.Success ? int.Parse(match.Groups["page"].Value) : -1
                        };
                    })
                    .Where(x => x.IsMatch)
                    .OrderBy(x => x.Page)
                    .Select(x => x.Path)
                    .ToList();

                // 5. 병합할 이미지가 있는 경우, PdfMergeManager에 작업을 위임합니다.
                if (imageList.Count > 0)
                {
                    _pdfMergeManager.MergeImagesToPdf(imageList, outputPdfPath);
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucImageTransPanel] PDF merge failed for baseName '{baseName}': {ex.Message}");
            }
        }

        private void MergeImageGroup(string baseName)
        {
            lock (_mergedBaseNames)
            {
                if (_mergedBaseNames.Contains(baseName)) return;
                _mergedBaseNames.Add(baseName);
            }

            string watchFolder = _settingsManager.GetValueFromSection("ImageTrans", "Target");
            string outputFolder = _settingsManager.GetValueFromSection("ImageTrans", "SaveFolder") ?? watchFolder;
            string safeBaseName = baseName.Replace('.', '_');
            string outputPdfPath = Path.Combine(outputFolder, $"{safeBaseName}.pdf");

            try
            {
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp" };
                var imageList = Directory.GetFiles(watchFolder, $"{baseName}_*.*")
                    .Where(p => imageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    .Select(p => new { Path = p, Page = int.Parse(Regex.Match(Path.GetFileNameWithoutExtension(p), @"_(?<page>\d+)$").Groups["page"].Value) })
                    .OrderBy(x => x.Page)
                    .Select(x => x.Path)
                    .ToList();

                if (imageList.Count > 0)
                {
                    _pdfMergeManager.MergeImagesToPdf(imageList, outputPdfPath);
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucImageTransPanel] PDF 병합 실패 (BaseName: {baseName}): {ex.Message}");
            }
            finally
            {
                // 한 번 시도한 그룹은 다음 감지 시 중복 실행되지 않도록 함
                // 성공 여부와 관계없이 baseName을 제거하지 않아야 중복 방지됨
            }
        }

        #endregion

        #region --- Public Control Methods ---

        public void UpdateStatusOnRun(bool isRunning)
        {
            _isRunning = isRunning;

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
            StopWatchingFolder();
            base.Dispose();
        }

        #endregion
    }
}
