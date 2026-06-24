using System;
using PcCam_x64.Models;
using PccAuthClient.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 인증 DLL 호출 어댑터.
    /// 
    /// pccam_32 본체는 인증서버 통신 구조를 직접 알지 않는다.
    /// 인증 등록, 실행 가능 여부 확인, 로컬 인증 제거는 모두 PccAuthClient.dll을 통해 수행한다.
    /// 
    /// 이 클래스는 PccAuthClient.dll의 결과 모델을
    /// pccam_32 내부 AuthResult 모델로 변환하는 역할을 담당한다.
    /// </summary>
    public class AuthDllAdapter
    {
        private readonly PccAuthClient.PccAuthClient _client;

        /// <summary>
        /// 인증 DLL 어댑터를 생성한다.
        /// 
        /// 기본 PccAuthClient 옵션을 사용한다.
        /// 기본 로컬 인증 저장 위치는 C:\ProgramData\POSCAM\Auth\auth_state.dat 이다.
        /// </summary>
        public AuthDllAdapter()
        {
            _client = new PccAuthClient.PccAuthClient();
        }

        /// <summary>
        /// 현재 PC CAM 실행 가능 여부를 확인한다.
        /// 
        /// 처리 내용:
        /// 1. PccAuthClient.CheckStartup() 호출
        /// 2. 로컬 인증정보 확인
        /// 3. 인증서버 Verify API 확인
        /// 4. 결과를 pccam_32 내부 AuthResult로 변환
        /// </summary>
        /// <param name="authConfig">
        /// pccam_32 INI 인증 설정.
        /// 현재 실제 인증 판단은 PccAuthClient의 로컬 인증 상태 파일을 기준으로 수행한다.
        /// </param>
        /// <returns>
        /// pccam_32 내부 인증 결과.
        /// </returns>
        public AuthResult CheckCanRun(AuthConfig authConfig)
        {
            try
            {
                PccamAuthorizationResult result = _client.CheckStartup();

                if (result != null && result.CanRun)
                    return AuthResult.Success(result.Message,
                result.NextCheckAt);

                string statusCode = result == null
                    ? "AUTH_RESULT_EMPTY"
                    : result.Status.ToString();

                string message = result == null
                    ? "인증 결과가 없습니다."
                    : result.Message;

                return AuthResult.Fail(statusCode, message);
            }
            catch (Exception ex)
            {
                return AuthResult.Fail(
                    "AUTH_CHECK_ERROR",
                    "인증 상태 확인 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        /// <summary>
        /// 인증키와 장비명을 이용해 PC CAM 인증 등록을 수행한다.
        /// 
        /// 처리 내용:
        /// 1. PccAuthClient.Register() 호출
        /// 2. 인증서버 Activate API 호출
        /// 3. 서버 발급 토큰을 로컬 인증 상태 파일에 저장
        /// 4. 결과를 pccam_32 내부 AuthResult로 변환
        /// </summary>
        /// <param name="licenseKey">
        /// 사용자가 입력한 인증키.
        /// </param>
        /// <param name="deviceName">
        /// 인증서버에 등록할 장비명.
        /// </param>
        /// <returns>
        /// 인증 등록 결과.
        /// </returns>
        public AuthResult TryActivate(string licenseKey, string deviceName)
        {
            try
            {
                PccamRegistrationResult result =
                    _client.Register(licenseKey, deviceName);

                if (result != null && result.IsSuccess && result.CanRun)
                    return AuthResult.Success(result.Message);

                string statusCode = result == null
                    ? "AUTH_REGISTER_RESULT_EMPTY"
                    : result.Status.ToString();

                string message = result == null
                    ? "인증 등록 결과가 없습니다."
                    : result.Message;

                return AuthResult.Fail(statusCode, message);
            }
            catch (Exception ex)
            {
                return AuthResult.Fail(
                    "AUTH_REGISTER_ERROR",
                    "인증 등록 중 오류가 발생했습니다. " + ex.Message);
            }
        }

        /// <summary>
        /// 로컬 인증정보가 등록되어 있는지 확인한다.
        /// 
        /// 인증 버튼 표시 기준으로 사용된다.
        /// 
        /// true이면 설정 화면 인증 버튼은 제거로 표시하고,
        /// false이면 등록으로 표시한다.
        /// </summary>
        /// <param name="authConfig">
        /// pccam_32 INI 인증 설정.
        /// 현재 판단에는 사용하지 않는다.
        /// </param>
        /// <returns>
        /// true: 로컬 인증정보 있음
        /// false: 로컬 인증정보 없음
        /// </returns>
        public bool HasLocalAuth(AuthConfig authConfig)
        {
            try
            {
                RegistrationView view = _client.GetRegistrationView();

                return view != null && view.IsRegistered;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 로컬 인증정보를 제거한다.
        /// 
        /// 이 작업은 서버의 장비 귀속 해제가 아니라
        /// 현재 PC의 로컬 인증 상태 파일만 삭제한다.
        /// </summary>
        public void RemoveLocalAuth()
        {
            _client.ClearLocalAuth();
        }

        /// <summary>
        /// 현재 PC의 HardwareId를 반환한다.
        /// 
        /// 현장 인증 문제 분석 시 로그 또는 안내 메시지에 사용할 수 있다.
        /// </summary>
        /// <returns>
        /// 현재 PC의 HardwareId.
        /// </returns>
        public string GetHardwareId()
        {
            return _client.GetHardwareId();
        }

        /// <summary>
        /// 로컬 인증 상태 파일 경로를 반환한다.
        /// 
        /// 진단 또는 로그 확인용으로 사용할 수 있다.
        /// </summary>
        /// <returns>
        /// 로컬 인증 상태 파일 전체 경로.
        /// </returns>
        public string GetLocalAuthFilePath()
        {
            return _client.GetLocalAuthFilePath();
        }

        /// <summary>
        /// 등록된 인증키 전체 값을 반환한다.
        /// 
        /// PccAuthClient.dll의 로컬 인증 상태 파일에서 인증키를 읽어온다.
        /// 등록 정보가 없으면 빈 문자열을 반환한다.
        /// </summary>
        /// <returns>
        /// 등록된 인증키 전체 값.
        /// </returns>
        public string GetRegisteredLicenseKey()
        {
            try
            {
                RegistrationView view = _client.GetRegistrationView();

                if (view == null || !view.IsRegistered)
                    return "";

                return view.LicenseKey ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 등록된 장비명을 반환한다.
        /// 
        /// PccAuthClient.dll의 로컬 인증 상태 파일에서 장비명을 읽어온다.
        /// </summary>
        /// <returns>
        /// 등록된 장비명.
        /// 등록 정보가 없으면 빈 문자열.
        /// </returns>
        public string GetRegisteredDeviceName()
        {
            try
            {
                RegistrationView view = _client.GetRegistrationView();

                if (view == null || !view.IsRegistered)
                    return "";

                return view.DeviceName ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// PC CAM 실행 중 재인증을 확인한다.
        /// 
        /// 재인증 시점 판단은 PccAuthClient.dll 내부 정책에 따른다.
        /// PC CAM 본체는 이 메서드를 호출하고 결과의 NextCheckAt을 기준으로 다음 호출만 예약한다.
        /// </summary>
        /// <returns>
        /// 인증 확인 결과.
        /// </returns>
        public AuthResult CheckRuntime()
        {
            try
            {
                PccAuthClient.Models.PccamAuthorizationResult result =
                    _client.CheckRuntime();

                if (result != null && result.CanRun)
                {
                    return AuthResult.Success(
                        result.Message,
                        result.NextCheckAt);
                }

                string statusCode = result == null
                    ? "AUTH_RUNTIME_RESULT_EMPTY"
                    : result.Status.ToString();

                string message = result == null
                    ? "실행 중 인증 결과가 없습니다."
                    : result.Message;

                return AuthResult.Fail(statusCode, message);
            }
            catch (Exception ex)
            {
                return AuthResult.Fail(
                    "AUTH_RUNTIME_CHECK_ERROR",
                    "실행 중 인증 확인 중 오류가 발생했습니다. " + ex.Message);
            }
        }
    }
}