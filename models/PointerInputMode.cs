namespace PcCam_x64.Models
{
    /// <summary>
    /// 터치 포인터 입력 인식 방식.
    /// </summary>
    public enum PointerInputMode
    {
        /// <summary>
        /// Windows가 터치에서 변환한 입력만 처리한다.
        /// </summary>
        TouchOnly = 0,

        /// <summary>
        /// 터치 입력과 일반 마우스 클릭을 모두 처리한다.
        /// 구형 POS 터치 드라이버 호환용이다.
        /// </summary>
        TouchAndMouse = 1
    }
}