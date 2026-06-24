using System;
using System.IO;

namespace PcCam_x64.Infrastructure
{
    /// <summary>
    /// 프로그램에서 사용하는 주요 폴더와 파일 경로를 관리한다.
    /// 
    /// 설정 파일, 로그 파일, 외부 실행 파일 경로를 한 곳에서 관리한다.
    /// </summary>
    public class PathProvider
    {
        /// <summary>
        /// 프로그램 실행 폴더.
        /// 예: bin\x86\Debug 또는 실제 설치 폴더
        /// </summary>
        public string BaseDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>
        /// 설정 파일 저장 폴더.
        /// </summary>
        public string ConfigDirectory
        {
            get { return Path.Combine(BaseDirectory, "config"); }
        }

        /// <summary>
        /// 로그 저장 폴더.
        /// </summary>
        public string LogDirectory
        {
            get { return Path.Combine(BaseDirectory, "logs"); }
        }

        /// <summary>
        /// FFmpeg, MediaMTX 같은 외부 실행 파일 보관 폴더.
        /// </summary>
        public string ExternalDirectory
        {
            get { return Path.Combine(BaseDirectory, "External"); }
        }

        /// <summary>
        /// 실행 중에만 사용하는 런타임 파일 저장 폴더.
        ///
        /// 사용자별 LocalAppData에 저장하므로
        /// 다른 일반 Windows 사용자가 직접 접근하기 어렵다.
        ///
        /// 예:
        /// C:\Users\사용자명\AppData\Local\POSCAM\PCCAM\Runtime
        /// </summary>
        public string RuntimeDirectory
        {
            get
            {
                string localAppData =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData);

                return Path.Combine(
                    localAppData,
                    "POSCAM",
                    "PCCAM",
                    "Runtime");
            }
        }

        /// <summary>
        /// PC CAM INI 설정 파일 경로.
        /// </summary>
        public string AppConfigFilePath
        {
            get { return Path.Combine(ConfigDirectory, "pccam.ini"); }
        }

        /// <summary>
        /// FFmpeg 실행 파일 경로.
        /// </summary>
        public string FfmpegExePath
        {
            get { return Path.Combine(ExternalDirectory, "ffmpeg.exe"); }
        }

        /// <summary>
        /// MediaMTX 실행 파일 경로.
        /// </summary>
        public string MediaMtxExePath
        {
            get { return Path.Combine(ExternalDirectory, "mediamtx.exe"); }
        }

        /// <summary>
        /// 실행 중 MediaMTX가 읽을 런타임 설정 파일 경로.
        ///
        /// 인증정보가 포함되므로 설치 폴더나 External 폴더에 저장하지 않는다.
        /// </summary>
        public string MediaMtxConfigPath
        {
            get
            {
                return Path.Combine(
                    RuntimeDirectory,
                    "mediamtx.runtime.yml");
            }
        }

        /// <summary>
        /// 프로그램에서 필요한 기본 폴더를 생성한다.
        /// </summary>
        public void EnsureDirectories()
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            if (!Directory.Exists(LogDirectory))
                Directory.CreateDirectory(LogDirectory);

            if (!Directory.Exists(ExternalDirectory))
                Directory.CreateDirectory(ExternalDirectory);

            /*
             * MediaMTX 인증 설정을 저장할 사용자 전용 런타임 폴더.
             */
            if (!Directory.Exists(RuntimeDirectory))
                Directory.CreateDirectory(RuntimeDirectory);
        }

        /// <summary>
        /// 현재 실행 중인 PCCAM 실행 파일 경로를 반환한다.
        /// </summary>
        public string AppExePath
        {
            get { return System.Reflection.Assembly.GetEntryAssembly().Location; }
        }

        /// <summary>
        /// MediaMTX 실행 파일 경로를 반환한다.
        /// </summary>
        public string GetExternalFilePath(string fileName)
        {
            return Path.Combine(BaseDirectory, "External", fileName);
        }
    }
}