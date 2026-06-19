using System.Collections.Generic;
using UnityEngine;
using BusJam;

/// <summary>
/// Central localization table. Keyed by the ENGLISH source string; each entry holds one
/// translation per language, in the SAME order as <see cref="LanguageSelector.Names"/>:
///   0 Türkçe · 1 English · 2 Deutsch · 3 Italiano · 4 Español · 5 中文 · 6 Français · 7 Português · 8 Bahasa Indonesia
///
/// Texts are translated at runtime by <see cref="LocalizedText"/> (one per on-screen Text/TMP),
/// tagged by <see cref="Localizer"/>. Selecting a language calls <see cref="SetLanguage"/>, which
/// fires <see cref="OnLanguageChanged"/> so every visible text refreshes live. No scene/baker edits.
/// </summary>
public static class Loc
{
    public static System.Action OnLanguageChanged;

    public static int Lang => Mathf.Clamp(SaveSystem.Language, 0, 8);

    public static void SetLanguage(int index)
    {
        SaveSystem.Language = Mathf.Clamp(index, 0, 8);
        OnLanguageChanged?.Invoke();
    }

    public static bool HasKey(string key) => !string.IsNullOrEmpty(key) && Table.ContainsKey(key);

    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        if (Table.TryGetValue(key, out var arr))
        {
            int i = Lang;
            if (i >= 0 && i < arr.Length && !string.IsNullOrEmpty(arr[i])) return arr[i];
            if (arr.Length > 1 && !string.IsNullOrEmpty(arr[1])) return arr[1]; // English fallback
        }
        return key; // unknown -> leave as-is (numbers, names, prices pass through)
    }

    public static string Format(string key, params object[] args) => string.Format(T(key), args);

    // tr, en, de, it, es, zh, fr, pt, id
    static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
    {
        // ---- Main menu + bottom nav ----
        { "PLAY",     new[]{ "OYNA","PLAY","SPIELEN","GIOCA","JUGAR","开始","JOUER","JOGAR","MAIN" } },
        { "DAILY",    new[]{ "GÜNLÜK","DAILY","TÄGLICH","GIORNALIERO","DIARIO","每日","QUOTIDIEN","DIÁRIO","HARIAN" } },
        { "HOME",     new[]{ "ANA SAYFA","HOME","START","HOME","INICIO","主页","ACCUEIL","INÍCIO","BERANDA" } },
        { "SHOP",     new[]{ "MAĞAZA","SHOP","SHOP","NEGOZIO","TIENDA","商店","BOUTIQUE","LOJA","TOKO" } },

        // ---- Settings / panels common ----
        { "SETTINGS", new[]{ "AYARLAR","SETTINGS","EINSTELLUNGEN","IMPOSTAZIONI","AJUSTES","设置","PARAMÈTRES","DEFINIÇÕES","PENGATURAN" } },
        { "REPLAY",   new[]{ "TEKRAR","REPLAY","NOCHMAL","RIGIOCA","REINTENTAR","重玩","REJOUER","REPETIR","ULANGI" } },
        { "LEVELS",   new[]{ "BÖLÜMLER","LEVELS","LEVEL","LIVELLI","NIVELES","关卡","NIVEAUX","NÍVEIS","LEVEL" } },
        { "LANGUAGE", new[]{ "DİL","LANGUAGE","SPRACHE","LINGUA","IDIOMA","语言","LANGUE","IDIOMA","BAHASA" } },
        { "Language", new[]{ "Dil","Language","Sprache","Lingua","Idioma","语言","Langue","Idioma","Bahasa" } }, // mixed-case variant (the TMP menu label)

        // ---- Continue / Fail / Success ----
        { "CONTINUE?",   new[]{ "DEVAM?","CONTINUE?","WEITER?","CONTINUARE?","¿SEGUIR?","继续？","CONTINUER ?","CONTINUAR?","LANJUT?" } },
        { "WATCH AD",    new[]{ "REKLAM İZLE","WATCH AD","WERBUNG ANSEHEN","GUARDA ANNUNCIO","VER ANUNCIO","观看广告","VOIR PUB","VER ANÚNCIO","TONTON IKLAN" } },
        { "FAIL",        new[]{ "BAŞARISIZ","FAIL","VERLOREN","FALLITO","FALLASTE","失败","ÉCHEC","FALHOU","GAGAL" } },
        { "ACHIEVEMENT", new[]{ "BAŞARI","ACHIEVEMENT","ERFOLG","SUCCESSO","LOGRO","成就","SUCCÈS","CONQUISTA","PENCAPAIAN" } },
        { "NEXT",        new[]{ "İLERİ","NEXT","WEITER","AVANTI","SIGUIENTE","下一关","SUIVANT","PRÓXIMO","LANJUT" } },
        { "AD  x2",      new[]{ "REKLAM  x2","AD  x2","WERBUNG  x2","ANNUNCIO  x2","ANUNCIO  x2","广告  x2","PUB  x2","ANÚNCIO  x2","IKLAN  x2" } },

        // ---- HUD ----
        { "LEVEL", new[]{ "BÖLÜM","LEVEL","LEVEL","LIVELLO","NIVEL","关卡","NIVEAU","NÍVEL","LEVEL" } },
        { "AD",    new[]{ "REKLAM","AD","WERBUNG","ANNUNCIO","ANUNCIO","广告","PUB","ANÚNCIO","IKLAN" } },
        { "COMBO x{0}!", new[]{ "KOMBO x{0}!","COMBO x{0}!","COMBO x{0}!","COMBO x{0}!","COMBO x{0}!","连击 x{0}!","COMBO x{0} !","COMBO x{0}!","COMBO x{0}!" } },

        // ---- Daily rewards ----
        { "Daily Rewards", new[]{ "Günlük Ödüller","Daily Rewards","Tägliche Belohnungen","Premi Giornalieri","Recompensas Diarias","每日奖励","Récompenses Quotidiennes","Recompensas Diárias","Hadiah Harian" } },
        { "COME BACK EVERY DAY TO GET\nGREAT REWARDS", new[]{
            "HER GÜN GELİP\nHARİKA ÖDÜLLER KAZAN","COME BACK EVERY DAY TO GET\nGREAT REWARDS","KOMM JEDEN TAG FÜR\nTOLLE BELOHNUNGEN",
            "TORNA OGNI GIORNO PER\nPREMI FANTASTICI","VUELVE CADA DÍA POR\nGRANDES RECOMPENSAS","每天回来领取\n丰厚奖励",
            "REVIENS CHAQUE JOUR POUR\nDE BELLES RÉCOMPENSES","VOLTE TODOS OS DIAS PARA\nGRANDES RECOMPENSAS","KEMBALI SETIAP HARI UNTUK\nHADIAH HEBAT" } },
        { "Recolor",   new[]{ "Renk Değiştir","Recolor","Umfärben","Ricolora","Recolorear","换色","Recolorier","Recolorir","Warnai Ulang" } },
        { "SWAP  +75", new[]{ "TAKAS  +75","SWAP  +75","TAUSCH  +75","SCAMBIO  +75","CAMBIO  +75","交换  +75","ÉCHANGE  +75","TROCA  +75","TUKAR  +75" } },
        { "Day 1", new[]{ "Gün 1","Day 1","Tag 1","Giorno 1","Día 1","第1天","Jour 1","Dia 1","Hari 1" } },
        { "Day 2", new[]{ "Gün 2","Day 2","Tag 2","Giorno 2","Día 2","第2天","Jour 2","Dia 2","Hari 2" } },
        { "Day 3", new[]{ "Gün 3","Day 3","Tag 3","Giorno 3","Día 3","第3天","Jour 3","Dia 3","Hari 3" } },
        { "Day 4", new[]{ "Gün 4","Day 4","Tag 4","Giorno 4","Día 4","第4天","Jour 4","Dia 4","Hari 4" } },
        { "Day 5", new[]{ "Gün 5","Day 5","Tag 5","Giorno 5","Día 5","第5天","Jour 5","Dia 5","Hari 5" } },
        { "Day 6", new[]{ "Gün 6","Day 6","Tag 6","Giorno 6","Día 6","第6天","Jour 6","Dia 6","Hari 6" } },
        { "Day 7", new[]{ "Gün 7","Day 7","Tag 7","Giorno 7","Día 7","第7天","Jour 7","Dia 7","Hari 7" } },

        // ---- Ad-reward popup ----
        // Key MATCHES the baked Panel_AdReward "Desc" text exactly (no "an") so Localizer auto-tags + translates it.
        { "Watch ad and earn 10 gold!", new[]{
            "Reklam izle, 10 altın kazan!","Watch an ad and earn 10 gold!","Schau eine Werbung und erhalte 10 Gold!",
            "Guarda un annuncio e guadagna 10 oro!","¡Mira un anuncio y gana 10 de oro!","观看广告赚取10金币！",
            "Regarde une pub et gagne 10 pièces !","Assista a um anúncio e ganhe 10 de ouro!","Tonton iklan dan dapatkan 10 emas!" } },
    };
}
