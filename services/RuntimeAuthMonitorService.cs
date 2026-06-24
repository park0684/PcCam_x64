using System;
using System.Threading;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 실행 중 인증 상태를 감시하는 서비스.
    /// 
    /// 재인증 정책은 이 서비스가 판단하지 않는다.
    /// PccAuthClient.dll이 반환한 NextCheckAt 시각에 맞춰
    /// AuthDllAdapter.CheckRuntime()만 호출한다.
    /// 
    /// 인증 실패 시 직접 송출을 중지하지 않고 AuthFailed 이벤트를 발생시킨다.
    /// 실제 송출 중지는 StreamSupervisorService가 담당한다.
    /// </summary>
    public class RuntimeAuthMonitorService : IDisposable
    {
        private readonly object _syncLock = new object();

        private readonly AuthDllAdapter _authDllAdapter;
        private readonly LogService _logService;

        private Timer _timer;
        private bool _isRunning;
        private bool _disposed;

        /// <summary>
        /// 실행 중 인증 실패 이벤트.
        /// 
        /// StreamSupervisorService는 이 이벤트를 구독하여 송출을 중지한다.
        /// </summary>
        public event Action<AuthResult> AuthFailed;

        /// <summary>
        /// 실행 중 인증 감시 서비스를 생성한다.
        /// </summary>
        /// <param name="authDllAdapter">
        /// 인증 DLL 어댑터.
        /// </param>
        /// <param name="logService">
        /// 로그 서비스.
        /// </param>
        public RuntimeAuthMonitorService(
            AuthDllAdapter authDllAdapter,
            LogService logService)
        {
            if (authDllAdapter == null)
                throw new ArgumentNullException("authDllAdapter");

            if (logService == null)
                throw new ArgumentNullException("logService");

            _authDllAdapter = authDllAdapter;
            _logService = logService;
        }

        /// <summary>
        /// 실행 중 인증 감시를 시작한다.
        /// 
        /// nextCheckAt은 인증 DLL이 반환한 다음 인증 확인 시각이다.
        /// PC CAM은 이 시간을 계산하지 않고, 이 시간에 맞춰 CheckRuntime만 호출한다.
        /// </summary>
        /// <param name="nextCheckAt">
        /// 다음 인증 확인 예정 시각.
        /// </param>
        public void Start(DateTime? nextCheckAt)
        {
            lock (_syncLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("RuntimeAuthMonitorService");

                _isRunning = true;

                ScheduleNext(nextCheckAt);

                _logService.WriteApp(
                    "실행 중 인증 감시 시작. NextCheckAt=" +
                    FormatDateTime(nextCheckAt));
            }
        }

        /// <summary>
        /// 실행 중 인증 감시를 중지한다.
        /// </summary>
        public void Stop()
        {
            lock (_syncLock)
            {
                _isRunning = false;

                DisposeTimer();

                _logService.WriteApp("실행 중 인증 감시 중지");
            }
        }

        /// <summary>
        /// 다음 인증 확인을 예약한다.
        /// </summary>
        /// <param name="nextCheckAt">
        /// 다음 인증 확인 예정 시각.
        /// </param>
        private void ScheduleNext(DateTime? nextCheckAt)
        {
            TimeSpan dueTime = CalculateDueTime(nextCheckAt);

            DisposeTimer();

            _timer = new Timer(
                OnTimer,
                null,
                dueTime,
                Timeout.InfiniteTimeSpan);

            _logService.WriteApp(
                "다음 실행 중 인증 확인 예약. DueTime=" +
                dueTime +
                ", NextCheckAt=" +
                FormatDateTime(nextCheckAt));
        }

        /// <summary>
        /// 다음 인증 확인까지 남은 시간을 계산한다.
        /// 
        /// NextCheckAt이 없으면 1분 뒤로 예약한다.
        /// 이미 지난 시간이면 1초 뒤 실행한다.
        /// </summary>
        /// <param name="nextCheckAt">
        /// 다음 인증 확인 예정 시각.
        /// </param>
        /// <returns>
        /// 타이머 대기 시간.
        /// </returns>
        private TimeSpan CalculateDueTime(DateTime? nextCheckAt)
        {
            if (!nextCheckAt.HasValue)
                return TimeSpan.FromMinutes(1);

            TimeSpan dueTime = nextCheckAt.Value - DateTime.Now;

            if (dueTime.TotalMilliseconds < 1000)
                return TimeSpan.FromSeconds(1);

            return dueTime;
        }

        /// <summary>
        /// 타이머 콜백.
        /// 
        /// AuthDllAdapter.CheckRuntime()을 호출하고,
        /// 성공이면 DLL이 반환한 NextCheckAt으로 다음 확인을 다시 예약한다.
        /// 실패이면 AuthFailed 이벤트를 발생시킨다.
        /// </summary>
        /// <param name="state">
        /// 사용하지 않음.
        /// </param>
        private void OnTimer(object state)
        {
            lock (_syncLock)
            {
                if (!_isRunning || _disposed)
                    return;
            }

            try
            {
                _logService.WriteApp("실행 중 인증 확인 시작");

                AuthResult authResult = _authDllAdapter.CheckRuntime();

                if (authResult == null || !authResult.IsSuccess)
                {
                    string message = authResult == null
                        ? "실행 중 인증 결과가 없습니다."
                        : authResult.Message;

                    _logService.WriteError(
                        "실행 중 인증 실패. Message=" + message);

                    lock (_syncLock)
                    {
                        _isRunning = false;
                        DisposeTimer();
                    }

                    RaiseAuthFailed(authResult);
                    return;
                }

                _logService.WriteApp(
                    "실행 중 인증 성공. Message=" +
                    authResult.Message +
                    ", NextCheckAt=" +
                    FormatDateTime(authResult.NextCheckAt));

                lock (_syncLock)
                {
                    if (_isRunning && !_disposed)
                        ScheduleNext(authResult.NextCheckAt);
                }
            }
            catch (Exception ex)
            {
                _logService.WriteException(
                    "실행 중 인증 확인 처리 오류",
                    ex);

                lock (_syncLock)
                {
                    if (_isRunning && !_disposed)
                        ScheduleNext(DateTime.Now.AddMinutes(1));
                }
            }
        }

        /// <summary>
        /// 실행 중 인증 실패 이벤트를 발생시킨다.
        /// </summary>
        /// <param name="authResult">
        /// 인증 실패 결과.
        /// </param>
        private void RaiseAuthFailed(AuthResult authResult)
        {
            Action<AuthResult> handler = AuthFailed;

            if (handler != null)
                handler(authResult);
        }

        /// <summary>
        /// 현재 타이머를 정리한다.
        /// </summary>
        private void DisposeTimer()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        /// <summary>
        /// DateTime? 값을 로그 출력용 문자열로 변환한다.
        /// </summary>
        /// <param name="value">
        /// 출력할 날짜.
        /// </param>
        /// <returns>
        /// 날짜 문자열.
        /// </returns>
        private string FormatDateTime(DateTime? value)
        {
            if (!value.HasValue)
                return "";

            return value.Value.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 리소스를 정리한다.
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
    }
}