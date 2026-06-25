using System;
using PcCam_x64.Models;

namespace PcCam_x64.Services
{
    /// <summary>
    /// FFmpeg 실행 인자 생성 서비스.
    /// 
    /// 현재 구조:
    /// - 하나의 모니터 화면을 캡처한다.
    /// - MainStream은 녹화용 고화질 RTSP로 송출한다.
    /// - SubStream은 실시간 보기용 저화질 RTSP로 송출한다.
    /// 
    /// 예:
    /// Main → rtsp://127.0.0.1:8554/poscam
    /// Sub  → rtsp://127.0.0.1:8554/poscam_sub
    /// </summary>
    public class FfmpegCommandBuilder
    {
        private const string TouchOverlayPocEnvironmentVariable = "PCCAM_TOUCH_OVERLAY_POC";
        private const int TouchOverlayPocMarkerSize = 320;
        private const int TouchOverlayPocBorderThickness = 6;

        /// <summary>
        /// FFmpeg 실행 Arguments를 생성한다.
        /// 
        /// 실제 실행 파일 경로(ffmpeg.exe)는 여기서 만들지 않고,
        /// ProcessStartInfo.FileName에서 별도로 지정한다.
        /// 
        /// 변경된 구조:
        /// - FFmpeg는 MediaMTX로 RTMP/FLV publish 한다.
        /// - NVR/VLC는 기존처럼 MediaMTX의 RTSP 주소를 읽는다.
        /// 
        /// 예:
        /// FFmpeg publish: rtmp://127.0.0.1:1935/poscam
        /// NVR read:       rtsp://PC_IP:554/poscam
        /// </summary>
        public string BuildArguments(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo,
            RtspServerConfig rtspServerConfig)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (rtspServerConfig == null)
                throw new ArgumentNullException("rtspServerConfig");

            ValidateMonitorInfo(monitorInfo);
            ValidateRtspServerConfig(rtspServerConfig);

            /*
             * Main/Sub 설정 객체를 streamConfig에 확정한다.
             * ONVIF 응답과 FFmpeg 실행 인자가 같은 설정 객체를 보도록 하기 위함이다.
             */
            EnsureStreamQualityConfigs(streamConfig);

            /*
             * MainStream 해상도가 0이면 실제 캡처 대상 모니터 해상도를 반영한다.
             */
            ApplyActualMonitorResolution(
                streamConfig,
                monitorInfo);

            StreamQualityConfig mainStream = streamConfig.MainStream;
            StreamQualityConfig subStream = streamConfig.SubStream;

            bool useMain =
                streamConfig.IsEnabled &&
                mainStream != null &&
                mainStream.IsEnabled;

            bool useSub =
                streamConfig.IsEnabled &&
                subStream != null &&
                subStream.IsEnabled;

            if (!useMain && !useSub)
                throw new InvalidOperationException("사용 가능한 Main/Sub 스트림 설정이 없습니다.");

            if (useMain)
                ValidateStreamQuality(mainStream, "MainStream");

            if (useSub)
                ValidateStreamQuality(subStream, "SubStream");

            /*
             * 캡처 FPS는 활성화된 출력 중 가장 높은 FPS를 사용한다.
             * Main 10fps, Sub 5fps라면 캡처는 10fps로 한다.
             */
            int captureFps = GetCaptureFps(
                mainStream,
                subStream,
                useMain,
                useSub);

            bool useTouchOverlayPoc =
                IsTouchOverlayPocEnabled(streamConfig);

            string inputOptions =
                "-f gdigrab " +
                "-draw_mouse 1 " +
                "-framerate " + captureFps + " " +
                "-offset_x " + monitorInfo.BoundsX + " " +
                "-offset_y " + monitorInfo.BoundsY + " " +
                "-video_size " + monitorInfo.BoundsWidth + "x" + monitorInfo.BoundsHeight + " " +
                "-i desktop ";

            if (useTouchOverlayPoc)
            {
                inputOptions += BuildTouchOverlayPocInputOptions(
                    monitorInfo,
                    captureFps);
            }

