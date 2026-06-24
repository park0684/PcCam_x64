using System;
using System.Runtime.InteropServices;

namespace PcCam_x64.Services
{
    /// <summary>
    /// Windows 절전모드 방지 정책을 제어하는 서비스.
    /// 
    /// PC CAM은 POS 화면을 지속적으로 캡처해야 하므로,
    /// 프로그램 실행 중 시스템 절전 또는 화면 꺼짐이 발생하면 송출 영상이 중단되거나
    /// 검은 화면이 녹화될 수 있다.
    /// 
    /// 이 서비스는 Windows API SetThreadExecutionState를 사용하여
    /// 프로그램 실행 중 절전모드 진입을 방지한다.
    /// </summary>
    public class PowerPolicyService
    {
        /// <summary>
        /// 실행 상태를 계속 유지한다.
        /// 이 플래그를 함께 지정해야 설정이 지속 적용된다.
        /// </summary>
        private const uint ES_CONTINUOUS = 0x80000000;

        /// <summary>
        /// 시스템 절전모드 진입을 방지한다.
        /// </summary>
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        /// <summary>
        /// 디스플레이 꺼짐을 방지한다.
        /// POS 화면 녹화 목적상 화면 꺼짐도 방지하는 것이 안전하다.
        /// </summary>
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private bool _isPreventSleepEnabled;

        /// <summary>
        /// Windows API를 호출하여 현재 스레드 기준 실행 상태를 설정한다.
        /// 
        /// PC CAM에서는 프로그램이 실행되는 동안 시스템 절전과 디스플레이 꺼짐을 방지하기 위해 사용한다.
        /// </summary>
        /// <param name="esFlags">
        /// 적용할 실행 상태 플래그.
        /// 예: ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
        /// </param>
        /// <returns>
        /// 이전 실행 상태 값.
        /// 0이면 호출 실패로 판단할 수 있다.
        /// </returns>
        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        /// <summary>
        /// 절전모드 방지 설정을 적용하거나 해제한다.
        /// 
        /// preventSleep 값이 true이면 시스템 절전과 디스플레이 꺼짐을 방지한다.
        /// false이면 이전에 적용한 절전 방지 상태를 해제한다.
        /// </summary>
        /// <param name="preventSleep">
        /// true: 절전모드 방지 적용
        /// false: 절전모드 방지 해제
        /// </param>
        public void Apply(bool preventSleep)
        {
            if (preventSleep)
            {
                EnablePreventSleep();
            }
            else
            {
                DisablePreventSleep();
            }
        }

        /// <summary>
        /// 시스템 절전모드 및 디스플레이 꺼짐 방지를 적용한다.
        /// 
        /// PC CAM은 화면 녹화 프로그램이므로 시스템 절전뿐 아니라
        /// 화면 꺼짐까지 방지하는 것이 기본 정책에 적합하다.
        /// </summary>
        public void EnablePreventSleep()
        {
            uint result = SetThreadExecutionState(
                ES_CONTINUOUS |
                ES_SYSTEM_REQUIRED |
                ES_DISPLAY_REQUIRED);

            if (result == 0)
            {
                throw new InvalidOperationException("절전모드 방지 설정을 적용하지 못했습니다.");
            }

            _isPreventSleepEnabled = true;
        }

        /// <summary>
        /// 이전에 적용한 절전모드 방지 설정을 해제한다.
        /// 
        /// ES_CONTINUOUS만 지정하면 현재 프로세스에서 요청한 절전 방지 상태가 해제된다.
        /// </summary>
        public void DisablePreventSleep()
        {
            uint result = SetThreadExecutionState(ES_CONTINUOUS);

            if (result == 0)
            {
                throw new InvalidOperationException("절전모드 방지 설정을 해제하지 못했습니다.");
            }

            _isPreventSleepEnabled = false;
        }

        /// <summary>
        /// 현재 프로그램에서 절전모드 방지를 적용 중인지 여부를 반환한다.
        /// 
        /// 이 값은 Windows 전체 전원 정책 상태가 아니라,
        /// PC CAM 프로그램이 마지막으로 적용한 상태를 의미한다.
        /// </summary>
        /// <returns>
        /// true: PC CAM이 절전모드 방지 적용 중
        /// false: PC CAM이 절전모드 방지 미적용 상태
        /// </returns>
        public bool IsPreventSleepEnabled()
        {
            return _isPreventSleepEnabled;
        }
    }
}