# Görev 2 teslim raporu — 2.0.0-prototype

Tarih: 9 Kasım 2026 (8 Kasım teslimi ve son arayüz kontrolü). Kaynak geliştirme ve otomatik doğrulama bu raporda, fiziksel kabul ise ayrı [A–J test planında](task-2-manual-tests.md) tutulur. Gerçek Chrome/Edge/Firefox, RDR2, Windows kilidi/reboot, gerçek DPI geçişi ve kalıcı eklenti kurulumu bu çalışma sırasında test edilmedi. Üretim kabulü bu kontroller tamamlanana kadar bekliyor.

## Sonuç ve teslimler

Mevcut Windows Forms projesi korunarak tarayıcı toplama, yerel Native Messaging, opt-in başlangıç ve isteğe bağlı ebeveyn PIN'i eklendi. Ortak TypeScript kodu Chrome/Edge MV3 worker ve Firefox 140+ MV3 event page için ayrı manifest üretir. Yeni tarayıcı kapsamları, per-browser seçimler ve iki başlangıç seçimi kapalı başlar. Mobil/bulut/uzaktan izleme eklenmedi.

Yayın: `artifacts/windows-2.0.0-prototype/FamilyMonitor.Agent.exe`, kendi .NET 10 win-x64 runtime'ıyla. Paket üç host varyantını, üç eklentiyi, manifestleri, HKCU kurulum betiğini ve Türkçe kurulum/kabul belgelerini içerir. Eski 1.2.1 ve önceki yayınlar ayrı tutuldu. Paket ve son hash kontrolü: başarılı. 310 dosya, yaklaşık 120,68 MiB; Agent ve üç host varyantı 2.0.0.0. Üç eklenti manifesti/sabit kimliği ve paket içeriği doğrulandı. Dört host exe geçersiz çağrıyı bağlantı kurmadan reddetti. Kurulum betiği -WhatIf ile de sınandı, registry yazılmadı. Tekrar yayından önce/sonra eski 1.2.1 klasörünün 280 dosyasının tamamı SHA-256 ile aynı; daha eski üç Agent exe önceki saklı hash listesiyle aynı. Yerel tam envanter Git dışındaki TestResults içinde.

## Yeni mimari ve tarayıcı desteği

- Core: izin modelleri, çift sanitizasyon, 8 KiB çerçeve/katı şema, epoch/sequence/focus korelasyonu, yalnızca doğrulanmış genel aralığı bölen site zaman çizelgesi, PIN kuralları.
- Infrastructure: aynı kullanıcı/oturum pipe sunucusu, PID→exe tam yol doğrulaması, sınırlı update kuyruğu, şema 4 doğrulanmış yedekli migration, tarayıcı SQL deposu ve mevcut tek transaction writer'ına entegrasyon, HKCU Run adaptörü, PIN hash dosyası.
- Agent: **TARAYICI ETKİNLİĞİ** ve **Tarayıcı Ayarları**, bugün/genel tarayıcı alt toplamları, site süreleri, tarih/tarayıcı filtreleri, son 500 ziyaret, izinli sütunlar, ayrı silme/onay, bağlantı/kurulum, başlangıç/PIN kontrolleri.
- NativeHost: tarayıcının stdio portu ile Agent pipe'ı arasında sınırlı çerçeve aktarır. Agent'ı açmaz, URL logu yazmaz, TCP portu açmaz.
- Eklentiler: aktif normal pencere/sekme olayları, 250 ms birleştirme ve değişmeyen snapshot'ın atlanması, 10 saniyelik boş heartbeat, kopmada politika temizliği ve 1/2/5/15 dakika yeniden deneme. Content script/history/cookie/DOM erişimi yoktur.

Chrome ve Edge aynı sabit Chromium public key kimliğiyle geliştirme paketi olarak hazırlanır; tarayıcıya özel host kayıtları ayrıdır. Firefox manifesti farklı arka plan bağlamı, sabit Gecko ID ve modern isteğe bağlı veri izinlerini kullanır. Firefox geçici kurulum yeniden başlatmada kaybolur; normal kalıcı kurulum için imzalama gerekir. Mağaza yayını veya imzalı eklenti/binary teslimi yapılmadı. [Kurulum adımları ve kaynaklar](task-2-browser-installation.md).

