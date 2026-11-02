using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Branvik.EmulatorUpdater
{
    public static class UpdateInstaller
    {
        static UpdateInstaller()
        {
            // A plugin cannot add binding redirects to Playnite's executable config.
            // Resolve only our archive library's bundled dependencies, on demand.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "SharpCompress", "System.Memory", "System.Buffers", "System.Runtime.CompilerServices.Unsafe",
                "System.Threading.Tasks.Extensions", "Microsoft.Bcl.AsyncInterfaces", "System.Text.Encoding.CodePages", "System.Numerics.Vectors"
            };
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var requested = new System.Reflection.AssemblyName(args.Name);
                if (!names.Contains(requested.Name)) return null;
                var requester = args.RequestingAssembly;
                if (requester != null && requester != typeof(UpdateInstaller).Assembly && !names.Contains(requester.GetName().Name)) return null;
                var path = Path.Combine(Path.GetDirectoryName(typeof(UpdateInstaller).Assembly.Location), requested.Name + ".dll");
                return File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
            };
        }
        public static bool Supported(string repository) =>
            string.Equals(repository, "PCSX2/pcsx2", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                repository,
                "RPCS3/rpcs3-binaries-win",
                StringComparison.OrdinalIgnoreCase
            );

        private static string Executable(string repository)
        {
            if (!Supported(repository))
                throw new InvalidOperationException("Unsupported update source.");
            return string.Equals(repository, "PCSX2/pcsx2", StringComparison.OrdinalIgnoreCase)
                ? "pcsx2-qt.exe"
                : "rpcs3.exe";
        }

        public static UpdateChecker.Asset SelectAsset(
            UpdateChecker.Release release,
            string repository = "PCSX2/pcsx2"
        )
        {
            var executable = Executable(repository);
            var pattern =
                executable == "rpcs3.exe"
                    ? @"^rpcs3-v[0-9].+_win64(?:_msvc)?\.(7z|zip)$"
                    : @"^pcsx2-.+-windows-x64-Qt\.(7z|zip)$";
            var assets = (release.assets ?? new List<UpdateChecker.Asset>())
                .Where(a => Regex.IsMatch(a.name ?? "", pattern, RegexOptions.IgnoreCase))
                .ToList();
            if (assets.Count != 1)
                throw new InvalidOperationException(
                    "Could not identify a unique Windows x64 archive for " + repository + "."
                );
            return assets[0];
        }

        public static string ValidateFolder(string folder, string repository = "PCSX2/pcsx2", string executableName = null)
        {
            var executable = executableName ?? Executable(repository);
            ValidateEntry(executable);
            if (Path.GetFileName(executable) != executable) throw new IOException("Select an executable in the emulator root folder.");
            if (
                string.IsNullOrWhiteSpace(folder)
                || !Path.IsPathRooted(folder)
                || folder.Contains("{")
            )
                throw new InvalidOperationException(
                    "An absolute emulator folder path without variables is required."
                );
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, executable)))
                throw new InvalidOperationException(
                    "The selected folder does not contain " + executable + "."
                );
            RejectLinks(root);
            if (
                Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)).Length != 0
            )
                throw new InvalidOperationException("Close " + executable + " before updating.");
            return root;
        }

        private static void RejectLinks(string path)
        {
            for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked folders are not supported: " + current.FullName);
        }

        public static bool Protected(string relative)
        {
            var parts = relative.Replace('\\', '/').Split('/');
            var reserved = new[]
            {
                "bios",
                "memcards",
                "sstates",
                "inis",
                "settings",
                "games",
                "roms",
                "snaps",
                "logs",
                "cache",
                "cheats",
                "cheats_ws",
                "textures",
                "inputprofiles",
                "gamesettings",
                "covers",
            };
            var rpcs3Data = new[]
            {
                "config",
                "GuiConfigs",
                "patches",
                "savestates",
                "captures",
                "screenshots",
                "input_configs",
                "custom_configs",
                "Icons",
            };
            return parts.Any(p =>
                    reserved.Contains(p, StringComparer.OrdinalIgnoreCase)
                    || rpcs3Data.Contains(p, StringComparer.OrdinalIgnoreCase)
                    || p.StartsWith("dev_", StringComparison.OrdinalIgnoreCase)
                )
                || new[]
                {
                    ".ini",
                    ".json",
                    ".yaml",
                    ".yml",
                    ".toml",
                    ".cfg",
                    ".p2m",
                    ".ps2",
                }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase)
                || string.Equals(
                    Path.GetFileName(relative),
                    "portable.txt",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        public static void ValidateEntry(string name)
        {
            if (
                string.IsNullOrWhiteSpace(name)
                || Path.IsPathRooted(name)
                || name.Contains(":")
                || name.Split('/', '\\')
                    .Any(p =>
                        p == ".."
                        || p == "."
                        || p.EndsWith(" ")
                        || p.EndsWith(".")
                        || Regex.IsMatch(
                            p,
                            @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)",
                            RegexOptions.IgnoreCase
                        )
                    )
            )
                throw new IOException("Invalid archive path: " + name);
        }

        public static void Extract(string archivePath, string stage, CancellationToken token)
        {
            ExtractCore(archivePath, stage, token);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ExtractCore(string archivePath, string stage, CancellationToken token)
        {
            Directory.CreateDirectory(stage);
            var prefix = Path.GetFullPath(stage).TrimEnd('\\') + "\\";
            long total = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = ArchiveFactory.OpenArchive(archivePath))
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                var name = (entry.Key ?? "").Replace('/', '\\').TrimEnd('\\');
                ValidateEntry(name);
                if (entry.IsEncrypted || !string.IsNullOrEmpty(entry.LinkTarget)) throw new IOException("Encrypted archives and links are not supported.");
                var path = Path.GetFullPath(Path.Combine(stage, name));
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !names.Add(path)) throw new IOException("Invalid or duplicate archive path.");
                if (entry.IsDirectory) { Directory.CreateDirectory(path); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = entry.OpenEntryStream())
                using (var output = File.Create(path))
                {
                    var buffer = new byte[81920]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        total += count;
                        if (total > 2L * 1024 * 1024 * 1024) throw new IOException("The archive is too large.");
                        output.Write(buffer, 0, count);
                    }
                }
            }
        }
        public static async Task<string> Install(
            string folder,
            UpdateChecker.Asset asset,
            CancellationToken token,
            string repository = "PCSX2/pcsx2", string executableName = null
        )
        {
            var executable = executableName ?? Executable(repository);
            var root = ValidateFolder(folder, repository, executable);
            var uri = new Uri(asset.browser_download_url);
            if (
                uri.Scheme != "https"
                || uri.Host != "github.com"
                || !uri.AbsolutePath.StartsWith(
                    "/" + repository + "/releases/download/",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new IOException("Invalid download source.");
            if (asset.size <= 0 || asset.size > 512L * 1024 * 1024)
                throw new IOException("Invalid archive size.");
            var work = Path.Combine(
                Path.GetTempPath(),
                "BranvikUpdater",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(work);
            try
            {
                var archive = Path.Combine(work, "package" + Path.GetExtension(asset.name));
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BranvikEmulatorUpdater/0.1.0");
                    using (
                        var response = await client.GetAsync(
                            uri,
                            HttpCompletionOption.ResponseHeadersRead,
                            token
                        )
                    )
                    {
                        response.EnsureSuccessStatusCode();
                        using (var input = await response.Content.ReadAsStreamAsync())
                        using (var output = File.Create(archive))
                        {
                            var buffer = new byte[81920];
                            long total = 0;
                            int count;
                            while (
                                (count = await input.ReadAsync(buffer, 0, buffer.Length, token))
                                != 0
                            )
                            {
                                total += count;
                                if (total > asset.size)
                                    throw new IOException("Download size mismatch.");
                                await output.WriteAsync(buffer, 0, count, token);
                            }
                            if (total != asset.size)
                                throw new IOException("The archive download is incomplete.");
                        }
                    }
                }
                if (!string.IsNullOrEmpty(asset.digest))
                {
                    using (var sha = SHA256.Create())
                    using (var file = File.OpenRead(archive))
                        if (
                            !string.Equals(
                                asset.digest,
                                "sha256:"
                                    + BitConverter.ToString(sha.ComputeHash(file)).Replace("-", ""),
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            throw new IOException("Archive checksum mismatch.");
                }
                return await Task.Run(
                    () =>
                    {
                        var stage = Path.Combine(work, "stage");
                        Extract(archive, stage, token);
                        // Accept a single wrapper folder used by many portable distributions.
                        while (!File.Exists(Path.Combine(stage, executable)) && Directory.GetFiles(stage).Length == 0 && Directory.GetDirectories(stage).Length == 1)
                            stage = Directory.GetDirectories(stage)[0];
                        if (!File.Exists(Path.Combine(stage, executable)))
                            throw new IOException(
                                "The archive does not contain " + executable + " at its root."
                            );
                        ValidateFolder(root, repository, executable);
                        return Apply(stage, root, !Supported(repository));
                    },
                    token
                );
            }
            finally
            {
                try
                {
                    Directory.Delete(work, true);
                }
                catch
                { /* Temp cleanup must not mask the installation result. */
                }
            }
        }

        public static string Apply(string stage, string root, bool conservative = false)
        {
            var files = new List<string>();
            Collect(stage, stage, files);
            files = files.Where(f => !Protected(f) && (!conservative || !File.Exists(Path.Combine(root, f)) ||
                new[] { ".exe", ".dll", ".pdb" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))).ToList();
            foreach (var relative in files)
            {
                ValidateEntry(relative);
                var target = Path.Combine(root, relative);
                RejectLinks(Path.GetDirectoryName(target));
                if (
                    File.Exists(target)
                    && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0
                )
                    throw new IOException("Linked file: " + target);
            }
            var backup = Path.Combine(
                Path.GetDirectoryName(root),
                "BranvikBackups",
                Guid.NewGuid().ToString("N")
            );
            RejectLinks(backup);
            Directory.CreateDirectory(backup);
            var existing = files.Where(f => File.Exists(Path.Combine(root, f))).ToList();
            foreach (var file in existing)
            {
                var dest = Path.Combine(backup, "files", file);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(Path.Combine(root, file), dest);
            }
            File.WriteAllLines(Path.Combine(backup, "added-files.txt"), files.Except(existing));
            File.WriteAllText(Path.Combine(backup, "installation.txt"), root);
            var changed = new List<string>();
            try
            {
                foreach (var file in files)
                {
                    var target = Path.Combine(root, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    changed.Add(file);
                    File.Copy(Path.Combine(stage, file), target, true);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                foreach (var file in changed.AsEnumerable().Reverse())
                    try
                    {
                        if (existing.Contains(file))
                            File.Copy(
                                Path.Combine(backup, "files", file),
                                Path.Combine(root, file),
                                true
                            );
                        else
                            File.Delete(Path.Combine(root, file));
                    }
                    catch (Exception rollback)
                    {
                        errors.Add(rollback);
                    }
                throw new IOException(
                    (
                        errors.Count == 1
                            ? "Changes have been rolled back. "
                            : "Rollback is incomplete! "
                    )
                        + "Backup: "
                        + backup,
                    new AggregateException(errors)
                );
            }
            return backup;
        }

        private static void Collect(string directory, string root, List<string> files)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Links inside archives are not supported.");
                if ((attributes & FileAttributes.Directory) != 0)
                    Collect(path, root, files);
                else
                    files.Add(path.Substring(root.Length + 1));
            }
        }
    }
}