            /*
             * Main/Sub 둘 다 사용하는 경우.
             * Main은 입력 FPS 그대로 통과시키고,
             * Sub만 fps/scale 필터를 적용한다.
             */
            if (useMain && useSub)
            {
                ValidateDifferentRtspPaths(
                    mainStream,
                    subStream);

                string mainUrl = BuildLocalRtmpPublishUrl(
                     mainStream.RtspPath,
                     rtspServerConfig);

                string subUrl = BuildLocalRtmpPublishUrl(
                    subStream.RtspPath,
                    rtspServerConfig);

                string filterComplex;

                if (useTouchOverlayPoc)
                {
                    filterComplex = BuildMainSubTouchOverlayPocFilterComplex(
                        subStream,
                        monitorInfo);
                }
                else
                {
                    filterComplex = BuildMainSubFilterComplex(
                        mainStream,
                        subStream,
                        monitorInfo);
                }

                string arguments =
                    inputOptions +
                    "-filter_complex \"" + filterComplex + "\" " +

                    "-map \"[mainout]\" " +
                    BuildCodecOptions(streamConfig, mainStream) + " " +
                    BuildRtmpOutputOptions() +
                    mainUrl + " " +

                    "-map \"[subout]\" " +
                    BuildCodecOptions(streamConfig, subStream) + " " +
                    BuildRtmpOutputOptions() +
                    subUrl;

                return arguments;
            }

            /*
             * Main만 사용하는 경우.
             * Main은 입력 gdigrab -framerate 값을 그대로 사용하므로
             * 별도 -vf fps 필터를 적용하지 않는다.
             */
            if (useMain)
            {
                string mainUrl = BuildLocalRtmpPublishUrl(
                    mainStream.RtspPath,
                    rtspServerConfig);

                if (useTouchOverlayPoc)
                {
                    string filterComplex =
                        BuildMainTouchOverlayPocFilterComplex();

                    string overlayArguments =
                        inputOptions +
                        "-filter_complex \"" + filterComplex + "\" " +
                        "-map \"[mainout]\" " +
                        BuildCodecOptions(streamConfig, mainStream) + " " +
                        BuildRtmpOutputOptions() +
                        mainUrl;

                    return overlayArguments;
                }

                string arguments =
                    inputOptions +
                    BuildCodecOptions(streamConfig, mainStream) + " " +
                    BuildRtmpOutputOptions() +
                    mainUrl;

                return arguments;
            }

            /*
             * Sub만 사용하는 경우.
             * Sub는 저해상도/저프레임 변환이 필요하므로 -vf를 적용한다.
             */
            if (useSub)
            {
                string subUrl = BuildLocalRtmpPublishUrl(
                    subStream.RtspPath,
                    rtspServerConfig);

                if (useTouchOverlayPoc)
                {
                    string filterComplex =
                        BuildSubTouchOverlayPocFilterComplex(
                            subStream,
                            monitorInfo);

                    string overlayArguments =
                        inputOptions +
                        "-filter_complex \"" + filterComplex + "\" " +
                        "-map \"[subout]\" " +
                        BuildCodecOptions(streamConfig, subStream) + " " +
                        BuildRtmpOutputOptions() +
                        subUrl;

                    return overlayArguments;
                }

                string videoFilter = BuildVideoFilterOption(
                    subStream,
                    monitorInfo);

                string arguments =
                    inputOptions +
                    videoFilter +
                    BuildCodecOptions(streamConfig, subStream) + " " +
                    BuildRtmpOutputOptions() +
                    subUrl;

                return arguments;
            }

            throw new InvalidOperationException("FFmpeg Arguments를 생성할 수 없습니다.");
        }

