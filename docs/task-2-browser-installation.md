# 2.0.0-prototype — Tarayıcı kurulumu

Bu bir Windows x64 masaüstü prototipidir. Eklentiler mağazada yayımlanmadı ve fiziksel tarayıcı kabulü henüz yapılmadı. Kurulum, izin ve takip ayrı seçimlerdir. Varsayılan olarak hepsi kapalıdır. Yönetici olarak çalıştırmayın.

## Yayın klasörü

`artifacts/windows-2.0.0-prototype` klasörünün tamamını yerel, kalıcı bir konuma taşıyın. İçinde Agent, üç native host exe'si, ortak .NET çalışma zamanı, `extensions`, `native-host-manifests` ve `Install-NativeHosts.ps1` birlikte bulunmalıdır. Önce eski Agent'tan normal çıkış yapın. Yeni Agent'ı açınca mevcut veritabanı için yükseltme onayını ve gösterilen yedek konumunu okuyun. Hayır seçimi uygulamayı kapatır; yükseltme yapılmaz.

Şema 4, eski kayıtları ve oyun kataloğunu korur. Yedek SQLite Backup API ile alınır ve bütünlüğü doğrulanır. Geliştirme/testler kişisel DB'yi açmadı. Gerçek ilk yükseltme sizin onayınızla yapılacaktır.

## Native Messaging kaydı

PowerShell'i **yayın klasöründe** normal kullanıcı olarak açın. Önce planı inceleyin:

```powershell
.\Install-NativeHosts.ps1 -Browser Chrome -WhatIf
.\Install-NativeHosts.ps1 -Browser Chrome
# İsteğe göre ayrıca:
.\Install-NativeHosts.ps1 -Browser Edge
.\Install-NativeHosts.ps1 -Browser Firefox
```

Betik yalnızca seçilen tarayıcının HKCU NativeMessagingHosts kaydını 32/64 bit görünümlerde oluşturur. Servis, HKLM, zamanlanmış görev veya otomatik başlatma oluşturmaz. PowerShell yürütme politikası engellerse önce betiği inceleyip kuruluşunuzun politikasına uygun çalıştırın; sistem genelindeki politikayı gevşetmeyin.

| Tarayıcı | HKCU altındaki kayıt | İzin verilen eklenti |
|---|---|---|
| Chrome | `Software\Google\Chrome\NativeMessagingHosts\com.familymonitor.chrome` | `mcipbngepoajhbkoecbbdiijckmfejol` |
| Edge | `Software\Microsoft\Edge\NativeMessagingHosts\com.familymonitor.edge` | aynı Chromium kimliği |
| Firefox | `Software\Mozilla\NativeMessagingHosts\com.familymonitor.firefox` | `family-monitor@local.familyactivitymonitor` |

Manifestlerde exe yolu mutlak olduğundan klasörü taşıdıktan sonra betiği **yeni konumdaki kopyasından** yeniden çalıştırın. Başka konuma ait mevcut kayıt otomatik değiştirilmez; doğruladıktan sonra bilinçli olarak `-ReplaceExisting` verebilirsiniz. Kaldırma yalnızca bu klasöre işaret eden kaydı kaldırır:

```powershell
.\Install-NativeHosts.ps1 -Browser Chrome,Edge,Firefox -Uninstall -WhatIf
.\Install-NativeHosts.ps1 -Browser Chrome,Edge,Firefox -Uninstall
```

Bu işlem eklentiyi kurmaz/kaldırmaz, izin vermez ve kullanım DB'sini açmaz. Eklentiyi tarayıcıdan ayrıca kaldırabilirsiniz.

## Chrome

1. `chrome://extensions` sayfasında Geliştirici modu açın.
2. **Paketlenmemiş öğe yükle** ile yayındaki `extensions/chrome` klasörünü seçin.
3. Gösterilen kimliğin yukarıdaki sabit kimlikle aynı olduğunu doğrulayın. Farklıysa izin listesini gevşetmeyin; bağlantı kurulmaz.
4. Eklenti simgesini açın; alan adı takibini açıkça seçip **Seçimlerimi uygula** düğmesini kullanın. İstenen isteğe bağlı `tabs` iznini okuyun. URL yolu ve arama sorgusu kutularını gerekmedikçe kapalı bırakın.

## Edge

`edge://extensions` → Geliştirici modu → **Paketlenmemiş yükle** → `extensions/edge`. Kimliği doğrulayın; Chrome kaydı yerine Edge native host kaydı gerekir. Eklenti açılır penceresindeki izinleri ayrıca verin. Kuruluş politikaları paketlenmemiş eklentileri engelleyebilir; engeller aşılmaz. Gerçek Edge'de kimlik/bağlantı testi bekliyor.

## Firefox

Bu prototip Firefox masaüstü **140 veya üstünü** hedefler. MV3 event page (`background.scripts`) kullanır; Chrome service worker manifesti Firefox'a kopyalanmaz. Yeni Firefox veri toplama izinleri de ayrı istenir.

