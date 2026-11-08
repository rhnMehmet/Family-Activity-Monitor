# Gizlilik ve kullanıcı kontrolü

## Toplananlar

Yalnızca açık izin ve ayrıca başlatma eylemi sonrasında: ön plandaki uygulama adı, süreç adı, UTC başlangıç/bitiş, doğrulanmış aktif süre, rastgele kayıt kimliği ve durum. Rastgele yerel cihaz kimliği ve yerel kullanıcı UUID'siyle Windows oturum numarasının birleşiminden oluşan yerel oturum tanımlayıcısı saklanır. Windows hesap adı, e-posta ve SID için ayrı toplama alanı yoktur; ayrı oyun izniyle saklanan kurulum yolları kullanıcı adını içerebilir. Kimlikler anonimlik garantisi değildir; kayıtlar yerel kullanım geçmişini gösterir.

Pencere tanıtıcısı ve son girdi zamanı yalnızca bellekte süre hesabı için kullanılır. Klavye/fare için sistem API'si son girdi zamanını verir; klavye tuş içeriği veya fare koordinatları okunmaz. 1.2.1'de güvenilir ön plan oyunu için XInput kontrolcü durumu saniyelik örneklenebilir. Geçici durumdan yalnızca bağlı/etkileşim var-yok sonucu çıkarılır; hangi düğmenin kullanıldığı, kombinasyonlar veya eksen değerleri saklanmaz. Son etkileşim zamanı bellekte tutulur.

## Toplanmayanlar

URL, tarayıcı geçmişi, arama sorgusu, pencere/sekme başlığı, web sayfası içeriği, özel mesaj, şifre, tuş vuruşu içeriği, ekran görüntüsü, ses ve kamera toplanmaz. Kullanıcı dosyalarının, hesap/oturum dosyalarının ve oyun kayıtlarının içerikleri okunmaz. Ayrı oyun izni yoksa exe yolu ve paket kimliği de okunmaz. Chrome, Edge ve Firefox sadece uygulama olarak görünür. Ağ aktarımı ve üçüncü tarafa paylaşım mekanizması yoktur. Geliştirme SDK/paket indirmeleri kullanıcı etkinliği gönderimi değildir.

## Ayrı oyun keşfi izni — 1.2

Varsayılan kapalı ve sürümlü bir oyun keşfi izni eklenmiştir. Eski ayarlar bu izni otomatik kazanmaz. Açıklamayı okuma ve işaretlenmemiş kutuyu seçme sonrasında izin verilebilir. Keşfe izin vermek genel kullanım iznini vermez veya izlemeyi başlatmaz.

İzinle: oyun adı/platform/kimlik, kurulum konumu, exe eşleştirmeleri, Windows paket aile kimliği ve kullanıcının verdiği manuel ad/eşleştirmeler yerel SQLite kataloğuna yazılır. Steam/Epic kurulum manifestleri, mevcut kullanıcı Windows paket manifestleri/erişilebilir MicrosoftGame.config, Minecraft versions JSON/JAR varlığı ve seçili oyun klasörlerindeki tanımlı oyun metaverileri incelenir. Kurulu program registry'sinde yalnızca kurulum konumları okunur; kayıtlı hesap bilgileri kullanılmaz.

İzleme de açıksa foreground sürecin erişilebilen exe yolu/paket kimliği eşleştirme için bellekte okunur. Genel uygulama zaman çizelgesine bu yollar kaydedilmez; bilinen oyun kurulum yolları katalogda, açık manuel eşleştirmeler ayrı tabloda saklanır. Kullanım kaydına yalnızca oyunun kurulum kimliğiyle ilişki eklenir. Son oynanma bu monitör kayıtlarından türetilir; platform hesap oturumu veya çevrimiçi geçmiş okunmaz.

Keşif açılışta, elle, bilinen metadata dizinlerindeki değişikliklerde ve saatlik yedek kontrolde gerçekleşir. Dizin izleyicileri ayrı izin olmadan kurulmaz; içerik veya oyun girdisi izlemez. Oyun modunda ağır tarama ertelenir. Tüm disk ve diğer kullanıcı profilleri taranmaz. Yalnızca yerel mutlak yollar desteklenir; ağ/UNC yolları kabul edilmez. Korumalı Windows klasörü izinleri değiştirilmez, yönetici yetkisi istenmez. Paket sorgusu `Get-AppxPackage`/`Get-AppxPackageManifest` ile yalnızca mevcut kullanıcı içindir; `-AllUsers`, kurulum/kaldırma veya güvenlik değiştirme komutu kullanılmaz. PowerShell komutu sabittir; kullanıcı girdisi komut olarak çalıştırılmaz.

