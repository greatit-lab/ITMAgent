// ITM_Agent.Common/Interfaces/ISettingsManager.cs
using System.Collections.Generic;

namespace ITM_Agent.Common.Interfaces
{
    /// <summary>
    /// Settings.ini 파일의 모든 설정 값을 읽고 쓰는 작업을 위한 표준 인터페이스입니다.
    /// UI나 다른 서비스들은 이 인터페이스를 통해 설정에 접근하므로,
    /// 실제 파일 처리 방식이 변경되어도 다른 코드에 영향을 주지 않습니다.
    /// </summary>
    public interface ISettingsManager
    {
        #region --- Properties ---

        /// <summary>
        /// 디버그 모드 활성화 여부를 가져오거나 설정합니다.
        /// </summary>
        bool IsDebugMode { get; set; }

        /// <summary>
        /// 성능 로그 파일 기록 활성화 여부를 가져오거나 설정합니다.
        /// </summary>
        bool IsPerformanceLogging { get; set; }

        /// <summary>
        /// 오래된 파일 자동 삭제 기능 활성화 여부를 가져오거나 설정합니다.
        /// </summary>
        bool IsInfoDeletionEnabled { get; set; }

        /// <summary>
        /// 파일 보존 기간(일)을 가져오거나 설정합니다.
        /// </summary>
        int InfoRetentionDays { get; set; }

        #endregion

        #region --- General Methods ---

        /// <summary>
        /// 현재 설정된 Eqpid를 반환합니다.
        /// </summary>
        string GetEqpid();

        /// <summary>
        /// 새로운 Eqpid를 설정 파일에 저장합니다.
        /// </summary>
        void SetEqpid(string eqpid);

        /// <summary>
        /// 애플리케이션 타입을 (예: "ONTO", "NOVA") 반환합니다.
        /// </summary>
        string GetApplicationType(); // <<-- 메서드 이름 변경

        /// <summary>
        /// 애플리케이션 타입을 설정 파일에 저장합니다.
        /// </summary>
        void SetApplicationType(string type); // <<-- 메서드 이름 변경

        /// <summary>
        /// 특정 섹션에서 키에 해당하는 값을 읽어옵니다.
        /// </summary>
        string GetValueFromSection(string section, string key);

        /// <summary>
        /// 특정 섹션에 키와 값을 설정합니다. 섹션이나 키가 없으면 새로 생성합니다.
        /// </summary>
        void SetValueToSection(string section, string key, string value);

        /// <summary>
        /// 특정 섹션에서 해당 키를 삭제합니다.
        /// </summary>
        void RemoveKeyFromSection(string section, string key);

        /// <summary>
        /// 지정된 섹션 전체를 설정 파일에서 삭제합니다.
        /// </summary>
        void RemoveSection(string section);

        #endregion

        #region --- Folder & Regex Methods ---

        /// <summary>
        /// BaseFolder 경로를 반환합니다.
        /// </summary>
        string GetBaseFolder();

        /// <summary>
        /// BaseFolder 경로를 설정 파일에 저장합니다.
        /// </summary>
        void SetBaseFolder(string folderPath);

        /// <summary>
        /// 특정 섹션(예: [TargetFolders])에 속한 모든 폴더 경로 목록을 반환합니다.
        /// </summary>
        List<string> GetFoldersFromSection(string section);

        /// <summary>
        /// 특정 섹션에 폴더 경로 목록 전체를 덮어씁니다.
        /// </summary>
        void SetFoldersToSection(string section, List<string> folders);

        /// <summary>
        /// [Regex] 섹션의 모든 정규표현식과 대상 폴더 매핑 정보를 반환합니다.
        /// </summary>
        Dictionary<string, string> GetRegexList();

        /// <summary>
        /// [Regex] 섹션에 정규표현식 목록 전체를 덮어씁니다.
        /// </summary>
        void SetRegexList(Dictionary<string, string> regexDict);

        /// <summary>
        /// 특정 섹션의 모든 키-값 쌍을 Dictionary 형태로 반환합니다.
        /// </summary>
        /// <param name="sectionName">대괄호를 포함한 섹션 이름입니다. (예: "[RegPlugins]")</param>
        Dictionary<string, string> GetSectionAsDictionary(string sectionName);

        #endregion

        #region --- File Management Methods ---

        /// <summary>
        /// Eqpid를 제외한 모든 설정을 초기화합니다.
        /// </summary>
        void ResetExceptEqpid();

        /// <summary>
        /// 지정된 경로의 .ini 파일을 현재 설정으로 불러옵니다.
        /// </summary>
        void LoadFromFile(string filePath);

        /// <summary>
        /// 현재 설정을 지정된 경로의 .ini 파일로 저장합니다.
        /// </summary>
        void SaveToFile(string filePath);

        #endregion
    }
}
