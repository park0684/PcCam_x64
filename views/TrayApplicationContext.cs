using System;
using System.Drawing;
using System.Windows.Forms;
using PcCam_x64.Infrastructure;

namespace PcCam_x64.Views
{
    /// <summary>
    /// 시스템 트레이 기반 ApplicationContext.
    /// 
    /// 메뉴 구성:
    /// 1. 인증상태
    /// 2. 설정
    /// 3. 종료
    /// </summary>
    public class TrayApplicationContext : ApplicationContext, ITrayView
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;

        private readonly ToolStripMenuItem _authStatusMenuItem;
        private readonly ToolStripMenuItem _settingMenuItem;
        private readonly ToolStripMenuItem _exitMenuItem;
        private readonly ToolStripMenuItem _versionMenuItem;
        private readonly string _displayVersion;

        private readonly Form _invokerForm;

        public event EventHandler SettingRequested;
        public event EventHandler ExitRequested;

        public TrayApplicationContext()
        {
            _displayVersion = AppVersionProvider.DisplayVersion;

            _invokerForm = new Form();
            _invokerForm.ShowInTaskbar = false;
            _invokerForm.WindowState = FormWindowState.Minimized;
            _invokerForm.Load += delegate
            {
                _invokerForm.Visible = false;
            };

            _authStatusMenuItem =
                new ToolStripMenuItem("인증상태: 확인 중");

            _authStatusMenuItem.Enabled = false;

            /*
             * 프로그램 버전은 확인용 정보이므로
             * 클릭할 수 없는 메뉴로 표시한다.
             */
            _versionMenuItem =
                new ToolStripMenuItem(
                    "프로그램 버전: " + _displayVersion);

            _versionMenuItem.Enabled = false;

            _settingMenuItem = new ToolStripMenuItem("설정");
            _exitMenuItem = new ToolStripMenuItem("종료");

            _contextMenu = new ContextMenuStrip();

            _contextMenu.Items.Add(_authStatusMenuItem);
            _contextMenu.Items.Add(_versionMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_settingMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_exitMenuItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = Properties.Resources.PCCAM;

            /*
             * 최초 생성 시점에도 버전이 포함된 툴팁을 표시한다.
             */
            _notifyIcon.Text =
                BuildNotifyIconText("PC CAM");

            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = _contextMenu;

            _settingMenuItem.Click += delegate
            {
                RaiseEvent(SettingRequested);
            };

            _exitMenuItem.Click += delegate
            {
                RaiseEvent(ExitRequested);
            };

            _notifyIcon.DoubleClick += delegate
            {
                RaiseEvent(SettingRequested);
            };
        }

        private void RaiseEvent(EventHandler handler)
        {
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void SetAuthStatusText(string text)
        {
            RunOnUiThread(delegate
            {
                if (string.IsNullOrWhiteSpace(text))
                    text = "확인 중";

                _authStatusMenuItem.Text = "인증상태: " + text;
            });
        }

        /// <summary>
        /// 트레이 아이콘의 현재 상태 툴팁을 변경한다.
        ///
        /// 프로그램 상태가 변경되더라도 버전 정보는 항상 유지한다.
        /// </summary>
        public void SetStatusText(string text)
        {
            RunOnUiThread(delegate
            {
                string safeText =
                    string.IsNullOrWhiteSpace(text)
                        ? "PC CAM"
                        : text;

                _notifyIcon.Text =
                    BuildNotifyIconText(safeText);
            });
        }

        public void ShowInfo(string message)
        {
            RunOnUiThread(delegate
            {
                MessageBox.Show(
                    message,
                    "PC CAM",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        public void ShowError(string message)
        {
            RunOnUiThread(delegate
            {
                MessageBox.Show(
                    message,
                    "PC CAM 오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            });
        }

        public string PromptPassword(string title, string message)
        {
            string result = null;

            RunOnUiThread(delegate
            {
                using (PasswordPromptForm form = new PasswordPromptForm(title, message))
                {
                    if (form.ShowDialog() == DialogResult.OK)
                        result = form.Password;
                }
            });

            return result;
        }

        public void CloseView()
        {
            RunOnUiThread(delegate
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();

                if (_contextMenu != null)
                    _contextMenu.Dispose();

                if (_invokerForm != null)
                    _invokerForm.Dispose();

                ExitThread();
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (action == null)
                return;

            if (_invokerForm.IsHandleCreated && _invokerForm.InvokeRequired)
            {
                try
                {
                    _invokerForm.Invoke(action);
                }
                catch
                {
                }
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// 트레이 아이콘 툴팁 문자열을 생성한다.
        ///
        /// NotifyIcon.Text에는 길이 제한이 있으므로
        /// 상태 문자열을 먼저 줄이고 버전 정보는 항상 유지한다.
        /// </summary>
        private string BuildNotifyIconText(string statusText)
        {
            string safeStatus =
                string.IsNullOrWhiteSpace(statusText)
                    ? "PC CAM"
                    : statusText.Trim();

            string suffix = " | " + _displayVersion;

            /*
             * NotifyIcon.Text의 Windows 호환성을 위해
             * 전체 길이를 63자 이내로 제한한다.
             */
            const int maxLength = 63;

            int statusMaxLength =
                maxLength - suffix.Length;

            if (statusMaxLength < 1)
                return _displayVersion;

            if (safeStatus.Length > statusMaxLength)
            {
                safeStatus =
                    safeStatus.Substring(
                        0,
                        statusMaxLength);
            }

            return safeStatus + suffix;
        }
    }
}