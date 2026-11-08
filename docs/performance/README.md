# Ölçüm dosyaları

- `baseline.json`: 1.2 optimizasyonundan **önce**, ayrı `.tools/perf-baseline` binarileriyle alınmış başlangıç; değiştirilmedi.
- `baseline-binaries.sha256.json`: o yerel dondurulmuş binarilerin hash'leri. `.tools` Git dışında kalır; eski normal yayın ayrıca `artifacts/windows-1.2` içinde korunur.
- `optimized-first.json`: ilk asenkron/buffered iyileştirme; 4 tarama tamamlandı.
- `optimized-second.json`: değişmeyen katalog SQL'ini atlayan sürüm; 4 tarama tamamlandı.
- `optimized.json`: son kıyas; ölçüm bitiminde asenkron taramanın pencere kapanışıyla iptal edilmesi engellendi, 5 tarama tamamlandı. Normal uygulama UI'si tarama beklemez.
- `preserved-releases.sha256.json`: eski üç yayın exe'sinin son yayın öncesi hash'leri; sonrasında tekrar kontrol edilir.

Gerçek oyun/GPU/FPS yoktur. I/O alanları süreç mantıksal baytlarıdır; fiziksel disk değildir. `system-only` satırında process CPU/RAM null, sistem CPU ayrı alandadır. Null alanları sıfır gibi yorumlamayın. Phase'ler aynı süreçte sırayla çalışır; JIT/OS cache önceki fazlardan etkilenebilir. Her faz yeni sentetik DB/Form kullanır; setup ve warmup ölçümden önce yapılır. Kapanışın son worker flush'ı pencere dışına kalabilir. Tek koşudan güven aralığı çıkarmayın.

Son ölçüm silme/bakım senaryosunu çalıştırmaz. Silme sonrası ertelenmiş WAL truncate yolu ayrıca otomatik testle doğrulandı. Yöntem, sonuçlar ve sınırlar [ana raporda](../performance-report-1.2.1.md); tekrar komutları [test belgesinde](../testing.md).
