// ITM_Agent.Common/Interfaces/ILogManager.cs
namespace ITM_Agent.Common.Interfaces
{
    /// <summary>
    /// 애플리케이션 전체에서 일관된 방식으로 로그를 기록하기 위한 표준 인터페이스입니다.
    /// 이 인터페이스를 통해 로깅 구현체가 변경되더라도(예: 파일, 데이터베이스, 클라우드 등)
    /// 로직 코드의 변경 없이 로깅 방식을 교체할 수 있습니다.
    /// </summary>
    public interface ILogManager
    {
        /// <summary>
        /// 일반적인 이벤트나 정보성 메시지를 기록합니다.
        /// (예: 서비스 시작/종료, 주요 작업 완료 등)
        /// </summary>
        /// <param name="message">기록할 로그 메시지입니다.</param>
        void LogEvent(string message);

        /// <summary>
        /// 오류나 예외 상황을 기록합니다.
        /// (예: 파일 접근 실패, DB 연결 오류, 예외 발생 등)
        /// </summary>
        /// <param name="message">기록할 오류 메시지입니다.</param>
        void LogError(string message);

        /// <summary>
        /// 디버그 모드가 활성화되었을 때만 상세한 개발 및 추적 정보를 기록합니다.
        /// (예: 메서드 진입/탈출, 변수 값 확인 등)
        /// </summary>
        /// <param name="message">기록할 디버그 메시지입니다.</param>
        void LogDebug(string message);
    }
}