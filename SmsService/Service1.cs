using Oracle.ManagedDataAccess.Client;
using SmsService.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;
using System.Runtime.Remoting.Messaging;
using System.Net.Http;

namespace SmsService
{
    public partial class Service1 : ServiceBase
    {
        public Service1()
        {
            InitializeComponent();
        }
        string dbAdres, dbSifre, dbKullAdi;
        string _gonderen = ""; string _pass = ""; string _port = ""; string _ssl = ""; string _host = ""; string _name = "";
        string _smsUserName = ""; string _smsPass = ""; string _smsBaslik = ""; bool testMode = false;
        int _timerSure = 10;
        private System.Threading.Timer dogumGunuTimer;
        private System.Threading.Timer indirimSilTimer;


        List<DGunuList> dgSmsList = new List<DGunuList>();
        List<Tbl_Sms_Set> smsSet = new List<Tbl_Sms_Set>();
        List<SmsBildirimMailList> smsBildirimMailLists = new List<SmsBildirimMailList>();
        List<HastaneList> hastaneList = new List<HastaneList>();
        List<SmsSablonlar> smsSablonList = new List<SmsSablonlar>();
        protected override void OnStart(string[] args)
        {
            LogKlasorCreate();

            BaglantiAyarlari();

            MailAyarlariOkuSQL();
            DgMailListOku();
            HastaneListGetir();
            if (smsSet.Count > 0 && smsSet[0].SmsAktif == "T") SablonListGetir();

            LogInsert("ServisStart", "Servis çalışmaya başladı. Ver.09/02/24 10:25");
            if (testMode)
            {
                if(smsSet.Count > 0)
                {
                    Tbl_Sms_Set settSms = smsSet[0];
                    LogInsert("SMS Ayarları", $" UserName : {settSms.Username} Pass : {settSms.Password} Header : {settSms.BaslikKodu}");
                }
                
                DGunuHizmetSQL();
            }

            if (smsSet.Count > 0 && smsSet[0].DogumGunuAktif == "T") DogumGunuKontrol();
            if (smsSet.Count > 0 && smsSet[0].IndirimAktif == "T") IndirimKontrol();
        }
        protected override void OnStop()
        {
            LogInsert("ServisStop", "Servis durduruldu.");
            dogumGunuTimer.Dispose();
            indirimSilTimer.Dispose();
        }
        public void DogumGunuKontrol()
        {
            string basSaat = smsSet[0].GonderimSaati;
            DateTime parsBasSaat;
            DateTime now = DateTime.Now;

            if (DateTime.TryParseExact(basSaat, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsBasSaat))
            {
                DateTime scheduledTime = new DateTime(now.Year, now.Month, now.Day, 10, 0, 0);
                TimeSpan timeUntilFirstRun = scheduledTime - now;

                // Eğer şu anki saat 10:00'dan önceyse, ilk çalıştırma zamanını bir gün ekleyerek ayarla
                if (timeUntilFirstRun.TotalMilliseconds < 0)
                {
                    timeUntilFirstRun = timeUntilFirstRun.Add(TimeSpan.FromDays(1));
                }
                LogInsert("Doğum Günü Kontrol Aktif", "İlk Kontrol için kalan süre : " + timeUntilFirstRun.ToString());

                // Timer'ı oluştur ve ilk çalıştırma zamanını ayarla
                dogumGunuTimer = new System.Threading.Timer(DogumGunu_Timer_Tick, null, (int)timeUntilFirstRun.TotalMilliseconds, 24 * 60 * 60 * 1000); // Her 24 saatte bir tekrar et
            }
        }

        public void IndirimKontrol()
        {
            // Yeni timer'ı oluştur ve her gece saat 01:00'da çalıştır
            DateTime now = DateTime.Now;
            DateTime scheduledIndirimSilTime = new DateTime(now.Year, now.Month, now.Day, 1, 0, 0);
            TimeSpan timeUntilIndirimSilFirstRun = scheduledIndirimSilTime - now;

            if (timeUntilIndirimSilFirstRun.TotalMilliseconds < 0)
            {
                timeUntilIndirimSilFirstRun = timeUntilIndirimSilFirstRun.Add(TimeSpan.FromDays(1));
            }
            // Timer'ı oluştur ve ilk çalıştırma zamanını ayarla
            LogInsert("Indirim Liste Kontrolü Aktif", "İlk Kontrol için kalan süre : " + timeUntilIndirimSilFirstRun.ToString());

            indirimSilTimer = new System.Threading.Timer(IndirimSil_Timer_Tick, null, (int)timeUntilIndirimSilFirstRun.TotalMilliseconds, 24 * 60 * 60 * 1000); // Her 24 saatte bir tekrar et

        }
        private void IndirimSil_Timer_Tick(object state)
        {
            for (int i = 0; i < hastaneList.Count; i++)
            {
                HastaneList _hastaneList = hastaneList[i];
                Database.ConnStr(_hastaneList.ipAdres, Database.dbKullAdi, Database.dbSifre);
                IndirimSilSQL(_hastaneList.HastaneAdi);
            }

            HastaneList _hastane = hastaneList[0];
            Database.ConnStr(_hastane.ipAdres, Database.dbKullAdi, Database.dbSifre);
            LogInsert("Silme İşlemi", $"{hastaneList.Count} şubeden indirim silme işlemi tamamlandı.");
        }

