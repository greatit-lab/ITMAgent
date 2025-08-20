// ITM_Agent.Core/PerformanceMonitor.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;

namespace ITM_Agent.Core
{
    /// <summary>
    /// CPU 및 메모리 사용률을 주기적으로 수집하여 관리하는 싱글턴 서비스 클래스입니다.
    /// 수집된 데이터는 내부 버퍼에 저장되며, 파일 로깅이 활성화된 경우 로그 파일로 기록됩니다.
    /// </summary>
    public sealed class PerformanceMonitor : IDisposable
    {
        #region --- Singleton Implementation ---

        private static readonly Lazy<PerformanceMonitor> _instance =
            new Lazy<PerformanceMonitor>(() => new PerformanceMonitor());

        /// <summary>
        /// PerformanceMonitor의 단일 인스턴스를 가져옵니다.
        /// </summary>
        public static PerformanceMonitor Instance => _instance.Value;

        #endregion

        #region --- Constants and Fields ---

        private const long MAX_LOG_SIZE = 5 * 1_024 * 1_024; // 5 MB
        private const int FLUSH_INTERVAL_MS = 30_000;         // 30초마다 파일 쓰기
        private const int BULK_WRITE_COUNT = 60;             // 60건 이상 쌓이면 즉시 쓰기

        private readonly PdhSampler _sampler;
        private readonly CircularBuffer<Metric> _buffer = new CircularBuffer<Metric>(1000);
        private readonly Timer _flushTimer;
        private readonly object _syncLock = new object();

        private bool _isSamplingEnabled = false;
        private bool _isFileLoggingEnabled = false;

        #endregion

        private PerformanceMonitor()
        {
            Console.WriteLine("[INFO] PerformanceMonitor - Initializing instance...");
            _sampler = new PdhSampler(initialIntervalMs: 5000); // 기본 5초 간격
            _sampler.OnSample += OnSampleReceived;
            _sampler.OnThresholdExceeded += () =>
            {
                Console.WriteLine("[EVENT] PerformanceMonitor - System load threshold exceeded. Increasing sampling frequency.");
                _sampler.IntervalMs = 1000; // 과부하: 1초
            };
            _sampler.OnBackToNormal += () =>
            {
                Console.WriteLine("[EVENT] PerformanceMonitor - System load is back to normal. Decreasing sampling frequency.");
                _sampler.IntervalMs = 5000;    // 정상: 5초
            };

            _flushTimer = new Timer(_ => FlushBufferToFile(), null, Timeout.Infinite, Timeout.Infinite);
            Console.WriteLine("[INFO] PerformanceMonitor - Instance initialized.");
        }

        #region --- Public Control Methods ---

        /// <summary>
        /// 성능 데이터 수집을 시작합니다.
        /// </summary>
        public void StartSampling()
        {
            lock (_syncLock)
            {
                if (_isSamplingEnabled)
                {
                    Console.WriteLine("[DEBUG] PerformanceMonitor - StartSampling called but already running.");
                    return;
                }
                Console.WriteLine("[EVENT] PerformanceMonitor - Starting performance sampling...");
                _sampler.Start();
                _isSamplingEnabled = true;
            }
        }

        /// <summary>
        /// 성능 데이터 수집을 중지하고, 활성화된 경우 파일 로깅도 중지합니다.
        /// </summary>
        public void StopSampling()
        {
            lock (_syncLock)
            {
                if (!_isSamplingEnabled) return;
                Console.WriteLine("[EVENT] PerformanceMonitor - Stopping performance sampling...");
                _sampler.Stop();
                SetFileLogging(false); // 샘플링 중지 시 파일 로깅도 비활성화
                _isSamplingEnabled = false;
            }
        }

        /// <summary>
        /// 수집된 성능 데이터를 파일로 기록하는 기능을 활성화하거나 비활성화합니다.
        /// </summary>
        public void SetFileLogging(bool enable)
        {
            lock (_syncLock)
            {
                if (_isFileLoggingEnabled == enable) return;

                if (enable)
                {
                    Console.WriteLine("[EVENT] PerformanceMonitor - Enabling file logging for performance data.");
                    Directory.CreateDirectory(GetLogDirectory());
                    _flushTimer.Change(FLUSH_INTERVAL_MS, FLUSH_INTERVAL_MS);
                }
                else
                {
                    Console.WriteLine("[EVENT] PerformanceMonitor - Disabling file logging for performance data.");
                    _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    FlushBufferToFile(); // 비활성화 전 남은 버퍼 기록
                }
                _isFileLoggingEnabled = enable;
            }
        }

