using System;

namespace PcCam_x64.Models
{
    /// <summary>
    /// 인증 관련 설정.
    /// 
    /// 인증 처리 자체는 인증 DLL이 담당하며,
    /// PC CAM은 인증키와 장비명을 DLL에 전달하고 결과만 수신한다.
    /// </summary>
    public class AuthConfig
    {
        /// <summary>
        /// 사용자가 입력한 인증키.
        /// </summary>
        public string LicenseKey { get; set; } = "";

        /// <summary>
        /// 장비명.
        /// 기본값은 PC 이름을 사용할 수 있다.
        /// </summary>
        public string DeviceName { get; set; } = Environment.MachineName;

        /// <summary>
        /// 마지막 인증 결과 코드 또는 메시지.
        /// </summary>
        public string LastAuthResult { get; set; } = "";

        /// <summary>
        /// 마지막 인증 시각.
        /// </summary>
        public DateTime? LastAuthAt { get; set; }
    }
}