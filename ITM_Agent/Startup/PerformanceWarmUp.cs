// ITM_Agent/Startup/PerformanceWarmUp.cs
using ITM_Agent.Common;
using Npgsql;
using System;
using System.Diagnostics;
using System.Threading;

namespace ITM_Agent.Startup
{
    /// <summary>
    /// 애플리케이션 시작 시 주요 구성 요소의 초기화 지연을 줄이기 위해
    /// 성능 카운터와 데이터베이스 연결을 미리 활성화하는 정적 유틸리티 클래스입니다.
    /// </summary>
    internal static class PerformanceWarmUp
    {
        /// <summary>
        /// 예열 프로세스를 실행합니다.
        /// </summary>
        public static void Run()
        {
            Console.WriteLine("[PerformanceWarmUp] Starting warmup process...");

            // 1. PDH 성능 카운터 예열
            // 처음 NextValue() 호출은 초기화 시간이 걸리므로, 유효한 값을 얻기 위해
            // 짧은 간격을 두고 두 번 호출하여 카운터를 준비시킵니다.
            try
            {
                Console.WriteLine("[PerformanceWarmUp] Warming up performance counters...");
                using (var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"))
                {
                    cpuCounter.NextValue();
                    Thread.Sleep(100);
                    cpuCounter.NextValue();
                }
                Console.WriteLine("[PerformanceWarmUp] Performance counters warmed up successfully.");
            }
            catch (Exception ex)
            {
                // 성능 카운터가 없는 시스템(예: 일부 Windows Server Core 버전)에서 예외가 발생할 수 있음
                Console.WriteLine($"[PerformanceWarmUp] Failed to warm up performance counters: {ex.Message}");
            }


            // 2. 데이터베이스 커넥션 풀 예열
            // 애플리케이션의 첫 DB 쿼리 시 발생할 수 있는 연결 지연을 최소화하기 위해
            // 미리 최소 1개의 연결을 생성하고 닫습니다.
            try
            {
                Console.WriteLine("[PerformanceWarmUp] Warming up database connection pool...");
                string connectionString = DatabaseInfo.CreateDefault().GetConnectionString();
                using (var conn = new NpgsqlConnection(connectionString))
                {
                    conn.Open();
                    // Open() 자체만으로도 연결 풀에 연결이 생성되므로 추가 쿼리는 불필요
                }
                Console.WriteLine("[PerformanceWarmUp] Database connection pool warmed up successfully.");
            }
            catch (Exception ex)
            {
                // DB 서버에 연결할 수 없는 경우, 로그만 남기고 애플리케이션 시작은 계속 진행
                Console.WriteLine($"[PerformanceWarmUp] Failed to warm up database connection pool: {ex.Message}");
            }
            
            Console.WriteLine("[PerformanceWarmUp] Warmup process finished.");
        }
    }
}
