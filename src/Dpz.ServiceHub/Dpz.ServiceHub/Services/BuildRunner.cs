using System.Diagnostics;
using System.Text;
using Serilog;

namespace Dpz.ServiceHub.Services;

public sealed class BuildRunner
{
    public async Task<int> RunAsync(
        string workingDirectory,
        string executable,
        string arguments,
        Action<string> onOutput,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(workingDirectory);
        }

        var (fileName, resolvedArguments) = ResolveStartCommand(
            executable,
            arguments,
            workingDirectory
        );

        if (IsPowerShellHost(fileName))
        {
            resolvedArguments = WrapPowerShellForUtf8(
                resolvedArguments,
                workingDirectory
            );
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = resolvedArguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };

        startInfo.Environment["TERM"] = "xterm-256color";
        startInfo.Environment["FORCE_COLOR"] = "1";
        startInfo.Environment["CLICOLOR_FORCE"] = "1";
        if (IsPowerShellHost(fileName))
        {
            startInfo.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var exitTcs = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onOutput(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onOutput(e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            try
            {
                exitTcs.TrySetResult(process.ExitCode);
            }
            catch (Exception ex)
            {
                exitTcs.TrySetException(ex);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the build process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to kill the frontend build process.");
            }
        });

        return await exitTcs.Task.WaitAsync(cancellationToken);
    }

    private static (string FileName, string Arguments) ResolveStartCommand(
        string executable,
        string arguments,
        string workingDirectory
    )
    {
        var trimmed = executable.Trim();
        var extension = Path.GetExtension(trimmed);

        if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var scriptPath = ResolvePath(trimmed, workingDirectory);
            var host = FindExecutableInPath("pwsh") ?? "powershell";
            var hostArguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}".Trim();
            return (host, hostArguments);
        }

        if (
            string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
        )
        {
            var scriptPath = ResolvePath(trimmed, workingDirectory);
            var cmdArguments = $"/c \"\"{scriptPath}\" {arguments}\"".Trim();
            return ("cmd.exe", cmdArguments);
        }

        if (
            OperatingSystem.IsWindows()
            && string.IsNullOrEmpty(extension)
            && !Path.IsPathRooted(trimmed)
            && trimmed.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]
            ) < 0
        )
        {
            var resolvedPath = FindExecutableInPath(trimmed);
            if (!string.IsNullOrEmpty(resolvedPath))
            {
                var resolvedExtension = Path.GetExtension(resolvedPath);
                if (
                    string.Equals(resolvedExtension, ".cmd", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        resolvedExtension,
                        ".bat",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    var cmdArguments = $"/c \"\"{resolvedPath}\" {arguments}\"".Trim();
                    return ("cmd.exe", cmdArguments);
                }

                return (resolvedPath, arguments);
            }
        }

        return (trimmed, arguments);
    }

    private static bool IsPowerShellHost(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return string.Equals(name, "pwsh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "powershell", StringComparison.OrdinalIgnoreCase);
    }

    private static string WrapPowerShellForUtf8(string arguments, string workingDirectory)
    {
        const string preamble =
            "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); "
            + "[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false); "
            + "$OutputEncoding = [System.Text.Encoding]::UTF8; "
            + "if ($null -ne $PSStyle) { $PSStyle.OutputRendering = 'Ansi' }; ";

        var setLocation =
            "Set-Location -LiteralPath " + QuotePowerShellSingle(workingDirectory) + "; ";

        if (TryParseFileInvocation(arguments, out var scriptPath, out var scriptArgs))
        {
            var resolvedScript = ResolvePath(scriptPath, workingDirectory);
            resolvedScript = Path.GetFullPath(resolvedScript);
            var invocation = "& " + QuotePowerShellSingle(resolvedScript);
            if (!string.IsNullOrWhiteSpace(scriptArgs))
            {
                invocation += " " + scriptArgs;
            }

            return "-NoProfile -Command \"" + preamble + setLocation + invocation + "\"";
        }

        if (TryParseCommandInvocation(arguments, out var command))
        {
            return "-NoProfile -Command \"" + preamble + setLocation + command + "\"";
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return "-NoProfile -Command \"" + preamble + setLocation + "\"";
        }

        return "-NoProfile -Command \"" + preamble + setLocation + arguments.Trim() + "\"";
    }

    private static bool TryParseFileInvocation(
        string arguments,
        out string scriptPath,
        out string scriptArgs
    )
    {
        scriptPath = string.Empty;
        scriptArgs = string.Empty;
        var tokens = TokenizeArguments(arguments);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (IsSwitch(tokens[i], "-File", "-f"))
            {
                if (i + 1 >= tokens.Count)
                {
                    return false;
                }

                scriptPath = tokens[i + 1];
                scriptArgs = JoinScriptArgs(tokens.Skip(i + 2));
                return !string.IsNullOrWhiteSpace(scriptPath);
            }

            if (ShouldSkipPowerShellSwitch(tokens, ref i))
            {
                continue;
            }
        }

        return false;
    }

    private static bool TryParseCommandInvocation(string arguments, out string command)
    {
        command = string.Empty;
        var tokens = TokenizeArguments(arguments);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (IsSwitch(tokens[i], "-Command", "-c"))
            {
                command = string.Join(" ", tokens.Skip(i + 1));
                return !string.IsNullOrWhiteSpace(command);
            }

            if (ShouldSkipPowerShellSwitch(tokens, ref i))
            {
                continue;
            }
        }

        return false;
    }

    private static bool ShouldSkipPowerShellSwitch(IReadOnlyList<string> tokens, ref int index)
    {
        var token = tokens[index];
        if (IsSwitch(token, "-NoProfile", "-nop", "-noprofile", "-NoLogo", "-NonInteractive"))
        {
            return true;
        }

        if (IsSwitch(token, "-ExecutionPolicy", "-EP"))
        {
            if (index + 1 < tokens.Count)
            {
                index++;
            }

            return true;
        }

        return false;
    }

    private static bool IsSwitch(string token, params string[] names)
    {
        return names.Any(name => string.Equals(token, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string JoinScriptArgs(IEnumerable<string> args)
    {
        return string.Join(
            " ",
            args.Select(arg =>
                arg.IndexOfAny([' ', '\t', '\'', '"']) >= 0 ? QuotePowerShellSingle(arg) : arg
            )
        );
    }

    private static string QuotePowerShellSingle(string value)
    {
        return "'" + value.Replace("'", "''") + "'";
    }

    private static List<string> TokenizeArguments(string arguments)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in arguments)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string ResolvePath(string path, string workingDirectory)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(workingDirectory, path);
    }

    private static string? FindExecutableInPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        var pathExt = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD"
            : string.Empty;
        var extensions = pathExt.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            foreach (var ext in extensions)
            {
                var withExt = candidate + ext;
                if (File.Exists(withExt))
                {
                    return withExt;
                }
            }
        }

        return null;
    }
}
