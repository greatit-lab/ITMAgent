// ITM_Agent/Forms/MainForm.cs
using ITM_Agent.Common.Interfaces;
using ITM_Agent.Core;
using ITM_Agent.Panels; // 네임스페이스 변경
using ITM_Agent.Startup;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ITM_Agent.Forms
{
    /// <summary>
    /// 애플리케이션의 메인 UI 폼 클래스입니다.
    /// 모든 UI 패널을 관리하고, 사용자의 입력을 받아 Core 서비스에 전달하는 역할을 합니다.
    /// </summary>
    public partial class MainForm : Form
    {
        #region --- Services and Managers ---

        private readonly ISettingsManager _settingsManager;
        private readonly ILogManager _logManager;
        private readonly EqpidManager _eqpidManager;
        private readonly FileWatcherManager _fileWatcherManager;
        private readonly InfoRetentionCleaner _infoCleaner;

        #endregion

        #region --- UI Panels ---

        private ucConfigurationPanel _ucConfigPanel;
        private ucOverrideNamesPanel _ucOverrideNamesPanel;
        private ucImageTransPanel _ucImageTransPanel;
        private ucUploadPanel _ucUploadPanel;
        private ucPluginPanel _ucPluginPanel;
        private ucOptionPanel _ucOptionPanel;

        #endregion

        #region --- Form State ---

        private bool _isExiting = false;
        private bool _isRunning = false;
        private const string AppVersion = "v1.0.0";
        internal static string VersionInfo => AppVersion;

        #endregion

        #region --- Tray Icon Components ---

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _titleItem;
        private ToolStripMenuItem _runItem;
        private ToolStripMenuItem _stopItem;
        private ToolStripMenuItem _quitItem;

        #endregion

        // Designer.cs에서 생성된 컨트롤들을 위한 기본 생성자
        public MainForm()
        {
            InitializeComponent();
        }

        public MainForm(
            ISettingsManager settingsManager,
            ILogManager logManager,
            EqpidManager eqpidManager,
            FileWatcherManager fileWatcherManager,
            InfoRetentionCleaner infoCleaner)
        {
            // --- 서비스 의존성 주입 ---
            _settingsManager = settingsManager;
            _logManager = logManager;
            _eqpidManager = eqpidManager;
            _fileWatcherManager = fileWatcherManager;
            _infoCleaner = infoCleaner;

            InitializeComponent();
            InitializeApplication();
        }

        private void InitializeApplication()
        {
            this.Text = $"ITM Agent - {AppVersion}";
            this.Icon = new Icon(@"Resources\Icons\icon.ico");
            this.MaximizeBox = false;

            this.HandleCreated += (sender, e) => UpdateUIBasedOnSettings();
            this.FormClosing += MainForm_FormClosing;
            this.Load += MainForm_Load;

            _eqpidManager.InitializeEqpid();
            InitializeUserControls();
            RegisterMenuEvents();
            InitializeTrayIcon();
        }

        private void InitializeUserControls()
        {
            _ucConfigPanel = new ucConfigurationPanel(_settingsManager);
            _ucPluginPanel = new ucPluginPanel(_settingsManager, _logManager);
            _ucOverrideNamesPanel = new ucOverrideNamesPanel(_settingsManager, _ucConfigPanel, _logManager);
            _ucImageTransPanel = new ucImageTransPanel(_settingsManager, _ucConfigPanel, _logManager);
            _ucUploadPanel = new ucUploadPanel(_ucConfigPanel, _ucPluginPanel, _settingsManager, _ucOverrideNamesPanel, _logManager);
            _ucOptionPanel = new ucOptionPanel(_settingsManager);

            _ucOptionPanel.DebugModeChanged += isDebug =>
            {
                LogManager.GlobalDebugEnabled = isDebug;
                // FileWatcherManager의 디버그 모드는 더 이상 직접 제어하지 않음 (LogManager 전역 설정 사용)
                _logManager.LogEvent($"Debug Mode {(isDebug ? "Enabled" : "Disabled")}");
            };
        }

        #region --- UI Event Handlers (Buttons & Menus) ---

        private void btn_Run_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("Run button clicked.");
            try
            {
                _isRunning = true;
                _fileWatcherManager.StartWatching();
                PerformanceDbWriter.Start(lb_eqpid.Text, _eqpidManager, _logManager);
                UpdateMainStatus("Running...", Color.Blue);
            }
            catch (Exception ex)
            {
                _logManager.LogError($"Error starting monitoring: {ex.Message}");
                UpdateMainStatus("Error!", Color.Red);
            }
        }

        private void btn_Stop_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show("프로그램을 중지하시겠습니까?", "작업 중지 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;

            _logManager.LogEvent("Stop button clicked and confirmed.");
            try
            {
                _isRunning = false;
                _fileWatcherManager.StopWatchers();
                PerformanceDbWriter.Stop();
                UpdateUIBasedOnSettings();
            }
            catch (Exception ex)
            {
                _logManager.LogError($"Error stopping processes: {ex.Message}");
                UpdateMainStatus("Error Stopping!", Color.Red);
            }
        }

        private void btn_Quit_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show("프로그램을 완전히 종료하시겠습니까?", "종료 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                PerformQuit();
            }
        }

        private void NewMenuItem_Click(object sender, EventArgs e)
        {
            _settingsManager.ResetExceptEqpid();
            MessageBox.Show("설정이 초기화되었습니다 (Eqpid 제외).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshAllPanelsUI();
            UpdateUIBasedOnSettings();
        }

        private void OpenMenuItem_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _settingsManager.LoadFromFile(ofd.FileName);
                        MessageBox.Show("새로운 설정 파일을 로드했습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        RefreshAllPanelsUI();
                        UpdateUIBasedOnSettings();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"파일 로드 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void SaveAsMenuItem_Click(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog { Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _settingsManager.SaveToFile(sfd.FileName);
                        MessageBox.Show("설정 파일이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"파일 저장 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void QuitMenuItem_Click(object sender, EventArgs e) => btn_Quit.PerformClick();

        private void tsm_AboutInfo_Click(object sender, EventArgs e)
        {
            using (var aboutForm = new AboutInfoForm())
            {
                aboutForm.ShowDialog(this);
            }
        }

        #endregion

        #region --- Core Application Logic & State Management ---

        private void UpdateMainStatus(string status, Color color)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateMainStatus(status, color)));
                return;
            }

            ts_Status.Text = status;
            ts_Status.ForeColor = color;

            // 모든 UI 패널에 현재 실행 상태 전파
            _ucConfigPanel?.UpdateStatusOnRun(_isRunning);
            _ucOverrideNamesPanel?.UpdateStatusOnRun(_isRunning);
            _ucImageTransPanel?.UpdateStatusOnRun(_isRunning);
            _ucUploadPanel?.UpdateStatusOnRun(_isRunning);
            _ucPluginPanel?.UpdateStatusOnRun(_isRunning);
            _ucOptionPanel?.UpdateStatusOnRun(_isRunning);

            // 버튼 및 메뉴 활성화 상태 업데이트
            btn_Run.Enabled = !_isRunning && _ucConfigPanel.IsReadyToRun();
            btn_Stop.Enabled = _isRunning;
            btn_Quit.Enabled = !_isRunning;

            UpdateTrayMenuStatus();
            UpdateFileMenuItemsState(!_isRunning);
        }

        private void UpdateUIBasedOnSettings()
        {
            lb_eqpid.Text = $"Eqpid: {_settingsManager.GetEqpid()}";
            UpdateMenusBasedOnType();

            if (_isRunning) return;

            if (_ucConfigPanel.IsReadyToRun())
            {
                UpdateMainStatus("Ready to Run", Color.Green);
            }
            else
            {
                UpdateMainStatus("Stopped", Color.Red);
            }
        }

        private void PerformQuit()
        {
            if (_isExiting) return;
            _isExiting = true;

            _logManager.LogEvent("[MainForm] Quit requested.");

            // --- 추가된 부분 ---
            _ucUploadPanel?.CleanUp(); // ucUploadPanel의 리소스 정리
                                       // -----------------

            _fileWatcherManager.StopWatchers();
            PerformanceDbWriter.Stop();
            _infoCleaner?.Dispose();

            _trayIcon?.Dispose();

            Application.Exit();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            PerformanceWarmUp.Run();
            ShowUserControl(_ucConfigPanel); // 첫 화면으로 설정 패널 표시
        }

        #endregion

        #region --- UI Helper Methods (생략되었던 부분) ---

        private void RegisterMenuEvents()
        {
            // Common 메뉴
            tsm_Categorize.Click += (s, e) => ShowUserControl(_ucConfigPanel);
            tsm_Option.Click += (s, e) => ShowUserControl(_ucOptionPanel);
            // ONTO 메뉴
            tsm_OverrideNames.Click += (s, e) => ShowUserControl(_ucOverrideNamesPanel);
            tsm_ImageTrans.Click += (s, e) => ShowUserControl(_ucImageTransPanel);
            tsm_UploadData.Click += (s, e) => ShowUserControl(_ucUploadPanel);
            // Plugin 메뉴
            tsm_PluginList.Click += (s, e) => ShowUserControl(_ucPluginPanel);
            // About 메뉴
            tsm_AboutInfo.Click += tsm_AboutInfo_Click;
        }

        private void ShowUserControl(UserControl control)
        {
            if (control == null) return;

            pMain.Controls.Clear();
            control.Dock = DockStyle.Fill;
            pMain.Controls.Add(control);
        }

        private void RefreshAllPanelsUI()
        {
            _ucConfigPanel?.RefreshUI();
            _ucOverrideNamesPanel?.RefreshUI();
            _ucImageTransPanel?.RefreshUI();
        }

        private void UpdateMenusBasedOnType()
        {
            string type = _settingsManager.GetApplicationType();
            tsm_Onto.Visible = "ONTO".Equals(type, StringComparison.OrdinalIgnoreCase);
            tsm_Nova.Visible = "NOVA".Equals(type, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateFileMenuItemsState(bool isEnabled)
        {
            newToolStripMenuItem.Enabled = isEnabled;
            openToolStripMenuItem.Enabled = isEnabled;
            saveAsToolStripMenuItem.Enabled = isEnabled;
            quitToolStripMenuItem.Enabled = isEnabled;
        }

        #endregion

        #region --- Tray Icon Logic ---

        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _titleItem = new ToolStripMenuItem(this.Text) { Enabled = false };
            _runItem = new ToolStripMenuItem("Run", null, (s, e) => { if (btn_Run.Enabled) btn_Run.PerformClick(); });
            _stopItem = new ToolStripMenuItem("Stop", null, (s, e) => { if (btn_Stop.Enabled) btn_Stop.PerformClick(); });
            _quitItem = new ToolStripMenuItem("Quit", null, (s, e) => { if (btn_Quit.Enabled) btn_Quit.PerformClick(); });

            _trayMenu.Items.AddRange(new ToolStripItem[] {
                _titleItem, new ToolStripSeparator(), _runItem, _stopItem, _quitItem
            });

            _trayIcon = new NotifyIcon
            {
                Icon = this.Icon,
                ContextMenuStrip = _trayMenu,
                Visible = true,
                Text = this.Text
            };
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        private void UpdateTrayMenuStatus()
        {
            if (_runItem != null) _runItem.Enabled = btn_Run.Enabled;
            if (_stopItem != null) _stopItem.Enabled = btn_Stop.Enabled;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_isExiting)
            {
                e.Cancel = true;
                this.Hide();
                _trayIcon.ShowBalloonTip(2000, "ITM Agent", "백그라운드에서 실행 중입니다.", ToolTipIcon.Info);
            }
        }

        #endregion
    }
}