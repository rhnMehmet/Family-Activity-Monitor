# Family Activity Monitor — 1.2.1 performans raporu

8 Kasım 2026. Kod, otomatik doğrulama ve ayrı Windows x64 yayın paketi hazırdır. **Gerçek oyun/FPS, fiziksel kontrolcü ve uzun süreli Windows kabulü bekliyor.** Bu rapordaki kaynak sayıları sentetik iş yükünü barındıran gerçek Windows sürecinden ölçüldü; gerçek oyun sonuçları değildir.

## A. Mevcut sistem analizi

1.2'de bir saniyelik UI yenilemesi görünmeyen tabloları da yeniden kuruyordu: günlük SQL toplamı, tüm günlük history'nin okunup son 500'ünün seçilmesi, katalog/son oynanma sorgusu, bugünkü/seçili dönem oyun toplamı ve oyun geçmişi. Tepsiye gizlemek bu işleri durdurmuyordu. Katalog son oynanma sorgusu kurulum başına bağlı alt sorgu kullanıyordu.

Tracker her doğrulanmış örneği ayrı SQLite transaction/bağlantısıyla yazıyor; aynı foreground uygulamanın süreç adı/yolu/paket bilgisi yeniden sorgulanıyordu. Oyun çözümlemesi değişmeyen uygulama için kataloğu tekrar değerlendiriyordu. Keşif yaklaşık 15 dakikada bir bütün sağlayıcılara dönüyor; değişmeyen dosyalar yeniden okunuyor ve aynı katalog satırları SQL'e gönderiliyordu. Eksik kurulum uzlaştırması tekrarlanan doğrusal aramayla O(n×m) çalışıyordu; kimlik kümesiyle O(n+m) yapıldı.

Üretim keşfi **1 saate** çıkarıldı. İlk açılış/manuel yenileme korunur; ayrı izinle bilinen metadata dizinleri izlenir. Değişiklikler 5 saniyelik debounce ve dakikalık zamanlayıcıyla birleştirilir. İzlenmeyen ikinci Steam kütüphaneleri, Xbox paket değişimleri ve özel konumlar için saatlik/manuel yedek mekanizması vardır. Tüm disk taraması eklenmedi.

`GetLastInputInfo`, klavye/fare için kullanışlıdır; her kontrolcünün girişini kapsadığı varsayılamaz. Bu yüzden yalnızca ekranda “aktif” yazmak yerine ön plan ve girişle doğrulanan süre ayrı modellendi.

## B. Yapılan optimizasyonlar

### CPU, RAM ve keşif

Foreground pencere/PID/masaüstü/idle kontrolü 1 Hz kaldı. Değişmeyen süreç metaverisi 5 saniye cache'lenir; pencere, PID veya izin değişimi anında yenilenir. Son oyun eşleştirmesi cache'lenir; katalog değişince geçersizleşir. Başarısız kaynak için 1 dakika backoff vardır; diğer kaynaklar çalışabilir.

Cache isabeti katalog SQL commit kilidini beklemez. Üretimde izin sonrası katalog yükleme ve manuel ekleme/eşleştirme sorguları da arka plandadır. Yükleme sırasında izin geri çekilirse eski iş izni yeniden açamaz; istek nesliyle korunur.

Dosya içeriği/listeleri için boyut+mtime damgalı, 10 dakika TTL'li LRU eklendi. Payload bütçesi 8 MiB, giriş sayısı 1024'tür; bu gerçek toplam heap boyutu garantisi değildir. İçerik hash'i hesaplanmaz. Aynı verinin SQL UPSERT'i atlanır; katalog tarama sonunda bir kez yüklenir, değişmeyen dolu kaynakta yeniden yüklenmez. Manuel adlar ve kaldırılmış oyunların geçmişi korunur.

[SetWinEventHook belgesi](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) incelendi. Hook mesaj döngüsü/callback yaşam döngüsü ister; idle ve gamepad örnekleme ihtiyacını ortadan kaldırmaz. Bu ortamda olay yaklaşımının daha verimli/güvenilir olduğu A/B testiyle kanıtlanmadığından çalışan saniyelik mekanizma değiştirilmedi. Hook'un daha kötü olduğu sonucuna varılmadı.

