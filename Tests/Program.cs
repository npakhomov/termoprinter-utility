using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Reflection;
using ThermoPrinterTool;

var failures = 0;
var checks = 0;
void Check(string name, Action test)
{
    checks++;
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Reject<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
object? Call(object owner, string name, params object[] arguments) => owner.GetType()
    .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, arguments);
void OnUiThread(Action test)
{
    Exception? error = null;
    var thread = new Thread(() =>
    {
        try { test(); } catch (Exception ex) { error = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(15000)) throw new Exception("UI test timed out");
    if (error is not null) throw error;
}
void PumpUntil(Func<bool> finished)
{
    var timer = Stopwatch.StartNew();
    while (!finished())
    {
        if (timer.Elapsed > TimeSpan.FromSeconds(8)) throw new Exception("UI operation timed out");
        Application.DoEvents();
        Thread.Sleep(5);
    }
}
var testRoot = Path.Combine(Path.GetTempPath(), "ThermoPrinterTool-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
try
{
    Check("Window defers automatic update until active operation ends", () => OnUiThread(() =>
    {
        var applications = 0;
        using var form = new Form1(false, _ => applications++);
        form.Show();
        using var release = new ManualResetEventSlim();
        var action = (Task)Call(form, "RunAction", (Action)(() => release.Wait(5000)))!;
        var updates = Field<UpdateSession>(form, "_updates");
        updates.Queue("verified.exe");
        Call(form, "ApplyPendingUpdate");
        Assert(applications == 0 && updates.IsBusy, "Update interrupted active action");
        form.Close();
        Assert(!updates.Token.IsCancellationRequested && form.Visible, "Busy window closed");
        release.Set();
        PumpUntil(() => action.IsCompleted);
        Assert(applications == 1 && !updates.IsBusy, "Deferred update did not run once");
        form.Close();
    }));
    Check("Closed window cancels downloads and ignores late update continuation", () => OnUiThread(() =>
    {
        var applications = 0;
        using var form = new Form1(false, _ => applications++);
        form.Show();
        var updates = Field<UpdateSession>(form, "_updates");
        var download = Task.Delay(30000, updates.Token);
        form.Close();
        Assert(updates.Token.IsCancellationRequested && download.IsCanceled, "Download not canceled");
        updates.Queue("late.exe");
        Call(form, "ApplyPendingUpdate");
        Assert(applications == 0 && !SupportLifetime.Restarting, "Closed window restarted app");
    }));
    Check("Manual update shares operation completion gate", () => OnUiThread(() =>
    {
        var applications = 0;
        using var form = new Form1(false, _ => applications++);
        form.Show();
        var updates = Field<UpdateSession>(form, "_updates");
        var action = (Task)Call(form, "RunAction", (Action)(() => updates.Queue("manual.exe")))!;
        PumpUntil(() => action.IsCompleted);
        Assert(applications == 1 && !updates.IsBusy, "Manual update remained blocked by its own action");
        form.Close();
    }));
    foreach (var throws in new[] { false, true })
    {
        Check("Failed restart resets deletion state and stops waiting updater: " + throws, () =>
        {
            using var updater = SupportLifetime.StartHiddenPowerShell("[Threading.Thread]::Sleep(30000)");
            try
            {
                Reject<InvalidOperationException>(() => SupportLifetime.CommitRestart(updater,
                    () => { if (throws) throw new InvalidOperationException("test close failure"); }, () => false));
                Assert(!SupportLifetime.Restarting && updater.HasExited, "Orphan updater or sticky restart flag");
            }
            finally { if (!updater.HasExited) { updater.Kill(); updater.WaitForExit(); } SupportLifetime.Restarting = false; }
        });
    }
    Check("Utility parameters are captured atomically and not reread after download", () => OnUiThread(() =>
    {
        using var form = new Form1(false);
        form.Show();
        Field<TextBox>(form, "_ethernetMaskBox").Text = "255.255.255.0";
        Field<TextBox>(form, "_ethernetGatewayBox").Text = "192.168.1.1";
        var settings = form.CaptureUtilitySettings();
        Field<ComboBox>(form, "_modelBox").SelectedIndex = 1;
        Field<TextBox>(form, "_ethernetIpBox").Text = "192.168.1.150";
        Field<TextBox>(form, "_ethernetMaskBox").Text = "255.0.0.0";
        Field<TextBox>(form, "_ethernetGatewayBox").Text = "10.0.0.1";
        Field<TextBox>(form, "_dhcpStartBox").Text = "192.168.1.100";
        Assert(settings.Model == "ATOL DD340" && settings.Ip == "" && settings.Mask == "255.255.255.0" &&
            settings.Gateway == "192.168.1.1" && settings.DhcpStart == "", "Snapshot mutated");
        form.Close();
    }));
    Check("Installer accepts successful exit and reports failure exit", () =>
    {
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        DriverInstallation.Run(cmd, ["/c", "exit", "0"], 10000);
        Reject<InvalidOperationException>(() => DriverInstallation.Run(cmd, ["/c", "exit", "7"], 10000));
    });
    Check("Installer timeout preserves files while vendor process is still running", () =>
    {
        var pidFile = Path.Combine(testRoot, "installer.pid");
        var source = Path.Combine(testRoot, "installer-source.txt");
        File.WriteAllText(source, "retained until installer exits");
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        try
        {
            Reject<DriverInstallation.InstallerStillRunningException>(() => DriverInstallation.CompleteInstallation(
                () => DriverInstallation.Run(ps,
                    ["-NoProfile", "-NonInteractive", "-Command", $"[IO.File]::WriteAllText('{pidFile.Replace("'", "''")}', [string]$PID); [Threading.Thread]::Sleep(30000)"], 1000),
                () => File.Delete(source)));
            var timer = Stopwatch.StartNew();
            while (!File.Exists(pidFile) && timer.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(20);
            using var vendor = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile)));
            Assert(!vendor.HasExited && File.Exists(source), "Installer or working files were removed");
        }
        finally
        {
            if (File.Exists(pidFile))
            {
                try { using var vendor = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile))); vendor.Kill(true); vendor.WaitForExit(); }
                catch (ArgumentException) { }
            }
        }
    });
    Check("Installer cleanup runs on completion but cannot mask installation error", () =>
    {
        var cleaned = false;
        DriverInstallation.CompleteInstallation(() => { }, () => cleaned = true);
        Assert(cleaned, "Successful installer did not clean up");
        cleaned = false;
        var expected = new InvalidOperationException("original install error");
        try
        {
            DriverInstallation.CompleteInstallation(() => throw expected,
                () => { cleaned = true; throw new IOException("cleanup failed"); });
            throw new Exception("Original error lost");
        }
        catch (InvalidOperationException ex) { Assert(ReferenceEquals(ex, expected) && cleaned, "Wrong failure or no cleanup"); }
    });
    Check("SHA256 accepts original bytes", () =>
    {
        using var stream = new MemoryStream("verified"u8.ToArray());
        TrustedPackages.VerifyHash(stream, Convert.ToHexString(SHA256.HashData("verified"u8)));
    });
    Check("SHA256 rejects tampered bytes", () =>
    {
        using var stream = new MemoryStream("tampered"u8.ToArray());
        Reject<InvalidDataException>(() => TrustedPackages.VerifyHash(stream, Convert.ToHexString(SHA256.HashData("verified"u8))));
    });
    foreach (var bad in new[] { "../outside.exe", "dir/../../outside.exe", "C:/outside.exe", "a.exe:stream", "dir/../a.exe", "file. ", "/root.exe" })
    {
        Check("ZIP rejects " + bad, () =>
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) zip.CreateEntry(bad);
            stream.Position = 0;
            var target = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            Reject<InvalidDataException>(() => TrustedPackages.ExtractVerifiedArchive(stream, target));
            Assert(!Directory.Exists(target), "Files were written before validation");
        });
    }
    Check("ZIP rejects duplicate Windows paths", () =>
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        { zip.CreateEntry("Tool.dll"); zip.CreateEntry("tool.dll"); }
        stream.Position = 0;
        Reject<InvalidDataException>(() => TrustedPackages.ExtractVerifiedArchive(stream, Path.Combine(testRoot, "duplicate")));
    });
    Check("DHCP pool is excluded with gateway, local and configured printers", () =>
    {
        var local = IPAddress.Parse("192.168.1.20"); var mask = IPAddress.Parse("255.255.255.0");
        var subnet = IpAddressPolicy.Subnet(local, mask);
        var pool = IpAddressPolicy.ParseDhcpPool("192.168.1.100", "192.168.1.254", subnet);
        var excluded = new HashSet<string> { "192.168.1.1", "192.168.1.2", "192.168.1.10" };
        var result = IpAddressPolicy.Candidates(local, mask, pool, excluded).ToList();
        Assert(result.Count > 0, "No candidates");
        Assert(result.All(ip => subnet.Contains(ip) && !pool!.Value.Contains(ip) && !excluded.Contains(ip.ToString()) && !ip.Equals(local)), "Unsafe candidate");
        Assert(result.Count == result.Distinct().Count(), "Duplicate candidate");
    });
    Check("Entire subnet DHCP pool yields no candidates", () =>
    {
        var local = IPAddress.Parse("10.1.1.2"); var mask = IPAddress.Parse("255.255.255.0");
        Assert(!IpAddressPolicy.Candidates(local, mask, IpAddressPolicy.Subnet(local, mask), new HashSet<string>()).Any(), "DHCP collision");
    });
    Check("Unknown DHCP pool remains unknown", () =>
        Assert(IpAddressPolicy.ParseDhcpPool("", "", IpAddressPolicy.Subnet(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("255.255.255.0"))) is null, "Invented pool"));
    Check("Large DHCP exclusion does not enumerate millions of addresses", () =>
    {
        var local = IPAddress.Parse("10.1.1.20"); var mask = IPAddress.Parse("255.0.0.0");
        var timer = Stopwatch.StartNew();
        Assert(!IpAddressPolicy.Candidates(local, mask, IpAddressPolicy.Subnet(local, mask), new HashSet<string>()).Any(), "Excluded pool yielded a candidate");
        Assert(timer.Elapsed < TimeSpan.FromSeconds(1), "DHCP pool was enumerated instead of skipped");
    });
    Check("Reject incomplete or reversed or foreign DHCP pool", () =>
    {
        var subnet = IpAddressPolicy.Subnet(IPAddress.Parse("192.168.1.5"), IPAddress.Parse("255.255.255.0"));
        foreach (var pair in new[] { ("192.168.1.2", ""), ("192.168.1.200", "192.168.1.100"), ("192.168.2.1", "192.168.2.200") })
            Reject<ArgumentException>(() => IpAddressPolicy.ParseDhcpPool(pair.Item1, pair.Item2, subnet));
    });
    Check("/23 candidates stay within subnet and are bounded", () =>
    {
        var local = IPAddress.Parse("172.16.3.200"); var mask = IPAddress.Parse("255.255.254.0");
        var subnet = IpAddressPolicy.Subnet(local, mask);
        var result = IpAddressPolicy.Candidates(local, mask, null, new HashSet<string>()).ToList();
        Assert(result.Count <= 512 && result.All(subnet.Contains) && !result.Contains(local), "Invalid /23 candidates");
    });
    Check("/30 contains only host addresses", () =>
    {
        var result = IpAddressPolicy.Candidates(IPAddress.Parse("10.0.0.1"), IPAddress.Parse("255.255.255.252"), null, new HashSet<string>()).ToArray();
        Assert(result.Length == 1 && result[0].ToString() == "10.0.0.2", "Network or broadcast selected");
    });
    Check("Invalid or /31 masks rejected", () =>
    {
        foreach (var mask in new[] { "255.0.255.0", "255.255.255.254", "255.255.255.255", "0.0.0.0" })
            Reject<ArgumentException>(() => IpAddressPolicy.Subnet(IPAddress.Parse("10.0.0.1"), IPAddress.Parse(mask)));
    });
    Check("Elevated helper rejects arbitrary commands and arguments", () =>
    {
        Assert(ElevatedTasks.ValidArguments(["--install-driver", "dd340"]), "DD340 rejected");
        Assert(ElevatedTasks.ValidArguments(["--configure-printer", "HPRT HD200 - ZPL", "Принтер упаковки", "192.168.1.200"]), "Valid printer rejected");
        foreach (var invalid in new[] { new[] { "--install-driver", "C:/bad.exe" },
            ["--install-driver", "dd340", "extra"], ["--command", "Remove-Item"],
            ["--configure-printer", "driver", "arbitrary", "192.168.1.200"],
            ["--configure-printer", "driver", "Принтер упаковки", "127.0.0.1"] })
            Assert(!ElevatedTasks.ValidArguments(invalid), "Unsafe args accepted");
    });
    Check("Development source tree is protected from self-deletion", () =>
    {
        var root = Path.Combine(testRoot, "development");
        Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, ".git"));
        var executable = Path.Combine(root, "ThermoPrinterTool.exe"); File.WriteAllText(executable, "fake executable");
        Assert(!SupportLifetime.ShouldDelete(executable, true), "Development EXE selected");
        Assert(!SupportLifetime.ShouldDelete(executable, false), "Framework-dependent EXE selected");
    });
    foreach (var mismatch in new[] { false, true })
    {
        Check("Cleanup " + (mismatch ? "preserves changed EXE" : "removes only exact EXE after process exit"), () =>
        {
            var executable = Path.Combine(testRoot, "support ' copy-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllText(executable, "fake executable");
            var neighbor = executable + ".log"; File.WriteAllText(neighbor, "keep");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)));
            if (mismatch) File.AppendAllText(executable, "changed");
            var start = new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true };
            using var exited = Process.Start(start)!; exited.WaitForExit();
            using var cleanup = SupportLifetime.StartHiddenPowerShell(SupportLifetime.BuildCleanupScript(exited.Id, executable, hash), captureErrors: true);
            var error = cleanup.StandardError.ReadToEndAsync();
            Assert(cleanup.WaitForExit(15000), "Cleanup timed out");
            Assert(cleanup.ExitCode == 0, "Cleanup exit code " + cleanup.ExitCode + ": " + error.GetAwaiter().GetResult());
            Assert(File.Exists(executable) == mismatch, "Unexpected cleanup result: " + error.GetAwaiter().GetResult());
            Assert(File.Exists(neighbor), "Neighbor file deleted");
        });
    }
    if (args.Length == 1)
    {
        foreach (var package in new[] { TrustedPackages.Dd340Driver, TrustedPackages.Bp41Driver, TrustedPackages.Dd340Utility, TrustedPackages.Bp41Utility })
        {
            Check("Real archive SHA256 and safe extraction: " + package.Id, () =>
            {
                using var stream = File.OpenRead(Path.Combine(args[0], package.Id + ".zip"));
                TrustedPackages.VerifyHash(stream, package.Sha256);
                stream.Position = 0;
                TrustedPackages.ExtractVerifiedArchive(stream, Path.Combine(testRoot, package.Id));
            });
        }
    }
}
finally
{
    Directory.Delete(testRoot, recursive: true);
}
Console.WriteLine($"Checks: {checks}, failed: {failures}");
return failures == 0 ? 0 : 1;
