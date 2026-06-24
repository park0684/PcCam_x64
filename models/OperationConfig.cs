namespace PcCam_x64.Models
{
    /// <summary>
    /// 운영 관련 설정.
    /// </summary>
    public class OperationConfig
    {
        /// <summary>
        /// 상세 로그 기록 여부.
        /// 장애 분석 시 FFmpeg 명령, MediaMTX 로그 등을 상세히 기록한다.
        /// </summary>
        public bool EnableDetailLog { get; set; } = false;

        /// <summary>
        /// Windows 시작 시 자동실행 여부.
        /// 1단계에서는 설정값만 저장하고, 실제 등록은 후속 단계에서 구현한다.
        /// </summary>
        public bool AutoStart { get; set; } = false;

        /// <summary>
        /// 프로그램 실행 중 시스템 절전 방지 여부.
        /// 1단계에서 SetThreadExecutionState 방식으로 적용한다.
        /// </summary>
        public bool PreventSleep { get; set; } = true;

        /// <summary>
        /// 프로그램 시작 시 자동 송출 여부.
        /// </summary>
        public bool AutoStartStreaming { get; set; } = false;
    }
}