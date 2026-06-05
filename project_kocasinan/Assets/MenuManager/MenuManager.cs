using UnityEngine;
using UnityEngine.SceneManagement; // Sahneler arası geçiş yapmak için bu kütüphane şart!

public class MenuManager : MonoBehaviour
{
    // Bu fonksiyonu ortadaki büyük "Oyna" butonuna bağlayacağız
    public void StartGame()
    {
        // e7459a90-387f-4427-b03c-6ede6e1cda7a görselindeki listende 1 numarada SampleScene vardı.
        // O yüzden buraya tam olarak o sahnenin adını yazıyoruz.
        SceneManager.LoadScene("SampleScene");
    }
}