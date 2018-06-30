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
            var counters = new PerformanceCounterCategory("PhysicalDisk")
                .GetInstanceNames().Where(n => n != "_Total")
                .Select(n => new Counter(2, new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", n), new PerformanceCounter("PhysicalDisk", "Avg. Disk Queue Length", n)))
                .ToList();
            try { counters.AddRange(new PerformanceCounterCategory("Hyper-V Virtual Storage Device").GetInstanceNames().Select(n => new Counter(1, new PerformanceCounter("Hyper-V Virtual Storage Device", "Queue Length", n), null))); }
            catch { } // system does not have Hyper-V
            var lastCalc = DateTime.UtcNow.AddYears(-1);
            var interval = TimeSpan.FromSeconds(10);
            while (true)
            {
                Thread.Sleep(200);
                foreach (var c in counters)
                    c.Sample();

                if (DateTime.UtcNow > lastCalc.AddSeconds(5))
                {
                    lastCalc = DateTime.UtcNow;
                    Console.Clear();
                    Console.WriteLine();
                    var maxInterval = TimeSpan.FromSeconds(Math.Floor((DateTime.UtcNow - counters[0].OldestSample).TotalSeconds));
                    if (maxInterval >= interval)
                        Console.WriteLine($"Disk usage over the last {interval}");
                    else
                        Console.WriteLine($"Disk usage over the last {maxInterval} (selected interval is {interval} but not enough data)");
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.White; Console.WriteLine($"{"Busy",10}{"Behind",10}{"Avg. Queue",14}      {"Physical Disk"}"); Console.ForegroundColor = ConsoleColor.Gray;
                    foreach (var stat in counters.Where(c => c.Kind == 2).Select(c => new { c.Name, stats = c.CalcStats(interval) }).OrderByDescending(c => c.stats.busy).ThenBy(c => c.Name))
                        Console.WriteLine($"{stat.stats.busy,10:0.0%}{stat.stats.behind,10:0.0%}{stat.stats.queueAvg,14:0.000}      {stat.Name}");
                    if (counters.Any(c => c.Kind == 1))
                    {
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.White; Console.WriteLine($"{"Busy",10}{"Behind",10}      {"Virtual Disk"}"); Console.ForegroundColor = ConsoleColor.Gray;
                        foreach (var stat in counters.Where(c => c.Kind == 1).Select(c => new { c.Name, stats = c.CalcStats(interval) }).OrderByDescending(c => c.stats.busy).ThenBy(c => c.Name))
                            Console.WriteLine($"{stat.stats.busy,10:0.0%}{stat.stats.behind,10:0.0%}      {stat.Name}");
                    }
                    Console.WriteLine();
                    Console.WriteLine($"Total samples per disk: {counters[0].SampleCount:#,0}");
                    Console.WriteLine($"Press 1-5 to select interval; any key to refresh screen.");
                }

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

    class Counter
    {
        private PerformanceCounter _counter1, _counter2;
        private Queue<(DateTime time, float value1, float value2)> _history = new Queue<(DateTime, float, float)>();
        public int Kind { get; private set; }
        public string Name { get; private set; }
        public DateTime OldestSample { get; private set; }

        public Counter(int kind, PerformanceCounter counter1, PerformanceCounter counter2)
        {
            Kind = kind;
            _counter1 = counter1;
            _counter2 = counter2;
            Name = _counter1.InstanceName.Replace("C:-", "").Replace(".vhdx", "").Replace(".avhdx", "").Replace("Virtual Hard Disks-", "");
        }

        public void Sample()
        {
            var value1 = _counter1.NextValue();
            var value2 = _counter2?.NextValue() ?? 0;
            _history.Enqueue((DateTime.UtcNow, value1, value2));
            while (_history.Peek().time < DateTime.UtcNow.AddHours(-24))
                _history.Dequeue();
            OldestSample = _history.Peek().time;
        }

        public int SampleCount => _history.Count;

        public (double busy, double behind, double queueAvg) CalcStats(TimeSpan interval)
        {
            int total = 0;
            int busy = 0;
            int behind = 0;
            double queueAvg = 0;
            foreach (var pt in _history)
            {
                if (pt.time >= DateTime.UtcNow - interval)
                {
                    total++;
                    queueAvg += pt.value2;
                    if (pt.value1 >= 1)
                        busy++;
                    if (pt.value1 > 1)
                        behind++;
                }
            }
            return (busy / (double) total, behind / (double) total, queueAvg / total);
        }
    }
}
