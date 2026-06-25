namespace PcCam_x64.Models
{
    /// <summary>
    /// ONVIF 관련 설정.
    ///
    /// ONVIF 계정 정보뿐 아니라 PCCAM 설치 단위의
    /// 영구 식별자를 함께 관리한다.
    ///
    /// InstallationId는 최초 실행 시 한 번 생성하고,
    /// 프로그램을 다시 실행해도 동일한 값을 유지해야 한다.
    /// 이후 ONVIF Endpoint UUID, SerialNumber, HardwareId,
    /// 가상 MAC 주소를 생성하는 기준값으로 사용한다.
    /// </summary>
    public class OnvifConfig
    {
        /// <summary>
        /// ONVIF 기능 사용 여부.
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// ONVIF 인증 사용자명.
        ///
        /// 현재는 MediaMTX RTSP 읽기 인증에도 같은 계정을 사용한다.
        /// </summary>
        public string UserId { get; set; } = "admin";

        /// <summary>
        /// ONVIF 인증 비밀번호.
        ///
        /// ConfigService에서 저장할 때 암호화하고,
        /// 로드할 때 복호화한다.
        /// </summary>
        public string Password { get; set; } = "";

        /// <summary>
        /// PCCAM 설치 단위의 영구 식별자.
        ///
        /// 저장 형식:
        /// - 하이픈이 없는 32자리 GUID
        /// - 영문 대문자
        ///
        /// 예:
        /// 4E77FB3903EE45D7B08F80B47D6A77D2
        ///
        /// 최초 실행 시 ConfigService에서 생성한다.
        /// 설정 파일이 유지되는 동안에는 절대 변경하지 않는다.
        /// </summary>
        public string InstallationId { get; set; } = "";
    }
}