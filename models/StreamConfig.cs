namespace PcCam_x64.Models
{
    /// <summary>
    /// 하나의 화면 스트림 설정 정보.
    /// 
    /// 설정 파일에는 사용자가 직접 관리해야 하는 값만 저장한다.
    /// 모니터 실제 좌표, 해상도, DISPLAY 이름 등은 저장하지 않고
    /// 프로그램 실행 시 Windows 모니터 정보를 조회하여 사용한다.
    /// </summary>
    public class StreamConfig
    {
        /// <summary>
        /// 스트림 사용 여부.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 스트림 번호.
        /// 0 = 주 모니터용 스트림
        /// 1 = 보조 모니터용 스트림
        /// </summary>
        public int StreamNo { get; set; } = 0;

        /// <summary>
        /// 캡처 대상 모니터 역할.
        /// 
        /// Primary   = 주 모니터
        /// Secondary = 보조 모니터
        /// 
        /// 실제 모니터 좌표와 해상도는 이 값을 기준으로
        /// MonitorService에서 실행 시점에 조회한다.
        /// </summary>
        public string MonitorRole { get; set; } = "Primary";

        /// <summary>
        /// 사용자가 직접 입력하는 화면명.
        /// 
        /// 이 값은 단순 모니터 이름이 아니라,
        /// 인증서버 또는 관리 화면에 기록될 수 있는 화면 식별명이다.
        /// 예: POS_MAIN, POS_SUB, 계산대화면, 보조화면
        /// </summary>
        public string ScreenName { get; set; } = "POS_MAIN";

        /// <summary>
        /// 향후 ONVIF 서버에서 사용할 포트.
        /// 1단계에서는 실제 ONVIF 서버 구현 없이 설정값만 저장한다.
        /// </summary>
        public int OnvifPort { get; set; } = 8080;

        /// <summary>
        /// 송출 FPS.
        /// 저사양 기본값은 5fps이다.
        /// </summary>
        public int Fps { get; set; } = 5;

        /// <summary>
        /// 송출 비트레이트.
        /// 저사양 기본값은 1200k이다.
        /// </summary>
        public string Bitrate { get; set; } = "1200k";

        /// <summary>
        /// 영상 코덱.
        /// 1단계 기본값은 H264이다.
        /// H265는 고급 옵션 또는 후속 검토 기능으로 둔다.
        /// </summary>
        public string Codec { get; set; } = "H264";

        /// <summary>
        /// MediaMTX에서 사용할 RTSP 경로.
        /// 예:
        /// Stream0 = poscam
        /// Stream1 = poscam_1
        /// </summary>
        public string RtspPath { get; set; } = "poscam";

        /// <summary>
        /// 녹화용 고화질 Main Stream 설정.
        /// </summary>
        public StreamQualityConfig MainStream { get; set; }

        /// <summary>
        /// 실시간 보기용 저화질 Sub Stream 설정.
        /// </summary>
        public StreamQualityConfig SubStream { get; set; }

        /// <summary>
        /// 사용자가 설정 화면에서 구분하기 쉽게 입력하는 표시명.
        /// 
        /// 실제 모니터 매칭에는 사용하지 않는다.
        /// 모니터 매칭은 ScreenName(예: \\.\DISPLAY1)을 기준으로 처리한다.
        /// </summary>
        public string DisplayName { get; set; }

        public StreamConfig()
        {
            StreamNo = 0;
            IsEnabled = true;
            MonitorRole = "";
            ScreenName = "";
            DisplayName = "";
            RtspPath = "poscam";
            OnvifPort = 8080;

            MainStream = StreamQualityConfig.CreateMain("poscam");
            SubStream = StreamQualityConfig.CreateSub("poscam_sub");
        }
    }
}