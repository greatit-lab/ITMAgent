// ITM_Agent/Panels/ucUploadPanel.cs
using ITM_Agent.Common.DTOs;
using ITM_Agent.Common.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITM_Agent.Panels
{
    public partial class ucUploadPanel : UserControl // IDisposable은 UserControl에 이미 구현되어 있음
    {
        // ... (이전과 동일한 모든 필드, 생성자, 이벤트 핸들러, 메서드) ...
        #region --- Services and Fields ---

        private readonly ucConfigurationPanel _configPanel;
        private readonly ucPluginPanel _pluginPanel;
        private readonly ISettingsManager _settingsManager;
        private readonly ucOverrideNamesPanel _overridePanel;
        private readonly ILogManager _logManager;

        private readonly ConcurrentQueue<FileProcessItem> _uploadQueue = new ConcurrentQueue<FileProcessItem>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>();

        private const string UploadSection = "UploadSetting";

        #endregion

        #region --- Initialization ---

        public ucUploadPanel(
            ucConfigurationPanel configPanel,
            ucPluginPanel pluginPanel,
            ISettingsManager settingsManager,
            ucOverrideNamesPanel overridePanel,
            ILogManager logManager)
        {
            _configPanel = configPanel ?? throw new ArgumentNullException(nameof(configPanel));
            _pluginPanel = pluginPanel ?? throw new ArgumentNullException(nameof(pluginPanel));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _overridePanel = overridePanel ?? throw new ArgumentNullException(nameof(overridePanel));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

            InitializeComponent();
            RegisterEventHandlers();
            LoadAllSettings();

            Task.Run(() => ProcessUploadQueueAsync(_cts.Token));
        }

        private void RegisterEventHandlers()
        {
            _pluginPanel.PluginsChanged += (s, e) => LoadPluginItems();

            btn_FlatSet.Click += (s, e) => SaveAndApplySetting("WaferFlat", cb_WaferFlat_Path, cb_FlatPlugin);
            btn_FlatClear.Click += (s, e) => ClearSetting("WaferFlat", cb_WaferFlat_Path, cb_FlatPlugin);

            btn_PreAlignSet.Click += (s, e) => SaveAndApplySetting("PreAlign", cb_PreAlign_Path, cb_PreAlignPlugin);
            btn_PreAlignClear.Click += (s, e) => ClearSetting("PreAlign", cb_PreAlign_Path, cb_PreAlignPlugin);

            btn_ErrSet.Click += (s, e) => SaveAndApplySetting("Error", cb_ErrPath, cb_ErrPlugin);
            btn_ErrClear.Click += (s, e) => ClearSetting("Error", cb_ErrPath, cb_ErrPlugin);

            btn_ImgSet.Click += (s, e) => SaveAndApplySetting("Image", cb_ImgPath, cb_ImagePlugin);
            btn_ImgClear.Click += (s, e) => ClearSetting("Image", cb_ImgPath, cb_ImagePlugin);

            btn_EvSet.Click += (s, e) => SaveAndApplySetting("Event", cb_EvPath, cb_EvPlugin);
            btn_EvClear.Click += (s, e) => ClearSetting("Event", cb_EvPath, cb_EvPlugin);

            btn_WaveSet.Click += (s, e) => SaveAndApplySetting("Wave", cb_WavePath, cb_WavePlugin);
            btn_WaveClear.Click += (s, e) => ClearSetting("Wave", cb_WavePath, cb_WavePlugin);
        }

        #endregion

        // ... (LoadAllSettings, LoadTargetFolderItems, LoadPluginItems 등 이전과 동일한 메서드들) ...
        #region --- Settings Loading & UI Refresh ---

        public void LoadAllSettings()
        {
            LoadTargetFolderItems();

            LoadSetting("WaferFlat", cb_WaferFlat_Path, cb_FlatPlugin);
            LoadSetting("PreAlign", cb_PreAlign_Path, cb_PreAlignPlugin);
            LoadSetting("Error", cb_ErrPath, cb_ErrPlugin);
            LoadSetting("Image", cb_ImgPath, cb_ImagePlugin);
            LoadSetting("Event", cb_EvPath, cb_EvPlugin);
            LoadSetting("Wave", cb_WavePath, cb_WavePlugin);
        }

        private void LoadTargetFolderItems()
        {
            var targetFolders = _configPanel.GetRegexTargetFolders();
            var allCombos = new[] { cb_WaferFlat_Path, cb_PreAlign_Path, cb_ErrPath, cb_ImgPath, cb_EvPath, cb_WavePath };

            foreach (var combo in allCombos)
            {
                string currentSelection = combo.Text;
                combo.Items.Clear();
                combo.Items.AddRange(targetFolders.ToArray());
                if (combo.Items.Contains(currentSelection))
                {
                    combo.SelectedItem = currentSelection;
                }
            }
        }

        public void LoadPluginItems()
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)LoadPluginItems);
                return;
            }

            var pluginNames = _pluginPanel.GetLoadedPlugins().Select(p => p.PluginName).ToArray();
            var allCombos = new[] { cb_FlatPlugin, cb_PreAlignPlugin, cb_ErrPlugin, cb_ImagePlugin, cb_EvPlugin, cb_WavePlugin };

            foreach (var combo in allCombos)
            {
                string currentSelection = combo.Text;
                combo.Items.Clear();
                combo.Items.AddRange(pluginNames);
                if (combo.Items.Contains(currentSelection))
                {
                    combo.SelectedItem = currentSelection;
                }
            }
        }

        #endregion

        #region --- Core Logic (Settings Save/Clear, Watcher) ---

        private void SaveAndApplySetting(string key, ComboBox comboPath, ComboBox comboPlugin)
        {
            string folder = comboPath.Text.Trim();
            string plugin = comboPlugin.Text.Trim();

            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(plugin))
            {
                MessageBox.Show("감시 폴더와 실행할 플러그인을 모두 선택해야 합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Directory.Exists(folder))
            {
                MessageBox.Show("선택한 폴더가 존재하지 않습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string settingValue = $"Folder : {folder}, Plugin : {plugin}";
            _settingsManager.SetValueToSection(UploadSection, key, settingValue);

            StartWatcher(key, folder);
            MessageBox.Show($"{key} 설정이 저장 및 적용되었습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearSetting(string key, ComboBox comboPath, ComboBox comboPlugin)
        {
            comboPath.SelectedIndex = -1;
            comboPath.Text = "";
            comboPlugin.SelectedIndex = -1;
            comboPlugin.Text = "";

            _settingsManager.RemoveKeyFromSection(UploadSection, key);
            StopWatcher(key);
            MessageBox.Show($"{key} 설정이 초기화되었습니다.", "초기화 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LoadSetting(string key, ComboBox comboPath, ComboBox comboPlugin)
        {
            string settingValue = _settingsManager.GetValueFromSection(UploadSection, key);
            if (string.IsNullOrEmpty(settingValue)) return;

            var folderMatch = Regex.Match(settingValue, @"Folder\s*:\s*(.*?)(,|$)");
            var pluginMatch = Regex.Match(settingValue, @"Plugin\s*:\s*(.*)");

            if (folderMatch.Success && pluginMatch.Success)
            {
                string folder = folderMatch.Groups[1].Value.Trim();
                string plugin = pluginMatch.Groups[1].Value.Trim();

                comboPath.SelectedItem = folder;
                comboPlugin.SelectedItem = plugin;

                if (Directory.Exists(folder))
                {
                    StartWatcher(key, folder);
                }
            }
        }

        private void StartWatcher(string key, string path)
        {
            StopWatcher(key);

            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            watcher.Created += (s, e) => OnFileDetected(key, e.FullPath);
            watcher.Changed += (s, e) => OnFileDetected(key, e.FullPath);

            _watchers[key] = watcher;
            _logManager.LogEvent($"[ucUploadPanel] Watcher started for '{key}' on path: {path}");
        }

        private void StopWatcher(string key)
        {
            if (_watchers.TryGetValue(key, out FileSystemWatcher watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(key);
                _logManager.LogEvent($"[ucUploadPanel] Watcher stopped for '{key}'.");
            }
        }

        #endregion

        #region --- Background File Processing Queue ---

        private void OnFileDetected(string key, string filePath)
        {
            if (Directory.Exists(filePath)) return;

            string pluginName = "";
            this.Invoke((MethodInvoker)delegate
            {
                if (key == "WaferFlat") pluginName = cb_FlatPlugin.Text;
                else if (key == "PreAlign") pluginName = cb_PreAlignPlugin.Text;
                else if (key == "Error") pluginName = cb_ErrPlugin.Text;
                else if (key == "Image") pluginName = cb_ImagePlugin.Text;
                else if (key == "Event") pluginName = cb_EvPlugin.Text;
                else if (key == "Wave") pluginName = cb_WavePlugin.Text;
            });

            if (!string.IsNullOrEmpty(pluginName))
            {
                _uploadQueue.Enqueue(new FileProcessItem(filePath, pluginName));
                _logManager.LogDebug($"[ucUploadPanel] File enqueued for processing: {Path.GetFileName(filePath)} with plugin {pluginName}");
            }
        }

        private async Task ProcessUploadQueueAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_uploadQueue.TryDequeue(out FileProcessItem item))
                {
                    try
                    {
                        string finalPath = _overridePanel.EnsureOverrideAndReturnPath(item.FilePath);

                        var pluginInfo = _pluginPanel.GetLoadedPlugins()
                            .FirstOrDefault(p => p.PluginName.Equals(item.PluginName, StringComparison.OrdinalIgnoreCase));

                        if (pluginInfo != null && File.Exists(pluginInfo.AssemblyPath))
                        {
                            _logManager.LogEvent($"[ucUploadPanel] Executing plugin '{pluginInfo.PluginName}' for file: {Path.GetFileName(finalPath)}");
                            ExecutePlugin(pluginInfo.AssemblyPath, finalPath);
                        }
                        else
                        {
                            _logManager.LogError($"[ucUploadPanel] Plugin '{item.PluginName}' not found or DLL is missing.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logManager.LogError($"[ucUploadPanel] Error processing file {item.FilePath}: {ex.Message}");
                    }
                }
                else
                {
                    await Task.Delay(200, token);
                }
            }
        }

        private void ExecutePlugin(string assemblyPath, string filePath)
        {
            try
            {
                byte[] dllBytes = File.ReadAllBytes(assemblyPath);
                Assembly asm = Assembly.Load(dllBytes);
                var pluginType = asm.GetTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface);

                if (pluginType != null)
                {
                    IPlugin plugin = (IPlugin)Activator.CreateInstance(pluginType);

                    // 수정된 부분: TimeSyncProvider.Instance를 세 번째 인자로 전달
                    plugin.Initialize(_settingsManager, _logManager, ITM_Agent.Core.TimeSyncProvider.Instance);

                    plugin.Execute(filePath);
                }
                else
                {
                    _logManager.LogError($"[ucUploadPanel] No class implementing IPlugin found in {Path.GetFileName(assemblyPath)}.");
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucUploadPanel] Failed to execute plugin {Path.GetFileName(assemblyPath)}: {ex.GetBaseException().Message}");
            }
        }

        private class FileProcessItem
        {
            public string FilePath { get; }
            public string PluginName { get; }
            public FileProcessItem(string path, string plugin) { FilePath = path; PluginName = plugin; }
        }

        #endregion

        #region --- Public Control & CleanUp ---

        public void UpdateStatusOnRun(bool isRunning)
        {
            SetControlsEnabled(!isRunning);

            if (isRunning)
            {
                LoadAllSettings();
            }
            else
            {
                var keys = _watchers.Keys.ToList();
                foreach (var key in keys)
                {
                    StopWatcher(key);
                }
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate { SetControlsEnabled(enabled); });
                return;
            }

            foreach (Control control in this.groupBox1.Controls)
            {
                control.Enabled = enabled;
            }
        }

        /// <summary>
        /// MainForm이 종료될 때 호출되어 모든 관리되지 않는 리소스와 백그라운드 작업을 정리합니다.
        /// </summary>
        public void CleanUp()
        {
            _cts.Cancel(); // 백그라운드 Task 중지
            _cts.Dispose();

            var keys = _watchers.Keys.ToList();
            foreach (var key in keys)
            {
                StopWatcher(key);
            }
        }

        #endregion
    }
}
