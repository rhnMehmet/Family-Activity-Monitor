# Windows kabul ve FPS kontrolü — 1.2.1

Bu adımlar fiziksel kabul içindir; henüz yapılmış test sonuçları değildir. Kontrolcü modeliniz henüz belli olmadığı için fiziksel gamepad desteği doğrulanmadı. Otomatik testler gerçek kullanım veritabanını açmaz.

## 1. Başlatma, yedek ve veri koruma

1. Eski uygulamayı tepsiden **Çıkış** ile normal kapatın. `artifacts/windows-1.2.1/FamilyMonitor.Agent.exe` dosyasını açın. Başlık **Family Activity Monitor — 1.2.1** olmalı. Klasörün tamamını taşıyın.
2. Mevcut veritabanında yükseltme onayı gösterilir. Önce **Hayır** seçin: uygulama kapanmalı, eski sürümde geçmiş/ayarlar değişmemeli. Eski sürümden çıkıp tekrar 1.2.1'i açın. Güncellemek istediğinizde yedek konumunu okuyup **Evet** seçin. Yedek doğrulanmadan migration yapılmamalı.
3. `%LOCALAPPDATA%\FamilyActivityMonitor\Backups` altında `activity-before-1.2.1-*.db` bulunmalı. Eski genel kullanım, katalog, manuel adlar/eşleştirmeler, izinler, cihaz kimliği ve eşik korunmalı. İzleme kapalı başlamalı. Yeni kurulmuş, boş bir profilde eski veri bulunmadığından migration onayı gerekmez.
4. Eski 1.2 uygulamasını yeni şema üzerinde çalıştırmayın: eski sürüm bunu reddeder. Geri dönüş gerekirse tüm sürümleri normal kapatın; mevcut veri klasörünün tamamını ayrı bir yere kopyalayın ve yükseltme öncesi yedeği ayrı bir geri dönüş kopyasında kullanın. Canlı DB/WAL dosyalarını çalışırken değiştirmeyin. Geri dönüş yedeği yükseltmeden sonraki ölçümleri içermez.

## 2. Normal kullanım, hareketsizlik ve tepsi

1. Genel izni verip **İzlemeyi başlat** seçin. İki sıradan uygulama arasında geçin. Ad ve süreler doğru olmalı; aynı zaman iki uygulamaya yazılmamalı. Oyun modu kapalı kalmalı.
2. Ayarlarda eşiği **5** yapıp **Eşiği uygula ve kaydet** seçin. 15 saniye fare/klavyeye dokunmayın: kullanıcı durumu **Hareketsiz** olmalı; boşluk aktif toplama eklenmemeli. Geri dönün: takip devam etmeli. Denemeden sonra varsayılan **180** saniyeyi geri kaydedin. Önceki 180 saniyelik sorunu da ayrıca, 3 dakikadan uzun tam girdisizlikle deneyin.
3. Duraklatınca hem aktif hem oyun ön plan sayaçları durmalı. Devam ettirince duraklatılan aralık eklenmemeli. Win+L ile 1 dakika kilitleyin; ardından uyku/uyanma deneyin. Bu boşluklar iki metriğe de eklenmemeli.
4. **Tepsiye küçült** ile 10 dakika kullanın. Tepsi kontrolleri çalışmalı. Pencereyi açınca güncel toplam görünmeli. Görünür normal tablolar yaklaşık 5, oyun modunda 15 saniyede yenilenir; durum etiketleri yaklaşık saniyeliktir. Açma/tarih/sekme değişikliği yeni sorgu ister. Arka plan kayıtları bu yenilemelerden bağımsızdır.
5. Windows ölçeğini ayrı ayrı %100 ve %150 yapın. Minimum pencere boyutunda sekmeler, ayarlar, silme, eşik ve otomatik oyun modu erişilebilir olmalı. Ayarlar kaydırılabilir. Otomatik font/geometri testleri gerçek DPI geçişinin yerine geçmez.

## 3. Keşif ve otomatik oyun modu