## Süre, hata ve gizlilik davranışı

OS foreground/idle mevcut monoton tracker'dan gelir. Tarayıcı ailesi/HWND veya izin/bağlantı değişince yeni challenge istenir. Aynı tarayıcının tüm bağlı profilleri taze cevap vermeli, tek normal odak bulunmalıdır; aksi halde siteye süre verilmez ve durum gösterilir. WebExtension pencere ID'si HWND değildir; bu prototip taze odak eşleştirmesi kullanır, birebir yerel pencere kanıtı yoktur. Bu nedenle çoklu profil/odak fiziksel kabulü özellikle gerekir.

Ziyaretler sadece genel tracker'ın girdiyle doğrulanmış pozitif aralıklarıdır; uygulama toplamına ikinci kez eklenmez. Oyun ön plandayken arka YouTube, idle, pause, lock/sleep ve bağlantı kopması site süresi üretmez. Aynı domain'e ait sekme/pencere değişimi de ayrı dönem oluşturur. Yaklaşık 1 saniye OS örnekleme ve 250 ms coalescing kesin zaman doğruluğu sağlamaz; belirsizlikte eksik atıf tercih edilir.

Domain varsayılan tek kapsamdır. URL yolu query/fragment olmadan ve hassas yol filtreleriyle, bilinen sonuç rotalarındaki sorgu ise ayrı izinle alınır. Agent tekrar doğrular. Her olası sırrı tanıyan filtre garantisi yoktur; izinli domain/yol/arama metni hassas olabilir. Formlar/parolalar/çerezler/token depoları/sayfa içerikleri/özel sekme adresleri için toplama kodu yoktur. Genel tarayıcı uygulama süresi, özel site metadata'sından farklı kapsamdır. [Güncel gizlilik metni](privacy.md).

İzin daraltma/geri çekme önce mevcut toplamayı durdurur, sonra diske kaydeder. Yazma hatası kullanıcıya bildirilir; otomatik başlangıç kaldırılmaya çalışılır. Disk ve registry birlikte erişilemezse tercih kalıcılığı garanti edilemez; sonraki açılışın kontrol edilmesi gerekir. Aynı Windows hesabında dosya/exe değiştiren kötü amaçlı yazılımı engelleyen bir sınır değildir.

## Veritabanı ve silme

Mevcut DB için onay → SQLite Backup API → bütünlük/FK kontrolü → transaction ile şema 4. Eski uygulama/oyun/ön plan kayıtları korunur. Yeni ziyaretler activity parent FK'sine bağlıdır. Migration reddi dosya hash'inin değişmediği testle, geri dönüş ise yedeğin ayrı yeni hedefe doğrulanmış kopyasıyla sınanır; canlı dosya otomatik ezilmez.

Tarayıcı saklama 30 gün varsayılan, 1–365 gün. Mantıksal cutoff hemen, fiziksel SQL budaması oyun dışı uygun bakımda uygulanır; kuyruktaki eski kısımlar geri getirilemez. **Tarayıcı geçmişini temizle** yalnızca onayla tarayıcı tablosu/kuyruğunu siler; genel/oyun/katalog/izin/PIN korunur. Genel **Kullanım geçmişini sil** bütün kullanım alt kayıtlarını ve kuyruklarını temizler. Upgrade yedekleri ayrıdır ve temizleme/saklama bunları silmez. `secure_delete`, ertelenen VACUUM/WAL truncate vardır; fiziksel SSD/OS/harici yedek kalıntıları için garanti yoktur.

Kişisel DB/ayar/PIN dosyaları bu geliştirmede açılmadı veya silinmedi; gerçek HKCU host veya startup kaydı oluşturulmadı. Otomatik testler geçici veritabanı ve sahte registry kullanır.

## Başlangıç ve PIN

