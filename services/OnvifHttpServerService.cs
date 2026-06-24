using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM ONVIF HTTP 서버 서비스.
    /// 
    /// Stream별 ONVIF 포트를 각각 열어 NVR에서 각 모니터를 별도 ONVIF 장치처럼 등록할 수 있게 한다.
    /// 
    /// 예:
    /// Stream0 → 8080
    /// Stream1 → 8081
    /// Stream2 → 8082
    /// </summary>
    public class OnvifHttpServerService : IDisposable
    {
        private readonly object _syncLock = new object();

        private readonly OnvifRequestDispatcher _dispatcher;
        private readonly LogService _logService;

        private readonly Dictionary<int, OnvifListenerSlot> _listenerSlots =
            new Dictionary<int, OnvifListenerSlot>();

        private AppConfig _currentConfig;

        private bool _disposed;

        /// <summary>
        /// ONVIF HTTP 서버 서비스를 생성한다.
        /// </summary>
        public OnvifHttpServerService(
            OnvifRequestDispatcher dispatcher,
            LogService logService)
        {
            if (dispatcher == null)
                throw new ArgumentNullException("dispatcher");

            if (logService == null)
                throw new ArgumentNullException("logService");

            _dispatcher = dispatcher;
            _logService = logService;
        }

        /// <summary>
        /// 하나 이상의 ONVIF HTTP 서버가 실행 중인지 여부를 반환한다.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    foreach (KeyValuePair<int, OnvifListenerSlot> pair in _listenerSlots)
                    {
                        if (pair.Value != null && pair.Value.IsRunning)
                            return true;
                    }

                    return false;
                }
            }
        }

        /// <summary>
        /// 호환용 속성.
        /// 실행 중인 첫 번째 ONVIF 포트를 반환한다.
        /// </summary>
        public int ListeningPort
        {
            get
            {
                lock (_syncLock)
                {
                    foreach (KeyValuePair<int, OnvifListenerSlot> pair in _listenerSlots)
                    {
                        if (pair.Value != null && pair.Value.IsRunning)
                            return pair.Value.Port;
                    }

                    return 0;
                }
            }
        }

        /// <summary>
        /// 활성화된 StreamConfig마다 ONVIF HTTP 서버를 시작한다.
        /// 
        /// Stream0 → Stream0.OnvifPort
        /// Stream1 → Stream1.OnvifPort
        /// Stream2 → Stream2.OnvifPort
        /// </summary>
        public void Start(AppConfig config)
        {
            if (_disposed)
                throw new ObjectDisposedException("OnvifHttpServerService");

            if (config == null)
                throw new ArgumentNullException("config");

            lock (_syncLock)
            {
                if (IsRunning)
                    return;

                _currentConfig = config;
            }

            if (config.Streams == null || config.Streams.Count == 0)
                throw new InvalidOperationException("ONVIF 서버를 시작할 스트림 설정이 없습니다.");

            HashSet<int> usedPorts = new HashSet<int>();

            foreach (StreamConfig stream in config.Streams)
            {
                if (stream == null)
                    continue;

                if (!stream.IsEnabled)
                    continue;

                int port = ResolveOnvifPort(stream);

                if (usedPorts.Contains(port))
                {
                    throw new InvalidOperationException(
                        "ONVIF 포트가 중복되었습니다. Port=" +
                        port +
                        ", StreamNo=" +
                        stream.StreamNo);
                }

                usedPorts.Add(port);

                StartListenerForStream(stream, port);
            }

            if (!IsRunning)
                throw new InvalidOperationException("시작된 ONVIF HTTP 서버가 없습니다.");
        }

        /// <summary>
        /// 특정 Stream의 ONVIF HTTP Listener를 시작한다.
        /// </summary>
        private void StartListenerForStream(StreamConfig stream, int port)
        {
            if (stream == null)
                return;

            if (port <= 0 || port > 65535)
                throw new InvalidOperationException("ONVIF 포트 값이 올바르지 않습니다. Port=" + port);

            int streamNo = stream.StreamNo;

            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            OnvifListenerSlot slot = new OnvifListenerSlot();
            slot.StreamNo = streamNo;
            slot.Port = port;
            slot.Listener = listener;
            slot.IsRunning = true;

            Thread thread = new Thread(delegate ()
            {
                AcceptLoop(slot);
            });

            thread.IsBackground = true;
            thread.Name = "PC CAM ONVIF HTTP Listener Stream" + streamNo;

            slot.Thread = thread;

            lock (_syncLock)
            {
                if (_listenerSlots.ContainsKey(streamNo))
                {
                    OnvifListenerSlot oldSlot = _listenerSlots[streamNo];
                    _listenerSlots.Remove(streamNo);
                    StopSlot(oldSlot);
                }

                _listenerSlots.Add(streamNo, slot);
            }

            thread.Start();

            _logService.WriteApp(
                "ONVIF HTTP 서버 시작. StreamNo=" +
                streamNo +
                ", Port=" +
                port);
        }

        /// <summary>
        /// 모든 ONVIF HTTP 서버를 중지한다.
        /// </summary>
        public void Stop()
        {
            List<OnvifListenerSlot> slots = new List<OnvifListenerSlot>();

            lock (_syncLock)
            {
                foreach (KeyValuePair<int, OnvifListenerSlot> pair in _listenerSlots)
                {
                    if (pair.Value != null)
                        slots.Add(pair.Value);
                }

                _listenerSlots.Clear();
            }

            for (int i = 0; i < slots.Count; i++)
            {
                StopSlot(slots[i]);
            }

            if (slots.Count > 0)
                _logService.WriteApp("ONVIF HTTP 서버 전체 중지 요청. Count=" + slots.Count);
        }

        /// <summary>
        /// 특정 Listener Slot을 중지한다.
        /// </summary>
        private void StopSlot(OnvifListenerSlot slot)
        {
            if (slot == null)
                return;

            slot.IsRunning = false;

            try
            {
                if (slot.Listener != null)
                    slot.Listener.Stop();
            }
            catch
            {
            }

            try
            {
                if (slot.Thread != null && slot.Thread.IsAlive)
                    slot.Thread.Join(1000);
            }
            catch
            {
            }

            _logService.WriteApp(
                "ONVIF HTTP 서버 중지. StreamNo=" +
                slot.StreamNo +
                ", Port=" +
                slot.Port);
        }

        /// <summary>
        /// TCP 클라이언트 연결을 대기하고 수락하는 루프.
        /// </summary>
        private void AcceptLoop(OnvifListenerSlot slot)
        {
            while (slot != null && slot.IsRunning)
            {
                try
                {
                    TcpListener listener = slot.Listener;

                    if (listener == null)
                        return;

                    TcpClient client = listener.AcceptTcpClient();

                    OnvifClientState state = new OnvifClientState();
                    state.Client = client;
                    state.StreamNo = slot.StreamNo;
                    state.Port = slot.Port;

                    ThreadPool.QueueUserWorkItem(HandleClient, state);
                }
                catch (SocketException)
                {
                    if (slot != null && slot.IsRunning)
                    {
                        _logService.WriteError(
                            "ONVIF HTTP 서버 소켓 오류 발생. StreamNo=" +
                            slot.StreamNo +
                            ", Port=" +
                            slot.Port);
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (slot != null && slot.IsRunning)
                    {
                        _logService.WriteException(
                            "ONVIF HTTP 요청 수락 오류. StreamNo=" +
                            slot.StreamNo +
                            ", Port=" +
                            slot.Port,
                            ex);
                    }
                }
            }
        }

        /// <summary>
        /// 개별 ONVIF HTTP 클라이언트 요청을 처리한다.
        /// </summary>
        private void HandleClient(object state)
        {
            OnvifClientState clientState = state as OnvifClientState;

            if (clientState == null || clientState.Client == null)
                return;

            TcpClient client = clientState.Client;
            int streamNo = clientState.StreamNo;
            int onvifPort = clientState.Port;

            try
            {
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;

                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    OnvifHttpRequest request = ReadHttpRequest(stream);

                    if (request == null)
                    {
                        WriteTextResponse(
                            stream,
                            400,
                            "Bad Request",
                            "Invalid ONVIF HTTP request");

                        return;
                    }

                    string host = ResolveRequestHost(request, client);
                    AppConfig config = _currentConfig;

                    if (config == null)
                    {
                        WriteTextResponse(
                            stream,
                            500,
                            "Internal Server Error",
                            "PC CAM config is not loaded");

                        return;
                    }

                    if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteTextResponse(
                            stream,
                            200,
                            "OK",
                            "PC CAM ONVIF service is running. StreamNo=" + streamNo);

                        return;
                    }

                    if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteTextResponse(
                            stream,
                            405,
                            "Method Not Allowed",
                            "Only POST is supported for ONVIF SOAP");

                        return;
                    }

                    string actionName = _dispatcher.GetActionName(request.Body);

                    /*
                     * ONVIF 요청은 NVR에서 매우 자주 반복 호출된다.
                     * 운영 중에는 모든 요청을 로그로 남기지 않고,
                     * Operation.EnableDetailLog=True일 때만 상세 로그를 남긴다.
                     */
                    bool detailLog = IsDetailLogEnabled(config);

                    if (detailLog)
                    {
                        _logService.WriteApp(
                            "ONVIF 요청 수신. StreamNo=" +
                            streamNo +
                            ", Port=" +
                            onvifPort +
                            ", Action=" +
                            actionName +
                            ", Path=" +
                            request.Path +
                            ", Host=" +
                            host);
                    }

                    if (detailLog &&
                        string.Equals(actionName, "GetProfiles", StringComparison.OrdinalIgnoreCase))
                    {
                        _logService.WriteApp(
                            "ONVIF GetProfiles 요청 수신. StreamNo=" +
                            streamNo +
                            ", Port=" +
                            onvifPort);
                    }

                    if (detailLog &&
                        string.Equals(actionName, "GetStreamUri", StringComparison.OrdinalIgnoreCase))
                    {
                        string profileToken = ExtractProfileTokenForLog(request.Body);

                        _logService.WriteApp(
                            "ONVIF GetStreamUri 요청 수신. StreamNo=" +
                            streamNo +
                            ", Port=" +
                            onvifPort +
                            ", ProfileToken=" +
                            profileToken);
                    }

                    if (detailLog &&
                        string.Equals(actionName, "Unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        _logService.WriteApp(
                            "ONVIF 미지원 요청 감지. StreamNo=" +
                            streamNo +
                            ", Port=" +
                            onvifPort +
                            ", Path=" +
                            request.Path +
                            ", BodyLength=" +
                            (request.Body == null ? 0 : request.Body.Length));
                    }

                    string responseXml = _dispatcher.Dispatch(
                        request.Body,
                        config,
                        host,
                        onvifPort,
                        streamNo);

                    if (detailLog &&
                        string.Equals(actionName, "GetStreamUri", StringComparison.OrdinalIgnoreCase))
                    {
                        string rtspUri = ExtractRtspUriForLog(responseXml);

                        _logService.WriteApp(
                            "ONVIF GetStreamUri 응답. StreamNo=" +
                            streamNo +
                            ", Port=" +
                            onvifPort +
                            ", RtspUri=" +
                            rtspUri);
                    }

                    WriteSoapResponse(stream, responseXml);
                }
            }
            catch (Exception ex)
            {
                _logService.WriteException(
                    "ONVIF HTTP 요청 처리 오류. StreamNo=" +
                    streamNo +
                    ", Port=" +
                    onvifPort,
                    ex);
            }
        }

        /// <summary>
        /// 상세 로그 사용 여부를 반환한다.
        /// 
        /// Operation.EnableDetailLog=True이면 ONVIF 요청/응답 상세 로그를 남긴다.
        /// false이면 NVR의 반복 요청 로그를 생략한다.
        /// </summary>
        /// <param name="config">
        /// 현재 PC CAM 설정.
        /// </param>
        /// <returns>
        /// true: 상세 로그 사용
        /// false: 반복 상세 로그 생략
        /// </returns>
        private bool IsDetailLogEnabled(AppConfig config)
        {
            return config != null &&
                   config.Operation != null &&
                   config.Operation.EnableDetailLog;
        }

        /// <summary>
        /// ONVIF GetStreamUri 요청 XML에서 ProfileToken 값을 로그용으로 추출한다.
        /// </summary>
        private string ExtractProfileTokenForLog(string requestBody)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return "";

            string elementName = "ProfileToken";

            int nameIndex = requestBody.IndexOf(
                elementName,
                StringComparison.OrdinalIgnoreCase);

            if (nameIndex < 0)
                return "";

            int startCloseIndex = requestBody.IndexOf(
                ">",
                nameIndex,
                StringComparison.OrdinalIgnoreCase);

            if (startCloseIndex < 0)
                return "";

            int valueStartIndex = startCloseIndex + 1;

            int endIndex = requestBody.IndexOf(
                "</",
                valueStartIndex,
                StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= valueStartIndex)
                return "";

            return requestBody
                .Substring(valueStartIndex, endIndex - valueStartIndex)
                .Trim();
        }

        /// <summary>
        /// ONVIF GetStreamUri 응답 XML에서 RTSP URI를 로그용으로 추출한다.
        /// </summary>
        private string ExtractRtspUriForLog(string responseXml)
        {
            if (string.IsNullOrWhiteSpace(responseXml))
                return "";

            int startIndex = responseXml.IndexOf(
                "rtsp://",
                StringComparison.OrdinalIgnoreCase);

            if (startIndex < 0)
                return "";

            int endIndex = responseXml.IndexOf(
                "<",
                startIndex,
                StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0 || endIndex <= startIndex)
                return responseXml.Substring(startIndex).Trim();

            return responseXml
                .Substring(startIndex, endIndex - startIndex)
                .Trim();
        }

        /// <summary>
        /// HTTP 요청을 읽어 OnvifHttpRequest 객체로 변환한다.
        /// </summary>
        private OnvifHttpRequest ReadHttpRequest(NetworkStream stream)
        {
            StreamReader reader = new StreamReader(stream, Encoding.UTF8);

            string requestLine = reader.ReadLine();

            if (string.IsNullOrWhiteSpace(requestLine))
                return null;

            string[] firstLineParts = requestLine.Split(' ');

            if (firstLineParts.Length < 2)
                return null;

            OnvifHttpRequest request = new OnvifHttpRequest();
            request.Method = firstLineParts[0];
            request.Path = firstLineParts[1];
            request.Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0)
                    break;

                int colonIndex = line.IndexOf(':');

                if (colonIndex <= 0)
                    continue;

                string name = line.Substring(0, colonIndex).Trim();
                string value = line.Substring(colonIndex + 1).Trim();

                if (!request.Headers.ContainsKey(name))
                    request.Headers.Add(name, value);
                else
                    request.Headers[name] = value;
            }

            int contentLength = 0;

            if (request.Headers.ContainsKey("Content-Length"))
                int.TryParse(request.Headers["Content-Length"], out contentLength);

            if (contentLength > 0)
            {
                char[] buffer = new char[contentLength];
                int totalRead = 0;

                while (totalRead < contentLength)
                {
                    int read = reader.Read(
                        buffer,
                        totalRead,
                        contentLength - totalRead);

                    if (read <= 0)
                        break;

                    totalRead += read;
                }

                request.Body = new string(buffer, 0, totalRead);
            }
            else
            {
                request.Body = "";
            }

            return request;
        }

        /// <summary>
        /// 요청에서 NVR이 접근한 PC CAM 호스트 주소를 추정한다.
        /// </summary>
        private string ResolveRequestHost(
            OnvifHttpRequest request,
            TcpClient client)
        {
            string hostHeader = "";

            if (request != null &&
                request.Headers != null &&
                request.Headers.ContainsKey("Host"))
            {
                hostHeader = request.Headers["Host"];
            }

            if (!string.IsNullOrWhiteSpace(hostHeader))
            {
                hostHeader = hostHeader.Trim();

                int colonIndex = hostHeader.IndexOf(':');

                if (colonIndex > 0)
                    hostHeader = hostHeader.Substring(0, colonIndex);

                if (!string.IsNullOrWhiteSpace(hostHeader))
                    return hostHeader;
            }

            try
            {
                IPEndPoint localEndPoint = client.Client.LocalEndPoint as IPEndPoint;

                if (localEndPoint != null && localEndPoint.Address != null)
                    return localEndPoint.Address.ToString();
            }
            catch
            {
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// SOAP XML 응답을 HTTP 응답으로 전송한다.
        /// </summary>
        private void WriteSoapResponse(
            NetworkStream stream,
            string xml)
        {
            if (xml == null)
                xml = "";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(xml);

            StringBuilder header = new StringBuilder();
            header.Append("HTTP/1.1 200 OK\r\n");
            header.Append("Content-Type: application/soap+xml; charset=utf-8\r\n");
            header.Append("Content-Length: " + bodyBytes.Length + "\r\n");
            header.Append("Connection: close\r\n");
            header.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// 일반 텍스트 HTTP 응답을 전송한다.
        /// </summary>
        private void WriteTextResponse(
            NetworkStream stream,
            int statusCode,
            string statusText,
            string text)
        {
            if (text == null)
                text = "";

            byte[] bodyBytes = Encoding.UTF8.GetBytes(text);

            StringBuilder header = new StringBuilder();
            header.Append("HTTP/1.1 " + statusCode + " " + statusText + "\r\n");
            header.Append("Content-Type: text/plain; charset=utf-8\r\n");
            header.Append("Content-Length: " + bodyBytes.Length + "\r\n");
            header.Append("Connection: close\r\n");
            header.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());

            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// StreamConfig에서 ONVIF HTTP 포트를 결정한다.
        /// 값이 없거나 잘못된 경우 StreamNo 기준 기본값을 사용한다.
        /// </summary>
        private int ResolveOnvifPort(StreamConfig stream)
        {
            if (stream != null &&
                stream.OnvifPort > 0 &&
                stream.OnvifPort <= 65535)
            {
                return stream.OnvifPort;
            }

            int streamNo = stream == null ? 0 : stream.StreamNo;

            return 8080 + streamNo;
        }

        /// <summary>
        /// ONVIF HTTP 서버 리소스를 정리한다.
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

        /// <summary>
        /// ONVIF Listener 정보.
        /// </summary>
        private class OnvifListenerSlot
        {
            public int StreamNo;
            public int Port;
            public TcpListener Listener;
            public Thread Thread;
            public bool IsRunning;
        }

        /// <summary>
        /// 클라이언트 요청 처리에 필요한 상태값.
        /// </summary>
        private class OnvifClientState
        {
            public TcpClient Client;
            public int StreamNo;
            public int Port;
        }

        /// <summary>
        /// 내부 HTTP 요청 모델.
        /// </summary>
        private class OnvifHttpRequest
        {
            public string Method;
            public string Path;
            public Dictionary<string, string> Headers;
            public string Body;
        }
    }
}