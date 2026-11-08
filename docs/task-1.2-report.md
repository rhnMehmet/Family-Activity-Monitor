# Görev 1.2 teslim raporu — 8 Kasım 2026

## 1. Oluşturulan ve değiştirilen dosyalar

Mevcut proje genişletildi; sıfırdan proje/depo oluşturulmadı. Core/Infrastructure/Agent ayrımı ve mevcut test projeleri korundu. Üretim veya test paketi bağımlılığı eklenmedi.

Yeni kaynaklar:

```text
src/FamilyMonitor.Core/Games/
  GameModels.cs
  GameContracts.cs
  GameDetector.cs
  GameDiscoveryService.cs
  GameActivityResolver.cs
src/FamilyMonitor.Infrastructure/
  SqliteSchema.cs
  Games/
    SqliteGameCatalogRepository.cs
    GameFileSystem.cs
    ValveKeyValues.cs
    GameConfiguration.cs
    SteamLibraryProvider.cs
    EpicLibraryProvider.cs
    WindowsPackageInventory.cs
    XboxLibraryProvider.cs
    MinecraftLibraryProvider.cs
    FolderLibraryProvider.cs
    StandaloneLibraryProvider.cs
    WindowsGameLocations.cs
src/FamilyMonitor.Agent/
  GamePages.cs
  GameConsentDialog.cs
  ManualGameDialog.cs
tests/FamilyMonitor.Tests/Games/
  GameTestDoubles.cs
  LibraryProviderTests.cs
  GameDetectorTests.cs
  GameIntegrationTests.cs
tests/FamilyMonitor.UiTests/
  GamePagesTests.cs
  NativeGameApiTests.cs
docs/
  games.md
  task-1.2-report.md
```

Değişen mevcut dosyalar: `Core/Models.cs`, `Core/ActivityTracker.cs`; `Infrastructure/SqliteActivityStore.cs`, `Infrastructure/LocalSettingsStore.cs`, `Infrastructure/Windows/WindowActivitySource.cs`, `Infrastructure/Windows/NativeWindowsActivityApi.cs`; `Agent/MainForm.cs`, `Agent/Program.cs`, `Agent/UserDialogs.cs`, `Agent/ConsentDialog.cs`, `Agent/FamilyMonitor.Agent.csproj`; UI test fixture ve sahte diyalog için `tests/FamilyMonitor.UiTests/MainFormTests.cs`; `README.md`, `docs/architecture.md`, `docs/privacy.md`, `docs/testing.md`. Önceki test vakaları silinmedi; UI helper isteğe bağlı oyun bileşenlerini destekleyecek şekilde genişletildi.

Yayın/test çıktıları `artifacts` ve `TestResults` altında Git dışındadır. Mevcut kişisel DB bu geliştirme/test sırasında açılmadı veya silinmedi. Yeni uygulama kullanıcı tarafından açıldığında teknik migration uygulanır.

## 2. Keşif ve kullanım mimarisi

`IGameLibraryProvider` platform kaynaklarını ayırır. `GameDiscoveryService` ayrı sürümlü izni, iptali, tek taramayı ve yaklaşık 15 dakikalık monoton aralığı yönetir. Bir kaynaktaki erişim/format hatası diğer sağlayıcıları durdurmaz. İzin geri çekildikten sonra bekleyen tarama sonucu kaydedilmez.

`IGameDetector`/`GameDetector`, `IGameMetadataResolver` ve `GameActivityResolver` mevcut foreground örneğine katalogdaki oyun kimliği/adını ekler. Süre hesabı hâlâ mevcut `ActivityTracker` içindedir. Tek bir foreground döneminden ikinci bir oyun sayacı üretilmez. Arka plana geçen ya da kapanan oyunun dönemi biter; Steam arka planda açık diye paralel süre yazılmaz.

`IGameCatalogRepository`/`SqliteGameCatalogRepository` katalog ve manuel eşleştirmeleri tutar. `GameUsageService` aynı kullanım kayıtlarının oyun alt kümesini günlük veya Pazartesi–Pazar haftalık olarak sorgular. Aynı oyun/platformun dönemleri birleşir; farklı platformların kimliği ayrıdır. Son oynanma monitör kayıtlarından türetilir.

