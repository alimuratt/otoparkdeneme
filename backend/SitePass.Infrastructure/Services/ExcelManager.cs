using OfficeOpenXml;
using Microsoft.Extensions.Configuration;
using SitePass.Core.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SitePass.Infrastructure.Services
{
    public class ExcelManager
    {
        private readonly string _filePath;
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public ExcelManager(IConfiguration configuration)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // Excel dosya yolunu al (varsayılan yedek yol da konmuştur)
            _filePath = configuration["ExcelDatabasePath"] ?? "/Users/yusufemirsen/Desktop/MevcutOtoparkSistemi.xlsx";
            
            EnsureExcelFileExists();
        }

        private void EnsureExcelFileExists()
        {
            _lock.Wait();
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(_filePath))
                {
                    using (var package = new ExcelPackage())
                    {
                        // BeyazListe Sayfası
                        var beyazListeSheet = package.Workbook.Worksheets.Add("BeyazListe");
                        beyazListeSheet.Cells[1, 1].Value = "Id";
                        beyazListeSheet.Cells[1, 2].Value = "Plaka";
                        beyazListeSheet.Cells[1, 3].Value = "SahipAdSoyad";
                        beyazListeSheet.Cells[1, 4].Value = "BlokDaire";
                        beyazListeSheet.Cells[1, 5].Value = "IsGuest";
                        beyazListeSheet.Cells[1, 6].Value = "IsActive";
                        beyazListeSheet.Cells[1, 7].Value = "ExpireDate";
                        
                        // Örnek Başlangıç Kaydı (Seeding)
                        beyazListeSheet.Cells[2, 1].Value = 1;
                        beyazListeSheet.Cells[2, 2].Value = "34ABC123";
                        beyazListeSheet.Cells[2, 3].Value = "Ahmet Sakin";
                        beyazListeSheet.Cells[2, 4].Value = "A-12";
                        beyazListeSheet.Cells[2, 5].Value = false; // IsGuest
                        beyazListeSheet.Cells[2, 6].Value = true;  // IsActive
                        beyazListeSheet.Cells[2, 7].Value = "";    // ExpireDate

                        // GirisCikisLoglari Sayfası
                        var logSheet = package.Workbook.Worksheets.Add("GirisCikisLoglari");
                        logSheet.Cells[1, 1].Value = "LogId";
                        logSheet.Cells[1, 2].Value = "OkunanPlaka";
                        logSheet.Cells[1, 3].Value = "GirisTarihi";
                        logSheet.Cells[1, 4].Value = "KameraKodu";

                        package.SaveAs(new FileInfo(_filePath));
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        // 1. ADIM YARDIMCI METODLARI

        public async Task<List<BeyazListe>> GetBeyazListeAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var list = new List<BeyazListe>();
                using (var package = new ExcelPackage(new FileInfo(_filePath)))
                {
                    var sheet = package.Workbook.Worksheets["BeyazListe"];
                    if (sheet == null) return list;

                    int rowCount = sheet.Dimension?.End.Row ?? 0;
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var idValue = sheet.Cells[row, 1].Value;
                        if (idValue == null) continue;

                        list.Add(new BeyazListe
                        {
                            Id = Convert.ToInt32(idValue),
                            Plaka = sheet.Cells[row, 2].Value?.ToString() ?? string.Empty,
                            SahipAdSoyad = sheet.Cells[row, 3].Value?.ToString() ?? string.Empty,
                            BlokDaire = sheet.Cells[row, 4].Value?.ToString() ?? string.Empty,
                            IsGuest = ParseBool(sheet.Cells[row, 5].Value),
                            IsActive = ParseBool(sheet.Cells[row, 6].Value),
                            ExpireDate = ParseDateTime(sheet.Cells[row, 7].Value)
                        });
                    }
                }
                return list;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task AddGuestVehicleAsync(BeyazListe vehicle)
        {
            await _lock.WaitAsync();
            try
            {
                using (var package = new ExcelPackage(new FileInfo(_filePath)))
                {
                    var sheet = package.Workbook.Worksheets["BeyazListe"];
                    if (sheet == null) return;

                    int rowCount = sheet.Dimension?.End.Row ?? 1;
                    int nextId = 1;

                    if (rowCount > 1)
                    {
                        var ids = new List<int>();
                        for (int r = 2; r <= rowCount; r++)
                        {
                            var idVal = sheet.Cells[r, 1].Value;
                            if (idVal != null && int.TryParse(idVal.ToString(), out int id))
                            {
                                ids.Add(id);
                            }
                        }
                        if (ids.Any()) nextId = ids.Max() + 1;
                    }

                    int newRow = rowCount + 1;
                    sheet.Cells[newRow, 1].Value = nextId;
                    sheet.Cells[newRow, 2].Value = vehicle.Plaka;
                    sheet.Cells[newRow, 3].Value = vehicle.SahipAdSoyad;
                    sheet.Cells[newRow, 4].Value = vehicle.BlokDaire;
                    sheet.Cells[newRow, 5].Value = vehicle.IsGuest;
                    sheet.Cells[newRow, 6].Value = vehicle.IsActive;
                    sheet.Cells[newRow, 7].Value = vehicle.ExpireDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";

                    await package.SaveAsync();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeactivateVehicleAsync(string plate)
        {
            await _lock.WaitAsync();
            try
            {
                using (var package = new ExcelPackage(new FileInfo(_filePath)))
                {
                    var sheet = package.Workbook.Worksheets["BeyazListe"];
                    if (sheet == null) return;

                    int rowCount = sheet.Dimension?.End.Row ?? 0;
                    bool updated = false;

                    for (int row = 2; row <= rowCount; row++)
                    {
                        var cellPlate = sheet.Cells[row, 2].Value?.ToString();
                        if (string.Equals(cellPlate, plate, StringComparison.OrdinalIgnoreCase))
                        {
                            sheet.Cells[row, 6].Value = false; // IsActive = false
                            updated = true;
                        }
                    }

                    if (updated)
                    {
                        await package.SaveAsync();
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeactivateExpiredGuestVehiclesAsync()
        {
            await _lock.WaitAsync();
            try
            {
                using (var package = new ExcelPackage(new FileInfo(_filePath)))
                {
                    var sheet = package.Workbook.Worksheets["BeyazListe"];
                    if (sheet == null) return;

                    int rowCount = sheet.Dimension?.End.Row ?? 0;
                    bool updated = false;
                    var now = DateTime.Now;

                    for (int row = 2; row <= rowCount; row++)
                    {
                        var isGuest = ParseBool(sheet.Cells[row, 5].Value);
                        var isActive = ParseBool(sheet.Cells[row, 6].Value);
                        var expireDate = ParseDateTime(sheet.Cells[row, 7].Value);

                        if (isGuest && isActive && expireDate != null && expireDate <= now)
                        {
                            sheet.Cells[row, 6].Value = false; // IsActive = false
                            updated = true;
                        }
                    }

                    if (updated)
                    {
                        await package.SaveAsync();
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task AddGirisCikisLogAsync(GirisCikisLoglari log)
        {
            await _lock.WaitAsync();
            try
            {
                using (var package = new ExcelPackage(new FileInfo(_filePath)))
                {
                    var sheet = package.Workbook.Worksheets["GirisCikisLoglari"];
                    if (sheet == null) return;

                    int rowCount = sheet.Dimension?.End.Row ?? 1;
                    int nextLogId = 1;

                    if (rowCount > 1)
                    {
                        var ids = new List<int>();
                        for (int r = 2; r <= rowCount; r++)
                        {
                            var idVal = sheet.Cells[r, 1].Value;
                            if (idVal != null && int.TryParse(idVal.ToString(), out int id))
                            {
                                ids.Add(id);
                            }
                        }
                        if (ids.Any()) nextLogId = ids.Max() + 1;
                    }

                    int newRow = rowCount + 1;
                    sheet.Cells[newRow, 1].Value = nextLogId;
                    sheet.Cells[newRow, 2].Value = log.OkunanPlaka;
                    sheet.Cells[newRow, 3].Value = log.GirisTarihi.ToString("yyyy-MM-dd HH:mm:ss");
                    sheet.Cells[newRow, 4].Value = log.KameraKodu;

                    await package.SaveAsync();
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        // PARSE YARDIMCILARI

        private bool ParseBool(object value)
        {
            if (value == null) return false;
            var str = value.ToString()?.Trim().ToLower();
            if (str == "true" || str == "1" || str == "yes") return true;
            return false;
        }

        private DateTime? ParseDateTime(object value)
        {
            var strValue = value?.ToString();
            if (string.IsNullOrWhiteSpace(strValue)) return null;
            if (DateTime.TryParse(strValue, out DateTime dt))
            {
                return dt;
            }
            return null;
        }
    }
}
