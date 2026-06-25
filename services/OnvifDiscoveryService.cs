using PcCam_x64.Models;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml;

namespace PcCam_x64.Services
{
    /// <summary>
    /// ONVIF WS-Discovery 서비스.
    /// 
    /// NVR이 네트워크에서 ONVIF 장치를 검색할 때 UDP 3702 포트로 Probe 요청을 보낸다.
    /// 이 서비스는 해당 Probe 요청을 수신하고, PC CAM의 ONVIF Device Service 주소를 ProbeMatch로 응답한다.
    /// </summary>
    public class OnvifDiscoveryService : IDisposable
    {
        private const int DiscoveryPort = 3702;
        private const string DiscoveryMulticastAddress = "239.255.255.250";

        private readonly object _syncLock = new object();
        private readonly LogService _logService;

        private UdpClient _udpClient;
        private Thread _receiveThread;
        private AppConfig _currentConfig;

        private bool _isRunning;
        private bool _disposed;
        private readonly OnvifDeviceIdentityService _deviceIdentityService;

        /// <summary>
        /// ONVIF WS-Discovery 서비스를 생성한다.
        /// </summary>
        /// <param name="logService">
        /// 로그 기록 서비스.
        /// </param>
        /// <param name="deviceIdentityService">
        /// 스트림별 고정 ONVIF 장치 식별정보 생성 서비스.
        /// </param>
        public OnvifDiscoveryService(
            LogService logService,
            OnvifDeviceIdentityService deviceIdentityService)
        {
            if (logService == null)
                throw new ArgumentNullException("logService");

            if (deviceIdentityService == null)
            {
                throw new ArgumentNullException(
                    "deviceIdentityService");
            }

            _logService = logService;

            _deviceIdentityService = deviceIdentityService;
        }

