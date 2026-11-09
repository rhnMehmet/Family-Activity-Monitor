# Gizlilik ve kullanıcı kontrolü — 2.0.0-prototype

## İzinler ve kapsam

Genel izleme, oyun keşfi, tarayıcı alan adı, ayrıntılı URL yolu, arama sorguları ve her tarayıcı seçimi ayrı tercihlerdir. Yeni tarayıcı ve başlangıç tercihleri varsayılan kapalıdır; eski ayarlar bunları otomatik kazanmaz. Agent ve eklenti izinlerinin kesişimi kullanılır. Genel izni vermek tek başına takip başlatmaz. Elle açılış kapalı başlar; kullanıcı ayrıca izinli otomatik başlangıcı seçebilir.

Genel kullanım: ön plandaki uygulama/süreç adı, UTC başlangıç-bitiş, son girdiyle doğrulanmış süre, durum ve rastgele yerel kimlikler. Windows hesap adı veya e-posta için alan yoktur. Pencere tanıtıcısı/son girdi zamanı bellektedir. Ham klavye tuşları/fare koordinatları toplanmaz. Güvenilir ön plan oyununda XInput üzerinden yalnızca etkileşim var/yok sonucu çıkarılır; ham kontrolcü durumu saklanmaz. Desteklenmeyen kontrolcü etkileşimi varsayılmaz.

Oyun keşfine ayrı izin varsa yerel kurulum metaverileri, oyun adı/platform/kurulum konumu, exe ve paket eşleştirmeleri, elle verilen adlar katalogda tutulur. Yollar kullanıcı adını içerebilir. Steam/Epic manifestleri, mevcut kullanıcı paket bilgisi ve seçili oyun klasörleri incelenir; hesap/launcher oturum dosyaları ve oyun kayıt içerikleri okunmaz. Tüm disk/diğer profiller taranmaz, korumalı klasör izinleri aşılmaz. Oyun izni çekilince keşif iptal edilir, eşleştirme belleği bırakılır ve takip durur; eski katalog ayrı kalır. Genel izin çekilince oyun ve tarayıcı toplama izinleri ile otomatik izleme de kapatılır.

## Tarayıcı verileri

İki tarafta açık izin ve uygun OS ön planı varken normal aktif sekmenin alan adı, tarayıcı türü, ziyaret döneminin UTC sınırları ve doğrulanmış aktif süre saklanır. Sekme/pencere kimlikleri eşleştirmede bellekte kullanılır; kalıcı visited-tab ID listesi yoktur. Sekme başlığı veya mevcut tüm tarayıcı geçmişi okunmaz. Tarayıcı `tabs` izni teknik olarak geniş erişim sağlayabilir; kod yalnızca odaktaki normal pencerenin aktif sekmesini sorgular, history API veya content script kullanmaz.

- Ayrıntı kapalı: yalnızca domain. Alt alan adı da hassas olabilir.
- Ayrıntı açık: query/fragment tamamen atılmış, hassas/opaque yol parçaları sansürlenmiş URL yolu. Ham URL günlüğü değildir.
- Sorgu açık: yalnızca desteklenen Google/Bing/DuckDuckGo/Yahoo sonuç rotalarının tek sorgu parametresi, en fazla 240 karakter. Yinelenen ve tanınan secret/token biçimleri reddedilir. Sıradan sitelerin parametreleri alınmaz.

Filtreler her olası kullanıcı sırrını tanıyamaz. İnsan tarafından URL yoluna veya bir arama kutusuna yazılmış sırlar tanınmayan biçimdeyse açıkça izin verilen yol/sorguda kalabilir. İhtiyaç yoksa bu iki kapsamı kapalı tutun; sırları arama sorgusuna koymayın. Parolalar, form içerikleri, çerezler, oturum depoları, sayfa içerikleri, ekran görüntüleri, mesajlar, ses/kamera veya sayfa DOM'u için hiçbir toplama mekanizması yoktur.

Özel/gizli gezinme domain/yol/sorgu kaydı üretmez. Manifest `incognito: not_allowed` ve kod kontrolleri birlikte kullanılır. Genel uygulama izni tarayıcının uygulama olarak aktif süresini yine ölçebilir; özel pencere adresi/başlığı öğrenilmez. Sayfa pasif video oynatırken kullanıcı idle olursa site aktif süre kazanmaz. Arka plandaki tarayıcı oyun sırasında site süresi kazanmaz. Site süreleri genel uygulama süresinin alt kümesidir, toplamın üzerine eklenmez.

## Yerel aktarım

Eklenti → tarayıcının Native Messaging stdio host'u → aynı Windows kullanıcı/oturumuna özel named pipe → Agent. İnternete açık TCP/HTTP dinleyicisi veya uzak sunucu yoktur. Sabit eklenti kimlikleri, host çağrı argümanları, beklenen host/Agent exe tam yolları ve Windows pipe kullanıcı kontrolü uygulanır. SID yalnızca pipe adının hash'ini üretmek için bellekte kullanılır. Mesajlar en fazla 8192 bayt, sıkı şemalı, sıra numaralı ve güncel oturum challenge'ına bağlıdır. Replay/bozuk mesaj reddedilir. Belirsiz veya eski bağlantıda siteye süre verilmez.

