using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;

namespace MyTts.Helpers
{
    public static class CsvFileReader
    {
        public static List<HaberSummaryCsv> ReadHaberSummariesFromCsv(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"The CSV file '{filePath}' was not found at the expected location: {filePath}");
            }

            using (var reader = new StreamReader(filePath))
            {
                var config = new CsvConfiguration(new CultureInfo("tr-TR"));
                config.Delimiter = ";";
                config.HasHeaderRecord = false;
                using (var csv = new CsvReader(reader, config))
                {
                    var records = csv.GetRecords<HaberSummaryCsv>().ToList();
                    return records;
                }
            }
        }
    }
    public class HaberSummaryCsv
    {
        [Index(0)] // Maps this property to the first column (index 0)
        public int IlgiId { get; set; }

        [Index(1)] // Maps this property to the second column (index 1)
        public string Baslik { get; set; } = string.Empty;

        [Index(2)] // Maps this property to the third column (index 2)
        public string Ozet { get; set; } = string.Empty;
    }
}