using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CycloneDDS.Compiler.Common
{
    public class IdlcRunner
    {
        // Platform detection
        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static string IdlcExecutableName => IsWindows ? "idlc.exe" : "idlc";
        private static string RuntimeIdentifier => IsWindows ? "win-x64" : "linux-x64";

        public string? IdlcPathOverride { get; set; }
        public string? IdlcExtraArgs { get; set; }

        public string FindIdlc()
        {
            if (!string.IsNullOrEmpty(IdlcPathOverride))
            {
                if (File.Exists(IdlcPathOverride)) return IdlcPathOverride;
                throw new FileNotFoundException($"{IdlcExecutableName} not found at override path: {IdlcPathOverride}");
            }

            // Check current directory (where DLLs are)
            string currentDir = AppDomain.CurrentDomain.BaseDirectory;
            string localIdlc = Path.Combine(currentDir, IdlcExecutableName);
            if (File.Exists(localIdlc)) return localIdlc;

            // Check NuGet package location relative to tools/ (tools/ -> ../runtimes/{rid}/native/)
            try
            {
               string nugetNativePath = Path.Combine(currentDir, "..", "runtimes", RuntimeIdentifier, "native", IdlcExecutableName);
               if (File.Exists(nugetNativePath)) return Path.GetFullPath(nugetNativePath);
            }
            catch { }

            // Also try tools/ directory (for NuGet package layout)
            try
            {
                string toolsPath = Path.Combine(currentDir, "..", "..", RuntimeIdentifier, "native", IdlcExecutableName);
                if (File.Exists(toolsPath)) return Path.GetFullPath(toolsPath);
            }
            catch { }

            // DEV: Check workspace location (for tests/dev)
            // Iterate up 6 levels looking for cyclonedds/install/bin/idlc OR cyclone-compiled/bin/idlc
            var searchDir = new DirectoryInfo(currentDir);
            for (int i = 0; i < 6; i++)
            {
                if (searchDir == null) break;

                // Check cyclonedds install directory
                string checkPath = Path.Combine(searchDir.FullName, "cyclonedds", "install", "bin", IdlcExecutableName);
                if (File.Exists(checkPath)) return checkPath;

                // Check cyclone-compiled directory
                string repoPath = Path.Combine(searchDir.FullName, "cyclone-compiled", "bin", IdlcExecutableName);
                if (File.Exists(repoPath)) return repoPath;

                // Check artifacts/native/{rid}/ directory
                repoPath = Path.Combine(searchDir.FullName, "artifacts", "native", RuntimeIdentifier, IdlcExecutableName);
                if (File.Exists(repoPath)) return repoPath;

                searchDir = searchDir.Parent;
            }

            // Check environment variable
            string? cycloneHome = Environment.GetEnvironmentVariable("CYCLONEDDS_HOME");
            if (!string.IsNullOrEmpty(cycloneHome))
            {
                string path = Path.Combine(cycloneHome, "bin", IdlcExecutableName);
                if (File.Exists(path))
                    return path;

                // Try without bin?
                path = Path.Combine(cycloneHome, IdlcExecutableName);
                if (File.Exists(path))
                    return path;
            }

            // Check PATH
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null)
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string path = Path.Combine(dir, IdlcExecutableName);
                        if (File.Exists(path))
                            return path;
                    }
                    catch { /* Ignore invalid paths in PATH */ }
                }
            }

            throw new FileNotFoundException($"{IdlcExecutableName} not found. Set CYCLONEDDS_HOME or add to PATH.");
        }

        public IdlcResult RunIdlc(string idlFilePath, string outputDir, string? includePath = null)
        {
            string idlcPath = FindIdlc();

            // Get the directory where idlc is located (for LD_LIBRARY_PATH on Linux)
            string idlcDir = Path.GetDirectoryName(idlcPath) ?? "";

            // Ensure output directory exists
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = idlcPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set LD_LIBRARY_PATH for Linux so idlc can find its shared libraries
            if (!IsWindows)
            {
                string currentLdLibPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "";
                string newLdLibPath = string.IsNullOrEmpty(currentLdLibPath)
                    ? idlcDir
                    : $"{idlcDir}:{currentLdLibPath}";
                startInfo.Environment["LD_LIBRARY_PATH"] = newLdLibPath;
            }
            
            if (!string.IsNullOrWhiteSpace(IdlcExtraArgs))
            {
                // Simple split by whitespace is sufficient for most compiler flags like "-Werror"
                // But if they have spaces inside quotes, this simple split would break. 
                // System.CommandLine parsing is better handled by caller, so we assume caller provides simple args
                foreach(var arg in IdlcExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }

            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputDir);
            
            if (!string.IsNullOrEmpty(includePath))
            {
                startInfo.ArgumentList.Add("-I");
                startInfo.ArgumentList.Add(includePath);
            }
            
            startInfo.ArgumentList.Add(idlFilePath);
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new Exception("Failed to start idlc process.");
            }
            
            var stdoutBuilder = new System.Text.StringBuilder();
            var stderrBuilder = new System.Text.StringBuilder();

            // 1. Subscribe to the events
            process.OutputDataReceived += (sender, e) => 
            {
                if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) => 
            {
                if (e.Data != null) stderrBuilder.AppendLine(e.Data);
            };

            // 2. Begin reading asynchronously
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 3. Wait for the process to finish
            process.WaitForExit();

            // 4. Extract the final strings
            string stdout = stdoutBuilder.ToString();
            string stderr = stderrBuilder.ToString();
            
            return new IdlcResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                GeneratedFiles = FindGeneratedFiles(outputDir, idlFilePath)
            };
        }

        public string GetArguments(string idlFilePath, string outputDir, string? includePath)
        {
            var args = $"-l json -o \"{outputDir}\"";
            if (!string.IsNullOrEmpty(includePath))
            {
                args += $" -I \"{includePath}\"";
            }
            args += $" \"{idlFilePath}\"";
            return args;
        }
        
        private string[] FindGeneratedFiles(string outputDir, string idlFile)
        {
            // idlc -l json generates: <basename>.json
            string baseName = Path.GetFileNameWithoutExtension(idlFile);
            var jsonFile = Path.Combine(outputDir, baseName + ".json");
            
            var files = new System.Collections.Generic.List<string>();
            if (File.Exists(jsonFile)) files.Add(jsonFile);
            
            return files.ToArray();
        }
    }
}
