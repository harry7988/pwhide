using PwHide.Core;

namespace PwHide.Gui;

internal static class Program
{
    /// <summary>GUI 入口：解锁 vault 后进入主界面。home 可经命令行 --home &lt;dir&gt; 指定。</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var home = Vault.DefaultHome();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--home") home = args[i + 1];

        Loc.Load(home);

        if (!Vault.Exists(home))
        {
            MessageBox.Show(Loc.T(
                $"No vault found at {home}.\nRun `pwhide init` in a terminal first, then reopen this app.",
                $"未找到 vault（{home}）。\n请先在终端运行 pwhide init，再打开本程序。"),
                "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Vault? vault = null;
        while (vault is null)
        {
            using var unlock = new UnlockForm(home);
            if (unlock.ShowDialog() != DialogResult.OK) return;
            try
            {
                vault = Vault.Open(home);
                vault.Unlock(unlock.Passphrase);
            }
            catch (VaultException ex)
            {
                MessageBox.Show(ex.Message, "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        Application.Run(new MainForm(vault, home));
    }
}
