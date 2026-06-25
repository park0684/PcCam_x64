using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// WH_MOUSE_LL 전역 마우스 후크를 사용하여 포인터 Down 좌표를 감지한다.
    ///
    /// 1차 POC 역할:
    /// - 전역 왼쪽 버튼 Down 감지
    /// - Windows 가상 화면 좌표 전달
    /// - dwExtraInfo 서명을 이용한 터치 승격 입력 구분
    ///
    /// 주의:
    /// - 후크 콜백에서는 로그 기록이나 영상 처리를 직접 수행하지 않는다.
    /// - 이벤트 전달은 ThreadPool로 넘겨 후크 콜백을 빠르게 반환한다.
    /// - 일부 POS 터치 드라이버는 터치를 일반 마우스로만 전달할 수 있다.
    /// </summary>
    public sealed class GlobalPointerInputService : IDisposable
    {
        private const int WhMouseLl = 14;
        private const int WmLeftButtonDown = 0x0201;

        /*
         * Microsoft Tablet PC 입력 서명.
         *
         * 상위 24비트가 MI_WP_SIGNATURE와 같으면
         * 펜 또는 터치에서 승격된 마우스 메시지이다.
         * 하위 바이트의 0x80 플래그가 있으면 터치 입력이다.
         */
        private const uint MiWpSignature = 0xFF515700;
        private const uint SignatureMask = 0xFFFFFF00;
        private const uint TouchFlag = 0x00000080;

        private readonly object _syncLock = new object();
        private readonly LowLevelMouseProc _hookCallback;

        private IntPtr _hookHandle;
        private bool _started;
        private bool _disposed;

        public GlobalPointerInputService()
        {
            /*
             * 네이티브 후크가 사용하는 delegate가 GC에 수집되지 않도록
             * 서비스 생명주기 동안 필드로 유지한다.
             */
            _hookCallback = HookCallback;
        }

        /// <summary>
        /// 포인터 Down 이벤트.
        /// 후크 콜백 스레드가 아니라 ThreadPool에서 호출된다.
        /// </summary>
        public event Action<PointerSample> PointerPressed;

        /// <summary>
        /// 전역 입력 감지를 시작한다.
        /// </summary>
        public void Start()
        {
            lock (_syncLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("GlobalPointerInputService");

                if (_started)
                    return;

                IntPtr moduleHandle = GetModuleHandle(null);

                _hookHandle = SetWindowsHookEx(
                    WhMouseLl,
                    _hookCallback,
                    moduleHandle,
                    0);

                if (_hookHandle == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastWin32Error();

                    throw new Win32Exception(
                        errorCode,
                        "전역 포인터 입력 후크를 등록하지 못했습니다.");
                }

                _started = true;
            }
        }

        /// <summary>
        /// 전역 입력 감지를 중지한다.
        /// </summary>
        public void Stop()
        {
            IntPtr hookHandle;

            lock (_syncLock)
            {
                hookHandle = _hookHandle;
                _hookHandle = IntPtr.Zero;
                _started = false;
            }

            if (hookHandle == IntPtr.Zero)
                return;

            if (!UnhookWindowsHookEx(hookHandle))
            {
                int errorCode = Marshal.GetLastWin32Error();

                throw new Win32Exception(
                    errorCode,
                    "전역 포인터 입력 후크를 해제하지 못했습니다.");
            }
        }

        /// <summary>
        /// Windows 저수준 마우스 후크 콜백.
        /// </summary>
        private IntPtr HookCallback(
            int nCode,
            IntPtr wParam,
            IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 &&
                    wParam.ToInt64() == WmLeftButtonDown)
                {
                    MouseLowLevelHookData hookData =
                        (MouseLowLevelHookData)Marshal.PtrToStructure(
                            lParam,
                            typeof(MouseLowLevelHookData));

                    uint extraInfo = unchecked(
                        (uint)hookData.ExtraInfo.ToUInt64());

                    PointerSample sample = new PointerSample();
                    sample.ScreenX = hookData.Point.X;
                    sample.ScreenY = hookData.Point.Y;
                    sample.OccurredAt = DateTime.Now;
                    sample.IsTouch = IsTouchGeneratedMouseMessage(extraInfo);
                    sample.ExtraInfo = extraInfo;

                    QueuePointerPressed(sample);
                }
            }
            catch
            {
                /*
                 * 입력 후크 콜백의 예외가 사용자 입력과 프로그램 전체를
                 * 중단시키지 않도록 여기서는 삼킨다.
                 */
            }

            /*
             * 다른 프로그램의 전역 후크도 정상적으로 입력을 받을 수 있도록
             * 반드시 다음 후크로 전달한다.
             */
            return CallNextHookEx(
                _hookHandle,
                nCode,
                wParam,
                lParam);
        }

        /// <summary>
        /// 후크 콜백 외부의 작업 스레드에서 구독자에게 입력을 전달한다.
        /// </summary>
        private void QueuePointerPressed(
            PointerSample sample)
        {
            ThreadPool.QueueUserWorkItem(
                delegate (object state)
                {
                    PointerSample queuedSample = state as PointerSample;
                    if (queuedSample == null)
                        return;

                    Action<PointerSample> handler = PointerPressed;
                    if (handler == null)
                        return;

                    try
                    {
                        handler(queuedSample);
                    }
                    catch
                    {
                        // 구독자의 오류가 전역 입력 감지를 중단시키지 않도록 무시한다.
                    }
                },
                sample);
        }

        /// <summary>
        /// dwExtraInfo 값이 터치에서 승격된 마우스 메시지인지 확인한다.
        /// </summary>
        private bool IsTouchGeneratedMouseMessage(
            uint extraInfo)
        {
            bool isPenOrTouch =
                (extraInfo & SignatureMask) == MiWpSignature;

            bool hasTouchFlag =
                (extraInfo & TouchFlag) == TouchFlag;

            return isPenOrTouch && hasTouchFlag;
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
                // 종료 단계에서는 후크 해제 오류를 외부로 다시 던지지 않는다.
            }

            _disposed = true;
        }

        private delegate IntPtr LowLevelMouseProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseLowLevelHookData
        {
            public NativePoint Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookId,
            LowLevelMouseProc hookCallback,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(
            IntPtr hookHandle);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Auto)]
        private static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Auto,
            SetLastError = true)]
        private static extern IntPtr GetModuleHandle(
            string moduleName);
    }
}