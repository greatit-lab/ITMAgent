// ITM_Agent/Panels/ucConfigurationPanel.cs
using ITM_Agent.Common.Interfaces;
using ITM_Agent.Forms;
using ITM_Agent.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ITM_Agent.Panels
{
    public partial class ucConfigurationPanel : UserControl
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ILogManager _logManager;
        public event Action<bool> ReadyStatusChanged;

        // *** 버그 수정: 연쇄적인 이벤트 발생을 막기 위한 플래그 ***
        private bool _isUpdatingControls = false;

        #region --- Fields ---
        public event Action SettingsChanged;

        // Settings.ini의 섹션 이름을 상수로 관리
        private const string TargetFoldersSection = "[TargetFolders]";
        private const string ExcludeFoldersSection = "[ExcludeFolders]";

        #endregion

        #region --- Initialization ---

        public ucConfigurationPanel(ISettingsManager settingsManager, ILogManager logManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

            InitializeComponent();
            RegisterEventHandlers();
            LoadDataFromSettings();
        }

        private void RegisterEventHandlers()
        {
            btn_TargetFolder.Click += (s, e) => AddFolder(TargetFoldersSection, lb_TargetList, "Target");
            btn_TargetRemove.Click += (s, e) => RemoveSelectedFolders(TargetFoldersSection, lb_TargetList, "Target");
            btn_ExcludeFolder.Click += (s, e) => AddFolder(ExcludeFoldersSection, lb_ExcludeList, "Exclude");
            btn_ExcludeRemove.Click += (s, e) => RemoveSelectedFolders(ExcludeFoldersSection, lb_ExcludeList, "Exclude");
            btn_BaseFolder.Click += Btn_BaseFolder_Click;
            btn_RegAdd.Click += Btn_RegAdd_Click;
            btn_RegEdit.Click += Btn_RegEdit_Click;
            btn_RegRemove.Click += Btn_RegRemove_Click;

            // 사용자가 직접 리스트 선택을 변경할 때만 상태 변경 알림
            lb_TargetList.SelectedIndexChanged += (s, e) => OnSettingsChanged();
            lb_ExcludeList.SelectedIndexChanged += (s, e) => OnSettingsChanged();
            lb_RegexList.SelectedIndexChanged += (s, e) => OnSettingsChanged();
        }

        #endregion

        #region --- Data Loading and UI Refresh ---

        public void LoadDataFromSettings()
        {
            _logManager.LogDebug("[ucConfigurationPanel] Loading all data from settings.");
            LoadFolders(TargetFoldersSection, lb_TargetList);
            LoadFolders(ExcludeFoldersSection, lb_ExcludeList);
            LoadBaseFolder();
            LoadRegexFromSettings();
            OnSettingsChanged(); // 로드 후 상태 갱신
            _logManager.LogDebug("[ucConfigurationPanel] Finished loading data from settings.");
        }

        private void LoadFolders(string section, ListBox listBox)
        {
            _logManager.LogDebug($"[ucConfigurationPanel] Loading folders for section '{section}' into '{listBox.Name}'.");
            listBox.Items.Clear();
            var folders = _settingsManager.GetFoldersFromSection(section);
            for (int i = 0; i < folders.Count; i++)
            {
                listBox.Items.Add($"{i + 1} {folders[i]}");
            }
        }

        private void LoadBaseFolder()
        {
            _logManager.LogDebug("[ucConfigurationPanel] Loading BaseFolder.");
            var baseFolder = _settingsManager.GetBaseFolder();
            if (!string.IsNullOrEmpty(baseFolder))
            {
                lb_BaseFolder.Text = baseFolder;
                lb_BaseFolder.ForeColor = Color.Black;
                _logManager.LogDebug($"[ucConfigurationPanel] BaseFolder is '{baseFolder}'.");
            }
            else
            {
                lb_BaseFolder.Text = Resources.MSG_BASE_NOT_SELECTED;
                lb_BaseFolder.ForeColor = Color.Red;
                _logManager.LogDebug("[ucConfigurationPanel] BaseFolder is not set.");
            }
        }

        private void LoadRegexFromSettings()
        {
            _logManager.LogDebug("[ucConfigurationPanel] Loading regex list from settings.");
            lb_RegexList.Items.Clear();
            var regexDict = _settingsManager.GetRegexList();
            int index = 1;
            foreach (var kvp in regexDict)
            {
                lb_RegexList.Items.Add($"{index++} {kvp.Key} -> {kvp.Value}");
            }
        }

        public void RefreshUI()
        {
            _logManager.LogEvent("[ucConfigurationPanel] RefreshUI called externally.");
            LoadDataFromSettings();
        }

        #endregion

        #region --- Folder Management Logic ---

        private void AddFolder(string section, ListBox listBox, string folderType)
        {
            _logManager.LogEvent($"[ucConfigurationPanel] '{folderType}' folder add button clicked.");
            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedFolder = folderDialog.SelectedPath;
                    _logManager.LogDebug($"[ucConfigurationPanel] User selected folder: {selectedFolder}");
                    var currentFolders = _settingsManager.GetFoldersFromSection(section);

                    if (currentFolders.Any(f => f.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logManager.LogDebug($"[ucConfigurationPanel] Folder '{selectedFolder}' already exists in the list.");
                        MessageBox.Show("해당 폴더는 이미 등록되어 있습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    currentFolders.Add(selectedFolder);
                    _settingsManager.SetFoldersToSection(section, currentFolders);
                    LoadFolders(section, listBox);
                    OnSettingsChanged();
                    _logManager.LogEvent($"[ucConfigurationPanel] Added new {folderType} folder: {selectedFolder}");
                }
                else
                {
                    _logManager.LogDebug("[ucConfigurationPanel] Folder dialog was canceled.");
                }
            }
        }

        private void RemoveSelectedFolders(string section, ListBox listBox, string folderType)
        {
            if (listBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("삭제할 폴더를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _logManager.LogEvent($"[ucConfigurationPanel] '{folderType}' folder remove button clicked.");
            if (MessageBox.Show("선택한 폴더를 정말 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var foldersToRemove = listBox.SelectedItems.Cast<string>()
                    .Select(item => item.Substring(item.IndexOf(' ') + 1))
                    .ToList();
                
                _logManager.LogDebug($"[ucConfigurationPanel] User confirmed removal of {foldersToRemove.Count} folder(s).");

                var currentFolders = _settingsManager.GetFoldersFromSection(section);
                currentFolders.RemoveAll(f => foldersToRemove.Contains(f, StringComparer.OrdinalIgnoreCase));

                _settingsManager.SetFoldersToSection(section, currentFolders);
                LoadFolders(section, listBox);
                OnSettingsChanged();
                _logManager.LogEvent($"[ucConfigurationPanel] Removed {foldersToRemove.Count} {folderType} folder(s).");
            }
            else
            {
                _logManager.LogDebug("[ucConfigurationPanel] Folder removal was canceled by user.");
            }
        }

        private void Btn_BaseFolder_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucConfigurationPanel] BaseFolder select button clicked.");
            using (var folderDialog = new FolderBrowserDialog())
            {
                string currentPath = _settingsManager.GetBaseFolder();
                folderDialog.SelectedPath = !string.IsNullOrEmpty(currentPath) ? currentPath : AppDomain.CurrentDomain.BaseDirectory;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    _settingsManager.SetBaseFolder(folderDialog.SelectedPath);
                    LoadBaseFolder();
                    OnSettingsChanged();
                    _logManager.LogEvent($"[ucConfigurationPanel] BaseFolder set to: {folderDialog.SelectedPath}");
                }
                else
                {
                    _logManager.LogDebug("[ucConfigurationPanel] BaseFolder dialog was canceled.");
                }
            }
        }

        #endregion

        #region --- Regex Management Logic ---

        private void Btn_RegAdd_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucConfigurationPanel] Regex Add button clicked.");
            using (var form = new RegexConfigForm(_settingsManager.GetBaseFolder()))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var regexDict = _settingsManager.GetRegexList();
                    regexDict[form.RegexPattern] = form.TargetFolder;
                    _settingsManager.SetRegexList(regexDict);
                    LoadRegexFromSettings();
                    OnSettingsChanged();
                    _logManager.LogEvent($"[ucConfigurationPanel] Added new regex: '{form.RegexPattern}' -> '{form.TargetFolder}'");
                }
            }
        }

        private void Btn_RegEdit_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucConfigurationPanel] Regex Edit button clicked.");
            if (lb_RegexList.SelectedItem == null)
            {
                MessageBox.Show("수정할 항목을 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var (oldRegex, oldFolder) = ParseSelectedRegexItem(lb_RegexList.SelectedItem.ToString());
            if (oldRegex == null)
            {
                _logManager.LogError($"[ucConfigurationPanel] Failed to parse selected regex item: '{lb_RegexList.SelectedItem}'");
                return;
            }

            using (var form = new RegexConfigForm(_settingsManager.GetBaseFolder()) { RegexPattern = oldRegex, TargetFolder = oldFolder })
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    _logManager.LogDebug($"[ucConfigurationPanel] Editing regex. Old: '{oldRegex}', New: '{form.RegexPattern}'");
                    var regexDict = _settingsManager.GetRegexList();
                    regexDict.Remove(oldRegex);
                    regexDict[form.RegexPattern] = form.TargetFolder;
                    _settingsManager.SetRegexList(regexDict);
                    LoadRegexFromSettings();
                    OnSettingsChanged();
                    _logManager.LogEvent($"[ucConfigurationPanel] Edited regex: '{form.RegexPattern}' -> '{form.TargetFolder}'");
                }
            }
        }

        private void Btn_RegRemove_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucConfigurationPanel] Regex Remove button clicked.");
            if (lb_RegexList.SelectedItem == null)
            {
                MessageBox.Show("삭제할 항목을 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show("선택한 항목을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var (regexToRemove, _) = ParseSelectedRegexItem(lb_RegexList.SelectedItem.ToString());
                if (regexToRemove == null)
                {
                    _logManager.LogError($"[ucConfigurationPanel] Failed to parse selected regex item for removal: '{lb_RegexList.SelectedItem}'");
                    return;
                }

                _logManager.LogDebug($"[ucConfigurationPanel] User confirmed removal of regex: '{regexToRemove}'");
                var regexDict = _settingsManager.GetRegexList();
                if (regexDict.Remove(regexToRemove))
                {
                    _settingsManager.SetRegexList(regexDict);
                    LoadRegexFromSettings();
                    OnSettingsChanged();
                    _logManager.LogEvent($"[ucConfigurationPanel] Removed regex: '{regexToRemove}'");
                }
            }
            else
            {
                _logManager.LogDebug("[ucConfigurationPanel] Regex removal was canceled by user.");
            }
        }

        private (string regex, string folder) ParseSelectedRegexItem(string item)
        {
            try
            {
                int arrowIndex = item.IndexOf("->");
                if (arrowIndex < 0) return (null, null);

                string regexPart = item.Substring(item.IndexOf(' ') + 1, arrowIndex - item.IndexOf(' ') - 2).Trim();
                string folderPart = item.Substring(arrowIndex + 2).Trim();

                return (regexPart, folderPart);
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucConfigurationPanel] Error parsing regex item '{item}': {ex.Message}");
                return (null, null);
            }
        }

        #endregion

        #region --- Public Methods & Properties for MainForm ---

        public bool IsReadyToRun()
        {
            bool hasTarget = lb_TargetList.Items.Count > 0;
            bool hasBase = !string.IsNullOrEmpty(_settingsManager.GetBaseFolder()) && lb_BaseFolder.Text != Resources.MSG_BASE_NOT_SELECTED;
            bool hasRegex = lb_RegexList.Items.Count > 0;
            _logManager.LogDebug($"[ucConfigurationPanel] IsReadyToRun check: hasTarget={hasTarget}, hasBase={hasBase}, hasRegex={hasRegex}. Result={hasTarget && hasBase && hasRegex}");
            return hasTarget && hasBase && hasRegex;
        }

        public string BaseFolderPath => _settingsManager.GetBaseFolder();

        public List<string> GetRegexTargetFolders() => _settingsManager.GetRegexList().Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public void UpdateStatusOnRun(bool isRunning)
        {
            _logManager.LogDebug($"[ucConfigurationPanel] Updating control enabled status. IsRunning: {isRunning}");
            SetControlsEnabled(!isRunning);
        }

        private void SetControlsEnabled(bool isEnabled)
        {
            _isUpdatingControls = true;
            try
            {
                btn_TargetFolder.Enabled = isEnabled;
                btn_TargetRemove.Enabled = isEnabled;
                btn_ExcludeFolder.Enabled = isEnabled;
                btn_ExcludeRemove.Enabled = isEnabled;
                btn_BaseFolder.Enabled = isEnabled;
                btn_RegAdd.Enabled = isEnabled;
                btn_RegEdit.Enabled = isEnabled;
                btn_RegRemove.Enabled = isEnabled;
                lb_TargetList.Enabled = isEnabled;
                lb_ExcludeList.Enabled = isEnabled;
                lb_RegexList.Enabled = isEnabled;
            }
            finally
            {
                _isUpdatingControls = false;
            }
        }

        private void OnSettingsChanged()
        {
            if (_isUpdatingControls) return;

            _logManager.LogDebug("[ucConfigurationPanel] Settings changed, invoking events.");
            SettingsChanged?.Invoke();
            ReadyStatusChanged?.Invoke(IsReadyToRun());
        }

        #endregion
    }
}
