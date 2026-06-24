namespace PcCam_x64.Models
{
    /// <summary>
    /// 실행 시점에 조회한 실제 모니터 정보.
    /// 
    /// 이 정보는 설정 파일에 저장하지 않는다.
    /// Windows에서 현재 연결된 모니터 정보를 조회하여
    /// FFmpeg gdigrab 명령 생성에 사용한다.
    /// </summary>
    public class MonitorInfo
    {
        /// <summary>
        /// 모니터 순번.
        /// 조회된 순서 기준으로 부여한다.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Windows 모니터 장치명.
        /// 예: \\.\DISPLAY1
        /// </summary>
        public string DeviceName { get; set; }

        /// <summary>
        /// 주 모니터 여부.
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// 화면 표시용 모니터 역할명.
        /// 예: 주 모니터, 보조 모니터
        /// </summary>
        public string RoleName
        {
            get { return IsPrimary ? "주 모니터" : "보조 모니터"; }
        }

        /// <summary>
        /// 모니터 시작 X 좌표.
        /// FFmpeg -offset_x 값으로 사용한다.
        /// </summary>
        public int BoundsX { get; set; }

        /// <summary>
        /// 모니터 시작 Y 좌표.
        /// FFmpeg -offset_y 값으로 사용한다.
        /// </summary>
        public int BoundsY { get; set; }

        /// <summary>
        /// 모니터 너비.
        /// FFmpeg -video_size 값 생성에 사용한다.
        /// </summary>
        public int BoundsWidth { get; set; }

        /// <summary>
        /// 모니터 높이.
        /// FFmpeg -video_size 값 생성에 사용한다.
        /// </summary>
        public int BoundsHeight { get; set; }

        /// <summary>
        /// 해상도 표시 문자열.
        /// 예: 1920x1080
        /// </summary>
        public string Resolution
        {
            get { return BoundsWidth + "x" + BoundsHeight; }
        }

        /// <summary>
        /// 설정 화면 표시용 문자열.
        /// </summary>
        public string DisplayText
        {
            get
            {
                return RoleName + " - " + DeviceName + " - " + Resolution;
            }
        }
    }
}