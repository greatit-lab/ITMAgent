// ITM_Agent.Common/DTOs/PluginListItem.cs
namespace ITM_Agent.Common.DTOs
{
    /// <summary>
    /// UI에 표시되거나 내부적으로 전달되는 플러그인의 메타데이터를 담는 데이터 전송 객체입니다.
    /// </summary>
    public class PluginListItem
    {
        /// <summary>
        /// 플러그인의 고유 이름입니다.
        /// </summary>
        public string PluginName { get; set; }

        /// <summary>
        /// 플러그인 DLL 파일의 전체 경로입니다.
        /// </summary>
        public string AssemblyPath { get; set; }

        /// <summary>
        /// 플러그인의 어셈블리 버전 정보입니다.
        /// </summary>
        public string PluginVersion { get; set; }

        /// <summary>
        /// UI의 리스트박스 등에 표시될 때 사용되는 기본 출력 형식입니다.
        /// 버전 정보가 있는 경우 이름과 함께 표시합니다.
        /// </summary>
        /// <returns>"PluginName (v1.0.0.0)" 형식의 문자열을 반환합니다.</returns>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(PluginVersion))
            {
                return PluginName;
            }
            return string.Format("{0} (v{1})", PluginName, PluginVersion);
        }
    }
}