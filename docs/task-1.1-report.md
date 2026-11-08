# Görev 1.1 — inceleme, düzeltmeler ve kabul durumu

## 1. Veri silme sorununun nedeni

Silme fonksiyonu eksik değildi. `MainForm.BuildSettingsPage` içindeki “Geçmiş kayıtlarını temizle” düğmesi üçüncü sekme olan “Ayarlar ve gizlilik” içindeydi. Click olayı Yes/No onayına bağlıydı; onaydan sonra `ActivityTracker.ClearHistory → IActivityStore.DeleteAll → SqliteActivityStore.DeleteAll` yolu aynı kullanıcı profiline ait DB'nin `activities` tablosunu temizliyordu. Ana ekrandan bu sekmeye erişim vardı; bağımsız ana ekran silme düğmesi yoktu.

İsim ve yerleşim bulunabilirliği zayıftı: izin düğmesiyle aynı sabit yükseklikli satırda duruyordu, ayarlar kaydırılabilir değildi ve DPI ölçekleme açıkça yapılandırılmamıştı. Ancak eski düğmenin kırpılmasını minimum pencere boyutunda %100/%150 geometri simülasyonunda yeniden üretemedim. **Kullanıcının bilgisayarındaki tam görünmeme nedeni kesinleşmiş değildir; DPI hatası bulunduğu iddia edilmiyor.**

Yeni düğme **Kullanım geçmişini sil** adını taşır, ayrı ve içerikle büyüyen bir satırdadır. Ayarlar kaydırılabilir. Onayın varsayılan yanıtı Hayır'dır. Evet yanıtı izlemeyi durdurur, geçmişi temizler ve toplam/listeleri hemen yeniler. Ayar/izin dosyasına dokunmaz. Gerçek düğme zinciri geçici SQLite ile onay/ret ve yeniden açılış senaryolarında doğrulandı.

## 2. Hareketsizlik sorununun nedeni

Kullanıcı varsayılan 180 saniyeyi test ettiğini bildirdi. İncelemede API imzası, LASTINPUTINFO boyutu, 32 bit tick taşması, eşiğin ayarlardan okunması ve `UpdateOptions` ile uygulanması doğru bulundu. Mevcut 180 saniyelik hesap testleri ve yeni 5 saniyelik testler geçti. Gerçek `NativeWindowsActivityApi.GetIdleTime` probunda sayaç artışı, 180 saniyenin aşılması ve sonraki girdide sıfırlanma gözlendi. Dolayısıyla bu ortamda `GetLastInputInfo` arızası veya aktif süreye idle ekleyen bir hata yeniden üretilemedi.

Doğrulanan UI eksikleri: izleme ve kullanıcı durumu tek etikette birleştirilmişti; son girdiden geçen süre ve uygulanan eşik görünmüyordu. Sayı alanına yazılan değer ancak “Ayarı kaydet” ile uygulanıyordu; kaydedilmemiş değer açıklanmıyordu. Kullanıcı varsayılanı kullandığından kaydetmemenin onun testinin nedeni olduğu **iddia edilmiyor**.

Artık izleme **Açık / Duraklatıldı / Kapalı**, kullanıcı durumu **Aktif / Hareketsiz / Kilitli / Uykuda / Ölçülmüyor** olarak ayrı gösterilir. Uygulanan eşik ve yalnızca bellekte tutulan idle süresi görünür. 180 saniye eşiğinde 15 saniyelik bekleme idle değildir; 5 saniye kaydedilmişse 15 saniye bekleme idle olmalıdır. Fareyi hareket ettirmek de girdi sayılır. Gerçek cihazdaki ilk şikâyetin kesin nedeni hâlâ fiziksel tekrar testiyle ayrılmalıdır.

## 3. Kod değişiklikleri

