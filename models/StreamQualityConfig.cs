namespace PcCam_x64.Models
{
    /// <summary>
    /// 하나의 송출 화면 안에서 사용할 품질별 스트림 설정.
    /// 
    /// Main Stream:
    /// - 녹화용 고화질 스트림
    /// 
    /// Sub Stream:
    /// - 실시간 보기용 저화질 스트림
    /// </summary>
    public class StreamQualityConfig
    {
        /// <summary>
        /// 해당 품질 스트림 사용 여부.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// RTSP 경로.
        /// 
        /// 예:
        /// Main: poscam
        /// Sub : poscam_sub
        /// </summary>
        public string RtspPath { get; set; }

        /// <summary>
        /// 송출 FPS.
        /// </summary>
        public int Fps { get; set; }

        /// <summary>
        /// 비트레이트.
        /// 
        /// 예:
        /// 2000k
        /// 500k
        /// </summary>
        public string Bitrate { get; set; }

        /// <summary>
        /// 출력 너비.
        /// 0이면 원본 또는 기존 기본값을 사용한다.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 출력 높이.
        /// 0이면 원본 또는 기존 기본값을 사용한다.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 기본 품질 설정을 생성한다.
        /// </summary>
        public StreamQualityConfig()
        {
            IsEnabled = true;
            RtspPath = "";
            Fps = 10;
            Bitrate = "2000k";
            Width = 0;
            Height = 0;
        }

        /// <summary>
        /// 녹화용 Main Stream 기본 설정을 생성한다.
        /// </summary>
        /// <param name="rtspPath">
        /// RTSP 경로.
        /// </param>
        /// <returns>
        /// Main Stream 기본 설정.
        /// </returns>
        public static StreamQualityConfig CreateMain(string rtspPath)
        {
            return new StreamQualityConfig
            {
                IsEnabled = true,
                RtspPath = rtspPath ?? "poscam",
                Fps = 10,
                Bitrate = "2000k",
                Width = 0,
                Height = 0
            };
        }

        /// <summary>
        /// 실시간 보기용 Sub Stream 기본 설정을 생성한다.
        /// </summary>
        /// <param name="rtspPath">
        /// RTSP 경로.
        /// </param>
        /// <returns>
        /// Sub Stream 기본 설정.
        /// </returns>
        public static StreamQualityConfig CreateSub(string rtspPath)
        {
            return new StreamQualityConfig
            {
                IsEnabled = true,
                RtspPath = rtspPath ?? "poscam_sub",
                Fps = 5,
                Bitrate = "500k",
                Width = 640,
                Height = 360
            };
        }
    }
}