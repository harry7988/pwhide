using PwHide.Core;

namespace PwHide.Gui;

/// <summary>主界面：条目列表（列出）+ 新建/删除 + 占位符查看。
/// 写事务在 Vault.FileLock 持锁下用新开的 Vault 实例完成（避免常驻陈旧快照覆盖 CLI 的并发写入，
/// 与 CLI"先取锁再读再写"的互斥语义一致）；读取仅元数据，不需要口令。</summary>
internal sealed class MainForm : Form
{
    private readonly string _home;
    private readonly string _passphrase;   // GUI 会话内存活（威胁模型声明的 GUI 暴露面）
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    private readonly ToolStrip _toolbar = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new();

    public MainForm(string home, string passphrase)
    {
        _home = home;
        _passphrase = passphrase;
        Text = "pwhide";
        Width = 900;
        Height = 520;
        Font = new Font("Segoe UI", 10f);

        _list.Columns.Add(L("name", "名称"), 160);
        _list.Columns.Add(L("type", "类型"), 90);
        _list.Columns.Add(L("user", "账号"), 130);
        _list.Columns.Add(L("tenant", "租户"), 90);
        _list.Columns.Add(L("fields (plain/encrypted)", "字段（明文/加密）"), 320);

        _toolbar.Items.Add(L("New entry…", "新建条目…"), null, (_, _) => CreateEntry());
        _toolbar.Items.Add(L("Delete", "删除"), null, (_, _) => DeleteSelected());
        _toolbar.Items.Add(L("Placeholders", "占位符"), null, (_, _) => ShowPlaceholders());
        _toolbar.Items.Add(L("Refresh", "刷新"), null, (_, _) => Reload());
        _toolbar.Items.Add(new ToolStripSeparator());
        var langItem = new ToolStripButton(L("中文", "English"));
        langItem.Click += (_, _) =>
        {
            Loc.Save(_home, Loc.Lang == "zh" ? "en" : "zh");
            MessageBox.Show(L("Language changed. Reopen the window to apply.",
                              "语言已切换。重新打开窗口后生效。"), "pwhide");
        };
        _toolbar.Items.Add(langItem);

        _status.Items.Add(_statusText);

        Controls.Add(_list);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        Reload();
    }

    private static string L(string en, string zh) => Loc.T(en, zh);

    /// <summary>刷新列表：重新从磁盘读（列表只需元数据，无需解锁；同时能看到 CLI 的最新写入）。</summary>
    private void Reload()
    {
        _list.Items.Clear();
        using var fresh = Vault.Open(_home);
        foreach (var e in fresh.Data.Entries)
        {
            var plain = string.Join(", ", e.PlainFields.Select(kv => $"{kv.Key}={kv.Value}"));
            var enc = string.Join(", ", e.Fields.Select(f => f.Name));
            var fields = plain.Length > 0 && enc.Length > 0 ? plain + " / " + enc : plain + enc;
            _list.Items.Add(new ListViewItem(e.Name)
            {
                SubItems = { e.Type ?? "-", e.Username ?? "-", e.Tenant ?? "-", fields },
            });
        }
        _statusText.Text = L($"{_home}  ·  {fresh.Data.Entries.Count} entries",
                             $"{_home}  ·  {fresh.Data.Entries.Count} 个条目");
    }

    private void CreateEntry()
    {
        using var form = new CreateEntryForm();
        if (form.ShowDialog() != DialogResult.OK) return;
        try
        {
            // 写事务：持锁 + 新开实例（读到锁后最新状态），失败只丢弃该实例、不影响后续
            using var _lock = Vault.FileLock.Acquire(_home);
            using var vault = Vault.Open(_home);
            vault.Unlock(_passphrase);
            var entry = vault.GetOrAdd(form.EntryName, form.EntryType, form.Username, form.Tenant);
            vault.SetPassword(entry, form.Password);
            foreach (var (fname, fval, plain) in form.Fields)
            {
                if (plain) vault.SetPlainField(entry, fname, fval);
                else vault.SetField(entry, fname, fval);
            }
            vault.Save();
            // 加密字段值的弱值警告（与 CLI 同口径，非阻断）：锁内只收集，锁释放后再弹，
            // 避免模态弹窗期间持有 FileLock 阻塞并发 CLI 写
            var weakFieldWarnings = new List<(string Name, string Reason)>();
            foreach (var (fname, fval, plain) in form.Fields)
                if (!plain && WeakSecret.Check(fval) is { } reason)
                    weakFieldWarnings.Add((fname, reason));
            Reload();
            foreach (var (wname, wreason) in weakFieldWarnings)
                MessageBox.Show(
                    Loc.T($"Warning: encrypted field {wname}: {Loc.Tr(wreason)} (may collide with normal output when injected)", $"警告：加密字段 {wname}：{wreason}（作为密文注入时可能与正常输出碰撞）"),
                    "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Tr(ex.Message.Split('\n')[0]), "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var name = _list.SelectedItems[0].Text;
        // 破坏性确认：默认按钮在 No（回车不会误删）。CLI delete 无确认直接执行；GUI 主动加确认是更安全的加强
        if (MessageBox.Show(
                L($"Delete entry \"{name}\"?", $"删除条目 {name}？"),
                "pwhide", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            using var _lock = Vault.FileLock.Acquire(_home);
            using var vault = Vault.Open(_home);
            vault.Unlock(_passphrase);
            vault.Delete(name);
            vault.Save();
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Tr(ex.Message.Split('\n')[0]), "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowPlaceholders()
    {
        if (_list.SelectedItems.Count == 0) return;
        using var fresh = Vault.Open(_home);
        var e = fresh.Find(_list.SelectedItems[0].Text);
        if (e is null) return;
        var lines = new List<string>();
        if (e.Ct.Length > 0) lines.Add(Vault.Token(e.Name, null));
        if (e.Username is not null) lines.Add(Vault.Token(e.Name, "user"));
        if (e.Tenant is not null) lines.Add(Vault.Token(e.Name, "tenant"));
        foreach (var f in e.Fields) lines.Add(Vault.Token(e.Name, f.Name));
        foreach (var kv in e.PlainFields) lines.Add(Vault.Token(e.Name, kv.Key));
        MessageBox.Show(string.Join("\n", lines), L("Available placeholders", "可用占位符"));
    }

    /// <summary>窗体销毁时释放资源（WinForms 关闭走 Dispose(bool)，非接口映射）。</summary>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