- Silme düğmesi ayrıldı ve yeniden adlandırıldı. İşlem onaydan önce başlamaz; başarılı sonuç açıkça gösterilir. Var olan SQLite silme SQL'i ve DB şeması değiştirilmedi.
- `IUserDialogs` sınırı onay ve izin diyaloğunu UI'den enjekte edilebilir kıldı. Üretimde normal MessageBox ve ConsentDialog kullanılır; otomatik testte sahte yanıtla gerçek Click yolu sınanır.
- `TrackingViewState` izleme/kullanıcı durumu ve tanılama etiketlerini tek eşlemede üretir.
- `ActivityTracker` eşiği, son gözlenen idle süresini ve kilit/uyku durumunu salt okunur sunar. Süre biriktirme algoritması korunur. Başarılı geçmiş temizlemede eski hata bilgisi sıfırlanır.
- Kaydet düğmesi **Eşiği uygula ve kaydet** oldu; kaydedilmemiş sayı ile gerçekten uygulanan eşik ayrı gösterilir. Dosyaya kayıt başarılı olduktan sonra tracker güncellenir.
- Sabit aksiyon satırları içerikle boyutlanır. Ayarlar kaydırılabilir; form DPI ile ölçeklenir. `ApplicationHighDpiMode=PerMonitorV2` proje ayarı kullanıldı: [Microsoft belgesi](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props-desktop#applicationhighdpimode).
- Yeni yayının başlığı **Family Activity Monitor — 1.1**, dosya sürümü **1.1.0.0**. Eski yayın korunarak yeni dosyalar `artifacts/windows-1.1` içine çıkarıldı; eski çalışan süreç kapatılmadı.

## 4. Değiştirilen/eklenen dosyalar

| Dosya | Değişiklik |
|---|---|
| `src/FamilyMonitor.Agent/MainForm.cs` | Düzen, görünür kontroller, uygulanan eşik, durum ayrımı, test için sistem entegrasyon sınırı |
| `src/FamilyMonitor.Agent/UserDialogs.cs` | Yeni; onay/izin adaptörü |
| `src/FamilyMonitor.Agent/TrackingViewState.cs` | Yeni; test edilebilir UI durum eşlemesi |
| `src/FamilyMonitor.Agent/FamilyMonitor.Agent.csproj` | Test assembly erişimi, sürüm 1.1.0 ve PerMonitorV2 |
| `src/FamilyMonitor.Core/ActivityTracker.cs` | Salt okunur idle/eşik/kilit/uyku bilgisi ve başarılı silmede hata temizliği |
| `tests/FamilyMonitor.Tests/IdleThresholdTests.cs` | Yeni; 6 çekirdek vaka |
| `tests/FamilyMonitor.UiTests/FamilyMonitor.UiTests.csproj` | Yeni; Windows'a özel UI test projesi, mevcut xUnit paketleri |
| `tests/FamilyMonitor.UiTests/MainFormTests.cs` | Yeni; gerçek kontroller + sahte kaynak/onay + geçici SQLite, 7 vaka |
| `tests/FamilyMonitor.UiTests/GlobalUsings.cs` | Yeni; Windows model adı çakışması için alias |
| `FamilyMonitor.sln` | UI test projesi eklendi |
| `README.md`, `docs/testing.md`, `docs/architecture.md`, `docs/privacy.md` | Güncel davranış, testler ve yayın yolu |
| `docs/task-1.1-report.md` | Bu rapor |

Üretim bağımlılığı eklenmedi. Yeni test projesi mevcut xUnit/test SDK sürümlerini kullanır. Eski 41 test değiştirilmedi. Yerel kullanıcı DB'si veya izin tercihi açılıp değiştirilmedi/silinmedi; testler yalnızca rastgele geçici klasörler oluşturdu. `.tools/IdleProbe` tanılama aracı ve test renderları Git dışındadır.

## 5. Yeni testler

Çekirdek, 6 vaka: varsayılan 180; 5 saniye eşiğinde 15 saniye bekleme ve dönüş; eşiği düşürmenin anında uygulanması; eşik artırıldığında atılmış idle süresinin geri gelmemesi; geçersiz değerin eski ayarı koruması; duraklatılmışken değişikliğin izlemeyi açmaması.

WinForms, 7 vaka: minimum boyutta %100 ve %150 yerleşimi (2 vaka); sayı alanı/kaydet/kalıcılık/durum etiketi/dönüş bağlantısı; aktif/duraklat/kilit/uyku/idle/kapalı etiketleri; Hayır yanıtında kayıt ve izleme korunması; Evet yanıtında SQLite/toplam/listelerin temizlenmesi ve yeniden açılışta boş kalması; tıklanmadan onay/silme başlamaması.

Onaylı silme testinde `settings.json` öncesi/sonrası aynı içerikte kalır; izin ve cihaz kimliği gereksiz yere sıfırlanmaz. Aktif bellekteki dönemin silme sonrası tekrar yazılmaması da sınanır. Hiçbir otomatik test gerçek kullanıcı DB'sini silmez.

## 6. Test ve derleme sonuçları

| Kontrol | Debug | Release |
|---|---:|---:|
| Önceki testler | 41 başarılı | 41 başarılı |
| Yeni çekirdek testleri | 6 başarılı | 6 başarılı |
| Yeni WinForms vakaları | 7 başarılı | 7 başarılı |
| **Toplam** | **54 başarılı** | **54 başarılı** |
| Başarısız / atlanan | **0 / 0** | **0 / 0** |
| Derleme uyarısı / hatası | **0 / 0** | **0 / 0** |

Restore ve Windows x64 self-contained publish başarılıdır. Her konfigürasyonda aynı 54 vaka koşuldu; 108 farklı test değildir. Son DPI ayarı sonrası sonuçlar `TestResults/task-1.1-verified-*.trx` içindedir. WinForms renderları `TestResults/ui-debug` ve `ui-release` içinde üretildi ve incelendi. Programatik DPI simülasyonu gerçek Windows ölçek ayarını değiştirerek yapılan bir test olarak sunulmaz. Gerçek idle probu 15 saniyelik ayrı tanılamadır; otomatik testlerin süresi sahte saate bağlıdır.

İlk restore ağ kısıtı nedeniyle NU1900 verdi; güvenlik denetimi kapatılmadan normal erişimle geçti. İlk UI test derlemesindeki model adı çakışması alias ile düzeltildi. Bunlar rapordan gizlenmedi; son sonuçlar yukarıdadır.

## 7. Kullanıcının uygulayacağı manuel kontroller

1. Eski uygulamadan normal çıkın. `artifacts/windows-1.1/FamilyMonitor.Agent.exe` dosyasını açın; başlıkta **1.1** bulunmalı. Programın kendisi mevcut verilerinizi silmez.
2. **Ayarlar ve gizlilik** sekmesini açın. **Kullanım geçmişini sil** ve **Eşiği uygula ve kaydet** düğmeleri görünmeli. %100 ve %150 Windows ölçeğinde uygulamayı yeniden açarak küçük pencere boyutunu da deneyin; alt bilgilere kaydırarak erişilebilmeli.
3. Silme düğmesine basıp **Hayır** seçin: kayıtlar/toplam ve izleme korunmalı. Yalnızca silmeyi kabul ettiğiniz geçmiş için tekrar basıp **Evet** seçin: izleme kapalı, toplam sıfır ve listeler boş olmalı. Yeniden açılışta kayıtlar geri gelmemeli; izin tercihi ve eşik korunmalı.
4. Eşiğe **5** yazın. Kaydetmeden önce “Henüz uygulanmadı / Geçerli eşik 180” görülmeli (önceden başka eşik kaydedilmişse o değer). **Eşiği uygula ve kaydet** düğmesine basın. Ana ekrandaki eşik **5 saniye** olmalı; yeniden açılışta da 5 kalmalı.
5. İzlemeyi başlatın, birkaç saniye Not Defteri'nde etkileşim kurun. Sonra fare ve klavyeye **15 saniye hiç dokunmayın**. Duruma bakmak için fareyi oynatmayın. Son girdi sayacı ilerlemeli; ilk uygun örneklemede yaklaşık 5 saniyede **Kullanıcı durumu: Hareketsiz** olmalı. **İzleme: Açık** kalması doğrudur. Bu aralık toplamda birikmemeli.
6. Fareyi hareket ettirin veya bir tuşa basın: sayaç sıfıra yaklaşmalı, kullanıcı durumu Aktif'e dönmeli. Idle boşluğu geriye dönük toplama eklenmemeli; sonraki etkileşimler yeni dönemde birikmeli.
7. Varsayılan testi için eşiği **180** kaydedin ve hiç girdi vermeden **en az 185 saniye** bekleyin. 180 saniye altında Aktif kalması normaldir. Aynı koşullarda hâlâ Aktif görünüyorsa ekrandaki son girdi süresini ve uygulanan eşiği not edin: sayaç sürekli sıfırlanıyorsa Windows yeni girdiler bildiriyor; sayaç 180'i aşıp durum değişmiyorsa takip/UI yolu ayrıca incelenmelidir.
8. Kilit/aç ve uyku/uyanma deneyin. Aradaki süre toplama eklenmemeli. Duraklatılmışken aynı işlemler yapılınca izleme kendiliğinden açılmamalı. Sonunda tercih ettiğiniz eşiği kaydedin.

## 8. Bilinen eksikler

Fiziksel %100/%150 ekran ölçeği geçişi, gerçek kilit/uyku ve kullanıcıdaki orijinal hareketsizlik şikâyetinin aynı koşullarda tekrar testi bu çalışma sırasında yapılmadı. Gerçek onay MessageBox'ının düğme/klavye etkileşimi otomatik test yerine manuel kontrol listesindedir. Süre hesabı arızası ve eski düğme kırpılması yeniden üretilemedi; kesin kök neden iddiası yoktur.

Yaklaşık bir saniyelik örnekleme, beş saniyeden uzun gözlem boşluğunu saymama, son girdiye kadar doğrulanan süre, şifrelenmemiş yerel SQLite ve büyük geçmişlerde senkron UI sorgusu sınırları korunmuştur. Yeni tanılama bilgisi bellektedir; kalıcı girdi geçmişi veya tuş içeriği eklenmedi.

## 9. Kabul durumu

**Kod değişiklikleri, otomatik testler, render kontrolleri ve yeniden yayın hazır. Yeniden manuel kabul testi için hazır; koşulsuz kabul/tam fiziksel çözüm henüz doğrulanmış değil.** Silme yolu geçici DB ile uçtan uca doğrulandı; hareketsizlik hesabı ve UI bağlantısı otomatik testte, native sayaç ise ayrı Windows probunda doğrulandı. Kullanıcının gerçek cihazındaki şikâyetin giderildiği ancak yukarıdaki tekrar testiyle söylenebilir.

Görev 2'ye geçilmedi, backend/mobil/eklenti eklenmedi, GitHub'a push yapılmadı. Mevcut kişisel SQLite verileri kendiliğinden silinmedi ve eski yayın korundu.
