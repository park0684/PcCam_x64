using System;
using System.Collections.Generic;
using PcCam_x64.Models;
using PcCam_x64.Services;
using PcCam_x64.Views;

namespace PcCam_x64.Presenters
{
    /// <summary>
    /// PC CAM 설정 화면 Presenter.
    /// 
    /// 역할:
    /// 1. INI 설정 파일을 로드하여 설정 화면에 표시한다.
    /// 2. 설정 화면의 입력값을 검증하고 저장한다.
    /// 3. 인증 등록/로컬 인증정보 제거 요청을 처리한다.
    /// 4. 자동실행, 방화벽, 자동 송출 정책을 저장 후 반영한다.
    /// 
    /// 주의:
    /// 인증 버튼의 등록/제거 표시는 인증키 TextBox 값이 아니라
    /// 로컬 인증 등록 완료 여부를 기준으로 판단한다.
    /// 향후 인증모듈 완료 후에는 지정된 위치의 인증토큰 존재 여부로 판단 기준을 교체한다.
    /// </summary>
    public class SettingPresenter : IDisposable
    {
        private readonly ISettingView _view;
        private readonly ConfigService _configService;
        private readonly AuthDllAdapter _authDllAdapter;
        private readonly StreamSupervisorService _supervisor;
        private readonly AutoStartService _autoStartService;
        private readonly FirewallService _firewallService;

        private AppConfig _config;
        private bool _disposed;

        /// <summary>
        /// PC CAM 설정 화면 Presenter를 생성한다.
        /// 
        /// 생성 시 설정 파일을 로드하고 View에 값을 표시한다.
        /// 또한 적용/확인/취소/인증 버튼 이벤트를 연결한다.
        /// </summary>
        /// <param name="view">설정 화면 View 인터페이스.</param>
        /// <param name="configService">INI 설정 저장/로드 서비스.</param>
        /// <param name="authDllAdapter">인증 DLL 호출 어댑터.</param>
        /// <param name="supervisor">송출 시작/중지/재시작 제어 서비스.</param>
        /// <param name="autoStartService">Windows 자동실행 등록/해제 서비스.</param>
        /// <param name="firewallService">Windows 방화벽 규칙 적용 서비스.</param>
        public SettingPresenter(
            ISettingView view,
            ConfigService configService,
            AuthDllAdapter authDllAdapter,
            StreamSupervisorService supervisor,
            AutoStartService autoStartService,
            FirewallService firewallService)
        {
            if (view == null)
                throw new ArgumentNullException("view");

            if (configService == null)
                throw new ArgumentNullException("configService");

            if (authDllAdapter == null)
                throw new ArgumentNullException("authDllAdapter");

            if (supervisor == null)
                throw new ArgumentNullException("supervisor");

            if (autoStartService == null)
                throw new ArgumentNullException("autoStartService");

            if (firewallService == null)
                throw new ArgumentNullException("firewallService");

            _view = view;
            _configService = configService;
            _authDllAdapter = authDllAdapter;
            _supervisor = supervisor;
            _autoStartService = autoStartService;
            _firewallService = firewallService;

            _view.ApplyRequested += OnApplyRequested;
            _view.OkRequested += OnOkRequested;
            _view.CancelRequested += OnCancelRequested;
            _view.AuthActionRequested += OnAuthActionRequested;

            LoadConfigToView();
        }

