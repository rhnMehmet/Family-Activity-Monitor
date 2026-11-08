# Testler ve doğrulama

## Görev 1.2.1 — güncel doğrulama

Mevcut 115 vaka korunur. Yeni oyun modu, ön plan/etkileşim ayrımı, XInput/deadzone, cache, tarama erteleme/backoff, WAL/backup/onay, batch, rollback, silme ve asenkron UI testleri eklendi. Son toplam ve Debug/Release çıktıları [performans raporunda](performance-report-1.2.1.md). Eski başlık assertion'ı 1.2.1 olarak güncellendi; test kaldırılmadı.

`PerformanceBehaviorTests`, `StoragePerformanceTests`, `Games/DiscoveryPerformanceTests` ve `PerformanceUiTests` yeni vakaları içerir. Sahte saat ve işlem sayısı kontrolleri deterministiktir. 10.000 SQLite kaydı/500 satır UI limiti, bloke sorgu sırasında yanıt veren Stop, gizliyken 1000 tick boyunca sıfır tablo sorgusu ve 100.000 cache erişimi sınanır. Fiziksel CPU eşiği unit-test assertion'ı yapılmadı; ham ölçüm ayrı harness'tedir.

`tools/FamilyMonitor.PerformanceHarness`, gerçek kullanıcı DB'si yerine 1000 katalog kaydı, 5000 aktivite ve 200 sentetik Steam manifesti kullanır. Başlangıç binarileri optimizasyon öncesi `.tools/perf-baseline` altında ayrıldı; sonuçları `docs/performance/baseline.json` ve hash dosyasında korunur. Güncel harness üretim buffered store, async UI ve oyun politikasını kullanır. Son zamanlayıcı callback'i, pencere kapanışının son taramayı iptal etmemesi için ölçüm sınırında son işi bekler. Bu, üretim UI akışında yapılan bir bekleme değildir. İlk iki ara sonuç da saklanmıştır; kıyas yöntemi ve sınırlamaları raporda açıklanır.

```powershell
.\scripts\dotnet.ps1 build tools/FamilyMonitor.PerformanceHarness/FamilyMonitor.PerformanceHarness.csproj -c Release -o .tools/perf-after '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet.ps1 .tools/perf-after/FamilyMonitor.PerformanceHarness.dll local-measurement artifacts/performance/local-measurement.json
```

Bu gerçek kaynak ölçümüdür, gerçek oyun testi değildir. CPU işlemin kendi CPU zamanından; logical I/O `GetProcessIoCounters` ile; working set/private bytes işletim sistemi sayaçlarından gelir. GPU/FPS/physical disk ölçülmedi. Windows 8 saat kabul ve A/B/C FPS planı [ayrı belgede](manual-performance-1.2.1.md). `scripts/Measure-MonitorProcess.ps1` gerçek bilgisayarda kullanıcı tarafından seçilen PID'nin kaynak sayaçlarını CSV'ye alır; parser ve yalnızca araç PowerShell PID'sinde 5 saniyelik smoke kontrolü yapıldı, üretim kullanıcı süreci bu görevde ölçülmedi.

Aşağıdaki 1.2/1.1 sonuçları önceki sürümlerin arşiv sonuçlarıdır; güncel yayın `artifacts/windows-1.2.1`'dir.

## Görev 1.2 — güncel sonuçlar (8 Kasım 2026)

Önceki 54 senaryo korunmuştur. **61 yeni vaka; toplam 115 başarılı, 0 başarısız, 0 atlanan**, Debug ve Release için ayrı ayrı. `FamilyMonitor.Tests`: 100; `FamilyMonitor.UiTests`: 15. Her iki build 0 uyarı/0 hata; restore ve Windows x64 self-contained publish başarılı. Güncel dosya: `artifacts/windows-1.2/FamilyMonitor.Agent.exe`, sürüm 1.2.0.0.