### SQLite ve veri güvenliği

Düşük öncelikli tek writer, aynı UUID'nin güncel sürümünü birleştirir. Yaklaşık 5 saniyede bir active+foreground kayıtları aynı transaction ile yazılır; tamamlanan dönem erken flush ister. Toplam bekleyen kuyruk 1024 kayıtla, yazılmış aktif dönem cache'leri 128'er kayıtla sınırlıdır. Kuyruk dolarsa sınırsız büyümek yerine toplama durur. Commit hatası kayıtları kuyrukta tutar; hata UI'ye yansır ve kontrollü yeniden deneme yapılır.

WAL + `synchronous=FULL` seçildi. FULL commit dayanıklılığını azaltmadan daha az transaction yapılır; NORMAL/OFF kullanılmadı. [SQLite WAL belgesi](https://www.sqlite.org/wal.html) eşzamanlı okuma/yazma ve checkpoint maliyetlerini açıklar. Aynı belgede belirtilen WAL-reset düzeltmesini içeren sürüm kullanıldığı otomatik testte gerçek yapılandırılmış bağlantıda kontrol edilir; yeni SQLite bağımlılığı eklenmedi.

**Batch dayanıklılık sınırı:** normal kapanış tüm kuyruğu flush eder. Ani süreç/OS çökmesinde son commit edilmemiş yaklaşık 5 saniyelik ölçüm kaybolabilir. Zamanlama ve disk hatalarında pencere uzayabilir; sıfır veri kaybı veya kesin 5 saniye üst sınırı iddia edilmiyor. Commit edilmiş verinin tutarlılığı SQLite transaction/FULL mekanizmasına dayanır; fiziksel güç kesintisi testi yapılmadı.

Ağır bakım oyun modunda ertelenir. Oyundan çıkıştan sonra keşif en az 15, bakım 45 saniye bekler; ağır iş koordinatörü tarama ve bakımı aynı anda çalıştırmaz. Checkpoint ihtiyaç eşiği 4 MiB'dir; oyun sırasında erteleme nedeniyle WAL büyüyebilir. Kapanış veya izinli bakım tamamlandığında normal SQLite yönetimi devam eder.

Silme, aktif/ön plan/link kayıtlarını tek transaction ile kaldırır. Başarısız silme bekleyen veriyi düşürmez. Başarılı silme kuyrukları boşaltır; sonradan yeniden yazılma engellenir. Katalog/ayar/izinler korunur. `secure_delete` açıktır; VACUUM ve WAL truncate uygun bakım aralığına bırakılır. Ayrı yükseltme yedekleri kalır ve bu durum onay diyaloğunda yazılıdır. Fiziksel kalıntıların tamamen silineceği garanti edilmez.

Mevcut DB, **açık onay → SQLite Backup API ile benzersiz yedek → quick_check → migration** sırasıyla şema 3'e geçer. Hayır'da uygulama kapanır. Dokuz sütunlu `activities`, kayıt kimlikleri, katalog, manuel eşleştirme ve ayarlar korunur. Yeni ön plan tablosu ayrı eklenir; geçmişe tahmin yazılmaz. Eski 1.2'ye dönüş için önceki yedek gerekir. **Bu çalışma sırasında gerçek kullanıcı DB'si açılmadı, silinmedi veya migrate edilmedi.**

### Arayüz ve otomatik oyun modu

Durum etiketleri saniyelik kalır; tablo sorguları arka plan görevine taşındı. Tek sorgu işi ve tek birleştirilmiş yeniden yükleme vardır. Tarih/sekme değişiminin eski sonucu UI'ye uygulanmaz. SQL history sınırı 500'dür; günlük toplam tüm veriyi kapsar. Gizli/minimize pencerede tablo sorgusu yapılmaz. Görünür seçili sekme normalde 5, oyunda 15 saniyede yenilenir; yeniden açma ve kullanıcı seçimi güncel okuma ister.

Güvenilir foreground oyununda mod otomatik açılır; izleme ve güvenli yazım devam eder. Yeni ağır tarama ertelenir; devam edene işbirlikli iptal gönderilir. Çıkışta tek bekleyen yenileme uygun zamanda çalışır. Pause iki sayacı durdurur; mod önceden açıksa ağır iş engeli korunur. Stop/kilit/uyku modu kapatır. Ayarlarda otomatik mod açılıp kapatılabilir; bu tercih izlemeyi kapatmaz. Genel izin ve ayrı oyun izni korunur.

### Gamepad ve iki süre

Ön planda kalma süresi, girişle doğrulanan aktif süreden bağımsız saklanır/gösterilir. Otomatik testte **60 saniye ön plan / 45 saniye aktif** hem depoda hem UI sütunlarında doğrulandı. Ön plan süresi genel aktif toplama eklenmez; eski kayıtta “Ölçülmedi” gösterilir.

[Microsoft XInput belgesi](https://learn.microsoft.com/en-us/windows/win32/xinput/getting-started-with-xinput) temel alınarak en fazla 4 slot örneklenir. Bağlı olmayan slotlar 5 saniye bekler. Stick deadzone/trigger eşiği uygulanır; sadece güvenilir ön plan oyununda yoklanır. Ham düğme/eksen paketi geçicidir; yalnızca var/yok etkinlik ve son zaman kullanılır. Tuş, kombinasyon veya klavye içeriği saklanmaz. Uyumsuz kontrolcü etkinliği gerçekleşmiş sayılmaz. Fiziksel cihaz testi yapılmadı.

## C. Önce / sonra karşılaştırması

Ortam: Windows derleme 26200, .NET 10.0.14, **20 mantıksal işlemci**, Release ölçüm hostu. Aynı sırada 1000 katalog kaydı + 5000 aktivite + 200 sentetik Steam manifesti kullanıldı. MainForm/Tracker/SQLite gerçek uygulama kodudur; foreground sentetik ve sürekli girdili, tepsi durumu pencere gizleme ile simüledir. Üretimin WTS/güç/native input entegrasyonu ve gerçek oyun çalıştırılmadı. Gerçek kişi/kütüphane/veri okunmadı.

Ölçülen CPU ve RAM, uygulama bileşenleriyle örnekleyiciyi birlikte barındıran sürece aittir; doğrudan paket exe'sinin gerçek oyun yükü değildir. `TotalProcessorTime / elapsed / 20 × 100` toplam CPU yüzdesidir. Tek çekirdek değeri bölme yapılmadan hesaplanır. Örneğin son idle değeri **%0,093 toplam = %1,863 tek çekirdek** karşılığıdır.

| Senaryo | Süre yaklaşık | CPU önce toplam % | CPU sonra toplam % | Sonra tek çekirdek % | Working set önce → sonra MiB |
|---|---:|---:|---:|---:|---:|
| Pencere açık, izleme kapalı | 20 s | 0,464 | 0,093 | 1,863 | 113,30 → 86,24 |
| Pencere açık, aktif takip | 25 s | 0,409 | 0,208 | 4,165 | 121,60 → 98,03 |
| Kütüphane taranırken | 25 s | 0,502 | 0,248 | 4,961 | 121,78 → 99,66 |
| Sentetik ön plan oyunu | 25 s | 0,299 | 0,081 | 1,614 | 115,64 → 100,42 |
| Gizli pencere, aktif takip | 25 s | 0,235 | 0,028 | 0,560 | 122,42 → 100,87 |
| Gizli pencere, kısa arka plan koşusu | 90 s | 0,243 | 0,030 | 0,604 | 125,63 → 100,10 |

Bellek değerleri working set ortalamasıdır; 1 MiB = 1.048.576 bayt. Son ölçümde en yüksek örnek yaklaşık **102,31 MiB** oldu. 150 MB hedefi yaklaşık 143 MiB'dir; bu sentetik örnek hedefin altındadır. Diğer donanım/kataloglar için garanti değildir.

| Senaryo | Mantıksal okuma önce → sonra MiB | Mantıksal yazma önce → sonra MiB |
|---|---:|---:|
| İzleme kapalı | 92,311 → 0 | 0 → 0 |
| Aktif/görünür | 114,106 → 0,004 | 0,623 → 0,051 |
| Tarama | 120,518 → 1,295 | 0,917 → 0,259 |
| Sentetik oyun | 116,727 → 0,031 | 0,826 → 0,157 |
| Aktif/gizli | 114,075 → 0,012 | 0,599 → 0,043 |
| 90 saniye/gizli | 388,851 → 0,012 | 2,156 → 0,145 |

Bu sayılar `GetProcessIoCounters` işlem mantıksal G/Ç toplamıdır. **Fiziksel diske giden bayt, disk etkin süre veya disk gecikmesi değildir.** OS cache/connection pooling büyük fark yaratabilir. Süreler ham dosyada tam verilmiştir; kurulum/ısınma, silme ve bakım bu tablonun dışında kalır. Kapanışın bazı son flush işleri ölçüm penceresinden sonra tamamlanabilir; tüm süreç ömrünün G/Ç toplamı olarak yorumlanmamalıdır.

Tarama stresi 25 saniyede **5 tamamlanan tarama** içerir; üretimin saatlik sıklığı değildir. İlk tarama **80,98 → 87,61 ms** ile biraz yavaşladı. Isınmış dört tarama önce **44,13–47,31 ms**, sonra **36,54–38,79 ms** aralığındadır. Önbellek tekrar okumayı azaltır; ilk tarama maliyetini yok etmez. Tek ölçüm setinden kesin yüzde kazanç veya istatistiksel güven çıkarılmadı.

Monitör bileşenleri kapalıyken 20 saniyelik sistem temel CPU'su önce **%2,77**, son ölçümde **%3,04** idi. Diğer süreçler ve frekanslar kontrol altında değildi; bu fark monitör maliyeti sayılamaz. Sistem toplam RAM'i ve fiziksel disk tabanı bu yakalamada **Ölçülmedi**.

90 saniyelik son koşuda yönetilen heap yaklaşık **2,04 → 2,65 MB**, son working set **99,57 MiB** idi. Cache/kuyruk sınırları ayrıca otomatik test edildi. Bu veriler 8 saatlik bellek sızıntısı testinin yerine geçmez. Gerçek oyun CPU/RAM etkisi, FPS, 1% low, kare süresi ve GPU: **Ölçülmedi**. Sentetik idle/oyun senaryosu hedefleri sağladı; gerçek oyun performansı henüz kabul edilmedi.

Ham dosyalar: [başlangıç](performance/baseline.json), [son ölçüm](performance/optimized.json), [ilk ara ölçüm](performance/optimized-first.json), [ikinci ara ölçüm](performance/optimized-second.json), [başlangıç binary hash'leri](performance/baseline-binaries.sha256.json). İlk iki ara koşuda son asenkron tarama pencere kapanışıyla iptal edilip 4 tarama tamamlandı; son kıyasta ölçüm sınırı düzeltilerek 5/5 karşılaştırıldı. Ara sonuçlar silinmedi/seçilerek gizlenmedi.

## D. Test sonuçları ve yayın

**Önceki 115 vaka korundu; 58 yeni vaka, toplam 173 benzersiz vaka.** Eski UI başlık assertion'ı 1.2.1'e güncellendi; test kaldırılmadı.

| Yapılandırma | Core/Infrastructure | Windows UI | Başarılı toplam | Başarısız | Atlanan |
|---|---:|---:|---:|---:|---:|
| Debug | 151 | 22 | 173 | 0 | 0 |
| Release | 151 | 22 | 173 | 0 | 0 |

İki yapılandırmada aynı 173 vaka çalışır; 346 farklı test değildir. Debug/Release build ve self-contained publish **0 uyarı, 0 hata** ile bitti. Son TRX'ler `TestResults/task-1.2.1-confirmed-*.trx` içindedir. Ara derlemede xUnit asenkron testlerin blocking Task beklemesini reddetti; testler async/await'e çevrilip yeniden derlendi. Önceki ara TRX'ler son test sayısı olarak kullanılmadı.

| Yeni grup | Vaka | Doğrulama |
|---|---:|---|
| PerformanceBehaviorTests | 23 | Mod geçişleri, pause/kilit/uyku/gap, 60/45 ayrımı, onay geri çekmede hata, pad kanıtı/deadzone/disconnect/API yokluğu, metadata cache |
| StoragePerformanceTests | 15 | Onay reddinde byte-identical DB, doğrulanmış eski sürüm yedeği, ayar/katalog koruma, future-schema ret, gerçek FULL/WAL, batch/overlay/rollback, 10.000 kayıt, queue sınırı, kapanış/çökme modeli, başarısız silme ve ertelenen WAL truncate |
| DiscoveryPerformanceTests | 13 | 2000 kayıt/10.000 çözümleme, değişmeyen SQL'i atlama, 300→301 Steam kurulumu, cache/TTL/list bütünlüğü/100.000 erişim, oyun erteleme/iptal/quiet/backoff, arka planda izin yükleme/geri çekme yarışı ve SQL commit'ini beklemeyen cache |
| PerformanceUiTests | 7 | Bloke SQL sırasında çalışan Stop, gizliyken 1000 tick boyunca sıfır tablo sorgusu, 10.000 history'den 500 satır, stale-date ret, mod tercihi, pending-read kapanışı, iki metrik sütunu |

CPU yüzdesi için kırılgan unit-test eşiği yazılmadı; süreç ölçümü ayrı yapıldı. Çökme testi commit edilmiş SQLite snapshot'ını yeniden açar; gerçek OS çökmesi/power-cut simülasyonu değildir. UI testleri sentetik kaynak/geçici DB kullanır. %100/%150 render simülasyonları `TestResults/task-1.2.1-ui-debug` ve `task-1.2.1-ui-release` altında üretildi ve ayarlar/istatistik görünümü incelendi; gerçek Windows ölçek değişimi yapılmadı.

Yeni paket: **`artifacts/windows-1.2.1/FamilyMonitor.Agent.exe`**, dosya sürümü **1.2.1.0**, başlık **Family Activity Monitor — 1.2.1**. x64 self-contained dosyalar birlikte taşınmalıdır. Eski `windows`, `windows-1.1`, `windows-1.2` yayınları korunmuştur. Kişisel profilde gerçek `Program` başlatılmadı; temiz ikinci bilgisayarda paket açılışı manuel kabul kapsamındadır.

Değişen/eklenen dosyalar, depo köküne göre:

| Alan | Dosyalar |
|---|---|
| Core | `ActivityTracker.cs`, `Models.cs`, `Games/GameModels.cs`, `Games/GameDiscoveryService.cs`, `Games/GameActivityResolver.cs`; yeni `Games/GameForegroundTracker.cs`, `Input/GamepadActivitySource.cs`, `Performance/GameModePolicy.cs` |
| Infrastructure | `SqliteActivityStore.cs`, `SqliteSchema.cs`, `LocalSettingsStore.cs`, `Windows/WindowActivitySource.cs`, `Games/GameFileSystem.cs`, `Games/SqliteGameCatalogRepository.cs`; yeni `BufferedActivityStore.cs`, `DatabaseUpgradeService.cs`, `LowPriorityWork.cs`, `HeavyWorkCoordinator.cs`, `Windows/XInputActivityProvider.cs`, `Games/SqliteGameForegroundStore.cs`, `Games/CachingGameFileSystem.cs`, `Games/GameLibraryChangeMonitor.cs` |
| Agent | `MainForm.cs`, `GamePages.cs`, `Program.cs`, `GameConsentDialog.cs`, `UserDialogs.cs`, `FamilyMonitor.Agent.csproj` |
| Test | Yeni `PerformanceBehaviorTests.cs`, `StoragePerformanceTests.cs`, `Games/DiscoveryPerformanceTests.cs`, `PerformanceUiTests.cs`; güncellenen `GamePagesTests.cs` |
| Ölçüm/belge | Yeni `tools/FamilyMonitor.PerformanceHarness/{Program.cs,FamilyMonitor.PerformanceHarness.csproj}`, `scripts/Measure-MonitorProcess.ps1`, `docs/performance/*`, bu rapor ve manuel kabul planı; güncellenen `README.md`, `docs/{architecture,games,privacy,testing}.md` |

Üretim bağımlılığı eklenmedi. GitHub'a push/commit yapılmadı; Görev 2 başlatılmadı.

## E. Gerçek bilgisayar kabul testleri

Tam sıralı adımlar ve boş sonuç tablosu: **[Windows kabul ve A/B/C FPS planı](manual-performance-1.2.1.md)**.

Önce migration reddi/onayı ve yedeği, eski veri/izinlerin korunmasını doğrulayın. Ardından normal kullanım, 5 saniye ve varsayılan 180 saniye idle, kilit/uyku/pause, tepsi ve %100/%150 ölçeği kontrol edin. Gerçek Steam/Epic kurulum/değişikliklerini, foreground/arka plan geçişini, oyun sırasında ertelenen taramayı ve normal kapanışta iki sürenin korunmasını deneyin. Kontrolcü yoksa fiziksel pad testini yapılmış saymayın.

Kaynak sayaçlarını seçtiğiniz PID'den almak için `scripts/Measure-MonitorProcess.ps1` sağlandı; CPU toplam/tek çekirdek, working set/private, handle/thread ve mantıksal G/Ç CSV'si üretir. Parser kontrolü ve yalnızca aracın kendi PowerShell PID'sinde 5 saniye/5 CSV satırı smoke testi geçti; kullanıcı üretim süreci ölçülmedi. Gerçek disk/GPU için Windows sayaçları ayrıca gerekir. 2 ve 8 saatlik tekrarları kaydedin.

FPS planı A=monitör kapalı, B=1.2.1 açık/mod kapalı, C=aynı sürüm açık/mod etkin şeklindedir. Aynı sahne/ayar ve ölçüm aracıyla en az 3×5 dakika, değiştirilmiş sıra ve ısınma turu kullanılır. Ortalama FPS, aynı tanımla 1% low, ortalama/P99 kare süresi, CPU/RAM/disk/GPU ölçülür. Fark tekrarlar arası oynaklıktan küçükse kazanç/kayıp doğrulanmış sayılmaz. Henüz hiçbir FPS hücresi ölçülmüş değer içermiyor.

## F. Bilinen sınırlamalar ve kabul kararı

- Sayılar tek Windows ortamında sentetiktir; gerçek oyun CPU<%1, FPS/kare süresi ve GPU hedefi henüz doğrulanmadı. İlk tarama gecikmesi iyileşmedi. RAM büyüklüğü gerçek kataloğa göre değişir. Sonradan eklenen izin/manuel yükleme ve cache-kilit yarış düzeltmeleri ayrı otomatik testle doğrulandı; bu kullanıcı etkileşim yolları kaynak ölçümünde çalıştırılmadı.
- Fiziksel kontrolcü yok/model belirsiz. XInput dışı DirectInput/HID/özel cihazlar doğrulanmaz; saniyelik kısa dokunuşlar kaçabilir. Bağlı pad belirli oyuna/kişiye bağlanmaz. Büyük drift veya basılı kontrol de etkileşim sayılabilir; insan dikkatinin kanıtı değildir.
- Otomatik mod ancak izinli takip ve güvenilir foreground oyunuyla açılır. JVM/özel launcher/korumalı paket kısıtları devam eder. İzleme kapalıyken veya algılanamayan oyunda mod otomatik açılamaz.
- Dizin izleyicisi recursive değildir ve en fazla 64 kökle sınırlıdır. Tüm ikincil kütüphaneler/paket kaynakları canlı izlenmez; saatlik/manuel kontrol gerekir. Boyut+mtime aynı bırakılan değişiklikler TTL süresine kadar kaçabilir. Metadata olayı kimi zaman gereksiz fakat birleştirilmiş yenileme isteyebilir.
- Uzun oyun sırasında checkpoint/bakım ertelendiğinden WAL dosyası büyüyebilir. 90 saniye uzun süreli sızıntı kanıtı değildir; 8 saat kabulü bekler. Ani çökmede son commit edilmemiş süre korunamaz; güç kesintisi dayanıklılığı fiziksel test edilmedi.
- Yükseltme yedeği geçmişin ayrı kopyasıdır ve silme düğmesiyle temizlenmez. Şifreleme, kurulum sihirbazı, kod imzası ve otomatik güncelleme eklenmedi.

**Kabul kararı:** yerel prototip kodu, 173 otomatik test, Debug/Release ve ayrı yayın hazırdır. Gerçek Windows/oyun/gamepad/uzun süre/FPS kabulü tamamlanmadan tam saha kabulü verilmemelidir. Görev 1.2.1 için kalan işler bu fiziksel kontrollerdir; Görev 2'ye geçilmedi.
