using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// StreamNo별 투명 BGRA 프레임을 Named Pipe로 FFmpeg에 공급한다.
    ///
    /// 생명주기:
    /// 1. Pipe 서버 생성
    /// 2. FFmpeg 연결 대기
    /// 3. 지정 FPS로 고정 크기 BGRA 프레임 전송
    /// 4. FFmpeg 연결 종료 시 Pipe 정리
    /// </summary>
    public sealed class TouchOverlayPipeSession : IDisposable
    {
        private readonly object _syncLock = new object();

        private readonly int _streamNo;
        private readonly int _width;
        private readonly int _height;
        private readonly int _frameRate;
        private readonly string _pipeName;
        private readonly TouchOverlayFrameRenderer _renderer;
        private readonly ManualResetEventSlim _serverReadyEvent;

        private CancellationTokenSource _cancellationTokenSource;
        private Task _workerTask;
        private NamedPipeServerStream _currentPipe;
        private bool _started;
        private bool _disposed;

        public TouchOverlayPipeSession(
            int streamNo,
            int width,
            int height,
            int frameRate,
            TouchOverlayFrameRenderer renderer)
        {
            if (streamNo < 0)
                throw new ArgumentOutOfRangeException("streamNo");

            if (width <= 0)
                throw new ArgumentOutOfRangeException("width");

            if (height <= 0)
                throw new ArgumentOutOfRangeException("height");

            if (frameRate <= 0)
                throw new ArgumentOutOfRangeException("frameRate");

            if (renderer == null)
                throw new ArgumentNullException("renderer");

            _streamNo = streamNo;
            _width = width;
            _height = height;
            _frameRate = frameRate;
            _renderer = renderer;
            _pipeName = "pccam_touch_" + streamNo;
            _serverReadyEvent = new ManualResetEventSlim(false);
        }

        public event Action<string> LogReceived;

        /// <summary>
        /// FFmpeg 명령에 전달할 Named Pipe 전체 경로.
        /// </summary>
        public string PipePath
        {
            get
            {
                return @"\\.\pipe\" + _pipeName;
            }
        }

        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        public int FrameRate
        {
            get { return _frameRate; }
        }

        /// <summary>
        /// FFmpeg 명령 빌더에 전달할 입력 정보를 생성한다.
        /// </summary>
        public TouchOverlayInput CreateOverlayInput()
        {
            TouchOverlayInput input =
                new TouchOverlayInput();

            input.PipePath = PipePath;
            input.Width = _width;
            input.Height = _height;
            input.FrameRate = _frameRate;

            return input;
        }

        /// <summary>
        /// 현재 스트림의 오버레이 프레임 좌표에 포인터를 표시한다.
        /// </summary>
        public void ShowPointer(
            int localX,
            int localY)
        {
            if (localX < 0 ||
                localY < 0 ||
                localX >= _width ||
                localY >= _height)
            {
                RaiseLog(
                    "포인터 좌표 범위 밖. " +
                    "X=" + localX +
                    ", Y=" + localY +
                    ", Frame=" + _width + "x" + _height);

                return;
            }

            _renderer.ShowPointer(
                localX,
                localY);

            RaiseLog(
                "포인터 좌표 수신. " +
                "X=" + localX +
                ", Y=" + localY +
                ", Frame=" + _width + "x" + _height);
        }

        /// <summary>
        /// Named Pipe 서버 및 프레임 공급 작업을 시작한다.
        /// </summary>
        public void Start()
        {
            lock (_syncLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(
                        "TouchOverlayPipeSession");
                }

                if (_started)
                    return;

                _cancellationTokenSource =
                    new CancellationTokenSource();

                CancellationToken token =
                    _cancellationTokenSource.Token;

                _workerTask =
                    Task.Factory.StartNew(
                        delegate
                        {
                            RunWorker(token);
                        },
                        token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);

                _started = true;
            }

            /*
             * FFmpeg를 실행하기 전에 Pipe 서버가 생성될 시간을 보장한다.
             * 서버 생성에 실패하면 FFmpeg가 존재하지 않는 Pipe를 기다리는
             * 상태로 들어가지 않도록 시작 단계에서 오류로 처리한다.
             */
            if (!_serverReadyEvent.Wait(3000))
            {
                Stop();

                throw new InvalidOperationException(
                    "터치 오버레이 Named Pipe 서버 준비 시간이 초과되었습니다.");
            }
        }

        /// <summary>
        /// FFmpeg 종료 후 Pipe와 프레임 공급 작업을 정리한다.
        /// </summary>
        public void Stop()
        {
            CancellationTokenSource cancellationTokenSource;
            Task workerTask;
            NamedPipeServerStream currentPipe;

            lock (_syncLock)
            {
                cancellationTokenSource =
                    _cancellationTokenSource;

                workerTask =
                    _workerTask;

                currentPipe =
                    _currentPipe;

                _cancellationTokenSource = null;
                _workerTask = null;
                _currentPipe = null;
                _started = false;
            }

            if (cancellationTokenSource != null)
            {
                try
                {
                    cancellationTokenSource.Cancel();
                }
                catch
                {
                }
            }

            /*
             * WaitForConnection 또는 Write의 블로킹을 해제한다.
             */
            if (currentPipe != null)
            {
                try
                {
                    currentPipe.Dispose();
                }
                catch
                {
                }
            }

            if (workerTask != null)
            {
                try
                {
                    workerTask.Wait(5000);
                }
                catch
                {
                    // 취소 또는 Pipe 종료에 따른 예외는 정상 종료 과정으로 본다.
                }
            }

            if (cancellationTokenSource != null)
            {
                try
                {
                    cancellationTokenSource.Dispose();
                }
                catch
                {
                }
            }
        }

        private void RunWorker(
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;

                try
                {
                    pipe =
                        new NamedPipeServerStream(
                            _pipeName,
                            PipeDirection.Out,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            64 * 1024,
                            64 * 1024);

                    lock (_syncLock)
                    {
                        _currentPipe = pipe;
                    }

                    _serverReadyEvent.Set();

                    RaiseLog(
                        "Named Pipe 연결 대기: " +
                        PipePath);

                    pipe.WaitForConnection();

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    RaiseLog(
                        "FFmpeg Named Pipe 연결 완료");

                    WriteFrames(
                        pipe,
                        cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        RaiseLog(
                            "Named Pipe가 예기치 않게 종료되었습니다.");
                    }
                }
                catch (IOException ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        RaiseLog(
                            "Named Pipe 입출력 종료: " +
                            ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        RaiseLog(
                            "Named Pipe 작업 오류: " +
                            ex.Message);
                    }
                }
                finally
                {
                    lock (_syncLock)
                    {
                        if (ReferenceEquals(
                            _currentPipe,
                            pipe))
                        {
                            _currentPipe = null;
                        }
                    }

                    if (pipe != null)
                    {
                        try
                        {
                            pipe.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                    Thread.Sleep(200);
            }
        }

        private void WriteFrames(
            NamedPipeServerStream pipe,
            CancellationToken cancellationToken)
        {
            int frameDelayMilliseconds =
                Math.Max(
                    1,
                    1000 / _frameRate);

            while (!cancellationToken.IsCancellationRequested &&
                   pipe != null &&
                   pipe.IsConnected)
            {
                byte[] frame =
                    _renderer.RenderFrame(
                        DateTime.UtcNow);

                pipe.Write(
                    frame,
                    0,
                    frame.Length);

                Thread.Sleep(
                    frameDelayMilliseconds);
            }
        }

        private void RaiseLog(
            string message)
        {
            Action<string> handler =
                LogReceived;

            if (handler != null)
                handler(message ?? "");
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
                _serverReadyEvent.Dispose();
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}