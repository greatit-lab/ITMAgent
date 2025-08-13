// ITM_Agent/Panels/ucOptionPanel.cs
using ITM_Agent.Common.Interfaces;
using System;
using System.Windows.Forms;

namespace ITM_Agent.Panels
{
    /// <summary>
    /// 로깅 및 데이터 보존 관련 옵션을 설정하는 UI 패널입니다.
    /// </summary>
    public partial class ucOptionPanel : UserControl
    {
        private readonly ISettingsManager _settingsManager;
        private bool _isInitializing = true; // 초기 로드 시 이벤트 발생을 막기 위한 플래그

        /// <summary>
        /// 디버그 모드 체크 상태가 변경될 때 발생하는 이벤트입니다.
        /// </summary>
        public event Action<bool> DebugModeChanged;

        public ucOptionPanel(ISettingsManager settingsManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            InitializeComponent();
            InitializeControls();
            RegisterEventHandlers();
            LoadSettings();
            _isInitializing = false;
        }

        /// <summary>
        /// 컨트롤의 초기 상태(예: 콤보박스 아이템)를 설정합니다.
        /// </summary>
        private void InitializeControls()
        {
            cb_info_Retention.Items.Clear();
            cb_info_Retention.Items.AddRange(new object[] { "1", "3", "5" });
            cb_info_Retention.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        /// <summary>
        /// 컨트롤의 이벤트 핸들러를 등록합니다.
        /// </summary>
        private void RegisterEventHandlers()
        {
            // 모든 컨트롤의 값 변경 이벤트를 하나의 핸들러로 통합
            chk_DebugMode.CheckedChanged += OnSettingsChanged;
            chk_PerfoMode.CheckedChanged += OnSettingsChanged;
            chk_infoDel.CheckedChanged += OnSettingsChanged;
            cb_info_Retention.SelectedIndexChanged += OnSettingsChanged;
        }

        /// <summary>
        /// ISettingsManager로부터 설정 값을 읽어와 UI에 반영합니다.
        /// </summary>
        private void LoadSettings()
        {
            chk_DebugMode.Checked = _settingsManager.IsDebugMode;
            chk_PerfoMode.Checked = _settingsManager.IsPerformanceLogging;
            chk_infoDel.Checked = _settingsManager.IsInfoDeletionEnabled;

            string retentionDays = _settingsManager.InfoRetentionDays.ToString();
            if (cb_info_Retention.Items.Contains(retentionDays))
            {
                cb_info_Retention.SelectedItem = retentionDays;
            }
            else
            {
                // 설정값이 1,3,5가 아니면 기본값 "1" 선택
                if (chk_infoDel.Checked)
                {
                    cb_info_Retention.SelectedItem = "1";
                }
                else
                {
                    cb_info_Retention.SelectedIndex = -1; // 선택 없음
                }
            }
            UpdateRetentionControlsState();
        }

        /// <summary>
        /// UI 컨트롤의 값이 변경될 때마다 호출되어 설정을 저장합니다.
        /// </summary>
        private void OnSettingsChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;

            // 디버그 모드 설정 저장 및 이벤트 발생
            _settingsManager.IsDebugMode = chk_DebugMode.Checked;
            DebugModeChanged?.Invoke(chk_DebugMode.Checked);

            // 성능 로그 설정 저장
            _settingsManager.IsPerformanceLogging = chk_PerfoMode.Checked;

            // 자동 삭제 설정 저장
            bool isDeletionEnabled = chk_infoDel.Checked;
            _settingsManager.IsInfoDeletionEnabled = isDeletionEnabled;

            if (isDeletionEnabled)
            {
                // 기능 활성화: 선택된 값이 없으면 "1"로 기본 설정
                if (cb_info_Retention.SelectedIndex < 0)
                {
                    cb_info_Retention.SelectedItem = "1";
                }
                _settingsManager.InfoRetentionDays = int.Parse(cb_info_Retention.SelectedItem.ToString());
            }
            else
            {
                // *** 기능 비활성화: 설정값을 0으로 변경하고 콤보박스 선택 해제 (누락되었던 로직) ***
                _settingsManager.InfoRetentionDays = 0; // 비활성화 상태를 0으로 저장
                cb_info_Retention.SelectedIndex = -1; // UI 선택 초기화
            }

            // UI 컨트롤 상태 업데이트
            UpdateRetentionControlsState();
        }

        /// <summary>
        /// '자동 삭제' 체크박스 상태에 따라 보존 기간 관련 컨트롤의 활성화 여부를 업데이트합니다.
        /// </summary>
        private void UpdateRetentionControlsState()
        {
            bool isEnabled = chk_infoDel.Checked;
            label3.Enabled = isEnabled;
            label4.Enabled = isEnabled;
            cb_info_Retention.Enabled = isEnabled;
        }

        /// <summary>
        /// MainForm의 Run/Stop 상태에 따라 UI 컨트롤의 활성화 상태를 업데이트합니다.
        /// </summary>
        public void UpdateStatusOnRun(bool isRunning)
        {
            SetControlsEnabled(!isRunning);
        }

        private void SetControlsEnabled(bool enabled)
        {
            chk_DebugMode.Enabled = enabled;
            chk_PerfoMode.Enabled = enabled;
            chk_infoDel.Enabled = enabled;

            // 보존 기간 컨트롤은 '자동 삭제'가 체크되어 있고, '실행 중'이 아닐 때만 활성화
            if (enabled)
            {
                UpdateRetentionControlsState();
            }
            else
            {
                cb_info_Retention.Enabled = false;
            }
        }
    }
}