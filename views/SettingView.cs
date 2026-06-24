using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PcCam_x64.Models;
using PcCam_x64.Infrastructure;

namespace PcCam_x64.Views
{
    /// <summary>
    /// PC CAM 설정 화면.
    /// 
    /// 기존 Python 버전의 설정 화면 구조를 참고하되,
    /// C# 1단계 Core MVP에 필요한 항목만 구성한다.
    /// 
    /// 화면 구성:
    /// - 스트림 설정
    /// - ONVIF 보안
    /// - 인증
    /// - 운영 설정
    /// - 적용 / 확인 / 취소
    /// </summary>
    public class SettingView : Form, ISettingView
    {
        private const int StreamCount = 2;

        private readonly CheckBox[] _chkUse = new CheckBox[StreamCount];
        private readonly Label[] _lblNo = new Label[StreamCount];
        //private readonly Label[] _lblMonitor = new Label[StreamCount];
        private readonly TextBox[] _txtOnvifPort = new TextBox[StreamCount];
        //private readonly TextBox[] _txtScreenName = new TextBox[StreamCount];
        private readonly TextBox[] _txtDisplayName = new TextBox[StreamCount];
        private readonly ComboBox[] _cboFps = new ComboBox[StreamCount];
        private readonly ComboBox[] _cboBitrate = new ComboBox[StreamCount];
        private readonly ComboBox[] _cboCodec = new ComboBox[StreamCount];

        private TextBox _txtOnvifUserId;
        private TextBox _txtOnvifPassword;

        private TextBox _txtLicenseKey;
        private TextBox _txtDeviceName;
        private Button _btnAuthAction;

        private CheckBox _chkEnableDetailLog;
        private CheckBox _chkAutoStart;
        private CheckBox _chkPreventSleep;
        private CheckBox _chkAutoStartStreaming;

        private Button _btnApply;
        private Button _btnOk;
        private Button _btnCancel;

        public event EventHandler ApplyRequested;
        public event EventHandler OkRequested;
        public event EventHandler CancelRequested;
        public event EventHandler AuthActionRequested;

        /// <summary>
        /// 설정 화면에 로드된 원본 Stream 설정 목록.
        /// 
        /// 화면에는 일부 항목만 표시되므로,
        /// MainStream/SubStream처럼 화면에 직접 표시하지 않는 설정은
        /// 저장 시 기존 값을 유지하기 위해 보관한다.
        /// </summary>
        private List<StreamConfig> _loadedStreams = new List<StreamConfig>();
        public SettingView()
        {

            InitializeComponent();
        }

