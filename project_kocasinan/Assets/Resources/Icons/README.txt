Mağaza (STORE) ikonu buraya gelecek.

Dosya adı tam olarak: store.png
Tam yol: Assets/Resources/Icons/store.png

- PNG, şeffaf arka plan (transparent), kare (örn. 256x256 veya 512x512).
- Dosyayı buraya kopyaladıktan sonra Unity'de Texture Type = "Sprite (2D and UI)"
  olmalı. (3D/URP projelerinde varsayılan "Default" gelir; Sprite yapılmazsa
  kod ikonu bulamaz ve otomatik olarak yazılı "STORE" butonuna döner.)

Kod ikonu su sekilde yukler: Resources.Load<Sprite>("Icons/store")
