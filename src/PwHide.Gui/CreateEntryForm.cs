using PwHide.Core;

namespace PwHide.Gui;

/// <summary>新建条目表单：基本信息 + 自定义字段（逐字段选择加密/明文，与 CLI 语义一致）。
/// 密码 Trim、二次确认比对、弱密码警告（默认 No）、字段去重与空值校验——全部对齐 CLI 的 set 行为。</summary>
internal sealed class CreateEntryForm : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _type = new();
    private readonly TextBox _user = new();
    private readonly TextBox _tenant = new();
    private readonly TextBox _pass = new() { UseSystemPasswordChar = true };
    private readonly TextBox _pass2 = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _forceWeak = new() { AutoSize = true };
    private readonly DataGridView _grid = new();
    private readonly Button _ok = new() { DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { DialogResult = DialogResult.Cancel };

    public string EntryName => _name.Text.Trim();
    public string? EntryType => NullIfEmpty(_type.Text.Trim());
    public string? Username => NullIfEmpty(_user.Text.Trim());
    public string? Tenant => NullIfEmpty(_tenant.Text.Trim());
    /// <summary>与 CLI 同口径：入库前清除首尾空白。</summary>
    public string Password => _pass.Text.Trim();

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
        Height = 490;

        _ok.Text = Loc.T("Save", "保存");
        _cancel.Text = Loc.T("Cancel", "取消");
        _forceWeak.Text = Loc.T("allow weak password (--force-weak)", "允许弱密码（--force-weak）");

        var table = new TableLayoutPanel { Dock = DockStyle.Top, Height = 200, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, L("name", "名称"), _name);
        AddRow(table, L("type", "类型"), _type);
        AddRow(table, L("user", "账号"), _user);
        AddRow(table, L("tenant", "租户"), _tenant);
        AddRow(table, L("password", "密码"), _pass);
        AddRow(table, L("confirm", "再次确认"), _pass2);
        table.Controls.Add(_forceWeak, 1, table.RowCount);

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
        // 新行的加密复选框默认为 true（与视觉一致：新行勾选=加密；取消勾选才存明文）
        _grid.DefaultValuesNeeded += (_, e) => e.Row.Cells["enc"].Value = true;

        var fieldLabel = new Label
        {
            Text = Loc.T("Custom fields (uncheck \"encrypted\" for plain fields like IP/protocol)",
                         "自定义字段（\"加密\"取消勾选即存明文，如 IP/协议）"),
            Dock = DockStyle.Top,
            Height = 30,
            Padding = new Padding(12, 8, 0, 0),
        };

        var fieldPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 12) };
        fieldPanel.Controls.Add(_grid);
        fieldPanel.Controls.Add(fieldLabel);

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

    /// <summary>OK 校验：两次密码比对、字段收集/去重/空值、Trim、弱密码警告（默认 No）。失败返回并弹窗。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) { base.OnFormClosing(e); return; }

        var problems = new List<string>();
        if (EntryName.Length == 0) problems.Add(L("name is required", "名称不能为空"));
        if (Password.Length == 0)
            problems.Add(L("password cannot be empty (or only whitespace)",
                           "密码不能为空（或全是空白）"));
        else if (_pass.Text.Trim() != _pass2.Text.Trim())
            problems.Add(L("passwords do not match", "两次输入不一致"));

        Fields.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) break;
            var fname = Convert.ToString(row.Cells[0].Value)?.Trim() ?? "";
            var fval = Convert.ToString(row.Cells[1].Value)?.Trim() ?? "";
            var enc = row.Cells["enc"].Value is null ? true : Convert.ToBoolean(row.Cells["enc"].Value);
            if (fname.Length == 0 && fval.Length == 0) continue;
            if (fname.Length == 0) { problems.Add(L("a field has no name", "有一个字段没有填写字段名")); continue; }
            if (fval.Length == 0)
            {
                problems.Add(L($"field {fname} has an empty value", $"字段 {fname} 的值为空"));
                continue;
            }
            if (Fields.Any(f => f.Name == fname))
                problems.Add(L($"duplicate field: {fname}", $"字段重复：{fname}"));
            else
                Fields.Add((fname, fval, !enc));
        }

        if (problems.Count > 0)
        {
            e.Cancel = true;
            DialogResult = DialogResult.None;   // 防止之后点 X 关闭时按 OK 重走校验/弱密码确认
            MessageBox.Show(string.Join("\n", problems), "pwhide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            base.OnFormClosing(e);
            return;
        }

        // 弱密码警告（与 CLI 同口径）：默认按钮 No（回车不会误存弱密码）
        if (!_forceWeak.Checked && WeakSecret.Check(Password) is { } reason)
        {
            var proceed = MessageBox.Show(
                Loc.T($"Weak password: {Loc.Tr(reason)}\n\nSave anyway?", $"弱密码：{reason}\n\n仍要保存吗？"),
                "pwhide", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (proceed != DialogResult.Yes) { e.Cancel = true; DialogResult = DialogResult.None; base.OnFormClosing(e); return; }
        }

        base.OnFormClosing(e);
    }
}
