using System;
using PcCam_x64.Infrastructure;
using PcCam_x64.Models;
using PcCam_x64.Services;
using PcCam_x64.Views;

namespace PcCam_x64.Presenters
{
    /// <summary>
    /// 시스템 트레이 Presenter.
    /// 
    /// 트레이 메뉴 정책:
    /// - 송출 시작/중지/재시작 메뉴는 제공하지 않는다.
    /// - 사용자가 임의로 송출 상태를 변경할 수 없도록 한다.
    /// - 트레이에는 인증상태, 설정, 종료만 표시한다.
    /// - 설정/종료 실행 시 날짜 기반 확인 코드를 입력해야 한다.
    /// 
    /// 확인 코드 규칙:
    /// - 실행 날짜 기준 일/월/년도 순서
    /// - 예: 2026년 5월 28일 → 28052026
    /// </summary>
    public class TrayPresenter : IDisposable
    {
        private readonly ITrayView _view;
        private readonly PathProvider _pathProvider;
        private readonly ConfigService _configService;
        private readonly StreamSupervisorService _supervisor;
        private readonly AuthDllAdapter _authDllAdapter;
        private readonly PowerPolicyService _powerPolicyService;
        private readonly AutoStartService _autoStartService;
        private readonly FirewallService _firewallService;
        private readonly LogService _logService;

        private AppConfig _config;
        private SettingView _settingView;
        private SettingPresenter _settingPresenter;
        private bool _disposed;

        /// <summary>
        /// TrayPresenter를 생성한다.
        /// </summary>
        public TrayPresenter(
            ITrayView view,
            PathProvider pathProvider,
            ConfigService configService,
            StreamSupervisorService supervisor,
            AuthDllAdapter authDllAdapter,
            PowerPolicyService powerPolicyService,
            AutoStartService autoStartService,
            FirewallService firewallService,
            LogService logService)
        {
            if (view == null)
                throw new ArgumentNullException("view");

            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            if (configService == null)
                throw new ArgumentNullException("configService");

            if (supervisor == null)
                throw new ArgumentNullException("supervisor");

            if (authDllAdapter == null)
                throw new ArgumentNullException("authDllAdapter");

            if (powerPolicyService == null)
                throw new ArgumentNullException("powerPolicyService");

            if (autoStartService == null)
                throw new ArgumentNullException("autoStartService");

            if (firewallService == null)
                throw new ArgumentNullException("firewallService");

            if (logService == null)
                throw new ArgumentNullException("logService");

            _view = view;
            _pathProvider = pathProvider;
            _configService = configService;
            _supervisor = supervisor;
            _authDllAdapter = authDllAdapter;
            _powerPolicyService = powerPolicyService;
            _autoStartService = autoStartService;
            _firewallService = firewallService;
            _logService = logService;

            _view.SettingRequested += OnSettingRequested;
            _view.ExitRequested += OnExitRequested;

            _supervisor.StatusChanged += OnStatusChanged;
            _supervisor.LogReceived += OnLogReceived;

            Initialize();
        }

        /// <summary>
        /// 트레이 Presenter를 초기화한다.
        /// 
        /// 처리 순서:
        /// 1. 설정 파일 로드
        /// 2. 트레이 기본 상태 표시
        /// 3. 운영 정책 적용
        /// 4. 로컬 인증 등록 상태 표시
        /// 5. 로컬 인증정보가 있고 자동 송출 설정이 켜져 있으면 송출 시작 요청
        /// 
        /// 주의:
        /// 이 단계에서는 인증서버 Verify를 호출하지 않는다.
        /// 실제 서버 인증은 StreamSupervisorService.Start()에서 송출 시작 직전에 1회만 수행한다.
        /// </summary>
        private void Initialize()
        {
            try
            {
                _logService.WriteApp("TrayPresenter 초기화 시작");

                _config = _configService.Load();

                _view.SetStatusText("PC CAM");
                _view.SetAuthStatusText("인증 확인 중");

                ApplyOperationPolicy();

                /*
                 * 서버 인증이 아니라 로컬 인증 등록 여부만 확인한다.
                 */
                RefreshAuthStatus();

                bool hasLocalAuth =
                    _config != null &&
                    _config.Auth != null &&
                    _authDllAdapter.HasLocalAuth(_config.Auth);

                bool autoStartStreaming =
                    hasLocalAuth &&
                    _config.Operation != null &&
                    _config.Operation.AutoStartStreaming;

                /*
                 * 자동 송출이 꺼져 있어도 프로그램 시작 시 인증 감시는 시작한다.
                 * 자동 송출이 켜져 있으면 StartStreamingInternal() → StreamSupervisorService.Start()
                 * 흐름에서 인증 확인과 감시 시작이 수행되므로 여기서 중복 호출하지 않는다.
                 */
                if (hasLocalAuth && !autoStartStreaming)
                {
                    _supervisor.StartAuthMonitoring(_config);
                }

                if (autoStartStreaming)
                {
                    _logService.WriteStream("자동 송출 설정 감지. 내부 송출 시작");
                    StartStreamingInternal();
                }

                _logService.WriteApp("TrayPresenter 초기화 완료");
            }
            catch (Exception ex)
            {
                _view.SetAuthStatusText("인증 오류");
                _view.SetStatusText("PC CAM - 오류");
                _view.ShowError("프로그램 초기화 중 오류가 발생했습니다.\r\n" + ex.Message);

                _logService.WriteException("TrayPresenter 초기화 오류", ex);
            }
        }