Otomatik açılış ve otomatik izleme ayrı kapalı seçimlerdir. Yalnızca açık opt-in HKCU Run kaydı yazar. Geçerli genel izin + iki seçim + beklenen kayıt + hazır etkileşimli oturum olmadan otomatik takip başlamaz. Elle açılış kapalıdır; oturum açılmadan takip, servis/SYSTEM veya Task Manager engeli yoktur. Otomatik başlangıç görünür tepsi/bildirimle anlaşılır.

PIN 6–12 rakamdır: rastgele salt + PBKDF2-HMAC-SHA256 600.000 iterasyon; düz metin yok, sabit zamanlı doğrulama. 5 yanlış deneme 15 dakika kalıcı bekleme getirir. 32 rastgele baytlık kurtarma kodu bir kez gösterilir, bağımsız saltlı hash'i saklanır. Kodla PIN kaldırılır ve yeni PIN kurulabilir. Kodsuz sessiz bypass yoktur. PIN yalnızca ebeveyn ayarları/başlangıcı etkinleştirmeyi korur; pause/stop/izin çekme/çıkış serbesttir. OS hesap güvenliği değildir.

## Otomatik doğrulama

| Kontrol | Başarılı | Başarısız | Atlanan |
|---|---:|---:|---:|
| Debug Core/Infrastructure | 205 | 0 | 0 |
| Debug Windows UI/yerel pipe | 35 | 0 | 0 |
| Release Core/Infrastructure | 205 | 0 | 0 |
| Release Windows UI/yerel pipe | 35 | 0 | 0 |
| Ortak TypeScript/Node eklenti testleri | 26 | 0 | 0 |

Her C# yapılandırmasında **240** test, eklentiyle **266 farklı vaka**; Debug/Release tekrarlarıyla toplam **506 başarılı çalıştırma, 0 başarısız, 0 atlanan**. Önceki 173 vakaya 67 C# + 26 Node vakası eklendi. İki son çözüm derlemesi 0 uyarı/0 hata. Yerel kanıtlar `TestResults/task2-ui-delivery-Debug_*.trx` ve `task2-ui-delivery-Release_*.trx`; Git dışında tutulur. Node testleri ve npm audit başarılı (0 bilinen açık).

Yeniden çalıştırma: README'deki Debug/Release komutları ve `extensions` içinde `npm ci`, `npm test`. Testler gerçek Program başlangıcını, kişisel DB'yi ve gerçek registry yazımını kullanmaz.

Önceki 173 vaka kaldırılmadı; eski UI başlık beklentisi yeni sürüme güncellendi. Ek kapsam:

| Test dosyası | Kapsam |
|---|---|
| `BrowserCoreTests.cs` | Üç tarayıcı, izin/epoch/HWND/çoklu profil/private, replay/şema/çerçeve sınırı, URL/sorgu temizleme, sekme/pencere bölme ve çift saymama |
| `BrowserStorageTests.cs` | Migration reddi/yedek/ayrı geri dönüş, FK/atomic batch/restart recovery, silme-kuyruk-dirilmeme, saklama/erteleme, 10.000 kayıt, oyun/pause/revoke, 5 saniye idle/kilit/uyku/dönüş, uzun pause/stale mesaj |
| `PinAndStartupTests.cs` | Salt/hash/doğrulama/deneme sınırı/saat geri alınması/kurtarma, kapalı startup varsayılanı/komut/rollback/izin-oturum koşulları |
| `BrowserUiTests.cs` | Granüler onay, veri alt kümesi/gizli sütunlar, silme onayı/reopen, PIN'in withdraw/exit'i engellememesi, opt-in başlangıç ve %100/%150 kaydırma/geometri |
| `BrowserPipeTests.cs` | Gerçek izole yerel pipe üzerinde test doğrulayıcısıyla pozitif çerçeve akışı, disconnect sınırı, üretim PID kontrolünde yabancı exe reddi, yanlış extension ID, UI pompasız kapanış |
| `extensions.test.mjs` | Üç platform için sahte API, izin/private/sanitizasyon, olay fırtınası/dedupe, sekme/pencere/challenge, policy kapalıyken sorgu yok, async revoke, reconnect ve manifestler |