        /// <summary>
        /// 정적 터치 오버레이 POC 사용 여부를 확인한다.
        ///
        /// 안전 조건:
        /// - 환경변수 PCCAM_TOUCH_OVERLAY_POC 값이 정확히 1이어야 한다.
        /// - 1차 POC 대상인 Stream0에서만 활성화한다.
        ///
        /// 이 조건을 만족하지 않으면 기존 FFmpeg 명령을 그대로 사용한다.
        /// </summary>
        private bool IsTouchOverlayPocEnabled(
            StreamConfig streamConfig)
        {
            if (streamConfig == null)
                return false;

            if (streamConfig.StreamNo != 0)
                return false;

            string value = Environment.GetEnvironmentVariable(
                TouchOverlayPocEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(value))
                return false;

            return string.Equals(
                value.Trim(),
                "1",
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 정적 POC용 투명 RGBA 오버레이 입력을 생성한다.
        ///
        /// 전체 화면 크기의 투명 프레임을 lavfi로 만들고,
        /// 화면 중앙에 노란색 사각형 테두리를 표시한다.
        /// 실제 POS 화면에는 그리지 않고 FFmpeg 내부에서만 사용한다.
        /// </summary>
        private string BuildTouchOverlayPocInputOptions(
            MonitorInfo monitorInfo,
            int captureFps)
        {
            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            int markerSize = Math.Min(
                TouchOverlayPocMarkerSize,
                Math.Min(
                    monitorInfo.BoundsWidth,
                    monitorInfo.BoundsHeight));

            int markerX =
                Math.Max(
                    0,
                    (monitorInfo.BoundsWidth - markerSize) / 2);

            int markerY =
                Math.Max(
                    0,
                    (monitorInfo.BoundsHeight - markerSize) / 2);

            string overlayFilter =
                "color=c=black@0.0" +
                ":s=" + monitorInfo.BoundsWidth + "x" + monitorInfo.BoundsHeight +
                ":r=" + captureFps +
                ",format=rgba" +
                ",drawbox=" +
                "x=" + markerX +
                ":y=" + markerY +
                ":w=" + markerSize +
                ":h=" + markerSize +
                ":color=magenta@1.0" +
                ":t=fill" +
                ":replace=1";

            return "-f lavfi " +
                   "-i \"" + overlayFilter + "\" ";
        }

        /// <summary>
        /// Main/Sub 동시 출력용 정적 오버레이 필터를 생성한다.
        ///
        /// 원본 화면과 오버레이를 먼저 한 번 합성한 다음,
        /// 합성 결과를 Main/Sub로 분기한다.
        /// </summary>
        private string BuildMainSubTouchOverlayPocFilterComplex(
            StreamQualityConfig subStream,
            MonitorInfo monitorInfo)
        {
            string subFilter = BuildSubVideoFilter(
                subStream,
                monitorInfo);

            return "[0:v]setpts=PTS-STARTPTS[base];" +
                   "[1:v]format=rgba,setpts=PTS-STARTPTS[pointer];" +
                   "[base][pointer]overlay=0:0:eof_action=pass:repeatlast=0[composed];" +
                   "[composed]split=2[mainraw][subraw];" +
                   "[mainraw]null[mainout];" +
                   "[subraw]" + subFilter + "[subout]";
        }

        /// <summary>
        /// Main 단독 출력용 정적 오버레이 필터를 생성한다.
        /// </summary>
        private string BuildMainTouchOverlayPocFilterComplex()
        {
            return "[0:v]setpts=PTS-STARTPTS[base];" +
                   "[1:v]format=rgba,setpts=PTS-STARTPTS[pointer];" +
                   "[base][pointer]overlay=0:0:eof_action=pass:repeatlast=0[mainout]";
        }

        /// <summary>
        /// Sub 단독 출력용 정적 오버레이 필터를 생성한다.
        /// </summary>
        private string BuildSubTouchOverlayPocFilterComplex(
            StreamQualityConfig subStream,
            MonitorInfo monitorInfo)
        {
            string subFilter = BuildSubVideoFilter(
                subStream,
                monitorInfo);

            return "[0:v]setpts=PTS-STARTPTS[base];" +
                   "[1:v]format=rgba,setpts=PTS-STARTPTS[pointer];" +
                   "[base][pointer]overlay=0:0:eof_action=pass:repeatlast=0[composed];" +
                   "[composed]" + subFilter + "[subout]";
        }

        /// <summary>
        /// MainStream과 SubStream의 RTSP 경로가 서로 다른지 확인한다.
        /// 
        /// 같은 RTSP 경로로 두 개의 출력을 publish하면
        /// MediaMTX에서 두 번째 publish 요청을 거부할 수 있다.
        /// </summary>
        private void ValidateDifferentRtspPaths(
            StreamQualityConfig mainStream,
            StreamQualityConfig subStream)
        {
            string mainPath = NormalizeRtspPath(mainStream == null ? "" : mainStream.RtspPath);
            string subPath = NormalizeRtspPath(subStream == null ? "" : subStream.RtspPath);

            if (string.Equals(mainPath, subPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "MainStream과 SubStream의 RTSP 경로가 같습니다. " +
                    "Main/Sub는 서로 다른 RtspPath를 사용해야 합니다. " +
                    "예: Main=poscam, Sub=poscam_sub");
            }
        }

        /// <summary>
        /// 활성화된 출력 중 가장 높은 FPS를 캡처 FPS로 사용한다.
        /// </summary>
        private int GetCaptureFps(
            StreamQualityConfig mainStream,
            StreamQualityConfig subStream,
            bool useMain,
            bool useSub)
        {
            int fps = 1;

            if (useMain && mainStream != null && mainStream.Fps > fps)
                fps = mainStream.Fps;

            if (useSub && subStream != null && subStream.Fps > fps)
                fps = subStream.Fps;

            return fps;
        }

        /// <summary>
        /// 코덱별 FFmpeg 옵션을 생성한다.
        /// 
        /// 파이썬 버전과 유사하게 단순 구성한다.
        /// 시간정보를 강제로 재작성하지 않고, 입력 프레임 흐름과 기본 인코더 흐름을 따른다.
        /// 
        /// 주의:
        /// 과거 오류였던 "-b:v -g 10" 형태가 되지 않도록
        /// -b:v 뒤에는 반드시 bitrate 값을 배치한다.
        /// </summary>
        /// <param name="streamConfig">상위 스트림 설정.</param>
        /// <param name="quality">Main/Sub 품질 설정.</param>
        /// <returns>FFmpeg 코덱 옵션 문자열.</returns>
        private string BuildCodecOptions(
            StreamConfig streamConfig,
            StreamQualityConfig quality)
        {
            string codec = streamConfig.Codec ?? "H264";

            int fps = GetSafeFps(quality);

            /*
             * 파이썬 버전 기준에 맞춰 GOP는 FPS의 2배로 설정한다.
             * 예:
             * 10fps → GOP 20
             * 5fps  → GOP 10
             */
            int gop = Math.Max(1, fps * 2);

            string bitrate = NormalizeBitrate(
                quality == null ? "" : quality.Bitrate,
                "1200k");

            if (string.Equals(codec, "H265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "H.265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "HEVC", StringComparison.OrdinalIgnoreCase))
            {
                return "-vcodec libx265 " +
                       "-preset ultrafast " +
                       "-pix_fmt yuv420p " +
                       "-b:v " + bitrate + " " +
                       "-g " + gop + " " +
                       "-keyint_min " + gop + " " +
                       "-sc_threshold 0";
            }

            //return "-vcodec libx264 " +
            //       "-profile:v baseline " +
            //       "-preset ultrafast " +
            //       "-tune zerolatency " +
            //       "-pix_fmt yuv420p " +
            //       "-bf 0 " +
            //       "-b:v " + bitrate + " " +
            //       "-g " + gop + " " +
            //       "-keyint_min " + gop + " " +
            //       "-sc_threshold 0";

            return "-vcodec libx264 " +
                   "-profile:v baseline " +
                   "-preset ultrafast " +
                   "-threads 1 " +
                   "-pix_fmt yuv420p " +
                   "-bf 0 " +
                   "-b:v " + bitrate + " " +
                   "-g " + gop + " " +
                   "-keyint_min " + gop + " " +
                   "-sc_threshold 0 " +
                   "-x264-params \"sliced-threads=0:slices=1:keyint=" + gop +
                        ":min-keyint=" + gop +
                        ":scenecut=0:repeat-headers=1\"";
        }



        /// <summary>
        /// 비트레이트 문자열을 FFmpeg에서 사용할 수 있는 형식으로 보정한다.
        /// 
        /// 예:
        /// - 1200k → 1200k
        /// - 2m → 2m
        /// - 1200 → 1200k
        /// </summary>
        /// <param name="bitrate">사용자 설정 비트레이트.</param>
        /// <param name="fallback">비어 있거나 잘못된 경우 사용할 기본값.</param>
        /// <returns>FFmpeg 비트레이트 문자열.</returns>
        private string NormalizeBitrate(
            string bitrate,
            string fallback)
        {
            if (string.IsNullOrWhiteSpace(bitrate))
                return fallback;

            string value = bitrate.Trim().ToLower();

            if (value.EndsWith("k") || value.EndsWith("m"))
                return value;

            int raw;

            if (int.TryParse(value, out raw))
                return raw + "k";

            return fallback;
        }

        /// <summary>
        /// 비트레이트 기준으로 FFmpeg bufsize 값을 생성한다.
        /// 
        /// 일반적으로 CBR에 가깝게 유지하려면 bitrate의 2배 정도를 사용한다.
        /// 예:
        /// - 1200k → 2400k
        /// - 500k → 1000k
        /// </summary>
        /// <param name="bitrate">FFmpeg 비트레이트 문자열.</param>
        /// <returns>FFmpeg bufsize 문자열.</returns>
        private string BuildBufferSize(string bitrate)
        {
            int kbps = ParseBitrateKbps(bitrate);

            if (kbps <= 0)
                return "2400k";

            return (kbps * 2) + "k";
        }

        /// <summary>
        /// 비트레이트 문자열을 kbps 숫자로 변환한다.
        /// 
        /// 예:
        /// - 1200k → 1200
        /// - 2m → 2000
        /// </summary>
        /// <param name="bitrate">비트레이트 문자열.</param>
        /// <returns>kbps 값.</returns>
        private int ParseBitrateKbps(string bitrate)
        {
            if (string.IsNullOrWhiteSpace(bitrate))
                return 0;

            string value = bitrate.Trim().ToLower();

            try
            {
                if (value.EndsWith("k"))
                {
                    value = value.Substring(0, value.Length - 1);

                    int kbps;

                    if (int.TryParse(value, out kbps))
                        return kbps;
                }

                if (value.EndsWith("m"))
                {
                    value = value.Substring(0, value.Length - 1);

                    int mbps;

                    if (int.TryParse(value, out mbps))
                        return mbps * 1000;
                }

                int raw;

                if (int.TryParse(value, out raw))
                    return raw;
            }
            catch
            {
            }

            return 0;
        }


        /// <summary>
        /// RTSP 경로에서 앞쪽 / 를 제거한다.
        /// </summary>
        private string NormalizeRtspPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "poscam";

            path = path.Trim();

            while (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }

            return path;
        }

        /// <summary>
        /// Main/Sub 품질 설정값을 검증한다.
        /// </summary>
        private void ValidateStreamQuality(
            StreamQualityConfig quality,
            string name)
        {
            if (quality == null)
                throw new InvalidOperationException(name + " 설정이 없습니다.");

            if (quality.Fps <= 0)
                throw new InvalidOperationException(name + " FPS 값이 올바르지 않습니다.");

            if (string.IsNullOrWhiteSpace(quality.Bitrate))
                throw new InvalidOperationException(name + " Bitrate 값이 비어 있습니다.");

            if (string.IsNullOrWhiteSpace(quality.RtspPath))
                throw new InvalidOperationException(name + " RTSP 경로가 비어 있습니다.");
        }

        /// <summary>
        /// 모니터 해상도 정보를 검증한다.
        /// </summary>
        private void ValidateMonitorInfo(MonitorInfo monitorInfo)
        {
            if (monitorInfo.BoundsWidth <= 0 || monitorInfo.BoundsHeight <= 0)
                throw new InvalidOperationException("모니터 해상도 정보가 올바르지 않습니다.");
        }

        /// <summary>
        /// RTSP 서버 설정값을 검증한다.
        /// </summary>
        private void ValidateRtspServerConfig(RtspServerConfig rtspServerConfig)
        {
            if (rtspServerConfig.RtspPort <= 0 || rtspServerConfig.RtspPort > 65535)
                throw new InvalidOperationException("RTSP 포트 값이 올바르지 않습니다.");

            if (rtspServerConfig.RtmpPort <= 0 || rtspServerConfig.RtmpPort > 65535)
                throw new InvalidOperationException("RTMP 포트 값이 올바르지 않습니다.");
        }

        /// <summary>
        /// Main/Sub 동시 출력용 filter_complex 값을 생성한다.
        /// 
        /// 파이썬 버전과 유사하게 MainStream은 입력 FPS를 그대로 사용한다.
        /// SubStream만 미리보기용 저해상도/저프레임으로 변환한다.
        /// </summary>
        /// <param name="mainStream">MainStream 품질 설정.</param>
        /// <param name="subStream">SubStream 품질 설정.</param>
        /// <param name="monitorInfo">현재 캡처 대상 모니터 정보.</param>
        /// <returns>FFmpeg filter_complex 문자열.</returns>
        private string BuildMainSubFilterComplex(
            StreamQualityConfig mainStream,
            StreamQualityConfig subStream,
            MonitorInfo monitorInfo)
        {
            string subFilter = BuildSubVideoFilter(
                subStream,
                monitorInfo);

            return "[0:v]split=2[mainraw][subraw];" +
                   "[mainraw]null[mainout];" +
                   "[subraw]" + subFilter + "[subout]";
        }

        /// <summary>
        /// 단일 출력일 때 사용할 -vf 옵션 문자열을 생성한다.
        /// 
        /// Main만 사용하거나 Sub만 사용하는 경우에도
        /// fps 필터를 적용하여 실제 송출 FPS가 설정값을 초과하지 않도록 한다.
        /// </summary>
        /// <param name="quality">송출 품질 설정.</param>
        /// <param name="monitorInfo">현재 캡처 대상 모니터 정보.</param>
        /// <returns>FFmpeg -vf 옵션 문자열.</returns>
        private string BuildVideoFilterOption(
            StreamQualityConfig quality,
            MonitorInfo monitorInfo)
        {
            string filter = BuildVideoFilter(
                quality,
                monitorInfo);

            return "-vf \"" + filter + "\" ";
        }

        /// <summary>
        /// 단일 출력용 영상 필터 문자열을 생성한다.
        /// 
        /// setpts, settb 같은 시간정보 강제 조정은 사용하지 않는다.
        /// 필요 시 fps/scale만 적용한다.
        /// </summary>
        /// <param name="quality">송출 품질 설정.</param>
        /// <param name="monitorInfo">현재 캡처 대상 모니터 정보.</param>
        /// <returns>FFmpeg 비디오 필터 문자열.</returns>
        private string BuildVideoFilter(
            StreamQualityConfig quality,
            MonitorInfo monitorInfo)
        {
            int fps = GetSafeFps(quality);

            string filter = "fps=" + fps;

            if (quality != null &&
                quality.Width > 0 &&
                quality.Height > 0)
            {
                filter += ",scale=" + quality.Width + ":" + quality.Height;
            }

            return filter;
        }

        /// <summary>
        /// 품질 설정에서 안전한 FPS 값을 가져온다.
        /// 값이 없거나 잘못된 경우 5fps를 기본값으로 사용한다.
        /// </summary>
        /// <param name="quality">송출 품질 설정.</param>
        /// <returns>1 이상 FPS 값.</returns>
        private int GetSafeFps(StreamQualityConfig quality)
        {
            if (quality == null)
                return 5;

            if (quality.Fps <= 0)
                return 5;

            return quality.Fps;
        }

        /// <summary>
        /// StreamConfig 내부의 MainStream/SubStream 설정 객체를 보장한다.
        /// 
        /// 기존 코드처럼 지역 변수에만 기본 객체를 생성하면,
        /// FFmpeg Arguments 생성에는 사용되지만 ONVIF 응답 생성 쪽의 AppConfig에는 반영되지 않는다.
        /// 그래서 실제 런타임 설정 객체인 streamConfig.MainStream / streamConfig.SubStream에 직접 대입한다.
        /// </summary>
        /// <param name="streamConfig">스트림 설정.</param>
        private void EnsureStreamQualityConfigs(StreamConfig streamConfig)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (streamConfig.MainStream == null)
            {
                streamConfig.MainStream =
                    StreamQualityConfig.CreateMain(streamConfig.RtspPath);
            }

            if (streamConfig.SubStream == null)
            {
                streamConfig.SubStream =
                    StreamQualityConfig.CreateSub(streamConfig.RtspPath + "_sub");
            }
        }

