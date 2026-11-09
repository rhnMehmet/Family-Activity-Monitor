namespace FamilyMonitor.Agent;

internal sealed class ConsentDialog : Form
{
    public const string PrivacyText = """
        Bu uygulama yalnızca bu Windows kullanıcısının ön plandaki uygulama adını, süreç adını,
        kullanım başlangıç/bitiş zamanını ve aktif kullanım süresini yerel bilgisayara kaydeder.
        Rastgele yerel cihaz ve oturum tanımlayıcıları da kayıtlarla birlikte saklanır.

        Bu genel izin kapsamında pencere başlığı, URL, arama sorgusu, sayfa içeriği, tuş vuruşu, şifre, özel mesaj,
        ekran görüntüsü veya tuşların içeriği toplanmaz. Son girdi zamanı yalnızca
        hareketsizliği hesaplamak için bellekte kullanılır. Hiçbir veri internete gönderilmez.

        İzlemeyi istediğiniz an duraklatabilir veya durdurabilirsiniz. Ayarlar bölümünden
        izni geri çekebilir, geçmiş kayıtlarını silebilirsiniz. Veriler otomatik silinmez.
        İzin vermek izlemeyi başlatmaz; ayrıca “İzlemeyi başlat” düğmesine basmanız gerekir.
        Normal açılışta izleme kapalı başlar. Windows ile otomatik başlatma varsayılan kapalıdır;
        yalnızca ayrı ayardan açıkça seçerseniz geçerli genel izninizle oturum açılışında takip başlayabilir.

        Tarayıcı alan adı, temizlenmiş URL yolu ve arama sorguları ayrı, varsayılan kapalı izinlere tabidir.
        Tarayıcı Ayarları ve eklenti ekranından bu izinleri ayrı ayrı seçebilirsiniz; bu genel izin onları açmaz.

        Oyun keşfi ayrı ve varsayılan kapalı bir izne tabidir. Bu ek izinle yerel oyun
        kurulum metaverileri ve exe eşleştirmeleri saklanabilir; ayrıntılar Yüklü Oyunlar sekmesindedir.
        """;

    public ConsentDialog()
    {
        Text = "Bilgilendirme ve açık izin";
        ClientSize = new Size(710, 500);
        MinimumSize = new Size(650, 500);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10);
        MinimizeBox = MaximizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Text = "Cihaz kullanımı sizin kontrolünüzde", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
        layout.Controls.Add(new TextBox { Text = PrivacyText.ReplaceLineEndings(), ReadOnly = true,
            Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Window });
        var consent = new CheckBox { Text = "Açıklamayı okudum. Bu Windows hesabındaki uygulama kullanımımın\nyukarıda açıklandığı şekilde yerel kaydına izin veriyorum.", Dock = DockStyle.Fill, AutoSize = true };
        layout.Controls.Add(consent);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var accept = new Button { Text = "İzin ver", Enabled = false, AutoSize = true, DialogResult = DialogResult.OK };
        var decline = new Button { Text = "Şimdi değil", AutoSize = true, DialogResult = DialogResult.Cancel };
        consent.CheckedChanged += (_, _) => accept.Enabled = consent.Checked;
        actions.Controls.AddRange([accept, decline]);
        layout.Controls.Add(actions);
        Controls.Add(layout);
        CancelButton = decline;
    }
}
