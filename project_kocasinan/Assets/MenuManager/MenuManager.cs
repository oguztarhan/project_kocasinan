using UnityEngine;
using TMPro; // Buton yazıları için TextMeshPro kütüphanesi
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    [Header("Sahne Geçişi")]
    [SerializeField] private string gameSceneName = "SampleScene";

    [Header("Ayarlar Penceresi")]
    [SerializeField] private GameObject settingsPanel;

    [Header("Butonların Yazıları (TMP)")]
    [SerializeField] private TextMeshProUGUI soundText;
    [SerializeField] private TextMeshProUGUI musicText;
    [SerializeField] private TextMeshProUGUI vibText;

    // Durumlar (Açık mı, Kapalı mı?)
    private bool isSoundOn = true;
    private bool isMusicOn = true;
    private bool isVibrationOn = true;

    void Start()
    {
        // Oyun ilk açıldığında ayarlar penceresi gizli olsun
        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        // Yazıları ilk durumlarına göre güncelle (Hepsi AÇIK başlayacak)
        UpdateSettingsTexts();

        // NOTE: the runtime menu overlay (bottom nav, profile, currencies, store &
        // skin) is now built automatically by MainMenuUI.Bootstrap() as soon as the
        // MainMenu scene loads, so it appears before Play without relying on this
        // component. (Kept here intentionally empty to document the change.)
    }

    // OYUNU BAŞLAT BUTONU İÇİN (Zaten yapmıştınız)
    public void PlayGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    // AYARLAR PENCERESİNİ AÇMA / KAPATMA FONKSİYONU
    public void ToggleSettingsPanel()
    {
        if (settingsPanel != null)
        {
            // Panel aktifse kapatır, kapalıysa açar
            settingsPanel.SetActive(!settingsPanel.activeSelf);
        }
    }

    // SES BUTONU FONKSİYONU
    public void ToggleSound()
    {
        isSoundOn = !isSoundOn; // Tersine çevir (Açıksa kapalı, kapalıysa açık yap)
        UpdateSettingsTexts();  // Yazıyı ekranda güncelle
    }

    // MÜZİK BUTONU FONKSİYONU
    public void ToggleMusic()
    {
        isMusicOn = !isMusicOn;
        UpdateSettingsTexts();
    }

    // TİTREŞİM BUTONU FONKSİYONU
    public void ToggleVibration()
    {
        isVibrationOn = !isVibrationOn;
        UpdateSettingsTexts();
    }

    // YAZILARI GÜNCELLEME YARDIMCISI
    private void UpdateSettingsTexts()
    {
        if (soundText != null) soundText.text = isSoundOn ? "Ses: AÇIK" : "Ses: KAPALI";
        if (musicText != null) musicText.text = isMusicOn ? "Müzik: AÇIK" : "Müzik: KAPALI";
        if (vibText != null) vibText.text = isVibrationOn ? "Titreşim: AÇIK" : "Titreşim: KAPALI";
    }
}