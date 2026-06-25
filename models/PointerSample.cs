using System;

namespace PcCam_x64.Models
{
    /// <summary>
    /// 전역 포인터 Down 이벤트 한 건의 좌표와 입력 종류.
    /// </summary>
    public sealed class PointerSample
    {
        /// <summary>
        /// Windows 가상 화면 기준 X 좌표.
        /// 다중 모니터 구성에서는 음수가 될 수 있다.
        /// </summary>
        public int ScreenX { get; set; }

        /// <summary>
        /// Windows 가상 화면 기준 Y 좌표.
        /// 다중 모니터 구성에서는 음수가 될 수 있다.
        /// </summary>
        public int ScreenY { get; set; }

        /// <summary>
        /// 입력이 감지된 로컬 시각.
        /// </summary>
        public DateTime OccurredAt { get; set; }

        /// <summary>
        /// Windows의 터치 승격 마우스 입력 서명이 확인되었는지 여부.
        /// </summary>
        public bool IsTouch { get; set; }

        /// <summary>
        /// 터치 드라이버 호환성 확인을 위한 원본 dwExtraInfo 하위 32비트 값.
        /// 1차 POC 진단 로그에만 사용한다.
        /// </summary>
        public uint ExtraInfo { get; set; }
    }
}