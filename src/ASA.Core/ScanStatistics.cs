using System.Text.Json;

namespace Asa.Core;

public sealed class DailyStatistics
{
    public DateTime Day { get; set; }

    public long FilesScanned { get; set; }

    public long ThreatsFound { get; set; }
}

public sealed class ScanStatistics
{
    private readonly string statisticsFile;
    private readonly object statisticsLock = new();
    private readonly Dictionary<DateTime, DailyStatistics> days = new();
    private DateTime lastSaveTime = DateTime.MinValue;

    public ScanStatistics(string statisticsFile)
    {
        this.statisticsFile = statisticsFile;
        Load();
    }

    public event Action? Changed;

    public long TotalFilesScanned
    {
        get
        {
            lock (statisticsLock)
            {
                return days.Values.Sum(day => day.FilesScanned);
            }
        }
    }

    public long TotalThreatsFound
    {
        get
        {
            lock (statisticsLock)
            {
                return days.Values.Sum(day => day.ThreatsFound);
            }
        }
    }

    public void RecordScannedFile(bool threatFound)
    {
        lock (statisticsLock)
        {
            DailyStatistics today = GetOrCreateDay(DateTime.Today);
            today.FilesScanned++;
            if (threatFound)
            {
                today.ThreatsFound++;
            }

            if (threatFound || DateTime.Now - lastSaveTime > TimeSpan.FromSeconds(30))
            {
                SaveWithoutLock();
            }
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<DailyStatistics> GetLastDays(int numberOfDays)
    {
        lock (statisticsLock)
        {
            var result = new List<DailyStatistics>();
            for (int offset = numberOfDays - 1; offset >= 0; offset--)
            {
                DateTime day = DateTime.Today.AddDays(-offset);
                if (days.TryGetValue(day, out DailyStatistics? existing))
                {
                    result.Add(new DailyStatistics { Day = day, FilesScanned = existing.FilesScanned, ThreatsFound = existing.ThreatsFound });
                }
                else
                {
                    result.Add(new DailyStatistics { Day = day });
                }
            }

            return result;
        }
    }

    public void Save()
    {
        lock (statisticsLock)
        {
            SaveWithoutLock();
        }
    }

    private DailyStatistics GetOrCreateDay(DateTime day)
    {
        if (!days.TryGetValue(day, out DailyStatistics? statistics))
        {
            statistics = new DailyStatistics { Day = day };
            days[day] = statistics;
        }

        return statistics;
    }

    private void SaveWithoutLock()
    {
        try
        {
            string json = JsonSerializer.Serialize(days.Values.OrderBy(day => day.Day).ToList());
            File.WriteAllText(statisticsFile, json);
            lastSaveTime = DateTime.Now;
        }
        catch (IOException)
        {
        }
    }

    private void Load()
    {
        if (!File.Exists(statisticsFile))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<DailyStatistics>>(File.ReadAllText(statisticsFile));
            if (loaded == null)
            {
                return;
            }

            foreach (DailyStatistics day in loaded)
            {
                days[day.Day.Date] = day;
            }
        }
        catch (JsonException)
        {
        }
    }
}