| Yeni test grubu | Vaka |
|---|---:|
| LibraryProviderTests: rastgele Steam/Epic adları, ikinci disk/kütüphane, yeni/kaldırılan oyun, bozuk/erişilemeyen kaynak, Xbox paket/game config ayrımı, Java/Bedrock, kullanıcı adayları, bağımsız metadata, tam disk ve XML güvenliği | 17 |
| GameDetectorTests: metadata adı, aynı süreç/farklı yol, launcher ve ortak runtime, platform çakışması, manuel eşleştirme, yol sınırları, PFN, kaldırılan oyun ve Java belirsizliği | 14 |
| GameIntegrationTests: ayrı izin/iptal, kaynak yalıtımı, periyodik yenileme, tek sayaç, foreground/background/kapanış, kalıcılık/manuel adlar, günlük/haftalık toplam, geçmiş silme, v1 migration/eski yazım uyumu, yabancı anahtar rollback, eski ayarlar ve yol/paket izin kapısı | 22 |
| GamePagesTests: yeni iki sekme, ayrı izin/ret/geri çekme, genel iznin geri çekilmesi, toplam/geçmiş/filtre, %100/%150 geometri + yazı ve yenileme düğmesi | 7 |
| NativeGameApiTests: yalnızca kendi test hostunda exe/paket Win32 çağrıları ve geçersiz süreç | 1 |
| **Yeni toplam** | **61** |

Sahte dosya sisteminde isimler/kurulum kökleri/çalıştırıcı adları GUID ile üretilir. C:/D:/E: kütüphaneleri bellekte simüle edilir; gerçek disklere sahte oyun yazılmaz. SQLite ve UI testleri benzersiz geçici klasör kullanır. Kişisel `%LOCALAPPDATA%/FamilyActivityMonitor` açılmaz; gerçek oyun kütüphanesi veya Appx envanteri sorgulanmaz. Win32 testi sadece başlattığımız test sürecinin bilinen yolunu doğrular; foreground veya başka kullanıcı süreçleri okunmaz.

Migration testinde eski dokuz sütunlu gerçek SQLite dosyasına kayıt eklenir, yeni sürümde kayıt kimliği/içerik/süre korunur, oyun kataloğu eklenip yeniden açılır ve eski biçimde dokuz değerli INSERT'in hâlâ çalıştığı doğrulanır. Tek kullanım kaydı için bir oyun bağlantısı bulunur; toplamın iki kez sayılmadığı gerçek sorgularla test edilir.

Ana sekmeler ve %100/%150 simülasyon renderları incelendi: `TestResults/task-1.2-ui-debug` ve `task-1.2-ui-release` altında `games-100.png`, `games-150.png`, `game-statistics.png`. Fiziksel DPI değişikliği, gerçek launcher/oyun açılışı, paket kurma/kaldırma ve canlı oyun oynamak bu otomatik testlere dahil değildir. Gerçek Appx/manifest sorgusunun makinedeki oyun kapsamı için manuel kabul gereklidir.

İlk turda erişilemeyen Steam kaynağında FileExists'in try sınırı dışında olduğu görüldü ve bir test başarısız oldu. Çağrı kaynak bazlı hata sınırına taşındı; aynı test ve tüm paket son turda geçti. İlk derlemede kullanılmayan Xbox sağlayıcı parametresi uyarısı hata verdi; gereksiz parametre kaldırıldı. Başarısız ilk denemeler son geçerli sonuç gibi sunulmadı.

### Görev 1.2 komutları

