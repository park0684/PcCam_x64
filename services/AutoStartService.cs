using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PcCam_x64.Services
{
    /// <summary>
    /// Windows 시작 시 PC CAM 자동실행 등록/해제를 담당하는 서비스.
    /// 
    /// 1단계에서는 관리자 권한이 필요 없는 HKCU Run 레지스트리 방식을 사용한다.
    /// 
    /// 등록 위치:
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    /// 
    /// 이 방식은 현재 로그인한 Windows 사용자 기준으로 자동실행된다.
    /// </summary>
    public class AutoStartService
    {
        private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "PC CAM";

        /// <summary>
        /// 자동실행 설정을 적용한다.
        /// 
        /// enable 값이 true이면 자동실행을 등록하고,
        /// false이면 자동실행 등록을 해제한다.
        /// </summary>
        /// <param name="enable">
        /// true: Windows 시작 시 자동실행 등록
        /// false: 자동실행 등록 해제
        /// </param>
        public void Apply(bool enable)
        {
            if (enable)
            {
                Register();
            }
            else
            {
                Unregister();
            }
        }

        /// <summary>
        /// 현재 실행 중인 PC CAM 실행 파일을 Windows 시작프로그램에 등록한다.
        /// 
        /// 등록 값은 실행 파일 전체 경로를 큰따옴표로 감싼 형태로 저장한다.
        /// 예:
        /// "C:\PC_CAM\pccam_32.exe"
        /// </summary>
        public void Register()
        {
            string exePath = Application.ExecutablePath;

            if (string.IsNullOrWhiteSpace(exePath))
                throw new InvalidOperationException("프로그램 실행 파일 경로를 확인할 수 없습니다.");

            string runValue = "\"" + exePath + "\"";

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
            {
                if (key == null)
                    throw new InvalidOperationException("자동실행 레지스트리 경로를 열 수 없습니다.");

                key.SetValue(RunValueName, runValue, RegistryValueKind.String);
            }
        }

        /// <summary>
        /// Windows 시작프로그램에 등록된 PC CAM 자동실행 항목을 제거한다.
        /// 
        /// 등록된 값이 없어도 예외를 발생시키지 않는다.
        /// </summary>
        public void Unregister()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true))
            {
                if (key == null)
                    return;

                object value = key.GetValue(RunValueName);

                if (value != null)
                    key.DeleteValue(RunValueName, false);
            }
        }

        /// <summary>
        /// PC CAM 자동실행 등록 여부를 확인한다.
        /// 
        /// 현재 실행 파일 경로와 레지스트리에 등록된 경로가 일치하는 경우 true를 반환한다.
        /// </summary>
        /// <returns>
        /// true: 현재 PC CAM 실행 파일이 자동실행으로 등록되어 있음
        /// false: 등록되어 있지 않거나 다른 경로가 등록되어 있음
        /// </returns>
        public bool IsRegistered()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, false))
            {
                if (key == null)
                    return false;

                object value = key.GetValue(RunValueName);

                if (value == null)
                    return false;

                string registeredValue = Convert.ToString(value) ?? "";
                string currentValue = "\"" + Application.ExecutablePath + "\"";

                return string.Equals(
                    registeredValue,
                    currentValue,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}