        /// <summary>
        /// 설정 메뉴 클릭 이벤트를 처리한다.
        /// 
        /// 설정 화면 접근은 현장 사용자가 임의로 실행하지 못하도록
        /// 날짜 기반 확인 코드 입력 후에만 허용한다.
        /// </summary>
        private void OnSettingRequested(object sender, EventArgs e)
        {
            _logService.WriteApp("설정 메뉴 클릭");

            if (!VerifyDailyPassword("설정"))
            {
                _logService.WriteApp("설정 접근 차단");
                return;
            }

            _logService.WriteApp("설정 접근 허용");

            if (_settingView != null && !_settingView.IsDisposed)
            {
                _settingView.ShowForm();
                return;
            }

            _settingView = new SettingView();

            _settingPresenter = new SettingPresenter(
                _settingView,
                _configService,
                _authDllAdapter,
                _supervisor,
                _autoStartService,
                _firewallService);

            _settingView.FormClosed += delegate
            {
                OnSettingFormClosed();
            };

            _settingView.ShowForm();

            _logService.WriteApp("설정 화면 표시");
        }

        /// <summary>
        /// 설정 화면 종료 후 후처리를 수행한다.
        /// 
        /// 처리 내용:
        /// 1. SettingPresenter 해제
        /// 2. 설정 화면 참조 제거
        /// 3. 설정 파일 재로드
        /// 4. 인증 상태 갱신
        /// 5. 운영 정책 재적용
        /// </summary>
        private void OnSettingFormClosed()
        {
            try
            {
                if (_settingPresenter != null)
                {
                    _settingPresenter.Dispose();
                    _settingPresenter = null;
                }

                _settingView = null;

                _config = _configService.Load();

                RefreshAuthStatus();
                ApplyOperationPolicy();

                _logService.WriteApp("설정 화면 종료 및 설정 재적용 완료");
            }
            catch (Exception ex)
            {
                _logService.WriteException("설정 화면 종료 후처리 오류", ex);
            }
        }

        /// <summary>
        /// 종료 메뉴 클릭 이벤트를 처리한다.
        /// 
        /// 종료 역시 임의 조작을 방지하기 위해 날짜 기반 확인 코드를 요구한다.
        /// 확인 성공 시 송출을 중지하고 프로그램을 종료한다.
        /// </summary>
        private void OnExitRequested(object sender, EventArgs e)
        {
            _logService.WriteApp("종료 메뉴 클릭");

            if (!VerifyDailyPassword("종료"))
            {
                _logService.WriteApp("종료 요청 차단");
                return;
            }

            _logService.WriteApp("종료 요청 허용");
            try
            {
                _supervisor.StopAuthMonitoring();
            }
            catch
            { 
            }
            try
            {
                _supervisor.Stop();
            }
            catch (Exception ex)
            {
                _logService.WriteException("종료 중 송출 중지 오류", ex);
            }

            _view.CloseView();
        }

