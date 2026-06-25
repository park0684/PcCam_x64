using pccam_32.Models;
using PcCam_x64.Infrastructure;
using PcCam_x64.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PcCam_x64.Services
{
    /// <summary>
    /// PC CAM 설정 저장/로드 서비스.
    /// 
    /// 설정 파일은 config\pccam.ini 파일을 사용한다.
    /// JSON이나 외부 DLL 없이 Windows 기본 INI 방식을 사용한다.
    /// </summary>
    public class ConfigService
    {
        private readonly PathProvider _pathProvider;
        private readonly MonitorService _monitorService;

        public ConfigService(
            PathProvider pathProvider,
            MonitorService monitorService)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            if (monitorService == null)
                throw new ArgumentNullException("monitorService");

            _pathProvider = pathProvider;
            _monitorService = monitorService;
        }

        /// <summary>
        /// 설정 파일을 로드한다.
        /// 
        /// 파일이 없으면 기본 설정을 생성하고 저장한 뒤 반환한다.
        /// 파일이 있으면 Stream0, Stream1처럼 존재하는 Stream 섹션을 순서대로 로드한다.
        /// </summary>
        public AppConfig Load()
        {
            _pathProvider.EnsureDirectories();

            /*
             * IniFileHelper 생성자에서 파일이 자동 생성될 수 있으므로,
             * 파일 존재 여부는 Helper 생성 전에 확인한다.
             */
            bool configFileExists = File.Exists(_pathProvider.AppConfigFilePath);

            IniFileHelper ini = new IniFileHelper(_pathProvider.AppConfigFilePath);

            if (!configFileExists || IsConfigFileEmpty())
            {
                AppConfig defaultConfig = AppConfig.CreateDefault();

                /*
                 * 최초 설치 시 PCCAM 설치 단위의 영구 ID를 생성한다.
                 *
                 * 이 값은 이후 ONVIF UUID, SerialNumber,
                 * HardwareId, 가상 MAC 주소의 기준으로 사용한다.
                 */
                EnsureOnvifInstallationId(defaultConfig);

                /*
                 * 최초 설치/최초 실행 시에는 ScreenName이 비어 있을 수 있으므로
                 * 현재 PC 모니터 기준으로 자동 보정한다.
                 */
                NormalizeMonitorBindings(defaultConfig);

                Save(defaultConfig);
                return defaultConfig;
            }

            AppConfig config = AppConfig.CreateDefault();

            if (config.Streams == null)
                config.Streams = new List<StreamConfig>();
            else
                config.Streams.Clear();

            LoadStreams(ini, config);

            if (config.Streams == null || config.Streams.Count == 0)
                config.Streams = AppConfig.CreateDefault().Streams;

            LoadOnvif(ini, config);
            LoadAuth(ini, config);
            LoadOperation(ini, config);
            LoadRtspServer(ini, config);

            /*
             * 기존 설정 파일에 ScreenName이 비어 있거나,
             * 현재 PC에 존재하지 않는 모니터 장치명이 저장되어 있으면 보정한다.
             */
            NormalizeMonitorBindings(config);

            /*
             * 보정된 ScreenName을 설정 파일에 반영한다.
             */
            Save(config);

            return config;
        }

        /// <summary>
        /// 설정 파일을 저장한다.
        /// 
        /// Stream 설정은 현재 config.Streams 개수만큼 저장한다.
        /// 저장 후 INI 파일의 섹션 사이에 빈 줄을 넣어 가독성을 높인다.
        /// </summary>
        public void Save(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            _pathProvider.EnsureDirectories();

            IniFileHelper ini = new IniFileHelper(_pathProvider.AppConfigFilePath);

            if (config.Streams == null || config.Streams.Count == 0)
                config.Streams = AppConfig.CreateDefault().Streams;

            /*
             * 설정 화면에서 ScreenName을 표시하지 않기 때문에,
             * 저장 직전에도 반드시 현재 PC 모니터 기준으로 보정한다.
             */
            NormalizeMonitorBindings(config);


            for (int i = 0; i < config.Streams.Count; i++)
            {
                SaveStream(ini, config.Streams[i]);
            }

            EnsureOnvifInstallationId(config);

            SaveOnvif(ini, config.Onvif ?? new OnvifConfig());
            SaveAuth(ini, config.Auth ?? new AuthConfig());
            SaveOperation(ini, config.Operation ?? new OperationConfig());
            SaveRtspServer(ini, config.RtspServer ?? new RtspServerConfig());

            /*
             * IniFileHelper가 섹션 간 공백을 관리하지 않으므로,
             * 모든 설정 저장이 끝난 뒤 파일을 한 번 정리한다.
             */
            FormatIniSectionSpacing();
        }

        /// <summary>
        /// INI 파일에 존재하는 Stream 섹션을 순서대로 로드한다.
        /// 
        /// 예:
        /// [Stream0], [Stream1], [Stream2] ...
        /// 
        /// 중간 번호가 비어 있으면 그 이후는 로드하지 않는다.
        /// </summary>
        private void LoadStreams(IniFileHelper ini, AppConfig config)
        {
            const int maxStreamCount = 16;

            for (int streamNo = 0; streamNo < maxStreamCount; streamNo++)
            {
                if (!HasStreamSection(ini, streamNo))
                    break;

                LoadStream(ini, config, streamNo);
            }
        }

        /// <summary>
        /// 지정한 Stream 섹션이 INI 파일에 존재하는지 확인한다.
        /// 
        /// IniFileHelper에 섹션 존재 확인 기능이 없으므로,
        /// 주요 키를 sentinel 값으로 읽어 존재 여부를 판단한다.
        /// </summary>
        private bool HasStreamSection(IniFileHelper ini, int streamNo)
        {
            string section = "Stream" + streamNo;
            const string missing = "__PCCAM_MISSING__";

            string streamNoValue = ini.ReadString(section, "StreamNo", missing);
            if (streamNoValue != missing)
                return true;

            string enabledValue = ini.ReadString(section, "IsEnabled", missing);
            if (enabledValue != missing)
                return true;

            string rtspPathValue = ini.ReadString(section, "RtspPath", missing);
            if (rtspPathValue != missing)
                return true;

            string screenNameValue = ini.ReadString(section, "ScreenName", missing);
            if (screenNameValue != missing)
                return true;

            return false;
        }

        /// <summary>
        /// 지정한 Stream 섹션을 로드한다.
        /// </summary>
        private void LoadStream(IniFileHelper ini, AppConfig config, int streamNo)
        {
            string section = "Stream" + streamNo;

            while (config.Streams.Count <= streamNo)
            {
                config.Streams.Add(new StreamConfig
                {
                    StreamNo = config.Streams.Count
                });
            }

            StreamConfig stream = config.Streams[streamNo];

            stream.IsEnabled = ini.ReadBool(section, "IsEnabled", stream.IsEnabled);
            stream.StreamNo = ini.ReadInt(section, "StreamNo", stream.StreamNo);

            /*
             * MonitorRole은 Primary, Secondary, Monitor2 같은 역할명이다.
             */
            stream.MonitorRole = ini.ReadString(section, "MonitorRole", stream.MonitorRole);

            /*
             * ScreenName은 현재 단계에서 화면 식별명으로 사용한다.
             */
            stream.ScreenName = ini.ReadString(section, "ScreenName", stream.ScreenName);
            stream.DisplayName = ini.ReadString(section, "DisplayName", stream.DisplayName);

            if (string.IsNullOrWhiteSpace(stream.DisplayName))
            {
                if (stream.StreamNo == 0)
                    stream.DisplayName = "주 모니터";
                else
                    stream.DisplayName = "보조 모니터";
            }

            stream.OnvifPort = ini.ReadInt(section, "OnvifPort", stream.OnvifPort);
            stream.Fps = ini.ReadInt(section, "Fps", stream.Fps);
            stream.Bitrate = ini.ReadString(section, "Bitrate", stream.Bitrate);
            stream.Codec = ini.ReadString(section, "Codec", stream.Codec);
            stream.RtspPath = ini.ReadString(section, "RtspPath", stream.RtspPath);

            /*
            * Main/Sub Stream 품질 설정 로드.
            */
            stream.MainStream = LoadStreamQuality(
                ini,
                "Stream" + streamNo + ".Main",
                StreamQualityConfig.CreateMain(stream.RtspPath));

            stream.SubStream = LoadStreamQuality(
                ini,
                "Stream" + streamNo + ".Sub",
                StreamQualityConfig.CreateSub(stream.RtspPath + "_sub"));

            /*
             * Main/Sub RTSP 경로 보정.
             * SubStream 경로가 비어 있거나 MainStream과 같으면
             * 기본적으로 Main 경로 + "_sub" 형태로 보정한다.
             */
            if (stream.MainStream != null && stream.SubStream != null)
            {
                string mainPath = stream.MainStream.RtspPath;
                string subPath = stream.SubStream.RtspPath;

                if (string.IsNullOrWhiteSpace(subPath) ||
                    string.Equals(mainPath, subPath, StringComparison.OrdinalIgnoreCase))
                {
                    stream.SubStream.RtspPath =
                        string.IsNullOrWhiteSpace(stream.RtspPath)
                            ? "poscam_sub"
                            : stream.RtspPath + "_sub";
                }
            }
        }

        /// <summary>
        /// 지정한 Stream 설정을 INI 파일에 저장한다.
        /// </summary>
        private void SaveStream(IniFileHelper ini, StreamConfig stream)
        {
            if (stream == null)
                return;

            //저장 전 Main/Sub RTSP 경로 보정. Stream.RtspPath를 기준으로 Main/Sub 경로가 올바르게 설정되도록 보정한다.
            NormalizeStreamQualityPaths(stream);
            /*
             * 현재 설정 화면에는 Main/Sub 개별 사용 여부가 없다.
             * 따라서 부모 Stream의 사용 여부가 Main/Sub 사용 여부의 기준이다.
             * 
             * Stream 사용 체크됨  → Main/Sub 모두 사용
             * Stream 사용 해제됨 → Main/Sub 모두 미사용
             */
            ApplyStreamQualityEnabledPolicy(stream);

            string section = "Stream" + stream.StreamNo;



            ini.WriteBool(section, "IsEnabled", stream.IsEnabled);
            ini.WriteInt(section, "StreamNo", stream.StreamNo);
            ini.WriteString(section, "MonitorRole", stream.MonitorRole);
            ini.WriteString(section, "ScreenName", stream.ScreenName);
            ini.WriteString(section, "DisplayName", stream.DisplayName);
            ini.WriteInt(section, "OnvifPort", stream.OnvifPort);
            ini.WriteInt(section, "Fps", stream.Fps);
            ini.WriteString(section, "Bitrate", stream.Bitrate);
            ini.WriteString(section, "Codec", stream.Codec);
            ini.WriteString(section, "RtspPath", stream.RtspPath);

            /*
            * Main/Sub Stream 품질 설정 저장.
            * 
            * 부모 Stream이 비활성화되어 있으면
            * Main/Sub도 설정 파일상 비활성화 상태로 저장한다.
            */
            StreamQualityConfig mainStream =
                stream.MainStream ?? StreamQualityConfig.CreateMain(stream.RtspPath);

            StreamQualityConfig subStream =
                stream.SubStream ?? StreamQualityConfig.CreateSub(stream.RtspPath + "_sub");

            SaveStreamQuality(
                ini,
                "Stream" + stream.StreamNo + ".Main",
                mainStream,
                stream.IsEnabled);

            SaveStreamQuality(
                ini,
                "Stream" + stream.StreamNo + ".Sub",
                subStream,
                stream.IsEnabled);
        }

        /// <summary>
        /// 품질별 스트림 설정을 INI에 저장한다.
        /// 
        /// 부모 Stream이 비활성화되어 있으면
        /// 품질 스트림도 실제 사용 불가 상태로 저장한다.
        /// </summary>
        private void SaveStreamQuality(
            IniFileHelper ini,
            string section,
            StreamQualityConfig quality,
            bool parentStreamEnabled)
        {
            if (quality == null)
                return;

            /*
             * Main/Sub 개별 사용 여부는 현재 UI에서 관리하지 않는다.
             * 부모 Stream이 사용이면 품질 Stream도 사용으로 저장한다.
             */
            bool effectiveEnabled = parentStreamEnabled;

            ini.WriteBool(section, "IsEnabled", effectiveEnabled);
            ini.WriteString(section, "RtspPath", quality.RtspPath);
            ini.WriteInt(section, "Fps", quality.Fps);
            ini.WriteString(section, "Bitrate", quality.Bitrate);
            ini.WriteInt(section, "Width", quality.Width);
            ini.WriteInt(section, "Height", quality.Height);
        }

        /// <summary>
        /// 품질별 스트림 설정을 INI에서 로드한다.
        /// 
        /// 해당 섹션이 없으면 기본값을 유지한다.
        /// </summary>
        private StreamQualityConfig LoadStreamQuality(
            IniFileHelper ini,
            string section,
            StreamQualityConfig defaultValue)
        {
            StreamQualityConfig quality = defaultValue ?? new StreamQualityConfig();

            quality.IsEnabled = ini.ReadBool(section, "IsEnabled", quality.IsEnabled);
            quality.RtspPath = ini.ReadString(section, "RtspPath", quality.RtspPath);
            quality.Fps = ini.ReadInt(section, "Fps", quality.Fps);
            quality.Bitrate = ini.ReadString(section, "Bitrate", quality.Bitrate);
            quality.Width = ini.ReadInt(section, "Width", quality.Width);
            quality.Height = ini.ReadInt(section, "Height", quality.Height);

            return quality;
        }

        private void LoadOnvif(IniFileHelper ini, AppConfig config)
        {
            if (config.Onvif == null) 
                config.Onvif = new OnvifConfig();

            config.Onvif.IsEnabled = ini.ReadBool("Onvif", "IsEnabled", config.Onvif.IsEnabled);
            config.Onvif.UserId = ini.ReadString("Onvif", "UserId", config.Onvif.UserId);

            string savedPassword = ini.ReadString("Onvif", "Password", config.Onvif.Password);
            config.Onvif.Password = ConfigCryptoService.Decrypt(savedPassword);

            /*
             * 기존 버전의 설정에는 InstallationId 키가 없다.
             *
             * 이 경우 빈 문자열로 로드되며,
             * Load()의 EnsureOnvifInstallationId()에서 새로 생성한다.
             */
            config.Onvif.InstallationId = ini.ReadString("Onvif", "InstallationId", config.Onvif.InstallationId);
        }

        private void SaveOnvif(IniFileHelper ini, OnvifConfig onvif)
        {
            ini.WriteBool("Onvif", "IsEnabled", onvif.IsEnabled);
            ini.WriteString("Onvif", "UserId", onvif.UserId);
            ini.WriteString("Onvif", "Password", ConfigCryptoService.Encrypt(onvif.Password));
            ini.WriteString("Onvif", "InstallationId", onvif.InstallationId);
        }

        private void LoadAuth(IniFileHelper ini, AppConfig config)
        {
            if (config.Auth == null)
                config.Auth = new AuthConfig();

            config.Auth.LicenseKey = ini.ReadString("Auth", "LicenseKey", config.Auth.LicenseKey);
            config.Auth.DeviceName = ini.ReadString("Auth", "DeviceName", config.Auth.DeviceName);
            config.Auth.LastAuthResult = ini.ReadString("Auth", "LastAuthResult", config.Auth.LastAuthResult);

            string lastAuthAt = ini.ReadString("Auth", "LastAuthAt", "");

            DateTime parsed;
            if (DateTime.TryParse(lastAuthAt, out parsed))
                config.Auth.LastAuthAt = parsed;
            else
                config.Auth.LastAuthAt = null;
        }

        private void SaveAuth(IniFileHelper ini, AuthConfig auth)
        {
            ini.WriteString("Auth", "LicenseKey", auth.LicenseKey);
            ini.WriteString("Auth", "DeviceName", auth.DeviceName);
            ini.WriteString("Auth", "LastAuthResult", auth.LastAuthResult);

            if (auth.LastAuthAt.HasValue)
                ini.WriteString("Auth", "LastAuthAt", auth.LastAuthAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            else
                ini.WriteString("Auth", "LastAuthAt", "");
        }

        private void LoadOperation(IniFileHelper ini, AppConfig config)
        {
            if (config.Operation == null)
                config.Operation = new OperationConfig();

            config.Operation.EnableDetailLog = ini.ReadBool("Operation", "EnableDetailLog", config.Operation.EnableDetailLog);
            config.Operation.AutoStart = ini.ReadBool("Operation", "AutoStart", config.Operation.AutoStart);
            config.Operation.PreventSleep = ini.ReadBool("Operation", "PreventSleep", config.Operation.PreventSleep);
            config.Operation.AutoStartStreaming = ini.ReadBool("Operation", "AutoStartStreaming", config.Operation.AutoStartStreaming);
        }

        private void SaveOperation(IniFileHelper ini, OperationConfig operation)
        {
            ini.WriteBool("Operation", "EnableDetailLog", operation.EnableDetailLog);
            ini.WriteBool("Operation", "AutoStart", operation.AutoStart);
            ini.WriteBool("Operation", "PreventSleep", operation.PreventSleep);
            ini.WriteBool("Operation", "AutoStartStreaming", operation.AutoStartStreaming);
        }

        private void LoadRtspServer(IniFileHelper ini, AppConfig config)
        {
            if (config.RtspServer == null)
                config.RtspServer = new RtspServerConfig();

            config.RtspServer.RtspPort = ini.ReadInt("RtspServer", "RtspPort", config.RtspServer.RtspPort);
            config.RtspServer.RtmpPort = ini.ReadInt("RtspServer", "RtmpPort", config.RtspServer.RtmpPort);
            config.RtspServer.ServerExeName = ini.ReadString("RtspServer", "ServerExeName", config.RtspServer.ServerExeName);
            //config.RtspServer.ConfigFileName = ini.ReadString("RtspServer", "ConfigFileName", config.RtspServer.ConfigFileName);
        }

        private void SaveRtspServer(IniFileHelper ini, RtspServerConfig rtspServer)
        {
            ini.WriteInt("RtspServer", "RtspPort", rtspServer.RtspPort);
            ini.WriteInt("RtspServer", "RtmpPort", rtspServer.RtmpPort);
            ini.WriteString("RtspServer", "ServerExeName", rtspServer.ServerExeName);
            //ini.WriteString("RtspServer", "ConfigFileName", rtspServer.ConfigFileName);
        }


        /// <summary>
        /// INI 파일의 섹션 사이에 빈 줄을 넣어 가독성을 높인다.
        /// 
        /// 파일 인코딩은 UTF-8 BOM으로 유지한다.
        /// </summary>
        private void FormatIniSectionSpacing()
        {
            string filePath = _pathProvider.AppConfigFilePath;

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            if (!File.Exists(filePath))
                return;

            Encoding utf8WithBom = new UTF8Encoding(true);

            string[] lines = File.ReadAllLines(filePath, utf8WithBom);

            List<string> formatted = new List<string>();
            bool hasWrittenLine = false;
            bool previousLineWasBlank = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? "";
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                    continue;

                bool isSectionHeader =
                    trimmed.StartsWith("[") &&
                    trimmed.EndsWith("]");

                if (isSectionHeader && hasWrittenLine && !previousLineWasBlank)
                {
                    formatted.Add("");
                    previousLineWasBlank = true;
                }

                formatted.Add(line);
                hasWrittenLine = true;
                previousLineWasBlank = false;
            }

            File.WriteAllLines(filePath, formatted.ToArray(), utf8WithBom);
        }

        /// <summary>
        /// StreamConfig의 Main/Sub RTSP 경로를 부모 Stream의 RtspPath 기준으로 보정한다.
        /// 
        /// 현재 정책:
        /// - Stream.RtspPath가 기준 경로이다.
        /// - MainStream.RtspPath는 Stream.RtspPath와 동일해야 한다.
        /// - SubStream.RtspPath는 Stream.RtspPath + "_sub" 이어야 한다.
        /// 
        /// 예:
        /// Stream1.RtspPath = poscam_1
        /// → Main = poscam_1
        /// → Sub  = poscam_1_sub
        /// </summary>
        private void NormalizeStreamQualityPaths(StreamConfig stream)
        {
            if (stream == null)
                return;

            string basePath = stream.RtspPath;

            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = stream.StreamNo == 0
                    ? "poscam"
                    : "poscam_" + stream.StreamNo;

                stream.RtspPath = basePath;
            }

            if (stream.MainStream == null)
                stream.MainStream = StreamQualityConfig.CreateMain(basePath);

            if (stream.SubStream == null)
                stream.SubStream = StreamQualityConfig.CreateSub(basePath + "_sub");

            /*
             * 기존에 Stream1.Main이 poscam으로 잘못 저장된 경우를 보정한다.
             */
            stream.MainStream.RtspPath = basePath;

            /*
             * 기존에 Stream1.Sub가 poscam_sub로 잘못 저장된 경우를 보정한다.
             */
            stream.SubStream.RtspPath = basePath + "_sub";
        }

        /// <summary>
        /// 설정 파일이 비어 있는지 확인한다.
        /// 
        /// IniFileHelper가 파일을 자동 생성한 경우,
        /// 파일은 존재하지만 내용이 없을 수 있다.
        /// 이 경우 기본 설정을 저장해야 한다.
        /// </summary>
        private bool IsConfigFileEmpty()
        {
            string filePath = _pathProvider.AppConfigFilePath;

            if (string.IsNullOrWhiteSpace(filePath))
                return true;

            if (!File.Exists(filePath))
                return true;

            FileInfo fileInfo = new FileInfo(filePath);

            return fileInfo.Length == 0;
        }

        /// <summary>
        /// 현재 PC에 연결된 모니터 기준으로 StreamConfig.ScreenName을 보정한다.
        /// 
        /// ScreenName은 사용자가 입력하는 표시명이 아니라
        /// Windows 모니터 장치명이다.
        /// 예: \\.\DISPLAY1
        /// 
        /// 설정 화면에서는 ScreenName을 숨겼으므로,
        /// 설정 로드/저장 시점에 서비스에서 자동 보정한다.
        /// </summary>
        private void NormalizeMonitorBindings(AppConfig config)
        {
            if (config == null || config.Streams == null)
                return;

            List<MonitorInfo> monitors = _monitorService.GetMonitors();

            for (int i = 0; i < config.Streams.Count; i++)
            {
                StreamConfig stream = config.Streams[i];

                if (stream == null)
                    continue;

                stream.StreamNo = i;
                stream.MonitorRole = GetDefaultMonitorRole(i);

                /*
                 * 현재 PC의 모니터 수보다 Stream 수가 많으면
                 * 해당 Stream은 실제 송출할 수 없으므로 비활성화한다.
                 */
                if (i >= monitors.Count)
                {
                    stream.IsEnabled = false;
                    stream.ScreenName = "";
                    DisableQualityStreams(stream);
                    continue;
                }

                MonitorInfo monitor = monitors[i];

                /*
                 * ScreenName이 비어 있거나 현재 PC에 존재하지 않는 값이면
                 * 현재 모니터 기준으로 다시 지정한다.
                 */
                if (string.IsNullOrWhiteSpace(stream.ScreenName) ||
                    !ContainsMonitor(monitors, stream.ScreenName))
                {
                    stream.ScreenName = monitor.DeviceName;
                }

                if (string.IsNullOrWhiteSpace(stream.DisplayName))
                    stream.DisplayName = GetDefaultDisplayName(i);
            }
        }

        /// <summary>
        /// 현재 모니터 목록에 지정한 DeviceName이 존재하는지 확인한다.
        /// </summary>
        private bool ContainsMonitor(List<MonitorInfo> monitors, string deviceName)
        {
            if (monitors == null || string.IsNullOrWhiteSpace(deviceName))
                return false;

            foreach (MonitorInfo monitor in monitors)
            {
                if (string.Equals(
                    monitor.DeviceName,
                    deviceName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// StreamNo 기준 기본 MonitorRole 값을 반환한다.
        /// </summary>
        private string GetDefaultMonitorRole(int streamNo)
        {
            if (streamNo == 0)
                return "Primary";

            if (streamNo == 1)
                return "Secondary";

            return "Monitor" + streamNo;
        }

        /// <summary>
        /// StreamNo 기준 기본 표시명을 반환한다.
        /// </summary>
        private string GetDefaultDisplayName(int streamNo)
        {
            if (streamNo == 0)
                return "주 모니터";

            if (streamNo == 1)
                return "보조 모니터";

            return "모니터 " + streamNo;
        }

        /// <summary>
        /// 부모 Stream이 비활성화될 때 Main/Sub Stream도 함께 비활성화한다.
        /// </summary>
        private void DisableQualityStreams(StreamConfig stream)
        {
            if (stream == null)
                return;

            if (stream.MainStream != null)
                stream.MainStream.IsEnabled = false;

            if (stream.SubStream != null)
                stream.SubStream.IsEnabled = false;
        }

        /// <summary>
        /// 부모 Stream 사용 여부를 Main/Sub Stream 사용 여부에 반영한다.
        /// 
        /// 현재 설정 화면에서는 Main/Sub 개별 사용 체크를 제공하지 않으므로,
        /// 부모 Stream의 IsEnabled 값이 Main/Sub의 사용 여부를 결정한다.
        /// </summary>
        private void ApplyStreamQualityEnabledPolicy(StreamConfig stream)
        {
            if (stream == null)
                return;

            string basePath = stream.RtspPath;

            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = stream.StreamNo == 0
                    ? "poscam"
                    : "poscam_" + stream.StreamNo;

                stream.RtspPath = basePath;
            }

            if (stream.MainStream == null)
                stream.MainStream = StreamQualityConfig.CreateMain(basePath);

            if (stream.SubStream == null)
                stream.SubStream = StreamQualityConfig.CreateSub(basePath + "_sub");

            stream.MainStream.IsEnabled = stream.IsEnabled;
            stream.SubStream.IsEnabled = stream.IsEnabled;
        }

        /// <summary>
        /// PCCAM 설치 단위의 ONVIF 영구 식별자를 확인하고 보정한다.
        ///
        /// 처리 규칙:
        /// 1. OnvifConfig가 없으면 생성한다.
        /// 2. InstallationId가 유효한 GUID이면 N 형식으로 정규화한다.
        /// 3. 값이 없거나 손상되었으면 새로운 GUID를 생성한다.
        ///
        /// InstallationId는 이후 다음 값을 만드는 기준으로 사용한다.
        /// - WS-Discovery Endpoint UUID
        /// - SerialNumber
        /// - HardwareId
        /// - 스트림별 가상 MAC 주소
        /// </summary>
        /// <param name="config">
        /// 보정할 PCCAM 전체 설정.
        /// </param>
        private void EnsureOnvifInstallationId(
            AppConfig config)
        {
            if (config == null)
                return;

            if (config.Onvif == null)
                config.Onvif = new OnvifConfig();

            string installationId =
                config.Onvif.InstallationId;

            Guid parsedId;

            /*
             * Guid.TryParse를 사용하므로 기존 값이
             * N 형식 또는 D 형식이어도 정상적으로 읽을 수 있다.
             */
            if (Guid.TryParse(
                    installationId,
                    out parsedId))
            {
                /*
                 * 파일 내 표기 형식을 하나로 통일한다.
                 *
                 * N 형식:
                 * - 하이픈 없음
                 * - 32자리
                 */
                config.Onvif.InstallationId =
                    parsedId
                        .ToString("N")
                        .ToUpperInvariant();

                return;
            }

            /*
             * 최초 설치이거나 기존 값이 손상된 경우
             * 새로운 설치 ID를 생성한다.
             */
            config.Onvif.InstallationId =
                Guid.NewGuid()
                    .ToString("N")
                    .ToUpperInvariant();
        }
    }
}