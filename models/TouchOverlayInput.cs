namespace PcCam_x64.Models
{
    /// <summary>
    /// FFmpeg 명령 생성 시 전달하는 터치 오버레이 입력 정보.
    /// </summary>
    public sealed class TouchOverlayInput
    {
        /// <summary>
        /// FFmpeg에서 열 Named Pipe 전체 경로.
        /// 예: \\.\pipe\pccam_touch_0
        /// </summary>
        public string PipePath { get; set; } = "";

        /// <summary>
        /// 오버레이 rawvideo 가로 해상도.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 오버레이 rawvideo 세로 해상도.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 오버레이 프레임 공급 FPS.
        /// </summary>
        public int FrameRate { get; set; }
    }
}