Şema migration 1 → 2 additive ve transaction içindedir. **Eski activities tablosunun dokuz sütunu ve kayıtları korunur.** Ayrı game_installations, manual_game_mappings, manual_game_names ve activity_game_links tabloları eklenir. Eski dokuz değerli INSERT biçiminin uyumu test edildi. Kullanım ve oyun bağlantısı aynı transaction'da kaydedilir; geçersiz bağlantı activity yazısını da geri alır. Daha yeni bilinmeyen şemada sıfırlama/downgrade yapılmaz. Ayrıntı: [games.md](games.md).

## 3. Platformlara göre geliştirilenler

| Kaynak | Uygulanan davranış | Doğrulama düzeyi |
|---|---|---|
| Steam | Registry/varsayılan Steam, iki libraryfolders konumu, birden fazla disk/kütüphane, appmanifest adı/kimliği/kurulum durumu, sınırlı exe keşfi | Rastgele sahte kütüphanelerle test edildi; gerçek Steam kurulumu taranmadı |
| Epic | Varsayılan/registry manifest kökü, .item metaverisi, farklı diskte InstallLocation, LaunchExecutable, incomplete/non-game filtreleri | Sahte .item dosyalarıyla test edildi; gerçek launcher doğrulaması bekliyor |
| Xbox / Microsoft Store | Desteklenen mevcut-kullanıcı Appx/manifest sorguları; erişilebilir game config/oyun extension'ı ve paket aile kimliği | Sahte paket envanteri ve XML ile test edildi; gerçek paket envanteri çalıştırılmadı |
| Minecraft Java | Default/seçili oyun kökünde versions JSON/JAR ile kurulum keşfi; hesap/profil/save içerikleri hariç | Sahte sürümlerle test edildi; paylaşılan JVM kullanımını otomatik oyun sayma yok |
| Minecraft Bedrock | Teknik paket ailesi ve çalıştırıcıları; Java'dan ayrı platform/kimlik | Sahte paketle test edildi; gerçek Bedrock açılışı bekliyor |
| Bağımsız Windows | Uninstall kurulum köklerinde MicrosoftGame.config / gameinfo.txt | Sahte yerel metadata ile test edildi; tüm bağımsız oyunlar kapsanmaz |
| Kullanıcı klasörleri | Seçili kök + bir alt düzey, doğrulanmış metadata veya onay bekleyen exe adayları, manuel ad/tam yol/paket eşleştirme | Sahte dosya sistemi ve geçici DB ile test edildi; gerçek klasör seçimi manuel |

Oyun isim listesi veya örnek oyunlara özel process eşleştirmesi yoktur. Yalnızca açıkça istenen Minecraft sürüm adaptörü için teknik aile/mainClass ayrımı vardır. Yeni bir rastgele başlık metaveride geldiğinde kaynak kodu değiştirmeden kataloglanır.

## 4. Otomatik keşfin sınırları

Her exe oyun değildir. Metadata bulunmayan kullanıcı klasörü sadece onay bekleyen adaydır ve otomatik oyun süresi üretmez. Aynı süreç adı farklı klasördeyse eşleşmez; proses adı tek başına asla yeterli değildir. Çakışan platform/kurulum kökü belirsiz olarak gösterilir; manuel tam yol seçimi gerekir.

**Minecraft Java sınırlaması:** javaw/java ve benzeri paylaşılan runtime'lar tam exe yolu bilinse bile birçok uygulamayı çalıştırabilir. Bu sürüm komut satırı veya hesap bilgisi okumaz; güvenilir JVM oturumu ayrımı eklenmedi. Java kurulumu katalogda görünür, fakat doğrulanamayan Java kullanımını oyun istatistiğine tahminen katmaz. Böyle bir süreç normal uygulama kullanımında kalır. Paylaşılan runtime/launcher'a kalıcı manuel eşleme de reddedilir. Bedrock'ın paket kimliği bu belirsizlikten ayrı değerlendirilebilir.