Pozitif pipe testindeki güvenilen süreç doğrulayıcısı test içindir; üretim kurucusu bunu kullanmaz. Bu test gerçek browser→native host zincirinin kabulü olarak sunulmaz. Özet makinece okunur kanıt: [verification.json](performance-2.0/verification.json). Windows UI bitmap'leri gözle incelendi; gerçek OS DPI geçişi ayrı bekliyor. PowerShell iki kurulum/yayın betiğinin AST kontrolü başarılı; gerçek HKCU kurulumu yapılmadı. npm audit bilinen açık bildirmedi.

## Performans

| Aşama | Saniye | CPU ortalama (%) | Working set ortalama (MiB) | Tepe (MiB) |
|---|---:|---:|---:|---:|
| İzleme kapalı, görünür | 20,1 | 0,128 | 85,97 | 86,89 |
| Sentetik oyun, görünür | 25,0 | 0,150 | 101,00 | 101,27 |
| Sentetik tarayıcı, görünür | 30,1 | 0,233 | 102,46 | 104,39 |
| Sentetik tarayıcı, gizli | 30,1 | 0,114 | 104,69 | 105,28 |
| Sentetik uygulama, gizli (kısa uzun-oturum denemesi) | 90,2 | 0,057 | 105,48 | 106,07 |

Ham veri: [measurements.json](performance-2.0/measurements.json). .NET 10.0.14, Windows 26200, 20 mantıksal işlemci. CPU = süreç CPU saniyesi / geçen saniye / 20 × 100. Ölçüm aşamaları 20–90 saniye; 90 saniye saatlerce dayanıklılık kanıtı değildir. Ölçüm sırasında başka build/test koşturulmadı.

1.2.1 arşivinde kapalı/görünür CPU yaklaşık %0,093 ve 86,24 MiB; sentetik oyunda %0,081 ve 100,42 MiB idi ([ham veri](performance/optimized.json)). Yeni sürümde bu iki senaryonun CPU değeri daha yüksek, RAM yakın; tarayıcı etkinliği de ek iş getiriyor. Ortam yükü ve fixture farkı olduğundan tek koşudan güvenilir neden/gerileme yüzdesi çıkarılamaz. Ölçülen Agent süreci hedeflerin altında kaldı; tüm host süreçleri/eklenti motorları birlikte ölçülmediği için ürün hedeflerine tam kabul verilmedi.

Yeniden üretmek için performans aracını ayrı restore/build edin: `scripts/dotnet.ps1 restore tools/FamilyMonitor.PerformanceHarness/FamilyMonitor.PerformanceHarness.csproj --configfile NuGet.Config`; ardından aynı projeyi `-c Release -o .tools/perf-task2` ile build edin. Yerel dotnet ile `.tools/perf-task2/FamilyMonitor.PerformanceHarness.dll 2.0.0-prototype TestResults/new-measurement.json` çalıştırın. Bu araç gerçek Program kullanmaz; yalnızca geçici sentetik veri oluşturur. Baseline JSON dosyalarını üzerine yazmayın.

Ölçüm aracı üretim MainForm/tracker/buffer/SQLite/tarayıcı runtime ve izole named-pipe listener'ını kullanır. 1.000 katalog + 5.000 geçmiş satırı, üç sentetik eklenti bağlantısı; mesajlar süreç içinden verilir. Gerçek browser motorları, çalışan native host alt süreçleri, GPU/FPS/frame time, fiziksel disk I/O ve saatlerce dayanıklılık ölçülmedi. Toplam Agent+host+eklenti hedefi tam doğrulanmış değildir. CPU toplam makine kapasitesine göre normalize edilir; tek çekirdek yüzdesi ham JSON'da ayrıca vardır. 1.2.1 verisi aynı makinedeki önceki ölçümdür; eşzamanlı kontrollü A/B değildir.

Ölçüm hazırlığında yakalanan UI-thread kapanış beklemesi düzeltildi: pipe listener arka planda başlar; mesaj döngüsü olmadan Dispose testi eklendi. Saklama ertelenirken dirty kaydın eski kısmının geri gelmesi ve aynı-domain sekme/pencere birleşmesi de regresyon testleriyle kapatıldı.