        /// <summary>
        /// 외부 소비자가 성능 측정 샘플을 구독할 수 있도록 이벤트를 노출합니다.
        /// </summary>
        public event Action<Metric> OnSample
        {
            add { _sampler.OnSample += value; }
            remove { _sampler.OnSample -= value; }
        }

        #endregion

        #region --- Private Logic ---

        private void OnSampleReceived(Metric metric)
        {
            lock (_syncLock)
            {
                _buffer.Push(metric);
                // 너무 빈번하므로 이벤트 로그 대신 콘솔 디버그 로그로 변경
                Console.WriteLine($"[DEBUG] PerformanceMonitor - Sample received. CPU: {metric.CpuUsage:F2}%, Mem: {metric.MemoryUsage:F2}%. Buffer count: {_buffer.Count}");
                if (_isFileLoggingEnabled && _buffer.Count >= BULK_WRITE_COUNT)
                {
                    Console.WriteLine($"[DEBUG] PerformanceMonitor - Buffer reached bulk write count ({_buffer.Count}/{BULK_WRITE_COUNT}). Flushing to file.");
                    FlushBufferToFile();
                }
            }
        }

        private void FlushBufferToFile()
        {
            Metric[] bufferedMetrics;
            lock (_syncLock)
            {
                if (!_isFileLoggingEnabled || _buffer.Count == 0) return;
                bufferedMetrics = _buffer.ToArray();
                _buffer.Clear();
            }

            if (bufferedMetrics.Length == 0) return;

            Console.WriteLine($"[DEBUG] PerformanceMonitor - Flushing {bufferedMetrics.Length} performance metrics to file.");
            string filePath = Path.Combine(GetLogDirectory(), $"{DateTime.Now:yyyyMMdd}_performance.log");
            RotatePerfLogIfNeeded(filePath);

            try
            {
                var logLines = new StringBuilder();
                foreach (Metric m in bufferedMetrics)
                {
                    logLines.AppendFormat("{0:yyyy-MM-dd HH:mm:ss.fff} C:{1:F2} M:{2:F2}{3}",
                        m.Timestamp, m.CpuUsage, m.MemoryUsage, Environment.NewLine);
                }
                File.AppendAllText(filePath, logLines.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] PerformanceMonitor - Failed to flush performance log to '{filePath}': {ex.Message}");
            }
        }

        private void RotatePerfLogIfNeeded(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists || fileInfo.Length <= MAX_LOG_SIZE) return;

                Console.WriteLine($"[DEBUG] PerformanceMonitor - Performance log '{fileInfo.Name}' exceeds max size. Rotating file.");
                string dir = fileInfo.DirectoryName;
                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string ext = fileInfo.Extension;

                int index = 1;
                string rotatedPath;
                do
                {
                    rotatedPath = Path.Combine(dir, $"{baseName}_{index++}{ext}");
                } while (File.Exists(rotatedPath));

