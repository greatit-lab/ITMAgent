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
        public event Action<bool> ReadyStatusChanged;

        // *** 버그 수정: 연쇄적인 이벤트 발생을 막기 위한 플래그 ***
        private bool _isUpdatingControls = false;

        #region --- Fields ---
        public event Action SettingsChanged;
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
            // 모든 설정 변경 버튼들은 각자의 로직 수행 후 OnSettingsChanged() 호출
            btn_TargetFolder.Click += (s, e) => AddFolder(TargetFoldersSection, lb_TargetList);
            btn_TargetRemove.Click += (s, e) => RemoveSelectedFolders(TargetFoldersSection, lb_TargetList);
            btn_ExcludeFolder.Click += (s, e) => AddFolder(ExcludeFoldersSection, lb_ExcludeList);
            btn_ExcludeRemove.Click += (s, e) => RemoveSelectedFolders(ExcludeFoldersSection, lb_ExcludeList);
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

        /// <summary>
        /// ISettingsManager로부터 모든 설정 값을 읽어와 UI에 표시합니다.
        /// </summary>
        public void LoadDataFromSettings()
        {
            LoadFolders(TargetFoldersSection, lb_TargetList);
            LoadFolders(ExcludeFoldersSection, lb_ExcludeList);
            LoadBaseFolder();
            LoadRegexFromSettings();
            OnSettingsChanged();
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
                    LoadFolders(section, listBox);
                    OnSettingsChanged();
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
                LoadFolders(section, listBox);
                OnSettingsChanged();
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
                    LoadBaseFolder();
                    OnSettingsChanged();
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
                    LoadRegexFromSettings();
                    OnSettingsChanged();
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

            // 수정된 부분:
            // 리스트박스의 선택된 문자열("1. regex -> C:\path")을 파싱하여
            // 기존 정규식과 폴더 경로를 추출합니다.
            var (oldRegex, oldFolder) = ParseSelectedRegexItem(lb_RegexList.SelectedItem.ToString());
            if (oldRegex == null) return; // 파싱 실패 시 중단

            using (var form = new RegexConfigForm(_settingsManager.GetBaseFolder()) { RegexPattern = oldRegex, TargetFolder = oldFolder })
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    var regexDict = _settingsManager.GetRegexList();

                    // 키(정규식)가 변경되었을 수 있으므로, 기존 키는 삭제하고 새로운 키로 값을 저장합니다.
                    regexDict.Remove(oldRegex);
                    regexDict[form.RegexPattern] = form.TargetFolder;

                    _settingsManager.SetRegexList(regexDict);
                    LoadRegexFromSettings(); // UI 목록 새로고침
                    OnSettingsChanged();     // MainForm에 상태 변경 알림
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
                // 수정된 부분:
                // 선택된 항목에서 삭제할 정규식(키)만 추출합니다.
                var (regexToRemove, _) = ParseSelectedRegexItem(lb_RegexList.SelectedItem.ToString());
                if (regexToRemove == null) return;

                var regexDict = _settingsManager.GetRegexList();
                if (regexDict.Remove(regexToRemove))
                {
                    _settingsManager.SetRegexList(regexDict);
                    LoadRegexFromSettings(); // UI 목록 새로고침
                    OnSettingsChanged();     // MainForm에 상태 변경 알림
                }
            }
        }

        private (string regex, string folder) ParseSelectedRegexItem(string item)
        {
            // 수정된 부분:
            // "->"를 기준으로 문자열을 분리하고, 앞뒤 공백을 제거하여
            // 정확한 정규식(key)과 폴더(value)를 추출합니다.
            int arrowIndex = item.IndexOf("->");
            if (arrowIndex < 0) return (null, null);

            // "N. " 부분을 제거하고 정규식 추출
            string regexPart = item.Substring(item.IndexOf(' ') + 1, arrowIndex - item.IndexOf(' ') - 2).Trim();
            string folderPart = item.Substring(arrowIndex + 2).Trim();

            return (regexPart, folderPart);
        }

        #endregion

        #region --- Public Methods & Properties for MainForm ---

        /// <summary>
        /// Run 버튼을 활성화하기 위한 모든 조건이 충족되었는지 확인합니다.
        /// </summary>
        public bool IsReadyToRun()
        {
            bool hasTarget = lb_TargetList.Items.Count > 0;
            // BaseFolder 텍스트가 리소스 문자열이 아닌 유효한 경로인지 확인
            bool hasBase = !string.IsNullOrEmpty(_settingsManager.GetBaseFolder()) && lb_BaseFolder.Text != Resources.MSG_BASE_NOT_SELECTED;
            bool hasRegex = lb_RegexList.Items.Count > 0;
            return hasTarget && hasBase && hasRegex;
        }

        public string BaseFolderPath => _settingsManager.GetBaseFolder();

        public List<string> GetRegexTargetFolders() => _settingsManager.GetRegexList().Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// MainForm의 Run/Stop 상태에 따라 UI 컨트롤의 활성화 상태를 업데이트합니다.
        /// </summary>
        public void UpdateStatusOnRun(bool isRunning)
        {
            SetControlsEnabled(!isRunning);
        }

        private void SetControlsEnabled(bool isEnabled)
        {
            // *** 버그 수정: 연쇄 이벤트 방지 플래그 설정 ***
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
                _isUpdatingControls = false; // 플래그 해제
            }
        }

        private void OnSettingsChanged()
        {
            // *** 버그 수정: 컨트롤이 프로그래밍 방식으로 업데이트 중일 때는 이벤트 발생 방지 ***
            if (_isUpdatingControls) return;

            SettingsChanged?.Invoke();
        }

        #endregion
    }
}