```powershell
.\scripts\dotnet.ps1 restore FamilyMonitor.sln --configfile NuGet.Config --disable-parallel '-m:1'
.\scripts\dotnet.ps1 build FamilyMonitor.sln --no-restore '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet.ps1 build FamilyMonitor.sln -c Release --no-restore '-m:1' '-p:UseSharedCompilation=false'
$env:FAMILYMONITOR_UI_ARTIFACTS = "$PWD\TestResults\task-1.2-ui-debug"
.\scripts\dotnet.ps1 test FamilyMonitor.sln --no-build --no-restore '-m:1' --logger 'trx;LogFilePrefix=task-1.2-final-debug' --results-directory TestResults
$env:FAMILYMONITOR_UI_ARTIFACTS = "$PWD\TestResults\task-1.2-ui-release"
.\scripts\dotnet.ps1 test FamilyMonitor.sln -c Release --no-build --no-restore '-m:1' --logger 'trx;LogFilePrefix=task-1.2-final-release' --results-directory TestResults
.\scripts\dotnet.ps1 restore src/FamilyMonitor.Agent/FamilyMonitor.Agent.csproj --configfile NuGet.Config --disable-parallel '-m:1' -r win-x64 '-p:SelfContained=true'
.\scripts\dotnet.ps1 publish src/FamilyMonitor.Agent/FamilyMonitor.Agent.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/windows-1.2 '-m:1' '-p:UseSharedCompilation=false'
```

Teslim sonuçları `TestResults/task-1.2-delivery-*.trx` içindedir. Aynı 115 vaka iki yapılandırmada çalıştırılmıştır; 230 farklı test değildir. Yeni kabul adımları ve sınırlar [Görev 1.2 raporunda](task-1.2-report.md) verilmiştir. Tam paket Windows'ta çalıştırılmalıdır: sahte çoklu disk sağlayıcı testleri Windows yol semantiği kullanır. Başka sistemde platformdan bağımsız alt küme için `--filter 'FullyQualifiedName!~FamilyMonitor.Tests.Games.LibraryProviderTests'` uygulanabilir; bu teslimde Linux/macOS testi yapılmadı.

## Görev 1.1 — önceki sonuçlar

**Mevcut 41 test değiştirilmeden korundu; 6 çekirdek ve 7 WinForms test vakası eklendi. Toplam 54 test: Debug ve Release için ayrı ayrı 54 başarılı, 0 başarısız, 0 atlanan.** Her iki derleme 0 uyarı/0 hata ile tamamlandı. Runtime içeren güncel yayın `artifacts/windows-1.1` klasöründedir. Ayrıntılı değerlendirme: [Görev 1.1 raporu](task-1.1-report.md).

Yeni `IdleThresholdTests` varsayılan 180 saniyeyi, 5 saniyelik eşiği ve 15 saniye girdisizliği, dönüşü, eşik düşürme/artırmayı, geçersiz değer ve duraklatılmış halde değiştirmeyi sınar. `FamilyMonitor.UiTests` STA iş parçacığında gerçek WinForms kontrollerini kullanır: sayı alanı → kaydet düğmesi → ayar dosyası → tracker → etiketler; silme düğmesi → sahte onay/ret → gerçek geçici SQLite → listeler/toplam → yeniden açılış. Ayar dosyasının silme öncesi/sonrası byte içeriğinin aynı olduğu doğrulanır.

Testler `Program.Main` girişini kullanmaz, `%LOCALAPPDATA%/FamilyActivityMonitor` yolunu açmaz ve gerçek kullanıcı DB'sini silmez. Kaynak sahte, DB/ayarlar `%TEMP%/FamilyMonitor.UiTests/<rastgele GUID>` altında benzersizdir. Sistem tepsisi, canlı timer ve Windows bildirim abonelikleri UI testlerinde kapalıdır. Onay sözleşmesi gerçek düğme üzerinden test edilir; gerçek MessageBox'a fiziksel tıklama ayrı manuel kontroldür.

