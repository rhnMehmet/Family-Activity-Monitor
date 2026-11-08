# Görev 1–1.2.1 mimarisi

## 1.2.1 çalışma politikası

Üretimde `Program`, mevcut DB için açık onay ve doğrulanmış SQLite yedeğinden sonra `DatabaseUpgradeService` ile şema 3'e geçer. Temel depo yapıcısının v1→v2 davranışı eski test/araç uyumu için korunur; üretim başlangıcı bu yapıcıdan önce yükseltme kapısını çalıştırır. Dokuz sütunlu aktiviteler korunur; `game_foreground_periods` ve ölçüm başlangıç bilgisi eklenir. Gerçek kullanıcı DB'si geliştirme/test sırasında açılmaz.

`ActivityTracker` hâlâ UI iş parçacığında saniyelik örnekler ve kontrol sınırları alır. `GameForegroundTracker` girişten bağımsız ön plan aralıklarını üretir; aynı pause/lock/sleep/gözlem boşluğu sınırları kullanılır. Üretimde `GameActivityResolver → GamepadActivitySource` sonucu, yalnızca güvenilir oyunda XInput gözlemiyle birleştirilir. Kontrolcü ham durumu saklanmaz. `WindowActivitySource` pencere/PID/izin değişiminde veya 5 saniyelik TTL sonunda metadata okur; idle/desktop/pencere/PID her örnekte kontrol edilir.

`BufferedActivityStore` UI'de yalnızca sınırlı sözlüğü günceller. Düşük öncelikli tek writer thread yaklaşık 5 saniyede bir active+foreground kayıtlarını tek transaction ile yazar; tamamlanan dönemler erken yazım ister. Kuyruk toplam 1024 kayıt, yazılmış aktif dönem cache'leri 128'er kayıtla sınırlıdır. Günlük okumalar commit edilmiş toplamın üzerine bekleyen sürüm farkını koyar; aynı UUID iki kez sayılmaz. Normal kapanış flush yapar. Ani çökmede son commit edilmemiş bölüm kaybolabilir; OS/depolama gecikmesi nedeniyle 5 saniye kesin üst sınır değildir. Hata kuyrukta veriyi tutar, UI toplama işlemini durdurur; worker kontrollü yeniden deneme yapar.

Üretim SQLite bağlantıları WAL, FULL synchronous ve uygulama kontrollü checkpoint kullanır. Katalog ve writer bağlantıları pooling kullanır; SQL okumaları arka plandadır. Silme tek transaction içinde aktif/ön plan/link kayıtlarını temizler; hata olursa bekleyen kuyruk bırakılır. VACUUM/WAL truncate daha sonra yapılır. Ayrı upgrade yedekleri silinmez. Oyun bitişinden sonra keşif için 15, bakım için 45 saniyelik aralık vardır. `HeavyWorkCoordinator` tarama ve bakımı birlikte çalıştırmaz; normal kayıt commit'leri bu koordinatörde beklemez.

`GameModePolicy` güvenilir foreground oyunuyla açılır. Idle oyun ön plan metriğini kesmez. Pause tüm sayaçları keser; mod önceden açıksa tarama engeli tedbir olarak korunur. Stop/lock/sleep modu kapatır. İzleme kapalıyken foreground okunmadığından oyun modu algılanamaz. Kullanıcının ayar tercihi ayrı saklanır.

`MainForm` durum etiketlerini saniyelik tutar. Üretim sorguları tek uçuşlu, düşük öncelikli `Task.Run` üzerinde; sekme/tarih nesilleri eski sonuçları düşürür ve tek bekleyen yeniden okuma birleştirilir. Gizli/minimize pencerede tablo sorgusu yoktur; görünür seçili sekme normalde 5, oyunda 15 saniyede okunur. SQL history sınırı 500'dür. Eski UI olay zinciri testlerinin senkron test modu korunur; yeni asenkron testler üretimin okuma yolunu ayrıca sınar. Tepsi/WTS/güç entegrasyonu yalnızca normal üretim başlatmasında açıktır.