Oyun izni geri çekilince devam eden tarama iptal edilir, iptal sonrası sonuç yazılmaz, bellekteki eşleştirme önbelleği bırakılır ve izleme durur. Eski katalog/geçmiş korunur. Genel izni geri çekmek oyun iznini de kapatır. “Kullanım geçmişini sil” oyun kullanım geçmişi/son oynanmayı da temizler; katalog ve manuel ad/eşleştirmeleri silmez. Tam katalog ve ayar temizliği için uygulamadan çıktıktan sonra kullanıcı tüm veri klasörünü kaldırabilir; yeniden izin gerekir.

## İzin ve durdurma

İlk izin ekranındaki kutu varsayılan olarak boştur. “Şimdi değil” veya kapatma izni reddeder. İzin verilmeden Windows etkinlik kaynağı hiç okunmaz. İzin vermek otomatik başlatmaz; her program açılışında izleme kapalıdır. Windows otomatik başlatma kaydı, servis veya gizli ajan oluşturulmaz.

Ana ekran ve görünür sistem tepsisi menüsü duraklatma, devam ve durdurma sunar. X düğmesi tamamen çıkar. Tepsiye küçültme yalnızca açık düğmeyle olur; Windows simgeyi taşma alanında gösterebilir. Ayarlardan izin geri çekilince izleme anında kapanır. İzin geri çekmek eski kayıtları silmez; ayrı “Kullanım geçmişini sil” işlemi gerekir. Bu işlem onay gerektirir; varsayılan yanıt Hayır'dır. Yeniden izin verilmesi de başlatma gerektirir. İzin metni sürümü değişirse eski izin kabul edilmez.

## Saklama ve silme

- Klasör: `%LOCALAPPDATA%\FamilyActivityMonitor`.
- `activity.db`: SQLite kullanım kayıtları ve oyun kataloğu/eşleştirmeleri. Şifreli değildir; Windows kullanıcı profili dosya izinlerine dayanır. Ortak Windows hesabı aynı kayıtlara erişebilir.
- `settings.json`: genel/oyun izin sürümleri/seçimleri, eşik, rastgele yerel kimlikler ve seçili oyun klasörleri. Geçmiş silme bunları korur.
- `agent.lock`: aynı kullanıcı için birden çok örneği önleyen boş kilit dosyası.
- `activity.db-wal` / `activity.db-shm`: SQLite'ın yerel transaction/okuma eşgüdüm dosyaları. Canlı DB'nin parçasıdır; uygulama açıkken ayrı ayrı kaldırmayın.
- `Backups/activity-before-1.2.1-*.db`: mevcut DB yükseltilmeden önce açık onayla oluşturulan ve bütünlüğü doğrulanan SQLite yedeği. Eski geçmiş ve katalog içerebilir; şifresizdir ve geçmiş silme düğmesiyle silinmez. Yedekleri kullanıcı ayrıca yönetir.

Otomatik saklama süresi yoktur. Geçmiş temizleme aktif ve ön plan kayıtlarını transaction içinde siler, izlemeyi durdurur ve bekleyen bellek kayıtlarını bırakır. `secure_delete` açıktır; `VACUUM` ve WAL truncate uygun bakım aralığına ertelenir. Ayrı yükseltme yedekleri kalır. SSD, yedekleme, sistem geri yükleme veya işletim sistemi kopyalarındaki fiziksel kalıntıların silineceği garanti edilmez. Tüm ayar, yedek ve kimlikleri de kaldırmak için uygulamadan çıktıktan sonra kullanıcı bu klasörü silebilir; sonraki açılış yeni kimlik ve izin ekranıyla başlar.

Ön plan oyunu süresi 1.2.1'den itibaren ayrı tabloda saklanır. Hareketsiz ama ön planda duran oyun bu metrikte artabilir; genel aktif süreye eklenmez. Kilit, uyku ve duraklatma iki sayacı da keser. Desteklenmeyen kontrolcü etkileşimi varsayılmaz. Normal yazılar yaklaşık 5 saniyede bir FULL-synchronous transaction ile gruplanır; normal kapanış kuyruğu boşaltır. Ani çökmede son commit edilmemiş bölüm kaybolabilir; yaklaşık 5 saniye hedefi bir üst sınır garantisi değildir.

Veritabanları, loglar, geçici veriler ve derleme çıktıları `.gitignore` içindedir. Uygulama etkinlik içeriği içeren log üretmez; UI yalnızca hatanın türünü gösterir. Görev 1 kapsamında uzaktan veri paylaşımı yoktur.