        /// <summary>
        /// 설정 파일과 로컬 인증 상태를 로드하여 설정 화면에 표시한다.
        /// 
        /// 인증 등록 상태이면 인증키 입력칸에는 실제 입력값이 아니라
        /// PccAuthClient.dll에서 가져온 표시용 마스킹 인증키를 보여준다.
        /// </summary>
        private void LoadConfigToView()
        {
            _config = _configService.Load();

            EnsureConfigObjects();

            _view.Streams = _config.Streams;

            _view.OnvifUserId = _config.Onvif.UserId;
            _view.OnvifPassword = _config.Onvif.Password;

            if (IsAuthRegistered())
            {
                /*
                 * 인증 등록 상태에서는 PccAuthClient.dll의 로컬 인증 상태 파일에서
                 * 등록된 인증키 전체 값을 가져와 표시한다.
                 */
                _view.LicenseKey = _authDllAdapter.GetRegisteredLicenseKey();

                string registeredDeviceName = _authDllAdapter.GetRegisteredDeviceName();

                if (!string.IsNullOrWhiteSpace(registeredDeviceName))
                    _view.DeviceName = registeredDeviceName;
                else
                    _view.DeviceName = _config.Auth.DeviceName;
            }
            else
            {
                _view.LicenseKey = _config.Auth.LicenseKey;
                _view.DeviceName = _config.Auth.DeviceName;
            }

            _view.EnableDetailLog = _config.Operation.EnableDetailLog;
            _view.AutoStart = _config.Operation.AutoStart;
            _view.PreventSleep = _config.Operation.PreventSleep;
            _view.AutoStartStreaming = _config.Operation.AutoStartStreaming;

            _view.TouchPointerEnabled = _config.TouchPointer.Enabled;

            _view.TouchPointerDiameter = _config.TouchPointer.Diameter;

            _view.TouchPointerVisibleMilliseconds = _config.TouchPointer.VisibleMilliseconds;

            RefreshAuthButtonText();
        }

        /// <summary>
        /// AppConfig의 필수 하위 설정 객체를 보정한다.
        /// 
        /// 구버전 INI 파일 또는 수동 수정된 설정 파일을 로드할 경우
        /// 일부 하위 설정 객체가 null일 수 있으므로 여기에서 기본값으로 보정한다.
        /// </summary>
        private void EnsureConfigObjects()
        {
            if (_config == null)
                _config = AppConfig.CreateDefault();

            if (_config.Streams == null || _config.Streams.Count == 0)
                _config.Streams = AppConfig.CreateDefault().Streams;

            if (_config.Onvif == null)
                _config.Onvif = new OnvifConfig();

            if (_config.Auth == null)
                _config.Auth = new AuthConfig();

            if (_config.Operation == null)
                _config.Operation = new OperationConfig();

            if (_config.TouchPointer == null)
            {
                _config.TouchPointer = new TouchPointerConfig();
            }

            _config.TouchPointer.Normalize();

            if (_config.RtspServer == null)
                _config.RtspServer = new RtspServerConfig();
        }

        /// <summary>
        /// 적용 버튼 클릭 이벤트를 처리한다.
        /// 
        /// 설정을 저장하되 설정 화면은 닫지 않는다.
        /// </summary>
        private void OnApplyRequested(object sender, EventArgs e)
        {
            SaveConfig(false);
        }

        /// <summary>
        /// 확인 버튼 클릭 이벤트를 처리한다.
        /// 
        /// 설정 저장에 성공하면 설정 화면을 닫는다.
        /// </summary>
        private void OnOkRequested(object sender, EventArgs e)
        {
            if (SaveConfig(true))
            {
                _view.CloseForm();
            }
        }

        /// <summary>
        /// 취소 버튼 클릭 이벤트를 처리한다.
        /// 
        /// 변경사항을 저장하지 않고 설정 화면을 닫는다.
        /// </summary>
        private void OnCancelRequested(object sender, EventArgs e)
        {
            _view.CloseForm();
        }

        /// <summary>
        /// 인증 등록/제거 버튼 클릭 이벤트를 처리한다.
        /// 
        /// 로컬 인증정보가 등록되어 있으면 제거를 수행하고,
        /// 등록되어 있지 않으면 인증 등록을 수행한다.
        /// 
        /// 판단 기준은 인증키 TextBox 값이 아니라 로컬 인증 등록 완료 여부이다.
        /// </summary>
        private void OnAuthActionRequested(object sender, EventArgs e)
        {
            if (IsAuthRegistered())
            {
                RemoveLocalAuth();
            }
            else
            {
                RegisterAuth();
            }
        }

