using System;
using NetMQ;
using NetMQ.Sockets;

namespace PcCam_x64.Services
{
    /// <summary>
    /// FFmpeg 측 구성 예:
    /// zmq=b='tcp\://127.0.0.1\:5555'
    /// overlay@touch=...
    ///
    /// 전송 명령 예:
    /// overlay@touch x 100
    /// overlay@touch y 200
    ///
    /// 주의:
    /// FFmpeg ZMQ 필터는 REP 서버로 동작하므로
    /// 클라이언트는 REQ 방식으로 명령을 전송한 뒤
    /// 반드시 응답을 수신해야 한다.
    /// </summary>
    public sealed class TouchZmqControlService
    {
        private readonly string _host;
        private readonly int _basePort;

        private const string TargetFilterName = "overlay@touch";

        /*
         * FFmpeg가 실행되지 않았거나 ZMQ 필터가 준비되지 않은 경우
         * 프로그램 전체가 멈추지 않도록 송수신 시간제한을 둔다.
         */
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(1000);

        /// <summary>
        /// 기본 호스트와 기준 포트를 사용한다.
        ///
        /// Stream0 → 5555
        /// Stream1 → 5556
        /// Stream2 → 5557
        /// </summary>
        public TouchZmqControlService()
            : this(
        TouchZmqEndpointPolicy.DefaultHost,
        TouchZmqEndpointPolicy.DefaultBasePort)
        {
        }

        /// <summary>
        /// ZMQ 호스트와 Stream0 기준 포트를 지정한다.
        /// </summary>
        public TouchZmqControlService(
            string host,
            int basePort)
        {
            /*
             * FFmpeg 명령 생성과 동일한 엔드포인트 정책으로
             * 호스트와 기준 포트를 검증한다.
             */
            _host =
                TouchZmqEndpointPolicy.NormalizeHost(
                    host);

            TouchZmqEndpointPolicy.ValidateBasePort(
                basePort);

            _basePort =
                basePort;
        }

        /// <summary>
        /// 클릭 중심 좌표를 기준으로 원형 포인터 이미지를 이동한다.
        ///
        /// overlay 필터의 x, y는 포인터 이미지의 좌측 상단 좌표이므로
        /// 포인터 크기의 절반을 빼서 클릭 지점이 원의 중심에 오도록 한다.
        /// </summary>
        public bool ShowPointer(
            int streamNo,
            int centerX,
            int centerY,
            int pointerSize,
            out string response,
            out string errorMessage)
        {
            response = "";
            errorMessage = "";

            if (streamNo < 0)
            {
                errorMessage =
                    "StreamNo는 0 이상이어야 합니다.";

                return false;
            }

            if (pointerSize <= 0)
            {
                errorMessage =
                    "포인터 크기는 1 이상이어야 합니다.";

                return false;
            }

            int halfSize =
                pointerSize / 2;

            int left =
                centerX - halfSize;

            int top =
                centerY - halfSize;

            return SetPointerPosition(
                streamNo,
                left,
                top,
                out response,
                out errorMessage);
        }

        /// <summary>
        /// 포인터를 영상 영역 밖으로 이동하여 숨긴다.
        /// </summary>
        public bool HidePointer(
            int streamNo,
            out string response,
            out string errorMessage)
        {
            if (streamNo < 0)
            {
                response = "";
                errorMessage =
                    "StreamNo는 0 이상이어야 합니다.";

                return false;
            }

            return SetPointerPosition(
                streamNo,
                -10000,
                -10000,
                out response,
                out errorMessage);
        }

