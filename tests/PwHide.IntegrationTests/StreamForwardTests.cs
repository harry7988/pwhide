using System.Text;
using PwHide.Cli;
using PwHide.Core;
using Xunit;

namespace PwHide.IntegrationTests;

/// <summary>
/// 文件流转发（stdin 管道/重定向 → 子进程、二进制安全、-f - stdin 脚本）测试。
/// 契约：命令模式下 pwhide 的 stdin 原样转发给子进程（字节级，不缓冲不污染）；
/// 输出侧经字节级脱敏泵后原样写出（二进制往返无损）；脱敏规则激活时管道数据同样被扫描；
/// 无口令来源时明确报错而非把管道数据当口令吃掉。
/// </summary>
[Collection("sequential")]
public class StreamForwardTests : IDisposable
{
    private static bool Unix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    private readonly CliFixture F;
    public StreamForwardTests(CliFixture f) => F = f;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PWHIDE_PASSPHRASE_FILE", null);
    }

    [Fact]
    public void StdinPipe_ForwardedToChild()
    {
        if (!Unix) return;
        // 真实管道进程：printf data | pwhide exec -- cat
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"printf hello-stream | dotnet \"{TestBin}\" --home \"{F.Home}\" exec -- /bin/cat");
        using var sh = System.Diagnostics.Process.Start(psi)!;
        var outTask = sh.StandardOutput.ReadToEndAsync();
        var errTask = sh.StandardError.ReadToEndAsync();
        sh.WaitForExit(30_000);
        if (sh.ExitCode != 0)
            Assert.Fail($"pipeline exit={sh.ExitCode}, stderr={errTask.Result}, TestBin exists={File.Exists(TestBin)}");
        Assert.Equal("hello-stream", outTask.Result.TrimEnd('\n'));
    }

    private static string? _testBin;
    /// <summary>CLI dll 路径：从测试输出目录向上找 pwhide.sln 定位仓库根（对 build 布局稳健）。</summary>
    private static string TestBin
    {
        get
        {
            if (_testBin is not null) return _testBin;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "pwhide.sln")))
                dir = dir.Parent;
            Assert.True(dir is not null, "pwhide.sln not found upward from test output");
            _testBin = Path.Combine(dir!.FullName, "src", "PwHide.Cli", "bin", "Debug", "net10.0", "pwhide.dll");
            Assert.True(File.Exists(_testBin), $"CLI dll missing: {_testBin}");
            return _testBin;
        }
    }

    [Fact]
    public void StdinPipe_BinaryRoundtrip()
    {
        if (!Unix) return;
        var bin = Path.Combine(Path.GetTempPath(), "pwhide-bin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rnd = new Random(42);
            var data = new byte[51_200];
            rnd.NextBytes(data);
            File.WriteAllBytes(bin, data);
            // 二进制 stdin 经 pwhide 转发必须字节无损
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"dotnet \"{TestBin}\" --home \"{F.Home}\" exec -- /bin/cat < '{bin}' | cmp - '{bin}'");
            using var sh = System.Diagnostics.Process.Start(psi)!;
            sh.WaitForExit(30_000);
            Assert.Equal(0, sh.ExitCode);   // cmp 相等 = 无损
        }
        finally { try { File.Delete(bin); } catch { } }
    }

    [Fact]
    public void BinaryOutput_Roundtrip()
    {
        if (!Unix) return;
        var bin = Path.Combine(Path.GetTempPath(), "pwhide-bout-" + Guid.NewGuid().ToString("N"));
        var copy = bin + ".copy";
        try
        {
            var rnd = new Random(7);
            var data = new byte[102_400];   // > 单块 8192，跨多块
            rnd.NextBytes(data);
            File.WriteAllBytes(bin, data);
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"dotnet \"{TestBin}\" --home \"{F.Home}\" exec -- /bin/cat '{bin}' > '{copy}'");
            using var sh = System.Diagnostics.Process.Start(psi)!;
            sh.WaitForExit(30_000);
            Assert.Equal(0, sh.ExitCode);
            Assert.Equal(data, File.ReadAllBytes(copy));
        }
        finally { try { File.Delete(bin); File.Delete(copy); } catch { } }
    }

    [Fact]
    public void ScriptFromStdin_DashF_Works()
    {
        if (!Unix) return;
        using var stdout2 = new MemoryStream();
        using var stderr2 = new MemoryStream();
        Environment.SetEnvironmentVariable("PWHIDE_PASSPHRASE", "init-pass-123");
        try
        {
            var exit2 = CliRunner.Run(["--home", F.Home, "exec", "--allow-echo", "-f", "-", "--shell", "sh"],
                stdout2, stderr2, new StringReader("echo stdin-script-ok; echo pw={{db}}"));
            var text = new UTF8Encoding(false).GetString(stdout2.ToArray());
            Assert.Equal(0, exit2);
            Assert.Contains("stdin-script-ok", text);
            Assert.Contains("pw={{db}}", text);   // 脱敏照常
            Assert.DoesNotContain(CliFixture.DbPassword, text);
        }
        finally { Environment.SetEnvironmentVariable("PWHIDE_PASSPHRASE", null); }
    }

    [Fact]
    public void PipedSecret_RedactedWhenRulesActive()
    {
        if (!Unix) return;
        // 真实管道：管道数据里恰好含密码，且命令引用 {{db}}（规则激活）→ 输出被字节级脱敏
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["PWHIDE_PASSPHRASE"] = "init-pass-123";
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"printf 'prefix {CliFixture.DbPassword} suffix' | dotnet \"{TestBin}\" --home \"{F.Home}\" exec --allow-echo -- /bin/sh -c 'cat; printf \" %s\" {{{{db}}}}'");
        using var sh = System.Diagnostics.Process.Start(psi)!;
        var outTask = sh.StandardOutput.ReadToEndAsync();
        sh.WaitForExit(30_000);
        var text = outTask.Result;
        Assert.True(sh.ExitCode == 0, $"exit={sh.ExitCode}");
        Assert.DoesNotContain(CliFixture.DbPassword, text);
        Assert.Contains("prefix {{db}} suffix", text);
    }

    [Fact]
    public void PipeWithSecret_NoPassphraseSource_FailsCleanly()
    {
        if (!Unix) return;
        // 真实子进程（stdin=EOF 管道）：无口令来源时明确报错（不把管道数据当口令吃掉）。
        // 不用进程内 CliRunner——它无法控制 Console.IsInputRedirected，测试会依赖宿主 stdin 状态。
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment.Remove("PWHIDE_PASSPHRASE");
        psi.Environment["PWHIDE_NO_KEYCHAIN"] = "1";
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"printf piped-data | dotnet \"{TestBin}\" --home \"{F.Home}\" exec --allow-echo -- /bin/true {{{{db}}}} < /dev/null");
        using var sh = System.Diagnostics.Process.Start(psi)!;
        var errTask = sh.StandardError.ReadToEndAsync();
        sh.WaitForExit(30_000);
        var err = errTask.Result;
        Assert.Equal(3, sh.ExitCode);
        Assert.Contains("keychain set", err);
        Assert.DoesNotContain("piped-data", err);
    }

    [Fact]
    public void PassphraseFile_PipedStdin_StillWorks()
    {
        if (!Unix) return;
        // 口令经文件（无 env）：管道照常转发，解锁不受影响
        var pwFile = Path.Combine(Path.GetTempPath(), "pwhide-pwf-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(pwFile, "init-pass-123\n");
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true };
            psi.Environment.Remove("PWHIDE_PASSPHRASE");
            psi.Environment["PWHIDE_PASSPHRASE_FILE"] = pwFile;
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"printf stream-ok | dotnet \"{TestBin}\" --home \"{F.Home}\" exec --allow-echo -- /bin/sh -c 'cat; printf \" %s\" {{{{db}}}}'");
            using var sh = System.Diagnostics.Process.Start(psi)!;
            var outTask = sh.StandardOutput.ReadToEndAsync();
            sh.WaitForExit(30_000);
            Assert.True(sh.ExitCode == 0, $"exit={sh.ExitCode}, err={sh.StandardError.ReadToEnd()}");
            Assert.Contains("stream-ok {{db}}", outTask.Result);
        }
        finally { try { File.Delete(pwFile); } catch { } }
    }
}
