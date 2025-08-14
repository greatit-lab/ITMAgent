// ITM_Agent.Common/Interfaces/ITimeSyncProvider.cs
using System;

namespace ITM_Agent.Common.Interfaces
{
    /// <summary>
    /// 시간 동기화 서비스를 위한 표준 인터페이스입니다.
    /// </summary>
    public interface ITimeSyncProvider
    {
        /// <summary>
        /// 주어진 시간을 서버 시간 기준으로 보정한 후, KST로 변환합니다.
        /// </summary>
        DateTime ToSynchronizedKst(DateTime agentLocalTime);
    }
}