        /// <summary>
        /// 날짜 기반 확인 코드를 검증한다.
        /// 
        /// 규칙:
        /// DateTime.Now.ToString("ddMMyyyy")
        /// 
        /// 예:
        /// 2026-05-28 → 28052026
        /// </summary>
        /// <param name="actionName">
        /// 확인 대상 동작명.
        /// 예: 설정, 종료
        /// </param>
        /// <returns>
        /// true: 확인 코드 일치
        /// false: 취소 또는 확인 코드 불일치
        /// </returns>
        private bool VerifyDailyPassword(string actionName)
        {
            string expectedPassword = DateTime.Now.ToString("ddMMyyyy");

            string input = _view.PromptPassword(
                actionName + " 확인",
                actionName + "을 실행하려면 확인 코드를 입력하세요.");

            if (string.IsNullOrWhiteSpace(input))
            {
                _logService.WriteApp(actionName + " 확인 코드 입력 취소");
                return false;
            }

            input = input.Trim();

            if (input == expectedPassword)
            {
                _logService.WriteApp(actionName + " 확인 코드 검증 성공");
                return true;
            }

            _logService.WriteApp(actionName + " 확인 코드 검증 실패");
            _view.ShowError("확인 코드가 올바르지 않습니다.");

            return false;
        }

        /// <summary>
        /// 설정 파일의 운영 정책을 프로그램에 적용한다.
        /// 
        /// 적용 대상:
        /// 1. 절전모드 해제 여부
        /// 2. Windows 시작 시 자동실행 등록 여부
        /// 3. Windows 방화벽 포트 허용 규칙
        /// 
        /// 각 정책은 독립적으로 적용한다.
        /// 하나의 정책 적용이 실패해도 다른 정책 적용과 프로그램 실행은 계속 유지한다.
        /// </summary>
        private void ApplyOperationPolicy()
        {

            if (_config == null || _config.Operation == null)
                return;

            ApplyPowerPolicy();
            ApplyAutoStartPolicy();
            ApplyFirewallPolicy();
        }

        /// <summary>
        /// 절전모드 방지 정책을 적용한다.
        /// 
        /// Operation.PreventSleep 값이 true이면 시스템 절전 및 화면 꺼짐을 방지하고,
        /// false이면 기존 절전 방지 요청을 해제한다.
        /// </summary>
        private void ApplyPowerPolicy()
        {
            try
            {
                _powerPolicyService.Apply(_config.Operation.PreventSleep);

                _logService.WriteApp(
                    "절전모드 정책 적용 완료. PreventSleep=" +
                    _config.Operation.PreventSleep);
            }
            catch (Exception ex)
            {
                _logService.WriteException("절전모드 정책 적용 오류", ex);
            }
        }

        /// <summary>
        /// Windows 자동실행 정책을 적용한다.
        /// 
        /// Operation.AutoStart 값이 true이면 HKCU Run 레지스트리에 자동실행을 등록하고,
        /// false이면 기존 자동실행 등록을 해제한다.
        /// </summary>
        private void ApplyAutoStartPolicy()
        {
            try
            {
                _autoStartService.Apply(_config.Operation.AutoStart);

                _logService.WriteApp(
                    "자동실행 정책 적용 완료. AutoStart=" +
                    _config.Operation.AutoStart);
            }
            catch (Exception ex)
            {
                _logService.WriteException("자동실행 정책 적용 오류", ex);
            }
        }

        /// <summary>
        /// Windows 방화벽 정책을 적용한다.
        /// 
        /// RTSP 포트와 ONVIF 포트를 인바운드 허용 규칙으로 등록한다.
        /// 관리자 권한이 없으면 FirewallService 내부에서 로그만 남기고 등록을 생략한다.
        /// </summary>
        private void ApplyFirewallPolicy()
        {
            try
            {
                if (_config == null || _config.Operation == null)
                    return;

                string pccExePath = _pathProvider.AppExePath;
                string mideaMtxExePath = _pathProvider.GetExternalFilePath(_config.RtspServer.ServerExeName);
                _firewallService.ApplyProgramRules(pccExePath, mideaMtxExePath);                

                _logService.WriteApp("방화벽 정책 적용 완료");
            }
            catch (Exception ex)
            {
                _logService.WriteException("방화벽 정책 적용 오류", ex);
            }
        }