Üretimde oyun izni sonrası katalog yüklemesi `GrantConsentAsync` ile yapılır. İstek nesli, yükleme sürerken çekilen iznin sonradan açılmasını engeller. Manuel katalog sorgusu/kayıt sonrası reload arka plandadır. Atomik tek girdilik çözümleme cache'i, isabet durumunda tarama SQL commit kilidini beklemez; katalog değişince bırakılır.

Keşif üretimde saatliktir. İzinle kurulan, en fazla 64 adet recursive olmayan metadata dizin izleyicisi değişiklikleri 5 saniyelik debounce'a dönüştürür; otomatik kontrol dakikalıktır. İzlenmeyen kaynaklarda saatlik/manuel yedek mekanizması vardır. Oyun başladığında devam eden taramaya iptal gönderilir. Kaynak hatalarında 1 dakika backoff; diğer kaynaklar bağımsızdır. `CachingGameFileSystem` 8 MiB payload/1024 entry LRU, boyut+mtime damgası ve 10 dakika TTL kullanır; dosya içerik hash'i yoktur. Katalogdaki değişmeyen kurulumlar SQL'e gönderilmez. Son foreground çözümleme sonucu tek girdilik cache'dedir; katalog değişince geçersizleşir.

`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` değerlendirildi. [Microsoft belgesi](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook) mesaj döngüsü ve callback yaşam döngüsü gereksinimlerini açıklar. Idle/gamepad örneklemesini kaldırmadığından ve bu ortamda olay yaklaşımının ek kazancı/güvenilirliği A/B testle kanıtlanmadığından kullanılmadı; çalışan 1 Hz örnekleme ve metadata cache korundu. Olay hook'unun daha yavaş olduğu iddia edilmiyor.

Aşağıdaki bölümler ilk sürümlerin tasarım gerekçelerini de içerir; 1.2.1 kalıcılık, tarama sıklığı ve ön plan metriğinde yukarıdaki bölüm geçerlidir. Güncel ölçümler [performans raporunda](performance-report-1.2.1.md).

## Bağımlılıklar

`Agent → Core`, `Agent → Infrastructure → Core`. Core hiçbir Windows, UI, veritabanı veya ağ bağımlılığı içermez. Infrastructure `net10.0` hedefler; Win32 adaptörleri, `WindowsGameLocations` ve `WindowsPackageInventory` platforma bağlıdır ve `SupportedOSPlatform` ile işaretlenmiştir. Ayrıntılı oyun mimarisi: [games.md](games.md).

| Parça | Sorumluluk |
|---|---|
| `ActivityRecord`, `ApplicationIdentity`, `ActivitySample` | Sınırlandırılmış uygulama verisi ve UTC zaman modeli |
| `ActivityTracker` | Tek iş parçacıklı izin/başlat/duraklat/idle/kilit/uyku/hata durum makinesi |
| `IClock`, `SystemClock` | UTC referansı ve `Stopwatch` monoton geçen süre |
| `IActivitySource`, `WindowActivitySource` | Foreground ve hareketsizliği platformdan çekirdeğe taşır |
| `IWindowsActivityApi`, `NativeWindowsActivityApi` | Enjekte edilebilir Win32 sınırı, süreç yarışları ve erişim hataları |
| `IActivityStore`, `SqliteActivityStore` | Idempotent upsert, okuma, gün bazında bölme, silme |
| `LocalSettingsStore` | Sürümlü izin, eşik ve rastgele yerel kimlikler; atomik dosya değiştirme |
| `Program` | Kullanıcı veri dizini, tek örnek dosya kilidi, bileşenlerin kurulması |
| `MainForm` | Türkçe ekran, günlük özet, son 500 kronolojik satır, ayarlar, görünür tepsi |
| `TrackingViewState` | İzleme durumu ile kullanıcı durumunu ayrı Türkçe etiketlere dönüştürür; uygulanan eşik ve son girdi süresini gösterir |
| `IUserDialogs`, `UserDialogs` | Açık silme/izin onayı; testte sahte yanıtla gerçek UI olay zincirini doğrulama |
| `ConsentDialog` | Başlangıçta işaretlenmemiş açık izin, toplanan/toplanmayan veri açıklaması |
| `WindowsSessionNotifications` | Yalnızca kendi Windows oturumu için WTS bildirim kaydı |

## Süre hesabı ve durumlar

