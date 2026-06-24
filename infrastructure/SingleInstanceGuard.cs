using System;
using System.Threading;

namespace PcCam_x64.Infrastructure
{
    /// <summary>
    /// PC CAM 프로그램의 중복 실행을 방지하는 클래스.
    /// 
    /// PC CAM은 MediaMTX RTSP 포트와 FFmpeg 캡처 프로세스를 사용하므로,
    /// 프로그램이 여러 개 실행되면 포트 충돌이나 중복 송출 문제가 발생할 수 있다.
    /// 
    /// 이 클래스는 Mutex를 이용하여 현재 PC에서 PC CAM이 이미 실행 중인지 확인한다.
    /// </summary>
    public class SingleInstanceGuard : IDisposable
    {
        private Mutex _mutex;
        private bool _hasHandle;
        private bool _disposed;

        /// <summary>
        /// Mutex 이름.
        /// 
        /// 동일한 이름의 Mutex가 이미 존재하면 프로그램이 이미 실행 중인 것으로 판단한다.
        /// </summary>
        private const string MutexName = "pccam_32_single_instance_mutex";

        /// <summary>
        /// 단일 실행 권한을 획득한다.
        /// 
        /// 반환값이 true이면 현재 프로세스가 실행 가능하다.
        /// 반환값이 false이면 이미 다른 PC CAM 프로세스가 실행 중인 상태이다.
        /// </summary>
        /// <returns>
        /// true: 실행 가능
        /// false: 이미 실행 중
        /// </returns>
        public bool TryAcquire()
        {
            if (_disposed)
                throw new ObjectDisposedException("SingleInstanceGuard");

            if (_mutex != null && _hasHandle)
                return true;

            bool createdNew;

            _mutex = new Mutex(
                true,
                MutexName,
                out createdNew);

            _hasHandle = createdNew;

            return _hasHandle;
        }

        /// <summary>
        /// 획득한 Mutex를 해제한다.
        /// 
        /// 프로그램 종료 시 호출하여 다른 실행이 가능하도록 한다.
        /// </summary>
        public void Release()
        {
            if (_mutex == null)
                return;

            if (_hasHandle)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // 이미 해제되었거나 소유권이 없는 경우일 수 있으므로 무시한다.
                }

                _hasHandle = false;
            }
        }

        /// <summary>
        /// Mutex 리소스를 해제한다.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Release();

            if (_mutex != null)
            {
                try
                {
                    _mutex.Dispose();
                }
                catch
                {
                }

                _mutex = null;
            }

            _disposed = true;
        }
    }
}