        /// <summary>
        /// 실제 캡처 대상 모니터 해상도를 MainStream 설정에 반영한다.
        /// 
        /// MainStream.Width/Height가 0이면 원본 모니터 해상도를 사용한다는 의미로 처리한다.
        /// 이 값을 실제 MonitorInfo.BoundsWidth / BoundsHeight로 보정해야
        /// ONVIF GetProfiles / GetVideoEncoderConfigurationOptions 응답과 실제 RTSP 스트림 해상도가 일치한다.
        /// 
        /// SubStream은 보통 640x360처럼 명시된 해상도로 축소 송출하므로,
        /// 값이 이미 있으면 그대로 둔다.
        /// </summary>
        /// <param name="streamConfig">스트림 설정.</param>
        /// <param name="monitorInfo">실제 캡처 대상 모니터 정보.</param>
        private void ApplyActualMonitorResolution(
            StreamConfig streamConfig,
            MonitorInfo monitorInfo)
        {
            if (streamConfig == null)
                throw new ArgumentNullException("streamConfig");

            if (monitorInfo == null)
                throw new ArgumentNullException("monitorInfo");

            if (streamConfig.MainStream == null)
                streamConfig.MainStream = StreamQualityConfig.CreateMain(streamConfig.RtspPath);

            if (streamConfig.SubStream == null)
                streamConfig.SubStream = StreamQualityConfig.CreateSub(streamConfig.RtspPath + "_sub");

            /*
             * MainStream은 기본적으로 실제 화면 원본 해상도와 같아야 한다.
             * Width/Height가 0이면 FFmpeg에서는 scale을 하지 않고 원본으로 나가게 되므로,
             * ONVIF 응답도 실제 원본 해상도를 알려줘야 한다.
             */
            if (streamConfig.MainStream.Width <= 0)
                streamConfig.MainStream.Width = monitorInfo.BoundsWidth;

            if (streamConfig.MainStream.Height <= 0)
                streamConfig.MainStream.Height = monitorInfo.BoundsHeight;

            /*
             * SubStream은 축소 해상도가 설정되어 있으면 그대로 사용한다.
             * 단, 값이 비어 있는 경우에는 실제 화면 해상도를 기준으로 한다.
             * 기본 CreateSub가 640x360을 넣는 구조라면 보통 이 부분은 실행되지 않는다.
             */
            if (streamConfig.SubStream.Width <= 0)
                streamConfig.SubStream.Width = monitorInfo.BoundsWidth;

            if (streamConfig.SubStream.Height <= 0)
                streamConfig.SubStream.Height = monitorInfo.BoundsHeight;
        }

