using System;
using System.Diagnostics;
using System.IO;
using PcCam_x64.Infrastructure;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 외부 실행 프로세스의 잔류 상태를 정리하는 서비스.
    /// 
    /// PC CAM이 비정상 종료되거나 디버깅 중 강제 종료되면
    /// ffmpeg.exe 또는 mediamtx.exe가 남아 있을 수 있다.
    /// 
    /// 이 서비스는 프로그램 시작 시 PC CAM의 External 폴더에서 실행된 프로세스만 찾아 종료한다.
    /// ffmpeg.exe는 다른 프로그램에서도 사용할 수 있으므로 프로세스 이름만 보고 종료하지 않고,
    /// 실행 파일 전체 경로가 PC CAM의 External 폴더 경로와 일치하는 경우에만 종료한다.
    /// </summary>
    public class ExternalProcessCleanupService
    {
        private readonly PathProvider _pathProvider;
        private readonly LogService _logService;

        public ExternalProcessCleanupService(
            PathProvider pathProvider,
            LogService logService)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            if (logService == null)
                throw new ArgumentNullException("logService");

            _pathProvider = pathProvider;
            _logService = logService;
        }

        /// <summary>
        /// PC CAM에서 사용하던 외부 프로세스 잔류 여부를 확인하고 정리한다.
        /// 
        /// 정리 대상:
        /// 1. External 폴더의 ffmpeg.exe
        /// 2. External 폴더의 MediaMTX 실행 파일
        /// 
        /// MediaMTX 실행 파일명은 설정값을 우선 사용하고,
        /// 설정이 비어 있으면 PathProvider의 기본값을 사용한다.
        /// </summary>
        /// <param name="config">
        /// 현재 로드된 PC CAM 설정.
        /// MediaMTX 실행 파일명을 확인하기 위해 사용한다.
        /// </param>
        public void CleanupLeftoverProcesses(AppConfig config)
        {
            _pathProvider.EnsureDirectories();

            string ffmpegPath = _pathProvider.FfmpegExePath;
            string mediaMtxPath = ResolveMediaMtxExePath(config);

            KillProcessByExactPath(ffmpegPath);
            KillProcessByExactPath(mediaMtxPath);
        }

        /// <summary>
        /// MediaMTX 실행 파일 전체 경로를 구한다.
        /// 
        /// 설정 파일에 RtspServer.ServerExeName이 있으면 해당 값을 사용하고,
        /// 값이 없으면 PathProvider.MediaMtxExePath를 사용한다.
        /// </summary>
        /// <param name="config">
        /// 현재 로드된 PC CAM 설정.
        /// </param>
        /// <returns>
        /// MediaMTX 실행 파일 전체 경로.
        /// </returns>
        private string ResolveMediaMtxExePath(AppConfig config)
        {
            if (config != null &&
                config.RtspServer != null &&
                !string.IsNullOrWhiteSpace(config.RtspServer.ServerExeName))
            {
                return Path.Combine(
                    _pathProvider.ExternalDirectory,
                    config.RtspServer.ServerExeName);
            }

            return _pathProvider.MediaMtxExePath;
        }

        /// <summary>
        /// 지정한 실행 파일 경로와 동일한 경로에서 실행 중인 프로세스를 찾아 종료한다.
        /// 
        /// 주의:
        /// 같은 이름의 프로세스라도 실행 경로가 다르면 종료하지 않는다.
        /// 예를 들어 다른 프로그램이 사용하는 ffmpeg.exe는 종료하지 않는다.
        /// </summary>
        /// <param name="expectedExePath">
        /// 종료 대상 실행 파일의 전체 경로.
        /// </param>
        private void KillProcessByExactPath(string expectedExePath)
        {
            if (string.IsNullOrWhiteSpace(expectedExePath))
                return;

            string fileName = Path.GetFileNameWithoutExtension(expectedExePath);

            if (string.IsNullOrWhiteSpace(fileName))
                return;

            Process[] processes = Process.GetProcessesByName(fileName);

            foreach (Process process in processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    string processPath = GetProcessPathSafe(process);

                    if (string.IsNullOrWhiteSpace(processPath))
                        continue;

                    if (!IsSamePath(processPath, expectedExePath))
                        continue;

                    _logService.WriteApp(
                        "잔류 프로세스 종료 시도: " +
                        process.ProcessName +
                        ", PID=" +
                        process.Id +
                        ", Path=" +
                        processPath);

                    KillProcess(process);
                }
                catch (Exception ex)
                {
                    _logService.WriteException("잔류 프로세스 정리 오류", ex);
                }
                finally
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// 프로세스의 실행 파일 전체 경로를 안전하게 조회한다.
        /// 
        /// 일부 프로세스는 권한 문제 또는 32/64bit 접근 문제로 MainModule 접근 시 예외가 발생할 수 있다.
        /// 이 경우 빈 문자열을 반환하고 해당 프로세스는 종료 대상에서 제외한다.
        /// </summary>
        /// <param name="process">
        /// 경로를 확인할 프로세스.
        /// </param>
        /// <returns>
        /// 프로세스 실행 파일 전체 경로.
        /// 조회 실패 시 빈 문자열.
        /// </returns>
        private string GetProcessPathSafe(Process process)
        {
            try
            {
                if (process == null || process.HasExited)
                    return "";

                return process.MainModule.FileName;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 두 파일 경로가 같은지 비교한다.
        /// 
        /// Windows 경로 비교이므로 대소문자를 구분하지 않는다.
        /// 가능한 경우 전체 경로로 변환한 뒤 비교한다.
        /// </summary>
        /// <param name="path1">
        /// 비교할 첫 번째 경로.
        /// </param>
        /// <param name="path2">
        /// 비교할 두 번째 경로.
        /// </param>
        /// <returns>
        /// true: 같은 경로
        /// false: 다른 경로
        /// </returns>
        private bool IsSamePath(string path1, string path2)
        {
            try
            {
                string fullPath1 = Path.GetFullPath(path1).TrimEnd('\\');
                string fullPath2 = Path.GetFullPath(path2).TrimEnd('\\');

                return string.Equals(
                    fullPath1,
                    fullPath2,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(
                    path1,
                    path2,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// 지정한 프로세스를 종료한다.
        /// 
        /// 먼저 일반 종료를 시도할 수 있는 콘솔 프로세스가 아니므로,
        /// 현재 단계에서는 Kill을 사용하여 종료한다.
        /// 종료 후 최대 3초까지 대기한다.
        /// </summary>
        /// <param name="process">
        /// 종료할 프로세스.
        /// </param>
        private void KillProcess(Process process)
        {
            if (process == null)
                return;

            if (process.HasExited)
                return;

            try
            {
                process.Kill();
                process.WaitForExit(3000);

                _logService.WriteApp(
                    "잔류 프로세스 종료 완료: " +
                    process.ProcessName +
                    ", PID=" +
                    process.Id);
            }
            catch (Exception ex)
            {
                _logService.WriteException("잔류 프로세스 Kill 실패", ex);
            }
        }
    }
}