        /// <summary>
        /// 설정 화면의 입력값을 검증한 뒤 INI 설정 파일에 저장한다.
        /// 
        /// 처리 순서:
        /// 1. View 입력값을 AppConfig 객체에 반영
        /// 2. 설정값 검증
        /// 3. INI 파일 저장
        /// 4. Windows 자동실행 등록/해제 반영
        /// 5. Windows 방화벽 정책 적용
        /// 6. 인증 버튼 상태 갱신
        /// 7. 자동 송출 설정에 따라 송출 시작/중지/재시작 적용
        /// 
        /// 방화벽 적용 실패는 설정 저장 실패로 처리하지 않는다.
        /// </summary>
        /// <param name="silent">
        /// true이면 일반 저장 완료 메시지를 생략한다.
        /// 단, 송출 시작/중지/재시작이 발생한 경우에는 별도 안내 메시지를 표시한다.
        /// </param>
        /// <returns>
        /// true: 저장 성공
        /// false: 저장 실패
        /// </returns>
        private bool SaveConfig(bool silent)
        {
            try
            {
                ApplyViewToConfig();

                ValidateConfig(_config);

                _configService.Save(_config);

                ApplyAutoStartPolicy(_config);

                //2026-06-02 저장시 매번 실행하던 방화벽 등록 보류
                //ApplyFirewallPolicy(_config);

                RefreshAuthButtonText();

                bool streamingPolicyApplied = ApplyStreamingPolicyAfterSave(_config);

                if (!silent && !streamingPolicyApplied)
                {
                    _view.ShowInfo("설정이 저장되었습니다.");
                }

                return true;
            }
            catch (Exception ex)
            {
                _view.ShowError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// View의 입력값을 AppConfig 객체에 반영한다.
        /// 
        /// 이 메서드는 저장 전 현재 화면 값을 내부 설정 객체에 복사하기 위해 사용한다.
        /// </summary>
        private void ApplyViewToConfig()
        {
            EnsureConfigObjects();

            List<StreamConfig> streams = _view.Streams;

            _config.Streams = streams ?? new List<StreamConfig>();

            _config.Onvif.UserId = _view.OnvifUserId;
            _config.Onvif.Password = _view.OnvifPassword;

            if (!IsAuthRegistered())
            {
                /*
                 * 미등록 상태에서만 사용자가 입력한 인증키를 INI에 반영한다.
                 * 등록 상태에서는 TextBox에 마스킹된 표시값이 들어 있으므로 저장하지 않는다.
                 */
                _config.Auth.LicenseKey = _view.LicenseKey;
            }

            _config.Auth.DeviceName = _view.DeviceName;

            _config.Operation.EnableDetailLog = _view.EnableDetailLog;
            _config.Operation.AutoStart = _view.AutoStart;
            _config.Operation.PreventSleep = _view.PreventSleep;
            _config.Operation.AutoStartStreaming = _view.AutoStartStreaming;

            /*
             * 클릭·터치 포인터 설정을 View에서 가져온다.
             */
            _config.TouchPointer.Enabled = _view.TouchPointerEnabled;
            _config.TouchPointer.Diameter = _view.TouchPointerDiameter;
            _config.TouchPointer.VisibleMilliseconds = _view.TouchPointerVisibleMilliseconds;

            /*
             * 잘못된 값이 설정 객체에 남지 않도록
             * 저장 전에 허용 범위로 보정한다.
             */
            _config.TouchPointer.Normalize();
        }

        /// <summary>
        /// 설정값을 검증한다.
        /// 
        /// 주 모니터(Stream0)가 반드시 선택될 필요는 없다.
        /// 사용자가 보조 모니터만 송출할 수도 있으므로,
        /// 활성화된 Stream이 하나 이상인지 여부만 검사한다.
        /// </summary>
        private void ValidateConfig(AppConfig config)
        {
            if (config == null)
                throw new InvalidOperationException("설정 정보가 없습니다.");

            if (config.Streams == null || config.Streams.Count == 0)
                throw new InvalidOperationException("스트림 설정이 없습니다.");

            if (config.TouchPointer == null)
            {
                throw new InvalidOperationException("클릭·터치 포인터 설정이 없습니다.");
            }

            if (config.TouchPointer.Diameter < 24 || config.TouchPointer.Diameter > 500)
            {
                throw new InvalidOperationException("포인터 크기는 24px 이상 500px 이하로 설정하세요.");
            }

            if (config.TouchPointer.VisibleMilliseconds < 100 ||
                config.TouchPointer.VisibleMilliseconds > 5000)
            {
                throw new InvalidOperationException("포인터 표시 시간은 100ms 이상 5000ms 이하로 설정하세요.");
            }

            int enabledStreamCount = 0;

            foreach (StreamConfig stream in config.Streams)
            {
                if (stream == null)
                    continue;

                /*
                 * 비활성 Stream은 실제 송출 대상이 아니므로 검증하지 않는다.
                 */
                if (!stream.IsEnabled)
                    continue;

                enabledStreamCount++;

                if (string.IsNullOrWhiteSpace(stream.ScreenName))
                {
                    throw new InvalidOperationException(
                        "Stream" + stream.StreamNo + "의 모니터 장치명이 비어 있습니다. 모니터 설정을 다시 확인하세요.");
                }

                if (string.IsNullOrWhiteSpace(stream.RtspPath))
                {
                    throw new InvalidOperationException(
                        "Stream" + stream.StreamNo + "의 RTSP 경로가 비어 있습니다.");
                }

                if (stream.OnvifPort <= 0)
                {
                    throw new InvalidOperationException(
                        "Stream" + stream.StreamNo + "의 ONVIF 포트가 올바르지 않습니다.");
                }

                ValidateStreamQuality(stream, stream.MainStream, "MainStream");
                ValidateStreamQuality(stream, stream.SubStream, "SubStream");
            }

            /*
             * Stream0이 필수인 것이 아니라,
             * 사용 체크된 Stream이 하나 이상이어야 한다.
             */
            if (enabledStreamCount == 0)
            {
                throw new InvalidOperationException(
                    "송출할 모니터를 하나 이상 선택하세요.");
            }
        }

        /// <summary>
        /// Main/Sub Stream 품질 설정을 검증한다.
        /// </summary>
        private void ValidateStreamQuality(
            StreamConfig parentStream,
            StreamQualityConfig quality,
            string qualityName)
        {
            if (parentStream == null)
                return;

            /*
             * 부모 Stream이 비활성화된 경우에는 검증하지 않는다.
             */
            if (!parentStream.IsEnabled)
                return;

            if (quality == null)
            {
                throw new InvalidOperationException(
                    "Stream" + parentStream.StreamNo + " " + qualityName + " 설정이 없습니다.");
            }

            /*
             * 현재 UI에서는 Main/Sub 개별 사용 여부를 관리하지 않는다.
             * 부모 Stream이 사용이면 Main/Sub도 사용 상태여야 한다.
             */
            if (!quality.IsEnabled)
            {
                throw new InvalidOperationException(
                    "Stream" + parentStream.StreamNo + " " + qualityName + " 사용 설정이 꺼져 있습니다.");
            }

            if (quality.Fps <= 0)
            {
                throw new InvalidOperationException(
                    "Stream" + parentStream.StreamNo + " " + qualityName + " FPS 값이 올바르지 않습니다.");
            }

            if (string.IsNullOrWhiteSpace(quality.Bitrate))
            {
                throw new InvalidOperationException(
                    "Stream" + parentStream.StreamNo + " " + qualityName + " Bitrate 값이 비어 있습니다.");
            }

            if (string.IsNullOrWhiteSpace(quality.RtspPath))
            {
                throw new InvalidOperationException(
                    "Stream" + parentStream.StreamNo + " " + qualityName + " RTSP 경로가 비어 있습니다.");
            }
        }

        /// <summary>
        /// 설정 저장 후 Windows 자동실행 정책을 적용한다.
        /// 
        /// Operation.AutoStart 값이 true이면 HKCU Run 레지스트리에 자동실행을 등록하고,
        /// false이면 기존 자동실행 등록을 해제한다.
        /// </summary>
        /// <param name="config">
        /// 저장 완료된 최신 설정 객체.
        /// </param>
        private void ApplyAutoStartPolicy(AppConfig config)
        {
            if (config == null || config.Operation == null)
                return;

            try
            {
                _autoStartService.Apply(config.Operation.AutoStart);
            }
            catch (Exception ex)
            {
                _view.ShowInfo(
                    "자동실행 설정 적용 중 오류가 발생했습니다.\r\n\r\n" +
                    "설정 저장은 완료되었지만 Windows 자동실행 등록은 적용되지 않았습니다.\r\n\r\n" +
                    "오류 내용:\r\n" + ex.Message);
            }
        }

        /// <summary>
        /// 설정 저장 후 Windows 방화벽 정책을 적용한다.
        /// 
        /// RTSP 포트와 스트림별 ONVIF 포트를 인바운드 허용 규칙으로 등록한다.
        /// 단, 방화벽 등록 실패가 설정 저장이나 인증 등록 흐름을 막으면 안 되므로
        /// 이 메서드에서는 예외를 외부로 던지지 않고 안내 메시지만 표시한다.
        /// </summary>
        /// <param name="config">
        /// 저장 완료된 최신 설정 객체.
        /// </param>
        private void ApplyFirewallPolicy(AppConfig config)
        {
            if (config == null)
                return;

            try
            {
                _firewallService.Apply(config);
            }
            catch (Exception ex)
            {
                _view.ShowInfo(
                    "방화벽 규칙 적용 중 오류가 발생했습니다.\r\n\r\n" +
                    "프로그램 실행과 설정 저장은 계속 진행됩니다.\r\n" +
                    "다른 PC 또는 NVR에서 접속이 안 될 경우 관리자 권한으로 실행 후 다시 설정을 저장하세요.\r\n\r\n" +
                    "오류 내용:\r\n" + ex.Message);
            }
        }

        /// <summary>
        /// 설정 저장 후 자동 송출 정책을 적용한다.
        /// 
        /// 처리 기준:
        /// - AutoStartStreaming=True 이고 현재 송출 중이면 새 설정으로 자동 재시작한다.
        /// - AutoStartStreaming=True 이고 현재 송출 중이 아니면 새 설정으로 송출을 시작한다.
        /// - AutoStartStreaming=False 이고 현재 송출 중이면 송출을 중지한다.
        /// - AutoStartStreaming=False 이고 현재 송출 중이 아니면 설정 저장만 수행한다.
        /// </summary>
        /// <param name="config">
        /// 저장 완료된 최신 설정 객체.
        /// </param>
        /// <returns>
        /// true: 송출 시작, 중지, 재시작 중 하나가 수행됨
        /// false: 설정 저장 외 별도 송출 동작 없음
        /// </returns>
        private bool ApplyStreamingPolicyAfterSave(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            bool autoStartStreaming =
                config.Operation != null &&
                config.Operation.AutoStartStreaming;

            bool isRunning =
                _supervisor.Status == StreamRuntimeStatus.Running ||
                _supervisor.Status == StreamRuntimeStatus.Starting;

            if (autoStartStreaming)
            {
                if (isRunning)
                {
                    _supervisor.Restart(config);
                    _view.ShowInfo("설정이 저장되었으며, 새 설정으로 송출을 재시작했습니다.");
                    return true;
                }

                _supervisor.Start(config);
                _view.ShowInfo("설정이 저장되었으며, 송출을 시작했습니다.");
                return true;
            }

            if (isRunning)
            {
                _supervisor.Stop();
                _view.ShowInfo("설정이 저장되었으며, 자동 송출 해제에 따라 송출을 중지했습니다.");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 인증 등록을 수행한다.
        /// 
        /// 현재 단계에서는 AuthDllAdapter가 임시 인증 성공 결과를 반환한다.
        /// 인증 성공 시 LicenseKey, DeviceName, LastAuthResult, LastAuthAt 값을 저장한다.
        /// 
        /// 향후 인증모듈 완료 후에는 인증토큰 저장 위치를 기준으로 로컬 인증 등록 여부를 판단하도록 변경한다.
        /// </summary>
        private void RegisterAuth()
        {
            try
            {
                string licenseKey = _view.LicenseKey;
                string deviceName = _view.DeviceName;

                if (string.IsNullOrWhiteSpace(licenseKey))
                    throw new InvalidOperationException("인증키를 입력하세요.");

                if (string.IsNullOrWhiteSpace(deviceName))
                    throw new InvalidOperationException("장비명을 입력하세요.");

                AuthResult result = _authDllAdapter.TryActivate(licenseKey, deviceName);

                if (result == null || !result.CanRun)
                {
                    string message = result == null
                        ? "인증 결과가 없습니다."
                        : result.Message;

                    throw new InvalidOperationException(message);
                }

                ApplyViewToConfig();

                if (_config.Auth == null)
                    _config.Auth = new AuthConfig();

                _config.Auth.LicenseKey = licenseKey;
                _config.Auth.DeviceName = deviceName;
                _config.Auth.LastAuthResult = "OK";
                _config.Auth.LastAuthAt = DateTime.Now;

                _configService.Save(_config);

                ApplyAutoStartPolicy(_config);
                ApplyFirewallPolicy(_config);

                RefreshAuthButtonText();

                RefreshAuthButtonText();

                bool autoStartStreaming =
                    _config.Operation != null &&
                    _config.Operation.AutoStartStreaming;

                /*
                 * 자동 송출이 꺼져 있으면 송출 시작 흐름을 타지 않으므로
                 * 인증 등록 직후 별도로 인증 감시를 시작한다.
                 */
                if (!autoStartStreaming)
                {
                    AuthResult monitorResult = _supervisor.StartAuthMonitoring(_config);

                    if (monitorResult == null || !monitorResult.IsSuccess)
                    {
                        string message = monitorResult == null
                            ? "인증 감시 시작 결과가 없습니다."
                            : monitorResult.Message;

                        throw new InvalidOperationException(
                            "인증 등록은 완료되었지만 인증 감시 시작에 실패했습니다.\r\n" +
                            message);
                    }
                }

                bool streamingPolicyApplied = ApplyStreamingPolicyAfterSave(_config);

                if (!streamingPolicyApplied)
                {
                    _view.ShowInfo("인증 등록이 완료되었습니다.");
                }
            }
            catch (Exception ex)
            {
                _view.ShowError("인증 등록 실패\r\n" + ex.Message);
            }
        }

        /// <summary>
        /// 로컬 인증정보를 제거한다.
        /// 
        /// 이 작업은 서버의 장비 귀속 해제가 아니라,
        /// 현재 PC의 로컬 인증 상태 파일과 INI에 남아 있는 인증 표시값만 제거한다.
        /// </summary>
        private void RemoveLocalAuth()
        {
            bool confirmed = _view.Confirm(
                "로컬 인증정보를 제거하시겠습니까?\r\n\r\n" +
                "이 작업은 서버의 장비 귀속 해제가 아니라 현재 PC의 로컬 인증정보만 제거합니다.");

            if (!confirmed)
                return;

            try
            {
                if (_config == null)
                    _config = _configService.Load();

                EnsureConfigObjects();

                /*
                 * PccAuthClient.dll이 관리하는 로컬 인증 파일 삭제.
                 * 기본 위치:
                 * C:\ProgramData\POSCAM\Auth\auth_state.dat
                 */
                _authDllAdapter.RemoveLocalAuth();

                /*
                 * INI에 남아 있는 인증 표시값 초기화.
                 */
                _config.Auth.LicenseKey = "";
                _config.Auth.LastAuthResult = "";
                _config.Auth.LastAuthAt = null;

                _configService.Save(_config);

                /*
                 * 화면 입력값도 즉시 초기화한다.
                 */
                _view.LicenseKey = "";

                /*
                 * 제거 후 실제 인증 상태를 다시 확인한다.
                 * 파일 삭제가 실패했다면 여전히 등록 상태로 판단될 수 있다.
                 */
                if (IsAuthRegistered())
                {
                    _view.SetAuthButtonText("제거");
                    _view.SetLicenseKeyInputEnabled(false);

                    _view.ShowError(
                        "로컬 인증정보 제거 후에도 인증 상태 파일이 남아 있습니다.\r\n\r\n" +
                        "다음 파일이 삭제되었는지 확인하세요.\r\n" +
                        _authDllAdapter.GetLocalAuthFilePath());

                    return;
                }
                /*
                 * 미등록 상태로 확정되었으므로 인증 감시를 중지한다.
                 * 
                 * 이유:
                 * - 로컬 인증정보가 제거된 상태에서는 더 이상 24시간 재인증을 수행하면 안 된다.
                 * - 송출 중이 아니어도 인증 감시 타이머가 살아 있을 수 있으므로 명시적으로 중지한다.
                 */
                _supervisor.StopAuthMonitoring();

                /*
                 * 미등록 상태로 확정되었으므로 인증키 입력을 다시 허용한다.
                 */
                _view.SetAuthButtonText("등록");
                _view.SetLicenseKeyInputEnabled(true);

                /*
                 * 송출 중이면 인증 제거에 따라 송출을 중지한다.
                 */
                if (_supervisor.Status == StreamRuntimeStatus.Running ||
                    _supervisor.Status == StreamRuntimeStatus.Starting)
                {
                    _supervisor.Stop();
                }

                _view.ShowInfo("로컬 인증정보가 제거되었습니다.");
            }
            catch (Exception ex)
            {
                _view.ShowError("로컬 인증정보 제거 실패\r\n" + ex.Message);
            }
        }

        /// <summary>
        /// 현재 로컬 인증정보가 등록되어 있는지 확인한다.
        /// 
        /// 현재 임시 단계에서는 AuthDllAdapter.HasLocalAuth를 사용한다.
        /// 이 메서드는 인증키 TextBox 입력 여부를 기준으로 판단하지 않는다.
        /// 
        /// 향후 인증모듈 완료 후에는 지정된 위치의 인증토큰 존재 여부를 기준으로 판단하도록 변경한다.
        /// </summary>
        /// <returns>
        /// true: 로컬 인증정보가 등록 완료된 상태
        /// false: 미등록 상태
        /// </returns>
        private bool IsAuthRegistered()
        {
            if (_config == null || _config.Auth == null)
                return false;

            return _authDllAdapter.HasLocalAuth(_config.Auth);
        }

        /// <summary>
        /// 로컬 인증 등록 상태에 따라 인증 버튼과 인증키 입력 상태를 갱신한다.
        /// 
        /// 판단 기준:
        /// - 인증키 TextBox 값이 아니라 PccAuthClient.dll의 로컬 인증 상태 파일 존재 여부
        /// - 등록 상태이면 제거 버튼 표시 및 인증키 입력 잠금
        /// - 미등록 상태이면 등록 버튼 표시 및 인증키 입력 허용
        /// </summary>
        private void RefreshAuthButtonText()
        {
            bool isRegistered = IsAuthRegistered();

            if (isRegistered)
            {
                _view.SetAuthButtonText("제거");
                _view.SetLicenseKeyInputEnabled(false);
            }
            else
            {
                _view.SetAuthButtonText("등록");
                _view.SetLicenseKeyInputEnabled(true);
            }
        }

        /// <summary>
        /// Presenter에서 연결한 View 이벤트를 해제한다.
        /// 
        /// 설정 화면 종료 시 호출되어 이벤트 중복 연결과 메모리 참조를 방지한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _view.ApplyRequested -= OnApplyRequested;
                _view.OkRequested -= OnOkRequested;
                _view.CancelRequested -= OnCancelRequested;
                _view.AuthActionRequested -= OnAuthActionRequested;
            }
            catch
            {
            }

            _disposed = true;
        }
    }
}