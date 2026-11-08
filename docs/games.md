# Oyun keşfi ve kullanım mimarisi — 1.2.1

1.2.1'de üretim keşif aralığı 1 saate çıkarıldı; anlamlı metadata dizin değişiklikleri kontrollü yenileme ister. Oyun modunda yeni taramalar ertelenir ve devam eden taramaya iptal gönderilir. Oyun çıkışı en az 15 saniye beklenir; sonraki dakikalık kontrolde bir yenileme çalışır. Kaynak hata backoff'u 1 dakikadır. Dosya metaverisi cache'i, değişmeyen katalog satırlarını SQL'e göndermeme, tek katalog reload'u ve son foreground eşleme cache'i eklendi. Detaylar [güncel mimaride](architecture.md) ve [performans raporunda](performance-report-1.2.1.md).

Aktif oyun süresinin yanında girişten bağımsız **ön planda kalma** metriği artık şema 3'te ayrı tutulur. XInput yalnızca güvenilir foreground oyunda etkinlik kanıtı sağlar; ham tuş/eksen değerleri saklanmaz. Bu metrik genel aktif toplama eklenmez. Eski dönemlere geriye dönük ön plan veya kontrolcü etkileşimi üretilmez. Aşağıdaki sağlayıcı sınırları korunur; eski 15 dakikalık varsayılan test/constructor davranışıdır, üretim 1 saat yapılandırır.

## Bileşenler

| Bileşen | Sorumluluk |
|---|---|
| `IGameLibraryProvider` | Bir kaynağın keşfi; önceki kayıtlar ve iptal belirteci alır |
| `GameInstallation`, `GameLibraryScan` | Platform kimliği, kurulum kimliği, kapsam, yol/exec/paket, doğrulama ve kurulum durumu |
| `GameDiscoveryService` | Ayrı izin, seri yenileme, 15 dakikalık monoton zaman planı, kaynak hata yalıtımı ve iptalden sonra yazmayı engelleme |
| `IGameDetector`, `GameDetector` | Tam manuel yol, paket aile kimliği veya katalog kurulum yolu eşlemesi; isimden oyun tahmin etmez |
| `IGameMetadataResolver`, `GameMetadataResolver` | Paylaşılan çalışma zamanı, launcher ve yardımcı süreçlerin otomatik oyun sayılmasını engelleme |
| `GameActivityResolver` | Windows örneğini zenginleştirir; normal tracker ve durum makinesi aynen kullanılır |
| `IGameCatalogRepository`, `SqliteGameCatalogRepository` | Katalog, kalıcı manuel adlar/yollar ve tek kullanım tablosundan oyun istatistikleri |
| `GameUsageService` | Yerel günlük/haftalık sınırlar; aynı oyunun dönemlerini birleştirir |
| `GamePages`, `GameConsentDialog`, `ManualGameDialog` | İki sekme, ayrı açık izin, yenileme, klasör seçimi, manuel eşleştirme ve filtreler |

Yeni platform eklemek için sağlayıcı yazılıp `Program` içindeki sağlayıcı koleksiyonuna eklenir. Core Windows veya launcherlara bağımlı değildir. Sağlayıcılar otomatik testte enjekte edilmiş dosya sistemi/paket envanteri kullanır. Üretim paketi/dependency eklenmedi.

## Platform adaptörleri

**Steam:** HKCU/HKLM'nin 32/64 bit Steam kurulum değerleri ve varsayılan Steam kökü değerlendirilir. `config/libraryfolders.vdf` ve `steamapps/libraryfolders.vdf` birleştirilir; sayısal kütüphane blokları ve eski düz yol girdileri desteklenir. Her kütüphanede `appmanifest_*.acf` içindeki appid/name/installdir/StateFlags okunur. Fully installed biti ve erişilebilir klasör birlikte değerlendirilir. Oyun kökü altındaki sınırlı exe listesi bulunur. Account/userdata/localconfig dosyaları okunmaz. Yerel client dosyaları kararlı bir çevrimiçi oyun türü API'si değildir; yazılım, DLC ve araç girişleri ayrıca kullanıcı gözden geçirmesi gerektirebilir.

