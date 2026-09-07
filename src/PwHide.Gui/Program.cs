using PwHide.Core;

namespace PwHide.Gui;

internal static class Program
{
    /// <summary>GUI 入口：解锁 vault 后进入主界面。home 可经命令行 --home &lt;dir&gt; 指定（取首个）。</summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var home = Vault.DefaultHome();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--home") { home = args[i + 1]; break; }

        Loc.Load(home);

        if (!Vault.Exists(home))
        {
            MessageBox.Show(Loc.T(
                $"No vault found at {home}.\nRun `pwhide init` in a terminal first, then reopen this app.",
                $"未找到 vault（{home}）。\n请先在终端运行 pwhide init，再打开本程序。"),
                "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 主口令在 GUI 会话内存活（威胁模型已声明：托管 string 无法清除，暴露面大于 CLI byte[] 路径）。
        // 每次写事务在持锁下重新 Open+Unlock，避免常驻陈旧快照覆盖并发写入。
        string? passphrase = null;
        while (passphrase is null)
        {
            using var unlock = new UnlockForm(home);
            if (unlock.ShowDialog() != DialogResult.OK) return;
            try
            {
                using var probe = Vault.Open(home);
                probe.Unlock(unlock.Passphrase);
                passphrase = unlock.Passphrase;
            }
            catch (Exception ex)
            {
                // 与 CLI 兜底同口径：单行消息；异常消息均为固定文案+路径/条目名，不含口令与密文
                MessageBox.Show(ex.Message.Split('\n')[0], "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        Application.Run(new MainForm(home, passphrase));
    }
}