1. **Yüklü Oyunlar** üzerinden ayrı keşif izni verin. Steam/Epic ve varsa Xbox/Bedrock örneklerini kontrol edin. İzin yokken keşif/oyun eşleştirmesi yapılmamalı. Minecraft Java ortak JVM kısıtı devam eder.
2. Yeni oyun yükleyin. İzlenen metadata dizinindeki değişiklikler birleştirilir; dakikalık zamanlayıcının sonraki uygun kontrolünü bekleyin veya elle yenileyin. İzlenmeyen ikinci kütüphane/özel launcher için **Oyunları yenile** veya saatlik yedek kontrol kullanılır. Oyun aktifse yenileme ertelenir.
3. Ayarlarda **Otomatik oyun modu** açık olsun. Katalogla güvenilir eşleşen bir oyunu ön plana alın. Pencereyi oyunun yanında/ikinci ekranda görünür tutarak, tıklamadan mod etiketini izleyin. Oyundan monitöre Alt+Tab yapmak ön planı değiştirdiği için modun kapanması normaldir.
4. Oyun ön plandayken kütüphane değişikliği oluşturulursa ağır tarama başlamamalı; başlamış tarama iptal belirtecini gördüğünde kesilmeli. Oyundan çıkınca en az 15 saniyelik sakinleşme ve sonraki dakikalık kontrol sonrasında tek yenileme yapılmalı. Bakım en az 45 saniye bekler ve taramayla aynı anda çalışmaz. Bir olay fırtınası birikmiş yüzlerce tarama üretmemeli.
5. Oyun arka plandayken süre büyümemeli. Oyun modundayken **Duraklat** iki sayacı durdurur; mod tedbir olarak açık kalabilir, ağır işler başlatılmaz. **Durdur**, kilit ve uyku modu kapatır. İzleme kapalıyken foreground okunmadığından otomatik oyun algılama da yapılmaz.

## 4. Kontrolcü ve iki metrik

1. Katalogla eşleşen oyunda eşiği geçici olarak 5 saniye yapın. 15 saniye hiçbir giriş yapmayın: **ön planda kalma** artabilir; **girişle doğrulanan aktif süre** artmamalı.
2. XInput uyumlu bir cihazınız varsa yalnızca kontrolcüyle en az 30 saniye oynayın; klavye/fareyi kullanmayın. Gözlenen tuş/stick/trigger etkinliği hareketsizliği sıfırlamalı. Kontrolcüyü nötr bırakınca eşik geçilmeli. İki süre Oyun İstatistikleri'nde ayrı sütunlarda olmalı; ön plan metriği genel aktif toplamına eklenmez.
3. XInput olarak görünmeyen cihazda “XInput algılanmadı; diğer kontrolcüler doğrulanamaz” açıklaması görülebilir. Bu durumda aktif sürenin eksik kalması bilinen sınırdır; cihazın giriş yaptığını varsaymayız. Cihaz/model/USB-Bluetooth/oyun/sonucu kaydedin. Kontrolcü yoksa bu satırı **Test edilmedi — cihaz yok** işaretleyin.
4. Bir saniyelik örnekler arasındaki kısa dokunuşlar kaçabilir; bağlı cihaz oyuna özel olarak eşlenmez. Büyük stick drift'i veya basılı kalan bir düğme etkinlik sayılabilir. Arka plandaki başka bir kişinin XInput cihazı da örneklenebilir; insanın gerçekten oyuna odaklandığını kanıtlamaz. Hiçbir tuş/kombinasyon DB'ye yazılmaz.

## 5. Silme ve kapanış

1. Yalnızca silmeyi kabul ettiğiniz test geçmişinde **Kullanım geçmişini sil → Hayır** deneyin: veri kalmalı. **Evet** ile aktif ve ön plan geçmişi temizlenmeli; izleme durmalı. Katalog, manuel eşleştirmeler ve izinler korunmalı. Sekmeleri açıp toplamları kontrol edin; yeniden açınca silinen kayıtlar dönmemeli.
2. Yükseltme yedekleri ayrı tutulur ve bu düğmeyle silinmez. Silme diyaloğu bunu belirtir; isterseniz uygulamadan çıktıktan sonra size ait yedekleri ayrıca yönetin.
3. Oyun modundayken normal çıkış yapıp yeniden açın: son doğrulanmış aktif ve ön plan süreleri kalmalı. Uygulamayı zorla kapatmayı gerçek veride denemeyin. Batch yazımında normal kapanış kuyruğu boşaltır; ani çökmede son yaklaşık 5 saniyelik henüz commit edilmemiş bölüm kaybolabilir, işletim sistemi/zamanlama/depolama hatası bu pencereyi uzatabilir.

## 6. Kaynak ölçümü ve uzun çalışma

Önce 2 dakika ısınma bekleyin. Güç planını, AC/batarya durumunu ve açık uygulamaları sabit tutun. Görev Yöneticisi'nde süreç CPU/RAM/GPU'yu, Kaynak İzleyicisi'nde gerçek disk bayt/s ve etkin süreyi gözleyin. Sistem temel ölçümünü monitör tamamen kapalıyken 2 dakika alın. Arka plan Windows güncellemeleri, antivirüs ve diğer uygulamaları not edin.

Monitörün kaynak örneklerini dosyaya almak için, açtığınız uygulamanın PID'sini Görev Yöneticisi/Ayrıntılar'dan bulun:

