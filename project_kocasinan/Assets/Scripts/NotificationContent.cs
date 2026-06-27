using UnityEngine;

namespace BusJam
{
    public struct NotifText { public string title; public string body; }

    /// <summary>
    /// The 13 local re-engagement notifications, localized to the 9 game languages (same index order as Loc:
    /// 0 tr · 1 en · 2 de · 3 it · 4 es · 5 zh · 6 fr · 7 pt · 8 id). <see cref="NotificationService"/> resolves the
    /// text via <see cref="Get"/> at SCHEDULE time, so each reminder is written in the player's CURRENT language.
    /// English (index 1) is the fallback for any empty slot.
    /// </summary>
    public static class NotificationContent
    {
        public static int Count => Titles.Length;

        public static NotifText Get(int i)
        {
            i = Mathf.Clamp(i, 0, Titles.Length - 1);
            int l = Mathf.Clamp(SaveSystem.Language, 0, 8);
            return new NotifText { title = Pick(Titles[i], l), body = Pick(Bodies[i], l) };
        }

        static string Pick(string[] a, int l) => (l < a.Length && !string.IsNullOrEmpty(a[l])) ? a[l] : a[1];

        // [notification][language]  — keep Titles and Bodies in the same order.
        static readonly string[][] Titles =
        {
            new[]{ "Otobüsler seni bekliyor! 🚌","The buses are waiting! 🚌","Die Busse warten! 🚌","Gli autobus ti aspettano! 🚌","¡Los autobuses te esperan! 🚌","巴士在等你！🚌","Les bus t'attendent ! 🚌","Os ônibus estão esperando! 🚌","Bus menunggumu! 🚌" }, // 0
            new[]{ "Seni özledik! 💜","We miss you! 💜","Wir vermissen dich! 💜","Ci manchi! 💜","¡Te extrañamos! 💜","我们想你了！💜","Tu nous manques ! 💜","Sentimos sua falta! 💜","Kami merindukanmu! 💜" }, // 1
            new[]{ "Uzun zaman oldu! 🎁","It's been a while! 🎁","Lange nicht gesehen! 🎁","È passato un po'! 🎁","¡Cuánto tiempo! 🎁","好久不见！🎁","Ça fait longtemps ! 🎁","Quanto tempo! 🎁","Sudah lama ya! 🎁" }, // 2
            new[]{ "Hâlâ buradayız! 🚍","We're still here! 🚍","Wir sind noch da! 🚍","Siamo ancora qui! 🚍","¡Seguimos aquí! 🚍","我们还在哦！🚍","On est toujours là ! 🚍","Ainda estamos aqui! 🚍","Kami masih di sini! 🚍" }, // 3
            new[]{ "Bedava sandık zamanı! 🎁","Free chest time! 🎁","Gratis-Truhe! 🎁","Forziere gratis! 🎁","¡Cofre gratis! 🎁","免费宝箱时间！🎁","Coffre gratuit ! 🎁","Baú grátis! 🎁","Waktunya peti gratis! 🎁" }, // 4 (free chest)
            new[]{ "Günlük ödülün hazır! 📅","Your daily reward is ready! 📅","Deine Tagesbelohnung ist da! 📅","Premio giornaliero pronto! 📅","¡Tu recompensa diaria está lista! 📅","每日奖励已就绪！📅","Ta récompense du jour ! 📅","Recompensa diária pronta! 📅","Hadiah harianmu siap! 📅" }, // 5
            new[]{ "Seriyi bozma! 🔥","Keep your streak! 🔥","Halte deine Serie! 🔥","Non perdere la serie! 🔥","¡Mantén tu racha! 🔥","保持连签！🔥","Garde ta série ! 🔥","Mantenha a sequência! 🔥","Jaga streak-mu! 🔥" }, // 6
            new[]{ "Garajını büyüt! 🚗","Grow your garage! 🚗","Erweitere deine Garage! 🚗","Espandi il garage! 🚗","¡Amplía tu garaje! 🚗","扩充你的车库！🚗","Agrandis ton garage ! 🚗","Amplie sua garagem! 🚗","Perbesar garasimu! 🚗" }, // 7
            new[]{ "Koleksiyonu tamamla! 🏆","Complete your collection! 🏆","Vervollständige die Sammlung! 🏆","Completa la collezione! 🏆","¡Completa tu colección! 🏆","集齐你的收藏！🏆","Complète ta collection ! 🏆","Complete sua coleção! 🏆","Lengkapi koleksimu! 🏆" }, // 8
            new[]{ "Yeni bölüm seni bekliyor! 🚀","A new level awaits! 🚀","Ein neues Level wartet! 🚀","Un nuovo livello ti aspetta! 🚀","¡Un nuevo nivel te espera! 🚀","新关卡在等你！🚀","Un nouveau niveau t'attend ! 🚀","Um novo nível espera! 🚀","Level baru menunggu! 🚀" }, // 9
            new[]{ "Pes etme! 💪","Don't give up! 💪","Gib nicht auf! 💪","Non mollare! 💪","¡No te rindas! 💪","别放弃！💪","N'abandonne pas ! 💪","Não desista! 💪","Jangan menyerah! 💪" }, // 10
            new[]{ "Bonus bölüm zamanı! ⏱️","Bonus level time! ⏱️","Bonuslevel-Zeit! ⏱️","È ora del bonus! ⏱️","¡Hora del nivel bonus! ⏱️","奖励关时间到！⏱️","C'est l'heure du bonus ! ⏱️","Hora do nível bônus! ⏱️","Waktunya level bonus! ⏱️" }, // 11
            new[]{ "Bir molaya ne dersin? 😎","Time for a break? 😎","Zeit für eine Pause? 😎","Una pausa? 😎","¿Un descanso? 😎","休息一下？😎","Une petite pause ? 😎","Que tal uma pausa? 😎","Mau rehat sejenak? 😎" }, // 12
        };