1. `about:debugging#/runtime/this-firefox` → **Geçici Eklenti Yükle** → yayındaki `extensions/firefox/manifest.json`.
2. Eklenti kimliğini kontrol edin. Açılır pencerede takibi seçip uygulayın; `tabs` ile isteğe bağlı `browsingActivity`, sorgu seçildiğinde ayrıca `searchTerms` iznini okuyun.
3. Geçici eklenti Firefox yeniden başlatılınca kaldırılır. Kalıcı normal kurulum için Mozilla imzalı paket gerekir; bu teslimde imzalama veya mağaza yayını yapılmadı. Yeniden başlatma kabulünde geçici kurulumun bu sınırını hesaba katın.

## Agent izinleri ve çalıştırma

1. **Tarayıcı Ayarları** sekmesinde alan adı takibini ve yalnızca istediğiniz tarayıcıları seçin. Gerekirse URL yolu / arama sorgusu için ayrı kutuları seçin. **İzinleri ve saklama süresini uygula** ve açık onay gerekir.
2. Genel izleme iznini ayrıca verin ve **İzlemeyi başlat** kullanın. Eklenti izni tek başına Agent'ı başlatmaz. Agent izni de eklenti izninin yerine geçmez.
3. Normal bir http(s) sekmesinde fare/klavyeyle etkileşin. Bağlantı durumu ve **TARAYICI ETKİNLİĞİ** sekmesini izleyin. Tablolar görünürken normalde 5, oyun modunda 15 saniyede yenilenir. Durum etiketleri saniyeliktir.

Süre son girdiyle doğrulanır; hareketsiz video izleme eşik sonunda aktif kullanım sayılmaz. Özel pencereler, tarayıcı iç sayfaları, dosya adresleri, IP adresleri ve doğrulanamayan pencereler site kaydı üretmez. Genel uygulama takibi tarayıcının uygulama süresini yine ölçebilir; buradan özel sekme adresi çıkarılmaz.

URL seçeneği ham URL geçmişi değildir: query ve fragment hiçbir zaman ayrıntılı URL'ye yazılmaz; hassas/opaque yol parçaları sansürlenir. Bilinen arama sonuçlarındaki sorgu ayrı izinle ve filtreyle alınabilir. Domain ve kullanıcı tarafından açıkça izin verilen yol/sorgu yine hassas olabilir; ihtiyacınız yoksa açmayın.

## Bağlantı sorunu

- Agent açık mı, genel takip açık mı, doğru tarayıcı gerçekten ön planda mı?
- İki tarafta aynı kapsam için izin var mı? Firefox veri izinleri verildi mi?
- Host kaydı, manifest ve exe aynı yayın klasörünü mü gösteriyor? Klasör taşındı mı?
- Eklenti kimliği doğru mu? Yanlış kimliği allowlist'e eklemeyin.
- **Yeniden bağlan** seçin. Agent kapalıyken eklenti metadata göndermez; otomatik denemeler 1/2/5/15 dakika aralıklarla sınırlıdır.
- Birden çok profil/normal pencere odak iddia ediyorsa Agent belirsizlik gösterir ve site süresi yazmaz. Stale bağlantı taze onay verene veya ayrılana kadar atıf durabilir.

Konsol/stdout'a URL dökülmez. Eklenti Developer Tools ve Agent hata türleriyle teşhis yaparken kişisel sekme adreslerini paylaşmayın.

## Otomatik başlatma ve PIN

**Ayarlar ve gizlilik** içinde Windows oturumunda otomatik açılma ve açılışta izleme ayrı kapalı seçimlerdir. İkincisi geçerli genel izin ister. Normal elle açılış durdurulmuş başlar. Oturum açılışında kayıt/ayar/izin ve etkileşimli oturum kontrolü geçerse takip başlar; görünür tepsi ve başlangıç bildirimi vardır. Simge Windows taşma alanına gidebilir.

İsteğe bağlı PIN yalnızca ebeveyn ayarlarını ve başlangıç seçeneklerini etkinleştirmeyi korur. 6–12 rakam, saltlı PBKDF2; yanlış 5 denemede 15 dakika bekleme. Kurtarma kodu oluşturulunca bir kez gösterilir, ayrı güvenli yerde saklayın. Kodla PIN kaldırılıp yeniden kurulabilir. PIN ve kod yoksa sessiz bir sıfırlama sunulmaz. Aynı Windows hesabının dosyalarını değiştiren kişiye karşı işletim sistemi güvenlik sınırı değildir. Duraklatma, durdurma, izin çekme ve çıkış PIN istemez.

## Teknik kaynaklar

[Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging), [Edge Native Messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging), [Firefox Native Messaging](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Native_messaging), [Firefox background uyumu](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/background), [Firefox veri izni](https://extensionworkshop.com/documentation/develop/firefox-builtin-data-consent/), [Chrome sabit geliştirme kimliği](https://developer.chrome.com/docs/extensions/reference/manifest/key).
