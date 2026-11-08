# Görev 1 teslim raporu — 7 Kasım 2026

## 1. Dosyalar ve klasörler

Başlangıç klasörü boştu. `FamilyMonitor.sln`, `Directory.Build.props`, `global.json`, `NuGet.Config`, `.editorconfig`, `.gitignore` oluşturuldu.

- `src/FamilyMonitor.Agent`: giriş noktası, ana ekran, açık izin diyaloğu, normal kullanıcı manifesti ve proje dosyası.
- `src/FamilyMonitor.Core`: modeller, arayüzler, seçenekler, takip durum makinesi.
- `src/FamilyMonitor.Infrastructure`: monoton saat, SQLite deposu, atomik yerel ayarlar, Win32 adaptörü ve WTS bildirimleri.
- `tests/FamilyMonitor.Tests`: sahte saat/kaynak/depo, süre testleri, gerçek SQLite dosya testleri, API adaptörü ve izin ayarı testleri.
- `scripts/dotnet.ps1`: SDK ve proje içi önbellek seçimi.
- `README.md`, `docs/architecture.md`, `docs/privacy.md`, `docs/testing.md`, bu rapor.
- `artifacts/windows`: kendi runtime'ını içeren Windows x64 dağıtım klasörü. SDK, NuGet ve test çıktılarıyla birlikte Git dışında.

Git deposu `main` dalında başlatıldı; commit, remote veya push oluşturulmadı. Mevcut dosya/çalışma silinmedi.

## 2. Geliştirilen özellikler

Yaklaşık saniyelik foreground uygulama örneklemesi, pencere değişiminde dönem ayırma, 180 saniye varsayılan idle eşiği, girdiyle doğrulanmış aktif süre, duraklat/devam/durdur, kilit/uyku/uzun örnek boşluğu kapıları, monoton ve çakışmayan zaman çizelgesi. İki örnek arasında tam eşikte geri dönüş ayrıca kontrol edilir.

Yerel SQLite kayıtları, UUID cihaz/kayıt kimliği, yerel oturum tanımlayıcısı, süreç/uygulama adları, UTC başlangıç/bitiş, süre ve durum saklanır. Upsert ile doğrulanmış süre yazılır, restart sonrası korunur. Yerel gün sınırları ve DST dikkate alınarak özet çıkarılır.

Türkçe WinForms ekranında durum, aktif uygulama, seçilen gün toplamı, uygulama listesi, kronolojik geçmiş, ayarlar ve silme vardır. Görünür tepsi menüsü duraklat/durdur/çıkış sağlar. İlk açık izin, geri çekme, ayrı başlatma ve geçmiş temizleme uygulanmıştır. Her açılışta izleme kapalıdır. Otomatik başlatma, yönetici yetkisi veya servis yoktur.

## 3. .NET seçimi

