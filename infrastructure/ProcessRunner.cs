using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace PcCam_x64.Infrastructure
{
    /// <summary>
    /// 외부 실행 파일 프로세스 실행/종료 관리 클래스.
    /// 
    /// PC CAM에서는 아래 외부 프로그램을 실행하기 위해 사용한다.
    /// - ffmpeg.exe
    /// - mediamtx.exe
    /// 
    /// 이 클래스는 하나의 외부 프로세스만 관리한다.
    /// 따라서 FFmpeg용, MediaMTX용으로 각각 인스턴스를 따로 생성하는 방식이 안전하다.
    /// </summary>
    public class ProcessRunner : IDisposable
    {
        private readonly object _syncLock = new object();

        private Process _process;
        private bool _disposed;

        /// <summary>
        /// 표준 출력 로그 수신 이벤트.
        /// MediaMTX는 주로 표준 출력으로 로그가 나올 수 있다.
        /// </summary>
        public event Action<string> OutputReceived;

        /// <summary>
        /// 표준 오류 로그 수신 이벤트.
        /// FFmpeg는 정상 동작 중에도 진행 로그를 stderr로 출력하는 경우가 많다.
        /// 따라서 ErrorReceived라고 해서 무조건 오류로 판단하면 안 된다.
        /// </summary>
        public event Action<string> ErrorReceived;

        /// <summary>
        /// 프로세스 종료 이벤트.
        /// </summary>
        public event Action<int> Exited;

        /// <summary>
        /// 현재 프로세스가 실행 중인지 여부.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    return _process != null && !_process.HasExited;
                }
            }
        }

        /// <summary>
        /// 현재 실행 중인 프로세스 ID.
        /// 실행 중이 아니면 null을 반환한다.
        /// </summary>
        public int? ProcessId
        {
            get
            {
                lock (_syncLock)
                {
                    if (_process == null || _process.HasExited)
                        return null;

                    return _process.Id;
                }
            }
        }

        /// <summary>
        /// 외부 프로세스를 시작한다.
        ///
        /// exePath:
        /// - 실행 파일 전체 경로
        ///
        /// arguments:
        /// - 실행 인자
        ///
        /// workingDirectory:
        /// - 실행 기준 폴더
        /// - null이면 exePath의 폴더를 사용한다.
        ///
        /// environmentVariables:
        /// - 현재 실행하는 자식 프로세스에만 전달할 환경변수
        /// - Windows 시스템 환경변수에는 저장하지 않는다.
        /// </summary>
        public void Start(string exePath, string arguments, string workingDirectory = null, IDictionary<string,string> environmentVariables = null)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    "ProcessRunner");
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException(
                    "실행 파일 경로가 비어 있습니다.",
                    "exePath");
            }

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException(
                    "실행 파일을 찾을 수 없습니다.",
                    exePath);
            }

            lock (_syncLock)
            {
                /*
                 * 이전 Process 객체가 남아 있는 경우를 처리한다.
                 *
                 * 실행 중이면 중복 시작을 차단하고,
                 * 이미 종료된 객체라면 Dispose한 뒤 새 프로세스를 생성한다.
                 */
                if (_process != null)
                {
                    bool hasExited = true;

                    try
                    {
                        hasExited = _process.HasExited;
                    }
                    catch
                    {
                        /*
                         * 이미 Dispose되었거나 Process 상태를 읽을 수 없다면
                         * 종료된 객체로 보고 정리한다.
                         */
                        hasExited = true;
                    }

                    if (!hasExited)
                    {
                        throw new InvalidOperationException("이미 실행 중인 프로세스가 있습니다.");
                    }

                    CleanupProcess();
                }

                string resolvedWorkingDirectory = workingDirectory;

                if (string.IsNullOrWhiteSpace(resolvedWorkingDirectory))
                {
                    resolvedWorkingDirectory =Path.GetDirectoryName(exePath);
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();

                startInfo.FileName = exePath;

                startInfo.Arguments = arguments ?? "";

                startInfo.WorkingDirectory = resolvedWorkingDirectory;

                /*
                 * 표준 입출력을 리다이렉션하기 위해
                 * UseShellExecute는 false로 유지한다.
                 */
                startInfo.UseShellExecute = false;

                startInfo.CreateNoWindow = true;

                startInfo.RedirectStandardOutput = true;

                startInfo.RedirectStandardError = true;

                startInfo.RedirectStandardInput = true;

                /*
                 * Windows 7 한글 환경에서 로그가 깨지는 현상을 줄이기 위해
                 * 시스템 기본 인코딩을 사용한다.
                 */
                startInfo.StandardOutputEncoding = Encoding.Default;

                startInfo.StandardErrorEncoding = Encoding.Default;

                /*
                 * 현재 실행할 자식 프로세스에만 환경변수를 전달한다.
                 */
                if (environmentVariables != null)
                {
                    foreach ( KeyValuePair<string, string> item in environmentVariables)
                    {
                        if (string.IsNullOrWhiteSpace(item.Key))
                        {
                            continue;
                        }

                        startInfo.EnvironmentVariables[item.Key] = item.Value ?? "";
                    }
                }

                _process = new Process();

                _process.StartInfo = startInfo;

                _process.EnableRaisingEvents = true;

                _process.OutputDataReceived += Process_OutputDataReceived;

                _process.ErrorDataReceived += Process_ErrorDataReceived;

                _process.Exited += Process_Exited;

                try
                {
                    bool started = _process.Start();

                    if (!started)
                    {
                        throw new InvalidOperationException("프로세스를 시작하지 못했습니다.");
                    }

                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                }
                catch
                {
                    /*
                     * Start 이후 로그 리다이렉션 시작 과정에서 오류가 발생하면
                     * 외부 프로세스만 남는 상황을 방지한다.
                     */
                    try
                    {
                        if (_process != null && !_process.HasExited)
                        {
                            _process.Kill();
                            _process.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                        /*
                         * 이미 종료됐거나 Kill 권한이 없는 경우는 무시한다.
                         */
                    }

                    CleanupProcess();

                    throw;
                }
            }
        }

        /// <summary>
        /// 실행 중인 프로세스에 입력값을 전달한다.
        /// 
        /// FFmpeg는 q 입력을 받으면 정상 종료를 시도한다.
        /// </summary>
        public void SendInputLine(string text)
        {
            lock (_syncLock)
            {
                if (_process == null || _process.HasExited)
                    return;

                try
                {
                    _process.StandardInput.WriteLine(text ?? "");
                    _process.StandardInput.Flush();
                }
                catch
                {
                    // 입력 전달 실패는 종료 처리 중 발생할 수 있으므로 무시한다.
                }
            }
        }

        /// <summary>
        /// 프로세스를 종료한다.
        /// 
        /// gracefulInput:
        /// - FFmpeg 종료 시 "q"를 전달하면 정상 종료 가능성이 높다.
        /// 
        /// waitMilliseconds:
        /// - 정상 종료 대기 시간.
        /// - 시간이 지나도 종료되지 않으면 Kill을 수행한다.
        /// </summary>
        public void Stop(string gracefulInput = null, int waitMilliseconds = 5000)
        {
            lock (_syncLock)
            {
                if (_process == null)
                    return;

                if (_process.HasExited)
                {
                    CleanupProcess();
                    return;
                }

                try
                {
                    if (!string.IsNullOrEmpty(gracefulInput))
                    {
                        try
                        {
                            _process.StandardInput.WriteLine(gracefulInput);
                            _process.StandardInput.Flush();
                        }
                        catch
                        {
                            // 입력 전달 실패 시 강제 종료로 넘어간다.
                        }
                    }

                    bool exited = _process.WaitForExit(waitMilliseconds);

                    if (!exited)
                    {
                        try
                        {
                            _process.Kill();
                            _process.WaitForExit(3000);
                        }
                        catch
                        {
                            // 이미 종료되었거나 권한 문제 등이 있을 수 있으므로 무시한다.
                        }
                    }
                }
                finally
                {
                    CleanupProcess();
                }
            }
        }

        /// <summary>
        /// FFmpeg 종료용 편의 메서드.
        /// FFmpeg는 q 입력으로 종료를 시도한다.
        /// </summary>
        public void StopFfmpeg()
        {
            Stop("q", 5000);
        }

        /// <summary>
        /// MediaMTX 종료용 편의 메서드.
        /// 1단계에서는 별도 콘솔 제어 신호 없이 종료한다.
        /// </summary>
        public void StopMediaMtx()
        {
            Stop(null, 3000);
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            Action<string> handler = OutputReceived;
            if (handler != null)
                handler(e.Data);
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data))
                return;

            Action<string> handler = ErrorReceived;
            if (handler != null)
                handler(e.Data);
        }

        /// <summary>
        /// 외부 프로세스 종료 이벤트를 처리한다.
        ///
        /// 공유 필드인 _process가 아니라 이벤트를 발생시킨
        /// 실제 Process 객체에서 종료 코드를 읽는다.
        /// </summary>
        private void Process_Exited(object sender, EventArgs e)
        {
            int exitCode = -1;

            Process exitedProcess = sender as Process;

            try
            {
                if (exitedProcess != null)
                {
                    exitCode = exitedProcess.ExitCode;
                }
            }
            catch
            {
                /*
                 * 이미 Dispose됐거나 종료 코드를 읽을 수 없으면
                 * 알 수 없는 종료 코드로 처리한다.
                 */
                exitCode = -1;
            }

            Action<int> handler = Exited;

            if (handler != null)
            {
                handler(exitCode);
            }
        }

        /// <summary>
        /// 현재 Process 객체의 이벤트 연결을 해제하고 Dispose한다.
        ///
        /// 이 메서드는 _syncLock 내부에서 호출하는 것을 전제로 한다.
        /// </summary>
        private void CleanupProcess()
        {
            Process process =
                _process;

            /*
             * 다른 코드에서 정리 중인 Process 객체를 다시 사용하지 않도록
             * 공유 필드를 먼저 비운다.
             */
            _process =
                null;

            if (process == null)
                return;

            try
            {
                process.OutputDataReceived -=
                    Process_OutputDataReceived;

                process.ErrorDataReceived -=
                    Process_ErrorDataReceived;

                process.Exited -=
                    Process_Exited;
            }
            catch
            {
            }

            try
            {
                process.Dispose();
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
                Stop(null, 1000);
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}