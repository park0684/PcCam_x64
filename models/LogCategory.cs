namespace PcCam_x64.Models
{
    /// <summary>
    /// PC CAM 로그 분류.
    /// 
    /// 로그 파일명을 분리하기 위해 사용한다.
    /// 예:
    /// app_20260528.log
    /// stream_20260528.log
    /// ffmpeg_20260528.log
    /// mediamtx_20260528.log
    /// auth_20260528.log
    /// </summary>
    public enum LogCategory
    {
        /// <summary>
        /// 프로그램 시작, 종료, 설정 변경 등 일반 로그.
        /// </summary>
        App = 0,

        /// <summary>
        /// 송출 시작, 중지, 상태 변경 로그.
        /// </summary>
        Stream = 1,

        /// <summary>
        /// FFmpeg 실행 및 출력 로그.
        /// </summary>
        Ffmpeg = 2,

        /// <summary>
        /// MediaMTX 실행 및 출력 로그.
        /// </summary>
        MediaMtx = 3,

        /// <summary>
        /// 인증 관련 로그.
        /// </summary>
        Auth = 4,

        /// <summary>
        /// 오류 로그.
        /// </summary>
        Error = 5
    }
}