                File.Move(filePath, rotatedPath);
                Console.WriteLine($"[DEBUG] PerformanceMonitor - Performance log rotated to '{Path.GetFileName(rotatedPath)}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] PerformanceMonitor - Failed to rotate performance log '{filePath}': {ex.Message}");
            }
        }

        private static string GetLogDirectory() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public void Dispose()
        {
            Console.WriteLine("[INFO] PerformanceMonitor - Disposing instance.");
            StopSampling();
            _flushTimer?.Dispose();
            _sampler?.Dispose();
        }

        #endregion
    }

    #region --- Supporting Classes and Structs ---

    /// <summary>
    /// 단일 시점의 성능 측정 데이터를 나타내는 읽기 전용 구조체입니다.
    /// </summary>
    public readonly struct Metric
    {
        public DateTime Timestamp { get; }
        public float CpuUsage { get; }
        public float MemoryUsage { get; }

        public Metric(float cpu, float mem)
        {
            Timestamp = DateTime.Now; // 측정 시점의 로컬 PC 시각
            CpuUsage = cpu;
            MemoryUsage = mem;
        }
    }

    /// <summary>
    /// PDH(Performance Counter)를 사용하여 시스템 성능을 샘플링하는 경량 클래스입니다.
    /// </summary>
    internal sealed class PdhSampler : IDisposable
    {
        public event Action<Metric> OnSample;
        public event Action OnThresholdExceeded;
        public event Action OnBackToNormal;

        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _memFreeMbCounter;
        private readonly float _totalMemoryMb;
        private Timer _timer;
        private int _interval;
        private bool _isOverloaded;
        private bool _isInitialized = false;

        public int IntervalMs
        {
            get => _interval;
            set
            {
                _interval = Math.Max(500, value); // 최소 0.5초 보장
                _timer?.Change(0, _interval);
            }
        }

        public PdhSampler(int initialIntervalMs)
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                _memFreeMbCounter = new PerformanceCounter("Memory", "Available MBytes", true);
                _totalMemoryMb = GetTotalMemoryInMb();
                _interval = initialIntervalMs;
                _isInitialized = true;
                Console.WriteLine("[INFO] PdhSampler - Performance counters initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] PdhSampler - Failed to initialize performance counters: {ex.Message}. Performance monitoring will be disabled.");
                _isInitialized = false;
            }
        }

        public void Start()
        {
            if (!_isInitialized) return;
            // 초기 더미 호출로 카운터 준비
            _cpuCounter.NextValue();
            _timer = new Timer(_ => Sample(), null, 0, _interval);
            Console.WriteLine("[INFO] PdhSampler - Sampling timer started.");
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            Console.WriteLine("[INFO] PdhSampler - Sampling timer stopped.");
        }

        private void Sample()
        {
            if (!_isInitialized) return;
            try
            {
                // 정확한 측정을 위해 1초 대기 후 값 수집 (기존 로직 유지)
                Thread.Sleep(1000);
                float cpuValue = _cpuCounter.NextValue();
                float freeMb = _memFreeMbCounter.NextValue();

                float usedMemoryRatio = (_totalMemoryMb > 0) ? ((_totalMemoryMb - freeMb) / _totalMemoryMb) * 100f : 0f;

                OnSample?.Invoke(new Metric(cpuValue, usedMemoryRatio));

                // 임계치 검사
                bool isCurrentlyOverloaded = (cpuValue > 75f) || (usedMemoryRatio > 80f);
                if (isCurrentlyOverloaded && !_isOverloaded)
                {
                    _isOverloaded = true;
                    OnThresholdExceeded?.Invoke();
                }
                else if (!isCurrentlyOverloaded && _isOverloaded)
                {
                    _isOverloaded = false;
                    OnBackToNormal?.Invoke();
                }
            }
            catch(Exception ex)
            {
                 Console.WriteLine($"[ERROR] PdhSampler - Failed to sample performance data: {ex.Message}");
            }
        }

        private static float GetTotalMemoryInMb()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (ulong.TryParse(obj["TotalVisibleMemorySize"]?.ToString(), out ulong totalKiloBytes))
                        {
                            return (float)totalKiloBytes / 1024f;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] PdhSampler - Failed to query total memory using WMI: {ex.Message}");
            }
            return 0f;
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _cpuCounter?.Dispose();
            _memFreeMbCounter?.Dispose();
        }
    }

    /// <summary>
    /// 고정된 크기를 가지는 순환 버퍼(Circular Buffer) 구현 클래스입니다.
    /// </summary>
    internal sealed class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _head;
        private int _count;
        public int Capacity { get; }
        public int Count => _count;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));
            Capacity = capacity;
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        public void Push(T item)
        {
            int tail = (_head + _count) % Capacity;
            _buffer[tail] = item;

            if (_count == Capacity)
            {
                _head = (_head + 1) % Capacity; // 가장 오래된 데이터 덮어쓰기
            }
            else
            {
                _count++;
            }
        }

        public T[] ToArray()
        {
            var array = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                array[i] = _buffer[(_head + i) % Capacity];
            }
            return array;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }

    #endregion
}
