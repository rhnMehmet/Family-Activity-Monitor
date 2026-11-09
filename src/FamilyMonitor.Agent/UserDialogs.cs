namespace FamilyMonitor.Agent;

internal interface IUserDialogs
{
    bool ConfirmHistoryDeletion(IWin32Window owner);
    bool RequestConsent(IWin32Window owner);
    bool RequestGameDiscoveryConsent(IWin32Window owner) => false;
    bool RequestBrowserConsent(IWin32Window owner,FamilyMonitor.Core.Browsers.BrowserOptions options) => false;
    bool ConfirmBrowserHistoryDeletion(IWin32Window owner) => false;
    bool AuthorizeParentSettings(IWin32Window owner,FamilyMonitor.Core.Security.ParentalPinService pin) => !pin.Enabled;
}

internal sealed class UserDialogs : IUserDialogs
{
    public bool RequestBrowserConsent(IWin32Window owner,FamilyMonitor.Core.Browsers.BrowserOptions options) => MessageBox.Show(owner,
        "Seçili Chrome / Edge / Firefox eklentilerinden yalnızca ön plandaki normal sekmenin alan adı ve doğrulanmış aktif süresi yerel olarak kaydedilecek.\n\n"+
        $"URL yolu: {(options.DetailedUrls?"Açılacak (query/fragment temizlenir)":"Kapalı")}\n"+
        $"Arama sorguları: {(options.SearchQueries?"Ayrı seçiminizle açılacak; yalnızca bilinen sonuç URL'lerinden":"Kapalı")}\n\n"+
        "Eklentide de ayrı izin gerekir. Özel gezinme, form, şifre, çerez, oturum tokenı, sayfa içeriği ve ekran görüntüsü toplanmaz. İnternete aktarım yoktur.\n"+
        "İzni PIN gerekmeksizin geri çekebilirsiniz. Belirtilen seçimleri açıkça onaylıyor musunuz?",
        "Tarayıcı verileri için açık izin",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
    public bool ConfirmBrowserHistoryDeletion(IWin32Window owner) => MessageBox.Show(owner,
        "Yalnızca yerel tarayıcı ziyaretleri ve arama geçmişi silinecek. Genel uygulama/oyun süreleri, izinler ve ayrı yükseltme yedekleri korunacak. Devam edilsin mi?",
        "Tarayıcı geçmişini temizle",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
    public bool AuthorizeParentSettings(IWin32Window owner,FamilyMonitor.Core.Security.ParentalPinService pin) => ParentPinUi.Authorize(owner,pin);
    public bool RequestGameDiscoveryConsent(IWin32Window owner)
    {
        using var dialog = new GameConsentDialog();
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
    public bool ConfirmHistoryDeletion(IWin32Window owner) => MessageBox.Show(owner,
        "Bu Windows kullanıcısının tüm yerel kullanım geçmişi silinecek ve izleme duracak.\n" +
        "Aktif kullanım, oyun ön plan ve tarayıcı ziyaret/arama kayıtları silinecek. Cihaz ayarları ve izin tercihiniz korunacak.\n" +
        "Sürüm güncelleme yedekleri Backups klasöründe ayrı kalır; bu düğme onları silmez. Devam edilsin mi?",
        "Kullanım geçmişini sil", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    public bool RequestConsent(IWin32Window owner)
    {
        using var dialog = new ConsentDialog();
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