Steam/Epic yerel client dosya biçimi kararlı genel bir oyun türü sözleşmesi değildir. Steam yazılım/DLC/araç kayıtları gelebilir; kullanıcı katalog sonucunu gözden geçirmelidir. Korumalı/şifreli Xbox paketinde erişilebilir oyun metaverisi yoksa oyun otomatik bulunmayabilir. Sadece mevcut Windows kullanıcısının paketleri sorgulanır; Windows izinleri aşılmaz. GDK dışı bağımsız oyunlarda özel metadata yoksa kullanıcı eklemesi gerekir.

Tam disk/ağ taraması yoktur. Yerel mutlak yollar, sınırlı derinlik ve adet bütçeleri kullanılır; çok büyük/özel/modlu kütüphaneler kısmi sonuç verebilir. Ulaşılamayan kaynakta son bilinen kayıt/kurulum durumu uyarıyla korunabilir; bu, yeni doğrulama değildir. Klasörün kesin yokluğu veya başarılı paket sorgusunda kaybolma kaldırılma göstergesi olabilir. Hiçbir keşif geçmişi veya katalog satırını otomatik silmez.

## 5. Eklenen testler

**61 yeni vaka, önceki 54 vaka korunmuştur.** 53 sağlayıcı/algılama/SQLite/iş kuralı; 7 yeni WinForms vaka ve 1 yalnızca kendi test sürecine yönelik native API vaka. Rastgele oyun adları, exe isimleri, kimlikler ve sahte kütüphaneler kullanıldı.

Yeni oyun, farklı disk, aynı ad/farklı platform, launcher ayrımı, foreground/background/kapanma, genel toplamın alt kümesi, çoklu oyun dönemleri, manuel ad/yol, javaw yanlış pozitif engeli, kaldırılmış oyunun kalıcılığı, yeniden tarama, iptal sonrası yazma engeli, kaynak hata yalıtımı, periyodik sınır, eski ayar izinleri, migration ve eski yazım uyumu kapsandı. Testlerde gerçek kullanıcı DB'si silinmedi. Ayrıntı ve sınıf başına sayılar [testing.md](testing.md) içinde.

## 6. Debug/Release ve yayın sonuçları

| Kontrol | Sonuç |
|---|---|
| dotnet restore | Başarılı, NuGet güvenlik denetimi kapatılmadı |
| Debug build | 0 uyarı, 0 hata |
| Debug test | **115 başarılı / 0 başarısız / 0 atlanan** |
| Release build | 0 uyarı, 0 hata |
| Release test | **115 başarılı / 0 başarısız / 0 atlanan** |
| Windows x64 self-contained publish | Başarılı; .NET ve Windows Desktop runtime 10.0.14 dahil |

SDK 10.0.403 ve .NET 10 LTS korundu. Teslim sonuç dosyaları `TestResults/task-1.2-delivery-*.trx`: her yapılandırmada 100 çekirdek/altyapı + 15 Windows/UI vaka. Aynı 115 vaka iki konfigürasyonda çalıştırıldı; 230 farklı test iddiası yoktur. Aynı oyun kimliğinin farklı kurulum adı/yolunda da tek toplama birleşmesi ayrıca regresyon testiyle doğrulandı.

İlk test turunda erişilemeyen Steam yolundaki FileExists hata sınırı dışında kaldığı için bir test başarısız oldu; düzeltildi ve son paket geçti. İlk build kullanılmayan Xbox parametresini hata saydı; kaldırıldı. Son geçerli sonuçlar tabloda verilmiştir. UI geometri/yazı %100/%150 simülasyon renderları incelendi; fiziksel Windows DPI değişimi test edilmiş gibi sunulmadı. Win32 exe/paket çağrısı yalnızca kendi test hostumuzda çalıştırıldı; bu gerçek oyun entegrasyon testi değildir.

## 7. Windows yayın dosyası

`artifacts/windows-1.2/FamilyMonitor.Agent.exe`

