# Family Activity Monitor — 1.2.1

Aile üyelerinin kendi açık izniyle çalışan, yalnızca yerel Windows uygulama kullanımını ölçen Windows Forms prototipi. Yönetici yetkisi, Windows servisi, hesap veya internet bağlantısı gerektirmez. Uygulama her açılışta izleme kapalı başlar.

## Gereksinimler ve teknoloji

- Windows 11 x64; geliştirme ortamı: 25H2, derleme 26200.9711.
- Kaynaktan derlemek için .NET **10 LTS SDK**. Bu çalışma için proje içine **10.0.403**, çalışma zamanı **10.0.14** kuruldu.
- Git 2.56.0 kurulu. Başlangıç klasörü boştu ve Git deposu değildi.
- WinForms, Microsoft.Data.Sqlite 10.0.14, xUnit. Core ve Infrastructure `net10.0`; Agent `net10.0-windows`.

.NET 8'in desteği 10 Kasım 2026'da sona yaklaştığından, Kasım 2028'e kadar desteklenen .NET 10 LTS seçildi. Windows 11 25H2 Microsoft'un destek tablosunda bulunur: [Windows uyumluluğu](https://learn.microsoft.com/en-us/dotnet/core/install/windows), [LTS politikası](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). SDK'nın güncel bakım sürümlerini takip edin; `global.json` kararlı 10.0 SDK özellik bantlarına izin verir.

## Yapı

```text
FamilyMonitor.sln
src/
  FamilyMonitor.Agent/           Türkçe WinForms, izin, tepsi, oturum olayları
  FamilyMonitor.Core/            Modeller, arayüzler, süre/state machine
  FamilyMonitor.Infrastructure/  Win32 adaptörleri, SQLite, yerel ayarlar
tests/FamilyMonitor.Tests/       Sahte saat testleri ve gerçek SQLite testleri
tests/FamilyMonitor.UiTests/     Windows Forms yerleşim ve UI bağlantı testleri
scripts/dotnet.ps1               Yerel SDK / kurulu SDK seçimi
docs/                           Mimari, gizlilik, test ve teslim raporu
artifacts/windows-1.2.1/        Güncel yayın çıktısı (Git dışında)
.tools/                         Yerel SDK / NuGet önbelleği (Git dışında)
```

## Kurulum ve derleme

Sistem genelinde SDK kurulması gerekirse Microsoft'un [Windows kurulum adımlarını](https://learn.microsoft.com/en-us/dotnet/core/install/windows) izleyin. Alternatif olarak resmi `dotnet-install.ps1` ile `-Channel 10.0 -InstallDir .tools\dotnet -NoPath` kullanabilirsiniz. Bu çalışma sırasında ikinci yöntem uygulandı; sistem PATH'i değiştirilmedi.

Proje kökünde PowerShell:

```powershell
.\scripts\dotnet.ps1 restore FamilyMonitor.sln --configfile NuGet.Config --disable-parallel '-m:1'
.\scripts\dotnet.ps1 build FamilyMonitor.sln --no-restore '-m:1' '-p:UseSharedCompilation=false'
.\scripts\dotnet.ps1 test FamilyMonitor.sln --no-build --no-restore '-m:1'
.\scripts\dotnet.ps1 build FamilyMonitor.sln -c Release --no-restore '-m:1' '-p:UseSharedCompilation=false'
```

Betik `DOTNET_CLI_TELEMETRY_OPTOUT=1` kullanır; SDK ve NuGet önbellekleri `.tools` içindedir. Sistem SDK'sını kullanıyorsanız aynı komutları `dotnet` ile de çalıştırabilirsiniz. İlk bağımlılık indirmesi internet gerektirir; uygulamanın kendisi bağlantı kurmaz.

## Çalıştırma

Hazır Windows x64 yayınında `artifacts\windows-1.2.1\FamilyMonitor.Agent.exe` dosyasını açın. Başlıkta **Family Activity Monitor — 1.2.1** görünmelidir. Eski `artifacts/windows`, `artifacts/windows-1.1` ve `artifacts/windows-1.2` yayınları korunmuştur. Yayın kendi .NET çalışma zamanını içerir; hedef bilgisayarda SDK veya ayrıca runtime kurulumu gerekmez. Klasörün tamamını birlikte taşıyın. Aynı kullanıcı için eski uygulama açıksa önce normal çıkış yapın.

Mevcut DB için 1.2.1 ilk açılışta **onay → SQLite yedeği → yedek bütünlük kontrolü → şema 3** sırasını uygular. Hayır seçilirse geçmiş ve ayarlar değiştirilmeden kapanır. Eski 1.2'ye dönüş için yükseltme öncesi yedek gerekir. Bu geliştirme sırasında gerçek kullanıcı DB'si açılmadı veya migrate edilmedi.

SDK üzerinden:

```powershell
.\scripts\dotnet.ps1 run --project src/FamilyMonitor.Agent -c Release --no-build --no-restore
```

1. İlk açılıştaki açıklamayı okuyun; yalnızca kendi kullanımınız için onay kutusunu işaretleyip izin verin.
2. “İzlemeyi başlat” ile başlayın. İzin vermek tek başına veri toplamaz.
3. “Duraklat / Devam et” veya “İzlemeyi durdur” kontrollerini kullanın.
4. Gün seçicisinden uygulama toplamlarını ve kronolojik kayıtları görüntüleyin.
5. “Ayarlar ve gizlilik” sekmesindeki **Kullanım geçmişini sil** düğmesi yalnızca onayınızla kayıtları siler; ayarlar ve izin tercihi korunur. İşlem izlemeyi durdurur, listeleri ve toplamı sıfırlar.
6. Aynı sekmede eşik değerini değiştirip **Eşiği uygula ve kaydet** düğmesine basın. Yalnızca sayı yazmak ayarı uygulamaz. Ana ekrandaki eşik uygulanan değerdir; kaydedilmemiş değişiklik ayrıca belirtilir. Varsayılan 180 saniyedir; hızlı test için 5 saniyeyi kaydedip fare/klavyeyi 15 saniye kullanmadan bekleyin.
7. Tepsi menüsünden duraklatın, durdurun veya çıkın. Pencerenin X düğmesi uygulamayı kapatır; yalnızca açık “Tepsiye küçült” seçimi pencereyi gizler. Tepsi simgesi görünür kalır (Windows taşma menüsüne yerleştirebilir).

“İzleme: Açık” toplama sisteminin etkin olduğunu gösterir; “Kullanıcı durumu: Hareketsiz” ile aynı anda görünebilir. Hareketsizlikte süre eklenmez. Ana ekrandaki son girdi sayacı fare/klavye girdisinde sıfırlanır. Duraklatıldığında ve kapalıyken kullanıcı durumu ölçülmez; bu sırada eski bir “Aktif” etiketi gösterilmez.

Süreler son girdiye kadar doğrulanır. Eşik altındaki okuma süresi sonraki girdide toplama eklenir; eşik aşılırsa son girdiden sonraki boşluk eklenmez. Bu nedenle sayaç her saniye artmayabilir. Uygulama değişimleri yaklaşık bir saniye çözünürlükle algılanır.

Veriler `%LOCALAPPDATA%\FamilyActivityMonitor` içinde bulunur. Veritabanı, ayarlar ve kilit dosyası proje klasörüne veya Git'e alınmaz. Tarayıcılar yalnızca uygulama olarak kaydedilir. Ayrıntı: [gizlilik](docs/privacy.md).

## Doğrulama ve kapsam

Önceki **115 vaka korundu**; 1.2.1 sonuçları, ham kaynak ölçümleri ve kalan fiziksel kabul kontrolleri [performans raporunda](docs/performance-report-1.2.1.md) bulunur. Gerçek oyun FPS/GPU ölçümü ve fiziksel gamepad testi yapılmadı. Testler sahte kaynak ve geçici SQLite kullanır; kişisel DB/oyun kütüphanesi okunmaz. %100/%150 geometri simülasyonu fiziksel DPI kabulünün yerine geçmez.

## Oyun keşfi ve istatistikler

**Yüklü Oyunlar** sekmesinde ayrı açık izin verin. Bu izin genel kullanım izninden ayrıdır ve varsayılan kapalıdır. İzin varsa açılışta, elle, izlenen metadata değişikliklerinde ve saatte bir keşif yapılır. Güvenilir ön plan oyununda otomatik oyun modu ağır işleri erteler; oyun çıktıktan sonra en az 15 saniye ve sonraki uygun kontrolde tek tarama yapılır. Bu işlem izlemeyi başlatmaz. Ayarlardan otomatik oyun modu kapatılabilir.

| Kaynak | Yerel keşif |
|---|---|
| Steam | Registry/varsayılan kurulum, iki VDF konumu, birden fazla disk/kütüphane, appmanifest kurulum kayıtları |
| Epic Games | Varsayılan/registry AppDataPath altındaki `.item` manifestleri ve farklı kurulum konumları |
| Xbox / Microsoft Store | Kullanıcıya ait desteklenen Appx paket sorgusu; erişilebilir MicrosoftGame.config veya oyun uzantısı metaverisi |
| Minecraft Java | Seçili/default oyun kökündeki versions JSON/JAR metaverisi; hesap ve launcher profilleri okunmaz |
| Minecraft Bedrock | Kendi Windows paket ailesiyle Java'dan ayrı kayıt |
| Bağımsız oyunlar | Uninstall kayıtlarındaki kurulum konumlarında MicrosoftGame.config / gameinfo.txt |
| Kullanıcı klasörleri | Seçili yerel klasör ve bir alt düzey; metaverisiz exe klasörleri yalnızca onay bekleyen aday |

Oyun adları kaynakta tek tek sabitlenmez; Minecraft'ın istenen iki sürümü için ayrı sağlayıcı mantığı ve teknik paket kimlikleri vardır. Herhangi bir exe tek başına oyun değildir. Tam yol/paket kimliği katalogla eşleşirse gerçek ad kullanılır; belirsizlik gösterilir. “Manuel oyun ekle / eşleştir” ile bilinmeyen exe veya tam paket aile kimliğine ad verebilirsiniz. Elle verilen adlar yeniden taramada korunur.

**Önemli sınır:** paylaşılan `javaw.exe`/Java ve yardımcı launcher süreçleri kalıcı tam yol eşleştirmesiyle bile güvenilir tek bir oyuna bağlanamaz. Minecraft Java kurulumu bulunur, fakat JVM oturumu doğrulanamıyorsa oyun süresi olarak tahmin edilmez; normal uygulama süresi korunur. Bedrock paket kimliği ayrı eşlenir. Korumalı paket metaverisine erişilemiyorsa izinler aşılmaz ve otomatik keşif eksik kalabilir. Steam/Epic yerel kurulum biçimleri değişebilir; Steam yazılım/DLC/yardımcı girişleri için katalog sonucunu gözden geçirin.

**Oyun İstatistikleri**, girişle doğrulanan aktif süre ile oyunun ön planda kalma süresini ayrı gösterir. Yalnızca aktif oyun süresi genel aktif kullanımın alt kümesidir. Ön plan ölçümü 1.2.1 ile başlar; eski kayıtlara tahmin eklenmez. XInput uyumlu cihazlar yalnızca güvenilir ön plan oyununda saniyelik örneklenir; hangi tuşa basıldığı saklanmaz. Diğer kontrolcüler ve örnekler arasındaki kısa girişler doğrulanamayabilir. Hafta Pazartesi–Pazar'dır; son 500 aktif oyun dönemi gösterilir. “Son oynanma” platformun hesap geçmişi değildir.

Şema 3 eski dokuz sütunlu `activities` tablosunu korur ve ayrı ön plan tablosu ekler. WAL + FULL dayanıklılık ve yaklaşık 5 saniyelik gruplanmış yazım kullanılır. Normal kapanış kuyruğu boşaltır; ani çökmede son commit edilmemiş bölüm kaybolabilir. Oyun sırasında checkpoint/bakım ertelendiğinden WAL büyüyebilir. “Kullanım geçmişini sil” aktif ve ön plan kayıtlarını temizler; katalog, ayarlar, izinler ve ayrı yükseltme yedekleri korunur. Yedeklerin ayrı kalması silme onayında açıklanır.

[Performans raporu](docs/performance-report-1.2.1.md), [Windows kabul ve A/B/C FPS planı](docs/manual-performance-1.2.1.md).

[Mimari](docs/architecture.md), [test ve manuel senaryolar](docs/testing.md), [Görev 1.2 raporu](docs/task-1.2-report.md), [Görev 1.1 raporu](docs/task-1.1-report.md), [ilk teslim raporu](docs/delivery-report.md).

Backend, tarayıcı eklentisi, URL/geçmiş toplama, mobil uygulama, uzaktan izleme ve bulut bağlantısı bu sürümde yoktur. Görev 2 başlatılmadı. Uygulama kullanım verilerini uzak sunucuya göndermez.
