param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    return (Resolve-Path (Join-Path $scriptDir "..")).Path
}

function Resolve-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($path -and (Test-Path $path)) {
            return $path
        }
    }

    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "MSBuild.exe not found. Run this on the Windows VM with Visual Studio installed."
}

function Resolve-CSharpCompiler {
    $candidates = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "csc.exe not found. .NET Framework compiler is required for the smoke test harness."
}

$repoRoot = Resolve-RepoRoot
$solution = Join-Path $repoRoot "ZimpleTesting.sln"
if (!(Test-Path $solution)) {
    $solution = Join-Path $repoRoot "Zimple.sln"
}
if (!(Test-Path $solution)) {
    throw "Solution file not found."
}

$msbuild = Resolve-MSBuild
$csc = Resolve-CSharpCompiler

Write-Host "Building $solution ($Configuration)..."
& $msbuild $solution /t:Rebuild /p:Configuration=$Configuration /p:Platform="Any CPU" /m
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

$outputExe = Join-Path $repoRoot "Zimple\bin\$Configuration\ZimpleTesting.exe"
if (!(Test-Path $outputExe)) {
    $outputExe = Join-Path $repoRoot "Zimple\bin\$Configuration\Zimple.exe"
}
if (!(Test-Path $outputExe)) {
    throw "Built executable not found under Zimple\bin\$Configuration."
}

$libDir = Join-Path $repoRoot "Zimple\Lib\net462"
$outputDir = Split-Path -Parent $outputExe
Get-ChildItem $libDir -Filter *.dll | ForEach-Object {
    Copy-Item $_.FullName -Destination $outputDir -Force
}

$tmp = Join-Path $repoRoot "tools\.exception-smoke"
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$source = Join-Path $tmp "ExceptionSmokeHarness.cs"
$harnessExe = Join-Path $tmp "ExceptionSmokeHarness.exe"

@'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

public static class ExceptionSmokeHarness
{
    private static string assemblyDir;
    private static string libDir;

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: ExceptionSmokeHarness <ZimpleTesting.exe> <libDir>");
            return 2;
        }

        assemblyDir = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        libDir = Path.GetFullPath(args[1]);

        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

        try
        {
            Assembly app = Assembly.LoadFrom(args[0]);
            TestLibPlcTagExceptionIsWrapped(app);
            TestNonPlcExceptionIsNotHidden(app);
            TestCrashGuardWritesLog(app);

            Console.WriteLine("PASS: exception resilience smoke tests completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string outputCandidate = Path.Combine(assemblyDir, name);
        if (File.Exists(outputCandidate))
            return Assembly.LoadFrom(outputCandidate);

        string libCandidate = Path.Combine(libDir, name);
        if (File.Exists(libCandidate))
            return Assembly.LoadFrom(libCandidate);

        return null;
    }

    private static void TestLibPlcTagExceptionIsWrapped(Assembly app)
    {
        Type storeType = app.GetType("Zimple.PlcTagStore", true);
        object store = Activator.CreateInstance(storeType, "127.0.0.1");
        MethodInfo execute = GetExecutePlcAccess(storeType);

        Type plcExceptionType = Assembly.LoadFrom(Path.Combine(libDir, "libplctag.dll"))
            .GetType("libplctag.LibPlcTagException", true);
        Exception syntheticPlcFailure = (Exception)FormatterServices.GetUninitializedObject(plcExceptionType);

        Func<int> action = delegate
        {
            throw syntheticPlcFailure;
        };

        Exception inner = InvokeExpectingFailure(execute, store, "write", null, action);
        Assert(inner.GetType().FullName == "Zimple.PlcCommunicationException",
            "LibPlcTagException should be wrapped as Zimple.PlcCommunicationException.");
        Assert(inner.InnerException != null && inner.InnerException.GetType() == plcExceptionType,
            "Wrapped exception should preserve original LibPlcTagException.");
        Assert(inner.Message.Contains("write"), "Wrapped message should include operation.");
        Assert(inner.Message.Contains("127.0.0.1"), "Wrapped message should include PLC IP.");
        Assert(inner.Message.Contains("<null>"), "Wrapped message should include tag fallback name.");

        Console.WriteLine("PASS: synthetic libplctag failure is wrapped with PLC context.");
    }

    private static void TestNonPlcExceptionIsNotHidden(Assembly app)
    {
        Type storeType = app.GetType("Zimple.PlcTagStore", true);
        object store = Activator.CreateInstance(storeType, "127.0.0.1");
        MethodInfo execute = GetExecutePlcAccess(storeType);

        Func<int> action = delegate
        {
            throw new InvalidOperationException("synthetic non PLC failure");
        };

        Exception inner = InvokeExpectingFailure(execute, store, "read", null, action);
        Assert(inner is InvalidOperationException,
            "Non-PLC exceptions should not be converted to PlcCommunicationException.");
        Assert(inner.Message.Contains("synthetic non PLC failure"),
            "Original non-PLC exception message should be preserved.");

        Console.WriteLine("PASS: non-PLC exceptions are not hidden by PLC wrapper.");
    }

    private static void TestCrashGuardWritesLog(Assembly app)
    {
        Type appType = app.GetType("Zimple.App", true);
        MethodInfo writeLog = appType.GetMethod(
            "WriteCrashGuardLog",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert(writeLog != null, "CrashGuard log method not found.");

        string marker = "synthetic-crash-guard-" + Guid.NewGuid().ToString("N");
        writeLog.Invoke(null, new object[] { "SmokeTest", new Exception(marker) });

        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZimpleTesting",
            "Logs",
            "CrashGuard.log");

        Assert(File.Exists(path), "CrashGuard.log was not created.");
        string text = File.ReadAllText(path);
        Assert(text.Contains("SmokeTest"), "CrashGuard.log does not include source marker.");
        Assert(text.Contains(marker), "CrashGuard.log does not include exception marker.");

        Console.WriteLine("PASS: crash guard writes diagnostic log.");
    }

    private static MethodInfo GetExecutePlcAccess(Type storeType)
    {
        MethodInfo method = storeType
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(m => m.Name == "ExecutePlcAccess" && m.IsGenericMethodDefinition);
        return method.MakeGenericMethod(typeof(int));
    }

    private static Exception InvokeExpectingFailure(
        MethodInfo execute,
        object store,
        string operation,
        object tag,
        Func<int> action)
    {
        try
        {
            execute.Invoke(store, new object[] { operation, tag, action });
            throw new Exception("Expected invocation to fail.");
        }
        catch (TargetInvocationException ex)
        {
            return ex.InnerException;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
'@ | Set-Content -Path $source -Encoding UTF8

Write-Host "Compiling exception smoke harness..."
& $csc /nologo /target:exe /out:$harnessExe $source
if ($LASTEXITCODE -ne 0) {
    throw "Harness compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Running exception smoke harness..."
& $harnessExe $outputExe $libDir
if ($LASTEXITCODE -ne 0) {
    throw "Exception smoke harness failed with exit code $LASTEXITCODE."
}

Write-Host "All exception resilience smoke tests passed."