        /// <summary>
        /// FFmpeg가 MediaMTX로 publish할 로컬 RTMP URL을 생성한다.
        /// 
        /// FFmpeg는 RTMP/FLV로 MediaMTX에 영상을 입력하고,
        /// NVR/VLC는 MediaMTX가 제공하는 RTSP 주소를 읽는다.
        /// 
        /// MediaMTX의 RTMP 포트는 RtspServerConfig.RtmpPort 값을 사용한다.
        /// </summary>
        /// <param name="rtspPath">스트림 경로.</param>
        /// <param name="rtspServerConfig">RTSP/RTMP 서버 설정.</param>
        /// <returns>로컬 RTMP publish URL.</returns>
        private string BuildLocalRtmpPublishUrl(
            string rtspPath,
            RtspServerConfig rtspServerConfig)
        {
            if (rtspServerConfig == null)
                throw new ArgumentNullException("rtspServerConfig");

            string path = NormalizeRtspPath(rtspPath);

            return "rtmp://127.0.0.1:" + rtspServerConfig.RtmpPort + "/" + path;
        }

        /// <summary>
        /// FFmpeg RTMP/FLV 출력 옵션을 생성한다.
        /// 
        /// RTSP publish 대신 FLV 컨테이너로 RTMP publish한다.
        /// MediaMTX는 RTMP 입력을 받아 동일 path의 RTSP read를 제공한다.
        /// </summary>
        /// <returns>FFmpeg RTMP 출력 옵션 문자열.</returns>
        private string BuildRtmpOutputOptions()
        {
            return "-an " +
                   "-f flv " +
                   "-flvflags no_duration_filesize ";
        }

        /// <summary>
        /// SubStream용 영상 필터 문자열을 생성한다.
        /// 
        /// SubStream은 MainStream보다 낮은 FPS/해상도로 송출하기 위해
        /// fps와 scale 필터를 적용한다.
        /// </summary>
        /// <param name="quality">SubStream 품질 설정.</param>
        /// <param name="monitorInfo">현재 캡처 대상 모니터 정보.</param>
        /// <returns>SubStream 필터 문자열.</returns>
        private string BuildSubVideoFilter(
            StreamQualityConfig quality,
            MonitorInfo monitorInfo)
        {
            int fps = GetSafeFps(quality);

            string filter = "fps=" + fps;

            if (quality != null &&
                quality.Width > 0 &&
                quality.Height > 0)
            {
                filter += ",scale=" + quality.Width + ":" + quality.Height;
            }

            return filter;
        }
    }
}