        /// <summary>
        /// 현재 로컬 인증 등록 상태를 확인하고 트레이 인증상태 표시를 갱신한다.
        /// 
        /// 이 메서드는 인증서버 Verify를 호출하지 않는다.
        /// 실제 실행 가능 여부 검증은 송출 시작 시 StreamSupervisorService.Start()에서 수행한다.
        /// </summary>
        private void RefreshAuthStatus()
        {
            try
            {
                if (_config == null)
                    _config = _configService.Load();

                bool hasLocalAuth =
                    _config != null &&
                    _config.Auth != null &&
                    _authDllAdapter.HasLocalAuth(_config.Auth);

                if (hasLocalAuth)
                {
                    _view.SetAuthStatusText("인증됨");
                    _logService.WriteAuth("인증 상태 확인: 로컬 인증정보 있음");
                }
                else
                {
                    _view.SetAuthStatusText("미인증");
                    _logService.WriteAuth("인증 상태 확인: 로컬 인증정보 없음");
                }
            }
            catch (Exception ex)
            {
                _view.SetAuthStatusText("인증 오류");
                _logService.WriteException("인증 상태 확인 오류", ex);
            }
        }

        /// <summary>
        /// 내부 송출 시작을 수행한다.
        /// 
        /// 사용자가 트레이에서 직접 호출하지 않는다.
        /// 프로그램 시작 시 자동 송출 설정이 켜져 있을 때만 사용한다.
        /// </summary>
        private void StartStreamingInternal()
        {
            try
            {
                _config = _configService.Load();

                _supervisor.Start(_config);

                _logService.WriteStream("내부 송출 시작 요청 완료");
            }
            catch (Exception ex)
            {
                _view.SetStatusText("PC CAM - 송출 오류");
                _view.ShowError("자동 송출 시작 실패\r\n" + ex.Message);

                _logService.WriteException("자동 송출 시작 실패", ex);
            }
        }

        /// <summary>
        /// 송출 상태 변경 이벤트를 처리한다.
        /// 
        /// 인증상태 메뉴는 인증 여부만 표시해야 하므로,
        /// Running/Stopped 같은 송출 상태는 인증상태 메뉴에 반영하지 않는다.
        /// 송출 상태는 트레이 툴팁과 로그에만 반영한다.
        /// </summary>
        /// <param name="status">
        /// 변경된 송출 상태.
        /// </param>
        private void OnStatusChanged(StreamRuntimeStatus status)
        {
            _logService.WriteStream("트레이 상태 반영: " + status);

            switch (status)
            {
                case StreamRuntimeStatus.Starting:
                    _view.SetStatusText("PC CAM - 송출 시작 중");
                    break;

                case StreamRuntimeStatus.Running:
                    _view.SetStatusText("PC CAM - 송출 중");
                    break;

                case StreamRuntimeStatus.Stopping:
                    _view.SetStatusText("PC CAM - 송출 중지 중");
                    break;

                case StreamRuntimeStatus.Stopped:
                    _view.SetStatusText("PC CAM - 중지");
                    break;

                case StreamRuntimeStatus.Unauthorized:
                    _view.SetStatusText("PC CAM - 인증 필요");
                    _view.SetAuthStatusText("미인증");
                    break;

                case StreamRuntimeStatus.Error:
                    _view.SetStatusText("PC CAM - 오류");
                    break;
            }
        }

        /// <summary>
        /// Supervisor 로그 수신 이벤트를 처리한다.
        /// 
        /// Program.cs에서도 Supervisor 로그를 파일에 기록하고 있으므로,
        /// 이곳에서는 별도 처리를 하지 않는다.
        /// 향후 트레이 상태 표시나 알림이 필요할 경우 이 메서드에서 확장한다.
        /// </summary>
        /// <param name="line">
        /// Supervisor에서 전달된 로그 메시지.
        /// </param>
        private void OnLogReceived(string line)
        {
            // 현재 단계에서는 별도 UI 표시 없음.
        }

        /// <summary>
        /// Presenter에서 연결한 이벤트를 해제한다.
        /// 
        /// 프로그램 종료 또는 트레이 View 종료 시 호출된다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _view.SettingRequested -= OnSettingRequested;
                _view.ExitRequested -= OnExitRequested;

                _supervisor.StatusChanged -= OnStatusChanged;
                _supervisor.LogReceived -= OnLogReceived;
            }
            catch
            {
            }

            try
            {
                if (_settingPresenter != null)
                {
                    _settingPresenter.Dispose();
                    _settingPresenter = null;
                }
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}