## Bilinen eksikler ve kabul kararı

1. A–J fiziksel kabul, gerçek browser Native Messaging/pencere odak davranışı, gerçek oyun ve Windows reboot henüz bekliyor. Prototip üretim kabulü tamamlandı denemez.
2. Firefox normal kalıcı kurulum için imzalı paket gerektirir. Chrome/Edge geliştirici modu kullanılır; kurumsal politikalar kurulumu engelleyebilir.
3. Extension pencere ID→HWND birebir bağlama yok; ihtiyatlı challenge/tek-odak korelasyonu bazen eksik süre oluşturabilir. Çok kısa geçişler yaklaşık çözünürlükte ölçülür.
4. Tam Agent+host+browser toplam kaynak/FPS/GPU ve uzun oturum ölçümü kullanıcı kabulünde yapılmalı. XInput dışı kontrolcü sınırları korunur.
5. Yerel DB/yedek şifreli değildir; PIN ve pipe aynı OS hesabında kötü amaçlı dosya değişimine karşı sınır değildir. Kod imzalama ve mağaza dağıtımı yapılmadı.
6. Sanitizasyon her olası sırrı tanımaz; ayrı URL/sorgu kapsamları ihtiyaca göre kapalı tutulmalıdır. Upgrade yedekleri kullanıcı tarafından ayrıca yönetilir.

9 Kasım 2026 kullanıcı talimatı, başarılı testlerden sonra yalnızca `codex/task-2-browser-prototype` dalına commit/push yetkisi verir. Main değiştirilmez; force push kullanılmaz. Fiziksel kabul bekliyor.

## 9 Kasım son arayüz iyileştirmeleri

Tarayıcı uygulama süresi ve listelenen doğrulanmış web sitesi süresi ayrı gösterilir; bu iki değerin toplanmayacağı açıklanır. Site toplamı seçili tarih/tarayıcıdaki en çok 500 site grubuna aittir; genel tarayıcı toplamı bu liste sınırından türetilmez. Eklenti olmadan uygulama süresi artabilir, site listesi boş kalır. Boş liste açıklaması kurulum, iki taraflı izin ve takip adımlarını anlatır; tarih/filtre durumunu da hatırlatır.

Üç tarayıcının bağlantısı etkinlik ekranında özetlenir; ayarlarda bağlantı ve Agent site izni ayrı gösterilir. Bağlantı olmaması eklentinin kurulmadığının kesin kanıtı olarak sunulmaz (kapalı/ulaşılamıyor da olabilir). Bağlı olmak kayıt yapıldığı anlamına gelmez; güncel atıf durumu ayrıca görünür.

Metin 11 punto, başlık 18 punto; kenar boşlukları azaltıldı, izleme/kullanıcı durumları aynı satıra alındı. Dar pencerede tablolar minimum okunur alanını koruyup kaydırılır; başlangıç boyutu çalışma alanına sığdırılır. %100/%150 geometri testleri ve sentetik bitmap'ler başarılı; gerçek OS DPI testi bekliyor. İzin, PIN, startup, oyun ve kalıcılık mantığı korunur; ek SQL/sekme polling'i yoktur. İlk yeni testteki erken odak bildirimi test kurulumunda düzeltildi; son iki yapılandırmanın tamamı başarılıdır.

Yeni UI regresyonu uygulama süresi varken eklentisiz boş listeyi, sıfır site süresini, bağlantı açıklamalarını ve bağlantı sonrası boş açıklamanın kalkmasını sınar. Önceki kaynak ölçümleri 8 Kasım'a aittir; 9 Kasım yazı/yerleşim değişikliği sonrasında performans yeniden ölçülmedi. Önceki sayılar yeni UI'nin performans kabulü olarak sunulmaz. Self-contained paket yeni arayüzle yeniden üretildi; kişisel Program/DB başlatılmadı.
## Değiştirilen dosyalar

Bu teslimde değişen/yeni 64 kaynak ve belge dosyası (yayın ve test çıktıları Git dışında):

