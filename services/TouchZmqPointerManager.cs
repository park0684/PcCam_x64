using System;
using System.Collections.Generic;
using System.Threading;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// 전역 마우스·터치 입력 좌표를
    /// 대상 Stream의 FFmpeg ZMQ 명령으로 전달한다.
    ///
    /// 지원 구조:
    /// - Stream0 → 주 모니터
    /// - Stream1 → 보조 모니터
    /// - Stream2 이상도 동일한 방식으로 확장 가능
    ///
    /// 각 Stream은 서로 다른 ZMQ 포트를 사용한다.
    ///
    /// 예:
    /// Stream0 → tcp://127.0.0.1:5555
    /// Stream1 → tcp://127.0.0.1:5556
    ///
    /// 실제 Windows 화면에는 포인터를 표시하지 않는다.
    /// 각 FFmpeg 프로세스 내부의 overlay@touch 위치만 변경한다.
    /// </summary>
    public sealed class TouchZmqPointerManager : IDisposable
    {
        private readonly object _syncRoot =
            new object();

        private readonly GlobalPointerInputService _pointerInputService;
        private readonly TouchZmqControlService _controlService;

        /*
         * 기존에는 Stream0 대상 하나만 보관했지만,
         * 다중 모니터 지원을 위해 StreamNo별 대상을 관리한다.
         */
        private readonly Dictionary<int, PointerTarget> _targets =
            new Dictionary<int, PointerTarget>();

        /*
         * 동일 Stream이 해제된 뒤 다시 등록됐을 때
         * 이전 비동기 작업이 새 등록 대상에 적용되는 것을 막기 위한 번호다.
         */
        private long _registrationSequence;

        private bool _disposed;

        /// <summary>
        /// ZMQ 포인터 처리 로그.
        /// </summary>
        public event Action<string> LogReceived;

        /// <summary>
        /// ZMQ 포인터 관리자를 생성한다.
        /// </summary>
        public TouchZmqPointerManager(
            GlobalPointerInputService pointerInputService,
            TouchZmqControlService controlService)
        {
            if (pointerInputService == null)
                throw new ArgumentNullException("pointerInputService");

            if (controlService == null)
                throw new ArgumentNullException("controlService");

            _pointerInputService =
                pointerInputService;

            _controlService =
                controlService;

            /*
             * 전역 클릭·터치 입력은 한 번만 구독한다.
             * 실제 대상 Stream 선택은 OnPointerPressed에서 수행한다.
             */
            _pointerInputService.PointerPressed +=
                OnPointerPressed;
        }

        /// <summary>
        /// 지정한 Stream과 모니터를 ZMQ 포인터 대상으로 등록한다.
        ///
        /// Stream별로 모니터 영역, 포인터 크기,
        /// 표시 시간과 숨김 타이머를 별도로 관리한다.
        /// </summary>
        public void RegisterTarget(
            int streamNo,
            MonitorInfo monitorInfo,
            int pointerSize,
            int visibleMilliseconds)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    "TouchZmqPointerManager");
            }

            if (streamNo < 0)
            {
                throw new ArgumentOutOfRangeException(
                    "streamNo",
                    "StreamNo는 0 이상이어야 합니다.");
            }

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (monitorInfo.BoundsWidth <= 0 ||
                monitorInfo.BoundsHeight <= 0)
            {
                throw new InvalidOperationException(
                    "ZMQ 포인터 대상 모니터의 해상도가 올바르지 않습니다.");
            }

            if (pointerSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "pointerSize",
                    "포인터 크기는 1 이상이어야 합니다.");
            }

            if (visibleMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "visibleMilliseconds",
                    "포인터 표시 시간은 1ms 이상이어야 합니다.");
            }

            PointerTarget oldTarget = null;
            PointerTarget newTarget;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(
                        "TouchZmqPointerManager");
                }

                /*
                 * 동일 Stream이 이미 등록되어 있으면
                 * 기존 대상을 제거한 뒤 최신 설정으로 교체한다.
                 */
                _targets.TryGetValue(
                    streamNo,
                    out oldTarget);

                _registrationSequence++;

                newTarget =
                    new PointerTarget();

                newTarget.StreamNo =
                    streamNo;

                newTarget.RegistrationId =
                    _registrationSequence;

                newTarget.BoundsX =
                    monitorInfo.BoundsX;

                newTarget.BoundsY =
                    monitorInfo.BoundsY;

                newTarget.BoundsWidth =
                    monitorInfo.BoundsWidth;

                newTarget.BoundsHeight =
                    monitorInfo.BoundsHeight;

                newTarget.PointerSize =
                    pointerSize;

                newTarget.VisibleMilliseconds =
                    visibleMilliseconds;

                /*
                 * 각 Stream이 별도의 숨김 타이머를 가진다.
                 *
                 * Stream0 포인터가 표시된 상태에서
                 * Stream1을 클릭해도 Stream0의 타이머와
                 * Stream1의 타이머가 서로 영향을 주지 않는다.
                 */
                newTarget.HideTimer =
                    new Timer(
                        OnHideTimerElapsed,
                        new HideTimerState
                        {
                            StreamNo = streamNo,
                            RegistrationId =
                                newTarget.RegistrationId
                        },
                        Timeout.Infinite,
                        Timeout.Infinite);

                _targets[streamNo] =
                    newTarget;
            }

            /*
             * Timer.Dispose는 Manager 잠금 밖에서 실행한다.
             * 타이머 콜백과 잠금이 서로 대기하는 상황을 줄이기 위함이다.
             */
            DisposeTargetTimer(
                oldTarget);

            RaiseLog(
                "[Stream" + streamNo +
                "] ZMQ 포인터 대상 등록. Bounds=" +
                monitorInfo.BoundsX + "," +
                monitorInfo.BoundsY + "," +
                monitorInfo.BoundsWidth + "x" +
                monitorInfo.BoundsHeight +
                ", PointerSize=" +
                pointerSize +
                ", VisibleMs=" +
                visibleMilliseconds);
        }

        /// <summary>
        /// 지정한 Stream의 ZMQ 포인터 대상 등록을 해제한다.
        ///
        /// FFmpeg 종료 이후 호출될 수도 있으므로
        /// 해제 과정에서는 ZMQ 숨김 명령을 전송하지 않는다.
        /// </summary>
        public void UnregisterTarget(
            int streamNo)
        {
            PointerTarget target = null;

            lock (_syncRoot)
            {
                if (_targets.TryGetValue(
                        streamNo,
                        out target))
                {
                    _targets.Remove(
                        streamNo);
                }
            }

            if (target == null)
                return;

            DisposeTargetTimer(
                target);

            RaiseLog(
                "[Stream" + streamNo +
                "] ZMQ 포인터 대상 해제");
        }

        /// <summary>
        /// 전역 클릭 좌표가 어느 등록 모니터에 포함되는지 확인하고,
        /// 해당 Stream의 모니터 로컬 좌표로 변환한다.
        /// </summary>
        private void OnPointerPressed(
            PointerSample sample)
        {
            if (sample == null)
                return;

            PointerRequest request = null;

            lock (_syncRoot)
            {
                /*
                 * 포인터 기능이 비활성화되어 등록된 대상이 없거나
                 * 관리자가 해제된 상태라면 클릭을 처리하지 않는다.
                 */
                if (_disposed ||
                    _targets.Count == 0)
                {
                    return;
                }

                /*
                 * 등록된 Stream들의 모니터 영역을 확인한다.
                 *
                 * Windows 확장 모니터 구성에서는 각 모니터 영역이
                 * 서로 겹치지 않으므로 하나의 대상만 선택된다.
                 */
                foreach (KeyValuePair<int, PointerTarget> pair in _targets)
                {
                    PointerTarget target =
                        pair.Value;

                    if (target == null)
                        continue;

                    bool containsX =
                        sample.ScreenX >= target.BoundsX &&
                        sample.ScreenX <
                        target.BoundsX +
                        target.BoundsWidth;

                    bool containsY =
                        sample.ScreenY >= target.BoundsY &&
                        sample.ScreenY <
                        target.BoundsY +
                        target.BoundsHeight;

                    if (!containsX ||
                        !containsY)
                    {
                        continue;
                    }

                    /*
                     * 이 Stream에서 가장 최근에 발생한 클릭을 식별한다.
                     * 이전 클릭 작업이 늦게 실행되는 경우를 구분하기 위해 사용한다.
                     */
                    target.ClickSequence++;

                    request =
                        new PointerRequest();

                    request.StreamNo =
                        target.StreamNo;

                    request.RegistrationId =
                        target.RegistrationId;

                    request.ClickSequence =
                        target.ClickSequence;

                    /*
                     * Windows 가상 화면 좌표를
                     * 해당 모니터 내부의 로컬 좌표로 변환한다.
                     */
                    request.LocalX =
                        sample.ScreenX -
                        target.BoundsX;

                    request.LocalY =
                        sample.ScreenY -
                        target.BoundsY;

                    request.PointerSize =
                        target.PointerSize;

                    request.VisibleMilliseconds =
                        target.VisibleMilliseconds;

                    break;
                }
            }

            /*
             * 등록된 어느 모니터에도 포함되지 않은 클릭은 무시한다.
             */
            if (request == null)
                return;
            /*
             * PointerPressed 이벤트는 GlobalPointerInputService에서
             * 이미 후크 콜백 외부의 ThreadPool 작업으로 전달된다.
             *
             * 여기서 다시 ThreadPool에 등록하면 클릭 순서가 한 번 더
             * 뒤바뀔 가능성이 있으므로 현재 작업에서 바로 처리한다.
             */
            ShowPointerCore(
                request);
        }

        /// <summary>
        /// 대상 Stream의 원형 포인터 위치를 변경하고
        /// 해당 Stream 전용 숨김 타이머를 시작한다.
        /// </summary>
        private void ShowPointerCore(
            PointerRequest request)
        {
            if (request == null)
                return;

            PointerTarget target;

            lock (_syncRoot)
            {
                if (!TryGetCurrentTarget(
                        request.StreamNo,
                        request.RegistrationId,
                        out target))
                {
                    return;
                }

                /*
                 * 이미 더 최근 클릭이 발생했다면
                 * 지연 실행된 이전 작업은 무시한다.
                 */
                if (target.ClickSequence !=
                    request.ClickSequence)
                {
                    return;
                }
            }

            /*
             * 동일 Stream의 표시와 숨김 명령이 동시에 실행되지 않도록
             * Stream별 명령 잠금을 사용한다.
             */
            lock (target.CommandSync)
            {
                lock (_syncRoot)
                {
                    PointerTarget currentTarget;

                    if (!TryGetCurrentTarget(
                            request.StreamNo,
                            request.RegistrationId,
                            out currentTarget))
                    {
                        return;
                    }

                    if (currentTarget.ClickSequence !=
                        request.ClickSequence)
                    {
                        return;
                    }
                }

                string response;
                string errorMessage;

                /*
                 * StreamNo를 함께 전달해야
                 * Stream0은 5555, Stream1은 5556으로 명령이 전송된다.
                 */
                bool success =
                    _controlService.ShowPointer(
                        request.StreamNo,
                        request.LocalX,
                        request.LocalY,
                        request.PointerSize,
                        out response,
                        out errorMessage);

                if (!success)
                {
                    RaiseLog(
                        "[Stream" +
                        request.StreamNo +
                        "] ZMQ 포인터 표시 실패. " +
                        errorMessage);

                    return;
                }

                lock (_syncRoot)
                {
                    PointerTarget currentTarget;

                    if (!TryGetCurrentTarget(
                            request.StreamNo,
                            request.RegistrationId,
                            out currentTarget))
                    {
                        return;
                    }

                    /*
                     * 명령을 전송하는 동안 더 최근 클릭이 발생했다면
                     * 이전 클릭 기준 숨김 타이머를 시작하지 않는다.
                     */
                    if (currentTarget.ClickSequence !=
                        request.ClickSequence)
                    {
                        return;
                    }

                    currentTarget.ScheduledHideSequence =
                        request.ClickSequence;

                    /*
                     * 빠르게 여러 번 클릭하면 기존 예약을 취소하고
                     * 마지막 클릭 기준으로 표시 시간을 다시 계산한다.
                     */
                    currentTarget.HideTimer.Change(
                        request.VisibleMilliseconds,
                        Timeout.Infinite);
                }

                // 성공시 로그 생성 부분은 테스트 완료 후 폐기
                //RaiseLog(
                //    "[Stream" +
                //    request.StreamNo +
                //    "] ZMQ 포인터 표시. " +
                //    "X=" +
                //    request.LocalX +
                //    ", Y=" +
                //    request.LocalY +
                //    ", Response=" +
                //    response);
            }
        }

        /// <summary>
        /// 지정한 Stream의 표시 시간이 끝나면
        /// 해당 Stream의 원형 포인터만 화면 밖으로 이동한다.
        /// </summary>
        private void OnHideTimerElapsed(
            object state)
        {
            HideTimerState timerState =
                state as HideTimerState;

            if (timerState == null)
                return;

            PointerTarget target;
            long hideSequence;

            lock (_syncRoot)
            {
                if (!TryGetCurrentTarget(
                        timerState.StreamNo,
                        timerState.RegistrationId,
                        out target))
                {
                    return;
                }

                hideSequence =
                    target.ScheduledHideSequence;

                /*
                 * 숨김이 예약된 뒤 새로운 클릭이 들어왔다면
                 * 이전 타이머 콜백은 포인터를 숨기지 않는다.
                 */
                if (hideSequence <= 0 ||
                    target.ClickSequence !=
                    hideSequence)
                {
                    return;
                }
            }

            lock (target.CommandSync)
            {
                lock (_syncRoot)
                {
                    PointerTarget currentTarget;

                    if (!TryGetCurrentTarget(
                            timerState.StreamNo,
                            timerState.RegistrationId,
                            out currentTarget))
                    {
                        return;
                    }

                    if (currentTarget.ClickSequence !=
                        hideSequence ||
                        currentTarget.ScheduledHideSequence !=
                        hideSequence)
                    {
                        return;
                    }
                }

                string response;
                string errorMessage;

                bool success =
                    _controlService.HidePointer(
                        timerState.StreamNo,
                        out response,
                        out errorMessage);

                if (!success)
                {
                    RaiseLog(
                        "[Stream" +
                        timerState.StreamNo +
                        "] ZMQ 포인터 숨김 실패. " +
                        errorMessage);

                    return;
                }

                lock (_syncRoot)
                {
                    PointerTarget currentTarget;

                    if (TryGetCurrentTarget(
                            timerState.StreamNo,
                            timerState.RegistrationId,
                            out currentTarget) &&
                        currentTarget.ScheduledHideSequence ==
                        hideSequence)
                    {
                        currentTarget.ScheduledHideSequence =
                            0;
                    }
                }
                // 테스트 완료 후 성고 로그 폐기
                //RaiseLog(
                //    "[Stream" +
                //    timerState.StreamNo +
                //    "] ZMQ 포인터 숨김. Response=" +
                //    response);
            }
        }

        /// <summary>
        /// 지정한 Stream이 현재도 같은 등록 대상으로 유지되는지 확인한다.
        /// </summary>
        private bool TryGetCurrentTarget(
            int streamNo,
            long registrationId,
            out PointerTarget target)
        {
            target = null;

            if (_disposed)
                return false;

            if (!_targets.TryGetValue(
                    streamNo,
                    out target))
            {
                return false;
            }

            if (target == null ||
                target.RegistrationId !=
                registrationId)
            {
                target = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 대상 Stream의 숨김 타이머를 중지하고 해제한다.
        /// </summary>
        private void DisposeTargetTimer(
            PointerTarget target)
        {
            if (target == null ||
                target.HideTimer == null)
            {
                return;
            }

            try
            {
                target.HideTimer.Change(
                    Timeout.Infinite,
                    Timeout.Infinite);
            }
            catch
            {
            }

            try
            {
                target.HideTimer.Dispose();
            }
            catch
            {
            }
        }

        /// <summary>
        /// 로그 이벤트를 전달한다.
        /// </summary>
        private void RaiseLog(
            string message)
        {
            Action<string> handler =
                LogReceived;

            if (handler != null)
                handler(message);
        }

        /// <summary>
        /// 이벤트 구독과 Stream별 타이머를 모두 해제한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            List<PointerTarget> targets =
                new List<PointerTarget>();

            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed =
                    true;

                foreach (KeyValuePair<int, PointerTarget> pair in _targets)
                {
                    if (pair.Value != null)
                        targets.Add(pair.Value);
                }

                _targets.Clear();
            }

            try
            {
                _pointerInputService.PointerPressed -=
                    OnPointerPressed;
            }
            catch
            {
            }

            for (int i = 0;
                 i < targets.Count;
                 i++)
            {
                DisposeTargetTimer(
                    targets[i]);
            }
        }

        /// <summary>
        /// Stream별 포인터 대상 정보를 보관한다.
        /// </summary>
        private sealed class PointerTarget
        {
            public int StreamNo;
            public long RegistrationId;

            public int BoundsX;
            public int BoundsY;
            public int BoundsWidth;
            public int BoundsHeight;

            public int PointerSize;
            public int VisibleMilliseconds;

            /*
             * 해당 Stream에서 발생한 가장 최근 클릭 번호.
             */
            public long ClickSequence;

            /*
             * 현재 숨김 타이머가 어느 클릭을 기준으로 예약됐는지 나타낸다.
             */
            public long ScheduledHideSequence;

            /*
             * 동일 Stream의 표시·숨김 ZMQ 명령 순서를 직렬화한다.
             */
            public readonly object CommandSync =
                new object();

            public Timer HideTimer;
        }

        /// <summary>
        /// 비동기 포인터 표시 작업에 전달할 값의 스냅샷.
        /// </summary>
        private sealed class PointerRequest
        {
            public int StreamNo;
            public long RegistrationId;
            public long ClickSequence;

            public int LocalX;
            public int LocalY;

            public int PointerSize;
            public int VisibleMilliseconds;
        }

        /// <summary>
        /// Stream별 숨김 타이머 콜백 상태.
        /// </summary>
        private sealed class HideTimerState
        {
            public int StreamNo;
            public long RegistrationId;
        }
    }
}