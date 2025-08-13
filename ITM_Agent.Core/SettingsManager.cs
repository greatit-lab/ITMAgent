// ITM_Agent.Core/SettingsManager.cs
using ITM_Agent.Common.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ITM_Agent.Core
{
    /// <summary>
    /// ISettingsManager 인터페이스를 구현하는 설정 관리 서비스 클래스입니다.
    /// Settings.ini 파일의 모든 섹션과 키-값 데이터를 읽고, 쓰고, 수정하는 기능을 제공합니다.
    /// </summary>
    public class SettingsManager : ISettingsManager
    {
        private readonly string _settingsFilePath;
        private readonly object _fileLock = new object();
        private readonly ILogManager _logManager;

        #region --- Properties ---

        public bool IsDebugMode { get; set; }

        public bool IsPerformanceLogging
        {
            get => GetValueFromSection("Option", "EnablePerfoLog") == "1";
            set => SetValueToSection("Option", "EnablePerfoLog", value ? "1" : "0");
        }

        public bool IsInfoDeletionEnabled
        {
            get => GetValueFromSection("Option", "EnableInfoAutoDel") == "1";
            set => SetValueToSection("Option", "EnableInfoAutoDel", value ? "1" : "0");
        }

        public int InfoRetentionDays
        {
            get
            {
                if (int.TryParse(GetValueFromSection("Option", "InfoRetentionDays"), out int days))
                {
                    return days;
                }
                return 1; // 기본값
            }
            set => SetValueToSection("Option", "InfoRetentionDays", value.ToString());
        }

        #endregion

        /// <summary>
        /// SettingsManager의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="settingsFilePath">관리할 Settings.ini 파일의 전체 경로입니다.</param>
        public SettingsManager(string settingsFilePath)
        {
            _settingsFilePath = settingsFilePath;
            _logManager = new LogManager(Path.GetDirectoryName(settingsFilePath)); // LogManager 자체 생성
            EnsureSettingsFileExists();
            // 초기 디버그 모드 상태 로드
            IsDebugMode = GetValueFromSection("Option", "DebugMode") == "1";
        }

        private void EnsureSettingsFileExists()
        {
            if (!File.Exists(_settingsFilePath))
            {
                try
                {
                    File.Create(_settingsFilePath).Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not create settings file at {_settingsFilePath}: {ex.Message}");
                }
            }
        }

        #region --- General Methods ---

        public string GetEqpid() => GetValueFromSection("Eqpid", "Eqpid");
        public void SetEqpid(string eqpid) => SetValueToSection("Eqpid", "Eqpid", eqpid);
        public string GetApplicationType() => GetValueFromSection("Eqpid", "Type");
        public void SetApplicationType(string type) => SetValueToSection("Eqpid", "Type", type);

        public string GetValueFromSection(string section, string key)
        {
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return null;

                bool inSection = false;
                foreach (string line in File.ReadLines(_settingsFilePath))
                {
                    string trimmedLine = line.Trim();
                    if (trimmedLine.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        continue;
                    }
                    if (inSection)
                    {
                        if (trimmedLine.StartsWith("[")) break; // 다른 섹션 시작

                        string[] parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            return parts[1].Trim();
                        }
                    }
                }
            }
            return null;
        }

        public void SetValueToSection(string section, string key, string value)
        {
            lock (_fileLock)
            {
                var lines = File.Exists(_settingsFilePath) ? File.ReadAllLines(_settingsFilePath).ToList() : new List<string>();
                int sectionIndex = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));

                if (sectionIndex == -1)
                {
                    // 섹션이 없으면 새로 추가
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines.Last())) lines.Add("");
                    lines.Add($"[{section}]");
                    lines.Add($"{key} = {value}");
                }
                else
                {
                    // 섹션이 있으면 키를 찾아 업데이트하거나 새로 추가
                    int keyIndex = -1;
                    int searchEndIndex = lines.Count;
                    for (int i = sectionIndex + 1; i < lines.Count; i++)
                    {
                        if (lines[i].Trim().StartsWith("["))
                        {
                            searchEndIndex = i;
                            break;
                        }
                        if (lines[i].Trim().StartsWith($"{key} =", StringComparison.OrdinalIgnoreCase))
                        {
                            keyIndex = i;
                            break;
                        }
                    }

                    if (keyIndex != -1)
                    {
                        lines[keyIndex] = $"{key} = {value}";
                    }
                    else
                    {
                        lines.Insert(searchEndIndex, $"{key} = {value}");
                    }
                }
                File.WriteAllLines(_settingsFilePath, lines);
            }
        }

        public void RemoveKeyFromSection(string section, string key)
        {
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return;
                var lines = File.ReadAllLines(_settingsFilePath).ToList();

                int sectionIndex = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
                if (sectionIndex == -1) return;

                for (int i = sectionIndex + 1; i < lines.Count; i++)
                {
                    if (lines[i].Trim().StartsWith("[")) break;

                    if (lines[i].Trim().StartsWith($"{key} =", StringComparison.OrdinalIgnoreCase))
                    {
                        lines.RemoveAt(i);
                        break;
                    }
                }
                File.WriteAllLines(_settingsFilePath, lines);
            }
        }

        public void RemoveSection(string section)
        {
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return;
                var lines = File.ReadAllLines(_settingsFilePath).ToList();

                int sectionIndex = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
                if (sectionIndex == -1) return;

                int endIndex = lines.FindIndex(sectionIndex + 1, l => l.Trim().StartsWith("["));
                if (endIndex == -1) endIndex = lines.Count;

                lines.RemoveRange(sectionIndex, endIndex - sectionIndex);
                File.WriteAllLines(_settingsFilePath, lines);
            }
        }

        #endregion

        #region --- Folder & Regex Methods ---

        public string GetBaseFolder() => GetFoldersFromSection("[BaseFolder]").FirstOrDefault();
        public void SetBaseFolder(string folderPath) => SetFoldersToSection("[BaseFolder]", new List<string> { folderPath });

        public List<string> GetFoldersFromSection(string section)
        {
            var folders = new List<string>();
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return folders;

                bool inSection = false;
                foreach (var line in File.ReadLines(_settingsFilePath))
                {
                    string trimmedLine = line.Trim();
                    if (trimmedLine.Equals(section, StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        continue;
                    }
                    if (inSection)
                    {
                        if (trimmedLine.StartsWith("[")) break;
                        if (!string.IsNullOrWhiteSpace(trimmedLine))
                        {
                            folders.Add(trimmedLine);
                        }
                    }
                }
            }
            return folders;
        }

        public void SetFoldersToSection(string section, List<string> folders)
        {
            lock (_fileLock)
            {
                var lines = File.ReadAllLines(_settingsFilePath).ToList();
                int sectionIndex = lines.FindIndex(l => l.Trim().Equals(section, StringComparison.OrdinalIgnoreCase));

                if (sectionIndex != -1)
                {
                    // 기존 섹션 제거
                    int endIndex = lines.FindIndex(sectionIndex + 1, l => l.Trim().StartsWith("["));
                    if (endIndex == -1) endIndex = lines.Count;
                    lines.RemoveRange(sectionIndex, endIndex - sectionIndex);
                }

                // 새 섹션 추가
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines.Last())) lines.Add("");
                lines.Add(section);
                lines.AddRange(folders);

                File.WriteAllLines(_settingsFilePath, lines);
            }
        }

        public Dictionary<string, string> GetRegexList()
        {
            var regexDict = new Dictionary<string, string>();
            var lines = GetFoldersFromSection("[Regex]");
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { "->" }, 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    regexDict[parts[0].Trim()] = parts[1].Trim();
                }
            }
            return regexDict;
        }

        public void SetRegexList(Dictionary<string, string> regexDict)
        {
            var lines = regexDict.Select(kvp => string.Format("{0} -> {1}", kvp.Key, kvp.Value)).ToList();
            SetFoldersToSection("[Regex]", lines);
        }

        #endregion

        #region --- File Management Methods ---

        public void ResetExceptEqpid()
        {
            string eqpid = GetEqpid();
            string type = GetApplicationType();

            lock (_fileLock)
            {
                File.WriteAllText(_settingsFilePath, string.Empty);
            }

            SetEqpid(eqpid);
            SetApplicationType(type);
        }

        public void LoadFromFile(string filePath)
        {
            try
            {
                lock (_fileLock)
                {
                    File.Copy(filePath, _settingsFilePath, true);
                }
                _logManager.LogEvent($"Settings loaded from: {filePath}");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"Failed to load settings from {filePath}: {ex.Message}");
                throw;
            }
        }

        public void SaveToFile(string filePath)
        {
            try
            {
                lock (_fileLock)
                {
                    File.Copy(_settingsFilePath, filePath, true);
                }
                _logManager.LogEvent($"Settings saved to: {filePath}");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"Failed to save settings to {filePath}: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}