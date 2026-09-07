using PwHide.Core;

namespace PwHide.Gui;

/// <summary>主口令解锁对话框。解锁成功后 Passphrase 供 MainForm 继续解锁 vault。</summary>
internal sealed class UnlockForm : Form
{
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true, Width = 320 };
    private readonly Button _ok = new() { Text = "", DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { Text = "", DialogResult = DialogResult.Cancel };

    public string Passphrase { get; private set; } = "";

    public UnlockForm(string home)
    {
        Text = "pwhide";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        var label = new Label
        {
            Text = Loc.T($"Master passphrase\n{home}", $"主口令\n{home}"),
            AutoSize = true,
            Margin = new Padding(12),
        };
        _ok.Location = new Point(180, 96);
        _cancel.Location = new Point(266, 96);

        Controls.Add(label);
        Controls.Add(_pass);
        Controls.Add(_ok);
        Controls.Add(_cancel);
        _pass.Location = new Point(20, 60);
        _pass.SetBounds(20, 56, 320, _pass.Height);
        label.SetBounds(20, 16, 340, 60);
        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Passphrase = _pass.Text;
            if (Passphrase.Length == 0)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnFormClosing(e);
    }
}