**.NET 10 LTS**, SDK **10.0.403**, runtime **10.0.14**. Windows 11 25H2 x64 (26200.9711) üzerinde derlendi. .NET 8 desteği Kasım 2026'da sona yaklaşırken .NET 10 Kasım 2028'e kadar desteklenir. [Microsoft destek politikası](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Windows destek matrisi](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

Başlangıçta yalnızca .NET 8 runtime ve Git vardı. Resmi SDK yönetici kurulumu olmadan `.tools/dotnet` içine indirildi. Sistem PATH'i veya Windows ayarları değiştirilmedi. Derlemek için SDK gerekir; runtime içeren yayını çalıştırmak için ayrıca SDK gerekmez.

## 4. Önemli API'ler

`GetForegroundWindow`, `GetWindowThreadProcessId`, `GetLastInputInfo`, `Process.GetProcessById/ProcessName`, `OpenInputDesktop`, `GetUserObjectInformationW`, `CloseDesktop`, `WTSRegisterSessionNotification`, `WTSUnRegisterSessionNotification`, `WM_WTSSESSION_CHANGE`, `SystemEvents.PowerModeChanged`, `Stopwatch` ve `Microsoft.Data.Sqlite`. Resmi API bağlantıları ve gerekçeleri [mimaride](architecture.md) bulunur. Tuş dinleme hook'u, pencere başlığı veya ekran görüntüsü API'si kullanılmadı.

## 5. Çalıştırılan komutlar

Ortam: `git --version`, `dotnet --info`, Windows kayıt defteri sürüm sorgusu. CIM sorgusu yetki nedeniyle başarısız oldu; başka salt okunur sorguyla sürüm belirlendi. Resmi `dotnet-install.ps1 -Channel 10.0 -InstallDir .tools/dotnet -NoPath` çalıştırıldı.

Çözüm `dotnet new sln --format sln` ve `dotnet sln add` ile oluşturuldu. `restore`, Debug `build`, `test`, Release `build`, Release `test` ve Windows x64 self-contained `publish` çalıştırıldı. Tekrarlanabilir tam komutlar [testing.md](testing.md#komutlar) içinde. Restore'da `--configfile NuGet.Config --disable-parallel '-m:1'`, derlemede `--no-restore '-m:1' '-p:UseSharedCompilation=false'` kullanıldı.

`git init -b main`, `git status --short`, `git check-ignore`, `git remote -v` ile depo ve dışlamalar kontrol edildi. SDK, derleme çıktısı, yayın ve test sonuçları dışlanıyor. Hiçbir kullanım verisi uzak sunucuya gönderilmedi; ağ yalnızca geliştirme SDK/paket indirmeleri için kullanıldı.

## 6. Test sonuçları

| Kontrol | Sonuç |
|---|---|
| Restore | Başarılı |
| Son Debug build | 0 uyarı, 0 hata |
| Son Release build | 0 uyarı, 0 hata |
| Debug xUnit | **41 geçen, 0 kalan/başarısız, 0 atlanan** |
| Release xUnit | **41 geçen, 0 kalan/başarısız, 0 atlanan** |
| Windows x64 self-contained publish | Başarılı |
| Gerçek süreç başlangıcı | İzin ve ana pencere oluşturuldu; yayın ayrıca harici SDK yolu verilmeden açıldı |
| Görsel inceleme | Ekran yakalama uygulama onayı zaman aşımı nedeniyle yapılamadı |
| Fiziksel kilit/uyku ve gerçek foreground takibi | Manuel kabul bekliyor; otomatik testler sahte sağlayıcılıdır |

Aynı 41 senaryo iki yapılandırmada çalıştırıldı; 82 farklı test olduğu iddia edilmez. Sonuçlar `TestResults/debug.trx` ve `TestResults/release.trx` içindedir. İlk hatalı derleme ve sandbox'ta iptal edilen test denemesi [testing.md](testing.md) içinde açıklandı; son geçerli sonuçlara dahil edilmedi.

## 7. Manuel test adımları

`artifacts/windows/FamilyMonitor.Agent.exe` dosyasını açın. İlk izin reddinde kayıt olmamasını, kendi açık izninizle başlatmayı, Not Defteri/tarayıcı geçişlerini, 180 saniye idle ve dönüşü, duraklat/devam/durduru deneyin. Ardından gerçek kilit/açma, uyku/uyanma, tepsi menüsü, izin geri çekme, geçmiş silme ve yeniden başlatma kontrollerini yapın. Ayrıntılı 14 kabul adımı ve beklenen sonuçlar [testing.md](testing.md) içinde. Uygulama otomasyonla izin verilerek veya başlatılarak gerçek etkinlik ölçümüne alınmadı.

## 8. Bilinen sorunlar ve eksikler

Görsel yerleşim ve DPI kontrolü, fiziksel kilit/uyku, gerçek API uçtan uca doğrulaması ve temiz ikinci Windows bilgisayarında yayın testi henüz tamamlanmadı. SQLite şifrelemesi, saklama süresi, kurulum sihirbazı, kod imzası ve otomatik güncelleme yoktur.

Yaklaşık bir saniyelik örnekleme kısa pencereleri kaçırabilir. Sayaç son girdiye kadar doğrulanır; girdisiz kuyruk çıkış/kilit/idle'da atılır. Pasif medya kullanımı ayrıca modellenmedi. Beş saniyeden uzun gözlem boşluğu sayılmaz. Büyük veri setlerinde senkron UI/SQLite erişimi yavaşlayabilir; kronolojik ekranda son 500 kayıt gösterilir. Özel `Default` dışı Windows masaüstlerinde ölçüm yapılmaz. Monoton zaman çizelgesi sistem saatine anında yeniden eşitlenmez.

## 9. Görev 1 durumu

**Çalıştırılabilir prototip, kaynak kod, belgeler, derlemeler, otomatik testler ve Windows dağıtımı tamamlandı. Fiziksel Windows manuel kabul doğrulaması bekliyor.** Bu sınır dışında test edilmiş gibi raporlama yapılmadı. Görev 2'ye geçilmedi.

## 10. Görev 2 öncesi öneriler

Önce manuel kabul listesini gerçek test hesabında tamamlayın ve aktif kullanım/okuma tanımını deneyerek onaylayın. Sonrasında şifreleme ve saklama süresi, SQLite migration/yedekleme politikası, büyük veri performansı, installer ve kod imzasını planlayın. Gelecekteki paylaşım izni ve cihaz eşleştirme, mevcut yalnızca yerel izinden ayrı tasarlanmalı. Backend, tarayıcı eklentisi ve mobil kapsam kullanıcı onayı gelene kadar başlatılmamalıdır.
