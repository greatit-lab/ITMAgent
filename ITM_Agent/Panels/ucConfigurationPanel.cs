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
    /// <summary>
    /// 파일 분류 규칙(대상/제외 폴더, 기준 폴더, 정규식)을 설정하는 UI 패널입니다.
    /// 모든 설정은 ISettingsManager를 통해 읽고 씁니다.
    /// </summary>
    public partial class ucConfigurationPanel : UserControl
    {
        #region --- Fields & Events ---

        private readonly ISettingsManager _settingsManager;
        public event Action<string, Color> StatusUpdated;
        public event Action ListSelectionChanged;

        // Settings.ini의 섹션 이름을 상수로 관리
        private const string TargetFoldersSection = "[TargetFolders]";
        private const string ExcludeFoldersSection = "[ExcludeFolders]";
        private const string BaseFolderSection = "[BaseFolder]";

        #endregion

        #region --- Initialization ---

        public ucConfigurationPanel(ISettingsManager settingsManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            InitializeComponent();
            RegisterEventHandlers();
            LoadDataFromSettings();
        }

        private void RegisterEventHandlers()
        {
            // 폴더 관리 버튼
            btn_TargetFolder.Click += (s, e) => AddFolder(TargetFoldersSection, lb_TargetList);
            btn_TargetRemove.Click += (s, e) => RemoveSelectedFolders(TargetFoldersSection, lb_TargetList);
            btn_ExcludeFolder.Click += (s, e) => AddFolder(ExcludeFoldersSection, lb_ExcludeList);
            btn_ExcludeRemove.Click += (s, e) => RemoveSelectedFolders(ExcludeFoldersSection, lb_ExcludeList);
            btn_BaseFolder.Click += Btn_BaseFolder_Click;

            // 정규식 관리 버튼
            btn_RegAdd.Click += Btn_RegAdd_Click;
            btn_RegEdit.Click += Btn_RegEdit_Click;
            btn_RegRemove.Click += Btn_RegRemove_Click;

            // UI 상태 변경 감지
            lb_TargetList.SelectedIndexChanged += (s, e) => ValidateRunButtonState();
            lb_RegexList.SelectedIndexChanged += (s, e) => ValidateRunButtonState();
            lb_BaseFolder.TextChanged += (s, e) => ValidateRunButtonState();

            // 외부 알림 이벤트
            lb_TargetList.SelectedIndexChanged += (s, e) => ListSelectionChanged?.Invoke();
            lb_ExcludeList.SelectedIndexChanged += (s, e) => ListSelectionChanged?.Invoke();
            lb_RegexList.SelectedIndexChanged += (s, e) => ListSelectionChanged?.Invoke();
        }

        #endregion

        #region --- Data Loading and UI Refresh ---

        /// <summary>
        /// ISettingsManager로부터 모든 설정 값을 읽어와 UI에 표시합니다.
        /// </summary>
        public void LoadDataFromSettings()
        {
            LoadFolders(TargetFoldersSection, lb_TargetList);
            LoadFolders(ExcludeFoldersSection, lb_ExcludeList);
            LoadBaseFolder();
            LoadRegexFromSettings();
            ValidateRunButtonState();
        }

        private void LoadFolders(string section, ListBox listBox)
        {
            listBox.Items.Clear();
            var folders = _settingsManager.GetFoldersFromSection(section);
            for (int i = 0; i < folders.Count; i++)
            {
                listBox.Items.Add($"{i + 1} {folders[i]}");
            }
        }

        private void LoadBaseFolder()
        {
            var baseFolder = _settingsManager.GetBaseFolder();
            if (!string.IsNullOrEmpty(baseFolder))
            {
                lb_BaseFolder.Text = baseFolder;
                lb_BaseFolder.ForeColor = Color.Black;
            }
            else
            {
                lb_BaseFolder.Text = Resources.MSG_BASE_NOT_SELECTED;
                lb_BaseFolder.ForeColor = Color.Red;
            }
        }

        private void LoadRegexFromSettings()
        {
            lb_RegexList.Items.Clear();
            var regexDict = _settingsManager.GetRegexList();
            int index = 1;
            foreach (var kvp in regexDict)
            {
                lb_RegexList.Items.Add($"{index++} {kvp.Key} -> {kvp.Value}");
            }
        }

        /// <summary>
        /// 외부에서 UI를 새로고침할 필요가 있을 때 호출합니다.
        /// </summary>
        public void RefreshUI() => LoadDataFromSettings();

        #endregion

        #region --- Folder Management Logic ---

        private void AddFolder(string section, ListBox listBox)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedFolder = folderDialog.SelectedPath;
                    var currentFolders = _settingsManager.GetFoldersFromSection(section);

                    if (currentFolders.Any(f => f.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show("해당 폴더는 이미 등록되어 있습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    currentFolders.Add(selectedFolder);
                    _settingsManager.SetFoldersToSection(section, currentFolders);
                    LoadFolders(section, listBox); // UI 갱신
                }
            }
        }

        private void RemoveSelectedFolders(string section, ListBox listBox)
        {
            if (listBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("삭제할 폴더를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show("선택한 폴더를 정말 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var foldersToRemove = listBox.SelectedItems.Cast<string>()
                    .Select(item => item.Substring(item.IndexOf(' ') + 1))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var currentFolders = _settingsManager.GetFoldersFromSection(section);
                currentFolders.RemoveAll(f => foldersToRemove.Contains(f));

                _settingsManager.SetFoldersToSection(section, currentFolders);
                LoadFolders(section, listBox); // UI 갱신
            }
        }

        private void Btn_BaseFolder_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                string currentPath = _settingsManager.GetBaseFolder();
                folderDialog.SelectedPath = !string.IsNullOrEmpty(currentPath) ? currentPath : AppDomain.CurrentDomain.BaseDirectory;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    _settingsManager.SetBaseFolder(folderDialog.SelectedPath);
                    LoadBaseFolder(); // UI 갱신
                }
            }
        }

        #endregion

        #region --- Regex Management Logic ---

        private void Btn_RegAdd_Click(object sender, EventArgs e)
        {
            using (var form = new RegexConfigForm(_settingsManager.GetBaseFolder()))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var regexDict = _settingsManager.GetRegexList();
                    regexDict[form.RegexPattern] = form.TargetFolder;
                    _settingsManager.SetRegexList(regexDict);
                    LoadRegexFromSettings(); // UI 갱신
                }
            }
        }

        private void Btn_RegEdit_Click(object sender, EventArgs e)
        {
            if (lb_RegexList.SelectedItem == null)
            {
                MessageBox.Show("수정할 항목을 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var (oldRegex, oldFolder) = ParseSelectedRegexItem(lb_RegexList.SelectedItem.ToString());
            if (oldRegex == null) return;

            using (var form = new RegexConfigForm(_settingsManager.GetBaseFolder()) { RegexPattern = oldRegex, TargetFolder = oldFolder })
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var regexDict = _settingsManager.GetRegexList();
                    // 키가 변경되었을 수 있으므로 기존 키 삭제 후 새 키 추가
                    regexDict.Remove(oldRegex);
                    regexDict[form.RegexPattern] = form.TargetFolder;
                    _settingsManager.SetRegexList(regexDict);
                    LoadRegexFromSettings(); // UI 갱신
                }
            }
        }

        private void Btn_RegRemove_Click(object sender, EventArgs e)
        {
            if (lb_RegexList.SelectedItem == null)
            {
                MessageBox.Show("삭제할 항목을 선택하세요.", "경고", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show("선택한 항목을 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                var (regexToRemove, _) = ParseSelectedRegexItem(lb_RegexList.SelectedItem.ToString());
                if (regexToRemove == null) return;

                var regexDict = _settingsManager.GetRegexList();
                regexDict.Remove(regexToRemove);
                _settingsManager.SetRegexList(regexDict);
                LoadRegexFromSettings(); // UI 갱신
            }
        }

        private (string regex, string folder) ParseSelectedRegexItem(string item)
        {
            int arrowIndex = item.IndexOf("->");
            if (arrowIndex < 0) return (null, null);

            string regex = item.Substring(item.IndexOf(' ') + 1, arrowIndex - item.IndexOf(' ') - 2).Trim();
            string folder = item.Substring(arrowIndex + 2).Trim();
            return (regex, folder);
        }

        #endregion

        #region --- Public Methods & Properties for MainForm ---

        /// <summary>
        /// Run 버튼을 활성화하기 위한 모든 조건이 충족되었는지 확인합니다.
        /// </summary>
        public bool IsReadyToRun()
        {
            bool hasTarget = lb_TargetList.Items.Count > 0;
            bool hasBase = !string.IsNullOrEmpty(_settingsManager.GetBaseFolder());
            bool hasRegex = lb_RegexList.Items.Count > 0;
            return hasTarget && hasBase && hasRegex;
        }

        public string BaseFolderPath => _settingsManager.GetBaseFolder();

        /// <summary>
        /// 정규식에 설정된 모든 대상 폴더 경로의 고유 목록을 반환합니다.
        /// </summary>
        public List<string> GetRegexTargetFolders()
        {
            return _settingsManager.GetRegexList()
                .Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// MainForm의 Run/Stop 상태에 따라 UI 컨트롤의 활성화 상태를 업데이트합니다.
        /// </summary>
        public void UpdateStatusOnRun(bool isRunning)
        {
            SetControlsEnabled(!isRunning);
        }

        private void SetControlsEnabled(bool isEnabled)
        {
            // 모든 버튼과 리스트박스의 활성화 상태를 동기화
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

        /// <summary>
        /// MainForm의 상태를 기반으로 이 패널의 상태를 업데이트하고 UI에 반영합니다.
        /// </summary>
        private void ValidateRunButtonState()
        {
            if (IsReadyToRun())
            {
                StatusUpdated?.Invoke("Ready to Run", Color.Green);
            }
            else
            {
                StatusUpdated?.Invoke("Stopped", Color.Red);
            }
        }

        #endregion
    }
}