        static readonly string[][] Bodies =
        {
            new[]{ "Yolcular sırada, hadi bir bölüm temizle!","Passengers are lining up — clear a level!","Die Fahrgäste stehen Schlange — räum ein Level!","I passeggeri sono in fila, libera un livello!","Los pasajeros hacen fila, ¡despeja un nivel!","乘客在排队，快来通关吧！","Les passagers font la queue, termine un niveau !","Os passageiros estão na fila, limpe um nível!","Penumpang mengantre, ayo selesaikan level!" }, // 0
            new[]{ "Garajındaki araçlar tozlanıyor, geri dön!","Your garage cars are gathering dust — come back!","Deine Garagenautos verstauben — komm zurück!","Le auto in garage si impolverano, torna!","Tus autos juntan polvo, ¡vuelve!","车库里的车都积灰了，快回来吧！","Tes voitures prennent la poussière, reviens !","Seus carros estão juntando poeira, volte!","Mobil di garasimu berdebu, ayo kembali!" }, // 1
            new[]{ "Biriken hediyelerin seni bekliyor, gel ve topla.","Your rewards have piled up — come collect them.","Deine Belohnungen haben sich angehäuft — hol sie ab.","I tuoi premi si sono accumulati, vieni a prenderli.","Tus recompensas se acumularon, ven por ellas.","你的奖励攒了一堆，快来领取吧。","Tes récompenses se sont accumulées, viens les chercher.","Suas recompensas se acumularam, venha buscá-las.","Hadiahmu menumpuk, ayo ambil." }, // 2
            new[]{ "Yeni bölümler ve araçlar seni bekliyor, bir el at?","New levels and cars await — fancy a round?","Neue Level und Autos warten — eine Runde?","Nuovi livelli e auto ti aspettano, una partita?","Nuevos niveles y autos te esperan, ¿una partida?","新关卡和新车在等你，来一局？","De nouveaux niveaux et voitures t'attendent, une partie ?","Novos níveis e carros esperam, que tal jogar?","Level dan mobil baru menunggu, main yuk?" }, // 3
            new[]{ "Belki bu sefer efsanevi bir araç çıkar!","Maybe a legendary car this time!","Vielleicht gibt's diesmal ein legendäres Auto!","Magari stavolta esce un'auto leggendaria!","¡Quizá esta vez salga un auto legendario!","也许这次能开出传说级座驾！","Peut-être une voiture légendaire cette fois !","Talvez um carro lendário desta vez!","Mungkin kali ini dapat mobil legendaris!" }, // 4
            new[]{ "Bugünün hediyesini almayı unutma!","Don't forget to grab today's gift!","Vergiss dein heutiges Geschenk nicht!","Non scordare il regalo di oggi!","¡No olvides el regalo de hoy!","别忘了领取今天的礼物！","N'oublie pas le cadeau du jour !","Não esqueça o presente de hoje!","Jangan lupa ambil hadiah hari ini!" }, // 5
            new[]{ "Günlük ödül serin tehlikede, bugünkünü al!","Your daily streak is at risk — claim today's!","Deine Tagesserie ist in Gefahr — hol dir die von heute!","La tua serie giornaliera è a rischio, prendi quella di oggi!","Tu racha diaria está en riesgo, ¡reclama la de hoy!","你的每日连签快断了，快来领取！","Ta série quotidienne est en danger, récupère celle d'aujourd'hui !","Sua sequência diária está em risco, pegue a de hoje!","Streak harianmu terancam, ambil hari ini!" }, // 6
            new[]{ "Sandıklardan yeni araçların kilidini aç!","Unlock new cars from the chests!","Schalte neue Autos aus den Truhen frei!","Sblocca nuove auto dai forzieri!","¡Desbloquea autos nuevos en los cofres!","从宝箱中解锁新车吧！","Débloque de nouvelles voitures dans les coffres !","Desbloqueie novos carros nos baús!","Buka mobil baru dari peti!" }, // 7
            new[]{ "Efsanevi araçları yakalamaya ne kadar yakınsın?","How close are you to the legendary cars?","Wie nah bist du an den legendären Autos?","Quanto manca alle auto leggendarie?","¿Cuánto te falta para los autos legendarios?","离传说级座驾还有多远？","À quel point es-tu proche des voitures légendaires ?","Quão perto você está dos carros lendários?","Seberapa dekat kamu dengan mobil legendaris?" }, // 8
            new[]{ "Bir sonraki seviyeyi geçebilir misin?","Can you beat the next level?","Schaffst du das nächste Level?","Riesci a superare il prossimo livello?","¿Puedes pasar el siguiente nivel?","你能通过下一关吗？","Peux-tu réussir le niveau suivant ?","Você consegue passar o próximo nível?","Bisakah kamu menaklukkan level berikutnya?" }, // 9
            new[]{ "O bölümü bu sefer geçeceksin, hadi dene!","You'll beat that level this time — give it a go!","Diesmal schaffst du das Level — versuch's!","Stavolta superi quel livello, riprova!","Esta vez pasarás ese nivel, ¡inténtalo!","这次你一定能过关，再试一次！","Cette fois tu vas réussir ce niveau, essaie !","Desta vez você passa esse nível, tente!","Kali ini kamu pasti lolos, coba lagi!" }, // 10
            new[]{ "Süreli bonus ve ekstra ödüller seni bekliyor!","A timed bonus and extra rewards await!","Ein Zeit-Bonus und Extra-Belohnungen warten!","Un bonus a tempo ed extra premi ti aspettano!","¡Un bonus por tiempo y premios extra te esperan!","限时奖励和额外大奖在等你！","Un bonus chronométré et des récompenses t'attendent !","Um bônus por tempo e prêmios extras esperam!","Bonus berbatas waktu dan hadiah ekstra menunggu!" }, // 11
            new[]{ "5 dakikan mı var? Tam BusJam zamanı!","Got 5 minutes? Perfect for some BusJam!","5 Minuten Zeit? Perfekt für BusJam!","Hai 5 minuti? Perfetti per BusJam!","¿Tienes 5 minutos? ¡Ideal para BusJam!","有5分钟吗？正好玩把BusJam！","5 minutes de libre ? Parfait pour BusJam !","Tem 5 minutos? Perfeito para BusJam!","Punya 5 menit? Pas buat main BusJam!" }, // 12
        };
    }
}
