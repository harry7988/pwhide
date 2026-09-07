using PwHide.Core;

namespace PwHide.Gui;

/// <summary>主界面：条目列表（列出）+ 新建/删除 + 占位符查看。所有写操作经 PwHide.Core 与 CLI 完全同源。</summary>
internal sealed class MainForm : Form, IDisposable
{
    private readonly Vault _vault;
    private readonly string _home;
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    private readonly ToolStrip _toolbar = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusText = new();

    public MainForm(Vault vault, string home)
    {
        _vault = vault;
        _home = home;
        Text = "pwhide";
        Width = 900;
        Height = 520;
        Font = new Font("Segoe UI", 10f);

        _list.Columns.Add(Loc.T("name", "名称"), 160);
        _list.Columns.Add(Loc.T("type", "类型"), 90);
        _list.Columns.Add(Loc.T("user", "账号"), 130);
        _list.Columns.Add(Loc.T("tenant", "租户"), 90);
        _list.Columns.Add(Loc.T("fields (plain/encrypted)", "字段（明文/加密）"), 320);

        _toolbar.Items.Add(Loc.T("New entry…", "新建条目…"), null, (_, _) => CreateEntry());
        _toolbar.Items.Add(Loc.T("Delete", "删除"), null, (_, _) => DeleteSelected());
        _toolbar.Items.Add(Loc.T("Placeholders", "占位符"), null, (_, _) => ShowPlaceholders());
        _toolbar.Items.Add(Loc.T("Refresh", "刷新"), null, (_, _) => Reload());
        _toolbar.Items.Add(new ToolStripSeparator());
        var langItem = new ToolStripButton(Loc.T("中文", "English"));
        langItem.Click += (_, _) =>
        {
            Loc.Save(_home, Loc.Lang == "zh" ? "en" : "zh");
            MessageBox.Show(Loc.T("Language changed. Reopen the window to apply.",
                                  "语言已切换。重新打开窗口后生效。"), "pwhide");
        };
        _toolbar.Items.Add(langItem);

        _status.Items.Add(_statusText);

        Controls.Add(_list);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        Reload();
        Dispose(false);
    }

    private void Reload()
    {
        _list.Items.Clear();
        foreach (var e in _vault.Data.Entries)
        {
            var plain = string.Join(", ", e.PlainFields.Select(kv => $"{kv.Key}={kv.Value}"));
            var enc = string.Join(", ", e.Fields.Select(f => f.Name));
            var fields = plain.Length > 0 && enc.Length > 0 ? plain + " / " + enc : plain + enc;
            var item = new ListViewItem(e.Name) { SubItems = { e.Type ?? "-", e.Username ?? "-", e.Tenant ?? "-", fields } };
            _list.Items.Add(item);
        }
        _statusText.Text = Loc.T($"{_home}  ·  {_vault.Data.Entries.Count} entries",
                                 $"{_home}  ·  {_vault.Data.Entries.Count} 个条目");
    }

    private void CreateEntry()
    {
        using var form = new CreateEntryForm();
        if (form.ShowDialog() != DialogResult.OK) return;
        try
        {
            var entry = _vault.GetOrAdd(form.EntryName, form.EntryType, form.Username, form.Tenant);
            _vault.SetPassword(entry, form.Password);
            foreach (var (fname, fval, plain) in form.Fields)
            {
                if (plain) _vault.SetPlainField(entry, fname, fval);
                else _vault.SetField(entry, fname, fval);
            }
            _vault.Save();
            Reload();
        }
        catch (Exception ex) when (ex is VaultException or UsageException)
        {
            MessageBox.Show(ex.Message, "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var name = _list.SelectedItems[0].Text;
        if (MessageBox.Show(
                Loc.T($"Delete entry \"{name}\"?", $"删除条目 {name}？"),
                "pwhide", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            _vault.Delete(name);
            _vault.Save();
            Reload();
        }
        catch (Exception ex) when (ex is VaultException or UsageException)
        {
            MessageBox.Show(ex.Message, "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowPlaceholders()
    {
        if (_list.SelectedItems.Count == 0) return;
        var e = _vault.Find(_list.SelectedItems[0].Text);
        if (e is null) return;
        var lines = new List<string>();
        if (e.Ct.Length > 0) lines.Add(Vault.Token(e.Name, null));
        if (e.Username is not null) lines.Add(Vault.Token(e.Name, "user"));
        if (e.Tenant is not null) lines.Add(Vault.Token(e.Name, "tenant"));
        foreach (var f in e.Fields) lines.Add(Vault.Token(e.Name, f.Name));
        foreach (var kv in e.PlainFields) lines.Add(Vault.Token(e.Name, kv.Key));
        MessageBox.Show(string.Join("\n", lines), Loc.T("Available placeholders", "可用占位符"));
    }

    void IDisposable.Dispose()
    {
        _vault.Dispose();
        base.Dispose(true);
    }
}
