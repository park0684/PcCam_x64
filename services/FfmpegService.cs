using System;
using System.Collections.Generic;
using System.IO;
using PcCam_x64.Infrastructure;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// FFmpeg 실행 관리 서비스.
    /// 
    /// 역할:
    /// 1. FFmpeg 실행 파일 존재 여부 확인
    /// 2. StreamConfig + MonitorInfo + RtspServerConfig 기준으로 FFmpeg 명령 생성
    /// 3. StreamNo별 FFmpeg 프로세스 실행
    /// 4. FFmpeg 로그 수신
    /// 5. FFmpeg 전체 종료 처리
    /// 
    /// 다중 모니터 구조:
    /// - Stream0 → FFmpeg 프로세스 1개
    /// - Stream1 → FFmpeg 프로세스 1개
    /// - Stream2 → FFmpeg 프로세스 1개
    /// 
    /// 각 FFmpeg 프로세스는 해당 모니터의 Main/Sub RTSP 출력을 담당한다.
    /// </summary>
    public class FfmpegService : IDisposable
    {
        private readonly object _syncLock = new object();

        private readonly PathProvider _pathProvider;
        private readonly FfmpegCommandBuilder _commandBuilder;
        private readonly TouchOverlayManager _touchOverlayManager;

        private readonly TouchZmqPointerManager _touchZmqPointerManager;
        /*
         * 기존에는 ProcessRunner 1개만 사용했다.
         * 다중 모니터 송출을 위해 StreamNo별 ProcessRunner를 관리한다.
         */
        private readonly Dictionary<int, FfmpegProcessSlot> _processSlots;

        private bool _disposed;

        /// <summary>
        /// FFmpeg 로그 수신 이벤트.
        /// Presenter 또는 LogService에서 구독하여 화면 표시 또는 파일 기록에 사용한다.
        /// </summary>
        public event Action<string> LogReceived;

        /// <summary>
        /// FFmpeg 종료 이벤트.
        /// 
        /// 기존 StreamSupervisorService와의 호환성을 위해 exitCode만 전달한다.
        /// 어떤 StreamNo가 종료되었는지는 로그에 함께 기록한다.
        /// </summary>
        public event Action<int> Exited;

        /// <summary>
        /// 터치 포인터 기능을 사용하지 않는 기존 생성자.
        /// 기존 호출부와의 호환성을 유지한다.
        /// </summary>
        public FfmpegService(
            PathProvider pathProvider,
            FfmpegCommandBuilder commandBuilder)
            : this(
                pathProvider,
                commandBuilder,
                null,
                null)
        {
        }

        /// <summary>
        /// 기존 Named Pipe 포인터 관리자만 전달하는 생성자.
        /// 기존 Program.cs 호출부와의 호환성을 유지한다.
        /// </summary>
        public FfmpegService(
            PathProvider pathProvider,
            FfmpegCommandBuilder commandBuilder,
            TouchOverlayManager touchOverlayManager)
            : this(
                pathProvider,
                commandBuilder,
                touchOverlayManager,
                null)
        {
        }

        /// <summary>
        /// Named Pipe 관리자와 ZMQ 포인터 관리자를 모두 전달한다.
        /// </summary>
        public FfmpegService(
            PathProvider pathProvider,
            FfmpegCommandBuilder commandBuilder,
            TouchOverlayManager touchOverlayManager,
            TouchZmqPointerManager touchZmqPointerManager)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            if (commandBuilder == null)
                throw new ArgumentNullException("commandBuilder");

            _pathProvider =
                pathProvider;

            _commandBuilder =
                commandBuilder;

            _touchOverlayManager =
                touchOverlayManager;

            _touchZmqPointerManager =
                touchZmqPointerManager;

            _processSlots =
                new Dictionary<int, FfmpegProcessSlot>();
        }

        /// <summary>
        /// 하나 이상의 FFmpeg 프로세스가 실행 중인지 여부.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    foreach (KeyValuePair<int, FfmpegProcessSlot> pair in _processSlots)
                    {
                        if (pair.Value != null &&
                            pair.Value.Runner != null &&
                            pair.Value.Runner.IsRunning)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }

        /// <summary>
        /// 특정 StreamNo의 FFmpeg 프로세스가 실행 중인지 확인한다.
        /// </summary>
        /// <param name="streamNo">
        /// 확인할 Stream 번호.
        /// </param>
        /// <returns>
        /// true: 해당 Stream FFmpeg 실행 중
        /// false: 실행 중 아님
        /// </returns>
        public bool IsStreamRunning(int streamNo)
        {
            lock (_syncLock)
            {
                FfmpegProcessSlot slot;

                if (!_processSlots.TryGetValue(streamNo, out slot))
                    return false;

                return slot != null &&
                       slot.Runner != null &&
                       slot.Runner.IsRunning;
            }
        }

        /// <summary>
        /// FFmpeg를 실행한다.
        /// 
        /// 처리 순서:
        /// 1. ffmpeg.exe 존재 확인
        /// 2. 스트림 설정 검증
        /// 3. 모니터 정보 검증
        /// 4. FFmpeg Arguments 생성
        /// 5. StreamNo별 ProcessRunner 생성
        /// 6. FFmpeg 실행
        /// </summary>
        public void Start(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo,
            RtspServerConfig rtspServerConfig,
            TouchPointerConfig touchPointerConfig)
        {
            if (_disposed)
                throw new ObjectDisposedException("FfmpegService");

            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (rtspServerConfig == null)
                throw new ArgumentNullException("rtspServerConfig");

            /*
             * 이전 호출부에서 null이 전달된 경우
             * 비활성 기본 설정을 사용한다.
             */
            TouchPointerConfig effectiveTouchPointerConfig =
                touchPointerConfig ??
                new TouchPointerConfig();

            effectiveTouchPointerConfig.Normalize();

            if (!streamConfig.IsEnabled)
                throw new InvalidOperationException("사용하지 않는 스트림은 송출할 수 없습니다.");

            if (streamConfig.StreamNo < 0)
                throw new InvalidOperationException("StreamNo 값이 올바르지 않습니다. StreamNo=" + streamConfig.StreamNo);

            int streamNo = streamConfig.StreamNo;

            /*
             * 해당 StreamNo가 이미 실행 중이면 중복 실행하지 않는다.
             * 단, 다른 StreamNo는 별도 FFmpeg 프로세스로 실행할 수 있다.
             */
            if (IsStreamRunning(streamNo))
            {
                RaiseLog("[Stream" + streamNo + "] FFmpeg가 이미 실행 중입니다.");
                return;
            }

            _pathProvider.EnsureDirectories();

            string ffmpegPath = _pathProvider.FfmpegExePath;

            if (!File.Exists(ffmpegPath))
            {
                throw new FileNotFoundException(
                    "FFmpeg 실행 파일을 찾을 수 없습니다. External 폴더에 ffmpeg.exe를 배치하세요.",
                    ffmpegPath);
            }

            /*
             * 환경변수가 아니라 현재 AppConfig의 TouchPointer 설정을 기준으로
             * ZMQ 포인터 사용 여부를 결정한다.
             */
            bool useTouchZmqPointer =
                ShouldUseTouchZmqPointer(
                    streamConfig,
                    effectiveTouchPointerConfig);

            TouchOverlayPipeSession overlaySession = null;
            TouchOverlayInput overlayInput = null;

            if (!useTouchZmqPointer && ShouldUseTouchOverlayPipePoc(streamConfig))
            {
                /*
                 * 4K 화면의 raw BGRA 전송량을 줄이기 위해
                 * 오버레이 입력 FPS는 최대 5fps로 제한한다.
                 * 원본 화면 캡처 FPS와 출력 FPS는 변경하지 않는다.
                 */
                int overlayFrameRate =
                    Math.Min(
                        GetCaptureFps(streamConfig),
                        5);

                /*
                 * 전체 4K 크기의 BGRA 프레임을 Named Pipe로 전달하면
                 * 프레임당 약 33MB, 5fps 기준 약 165MB/s가 된다.
                 *
                 * 오버레이 프레임은 최대 960x540으로 제한하고
                 * FFmpeg 필터에서 실제 모니터 해상도로 확대한다.
                 */
                int overlayWidth =
                    Math.Min(
                        monitorInfo.BoundsWidth,
                        960);

                int overlayHeight =
                    Math.Max(
                        1,
                        (int)Math.Round(
                            monitorInfo.BoundsHeight *
                            (overlayWidth /
                             (double)monitorInfo.BoundsWidth)));

                if (overlayHeight > 540)
                {
                    overlayHeight = 540;

                    overlayWidth =
                        Math.Max(
                            1,
                            (int)Math.Round(
                                monitorInfo.BoundsWidth *
                                (overlayHeight /
                                 (double)monitorInfo.BoundsHeight)));
                }

                int overlayPointerDiameter =
                    Math.Max(
                        24,
                        (int)Math.Round(
                            320.0 *
                            overlayWidth /
                            monitorInfo.BoundsWidth));

                TouchOverlayFrameRenderer renderer =
                    new TouchOverlayFrameRenderer(
                        overlayWidth,
                        overlayHeight,
                        overlayPointerDiameter,
                        3000,
                        0);

                overlaySession =
                    new TouchOverlayPipeSession(
                        streamNo,
                        overlayWidth,
                        overlayHeight,
                        overlayFrameRate,
                        renderer);

                overlaySession.LogReceived +=
                    delegate (string line)
                    {
                        RaiseLog(
                            "[Stream" + streamNo +
                            "][TouchPipe] " +
                            line);
                    };

                _touchOverlayManager.RegisterSession(
                    streamNo,
                    monitorInfo,
                    overlaySession);

                try
                {
                    overlaySession.Start();
                    overlayInput =
                        overlaySession.CreateOverlayInput();
                }
                catch
                {
                    _touchOverlayManager.UnregisterSession(
                        streamNo);

                    overlaySession.Dispose();
                    throw;
                }
            }

            string arguments;

            try
            {
                arguments = _commandBuilder.BuildArguments(
                    streamConfig,
                    monitorInfo,
                    rtspServerConfig,
                    overlayInput,
                    effectiveTouchPointerConfig);
            }
            catch
            {
                if (overlaySession != null)
                {
                    if (_touchOverlayManager != null)
                    {
                        _touchOverlayManager.UnregisterSession(
                            streamNo);
                    }

                    overlaySession.Dispose();
                }

                throw;
            }

            ProcessRunner runner = new ProcessRunner();

            runner.OutputReceived += delegate (string line)
            {
                OnProcessOutputReceived(streamNo, line);
            };

            runner.ErrorReceived += delegate (string line)
            {
                OnProcessErrorReceived(streamNo, line);
            };

            runner.Exited += delegate (int exitCode)
            {
                OnProcessExited(streamNo, exitCode);
            };

            FfmpegProcessSlot slot = new FfmpegProcessSlot();
            slot.StreamNo = streamNo;
            slot.Runner = runner;
            slot.OverlaySession = overlaySession;
            slot.UsesTouchZmqPointer = useTouchZmqPointer;


            lock (_syncLock)
            {
                /*
                 * 혹시 이전에 같은 StreamNo의 종료된 Runner가 남아 있으면 제거한다.
                 */
                if (_processSlots.ContainsKey(streamNo))
                {
                    FfmpegProcessSlot oldSlot = _processSlots[streamNo];
                    _processSlots.Remove(streamNo);

                    DisposeSlot(oldSlot);
                }

                _processSlots.Add(streamNo, slot);
            }

            try
            {
                RaiseLog("[Stream" + streamNo + "] FFmpeg 실행 파일: " + ffmpegPath);
                RaiseLog("[Stream" + streamNo + "] FFmpeg 대상 모니터: " + monitorInfo.DisplayText);
                RaiseLog("[Stream" + streamNo + "] FFmpeg 실행 인자: " + arguments);

                runner.Start(
                    ffmpegPath,
                    arguments,
                    _pathProvider.ExternalDirectory);

                /*
                 * FFmpeg 프로세스를 먼저 시작한 뒤
                 * 해당 모니터를 ZMQ 포인터 대상으로 등록한다.
                 */
                if (useTouchZmqPointer)
                {
                    _touchZmqPointerManager.RegisterTarget(
                        streamNo,
                        monitorInfo,
                        effectiveTouchPointerConfig.Diameter,
                        effectiveTouchPointerConfig.VisibleMilliseconds);

                    RaiseLog(
                        "[Stream" + streamNo +
                        "][TouchZmq] 포인터 대상 등록 완료. " +
                        "Diameter=" +
                        effectiveTouchPointerConfig.Diameter +
                        ", VisibleMs=" +
                        effectiveTouchPointerConfig.VisibleMilliseconds);
                }
            }
            catch
            {
                lock (_syncLock)
                {
                    if (_processSlots.ContainsKey(streamNo))
                        _processSlots.Remove(streamNo);
                }

                DisposeSlot(slot);

                throw;
            }
        }

        /// <summary>
        /// 특정 StreamNo의 FFmpeg를 종료한다.
        /// </summary>
        /// <param name="streamNo">
        /// 종료할 Stream 번호.
        /// </param>
        public void Stop(int streamNo)
        {
            FfmpegProcessSlot slot = null;

            lock (_syncLock)
            {
                if (_processSlots.ContainsKey(streamNo))
                {
                    slot = _processSlots[streamNo];
                    _processSlots.Remove(streamNo);
                }
            }

            if (slot == null || slot.Runner == null)
                return;

            try
            {
                if (slot.Runner.IsRunning)
                {
                    RaiseLog("[Stream" + streamNo + "] FFmpeg 종료 요청");
                    slot.Runner.StopFfmpeg();
                }
            }
            finally
            {
                DisposeSlot(slot);
            }
        }

        /// <summary>
        /// 실행 중인 모든 FFmpeg를 종료한다.
        /// 
        /// FFmpeg는 q 입력으로 정상 종료를 시도한다.
        /// 정상 종료가 되지 않으면 ProcessRunner에서 강제 종료한다.
        /// </summary>
        public void Stop()
        {
            if (_disposed)
                return;

            List<FfmpegProcessSlot> slots = new List<FfmpegProcessSlot>();

            lock (_syncLock)
            {
                foreach (KeyValuePair<int, FfmpegProcessSlot> pair in _processSlots)
                {
                    if (pair.Value != null)
                        slots.Add(pair.Value);
                }

                _processSlots.Clear();
            }

            if (slots.Count == 0)
                return;

            RaiseLog("FFmpeg 전체 종료 요청. Count=" + slots.Count);

            for (int i = 0; i < slots.Count; i++)
            {
                FfmpegProcessSlot slot = slots[i];

                if (slot == null || slot.Runner == null)
                    continue;

                try
                {
                    if (slot.Runner.IsRunning)
                    {
                        RaiseLog("[Stream" + slot.StreamNo + "] FFmpeg 종료 요청");
                        slot.Runner.StopFfmpeg();
                    }
                }
                catch (Exception ex)
                {
                    RaiseLog("[Stream" + slot.StreamNo + "] FFmpeg 종료 중 오류: " + ex.Message);
                }
                finally
                {
                    DisposeSlot(slot);
                }
            }
        }

        private void OnProcessOutputReceived(int streamNo, string line)
        {
            RaiseLog("[Stream" + streamNo + "][OUT] " + line);
        }

        /// <summary>
        /// FFmpeg stderr 로그를 처리한다.
        /// 
        /// FFmpeg는 정상 실행 중에도 stderr로 진행 상태를 계속 출력한다.
        /// 예:
        /// frame=..., fps=..., q=..., size=..., time=..., bitrate=..., speed=...
        /// 
        /// 이러한 진행 로그는 장시간 실행 시 로그 파일을 매우 크게 만들기 때문에
        /// 기본 로그에서는 제외한다.
        /// 실제 오류, 경고, 종료 메시지는 그대로 기록한다.
        /// </summary>
        /// <param name="streamNo">
        /// FFmpeg가 담당하는 Stream 번호.
        /// </param>
        /// <param name="line">
        /// FFmpeg stderr 출력 한 줄.
        /// </param>
        private void OnProcessErrorReceived(int streamNo, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            /*
             * 정상 진행 상태 로그는 저장하지 않는다.
             * 이 로그는 1초 단위로 계속 발생하므로 장시간 실행 시 로그 파일을 크게 만든다.
             */
            if (IsFfmpegProgressLine(line))
                return;

            /*
             * 진행 로그가 아닌 stderr는 유지한다.
             * 예:
             * - Could not write header
             * - Error opening output
             * - Conversion failed
             * - codec parameter 오류
             */
            RaiseLog("[Stream" + streamNo + "][ERR] " + line);
        }

        /// <summary>
        /// FFmpeg 진행 상태 로그인지 확인한다.
        /// 
        /// FFmpeg는 정상 동작 중에도 아래와 같은 진행 상태를 stderr로 출력한다.
        /// frame=..., fps=..., size=..., time=..., bitrate=..., speed=...
        /// 
        /// 이 로그는 오류가 아니므로 기본 로그에서는 제외한다.
        /// </summary>
        /// <param name="line">
        /// FFmpeg 출력 한 줄.
        /// </param>
        /// <returns>
        /// true: 진행 상태 로그
        /// false: 오류/정보 로그
        /// </returns>
        private bool IsFfmpegProgressLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string value = line.TrimStart();

            /*
             * FFmpeg 진행 로그는 대부분 frame= 으로 시작한다.
             */
            if (value.StartsWith("frame=", StringComparison.OrdinalIgnoreCase))
                return true;

            /*
             * 일부 빌드나 옵션에 따라 fps= 로 시작하는 진행 로그가 나올 수 있다.
             */
            if (value.StartsWith("fps=", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private void OnProcessExited(int streamNo, int exitCode)
        {
            FfmpegProcessSlot slot = null;

            lock (_syncLock)
            {
                if (_processSlots.ContainsKey(streamNo))
                {
                    slot = _processSlots[streamNo];
                    _processSlots.Remove(streamNo);
                }
            }

            /*
             * 비정상 종료에서도 Named Pipe와 공급 작업을 즉시 정리해야
             * 자동 복구 시 같은 Pipe 이름으로 새 세션을 만들 수 있다.
             */
            if (slot != null)
                DisposeSlot(slot);

            RaiseLog("[Stream" + streamNo + "] FFmpeg 종료됨. ExitCode=" + exitCode);

            Action<int> handler = Exited;

            if (handler != null)
                handler(exitCode);
        }

        /// <summary>
        /// 현재 스트림에서 ZMQ 클릭·터치 포인터를 사용할지 확인한다.
        /// </summary>
        private bool ShouldUseTouchZmqPointer(
            StreamConfig streamConfig,
            TouchPointerConfig touchPointerConfig)
        {
            /*
             * ZMQ 포인터 관리자가 생성되지 않은 경우에는
             * 설정이 활성화되어 있어도 적용할 수 없다.
             */
            if (_touchZmqPointerManager == null ||
                streamConfig == null ||
                touchPointerConfig == null)
            {
                return false;
            }

            /*
             * 현재 ZMQ 제어 포트가 하나이므로
             * Stream0에만 적용한다.
             */
            if (streamConfig.StreamNo < 0)
                return false;

            return touchPointerConfig.Enabled;
        }

        /// <summary>
        /// Stream0 Named Pipe 터치 오버레이 POC 사용 여부를 확인한다.
        /// </summary>
        private bool ShouldUseTouchOverlayPipePoc(
            StreamConfig streamConfig)
        {
            if (_touchOverlayManager == null ||
                streamConfig == null ||
                streamConfig.StreamNo != 0)
            {
                return false;
            }

            string value =
                Environment.GetEnvironmentVariable(
                    "PCCAM_TOUCH_PIPE_POC");

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return string.Equals(
                value.Trim(),
                "1",
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 활성화된 Main/Sub 중 가장 높은 FPS를 구한다.
        /// 오버레이 FPS 계산에만 사용한다.
        /// </summary>
        private int GetCaptureFps(
            StreamConfig streamConfig)
        {
            int fps = 1;

            if (streamConfig == null)
                return 5;

            if (streamConfig.Fps > fps)
                fps = streamConfig.Fps;

            if (streamConfig.MainStream != null &&
                streamConfig.MainStream.IsEnabled &&
                streamConfig.MainStream.Fps > fps)
            {
                fps = streamConfig.MainStream.Fps;
            }

            if (streamConfig.SubStream != null &&
                streamConfig.SubStream.IsEnabled &&
                streamConfig.SubStream.Fps > fps)
            {
                fps = streamConfig.SubStream.Fps;
            }

            return Math.Max(1, fps);
        }
        private void RaiseLog(string message)
        {
            Action<string> handler = LogReceived;

            if (handler != null)
                handler(message);
        }

        private void DisposeSlot(FfmpegProcessSlot slot)
        {
            if (slot == null)
                return;
            /*
             * 이 FFmpeg가 ZMQ 포인터 대상이었다면
             * ZMQ 좌표 라우팅 등록을 해제한다.
             */
            if (slot.UsesTouchZmqPointer &&
                _touchZmqPointerManager != null)
            {
                try
                {
                    _touchZmqPointerManager.UnregisterTarget(
                        slot.StreamNo);
                }
                catch
                {
                }
            }

            if (slot.OverlaySession != null)
            {
                try
                {
                    if (_touchOverlayManager != null)
                    {
                        _touchOverlayManager.UnregisterSession(
                            slot.StreamNo);
                    }
                }
                catch
                {
                }

                try
                {
                    slot.OverlaySession.Dispose();
                }
                catch
                {
                }
            }

            try
            {
                if (slot.Runner != null)
                    slot.Runner.Dispose();
            }
            catch
            {
            }
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

            _disposed = true;
        }

        /// <summary>
        /// StreamNo별 FFmpeg 실행 정보를 보관하는 내부 모델.
        /// </summary>
        private class FfmpegProcessSlot
        {
            public int StreamNo;
            public ProcessRunner Runner;
            public TouchOverlayPipeSession OverlaySession;
            public bool UsesTouchZmqPointer;
        }
    }
}
