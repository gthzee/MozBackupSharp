using System;
using System.Drawing;
using System.Windows.Forms;

namespace MozBackupSharp.Ui
{
    public sealed class PasswordForm : Form
    {
        private readonly TextBox _passwordTextBox;
        private readonly TextBox _confirmTextBox;
        private readonly bool _confirmPassword;

        public PasswordForm(string title, string message, bool confirmPassword)
        {
            _confirmPassword = confirmPassword;

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F);
            ClientSize = confirmPassword ? new Size(430, 178) : new Size(430, 138);

            var messageLabel = new Label
            {
                Text = message,
                AutoSize = false,
                Left = 12,
                Top = 12,
                Width = ClientSize.Width - 24,
                Height = 36
            };
            Controls.Add(messageLabel);

            var passwordLabel = new Label
            {
                Text = "Password:",
                Left = 12,
                Top = 56,
                Width = 110,
                AutoSize = true
            };
            Controls.Add(passwordLabel);

            _passwordTextBox = new TextBox
            {
                Left = 126,
                Top = 52,
                Width = ClientSize.Width - 138,
                UseSystemPasswordChar = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            Controls.Add(_passwordTextBox);

            int buttonTop;
            if (confirmPassword)
            {
                var confirmLabel = new Label
                {
                    Text = "Confirm password:",
                    Left = 12,
                    Top = 88,
                    Width = 110,
                    AutoSize = true
                };
                Controls.Add(confirmLabel);

                _confirmTextBox = new TextBox
                {
                    Left = 126,
                    Top = 84,
                    Width = ClientSize.Width - 138,
                    UseSystemPasswordChar = true,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
                };
                Controls.Add(_confirmTextBox);
                buttonTop = 128;
            }
            else
            {
                buttonTop = 88;
            }

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Left = ClientSize.Width - 174,
                Top = buttonTop,
                Width = 75,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            okButton.Click += OkButtonClick;
            Controls.Add(okButton);

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Left = ClientSize.Width - 93,
                Top = buttonTop,
                Width = 75,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        public string Password
        {
            get { return _passwordTextBox.Text; }
        }

        public static PasswordForm ForBackup()
        {
            return new PasswordForm(
                "Create backup password",
                "Enter a password to protect this backup file.",
                true);
        }

        public static PasswordForm ForRestore()
        {
            return new PasswordForm(
                "Backup password required",
                "This backup is password protected. Enter the password to restore it.",
                false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _passwordTextBox.Focus();
        }

        private void OkButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_passwordTextBox.Text) || (_confirmPassword && _passwordTextBox.Text.Length < 3))
            {
                MessageBox.Show(this, _confirmPassword ? "Password should be at least three characters long." : "Password cannot be empty.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            if (_confirmPassword && _confirmTextBox != null && _passwordTextBox.Text != _confirmTextBox.Text)
            {
                MessageBox.Show(this, "The passwords do not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }
        }
    }
}
