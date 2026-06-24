using System;
using System.Drawing;
using System.Windows.Forms;

namespace PcCam_x64.Views
{
    /// <summary>
    /// 설정/종료 접근을 제한하기 위한 간단한 비밀번호 입력 창.
    /// 
    /// 입력값 검증은 Presenter에서 수행한다.
    /// </summary>
    public class PasswordPromptForm : Form
    {
        private readonly Label _messageLabel;
        private readonly TextBox _passwordTextBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public string Password
        {
            get { return _passwordTextBox.Text; }
        }

        public PasswordPromptForm(string title, string message)
        {
            Text = string.IsNullOrWhiteSpace(title) ? "확인" : title;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 360;
            Height = 170;

            _messageLabel = new Label();
            _messageLabel.Left = 20;
            _messageLabel.Top = 20;
            _messageLabel.Width = 300;
            _messageLabel.Height = 35;
            _messageLabel.Text = message;

            _passwordTextBox = new TextBox();
            _passwordTextBox.Left = 20;
            _passwordTextBox.Top = 60;
            _passwordTextBox.Width = 300;
            _passwordTextBox.PasswordChar = '*';

            _okButton = new Button();
            _okButton.Text = "확인";
            _okButton.Left = 160;
            _okButton.Top = 95;
            _okButton.Width = 75;
            _okButton.DialogResult = DialogResult.OK;

            _cancelButton = new Button();
            _cancelButton.Text = "취소";
            _cancelButton.Left = 245;
            _cancelButton.Top = 95;
            _cancelButton.Width = 75;
            _cancelButton.DialogResult = DialogResult.Cancel;

            Controls.Add(_messageLabel);
            Controls.Add(_passwordTextBox);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Load += delegate
            {
                _passwordTextBox.Focus();
            };
        }
    }
}