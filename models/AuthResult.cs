using System;

namespace PcCam_x64.Models
{
    /// <summary>
    /// PC CAM 내부 인증 결과 모델.
    /// 
    /// PccAuthClient.dll의 인증 결과를 PC CAM 내부에서 사용하기 위한 모델이다.
    /// </summary>
    public class AuthResult
    {
        /// <summary>
        /// 인증 성공 여부.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 결과 코드.
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// 결과 메시지.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 다음 인증 확인 예정 시각.
        /// 
        /// 이 값은 PccAuthClient.dll에서 전달받은 값을 그대로 사용한다.
        /// </summary>
        public DateTime? NextCheckAt { get; set; }

        /// <summary>
        /// 인증 성공 결과를 생성한다.
        /// </summary>
        /// <param name="message">
        /// 결과 메시지.
        /// </param>
        /// <returns>
        /// 인증 성공 결과.
        /// </returns>
        public static AuthResult Success(string message)
        {
            return Success(message, null);
        }

        /// <summary>
        /// 인증 성공 결과를 생성한다.
        /// </summary>
        /// <param name="message">
        /// 결과 메시지.
        /// </param>
        /// <param name="nextCheckAt">
        /// 다음 인증 확인 예정 시각.
        /// </param>
        /// <returns>
        /// 인증 성공 결과.
        /// </returns>
        public static AuthResult Success(string message, DateTime? nextCheckAt)
        {
            return new AuthResult
            {
                IsSuccess = true,
                Code = "OK",
                Message = message ?? "",
                NextCheckAt = nextCheckAt
            };
        }

        /// <summary>
        /// 인증 실패 결과를 생성한다.
        /// </summary>
        /// <param name="code">
        /// 실패 코드.
        /// </param>
        /// <param name="message">
        /// 실패 메시지.
        /// </param>
        /// <returns>
        /// 인증 실패 결과.
        /// </returns>
        public static AuthResult Fail(string code, string message)
        {
            return new AuthResult
            {
                IsSuccess = false,
                Code = code ?? "",
                Message = message ?? "",
                NextCheckAt = null
            };
        }

        /// <summary>
        /// 인증 성공 여부.
        /// 
        /// PccAuthClient.dll의 CanRun 명칭과 호환하기 위한 읽기 전용 별칭이다.
        /// </summary>
        public bool CanRun
        {
            get { return IsSuccess; }
        }
    }
}