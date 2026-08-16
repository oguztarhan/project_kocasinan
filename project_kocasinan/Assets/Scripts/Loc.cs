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

        // ---- Remove-Ads popup (Panel_RemoveAds): title + the banner-only offer ----
        { "REMOVE ADS",    new[]{ "REKLAMLARI KALDIR","REMOVE ADS","WERBUNG ENTFERNEN","RIMUOVI ANNUNCI","QUITAR ANUNCIOS","移除广告","SUPPRIMER LES PUBS","REMOVER ANÚNCIOS","HAPUS IKLAN" } },
        { "Remove Banner", new[]{ "Banner'ı Kaldır","Remove Banner","Banner entfernen","Rimuovi banner","Quitar banner","移除横幅广告","Supprimer la bannière","Remover banner","Hapus banner" } },

        // ---- "Did you like the game? / Rate us" prompt (RateUs) ----
        { "ENJOYING THE GAME?", new[]{ "OYUNU BEĞENDİN Mİ?","ENJOYING THE GAME?","GEFÄLLT DIR DAS SPIEL?","TI PIACE IL GIOCO?","¿TE GUSTA EL JUEGO?","喜欢这款游戏吗？","LE JEU TE PLAÎT ?","ESTÁ GOSTANDO DO JOGO?","SUKA GAME INI?" } },
        { "Are you having fun so far? Your answer helps us make the game better.",
          new[]{ "Buraya kadar eğlendin mi? Cevabın oyunu daha iyi yapmamıza yardımcı oluyor.",
                 "Are you having fun so far? Your answer helps us make the game better.",
                 "Hast du bisher Spaß? Deine Antwort hilft uns, das Spiel besser zu machen.",
                 "Ti stai divertendo finora? La tua risposta ci aiuta a migliorare il gioco.",
                 "¿Te estás divirtiendo? Tu respuesta nos ayuda a mejorar el juego.",
                 "玩得开心吗？你的回答能帮助我们把游戏做得更好。",
                 "Tu t'amuses bien ? Ta réponse nous aide à améliorer le jeu.",
                 "Está se divertindo até agora? Sua resposta nos ajuda a melhorar o jogo.",
                 "Seru nggak sejauh ini? Jawabanmu membantu kami membuat game ini lebih baik." } },
        { "YES", new[]{ "EVET","YES","JA","SÌ","SÍ","喜欢","OUI","SIM","YA" } },
        { "NO",  new[]{ "HAYIR","NO","NEIN","NO","NO","不喜欢","NON","NÃO","TIDAK" } },
        { "AWESOME!", new[]{ "HARİKA!","AWESOME!","SUPER!","FANTASTICO!","¡GENIAL!","太好了！","SUPER !","QUE ÓTIMO!","KEREN!" } },
        { "Then please rate us and leave a comment on the store. It only takes a moment and it really helps us.",
          new[]{ "O zaman bize puan ver ve mağazada bir yorum bırak. Sadece birkaç saniye sürüyor ve bize çok yardımcı oluyor.",
                 "Then please rate us and leave a comment on the store. It only takes a moment and it really helps us.",
                 "Dann bewerte uns bitte und hinterlasse einen Kommentar im Store. Es dauert nur einen Moment und hilft uns sehr.",
                 "Allora valutaci e lascia un commento sullo store. Ci vuole un attimo e ci aiuta davvero.",
                 "Entonces valóranos y deja un comentario en la tienda. Solo toma un momento y nos ayuda mucho.",
                 "那就请为我们打个分，并在商店里留下评论吧。只需片刻，却对我们帮助很大。",
                 "Alors note-nous et laisse un commentaire sur le store. Ça prend un instant et ça nous aide beaucoup.",
                 "Então avalie e deixe um comentário na loja. Leva só um instante e ajuda muito.",
                 "Kalau begitu beri rating dan tulis komentar di store. Cuma sebentar dan sangat membantu kami." } },
        { "SORRY TO HEAR THAT", new[]{ "BUNU DUYDUĞUMUZA ÜZÜLDÜK","SORRY TO HEAR THAT","DAS TUT UNS LEID","CI DISPIACE","LAMENTAMOS ESCUCHAR ESO","很抱歉","NOUS SOMMES DÉSOLÉS","SENTIMOS MUITO","MAAF MENDENGARNYA" } },
        { "Please rate us and tell us what went wrong in a comment. We read every one and we will fix it.",
          new[]{ "Lütfen bize puan ver ve yorumda neyin ters gittiğini anlat. Hepsini okuyoruz ve düzelteceğiz.",
                 "Please rate us and tell us what went wrong in a comment. We read every one and we will fix it.",
                 "Bitte bewerte uns und schreib im Kommentar, was schiefgelaufen ist. Wir lesen jeden und beheben es.",
                 "Valutaci e scrivi nel commento cosa non ha funzionato. Li leggiamo tutti e lo sistemeremo.",
                 "Valóranos y cuéntanos en un comentario qué salió mal. Leemos todos y lo arreglaremos.",
                 "请给我们打个分，并在评论里告诉我们哪里出了问题。我们会认真阅读并加以改进。",
                 "Note-nous et dis-nous en commentaire ce qui n'a pas marché. On lit tout et on corrigera.",
                 "Avalie e conte num comentário o que deu errado. Lemos todos e vamos corrigir.",
                 "Beri rating dan tulis di komentar apa yang kurang. Kami baca semuanya dan akan memperbaikinya." } },
        { "RATE US", new[]{ "PUAN VER","RATE US","BEWERTEN","VALUTACI","VALÓRANOS","去评分","NOUS NOTER","AVALIAR","BERI RATING" } },
        { "WRITE A REVIEW", new[]{ "YORUM YAZ","WRITE A REVIEW","BEWERTUNG SCHREIBEN","SCRIVI UNA RECENSIONE","ESCRIBIR RESEÑA","写评论","LAISSER UN AVIS","ESCREVER AVALIAÇÃO","TULIS ULASAN" } },
        { "ASK ME LATER", new[]{ "SONRA SOR","ASK ME LATER","SPÄTER FRAGEN","CHIEDIMELO DOPO","PREGÚNTAME LUEGO","以后再问","PLUS TARD","PERGUNTE DEPOIS","TANYA NANTI" } },
        { "NEVER ASK AGAIN", new[]{ "BİR DAHA SORMA","NEVER ASK AGAIN","NIE WIEDER FRAGEN","NON CHIEDERMELO PIÙ","NO PREGUNTAR MÁS","不再询问","NE PLUS DEMANDER","NÃO PERGUNTAR MAIS","JANGAN TANYA LAGI" } },

        // ---- Settings: Color-blind toggle label ----
        { "Color Blind", new[]{ "Renk Körü","Color Blind","Farbenblind","Daltonici","Daltónico","色盲模式","Daltonien","Daltônico","Buta Warna" } },

        // ---- Settings / panels common ----
        { "SETTINGS", new[]{ "AYARLAR","SETTINGS","EINSTELLUNGEN","IMPOSTAZIONI","AJUSTES","设置","PARAMÈTRES","DEFINIÇÕES","PENGATURAN" } },
        { "REPLAY",   new[]{ "TEKRAR","REPLAY","NOCHMAL","RIGIOCA","REINTENTAR","重玩","REJOUER","REPETIR","ULANGI" } },
        { "LEVELS",   new[]{ "BÖLÜMLER","LEVELS","LEVEL","LIVELLI","NIVELES","关卡","NIVEAUX","NÍVEIS","LEVEL" } },
        { "SELECT LEVEL", new[]{ "BÖLÜM SEÇ","SELECT LEVEL","LEVEL WÄHLEN","SCEGLI LIVELLO","ELIGE NIVEL","选择关卡","CHOISIR NIVEAU","ESCOLHE O NÍVEL","PILIH LEVEL" } },
        { "RESTORE PURCHASES", new[]{ "SATIN ALIMLARI GERİ YÜKLE","RESTORE PURCHASES","KÄUFE WIEDERHERSTELLEN","RIPRISTINA ACQUISTI","RESTAURAR COMPRAS","恢复购买","RESTAURER LES ACHATS","RESTAURAR COMPRAS","PULIHKAN PEMBELIAN" } },
        { "RESTORED", new[]{ "GERİ YÜKLENDİ","RESTORED","WIEDERHERGESTELLT","RIPRISTINATO","RESTAURADO","已恢复","RESTAURÉ","RESTAURADO","DIPULIHKAN" } },
        { "LOADING",  new[]{ "YÜKLENİYOR","LOADING","LÄDT","CARICAMENTO","CARGANDO","加载中","CHARGEMENT","A CARREGAR","MEMUAT" } },
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
        { "GARAGE",   new[]{ "GARAJ","GARAGE","GARAGE","GARAGE","GARAJE","车库","GARAGE","GARAGEM","GARASI" } },
        { "VEHICLES", new[]{ "ARAÇLAR","VEHICLES","FAHRZEUGE","VEICOLI","VEHÍCULOS","车辆","VÉHICULES","VEÍCULOS","KENDARAAN" } },

        // ---- Garage tutorial (first open) ----
        { "TAP TO CONTINUE", new[]{ "DEVAM İÇİN DOKUN","TAP TO CONTINUE","ZUM FORTFAHREN TIPPEN","TOCCA PER CONTINUARE","TOCA PARA CONTINUAR","点击继续","TOUCHEZ POUR CONTINUER","TOQUE PARA CONTINUAR","KETUK UNTUK LANJUT" } },
        { "Welcome to your GARAGE! Tap VEHICLES to see and EQUIP the cars, minivans and buses you own.",
          new[]{ "GARAJINA hoş geldin! Sahip olduğun arabaları, minivanları ve otobüsleri görmek ve KUŞANMAK için ARAÇLAR'a dokun.",
                 "Welcome to your GARAGE! Tap VEHICLES to see and EQUIP the cars, minivans and buses you own.",
                 "Willkommen in deiner GARAGE! Tippe auf FAHRZEUGE, um deine Autos, Minivans und Busse zu sehen und AUSZURÜSTEN.",
                 "Benvenuto nel tuo GARAGE! Tocca VEICOLI per vedere ed EQUIPAGGIARE le tue auto, minivan e autobus.",
                 "¡Bienvenido a tu GARAJE! Toca VEHÍCULOS para ver y EQUIPAR tus coches, minivans y autobuses.",
                 "欢迎来到车库！点击“车辆”查看并装备你拥有的汽车、面包车和巴士。",
                 "Bienvenue dans ton GARAGE ! Touche VÉHICULES pour voir et ÉQUIPER tes voitures, minivans et bus.",
                 "Bem-vindo à sua GARAGEM! Toque em VEÍCULOS para ver e EQUIPAR seus carros, minivans e ônibus.",
                 "Selamat datang di GARASI! Ketuk KENDARAAN untuk melihat dan MEMAKAI mobil, minivan, dan bus milikmu." } },
        { "Open chests with gold to win NEW vehicles — the better the chest, the rarer the prize!",
          new[]{ "YENİ araçlar kazanmak için altınla sandık aç — sandık ne kadar iyiyse ödül o kadar nadir!",
                 "Open chests with gold to win NEW vehicles — the better the chest, the rarer the prize!",
                 "Öffne Truhen mit Gold und gewinne NEUE Fahrzeuge — je besser die Truhe, desto seltener der Preis!",
                 "Apri i forzieri con l'oro per vincere NUOVI veicoli — più bello il forziere, più raro il premio!",
                 "¡Abre cofres con oro para ganar vehículos NUEVOS! Cuanto mejor el cofre, más raro el premio.",
                 "用金币打开宝箱赢取新车辆——宝箱越好，奖励越稀有！",
                 "Ouvre des coffres avec de l'or pour gagner de NOUVEAUX véhicules — plus le coffre est beau, plus le prix est rare !",
                 "Abra baús com ouro para ganhar veículos NOVOS — quanto melhor o baú, mais raro o prêmio!",
                 "Buka peti dengan emas untuk memenangkan kendaraan BARU — makin bagus petinya, makin langka hadiahnya!" } },
        { "The FREE CHEST refills over time. Come back and open it — it costs nothing!",
          new[]{ "ÜCRETSİZ SANDIK zamanla yenilenir. Geri gel ve aç — hiçbir şeye mal olmaz!",
                 "The FREE CHEST refills over time. Come back and open it — it costs nothing!",
                 "Die GRATIS-TRUHE füllt sich mit der Zeit. Komm wieder und öffne sie — sie kostet nichts!",
                 "Il FORZIERE GRATIS si ricarica col tempo. Torna ad aprirlo — non costa nulla!",
                 "El COFRE GRATIS se recarga con el tiempo. ¡Vuelve y ábrelo, no cuesta nada!",
                 "免费宝箱会随时间恢复。记得回来打开——完全免费！",
                 "Le COFFRE GRATUIT se recharge avec le temps. Reviens l'ouvrir — il ne coûte rien !",
                 "O BAÚ GRÁTIS recarrega com o tempo. Volte e abra — não custa nada!",
                 "PETI GRATIS terisi ulang seiring waktu. Kembalilah dan buka — gratis!" } },
        { "Duplicate vehicles turn into shards. Spend shards here to CRAFT a guaranteed NEW car!",
          new[]{ "Tekrarlanan araçlar parçaya dönüşür. Parçaları burada harcayıp garantili YENİ bir araba ÜRET!",
                 "Duplicate vehicles turn into shards. Spend shards here to CRAFT a guaranteed NEW car!",
                 "Doppelte Fahrzeuge werden zu Splittern. Gib sie hier aus und FERTIGE garantiert ein NEUES Auto!",
                 "I veicoli doppi diventano frammenti. Spendili qui per CREARE un'auto NUOVA garantita!",
                 "Los vehículos repetidos se convierten en fragmentos. ¡Gástalos aquí para CREAR un coche NUEVO garantizado!",
                 "重复的车辆会变成碎片。在这里用碎片打造一辆全新的车！",
                 "Les véhicules en double deviennent des fragments. Dépense-les ici pour FABRIQUER une NOUVELLE voiture garantie !",
                 "Veículos repetidos viram fragmentos. Gaste-os aqui para CRIAR um carro NOVO garantido!",
                 "Kendaraan duplikat berubah jadi pecahan. Gunakan di sini untuk MEMBUAT mobil BARU yang dijamin!" } },
        { "CARS",     new[]{ "ARABALAR","CARS","AUTOS","AUTO","COCHES","汽车","VOITURES","CARROS","MOBIL" } },
        { "MINIVANS", new[]{ "MİNİVANLAR","MINIVANS","MINIVANS","MINIVAN","MINIVANS","面包车","MONOSPACES","MINIVANS","MINIVAN" } },
        { "BUSES",    new[]{ "OTOBÜSLER","BUSES","BUSSE","AUTOBUS","AUTOBUSES","巴士","BUS","ÔNIBUS","BUS" } },
        { "EQUIPPED", new[]{ "TAKILI","EQUIPPED","AUSGERÜSTET","EQUIPAGGIATO","EQUIPADO","已装备","ÉQUIPÉ","EQUIPADO","TERPASANG" } },
        { "LOCKED",   new[]{ "KİLİTLİ","LOCKED","GESPERRT","BLOCCATO","BLOQUEADO","已锁定","VERROUILLÉ","BLOQUEADO","TERKUNCI" } },
        { "From chests", new[]{ "Sandıktan","From chests","Aus Truhen","Dai forzieri","De cofres","来自宝箱","Des coffres","De baús","Dari peti" } },

        // ---- Garage: chests + craft (section headers, chest cards, reveal popup) ----
        { "CHESTS",     new[]{ "SANDIKLAR","CHESTS","TRUHEN","FORZIERI","COFRES","宝箱","COFFRES","BAÚS","PETI" } },
        { "CRAFT",      new[]{ "ÜRET","CRAFT","HERSTELLEN","CREA","CREAR","打造","FABRIQUER","CRIAR","BUAT" } },
        { "BRONZE",     new[]{ "BRONZ","BRONZE","BRONZE","BRONZO","BRONCE","青铜","BRONZE","BRONZE","PERUNGGU" } },
        { "SILVER",     new[]{ "GÜMÜŞ","SILVER","SILBER","ARGENTO","PLATA","白银","ARGENT","PRATA","PERAK" } },
        { "GOLD",       new[]{ "ALTIN","GOLD","GOLD","ORO","ORO","黄金","OR","OURO","EMAS" } },
        { "LEGENDARY",  new[]{ "EFSANEVİ","LEGENDARY","LEGENDÄR","LEGGENDARIO","LEGENDARIO","传说","LÉGENDAIRE","LENDÁRIO","LEGENDARIS" } },
        { "EPIC",       new[]{ "DESTANSI","EPIC","EPISCH","EPICO","ÉPICO","史诗","ÉPIQUE","ÉPICO","EPIK" } },
        { "UNCOMMON",   new[]{ "NADİR","UNCOMMON","SELTEN","NON COMUNE","POCO COMÚN","罕见","PEU COMMUN","INCOMUM","TAK UMUM" } },
        { "COMMON",     new[]{ "SIRADAN","COMMON","GEWÖHNLICH","COMUNE","COMÚN","普通","COMMUN","COMUM","UMUM" } },
        { "OPEN",       new[]{ "AÇ","OPEN","ÖFFNEN","APRI","ABRIR","打开","OUVRIR","ABRIR","BUKA" } },
        { "key only",   new[]{ "sadece anahtarla","key only","nur mit Schlüssel","solo con chiave","solo con llave","仅限钥匙","clé uniquement","só com chave","hanya kunci" } },
        { "FIND A KEY", new[]{ "ANAHTAR BUL","FIND A KEY","FINDE EINEN SCHLÜSSEL","TROVA UNA CHIAVE","CONSIGUE UNA LLAVE","找到钥匙","TROUVE UNE CLÉ","ENCONTRA UMA CHAVE","CARI KUNCI" } },
        { "FREE CHEST", new[]{ "BEDAVA SANDIK","FREE CHEST","GRATIS-TRUHE","FORZIERE GRATIS","COFRE GRATIS","免费宝箱","COFFRE GRATUIT","BAÚ GRÁTIS","PETI GRATIS" } },
        { "{0} left",   new[]{ "{0} kaldı","{0} left","{0} übrig","{0} rimasti","{0} restantes","剩余{0}","{0} restants","{0} restantes","{0} tersisa" } },
        { "NEW!",       new[]{ "YENİ!","NEW!","NEU!","NUOVO!","¡NUEVO!","新车！","NOUVEAU !","NOVO!","BARU!" } },
        { "CRAFTED!",   new[]{ "ÜRETİLDİ!","CRAFTED!","HERGESTELLT!","CREATO!","¡CREADO!","打造成功！","FABRIQUÉ !","CRIADO!","DIBUAT!" } },
        { "YOU GOT",    new[]{ "KAZANDIN","YOU GOT","DU BEKOMMST","HAI OTTENUTO","HAS GANADO","获得","TU AS OBTENU","GANHASTE","KAMU DAPAT" } },
        { "OK",         new[]{ "TAMAM","OK","OK","OK","OK","好的","OK","OK","OKE" } },
        { "DUPLICATE  +{0} shards", new[]{
            "KOPYA  +{0} parça","DUPLICATE  +{0} shards","DOPPELT  +{0} Splitter","DOPPIONE  +{0} frammenti","REPETIDO  +{0} fragmentos","重复  +{0}碎片","DOUBLON  +{0} fragments","REPETIDO  +{0} fragmentos","DUPLIKAT  +{0} pecahan" } },
        { "+1 {0} KEY!", new[]{
            "+1 {0} ANAHTAR!","+1 {0} KEY!","+1 {0}-SCHLÜSSEL!","+1 CHIAVE {0}!","¡+1 LLAVE {0}!","+1把{0}钥匙！","+1 CLÉ {0} !","+1 CHAVE {0}!","+1 KUNCI {0}!" } },
        { "DROP RATES", new[]{ "DÜŞME ORANLARI","DROP RATES","DROP-CHANCEN","PROBABILITÀ","PROBABILIDADES","掉落概率","TAUX DE DROP","PROBABILIDADES","PELUANG DROP" } },

        // ---- Bonus reward flow (stop-the-bar + "you won a chest" screen) ----
        { "YOU WON",            new[]{ "KAZANDIN","YOU WON","DU HAST GEWONNEN","HAI VINTO","HAS GANADO","你赢得了","TU AS GAGNÉ","GANHASTE","KAMU MENANG" } },
        { "STOP ON GOLD!",      new[]{ "ALTINDA DURDUR!","STOP ON GOLD!","STOPP AUF GOLD!","FERMATI SULL'ORO!","¡PARA EN EL ORO!","停在黄金区！","ARRÊTE SUR L'OR !","PARA NO OURO!","BERHENTI DI EMAS!" } },
        { "Tap to stop the bar",new[]{ "Çubuğu durdurmak için dokun","Tap to stop the bar","Tippe, um den Balken zu stoppen","Tocca per fermare la barra","Toca para detener la barra","点击停止滑条","Touche pour arrêter la barre","Toca para parar a barra","Ketuk untuk menghentikan bilah" } },
        // Dynamic key: Loc.T(tier.ToString().ToUpper() + " CHEST!") in GameUI.Bonus.cs -> all four tiers needed.
        { "BRONZE CHEST!",      new[]{ "BRONZ SANDIK!","BRONZE CHEST!","BRONZE-TRUHE!","FORZIERE DI BRONZO!","¡COFRE DE BRONCE!","青铜宝箱！","COFFRE DE BRONZE !","BAÚ DE BRONZE!","PETI PERUNGGU!" } },
        { "SILVER CHEST!",      new[]{ "GÜMÜŞ SANDIK!","SILVER CHEST!","SILBER-TRUHE!","FORZIERE D'ARGENTO!","¡COFRE DE PLATA!","白银宝箱！","COFFRE D'ARGENT !","BAÚ DE PRATA!","PETI PERAK!" } },
        { "GOLD CHEST!",        new[]{ "ALTIN SANDIK!","GOLD CHEST!","GOLD-TRUHE!","FORZIERE D'ORO!","¡COFRE DE ORO!","黄金宝箱！","COFFRE D'OR !","BAÚ DE OURO!","PETI EMAS!" } },
        { "LEGENDARY CHEST!",   new[]{ "EFSANEVİ SANDIK!","LEGENDARY CHEST!","LEGENDÄRE TRUHE!","FORZIERE LEGGENDARIO!","¡COFRE LEGENDARIO!","传说宝箱！","COFFRE LÉGENDAIRE !","BAÚ LENDÁRIO!","PETI LEGENDARIS!" } },
        { "EPIC or better guaranteed every {0} opens.", new[]{
            "Her {0} açılışta DESTANSI veya üstü garantili.","EPIC or better guaranteed every {0} opens.","Alle {0} Öffnungen garantiert EPISCH oder besser.",
            "EPICO o superiore garantito ogni {0} aperture.","ÉPICO o mejor garantizado cada {0} aperturas.","每开启{0}次必得史诗或更高。",
            "ÉPIQUE ou mieux garanti toutes les {0} ouvertures.","ÉPICO ou melhor garantido a cada {0} aberturas.","EPIK atau lebih dijamin setiap {0} kali buka." } },
        { "Privacy options", new[]{
            "Gizlilik seçenekleri","Privacy options","Datenschutzoptionen","Opzioni privacy","Opciones de privacidad","隐私选项","Options de confidentialité","Opções de privacidade","Opsi privasi" } },
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
        // Daily-card captions (composed at runtime by DailyRewards.LabelFor — keep them SHORT, the cards are narrow).
        { "RECOLOR", new[]{ "DEĞİŞTİR","RECOLOR","UMFÄRBEN","RICOLORA","RECOLOR","换色","RECOLORIER","RECOLORIR","WARNAI" } },
        { "SWAP",    new[]{ "TAKAS","SWAP","TAUSCH","SCAMBIO","CAMBIO","交换","ÉCHANGE","TROCA","TUKAR" } },
        { "HELI",    new[]{ "HELİ","HELI","HELI","ELICO","HELI","直升机","HÉLICO","HELI","HELI" } },
        { "JOKERS",  new[]{ "JOKER","JOKERS","JOKER","JOLLY","COMODINES","道具","JOKERS","CORINGAS","JOKER" } },
        { "BRONZE KEY",    new[]{ "BRONZ ANAHTAR","BRONZE KEY","BRONZE-SCHLÜSSEL","CHIAVE BRONZO","LLAVE BRONCE","青铜钥匙","CLÉ BRONZE","CHAVE BRONZE","KUNCI PERUNGGU" } },
        { "SILVER KEY",    new[]{ "GÜMÜŞ ANAHTAR","SILVER KEY","SILBER-SCHLÜSSEL","CHIAVE ARGENTO","LLAVE PLATA","白银钥匙","CLÉ ARGENT","CHAVE PRATA","KUNCI PERAK" } },
        { "GOLD KEY",      new[]{ "ALTIN ANAHTAR","GOLD KEY","GOLD-SCHLÜSSEL","CHIAVE ORO","LLAVE ORO","黄金钥匙","CLÉ OR","CHAVE OURO","KUNCI EMAS" } },
        { "LEGENDARY KEY", new[]{ "EFSANE ANAHTAR","LEGENDARY KEY","LEGENDÄRER SCHLÜSSEL","CHIAVE LEGGENDARIA","LLAVE LEGENDARIA","传奇钥匙","CLÉ LÉGENDAIRE","CHAVE LENDÁRIA","KUNCI LEGENDARIS" } },
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
        { "SWAP unlocked — here's 1 free! Tap it to shuffle the waiting people's order.", new[]{
            "SWAP açıldı — 1 tanesi bedava! Bekleyen insanların sırasını karıştırmak için dokun.","SWAP unlocked — here's 1 free! Tap it to shuffle the waiting people's order.","SWAP freigeschaltet — 1 gratis! Tippe drauf, um die Reihenfolge der wartenden Personen zu mischen.",
            "SCAMBIO sbloccato — 1 gratis! Toccalo per mescolare l'ordine delle persone in attesa.","¡SWAP desbloqueado — 1 gratis! Tócalo para barajar el orden de la gente que espera.","已解锁换位道具——免费赠送1个！点击它打乱排队乘客的顺序。",
            "ÉCHANGE débloqué — 1 gratuit ! Touche-le pour mélanger l'ordre des personnes qui attendent.","TROCA desbloqueada — 1 grátis! Toca para baralhar a ordem das pessoas em espera.","SWAP terbuka — 1 gratis! Ketuk untuk mengacak urutan orang yang menunggu." } },
        { "HELICOPTER unlocked — here's 1 free! Tap it to airlift a vehicle straight onto a stop.", new[]{
            "HELİKOPTER açıldı — 1 tanesi bedava! Bir aracı doğrudan durağa taşımak için dokun.","HELICOPTER unlocked — here's 1 free! Tap it to airlift a vehicle straight onto a stop.","HELIKOPTER freigeschaltet — 1 gratis! Tippe drauf, um ein Fahrzeug direkt zu einer Haltestelle zu fliegen.",
            "ELICOTTERO sbloccato — 1 gratis! Toccalo per trasportare un veicolo dritto a una fermata.","¡HELICÓPTERO desbloqueado — 1 gratis! Tócalo para llevar un vehículo directo a una parada.","已解锁直升机道具——免费赠送1个！点击它把一辆车直接吊到站台。",
            "HÉLICOPTÈRE débloqué — 1 gratuit ! Touche-le pour transporter un véhicule droit vers un arrêt.","HELICÓPTERO desbloqueado — 1 grátis! Toca para levar um veículo direto a uma paragem.","HELIKOPTER terbuka — 1 gratis! Ketuk untuk mengangkut kendaraan langsung ke halte." } },
        { "Nice! Here's +1 free joker — use it anytime.", new[]{
            "Harika! +1 bedava joker daha — istediğin zaman kullan.","Nice! Here's +1 free joker — use it anytime.","Super! Hier ist +1 Gratis-Joker — nutze ihn jederzeit.",
            "Ottimo! Ecco +1 jolly gratis — usalo quando vuoi.","¡Genial! Aquí tienes +1 comodín gratis — úsalo cuando quieras.","太棒了！再送你1个免费道具——随时使用。",
            "Bien joué ! Voici +1 joker gratuit — utilise-le quand tu veux.","Boa! Aqui tens +1 curinga grátis — usa quando quiseres.","Mantap! Ini +1 joker gratis — pakai kapan saja." } },

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
        { "Minivans seat 6 people!", new[]{
            "Minivanlar 6 kişiliktir!","Minivans seat 6 people!","Minivans fassen 6 Personen!",
            "I minivan hanno 6 posti!","¡Las minivans tienen 6 plazas!","面包车可坐6人！",
            "Les monospaces ont 6 places !","As minivans levam 6 pessoas!","Minivan memuat 6 orang!" } },
        { "New: vehicles can now move DIAGONALLY!", new[]{
            "Yeni: araçlar artık ÇAPRAZ gidebilir!","New: vehicles can now move DIAGONALLY!","Neu: Fahrzeuge können sich jetzt DIAGONAL bewegen!",
            "Novità: i veicoli ora possono muoversi in DIAGONALE!","¡Nuevo: los vehículos ahora pueden moverse en DIAGONAL!","新增：车辆现在可以斜向移动！",
            "Nouveau : les véhicules peuvent maintenant se déplacer en DIAGONALE !","Novo: os veículos agora podem mover-se na DIAGONAL!","Baru: kendaraan kini bisa bergerak DIAGONAL!" } },
        // Lv10 bonus intro. REWRITTEN 2026-07-17 when that bonus went 2-colour (yellow fill + red core) -> 4-colour: it now
        // names the 4-colour jam, and says "vehicles" instead of "buses" (the board is ~40% cars / 30% minivans / 30%
        // buses, so "buses" was already wrong). Key must stay byte-identical to the BusJamGame coach.ShowText string.
        { "Bonus round! A 4-colour jam — clear every vehicle before time runs out, and don't hit the cars crossing the road!", new[]{
            "Bonus tur! 4 renkli tıkanıklık — süre dolmadan tüm araçları gönder ve yoldan geçen arabalara çarpma!","Bonus round! A 4-colour jam — clear every vehicle before time runs out, and don't hit the cars crossing the road!","Bonusrunde! Ein Stau in 4 Farben — schaffe alle Fahrzeuge, bevor die Zeit abläuft, und ramme nicht die kreuzenden Autos!",
            "Round bonus! Un ingorgo a 4 colori — libera tutti i veicoli prima che scada il tempo e non urtare le auto che attraversano!","¡Ronda bonus! Un atasco de 4 colores: despeja todos los vehículos antes de que acabe el tiempo y no choques con los autos que cruzan!","奖励关！四色车阵——在时间结束前清空所有车辆，别撞上横穿马路的汽车！",
            "Manche bonus ! Un embouteillage à 4 couleurs — dégage tous les véhicules avant la fin du temps et ne percute pas les voitures qui traversent !","Rodada bónus! Um engarrafamento de 4 cores — limpa todos os veículos antes do tempo acabar e não batas nos carros que atravessam!","Ronde bonus! Tumpukan 4 warna — bersihkan semua kendaraan sebelum waktu habis dan jangan tabrak mobil yang menyeberang!" } },

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
