using System.Collections.Generic;

namespace PcCam_x64.Models
{
    /// <summary>
    /// PC CAM 전체 설정 모델.
    /// 
    /// INI 파일의 설정값을 프로그램 내부에서 다루기 위한 루트 설정 객체이다.
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 스트림 설정 목록.
        /// 
        /// 기본 설정에서는 0번 주 모니터 스트림만 생성한다.
        /// 보조 모니터 스트림은 실제 보조 모니터가 있거나,
        /// 사용자가 설정 화면에서 추가할 때 생성한다.
        /// </summary>
        public List<StreamConfig> Streams { get; set; } = new List<StreamConfig>();

        /// <summary>
        /// ONVIF 설정.
        /// </summary>
        public OnvifConfig Onvif { get; set; } = new OnvifConfig();

        /// <summary>
        /// 인증 설정.
        /// </summary>
        public AuthConfig Auth { get; set; } = new AuthConfig();

        /// <summary>
        /// 운영 설정.
        /// </summary>
        public OperationConfig Operation { get; set; } = new OperationConfig();

        /// <summary>
        /// 녹화 및 실시간 송출 영상에 표시할
        /// 클릭·터치 포인터 설정.
        /// </summary>
        public TouchPointerConfig TouchPointer { get; set; } = new TouchPointerConfig();

        /// <summary>
        /// RTSP 서버 설정.
        /// </summary>
        public RtspServerConfig RtspServer { get; set; } = new RtspServerConfig();

        /// <summary>
        /// 기본 설정 객체를 생성한다.
        /// 
        /// 기본값은 주 모니터용 Stream0만 생성한다.
        /// Stream1은 보조 모니터가 실제로 필요할 때 별도로 추가한다.
        /// </summary>
        public static AppConfig CreateDefault()
        {
            AppConfig config =
                new AppConfig();

            /*
             * 신규 설치 기본값에서는 터치 포인터를 비활성화한다.
             * 사용자가 설정 화면에서 명시적으로 활성화한 경우에만
             * FFmpeg ZMQ 필터를 적용하도록 한다.
             */
            config.TouchPointer =
                new TouchPointerConfig
                {
                    Enabled = false,
                    Diameter = 160,
                    VisibleMilliseconds = 700
                };

            config.Streams.Add(
                CreateStreamByIndex(
                    0,
                    "Primary",
                    @"\\.\DISPLAY1"));

            return config;
        }

        /// <summary>
        /// 현재 연결된 모니터 목록에 맞춰 스트림 설정을 보정한다.
        /// 
        /// Stream0은 Windows 주 모니터를 우선 사용한다.
        /// 기존 StreamConfig는 삭제하지 않고, 부족한 StreamConfig만 추가한다.
        /// 단, ScreenName이 DISPLAY 장치명인 경우 현재 감지된 장치명으로 갱신한다.
        /// </summary>
        public static void EnsureStreamsForMonitorNames(
            AppConfig config,
            IList<string> screenNames)
        {
            if (config == null)
                return;

            if (config.Streams == null)
                config.Streams = new List<StreamConfig>();

            if (screenNames == null || screenNames.Count == 0)
            {
                screenNames = new List<string>();
                screenNames.Add(@"\\.\DISPLAY1");
            }

            for (int i = config.Streams.Count; i < screenNames.Count; i++)
            {
                string role = i == 0
                    ? "Primary"
                    : "Monitor" + i;

                config.Streams.Add(
                    CreateStreamByIndex(
                        i,
                        role,
                        screenNames[i]));
            }

            for (int i = 0; i < config.Streams.Count && i < screenNames.Count; i++)
            {
                StreamConfig stream = config.Streams[i];

                if (stream == null)
                    continue;

                stream.StreamNo = i;

                if (i == 0)
                    stream.MonitorRole = "Primary";
                else
                    stream.MonitorRole = "Monitor" + i;

                /*
                 * 현재 단계에서는 ScreenName을 실제 Windows 모니터 장치명으로 사용한다.
                 * 따라서 기존 값이 있더라도 현재 감지된 모니터 순서에 맞춰 갱신한다.
                 */
                stream.ScreenName = screenNames[i];
            }
        }

        /// <summary>
        /// 모니터 순번 기준으로 기본 스트림 설정을 생성한다.
        /// </summary>
        /// <param name="index">
        /// 스트림 번호.
        /// </param>
        /// <param name="monitorRole">
        /// 모니터 역할명.
        /// </param>
        /// <param name="screenName">
        /// 실제 Windows 모니터 장치명.
        /// 예: \\.\DISPLAY1
        /// </param>
        /// <returns>
        /// 기본 스트림 설정.
        /// </returns>
        private static StreamConfig CreateStreamByIndex(
            int index,
            string monitorRole,
            string screenName)
        {
            string rtspPath = index == 0
                ? "poscam"
                : "poscam_" + index;

            return new StreamConfig
            {
                IsEnabled = index == 0,
                StreamNo = index,
                MonitorRole = monitorRole,
                ScreenName = screenName ?? "",
                OnvifPort = 8080 + index,
                Fps = 5,
                Bitrate = "1200k",
                Codec = "H264",
                RtspPath = rtspPath,

                MainStream = StreamQualityConfig.CreateMain(rtspPath),
                SubStream = StreamQualityConfig.CreateSub(rtspPath + "_sub")
            };
        }
    }
}