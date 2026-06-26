namespace PcCam_x64.Models
{
    /// <summary>
    /// 녹화 및 실시간 송출 영상에 표시할
    /// 클릭·터치 포인터 설정.
    ///
    /// 실제 Windows 화면에는 포인터를 표시하지 않고,
    /// FFmpeg가 생성하는 Main/Sub 영상에만 적용한다.
    /// </summary>
    public sealed class TouchPointerConfig
    {
        /// <summary>
        /// 클릭·터치 포인터 표시 기능 사용 여부.
        ///
        /// false이면 기존 FFmpeg 송출 구조를 그대로 사용한다.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 영상에 표시할 원형 포인터의 지름.
        ///
        /// FFmpeg 내부에서 생성되는 원형 테두리 영상의
        /// 가로·세로 크기로 사용한다.
        /// 단위는 픽셀이다.
        /// </summary>
        public int Diameter { get; set; } = 160;

        /// <summary>
        /// 클릭 후 포인터를 유지하는 시간.
        /// 단위는 밀리초(ms)이다.
        /// </summary>
        public int VisibleMilliseconds { get; set; } = 700;

        /// <summary>
        /// 설정값을 안전한 범위로 보정한다.
        ///
        /// 설정 파일이 직접 수정되었거나
        /// 이전 버전의 잘못된 값이 남아 있더라도
        /// FFmpeg 명령 생성에 비정상 값이 전달되지 않도록 한다.
        /// </summary>
        public void Normalize()
        {
            /*
             * 지나치게 작은 포인터는 영상에서 확인하기 어렵고,
             * 지나치게 큰 포인터는 화면을 과도하게 가릴 수 있다.
             */
            if (Diameter < 24)
                Diameter = 24;

            if (Diameter > 500)
                Diameter = 500;

            /*
             * 표시 시간이 너무 짧으면 저프레임 영상에서
             * 포인터가 한 프레임도 보이지 않을 수 있다.
             */
            if (VisibleMilliseconds < 250)
                VisibleMilliseconds = 250;

            if (VisibleMilliseconds > 800)
                VisibleMilliseconds = 800;
        }
    }
}