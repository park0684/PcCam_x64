using System;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// 런타임 스트림 설정 보정 서비스.
    /// 
    /// 설정 파일에는 MainStream 해상도가 0으로 저장될 수 있다.
    /// 0은 "실제 모니터 원본 해상도 사용"이라는 의미로 보고,
    /// 프로그램 실행 시점에 선택된 MonitorInfo의 실제 해상도를 반영한다.
    /// </summary>
    public class StreamRuntimeConfigApplier
    {
        /// <summary>
        /// 특정 StreamConfig에 실제 모니터 해상도를 반영한다.
        /// </summary>
        /// <param name="streamConfig">스트림 설정.</param>
        /// <param name="monitorInfo">해당 스트림이 캡처할 모니터 정보.</param>
        public void Apply(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (monitorInfo.BoundsWidth <= 0 || monitorInfo.BoundsHeight <= 0)
                throw new InvalidOperationException("모니터 해상도 정보가 올바르지 않습니다.");

            EnsureQualityConfigs(streamConfig);

            ApplyMainResolution(
                streamConfig,
                monitorInfo);

            ApplySubResolutionIfEmpty(
                streamConfig,
                monitorInfo);
        }

        /// <summary>
        /// Main/Sub 품질 설정 객체를 보장한다.
        /// </summary>
        private void EnsureQualityConfigs(StreamConfig streamConfig)
        {
            if (streamConfig.MainStream == null)
                streamConfig.MainStream = StreamQualityConfig.CreateMain(streamConfig.RtspPath);

            if (streamConfig.SubStream == null)
                streamConfig.SubStream = StreamQualityConfig.CreateSub(streamConfig.RtspPath + "_sub");
        }

        /// <summary>
        /// MainStream의 해상도가 비어 있으면 실제 모니터 해상도를 적용한다.
        /// </summary>
        private void ApplyMainResolution(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo)
        {
            if (streamConfig.MainStream.Width <= 0)
                streamConfig.MainStream.Width = monitorInfo.BoundsWidth;

            if (streamConfig.MainStream.Height <= 0)
                streamConfig.MainStream.Height = monitorInfo.BoundsHeight;
        }

        /// <summary>
        /// SubStream 해상도가 비어 있는 경우에만 실제 모니터 해상도를 적용한다.
        /// 일반적으로 SubStream은 640x360 같은 축소 해상도를 유지한다.
        /// </summary>
        private void ApplySubResolutionIfEmpty(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo)
        {
            if (streamConfig.SubStream.Width <= 0)
                streamConfig.SubStream.Width = monitorInfo.BoundsWidth;

            if (streamConfig.SubStream.Height <= 0)
                streamConfig.SubStream.Height = monitorInfo.BoundsHeight;
        }
    }
}