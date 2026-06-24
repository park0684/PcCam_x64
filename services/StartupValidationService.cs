using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using PcCam_x64.Infrastructure;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 시작 환경을 검사하는 서비스.
    /// 
    /// 프로그램 시작 시 필수 폴더, 필수 실행 파일, 설정값, 포트 상태를 점검한다.
    /// 이 검사는 송출 시작 전에 문제를 빠르게 파악하기 위한 용도이다.
    /// </summary>
    public class StartupValidationService
    {
        private readonly PathProvider _pathProvider;
        private readonly LogService _logService;

        /// <summary>
        /// 시작 환경 검사 서비스를 생성한다.
        /// </summary>
        /// <param name="pathProvider">프로그램 경로 제공자.</param>
        /// <param name="logService">로그 기록 서비스.</param>
        public StartupValidationService(
            PathProvider pathProvider,
            LogService logService)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            if (logService == null)
                throw new ArgumentNullException("logService");

            _pathProvider = pathProvider;
            _logService = logService;
        }

        /// <summary>
        /// PC CAM 시작 환경을 검사한다.
        /// 
        /// 검사 항목:
        /// 1. 기본 폴더 생성 가능 여부
        /// 2. 로그 폴더 쓰기 가능 여부
        /// 3. ffmpeg.exe 존재 여부
        /// 4. MediaMTX 실행 파일 존재 여부
        /// 5. RTSP 포트 사용 가능 여부
        /// 6. ONVIF HTTP 포트 사용 가능 여부
        /// 7. 기본 설정값 유효성
        /// </summary>
        /// <param name="config">현재 로드된 PC CAM 설정.</param>
        /// <returns>시작 환경 검사 결과.</returns>
        public StartupValidationResult Validate(AppConfig config)
        {
            StartupValidationResult result = new StartupValidationResult();

            ValidateDirectories(result);
            ValidateRequiredFiles(config, result);
            ValidateConfigValues(config, result);
            ValidateRtspPort(config, result);
            ValidateOnvifPort(config, result);

            WriteValidationLog(result);

            return result;
        }

        /// <summary>
        /// 프로그램에서 사용하는 기본 폴더 생성 가능 여부를 검사한다.
        /// </summary>
        /// <param name="result">검사 결과 객체.</param>
        private void ValidateDirectories(StartupValidationResult result)
        {
            try
            {
                _pathProvider.EnsureDirectories();
            }
            catch (Exception ex)
            {
                result.AddError("기본 폴더를 생성할 수 없습니다. " + ex.Message);
                return;
            }

            if (!Directory.Exists(_pathProvider.ConfigDirectory))
                result.AddError("config 폴더가 없습니다.");

            if (!Directory.Exists(_pathProvider.LogDirectory))
                result.AddError("logs 폴더가 없습니다.");

            if (!Directory.Exists(_pathProvider.ExternalDirectory))
                result.AddError("External 폴더가 없습니다.");

            ValidateLogDirectoryWritable(result);
        }

        /// <summary>
        /// logs 폴더에 파일을 쓸 수 있는지 검사한다.
        /// </summary>
        /// <param name="result">검사 결과 객체.</param>
        private void ValidateLogDirectoryWritable(StartupValidationResult result)
        {
            try
            {
                string testFilePath = Path.Combine(
                    _pathProvider.LogDirectory,
                    "write_test.tmp");

                File.WriteAllText(testFilePath, "test");

                if (File.Exists(testFilePath))
                    File.Delete(testFilePath);
            }
            catch (Exception ex)
            {
                result.AddWarning("logs 폴더에 파일을 쓸 수 없습니다. " + ex.Message);
            }
        }

        /// <summary>
        /// 송출에 필요한 외부 실행 파일의 존재 여부와
        /// 실행 파일 아키텍처를 검사한다.
        /// </summary>
        private void ValidateRequiredFiles(
            AppConfig config,
            StartupValidationResult result)
        {
            string ffmpegPath =
                _pathProvider.FfmpegExePath;

            if (!File.Exists(ffmpegPath))
            {
                result.AddError(
                    "ffmpeg.exe 파일이 없습니다. 위치: " +
                    ffmpegPath);
            }
            else
            {
                /*
                 * PcCam_x64에서는 64비트 FFmpeg만 허용한다.
                 * 실수로 기존 32비트 실행 파일이 배포되는 것을
                 * 프로그램 시작 단계에서 차단한다.
                 */
                ValidateX64Executable(
                    ffmpegPath,
                    "FFmpeg",
                    result);
            }

            string mediaMtxPath =
                ResolveMediaMtxPath(config);

            if (!File.Exists(mediaMtxPath))
            {
                result.AddError(
                    "MediaMTX 실행 파일이 없습니다. 위치: " +
                    mediaMtxPath);
            }
            else
            {
                /*
                 * 최신 MediaMTX Windows amd64 실행 파일인지 확인한다.
                 */
                ValidateX64Executable(
                    mediaMtxPath,
                    "MediaMTX",
                    result);
            }
        }

        /// <summary>
        /// Windows PE 실행 파일이 AMD64(x64) 아키텍처인지 확인한다.
        ///
        /// 검사 방식:
        /// 1. DOS 헤더의 PE 헤더 위치를 읽는다.
        /// 2. PE 시그니처를 확인한다.
        /// 3. COFF 헤더의 Machine 값을 읽는다.
        /// 4. Machine 값이 AMD64(0x8664)인지 확인한다.
        /// </summary>
        /// <param name="filePath">
        /// 검사할 실행 파일의 전체 경로.
        /// </param>
        /// <param name="displayName">
        /// 오류 메시지에 표시할 프로그램 이름.
        /// </param>
        /// <param name="result">
        /// 시작 환경 검사 결과.
        /// </param>
        private void ValidateX64Executable(
            string filePath,
            string displayName,
            StartupValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.AddError(
                    displayName +
                    " 실행 파일 경로가 비어 있습니다.");

                return;
            }

            try
            {
                using (FileStream stream =
                    new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                using (BinaryReader reader =
                    new BinaryReader(stream))
                {
                    /*
                     * PE 파일의 DOS 헤더는 최소 64바이트 이상이다.
                     * 0x3C 위치에 실제 PE 헤더의 시작 위치가 들어 있다.
                     */
                    if (stream.Length < 64)
                    {
                        result.AddError(
                            displayName +
                            " 실행 파일 형식이 올바르지 않습니다. " +
                            "파일 크기가 PE 헤더보다 작습니다.");

                        return;
                    }

                    stream.Position = 0x3C;

                    int peHeaderOffset =
                        reader.ReadInt32();

                    /*
                     * PE 헤더 위치 뒤에는
                     * 시그니처 4바이트와 Machine 2바이트가 있어야 한다.
                     */
                    if (peHeaderOffset <= 0 ||
                        peHeaderOffset + 6 > stream.Length)
                    {
                        result.AddError(
                            displayName +
                            " 실행 파일의 PE 헤더 위치가 올바르지 않습니다.");

                        return;
                    }

                    stream.Position =
                        peHeaderOffset;

                    uint peSignature =
                        reader.ReadUInt32();

                    /*
                     * PE 시그니처:
                     * ASCII "PE\0\0"
                     * 값: 0x00004550
                     */
                    if (peSignature != 0x00004550)
                    {
                        result.AddError(
                            displayName +
                            " 파일이 올바른 Windows PE 실행 파일이 아닙니다.");

                        return;
                    }

                    ushort machine =
                        reader.ReadUInt16();

                    /*
                     * 주요 Machine 값:
                     * 0x014C = Intel 386, 32비트
                     * 0x8664 = AMD64, 64비트
                     * 0xAA64 = ARM64
                     */
                    const ushort imageFileMachineAmd64 =
                        0x8664;

                    if (machine != imageFileMachineAmd64)
                    {
                        result.AddError(
                            displayName +
                            " 실행 파일이 x64 버전이 아닙니다. " +
                            "Machine=0x" +
                            machine.ToString("X4") +
                            ", Path=" +
                            filePath);

                        return;
                    }

                    _logService.WriteApp(
                        displayName +
                        " x64 실행 파일 확인 완료. " +
                        "Machine=AMD64, Path=" +
                        filePath);
                }
            }
            catch (Exception ex)
            {
                result.AddError(
                    displayName +
                    " 실행 파일 아키텍처를 확인하지 못했습니다. " +
                    ex.Message);
            }
        }

        /// <summary>
        /// 설정값에 지정된 MediaMTX 실행 파일 경로를 계산한다.
        /// </summary>
        /// <param name="config">현재 설정.</param>
        /// <returns>MediaMTX 실행 파일 전체 경로.</returns>
        private string ResolveMediaMtxPath(AppConfig config)
        {
            if (config != null &&
                config.RtspServer != null &&
                !string.IsNullOrWhiteSpace(config.RtspServer.ServerExeName))
            {
                return Path.Combine(
                    _pathProvider.ExternalDirectory,
                    config.RtspServer.ServerExeName);
            }

            return _pathProvider.MediaMtxExePath;
        }

        /// <summary>
        /// 기본 설정값의 유효성을 검사한다.
        /// 
        /// 정책:
        /// - Stream0은 필수 사용 대상이 아니다.
        /// - 사용자가 보조 모니터(Stream1)만 선택할 수 있다.
        /// - 비활성 Stream은 송출 대상이 아니므로 상세 검증하지 않는다.
        /// - 활성 Stream이 하나도 없을 때만 자동 송출 경고를 표시한다.
        /// </summary>
        /// <param name="config">현재 설정.</param>
        /// <param name="result">검사 결과 객체.</param>
        private void ValidateConfigValues(
            AppConfig config,
            StartupValidationResult result)
        {
            if (config == null)
            {
                result.AddError("설정 정보를 로드할 수 없습니다.");
                return;
            }

            if (config.Streams == null || config.Streams.Count == 0)
            {
                result.AddError("스트림 설정이 없습니다.");
                return;
            }

            int enabledStreamCount = 0;

            foreach (StreamConfig stream in config.Streams)
            {
                if (stream == null)
                    continue;

                /*
                 * 비활성 Stream은 실제 송출 대상이 아니다.
                 * 따라서 ScreenName, FPS, Bitrate, RTSP, ONVIF 포트 검증에서 제외한다.
                 * 
                 * 예:
                 * - Stream0 미사용
                 * - Stream1 사용
                 * 
                 * 위 구성은 정상 허용되어야 한다.
                 */
                if (!stream.IsEnabled)
                    continue;

                enabledStreamCount++;

                if (string.IsNullOrWhiteSpace(stream.ScreenName))
                {
                    result.AddWarning(
                        "모니터 장치명이 비어 있습니다. StreamNo=" + stream.StreamNo);
                }

                if (stream.Fps <= 0)
                {
                    result.AddError(
                        "FPS 값이 올바르지 않습니다. StreamNo=" + stream.StreamNo);
                }

                if (string.IsNullOrWhiteSpace(stream.Bitrate))
                {
                    result.AddError(
                        "Bitrate 값이 비어 있습니다. StreamNo=" + stream.StreamNo);
                }

                if (string.IsNullOrWhiteSpace(stream.RtspPath))
                {
                    result.AddError(
                        "RTSP 경로가 비어 있습니다. StreamNo=" + stream.StreamNo);
                }

                if (stream.OnvifPort <= 0 || stream.OnvifPort > 65535)
                {
                    result.AddError(
                        "ONVIF 포트 값이 올바르지 않습니다. StreamNo=" + stream.StreamNo);
                }

                /*
                 * 현재 설정 화면에는 Main/Sub 개별 사용 체크가 없다.
                 * 따라서 부모 Stream이 사용이면 Main/Sub도 사용 상태여야 한다.
                 */
                if (stream.MainStream == null)
                {
                    result.AddError(
                        "Main Stream 설정이 없습니다. StreamNo=" + stream.StreamNo);
                }
                else if (!stream.MainStream.IsEnabled)
                {
                    result.AddWarning(
                        "Main Stream이 비활성화되어 있습니다. StreamNo=" + stream.StreamNo);
                }

                if (stream.SubStream == null)
                {
                    result.AddError(
                        "Sub Stream 설정이 없습니다. StreamNo=" + stream.StreamNo);
                }
                else if (!stream.SubStream.IsEnabled)
                {
                    result.AddWarning(
                        "Sub Stream이 비활성화되어 있습니다. StreamNo=" + stream.StreamNo);
                }
            }

            /*
             * Stream0이 꺼져 있는 것은 문제가 아니다.
             * 사용 체크된 Stream이 하나도 없을 때만 경고한다.
             */
            if (enabledStreamCount == 0)
            {
                result.AddWarning(
                    "사용 체크된 모니터가 없습니다. 자동 송출이 시작되지 않습니다.");
            }

            if (config.RtspServer == null)
            {
                result.AddError("RTSP 서버 설정이 없습니다.");
                return;
            }

            if (config.RtspServer.RtspPort <= 0 ||
                config.RtspServer.RtspPort > 65535)
            {
                result.AddError("RTSP 포트 값이 올바르지 않습니다.");
            }

            if (config.Auth == null ||
                string.IsNullOrWhiteSpace(config.Auth.DeviceName))
            {
                result.AddWarning("장비명이 비어 있습니다.");
            }
        }

        /// <summary>
        /// RTSP 포트가 현재 사용 가능한지 검사한다.
        /// 
        /// RTSP 포트가 이미 사용 중이면 MediaMTX가 실행될 수 없으므로,
        /// 이 항목은 경고가 아니라 실행 차단 오류로 처리한다.
        /// </summary>
        /// <param name="config">현재 설정.</param>
        /// <param name="result">검사 결과 객체.</param>
        private void ValidateRtspPort(
            AppConfig config,
            StartupValidationResult result)
        {
            if (config == null || config.RtspServer == null)
                return;

            int port = config.RtspServer.RtspPort;

            if (port <= 0 || port > 65535)
                return;

            if (!IsTcpPortAvailable(port))
            {
                result.AddError(
                    "RTSP 포트가 이미 사용 중입니다. Port=" +
                    port +
                    ". 기존 잔류 프로세스 또는 다른 프로그램이 사용 중일 수 있습니다.");
            }
        }

        /// <summary>
        /// ONVIF HTTP 포트가 현재 사용 가능한지 검사한다.
        /// 
        /// ONVIF 수동 등록 방식에서는 NVR이 PC CAM의 ONVIF HTTP 서버에 접근해야 하므로,
        /// ONVIF 포트가 이미 사용 중이면 NVR 등록과 GetStreamUri 요청이 실패할 수 있다.
        /// 
        /// 현재 구현에서는 Stream0의 OnvifPort를 ONVIF HTTP 서버 포트로 사용한다.
        /// </summary>
        /// <param name="config">현재 설정.</param>
        /// <param name="result">검사 결과 객체.</param>
        private void ValidateOnvifPort(
            AppConfig config,
            StartupValidationResult result)
        {
            if (config == null)
                return;

            int onvifPort = ResolveOnvifPort(config);

            if (onvifPort <= 0 || onvifPort > 65535)
            {
                result.AddError(
                    "ONVIF 포트 값이 올바르지 않습니다. Port=" +
                    onvifPort);

                return;
            }

            if (config.RtspServer != null &&
                config.RtspServer.RtspPort == onvifPort)
            {
                result.AddError(
                    "ONVIF 포트와 RTSP 포트가 동일합니다. " +
                    "ONVIF Port=" + onvifPort +
                    ", RTSP Port=" + config.RtspServer.RtspPort);

                return;
            }

            if (!IsTcpPortAvailable(onvifPort))
            {
                result.AddError(
                    "ONVIF 포트가 이미 사용 중입니다. Port=" +
                    onvifPort +
                    ". 다른 프로그램이 사용 중이거나 기존 ONVIF 서버 프로세스가 남아 있을 수 있습니다.");
            }
        }

        /// <summary>
        /// 설정에서 ONVIF HTTP 포트를 결정한다.
        /// 
        /// 현재 PC CAM 2단계 구현에서는 Stream0의 OnvifPort를 ONVIF HTTP 서버 포트로 사용한다.
        /// 값이 없거나 잘못된 경우 기본값 8080을 반환한다.
        /// </summary>
        /// <param name="config">현재 설정.</param>
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
        /// 지정한 TCP 포트를 바인딩할 수 있는지 확인한다.
        /// </summary>
        /// <param name="port">검사할 TCP 포트.</param>
        /// <returns>true이면 사용 가능, false이면 이미 사용 중.</returns>
        private bool IsTcpPortAvailable(int port)
        {
            TcpListener listener = null;

            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (listener != null)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// 시작 환경 검사 결과를 로그에 기록한다.
        /// </summary>
        /// <param name="result">검사 결과 객체.</param>
        private void WriteValidationLog(StartupValidationResult result)
        {
            if (result == null)
                return;

            if (result.IsValid && !result.HasWarnings)
            {
                _logService.WriteApp("시작 환경 검사 완료: 정상");
                return;
            }

            if (!result.IsValid)
            {
                _logService.WriteError(
                    "시작 환경 검사 오류\r\n" +
                    result.ToDisplayMessage());
            }

            if (result.HasWarnings)
            {
                _logService.WriteApp(
                    "시작 환경 검사 경고\r\n" +
                    result.ToDisplayMessage());
            }
        }
    }
}