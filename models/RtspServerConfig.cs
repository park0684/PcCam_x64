namespace PcCam_x64.Models
{
    /// <summary>
    /// RTSP 서버 설정.
    /// 
    /// 현재 1단계에서는 MediaMTX를 사용하지만,
    /// 향후 다른 RTSP 서버로 교체될 가능성을 고려하여
    /// 특정 제품명 대신 RtspServerConfig로 정의한다.
    /// </summary>
    public class RtspServerConfig
    {
        /// <summary>
        /// RTSP 포트.
        /// 기본값은 8554이다.
        /// </summary>
        public int RtspPort { get; set; } = 8554;

        /// <summary>
        /// RTMP 포트.
        /// 현재는 필수는 아니지만 MediaMTX 설정 파일 생성을 위해 보관한다.
        /// </summary>
        public int RtmpPort { get; set; } = 1935;

        /// <summary>
        /// MediaMTX 실행 파일명.
        /// </summary>
        public string ServerExeName { get; set; } = "mediamtx.exe";

        /// <summary>
        /// MediaMTX 설정 파일명.
        /// </summary>
       // public string ConfigFileName { get; set; } = "mediamtx.yml";
    }
}