using PcCam_x64.Infrastructure;
using PcCam_x64.Models;
using System;
using System.IO;
using System.Threading;
using System.Text;

namespace PcCam_x64.Services
{
    /// <summary>
    /// RTSP 서버 실행 관리 서비스.
    ///
    /// MediaMTX를 RTSP 서버로 사용하며,
    /// 사용자별 LocalAppData Runtime 폴더에
    /// 실행용 설정 파일을 생성하여 전달한다.
    ///
    /// 주요 역할:
    /// 1. MediaMTX 런타임 설정 생성
    /// 2. RTSP 읽기 인증 설정
    /// 3. MediaMTX 프로세스 실행
    /// 4. 시작 직후 종료 여부 확인
    /// 5. MediaMTX 종료 및 런타임 설정 삭제
    /// </summary>
    public class RtspServerService : IDisposable
    {
        private readonly PathProvider _pathProvider;
        private readonly ProcessRunner _processRunner;

        private bool _disposed;

        /// <summary>
        /// RTSP 서버 로그 수신 이벤트.
        /// View 또는 Presenter에서 이 이벤트를 구독하여 로그를 표시하거나 파일에 기록할 수 있다.
        /// </summary>
        public event Action<string> LogReceived;

        /// <summary>
        /// RTSP 서버 종료 이벤트.
        /// </summary>
        public event Action<int> Exited;

        public RtspServerService(PathProvider pathProvider)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            _pathProvider = pathProvider;
            _processRunner = new ProcessRunner();

            _processRunner.OutputReceived += OnProcessOutputReceived;
            _processRunner.ErrorReceived += OnProcessErrorReceived;
            _processRunner.Exited += OnProcessExited;
        }

        /// <summary>
        /// 현재 RTSP 서버가 실행 중인지 여부.
        /// </summary>
        public bool IsRunning
        {
            get { return _processRunner.IsRunning; }
        }