Dosya sürümü **1.2.0.0**, pencere başlığı **Family Activity Monitor — 1.2**. Klasörün tamamı taşınmalıdır; tek exe'yi ayırmayın. Ayrıca SDK/runtime kurulumu gerekmez. Önceki `artifacts/windows` (1.0) ve `artifacts/windows-1.1` (1.1) dosyaları üzerine yazılmadı. Çalışan eski uygulama otomatik kapatılmadı.

Kişisel veriye migration uygulamamak için normal üretim Program girişini bu çalışma sırasında kişisel profilde açmadım. MainForm ve yeni sekmeler sentetik kaynak/geçici DB ile oluşturuldu. Yeni yayın temiz ikinci Windows bilgisayarında henüz denenmedi.

## 8. Gerçek bilgisayarda manuel kabul

1. Eski uygulamadan normal çıkın. Yeni klasördeki exe'yi açın ve 1.2 başlığını doğrulayın. Eski genel geçmiş/izin/eşik görünmeli; izleme kapalı başlamalı, oyun keşfi ilk yükseltmede izin verilmemiş olmalı. Migration uyarısız geçmeli; eski kayıtlar kaybolmamalı.
2. **Yüklü Oyunlar** sekmesinde izin vermeden yenileme/manual/klasör düğmeleri kapalı olmalı. Ayrı izin diyaloğunu inceleyin; kutu boş ve kabul düğmesi pasif olmalı. “Şimdi değil” keşif başlatmamalı. Kendi açık seçiminizle izin verdiğinizde yenileme çalışmalı.
3. Steam'in tüm disklerdeki kütüphanelerinden örnekler seçin; katalog adı/platform/konum doğru mu kontrol edin. Yeni bir oyun kurduktan sonra **Oyunları yenile** ile eklenmesini ve tekrar taramada çoğalmamasını deneyin. Yaklaşık 15 dakika açık bırakınca otomatik kontrolü doğrulayın.
4. Epic'te farklı diskteki bir oyunu, Xbox/Store'da erişilebilir paket oyununu ve varsa Bedrock'ı deneyin. Bir kaynak hata verse bile diğer sonuçlar görünmeli. Korunan klasörde erişim yoksa izin/yönetici isteği veya izin aşma girişimi olmamalı. Kapsanmayan oyunu manuel ekleyebilirsiniz.
5. Bağımsız oyunun klasörünü **Oyun klasörlerini düzenle** ile seçin. Metaverisiz exe adayında “oyun onayı bekliyor” görünmeli. Manuel oyuna ad ve gerçek oyun exe'sini veya tam paket aile kimliğini seçin; kaydedip yeniden tarayın/açın: manuel ad korunmalı. Exe bu işlemde çalıştırılmamalı.
6. Genel izni verip **İzlemeyi başlat** ile gerçek bir oyunu ön plana alın. Ana ekranda katalogdaki ad görünmeli. Oyundan normal uygulamaya geçin; oyun arka planda çalışsa da oyun süresi büyümemeli. Steam'in arka planda açık olması toplamı ikiye katlamamalı. Oyunu kapatınca dönem tamamlanmalı.
7. Aynı oyunu birkaç kez kullanın. **Oyun İstatistikleri** günlük toplamı birleştirmeli, üstteki tarih ve günlük/haftalık filtre çalışmalı. Oyun toplamı genel toplamın alt kümesi olmalı. Son oynanma, yalnızca monitörün ölçtüğü son dönem olmalı.
8. Aynı adlı iki platform oyunu veya belirsiz kurulum örneğinde platform ayrımı/manuel seçim görünmeli. Oyun olmayan Java uygulamasını çalıştırın: Minecraft Java süresine yazılmamalı. Minecraft Java kurulumunun görünmesi her javaw oturumunun oyun kabul edileceği anlamına gelmez; bu sınır bilinçlidir.
9. Bir oyunu kaldırıp yenileyin: güvenilir biçimde kaldırılma tespit edilebiliyorsa kurulu değil, doğrulanamıyorsa uyarı/son durum görülmeli. Eski oyun süreleri kalmalı. Harici kütüphane diskini ayırın: kayıt/geçmiş silinmemeli.
10. Oyun iznini yenileme sırasında geri çekin: izleme dursun, sonradan sonuç yazılmasın. Yeniden izin vermeden path/paket eşleştirmesi/keşif başlamamalı. Genel izni geri çekmek oyun iznini de kapatmalı; yeniden açılışta tercih korunmalı.
11. Silmeyi kabul ettiğiniz test geçmişinde **Kullanım geçmişini sil** onayını deneyin. Genel/oyun toplam ve geçmiş sıfırlanmalı; oyun kataloğu, manuel adlar ve izinler korunmalı. İptalde veri silinmemeli. Sonra yeniden açıp kalıcılığı kontrol edin.
12. %100/%150 gerçek Windows ölçeklendirmesinde eski/yeni sekmeleri ve yeni diyalogları deneyin. Kilit/uyku/idle senaryolarını oyun ön plandayken tekrarlayın; mevcut aktif süre kuralları korunmalı. İnternet kapalıyken yerel keşif/istatistik çalışmalı.