        private void IndirimSilSQL(string hastaneAdi)
        {
            try
            {
                // Güncel tarih
                DateTime now = DateTime.Now;

                // İndirim silinecek tarih limiti (örneğin, 30 gün önceki tarih)
                int indirimGun = smsSet[0].IndirimGun;
                DateTime silinecekTarihLimit = now.AddDays(-indirimGun);

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand())
                {
                    conn.Open();
                    cmd.Connection = conn;

                    // SQL sorgusu: Silinecek tarihten önceki kayıtları sil
                    cmd.CommandText = "DELETE FROM HASTANE.INDIRIM_LISTESI WHERE CREATEDATE < :silinecekTarih";
                    cmd.Parameters.Add("silinecekTarih", OracleDbType.Date).Value = silinecekTarihLimit;

                    int rowsAffected = cmd.ExecuteNonQuery();
                    LogInsert("IndirimSil", $"{hastaneAdi} şubesinden {rowsAffected} adet indirim kaydı silindi. Silinen tarih limiti: {silinecekTarihLimit}");
                }
            }
            catch (Exception ex)
            {
                // Hata durumunda loglama
                LogInsert("IndirimSil - Hata", ex.Message);
            }
        }

        private void DogumGunu_Timer_Tick(object state)
        {
            DGunuHizmetSQL();
        }
        public async void DGunuHizmetSQL()
        {
            LogInsert("DGunuHizmetSQL", "Doğum günü kişi listesi oluşturulmaya başladı.");

            string cmdtxt = @"SELECT SUBE, TC_KIMLIK_NO, UPPER(ADI) ADI, UPPER(SOYADI) SOYADI, CEP_TEL, E_MAIL, TO_CHAR(DOGUM_TAR,'DD.MM.YYYY') DOGUM_TAR, GELIS_SAYISI, MAX(SON_GTARIH) SON_GTARIH, YAS, MERSISNO, SMSRED FROM HASTANE.VW_DGUNU_LIST
            WHERE LENGTH(CEP_TEL) > 9 AND YAS > 15 and CEP_TEL LIKE '05%'
            GROUP BY SUBE, TC_KIMLIK_NO, UPPER(ADI), UPPER(SOYADI), CEP_TEL, E_MAIL, TO_CHAR(DOGUM_TAR,'DD.MM.YYYY'), GELIS_SAYISI, YAS, MERSISNO, SMSRED ORDER BY ADI, SUBE";
            System.Data.DataTable dataTable = new System.Data.DataTable();

            try
            {
                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxt, conn))
                {
                    conn.Open();

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.HasRows)
                        {
                            dataTable.Load(dr);

                        }
                    }
                    conn.Close();
                    cmd.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogInsert("DGunuHizmetSQL", "Doğum günü kişi listesi oluşturulma sırasında hata : " + ex.Message);
            }
            finally
            {
                LogInsert("DGunuHizmetSQL", "Doğum günü kişi listesi oluşturuldu. Listedeki kişi sayısı : " + dataTable.Rows.Count);
                int smsAdet = 0;

                if (dataTable.Rows.Count > 0)
                {
                    foreach (DataRow row in dataTable.Rows)
                    {
                        string sube = row["SUBE"].ToString();
                        string tcNo = row["TC_KIMLIK_NO"].ToString();
                        string adi = row["ADI"].ToString();
                        string soyadi = row["SOYADI"].ToString();
                        string cepTel = row["CEP_TEL"].ToString();
                        string eMail = row["E_MAIL"].ToString();
                        string dogumTar = row["DOGUM_TAR"].ToString();
                        string gelisSayisi = row["GELIS_SAYISI"].ToString();
                        string sonGtarih = row["SON_GTARIH"].ToString();
                        string yas = row["YAS"].ToString();
                        string mersis = row["MERSISNO"].ToString();
                        string smsRed = row["SMSRED"].ToString();

                        string rowMessage = $"Şube: {sube} | " +
                                            $"TC No: {tcNo} | " +
                                            $"Adı: {adi} | " +
                                            $"Soyadı: {soyadi} | " +
                                            $"Cep Telefonu: {cepTel} | " +
                                            $"E-Mail: {eMail} | " +
                                            $"Doğum Tarihi: {dogumTar} | " +
                                            $"Geliş Sayısı: {gelisSayisi} | " +
                                            $"Son Geliş Tarihi: {sonGtarih} | " +
                                            $"Yaş: {yas} | " +
                                            $"MersisNo: {mersis} | " +
                                            $"SmsRed: {smsRed} | ";

                        // İNDİRİM_LİSTESİ TANIMLAMA İÇİN
                        if (smsSet.Count > 0 && smsSet[0].IndirimAktif == "T")
                        {
                            for (int i = 0; i < hastaneList.Count; i++)
                            {
                                HastaneList _hastaneList = hastaneList[i];
                                if (testMode) LogInsert("HastaneListInsert", $"{_hastaneList.HastaneAdi} için indirim tanımlanıyor. TC.No : {tcNo}");
                                DGindirimInsert(tcNo, adi, soyadi, "Doğum Günü İndirimi", smsSet[0].IndirimYuzde, _hastaneList.ipAdres);
                            }
                        }
                        if (smsSet.Count > 0 && smsSet[0].SmsAktif == "T") // SMS GÖNDERİMİ
                        {
                           
                            if (testMode && smsAdet == 1 )
                            {
                                LogInsert("SMS Adet",$"{smsAdet}");
                                int indirimSure = 0;
                                int indirimYuzde = 0;
                                if (smsSet[0].IndirimGun > 0) indirimSure = smsSet[0].IndirimGun;
                                if (smsSet[0].IndirimYuzde > 0) indirimYuzde = smsSet[0].IndirimYuzde;
                                for (int i = 0; i < smsSablonList.Count; i++)
                                {
                                    if(testMode) LogInsert("SMS Gönder", $"1. Adım. Şablon döngüsü başladı. Toplam Şablon Adet : {smsSablonList.Count}");
                                    SmsSablonlar _sablon = smsSablonList[i];
                                    string msj = SmsSablonHazirla(_sablon.SmsSablon, adi, soyadi, yas, indirimSure, indirimYuzde,mersis, smsRed);
                                    if (testMode) LogInsert("SMS Gönder", $"2. Adım Şablon oluştu : {msj}");

                                   await smsGonder("05377005383", msj);
                                   await smsGonder("05323083908", msj);
                                }
                                smsAdet++;
                            }
                            else if (!testMode)
                            {
                                int indirimSure = 0;
                                int indirimYuzde = 0;
                                if (smsSet[0].IndirimGun > 0) indirimSure = smsSet[0].IndirimGun;
                                if (smsSet[0].IndirimYuzde > 0) indirimYuzde = smsSet[0].IndirimYuzde;
                                for (int i = 0; i < smsSablonList.Count; i++)
                                {
                                    SmsSablonlar _sablon = smsSablonList[i];
                                    string msj = SmsSablonHazirla(_sablon.SmsSablon, adi, soyadi, yas, indirimSure, indirimYuzde, mersis, smsRed);
                                    await smsGonder(cepTel, msj);
                                }
                            }
                        }

                        if (testMode) LogInsert("Test", rowMessage);
                    }
                    HastaneList _hastaneLista = hastaneList[0];
                    Database.ConnStr(_hastaneLista.ipAdres, Database.dbKullAdi, Database.dbSifre);
                    if (testMode) LogInsert("Db Değişikliği", $"Güncel ConnectionString : {Database.connstr}");

                }
                if (smsSet.Count > 0 && smsSet[0].CagriMerkezineBildir == "T") // ÇAĞRI MERKEZİ BİLDİRİM MAİLİ İÇİN
                {
                    string htmlTable = "<h3>Sayın yetkili; </h3>";
                    htmlTable += "<h3>" + DateTime.Now.ToShortDateString() + " tarihli doğum günü olan hasta listesi aşağıdaki tabloya eklenmiştir. Bu liste Tc Kimlik numaraları ile eşleştirilip Meddata da hasta dosyalarına tanımlanmıştır.</h3>";
                    if (smsSet != null && smsSet[0].IndirimGun > 0) htmlTable += "<h4> " + dataTable.Rows.Count + " hasta için tanımlanan Meddata hatırlatması, " + smsSet[0].IndirimGun + " gün sonra otomatik sonra erecektir.</h4>";
                    htmlTable += "<table border='1'><tr>";

                    foreach (DataColumn column in dataTable.Columns)
                    {
                        htmlTable += "<th>" + column.ColumnName + "</th>";
                    }
                    htmlTable += "</tr>";
                    foreach (DataRow row in dataTable.Rows)
                    {
                        htmlTable += "<tr>";
                        foreach (DataColumn column in dataTable.Columns)
                        {
                            htmlTable += "<td>" + row[column] + "</td>";
                        }
                        htmlTable += "</tr>";
                    }
                    htmlTable += "</table>";
                    htmlTable += "<p> </p>";
                    htmlTable += "<p><em>Not: Bu rapor otomatik hazırlanıp tarafınıza gönderilmektedir. Problem olduğunu düşünüyorsanız bilgi işlem birimi ile irtibata geçiniz.</em></p>";

                    DgListMailGonder(htmlTable, DateTime.Now.ToShortDateString());
                }
            }
            LogInsert("DGunuHizmetSQL", "Metot tamamlandı.");
        }

        public void DGindirimInsert(string tcKimlikNo, string adi, string soyadi, string aciklama, int muayene, string ipAdres)
        {
            Database.ConnStr(ipAdres, Database.dbKullAdi, Database.dbSifre);

            string cmdInsert = @"
                INSERT INTO HASTANE.INDIRIM_LISTESI
                ( HASTANE_KODU, TC_KIMLIK_NO, ADI, SOYADI, ACIKLAMA, MUAYENE )
                VALUES
                ( :HASTANE_KODU, :TC_KIMLIK_NO, :ADI, :SOYADI, :ACIKLAMA, :MUAYENE )";

            try
            {
                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdInsert, conn))
                {
                    conn.Open();

                    cmd.Parameters.Add(new OracleParameter(":HASTANE_KODU", OracleDbType.Int32)).Value = 1;
                    cmd.Parameters.Add(new OracleParameter(":TC_KIMLIK_NO", OracleDbType.Int32)).Value = tcKimlikNo;
                    cmd.Parameters.Add(new OracleParameter(":ADI", OracleDbType.Varchar2)).Value = adi;
                    cmd.Parameters.Add(new OracleParameter(":SOYADI", OracleDbType.Varchar2)).Value = soyadi;
                    cmd.Parameters.Add(new OracleParameter(":ACIKLAMA", OracleDbType.Varchar2)).Value = aciklama;
                    cmd.Parameters.Add(new OracleParameter(":MUAYENE", OracleDbType.Int32)).Value = muayene;

                    int rowsAffected = cmd.ExecuteNonQuery();
                    if (rowsAffected > 0 && testMode) LogInsert("İndirim Tanımı Yapılan Kişi", tcKimlikNo + " " + adi + " " + soyadi);
                }
            }
            catch (Exception ex)
            {
                LogInsert("IndirimTanımında Hata", ex.Message);
            }
        }
        public async Task smsGonder(string tel, string msg)
        {


            Tbl_Sms_Set settSms = smsSet[0];

            string ss = "";
            ss += "<?xml version='1.0' encoding='UTF-8'?>";
            ss += "<mainbody>";
            ss += "<header>";
            ss += "<company dil='TR'>Netgsm</company>";
            ss += "<usercode>" + settSms.Username + "</usercode>";
            ss += "<password>" + settSms.Password + "</password>";
            ss += "<type>1:n</type>";
            ss += "<msgheader>" + settSms.BaslikKodu + "</msgheader>";
            ss += "</header>";
            ss += "<body>";
            ss += "<msg>";
            ss += "<![CDATA[" + msg + "]]>";
            ss += "</msg>";
            ss += "<no>" + tel + "</no>";
            ss += "<no>" + tel + "</no>";
            ss += "</body>  ";
            ss += "</mainbody>";

            string xmlData = ss;

            string PostAddress = "https://api.netgsm.com.tr/sms/send/xml";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var content = new StringContent(xmlData, Encoding.UTF8, "application/x-www-form-urlencoded");

                    HttpResponseMessage response = await client.PostAsync(PostAddress, content);

                    if (response.IsSuccessStatusCode)
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();
                        if (testMode)
                            LogInsert("SMS Gönderimi Tamamlandı", $"Telefon : {tel} İçerik : {msg} NetGSM Dönüş Log : {responseContent}");
                    }
                    else
                    {
                        string errorMessage = $"HTTP Error: {response.StatusCode}";
                        if (testMode)
                            LogInsert("SMS Gönderimi Başarısız", errorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                LogInsert("SmsGonder Hata : ", ex.Message);
            }


            /*Tbl_Sms_Set settSms = smsSet[0];

            string ss = "";
            ss += "<?xml version='1.0' encoding='UTF-8'?>";
            ss += "<mainbody>";
            ss += "<header>";
            ss += "<company dil='TR'>Netgsm</company>";
            ss += "<usercode>" + settSms.Username + "</usercode>";
            ss += "<password>" + settSms.Password + "</password>";
            ss += "<type>1:n</type>";
            ss += "<msgheader>" + settSms.BaslikKodu + "</msgheader>";
            ss += "</header>";
            ss += "<body>";
            ss += "<msg>";
            ss += "<![CDATA[" + msg + "]]>";
            ss += "</msg>";
            ss += "<no>" + tel + "</no>";
            ss += "<no>" + tel + "</no>";
            ss += "</body>  ";
            ss += "</mainbody>";

            string xmlData = ss;

            string PostAddress = "https://api.netgsm.com.tr/sms/send/xml";
            try
            {
                WebClient wUpload = new WebClient();
                HttpWebRequest request = WebRequest.Create(PostAddress) as HttpWebRequest;
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                Byte[] bPostArray = Encoding.UTF8.GetBytes(xmlData);
                Byte[] bResponse =  wUpload.UploadData(PostAddress, "POST", bPostArray);
                Char[] sReturnChars = Encoding.UTF8.GetChars(bResponse);
                string sWebPage = new string(sReturnChars);
                if (testMode) LogInsert("SMS Gönderimi Tamamlandı", $"Telefon : {tel} İçerik : {msg} NetGSM Dönüş Log : {sWebPage}");
                //MessageBox.Show(sWebPage);
            }
            catch (Exception ex)
            {
                LogInsert("SmsGonder Hata : ", ex.Message);
            }*/
        }
        public void DgListMailGonder(string html, string tarih)
        {
            try
            {
                bool sslType = false;
                if (_ssl == "T") sslType = true;

                string subj = tarih + " Doğum Günü Olan Hasta Listesi";

                MailMessage mail = new MailMessage();
                SmtpClient smtpClient = new SmtpClient(_host, Convert.ToInt32(_port));
                smtpClient.Credentials = new NetworkCredential(_gonderen, _pass);
                smtpClient.EnableSsl = sslType;
                mail.From = new MailAddress(_gonderen, "Çağrı Merkezi Bilgilendirme Servisi");


                for (int i = 0; i < smsBildirimMailLists.Count; i++)
                {
                    SmsBildirimMailList smsMailList = smsBildirimMailLists[i];

                    if (smsMailList.mailAdres.Contains("@"))
                    {
                        mail.To.Add(smsMailList.mailAdres);
                    }
                }

                mail.Subject = subj;
                mail.Body = html;
                mail.IsBodyHtml = true;
                smtpClient.Send(mail);
                LogInsert("Çağrı Merkezi Mail Gönderimi", "Çağrı merkezine liste gönderimi tamamlandı.");
            }
            catch (Exception ex)
            {
                LogInsert("DgListMailGonder Hata : ", "E-Posta Gönderilemedi. Hata Açıklaması : " + ex.Message);
            }
        }
        public void DgMailListOku()
        {
            string cmdSmsUser = @"SELECT EMAIL FROM HASTANE.TBL_SMS_CAGRI_MERKEZI WHERE AKTIF = 'T' ";

            try
            {
                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdSmsUser, conn))
                {
                    conn.Open();

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                SmsBildirimMailList mailListesi = new SmsBildirimMailList
                                {
                                    mailAdres = dr[0].ToString()
                                };
                                smsBildirimMailLists.Add(mailListesi);
                            }
                        }
                    }
                }
                string mailMessage = "";
                for (int i = 0; i < smsBildirimMailLists.Count; i++)
                {
                    SmsBildirimMailList smsMailList = smsBildirimMailLists[i];
                    mailMessage += $"{smsMailList.mailAdres} | ";

                }
                if (testMode) LogInsert("Çağrı Merkezi Mail Listesi", mailMessage);
                //DgListMailGonder("TEST", "29/01/2024");

            }
            catch (Exception ex)
            {
                LogInsert("Çağrı Merkezi Mail Listesi Okunurken Hata : ", ex.Message);
            }
        }
        public void HastaneListGetir()
        {
            string cmdSmsUser = @"SELECT HASTANE, IPADRES, DBLINK FROM HASTANE.HASTANE_ISTATISTIK WHERE AKTIF = 'T' ORDER BY ID";

            try
            {
                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdSmsUser, conn))
                {
                    conn.Open();

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                HastaneList _hastaneList = new HastaneList
                                {
                                    HastaneAdi = dr[0].ToString(),
                                    ipAdres = dr[1].ToString(),
                                    DbLink = dr[2].ToString()
                                };
                                hastaneList.Add(_hastaneList);
                            }
                        }
                    }
                }
                string hastaneMassage = "";
                for (int i = 0; i < hastaneList.Count; i++)
                {
                    HastaneList _hastaneList = hastaneList[i];
                    hastaneMassage += $"Hastane Adı : {_hastaneList.HastaneAdi} | ";

                }
                if (testMode) LogInsert("Aktif Hastane Listesi : ", hastaneMassage);
                //DgListMailGonder("TEST", "29/01/2024");

            }
            catch (Exception ex)
            {
                LogInsert("HastaneListGetir Okunurken Hata : ", ex.Message);
            }
        }
        public void SablonListGetir()
        {
            string cmdSmsUser = @"SELECT SABLON FROM HASTANE.TBL_SMS_SABLON WHERE AKTIF = 'T' ORDER BY ID";

            try
            {
                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdSmsUser, conn))
                {
                    conn.Open();

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                SmsSablonlar _sablon = new SmsSablonlar
                                {
                                    SmsSablon = dr[0].ToString()
                                };
                                smsSablonList.Add(_sablon);
                            }
                        }
                    }
                }
                string hastaneMassage = "";
                for (int i = 0; i < smsSablonList.Count; i++)
                {
                    SmsSablonlar _sablon = smsSablonList[i];
                    hastaneMassage += $"Şabon : {_sablon.SmsSablon} | ";

                }
                if (testMode) LogInsert("Aktif Şablon Listesi : ", hastaneMassage);
                //DgListMailGonder("TEST", "29/01/2024");

            }
            catch (Exception ex)
            {
                LogInsert("SablonListGetir Okunurken Hata : ", ex.Message);
            }
        }
        public string SmsSablonHazirla(string sablon, string adi, string soyadi, string yas, int indirimSure, int indirimYuzde, string mersis, string smsRed)
        {
            string msj = sablon.Replace(":adi", adi.ToUpper()).Replace(":soyadi", soyadi.ToUpper()).Replace(":yas", yas).Replace(":indirimSure",indirimSure.ToString()).Replace(":indirimYuzde",indirimYuzde.ToString()).Replace(":mersis",mersis).Replace(":red",smsRed);
            if (testMode) LogInsert("SmsSablonHazirla", msj);
            return msj;
        }
        public void BaglantiAyarlari() // bağlantı ayarları
        {

            string directory = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(directory, "SmsService.ini");
            Dictionary<string, string> ayarlar = new Dictionary<string, string>();

            // Dosya yoksa oluşturulur ve tarih yazılır
            if (!File.Exists(filePath))
            {
                LogInsert("BağlantıAyarları", "SmsService.ini bulunamadı. Yüklü olduğu klasöre örnek dosya oluşturuluyor.. Lütfen düzenleyin.");
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.Write("DB=" + "172.0.0.1:1521/orcl" + ";");
                    writer.Write("KullAdi=" + "enabiz" + ";");
                    writer.Write("KullSifre=" + "enabiz");
                }
                this.Stop();
            }
            else
            {
                string veri = "";
                try
                {

                    string[] satirlar = File.ReadAllLines(filePath);

                    string satirlarBirlestirilmis = string.Join("", satirlar);

                    // Satır içinde noktalı virgül ile ayrılmış ayarları ayır
                    string[] ayarParcalar = satirlarBirlestirilmis.Split(';');

                    foreach (string ayarParca in ayarParcalar)
                    {
                        // Ayarı "=" karakterine göre ayır
                        string[] parcalar = ayarParca.Split('=');

                        // Ayar adını ve değerini al
                        string ayarAdi = parcalar[0].Trim();
                        string ayarDegeri = parcalar[1].Trim();

                        // Sözlüğe ekle
                        ayarlar[ayarAdi] = ayarDegeri;
                    }

                    // Değerleri kullanma örneği
                    string dbAdres = ayarlar["DB"];
                    string kullAdi = ayarlar["KullAdi"];
                    string kullSifre = ayarlar["KullSifre"];
                    if (ayarlar.TryGetValue("TEST_MODE", out string _testMode))
                    {
                        _testMode = ayarlar["TEST_MODE"];
                    }
                    else _testMode = "F";
                    if (_testMode == "T") testMode = true; else testMode = false;

                    if (testMode) LogInsert("SmsService.ini", $"Dosya içeriği okundu. DB Adres: {dbAdres} Kullanıcı Adı: {kullAdi} Şifre: {kullSifre} Test Modu: {_testMode}");

                }
                catch (Exception ex)
                {
                    LogInsert("BaglantiAyarlari Hata : ", ex.Message);
                }

                if (testMode) LogInsert("BaglantiAyarlari : ", veri);
                dbAdres = ayarlar["DB"];
                dbSifre = ayarlar["KullSifre"];
                dbKullAdi = ayarlar["KullAdi"];
                if (testMode)
                {
                    LogInsert("TEST_MODE", "Test modu aktif. Kapatmak için SmsService.ini dosyasındaki TEST_MODE silin.");
                    testMode = true;
                }
                Database.ConnStr(dbAdres, dbKullAdi, dbSifre);
                LogInsert("BaglantiAyarlari", "Bağlantı ayarları okundu." + Database.connstr);
            }
        }
        void LogKlasorCreate()
        {
            string logKlasor = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LOG");
            if (!Directory.Exists(logKlasor))
            {
                Directory.CreateDirectory(logKlasor);
                LogInsert("Klasör Oluşturma", "LOG klasörü oluşturuldu.");
            }
        }
        void LogInsert(string baslik, string msj)
        {
            //LogKlasorCreate();
            string time = DateTime.Now.ToShortDateString();
            string log = DateTime.Now + ";" + baslik + ";" + msj;

            string directory = AppDomain.CurrentDomain.BaseDirectory + @"\LOG";
            string filePath = Path.Combine(directory, "SmsService_" + time + ".log");

            if (!File.Exists(filePath))
            {
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.WriteLine(log);
                }
            }
            else // Dosya varsa altına tarih eklenir
            {
                using (StreamWriter writer = File.AppendText(filePath))
                {
                    writer.WriteLine(log);
                }
            }
        }
        void MailAyarlariOkuSQL()
        {
            string cmdtxtGonderen = @"select deger from hastane.hastanekey where key = 'MAIL_WL_SMTP_GONDEREN'";
            string cmdtxtPass = @"select deger from hastane.hastanekey where key = 'MAIL_WL_SMTP_PASS'";
            string cmdtxtPort = @"select deger from hastane.hastanekey where key = 'MAIL_WL_SMTP_PORT'";
            string cmdtxtSSL = @"select deger from hastane.hastanekey where key = 'MAIL_WL_SMTP_SSL'";
            string cmdtxtHost = @"select deger from hastane.hastanekey where key = 'MAIL_WL_SMTP_HOST'";
            string cmdtxtName = @"select deger from hastane.hastanekey where key = 'MAIL_WL_SMTP_NAME'";
            string cmdtxtTimer = @"select deger from hastane.hastanekey where key = 'MAIL_WL_SMTP_TIMER'";
            string cmdSmsUser = @"select USERNAME, PASS, BASLIK_KODU, SMS_AKTIF, DOGUM_GUNU_AKTIF, SORGU_GUNU, GELIS_SAYISI, EN_KUCUK_YAS, GONDERIM_GUNU, GONDERIM_SAATI, INDIRIM_GUN, INDIRIM_YUZDE, INDIRIM_AKTIF, CAGRI_MERKEZINE_BILDIR from HASTANE.TBL_SMS_SERVIS_SET";

            try
            {
                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdSmsUser, conn))
                {
                    conn.Open();

                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        if (dr.HasRows)
                        {
                            Tbl_Sms_Set smsUser = new Tbl_Sms_Set();

                            while (dr.Read())
                            {
                                smsUser.Username = dr["USERNAME"].ToString();
                                smsUser.Password = dr["PASS"].ToString();
                                smsUser.BaslikKodu = dr["BASLIK_KODU"].ToString();
                                smsUser.SmsAktif = dr["SMS_AKTIF"].ToString();
                                smsUser.DogumGunuAktif = dr["DOGUM_GUNU_AKTIF"].ToString();
                                smsUser.SorguGunu = Convert.ToInt32(dr["SORGU_GUNU"]);
                                smsUser.GelisSayisi = Convert.ToInt32(dr["GELIS_SAYISI"]);
                                smsUser.EnKucukYas = Convert.ToInt32(dr["EN_KUCUK_YAS"]);
                                smsUser.GonderimGunu = Convert.ToInt32(dr["GONDERIM_GUNU"]);
                                smsUser.GonderimSaati = dr["GONDERIM_SAATI"].ToString();
                                smsUser.IndirimGun = Convert.ToInt32(dr["INDIRIM_GUN"]);
                                smsUser.IndirimYuzde = Convert.ToInt32(dr["INDIRIM_YUZDE"]);
                                smsUser.IndirimAktif = dr["INDIRIM_AKTIF"].ToString();
                                smsUser.CagriMerkezineBildir = dr["CAGRI_MERKEZINE_BILDIR"].ToString();
                            }
                            smsSet.Add(smsUser);
                        }
                    }
                }

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxtGonderen, conn))
                {
                    conn.Open();

                    // reader is IDisposable and should be closed
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        //List<String> items = new List<String>();
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                _gonderen = (dr.GetString(0));
                            }
                        }
                    }
                }

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxtPass, conn))
                {
                    conn.Open();

                    // reader is IDisposable and should be closed
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        //List<String> items = new List<String>();
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                _pass = (dr.GetString(0));
                            }
                        }
                    }

                }

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxtPort, conn))
                {
                    conn.Open();

                    // reader is IDisposable and should be closed
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        //List<String> items = new List<String>();
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                _port = (dr.GetString(0));
                            }
                        }
                    }
                }

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxtSSL, conn))
                {
                    conn.Open();

                    // reader is IDisposable and should be closed
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        //List<String> items = new List<String>();
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                _ssl = (dr.GetString(0));
                            }
                        }
                    }
                }

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxtHost, conn))
                {
                    conn.Open();

                    // reader is IDisposable and should be closed
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        //List<String> items = new List<String>();
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                _host = (dr.GetString(0));
                            }
                        }
                    }
                }

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxtName, conn))
                {
                    conn.Open();

                    // reader is IDisposable and should be closed
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        //List<String> items = new List<String>();
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                _name = (dr.GetString(0));
                            }
                        }
                    }
                }

                using (OracleConnection conn = new OracleConnection(Database.connstr))
                using (OracleCommand cmd = new OracleCommand(cmdtxtTimer, conn))
                {
                    conn.Open();

                    // reader is IDisposable and should be closed
                    using (OracleDataReader dr = cmd.ExecuteReader())
                    {
                        //List<String> items = new List<String>();
                        if (dr.HasRows)
                        {
                            while (dr.Read())
                            {
                                _timerSure = Convert.ToInt32((dr.GetString(0)));
                            }
                        }
                    }
                }
                if (testMode) LogInsert("Key okundu : ", _host + _gonderen + _pass + _port + _ssl + _name + _timerSure + _smsUserName + _smsPass + _smsBaslik);

                if (testMode) LogInsert("SmsTable", smsSet.Count.ToString());

                for (int i = 0; i < smsSet.Count; i++)
                {
                    Tbl_Sms_Set dgSms = smsSet[i];
                    string message = $"USERNAME: {dgSms.Username} | " +
                                     $"PASS: {dgSms.Password} | " +
                                     $"BASLIK_KODU: {dgSms.BaslikKodu} | " +
                                     $"SMS_AKTIF: {dgSms.SmsAktif} | " +
                                     $"DOGUM_GUNU_AKTIF: {dgSms.DogumGunuAktif} | " +
                                     $"SORGU_GUNU: {dgSms.SorguGunu} | " +
                                     $"GELIS_SAYISI: {dgSms.GelisSayisi} | " +
                                     $"EN_KUCUK_YAS: {dgSms.EnKucukYas} | " +
                                     $"GONDERIM_GUNU: {dgSms.GonderimGunu} | " +
                                     $"GONDERIM_SAATI: {dgSms.GonderimSaati} | " +
                                     $"INDIRIM_GUN: {dgSms.IndirimGun} | " +
                                     $"INDIRIM_YUZDE: {dgSms.IndirimYuzde} | " +
                                     $"INDIRIM_AKTIF: {dgSms.IndirimAktif}  | " +
                                     $"CAGRI_MERKEZINE_BILDIR: {dgSms.CagriMerkezineBildir}  | ";

                    if (testMode) LogInsert("TBL_SMS_SERVIS_SET", message);
                }
            }
            catch (Exception ex)
            {
                LogInsert("Key Okuma Hatası : ", ex.Message);
                this.Stop();
            }
        }
    }
}
