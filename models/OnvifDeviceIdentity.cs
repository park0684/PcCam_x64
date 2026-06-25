using System;

namespace PcCam_x64.Models
{
    /// <summary>
    /// 하나의 PCCAM ONVIF 가상 장치를 식별하는 값 모음.
    ///
    /// PCCAM은 모니터별 스트림을 서로 다른 ONVIF 포트로 제공하므로,
    /// 각 StreamNo는 Milestone에서 별도 하드웨어로 등록될 수 있다.
    /// </summary>
    public sealed class OnvifDeviceIdentity
    {
        /// <summary>
        /// PCCAM 스트림 번호.
        /// </summary>
        public int StreamNo { get; set; }

        /// <summary>
        /// WS-Discovery EndpointReference에 사용할 고정 UUID.
        ///
        /// 프로그램을 재시작하더라도 같은 InstallationId와
        /// StreamNo에서는 항상 같은 UUID가 생성된다.
        /// </summary>
        public Guid EndpointUuid { get; set; }

        /// <summary>
        /// ONVIF GetDeviceInformation에서 반환할 시리얼 번호.
        /// </summary>
        public string SerialNumber { get; set; } = "";

        /// <summary>
        /// ONVIF GetDeviceInformation에서 반환할 하드웨어 ID.
        /// </summary>
        public string HardwareId { get; set; } = "";

        /// <summary>
        /// ONVIF GetNetworkInterfaces에서 반환할 가상 MAC 주소.
        ///
        /// 실제 물리 NIC 주소가 아니라 모니터별 가상 ONVIF 장치를
        /// 구분하기 위한 로컬 관리 유니캐스트 주소이다.
        /// </summary>
        public string MacAddress { get; set; } = "";

        /// <summary>
        /// ONVIF Scope와 로그에 사용할 장치 표시명.
        /// </summary>
        public string DeviceName { get; set; } = "";
    }
}