        /// <summary>
        /// 스트림 설정 목록.
        /// 
        /// 현재 설정 화면에서는 Stream 단위의 기본 정보와 MainStream 품질 일부만 표시한다.
        /// SubStream 정보는 화면에 직접 표시하지 않으므로 기존 설정을 유지한다.
        /// </summary>
        public List<StreamConfig> Streams
        {
            get
            {
                List<StreamConfig> result = new List<StreamConfig>();

                for (int i = 0; i < StreamCount; i++)
                {
                    string rtspPath = GetDefaultRtspPath(i);

                    StreamConfig source = FindStream(_loadedStreams, i);

                    StreamQualityConfig mainStream =
                        CloneStreamQuality(
                            source == null ? null : source.MainStream,
                            StreamQualityConfig.CreateMain(rtspPath));

                    StreamQualityConfig subStream =
                        CloneStreamQuality(
                            source == null ? null : source.SubStream,
                            StreamQualityConfig.CreateSub(rtspPath + "_sub"));

                    mainStream.RtspPath = rtspPath;
                    subStream.RtspPath = rtspPath + "_sub";

                    /*
                     * 화면에는 Main/Sub 개별 사용 체크가 없으므로
                     * 부모 Stream 체크 상태를 Main/Sub에도 반영한다.
                     */
                    mainStream.IsEnabled = _chkUse[i].Checked;
                    subStream.IsEnabled = _chkUse[i].Checked;

                    mainStream.Fps = ToInt(Convert.ToString(_cboFps[i].SelectedItem), mainStream.Fps);
                    mainStream.Bitrate = Convert.ToString(_cboBitrate[i].SelectedItem);

                    result.Add(new StreamConfig
                    {
                        //IsEnabled = _chkUse[i].Checked,
                        IsEnabled = _chkUse[i].Checked && HasScreenForStream(i),
                        StreamNo = i,

                        /*
                         * 화면에는 표시하지 않지만 내부 모니터 매칭에 필요하므로
                         * 기존 설정값을 그대로 유지한다.
                         */
                        MonitorRole = source == null ? GetDefaultMonitorRole(i) : source.MonitorRole,
                        //ScreenName = source == null ? "" : source.ScreenName,
                        ScreenName = ResolveScreenName(source, i),

                        DisplayName = _txtDisplayName[i].Text.Trim(),
                        OnvifPort = ToInt(_txtOnvifPort[i].Text, i == 0 ? 8080 : 8081),

                        Fps = mainStream.Fps,
                        Bitrate = mainStream.Bitrate,
                        Codec = Convert.ToString(_cboCodec[i].SelectedItem),
                        RtspPath = rtspPath,

                        MainStream = mainStream,
                        SubStream = subStream
                    });
                }

                return result;
            }
            set
            {
                List<StreamConfig> streams = value ?? new List<StreamConfig>();
                _loadedStreams = streams;

                for (int i = 0; i < StreamCount; i++)
                {
                    StreamConfig stream = FindStream(streams, i);

                    if (stream == null)
                    {
                        string defaultRtspPath = GetDefaultRtspPath(i);

                        stream = new StreamConfig
                        {
                            StreamNo = i,
                            MonitorRole = GetDefaultMonitorRole(i),
                            IsEnabled = i == 0,
                            ScreenName = "",
                            DisplayName = GetDefaultDisplayName(i),
                            OnvifPort = i == 0 ? 8080 : 8081,
                            Fps = 5,
                            Bitrate = "1200k",
                            Codec = "H264",
                            RtspPath = defaultRtspPath,
                            MainStream = StreamQualityConfig.CreateMain(defaultRtspPath),
                            SubStream = StreamQualityConfig.CreateSub(defaultRtspPath + "_sub")
                        };
                    }

                    string rtspPath = string.IsNullOrWhiteSpace(stream.RtspPath)
                        ? GetDefaultRtspPath(i)
                        : stream.RtspPath;

                    StreamQualityConfig mainStream =
                        stream.MainStream ?? StreamQualityConfig.CreateMain(rtspPath);

                    mainStream.RtspPath = rtspPath;

                    _chkUse[i].Checked = stream.IsEnabled;
                    _lblNo[i].Text = stream.StreamNo.ToString();

                    _txtDisplayName[i].Text = string.IsNullOrWhiteSpace(stream.DisplayName)
                        ? GetDefaultDisplayName(i)
                        : stream.DisplayName;

                    _txtOnvifPort[i].Text = stream.OnvifPort.ToString();

                    SelectComboValue(_cboFps[i], mainStream.Fps.ToString());
                    SelectComboValue(_cboBitrate[i], mainStream.Bitrate);
                    SelectComboValue(_cboCodec[i], stream.Codec);
                }
            }
        }

        public string OnvifUserId
        {
            get { return _txtOnvifUserId.Text.Trim(); }
            set { _txtOnvifUserId.Text = value ?? ""; }
        }

        public string OnvifPassword
        {
            get { return _txtOnvifPassword.Text; }
            set { _txtOnvifPassword.Text = value ?? ""; }
        }

        public string LicenseKey
        {
            get { return _txtLicenseKey.Text.Trim(); }
            set { _txtLicenseKey.Text = value ?? ""; }
        }

        public string DeviceName
        {
            get { return _txtDeviceName.Text.Trim(); }
            set { _txtDeviceName.Text = value ?? ""; }
        }

        public bool EnableDetailLog
        {
            get { return _chkEnableDetailLog.Checked; }
            set { _chkEnableDetailLog.Checked = value; }
        }

        public bool AutoStart
        {
            get { return _chkAutoStart.Checked; }
            set { _chkAutoStart.Checked = value; }
        }

        public bool PreventSleep
        {
            get { return _chkPreventSleep.Checked; }
            set { _chkPreventSleep.Checked = value; }
        }

        public bool AutoStartStreaming
        {
            get { return _chkAutoStartStreaming.Checked; }
            set { _chkAutoStartStreaming.Checked = value; }
        }

        public void ShowForm()
        {
            if (Visible)
            {
                Activate();
                BringToFront();
                return;
            }

            Show();
            Activate();
        }

        public void CloseForm()
        {
            Close();
        }

