// ITM_Agent.Common/Interfaces/IPlugin.cs
using ITM_Agent.Common.Interfaces;

namespace ITM_Agent.Common.Interfaces
{
    /// <summary>
    /// ITM Agent가 동적으로 로드하고 실행할 모든 외부 플러그인이 구현해야 하는 표준 인터페이스입니다.
    /// 이 계약을 통해 메인 애플리케이션은 플러그인의 구체적인 구현을 몰라도
    /// 일관된 방식으로 플러그인을 초기화하고 실행할 수 있습니다.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>
        /// 플러그인의 고유한 이름을 반환합니다. 이 이름은 UI에 표시되고 설정을 관리하는 데 사용됩니다.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 플러그인의 버전을 반환합니다.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 플러그인이 처음 로드될 때 메인 애플리케이션에 의해 호출됩니다.
        /// 로깅, 설정 관리 등 필요한 서비스를 주입받아 플러그인 내부에서 사용할 수 있도록 준비합니다.
        /// </summary>
        /// <param name="settings">설정 값에 접근할 수 있는 ISettingsManager 서비스입니다.</param>
        /// <param name="logger">로그를 기록할 수 있는 ILogManager 서비스입니다.</param>
        void Initialize(ISettingsManager settings, ILogManager logger);

        /// <summary>
        /// 플러그인의 핵심 로직을 실행합니다.
        /// 파일 경로 등 작업에 필요한 데이터를 인자로 전달받아 처리합니다.
        /// </summary>
        /// <param name="filePath">플러그인이 처리해야 할 대상 파일의 전체 경로입니다.</param>
        void Execute(string filePath);

        /// <summary>
        /// 메인 애플리케이션의 디버그 모드 상태가 변경될 때 호출되어,
        /// 플러그인의 내부 로깅 상세 수준을 동기화합니다.
        /// </summary>
        /// <param name="isEnabled">디버그 모드 활성화 여부입니다.</param>
        void SetDebugMode(bool isEnabled);
    }
}