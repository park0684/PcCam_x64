using System;
using System.Threading;
using PcCam_x64.Infrastructure;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 송출 전체 흐름을 제어하는 서비스.
    /// 
    /// 역할:
    /// 1. 인증 상태 확인
    /// 2. 실행 중 재인증 감시 시작
    /// 3. 모니터 정보 조회
    /// 4. MediaMTX 실행
    /// 5. ONVIF HTTP / Discovery 실행
    /// 6. FFmpeg 실행
    /// 7. 송출 상태 관리
    /// 8. 송출 중지 처리
    /// 9. 오류 발생 시 상태 변경
    /// </summary>
    public class StreamSupervisorService : IDisposable
    {
        private readonly object _syncLock = new object();

        private readonly PathProvider _pathProvider;
        private readonly MonitorService _monitorService;
        private readonly RtspServerService _rtspServerService;
        private readonly FfmpegService _ffmpegService;
        private readonly AuthDllAdapter _authDllAdapter;
        private readonly OnvifHttpServerService _onvifHttpServerService;
        private readonly OnvifDiscoveryService _onvifDiscoveryService;
        private readonly RuntimeAuthMonitorService _runtimeAuthMonitorService;

        private AppConfig _currentConfig;
        private StreamRuntimeStatus _status = StreamRuntimeStatus.Stopped;
        private bool _disposed;
        private readonly StreamRuntimeConfigApplier _runtimeConfigApplier =  new StreamRuntimeConfigApplier();
        /// <summary>
        /// 로그 수신 이벤트.
        /// Presenter 또는 LogService에서 구독한다.
        /// </summary>
        public event Action<string> LogReceived;

        /// <summary>
        /// 상태 변경 이벤트.
        /// 트레이 아이콘, 설정 화면 상태 표시에서 사용한다.
        /// </summary>
        public event Action<StreamRuntimeStatus> StatusChanged;

        /// <summary>
        /// 마지막 오류 메시지.
        /// </summary>
        public string LastErrorMessage { get; private set; }

        /// <summary>
        /// 프로세스 종료 이벤트에 의한 자동 복구를 일시적으로 막기 위한 플래그.
        /// 
        /// 설정 변경에 따른 재시작, 사용자의 종료 요청, 프로그램 종료 과정에서는
        /// FFmpeg/MediaMTX 종료 이벤트가 정상적으로 발생할 수 있다.
        /// 이 경우 자동 복구가 실행되면 안 되므로 이 값을 true로 둔다.
        /// </summary>
        private bool _suppressExitRecovery;

        /// <summary>
        /// 자동 복구가 이미 진행 중인지 여부.
        /// 
        /// FFmpeg와 MediaMTX가 연속으로 종료 이벤트를 발생시킬 수 있으므로,
        /// 중복 복구 시도를 막기 위해 사용한다.
        /// </summary>
        private bool _isRecovering;

        /// <summary>
        /// 현재 송출 상태.
        /// </summary>
        public StreamRuntimeStatus Status
        {
            get
            {
                lock (_syncLock)
                {
                    return _status;
                }
            }
        }

        /// <summary>
        /// PC CAM 송출 제어 서비스를 생성한다.
        /// </summary>
        public StreamSupervisorService(
            PathProvider pathProvider,
            MonitorService monitorService,
            RtspServerService rtspServerService,
            FfmpegService ffmpegService,
            AuthDllAdapter authDllAdapter,
            OnvifHttpServerService onvifHttpServerService,
            OnvifDiscoveryService onvifDiscoveryService,
            RuntimeAuthMonitorService runtimeAuthMonitorService)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            if (monitorService == null)
                throw new ArgumentNullException("monitorService");

            if (rtspServerService == null)
                throw new ArgumentNullException("rtspServerService");

            if (ffmpegService == null)
                throw new ArgumentNullException("ffmpegService");

            if (authDllAdapter == null)
                throw new ArgumentNullException("authDllAdapter");

            if (onvifHttpServerService == null)
                throw new ArgumentNullException("onvifHttpServerService");

            if (onvifDiscoveryService == null)
                throw new ArgumentNullException("onvifDiscoveryService");

            if (runtimeAuthMonitorService == null)
                throw new ArgumentNullException("runtimeAuthMonitorService");

            _pathProvider = pathProvider;
            _monitorService = monitorService;
            _rtspServerService = rtspServerService;
            _ffmpegService = ffmpegService;
            _authDllAdapter = authDllAdapter;
            _onvifHttpServerService = onvifHttpServerService;
            _onvifDiscoveryService = onvifDiscoveryService;
            _runtimeAuthMonitorService = runtimeAuthMonitorService;

            _rtspServerService.LogReceived += OnRtspServerLogReceived;
            _ffmpegService.LogReceived += OnFfmpegLogReceived;

            _rtspServerService.Exited += OnRtspServerExited;
            _ffmpegService.Exited += OnFfmpegExited;

            /*
             * 실행 중 인증 실패 이벤트 구독.
             * RuntimeAuthMonitorService는 인증 실패를 감지하면 이벤트만 발생시키고,
             * 실제 송출 중지는 StreamSupervisorService가 담당한다.
             */
            _runtimeAuthMonitorService.AuthFailed += OnRuntimeAuthFailed;
        }

        /// <summary>
        /// 송출을 시작한다.
        /// 
        /// 처리 순서:
        /// 1. 시작 인증 확인
        /// 2. 인증 성공 시 DLL이 반환한 NextCheckAt 기준으로 실행 중 인증 감시 시작
        /// 3. 활성화된 Stream 설정 확인
        /// 4. MediaMTX 실행
        /// 5. ONVIF HTTP 서버 실행
        /// 6. ONVIF Discovery 실행
        /// 7. 활성화된 모든 StreamConfig에 대해 FFmpeg 실행
        /// </summary>
        public void Start(AppConfig config)
        {
            if (_disposed)
                throw new ObjectDisposedException("StreamSupervisorService");

            if (config == null)
                throw new ArgumentNullException("config");

            lock (_syncLock)
            {
                if (_status == StreamRuntimeStatus.Running ||
                    _status == StreamRuntimeStatus.Starting)
                {
                    RaiseLog("이미 송출 중입니다.");
                    return;
                }

                _currentConfig = config;
                LastErrorMessage = "";
                SetStatus(StreamRuntimeStatus.Starting);
            }

            try
            {
                RaiseLog("송출 시작 요청");

                /*
                 * 1. 시작 인증 상태 확인.
                 * 
                 * CheckCanRun은 AuthDllAdapter를 통해 PccAuthClient.dll의 CheckStartup을 호출한다.
                 * 인증 성공 시 PccAuthClient.dll이 계산한 다음 인증 확인 시각인 NextCheckAt을 반환한다.
                 */
                AuthResult authResult = StartAuthMonitoring(config);

                if (authResult == null || !authResult.IsSuccess)
                {
                    return;
                }

                RaiseLog(
                    "시작 인증 결과. Success=" +
                    (authResult != null && authResult.IsSuccess) +
                    ", Message=" +
                    (authResult == null ? "" : authResult.Message) +
                    ", NextCheckAt=" +
                    (authResult != null && authResult.NextCheckAt.HasValue
                        ? authResult.NextCheckAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                        : ""));

                if (authResult == null || !authResult.IsSuccess)
                {
                    string message = authResult == null
                        ? "인증 결과가 없습니다."
                        : authResult.Message;

                    LastErrorMessage = message;
                    RaiseLog("인증 실패: " + message);
                    SetStatus(StreamRuntimeStatus.Unauthorized);
                    return;
                }

                RaiseLog("인증 확인 완료: " + authResult.Message);

                /*
                 * 2. 실행 중 인증 감시 시작.
                 */
                _runtimeAuthMonitorService.Start(authResult.NextCheckAt);

                /*
                 * 3. 활성 Stream 설정 확인.
                 * 
                 * 기존에는 Stream0만 확인했지만,
                 * 이제는 Stream0, Stream1, Stream2 등 활성화된 모든 Stream을 송출 대상으로 본다.
                 */
                if (config.Streams == null || config.Streams.Count == 0)
                    throw new InvalidOperationException("스트림 설정이 없습니다.");

                int enabledStreamCount = CountEnabledStreams(config);

                if (enabledStreamCount <= 0)
                    throw new InvalidOperationException("활성화된 스트림 설정이 없습니다.");

                RaiseLog("활성 스트림 수: " + enabledStreamCount);

                /*
                 * ONVIF 서버를 시작하기 전에 실제 모니터 해상도를 StreamConfig에 반영한다.
                 * 
                 * 이유:
                 * - ONVIF GetProfiles / GetVideoEncoderConfigurationOptions는 config.Streams 값을 보고 응답한다.
                 * - FFmpeg BuildArguments 안에서 해상도를 보정하면 이미 ONVIF 서버가 시작된 뒤라 늦을 수 있다.
                 * - 따라서 MediaMTX / ONVIF / FFmpeg 시작 전에 먼저 런타임 해상도를 확정한다.
                 */
                ApplyRuntimeStreamConfig(config);

                /*
                 * 4. MediaMTX 실행.
                 * 
                 * MediaMTX 설정 파일에는 Stream.Main/Sub RTSP 경로가 모두 포함되어 있어야 한다.
                 */
                _rtspServerService.Start(config);

                /*
                 * MediaMTX가 RTSP 포트를 열 시간을 잠시 준다.
                 * 후속 단계에서는 포트 체크 또는 로그 감지 방식으로 개선할 수 있다.
                 */
                Thread.Sleep(1500);

                /*
                 * 5. ONVIF HTTP 서버 실행.
                 * 
                 * ONVIF 서버는 하나의 포트에서 동작하고,
                 * Profile만 여러 개 반환하는 구조다.
                 */
                _onvifHttpServerService.Start(config);

                /*
                 * 6. ONVIF Discovery 실행.
                 * 자동 검색은 부가 기능이므로 실패해도 송출은 계속한다.
                 */
                TryStartOnvifDiscovery(config);

                /*
                 * 7. 활성화된 모든 StreamConfig에 대해 FFmpeg 실행.
                 * 
                 * Stream0 → 모니터 1
                 * Stream1 → 모니터 2
                 * Stream2 → 모니터 3
                 * 
                 * 각 Stream은 내부적으로 Main/Sub RTSP 출력을 가질 수 있다.
                 */
                StartEnabledStreams(config);

                SetStatus(StreamRuntimeStatus.Running);

                RaiseLog("송출 시작 완료. ActiveStreams=" + enabledStreamCount);
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
                RaiseLog("송출 시작 실패: " + ex);

                /*
                 * 시작 중 실패하면 실행 중 인증 감시와 프로세스를 정리한다.
                 */
                try
                {
                    _runtimeAuthMonitorService.Stop();
                }
                catch
                {
                }

                try
                {
                    _ffmpegService.Stop();
                }
                catch
                {
                }

                try
                {
                    TryStopOnvifDiscovery();
                }
                catch
                {
                }

                try
                {
                    TryStopOnvifHttpServer();
                }
                catch
                {
                }

                try
                {
                    _rtspServerService.Stop();
                }
                catch
                {
                }

                SetStatus(StreamRuntimeStatus.Error);
            }
        }

        /// <summary>
        /// 활성화된 StreamConfig 개수를 계산한다.
        /// 
        /// StreamConfig.IsEnabled가 true인 항목만 송출 대상으로 본다.
        /// Main/Sub 사용 여부는 FfmpegCommandBuilder에서 다시 판단한다.
        /// </summary>
        /// <param name="config">
        /// 현재 PC CAM 설정.
        /// </param>
        /// <returns>
        /// 활성화된 StreamConfig 개수.
        /// </returns>
        private int CountEnabledStreams(AppConfig config)
        {
            if (config == null || config.Streams == null)
                return 0;

            int count = 0;

            foreach (StreamConfig stream in config.Streams)
            {
                if (stream == null)
                    continue;

                if (stream.IsEnabled)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 활성화된 모든 StreamConfig에 대해 FFmpeg 송출을 시작한다.
        /// 
        /// 각 StreamConfig는 하나의 모니터 화면을 의미한다.
        /// 각 StreamConfig 내부에는 MainStream/SubStream이 포함될 수 있다.
        /// </summary>
        /// <param name="config">
        /// 현재 PC CAM 설정.
        /// </param>
        private void StartEnabledStreams(AppConfig config)
        {
            if (config == null || config.Streams == null)
                return;

            foreach (StreamConfig stream in config.Streams)
            {
                if (stream == null)
                    continue;

                if (!stream.IsEnabled)
                {
                    RaiseLog(
                        "비활성 스트림 건너뜀. StreamNo=" +
                        stream.StreamNo +
                        ", ScreenName=" +
                        stream.ScreenName);

                    continue;
                }

                /*
                 * StreamConfig 기준으로 대상 모니터를 찾는다.
                 * 
                 * 현재 MonitorService.GetMonitorForStream()이
                 * MonitorRole 또는 ScreenName 기준으로 모니터를 찾는 구조라면,
                 * 여기서 각 Stream별로 다른 MonitorInfo가 반환되어야 한다.
                 */
                MonitorInfo monitor = _monitorService.GetMonitorForStream(stream);

                if (monitor == null)
                {
                    throw new InvalidOperationException(
                        "송출 대상 모니터를 찾을 수 없습니다. " +
                        "StreamNo=" +
                        stream.StreamNo +
                        ", MonitorRole=" +
                        stream.MonitorRole +
                        ", ScreenName=" +
                        stream.ScreenName);
                }

                RaiseLog(
                    "스트림 송출 준비. StreamNo=" +
                    stream.StreamNo +
                    ", ScreenName=" +
                    stream.ScreenName +
                    ", Monitor=" +
                    monitor.DisplayText);

                /*
                 * FfmpegService는 StreamNo별 ProcessRunner를 관리한다.
                 * 따라서 Stream0, Stream1, Stream2가 각각 별도 FFmpeg 프로세스로 실행된다.
                 */
                _ffmpegService.Start(
                    stream,
                    monitor,
                    config.RtspServer);

                RaiseLog(
                    "스트림 송출 시작 요청 완료. StreamNo=" +
                    stream.StreamNo +
                    ", RtspPath=" +
                    stream.RtspPath);
            }
        }

        /// <summary>
        /// 송출을 중지한다.
        /// 
        /// 외부에서 명시적으로 송출 중지를 요청할 때 호출된다.
        /// 종료 순서는 인증 감시 → FFmpeg → ONVIF Discovery → ONVIF HTTP → MediaMTX 순서이다.
        /// </summary>
        public void Stop()
        {
            StopCore(true);
        }

        /// <summary>
        /// 송출 중지 내부 처리 메서드.
        /// 
        /// suppressRecovery 값이 true이면 종료 이벤트가 발생해도 자동 복구하지 않는다.
        /// 설정 변경으로 인한 재시작이나 프로그램 종료 과정에서는 true로 사용한다.
        /// </summary>
        /// <param name="suppressRecovery">
        /// true: 종료 이벤트에 의한 자동 복구를 막음
        /// false: 자동 복구 억제를 적용하지 않음
        /// </param>
        private void StopCore(bool suppressRecovery)
        {
            if (_disposed)
                return;

            lock (_syncLock)
            {
                if (_status == StreamRuntimeStatus.Stopped ||
                    _status == StreamRuntimeStatus.Stopping)
                {
                    RaiseLog("이미 송출 중지 상태입니다.");
                    return;
                }

                _suppressExitRecovery = suppressRecovery;
                SetStatus(StreamRuntimeStatus.Stopping);
            }

            try
            {
                RaiseLog("송출 중지 요청");

                /*
                 * FFmpeg가 MediaMTX로 publish 중이므로,
                 * FFmpeg를 먼저 종료한 뒤 MediaMTX를 종료한다.
                 */
                _ffmpegService.Stop();

                Thread.Sleep(500);

                TryStopOnvifDiscovery();

                Thread.Sleep(200);

                TryStopOnvifHttpServer();

                Thread.Sleep(200);

                _rtspServerService.Stop();

                SetStatus(StreamRuntimeStatus.Stopped);

                RaiseLog("송출 중지 완료");
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
                RaiseLog("송출 중지 중 오류: " + ex);
                SetStatus(StreamRuntimeStatus.Error);
            }
            finally
            {
                _suppressExitRecovery = false;
            }
        }

        /// <summary>
        /// 송출을 재시작한다.
        /// 
        /// 설정 변경 후 새 설정을 적용하거나,
        /// 자동 복구 과정에서 송출을 다시 시작할 때 사용한다.
        /// 
        /// 재시작 중 발생하는 FFmpeg/MediaMTX 종료 이벤트는 정상 흐름이므로
        /// 자동 복구가 다시 중복 실행되지 않도록 StopCore(true)를 사용한다.
        /// </summary>
        /// <param name="config">
        /// 재시작에 사용할 최신 설정 객체.
        /// </param>
        public void Restart(AppConfig config)
        {
            RaiseLog("송출 재시작 요청");

            StopCore(true);

            Thread.Sleep(500);

            Start(config);
        }

        private void OnRtspServerLogReceived(string line)
        {
            RaiseLog("[MediaMTX] " + line);
        }

        private void OnFfmpegLogReceived(string line)
        {
            RaiseLog("[FFmpeg] " + line);
        }

        /// <summary>
        /// MediaMTX 프로세스 종료 이벤트를 처리한다.
        /// 
        /// 설정 변경, 프로그램 종료, 명시적 중지 과정에서 발생한 종료 이벤트는 무시한다.
        /// 송출 중 MediaMTX가 예기치 않게 종료된 경우에는 전체 송출 재시작을 시도한다.
        /// </summary>
        /// <param name="exitCode">
        /// MediaMTX 프로세스 종료 코드.
        /// </param>
        private void OnRtspServerExited(int exitCode)
        {
            RaiseLog("MediaMTX 프로세스 종료 감지. ExitCode=" + exitCode);

            if (_suppressExitRecovery)
            {
                RaiseLog("MediaMTX 종료 이벤트 무시: 정상 중지 또는 재시작 과정");
                return;
            }

            if (Status == StreamRuntimeStatus.Running)
            {
                LastErrorMessage = "MediaMTX 프로세스가 예기치 않게 종료되었습니다.";
                SetStatus(StreamRuntimeStatus.Error);

                TryRecoverStreaming("MediaMTX 예기치 않은 종료");
            }
        }

        /// <summary>
        /// FFmpeg 프로세스 종료 이벤트를 처리한다.
        /// 
        /// 사용자가 종료하거나 설정 변경으로 재시작하는 과정에서 발생한 종료 이벤트는 무시한다.
        /// 송출 중 예기치 않게 종료된 경우에는 자동 복구를 시도한다.
        /// </summary>
        /// <param name="exitCode">
        /// FFmpeg 프로세스 종료 코드.
        /// </param>
        private void OnFfmpegExited(int exitCode)
        {
            RaiseLog("FFmpeg 프로세스 종료 감지. ExitCode=" + exitCode);

            if (_suppressExitRecovery)
            {
                RaiseLog("FFmpeg 종료 이벤트 무시: 정상 중지 또는 재시작 과정");
                return;
            }

            if (Status == StreamRuntimeStatus.Running)
            {
                LastErrorMessage = "FFmpeg 프로세스가 예기치 않게 종료되었습니다.";
                SetStatus(StreamRuntimeStatus.Error);

                TryRecoverStreaming("FFmpeg 예기치 않은 종료");
            }
        }

        /// <summary>
        /// 송출 자동 복구를 시도한다.
        /// 
        /// 자동 복구 조건:
        /// 1. 현재 복구가 진행 중이 아니어야 한다.
        /// 2. 현재 설정이 존재해야 한다.
        /// 3. Operation.AutoStartStreaming 값이 true여야 한다.
        /// 
        /// 복구 방식:
        /// - 잠시 대기 후 기존 FFmpeg/MediaMTX를 정리한다.
        /// - 현재 설정으로 MediaMTX와 FFmpeg를 다시 시작한다.
        /// </summary>
        /// <param name="reason">
        /// 자동 복구를 시도하는 원인.
        /// 예: FFmpeg 예기치 않은 종료, MediaMTX 예기치 않은 종료
        /// </param>
        private void TryRecoverStreaming(string reason)
        {
            lock (_syncLock)
            {
                if (_isRecovering)
                {
                    RaiseLog("송출 자동 복구 생략: 이미 복구 진행 중");
                    return;
                }

                _isRecovering = true;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    RaiseLog("송출 자동 복구 대기 시작. 원인=" + reason);

                    Thread.Sleep(2000);

                    AppConfig config = _currentConfig;

                    if (config == null)
                    {
                        RaiseLog("송출 자동 복구 중단: 현재 설정 없음");
                        return;
                    }

                    bool autoStartStreaming =
                        config.Operation != null &&
                        config.Operation.AutoStartStreaming;

                    if (!autoStartStreaming)
                    {
                        RaiseLog("송출 자동 복구 중단: AutoStartStreaming=False");
                        return;
                    }

                    RaiseLog("송출 자동 복구 시작");

                    Restart(config);

                    RaiseLog("송출 자동 복구 완료");
                }
                catch (Exception ex)
                {
                    LastErrorMessage = ex.Message;
                    RaiseLog("송출 자동 복구 실패: " + ex);
                    SetStatus(StreamRuntimeStatus.Error);
                }
                finally
                {
                    lock (_syncLock)
                    {
                        _isRecovering = false;
                    }
                }
            });
        }

        private void SetStatus(StreamRuntimeStatus status)
        {
            lock (_syncLock)
            {
                _status = status;
            }

            RaiseLog("상태 변경: " + status);

            Action<StreamRuntimeStatus> handler = StatusChanged;

            if (handler != null)
                handler(status);
        }

        private void RaiseLog(string message)
        {
            Action<string> handler = LogReceived;

            if (handler != null)
                handler(message);
        }

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

            try
            {
                _rtspServerService.LogReceived -= OnRtspServerLogReceived;
                _ffmpegService.LogReceived -= OnFfmpegLogReceived;

                _rtspServerService.Exited -= OnRtspServerExited;
                _ffmpegService.Exited -= OnFfmpegExited;

                /*
                 * 실행 중 인증 실패 이벤트 해제.
                 */
                if (_runtimeAuthMonitorService != null)
                    _runtimeAuthMonitorService.AuthFailed -= OnRuntimeAuthFailed;
            }
            catch
            {
            }

            _disposed = true;
        }

        /// <summary>
        /// ONVIF WS-Discovery 서비스를 시작한다.
        /// 
        /// WS-Discovery는 NVR 자동 검색을 위한 부가 기능이다.
        /// ONVIF 수동 등록과 NVR 녹화는 ONVIF HTTP 서버와 RTSP 스트림만으로도 가능하므로,
        /// Discovery 시작 실패가 전체 송출 실패로 이어지면 안 된다.
        /// 
        /// 따라서 이 메서드는 예외를 외부로 던지지 않고 로그만 기록한다.
        /// </summary>
        /// <param name="config">
        /// 현재 PC CAM 설정.
        /// </param>
        private void TryStartOnvifDiscovery(AppConfig config)
        {
            if (_onvifDiscoveryService == null)
                return;

            try
            {
                _onvifDiscoveryService.Start(config);

                RaiseLog("ONVIF Discovery 서비스 시작 완료");
            }
            catch (Exception ex)
            {
                /*
                 * 자동 검색 기능은 선택 기능이다.
                 * 수동 ONVIF 등록과 NVR 녹화는 계속 가능해야 하므로
                 * Discovery 실패는 오류 로그만 남기고 송출은 계속 진행한다.
                 */
                RaiseLog("ONVIF Discovery 서비스 시작 실패. 자동 검색은 사용할 수 없지만 수동 ONVIF 등록은 계속 가능합니다. " + ex.Message);
            }
        }

        /// <summary>
        /// ONVIF WS-Discovery 서비스를 안전하게 중지한다.
        /// 
        /// Discovery 서비스는 자동 검색용 부가 기능이므로,
        /// 중지 중 오류가 발생해도 전체 종료 흐름을 막지 않는다.
        /// </summary>
        private void TryStopOnvifDiscovery()
        {
            if (_onvifDiscoveryService == null)
                return;

            try
            {
                _onvifDiscoveryService.Stop();

                RaiseLog("ONVIF Discovery 서비스 중지 완료");
            }
            catch (Exception ex)
            {
                RaiseLog("ONVIF Discovery 서비스 중지 중 오류: " + ex.Message);
            }
        }

        /// <summary>
        /// ONVIF HTTP 서버를 안전하게 중지한다.
        /// 
        /// ONVIF HTTP 서버는 수동 ONVIF 등록에 필요한 핵심 서비스지만,
        /// 프로그램 종료 또는 송출 재시작 과정에서 중지 실패가 발생해도
        /// 전체 종료 흐름이 멈추면 안 된다.
        /// </summary>
        private void TryStopOnvifHttpServer()
        {
            if (_onvifHttpServerService == null)
                return;

            try
            {
                _onvifHttpServerService.Stop();

                RaiseLog("ONVIF HTTP 서버 중지 완료");
            }
            catch (Exception ex)
            {
                RaiseLog("ONVIF HTTP 서버 중지 중 오류: " + ex.Message);
            }
        }

        /// <summary>
        /// 실행 중 인증 실패 이벤트를 처리한다.
        /// 
        /// RuntimeAuthMonitorService는 인증 실패를 감지하면 이벤트만 발생시키고,
        /// 실제 송출 중지는 StreamSupervisorService가 담당한다.
        /// </summary>
        /// <param name="authResult">
        /// 실행 중 인증 실패 결과.
        /// </param>
        private void OnRuntimeAuthFailed(AuthResult authResult)
        {
            string message = authResult == null
                ? "실행 중 인증 결과가 없습니다."
                : authResult.Message;

            RaiseLog("실행 중 인증 실패로 송출을 중지합니다. " + message);

            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                RaiseLog("실행 중 인증 실패 후 송출 중지 오류: " + ex.Message);
            }
        }

        /// <summary>
        /// 송출 시작 전에 런타임 스트림 설정을 보정한다.
        /// 
        /// MainStream.Width / Height가 0인 경우,
        /// 실제 캡처 대상 모니터의 해상도 값을 반영한다.
        /// 
        /// 이 메서드는 반드시 ONVIF HTTP 서버 시작 전에 호출되어야 한다.
        /// 그래야 NVR이 ONVIF로 조회하는 설정 해상도와 실제 RTSP 송출 해상도가 일치한다.
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        private void ApplyRuntimeStreamConfig(AppConfig config)
        {
            if (config == null || config.Streams == null)
                return;

            foreach (StreamConfig stream in config.Streams)
            {
                if (stream == null)
                    continue;

                if (!stream.IsEnabled)
                    continue;

                MonitorInfo monitor = _monitorService.GetMonitorForStream(stream);

                if (monitor == null)
                {
                    throw new InvalidOperationException(
                        "송출 대상 모니터를 찾을 수 없습니다. " +
                        "StreamNo=" + stream.StreamNo +
                        ", MonitorRole=" + stream.MonitorRole +
                        ", ScreenName=" + stream.ScreenName);
                }

                _runtimeConfigApplier.Apply(
                    stream,
                    monitor);

                RaiseLog(
                    "런타임 스트림 해상도 적용. " +
                    "StreamNo=" + stream.StreamNo +
                    ", Monitor=" + monitor.DisplayText +
                    ", Main=" + stream.MainStream.Width + "x" + stream.MainStream.Height +
                    ", Sub=" + stream.SubStream.Width + "x" + stream.SubStream.Height);
            }
        }

        /// <summary>
        /// 송출 여부와 무관하게 PC CAM 인증 상태를 확인하고 실행 중 인증 감시를 시작한다.
        /// 
        /// 사용 위치:
        /// - 프로그램 시작 시 로컬 인증정보가 있는 경우
        /// - 설정 화면에서 인증 등록이 완료된 경우
        /// - 송출 시작 전에 인증 상태를 보장해야 하는 경우
        /// </summary>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <returns>인증 확인 결과.</returns>
        public AuthResult StartAuthMonitoring(AppConfig config)
        {
            if (_disposed)
                throw new ObjectDisposedException("StreamSupervisorService");

            if (config == null)
                throw new ArgumentNullException("config");

            AuthResult authResult = _authDllAdapter.CheckCanRun(config.Auth);

            RaiseLog(
                "인증 감시 시작 전 인증 확인. Success=" +
                (authResult != null && authResult.IsSuccess) +
                ", Message=" +
                (authResult == null ? "" : authResult.Message) +
                ", NextCheckAt=" +
                (authResult != null && authResult.NextCheckAt.HasValue
                    ? authResult.NextCheckAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : ""));

            if (authResult == null || !authResult.IsSuccess)
            {
                string message = authResult == null
                    ? "인증 결과가 없습니다."
                    : authResult.Message;

                LastErrorMessage = message;

                RaiseLog("인증 감시 시작 실패: " + message);

                SetStatus(StreamRuntimeStatus.Unauthorized);

                return authResult;
            }

            _runtimeAuthMonitorService.Start(authResult.NextCheckAt);

            RaiseLog("인증 감시 시작 완료: " + authResult.Message);

            return authResult;
        }

        /// <summary>
        /// 실행 중 인증 감시를 중지한다.
        /// 
        /// 사용 위치:
        /// - 프로그램 종료
        /// - 로컬 인증정보 제거 완료 후
        /// 
        /// 송출 중지와 별개로 인증 감시 타이머만 명시적으로 중지하기 위한 메서드다.
        /// </summary>
        public void StopAuthMonitoring()
        {
            try
            {
                _runtimeAuthMonitorService.Stop();
                RaiseLog("실행 중 인증 감시 중지 완료");
            }
            catch (Exception ex)
            {
                RaiseLog("실행 중 인증 감시 중지 중 오류: " + ex.Message);
            }
        }

    }
}