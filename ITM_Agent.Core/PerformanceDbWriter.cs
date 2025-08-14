// ITM_Agent.Core/PerformanceDbWriter.cs
using ITM_Agent.Common;
using ITM_Agent.Common.Interfaces;
using Npgsql;
using System;
using System.Collections.Generic;

namespace ITM_Agent.Core
{
    /// <summary>
    /// PerformanceMonitor로부터 수집된 성능 데이터를 주기적으로 데이터베이스에 기록하는 싱글턴 서비스 클래스입니다.
    /// </summary>
    public sealed class PerformanceDbWriter : IDisposable
    {
        #region --- Singleton Implementation ---

        private static PerformanceDbWriter _currentInstance;
        private static readonly object _instanceLock = new object();

        /// <summary>
        /// 성능 DB 기록 서비스를 시작합니다. 이미 실행 중인 경우 아무 작업도 수행하지 않습니다.
        /// </summary>
        /// <param name="eqpid">현재 장비의 Eqpid입니다.</param>
        /// <param name="eqpidManager">타임존 정보 조회를 위한 EqpidManager 인스턴스입니다.</param>
        /// <param name="logger">로그 기록을 위한 ILogManager 인스턴스입니다.</param>
        public static void Start(string eqpid, EqpidManager eqpidManager, ILogManager logger)
        {
            lock (_instanceLock)
            {
                if (_currentInstance != null) return;

                PerformanceMonitor.Instance.StartSampling(); // 데이터 수집 시작
                _currentInstance = new PerformanceDbWriter(eqpid, eqpidManager, logger);
                logger.LogEvent("[PerformanceDbWriter] Service started.");
            }
        }

        /// <summary>
        /// 성능 DB 기록 서비스를 중지합니다.
        /// </summary>
        public static void Stop()
        {
            lock (_instanceLock)
            {
                if (_currentInstance == null) return;

                PerformanceMonitor.Instance.StopSampling(); // 데이터 수집 중지
                _currentInstance.Dispose();
                _currentInstance = null;
            }
        }

        #endregion

        #region --- Fields and Constructor ---

        private readonly string _eqpid;
        private readonly EqpidManager _eqpidManager;
        private readonly ILogManager _logManager;
        private readonly List<Metric> _buffer = new List<Metric>(1000);
        private readonly System.Threading.Timer _flushTimer;
        private readonly object _bufferLock = new object();
        private const int BULK_WRITE_COUNT = 60;
        private const int FLUSH_INTERVAL_MS = 30000;

        private PerformanceDbWriter(string eqpid, EqpidManager eqpidManager, ILogManager logger)
        {
            // Eqpid 문자열에서 "Eqpid:" 접두사 제거
            _eqpid = eqpid.StartsWith("Eqpid:", StringComparison.OrdinalIgnoreCase)
                   ? eqpid.Substring(6).Trim()
                   : eqpid.Trim();

            _eqpidManager = eqpidManager;
            _logManager = logger;

            PerformanceMonitor.Instance.OnSample += OnSampleReceived; // 성능 데이터 구독
            _flushTimer = new System.Threading.Timer(_ => FlushBufferToDb(), null, FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
        }

        #endregion

        #region --- Private Logic ---

        private void OnSampleReceived(Metric metric)
        {
            lock (_bufferLock)
            {
                _buffer.Add(metric);
                if (_buffer.Count >= BULK_WRITE_COUNT)
                {
                    // 버퍼가 가득 차면 즉시 DB에 기록
                    FlushBufferToDb();
                }
            }
        }

        /// <summary>
        /// 버퍼에 쌓인 성능 데이터를 DB에 일괄 기록(Bulk Insert)합니다.
        /// </summary>
        private void FlushBufferToDb()
        {
            List<Metric> batch;
            lock (_bufferLock)
            {
                if (_buffer.Count == 0) return;
                batch = new List<Metric>(_buffer);
                _buffer.Clear();
            }

            _logManager.LogDebug($"[PerformanceDbWriter] Flushing {batch.Count} performance metrics to DB.");
            string connString = DatabaseInfo.CreateDefault().GetConnectionString();

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        const string sql = @"
                            INSERT INTO public.eqp_perf (eqpid, ts, serv_ts, cpu_usage, mem_usage)
                            VALUES (@eqpid, @ts, @serv_ts, @cpu, @mem)
                            ON CONFLICT (eqpid, ts) DO NOTHING;";

                        foreach (var metric in batch)
                        {
                            using (var cmd = new NpgsqlCommand(sql, conn, tx))
                            {
                                // 밀리초를 제거하여 초 단위로 시간 정규화
                                var timestamp = new DateTime(metric.Timestamp.Year, metric.Timestamp.Month, metric.Timestamp.Day,
                                                             metric.Timestamp.Hour, metric.Timestamp.Minute, metric.Timestamp.Second);

                                // TimeSyncProvider를 통해 KST로 보정된 서버 시간 계산
                                var serverTimestamp = TimeSyncProvider.Instance.ToSynchronizedKst(timestamp);

                                // *** 수정된 부분: 'serv_ts' 값에서 밀리초를 제거합니다. ***
                                // 보정된 KST 시간(serverTimestamp)을 기반으로 시/분/초까지만 포함된
                                // 새로운 DateTime 객체를 생성하여 밀리초를 0으로 만듭니다.
                                var serverTimestampWithoutMilliseconds = new DateTime(
                                    serverTimestamp.Year,
                                    serverTimestamp.Month,
                                    serverTimestamp.Day,
                                    serverTimestamp.Hour,
                                    serverTimestamp.Minute,
                                    serverTimestamp.Second
                                );

                                // CPU 사용량이 0보다 크지만 반올림 후 0이 되는 경우 0.01로 보정
                                float cpuUsage = (float)Math.Round(metric.CpuUsage, 2);
                                if (cpuUsage == 0.0f && metric.CpuUsage > 0.0f)
                                {
                                    cpuUsage = 0.01f;
                                }

                                cmd.Parameters.AddWithValue("@eqpid", _eqpid);
                                cmd.Parameters.AddWithValue("@ts", timestamp);
                                cmd.Parameters.AddWithValue("@serv_ts", serverTimestampWithoutMilliseconds);
                                cmd.Parameters.AddWithValue("@cpu", cpuUsage);
                                cmd.Parameters.AddWithValue("@mem", (float)Math.Round(metric.MemoryUsage, 2));

                                cmd.ExecuteNonQuery();
                            }
                        }
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                _logManager.LogError($"[PerformanceDbWriter] Failed to flush performance data to DB: {ex.Message}");
                // 실패 시 데이터를 버퍼에 다시 넣는 로직을 추가할 수 있으나, 여기서는 단순 실패로 처리
            }
        }

        #endregion

        public void Dispose()
        {
            _logManager.LogEvent("[PerformanceDbWriter] Service stopping.");
            PerformanceMonitor.Instance.OnSample -= OnSampleReceived; // 구독 해제
            _flushTimer?.Dispose();
            FlushBufferToDb(); // 종료 전 남은 데이터 모두 기록
        }
    }
}
