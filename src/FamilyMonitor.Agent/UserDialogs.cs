namespace FamilyMonitor.Agent;

internal interface IUserDialogs
{
    bool ConfirmHistoryDeletion(IWin32Window owner);
    bool RequestConsent(IWin32Window owner);
    bool RequestGameDiscoveryConsent(IWin32Window owner) => false;
}

internal sealed class UserDialogs : IUserDialogs
{
    public bool RequestGameDiscoveryConsent(IWin32Window owner)
    {
        using var dialog = new GameConsentDialog();
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
    public bool ConfirmHistoryDeletion(IWin32Window owner) => MessageBox.Show(owner,
        "Bu Windows kullanıcısının tüm yerel kullanım geçmişi silinecek ve izleme duracak.\n" +
        "Aktif kullanım ve oyun ön plan kayıtları silinecek. Cihaz ayarları ve izin tercihiniz korunacak.\n" +
        "Sürüm güncelleme yedekleri Backups klasöründe ayrı kalır; bu düğme onları silmez. Devam edilsin mi?",
        "Kullanım geçmişini sil", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    public bool RequestConsent(IWin32Window owner)
    {
        using var dialog = new ConsentDialog();
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
