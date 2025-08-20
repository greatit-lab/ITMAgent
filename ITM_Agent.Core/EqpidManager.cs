// ITM_Agent.Core/EqpidManager.cs
using ITM_Agent.Common;
using ITM_Agent.Common.Interfaces;
using Npgsql;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Management;

namespace ITM_Agent.Core
{
    /// <summary>
    /// Eqpid 및 관련 장비 정보를 관리하고 데이터베이스와 동기화하는 서비스 클래스입니다.
    /// </summary>
    public class EqpidManager
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ILogManager _logManager;
        private readonly string _appVersion;

        // UI 상호작용을 위한 델리게이트
        private readonly Func<(string Eqpid, string Type)> _promptForEqpidAction;
        private readonly Action _handleCanceledAction;

        // 장비별 타임존 정보를 캐싱하여 DB 조회를 최소화합니다.
        private static readonly ConcurrentDictionary<string, TimeZoneInfo> TimezoneCache = new ConcurrentDictionary<string, TimeZoneInfo>();

        public EqpidManager(ISettingsManager settings, ILogManager logger, string appVersion, Func<(string Eqpid, string Type)> promptAction, Action CanceledAction)
        {
            _settingsManager = settings ?? throw new ArgumentNullException(nameof(settings));
            _logManager = logger ?? throw new ArgumentNullException(nameof(logger));
            _appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));
            _promptForEqpidAction = promptAction ?? throw new ArgumentNullException(nameof(promptAction));
            _handleCanceledAction = CanceledAction ?? throw new ArgumentNullException(nameof(CanceledAction));
        }

        /// <summary>
        /// 애플리케이션 시작 시 Eqpid를 초기화합니다.
        /// 설정 파일에 Eqpid가 없으면 사용자에게 입력을 요청합니다.
        /// </summary>
        public void InitializeEqpid()
        {
            _logManager.LogEvent("[EqpidManager] Initializing Eqpid.");
            string eqpid = _settingsManager.GetEqpid();

            if (string.IsNullOrEmpty(eqpid))
            {
                _logManager.LogEvent("[EqpidManager] Eqpid not found in settings. Prompting for user input.");
                PromptForEqpid();
            }
            else
            {
                _logManager.LogEvent($"[EqpidManager] Eqpid found in settings: {eqpid}");
                _logManager.LogDebug($"[EqpidManager] Application Type from settings: {_settingsManager.GetApplicationType()}");
            }
        }

        /// <summary>
        /// 외부(UI)로부터 주입된 델리게이트를 통해 Eqpid 입력을 요청합니다.
        /// </summary>
        private void PromptForEqpid()
        {
            _logManager.LogDebug("[EqpidManager] Executing prompt action for Eqpid input.");
            var result = _promptForEqpidAction();

            if (result.Eqpid != null)
            {
                string newEqpid = result.Eqpid.ToUpper();
                string type = result.Type;

                _logManager.LogEvent($"[EqpidManager] User entered new Eqpid: {newEqpid}, Type: {type}");
                _logManager.LogDebug("[EqpidManager] Saving new Eqpid and Type to settings.");
                _settingsManager.SetEqpid(newEqpid);
                _settingsManager.SetApplicationType(type);

                UploadAgentInfoToDatabase(newEqpid, type);
            }
            else
            {
                // 입력이 취소된 경우
                _logManager.LogEvent("[EqpidManager] Eqpid input was canceled by the user.");
                _handleCanceledAction();
            }
        }

        /// <summary>
        /// 수집된 에이전트 정보를 데이터베이스에 업로드(INSERT 또는 UPDATE)합니다.
        /// </summary>
        private void UploadAgentInfoToDatabase(string eqpid, string type)
        {
            _logManager.LogEvent($"[EqpidManager] Preparing to upload agent info to database for Eqpid: {eqpid}");
            string connString = DatabaseInfo.CreateDefault().GetConnectionString();
            var systemInfo = SystemInfoCollector.Collect();

            _logManager.LogDebug($"[EqpidManager] System Info Collected: OS='{systemInfo.OsVersion}', Arch='{systemInfo.Architecture}', Machine='{systemInfo.MachineName}', Locale='{systemInfo.Locale}', TZ='{systemInfo.TimeZoneId}'");

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    _logManager.LogDebug("[EqpidManager] Database connection opened.");

                    const string upsertSql = @"
                        INSERT INTO public.agent_info
                        (eqpid, type, os, system_type, pc_name, locale, timezone, app_ver, reg_date, servtime)
                        VALUES
                        (@eqpid, @type, @os, @arch, @pc_name, @loc, @tz, @app_ver, @reg_date, NOW()::timestamp(0))
                        ON CONFLICT (eqpid, pc_name)
                        DO UPDATE SET
                            type = EXCLUDED.type,
                            os = EXCLUDED.os,
                            system_type = EXCLUDED.system_type,
                            locale = EXCLUDED.locale,
                            timezone = EXCLUDED.timezone,
                            app_ver = EXCLUDED.app_ver,
                            reg_date = EXCLUDED.reg_date,
                            servtime = NOW()::timestamp(0);";

                    using (var cmd = new NpgsqlCommand(upsertSql, conn))
                    {
                        DateTime pcTimeWithoutMilliseconds = new DateTime(
                            systemInfo.PcTime.Year,
                            systemInfo.PcTime.Month,
                            systemInfo.PcTime.Day,
                            systemInfo.PcTime.Hour,
                            systemInfo.PcTime.Minute,
                            systemInfo.PcTime.Second
                        );
                        _logManager.LogDebug($"[EqpidManager] PC time normalized to: {pcTimeWithoutMilliseconds:yyyy-MM-dd HH:mm:ss}");

                        cmd.Parameters.AddWithValue("@eqpid", eqpid);
                        cmd.Parameters.AddWithValue("@type", type);
                        cmd.Parameters.AddWithValue("@os", systemInfo.OsVersion);
                        cmd.Parameters.AddWithValue("@arch", systemInfo.Architecture);
                        cmd.Parameters.AddWithValue("@pc_name", systemInfo.MachineName);
                        cmd.Parameters.AddWithValue("@loc", systemInfo.Locale);
                        cmd.Parameters.AddWithValue("@tz", systemInfo.TimeZoneId);
                        cmd.Parameters.AddWithValue("@app_ver", _appVersion);
                        cmd.Parameters.AddWithValue("@reg_date", pcTimeWithoutMilliseconds);

                        _logManager.LogDebug("[EqpidManager] Executing UPSERT command for agent_info.");
                        int rowsAffected = cmd.ExecuteNonQuery();
                        _logManager.LogEvent($"[EqpidManager] Agent info uploaded to DB successfully. (Rows affected: {rowsAffected})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[EqpidManager] Failed to upload agent info to DB for Eqpid '{eqpid}': {ex.Message}");
                _logManager.LogDebug($"[EqpidManager] DB Upload Exception Details: {ex.ToString()}");
            }
        }

        /// <summary>
        /// 특정 Eqpid에 해당하는 타임존 정보를 캐시 또는 DB에서 조회하여 반환합니다.
        /// </summary>
        public TimeZoneInfo GetTimezoneForEqpid(string eqpid)
        {
            if (TimezoneCache.TryGetValue(eqpid, out TimeZoneInfo cachedZone))
            {
                _logManager.LogDebug($"[EqpidManager] Timezone for '{eqpid}' found in cache: {cachedZone.Id}");
                return cachedZone;
            }

            _logManager.LogDebug($"[EqpidManager] Timezone for '{eqpid}' not in cache. Fetching from database.");
            try
            {
                string connString = DatabaseInfo.CreateDefault().GetConnectionString();
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand("SELECT timezone FROM public.agent_info WHERE eqpid = @eqpid LIMIT 1", conn))
                    {
                        cmd.Parameters.AddWithValue("@eqpid", eqpid);
                        var result = cmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            string timezoneId = result.ToString();
                            _logManager.LogDebug($"[EqpidManager] Found timezone '{timezoneId}' for '{eqpid}' in DB.");
                            TimeZoneInfo fetchedZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                            TimezoneCache.TryAdd(eqpid, fetchedZone);
                            return fetchedZone;
                        }
                        _logManager.LogDebug($"[EqpidManager] No timezone information found in DB for '{eqpid}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[EqpidManager] Failed to fetch timezone for '{eqpid}' from DB: {ex.Message}");
                _logManager.LogDebug($"[EqpidManager] Fetch Timezone Exception Details: {ex.ToString()}");
            }

            _logManager.LogEvent($"[EqpidManager] Returning local timezone as a fallback for '{eqpid}'.");
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// 로컬 시스템의 정보를 수집하는 정적 헬퍼 클래스입니다.
    /// </summary>
    internal static class SystemInfoCollector
    {
        public static (string OsVersion, string Architecture, string MachineName, string Locale, string TimeZoneId, DateTime PcTime) Collect()
        {
            return (
                GetOSVersion(),
                Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit",
                Environment.MachineName,
                CultureInfo.CurrentUICulture.Name,
                TimeZoneInfo.Local.Id,
                DateTime.Now
            );
        }

        private static string GetOSVersion()
        {
            try
            {
                // WMI를 사용하여 더 상세한 OS 이름 가져오기
                using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        return obj["Caption"]?.ToString() ?? "Unknown OS";
                    }
                }
            }
            catch
            {
                // WMI 실패 시 기본 OS 버전 문자열 반환
                return Environment.OSVersion.VersionString;
            }
            return "Unknown OS";
        }
    }
}