Görev 1.1'de `ActivityTracker` salt okunur `IdleThreshold`, `LastObservedIdleTime`, `IsSessionLocked` ve `IsSuspended` durumlarını UI'ye sunar. İş kuralları ve süre biriktirme algoritması korunmuştur. `MainForm` izlemeyi açık/kapalı/duraklatıldı, kullanıcıyı aktif/hareketsiz/kilitli/uykuda/ölçülmüyor olarak ayrı gösterir. Son girdi süresi yalnızca bellekte tutulur; DB şeması genişletilmedi.

Eşik kaydı önce yerel ayar dosyasını atomik değiştirir, ardından `UpdateOptions` çağırır. Başarısız kayıt eski eşiği değiştirmez. Sayı alanını değiştirmek tek başına ayarı uygulamaz; UI bunu açıkça belirtir. Başlangıçta ayarlardan okunan değer aynı `TrackerOptions` yoluyla kullanılır. Geçmiş silme sadece olumlu `IUserDialogs.ConfirmHistoryDeletion` dönüşüyle çalışır; ayar dosyasını değiştirmez. Başarılı silmede eski hata mesajı temizlenir.

UI düzeninde sabit yükseklikteki aksiyon satırları yerine içerikle büyüyen satırlar, DPI ölçekleme ve kaydırılabilir ayarlar kullanılır. Uygulama `ApplicationHighDpiMode=PerMonitorV2` ayarıyla başlar; [Microsoft'un proje ayarı açıklaması](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop#applicationhighdpimode). Görsel düzen testleri gerçek OS ölçek geçişinin yerine geçmez.

Windows'a özel `FamilyMonitor.UiTests`, yalnızca benzersiz geçici DB ve sahte etkinlik kaynağıyla `MainForm` oluşturur. Dahili test parametresi sistem tepsisi, WTS/güç aboneliği ve canlı zamanlayıcıyı kapatır; üretim varsayılanları açıktır. Gerçek `Program` giriş noktası testte çağrılmaz. Onay diyalogları sahte yanıt verir; kişisel izin tercihleri ve gerçek kullanıcı DB'si okunmaz/silinmez.

UI zamanlayıcısı yaklaşık saniyede bir `Tick` çağırır. Tüm kontroller ve Windows olayları UI iş parçacığına serileştirilir; aynı anda iki uygulama birikmez. `Stopped`, `Active`, `Idle`, `Locked`, `Suspended`, `Paused`, `Unavailable`, `Faulted` durumları açıkça modellenmiştir. İzin olmadan sağlayıcı okunmaz. Önceki izin saklanmış olsa bile program açılışında başlatma açıkça kullanıcıya bırakılır.

Bir dönem için başlangıç ve son girdiyle doğrulanmış bitiş tutulur. Son girdinin zamanı `şimdi − hareketsizlik` olarak hesaplanır; tuşların içeriği veya hangi tuşa basıldığı okunmaz. Eşik altındaki bekleme bir sonraki girdide doğrulanabilir. Hareketsizlik 180 saniyeye ulaşırsa son girdiden sonraki tüm bekleme atılır. Son girdiyle doğrulanmamış kuyruk duraklatma, çıkış veya kilitte de yazılmaz. Pasif video izleme uzun hareketsizlikte aktif sayılmaz; bu prototipin bilinçli ve ihtiyatlı davranışıdır.

Pencere tanıtıcısı/süreç değişince önceki dönem tamamlanır ve yeni dönem açılır. Pencere tanıtıcısı bellekte kullanılır, SQLite'a kaydedilmez. Girdiyle yeni doğrulanan sınır önceki döneme aktarılır; yeni pencere ilk gözlemlendiği andan önceye taşınmaz. Böylece dönemler çakışmaz. Geçersiz/erişilemeyen pencere boşluk oluşturur; o süre herhangi bir uygulamaya verilmez.

UTC referansı başlangıçta sabitlenir, süreler `Stopwatch` üzerinden ilerletilir. Sistem saatinin ileri/geri alınması süreyi değiştirmez. Yeniden başlatmada başlangıç en son kayıt bitişinden geriye alınmaz; duvar saati geriye alınmışsa gösterilen zaman geçici olarak gerçek saatten ileride olabilir. Saat yeniden eşitlemesi bu görevde yoktur. Beş saniyeden uzun gözlem boşluğu yeni dönem başlatır; uyku, UI blokajı veya kaçan bildirimler kullanım olarak birikmez.

## Kalıcılık ve hata davranışı

Doğrulanmış pozitif süre yaklaşık saniyede bir aynı UUID üzerinden upsert edilir. Ani kapanmada yalnızca son örnekten sonraki girdi doğrulaması kaybolabilir. Yeniden açılışta eski `Active` kayıtları `Interrupted` yapılır. İzin iptali hiçbir sağlayıcı okuması veya yeni etkinlik yazısı yapmadan bellekteki dönemi düşürür; mevcut kaydın `Active` işareti yeniden açılışta `Interrupted` olur. Duraklatma/normal durdurma kayıtları `Completed` yapar.

Zamanlar UTC tick tamsayılarıyla tutulur; süre tam olarak bitiş−başlangıçtır ve SQLite CHECK kısıtlarıyla doğrulanır. Günlük toplamlar yerel takvim gününün UTC sınırlarına göre kesilir; gece yarısı ve yaz saati değişimleri hesaba katılır. UI zamanları yerel saate dönüştürür. Şema sürümü artık **2**'dir: `SqliteSchema` transaction içinde ek tabloları oluşturur; dokuz sütunlu `activities` ve eski kayıtlar korunur. Oyun sınıflandırması `activity_game_links` ile bağlanır. Daha yeni bilinmeyen şema sürümü reddedilir; sıfırlama/downgrade yapılmaz.

Depolama/sağlayıcı istisnası toplama işlemini durdurur; yeniden başlatmak kullanıcı eylemi ister. Erişilemeyen ya da kapanan süreç yalnızca okunamayan örnek üretir. Geçmiş silinmeden önce bellek dönemi silinir ve izleme durur; eski veri yeniden yazılmaz. Aynı kullanıcı için dosya kilidi iki uygulama örneğinin ortak DB'ye yazmasını önler.

## Windows API'leri

- [`GetForegroundWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow): ön plan pencere tanıtıcısı.
- [`GetWindowThreadProcessId`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid): pencereyi süreçle ilişkilendirir.
- `Process.GetProcessById(...).ProcessName`: süreç adı; kapanma/erişim hataları tutulur. Başlık toplanmaz. 1.2'de ayrı oyun izni varsa `QueryFullProcessImageNameW` ve `GetPackageFamilyName` ile ek eşleştirme bilgisi bellekte okunur; sıradan uygulama geçmişine exe yolu yazılmaz.
- [`GetLastInputInfo`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo): sadece çağıranın oturumundaki son girdi zamanı. 32 bit sayaç taşması unsigned çıkarma ile ele alınır; olağandışı geri/future input tick değeri ihtiyatlı biçimde idle üretebilir.
- [`OpenInputDesktop`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openinputdesktop), [`GetUserObjectInformationW`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getuserobjectinformationw), `CloseDesktop`: ilk açılışta kilitli/güvenli masaüstü kontrolü. Masaüstü değiştirilmez. Yalnızca `Default` masaüstünde toplama yapılır.
- [`WTSRegisterSessionNotification`](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification), `WM_WTSSESSION_CHANGE`: kendi oturumunun kilit/açılma, bağlantı ve çıkış olayları. Kayıt başarısızsa başlatma engellenir.
- `SystemEvents.PowerModeChanged`: suspend/resume; olay UI kuyruğuna alınır. Kapatırken statik event aboneliği kaldırılır.

## Gelecek bağlantı noktaları

İleride izin kapsamı genişletilirse yeni sağlayıcılar `IActivitySource` dışında ayrı veri türleriyle eklenebilir. Yerel kayıt arayüzü bağımsızdır; gelecekte açık rızaya bağlı outbox/senkronizasyon adaptörü kurulabilir. Bu sürümde HTTP istemcisi, backend adresi, ağ servisi veya tarayıcı erişimi yoktur. Cihaz eşleştirme, kimlik doğrulama ve paylaşım izni mevcut yerel izinden ayrı tasarlanmalıdır.
