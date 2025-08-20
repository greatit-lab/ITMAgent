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

        public SettingsManager(string settingsFilePath)
        {
            _settingsFilePath = settingsFilePath;
            _logManager = new LogManager(Path.GetDirectoryName(settingsFilePath));
            EnsureSettingsFileExists();
            IsDebugMode = GetValueFromSection("Option", "DebugMode") == "1";
            _logManager.LogDebug($"[SettingsManager] Initialized. Settings file path: '{_settingsFilePath}'");
        }

        private void EnsureSettingsFileExists()
        {
            if (!File.Exists(_settingsFilePath))
            {
                try
                {
                    _logManager.LogEvent($"[SettingsManager] Settings file not found. Creating a new one at '{_settingsFilePath}'.");
                    File.Create(_settingsFilePath).Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL] SettingsManager - Could not create settings file at {_settingsFilePath}: {ex.Message}");
                    _logManager.LogError($"[SettingsManager] Could not create settings file at {_settingsFilePath}: {ex.Message}");
                }
            }
        }

        #region --- General Methods ---

        public string GetEqpid() => GetValueFromSection("Eqpid", "Eqpid");
        public void SetEqpid(string eqpid)
        {
            _logManager.LogEvent($"[SettingsManager] Setting Eqpid to: {eqpid}");
            SetValueToSection("Eqpid", "Eqpid", eqpid);
        }

        public string GetApplicationType() => GetValueFromSection("Eqpid", "Type");
        public void SetApplicationType(string type)
        {
            _logManager.LogEvent($"[SettingsManager] Setting Application Type to: {type}");
            SetValueToSection("Eqpid", "Type", type);
        }

        public string GetValueFromSection(string section, string key)
        {
            _logManager.LogDebug($"[SettingsManager] Reading value for Key='{key}' in Section='{section}'.");
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath))
                {
                    _logManager.LogDebug($"[SettingsManager] Settings file not found while reading. Returning null.");
                    return null;
                }

                try
                {
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
                            if (trimmedLine.StartsWith("[")) break;

                            string[] parts = line.Split(new[] { '=' }, 2);
                            if (parts.Length == 2 && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                            {
                                string value = parts[1].Trim();
                                _logManager.LogDebug($"[SettingsManager] Value found: '{value}'.");
                                return value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[SettingsManager] Error reading file '{_settingsFilePath}': {ex.Message}");
                }
            }
            _logManager.LogDebug($"[SettingsManager] Key='{key}' not found in Section='{section}'. Returning null.");
            return null;
        }

        public void SetValueToSection(string section, string key, string value)
        {
            _logManager.LogDebug($"[SettingsManager] Setting value for Key='{key}' to '{value}' in Section='{section}'.");
            lock (_fileLock)
            {
                try
                {
                    var lines = File.Exists(_settingsFilePath) ? File.ReadAllLines(_settingsFilePath).ToList() : new List<string>();
                    int sectionIndex = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));

                    if (sectionIndex == -1)
                    {
                        _logManager.LogDebug($"[SettingsManager] Section '{section}' not found. Creating new section.");
                        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines.Last())) lines.Add("");
                        lines.Add($"[{section}]");
                        lines.Add($"{key} = {value}");
                    }
                    else
                    {
                        int keyIndex = -1;
                        int searchEndIndex = lines.Count;
                        for (int i = sectionIndex + 1; i < lines.Count; i++)
                        {
                            if (lines[i].Trim().StartsWith("["))
                            {
                                searchEndIndex = i;
                                break;
                            }
                            if (lines[i].Trim().StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase) || lines[i].Trim().StartsWith($"{key} =", StringComparison.OrdinalIgnoreCase))
                            {
                                keyIndex = i;
                                break;
                            }
                        }

                        if (keyIndex != -1)
                        {
                            _logManager.LogDebug($"[SettingsManager] Key '{key}' found. Updating value.");
                            lines[keyIndex] = $"{key} = {value}";
                        }
                        else
                        {
                            _logManager.LogDebug($"[SettingsManager] Key '{key}' not found. Adding new key to section.");
                            lines.Insert(searchEndIndex, $"{key} = {value}");
                        }
                    }
                    File.WriteAllLines(_settingsFilePath, lines);
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[SettingsManager] Error writing to file '{_settingsFilePath}': {ex.Message}");
                }
            }
        }

        public void RemoveKeyFromSection(string section, string key)
        {
            _logManager.LogDebug($"[SettingsManager] Removing Key='{key}' from Section='{section}'.");
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return;
                try
                {
                    var lines = File.ReadAllLines(_settingsFilePath).ToList();
                    int sectionIndex = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
                    if (sectionIndex == -1)
                    {
                        _logManager.LogDebug($"[SettingsManager] Section '{section}' not found. Nothing to remove.");
                        return;
                    }

                    for (int i = sectionIndex + 1; i < lines.Count; i++)
                    {
                        if (lines[i].Trim().StartsWith("[")) break;

                        if (lines[i].Trim().StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase) || lines[i].Trim().StartsWith($"{key} =", StringComparison.OrdinalIgnoreCase))
                        {
                            lines.RemoveAt(i);
                            _logManager.LogDebug($"[SettingsManager] Key '{key}' removed.");
                            break;
                        }
                    }
                    File.WriteAllLines(_settingsFilePath, lines);
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[SettingsManager] Error processing file for key removal '{_settingsFilePath}': {ex.Message}");
                }
            }
        }

        public void RemoveSection(string section)
        {
            _logManager.LogDebug($"[SettingsManager] Removing Section='{section}'.");
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return;
                try
                {
                    var lines = File.ReadAllLines(_settingsFilePath).ToList();
                    int sectionIndex = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
                    if (sectionIndex == -1)
                    {
                        _logManager.LogDebug($"[SettingsManager] Section '{section}' not found. Nothing to remove.");
                        return;
                    }

                    int endIndex = lines.FindIndex(sectionIndex + 1, l => l.Trim().StartsWith("["));
                    if (endIndex == -1) endIndex = lines.Count;

                    lines.RemoveRange(sectionIndex, endIndex - sectionIndex);
                    File.WriteAllLines(_settingsFilePath, lines);
                    _logManager.LogDebug($"[SettingsManager] Section '{section}' removed.");
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[SettingsManager] Error processing file for section removal '{_settingsFilePath}': {ex.Message}");
                }
            }
        }

        #endregion

        #region --- Folder & Regex Methods ---

        public string GetBaseFolder() => GetFoldersFromSection("[BaseFolder]").FirstOrDefault();
        public void SetBaseFolder(string folderPath)
        {
            _logManager.LogEvent($"[SettingsManager] Setting BaseFolder to: {folderPath}");
            SetFoldersToSection("[BaseFolder]", new List<string> { folderPath });
        }

        public List<string> GetFoldersFromSection(string section)
        {
            var folders = new List<string>();
            _logManager.LogDebug($"[SettingsManager] Reading folders from Section='{section}'.");
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return folders;

                try
                {
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
                catch (Exception ex)
                {
                    _logManager.LogError($"[SettingsManager] Error reading folders from section '{section}': {ex.Message}");
                }
            }
            _logManager.LogDebug($"[SettingsManager] Found {folders.Count} folder(s) in section '{section}'.");
            return folders;
        }

        public void SetFoldersToSection(string section, List<string> folders)
        {
            _logManager.LogDebug($"[SettingsManager] Setting {folders.Count} folder(s) to Section='{section}'.");
            lock (_fileLock)
            {
                try
                {
                    var lines = File.ReadAllLines(_settingsFilePath).ToList();
                    int sectionIndex = lines.FindIndex(l => l.Trim().Equals(section, StringComparison.OrdinalIgnoreCase));

                    if (sectionIndex != -1)
                    {
                        _logManager.LogDebug($"[SettingsManager] Existing section '{section}' found. Removing before re-adding.");
                        int endIndex = lines.FindIndex(sectionIndex + 1, l => l.Trim().StartsWith("["));
                        if (endIndex == -1) endIndex = lines.Count;
                        lines.RemoveRange(sectionIndex, endIndex - sectionIndex);
                    }

                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines.Last())) lines.Add("");
                    lines.Add(section);
                    lines.AddRange(folders);

                    File.WriteAllLines(_settingsFilePath, lines);
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[SettingsManager] Error writing folders to section '{section}': {ex.Message}");
                }
            }
        }

        public Dictionary<string, string> GetRegexList()
        {
            var regexDict = new Dictionary<string, string>();
            _logManager.LogDebug("[SettingsManager] Reading regex list from [Regex] section.");
            var lines = GetFoldersFromSection("[Regex]");
            froeach (var line in lines)
            {
                var parts = line.Split(new[] { "->" }, 2, stringSplitOptions.None);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string value = parts[1].Trim();
                    regexDict[key] = value;
                    _logManager.LogDebug($"[SettingsManager] Parsed regex entry: '{key}' -> '{value}'");
                }
                else
                {
                    _logManager.LogDebug($"[SettingsManager] Could not parse regex line: '{line}'");
                }
            }
            return regexDict;
        }

        public void SetRegexList(Dictionary<string, string> regexDict)
        {
            _logManager.LogDebug($"[SettingsManager] Setting {regexDict.Count} regex entries to [Regex] section.");
            var lines = regexDict.Select(kvp => string.Format("{0} -> {1}", kvp.Key, kvp.Value)).ToList();
            SetFoldersToSection("[Regex]", lines);
        }

        #endregion

        #region --- File Management Methods ---

        public void ResetExceptEqpid()
        {
            _logManager.LogEvent("[SettingsManager] Resetting all settings except Eqpid info.");
            string eqpid = GetEqpid();
            string type = GetApplicationType();

            try
            {
                lock (_fileLock)
                {
                    File.WriteAllText(_settingsFilePath, string.Empty);
                }

                SetEqpid(eqpid);
                SetApplicationType(type);
                _logManager.LogEvent("[SettingsManager] Settings reset successfully.");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[SettingsManager] Failed to reset settings file: {ex.Message}");
            }
        }

        public void LoadFromFile(string filePath)
        {
            _logManager.LogEvent($"[SettingsManager] Loading settings from external file: {filePath}");
            try
            {
                lock (_fileLock)
                {
                    File.Copy(filePath, _settingsFilePath, true);
                }
                _logManager.LogEvent($"[SettingsManager] Settings loaded successfully from: {filePath}");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[SettingsManager] Failed to load settings from '{filePath}': {ex.Message}");
                throw;
            }
        }

        public void SaveToFile(string filePath)
        {
            _logManager.LogEvent($"[SettingsManager] Saving current settings to external file: {filePath}");
            try
            {
                lock (_fileLock)
                {
                    File.Copy(_settingsFilePath, filePath, true);
                }
                _logManager.LogEvent($"[SettingsManager] Settings saved successfully to: {filePath}");
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[SettingsManager] Failed to save settings to '{filePath}': {ex.Message}");
                throw;
            }
        }

        public Dictionary<string, string> GetSectionAsDictionary(string sectionName)
        {
            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _logManager.LogDebug($"[SettingsManager] Reading section '{sectionName}' as a dictionary.");
            lock (_fileLock)
            {
                if (!File.Exists(_settingsFilePath)) return dictionary;

                try
                {
                    bool inSection = false;
                    foreach (var line in File.ReadLines(_settingsFilePath))
                    {
                        string trimmedLine = line.Trim();
                        if (trimmedLine.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                        {
                            inSection = true;
                            continue;
                        }

                        if (inSection)
                        {
                            if (trimmedLine.StartsWith("[")) break;

                            var parts = trimmedLine.Split(new[] { '=' }, 2);
                            if (parts.Length == 2)
                            {
                                string key = parts[0].Trim();
                                string value = parts[1].Trim();
                                if (!string.IsNullOrEmpty(key))
                                {
                                    dictionary[key] = value;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logManager.LogError($"[SettingsManager] Error reading section '{sectionName}' from file: {ex.Message}");
                }
            }
            _logManager.LogDebug($"[SettingsManager] Found {dictionary.Count} key-value pair(s) in section '{sectionName}'.");
            return dictionary;
        }

        #endregion
    }
}