**Epic:** varsayılan ProgramData ve registry AppDataPath konumlarındaki `.item` kayıtlarından AppName, DisplayName, InstallLocation ve LaunchExecutable okunur. `bIsApplication=false` ve oyun kategorisi bulunmayan açık kategori listeleri hariç tutulur; incomplete install doğrulanmamış olur. Yerel biçimin alanları için özgün parser referansı: [Legendary EGL modeli](https://github.com/legendary-gl/legendary/blob/master/legendary/models/egl.py). Bu bağımlılık olarak eklenmedi; kod kopyalanmadı. Epic biçim değişiklikleri manuel doğrulama gerektirir.

**Xbox/Microsoft Store:** Windows PowerShell 5.1'in desteklenen [Get-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/get-appxpackage?view=windows-11) ve Get-AppxPackageManifest cmdletleri PackageManager üzerinden yalnızca mevcut kullanıcı paketlerini sorgular. Script sabittir, gizli konsol penceresi açmaz; UI'de görünür izinli uygulama çalışmaya devam eder. Hedef oyun metaverisi erişilebilir `MicrosoftGame.config` ya da oyun extension bildirimiyle ayrılır; bütün Store uygulamaları oyun kabul edilmez. Config içindeki [ExecutableList](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-executablelist?view=gdk-2604) ve [ShellVisuals](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/elements/microsoftgameconfig-element-shellvisuals?view=gdk-2604) değerlendirilir. Paket envanteri aynı keşif sırasında kısa süre önbelleğe alınır. Sorgu 30 saniyeyle sınırlıdır; erişim/timeout diğer sağlayıcıları durdurmaz. Korumalı klasörler enumerate edilmez veya izinleri değiştirilmez. Bazı GDK/UWP/şifreli paketler oyun kimliği vermediği için otomatik listelenmeyebilir.

**Minecraft:** Java için varsayılan `.minecraft` ve kullanıcının belirttiği köklerde yalnızca `versions/<id>/<id>.json` mainClass ile JAR varlığı kontrol edilir. Launcher profili, hesap ve save dosyaları okunmaz. Bedrock teknik paket ailesiyle ayrı installation/platform olarak bulunur. Bu açıkça istenen sürüm adaptörüdür; genel oyun sistemi isim listesine dayanmaz. Java/JVM süreci farklı uygulamalarca paylaşılabileceği için otomatik oyun eşleştirmesi yapılmaz; belirsizlik gösterilir. Java kurulumu keşfi ile gerçek oynama oturumunun güvenilir tanınması aynı şey değildir.

**Bağımsız Windows:** mevcut kullanıcı/makine Uninstall registry kayıtlarından sadece InstallLocation okunur. Bu bilinen köklerde MicrosoftGame.config veya Source gameinfo.txt varsa oyun metaverisi oluşturulur. Metaverisiz programların tümü oyun yapılmaz.

**Kullanıcı klasörleri:** seçilmiş yerel kök ve bir alt düzey oyun klasörleri incelenir. Game config varsa ad oradan alınır; sadece exe içeren bir klasör onay bekleyen adaydır ve otomatik oyun süresi üretmez. Kullanıcı ayrı manuel eşleştirmeyle onaylayabilir. Tam disk kökü ve UNC/ağ konumu kabul edilmez.

## Kapsam ve maliyet sınırları

Steam 64 kütüphaneyle, kullanıcı klasörleri 64 kökle, bağımsız registry kurulumları 2000 kökle sınırlandırılır. Kullanıcı klasöründe bir alt düzeyden en fazla 200 aday incelenir. Bilinen kurulum kökünde exe bulma en fazla 4 alt düzey, 300 dizin ve 2000 exe bütçesi kullanır; saves/logs/userdata/.egstore altına girmez. Metadata dosyaları 2 MiB, VDF derinliği 32 ile sınırlıdır; XML DTD/external entity kapalıdır. Reparse point dosya/alt klasörler takip edilmez. Büyük/özel kütüphaneler kısmi sonuç ve uyarı üretebilir; manuel ekleme desteklenir. Genel disk araması yoktur.

Bir sağlayıcının hatası diğer sağlayıcıları durdurmaz. Tek kurulum manifesti bozuksa aynı kapsamın eksik kayıtları kaldırılmış sayılmaz. Ulaşılamayan disk ve başarısız kaynak, eski kaydı silmez; UI uyarısıyla son bilinen durum korunabilir. Metadata yok ama klasör hâlâ varsa durum Unknown'dur. Başarılı paket envanterinden kaybolma veya erişilebilir yerel diskte kurulum klasörünün kesin yokluğu NotInstalled olabilir. Hiçbir keşif katalog/geçmiş satırını fiziksel silmez.

## Foreground ve sınıflandırma

Genel takip izni ve Başlat olmadan Windows etkinlik örneği okunmaz. Ayrıca oyun izni varsa [QueryFullProcessImageNameW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-queryfullprocessimagenamew) ve [GetPackageFamilyName](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagefamilyname) sınırlı sorgu hakkıyla çağrılır. Erişim hatasında null gelir; ek yetki istenmez. Komut satırı, pencere başlığı ve içerik okunmaz.

Öncelik açık manuel tam dosya yolu, sonra paket kimliği veya bilinen kurulum yolu. Yol eşleşmesi Windows büyük/küçük harf ve dizin sınırlarına dikkat eder; benzer kardeş klasör eşleşmez. Birden fazla adayda tahmin yapılmaz. Launcher ve javaw/dotnet/python gibi ortak çalıştırıcılar tam yol kalıcı manuel eşleştirmeyle de tek oyuna bağlanmaz; bunlar genel kullanımda kalır. Java oturumunu komut satırı okumadan ayıran bir ileriki yöntem bu sürüme eklenmedi.

Sınıflandırma değiştiğinde tracker yeni dönem açar. Yalnızca foreground birikir; arka planda Steam açık diye ikinci bir sayaç tutulmaz. Oyun kapanıp foreground yok olursa dönem biter. İzin geri çekilmesi izlemeyi durdurur; yeni kaynak okumaları ve iptal edilen tarama sonuçları yazılmaz.

## SQLite ve migration

Şema sürümü 1'den 2'ye tek transaction içinde additive geçer:

- `activities`: **eski dokuz sütun aynen korunur**; kayıtlar yeniden yazılıp silinmez.
- `game_installations`: oyun/platform kimliği, kurulum kimliği, kaynak/kapsam, kurulum kökü, exe listesi, paket kimliği, kurulum ve doğrulama durumu.
- `manual_game_mappings`: tam exe yolu → kurulum kimliği, bir dosya için tek açık seçim.
- `manual_game_names`: kullanıcı adı; otomatik yeniden tarama bunu ezmez.
- `activity_game_links`: her kullanım kaydı için en fazla bir kurulum bağlantısı. Kullanım ve bağlantı aynı transaction içinde kaydedilir; yabancı anahtar kontrolü açıktır.

Eski 1.1'in dokuz değerli INSERT biçiminin çalışmaya devam ettiği test edildi. Eski uygulama aynı kullanıcı için paralel çalıştırılmaz (ortak instance lock). Eski sürümün geçmiş silmesi bağlantı bırakırsa yeni şema açılışında sadece yetim bağlantılar temizlenir. Bilinmeyen daha yeni şema reddedilir; downgrade/reset yoktur.

Günlük/haftalık oyun toplamı `activities JOIN activity_game_links JOIN game_installations` üzerinden kesilmiş UTC aralıklarını toplar. İkinci aktif süre tablosu/sayacı yoktur. Aynı oyun kimliği/platform içindeki farklı dönemler/kurulumlar birleşir; farklı platformlar ayrı kimliktir. Son oynanma MAX(end) olarak bu monitör kayıtlarından türetilir. Önceki ham süreç kayıtları geriye dönük tahminle sınıflandırılmaz. Kullanım geçmişi temizlenince genel/oyun kayıtları ve bağlantıları birlikte temizlenir; katalog, manuel adlar ve izinler korunur.
