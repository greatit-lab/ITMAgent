// ITM_Agent.Common/ConnectInfo.cs
using Npgsql;
using System;

namespace ITM_Agent.Common
{
    /// <summary>
    /// 데이터베이스 연결 정보를 생성하고 관리하는 유틸리티 클래스입니다.
    /// 솔루션 내 모든 프로젝트에서 일관된 DB 연결 문자열을 얻기 위해 사용됩니다.
    /// </summary>
    public sealed class DatabaseInfo
    {
        // DB 연결 정보는 보안을 위해 별도 설정 파일로 관리하는 것이 이상적이나,
        // 원본 구조를 유지하여 상수로 정의합니다.
        private const string _server = "00.000.00.00";
        private const string _database = "itm";
        private const string _userId = "userid";
        private const string _password = "pw";
        private const int _port = 5432; // PostgreSQL 기본 포트

        // 외부에서 인스턴스 생성을 막기 위한 private 생성자
        private DatabaseInfo() { }

        /// <summary>
        /// 기본 설정으로 DatabaseInfo의 새 인스턴스를 생성합니다.
        /// </summary>
        public static DatabaseInfo CreateDefault() => new DatabaseInfo();

        /// <summary>
        /// PostgreSQL에 맞는 연결 문자열을 생성하여 반환합니다.
        /// </summary>
        /// <returns>Npgsql 라이브러리에서 사용할 수 있는 DB 연결 문자열입니다.</returns>
        public string GetConnectionString()
        {
            // NpgsqlConnectionStringBuilder를 사용하여 프로그래밍 방식으로 안전하게 연결 문자열 구성
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = _server,
                Database = _database,
                Username = _userId,
                Password = _password,
                Port = _port,
                Encoding = "UTF8",           // UTF-8 인코딩 지정
                SslMode = SslMode.Disable,   // SSL 모드 (필요시 Enable로 변경)
                SearchPath = "public"        // 기본 검색 스키마를 'public'으로 지정
            };
            return csb.ConnectionString;
        }

        /// <summary>
        /// 데이터베이스 연결을 테스트하고 콘솔에 결과를 출력합니다. (개발 및 진단용)
        /// </summary>
        public void TestConnection()
        {
            Console.WriteLine($"[DB] Connection Attempt ▶ {GetConnectionString()}");
            try
            {
                using (var conn = new NpgsqlConnection(GetConnectionString()))
                {
                    conn.Open();
                    Console.WriteLine($"[DB] Connection Succeeded ▶ PostgreSQL Version: {conn.PostgreSqlVersion}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Connection Failed ▶ {ex.Message}");
            }
        }
    }
}