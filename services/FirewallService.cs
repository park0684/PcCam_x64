using System;
using System.Diagnostics;
using System.Text;
using PcCam_x64.Models;
using System.Security.Principal;

namespace PcCam_x64.Services
{
    /// <summary>
    /// Windows 방화벽 규칙 등록 서비스.
    /// 
    /// PC CAM은 NVR 또는 다른 PC에서 RTSP로 접근해야 하므로
    /// RTSP 포트가 Windows 방화벽에서 허용되어야 한다.
    /// 
    /// 1단계에서는 netsh advfirewall 명령을 사용한다.
    /// Windows 7에서도 사용할 수 있는 방식이다.
    /// 
    /// 주의:
    /// 방화벽 규칙 등록은 관리자 권한이 필요할 수 있다.
    /// 권한이 부족하면 예외를 발생시키고, 호출 측에서 로그 또는 안내 메시지를 처리한다.
    /// </summary>
    public class FirewallService
    {
        private readonly LogService _logService;

        public FirewallService(LogService logService)
        {
            if (logService == null)
                throw new ArgumentNullException("logService");

            _logService = logService;
        }

        /// <summary>
        /// PC CAM 실행에 필요한 방화벽 규칙을 적용한다.
        /// 
        /// 적용 대상:
        /// - RTSP 포트
        /// - 스트림별 ONVIF 포트
        /// 
        /// 관리자 권한이 없는 경우 방화벽 등록을 생략하고 로그만 남긴다.
        /// 방화벽 등록 실패가 PC CAM 실행 자체를 막으면 안 되기 때문이다.
        /// </summary>
        /// <param name="config">
        /// 현재 PC CAM 설정.
        /// </param>
        public void Apply(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            if (config.RtspServer == null)
                throw new InvalidOperationException("RTSP 서버 설정이 없습니다.");

            if (!IsAdministrator())
            {
                _logService.WriteApp(
                    "방화벽 규칙 등록 생략: 관리자 권한이 아닙니다. " +
                    "외부 NVR 또는 다른 PC에서 접속이 안 될 경우 관리자 권한으로 실행 후 다시 설정을 저장해야 합니다.");

                return;
            }

            AddTcpInboundRule(
                "PC CAM RTSP",
                config.RtspServer.RtspPort);
            AddUdpInboundRule(
                "PC CAM ONVIF Discovery",
                3702);

            if (config.Streams != null)
            {
                foreach (StreamConfig stream in config.Streams)
                {
                    if (stream == null)
                        continue;

                    if (stream.OnvifPort <= 0)
                        continue;

                    AddTcpInboundRule(
                        "PC CAM ONVIF " + stream.StreamNo,
                        stream.OnvifPort);
                }
            }
        }

        /// <summary>
        /// 지정한 TCP 포트에 대한 인바운드 허용 규칙을 추가한다.
        /// 
        /// 같은 이름의 규칙이 이미 있을 수 있으므로,
        /// 먼저 동일 이름 규칙을 삭제한 뒤 다시 등록한다.
        /// </summary>
        /// <param name="ruleName">
        /// Windows 방화벽 규칙 이름.
        /// </param>
        /// <param name="port">
        /// 허용할 TCP 포트 번호.
        /// </param>
        public void AddTcpInboundRule(string ruleName, int port)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException("방화벽 규칙명이 비어 있습니다.", "ruleName");

            if (port <= 0 || port > 65535)
                throw new InvalidOperationException("방화벽 포트 값이 올바르지 않습니다. Port=" + port);

            DeleteRule(ruleName);

            string arguments =
                "advfirewall firewall add rule " +
                "name=\"" + ruleName + "\" " +
                "dir=in " +
                "action=allow " +
                "protocol=TCP " +
                "localport=" + port;

            ExecuteNetsh(arguments);

            _logService.WriteApp(
                "방화벽 규칙 등록 완료. Rule=" +
                ruleName +
                ", Port=" +
                port);
        }

        /// <summary>
        /// 지정한 이름의 Windows 방화벽 규칙을 삭제한다.
        /// 
        /// 규칙이 없는 경우에도 netsh 결과가 실패로 나올 수 있으므로,
        /// 이 메서드는 삭제 실패를 치명 오류로 보지 않는다.
        /// </summary>
        /// <param name="ruleName">
        /// 삭제할 방화벽 규칙 이름.
        /// </param>
        public void DeleteRule(string ruleName)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                return;

            string arguments =
                "advfirewall firewall delete rule " +
                "name=\"" + ruleName + "\"";