%100/%150 için kontrol geometrisi ve yazı boyutu programatik ölçeklendi; minimum pencere boyutunda silme/eşik kontrolleri ve özet etiketleri ebeveyn görünür alanı içinde kaldı. `DrawToBitmap` renderları incelendi: `TestResults/ui-debug/settings-100.png`, `settings-150.png`, `idle.png` ve Release karşılıkları. Bu, Windows görüntü ayarını değiştirerek yapılan fiziksel DPI testi değildir. Önceki yerleşimdeki silme düğmesinin kırpılması başlangıçtaki %100/%150 geometri testinde yeniden üretilemedi; DPI kesin kök neden olarak doğrulanmadı.

Sayı alanına 5 yazıp kaydetmeden bırakılırsa 180 uygulanmaya devam eder ve “Henüz uygulanmadı” gösterilir. Kaydedildiğinde ana ekrandaki eşik 5 olur. “İzleme: Açık” ve “Kullanıcı durumu: Hareketsiz” aynı anda doğru durumdur: sistem açık, fakat idle süresi birikmez.

Gerçek `NativeWindowsActivityApi.GetIdleTime` metodu ayrıca 15 saniyelik tanılama probuyla çağrıldı. Hareketsizlik süresinin arttığı, 180 saniyeyi aştığı ve sonraki girdide sıfıra yaklaştığı gözlendi. Bu prob sadece idle süresini okudu; uygulama/süreç/tuş içeriği veya DB kullanmadı. Gerçek zamanlı prob xUnit sayısına dahil değildir. Gerçek kilit/uyku, fiziksel %100/%150 ve kullanıcının ilk şikâyetinin aynı koşullarda yeniden testi henüz tamamlanmadı; süre hesabı arızası yeniden üretilemedi.

Yeni test projesinin ilk derlemesinde Windows'un `System.ApplicationIdentity` adı ile model adı çakıştı; test projesindeki alias ile düzeltildi. Kısıtlı ağda NuGet güvenlik denetimi NU1900 verdi; denetim kapatılmadan normal erişimle restore tekrarlandı ve geçti. Başarısız ilk denemeler son sonuç olarak sunulmadı.

### Görev 1.1 komutları

```powershell
.\scripts\dotnet.ps1 restore FamilyMonitor.sln --configfile NuGet.Config --disable-parallel '-m:1'
.\scripts\dotnet.ps1 build FamilyMonitor.sln --no-restore '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet.ps1 build FamilyMonitor.sln -c Release --no-restore '-m:1' '-p:UseSharedCompilation=false'
$env:FAMILYMONITOR_UI_ARTIFACTS = "$PWD\TestResults\ui-debug"
.\scripts\dotnet.ps1 test FamilyMonitor.sln --no-build --no-restore '-m:1' --logger 'trx;LogFilePrefix=task-1.1-final-debug' --results-directory TestResults
$env:FAMILYMONITOR_UI_ARTIFACTS = "$PWD\TestResults\ui-release"
.\scripts\dotnet.ps1 test FamilyMonitor.sln -c Release --no-build --no-restore '-m:1' --logger 'trx;LogFilePrefix=task-1.1-final-release' --results-directory TestResults
```

UI testleri Windows gerektirir; diğer sistemlerde yalnızca `tests/FamilyMonitor.Tests/FamilyMonitor.Tests.csproj` hedeflenmelidir. Test kaynakları `Thread.Sleep`/gerçek bekleme kullanmaz. STA test sonlandırma zaman aşımı yalnızca takılan UI testini yakalamak içindir.

Güncel yayın komutları aşağıdaki eski görev komutlarıyla aynıdır; çıkış dizinini `artifacts/windows-1.1` olarak değiştirin. Kullanıcıdan onaysız gerçek uygulamayı kapatan veya kişisel DB'yi temizleyen bir test/başlatma betiği eklenmedi.

## Görev 1 — önceki kontroller (7 Kasım 2026)

