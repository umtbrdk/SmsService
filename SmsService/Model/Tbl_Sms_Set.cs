using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SmsService.Model
{
    internal class Tbl_Sms_Set
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string BaslikKodu { get; set; }
        public string SmsAktif { get; set; }
        public string DogumGunuAktif { get; set; }
        public int SorguGunu { get; set; }
        public int GelisSayisi { get; set; }
        public int EnKucukYas { get; set; }
        public int GonderimGunu { get; set; }
        public string GonderimSaati { get; set; }
        public int IndirimGun { get; set; }
        public int IndirimYuzde { get; set; }
        public string IndirimAktif { get; set; }
        public string CagriMerkezineBildir { get; set; }

    }
}