        /// <summary>
        /// overlay 원형 포인터 이미지의 좌측 상단 좌표를 변경한다.
        ///
        /// 각 호출은 독립적인 RequestSocket을 생성하므로
        /// 서로 다른 Stream의 ZMQ 명령을 동시에 처리할 수 있다.
        ///
        /// 동일 Stream의 표시·숨김 순서는
        /// TouchZmqPointerManager의 Stream별 CommandSync가 보장한다.
        /// </summary>
        private bool SetPointerPosition(
            int streamNo,
            int left,
            int top,
            out string response,
            out string errorMessage)
        {
            response = "";
            errorMessage = "";

            string endpoint;

            try
            {
                endpoint =
                    TouchZmqEndpointPolicy.BuildClientEndpoint(
                        _host,
                        _basePort,
                        streamNo);
            }
            catch (Exception ex)
            {
                errorMessage =
                    ex.Message;

                return false;
            }

            try
            {
                /*
                 * 각 명령마다 별도의 RequestSocket을 생성한다.
                 *
                 * Stream0의 응답 지연이 Stream1 명령까지 막지 않도록
                 * 서비스 전체를 하나의 lock으로 묶지 않는다.
                 */
                using (RequestSocket socket =
                       new RequestSocket())
                {
                    socket.Options.Linger =
                        TimeSpan.Zero;

                    socket.Connect(
                        endpoint);

                    string xResponse;

                    if (!SendCommand(
                            socket,
                            TargetFilterName +
                            " x " +
                            left,
                            out xResponse,
                            out errorMessage))
                    {
                        errorMessage =
                            "StreamNo=" +
                            streamNo +
                            ", Endpoint=" +
                            endpoint +
                            ", " +
                            errorMessage;

                        return false;
                    }

                    string yResponse;

                    if (!SendCommand(
                            socket,
                            TargetFilterName +
                            " y " +
                            top,
                            out yResponse,
                            out errorMessage))
                    {
                        errorMessage =
                            "StreamNo=" +
                            streamNo +
                            ", Endpoint=" +
                            endpoint +
                            ", " +
                            errorMessage;

                        return false;
                    }

                    response =
                        "StreamNo=" +
                        streamNo +
                        ", Endpoint=" +
                        endpoint +
                        ", X=[" +
                        xResponse +
                        "], Y=[" +
                        yResponse +
                        "]";

                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage =
                    "ZMQ 포인터 명령 전송 중 오류가 발생했습니다. " +
                    "StreamNo=" +
                    streamNo +
                    ", Endpoint=" +
                    endpoint +
                    ", Error=" +
                    ex.Message;

                return false;
            }
        }

        /// <summary>
        /// 하나의 FFmpeg 필터 명령을 전송하고 응답을 수신한다.
        ///
        /// ZMQ REQ/REP 규칙:
        /// Send → Receive → Send → Receive 순서를 지켜야 한다.
        /// </summary>
        private bool SendCommand(
            RequestSocket socket,
            string command,
            out string response,
            out string errorMessage)
        {
            response = "";
            errorMessage = "";

            bool sent =
                socket.TrySendFrame(
                    CommandTimeout,
                    command);

            if (!sent)
            {
                errorMessage =
                    "ZMQ 명령 전송 시간이 초과되었습니다. " +
                    "Command=" + command;

                return false;
            }

            bool received =
                socket.TryReceiveFrameString(
                    CommandTimeout,
                    out response);

            if (!received)
            {
                errorMessage =
                    "FFmpeg ZMQ 응답 대기 시간이 초과되었습니다. " +
                    "Command=" + command;

                return false;
            }

            /*
             * FFmpeg ZMQ 성공 응답은 일반적으로
             * '0 Success' 형태로 시작한다.
             */
            if (!IsSuccessResponse(response))
            {
                errorMessage =
                    "FFmpeg가 ZMQ 명령을 거부했습니다. " +
                    "Command=" + command +
                    ", Response=" + response;

                return false;
            }

            return true;
        }

        /// <summary>
        /// FFmpeg ZMQ 응답 코드가 성공인지 확인한다.
        /// </summary>
        private bool IsSuccessResponse(
            string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            string value =
                response.Trim();

            return string.Equals(
                       value,
                       "0",
                       StringComparison.Ordinal) ||
                   value.StartsWith(
                       "0 ",
                       StringComparison.Ordinal) ||
                   value.StartsWith(
                       "0\n",
                       StringComparison.Ordinal) ||
                   value.StartsWith(
                       "0\r",
                       StringComparison.Ordinal);
        }
    }
}