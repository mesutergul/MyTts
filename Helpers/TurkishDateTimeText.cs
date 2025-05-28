using System.Globalization;

namespace MyTts.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    public static class TurkishDateTimeText
    {
        public static string GunlukSeslendirmeMetniOlustur(DateTime zaman)
        {
            string gunAdi = zaman.ToString("dddd", new CultureInfo("tr-TR")); // Örn: Çarşamba
            string gunYazi = GunSayisiYaziyaCevir(zaman.Day);                 // Örn: on beş
            string ayAdi = zaman.ToString("MMMM", new CultureInfo("tr-TR"));  // Örn: Mayıs
            string yilYazi = YiliYaziyaCevir(zaman.Year);                     // Örn: iki bin yirmi beş
            string saatYazi = SaatVeDakikaYaziyaCevir(zaman);                 // Örn: on beş otuz sekiz

            return $"Bugün {gunYazi} {ayAdi} {yilYazi}, {gunAdi}. Saat şu anda {saatYazi}.";
        }

        public static string YiliYaziyaCevir(int yil)
        {
            // Sabit: "iki bin"
            int sonIki = yil % 100;
            string yazi = "iki bin";

            if (sonIki > 0)
                yazi += " " + GunSayisiYaziyaCevir(sonIki);

            return yazi;
        }

        public static string SaatVeDakikaYaziyaCevir(DateTime zaman)
        {
            // two-digit strings
            string hh = zaman.Hour.ToString("D2");   // e.g. "01" or "10"
            string mm = zaman.Minute.ToString("D2"); // e.g. "03" or "15"

            string hoursText;
            if (hh[0] == '0')
            {
                // digit-by-digit for 0x
                hoursText = $"{GunSayisiYaziyaCevir(0)} {GunSayisiYaziyaCevir(hh[1] - '0')}";
            }
            else
            {
                // whole number for >=10
                hoursText = GunSayisiYaziyaCevir(zaman.Hour);
            }

            //string minutesText;
            //if (mm[0] == '0')
            //{
            //    // digit-by-digit for 0x
            //    minutesText = $"{GunSayisiYaziyaCevir(0)} {GunSayisiYaziyaCevir(mm[1] - '0')}";
            //}
            //else
            //{
            //    // whole number for >=10
            //    minutesText = GunSayisiYaziyaCevir(zaman.Minute);
            //}

            //return $"{hoursText} {minutesText}".Trim();
            return $"{hoursText} sıfır sıfır".Trim();
        }


        public static string GunSayisiYaziyaCevir(int sayi)
        {
            if (sayi < 0 || sayi > 99)
                return sayi.ToString();

            string[] birler = { "", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz" };
            string[] onlar = { "", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan" };

            int on = sayi / 10;
            int bir = sayi % 10;

            string sonuc = onlar[on];
            if (bir > 0)
                sonuc += " " + birler[bir];

            return sonuc.Trim();
        }
        public static int GetCompactTimeId(DateTime dt) => int.Parse(dt.ToString("yyyyMMddHH"));
        //public static int GetCompactTimeId(DateTime dt)
        //{
        //    return int.Parse($"{dt.Month:D2}{dt.Day:D2}{dt.Hour:D2}{dt.Minute:D2}");
        //}
    }

}
