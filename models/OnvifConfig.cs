namespace PcCam_x64.Models
{
    /// <summary>
    /// ONVIF 관련 설정.
    /// 
    /// 1단계에서는 실제 ONVIF 서버를 구현하지 않고,
    /// 향후 ONVIF 단계에서 사용할 ID/PW 설정값만 저장한다.
    /// </summary>
    public class OnvifConfig
    {
        /// <summary>
        /// ONVIF 기능 사용 여부.
        /// 1단계에서는 기본 false로 두고 설정값만 관리한다.
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// ONVIF 인증 사용자명.
        /// </summary>
        public string UserId { get; set; } = "admin";

        /// <summary>
        /// ONVIF 인증 비밀번호.
        /// 실제 저장 시에는 추후 암호화 여부를 검토한다.
        /// </summary>
        public string Password { get; set; } = "";
    }
}