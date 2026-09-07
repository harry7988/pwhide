using PwHide.Core;

namespace PwHide.Gui;

/// <summary>新建条目表单：基本信息 + 自定义字段（逐字段选择加密/明文，与 CLI 语义一致）。</summary>
internal sealed class CreateEntryForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _type = new();
    private readonly TextBox _user = new();
    private readonly TextBox _tenant = new();
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true };
    private readonly TextBox _pass2 = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _forceWeak = new() { Text = "", AutoSize = true };
    private readonly DataGridView _grid = new();
    private readonly Button _ok = new() { Text = "", DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { Text = "", DialogResult = DialogResult.Cancel };

    public string EntryName => _name.Text.Trim();
    public string? EntryType => NullIfEmpty(_type.Text.Trim());
    public string? Username => NullIfEmpty(_user.Text.Trim());
    public string? Tenant => NullIfEmpty(_tenant.Text.Trim());
    public string Password => _pass.Text;
    public bool ForceWeak => _forceWeak.Checked;

    public readonly List<(string Name, string Value, bool Plain)> Fields = [];

    public CreateEntryForm()
    {
        Text = Loc.T("pwhide - new entry", "pwhide - 新建条目");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);
        Width = 560;
        Height = 480;

        var table = new TableLayoutPanel { Dock = DockStyle.Top, Height = 190, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, L("name", "名称"), _name);
        AddRow(table, L("type", "类型"), _type);
        AddRow(table, L("user", "账号"), _user);
        AddRow(table, L("tenant", "租户"), _tenant);
        AddRow(table, L("password", "密码"), _pass);
        AddRow(table, L("confirm", "再次确认"), _pass2);
        _forceWeak.Text = Loc.T("allow weak password (--force-weak)", "允许弱密码（--force-weak）");
        table.Controls.Add(_forceWeak, 1, 6);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add("f", L("field", "字段名"));
        _grid.Columns.Add("v", L("value", "值"));
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "enc",
            HeaderText = Loc.T("encrypted", "加密"),
            FillWeight = 60,
        });

        var fieldPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        fieldPanel.Controls.Add(_grid);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
        bottom.Controls.Add(_ok);
        bottom.Controls.Add(_cancel);

        Controls.Add(fieldPanel);
        Controls.Add(table);
        Controls.Add(bottom);
        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    private static string L(string en, string zh) => Loc.T(en, zh);

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, table.RowCount);
        table.Controls.Add(control, 1, table.RowCount);
        table.RowCount++;
    }

    /// <summary>OK 校验：收集字段、去重、Trim、弱密码警告。失败返回 false 并弹窗。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) { base.OnFormClosing(e); return; }

        var problems = new List<string>();
        if (EntryName.Length == 0) problems.Add(Loc.T("name is required", "名称不能为空"));
        if (Password.Length == 0) problems.Add(Loc.T("password cannot be empty", "密码不能为空"));

        Fields.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) break;
            var fname = Convert.ToString(row.Cells[0].Value)?.Trim() ?? "";
            var fval = Convert.ToString(row.Cells[1].Value)?.Trim() ?? "";
            var enc = Convert.ToBoolean(row.Cells[2].Value ?? true);
            if (fname.Length == 0 && fval.Length == 0) continue;
            if (fname.Length == 0) { problems.Add(Loc.T("a field has no name", "有一个字段没有填写字段名")); continue; }
            if (Fields.Any(f => f.Name == fname))
                problems.Add(Loc.T($"duplicate field: {fname}", $"字段重复：{fname}"));
            else
                Fields.Add((fname, fval, !enc));
        }

        if (problems.Count > 0)
        {
            e.Cancel = true;
            MessageBox.Show(string.Join("\n", problems), "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            base.OnFormClosing(e);
            return;
        }

        // 弱密码警告（与 CLI 同一口径）：确认后继续
        if (!ForceWeak && WeakSecret.Check(Password) is { } reason)
        {
            var proceed = MessageBox.Show(
                Loc.T($"Weak password: {reason}\n\nSave anyway?", $"弱密码：{reason}\n\n仍要保存吗？"),
                "pwhide", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (proceed != DialogResult.Yes) { e.Cancel = true; base.OnFormClosing(e); return; }
        }

        base.OnFormClosing(e);
    }
}
