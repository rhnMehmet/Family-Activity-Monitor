using FamilyMonitor.Core.Games;

namespace FamilyMonitor.Agent;

internal sealed class ManualGameDialog : Form
{
    private readonly ComboBox catalog = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox name = new() { Dock = DockStyle.Fill };
    private readonly TextBox executable = new() { Dock = DockStyle.Fill };
    private readonly TextBox family = new() { Dock = DockStyle.Fill };
    private readonly ComboBox platform = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Label error = new() { AutoSize = true, ForeColor = Color.Firebrick, MaximumSize = new Size(640, 0) };
    private readonly GameInstallation[] installations;
    public GameInstallation? Installation { get; private set; }
    public string ExecutablePath { get; private set; } = "";

    public ManualGameDialog(IReadOnlyList<GameInstallation> games)
    {
        installations = games.ToArray();
        Text = "Manuel oyun ekle / eşleştir"; ClientSize = new Size(710, 425); MinimumSize = new Size(660, 425);
        Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string title, Control control) { layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); layout.Controls.Add(control); }
        catalog.Items.Add("Yeni oyun"); foreach (var game in games) catalog.Items.Add($"{game.Name} ({GamePages.PlatformText(game.Platform)})");
        catalog.SelectedIndex = 0;
        foreach (var value in Enum.GetValues<GamePlatform>()) platform.Items.Add(value);
        platform.SelectedItem = GamePlatform.Custom;
        Row("Katalog kaydı", catalog); Row("Oyun adı", name); Row("Platform", platform);
        var exeRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        exeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); exeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Exe seç…", AutoSize = true };
        exeRow.Controls.Add(executable); exeRow.Controls.Add(browse); Row("Tam exe yolu", exeRow);
        Row("Paket aile kimliği\n(isteğe bağlı)", family);
        var note = new Label { Text = "Exe çalıştırılmaz. javaw, launcher ve benzeri ortak/yardımcı süreçler kalıcı oyun eşleştirmesine alınmaz. " +
            "Paket kimliği yalnızca bildiğiniz tam Windows paket aile kimliği olmalıdır; joker karakter kullanmayın.", AutoSize = true, MaximumSize = new Size(470, 0) };
        Row("Eşleştirme kapsamı", note); Row("", error);
        var save = new Button { Text = "Eşleştirmeyi kaydet", AutoSize = true };
        Row("", save);
        catalog.SelectedIndexChanged += (_, _) =>
        {
            platform.Enabled = catalog.SelectedIndex == 0;
            if (catalog.SelectedIndex <= 0) return;
            var game = installations[catalog.SelectedIndex - 1]; name.Text = game.Name;
            platform.SelectedItem = game.Platform; family.Text = game.PackageFamilyName ?? "";
            executable.Text = game.ExecutablePaths.FirstOrDefault() ?? "";
        };
        browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "Windows uygulaması (*.exe)|*.exe", CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK) executable.Text = dialog.FileName; };
        save.Click += (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("Oyun adını girin.");
                var path = executable.Text.Trim(); var package = family.Text.Trim();
                if (path.Length == 0 && package.Length == 0) throw new ArgumentException("Tam exe yolu veya paket aile kimliği gereklidir.");
                if (path.Length > 0 && (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Mevcut bir exe dosyası seçin.");
                if (path.Length > 0 && new GameMetadataResolver().IsSharedOrHelperExecutable(path)) throw new ArgumentException("Ortak çalışma zamanı / launcher güvenilir biçimde tek bir oyuna eşlenemez.");
                if (package.Length > 255 || package.IndexOfAny(['*', '?', '\\', '/', ' ']) >= 0) throw new ArgumentException("Paket aile kimliği geçersiz.");
                var selected = catalog.SelectedIndex > 0 ? installations[catalog.SelectedIndex - 1] : null;
                var root = path.Length > 0 ? Path.GetDirectoryName(Path.GetFullPath(path))! : "";
                Installation = selected is not null ? selected with { Name = name.Text.Trim(), IsConfirmed = true, PackageFamilyName = package.Length > 0 ? package : selected.PackageFamilyName }
                    : GameIdentity.Create(GameIdentity.Stable(path.Length > 0 ? path : package), name.Text.Trim(), (GamePlatform)platform.SelectedItem!,
                        "Manual", "user-defined", root, path.Length > 0 ? [path] : [], GameInstallationState.Installed, packageFamily: package.Length > 0 ? package : null);
                ExecutablePath = path; DialogResult = DialogResult.OK; Close();
            }
            catch (ArgumentException exception) { error.Text = exception.Message; }
        };
        Controls.Add(layout);
    }
}