```powershell
.\scripts\Measure-MonitorProcess.ps1 -TargetProcessId 12345 -DurationSeconds 120
```

Bu betik yalnızca verdiğiniz sürecin sayaçlarını okur; DB, giriş veya diğer uygulama içeriği okumaz. CSV `artifacts/performance/manual` altına benzersiz adla yazılır. `CpuTotalPercent`, tek çekirdek yüzdesinin mantıksal işlemci sayısına bölümüdür. `LogicalReadBytes/LogicalWriteBytes` fiziksel disk baytı değildir. Gerçek oyun, 2 saat ve ardından mümkünse 8 saat çalışma için tekrarlayın; RAM/private bytes/handle/thread/WAL büyümesini ve kapanış sonrası süreleri kaydedin. RAM'in kısa süre dalgalanması tek başına sızıntı kanıtı değildir; tekrarlanan aynı kullanım döngülerindeki yükselen tabanı inceleyin. Otomatik ölçümdeki 90 saniye, bu uzun süre kabulünün yerine geçmez.

## 7. Aynı oyunda A/B/C FPS planı

Kullanacağınız oyunun yerleşik benchmark'ı varsa onu tercih edin. Kare zamanı kaydı gerekiyorsa [PresentMon'un resmi aracı ve belgeleri](https://github.com/GameTechDev/PresentMon) kullanılabilir; araç Windows grafik sunum olaylarını ölçer. Bu görevde kurulum veya gerçek oyun kaydı yapılmadı. Aynı ölçüm aracını ve aynı sürümünü tüm koşullarda açık tutun; ölçüm aracının kendi yükü de sabit olmalı.

| Koşul | Monitör ayarı |
|---|---|
| A | Tamamen kapalı; tepside de süreç yok |
| B | 1.2.1 açık, izleme açık, otomatik oyun modu kapalı |
| C | Aynı 1.2.1, izleme açık, otomatik oyun modu açık ve güvenilir oyun eşleşmesi var |

1. Aynı çözünürlük/grafik/ekran modu/VSync/FPS sınırı, sürücü, güç planı ve kontrol yöntemini kullanın. Dinamik çözünürlük ve benzeri uyarlamalar sonuçları etkiler; ayarlarını kaydedin. Aynı kayıt/sahne veya yerleşik benchmark kullanın; ağ oyuncularının değiştiği maçlar uygun karşılaştırma değildir.
2. Shader/önbellek ısınması için bir hazırlık turu yapın; hazırlığı sonuçlara eklemeyin. Steam/Epic güncellemelerini ve ilk keşfi ölçümden önce tamamlayın. Monitör her B/C açılışında izlemeyi ayrıca başlatmayı unutmayın.
3. Her koşulda 5 dakikalık en az 3 ölçüm alın. Sıralamayı A-B-C, C-A-B, B-C-A gibi değiştirin; sıcaklık ve CPU/GPU frekanslarını not edin. Tepsi/görünür pencere durumunu B ve C'de aynı tutun. UI etkisini ayrıca görmek için görünür pencereyle ikinci bir seri alın.
4. Ortalama FPS, **aynı araç/aynı tanımdaki 1% low**, ortalama ve P99 kare süresi (ms), oyun ve monitör CPU/RAM'i, disk etkinliği ve GPU'yu kaydedin. `1000/P99 kare süresi` değerini aracın “en yavaş %1 ortalaması” ile karıştırmayın. Kullanılan FPS/kare-zamanı tanımını rapora yazın.
5. Normal sabit oyun yükünü ve ayrı olarak bekleyen kütüphane yenilemesi bulunan yükü deneyin. C'de tarama ertelenmeli; A/B/C karşılaştırmasında değiştirdiğiniz tek değişkeni yazın. Her koşuldan sonra normal çıkın ve sürelerin korunduğunu kontrol edin.
6. Her koşulun 3 tekrarının medyanını ve en düşük/en yüksek sonucunu karşılaştırın. B−A ve C−A farklarını, C−B kazancını hesaplayın. Fark koşul içi oynaklık kadar küçükse “ölçülebilir fark doğrulanmadı” yazın; “sıfır etki” sonucu çıkarmayın.

| Koşul/tekrar | Ortalama FPS | 1% low | Ortalama/P99 ms | Monitör CPU toplam % | Working set MiB | Fiziksel disk bayt/s | GPU % |
|---|---:|---:|---:|---:|---:|---:|---:|
| A / 1–3 | Ölçülmedi | Ölçülmedi | Ölçülmedi | Süreç yok | Süreç yok | Ölçülmedi | Ölçülmedi |
| B / 1–3 | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi |
| C / 1–3 | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi | Ölçülmedi |
