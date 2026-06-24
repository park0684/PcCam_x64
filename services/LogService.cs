using System;
using System.IO;
using System.Text;
using PcCam_x64.Infrastructure;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 파일 로그 기록 서비스.
    /// 
    /// 로그는 logs 폴더 아래에 일자별 파일로 기록한다.
    /// 예:
    /// logs\app_20260528.log
    /// logs\stream_20260528.log
    /// logs\ffmpeg_20260528.log
    /// logs\mediamtx_20260528.log
    /// logs\auth_20260528.log
    /// logs\error_20260528.log
    /// </summary>
    public class LogService
    {
        private readonly object _syncLock = new object();
        private readonly PathProvider _pathProvider;

        public LogService(PathProvider pathProvider)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            _pathProvider = pathProvider;
        }

        /// <summary>
        /// 일반 프로그램 로그를 기록한다.
        /// </summary>
        /// <param name="message">기록할 메시지.</param>
        public void WriteApp(string message)
        {
            Write(LogCategory.App, message);
        }

        /// <summary>
        /// 송출 상태 관련 로그를 기록한다.
        /// </summary>
        /// <param name="message">기록할 메시지.</param>
        public void WriteStream(string message)
        {
            Write(LogCategory.Stream, message);
        }

        /// <summary>
        /// FFmpeg 관련 로그를 기록한다.
        /// </summary>
        /// <param name="message">기록할 메시지.</param>
        public void WriteFfmpeg(string message)
        {
            Write(LogCategory.Ffmpeg, message);
        }

        /// <summary>
        /// MediaMTX 관련 로그를 기록한다.
        /// </summary>
        /// <param name="message">기록할 메시지.</param>
        public void WriteMediaMtx(string message)
        {
            Write(LogCategory.MediaMtx, message);
        }

        /// <summary>
        /// 인증 관련 로그를 기록한다.
        /// </summary>
        /// <param name="message">기록할 메시지.</param>
        public void WriteAuth(string message)
        {
            Write(LogCategory.Auth, message);
        }

        /// <summary>
        /// 오류 로그를 기록한다.
        /// </summary>
        /// <param name="message">기록할 메시지.</param>
        public void WriteError(string message)
        {
            Write(LogCategory.Error, message);
        }

        /// <summary>
        /// 예외 정보를 오류 로그에 기록한다.
        /// </summary>
        /// <param name="title">오류 제목 또는 발생 위치.</param>
        /// <param name="ex">기록할 예외 객체.</param>
        public void WriteException(string title, Exception ex)
        {
            string message =
                title + Environment.NewLine +
                (ex == null ? "" : ex.ToString());

            Write(LogCategory.Error, message);
        }

        /// <summary>
        /// 로그 분류에 따라 로그 파일에 메시지를 기록한다.
        /// </summary>
        /// <param name="category">로그 분류.</param>
        /// <param name="message">기록할 메시지.</param>
        public void Write(LogCategory category, string message)
        {
            lock (_syncLock)
            {
                _pathProvider.EnsureDirectories();

                string filePath = GetLogFilePath(category);

                string line =
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                    " | " +
                    (message ?? "");

                File.AppendAllText(
                    filePath,
                    line + Environment.NewLine,
                    Encoding.UTF8);
            }
        }

        /// <summary>
        /// Supervisor에서 전달된 통합 로그를 내용에 따라 적절한 로그 파일로 분배한다.
        /// </summary>
        /// <param name="line">Supervisor에서 수신한 로그 한 줄.</param>
        public void WriteSupervisorLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            if (line.StartsWith("[MediaMTX]", StringComparison.OrdinalIgnoreCase))
            {
                WriteMediaMtx(line);
                return;
            }

            if (line.StartsWith("[FFmpeg]", StringComparison.OrdinalIgnoreCase))
            {
                WriteFfmpeg(line);
                return;
            }

            if (line.IndexOf("인증", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                WriteAuth(line);
                return;
            }

            if (line.IndexOf("실패", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("오류", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                WriteError(line);
                return;
            }

            WriteStream(line);
        }

        /// <summary>
        /// 로그 분류에 해당하는 일자별 로그 파일 경로를 반환한다.
        /// </summary>
        /// <param name="category">로그 분류.</param>
        /// <returns>로그 파일 전체 경로.</returns>
        private string GetLogFilePath(LogCategory category)
        {
            string prefix = GetLogFilePrefix(category);
            string fileName = prefix + "_" + DateTime.Now.ToString("yyyyMMdd") + ".log";

            return Path.Combine(_pathProvider.LogDirectory, fileName);
        }

        /// <summary>
        /// 로그 분류에 따른 파일명 접두사를 반환한다.
        /// </summary>
        /// <param name="category">로그 분류.</param>
        /// <returns>파일명 접두사.</returns>
        private string GetLogFilePrefix(LogCategory category)
        {
            switch (category)
            {
                case LogCategory.App:
                    return "app";

                case LogCategory.Stream:
                    return "stream";

                case LogCategory.Ffmpeg:
                    return "ffmpeg";

                case LogCategory.MediaMtx:
                    return "mediamtx";

                case LogCategory.Auth:
                    return "auth";

                case LogCategory.Error:
                    return "error";

                default:
                    return "app";
            }
        }
    }
}