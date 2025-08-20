// ITM_Agent/Panels/ucPluginPanel.cs
using ITM_Agent.Common.DTOs;
using ITM_Agent.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ITM_Agent.Panels
{
    /// <summary>
    /// 외부 플러그인 DLL을 등록하고 관리하는 UI 패널입니다.
    /// </summary>
    public partial class ucPluginPanel : UserControl
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ILogManager _logManager;
        private readonly List<PluginListItem> _loadedPlugins = new List<PluginListItem>();

        /// <summary>
        /// 플러그인 목록에 변경이 생겼을 때 발생하는 이벤트입니다.
        /// </summary>
        public event EventHandler PluginsChanged;

        public ucPluginPanel(ISettingsManager settingsManager, ILogManager logManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

            InitializeComponent();
        }

        #region --- Public Methods ---

        /// <summary>
        /// 현재 로드된 플러그인 정보 목록을 반환합니다.
        /// </summary>
        public List<PluginListItem> GetLoadedPlugins() => _loadedPlugins;

        /// <summary>
        /// MainForm의 Run/Stop 상태에 따라 UI 컨트롤의 활성화 상태를 업데이트합니다.
        /// </summary>
        public void UpdateStatusOnRun(bool isRunning)
        {
            _logManager.LogDebug($"[ucPluginPanel] Updating control enabled status based on run state. IsRunning: {isRunning}");
            SetControlsEnabled(!isRunning);
        }

        #endregion

        #region --- UI Event Handlers ---

        private void btn_PlugAdd_Click(object sender, EventArgs e)
        {
            _logManager.LogEvent("[ucPluginPanel] Add plugin button clicked.");
            using (var dialog = new OpenFileDialog
            {
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    _logManager.LogDebug("[ucPluginPanel] Add plugin dialog was canceled.");
                    return;
                }

                try
                {
                    _logManager.LogDebug($"[ucPluginPanel] User selected plugin file: {dialog.FileName}");
                    AddPlugin(dialog.FileName);
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[ucPluginPanel] Failed to add plugin '{dialog.FileName}': {ex.Message}");
                    MessageBox.Show($"플러그인 로드 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btn_PlugRemove_Click(object sender, EventArgs e)
        {
            if (lb_PluginList.SelectedItem == null)
            {
                MessageBox.Show("삭제할 플러그인을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            
            _logManager.LogEvent("[ucPluginPanel] Remove plugin button clicked.");

            string selectedText = lb_PluginList.SelectedItem.ToString();
            // "1. PluginName (v1.0.0)" 형식에서 PluginName 추출
            Match match = Regex.Match(selectedText, @"^\d+\.\s*(?<name>.*?)\s*\(v.*\)$");
            string pluginName = match.Success ? match.Groups["name"].Value.Trim() : selectedText.Split(' ')[1];
            
            _logManager.LogDebug($"[ucPluginPanel] Selected plugin to remove: {pluginName}");

            if (MessageBox.Show($"플러그인 '{pluginName}'을(를) 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _logManager.LogDebug($"[ucPluginPanel] User confirmed removal of plugin '{pluginName}'.");
                RemovePlugin(pluginName);
            }
            else
            {
                _logManager.LogDebug("[ucPluginPanel] Plugin removal was canceled by user.");
            }
        }

        #endregion

        #region --- Private Core Logic ---

        private void AddPlugin(string sourceDllPath)
        {
            _logManager.LogDebug($"[ucPluginPanel] Starting AddPlugin logic for: {sourceDllPath}");

            // 1. Assembly 정보를 메모리에서 먼저 로드하여 유효성 검사
            _logManager.LogDebug("[ucPluginPanel] Reading DLL file into memory for validation.");
            byte[] dllBytes = File.ReadAllBytes(sourceDllPath);
            Assembly asm = Assembly.Load(dllBytes);
            string pluginName = asm.GetName().Name;
            _logManager.LogDebug($"[ucPluginPanel] Assembly loaded successfully. Plugin name: {pluginName}");

            if (_loadedPlugins.Any(p => p.PluginName.Equals(pluginName, StringComparison.OrdinalIgnoreCase)))
            {
                _logManager.LogDebug($"[ucPluginPanel] Plugin '{pluginName}' is already registered.");
                MessageBox.Show("이미 등록된 플러그인입니다.", "중복", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 2. 'Library' 폴더로 DLL 복사
            string libraryFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library");
            Directory.CreateDirectory(libraryFolder);
            string destDllPath = Path.Combine(libraryFolder, Path.GetFileName(sourceDllPath));
            _logManager.LogDebug($"[ucPluginPanel] Destination path set to: {destDllPath}");

            if (File.Exists(destDllPath))
            {
                 _logManager.LogDebug($"[ucPluginPanel] A DLL with the same name already exists in the Library folder.");
                MessageBox.Show("동일한 이름의 DLL 파일이 'Library' 폴더에 이미 존재합니다.", "파일 중복", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            File.Copy(sourceDllPath, destDllPath);
            _logManager.LogDebug($"[ucPluginPanel] Copied plugin DLL to Library folder.");

            // 3. 참조된 어셈블리(필요 시)도 함께 복사
            CopyReferencedAssemblies(asm, Path.GetDirectoryName(sourceDllPath), libraryFolder);

            // 4. 플러그인 정보 생성 및 목록에 추가
            var newItem = new PluginListItem
            {
                PluginName = pluginName,
                AssemblyPath = destDllPath,
                PluginVersion = asm.GetName().Version.ToString()
            };
            _loadedPlugins.Add(newItem);
            _logManager.LogDebug($"[ucPluginPanel] New plugin item created and added to the internal list.");

            // 5. 설정 저장 및 UI 갱신
            SavePluginSetting(newItem);
            UpdatePluginListDisplay();
            PluginsChanged?.Invoke(this, EventArgs.Empty); // 다른 패널에 변경 알림
            _logManager.LogEvent($"[ucPluginPanel] Plugin added successfully: {newItem}");
        }

        private void RemovePlugin(string pluginName)
        {
            var pluginToRemove = _loadedPlugins.FirstOrDefault(p => p.PluginName.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
            if (pluginToRemove == null)
            {
                _logManager.LogError($"[ucPluginPanel] Could not find plugin '{pluginName}' in the loaded list for removal.");
                return;
            }

            _logManager.LogDebug($"[ucPluginPanel] Starting RemovePlugin logic for: {pluginName}");
            try
            {
                // DLL 파일 삭제
                if (File.Exists(pluginToRemove.AssemblyPath))
                {
                    File.Delete(pluginToRemove.AssemblyPath);
                    _logManager.LogDebug($"[ucPluginPanel] Deleted plugin DLL: {pluginToRemove.AssemblyPath}");
                }

                // 목록 및 설정에서 제거
                _loadedPlugins.Remove(pluginToRemove);
                _settingsManager.RemoveKeyFromSection("RegPlugins", pluginToRemove.PluginName);
                 _logManager.LogDebug($"[ucPluginPanel] Removed plugin from internal list and settings.ini.");

                // UI 갱신 및 변경 알림
                UpdatePluginListDisplay();
                PluginsChanged?.Invoke(this, EventArgs.Empty);
                _logManager.LogEvent($"[ucPluginPanel] Plugin removed successfully: {pluginName}");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucPluginPanel] Failed to remove plugin {pluginName}: {ex.Message}");
                MessageBox.Show($"플러그인 삭제 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyReferencedAssemblies(Assembly asm, string sourceDir, string destDir)
        {
            _logManager.LogDebug($"[ucPluginPanel] Checking for referenced assemblies to copy for plugin '{asm.GetName().Name}'.");
            string[] requiredDlls = { "System.Text.Encoding.CodePages.dll" };

            foreach (var asmName in asm.GetReferencedAssemblies())
            {
                if (requiredDlls.Any(r => r.StartsWith(asmName.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    string sourceFile = Path.Combine(sourceDir, asmName.Name + ".dll");
                    string destFile = Path.Combine(destDir, asmName.Name + ".dll");
                     _logManager.LogDebug($"[ucPluginPanel] Found a required referenced assembly: {asmName.Name}");
                    if (File.Exists(sourceFile) && !File.Exists(destFile))
                    {
                        File.Copy(sourceFile, destFile);
                        _logManager.LogDebug($"[ucPluginPanel] Copied referenced assembly from '{sourceFile}' to '{destFile}'.");
                    }
                }
            }
        }

        #endregion

        #region --- Settings & UI Helpers ---

        private void SavePluginSetting(PluginListItem item)
        {
            string relativePath = Path.Combine("Library", Path.GetFileName(item.AssemblyPath));
            _settingsManager.SetValueToSection("RegPlugins", item.PluginName, relativePath);
            _logManager.LogDebug($"[ucPluginPanel] Saved plugin setting: [{UploadSection}] {item.PluginName} = {relativePath}");
        }

        private void UpdatePluginListDisplay()
        {
            _logManager.LogDebug("[ucPluginPanel] Updating plugin list display in UI.");
            lb_PluginList.Items.Clear();
            for (int i = 0; i < _loadedPlugins.Count; i++)
            {
                lb_PluginList.Items.Add($"{i + 1}. {_loadedPlugins[i]}");
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)delegate { SetControlsEnabled(enabled); });
                return;
            }
            btn_PlugAdd.Enabled = enabled;
            btn_PlugRemove.Enabled = enabled;
            lb_PluginList.Enabled = enabled;
        }

        public async Task<List<PluginListItem>> LoadPluginsAsync()
        {
            _logManager.LogDebug("[ucPluginPanel] Starting to load plugins asynchronously from settings.");
            return await Task.Run(() =>
            {
                var plugins = new List<PluginListItem>();
                var pluginEntries = _settingsManager.GetSectionAsDictionary("[RegPlugins]");
                _logManager.LogDebug($"[ucPluginPanel] Found {pluginEntries.Count} plugin entries in settings.");

                foreach (var entry in pluginEntries)
                {
                    string pluginName = entry.Key;
                    string relativePath = entry.Value;
                    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            byte[] dllBytes = File.ReadAllBytes(fullPath);
                            Assembly asm = Assembly.Load(dllBytes);

                            if (asm.GetName().Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase))
                            {
                                plugins.Add(new PluginListItem
                                {
                                    PluginName = pluginName,
                                    AssemblyPath = fullPath,
                                    PluginVersion = asm.GetName().Version.ToString()
                                });
                                 _logManager.LogDebug($"[ucPluginPanel] Successfully loaded plugin from settings: {pluginName}");
                            }
                            else
                            {
                                _logManager.LogError($"[ucPluginPanel] Plugin name mismatch. Key='{pluginName}', AssemblyName='{asm.GetName().Name}'. Skipping.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logManager.LogError($"[ucPluginPanel] Failed to load plugin assembly from '{fullPath}' (Key: {pluginName}): {ex.Message}");
                        }
                    }
                    else
                    {
                        _logManager.LogError($"[ucPluginPanel] Plugin DLL not found at path: {fullPath} (Key: {pluginName})");
                    }
                }
                return plugins;
            });
        }

        public void SetLoadedPluginsAndUpdateUI(List<PluginListItem> plugins)
        {
            _logManager.LogDebug("[ucPluginPanel] Setting loaded plugins from MainForm and updating UI.");
            _loadedPlugins.Clear();
            _loadedPlugins.AddRange(plugins);
            
            UpdatePluginListDisplay();

            _logManager.LogDebug("[ucPluginPanel] Invoking PluginsChanged event to notify other panels.");
            PluginsChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