Ortam Windows 11 25H2 x64 (26200.9711), .NET SDK 10.0.403 ve çalışma zamanı 10.0.14. Başlangıçta SDK yoktu; Microsoft'un resmi betiğiyle yönetici kurulumu gerektirmeyen `.tools/dotnet` kurulumu yapıldı. CIM sorgusu erişim kısıtı nedeniyle çalışmadı; Windows sürümü kayıt defteri ve .NET runtime bilgisiyle doğrulandı. Kayıt defterindeki eski “Windows 10” ürün adı sürüm belirlemek için kullanılmadı.

- `dotnet restore`: başarılı; SQLite ve xUnit bağımlılıkları NuGet.org'dan proje içi önbelleğe indirildi.
- Debug build: başarılı, 0 uyarı, 0 hata.
- Release build: başarılı, 0 uyarı, 0 hata.
- xUnit: **41 başarılı, 0 başarısız, 0 atlanan**, Debug ve Release için ayrı ayrı. Gerçek bekleme yok.
- Sonuç dosyaları `TestResults/` altında, Git dışında tutulur.
- Gerçek Agent süreci açıldı. Windows pencere listesinde ana pencere ve “Bilgilendirme ve açık izin” penceresi gözlendi. Bilgisayar kullanımı ekran yakalama onayı zaman aşımına uğradı; ekran görüntüsü veya piksel yerleşimi incelenmedi. Otomasyonla izleme izni verilmedi ve izleme başlatılmadı. Runtime içeren yayın ayrıca `DOTNET_ROOT` verilmeden açıldı ve ana pencere oluşturdu. Gerçek etkinlik kayıtlarının içerikleri bu kontrol için okunmadı. Test için açılan süreçler kapatıldı.

İlk derlemede `SessionSwitchEventArgs` üzerinde bulunmayan oturum kimliği varsayımı hata verdi; kendi oturumunu filtreleyen `WTSRegisterSessionNotification`/`WM_WTSSESSION_CHANGE` kullanılarak düzeltildi. İlk sandbox test denemesi yerel süreç iletişimi nedeniyle sonuç üretmedi ve iptal edildi; testler normal Windows sürecinde tekrar çalıştırılarak geçti. Bu ilk denemeler başarılı test olarak sayılmadı.

## Test kapsamı

| Test dosyası | Kapsam | Vaka sayısı |
|---|---|---:|
| ActivityTrackerTests | İzinsiz okuma engeli, açık başlatma, tek uygulama, pencere/süreç değişimi, idle başlangıç/dönüş, iki örnek arasındaki eşik geçişi, kısa okuma aralığı, kilit, kilitliyken başlama, uyku, kaçan örnek, saat düzeltmesi, negatif sayaç, duraklat/devam/durdur, izin geri çekme, geçmiş silme, sağlayıcı/depolama hataları | 22 |
| SqliteActivityStoreTests | Gerçek dosyada insert/read/upsert, yeniden açılış, Interrupted işareti, yerel gece yarısı, 23 saatlik DST günü, kalıcı silme, geçersiz süre, restart sonrası saat geri alma | 7 |
| WindowActivitySourceTests | Üç tarayıcı adı, geçersiz pencere/süreç, kapanmış/erişilemeyen süreç, güvenli masaüstü; enjekte edilmiş API | 7 |
| LocalSettingsStoreTests | Varsayılan izin yok, kalıcı izin/geri çekme, bozuk/null/eski sürüm/geçersiz eşik | 5 |
| **Toplam** | | **41** |

`FakeClock` ve `FakeSource` gerçek bekleme veya foreground pencere gerektirmez. SQLite testleri geçici benzersiz dosyalarda çalışır ve sonrasında yalnızca oluşturdukları test klasörlerini siler. Native Windows API adaptörü otomatik testlerde gerçek kullanıcı etkinliği okumaz; sahte API sınırı test edilir.

## Komutlar

Proje kökünde:

