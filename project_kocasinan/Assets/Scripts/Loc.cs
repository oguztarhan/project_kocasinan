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
        { "HOME",     new[]{ "ANASAYFA","HOME","START","HOME","INICIO","主页","ACCUEIL","INÍCIO","BERANDA" } }, // single-word (was "ANA SAYFA")
        { "SHOP",     new[]{ "MAĞAZA","SHOP","SHOP","NEGOZIO","TIENDA","商店","BOUTIQUE","LOJA","TOKO" } },

        // ---- Settings / panels common ----
        { "SETTINGS", new[]{ "AYARLAR","SETTINGS","EINSTELLUNGEN","IMPOSTAZIONI","AJUSTES","设置","PARAMÈTRES","DEFINIÇÕES","PENGATURAN" } },
        { "REPLAY",   new[]{ "TEKRAR","REPLAY","NOCHMAL","RIGIOCA","REINTENTAR","重玩","REJOUER","REPETIR","ULANGI" } },
        { "LEVELS",   new[]{ "BÖLÜMLER","LEVELS","LEVEL","LIVELLI","NIVELES","关卡","NIVEAUX","NÍVEIS","LEVEL" } },
        { "LANGUAGE", new[]{ "DİL","LANGUAGE","SPRACHE","LINGUA","IDIOMA","语言","LANGUE","IDIOMA","BAHASA" } },
        { "Language", new[]{ "Dil","Language","Sprache","Lingua","Idioma","语言","Langue","Idioma","Bahasa" } }, // mixed-case variant (the TMP menu label)

        // ---- Continue / Fail / Success ----
        { "CONTINUE?",   new[]{ "DEVAM?","CONTINUE?","WEITER?","CONTINUARE?","¿SEGUIR?","继续？","CONTINUER ?","CONTINUAR?","LANJUT?" } },
        { "WATCH AD",    new[]{ "İZLE","WATCH","ANSEHEN","GUARDA","VER","观看","VOIR","VER","TONTON" } }, // single-word verb (icon shows it's an ad); was 2 words and overflowed
        { "FAIL",        new[]{ "BAŞARISIZ","FAIL","VERLOREN","FALLITO","FALLASTE","失败","ÉCHEC","FALHOU","GAGAL" } },
        { "ACHIEVEMENT", new[]{ "BAŞARI","ACHIEVEMENT","ERFOLG","SUCCESSO","LOGRO","成就","SUCCÈS","CONQUISTA","PENCAPAIAN" } },
        { "NEXT",        new[]{ "İLERİ","NEXT","WEITER","AVANTI","SIGUIENTE","下一关","SUIVANT","PRÓXIMO","LANJUT" } },
        { "AD  x2",      new[]{ "x2","x2","x2","x2","x2","x2","x2","x2","x2" } }, // single token (ad icon conveys the ad); was "REKLAM x2" etc. and overflowed

        // ---- HUD ----
        { "LEVEL", new[]{ "BÖLÜM","LEVEL","LEVEL","LIVELLO","NIVEL","关卡","NIVEAU","NÍVEL","LEVEL" } },
        { "AD",    new[]{ "REKLAM","AD","WERBUNG","ANNUNCIO","ANUNCIO","广告","PUB","ANÚNCIO","IKLAN" } },
        { "COMBO x{0}!", new[]{ "KOMBO x{0}!","COMBO x{0}!","COMBO x{0}!","COMBO x{0}!","COMBO x{0}!","连击 x{0}!","COMBO x{0} !","COMBO x{0}!","COMBO x{0}!" } },

        // ---- Garage: vehicle wardrobe ("dolap") ----
        { "VEHICLES", new[]{ "ARAÇLAR","VEHICLES","FAHRZEUGE","VEICOLI","VEHÍCULOS","车辆","VÉHICULES","VEÍCULOS","KENDARAAN" } },
        { "CARS",     new[]{ "ARABALAR","CARS","AUTOS","AUTO","COCHES","汽车","VOITURES","CARROS","MOBIL" } },
        { "MINIVANS", new[]{ "MİNİVANLAR","MINIVANS","MINIVANS","MINIVAN","MINIVANS","面包车","MONOSPACES","MINIVANS","MINIVAN" } },
        { "BUSES",    new[]{ "OTOBÜSLER","BUSES","BUSSE","AUTOBUS","AUTOBUSES","巴士","BUS","ÔNIBUS","BUS" } },
        { "EQUIPPED", new[]{ "TAKILI","EQUIPPED","AUSGERÜSTET","EQUIPAGGIATO","EQUIPADO","已装备","ÉQUIPÉ","EQUIPADO","TERPASANG" } },
        { "LOCKED",   new[]{ "KİLİTLİ","LOCKED","GESPERRT","BLOCCATO","BLOQUEADO","已锁定","VERROUILLÉ","BLOQUEADO","TERKUNCI" } },
        { "From chests", new[]{ "Sandıktan","From chests","Aus Truhen","Dai forzieri","De cofres","来自宝箱","Des coffres","De baús","Dari peti" } },
        { "Run BusJam > Build Vehicle Sets", new[]{
            "Araç setleri yok — BusJam ▸ Build Vehicle Sets çalıştır","Run BusJam ▸ Build Vehicle Sets","Run BusJam ▸ Build Vehicle Sets",
            "Run BusJam ▸ Build Vehicle Sets","Run BusJam ▸ Build Vehicle Sets","Run BusJam ▸ Build Vehicle Sets",
            "Run BusJam ▸ Build Vehicle Sets","Run BusJam ▸ Build Vehicle Sets","Run BusJam ▸ Build Vehicle Sets" } },

        // ---- Daily rewards ----
        { "Daily Rewards", new[]{ "Günlük Ödüller","Daily Rewards","Tägliche Belohnungen","Premi Giornalieri","Recompensas Diarias","每日奖励","Récompenses Quotidiennes","Recompensas Diárias","Hadiah Harian" } },
        { "COME BACK EVERY DAY TO GET\nGREAT REWARDS", new[]{
            "HER GÜN GELİP\nHARİKA ÖDÜLLER KAZAN","COME BACK EVERY DAY TO GET\nGREAT REWARDS","KOMM JEDEN TAG FÜR\nTOLLE BELOHNUNGEN",
            "TORNA OGNI GIORNO PER\nPREMI FANTASTICI","VUELVE CADA DÍA POR\nGRANDES RECOMPENSAS","每天回来领取\n丰厚奖励",
            "REVIENS CHAQUE JOUR POUR\nDE BELLES RÉCOMPENSES","VOLTE TODOS OS DIAS PARA\nGRANDES RECOMPENSAS","KEMBALI SETIAP HARI UNTUK\nHADIAH HEBAT" } },
        { "Recolor",   new[]{ "Değiştir","Recolor","Umfärben","Ricolora","Recolorear","换色","Recolorier","Recolorir","Warnai Ulang" } }, // single word (was "Renk Değiştir" — overflowed the card); the swirl icon conveys "colour"
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

        // ---- Tutorial (#4 level-1 coach) ----
        { "Tap a bus to send it to a parking spot!", new[]{
            "Bir otobüse dokun, park yerine gitsin!","Tap a bus to send it to a parking spot!","Tippe auf einen Bus, um ihn zum Parkplatz zu schicken!",
            "Tocca un autobus per mandarlo al parcheggio!","¡Toca un autobús para enviarlo al aparcamiento!","点击巴士，把它送到停车位！",
            "Touche un bus pour l'envoyer au parking !","Toque num autocarro para enviá-lo à vaga!","Ketuk bus untuk mengirimnya ke tempat parkir!" } },
        { "Same-color passengers board automatically. Clear them all to win!", new[]{
            "Aynı renk yolcular otomatik biner. Hepsini bitir, kazan!","Same-color passengers board automatically. Clear them all to win!","Gleichfarbige Fahrgäste steigen automatisch ein. Schaffe alle, um zu gewinnen!",
            "I passeggeri dello stesso colore salgono da soli. Eliminali tutti per vincere!","Los pasajeros del mismo color suben solos. ¡Despeja a todos para ganar!","同色乘客会自动上车，清空所有乘客即可获胜！",
            "Les passagers de même couleur montent tout seuls. Élimine-les tous pour gagner !","Passageiros da mesma cor embarcam sozinhos. Limpe todos para vencer!","Penumpang sewarna naik otomatis. Habiskan semua untuk menang!" } },

        // ---- Tutorial (#5 level-10 bonus coach) ----
        { "Bonus round! Clear every bus before the timer runs out!", new[]{
            "Bonus tur! Süre dolmadan tüm otobüsleri temizle!","Bonus round! Clear every bus before the timer runs out!","Bonusrunde! Räume alle Busse, bevor die Zeit abläuft!",
            "Round bonus! Libera tutti gli autobus prima che scada il tempo!","¡Ronda bonus! ¡Despeja todos los autobuses antes de que acabe el tiempo!","奖励关！在时间结束前清空所有巴士！",
            "Manche bonus ! Dégage tous les bus avant la fin du temps !","Rodada bónus! Limpa todos os autocarros antes do tempo acabar!","Ronde bonus! Bersihkan semua bus sebelum waktu habis!" } },
        { "Watch out — crossing cars can crash a moving bus!", new[]{
            "Dikkat — geçen arabalar hareket eden otobüse çarpabilir!","Watch out — crossing cars can crash a moving bus!","Vorsicht — kreuzende Autos können einen fahrenden Bus rammen!",
            "Attento — le auto di passaggio possono speronare un autobus in movimento!","¡Cuidado! Los autos que cruzan pueden chocar un autobús en movimiento!","小心——横穿的汽车会撞上行驶中的巴士！",
            "Attention — les voitures qui traversent peuvent percuter un bus en mouvement !","Cuidado — carros a atravessar podem bater num autocarro em movimento!","Awas — mobil yang melintas bisa menabrak bus yang bergerak!" } },

        // ---- Tutorial (#6 joker-unlock coach) ----
        { "RECOLOR unlocked — here's 1 free! Tap it to reshuffle the buses' colours when stuck.", new[]{
            "RECOLOR açıldı — 1 tanesi bedava! Sıkışınca otobüslerin renklerini karıştırmak için dokun.","RECOLOR unlocked — here's 1 free! Tap it to reshuffle the buses' colours when stuck.","RECOLOR freigeschaltet — 1 gratis! Tippe drauf, um die Busfarben zu mischen, wenn du feststeckst.",
            "RICOLORA sbloccato — 1 gratis! Toccalo per rimescolare i colori degli autobus quando sei bloccato.","¡RECOLOR desbloqueado — 1 gratis! Tócalo para barajar los colores de los autobuses cuando te atasques.","已解锁换色道具——免费赠送1个！卡住时点击它打乱巴士颜色。",
            "RECOLORIER débloqué — 1 gratuit ! Touche-le pour mélanger les couleurs des bus quand tu es bloqué.","RECOLORIR desbloqueado — 1 grátis! Toca para baralhar as cores dos autocarros quando estiveres preso.","RECOLOR terbuka — 1 gratis! Ketuk untuk mengacak warna bus saat buntu." } },

        // ---- Tutorial (new lines: Lv1 cars-only + seat 4, Lv5 bus seat 10, Lv6 diagonals) ----
        { "Tap a car to send it to a parking spot!", new[]{
            "Bir arabaya dokun, park yerine gitsin!","Tap a car to send it to a parking spot!","Tippe auf ein Auto, um es zum Parkplatz zu schicken!",
            "Tocca un'auto per mandarla al parcheggio!","¡Toca un coche para enviarlo al aparcamiento!","点击汽车，把它送到停车位！",
            "Touche une voiture pour l'envoyer au parking !","Toque num carro para enviá-lo à vaga!","Ketuk mobil untuk mengirimnya ke tempat parkir!" } },
        { "Same-color passengers board automatically — small cars seat 4. Clear them all to win!", new[]{
            "Aynı renk yolcular otomatik biner — küçük arabalar 4 kişilik. Hepsini bitir, kazan!","Same-color passengers board automatically — small cars seat 4. Clear them all to win!","Gleichfarbige Fahrgäste steigen automatisch ein — kleine Autos fassen 4. Schaffe alle, um zu gewinnen!",
            "I passeggeri dello stesso colore salgono da soli — le auto piccole hanno 4 posti. Eliminali tutti per vincere!","Los pasajeros del mismo color suben solos — los coches pequeños tienen 4 plazas. ¡Despeja a todos para ganar!","同色乘客会自动上车——小汽车坐4人。清空所有乘客即可获胜！",
            "Les passagers de même couleur montent tout seuls — les petites voitures ont 4 places. Élimine-les tous pour gagner !","Passageiros da mesma cor embarcam sozinhos — carros pequenos levam 4. Limpe todos para vencer!","Penumpang sewarna naik otomatis — mobil kecil memuat 4. Habiskan semua untuk menang!" } },
        { "Buses seat 10 people!", new[]{
            "Otobüsler 10 kişiliktir!","Buses seat 10 people!","Busse fassen 10 Personen!",
            "Gli autobus hanno 10 posti!","¡Los autobuses tienen 10 plazas!","巴士可坐10人！",
            "Les bus ont 10 places !","Os autocarros levam 10 pessoas!","Bus memuat 10 orang!" } },
        { "New: vehicles can now move DIAGONALLY!", new[]{
            "Yeni: araçlar artık ÇAPRAZ gidebilir!","New: vehicles can now move DIAGONALLY!","Neu: Fahrzeuge können sich jetzt DIAGONAL bewegen!",
            "Novità: i veicoli ora possono muoversi in DIAGONALE!","¡Nuevo: los vehículos ahora pueden moverse en DIAGONAL!","新增：车辆现在可以斜向移动！",
            "Nouveau : les véhicules peuvent maintenant se déplacer en DIAGONALE !","Novo: os veículos agora podem mover-se na DIAGONAL!","Baru: kendaraan kini bisa bergerak DIAGONAL!" } },
        { "Bonus round! Clear every bus before time runs out — and don't hit the cars crossing the road!", new[]{
            "Bonus tur! Süre dolmadan tüm otobüsleri gönder — ve yoldan geçen arabalara çarpma!","Bonus round! Clear every bus before time runs out — and don't hit the cars crossing the road!","Bonusrunde! Schaffe alle Busse, bevor die Zeit abläuft — und ramme nicht die kreuzenden Autos!",
            "Round bonus! Libera tutti gli autobus prima che scada il tempo — e non urtare le auto che attraversano!","¡Ronda bonus! Despeja todos los autobuses antes de que acabe el tiempo y no choques con los autos que cruzan!","奖励关！在时间结束前清空所有巴士——别撞上横穿马路的汽车！",
            "Manche bonus ! Dégage tous les bus avant la fin du temps — et ne percute pas les voitures qui traversent !","Rodada bónus! Limpa todos os autocarros antes do tempo acabar — e não batas nos carros que atravessam!","Ronde bonus! Bersihkan semua bus sebelum waktu habis — dan jangan tabrak mobil yang menyeberang!" } },

        // ---- Special-bonus intros (CoinRush / TimeAttack / MysteryRush). ADDED 2026-07-13 — these were MISSING from the
        //      table, so on those levels (e.g. Lv65 = MysteryRush) the banner showed English on every language. The
        //      tr/de/it/es/fr/pt lines are solid; zh + id are AI-generated — have a native speaker verify them.
        { "Coin Rush! Clear the heart jam for a chest — then stop the bar on GOLD!", new[]{
            "Coin Rush! Kalp yığınını temizle, sandığı kap — sonra çubuğu ALTIN'da durdur!","Coin Rush! Clear the heart jam for a chest — then stop the bar on GOLD!","Coin Rush! Räume den Herz-Stau für eine Truhe — stopp dann den Balken auf GOLD!",
            "Coin Rush! Libera l'ingorgo a cuore per uno scrigno — poi ferma la barra sull'ORO!","¡Coin Rush! ¡Despeja el atasco de corazón por un cofre y detén la barra en ORO!","金币狂潮！清空爱心车阵赢取宝箱——然后把指针停在金色区域！",
            "Coin Rush ! Dégage l'embouteillage en cœur pour un coffre — puis arrête la barre sur l'OR !","Coin Rush! Limpa o engarrafamento em coração por um baú — depois para a barra no OURO!","Coin Rush! Bersihkan tumpukan berbentuk hati untuk peti — lalu hentikan bilah di EMAS!" } },
        { "Time Attack! Clear the jam FAST — a quicker time = a better chest!", new[]{
            "Time Attack! Yığını HIZLI temizle — daha kısa süre = daha iyi sandık!","Time Attack! Clear the jam FAST — a quicker time = a better chest!","Time Attack! Räume den Stau SCHNELL — schnellere Zeit = bessere Truhe!",
            "Time Attack! Libera l'ingorgo VELOCE — tempo più rapido = scrigno migliore!","¡Time Attack! ¡Despeja el atasco RÁPIDO — menos tiempo = mejor cofre!","极速挑战！快速清空车阵——用时越短，宝箱越好！",
            "Time Attack ! Dégage l'embouteillage VITE — un temps plus court = un meilleur coffre !","Time Attack! Limpa o engarrafamento RÁPIDO — tempo menor = baú melhor!","Time Attack! Bersihkan tumpukan dengan CEPAT — waktu lebih cepat = peti lebih bagus!" } },
        { "Mystery Rush! Every car is GRAY — send them out to reveal their colour, then grab a chest!", new[]{
            "Mystery Rush! Tüm arabalar GRİ — rengini ortaya çıkarmak için onları gönder, sonra sandığı kap!","Mystery Rush! Every car is GRAY — send them out to reveal their colour, then grab a chest!","Mystery Rush! Jedes Auto ist GRAU — schick sie raus, um ihre Farbe zu enthüllen, und schnapp dir eine Truhe!",
            "Mystery Rush! Ogni auto è GRIGIA — mandale fuori per svelarne il colore, poi prendi uno scrigno!","¡Mystery Rush! Cada coche es GRIS — envíalos para revelar su color y llévate un cofre!","神秘狂潮！所有汽车都是灰色——把它们送出去显示颜色，然后夺取宝箱！",
            "Mystery Rush ! Chaque voiture est GRISE — envoie-les pour révéler leur couleur, puis rafle un coffre !","Mystery Rush! Cada carro é CINZENTO — envia-os para revelar a cor e agarra um baú!","Mystery Rush! Semua mobil ABU-ABU — kirim keluar untuk mengungkap warnanya, lalu ambil peti!" } },
    };
}
