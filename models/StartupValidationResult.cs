using System.Collections.Generic;
using System.Text;

namespace PcCam_x64.Models
{
    /// <summary>
    /// 프로그램 시작 환경 검사 결과.
    /// 
    /// 오류는 송출 실행에 영향을 줄 수 있는 문제이고,
    /// 경고는 프로그램 실행은 가능하지만 확인이 필요한 문제이다.
    /// </summary>
    public class StartupValidationResult
    {
        /// <summary>
        /// 송출 또는 프로그램 실행에 영향을 줄 수 있는 오류 목록.
        /// </summary>
        public List<string> Errors { get; private set; }

        /// <summary>
        /// 사용자가 확인해야 하는 경고 목록.
        /// </summary>
        public List<string> Warnings { get; private set; }

        /// <summary>
        /// 시작 환경 검사 결과 객체를 생성한다.
        /// </summary>
        public StartupValidationResult()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        /// <summary>
        /// 오류가 없는지 여부를 반환한다.
        /// </summary>
        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }

        /// <summary>
        /// 경고가 존재하는지 여부를 반환한다.
        /// </summary>
        public bool HasWarnings
        {
            get { return Warnings.Count > 0; }
        }

        /// <summary>
        /// 오류 메시지를 추가한다.
        /// </summary>
        /// <param name="message">추가할 오류 메시지.</param>
        public void AddError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Errors.Add(message);
        }

        /// <summary>
        /// 경고 메시지를 추가한다.
        /// </summary>
        /// <param name="message">추가할 경고 메시지.</param>
        public void AddWarning(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Warnings.Add(message);
        }

        /// <summary>
        /// 오류와 경고를 사용자 표시용 문자열로 변환한다.
        /// </summary>
        /// <returns>검사 결과 요약 문자열.</returns>
        public string ToDisplayMessage()
        {
            StringBuilder sb = new StringBuilder();

            if (Errors.Count > 0)
            {
                sb.AppendLine("[오류]");
                foreach (string error in Errors)
                {
                    sb.AppendLine("- " + error);
                }

                sb.AppendLine();
            }

            if (Warnings.Count > 0)
            {
                sb.AppendLine("[경고]");
                foreach (string warning in Warnings)
                {
                    sb.AppendLine("- " + warning);
                }
            }

            return sb.ToString();
        }
    }
}