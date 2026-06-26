using System;

namespace PcCam_x64.Services
{
    /// <summary>
    /// 클릭·터치 포인터용 ZMQ 엔드포인트 정책.
    ///
    /// FFmpeg ZMQ 서버와 NetMQ 클라이언트가
    /// 반드시 같은 호스트와 포트 계산 규칙을 사용하도록 한다.
    ///
    /// 기본 포트 정책:
    /// Stream0 → 5555
    /// Stream1 → 5556
    /// Stream2 → 5557
    /// </summary>
    public static class TouchZmqEndpointPolicy
    {
        /// <summary>
        /// 로컬 FFmpeg ZMQ 서버의 기본 호스트.
        /// </summary>
        public const string DefaultHost =
            "127.0.0.1";

        /// <summary>
        /// Stream0에서 사용하는 기본 ZMQ 포트.
        /// </summary>
        public const int DefaultBasePort =
            5555;

        /// <summary>
        /// ZMQ 호스트 문자열을 검증하고 정규화한다.
        /// </summary>
        public static string NormalizeHost(
            string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException(
                    "ZMQ 호스트가 비어 있습니다.",
                    "host");
            }

            return host.Trim();
        }

        /// <summary>
        /// Stream0 기준 포트가 유효한지 확인한다.
        /// </summary>
        public static void ValidateBasePort(
            int basePort)
        {
            if (basePort <= 0 ||
                basePort > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    "basePort",
                    "ZMQ 기준 포트가 올바르지 않습니다.");
            }
        }

        /// <summary>
        /// 기본 기준 포트와 StreamNo를 사용하여
        /// 해당 Stream의 ZMQ 포트를 계산한다.
        /// </summary>
        public static int GetPort(
            int streamNo)
        {
            return GetPort(
                DefaultBasePort,
                streamNo);
        }

        /// <summary>
        /// 지정한 기준 포트와 StreamNo를 사용하여
        /// 해당 Stream의 ZMQ 포트를 계산한다.
        /// </summary>
        public static int GetPort(
            int basePort,
            int streamNo)
        {
            ValidateBasePort(
                basePort);

            if (streamNo < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "streamNo",
                    "StreamNo는 0 이상이어야 합니다.");
            }

            /*
             * int 덧셈 오버플로를 방지하기 위해
             * long으로 최종 포트를 계산한다.
             */
            long calculatedPort =
                (long)basePort +
                streamNo;

            if (calculatedPort > 65535)
            {
                throw new InvalidOperationException(
                    "StreamNo에 해당하는 ZMQ 포트가 유효 범위를 초과했습니다. " +
                    "StreamNo=" +
                    streamNo +
                    ", Port=" +
                    calculatedPort);
            }

            return (int)calculatedPort;
        }

        /// <summary>
        /// NetMQ RequestSocket에서 사용할
        /// 일반 ZMQ 엔드포인트를 생성한다.
        ///
        /// 예:
        /// tcp://127.0.0.1:5555
        /// </summary>
        public static string BuildClientEndpoint(
            string host,
            int basePort,
            int streamNo)
        {
            string normalizedHost =
                NormalizeHost(
                    host);

            int port =
                GetPort(
                    basePort,
                    streamNo);

            return
                "tcp://" +
                normalizedHost +
                ":" +
                port;
        }

        /// <summary>
        /// FFmpeg 필터 문자열에서 사용할
        /// 이스케이프된 ZMQ 바인딩 주소를 생성한다.
        ///
        /// 반환 예:
        /// tcp\://127.0.0.1\:5555
        /// </summary>
        public static string BuildFfmpegBindAddress(
            int streamNo)
        {
            return BuildFfmpegBindAddress(
                DefaultHost,
                DefaultBasePort,
                streamNo);
        }

        /// <summary>
        /// 지정한 호스트와 기준 포트를 사용하여
        /// FFmpeg용 ZMQ 바인딩 주소를 생성한다.
        /// </summary>
        public static string BuildFfmpegBindAddress(
            string host,
            int basePort,
            int streamNo)
        {
            string normalizedHost =
                NormalizeHost(
                    host);

            int port =
                GetPort(
                    basePort,
                    streamNo);

            /*
             * FFmpeg filter_complex 내부에서는
             * 프로토콜의 ':'와 포트 앞의 ':'를 이스케이프해야 한다.
             */
            return
                "tcp\\://" +
                normalizedHost +
                "\\:" +
                port;
        }
    }
}