Pause/idle/kilit/uyku/izin çekme uygun politikayı kapatır. Agent çıkınca pipe kapanır, host çıkar; eklenti metadata göndermez. İzinli eklenti açıkken kapalı Agent'a yeniden deneme 1/2/5/15 dakika ile sınırlıdır; boş heartbeat 10 saniyedir, sekme polling'i yapmaz. Bunlar aynı OS hesabındaki kötü amaçlı yazılıma karşı kriptografik cihaz kimliği veya imzalı binary doğrulaması değildir. Hesap dosyalarını değiştirebilen kişi yerel güven sınırını aşabilir.

## Kontrol ve otomatik başlangıç

Başlangıç için yalnızca açık kullanıcı tercihiyle HKCU Run girdisi yazılır. Oturum açılmadan, servis/SYSTEM ile veya gizli bir görevle çalışmaz. Otomatik izleme için ayrı seçim, geçerli genel izin, beklenen başlangıç kaydı ve hazır etkileşimli oturum gerekir. Tepsi ve başlangıç bildirimi görünürdür; simge Windows taşma menüsünde olabilir. Normal manuel açılış kapalıdır.

Duraklatma, durdurma, tarayıcı/genel izni geri çekme ve çıkış daima serbesttir. PIN bunları engellemez. X çıkış yapar; yalnızca açık Tepsiye küçült düğmesi gizler. İzin çekme toplamayı önce bellekte durdurur, sonra tercihi kaydeder. Yazma hatasında UI hata bildirir ve otomatik başlangıç kaydı kaldırılmaya çalışılır. Dosya/registry birlikte erişilemezse sonraki açılışta eski tercih kalabilir: kullanıcı bildirimi ve disk sorununun giderilmesi gerekir; başarısız kalıcılık kesin güvence gibi sunulmaz.

PIN isteğe bağlıdır: rastgele 32 bayt salt, PBKDF2-HMAC-SHA256 600.000 iterasyon, sabit zamanlı hash karşılaştırması; düz metin PIN yoktur. 5 yanlış deneme 15 dakika bekletir, sayaç diske yazılır. Kurtarma kodu 32 rastgele bayt, yalnızca bir kez gösterilir, bağımsız saltlı hash'i tutulur. Kodla PIN kaldırılır; kodsuz sessiz bypass yoktur. PIN bir ayar ekranı korumasıdır, aynı Windows hesabına veya Task Manager'a karşı erişim sınırı değildir. Ayrı Windows hesapları/kurumsal cihaz politikaları bu uygulamadan bağımsız yönetilir.

## Saklama, silme ve yedekler

`%LOCALAPPDATA%\FamilyActivityMonitor` içinde:

| Dosya | İçerik |
|---|---|
| `activity.db`, `-wal`, `-shm` | Genel/oyun/tarayıcı kullanımı, katalog/eşleştirme; SQLite, şifresiz |
| `settings.json` | Sürümlü izinler, eşik, rastgele cihaz/kullanıcı kimlikleri, klasörler, tarayıcı saklama ve başlangıç tercihleri |
| `parental-security.json` | Saltlı PIN/kurtarma hash'leri ve deneme sınırı; düz metin sır yok |
| `agent.lock` | Tek örnek için boş kilit dosyası |
| `Backups/activity-before-*.db` | Yükseltme öncesi doğrulanmış şifresiz kopya; kişisel geçmiş içerebilir |

Tarayıcı saklama varsayılan 30 gün, 1–365 gün ayarlanabilir. Saatlik ve ayar uygulanınca kontrol edilir. Mantıksal sınır anında uygulanır; oyun sırasında fiziksel SQLite budaması uygun bakım zamanına ertelenebilir. Süreyi artırmak silineni geri getirmez. Genel uygulama/oyun geçmişinin otomatik yaş sınırı yoktur. Yedekler saklama politikasından ve silme düğmelerinden ayrı kalır; kullanıcı ayrıca yönetir.

**Tarayıcı geçmişini temizle** yalnızca açık, varsayılan Hayır onayından sonra browser_visits tablosunu ve bekleyen tarayıcı kuyruğunu temizler. Genel uygulama/oyun süreleri, katalog, ayarlar, izin ve PIN korunur. İzleme açıksa sonraki etkileşim yeni site kaydı oluşturabilir. **Kullanım geçmişini sil** izlemeyi durdurur ve tüm kullanım alt kayıtlarını/tüm kullanım kuyruklarını temizler; katalog ve tercihler korunur. Silinen eski kayıtlar yeniden açılışta kuyruktan dönmez. İzin çekme geçmişi kendiliğinden silmez.

`secure_delete` kullanılır; VACUUM/WAL truncate ertelenebilir. SSD/OneDrive/OS yedeği/harici kopyalardaki fiziksel kalıntılar için garanti yoktur. Canlı WAL/SHM'yi uygulama açıkken tek tek kaldırmayın. Veritabanı Windows profil dosya izinlerine dayanır; ortak OS hesabı aynı veriye erişebilir. Yükseltme yedekleri de bu kapsamda korunmalıdır.

Yazılar yaklaşık 5 saniyede gruplanmış FULL-synchronous transaction'la yapılır. Normal kapanış flush yapar; çökme/güç kesintisinde commit edilmemiş kuyruk kaybolabilir. Yaklaşık 5 saniye üst sınır garantisi değildir. Payload/URL/sorgu logu üretilmez; UI hata türünü gösterir. Kişisel DB, ayar, PIN dosyası, log, token ve yayın çıktıları Git dışında kalır. Geliştirme için SDK/npm indirmeleri kullanım verisinin aktarımı değildir.

[1.2.1 gizlilik metni arşivi](privacy-1.2.1.md), [kurulum](task-2-browser-installation.md), [manuel kontrol](task-2-manual-tests.md).
