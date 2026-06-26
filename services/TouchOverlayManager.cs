using System;
using System.Collections.Generic;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// 전역 포인터 좌표를 활성 Stream의 모니터 로컬 좌표로 변환한다.
    ///
    /// 현재 1차 POC에서는 Stream0 세션만 등록되지만,
    /// 등록 구조는 다중 모니터 확장이 가능하도록 StreamNo별로 관리한다.
    /// </summary>
    public sealed class TouchOverlayManager : IDisposable
    {
        private readonly object _syncLock = new object();
        private readonly GlobalPointerInputService _pointerInputService;
        private readonly Dictionary<int, SessionRegistration> _registrations;

        private bool _disposed;

        public TouchOverlayManager(
            GlobalPointerInputService pointerInputService)
        {
            if (pointerInputService == null)
            {
                throw new ArgumentNullException(
                    "pointerInputService");
            }

            _pointerInputService =
                pointerInputService;

            _registrations =
                new Dictionary<int, SessionRegistration>();

            _pointerInputService.PointerPressed +=
                OnPointerPressed;
        }

        /// <summary>
        /// 스트림과 모니터 영역, Pipe 세션을 연결한다.
        /// </summary>
        public void RegisterSession(
            int streamNo,
            MonitorInfo monitorInfo,
            TouchOverlayPipeSession session)
        {
            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (session == null)
                throw new ArgumentNullException("session");

            SessionRegistration registration =
                new SessionRegistration();

            registration.StreamNo = streamNo;
            registration.BoundsX = monitorInfo.BoundsX;
            registration.BoundsY = monitorInfo.BoundsY;
            registration.BoundsWidth = monitorInfo.BoundsWidth;
            registration.BoundsHeight = monitorInfo.BoundsHeight;
            registration.Session = session;

            lock (_syncLock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(
                        "TouchOverlayManager");
                }

                _registrations[streamNo] =
                    registration;
            }
        }

        /// <summary>
        /// 스트림 종료 시 좌표 전달 대상에서 제거한다.
        /// </summary>
        public void UnregisterSession(
            int streamNo)
        {
            lock (_syncLock)
            {
                _registrations.Remove(
                    streamNo);
            }
        }

        private void OnPointerPressed(
            PointerSample sample)
        {
            if (sample == null)
                return;

            SessionRegistration target =
                null;

            lock (_syncLock)
            {
                foreach (
                    KeyValuePair<int, SessionRegistration> pair
                    in _registrations)
                {
                    SessionRegistration registration =
                        pair.Value;

                    if (registration == null ||
                        registration.Session == null)
                    {
                        continue;
                    }

                    bool containsX =
                        sample.ScreenX >= registration.BoundsX &&
                        sample.ScreenX <
                        registration.BoundsX +
                        registration.BoundsWidth;

                    bool containsY =
                        sample.ScreenY >= registration.BoundsY &&
                        sample.ScreenY <
                        registration.BoundsY +
                        registration.BoundsHeight;

                    if (!containsX || !containsY)
                        continue;

                    target = registration;
                    break;
                }
            }

            if (target == null ||
                target.Session == null)
            {
                return;
            }

            int localX =
                sample.ScreenX -
                target.BoundsX;

            int localY =
                sample.ScreenY -
                target.BoundsY;

            /*
             * Named Pipe 프레임은 실제 모니터보다 작은 해상도를 사용할 수 있다.
             * 실제 화면 좌표를 오버레이 프레임 좌표로 비례 변환한다.
             */
            int overlayX =
                (int)Math.Round(
                    localX *
                    target.Session.Width /
                    (double)target.BoundsWidth);

            int overlayY =
                (int)Math.Round(
                    localY *
                    target.Session.Height /
                    (double)target.BoundsHeight);

            overlayX =
                Math.Max(
                    0,
                    Math.Min(
                        target.Session.Width - 1,
                        overlayX));

            overlayY =
                Math.Max(
                    0,
                    Math.Min(
                        target.Session.Height - 1,
                        overlayY));

            /*
             * 현재 단계에서는 IsTouch 값을 검사하지 않는다.
             * 마우스 클릭과 터치 모두 같은 포인터 입력으로 처리한다.
             */
            target.Session.ShowPointer(
                overlayX,
                overlayY);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _pointerInputService.PointerPressed -=
                    OnPointerPressed;
            }
            catch
            {
            }

            lock (_syncLock)
            {
                _registrations.Clear();
                _disposed = true;
            }
        }

        private sealed class SessionRegistration
        {
            public int StreamNo;
            public int BoundsX;
            public int BoundsY;
            public int BoundsWidth;
            public int BoundsHeight;
            public TouchOverlayPipeSession Session;
        }
    }
}
