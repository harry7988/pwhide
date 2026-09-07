using PwHide.Core;

namespace PwHide.Gui;

/// <summary>解锁对话框：主口令（密码遮蔽输入）。验证通过后 Passphrase 交给主界面用于写事务解锁。</summary>
internal sealed class UnlockForm : Form
{
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true };
    private readonly Button _ok = new() { DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { DialogResult = DialogResult.Cancel };

    public string Passphrase { get; private set; } = "";

    public UnlockForm(string home)
    {
        Text = "pwhide";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(370, 130);

        _ok.Text = Loc.T("Unlock", "解锁");
        _cancel.Text = Loc.T("Cancel", "取消");

        var label = new Label
        {
            Text = Loc.T($"Master passphrase\n{home}", $"主口令\n{home}"),
        };
        label.SetBounds(20, 14, 340, 44);
        _pass.SetBounds(20, 58, 330, 30);
        _ok.SetBounds(168, 96, 88, 28);
        _cancel.SetBounds(262, 96, 88, 28);

        Controls.Add(label);
        Controls.Add(_pass);
        Controls.Add(_ok);
        Controls.Add(_cancel);
        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Passphrase = _pass.Text;
            if (Passphrase.Length == 0) e.Cancel = true;
        }
        base.OnFormClosing(e);
    }
}
