using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace RemoteDebuggerVSX.UI
{
    internal sealed class RemoteEndpointDialog : Form
    {
        private const int DialogWidth = 420;
        private const int DialogHeight = 150;

        private const int LabelLeft = 12;
        private const int InputLeft = 110;
        private const int InputWidth = 290;

        private const int HostTop = 14;
        private const int PortTop = 50;

        private const int ButtonTop = 95;
        private const int OkLeft = 244;
        private const int CancelLeft = 325;
        private const int ButtonWidth = 75;

        private readonly TextBox _hostText;
        private readonly TextBox _portText;
        private readonly Button _ok;
        private readonly Button _cancel;

        public string Host
        {
            get { return _hostText.Text?.Trim(); }
        }

        public string PortText
        {
            get { return _portText.Text?.Trim(); }
        }

        public RemoteEndpointDialog(string defaultHost, int defaultPort)
        {
            Text = "Remote Debugger Endpoint";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(DialogWidth, DialogHeight);

            var hostLabel = new Label { Text = "IP / Host:", AutoSize = true, Left = LabelLeft, Top = 18 };
            _hostText = new TextBox { Left = InputLeft, Top = HostTop, Width = InputWidth, Text = defaultHost ?? string.Empty };

            var portLabel = new Label { Text = "Port:", AutoSize = true, Left = LabelLeft, Top = 54 };
            _portText = new TextBox { Left = InputLeft, Top = PortTop, Width = InputWidth, Text = defaultPort.ToString(CultureInfo.InvariantCulture) };

            _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = OkLeft, Width = ButtonWidth, Top = ButtonTop };
            _cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = CancelLeft, Width = ButtonWidth, Top = ButtonTop };

            AcceptButton = _ok;
            CancelButton = _cancel;

            Controls.Add(hostLabel);
            Controls.Add(_hostText);
            Controls.Add(portLabel);
            Controls.Add(_portText);
            Controls.Add(_ok);
            Controls.Add(_cancel);

            _ok.Click += OnOkClick;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            if (!ValidateInputs(out var error))
            {
                MessageBox.Show(this, error, "Invalid endpoint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
        }

        private bool ValidateInputs(out string error)
        {
            error = null;

            var host = Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                error = "Host cannot be empty.";
                return false;
            }

            if (!int.TryParse(PortText, out var port) || port <= 0 || port > 65535)
            {
                error = "Port must be an integer between 1 and 65535.";
                return false;
            }

            return true;
        }
    }
}
