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
            // 수정된 부분:
            // 생성자에서는 더 이상 동기적으로 플러그인을 로드하지 않습니다.
            // LoadPluginsFromSettings(); // 이 라인을 제거하거나 주석 처리합니다.
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
            SetControlsEnabled(!isRunning);
        }

        #endregion

        #region --- UI Event Handlers ---

        private void btn_PlugAdd_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    AddPlugin(dialog.FileName);
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[ucPluginPanel] Failed to add plugin: {ex.Message}");
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

            // "1. PluginName (v1.0.0)" 형식에서 PluginName 추출
            string selectedText = lb_PluginList.SelectedItem.ToString();
            Match match = Regex.Match(selectedText, @"^\d+\.\s*(?<name>.*?)\s*\(v.*\)$");
            string pluginName = match.Success ? match.Groups["name"].Value.Trim() : selectedText.Split(' ')[1];

            if (MessageBox.Show($"플러그인 '{pluginName}'을(를) 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                RemovePlugin(pluginName);
            }
        }

        #endregion

        #region --- Private Core Logic ---

        private void AddPlugin(string sourceDllPath)
        {
            // 1. Assembly 정보를 메모리에서 먼저 로드하여 유효성 검사
            byte[] dllBytes = File.ReadAllBytes(sourceDllPath);
            Assembly asm = Assembly.Load(dllBytes);
            string pluginName = asm.GetName().Name;

            if (_loadedPlugins.Any(p => p.PluginName.Equals(pluginName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("이미 등록된 플러그인입니다.", "중복", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 2. 'Library' 폴더로 DLL 복사
            string libraryFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library");
            Directory.CreateDirectory(libraryFolder);
            string destDllPath = Path.Combine(libraryFolder, Path.GetFileName(sourceDllPath));

            if (File.Exists(destDllPath))
            {
                MessageBox.Show("동일한 이름의 DLL 파일이 'Library' 폴더에 이미 존재합니다.", "파일 중복", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            File.Copy(sourceDllPath, destDllPath);

            // 3. 참조된 어셈블리(필요 시)도 함께 복사 (원본 코드 로직 유지)
            CopyReferencedAssemblies(asm, Path.GetDirectoryName(sourceDllPath), libraryFolder);

            // 4. 플러그인 정보 생성 및 목록에 추가
            var newItem = new PluginListItem
            {
                PluginName = pluginName,
                AssemblyPath = destDllPath,
                PluginVersion = asm.GetName().Version.ToString()
            };
            _loadedPlugins.Add(newItem);

            // 5. 설정 저장 및 UI 갱신
            SavePluginSetting(newItem);
            UpdatePluginListDisplay();
            PluginsChanged?.Invoke(this, EventArgs.Empty); // 다른 패널에 변경 알림
            _logManager.LogEvent($"[ucPluginPanel] Plugin added: {newItem}");
        }

        private void RemovePlugin(string pluginName)
        {
            var pluginToRemove = _loadedPlugins.FirstOrDefault(p => p.PluginName.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
            if (pluginToRemove == null) return;

            try
            {
                // DLL 파일 삭제
                if (File.Exists(pluginToRemove.AssemblyPath))
                {
                    File.Delete(pluginToRemove.AssemblyPath);
                }

                // 목록 및 설정에서 제거
                _loadedPlugins.Remove(pluginToRemove);
                _settingsManager.RemoveKeyFromSection("RegPlugins", pluginToRemove.PluginName);

                // UI 갱신 및 변경 알림
                UpdatePluginListDisplay();
                PluginsChanged?.Invoke(this, EventArgs.Empty);
                _logManager.LogEvent($"[ucPluginPanel] Plugin removed: {pluginName}");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[ucPluginPanel] Failed to remove plugin {pluginName}: {ex.Message}");
                MessageBox.Show($"플러그인 삭제 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopyReferencedAssemblies(Assembly asm, string sourceDir, string destDir)
        {
            // System.Text.Encoding.CodePages.dll 과 같은 필수 참조 DLL을 복사하는 로직
            string[] requiredDlls = { "System.Text.Encoding.CodePages.dll" };

            foreach (var asmName in asm.GetReferencedAssemblies())
            {
                if (requiredDlls.Any(r => r.StartsWith(asmName.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    string sourceFile = Path.Combine(sourceDir, asmName.Name + ".dll");
                    string destFile = Path.Combine(destDir, asmName.Name + ".dll");
                    if (File.Exists(sourceFile) && !File.Exists(destFile))
                    {
                        File.Copy(sourceFile, destFile);
                    }
                }
            }
        }

        #endregion

        #region --- Settings & UI Helpers ---

        private void LoadPluginsFromSettings()
        {
            _loadedPlugins.Clear();
            var pluginEntries = _settingsManager.GetRegexList(); // [RegPlugins] 섹션을 읽어옴

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

                        _loadedPlugins.Add(new PluginListItem
                        {
                            PluginName = asm.GetName().Name,
                            AssemblyPath = fullPath,
                            PluginVersion = asm.GetName().Version.ToString()
                        });
                    }
                    catch (Exception ex)
                    {
                        _logManager.LogError($"[ucPluginPanel] Failed to load plugin from settings ({pluginName}): {ex.Message}");
                    }
                }
            }
            UpdatePluginListDisplay();
        }

        private void SavePluginSetting(PluginListItem item)
        {
            string relativePath = Path.Combine("Library", Path.GetFileName(item.AssemblyPath));
            _settingsManager.SetValueToSection("RegPlugins", item.PluginName, relativePath);
        }

        private void UpdatePluginListDisplay()
        {
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

        /// <summary>
        /// 설정 파일에서 플러그인 목록을 비동기적으로 로드하여 UI에 반영합니다.
        /// </summary>
        public async Task<List<PluginListItem>> LoadPluginsAsync()
        {
            return await Task.Run(() =>
            {
                var plugins = new List<PluginListItem>();

                // *** 버그 수정: [RegPlugins] 섹션을 올바르게 읽도록 수정 ***
                // 잘못된 GetRegexList() 호출 대신, 새로 추가한 GetSectionAsDictionary 메서드를 사용하여
                // [RegPlugins] 섹션의 모든 "Key = Value" 라인을 정확하게 읽어옵니다.
                var pluginEntries = _settingsManager.GetSectionAsDictionary("[RegPlugins]");

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

                            // 실제 어셈블리 이름과 설정 파일의 키(플러그인 이름)가 일치하는지 확인
                            if (asm.GetName().Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase))
                            {
                                plugins.Add(new PluginListItem
                                {
                                    PluginName = pluginName, // 설정 파일의 키를 이름으로 사용
                                    AssemblyPath = fullPath,
                                    PluginVersion = asm.GetName().Version.ToString()
                                });
                            }
                            else
                            {
                                _logManager.LogError($"[ucPluginPanel] Plugin name mismatch. Key='{pluginName}', AssemblyName='{asm.GetName().Name}'. Skipping.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logManager.LogError($"[ucPluginPanel] Failed to load plugin from settings ({pluginName}): {ex.Message}");
                        }
                    }
                }
                return plugins;
            });
        }

        public void SetLoadedPluginsAndUpdateUI(List<PluginListItem> plugins)
        {
            // 1. MainForm으로부터 받은 플러그인 데이터로 내부 리스트를 갱신합니다.
            _loadedPlugins.Clear();
            _loadedPlugins.AddRange(plugins);

            // 2. 갱신된 내부 리스트를 기준으로 UI 리스트박스를 업데이트합니다.
            UpdatePluginListDisplay();

            // *** 버그 수정: 누락되었던 기능 복원 ***
            // 3. UI 업데이트가 완료된 후, PluginsChanged 이벤트를 발생시켜
            //    ucUploadPanel과 같은 다른 구독자(리스너)에게 플러그인 목록이
            //    변경되었음을 명확하게 알립니다.
            //    이 신호가 바로 ucUploadPanel이 자신의 콤보박스를 채우는 계기가 됩니다.
            PluginsChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
