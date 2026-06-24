using System;
using System.IO;
using PcCam_x64.Infrastructure;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 로컬 인증토큰 저장 경로를 제공하는 서비스.
    /// 
    /// 인증모듈 연동 후에는 인증 성공 시 지정된 위치에 인증토큰이 저장된다.
    /// PC CAM 본체는 인증키 TextBox 값이나 INI의 LastAuthResult가 아니라,
    /// 이 토큰 파일의 존재 여부를 기준으로 인증 등록 상태를 판단한다.
    /// </summary>
    public class AuthTokenPathProvider
    {
        private readonly PathProvider _pathProvider;

        /// <summary>
        /// 인증토큰 경로 제공자를 생성한다.
        /// </summary>
        /// <param name="pathProvider">
        /// PC CAM의 기본 경로 제공자.
        /// </param>
        public AuthTokenPathProvider(PathProvider pathProvider)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            _pathProvider = pathProvider;
        }

        /// <summary>
        /// 로컬 인증토큰 파일의 전체 경로를 반환한다.
        /// 
        /// 현재 단계에서는 config 폴더 아래에 토큰 파일을 둔다.
        /// 향후 인증모듈 정책에 따라 ProgramData, AppData 또는 별도 보안 폴더로 변경할 수 있다.
        /// </summary>
        /// <returns>
        /// 인증토큰 파일 전체 경로.
        /// </returns>
        public string GetTokenFilePath()
        {
            return Path.Combine(
                _pathProvider.ConfigDirectory,
                "pccam.auth.token");
        }

        /// <summary>
        /// 인증토큰 파일이 존재하는지 확인한다.
        /// 
        /// 이 메서드는 토큰의 유효성 검증까지 수행하지 않는다.
        /// 토큰 유효성 검증은 향후 인증모듈의 CheckCanRun 또는 VerifyToken 기능에서 처리한다.
        /// </summary>
        /// <returns>
        /// true: 인증토큰 파일 존재
        /// false: 인증토큰 파일 없음
        /// </returns>
        public bool ExistsToken()
        {
            string tokenPath = GetTokenFilePath();

            return File.Exists(tokenPath);
        }

        /// <summary>
        /// 로컬 인증토큰 파일을 삭제한다.
        /// 
        /// 설정 화면의 제거 버튼에서 사용한다.
        /// 이 작업은 서버의 장비 귀속 해제가 아니라 현재 PC의 로컬 인증토큰만 제거한다.
        /// </summary>
        public void DeleteToken()
        {
            string tokenPath = GetTokenFilePath();

            if (File.Exists(tokenPath))
                File.Delete(tokenPath);
        }

        /// <summary>
        /// 개발 테스트용 인증토큰 파일을 생성한다.
        /// 
        /// 실제 인증모듈이 완성되면 이 메서드는 사용하지 않고,
        /// 인증모듈이 직접 토큰을 생성하거나 저장하게 된다.
        /// </summary>
        /// <param name="tokenValue">
        /// 테스트용으로 저장할 토큰 문자열.
        /// </param>
        public void WriteTestToken(string tokenValue)
        {
            _pathProvider.EnsureDirectories();

            string tokenPath = GetTokenFilePath();

            File.WriteAllText(
                tokenPath,
                tokenValue ?? "");
        }
    }
}