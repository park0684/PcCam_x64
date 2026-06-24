using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// 현재 Windows에 연결된 모니터 정보를 조회하는 서비스.
    /// 
    /// 설정 파일에는 Primary / Secondary 역할만 저장하고,
    /// 실제 좌표와 해상도는 이 서비스에서 실행 시점에 조회한다.
    /// </summary>
    public class MonitorService
    {
        /// <summary>
        /// 현재 연결된 모든 모니터 정보를 조회한다.
        /// </summary>
        public List<MonitorInfo> GetMonitors()
        {
            List<MonitorInfo> result = new List<MonitorInfo>();

            Screen[] screens = Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                Screen screen = screens[i];

                result.Add(new MonitorInfo
                {
                    Index = i,
                    DeviceName = screen.DeviceName,
                    IsPrimary = screen.Primary,
                    BoundsX = screen.Bounds.X,
                    BoundsY = screen.Bounds.Y,
                    BoundsWidth = screen.Bounds.Width,
                    BoundsHeight = screen.Bounds.Height
                });
            }

            return result;
        }

        /// <summary>
        /// StreamConfig의 MonitorRole 값에 해당하는 실제 모니터를 찾는다.
        /// 
        /// MonitorRole:
        /// - Primary   : 주 모니터
        /// - Secondary : 보조 모니터
        /// 
        /// 보조 모니터가 없는데 Secondary가 요청되면 null을 반환한다.
        /// </summary>
        public MonitorInfo GetMonitorByRole(string monitorRole)
        {
            List<MonitorInfo> monitors = GetMonitors();

            if (string.Equals(monitorRole, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                return monitors.FirstOrDefault(x => x.IsPrimary);
            }

            if (string.Equals(monitorRole, "Secondary", StringComparison.OrdinalIgnoreCase))
            {
                return monitors.FirstOrDefault(x => !x.IsPrimary);
            }

            return null;
        }

        /// <summary>
        /// 특정 스트림 설정에 해당하는 실제 모니터를 찾는다.
        /// </summary>
        public MonitorInfo GetMonitorForStream(StreamConfig streamConfig)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            return GetMonitorByRole(streamConfig.MonitorRole);
        }

        /// <summary>
        /// 설정 화면에서 표시할 모니터 설명 문자열을 만든다.
        /// 
        /// 예:
        /// 주 모니터 - \\.\DISPLAY1 - 1920x1080
        /// 보조 모니터 - \\.\DISPLAY2 - 1920x1080
        /// </summary>
        public string GetMonitorDisplayText(string monitorRole)
        {
            MonitorInfo monitor = GetMonitorByRole(monitorRole);

            if (monitor == null)
            {
                if (string.Equals(monitorRole, "Secondary", StringComparison.OrdinalIgnoreCase))
                    return "보조 모니터 없음";

                return "모니터 정보 없음";
            }

            return monitor.DisplayText;
        }

        /// <summary>
        /// 현재 PC에 보조 모니터가 있는지 확인한다.
        /// </summary>
        public bool HasSecondaryMonitor()
        {
            List<MonitorInfo> monitors = GetMonitors();

            return monitors.Any(x => !x.IsPrimary);
        }
    }
}