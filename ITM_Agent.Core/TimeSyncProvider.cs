// ITM_Agent.Core/TimeSyncProvider.cs
using ITM_Agent.Common;
using Npgsql;
using System;
using System.Threading;
using ITM_Agent.Common.Interfaces;

namespace ITM_Agent.Core
{
    /// <summary>
    /// 서버와 에이전트 PC 간의 시간 오차를 보정하고, 모든 시간을 한국 표준시(KST)로 변환하는
    /// 중앙 집중형 시간 동기화 싱글턴 서비스 클래스입니다.
    /// </summary>
    public sealed class TimeSyncProvider : IDisposable, ITimeSyncProvider
    {
        #region --- Singleton Implementation ---

        private static readonly Lazy<TimeSyncProvider> _instance =
            new Lazy<TimeSyncProvider>(() => new TimeSyncProvider());

        /// <summary>
        /// TimeSyncProvider의 단일 인스턴스를 가져옵니다.
        /// </summary>
        public static TimeSyncProvider Instance => _instance.Value;

        #endregion

        #region --- Fields ---

        private readonly object _syncLock = new object();
        private TimeSpan _clockDifference = TimeSpan.Zero; // 서버와 PC의 순수 시간 차이
        private readonly TimeZoneInfo _koreaStandardTimezone;
        private readonly Timer _syncTimer;

        #endregion

        private TimeSyncProvider()
        {
            Console.WriteLine("[INFO] TimeSyncProvider - Initializing instance...");
            // KST 타임존 정보 로드 (Windows와 Linux/macOS 환경 모두 호환)
            try
            {
                _koreaStandardTimezone = TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                try
                {
                    _koreaStandardTimezone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL] TimeSyncProvider - KST timezone could not be found. {ex.Message}");
                    // KST를 찾을 수 없는 경우, 시스템 로컬 타임존을 사용 (최후의 수단)
                    _koreaStandardTimezone = TimeZoneInfo.Local;
                }
            }
            Console.WriteLine($"[INFO] TimeSyncProvider - Using timezone: {_koreaStandardTimezone.Id}");

            // 앱 시작 시 즉시 동기화 후, 10분마다 주기적으로 서버 시간과 동기화
            _syncTimer = new Timer(
                _ => SynchronizeWithServer(),
                null,
                TimeSpan.Zero,           // 즉시 1회 실행
                TimeSpan.FromMinutes(10) // 10분 간격
            );
        }

        /// <summary>
        /// DB 서버의 UTC 시간과 로컬 PC의 UTC 시간을 비교하여 순수한 시간 차이를 계산하고 저장합니다.
        /// </summary>
        private void SynchronizeWithServer()
        {
            Console.WriteLine("[EVENT] TimeSyncProvider - Starting time synchronization with the server...");
            try
            {
                string connString = DatabaseInfo.CreateDefault().GetConnectionString();
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    // 'UTC' 타임존의 현재 시간을 조회하여 서버와 클라이언트 간의 시간대 차이 영향을 배제
                    using (var cmd = new NpgsqlCommand("SELECT NOW() AT TIME ZONE 'UTC'", conn))
                    {
                        DateTime serverUtcTime = Convert.ToDateTime(cmd.ExecuteScalar());
                        DateTime clientUtcTime = DateTime.UtcNow;

                        lock (_syncLock)
                        {
                            _clockDifference = serverUtcTime - clientUtcTime;
                        }
                        Console.WriteLine($"[EVENT] TimeSyncProvider - Time synchronized successfully.");
                        Console.WriteLine($"[DEBUG] TimeSyncProvider - Server UTC: {serverUtcTime:yyyy-MM-dd HH:mm:ss.fff}, Client UTC: {clientUtcTime:yyyy-MM-dd HH:mm:ss.fff}, Calculated Difference: {_clockDifference.TotalMilliseconds}ms");
                    }
                }
            }
            catch (Exception ex)
            {
                // DB 연결 실패 시 기존 시간 차이 값을 유지하며, 에러 로그 출력
                Console.WriteLine($"[ERROR] TimeSyncProvider - Failed to synchronize time with server: {ex.Message}");
            }
        }

        /// <summary>
        /// 장비에서 발생한 로컬 시간을 서버 시간 기준으로 보정한 후, 한국 표준시(KST)로 변환합니다.
        /// </summary>
        /// <param name="agentLocalTime">장비에서 발생한 로컬 시간입니다.</param>
        /// <returns>서버 시간과 동기화되고 KST로 변환된 최종 시간입니다.</returns>
        public DateTime ToSynchronizedKst(DateTime agentLocalTime)
        {
            // 1. 입력된 시간을 UTC 시간으로 변환합니다.
            //    (DateTimeKind가 지정되지 않았다면 Local로 간주하여 변환)
            DateTime agentUtcTime = (agentLocalTime.Kind == DateTimeKind.Unspecified)
                                  ? DateTime.SpecifyKind(agentLocalTime, DateTimeKind.Local).ToUniversalTime()
                                  : agentLocalTime.ToUniversalTime();

            // 2. 미리 계산된 서버-장비 시간 오차를 더하여 시간을 동기화합니다.
            DateTime synchronizedUtcTime;
            lock (_syncLock)
            {
                synchronizedUtcTime = agentUtcTime.Add(_clockDifference);
            }

            // 3. 동기화된 UTC 시간을 최종적으로 KST로 변환하여 반환합니다.
            DateTime kstTime = TimeZoneInfo.ConvertTimeFromUtc(synchronizedUtcTime, _koreaStandardTimezone);

            // 디버깅을 위해 변환 과정을 로그로 남길 수 있지만, 너무 빈번하게 호출되므로 주석 처리
            // Console.WriteLine($"[DEBUG] TimeSyncProvider - Time conversion: {agentLocalTime:s} (Local) -> {synchronizedUtcTime:s} (Sync UTC) -> {kstTime:s} (KST)");

            return kstTime;
        }

        /// <summary>
        /// Timer 리소스를 해제합니다.
        /// </summary>
        public void Dispose()
        {
            Console.WriteLine("[INFO] TimeSyncProvider - Disposing instance and stopping timer.");
            _syncTimer?.Dispose();
        }
    }
}