## 9. Bilinen eksikler ve riskler

Gerçek Steam/Epic/Xbox/Minecraft oyun kurulumları ve oynama oturumları bu çalışma sırasında test edilmedi; sahte kaynak testi bunun yerine geçmez. Paylaşılan JVM oturumu ayrımı yoktur. Korumalı paket/özel launcher/modlu kurulumlar eksik kalabilir; manuel kayıt gerekir. Steam'de oyun olmayan bir kurulum kaydı görülebilir; yerel metadata tek başına kusursuz tür kataloğu değildir.

Saniyelik örnekleme ve son girdiye kadar doğrulama kuralları korunur; gamepad girdilerinin GetLastInputInfo tarafından nasıl raporlandığı gerçek cihaz/oyunda ayrıca denenmelidir. Oyun sadece controller ile oynanırken input idle eşiği yanlış aktiflik sonucu verebilir; bu görev gamepad API'si eklememiştir. Büyük kataloğun senkron UI sorguları yavaşlayabilir. UI history en son 500 dönemi gösterir; toplamlar tüm aralığı kapsar. Katalogdaki son oynanma platformun hesap geçmişi değil, monitör kayıtlarından türetilendir.

Elle eklenen ve herhangi bir sağlayıcıya bağlı olmayan oyunların kurulum durumu kullanıcı beyanıdır; otomatik kaldırılma teyidi yapılmaz. Eski sürüm yeni oyun ayarlarını tanımadığından, sürümler arasında geri dönüşte oyun iznini tekrar vermeniz gerekebilir. SQLite şifreleme, otomatik saklama süresi, kurulum sihirbazı, kod imzası ve otomatik güncelleme eklenmedi. Katalog/manuel yol bilgileri Windows kullanıcı profili dosya izinlerine dayanır. Oyun izni/Geçmiş temizleme davranışları [privacy.md](privacy.md) içinde açıklandı. Keşif varsayılan kapalıdır; hesabın gizli dosyaları, komut satırı, oyun kayıt içeriği, mesaj ve şifre okunmaz. Uygulama internete kullanım verisi göndermez.

## 10. Tamamlanma durumu

**Görev 1.2'nin yerel prototip kodu, ayrı izin, platform adaptörleri, güvenli eşleştirme sınırları, SQLite migration, iki sekme, otomatik testler ve istenen yayın tamamlandı. Gerçek platform/oyun kabul testleri bekliyor.** Tüm oyunları kusursuz tanıma veya paylaşılan Java oturumunu otomatik ayırma tamamlandı denmiyor; sınırlar yukarıda belirtildi.

Mevcut kullanıcı veritabanı kendiliğinden silinmedi, eski yayınlar korundu, yeni uzak bağlantı/backend/mobil/tarayıcı eklentisi geliştirilmedi ve GitHub'a push yapılmadı. Görev 2'ye geçilmedi.
