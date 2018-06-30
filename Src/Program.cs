using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace HyperVDiskUsage
{
    class Program
    {
        static void Main(string[] args)
        {
            var countersPhysical = new PerformanceCounterCategory("PhysicalDisk")
                .GetInstanceNames().Where(n => n != "_Total")
                .Select(n => new DiskCounter(new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", n), new PerformanceCounter("PhysicalDisk", "Avg. Disk Queue Length", n)))
                .ToList();

            var countersHyperV = new List<DiskCounter>();
            try
            {
                countersHyperV = new PerformanceCounterCategory("Hyper-V Virtual Storage Device")
                    .GetInstanceNames()
                    .Select(n => new DiskCounter(new PerformanceCounter("Hyper-V Virtual Storage Device", "Queue Length", n), null))
                    .ToList();
            }
            catch { } // system does not have Hyper-V

            var lastCalc = DateTime.UtcNow.AddYears(-1);
            var interval = TimeSpan.FromSeconds(10);
            while (true)
            {
                foreach (var c in countersPhysical.Concat(countersHyperV))
                    c.Sample();

                if (DateTime.UtcNow > lastCalc.AddSeconds(5))
                {
                    lastCalc = DateTime.UtcNow;

                    Console.Clear();
                    Console.WriteLine();
                    var maxInterval = TimeSpan.FromSeconds(Math.Floor((DateTime.UtcNow - countersPhysical[0].OldestSample).TotalSeconds));
                    if (maxInterval >= interval)
                        Console.WriteLine($"Disk usage over the last {interval}");
                    else
                        Console.WriteLine($"Disk usage over the last {maxInterval} (selected interval is {interval} but not enough data)");
                    Console.WriteLine();

                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine($"{"Busy",10}{"Behind",10}{"Avg. Queue",14}      {"Physical Disk"}"); Console.ForegroundColor = ConsoleColor.Gray;
                    foreach (var stat in countersPhysical.Select(c => new { c.Name, stats = c.CalcStats(interval) }).OrderByDescending(c => c.stats.busy).ThenBy(c => c.Name))
                        Console.WriteLine($"{stat.stats.busy,10:0.0%}{stat.stats.behind,10:0.0%}{stat.stats.queueAvg,14:0.000}      {stat.Name}");
                    Console.WriteLine();

                    if (countersHyperV.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.White; Console.WriteLine($"{"Busy",10}{"Behind",10}      {"Virtual Disk"}"); Console.ForegroundColor = ConsoleColor.Gray;
                        foreach (var stat in countersHyperV.Select(c => new { c.Name, stats = c.CalcStats(interval) }).OrderByDescending(c => c.stats.busy).ThenBy(c => c.Name))
                            Console.WriteLine($"{stat.stats.busy,10:0.0%}{stat.stats.behind,10:0.0%}      {stat.Name}");
                        Console.WriteLine();
                    }

                    Console.WriteLine($"Total samples per disk: {countersPhysical[0].SampleCount:#,0}");
                    Console.WriteLine($"Press 1-5 to select interval; any key to refresh screen.");
                }

                Thread.Sleep(200);

                while (Console.KeyAvailable)
                {
                    lastCalc = DateTime.UtcNow.AddYears(-1);
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.D1)
                        interval = TimeSpan.FromSeconds(10);
                    else if (k.Key == ConsoleKey.D2)
                        interval = TimeSpan.FromSeconds(60);
                    else if (k.Key == ConsoleKey.D3)
                        interval = TimeSpan.FromMinutes(10);
                    else if (k.Key == ConsoleKey.D4)
                        interval = TimeSpan.FromHours(1);
                    else if (k.Key == ConsoleKey.D5)
                        interval = TimeSpan.FromHours(24);
                }
            }
        }
    }

    class DiskCounter
    {
        private PerformanceCounter _ctrCurrent, _ctrAverage;
        private Queue<(DateTime time, float cur, float avg)> _history = new Queue<(DateTime, float, float)>();

        public string Name { get; private set; }
        public DateTime OldestSample { get; private set; }
        public int SampleCount => _history.Count;

        public DiskCounter(PerformanceCounter ctrCurrent, PerformanceCounter ctrAverage)
        {
            _ctrCurrent = ctrCurrent;
            _ctrAverage = ctrAverage;
            Name = _ctrCurrent.InstanceName.Replace("C:-", "").Replace("D:-", "").Replace(".vhdx", "").Replace(".avhdx", "").Replace("Virtual Hard Disks-", "");
        }

        public void Sample()
        {
            var cur = _ctrCurrent.NextValue();
            var avg = _ctrAverage?.NextValue() ?? 0;
            _history.Enqueue((DateTime.UtcNow, cur, avg));
            while (_history.Peek().time < DateTime.UtcNow.AddHours(-24))
                _history.Dequeue();
            OldestSample = _history.Peek().time;
        }

        public (double busy, double behind, double queueAvg) CalcStats(TimeSpan interval)
        {
            int total = 0;
            int busy = 0;
            int behind = 0;
            double average = 0;
            foreach (var pt in _history)
            {
                if (pt.time >= DateTime.UtcNow - interval)
                {
                    total++;
                    average += pt.avg;
                    if (pt.cur >= 1)
                        busy++;
                    if (pt.cur > 1)
                        behind++;
                }
            }
            return (busy / (double) total, behind / (double) total, average / total);
        }
    }
}
