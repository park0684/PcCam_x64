using System;
using System.Security.Cryptography;
using System.Text;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PCCAM InstallationId와 StreamNo를 기준으로
    /// 고정된 ONVIF 장치 식별정보를 생성한다.
    ///
    /// 생성되는 값:
    /// - Endpoint UUID
    /// - SerialNumber
    /// - HardwareId
    /// - 가상 MAC 주소
    ///
    /// 같은 InstallationId와 StreamNo가 입력되면
    /// 프로그램 재실행 후에도 항상 같은 결과를 반환한다.
    /// </summary>
    public class OnvifDeviceIdentityService
    {
        /// <summary>
        /// 지정한 스트림의 ONVIF 장치 식별정보를 생성한다.
        /// </summary>
        /// <param name="config">
        /// InstallationId가 포함된 PCCAM 전체 설정.
        /// </param>
        /// <param name="streamNo">
        /// 식별정보를 생성할 스트림 번호.
        /// </param>
        /// <returns>
        /// 스트림별 고정 ONVIF 장치 식별정보.
        /// </returns>
        public OnvifDeviceIdentity Create(
            AppConfig config,
            int streamNo)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (config.Onvif == null)
            {
                throw new InvalidOperationException(
                    "ONVIF 설정이 없습니다.");
            }

            if (streamNo < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "streamNo",
                    "StreamNo는 0 이상이어야 합니다.");
            }

            Guid installationGuid;

            if (!Guid.TryParse(
                    config.Onvif.InstallationId,
                    out installationGuid))
            {
                throw new InvalidOperationException(
                    "ONVIF InstallationId가 올바르지 않습니다.");
            }

            /*
             * InstallationId 표기 형식을 정규화한다.
             *
             * 예:
             * 4E77FB3903EE45D7B08F80B47D6A77D2
             */
            string installationId =
                installationGuid
                    .ToString("N")
                    .ToUpperInvariant();

            /*
             * SerialNumber와 HardwareId가 지나치게 길어지지 않도록
             * InstallationId 앞 12자리만 장치 표시값에 사용한다.
             *
             * 전체 InstallationId는 UUID와 MAC 생성에 사용하므로
             * 실제 장치 식별 충돌 가능성은 매우 낮다.
             */
            string shortInstallationId =
                installationId.Substring(0, 12);

            OnvifDeviceIdentity identity =
                new OnvifDeviceIdentity();

            identity.StreamNo = streamNo;

            identity.EndpointUuid =
                CreateEndpointUuid(
                    installationId,
                    streamNo);

            identity.SerialNumber =
                "PCCAM-" +
                shortInstallationId +
                "-S" +
                streamNo;

            identity.HardwareId =
                "PCCAM-X64-" +
                shortInstallationId +
                "-S" +
                streamNo;

            identity.MacAddress =
                CreateVirtualMacAddress(
                    installationId,
                    streamNo);

            identity.DeviceName =
                "PC_CAM_STREAM_" +
                streamNo;

            return identity;
        }

        /// <summary>
        /// InstallationId와 StreamNo를 기준으로
        /// 고정된 Endpoint UUID를 생성한다.
        /// </summary>
        private Guid CreateEndpointUuid(
            string installationId,
            int streamNo)
        {
            string source =
                "PCCAM|ONVIF|ENDPOINT|" +
                installationId +
                "|" +
                streamNo;

            byte[] hash =
                ComputeSha256(source);

            byte[] guidBytes =
                new byte[16];

            Buffer.BlockCopy(
                hash,
                0,
                guidBytes,
                0,
                guidBytes.Length);

            /*
             * 해시 앞 16바이트로 Guid를 생성한다.
             *
             * 매번 Guid.NewGuid()를 호출하는 방식과 달리,
             * 입력값이 같으면 항상 같은 UUID가 생성된다.
             */
            return new Guid(guidBytes);
        }

        /// <summary>
        /// 스트림별 고정 가상 MAC 주소를 생성한다.
        ///
        /// 첫 번째 바이트는 0x02로 고정한다.
        /// - Multicast bit: 0
        /// - Locally administered bit: 1
        ///
        /// 따라서 실제 제조사 NIC 주소와 충돌하지 않는
        /// 로컬 관리 유니캐스트 MAC 주소가 된다.
        /// </summary>
        private string CreateVirtualMacAddress(
            string installationId,
            int streamNo)
        {
            string source =
                "PCCAM|ONVIF|MAC|" +
                installationId +
                "|" +
                streamNo;

            byte[] hash =
                ComputeSha256(source);

            byte[] mac =
                new byte[6];

            mac[0] = 0x02;
            mac[1] = hash[0];
            mac[2] = hash[1];
            mac[3] = hash[2];
            mac[4] = hash[3];
            mac[5] = hash[4];

            return string.Format(
                "{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                mac[0],
                mac[1],
                mac[2],
                mac[3],
                mac[4],
                mac[5]);
        }

        /// <summary>
        /// UTF-8 문자열의 SHA-256 해시를 계산한다.
        /// </summary>
        private byte[] ComputeSha256(
            string value)
        {
            byte[] inputBytes =
                Encoding.UTF8.GetBytes(
                    value ?? "");

            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(
                    inputBytes);
            }
        }
    }
}