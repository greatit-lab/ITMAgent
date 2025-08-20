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
    public partial class ucUploadPanel : UserControl
    {
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

            _logManager.LogDebug("[ucUploadPanel] Starting background upload queue processing task.");
            Task.Run(() => ProcessUploadQueueAsync(_cts.Token));
        }

        private void RegisterEventHandlers()
        {
            _pluginPanel.PluginsChanged += OnPluginsChanged;

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

        #region --- Settings Loading & UI Refresh ---

        public void LoadAllSettings()
        {
            _logManager.LogDebug("[ucUploadPanel] Loading all upload settings.");
            LoadTargetFolderItems();

            LoadSetting("WaferFlat", cb_WaferFlat_Path, cb_FlatPlugin);
            LoadSetting("PreAlign", cb_PreAlign_Path, cb_PreAlignPlugin);
            LoadSetting("Error", cb_ErrPath, cb_ErrPlugin);
            LoadSetting("Image", cb_ImgPath, cb_ImagePlugin);
            LoadSetting("Event", cb_EvPath, cb_EvPlugin);
            LoadSetting("Wave", cb_WavePath, cb_WavePlugin);
            _logManager.LogDebug("[ucUploadPanel] Finished loading all upload settings.");
        }

        private void LoadTargetFolderItems()
        {
            _logManager.LogDebug("[ucUploadPanel] Refreshing target folder items in dropdowns.");
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
            _logManager.LogDebug("[ucUploadPanel] Refreshing plugin items in dropdowns.");
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
            _logManager.LogEvent($"[ucUploadPanel] 'Set' button clicked for '{key}'.");
            string folder = comboPath.Text.Trim();
            string plugin = comboPlugin.Text.Trim();

            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(plugin))
            {
                MessageBox.Show("감시 폴더와 실행할 플러그인을 모두 선택해야 합니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!Directory.Exists(folder))
            {
                _logManager.LogError($"[ucUploadPanel] Save failed for '{key}': Folder '{folder}' does not exist.");
                MessageBox.Show("선택한 폴더가 존재하지 않습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string settingValue = $"Folder : {folder}, Plugin : {plugin}";
            _settingsManager.SetValueToSection(UploadSection, key, settingValue);

            StartWatcher(key, folder);
            MessageBox.Show($"{key} 설정이 저장 및 적용되었습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _logManager.LogEvent($"[ucUploadPanel] Setting for '{key}' saved. Watching folder '{folder}' with plugin '{plugin}'.");
        }

        private void ClearSetting(string key, ComboBox comboPath, ComboBox comboPlugin)
        {
            _logManager.LogEvent($"[ucUploadPanel] 'Clear' button clicked for '{key}'.");
            comboPath.SelectedIndex = -1;
            comboPath.Text = "";
            comboPlugin.SelectedIndex = -1;
            comboPlugin.Text = "";

            _settingsManager.RemoveKeyFromSection(UploadSection, key);
            StopWatcher(key);
            MessageBox.Show($"{key} 설정이 초기화되었습니다.", "초기화 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _logManager.LogEvent($"[ucUploadPanel] Setting for '{key}' cleared.");
        }

        private void LoadSetting(string key, ComboBox comboPath, ComboBox comboPlugin)
        {
            _logManager.LogDebug($"[ucUploadPanel] Loading setting for '{key}'.");
            string settingValue = _settingsManager.GetValueFromSection(UploadSection, key);
            if (string.IsNullOrEmpty(settingValue))
            {
                _logManager.LogDebug($"[ucUploadPanel] No setting found for '{key}'.");
                return;
            }

            var folderMatch = Regex.Match(settingValue, @"Folder\s*:\s*(.*?)(,|$)");
            var pluginMatch = Regex.Match(settingValue, @"Plugin\s*:\s*(.*)");

            if (folderMatch.Success && pluginMatch.Success)
            {
                string folder = folderMatch.Groups[1].Value.Trim();
                string plugin = pluginMatch.Groups[1].Value.Trim();

                _logManager.LogDebug($"[ucUploadPanel] Found setting for '{key}': Folder='{folder}', Plugin='{plugin}'.");

                comboPath.SelectedItem = folder;
                comboPlugin.SelectedItem = plugin;
            }
            else
            {
                _logManager.LogError($"[ucUploadPanel] Failed to parse setting for '{key}'. Value: '{settingValue}'");
            }
        }

        private void StartWatcher(string key, string path)
        {
            StopWatcher(key); // 기존 감시자 정리

            _logManager.LogDebug($"[ucUploadPanel] Starting watcher for key '{key}' on path: {path}");
            try
            {
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
            catch (Exception ex)
            {
                _logManager.LogError($"[ucUploadPanel] Failed to start watcher for key '{key}' on path '{path}': {ex.Message}");
            }
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
            if (this.IsDisposed || !this.IsHandleCreated) return;
            if (Directory.Exists(filePath))
            {
                _logManager.LogDebug($"[ucUploadPanel] Directory event ignored: {filePath}");
                return;
            }

            _logManager.LogDebug($"[ucUploadPanel] File event detected for key '{key}': {filePath}");

            string pluginName = "";
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    if (key == "WaferFlat") pluginName = cb_FlatPlugin.Text;
                    else if (key == "PreAlign") pluginName = cb_PreAlignPlugin.Text;
                    else if (key == "Error") pluginName = cb_ErrPlugin.Text;
                    else if (key == "Image") pluginName = cb_ImagePlugin.Text;
                    else if (key == "Event") pluginName = cb_EvPlugin.Text;
                    else if (key == "Wave") pluginName = cb_WavePlugin.Text;
                });
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucUploadPanel] Error getting plugin name from UI thread for key '{key}': {ex.Message}");
                return;
            }

            if (!string.IsNullOrEmpty(pluginName))
            {
                _uploadQueue.Enqueue(new FileProcessItem(filePath, pluginName));
                _logManager.LogDebug($"[ucUploadPanel] File enqueued for processing: '{Path.GetFileName(filePath)}' with plugin '{pluginName}'");
            }
            else
            {
                _logManager.LogDebug($"[ucUploadPanel] No plugin configured for key '{key}', file '{Path.GetFileName(filePath)}' will be ignored.");
            }
        }

        private async Task ProcessUploadQueueAsync(CancellationToken token)
        {
            _logManager.LogDebug("[ucUploadPanel] Background processing queue started.");
            while (!token.IsCancellationRequested)
            {
                if (_uploadQueue.TryDequeue(out FileProcessItem item))
                {
                    _logManager.LogDebug($"[ucUploadPanel] Dequeued file: '{item.FilePath}', Plugin: '{item.PluginName}'");
                    try
                    {
                        string finalPath = _overridePanel.EnsureOverrideAndReturnPath(item.FilePath);
                        _logManager.LogDebug($"[ucUploadPanel] Final path after override check: '{finalPath}'");

                        var pluginInfo = _pluginPanel.GetLoadedPlugins()
                            .FirstOrDefault(p => p.PluginName.Equals(item.PluginName, StringComparison.OrdinalIgnoreCase));

                        if (pluginInfo != null && File.Exists(pluginInfo.AssemblyPath))
                        {
                            _logManager.LogEvent($"[ucUploadPanel] Executing plugin '{pluginInfo.PluginName}' for file: {Path.GetFileName(finalPath)}");
                            ExecutePlugin(pluginInfo.AssemblyPath, finalPath);
                        }
                        else
                        {
                            _logManager.LogError($"[ucUploadPanel] Plugin '{item.PluginName}' not found or its DLL file is missing. Path: {pluginInfo?.AssemblyPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logManager.LogError($"[ucUploadPanel] Error processing file '{item.FilePath}' in queue: {ex.Message}");
                    }
                }
                else
                {
                    await Task.Delay(200, token);
                }
            }
            _logManager.LogDebug("[ucUploadPanel] Background processing queue stopped.");
        }

        private void ExecutePlugin(string assemblyPath, string filePath)
        {
            try
            {
                _logManager.LogDebug($"[ucUploadPanel] Loading assembly from path: {assemblyPath}");
                byte[] dllBytes = File.ReadAllBytes(assemblyPath);
                Assembly asm = Assembly.Load(dllBytes);

                _logManager.LogDebug($"[ucUploadPanel] Searching for a type that implements IPlugin in '{asm.FullName}'.");
                var pluginType = asm.GetTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface);

                if (pluginType != null)
                {
                    _logManager.LogDebug($"[ucUploadPanel] Found plugin type: {pluginType.FullName}. Creating instance.");
                    IPlugin plugin = (IPlugin)Activator.CreateInstance(pluginType);

                    _logManager.LogDebug($"[ucUploadPanel] Initializing plugin '{plugin.Name}'.");
                    plugin.Initialize(_settingsManager, _logManager, ITM_Agent.Core.TimeSyncProvider.Instance);

                    _logManager.LogDebug($"[ucUploadPanel] Executing plugin with file: {filePath}");
                    plugin.Execute(filePath);
                    _logManager.LogDebug($"[ucUploadPanel] Plugin execution completed for: {filePath}");
                }
                else
                {
                    _logManager.LogError($"[ucUploadPanel] No class implementing IPlugin found in {Path.GetFileName(assemblyPath)}.");
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucUploadPanel] Failed to execute plugin '{Path.GetFileName(assemblyPath)}' on file '{filePath}': {ex.GetBaseException().Message}");
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

        public void ProcessFileImmediately(string filePath, string dataTypeKey)
        {
            _logManager.LogEvent($"[ucUploadPanel] Immediate processing requested for '{filePath}' with key '{dataTypeKey}'.");
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                _logManager.LogError($"[ucUploadPanel] ProcessFileImmediately called with invalid path: {filePath}");
                return;
            }

            string pluginName = "";
            try
            {
                this.Invoke((MethodInvoker)delegate
                {
                    switch (dataTypeKey)
                    {
                        case "WaferFlat": pluginName = cb_FlatPlugin.Text; break;
                        case "PreAlign": pluginName = cb_PreAlignPlugin.Text; break;
                        // 필요 시 다른 데이터 타입에 대한 케이스 추가
                    }
                });
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucUploadPanel] Error getting plugin name from UI for immediate processing (key '{dataTypeKey}'): {ex.Message}");
                return;
            }

            if (!string.IsNullOrEmpty(pluginName))
            {
                _uploadQueue.Enqueue(new FileProcessItem(filePath, pluginName));
                _logManager.LogDebug($"[ucUploadPanel] Enqueued immediate processing request for: '{Path.GetFileName(filePath)}' with plugin '{pluginName}'");
            }
            else
            {
                _logManager.LogError($"[ucUploadPanel] Could not find a plugin configured for data type key: {dataTypeKey}. Immediate processing aborted.");
            }
        }

        public void UpdateStatusOnRun(bool isRunning)
        {
            _logManager.LogEvent($"[ucUploadPanel] Run status updated. IsRunning: {isRunning}");
            SetControlsEnabled(!isRunning);

            if (isRunning)
            {
                _logManager.LogDebug("[ucUploadPanel] Run mode: Loading all settings and starting watchers.");
                LoadAllSettings();
            }
            else
            {
                _logManager.LogDebug("[ucUploadPanel] Stop mode: Stopping all watchers.");
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

        public void CleanUp()
        {
            _logManager.LogEvent("[ucUploadPanel] CleanUp called. Stopping background tasks and watchers.");
            _cts.Cancel();
            _cts.Dispose();

            var keys = _watchers.Keys.ToList();
            foreach (var key in keys)
            {
                StopWatcher(key);
            }
        }

        #endregion

        private void OnPluginsChanged(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucUploadPanel] Received PluginsChanged event. Refreshing plugin lists.");
            LoadPluginItems();

            CheckAndClearInvalidPluginSetting("WaferFlat", cb_WaferFlat_Path, cb_FlatPlugin);
            CheckAndClearInvalidPluginSetting("PreAlign", cb_PreAlign_Path, cb_PreAlignPlugin);
            CheckAndClearInvalidPluginSetting("Error", cb_ErrPath, cb_ErrPlugin);
            CheckAndClearInvalidPluginSetting("Image", cb_ImgPath, cb_ImagePlugin);
            CheckAndClearInvalidPluginSetting("Event", cb_EvPath, cb_EvPlugin);
            CheckAndClearInvalidPluginSetting("Wave", cb_WavePath, cb_WavePlugin);
        }

        private void CheckAndClearInvalidPluginSetting(string key, ComboBox comboPath, ComboBox comboPlugin)
        {
            if (!string.IsNullOrEmpty(comboPlugin.Text) && !comboPlugin.Items.Contains(comboPlugin.Text))
            {
                string removedPluginName = comboPlugin.Text;
                _logManager.LogEvent($"[ucUploadPanel] Setting for '{key}' is being cleared because its plugin '{removedPluginName}' was removed or is no longer available.");
                ClearSetting(key, comboPath, comboPlugin);
            }
        }
    }
}
