namespace PcCam_x64.Models
{
    /// <summary>
    /// PC CAM 송출 실행 상태.
    /// 
    /// 트레이 아이콘, 설정 화면, 로그 표시에서 공통으로 사용한다.
    /// </summary>
    public enum StreamRuntimeStatus
    {
        /// <summary>
        /// 인증되지 않은 상태.
        /// </summary>
        Unauthorized = 0,

        /// <summary>
        /// 송출 중지 상태.
        /// </summary>
        Stopped = 1,

        /// <summary>
        /// 송출 시작 처리 중.
        /// </summary>
        Starting = 2,

        /// <summary>
        /// 송출 중.
        /// </summary>
        Running = 3,

        /// <summary>
        /// 송출 중지 처리 중.
        /// </summary>
        Stopping = 4,

        /// <summary>
        /// 오류 발생 상태.
        /// </summary>
        Error = 5
    }
}