```powershell
.\scripts\dotnet.ps1 restore FamilyMonitor.sln --configfile NuGet.Config --disable-parallel '-m:1'
.\scripts\dotnet.ps1 build FamilyMonitor.sln --no-restore '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet.ps1 test FamilyMonitor.sln --no-build --no-restore '-m:1' --logger 'trx;LogFileName=debug.trx' --results-directory TestResults
.\scripts\dotnet.ps1 build FamilyMonitor.sln -c Release --no-restore '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet.ps1 test FamilyMonitor.sln -c Release --no-build --no-restore '-m:1' --logger 'trx;LogFileName=release.trx' --results-directory TestResults
```

PowerShell betiğine `-m:1` ve `-p:...` seçenekleri tek tırnakla bütün argüman olarak verilir. Tek MSBuild düğümü ve paylaşımsız derleyici, bu geliştirme ortamındaki süreç kısıtlarını aşmak içindir; normal bir makinede şart değildir. Test çalıştırıcısı için kısıtlı ortamdaki yerel IPC izni de gerekir.

Runtime içeren dağıtım:

```powershell
.\scripts\dotnet.ps1 restore src/FamilyMonitor.Agent/FamilyMonitor.Agent.csproj --configfile NuGet.Config --disable-parallel '-m:1' -r win-x64 '-p:SelfContained=true'
.\scripts\dotnet.ps1 publish src/FamilyMonitor.Agent/FamilyMonitor.Agent.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/windows '-m:1' '-p:UseSharedCompilation=false'
```

Windows dışındaki CI için çekirdek testleri:

```text
dotnet test tests/FamilyMonitor.Tests/FamilyMonitor.Tests.csproj
```

Agent WinForms arayüzü ve gerçek Win32 çağrıları Windows gerektirir. Platform bağımsız testlerin başka bir işletim sisteminde çalıştırıldığı bu teslimde iddia edilmez.

## Windows'ta yapılacak manuel kabul testleri

Aşağıdaki adımlar henüz fiziksel Windows etkileşimiyle tamamlanmadı. Ayrı bir test Windows hesabı önerilir. Kendi açık izniniz dışında kimsenin etkinliğini izlemeyin.