            try
            {
                ExecuteNetsh(arguments);
            }
            catch (Exception ex)
            {
                _logService.WriteApp(
                    "방화벽 규칙 삭제 생략 또는 실패. Rule=" +
                    ruleName +
                    ", Message=" +
                    ex.Message);
            }
        }

        /// <summary>
        /// netsh 명령을 실행한다.
        /// 
        /// netsh는 Windows 7에서도 사용할 수 있으며,
        /// 방화벽 규칙 등록/삭제에 사용한다.
        /// </summary>
        /// <param name="arguments">
        /// netsh.exe에 전달할 인자.
        /// </param>
        private void ExecuteNetsh(string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "netsh";
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.Default;
            startInfo.StandardErrorEncoding = Encoding.Default;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;

                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "방화벽 명령 실행 실패\r\n" +
                        "Arguments: " + arguments + "\r\n" +
                        "Output: " + output + "\r\n" +
                        "Error: " + error);
                }

                if (!string.IsNullOrWhiteSpace(output))
                    _logService.WriteApp("netsh output: " + output.Trim());

                if (!string.IsNullOrWhiteSpace(error))
                    _logService.WriteApp("netsh error: " + error.Trim());
            }
        }

        /// <summary>
        /// 현재 프로그램이 관리자 권한으로 실행 중인지 확인한다.
        /// 
        /// Windows 방화벽 규칙 등록은 관리자 권한이 필요할 수 있다.
        /// 관리자 권한이 없는 경우 방화벽 등록은 건너뛰고,
        /// 프로그램 실행과 송출 기능은 계속 유지하는 방향으로 처리한다.
        /// </summary>
        /// <returns>
        /// true: 관리자 권한으로 실행 중
        /// false: 일반 사용자 권한으로 실행 중
        /// </returns>
        public bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);

                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 지정한 UDP 포트에 대한 인바운드 허용 규칙을 추가한다.
        /// 
        /// WS-Discovery는 UDP 3702 포트를 사용하므로,
        /// NVR에서 ONVIF 장치 검색을 하기 위해 이 규칙이 필요하다.
        /// </summary>
        /// <param name="ruleName">Windows 방화벽 규칙 이름.</param>
        /// <param name="port">허용할 UDP 포트 번호.</param>
        public void AddUdpInboundRule(string ruleName, int port)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException("방화벽 규칙명이 비어 있습니다.", "ruleName");

            if (port <= 0 || port > 65535)
                throw new InvalidOperationException("방화벽 포트 값이 올바르지 않습니다. Port=" + port);

            DeleteRule(ruleName);

            string arguments =
                "advfirewall firewall add rule " +
                "name=\"" + ruleName + "\" " +
                "dir=in " +
                "action=allow " +
                "protocol=UDP " +
                "localport=" + port;

            ExecuteNetsh(arguments);

            _logService.WriteApp(
                "방화벽 UDP 규칙 등록 완료. Rule=" +
                ruleName +
                ", Port=" +
                port);
        }

        /// <summary>
        /// PC CAM 실행에 필요한 프로그램 기준 방화벽 규칙을 적용한다.
        /// 2026-06-02
        /// 포트 번호가 아니라 실행 파일 경로 기준으로 인바운드 허용 규칙을 등록한다.
        /// 설정 저장 때마다 포트별 규칙을 삭제/재등록하지 않기 위한 방식이다.
        /// </summary>
        public void ApplyProgramRules(
            string pccamExePath,
            string mediamtxExePath)
        {
            if (!IsAdministrator())
            {
                _logService.WriteApp(
                    "방화벽 프로그램 규칙 등록 생략: 관리자 권한이 아닙니다.");
                return;
            }

            AddProgramInboundRule("PC CAM App", pccamExePath);
            AddProgramInboundRule("PC CAM MediaMTX", mediamtxExePath);
        }

        /// <summary>
        /// 지정한 프로그램 경로에 대한 인바운드 허용 규칙을 등록한다.
        /// </summary>
        private void AddProgramInboundRule(
            string ruleName,
            string programPath)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException("방화벽 규칙명이 비어 있습니다.", "ruleName");

            if (string.IsNullOrWhiteSpace(programPath))
                throw new ArgumentException("프로그램 경로가 비어 있습니다.", "programPath");

            DeleteRule(ruleName);

            string arguments =
                "advfirewall firewall add rule " +
                "name=\"" + ruleName + "\" " +
                "dir=in " +
                "action=allow " +
                "program=\"" + programPath + "\" " +
                "enable=yes";

            ExecuteNetsh(arguments);

            _logService.WriteApp(
                "방화벽 프로그램 규칙 등록 완료. Rule=" +
                ruleName +
                ", Program=" +
                programPath);
        }
    }
}