- `.gitignore`
- `docs/architecture-1.2.1.md`
- `docs/architecture.md`
- `docs/performance-2.0/measurements.json`
- `docs/performance-2.0/verification.json`
- `docs/privacy-1.2.1.md`
- `docs/privacy.md`
- `docs/task-2-browser-installation.md`
- `docs/task-2-manual-tests.md`
- `docs/task-2-report.md`
- `docs/testing.md`
- `extensions/build.mjs`
- `extensions/identity.json`
- `extensions/package-lock.json`
- `extensions/package.json`
- `extensions/popup.css`
- `extensions/popup.html`
- `extensions/src/background.ts`
- `extensions/src/platform.ts`
- `extensions/src/popup.ts`
- `extensions/src/privacy.ts`
- `extensions/src/protocol.ts`
- `extensions/tests/extensions.test.mjs`
- `extensions/tsconfig.json`
- `FamilyMonitor.sln`
- `README.md`
- `scripts/Install-NativeHosts.ps1`
- `scripts/Publish-Prototype.ps1`
- `src/FamilyMonitor.Agent/BrowserPages.cs`
- `src/FamilyMonitor.Agent/ConsentDialog.cs`
- `src/FamilyMonitor.Agent/FamilyMonitor.Agent.csproj`
- `src/FamilyMonitor.Agent/MainForm.cs`
- `src/FamilyMonitor.Agent/ParentPinUi.cs`
- `src/FamilyMonitor.Agent/Program.cs`
- `src/FamilyMonitor.Agent/UserDialogs.cs`
- `src/FamilyMonitor.Core/ActivityTracker.cs`
- `src/FamilyMonitor.Core/Browsers/BrowserCorrelator.cs`
- `src/FamilyMonitor.Core/Browsers/BrowserModels.cs`
- `src/FamilyMonitor.Core/Browsers/BrowserPrivacy.cs`
- `src/FamilyMonitor.Core/Browsers/BrowserTimeline.cs`
- `src/FamilyMonitor.Core/Browsers/NativeHostIdentity.cs`
- `src/FamilyMonitor.Core/Browsers/NativeMessageProtocol.cs`
- `src/FamilyMonitor.Core/Security/ParentalPinService.cs`
- `src/FamilyMonitor.Infrastructure/Browsers/BrowserDatabaseUpgradeService.cs`
- `src/FamilyMonitor.Infrastructure/Browsers/BrowserPipeServer.cs`
- `src/FamilyMonitor.Infrastructure/Browsers/BrowserRuntime.cs`
- `src/FamilyMonitor.Infrastructure/Browsers/SqliteBrowserActivityStore.cs`
- `src/FamilyMonitor.Infrastructure/BufferedActivityStore.cs`
- `src/FamilyMonitor.Infrastructure/DatabaseUpgradeService.cs`
- `src/FamilyMonitor.Infrastructure/FamilyMonitor.Infrastructure.csproj`
- `src/FamilyMonitor.Infrastructure/LocalSettingsStore.cs`
- `src/FamilyMonitor.Infrastructure/Security/LocalPinStateStore.cs`
- `src/FamilyMonitor.Infrastructure/SqliteActivityStore.cs`
- `src/FamilyMonitor.Infrastructure/SqliteSchema.cs`
- `src/FamilyMonitor.Infrastructure/Windows/AutoStartService.cs`
- `src/FamilyMonitor.NativeHost/FamilyMonitor.NativeHost.csproj`
- `src/FamilyMonitor.NativeHost/Program.cs`
- `tests/FamilyMonitor.Tests/Browsers/BrowserCoreTests.cs`
- `tests/FamilyMonitor.Tests/Browsers/BrowserStorageTests.cs`
- `tests/FamilyMonitor.Tests/Browsers/PinAndStartupTests.cs`
- `tests/FamilyMonitor.UiTests/BrowserPipeTests.cs`
- `tests/FamilyMonitor.UiTests/BrowserUiTests.cs`
- `tests/FamilyMonitor.UiTests/GamePagesTests.cs`
- `tools/FamilyMonitor.PerformanceHarness/Program.cs`