        /// <summary>
        /// RTSP 서버를 시작한다.
        ///
        /// 처리 순서:
        /// 1. 실행 폴더와 런타임 폴더 확인
        /// 2. MediaMTX 실행 파일 확인
        /// 3. External 폴더의 구형 mediamtx.yml 제거
        /// 4. 사용자 전용 Runtime 폴더에 인증 설정 생성
        /// 5. 런타임 설정 파일을 인자로 MediaMTX 실행
        /// 6. 시작 직후 종료 여부 확인
        /// </summary>
        public void Start(AppConfig config)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    "RtspServerService");
            }

            if (config == null)
            {
                throw new ArgumentNullException(
                    "config");
            }

            if (config.RtspServer == null)
            {
                throw new InvalidOperationException(
                    "RTSP 서버 설정이 없습니다.");
            }

            if (IsRunning)
                return;

            /*
             * External, Config, Log, Runtime 폴더를 생성한다.
             */
            _pathProvider.EnsureDirectories();

            string exePath =
                GetServerExePath(
                    config.RtspServer);

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException(
                    "MediaMTX 실행 파일을 찾을 수 없습니다. " +
                    "External 폴더에 파일을 배치하세요.",
                    exePath);
            }

            ValidatePort(
                config.RtspServer.RtspPort,
                "RTSP");

            ValidatePort(
                config.RtspServer.RtmpPort,
                "RTMP");

            /*
             * 이번 정책에서는 RTSP 읽기 인증을 반드시 사용한다.
             *
             * 계정이나 비밀번호가 없을 때 무인증으로 실행하지 않고
             * 시작 자체를 중단한다.
             */
            ValidateRtspAuthentication(
                config);

            /*
             * 이전 설치 버전에서 External 폴더에 생성했던
             * mediamtx.yml이 남아 있다면 제거한다.
             */
            DeleteLegacyMediaMtxConfig();

            string runtimeConfigPath =
                _pathProvider.MediaMtxConfigPath;

            /*
             * 이전 비정상 종료로 런타임 설정이 남아 있을 수 있으므로
             * 새 파일을 만들기 전에 정리한다.
             */
            DeleteRuntimeMediaMtxConfig();

            WriteMediaMtxRuntimeConfig(
                config,
                runtimeConfigPath);

            RaiseLog(
                "MediaMTX 실행 파일: " +
                exePath);

            RaiseLog(
                "MediaMTX 설정 방식: 사용자 전용 런타임 YML");

            RaiseLog(
                "MediaMTX RTSP 포트: " +
                config.RtspServer.RtspPort);

            RaiseLog(
                "MediaMTX RTMP 주소: 127.0.0.1:" +
                config.RtspServer.RtmpPort);

            RaiseLog(
                "MediaMTX RTSP 읽기 인증: 사용");

            /*
             * 설정 파일 경로에는 공백이 포함될 수 있으므로
             * 큰따옴표로 감싸서 실행 인자로 전달한다.
             */
            string arguments =
                "\"" +
                runtimeConfigPath +
                "\"";

            try
            {
                _processRunner.Start(
                    exePath,
                    arguments,
                    _pathProvider.ExternalDirectory);

                /*
                 * 포트 충돌이나 설정 오류로 MediaMTX가
                 * 시작 직후 종료되는지 확인한다.
                 */
                Thread.Sleep(1000);

                if (!_processRunner.IsRunning)
                {
                    throw new InvalidOperationException(
                        "MediaMTX가 시작 직후 종료되었습니다. " +
                        "MediaMTX 오류 로그를 확인하세요.");
                }
            }
            catch
            {
                /*
                 * 시작에 실패한 경우 인증정보가 들어 있는
                 * 런타임 설정 파일을 남기지 않는다.
                 */
                DeleteRuntimeMediaMtxConfig();

                throw;
            }
        }

        /// <summary>
        /// RTSP 서버를 종료하고 런타임 설정 파일을 제거한다.
        /// </summary>
        public void Stop()
        {
            if (_disposed)
                return;

            try
            {
                if (IsRunning)
                {
                    RaiseLog(
                        "MediaMTX 종료 요청");

                    _processRunner.StopMediaMtx();
                }
            }
            finally
            {
                /*
                 * MediaMTX가 이미 종료된 상태라도
                 * 이전 실행에서 남은 인증 설정을 정리한다.
                 */
                DeleteRuntimeMediaMtxConfig();
            }
        }

        /// <summary>
        /// MediaMTX 실행 파일 전체 경로를 반환한다.
        /// </summary>
        private string GetServerExePath(RtspServerConfig config)
        {
            string exeName = config.ServerExeName;

            if (string.IsNullOrWhiteSpace(exeName))
                exeName = "mediamtx.exe";

            return Path.Combine(_pathProvider.ExternalDirectory, exeName);
        }


        /// <summary>
        /// MediaMTX 포트 범위를 검증한다.
        /// </summary>
        private void ValidatePort(
            int port,
            string portName)
        {
            if (port <= 0 || port > 65535)
            {
                throw new InvalidOperationException(
                    portName +
                    " 포트 값이 올바르지 않습니다. Port=" +
                    port);
            }
        }

        /// <summary>
        /// MediaMTX RTSP 읽기 인증에 사용할 계정정보를 검증한다.
        ///
        /// 인증정보가 없을 때 무인증 스트림으로 실행되지 않도록
        /// 시작 단계에서 명확하게 차단한다.
        /// </summary>
        private void ValidateRtspAuthentication(
            AppConfig appConfig)
        {
            if (appConfig == null)
            {
                throw new ArgumentNullException(
                    "appConfig");
            }

            if (appConfig.Onvif == null)
            {
                throw new InvalidOperationException(
                    "ONVIF 인증 설정이 없습니다.");
            }

            if (string.IsNullOrWhiteSpace(
                    appConfig.Onvif.UserId))
            {
                throw new InvalidOperationException(
                    "RTSP 읽기 인증에 사용할 계정이 없습니다.");
            }

            if (string.IsNullOrWhiteSpace(
                    appConfig.Onvif.Password))
            {
                throw new InvalidOperationException(
                    "RTSP 읽기 인증에 사용할 비밀번호가 없습니다.");
            }
        }

        /// <summary>
        /// MediaMTX 런타임 설정 파일을 생성한다.
        ///
        /// 설정 파일은 사용자별 LocalAppData의 Runtime 폴더에 저장한다.
        ///
        /// 구성 정책:
        /// - RTSP는 외부 NVR이 접근할 수 있도록 모든 인터페이스에서 수신
        /// - RTMP는 같은 PC의 FFmpeg만 접근하도록 127.0.0.1로 제한
        /// - 모든 스트림 경로는 publisher 방식으로 동적 생성
        /// - RTSP 읽기는 ONVIF 계정과 비밀번호로 인증
        /// </summary>
        private void WriteMediaMtxRuntimeConfig(
            AppConfig appConfig,
            string configPath)
        {
            if (appConfig == null)
            {
                throw new ArgumentNullException(
                    "appConfig");
            }

            if (string.IsNullOrWhiteSpace(configPath))
            {
                throw new ArgumentException(
                    "MediaMTX 설정 파일 경로가 비어 있습니다.",
                    "configPath");
            }

            ValidatePort(
                appConfig.RtspServer.RtspPort,
                "RTSP");

            ValidatePort(
                appConfig.RtspServer.RtmpPort,
                "RTMP");

            ValidateRtspAuthentication(
                appConfig);

            string directory =
                Path.GetDirectoryName(
                    configPath);

            if (!string.IsNullOrWhiteSpace(directory) &&
                !Directory.Exists(directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            StringBuilder sb =
                new StringBuilder();

            /*
             * 운영 단계에서는 info 로그를 사용한다.
             */
            sb.AppendLine(
                "logLevel: info");

            sb.AppendLine();

            /*
             * NVR이나 캠뷰어가 다른 PC에서 접속해야 하므로
             * RTSP는 전체 네트워크 인터페이스에서 수신한다.
             */
            sb.AppendLine(
                "rtspAddress: \":" +
                appConfig.RtspServer.RtspPort +
                "\"");

            /*
             * RTMP는 로컬 FFmpeg의 publish 용도로만 사용한다.
             */
            sb.AppendLine(
                "rtmpAddress: \"127.0.0.1:" +
                appConfig.RtspServer.RtmpPort +
                "\"");

            sb.AppendLine();
            sb.AppendLine(
                "paths:");

            /*
             * all 경로 설정은 poscam, poscam_sub, poscam_1 등
             * FFmpeg가 요청하는 경로를 동적으로 허용한다.
             */
            sb.AppendLine(
                "  all:");

            sb.AppendLine(
                "    source: publisher");

            /*
             * RTSP URL 자체에는 계정정보를 포함하지 않지만,
             * 실제 RTSP 접속 과정에서는 인증을 요구한다.
             */
            sb.AppendLine(
                "    readUser: \"" +
                EscapeYamlValue(
                    appConfig.Onvif.UserId) +
                "\"");

            sb.AppendLine(
                "    readPass: \"" +
                EscapeYamlValue(
                    appConfig.Onvif.Password) +
                "\"");

            /*
             * UTF-8 BOM 없이 저장한다.
             * MediaMTX가 읽는 설정 파일이므로 인코딩 차이를 최소화한다.
             */
            File.WriteAllText(
                configPath,
                sb.ToString(),
                new UTF8Encoding(false));

            RaiseLog(
                "MediaMTX 런타임 설정 생성 완료");
        }

        /// <summary>
        /// YAML의 큰따옴표 문자열 안에 들어갈 값을 안전하게 변환한다.
        /// </summary>
        private string EscapeYamlValue(
            string value)
        {
            if (value == null)
                return "";

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        /// <summary>
        /// 사용자 전용 Runtime 폴더에 생성된
        /// MediaMTX 인증 설정 파일을 제거한다.
        ///
        /// 설정 파일에는 RTSP 계정과 비밀번호가 있으므로
        /// MediaMTX 종료 후 남기지 않는다.
        /// </summary>
        private void DeleteRuntimeMediaMtxConfig()
        {
            string configPath =
                _pathProvider.MediaMtxConfigPath;

            if (!File.Exists(configPath))
                return;

            try
            {
                File.Delete(
                    configPath);

                RaiseLog(
                    "MediaMTX 런타임 설정 제거 완료");
            }
            catch (Exception ex)
            {
                /*
                 * 종료 과정에서 삭제 실패가 발생하더라도
                 * 프로그램 전체 종료는 계속 진행한다.
                 *
                 * 다만 보안상 확인할 수 있도록 로그는 남긴다.
                 */
                RaiseLog(
                    "MediaMTX 런타임 설정 제거 실패: " +
                    ex.Message);
            }
        }

        /// <summary>
        /// 이전 버전의 PC CAM이 생성한 mediamtx.yml을 제거한다.
        ///
        /// MediaMTX를 실행 인자 없이 시작하면 실행 폴더에 남아 있는
        /// mediamtx.yml을 자동으로 읽을 가능성이 있으므로 반드시 제거한다.
        /// </summary>
        private void DeleteLegacyMediaMtxConfig()
        {
            string legacyConfigPath =
                Path.Combine(
                    _pathProvider.ExternalDirectory,
                    "mediamtx.yml");

            if (!File.Exists(legacyConfigPath))
                return;

            try
            {
                File.Delete(legacyConfigPath);

                RaiseLog(
                    "기존 MediaMTX 설정 파일 제거 완료");
            }
            catch (Exception ex)
            {
                /*
                 * 기존 파일을 삭제하지 못한 상태에서 실행하면
                 * 환경변수와 파일 설정이 혼합될 수 있으므로 시작을 중단한다.
                 */
                throw new IOException(
                    "기존 mediamtx.yml 파일을 삭제하지 못했습니다. " +
                    legacyConfigPath,
                    ex);
            }
        }


        /// <summary>
        /// RTSP 경로를 MediaMTX path 이름으로 사용할 수 있도록 정리한다.
        /// 
        /// 예:
        /// /poscam  -> poscam
        /// poscam/  -> poscam
        /// </summary>
        private string NormalizeRtspPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            path = path.Trim();

            while (path.StartsWith("/"))
                path = path.Substring(1);

            while (path.EndsWith("/"))
                path = path.Substring(0, path.Length - 1);

            /*
             * MediaMTX path 이름은 단순하게 유지하는 것이 좋다.
             * 1단계에서는 영문, 숫자, _, - 정도만 허용한다.
             */
            foreach (char ch in path)
            {
                bool valid =
                    char.IsLetterOrDigit(ch) ||
                    ch == '_' ||
                    ch == '-';

                if (!valid)
                {
                    throw new InvalidOperationException(
                        "RTSP 경로에는 영문, 숫자, _, - 만 사용할 수 있습니다. 경로: " + path);
                }
            }

            return path;
        }

        private void OnProcessOutputReceived(string line)
        {
            RaiseLog("[OUT] " + line);
        }

        private void OnProcessErrorReceived(string line)
        {
            /*
             * MediaMTX는 대부분 stdout으로 로그를 출력하지만,
             * 빌드/환경에 따라 stderr로 출력될 수도 있으므로 같이 기록한다.
             */
            RaiseLog("[ERR] " + line);
        }

        private void OnProcessExited(int exitCode)
        {
            RaiseLog("MediaMTX 종료됨. ExitCode=" + exitCode);

            Action<int> handler = Exited;
            if (handler != null)
                handler(exitCode);
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
                _processRunner.OutputReceived -= OnProcessOutputReceived;
                _processRunner.ErrorReceived -= OnProcessErrorReceived;
                _processRunner.Exited -= OnProcessExited;
                _processRunner.Dispose();
            }
            catch
            {
            }

            _disposed = true;
        }
    }


}