1. `artifacts/windows/FamilyMonitor.Agent.exe` dosyasını normal kullanıcı olarak açın. İlk açılış ekranı Türkçe olmalı; kutu boş, “İzin ver” pasif olmalı. “Şimdi değil” seçin. Birkaç uygulamaya geçin: günlük toplam sıfır kalmalı. Hiç izin verilmediyse DB'de etkinlik olmamalı.
2. “İzlemeyi başlat” ile izin ekranını açın. Açıklamayı okuyup kendi test hesabınız için izin verin. Ayrı başlatma eyleminin ardından “İzleme: Açık” görünmeli. Yeniden açılışta durum yine kapalı olmalı.
3. Not Defteri'nde yaklaşık 20 saniye etkileşim kurun, ardından Chrome/Edge/Firefox'a geçin. Her biri yalnızca uygulama adı/süreç adı olarak listelenmeli. Farklı sekmelerin URL, başlık veya içeriği hiçbir yerde görünmemeli. Aynı süreçte farklı pencereye geçince dönem ayrılmalı.
4. Son bir girişten sonra varsayılan 180 saniyeyi aşacak şekilde bekleyin. “Hareketsiz” görünmeli. Son girdiden sonraki boşluk toplamda bulunmamalı. Birkaç yeni girdi sonrası yeni dönem oluşmalı; idle aralığı doldurulmamalı. Hızlı test için eşiği 5 saniye yapıp sonunda 180'e geri alın.
5. Duraklatın, uygulamalar arasında gezinin, devam edin. Duraklatılan aralık sayılmamalı. Tamamen durdurun: tick'ler yeni kayıt üretmemeli.
6. İzleme açıkken Windows oturumunu kendi yönteminizle kilitleyin, bir dakika bekleyip açın. Kilit aralığı hariç kalmalı. Duraklatılmışken kilitle/aç, hâlâ duraklatılmış kalmalı. Başka Windows kullanıcı oturumunun olayları bu izlemeyi açmamalı.
7. İzleme açıkken bilgisayarı uyutun ve uyandırın. Uyku süresi sayılmamalı. Uyandırma sonrasında yeni dönem başlamalı. Aynı işlemi duraklatılmış halde tekrarlayın.
8. Tepsiye küçültün. Simge Windows'un görünür/taşma alanında bulunmalı; menüde durum, duraklat, durdur, pencereyi göster ve çıkış çalışmalı. Ana pencere X düğmesiyle tamamen kapanmalı; gizli süreç kalmamalı.
9. Ayarlardan izni geri çekin. İzleme hemen kapalı olmalı; yeni uygulama/süre kaydı eklenmemeli. Mevcut geçmiş korunmalı. Yeniden izleme açık izin ve başlatma gerektirmeli.
10. Güncel 1.1 sürümünde “Kullanım geçmişini sil” uyarısını onaylayın. Toplam ve listeler sıfırlanmalı, izleme durmalı. Programı yeniden açınca veri geri gelmemeli; cihaz kimliği ve izin ayarı korunmalı.
11. Kayıt oluşturup normal çıkın ve yeniden açın: geçmiş aynı kalmalı, izleme kapalı olmalı. Ani sonlandırmadan sonra doğrulanmış kayıtlar korunmalı ve kesintiye uğramış dönem işaretlenmeli.
12. Hızla kapanan uygulamalar veya erişilemeyen yükseltilmiş bir süreçle deneyin: çökmemeli, okunamayan aralık başka uygulamaya eklenmemeli. Veri klasörü erişilemiyorsa anlaşılır hata ve durdurma olmalı.
13. İzleme sürerken saat ileri/geri değişimi yalnızca test makinesinde denenebilir. Negatif/çakışan süre olmamalı. Tarih seçimi, gece yarısı ve Türkçe karakterler ile %100/%150/%200 DPI yerleşimini denetleyin.
14. Aynı kullanıcı için ikinci örneği açın: ikinci örnek kayıt toplamadan kapanmalı. İnternet bağlantısı kapalıyken tüm yerel özellikler çalışmalı. Runtime içeren yayını .NET kurulmamış başka Windows 11 x64 makinesinde ayrıca deneyin.

## Bilinen sınırlar

- Yaklaşık bir saniyelik örnekleme, arada açılıp kapanan pencereleri kaçırabilir; kullanıcı etkileşimi son uygulamaya örnekleme sınırında atanır.
- Süre, son girdiye kadar doğrulanır. Çıkış/kilit/idle öncesi girdisiz kuyruk bilerek atılır; medya/okuma için ayrı politika yoktur.
- 5 saniyeden uzun örnekleme boşluğu ölçülmez. Geçmişi her saniye sorgulayan senkron UI, büyük veritabanlarında yavaşlayabilir; ekranda en son 500 satır gösterilir, toplamlar tüm günü kapsar.
- SQLite şifrelemesi, otomatik saklama süresi, installer, kod imzası ve otomatik güncelleme yoktur. Veri kaybına karşı yedekleme/senkronizasyon eklenmedi.
- `Default` dışındaki özel masaüstlerinde ölçüm yapılmaz. RDP/çoklu kullanıcı, gerçek fiziksel kilit/uyku ve yüksek DPI kabul testleri tamamlanmadı.
- Saat düzeltmelerinde monoton zaman çizelgesi korunur; duvar saatinin anında yeniden eşitlenmesi yapılmaz. Restart da önceki kayıt sınırından geriye gitmez.
- Gerçek Windows API uçtan uca izleme testi otomasyonla yapılmadı. İzin ekranına otomasyonla onay verilmedi; sahte sağlayıcı testleri manuel kontrolün yerine geçmez.
- Kendi runtime'ını içeren yayın henüz temiz ikinci bir Windows bilgisayarında denenmedi.
