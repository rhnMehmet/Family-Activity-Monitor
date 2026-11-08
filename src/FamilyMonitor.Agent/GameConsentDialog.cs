namespace FamilyMonitor.Agent;

internal sealed class GameConsentDialog : Form
{
    public GameConsentDialog()
    {
        Text = "Oyun kütüphanesi keşfi için ayrı izin";
        ClientSize = new Size(660, 400); MinimumSize = new Size(620, 400);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = """
                Bu ayrı izinle Steam/Epic kurulum metaverileri, bu Windows kullanıcısının paket bilgileri,
                Minecraft sürüm metaverileri, kurulu programların kurulum konumları ve seçtiğiniz oyun
                klasörleri sınırlı olarak incelenir. Tam disk taraması yapılmaz, klasör izinleri aşılmaz.

                Oyun adı, platformu, kimliği, kurulum konumu, exe eşleştirmeleri ve elle verdiğiniz adlar
                yerel SQLite kataloğunda saklanır. Aktif uygulamanın erişilebilen exe yolu ve paket kimliği
                oyun eşleştirmesi için bellekte kullanılır. Oyun süreleri mevcut kullanım kaydının parçasıdır.

                1.2.1: Oyunların ön planda kalma süresi ayrıca saklanır. Güvenilir ön plan oyununda XInput
                durumu saniyelik örneklenerek yalnızca etkileşim var/yok değerlendirilir. Hangi kontrolcü
                tuşunun veya kombinasyonun kullanıldığı saklanmaz. Diğer kontrolcüler doğrulanamayabilir.

                Hesap/oturum dosyaları, şifreler, komut satırı, oyun kayıtlarının içeriği, mesajlar,
                tuş içerikleri ve ekran görüntüleri okunmaz. Veri internete gönderilmez.

                İzin varsa açılışta, elle, kütüphane değişikliklerinde ve saatte bir keşif yapılır.
                Oyun modunda ağır keşif ertelenir. Ön planda kalma süresi, doğrulanmış aktif süre değildir.
                İzni aynı sekmeden geri çekebilirsiniz. Eski katalog/geçmiş kendiliğinden silinmez.
                Bu izin tek başına kullanım izlemeyi başlatmaz; izleme için ayrıca genel izin ve Başlat gerekir.
                """.ReplaceLineEndings() });
        var check = new CheckBox { AutoSize = true, Text = "Oyun kütüphanemin bu kapsamda yerel keşfine izin veriyorum.", Margin = new Padding(3, 14, 3, 14) };
        layout.Controls.Add(check);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var accept = new Button { Text = "Keşfe izin ver", AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Şimdi değil", AutoSize = true, DialogResult = DialogResult.Cancel };
        check.CheckedChanged += (_, _) => accept.Enabled = check.Checked;
        actions.Controls.AddRange([accept, cancel]); layout.Controls.Add(actions); Controls.Add(layout); CancelButton = cancel;
    }
}