        /// <summary>
        /// WS-Discovery 서비스 실행 여부를 반환한다.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    return _isRunning;
                }
            }
        }

        /// <summary>
        /// WS-Discovery 수신 서비스를 시작한다.
        /// 
        /// UDP 3702 포트에 바인딩하고 ONVIF Discovery 멀티캐스트 주소에 가입한다.
        /// NVR의 Probe 요청을 수신하면 ProbeMatch 응답을 보낸다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        public void Start(AppConfig config)
        {
            if (_disposed)
                throw new ObjectDisposedException("OnvifDiscoveryService");

            if (config == null)
                throw new ArgumentNullException("config");

            lock (_syncLock)
            {
                if (_isRunning)
                    return;

                _currentConfig = config;

                _udpClient = new UdpClient();

                /*
                 * Windows 환경에서 UDP 3702 포트 재사용 가능성을 높이기 위해 ReuseAddress를 설정한다.
                 * 단, 다른 ONVIF 장치 에뮬레이터나 프로그램이 이미 3702를 점유 중이면 시작에 실패할 수 있다.
                 */
                _udpClient.Client.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                _udpClient.Client.Bind(
                    new IPEndPoint(IPAddress.Any, DiscoveryPort));

                _udpClient.JoinMulticastGroup(
                    IPAddress.Parse(DiscoveryMulticastAddress));

                _isRunning = true;

                _receiveThread = new Thread(ReceiveLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Name = "PC CAM ONVIF Discovery Listener";
                _receiveThread.Start();

                _logService.WriteApp("ONVIF Discovery 서비스 시작. UDP Port=" + DiscoveryPort);
            }
        }

        /// <summary>
        /// WS-Discovery 서비스를 중지한다.
        /// 
        /// UdpClient.Close를 호출하면 Receive 대기 중인 스레드에서 예외가 발생할 수 있다.
        /// 이 예외는 정상적인 중지 과정으로 처리한다.
        /// </summary>
        public void Stop()
        {
            lock (_syncLock)
            {
                if (!_isRunning)
                    return;

                _isRunning = false;

                try
                {
                    if (_udpClient != null)
                    {
                        try
                        {
                            _udpClient.DropMulticastGroup(
                                IPAddress.Parse(DiscoveryMulticastAddress));
                        }
                        catch
                        {
                        }

                        _udpClient.Close();
                    }
                }
                catch
                {
                }

                _udpClient = null;

                _logService.WriteApp("ONVIF Discovery 서비스 중지 요청");
            }

            try
            {
                if (_receiveThread != null && _receiveThread.IsAlive)
                    _receiveThread.Join(1000);
            }
            catch
            {
            }

            _receiveThread = null;
        }

        /// <summary>
        /// UDP 3702 Probe 요청을 반복 수신하는 루프.
        /// 
        /// 수신된 메시지가 ONVIF Probe 요청이면 ProbeMatch 응답을 생성하여 요청자에게 전송한다.
        /// </summary>
        private void ReceiveLoop()
        {
            while (IsRunning)
            {
                try
                {
                    UdpClient client = _udpClient;

                    if (client == null)
                        return;

                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = client.Receive(ref remoteEndPoint);

                    if (receivedBytes == null || receivedBytes.Length == 0)
                        continue;

                    string requestXml = Encoding.UTF8.GetString(receivedBytes);

                    if (!IsProbeRequest(requestXml))
                        continue;

                    string localHost = ResolveLocalAddressForRemote(remoteEndPoint);
                    int streamNo = 0;
                    int onvifPort = ResolveOnvifPort(_currentConfig);
                    string responseXml = BuildProbeMatchResponse(
                        requestXml,
                        _currentConfig,
                        streamNo,
                        localHost,
                        onvifPort);

                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseXml);

                    client.Send(
                        responseBytes,
                        responseBytes.Length,
                        remoteEndPoint);

                    _logService.WriteApp(
                        "ONVIF Discovery ProbeMatch 응답 완료. Remote=" +
                        remoteEndPoint.Address +
                        ", XAddr=http://" +
                        localHost +
                        ":" +
                        onvifPort +
                        "/onvif/device_service");
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    if (IsRunning)
                        _logService.WriteError("ONVIF Discovery 소켓 오류 발생");
                }
                catch (Exception ex)
                {
                    if (IsRunning)
                        _logService.WriteException("ONVIF Discovery 요청 처리 오류", ex);
                }
            }
        }

        /// <summary>
        /// 수신된 XML이 WS-Discovery Probe 요청인지 확인한다.
        /// 
        /// NVR마다 namespace prefix가 다를 수 있으므로,
        /// 현재 단계에서는 Probe 문자열 포함 여부로 판단한다.
        /// ProbeMatches 응답은 요청으로 처리하지 않는다.
        /// </summary>
        /// <param name="requestXml">수신된 UDP XML 메시지.</param>
        /// <returns>true이면 Probe 요청.</returns>
        private bool IsProbeRequest(string requestXml)
        {
            if (string.IsNullOrWhiteSpace(requestXml))
                return false;

            if (requestXml.IndexOf("ProbeMatches", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return requestXml.IndexOf("Probe", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// NVR이 접근 가능한 현재 PC의 로컬 IP 주소를 추정한다.
        /// 
        /// UDP 소켓을 원격 주소로 Connect한 뒤 LocalEndPoint를 확인하면,
        /// 해당 원격 주소로 통신할 때 사용될 로컬 IP를 얻을 수 있다.
        /// </summary>
        /// <param name="remoteEndPoint">Probe 요청을 보낸 NVR 주소.</param>
        /// <returns>NVR에서 접근 가능한 PC CAM 로컬 IP 주소.</returns>
        private string ResolveLocalAddressForRemote(IPEndPoint remoteEndPoint)
        {
            Socket socket = null;

            try
            {
                socket = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Dgram,
                    ProtocolType.Udp);

                socket.Connect(remoteEndPoint.Address, remoteEndPoint.Port);

                IPEndPoint localEndPoint = socket.LocalEndPoint as IPEndPoint;

                if (localEndPoint != null && localEndPoint.Address != null)
                    return localEndPoint.Address.ToString();
            }
            catch
            {
            }
            finally
            {
                if (socket != null)
                {
                    try
                    {
                        socket.Close();
                    }
                    catch
                    {
                    }
                }
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// 설정에서 ONVIF HTTP 포트를 결정한다.
        /// 
        /// 현재는 Stream0의 OnvifPort를 사용한다.
        /// 값이 없거나 잘못된 경우 8080을 기본값으로 사용한다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <returns>ONVIF HTTP 포트.</returns>
        private int ResolveOnvifPort(AppConfig config)
        {
            if (config != null && config.Streams != null)
            {
                foreach (StreamConfig stream in config.Streams)
                {
                    if (stream == null)
                        continue;

                    if (stream.StreamNo == 0 &&
                        stream.OnvifPort > 0 &&
                        stream.OnvifPort <= 65535)
                    {
                        return stream.OnvifPort;
                    }
                }
            }

            return 8080;
        }

        /// <summary>
        /// WS-Discovery ProbeMatch 응답 XML을 생성한다.
        /// 
        /// NVR은 이 응답의 XAddrs 값을 보고 PC CAM의 ONVIF Device Service 주소로 접근한다.
        /// </summary>
        /// <param name="requestXml">NVR이 보낸 Probe 요청 XML.</param>
        /// <param name="host">NVR이 접근 가능한 PC CAM IP 주소.</param>
        /// <param name="onvifPort">ONVIF HTTP 포트.</param>
        /// <returns>ProbeMatch 응답 XML.</returns>
        private string BuildProbeMatchResponse(
            string requestXml,
            AppConfig config,
            int streamNo,
            string host,
            int onvifPort)
        {
            string relatesTo = ExtractMessageId(requestXml);

            if (string.IsNullOrWhiteSpace(relatesTo))
                relatesTo = "uuid:" + Guid.NewGuid().ToString();
            OnvifDeviceIdentity identity = _deviceIdentityService.Create(config, streamNo);
            string messageId = "uuid:" + Guid.NewGuid().ToString();
            string endpointAddress = "urn:uuid:" + identity.EndpointUuid.ToString();
            string xaddr = "http://" + host + ":" + onvifPort + "/onvif/device_service";

            StringBuilder sb = new StringBuilder();

            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<s:Envelope ");
            sb.Append("xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" ");
            sb.Append("xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" ");
            sb.Append("xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" ");
            sb.Append("xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\" ");
            sb.Append("xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\">");

            sb.Append("<s:Header>");
            sb.Append("<a:MessageID>" + XmlEscape(messageId) + "</a:MessageID>");
            sb.Append("<a:RelatesTo>" + XmlEscape(relatesTo) + "</a:RelatesTo>");
            sb.Append("<a:To>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</a:To>");
            sb.Append("<a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</a:Action>");
            sb.Append("</s:Header>");

            sb.Append("<s:Body>");
            sb.Append("<d:ProbeMatches>");
            sb.Append("<d:ProbeMatch>");

            sb.Append("<a:EndpointReference>");
            sb.Append("<a:Address>" + XmlEscape(endpointAddress) + "</a:Address>");
            sb.Append("</a:EndpointReference>");

            sb.Append("<d:Types>dn:NetworkVideoTransmitter tds:Device</d:Types>");
            sb.Append("<d:Scopes>");
            sb.Append("onvif://www.onvif.org/type/video_encoder ");
            sb.Append("onvif://www.onvif.org/name/" + XmlEscape(identity.DeviceName) + " ");
            sb.Append("onvif://www.onvif.org/location/POS");
            sb.Append("</d:Scopes>");
            sb.Append("<d:XAddrs>" + XmlEscape(xaddr) + "</d:XAddrs>");
            sb.Append("<d:MetadataVersion>1</d:MetadataVersion>");

            sb.Append("</d:ProbeMatch>");
            sb.Append("</d:ProbeMatches>");
            sb.Append("</s:Body>");
            sb.Append("</s:Envelope>");

            return sb.ToString();
        }

        /// <summary>
        /// Probe 요청 XML에서 WS-Addressing MessageID 값을 추출한다.
        /// 
        /// namespace prefix가 달라도 LocalName이 MessageID인 노드를 찾는다.
        /// XML 파싱에 실패하면 빈 문자열을 반환한다.
        /// </summary>
        /// <param name="requestXml">Probe 요청 XML.</param>
        /// <returns>MessageID 값. 없으면 빈 문자열.</returns>
        private string ExtractMessageId(string requestXml)
        {
            if (string.IsNullOrWhiteSpace(requestXml))
                return "";

            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(requestXml);

                XmlNodeList nodes = document.GetElementsByTagName("*");

                foreach (XmlNode node in nodes)
                {
                    if (node == null)
                        continue;

                    if (string.Equals(node.LocalName, "MessageID", StringComparison.OrdinalIgnoreCase))
                        return node.InnerText;
                }
            }
            catch
            {
            }

            return "";
        }

        /// <summary>
        /// XML 특수문자를 안전하게 이스케이프한다.
        /// </summary>
        /// <param name="value">원본 문자열.</param>
        /// <returns>XML 이스케이프 처리된 문자열.</returns>
        private string XmlEscape(string value)
        {
            if (value == null)
                return "";

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        /// <summary>
        /// WS-Discovery 서비스 리소스를 정리한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Stop();
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}