        public void ShowInfo(string message)
        {
            MessageBox.Show(
                this,
                message,
                "PC CAM",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        public void ShowError(string message)
        {
            MessageBox.Show(
                this,
                message,
                "PC CAM 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        public void SetAuthButtonText(string text)
        {
            if (_btnAuthAction == null)
                return;

            _btnAuthAction.Text = string.IsNullOrWhiteSpace(text) ? "등록" : text;
        }

        public bool Confirm(string message)
        {
            DialogResult result = MessageBox.Show(
                this,
                message,
                "PC CAM",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            return result == DialogResult.Yes;
        }

        /// <summary>
        /// 인증키 입력 TextBox의 입력 가능 여부를 변경한다.
        /// 
        /// 인증 등록 전에는 입력 가능하게 하고,
        /// 인증 등록 후에는 사용자가 임의로 인증키를 수정하지 못하도록 읽기 전용으로 전환한다.
        /// </summary>
        /// <param name="enabled">
        /// true: 인증키 입력 가능
        /// false: 인증키 읽기 전용
        /// </param>
        public void SetLicenseKeyInputEnabled(bool enabled)
        {
            if (_txtLicenseKey == null)
                return;

            _txtLicenseKey.ReadOnly = !enabled;
            _txtLicenseKey.TabStop = enabled;
        }

        private void InitializeComponent()
        {
            Text = "PC CAM 설정 - " + AppVersionProvider.DisplayVersion;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            Width = 760;
            Height = 390;

            Font = new Font("맑은 고딕", 9F, FontStyle.Regular);

            CreateStreamGroup();
            CreateOnvifGroup();
            CreateAuthGroup();
            CreateOperationGroup();
            CreateButtons();

            _txtLicenseKey.ReadOnly = false;
            _txtLicenseKey.Enabled = true;
        }

        private void CreateStreamGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "스트림";
            group.Left = 15;
            group.Top = 15;
            group.Width = 710;
            group.Height = 130;
            Controls.Add(group);

            AddHeader(group, "사용", 20, 25, 45);
            AddHeader(group, "번호", 70, 25, 40);
            AddHeader(group, "표시명", 120, 25, 150);
            AddHeader(group, "ONVIF 포트", 285, 25, 90);
            AddHeader(group, "Main FPS", 390, 25, 75);
            AddHeader(group, "Main Bitrate", 475, 25, 100);
            AddHeader(group, "Codec", 590, 25, 75);

            for (int i = 0; i < StreamCount; i++)
            {
                int top = 50 + (i * 35);

                _chkUse[i] = new CheckBox();
                _chkUse[i].Left = 30;
                _chkUse[i].Top = top + 3;
                _chkUse[i].Width = 20;
                group.Controls.Add(_chkUse[i]);

                _lblNo[i] = new Label();
                _lblNo[i].Left = 75;
                _lblNo[i].Top = top + 6;
                _lblNo[i].Width = 30;
                group.Controls.Add(_lblNo[i]);

                _txtDisplayName[i] = new TextBox();
                _txtDisplayName[i].Left = 120;
                _txtDisplayName[i].Top = top;
                _txtDisplayName[i].Width = 145;
                group.Controls.Add(_txtDisplayName[i]);

                _txtOnvifPort[i] = new TextBox();
                _txtOnvifPort[i].Left = 285;
                _txtOnvifPort[i].Top = top;
                _txtOnvifPort[i].Width = 85;
                group.Controls.Add(_txtOnvifPort[i]);

                _cboFps[i] = new ComboBox();
                _cboFps[i].Left = 390;
                _cboFps[i].Top = top;
                _cboFps[i].Width = 70;
                _cboFps[i].DropDownStyle = ComboBoxStyle.DropDownList;
                _cboFps[i].Items.AddRange(new object[] { "5", "10", "15", "30" });
                group.Controls.Add(_cboFps[i]);

                _cboBitrate[i] = new ComboBox();
                _cboBitrate[i].Left = 475;
                _cboBitrate[i].Top = top;
                _cboBitrate[i].Width = 95;
                _cboBitrate[i].DropDownStyle = ComboBoxStyle.DropDownList;
                _cboBitrate[i].Items.AddRange(new object[] { "800k", "1200k", "1500k", "2M" });
                group.Controls.Add(_cboBitrate[i]);

                _cboCodec[i] = new ComboBox();
                _cboCodec[i].Left = 590;
                _cboCodec[i].Top = top;
                _cboCodec[i].Width = 75;
                _cboCodec[i].DropDownStyle = ComboBoxStyle.DropDownList;
                _cboCodec[i].Items.AddRange(new object[] { "H264", "H265" });
                group.Controls.Add(_cboCodec[i]);
            }
        }

        private void CreateOnvifGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "ONVIF 보안";
            group.Left = 15;
            group.Top = 155;
            group.Width = 330;
            group.Height = 95;
            Controls.Add(group);

            Label lblId = new Label();
            lblId.Text = "ID:";
            lblId.Left = 25;
            lblId.Top = 30;
            lblId.Width = 35;
            group.Controls.Add(lblId);

            _txtOnvifUserId = new TextBox();
            _txtOnvifUserId.Left = 60;
            _txtOnvifUserId.Top = 27;
            _txtOnvifUserId.Width = 230;
            group.Controls.Add(_txtOnvifUserId);

            Label lblPw = new Label();
            lblPw.Text = "PW:";
            lblPw.Left = 25;
            lblPw.Top = 60;
            lblPw.Width = 35;
            group.Controls.Add(lblPw);

            _txtOnvifPassword = new TextBox();
            _txtOnvifPassword.Left = 60;
            _txtOnvifPassword.Top = 57;
            _txtOnvifPassword.Width = 230;
            _txtOnvifPassword.PasswordChar = '*';
            group.Controls.Add(_txtOnvifPassword);
        }

        private void CreateAuthGroup()
        {
            GroupBox group = new GroupBox();
            group.Text = "인증";
            group.Left = 360;
            group.Top = 155;
            group.Width = 365;
            group.Height = 95;
            Controls.Add(group);

            Label lblKey = new Label();
            lblKey.Text = "인증키:";
            lblKey.Left = 20;
            lblKey.Top = 30;
            lblKey.Width = 60;
            group.Controls.Add(lblKey);

            _txtLicenseKey = new TextBox();
            _txtLicenseKey.Left = 80;
            _txtLicenseKey.Top = 27;
            _txtLicenseKey.Width = 200;
            group.Controls.Add(_txtLicenseKey);

            Label lblDevice = new Label();
            lblDevice.Text = "장비명:";
            lblDevice.Left = 20;
            lblDevice.Top = 60;
            lblDevice.Width = 60;
            group.Controls.Add(lblDevice);

            _txtDeviceName = new TextBox();
            _txtDeviceName.Left = 80;
            _txtDeviceName.Top = 57;
            _txtDeviceName.Width = 200;
            group.Controls.Add(_txtDeviceName);

            _btnAuthAction = new Button();
            _btnAuthAction.Text = "등록";
            _btnAuthAction.Left = 290;
            _btnAuthAction.Top = 27;
            _btnAuthAction.Width = 55;
            _btnAuthAction.Height = 55;
            _btnAuthAction.Click += delegate
            {
                EventHandler handler = AuthActionRequested;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            };
            group.Controls.Add(_btnAuthAction);
        }

        private void CreateOperationGroup()
        {
            _chkEnableDetailLog = new CheckBox();
            _chkEnableDetailLog.Text = "상세로그 기록";
            _chkEnableDetailLog.Left = 25;
            _chkEnableDetailLog.Top = 270;
            _chkEnableDetailLog.Width = 120;
            Controls.Add(_chkEnableDetailLog);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "자동실행";
            _chkAutoStart.Left = 160;
            _chkAutoStart.Top = 270;
            _chkAutoStart.Width = 90;
            Controls.Add(_chkAutoStart);

            _chkPreventSleep = new CheckBox();
            _chkPreventSleep.Text = "절전모드 해제";
            _chkPreventSleep.Left = 260;
            _chkPreventSleep.Top = 270;
            _chkPreventSleep.Width = 125;
            Controls.Add(_chkPreventSleep);

            _chkAutoStartStreaming = new CheckBox();
            _chkAutoStartStreaming.Text = "실행 시 자동 송출";
            _chkAutoStartStreaming.Left = 400;
            _chkAutoStartStreaming.Top = 270;
            _chkAutoStartStreaming.Width = 140;
            Controls.Add(_chkAutoStartStreaming);
        }

        private void CreateButtons()
        {
            _btnApply = new Button();
            _btnApply.Text = "적용";
            _btnApply.Left = 470;
            _btnApply.Top = 310;
            _btnApply.Width = 80;
            _btnApply.Click += delegate
            {
                EventHandler handler = ApplyRequested;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            };
            Controls.Add(_btnApply);

            _btnOk = new Button();
            _btnOk.Text = "확인";
            _btnOk.Left = 560;
            _btnOk.Top = 310;
            _btnOk.Width = 80;
            _btnOk.Click += delegate
            {
                EventHandler handler = OkRequested;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            };
            Controls.Add(_btnOk);

            _btnCancel = new Button();
            _btnCancel.Text = "취소";
            _btnCancel.Left = 650;
            _btnCancel.Top = 310;
            _btnCancel.Width = 80;
            _btnCancel.Click += delegate
            {
                EventHandler handler = CancelRequested;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            };
            Controls.Add(_btnCancel);
        }

        private void AddHeader(Control parent, string text, int left, int top, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Left = left;
            label.Top = top;
            label.Width = width;
            label.Font = new Font(Font, FontStyle.Bold);
            parent.Controls.Add(label);
        }

        private StreamConfig FindStream(List<StreamConfig> streams, int streamNo)
        {
            foreach (StreamConfig stream in streams)
            {
                if (stream != null && stream.StreamNo == streamNo)
                    return stream;
            }

            return null;
        }

        private string GetMonitorText(string monitorRole)
        {
            if (string.Equals(monitorRole, "Primary", StringComparison.OrdinalIgnoreCase))
                return "주 모니터";

            if (string.Equals(monitorRole, "Secondary", StringComparison.OrdinalIgnoreCase))
                return "보조 모니터";

            return "모니터";
        }

        private int ToInt(string value, int defaultValue)
        {
            int result;

            if (int.TryParse(value, out result))
                return result;

            return defaultValue;
        }

        private void SelectComboValue(ComboBox comboBox, string value)
        {
            if (comboBox == null)
                return;

            if (value == null)
                value = "";

            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (string.Equals(Convert.ToString(comboBox.Items[i]), value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            if (comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// StreamNo 기준 기본 RTSP 경로를 반환한다.
        /// 
        /// Stream0 → poscam
        /// Stream1 → poscam_1
        /// Stream2 → poscam_2
        /// </summary>
        private string GetDefaultRtspPath(int streamNo)
        {
            if (streamNo <= 0)
                return "poscam";

            return "poscam_" + streamNo;
        }

        /// <summary>
        /// StreamQualityConfig 값을 복사한다.
        /// 
        /// 원본이 없으면 기본값을 복사한다.
        /// View에서 표시하지 않는 SubStream 설정을 보존하기 위해 사용한다.
        /// </summary>
        private StreamQualityConfig CloneStreamQuality(
            StreamQualityConfig source,
            StreamQualityConfig defaultValue)
        {
            StreamQualityConfig baseValue = source ?? defaultValue ?? new StreamQualityConfig();

            return new StreamQualityConfig
            {
                IsEnabled = baseValue.IsEnabled,
                RtspPath = baseValue.RtspPath,
                Fps = baseValue.Fps,
                Bitrate = baseValue.Bitrate,
                Width = baseValue.Width,
                Height = baseValue.Height
            };
        }

        /// <summary>
        /// Stream 번호 기준 기본 표시명을 반환한다.
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
        /// Stream 순번에 해당하는 모니터가 현재 PC에 존재하는지 확인한다.
        /// </summary>
        private bool HasScreenForStream(int streamNo)
        {
            return streamNo >= 0 && streamNo < Screen.AllScreens.Length;
        }

        /// <summary>
        /// Stream 설정에 사용할 Windows 모니터 장치명을 반환한다.
        /// 
        /// 기존 ScreenName이 현재 PC에 존재하면 그대로 사용하고,
        /// 없거나 비어 있으면 현재 모니터 목록 기준으로 자동 지정한다.
        /// </summary>
        private string ResolveScreenName(StreamConfig source, int streamNo)
        {
            if (source != null &&
                !string.IsNullOrWhiteSpace(source.ScreenName) &&
                ExistsScreenName(source.ScreenName))
            {
                return source.ScreenName;
            }

            Screen screen = GetScreenByStreamNo(streamNo);

            if (screen == null)
                return "";

            return screen.DeviceName;
        }

        /// <summary>
        /// 지정한 ScreenName이 현재 PC 모니터 목록에 존재하는지 확인한다.
        /// </summary>
        private bool ExistsScreenName(string screenName)
        {
            if (string.IsNullOrWhiteSpace(screenName))
                return false;

            foreach (Screen screen in Screen.AllScreens)
            {
                if (string.Equals(
                    screen.DeviceName,
                    screenName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// StreamNo 기준으로 현재 PC 모니터를 반환한다.
        /// 
        /// 현재는 Screen.AllScreens 순서를 사용한다.
        /// </summary>
        private Screen GetScreenByStreamNo(int streamNo)
        {
            if (streamNo < 0)
                return null;

            if (streamNo >= Screen.AllScreens.Length)
                return null;

            return Screen.AllScreens[streamNo];
        }
    }
}