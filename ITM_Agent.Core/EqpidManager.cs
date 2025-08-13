// ITM_Agent.Core/EqpidManager.cs
using ITM_Agent.Common;
using ITM_Agent.Common.Interfaces;
using Npgsql;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Management;
// using System.Windows.Forms; // UI 종속성 제거

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
                _logManager.LogEvent("[EqpidManager] Eqpid is empty. Prompting for input.");
                PromptForEqpid();
            }
            else
            {
                _logManager.LogEvent($"[EqpidManager] Eqpid found: {eqpid}");
            }
        }

        /// <summary>
        /// 외부(UI)로부터 주입된 델리게이트를 통해 Eqpid 입력을 요청합니다.
        /// </summary>
        private void PromptForEqpid()
        {
            var result = _promptForEqpidAction();

            if (result.Eqpid != null)
            {
                string newEqpid = result.Eqpid.ToUpper();
                string type = result.Type;

                _logManager.LogEvent($"[EqpidManager] Eqpid input accepted: {newEqpid}, Type: {type}");
                _settingsManager.SetEqpid(newEqpid);
                _settingsManager.SetApplicationType(type);

                UploadAgentInfoToDatabase(newEqpid, type);
            }
            else
            {
                // 입력이 취소된 경우
                _logManager.LogEvent("[EqpidManager] Eqpid input was canceled.");
                _handleCanceledAction();
            }
        }

        /// <summary>
        /// 수집된 에이전트 정보를 데이터베이스에 업로드(INSERT 또는 UPDATE)합니다.
        /// </summary>
        private void UploadAgentInfoToDatabase(string eqpid, string type)
        {
            string connString = DatabaseInfo.CreateDefault().GetConnectionString();
            var systemInfo = SystemInfoCollector.Collect();

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();

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
                            servtime = NOW();";

                    using (var cmd = new NpgsqlCommand(upsertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@eqpid", eqpid);
                        cmd.Parameters.AddWithValue("@type", type);
                        cmd.Parameters.AddWithValue("@os", systemInfo.OsVersion);
                        cmd.Parameters.AddWithValue("@arch", systemInfo.Architecture);
                        cmd.Parameters.AddWithValue("@pc_name", systemInfo.MachineName);
                        cmd.Parameters.AddWithValue("@loc", systemInfo.Locale);
                        cmd.Parameters.AddWithValue("@tz", systemInfo.TimeZoneId);
                        cmd.Parameters.AddWithValue("@app_ver", _appVersion);
                        cmd.Parameters.AddWithValue("@reg_date", systemInfo.PcTime);

                        int rowsAffected = cmd.ExecuteNonQuery();
                        _logManager.LogEvent($"[EqpidManager] Agent info uploaded to DB. (Rows affected: {rowsAffected})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[EqpidManager] Failed to upload agent info to DB: {ex.Message}");
            }
        }

        /// <summary>
        /// 특정 Eqpid에 해당하는 타임존 정보를 캐시 또는 DB에서 조회하여 반환합니다.
        /// </summary>
        public TimeZoneInfo GetTimezoneForEqpid(string eqpid)
        {
            if (TimezoneCache.TryGetValue(eqpid, out TimeZoneInfo cachedZone))
            {
                return cachedZone;
            }

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
                            TimeZoneInfo fetchedZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                            TimezoneCache.TryAdd(eqpid, fetchedZone);
                            return fetchedZone;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[EqpidManager] Failed to fetch timezone for {eqpid}: {ex.Message}");
            }

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
                return Environment.OSVersion.VersionString;
            }
            return "Unknown OS";
        }
    }
}