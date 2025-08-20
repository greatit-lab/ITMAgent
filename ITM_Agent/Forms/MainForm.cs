// ITM_Agent/Forms/MainForm.cs
using ITM_Agent.Common.Interfaces;
using ITM_Agent.Core;
using ITM_Agent.Panels;
using ITM_Agent.Startup;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITM_Agent.Forms
{
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

        public MainForm()
        {
            // 이 생성자는 디자인 타임에만 사용됩니다.
            InitializeComponent();
        }

        public MainForm(
            ISettingsManager settingsManager,
            ILogManager logManager,
            EqpidManager eqpidManager,
            FileWatcherManager fileWatcherManager,
            InfoRetentionCleaner infoCleaner)
        {
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

            _logManager.LogDebug("[MainForm] Initializing Eqpid...");
            _eqpidManager.InitializeEqpid();
            _logManager.LogDebug("[MainForm] Initializing UserControls...");
            InitializeUserControls();
            _logManager.LogDebug("[MainForm] Registering menu events...");
            RegisterMenuEvents();
            _logManager.LogDebug("[MainForm] Initializing TrayIcon...");
            InitializeTrayIcon();
            _logManager.LogEvent("[MainForm] Application components initialized.");
        }

        private void InitializeUserControls()
        {
            _logManager.LogDebug("[MainForm] Creating ucConfigurationPanel...");
            _ucConfigPanel = new ucConfigurationPanel(_settingsManager, _logManager);
            _ucConfigPanel.SettingsChanged += RefreshUIState;
            _ucConfigPanel.ReadyStatusChanged += ConfigPanel_ReadyStatusChanged;

            _logManager.LogDebug("[MainForm] Creating ucPluginPanel...");
            _ucPluginPanel = new ucPluginPanel(_settingsManager, _logManager);

            _logManager.LogDebug("[MainForm] Creating ucOptionPanel...");
            _ucOptionPanel = new ucOptionPanel(_settingsManager, _logManager);

            _logManager.LogDebug("[MainForm] Creating ucOverrideNamesPanel...");
            _ucOverrideNamesPanel = new ucOverrideNamesPanel(_settingsManager, _ucConfigPanel, _logManager);

            _logManager.LogDebug("[MainForm] Creating ucUploadPanel...");
            _ucUploadPanel = new ucUploadPanel(_ucConfigPanel, _ucPluginPanel, _settingsManager, _ucOverrideNamesPanel, _logManager);

            _logManager.LogDebug("[MainForm] Linking OverridePanel to UploadPanel...");
            _ucOverrideNamesPanel.LinkUploadPanel(_ucUploadPanel);

            _logManager.LogDebug("[MainForm] Creating ucImageTransPanel...");
            _ucImageTransPanel = new ucImageTransPanel(_settingsManager, _ucConfigPanel, _logManager);

            // 이벤트 연결
            _logManager.LogDebug("[MainForm] Linking panel events...");
            _ucPluginPanel.PluginsChanged += (sender, args) => _ucUploadPanel.LoadPluginItems();
            _ucOptionPanel.DebugModeChanged += isDebug =>
            {
                LogManager.GlobalDebugEnabled = isDebug;
                // LogManager 자체에서 로그를 남기므로 MainForm에서 중복 로깅하지 않음
            };
            _logManager.LogDebug("[MainForm] UserControls initialization complete.");
        }

        private void RefreshUIState()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(RefreshUIState));
                return;
            }
            
            _logManager.LogDebug($"[MainForm] Refreshing UI state. IsRunning: {_isRunning}");

            // 1. 모든 자식 패널에 실행 상태 전파 (활성화/비활성화)
            _ucConfigPanel.UpdateStatusOnRun(_isRunning);
            _ucOverrideNamesPanel.UpdateStatusOnRun(_isRunning);
            _ucImageTransPanel.UpdateStatusOnRun(_isRunning);
            _ucUploadPanel.UpdateStatusOnRun(_isRunning);
            _ucPluginPanel.UpdateStatusOnRun(_isRunning);
            _ucOptionPanel.UpdateStatusOnRun(_isRunning);

            // 2. MainForm의 컨트롤 상태 결정
            if (_isRunning)
            {
                ts_Status.Text = "Running...";
                ts_Status.ForeColor = Color.Blue;
                btn_Run.Enabled = false;
                btn_Stop.Enabled = true;
                _logManager.LogDebug("[MainForm] UI state set to 'Running'.");
            }
            else
            {
                if (_ucConfigPanel.IsReadyToRun())
                {
                    ts_Status.Text = "Ready to Run";
                    ts_Status.ForeColor = Color.Green;
                    btn_Run.Enabled = true;
                    _logManager.LogDebug("[MainForm] UI state set to 'Ready to Run'.");
                }
                else
                {
                    ts_Status.Text = "Stopped";
                    ts_Status.ForeColor = Color.Red;
                    btn_Run.Enabled = false;
                    _logManager.LogDebug("[MainForm] UI state set to 'Stopped'.");
                }
                btn_Stop.Enabled = false;
            }

            btn_Quit.Enabled = !_isRunning;
            UpdateTrayMenuStatus();
            UpdateFileMenuItemsState(!_isRunning);
            _logManager.LogDebug("[MainForm] UI state refresh complete.");
        }

        #region --- UI Event Handlers (Buttons & Menus) ---

        private void ConfigPanel_ReadyStatusChanged(bool isReady)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<bool>(ConfigPanel_ReadyStatusChanged), isReady);
                return;
            }
            
            _logManager.LogDebug($"[MainForm] ConfigPanel_ReadyStatusChanged received: isReady = {isReady}");

            // 실행 중이 아닐 때만 UI 상태를 갱신
            if (!_isRunning)
            {
                btn_Run.Enabled = isReady;
                ts_Status.Text = isReady ? "Ready to Run" : "Stopped";
                ts_Status.ForeColor = isReady ? Color.Green : Color.Red;
                UpdateTrayMenuStatus();
            }
        }

        private void btn_Run_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[MainForm] Run button clicked.");
            try
            {
                _isRunning = true;
                _logManager.LogDebug("[MainForm] _isRunning state set to true.");
                RefreshUIState();  // UI 상태를 먼저 'Running'으로 갱신

                _logManager.LogDebug("[MainForm] Starting FileWatcherManager...");
                _fileWatcherManager.StartWatching();
                _logManager.LogDebug("[MainForm] Starting PerformanceDbWriter...");
                PerformanceDbWriter.Start(lb_eqpid.Text, _eqpidManager, _logManager);
                _logManager.LogEvent("[MainForm] All services started successfully.");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[MainForm] Error starting monitoring: {ex.Message}");
                MessageBox.Show($"모니터링 시작 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _isRunning = false; // 에러 발생 시 상태 복원
                RefreshUIState();
            }
        }

        private void btn_Stop_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show("프로그램을 중지하시겠습니까?", "작업 중지 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                _logManager.LogEvent("[MainForm] Stop operation canceled by user.");
                return;
            }

            _logManager.LogEvent("[MainForm] Stop button clicked and confirmed.");
            try
            {
                _isRunning = false;
                _logManager.LogDebug("[MainForm] _isRunning state set to false.");
                _logManager.LogDebug("[MainForm] Stopping FileWatcherManager...");
                _fileWatcherManager.StopWatchers();
                _logManager.LogDebug("[MainForm] Stopping PerformanceDbWriter...");
                PerformanceDbWriter.Stop();

                _logManager.LogEvent("[MainForm] All services stopped.");
                RefreshUIState();
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[MainForm] Error stopping processes: {ex.Message}");
                MessageBox.Show($"프로세스 중지 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshUIState();
            }
        }

        private void btn_Quit_Click(object sender, EventArgs e)
        {
            _logManager.LogDebug("[MainForm] Quit button clicked.");
            var result = MessageBox.Show("프로그램을 완전히 종료하시겠습니까?", "종료 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                _logManager.LogEvent("[MainForm] Quit confirmed by user.");
                PerformQuit();
            }
            else
            {
                _logManager.LogEvent("[MainForm] Quit canceled by user.");
            }
        }

        private void NewMenuItem_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[MainForm] 'File -> New' menu item clicked.");
            _settingsManager.ResetExceptEqpid();
            MessageBox.Show("설정이 초기화되었습니다 (Eqpid 제외).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshAllPanelsUI();
            UpdateUIBasedOnSettings();
            _logManager.LogEvent("[MainForm] Settings have been reset.");
        }

        private void OpenMenuItem_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[MainForm] 'File -> Open' menu item clicked.");
            using (var ofd = new OpenFileDialog { Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _logManager.LogDebug($"[MainForm] Opening settings file: {ofd.FileName}");
                    try
                    {
                        _settingsManager.LoadFromFile(ofd.FileName);
                        MessageBox.Show("새로운 설정 파일을 로드했습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        RefreshAllPanelsUI();
                        UpdateUIBasedOnSettings();
                        _logManager.LogEvent($"[MainForm] Settings loaded successfully from {ofd.FileName}.");
                    }
                    catch (Exception ex)
                    {
                        _logManager.LogError($"[MainForm] Error loading settings file {ofd.FileName}: {ex.Message}");
                        MessageBox.Show($"파일 로드 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    _logManager.LogDebug("[MainForm] Open file dialog was canceled.");
                }
            }
        }

        private void SaveAsMenuItem_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[MainForm] 'File -> Save As' menu item clicked.");
            using (var sfd = new SaveFileDialog { Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    _logManager.LogDebug($"[MainForm] Saving settings to file: {sfd.FileName}");
                    try
                    {
                        _settingsManager.SaveToFile(sfd.FileName);
                        MessageBox.Show("설정 파일이 저장되었습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        _logManager.LogEvent($"[MainForm] Settings saved successfully to {sfd.FileName}.");
                    }
                    catch (Exception ex)
                    {
                        _logManager.LogError($"[MainForm] Error saving settings to file {sfd.FileName}: {ex.Message}");
                        MessageBox.Show($"파일 저장 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                     _logManager.LogDebug("[MainForm] Save file dialog was canceled.");
                }
            }
        }

        private void QuitMenuItem_Click(object sender, EventArgs e) => btn_Quit.PerformClick();

        private void tsm_AboutInfo_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[MainForm] 'About -> Information' menu item clicked.");
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
            
            _logManager.LogDebug($"[MainForm] Updating main status. Text: '{status}', Color: {color.Name}");

            ts_Status.Text = status;
            ts_Status.ForeColor = color;

            _ucConfigPanel?.UpdateStatusOnRun(_isRunning);
            _ucOverrideNamesPanel?.UpdateStatusOnRun(_isRunning);
            _ucImageTransPanel?.UpdateStatusOnRun(_isRunning);
            _ucUploadPanel?.UpdateStatusOnRun(_isRunning);
            _ucPluginPanel?.UpdateStatusOnRun(_isRunning);
            _ucOptionPanel?.UpdateStatusOnRun(_isRunning);

            // 버튼 상태 업데이트
            btn_Run.Enabled = !_isRunning && _ucConfigPanel.IsReadyToRun();
            btn_Stop.Enabled = _isRunning;
            btn_Quit.Enabled = !_isRunning;

            UpdateTrayMenuStatus();
            UpdateFileMenuItemsState(!_isRunning);
        }

        private void UpdateUIBasedOnSettings()
        {
            _logManager.LogDebug("[MainForm] Updating UI based on current settings.");
            lb_eqpid.Text = $"Eqpid: {_settingsManager.GetEqpid()}";
            UpdateMenusBasedOnType();

            if (_isRunning) return;

            if (_ucConfigPanel != null && _ucConfigPanel.IsReadyToRun())
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

            _logManager.LogEvent("[MainForm] Quit requested. Starting cleanup...");

            _logManager.LogDebug("[MainForm] Cleaning up UploadPanel...");
            _ucUploadPanel?.CleanUp();
            _logManager.LogDebug("[MainForm] Stopping FileWatcherManager...");
            _fileWatcherManager.StopWatchers();
            _logManager.LogDebug("[MainForm] Stopping PerformanceDbWriter...");
            PerformanceDbWriter.Stop();
            _logManager.LogDebug("[MainForm] Disposing InfoCleaner...");
            _infoCleaner?.Dispose();

            _logManager.LogDebug("[MainForm] Disposing TrayIcon...");
            _trayIcon?.Dispose();
            
            _logManager.LogEvent("[MainForm] Cleanup complete. Exiting application.");
            Application.Exit();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            ShowUserControl(_ucConfigPanel);
            ts_Status.Text = "Initializing...";
            ts_Status.ForeColor = Color.Gray;
            this.Update();
            _logManager.LogEvent("[MainForm] MainForm_Load started.");

            // 1. 시간이 오래 걸리는 작업들을 백그라운드에서 비동기적으로 실행
            _logManager.LogDebug("[MainForm] Warming up performance counters...");
            await Task.Run(() => PerformanceWarmUp.Run());
            _logManager.LogDebug("[MainForm] Performance counters warmed up.");

            // 2. ucPluginPanel에서 비동기적으로 플러그인 '데이터만' 가져옵니다.
            _logManager.LogDebug("[MainForm] Loading plugins asynchronously...");
            var loadedPlugins = await _ucPluginPanel.LoadPluginsAsync();
            _logManager.LogDebug($"[MainForm] {loadedPlugins.Count} plugins loaded from settings.");

            // 3. MainForm이 로드된 데이터를 ucPluginPanel에 전달하여 UI를 업데이트하도록 지시합니다.
            _logManager.LogDebug("[MainForm] Updating plugin panel UI with loaded plugins...");
            _ucPluginPanel.SetLoadedPluginsAndUpdateUI(loadedPlugins);

            // 4. ucUploadPanel의 플러그인 콤보박스가 모두 채워진 것을 보장한 후에,
            _logManager.LogDebug("[MainForm] Loading all settings for UploadPanel...");
            _ucUploadPanel.LoadAllSettings();

            // 5. 모든 초기화가 끝난 후, 최종 UI 상태 갱신
            RefreshUIState();
            _logManager.LogEvent("[MainForm] Application initialized and ready.");
        }

        #endregion

        #region --- UI Helper Methods ---

        private void RegisterMenuEvents()
        {
            tsm_Categorize.Click += (s, e) => ShowUserControl(_ucConfigPanel);
            tsm_Option.Click += (s, e) => ShowUserControl(_ucOptionPanel);
            tsm_OverrideNames.Click += (s, e) => ShowUserControl(_ucOverrideNamesPanel);
            tsm_ImageTrans.Click += (s, e) => ShowUserControl(_ucImageTransPanel);
            tsm_UploadData.Click += (s, e) => ShowUserControl(_ucUploadPanel);
            tsm_PluginList.Click += (s, e) => ShowUserControl(_ucPluginPanel);
            tsm_AboutInfo.Click += tsm_AboutInfo_Click;
        }

        private void ShowUserControl(UserControl control)
        {
            if (control == null)
            {
                _logManager.LogDebug("[MainForm] ShowUserControl called with null control.");
                return;
            }
            
            if (pMain.Controls.Count > 0 && pMain.Controls[0] == control)
            {
                _logManager.LogDebug($"[MainForm] UserControl '{control.Name}' is already visible.");
                return;
            }
            
            _logManager.LogEvent($"[MainForm] Displaying panel: {control.Name}");
            pMain.Controls.Clear();
            control.Dock = DockStyle.Fill;
            pMain.Controls.Add(control);
        }

        private void RefreshAllPanelsUI()
        {
            _logManager.LogEvent("[MainForm] Refreshing all panel UIs from settings.");
            _ucConfigPanel?.RefreshUI();
            _ucOverrideNamesPanel?.RefreshUI();
            _ucImageTransPanel?.RefreshUI();
        }

        private void UpdateMenusBasedOnType()
        {
            string type = _settingsManager.GetApplicationType();
            _logManager.LogDebug($"[MainForm] Updating menus for application type: {type}");
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
            _logManager.LogDebug("[MainForm] Tray icon initialized.");
        }

        private void RestoreFromTray()
        {
            _logManager.LogEvent("[MainForm] Restoring window from tray.");
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
            // '종료' 버튼이 아닌, 창의 X 버튼 등으로 닫으려고 할 때
            if (e.CloseReason == CloseReason.UserClosing && !_isExiting)
            {
                e.Cancel = true; // 종료를 취소
                this.Hide();     // 창을 숨김
                _logManager.LogEvent("[MainForm] Application minimized to tray.");
                _trayIcon.ShowBalloonTip(2000, "ITM Agent", "백그라운드에서 실행 중입니다.", ToolTipIcon.Info);
            }
        }

        #endregion
    }
}
