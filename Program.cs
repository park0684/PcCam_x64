using PcCam_x64.Infrastructure;
using PcCam_x64.Models;
using PcCam_x64.Presenters;
using PcCam_x64.Services;
using PcCam_x64.Views;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PcCam_x64
{
    static class Program
    {
        /// <summary>
        /// PC CAM 프로그램 진입점.
        /// 
        /// 처리 순서:
        /// 1. WinForms 기본 설정 초기화
        /// 2. 중복 실행 방지 Mutex 획득
        /// 3. 설정/로그 서비스 생성
        /// 4. 이전 실행에서 남은 FFmpeg / MediaMTX 프로세스 정리
        /// 5. 시작 환경 검사
        /// 6. 모니터, 인증, 전원 정책, 자동실행, 방화벽 서비스 생성
        /// 7. 실행 중 재인증 감시 서비스 생성
        /// 8. ONVIF SOAP / HTTP 서버 서비스 생성
        /// 9. MediaMTX / FFmpeg 실행 서비스 생성
        /// 10. StreamSupervisorService 생성
        /// 11. 트레이 View / Presenter 연결
        /// 12. 트레이 상주 프로그램 실행
        /// 13. 종료 시 외부 프로세스와 운영 정책 정리
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            SingleInstanceGuard singleInstanceGuard = null;

            PathProvider pathProvider = null;
            ConfigService configService = null;
            LogService logService = null;

            ExternalProcessCleanupService processCleanupService = null;
            StartupValidationService startupValidationService = null;

            MonitorService monitorService = null;
            FfmpegCommandBuilder commandBuilder = null;
            GlobalPointerInputService pointerInputService = null;
            AuthDllAdapter authDllAdapter = null;
            RuntimeAuthMonitorService runtimeAuthMonitorService = null;

            PowerPolicyService powerPolicyService = null;
            AutoStartService autoStartService = null;
            FirewallService firewallService = null;

            OnvifSoapResponseBuilder onvifSoapResponseBuilder = null;
            OnvifRequestDispatcher onvifRequestDispatcher = null;
            OnvifHttpServerService onvifHttpServerService = null;
            OnvifDiscoveryService onvifDiscoveryService = null;

            RtspServerService rtspServerService = null;
            FfmpegService ffmpegService = null;
            StreamSupervisorService supervisor = null;

            TrayApplicationContext trayView = null;
            TrayPresenter trayPresenter = null;
            OnvifDeviceIdentityService onvifDeviceIdentityService = null;
            try
            {
                /*
                 * PC CAM은 RTSP 포트와 화면 캡처 프로세스를 사용하므로
                 * 중복 실행을 허용하지 않는다.
                 */
                singleInstanceGuard = new SingleInstanceGuard();

                if (!singleInstanceGuard.TryAcquire())
                {
                    MessageBox.Show(
                        "PC CAM이 이미 실행 중입니다.",
                        "PC CAM",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    return;
                }

                /*
                 * 기본 서비스 생성.
                 * LogService는 다른 서비스에서 사용되므로 초반에 생성한다.
                 */
                pathProvider = new PathProvider();
                monitorService = new MonitorService();
                configService = new ConfigService(pathProvider, monitorService);
                logService = new LogService(pathProvider);

                logService.WriteApp("PC CAM 프로그램 시작");

                /*
                 * UI 스레드에서 처리되지 않은 예외를 로그에 기록한다.
                 */
                Application.ThreadException += delegate (object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    try
                    {
                        if (logService != null)
                            logService.WriteException("UI 스레드 예외", e.Exception);
                    }
                    catch
                    {
                        // 예외 로그 기록 실패가 프로그램을 다시 중단시키면 안 되므로 무시한다.
                    }
                };

                /*
                 * UI 스레드 외부에서 처리되지 않은 예외를 로그에 기록한다.
                 */
                AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
                {
                    try
                    {
                        if (logService == null)
                            return;

                        Exception ex = e.ExceptionObject as Exception;

                        if (ex != null)
                        {
                            logService.WriteException("처리되지 않은 예외", ex);
                        }
                        else
                        {
                            logService.WriteError("처리되지 않은 예외: " + Convert.ToString(e.ExceptionObject));
                        }
                    }
                    catch
                    {
                        // 예외 로그 기록 실패는 무시한다.
                    }
                };

                /*
                 * 설정 로드.
                 * 설정 파일이 없으면 ConfigService에서 기본 INI 파일을 생성한다.
                 */
                AppConfig config = configService.Load();

                /*
                 * 현재 PC의 모니터 수에 맞춰 StreamConfig를 보정한다.
                 * 
                 * 예:
                 * 모니터 1대 → Stream0
                 * 모니터 2대 → Stream0, Stream1
                 * 모니터 3대 → Stream0, Stream1, Stream2
                 * 
                 * 기존 StreamConfig는 삭제하지 않고,
                 * 부족한 StreamConfig만 추가한다.
                 */
                List<string> screenNames = new List<string>();

                /*
                 * Windows의 DISPLAY 번호와 주 모니터 순서는 다를 수 있다.
                 * 예:
                 * 주 모니터가 \\.\DISPLAY2
                 * 보조 모니터가 \\.\DISPLAY1
                 * 
                 * 따라서 Stream0은 Screen.AllScreens[0]이 아니라
                 * Primary 모니터를 우선으로 생성해야 한다.
                 */
                Screen primaryScreen = Screen.PrimaryScreen;

                if (primaryScreen != null &&
                    !string.IsNullOrWhiteSpace(primaryScreen.DeviceName))
                {
                    screenNames.Add(primaryScreen.DeviceName);
                }

                /*
                 * Primary를 제외한 나머지 모니터를 뒤에 추가한다.
                 */
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(screen.DeviceName))
                        continue;

                    bool isPrimary =
                        primaryScreen != null &&
                        string.Equals(
                            screen.DeviceName,
                            primaryScreen.DeviceName,
                            StringComparison.OrdinalIgnoreCase);

                    if (isPrimary)
                        continue;

                    screenNames.Add(screen.DeviceName);
                }

                int beforeStreamCount = config.Streams == null
                    ? 0
                    : config.Streams.Count;

                AppConfig.EnsureStreamsForMonitorNames(config, screenNames);

                int afterStreamCount = config.Streams == null
                    ? 0
                    : config.Streams.Count;

                if (afterStreamCount != beforeStreamCount)
                {
                    configService.Save(config);

                    logService.WriteApp(
                        "모니터 목록 기준 스트림 설정 보정 완료. MonitorCount=" +
                        screenNames.Count +
                        ", BeforeStreams=" +
                        beforeStreamCount +
                        ", AfterStreams=" +
                        afterStreamCount);
                }

                /*
                 * 이전 실행이 비정상 종료되었을 때 남아 있을 수 있는
                 * FFmpeg / MediaMTX 프로세스를 정리한다.
                 * 
                 * 단, 우리 External 폴더에서 실행된 프로세스만 종료한다.
                 */
                processCleanupService = new ExternalProcessCleanupService(
                    pathProvider,
                    logService);

                processCleanupService.CleanupLeftoverProcesses(config);

                /*
                 * 시작 환경 검사.
                 * 
                 * 필수 파일 누락 또는 RTSP 포트 충돌 같은 치명 오류가 있으면
                 * 트레이 프로그램을 실행하지 않고 종료한다.
                 */
                startupValidationService = new StartupValidationService(
                    pathProvider,
                    logService);

                StartupValidationResult validationResult =
                    startupValidationService.Validate(config);

                if (!validationResult.IsValid)
                {
                    MessageBox.Show(
                        "PC CAM 실행에 필요한 환경이 준비되지 않았습니다.\r\n\r\n" +
                        validationResult.ToDisplayMessage() +
                        "\r\n필수 항목을 확인한 뒤 다시 실행하세요.",
                        "PC CAM 시작 오류",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    logService.WriteError(
                        "PC CAM 시작 차단\r\n" +
                        validationResult.ToDisplayMessage());

                    return;
                }

                if (validationResult.HasWarnings)
                {
                    MessageBox.Show(
                        "PC CAM 실행은 가능하지만 확인이 필요한 항목이 있습니다.\r\n\r\n" +
                        validationResult.ToDisplayMessage(),
                        "PC CAM 시작 경고",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    logService.WriteApp(
                        "PC CAM 시작 경고\r\n" +
                        validationResult.ToDisplayMessage());
                }

                /*
                 * 기능 서비스 생성.
                 */
                monitorService = new MonitorService();
                commandBuilder = new FfmpegCommandBuilder();

                /*
                 * 전역 포인터 입력 감지 POC.
                 *
                 * PCCAM_POINTER_INPUT_POC=1인 경우에만 후크를 등록한다.
                 * 일반 운영 실행에는 영향을 주지 않으며,
                 * 터치 드라이버가 Touch 서명을 전달하는지 로그로 확인한다.
                 */
                string pointerInputPocValue =
                    Environment.GetEnvironmentVariable(
                        "PCCAM_POINTER_INPUT_POC");

                if (string.Equals(
                    pointerInputPocValue == null
                        ? ""
                        : pointerInputPocValue.Trim(),
                    "1",
                    StringComparison.Ordinal))
                {
                    pointerInputService =
                        new GlobalPointerInputService();

                    pointerInputService.PointerPressed +=
                        delegate (PointerSample sample)
                        {
                            try
                            {
                                if (logService == null || sample == null)
                                    return;

                                string inputType = sample.IsTouch
                                    ? "Touch"
                                    : "MouseOrPen";

                                logService.WriteApp(
                                    "[PointerInput] " +
                                    inputType +
                                    " Down X=" + sample.ScreenX +
                                    ", Y=" + sample.ScreenY +
                                    ", ExtraInfo=0x" +
                                    sample.ExtraInfo.ToString("X8"));
                            }
                            catch
                            {
                                // 입력 진단 로그 실패가 후크 동작을 중단시키면 안 된다.
                            }
                        };

                    pointerInputService.Start();

                    logService.WriteApp(
                        "[PointerInput] 전역 입력 감지 POC 시작");
                }

                /*
                 * 인증 DLL 어댑터.
                 * PccAuthClient.dll을 호출하여 인증 등록, 시작 인증, 실행 중 재인증을 처리한다.
                 */
                authDllAdapter = new AuthDllAdapter();

                /*
                 * 실행 중 재인증 감시 서비스.
                 * 
                 * 중요:
                 * RuntimeAuthMonitorService는 AuthDllAdapter와 LogService만 필요하다.
                 * StreamSupervisorService보다 먼저 생성해야 한다.
                 */
                runtimeAuthMonitorService = new RuntimeAuthMonitorService(
                    authDllAdapter,
                    logService);

                powerPolicyService = new PowerPolicyService();
                autoStartService = new AutoStartService();
                firewallService = new FirewallService(logService);
                /*
                 * ONVIF 가상 장치 식별정보 생성 서비스.
                 *
                 * InstallationId와 StreamNo를 기준으로
                 * Endpoint UUID, SerialNumber, HardwareId,
                 * 가상 MAC 주소를 고정 생성한다.
                 */
                onvifDeviceIdentityService =
                    new OnvifDeviceIdentityService();

                /*
                 * ONVIF SOAP 응답 생성 서비스.
                 */
                onvifSoapResponseBuilder =
                    new OnvifSoapResponseBuilder(
                        onvifDeviceIdentityService);

                /*
                 * ONVIF 요청 분기 서비스.
                 */
                onvifRequestDispatcher =
                    new OnvifRequestDispatcher(
                        onvifSoapResponseBuilder);

                /*
                 * ONVIF HTTP 서버.
                 */
                onvifHttpServerService =
                    new OnvifHttpServerService(
                        onvifRequestDispatcher,
                        logService);

                /*
                 * ONVIF WS-Discovery 서비스.
                 */
                onvifDiscoveryService =
                    new OnvifDiscoveryService(
                        logService,
                        onvifDeviceIdentityService);

                /*
                 * 외부 프로세스 실행 서비스.
                 *
                 * 중요:
                 * StreamSupervisorService를 생성하기 전에
                 * 반드시 MediaMTX와 FFmpeg 서비스를 생성해야 한다.
                 */
                rtspServerService =
                    new RtspServerService(
                        pathProvider);

                ffmpegService =
                    new FfmpegService(
                        pathProvider,
                        commandBuilder);

                /*
                 * 송출 통합 제어 서비스.
                 *
                 * 위에서 생성한 서비스들이 모두 null이 아닌 상태에서
                 * StreamSupervisorService에 전달되어야 한다.
                 */
                supervisor =
                    new StreamSupervisorService(
                        pathProvider,
                        monitorService,
                        rtspServerService,
                        ffmpegService,
                        authDllAdapter,
                        onvifHttpServerService,
                        onvifDiscoveryService,
                        runtimeAuthMonitorService);

                /*
                 * Supervisor에서 발생하는 로그를 파일 로그로 연결한다.
                 * LogService가 로그 내용을 보고 FFmpeg / MediaMTX / Stream / Auth / Error로 분류한다.
                 */
                supervisor.LogReceived += delegate (string line)
                {
                    try
                    {
                        if (logService != null)
                            logService.WriteSupervisorLog(line);
                    }
                    catch
                    {
                        // 로그 기록 실패가 송출 실행을 막으면 안 되므로 무시한다.
                    }
                };

                /*
                 * 송출 상태 변경 로그를 별도로 기록한다.
                 */
                supervisor.StatusChanged += delegate (StreamRuntimeStatus status)
                {
                    try
                    {
                        if (logService != null)
                            logService.WriteStream("상태 변경: " + status);
                    }
                    catch
                    {
                        // 로그 기록 실패는 무시한다.
                    }
                };

                /*
                 * 트레이 View / Presenter 연결.
                 */
                trayView = new TrayApplicationContext();

                trayPresenter = new TrayPresenter(
                    trayView,
                    pathProvider,
                    configService,
                    supervisor,
                    authDllAdapter,
                    powerPolicyService,
                    autoStartService,
                    firewallService,
                    logService);

                /*
                 * 트레이 상주 프로그램 실행.
                 */
                Application.Run(trayView);
            }
            catch (Exception ex)
            {
                try
                {
                    if (logService != null)
                        logService.WriteException("프로그램 시작 중 오류", ex);
                }
                catch
                {
                }

                MessageBox.Show(
                    ex.ToString(),
                    "PC CAM 실행 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    if (logService != null)
                        logService.WriteApp("PC CAM 프로그램 종료 시작");
                }
                catch
                {
                }

                /*
                 * 전역 포인터 입력 후크를 다른 서비스보다 먼저 해제한다.
                 */
                try
                {
                    if (pointerInputService != null)
                        pointerInputService.Dispose();
                }
                catch
                {
                }
                /*
                 * Presenter 먼저 해제한다.
                 * 이벤트 연결을 먼저 끊어야 종료 중 불필요한 UI 호출을 줄일 수 있다.
                 */
                try
                {
                    if (trayPresenter != null)
                        trayPresenter.Dispose();
                }
                catch
                {
                }

                /*
                 * 송출 통합 서비스를 정리한다.
                 * 내부적으로 FFmpeg / ONVIF HTTP 서버 / MediaMTX 중지 요청이 수행된다.
                 * 
                 * supervisor.Dispose() 안에서 RuntimeAuthMonitorService.Stop()이 호출된다.
                 */
                try
                {
                    if (supervisor != null)
                        supervisor.Dispose();
                }
                catch
                {
                }

                /*
                 * 실행 중 재인증 감시 서비스 정리.
                 * supervisor.Dispose()에서 Stop이 호출되더라도 Dispose는 한 번 더 호출해도 문제 없게 구성되어야 한다.
                 */
                try
                {
                    if (runtimeAuthMonitorService != null)
                        runtimeAuthMonitorService.Dispose();
                }
                catch
                {
                }

                /*
                 * FFmpeg 서비스 정리.
                 */
                try
                {
                    if (ffmpegService != null)
                        ffmpegService.Dispose();
                }
                catch
                {
                }

                /*
                 * ONVIF HTTP 서버 정리.
                 * supervisor.Dispose()에서 이미 중지되었더라도 한 번 더 정리해도 문제 없다.
                 */
                try
                {
                    if (onvifHttpServerService != null)
                        onvifHttpServerService.Dispose();
                }
                catch
                {
                }

                /*
                 * ONVIF 디스커버리 서비스 정리.
                 */
                try
                {
                    if (onvifDiscoveryService != null)
                        onvifDiscoveryService.Dispose();
                }
                catch
                {
                }

                /*
                 * RTSP 서버 서비스 정리.
                 */
                try
                {
                    if (rtspServerService != null)
                        rtspServerService.Dispose();
                }
                catch
                {
                }

                /*
                 * 프로그램 종료 시 절전모드 방지 요청을 해제한다.
                 */
                try
                {
                    if (powerPolicyService != null)
                        powerPolicyService.DisablePreventSleep();
                }
                catch
                {
                }

                try
                {
                    if (logService != null)
                        logService.WriteApp("PC CAM 프로그램 종료 완료");
                }
                catch
                {
                }

                /*
                 * 중복 실행 방지 Mutex 해제.
                 */
                try
                {
                    if (singleInstanceGuard != null)
                        singleInstanceGuard.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}